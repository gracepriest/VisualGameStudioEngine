using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Plan task 17 — classes.
///
/// <para><b>The highest-risk item, and it is not obvious.</b> An UNQUALIFIED field reference
/// inside a method — <c>count = count + 1</c> — does NOT produce an <c>IRFieldAccess</c>. It
/// produces a bare <c>IRVariable</c>: IRBuilder's identifier resolution has no arm that
/// recognises a class field, so the result is neither a parameter nor a local. C# and C++
/// both survive that because they have implicit <c>this.</c>/<c>this-&gt;</c> resolution.
/// JavaScript does not — under an ES module (always strict) it is a <c>ReferenceError</c> at
/// runtime from a build that reported success. Members must therefore be rewritten to
/// <c>this.X</c>, and NOT when the name is shadowed by a parameter or local.</para>
/// </summary>
[TestFixture]
[Category("Integration")]   // spawns node
public class JavaScriptClassTests
{
    private static string Run(string source) => JavaScriptExecutionTests.RunJs(source);

    // ---------------------------------------------------------------- basics

    [Test]
    public void Class_MethodCallOnAnInstance()
        => Assert.That(Run(
            "Class Greeter\n" +
            "Public Sub Hello()\nConsole.WriteLine(\"hi\")\nEnd Sub\n" +
            "End Class\n" +
            "Sub Main()\nDim g As New Greeter()\ng.Hello()\nEnd Sub"),
            Is.EqualTo("hi"));

    [Test]
    public void Class_MethodWithParametersAndReturn()
        => Assert.That(Run(
            "Class Math2\n" +
            "Public Function Add(a As Integer, b As Integer) As Integer\nReturn a + b\nEnd Function\n" +
            "End Class\n" +
            "Sub Main()\nDim m As New Math2()\nConsole.WriteLine(m.Add(2, 3))\nEnd Sub"),
            Is.EqualTo("5"));

    // ---------------------------------------------------------------- fields

    [Test]
    public void Class_FieldWrittenAndReadFromOutside()
        => Assert.That(Run(
            "Class Box\nPublic Value As Integer\nEnd Class\n" +
            "Sub Main()\nDim b As New Box()\nb.Value = 7\nConsole.WriteLine(b.Value)\nEnd Sub"),
            Is.EqualTo("7"));

    /// <summary>
    /// THE trap. An unqualified member reference inside a method arrives as a bare
    /// IRVariable; emitting it verbatim gives `count = count + 1` at method scope, which is a
    /// ReferenceError in strict mode.
    /// </summary>
    [Test]
    public void Class_UnqualifiedFieldReferenceInsideAMethod()
        => Assert.That(Run(
            "Class Counter\n" +
            "Public Count As Integer\n" +
            "Public Sub Inc()\nCount = Count + 1\nEnd Sub\n" +
            "End Class\n" +
            "Sub Main()\nDim c As New Counter()\nc.Count = 0\nc.Inc()\nc.Inc()\nConsole.WriteLine(c.Count)\nEnd Sub"),
            Is.EqualTo("2"));

    /// <summary>
    /// A PARAMETER of the same name must shadow the field — rewriting it to `this.` would
    /// silently read the wrong value.
    /// </summary>
    [Test]
    public void Class_ParameterShadowsAField()
        => Assert.That(Run(
            "Class Box\n" +
            "Public Value As Integer\n" +
            "Public Function Echo(Value As Integer) As Integer\nReturn Value\nEnd Function\n" +
            "End Class\n" +
            "Sub Main()\nDim b As New Box()\nb.Value = 1\nConsole.WriteLine(b.Echo(99))\nEnd Sub"),
            Is.EqualTo("99"));

    /// <summary>A local of the same name must shadow the field too.</summary>
    [Test]
    public void Class_LocalShadowsAField()
        => Assert.That(Run(
            "Class Box\n" +
            "Public Value As Integer\n" +
            "Public Function Calc() As Integer\nDim Value As Integer\nValue = 5\nReturn Value\nEnd Function\n" +
            "End Class\n" +
            "Sub Main()\nDim b As New Box()\nb.Value = 1\nConsole.WriteLine(b.Calc())\nEnd Sub"),
            Is.EqualTo("5"));

    // ---------------------------------------------------------------- constructors

    [Test]
    public void Class_ConstructorWithParameters()
        => Assert.That(Run(
            "Class Point2\n" +
            "Public X As Integer\n" +
            "Public Sub New(x As Integer)\nMe.X = x\nEnd Sub\n" +
            "End Class\n" +
            "Sub Main()\nDim p As New Point2(9)\nConsole.WriteLine(p.X)\nEnd Sub"),
            Is.EqualTo("9"));

    // ---------------------------------------------------------------- properties

    [Test]
    public void Class_AutoPropertyRoundTrips()
        => Assert.That(Run(
            "Class Person\nPublic Property Name As String\nEnd Class\n" +
            "Sub Main()\nDim p As New Person()\np.Name = \"Ada\"\nConsole.WriteLine(p.Name)\nEnd Sub"),
            Is.EqualTo("Ada"));

    // ---------------------------------------------------------------- inheritance

    [Test]
    public void Class_InheritedMethodIsCallable()
        => Assert.That(Run(
            "Class Animal\nPublic Sub Speak()\nConsole.WriteLine(\"generic\")\nEnd Sub\nEnd Class\n" +
            "Class Dog\nInherits Animal\nEnd Class\n" +
            "Sub Main()\nDim d As New Dog()\nd.Speak()\nEnd Sub"),
            Is.EqualTo("generic"));

    /// <summary>
    /// Prototype dispatch is virtual by default, which already matches VB's Overridable —
    /// so an override must win without any extra emission.
    /// </summary>
    [Test]
    public void Class_OverrideDispatchesToTheDerivedMethod()
        => Assert.That(Run(
            "Class Animal\nPublic Overridable Sub Speak()\nConsole.WriteLine(\"generic\")\nEnd Sub\nEnd Class\n" +
            "Class Dog\nInherits Animal\nPublic Overrides Sub Speak()\nConsole.WriteLine(\"woof\")\nEnd Sub\nEnd Class\n" +
            "Sub Main()\nDim d As New Dog()\nd.Speak()\nEnd Sub"),
            Is.EqualTo("woof"));

    /// <summary>
    /// A value-producing node is emitted into block.Instructions AND handed back as the
    /// expression result. If the Expr arm RE-RENDERS it instead of returning the name it was
    /// bound to, the work happens TWICE — here the constructor runs twice.
    ///
    /// <para>Every earlier class test missed this because none had an observable side effect:
    /// constructing Point2 twice assigns the same field twice and looks identical. Only a
    /// constructor that PRINTS reveals it.</para>
    /// </summary>
    [Test]
    public void Constructor_RunsExactlyOnce()
        => Assert.That(Run(
            "Class Noisy\nPublic Sub New()\nConsole.WriteLine(\"ctor\")\nEnd Sub\nEnd Class\n" +
            "Sub Main()\nDim n As New Noisy()\nEnd Sub"),
            Is.EqualTo("ctor"));

    /// <summary>The same hazard for a method call whose result is consumed.</summary>
    [Test]
    public void MethodCallUsedAsAValue_RunsExactlyOnce()
        => Assert.That(Run(
            "Class Counter\n" +
            "Public N As Integer\n" +
            "Public Function Bump() As Integer\nN = N + 1\nConsole.WriteLine(\"call\")\nReturn N\nEnd Function\n" +
            "End Class\n" +
            "Sub Main()\nDim c As New Counter()\nc.N = 0\nDim r As Integer\nr = c.Bump()\nConsole.WriteLine(r)\nEnd Sub"),
            Is.EqualTo("call\n1"));

    // ---------------------------------------------------------------- emission shape

    /// <summary>
    /// Class members must NOT also be emitted as free functions. They flatten into
    /// module.Functions with unqualified names AND are reachable via
    /// IRClass.Methods[].Implementation, so a naive walk emits every method twice — and with
    /// two classes sharing a method name, the second definition silently wins.
    /// </summary>
    [Test]
    public void ClassMethods_AreNotAlsoEmittedAsFreeFunctions()
    {
        var js = JsTestSupport.Compile(
            "Class A\nPublic Sub Handle()\nConsole.WriteLine(\"a\")\nEnd Sub\nEnd Class\n" +
            "Class B\nPublic Sub Handle()\nConsole.WriteLine(\"b\")\nEnd Sub\nEnd Class\n" +
            "Sub Main()\nEnd Sub");

        Assert.That(System.Text.RegularExpressions.Regex.Matches(js, @"^function Handle\(",
            System.Text.RegularExpressions.RegexOptions.Multiline).Count, Is.Zero,
            "a class method must not leak out as a top-level function");
        Assert.That(js, Does.Contain("class A"));
        Assert.That(js, Does.Contain("class B"));
    }
}
