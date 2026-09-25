using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NUnit.Framework;
using VisualGameStudio.Shell.Controls;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// Task 22 (commit 24d), Step 1: the failing tests for <see cref="FormTypeHereEditor"/>, the overlay
/// control the canvas' Type Here slot activates. Written against a deliberate compile-only stub (no
/// behaviour) — see the class doc comment there for exactly what is absent.
///
/// <para>⛔⛔ BLOCKER 4 (plan PRE-FLIGHT CORRECTIONS): Avalonia's <c>IsVisible</c> defaults TRUE and
/// <c>IsActive</c> defaults FALSE, and a styled property set to the value it already holds raises no
/// change notification — so an <c>OnPropertyChanged</c>-only implementation never runs at startup and
/// an empty focusable <c>TextBox</c> sits permanently over the design surface at (0,0). The plan's own
/// implementation snippet (Step 3, without a constructor assignment) fails
/// <see cref="ConstructedEditor_IsHiddenAndNotHitTestable_BeforeActivation"/> for exactly this reason.</para>
/// </summary>
[TestFixture]
public class FormTypeHereEditorTests
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

    /// <summary>
    /// A command whose <c>CanExecute</c> always refuses, used to pin that the editor's key handler
    /// actually GUARDS with <c>CanExecute</c> rather than calling <c>Execute</c> unconditionally — a
    /// CommunityToolkit <c>RelayCommand</c> would run its body even though the view model just
    /// refused, since <c>ICommand.Execute</c> does not re-check on its own.
    /// </summary>
    private sealed class RefusingRecorder : System.Windows.Input.ICommand
    {
        public int Executions { get; private set; }

        public event EventHandler? CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object? parameter) => false;

        public void Execute(object? parameter) => Executions++;
    }

    /// <summary>
    /// Hosts the editor plus a plain, unrelated <see cref="Button"/> a test can steal focus with —
    /// needed by <see cref="ChangingHost_WhileActive_RefocusesTheBox_OnlyAfterFocusHasMovedAway"/>,
    /// which must move focus AWAY from the box before it can tell "re-focuses on a Host change" apart
    /// from "was never focused to begin with".
    /// </summary>
    private static (FormTypeHereEditor Editor, Window Window, Button Other) Surface()
    {
        var editor = new FormTypeHereEditor();
        var other = new Button { Content = "other", Width = 60, Height = 20 };

        var root = new Canvas { Width = 400, Height = 300 };
        root.Children.Add(editor);
        root.Children.Add(other);
        Canvas.SetLeft(other, 250);
        Canvas.SetTop(other, 250);

        var window = new Window { Width = 500, Height = 400, Content = root };
        window.Show();

        return (editor, window, other);
    }

    /// <summary>
    /// The editor is declared as "a Canvas with one TextBox child" (plan Task 22 Step 3) and the stub
    /// adds exactly that one child in its constructor — so the inner box is reached the same way a
    /// real caller who does not have a private field would reach it.
    /// </summary>
    private static TextBox Box(FormTypeHereEditor editor) => (TextBox)editor.Children[0];

    // ==================================================================
    // A — BLOCKER 4: the overlay must NOT be born visible
    // ==================================================================

    [AvaloniaTest]
    public void ConstructedEditor_IsHiddenAndNotHitTestable_BeforeActivation()
    {
        var editor = new FormTypeHereEditor();

        Assert.Multiple(() =>
        {
            Assert.That(editor.IsVisible, Is.False,
                "Avalonia's IsVisible defaults to TRUE; a freshly constructed editor must hide " +
                "itself explicitly (in the CONSTRUCTOR, not only in a PropertyChanged handler that " +
                "never runs when IsActive is set to the value it already holds) or it sits over the " +
                "whole design surface at (0,0) — BLOCKER 4");
            Assert.That(editor.IsHitTestVisible, Is.False,
                "a hit-testable empty overlay at (0,0) swallows every canvas click before the editor " +
                "has ever been used");
            Assert.That(editor.Background, Is.Null,
                "a Background on the overlay would swallow every click on the canvas beneath it — " +
                "FormCanvasControl fills its own viewport with Brushes.Transparent for exactly the " +
                "opposite reason (FormCanvasControl.cs, the comment above the Render override), which " +
                "is why an implementer reaching for a Background here is the natural mistake");
        });
    }

    /// <summary>
    /// ⚠ Both assertions below happen to hold against the STUB already, because Avalonia's OWN
    /// defaults for <c>IsVisible</c>/<c>IsHitTestVisible</c> are already true — setting
    /// <c>IsActive = true</c> changes nothing today, and nothing needed to. This is NOT a tautology:
    /// once BLOCKER 4's fix lands (the constructor hides the editor), this becomes the real regression
    /// guard that activation flips it back — a fix that only hid the editor and never un-hid it on
    /// activation would fail exactly this test. See the report for why it is reported as a stub-time
    /// pass rather than rewritten.
    /// </summary>
    [AvaloniaTest]
    public void ActivatingTheEditor_MakesItVisibleAndHitTestable()
    {
        var editor = new FormTypeHereEditor();

        editor.IsActive = true;

        Assert.Multiple(() =>
        {
            Assert.That(editor.IsVisible, Is.True);
            Assert.That(editor.IsHitTestVisible, Is.True);
        });
    }

    // ==================================================================
    // B — layout against the FluentTheme clamp
    // ==================================================================

    /// <summary>
    /// ⛔ The test that catches the FluentTheme clamp: its TextBox theme sets MinHeight 32 / MinWidth
    /// 64, so without the constructor neutralising them a 22px-tall slot renders 32px tall and the
    /// overlay overhangs by 10px. Asserts the FULL rect, not just height — a size-agnostic assertion
    /// cannot see the clamp at all, and a width-only or height-only one can miss half of it.
    /// </summary>
    [AvaloniaTest]
    public void ActiveEditor_PositionsTheBoxExactlyAtSlotBounds_DespiteTheFluentThemeMinSizeClamp()
    {
        var (editor, window, _) = Surface();
        var slot = new Rect(30, 40, 120, 22);

        editor.SlotBounds = slot;
        editor.IsActive = true;
        window.UpdateLayout();

        Assert.That(Box(editor).Bounds, Is.EqualTo(slot),
            "FluentTheme's TextBox theme sets MinHeight 32 / MinWidth 64 (DesignerHeadlessApp and " +
            "App.axaml both load FluentTheme); the constructor must neutralise MinHeight/MinWidth/" +
            "Padding or a 22px slot renders 32px tall and the overlay overhangs the slot");
    }

    // ==================================================================
    // C — focus
    // ==================================================================

    /// <summary>The control POSTS the focus call to the dispatcher — RunJobs must be pumped before this is true.</summary>
    [AvaloniaTest]
    public void ActivatingTheEditor_FocusesTheInnerBox()
    {
        var (editor, window, _) = Surface();

        editor.IsActive = true;
        Dispatcher.UIThread.RunJobs();

        Assert.That(Box(editor).IsFocused, Is.True);
    }

    // ==================================================================
    // D — re-focus on EVERY Begin, tested the only way it can be seen
    // ==================================================================

    /// <summary>
    /// ⛔ Moving focus AWAY first is what makes the final assertion meaningful. Without that step, the
    /// "focused again" assertion would pass whether or not a Host change re-focuses anything — the box
    /// would simply have never lost focus (Task 25's mutant, "the editor not re-focusing on Host
    /// change", would not be a kill).
    /// </summary>
    [AvaloniaTest]
    public void ChangingHost_WhileActive_RefocusesTheBox_OnlyAfterFocusHasMovedAway()
    {
        var (editor, window, other) = Surface();
        var hostA = new object();
        var hostB = new object();

        editor.Host = hostA;
        editor.IsActive = true;
        Dispatcher.UIThread.RunJobs();

        other.Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.That(Box(editor).IsFocused, Is.False,
            "precondition for the real check below: focus must actually have left the box");

        editor.Host = hostB;
        Dispatcher.UIThread.RunJobs();

        Assert.That(Box(editor).IsFocused, Is.True,
            "a Begin on a NEW host (Host changes while IsActive stays true) must re-focus the box, or " +
            "'type a whole menu in one run' silently stops working after the first item");
    }

    // ==================================================================
    // E — keys
    // ==================================================================

    [AvaloniaTest]
    public void EnterCommitsTheTypedText_ClearsTheBox_AndHandlesTheKey()
    {
        var (editor, window, _) = Surface();
        var commit = new Recorder();
        editor.CommitCommand = commit;
        editor.IsActive = true;
        Dispatcher.UIThread.RunJobs();

        KeyEventArgs? seen = null;
        window.AddHandler(InputElement.KeyDownEvent, (_, e) => seen = e,
            RoutingStrategies.Bubble, handledEventsToo: true);

        window.KeyTextInput("Open");
        var typedIntoBox = Box(editor).Text;
        window.KeyPress(Key.Enter, RawInputModifiers.None);
        var boxTextAfterCommit = Box(editor).Text;

        Assert.Multiple(() =>
        {
            Assert.That(typedIntoBox, Is.EqualTo("Open"),
                "the box must be focused for typed text to reach it at all — see " +
                "ActivatingTheEditor_FocusesTheInnerBox");
            Assert.That(commit.Executions, Is.EqualTo(1));
            Assert.That(commit.LastParameter, Is.EqualTo("Open"));
            Assert.That(boxTextAfterCommit, Is.Null.Or.Empty,
                "cleared after a commit, ready for the next item typed in the same run");
            Assert.That(seen?.Handled, Is.True,
                "Enter must be marked handled or it falls through past the editor");
        });
    }

    [AvaloniaTest]
    public void EscapeCancels()
    {
        var (editor, window, _) = Surface();
        var cancel = new Recorder();
        editor.CancelCommand = cancel;
        editor.IsActive = true;
        Dispatcher.UIThread.RunJobs();

        window.KeyPress(Key.Escape, RawInputModifiers.None);

        Assert.That(cancel.Executions, Is.EqualTo(1));
    }

    // ==================================================================
    // F — deactivation
    // ==================================================================

    [AvaloniaTest]
    public void DeactivatingTheEditor_HidesItAndMakesItNotHitTestable()
    {
        var (editor, window, _) = Surface();
        editor.IsActive = true;

        editor.IsActive = false;

        Assert.Multiple(() =>
        {
            Assert.That(editor.IsVisible, Is.False);
            Assert.That(editor.IsHitTestVisible, Is.False);
        });
    }

    // ==================================================================
    // G — behaviours that WORK today but that no test held (Task 22 pin pass)
    // ==================================================================

    /// <summary>
    /// ⛔ Pins that the constructor's MinHeight/MinWidth/Padding assignments are LOCAL VALUES, which
    /// is the claim that makes the headless green transferable to the real IDE at all. The Shell
    /// (App.axaml) loads FluentTheme AND a global <c>Style Selector="TextBox"</c>
    /// (AppStyles.axaml, Padding="8,4"); <see cref="DesignerHeadlessApp"/> loads FluentTheme alone.
    /// A window-level Style here stands in for AppStyles and is made DELIBERATELY HOSTILE — its
    /// MinHeight/MinWidth are even larger than FluentTheme's own clamp — so if someone later "tidies"
    /// the constructor's local assignments into a Style, this fails while
    /// <see cref="ActiveEditor_PositionsTheBoxExactlyAtSlotBounds_DespiteTheFluentThemeMinSizeClamp"/>
    /// might still happen to pass (a Style added AFTER AppStyles in resolution order could still lose
    /// in the IDE while it wins here) — this test targets the MECHANISM (local value beats any
    /// style), not just one observed outcome of it.
    /// </summary>
    [AvaloniaTest]
    public void LocalConstructorValues_BeatAHostileWindowLevelTextBoxStyle()
    {
        var editor = new FormTypeHereEditor();
        var slot = new Rect(30, 40, 120, 22);

        var window = new Window
        {
            Width = 500,
            Height = 400,
            Content = editor,
            Styles =
            {
                new Style(x => x.OfType<TextBox>())
                {
                    Setters =
                    {
                        new Setter(TextBox.PaddingProperty, new Thickness(8, 4)),
                        new Setter(TextBox.MinHeightProperty, 40d),
                        new Setter(TextBox.MinWidthProperty, 200d),
                    }
                }
            }
        };
        window.Show();

        editor.SlotBounds = slot;
        editor.IsActive = true;
        window.UpdateLayout();

        // ⛔ Expressed through the SAME shared constant the constructor itself reads
        // (FormCanvasTransform.MenuItemCaptionInset), never a hard-coded (2,0): the constructor sets
        // Padding to (MenuItemCaptionInset - border, 0, 2, 0) so the overlay's typed text starts
        // exactly where the committed MenuItem caption will draw — a literal (2,0) here would silently
        // stop discriminating the day either number changes. `border` mirrors the constructor's own
        // local BorderThickness (also a local value, for the same anti-drift reason).
        const double border = 1;
        var expectedPadding = new Thickness(FormCanvasTransform.MenuItemCaptionInset - border, 0, 2, 0);

        Assert.Multiple(() =>
        {
            Assert.That(Box(editor).Bounds, Is.EqualTo(slot),
                "a window-level TextBox style more hostile than FluentTheme's own clamp must still " +
                "lose to the constructor's local MinHeight/MinWidth values");
            Assert.That(Box(editor).Padding, Is.EqualTo(expectedPadding),
                "the constructor's local Padding must beat a window-level style too, or the text's " +
                "left inset drifts from the committed MenuItem caption's own inset");
        });
    }

    /// <summary>
    /// ⛔ Pins the EFFECT of the no-Background design (Blocker 4 / the class doc comment): while
    /// active and STRETCHED across a Grid cell shared with another control, a press outside the
    /// TextBox must still reach the control beneath, and a press ON the TextBox must not. The
    /// existing "Background is null" assertion in
    /// <see cref="ConstructedEditor_IsHiddenAndNotHitTestable_BeforeActivation"/> pins the CAUSE only
    /// — a different cause (e.g. a hit-test override, or a fully transparent but still-hit-testable
    /// Fill) could satisfy that assertion and still swallow clicks. This is Task 24's actual shape:
    /// the editor shares a Grid cell with the canvas and paints over it.
    /// </summary>
    [AvaloniaTest]
    public void WhileActiveAndStretchedOverAGridCell_OnlyTheBoxItselfIsHitTestable()
    {
        var editor = new FormTypeHereEditor();
        editor.SlotBounds = new Rect(30, 40, 120, 22);
        editor.IsActive = true;

        var recorder = new Border { Background = Brushes.Gray };

        var grid = new Grid();
        grid.Children.Add(recorder);
        grid.Children.Add(editor); // added second: on top in Z-order, same as the canvas/overlay pairing

        var window = new Window { Width = 250, Height = 250, Content = grid };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        // ⚠ Measured: other [AvaloniaTest] methods in this fixture activate a FormTypeHereEditor,
        // which focuses its box via a POSTED dispatcher continuation (production code, see the
        // constructor's comment on why it must be posted) — and leave their window open (the
        // headless platform never tears one down between tests). A pending posted focus continuation
        // drained by RunJobs, from an EARLIER test, leaves IInputElement.InputHitTest spuriously
        // returning null for a brand-new Window until a render frame is captured on it. Rendering a
        // frame here (twice, defensively) clears it regardless of what ran before this test.
        using (window.CaptureRenderedFrame()) { }
        Dispatcher.UIThread.RunJobs();
        using (window.CaptureRenderedFrame()) { }
        Dispatcher.UIThread.RunJobs();

        // window.MouseDown's pixel-based hit test does not reliably resolve a nested child under a
        // full-size sibling in this headless environment (measured: it resolves to the Window's own
        // template chrome instead), so this asserts through IInputElement.InputHitTest — the same
        // API Avalonia's real input pipeline resolves a pointer target through — rather than
        // simulating a full routed event.
        var outsideHit = ((IInputElement)window).InputHitTest(new Point(150, 150));
        Assert.That(outsideHit, Is.SameAs(recorder),
            "a press outside the TextBox but inside the editor's (stretched) Canvas must resolve to " +
            "the control beneath — the editor has no Background to catch it");

        // The raw hit is a TEMPLATE PART inside the TextBox (its ScrollContentPresenter, not the
        // TextBox control itself) — real pointer routing still bubbles up through it to the TextBox,
        // so what matters is that the hit is somewhere INSIDE the box's own subtree, never recorder's.
        var insideHit = ((IInputElement)window).InputHitTest(new Point(60, 50));
        Assert.That(insideHit, Is.InstanceOf<Visual>().And.Not.Null);
        var insideHitAncestry = ((Visual)insideHit!).GetSelfAndVisualAncestors().ToList();
        Assert.Multiple(() =>
        {
            Assert.That(insideHitAncestry, Does.Contain(Box(editor)),
                "a press ON the box must resolve inside the box's own visual subtree");
            Assert.That(insideHitAncestry, Does.Not.Contain(recorder),
                "a press ON the box must not resolve to the control beneath it");
        });
    }

    /// <summary>Pins the two-way <see cref="FormTypeHereEditor.Text"/> binding both directions.</summary>
    [AvaloniaTest]
    public void TextProperty_FlowsBothWaysWithTheInnerBox()
    {
        var (editor, window, _) = Surface();

        editor.Text = "Preset";
        Assert.That(Box(editor).Text, Is.EqualTo("Preset"),
            "setting Text on the editor must flow into the box (Task 24's view model presets it)");

        Box(editor).Text = "Typed";
        Assert.That(editor.Text, Is.EqualTo("Typed"),
            "typing into the box must flow back out through Text (Task 23's view model reads it)");
    }

    /// <summary>
    /// Pins that a REFUSED commit (<c>CanExecute</c> false) leaves the typed text in place and still
    /// marks Enter handled — <see cref="EnterCommitsTheTypedText_ClearsTheBox_AndHandlesTheKey"/>
    /// only ever exercises a command that accepts.
    /// </summary>
    [AvaloniaTest]
    public void EnterOnARefusedCommit_LeavesTheTypedTextInPlace_AndStillHandlesTheKey()
    {
        var (editor, window, _) = Surface();
        var commit = new RefusingRecorder();
        editor.CommitCommand = commit;
        editor.IsActive = true;
        Dispatcher.UIThread.RunJobs();

        KeyEventArgs? seen = null;
        window.AddHandler(InputElement.KeyDownEvent, (_, e) => seen = e,
            RoutingStrategies.Bubble, handledEventsToo: true);

        window.KeyTextInput("Open");
        window.KeyPress(Key.Enter, RawInputModifiers.None);

        Assert.Multiple(() =>
        {
            Assert.That(commit.Executions, Is.EqualTo(0),
                "CanExecute false must prevent Execute from running at all");
            Assert.That(Box(editor).Text, Is.EqualTo("Open"),
                "a refused commit must not clear what the user typed");
            Assert.That(seen?.Handled, Is.True,
                "Enter must still be marked handled even when the commit is refused");
        });
    }
}
