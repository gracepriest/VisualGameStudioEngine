using NUnit.Framework;
using VisualGameStudio.Tests.Native;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task #173 (fix commit c0b457d9) — <c>Nothing</c> RUNS correctly on all four backends once
/// admitted into a reference type, on both the standard and aggressive optimizer pipelines.
/// Expected strings below are taken from the implementer's measured <c>.exp</c> files
/// (<c>S/t173/rw/*.exp</c>), themselves the VB/C# answer — never copied from MSIL, which the
/// implementer's brief calls out as a backend whose own semantics can diverge.
///
/// <para>Only the probes that run EVERYWHERE are here: N4/N4b (storing a lambda / AddressOf
/// result into a user <c>Delegate</c> variable) and N6 (a delegate FIELD invoked from inside its
/// own class) are pre-existing gaps, unrelated to <c>Nothing</c>, filed as #187/#188 — measured in
/// <c>matrix-after.txt</c> to fail identically whether or not <c>Nothing</c> is involved (N4c, the
/// no-lambda-storage control, runs everywhere). X4 (<c>Case Is Nothing</c> on an array) fails to
/// compile on C++ for the same pre-existing reason as N7 below and is tracked under #189; X4b (no
/// <c>Case Is Nothing</c> on the array) runs everywhere and is used instead.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // several legs redirect Console.Out
public class NothingConversionExecutionTests
{
    // ============================================================================================
    // 1. The probes that run on every backend, under both pipelines (FourBackends).
    // ============================================================================================

    private const string N1 = """
        Sub Main()
            Dim f As Action = Nothing
            Select Case f
                Case Is Nothing
                    Console.WriteLine("none")
                Case Else
                    Console.WriteLine("some")
            End Select
            f = Sub() Console.WriteLine("set")
            f()
        End Sub
        """;

    private const string N2 = """
        Sub Main()
            Dim g As Func(Of Integer) = Nothing
            g = Function() 5
            Console.WriteLine(g())
            g = Nothing
            Select Case g
                Case Is Nothing
                    Console.WriteLine("True")
                Case Else
                    Console.WriteLine("False")
            End Select
        End Sub
        """;

    private const string N3 = """
        Sub Run(a As Action)
            Select Case a
                Case Is Nothing
                    Console.WriteLine("skip")
                Case Else
                    a()
            End Select
        End Sub

        Sub Main()
            Run(Nothing)
            Run(Sub() Console.WriteLine("go"))
        End Sub
        """;

    /// <summary>The no-lambda-storage control: a <c>Delegate</c>-typed variable set to <c>Nothing</c>
    /// twice, no lambda ever stored into it — N4/N4b's storage step (a pre-existing gap, #187) is
    /// the only thing this probe avoids.</summary>
    private const string N4c = """
        Delegate Sub Notify(msg As String)

        Sub Main()
            Dim d As Notify = Nothing
            Select Case d
                Case Is Nothing
                    Console.WriteLine("empty")
                Case Else
                    Console.WriteLine("full")
            End Select
            d = Nothing
            Select Case d
                Case Is Nothing
                    Console.WriteLine("empty2")
                Case Else
                    Console.WriteLine("full2")
            End Select
        End Sub
        """;

    private const string N5 = """
        Function Pick(b As Boolean) As Func(Of Integer)
            If b Then Return Function() 7
            Return Nothing
        End Function

        Sub Main()
            Dim q As Func(Of Integer) = Pick(True)
            Console.WriteLine(q())
            Dim p As Func(Of Integer) = Pick(False)
            Select Case p
                Case Is Nothing
                    Console.WriteLine("True")
                Case Else
                    Console.WriteLine("False")
            End Select
        End Sub
        """;

    /// <summary>N6's cross-file/indirect twin: the same "Callback = Nothing then set later" shape,
    /// but read into a LOCAL before the <c>Select Case</c> rather than switched on the field
    /// directly — which is what N6 itself does, and what fails on C++ (a `void*` field) and
    /// JavaScript (an undeclared `Callback` reference) for reasons unrelated to Nothing (#188).
    /// N6b runs everywhere.</summary>
    private const string N6b = """
        Class Holder
            Public Callback As Action = Nothing
        End Class

        Sub Fire(h As Holder)
            Dim cb As Action = h.Callback
            Select Case cb
                Case Is Nothing
                    Console.WriteLine("unset")
                Case Else
                    cb()
            End Select
        End Sub

        Sub Main()
            Dim h As New Holder()
            Fire(h)
            h.Callback = Sub() Console.WriteLine("fired")
            Fire(h)
        End Sub
        """;

    private const string X1 = """
        Class C
            Public N As Integer
        End Class
        Class H
            Public F As C = Nothing
            Private _p As C
            Public Property P As C
                Get
                    Return _p
                End Get
                Set(value As C)
                    _p = value
                End Set
            End Property
            Public V As C
            Public Sub New(c As C)
                V = c
            End Sub
        End Class
        Function Mk(b As Boolean) As C
            If b Then Return New C()
            Return Nothing
        End Function
        Sub Show(tag As String, c As C)
            Select Case c
                Case Is Nothing
                    Console.WriteLine(tag & ":null")
                Case Else
                    Console.WriteLine(tag & ":obj")
            End Select
        End Sub
        Sub Main()
            Dim a As C = Nothing
            Show("dim", a)
            a = New C()
            Show("new", a)
            a = Nothing
            Show("asg", a)
            Show("arg", Nothing)
            Show("ret", Mk(False))
            Show("ret2", Mk(True))
            Dim h As New H(Nothing)
            Show("ctor", h.V)
            Show("field", h.F)
            h.P = New C()
            h.P = Nothing
            Show("prop", h.P)
        End Sub
        """;

    private const string X2 = """
        Interface I
            Sub Hi()
        End Interface
        Class K
            Implements I
            Public Sub Hi() Implements I.Hi
                Console.WriteLine("hi")
            End Sub
        End Class
        Function Get1(b As Boolean) As I
            If b Then Return New K()
            Return Nothing
        End Function
        Sub Show(i As I)
            Select Case i
                Case Is Nothing
                    Console.WriteLine("null")
                Case Else
                    Console.WriteLine("obj")
            End Select
        End Sub
        Sub Main()
            Dim i As I = Nothing
            Show(i)
            i = Get1(True)
            Show(i)
            i = Get1(False)
            Show(i)
            Show(Nothing)
        End Sub
        """;

    private const string X3 = """
        Sub Main()
            Dim l As List(Of Integer) = Nothing
            Select Case l
                Case Is Nothing
                    Console.WriteLine("null")
            End Select
            l = New List(Of Integer)()
            l.Add(4)
            Console.WriteLine(l.Count)
            l = Nothing
            Select Case l
                Case Is Nothing
                    Console.WriteLine("null2")
            End Select
        End Sub
        """;

    /// <summary>X4's twin without a <c>Case Is Nothing</c> ON THE ARRAY itself (X4 fails to compile
    /// on C++ for that — a pre-existing gap, #189); this probe still exercises an array-typed
    /// <c>Dim</c>/assignment/Return admitting Nothing and runs everywhere.</summary>
    private const string X4b = """
        Function Mk(b As Boolean) As Integer()
            If b Then Return New Integer() {1, 2, 3}
            Return Nothing
        End Function
        Sub Main()
            Dim a() As Integer = Nothing
            a = New Integer() {1, 2}
            Console.WriteLine(a.Length)
            a = Nothing
            a = Mk(True)
            Console.WriteLine(a.Length)
        End Sub
        """;

    private const string X5 = """
        Class C
            Public N As Integer
        End Class
        Dim G As C = Nothing
        Sub Main()
            Select Case G
                Case Is Nothing
                    Console.WriteLine("null")
            End Select
            G = New C()
            G.N = 3
            Console.WriteLine(G.N)
        End Sub
        """;

    private const string X6 = """
        Class C
        End Class
        Class H
            Public V As C
            Public Sub Put(c As C)
                V = c
            End Sub
        End Class
        Sub Main()
            Dim h As New H()
            h.Put(New C())
            h.Put(Nothing)
            Select Case h.V
                Case Is Nothing
                    Console.WriteLine("null")
                Case Else
                    Console.WriteLine("obj")
            End Select
            Dim arr(1) As C
            arr(0) = New C()
            arr(0) = Nothing
            Select Case arr(0)
                Case Is Nothing
                    Console.WriteLine("elem-null")
                Case Else
                    Console.WriteLine("elem-obj")
            End Select
        End Sub
        """;

    private const string X7 = """
        Class C
            Public Tag As String
        End Class
        Sub Show(x As C)
            Select Case x
                Case Is Nothing
                    Console.WriteLine("got null")
                Case Else
                    Console.WriteLine("got obj")
            End Select
        End Sub
        Sub Main()
            Dim f As Action(Of C) = Sub(x As C) Show(x)
            f(Nothing)
            f(New C())
            Dim a As Action(Of Integer()) = Sub(v As Integer()) Console.WriteLine("arr")
            a(Nothing)
        End Sub
        """;

    private const string X8 = """
        Class C
        End Class
        Class B
            Public Seen As String
            Public Sub New(c As C)
                Select Case c
                    Case Is Nothing
                        Seen = "base-null"
                    Case Else
                        Seen = "base-obj"
                End Select
            End Sub
        End Class
        Class D
            Inherits B
            Public Sub New()
                MyBase.New(Nothing)
            End Sub
        End Class
        Sub Main()
            Dim d As New D()
            Console.WriteLine(d.Seen)
        End Sub
        """;

    [TestCase(N1, "none\nset", "N1")]
    [TestCase(N2, "5\nTrue", "N2")]
    [TestCase(N3, "skip\ngo", "N3")]
    [TestCase(N4c, "empty\nempty2", "N4c")]
    [TestCase(N5, "7\nTrue", "N5")]
    [TestCase(N6b, "unset\nfired", "N6b")]
    [TestCase(X1, "dim:null\nnew:obj\nasg:null\narg:null\nret:null\nret2:obj\nctor:null\nfield:null\nprop:null", "X1")]
    [TestCase(X2, "null\nobj\nnull\nnull", "X2")]
    [TestCase(X3, "null\n1\nnull2", "X3")]
    [TestCase(X4b, "2\n3", "X4b")]
    [TestCase(X5, "null\n3", "X5")]
    [TestCase(X6, "null\nelem-null", "X6")]
    [TestCase(X7, "got null\ngot obj\narr", "X7")]
    [TestCase(X8, "base-null", "X8")]
    public void Probe_RunsOnEveryBackend_BothPipelines(string source, string expected, string label)
    {
        TestContext.Out.WriteLine($"[{label}] standard pipeline");
        FourBackends.RunsOnEveryBackend(source, expected);
        TestContext.Out.WriteLine($"[{label}] aggressive pipeline");
        FourBackends.RunsOnEveryBackendAggressive(source, expected);
    }

    // ============================================================================================
    // 2. N7 / L16 — a Catch-captured Action initialized to Nothing, invoked after the Try.
    //    (ClosureLoweringTests' L16 constant was never written — its own doc comment records why:
    //    before #173 the front end refused it outright. This is that shape, now that it compiles.)
    // ============================================================================================

    private const string N7 = """
        Sub Main()
            Dim f As Action = Nothing
            Try
                Throw New Exception("boom")
            Catch ex As Exception
                f = Sub() Console.WriteLine(ex.Message)
            End Try
            f()
        End Sub
        """;

    [Test]
    public void N7_CatchCapturedActionInitializedToNothing_CSharpJavaScriptMsil_PrintBoom()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(N7)), Is.EqualTo("boom"), "C#");
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(N7)), Is.EqualTo("boom"), "JavaScript");
            Assert.That(FourBackends.Norm(Msil.MsilHarness.RunExpectingSuccess(N7)), Is.EqualTo("boom"), "MSIL");
        });
    }

    /// <summary>
    /// C++ does not run this: a captured <c>Catch</c> variable's member access (<c>ex.Message</c>)
    /// lowers to <c>ex-&gt;Message</c>, and the C++ exception representations
    /// (<c>BasicLang::NetException</c> / <c>std::exception</c>) are held by VALUE, not by pointer —
    /// a PRE-EXISTING gap, unrelated to <c>Nothing</c> (measured: the same shape with a
    /// non-Nothing-initialized <c>Dim f As Action</c>, i.e. <c>ClosureLoweringTests.L16b</c>, fails
    /// identically on C++). Filed under #189. Pinned so a fix is a deliberate, noticed change.
    /// </summary>
    [Test]
    public void N7_CatchCapturedActionInitializedToNothing_Cpp_PinsTodaysCompileFailure_Against189()
    {
        var cpp = BclE2E.CompileToCppOptimized(N7);
        var ex = Assert.Throws<AssertionException>(() => BclE2E.CompileRun(cpp));
        Assert.That(ex!.Message, Does.Contain("NetException").And.Contain("is not a pointer"),
            "the compile failure must still be the captured Catch variable's member access on a " +
            "by-value exception type (#189). A DIFFERENT failure here means this pin is stale.\n" +
            ex.Message);
    }

    // ============================================================================================
    // 3. S1-S3 — a String Nothing. VB semantics: `Dim s As String = Nothing` behaves like "",
    //    because C++ represents String by VALUE (std::string) with no null state — the recorded
    //    divergence (Nothing is distinguishable from "" only through `Is`, #185, not yet parsed).
    // ============================================================================================

    private const string S1 = """
        Sub Main()
            Dim s As String = Nothing
            Console.WriteLine("[" & s & "]")
        End Sub
        """;

    private const string S2 = """
        Function F() As String
            Return Nothing
        End Function
        Sub Take(t As String)
            Console.WriteLine("arg[" & t & "]")
        End Sub
        Sub Main()
            Dim s As String = "x"
            s = Nothing
            Console.WriteLine("asg[" & s & "]")
            Console.WriteLine("ret[" & F() & "]")
            Take(Nothing)
        End Sub
        """;

    private const string S3 = """
        Class H
            Public F As String = Nothing
            Public V As String
            Public Sub New(s As String)
                V = s
            End Sub
            Public Sub Put(s As String)
                V = s
            End Sub
        End Class
        Dim G As String = Nothing
        Sub Main()
            Dim h As New H(Nothing)
            Console.WriteLine("ctor[" & h.V & "]")
            Console.WriteLine("field[" & h.F & "]")
            h.Put("x")
            h.Put(Nothing)
            Console.WriteLine("meth[" & h.V & "]")
            Console.WriteLine("mod[" & G & "]")
            Dim arr(1) As String
            arr(0) = "y"
            arr(0) = Nothing
            Console.WriteLine("elem[" & arr(0) & "]")
            Dim f As Action(Of String) = Sub(t As String) Console.WriteLine("del[" & t & "]")
            f(Nothing)
        End Sub
        """;

    [TestCase(S1, "[]", "S1")]
    [TestCase(S2, "asg[]\nret[]\narg[]", "S2")]
    [TestCase(S3, "ctor[]\nfield[]\nmeth[]\nmod[]\nelem[]\ndel[]", "S3")]
    public void StringNothing_CSharpCppMsil_MatchTheExpectation(string source, string expected, string label)
    {
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(source)), Is.EqualTo(expected), $"[{label}] C#");
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(source))), Is.EqualTo(expected), $"[{label}] C++");
            Assert.That(FourBackends.Norm(Msil.MsilHarness.RunExpectingSuccess(source)), Is.EqualTo(expected), $"[{label}] MSIL");
        });
    }

    /// <summary>
    /// JavaScript prints the real <c>null</c> rather than an empty string — measured as a
    /// PRE-EXISTING behaviour (string concatenation with a JS <c>null</c> renders "null"), not
    /// introduced by #173: before this fix, none of these probes compiled at all. Filed under
    /// #189. Pinned so a fix (or a decision to special-case String's default in the JS backend)
    /// is deliberate rather than a silent, unnoticed behavior change.
    /// </summary>
    [TestCase(S1, "[null]", "S1")]
    [TestCase(S2, "asg[null]\nret[null]\narg[null]", "S2")]
    [TestCase(S3, "ctor[null]\nfield[null]\nmeth[null]\nmod[null]\nelem[null]\ndel[null]", "S3")]
    public void StringNothing_JavaScript_PinsTodaysNullText_Against189(string source, string expected, string label)
    {
        Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(source)), Is.EqualTo(expected), $"[{label}] JavaScript");
    }

    // ============================================================================================
    // 4. The C++ spelling (CppCodeGenerator.NothingOf): a value-held reference type gets its EMPTY
    //    value, never a bare nullptr; a shared_ptr/std::function-represented reference type keeps
    //    nullptr. Mutant (c3) — "NothingOf always returns nullptr" — must die on the FIRST
    //    assertion of each test below.
    // ============================================================================================

    private const string StringClassDelegateNothing = """
        Class C
        End Class
        Sub Main()
            Dim s As String = Nothing
            Dim c As C = Nothing
            Dim f As Action = Nothing
            Console.WriteLine("ok")
        End Sub
        """;

    [Test]
    public void Cpp_StringNothing_EmitsEmptyStdString_NeverBareNullptr()
    {
        var cpp = BclE2E.CompileToCppOptimized(StringClassDelegateNothing);

        Assert.That(cpp, Does.Contain("s = std::string{};"),
            "Dim s As String = Nothing must emit std::string{} — a bare `s = nullptr;` is " +
            "undefined behaviour for a std::string.\n--- generated C++ ---\n" + cpp);
        Assert.That(cpp, Does.Not.Contain("s = nullptr;"),
            "mutant (c3) — NothingOf must not have collapsed to always returning nullptr.\n--- generated C++ ---\n" + cpp);
    }

    [Test]
    public void Cpp_ClassAndDelegateNothing_StillEmitNullptr()
    {
        var cpp = BclE2E.CompileToCppOptimized(StringClassDelegateNothing);

        Assert.Multiple(() =>
        {
            Assert.That(cpp, Does.Contain("c = nullptr;"), "a class Nothing must still spell nullptr.\n" + cpp);
            Assert.That(cpp, Does.Contain("f = nullptr;"), "a delegate Nothing must still spell nullptr.\n" + cpp);
        });
    }
}
