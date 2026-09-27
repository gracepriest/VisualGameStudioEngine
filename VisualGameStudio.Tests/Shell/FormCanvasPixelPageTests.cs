using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using BasicLang.Forms;
using NUnit.Framework;
using VisualGameStudio.Shell.Controls;
using VisualGameStudio.Shell.ViewModels.Designer;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// Spec 2026-09-27 §2.4 / §7a, driven through the real <see cref="FormCanvasControl"/> and its real pixels: a Canvas web
/// page gets the alignment grid and the form grips but no window frame, and a DOCKED control has no handles and cannot
/// be dragged. Canvas-level on purpose (pre-flight B7): the real document view cannot pin the zoom, and the alignment
/// dots vanish below 0.5. The two windows below fit an 800x450 page at 0.815 and at 1:1.
/// </summary>
[TestFixture]
public class FormCanvasPixelPageTests
{
    private static readonly Color Navy = Color.FromRgb(0x00, 0x00, 0x80);
    private static readonly Color Face = Color.FromRgb(0xD4, 0xD0, 0xC8);

    /// <summary>(700,500) fits 800x450 at 0.815; (900,600) at 1.0 (Fit never enlarges past 1:1).</summary>
    private static readonly (double Width, double Height)[] TwoZooms = { (700, 500), (900, 600) };

    private sealed class Recorder : System.Windows.Input.ICommand
    {
        public int Executions { get; private set; }

        public event EventHandler? CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => Executions++;
    }

    private static FormDocument Page(FormLayoutKind kind) => new()
    {
        Target = FormTarget.Web,
        Name = "P",
        Width = 800,
        Height = 450,
        Layout = kind == FormLayoutKind.Grid
            ? new FormLayout { Kind = kind, Cols = "1fr,1fr", Rows = "1fr,1fr", Gap = "0px" }
            : new FormLayout { Kind = kind }
    };

    private static FormDocument AWindow() => new() { Target = FormTarget.WinForms, Name = "P", Width = 800, Height = 450 };

    /// <summary>A Canvas page: a MenuStrip, a Dock=Top Panel (stored X/Y stale by design) and an undocked Button.</summary>
    private static (FormDocument Doc, FormControl Panel, FormControl Button) DockedPage()
    {
        var doc = Page(FormLayoutKind.Canvas);
        var panel = new FormControl
        {
            Kind = "Panel", Id = "pnl",
            Geometry = new PixelGeometry { X = 300, Y = 300, Width = 120, Height = 40, Dock = "Top" }
        };
        var button = new FormControl
        {
            Kind = "Button", Id = "btn",
            Geometry = new PixelGeometry { X = 40, Y = 120, Width = 75, Height = 23 }
        };
        doc.Controls.Add(new FormControl { Kind = "MenuStrip", Id = "menuStrip1" });
        doc.Controls.Add(panel);
        doc.Controls.Add(button);
        return (doc, panel, button);
    }

    private sealed class Surface : IDisposable
    {
        public required FormCanvasControl Canvas { get; init; }
        public required Window Window { get; init; }
        public required FormDocument Doc { get; init; }

        private FormCanvasTransform Fit => FormCanvasControl.Fit(Doc, Canvas.Bounds.Size);

        /// <summary>The form's client rectangle in canvas (= window) pixels, through the canvas's own Fit.</summary>
        public Rect SurfaceRect
        {
            get
            {
                var size = FormCanvasTransform.SurfaceSize(Doc);
                return Fit.ToCanvas(new Rect(0, 0, size.Width, size.Height));
            }
        }

        /// <summary>A FORM point in canvas (= window) pixels.</summary>
        public Point At(double x, double y) => Fit.ToCanvas(new Point(x, y));

        public T Frame<T>(Func<WriteableBitmap, T> read)
        {
            using var frame = Window.CaptureRenderedFrame()
                ?? throw new InvalidOperationException("No rendered frame — Skia is required (DesignerHeadlessApp).");
            return read(frame);
        }

        public void Drag(Point from, Point to)
        {
            Window.MouseDown(from, MouseButton.Left);
            Window.MouseMove(to, RawInputModifiers.LeftMouseButton);
            Window.MouseUp(to, MouseButton.Left);
        }

        public void Dispose() => Window.Close();
    }

    private static Surface Open(
        FormDocument doc, double width, double height, FormControl? selected = null, FormSelection? selection = null)
    {
        var canvas = new FormCanvasControl { Document = doc, SelectedControl = selected, Selection = selection };
        var window = new Window { Width = width, Height = height, Content = canvas };
        window.Show();
        canvas.Focus();
        return new Surface { Canvas = canvas, Window = window, Doc = doc };
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

    private static int CountOtherThan(WriteableBitmap frame, Rect region, Color colour)
    {
        using var fb = frame.Lock();
        var scale = frame.Size.Width > 0 ? fb.Size.Width / frame.Size.Width : 1;
        var row = new byte[fb.RowBytes];
        var count = 0;
        for (var y = (int)Math.Ceiling(region.Y * scale); y < (int)(region.Bottom * scale); y++)
        {
            Marshal.Copy(fb.Address + (y * fb.RowBytes), row, 0, fb.RowBytes);
            for (var x = (int)Math.Ceiling(region.X * scale); x < (int)(region.Right * scale); x++)
            {
                var c = fb.Format == PixelFormat.Rgba8888
                    ? Color.FromArgb(row[x * 4 + 3], row[x * 4], row[x * 4 + 1], row[x * 4 + 2])
                    : Color.FromArgb(row[x * 4 + 3], row[x * 4 + 2], row[x * 4 + 1], row[x * 4]);
                if (c != colour)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static string Hash(WriteableBitmap frame)
    {
        using var stream = new MemoryStream();
        frame.Save(stream);
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    // ==================================================================
    // The page's chrome
    // ==================================================================

    [AvaloniaTest]
    public void ACanvasPage_HasNoTitleBar_WhileAWindowDoes_AtTwoZooms()
    {
        foreach (var (w, h) in TwoZooms)
        {
            // ⚠ The MIDDLE of where an 18px title bar would be: a page's caption is drawn at the left (B6).
            Color window, page;
            using (var s = Open(AWindow(), w, h))
            {
                window = s.Frame(f => PixelAt(f, new Point(s.SurfaceRect.Center.X, s.SurfaceRect.Y - 9)));
            }

            using (var s = Open(Page(FormLayoutKind.Canvas), w, h))
            {
                page = s.Frame(f => PixelAt(f, new Point(s.SurfaceRect.Center.X, s.SurfaceRect.Y - 9)));
            }

            Assert.Multiple(() =>
            {
                Assert.That(window, Is.EqualTo(Navy), $"{w}x{h}: control — the sample lands in a WINDOW's title bar");
                Assert.That(page, Is.Not.EqualTo(Navy), $"{w}x{h}: a page is not a window (spec §2.4)");
            });
        }
    }

    [AvaloniaTest]
    public void ACanvasPage_IsDesignedOnTheAlignmentGrid_AFlowPageIsNot_AtTwoZooms()
    {
        foreach (var (w, h) in TwoZooms)
        {
            int canvasDots, flowDots;
            using (var s = Open(Page(FormLayoutKind.Canvas), w, h))
            {
                canvasDots = s.Frame(f => CountOtherThan(f, s.SurfaceRect.Deflate(6), Face));
            }

            using (var s = Open(Page(FormLayoutKind.Flow), w, h))
            {
                flowDots = s.Frame(f => CountOtherThan(f, s.SurfaceRect.Deflate(6), Face));
            }

            Assert.Multiple(() =>
            {
                Assert.That(flowDots, Is.Zero,
                    $"{w}x{h}: control — an empty Flow page's face is plain, so the counter can see absence");
                Assert.That(canvasDots, Is.GreaterThan(100), $"{w}x{h}: the alignment dots (spec §2.4)");
            });
        }
    }

    [AvaloniaTest]
    public void ACanvasPage_HasTheFormGrips_AGridPageDoesNot_AtTwoZooms()
    {
        foreach (var (w, h) in TwoZooms)
        {
            Color canvasGrip, gridGrip;
            using (var s = Open(Page(FormLayoutKind.Canvas), w, h))
            {
                canvasGrip = s.Frame(f => PixelAt(f, new Point(s.SurfaceRect.Right, s.SurfaceRect.Center.Y)));
            }

            using (var s = Open(Page(FormLayoutKind.Grid), w, h))
            {
                gridGrip = s.Frame(f => PixelAt(f, new Point(s.SurfaceRect.Right, s.SurfaceRect.Center.Y)));
            }

            Assert.Multiple(() =>
            {
                Assert.That(canvasGrip, Is.EqualTo(Navy), $"{w}x{h}: the right-hand form grip's centre");
                Assert.That(gridGrip, Is.Not.EqualTo(Navy), $"{w}x{h}: a Grid page has no design size to drag");
            });
        }
    }

    // ==================================================================
    // A docked control (spec §7a): no handles, no drag
    // ==================================================================

    [AvaloniaTest]
    public void ADockedControl_GetsNoHandles_ButAnUndockedOneDoes_AtTwoZooms()
    {
        foreach (var (w, h) in TwoZooms)
        {
            var (doc, panel, button) = DockedPage();
            using var s = Open(doc, w, h, selected: panel);

            // The left-middle handle's centre, where the Panel is DRAWN: (0, 24, 800, 40).
            var docked = s.Frame(f => PixelAt(f, s.At(0, 24 + 20)));
            s.Canvas.SelectedControl = button;
            var undocked = s.Frame(f => PixelAt(f, s.At(40, 120)));   // the Button's top-left handle's centre

            Assert.Multiple(() =>
            {
                Assert.That(undocked, Is.EqualTo(Navy), $"{w}x{h}: control — the sampler sees a handle");
                Assert.That(docked, Is.Not.EqualTo(Navy), $"{w}x{h}: a docked control has no handles");
            });
        }
    }

    [AvaloniaTest]
    public void ASelectedDockedControl_IsStillShownSelected()
    {
        var (doc, panel, _) = DockedPage();
        using var s = Open(doc, 900, 600);

        var unselected = s.Frame(Hash);
        s.Canvas.SelectedControl = panel;
        var selected = s.Frame(Hash);

        Assert.That(selected, Is.Not.EqualTo(unselected),
            "no handles (spec §7a) must not mean no sign of the selection at all (pre-flight decision 1)");
    }

    [AvaloniaTest]
    public void ADragStartingOnADockedControl_SelectsIt_AndMovesNothing_AtTwoZooms()
    {
        foreach (var (w, h) in TwoZooms)
        {
            var (doc, panel, _) = DockedPage();
            using var s = Open(doc, w, h, selection: new FormSelection());
            var commits = new Recorder();
            s.Canvas.CommitGeometryCommand = commits;

            var from = s.At(600, 44);
            s.Drag(from, from + new Point(60, 40));

            var g = (PixelGeometry)panel.Geometry!;
            Assert.Multiple(() =>
            {
                Assert.That(s.Canvas.SelectedControl, Is.SameAs(panel), $"{w}x{h}: the press selects it");
                Assert.That((g.X, g.Y, g.Width, g.Height), Is.EqualTo((300, 300, 120, 40)), $"{w}x{h}: and moves nothing");
                Assert.That(doc.Controls.IndexOf(panel), Is.EqualTo(1), $"{w}x{h}: nor re-parents it");
                Assert.That(commits.Executions, Is.Zero, $"{w}x{h}: nothing to commit");
            });
        }
    }

    [AvaloniaTest]
    public void ADragStartingOnADockedPrimary_MovesNoOtherSelectedControlEither()
    {
        // ⛔ B4: the model refuses the docked primary, but a drag armed on it would still move every OTHER selected
        // control through the group loop. "A drag starting on it selects only" (spec §7a).
        var (doc, panel, button) = DockedPage();
        var selection = new FormSelection();
        selection.SetRange(new[] { button, panel });   // the Panel is the primary (the LAST member)
        using var s = Open(doc, 900, 600, selected: panel, selection: selection);

        var from = s.At(600, 44);
        s.Drag(from, from + new Point(64, 48));

        var g = (PixelGeometry)button.Geometry!;
        Assert.That((g.X, g.Y), Is.EqualTo((40, 120)), "the drag started on a docked control, so nothing moves");
    }

    /// <summary>
    /// Task 9 review: an overflowing dock (a Dock=Left Panel 900 wide on an 800-wide form) is CLIPPED to the form
    /// surface, as WinForms clips it — nothing of it is painted in the canvas gap to the right of the form.
    /// </summary>
    [AvaloniaTest]
    public void AnOverflowingDockedControl_IsNotPaintedOutsideTheForm()
    {
        FormDocument Form(bool overflowing)
        {
            var doc = AWindow();
            if (overflowing)
            {
                doc.Controls.Add(new FormControl
                {
                    Kind = "Panel", Id = "X",
                    Geometry = new PixelGeometry { X = 0, Y = 0, Width = 900, Height = 40, Dock = "Left" }
                });
            }

            return doc;
        }

        // Right of the form, clear of the right-hand grip (at the surface's vertical centre) and the corner grip.
        Rect Gap(Surface s) => new(s.SurfaceRect.Right + 8, s.SurfaceRect.Y + 10, 30, 90);

        // Just inside the form's right edge: the empty form shows its alignment dots there; the Panel covers them.
        Rect Inside(Surface s) => new(s.SurfaceRect.Right - 38, s.SurfaceRect.Y + 10, 30, 90);

        int emptyGap, overflowGap, emptyInside, overflowInside;
        using (var s = Open(Form(false), 900, 600))
        {
            var bg = s.Frame(f => PixelAt(f, Gap(s).TopLeft));
            emptyGap = s.Frame(f => CountOtherThan(f, Gap(s), bg));
            emptyInside = s.Frame(f => CountOtherThan(f, Inside(s), Face));
            using var o = Open(Form(true), 900, 600);
            overflowGap = o.Frame(f => CountOtherThan(f, Gap(o), bg));
            overflowInside = o.Frame(f => CountOtherThan(f, Inside(o), Face));
        }

        Assert.Multiple(() =>
        {
            Assert.That(emptyGap, Is.Zero, "control: the gap right of an empty form is plain background");
            Assert.That(overflowInside, Is.Not.EqualTo(emptyInside), "control: the Panel IS drawn inside the form");
            Assert.That(overflowGap, Is.Zero, "the overflow past the form's right edge is clipped");
        });
    }

    /// <summary>
    /// A SHORT form (60 high) whose MenuStrip's File menu has four rows: opened, the dropdown hangs far below the
    /// surface. Returns the form, the File item (select it to open the menu) and its last row.
    /// </summary>
    private static (FormDocument Doc, FormControl File, FormControl LastRow) ShortFormWithAMenu()
    {
        var doc = new FormDocument { Target = FormTarget.WinForms, Name = "P", Width = 800, Height = 60 };
        var menu = new FormControl { Kind = "MenuStrip", Id = "menuStrip1" };
        var file = new FormControl { Kind = "ToolStripMenuItem", Id = "fileToolStripMenuItem" };
        file.Properties["Text"] = "&File";
        FormControl last = file;
        foreach (var caption in new[] { "New", "Open", "Save", "Exit" })
        {
            last = new FormControl { Kind = "ToolStripMenuItem", Id = caption.ToLowerInvariant() + "ToolStripMenuItem" };
            last.Properties["Text"] = caption;
            file.Children.Add(last);
        }

        menu.Children.Add(file);
        doc.Controls.Add(menu);
        return (doc, file, last);
    }

    /// <summary>
    /// ⛔ Task 9 review: only POSITIONED controls are clipped to the form surface. An open dropdown is chrome — it runs
    /// past a short form's bottom edge and its rows there must still be hit, or pressing one starts a rubber band,
    /// clears the selection and the menu vanishes.
    /// </summary>
    [Test]
    public void AnOpenDropdownRowBelowTheForm_IsStillHit()
    {
        var (doc, file, last) = ShortFormWithAMenu();
        var t = new FormCanvasTransform(0.8, new Vector(40, 30));

        var row = FormCanvasTransform.Layout(doc, file).Single(e => ReferenceEquals(e.Control, last)).Bounds;

        Assert.Multiple(() =>
        {
            Assert.That(row.Top, Is.GreaterThan(60), "precondition: the last row is entirely below the 60-high form");
            Assert.That(t.HitTest(doc, t.ToCanvas(row.Center), file), Is.SameAs(last));
        });
    }

    /// <summary>The render half of <see cref="AnOpenDropdownRowBelowTheForm_IsStillHit"/>: that row is PAINTED.</summary>
    [AvaloniaTest]
    public void AnOpenDropdownRowBelowTheForm_IsStillPainted()
    {
        var (doc, file, last) = ShortFormWithAMenu();
        var row = FormCanvasTransform.Layout(doc, file).Single(e => ReferenceEquals(e.Control, last)).Bounds;

        // ⚠ The whole row, counted against the canvas background: a row's FILL is white like the headless window
        // behind the canvas, so a single fill pixel cannot tell a painted row from none — its caption and edges can.
        int closed, open;
        using (var s = Open(doc, 900, 600))
        {
            // ⚠ Deflated: the dropdown's Type Here slot sits directly under this row and its 1px edge (unclipped chrome
            // of its own) bleeds into the row's last pixel line — the count must see THIS row, nothing adjacent.
            var area = new Rect(s.At(row.X, row.Y), s.At(row.Right, row.Bottom)).Deflate(2);
            var bg = s.Frame(f => PixelAt(f, area.TopLeft));
            closed = s.Frame(f => CountOtherThan(f, area, bg));

            using var o = Open(doc, 900, 600, selected: file);
            open = o.Frame(f => CountOtherThan(f, area, bg));
        }

        Assert.Multiple(() =>
        {
            Assert.That(row.Top, Is.GreaterThan(60), "precondition: the last row is entirely below the 60-high form");
            Assert.That(closed, Is.Zero, "control: with the menu closed that area is plain background");
            Assert.That(open, Is.GreaterThan(0), "the open dropdown's row is painted there, past the form's edge");
        });
    }

    [AvaloniaTest]
    public void TheArrowKeys_NeitherNudgeNorResizeADockedControl()
    {
        var (doc, panel, _) = DockedPage();
        using var s = Open(doc, 900, 600, selected: panel);

        s.Window.KeyPress(Key.Right, RawInputModifiers.None);
        s.Window.KeyPress(Key.Down, RawInputModifiers.Shift);
        s.Window.KeyPress(Key.Right, RawInputModifiers.Control);

        var g = (PixelGeometry)panel.Geometry!;
        Assert.That((g.X, g.Y, g.Width, g.Height), Is.EqualTo((300, 300, 120, 40)));
    }
}
