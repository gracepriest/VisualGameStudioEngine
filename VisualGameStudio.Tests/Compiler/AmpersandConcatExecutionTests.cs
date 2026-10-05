using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #190 — `&` converts BOTH operands to String, the VB way, on every backend. RUN, against vbc, through every entry point.
//
//  ⛔ THE BUG. `i & j` with two Integers, `True & 1`, `c & c` with two Chars, `o & 1` with an Object and `1 & 2 & 3` were refused on every backend ("Operator '&' requires at least one string
//  operand"). VB's `&` is always concatenation: each operand goes through CStr first, so `1 & 2` is "12" and `True & 1` is "True1". The owner adopted VB's rule (2026-09-28). Now
//  `SemanticAnalyzer` types `&` as String whatever its operands, admitting a pair with NO String side only when `ConvertsToStringForConcat` holds for both (a number, Boolean, Char or Object, the
//  Nothing literal included), and `IRBuilder.ConcatOperandAsString` wraps each operand of such a pair in `CStr(...)` — the conversion an interpolated string's hole already uses — so every
//  backend sees a String concat of Strings. The Nothing literal is re-typed to "" on either side (it never built on C++: `nullptr + std::string`). `&` is no longer a Decimal context, so
//  `m & 1.50` prints "1.5" as vbc does. A class or a structure operand stays refused, as vbc refuses it (BC30452).
//
//  ⭐ THE ORACLE IS vbc. Each probe's `Vb` is what the SDK's vbc prints for the program wrapped in a VB Module with Microsoft.VisualBasic imported (S/t190/probes, the `.exp` beside each `.bas`) —
//  never what a backend printed. A line the table leaves out of a probe is one of the KNOWN GAPS below; the lines that remain are vbc's own, unchanged. A GROUP is one test case: it runs each of its
//  probes on every backend where that probe now RUNS, each through the spawned CLI, the CLI with `--optimize` and `BasicCompiler.CompileProjectFiles` (aggressive — what a Release .blproj build and
//  the IDE call), and reports every failing cell by probe id. A backend whose tool is missing is SKIPPED (`TempExec.RequireTool`: g++/clang++, Node, ilasm), never failed; the case is ignored only
//  when no cell could run. Every C# cell is `HangSafe`: a child process with a time limit (`CSharpProcessRunner`), never the in-process runner that has no timeout (#256).
//
//  ⭐ WHICH CELLS EXIST. A probe's `Agrees` flags are the backends where it now runs. JavaScript refuses a Char, a Decimal and a Long however they are used (BL7004, BL7003, BL7007: one fast
//  refusal row in `AmpersandConcatCompileTests`), and C++ has no Object, so those probes drop the backend.
//
//  ⛔ KNOWN GAPS — each a defect that is NOT #190's, each measured on the commit before it, listed with NO test (asserting one would pin the defect):
//    C#     a concat used as a RECEIVER is not parenthesised, so `(i & j).Length` and `Len(i & j)` count the wrong string (`Len("ab" & G())` prints "ab1" on master too) — task #275. The probes
//           leave those two lines out: `Wrap(i & j)` and `Console.WriteLine(i & j)` are the shapes that run.
//    JS     `CStr(1E+20)` prints all 21 digits, where vbc prints `1E+20` — task #280. The Double probe leaves `i & 1E+20` out.
//    FRONT  an Enum, an array, a type parameter or a DateTime operand with no String side is still refused, as it was before #190. CStr has no VB spelling for them on every backend, and on C#
//           the CStr of an enum would give the member NAME where VB gives the number.
//
//  ⭐ MUTANTS (S/t190/mut: the fix plus ONE change, built from a plain source copy and run against a copy of the test output with its BasicLang.dll swapped; each is killed by a committed test):
//    m1 `ConcatOperandAsString` never converts (a non-String & non-String reaches the backends raw):  groups 1-3 (a01 and a03 on C#: CS0029 / CS0019; a04 `1 & 2 & 3` prints 6 on C++ and JavaScript; MSIL: InvalidProgramException) and the fast `ANonStringPair_BecomesTwoCStrCalls_...`, `TheNothingLiteral_...` (the Integer is not a CStr call) and `ADoubleLiteralBesideADecimal_...`
//    m2 the Nothing literal is not re-typed to "":  group 2 (a05, C++ cells only: `nullptr + std::string("x")` does not build) and the fast `TheNothingLiteral_IsAnEmptyStringConstant_OnEitherSide`. C#, JavaScript and MSIL print the same with or without the re-type, so off C++ only the fast row sees it
//    m3 `&` is a Decimal context again (`m & 1.50` re-types the literal to Decimal):  group 3 (a11 on C#, C++ and MSIL: `m & 1.50` prints "1.51.50"; JavaScript refuses Decimal) and the fast `ADoubleLiteralBesideADecimal_StaysADouble_InAConcat`
//
//  ⚠ Named "…ExecutionTests" on purpose: its rows RUN under Node, so it is in JsExecutionTierRosterTests' roster.
// ================================================================================================

/// <summary>
/// #190 RUN: `&amp;` with no String operand — two Integers, an Integer and a Double, a Boolean, a Char, a Nothing, an Object, a Decimal, a Long, a chain, an append-assign and an argument — prints
/// vbc's answer on every backend where it runs, through every entry point; and a class operand is still refused by the CLI and the project build.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the g++, Node, ilasm and C# child runs share the machine with the spawned CLI
public class AmpersandConcatExecutionTests
{
    private const Bk Four = Bk.All;
    private const Bk NotJs = Bk.CSharp | Bk.Cpp | Bk.Msil;          // JavaScript refuses a Char (BL7004), a Decimal (BL7007) and a Long (BL7003)
    private const Bk NotCpp = Bk.CSharp | Bk.JavaScript | Bk.Msil;  // C++ has no Object: an Object local is "has no C++ mapping"

    // ================================================================================================
    // Group 1 — a number or a Boolean on each side. Kills m1 (a01 and a03: CS0029 / CS0019 on C#, InvalidProgramException on MSIL).
    // ================================================================================================

    /// <summary>`i &amp; j` printed directly and stored into a String (`j &amp; i`). Neither operand is a String. Kills m1.</summary>
    internal static readonly TempProbe a01_Integer_and_Integer = new("a01_Integer_and_Integer", """
        Sub Main()
            Dim i As Integer = 1
            Dim j As Integer = 2
            Console.WriteLine(i & j)
            Dim s As String = j & i
            Console.WriteLine(s)
        End Sub
        """, """
        12
        21
        """, Four, HangSafe: true);

    /// <summary>`i &amp; d` with d = 0.5 and 0.1 (a Double's shortest round-trip text, not "0.10000000000000001"), both orders, and a Double with an Integer on the right.</summary>
    internal static readonly TempProbe a02_Integer_and_Double = new("a02_Integer_and_Double", """
        Sub Main()
            Dim i As Integer = 12
            Dim d As Double = 0.5
            Console.WriteLine(i & d)
            Dim e As Double = 0.1
            Console.WriteLine(i & e)
            Console.WriteLine(e & i)
            Dim g As Double = 3
            Console.WriteLine(g & i)
        End Sub
        """, """
        120.5
        120.1
        0.112
        312
        """, Four, HangSafe: true);

    /// <summary>`b &amp; i` is "True1", `i &amp; n` is "1False" and `b &amp; n` is "TrueFalse": a Boolean is converted as VB's CStr does, on every backend. Kills m1.</summary>
    internal static readonly TempProbe a03_Boolean_and_Integer = new("a03_Boolean_and_Integer", """
        Sub Main()
            Dim b As Boolean = True
            Dim i As Integer = 1
            Console.WriteLine(b & i)
            Dim n As Boolean = False
            Console.WriteLine(i & n)
            Console.WriteLine(b & n)
        End Sub
        """, """
        True1
        1False
        TrueFalse
        """, Four, HangSafe: true);

    // ================================================================================================
    // Group 2 — a chain, Nothing, an append-assign and an argument. Kills m1 (a04), m2 (a05).
    // ================================================================================================

    /// <summary>⭐ `1 &amp; 2 &amp; 3` must print "123" — with no CStr (m1) C++ and JavaScript print 6, the numeric sum. Beside it `True &amp; False`, `1.5 &amp; 2` and `Dim s As String = 4 &amp; 5`. Kills m1.</summary>
    internal static readonly TempProbe a04_chain_1_2_3 = new("a04_chain_1_2_3", """
        Sub Main()
            Console.WriteLine(1 & 2 & 3)
            Console.WriteLine(True & False)
            Console.WriteLine(1.5 & 2)
            Dim s As String = 4 & 5
            Console.WriteLine(s)
        End Sub
        """, """
        123
        TrueFalse
        1.52
        45
        """, Four, HangSafe: true);

    /// <summary>⭐ `Nothing &amp; "x"`, `"y" &amp; Nothing`, `Nothing &amp; i` and `(Nothing &amp; Nothing)`: VB's CStr(Nothing) is "". On C++ `Nothing &amp; "x"` was `nullptr + std::string` and never built. Kills m2.</summary>
    internal static readonly TempProbe a05_Nothing_on_either_side = new("a05_Nothing_on_either_side", """
        Sub Main()
            Console.WriteLine(Nothing & "x")
            Console.WriteLine("y" & Nothing)
            Dim i As Integer = 3
            Console.WriteLine(Nothing & i)
            Console.WriteLine("[" & (Nothing & Nothing) & "]")
        End Sub
        """, """
        x
        y
        3
        []
        """, Four, HangSafe: true);

    /// <summary>`s &amp;= 5`, `s &amp;= True`, `s &amp;= i` — an append-assign with a non-String right side (it already worked: a regression pin on the shared path).</summary>
    internal static readonly TempProbe a06_append_assign_Integer_right = new("a06_append_assign_Integer_right", """
        Sub Main()
            Dim s As String = "a"
            s &= 5
            Console.WriteLine(s)
            s &= True
            Console.WriteLine(s)
            Dim i As Integer = 9
            s &= i
            Console.WriteLine(s)
        End Sub
        """, """
        a5
        a5True
        a5True9
        """, Four, HangSafe: true);

    /// <summary>A concat of two Integers passed to a String parameter and wrapped again: `Wrap(i &amp; j)` is "[456]". (`Len(i &amp; j)` is the C# gap #275 and stays out.)</summary>
    internal static readonly TempProbe a07_function_argument = new("a07_function_argument", """
        Function Wrap(s As String) As String
            Return "[" & s & "]"
        End Function
        Sub Main()
            Dim i As Integer = 4
            Dim j As Integer = 56
            Console.WriteLine(Wrap(i & j))
        End Sub
        """, "[456]", Four, HangSafe: true);

    /// <summary>The controls that already worked and must not move: `"a" &amp; 1`, `"a" &amp; i`, `"a" &amp; b`, `"a" &amp; d`, `i &amp; "z"` — a String on one side converts the other by the backend's own rule.</summary>
    internal static readonly TempProbe a08_a_String_side_is_unchanged = new("a08_a_String_side_is_unchanged", """
        Sub Main()
            Dim i As Integer = 1
            Console.WriteLine("a" & 1)
            Console.WriteLine("a" & i)
            Dim b As Boolean = True
            Console.WriteLine("a" & b)
            Dim d As Double = 0.1
            Console.WriteLine("a" & d)
            Console.WriteLine(i & "z")
        End Sub
        """, """
        a1
        a1
        aTrue
        a0.1
        1z
        """, Four, HangSafe: true);

    // ================================================================================================
    // Group 3 — a Char, an Object, a Decimal and a Long, on the backends that have them. Kills m3 (a11).
    // ================================================================================================

    /// <summary>`c &amp; e` (two Chars) and `c &amp; i`. Not JavaScript: it has no Char type (BL7004).</summary>
    internal static readonly TempProbe a09_Char_and_Char = new("a09_Char_and_Char", """
        Sub Main()
            Dim c As Char = "a"c
            Dim e As Char = "b"c
            Console.WriteLine(c & e)
            Dim i As Integer = 7
            Console.WriteLine(c & i)
        End Sub
        """, """
        ab
        a7
        """, NotJs, HangSafe: true);

    /// <summary>An Object on either side: `o &amp; 1` is "51", `i &amp; p` with p = 2.5 is "42.5", `q &amp; i` with q = True is "True4". Not C++: it has no Object.</summary>
    internal static readonly TempProbe a10_Object_and_Integer = new("a10_Object_and_Integer", """
        Sub Main()
            Dim o As Object = 5
            Console.WriteLine(o & 1)
            Dim p As Object = 2.5
            Dim i As Integer = 4
            Console.WriteLine(i & p)
            Dim q As Object = True
            Console.WriteLine(q & i)
        End Sub
        """, """
        51
        42.5
        True4
        """, NotCpp, HangSafe: true);

    /// <summary>⭐ A Decimal with an Integer both ways, and `m &amp; 1.50`: the literal stays a Double, so vbc prints "1.51.5", not the re-typed Decimal's "1.51.50". Not JavaScript (BL7007). Kills m3.</summary>
    internal static readonly TempProbe a11_Decimal_and_a_Double_literal = new("a11_Decimal_and_a_Double_literal", """
        Sub Main()
            Dim m As Decimal = 1.5
            Dim i As Integer = 2
            Console.WriteLine(m & i)
            Console.WriteLine(i & m)
            Console.WriteLine(m & 1.50)
        End Sub
        """, """
        1.52
        21.5
        1.51.5
        """, NotJs, HangSafe: true);

    /// <summary>A Long beyond Integer's range on either side. Not JavaScript: a JS number is exact only to 2^53 (BL7003).</summary>
    internal static readonly TempProbe a12_Long_and_Integer = new("a12_Long_and_Integer", """
        Sub Main()
            Dim l As Long = 9000000000
            Dim i As Integer = 1
            Console.WriteLine(l & i)
            Console.WriteLine(i & l)
        End Sub
        """, """
        90000000001
        19000000000
        """, NotJs, HangSafe: true);

    // ================================================================================================
    // The table
    // ================================================================================================

    internal static readonly IReadOnlyList<ProbeGroup> Groups = new[]
    {
        new ProbeGroup("Ampersand_of_a_number_and_a_Boolean_with_no_String_side",
            new[] { a01_Integer_and_Integer, a02_Integer_and_Double, a03_Boolean_and_Integer }),
        new ProbeGroup("Ampersand_in_a_chain_with_Nothing_as_an_append_and_as_an_argument",
            new[] { a04_chain_1_2_3, a05_Nothing_on_either_side, a06_append_assign_Integer_right, a07_function_argument, a08_a_String_side_is_unchanged }),
        new ProbeGroup("Ampersand_of_a_Char_an_Object_a_Decimal_and_a_Long_where_the_backend_has_them",
            new[] { a09_Char_and_Char, a10_Object_and_Integer, a11_Decimal_and_a_Double_literal, a12_Long_and_Integer }),
    };

    private static IEnumerable<TestCaseData> GroupCells() => Groups.Select(g => new TestCaseData(g).SetName(g.Id));

    /// <summary>
    /// The table IS the proof, so its shape is pinned: a probe cannot vanish, and a backend cannot be dropped from one, without this test saying so. It is also one of the fixture's plain [Test]s
    /// besides its case source: <c>JsExecutionTierRosterTests</c> counts attributes, and a fixture whose tests are all [TestCaseSource] counts as empty.
    /// </summary>
    [Test]
    public void TheTable_HasItsRows()
    {
        var probes = Groups.SelectMany(g => g.Probes).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(Groups.Select(g => g.Probes.Length), Is.EqualTo(new[] { 3, 5, 4 }));
            Assert.That(probes.Select(p => p.Id), Is.EqualTo(new[]
            {
                "a01_Integer_and_Integer", "a02_Integer_and_Double", "a03_Boolean_and_Integer",
                "a04_chain_1_2_3", "a05_Nothing_on_either_side", "a06_append_assign_Integer_right", "a07_function_argument", "a08_a_String_side_is_unchanged",
                "a09_Char_and_Char", "a10_Object_and_Integer", "a11_Decimal_and_a_Double_literal", "a12_Long_and_Integer",
            }));
            Assert.That(probes.Select(p => p.Id).Distinct().Count(), Is.EqualTo(probes.Count));
            Assert.That(probes.Where(p => !p.HangSafe).Select(p => p.Id), Is.Empty, "every C# cell runs in a child process with a time limit (#256)");
            // The known gaps are DROPPED backends, and each is named in the header.
            Assert.That(probes.Where(p => p.Agrees == Four).Count(), Is.EqualTo(8));
            Assert.That(probes.Where(p => p.Agrees == NotJs).Select(p => p.Id), Is.EqualTo(new[] { "a09_Char_and_Char", "a11_Decimal_and_a_Double_literal", "a12_Long_and_Integer" }));
            Assert.That(probes.Where(p => p.Agrees == NotCpp).Select(p => p.Id), Is.EqualTo(new[] { "a10_Object_and_Integer" }));
            Assert.That(probes.Sum(p => TempExec.Backends(p.Agrees).Count()), Is.EqualTo(8 * 4 + 3 * 3 + 1 * 3), "12 probes, each on every backend it runs on, each through three entry points");
        });
    }

    // ============================================================================================
    // RUN — vbc's answer, every backend the probe runs on, three entry points
    // ============================================================================================

    /// <summary>
    /// Each probe of the group on every backend it runs on, through the spawned CLI (standard passes), the CLI with `--optimize` and CompileProjectFiles with the aggressive passes. A pair with no
    /// String side that is not converted reaches the backend raw: CS0029 / CS0019 on C#, a numeric sum on C++ and JavaScript, InvalidProgramException on MSIL (m1); a Nothing that is not re-typed
    /// does not build on C++ (m2); a Decimal context re-types the literal and prints "1.51.50" (m3). A backend whose tool is missing is skipped; the test is ignored only when none could run.
    /// </summary>
    [TestCaseSource(nameof(GroupCells))]
    public void AGroupOfProbes_PrintsVbcsAnswer_OnEveryBackendItRuns(ProbeGroup group)
    {
        var failures = new List<string>();
        int ran = 0, skipped = 0;
        foreach (var probe in group.Probes)
        {
            foreach (var backend in TempExec.Backends(probe.Agrees))
            {
                try
                {
                    TempExec.AssertMatchesInEveryEntryPoint(backend, probe.Source, probe.Vb, probe.Id, probe.HangSafe);
                    ran++;
                }
                catch (IgnoreException)
                {
                    skipped++;
                }
                catch (AssertionException ex)
                {
                    ran++;
                    failures.Add(ex.Message);
                }
            }
        }

        Assert.That(failures, Is.Empty, string.Join("\n", failures));
        if (ran == 0) Assert.Ignore($"{group.Id}: no execution tool on this machine ({skipped} cells skipped).");
    }

    // ============================================================================================
    // REFUSED — a class operand, through the CLI and the project build
    // ============================================================================================

    /// <summary>
    /// `b &amp; 1` with a class `Box` is still refused, as vbc refuses it (BC30452) — through the spawned CLI on every target (standard and `--optimize`; no output file is left behind) and through
    /// CompileProjectFiles (what the IDE build calls). The front end says no before any backend is chosen, so no tool is needed. The in-process legs live in <c>AmpersandConcatCompileTests</c>.
    /// </summary>
    [Test]
    public void AClassAmpersandInteger_IsRefused_ThroughTheCliAndTheProjectBuild()
    {
        const string source = "Class Box\n    Public V As Integer\nEnd Class\nSub Main()\n    Dim b As New Box()\n    Console.WriteLine(b & 1)\nEnd Sub\n";
        const string expect = "Operator '&' is not defined for 'Box' and 'Integer'";
        var failures = new List<string>();

        foreach (var backend in TempExec.Backends(Bk.All))
        {
            foreach (var entry in new[] { EntryPoint.Cli, EntryPoint.CliOptimize })
            {
                var dir = Path.Combine(Path.GetTempPath(), "bl-t190-cli-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                try
                {
                    File.WriteAllText(Path.Combine(dir, "Prog.bas"), source);
                    var args = new List<string> { "Prog.bas", "--target=" + TempExec.TargetName(backend) };
                    if (entry == EntryPoint.CliOptimize) args.Add("--optimize");
                    var (exit, stdout, stderr) = CliTestHarness.RunProcess(CliTestHarness.CliPath(), args.ToArray(), dir, timeoutMs: 120_000);
                    var said = stdout + stderr;
                    if (exit == 0) failures.Add($"{TempExec.TargetName(backend)}, {entry}: the CLI accepted the program:\n{said}");
                    else if (!said.Contains(expect, StringComparison.Ordinal)) failures.Add($"{TempExec.TargetName(backend)}, {entry}: refused, but not with [{expect}]: {said.Replace("\n", " | ")}");
                    if (File.Exists(Path.Combine(dir, "Prog" + TempExec.Extension(backend)))) failures.Add($"{TempExec.TargetName(backend)}, {entry}: a refused program left an output file behind");
                }
                finally { try { Directory.Delete(dir, recursive: true); } catch { /* temp */ } }
            }
        }

        var projectDir = Path.Combine(Path.GetTempPath(), "bl-t190-proj-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(projectDir);
        try
        {
            var path = Path.Combine(projectDir, "Main.bas");
            File.WriteAllText(path, source);
            var result = new BasicCompiler(new CompilerOptions { OptimizeAggressive = true }).CompileProjectFiles(new List<string> { path });
            if (!result.HasErrors) failures.Add("CompileProjectFiles accepted the program");
            else
            {
                var said = string.Join(" | ", result.AllErrors.Select(e => e.Message));
                if (!said.Contains(expect, StringComparison.Ordinal)) failures.Add($"CompileProjectFiles refused, but not with [{expect}]: {said}");
            }
        }
        finally { try { Directory.Delete(projectDir, recursive: true); } catch { /* temp */ } }

        Assert.That(failures, Is.Empty, string.Join("\n", failures));
    }
}

/// <summary>
/// #190 COMPILE, in process — no spawned CLI, no native compile, so it runs in the fast subset. The refusal vbc ALSO gives (a class operand), the JavaScript refusals (Char, Decimal, Long), every
/// operand pair the front end now admits, and the IR shape of the conversion itself: both operands through `CStr`, none when a String is present, the Nothing literal re-typed to "", a Double literal
/// left a Double beside a Decimal.
/// </summary>
[TestFixture]
public class AmpersandConcatCompileTests
{
    /// <summary>Parse (asserting no parse error: a typo in a probe must not pass as a refusal), analyze, and return the analyzer, the AST and every ERROR with its line.</summary>
    private static (SemanticAnalyzer Analyzer, ProgramNode Ast, bool Ok, List<(int Line, string Message)> Errors) Analyze(string source)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty, "the probe does not parse: " + string.Join("; ", parser.Errors.Select(e => e.ToString())));
        var analyzer = new SemanticAnalyzer();
        var ok = analyzer.Analyze(ast);
        return (analyzer, ast, ok, analyzer.Errors.Where(e => e.Severity == ErrorSeverity.Error).Select(e => (e.Line, e.Message)).ToList());
    }

    /// <summary>The IR of `Sub Main` for a program the front end ACCEPTS: its instructions in order.</summary>
    private static List<IRInstruction> MainInstructions(string source)
    {
        var (analyzer, ast, ok, errors) = Analyze(source);
        Assert.That(ok, Is.True, "the probe is refused: " + string.Join("; ", errors.Select(e => $"line {e.Line}: {e.Message}")));
        var module = new IRBuilder(analyzer).Build(ast, "TestModule");
        var main = module.Functions.Single(f => string.Equals(f.Name, "Main", StringComparison.OrdinalIgnoreCase));
        return main.Blocks.SelectMany(b => b.Instructions).ToList();
    }

    /// <summary>
    /// A class on EITHER side of `&amp;`, with no String operand, is refused with ONE error on the offending line that names both types, and nothing else in the program is refused: the Integer and
    /// the String concats beside it are accepted. vbc says BC30452 for both orders.
    /// </summary>
    [Test]
    public void AClassOnEitherSide_WithNoStringOperand_IsRefused()
    {
        const string head = "Class Box\n    Public V As Integer\nEnd Class\nSub Main()\n    Dim b As New Box()\n    Dim i As Integer = 1\n    Dim t As String = i & i\n    t = \"a\" & i\n";
        var cases = new (string Name, string Statement, string Message)[]
        {
            ("a class on the left", "    Console.WriteLine(b & 1)\nEnd Sub", "Operator '&' is not defined for 'Box' and 'Integer'"),
            ("a class on the right", "    Console.WriteLine(1 & b)\nEnd Sub", "Operator '&' is not defined for 'Integer' and 'Box'"),
        };
        Assert.Multiple(() =>
        {
            foreach (var (name, statement, message) in cases)
            {
                var (_, _, ok, errors) = Analyze(head + statement);
                Assert.That(ok, Is.False, $"{name}: the front end accepted it");
                Assert.That(errors, Has.Count.EqualTo(1), $"{name}: " + string.Join(" | ", errors.Select(e => $"line {e.Line}: {e.Message}")));
                if (errors.Count == 1)
                {
                    Assert.That(errors[0].Line, Is.EqualTo(9), $"{name}: {errors[0].Message}");
                    Assert.That(errors[0].Message, Does.Contain(message), name);
                }
            }
        });
    }

    /// <summary>
    /// JavaScript has no Char, Decimal or Long, so `c &amp; c`, `m &amp; 1` and `l &amp; 1` stay BL7004 / BL7007 / BL7003 — admitting `&amp;` for them does not make them JavaScript values. Through the
    /// standard and the aggressive passes (the refusal is a capability check, not an optimizer rule). The probes that hold none of them run: the execution fixture's rows.
    /// </summary>
    [Test]
    public void JavaScript_ACharADecimalAndALongAmpersand_AreStillRefused()
    {
        var cases = new (string Name, string Source, string Code, string What)[]
        {
            ("Char", "Sub Main()\n    Dim c As Char = \"a\"c\n    Console.WriteLine(c & c)\nEnd Sub", "BL7004: 'Char' cannot be lowered to JavaScript", "local variable 'c'"),
            ("Decimal", "Sub Main()\n    Dim m As Decimal = 1.5\n    Console.WriteLine(m & 1)\nEnd Sub", "BL7007: 'Decimal' is not available on the JavaScript backend", "'m'"),
            ("Long", "Sub Main()\n    Dim l As Long = 9000000000\n    Console.WriteLine(l & 1)\nEnd Sub", "BL7003: 'Long' cannot be lowered to JavaScript", "local variable 'l'"),
        };
        Assert.Multiple(() =>
        {
            foreach (var (name, source, code, what) in cases)
            {
                var standard = Assert.Throws<ForeignFeatureException>(() => JsTestSupport.Compile(source), name);
                var aggressive = Assert.Throws<ForeignFeatureException>(() => JsTestSupport.CompileAggressive(source), name);
                Assert.That(standard!.Message, Does.StartWith(code), name);
                Assert.That(standard.Message, Does.Contain(what), name);
                Assert.That(aggressive!.Message, Does.StartWith(code), name);
            }
        });
    }

    /// <summary>
    /// Every operand pair the front end admits with no String side, in ONE program that must be accepted whole: two Integers, a Boolean and an Integer, two Chars, an Object and an Integer, Nothing
    /// and Nothing, a Double and a Decimal, a Long and an Integer, a chain of literals and an append-assign with a number. A predicate that dropped any one of them fails here, in the fast tier,
    /// before a native compiler is asked.
    /// </summary>
    [Test]
    public void EveryPairOfNumbersBooleansCharsAndObjects_IsAccepted()
    {
        const string source = """
            Sub Main()
                Dim i As Integer = 1
                Dim b As Boolean = True
                Dim c As Char = "a"c
                Dim o As Object = 5
                Dim d As Double = 0.5
                Dim m As Decimal = 1.5
                Dim l As Long = 9000000000
                Dim s As String = ""
                s = i & i
                s = b & i
                s = c & c
                s = o & 1
                s = Nothing & Nothing
                s = d & m
                s = l & i
                s = 1 & 2 & 3
                s &= 5
                s &= i
            End Sub
            """;
        var (_, _, ok, errors) = Analyze(source);
        Assert.That(errors.Select(e => $"line {e.Line}: {e.Message}"), Is.Empty);
        Assert.That(ok, Is.True);
    }

    /// <summary>
    /// The IR shape of the conversion: `i &amp; j` becomes a String concat of exactly TWO `CStr(...)` calls typed String, one over each Integer — the conversion an interpolation hole uses —
    /// while `"a" &amp; i` gets none (a String side converts the other by the backend's own rule, and a second call would change the emitted text of every concat that ran before #190). Without the
    /// calls (m1) the Integers reach the backend raw. Fast — the execution fixture proves the same thing by RUNNING it, slowly.
    /// </summary>
    [Test]
    public void ANonStringPair_BecomesTwoCStrCalls_AndAStringSideBecomesNone()
    {
        var both = MainInstructions("Sub Main()\n    Dim i As Integer = 1\n    Dim j As Integer = 2\n    Dim s As String = i & j\n    Console.WriteLine(s)\nEnd Sub");
        var one = MainInstructions("Sub Main()\n    Dim i As Integer = 1\n    Dim s As String = \"a\" & i\n    Console.WriteLine(s)\nEnd Sub");

        var conversions = both.OfType<IRCall>().Where(c => c.FunctionName == "CStr").ToList();
        var concat = both.OfType<IRBinaryOp>().Single(o => o.Operation == BinaryOpKind.Concat);
        Assert.Multiple(() =>
        {
            Assert.That(conversions, Has.Count.EqualTo(2), "one CStr per operand of `i & j`");
            Assert.That(conversions.Select(c => c.Type.Name), Is.All.EqualTo("String"));
            Assert.That(conversions.Select(c => c.Arguments.Single().Type.Name), Is.All.EqualTo("Integer"));
            Assert.That(concat.Left, Is.SameAs(conversions[0]), "the concat's left operand is the first CStr");
            Assert.That(concat.Right, Is.SameAs(conversions[1]), "the concat's right operand is the second CStr");
            Assert.That(one.OfType<IRCall>().Where(c => c.FunctionName == "CStr"), Is.Empty, "`\"a\" & i` has a String side: no CStr");
        });
    }

    /// <summary>
    /// The Nothing literal is re-typed in place to the String constant "" on EITHER side — VB's CStr(Nothing) — whether the partner is a String (`Nothing &amp; "x"`, `"y" &amp; Nothing`) or an
    /// Integer (`Nothing &amp; i`, where the Integer alone goes through CStr). Left a null constant (m2), C++ emits `nullptr + std::string` and never builds.
    /// </summary>
    [Test]
    public void TheNothingLiteral_IsAnEmptyStringConstant_OnEitherSide()
    {
        var ir = MainInstructions("""
            Sub Main()
                Dim i As Integer = 3
                Dim a As String = Nothing & "x"
                Dim b As String = "y" & Nothing
                Dim c As String = Nothing & i
            End Sub
            """);
        var concats = ir.OfType<IRBinaryOp>().Where(o => o.Operation == BinaryOpKind.Concat).ToList();

        Assert.That(concats, Has.Count.EqualTo(3));
        Assert.Multiple(() =>
        {
            Assert.That(concats[0].Left, Is.InstanceOf<IRConstant>().With.Property("Value").EqualTo(""), "Nothing & \"x\": the left");
            Assert.That(concats[0].Left.Type.Name, Is.EqualTo("String"));
            Assert.That(concats[1].Right, Is.InstanceOf<IRConstant>().With.Property("Value").EqualTo(""), "\"y\" & Nothing: the right");
            Assert.That(concats[1].Right.Type.Name, Is.EqualTo("String"));
            Assert.That(concats[2].Left, Is.InstanceOf<IRConstant>().With.Property("Value").EqualTo(""), "Nothing & i: the left");
            Assert.That(concats[2].Right, Is.InstanceOf<IRCall>().With.Property("FunctionName").EqualTo("CStr"), "Nothing & i: only the Integer is converted");
            Assert.That(ir.OfType<IRCall>().Count(c => c.FunctionName == "CStr"), Is.EqualTo(1), "no CStr over a Nothing");
        });
    }

    /// <summary>
    /// `&amp;` is not a Decimal context: in `m &amp; 1.50` the literal stays a Double, so its text is VB's "1.5". A Decimal context re-types the literal (`m * 1.50` does) and the concat would print the
    /// re-typed Decimal's "1.50" (m3). Reads the CStr's argument off the IR: a constant typed Double beside the Decimal variable.
    /// </summary>
    [Test]
    public void ADoubleLiteralBesideADecimal_StaysADouble_InAConcat()
    {
        var ir = MainInstructions("Sub Main()\n    Dim m As Decimal = 1.5\n    Dim s As String = m & 1.50\n    Console.WriteLine(s)\nEnd Sub");
        var arguments = ir.OfType<IRCall>().Where(c => c.FunctionName == "CStr").Select(c => c.Arguments.Single()).ToList();

        Assert.That(arguments, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(arguments[0].Type.Name, Is.EqualTo("Decimal"), "the variable m");
            Assert.That(arguments[1], Is.InstanceOf<IRConstant>(), "the literal 1.50");
            Assert.That(arguments[1].Type.Name, Is.EqualTo("Double"), "the literal is not re-typed to Decimal");
        });
    }
}
