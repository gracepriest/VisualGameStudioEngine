using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// The diagnostic for a base class that cannot be constructed with no arguments — VB's BC30387 and
/// its sibling BC30148.
///
/// <para>⛔ Nothing said anything. A derived class whose base needs arguments COMPILED, and then
/// failed on every backend, differently: <c>MissingMethodException: Void Base..ctor()</c> on MSIL,
/// <b>CS7036</b> on C#, <c>base:undefined</c> on JavaScript. None of them can invent the arguments,
/// which is what makes this the front end's to catch rather than a codegen gap.</para>
///
/// <para>⚠ TWO shapes, one condition: a class that declares no constructor at all (BC30387) and a
/// constructor that never calls <c>MyBase.New</c> (BC30148). Both get an implicit base call with no
/// arguments, so both are unbuildable for the same reason — checking one and not the other is how
/// half a defect survives.</para>
///
/// <para>⛔ "Callable with no arguments" is asked through <c>ResolveConstructor</c>, the same helper
/// a <c>New</c> site uses, so an all-<c>Optional</c> base constructor COUNTS — its defaults fill.
/// Asking it a second way here is how the two would come to disagree about one program, and the
/// all-Optional case is exactly where a hand-rolled "is there a .ctor0 key" test would have been
/// wrong.</para>
/// </summary>
[TestFixture]
public class BaseConstructorDiagnosticTests
{
    /// <summary>
    /// ⛔ BC30387. Measured before: compiled clean, then
    /// <c>MissingMethodException: Void Base..ctor()</c> at run time.
    /// </summary>
    [Test]
    public void AClassWithNoConstructor_AndABaseNeedingArguments_IsRejected()
    {
        var errors = OptionalConstructorTests.Analyze("""
            Class Base
             Public Sub New(a As Integer)
              PrintLine("base:" & CStr(a))
             End Sub
            End Class

            Class Derived
             Inherits Base
            End Class

            Module M
             Sub Main()
              Dim d As New Derived()
             End Sub
            End Module
            """);

        Assert.That(errors, Has.Some.Contains(
            "Class 'Derived' must declare a 'Sub New' because its base class 'Base' does not have "
            + "an accessible 'Sub New' that can be called with no arguments"),
            "actual: " + string.Join(" | ", errors));
    }

    /// <summary>
    /// ⛔ BC30148, the sibling: a constructor exists but never calls <c>MyBase.New</c>, so the base
    /// still gets an implicit no-argument call. Identical failure before —
    /// <c>MissingMethodException</c> on MSIL, CS7036 on C#.
    /// </summary>
    [Test]
    public void AConstructorWithNoMyBaseNew_AndABaseNeedingArguments_IsRejected()
    {
        var errors = OptionalConstructorTests.Analyze("""
            Class Base
             Public Sub New(a As Integer)
              PrintLine("base:" & CStr(a))
             End Sub
            End Class

            Class Derived
             Inherits Base
             Public Sub New()
              PrintLine("derived")
             End Sub
            End Class

            Module M
             Sub Main()
              Dim d As New Derived()
             End Sub
            End Module
            """);

        Assert.That(errors, Has.Some.Contains(
            "First statement of this 'Sub New' must be a call to 'MyBase.New' because base class "
            + "'Base' of 'Derived' does not have an accessible 'Sub New' that can be called with "
            + "no arguments"),
            "actual: " + string.Join(" | ", errors));
    }

    /// <summary>⚠ A MIDDLE class in a chain is a derived class too, and is checked as one.</summary>
    [Test]
    public void AMiddleClassWithNoConstructor_IsRejectedToo()
    {
        var errors = OptionalConstructorTests.Analyze("""
            Class A
             Public Sub New(n As Integer)
              PrintLine("A:" & CStr(n))
             End Sub
            End Class

            Class B
             Inherits A
            End Class

            Module M
             Sub Main()
              Dim b As New B()
             End Sub
            End Module
            """);

        Assert.That(errors, Has.Some.Contains("Class 'B' must declare a 'Sub New'"),
            "actual: " + string.Join(" | ", errors));
    }

    // ====================================================================================
    // What must NOT be rejected. A diagnostic that over-fires is worse than none.
    // ====================================================================================

    /// <summary>⚠ A parameterless base is exactly what the implicit call wants.</summary>
    [Test]
    public void AParameterlessBase_IsAccepted()
    {
        Assert.That(OptionalConstructorTests.Analyze("""
            Class Base
             Public Sub New()
              PrintLine("base0")
             End Sub
            End Class

            Class Derived
             Inherits Base
            End Class

            Module M
             Sub Main()
              Dim d As New Derived()
             End Sub
            End Module
            """), Is.Empty);
    }

    /// <summary>
    /// ⚠ A base that declares NO constructor at all gets the implicit default, so it is
    /// constructible — the check must distinguish "declares none" from "declares only ones that
    /// need arguments". Both look like "no <c>.ctor0</c> key".
    /// </summary>
    [Test]
    public void ABaseWithNoConstructorAtAll_IsAccepted()
    {
        Assert.That(OptionalConstructorTests.Analyze("""
            Class Base
             Public F As Integer
            End Class

            Class Derived
             Inherits Base
            End Class

            Module M
             Sub Main()
              Dim d As New Derived()
              PrintLine("ok")
             End Sub
            End Module
            """), Is.Empty);
    }

    /// <summary>
    /// ⛔ THE discriminating case. An all-<c>Optional</c> base constructor has no <c>.ctor0</c> key,
    /// so a check written against the key alone would reject this legal program. Asking
    /// <c>ResolveConstructor(baseType, 0)</c> gets it right because that is the same question a
    /// <c>New Base()</c> site asks.
    /// </summary>
    [Test]
    public void AnAllOptionalBase_IsAccepted()
    {
        Assert.That(OptionalConstructorTests.Analyze("""
            Class Base
             Public Sub New(Optional a As Integer = 3)
              PrintLine("base:" & CStr(a))
             End Sub
            End Class

            Class Derived
             Inherits Base
             Public Sub New()
              PrintLine("derived")
             End Sub
            End Class

            Module M
             Sub Main()
              Dim d As New Derived()
             End Sub
            End Module
            """), Is.Empty);
    }

    /// <summary>⚠ A class with no base at all is not in this check's business.</summary>
    [Test]
    public void AClassWithNoBase_IsUntouched()
    {
        Assert.That(OptionalConstructorTests.Analyze("""
            Class Solo
             Public Sub New(a As Integer)
              PrintLine("solo:" & CStr(a))
             End Sub
            End Class

            Module M
             Sub Main()
              Dim s As New Solo(5)
             End Sub
            End Module
            """), Is.Empty);
    }

    // ====================================================================================
    // Accepting it is not enough — it has to RUN.
    // ====================================================================================

    /// <summary>
    /// ⛔ Accepting the all-Optional base would be certifying a program that does not build, so the
    /// implicit base call FILLS the base's defaults. Measured before: the analyzer said yes and
    /// then MSIL threw <c>MissingMethodException: Void Base..ctor()</c> and C# was CS7036, because
    /// the emitted base call passed nothing at a constructor that declares one parameter.
    /// </summary>
    [Test]
    [Category("Integration")]
    public void AnImplicitBaseCall_FillsTheBasesOptionalDefaults()
    {
        const string program = """
            Class Base
             Public Sub New(Optional a As Integer = 3)
              PrintLine("base:" & CStr(a))
             End Sub
            End Class

            Class Derived
             Inherits Base
             Public Sub New()
              PrintLine("derived")
             End Sub
            End Class

            Module M
             Sub Main()
              Dim d As New Derived()
             End Sub
            End Module
            """;

        Assert.Multiple(() =>
        {
            Assert.That(ReturnCoercionTests.CompileEmittedCSharpForTest(program), Is.Empty);
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo("base:3\nderived"));
            Assert.That(Msil.MsilHarness.RunExpectingSuccess(program),
                Is.EqualTo("base:3\nderived\n"));
        });
    }

    /// <summary>
    /// ⚠ An EXPLICIT <c>MyBase.New</c> must be unaffected by the implicit-fill path — the two are
    /// separate branches and only one may run.
    /// </summary>
    [Test]
    [Category("Integration")]
    public void AnExplicitMyBaseNew_IsUnaffected()
    {
        Assert.That(Msil.MsilHarness.RunExpectingSuccess("""
            Class Base
             Public Sub New(Optional a As Integer = 3)
              PrintLine("base:" & CStr(a))
             End Sub
            End Class

            Class Derived
             Inherits Base
             Public Sub New()
              MyBase.New(9)
             End Sub
            End Class

            Module M
             Sub Main()
              Dim d As New Derived()
             End Sub
            End Module
            """), Is.EqualTo("base:9\n"));
    }

    /// <summary>
    /// ⛔ A class declaring NO constructor at all, whose base takes only <c>Optional</c>
    /// parameters. The analyzer accepts it — correctly, the base IS callable with no arguments —
    /// and it used to be a legal program every backend MISCOMPILED: there was no
    /// <c>IRConstructor</c> to hang the filled defaults on, so each backend invented a bare
    /// no-argument base call. Measured before: C# emitted <c>class Derived : Base</c> with no
    /// constructor and got <b>CS7036</b>; MSIL threw
    /// <c>MissingMethodException: Void Base..ctor()</c>.
    ///
    /// <para>⚠ Fixed by SYNTHESIZING one (<c>IRBuilder.SynthesizeImplicitConstructor</c>) — a real
    /// <c>IRFunction</c> with an entry block and a return, not an <c>IRConstructor</c> with a null
    /// Implementation. Only MSIL has a synthesize-a-default path at all; C#, JavaScript and C++
    /// lean on their target language's implicit constructor, so handing them a shape no declared
    /// constructor ever produces is how one of them would break uncovered.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    public void AClassWithNoConstructor_AndAnAllOptionalBase_FillsTheDefaultsToo()
    {
        const string program = """
            Class Base
             Public Sub New(Optional a As Integer = 3)
              PrintLine("base:" & CStr(a))
             End Sub
            End Class

            Class Derived
             Inherits Base
            End Class

            Module M
             Sub Main()
              Dim d As New Derived()
             End Sub
            End Module
            """;

        Assert.Multiple(() =>
        {
            Assert.That(OptionalConstructorTests.Analyze(program), Is.Empty);
            Assert.That(ReturnCoercionTests.CompileEmittedCSharpForTest(program), Is.Empty);
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo("base:3"));
            Assert.That(Msil.MsilHarness.RunExpectingSuccess(program), Is.EqualTo("base:3\n"));
            Assert.That(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program)),
                Is.EqualTo("base:3\n"));
        });
    }

    /// <summary>
    /// ⚠ A class with no base, or one whose base constructor takes NO parameters, must NOT acquire
    /// a synthesized constructor — there is nothing to fill, and the backends' own default is what
    /// every existing class in the repo relies on.
    ///
    /// <para>⛔ The <c>Constructors</c> assertions are STRUCTURAL on purpose. The run assertions
    /// below them pass either way: measured by mutation, an empty synthesized constructor and the
    /// default each backend invents behave identically, so a runtime-only test here would hold
    /// nothing. Asserting the IR is what makes the early-out in
    /// <c>SynthesizeImplicitConstructor</c> a claim a test can falsify.</para>
    ///
    /// <para>⚠ The two shapes reach the early-out by DIFFERENT routes, which is why both are here:
    /// <c>parameterlessBase</c> has a bound base constructor with zero parameters (the
    /// parameter-count guard stops it), while <c>noBaseCtor</c>'s base declares no constructor at
    /// all, so the analyzer records no binding and the lookup guard stops it first.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    public void NothingToFill_MeansNoSynthesizedConstructor()
    {
        const string parameterlessBase = """
            Class Base
             Public Sub New()
              PrintLine("base0")
             End Sub
            End Class

            Class Derived
             Inherits Base
            End Class

            Module M
             Sub Main()
              Dim d As New Derived()
             End Sub
            End Module
            """;

        const string noBaseCtor = """
            Class Base
             Public F As Integer
            End Class

            Class Derived
             Inherits Base
            End Class

            Module M
             Sub Main()
              Dim d As New Derived()
              PrintLine("ok")
             End Sub
            End Module
            """;

        Assert.Multiple(() =>
        {
            Assert.That(JsTestSupport.BuildModule(parameterlessBase).Classes["Derived"].Constructors,
                Is.Empty,
                "a zero-parameter base has nothing to fill — no constructor may be synthesized");
            Assert.That(JsTestSupport.BuildModule(noBaseCtor).Classes["Derived"].Constructors,
                Is.Empty,
                "a base declaring no constructor records no binding — nothing to synthesize from");

            Assert.That(Msil.MsilHarness.RunExpectingSuccess(parameterlessBase),
                Is.EqualTo("base0\n"));
            Assert.That(Msil.MsilHarness.RunExpectingSuccess(noBaseCtor), Is.EqualTo("ok\n"));
            Assert.That(JavaScriptExecutionTests.RunJs(parameterlessBase), Is.EqualTo("base0"));
            Assert.That(JavaScriptExecutionTests.RunJs(noBaseCtor), Is.EqualTo("ok"));
        });
    }

    /// <summary>
    /// ⚠ The synthesized constructor is SHAPED like a declared one, which is the whole reason it is
    /// a real <c>IRFunction</c> rather than an <c>IRConstructor</c> with a null Implementation: an
    /// entry block, a terminating return, no parameters, and the filled base call.
    ///
    /// <para>⛔ STRUCTURAL because nothing else can see it. Measured by mutation: dropping the
    /// terminating return breaks no backend — every one of them tolerates the unterminated block —
    /// so the only thing holding the invariant is an assertion on the IR itself. An unterminated
    /// block is malformed IR whether or not today's four backends happen to cope, and
    /// <c>Visit(ConstructorNode)</c> terminates every declared constructor the same way.</para>
    /// </summary>
    [Test]
    public void TheSynthesizedConstructor_IsShapedLikeADeclaredOne()
    {
        var module = JsTestSupport.BuildModule("""
            Class Base
             Public Sub New(Optional a As Integer = 3)
              PrintLine("base:" & CStr(a))
             End Sub
            End Class

            Class Derived
             Inherits Base
            End Class

            Module M
             Sub Main()
              Dim d As New Derived()
             End Sub
            End Module
            """);

        var derived = module.Classes["Derived"];

        Assert.That(derived.Constructors, Has.Count.EqualTo(1),
            "the class declares none, so exactly one must have been synthesized");

        var ctor = derived.Constructors[0];

        Assert.Multiple(() =>
        {
            Assert.That(ctor.Implementation, Is.Not.Null,
                "a null Implementation is a shape no declared constructor produces");
            Assert.That(ctor.Implementation.Parameters, Is.Empty);
            Assert.That(ctor.Implementation.EntryBlock, Is.Not.Null);
            Assert.That(ctor.Implementation.EntryBlock.IsTerminated(), Is.True,
                "an unterminated entry block is malformed IR even where a backend tolerates it");
            Assert.That(ctor.BaseConstructorArgs, Has.Count.EqualTo(1),
                "the base's one Optional default must be filled");
        });
    }

    /// <summary>
    /// ⛔ The remaining gap, and it is the one PR #31 already pinned rather than a new one: a base
    /// <c>Optional</c> default that is an EXPRESSION rather than a literal
    /// (<c>Optional a As Integer = 2 + 3</c>). The filled value is a temp the constructor BODY
    /// computes, and a base call must precede the body — so MSIL refuses it by the same rule that
    /// refuses <c>MyBase.New(v + 1)</c>, and C# emits <c>: base(t0)</c> naming a temp out of scope
    /// (<b>CS0103</b>).
    ///
    /// <para>⚠ NOT a regression: this shape was already broken before the synthesized constructor
    /// existed (it was CS7036 / MissingMethodException then, CS0103 / a clear refusal now). It is
    /// the same IR-level gap — <c>BaseConstructorArgs</c> is not self-contained — and closing THAT
    /// closes this. Pinned here so the synthesized-constructor position is named in it.</para>
    /// </summary>
    [Test]
    public void ANonLiteralOptionalDefault_IsStillTheComputedArgumentGap()
    {
        const string program = """
            Class Base
             Public Sub New(Optional a As Integer = 2 + 3)
              PrintLine("base:" & CStr(a))
             End Sub
            End Class

            Class Derived
             Inherits Base
            End Class

            Module M
             Sub Main()
              Dim d As New Derived()
             End Sub
            End Module
            """;

        Assert.That(ReturnCoercionTests.CompileEmittedCSharpForTest(program),
            Has.Some.Contains("CS0103"),
            "when this stops failing, BaseConstructorArgs has become self-contained — that closes "
            + "MsilBaseConstructorTests.AComputedBaseArgument_IsRefused_NotSilentlyZero too");
    }
}
