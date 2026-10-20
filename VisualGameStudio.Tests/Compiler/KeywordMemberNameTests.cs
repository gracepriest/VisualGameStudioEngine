using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

[TestFixture]
public class KeywordMemberNameTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void KeywordSpelledMembers_ParseInDeclarationsAndAfterADot(bool external)
    {
        var signatures = external ? "" : "\n Get\n Return False\n End Get\n Set(value As Boolean)\n End Set\nEnd Property";
        var source = $"Public {(external ? "Extern " : "")}Class EditControl\n" +
            $" Public Property ReadOnly As Boolean{signatures}\n" +
            " Public Property type As String\n" +
            " Public Sub select()\n" + (external ? "" : " End Sub\n") + "End Class\n" +
            "Sub Main()\n Dim e As EditControl\n e.ReadOnly = True\n e.type = \"text\"\n e.select()\nEnd Sub\n";
        var parser = new Parser(new Lexer(source).Tokenize());
        var program = parser.Parse();

        Assert.That(parser.Errors, Is.Empty);
        var type = program.Declarations.OfType<ClassNode>().Single();
        Assert.That(type.Members.OfType<PropertyNode>().Select(p => p.Name),
            Is.EquivalentTo(new[] { "ReadOnly", "type" }));
        Assert.That(type.Members.Any(m => m is FunctionNode fn && fn.Name == "select" ||
                                         m is SubroutineNode sub && sub.Name == "select"), Is.True);
    }
}
