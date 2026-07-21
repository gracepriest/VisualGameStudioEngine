using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Editor;

/// <summary>
/// Source-level guard (headless Avalonia views are unconstructable in this suite —
/// same technique as <c>NewProjectWizardSwapGuardTests</c>): every split-view editor
/// pane must forward the document file path to the inline color-swatch language gate.
/// MainEditor gets the path via SetLanguageService, but split view HIDES MainEditor
/// (IsVisible="{Binding !IsSplitView}") — if the four split editors are not forwarded
/// the path they sit at ColorLanguage.None and every .bas swatch vanishes on split.
/// Deliberately SetColorGateFile, NOT SetLanguageService: the split panes must not
/// gain LSP behavior from a rendering-parity fix.
/// </summary>
[TestFixture]
public class SplitEditorColorGateGuardTests
{
    private static string? FindRepoFile(params string[] relativeParts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    private static string ReadDocumentViewSource()
    {
        var path = FindRepoFile(
            "VisualGameStudio.Shell", "Views", "Documents", "CodeEditorDocumentView.axaml.cs");
        if (path == null)
        {
            Assert.Ignore("CodeEditorDocumentView.axaml.cs not found from the test base directory — skipping color-gate guard.");
            return string.Empty;
        }

        return File.ReadAllText(path);
    }

    [TestCase("TopEditor")]
    [TestCase("BottomEditor")]
    [TestCase("LeftEditor")]
    [TestCase("RightEditor")]
    public void SplitEditor_ReceivesColorGateFilePath(string editorName)
    {
        var src = ReadDocumentViewSource();

        Assert.That(src, Does.Contain($"\"{editorName}\")?.SetColorGateFile(vm.FilePath)"),
            $"{editorName} must forward vm.FilePath to the inline color-swatch language gate; " +
            "otherwise toggling split view (which hides MainEditor) loses all color swatches.");
    }

    [Test]
    public void MainEditor_KeepsLanguageServicePath()
    {
        var src = ReadDocumentViewSource();

        Assert.That(src, Does.Contain("MainEditor.SetLanguageService(vm.LanguageService, vm.FilePath)"),
            "MainEditor must keep receiving the file path through SetLanguageService.");
    }
}
