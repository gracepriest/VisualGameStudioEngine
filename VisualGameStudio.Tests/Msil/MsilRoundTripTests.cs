using System;
using NUnit.Framework;
using static VisualGameStudio.Tests.Msil.MsilHarness;

namespace VisualGameStudio.Tests.Msil;

/// <summary>
/// The MSIL backend's capability matrix, established by RUNNING generated programs.
///
/// <para><b>This fixture is new because the backend had none.</b> 2,136 lines of generator
/// shipped with zero tests while JavaScript had three files and C++ had many. The first probe
/// found a <c>Select Case</c> that assembles, runs, and silently answers <c>Else</c> for every
/// input — a class of defect that only a round trip catches, and that a backend with a test
/// suite would not have kept.</para>
///
/// <para><b>Known-broken shapes are pinned too</b>, as <c>_PinnedDivergence</c> tests that
/// assert the CURRENT wrong behaviour. That is not blessing it: it is the difference between a
/// gap someone measured and a gap nobody knows about. Each one carries the root cause and goes
/// RED the moment it starts working, which is the signal to promote it to a real assertion.
/// The repo already uses this convention for the .NET boundary's divergences.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class MsilRoundTripTests
{
    // ====================================================================================
    // What works. These are the floor — a regression here is a real regression.
    // ====================================================================================

    [Test]
    public void ArithmeticAndLocals_Run()
    {
        // PrintLine rather than Console.WriteLine deliberately: it is the ONE output path that
        // works today (TryEmitStdLibCall's "printline" arm), so it is the oracle every other
        // assertion in this fixture is built on. See ConsoleWriteLine_IsAPhantomSelfCall.
        Assert.That(RunExpectingSuccess("""
            Module M
             Sub Main()
              Dim a As Integer = 20
              Dim b As Integer = a + 1
              If b = 21 Then
               PrintLine("TWENTY-ONE")
              End If
             End Sub
            End Module
            """), Is.EqualTo("TWENTY-ONE\n"));
    }

    [Test]
    public void IfElse_TakesTheRightBranch()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Sub Main()
              Dim n As Integer = 7
              If n > 5 Then
               PrintLine("BIG")
              Else
               PrintLine("SMALL")
              End If
             End Sub
            End Module
            """), Is.EqualTo("BIG\n"));
    }

    [Test]
    public void StringConcatenation_Runs()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Sub Main()
              Dim s As String = "a" & "b"
              PrintLine(s)
             End Sub
            End Module
            """), Is.EqualTo("ab\n"));
    }

    /// <summary>
    /// A loop whose RESULT is observable. A loop that merely runs proves nothing — it has to
    /// be able to get the arithmetic wrong and be caught, which means the value has to escape.
    /// </summary>
    [Test]
    public void AWhileLoop_AccumulatesCorrectly()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Sub Main()
              Dim i As Integer = 0
              Dim s As String = ""
              While i < 3
               i = i + 1
               s = s & "X"
              End While
              If i = 3 Then
               PrintLine(s)
              End If
             End Sub
            End Module
            """), Is.EqualTo("XXX\n"));
    }

    [Test]
    public void AUserFunctionsReturnValue_CrossesCorrectly()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Function Twice(v As Integer) As Integer
              Return v + v
             End Function
             Sub Main()
              If Twice(21) = 42 Then
               PrintLine("FORTY-TWO")
              Else
               PrintLine("WRONG")
              End If
             End Sub
            End Module
            """), Is.EqualTo("FORTY-TWO\n"));
    }

    // ====================================================================================
    // Pinned divergences. Each names its root cause; each goes RED when fixed.
    // ====================================================================================

    /// <summary>
    /// ⛔ <b>Select Case is silently WRONG — the most dangerous defect in this backend.</b>
    ///
    /// <para>It assembles and runs, and every input falls to <c>Case Else</c>. The generated IL
    /// for a two-case switch is <c>ldc.i4.2 / ldloc.0 / br switch0default</c> — both operands
    /// are loaded and then an UNCONDITIONAL branch is taken. No <c>beq</c>, no <c>ceq</c>, no
    /// conditional branch of any kind is emitted, so the case tests do not exist.</para>
    ///
    /// <para>Cross-checked: the C# backend emits <c>case 2: "TWO"</c> for this same source, so
    /// this is an MSIL defect and not a language semantic.</para>
    /// </summary>
    [Test]
    public void SelectCase_AlwaysTakesElse_PinnedDivergence()
    {
        var output = RunExpectingSuccess("""
            Module M
             Sub Main()
              Dim n As Integer = 2
              Select Case n
               Case 1
                PrintLine("ONE")
               Case 2
                PrintLine("TWO")
               Case Else
                PrintLine("OTHER")
              End Select
             End Sub
            End Module
            """);

        Assert.That(output, Is.EqualTo("OTHER\n"),
            "PINNED WRONG ANSWER: n = 2 must print TWO. When this goes red because it prints "
            + "TWO, the generator learned to emit case comparisons — delete this test and "
            + "assert the correct answer.");
    }

    /// <summary>
    /// ⛔ <c>Console.WriteLine</c> emits <c>call object &lt;Module&gt;::ConsoleWriteLine(string)</c>
    /// — a call to a method on the GENERATED class that is never defined anywhere in the file.
    /// ilasm accepts it (a MemberRef needs no definition), so it fails at RUN time with
    /// <c>MissingMethodException</c>.
    ///
    /// <para>Root cause: <c>TryEmitStdLibCall</c> matches unqualified stdlib names only
    /// (<c>printline</c>, <c>sqrt</c>, …). Any <c>Type.Member(...)</c> call misses every arm and
    /// falls through to the emit-a-call-on-the-current-class default. The correct emission
    /// already exists in that function — <c>call void [mscorlib]System.Console::WriteLine(string)</c>
    /// — it is simply never reached for a qualified call.</para>
    /// </summary>
    [Test]
    public void ConsoleWriteLine_IsAPhantomSelfCall_PinnedDivergence()
    {
        var run = Run("""
            Module M
             Sub Main()
              Console.WriteLine("HELLO")
             End Sub
            End Module
            """);

        Assert.Multiple(() =>
        {
            Assert.That(run.Outcome, Is.EqualTo(MsilOutcome.RunFailed), run.Report);
            Assert.That(run.Output, Does.Contain("MissingMethodException"),
                "the phantom call must still be the failure; a DIFFERENT failure here means "
                + "something else broke on the way: " + run.Detail);
            Assert.That(run.Il, Does.Contain("ConsoleWriteLine"),
                "pinned on the emitted text as well, so the cause stays visible when the "
                + "runtime message changes between .NET versions.");
        });
    }

    // ---- Fixed 2026-09-15: IL type specs and tokens ------------------------------------
    //
    // IL spells a type differently in different positions, and the backend used one spelling
    // everywhere. A local/parameter/field takes a type SPEC (`int32`, `string`, `class Foo`,
    // `string[]`); box/newarr/castclass take a type TOKEN (`[mscorlib]System.Int32`). Since
    // `[X]` means "assembly X" in IL, the old `box [int32]` named a type in an assembly called
    // int32 and did not parse. IlTypeSpec/IlTypeToken now split the two.

    /// <summary>
    /// Boxing an Integer for <c>CStr</c>. The VALUE is asserted, not merely that it ran: a
    /// wrong token would still have to produce 42 to pass here.
    /// </summary>
    [Test]
    public void BoxingAnInteger_Runs_AndComputesTheRightValue()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Sub Main()
              Dim a As Integer = 21
              PrintLine(CStr(a + a))
             End Sub
            End Module
            """), Is.EqualTo("42\n"));
    }

    /// <summary>A local of a user class — <c>class Greeter</c>, not a bare <c>Greeter</c>.</summary>
    [Test]
    public void ALocalOfAUserClass_Runs()
    {
        Assert.That(RunExpectingSuccess("""
            Class Greeter
             Public Name As String
            End Class

            Module M
             Sub Main()
              Dim g As New Greeter()
              PrintLine("MADE")
             End Sub
            End Module
            """), Is.EqualTo("MADE\n"));
    }

    // ---- Still broken, each at its own root cause --------------------------------------

    /// <summary>
    /// ⛔ <b>An array local is declared but never ALLOCATED.</b> The type spec is right now
    /// (<c>string[]</c> assembles), so this moved from an ilasm rejection to a runtime
    /// <c>NullReferenceException</c> — the next cause in the same shape, and a more useful
    /// failure than the one it replaced.
    ///
    /// <para>Measured: the generated method contains ZERO <c>newarr</c> instructions.
    /// <c>Dim a(2) As String</c> must emit <c>ldc.i4.3 / newarr [mscorlib]System.String /
    /// stloc</c>, and element writes need <c>stelem</c>.</para>
    /// </summary>
    [Test]
    public void AnArrayLocal_IsNeverAllocated_PinnedDivergence()
    {
        var run = Run("""
            Module M
             Sub Main()
              Dim a(2) As String
              a(0) = "ZERO"
              PrintLine(a(0))
             End Sub
            End Module
            """);

        Assert.Multiple(() =>
        {
            Assert.That(run.Outcome, Is.EqualTo(MsilOutcome.RunFailed), run.Report);
            Assert.That(run.Output, Does.Contain("NullReferenceException"),
                "still the un-allocated array: " + run.Detail);
            Assert.That(run.Il, Does.Not.Contain("newarr"),
                "and the cause is still that no array is created. When newarr appears, this "
                + "test has done its job — assert the value instead.");
        });
    }

    /// <summary>
    /// ⛔ <b>A member call on a BCL receiver does not resolve to a BCL token</b> — the same
    /// root cause as <see cref="ConsoleWriteLine_IsAPhantomSelfCall_PinnedDivergence"/>, seen
    /// from the other side. <c>s.ToUpper()</c> emits a call on a class literally named
    /// <c>String</c> rather than <c>[mscorlib]System.String</c>, and ilasm refuses it outright
    /// instead of deferring to run time as it does for the phantom self-call.
    ///
    /// <para>Both are one missing step: qualified calls need the receiver's type resolved to an
    /// assembly-qualified token before the call is emitted.</para>
    /// </summary>
    [Test]
    public void AStringMethodCall_DoesNotResolveToTheBclType_PinnedDivergence()
    {
        var run = Run("""
            Module M
             Sub Main()
              Dim s As String = "hello"
              PrintLine(s.ToUpper())
             End Sub
            End Module
            """);

        Assert.Multiple(() =>
        {
            Assert.That(run.Outcome, Is.EqualTo(MsilOutcome.AssembleFailed), run.Report);
            Assert.That(run.Detail, Does.Contain("String"),
                "ilasm must still be refusing an undefined class 'String': " + run.Detail);
        });
    }

    /// <summary>
    /// ⛔ Calling an instance method on a user class produces a method the CLR rejects. The
    /// local and the object now exist (see <see cref="ALocalOfAUserClass_Runs"/>), so this is a
    /// CALL-side defect, distinct from the type-spec one that was fixed.
    /// </summary>
    [Test]
    public void AUserClassInstanceMethodCall_IsAnInvalidProgram_PinnedDivergence()
    {
        var run = Run("""
            Class Greeter
             Public Name As String
             Public Function Greet() As String
              Return "HI-" & Name
             End Function
            End Class

            Module M
             Sub Main()
              Dim g As New Greeter()
              g.Name = "BOB"
              PrintLine(g.Greet())
             End Sub
            End Module
            """);

        Assert.Multiple(() =>
        {
            Assert.That(run.Outcome, Is.EqualTo(MsilOutcome.RunFailed), run.Report);
            Assert.That(run.Output, Does.Contain("InvalidProgramException"), run.Detail);
        });
    }

    /// <summary>
    /// ⛔ <c>Try</c>/<c>Catch</c> assembles but the CLR rejects the method. IL exception
    /// handling is structural — <c>.try { … } catch … { … }</c> regions with <c>leave</c> out
    /// of each, recorded in the method's EH table — and emitting the bodies as straight-line
    /// code with ordinary branches produces a method the verifier will not accept.
    /// </summary>
    [Test]
    public void TryCatch_ProducesAnInvalidProgram_PinnedDivergence()
    {
        var run = Run("""
            Module M
             Sub Main()
              Try
               PrintLine("TRY")
              Catch ex As Exception
               PrintLine("CATCH")
              End Try
             End Sub
            End Module
            """);

        Assert.Multiple(() =>
        {
            Assert.That(run.Outcome, Is.EqualTo(MsilOutcome.RunFailed), run.Report);
            Assert.That(run.Output, Does.Contain("InvalidProgramException"),
                "the CLR must still be rejecting the method: " + run.Detail);
        });
    }
}
