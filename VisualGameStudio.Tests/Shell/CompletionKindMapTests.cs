using NUnit.Framework;
using VisualGameStudio.Shell.Views.Documents;
using CoreKind = VisualGameStudio.Core.Abstractions.Services.CompletionItemKind;
using EditorKind = VisualGameStudio.Editor.Completion.CompletionItemKind;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// Pins <see cref="CodeEditorDocumentView.ConvertCompletionKind"/> — the Core→Editor kind map
/// every LSP completion row passes through on its way to the popup's glyph column.
///
/// <para>
/// The regression it exists to prevent: the switch used to map only 14 of the 25 LSP kinds and
/// funnel the rest through <c>_ =&gt; Text</c>, silently demoting EnumMember, Event, Operator,
/// TypeParameter, File, Folder, Reference, Unit, Value and Color rows to the plain-text glyph.
/// Harmless-looking for BasicLang (which rarely sends those kinds) but immediately visible with
/// clangd, whose member lists are full of EnumMember/Operator/TypeParameter items. The
/// exhaustive enumeration below is the contract that kills the silent funnel: ANY kind added to
/// the Core enum without a deliberate mapping decision fails this test by name.
/// </para>
/// </summary>
[TestFixture]
public class CompletionKindMapTests
{
    [Test]
    public void ConvertCompletionKind_MapsAllTwentyFiveKinds()
    {
        var coreKinds = Enum.GetValues<CoreKind>();

        // Premise pin: LSP 3.x defines exactly 25 completion item kinds and both enums mirror
        // the wire values 1..25. If the Core enum grows, the by-name loop below decides what
        // the new kind maps to — this count just makes the growth loud.
        Assert.That(coreKinds, Has.Length.EqualTo(25),
            "the Core CompletionItemKind enum is expected to mirror LSP's 25 kinds");

        Assert.Multiple(() =>
        {
            foreach (var coreKind in coreKinds)
            {
                var editorKind = CodeEditorDocumentView.ConvertCompletionKind(coreKind);

                // The Editor enum mirrors the Core enum name-for-name (both are the LSP set),
                // so the exhaustive contract is name identity — which also proves no kind
                // falls through a default arm to Text except Text itself.
                Assert.That(editorKind.ToString(), Is.EqualTo(coreKind.ToString()),
                    $"Core kind '{coreKind}' must map to the Editor kind of the same name, " +
                    "never funnel through a default arm");

                if (coreKind != CoreKind.Text)
                {
                    Assert.That(editorKind, Is.Not.EqualTo(EditorKind.Text),
                        $"'{coreKind}' demoted to the Text glyph — the silent `_ => Text` funnel is back");
                }
            }
        });
    }
}
