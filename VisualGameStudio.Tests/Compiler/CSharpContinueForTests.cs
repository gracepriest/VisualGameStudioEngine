using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// <c>Continue For</c> on the C# backend, RUN — a counted For whose body ends in an If or a
/// Select has no direct branch to its step, so the backend used to write the step wherever the
/// walk first reached it: inside the arm that continued. That arm stepped; every other path
/// through the body did not, and the loop never ended (measured: <c>If i = 3 Then Continue For</c>
/// over 1..5). A first attempt jumped to a label nothing wrote — CS0159 on every such loop,
/// Continue or not. Both are visible only by compiling AND running, so every row here does both,
/// through <see cref="CSharpProcessRunner"/> (a hang fails one test, not the host).
/// </summary>
[TestFixture]
[Category("Integration")]
public class CSharpContinueForTests
{
    private static IEnumerable<TestCaseData> Programs()
    {
        yield return new TestCaseData(@"
Sub Main()
    Dim t As Integer = 0
    For i As Integer = 1 To 5
        If i = 3 Then Continue For
        t = t + i
    Next
    Console.WriteLine(t)
End Sub", "12").SetName("{m}(IfThenContinue)");

        yield return new TestCaseData(@"
Sub Main()
    Dim u As Integer = 0
    For j As Integer = 1 To 4
        If j Mod 2 = 0 Then
            u = u + 100
        Else
            u = u + j
        End If
    Next
    Console.WriteLine(u)
End Sub", "204").SetName("{m}(IfElseBodyNoContinue)");

        yield return new TestCaseData(@"
Sub Main()
    Dim w As Integer = 0
    For k As Integer = 1 To 6
        Select Case k
            Case 2, 4
                Continue For
            Case Else
                w = w + k
        End Select
    Next
    Console.WriteLine(w)
End Sub", "15").SetName("{m}(SelectCaseContinue)");

        yield return new TestCaseData(@"
Sub Main()
    Dim s As Integer = 0
    For a As Integer = 1 To 3
        For b As Integer = 1 To 3
            If b = 2 Then Continue For
            s = s + a * 10 + b
        Next
    Next
    Console.WriteLine(s)
End Sub", "132").SetName("{m}(NestedInnerContinue)");

        yield return new TestCaseData(@"
Sub Main()
    Dim n As Integer = 0
    For i As Integer = 1 To 10
        If i Mod 3 = 0 Then
            Continue For
        ElseIf i = 8 Then
            Exit For
        End If
        n = n + i
    Next
    Console.WriteLine(n)
End Sub", "19").SetName("{m}(ContinueAndExitInOneChain)");
    }

    [TestCaseSource(nameof(Programs))]
    public void RunsToTheVbAnswer_Standard(string source, string expected) =>
        Assert.That(CSharpProcessRunner.RunExpectingSuccess(ReturnCoercionTests.EmitCSharpForTest(source)).Trim(),
            Is.EqualTo(expected));

    [TestCaseSource(nameof(Programs))]
    public void RunsToTheVbAnswer_Aggressive(string source, string expected) =>
        Assert.That(CSharpProcessRunner.RunExpectingSuccess(ReturnCoercionTests.EmitCSharpAggressiveForTest(source)).Trim(),
            Is.EqualTo(expected));
}
