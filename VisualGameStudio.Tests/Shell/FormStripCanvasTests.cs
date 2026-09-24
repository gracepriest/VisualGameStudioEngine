using System.Linq;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using BasicLang.Forms;
using NUnit.Framework;
using VisualGameStudio.Shell.Controls;
using VisualGameStudio.Shell.ViewModels.Designer;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// Task 21 (commit 24d), the canvas half of the Type Here strip editor: pressing a band or dropdown
/// CELL selects the item it belongs to, a double-click on one opens its handler, and a press on the
/// Type Here SLOT asks the host to begin editing rather than merely selecting the strip. Also carries
/// Task 25's per-item render pin's sibling requirement — that <c>TypeHereBounds</c> reads the entry
/// whose HOST matches, never merely the first Type Here entry <c>Layout</c> yields.
///
/// <para>⛔ Step 2 of this task landed only THREE property stubs on <see cref="FormCanvasControl"/>
/// (<c>TypeHereHost</c>, <c>TypeHereBounds</c>, <c>BeginTypeHereCommand</c>) — registered, with no
/// behaviour. Every assertion below that depends on Task 21's actual wiring (the slot press, the
/// dropdown-aware hit test, the bounds computation, the slot highlight) is measured RED against that
/// stub tree; see the accompanying report for which ones already passed and why.</para>
///
/// <para>⛔⛔ A DEFECT IN THE PLAN TEXT: it describes the slot highlight as "Control == TypeHereHost".
/// A slot entry is always <c>(Control: null, Bounds, Role: TypeHere, Host: host)</c> — <c>Control</c>
/// is ALWAYS null on a slot, so that comparison is true only when <c>TypeHereHost</c> is ALSO null:
/// the highlight would show with nothing being edited and vanish the moment something is. The correct
/// field is <see cref="FormLayoutEntry.Host"/>, and <see cref="TypeHereBounds_WithTwoSlotsVisible_ReadsTheEntryMatchingHost_NotMerelyTheFirstOne"/>
/// is written so a `Control == TypeHereHost` implementation — or any implementation that reads the
/// first Type Here entry rather than the one whose Host matches — fails it.</para>
/// </summary>
[TestFixture]
public class FormStripCanvasTests
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

    private sealed class Rig
    {
        public required FormCanvasControl Canvas { get; init; }
        public required Window Window { get; init; }
        public required FormDocument Doc { get; init; }
        public required FormSelection Selection { get; init; }
        public required FormControl MenuStrip { get; init; }
        public required FormControl MnuFile { get; init; }
        public required FormControl MnuOpen { get; init; }
        public required FormControl Sep1 { get; init; }
        public required FormControl MnuExit { get; init; }

        /// <summary>
        /// The CANVAS centre of the <c>Layout</c> entry that places <paramref name="control"/> with
        /// the given <paramref name="role"/> — read AT CALL TIME against <c>Canvas.SelectedControl</c>,
        /// exactly the picture the canvas itself would be rendering right now. Never a hand-computed
        /// rectangle: a drift between this and the real <c>Bands</c> stacking is exactly the defect
        /// class this whole feature exists to prevent.
        /// </summary>
        public Point CentreOfEntry(FormControl control, FormLayoutRole role)
        {
            var fit = FormCanvasControl.Fit(Doc, Canvas.Bounds.Size);
            var entry = FormCanvasTransform.Layout(Doc, Canvas.SelectedControl)
                .Single(e => ReferenceEquals(e.Control, control) && e.Role == role);
            return fit.ToCanvas(entry.Bounds).Center;
        }

        /// <summary>The CANVAS centre of the Type Here slot hosted by <paramref name="host"/>, under the CURRENT selection.</summary>
        public Point CentreOfSlot(FormControl host)
        {
            var fit = FormCanvasControl.Fit(Doc, Canvas.Bounds.Size);
            var entry = FormCanvasTransform.Layout(Doc, Canvas.SelectedControl)
                .Single(e => e.Role == FormLayoutRole.TypeHere && ReferenceEquals(e.Host, host));
            return fit.ToCanvas(entry.Bounds).Center;
        }

        public void Click(Point at)
        {
            Window.MouseDown(at, MouseButton.Left);
            Window.MouseUp(at, MouseButton.Left);
        }

        public void DoubleClick(Point at)
        {
            Click(at);
            Click(at);
        }
    }

    /// <summary>
    /// The rectangle <see cref="FormCanvasControl.TypeHereBounds"/> SHOULD equal once Task 21 lands:
    /// the canvas rect of the Type Here slot hosted by <paramref name="host"/>, under
    /// <paramref name="selected"/> — computed independently of the control under test, through the
    /// SAME <c>Fit</c>/<c>Layout</c> the canvas itself uses.
    /// </summary>
    private static Rect ExpectedSlotBounds(FormDocument doc, Size viewport, FormControl? selected, FormControl host)
    {
        var fit = FormCanvasControl.Fit(doc, viewport);
        var entry = FormCanvasTransform.Layout(doc, selected)
            .Single(e => e.Role == FormLayoutRole.TypeHere && ReferenceEquals(e.Host, host));
        return fit.ToCanvas(entry.Bounds);
    }

    /// <summary>Forces a render pass without needing the frame — <c>TypeHereBounds</c> (Task 21) is written at the END of <c>Render</c>, never eagerly.</summary>
    private static void ForceRender(Window window)
    {
        using var frame = window.CaptureRenderedFrame();
    }

    private static string Hash(Window window)
    {
        using var frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("No rendered frame — Skia is required to render pixels.");
        using var stream = new MemoryStream();
        frame.Save(stream);
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    /// <summary>
    /// The spec §3 shape, verbatim (same fixture <c>FormStripLayoutTests.BuildSpec3Document</c> pins
    /// at the transform level): a MenuStrip with mnuFile → mnuOpen, sep1, mnuExit. Only the MenuStrip
    /// arm is needed here — the canvas gestures under test do not touch the ToolStrip/StatusStrip.
    /// </summary>
    private static Rig Surface()
    {
        var mnuOpen = new FormControl { Kind = "ToolStripMenuItem", Id = "mnuOpen" };
        mnuOpen.Properties["Text"] = "&Open...";
        var sep1 = new FormControl { Kind = "ToolStripSeparator", Id = "sep1" };
        var mnuExit = new FormControl { Kind = "ToolStripMenuItem", Id = "mnuExit" };
        mnuExit.Properties["Text"] = "E&xit";
        var mnuFile = new FormControl { Kind = "ToolStripMenuItem", Id = "mnuFile" };
        mnuFile.Properties["Text"] = "&File";
        mnuFile.Children.Add(mnuOpen);
        mnuFile.Children.Add(sep1);
        mnuFile.Children.Add(mnuExit);

        var menuStrip = new FormControl { Kind = "MenuStrip", Id = "menuStrip1" };
        menuStrip.Children.Add(mnuFile);

        var doc = new FormDocument
        {
            Target = FormTarget.WinForms, Name = "MenuForm", Width = 640, Height = 480
        };
        doc.Controls.Add(menuStrip);

        var selection = new FormSelection();
        var canvas = new FormCanvasControl { Document = doc, Selection = selection };
        var window = new Window { Width = 750, Height = 600, Content = canvas };
        window.Show();
        canvas.Focus();

        return new Rig
        {
            Canvas = canvas, Window = window, Doc = doc, Selection = selection,
            MenuStrip = menuStrip, MnuFile = mnuFile, MnuOpen = mnuOpen, Sep1 = sep1, MnuExit = mnuExit
        };
    }

    // ==================================================================
    // Band cells
    // ==================================================================

    /// <summary>
    /// ⚠ MEASURED ALREADY PASSING against the property-stub tree: a top-level item's cell is laid
    /// out by <c>Bands</c> unconditionally (Task 20, already landed), so selecting it through
    /// <c>OnPointerPressed</c> needs none of Task 21's changes. Kept as a REGRESSION pin for Task
    /// 21's own edit to that method (the Type-Here-slot check and threading <c>SelectedControl</c>
    /// into every <c>HitTest</c> call): a mutant that reorders those checks ahead of the ordinary
    /// cell hit, or that forgets to fall through when the point is NOT a slot, fails this.
    /// </summary>
    [AvaloniaTest]
    public void PressingMnuFilesCell_SelectsIt_AndArmsNoDrag()
    {
        var rig = Surface();
        var commit = new Recorder();
        rig.Canvas.CommitGeometryCommand = commit;

        var at = rig.CentreOfEntry(rig.MnuFile, FormLayoutRole.Cell);
        rig.Window.MouseDown(at, MouseButton.Left);
        rig.Window.MouseMove(at + new Point(20, 0), RawInputModifiers.LeftMouseButton);
        rig.Window.MouseUp(at + new Point(20, 0), MouseButton.Left);

        Assert.Multiple(() =>
        {
            Assert.That(rig.Selection.Primary, Is.SameAs(rig.MnuFile));
            Assert.That(commit.Executions, Is.Zero,
                "a menu item carries no PixelGeometry — pressing its cell must never arm a drag");
        });
    }

    /// <summary>
    /// A press on a NESTED item's cell must resolve through the SAME <c>SelectedControl</c> the
    /// canvas is currently rendering with. Pre-Task-21, <c>OnPointerPressed</c>'s <c>HitTest</c> calls
    /// pass no selection at all, so the dropdown Layout is drawing (mnuFile IS selected, so Render
    /// shows it) is invisible to hit-testing — the press instead misses everything, starts a rubber
    /// band, and the empty-band release on MouseUp CLEARS the selection outright.
    /// </summary>
    [AvaloniaTest]
    public void PressingADropdownCell_WithItsHostSelected_SelectsTheNestedItem_AndArmsNoDrag()
    {
        var rig = Surface();
        var commit = new Recorder();
        rig.Canvas.CommitGeometryCommand = commit;
        rig.Selection.Set(rig.MnuFile); // opens mnuFile's dropdown (mnuOpen, sep1, mnuExit)

        var at = rig.CentreOfEntry(rig.MnuOpen, FormLayoutRole.Cell);
        rig.Window.MouseDown(at, MouseButton.Left);
        rig.Window.MouseMove(at + new Point(20, 0), RawInputModifiers.LeftMouseButton);
        rig.Window.MouseUp(at + new Point(20, 0), MouseButton.Left);

        Assert.Multiple(() =>
        {
            Assert.That(rig.Selection.Primary, Is.SameAs(rig.MnuOpen),
                "a press on a dropdown cell must select the NESTED item, not miss it or clear the selection");
            Assert.That(commit.Executions, Is.Zero,
                "an item carries no PixelGeometry — the press must not arm a drag at all");
        });
    }

    // ==================================================================
    // Double-click
    // ==================================================================

    /// <summary>
    /// VS's "open my handler" gesture, reached through a dropdown cell. Pre-Task-21,
    /// <c>OnCanvasDoubleTapped</c>'s <c>HitTest</c> call also passes no selection, so it can never
    /// find a nested item — the command never fires.
    /// </summary>
    [AvaloniaTest]
    public void DoubleClickingADropdownCell_ExecutesActivateControlCommand_WithIt()
    {
        var rig = Surface();
        var opened = new Recorder();
        rig.Canvas.ActivateControlCommand = opened;
        rig.Selection.Set(rig.MnuFile);

        var at = rig.CentreOfEntry(rig.MnuOpen, FormLayoutRole.Cell);
        rig.DoubleClick(at);

        Assert.Multiple(() =>
        {
            Assert.That(opened.Executions, Is.EqualTo(1));
            Assert.That(opened.LastParameter, Is.SameAs(rig.MnuOpen));
        });
    }

    // ==================================================================
    // The Type Here slot
    // ==================================================================

    /// <summary>
    /// ⛔⛔ The discriminating case Task 21's own plan text calls out: a BAND slot's rectangle is
    /// entirely inside the strip's own band, so if the slot check is placed AFTER the marquee/handle
    /// branch (or omitted), <c>HitTest</c> resolves the point to the STRIP ITSELF and the press is
    /// merely a selection — <see cref="Recorder"/> bound to <c>BeginTypeHereCommand</c> never fires.
    /// Pre-Task-21 the command is not wired into <c>OnPointerPressed</c> at all, so this is red for
    /// exactly that reason today.
    /// </summary>
    [AvaloniaTest]
    public void PressingTheTypeHereSlot_WithTheStripSelected_BeginsTypeHere_WithTheHost()
    {
        var rig = Surface();
        var begin = new Recorder();
        rig.Canvas.BeginTypeHereCommand = begin;
        rig.Selection.Set(rig.MenuStrip);

        var at = rig.CentreOfSlot(rig.MenuStrip);
        rig.Click(at);

        Assert.Multiple(() =>
        {
            Assert.That(begin.Executions, Is.EqualTo(1));
            Assert.That(begin.LastParameter, Is.SameAs(rig.MenuStrip));
        });
    }

    /// <summary>
    /// ⛔⛔ MEASURED: moving the <c>TypeHereAt</c> check in <c>OnPointerPressed</c> to AFTER the
    /// handle/marquee branch survives all 7 pre-existing tests, INCLUDING
    /// <see cref="PressingTheTypeHereSlot_WithTheStripSelected_BeginsTypeHere_WithTheHost"/> whose own
    /// doc comment claims it would die. It does not: a BAND slot's rectangle lies entirely INSIDE its
    /// band, so <c>HitTest</c> resolves the point to the STRIP itself (never null) — the marquee
    /// branch's early <c>return</c> never fires, execution reaches the moved check regardless of where
    /// it sits, and the command still executes either way.
    ///
    /// <para>A DROPDOWN slot is the real discriminator. <c>mnuFile</c>'s own Type Here slot is a
    /// standalone rectangle below its last row (<c>Bands</c>, the dropdown-row loop) with nothing else
    /// painted over it, so <c>HitTest</c> — which only matches entries with a non-null
    /// <c>Control</c> — returns null there. Under the correct ordering the slot check fires and
    /// returns BEFORE that hit test ever runs. Under the mutant, <c>hit == null</c> with no modifier
    /// held takes the marquee branch, which returns EARLY — the moved check, sitting after it, is
    /// never reached, and <c>BeginTypeHereCommand</c> never fires.</para>
    /// </summary>
    [AvaloniaTest]
    public void PressingADropdownsTypeHereSlot_WithItsHostSelected_BeginsTypeHere_WithTheHost()
    {
        var rig = Surface();
        var begin = new Recorder();
        rig.Canvas.BeginTypeHereCommand = begin;
        rig.Selection.Set(rig.MnuFile); // opens mnuFile's dropdown; its slot sits over bare form surface

        var at = rig.CentreOfSlot(rig.MnuFile);
        rig.Click(at);

        Assert.Multiple(() =>
        {
            Assert.That(begin.Executions, Is.EqualTo(1));
            Assert.That(begin.LastParameter, Is.SameAs(rig.MnuFile));
        });
    }

    /// <summary>
    /// <c>TypeHereBounds</c> is an OUTPUT written at the end of <c>Render</c> (Task 21) — never eagerly
    /// on the property changing. Pre-Task-21 it is never written at all, so it reads <c>default</c>.
    /// </summary>
    [AvaloniaTest]
    public void TypeHereBounds_AfterARender_EqualsTheActiveStripsSlot_InCanvasSpace()
    {
        var rig = Surface();
        rig.Selection.Set(rig.MenuStrip);
        rig.Canvas.TypeHereHost = rig.MenuStrip;

        ForceRender(rig.Window);

        var expected = ExpectedSlotBounds(rig.Doc, rig.Canvas.Bounds.Size, rig.MenuStrip, rig.MenuStrip);

        Assert.That(rig.Canvas.TypeHereBounds, Is.EqualTo(expected));
    }

    /// <summary>
    /// Task 25's discrimination requirement, pulled up: with <c>mnuFile</c> selected, TWO slots are
    /// visible at once — <c>menuStrip1</c>'s own band-level slot (still active, since mnuFile is its
    /// item) and <c>mnuFile</c>'s own (now-open) dropdown slot. Setting <c>TypeHereHost = mnuFile</c>
    /// must resolve to the DROPDOWN slot, never to the band-level one — which a "first Type Here
    /// entry" implementation, or the plan's own "<c>Control == TypeHereHost</c>" misreading (Control
    /// is null on every slot), would return instead, because the band-level slot for the active strip
    /// is yielded EARLIER in <c>Layout</c>'s sequence than any dropdown's.
    /// </summary>
    [AvaloniaTest]
    public void TypeHereBounds_WithTwoSlotsVisible_ReadsTheEntryMatchingHost_NotMerelyTheFirstOne()
    {
        var rig = Surface();
        rig.Selection.Set(rig.MnuFile);
        rig.Canvas.TypeHereHost = rig.MnuFile;

        ForceRender(rig.Window);

        var expected = ExpectedSlotBounds(rig.Doc, rig.Canvas.Bounds.Size, rig.MnuFile, rig.MnuFile);
        var bandSlot = ExpectedSlotBounds(rig.Doc, rig.Canvas.Bounds.Size, rig.MnuFile, rig.MenuStrip);

        Assert.Multiple(() =>
        {
            Assert.That(bandSlot, Is.Not.EqualTo(expected),
                "the fixture must offer two DIFFERENT slots, or this test cannot discriminate right from wrong");
            Assert.That(rig.Canvas.TypeHereBounds, Is.EqualTo(expected),
                "must read the slot whose HOST is mnuFile, not menuStrip1's band-level slot");
        });
    }

    // ==================================================================
    // Frame distinctness
    // ==================================================================

    /// <summary>
    /// ⚠ The FIRST half of this pin (selecting mnuFile changes the picture) is ALREADY TRUE on the
    /// property-stub tree — <c>Render</c> already threads <c>SelectedControl</c> into <c>Layout</c>
    /// (Task 20), so the dropdown already paints. The SECOND half (the slot highlights once
    /// <c>TypeHereHost</c> names it) is Task 21's own drawing and is what makes this test red today:
    /// nothing yet reads <c>TypeHereHost</c> in <c>Render</c>, so setting it changes no pixel at all.
    /// </summary>
    [AvaloniaTest]
    public void FramesDiffer_AsSelectionOpensADropdown_AndTypeHereHostHighlightsTheSlot()
    {
        var rig = Surface();
        var blank = Hash(rig.Window);

        rig.Selection.Set(rig.MnuFile);
        var withDropdown = Hash(rig.Window);

        rig.Canvas.TypeHereHost = rig.MenuStrip;
        var withSlotHighlighted = Hash(rig.Window);

        Assert.Multiple(() =>
        {
            Assert.That(withDropdown, Is.Not.EqualTo(blank),
                "selecting mnuFile must open its dropdown on screen");
            Assert.That(withSlotHighlighted, Is.Not.EqualTo(withDropdown),
                "naming a TypeHereHost must change what is drawn — the slot itself must appear/highlight");
        });
    }

    /// <summary>
    /// ⛔⛔ MEASURED: mutating the highlight predicate to the plan's own literal shape
    /// <c>entry.Control == TypeHereHost</c> survives <see cref="FramesDiffer_AsSelectionOpensADropdown_AndTypeHereHostHighlightsTheSlot"/>
    /// and every other test in this fixture. <c>entry.Control</c> is ALWAYS null on a slot entry, so
    /// under that mutant the comparison is <c>ReferenceEquals(null, null)</c> — true exactly when
    /// <see cref="FormCanvasControl.TypeHereHost"/> is ALSO null. The defect is an INVERSION: every
    /// slot highlights when nothing is being edited, and the highlight vanishes the instant a host is
    /// actually named — so naming EITHER host paints the SAME picture (nothing highlighted), and a
    /// test that only compares against the un-hosted frame can never see that the two hosted frames
    /// became identical to EACH OTHER.
    ///
    /// <para>With <c>mnuFile</c> selected, two slots are on screen at once: <c>menuStrip1</c>'s own
    /// band-level slot (still active, since mnuFile is its item) and <c>mnuFile</c>'s own dropdown
    /// slot. Naming each host in turn must highlight ITS OWN slot only, so all three frames — nothing
    /// named, <c>menuStrip1</c> named, <c>mnuFile</c> named — must be pairwise distinct. Under the
    /// inversion, the "nothing named" frame instead has BOTH slots highlighted (wrong on its own), and
    /// both "named" frames have NEITHER highlighted — so they collapse onto each other.</para>
    /// </summary>
    [AvaloniaTest]
    public void FramesDiffer_AcrossWhichSlotIsHighlighted_NeverJustWhetherOneIs()
    {
        var rig = Surface();
        rig.Selection.Set(rig.MnuFile); // menuStrip1's band slot AND mnuFile's dropdown slot are both on screen

        rig.Canvas.TypeHereHost = null;
        var none = Hash(rig.Window);

        rig.Canvas.TypeHereHost = rig.MenuStrip;
        var bandHighlighted = Hash(rig.Window);

        rig.Canvas.TypeHereHost = rig.MnuFile;
        var dropdownHighlighted = Hash(rig.Window);

        Assert.Multiple(() =>
        {
            Assert.That(bandHighlighted, Is.Not.EqualTo(none),
                "naming menuStrip1 must highlight its own band slot, changing the picture");
            Assert.That(dropdownHighlighted, Is.Not.EqualTo(none),
                "naming mnuFile must highlight its own dropdown slot, changing the picture");
            Assert.That(bandHighlighted, Is.Not.EqualTo(dropdownHighlighted),
                "the two named hosts must highlight DIFFERENT slots — under the Control==TypeHereHost " +
                "inversion both hosted frames highlight nothing and become identical to each other, even " +
                "though each still differs from the un-hosted frame (where the inversion highlights EVERY slot)");
        });
    }

    // ==================================================================
    // "Can't edit a menu item once it is entered" (owner's report, 2026-09-23) — the canvas
    // half: a SECOND single click on an already-selected item's cell, or F2 with an item
    // selected, must fire EditItemCommand with that item; a double-click must still open the
    // handler and never leave an edit open. Contract: FormCanvasControl.EditItemCommand (new
    // bindable command, stubbed with NO behaviour wired into OnPointerPressed/OnKeyDown yet) and
    // FormCanvasControl.EditingItem (new styled property, bound to StripEditor.EditTarget,
    // consumed by nothing yet). Every test below is measured red against those stubs.
    // ==================================================================

    /// <summary>Catches: EditItemCommand firing on the FIRST click of an unselected item — VS
    /// only opens an in-place rename on an ALREADY-selected item.</summary>
    [AvaloniaTest]
    public void PressingAnUnselectedItemsCell_SelectsIt_ButDoesNotBeginEditing()
    {
        var rig = Surface();
        var edit = new Recorder();
        rig.Canvas.EditItemCommand = edit;

        var at = rig.CentreOfEntry(rig.MnuFile, FormLayoutRole.Cell);
        rig.Click(at);

        Assert.Multiple(() =>
        {
            Assert.That(rig.Selection.Primary, Is.SameAs(rig.MnuFile));
            Assert.That(edit.Executions, Is.Zero, "the FIRST click only selects — VS's rename gesture needs a SECOND one");
        });
    }

    /// <summary>
    /// ⛔⛔ THE CORE GESTURE. A separate single click — not a double-click — on a cell that is
    /// ALREADY the selection. Pre-implementation this is red because <c>OnPointerPressed</c> has
    /// no "already selected, ordinary click" branch that fires <c>EditItemCommand</c> at all.
    /// </summary>
    [AvaloniaTest]
    public void PressingAnAlreadySelectedItemsCell_ASecondTime_BeginsEditingIt()
    {
        var rig = Surface();
        var edit = new Recorder();
        rig.Canvas.EditItemCommand = edit;
        rig.Selection.Set(rig.MnuFile);

        var at = rig.CentreOfEntry(rig.MnuFile, FormLayoutRole.Cell);
        rig.Click(at);

        Assert.Multiple(() =>
        {
            Assert.That(edit.Executions, Is.EqualTo(1));
            Assert.That(edit.LastParameter, Is.SameAs(rig.MnuFile));
        });
    }

    /// <summary>F2 is the keyboard equivalent of the second click. Catches: OnKeyDown having no
    /// F2 arm at all.</summary>
    [AvaloniaTest]
    public void F2_WithAnItemSelected_BeginsEditingIt()
    {
        var rig = Surface();
        var edit = new Recorder();
        rig.Canvas.EditItemCommand = edit;
        rig.Selection.Set(rig.MnuFile);
        rig.Canvas.SelectedControl = rig.MnuFile;

        rig.Window.KeyPress(Key.F2, RawInputModifiers.None);

        Assert.Multiple(() =>
        {
            Assert.That(edit.Executions, Is.EqualTo(1));
            Assert.That(edit.LastParameter, Is.SameAs(rig.MnuFile));
        });
    }

    /// <summary>A separator has no Text: catches EditItemCommand firing for a kind that cannot
    /// be renamed at all — F2 half.</summary>
    [AvaloniaTest]
    public void F2_WithASeparatorSelected_DoesNothing()
    {
        var rig = Surface();
        var edit = new Recorder();
        rig.Canvas.EditItemCommand = edit;
        rig.Selection.Set(rig.Sep1);
        rig.Canvas.SelectedControl = rig.Sep1;

        rig.Window.KeyPress(Key.F2, RawInputModifiers.None);

        Assert.That(edit.Executions, Is.Zero);
    }

    /// <summary>F2 with nothing selected must not throw and must not fire.</summary>
    [AvaloniaTest]
    public void F2_WithNothingSelected_DoesNothing()
    {
        var rig = Surface();
        var edit = new Recorder();
        rig.Canvas.EditItemCommand = edit;

        Assert.DoesNotThrow(() => rig.Window.KeyPress(Key.F2, RawInputModifiers.None));
        Assert.That(edit.Executions, Is.Zero);
    }

    /// <summary>
    /// A double-click is two presses; the FIRST of the pair lands on an already-selected item
    /// exactly like <see cref="PressingAnAlreadySelectedItemsCell_ASecondTime_BeginsEditingIt"/>'s
    /// precondition. VS's rule: a double-click always opens the handler and never leaves a rename
    /// box open behind it. Catches an implementation that fires EditItemCommand on the first
    /// press of the pair and never closes it once the double-tap gesture completes.
    /// </summary>
    [AvaloniaTest]
    public void DoubleClickingAnAlreadySelectedItem_OpensItsHandler_AndLeavesNoEditItemActive()
    {
        var rig = Surface();
        var edit = new Recorder();
        var opened = new Recorder();
        rig.Canvas.EditItemCommand = edit;
        rig.Canvas.ActivateControlCommand = opened;
        rig.Selection.Set(rig.MnuFile);

        var at = rig.CentreOfEntry(rig.MnuFile, FormLayoutRole.Cell);
        rig.DoubleClick(at);

        Assert.Multiple(() =>
        {
            Assert.That(opened.Executions, Is.EqualTo(1), "the double-click gesture must still open the handler");
            Assert.That(opened.LastParameter, Is.SameAs(rig.MnuFile));
            Assert.That(rig.Canvas.EditingItem, Is.Null,
                "a double-click must leave no rename in progress — EditingItem must not be left set");
        });
    }

    /// <summary>
    /// While editing an item, <c>TypeHereBounds</c> must equal that item's OWN cell — never a
    /// Type Here slot's rectangle, which is what an implementation that just reuses the existing
    /// slot-highlight code path (matching on <c>TypeHereHost</c> alone) would produce.
    /// </summary>
    [AvaloniaTest]
    public void TypeHereBounds_WhileEditingAnItem_EqualsTheItemsOwnCell_NotATypeHereSlot()
    {
        var rig = Surface();
        rig.Selection.Set(rig.MnuFile);
        rig.Canvas.EditingItem = rig.MnuFile;

        ForceRender(rig.Window);

        var expectedCell = rig.CentreOfEntry(rig.MnuFile, FormLayoutRole.Cell);
        var actualCentre = rig.Canvas.TypeHereBounds.Center;

        Assert.That(actualCentre, Is.EqualTo(expectedCell),
            "editing mnuFile must publish ITS OWN cell's bounds, not menuStrip1's Type Here slot");
    }
}
