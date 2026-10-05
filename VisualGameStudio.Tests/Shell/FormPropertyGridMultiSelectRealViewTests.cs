using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NUnit.Framework;
using VisualGameStudio.Shell.Controls;
using VisualGameStudio.Shell.ViewModels.Designer;
using VisualGameStudio.Tests.Compiler;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// Property-grid slice 6 (pre-flight 2026-10-05): multi-select through the REAL document view, real input, two zooms.
/// </summary>
public partial class FormPropertyGridRealViewTests
{
    /// <summary>One canvas gesture of the one-store sweep: a name, and what it does to the real rig.</summary>
    private sealed record CanvasGesture(string Name, Action<Rig> Do);

    /// <summary>A real press-and-release on a control's centre (offset by <paramref name="dx"/>, so a repeat is not a double-click).</summary>
    private static void PressOn(Rig rig, string id, MouseButton button = MouseButton.Left,
        RawInputModifiers modifiers = RawInputModifiers.None, double dx = 0)
    {
        var at = rig.Canvas.TranslatePoint(rig.CanvasCentreOf(rig.Control(id)) + new Point(dx, 0), rig.Window)!.Value;
        rig.Window.MouseDown(at, button, modifiers);
        rig.Window.MouseUp(at, button, modifiers);
        Dispatcher.UIThread.RunJobs();
        rig.Window.UpdateLayout();
    }

    /// <summary>A point of the FORM, in window coordinates, mapped the way the canvas maps it.</summary>
    private static Point FormPointInWindow(Rig rig, double x, double y)
    {
        var fit = FormCanvasControl.Fit(rig.Doc, rig.Canvas.Bounds.Size);
        return rig.Canvas.TranslatePoint(fit.ToCanvas(new Rect(x, y, 1, 1)).TopLeft, rig.Window)!.Value;
    }

    /// <summary>
    /// ⛔ The gesture list of the one-store invariant (pre-flight Task 2, review m6). A new canvas gesture is ONE row here.
    /// (D-13's promotion row lands with Task 4b and the Delete row with Task 6 — both change those gestures.)
    /// </summary>
    private static IReadOnlyList<CanvasGesture> CanvasGestures() => new CanvasGesture[]
    {
        new("a plain click on btn", r => PressOn(r, "btn")),
        new("a Ctrl+click adding btn2", r => PressOn(r, "btn2", modifiers: RawInputModifiers.Control)),
        new("a Ctrl+click adding lbl", r => PressOn(r, "lbl", modifiers: RawInputModifiers.Control)),
        new("a Ctrl+click REMOVING the primary lbl", r => PressOn(r, "lbl", modifiers: RawInputModifiers.Control, dx: 3)),
        new("a Shift+click adding txt", r => PressOn(r, "txt", modifiers: RawInputModifiers.Shift)),
        new("a click on empty canvas", r =>
        {
            var at = FormPointInWindow(r, 300, 300);
            r.Window.MouseDown(at, MouseButton.Left);
            r.Window.MouseUp(at, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }),
        new("a marquee over btn and btn2", r =>
        {
            var from = FormPointInWindow(r, 4, 4);
            var to = FormPointInWindow(r, 100, 90);
            r.Window.MouseDown(from, MouseButton.Left);
            r.Window.MouseMove(from + new Point(6, 6), RawInputModifiers.LeftMouseButton);
            r.Window.MouseMove(to, RawInputModifiers.LeftMouseButton);
            r.Window.MouseUp(to, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            r.Window.UpdateLayout();
        }),
        new("Esc", r =>
        {
            r.Canvas.Focus();
            r.Window.KeyPress(Key.Escape, RawInputModifiers.None);
            r.Window.KeyRelease(Key.Escape, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
        }),
        new("a right-click on a non-member (txt)", r => PressOn(r, "txt", MouseButton.Right, dx: -3)),
        new("a Ctrl+click adding btn", r => PressOn(r, "btn", modifiers: RawInputModifiers.Control, dx: -3)),
        new("a right-click on a member (btn)", r => PressOn(r, "btn", MouseButton.Right, dx: 3)),
        new("a right-click on a non-member (lbl)", r => PressOn(r, "lbl", MouseButton.Right, dx: -3)),
    };

    /// <summary>
    /// ⛔⛔ The ONE selection store (CLAUDE.md), for a multi-selection: after EVERY canvas gesture the grid's set equals the
    /// store's, element-wise IN ORDER, and the grid's primary is the store's primary and the canvas's — at two zooms. A grid
    /// that showed only the primary, or kept a stale list when the primary left, fails on the row that does it.
    /// </summary>
    [AvaloniaTest]
    public void AfterEveryCanvasGesture_TheGridShowsTheStoresSet_InOrder_AtTwoZooms()
    {
        var multiSeen = 0;
        foreach (var (w, h) in TwoZooms)
        {
            using var rig = Open(w, h, FormPropertyGridMultiSelectTests.MultiDoc);

            foreach (var gesture in CanvasGestures())
            {
                gesture.Do(rig);

                var store = rig.Vm.Selection.Controls.Select(c => c.Id).ToList();
                var grid = rig.GridVm.SelectedControls.Select(c => c.Id).ToList();
                multiSeen += store.Count > 1 ? 1 : 0;
                Assert.Multiple(() =>
                {
                    Assert.That(grid, Is.EqualTo(store), $"{w}x{h} after {gesture.Name}: the grid's set is the store's, in order");
                    Assert.That(rig.GridVm.SelectedControl, Is.SameAs(rig.Vm.Selection.Primary),
                        $"{w}x{h} after {gesture.Name}: the grid's primary is the store's");
                    Assert.That(rig.Canvas.SelectedControl, Is.SameAs(rig.Vm.Selection.Primary),
                        $"{w}x{h} after {gesture.Name}: the canvas's primary is the store's");
                    if (store.Count > 1)
                    {
                        Assert.That(rig.Selector.SelectedItem, Is.Null, $"{w}x{h} after {gesture.Name}: the selector is blank");
                        Assert.That(rig.GridVm.Rows.Any(r => r.Name == "Name"), Is.False,
                            $"{w}x{h} after {gesture.Name}: the rows are the merged rows");
                    }
                });
            }
        }

        Assert.That(multiSeen, Is.GreaterThanOrEqualTo(8), "precondition: the gestures really built multi-selections");
    }

    /// <summary>
    /// D-1 (M3, now pinned): a real Ctrl+click rebuilds the grid ONCE — the view model's handler sets the set and the
    /// canvas's TwoWay echo of the primary is a no-op — and the list shows the merged rows (Location, not Name).
    /// </summary>
    [AvaloniaTest]
    public void ARealCtrlClick_RebuildsTheGridOnce_AndListsTheMergedRows_AtTwoZooms()
    {
        foreach (var (w, h) in TwoZooms)
        {
            using var rig = Open(w, h, FormPropertyGridMultiSelectTests.MultiDoc);
            PressOn(rig, "btn");
            var rebuilds = CountGridRebuilds(rig);

            PressOn(rig, "btn2", modifiers: RawInputModifiers.Control);

            Assert.Multiple(() =>
            {
                Assert.That(rebuilds(), Is.EqualTo(1), $"{w}x{h}: one rebuild per Ctrl+click");
                Assert.That(rig.GridVm.SelectedControls.Select(c => c.Id), Is.EqualTo(new[] { "btn", "btn2" }));
                // The real list virtualises: the row is scrolled into view and its REAL name cell read.
                Assert.That(rig.NameCell(rig.Row("Location")).Text, Is.EqualTo("Location"),
                    $"{w}x{h}: Location is listed (realised in the real list) for two Buttons");
                Assert.That(rig.GridVm.DisplayItems.OfType<FormPropertyRow>().Select(r => r.Name),
                    Has.None.EqualTo("TabIndex").And.None.EqualTo("Name"), $"{w}x{h}: TabIndex and Name are not");
            });
        }
    }
}
