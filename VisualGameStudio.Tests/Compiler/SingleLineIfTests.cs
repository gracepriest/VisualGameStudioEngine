using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.CodeGen.CPlusPlus;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// The single-line If, as in VB: <c>If c Then s1 : s2 Else s3 : s4</c>, where everything to the
/// end of the line belongs to the If. And <c>:</c> as a statement separator, which the lexer has
/// always produced and language.md uses (<c>Dim t = a : a = b : b = t</c>).
///
/// <para>Before: the single-line If took ONE statement and returned, so the enclosing block met
/// <c>Else</c> where a statement should start — <c>If x &gt; 3 Then A() Else B()</c> failed with
/// "Unexpected token in expression: 'Else'". No statement loop consumed <c>:</c>, so
/// <c>a() : b()</c> failed the same way on the colon.</para>
/// </summary>
[TestFixture]
public class SingleLineIfTests
{
    private static BlockNode MainBody(string body)
    {
        var parser = new Parser(new Lexer("Sub Main()\n" + body + "\nEnd Sub").Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty, string.Join("; ", parser.Errors.Select(e => e.Message)));
        return ast.Declarations.OfType<SubroutineNode>().Single().Body;
    }

    private static IfStatementNode OnlyIf(string body) =>
        (IfStatementNode)MainBody(body).Statements.Single();

    private static string ParseErrors(string body)
    {
        var parser = new Parser(new Lexer("Sub Main()\n" + body + "\nEnd Sub").Tokenize());
        parser.Parse();
        return string.Join("; ", parser.Errors.Select(e => e.Message));
    }

    [Test]
    public void ThenAndElse_OnOneLine()
    {
        var node = OnlyIf("If x Then a() Else b()");
        Assert.That(node.ThenBlock.Statements, Has.Count.EqualTo(1));
        Assert.That(node.ElseBlock.Statements, Has.Count.EqualTo(1));
    }

    [Test]
    public void AColonJoinedStatement_BelongsToTheIf_NotAfterIt()
    {
        var node = OnlyIf("If x Then a() : b()");
        Assert.That(node.ThenBlock.Statements, Has.Count.EqualTo(2));
        Assert.That(node.ElseBlock, Is.Null);
    }

    [Test]
    public void AColonAfterElse_BelongsToTheElse()
    {
        var node = OnlyIf("If x Then a() Else b() : c()");
        Assert.That(node.ThenBlock.Statements, Has.Count.EqualTo(1));
        Assert.That(node.ElseBlock.Statements, Has.Count.EqualTo(2));
    }

    [Test]
    public void AnEmptyThenPart_IsAllowed()
    {
        var node = OnlyIf("If x Then Else b()");
        Assert.That(node.ThenBlock.Statements, Is.Empty);
        Assert.That(node.ElseBlock.Statements, Has.Count.EqualTo(1));
    }

    [Test]
    public void ANestedSingleLineIf_TakesTheElse()
    {
        // VB binds an Else to the nearest If.
        var outer = OnlyIf("If x Then If y Then a() Else b()");
        Assert.That(outer.ElseBlock, Is.Null);
        var inner = (IfStatementNode)outer.ThenBlock.Statements.Single();
        Assert.That(inner.ElseBlock.Statements, Has.Count.EqualTo(1));
    }

    [Test]
    public void ElseIf_ChainsThroughANestedIf()
    {
        var node = OnlyIf("If x Then Return 1 Else If y Then Return 2 Else Return 3");
        var nested = (IfStatementNode)node.ElseBlock.Statements.Single();
        Assert.That(nested.ElseBlock.Statements, Has.Count.EqualTo(1));
    }

    [Test]
    public void ASingleLineIf_InsideABlockIf_LeavesTheBlockElseAlone()
    {
        var node = OnlyIf("If x Then\n    If y Then a() Else b()\nElse\n    c()\nEnd If");
        Assert.That(node.ThenBlock.Statements.Single(), Is.InstanceOf<IfStatementNode>());
        Assert.That(node.ElseBlock.Statements, Has.Count.EqualTo(1));
    }

    [Test]
    public void AColon_SeparatesStatements_InABlock()
        => Assert.That(MainBody("Dim t As Integer = 1 : Dim u As Integer = 2 : t = u").Statements,
            Has.Count.EqualTo(3));

    [Test]
    public void AColon_SeparatesACaseFromItsStatement()
        => Assert.That(ParseErrors("Select Case 1\n    Case 1 : a()\n    Case Else : b()\nEnd Select"), Is.Empty);

    [Test]
    public void AOneLineForLoop_Parses()
        => Assert.That(ParseErrors("For i As Integer = 1 To 3 : a(i) : Next"), Is.Empty);

    [Test]
    public void AStrayToken_AfterASingleLineIf_IsStillRefused()
        => Assert.That(ParseErrors("If x Then a() b()"), Does.Contain("End of statement expected, found 'b'"));
}

[TestFixture]
[Category("Integration")]   // spawns node, a C++ compiler and dotnet
[NonParallelizable]
public class SingleLineIfExecutionTests
{
    /// <summary>
    /// Every single-line shape and <c>:</c>-joined lines, on each backend. The expected text is
    /// what VB (and the C# backend) prints: a <c>:</c>-joined statement runs only when its branch does.
    /// </summary>
    private const string Program = @"
Function Sign(n As Integer) As String
    If n > 0 Then Return ""pos"" Else If n < 0 Then Return ""neg"" Else Return ""zero""
End Function

Sub Main()
    Dim x As Integer = 5
    For i As Integer = 1 To 3 : Console.Write(i) : Next
    Console.WriteLine()
    Dim p As Integer = 1 : Dim q As Integer = 2
    Dim t = p : p = q : q = t
    Console.WriteLine(p & "","" & q)
    If x > 3 Then Console.WriteLine(""big"") Else Console.WriteLine(""small"")
    If x < 3 Then Console.WriteLine(""A"") Else Console.WriteLine(""B"")
    If x > 3 Then Console.WriteLine(""c1"") : Console.WriteLine(""c2"")
    If x < 3 Then Console.WriteLine(""d1"") : Console.WriteLine(""d2"")
    If x < 3 Then Console.WriteLine(""e1"") Else Console.WriteLine(""e2"") : Console.WriteLine(""e3"")
    If x > 3 Then Else Console.WriteLine(""never"")
    If x > 3 Then If x > 10 Then Console.WriteLine(""f1"") Else Console.WriteLine(""f2"")
    Console.WriteLine(Sign(3) & Sign(-2) & Sign(0))
    If x > 3 Then
        If x = 5 Then Console.WriteLine(""g1"") Else Console.WriteLine(""g2"")
    Else
        Console.WriteLine(""g3"")
    End If
    Console.WriteLine(""end"")
End Sub";

    private const string Expected = "123\n2,1\nbig\nB\nc1\nc2\ne2\ne3\nf2\nposnegzero\ng1\nend";

    private static string Normalize(string s) => s.Replace("\r\n", "\n").TrimEnd('\n');

    [Test]
    public void OnJavaScript() =>
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
