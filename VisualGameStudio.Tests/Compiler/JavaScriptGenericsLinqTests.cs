using BasicLang.Compiler.CodeGen;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Plan task 23 — generics erasure and LINQ.
///
/// <para><b>Generics erase.</b> JavaScript is untyped, so a generic function or class emits
/// exactly like a non-generic one and <c>List(Of Integer)</c> and <c>List(Of String)</c>
/// produce identical output. That is the spec's stated posture — the OPPOSITE of the C++
/// backend, which preserves the type system into real templates and pays for it.</para>
///
/// <para><b>LINQ lowers to array methods.</b> Where/Select/OrderBy become
/// <c>.filter</c>/<c>.map</c>/<c>.sort</c>.</para>
/// </summary>
[TestFixture]
[Category("Integration")]   // spawns node
public class JavaScriptGenericsLinqTests
{
    private static string Run(string source) => JavaScriptExecutionTests.RunJs(source);

    private static string InMain(string body) =>
        JavaScriptExecutionTests.RunJs($"Sub Main()\n{body}\nEnd Sub");

    // ---------------------------------------------------------------- generics

    [Test]
    public void GenericFunction_Erases()
        => Assert.That(Run(
            "Function Echo(Of T)(value As T) As T\nReturn value\nEnd Function\n" +
            "Sub Main()\nConsole.WriteLine(Echo(Of Integer)(7))\nEnd Sub"),
            Is.EqualTo("7"));

    /// <summary>The same generic function must serve every instantiation.</summary>
    [Test]
    public void GenericFunction_WorksForTwoTypeArguments()
        => Assert.That(Run(
            "Function Echo(Of T)(value As T) As T\nReturn value\nEnd Function\n" +
            "Sub Main()\n" +
            "Console.WriteLine(Echo(Of Integer)(1))\n" +
            "Console.WriteLine(Echo(Of String)(\"a\"))\n" +
            "End Sub"),
            Is.EqualTo("1\na"));

    [Test]
    public void GenericClass_Erases()
        => Assert.That(Run(
            "Class Box(Of T)\nPublic Value As T\nEnd Class\n" +
            "Sub Main()\nDim b As New Box(Of Integer)()\nb.Value = 5\nConsole.WriteLine(b.Value)\nEnd Sub"),
            Is.EqualTo("5"));

    /// <summary>
    /// Two instantiations of one generic collection must produce the same JS, since the type
    /// argument has no runtime form at all.
    /// </summary>
    [Test]
    public void GenericCollections_OfDifferentTypes_BothWork()
        => Assert.That(InMain(
            "Dim a As New List(Of Integer)()\na.Add(1)\n" +
            "Dim b As New List(Of String)()\nb.Add(\"x\")\n" +
            "Console.WriteLine(a(0))\nConsole.WriteLine(b(0))"),
            Is.EqualTo("1\nx"));

    // ---------------------------------------------------------------- LINQ

    [Test]
    public void Linq_Where_Filters()
        => Assert.That(InMain(
            "Dim l As New List(Of Integer)()\nl.Add(1)\nl.Add(2)\nl.Add(3)\nl.Add(4)\n" +
            "Dim big = l.Where(Function(x As Integer) x > 2)\n" +
            "For Each n As Integer In big\nConsole.WriteLine(n)\nNext"),
            Is.EqualTo("3\n4"));

    [Test]
    public void Linq_Select_Projects()
        => Assert.That(InMain(
            "Dim l As New List(Of Integer)()\nl.Add(1)\nl.Add(2)\n" +
            "Dim doubled = l.Select(Function(x As Integer) x * 2)\n" +
            "For Each n As Integer In doubled\nConsole.WriteLine(n)\nNext"),
            Is.EqualTo("2\n4"));

    /// <summary>
    /// The plan's own criterion: filter then project over five elements. Also the reason
    /// the generator keeps a side table of sequence-valued names — the IR types the result
    /// of <c>Where</c> as plain <c>Object</c>, so the receiver of <c>Select</c> carries no
    /// hint that it is a collection.
    /// </summary>
    [Test]
    public void Linq_WhereThenSelect()
        => Assert.That(InMain(
            "Dim l As New List(Of Integer)()\n" +
            "l.Add(1)\nl.Add(2)\nl.Add(3)\nl.Add(4)\nl.Add(5)\n" +
            "Dim r = l.Where(Function(x As Integer) x > 2).Select(Function(x As Integer) x * 10)\n" +
            "For Each n As Integer In r\nConsole.WriteLine(n)\nNext"),
            Is.EqualTo("30\n40\n50"));

    [Test]
    public void Linq_AnyAllCountSum()
        => Assert.That(InMain(
            "Dim l As New List(Of Integer)()\nl.Add(1)\nl.Add(2)\nl.Add(3)\n" +
            "Console.WriteLine(l.Any(Function(x As Integer) x > 2))\n" +
            "Console.WriteLine(l.All(Function(x As Integer) x > 2))\n" +
            "Console.WriteLine(l.Count())\n" +
            "Console.WriteLine(l.Sum())"),
            Is.EqualTo("true\nfalse\n3\n6"));

    [Test]
    public void Linq_SkipTakeDistinct()
        => Assert.That(InMain(
            "Dim l As New List(Of Integer)()\nl.Add(1)\nl.Add(1)\nl.Add(2)\nl.Add(3)\nl.Add(4)\n" +
            "For Each n As Integer In l.Distinct().Skip(1).Take(2)\nConsole.WriteLine(n)\nNext"),
            Is.EqualTo("2\n3"));

    /// <summary>
    /// THE sort trap. A bare <c>.sort()</c> compares as STRINGS, so this exact input would
    /// come back 1, 10, 9 — the whole reason OrderBy emits a comparator.
    /// </summary>
    [Test]
    public void Linq_OrderBy_ComparesNumerically_NotAsStrings()
        => Assert.That(InMain(
            "Dim l As New List(Of Integer)()\nl.Add(10)\nl.Add(9)\nl.Add(1)\n" +
            "For Each n As Integer In l.OrderBy(Function(x As Integer) x)\nConsole.WriteLine(n)\nNext"),
            Is.EqualTo("1\n9\n10"));

    [Test]
    public void Linq_OrderByDescending()
        => Assert.That(InMain(
            "Dim l As New List(Of Integer)()\nl.Add(2)\nl.Add(30)\nl.Add(1)\n" +
            "For Each n As Integer In l.OrderByDescending(Function(x As Integer) x)\nConsole.WriteLine(n)\nNext"),
            Is.EqualTo("30\n2\n1"));

    /// <summary>
    /// OrderBy must not disturb its source — <c>.sort()</c> alone would, because it sorts
    /// IN PLACE and returns the very same array.
    /// </summary>
    [Test]
    public void Linq_OrderBy_LeavesTheSourceAlone()
        => Assert.That(InMain(
            "Dim l As New List(Of Integer)()\nl.Add(3)\nl.Add(1)\nl.Add(2)\n" +
            "Dim sorted = l.OrderBy(Function(x As Integer) x)\n" +
            "For Each n As Integer In l\nConsole.WriteLine(n)\nNext"),
            Is.EqualTo("3\n1\n2"));

    /// <summary>Same in-place hazard as OrderBy: <c>.reverse()</c> mutates.</summary>
    [Test]
    public void Linq_Reverse_LeavesTheSourceAlone()
        => Assert.That(InMain(
            "Dim l As New List(Of Integer)()\nl.Add(1)\nl.Add(2)\n" +
            "Dim r = l.Reverse()\n" +
            "For Each n As Integer In r\nConsole.WriteLine(n)\nNext\n" +
            "For Each n As Integer In l\nConsole.WriteLine(n)\nNext"),
            Is.EqualTo("2\n1\n1\n2"));

    /// <summary>
    /// PINS A DELIBERATE DIVERGENCE FROM .NET. <c>.filter</c> materialises at the call, where
    /// .NET's <c>Where</c> defers and would see the later Add. This is the chosen lowering,
    /// recorded here so a future change to it is a decision rather than an accident.
    /// </summary>
    [Test]
    public void Linq_IsEager_NotDeferred()
        => Assert.That(InMain(
            "Dim l As New List(Of Integer)()\nl.Add(1)\nl.Add(5)\n" +
            "Dim big = l.Where(Function(x As Integer) x > 2)\n" +
            "l.Add(9)\n" +
            "For Each n As Integer In big\nConsole.WriteLine(n)\nNext"),
            Is.EqualTo("5"));

    /// <summary>
    /// A user's own method must NOT be rewritten just because it shares a LINQ name. The
    /// mapping is gated on the receiver being a known sequence, not on the name alone.
    /// </summary>
    [Test]
    public void Linq_NamesOnAUserClass_AreLeftAlone()
        => Assert.That(Run(
            "Class Query\n" +
            "Public Function Select2() As Integer\nReturn 42\nEnd Function\n" +
            "End Class\n" +
            "Sub Main()\nDim q As New Query()\nConsole.WriteLine(q.Select2())\nEnd Sub"),
            Is.EqualTo("42"));
}

/// <summary>
/// BL7008 — the refusal half of task 23. Codegen-only, so these belong in the fast subset.
/// </summary>
[TestFixture]
public class JavaScriptLinqRejectionTests
{
    /// <summary>
    /// <c>First</c> throws on an empty sequence in .NET; <c>a[0]</c> quietly answers
    /// undefined. Refused rather than approximated.
    /// </summary>
    [Test]
    public void ThrowOnEmptyOperator_IsRejected()
    {
        var ex = Assert.Throws<ForeignFeatureException>(() => JsTestSupport.Compile(
            "Sub Main()\nDim l As New List(Of Integer)()\nDim f = l.First()\nEnd Sub"));

        Assert.That(ex.Message, Does.Contain("BL7008"));
        Assert.That(ex.Message, Does.Contain("First"));
    }

    [TestCase("Min")]
    [TestCase("Max")]
    [TestCase("Average")]
    [TestCase("Last")]
    public void EmptySequenceOperator_IsRejected(string op)
        => Assert.That(Assert.Throws<ForeignFeatureException>(() => JsTestSupport.Compile(
                $"Sub Main()\nDim l As New List(Of Integer)()\nDim m = l.{op}()\nEnd Sub"))
            .Message, Does.Contain("BL7008"));

    /// <summary>
    /// The rejection is gated on the RECEIVER too — a user class with a method named
    /// <c>Min</c> is its own method, not LINQ, and must compile.
    /// </summary>
    [Test]
    public void SameNameOnAUserClass_IsNotRejected()
        => Assert.That(JsTestSupport.Compile(
            "Class Stats\n" +
            "Public Function Min() As Integer\nReturn 1\nEnd Function\n" +
            "End Class\n" +
            "Sub Main()\nDim s As New Stats()\nDim v = s.Min()\nEnd Sub"),
            Does.Contain("Min"));
}
