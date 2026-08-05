using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Plan task 16 — arrays.
///
/// <para><b>Sizes are not in the instruction stream.</b> <c>Dim a(4) As Integer</c> emits only
/// an <c>IRAlloca</c> with <c>Size == 1</c>; the element count lives on the DECLARATION, in
/// <c>TypeInfo.ArrayDimensionSizes</c>. So allocation happens where locals, globals and fields
/// are declared — three sites, which the C# backend got wrong by handling only the first.</para>
///
/// <para><b>⚠ A note on the bound.</b> In this compiler <c>Dim a(4)</c> means FOUR elements
/// (0..3), not VB's five. That is measured behaviour shared by the C# and C++ backends, so the
/// JS backend matches it deliberately rather than "helpfully" adding one and diverging from
/// both shipping backends and from the analyzer's own bounds reasoning.</para>
/// </summary>
[TestFixture]
[Category("Integration")]   // spawns node
public class JavaScriptArrayTests
{
    private static string Run(string body) =>
        JavaScriptExecutionTests.RunJs($"Sub Main()\n{body}\nEnd Sub");

    // ---------------------------------------------------------------- allocation

    /// <summary>
    /// Elements must be INITIALISED, not merely reserved. <c>new Array(4)</c> is a sparse
    /// array whose holes read as <c>undefined</c>; BasicLang expects 0 for Integer.
    /// </summary>
    [Test]
    public void Array_ElementsDefaultToZero()
        => Assert.That(Run("Dim a(4) As Integer\nConsole.WriteLine(a(0))"), Is.EqualTo("0"));

    [Test]
    public void Array_StringElementsDefaultToEmpty()
        => Assert.That(Run("Dim s(2) As String\nConsole.WriteLine(\"[\" & s(0) & \"]\")"),
            Is.EqualTo("[]"));

    [Test]
    public void Array_HasTheDeclaredLength()
        => Assert.That(Run("Dim a(4) As Integer\nConsole.WriteLine(a.Length)"), Is.EqualTo("4"));

    // ---------------------------------------------------------------- read / write

    /// <summary>
    /// The write must reach the ARRAY, not a temp. A gep materialised as a value makes the
    /// following store write to that temp and silently drops the element write — a bug this
    /// repo has already measured once, where C++ printed 0 where C# printed 5.
    /// </summary>
    [Test]
    public void Array_WriteThenReadRoundTrips()
        => Assert.That(Run("Dim a(4) As Integer\na(0) = 42\nConsole.WriteLine(a(0))"),
            Is.EqualTo("42"));

    [Test]
    public void Array_WriteIsVisibleAtAnotherIndex()
        => Assert.That(Run(
            "Dim a(4) As Integer\na(0) = 1\na(3) = 9\n" +
            "Console.WriteLine(a(0))\nConsole.WriteLine(a(3))"),
            Is.EqualTo("1\n9"));

    /// <summary>A write through a computed index must land in the same slot a read uses.</summary>
    [Test]
    public void Array_ComputedIndex()
        => Assert.That(Run(
            "Dim a(4) As Integer\nDim i As Integer\ni = 2\na(i) = 7\nConsole.WriteLine(a(i))"),
            Is.EqualTo("7"));

    /// <summary>
    /// The whole point of arrays: a write inside a loop must still be there afterwards.
    /// This is the case a dropped store fails while every single-statement test passes.
    /// </summary>
    [Test]
    public void Array_WrittenInALoop_RetainsEveryElement()
        => Assert.That(Run(
            "Dim a(4) As Integer\n" +
            "For i As Integer = 0 To 3\na(i) = i * 10\nNext\n" +
            "Dim total As Integer\ntotal = 0\n" +
            "For j As Integer = 0 To 3\ntotal = total + a(j)\nNext\n" +
            "Console.WriteLine(total)"),
            Is.EqualTo("60"));

    // ---------------------------------------------------------------- multi-dimensional

    /// <summary>
    /// ⛔ Indices must nest as <c>[i][j]</c>. Joining them as <c>[i, j]</c> — what the C#
    /// backend emits for its own target — is VALID JavaScript: the comma operator discards
    /// <c>i</c> and evaluates to <c>a[j]</c>. No syntax error, no exception, just the wrong
    /// element.
    /// </summary>
    [Test]
    public void MultiDimArray_IndexesIndependently()
        => Assert.That(Run(
            "Dim g(2,3) As Integer\ng(0,0) = 1\ng(1,2) = 5\n" +
            "Console.WriteLine(g(0,0))\nConsole.WriteLine(g(1,2))"),
            Is.EqualTo("1\n5"));

    /// <summary>
    /// Rows must be DISTINCT objects. Allocating with <c>new Array(d0).fill(new Array(d1))</c>
    /// shares one row across every index, so a write to one row appears in all of them.
    /// </summary>
    [Test]
    public void MultiDimArray_RowsAreNotShared()
        => Assert.That(Run(
            "Dim g(2,3) As Integer\ng(0,0) = 7\nConsole.WriteLine(g(1,0))"),
            Is.EqualTo("0"));
}
