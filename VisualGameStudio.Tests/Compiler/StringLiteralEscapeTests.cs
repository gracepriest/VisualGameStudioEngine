using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.IR.Optimization;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.CodeGen.JavaScript;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// String literals follow VB: a backslash is an ordinary character and the only escape is a
/// doubled quote (plus doubled braces in an interpolated string).
///
/// <para>The lexer used to read C-style escapes, so "a\b" was the 2-char "ab" and
/// "C:\temp\new" held a real tab and newline — on every backend, since the value was already
/// wrong before any backend saw it. A newline is spelled vbCrLf / vbLf instead.</para>
/// </summary>
[TestFixture]
public class StringLiteralEscapeTests
{
    private static string LexString(string literal)
    {
        var tokens = new Lexer("Dim s = " + literal).Tokenize();
        var token = tokens.Single(t => t.Type == TokenType.StringLiteral);
        return (string)token.Value;
    }

    [TestCase(@"""a\b""", @"a\b")]
    [TestCase(@"""C:\temp\new""", @"C:\temp\new")]
    [TestCase(@"""x\\y""", @"x\\y")]
    [TestCase(@"""q""""q""", "q\"q")]
    [TestCase(@"""C:\""", @"C:\")]
    [TestCase(@"""\n\t\r\""""""", "\\n\\t\\r\\\"")]
    public void Backslash_IsAnOrdinaryCharacter(string literal, string expected)
    {
        Assert.That(LexString(literal), Is.EqualTo(expected));
    }

    [Test]
    public void TrailingBackslash_DoesNotSwallowTheClosingQuote()
    {
        var tokens = new Lexer("Dim p = \"C:\\\" & \"x\"").Tokenize();

        var strings = tokens.Where(t => t.Type == TokenType.StringLiteral).Select(t => (string)t.Value);
        Assert.That(strings, Is.EqualTo(new[] { "C:\\", "x" }));
    }

    [Test]
    public void InterpolatedString_UsesVbEscapes()
    {
        var source = "Dim s = $\"p\\{n}\\q \"\"z\"\" {{lit}} {n}}}\"";
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty);

        var interp = FindInterpolated(ast);
        Assert.That(interp, Is.Not.Null);

        var parts = interp!.Parts;
        Assert.That(parts, Has.Count.EqualTo(5));
        Assert.That(parts[0], Is.EqualTo("p\\"));
        Assert.That(parts[1], Is.InstanceOf<IdentifierExpressionNode>());
        Assert.That(parts[2], Is.EqualTo("\\q \"z\" {lit} "));
        Assert.That(parts[3], Is.InstanceOf<IdentifierExpressionNode>());
        Assert.That(parts[4], Is.EqualTo("}"));
    }

    private static InterpolatedStringNode? FindInterpolated(ProgramNode ast)
    {
        foreach (var decl in ast.Declarations)
            if (decl is VariableDeclarationNode v && v.Initializer is InterpolatedStringNode i)
                return i;
        return null;
    }

    private const string PathProgram = @"
Sub Main()
    Dim p As String = ""C:\temp\new""
    Console.WriteLine(p)
End Sub";

    // Through the optimizer: the IR that actually ships (see JsTestSupport.CompileOptimized).
    private static BasicLang.Compiler.IR.IRModule Optimized(string source)
    {
        var module = JsTestSupport.BuildModule(source);
        var pipeline = new OptimizationPipeline();
        pipeline.AddStandardPasses();
        pipeline.Run(module);
        return module;
    }

    private static string CSharp(string source) =>
        new ImprovedCSharpCodeGenerator(new CodeGenOptions { GenerateComments = false })
            .Generate(Optimized(source));

    private static string Cpp(string source) =>
        new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false })
            .Generate(Optimized(source));

    private static string Js(string source) =>
        new JavaScriptCodeGenerator().Generate(Optimized(source));

    [Test]
    public void WindowsPath_ReachesEveryBackend_WithItsBackslashes()
    {
        // The escaped form of the 11-char C:\temp\new; before, this was "C:\temp\new" in
        // the output, i.e. a tab and a newline.
        const string emitted = "\"C:\\\\temp\\\\new\"";
        Assert.That(CSharp(PathProgram), Does.Contain(emitted), "C#");
        Assert.That(Cpp(PathProgram), Does.Contain(emitted), "C++");
        Assert.That(Js(PathProgram), Does.Contain(emitted), "JavaScript");
    }

    private const string NewlineProgram = @"
Sub Main()
    Dim s As String = ""a"" & vbCrLf & ""b"" & vbTab & ""c"" & VBLF
    Console.WriteLine(s)
End Sub";

    [Test]
    public void VbControlConstants_LowerToStringConstants_OnEveryBackend()
    {
        foreach (var (name, output) in new[]
                 {
                     ("C#", CSharp(NewlineProgram)),
                     ("C++", Cpp(NewlineProgram)),
                     ("JavaScript", Js(NewlineProgram)),
                 })
        {
            Assert.That(output, Does.Contain("\\r\\n"), name);
            Assert.That(output, Does.Contain("\\t"), name);
            Assert.That(output, Does.Not.Contain("vbCrLf").And.Not.Contain("vbTab"), name);
        }
    }

    [Test]
    public void VbControlConstant_IsShadowedByAUserDeclaration()
    {
        var source = @"
Sub Main()
    Dim vbTab As String = ""USER""
    Console.WriteLine(vbTab)
End Sub";

        var js = Js(source);
        Assert.That(js, Does.Contain("USER"));
        Assert.That(js, Does.Not.Contain("\\t"));
    }
}
