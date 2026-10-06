using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.LSP;
using BasicLang.Compiler.SemanticAnalysis;
using OmniSharp.Extensions.LanguageServer.Protocol;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #217 — VB's BC36641: a lambda parameter that HIDES a local or parameter of the procedure around it is an error, not a silent shadow.
//
//  "Lambda parameter 'x' hides a variable in an enclosing block, a previously defined range variable, or an implicitly declared variable in a query expression." Owner ruling (2026-10-05): the VB way.
//  Before, BasicLang accepted every one of these programs and the lambda's parameter silently won over the outer name. Now `SemanticAnalyzer.CheckLambdaParameterHides` reports it AT THE PARAMETER, as each
//  lambda parameter is declared, and `ReportLambdaLocalsHiddenBy` asks the question again when a local is declared LATER in an enclosing block (VB's block scope is the whole block, so the order of the two
//  declarations does not matter). What counts as hidden: a Dim, a local Const, a For / For Each / Catch variable, a parameter of the procedure (ByVal or ByRef), and a parameter or local of an enclosing
//  lambda. What does not: a class field or property, a module global, a sibling lambda's parameter, a local of a sibling block (none of them is a procedure-local scope that ENCLOSES the lambda).
//
//  ⭐ THE ORACLE IS vbc. Every probe below is one of the implementer's 24 (S/t217/probes, the verdict per probe in S/t217/vbc.txt: the SDK's vbc on the program wrapped in a VB Module). 17 are refused by vbc
//  with BC36641 and 7 are accepted and run; the rows here are those same programs, nothing re-derived from what the analyzer prints. The expected line and column of each refusal is where the PARAMETER NAME
//  starts (1-based), counted off the source text, never read back from the analyzer.
//
//  This fixture is the fast, front-end half: parse + analyze (+ `CompileProjectFiles`, which stops at the combined IR) and the LSP, no process. `LambdaParameterHidesExecutionTests` (Integration, below) RUNS
//  an accepted shape on C#, JavaScript and C++ through the CLI, `--optimize` and `CompileProjectFiles`, so the refusal is shown not to over-reach.
//
//  ⛔ KNOWN GAP — listed, deliberately NOT tested (asserting it would pin a defect): p21, a lambda parameter named like its ENCLOSING FUNCTION (`Function Calc(n)` holding `Function(calc) calc + 1`). vbc refuses
//  it with BC30530 + BC36641 (the function's own name is a local of the function in VB). BasicLang models no function-name local, so it accepts p21 and runs it. Out of scope here.
//
//  ⭐ MUTANTS (each the fix plus ONE change, built from a plain source copy of the working tree and run against a copy of the test output with its BasicLang.dll swapped; each is killed by a committed test):
//    M1  the later-declaration half of `ReportLambdaLocalsHiddenBy` is removed (a local declared BELOW the lambda no longer hides its parameter) ... p06, p14 and p22 (`..._LocalDeclaredAfterTheLambda...`)
//    M2  the lookup crosses the procedure boundary (`lambda.Parent?.Resolve(name)`: a class member and a module global hide too) ................. p07 and p08 (`..._ClassFieldOrProperty...`, `..._ModuleGlobal...`)
//    M3  the match is case-SENSITIVE (`X` no longer hides `x`) ................................................................................... p04 (`..._CaseOnly_...`)
// ================================================================================================

/// <summary>One probe: its source, and the lambda parameters (name, 1-based line, 1-based column) vbc says hide something. An empty list is a program vbc ACCEPTS.</summary>
public sealed record HidingProbe(string Id, string Source, params (string Param, int Line, int Column)[] Hidden)
{
    public override string ToString() => Id;
}

/// <summary>A test case's probes, named for the rows it covers.</summary>
public sealed record HidingGroup(string Id, params HidingProbe[] Probes)
{
    public override string ToString() => Id;
}

[TestFixture]
public class LambdaParameterHidesDiagnosticsTests
{
    private const string BC36641 = "BC36641";

    private static string Message(string parameter) =>
        $"BC36641: Lambda parameter '{parameter}' hides a variable in an enclosing block, a previously defined range variable, or an implicitly declared variable in a query expression.";

    /// <summary>Parse (a parse error fails the test: a typo in a probe must not pass as a refusal) and analyze; every diagnostic, in the analyzer's order.</summary>
    private static List<SemanticError> Analyze(string source)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty, "parse errors:\n" + string.Join("\n", parser.Errors.Select(e => e.Message)));
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);
        return analyzer.Errors.ToList();
    }

    // ============================================================================================
    // The probes — verbatim from S/t217/probes (the file name is the id).
    // ============================================================================================

    private static readonly HidingProbe p01_local = new("p01_local", """
        Sub Main()
            Dim x As Integer = 10
            Dim f As Func(Of Integer, Integer) = Function(x) x + 1
            Console.WriteLine(f(1))
        End Sub
        """, ("x", 3, 51));

    private static readonly HidingProbe p15_const = new("p15_const", """
        Sub Main()
            Const k As Integer = 2
            Dim f As Func(Of Integer, Integer) = Function(k) k * 5
            Console.WriteLine(f(k))
        End Sub
        """, ("k", 3, 51));

    private static readonly HidingProbe p02_byvalparam = new("p02_byvalparam", """
        Sub Run(n As Integer)
            Dim f As Func(Of Integer, Integer) = Function(n) n * 2
            Console.WriteLine(f(n))
        End Sub
        Sub Main()
            Run(3)
        End Sub
        """, ("n", 2, 51));

    private static readonly HidingProbe p03_byrefparam = new("p03_byrefparam", """
        Sub Run(ByRef n As Integer)
            Dim f As Func(Of Integer, Integer) = Function(n) n + 1
            n = f(n)
        End Sub
        Sub Main()
            Dim a As Integer = 1
            Run(a)
            Console.WriteLine(a)
        End Sub
        """, ("n", 2, 51));

    private static readonly HidingProbe p17_multiline_param = new("p17_multiline_param", """
        Sub Main()
            Dim m As Integer = 4
            Dim f As Func(Of Integer, Integer) = Function(m)
                                                     Return m * 2
                                                 End Function
            Console.WriteLine(f(m))
        End Sub
        """, ("m", 3, 51));

    private static readonly HidingProbe p18_secondparam = new("p18_secondparam", """
        Sub Main()
            Dim b As Integer = 4
            Dim f As Func(Of Integer, Integer, Integer) = Function(a, b) a + b
            Console.WriteLine(f(1, 2))
        End Sub
        """, ("b", 3, 63));

    private static readonly HidingProbe p19_sublambda_typed = new("p19_sublambda_typed", """
        Sub Main()
            Dim s As String = "hi"
            Dim f As Action(Of String) = Sub(s As String) Console.WriteLine(s)
            f(s)
        End Sub
        """, ("s", 3, 38));

    /// <summary>`X` against `x`: the spelling differs only in case, and BasicLang (like VB) is case-insensitive.</summary>
    private static readonly HidingProbe p04_casediff = new("p04_casediff", """
        Sub Main()
            Dim X As Integer = 10
            Dim f As Func(Of Integer, Integer) = Function(x) x + 1
            Console.WriteLine(f(1))
        End Sub
        """, ("x", 3, 51));

    /// <summary>The INNER lambda's `a` hides the outer lambda's parameter `a` (the outer's own `a` hides nothing).</summary>
    private static readonly HidingProbe p05_nestedlambda = new("p05_nestedlambda", """
        Sub Main()
            Dim f As Func(Of Integer, Func(Of Integer, Integer)) = Function(a) Function(a) a + 1
            Console.WriteLine(f(1)(2))
        End Sub
        """, ("a", 2, 81));

    /// <summary>A LOCAL of the outer lambda's body, declared before the inner lambda.</summary>
    private static readonly HidingProbe p16_outerlambdalocal = new("p16_outerlambdalocal", """
        Sub Main()
            Dim f As Action = Sub()
                                  Dim w As Integer = 1
                                  Dim g As Func(Of Integer, Integer) = Function(w) w + 1
                                  Console.WriteLine(g(w))
                              End Sub
            f()
        End Sub
        """, ("w", 4, 73));

    /// <summary>⭐ M1. The local `y` is declared BELOW the lambda, in the same block. vbc: the whole block is `y`'s scope, so the parameter hides it.</summary>
    private static readonly HidingProbe p06_localafter = new("p06_localafter", """
        Sub Main()
            Dim f As Func(Of Integer, Integer) = Function(y) y + 1
            Dim y As Integer = 5
            Console.WriteLine(f(y))
        End Sub
        """, ("y", 2, 51));

    /// <summary>⭐ M1. The lambda is inside an `If` block; the local `w` is declared below the `If`, in the block that ENCLOSES the lambda's own.</summary>
    private static readonly HidingProbe p14_localafter_outer = new("p14_localafter_outer", """
        Sub Main()
            Dim t As Integer = 1
            If t > 0 Then
                Dim f As Func(Of Integer, Integer) = Function(w) w + 1
                Console.WriteLine(f(2))
            End If
            Dim w As Integer = 3
            Console.WriteLine(w)
        End Sub
        """, ("w", 4, 55));

    /// <summary>⭐ M1. A lambda inside a lambda; the local `w` is declared below the INNER lambda, in the OUTER lambda's body.</summary>
    private static readonly HidingProbe p22_outerlambda_laterdim = new("p22_outerlambda_laterdim", """
        Sub Main()
            Dim f As Action = Sub()
                                  Dim g As Func(Of Integer, Integer) = Function(w) w + 1
                                  Dim w As Integer = 1
                                  Console.WriteLine(g(w))
                              End Sub
            f()
        End Sub
        """, ("w", 3, 73));

    private static readonly HidingProbe p09_forvar = new("p09_forvar", """
        Sub Main()
            For i As Integer = 1 To 2
                Dim f As Func(Of Integer, Integer) = Function(i) i * 10
                Console.WriteLine(f(i))
            Next
        End Sub
        """, ("i", 3, 55));

    private static readonly HidingProbe p10_foreachvar = new("p10_foreachvar", """
        Sub Main()
            Dim xs As New List(Of Integer)
            xs.Add(1)
            xs.Add(2)
            For Each v As Integer In xs
                Dim f As Func(Of Integer, Integer) = Function(v) v + 100
                Console.WriteLine(f(v))
            Next
        End Sub
        """, ("v", 6, 55));

    private static readonly HidingProbe p11_catchvar = new("p11_catchvar", """
        Sub Main()
            Try
                Throw New Exception("boom")
            Catch ex As Exception
                Dim f As Func(Of String, String) = Function(ex) ex & "!"
                Console.WriteLine(f(ex.Message))
            End Try
        End Sub
        """, ("ex", 5, 53));

    /// <summary>The `Dim` whose OWN initializer holds the lambda: `f` is not initialised yet, and vbc still says the parameter hides it.</summary>
    private static readonly HidingProbe p20_selfinit = new("p20_selfinit", """
        Sub Main()
            Dim f As Func(Of Integer, Integer) = Function(f) f + 1
            Console.WriteLine(f(1))
        End Sub
        """, ("f", 2, 51));

    // ---- accepted by vbc: no BC36641 (and no other diagnostic) ----

    /// <summary>⭐ M2. A Private FIELD `x` of the class; the lambda inside a method of it names its parameter `x`. A field is not a procedure-local scope. vbc runs it: 14.</summary>
    private static readonly HidingProbe p07_field = new("p07_field", """
        Class C
            Private x As Integer = 3
            Public Function Go() As Integer
                Dim f As Func(Of Integer, Integer) = Function(x) x + 1
                Return f(10) + x
            End Function
        End Class
        Sub Main()
            Dim c As New C()
            Console.WriteLine(c.Go())
        End Sub
        """);

    /// <summary>A PROPERTY `P` and a parameter `p` (case-insensitively the same name). vbc runs it: 34.</summary>
    private static readonly HidingProbe p07b_property = new("p07b_property", """
        Class C
            Public Property P As Integer
            Public Sub New()
                P = 4
            End Sub
            Public Function Go() As Integer
                Dim f As Func(Of Integer, Integer) = Function(p) p * 3
                Return f(10) + P
            End Function
        End Class
        Sub Main()
            Dim c As New C()
            Console.WriteLine(c.Go())
        End Sub
        """);

    /// <summary>⭐ M2. A MODULE-LEVEL variable `g` of the file. vbc runs it: 9.</summary>
    private static readonly HidingProbe p08_moduleglobal = new("p08_moduleglobal", """
        Dim g As Integer = 7
        Sub Main()
            Dim f As Func(Of Integer, Integer) = Function(g) g + 1
            Console.WriteLine(f(1) + g)
        End Sub
        """);

    /// <summary>⭐ M2. A `Public` variable `h` of a `Module` block. vbc runs it: 11.</summary>
    private static readonly HidingProbe p08b_modulefield = new("p08b_modulefield", """
        Module Util
            Public h As Integer = 9
        End Module
        Sub Main()
            Dim f As Func(Of Integer, Integer) = Function(h) h + 1
            Console.WriteLine(f(1) + h)
        End Sub
        """);

    /// <summary>Two SIBLING lambdas both name their parameter `z`: neither encloses the other. vbc runs it: 6.</summary>
    private static readonly HidingProbe p12_sibling = new("p12_sibling", """
        Sub Main()
            Dim f As Func(Of Integer, Integer) = Function(z) z + 1
            Dim g As Func(Of Integer, Integer) = Function(z) z * 2
            Console.WriteLine(f(1) + g(2))
        End Sub
        """);

    /// <summary>A local `q` of a SIBLING block (an `If` that has closed) declared BEFORE the lambda. vbc runs it: 1 then 6.</summary>
    private static readonly HidingProbe p13_siblingblock = new("p13_siblingblock", """
        Sub Main()
            If True Then
                Dim q As Integer = 1
                Console.WriteLine(q)
            End If
            Dim f As Func(Of Integer, Integer) = Function(q) q + 1
            Console.WriteLine(f(5))
        End Sub
        """);

    /// <summary>A local `u` of a SIBLING block declared AFTER the lambda: the later-declaration half must ask whether the new local ENCLOSES the lambda, and an `If` block does not. vbc runs it: 6.</summary>
    private static readonly HidingProbe p23_lambdabodylocal_after = new("p23_lambdabodylocal_after", """
        Sub Main()
            Dim f As Func(Of Integer, Integer) = Function(u)
                                                     Return u * 3
                                                 End Function
            If f(1) > 0 Then
                Dim u As Integer = 2
                Console.WriteLine(f(u))
            End If
        End Sub
        """);

    // ============================================================================================
    // The rows — one test case each; a case holding several probes names each in its failure message.
    // ============================================================================================

    private static IEnumerable<TestCaseData> RefusedRows()
    {
        yield return new TestCaseData(new HidingGroup("A_Dim_a_Const_or_a_parameter_of_the_procedure__p01_p15_p02_p03_p17_p18_p19",
            p01_local, p15_const, p02_byvalparam, p03_byrefparam, p17_multiline_param, p18_secondparam, p19_sublambda_typed))
            .SetName("Refused_A_Dim_a_Const_or_a_ByVal_ByRef_parameter_of_the_procedure__p01_p15_p02_p03_p17_p18_p19");
        yield return new TestCaseData(new HidingGroup("CaseOnly_X_against_x__p04", p04_casediff))
            .SetName("Refused_CaseOnly_X_against_x__M3_p04");
        yield return new TestCaseData(new HidingGroup("An_enclosing_lambdas_parameter_or_local__p05_p16", p05_nestedlambda, p16_outerlambdalocal))
            .SetName("Refused_An_enclosing_lambdas_parameter_or_local__p05_p16");
        yield return new TestCaseData(new HidingGroup("A_local_declared_AFTER_the_lambda_in_an_enclosing_block__p06_p14_p22", p06_localafter, p14_localafter_outer, p22_outerlambda_laterdim))
            .SetName("Refused_A_local_declared_AFTER_the_lambda_in_an_enclosing_block__M1_p06_p14_p22");
        yield return new TestCaseData(new HidingGroup("A_For_For_Each_or_Catch_variable__p09_p10_p11", p09_forvar, p10_foreachvar, p11_catchvar))
            .SetName("Refused_A_For_ForEach_or_Catch_variable__p09_p10_p11");
        yield return new TestCaseData(new HidingGroup("The_Dim_whose_own_initializer_holds_the_lambda__p20", p20_selfinit))
            .SetName("Refused_The_Dim_whose_own_initializer_holds_the_lambda__p20");
    }

    private static IEnumerable<TestCaseData> AcceptedRows()
    {
        yield return new TestCaseData(new HidingGroup("A_class_field_or_property__p07_p07b", p07_field, p07b_property))
            .SetName("Accepted_A_class_field_or_property__M2_p07_p07b");
        yield return new TestCaseData(new HidingGroup("A_module_global__p08_p08b", p08_moduleglobal, p08b_modulefield))
            .SetName("Accepted_A_module_global__M2_p08_p08b");
        yield return new TestCaseData(new HidingGroup("A_sibling_lambdas_parameter_or_a_local_of_a_sibling_block__p12_p13_p23", p12_sibling, p13_siblingblock, p23_lambdabodylocal_after))
            .SetName("Accepted_A_sibling_lambdas_parameter_or_a_local_of_a_sibling_block__p12_p13_p23");
    }

    /// <summary>
    /// Each probe is refused with BC36641, and with NOTHING else: exactly the parameters the table names, each once (the analyzer revisits some nodes), at the parameter's own line and column, with VB's message.
    /// </summary>
    [TestCaseSource(nameof(RefusedRows))]
    public void ALambdaParameterThatHidesAnEnclosingName_IsRefused_BC36641_AtTheParameter(HidingGroup group)
    {
        Assert.Multiple(() =>
        {
            foreach (var probe in group.Probes)
            {
                var errors = Analyze(probe.Source);
                var said = string.Join(" | ", errors.Select(e => $"{e.Line}:{e.Column} {e.ErrorCode}"));
                Assert.That(errors.Select(e => e.ErrorCode), Is.All.EqualTo(BC36641), $"{probe.Id}: only BC36641 may be reported; got {said}");
                Assert.That(errors.Select(e => (e.Message, e.Line, e.Column)).ToArray(),
                    Is.EqualTo(probe.Hidden.Select(h => (Message(h.Param), h.Line, h.Column)).ToArray()),
                    $"{probe.Id}: BC36641 at each hiding parameter, once; got {said}");
            }
        });
    }

    /// <summary>
    /// Each probe is ACCEPTED: no BC36641, and no diagnostic of any kind. Every one names a lambda parameter like something that does not enclose the lambda inside its procedure (a field or property, a module
    /// global, a sibling lambda's parameter, a local of a sibling block) and vbc runs it.
    /// </summary>
    [TestCaseSource(nameof(AcceptedRows))]
    public void ALambdaParameterThatHidesNothingInItsProcedure_IsAccepted(HidingGroup group)
    {
        Assert.Multiple(() =>
        {
            foreach (var probe in group.Probes)
            {
                var errors = Analyze(probe.Source);
                Assert.That(errors, Is.Empty, $"{probe.Id}: vbc accepts it; got " + string.Join(" | ", errors.Select(e => $"{e.Line}:{e.Column} {e.ErrorCode} {e.Message}")));
            }
        });
    }

    // ============================================================================================
    // Both entry points, and the LSP
    // ============================================================================================

    /// <summary>
    /// <c>BasicCompiler.CompileProjectFiles</c> (what a .blproj build and the IDE call; it stops at the combined IR, so no process) refuses p01 and the LATER-declared p06 with BC36641 at the parameter, and still
    /// accepts the field shape p07. The single-file rows above never go through it: a project compile analyzes a file inside a unit, a different path from the bare Parser + SemanticAnalyzer pair.
    /// </summary>
    [Test]
    public void TheProjectEntryPoint_RefusesTheSameWay_AndStillAcceptsAField()
    {
        List<SemanticError> ViaProject(string source)
        {
            var dir = Path.Combine(Path.GetTempPath(), "bl-t217-proj-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var path = Path.Combine(dir, "Main.bas");
                File.WriteAllText(path, source);
                return new BasicCompiler(new CompilerOptions { OptimizeAggressive = true }).CompileProjectFiles(new List<string> { path }).AllErrors.ToList();
            }
            finally { try { Directory.Delete(dir, recursive: true); } catch { /* temp */ } }
        }

        Assert.Multiple(() =>
        {
            foreach (var probe in new[] { p01_local, p06_localafter })
            {
                var errors = ViaProject(probe.Source);
                var said = string.Join(" | ", errors.Select(e => $"{e.Line}:{e.Column} {e.ErrorCode}"));
                Assert.That(errors.Select(e => (e.ErrorCode, e.Line, e.Column)).ToArray(),
                    Is.EqualTo(probe.Hidden.Select(h => (BC36641, h.Line, h.Column)).ToArray()),
                    $"{probe.Id} through CompileProjectFiles: BC36641 at the parameter, once; got {said}");
            }
            Assert.That(ViaProject(p07_field.Source), Is.Empty, "p07_field through CompileProjectFiles: a field is not hidden-by-error");
        });
    }

    /// <summary>
    /// The editor sees it too: <c>DocumentManager</c> (which copies <c>analyzer.Errors</c> into its OWN diagnostic list) surfaces BC36641 as an ERROR at the parameter — the same line and column the compiler
    /// reports — and says nothing about the accepted field shape.
    /// </summary>
    [Test]
    public void Lsp_SurfacesBC36641_AsAnError_AtTheParameter()
    {
        DocumentState Analyzed(string source) => new DocumentManager().UpdateDocument(DocumentUri.From("untitled:LambdaParameterHidesProbe.bas"), source);

        var refused = Analyzed(p01_local.Source).Diagnostics.Where(d => d.Message.Contains(BC36641)).ToList();
        Assert.That(refused, Has.Count.EqualTo(1), "expected one BC36641 over LSP; got: " + string.Join(" | ", Analyzed(p01_local.Source).Diagnostics.Select(d => d.Message)));
        Assert.Multiple(() =>
        {
            Assert.That(refused[0].Severity, Is.EqualTo(DiagnosticSeverity.Error));
            Assert.That(refused[0].Message, Does.Contain("Lambda parameter 'x' hides a variable in an enclosing block"));
            Assert.That(refused[0].Line, Is.EqualTo(3), "the parameter's line");
            Assert.That(refused[0].Column, Is.EqualTo(51), "the parameter's column");
            Assert.That(Analyzed(p07_field.Source).Diagnostics.Where(d => d.Message.Contains(BC36641)), Is.Empty, "a field is not hidden-by-error");
        });
    }
}

/// <summary>
/// #217, RUN: the refusal does not over-reach. A lambda parameter named like a class FIELD (p07) and like a MODULE GLOBAL (p08) is accepted by the front end and RUNS — on C#, JavaScript and C++, through the
/// spawned CLI, the CLI with <c>--optimize</c> and <c>CompileProjectFiles</c> — printing what vbc prints (14 and 9). A tool that is missing is skipped, never failed; the test is ignored only when no cell could run.
/// ⚠ Named "…ExecutionTests" and runs JavaScript under Node: it is in <c>JsExecutionTierRosterTests</c>' roster.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the g++, Node and C# child runs share the machine with the spawned CLI
public class LambdaParameterHidesExecutionTests
{
    private const Bk ThreeBackends = Bk.CSharp | Bk.Cpp | Bk.JavaScript;

    [Test]
    public void AParameterNamedLikeAFieldOrAModuleGlobal_StillRuns_AndPrintsVbcsAnswer()
    {
        var probes = new[]
        {
            // vbc's answers: S/t217/probes/p07_field.exp and p08_moduleglobal.exp.
            new TempProbe("p07_field", """
                Class C
                    Private x As Integer = 3
                    Public Function Go() As Integer
                        Dim f As Func(Of Integer, Integer) = Function(x) x + 1
                        Return f(10) + x
                    End Function
                End Class
                Sub Main()
                    Dim c As New C()
                    Console.WriteLine(c.Go())
                End Sub
                """, "14", ThreeBackends, HangSafe: true),
            new TempProbe("p08_moduleglobal", """
                Dim g As Integer = 7
                Sub Main()
                    Dim f As Func(Of Integer, Integer) = Function(g) g + 1
                    Console.WriteLine(f(1) + g)
                End Sub
                """, "9", ThreeBackends, HangSafe: true),
        };

        var failures = new List<string>();
        int ran = 0, skipped = 0;
        foreach (var probe in probes)
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
        if (ran == 0) Assert.Ignore($"no execution tool on this machine ({skipped} cells skipped).");
    }
}
