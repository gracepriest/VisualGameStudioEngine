using System.Security.Cryptography;
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using BasicLang.Forms;
using NUnit.Framework;
using VisualGameStudio.Shell.Controls;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// Task 25 — the tray strip in the design view: is it reachable, and does a selected component
/// leave the canvas standing.
///
/// <para>⛔⛔ The AXAML assertions are the reachability gate, the way every UI seam on this branch
/// has one: a tray view model nothing binds is as unreachable as a method nothing calls. They parse
/// the view as XML and look at the tray ELEMENT — <c>Focusable="True"</c> is load-bearing, because a
/// <c>KeyBinding</c> fires only while keyboard focus is inside the element and a <c>Border</c> is
/// not focusable by default, so without it the tray's Delete is complete, tested and dead.</para>
///
/// <para>⚠ The headless test proves the CANVAS-focus path: a component as the canvas's selection
/// draws nothing and throws nothing, and Delete reaches the command with it. The tray-focus path is
/// what the AXAML assertions prove exists.</para>
/// </summary>
[TestFixture]
public class FormTrayViewTests
{
    private sealed class Recorder : System.Windows.Input.ICommand
    {
        public object? LastParameter { get; private set; }
        public int Executions { get; private set; }

        public event EventHandler? CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
            LastParameter = parameter;
            Executions++;
        }
    }

    [AvaloniaTest]
    public void ASelectedComponent_DrawsNothing_ThrowsNothing_AndDeletes()
    {
        var doc = new FormDocument { Target = FormTarget.WinForms, Name = "T", Width = 400, Height = 300 };
        doc.Controls.Add(new FormControl
        {
            Kind = "Button", Id = "btn", Geometry = new PixelGeometry { X = 40, Y = 40, Width = 80, Height = 24 }
        });
        var tmr = new FormControl { Kind = "Timer", Id = "tmr" };
        doc.Components.Add(tmr);

        var delete = new Recorder();
        var canvas = new FormCanvasControl { Document = doc, DeleteCommand = delete };
        var window = new Window { Width = 600, Height = 500, Content = canvas };
        window.Show();
        canvas.Focus();

        // ⛔ "Draws nothing" is a PIXEL claim: the frame with the component selected must be the
        // frame with nothing selected. Asserting only that a frame exists let a canvas that drew
        // handles at (0,0) for a component pass (review, 2026-09-19).
        var unselected = Hash(window);
        canvas.SelectedControl = tmr;
        var selected = Hash(window);

        Assert.That(selected, Is.EqualTo(unselected),
            "selecting a component changed the pixels — the canvas drew handles or an outline for a thing with no place");

        window.KeyPress(Key.Delete, RawInputModifiers.None);

        Assert.Multiple(() =>
        {
            Assert.That(delete.Executions, Is.EqualTo(1));
            Assert.That(delete.LastParameter, Is.SameAs(tmr), "Delete reaches the command with the component");
        });
    }

    [Test]
    public void TheDesignView_BindsTheTray_AndItsCommands()
    {
        var axamlPath = FindRepoFile("VisualGameStudio.Shell", "Views", "Documents", "CodeEditorDocumentView.axaml");
        var codeBehindPath = FindRepoFile("VisualGameStudio.Shell", "Views", "Documents", "CodeEditorDocumentView.axaml.cs");
        if (axamlPath == null || codeBehindPath == null)
        {
            Assert.Ignore("CodeEditorDocumentView.axaml(.cs) not found from the test base directory.");
            return;
        }

        var axaml = XDocument.Load(axamlPath);
        var tray = axaml.Descendants()
            .FirstOrDefault(e => (string?)e.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "ComponentTray");

        Assert.That(tray, Is.Not.Null, "the design view has no element named ComponentTray");

        var text = File.ReadAllText(axamlPath);
        var codeBehind = File.ReadAllText(codeBehindPath);

        Assert.Multiple(() =>
        {
            Assert.That((string?)tray!.Attribute("Focusable"), Is.EqualTo("True"),
                "a KeyBinding fires only while focus is INSIDE the element, and a Border is not focusable by default");
            Assert.That((string?)tray.Attribute("IsVisible"), Does.Contain("Tray.IsVisible"));

            var keyBindings = tray.Descendants().Where(e => e.Name.LocalName == "KeyBinding").ToList();
            Assert.That(keyBindings.Any(k =>
                    (string?)k.Attribute("Gesture") == "Delete" &&
                    ((string?)k.Attribute("Command") ?? "").Contains("DeleteControlCommand")),
                Is.True, "the tray's own Delete must reach DeleteControlCommand");
            Assert.That(keyBindings.Any(k =>
                    (string?)k.Attribute("Gesture") == "Delete" &&
                    ((string?)k.Attribute("CommandParameter") ?? "").Contains("PropertyGrid.SelectedControl")),
                Is.True, "…WITH the grid's control: DeleteControl(null) is a silent no-op, and the grid is what follows the selection");

            Assert.That(text, Does.Contain("Tray.Items"), "the tray is bound to the view model's items");
            Assert.That(text, Does.Contain("OnTrayItemPressed").And.Contain("OnTrayItemDoubleTapped"),
                "click selects, double-click opens the handler");

            Assert.That(codeBehind, Does.Contain("OnTrayDrop"), "the tray is a drop target");
            Assert.That(codeBehind, Does.Contain("TrayDropCommand"),
                "a drop on the tray must go through the tray's OWN command — the canvas's would place a Button at (0,0)");
            Assert.That(codeBehind, Does.Contain("ComponentTray.Focus()"),
                "clicking a tray item must move keyboard focus into the tray, or its Delete never fires");
        });
    }

    private static string Hash(Window window)
    {
        using var frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("No rendered frame — Skia is required to render pixels.");
        using var stream = new MemoryStream();
        frame.Save(stream);
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

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
}
