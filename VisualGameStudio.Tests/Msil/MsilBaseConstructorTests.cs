using NUnit.Framework;
using VisualGameStudio.Tests.Compiler;
using static VisualGameStudio.Tests.Msil.MsilHarness;

namespace VisualGameStudio.Tests.Msil;

/// <summary>
/// <c>MyBase.New(…)</c> on the MSIL backend.
///
/// <para>⛔ The arguments were DROPPED. <c>IRConstructor.BaseConstructorArgs</c> was never read by
/// the generator — the emission was a fixed <c>call instance void Base::.ctor()</c> whatever was
/// written — so every base constructor that takes arguments died with
/// <c>MissingMethodException: Void Base..ctor()</c>. Measured as MSIL-only: C#, JavaScript and
/// C++ all pass them, and the same program prints <c>base:7,9</c> on each.</para>
///
/// <para>⚠ It was pinned by <c>OptionalConstructorTests</c> rather than fixed when the Optional
/// work landed, and proved pre-existing there by supplying EVERY argument to a base constructor
/// with no <c>Optional</c> anywhere — the same failure. This fixture is the promotion of that
/// pin.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class MsilBaseConstructorTests
{
    /// <summary>
    /// ⛔ The headline shape: literal arguments, two of them.
    /// <c>MissingMethodException: Void Base..ctor()</c> before.
    /// </summary>
    [Test]
    public void LiteralBaseArguments_ReachTheBaseConstructor()
    {
        Assert.That(RunExpectingSuccess("""
            Class Base
             Public Sub New(a As Integer, b As Integer)
              PrintLine("base:" & CStr(a) & "," & CStr(b))
             End Sub
            End Class

            Class Derived
             Inherits Base
             Public Sub New()
              MyBase.New(7, 9)
             End Sub
            End Class

            Module M
             Sub Main()
              Dim d As New Derived()
             End Sub
            End Module
            """), Is.EqualTo("base:7,9\n"));
    }

    /// <summary>
    /// ⚠ A PARAMETER of the derived constructor, which is the shape that actually makes
    /// inheritance useful — it is an <c>ldarg</c>, available before the body runs.
    /// </summary>
    [Test]
    public void AConstructorParameter_CanBeForwardedToTheBase()
    {
        Assert.That(RunExpectingSuccess("""
            Class Base
             Public Sub New(a As Integer)
              PrintLine("base:" & CStr(a))
             End Sub
            End Class

            Class Derived
             Inherits Base
             Public Sub New(v As Integer)
              MyBase.New(v)
             End Sub
            End Class

            Module M
             Sub Main()
              Dim d As New Derived(41)
             End Sub
            End Module
            """), Is.EqualTo("base:41\n"));
    }

    /// <summary>
    /// ⚠ THREE levels, so the base call is both a caller and a callee. A fix that only handled the
    /// leaf would pass this one's first line and fail its second.
    /// </summary>
    [Test]
    public void MultiLevelInheritance_ThreadsArgumentsAllTheWayUp()
    {
        Assert.That(RunExpectingSuccess("""
            Class A
             Public Sub New(n As Integer)
              PrintLine("A:" & CStr(n))
             End Sub
            End Class

            Class B
             Inherits A
             Public Sub New(n As Integer)
              MyBase.New(n)
              PrintLine("B")
             End Sub
            End Class

            Class C
             Inherits B
             Public Sub New()
              MyBase.New(3)
              PrintLine("C")
             End Sub
            End Class

            Module M
             Sub Main()
              Dim c As New C()
             End Sub
            End Module
            """), Is.EqualTo("A:3\nB\nC\n"));
    }

    /// <summary>
    /// ⚠ A base constructor with an omitted <c>Optional</c>. The fill itself landed with the
    /// Optional work, but MSIL could not show it because the arguments never arrived —
    /// <c>OptionalConstructorTests.AMyBaseNewCall_FillsAnOmittedOptional</c> had to assert the
    /// other three backends. It can now assert this one too, and does.
    /// </summary>
    [Test]
    public void AnOmittedOptionalOnTheBaseConstructor_IsFilledAndPassed()
    {
        Assert.That(RunExpectingSuccess("""
            Class Base
             Public Sub New(a As Integer, Optional b As Integer = 5)
              PrintLine("base:" & CStr(a) & "," & CStr(b))
             End Sub
            End Class

            Class Derived
             Inherits Base
             Public Sub New()
              MyBase.New(7)
             End Sub
            End Class

            Module M
             Sub Main()
              Dim d As New Derived()
             End Sub
            End Module
            """), Is.EqualTo("base:7,5\n"));
    }

    /// <summary>
    /// ⚠ A class with no base and one with a PARAMETERLESS base both still emit the plain
    /// <c>::.ctor()</c> they always did — the change must not disturb the path that worked.
    /// </summary>
    [Test]
    public void AParameterlessBase_IsUnchanged()
    {
        Assert.That(RunExpectingSuccess("""
            Class Base
             Public Sub New()
              PrintLine("base0")
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
            """), Is.EqualTo("base0\nderived\n"));
    }

    /// <summary>
    /// ⚠ The class declares NO constructor at all, and its base takes an omitted
    /// <c>Optional</c>. Before the synthesized <c>IRConstructor</c> there was nothing in the IR
    /// to hang the filled base call on: <c>Constructors</c> was empty, so MSIL invented a
    /// default <c>.ctor</c> whose base call passed no arguments, and the base's own default was
    /// never consulted — the program printed <c>base:0</c>.
    /// </summary>
    [Test]
    public void ANoConstructorClass_StillReachesTheBase_WithOptionalsFilled()
    {
        Assert.That(RunExpectingSuccess("""
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
            """), Is.EqualTo("base:3\n"));
    }

    /// <summary>
    /// ⚠ What the <c>_currentFunction</c> clear at the end of
    /// <c>IRBuilder.SynthesizeImplicitConstructor</c> is FOR, pinned end to end.
    ///
    /// <para><c>Visit(VariableDeclarationNode)</c> decides global-versus-local purely on
    /// <c>_currentFunction == null</c>. The synthesized constructor points that field at its own
    /// function in order to emit an expression-valued default into the right body, so leaving it
    /// set sends the NEXT module-level <c>Dim</c> down the local-variable branch: <c>G</c> is
    /// never registered as a global, no static field is emitted, and the program dies at run time
    /// with <c>InvalidProgramException</c> rather than printing. Measured — that is exactly what
    /// the mutation that drops the clear produces here.</para>
    ///
    /// <para>⚠ The initializer is a plain literal ON PURPOSE. A module-scope initializer that
    /// needs a temp (<c>= 40 + 2</c>) calls <c>GetNextTempName()</c> on the null
    /// <c>_currentFunction</c> and crashes the IR builder — a PRE-EXISTING gap, confirmed on
    /// unmodified master with no class in the file at all, and wider than the "New initializers"
    /// note at that branch claims. A computed initializer here would fail for that reason instead
    /// of this one and prove nothing.</para>
    /// </summary>
    [Test]
    public void TheSynthesizedConstructor_DoesNotLeakIntoTheNextModuleGlobal()
    {
        Assert.That(RunExpectingSuccess("""
            Class Base
             Public Sub New(Optional a As Integer = 3)
              PrintLine("base:" & CStr(a))
             End Sub
            End Class

            Class Derived
             Inherits Base
            End Class

            Module M
             Dim G As Integer = 42
             Sub Main()
              Dim d As New Derived()
              PrintLine("g=" & CStr(G))
             End Sub
            End Module
            """), Is.EqualTo("base:3\ng=42\n"));
    }

    // ====================================================================================
    // What is REFUSED, and why refusing beats emitting.
    // ====================================================================================

    /// <summary>
    /// ⭐ MOVED PIN (ADR-0016 / #170). A COMPUTED base argument used to be refused rather than
    /// emitted: IL requires the base constructor call before the constructor body, so a value the
    /// body computed did not exist yet, and the same program did not build on C# either — it
    /// emitted <c>: base(t0)</c>, naming a temp that was not in scope (CS0103). Both were the same
    /// fault: the temp's DEFINING instruction sat in the entry block while the call site itself
    /// sat off-stream in <c>IRConstructor.BaseConstructorArgs</c>, a list no block held.
    ///
    /// <para>#170's <c>IRBaseConstructorCall</c> terminates the entry block's prologue, so
    /// <c>v + 1</c> is an ordinary prologue instruction the base call consumes in place — MSIL
    /// emits it before <c>call instance void Base::.ctor(...)</c>, C# renders it as the
    /// parenthesised expression <c>: base((v + 1))</c>. <c>base:42</c> now runs on C#, JavaScript,
    /// MSIL AND C++ (measured, <c>S/t170/ctrlW/W1</c>'s shape; here with <c>v=41</c>).</para>
    /// </summary>
    [Test]
    public void AComputedBaseArgument_RunsOnEveryBackend()
    {
        const string program = """
            Class Base
             Public Sub New(a As Integer)
              PrintLine("base:" & CStr(a))
             End Sub
            End Class

            Class Derived
             Inherits Base
             Public Sub New(v As Integer)
              MyBase.New(v + 1)
             End Sub
            End Class

            Module M
             Sub Main()
              Dim d As New Derived(41)
             End Sub
            End Module
            """;

        FourBackends.RunsOnEveryBackend(program, "base:42");
    }

    // ⚠ The pin that used to live here — a derived class with NO constructor whose base requires
    // arguments, dying at run time with MissingMethodException — is gone because the FRONT END now
    // rejects that program (VB's BC30387). It is no longer an MSIL shape at all:
    // BaseConstructorDiagnosticTests owns it. It cannot even be written through MsilHarness, whose
    // CompileToIl asserts a clean analyze, so a rejected program fails the harness's own assertion
    // rather than returning an outcome.
}
