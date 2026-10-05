using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.CodeGen.JavaScript;
using BasicLang.Compiler.CodeGen.MSIL;
using VisualGameStudio.Tests.Native;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task #188 (fix commit <c>5e82a786</c>) — execution. F0-F9 and the G/J edge probes RUN correctly
/// on all four backends, on both the standard and aggressive optimizer pipelines, plus the
/// <c>BasicCompiler.CompileProjectFiles</c> entry point a Release <c>.blproj</c> build uses — the
/// same second entry point <see cref="UserDelegateConversionExecutionTests"/> covers for #187
/// (CLAUDE.md: "the IDE build delegates to the CLI engine; a fix verified only through the test
/// helper can still break via the IDE or the CLI"). Expected strings are taken verbatim from the
/// implementer's measured <c>.exp</c> files (<c>S/t188/probes/*.exp</c>, <c>S/t188/edge/*.exp</c>)
/// — the VB/C# answer — never copied from a non-C# backend.
///
/// <para><see cref="UserDelegateConversionExecutionTests"/> already covers J1/J2 (a user-Delegate
/// field, dot-<c>Invoke</c>d and plain-called) and E10 (a field invoked bare inside its own class);
/// they are not repeated here. J2f below is the SAME plain-call shape over a plain <c>Action(Of
/// String)</c> field rather than a user Delegate — #188's point that the gap was never specific to
/// a user Delegate type.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // several legs redirect Console.Out
public class DelegateMemberInvocationExecutionTests
{
    // ============================================================================================
    // 0. Helpers.
    // ============================================================================================

    private static string Norm(string s) => FourBackends.Norm(s);

    /// <summary>
    /// <c>BasicCompiler.CompileProjectFiles</c> with <c>OptimizeAggressive = true</c> — what a
    /// Release <c>.blproj</c> build requests. Mirrors
    /// <c>UserDelegateConversionExecutionTests.RunThroughProjectEntryPoint</c>.
    /// </summary>
    private static string RunThroughProjectEntryPoint(string source)
    {
        var dir = Path.Combine(Path.GetTempPath(), "BasicLang_T188ProjectEntry_" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "prog.bas");
            File.WriteAllText(file, source);

            var compiler = new BasicCompiler(new CompilerOptions { OptimizeAggressive = true });
            var result = compiler.CompileProjectFiles(new[] { file });

            Assert.That(result.HasErrors, Is.False,
                string.Join(" | ", result.AllErrors.Select(e => e.Message)));
            Assert.That(result.CombinedIR, Is.Not.Null, "CompileProjectFiles produced no combined IR");

            return FourBackends.RunEmittedCSharpText(
                new ImprovedCSharpCodeGenerator().Generate(result.CombinedIR));
        }
        finally { try { Directory.Delete(dir, true); } catch { /* temp */ } }
    }

    // ============================================================================================
    // 1. F0-F9 — the implementer's own matrix, every cell now OK. All four backends, both
    //    pipelines, plus the Release .blproj entry point.
    // ============================================================================================

    private const string F0 = """
        Class Holder
            Public Callback As Action = Nothing
            Public Sub Fire()
                Select Case Callback
                    Case Is Nothing
                        Console.WriteLine("unset")
                    Case Else
                        Callback()
                End Select
            End Sub
        End Class

        Sub Main()
            Dim h As New Holder()
            h.Fire()
            h.Callback = Sub() Console.WriteLine("fired")
            h.Fire()
        End Sub
        """;

    private const string F1 = """
        Class Holder
            Public Callback As Action
            Public Sub Fire()
                Callback()
            End Sub
        End Class

        Sub Main()
            Dim h As New Holder()
            h.Callback = Sub() Console.WriteLine("fired")
            h.Fire()
        End Sub
        """;

    private const string F2 = """
        Class Holder
            Public Callback As Action
            Public Sub Fire()
                Me.Callback()
            End Sub
        End Class

        Sub Main()
            Dim h As New Holder()
            h.Callback = Sub() Console.WriteLine("fired")
            h.Fire()
        End Sub
        """;

    private const string F3 = """
        Class Calc
            Public Op As Func(Of Integer, Integer)
            Public Function Run(n As Integer) As Integer
                Return Op(n) + 1
            End Function
        End Class

        Sub Main()
            Dim c As New Calc()
            c.Op = Function(x As Integer) x * 10
            Console.WriteLine(c.Run(4))
        End Sub
        """;

    private const string F4 = """
        Delegate Sub Notify(msg As String)

        Class Bus
            Public Handler As Notify
            Public Sub Send(text As String)
                Handler(text)
            End Sub
        End Class

        Sub Main()
            Dim b As New Bus()
            b.Handler = Sub(m As String) Console.WriteLine("got " & m)
            b.Send("hi")
        End Sub
        """;

    private const string F5 = """
        Class Holder
            Public Property Callback As Action
            Public Sub Fire()
                Callback()
            End Sub
        End Class

        Sub Main()
            Dim h As New Holder()
            h.Callback = Sub() Console.WriteLine("prop fired")
            h.Fire()
        End Sub
        """;

    private const string F6 = """
        Class Holder
            Public Callback As Action
            Public Function Wrap() As Action
                Return Sub() Callback()
            End Function
        End Class

        Sub Main()
            Dim h As New Holder()
            h.Callback = Sub() Console.WriteLine("wrapped")
            Dim w As Action = h.Wrap()
            w()
        End Sub
        """;

    private const string F7 = """
        Class Registry
            Public Shared Hook As Action
            Public Shared Sub Trigger()
                Hook()
            End Sub
        End Class

        Sub Main()
            Registry.Hook = Sub() Console.WriteLine("shared hook")
            Registry.Trigger()
        End Sub
        """;

    private const string F8 = """
        Class Calc
            Public Op As Func(Of Integer, Integer)
        End Class

        Sub Main()
            Dim c As New Calc()
            c.Op = Function(x As Integer) x + 100
            Console.WriteLine(c.Op(5))
        End Sub
        """;

    private const string F9 = """
        Class BaseH
            Public Callback As Action
        End Class

        Class Child
            Inherits BaseH
            Public Sub Fire()
                Callback()
            End Sub
        End Class

        Sub Main()
            Dim c As New Child()
            c.Callback = Sub() Console.WriteLine("inherited")
            c.Fire()
        End Sub
        """;

    // ============================================================================================
    // 2. The edge probes that run everywhere (S/t188/edge/*.exp). J1/J2/J2/E10 live in
    //    UserDelegateConversionExecutionTests already; not repeated.
    // ============================================================================================

    private const string G1 = """
        Class Calc
            Public Op As Func(Of Integer, Integer)
            Public Function Run(n As Integer) As Integer
                Return Op(n) * 2 + Me.Op(1) - Op(Op(0))
            End Function
        End Class

        Sub Main()
            Dim c As New Calc()
            c.Op = Function(x As Integer) x + 3
            Console.WriteLine(c.Run(4))
            Dim total As Integer = c.Op(10) + c.Op(20)
            Console.WriteLine(total)
            If c.Op(1) > 3 Then Console.WriteLine("gt")
        End Sub
        """;

    private const string G2 = """
        Class Bus
            Public Handler As Action(Of Integer, Integer)
            Public Function Twice(n As Integer) As Integer
                Console.WriteLine("twice " & n)
                Return n * 2
            End Function
            Public Sub Send()
                Handler(Twice(2), Me.Twice(3))
            End Sub
        End Class

        Function Trace(n As Integer) As Integer
            Console.WriteLine("trace " & n)
            Return n + 1
        End Function

        Sub Main()
            Dim b As New Bus()
            b.Handler = Sub(a As Integer, c As Integer) Console.WriteLine("got " & a & "," & c)
            b.Send()
            b.Handler(Trace(1), b.Twice(Trace(5)))
        End Sub
        """;

    private const string G3 = """
        Class Calc
            Public Op As Func(Of Integer, Integer)
            Public Function Apply(n As Integer) As Integer
                Return n * 100
            End Function
            Public Function Both(n As Integer) As Integer
                Return Apply(n) + Op(n)
            End Function
        End Class

        Sub Main()
            Dim c As New Calc()
            c.Op = Function(x As Integer) x + 1
            Console.WriteLine(c.Both(2))
            Console.WriteLine(c.Apply(3) + c.Op(3))
        End Sub
        """;

    private const string G4 = """
        Delegate Function Transform(n As Integer) As Integer

        Class Scaler
            Public Scale As Transform
            Public Function Apply(n As Integer) As Integer
                Return Scale(n)
            End Function
            Public Function ApplyMe(n As Integer) As Integer
                Return Me.Scale(n) + 1
            End Function
        End Class

        Sub Main()
            Dim s As New Scaler()
            s.Scale = Function(n As Integer) n * 3
            Console.WriteLine(s.Apply(4))
            Console.WriteLine(s.ApplyMe(4))
            Console.WriteLine(s.Scale(5))
        End Sub
        """;

    private const string G6 = """
        Class Board
            Public Items As List(Of Action)
            Public Sub New()
                Items = New List(Of Action)()
            End Sub
            Public Sub RunFirst()
                Items(0)()
                Dim a As Action = Items(1)
                a()
            End Sub
        End Class

        Sub Main()
            Dim b As New Board()
            b.Items.Add(Sub() Console.WriteLine("zero"))
            b.Items.Add(Sub() Console.WriteLine("one"))
            b.RunFirst()
        End Sub
        """;

    private const string G7 = """
        Class BaseH
            Public Callback As Action
            Public Op As Func(Of Integer, Integer)
        End Class

        Class Child
            Inherits BaseH
        End Class

        Sub Main()
            Dim b As BaseH = New Child()
            b.Callback = Sub() Console.WriteLine("via base")
            b.Op = Function(x As Integer) x - 1
            b.Callback()
            Console.WriteLine(b.Op(10))
            Dim c As New Child()
            c.Callback = Sub() Console.WriteLine("via child")
            c.Callback()
        End Sub
        """;

    private const string G9 = """
        Class BaseH
            Public Callback As Action(Of String)
        End Class

        Class Child
            Inherits BaseH
            Public Sub Fire()
                MyBase.Callback("mybase")
                Me.Callback("me")
                Callback("bare")
            End Sub
        End Class

        Sub Main()
            Dim c As New Child()
            c.Callback = Sub(s As String) Console.WriteLine("got " & s)
            c.Fire()
        End Sub
        """;

    private const string G10 = """
        Interface IHolder
            Property Callback As Action
            Property Op As Func(Of Integer, Integer)
        End Interface

        Class Holder
            Implements IHolder
            Public Property Callback As Action
            Public Property Op As Func(Of Integer, Integer)
        End Class

        Sub Main()
            Dim h As IHolder = New Holder()
            h.Callback = Sub() Console.WriteLine("iface")
            h.Op = Function(x As Integer) x * 4
            h.Callback()
            Console.WriteLine(h.Op(2))
        End Sub
        """;

    private const string G11 = """
        Class Holder
            Private _cb As Func(Of Integer, Integer)
            Public Reads As Integer = 0
            Public Property Op As Func(Of Integer, Integer)
                Get
                    Reads = Reads + 1
                    Return _cb
                End Get
                Set(value As Func(Of Integer, Integer))
                    _cb = value
                End Set
            End Property
            Public Function Run(n As Integer) As Integer
                Return Op(n) + Me.Op(n)
            End Function
        End Class

        Sub Main()
            Dim h As New Holder()
            h.Op = Function(x As Integer) x * 2
            Console.WriteLine(h.Run(5))
            Console.WriteLine(h.Op(1))
            Console.WriteLine(h.Reads)
        End Sub
        """;

    private const string G12 = """
        Class Registry
            Public Shared Hook As Action(Of Integer)
            Public Shared Property Named As Func(Of String, String)
            Public Shared Sub Trigger(n As Integer)
                Hook(n)
                Registry.Hook(n + 1)
                Console.WriteLine(Named("in"))
            End Sub
            Public Sub Instance()
                Hook(7)
            End Sub
        End Class

        Sub Main()
            Registry.Hook = Sub(n As Integer) Console.WriteLine("hook " & n)
            Registry.Named = Function(s As String) "<" & s & ">"
            Registry.Trigger(1)
            Registry.Hook(3)
            Console.WriteLine(Registry.Named("out"))
            Dim r As New Registry()
            r.Instance()
        End Sub
        """;

    private const string G13 = """
        Sub Notify()
            Console.WriteLine("module sub")
        End Sub

        Class Holder
            Public Notify As Action
            Public Sub Fire()
                Notify()
            End Sub
        End Class

        Sub Main()
            Dim h As New Holder()
            h.Notify = Sub() Console.WriteLine("field")
            h.Fire()
            h.Notify()
            Notify()
        End Sub
        """;

    private const string G14 = """
        Class Bus
            Public Handler As Action(Of String)
            Public Sub Send()
                Handler(Nothing)
                Me.Handler(Nothing)
            End Sub
        End Class

        Sub Main()
            Dim b As New Bus()
            b.Handler = Sub(s As String) Console.WriteLine(s Is Nothing)
            b.Send()
            b.Handler(Nothing)
        End Sub
        """;

    private const string G17 = """
        Class Holder
            Public Callback As Action
            Public Sub Fire()
                Dim Callback As Action = Sub() Console.WriteLine("local")
                Callback()
                Me.Callback()
            End Sub
            Public Sub FireParam(Callback As Action)
                Callback()
            End Sub
        End Class

        Sub Main()
            Dim h As New Holder()
            h.Callback = Sub() Console.WriteLine("field")
            h.Fire()
            h.FireParam(Sub() Console.WriteLine("param"))
        End Sub
        """;

    private const string G18 = """
        Class Holder
            Public Sub Fire()
                Callback()
                Console.WriteLine(Op(2))
            End Sub
            Public Callback As Action
            Public Op As Func(Of Integer, Integer)
        End Class

        Sub Main()
            Dim h As New Holder()
            h.Callback = Sub() Console.WriteLine("below")
            h.Op = Function(x As Integer) x * 5
            h.Fire()
        End Sub
        """;

    private const string J2f = """
        Class Button
            Public OnClick As Action(Of String)
        End Class

        Sub Main()
            Dim b As New Button()
            b.OnClick = Sub(m) Console.WriteLine("click " & m)
            b.OnClick("b")
        End Sub
        """;

    [TestCase(F0, "unset\nfired", "F0")]
    [TestCase(F1, "fired", "F1")]
    [TestCase(F2, "fired", "F2")]
    [TestCase(F3, "41", "F3")]
    [TestCase(F4, "got hi", "F4")]
    [TestCase(F5, "prop fired", "F5")]
    [TestCase(F6, "wrapped", "F6")]
    [TestCase(F7, "shared hook", "F7")]
    [TestCase(F8, "105", "F8")]
    [TestCase(F9, "inherited", "F9")]
    [TestCase(G1, "12\n36\ngt", "G1")]
    [TestCase(G2, "twice 2\ntwice 3\ngot 4,6\ntrace 1\ntrace 5\ntwice 6\ngot 2,12", "G2")]
    [TestCase(G3, "203\n304", "G3")]
    [TestCase(G4, "12\n13\n15", "G4")]
    [TestCase(G6, "zero\none", "G6")]
    [TestCase(G7, "via base\n9\nvia child", "G7")]
    [TestCase(G9, "got mybase\ngot me\ngot bare", "G9")]
    [TestCase(G10, "iface\n8", "G10")]
    [TestCase(G11, "20\n2\n3", "G11")]
    [TestCase(G12, "hook 1\nhook 2\n<in>\nhook 3\n<out>\nhook 7", "G12")]
    [TestCase(G13, "field\nfield\nmodule sub", "G13")]
    [TestCase(G14, "True\nTrue\nTrue", "G14")]
    [TestCase(G17, "local\nfield\nparam", "G17")]
    [TestCase(G18, "below\n10", "G18")]
    [TestCase(J2f, "click b", "J2f")]
    public void Probe_RunsOnEveryBackend_BothPipelines_AndProjectEntryPoint(string source, string expected, string label)
    {
        TestContext.Out.WriteLine($"[{label}] standard pipeline");
        FourBackends.RunsOnEveryBackend(source, expected);
        TestContext.Out.WriteLine($"[{label}] aggressive pipeline");
        FourBackends.RunsOnEveryBackendAggressive(source, expected);
        TestContext.Out.WriteLine($"[{label}] CompileProjectFiles (Release .blproj entry point)");
        Assert.That(Norm(RunThroughProjectEntryPoint(source)), Is.EqualTo(expected), $"[{label}] project entry point");
    }

    // ============================================================================================
    // 3. The multi-file .blproj (S/t188/multi): a bare delegate field AND a bare accessor property
    //    called from a SEPARATE FILE than their class declaration, through the project entry point,
    //    on all four backends. Before #188 this was OK only on C# (the implementer's own measurement).
    // ============================================================================================

    private const string MultiTypes = """
        Public Class Holder
            Public Callback As Action
            Public Property Op As Func(Of Integer, Integer)
            Public Sub Fire()
                Callback()
                Console.WriteLine(Op(1))
            End Sub
        End Class
        """;

    private const string MultiMain = """
        Sub Main()
            Dim h As New Holder()
            h.Callback = Sub() Console.WriteLine("multi cb")
            h.Op = Function(x As Integer) x + 40
            h.Fire()
            h.Callback()
            Console.WriteLine(h.Op(2))
        End Sub
        """;

    [Test]
    public void MultiFileProject_BareFieldAndProperty_RunOnAllFourBackends()
    {
        const string expected = "multi cb\n41\nmulti cb\n42";
        var dir = Path.Combine(Path.GetTempPath(), "BasicLang_T188Multi_" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            var mainFile = Path.Combine(dir, "Main.bas");
            var typesFile = Path.Combine(dir, "Types.bas");
            File.WriteAllText(mainFile, MultiMain);
            File.WriteAllText(typesFile, MultiTypes);

            var compiler = new BasicCompiler();
            var result = compiler.CompileProjectFiles(new[] { mainFile, typesFile });
            Assert.That(result.AllErrors, Is.Empty,
                "multi-file compile failed: " + string.Join("; ", result.AllErrors.Select(e => e.Message)));
            Assert.That(result.CombinedIR, Is.Not.Null);

            Assert.Multiple(() =>
            {
                var cs = new ImprovedCSharpCodeGenerator().Generate(result.CombinedIR);
                Assert.That(Norm(FourBackends.RunEmittedCSharpText(cs)), Is.EqualTo(expected), "C#");

                var cpp = new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false })
                    .Generate(result.CombinedIR);
                Assert.That(Norm(BclE2E.CompileRun(cpp)), Is.EqualTo(expected), "C++");

                var js = new JavaScriptCodeGenerator().Generate(result.CombinedIR);
                Assert.That(Norm(JavaScriptExecutionTests.RunNodeScript(js)), Is.EqualTo(expected), "JavaScript");

                var il = new MSILCodeGenerator().Generate(result.CombinedIR);
                Assert.That(Norm(Msil.MsilHarness.RunIlExpectingSuccess(il)), Is.EqualTo(expected), "MSIL");
            });
        }
        finally { try { Directory.Delete(dir, true); } catch { /* temp */ } }
    }

    // ============================================================================================
    // 4. L1 — an immediately-invoked lambda, on C++ and JavaScript (the two backends #188 touched
    //    for a non-name callee: C++'s GetValueName rendering, JS's parenthesisation rule).
    // ============================================================================================

    private const string L1 = """
        Function Adder(n As Integer) As Func(Of Integer, Integer)
            Return Function(x As Integer) x + n
        End Function

        Sub Main()
            Console.WriteLine((Function(x As Integer) x * 2)(5))
            Console.WriteLine(Adder(3)(4))
        End Sub
        """;

    [Test]
    public void L1_ImmediatelyInvokedLambda_RunsOnCppAndJavaScript()
    {
        const string expected = "10\n7";
        Assert.Multiple(() =>
        {
            Assert.That(Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(L1))), Is.EqualTo(expected), "C++");
            Assert.That(Norm(JavaScriptExecutionTests.RunJs(L1)), Is.EqualTo(expected), "JavaScript");
        });
    }

    // ============================================================================================
    // 5. G8 — a Nothing field invoked: every backend must RAISE, never a silent success.
    // ============================================================================================

    private const string G8 = """
        Class Holder
            Public Callback As Action
            Public Sub Fire()
                Callback()
            End Sub
        End Class

        Sub Main()
            Dim h As New Holder()
            Console.WriteLine("before")
            h.Fire()
            Console.WriteLine("after")
        End Sub
        """;

    [Test]
    public void G8_NothingFieldInvoked_RaisesOnEveryBackend_NeverASilentSuccess()
    {
        Assert.Multiple(() =>
        {
            var csEx = Assert.Throws<AssertionException>(() => FourBackends.RunEmittedCSharp(G8));
            Assert.That(csEx!.Message, Does.Contain("NullReferenceException"), "C#");

            var cppEx = Assert.Throws<AssertionException>(
                () => BclE2E.CompileRun(BclE2E.CompileToCppOptimized(G8)));
            Assert.That(cppEx!.Message, Does.Contain("bad_function_call"), "C++");

            var jsOutcome = JavaScriptExecutionTests.RunNodeScriptForOutcome(JsTestSupport.Compile(G8));
            Assert.That(jsOutcome.ExitCode, Is.Not.Zero, "JavaScript must exit non-zero");
            Assert.That(jsOutcome.StdErr, Does.Contain("TypeError"), "JavaScript");

            var msilRun = Msil.MsilHarness.Run(G8);
            Assert.That(msilRun.Outcome, Is.EqualTo(Msil.MsilHarness.MsilOutcome.RunFailed), "MSIL");
            Assert.That(msilRun.Output, Does.Contain("NullReferenceException"), "MSIL");
        });
    }

    // ============================================================================================
    // 6. Pins — a divergence UNRELATED to #188, each against the task that owns it.
    // ============================================================================================

    /// <summary>
    /// ⭐ MOVED PIN (#140). t155edge/P8: a bare field call (<c>Click()</c>)
    /// directly AND from inside a lambda (<c>Dim again = Sub() Click()</c>) in the SAME method.
    /// #188 made this COMPILE on C++ for the first time (it used to fail with the "assigning to
    /// 'void *'" defect); once it compiled, it exposed #140 (a C++ lambda captured its enclosing
    /// object BY COPY, not by reference) and USED TO silently print 0 for 2, then #170 refused it by
    /// name (the lambda assigned to <c>b.Click</c> writes <c>Main</c>'s captured <c>n</c>). #140 lowers
    /// it: <c>n</c> lives in <c>Main</c>'s environment, so C++ prints VB's 2.
    /// </summary>
    private const string P8 = """
        Class Btn
            Public Click As Action
            Public Sub Fire()
                Click()
                Dim again = Sub() Click()
                again()
            End Sub
        End Class
        Sub Main()
            Dim b As New Btn()
            Dim n As Integer = 0
            b.Click = Sub() n = n + 1
            b.Fire()
            Console.WriteLine(n)
        End Sub
        """;

    [Test]
    public void P8_FieldCalledDirectlyAndFromALambda_Cpp_RunsLowered_FormerlyPinnedForTask140()
    {
        Assert.That(CppClosures.Compile(P8).PathOf("Main"), Is.EqualTo(CppClosurePath.Lowered), "C++ root 'Main'");
        CppClosures.RunsInAllModes(P8, "2");
    }

    /// <summary>
    /// G2b: a delegate FIELD reassigned by one of its OWN call's arguments. C# (the oracle)
    /// evaluates the callee's VALUE before any argument — "old N" throughout, because the argument
    /// reassigns the field only after the OLD value was already captured for the call. The
    /// <c>Me.</c>-qualified and externally-qualified spellings agree (they snapshot the field into
    /// a temp BEFORE the arguments run); only the BARE spelling gets this backwards on C++/
    /// JavaScript/MSIL — its callee value is an <c>IRVariable</c> read INLINE at the call site
    /// (ADR-0007's bare-name rule), not a temp taken up front, so it observes the argument's own
    /// reassignment. Filed as #203, not #188 — the bare form still calls the right VALUE, just at
    /// the wrong TIME.
    /// </summary>
    private const string G2b = """
        Class Bus
            Public Handler As Action(Of Integer)
            Public Function Swap(n As Integer) As Integer
                Handler = Sub(v As Integer) Console.WriteLine("new " & v)
                Return n
            End Function
            Public Sub Send()
                Handler(Swap(1))
            End Sub
            Public Sub SendMe()
                Me.Handler(Swap(2))
            End Sub
        End Class

        Sub Main()
            Dim b As New Bus()
            b.Handler = Sub(v As Integer) Console.WriteLine("old " & v)
            b.Send()
            b.Handler = Sub(v As Integer) Console.WriteLine("old " & v)
            b.SendMe()
            b.Handler = Sub(v As Integer) Console.WriteLine("old " & v)
            b.Handler(b.Swap(3))
        End Sub
        """;

    [Test]
    public void G2b_BareSpellingEvaluatesTheCalleeAfterItsArgument_PinsTodaysWrongOrder_Against203()
    {
        Assert.That(Norm(FourBackends.RunEmittedCSharp(G2b)), Is.EqualTo("old 1\nold 2\nold 3"),
            "the oracle: the callee's value is captured before its argument runs, every spelling");

        Assert.Multiple(() =>
        {
            Assert.That(Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(G2b))), Is.EqualTo("new 1\nold 2\nold 3"),
                "C++: only the BARE spelling (Send) reads the callee AFTER its argument reassigns it");
            Assert.That(Norm(JavaScriptExecutionTests.RunJs(G2b)), Is.EqualTo("new 1\nold 2\nold 3"), "JavaScript");
            Assert.That(Norm(Msil.MsilHarness.RunExpectingSuccess(G2b)), Is.EqualTo("new 1\nold 2\nold 3"),
                "if this ever matches the C# oracle, #203 has been fixed — update this pin deliberately");
        });
    }

    /// <summary>
    /// G5: a <c>Structure</c> (value type) with a delegate FIELD, called through a bare identifier
    /// after a member-access write (<c>s.F = ...; s.F(41)</c>). C++ RUNS it correctly (structures
    /// are plain C++ structs, no boxing involved). JavaScript refuses at COMPILE time by DESIGN —
    /// BL7005, a Structure cannot be lowered to JavaScript at all (value semantics a JS object
    /// cannot preserve), unrelated to #188. MSIL threw a NullReferenceException where C# returns
    /// "inc 42" — filed as #192, not #188: #188 only taught MSIL to ADMIT a delegate-member call
    /// under ADR-0010 D8, not to handle one whose owner is a value-type Structure correctly. #192
    /// spelled a Structure `valuetype`, and MSIL now prints "inc 42" too.
    /// </summary>
    private const string G5 = """
        Structure Slot
            Public F As Func(Of Integer, Integer)
            Public Name As String
        End Structure

        Sub Main()
            Dim s As Slot
            s.Name = "inc"
            s.F = Function(x As Integer) x + 1
            Console.WriteLine(s.Name & " " & s.F(41))
        End Sub
        """;

    [Test]
    public void G5_StructureDelegateField_Cpp_Runs_And_JavaScript_RefusesByDesign()
    {
        Assert.That(Norm(FourBackends.RunEmittedCSharp(G5)), Is.EqualTo("inc 42"), "C# (the oracle)");
        Assert.That(Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(G5))), Is.EqualTo("inc 42"), "C++");

        var jsEx = Assert.Throws<BasicLang.Compiler.CodeGen.ForeignFeatureException>(() => JsTestSupport.Compile(G5));
        Assert.That(jsEx!.Message, Does.Contain("BL7005").And.Contain("Slot"),
            "JavaScript must still refuse a Structure BY DESIGN, unrelated to #188");
    }

    [Test]
    public void G5_StructureDelegateField_Msil_PrintsInc42()
    {
        // C#'s "inc 42", at the CLI and the CLI with --optimize: the field is written and read through the
        // Structure's own storage (its address), not through a null reference (#192).
        // Skip OUTSIDE the multiple block: without ilasm there is nothing to compare.
        Msil.MsilHarness.RequireIlasm();
        Assert.Multiple(() =>
        {
            Assert.That(Norm(Msil.MsilHarness.RunExpectingSuccess(G5)), Is.EqualTo("inc 42"), "MSIL CLI");
            Assert.That(Norm(Msil.MsilHarness.RunAggressiveExpectingSuccess(G5)), Is.EqualTo("inc 42"), "MSIL CLI --optimize");
        });
    }

    /// <summary>
    /// G6c: a <c>List(Of Action)</c> FIELD (not a local) indexed with VB's paren syntax through an
    /// EXTERNAL receiver (<c>b.Items(0)</c>) — even copied into a local first, exactly like #173's
    /// N6b control does for a plain field. Unlike every other pin in this fixture, this one fails
    /// on C# TOO: the generated C# is <c>b.Items(0)</c>, and <c>List&lt;T&gt;</c> has no such
    /// method — CS1955, "Non-invocable member". <see cref="G6"/> above (the SAME indexer, called
    /// BARE from a method of the declaring class — <c>Items(0)()</c>) runs everywhere, so the gap
    /// is specific to reading a List-typed FIELD through an EXTERNAL, qualified receiver — filed as
    /// #204, not #188 (C# failing rules out "C# is the oracle" reasoning; this is a pre-existing
    /// codegen gap #188 never touched, measured independently of the delegate-invocation fix).
    /// </summary>
    private const string G6c = """
        Class Board
            Public Items As List(Of Action)
            Public Sub New()
                Items = New List(Of Action)()
            End Sub
        End Class

        Sub Main()
            Dim b As New Board()
            b.Items.Add(Sub() Console.WriteLine("one"))
            Dim a As Action = b.Items(0)
            a()
        End Sub
        """;

    [Test]
    public void G6c_ListFieldIndexedThroughAnExternalReceiver_PinsTodaysCSharpCompileFailure_Against204()
    {
        var ex = Assert.Throws<AssertionException>(() => FourBackends.RunEmittedCSharp(G6c));
        Assert.That(ex!.Message, Does.Contain("CS1955").And.Contain("Items"),
            "the failure must still be C#'s own CS1955 over List<Action>.Items(0) — even the "
            + "ORACLE fails to build this shape (#204). A DIFFERENT failure here means this pin "
            + "is stale.\n" + ex.Message);
    }
}
