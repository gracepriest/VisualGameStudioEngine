using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #139 — the SHAPE of the C# a `MyBase.M(...)` call is emitted as. Nothing runs here and nothing spawns: the emitted text is read and handed to Roslyn
//  to COMPILE (never to run). This is the fast subset's half of the fix, and the half that names the mutants the RUN fixture
//  (MyBaseMethodCallStatementExecutionTests) cannot tell apart cheaply — the two that change text and not behaviour.
//
//  THE BUG. IRBaseMethodCall is an IR VALUE. CSharpBackend.ShouldEmitInstruction had an arm for IRCall and one for IRInstanceMethodCall but none for it, so it fell
//  to the catch-all for values — "write it only if it is named after a declared variable" — and a base call never is. A statement-level `MyBase.Show(n + 1)` (a Sub, or a
//  Function whose result is discarded) was never written. A base call whose result is USED was inlined into its use and ran, which is why only the statement form was wrong.
//  The fix gives it IRCall's rule (a statement when void or unused; inlined at its one use otherwise) and gives Visit(IRBaseMethodCall) IRCall's statement forms.
//
//  What it pins, through the standard pipeline, the aggressive pipeline (what `--optimize` and a Release project build run) and the project entry point
//  (`BasicCompiler.CompileProjectFiles`, what the IDE's build service calls):
//    - every base call of the 33-row table is written where it is and as it is: a statement as `base.M(args);` between the lines it sits between, in each body copy the
//      emitter writes; a used result inline, exactly as often as the source calls it, with no statement-form copy beside it;
//    - a discarded Function result is a BARE call (never `int tN = base.F(...);`) and a statement is preceded by the `#line` of its own source line;
//    - a lambda holding a statement-level base call is a BLOCK lambda, and one holding only a used base result stays an EXPRESSION lambda;
//    - a program with no base method call writes the C# it wrote before (three representatives: the Derived class of each, line for line);
//    - ADR-0001 is still answered first: a base call with two uses is written once, into a temp (a hand-edited-IR witness — no source shape gives a base call two uses);
//    - every row compiles (Roslyn, not run); a ByRef argument to a base method is written with `ref` (#265) and compiles;
//    - the run fixture never runs emitted C# in the test host.
//
//  ⭐ MUTANTS (S/t139/mut and S/t139/tw/mut: each is the fix plus ONE change, built in a detached worktree and run against a copy of the test output with its BasicLang.dll swapped;
//  the number is how many of this fixture's 67 tests fail). KILLED here: M1 the arm removed, a base call falls to the IRValue catch-all again 38; M7 the arm placed AFTER the IRValue arm
//  (unreachable, so = M1) 38; M2 the arm answers only for a Sub, a discarded Function result is dropped 12; M3 every base call a statement while the visit writes it, a used result runs
//  twice 21; M8 off by one, a result used ONCE is a statement too 21; M5 the old visit behind the fixed arm, `int tN = base.F(...)` for a discarded result 13; M9 `IsExpressionLambda` does
//  not ask about a base call, a Function lambda that calls the base and returns is `() => K` 4; M3a the arm says "statement" for every base call and the visit does not, the same
//  behaviour in different TEXT (a used result's expression lambda becomes a block) 2; M10 the arm placed BEFORE ADR-0001's materialised-value answer 1 — the hand-edited-IR witness only,
//  because no source shape gives a base call two uses. NOT killable, and why: M4 and M6 (the named-destination disjunct removed from the arm / from the visit) — IsNamedDestination is
//  false for every base call (IRBuilder renames none to a variable), so the C# of 62 programs x {standard, --optimize} is byte-identical.
// ================================================================================================

[TestFixture]
public class MyBaseMethodCallStatementShapeTests
{
    // ============================================================================================
    // helpers
    // ============================================================================================

    private enum Pipeline { Standard, Aggressive }

    private static string Emit(string source, Pipeline pipeline)
    {
        var module = CSharpTestSupport.BuildModule(source, "Prog.bas");
        if (pipeline == Pipeline.Aggressive)
        {
            AggressivePipeline.Apply(module);
        }
        else
        {
            var passes = new BasicLang.Compiler.IR.Optimization.OptimizationPipeline();
            passes.AddStandardPasses();
            passes.Run(module);
        }

        return new ImprovedCSharpCodeGenerator().Generate(module).Replace("\r\n", "\n");
    }

    /// <summary>The same program through <c>BasicCompiler.CompileProjectFiles</c> (aggressive): the entry point of a Release project build and of the IDE.</summary>
    private static string EmitViaProject(string source) => TempExec.Emit(Bk.CSharp, EntryPoint.ProjectRelease, source).Replace("\r\n", "\n");

    private static IEnumerable<(string Name, Func<string, string> Emit)> EntryPoints()
    {
        yield return ("standard", s => Emit(s, Pipeline.Standard));
        yield return ("aggressive", s => Emit(s, Pipeline.Aggressive));
        yield return ("project", EmitViaProject);
    }

    private static string Probe(string id) => MyBaseCallProbes.All.Concat(MyBaseCallProbes.ByRef).Single(p => p.Id == id).Source;

    /// <summary>Every line of the program, trimmed, without blank lines and `#line` directives.</summary>
    internal static string[] Lines(string csharp)
        => csharp.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#line", StringComparison.Ordinal)).ToArray();

    /// <summary>The lines that call through <c>base.</c> (a constructor's <c>: base(...)</c> initialiser is not one), in order. The CLI and the project entry point must agree on it.</summary>
    internal static string[] BaseLines(string csharp) => Lines(csharp).Where(l => l.Contains("base.", StringComparison.Ordinal)).ToArray();

    /// <summary>A line that is nothing but <c>base.M(args);</c> — the statement form.</summary>
    private static readonly Regex StatementForm = new(@"^base\.\w+(<[^>]*>)?\(.*\);$", RegexOptions.Compiled);

    /// <summary>A line that declares a local from a base call: <c>int t1 = base.F(x);</c> — what the visit wrote before #139 made it reachable.</summary>
    private static readonly Regex DeclaresFromBaseCall = new(@"^[A-Za-z_][\w<>,\[\]?.]*\s+[A-Za-z_]\w*\s*=\s*base\.", RegexOptions.Compiled);

    private static int Count(string text, string needle)
    {
        var n = 0;
        for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = text.IndexOf(needle, i + needle.Length, StringComparison.Ordinal)) n++;
        return n;
    }

    // ============================================================================================
    // THE TABLE — what each row's base calls are written as
    // ============================================================================================

    private static readonly (string, string, string)[] None = Array.Empty<(string, string, string)>();
    private static readonly string[] NoInline = Array.Empty<string>();

    /// <summary>
    /// A row's expectation: its statement-form base calls as (the line before, the call, the line after) — the neighbours are matched as a PREFIX, so a change to how an
    /// unrelated statement is spelled (`Console.WriteLine(` …) does not move this table; the call itself is exact — and its inline uses, exact, in order.
    /// A bottom-tested loop is written ONCE (#227: `do { … } while (c);`; it was a peeled first iteration plus a copy of the body, so its call appeared twice), so
    /// its call appears once, as often as the source calls it.
    /// </summary>
    public sealed record Expected(string Id, (string Prev, string Stmt, string Next)[] Statements, string[] Inline)
    {
        public override string ToString() => Id;
    }

    internal static readonly Expected[] Rows =
    {
        new("s1_sub", new[] { ("{", "base.Show(n + 1);", "Console.WriteLine(") }, NoInline),
        new("s2_func", new[] { ("{", "base.F(x + 1);", "Console.WriteLine(") }, NoInline),
        new("s2b_funcself", new[] { ("{", "base.F(n);", "return K * 10;") }, NoInline),
        new("s2c_types", new[] { ("{", "base.S(1);", "base.B(1);"), ("base.S(1);", "base.B(1);", "base.D(1);"), ("base.B(1);", "base.D(1);", "Console.WriteLine(") }, NoInline),
        new("s2d_tostring", new[] { ("{", "base.ToString();", "return \"D\";") }, NoInline),
        new("s3_noargs", new[] { ("{", "base.Bump();", "K = K + 1;") }, NoInline),
        new("c1_ctor", new[] { ("{", "base.Init(5);", "Console.WriteLine(") }, NoInline),
        new("c2_prop", new[] { ("{", "base.Bump(1);", "return v;"), ("{", "base.Bump(100);", "v = value;") }, NoInline),
        new("c3_lambda", new[] { ("a = () => {", "base.Bump(1);", "};"), ("f = () => {", "base.Bump(10);", "return K;") }, NoInline),
        new("c3b_lamvalue", None, new[] { "f = () => base.Tag(n) + 10;" }),
        new("c4_if", new[] { ("{", "base.Bump(1);", "}"), ("{", "base.Bump(100);", "}") }, NoInline),
        new("c5_loop", new[] { ("{", "base.Bump(i);", "i = i + 1;"), ("{", "base.Bump(100);", "j = j + 1;") }, NoInline),
        new("c5b_dowhile", new[] { ("{", "base.Bump(10);", "i = i + 1;"), ("{", "base.Bump(1);", "i = i + 1;") }, NoInline), // #227: the Do … Loop While body is written ONCE (it was a peel plus a copy: the first call twice)
        new("c5c_foreach", new[] { ("{", "base.Bump(1);", "}"), ("{", "base.Bump(20);", "}"), ("{", "base.Bump(300);", "}") }, NoInline),
        new("c6_select", new[] { ("case 1:", "base.Bump(1);", "break;"), ("case 2:", "base.Bump(20);", "break;"), ("default:", "base.Bump(300);", "break;") }, NoInline),
        new("c7_try", new[] { ("{", "base.Bump(1);", "if (x > 0)"), ("{", "base.Bump(10);", "}"), ("{", "base.Bump(100);", "}") }, NoInline),
        new("c8_twolevel", new[] { ("{", "base.Bump(n + 1);", "Console.WriteLine(") }, NoInline),
        new("c8b_chain", new[] { ("{", "base.Bump(n * 10);", "Console.WriteLine("), ("{", "base.Bump(n + 1);", "Console.WriteLine(") }, NoInline),
        new("c9b_genderived", new[] { ("{", "base.Show(n + 1);", "Console.WriteLine(") }, NoInline),
        new("c9c_genmethod", new[] { ("{", "base.Put(5);", "base.Put(\"s\");"), ("base.Put(5);", "base.Put(\"s\");", "Console.WriteLine(") }, NoInline),
        new("n1_tempname", new[] { ("t3 = 400;", "base.F(x);", "g = () => {"), ("g = () => {", "base.F(x);", "return t0 + t1;") }, NoInline),
        new("p2_object", new[] { ("{", "base.Show(5);", "Console.WriteLine(") }, NoInline),
        new("p3_optional", new[] { ("{", "base.Show(1, 7);", "base.Show(1, 2);"), ("base.Show(1, 7);", "base.Show(1, 2);", "}") }, NoInline), // the Optional left out is filled with its default (#265)
        new("p4_paramarray", new[] { ("t0[2] = 3;", "base.Sum(t0);", "Console.WriteLine(") }, NoInline), // the three loose arguments are packed into an array first (#265)
        new("v1_dim", None, new[] { "r = base.F(x);" }),
        new("v2_local", None, new[] { "r = base.F(x);" }),
        new("v3_field", None, new[] { "Total = base.F(x);", "this.Total = base.F(x + 1);" }),
        new("v4_param", None, new[] { "x = base.F(x);" }),
        new("v5_twice", None, new[] { "Console.WriteLine(base.F(x) + base.F(x + 1));" }),
        new("v6_select", None, new[] { "switch (base.F(x))" }),
        new("v6b_selrange", None, new[] { "switch (base.F(x))" }),
        new("v7_inline", None, new[] { "Console.WriteLine(base.F(x) * 3);", "Console.WriteLine(base.F(base.F(x)));" }),
        new("v8_ifcond", None, new[] { "if (base.F(x) > 5)", "return base.F(x + 1);" }),
    };

    private static IEnumerable<TestCaseData> RowCells() => Rows.Select(r => new TestCaseData(r).SetName(r.Id));

    private static string Show((string Prev, string Stmt, string Next) t) => $"[{t.Prev}] {t.Stmt} [{t.Next}]";

    private static (string Prev, string Stmt, string Next)[] StatementsOf(string[] lines)
    {
        var found = new List<(string, string, string)>();
        for (var i = 0; i < lines.Length; i++)
            if (StatementForm.IsMatch(lines[i]))
                found.Add((i > 0 ? lines[i - 1] : "", lines[i], i + 1 < lines.Length ? lines[i + 1] : ""));
        return found.ToArray();
    }

    /// <summary>
    /// ⭐ THE TABLE. Every base call of every row, through the standard pipeline, the aggressive one and the project entry point, is written exactly as the table says.
    /// A statement-level call that is not written (M1; M2 for a discarded Function result) is a missing triple; a used result that is ALSO written as a statement
    /// (M3, M8) is an extra one, and an extra occurrence of the inline line; a discarded Function result declared into a temp (M5) is an inline line the table does not hold.
    /// </summary>
    [TestCaseSource(nameof(RowCells))]
    public void EveryBaseCall_IsWrittenWhereItIs_AndAsOftenAsTheSourceCallsIt(Expected row)
    {
        var source = Probe(row.Id);
        foreach (var (name, emit) in EntryPoints())
        {
            var csharp = emit(source);
            var lines = Lines(csharp);
            var statements = StatementsOf(lines);
            var inline = lines.Where(l => l.Contains("base.", StringComparison.Ordinal) && !StatementForm.IsMatch(l)).ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(statements.Select(s => s.Stmt), Is.EqualTo(row.Statements.Select(s => s.Stmt)), $"{row.Id} {name}: the statement-form base calls, in order:\n{csharp}");
                for (var i = 0; i < Math.Min(statements.Length, row.Statements.Length); i++)
                {
                    var (prev, _, next) = statements[i];
                    var want = row.Statements[i];
                    Assert.That(prev, Does.StartWith(want.Prev), $"{row.Id} {name}: the line BEFORE {Show(statements[i])}");
                    Assert.That(next, Does.StartWith(want.Next), $"{row.Id} {name}: the line AFTER {Show(statements[i])}");
                }

                Assert.That(inline, Is.EqualTo(row.Inline), $"{row.Id} {name}: the base calls written inside another statement:\n{csharp}");
            });
        }
    }

    // ============================================================================================
    // A discarded Function result is a BARE call
    // ============================================================================================

    /// <summary>
    /// ⭐ `MyBase.F(x + 1)` whose result nothing reads is `base.F(x + 1);`: no declaration, whatever the result type (Integer, String, Boolean, Double). The visit used to write
    /// `T tN = base.F(...);` — a local nothing reads, and a redeclaration (CS0128) were the call ever a named destination. Mutant M5 (the old visit, behind the fixed arm) writes
    /// it, M2 (the arm answers only for a Sub) writes nothing; both change only this text or the counter, so this is their cheap kill. No line anywhere in the table
    /// declares a local from a base call.
    /// </summary>
    [TestCase("s2_func", TestName = "ADiscardedFunctionResult_IsABareCall")]
    [TestCase("s2b_funcself", TestName = "ADiscardedFunctionResult_InTheOverrideOfThatFunction_IsABareCall")]
    [TestCase("s2c_types", TestName = "ADiscardedStringBooleanAndDoubleResult_AreBareCalls")]
    [TestCase("s2d_tostring", TestName = "ADiscardedToString_IsABareCall")]
    [TestCase("n1_tempname", TestName = "ADiscardedResult_BesideUserLocalsNamedLikeTemps_IsABareCall")]
    public void ADiscardedFunctionResult_IsABareCall_NotADeclaration(string id)
    {
        foreach (var (name, emit) in EntryPoints())
        {
            var csharp = emit(Probe(id));
            var lines = Lines(csharp);
            Assert.Multiple(() =>
            {
                Assert.That(lines.Where(l => DeclaresFromBaseCall.IsMatch(l)), Is.Empty, $"{id} {name}: a local declared from a base call:\n{csharp}");
                Assert.That(lines.Count(l => StatementForm.IsMatch(l)), Is.EqualTo(lines.Count(l => l.Contains("base.", StringComparison.Ordinal))),
                    $"{id} {name}: every base call of the row is a bare statement:\n{csharp}");
                Assert.That(lines.Count(l => StatementForm.IsMatch(l)), Is.GreaterThan(0), $"{id} {name}: the call is written at all:\n{csharp}");
            });
        }
    }

    [Test]
    public void NoRowDeclaresALocalFromABaseCall()
    {
        var offenders = new List<string>();
        foreach (var row in MyBaseCallProbes.All.Concat(MyBaseCallProbes.ByRef))
            foreach (var (name, emit) in EntryPoints())
                foreach (var line in Lines(emit(row.Source)).Where(l => DeclaresFromBaseCall.IsMatch(l)))
                    offenders.Add($"{row.Id} {name}: {line}");

        Assert.That(offenders, Is.Empty, string.Join("\n", offenders));
    }

    // ============================================================================================
    // A USED result is inlined ONCE
    // ============================================================================================

    /// <summary>
    /// ⭐ The half of the rule that was right before the fix and must stay right: a base call whose result is read is written inside its use, once per call in the source, and
    /// NO statement-form copy appears beside it (that would run the call twice — mutants M3 and M8, which a call counter in every row also sees at run time). The count is the
    /// source's own: v5_twice calls it twice in one expression, v7_inline three times (`F(x) * 3`, `F(F(x))`), a Select Case selector once however many Case tests read it.
    /// </summary>
    [TestCase("v1_dim", 1, TestName = "AUsedResult_ADim")]
    [TestCase("v2_local", 1, TestName = "AUsedResult_AnAssignmentToALocal")]
    [TestCase("v3_field", 2, TestName = "AUsedResult_AnAssignmentToAField")]
    [TestCase("v4_param", 1, TestName = "AUsedResult_AnAssignmentToAParameter")]
    [TestCase("v5_twice", 2, TestName = "AUsedResult_TwoCallsInOneExpression")]
    [TestCase("v6_select", 1, TestName = "AUsedResult_ASelectCaseSelector")]
    [TestCase("v6b_selrange", 1, TestName = "AUsedResult_ASelectCaseSelectorOfRangeCases")]
    [TestCase("v7_inline", 3, TestName = "AUsedResult_InlineAndNested")]
    [TestCase("v8_ifcond", 2, TestName = "AUsedResult_AnIfConditionAndAReturn")]
    [TestCase("c3b_lamvalue", 1, TestName = "AUsedResult_InAnExpressionLambda")]
    public void AUsedResult_IsInlinedOnce_AndHasNoStatementCopy(string id, int calls)
    {
        foreach (var (name, emit) in EntryPoints())
        {
            var csharp = emit(Probe(id));
            var lines = Lines(csharp);
            Assert.Multiple(() =>
            {
                Assert.That(lines.Where(l => StatementForm.IsMatch(l)), Is.Empty, $"{id} {name}: a statement-form copy of a call whose result is used:\n{csharp}");
                Assert.That(Count(string.Join("\n", lines), "base."), Is.EqualTo(calls), $"{id} {name}: how many times the source's base calls are written:\n{csharp}");
            });
        }
    }

    // ============================================================================================
    // ADR-0001 is answered FIRST
    // ============================================================================================

    /// <summary>
    /// ⭐ "ADR-0001's materialised value is still answered first": a base call with TWO uses is written ONCE into a temp local (declared at the top of the method, <c>tN = base.F(x);</c>, then <c>tN + tN</c>), before its uses —
    /// not inlined at each use, which would call the base method twice. ⚠ NO SOURCE SHAPE GIVES A BASE CALL TWO USES (measured by the implementer over Select Case ranges and commas, a For bound,
    /// With, Dim-then-reread, and by this task over the 62 programs it compared: the arm placed BEFORE the materialised check, mutant M10, writes the same bytes for every one), so
    /// the IR is EDITED: <c>MyBase.F(x) + 1</c> is built, then the sum's other operand is pointed at the call. This is a hand-edited IR witness, not a program a user can write; it is
    /// the one test that sees M10, and it says what the ordering in <c>ShouldEmitInstruction</c> is for if a future IRBuilder change ever gives a base call a second use.
    /// </summary>
    [Test]
    public void ABaseCallWithTwoUses_IsMaterialisedOnce_BeforeItsUses_ADR0001()
    {
        const string source = """
            Class Base
                Public Function F(n As Integer) As Integer
                    Return n * 2
                End Function
            End Class

            Class Derived
                Inherits Base
                Public Sub Work(x As Integer)
                    Console.WriteLine(MyBase.F(x) + 1)
                End Sub
            End Class

            Sub Main()
                Dim d As New Derived()
                d.Work(4)
            End Sub
            """;

        var module = CSharpTestSupport.BuildModule(source, "Prog.bas");
        var work = module.Classes["Derived"].Methods.Single(m => m.Name == "Work").Implementation;
        IRBaseMethodCall call = null;
        IRBinaryOp sum = null;
        foreach (var block in work.Blocks)
            foreach (var instruction in block.Instructions)
                if (instruction is IRBinaryOp b && (b.Left is IRBaseMethodCall || b.Right is IRBaseMethodCall))
                {
                    sum = b;
                    call = (IRBaseMethodCall)(b.Left is IRBaseMethodCall ? b.Left : b.Right);
                }

        Assert.That(sum, Is.Not.Null, "the program's IR holds `base.F(x) + 1` as an IRBinaryOp over the base call");
        sum.Left = call;
        sum.Right = call;

        var csharp = new ImprovedCSharpCodeGenerator().Generate(module).Replace("\r\n", "\n");
        var lines = Lines(csharp);
        var assigned = lines.Select(l => Regex.Match(l, @"^(\w+) = base\.F\(x\);$")).Where(m => m.Success).ToArray();
        Assert.That(Count(string.Join("\n", lines), "base.F("), Is.EqualTo(1), $"the call is written once:\n{csharp}");
        Assert.That(assigned, Has.Length.EqualTo(1), $"into a temp local (declared at the top of the method, assigned here), as ADR-0001 says:\n{csharp}");
        var temp = assigned[0].Groups[1].Value;
        var use = Array.IndexOf(lines, $"Console.WriteLine({temp} + {temp});");
        Assert.That(use, Is.GreaterThan(Array.FindIndex(lines, l => l == $"{temp} = base.F(x);")), $"read twice through that local, after it is assigned:\n{csharp}");
        Assert.That(RoslynErrors(csharp), Is.Empty, csharp);
    }

    // ============================================================================================
    // A statement is mapped to its own source line
    // ============================================================================================

    /// <summary>
    /// A statement-level base call outside a lambda body is preceded by the `#line` of the source line it is written on (the debugger steps by it; the body of a
    /// bottom-tested loop carries it too). Inside a lambda body no `#line` is written (#136), so those calls are not asked.
    /// </summary>
    [TestCase("s1_sub", TestName = "TheLineOfASubCall")]
    [TestCase("s2_func", TestName = "TheLineOfADiscardedFunctionCall")]
    [TestCase("c1_ctor", TestName = "TheLineOfAConstructorCall")]
    [TestCase("c2_prop", TestName = "TheLineOfAPropertyAccessorCall")]
    [TestCase("c5b_dowhile", TestName = "TheLineOfALoopBodyCall")]
    [TestCase("c7_try", TestName = "TheLineOfATryCatchFinallyCall")]
    public void AStatementLevelBaseCall_IsPrecededByTheLineDirectiveOfItsSourceLine(string id)
    {
        var source = Probe(id);
        var sourceLines = source.Replace("\r\n", "\n").Split('\n');
        foreach (var (name, emit) in EntryPoints())
        {
            var csharp = emit(source);
            var raw = csharp.Split('\n');
            var inLambda = new HashSet<int>(LambdaBodyEmissionShapeTests.Blocks(csharp).SelectMany(b => Enumerable.Range(b.Start + 1, b.End - b.Start - 1)));
            var checkedCalls = 0;
            for (var i = 0; i < raw.Length; i++)
            {
                if (!StatementForm.IsMatch(raw[i].Trim()) || inLambda.Contains(i)) continue;
                var directive = Regex.Match(raw[i - 1], @"^\s*#line (\d+) ");
                Assert.That(directive.Success, Is.True, $"{id} {name}: no #line before `{raw[i].Trim()}`:\n{csharp}");
                var n = int.Parse(directive.Groups[1].Value);
                Assert.That(sourceLines[n - 1], Does.Contain("MyBase."), $"{id} {name}: `{raw[i].Trim()}` is mapped to source line {n}: `{sourceLines[n - 1].Trim()}`");
                checkedCalls++;
            }

            Assert.That(checkedCalls, Is.GreaterThan(0), $"{id} {name}: no statement-level base call found:\n{csharp}");
        }
    }

    // ============================================================================================
    // A lambda
    // ============================================================================================

    /// <summary>
    /// ⭐ A lambda that holds a statement-level base call is a BLOCK lambda: `IsExpressionLambda` asks <c>ShouldEmitInstruction</c> about each instruction before the return, and
    /// the base call now answers "statement". A Function lambda that called the base and returned a value (`MyBase.Bump(10) : Return K`) used to come out as the expression
    /// lambda `() => K`, its call gone. Mutant M9 (the rule not asking about a base call) writes `() => K` here; the Sub lambda is a block either way.
    /// </summary>
    [TestCase("c3_lambda", "base.Bump(1);|base.Bump(10);return K;", TestName = "ASubLambdaAndAFunctionLambda")]
    [TestCase("n1_tempname", "base.F(x);return t0 + t1;", TestName = "AFunctionLambdaBesideTempNamedLocals")]
    public void ALambdaHoldingAStatementLevelBaseCall_IsABlockLambda_AndTheCallIsInIt(string id, string bodies)
    {
        foreach (var (name, emit) in EntryPoints())
        {
            var csharp = emit(Probe(id));
            var blocks = LambdaBodyEmissionShapeTests.Blocks(csharp).Select(b => string.Concat(b.Body)).ToArray();
            Assert.That(blocks, Is.EqualTo(bodies.Split('|')), $"{id} {name}: the block lambdas' bodies:\n{csharp}");
        }
    }

    /// <summary>
    /// A lambda whose only content is a USED base result stays an expression lambda, byte for byte (`f = () => base.Tag(n) + 10;`). Mutant M3a (the arm answers "statement"
    /// for every base call while the visit writes only the statement forms) turns it into a block `{ return base.Tag(n) + 10; }` — the same behaviour, different text, so only
    /// this test sees it.
    /// </summary>
    [Test]
    public void AnExpressionLambda_OfAUsedBaseResult_KeepsItsBytes()
    {
        foreach (var (name, emit) in EntryPoints())
        {
            var csharp = emit(Probe("c3b_lamvalue"));
            Assert.Multiple(() =>
            {
                Assert.That(Lines(csharp), Does.Contain("f = () => base.Tag(n) + 10;"), $"{name}:\n{csharp}");
                Assert.That(LambdaBodyEmissionShapeTests.Blocks(csharp), Is.Empty, $"{name}: a used base result is not a block lambda:\n{csharp}");
            });
        }
    }

    // ============================================================================================
    // Nothing else changes
    // ============================================================================================

    // The CONTROLS: the same shapes with the base call replaced by a call through Me (S/t139/probes/m/*_ctl.bas). They have no base METHOD call, so the fix must not touch one
    // byte of what is written for them (measured: 17,523 of 17,604 cells identical, every difference in a program that calls a MyBase method other than New).
    private const string CtlFunction = """
        Class Base
            Public K As Integer
            Public Overridable Function F(n As Integer) As Integer
                K = K + n
                Console.WriteLine("F " & n)
                Return n * 2
            End Function
            Public Function G(n As Integer) As Integer
                K = K + n
                Console.WriteLine("G " & n)
                Return n * 2
            End Function
        End Class

        Class Derived
            Inherits Base
            Public Overrides Function F(n As Integer) As Integer
                Console.WriteLine("derived F")
                Return 0
            End Function
            Public Sub Work(x As Integer)
                Me.G(x + 1)
                Console.WriteLine("K=" & K)
            End Sub
        End Class

        Sub Main()
            Dim d As New Derived()
            d.Work(4)
        End Sub
        """;

    private const string CtlConstructor = """
        Class Base
            Public K As Integer
            Public Sub New(k0 As Integer)
                K = k0
            End Sub
            Public Overridable Sub Init(n As Integer)
                K = K + n
                Console.WriteLine("init " & n)
            End Sub
            Public Sub Init2(n As Integer)
                K = K + n
                Console.WriteLine("init2 " & n)
            End Sub
        End Class

        Class Derived
            Inherits Base
            Public Sub New()
                MyBase.New(10)
                Me.Init2(5)
                Console.WriteLine("ctor K=" & K)
            End Sub
            Public Overrides Sub Init(n As Integer)
                Console.WriteLine("derived init")
            End Sub
        End Class

        Sub Main()
            Dim d As New Derived()
            Console.WriteLine(d.K)
        End Sub
        """;

    private const string CtlLambda = """
        Class Base
            Public K As Integer
            Public Overridable Sub Bump(n As Integer)
                K = K + n
            End Sub
            Public Sub Bump2(n As Integer)
                K = K + n
            End Sub
        End Class

        Class Derived
            Inherits Base
            Public Overrides Sub Bump(n As Integer)
                Console.WriteLine("derived bump")
            End Sub
            Public Sub Work()
                Dim a As Action = Sub() Me.Bump2(1)
                a()
                a()
                Dim f As Func(Of Integer) = Function()
                                                Me.Bump2(10)
                                                Return K
                                            End Function
                Console.WriteLine(f())
                Console.WriteLine(K)
            End Sub
        End Class

        Sub Main()
            Dim d As New Derived()
            d.Work()
        End Sub
        """;

    /// <summary>The class <paramref name="name"/> from its header to its closing brace, trimmed, without `#line` directives and blank lines.</summary>
    private static string[] ClassText(string csharp, string name)
    {
        var lines = Lines(csharp);
        var start = Array.FindIndex(lines, l => l.StartsWith("public class " + name, StringComparison.Ordinal));
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"no class {name}:\n{csharp}");
        var end = start + 1;
        var depth = 0;
        for (; end < lines.Length; end++)
        {
            if (lines[end] == "{") depth++;
            if (lines[end] == "}" && --depth == 0) break;
        }

        return lines[start..(end + 1)];
    }

    /// <summary>
    /// ⭐ "No other C# text changes for a program without a base method call": the Derived class of three programs whose only difference from a row is `Me.` where the row has
    /// `MyBase.` — a discarded Function result through Me (still `this.G(x + 1);`, a bare call), a constructor after `MyBase.New(10)` (still `: base(10)`, the initialiser, and no
    /// `base.` call), and block lambdas calling through Me — is the text it was on master, line for line. These are the bytes the standard pipeline wrote before the fix.
    /// </summary>
    [TestCase(CtlFunction, "Derived", "public class Derived : @Base|{|public override int F(int n)|{|Console.WriteLine(\"derived F\");|return 0;|}|public void Work(int x)|{|this.G(x + 1);|Console.WriteLine(\"K=\" + K);|}|}",
        TestName = "AProgramWithoutABaseMethodCall_ADiscardedFunctionResultThroughMe")]
    [TestCase(CtlConstructor, "Derived", "public class Derived : @Base|{|public Derived() : base(10)|{|this.Init2(5);|Console.WriteLine(\"ctor K=\" + K);|}|public override void Init(int n)|{|Console.WriteLine(\"derived init\");|}|}",
        TestName = "AProgramWithoutABaseMethodCall_AConstructorAfterMyBaseNew")]
    [TestCase(CtlLambda, "Derived",
        "public class Derived : @Base|{|public override void Bump(int n)|{|Console.WriteLine(\"derived bump\");|}|public void Work()|{|Action a = default!;|Func<int> f = default!;|a = () => {|this.Bump2(1);|};|a();|a();|f = () => {|this.Bump2(10);|return K;|};|Console.WriteLine(f());|Console.WriteLine(K);|}|}",
        TestName = "AProgramWithoutABaseMethodCall_BlockLambdasCallingThroughMe")]
    public void AProgramWithoutABaseMethodCall_IsWrittenAsItWasBeforeTheFix(string source, string cls, string expected)
    {
        foreach (var (name, emit) in EntryPoints())
        {
            var csharp = emit(source);
            Assert.Multiple(() =>
            {
                Assert.That(ClassText(csharp, cls), Is.EqualTo(expected.Split('|')), $"{name}:\n{csharp}");
                Assert.That(BaseLines(csharp), Is.Empty, $"{name}: a program with no MyBase method call writes no `base.` call");
            });
        }
    }

    // ============================================================================================
    // Every program compiles; the known gap is refused by name
    // ============================================================================================

    private static readonly Lazy<ImmutableArray<MetadataReference>> References = new(() =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToImmutableArray());

    /// <summary>Roslyn's error diagnostics for <paramref name="csharp"/> (compiled, never run). Shared with the run fixture and KillVocabularyExtensions' B2 pin.</summary>
    internal static string[] RoslynErrors(string csharp)
    {
        var compilation = CSharpCompilation.Create(
            "MyBaseShape_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(csharp) },
            References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToArray();
    }

    /// <summary>
    /// Roslyn is handed what the compiler writes for every program of <see cref="MyBaseCallProbes"/> (33), through the standard pipeline, the aggressive one and the project
    /// entry point, and must find nothing wrong. Compiled, never run: CS0128 (a redeclared temp), CS0103 (a temp never declared) and CS0165 would be the failures of a visit that
    /// declares the call's result, and CS1620 is the one known refusal (below).
    /// </summary>
    [Test]
    public void TheEmittedCSharp_OfEveryRowInTheTable_Compiles()
    {
        var failures = new List<string>();
        foreach (var probe in MyBaseCallProbes.All)
            foreach (var (name, emit) in EntryPoints())
            {
                var errors = RoslynErrors(emit(probe.Source));
                if (errors.Length > 0) failures.Add($"{probe.Id} {name}: {errors[0]}");
            }

        Assert.That(failures, Is.Empty, string.Join("\n", failures));
    }

    /// <summary>
    /// A ByRef argument to a base method is written with `ref` (#265): `base.SetIt(ref p);`, the call's own ByRef flag read the way an instance call's is, and Roslyn finds nothing wrong. It was
    /// CS1620 ("Argument 1 must be passed with the 'ref' keyword") before the fix, and before #139 wrote the call at all a silent wrong answer (`5` for vbc's `105`). The run fixture
    /// runs it (C#, C++ and MSIL print vbc's `105`; JavaScript refuses ByRef, BL7002).
    /// </summary>
    [TestCase("p1_byref", "base.SetIt(ref p);", TestName = "AByRefArgument_ToAnOverriddenBaseMethod")]
    [TestCase("p1b_byrefinh", "base.SetIt(ref q);", TestName = "AByRefArgument_ToAnInheritedBaseMethod")]
    public void AByRefArgumentToABaseMethod_IsWrittenWithRef_AndCompiles(string id, string call)
    {
        foreach (var (name, emit) in EntryPoints())
        {
            var csharp = emit(Probe(id));
            Assert.That(BaseLines(csharp), Is.EqualTo(new[] { call }), $"{id} {name}: the one base call, with `ref`:\n{csharp}");
            var errors = RoslynErrors(csharp);
            Assert.That(errors, Is.Empty, $"{id} {name}: {string.Join(" | ", errors)}");
        }
    }

    // ============================================================================================
    // THE TABLE'S SHAPE, and the rule: no test here or in the run fixture runs emitted C# in the TEST HOST
    // ============================================================================================

    [Test]
    public void TheTable_HasTheRunFixturesRows()
    {
        Assert.That(Rows.Select(r => r.Id), Is.EqualTo(MyBaseCallProbes.All.Select(p => p.Id)), "this table and the run fixture's table are the same rows in the same order");
        Assert.That(Rows.Select(r => r.Id).Distinct().Count(), Is.EqualTo(Rows.Length));
    }

    /// <summary>
    /// ⛔⛔ Some rows hold loops, and the in-process runner has no timeout: a C# loop that never ends there freezes the whole test host (#256). So the run fixture never calls it:
    /// every C# run is <c>hangSafe: true</c> (<see cref="CSharpProcessRunner"/>). This reads the fixture's code (comments dropped) and fails on a call of the in-process runners and
    /// on a <c>TempExec.Run</c> / <c>AssertMatchesInEveryEntryPoint</c> call that names C# without <c>hangSafe: true</c>. (The controls' call takes a backend VARIABLE;
    /// <c>TheTable_HasItsRows</c> pins that it never holds C#.) This fixture runs nothing.
    /// </summary>
    [Test]
    public void TheRunFixture_NeverRunsEmittedCSharpInTheTestHost()
    {
        var problems = new List<string>();

        // #139's fixture runs C# through TempExec with the literal `Bk.CSharp`; its controls take a backend VARIABLE (TheTable_HasItsRows pins that they never hold C#).
        CheckNeverRunsInProcess("MyBaseMethodCallStatementExecutionTests.cs", @"TempExec\.(?:Run|AssertMatchesInEveryEntryPoint)\(\s*Bk\.CSharp(?<args>[^;]*);", problems);

        // #142/#265/#213's fixture runs every row's backends in a loop over a VARIABLE, so EVERY run call of it must be hang-safe.
        CheckNeverRunsInProcess("MyBaseCallArgumentsExecutionTests.cs", @"TempExec\.(?:Run|AssertMatchesInEveryEntryPoint)\((?<args>[^;]*);", problems);

        Assert.That(problems, Is.Empty, string.Join("\n", problems));
    }

    private static void CheckNeverRunsInProcess(string fileName, string runPattern, List<string> problems)
    {
        var path = Path.Combine(SampleSources.RepoRoot(), "VisualGameStudio.Tests", "Compiler", fileName);
        var code = string.Join("\n", File.ReadAllLines(path).Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        foreach (var call in new[] { "RunEmittedCSharp(", "RunEmittedCSharpText(", "RunEmittedCSharpAggressive(", "RunsOnEveryBackend(", "RunsOnEveryBackendAggressive(" })
            if (code.Contains(call, StringComparison.Ordinal)) problems.Add($"{fileName}: a call of the in-process runner {call}");

        var csharpRuns = Regex.Matches(code, runPattern);
        if (csharpRuns.Count < 1) problems.Add($"{fileName}: the guard must have found the fixture's C# run");
        foreach (Match m in csharpRuns)
            if (!m.Groups["args"].Value.Contains("hangSafe: true", StringComparison.Ordinal))
                problems.Add($"{fileName}: a C# run that is not hang-safe: {m.Value}");
    }
}
