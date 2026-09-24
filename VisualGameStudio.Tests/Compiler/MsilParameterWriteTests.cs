using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Writing to a BYVAL parameter on MSIL — defect (A) on its own, with no <c>ByRef</c> anywhere.
///
/// <para>⛔ THE CAUSE. <c>MSILCodeGenerator.EmitStoreLocal</c> had no <c>starg</c> arm at all: it
/// walked locals, then instance fields, then properties, then static fields, then module globals,
/// and fell off the end for ANY parameter with <c>// WARNING: Cannot store to 'n'</c> — the
/// computed value stayed on the evaluation stack, and <c>ret</c> followed with a non-empty stack,
/// which the CLR rejects as an invalid program. A parameter that is only READ always worked;
/// <c>Sub Bump(n As Integer) : n = n + 1</c> — no ByRef, no loop — is the minimal program that
/// discriminates this: it threw <c>InvalidProgramException</c> before the fix and prints 42 now.
/// <c>CountedForVariableTests.CountedFor_OverAParameter_RunsOnEveryBackend_IncludingMsil</c> is
/// the counted-<c>For</c> shape of the same defect and lives in that fixture, next to its siblings.</para>
///
/// <para><b>This is a FOUR-BACKEND shape</b> — a ByVal write has nothing JavaScript refuses, so
/// every case here uses <see cref="FourBackends.RunsOnEveryBackend"/>, unlike the ByRef family in
/// <see cref="MsilByRefTests"/>.</para>
///
/// <para>⭐ SHADOWING gets two cases of its own: <c>EmitStoreLocal</c>'s new parameter arm has to
/// sit at the SAME point in the ladder <c>EmitLoadLocal</c> already checks a parameter at —
/// BEFORE the field/property/static-field arms — or a parameter shadowing a same-named field or
/// module global would read the parameter and silently write the outer one instead.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class MsilParameterWriteTests
{
    private static void RunsOnEveryBackend(string program, string expected) => FourBackends.RunsOnEveryBackend(program, expected);

    // ========================================================================================
    // THE HEADLINE — a ByVal parameter is written and re-read, in the same Sub, no loop.
    // ========================================================================================

    [Test]
    public void AByValParameter_IsWrittenAndReadBackInTheSameSub()
        => RunsOnEveryBackend("""
            Sub Bump(n As Integer)
             n = n + 1
             PrintLine(CStr(n))
            End Sub
            Sub Main()
             Bump(41)
            End Sub
            """, "42");

    // ========================================================================================
    // CONTEXT PATHS — a class instance method and a Function build through a different context
    // from a module Sub (InitializeMethodContext vs GenerateMethod), and a Function additionally
    // exercises the RETURN of a value computed from the written parameter.
    // ========================================================================================

    [Test]
    public void AnInstanceMethod_WritesItsOwnByValParameter()
        => RunsOnEveryBackend("""
            Class Worker
             Public Sub Go(n As Integer)
              n = n + 1
              PrintLine(CStr(n))
             End Sub
            End Class
            Sub Main()
             Dim w As New Worker()
             w.Go(41)
            End Sub
            """, "42");

    [Test]
    public void AFunction_WritesItsOwnByValParameter_AndReturnsIt()
        => RunsOnEveryBackend("""
            Function Increment(n As Integer) As Integer
             n = n + 1
             Return n
            End Function
            Sub Main()
             PrintLine(CStr(Increment(41)))
            End Sub
            """, "42");

    // ========================================================================================
    // TYPE — String is reference-typed on MSIL, the same shape ByRefString exercises for the
    // pointer arm; here there is no pointer at all, only the ordinary `starg`.
    // ========================================================================================

    [Test]
    public void AStringByValParameter_IsWrittenAndReadBack()
        => RunsOnEveryBackend("""
            Sub Shout(s As String)
             s = s & "!"
             PrintLine(s)
            End Sub
            Sub Main()
             Shout("hi")
            End Sub
            """, "hi!");

    // ========================================================================================
    // ACCUMULATION — the parameter is written repeatedly from a loop BODY (not as the loop's own
    // induction variable, which CountedForVariableTests already covers).
    // ========================================================================================

    [Test]
    public void AByValParameter_IsAccumulatedInALoopBody()
        => RunsOnEveryBackend("""
            Sub Accumulate(n As Integer)
             For i = 1 To 3
              n = n + i
             Next
             PrintLine(CStr(n))
            End Sub
            Sub Main()
             Accumulate(10)
            End Sub
            """, "16");

    // ========================================================================================
    // ⭐ SHADOWING — the store must resolve the same ladder the load does.
    // ========================================================================================

    [Test]
    public void AByValParameter_ShadowsAModuleGlobal_ForTheStoreAsWellAsTheLoad()
        => RunsOnEveryBackend("""
            Dim N As Integer = 100
            Sub Bump(N As Integer)
             N = N + 1
             PrintLine(CStr(N))
            End Sub
            Sub Main()
             Bump(5)
             PrintLine(CStr(N))
            End Sub
            """, "6\n100");

    [Test]
    public void AByValParameter_ShadowsAnInstanceField_ForTheStoreAsWellAsTheLoad()
        => RunsOnEveryBackend("""
            Class Holder
             Public N As Integer = 100
             Public Sub Bump(N As Integer)
              N = N + 1
              PrintLine(CStr(N))
             End Sub
            End Class
            Sub Main()
             Dim h As New Holder()
             h.Bump(5)
             PrintLine(CStr(h.N))
            End Sub
            """, "6\n100");
}
