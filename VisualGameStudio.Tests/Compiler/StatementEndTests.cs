using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// After a complete statement the line must end. Nothing checked this, so a trailing token began
/// a SECOND statement on the same line: <c>Dim d As Integer = 5 E3</c> compiled (the stray E3
/// passing as a possible .NET type), which is how <c>5E3</c> silently became 5 before exponents
/// lexed.
/// </summary>
[TestFixture]
public class StatementEndTests
{
    private static string ParseErrors(string source)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        parser.Parse();
        return string.Join("; ", parser.Errors.Select(e => e.Message));
    }

    private static string InMain(string body) => "Sub Main()\n" + body + "\nEnd Sub";

    [TestCase("Dim d As Integer = 5 E3", "'E3'")]
    [TestCase("Dim d As Integer\nd = 5 foo", "'foo'")]
    [TestCase("Console.WriteLine(1) Console.WriteLine(2)", "'Console'")]
    public void ATrailingToken_AfterAStatement_IsRefused(string body, string culprit)
    {
        var errors = ParseErrors(InMain(body));
        Assert.That(errors, Does.Contain("Expected the end of the statement"));
        Assert.That(errors, Does.Contain(culprit));
    }

    [Test]
    public void ATrailingToken_AfterAModuleMember_IsRefusedTheSameWay()
        => Assert.That(ParseErrors("Module M\n    Dim G As Integer = 5 E3\nEnd Module"),
            Does.Contain("Expected the end of the statement, but found 'E3'"));

    [TestCase("Dim x As Integer = 1 ' a comment\nConsole.WriteLine(x) ' another")]
    [TestCase("Dim f = Function(a As Integer) a * 2\nConsole.WriteLine(f(4))")]
    [TestCase("PrintLine \"hi\"")]
    [TestCase("For i As Integer = 1 To 3\n    If i = 2 Then Exit For\n    Console.WriteLine(i)\nNext")]
    [TestCase("Dim s As Integer = 0\nDo While s < 3\n    s += 1\nLoop")]
    [TestCase("Dim a[3] As Integer\nReDim Preserve a[5]")]
    public void OrdinaryLines_StillParse(string body)
        => Assert.That(ParseErrors(InMain(body)), Is.Empty);
}
