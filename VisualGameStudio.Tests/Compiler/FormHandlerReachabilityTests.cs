using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 22's reachability seam: the gesture is wired all the way from the canvas to the tab well.
///
/// <para>⛔⛔ <b>This repo's signature failure, five times over.</b> <c>RegionWriter</c>,
/// <c>DispatchSource</c>, <c>JavaScriptEmitter.Emit(forms:)</c>, the default project shape and
/// <c>AddNewFormAsync</c> were each complete, unit-tested and unreachable, with a green suite
/// throughout. <c>AddNewFormAsync</c> is the closest relative of this one: its <c>[RelayCommand]</c>
/// was PRESENT but bound to the wrong method because a doc comment sat between the attribute and its
/// declaration, so the toolkit generated a command nothing binds — it compiles either way, and the
/// only symptom is a menu item that cannot exist.</para>
///
/// <para>A <c>[RelayCommand]</c> no view binds is as unreachable as an un-attributed method, so
/// these read the AXAML and the shell's subscriptions as TEXT. There are three links in this chain
/// and breaking any one of them is silent:</para>
/// <list type="number">
///   <item><description>the canvas raises <c>ActivateControlCommand</c> on double-click;</description></item>
///   <item><description>the view binds it to the document's command;</description></item>
///   <item><description>the shell answers the resulting <c>NavigateToFileEvent</c>.</description></item>
/// </list>
/// <para>⚠ Link 3 is the cruel one: without it the handler IS generated and written to disk, and the
/// user sees nothing happen at all.</para>
/// </summary>
[TestFixture]
public class FormHandlerReachabilityTests
{
    private static string RepoFile(params string[] parts)
    {
        var dir = TestContext.CurrentContext.TestDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "VisualGameStudio.Shell")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.That(dir, Is.Not.Null, "could not locate the repository root from the test directory");
        return Path.Combine(new[] { dir! }.Concat(parts).ToArray());
    }

    private static string Read(params string[] parts)
    {
        var path = RepoFile(parts);
        Assert.That(File.Exists(path), Is.True, $"expected '{path}' to exist");
        return File.ReadAllText(path);
    }

    /// <summary>Link 2 — the binding that makes the command exist as far as the user is concerned.</summary>
    [Test]
    public void TheDesignViewBindsTheCanvasActivateCommand()
    {
        var axaml = Read("VisualGameStudio.Shell", "Views", "Documents", "CodeEditorDocumentView.axaml");

        Assert.That(
            axaml.Replace(" ", "").Replace("\r", "").Replace("\n", ""),
            Does.Contain("ActivateControlCommand=\"{BindingActivateControlCommand}\""),
            "the canvas's double-click command is not bound, so the gesture reaches nothing");
    }

    /// <summary>Link 1 — the canvas actually listens for the gesture.</summary>
    [Test]
    public void TheCanvasHandlesDoubleTap()
    {
        var canvas = Read("VisualGameStudio.Shell", "Controls", "FormCanvasControl.cs");

        Assert.Multiple(() =>
        {
            Assert.That(canvas, Does.Contain("Gestures.DoubleTappedEvent"),
                "nothing subscribes the canvas to the double-tap gesture");
            Assert.That(canvas, Does.Contain("ActivateControlCommandProperty"));
        });
    }

    /// <summary>
    /// Link 3 — the shell answers. ⛔ The one whose absence is invisible: the handler still gets
    /// written, so every other test here would pass while the user sees nothing happen.
    /// </summary>
    [Test]
    public void TheShellSubscribesToNavigateToFileRequests()
    {
        var shell = Read("VisualGameStudio.Shell", "ViewModels", "MainWindowViewModel.cs");

        Assert.Multiple(() =>
        {
            Assert.That(shell, Does.Contain("Subscribe<NavigateToFileEvent>"),
                "nothing answers the designer's 'take them to the handler' request");
            Assert.That(shell, Does.Contain("OpenFileAndNavigateAsync"),
                "the subscription must actually open and navigate");
        });
    }
}
