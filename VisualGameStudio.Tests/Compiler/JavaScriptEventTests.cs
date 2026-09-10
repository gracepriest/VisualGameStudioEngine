using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// <c>Event</c> / <c>RaiseEvent</c> / <c>AddHandler</c> / <c>RemoveHandler</c>.
///
/// <para><b>MEASURED before the fix.</b> Events had never worked end to end on ANY backend:</para>
/// <code>
///   Public Event Clicked(count As Integer)        parse error on every backend ("Unexpected token '('")
///   Public Event Clicked As Action(Of Integer)
///     C#          public event Action Clicked;      generic arguments DROPPED (IREvent carried a name)
///                 raise_Clicked(clicks);             CS0103 — nothing defines raise_X
///     JavaScript  refused                           NotYet("Events")
///     C++         raise_Clicked() takes no arguments
/// </code>
///
/// <para>IRBuilder lowers <c>RaiseEvent X(args)</c> to a call named <c>raise_X</c> and
/// <c>AddHandler</c>/<c>RemoveHandler</c> to <c>Delegate.Combine</c>/<c>Delegate.Remove</c>
/// calls; the C# backend already rendered the latter two as <c>+=</c>/<c>-=</c>. What was
/// missing: the event's real TYPE on the IR (so <c>Action(Of Integer)</c> survives), the raise
/// itself (<c>X?.Invoke(args)</c> on C#), the canonical parameter-list declaration syntax, and
/// any JavaScript lowering at all. On JavaScript an event is a <c>Set</c> of handlers on the
/// instance: subscribe is <c>add</c>, unsubscribe is <c>delete</c>, raise iterates.</para>
///
/// <para>⚠ <c>RemoveHandler</c> with a lambda or a bound instance method removes nothing on
/// JavaScript — each <c>AddressOf obj.Method</c> would be a fresh function object — exactly as
/// an anonymous handler cannot be removed in .NET either. A free function is the same object
/// every time, so the ordinary <c>AddressOf FreeSub</c> case works.</para>
/// </summary>
[TestFixture]
[Category("Integration")]   // spawns node; the C# legs build with dotnet
public class JavaScriptEventTests
{
    private static string Run(string source) => JavaScriptExecutionTests.RunJs(source);

    /// <summary>The canonical VB shape: a parameter list on the declaration.</summary>
    private const string ParameterListProgram =
        "Class Button\nPublic Event Clicked(count As Integer)\nPrivate clicks As Integer\n" +
        "Public Sub Press()\nclicks = clicks + 1\nRaiseEvent Clicked(clicks)\nEnd Sub\nEnd Class\n" +
        "Sub OnClicked(count As Integer)\nConsole.WriteLine(\"clicked \" & count)\nEnd Sub\n" +
        "Sub Main()\nDim b As New Button()\n" +
        "AddHandler b.Clicked, AddressOf OnClicked\nb.Press()\nb.Press()\n" +
        "RemoveHandler b.Clicked, AddressOf OnClicked\nb.Press()\n" +
        "Console.WriteLine(\"done\")\nEnd Sub";

    /// <summary>The delegate-typed shape.</summary>
    private const string AsDelegateProgram =
        "Class Button\nPublic Event Clicked As Action(Of Integer)\nPrivate clicks As Integer\n" +
        "Public Sub Press()\nclicks = clicks + 1\nRaiseEvent Clicked(clicks)\nEnd Sub\nEnd Class\n" +
        "Sub OnClicked(count As Integer)\nConsole.WriteLine(\"clicked \" & count)\nEnd Sub\n" +
        "Sub Main()\nDim b As New Button()\n" +
        "AddHandler b.Clicked, AddressOf OnClicked\nb.Press()\nb.Press()\n" +
        "RemoveHandler b.Clicked, AddressOf OnClicked\nb.Press()\n" +
        "Console.WriteLine(\"done\")\nEnd Sub";

    private const string Expected = "clicked 1\nclicked 2\ndone";

    // ---------------------------------------------------------------- subscribe / raise / unsubscribe

    [Test]
    public void Event_ParameterListSyntax_SubscribeRaiseUnsubscribe_JavaScript()
        => Assert.That(Run(ParameterListProgram), Is.EqualTo(Expected));

    [Test]
    public void Event_AsDelegateSyntax_SubscribeRaiseUnsubscribe_JavaScript()
        => Assert.That(Run(AsDelegateProgram), Is.EqualTo(Expected));

    /// <summary>The C# mirror — CS0103 before. The raise and the event's type are IR/C# fixes, so this is the proof.</summary>
    [Test]
    public void Event_ParameterListSyntax_SubscribeRaiseUnsubscribe_CSharp()
        => Assert.That(CliTestHarness.CompileRunCSharp(ParameterListProgram).Replace("\r\n", "\n").Trim(), Is.EqualTo(Expected));

    [Test]
    public void Event_AsDelegateSyntax_SubscribeRaiseUnsubscribe_CSharp()
        => Assert.That(CliTestHarness.CompileRunCSharp(AsDelegateProgram).Replace("\r\n", "\n").Trim(), Is.EqualTo(Expected));

    // ---------------------------------------------------------------- semantics

    [Test]
    public void TwoHandlers_BothFire_InSubscriptionOrder()
        => Assert.That(Run(
            "Class Button\nPublic Event Clicked(count As Integer)\n" +
            "Public Sub Press()\nRaiseEvent Clicked(7)\nEnd Sub\nEnd Class\n" +
            "Sub First(c As Integer)\nConsole.WriteLine(\"first \" & c)\nEnd Sub\n" +
            "Sub Second(c As Integer)\nConsole.WriteLine(\"second \" & c)\nEnd Sub\n" +
            "Sub Main()\nDim b As New Button()\n" +
            "AddHandler b.Clicked, AddressOf First\nAddHandler b.Clicked, AddressOf Second\nb.Press()\nEnd Sub"),
            Is.EqualTo("first 7\nsecond 7"));

    [Test]
    public void ALambdaHandler_Fires()
        => Assert.That(Run(
            "Class Button\nPublic Event Clicked(count As Integer)\n" +
            "Public Sub Press()\nRaiseEvent Clicked(3)\nEnd Sub\nEnd Class\n" +
            "Sub Main()\nDim b As New Button()\n" +
            "AddHandler b.Clicked, Sub(c As Integer) Console.WriteLine(\"lambda \" & c)\nb.Press()\nEnd Sub"),
            Is.EqualTo("lambda 3"));

    /// <summary>Raising with nobody subscribed is a no-op, not a crash.</summary>
    [Test]
    public void RaiseWithNoSubscribers_IsANoOp()
        => Assert.That(Run(
            "Class Button\nPublic Event Clicked(count As Integer)\n" +
            "Public Sub Press()\nRaiseEvent Clicked(1)\nEnd Sub\nEnd Class\n" +
            "Sub Main()\nDim b As New Button()\nb.Press()\nConsole.WriteLine(\"done\")\nEnd Sub"),
            Is.EqualTo("done"));

    [Test]
    public void AnEventWithNoParameters()
        => Assert.That(Run(
            "Class Timer\nPublic Event Tick()\n" +
            "Public Sub Fire()\nRaiseEvent Tick()\nEnd Sub\nEnd Class\n" +
            "Sub OnTick()\nConsole.WriteLine(\"tick\")\nEnd Sub\n" +
            "Sub Main()\nDim t As New Timer()\nAddHandler t.Tick, AddressOf OnTick\nt.Fire()\nt.Fire()\nEnd Sub"),
            Is.EqualTo("tick\ntick"));

    [Test]
    public void AnEventWithTwoParameters()
        => Assert.That(Run(
            "Class Sensor\nPublic Event Reading(name As String, value As Integer)\n" +
            "Public Sub Report()\nRaiseEvent Reading(\"temp\", 21)\nEnd Sub\nEnd Class\n" +
            "Sub OnReading(n As String, v As Integer)\nConsole.WriteLine(n & \"=\" & v)\nEnd Sub\n" +
            "Sub Main()\nDim s As New Sensor()\nAddHandler s.Reading, AddressOf OnReading\ns.Report()\nEnd Sub"),
            Is.EqualTo("temp=21"));

    /// <summary>Each INSTANCE has its own subscribers — the handler set is per object, not per class.</summary>
    [Test]
    public void Subscriptions_ArePerInstance()
        => Assert.That(Run(
            "Class Button\nPublic Event Clicked(count As Integer)\n" +
            "Public Sub Press()\nRaiseEvent Clicked(1)\nEnd Sub\nEnd Class\n" +
            "Sub OnClicked(c As Integer)\nConsole.WriteLine(\"clicked\")\nEnd Sub\n" +
            "Sub Main()\nDim a As New Button()\nDim b As New Button()\n" +
            "AddHandler a.Clicked, AddressOf OnClicked\na.Press()\nb.Press()\nConsole.WriteLine(\"done\")\nEnd Sub"),
            Is.EqualTo("clicked\ndone"));

    // ---------------------------------------------------------------- handlers that are instance methods
    //
    // ⛔ Found by the branch review: `AddressOf OnClicked` inside the class rendered the bare
    // name (a ReferenceError — member bodies are not top-level functions), and `AddressOf
    // Me.OnClicked` rendered `this.OnClicked` UNBOUND, so `hits` inside the handler was a
    // TypeError once the event invoked it as `h(args)`.

    private const string SelfSubscribing =
        "Class Button\nPublic Event Clicked(count As Integer)\nPrivate hits As Integer\n" +
        "Public Sub Watch()\nAddHandler Clicked, AddressOf OnClicked\nEnd Sub\n" +
        "Private Sub OnClicked(c As Integer)\nhits = hits + c\nConsole.WriteLine(\"hits \" & hits)\nEnd Sub\n" +
        "Public Sub Press()\nRaiseEvent Clicked(1)\nEnd Sub\nEnd Class\n" +
        "Sub Main()\nDim b As New Button()\nb.Watch()\nb.Press()\nb.Press()\nEnd Sub";

    [Test]
    public void AddressOfAnInstanceMethod_InsideItsOwnClass_KeepsThis()
        => Assert.That(Run(SelfSubscribing), Is.EqualTo("hits 1\nhits 2"));

    [Test]
    public void AddressOfMeDotMethod_KeepsThis()
        => Assert.That(Run(SelfSubscribing.Replace("AddressOf OnClicked", "AddressOf Me.OnClicked")),
            Is.EqualTo("hits 1\nhits 2"));

    /// <summary>The C# mirror of the same program — `this.OnClicked` is a bound method group there.</summary>
    [Test]
    public void AddressOfAnInstanceMethod_InsideItsOwnClass_CSharp()
        => Assert.That(CliTestHarness.CompileRunCSharp(SelfSubscribing).Replace("\r\n", "\n").Trim(),
            Is.EqualTo("hits 1\nhits 2"));

    // ---------------------------------------------------------------- the SHIPPING IR

    [Test]
    public void Optimized_Event_SubscribeRaiseUnsubscribe()
        => Assert.That(JavaScriptOptimizedExecutionTests.RunOptimized(ParameterListProgram), Is.EqualTo(Expected));
}

/// <summary>Codegen-side contract, no Node.</summary>
[TestFixture]
public class EventCodeGenTests
{
    private const string Program =
        "Class Button\nPublic Event Clicked(count As Integer)\n" +
        "Public Sub Press()\nRaiseEvent Clicked(1)\nEnd Sub\nEnd Class\n" +
        "Sub OnClicked(c As Integer)\nEnd Sub\n" +
        "Sub Main()\nDim b As New Button()\nAddHandler b.Clicked, AddressOf OnClicked\nb.Press()\nEnd Sub";

    [Test]
    public void JavaScript_AnEventIsAHandlerSetOnTheInstance()
    {
        var js = JsTestSupport.Compile(Program);

        Assert.That(js, Does.Contain("Clicked = new Set();"));
        // The subscribe: `.add(…)` on the event's set, with the handler REFERENCE (an SSA temp
        // bound to the function, never a call to it).
        Assert.That(js, Does.Contain(".add("));
        Assert.That(js, Does.Contain("= OnClicked;"));
        Assert.That(js, Does.Contain("for (const h of this.Clicked) h(1);"));
        Assert.That(js, Does.Not.Contain("raise_"), "the IR's raise_X convention must not leak as a call to nothing");
        Assert.That(js, Does.Not.Contain("Delegate.Combine"));
    }

    [Test]
    public void CSharp_TheEventKeepsItsDelegateType_AndTheRaiseIsAnInvoke()
    {
        var cs = new BasicLang.Compiler.CodeGen.CSharp.CSharpCodeGenerator().Generate(JsTestSupport.BuildModule(Program));

        Assert.That(cs, Does.Contain("event Action<int> Clicked;"), "the generic arguments were dropped before");
        Assert.That(cs, Does.Contain("Clicked?.Invoke(1);"));
        Assert.That(cs, Does.Not.Contain("raise_"));
    }

    [Test]
    public void CSharp_AsDelegateSyntax_KeepsItsType()
        => Assert.That(new BasicLang.Compiler.CodeGen.CSharp.CSharpCodeGenerator().Generate(JsTestSupport.BuildModule(
                "Class Button\nPublic Event Clicked As Action(Of Integer)\n" +
                "Public Sub Press()\nRaiseEvent Clicked(1)\nEnd Sub\nEnd Class\nSub Main()\nEnd Sub")),
            Does.Contain("event Action<int> Clicked;"));
}
