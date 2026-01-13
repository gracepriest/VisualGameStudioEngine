using NUnit.Framework;

namespace VisualGameStudio.Tests.Infrastructure;

/// <summary>
/// Basic infrastructure tests to verify types are loadable
/// </summary>
[TestFixture]
public class InfrastructureTests
{
    [Test]
    public void LspTypes_ShouldBeAccessible()
    {
        // Verify LSP types can be instantiated
        var position = new VisualGameStudio.Core.LSP.Position { Line = 0, Character = 0 };
        var range = new VisualGameStudio.Core.LSP.LspRange { Start = position, End = position };
        var location = new VisualGameStudio.Core.LSP.Location { Uri = "test", Range = range };

        Assert.That(position.Line, Is.EqualTo(0));
        Assert.That(range.Start, Is.Not.Null);
        Assert.That(location.Uri, Is.EqualTo("test"));
    }

    [Test]
    public void DapTypes_ShouldBeAccessible()
    {
        // Verify DAP types can be instantiated
        var frame = new VisualGameStudio.Core.DAP.DapStackFrame { Id = 1, Name = "Test", Line = 1 };
        var breakpoint = new VisualGameStudio.Core.DAP.Breakpoint { Id = 1, Verified = true };
        var variable = new VisualGameStudio.Core.DAP.Variable { Name = "x", Value = "1" };

        Assert.That(frame.Id, Is.EqualTo(1));
        Assert.That(breakpoint.Verified, Is.True);
        Assert.That(variable.Name, Is.EqualTo("x"));
    }

    [Test]
    public void TextMateTypes_ShouldBeAccessible()
    {
        // Verify TextMate types can be instantiated
        var theme = new VisualGameStudio.Core.TextMate.TextMateTheme { Name = "Test" };
        var pattern = new VisualGameStudio.Core.TextMate.TextMatePattern { Name = "test", Match = ".*" };

        Assert.That(theme.Name, Is.EqualTo("Test"));
        Assert.That(pattern.Match, Is.EqualTo(".*"));
    }

    [Test]
    public void SnippetTypes_ShouldBeAccessible()
    {
        // Verify Snippet types can be instantiated
        var snippet = new VisualGameStudio.Core.Snippets.Snippet
        {
            Prefix = "test",
            Body = "test body",
            Description = "Test"
        };

        Assert.That(snippet.Prefix, Is.EqualTo("test"));
        Assert.That(snippet.Body, Is.EqualTo("test body"));
    }
}
