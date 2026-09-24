using System.Text.RegularExpressions;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// The C# backend INLINES a single-use value into the expression that consumes it instead of
/// declaring a temp. Four visitors rendered an operand by its bare name (GetValueName) instead of
/// through EmitExpression, so an inlined operand came out as a temp that was never declared:
/// <list type="bullet">
/// <item><c>l(i) = i * 10</c> → <c>l[i] = t2;</c>: every computed value stored through a List or
/// Dictionary indexer (IRIndexerStore; IRArrayStore had the same code).</item>
/// <item><c>Yield i * 2</c> → <c>yield return t1;</c> (IRYield).</item>
/// <item><c>For Each v In MakeList(7)</c> → <c>foreach (int v in t0)</c> (IRForEach).</item>
/// </list>
/// Rendering them through EmitExpression also needed GetOperands to COUNT the For Each
/// collection, the Yield value and the array store's operands as uses: an uncounted operand reads
/// as unused, gets emitted as a standalone statement too, and a call then runs twice.
/// </summary>
[TestFixture]
public class CSharpInlinedOperandCodeGenTests
{
    private static string Cs(string source) =>
        new BasicLang.Compiler.CodeGen.CSharp.CSharpCodeGenerator().Generate(JsTestSupport.BuildModule(source));

    /// <summary>A compiler temp name (t0, t1, ...) used anywhere as an operand.</summary>
    private static readonly Regex TempOperand = new(@"(?<![\w.])t\d+\b");

    [Test]
    public void AComputedValue_StoredThroughAListOrDictionaryIndexer_IsInlined()
    {
        var cs = Cs(@"
Sub Main()
    Dim l As New List(Of Integer)()
    l.Add(0)
    Dim d As New Dictionary(Of String, Integer)()
    For i As Integer = 0 To 0
        l(i) = i * 10
        d(""k"") = i + 1
    Next
End Sub");

        Assert.That(cs, Does.Contain("l[i] = i * 10;"), cs);
        Assert.That(cs, Does.Contain("d[\"k\"] = i + 1;"), cs);
        Assert.That(TempOperand.IsMatch(cs), Is.False, cs);
    }

    [Test]
    public void AComputedYield_IsInlined()
    {
        var cs = Cs(@"
Iterator Function Doubles(n As Integer) As Integer
    For i As Integer = 1 To n
        Yield i * 2
    Next
End Function

Sub Main()
    For Each v As Integer In Doubles(3)
        Console.WriteLine(v)
    Next
End Sub");

        Assert.That(cs, Does.Contain("yield return i << 1;").Or.Contain("yield return i * 2;"), cs);
        Assert.That(TempOperand.IsMatch(cs), Is.False, cs);
    }

    [Test]
    public void AForEachOverACall_CallsItOnce_Inline()
    {
        var cs = Cs(@"
Function MakeList(k As Integer) As List(Of Integer)
    Dim l As New List(Of Integer)()
    l.Add(k)
    Return l
End Function

Sub Main()
    For Each w As Integer In MakeList(7)
        Console.WriteLine(w)
    Next
End Sub");

        Assert.That(cs, Does.Contain("foreach (int w in MakeList(7))"), cs);
        Assert.That(Regex.Matches(cs, @"MakeList\(7\)").Count, Is.EqualTo(1),
            "the call must appear once, not also as a standalone statement:\n" + cs);
    }
}

[TestFixture]
[Category("Integration")]   // builds and runs with dotnet through the CLI
public class CSharpInlinedOperandExecutionTests
{
    [Test]
    public void AllThreeShapes_RunAndPrintWhatTheOtherBackendsPrint()
    {
        const string source = @"
Dim Calls As Integer = 0

Function MakeList(k As Integer) As List(Of Integer)
    Calls = Calls + 1
    Dim l As New List(Of Integer)()
    l.Add(k)
    l.Add(k + 1)
    Return l
End Function

Iterator Function Doubles(n As Integer) As Integer
    For i As Integer = 1 To n
        Yield i * 2
    Next
End Function

Sub Main()
    Dim l As New List(Of Integer)()
    l.Add(0)
    l.Add(0)
    For i As Integer = 0 To 1
        l(i) = i * 10
    Next
    Console.WriteLine(l(1))
    Dim d As New Dictionary(Of String, Integer)()
    d(""k"") = l(1) + 1
    Console.WriteLine(d(""k""))
    For Each v As Integer In Doubles(2)
        Console.WriteLine(v)
    Next
    For Each w As Integer In MakeList(7)
        Console.WriteLine(w)
    Next
    Console.WriteLine(""calls="" & Calls)
End Sub";

        Assert.That(CliTestHarness.CompileRunCSharp(source).Replace("\r\n", "\n").TrimEnd('\n'),
            Is.EqualTo("10\n11\n2\n4\n7\n8\ncalls=1"));
    }
}
