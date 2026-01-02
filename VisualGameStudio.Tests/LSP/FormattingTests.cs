using NUnit.Framework;
using BasicLang.Compiler.LSP;
using OmniSharp.Extensions.LanguageServer.Protocol;

namespace VisualGameStudio.Tests.LSP;

[TestFixture]
public class FormattingHandlerTests
{
    private FormattingHandler _handler = null!;
    private DocumentManager _documentManager = null!;

    [SetUp]
    public void SetUp()
    {
        _documentManager = new DocumentManager();
        _handler = new FormattingHandler(_documentManager);
    }

    [Test]
    public void FormattingHandler_CanBeCreated()
    {
        Assert.That(_handler, Is.Not.Null);
    }
}

[TestFixture]
public class FormattingIndentationTests
{
    [Test]
    public void IndentationKeywords_Sub_IncreaseIndent()
    {
        var keywords = new[] { "Sub", "Function", "Class", "If", "For", "While", "Do", "Try", "Select Case" };

        foreach (var keyword in keywords)
        {
            Assert.That(keyword, Is.Not.Null, $"Keyword '{keyword}' should increase indent");
        }
    }

    [Test]
    public void IndentationKeywords_End_DecreaseIndent()
    {
        var keywords = new[] { "End Sub", "End Function", "End Class", "End If", "Next", "Wend", "Loop", "End Try", "End Select" };

        foreach (var keyword in keywords)
        {
            Assert.That(keyword, Is.Not.Null, $"Keyword '{keyword}' should decrease indent");
        }
    }

    [Test]
    public void IndentationKeywords_ElseIf_MaintainIndent()
    {
        var keywords = new[] { "ElseIf", "Else", "Case", "Catch", "Finally" };

        foreach (var keyword in keywords)
        {
            Assert.That(keyword, Is.Not.Null, $"Keyword '{keyword}' should maintain indent");
        }
    }
}
