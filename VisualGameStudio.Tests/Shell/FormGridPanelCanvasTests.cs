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

/// <summary>GridPanel on the real canvas: children are hit-tested and selected inside their cells.</summary>
[TestFixture]
public class FormGridPanelCanvasTests
{
    private sealed class Rig
    {
        public required FormCanvasControl Canvas { get; init; }
        public required Window Window { get; init; }
        public required FormDocument Doc { get; init; }
        public required FormSelection Selection { get; init; }
        public required FormControl Panel { get; init; }
        public required FormControl Button { get; init; }
        public required FormControl Text { get; init; }
        public required FormControl Outside { get; init; }
    }

    private static Rig Open()
    {
        var doc = new FormDocument
        {
            Target = FormTarget.Web, Name = "T", Width = 400, Height = 300,
            Layout = new FormLayout { Kind = FormLayoutKind.Canvas }
        };
        var panel = new FormControl
        {
            Kind = "GridPanel", Id = "grid", TabIndex = 0,
            Geometry = new PixelGeometry { X = 20, Y = 20, Width = 300, Height = 200 }
        };
        panel.Properties["Columns"] = "1fr,1fr";
        panel.Properties["Rows"] = "auto,auto";
        panel.Properties["Gap"] = "8px";
        var button = new FormControl { Kind = "Button", Id = "btn", TabIndex = 1, GridCell = new GridCell { Col = 0, Row = 0 } };
        var text = new FormControl { Kind = "TextBox", Id = "txt", TabIndex = 2, GridCell = new GridCell { Col = 1, Row = 0 } };
        panel.Children.Add(button);
        panel.Children.Add(text);
        doc.Controls.Add(panel);

        var outside = new FormControl
        {
            Kind = "Button", Id = "outside", TabIndex = 3,
            Geometry = new PixelGeometry { X = 20, Y = 250, Width = 80, Height = 24 }
        };
        doc.Controls.Add(outside);

        var selection = new FormSelection();
        var canvas = new FormCanvasControl { Document = doc, Selection = selection };
        var window = new Window { Width = 600, Height = 500, Content = canvas };
        window.Show();
        canvas.Focus();
        return new Rig
        {
            Canvas = canvas, Window = window, Doc = doc, Selection = selection,
            Panel = panel, Button = button, Text = text, Outside = outside
        };
    }

    private static Point Centre(Rig rig, FormControl control)
    {
        var bounds = FormCanvasTransform.Layout(rig.Doc)
            .Single(e => ReferenceEquals(e.Control, control)).Bounds;
        return FormCanvasControl.Fit(rig.Doc, rig.Canvas.Bounds.Size).ToCanvas(bounds).Center;
    }

    private static void Click(Rig rig, Point point)
    {
        rig.Window.MouseDown(point, MouseButton.Left);
        rig.Window.MouseUp(point, MouseButton.Left);
    }

    [AvaloniaTest]
    public void ClickingAChild_SelectsTheChild_NotThePanel()
    {
        var rig = Open();

        Click(rig, Centre(rig, rig.Button));

        Assert.Multiple(() =>
        {
            Assert.That(rig.Selection.Primary, Is.SameAs(rig.Button));
            Assert.That(rig.Canvas.SelectedControl, Is.SameAs(rig.Button));
        });
    }

    [AvaloniaTest]
    public void ClickingAnEmptyCell_SelectsThePanel()
    {
        var rig = Open();
        var panelBounds = FormCanvasTransform.Layout(rig.Doc)
            .Single(e => ReferenceEquals(e.Control, rig.Panel)).Bounds;
        var emptyCell = FormCanvasControl.Fit(rig.Doc, rig.Canvas.Bounds.Size)
            .ToCanvas(new Rect(panelBounds.X + 40, panelBounds.Y + 170, 20, 20)).Center;

        Click(rig, emptyCell);

        Assert.That(rig.Selection.Primary, Is.SameAs(rig.Panel),
            $"selected: {rig.Selection.Primary?.Id ?? "(none)"}; panel cell: {panelBounds}; clicked: {emptyCell}");
    }

    [AvaloniaTest]
    public void ColumnHeader_SelectsATrack_AndItsPlusAndMinusInsertAndDelete()
    {
        var rig = Open();
        rig.Selection.Set(rig.Panel);
        var panelBounds = FormCanvasTransform.Layout(rig.Doc)
            .Single(e => ReferenceEquals(e.Control, rig.Panel)).Bounds;
        var fit = FormCanvasControl.Fit(rig.Doc, rig.Canvas.Bounds.Size);
        var cols = FormGridLayout.TrackRects(
            FormGridPanel.LayoutOf(rig.Panel), panelBounds.Size, columns: true);
        var label = fit.ToCanvas(new Rect(
            panelBounds.X + cols[0].X, panelBounds.Y, cols[0].Width, panelBounds.Height)).TopLeft;
        var labelCentre = new Point(label.X + 2 + 7, label.Y + 2 + 5);

        Click(rig, labelCentre);
        Assert.That(rig.Panel.Properties.GetValueOrDefault("Columns"), Is.EqualTo("1fr,1fr").Or.Null);

        // + is immediately right of the label badge; − is immediately right of +.
        Click(rig, new Point(labelCentre.X + 14, labelCentre.Y));
        var afterInsert = FormTracks.Split(rig.Panel.Properties["Columns"]);
        Assert.That(afterInsert, Has.Count.EqualTo(3));

        // Select the new (empty) second track, then delete it. Track 0 holds the Button, so removing
        // it is correctly refused.
        var cols3 = FormGridLayout.TrackRects(
            FormGridPanel.LayoutOf(rig.Panel), panelBounds.Size, columns: true);
        var secondLabel = fit.ToCanvas(new Rect(
            panelBounds.X + cols3[1].X, panelBounds.Y, cols3[1].Width, panelBounds.Height)).TopLeft;
        var secondCentre = new Point(secondLabel.X + 9, secondLabel.Y + 7);
        Click(rig, secondCentre);
        Click(rig, new Point(secondCentre.X + 30, secondCentre.Y));
        Assert.That(FormTracks.Split(rig.Panel.Properties["Columns"]), Has.Count.EqualTo(2));
    }

    [AvaloniaTest]
    public void DraggingAColumnHeader_ReordersTheTrackAndItsCells()
    {
        var rig = Open();
        rig.Panel.Properties["Columns"] = "1fr,2fr";
        rig.Selection.Set(rig.Panel);
        rig.Canvas.InvalidateVisual();

        var panelBounds = FormCanvasTransform.Layout(rig.Doc)
            .Single(e => ReferenceEquals(e.Control, rig.Panel)).Bounds;
        var fit = FormCanvasControl.Fit(rig.Doc, rig.Canvas.Bounds.Size);
        var cols = FormGridLayout.TrackRects(
            FormGridPanel.LayoutOf(rig.Panel), panelBounds.Size, columns: true);
        Point Centre(int i)
        {
            var label = fit.ToCanvas(new Rect(
                panelBounds.X + cols[i].X, panelBounds.Y, cols[i].Width, panelBounds.Height)).TopLeft;
            return new Point(label.X + 9, label.Y + 7);
        }

        var from = Centre(0);
        var to = fit.ToCanvas(new Rect(
            panelBounds.X + cols[1].X, panelBounds.Y, cols[1].Width, panelBounds.Height)).Center;
        rig.Window.MouseDown(from, MouseButton.Left);
        rig.Window.MouseMove(to, RawInputModifiers.LeftMouseButton);
        rig.Window.MouseUp(to, MouseButton.Left);

        Assert.Multiple(() =>
        {
            Assert.That(FormTracks.Split(rig.Panel.Properties["Columns"]), Is.EqualTo(new[] { "2fr", "1fr" }));
            Assert.That((rig.Button.GridCell!.Col, rig.Text.GridCell!.Col), Is.EqualTo((1, 0)));
        });
    }

    [AvaloniaTest]
    public void DraggingAColumnBoundary_ResizesTheTrack()
    {
        var rig = Open();
        rig.Selection.Set(rig.Panel);

        var panelBounds = FormCanvasTransform.Layout(rig.Doc)
            .Single(e => ReferenceEquals(e.Control, rig.Panel)).Bounds;
        var cols = FormGridLayout.TrackRects(
            FormGridPanel.LayoutOf(rig.Panel), panelBounds.Size, columns: true);
        var boundary = panelBounds.X + ((cols[0].Right + cols[1].X) / 2);
        var from = FormCanvasControl.Fit(rig.Doc, rig.Canvas.Bounds.Size)
            .ToCanvas(new Point(boundary, panelBounds.Y + 50));
        var to = from + new Point(30, 0);

        rig.Window.MouseDown(from, MouseButton.Left);
        rig.Window.MouseMove(to, RawInputModifiers.LeftMouseButton);
        rig.Window.MouseUp(to, MouseButton.Left);

        Assert.That(rig.Panel.Properties["Columns"], Does.Contain("px").And.Contain(",1fr"));
        Assert.That(FormTracks.Split(rig.Panel.Properties["Columns"])[0], Is.Not.EqualTo("1fr"));
    }

    [AvaloniaTest]
    public void DraggingAnOutsideControlIntoACell_MakesItAGridChild()
    {
        var rig = Open();
        var panelBounds = FormCanvasTransform.Layout(rig.Doc)
            .Single(e => ReferenceEquals(e.Control, rig.Panel)).Bounds;
        var emptyCell = FormCanvasControl.Fit(rig.Doc, rig.Canvas.Bounds.Size)
            .ToCanvas(new Rect(panelBounds.X + 40, panelBounds.Y + 170, 20, 20)).Center;
        var from = Centre(rig, rig.Outside);

        rig.Window.MouseDown(from, MouseButton.Left);
        rig.Window.MouseMove(emptyCell, RawInputModifiers.LeftMouseButton);
        rig.Window.MouseUp(emptyCell, MouseButton.Left);

        Assert.Multiple(() =>
        {
            Assert.That(rig.Panel.Children.Select(c => c.Id), Does.Contain("outside"));
            Assert.That(rig.Outside.Geometry, Is.Null);
            Assert.That((rig.Outside.GridCell!.Col, rig.Outside.GridCell.Row), Is.EqualTo((0, 1)));
        });
    }

    [AvaloniaTest]
    public void DraggingAChildOntoAnOccupiedCell_ChangesNothing()
    {
        var rig = Open();
        var from = Centre(rig, rig.Button);
        var onto = Centre(rig, rig.Text);

        rig.Window.MouseDown(from, MouseButton.Left);
        rig.Window.MouseMove(onto, RawInputModifiers.LeftMouseButton);
        rig.Window.MouseUp(onto, MouseButton.Left);

        Assert.That((rig.Button.GridCell!.Col, rig.Button.GridCell.Row), Is.EqualTo((0, 0)));
    }

    [AvaloniaTest]
    public void DraggingAChild_MovesItToTheCellUnderThePointer()
    {
        var rig = Open();
        var panelBounds = FormCanvasTransform.Layout(rig.Doc)
            .Single(e => ReferenceEquals(e.Control, rig.Panel)).Bounds;
        var emptyCell = FormCanvasControl.Fit(rig.Doc, rig.Canvas.Bounds.Size)
            .ToCanvas(new Rect(panelBounds.X + 40, panelBounds.Y + 170, 20, 20)).Center;
        var from = Centre(rig, rig.Button);

        rig.Window.MouseDown(from, MouseButton.Left);
        rig.Window.MouseMove(emptyCell, RawInputModifiers.LeftMouseButton);
        rig.Window.MouseUp(emptyCell, MouseButton.Left);

        Assert.That((rig.Button.GridCell!.Col, rig.Button.GridCell.Row), Is.EqualTo((0, 1)));
    }
}
