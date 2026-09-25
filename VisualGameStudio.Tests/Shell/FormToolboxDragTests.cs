using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// The toolbox can START a drag — pinned at the only place the bug lived: the subscription.
///
/// <para>⛔ The defect: <c>PointerPressed</c> was wired as a XAML attribute on the toolbox
/// <c>ListBox</c>. <c>ListBox</c> marks that event HANDLED while it updates selection, and a XAML
/// attribute subscribes WITHOUT <c>handledEventsToo</c> — so <c>OnToolboxPointerPressed</c> never
/// ran, the drag kind stayed null, and every <c>OnToolboxPointerMoved</c> returned at its first
/// line. Clicking a row highlighted it and nothing could be dragged onto the canvas. The suite had
/// nothing at all on this path: the only toolbox assertion in the repo was
/// <c>Toolbox.Target</c>.</para>
///
/// <para>⛔ There is nothing to assert about OUTPUT here — the handler simply never fires — so
/// these pin the wiring, the way <c>SolutionExplorerNewFormTests</c> reads the AXAML for a command
/// binding. A handler the platform never calls is exactly as unreachable as one nobody wrote.</para>
/// </summary>
[TestFixture]
public class FormToolboxDragTests
{
    private static string Axaml => ReadRepoFile("CodeEditorDocumentView.axaml");

    private static string CodeBehind => ReadRepoFile("CodeEditorDocumentView.axaml.cs");

    /// <summary>
    /// ⛔ FAILS when the file is missing rather than <c>Assert.Ignore</c>-ing. An ignore on a
    /// missing asset is a pass by absence — and these tests exist precisely because a green suite
    /// said nothing while the drag was dead.
    /// </summary>
    private static string ReadRepoFile(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(
                directory.FullName, "VisualGameStudio.Shell", "Views", "Documents", fileName);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        Assert.Fail($"'{fileName}' was not found walking up from {AppContext.BaseDirectory}. " +
                    "These tests read the view's own source; without it they verify nothing.");
        return string.Empty;
    }

    /// <summary>
    /// ⛔ The regression guard. Re-adding the attribute is the natural thing to do — it looks
    /// tidier and sits beside the two that DO work — and it silently kills the drag.
    /// </summary>
    [Test]
    public void ToolboxPointerPressedIsNotWiredAsAXamlAttribute()
    {
        Assert.That(Axaml, Does.Not.Contain("PointerPressed=\"OnToolboxPointerPressed\""),
            "PointerPressed is wired as a XAML attribute again. ListBox marks that event handled " +
            "when it updates selection, and a XAML attribute does not receive handled events, so " +
            "the handler will never run and the toolbox will silently stop being a drag source. " +
            "Wire it in the constructor with handledEventsToo: true instead.");
    }

    [Test]
    public void ToolboxPointerPressedIsWiredWithHandledEventsToo()
    {
        var code = CodeBehind;

        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Contain("PointerPressedEvent"),
                "nothing subscribes to PointerPressedEvent in code-behind — the toolbox drag " +
                "cannot start");
            Assert.That(code, Does.Contain("OnToolboxPointerPressed"),
                "the toolbox press handler is not referenced by the constructor wiring");
            Assert.That(code, Does.Contain("handledEventsToo: true"),
                "the toolbox press is subscribed WITHOUT handledEventsToo, so ListBox's own " +
                "selection handling will consume it first and the drag will never arm");
        });
    }

    /// <summary>
    /// The platform fact the fix depends on, measured rather than assumed.
    ///
    /// <para>⚠ If a future Avalonia stops marking the press handled, the code-behind wiring becomes
    /// belt-and-braces rather than load-bearing — worth knowing, and this is what would say so.
    /// It asserts Avalonia's behaviour, deliberately: the fix is only correct BECAUSE of it.</para>
    /// </summary>
    [AvaloniaTest]
    public void AListBoxMarksAPointerPressHandled_WhichIsWhyTheAttributeFails()
    {
        // ⚠ No Width/Height: a sized control is CENTRED in the window, so its rows would not start
        // at the origin and a fixed press point would land on empty chrome — which fails as
        // "the press did not reach the ListBox", looking like a routing problem rather than a
        // layout one. Letting it fill puts row 0 at the top-left.
        var list = new ListBox
        {
            ItemsSource = new[] { "Button", "TextBox", "Label" }
        };

        var attributeStyleFired = false;
        var handledEventsTooFired = false;
        var handledAlready = false;

        // Exactly what PointerPressed="Handler" in XAML does: the CLR event, which is AddHandler
        // with the default routes and handledEventsToo FALSE.
        list.PointerPressed += (_, _) => attributeStyleFired = true;

        list.AddHandler(
            InputElement.PointerPressedEvent,
            (object? _, PointerPressedEventArgs e) =>
            {
                handledEventsTooFired = true;
                handledAlready = e.Handled;
            },
            RoutingStrategies.Bubble,
            handledEventsToo: true);

        var window = new Window { Width = 300, Height = 300, Content = list };
        window.Show();

        Assert.That(list.ContainerFromIndex(0), Is.Not.Null,
            "the ListBox realised no item containers, so a press would land on empty chrome and " +
            "this test would prove nothing");

        // The list fills the window from its origin, so a point a little way in is over a row.
        // Deliberately not computed from the container's own transform: the assertion below is
        // that SOMETHING was selected, which is what makes the press meaningful.
        window.MouseDown(new Avalonia.Point(20, 12), MouseButton.Left);

        Assert.Multiple(() =>
        {
            Assert.That(list.SelectedItem, Is.Not.Null,
                "the press did not reach the ListBox at all — this test proves nothing as written");
            Assert.That(handledEventsTooFired, Is.True,
                "a handledEventsToo subscriber did not fire; the routing assumption is wrong");
            Assert.That(handledAlready, Is.True,
                "ListBox no longer marks the press handled. The toolbox's code-behind wiring is " +
                "then no longer load-bearing — but it is still correct, so this is information, " +
                "not a defect.");
            Assert.That(attributeStyleFired, Is.False,
                "an attribute-style subscriber DID receive the handled press, which would mean " +
                "the original wiring works after all");
        });
    }
}
