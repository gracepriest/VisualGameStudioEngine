using BasicLang.Forms;
using BasicLang.Forms.Serialization;
using NUnit.Framework;
using VisualGameStudio.Shell.Controls;
using VisualGameStudio.Shell.ViewModels.Designer;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// GridPanel: a web-first container whose children live in cells of the PANEL's grid. The page may
/// be Canvas (the panel has pixels) or Grid (the panel has a page cell); the child cell is independent
/// of both, which is the model rule these tests pin.
/// </summary>
[TestFixture]
public class FormGridPanelTests
{
    private static FormDocument CanvasForm()
    {
        var form = new FormDocument
        {
            Target = FormTarget.Web, Name = "T", Width = 400, Height = 300,
            Layout = new FormLayout { Kind = FormLayoutKind.Canvas }
        };

        var panel = new FormControl
        {
            Kind = "GridPanel", Id = "grid1", TabIndex = 0,
            Geometry = new PixelGeometry { X = 20, Y = 20, Width = 300, Height = 200 }
        };
        panel.Properties["Columns"] = "1fr,1fr";
        panel.Properties["Rows"] = "auto,auto";
        panel.Properties["Gap"] = "8px";

        var button = new FormControl { Kind = "Button", Id = "btn", TabIndex = 1, GridCell = new GridCell { Col = 0, Row = 0 } };
        button.Properties["Text"] = "Save";
        var text = new FormControl { Kind = "TextBox", Id = "txt", TabIndex = 2, GridCell = new GridCell { Col = 1, Row = 0, ColSpan = 1 } };
        panel.Children.Add(button);
        panel.Children.Add(text);
        form.Controls.Add(panel);
        return form;
    }

    private static FormDocument GridPage(FormDocument source)
    {
        source.Layout = new FormLayout
        {
            Kind = FormLayoutKind.Grid, Cols = "1fr,1fr", Rows = "1fr", Gap = "8px"
        };
        var panel = source.Controls.Single();
        panel.Geometry = new GridGeometry { Col = 0, Row = 0, ColSpan = 2 };
        return source;
    }

    [Test]
    public void Catalog_HasAWebGridPanel_ThatHoldsChildren()
    {
        var definition = FormControlCatalog.Find("GridPanel");

        Assert.Multiple(() =>
        {
            Assert.That(definition, Is.Not.Null);
            Assert.That(definition!.SupportsTarget(FormTarget.Web), Is.True);
            Assert.That(definition.SupportsTarget(FormTarget.WinForms), Is.False, "web-first in this pass");
            Assert.That(definition.IsContainer, Is.True);
            Assert.That(definition.HtmlTag, Is.EqualTo("div"));
            Assert.That(definition.Property("Columns")?.Default, Is.EqualTo("1fr,1fr"));
            Assert.That(definition.Property("Rows")?.Default, Is.EqualTo("auto,auto"));
            Assert.That(definition.Property("Gap")?.Default, Is.EqualTo("8px"));
        });
    }

    [Test]
    public void CanvasRoundTrip_KeepsThePanelPixels_AndChildCells()
    {
        var text = FormDocumentWriter.Create(CanvasForm());
        var file = FormDocumentReader.Read("T.blwebform", text);

        Assert.That(file.IsRefused, Is.False, string.Join("; ", file.Diagnostics.Select(d => d.Message)));
        var panel = file.Model.FindById("grid1")!;
        var button = file.Model.FindById("btn")!;

        Assert.Multiple(() =>
        {
            Assert.That(panel.Geometry, Is.TypeOf<PixelGeometry>());
            Assert.That(((PixelGeometry)panel.Geometry!).Width, Is.EqualTo(300));
            Assert.That(button.Geometry, Is.Null, "a GridPanel child has a CELL, not pixel geometry");
            Assert.That(button.GridCell, Is.Not.Null);
            Assert.That((button.GridCell!.Col, button.GridCell.Row), Is.EqualTo((0, 0)));
            Assert.That(file.Model.FindById("txt")!.GridCell!.Col, Is.EqualTo(1));
        });
    }

    [Test]
    public void GridPageRoundTrip_KeepsThePageCell_AndThePanelCell()
    {
        var text = FormDocumentWriter.Create(GridPage(CanvasForm()));
        var file = FormDocumentReader.Read("T.blwebform", text);

        Assert.That(file.IsRefused, Is.False, string.Join("; ", file.Diagnostics.Select(d => d.Message)));
        var panel = file.Model.FindById("grid1")!;

        Assert.Multiple(() =>
        {
            Assert.That(panel.Geometry, Is.TypeOf<GridGeometry>());
            Assert.That(((GridGeometry)panel.Geometry!).ColSpan, Is.EqualTo(2));
            Assert.That(panel.Children.Single(c => c.Id == "txt").GridCell!.Col, Is.EqualTo(1));
            Assert.That(panel.Children.Single(c => c.Id == "txt").Geometry, Is.Null);
        });
    }

    [Test]
    public void ClipboardRoundTrip_KeepsTheChildCells()
    {
        var form = CanvasForm();
        var panel = form.Controls.Single();

        var xml = FormClipboard.SerializeSubtree(
            FormTarget.Web, new[] { panel }, FormLayoutKind.Canvas);
        var pasted = FormClipboard.Paste(
            xml, FormTarget.Web, FormLayoutKind.Canvas, id => form.FindById(id) != null);

        Assert.That(pasted.Controls, Is.Not.Empty, pasted.Refusal);
        var child = pasted.Controls.Single().Children.Single(c => c.Kind == "TextBox");
        Assert.Multiple(() =>
        {
            Assert.That(child.Geometry, Is.Null);
            Assert.That(child.GridCell, Is.Not.Null);
            Assert.That(child.GridCell!.Col, Is.EqualTo(1));
        });
    }

    [Test]
    public void Validation_ReportsOverlapOutOfRangeAndMissingCells()
    {
        var form = CanvasForm();
        var panel = form.Controls.Single();
        panel.Children.Add(new FormControl
        {
            Kind = "Label", Id = "lbl", TabIndex = 3, GridCell = new GridCell { Col = 0, Row = 0 }
        });
        panel.Children.Add(new FormControl
        {
            Kind = "Label", Id = "outside", TabIndex = 4, GridCell = new GridCell { Col = 9, Row = 0 }
        });
        panel.Children.Add(new FormControl { Kind = "Label", Id = "missing", TabIndex = 5 });

        var problems = FormGridPanel.Validate(panel);

        Assert.Multiple(() =>
        {
            Assert.That(problems.Any(p => p.Contains("btn") && p.Contains("lbl") && p.Contains("overlap")), Is.True,
                string.Join("\n", problems));
            Assert.That(problems.Any(p => p.Contains("outside") && p.Contains("explicit grid")), Is.True);
            Assert.That(problems.Any(p => p.Contains("missing") && p.Contains("no grid cell")), Is.True);
        });
    }

    [Test]
    public void Emitter_WritesThePanelGrid_AndChildCellRules()
    {
        var form = CanvasForm();
        var css = FormAssetEmitter.Css(form, FormStyle.Portable);
        var html = FormAssetEmitter.Html(form, "app.js", FormStyle.Portable);

        Assert.Multiple(() =>
        {
            Assert.That(css, Does.Contain("display: grid"));
            Assert.That(css, Does.Contain("grid-template-columns: 1fr 1fr"));
            Assert.That(css, Does.Contain("grid-template-rows: auto auto"));
            Assert.That(css, Does.Contain("gap: 8px"));
            Assert.That(css, Does.Contain("#btn { grid-column: 1; grid-row: 1; }"));
            Assert.That(css, Does.Contain("#txt { grid-column: 2; grid-row: 1; }"));
            Assert.That(html, Does.Contain("<div id=\"grid1\" class=\"vgs-GridPanel\""));
            Assert.That(html, Does.Contain("<button id=\"btn\""));
            Assert.That(html, Does.Contain("<input id=\"txt\""));
        });
    }

    [Test]
    public void Emitter_KeepsACssFunctionAsOneTrack()
    {
        var form = CanvasForm();
        var panel = form.Controls.Single();
        panel.Properties["Columns"] = "minmax(100px, 1fr),1fr";

        var css = FormAssetEmitter.Css(form, FormStyle.Portable);

        Assert.That(css, Does.Contain("grid-template-columns: minmax(100px, 1fr) 1fr"));
    }

    [Test]
    public void Emitter_OnAPageGrid_PutsThePanelInItsPageCell_AndItsChildrenInItsOwnGrid()
    {
        var form = GridPage(CanvasForm());
        var css = FormAssetEmitter.Css(form, FormStyle.Portable);

        Assert.Multiple(() =>
        {
            Assert.That(css, Does.Contain("#grid1 { display: grid;"));
            Assert.That(css, Does.Contain("grid-column: 1 / span 2"));
            Assert.That(css, Does.Contain("grid-row: 1"));
            Assert.That(css, Does.Contain("#btn { grid-column: 1; grid-row: 1; }"));
        });
    }

    [Test]
    public void PropertyGrid_ExposesATrackEditor_ThatAddsAndRemovesTracks()
    {
        var form = CanvasForm();
        var file = FormDocumentReader.Read("T.blwebform", FormDocumentWriter.Create(form));
        var grid = new VisualGameStudio.Shell.ViewModels.Designer.FormPropertyGridViewModel();
        grid.Load(file);
        grid.SelectedControl = file.Model.FindById("grid1");

        var columns = grid.Rows.Single(r => r.Name == "Columns");
        var rows = grid.Rows.Single(r => r.Name == "Rows");

        Assert.That(columns.IsGridTrackEditor, Is.True);
        Assert.That(rows.IsGridTrackEditor, Is.True);

        columns.AddGridTrackCommand.Execute(null);
        rows.AddGridTrackCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(file.Model.FindById("grid1")!.Properties["Columns"], Is.EqualTo("1fr,1fr,1fr"));
            Assert.That(file.Model.FindById("grid1")!.Properties["Rows"], Is.EqualTo("auto,auto,auto"));
            Assert.That(columns.StringValue, Is.EqualTo("1fr,1fr,1fr"));
        });

        columns.RemoveGridTrackCommand.Execute(null);
        rows.RemoveGridTrackCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(columns.StringValue, Is.EqualTo("1fr,1fr"));
            Assert.That(rows.StringValue, Is.EqualTo("auto,auto"));
        });

        grid.SelectedControl = file.Model.FindById("btn");
        Assert.That(grid.Rows.Select(r => r.Name), Is.SupersetOf(new[] { "Col", "Row", "ColSpan", "RowSpan" }));
    }

    [Test]
    public void CanvasLayout_PutsChildrenInTheirCells()
    {
        var form = CanvasForm();
        var entries = FormCanvasTransform.Layout(form).ToList();

        var panel = entries.Single(e => e.Control?.Id == "grid1");
        var button = entries.Single(e => e.Control?.Id == "btn");
        var text = entries.Single(e => e.Control?.Id == "txt");

        Assert.Multiple(() =>
        {
            Assert.That(panel.Bounds.X, Is.EqualTo(20));
            Assert.That(button.Bounds.X, Is.EqualTo(20), "cell 0 starts at the panel's left edge");
            Assert.That(text.Bounds.X, Is.EqualTo(20 + 154), "1fr / 1fr with an 8px gap: 146 each, second starts 154 in");
            Assert.That(button.Bounds.Y, Is.EqualTo(20));
        });
    }

    [Test]
    [Category("Integration")]
    public async Task CliBuild_EmitsAGridPanelPage()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-gridpanel-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var scaffold = FormScaffolder.Create("GridDemo", FormTarget.Web, portable: true);
            var form = CanvasForm();
            form.Name = "GridDemo";
            File.WriteAllText(Path.Combine(dir, scaffold.DocumentFileName), FormDocumentWriter.Create(form));
            File.WriteAllText(Path.Combine(dir, scaffold.CodeFileName), scaffold.CodeText);
            File.WriteAllText(Path.Combine(dir, "Main.bas"), "Sub Main()\nEnd Sub\n");
            File.WriteAllText(Path.Combine(dir, "App.blproj"),
                """
                <BasicLangProject Version="1.0">
                  <PropertyGroup>
                    <ProjectName>App</ProjectName>
                    <OutputType>Exe</OutputType>
                    <TargetBackend>JavaScript</TargetBackend>
                  </PropertyGroup>
                  <ItemGroup>
                    <Compile Include="Main.bas" />
                    <Compile Include="GridDemo.bas" />
                    <Compile Include="GridDemo.blwebform" />
                  </ItemGroup>
                </BasicLangProject>
                """);

            var (exit, stdout, stderr) = await CliTestHarness.RunCli(
                dir, "build", Path.Combine(dir, "App.blproj"));

            var html = Directory.GetFiles(Path.Combine(dir, "bin"), "GridDemo.html", SearchOption.AllDirectories);
            Assert.Multiple(() =>
            {
                Assert.That(exit, Is.Zero, $"{stdout}\n{stderr}");
                Assert.That(html, Has.Length.EqualTo(1));
                Assert.That(File.ReadAllText(html[0]), Does.Contain("class=\"vgs-GridPanel\""));
                Assert.That(File.ReadAllText(Path.ChangeExtension(html[0], ".css")), Does.Contain("grid-template-columns"));
            });
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Test]
    public void InsertingAndRemovingATrack_ShiftsCells_AndRefusesAnOccupiedTrack()
    {
        var form = CanvasForm();
        var panel = form.Controls.Single();

        Assert.That(FormGridPanel.InsertTrack(panel, columns: true, index: 0), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(FormTracks.Split(panel.Properties["Columns"]), Has.Count.EqualTo(3));
            Assert.That(panel.Children.Single(c => c.Id == "btn").GridCell!.Col, Is.EqualTo(1));
            Assert.That(panel.Children.Single(c => c.Id == "txt").GridCell!.Col, Is.EqualTo(2));
        });

        Assert.That(FormGridPanel.RemoveTrack(panel, columns: true, index: 0, out _), Is.True);
        Assert.That(panel.Children.Single(c => c.Id == "btn").GridCell!.Col, Is.EqualTo(0));

        Assert.That(FormGridPanel.RemoveTrack(panel, columns: true, index: 1, out var refusal), Is.False);
        Assert.That(refusal, Does.Contain("txt"));
    }

    [Test]
    public void Retarget_ToWinForms_RemovesTheWholeGridPanel_AndNamesIt()
    {
        var result = FormRetarget.Convert(GridPage(CanvasForm()), FormTarget.WinForms);

        Assert.Multiple(() =>
        {
            Assert.That(result.Document.FindById("grid1"), Is.Null);
            Assert.That(result.Document.FindById("btn"), Is.Null, "a cell child has no desktop position");
            var finding = result.Diagnostics.Single(d => d.Code == DesignCodes.RetargetControlLost);
            Assert.That(finding.Message, Does.Contain("GridPanel"));
            Assert.That(finding.Message, Does.Contain("2 child control(s)"));
        });
    }

    [Test]
    public void LoadWebForms_RefusesAnOverlappingGridPanel()
    {
        var form = CanvasForm();
        var panel = form.Controls.Single();
        panel.Children.Add(new FormControl
        {
            Kind = "Label", Id = "lbl", TabIndex = 3, GridCell = new GridCell { Col = 0, Row = 0 }
        });

        var dir = Path.Combine(Path.GetTempPath(), "bl-gridpanel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "T.blwebform");
            File.WriteAllText(path, FormDocumentWriter.Create(form));
            var errors = new List<string>();
            var loaded = FormDocumentLoader.LoadWebForms(new[] { path }, errors.Add, errors.Add);

            Assert.Multiple(() =>
            {
                Assert.That(loaded, Is.Empty);
                Assert.That(errors, Is.Not.Empty);
                Assert.That(errors.Any(e => e.Contains(DesignCodes.GridCellConflict) && e.Contains("overlap")), Is.True,
                    string.Join("\n", errors));
            });
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Test]
    public void Placement_IntoAnEmptyCanvasGridPanel_WritesACell_AndRefusesAnOccupiedOne()
    {
        var form = CanvasForm();
        var panel = form.Controls.Single();
        panel.Children.Clear();

        var placed = FormPlacement.Place(form, "Button", 60, 60);
        Assert.That(placed.Control, Is.Not.Null, placed.Refusal);
        Assert.Multiple(() =>
        {
            Assert.That(panel.Children, Has.Count.EqualTo(1));
            Assert.That(placed.Control!.GridCell, Is.Not.Null);
            Assert.That((placed.Control.GridCell!.Col, placed.Control.GridCell.Row), Is.EqualTo((0, 0)));
            Assert.That(placed.Control.Geometry, Is.Null);
        });

        var refused = FormPlacement.Place(form, "Button", 60, 60);
        Assert.Multiple(() =>
        {
            Assert.That(refused.Control, Is.Null);
            Assert.That(refused.Refusal, Does.Contain("already occupied"));
        });
    }

    [Test]
    public void Placement_IntoAPageGridGridPanel_UsesThePanelCell()
    {
        var form = GridPage(CanvasForm());
        var panel = form.Controls.Single();
        panel.Children.Clear();

        var placed = FormPlacement.Place(form, "Button", 30, 30);

        Assert.That(placed.Control, Is.Not.Null, placed.Refusal);
        Assert.Multiple(() =>
        {
            Assert.That(panel.Children, Has.Count.EqualTo(1));
            Assert.That(placed.Control!.Geometry, Is.Null, "the child is in the PANEL's grid");
            Assert.That((placed.Control.GridCell!.Col, placed.Control.GridCell.Row), Is.EqualTo((0, 0)));
        });
    }
}
