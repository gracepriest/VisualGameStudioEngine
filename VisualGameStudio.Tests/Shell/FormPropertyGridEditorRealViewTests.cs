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
    /// Part C review (VS): Esc CANCELS a Custom move — the pop-up closes and nothing is written; a click AWAY commits it
    /// once. Both close routes through real input. At two window sizes.
    /// </summary>
    [AvaloniaTest]
    public void TheCustomColour_EscCancels_AndAClickAwayCommitsOnce_AtTwoSizes()
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
            try
            {
                CustomView(tabs).Color = Colors.Red;
                Press(rig, Key.Escape);
                Assert.Multiple(() =>
                {
                    Assert.That(flyout.IsOpen, Is.False, $"{w}x{h}: Esc closed the pop-up");
                    Assert.That(edits, Is.Zero, $"{w}x{h}: Esc wrote nothing");
                    Assert.That(rig.Vm.Text, Is.EqualTo(before), $"{w}x{h}: byte-identical");
                });

                (flyout, tabs) = OpenColorDropDown(rig, back);
                CustomView(tabs).Color = Color.FromRgb(0, 0, 255);
                rig.Click(rig.Canvas); // a real click elsewhere in the window: light dismiss
                rig.Window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                Assert.Multiple(() =>
                {
                    Assert.That(flyout.IsOpen, Is.False, $"{w}x{h}: the click away closed the pop-up");
                    Assert.That(edits, Is.EqualTo(1), $"{w}x{h}: ONE write");
                    Assert.That(rig.Vm.Text, Does.Contain("BackColor=\"#0000FF\""), $"{w}x{h}: the moved colour");
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
    /// Part C review (WinForms run-time truth): the FORM's BackColor throws on a translucent colour, so its Custom tab has
    /// no alpha channel; a Label's (which supports one, measured) keeps it.
    /// </summary>
    [AvaloniaTest]
    public void TheCustomTabsAlphaChannel_IsOffWhereWinFormsThrowsOnAlpha()
    {
        using var rig = Open();
        var (flyout, tabs) = OpenColorDropDown(rig, rig.Row("BackColor"));
        var formAlpha = CustomView(tabs).IsAlphaEnabled;
        flyout.Hide();
        Dispatcher.UIThread.RunJobs();

        SelectOnCanvas(rig, "lbl");
        (flyout, tabs) = OpenColorDropDown(rig, rig.Row("BackColor"));
        var labelAlpha = CustomView(tabs).IsAlphaEnabled;
        flyout.Hide();
        Dispatcher.UIThread.RunJobs();

        Assert.Multiple(() =>
        {
            Assert.That(formAlpha, Is.False, "the Form's BackColor: no alpha");
            Assert.That(labelAlpha, Is.True, "a Label's BackColor: alpha");
        });
    }

    // ==================================================================
    // Slice 4 Task 5 — the Font dialog (D-2)
    // ==================================================================

    /// <summary>The seam's families — never this machine's (pre-flight M6). "Font &amp; Co" is one the catalog refuses.</summary>
    private static readonly string[] FakeFamilies = { "Arial", "Font & Co", "Verdana" };

    /// <summary>Clicks a Font row's <c>…</c> (real click) and returns the modal dialog it opened over the rig's window.</summary>
    private static VisualGameStudio.Shell.Views.Dialogs.FormFontDialog OpenFontDialog(Rig rig, FormPropertyRow font)
    {
        rig.Grid.FontFamilies = () => FakeFamilies;
        var ellipsis = rig.Container(font).GetVisualDescendants().OfType<Button>()
            .Single(b => b.Name == "FontEllipsis" && b.IsEffectivelyVisible);
        var hit = rig.Window.InputHitTest(rig.CentreInWindow(ellipsis)) as Visual;
        Assert.That(hit?.GetSelfAndVisualAncestors().Contains(ellipsis), Is.True,
            $"a click at the Font row's … reaches it (hit {hit?.GetType().Name})");

        rig.Click(ellipsis);
        Dispatcher.UIThread.RunJobs();
        var dialog = rig.Window.OwnedWindows.OfType<VisualGameStudio.Shell.Views.Dialogs.FormFontDialog>().SingleOrDefault()
                     ?? throw new InvalidOperationException("the … opened no Font dialog owned by the IDE window");
        dialog.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return dialog;
    }

    private static T Named<T>(Window dialog, string name) where T : Control =>
        dialog.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    /// <summary>
    /// The Font dialog through the real grid: a real click on <c>lbl</c>'s Font <c>…</c> opens a modal dialog owned by the
    /// IDE window (M4), offering the seam's families minus the one the catalog refuses; a real click on Arial, on Bold and
    /// on OK writes ONE canonical Font into the FILE with ONE Edited. Then the same with Cancel writes nothing. At two sizes.
    /// </summary>
    [AvaloniaTest]
    public void TheFontDialog_PicksAFamilyAndBold_AndOkWritesOneCanonicalFont_CancelWritesNothing_AtTwoSizes()
    {
        foreach (var (w, h) in TwoZooms)
        {
            using var rig = Open(w, h);
            SelectOnCanvas(rig, "lbl");
            var font = rig.Row("Font");
            var edits = 0;
            rig.GridVm.Edited += (_, _) => edits++;

            var dialog = OpenFontDialog(rig, font);
            try
            {
                var families = Named<ListBox>(dialog, "FamilyList").Items.Cast<object?>().ToList();
                Assert.Multiple(() =>
                {
                    Assert.That(families, Is.EqualTo(new object[] { "Arial", "Segoe UI", "Verdana" }),
                        $"{w}x{h}: the seam's families, minus 'Font & Co', plus WinForms' default");
                    Assert.That(Named<ListBox>(dialog, "FamilyList").SelectedItem, Is.EqualTo("Segoe UI"),
                        $"{w}x{h}: it starts at the font the Label inherits");
                });

                var arial = Named<ListBox>(dialog, "FamilyList").ContainerFromIndex(0)!;
                ClickInItsTopLevel(rig, arial);
                ClickInItsTopLevel(rig, Named<CheckBox>(dialog, "BoldBox"));
                Assert.That(edits, Is.Zero, $"{w}x{h}: nothing written while the dialog is open");
                ClickInItsTopLevel(rig, Named<Button>(dialog, "OkButton"));
                Dispatcher.UIThread.RunJobs();

                Assert.Multiple(() =>
                {
                    Assert.That(dialog.IsVisible, Is.False, $"{w}x{h}: OK closed the dialog");
                    Assert.That(rig.Control("lbl").Properties.GetValueOrDefault("Font"), Is.EqualTo("Arial, 9pt, style=Bold"), $"{w}x{h}: the model");
                    Assert.That(rig.Vm.Text, Does.Contain("Font=\"Arial, 9pt, style=Bold\""), $"{w}x{h}: the .blform text");
                    Assert.That(edits, Is.EqualTo(1), $"{w}x{h}: ONE edit for the whole dialog");
                });
            }
            finally
            {
                if (dialog.IsVisible) dialog.Close();
                Dispatcher.UIThread.RunJobs();
            }

            var before = rig.Vm.Text;
            var cancelled = OpenFontDialog(rig, font);
            try
            {
                Assert.That(Named<CheckBox>(cancelled, "BoldBox").IsChecked, Is.True, $"{w}x{h}: reopened at the stored Bold font");
                ClickInItsTopLevel(rig, Named<CheckBox>(cancelled, "ItalicBox"));
                ClickInItsTopLevel(rig, Named<Button>(cancelled, "CancelButton"));
                Dispatcher.UIThread.RunJobs();

                Assert.Multiple(() =>
                {
                    Assert.That(cancelled.IsVisible, Is.False, $"{w}x{h}: Cancel closed the dialog");
                    Assert.That(rig.Vm.Text, Is.EqualTo(before), $"{w}x{h}: Cancel leaves the file byte-identical");
                    Assert.That(edits, Is.EqualTo(1), $"{w}x{h}: no edit for Cancel");
                });
            }
            finally
            {
                if (cancelled.IsVisible) cancelled.Close();
                Dispatcher.UIThread.RunJobs();
            }
        }
    }

    /// <summary>
    /// Part C review: the dialog's other two ways out write nothing either — Esc (Cancel is the dialog's IsCancel button)
    /// and the window's close box (<c>Close()</c> with no result).
    /// </summary>
    [AvaloniaTest]
    public void TheFontDialog_EscAndTheCloseBox_WriteNothing()
    {
        using var rig = Open();
        SelectOnCanvas(rig, "lbl");
        var font = rig.Row("Font");
        var before = rig.Vm.Text;
        var edits = 0;
        rig.GridVm.Edited += (_, _) => edits++;

        var escaped = OpenFontDialog(rig, font);
        ClickInItsTopLevel(rig, Named<CheckBox>(escaped, "BoldBox"));
        Named<CheckBox>(escaped, "BoldBox").Focus();
        escaped.KeyPress(Key.Escape, RawInputModifiers.None); // no release: the press already closed the window
        Dispatcher.UIThread.RunJobs();
        Assert.That(escaped.IsVisible, Is.False, "Esc closed the dialog");
        if (escaped.IsVisible) escaped.Close();

        var boxed = OpenFontDialog(rig, font);
        ClickInItsTopLevel(rig, Named<CheckBox>(boxed, "ItalicBox"));
        boxed.Close(); // the close box: no result
        Dispatcher.UIThread.RunJobs();

        Assert.Multiple(() =>
        {
            Assert.That(edits, Is.Zero, "neither wrote");
            Assert.That(rig.Vm.Text, Is.EqualTo(before), "byte-identical");
        });
    }

    /// <summary>
    /// Part C review — VS behaviour, pinned: OK on an INHERITED font (the Label sets none) stores it explicitly, so the row
    /// turns bold and the control no longer follows its container's font.
    /// </summary>
    [AvaloniaTest]
    public void TheFontDialog_OkOnAnInheritedFont_StoresItExplicitly()
    {
        using var rig = Open();
        SelectOnCanvas(rig, "lbl");
        var font = rig.Row("Font");
        Assert.That(font.IsPresent, Is.False, "precondition: the Label inherits its font");
        var edits = 0;
        rig.GridVm.Edited += (_, _) => edits++;

        var dialog = OpenFontDialog(rig, font);
        ClickInItsTopLevel(rig, Named<Button>(dialog, "OkButton"));
        Dispatcher.UIThread.RunJobs();

        Assert.Multiple(() =>
        {
            Assert.That(rig.Vm.Text, Does.Contain("Font=\"Segoe UI, 9pt\""), "the inherited font, now explicit");
            Assert.That(edits, Is.EqualTo(1));
            Assert.That(font.IsPresent, Is.True);
        });
    }

    // ==================================================================
    // Slice 4 Task 7 — the Items editor (VS's String Collection Editor)
    // ==================================================================

    private static readonly string ComboDoc = Doc.Replace(
        "<Button Id=\"btn\" X=\"16\" Y=\"56\" Width=\"75\" Height=\"23\" TabIndex=\"1\"/>",
        "<Button Id=\"btn\" X=\"16\" Y=\"56\" Width=\"75\" Height=\"23\" TabIndex=\"1\"/>\n" +
        "    <ComboBox Id=\"cmb\" X=\"16\" Y=\"96\" Width=\"121\" Height=\"23\" TabIndex=\"2\"/>");

    /// <summary>
    /// The Items row of a ComboBox shows <c>(Collection)</c>; a real click on its <c>…</c> opens the String Collection
    /// Editor over the IDE window; <c>Smith, John⏎Beta</c> typed with REAL key input and OK writes two <c>&lt;Item&gt;</c>
    /// children (one Edited); reopened, typing and Cancel writes nothing. At two window sizes.
    /// </summary>
    [AvaloniaTest]
    public void TheItemsEditor_TypedThroughRealKeys_WritesOneItemEach_AndCancelWritesNothing_AtTwoSizes()
    {
        Assert.That(ComboDoc, Does.Contain("Id=\"cmb\""), "precondition: the fixture carries a ComboBox");
        foreach (var (w, h) in TwoZooms)
        {
            using var rig = Open(w, h, ComboDoc);
            SelectOnCanvas(rig, "cmb");
            var items = rig.Row("Items");
            var edits = 0;
            rig.GridVm.Edited += (_, _) => edits++;

            Assert.That(rig.ValuePanel(items).GetVisualDescendants().OfType<TextBlock>()
                .Any(t => t.Text == "(Collection)" && t.IsEffectivelyVisible), Is.True, $"{w}x{h}: the row shows (Collection)");

            var dialog = OpenItemsDialog(rig, items);
            try
            {
                var box = Named<TextBox>(dialog, "ItemsBox");
                ClickInItsTopLevel(rig, box);
                box.Focus();
                dialog.KeyTextInput("Smith, John");
                dialog.KeyPress(Key.Enter, RawInputModifiers.None);
                dialog.KeyRelease(Key.Enter, RawInputModifiers.None);
                dialog.KeyTextInput("Beta");
                Dispatcher.UIThread.RunJobs();
                Assert.That(edits, Is.Zero, $"{w}x{h}: nothing written while the editor is open");

                ClickInItsTopLevel(rig, Named<Button>(dialog, "OkButton"));
                Dispatcher.UIThread.RunJobs();

                Assert.Multiple(() =>
                {
                    Assert.That(dialog.IsVisible, Is.False, $"{w}x{h}: OK closed the editor");
                    Assert.That(rig.Control("cmb").Properties.GetValueOrDefault("Items"), Is.EqualTo("Smith, John\nBeta"), $"{w}x{h}: the model");
                    Assert.That(rig.Vm.Text, Does.Contain("<Item>Smith, John</Item>").And.Contain("<Item>Beta</Item>"), $"{w}x{h}: the file");
                    Assert.That(edits, Is.EqualTo(1), $"{w}x{h}: ONE edit");
                });
            }
            finally
            {
                if (dialog.IsVisible) dialog.Close();
                Dispatcher.UIThread.RunJobs();
            }

            var before = rig.Vm.Text;
            var cancelled = OpenItemsDialog(rig, items);
            try
            {
                Assert.That(Named<TextBox>(cancelled, "ItemsBox").Text, Is.EqualTo("Smith, John" + Environment.NewLine + "Beta"),
                    $"{w}x{h}: reopened on the stored items");
                Named<TextBox>(cancelled, "ItemsBox").Focus();
                cancelled.KeyTextInput("X");
                ClickInItsTopLevel(rig, Named<Button>(cancelled, "CancelButton"));
                Dispatcher.UIThread.RunJobs();
                Assert.Multiple(() =>
                {
                    Assert.That(rig.Vm.Text, Is.EqualTo(before), $"{w}x{h}: Cancel leaves the file byte-identical");
                    Assert.That(edits, Is.EqualTo(1), $"{w}x{h}: no edit for Cancel");
                });
            }
            finally
            {
                if (cancelled.IsVisible) cancelled.Close();
                Dispatcher.UIThread.RunJobs();
            }
        }
    }

    /// <summary>
    /// Part E: the MODELESS path of <see cref="FormPropertyGridView.ShowDialogAsync"/> — a host whose top level is not a
    /// Window cannot own a modal dialog, so the dialog is shown on its own and awaited until it closes: OK yields the
    /// dialog's result, the close box yields null.
    /// </summary>
    [AvaloniaTest]
    public void TheModelessDialogPath_YieldsOkResult_AndNullForTheCloseBox()
    {
        var okDialog = new VisualGameStudio.Shell.Views.Dialogs.FormItemsDialog { DataContext = new FormItemsDialogViewModel("A") };
        var ok = FormPropertyGridView.ShowDialogAsync(okDialog, top: null,
            () => (okDialog.DataContext as FormItemsDialogViewModel)?.Result);
        Dispatcher.UIThread.RunJobs();
        Assert.That(okDialog.IsVisible, Is.True, "shown on its own, with no owner");
        ((FormItemsDialogViewModel)okDialog.DataContext!).Text = "A\nB";
        var okButton = okDialog.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "OkButton");
        var at = okButton.TranslatePoint(new Point(okButton.Bounds.Width / 2, okButton.Bounds.Height / 2), okDialog)!.Value;
        okDialog.MouseDown(at, MouseButton.Left);
        okDialog.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        var boxed = new VisualGameStudio.Shell.Views.Dialogs.FormItemsDialog { DataContext = new FormItemsDialogViewModel("A") };
        var closed = FormPropertyGridView.ShowDialogAsync(boxed, top: null,
            () => (boxed.DataContext as FormItemsDialogViewModel)?.Result);
        Dispatcher.UIThread.RunJobs();
        boxed.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.Multiple(() =>
        {
            Assert.That(ok.IsCompletedSuccessfully, Is.True, "OK closed the modeless dialog");
            Assert.That(ok.Result, Is.EqualTo("A\nB"));
            Assert.That(closed.IsCompletedSuccessfully, Is.True);
            Assert.That(closed.Result, Is.Null, "the close box yields nothing");
        });
    }

    private static VisualGameStudio.Shell.Views.Dialogs.FormItemsDialog OpenItemsDialog(Rig rig, FormPropertyRow items)
    {
        var ellipsis = rig.Container(items).GetVisualDescendants().OfType<Button>()
            .Single(b => b.Name == "ItemsEllipsis" && b.IsEffectivelyVisible);
        var hit = rig.Window.InputHitTest(rig.CentreInWindow(ellipsis)) as Visual;
        Assert.That(hit?.GetSelfAndVisualAncestors().Contains(ellipsis), Is.True,
            $"a click at the Items row's … reaches it (hit {hit?.GetType().Name})");
        rig.Click(ellipsis);
        Dispatcher.UIThread.RunJobs();
        var dialog = rig.Window.OwnedWindows.OfType<VisualGameStudio.Shell.Views.Dialogs.FormItemsDialog>().SingleOrDefault()
                     ?? throw new InvalidOperationException("the … opened no String Collection Editor owned by the IDE window");
        dialog.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return dialog;
    }

    // ==================================================================
    // Slice 4 Task 10 — the image picker
    // ==================================================================

    /// <summary>
    /// A PictureBox's Image <c>…</c>, with the two seams faked (the headless platform has no storage provider): a real
    /// click; a file OUTSIDE the project is picked; "copy" is chosen (and keeping the path was offered — WinForms); the
    /// document gains <c>Image="Resources/x.png"</c> through the row (ONE Edited), and the copy exists in the project.
    /// </summary>
    [AvaloniaTest]
    public void TheImagePicker_CopiesAnOutsideFileIntoResources_AndWritesTheRelativePath()
    {
        var root = Path.Combine(Path.GetTempPath(), "bl-picker-" + Guid.NewGuid().ToString("N"));
        var project = Path.Combine(root, "App");
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, "App.blproj"), "<BasicLangProject/>");
        var outside = Path.Combine(root, "Downloads", "x.png");
        Directory.CreateDirectory(Path.GetDirectoryName(outside)!);
        File.WriteAllBytes(outside, new byte[] { 1, 2, 3 });

        var doc = Doc.Replace("<Button Id=\"btn\" X=\"16\" Y=\"56\" Width=\"75\" Height=\"23\" TabIndex=\"1\"/>",
            "<Button Id=\"btn\" X=\"16\" Y=\"56\" Width=\"75\" Height=\"23\" TabIndex=\"1\"/>\n" +
            "    <PictureBox Id=\"pic\" X=\"16\" Y=\"96\" Width=\"100\" Height=\"50\" TabIndex=\"2\"/>");
        try
        {
            using var rig = Open(doc: doc, dir: project + Path.DirectorySeparatorChar);
            bool? offered = null;
            rig.Grid.PickFile = _ => Task.FromResult<string?>(outside);
            rig.Grid.ChooseImport = offer =>
            {
                offered = offer;
                return Task.FromResult(FormAssetImportChoice.CopyIntoProject);
            };
            SelectOnCanvas(rig, "pic");
            var image = rig.Row("Image");
            var edits = 0;
            rig.GridVm.Edited += (_, _) => edits++;

            var picker = rig.Container(image).GetVisualDescendants().OfType<Button>()
                .Single(b => b.Name == "AssetPicker" && b.IsEffectivelyVisible);
            rig.Click(picker);
            Dispatcher.UIThread.RunJobs();

            Assert.Multiple(() =>
            {
                Assert.That(offered, Is.True, "WinForms offers keeping the absolute path");
                Assert.That(rig.Vm.Text, Does.Contain("Image=\"Resources/x.png\""), "the .blform text");
                Assert.That(edits, Is.EqualTo(1));
                Assert.That(File.ReadAllBytes(Path.Combine(project, "Resources", "x.png")), Is.EqualTo(new byte[] { 1, 2, 3 }));
            });
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
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
