using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BasicLang.Forms;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Events;
using VisualGameStudio.Editor.Controls;
using VisualGameStudio.Shell.Controls;
using VisualGameStudio.Shell.ViewModels.Designer;
using VisualGameStudio.Shell.ViewModels.Documents;
using VisualGameStudio.Shell.Views.Controls;
using VisualGameStudio.Shell.Views.Documents;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// The owner's click-through of the property grid (slice 2), in the REAL <see cref="CodeEditorDocumentView"/>
/// with the IDE's own AppStyles and the Dark theme, at the owner's window size (~1200x830) and a smaller one.
/// Each test measures LAYOUT — bounds, visibility, pixels — because every defect here was a layout defect in
/// code whose bindings and behaviour were already green:
/// <list type="number">
/// <item>the Code/Design toggle was drawn ON TOP of the grid's object selector;</item>
/// <item>a row's value was clipped on the LEFT ("omboBox1") — an editor wider than its column is centred over it;</item>
/// <item>the form document's XML showed through the design surface;</item>
/// <item>every row must be reachable by scrolling at a short panel height.</item>
/// </list>
/// ⛔ Every window is CLOSED (<see cref="Rig.Dispose"/>).
/// </summary>
[TestFixture]
public class FormDesignerLayoutRealViewTests
{
    private const string Dir = "/proj/";

    private const string LongCaption = "A caption far longer than the value column can ever show";

    /// <summary>The owner's shape: a Grid-layout web form with a ComboBox1 — plus a short and a long caption.</summary>
    private const string WebDoc = $"""
        <WebForm Name="LoginForm" Version="1">
          <Layout Kind="Grid" Cols="auto,1fr" Rows="auto,auto" Gap="8px"/>
          <Controls>
            <Label Id="Label1" Col="0" Row="0" TabIndex="0" Text="A"/>
            <ComboBox Id="ComboBox1" Col="1" Row="0" TabIndex="1" Text="ComboBox1"/>
            <Label Id="Label2" Col="0" Row="1" TabIndex="2" Text="{LongCaption}"/>
          </Controls>
        </WebForm>
        """;

    /// <summary>A WinForms form, for the pixel-geometry rows (choice lists, spinners, Anchor/Dock).</summary>
    private const string WinDoc = """
        <Form Name="WinForm" Version="1" Width="640" Height="480" Text="WinForm">
          <Controls>
            <Button Id="btn" X="16" Y="56" Width="75" Height="23" TabIndex="0"/>
          </Controls>
          <Resources/>
        </Form>
        """;

    /// <summary>The owner's window, and a smaller one (a shorter panel, a narrower canvas).</summary>
    private static readonly (double Width, double Height)[] TwoSizes = { (1200, 830), (900, 560) };

    private sealed class Rig : IDisposable
    {
        public required CodeEditorDocumentViewModel Vm { get; init; }
        public required CodeEditorDocumentView View { get; init; }
        public required Window Window { get; init; }

        public FormPropertyGridViewModel GridVm => Vm.PropertyGrid;
        public FormPropertyGridView Grid => View.FindControl<FormPropertyGridView>("PropertyGridView")!;
        public ListBox List => Grid.FindControl<ListBox>("PropertyList")!;
        public ComboBox Selector => Grid.FindControl<ComboBox>("ObjectSelector")!;
        public FormCanvasControl Canvas => View.FindControl<FormCanvasControl>("DesignCanvas")!;
        public ListBox Toolbox => View.FindControl<ListBox>("ToolboxList")!;

        /// <summary>The Code/Design toggle and the split buttons beside it: the whole strip at the top right.</summary>
        public Button ModeToggle =>
            View.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Command, Vm.ToggleDesignModeCommand));

        public StackPanel ToggleStrip => (StackPanel)ModeToggle.Parent!;

        public Rect InWindow(Visual v) =>
            new(v.TranslatePoint(new Point(0, 0), Window)
                ?? throw new InvalidOperationException($"{v} is not in the window"), v.Bounds.Size);

        public void Select(string id)
        {
            Selector.SelectedIndex = GridVm.Objects.ToList().FindIndex(o => o.Name == id);
            Dispatcher.UIThread.RunJobs();
            Window.UpdateLayout();
            Assert.That(GridVm.SelectedControl?.Id, Is.EqualTo(id), $"precondition: {id} is selected");
        }

        public ListBoxItem Container(object item)
        {
            List.ScrollIntoView(item);
            Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            return (ListBoxItem?)List.ContainerFromItem(item)
                   ?? throw new InvalidOperationException($"{item} is not realised in the real grid");
        }

        public FormPropertyRow Row(string name) => GridVm.Rows.Single(r => r.Name == name);

        /// <summary>The 1px rule between a row's name and value columns.</summary>
        public Border Divider(ListBoxItem container) =>
            container.GetVisualDescendants().OfType<Border>()
                .First(b => Avalonia.Controls.Grid.GetColumn(b) == 1 && b.Bounds.Width is > 0 and <= 1.01);

        public Panel ValuePanel(ListBoxItem container) =>
            container.GetVisualDescendants().OfType<Panel>()
                .First(p => p.GetType() == typeof(Panel) && Avalonia.Controls.Grid.GetColumn(p) == 2);

        public void Dispose() => Window.Close();
    }

    private static Rig Open(string doc, string formName, FormTarget target, double width, double height, bool design = true)
    {
        var scaffold = FormScaffolder.Create(formName, target);
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Dir + scaffold.DocumentFileName] = doc,
            [Dir + scaffold.CodeFileName] = scaffold.CodeText
        };
        var fs = new Mock<IFileService>();
        fs.Setup(f => f.ReadFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string p, CancellationToken _) => Task.FromResult(files[p]));
        fs.Setup(f => f.FileExistsAsync(It.IsAny<string>()))
            .Returns((string p) => Task.FromResult(files.ContainsKey(p)));

        var vm = new CodeEditorDocumentViewModel(fs.Object, new Mock<IEventAggregator>().Object)
        {
            FilePath = Dir + scaffold.DocumentFileName
        };
        vm.SetContent(doc);
        if (design)
        {
            Assert.That(vm.EnterDesignModeForFormDocument(), Is.True, "precondition: the designer must open");
        }

        var view = new CodeEditorDocumentView { DataContext = vm };
        var window = new Window
        {
            Width = width, Height = height, Content = view, RequestedThemeVariant = ThemeVariant.Dark
        };
        // The IDE's own styles and theme brushes (App.axaml includes exactly this file).
        window.Styles.Add(new StyleInclude(new Uri("avares://VisualGameStudio/"))
        {
            Source = new Uri("avares://VisualGameStudio/Resources/Styles/AppStyles.axaml")
        });

        try
        {
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.That(window.FindResource("IdeBg"), Is.Not.Null, "precondition: the IDE's styles are loaded");
            return new Rig { Vm = vm, View = view, Window = window };
        }
        catch
        {
            window.Close();
            throw;
        }
    }

    private static Rig OpenWeb(double width, double height, bool design = true) =>
        Open(WebDoc, "LoginForm", FormTarget.Web, width, height, design);

    // ==================================================================
    // 1. The mode toggle never covers the grid
    // ==================================================================

    /// <summary>
    /// ⛔ The Code/Design toggle (and the split buttons beside it) sits over the top-right corner of the
    /// document view in BOTH modes. The old Properties window started with an inert caption row there; the
    /// new one starts with the object selector, and the toggle was drawn on top of it — covering its text and
    /// its drop-down arrow. The design surface now reserves a strip for it.
    /// </summary>
    [AvaloniaTest]
    public void TheModeToggle_NeverOverlapsTheObjectSelectorOrAnyDesignPanel_AtTwoSizes()
    {
        foreach (var (w, h) in TwoSizes)
        {
            using var rig = OpenWeb(w, h);
            rig.Select("ComboBox1");

            var strip = rig.InWindow(rig.ToggleStrip);
            var toggle = rig.InWindow(rig.ModeToggle);
            var selector = rig.InWindow(rig.Selector);
            TestContext.WriteLine($"[{w}x{h}] toggle strip {strip}; selector {selector}");

            Assert.Multiple(() =>
            {
                Assert.That(rig.ModeToggle.IsEffectivelyVisible, Is.True, $"{w}x{h}: precondition: the toggle is shown");
                Assert.That(toggle.Intersects(selector), Is.False, $"{w}x{h}: the toggle covers the object selector");
                Assert.That(strip.Intersects(rig.InWindow(rig.Grid)), Is.False, $"{w}x{h}: the toggle strip covers the property grid");
                Assert.That(strip.Intersects(rig.InWindow(rig.Toolbox)), Is.False, $"{w}x{h}: the toggle strip covers the toolbox");
                Assert.That(strip.Intersects(rig.InWindow(rig.Canvas)), Is.False, $"{w}x{h}: the toggle strip covers the canvas");
            });
        }
    }

    // ==================================================================
    // 2. A row's value is never clipped on the left
    // ==================================================================

    /// <summary>
    /// ⛔ The owner saw "omboBox1" in the Text row. The shared <see cref="TypedValueEditor"/>'s text box had
    /// MinWidth 200 (sized for the Settings dialog) in a ~154px value column, and Avalonia arranges a Stretch
    /// child that does not fit CENTRED over its slot — so the box began left of the divider, under the name
    /// cell. Checked for a short, the owner's, and a long caption.
    /// </summary>
    [AvaloniaTest]
    public void ATextRowsValue_StartsRightOfTheDivider_AndItsFirstCharacterIsVisible_ForShortAndLongValues()
    {
        foreach (var (w, h) in TwoSizes)
        {
            using var rig = OpenWeb(w, h);
            foreach (var (id, caption) in new[] { ("Label1", "A"), ("ComboBox1", "ComboBox1"), ("Label2", LongCaption) })
            {
                rig.Select(id);
                var container = rig.Container(rig.Row("Text"));
                var box = container.GetVisualDescendants().OfType<TextBox>().First(t => t.IsEffectivelyVisible);
                var presenter = box.GetVisualDescendants().OfType<TextPresenter>().First();
                var divider = rig.InWindow(rig.Divider(container));
                var valueCell = rig.InWindow(rig.ValuePanel(container));
                var boxRect = rig.InWindow(box);
                var textRect = rig.InWindow(presenter);
                TestContext.WriteLine($"[{w}x{h} {id}] divider {divider}; box {boxRect}; text {textRect}; cell {valueCell}");

                Assert.Multiple(() =>
                {
                    Assert.That(box.Text, Is.EqualTo(caption), $"{id}: precondition: the Text row's editor");
                    Assert.That(boxRect.Left, Is.GreaterThanOrEqualTo(divider.Right),
                        $"{w}x{h} {id}: the value editor starts under the name column / divider");
                    Assert.That(boxRect.Right, Is.LessThanOrEqualTo(valueCell.Right + 0.5),
                        $"{w}x{h} {id}: the value editor overruns its column on the right");
                    Assert.That(textRect.Left, Is.GreaterThanOrEqualTo(Math.Max(boxRect.Left, divider.Right)),
                        $"{w}x{h} {id}: the text's first character is hidden");
                });
            }
        }
    }

    /// <summary>
    /// The same rule for EVERY row's editor — text boxes, choice lists, spinners, switches — on a WinForms
    /// control, whose rows include the choice lists (MinWidth 170 in the shared editor) a web control lacks.
    /// </summary>
    [AvaloniaTest]
    public void EveryRowsEditor_FitsBetweenTheDividerAndTheColumnsRightEdge()
    {
        using var rig = Open(WinDoc, "WinForm", FormTarget.WinForms, 1200, 830);
        rig.Select("btn");
        var checkedKinds = new Dictionary<string, int>();

        Assert.Multiple(() =>
        {
            foreach (var row in rig.GridVm.Rows.ToList())
            {
                var container = rig.Container(row);
                var divider = rig.InWindow(rig.Divider(container));
                var cell = rig.ValuePanel(container);
                var cellRect = rig.InWindow(cell);
                var editors = cell.GetVisualDescendants().OfType<Control>()
                    .Where(c => c is TextBox or ComboBox or NumericUpDown or ToggleSwitch or FormColorDropDown
                        or Button { Name: "FontEllipsis" })
                    .Where(c => c.IsEffectivelyVisible && c.Bounds.Width > 0)
                    .Where(c => c.FindAncestorOfType<NumericUpDown>() == null); // its inner box is its own business
                foreach (var editor in editors)
                {
                    var kind = editor is Button { Name: { } named } ? named : editor.GetType().Name;
                    checkedKinds[kind] = checkedKinds.GetValueOrDefault(kind) + 1;
                    var r = rig.InWindow(editor);
                    Assert.That(r.Left, Is.GreaterThanOrEqualTo(divider.Right), $"{row.Name}: {kind} starts left of the divider ({r})");
                    Assert.That(r.Right, Is.LessThanOrEqualTo(cellRect.Right + 0.5), $"{row.Name}: {kind} overruns its column ({r})");
                }
            }
        });

        TestContext.WriteLine($"[editors checked] {string.Join(", ", checkedKinds.Select(k => $"{k.Key}={k.Value}"))}");
        // Slice 4 D-1 / D-2: the colour rows' swatch drop-down and the Font row's `…` sit in the same cell, beside the text box.
        Assert.That(checkedKinds.Keys, Is.SupersetOf(new[] { "TextBox", "ComboBox", "NumericUpDown", nameof(FormColorDropDown), "FontEllipsis" }),
            "precondition: the sweep saw every kind of editor that has a minimum width");
        // Slice 4 D-3: a Bool row is the True/False drop-down (counted as a ComboBox above); the shared editor's switch is
        // the Settings dialog's and never renders in the grid.
        Assert.That(checkedKinds.ContainsKey(nameof(ToggleSwitch)), Is.False, "no Bool row renders as a switch");
    }

    // ==================================================================
    // 3. The code editor never shows through the design surface
    // ==================================================================

    /// <summary>
    /// ⛔ The owner saw the .blwebform's XML behind the toolbox and around the canvas. The design surface is an
    /// overlay ABOVE the code editor, so the editor under it was still laid out and rendered every frame — and
    /// anything less than opaque on top (a theme brush, a panel left transparent) let it through. In design mode
    /// no code editor is visible at all now; the covering panels still resolve opaque Ide* brushes, and the
    /// pixels in the toolbox column and the canvas gap are those brushes. Leaving design mode shows the editor
    /// again.
    /// </summary>
    [AvaloniaTest]
    public void InDesignMode_NoCodeEditorIsVisible_AndTheSurfaceIsOpaqueIdeBrushes_AtTwoSizes()
    {
        foreach (var (w, h) in TwoSizes)
        {
            using var rig = OpenWeb(w, h);
            var editors = rig.View.GetVisualDescendants().OfType<CodeEditorControl>().ToList();
            Assume.That(editors, Is.Not.Empty, "precondition: the view hosts its code editors");

            var toolboxRect = rig.InWindow(rig.Toolbox);
            var canvasRect = rig.InWindow(rig.Canvas);
            // The empty right end of the toolbox's caption row (a point among the rows lands on a glyph box once
            // the list fills a short column), and just inside the canvas's own margin (the form is centred in it).
            var toolboxGap = new Point(toolboxRect.Right - 8, toolboxRect.Top - 12);
            var canvasGap = new Point(canvasRect.X + 12, canvasRect.Bottom - 12);

            var panelBg = (ISolidColorBrush)rig.Window.FindResource(ThemeVariant.Dark, "IdePanelBg")!;
            var bg = (ISolidColorBrush)rig.Window.FindResource(ThemeVariant.Dark, "IdeBg")!;

            Color toolboxPixel, canvasPixel;
            using (var frame = rig.Window.CaptureRenderedFrame()!)
            {
                toolboxPixel = PixelAt(frame, toolboxGap);
                canvasPixel = PixelAt(frame, canvasGap);
            }

            TestContext.WriteLine($"[{w}x{h}] toolbox gap {toolboxGap} = {toolboxPixel}; canvas gap {canvasGap} = {canvasPixel}");

            Assert.Multiple(() =>
            {
                foreach (var editor in editors)
                {
                    Assert.That(editor.IsEffectivelyVisible, Is.False,
                        $"{w}x{h}: code editor '{editor.Name}' is still visible (and rendering) under the design surface");
                }

                foreach (var at in new[] { toolboxGap, canvasGap })
                {
                    var hit = rig.Window.InputHitTest(at) as Visual;
                    Assert.That(hit?.FindAncestorOfType<CodeEditorControl>(includeSelf: true), Is.Null,
                        $"{w}x{h}: a press at {at} reaches the code editor");
                }

                Assert.That(panelBg.Color.A, Is.EqualTo(255), "IdePanelBg is opaque");
                Assert.That(bg.Color.A, Is.EqualTo(255), "IdeBg is opaque");
                Assert.That(toolboxPixel, Is.EqualTo(panelBg.Color), $"{w}x{h}: the toolbox column draws IdePanelBg");
                Assert.That(canvasPixel, Is.EqualTo(bg.Color), $"{w}x{h}: the gap around the form draws IdeBg");
            });

            // Back to Code: the editor comes back.
            rig.Vm.ToggleDesignModeCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            rig.Window.UpdateLayout();
            Assert.That(rig.View.FindControl<CodeEditorControl>("MainEditor")!.IsEffectivelyVisible, Is.True,
                $"{w}x{h}: Code view shows the editor again");
        }
    }

    private static Color PixelAt(WriteableBitmap frame, Point at)
    {
        using var fb = frame.Lock();
        var scale = frame.Size.Width > 0 ? fb.Size.Width / frame.Size.Width : 1;
        var x = (int)(at.X * scale);
        var y = (int)(at.Y * scale);
        var offset = y * fb.RowBytes + x * 4;
        var px = new byte[4];
        System.Runtime.InteropServices.Marshal.Copy(fb.Address + offset, px, 0, 4);
        return fb.Format == PixelFormat.Rgba8888
            ? Color.FromArgb(px[3], px[0], px[1], px[2])
            : Color.FromArgb(px[3], px[2], px[1], px[0]); // Bgra8888
    }

    // ==================================================================
    // 4. Every row is reachable
    // ==================================================================

    /// <summary>
    /// ComboBox1 on a web form has rows down to Layout's Col/Row. At a short panel height the list must SCROLL
    /// — with real mouse-wheel input — until the last row is realised and inside the list's viewport. (The
    /// Fluent scroll bar is an overlay that appears on hover, which is why none was visible in the screenshot.)
    /// </summary>
    [AvaloniaTest]
    public void TheWheelScrollsThePropertyList_UntilTheLastRowIsRealisedAndVisible_AtTwoSizes()
    {
        foreach (var (w, h) in TwoSizes)
        {
            using var rig = OpenWeb(w, h);
            rig.Select("ComboBox1");
            var names = rig.GridVm.Rows.Select(r => r.Name).ToList();
            Assert.That(names, Is.SupersetOf(new[] { "Visible", "Items", "Name", "Col", "Row" }),
                "precondition: ComboBox1's rows run below TabIndex");

            var scroller = rig.List.GetVisualDescendants().OfType<ScrollViewer>().First();
            var listRect = rig.InWindow(rig.List);
            var wheelAt = new Point(listRect.X + listRect.Width / 2, listRect.Y + listRect.Height / 2);
            TestContext.WriteLine($"[{w}x{h}] extent {scroller.Extent.Height:0} viewport {scroller.Viewport.Height:0}");

            for (var i = 0; i < 60; i++)
            {
                var before = scroller.Offset.Y;
                rig.Window.MouseWheel(wheelAt, new Vector(0, -1));
                Dispatcher.UIThread.RunJobs();
                rig.Window.UpdateLayout();
                if (Math.Abs(scroller.Offset.Y - before) < 0.01) break;
            }

            var last = rig.GridVm.DisplayItems.Last();
            var container = rig.List.ContainerFromItem(last) as Control;
            Assert.That(container, Is.Not.Null, $"{w}x{h}: the last row ({(last as FormPropertyRow)?.Name}) is realised after scrolling");
            var lastRect = rig.InWindow(container!);
            Assert.Multiple(() =>
            {
                Assert.That(lastRect.Bottom, Is.LessThanOrEqualTo(listRect.Bottom + 0.5), $"{w}x{h}: the last row is inside the viewport");
                Assert.That(lastRect.Top, Is.GreaterThanOrEqualTo(listRect.Top - 0.5), $"{w}x{h}: the last row is inside the viewport");
            });

            if (h < 700)
            {
                Assert.That(scroller.Extent.Height, Is.GreaterThan(scroller.Viewport.Height),
                    $"{w}x{h}: precondition: the short panel really had to scroll");
            }
        }
    }
}
