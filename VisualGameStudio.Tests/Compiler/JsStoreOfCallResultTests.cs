using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// A call's result stored into a variable is an ASSIGNMENT on the JavaScript backend even when
/// nothing later in the same body reads it. The backend bound a call's result only when the
/// current body read it afterwards, and a store's reader can be somewhere else entirely.
/// MEASURED on master ad43edd, with and without the optimizer, from builds that reported success:
/// <list type="bullet">
/// <item><c>Dim n1 = MinusOne()</c> read only inside a lambda (a separate IR function, rendered
/// inline) emitted a bare <c>MinusOne();</c>, so the lambda saw 0 — printed 0 instead of -5.</item>
/// <item><c>g = MinusOne()</c> in a Sub, for a module-level <c>g</c> read by <c>Main</c>, left
/// <c>g</c> at 0.</item>
/// </list>
/// C# was correct throughout; it is the reference here.
/// </summary>
public class JsStoreOfCallResultTests
{
    internal const string Program = @"
Dim g As Integer = 0

Class Counter
    Public Value As Integer
    Public Sub Load()
        Value = MinusOne()
    End Sub
End Class

Function MinusOne() As Integer
    Return -1
End Function

Sub Show(f As Func(Of Integer))
    Console.WriteLine(f())
End Sub

Sub SetG()
    g = MinusOne()
End Sub

Sub Main()
    Dim n1 As Integer = MinusOne()
    Show(Function() n1 * 5)
    SetG()
    Console.WriteLine(g)
    Dim c As New Counter()
    c.Load()
    Console.WriteLine(c.Value)
    Dim acc As Integer = 0
    Dim setAcc = Sub(x As Integer)
                     acc = MinusOne() * x
                 End Sub
    setAcc(4)
    Console.WriteLine(acc)
End Sub
";

    internal const string Expected = "-5\n-1\n-1\n-4";

    [Test]
    public void LocalReadOnlyInALambda_IsAssigned()
    {
        var js = JsTestSupport.Compile(Program);
        Assert.Multiple(() =>
        {
            Assert.That(js, Does.Contain("n1 = MinusOne();"), js);
            Assert.That(js, Does.Contain("g = MinusOne();"), js);
            Assert.That(js, Does.Not.Match(@"\n\s*MinusOne\(\);"), "a bare call drops the store:\n" + js);
        });
    }

    [Test]
    public void ThroughTheOptimizer_TheStoresSurvive()
    {
        var js = JsTestSupport.CompileAggressive(Program);
        Assert.Multiple(() =>
        {
            Assert.That(js, Does.Contain("n1 = MinusOne();"), js);
            Assert.That(js, Does.Contain("g = MinusOne();"), js);
        });
    }

    /// <summary>A call whose result nothing reads and that stores nothing stays a bare statement.</summary>
    [Test]
    public void AnUnusedTempResult_StaysABareCall()
    {
        var js = JsTestSupport.Compile(
            "Function F() As Integer\n    Return 1\nEnd Function\nSub Main()\n    F()\nEnd Sub\n");
        Assert.That(js, Does.Match(@"\n\s*F\(\);"), js);
    }
}

/// <summary>The program run under Node (both pipelines) and as C#, the reference.</summary>
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class JsStoreOfCallResultRunTests
{
    [Test]
    public void JavaScript_Runs() =>
        Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunNodeScript(JsTestSupport.Compile(JsStoreOfCallResultTests.Program))),
            Is.EqualTo(JsStoreOfCallResultTests.Expected));

    [Test]
    public void JavaScript_Optimized_Runs() =>
        Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunNodeScript(JsTestSupport.CompileAggressive(JsStoreOfCallResultTests.Program))),
            Is.EqualTo(JsStoreOfCallResultTests.Expected));

    [Test]
    public void CSharp_Runs() =>
        Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(JsStoreOfCallResultTests.Program)),
            Is.EqualTo(JsStoreOfCallResultTests.Expected));
}
