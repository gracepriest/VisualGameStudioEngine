using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Plan task 19 — Try / Catch / Finally / Throw.
///
/// <para>JavaScript has all four natively, so this is one of the places the spec says JS gets
/// for free what the C++ backend had to construct. That makes the interesting tests the
/// SEMANTIC ones rather than the syntactic ones.</para>
///
/// <para><b>The headline test is a regression guard against a defect this backend must not
/// inherit.</b> On the C++ backend a <c>Return</c> inside a <c>Try</c> BYPASSES its
/// <c>Finally</c>. In JavaScript the language guarantees the opposite, so
/// <see cref="Finally_RunsEvenWhenTryReturns"/> passes the moment it is written — it is kept
/// precisely because it pins a known cross-backend divergence, not because it is expected to
/// fail.</para>
/// </summary>
[TestFixture]
[Category("Integration")]   // spawns node
public class JavaScriptExceptionTests
{
    private static string Run(string source) => JavaScriptExecutionTests.RunJs(source);

    private static string InMain(string body) =>
        JavaScriptExecutionTests.RunJs($"Sub Main()\n{body}\nEnd Sub");

    // ---------------------------------------------------------------- basics

    [Test]
    public void Try_WithoutAnException_SkipsTheCatch()
        => Assert.That(InMain(
            "Try\nConsole.WriteLine(\"body\")\nCatch ex As Exception\nConsole.WriteLine(\"caught\")\nEnd Try"),
            Is.EqualTo("body"));

    [Test]
    public void Finally_RunsOnTheNormalPath()
        => Assert.That(InMain(
            "Try\nConsole.WriteLine(\"body\")\nFinally\nConsole.WriteLine(\"finally\")\nEnd Try"),
            Is.EqualTo("body\nfinally"));

    [Test]
    public void Throw_IsCaught()
        => Assert.That(InMain(
            "Try\nThrow New Exception(\"boom\")\nConsole.WriteLine(\"unreached\")\n" +
            "Catch ex As Exception\nConsole.WriteLine(\"caught\")\nEnd Try"),
            Is.EqualTo("caught"));

    [Test]
    public void Finally_RunsAfterAnExceptionIsCaught()
        => Assert.That(InMain(
            "Try\nThrow New Exception(\"boom\")\n" +
            "Catch ex As Exception\nConsole.WriteLine(\"caught\")\n" +
            "Finally\nConsole.WriteLine(\"finally\")\nEnd Try"),
            Is.EqualTo("caught\nfinally"));

    /// <summary>Execution continues past End Try, exactly once.</summary>
    [Test]
    public void ExecutionContinuesAfterEndTry()
        => Assert.That(InMain(
            "Try\nConsole.WriteLine(\"a\")\nCatch ex As Exception\nEnd Try\nConsole.WriteLine(\"b\")"),
            Is.EqualTo("a\nb"));

    // ---------------------------------------------------------------- the guard

    /// <summary>
    /// A <c>Return</c> inside <c>Try</c> must still run <c>Finally</c>. This is a KNOWN break
    /// on the C++ backend; JavaScript guarantees it, so this passes on first write and exists
    /// to stop the defect ever following the IR into this backend.
    /// </summary>
    [Test]
    public void Finally_RunsEvenWhenTryReturns()
        => Assert.That(Run(
            "Function Compute() As Integer\n" +
            "Try\nReturn 1\nFinally\nConsole.WriteLine(\"finally\")\nEnd Try\n" +
            "End Function\n" +
            "Sub Main()\nConsole.WriteLine(Compute())\nEnd Sub"),
            Is.EqualTo("finally\n1"));

    /// <summary>And the returned value must survive the Finally unchanged.</summary>
    [Test]
    public void ReturnValueSurvivesTheFinally()
        => Assert.That(Run(
            "Function Compute() As Integer\n" +
            "Try\nReturn 42\nFinally\nConsole.WriteLine(\"cleanup\")\nEnd Try\n" +
            "End Function\n" +
            "Sub Main()\nConsole.WriteLine(Compute())\nEnd Sub"),
            Is.EqualTo("cleanup\n42"));

    // ---------------------------------------------------------------- nesting

    [Test]
    public void Try_InsideALoop_RunsEachIteration()
        => Assert.That(InMain(
            "For i As Integer = 1 To 3\n" +
            "Try\nConsole.WriteLine(i)\nCatch ex As Exception\nEnd Try\n" +
            "Next"),
            Is.EqualTo("1\n2\n3"));

    [Test]
    public void Catch_RecoversAndTheLoopContinues()
        => Assert.That(InMain(
            "For i As Integer = 1 To 3\n" +
            "Try\n" +
            "If i = 2 Then\nThrow New Exception(\"x\")\nEnd If\n" +
            "Console.WriteLine(i)\n" +
            "Catch ex As Exception\nConsole.WriteLine(\"caught\")\nEnd Try\n" +
            "Next"),
            Is.EqualTo("1\ncaught\n3"));
}
