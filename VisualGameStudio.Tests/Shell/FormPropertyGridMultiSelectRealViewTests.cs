using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BasicLang.Forms;
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
    /// <param name="Expect">When given, the store's ids in ORDER after the gesture — so the sweep also pins WHAT the gesture
    /// does, not only that the grid agrees with the store (review of 7159c2dc: a missing promotion kept the two agreeing).</param>
    private sealed record CanvasGesture(string Name, Action<Rig> Do, string[]? Expect = null);

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
    /// (D-13's promotion row landed with Task 4b; the Delete row lands with Task 6, which changes that gesture.)
    /// </summary>
    private static IReadOnlyList<CanvasGesture> CanvasGestures() => new CanvasGesture[]
    {
        new("a plain click on btn", r => PressOn(r, "btn")),
        new("a Ctrl+click adding btn2", r => PressOn(r, "btn2", modifiers: RawInputModifiers.Control)),
        new("a Ctrl+click adding lbl", r => PressOn(r, "lbl", modifiers: RawInputModifiers.Control)),
        new("a Ctrl+click REMOVING the primary lbl", r => PressOn(r, "lbl", modifiers: RawInputModifiers.Control, dx: 3),
            new[] { "btn", "btn2" }),
        new("a plain click on the member btn (D-13: promoted, the group kept)", r => PressOn(r, "btn", dx: 6),
            new[] { "btn2", "btn" }),
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
                    if (gesture.Expect != null)
                    {
                        Assert.That(store, Is.EqualTo(gesture.Expect), $"{w}x{h} after {gesture.Name}: the store's order");
                    }

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

    // ==================================================================
    // Task 4 — multi-select EDITS through the real view (plan "Tests"; spec §8 "Real view"), two sizes, one Ctrl+Z
    // ==================================================================

    /// <summary>A real click on the first id, then a real Ctrl+click on each other one.</summary>
    private static void SelectWithCtrl(Rig rig, params string[] ids)
    {
        PressOn(rig, ids[0]);
        foreach (var id in ids.Skip(1))
        {
            PressOn(rig, id, modifiers: RawInputModifiers.Control);
        }

        Assert.That(rig.Vm.Selection.Controls.Select(c => c.Id), Is.EqualTo(ids), "precondition: the real clicks selected the set");
    }

    /// <summary>Types into a row's real text box (select-all first) and clicks the search box: the LostFocus commit.</summary>
    private static void TypeInto(Rig rig, FormPropertyRow row, string text, double dx = 0)
    {
        var box = rig.EditorBox(row);
        rig.Click(box, dx);
        rig.Window.KeyPress(Key.A, RawInputModifiers.Control);
        rig.Window.KeyRelease(Key.A, RawInputModifiers.Control);
        rig.Window.KeyTextInput(text);
        Dispatcher.UIThread.RunJobs();
        rig.Click(rig.Search, dx);
        rig.Window.UpdateLayout();
    }

    /// <summary>A REAL Ctrl+Z on the design surface — focus there by a real click on empty canvas (which selects nothing).</summary>
    private static void CtrlZOnTheCanvas(Rig rig)
    {
        var at = FormPointInWindow(rig, 300, 300);
        rig.Window.MouseDown(at, MouseButton.Left);
        rig.Window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        rig.Window.KeyPress(Key.Z, RawInputModifiers.Control);
        rig.Window.KeyRelease(Key.Z, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        rig.Window.UpdateLayout();
    }

    private static string FontDoc => FormPropertyGridMultiSelectTests.MultiDoc
        .Replace("""Text="OK" BackColor="Red" ForeColor="Blue"/>""", """Text="OK" BackColor="Red" ForeColor="Blue" Font="Courier New, 12pt"/>""")
        .Replace("""Text="Hello"/>""", """Text="Hello" Font="Arial, 10pt"/>""");

    private static string MixedComboDoc => FormPropertyGridMultiSelectTests.MultiDoc
        .Replace("""Text="OK" BackColor="Red"/>""", """Text="OK" BackColor="Red" Enabled="false" TextAlign="TopRight"/>""");

    private static PixelGeometry Pixel(Rig rig, string id) => (PixelGeometry)rig.Control(id).Geometry!;

    /// <summary>
    /// (a) + (b): two real clicks select two Buttons — the selector is blank, Location is listed and Name is not; typing
    /// <c>Go</c> into the merged Text row writes BOTH, as ONE undo step, and ONE real Ctrl+Z restores the document byte for
    /// byte. At two sizes.
    /// </summary>
    [AvaloniaTest]
    public void TypingIntoTheMergedText_WritesBothButtons_AndOneRealCtrlZRestoresBoth_AtTwoSizes()
    {
        foreach (var (w, h) in TwoZooms)
        {
            using var rig = Open(w, h, FormPropertyGridMultiSelectTests.MultiDoc);
            SelectWithCtrl(rig, "btn", "btn2");
            Assert.Multiple(() =>
            {
                Assert.That(rig.Selector.SelectedItem, Is.Null, $"{w}x{h} (a): the selector is blank");
                Assert.That(rig.NameCell(rig.Row("Location")).Text, Is.EqualTo("Location"), $"{w}x{h} (a): Location listed");
                Assert.That(rig.GridVm.Rows.Any(r => r.Name == "Name"), Is.False, $"{w}x{h} (a): Name not listed");
            });
            var before = rig.Vm.Text;

            TypeInto(rig, rig.Row("Text"), "Go");

            Assert.Multiple(() =>
            {
                Assert.That(rig.Control("btn").Properties["Text"], Is.EqualTo("Go"), $"{w}x{h} (b): btn");
                Assert.That(rig.Control("btn2").Properties["Text"], Is.EqualTo("Go"), $"{w}x{h} (b): btn2");
                Assert.That(rig.Vm.Text, Does.Not.Contain("Text=\"OK\""), $"{w}x{h} (b): the .blform text");
            });

            CtrlZOnTheCanvas(rig);

            Assert.Multiple(() =>
            {
                Assert.That(rig.Vm.Text, Is.EqualTo(before), $"{w}x{h} (b): ONE real Ctrl+Z restores both, byte for byte");
                Assert.That(rig.Vm.TextDocument.UndoStack.CanUndo, Is.False, $"{w}x{h} (b): the edit was ONE step");
            });
        }
    }

    /// <summary>(c): a real pick of False in the merged Enabled drop-down writes both, one undo step. At two sizes.</summary>
    [AvaloniaTest]
    public void PickingFalseInTheMergedEnabled_WritesBoth_AsOneStep_AtTwoSizes()
    {
        foreach (var (w, h) in TwoZooms)
        {
            using var rig = Open(w, h, FormPropertyGridMultiSelectTests.MultiDoc);
            SelectWithCtrl(rig, "btn", "btn2");
            var before = rig.Vm.Text;

            PickInCombo(rig, VisibleCombo(rig, rig.Row("Enabled")), "False");

            Assert.Multiple(() =>
            {
                Assert.That(rig.Control("btn").Properties["Enabled"], Is.EqualTo("false"), $"{w}x{h}: btn");
                Assert.That(rig.Control("btn2").Properties["Enabled"], Is.EqualTo("false"), $"{w}x{h}: btn2");
            });

            CtrlZOnTheCanvas(rig);
            Assert.Multiple(() =>
            {
                Assert.That(rig.Vm.Text, Is.EqualTo(before), $"{w}x{h}: one Ctrl+Z");
                Assert.That(rig.Vm.TextDocument.UndoStack.CanUndo, Is.False, $"{w}x{h}: one step");
            });
        }
    }

    /// <summary>
    /// (d): Ctrl+click a Label too — BackColor (Red, Red, none) shows BLANK, and the colour drop-down's Web → Red,
    /// picked by real clicks, colours all three with ONE edit. At two sizes.
    /// </summary>
    [AvaloniaTest]
    public void TheColourDropDown_OnThreeControls_ColoursAllThree_AtTwoSizes()
    {
        foreach (var (w, h) in TwoZooms)
        {
            using var rig = Open(w, h, FormPropertyGridMultiSelectTests.MultiDoc);
            SelectWithCtrl(rig, "btn", "btn2", "lbl");
            var back = rig.Row("BackColor");
            var edits = 0;
            rig.GridVm.Edited += (_, _) => edits++;
            Assert.That(rig.EditorBox(back).Text, Is.Empty, $"{w}x{h}: Red, Red and none differ: blank");

            var (flyout, tabs) = OpenColorDropDown(rig, back);
            try
            {
                PickTab(rig, tabs, "Web");
                PickNamed(rig, tabs, "Red");
            }
            finally
            {
                flyout.Hide();
                Dispatcher.UIThread.RunJobs();
            }

            Assert.Multiple(() =>
            {
                Assert.That(new[] { "btn", "btn2", "lbl" }.Select(id => rig.Control(id).Properties.GetValueOrDefault("BackColor")),
                    Is.All.EqualTo("Red"), $"{w}x{h}: all three");
                Assert.That(edits, Is.EqualTo(1), $"{w}x{h}: ONE edit");
                Assert.That(rig.EditorBox(back).Text, Is.EqualTo("Red"), $"{w}x{h}: the box shows the shared value now");
            });
        }
    }

    /// <summary>
    /// (e): a mixed Width (75, 75, 100) is a TEXT box showing blank — never the NumericUpDown's false 0. Focus it and click
    /// away: nothing written, no undo step. Type 90: all three are 90. At two sizes.
    /// </summary>
    [AvaloniaTest]
    public void AMixedWidth_IsABlankTextBox_LeavingItWritesNothing_AndNinetySizesAll_AtTwoSizes()
    {
        foreach (var (w, h) in TwoZooms)
        {
            using var rig = Open(w, h, FormPropertyGridMultiSelectTests.MultiDoc);
            SelectWithCtrl(rig, "btn", "btn2", "lbl");
            var width = rig.Row("Width");
            var container = rig.Container(width);
            Assert.Multiple(() =>
            {
                Assert.That(container.GetVisualDescendants().OfType<NumericUpDown>().Any(n => n.IsEffectivelyVisible), Is.False,
                    $"{w}x{h}: no NumericUpDown for a mixed Int");
                Assert.That(rig.EditorBox(width).Text, Is.Empty, $"{w}x{h}: a blank text box");
            });
            var before = rig.Vm.Text;

            rig.Click(rig.EditorBox(width));
            rig.Click(rig.Search);

            Assert.Multiple(() =>
            {
                Assert.That(rig.Vm.Text, Is.EqualTo(before), $"{w}x{h}: focus-and-leave wrote nothing");
                Assert.That(rig.Vm.TextDocument.UndoStack.CanUndo, Is.False, $"{w}x{h}: no undo step");
                Assert.That(new[] { "btn", "btn2", "lbl" }.Select(id => Pixel(rig, id).Width), Is.EqualTo(new[] { 75, 75, 100 }));
            });

            TypeInto(rig, rig.Row("Width"), "90", dx: 6);

            Assert.That(new[] { "btn", "btn2", "lbl" }.Select(id => Pixel(rig, id).Width), Is.All.EqualTo(90), $"{w}x{h}: all 90");
        }
    }

    /// <summary>
    /// (f): the merged Font of two controls with different fonts — expand it, real-click the Bold part's combo and pick
    /// True: each control keeps its OWN family and size. At two sizes.
    /// </summary>
    [AvaloniaTest]
    public void TheMergedBoldPart_KeepsEachControlsOwnFamily_AtTwoSizes()
    {
        foreach (var (w, h) in TwoZooms)
        {
            using var rig = Open(w, h, FontDoc);
            SelectWithCtrl(rig, "btn", "lbl");
            var bold = rig.Row("Bold");

            PickInCombo(rig, VisibleCombo(rig, bold), "True");

            Assert.Multiple(() =>
            {
                Assert.That(rig.Control("btn").Properties["Font"], Is.EqualTo("Courier New, 12pt, style=Bold"), $"{w}x{h}: btn");
                Assert.That(rig.Control("lbl").Properties["Font"], Is.EqualTo("Arial, 10pt, style=Bold"), $"{w}x{h}: lbl");
            });
        }
    }

    /// <summary>
    /// (g) D-9: Arrange (align lefts) while two controls are selected — the merged X shows the new shared value in its REAL
    /// editor without a reselect (and, un-mixed, it is the NumericUpDown again). At two sizes.
    /// </summary>
    [AvaloniaTest]
    public void AlignLefts_WhileSelected_ShowsTheNewSharedXInTheRealEditor_AtTwoSizes()
    {
        foreach (var (w, h) in TwoZooms)
        {
            using var rig = Open(w, h, FormPropertyGridMultiSelectTests.MultiDoc);
            SelectWithCtrl(rig, "btn", "lbl"); // X 16 and 120; primary lbl
            var x = rig.Row("X");
            Assert.That(rig.EditorBox(x).Text, Is.Empty, $"{w}x{h}: precondition: X differs, blank");

            rig.Vm.ArrangeCommand.Execute(FormArrangeKind.AlignLeft);
            Dispatcher.UIThread.RunJobs();
            rig.Window.UpdateLayout();

            var spinner = rig.Container(x).GetVisualDescendants().OfType<NumericUpDown>().SingleOrDefault(n => n.IsEffectivelyVisible);
            Assert.Multiple(() =>
            {
                Assert.That(rig.GridVm.SelectedControls.Count, Is.EqualTo(2), $"{w}x{h}: still both selected");
                Assert.That(spinner, Is.Not.Null, $"{w}x{h}: un-mixed, the NumericUpDown is back");
                Assert.That(spinner?.Value, Is.EqualTo(120m), $"{w}x{h}: it shows the new shared X without a reselect");
            });
        }
    }

    /// <summary>
    /// (h) M4's real half: a real click opening a MIXED Bool combo and a mixed Enum combo, each closed without a pick, then a
    /// click away — nothing written. At two sizes.
    /// </summary>
    [AvaloniaTest]
    public void OpeningMixedCombos_AndClickingAway_WritesNothing_AtTwoSizes()
    {
        foreach (var (w, h) in TwoZooms)
        {
            using var rig = Open(w, h, MixedComboDoc);
            SelectWithCtrl(rig, "btn", "btn2");
            var before = rig.Vm.Text;

            foreach (var name in new[] { "Enabled", "TextAlign" })
            {
                var combo = VisibleCombo(rig, rig.Row(name));
                Assert.That(combo.SelectedIndex, Is.EqualTo(-1), $"{w}x{h} {name}: mixed — nothing selected");
                rig.Click(combo);
                combo = VisibleCombo(rig, rig.Row(name));
                Assert.That(combo.IsDropDownOpen, Is.True, $"{w}x{h} {name}: the real click opened it");
                rig.Window.KeyPress(Key.Escape, RawInputModifiers.None);
                rig.Window.KeyRelease(Key.Escape, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();
                combo.IsDropDownOpen = false;
                rig.Click(rig.Search);
            }

            Assert.Multiple(() =>
            {
                Assert.That(rig.Vm.Text, Is.EqualTo(before), $"{w}x{h}: nothing written");
                Assert.That(rig.Vm.TextDocument.UndoStack.CanUndo, Is.False, $"{w}x{h}: no undo step");
            });
        }
    }

    /// <summary>
    /// (i) D-9 re-entrancy through the editor echo: <c>Bogus</c> typed into the merged (mixed) BackColor is refused — the
    /// REAL TextBox snaps back to blank (the posted two-step echo is not undone by the revision refresh), the pane says why,
    /// nothing is written. At two sizes.
    /// </summary>
    [AvaloniaTest]
    public void ARefusedValueInAMergedRow_SnapsTheRealBoxBackToBlank_AtTwoSizes()
    {
        foreach (var (w, h) in TwoZooms)
        {
            using var rig = Open(w, h, FormPropertyGridMultiSelectTests.MultiDoc);
            SelectWithCtrl(rig, "btn", "lbl");
            var back = rig.Row("BackColor");
            var box = rig.EditorBox(back);
            var before = rig.Vm.Text;

            rig.Click(box);
            rig.Window.KeyTextInput("Bogus");
            Dispatcher.UIThread.RunJobs();
            Assume.That(box.Text, Is.EqualTo("Bogus"), "precondition: the typing landed");
            rig.Click(rig.Search);
            rig.Window.UpdateLayout();

            Assert.Multiple(() =>
            {
                Assert.That(rig.EditorBox(back).Text, Is.Empty, $"{w}x{h}: the real box snapped back to blank");
                Assert.That(rig.Vm.Text, Is.EqualTo(before), $"{w}x{h}: nothing written");
                Assert.That(rig.GridVm.DescriptionBody, Does.Contain("'Bogus'").And.Contain("'btn' (Button)"),
                    $"{w}x{h}: the pane names the refusing members and the value");
            });
        }
    }

    /// <summary>
    /// (j) D-4: the mixed Width's text box has the focus when another route makes the members EQUAL (Arrange "same width",
    /// the editor turns back into the NumericUpDown under the focus); the dying text box's LostFocus must write nothing —
    /// the only undo step is the Arrange's, and the document keeps the arranged widths. At two sizes.
    /// </summary>
    [AvaloniaTest]
    public void AMixedWidthUnMixedUnderTheFocus_ItsDyingTextBoxWritesNothing_AtTwoSizes()
    {
        foreach (var (w, h) in TwoZooms)
        {
            using var rig = Open(w, h, FormPropertyGridMultiSelectTests.MultiDoc);
            SelectWithCtrl(rig, "btn", "lbl"); // 75 vs 100; primary lbl
            var width = rig.Row("Width");
            var box = rig.EditorBox(width);
            rig.Click(box);
            Assume.That(box.IsFocused, Is.True, "precondition: the mixed text box has the focus");
            var before = rig.Vm.Text;
            var edits = 0;
            rig.GridVm.Edited += (_, _) => edits++;

            rig.Vm.ArrangeCommand.Execute(FormArrangeKind.SameWidth);
            Dispatcher.UIThread.RunJobs();
            rig.Window.UpdateLayout();
            var arranged = rig.Vm.Text;
            TestContext.WriteLine($"[{w}x{h} un-mixed] box.Text=\"{box.Text}\" visible={box.IsEffectivelyVisible} focused={box.IsFocused}");

            rig.Click(rig.Search);

            Assert.Multiple(() =>
            {
                Assert.That(edits, Is.Zero, $"{w}x{h}: the grid made no edit");
                Assert.That(rig.Vm.Text, Is.EqualTo(arranged), $"{w}x{h}: the document holds the arranged widths");
                Assert.That(new[] { "btn", "lbl" }.Select(id => Pixel(rig, id).Width), Is.All.EqualTo(100), $"{w}x{h}: both 100");
            });

            CtrlZOnTheCanvas(rig);
            Assert.Multiple(() =>
            {
                Assert.That(rig.Vm.Text, Is.EqualTo(before), $"{w}x{h}: one undo is the Arrange");
                Assert.That(rig.Vm.TextDocument.UndoStack.CanUndo, Is.False, $"{w}x{h}: and it was the only step");
            });
        }
    }

    // ==================================================================
    // Task 5 — the Events tab and the canvas double-click for a multi-selection (D-8), real input, two zooms
    // ==================================================================

    /// <summary>
    /// Ctrl+select two Buttons, click the bolt, real double-click on the empty Click value: the .bas on disk gains ONE
    /// <c>btn2_Click</c> (the primary's name), both Buttons are bound, the cell shows it after a click on the canvas (slice
    /// 5's CRITICAL, re-run in multi), and ONE real Ctrl+Z removes both binds.
    /// </summary>
    [AvaloniaTest]
    public void DoubleClickingTheMergedClickValue_WritesOneStub_BindsBoth_AndOneCtrlZUnbindsBoth_AtTwoZooms()
    {
        foreach (var (w, h) in TwoZooms)
        {
            using var rig = Open(w, h, FormPropertyGridMultiSelectTests.MultiDoc);
            SelectWithCtrl(rig, "btn", "btn2");
            ShowEvents(rig);
            var before = rig.Vm.Text;

            DoubleClick(rig, HandlerCombo(rig, EventRow(rig, "Click")));
            Dispatcher.UIThread.RunJobs();

            var stub = "Private Sub btn2_Click(sender As Object, e As EventArgs)";
            Assert.Multiple(() =>
            {
                Assert.That(rig.Code.Split(stub).Length - 1, Is.EqualTo(1), $"{w}x{h}: ONE stub, after the primary");
                Assert.That(new[] { "btn", "btn2" }.Select(id => rig.Control(id).Binds.SingleOrDefault()?.Handler),
                    Is.All.EqualTo("btn2_Click"), $"{w}x{h}: both bound");
                Assert.That(rig.Vm.Selection.Controls.Select(c => c.Id), Is.EqualTo(new[] { "btn", "btn2" }), $"{w}x{h}: kept");
            });

            // Focus leaves the cell by a real Ctrl-press on a member (keeps the group): the cell still shows the handler.
            PressOn(rig, "btn2", modifiers: RawInputModifiers.Control); // removes btn2…
            PressOn(rig, "btn2", modifiers: RawInputModifiers.Control, dx: 4); // …and adds it back: the same set, focus on the canvas
            Assert.That(EventRow(rig, "Click").Handler, Is.EqualTo("btn2_Click"), $"{w}x{h}: the cell still shows it");

            CtrlZOnTheCanvas(rig);
            Assert.Multiple(() =>
            {
                Assert.That(rig.Vm.Text, Is.EqualTo(before), $"{w}x{h}: ONE Ctrl+Z removes both binds");
                Assert.That(rig.Vm.TextDocument.UndoStack.CanUndo, Is.False, $"{w}x{h}: ONE step");
            });
        }
    }

    /// <summary>
    /// The canvas route (D-8 + D-13): Ctrl+select btn2 and btn, then a REAL double-click on btn2's twin member btn — the
    /// first press promotes it — gives ONE <c>btn_Click</c> bound on BOTH, the selection still {btn2, btn}.
    /// </summary>
    [AvaloniaTest]
    public void ARealDoubleClickOnAMemberOnTheCanvas_WiresBoth_AndKeepsTheGroup_AtTwoZooms()
    {
        foreach (var (w, h) in TwoZooms)
        {
            using var rig = Open(w, h, FormPropertyGridMultiSelectTests.MultiDoc);
            SelectWithCtrl(rig, "btn", "btn2"); // primary btn2
            var at = rig.Canvas.TranslatePoint(rig.CanvasCentreOf(rig.Control("btn")) + new Point(-5, 0), rig.Window)!.Value;

            rig.Window.MouseDown(at, MouseButton.Left);
            rig.Window.MouseUp(at, MouseButton.Left);
            rig.Window.MouseDown(at, MouseButton.Left);
            rig.Window.MouseUp(at, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            rig.Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            Assert.Multiple(() =>
            {
                Assert.That(rig.Code.Split("Private Sub btn_Click(").Length - 1, Is.EqualTo(1), $"{w}x{h}: ONE btn_Click");
                Assert.That(new[] { "btn", "btn2" }.Select(id => rig.Control(id).Binds.SingleOrDefault()?.Handler),
                    Is.All.EqualTo("btn_Click"), $"{w}x{h}: bound on both");
                Assert.That(rig.Vm.Selection.Controls.Select(c => c.Id), Is.EqualTo(new[] { "btn2", "btn" }),
                    $"{w}x{h}: the group kept, btn promoted");
            });
        }
    }

    /// <summary>
    /// Review of 9fe0d153 (4): D-9's refresh re-raises EVERY Events row's Handler on every document revision. A revision
    /// made elsewhere — here an Events-tab bind on ANOTHER row, a designer write — while the user is typing into the Click
    /// row's handler combo (whose Text is a one-way binding) must not wipe the typed text. At two zooms.
    /// </summary>
    [AvaloniaTest]
    public void AnUnrelatedRevision_DoesNotWipeTextTypedIntoAnotherEventsCombo_AtTwoZooms()
    {
        foreach (var (w, h) in TwoZooms)
        {
            using var rig = Open(w, h);
            SelectOnCanvas(rig, "btn");
            ShowEvents(rig);
            AddSubs(rig, "    Private Sub Downs(sender As Object, e As MouseEventArgs)\n    End Sub\n");
            rig.GridVm.CodeBehindText = rig.Code;
            var click = EventRow(rig, "Click");
            var before = rig.Vm.Text;

            rig.Click(HandlerCombo(rig, click).GetVisualDescendants().OfType<TextBox>().First(t => t.IsEffectivelyVisible));
            rig.Window.KeyTextInput("Typ");
            Dispatcher.UIThread.RunJobs();
            Assume.That(HandlerCombo(rig, click).Text, Is.EqualTo("Typ"), "precondition: the typing landed");

            EventRow(rig, "MouseDown").Commit("Downs"); // another row's bind: a designer write, a model revision
            Dispatcher.UIThread.RunJobs();
            rig.Window.UpdateLayout();

            Assert.Multiple(() =>
            {
                Assert.That(rig.Vm.Text, Is.Not.EqualTo(before).And.Contain("Downs"), $"{w}x{h}: precondition: the revision happened");
                Assert.That(HandlerCombo(rig, click).Text, Is.EqualTo("Typ"), $"{w}x{h}: the typed text survives the refresh");
                Assert.That(click.Handler, Is.Empty, $"{w}x{h}: and nothing was committed for Click");
            });
        }
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
