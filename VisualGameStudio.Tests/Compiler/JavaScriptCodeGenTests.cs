using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Plan task 2: the first end-to-end emit. Pins the shape of generated JS; behaviour is
/// proved separately by <c>JavaScriptExecutionTests</c> (task 3), which actually runs it.
/// </summary>
[TestFixture]
public class JavaScriptCodeGenTests
{
    [Test]
    public void Emits_HelloWorld()
    {
        var js = JsTestSupport.Compile("Sub Main()\nConsole.WriteLine(\"Hello\")\nEnd Sub");

        Assert.That(js, Does.Contain("function Main()"));
        Assert.That(js, Does.Contain("console.log(\"Hello\")"));
        Assert.That(js, Does.Contain("Main();"), "entry point must be invoked");
    }

    [Test]
    public void Emits_UserFunctionCall_Unqualified()
    {
        var js = JsTestSupport.Compile(
            "Sub Greet()\nConsole.WriteLine(\"hi\")\nEnd Sub\nSub Main()\nGreet()\nEnd Sub");

        Assert.That(js, Does.Contain("function Greet()"));
        Assert.That(js, Does.Contain("Greet();"));
    }

    /// <summary>
    /// Doubles must render invariant. A comma decimal separator from the host locale
    /// would turn `3.14` into `3,14`, which is a syntax error inside a JS call — or,
    /// worse, a second argument.
    /// </summary>
    [Test]
    public void Emits_DoubleConstant_InvariantCulture()
    {
        var js = JsTestSupport.Compile("Sub Main()\nConsole.WriteLine(3.14)\nEnd Sub");

        Assert.That(js, Does.Contain("3.14"));
        Assert.That(js, Does.Not.Contain("3,14"));
    }

    [Test]
    public void Escapes_StringLiterals()
    {
        var js = JsTestSupport.Compile("Sub Main()\nConsole.WriteLine(\"a\"\"b\")\nEnd Sub");

        // The BasicLang "" escape is one literal quote; it must reach JS escaped.
        Assert.That(js, Does.Contain("\\\""));
    }

    /// <summary>
    /// The whole point of the backend's honesty rule: an IR node with no lowering must
    /// throw, never emit nothing. A silent no-op is indistinguishable from correct
    /// codegen at build time and shows up as wrong behaviour at runtime — which is exactly
    /// how LLVM and MSIL came to drop collection indexed writes.
    ///
    /// <para><b>This is a MOVING canary and is meant to be re-pointed.</b> It must always
    /// name a construct just beyond the implemented frontier, so as Phase 2 lands features
    /// this test goes green-by-accident and has to be aimed further out. It has already moved
    /// five times: `x = x + 1` (task 13), Try/Catch (task 19), Async (task 21), Iterator
    /// (task 22), a second Catch clause (now an instanceof ladder). It now names a COMPUTED
    /// base-constructor argument — `MyBase.New(n &amp; "!")` — which JS forbids before
    /// <c>super()</c> because the argument's instructions would have to run first, an ordering
    /// the IR does not mark; only constants and plain parameters lower. (Reachable at all only
    /// since <c>MyBase.New</c> parses.) Re-point it rather than deleting it — the principle it
    /// guards outlives any one node.</para>
    ///
    /// <para><b>What it must NOT name: a construct the capability checker REFUSES.</b> Those
    /// throw ForeignFeatureException by design and are permanent, so they would pin the canary
    /// where it can never move from — and the assertion below is specifically that unbuilt
    /// lowerings surface as NotSupportedException. Two candidates were rejected for exactly
    /// this reason while re-pointing: an Event defaults to the type `EventHandler` and draws
    /// BL7007, and `New List(Of T)(capacity)` is a deliberate refusal.</para>
    /// </summary>
    [Test]
    public void UnimplementedNode_Throws_RatherThanEmittingNothing()
    {
        var ex = Assert.Catch(() => JsTestSupport.Compile(
            "Class Animal\nPublic Name As String\n" +
            "Public Sub New(n As String)\nName = n\nEnd Sub\nEnd Class\n" +
            "Class Dog\nInherits Animal\n" +
            "Public Sub New(n As String)\nMyBase.New(n & \"!\")\nEnd Sub\nEnd Class\n" +
            "Sub Main()\nDim d As New Dog(\"rex\")\nConsole.WriteLine(d.Name)\nEnd Sub"));

        Assert.That(ex, Is.InstanceOf<System.NotSupportedException>(),
            "unimplemented lowering must surface as NotSupportedException, not silence");
    }
}
