using AvaloniaEdit.Snippets;
using NUnit.Framework;
using VisualGameStudio.Editor.Completion;

namespace VisualGameStudio.Tests.Editor;

/// <summary>
/// Verifies the snippet parser accepts LSP snippet syntax ($0, $N, ${N}, ${N:text})
/// so server completion items with insertTextFormat=Snippet expand with real
/// tab stops instead of inserting raw markers into user code.
/// </summary>
[TestFixture]
public class LspSnippetParsingTests
{
    [Test]
    public void FromInsertText_SplitsLines()
    {
        var def = SnippetDefinition.FromInsertText("Sub ${1:Name}()\n\t$0\nEnd Sub");

        Assert.That(def.BodyLines, Has.Length.EqualTo(3));
        Assert.That(def.BodyLines[0], Is.EqualTo("Sub ${1:Name}()"));
        Assert.That(def.BodyLines[2], Is.EqualTo("End Sub"));
    }

    [Test]
    public void FromInsertText_NormalizesCrLf()
    {
        var def = SnippetDefinition.FromInsertText("a\r\nb");

        Assert.That(def.BodyLines, Is.EqualTo(new[] { "a", "b" }));
    }

    [Test]
    public void BuildSnippet_BareDollarZero_BecomesCaretElement()
    {
        var def = SnippetDefinition.FromInsertText("WriteLine($0)");

        var snippet = def.BuildSnippet();

        Assert.That(snippet.Elements[0], Is.InstanceOf<SnippetTextElement>());
        Assert.That(((SnippetTextElement)snippet.Elements[0]).Text, Is.EqualTo("WriteLine("));
        Assert.That(snippet.Elements[1], Is.InstanceOf<SnippetCaretElement>());
        Assert.That(snippet.Elements[2], Is.InstanceOf<SnippetTextElement>());
        Assert.That(((SnippetTextElement)snippet.Elements[2]).Text, Is.EqualTo(")"));
    }

    [Test]
    public void BuildSnippet_PlaceholderWithDefault_BecomesReplaceableElement()
    {
        var def = SnippetDefinition.FromInsertText("List(Of ${1:Type})");

        var snippet = def.BuildSnippet();

        var replaceable = snippet.Elements.OfType<SnippetReplaceableTextElement>().Single();
        Assert.That(replaceable.Text, Is.EqualTo("Type"));
    }

    [Test]
    public void BuildSnippet_BracedTabStopWithoutColon_IsNotLeftAsLiteralText()
    {
        // ${0} appears throughout the built-in snippet set and ${1} in LSP output;
        // both must parse as tab stops, never remain literal "${0}" text.
        var def = SnippetDefinition.FromInsertText("If ${1} Then\n\t${0}\nEnd If");

        var snippet = def.BuildSnippet();

        Assert.That(snippet.Elements.OfType<SnippetCaretElement>().Count(), Is.EqualTo(1), "${0} must be the caret");
        Assert.That(snippet.Elements.OfType<SnippetReplaceableTextElement>().Count(), Is.EqualTo(1), "${1} must be a tab stop");
        foreach (var textElement in snippet.Elements.OfType<SnippetTextElement>())
        {
            Assert.That(textElement.Text, Does.Not.Contain("$"), "no snippet markers may leak into the inserted text");
        }
    }

    [Test]
    public void BuildSnippet_RepeatedTabStop_LinksSubsequentOccurrences()
    {
        var def = SnippetDefinition.FromInsertText("Property ${1:Name}\n_${1:Name} = value");

        var snippet = def.BuildSnippet();

        var replaceables = snippet.Elements.OfType<SnippetReplaceableTextElement>().ToList();
        var bound = snippet.Elements.OfType<SnippetBoundElement>().ToList();
        Assert.That(replaceables, Has.Count.EqualTo(1));
        Assert.That(bound, Has.Count.EqualTo(1));
        Assert.That(bound[0].TargetElement, Is.SameAs(replaceables[0]));
    }

    [Test]
    public void BuildSnippet_MultiLine_DoesNotAddManualIndent_EditorAutoIndentsOnInsert()
    {
        // AvaloniaEdit's InsertionContext already re-applies the insertion
        // line's leading whitespace after every newline inside a snippet
        // element. BuildSnippet must therefore NOT prepend the current indent
        // itself — doing both double-indented every continuation line of every
        // multi-line snippet committed at a non-zero indentation level.
        var def = SnippetDefinition.FromInsertText("Sub ${1:Name}()\n\t$0\nEnd Sub");

        var snippet = def.BuildSnippet();

        var text = string.Join("", snippet.Elements
            .Select(el => el switch
            {
                // Note: SnippetReplaceableTextElement derives from
                // SnippetTextElement, so it must be matched first.
                SnippetReplaceableTextElement r => r.Text,
                SnippetTextElement t => t.Text,
                _ => ""
            }));

        Assert.That(text, Does.Contain("Sub Name()"));
        Assert.That(text, Does.Contain(Environment.NewLine + "\t"),
            "the body keeps only its own relative indent");
        Assert.That(text, Does.Contain(Environment.NewLine + "End Sub"),
            "continuation lines carry no manual indent — the editor re-adds it on insert");
        Assert.That(text, Does.Not.Contain(Environment.NewLine + " "),
            "no manual indentation may be baked into continuation lines");
    }

    [Test]
    public void BuildSnippet_NoMarkers_ProducesPlainText()
    {
        var def = SnippetDefinition.FromInsertText("Console.ReadLine()");

        var snippet = def.BuildSnippet();

        Assert.That(snippet.Elements, Has.Count.EqualTo(1));
        Assert.That(((SnippetTextElement)snippet.Elements[0]).Text, Is.EqualTo("Console.ReadLine()"));
    }

    [Test]
    public void Expand_BracedTabStopWithoutColon_IsStripped()
    {
        var def = SnippetDefinition.FromInsertText("If ${1} Then ${0} End If");

        var (text, _) = def.Expand("");

        Assert.That(text, Does.Not.Contain("$"));
    }

    [Test]
    public void Expand_BracedCursorMarker_PlacesCursorAtMarker()
    {
        var def = SnippetDefinition.FromInsertText("A${0}B");

        var (text, cursorOffset) = def.Expand("");

        Assert.That(text, Is.EqualTo("AB"));
        Assert.That(cursorOffset, Is.EqualTo(1));
    }
}
