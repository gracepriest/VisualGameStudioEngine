using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// ⛔ C# treats U+0085, U+2028 and U+2029 as LINE TERMINATORS, so one of them raw inside a regular
/// string or char literal is CS1010 ("newline in constant"). BasicLang's own lexer does not — they are
/// ordinary characters in a BasicLang string — so a program BasicLang accepts produced C# csc refused.
/// Found through the form designer (a caption containing U+2028), but the escape is the C# backend's,
/// shared by every C# program. Both the plain and the optimizing path, per CLAUDE.md.
///
/// <para>The characters are built from their code points on purpose: typed raw into this file they
/// would break THIS file's own compile, for the same reason.</para>
/// </summary>
[TestFixture]
public class CSharpLineTerminatorEscapeTests
{
    [TestCase(0x0085)]
    [TestCase(0x2028)]
    [TestCase(0x2029)]
    public void AStringLiteral_EscapesEveryCSharpLineTerminator(int codePoint)
    {
        var terminator = ((char)codePoint).ToString();
        var escape = "\\" + "u" + codePoint.ToString("x4");
        var source = "Sub Main()\n    Dim s As String = \"a" + terminator + "b\"\n    Console.WriteLine(s)\nEnd Sub\n";

        foreach (var generated in new[]
                 {
                     CSharpTestSupport.CompileToCSharp(source), CSharpTestSupport.CompileToCSharpOptimized(source)
                 })
        {
            Assert.Multiple(() =>
            {
                Assert.That(generated, Does.Contain("\"a" + escape + "b\""));
                Assert.That(generated, Does.Not.Contain(terminator), "no raw terminator anywhere");
            });
        }
    }

    [TestCase(0x0085)]
    [TestCase(0x2028)]
    [TestCase(0x2029)]
    public void ACharLiteral_EscapesEveryCSharpLineTerminator(int codePoint)
    {
        var terminator = ((char)codePoint).ToString();
        var escape = "\\" + "u" + codePoint.ToString("x4");
        var source = "Sub Main()\n    Dim c As Char = \"" + terminator + "\"c\n    Console.WriteLine(c)\nEnd Sub\n";

        foreach (var generated in new[]
                 {
                     CSharpTestSupport.CompileToCSharp(source), CSharpTestSupport.CompileToCSharpOptimized(source)
                 })
        {
            Assert.Multiple(() =>
            {
                Assert.That(generated, Does.Contain("'" + escape + "'"));
                Assert.That(generated, Does.Not.Contain(terminator), "no raw terminator anywhere");
            });
        }
    }
}
