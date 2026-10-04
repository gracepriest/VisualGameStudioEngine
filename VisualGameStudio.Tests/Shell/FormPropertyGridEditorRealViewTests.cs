using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BasicLang.Forms;
using NUnit.Framework;
using VisualGameStudio.Shell.ViewModels.Designer;
using VisualGameStudio.Shell.Views.Controls;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// Slice 4's editors driven through the REAL grid view (the same rig as <c>FormPropertyGridRealViewTests.cs</c>; this
/// file is the same partial fixture, so the rig, <c>TwoZooms</c> and the stable ids are shared rather than copied).
/// Real clicks open each pop-up and land in its own top level, in ITS coordinates. Every window is closed.
/// </summary>
public partial class FormPropertyGridRealViewTests
{
    // ==================================================================
    // Slice 4 Task 4 — the colour editor (D-1)
    // ==================================================================

    private const string WebGridDoc = """
        <WebForm Name="GridForm" Version="1">
          <Layout Kind="Grid" Cols="1fr" Rows="auto"/>
          <Controls>
            <Label Id="lbl" Col="0" Row="0" TabIndex="0" Text="Hello"/>
          </Controls>
        </WebForm>
        """;

    /// <summary>A real click at <paramref name="target"/>'s centre, in its own top level (a pop-up's, for pop-up content).</summary>
    private static void ClickInItsTopLevel(Rig rig, Visual target)
    {
        var top = TopLevel.GetTopLevel(target) ?? throw new InvalidOperationException($"{target} has no top level");
        var at = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), top)!.Value;
        top.MouseDown(at, MouseButton.Left);
        top.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        rig.Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Opens a colour row's drop-down with a real click on its swatch button (asserting the click reaches it, the
    /// Anchor/Dock scrollbar lesson) and returns the open flyout and its tab control.
    /// </summary>
    private static (Flyout Flyout, TabControl Tabs) OpenColorDropDown(Rig rig, FormPropertyRow row)
    {
        var dropDown = rig.Container(row).GetVisualDescendants().OfType<FormColorDropDown>().Single(d => d.IsEffectivelyVisible);
        var button = dropDown.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SwatchButton");
        var hit = rig.Window.InputHitTest(rig.CentreInWindow(button)) as Visual;
        Assert.That(hit?.GetSelfAndVisualAncestors().Contains(button), Is.True,
            $"{row.Name}: a click at the swatch button's centre reaches it (hit {hit?.GetType().Name})");

        rig.Click(button);
        rig.Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var flyout = (Flyout)button.Flyout!;
        Assert.That(flyout.IsOpen, Is.True, $"{row.Name}: the real click opened the colour pop-up");
        var tabs = (TabControl)flyout.Content!;
        Assert.That(tabs.DataContext, Is.SameAs(row), $"{row.Name}: the pop-up is bound to its row");
        return (flyout, tabs);
    }

    /// <summary>Selects a tab with a real click on its header.</summary>
    private static void PickTab(Rig rig, TabControl tabs, string header)
    {
        var tab = tabs.Items.OfType<TabItem>().Single(t => (string?)t.Header == header);
        ClickInItsTopLevel(rig, tab);
        Assert.That(tabs.SelectedItem, Is.SameAs(tab), $"the real click selected the {header} tab");
    }

    /// <summary>The named entries the open tab shows, in order.</summary>
    private static List<Button> NamedEntries(TabControl tabs) =>
        tabs.GetVisualDescendants().OfType<Button>().Where(b => b.DataContext is FormColorChoice && b.IsEffectivelyVisible).ToList();

    /// <summary>Picks a named entry with a real click, scrolling it into the pop-up's view first.</summary>
    private static void PickNamed(Rig rig, TabControl tabs, string name)
    {
        var entry = NamedEntries(tabs).Single(b => ((FormColorChoice)b.DataContext!).Name == name);
        entry.BringIntoView();
        rig.Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        ClickInItsTopLevel(rig, entry);
    }

    /// <summary>The Custom tab's ColorView — through the tab's own content, so it is found whichever tab is showing.</summary>
    private static ColorView CustomView(TabControl tabs) =>
        ((Panel)tabs.Items.OfType<TabItem>().Single(t => (string?)t.Header == "Custom").Content!).Children.OfType<ColorView>().Single();

    private static byte[] FrameBytes(Rig rig)
    {
        Dispatcher.UIThread.RunJobs();
        using var frame = rig.Window.CaptureRenderedFrame() ?? throw new InvalidOperationException("no frame");
        using var buffer = frame.Lock();
        var bytes = new byte[buffer.RowBytes * buffer.Size.Height];
        Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
        return bytes;
    }

    /// <summary>
    /// The BackColor drop-down on <c>lbl</c>: a real click opens it; its Custom tab's ColorView HAS A TEMPLATE (visual
    /// children — pre-flight M2: without the theme include it draws nothing and nothing throws) and starts at the row's
    /// colour; the frame with the pop-up open differs from the closed one; a real click on <c>Red</c> in the Web tab
    /// writes <c>BackColor="Red"</c> into the FILE with ONE Edited, and the pop-up closes. At two window sizes.
    /// </summary>
    [AvaloniaTest]
    public void TheColourDropDown_HasATemplatedColorView_AndPickingRedOnTheWebTab_WritesItOnce_AtTwoSizes()
    {
        foreach (var (w, h) in TwoZooms)
        {
            using var rig = Open(w, h);
            SelectOnCanvas(rig, "lbl");
            var back = rig.Row("BackColor");
            var closed = FrameBytes(rig);
            var edits = 0;
            rig.GridVm.Edited += (_, _) => edits++;

            var (flyout, tabs) = OpenColorDropDown(rig, back);
            try
            {
                var view = tabs.GetVisualDescendants().OfType<ColorView>().Single();
                Assert.Multiple(() =>
                {
                    Assert.That(view.GetVisualDescendants().Count(), Is.GreaterThan(0),
                        $"{w}x{h}: the ColorView has a template (the theme include reached the pop-up)");
                    Assert.That(back.SwatchColor, Is.Null, $"{w}x{h}: precondition: a Label's BackColor has no default");
                    Assert.That(view.Color, Is.EqualTo(Colors.White), $"{w}x{h}: …so the Custom tab starts at white");
                    Assert.That(FrameBytes(rig), Is.Not.EqualTo(closed), $"{w}x{h}: the open pop-up draws");
                });

                PickTab(rig, tabs, "Web");
                PickNamed(rig, tabs, "Red");

                Assert.Multiple(() =>
                {
                    Assert.That(rig.Control("lbl").Properties.GetValueOrDefault("BackColor"), Is.EqualTo("Red"), $"{w}x{h}: the model");
                    Assert.That(rig.Vm.Text, Does.Contain("BackColor=\"Red\""), $"{w}x{h}: the .blform text");
                    Assert.That(edits, Is.EqualTo(1), $"{w}x{h}: ONE edit for one pick");
                    Assert.That(flyout.IsOpen, Is.False, $"{w}x{h}: a named pick closes the pop-up");
                    Assert.That(back.SwatchColor, Is.EqualTo(Color.FromRgb(255, 0, 0)), $"{w}x{h}: the swatch follows");
                });

                // MEMORY: a distinctness gate alone lets the WRONG thing draw — so the Custom tab must also START at the
                // row's value: reopened over Red, it shows red.
                (flyout, tabs) = OpenColorDropDown(rig, back);
                Assert.Multiple(() =>
                {
                    Assert.That(((TabItem)tabs.SelectedItem!).Header, Is.EqualTo("Web"), $"{w}x{h}: reopened on the tab holding Red (VS)");
                    Assert.That(CustomView(tabs).Color, Is.EqualTo(Color.FromRgb(255, 0, 0)),
                        $"{w}x{h}: reopened, the Custom tab starts at the row's Red");
                });
                flyout.Hide();
                Dispatcher.UIThread.RunJobs();
                Assert.That(edits, Is.EqualTo(1), $"{w}x{h}: reopening and closing wrote nothing");
            }
            finally
            {
                flyout.Hide();
                Dispatcher.UIThread.RunJobs();
            }
        }
    }

    /// <summary>
    /// VS opens its colour drop-down on the tab that holds the current value: the Form's BackColor shows <c>Control</c>
    /// (its default), so the pop-up opens on System, with <c>Control</c> among its entries. At two window sizes.
    /// </summary>
    [AvaloniaTest]
    public void TheColourDropDown_OpensOnTheTabHoldingTheValue_AtTwoSizes()
    {
        foreach (var (w, h) in TwoZooms)
        {
            using var rig = Open(w, h);
            Assert.That(rig.GridVm.SelectedControl, Is.Null, $"{w}x{h}: precondition: the Form is selected");
            var back = rig.Row("BackColor");
            Assert.That(back.DisplayValue, Is.EqualTo("Control"), $"{w}x{h}: precondition: the Form's default BackColor");

            var (flyout, tabs) = OpenColorDropDown(rig, back);
            try
            {
                Assert.Multiple(() =>
                {
                    Assert.That(((TabItem)tabs.SelectedItem!).Header, Is.EqualTo("System"), $"{w}x{h}: opened on System");
                    Assert.That(NamedEntries(tabs).Select(b => ((FormColorChoice)b.DataContext!).Name), Does.Contain("Control"),
                        $"{w}x{h}: showing the value's own entry");
                });
            }
            finally
            {
                flyout.Hide();
                Dispatcher.UIThread.RunJobs();
            }
        }
    }

    /// <summary>
    /// On a WEB form the System tab is the catalog's answer for the web: <c>Window</c> (CSS <c>Canvas</c>) is offered,
    /// <c>ActiveCaption</c> (no CSS) is not; picking <c>Window</c> with a real click writes it once. At two window sizes.
    /// </summary>
    [AvaloniaTest]
    public void OnAWebForm_TheSystemTab_OffersOnlyCssColours_AndAPickWritesIt_AtTwoSizes()
    {
        foreach (var (w, h) in TwoZooms)
        {
            using var rig = Open(w, h, WebGridDoc, FormTarget.Web);
            SelectOnCanvas(rig, "lbl");
            var back = rig.Row("BackColor");
            var edits = 0;
            rig.GridVm.Edited += (_, _) => edits++;

            var (flyout, tabs) = OpenColorDropDown(rig, back);
            try
            {
                PickTab(rig, tabs, "System");
                var names = NamedEntries(tabs).Select(b => ((FormColorChoice)b.DataContext!).Name).ToList();
                Assert.Multiple(() =>
                {
                    Assert.That(names, Does.Contain("Window").And.Not.Contain("ActiveCaption"), $"{w}x{h}: the web's system colours");
                    Assert.That(names, Is.EqualTo(FormSystemColors.Names.Where(n => FormSystemColors.CssFor(n) != null)),
                        $"{w}x{h}: exactly the eight with CSS");
                });

                PickNamed(rig, tabs, "Window");

                Assert.Multiple(() =>
                {
                    Assert.That(rig.Vm.Text, Does.Contain("BackColor=\"Window\""), $"{w}x{h}: the .blwebform text");
                    Assert.That(edits, Is.EqualTo(1), $"{w}x{h}: ONE edit");
                });
            }
            finally
            {
                flyout.Hide();
                Dispatcher.UIThread.RunJobs();
            }
        }
    }

    /// <summary>
    /// D-1c: the Custom colour is written ONCE, when the pop-up closes — never per <c>ColorChanged</c>. Opening and
    /// closing without moving it writes nothing; moving it three times and clicking OK writes the LAST colour once, as
    /// <c>#RRGGBB</c>. At two window sizes.
    /// </summary>
    [AvaloniaTest]
    public void TheCustomColour_IsWrittenOnceOnClose_NeverPerChange_AtTwoSizes()
    {
        foreach (var (w, h) in TwoZooms)
        {
            using var rig = Open(w, h);
            SelectOnCanvas(rig, "lbl");
            var back = rig.Row("BackColor");
            var before = rig.Vm.Text;
            var edits = 0;
            rig.GridVm.Edited += (_, _) => edits++;

            var (flyout, tabs) = OpenColorDropDown(rig, back);
            flyout.Hide();
            Dispatcher.UIThread.RunJobs();
            Assert.Multiple(() =>
            {
                Assert.That(edits, Is.Zero, $"{w}x{h}: opening and closing writes nothing (the seed is not an edit)");
                Assert.That(rig.Vm.Text, Is.EqualTo(before), $"{w}x{h}: the file is byte-identical");
            });

            (flyout, tabs) = OpenColorDropDown(rig, back);
            try
            {
                var view = tabs.GetVisualDescendants().OfType<ColorView>().Single();
                view.Color = Colors.Red;
                view.Color = Colors.Lime;
                view.Color = Color.FromRgb(0, 0, 255);
                Dispatcher.UIThread.RunJobs();
                Assert.That(edits, Is.Zero, $"{w}x{h}: nothing written while the pop-up is open");

                var ok = tabs.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "OK");
                ClickInItsTopLevel(rig, ok);

                Assert.Multiple(() =>
                {
                    Assert.That(flyout.IsOpen, Is.False, $"{w}x{h}: OK closed the pop-up");
                    Assert.That(edits, Is.EqualTo(1), $"{w}x{h}: ONE write for three moves");
                    Assert.That(rig.Vm.Text, Does.Contain("BackColor=\"#0000FF\""), $"{w}x{h}: the last colour, opaque hex");
                });
            }
            finally
            {
                flyout.Hide();
                Dispatcher.UIThread.RunJobs();
            }
        }
    }
}
