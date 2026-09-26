using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.SemanticAnalysis;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// An array type written on the TYPE — <c>As Integer()</c>, <c>As Integer[]</c>,
/// <c>As Integer(,)</c> — as VB writes it wherever there is no name to carry the suffix. The parser
/// only read the suffix on a NAME (<c>Dim a() As Integer</c>, <c>v[] As Integer</c>), so a
/// Function could not return an array at all, <c>List(Of Integer())</c> could not be written, and
/// even the parameter spelling <c>v() As Integer</c> was refused with "Expected 'As'".
/// </summary>
[TestFixture]
public class ArrayTypeSuffixTests
{
    private static ProgramNode Parse(string source, out string errors)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        errors = string.Join("; ", parser.Errors.Select(e => e.Message));
        return ast;
    }

    private static string Errors(string source)
    {
        var ast = Parse(source, out var errors);
        if (errors.Length > 0) return errors;
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);
        return string.Join("; ", analyzer.Errors.Select(e => e.Message));
    }

    private static FunctionNode Function(string source)
    {
        var ast = Parse(source, out var errors);
        Assert.That(errors, Is.Empty);
        return ast.Declarations.OfType<FunctionNode>().Single();
    }

    [TestCase("()", 1)]
    [TestCase("[]", 1)]
    [TestCase("(,)", 2)]
    [TestCase("[,,]", 3)]
    public void AReturnType_CarriesItsSuffix(string suffix, int rank)
    {
        var type = Function($"Function F() As Integer{suffix}\n    Return Nothing\nEnd Function").ReturnType;
        Assert.That(type.Name, Is.EqualTo("Integer"));
        Assert.That(type.IsArray, Is.True);
        Assert.That(type.ArrayDimensions, Has.Count.EqualTo(rank));
        Assert.That(type.ArrayDimensions, Has.All.Null, "a type names no size");
    }

    [TestCase("v() As Integer")]
    [TestCase("v[] As Integer")]
    [TestCase("v As Integer()")]
    [TestCase("v As Integer[]")]
    [TestCase("ParamArray v As Integer()")]
    public void EveryParameterSpelling_IsAnArray(string parameter)
    {
        var type = Function($"Function F({parameter}) As Integer\n    Return 0\nEnd Function").Parameters.Single().Type;
        Assert.That(type.IsArray, Is.True);
        Assert.That(type.ArrayDimensions, Has.Count.EqualTo(1));
    }

    [Test]
    public void AGenericArgument_CarriesItsSuffix()
    {
        var type = Function("Function F() As List(Of String())\n    Return Nothing\nEnd Function").ReturnType;
        Assert.That(type.IsArray, Is.False);
        Assert.That(type.GenericArguments.Single().IsArray, Is.True);
    }

    /// <summary>
    /// After <c>New</c> the parentheses are the constructor's, never an array suffix: reading them
    /// as one would turn every <c>As New T()</c> into an array of T.
    /// </summary>
    [TestCase("Dim l As New List(Of Integer)()")]
    [TestCase("Dim l = New List(Of Integer)()")]
    [TestCase("Dim a() As Integer = New Integer() {1, 2}")]
    public void AfterNew_TheParenthesesStayTheConstructors(string body)
    {
        var ast = Parse("Sub Main()\n    " + body + "\nEnd Sub", out var errors);
        Assert.That(errors, Is.Empty);
        Assert.That(Errors("Sub Main()\n    " + body + "\nEnd Sub"), Is.Empty);
    }

    [TestCase("Dim a As Integer() = {1, 2}")]
    [TestCase("Dim a As Integer[] = {}")]
    [TestCase("Dim g As Integer(,)")]
    [TestCase("Dim l As New List(Of Integer())()\n    l.Add({1})")]
    [TestCase("Dim f = Function(q() As Integer) q.Length")]
    [TestCase("Dim f = Function(q As String()) q(0)")]
    public void EveryDeclarationSite_Analyzes(string body)
        => Assert.That(Errors("Sub Main()\n    " + body + "\nEnd Sub"), Is.Empty);

    [TestCase("Class C\n    Public Items As String()\nEnd Class")]
    [TestCase("Class C\n    Public Function All() As String()\n        Return {\"a\"}\n    End Function\nEnd Class")]
    [TestCase("Structure S\n    Public Cells As Integer()\nEnd Structure")]
    public void MemberTypes_Analyze(string declaration)
        => Assert.That(Errors(declaration + "\nSub Main()\nEnd Sub"), Is.Empty);

    /// <summary>
    /// A size on a type used to be left behind for whatever parsed next, so
    /// <c>Function F() As Integer(3)</c> silently declared a SCALAR Integer return.
    /// </summary>
    [TestCase("Function F() As Integer(3)\n    Return Nothing\nEnd Function", "array size cannot appear in a type")]
    [TestCase("Function F() As Integer[3]\n    Return Nothing\nEnd Function", "array size cannot appear in a type")]
    [TestCase("Class C\n    Public X As Integer(2)\nEnd Class", "array size cannot appear in a type")]
    [TestCase("Sub Main()\n    Dim a As Integer()()\nEnd Sub", "Jagged array types")]
    [TestCase("Sub Main()\n    Dim a() As Integer()\nEnd Sub", "both its name and its type")]
    [TestCase("Sub P(v() As Integer())\nEnd Sub", "both its name and its type")]
    [TestCase("Class C\n    Public X() As Integer()\nEnd Class", "both its name and its type")]
    public void WhatTheCompilerCannotRepresent_IsRefused(string source, string error)
    {
        Parse(source, out var errors);
        Assert.That(errors, Does.Contain(error));
    }
}

[TestFixture]
[Category("Integration")]   // spawns node, a C++ compiler and dotnet
[NonParallelizable]
public class ArrayTypeSuffixExecutionTests
{
    private const string Program = @"
Class Bag
    Public Items As String()
    Public Sub New()
        Items = {""a"", ""b"", ""c""}
    End Sub
    Public Function First2() As String()
        Return {Items(0), Items(1)}
    End Function
End Class

Function MakeInts(n As Integer) As Integer()
    Dim r As Integer[]
    ReDim r[n]
    For i As Integer = 0 To n - 1
        r(i) = i * i
    Next
    Return r
End Function

Function Twice(v As Integer[]) As Integer[]
    Dim r() As Integer
    ReDim r[v.Length]
    For i As Integer = 0 To v.Length - 1
        r(i) = v(i) * 2
    Next
    Return r
End Function

Function Total(v() As Integer) As Integer
    Dim t As Integer = 0
    For Each x As Integer In v
        t = t + x
    Next
    Return t
End Function

Function Join2(v As String()) As String
    Return v(0) & v(1)
End Function

Sub Main()
    Dim a As Integer() = MakeInts(4)
    Console.WriteLine(Total(a))
    Dim b As Integer[] = Twice(a)
    Console.WriteLine(Total(b))
    Console.WriteLine(Total(Twice(MakeInts(3))))
    Dim bag As New Bag()
    Console.WriteLine(Join2(bag.First2()))
    Console.WriteLine(bag.Items.Length)
    Dim empty As String() = {}
    Console.WriteLine(empty.Length)
    Dim lst As New List(Of Integer())()
    lst.Add(a)
    lst.Add(b)
    Console.WriteLine(lst.Count)
    Dim row As Integer() = lst(1)
    Console.WriteLine(row(3))
    Dim g = Function(k() As String) k(1)
    Console.WriteLine(g({""x"", ""y""}))
End Sub";

    private const string Expected = "14\n28\n10\nab\n3\n0\n2\n18\ny";

    private static string Normalize(string s) => s.Replace("\r\n", "\n").TrimEnd('\n');

    [Test]
    public void OnJavaScript() =>
        Assert.That(Normalize(JavaScriptExecutionTests.RunJs(Program)), Is.EqualTo(Expected));

    [Test]
    public void OnJavaScript_Optimized() =>
        Assert.That(Normalize(JavaScriptOptimizedExecutionTests.RunOptimized(Program)), Is.EqualTo(Expected));

    [Test]
    public void OnCpp()
    {
        var compiler = VisualGameStudio.Tests.Native.CppCompile.FindRunCompiler();
        if (compiler == null) Assert.Ignore("No C++ compiler available on this machine");

        var cpp = new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false })
            .Generate(InterpolatedStringLoweringTests.Optimized(Program));
        Assert.That(Normalize(VisualGameStudio.Tests.Native.CppCompile.CompileAndRun(cpp, compiler.Value)),
            Is.EqualTo(Expected));
    }

    [Test]
    public void OnCSharp() =>
        Assert.That(Normalize(CliTestHarness.CompileRunCSharp(Program)), Is.EqualTo(Expected));
}
