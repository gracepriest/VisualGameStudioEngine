using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

[TestFixture]
public class PortablePageRenderingTests
{
    internal static FormControl Box(string kind, string id, int x = 10, int y = 20, int w = 120, int h = 30) => new()
    {
        Kind = kind, Id = id,
        Geometry = new PixelGeometry { X = x, Y = y, Width = w, Height = h }
    };

    internal static FormDocument Page(FormLayoutKind layout = FormLayoutKind.Canvas)
    {
        var page = new FormDocument
        {
            Target = FormTarget.Web, Name = "PortablePage", Width = 400, Height = 300,
            Layout = new FormLayout { Kind = layout, Cols = "1fr,1fr", Rows = "auto,auto" }
        };
        var check = Box("CheckBox", "remember");
        check.Properties["Text"] = "<b>Remember & save</b>";
        check.Properties["Checked"] = "true";
        var radio = Box("RadioButton", "choice");
        radio.Properties["Text"] = "Choose";
        var text = Box("TextBox", "entry");
        text.Properties["Text"] = "<b>typed</b>";
        text.Properties["Multiline"] = "true";
        text.Properties["ReadOnly"] = "true";
        text.Properties["PasswordChar"] = "*";
        text.Properties["MaxLength"] = "37";
        var panel = Box("Panel", "panel", w: 200, h: 140);
        panel.Properties["BorderStyle"] = "FixedSingle";
        var top = Box("Button", "top", h: 25);
        ((PixelGeometry)top.Geometry!).Dock = "Top";
        top.Properties["Text"] = "Go";
        panel.Children.Add(top);
        var group = Box("GroupBox", "group", w: 250, h: 180);
        group.Properties["Text"] = "Caption";
        group.Children.Add(panel);
        var label = Box("Label", "message");
        label.Properties["Text"] = "Hello";
        page.Controls.AddRange(new[] { check, radio, text, group, label });
        return page;
    }

    // Captured on ef67f690, before the Task 32 product changes. Includes nested borders,
    // docking and every new markup shape, so a DOM-only caller cannot acquire them quietly.
    [TestCase(FormLayoutKind.Canvas, "6AFE3CBCCF8B3300A1A027E6BF1081DCF12D2577720E153D6F6F7B69D205E871")]
    [TestCase(FormLayoutKind.Grid, "3C7E8FC8A172944B739AF65CAE7B7968F76C320467EE907201FFD46B6CD0D1F4")]
    [TestCase(FormLayoutKind.Flow, "CEE04FDEBFBEB1211ECDDA399ABADC96B32B1484AD78A183CDBB7BFC19D104E7")]
    public void LegacyDomPage_IsByteIdentical(FormLayoutKind layout, string hash)
    {
        var page = Page(layout);
        var bytes = Encoding.UTF8.GetBytes(FormAssetEmitter.Html(page, "App.js") + FormAssetEmitter.Css(page));
        Assert.That(Convert.ToHexString(SHA256.HashData(bytes)), Is.EqualTo(hash));
    }

    private static string Tag(string html, string id) =>
        Regex.Match(html, "<[^>]* id=\"" + Regex.Escape(id) + "\"[^>]*>").Value;

    [Test]
    public void PortableMarkup_HasCaptionWrappersTextareaLegendAndEscapedText()
    {
        var html = FormAssetEmitter.Html(Page(), "App.js", FormStyle.Portable);
        var check = Regex.Match(html, "<label id=\"remember\".*?</label>", RegexOptions.Singleline).Value;
        Assert.Multiple(() =>
        {
            Assert.That(Tag(html, "remember"), Does.StartWith("<label ").And.Contain("class=\"vgs-CheckBox\""));
            Assert.That(check, Does.Contain("<input type=\"checkbox\"").And.Contain(" checked"));
            Assert.That(check, Does.Contain("<span>&lt;b&gt;Remember &amp; save&lt;/b&gt;</span>"));
            Assert.That(Tag(html, "entry"), Does.StartWith("<textarea ").And.Contain(" readonly").And.Contain("maxlength=\"37\""));
            Assert.That(Tag(html, "entry"), Does.Not.Contain(" type="));
            Assert.That(html, Does.Contain(">&lt;b&gt;typed&lt;/b&gt;</textarea>"));
            Assert.That(html, Does.Contain("<legend>Caption</legend>"));
            Assert.That(html, Does.Not.Contain("<b>"));
            Assert.That(Tag(html, "group"), Does.Contain("data-vgs-pad=\"3,19,3,3\""));
            Assert.That(Tag(html, "panel"), Does.Contain("data-vgs-inset=\"1\"").And.Contain("data-vgs-cw=\"198\"").And.Contain("data-vgs-ch=\"138\""));
            Assert.That(Tag(html, "top"), Does.Contain("data-vgs-dock=\"Top\"").And.Contain("data-vgs-anchor=\"5\""));
        });
    }

    [Test]
    public void SingleLinePasswordAndRadioGroups_AreCatalogDriven()
    {
        var page = Page();
        page.FindById("entry")!.Properties["Multiline"] = "false";
        var nested = Box("RadioButton", "nested");
        page.FindById("panel")!.Children.Add(nested);
        var html = FormAssetEmitter.Html(page, "App.js", FormStyle.Portable);
        Assert.That(Tag(html, "entry"), Does.Contain("type=\"password\"").And.Contain("data-vgs-passwordchar=\"*\""));
        Assert.That(html, Does.Contain("name=\"vgs-form-PortablePage\"").And.Contain("name=\"panel\""));
        nested.Properties["GroupName"] = "same & \"group\"";
        Assert.That(FormAssetEmitter.Html(page, "App.js", FormStyle.Portable),
            Does.Contain("name=\"same &amp; &quot;group&quot;\""));
    }

    [TestCase(FormLayoutKind.Canvas)]
    [TestCase(FormLayoutKind.Grid)]
    [TestCase(FormLayoutKind.Flow)]
    public void PortableHiddenAndBorders_WorkInEveryLayout_WithoutDisplayNoneRows(FormLayoutKind layout)
    {
        var page = Page(layout);
        page.FindById("group")!.Properties["Visible"] = "false";
        var html = FormAssetEmitter.Html(page, "App.js", FormStyle.Portable);
        var css = FormAssetEmitter.Css(page, FormStyle.Portable);
        Assert.Multiple(() =>
        {
            Assert.That(Tag(html, "group"), Does.Contain(" hidden"));
            Assert.That(css, Does.Contain(".vgs-form [hidden] { display: none !important; }"));
            Assert.That(css, Does.Contain(".vgs-form[hidden] { display: none !important; }"));
            Assert.That(css, Does.Contain("border: 1px solid").And.Contain(".vgs-GroupBox::before"));
            Assert.That(css, Does.Contain("border: 0; padding: 0; margin: 0"));
            Assert.That(css, Does.Not.Match(@"#group \{[^}]*display: none"));
            Assert.That(FormAssetEmitter.Css(page), Does.Not.Contain("border: 1px solid"));
        });
    }

    [Test]
    public void DisabledContainers_PreserveChildOwnStateAndUseTheRowsEnableTarget()
    {
        var page = Page();
        page.FindById("group")!.Properties["Enabled"] = "false";
        page.FindById("remember")!.Properties["Enabled"] = "false";
        var html = FormAssetEmitter.Html(page, "App.js", FormStyle.Portable);
        Assert.Multiple(() =>
        {
            Assert.That(Tag(html, "group"), Does.Contain(" disabled").And.Contain("data-vgs-enabled=\"false\""));
            Assert.That(Tag(html, "panel"), Does.Contain("aria-disabled=\"true\"").And.Contain("data-vgs-enabled=\"true\""));
            Assert.That(Tag(html, "top"), Does.Contain(" disabled").And.Contain("data-vgs-enabled=\"true\""));
            Assert.That(Tag(html, "remember"), Does.Not.Match(@"\sdisabled(?:\s|>)"));
            Assert.That(Regex.Match(html, "<label id=\"remember\".*?</label>").Value,
                Does.Contain("<input type=\"checkbox\" tabindex=\"0\" disabled checked"));
        });
    }

    [Test]
    public void PortableComboBox_TextSelectsAnOption_InsteadOfWritingATitle()
    {
        var page = Page();
        var combo = Box("ComboBox", "pick", w: 180);
        combo.Properties["Items"] = "Alpha, Beta, Gamma";
        combo.Properties["Text"] = "Beta";
        page.Controls.Add(combo);

        var html = FormAssetEmitter.Html(page, "App.js", FormStyle.Portable);
        var select = Regex.Match(html, "<select id=\"pick\".*?</select>", RegexOptions.Singleline).Value;
        Assert.Multiple(() =>
        {
            Assert.That(Tag(html, "pick"), Does.StartWith("<select ").And.Not.Contain("title="));
            Assert.That(select, Does.Contain("<option>Alpha</option><option selected>Beta</option><option>Gamma</option>"));
        });
    }

    [Test]
    public void PortableComboBox_AnExplicitSelectedIndexWinsOverText()
    {
        var page = Page();
        var combo = Box("ComboBox", "pick", w: 180);
        combo.Properties["Items"] = "Alpha, Beta, Gamma";
        combo.Properties["Text"] = "Beta";
        combo.Properties["SelectedIndex"] = "2";
        page.Controls.Add(combo);

        var html = FormAssetEmitter.Html(page, "App.js", FormStyle.Portable);
        Assert.That(html, Does.Contain("<option>Alpha</option><option>Beta</option><option selected>Gamma</option>"));
    }

    [Test]
    public void PortablePictureBox_WritesSrcAndPortableSizeModeCss_WithoutChangingTheDomPage()
    {
        var page = Page();
        var picture = Box("PictureBox", "photo", w: 200, h: 120);
        picture.Properties["Image"] = "images/a.png";
        picture.Properties["SizeMode"] = "CenterImage";
        page.Controls.Add(picture);

        var html = FormAssetEmitter.Html(page, "App.js", FormStyle.Portable);
        var css = FormAssetEmitter.Css(page, FormStyle.Portable);
        Assert.Multiple(() =>
        {
            Assert.That(Tag(html, "photo"), Does.StartWith("<img ").And.Contain("src=\"images/a.png\"")
                .And.Contain("alt=\"photo\"").And.Contain("data-vgs-sizemode=\"3\""));
            Assert.That(css, Does.Contain("#photo {").And.Contain("object-fit: none")
                .And.Contain("object-position: center"));
            Assert.That(FormAssetEmitter.Css(page), Does.Not.Contain("object-fit"));
        });
    }

    [Test]
    public void PortableValueKinds_MapTheirCatalogRowsToInputAndProgressMarkup()
    {
        var page = Page();
        var numeric = Box("NumericUpDown", "num");
        numeric.Properties["Minimum"] = "1";
        numeric.Properties["Maximum"] = "10";
        numeric.Properties["Value"] = "5";
        numeric.Properties["Increment"] = "2";
        numeric.Properties["ReadOnly"] = "true";
        numeric.Properties["TextAlign"] = "Right";
        var date = Box("DateTimePicker", "when", w: 200);
        var track = Box("TrackBar", "level", w: 150, h: 45);
        track.Properties["Minimum"] = "1";
        track.Properties["Maximum"] = "10";
        track.Properties["Value"] = "4";
        var progress = Box("ProgressBar", "bar", w: 150, h: 23);
        progress.Properties["Maximum"] = "200";
        progress.Properties["Value"] = "25";
        page.Controls.AddRange(new[] { numeric, date, track, progress });

        var html = FormAssetEmitter.Html(page, "App.js", FormStyle.Portable);
        var css = FormAssetEmitter.Css(page, FormStyle.Portable);
        Assert.Multiple(() =>
        {
            Assert.That(Tag(html, "num"), Does.StartWith("<input ").And.Contain("type=\"number\"")
                .And.Contain("min=\"1\"").And.Contain("max=\"10\"").And.Contain("value=\"5\"")
                .And.Contain("step=\"2\"").And.Contain("readonly"));
            Assert.That(Tag(html, "when"), Does.StartWith("<input ").And.Contain("type=\"date\"")
                .And.Not.Contain("value="));
            Assert.That(Tag(html, "level"), Does.StartWith("<input ").And.Contain("type=\"range\"")
                .And.Contain("min=\"1\"").And.Contain("max=\"10\"").And.Contain("value=\"4\""));
            Assert.That(Tag(html, "bar"), Does.StartWith("<progress ").And.Contain("max=\"200\"")
                .And.Contain("value=\"25\"").And.Not.Contain("min="));
            Assert.That(css, Does.Contain("#num {").And.Contain("text-align: right"));
        });
    }

    [Test]
    public void PortableStrips_RenderPageChromeWrappersAndNestedSubmenus()
    {
        // Grid/Flow pages put docked strips outside the form area as page chrome. A Canvas page
        // positions them inside it; that shape is covered by the canvas layout tests.
        var page = Page(FormLayoutKind.Grid);
        var menu = new FormControl { Kind = "MenuStrip", Id = "menuStrip1" };
        menu.Properties["Dock"] = "Top";
        var file = new FormControl { Kind = "ToolStripMenuItem", Id = "mnuFile" };
        file.Properties["Text"] = "&File";
        var open = new FormControl { Kind = "ToolStripMenuItem", Id = "mnuOpen" };
        open.Properties["Text"] = "&Open...";
        var separator = new FormControl { Kind = "ToolStripSeparator", Id = "sep1" };
        var exit = new FormControl { Kind = "ToolStripMenuItem", Id = "mnuExit" };
        exit.Properties["Text"] = "E&xit";
        file.Children.AddRange(new[] { open, separator, exit });
        menu.Children.Add(file);

        var tools = new FormControl { Kind = "ToolStrip", Id = "toolStrip1" };
        tools.Properties["Dock"] = "Top";
        var toolButton = new FormControl { Kind = "ToolStripButton", Id = "tsbOpen" };
        toolButton.Properties["Text"] = "Open";
        tools.Children.Add(toolButton);

        var status = new FormControl { Kind = "StatusStrip", Id = "statusStrip1" };
        status.Properties["Dock"] = "Bottom";
        var statusLabel = new FormControl { Kind = "ToolStripStatusLabel", Id = "lblStatus" };
        statusLabel.Properties["Text"] = "Ready";
        status.Children.Add(statusLabel);

        page.Controls.AddRange(new[] { menu, tools, status });
        var html = FormAssetEmitter.Html(page, "App.js", FormStyle.Portable);
        var css = FormAssetEmitter.Css(page, FormStyle.Portable);

        var divOpen = html.IndexOf("<div class=\"vgs-form\"", StringComparison.Ordinal);
        var divClose = html.IndexOf("</div>", StringComparison.Ordinal);
        Assert.Multiple(() =>
        {
            Assert.That(divOpen, Is.GreaterThanOrEqualTo(0));
            Assert.That(divClose, Is.GreaterThan(divOpen));
            Assert.That(html.IndexOf("<nav id=\"menuStrip1\"", StringComparison.Ordinal),
                Is.GreaterThanOrEqualTo(0).And.LessThan(divOpen));
            Assert.That(html.IndexOf("<menu id=\"toolStrip1\"", StringComparison.Ordinal),
                Is.GreaterThanOrEqualTo(0).And.LessThan(divOpen));
            Assert.That(html.IndexOf("<footer id=\"statusStrip1\"", StringComparison.Ordinal),
                Is.GreaterThan(divClose));

            Assert.That(Tag(html, "menuStrip1"), Does.StartWith("<nav ").And.Contain("class=\"vgs-MenuStrip\"")
                .And.Contain("data-vgs-dock=\"Top\""));
            Assert.That(Tag(html, "toolStrip1"), Does.StartWith("<menu ").And.Contain("class=\"vgs-ToolStrip\"")
                .And.Contain("role=\"toolbar\""));
            Assert.That(Tag(html, "statusStrip1"), Does.StartWith("<footer ").And.Contain("class=\"vgs-StatusStrip\"")
                .And.Contain("role=\"status\""));

            var pos = -1;
            void ExpectNext(string fragment)
            {
                var index = html.IndexOf(fragment, pos + 1, StringComparison.Ordinal);
                Assert.That(index, Is.GreaterThan(pos), $"expected '{fragment}' after position {pos}");
                pos = index;
            }

            ExpectNext("<ul>");
            ExpectNext("<li id=\"mnuFile\" class=\"vgs-ToolStripMenuItem\"");
            ExpectNext(">File");
            ExpectNext("<ul>");
            ExpectNext("<li id=\"mnuOpen\"");
            ExpectNext("<li id=\"sep1\" class=\"vgs-ToolStripSeparator\" role=\"separator\"");
            ExpectNext("<li id=\"mnuExit\"");
            ExpectNext(">Exit");
            ExpectNext("</ul>");
            ExpectNext("</li>");
            ExpectNext("<input id=\"tsbOpen\" class=\"vgs-ToolStripButton\"");
            ExpectNext("value=\"Open\"");
            ExpectNext("<span id=\"lblStatus\"");
            ExpectNext(">Ready</span>");

            Assert.That(html, Does.Not.Contain("&File"));
            Assert.That(html, Does.Not.Contain("&amp;File"));
            Assert.That(html, Does.Not.Contain("E&xit"));
            foreach (var id in new[] { "menuStrip1", "mnuFile", "mnuOpen", "sep1", "mnuExit", "toolStrip1", "tsbOpen", "statusStrip1", "lblStatus" })
            {
                Assert.That(Tag(html, id), Does.Not.Contain("tabindex"), $"{id} must not carry a tabindex");
            }

            Assert.That(css, Does.Contain(".vgs-MenuStrip ul{list-style:none"));
            Assert.That(css, Does.Contain(".vgs-ToolStrip{display:flex"));
            Assert.That(css, Does.Contain(".vgs-StatusStrip{display:flex"));
        });
    }

    [Test]
    public void FirstKindWebProperties_RecordTheirLiveDomMember()
    {
        foreach (var kind in new[] { "Button", "Label", "TextBox", "CheckBox", "RadioButton", "Panel", "GroupBox" })
            foreach (var property in FormControlCatalog.Find(kind)!.Properties.Where(p => p.AppliesTo(FormTarget.Web)))
                Assert.That(property.DomMember, Is.Not.Null.And.Not.Empty, kind + "." + property.Name);
        Assert.That(FormControlCatalog.Find("TextBox")!.Property("Text")!.DomMember, Is.EqualTo("value"));
        Assert.That(FormControlCatalog.Find("CheckBox")!.Property("Checked")!.DomMember, Is.EqualTo("querySelector(\"input\").checked"));
        Assert.That(FormControlCatalog.Find("Label")!.Property("BackColor")!.DomMember, Is.EqualTo("style.backgroundColor"));
    }

    [Test]
    public void EveryWebElementProperty_HasDomMemberMetadata()
    {
        var missing = FormControlCatalog.All.Where(c => c.HtmlTag != null)
            .SelectMany(c => c.Properties.Where(p => p.AppliesTo(FormTarget.Web) && string.IsNullOrEmpty(p.DomMember))
                .Select(p => c.Kind + "." + p.Name));
        Assert.That(missing, Is.Empty);
    }

    [Test]
    public void Emit_ReadsThePairedMarkers_AndRejectsUnknownStylesWithoutWritingAPage()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "bl-page-style-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var page = Page();
            page.SourcePath = Path.Combine(dir, "PortablePage.blwebform");
            var scaffold = FormScaffolder.Create(page.Name, portable: true);
            File.WriteAllText(FormCodeBehind.PathFor(page.SourcePath), scaffold.CodeText);
            var output = Path.Combine(dir, "out");
            FormAssetEmitter.Emit(output, "App.js", new[] { page });
            Assert.That(File.ReadAllText(Path.Combine(output, "PortablePage.html")), Does.Contain("<label id=\"remember\""));
            File.WriteAllText(FormCodeBehind.PathFor(page.SourcePath), scaffold.CodeText.Replace("style=\"portable\"", "style=\"unknown\""));
            var refused = Path.Combine(dir, "refused");
            Assert.That(() => FormAssetEmitter.Emit(refused, "App.js", new[] { page }),
                Throws.InvalidOperationException.With.Message.Contains(DesignCodes.RegionMalformed));
            Assert.That(File.Exists(Path.Combine(refused, "PortablePage.html")), Is.False);
        }
        finally { Directory.Delete(dir, true); }
    }

    [TestCase(false)]
    [TestCase(true)]
    [Category("Integration")]
    [NonParallelizable]
    public async Task ShippingBuildRoutes_SelectPortableRenderingFromThePairedMarkers(bool ideRoute)
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "bl-portable-page-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var page = Page();
            var scaffold = FormScaffolder.Create(page.Name, portable: true);
            var code = RegionWriter.Write(scaffold.CodeFileName, scaffold.CodeText, page, scaffold.DocumentFileName);
            Assert.That(code.Refused, Is.False);
            await File.WriteAllTextAsync(Path.Combine(dir, scaffold.CodeFileName), code.Text);
            await File.WriteAllTextAsync(Path.Combine(dir, scaffold.DocumentFileName), BasicLang.Forms.Serialization.FormDocumentWriter.Create(page));
            var projectPath = Path.Combine(dir, "Site.blproj");
            await File.WriteAllTextAsync(projectPath, """
                <Project><PropertyGroup><ProjectName>Site</ProjectName><AssemblyName>Site</AssemblyName>
                <TargetBackend>JavaScript</TargetBackend></PropertyGroup><ItemGroup>
                <Compile Include="PortablePage.bas" /><Compile Include="PortablePage.blwebform" />
                </ItemGroup></Project>
                """);
            string output;
            if (ideRoute)
            {
                var project = await new VisualGameStudio.ProjectSystem.Serialization.ProjectSerializer().LoadAsync(projectPath);
                var build = await new VisualGameStudio.ProjectSystem.Services.BuildService(
                    Moq.Mock.Of<VisualGameStudio.Core.Abstractions.Services.IOutputService>()).BuildProjectAsync(project);
                Assert.That(build.Success, Is.True, string.Join("\n", build.Errors));
                output = build.OutputPath!;
            }
            else
            {
                var (exit, stdout, stderr) = await CliTestHarness.RunCli(dir, "build", projectPath);
                Assert.That(exit, Is.Zero, stdout + "\n" + stderr);
                output = Path.GetDirectoryName(Directory.GetFiles(Path.Combine(dir, "bin"), "Site.js", SearchOption.AllDirectories).Single())!;
            }
            var html = await File.ReadAllTextAsync(Path.Combine(output, "PortablePage.html"));
            var css = await File.ReadAllTextAsync(Path.Combine(output, "PortablePage.css"));
            Assert.That(html, Does.Contain("<label id=\"remember\"").And.Contain("<textarea id=\"entry\"").And.Contain("<legend>Caption</legend>"));
            Assert.That(css, Does.Contain(".vgs-GroupBox::before").And.Contain("border: 1px solid"));
        }
        finally { Directory.Delete(dir, true); }
    }
}
