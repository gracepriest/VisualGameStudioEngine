using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
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
using VisualGameStudio.Shell.Controls;
using VisualGameStudio.Shell.ViewModels.Designer;
using VisualGameStudio.Shell.ViewModels.Documents;
using VisualGameStudio.Shell.Views.Controls;
using VisualGameStudio.Shell.Views.Documents;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// Spec 2026-09-27 §7 "Canvas (headless, real IDE view)": a Canvas web page driven through the REAL
/// <see cref="CodeEditorDocumentView"/> (AppStyles loaded, the real view model, a dictionary "disk") — because every
/// piece-level designer test passed while the owner hit real bugs (MEMORY: host the REAL view).
///
/// <para>⛔ Gestures run at TWO window sizes and each asserts one of them is not 1:1 (<c>ToForm</c> is the identity at
/// 1.0, where a missing mapping hides). ⛔ Every window is CLOSED. ⛔ Repeated presses are at different points (two
/// presses at one point with no time between are a double-click). The chrome's pixel rules are proven at the canvas
/// level (<see cref="FormCanvasPixelPageTests"/>, pre-flight B7); here only the title-bar and grip samples repeat.</para>
/// </summary>
[TestFixture]
public class FormPixelPageRealViewTests
{
    private const string Dir = "/proj/";

    private static readonly Color Navy = Color.FromRgb(0x00, 0x00, 0x80);

    private static readonly (double Width, double Height)[] TwoSizes = { (800, 560), (1400, 900) };

    /// <summary>A MenuStrip (24), a Dock=Top Panel (resolved 0,24,800,40 — its stored X/Y stale by design), a Button.</summary>
    private const string CanvasDoc = """
        <WebForm Name="PixelPage" Version="1" Width="800" Height="450" Text="PixelPage">
          <Layout Kind="Canvas" MobileBreakpoint="600"/>
          <Controls>
            <MenuStrip Id="menuStrip1" Dock="Top"/>
            <Panel Id="pnl" X="300" Y="300" Width="120" Height="40" Dock="Top" TabIndex="0"/>
            <Button Id="btn" X="40" Y="120" Width="75" Height="23" TabIndex="1" Text="Go"/>
          </Controls>
        </WebForm>
        """;

    private const string GridDoc = """
        <WebForm Name="PixelPage" Version="1" Text="PixelPage">
          <Layout Kind="Grid" Cols="1fr,1fr" Rows="1fr,1fr" Gap="0px"/>
          <Controls>
            <Button Id="btn" Col="0" Row="0" TabIndex="0" Text="Go"/>
          </Controls>
        </WebForm>
        """;

    private sealed class Files
    {
        public readonly Dictionary<string, string> Contents = new(StringComparer.Ordinal);

        public IFileService Service
        {
            get
            {
                var mock = new Mock<IFileService>();
                mock.Setup(f => f.ReadFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .Returns((string p, CancellationToken _) => Task.FromResult(Contents[p]));
                mock.Setup(f => f.FileExistsAsync(It.IsAny<string>()))
                    .Returns((string p) => Task.FromResult(Contents.ContainsKey(p)));
                mock.Setup(f => f.WriteFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .Returns((string p, string text, CancellationToken _) =>
                    {
                        Contents[p] = text;
                        return Task.CompletedTask;
                    });
                return mock.Object;
            }
        }
    }

    private sealed class Rig : IDisposable
    {
        public required CodeEditorDocumentViewModel Vm { get; init; }
        public required Window Window { get; init; }
        public required FormPropertyGridView Grid { get; init; }
        public required FormCanvasControl Canvas { get; init; }

        public FormPropertyGridViewModel GridVm => Vm.PropertyGrid;
        public ListBox List => Grid.FindControl<ListBox>("PropertyList")!;
        public TextBox Search => Grid.FindControl<TextBox>("SearchBox")!;
        public FormDocument Doc => Vm.DesignDocument!;
        public FormControl Control(string id) => Doc.FindById(id)!;
        /// <summary>A top-level row, or a composite's part with its parent expanded first (slice 3: X is Location's part).</summary>
        public FormPropertyRow Row(string name)
        {
            var row = GridVm.Rows.SingleOrDefault(r => r.Name == name) ?? GridVm.AllRows().Single(r => r.Name == name);
            if (row.Parent is { IsExpanded: false } parent)
            {
                parent.IsExpanded = true;
                Window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
            }

            return row;
        }
        private FormCanvasTransform Fit => FormCanvasControl.Fit(Doc, Canvas.Bounds.Size);
        public double Zoom => Fit.Zoom;

        /// <summary>A FORM point, in window coordinates — through the canvas's own Fit.</summary>
        public Point InWindow(double formX, double formY) =>
            Canvas.TranslatePoint(Fit.ToCanvas(new Point(formX, formY)), Window)
            ?? throw new InvalidOperationException("the canvas is not in the window");

        public Rect SurfaceInWindow()
        {
            var size = FormCanvasTransform.SurfaceSize(Doc);
            var onCanvas = Fit.ToCanvas(new Rect(0, 0, size.Width, size.Height));
            return new Rect(Canvas.TranslatePoint(onCanvas.TopLeft, Window)!.Value, onCanvas.Size);
        }

        public void Click(Point at)
        {
            Window.MouseDown(at, MouseButton.Left);
            Window.MouseUp(at, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }

        public void Click(Visual target, double dx = 0) =>
            Click(target.TranslatePoint(new Point(target.Bounds.Width / 2 + dx, target.Bounds.Height / 2), Window)
                  ?? throw new InvalidOperationException($"{target} is not in the window"));

        /// <summary>A left-button drag by FORM units — scaled by the zoom, so it means the same at every size.</summary>
        public void Drag(Point from, double formDx, double formDy)
        {
            var to = from + new Point(formDx * Zoom, formDy * Zoom);
            Window.MouseDown(from, MouseButton.Left);
            Window.MouseMove(to, RawInputModifiers.LeftMouseButton);
            Window.MouseUp(to, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }

        /// <summary>
        /// A toolbox drop, as the toolbox packs it (CodeEditorDocumentView.axaml.cs:2180-2181). ⚠ Avalonia.Headless's
        /// DragDrop (pre-flight B8) — if 11.3.13 lacks it, execute Canvas.DropCommand with the snapped request instead
        /// and say so here.
        /// </summary>
        public void DropFromToolbox(string kind, Point at)
        {
            var data = new DataObject();
            data.Set(FormCanvasControl.ControlKindFormat, kind);
            Window.DragDrop(at, RawDragEventType.DragEnter, data, DragDropEffects.Copy);
            Window.DragDrop(at, RawDragEventType.DragOver, data, DragDropEffects.Copy);
            Window.DragDrop(at, RawDragEventType.Drop, data, DragDropEffects.Copy);
            Dispatcher.UIThread.RunJobs();
        }

        public ListBoxItem Container(object item)
        {
            List.ScrollIntoView(item);
            Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            return (ListBoxItem?)List.ContainerFromItem(item)
                   ?? throw new InvalidOperationException($"{item} is not realised in the real grid");
        }

        /// <summary>Types into a row's real NumericUpDown and clicks away (the LostFocus commit).</summary>
        public void TypeInto(FormPropertyRow row, string value, double dx)
        {
            var spinner = Container(row).GetVisualDescendants().OfType<NumericUpDown>().Single(n => n.IsEffectivelyVisible);
            var text = spinner.GetVisualDescendants().OfType<TextBox>().First();
            Click(text, dx);
            Window.KeyPress(Key.A, RawInputModifiers.Control);
            Window.KeyRelease(Key.A, RawInputModifiers.Control);
            Window.KeyTextInput(value);
            Dispatcher.UIThread.RunJobs();
            Click(Search, dx);
            Window.UpdateLayout();
        }

        public void Dispose() => Window.Close();
    }

    private static Rig Open(string doc, double width = 1000, double height = 700)
    {
        var scaffold = FormScaffolder.Create("PixelPage", FormTarget.Web);
        var files = new Files();
        files.Contents[Dir + scaffold.DocumentFileName] = doc;
        files.Contents[Dir + scaffold.CodeFileName] = scaffold.CodeText;

        var vm = new CodeEditorDocumentViewModel(files.Service, new Mock<IEventAggregator>().Object)
        {
            FilePath = Dir + scaffold.DocumentFileName
        };
        vm.SetContent(doc);
        Assert.That(vm.EnterDesignModeForFormDocument(), Is.True, "precondition: the designer must open");

        var view = new CodeEditorDocumentView { DataContext = vm };
        // ⚠ Dark, exactly as FormDesignerLayoutRealViewTests' rig: the colour assertions resolve IdeBg under Dark, and a
        // window left on the default variant renders the Light dictionary's IdeBg (#F5F5F5) instead.
        var window = new Window
        {
            Width = width, Height = height, Content = view, RequestedThemeVariant = ThemeVariant.Dark
        };
        window.Styles.Add(new StyleInclude(new Uri("avares://VisualGameStudio/"))
        {
            Source = new Uri("avares://VisualGameStudio/Resources/Styles/AppStyles.axaml")
        });

        try
        {
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var grid = view.FindControl<FormPropertyGridView>("PropertyGridView")
                       ?? throw new InvalidOperationException("PropertyGridView not found");
            var canvas = view.FindControl<FormCanvasControl>("DesignCanvas")
                         ?? throw new InvalidOperationException("DesignCanvas not found");
            return new Rig { Vm = vm, Window = window, Grid = grid, Canvas = canvas };
        }
        catch
        {
            window.Close();
            throw;
        }
    }

    private static Color PixelAt(WriteableBitmap frame, Point at)
    {
        using var fb = frame.Lock();
        var scale = frame.Size.Width > 0 ? fb.Size.Width / frame.Size.Width : 1;
        var px = new byte[4];
        Marshal.Copy(fb.Address + ((int)(at.Y * scale) * fb.RowBytes) + ((int)(at.X * scale) * 4), px, 0, 4);
        return fb.Format == PixelFormat.Rgba8888
            ? Color.FromArgb(px[3], px[0], px[1], px[2])
            : Color.FromArgb(px[3], px[2], px[1], px[0]);
    }

    private static PixelGeometry Pixel(FormControl control) => (PixelGeometry)control.Geometry!;

    [AvaloniaTest]
    public void TheControlsSitAtTheirPixels_AndTheDockedPanelWhereItRuns_AtTwoSizes()
    {
        var zooms = new List<double>();
        foreach (var (w, h) in TwoSizes)
        {
            using var rig = Open(CanvasDoc, w, h);
            zooms.Add(rig.Zoom);

            rig.Click(rig.InWindow(40 + 37, 120 + 11));   // the Button's STORED rectangle
            var afterButton = rig.Vm.Selection.Primary?.Id;
            rig.Click(rig.InWindow(600, 44));              // the Panel as DOCKED: (0,24,800,40)
            var afterPanel = rig.Vm.Selection.Primary?.Id;
            rig.Click(rig.InWindow(360, 320));             // the Panel's stale STORED rectangle — nothing there
            var afterStale = rig.Vm.Selection.Primary;

            Assert.Multiple(() =>
            {
                Assert.That(afterButton, Is.EqualTo("btn"), $"{w}x{h}: drawn at its pixels");
                Assert.That(afterPanel, Is.EqualTo("pnl"), $"{w}x{h}: the docked Panel is drawn where it runs");
                Assert.That(afterStale, Is.Null, $"{w}x{h}: nothing is drawn at the stale stored X/Y");
            });
        }

        Assert.That(zooms, Has.Some.Not.EqualTo(1.0), "at least one size is not 1:1");
    }

    [AvaloniaTest]
    public void AToolboxDrop_LandsUnderThePointer_InPixels_AtTwoSizes()
    {
        var zooms = new List<double>();
        foreach (var (w, h) in TwoSizes)
        {
            using var rig = Open(CanvasDoc, w, h);
            zooms.Add(rig.Zoom);

            rig.DropFromToolbox("Button", rig.InWindow(200, 208));

            var dropped = rig.Doc.FindById("Button1");
            Assert.Multiple(() =>
            {
                Assert.That(dropped?.Geometry, Is.InstanceOf<PixelGeometry>(), $"{w}x{h}: a Canvas page drops in pixels");
                Assert.That((Pixel(dropped!).X, Pixel(dropped!).Y), Is.EqualTo((200, 208)), $"{w}x{h}: under the pointer");
                Assert.That(rig.Vm.Text, Does.Contain("Id=\"Button1\"").And.Contain("X=\"200\"").And.Contain("Y=\"208\""));
                Assert.That(rig.Vm.Selection.Primary, Is.SameAs(dropped), $"{w}x{h}: and is selected");
            });
        }

        Assert.That(zooms, Has.Some.Not.EqualTo(1.0), "at least one size is not 1:1");
    }

    [AvaloniaTest]
    public void DraggingMovesAControl_AndItsHandleResizesIt_AndBothReachTheFile_AtTwoSizes()
    {
        var zooms = new List<double>();
        foreach (var (w, h) in TwoSizes)
        {
            using var rig = Open(CanvasDoc, w, h);
            zooms.Add(rig.Zoom);
            var btn = rig.Control("btn");

            rig.Drag(rig.InWindow(77, 131), 40, 16);                  // snapped: (80, 136)
            var moved = (Pixel(btn).X, Pixel(btn).Y);

            rig.Drag(rig.InWindow(80 + 75, 136 + 23), 24, 8);          // its bottom-right handle; resizes are not snapped
            var sized = (Pixel(btn).X, Pixel(btn).Y, Pixel(btn).Width, Pixel(btn).Height);

            Assert.Multiple(() =>
            {
                Assert.That(moved, Is.EqualTo((80, 136)), $"{w}x{h}: the drag moved it");
                Assert.That(sized, Is.EqualTo((80, 136, 99, 31)), $"{w}x{h}: the handle resized it");
                Assert.That(rig.Vm.Text, Does.Contain("X=\"80\"").And.Contain("Y=\"136\"").And.Contain("Width=\"99\""),
                    $"{w}x{h}: both were committed to the file");
            });
        }

        Assert.That(zooms, Has.Some.Not.EqualTo(1.0), "at least one size is not 1:1");
    }

    [AvaloniaTest]
    public void GridEditsOfLocationAnchorAndDock_RedrawAndReachTheFile()
    {
        using var rig = Open(CanvasDoc);
        rig.Click(rig.InWindow(77, 131));
        Assume.That(rig.GridVm.SelectedControl?.Id, Is.EqualTo("btn"), "precondition: btn selected on the canvas");
        var btn = rig.Control("btn");

        rig.TypeInto(rig.Row("X"), "200", 0);
        var afterX = FormCanvasTransform.Layout(rig.Doc).Single(e => ReferenceEquals(e.Control, btn)).Bounds;

        // ⚠ Set through the rows (their pickers' own gestures are FormAnchorDockPickerTests'); this is the redraw.
        rig.Row("Anchor").StringValue = "Top, Right";
        rig.Row("Dock").StringValue = "Bottom";
        Dispatcher.UIThread.RunJobs();

        Assert.That(FormDockLayout.Resolve(rig.Doc).TryGet(btn, out var docked), Is.True);
        var afterDock = FormCanvasTransform.Layout(rig.Doc).Single(e => ReferenceEquals(e.Control, btn)).Bounds;
        rig.Click(rig.InWindow(400, 438));

        Assert.Multiple(() =>
        {
            Assert.That(afterX, Is.EqualTo(new Rect(200, 120, 75, 23)), "X typed in the real editor moved it");
            Assert.That(afterDock, Is.EqualTo(new Rect(docked.Bounds.X, docked.Bounds.Y, docked.Bounds.Width, docked.Bounds.Height)));
            Assert.That(afterDock, Is.EqualTo(new Rect(0, 427, 800, 23)), "Dock=Bottom: the bottom edge, full width");
            Assert.That(rig.Vm.Selection.Primary, Is.SameAs(btn), "and it is hit where it is now drawn");
            Assert.That(rig.Vm.Text, Does.Contain("X=\"200\"").And.Contain("Dock=\"Bottom\"").And.Contain("Anchor=\"Top, Right\""));
        });
    }

    [AvaloniaTest]
    public void ThePageHasGripsButNoTitleBar_AndAGripDragResizesTheDesignSize_AtTwoSizes()
    {
        var zooms = new List<double>();
        foreach (var (w, h) in TwoSizes)
        {
            using var rig = Open(CanvasDoc, w, h);
            zooms.Add(rig.Zoom);
            var surface = rig.SurfaceInWindow();
            var bg = (ISolidColorBrush)rig.Window.FindResource(ThemeVariant.Dark, "IdeBg")!;

            Color above, grip;
            using (var frame = rig.Window.CaptureRenderedFrame()!)
            {
                above = PixelAt(frame, new Point(surface.Center.X, surface.Y - 9));
                grip = PixelAt(frame, new Point(surface.Right, surface.Center.Y));
            }

            rig.Drag(rig.InWindow(800, 225), 80, 0);

            Assert.Multiple(() =>
            {
                Assert.That(above, Is.EqualTo(bg.Color), $"{w}x{h}: no title bar above a page (spec §2.4)");
                Assert.That(grip, Is.EqualTo(Navy), $"{w}x{h}: the right-hand form grip");
                Assert.That(rig.Doc.Width, Is.EqualTo(880), $"{w}x{h}: the grip resized the design size");
                Assert.That(rig.Vm.Text, Does.Contain("Version=\"1\" Width=\"880\" Height=\"450\""),
                    $"{w}x{h}: and the page's ClientSize reached the file");
            });
        }

        Assert.That(zooms, Has.Some.Not.EqualTo(1.0), "at least one size is not 1:1");
    }

    [AvaloniaTest]
    public void ADockedPanel_ShowsNoHandles_AndADragOnItOnlySelectsIt_AtTwoSizes()
    {
        var zooms = new List<double>();
        foreach (var (w, h) in TwoSizes)
        {
            using var rig = Open(CanvasDoc, w, h);
            zooms.Add(rig.Zoom);
            var before = rig.Vm.Text;
            var pnl = rig.Control("pnl");

            rig.Drag(rig.InWindow(600, 44), 60, 80);

            Color handle;
            using (var frame = rig.Window.CaptureRenderedFrame()!)
            {
                handle = PixelAt(frame, rig.InWindow(0, 44));   // its left-middle handle's centre, where it is DRAWN
            }

            Assert.Multiple(() =>
            {
                Assert.That(rig.Vm.Selection.Primary, Is.SameAs(pnl), $"{w}x{h}: the press selected it");
                Assert.That((Pixel(pnl).X, Pixel(pnl).Y), Is.EqualTo((300, 300)), $"{w}x{h}: the drag moved nothing");
                Assert.That(rig.Vm.Text, Is.EqualTo(before), $"{w}x{h}: nothing was written");
                Assert.That(handle, Is.Not.EqualTo(Navy), $"{w}x{h}: no handles on a docked control (spec §7a)");
            });
        }

        Assert.That(zooms, Has.Some.Not.EqualTo(1.0), "at least one size is not 1:1");
    }

    [AvaloniaTest]
    public void TheFormsRows_AreClientSizeAndMobileBreakpoint_OnACanvasPage_AndTheTracks_OnAGridPage()
    {
        List<string> canvasRows, gridRows;
        bool breakpointRealised;
        using (var rig = Open(CanvasDoc))
        {
            canvasRows = rig.GridVm.Rows.Select(r => r.Name).ToList();
            rig.Container(rig.Row("MobileBreakpoint"));
            breakpointRealised = rig.List.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "MobileBreakpoint");
        }

        using (var rig = Open(GridDoc))
        {
            gridRows = rig.GridVm.Rows.Select(r => r.Name).ToList();
        }

        Assert.Multiple(() =>
        {
            Assert.That(canvasRows, Does.Contain("ClientSize").And.Contain("MobileBreakpoint"));
            Assert.That(canvasRows, Does.Not.Contain("Cols").And.Not.Contain("Rows").And.Not.Contain("Gap"));
            Assert.That(breakpointRealised, Is.True, "the row is in the REAL list, not only the view model");
            Assert.That(gridRows, Does.Contain("Cols").And.Contain("Rows").And.Contain("Gap"));
            Assert.That(gridRows, Does.Not.Contain("ClientSize").And.Not.Contain("MobileBreakpoint"));
        });
    }

    [AvaloniaTest]
    public void ANegativeMobileBreakpoint_SnapsTheRealEditorBack_AndThePaneSaysWhy()
    {
        using var rig = Open(CanvasDoc);
        var row = rig.Row("MobileBreakpoint");
        var before = rig.Vm.Text;
        var edits = 0;
        rig.GridVm.Edited += (_, _) => edits++;

        rig.TypeInto(row, "-5", 0);

        var spinner = rig.Container(row).GetVisualDescendants().OfType<NumericUpDown>().Single(n => n.IsEffectivelyVisible);
        Assert.Multiple(() =>
        {
            Assert.That(spinner.Value, Is.EqualTo(600m), "snapped back to what the page holds");
            Assert.That(rig.Vm.Text, Is.EqualTo(before), "never written");
            Assert.That(edits, Is.Zero, "a refusal is not an edit");
            Assert.That(rig.GridVm.DescriptionTitle, Is.EqualTo("MobileBreakpoint"));
            Assert.That(rig.GridVm.DescriptionBody, Does.Contain("'-5'").And.Contain("not applied"),
                "the plan's review note: the reason is SHOWN, not silently dropped");
        });
    }
}
