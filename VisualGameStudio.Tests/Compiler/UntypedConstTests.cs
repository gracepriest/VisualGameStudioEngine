using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.IR;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #123, D1 — `Const X = expr` with no `As` clause takes the type of its constant expression.
//
//  ⭐ THE ORACLE IS vbc, NOT BASICLANG. Every expected type below is what vbc's `TypeName(X)` prints for the
//  same constant, declared at each of the three levels (module, local in a Sub, class); vbc answers identically at all
//  three (S/t123/tw/d1gen.py, which builds the VB program from the rows of `Rows` and runs it). The rows are the
//  literal kinds plus an expression over other constants, so both halves of D1's rule ("the literal's type" and "the
//  expression's type") are pinned, and the Integer-versus-Double split that `/` makes is in there (EDIV is Double,
//  EIDIV is Integer).
//
//  ⛔ WHAT A TYPE TABLE CANNOT SEE, AND WHERE IT IS SEEN INSTEAD. The sample programs build with an untyped Const
//  forced to Integer (mutant M1int), so the sample tests hold nothing about D1. The execution probes do: c1mod, c2loc,
//  c3cls, c4init, c5big in UntypedConstAndConditionalExecutionTests run each level on four backends, where a Double
//  constant that became an Integer prints 2 for `Dim v = K : v = 2.5`.
//
//  ⛔ ROWS THAT THIS FIXTURE DELIBERATELY DOES NOT LOWER (no expectation; each is measured on the BEFORE build too, with
//  a TYPED constant, so none of them is D1's):
//    MODULE level, a Const over an Integer/Double/Single/Long MIX — `Const EDBL = I * 1.5`, ELNG `I + 1L`, ESNG `SF * 2`,
//      ESD `SF * 2.0`, ELL `L * 2`, EDS `D * SF` (and `Const X As Double = 800 * 1.5`, two literals): the IR builder
//      refuses them with "cannot be computed at compile time" because the constant folder folds a binary operation only
//      when both operands already have ONE type. The analyzer types them right (they ARE in the type table). No task.
//    CLASS level, a Const over another class Const — `Public Const EDIV = I / 2` (typed too): the same message, for the
//      field. So the class-level LOWERING rows use literal initializers only. No task.
//    MODULE level `Const ELE = D <= 3` (D = 3.14): ⛔ it is accepted and FOLDS WRONG (`true`, vbc's answer is False) —
//      IROptimizer.TryFoldCompare has no arm for a Double/Integer pair, so `<`/`>` report false and `<=`/`>=` true. The
//      literal form `Const E As Boolean = 3.14 <= 3` is wrong on the BEFORE build too; what #123 changed is that
//      SubstituteFoldedConstGlobals now lets a Const operand reach that folder, so the Const form went from a refusal
//      to a silent wrong answer on every backend. ELE is in the type table (Boolean, right) and in NO lowering list.
//
//  ⚠ vbc refuses `Const X = "v" & I` (BC30060) and BasicLang accepts it; vbc accepts `Const N = Nothing` (Object) and
//  BasicLang refuses it (see ConstNothing_IsRefused). Neither is D1's rule; ECAT uses two string literals.
// ================================================================================================

public enum ConstLevel { Module, Local, Class }

/// <summary>The D1 rows and the programs built from them.</summary>
internal static class UntypedConstProbes
{
    /// <summary>(name, initializer, vbc's TypeName). Rows depend only on EARLIER rows.</summary>
    internal static readonly (string Name, string Init, string Vb)[] Rows =
    {
        ("I", "800", "Integer"),
        ("L", "800L", "Long"),
        ("BL", "3000000000", "Long"),
        ("D", "3.14", "Double"),
        ("SF", "1.5F", "Single"),
        ("S", "\"s\"", "String"),
        ("B", "True", "Boolean"),
        ("C", "\"a\"c", "Char"),
        ("HX", "&HFF", "Integer"),
        ("NG", "-5", "Integer"),
        ("NGD", "-2.5", "Double"),
        ("EDIV", "I / 2", "Double"),
        ("EMUL", "I * 2", "Integer"),
        ("EIDIV", "I \\ 2", "Integer"),
        ("EMOD", "I Mod 3", "Integer"),
        ("EDBL", "I * 1.5", "Double"),
        ("ECMP", "I > 3", "Boolean"),
        ("ECAT", "\"v\" & \"w\"", "String"),
        ("ELNG", "I + 1L", "Long"),
        ("ESNG", "SF * 2", "Single"),
        ("ESD", "SF * 2.0", "Double"),
        ("ENEG", "-I", "Integer"),
        ("EPAR", "(I + 1) * 2", "Integer"),
        ("EAND", "B And True", "Boolean"),
        ("ENOT", "Not B", "Boolean"),
        ("EREF", "EMUL", "Integer"),
        ("EREF2", "EDIV", "Double"),
        ("ELL", "L * 2", "Long"),
        ("EDS", "D * SF", "Double"),
        ("MAXI", "2147483647", "Integer"),
        ("NMIN", "-2147483648", "Long"),
        ("ELE", "D <= 3", "Boolean"),
        ("ECHR", "C", "Char"),
    };

    /// <summary>Module-level rows the IR builder refuses (a mix of numeric types; see the header). Not D1's.</summary>
    internal static readonly HashSet<string> ModuleFoldRefused = new() { "EDBL", "ELNG", "ESNG", "ESD", "ELL", "EDS" };

    /// <summary>Module-level rows that lower to a WRONG value (see the header). Never asserted.</summary>
    internal static readonly HashSet<string> WrongFold = new() { "ELE" };

    /// <summary>Rows with no other constant in the initializer: the only ones a class-level Const can lower.</summary>
    internal static readonly HashSet<string> Literal = new()
    {
        "I", "L", "BL", "D", "SF", "S", "B", "C", "HX", "NG", "NGD", "MAXI", "NMIN",
    };

    internal static string CSharpType(string vb) => vb switch
    {
        "Integer" => "int",
        "Long" => "long",
        "Double" => "double",
        "Single" => "float",
        "String" => "string",
        "Boolean" => "bool",
        "Char" => "char",
        _ => throw new ArgumentException(vb),
    };

    internal static string Program(ConstLevel level, Func<string, bool> keep)
    {
        var declarations = Rows.Where(r => keep(r.Name)).Select(r => $"Const {r.Name} = {r.Init}").ToList();
        return level switch
        {
            ConstLevel.Module => string.Join("\n", declarations) + "\nSub Main()\nEnd Sub\n",
            ConstLevel.Local => "Sub Main()\n" + string.Join("\n", declarations.Select(d => "    " + d)) + "\nEnd Sub\n",
            ConstLevel.Class => "Class K\n" + string.Join("\n", declarations.Select(d => "    Public " + d)) + "\nEnd Class\nSub Main()\nEnd Sub\n",
            _ => throw new ArgumentException(level.ToString()),
        };
    }

    /// <summary>The program of EVERY row at a level — what the vbc oracle was asked about.</summary>
    internal static string Whole(ConstLevel level) => Program(level, _ => true);
}

/// <summary>
/// D1 (#123): an untyped <c>Const</c> is typed by its constant expression, at local, module and class level, the way vbc
/// types it; the fit check (BC30439) and the no-value check still apply; <c>Const X = Nothing</c> is refused.
/// Front end and IR only — the same constants RUN in <c>UntypedConstAndConditionalExecutionTests</c>.
/// </summary>
[TestFixture]
public class UntypedConstTests
{
    private static readonly Dictionary<ConstLevel, FrontEndRun> Runs =
        Enum.GetValues<ConstLevel>().ToDictionary(l => l, l => T123Front.Run(UntypedConstProbes.Whole(l)));

    private static IEnumerable<TestCaseData> EveryRowAtEveryLevel()
        => from level in Enum.GetValues<ConstLevel>()
           from row in UntypedConstProbes.Rows
           select new TestCaseData(level, row.Name, row.Vb).SetName($"{level}_{row.Name}_is_{row.Vb}");

    private static IEnumerable<TestCaseData> ModuleRows()
        => UntypedConstProbes.Rows.Select(r => new TestCaseData(r.Name, r.Vb).SetName($"Module_{r.Name}_is_{r.Vb}"));

    private static IEnumerable<TestCaseData> LowerableModuleRows()
        => UntypedConstProbes.Rows
            .Where(r => !UntypedConstProbes.ModuleFoldRefused.Contains(r.Name) && !UntypedConstProbes.WrongFold.Contains(r.Name))
            .Select(r => new TestCaseData(r.Name, r.Vb).SetName($"Module_{r.Name}_lowers_as_{r.Vb}"));

    private static IEnumerable<TestCaseData> LiteralClassRows()
        => UntypedConstProbes.Rows.Where(r => UntypedConstProbes.Literal.Contains(r.Name))
            .Select(r => new TestCaseData(r.Name, r.Vb).SetName($"Class_{r.Name}_lowers_as_{r.Vb}"));

    private static IEnumerable<TestCaseData> LocalRows()
        => UntypedConstProbes.Rows.Select(r => new TestCaseData(r.Name, r.Vb).SetName($"Local_{r.Name}_lowers_as_{r.Vb}"));

    /// <summary>
    /// The tables ARE the proof, so their shape is pinned: a row cannot vanish without this test saying so. It is also the
    /// fixture's one plain <c>[Test]</c>, and it checks that every level's whole program is accepted by the front end.
    /// </summary>
    [Test]
    public void TheTable_HasItsRows_AndEveryLevelsProgramIsAccepted()
    {
        Assert.Multiple(() =>
        {
            Assert.That(UntypedConstProbes.Rows, Has.Length.EqualTo(33), "D1 rows");
            Assert.That(UntypedConstProbes.Rows.Select(r => r.Name).Distinct().Count(), Is.EqualTo(33), "names are unique");
            Assert.That(UntypedConstProbes.Rows.Select(r => r.Vb).Distinct().OrderBy(x => x, StringComparer.Ordinal),
                Is.EqualTo(new[] { "Boolean", "Char", "Double", "Integer", "Long", "Single", "String" }),
                "every type D1 can give a constant is in the table");
            foreach (var level in Enum.GetValues<ConstLevel>())
                Assert.That(Runs[level].AllErrors, Is.Empty, $"{level}: the whole table is one program the front end accepts");
        });
    }

    /// <summary>
    /// The headline: an untyped Const's type, as the ANALYZER assigns it, is vbc's <c>TypeName</c> at module, local and
    /// class level — 33 rows by 3 levels. Mutant M1int (always Integer) and M1obj (always Object) both fail here.
    /// </summary>
    [TestCaseSource(nameof(EveryRowAtEveryLevel))]
    public void AnUntypedConst_TakesVbcsTypeName(ConstLevel level, string name, string vb)
        => Assert.That(Runs[level].ConstTypes()[name], Is.EqualTo(vb),
            $"{level}: Const {name} = {UntypedConstProbes.Rows.Single(r => r.Name == name).Init}");

    /// <summary>
    /// What IntelliSense and every later pass read: the SYMBOL the analyzer leaves for a module-level constant carries the
    /// inferred type too, not the signature pass's Object stand-in.
    /// </summary>
    [TestCaseSource(nameof(ModuleRows))]
    public void TheModuleLevelSymbol_CarriesTheInferredType(string name, string vb)
    {
        var symbol = Runs[ConstLevel.Module].Analyzer.GlobalScope.Resolve(name);
        Assert.That(symbol, Is.Not.Null, $"no symbol for {name}");
        Assert.That(symbol.Type.ToString(), Is.EqualTo(vb));
    }

    /// <summary>
    /// The IR global: type, the Const flag, and a constant initial value — what the backends emit a `const` from.
    /// Module level only, and only the rows the IR builder can lower (see the header for the rest).
    /// </summary>
    [TestCaseSource(nameof(LowerableModuleRows))]
    public void TheModuleGlobal_IsATypedConstant(string name, string vb)
    {
        var program = UntypedConstProbes.Program(ConstLevel.Module,
            n => !UntypedConstProbes.ModuleFoldRefused.Contains(n) && !UntypedConstProbes.WrongFold.Contains(n));
        var global = JsTestSupport.BuildModule(program).GlobalVariables[name];

        Assert.Multiple(() =>
        {
            Assert.That(global.Type.Name, Is.EqualTo(vb));
            Assert.That(global.IsConst, Is.True);
            // A bare reference to another constant (EREF, EREF2, ECHR) keeps the reference: lowering leaves "already a
            // self-contained value" alone. Everything else is folded to a constant.
            Assert.That(global.InitialValue is IRConstant || global.InitialValue is IRVariable { IsGlobal: true, IsConst: true },
                Is.True, $"a Const's value is a constant or a reference to one, was {global.InitialValue?.GetType().Name}");
        });
    }

    /// <summary>
    /// The emitted C# declares each module constant with the CLR type that matches vbc's. `const double EDIV` is the
    /// line that disappears if `/` over two Integer constants stops being Double.
    /// </summary>
    [TestCaseSource(nameof(LowerableModuleRows))]
    public void TheModuleConst_IsDeclaredWithTheMatchingCSharpType(string name, string vb)
    {
        var program = UntypedConstProbes.Program(ConstLevel.Module,
            n => !UntypedConstProbes.ModuleFoldRefused.Contains(n) && !UntypedConstProbes.WrongFold.Contains(n));
        var csharp = ReturnCoercionTests.EmitCSharpForTest(program);

        Assert.That(csharp, Does.Match($@"\bconst {UntypedConstProbes.CSharpType(vb)} {name} = "), csharp);
    }

    /// <summary>A LOCAL constant is a typed local in the emitted C#: `double LD`, `long LL`, `char LC`.</summary>
    [TestCaseSource(nameof(LocalRows))]
    public void TheLocalConst_IsDeclaredWithTheMatchingCSharpType(string name, string vb)
    {
        var csharp = ReturnCoercionTests.EmitCSharpForTest(UntypedConstProbes.Whole(ConstLevel.Local));

        Assert.That(csharp, Does.Match($@"\b{UntypedConstProbes.CSharpType(vb)} {name} = "), csharp);
    }

    /// <summary>A class-level constant, literal initializers only (see the header): `public static double Scale = 1.5;`.</summary>
    [TestCaseSource(nameof(LiteralClassRows))]
    public void TheClassConst_IsDeclaredWithTheMatchingCSharpType(string name, string vb)
    {
        var program = UntypedConstProbes.Program(ConstLevel.Class, n => UntypedConstProbes.Literal.Contains(n));
        var csharp = ReturnCoercionTests.EmitCSharpForTest(program);

        Assert.That(csharp, Does.Match($@"\bpublic static {UntypedConstProbes.CSharpType(vb)} {name} = "), csharp);
    }

    // ============================================================================================
    // The parse shape
    // ============================================================================================

    /// <summary>The As clause is optional, and its absence is a null <c>Type</c> the analyzer fills — not a guessed one.</summary>
    [Test]
    public void TheAsClause_IsOptional_AndAnAbsentOneLeavesTypeNull()
    {
        var run = T123Front.Run("Const X = 800\nConst Y As Long = 5\nConst Z As String = \"a\"\nSub Main()\nEnd Sub\n");
        var consts = run.Nodes<ConstantDeclarationNode>().ToDictionary(c => c.Name);

        Assert.Multiple(() =>
        {
            Assert.That(run.AllErrors, Is.Empty);
            Assert.That(consts["X"].Type, Is.Null, "no As clause, no declared type");
            Assert.That(consts["X"].Value, Is.InstanceOf<LiteralExpressionNode>());
            Assert.That(consts["Y"].Type, Is.Not.Null);
            Assert.That(consts["Z"].Type, Is.Not.Null);
            Assert.That(run.TypeOf(consts["Y"]), Is.EqualTo("Long"), "a typed Const keeps its declared type");
            Assert.That(run.TypeOf(consts["Z"]), Is.EqualTo("String"));
        });
    }

    // ============================================================================================
    // The rules that still apply
    // ============================================================================================

    private static string AtLevel(ConstLevel level, string declaration) => level switch
    {
        ConstLevel.Module => declaration + "\nSub Main()\nEnd Sub\n",
        ConstLevel.Local => "Sub Main()\n    " + declaration + "\nEnd Sub\n",
        _ => "Class K\n    Public " + declaration + "\nEnd Class\nSub Main()\nEnd Sub\n",
    };

    /// <summary>
    /// BC30439 still applies to an inferred constant, at every level: the value is checked against the type it was given.
    /// vbc (S/t123/tw): `Const OV = 2147483647 + 1` is BC30439 at module and local level, and so is `2147483647 * 2`, and
    /// so is `B = A + 1` over `Const A = 2147483647`; `2147483647 + 1L` is a Long and is fine.
    /// </summary>
    [Test]
    public void TheFitCheck_StillAppliesToAnInferredConstant([Values] ConstLevel level,
        [Values("Const OV = 2147483647 + 1", "Const OV = 2147483647 * 2")] string declaration)
    {
        var errors = T123Front.Errors(AtLevel(level, declaration));

        Assert.That(errors, Has.Count.EqualTo(1), string.Join(" | ", errors));
        Assert.That(errors[0], Does.Contain("Constant expression not representable in type 'Integer'").And.Contain("'OV'"));
    }

    /// <summary>The same check through a chain: the constant that overflows is the one over another constant.</summary>
    [Test]
    public void TheFitCheck_NamesTheConstantThatOverflows_InAChain()
    {
        var errors = T123Front.Errors("Const A = 2147483647\nConst B = A + 1\nSub Main()\nEnd Sub\n");

        Assert.That(errors, Has.Count.EqualTo(1), string.Join(" | ", errors));
        Assert.That(errors[0], Does.Contain("not representable in type 'Integer'").And.Contain("'B'"));
    }

    /// <summary>A Long expression is a Long constant and fits (vbc: `2147483647 + 1L` prints 2147483648).</summary>
    [Test]
    public void AnExpressionThatIsALong_Fits([Values] ConstLevel level)
        => Assert.That(T123Front.Errors(AtLevel(level, "Const OV = 2147483647 + 1L")), Is.Empty);

    /// <summary>The TYPED fit check is untouched: a declared Byte still refuses 300.</summary>
    [Test]
    public void TheTypedFitCheck_IsUnchanged([Values] ConstLevel level)
    {
        var errors = T123Front.Errors(AtLevel(level, "Const B As Byte = 300"));

        Assert.That(errors, Has.Count.EqualTo(1), string.Join(" | ", errors));
        Assert.That(errors[0], Does.Contain("not representable in type 'Byte'"));
    }

    /// <summary>
    /// `Const N = Nothing` is refused with the advice `Dim x = Nothing` gets, at every level — vbc would make it an
    /// Object, which BasicLang does not infer for a Dim either. ⚠ The advice names `Const N As &lt;Type&gt; = Nothing`, and
    /// only Object takes it: `Const M As String = Nothing` is refused too, before #123 as well ("Constant value type
    /// 'Object' is not compatible with declared type 'String'"). Not asserted here.
    /// </summary>
    [Test]
    public void ConstNothing_IsRefused([Values] ConstLevel level)
    {
        var errors = T123Front.Errors(AtLevel(level, "Const N = Nothing"));

        Assert.That(errors, Has.Count.EqualTo(1), string.Join(" | ", errors));
        Assert.That(errors[0], Does.Contain("Cannot infer a type for constant 'N' from 'Nothing'").And.Contain("Const N As <Type> = Nothing"));
    }

    /// <summary>The spelling the advice points at does compile when the type is Object.</summary>
    [Test]
    public void ConstOfObjectNothing_IsAccepted([Values] ConstLevel level)
        => Assert.That(T123Front.Errors(AtLevel(level, "Const N As Object = Nothing")), Is.Empty);

    /// <summary>A constant with no value is still the existing diagnostic (vbc BC30438), typed or not.</summary>
    [Test]
    public void AConstWithNoValue_IsStillRefused([Values] ConstLevel level, [Values("Const Z", "Const Z As Integer")] string declaration)
    {
        var errors = T123Front.Errors(AtLevel(level, declaration));

        Assert.That(errors, Has.Count.EqualTo(1), string.Join(" | ", errors));
        Assert.That(errors[0], Does.Contain("Constant 'Z' must have a value"));
    }

    // ============================================================================================
    // Across files — the signature pass gives a sibling's untyped Const its literal's type
    // ============================================================================================

    private static readonly (string Name, string Source)[] ConstsFirst =
    {
        ("Consts.bas", """
            ' untyped Consts in the file that sorts FIRST
            Const WIDE = 800
            Const RATE = 1.5
            Const TITLE = "game"

            Module Settings
                Public Const Limit = 10
                Public Const Factor = 0.5
            End Module
            """),
        ("Main.bas", """
            Sub Main()
                Dim w As Integer = WIDE * 2
                Console.WriteLine(w)
                Dim r As Double = RATE * 3
                Console.WriteLine(r)
                Console.WriteLine(TITLE & "!")
                Console.WriteLine(Settings.Limit + 1)
                Console.WriteLine(Limit * Factor)
                Dim v = WIDE
                v = 2.5
                Console.WriteLine(v)
                Console.WriteLine(If(WIDE > 500, "wide", "narrow"))
            End Sub
            """),
    };

    private static readonly (string Name, string Source)[] ConstsLast =
    {
        ("Main.bas", """
            Sub Main()
                Dim w As Integer = WIDE * 2
                Console.WriteLine(w)
                Dim r As Double = RATE * 3
                Console.WriteLine(r)
                Console.WriteLine(TITLE & "!")
                Dim v = WIDE
                v = 2.5
                Console.WriteLine(v)
                Console.WriteLine(If(WIDE > 500, "wide", "narrow"))
            End Sub
            """),
        ("Zconsts.bas", """
            ' untyped Consts in the file that sorts LAST, no Module block
            Const WIDE = 800
            Const RATE = 1.5
            Const TITLE = "game"
            """),
    };

    private static CompilationResult CompileProject((string Name, string Source)[] files, bool reversed, bool aggressive, out string dir)
    {
        var root = Path.Combine(Path.GetTempPath(), "bl-t123-mf-" + Guid.NewGuid().ToString("N"));
        dir = root;
        Directory.CreateDirectory(root);
        foreach (var (name, source) in files) File.WriteAllText(Path.Combine(root, name), source);
        var paths = files.Select(f => Path.Combine(root, f.Name)).ToList();
        if (reversed) paths.Reverse();
        return new BasicCompiler(new CompilerOptions { OptimizeAggressive = aggressive }).CompileProjectFiles(paths);
    }

    /// <summary>
    /// The compile orders a program can have. The file order is the order the paths are handed to the compiler.
    /// <para>MC1 is the constants' file FIRST (its natural order), MC3 is the constants' file LAST — where the analyzer of
    /// Main sees only the signature pass's stand-in, which is the literal's type — in BOTH orders. Every use of an untyped
    /// Const from another file type-checks: `WIDE * 2` is an Integer product, `RATE * 3` a Double. Before the stand-in the
    /// constant was Object and `WIDE * 2` was "Arithmetic operator '*' requires numeric operands".</para>
    /// <para>⚠ MC2 is MC1 with the files handed over in the OTHER order (a `Module Settings` block in the file that comes
    /// last): it fails on the BEFORE build with a TYPED constant too — a Module-block Const from a later file is Object
    /// whatever its spelling, so `Settings.Limit + 1` is "Arithmetic operator '+' requires numeric operands". Pre-existing,
    /// no task; there is no row for it.</para>
    /// </summary>
    private static IEnumerable<TestCaseData> CompileOrders()
    {
        foreach (var aggressive in new[] { false, true })
        {
            yield return new TestCaseData(true, false, aggressive).SetName($"MC1_ConstsFirst_aggressive={aggressive}");
            yield return new TestCaseData(false, false, aggressive).SetName($"MC3_ConstsLast_aggressive={aggressive}");
            yield return new TestCaseData(false, true, aggressive).SetName($"MC3_ConstsLast_handedOver_reversed_aggressive={aggressive}");
        }
    }

    [TestCaseSource(nameof(CompileOrders))]
    public void AnUntypedConstInAnotherFile_TypeChecks(bool constsFirst, bool reversed, bool aggressive)
    {
        var files = constsFirst ? ConstsFirst : ConstsLast;
        var result = CompileProject(files, reversed, aggressive, out var dir);
        try
        {
            Assert.That(result.AllErrors.Select(e => e.Message), Is.Empty);
            var globals = result.CombinedIR.GlobalVariables.Values;
            Assert.Multiple(() =>
            {
                Assert.That(globals.Single(g => g.Name == "WIDE").Type.Name, Is.EqualTo("Integer"));
                Assert.That(globals.Single(g => g.Name == "RATE").Type.Name, Is.EqualTo("Double"));
                Assert.That(globals.Single(g => g.Name == "TITLE").Type.Name, Is.EqualTo("String"));
                if (constsFirst)
                {
                    Assert.That(globals.Single(g => g.Name.EndsWith("Limit")).Type.Name, Is.EqualTo("Integer"));
                    Assert.That(globals.Single(g => g.Name.EndsWith("Factor")).Type.Name, Is.EqualTo("Double"));
                }
            });
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* temp */ } }
    }
}
