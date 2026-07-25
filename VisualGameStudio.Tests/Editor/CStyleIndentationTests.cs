using AvaloniaEdit.Document;
using NUnit.Framework;
using VisualGameStudio.Editor.Completion;

namespace VisualGameStudio.Tests.Editor;

/// <summary>
/// C-style (curly-brace) auto-indentation for C/C++ files. Mirrors VS Code's
/// on-enter behavior: open an indented body between { and }, indent one level
/// after a line ending in {, and align a lone } with its opener.
/// </summary>
[TestFixture]
public class CStyleIndentationTests
{
    // ---- GetNewLineIndent: indentation applied to the line created by Enter ----

    [Test]
    public void GetNewLineIndent_AfterOpenBrace_IndentsOneLevel()
    {
        Assert.That(CStyleIndentation.GetNewLineIndent("int main() {", "", 4, false),
            Is.EqualTo("    "));
    }

    [Test]
    public void GetNewLineIndent_AfterOpenBraceAlreadyIndented_IndentsDeeper()
    {
        Assert.That(CStyleIndentation.GetNewLineIndent("    if (x) {", "", 4, false),
            Is.EqualTo("        "));
    }

    [Test]
    public void GetNewLineIndent_AfterPlainStatement_CopiesIndent()
    {
        Assert.That(CStyleIndentation.GetNewLineIndent("    int x = 5;", "", 4, false),
            Is.EqualTo("    "));
    }

    [Test]
    public void GetNewLineIndent_TrailingCommentAfterBrace_StillIndents()
    {
        // "{ // comment" still opens a block.
        Assert.That(CStyleIndentation.GetNewLineIndent("int main() { // go", "", 4, false),
            Is.EqualTo("    "));
    }

    [Test]
    public void GetNewLineIndent_ClosingBraceAlignsWithOpener()
    {
        // The new line is a lone "}"; it should align with the opening line.
        Assert.That(CStyleIndentation.GetNewLineIndent("int main() {", "}", 4, false),
            Is.EqualTo(""));
        Assert.That(CStyleIndentation.GetNewLineIndent("    if (x) {", "}", 4, false),
            Is.EqualTo("    "));
    }

    [Test]
    public void GetNewLineIndent_ClosingBraceAfterStatement_DedentsOneLevel()
    {
        Assert.That(CStyleIndentation.GetNewLineIndent("        y = 1;", "}", 4, false),
            Is.EqualTo("    "));
    }

    [Test]
    public void GetNewLineIndent_Tabs_UsesTabUnits()
    {
        Assert.That(CStyleIndentation.GetNewLineIndent("\tif (x) {", "", 4, true),
            Is.EqualTo("\t\t"));
    }

    // ---- BuildBraceExpansion: Enter pressed with caret between { and } ----

    [Test]
    public void BuildBraceExpansion_EmptyBaseIndent_OpensIndentedBodyWithCaretInside()
    {
        var (text, caret) = CStyleIndentation.BuildBraceExpansion("", "\n", 4, false);
        // Inserted between { and }: newline, indented blank body line, newline, closer indent.
        Assert.That(text, Is.EqualTo("\n    \n"));
        // Caret lands right after the body indent (start of the blank body line).
        Assert.That(caret, Is.EqualTo(5)); // "\n" (1) + "    " (4)
    }

    [Test]
    public void BuildBraceExpansion_NestedIndent_PushesCloserToBaseIndent()
    {
        var (text, caret) = CStyleIndentation.BuildBraceExpansion("    ", "\r\n", 4, false);
        Assert.That(text, Is.EqualTo("\r\n        \r\n    "));
        Assert.That(caret, Is.EqualTo(10)); // "\r\n" (2) + "        " (8)
    }

    // ---- GetDedentedIndent: typing } on an otherwise-blank line ----

    [Test]
    public void GetDedentedIndent_RemovesOneLevel()
    {
        Assert.That(CStyleIndentation.GetDedentedIndent("        ", 4, false), Is.EqualTo("    "));
        Assert.That(CStyleIndentation.GetDedentedIndent("    ", 4, false), Is.EqualTo(""));
    }

    [Test]
    public void GetDedentedIndent_AtColumnZero_StaysEmpty()
    {
        Assert.That(CStyleIndentation.GetDedentedIndent("", 4, false), Is.EqualTo(""));
    }

    [Test]
    public void GetDedentedIndent_Tabs_RemovesOneTab()
    {
        Assert.That(CStyleIndentation.GetDedentedIndent("\t\t", 4, true), Is.EqualTo("\t"));
    }

    // ---- CStyleIndentationStrategy.IndentLine: full document integration ----

    [Test]
    public void Strategy_IndentsLineAfterOpenBrace()
    {
        var doc = new TextDocument("int main() {\nreturn 0;");
        var strategy = new CStyleIndentationStrategy { IndentSize = 4, UseTabs = false };
        strategy.IndentLine(doc, doc.GetLineByNumber(2));
        Assert.That(doc.Text, Is.EqualTo("int main() {\n    return 0;"));
    }

    [Test]
    public void Strategy_AlignsLoneClosingBraceWithOpener()
    {
        var doc = new TextDocument("    if (x) {\n        y;\n}");
        var strategy = new CStyleIndentationStrategy { IndentSize = 4, UseTabs = false };
        strategy.IndentLine(doc, doc.GetLineByNumber(3));
        Assert.That(doc.Text, Is.EqualTo("    if (x) {\n        y;\n    }"));
    }

    [Test]
    public void Strategy_CopiesIndentForPlainLine()
    {
        var doc = new TextDocument("    a();\nb();");
        var strategy = new CStyleIndentationStrategy { IndentSize = 4, UseTabs = false };
        strategy.IndentLine(doc, doc.GetLineByNumber(2));
        Assert.That(doc.Text, Is.EqualTo("    a();\n    b();"));
    }

    // ---- End-to-end brace expansion: the exact reported symptom ----
    // Mirrors CodeEditorControl.TryExpandBraceBlock: caret between { and }, insert the
    // expansion text at the caret, then move the caret forward by the reported offset.

    [Test]
    public void BraceExpansion_EnterBetweenBraces_OpensIndentedBodyAndPlacesCaretInside()
    {
        // "int main() {}" with the caret between the auto-closed braces (offset 12).
        var doc = new TextDocument("int main() {}");
        const int caret = 12;
        var baseIndent = CStyleIndentation.GetLineIndent(
            doc.GetText(doc.GetLineByOffset(caret)));

        var (text, caretInText) = CStyleIndentation.BuildBraceExpansion(baseIndent, "\n", 4, false);
        doc.Insert(caret, text);
        var finalCaret = caret + caretInText;

        // Body line opened, } pushed to its own line aligned with the opener.
        Assert.That(doc.Text, Is.EqualTo("int main() {\n    \n}"));
        // Caret rests at the end of the indented body line (offset 17), NOT on the '}'.
        Assert.That(finalCaret, Is.EqualTo(17));
        Assert.That(doc.GetLineByOffset(finalCaret).LineNumber, Is.EqualTo(2));
    }

    [Test]
    public void BraceExpansion_NestedBraces_KeepsBodyOneLevelDeeper()
    {
        // "    if (x) {}" — caret between braces at offset 12.
        var doc = new TextDocument("    if (x) {}");
        const int caret = 12;
        var baseIndent = CStyleIndentation.GetLineIndent(
            doc.GetText(doc.GetLineByOffset(caret)));

        var (text, caretInText) = CStyleIndentation.BuildBraceExpansion(baseIndent, "\n", 4, false);
        doc.Insert(caret, text);

        Assert.That(doc.Text, Is.EqualTo("    if (x) {\n        \n    }"));
    }
}
