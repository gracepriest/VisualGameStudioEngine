using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Slice 4 Task 9 (D-5c/d): the ONE build-copy helper both routes call — sub-paths preserved, containment, BL8036 for what it
/// did not copy, each file once, temp + rename. Fast: temp directories only.
/// </summary>
[TestFixture]
public class FormAssetCopyTests
{
    private string _project = "";
    private string _output = "";

    [SetUp]
    public void SetUp()
    {
        var root = Path.Combine(Path.GetTempPath(), "bl-assetcopy-" + Guid.NewGuid().ToString("N"));
        _project = Path.Combine(root, "proj");
        _output = Path.Combine(root, "proj", "bin", "Debug", "net8.0");
        Directory.CreateDirectory(_project);
        Directory.CreateDirectory(_output);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(Path.GetDirectoryName(_project)!, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private void WriteProjectFile(string relative, string text = "PNG")
    {
        var path = Path.Combine(_project, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static LoadedForm Form(FormTarget target, string documentPath, params (string Id, string Image)[] pictures)
    {
        var form = new FormDocument { Target = target, Name = "LoginForm", Width = 400, Height = 300 };
        foreach (var (id, image) in pictures)
        {
            var pic = new FormControl
            {
                Kind = "PictureBox", Id = id, TabIndex = 0,
                Geometry = target == FormTarget.WinForms ? new PixelGeometry { X = 0, Y = 0, Width = 10, Height = 10 } : new GridGeometry()
            };
            pic.Properties["Image"] = image;
            form.Controls.Add(pic);
        }

        return new LoadedForm(form, documentPath);
    }

    private (IReadOnlyList<string> Written, List<DesignDiagnostic> Reported) Copy(params LoadedForm[] forms)
    {
        var reported = new List<DesignDiagnostic>();
        var written = FormAssetCopy.Copy(forms, _project, _output, reported.Add);
        return (written, reported);
    }

    private string Doc => Path.Combine(_project, "LoginForm.blform");

    [TestCase(FormTarget.WinForms)]
    [TestCase(FormTarget.Web)]
    public void AProjectImage_IsCopiedBesideTheOutput_SubPathPreserved(FormTarget target)
    {
        WriteProjectFile("Resources/sub/logo.png", "the bytes");

        var (written, reported) = Copy(Form(target, Doc, ("pic", @"Resources\sub\logo.png")));
        var copy = Path.Combine(_output, "Resources", "sub", "logo.png");

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(copy), Is.EqualTo("the bytes"));
            Assert.That(written, Is.EqualTo(new[] { copy }));
            Assert.That(reported, Is.Empty);
            Assert.That(Directory.GetFiles(_output, "*.tmp", SearchOption.AllDirectories), Is.Empty, "no temp left behind");
        });
    }

    [Test]
    public void AMissingFile_IsBL8036_NamingTheOwnerAndTheAbsolutePath_AndNeverFailsTheBuild()
    {
        var (written, reported) = Copy(Form(FormTarget.WinForms, Doc, ("pic", "Resources/gone.png")));
        var warning = reported.Single();

        Assert.Multiple(() =>
        {
            Assert.That(written, Is.Empty);
            Assert.That(warning.Code, Is.EqualTo(DesignCodes.AssetNotCopied));
            Assert.That(warning.Code, Is.EqualTo("BL8036"));
            Assert.That(warning.IsWarning, Is.True, "a warning — the build never fails for it");
            Assert.That(warning.Message, Does.Contain("'LoginForm.pic.Image'")
                .And.Contain(Path.Combine(_project, "Resources", "gone.png")));
            Assert.That(warning.FilePath, Is.EqualTo(Doc), "the form document, for the Error List");
        });
    }

    [Test]
    public void APathOutsideTheProject_IsBL8036_AndNothingIsWrittenOutsideTheOutput()
    {
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(_project)!, "x.png"), "outside");

        var (written, reported) = Copy(Form(FormTarget.WinForms, Doc, ("pic", "../x.png")));

        Assert.Multiple(() =>
        {
            Assert.That(written, Is.Empty);
            Assert.That(reported.Single().Message, Does.Contain("outside the project"));
            Assert.That(File.Exists(Path.Combine(Path.GetDirectoryName(_output)!, "x.png")), Is.False);
            Assert.That(Directory.GetFiles(_output, "*", SearchOption.AllDirectories), Is.Empty);
        });
    }

    [Test]
    public void AnAbsolutePath_IsNotCopied_AndBL8036SaysTheProgramLooksForItThere()
    {
        var absolute = Path.Combine(Path.GetDirectoryName(_project)!, "abs.png");
        File.WriteAllText(absolute, "abs");

        var (written, reported) = Copy(Form(FormTarget.WinForms, Doc, ("pic", absolute)));

        Assert.Multiple(() =>
        {
            Assert.That(written, Is.Empty);
            Assert.That(reported.Single().Message, Does.Contain("absolute path"));
        });
    }

    [Test]
    public void AUrl_IsNeitherCopiedNorWarned()
    {
        var (written, reported) = Copy(Form(FormTarget.Web, Doc, ("pic", "https://example.com/a.png")));

        Assert.Multiple(() =>
        {
            Assert.That(written, Is.Empty);
            Assert.That(reported, Is.Empty);
        });
    }

    [Test]
    public void TheSameFileReferencedTwice_IsCopiedOnce()
    {
        WriteProjectFile("Resources/logo.png");

        var (written, _) = Copy(
            Form(FormTarget.WinForms, Doc, ("a", "Resources/logo.png"), ("b", @"Resources\logo.png")),
            Form(FormTarget.WinForms, Doc, ("c", "Resources/logo.png")));

        Assert.That(written, Has.Count.EqualTo(1));
    }

    [Test]
    public void TheFormsIcon_IsCopiedToo()
    {
        WriteProjectFile("Resources/app.ico", "ICO");
        var form = Form(FormTarget.WinForms, Doc);
        form.Model.Properties["Icon"] = "Resources/app.ico";

        Copy(form);

        Assert.That(File.ReadAllText(Path.Combine(_output, "Resources", "app.ico")), Is.EqualTo("ICO"));
    }

    /// <summary>A rebuild over an existing copy replaces it (temp + rename) — the new bytes win, and no temp is left.</summary>
    [Test]
    public void ARebuild_ReplacesTheExistingCopy()
    {
        WriteProjectFile("Resources/logo.png", "old");
        Copy(Form(FormTarget.WinForms, Doc, ("pic", "Resources/logo.png")));
        WriteProjectFile("Resources/logo.png", "new");

        Copy(Form(FormTarget.WinForms, Doc, ("pic", "Resources/logo.png")));

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(Path.Combine(_output, "Resources", "logo.png")), Is.EqualTo("new"));
            Assert.That(Directory.GetFiles(_output, "*.tmp", SearchOption.AllDirectories), Is.Empty);
        });
    }

    [Test]
    public void ARefusedValue_IsNotCopied_TheGeneratorNamesIt()
    {
        WriteProjectFile("Resources/a.svg");

        var (written, reported) = Copy(Form(FormTarget.WinForms, Doc, ("pic", "Resources/a.svg")));

        Assert.Multiple(() =>
        {
            Assert.That(written, Is.Empty, "WinForms cannot decode .svg; nothing references a copy of it");
            Assert.That(reported, Is.Empty, "BL8009 names it at generation, not BL8036");
        });
    }

    // ==================================================================
    // FormDocumentLoader — the refusal's consequence is the route's own
    // ==================================================================

    [Test]
    public void ARefusedBlform_OnTheCSharpRoute_SaysItsImagesWereNotCopied_NeverThatNoPageWasMade()
    {
        var bad = Path.Combine(_project, "Bad.blform");
        File.WriteAllText(bad, "<Form Name=\"Bad\" Version=\"99\"/>");
        var warnings = new List<string>();

        var loaded = FormDocumentLoader.Load(new[] { bad }, FormTarget.WinForms,
            "— its images and icons were not copied into the output", warnings.Add);

        Assert.Multiple(() =>
        {
            Assert.That(loaded, Is.Empty);
            Assert.That(warnings.Single(), Does.Contain("images and icons were not copied").And.Not.Contain("turned into a page"));
        });
    }

    [Test]
    public void ARefusedBlwebform_OnTheWebRoute_KeepsItsText()
    {
        var bad = Path.Combine(_project, "Bad.blwebform");
        File.WriteAllText(bad, "<WebForm Name=\"Bad\" Version=\"99\"/>");
        var warnings = new List<string>();

        var loaded = FormDocumentLoader.LoadWebForms(new[] { bad }, warnings.Add);

        Assert.Multiple(() =>
        {
            Assert.That(loaded, Is.Empty);
            Assert.That(warnings.Single(), Does.Contain("'Bad.blwebform' was not turned into a page"));
        });
    }
}
