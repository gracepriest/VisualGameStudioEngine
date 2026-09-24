using NUnit.Framework;
using VisualGameStudio.Tests.Msil;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// <c>Right</c>'s receiver and length on the C# backend — the sixth defect of the C#-backend batch
/// that <see cref="CSharpLoopExitTests"/> covers the other five of.
///
/// <para>⛔ <b>What was wrong.</b> <c>CSharpStdLibProvider.EmitRight</c> emitted
/// <c>{str}.Substring({str}.Length - {length})</c>, interpolating the RECEIVER EXPRESSION TWICE and
/// parenthesizing neither operand. Three separate consequences, all measured at f20435d:</para>
///
/// <list type="bullet">
/// <item>an effectful receiver RAN TWICE — <c>Right(Tag(), 2)</c> printed <c>tag</c> twice;</item>
/// <item>a receiver that is not a single term did not even COMPILE —
/// <c>Right(Ab() &amp; "cdef", 2)</c> emitted <c>Ab() + "cdef".Length - 2</c> and Roslyn refused it
/// with <c>CS0019: Operator '-' cannot be applied to operands of type 'string' and 'int'</c>;</item>
/// <item>a length that is not a single term bound wrong — <c>Right("abcdef", Len(Ab()) + 1)</c>
/// computed <c>Length - Len(Ab()) + 1</c> and printed <c>[f]</c> where every other backend printed
/// <c>[def]</c>: a program that compiled, ran and answered wrongly.</item>
/// </list>
///
/// <para>The fix is <c>({str})[^({length})..]</c> — one evaluation of the receiver, and both operands
/// parenthesized.</para>
///
/// <para>⚠ <b>The receiver and the length must be UN-FOLDABLE.</b> <c>Right("ab" &amp; "cdef", 2)</c>
/// is constant-folded by the IR optimizer into a single literal before the backend sees it, and the
/// folded program cannot see the parenthesization at all. Every case here routes through a
/// <c>Function</c> call for that reason.</para>
///
/// <para>⚠ <b>Not duplicated here:</b> the "receiver evaluated exactly once" shape lives in
/// <see cref="MsilStringIntrinsicTests"/>
/// (<c>RightWithAnEffectfulReceiver_IsEvaluatedOnce</c>), which now holds both .NET backends to it;
/// and the out-of-range shapes for a length past the end of the string and for an empty receiver are
/// pinned there too. Only the NEGATIVE length was uncovered, and it is added below.</para>
///
/// <para>⚠ Kept to ONE shape per test — <c>FourBackends</c> and <c>MsilHarness</c> both use
/// <c>Assert.Multiple</c> internally.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class CSharpRightReceiverTests
{
    /// <summary>
    /// ⭐ A receiver that is a CONCATENATION, built through a call so the optimizer cannot fold it.
    /// <c>Right("abcdef", 2)</c> = <c>ef</c>. ⛔ MEASURED at f20435d: the emitted C# did not compile
    /// (<c>CS0019</c>, <c>string</c> minus <c>int</c>) — the missing parentheses bound
    /// <c>.Length - 2</c> to the string literal alone. ⛔ <c>n = 2</c>, not 3: with a 6-character
    /// string a correct <c>Right(s, 3)</c> and a version that forgets the subtraction both answer
    /// <c>def</c>.
    /// </summary>
    [Test]
    public void Right_OverAConcatenatedReceiver_TakesTheLastNOfTheWholeString()
        => FourBackends.RunsOnEveryBackend(
            "Function Ab() As String\n" +
            " Return \"ab\"\n" +
            "End Function\n\n" +
            "Sub Main()\n" +
            " PrintLine(\"[\" & Right(Ab() & \"cdef\", 2) & \"]\")\n" +
            "End Sub",
            "[ef]");

    /// <summary>
    /// ⭐ A LENGTH that is a sum, again built through a call so it cannot be folded.
    /// <c>Len("ab") + 1</c> = 3, so the answer is the last three characters of <c>abcdef</c>.
    /// ⛔ MEASURED at f20435d: <b>[f]</b> — <c>Length - Len(Ab()) + 1</c> is <c>(6 - 2) + 1 = 5</c>,
    /// a program that compiled, ran and printed the wrong substring.
    ///
    /// <para>⚠ <b>C++ is excluded</b>: measured, the C++ backend's <c>right</c> arm calls
    /// <c>.substr</c> directly on its argument, so a STRING-LITERAL receiver is a bare
    /// <c>const char*</c> and the program does not compile —
    /// <c>error: member reference base type 'const char[7]' is not a structure or union</c>. That is
    /// the pre-existing C++ gap <see cref="MsilStringIntrinsicTests"/> already records for this whole
    /// intrinsic family, not this family's defect. The concatenated-receiver case above does not hit
    /// it (its receiver is a real <c>String</c>) and is asserted against all four backends.</para>
    /// </summary>
    [Test]
    public void Right_WithAComputedLength_TakesThatManyCharacters()
    {
        const string program =
            "Function Ab() As String\n" +
            " Return \"ab\"\n" +
            "End Function\n\n" +
            "Sub Main()\n" +
            " PrintLine(\"[\" & Right(\"abcdef\", Len(Ab()) + 1) & \"]\")\n" +
            "End Sub";
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(program)), Is.EqualTo("[def]"), "JavaScript");
            Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(program)), Is.EqualTo("[def]"), "MSIL");
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(program)), Is.EqualTo("[def]"), "C#");
        });
    }

    /// <summary>
    /// A NEGATIVE length still throws, and throws <c>ArgumentOutOfRangeException</c> — the third of
    /// the three out-of-range shapes in the contract, and the one
    /// <see cref="MsilStringIntrinsicTests"/> did not already pin. This is contract, not accident:
    /// the C# backend's <c>Right</c> is a raw BCL range index with no clamping (real VB clamps; this
    /// front end deliberately does not — see that fixture's note).
    ///
    /// <para>⚠ C#-only, on purpose. Only the exception TYPE is asserted, never the message: that is
    /// BCL text and it moves between runtimes.</para>
    /// </summary>
    [Test]
    public void Right_WithANegativeLength_Throws()
        => Assert.That(
            () => FourBackends.RunEmittedCSharp(
                "Sub Main()\n" +
                " PrintLine(\"[\" & Right(\"abcdef\", -1) & \"]\")\n" +
                "End Sub"),
            Throws.InstanceOf<AssertionException>().With.Message.Contains("ArgumentOutOfRangeException"));
}
