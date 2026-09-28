using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.CodeGen.MSIL;
using BasicLang.Compiler.SemanticAnalysis;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #169 (plus #199) — ADR-0013's EXECUTION half: real compiled-and-run programs on all four
//  backends. See NameBindingTests.cs for the front-end/IR half and the mechanism this proves.
//
//  Every probe's source and expected string is transcribed VERBATIM from the implementer's
//  scratch probes (measured against this exact working tree — matrix-p2.txt, edge-p2.txt,
//  leak-p2.txt, p1x-p1.txt, e19-p2.txt) and re-verified directly against this build by the
//  test-writer before being pinned here. Expected strings for a C# pin come from the C# text
//  ITSELF (a real Roslyn compile, or a `dotnet run` of the emitted text) — never guessed, and
//  never a non-C# backend's answer used to stand in for C#'s.
// ================================================================================================

[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class NameBindingExecutionTests
{
    // ============================================================================================
    // Shared plumbing
    // ============================================================================================

    /// <summary>Analyze <paramref name="source"/> and assert it reports NO diagnostic at all —
    /// the D2 interim: a lambda parameter shadows its creator's same-spelled local
    /// case-insensitively, and #169 adds no BC36641 (that stays an owner decision, #217).</summary>
    private static void AssertNoDiagnostic(string source)
    {
        var ast = new Parser(new Lexer(source).Tokenize()).Parse();
        var analyzer = new SemanticAnalyzer();
        var ok = analyzer.Analyze(ast);
        Assert.That(ok, Is.True, string.Join(" | ", analyzer.Errors.Select(e => e.Message)));
        Assert.That(analyzer.Errors, Is.Empty,
            "expected NO diagnostic (D2 interim -- shadowing, no BC36641): "
            + string.Join(" | ", analyzer.Errors.Select(e => e.Message)));
    }

    /// <summary>Compile <paramref name="source"/> to C# text (standard pipeline) with Roslyn, and
    /// report whether it FAILS to compile — the "C# pin" idiom for a known codegen defect
    /// (#165's dropped nested-lambda declaration) that this task does not fix.</summary>
    private static (bool Failed, string Diagnostics) CSharpFailsToCompile(string basicLangSource)
    {
        var csharp = ReturnCoercionTests.EmitCSharpForTest(basicLangSource);
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToImmutableArray();
        var compilation = CSharpCompilation.Create(
            "NameBindingPinProbe_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(csharp) },
            references,
            new CSharpCompilationOptions(OutputKind.ConsoleApplication));
        using var ms = new MemoryStream();
        var emitted = compilation.Emit(ms);
        var diagnostics = string.Join("\n", emitted.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()));
        return (!emitted.Success, diagnostics + "\n--- emitted ---\n" + csharp);
    }

    /// <summary>
    /// Task #169's own "Release <c>.blproj</c> leg" — <c>BasicCompiler.CompileProjectFiles</c>
    /// with <c>OptimizeAggressive</c> set (what the CLI's own <c>-c Release</c> maps to), the
    /// SAME entry point the IDE's build service and a multi-file project use, distinct from the
    /// single-file <c>CompileFile</c> path every other helper in this file goes through. One or
    /// more <c>.bas</c> files in, ONE combined (already-optimized) <see cref="IRModule"/> out,
    /// handed to each backend's own generator in turn.
    /// </summary>
    private static string RunViaProjectEntryPoint(string backend, params (string FileName, string Source)[] files)
    {
        var compiler = new BasicCompiler(new CompilerOptions { OptimizeAggressive = true });
        var dir = Path.Combine(Path.GetTempPath(), "bl-namebinding-proj-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var paths = new List<string>();
            foreach (var (fileName, source) in files)
            {
                var p = Path.Combine(dir, fileName);
                File.WriteAllText(p, source);
                paths.Add(p);
            }

            var result = compiler.CompileProjectFiles(paths);
            Assert.That(result.HasErrors, Is.False,
                string.Join(" | ", result.AllErrors.Select(e => e.Message)));
            Assert.That(result.CombinedIR, Is.Not.Null, "the project entry point produced no combined IR");
            var ir = result.CombinedIR;

            return backend switch
            {
                "csharp" => FourBackends.RunEmittedCSharpText(new ImprovedCSharpCodeGenerator().Generate(ir)),
                "cpp" => BclE2E.CompileRun(
                    new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false }).Generate(ir)),
                "javascript" => JavaScriptExecutionTests.RunNodeScript(
                    new BasicLang.Compiler.CodeGen.JavaScript.JavaScriptCodeGenerator().Generate(ir)),
                "msil" => Msil.MsilHarness.RunIlExpectingSuccess(new MSILCodeGenerator().Generate(ir), "T"),
                _ => throw new ArgumentException("unknown backend " + backend),
            };
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* temp */ } }
    }

    /// <summary>Standard + aggressive + a Release <c>.blproj</c> leg on ONE backend
    /// (<c>msil</c> — matching this suite's existing <c>BuildReleaseMsilAndRun</c> convention,
    /// <c>MeReceiverTypingExecutionTests</c>/<c>MsilValueToStringExecutionTests</c>) for a
    /// single-file probe: the two pipelines through <see cref="FourBackends"/>, plus the project
    /// entry point through <see cref="RunViaProjectEntryPoint"/>.</summary>
    private static void RunsEverywhereAndThroughTheProjectEntryPoint(string source, string expected)
    {
        FourBackends.RunsOnEveryBackend(source, expected);
        FourBackends.RunsOnEveryBackendAggressive(source, expected);
        Assert.That(FourBackends.Norm(RunViaProjectEntryPoint("msil", ("Main.bas", source))),
            Is.EqualTo(expected), "Release .blproj leg (MSIL, CompileProjectFiles)");
    }

    // ============================================================================================
    // Headline K-probes (S/t169/probes) — K2, K3, K8, K10. All four backends, both pipelines,
    // plus a Release .blproj leg.
    // ============================================================================================

    private const string K2 = """
        Sub Main()
            Dim f = Function(N As Integer) n * 2
            Console.WriteLine(f(21))
        End Sub
        """;

    [Test]
    public void K2_LambdaParamCaseDiffer_PrintsFortyTwo()
        => RunsEverywhereAndThroughTheProjectEntryPoint(K2, "42");

    private const string K3 = """
        Sub Main()
            Dim g = Sub(Value As Integer) Console.WriteLine(value + 1)
            g(5)
        End Sub
        """;

    [Test]
    public void K3_LambdaParamCaseDiffer_PrintsSix()
        => RunsEverywhereAndThroughTheProjectEntryPoint(K3, "6");

    private const string K8 = """
        Class Box
            Private n As Integer = 1
            Public Function Run() As Integer
                Dim f = Function(N As Integer) n * 10
                Return f(4)
            End Function
        End Class
        Sub Main()
            Dim b As New Box()
            Console.WriteLine(b.Run())
        End Sub
        """;

    /// <summary>K8 — the field/parameter split (D1's motivating repro). Pinned BY NAME: before
    /// #169 this silently printed 10 (the field won); the fix makes it print 40. Never a 10.</summary>
    [Test]
    public void K8_LambdaParamShadowsField_PrintsFortyNotSilentTen()
        => RunsEverywhereAndThroughTheProjectEntryPoint(K8, "40");

    private const string K10 = """
        Sub Main()
            Dim items As New List(Of Integer)()
            items.Add(3)
            items.Add(4)
            Dim sum As Integer = 0
            For Each Item As Integer In items
                Dim f = Function(k As Integer) k + item
                sum = sum + f(10)
            Next
            Console.WriteLine(sum)
        End Sub
        """;

    [Test]
    public void K10_ForEachControlVariableCaseDiffer_ReadInsideALambda_PrintsTwentySeven()
        => RunsEverywhereAndThroughTheProjectEntryPoint(K10, "27");

    // ============================================================================================
    // K1 / K6 / K7 — the D2 interim: shadowing, no diagnostic. Standard + aggressive.
    // ============================================================================================

    private const string K1 = """
        Function Seed(v As Integer) As Integer
            Return v
        End Function
        Sub Main()
            Dim n As Integer = Seed(1)
            Dim setp = Sub(N As Integer) n = n + 100
            setp(5)
            Console.WriteLine(n)
        End Sub
        """;

    [Test]
    public void K1_LambdaParamShadowsCreatorLocal_CreatorUntouched_NoDiagnostic()
    {
        AssertNoDiagnostic(K1);
        FourBackends.RunsOnEveryBackend(K1, "1");
        FourBackends.RunsOnEveryBackendAggressive(K1, "1");
    }

    private const string K6 = """
        Sub Run(n As Integer)
            Dim f = Sub(N As Integer) Console.WriteLine(n)
            f(7)
        End Sub
        Sub Main()
            Run(3)
        End Sub
        """;

    [Test]
    public void K6_LambdaParamShadowsCreatorParameter_ReadsItsOwnParameter_NoDiagnostic()
    {
        AssertNoDiagnostic(K6);
        FourBackends.RunsOnEveryBackend(K6, "7");
        FourBackends.RunsOnEveryBackendAggressive(K6, "7");
    }

    private const string K7 = """
        Function Seed(v As Integer) As Integer
            Return v
        End Function
        Sub Main()
            Dim n As Integer = Seed(1)
            Dim setp = Sub(n As Integer) n = n + 100
            setp(5)
            Console.WriteLine(n)
        End Sub
        """;

    /// <summary>K7 — the EXACT-spelling sibling of K1 (no case difference at all): already
    /// shadowed before #169, unaffected by it, kept here as the control that proves K1's answer
    /// is VB's shadowing rule and not a coincidence of this fix.</summary>
    [Test]
    public void K7_ExactSpellingShadow_Control_NoDiagnostic()
    {
        AssertNoDiagnostic(K7);
        FourBackends.RunsOnEveryBackend(K7, "1");
        FourBackends.RunsOnEveryBackendAggressive(K7, "1");
    }

    // ============================================================================================
    // Leak probes (S/t176/leak) K1-K6 — #199's own corpus, re-run here as part of #169's gate.
    // ============================================================================================

    private const string LeakK1 = """
        Class A
            Public Function F() As String
                Dim n As String = "s"
                Return n
            End Function
        End Class

        Class B
            Private n As Integer
            Public Function G() As Integer
                n = 4
                Return n + 1
            End Function
        End Class

        Sub Main()
            Console.WriteLine(New A().F())
            Console.WriteLine(New B().G())
        End Sub
        """;

    [Test]
    public void Leak_K1_LocalVsField()
    {
        FourBackends.RunsOnEveryBackend(LeakK1, "s\n5");
        FourBackends.RunsOnEveryBackendAggressive(LeakK1, "s\n5");
    }

    private const string LeakK2 = """
        Class A
            Private k As Integer
            Public Sub New(n As String)
                k = n.Length
            End Sub
            Public Function K2() As Integer
                Return k
            End Function
        End Class

        Class B
            Private n As Integer
            Public Function G() As Integer
                n = 4
                Return n + 1
            End Function
        End Class

        Sub Main()
            Console.WriteLine(New A("abc").K2())
            Console.WriteLine(New B().G())
        End Sub
        """;

    [Test]
    public void Leak_K2_ConstructorParamVsField()
    {
        FourBackends.RunsOnEveryBackend(LeakK2, "3\n5");
        FourBackends.RunsOnEveryBackendAggressive(LeakK2, "3\n5");
    }

    private const string LeakK3 = """
        Sub A()
            Dim i As String = "z"
            Console.WriteLine(i)
        End Sub

        Sub B()
            For i = 1 To 3
                Console.WriteLine(i)
            Next
        End Sub

        Sub Main()
            A()
            B()
        End Sub
        """;

    [Test]
    public void Leak_K3_ForAfterLocal()
    {
        FourBackends.RunsOnEveryBackend(LeakK3, "z\n1\n2\n3");
        FourBackends.RunsOnEveryBackendAggressive(LeakK3, "z\n1\n2\n3");
    }

    private const string LeakK4 = """
        Dim g As Integer = 5

        Sub A()
            Dim g As String = "x"
            Console.WriteLine(g)
        End Sub

        Sub Main()
            A()
            Console.WriteLine(g)
        End Sub
        """;

    [Test]
    public void Leak_K4_GlobalAfterLocal()
    {
        FourBackends.RunsOnEveryBackend(LeakK4, "x\n5");
        FourBackends.RunsOnEveryBackendAggressive(LeakK4, "x\n5");
    }

    private const string LeakK5 = """
        Sub A()
            Dim Box As Integer = 9
            Console.WriteLine(Box)
        End Sub

        Class Box
            Private Shared _t As Integer
            Public Shared Property T As Integer
                Get
                    Return _t
                End Get
                Set(value As Integer)
                    _t = value * 3
                End Set
            End Property
            Public Function Probe() As Integer
                T = 2
                Return T
            End Function
        End Class

        Sub Main()
            A()
            Console.WriteLine(New Box().Probe())
        End Sub
        """;

    [Test]
    public void Leak_K5_LocalNamedClass()
    {
        FourBackends.RunsOnEveryBackend(LeakK5, "9\n6");
        FourBackends.RunsOnEveryBackendAggressive(LeakK5, "9\n6");
    }

    private const string LeakK6 = """
        Class Animal
            Public Overridable ReadOnly Property Sound As String
                Get
                    Return "..."
                End Get
            End Property
            Public Function Speak() As String
                Return Me.Sound & "!"
            End Function
        End Class

        Class Base
            Private _v As Integer
            Public Property V As Integer
                Get
                    Return _v
                End Get
                Set(value As Integer)
                    _v = value * 10
                End Set
            End Property
        End Class

        Class Derived
            Inherits Base
            Public Function Probe() As Integer
                MyBase.V = 2
                Return MyBase.V
            End Function
        End Class

        Sub Main()
            Console.WriteLine(New Derived().Probe())
        End Sub
        """;

    [Test]
    public void Leak_K6_MyBase()
    {
        FourBackends.RunsOnEveryBackend(LeakK6, "20");
        FourBackends.RunsOnEveryBackendAggressive(LeakK6, "20");
    }

    // ============================================================================================
    // X1 (S/t169/p1x) — two Subs, each using an undeclared bare `For i` (#199's own scoping,
    // exercised again here since #169 builds directly on it).
    // ============================================================================================

    private const string X1 = """
        Sub A()
            For i = 1 To 2
                Console.WriteLine(i)
            Next
        End Sub

        Sub B()
            For i = 5 To 6
                Console.WriteLine(i)
            Next
        End Sub

        Sub Main()
            A()
            B()
        End Sub
        """;

    [Test]
    public void X1_TwoSubsEachUsingAnUndeclaredForI()
    {
        FourBackends.RunsOnEveryBackend(X1, "1\n2\n5\n6");
        FourBackends.RunsOnEveryBackendAggressive(X1, "1\n2\n5\n6");
    }

    // ============================================================================================
    // Edge probes (S/t169/edge) that now run everywhere. One test per probe, standard +
    // aggressive; the .exp answer transcribed verbatim.
    // ============================================================================================

    [Test]
    public void E01_LocalCase()
        => RunEdge("""
            Sub Main()
                Dim Total As Integer = 0
                total += 1
                TOTAL = total + 10
                Console.WriteLine(Total)
            End Sub
            """, "11");

    [Test]
    public void E02_ParamCase()
        => RunEdge("""
            Sub Show(Value As Integer)
                value = value + 1
                Console.WriteLine(VALUE * 2)
            End Sub
            Sub Main()
                Show(20)
            End Sub
            """, "42");

    [Test]
    public void E03_NestedCurried()
        => RunEdge("""
            Sub Main()
                Dim f = Function(A As Integer) Function(B As Integer) a * 10 + b
                Console.WriteLine(f(1)(2))
            End Sub
            """, "12");

    [Test]
    public void E05_LambdaVsModuleGlobal()
        => RunEdge("""
            Dim g As Integer = 5
            Sub Main()
                Dim f = Function(G As Integer) g + 1
                Console.WriteLine(f(10))
                Console.WriteLine(g)
            End Sub
            """, "11\n5");

    [Test]
    public void E06_LambdaVsProperty()
        => RunEdge("""
            Class Box
                Private _size As Integer = 3
                Public Property Size As Integer
                    Get
                        Return _size
                    End Get
                    Set(value As Integer)
                        _size = value
                    End Set
                End Property
                Public Function Run() As Integer
                    Dim f = Function(size As Integer) Size * 2
                    Return f(10) + Me.Size
                End Function
            End Class
            Sub Main()
                Console.WriteLine(New Box().Run())
            End Sub
            """, "23");

    [Test]
    public void E09_SetterNamedParam()
        => RunEdge("""
            Class Box
                Private _v As Integer
                Public Property V As Integer
                    Get
                        Return _v
                    End Get
                    Set(NewValue As Integer)
                        _v = newvalue * 3
                    End Set
                End Property
            End Class
            Sub Main()
                Dim b As New Box()
                b.V = 2
                Console.WriteLine(b.V)
            End Sub
            """, "6");

    [Test]
    public void E10_ForeachCase()
        => RunEdge("""
            Sub Main()
                Dim items As New List(Of Integer)()
                items.Add(3)
                items.Add(4)
                Dim sum As Integer = 0
                For Each Item As Integer In items
                    sum = sum + item
                Next
                Console.WriteLine(sum)
            End Sub
            """, "7");

    [Test]
    public void E11_ForeachReuseCase()
        => RunEdge("""
            Sub Main()
                Dim items As New List(Of Integer)()
                items.Add(3)
                items.Add(4)
                Dim Item As Integer = 0
                Dim sum As Integer = 0
                For Each item In items
                    sum = sum + ITEM
                Next
                Console.WriteLine(sum)
                Console.WriteLine(Item)
            End Sub
            """, "7\n4");

    [Test]
    public void E12_ForGlobalCase()
        => RunEdge("""
            Dim g As Integer = 0
            Sub Bump()
                For g = 1 To 3
                    Console.WriteLine(G)
                Next
            End Sub
            Sub Main()
                Bump()
                Console.WriteLine(g)
            End Sub
            """, "1\n2\n3\n4");

    [Test]
    public void E13_ForDeclCase()
        => RunEdge("""
            Sub Main()
                Dim s As Integer = 0
                For I As Integer = 1 To 3
                    s = s + i
                Next
                Console.WriteLine(s)
            End Sub
            """, "6");

    [Test]
    public void E14_CatchCase()
        => RunEdge("""
            Sub Main()
                Try
                    Throw New Exception("boom")
                Catch Ex As Exception
                    Console.WriteLine(ex.Message)
                End Try
            End Sub
            """, "boom");

    [Test]
    public void E15_ConstCase()
        => RunEdge("""
            Sub Main()
                Const Limit As Integer = 3
                Console.WriteLine(LIMIT * 2)
            End Sub
            """, "6");

    /// <summary>E16 — a lambda parameter (<c>total</c>) hides the creator's local
    /// (<c>Total</c>) with the SAME case shape as N4b/K1: read and write both stay inside the
    /// lambda's own parameter, and the creator's <c>Total</c> is untouched afterward.</summary>
    [Test]
    public void E16_LambdaParamHidesLocal_SameCaseInLambda()
    {
        const string source = """
            Sub Main()
                Dim Total As Integer = 100
                Dim f = Function(total As Integer) total + 1
                Console.WriteLine(f(5))
                Console.WriteLine(Total)
            End Sub
            """;
        AssertNoDiagnostic(source);
        FourBackends.RunsOnEveryBackend(source, "6\n100");
        FourBackends.RunsOnEveryBackendAggressive(source, "6\n100");
    }

    private static void RunEdge(string source, string expected)
    {
        FourBackends.RunsOnEveryBackend(source, expected);
        FourBackends.RunsOnEveryBackendAggressive(source, expected);
    }

    // ============================================================================================
    // The multi-file .blproj case (S/t169/edge-mf/E19) — module global Counter vs local counter,
    // through the PROJECT entry point, on all four backends. Mandatory per the brief: this is
    // the one shape in this file that NEEDS the project entry point rather than a single .bas.
    // ============================================================================================

    private const string E19Main = """
        Sub Main()
            Dim counter As Integer = 1
            counter = COUNTER + 1
            Console.WriteLine(Counter)
            Show()
        End Sub
        """;

    private const string E19Data = """
        Module Data
            Public Counter As Integer = 7
            Public Sub Show()
                Console.WriteLine(counter)
            End Sub
        End Module
        """;

    [TestCase("csharp")]
    [TestCase("cpp")]
    [TestCase("javascript")]
    [TestCase("msil")]
    public void E19_MultiFileModuleGlobalVsLocal_ThroughTheProjectEntryPoint(string backend)
    {
        var output = RunViaProjectEntryPoint(backend, ("Main.bas", E19Main), ("Data.bas", E19Data));
        Assert.That(FourBackends.Norm(output), Is.EqualTo("2\n7"));
    }

    // ============================================================================================
    // Pins -- each a KNOWN, PRE-EXISTING backend defect #169 does not fix, named to its own task.
    // ============================================================================================

    /// <summary>
    /// K4 on C# prints 1 for 2 -- widened #136: the C# backend's statement-lambda emitter DROPS
    /// the body's self-assignment to its own parameter entirely. Measured directly: the emitted
    /// C# for <c>Dim h = Sub(X As Integer) : x = x + 1 : Console.WriteLine(x) : End Sub</c>
    /// contains NO assignment to <c>X</c> at all -- only the <c>Console.WriteLine(X)</c> survives
    /// -- so <c>h(1)</c> prints the untouched parameter, 1. C++/JavaScript/MSIL all print 2
    /// correctly (matrix-p2.txt). Unrelated to #169 -- a C# backend defect on the WRITTEN
    /// spelling matching the parameter EXACTLY, no case difference involved.
    /// </summary>
    [Test]
    public void K4_CSharp_DropsTheLambdasWriteToItsOwnParameter_PinsTodaysWrongOne_Against136()
    {
        const string k4 = """
            Sub Main()
                Dim h = Sub(X As Integer)
                            x = x + 1
                            Console.WriteLine(x)
                        End Sub
                h(1)
            End Sub
            """;
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(k4)), Is.EqualTo("1"),
                "known gap #136 (widened): C# drops the write, so h(1) prints the untouched parameter");
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(k4))), Is.EqualTo("2"), "C++");
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(k4)), Is.EqualTo("2"), "JavaScript");
            Assert.That(FourBackends.Norm(Msil.MsilHarness.RunExpectingSuccess(k4)), Is.EqualTo("2"), "MSIL");
        });
    }

    /// <summary>
    /// K9 on C++ prints 0 for 7 -- #140: the C++ backend's lambda lowering captures its creator's
    /// locals BY COPY, not by reference, so writes inside the lambda (<c>total = total + amount</c>)
    /// never reach the creator's own storage. C#/JavaScript/MSIL all print 7 correctly. Unrelated
    /// to #169 -- no case difference in this probe at all.
    /// </summary>
    [Test]
    public void K9_Cpp_CapturesByCopy_PinsTodaysWrongZero_Against140()
    {
        const string k9 = """
            Sub Main()
                Dim total As Integer = 0
                Dim add = Sub(Amount As Integer) total = total + amount
                add(3)
                add(4)
                Console.WriteLine(total)
            End Sub
            """;
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(k9)), Is.EqualTo("7"), "C#");
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(k9)), Is.EqualTo("7"), "JavaScript");
            Assert.That(FourBackends.Norm(Msil.MsilHarness.RunExpectingSuccess(k9)), Is.EqualTo("7"), "MSIL");
        });

        var cpp = BclE2E.CompileToCppOptimized(k9);
        Assert.That(FourBackends.Norm(BclE2E.CompileRun(cpp)), Is.EqualTo("0"),
            "known gap #140 (C++ captures by copy): total never sees add()'s writes");
    }

    /// <summary>
    /// K5 on C# fails to compile with CS0103 on 'inner' -- #165: a multi-line
    /// <c>Function(...) As Integer ... End Function</c> lambda that declares its OWN nested
    /// lambda local is emitted with the nested declaration DROPPED (a bare <c>;</c>), so every
    /// later reference to <c>inner</c> is undefined. C++/JavaScript/MSIL all print 3 correctly
    /// (K5.exp). Unrelated to #169 -- a pre-existing C# multi-line-lambda-body scoping gap.
    /// </summary>
    [Test]
    public void K5_CSharp_DropsTheNestedLambdaDeclaration_PinsTodaysCS0103_Against165()
    {
        const string k5 = """
            Sub Main()
                Dim outer = Function(A As Integer) As Integer
                                Dim inner = Function(b As Integer) a + b
                                Return inner(2)
                            End Function
                Console.WriteLine(outer(1))
            End Sub
            """;
        var (failed, diagnostics) = CSharpFailsToCompile(k5);
        Assert.That(failed, Is.True, "expected C# to fail to compile (known gap #165):\n" + diagnostics);
        Assert.That(diagnostics, Does.Contain("CS0103").And.Contain("'inner'"), diagnostics);

        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(k5))), Is.EqualTo("3"), "C++");
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(k5)), Is.EqualTo("3"), "JavaScript");
            Assert.That(FourBackends.Norm(Msil.MsilHarness.RunExpectingSuccess(k5)), Is.EqualTo("3"), "MSIL");
        });
    }

    /// <summary>
    /// E17 — LINQ (<c>From ... Where ... Select</c>) is broken EVERYWHERE (a pre-existing gap,
    /// #224), but #169's own obligation is narrower and MUST hold regardless: the front end
    /// accepts the program and the IR BUILDS without ever throwing the #169 internal compiler
    /// error. Measured directly (front end + <c>IRBuilder.Build</c> alone, no backend): analysis
    /// succeeds with zero errors and the IR builds with zero exceptions; every backend's own
    /// FAILURE happens downstream, in that backend's own codegen or at run time, never inside
    /// <c>ReferencedVariable</c>'s bound-miss check.
    /// </summary>
    [Test]
    public void E17_Linq_BrokenEverywhere_ButNeverAnInternalCompilerError()
    {
        const string e17 = """
            Sub Main()
                Dim xs As New List(Of Integer)()
                xs.Add(1)
                xs.Add(5)
                Dim q = From X In xs Where x > 2 Select x
                For Each y In q
                    Console.WriteLine(y)
                Next
            End Sub
            """;
        var ast = new Parser(new Lexer(e17).Tokenize()).Parse();
        var analyzer = new SemanticAnalyzer();
        Assert.That(analyzer.Analyze(ast), Is.True,
            "front end must accept this program: " + string.Join(" | ", analyzer.Errors.Select(e => e.Message)));

        IRModuleOrThrow(ast, analyzer, out var threw, out var message);
        Assert.That(threw, Is.False, "the IR must build without throwing at all: " + message);
        Assert.That(message, Does.Not.Contain("Internal compiler error"));
        Assert.That(message, Does.Not.Contain("#169"));
    }

    private static void IRModuleOrThrow(ProgramNode ast, SemanticAnalyzer analyzer, out bool threw, out string message)
    {
        try
        {
            new BasicLang.Compiler.IR.IRBuilder(analyzer).Build(ast, "T");
            threw = false;
            message = "";
        }
        catch (Exception ex)
        {
            threw = true;
            message = ex.Message;
        }
    }

    /// <summary>
    /// E18 — <c>For total = 1 To 3</c> over a class field declared <c>Total</c> (case-differing):
    /// this is a counted <c>For</c> loop's own control-variable RESOLUTION decision, which
    /// ADR-0013 D6 leaves entirely to #124 (not #169's identifier-reference consuming site).
    /// JavaScript's own symptom is the cleanest to pin precisely: it silently prints 0 instead of
    /// 4 (the field is never touched; a SEPARATE, undeclared <c>total</c> is created and
    /// discarded). C#/C++ fail to COMPILE (an undeclared identifier 'total'); MSIL throws
    /// <c>MissingFieldException: 'C.total'</c> at run time (edge-p2.txt) -- none of these are
    /// #169's ICE (a compile-time front-end/codegen failure and a .NET reflection exception are
    /// both categorically different from <c>ReferencedVariable</c>'s own thrown message).
    /// </summary>
    [Test]
    public void E18_ForFieldCase_PinsTodaysWrongZeroOnJavaScript_Against124()
    {
        const string e18 = """
            Class C
                Private Total As Integer
                Public Function Run() As Integer
                    For total = 1 To 3
                    Next
                    Return Total
                End Function
            End Class
            Sub Main()
                Console.WriteLine(New C().Run())
            End Sub
            """;
        Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(e18)), Is.EqualTo("0"),
            "known gap #124 (D6): the For loop's control-variable resolution, not #169's identifier-reference site");

        var (failed, _) = CSharpFailsToCompile(e18);
        Assert.That(failed, Is.True, "C# is also known to fail to compile this shape (#124)");
    }
}
