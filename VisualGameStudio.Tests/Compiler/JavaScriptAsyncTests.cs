using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Plan task 21 — Async / Await.
///
/// <para>This is one of the places the spec says JavaScript gets for free what the C++
/// backend had to fake: there, async is a SYNCHRONOUS <c>Task&lt;T&gt;</c> emulation with no
/// scheduler. Here <c>Async</c> maps onto native <c>async</c> and <c>Await</c> onto
/// <c>await</c>, so the semantics are the runtime's rather than the backend's.</para>
///
/// <para><b>What that makes worth testing.</b> Not that the keywords appear — that ORDERING
/// is real. An implementation that emits <c>async</c> but drops the <c>await</c> still
/// compiles and still prints something; it just prints a Promise, or prints in the wrong
/// order. Every test below pins observable sequencing.</para>
/// </summary>
[TestFixture]
[Category("Integration")]   // spawns node
public class JavaScriptAsyncTests
{
    private static string Run(string source) => JavaScriptExecutionTests.RunJs(source);

    [Test]
    public void Await_YieldsTheValue_NotAPromise()
        => Assert.That(Run(
            "Async Function GetValue() As Task(Of Integer)\nReturn 42\nEnd Function\n" +
            "Async Function Run() As Task(Of Integer)\n" +
            "Dim v As Integer\nv = Await GetValue()\nConsole.WriteLine(v)\nReturn v\n" +
            "End Function\n" +
            "Sub Main()\nRun()\nEnd Sub"),
            Is.EqualTo("42"),
            "a dropped await prints [object Promise] rather than the value");

    /// <summary>
    /// The plan's own criterion: awaited values must arrive IN ORDER. A lowering that emits
    /// async but loses the await interleaves them.
    /// </summary>
    [Test]
    public void AwaitedValues_ArriveInOrder()
        => Assert.That(Run(
            "Async Function One() As Task(Of Integer)\nConsole.WriteLine(\"one\")\nReturn 1\nEnd Function\n" +
            "Async Function Two() As Task(Of Integer)\nConsole.WriteLine(\"two\")\nReturn 2\nEnd Function\n" +
            "Async Function Run() As Task(Of Integer)\n" +
            "Dim a As Integer\nDim b As Integer\n" +
            "a = Await One()\n" +
            "b = Await Two()\n" +
            "Console.WriteLine(a + b)\n" +
            "Return 0\n" +
            "End Function\n" +
            "Sub Main()\nRun()\nEnd Sub"),
            Is.EqualTo("one\ntwo\n3"));

    /// <summary>Work after an await must run AFTER it, not before.</summary>
    [Test]
    public void CodeAfterAwait_RunsAfterIt()
        => Assert.That(Run(
            "Async Function Step1() As Task(Of Integer)\nConsole.WriteLine(\"step1\")\nReturn 1\nEnd Function\n" +
            "Async Function Run() As Task(Of Integer)\n" +
            "Console.WriteLine(\"before\")\n" +
            "Dim v As Integer\nv = Await Step1()\n" +
            "Console.WriteLine(\"after\")\n" +
            "Return v\n" +
            "End Function\n" +
            "Sub Main()\nRun()\nEnd Sub"),
            Is.EqualTo("before\nstep1\nafter"));

    [Test]
    public void Await_InsideALoop_SequencesEachIteration()
        => Assert.That(Run(
            "Async Function Echo(n As Integer) As Task(Of Integer)\nReturn n\nEnd Function\n" +
            "Async Function Run() As Task(Of Integer)\n" +
            "For i As Integer = 1 To 3\n" +
            "Dim v As Integer\nv = Await Echo(i)\nConsole.WriteLine(v)\n" +
            "Next\nReturn 0\n" +
            "End Function\n" +
            "Sub Main()\nRun()\nEnd Sub"),
            Is.EqualTo("1\n2\n3"));

    /// <summary>An Async Sub has no return value but must still be awaitable machinery.</summary>
    [Test]
    public void AsyncSub_Runs()
        => Assert.That(Run(
            "Async Sub Report()\nConsole.WriteLine(\"reported\")\nEnd Sub\n" +
            "Sub Main()\nReport()\nEnd Sub"),
            Is.EqualTo("reported"));

    /// <summary>Await must compose with Try/Catch — a rejected promise is a thrown error.</summary>
    [Test]
    public void Await_InsideTryCatch()
        => Assert.That(Run(
            "Async Function Boom() As Task(Of Integer)\nThrow New Exception(\"x\")\nReturn 0\nEnd Function\n" +
            "Async Function Run() As Task(Of Integer)\n" +
            "Try\n" +
            "Dim v As Integer\nv = Await Boom()\n" +
            "Catch ex As Exception\nConsole.WriteLine(\"caught\")\n" +
            "End Try\nReturn 0\n" +
            "End Function\n" +
            "Sub Main()\nRun()\nEnd Sub"),
            Is.EqualTo("caught"));
}
