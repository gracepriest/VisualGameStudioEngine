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
    /// ⛔ The remaining gap, pinned: a class declaring NO constructor at all, whose base takes only
    /// <c>Optional</c> parameters. The analyzer correctly accepts it — the base IS callable with no
    /// arguments — but there is no <c>IRConstructor</c> to hang the filled defaults on, so every
    /// backend synthesizes a bare no-argument base call. Measured: C# emits
    /// <c>class Derived : Base</c> with no constructor and gets CS7036; MSIL throws
    /// <c>MissingMethodException: Void Base..ctor()</c>.
    ///
    /// <para>⚠ Closing it means SYNTHESIZING an <c>IRConstructor</c> for such a class so all four
    /// backends receive the filled call, rather than each inventing a default. Deliberately not
    /// bundled: the sibling shape — the same base with a declared constructor — works, and is
    /// asserted above.</para>
    /// </summary>
    [Test]
    public void AClassWithNoConstructor_AndAnAllOptionalBase_IsAcceptedButStillDoesNotBuild()
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
            Assert.That(OptionalConstructorTests.Analyze(program), Is.Empty,
                "the analyzer is right to accept it — the base IS callable with no arguments");
            Assert.That(ReturnCoercionTests.CompileEmittedCSharpForTest(program),
                Has.Some.Contains("CS7036"),
                "when this stops failing, a synthesized IRConstructor carries the filled base "
                + "call — delete this pin and assert the run");
        });
    }
}
