using System.IO;
using NUnit.Framework;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using VisualGameStudio.Tests.Native;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task #187 (fix commit <c>37faed14</c>) — execution tests. D1-D6 RUN correctly on all four
/// backends, on both the standard and aggressive optimizer pipelines, PLUS the
/// <c>BasicCompiler.CompileProjectFiles</c> entry point a Release <c>.blproj</c> build uses (the
/// CLI single-file path is <see cref="FourBackends"/>'s own C# leg —
/// <c>ReturnCoercionTests.EmitCSharpForTest</c> — so this fixture's project-entry-point leg is
/// the SECOND entry point CLAUDE.md asks every compiler fix to cover). Expected strings are taken
/// verbatim from the implementer's measured <c>.exp</c> files (<c>S/t187/probes/*.exp</c>,
/// <c>S/t187/edge*/*.exp</c>) — the VB/C# answer — never copied from MSIL.
///
/// <para>Edge probes that fail on ONE backend for a reason UNRELATED to #187 are excluded from
/// the shared four-backend runner and pinned individually against the task that owns the gap:
/// E1 on C++ (#140 — a captured local lambda variable is captured BY COPY on this backend); E8 on
/// C++ (#201 — <c>AddressOf</c> an INSTANCE method fails to compile) and E9e on C++ (#201 — a
/// branch that <c>Return</c>s an <c>AddressOf</c> result on one arm fails to compile, see that
/// pin's own comment for what changed and what did not). E13 USED TO be here too (a lambda
/// argument to <c>MyBase.New</c> had no IL lowering on MSIL and no lambda-hoisting on C#) — ADR-
/// 0016 / #170's <c>IRBaseConstructorCall</c> closes both, so E13 now runs on ALL FOUR backends
/// (JavaScript and C++ already did, since ADR-0015 / task #200's two-phase construction) and its
/// pins below are plain <c>_Runs</c> assertions, not known-wrong ones.
///
/// <para>⭐ <b>UPDATED for #188 (fix commit 5e82a786):</b> at the time this fixture was written,
/// invoking a delegate-typed FIELD through its member spelling (not a local copy) was a
/// pre-existing gap on three backends: E10 (a field read unqualified INSIDE its own class) failed
/// on JavaScript with a ReferenceError; J2 (calling a delegate-typed field directly,
/// <c>b.OnClick("b")</c>) failed on MSIL with a MissingMethodException, for a Func/Action field
/// too, not specific to a user Delegate; and E5/E5b/J1 (a <c>List(Of D)</c> of user-delegate
/// values, a capturing lambda added to one, and <c>.Invoke</c> on a field) all failed to COMPILE
/// on C++, because that backend called a delegate VALUE by NAME, which its own temp-renaming
/// could point at the wrong temp entirely. #188 is now DONE: E10, J2, E5, E5b and J1 are promoted
/// below, folded into the shared four-backend runners. E8 and E9e stay pinned against #201 — a
/// SEPARATE gap, <c>AddressOf</c> on an instance receiver, that #188 never touched.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // several legs redirect Console.Out
public class UserDelegateConversionExecutionTests
{
    // ============================================================================================
    // 0. Helpers.
    // ============================================================================================

    /// <summary>
    /// <c>BasicCompiler.CompileProjectFiles</c> — the SAME entry point the IDE's build service and
    /// a Release <c>.blproj</c> CLI build both delegate to (CLAUDE.md: "the IDE build delegates to
    /// the CLI engine (<c>CompileProjectFiles</c>); a fix verified only through the test helper can
    /// still break via the IDE or the CLI"), run with <c>OptimizeAggressive = true</c> — what a
    /// Release build requests. Mirrors
    /// <c>Family111MaterialisationBehaviourTests.RunThroughEntryPoint</c>.
    /// </summary>
    private static string RunThroughProjectEntryPoint(string source)
    {
        var dir = Path.Combine(Path.GetTempPath(), "BasicLang_T187ProjectEntry_" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "prog.bas");
            File.WriteAllText(file, source);

            var compiler = new BasicLang.Compiler.BasicCompiler(
                new BasicLang.Compiler.CompilerOptions { OptimizeAggressive = true });
            var result = compiler.CompileProjectFiles(new[] { file });

            Assert.That(result.HasErrors, Is.False,
                string.Join(" | ", result.AllErrors.Select(e => e.Message)));
            Assert.That(result.CombinedIR, Is.Not.Null, "CompileProjectFiles produced no combined IR");

            return FourBackends.Norm(FourBackends.RunEmittedCSharpText(
                new BasicLang.Compiler.CodeGen.CSharp.ImprovedCSharpCodeGenerator().Generate(result.CombinedIR)));
        }
        finally { try { Directory.Delete(dir, true); } catch { /* temp */ } }
    }

    private static string Norm(string s) => FourBackends.Norm(s);

    // ============================================================================================
    // 1. D1-D6 — all four backends, both pipelines, plus the project-build entry point.
    // ============================================================================================

    private const string D1 = """
        Delegate Sub Notify(msg As String)

        Sub Main()
            Dim d As Notify = Sub(m As String) Console.WriteLine("got " & m)
            d("x")
        End Sub
        """;

    private const string D2 = """
        Delegate Sub Notify(msg As String)

        Sub Handler(m As String)
            Console.WriteLine("h " & m)
        End Sub

        Sub Main()
            Dim d As Notify = AddressOf Handler
            d("y")
        End Sub
        """;

    private const string D3 = """
        Delegate Function Combine(a As Integer, b As Integer) As Integer

        Function Apply(f As Combine, x As Integer, y As Integer) As Integer
            Return f(x, y)
        End Function

        Sub Main()
            Console.WriteLine(Apply(Function(a As Integer, b As Integer) a + b, 2, 3))
            Dim g As Combine = Function(a As Integer, b As Integer)
                                   Return a * b
                               End Function
            Console.WriteLine(g(4, 5))
        End Sub
        """;

    private const string D4 = """
        Delegate Sub Notify(msg As String)

        Sub Main()
            Dim d As Notify
            d = Sub(m As String) Console.WriteLine("assigned " & m)
            d("z")
            Dim prefix As String = ">"
            d = Sub(m As String) Console.WriteLine(prefix & m)
            d("w")
        End Sub
        """;

    private const string D5 = """
        Delegate Function Transform(n As Integer) As Integer

        Function MakeAdder(k As Integer) As Transform
            Return Function(n As Integer) n + k
        End Function

        Sub Main()
            Dim t As Transform = MakeAdder(10)
            Console.WriteLine(t(5))
        End Sub
        """;

    private const string D6 = """
        Delegate Sub Notify(msg As String)

        Sub Main()
            Dim d As Notify = Sub(m) Console.WriteLine("untyped " & m)
            d("q")
        End Sub
        """;

    [TestCase(D1, "got x", "D1")]
    [TestCase(D2, "h y", "D2")]
    [TestCase(D3, "5\n20", "D3")]
    [TestCase(D4, "assigned z\n>w", "D4")]
    [TestCase(D5, "15", "D5")]
    [TestCase(D6, "untyped q", "D6")]
    public void D_Probe_RunsOnEveryBackend_BothPipelines_AndProjectEntryPoint(string source, string expected, string label)
    {
        TestContext.Out.WriteLine($"[{label}] standard pipeline");
        FourBackends.RunsOnEveryBackend(source, expected);
        TestContext.Out.WriteLine($"[{label}] aggressive pipeline");
        FourBackends.RunsOnEveryBackendAggressive(source, expected);
        TestContext.Out.WriteLine($"[{label}] CompileProjectFiles (Release .blproj entry point)");
        Assert.That(Norm(RunThroughProjectEntryPoint(source)), Is.EqualTo(expected), $"[{label}] project entry point");
    }

    // ============================================================================================
    // 2. Edge probes that pass on every backend (standard pipeline).
    // ============================================================================================

    private const string E2 = """
        Delegate Function Mix(a As Integer, b As String, c As Integer) As String
        Delegate Sub Show4(a As Integer, b As Integer, c As Integer, d As Integer)

        Sub Main()
            Dim m As Mix = Function(a, b, c) b & ":" & CStr(a + c)
            Console.WriteLine(m(1, "x", 3))
            Dim s As Show4 = Sub(a As Integer, b As Integer, c As Integer, d As Integer) Console.WriteLine(a + b + c + d)
            s(1, 2, 3, 4)
        End Sub
        """;

    private const string E3 = """
        Class Widget
            Public Name As String
            Public Sub New(n As String)
                Name = n
            End Sub
        End Class

        Delegate Function MakeWidget(n As String) As Widget
        Delegate Sub ShowWidget(w As Widget)

        Function Build(n As String) As Widget
            Return New Widget("built " & n)
        End Function

        Sub Main()
            Dim f As MakeWidget = Function(n As String) New Widget(n)
            Console.WriteLine(f("a").Name)
            Dim g As MakeWidget = AddressOf Build
            Dim w As Widget = g("b")
            Console.WriteLine(w.Name)
            Dim show As ShowWidget = Sub(x) Console.WriteLine("show " & x.Name)
            show(w)
        End Sub
        """;

    private const string E4 = """
        Delegate Function Transform(n As Integer) As Integer

        Class Runner
            Public Function Run(ByVal f As Transform, x As Integer) As Integer
                Return f(x)
            End Function
            Public Shared Function SRun(ByVal f As Transform, x As Integer) As Integer
                Return f.Invoke(x)
            End Function
        End Class

        Sub Main()
            Dim r As New Runner()
            Console.WriteLine(r.Run(Function(n) n + 1, 10))
            Console.WriteLine(Runner.SRun(Function(n As Integer) n * 2, 10))
            Dim k As Integer = 5
            Console.WriteLine(r.Run(Function(n)
                                        Return n + k
                                    End Function, 10))
        End Sub
        """;

    private const string E6 = """
        Delegate Function Transform(n As Integer) As Integer
        Delegate Sub Notify(msg As String)

        Sub Main()
            Dim t As Transform = Function(n As Integer) n * 3
            Dim r As Integer = t.Invoke(4)
            Console.WriteLine(r)
            Dim d As Notify = Sub(m) Console.WriteLine("inv " & m)
            d.Invoke("a")
        End Sub
        """;

    private const string E7 = """
        Delegate Sub Notify(msg As String)

        Sub Fire(d As Notify)
            If d IsNot Nothing Then d("x")
            If d Is Nothing Then Console.WriteLine("none")
        End Sub

        Sub Main()
            Fire(Nothing)
            Fire(Sub(m) Console.WriteLine("fired " & m))
            Dim n As Notify = Nothing
            If n IsNot Nothing Then n("never")
            n = AddressOf Show
            If n IsNot Nothing Then n("y")
        End Sub

        Sub Show(m As String)
            Console.WriteLine("show " & m)
        End Sub
        """;

    private const string E9 = """
        Delegate Sub Notify(msg As String)
        Delegate Function Transform(n As Integer) As Integer

        Class Holder
            Public Property Prop As Notify
            Public Held As Transform
            Public Sub New(t As Transform)
                Held = t
            End Sub
        End Class

        Function PickUp() As Transform
            Return AddressOf Inc
        End Function

        Function PickDown() As Transform
            Return Function(n) n - 1
        End Function

        Function Inc(n As Integer) As Integer
            Return n + 1
        End Function

        Sub Take(d As Notify, s As String)
            d(s)
        End Sub

        Sub Main()
            Dim h As New Holder(Function(n) n * 7)
            h.Prop = Sub(m) Console.WriteLine("prop " & m)
            Dim p As Notify = h.Prop
            p("y")
            Dim held As Transform = h.Held
            Console.WriteLine(held(3))
            Dim up As Transform = PickUp()
            Dim down As Transform = PickDown()
            Console.WriteLine(up(10) + down(10))
            Take(Sub(m) Console.WriteLine("arg " & m), "z")
            Take(AddressOf Shout, "w")
            Dim a As Notify
            a = AddressOf Shout
            a("v")
        End Sub

        Sub Shout(m As String)
            Console.WriteLine("shout " & m)
        End Sub
        """;

    private const string E11 = """
        Delegate Function Transform(n As Integer) As Integer
        Delegate Function MakeT(k As Integer) As Transform

        Sub Main()
            Dim mk As MakeT = Function(k) Function(n) n * k
            Dim t As Transform = mk(3)
            Console.WriteLine(t(5))
        End Sub
        """;

    private const string E12 = """
        Delegate Sub Notify(msg As String)
        Delegate Sub Notify2(msg As String)

        Sub Main()
            Dim a As Notify = Sub(msg) Console.WriteLine("one " & msg)
            Dim b As Notify2 = Sub(msg) Console.WriteLine("two " & msg)
            a("p")
            b("q")
        End Sub
        """;

    private const string E14 = """
        Delegate Sub Notify(msg As String)
        Delegate Sub Relay(n As Notify, s As String)

        Sub Main()
            Dim r As Relay = Sub(n, s) n(s & "!")
            r(Sub(m) Console.WriteLine("relayed " & m), "hi")
        End Sub
        """;

    private const string J2 = """
        Delegate Sub Notify(msg As String)

        Class Button
            Public OnClick As Notify
        End Class

        Sub Main()
            Dim b As New Button()
            b.OnClick = Sub(m) Console.WriteLine("click " & m)
            b.OnClick("b")
        End Sub
        """;

    [TestCase(E2, "x:4\n10", "E2")]
    [TestCase(E3, "a\nbuilt b\nshow built b", "E3")]
    [TestCase(E4, "11\n20\n15", "E4")]
    [TestCase(E6, "12\ninv a", "E6")]
    [TestCase(E7, "none\nfired x\nshow y", "E7")]
    [TestCase(E9, "prop y\n21\n20\narg z\nshout w\nshout v", "E9")]
    [TestCase(E11, "15", "E11")]
    [TestCase(E12, "one p\ntwo q", "E12")]
    [TestCase(E14, "relayed hi!", "E14")]
    public void EdgeProbe_RunsOnEveryBackend(string source, string expected, string label)
    {
        FourBackends.RunsOnEveryBackend(source, expected);
    }

    /// <summary>
    /// J2 — calling a delegate-typed FIELD directly (<c>b.OnClick("b")</c>). #188, DONE: MSIL
    /// used to throw <c>MissingMethodException</c> (calling a delegate-field spelling as a METHOD
    /// call) — the SAME failure reproduced for a Func/Action field
    /// (<c>S/t187/edge2/J2f_fieldcall_func.bas</c>, measured identically), so it was never specific
    /// to a user Delegate. It now runs on all four backends.
    /// </summary>
    [Test]
    public void J2_FieldCallSyntax_RunsOnEveryBackend()
    {
        FourBackends.RunsOnEveryBackend(J2, "click b");
    }

    // ============================================================================================
    // 3. E1 — zero-parameter user delegates: runs on C#/JavaScript/MSIL; C++'s capturing lambda
    //    (inc) is REFUSED BY NAME (#140/ADR-0016 D3/W2 — captured BY COPY, not by reference, so
    //    the write would be silently lost — a named refusal beats that).
    // ============================================================================================

    private const string E1 = """
        Delegate Sub Tick()
        Delegate Function Seed() As Integer

        Sub Main()
            Dim t As Tick = Sub() Console.WriteLine("tick")
            t()
            Dim s As Seed = Function() 42
            Console.WriteLine(s())
            Dim count As Integer = 0
            Dim inc As Tick = Sub()
                                  count += 1
                              End Sub
            inc()
            inc()
            Console.WriteLine(count)
        End Sub
        """;

    [Test]
    public void E1_ZeroParameterDelegates_RunsOnCSharpJavaScriptMsil()
    {
        const string expected = "tick\n42\n2";
        Assert.Multiple(() =>
        {
            Assert.That(Norm(FourBackends.RunEmittedCSharp(E1)), Is.EqualTo(expected), "C#");
            Assert.That(Norm(JavaScriptExecutionTests.RunJs(E1)), Is.EqualTo(expected), "JavaScript");
            Assert.That(Norm(Msil.MsilHarness.RunExpectingSuccess(E1)), Is.EqualTo(expected), "MSIL");
        });
    }

    /// <summary>
    /// ⭐ MOVED PIN (ADR-0016 D3/W2, task #170). A pre-existing, UNRELATED C++ lambda-capture
    /// defect (#140): `count` is captured BY VALUE, so `inc()` twice never mutates the caller's
    /// own `count`. Measured identically for Action-typed captures before #187. This USED TO
    /// silently print tick\n42\n0 for tick\n42\n2; #170's capability check now REFUSES it by name
    /// (arm (a): <c>inc</c> writes <c>count</c>, which it captures) rather than compiling it
    /// wrong. Filed against #140, not fixed here.
    /// </summary>
    [Test]
    public void E1_ZeroParameterDelegates_Cpp_RefusedByName_PinnedForTask140()
    {
        var ex = Assert.Throws<CppCapabilityException>(() => BclE2E.CompileToCppOptimized(E1));
        Assert.That(ex!.Message, Does.Contain("captures 'count' of 'Main'").And.Contain("#140"),
            "if this stops refusing, #140 has been fixed — update this pin deliberately.\n" + ex.Message);
    }

    // ============================================================================================
    // 4. E5/E5b/J1 — a user delegate stored in a List(Of D) and invoked, and .Invoke on a field:
    //    #188, DONE — these now run on ALL FOUR backends (folded into EdgeProbe_RunsOnEveryBackend
    //    below). E8/E9e — AddressOf an instance method, a branch that Returns an AddressOf result
    //    on one arm: still run on C#/JavaScript/MSIL only; still fail on C++ (#201, filed by this
    //    task, untouched by #188).
    // ============================================================================================

    private const string E5 = """
        Delegate Sub Notify(msg As String)

        Sub Main()
            Dim handlers As New List(Of Notify)
            Dim a As Notify = Sub(m) Console.WriteLine("a " & m)
            handlers.Add(a)
            Dim b As Notify = Sub(m As String) Console.WriteLine("b " & m)
            handlers.Add(b)
            For Each h As Notify In handlers
                h("x")
            Next
            handlers(0)("y")
            Dim first As Notify = handlers(1)
            first.Invoke("z")
        End Sub
        """;

    private const string E5b = """
        Delegate Sub Notify(msg As String)

        Sub Main()
            Dim handlers As New List(Of Notify)
            handlers.Add(Sub(m As String) Console.WriteLine("b " & m))
            handlers(0)("y")
        End Sub
        """;

    private const string E8 = """
        Delegate Sub Notify(msg As String)
        Delegate Function Transform(n As Integer) As Integer

        Class Greeter
            Public Prefix As String
            Public Sub Greet(m As String)
                Console.WriteLine(Prefix & m)
            End Sub
            Public Function Scale(n As Integer) As Integer
                Return n * 10
            End Function
            Public Function Self() As Notify
                Return AddressOf Me.Greet
            End Function
        End Class

        Sub Main()
            Dim g As New Greeter()
            g.Prefix = "hi "
            Dim d As Notify = AddressOf g.Greet
            d("bob")
            Dim t As Transform = AddressOf g.Scale
            Console.WriteLine(t(4))
            Dim s As Notify = g.Self()
            s("me")
        End Sub
        """;

    private const string E9e = """
        Delegate Function Transform(n As Integer) As Integer

        Function Pick(up As Boolean) As Transform
            If up Then Return AddressOf Inc
            Return Function(n) n - 1
        End Function

        Function Inc(n As Integer) As Integer
            Return n + 1
        End Function

        Sub Main()
            Console.WriteLine(Pick(True)(10) + Pick(False)(10))
        End Sub
        """;

    private const string J1 = """
        Delegate Sub Notify(msg As String)
        Delegate Function Transform(n As Integer) As Integer

        Class Button
            Public OnClick As Notify
            Public Scale As Transform
        End Class

        Sub Main()
            Dim b As New Button()
            b.OnClick = Sub(m) Console.WriteLine("click " & m)
            b.Scale = Function(n) n * 2
            b.OnClick.Invoke("a")
            Console.WriteLine(b.Scale.Invoke(4))
        End Sub
        """;

    /// <summary>#188, DONE — E5/E5b/J1 now run on ALL FOUR backends.</summary>
    [TestCase(E5, "a x\nb x\na y\nb z", "E5")]
    [TestCase(E5b, "b y", "E5b")]
    [TestCase(J1, "click a\n8", "J1")]
    public void FormerlyCppExcludedProbe_RunsOnEveryBackend(string source, string expected, string label)
    {
        FourBackends.RunsOnEveryBackend(source, expected);
    }

    [TestCase(E8, "hi bob\n40\nhi me", "E8")]
    [TestCase(E9e, "20", "E9e")]
    public void CppExcludedProbe_RunsOnCSharpJavaScriptMsil(string source, string expected, string label)
    {
        Assert.Multiple(() =>
        {
            Assert.That(Norm(FourBackends.RunEmittedCSharp(source)), Is.EqualTo(expected), $"[{label}] C#");
            Assert.That(Norm(JavaScriptExecutionTests.RunJs(source)), Is.EqualTo(expected), $"[{label}] JavaScript");
            Assert.That(Norm(Msil.MsilHarness.RunExpectingSuccess(source)), Is.EqualTo(expected), $"[{label}] MSIL");
        });
    }

    [Test]
    public void E8_AddressOfInstanceMethod_Cpp_PinsTodaysCompileFailure_Against201()
    {
        var cpp = BclE2E.CompileToCppOptimized(E8);
        var ex = Assert.Throws<AssertionException>(() => BclE2E.CompileRun(cpp));
        Assert.That(ex!.Message, Does.Contain("undeclared identifier"),
            "the failure must still be a C++ compile error over an undeclared identifier (#201).\n" + ex.Message);
    }

    /// <summary>
    /// #188 fixed the HALF of this shape that was #188's own: the delegate value returned by
    /// <c>Pick(...)</c> used to be invoked by its raw temp NAME, which C++'s temp-renaming could
    /// point at the WRONG temp. That half is gone. What remains, unchanged, is #201's own half —
    /// the <c>AddressOf Inc</c> branch initializes a <c>std::function</c> local (<c>t0</c>) only on
    /// ONE arm of the <c>If</c>, and the C++ backend's <c>goto</c>-based control flow jumps over
    /// that initialization on the other arm, which clang refuses outright.
    /// </summary>
    [Test]
    public void E9e_ReturnAddressOfOnOneBranchArm_Cpp_PinsTodaysCompileFailure_Against201()
    {
        var cpp = BclE2E.CompileToCppOptimized(E9e);
        var ex = Assert.Throws<AssertionException>(() => BclE2E.CompileRun(cpp));
        Assert.That(ex!.Message, Does.Contain("cannot jump from this goto statement"),
            "the failure must still be the branch-return goto-crosses-initialization C++ compile error (#201).\n" + ex.Message);
    }

    // ============================================================================================
    // 5. E10 — a delegate-typed FIELD invoked, unqualified, from inside its OWN class: runs on
    //    C#/C++/MSIL; fails on JavaScript with a ReferenceError (#188, widened scope — the SAME
    //    class of bug NothingConversionExecutionTests' N6/#188 entry already files).
    // ============================================================================================

    private const string E10 = """
        Delegate Sub Notify(msg As String)

        Class Button
            Public OnClick As Notify
            Public Sub Click()
                If OnClick IsNot Nothing Then OnClick("clicked")
            End Sub
        End Class

        Sub Main()
            Dim b As New Button()
            b.OnClick = Sub(m) Console.WriteLine(m)
            b.Click()
        End Sub
        """;

    /// <summary>#188, DONE — JavaScript used to emit the unqualified field as a bare
    /// <c>ReferenceError</c> ("OnClick is not defined"); it now reads <c>this.OnClick</c> and
    /// invokes that value, so E10 runs on all four backends.</summary>
    [Test]
    public void E10_FieldInvokedInsideItsOwnClass_RunsOnEveryBackend()
    {
        FourBackends.RunsOnEveryBackend(E10, "clicked");
    }

    // ============================================================================================
    // 6. E13 — a lambda argument to MyBase.New. Ran on JavaScript AND, since ADR-0015 / task
    //    #200's two-phase construction (E11 placement: "a lambda argument renders inline, like
    //    any lambda use"), on C++ too. ADR-0016 / #170's IRBaseConstructorCall now closes the
    //    remaining two: C# no longer fails on an undeclared `__lambda_0` (the lambda creation is
    //    an ordinary prologue instruction the body emitter already knows how to render inline),
    //    and MSIL no longer refuses with a named ForeignFeatureException ("invisible to the
    //    capture analysis") for the same reason. E13 runs on ALL FOUR backends, printing 101.
    // ============================================================================================

    private const string E13 = """
        Delegate Function Transform(n As Integer) As Integer

        Class BaseBox
            Public Held As Transform
            Public Sub New(t As Transform)
                Held = t
            End Sub
        End Class

        Class Derived
            Inherits BaseBox
            Public Sub New()
                MyBase.New(Function(n) n + 100)
            End Sub
        End Class

        Sub Main()
            Dim d As New Derived()
            Dim t As Transform = d.Held
            Console.WriteLine(t(1))
        End Sub
        """;

    [Test]
    public void E13_MyBaseNewLambdaArgument_JavaScript_Runs()
    {
        Assert.That(Norm(JavaScriptExecutionTests.RunJs(E13)), Is.EqualTo("101"));
    }

    /// <summary>
    /// ⭐ MOVED PIN (ADR-0016 / #170). This used to pin C#'s undeclared-<c>__lambda_0</c> compile
    /// error (#201): the base-args lambda's creation instruction lived off-stream in
    /// <c>IRConstructor.BaseConstructorArgs</c>, so nothing in the body ever saw it declared.
    /// #170's <c>IRBaseConstructorCall</c> puts the creation instruction back in the entry
    /// block's prologue, and D1's C# rule renders a lambda creation there as an inline C# lambda
    /// through the existing body emitter — the same shared capture as any other lambda use.
    /// Prints 101, matching the JavaScript leg above.
    /// </summary>
    [Test]
    public void E13_MyBaseNewLambdaArgument_CSharp_Runs()
    {
        Assert.That(Norm(FourBackends.RunEmittedCSharp(E13)), Is.EqualTo("101"));
    }

    /// <summary>
    /// PROMOTED (ADR-0015 / task #200 — was <c>E13_MyBaseNewLambdaArgument_Cpp_PinsTodaysUndeclaredIdentifier_Against201</c>,
    /// pinned against an undeclared-identifier compile error). A lambda argument to
    /// <c>MyBase.New</c> renders INLINE at its use site — the same as any other lambda use — so
    /// once <c>Base::ctor_</c> is correctly placed (two-phase construction's E11 rule) the
    /// lambda compiles and runs, printing JavaScript's own answer. #201's C++ leg was fixed by
    /// #200 as a side effect; its C# leg is #170's own moved pin above — the sibling test now
    /// runs too, so all four backends agree here.
    /// </summary>
    [Test]
    public void E13_MyBaseNewLambdaArgument_Cpp_Runs()
    {
        var cpp = BclE2E.CompileToCppOptimized(E13);
        Assert.That(Norm(BclE2E.CompileRun(cpp)), Is.EqualTo("101"));
    }

    /// <summary>
    /// ⭐ MOVED PIN (ADR-0016 / #170). MSIL used to refuse <see cref="E13"/> outright with a named
    /// <c>ForeignFeatureException</c> ("a lambda passed to MyBase.New... is invisible to the
    /// capture analysis") — the base-args lambda's creation instruction was off-stream, so
    /// <c>ScanForLambdas</c> / <c>LambdaReferences</c> never found it and ClosureLowering had no
    /// environment to build. #170 makes the lambda creation an ordinary prologue instruction
    /// ClosureLowering DOES see; D3's contract places the environment allocation and hoists
    /// before the prologue and writes <c>Me</c> into it immediately after
    /// <c>IRBaseConstructorCall</c> (this lambda captures no <c>Me</c> at all, so that ordering
    /// is moot here — <see cref="CppMeAsValueTests"/>'s own class covers a base-args lambda that
    /// reads <c>Me</c> from the body). Prints 101, same as every other backend.
    ///
    /// <para>On a machine with no <c>ilasm</c> (this container) the harness skips before even
    /// building — <c>MsilHarness.RunExpectingSuccess</c> calls <c>RequireIlasm()</c> first — so
    /// this pin can only be MEASURED on Windows; written correctly here for that run.</para>
    /// </summary>
    [Test]
    public void E13_MyBaseNewLambdaArgument_Msil_Runs()
    {
        Assert.That(Norm(Msil.MsilHarness.RunExpectingSuccess(E13)), Is.EqualTo("101"));
    }
}
