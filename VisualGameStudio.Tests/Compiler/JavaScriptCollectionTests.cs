using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Plan task 18 — collections.
///
/// <para><b>List lowers to Array and Dictionary to Map, with REFERENCE semantics.</b> That
/// requirement is the one the C++ backend got wrong: value-wrapper collections diverged from
/// .NET and produced silently wrong results. In JavaScript reference semantics are free —
/// arrays and Maps ARE references — so this is the one area where JS is structurally easier
/// than C++ rather than harder.</para>
///
/// <para><b>What was broken before this.</b> `New List(Of Integer)()` emitted
/// <c>new List()</c> — a class that exists nowhere in JavaScript — and <c>.Add</c>/<c>.Count</c>
/// went out verbatim, where an Array has <c>push</c> and <c>length</c>. It compiled cleanly
/// and produced a ReferenceError at runtime.</para>
/// </summary>
[TestFixture]
[Category("Integration")]   // spawns node
public class JavaScriptCollectionTests
{
    private static string Run(string body) =>
        JavaScriptExecutionTests.RunJs($"Sub Main()\n{body}\nEnd Sub");

    // ---------------------------------------------------------------- List

    [Test]
    public void List_AddThenCount()
        => Assert.That(Run(
            "Dim l As New List(Of Integer)()\nl.Add(1)\nl.Add(2)\nConsole.WriteLine(l.Count)"),
            Is.EqualTo("2"));

    [Test]
    public void List_IndexerReads()
        => Assert.That(Run(
            "Dim l As New List(Of Integer)()\nl.Add(10)\nl.Add(20)\nConsole.WriteLine(l(1))"),
            Is.EqualTo("20"));

    [Test]
    public void List_IndexerWrites()
        => Assert.That(Run(
            "Dim l As New List(Of Integer)()\nl.Add(1)\nl(0) = 99\nConsole.WriteLine(l(0))"),
            Is.EqualTo("99"));

    [Test]
    public void List_IsEmptyOnCreation()
        => Assert.That(Run("Dim l As New List(Of Integer)()\nConsole.WriteLine(l.Count)"),
            Is.EqualTo("0"));

    [Test]
    public void List_ForEachIterates()
        => Assert.That(Run(
            "Dim l As New List(Of Integer)()\nl.Add(1)\nl.Add(2)\nl.Add(3)\n" +
            "For Each n As Integer In l\nConsole.WriteLine(n)\nNext"),
            Is.EqualTo("1\n2\n3"));

    /// <summary>
    /// THE bug-class guard. A collection assigned to a second name must be the SAME object,
    /// so a mutation through one alias is visible through the other. Value-wrapper
    /// collections are exactly what diverged on the C++ backend.
    /// </summary>
    [Test]
    public void List_HasReferenceSemantics()
        => Assert.That(Run(
            "Dim a As New List(Of Integer)()\n" +
            "Dim b As List(Of Integer)\n" +
            "a.Add(1)\n" +
            "b = a\n" +
            "b.Add(2)\n" +
            "Console.WriteLine(a.Count)"),
            Is.EqualTo("2"),
            "mutating through the second name must be visible through the first");

    /// <summary>Passing a collection to a procedure must not copy it either.</summary>
    [Test]
    public void List_PassedToAProcedure_IsNotCopied()
        => Assert.That(JavaScriptExecutionTests.RunJs(
            "Sub Fill(target As List(Of Integer))\ntarget.Add(7)\nEnd Sub\n" +
            "Sub Main()\nDim l As New List(Of Integer)()\nFill(l)\nConsole.WriteLine(l.Count)\nEnd Sub"),
            Is.EqualTo("1"));

    // ---------------------------------------------------------------- Dictionary

    [Test]
    public void Dictionary_AddThenRead()
        => Assert.That(Run(
            "Dim d As New Dictionary(Of String, Integer)()\nd.Add(\"a\", 1)\nConsole.WriteLine(d(\"a\"))"),
            Is.EqualTo("1"));

    [Test]
    public void Dictionary_Count()
        => Assert.That(Run(
            "Dim d As New Dictionary(Of String, Integer)()\nd.Add(\"a\", 1)\nd.Add(\"b\", 2)\n" +
            "Console.WriteLine(d.Count)"),
            Is.EqualTo("2"));

    /// <summary>Map.size, not Array.length — a Map's `.length` is undefined.</summary>
    [Test]
    public void Dictionary_IsEmptyOnCreation()
        => Assert.That(Run(
            "Dim d As New Dictionary(Of String, Integer)()\nConsole.WriteLine(d.Count)"),
            Is.EqualTo("0"));

    [Test]
    public void Dictionary_IndexerWrites()
        => Assert.That(Run(
            "Dim d As New Dictionary(Of String, Integer)()\nd(\"k\") = 5\nConsole.WriteLine(d(\"k\"))"),
            Is.EqualTo("5"));

    [Test]
    public void Dictionary_ContainsKey()
        => Assert.That(Run(
            "Dim d As New Dictionary(Of String, Integer)()\nd.Add(\"a\", 1)\n" +
            "Console.WriteLine(d.ContainsKey(\"a\"))\nConsole.WriteLine(d.ContainsKey(\"z\"))"),
            Is.EqualTo("true\nfalse"));

    [Test]
    public void Dictionary_HasReferenceSemantics()
        => Assert.That(Run(
            "Dim a As New Dictionary(Of String, Integer)()\n" +
            "Dim b As Dictionary(Of String, Integer)\n" +
            "b = a\n" +
            "b.Add(\"x\", 1)\n" +
            "Console.WriteLine(a.Count)"),
            Is.EqualTo("1"));
}
