using BasicLang.Forms;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Events;
using VisualGameStudio.Shell.ViewModels.Designer;
using VisualGameStudio.Shell.ViewModels.Documents;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 20's host commands: align, size, z-order and the clipboard, driven through the commands the
/// canvas and the menus actually invoke.
///
/// <para>⛔⛔ <b><c>FormClipboard</c> is the repo's longest-standing thing with no caller.</b> It was
/// built in Task 4 alongside the model — complete, and covered from a dozen angles by
/// <c>FormDocumentTests</c> — and until this fixture nothing in a shipping build had ever called it.
/// <c>CLAUDE.md</c> lists it by name as still unreachable. These tests exist to make that false, and
/// to keep it false.</para>
/// </summary>
[TestFixture]
public class FormDesignerCommandTests
{
    private const string Dir = "/proj/";

    private sealed class Files
    {
        public readonly Dictionary<string, string> Contents = new(StringComparer.Ordinal);

        public IFileService Service
        {
            get
            {
                var mock = new Mock<IFileService>();

                mock.Setup(f => f.ReadFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .Returns((string p, CancellationToken _) => Task.FromResult(Contents[p]));

                mock.Setup(f => f.FileExistsAsync(It.IsAny<string>()))
                    .Returns((string p) => Task.FromResult(Contents.ContainsKey(p)));

                mock.Setup(f => f.WriteFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .Returns((string p, string text, CancellationToken _) =>
                    {
                        Contents[p] = text;
                        return Task.CompletedTask;
                    });

                return mock.Object;
            }
        }
    }

    /// <summary>A WinForms document with three buttons at known, deliberately unaligned positions.</summary>
    private static CodeEditorDocumentViewModel Open()
    {
        var scaffold = FormScaffolder.Create("LoginForm", FormTarget.WinForms);

        var document = new FormDocument
        {
            Target = FormTarget.WinForms, Name = "LoginForm", Width = 800, Height = 450, Text = "LoginForm"
        };

        foreach (var (id, x, y, w, h) in new[]
                 {
                     ("a", 10, 10, 50, 20),
                     ("b", 100, 60, 80, 40),
                     ("c", 200, 120, 30, 60)
                 })
        {
            document.Controls.Add(new FormControl
            {
                Kind = "Button",
                Id = id,
                Geometry = new PixelGeometry { X = x, Y = y, Width = w, Height = h }
            });
        }

        var files = new Files();
        files.Contents[Dir + scaffold.DocumentFileName] =
            BasicLang.Forms.Serialization.FormDocumentWriter.Create(document);
        files.Contents[Dir + scaffold.CodeFileName] = scaffold.CodeText;

        var vm = new CodeEditorDocumentViewModel(files.Service, new Mock<IEventAggregator>().Object)
        {
            FilePath = Dir + scaffold.DocumentFileName
        };
        vm.Text = files.Contents[vm.FilePath];
        return vm;
    }

    private static FormControl Control(CodeEditorDocumentViewModel vm, string id) =>
        vm.DesignDocument!.AllControls().Single(c => c.Id == id);

    private static PixelGeometry G(FormControl control) => (PixelGeometry)control.Geometry!;

    private static IEnumerable<string> Ids(CodeEditorDocumentViewModel vm) =>
        vm.DesignDocument!.Controls.Select(c => c.Id);

    // ==================================================================
    // Align
    // ==================================================================

    [Test]
    public void ArrangeAlignsTheSelectionToItsPrimary()
    {
        var vm = Open();
        vm.Selection.Set(Control(vm, "a"));
        vm.Selection.Add(Control(vm, "c"));

        vm.ArrangeCommand.Execute(FormArrangeKind.AlignLeft);

        Assert.That(G(Control(vm, "a")).X, Is.EqualTo(200));
    }

    /// <summary>The edit has to reach the document TEXT, or the next save writes the old one back.</summary>
    [Test]
    public void AnAlignReachesTheDocumentText()
    {
        var vm = Open();
        vm.Selection.Set(Control(vm, "a"));
        vm.Selection.Add(Control(vm, "c"));

        vm.ArrangeCommand.Execute(FormArrangeKind.AlignLeft);

        Assert.That(vm.Text, Does.Contain("X=\"200\""));
    }

    [Test]
    public void ArrangeWithASingleSelectionDoesNothing()
    {
        var vm = Open();
        vm.Selection.Set(Control(vm, "a"));
        var before = vm.Text;

        vm.ArrangeCommand.Execute(FormArrangeKind.AlignLeft);

        Assert.That(vm.Text, Is.EqualTo(before));
    }

    [Test]
    public void MakeSameSizeTakesThePrimarysSize()
    {
        var vm = Open();
        vm.Selection.Set(Control(vm, "a"));
        vm.Selection.Add(Control(vm, "c"));

        vm.ArrangeCommand.Execute(FormArrangeKind.SameSize);

        Assert.Multiple(() =>
        {
            Assert.That(G(Control(vm, "a")).Width, Is.EqualTo(30));
            Assert.That(G(Control(vm, "a")).Height, Is.EqualTo(60));
        });
    }

    // ==================================================================
    // Z-order
    // ==================================================================

    [Test]
    public void BringToFrontMovesTheSelectionLast()
    {
        var vm = Open();
        vm.Selection.Set(Control(vm, "a"));

        vm.BringToFrontCommand.Execute(null);

        Assert.That(Ids(vm), Is.EqualTo(new[] { "b", "c", "a" }));
    }

    [Test]
    public void SendToBackMovesTheSelectionFirst()
    {
        var vm = Open();
        vm.Selection.Set(Control(vm, "c"));

        vm.SendToBackCommand.Execute(null);

        Assert.That(Ids(vm), Is.EqualTo(new[] { "c", "a", "b" }));
    }

    /// <summary>
    /// ⚠ A group keeps its own internal layering. Sending two controls to the back one at a time in
    /// forward order reverses them, because each lands at index 0 and pushes the previous one back.
    /// </summary>
    [Test]
    public void SendingAGroupToTheBackKeepsItsInternalOrder()
    {
        var vm = Open();
        vm.Selection.Set(Control(vm, "b"));
        vm.Selection.Add(Control(vm, "c"));

        vm.SendToBackCommand.Execute(null);

        Assert.That(Ids(vm), Is.EqualTo(new[] { "b", "c", "a" }));
    }

    [Test]
    public void AZOrderChangeReachesTheDocumentText()
    {
        var vm = Open();
        vm.Selection.Set(Control(vm, "a"));

        vm.BringToFrontCommand.Execute(null);

        var text = vm.Text;
        Assert.That(
            text.IndexOf("Id=\"a\"", StringComparison.Ordinal),
            Is.GreaterThan(text.IndexOf("Id=\"c\"", StringComparison.Ordinal)),
            "the structure-preserving writer edits elements in place — it must still REORDER them, " +
            "or the z-order the canvas shows is not the one that is saved or built");
    }

    // ==================================================================
    // ⛔⛔ The clipboard — its first production caller, ever
    // ==================================================================

    [Test]
    public void CopyThenPasteAddsACopy()
    {
        var vm = Open();
        vm.Selection.Set(Control(vm, "a"));

        vm.CopyControlsCommand.Execute(null);
        vm.PasteControlsCommand.Execute(null);

        Assert.That(vm.DesignDocument!.Controls, Has.Count.EqualTo(4));
    }

    /// <summary>
    /// ⛔ The renaming is the whole reason the clipboard was built with the model. Two controls
    /// sharing an id produce a duplicate field declaration and a form that will not compile.
    /// </summary>
    [Test]
    public void APastedControlIsRenamedRatherThanCollidingWithTheOriginal()
    {
        var vm = Open();
        vm.Selection.Set(Control(vm, "a"));

        vm.CopyControlsCommand.Execute(null);
        vm.PasteControlsCommand.Execute(null);

        var ids = vm.DesignDocument!.AllControls().Select(c => c.Id).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(ids, Has.Count.EqualTo(ids.Distinct(StringComparer.OrdinalIgnoreCase).Count()),
                "every id in a form must be unique — it becomes a field name");
            Assert.That(ids, Does.Contain("a"), "the original keeps its name");
        });
    }

    /// <summary>⚠ Offset, so a paste is visible rather than hidden exactly under the original.</summary>
    [Test]
    public void APastedControlIsOffsetFromTheOriginal()
    {
        var vm = Open();
        vm.Selection.Set(Control(vm, "a"));

        vm.CopyControlsCommand.Execute(null);
        vm.PasteControlsCommand.Execute(null);

        var pasted = vm.DesignDocument!.Controls.Last();
        Assert.Multiple(() =>
        {
            Assert.That(G(pasted).X, Is.EqualTo(18));
            Assert.That(G(pasted).Y, Is.EqualTo(18));
        });
    }

    [Test]
    public void ThePasteBecomesTheSelection()
    {
        var vm = Open();
        vm.Selection.Set(Control(vm, "a"));

        vm.CopyControlsCommand.Execute(null);
        vm.PasteControlsCommand.Execute(null);

        Assert.That(vm.Selection.Controls.Single(), Is.SameAs(vm.DesignDocument!.Controls.Last()));
    }

    [Test]
    public void CutRemovesTheControlAndPasteBringsItBack()
    {
        var vm = Open();
        vm.Selection.Set(Control(vm, "b"));

        vm.CutControlsCommand.Execute(null);

        Assert.That(Ids(vm), Is.EqualTo(new[] { "a", "c" }));

        vm.PasteControlsCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(vm.DesignDocument!.Controls, Has.Count.EqualTo(3));
            Assert.That(Ids(vm), Does.Contain("b"), "its id is free again, so it keeps its name");
        });
    }

    [Test]
    public void CutClearsTheSelection()
    {
        var vm = Open();
        vm.Selection.Set(Control(vm, "b"));

        vm.CutControlsCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(vm.Selection.IsEmpty, Is.True);
            Assert.That(vm.PropertyGrid.SelectedControl, Is.Null,
                "the grid must not be left editing a control that is no longer in the document");
        });
    }

    [Test]
    public void ACopyReachesTheDocumentTextOnPaste()
    {
        var vm = Open();
        vm.Selection.Set(Control(vm, "a"));

        vm.CopyControlsCommand.Execute(null);
        vm.PasteControlsCommand.Execute(null);

        Assert.That(vm.Text.Split("<Button").Length - 1, Is.EqualTo(4));
    }

    /// <summary>
    /// ⛔ A WinForms subtree pasted into a web form is REFUSED, not silently converted. The two
    /// catalogs differ, and a Button carrying WinForms-only properties on a page would emit markup
    /// the web target cannot honour.
    ///
    /// <para>⚠ Deliberately not a test for "the clipboard is empty": the buffer is static so that
    /// copy in one form pastes into another, which means no test can assume it starts empty.</para>
    /// </summary>
    [Test]
    public void PastingAWinFormsSubtreeIntoAWebFormIsRefused()
    {
        var source = Open();
        source.Selection.Set(Control(source, "a"));
        source.CopyControlsCommand.Execute(null);

        var web = OpenWeb();
        var before = web.Text;

        web.PasteControlsCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(web.DesignDocument!.Controls, Is.Empty);
            Assert.That(web.Text, Is.EqualTo(before));
        });
    }

    private static CodeEditorDocumentViewModel OpenWeb()
    {
        var scaffold = FormScaffolder.Create("WebForm", FormTarget.Web);
        var files = new Files();
        files.Contents[Dir + scaffold.DocumentFileName] = scaffold.DocumentText;
        files.Contents[Dir + scaffold.CodeFileName] = scaffold.CodeText;

        var vm = new CodeEditorDocumentViewModel(files.Service, new Mock<IEventAggregator>().Object)
        {
            FilePath = Dir + scaffold.DocumentFileName
        };
        vm.Text = files.Contents[vm.FilePath];
        return vm;
    }

    /// <summary>
    /// ⛔ A pasted subtree keeps its children, and they are renamed too — a Panel pasted with two
    /// buttons inside it must not produce two controls answering to the originals' names.
    /// </summary>
    [Test]
    public void PastingAContainerCopiesAndRenamesItsChildren()
    {
        var vm = Open();
        var panel = new FormControl
        {
            Kind = "Panel", Id = "pnl",
            Geometry = new PixelGeometry { X = 0, Y = 0, Width = 200, Height = 100 }
        };
        panel.Children.Add(new FormControl
        {
            Kind = "Button", Id = "inner",
            Geometry = new PixelGeometry { X = 5, Y = 5, Width = 40, Height = 20 }
        });
        vm.DesignDocument!.Controls.Add(panel);

        vm.Selection.Set(panel);
        vm.CopyControlsCommand.Execute(null);
        vm.PasteControlsCommand.Execute(null);

        var ids = vm.DesignDocument.AllControls().Select(c => c.Id).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(ids.Count(i => i.StartsWith("inner", StringComparison.OrdinalIgnoreCase)),
                Is.EqualTo(2));
            Assert.That(ids, Has.Count.EqualTo(ids.Distinct(StringComparer.OrdinalIgnoreCase).Count()));
        });
    }
}
