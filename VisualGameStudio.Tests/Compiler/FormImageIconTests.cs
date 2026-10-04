using BasicLang.Forms;
using BasicLang.Forms.Serialization;
using NUnit.Framework;
using VisualGameStudio.Shell.ViewModels.Designer;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Slice 4 Task 8 (D-5a/b/f): the Image and Icon types — their per-target value rules (each a run-time failure
/// otherwise), the fully qualified WinForms literal anchored to the program's folder, and the page's <c>src</c> / icon.
/// </summary>
[TestFixture]
public class FormImageIconTests
{
    private static FormPropertyDef Image => FormControlCatalog.Find("PictureBox")!.Property("Image")!;
    private static FormPropertyDef Icon => FormControlCatalog.FormRoot.Property("Icon")!;

    [TestCase("Resources/logo.png", true, true)]
    [TestCase(@"Resources\logo.png", true, true)]
    [TestCase("logo.jpg", true, true)]
    [TestCase(@"C:\pics\a.png", true, false)]
    [TestCase("https://example.com/a.png", false, true)]
    [TestCase("Resources/a.svg", false, true)]
    [TestCase("Resources/a.webp", false, true)]
    [TestCase("../outside.png", false, false)]
    [TestCase("a/../../outside.png", false, false)]
    [TestCase("", false, false)]
    public void TheImageRules_PerTarget(string value, bool winForms, bool web)
    {
        Assert.Multiple(() =>
        {
            Assert.That(Image.Accepts(value, FormTarget.WinForms), Is.EqualTo(winForms), "WinForms");
            Assert.That(Image.Accepts(value, FormTarget.Web), Is.EqualTo(web), "web");
        });
    }

    [TestCase("Resources/app.ico", true, true)]
    [TestCase("Resources/app.png", false, true)]
    [TestCase("Resources/app.svg", false, true)]
    [TestCase("Resources/app.gif", false, true)]
    [TestCase("Resources/app.jpg", false, false)]
    [TestCase(@"C:\icons\app.ico", true, false)]
    [TestCase("https://example.com/favicon", false, true)]
    [TestCase("../app.ico", false, false)]
    public void TheIconRules_PerTarget(string value, bool winForms, bool web)
    {
        Assert.Multiple(() =>
        {
            Assert.That(Icon.Accepts(value, FormTarget.WinForms), Is.EqualTo(winForms), "WinForms");
            Assert.That(Icon.Accepts(value, FormTarget.Web), Is.EqualTo(web), "web");
        });
    }

    [Test]
    public void EachRefusal_SaysWhy()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Image.DescribeRefusal("https://x.com/a.png", FormTarget.WinForms), Does.Contain("web address"));
            Assert.That(Image.DescribeRefusal("a.svg", FormTarget.WinForms), Does.Contain("GDI+"));
            Assert.That(Icon.DescribeRefusal("logo.png", FormTarget.WinForms), Does.Contain("requires .ico"));
            Assert.That(Image.DescribeRefusal(@"C:\a.png", FormTarget.Web), Does.Contain("author's machine"));
            Assert.That(Image.DescribeRefusal("../a.png", FormTarget.WinForms), Does.Contain("'..'"));
        });
    }

    [Test]
    public void ABackslashPath_IsCanonicalWithForwardSlashes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Image.Canonical(@"Resources\sub\logo.png"), Is.EqualTo("Resources/sub/logo.png"));
            Assert.That(Image.Canonical(@"C:\pics\a.png"), Is.EqualTo(@"C:\pics\a.png"), "a rooted path is the user's own");
        });
    }

    // ==================================================================
    // WinForms emission (D-5b) — the exact fully qualified shape
    // ==================================================================

    private static string Emit(Action<FormDocument> build, string codeText = "")
    {
        var form = new FormDocument { Target = FormTarget.WinForms, Name = "F", Width = 400, Height = 300, Text = "F" };
        build(form);
        var scaffold = FormScaffolder.Create("F", FormTarget.WinForms).CodeText;
        var result = RegionWriter.Write("F.bas", codeText.Length == 0 ? scaffold : codeText, form, "F.blform");
        return result.Text + "\n" + string.Join("\n", result.Diagnostics.Select(d => d.Message));
    }

    private static Action<FormDocument> Picture(string image) => form =>
    {
        var pic = new FormControl
        {
            Kind = "PictureBox", Id = "pic", TabIndex = 0, Geometry = new PixelGeometry { X = 0, Y = 0, Width = 10, Height = 10 }
        };
        pic.Properties["Image"] = image;
        form.Controls.Add(pic);
    };

    [Test]
    public void AProjectImage_IsAnchoredToTheProgramsFolder_FullyQualified()
    {
        Assert.That(Emit(Picture(@"Resources\logo.png")), Does.Contain(
            "pic.Image = System.Drawing.Image.FromFile(System.IO.Path.Combine(System.AppContext.BaseDirectory, \"Resources/logo.png\"))"));
    }

    [Test]
    public void ARootedImage_IsWrittenAsItStands()
    {
        Assert.That(Emit(Picture(@"C:\pics\a.png")), Does.Contain("pic.Image = System.Drawing.Image.FromFile(\"C:\\pics\\a.png\")"));
    }

    [Test]
    public void TheFormsIcon_IsANewSystemDrawingIcon_AnchoredToTheProgramsFolder()
    {
        Assert.That(Emit(form => form.Properties["Icon"] = "Resources/app.ico"), Does.Contain(
            "Me.Icon = New System.Drawing.Icon(System.IO.Path.Combine(System.AppContext.BaseDirectory, \"Resources/app.ico\"))"));
    }

    [Test]
    public void APngIcon_OnWinForms_IsWarnedAndNotEmitted()
    {
        var code = Emit(form => form.Properties["Icon"] = "logo.png");

        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Not.Contain("Me.Icon ="));
            Assert.That(code, Does.Contain(DesignCodes.DegradedProperty).And.Contain("requires .ico"));
        });
    }

    [TestCase("https://example.com/a.png")]
    [TestCase("a.webp")]
    [TestCase("../a.png")]
    public void ARefusedImage_IsNeverEmitted(string image)
    {
        Assert.That(Emit(Picture(image)), Does.Not.Contain("pic.Image ="));
    }

    /// <summary>
    /// ⛔ Fully qualified because a user <c>Using</c> can make a bare <c>Image</c> ambiguous (CS0104, BasicLang silent): the
    /// emitted call names <c>System.Drawing.Image</c> whatever the file imports.
    /// </summary>
    [Test]
    public void UnderAUsingThatWouldMakeImageAmbiguous_TheCallIsStillQualified()
    {
        var scaffold = FormScaffolder.Create("F", FormTarget.WinForms).CodeText;
        var code = Emit(Picture("Resources/logo.png"), "Using System.Windows.Controls\n" + scaffold);

        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Contain("System.Drawing.Image.FromFile("));
            Assert.That(code, Does.Not.Contain(" Image.FromFile("), "never a bare Image");
        });
    }

    /// <summary>
    /// ⛔ The region writer's last line of defence: an Image/Icon value that cannot become source must THROW in
    /// <c>Literal</c>, never fall through to a quoted string (<c>pic.Image = "x.png"</c> is CS0029, BasicLang silent).
    /// Unreachable by construction (every caller skips a Degraded value first), so it is forced here by reflection.
    /// </summary>
    [TestCase("../outside.png")]
    [TestCase("https://example.com/a.png")]
    public void AnUnwritableImage_ForcedIntoLiteral_Throws_NeverQuotes(string value)
    {
        var literal = typeof(RegionWriter).GetMethod("Literal",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        var thrown = Assert.Throws<System.Reflection.TargetInvocationException>(() => literal.Invoke(null, new object?[] { Image, value }));
        Assert.That(thrown!.InnerException, Is.InstanceOf<InvalidOperationException>());
    }

    // ==================================================================
    // The page (D-5f)
    // ==================================================================

    private static string Page(Action<FormDocument> build)
    {
        var form = new FormDocument { Target = FormTarget.Web, Name = "F" };
        build(form);
        return FormAssetEmitter.Html(form, "App.js");
    }

    private static Action<FormDocument> WebPicture(string image) => form =>
    {
        var pic = new FormControl { Kind = "PictureBox", Id = "pic", TabIndex = 0, Geometry = new GridGeometry() };
        pic.Properties["Image"] = image;
        form.Controls.Add(pic);
    };

    [Test]
    public void TheImgSrc_IsPercentEncodedPerSegment()
    {
        Assert.That(Page(WebPicture("Resources/my logo#1.png")), Does.Contain("src=\"Resources/my%20logo%231.png\""));
    }

    [Test]
    public void AUrlIsKept_AndARootedPathIsNotEmitted()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Page(WebPicture("https://example.com/a.png")), Does.Contain("src=\"https://example.com/a.png\""));
            Assert.That(Page(WebPicture(@"C:\pics\a.png")), Does.Not.Match("<img[^>]*src="), "the author's disk is never a src");
        });
    }

    [Test]
    public void TheFormsIcon_IsThePagesIcon_AfterTheTitle()
    {
        var html = Page(form => form.Properties["Icon"] = "Resources/app.ico");
        var title = html.IndexOf("</title>", StringComparison.Ordinal);
        var link = html.IndexOf("<link rel=\"icon\" href=\"Resources/app.ico\">", StringComparison.Ordinal);

        Assert.That(link, Is.GreaterThan(title).And.GreaterThan(0), html);
    }

    // ==================================================================
    // The grid (IsTextBox) — catalog-driven over every Image/Icon row
    // ==================================================================

    private static IEnumerable<TestCaseData> AssetRows() =>
        FormControlCatalog.All.Append(FormControlCatalog.FormRoot)
            .SelectMany(k => k.Properties.Where(p => p.Type is FormPropertyType.Image or FormPropertyType.Icon)
                .Select(p => new TestCaseData(k.Kind, p.Name).SetName($"{{m}}({k.Kind}.{p.Name})")));

    [TestCaseSource(nameof(AssetRows))]
    public void EveryImageOrIconRow_IsATextBox_AndTypingAProjectPathWritesIt(string kind, string name)
    {
        var xml = kind == "Form"
            ? "<Form Name=\"F\" Version=\"1\" Width=\"400\" Height=\"300\"><Controls/></Form>"
            : $"<Form Name=\"F\" Version=\"1\" Width=\"400\" Height=\"300\"><Controls><{kind} Id=\"c\" X=\"0\" Y=\"0\" Width=\"10\" Height=\"10\" TabIndex=\"0\"/></Controls></Form>";
        var file = FormDocumentReader.Read("F.blform", xml);
        var grid = new FormPropertyGridViewModel();
        grid.Load(file);
        if (kind != "Form")
        {
            grid.SelectedControl = file.Model.FindById("c");
        }

        var row = grid.Rows.Single(r => r.Name == name);
        var value = name == "Icon" ? "Resources/app.ico" : "Resources/a.png";
        row.StringValue = value;

        Assert.Multiple(() =>
        {
            Assert.That(row.IsTextBox, Is.True);
            Assert.That(FormDocumentWriter.Write(file), Does.Contain($"{name}=\"{value}\""));
        });
    }
}
