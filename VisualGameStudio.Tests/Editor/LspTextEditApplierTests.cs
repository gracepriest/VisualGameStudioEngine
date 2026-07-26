using AvaloniaEdit.Document;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Editor.Formatting;

namespace VisualGameStudio.Tests.Editor;

/// <summary>
/// Reproduces and locks the fix for the C++ "Enter jumps the caret to just behind the }"
/// bug: on-type / document formatting used to rebuild the whole buffer and
/// TextDocument.Replace(0, TextLength, ...), which remaps an interior caret to the end of
/// the document. LspTextEditApplier applies edits at their real offsets so the caret rides
/// along correctly (modeled exactly as AvaloniaEdit's Caret does — GetNewOffset with
/// AnchorMovementType.Default, see Caret.OnDocumentChanged).
/// </summary>
[TestFixture]
public class LspTextEditApplierTests
{
    // The sample from the bug report; the trailing "\n" gives the empty line 6.
    private const string Sample =
        "int main()\n{\n    std::cout << \"Hello from msvcbbbb!\" << std::endl;\n    return 0;\n}\n";

    private static int TrackCaret(TextDocument doc, int caretOffset, System.Action mutate)
    {
        int tracked = caretOffset;
        void Handler(object? s, DocumentChangeEventArgs e)
            => tracked = e.GetNewOffset(tracked, AnchorMovementType.Default);
        doc.Changed += Handler;
        try { mutate(); }
        finally { doc.Changed -= Handler; }
        return tracked;
    }

    private static int OffsetAt(TextDocument doc, int line1, int col1)
        => doc.GetLineByNumber(line1).Offset + (col1 - 1);

    [Test]
    public void WholeDocumentReplace_LosesCaretPosition_reproducesBug()
    {
        var doc = new TextDocument(Sample);
        var caret = OffsetAt(doc, 3, 5);            // inside the cout line
        var caretLineBefore = doc.GetLineByOffset(caret).LineNumber; // 3
        var reformatted = Sample.Replace("    return 0;", "        return 0;"); // content differs

        var newCaret = TrackCaret(doc, caret, () => doc.Replace(0, doc.TextLength, reformatted));

        TestContext.WriteLine($"caret landed on line {doc.GetLineByOffset(newCaret).LineNumber} of {doc.LineCount}");
        Assert.That(doc.GetLineByOffset(newCaret).LineNumber, Is.Not.EqualTo(caretLineBefore),
            "whole-document replace does NOT preserve the caret line (this is the bug)");
    }

    [Test]
    public void Apply_EditOnDifferentLine_KeepsCaretOnItsLine()
    {
        var doc = new TextDocument(Sample);
        var caret = OffsetAt(doc, 3, 9);
        var edit = new TextEditInfo { StartLine = 4, StartColumn = 1, EndLine = 4, EndColumn = 5, NewText = "        " };

        var newCaret = TrackCaret(doc, caret, () => LspTextEditApplier.Apply(doc, new[] { edit }));

        Assert.That(doc.GetLineByOffset(newCaret).LineNumber, Is.EqualTo(3));
        Assert.That(doc.GetText(doc.GetLineByNumber(4)), Is.EqualTo("        return 0;"));
    }

    [Test]
    public void Apply_ReindentBeforeCaretSameLine_ShiftsCaretByDelta()
    {
        var doc = new TextDocument(Sample);
        var caret = OffsetAt(doc, 4, 9);            // on "    return 0;" past the indent
        // Reindent line 4: replace the leading 4 spaces [1,5) with 8 spaces (+4 chars, before caret).
        var edit = new TextEditInfo { StartLine = 4, StartColumn = 1, EndLine = 4, EndColumn = 5, NewText = "        " };

        var newCaret = TrackCaret(doc, caret, () => LspTextEditApplier.Apply(doc, new[] { edit }));

        Assert.That(doc.GetLineByOffset(newCaret).LineNumber, Is.EqualTo(4), "caret stayed on line 4");
        Assert.That(newCaret, Is.EqualTo(caret + 4), "caret rode along the +4 indent delta");
    }

    [Test]
    public void Apply_MultipleEdits_AllApplied_ReverseOrderSafe()
    {
        var doc = new TextDocument(Sample);
        var edits = new[]
        {
            new TextEditInfo { StartLine = 3, StartColumn = 1, EndLine = 3, EndColumn = 5, NewText = "  " },   // 4->2 spaces
            new TextEditInfo { StartLine = 4, StartColumn = 1, EndLine = 4, EndColumn = 5, NewText = "  " },   // 4->2 spaces
        };

        LspTextEditApplier.Apply(doc, edits);

        Assert.That(doc.GetText(doc.GetLineByNumber(3)), Does.StartWith("  std::cout"));
        Assert.That(doc.GetText(doc.GetLineByNumber(4)), Is.EqualTo("  return 0;"));
    }

    [Test]
    public void Apply_NullOrEmpty_NoOp()
    {
        var doc = new TextDocument(Sample);
        Assert.DoesNotThrow(() => LspTextEditApplier.Apply(doc, System.Array.Empty<TextEditInfo>()));
        Assert.That(doc.Text, Is.EqualTo(Sample));
    }

    // Workspace-edit (rename/refactor) scenario: multiple occurrences across lines, with the
    // caret on one of the renamed lines. ApplyWorkspaceEditAsync used to SetContent the whole
    // buffer here, jumping the caret to the end and clearing undo — this locks caret-safety.
    [Test]
    public void Apply_RenameAcrossLines_AppliesAllOccurrences_AndKeepsCaretOnItsLine()
    {
        var doc = new TextDocument("int foo = 1;\nreturn foo + foo;\n");
        var caret = OffsetAt(doc, 2, 12);   // on line 2, at the '+' (past the first 'foo')
        var edits = new[]
        {
            new TextEditInfo { StartLine = 1, StartColumn = 5, EndLine = 1, EndColumn = 8, NewText = "myField" },   // foo -> myField
            new TextEditInfo { StartLine = 2, StartColumn = 8, EndLine = 2, EndColumn = 11, NewText = "myField" },
            new TextEditInfo { StartLine = 2, StartColumn = 14, EndLine = 2, EndColumn = 17, NewText = "myField" },
        };

        var newCaret = TrackCaret(doc, caret, () => LspTextEditApplier.Apply(doc, edits));

        Assert.That(doc.Text, Is.EqualTo("int myField = 1;\nreturn myField + myField;\n"));
        Assert.That(doc.GetLineByOffset(newCaret).LineNumber, Is.EqualTo(2), "caret stayed on line 2, not the document end");
    }
}
