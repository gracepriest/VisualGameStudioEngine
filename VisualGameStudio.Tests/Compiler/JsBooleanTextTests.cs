using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// A Boolean becomes .NET text — "True" / "False" — on the JavaScript backend, and
/// <c>x.ToString()</c> on a primitive no longer calls a method JavaScript does not have.
/// MEASURED on master e69ec64, both pipelines, from builds that reported success:
/// <list type="bullet">
/// <item><c>"b" &amp; True</c> printed "btrue", <c>s &amp;= f</c> appended "false", <c>CStr(b)</c> and
/// <c>Console.WriteLine(b)</c> printed "true" — JavaScript's own spelling.</item>
/// <item><c>b.ToString()</c>, <c>n.ToString()</c> and <c>s.ToString()</c> emitted <c>x.ToString()</c>
/// and died in Node: "ToString is not a function".</item>
/// </list>
/// C# printed "True" throughout; it is the reference.
/// </summary>
public class JsBooleanTextTests
{
    internal const string Program = @"
Function T() As Boolean
    Return True
End Function
Function N() As Integer
    Return 5
End Function
Sub Main()
    Dim f As Boolean = False
    Dim s As String = ""a""
    s = s & T()
    Console.WriteLine(s)
    Console.WriteLine(""b"" & True)
    Console.WriteLine(f & ""c"")
    s &= f
    Console.WriteLine(s)
    Console.WriteLine(CStr(T()))
    Console.WriteLine(T().ToString())
    Console.WriteLine(T())
    Console.Write(f)
    Console.WriteLine()
    Console.WriteLine(N().ToString())
    Dim q As String = ""q""
    Console.WriteLine(q.ToString())
    Dim d As Double = 2.5
    Console.WriteLine(d.ToString() & ""|"" & N())
End Sub
";

    internal const string Expected =
        "aTrue\nbTrue\nFalsec\naTrueFalse\nTrue\nTrue\nTrue\nFalse\n5\nq\n2.5|5";

    [Test]
    public void BooleanText_IsSpelledOut_AndPrimitiveToString_IsNotAMethodCall()
    {
        var js = JsTestSupport.CompileAggressive(Program);
        Assert.Multiple(() =>
        {
            Assert.That(js, Does.Contain("? \"True\" : \"False\")"), js);
            Assert.That(js, Does.Not.Contain(".ToString()"), js);
        });
    }

    /// <summary>Type-directed: a String or number operand of <c>&amp;</c> is emitted as before.</summary>
    [Test]
    public void NonBooleanConcat_IsUnchanged()
    {
        var js = JsTestSupport.Compile(
            "Function N() As Integer\n    Return 5\nEnd Function\n" +
            "Sub Main()\n    Dim s As String = \"a\"\n    Console.WriteLine(s & N())\nEnd Sub\n");
        Assert.Multiple(() =>
        {
            Assert.That(js, Does.Not.Contain("\"True\""), js);
            Assert.That(js, Does.Match(@"\(s \+ \w+\)"), js);
        });
    }
}

/// <summary>The program under Node (both pipelines) and as C#, the reference.</summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class JsBooleanTextRunTests
{
    [Test]
    public void JavaScript_Runs() =>
        Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunNodeScript(JsTestSupport.Compile(JsBooleanTextTests.Program))),
            Is.EqualTo(JsBooleanTextTests.Expected));

    [Test]
    public void JavaScript_Optimized_Runs() =>
        Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunNodeScript(JsTestSupport.CompileAggressive(JsBooleanTextTests.Program))),
            Is.EqualTo(JsBooleanTextTests.Expected));

    [Test]
    public void CSharp_Runs() =>
        Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(JsBooleanTextTests.Program)),
            Is.EqualTo(JsBooleanTextTests.Expected));
}
