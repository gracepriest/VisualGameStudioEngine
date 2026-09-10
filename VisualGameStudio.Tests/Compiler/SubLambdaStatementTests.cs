using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// A single-line <c>Sub</c> lambda's body is a STATEMENT, not an expression.
///
/// <para>⛔ <b>MEASURED, all backends:</b> <c>Sub(x As Integer) total = total + x</c> lowered to
/// <c>const t1 = (total === t0);</c> — a COMPARISON. <c>ParseLambdaExpression</c> parsed every
/// single-line body with <c>ParseExpression()</c>, and in expression position <c>=</c> is
/// equality. The lambda ran, computed a boolean, discarded it, and the accumulator never
/// changed — from a build that reported success on every backend.</para>
///
/// <para>The same parser decided single-line vs multi-line by peeking for a statement KEYWORD
/// (Dim/If/For/…) after skipping newlines, so a multi-line <c>Sub</c> lambda whose first
/// statement was a call or an assignment was also mis-parsed as single-line, leaving its
/// <c>End Sub</c> dangling. The fix decides by whether a NEWLINE follows the parameter list —
/// which is what the language says — and parses a single-line <c>Sub</c> body as one statement.</para>
/// </summary>
[TestFixture]
public class SubLambdaStatementTests
{
    /// <summary>Codegen-only: the body must be an assignment, not a comparison.</summary>
    [Test]
    public void SingleLineSubLambda_AssignmentBody_IsAnAssignment()
    {
        var js = JsTestSupport.Compile(
            "Sub Main()\nDim total As Integer\ntotal = 0\nDim xs As New List(Of Integer)()\nxs.Add(1)\n" +
            "xs.ForEach(Sub(x As Integer) total = total + x)\nConsole.WriteLine(total)\nEnd Sub");

        Assert.That(js, Does.Not.Contain("total ==="), "the body was parsed as an equality expression");
        Assert.That(js, Does.Contain("total = "));
    }

    [Test]
    public void SingleLineSubLambda_CallBody_StillParses()
        => Assert.That(JsTestSupport.Compile(
                "Sub Main()\nDim xs As New List(Of Integer)()\nxs.Add(1)\n" +
                "xs.ForEach(Sub(x As Integer) Console.WriteLine(x))\nEnd Sub"),
            Does.Contain("console.log(x)"));

    /// <summary>A multi-line Sub lambda whose FIRST statement is a call or an assignment.</summary>
    [Test]
    public void MultiLineSubLambda_StartingWithACall_Parses()
        => Assert.That(JsTestSupport.Compile(
                "Sub Main()\nDim xs As New List(Of Integer)()\nxs.Add(1)\n" +
                "xs.ForEach(Sub(x As Integer)\nConsole.WriteLine(x)\nConsole.WriteLine(\"next\")\nEnd Sub)\nEnd Sub"),
            Does.Contain("console.log(\"next\")"));

    [Test]
    public void MultiLineSubLambda_StartingWithAnAssignment_Parses()
    {
        var js = JsTestSupport.Compile(
            "Sub Main()\nDim total As Integer\ntotal = 0\nDim xs As New List(Of Integer)()\nxs.Add(1)\n" +
            "xs.ForEach(Sub(x As Integer)\ntotal = total + x\nConsole.WriteLine(total)\nEnd Sub)\nEnd Sub");

        Assert.That(js, Does.Not.Contain("total ==="));
        Assert.That(js, Does.Contain("console.log(total)"));
    }

    /// <summary>A single-line FUNCTION lambda is still an expression — `=` there is equality and must stay so.</summary>
    [Test]
    public void SingleLineFunctionLambda_IsStillAnExpression()
        => Assert.That(JsTestSupport.Compile(
                "Sub Main()\nDim xs As New List(Of Integer)()\nxs.Add(1)\n" +
                "Dim n As Integer = xs.Where(Function(x As Integer) x = 1).Count()\nConsole.WriteLine(n)\nEnd Sub"),
            Does.Contain("x === 1"));
}

/// <summary>And it runs — the accumulator actually accumulates.</summary>
[TestFixture]
[Category("Integration")]   // spawns node
public class SubLambdaStatementExecutionTests
{
    [Test]
    public void SingleLineSubLambda_Accumulates()
        => Assert.That(JavaScriptExecutionTests.RunJs(
            "Sub Main()\nDim total As Integer\ntotal = 0\nDim xs As New List(Of Integer)()\n" +
            "xs.Add(1)\nxs.Add(2)\nxs.Add(3)\n" +
            "xs.ForEach(Sub(x As Integer) total = total + x)\nConsole.WriteLine(total)\nEnd Sub"),
            Is.EqualTo("6"));

    [Test]
    public void MultiLineSubLambda_RunsEveryStatement()
        => Assert.That(JavaScriptExecutionTests.RunJs(
            "Sub Main()\nDim xs As New List(Of Integer)()\nxs.Add(1)\nxs.Add(2)\n" +
            "xs.ForEach(Sub(x As Integer)\nConsole.WriteLine(x)\nConsole.WriteLine(\"-\")\nEnd Sub)\nEnd Sub"),
            Is.EqualTo("1\n-\n2\n-"));

    /// <summary>Cross-backend: the C# leg, same program, same answer.</summary>
    [Test]
    public void SingleLineSubLambda_Accumulates_CSharp()
        => Assert.That(CliTestHarness.CompileRunCSharp(
            "Sub Main()\nDim total As Integer\ntotal = 0\nDim xs As New List(Of Integer)()\n" +
            "xs.Add(1)\nxs.Add(2)\nxs.Add(3)\n" +
            "xs.ForEach(Sub(x As Integer) total = total + x)\nConsole.WriteLine(total)\nEnd Sub").Trim(),
            Is.EqualTo("6"));
}
