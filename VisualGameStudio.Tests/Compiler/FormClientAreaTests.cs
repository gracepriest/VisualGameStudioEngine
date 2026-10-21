using Avalonia;
using BasicLang.Forms;
using NUnit.Framework;
using VisualGameStudio.Shell.Controls;

namespace VisualGameStudio.Tests.Compiler;

[TestFixture]
public class FormClientAreaTests
{
    [TestCase("None", 0)]
    [TestCase("FixedSingle", 1)]
    [TestCase("Fixed3D", 2)]
    [TestCase("invalid", 0)]
    public void PanelBorder_IsTheSingleClientOriginAndSizeRule(string border, int inset)
    {
        var parent = PortablePageRenderingTests.Box("Panel", "parent", w: 200, h: 100);
        parent.Properties["BorderStyle"] = border;
        var area = FormClientArea.Of(parent);
        Assert.Multiple(() =>
        {
            Assert.That(area.OriginInset, Is.EqualTo(inset));
            Assert.That(area.ClientSize(200, 100), Is.EqualTo((200 - 2 * inset, 100 - 2 * inset)));
            Assert.That(area.ClientSize(1, 1), Is.EqualTo((Math.Max(0, 1 - 2 * inset), Math.Max(0, 1 - 2 * inset))));
            Assert.That(area.DockPadding, Is.EqualTo(default(FormInsets)));
        });
    }

    [TestCase("Panel", "FixedSingle", 1, 0, 0, 198, 98)]
    [TestCase("Panel", "Fixed3D", 2, 0, 0, 196, 96)]
    [TestCase("GroupBox", "None", 0, 3, 19, 200, 100)]
    public void ResolverCanvasAndRegionWriter_ShareClientSizeAndSeparateDockPadding(
        string kind, string border, int inset, int dockX, int dockY, int cw, int ch)
    {
        var parent = PortablePageRenderingTests.Box(kind, "parent", w: 200, h: 100);
        if (kind == "Panel") parent.Properties["BorderStyle"] = border;
        var free = PortablePageRenderingTests.Box("Button", "free");
        var top = PortablePageRenderingTests.Box("Button", "top", h: 20);
        ((PixelGeometry)top.Geometry!).Dock = "Top";
        parent.Children.AddRange(new[] { free, top });
        var form = new FormDocument { Target = FormTarget.WinForms, Name = "F", Width = 400, Height = 300 };
        form.Controls.Add(parent);
        var layout = FormDockLayout.Resolve(form);
        var entries = FormCanvasTransform.Layout(form).ToDictionary(e => e.Control!.Id);
        var width = cw - (kind == "GroupBox" ? 6 : 0);
        var lines = RegionWriter.GenerateInitLines(form, FormStyle.WinForms, "", "F.bas", new List<DesignDiagnostic>());
        Assert.Multiple(() =>
        {
            Assert.That(layout.ClientSizeOf(parent), Is.EqualTo((cw, ch)));
            Assert.That(layout.All.Single(d => d.Control == top).Bounds, Is.EqualTo(new FormRect(dockX, dockY, width, 20)));
            Assert.That(entries["free"].Bounds.TopLeft, Is.EqualTo(new Point(20 + inset, 40 + inset)),
                "positioned children use border origin only, never the caption's docking padding");
            Assert.That(entries["top"].Bounds.TopLeft, Is.EqualTo(new Point(10 + inset + dockX, 20 + inset + dockY)));
            Assert.That(FormCanvasTransform.ContainerAt(form, new Point(150, 90))!.Value.Origin,
                Is.EqualTo(new Point(10 + inset, 20 + inset)));
            Assert.That(lines.Select(l => l.Text), Does.Contain($"    top.Size = New Size({width}, 20)"));
            Assert.That(FormDockLayout.Resolve(form, clientAreas: false).ClientSizeOf(parent), Is.EqualTo((200, 100)),
                "the frozen DOM page path retains its original coordinate space");
        });
    }

    [Test]
    public void GroupBoxPadding_IsDockOnly_AndOverflowClampsSizesNotTheRemainder()
    {
        var area = FormClientArea.Of(new FormControl { Kind = "GroupBox" });
        var top = PortablePageRenderingTests.Box("Panel", "top", h: 20);
        ((PixelGeometry)top.Geometry!).Dock = "Top";
        var fill = PortablePageRenderingTests.Box("Panel", "fill");
        ((PixelGeometry)fill.Geometry!).Dock = "Fill";
        var resolved = FormDockLayout.ResolveSiblings(new[] { top, fill }, 4, 10, padding: area.DockPadding);
        Assert.That(resolved.Select(d => d.Bounds), Is.EqualTo(new[]
            { new FormRect(3, 19, 0, 20), new FormRect(3, 39, 0, 0) }));
    }
}
