using BasicLang.Forms;
using NUnit.Framework;
using VisualGameStudio.Shell.ViewModels.Designer;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>Slice 4 Task 10 (D-5e): what a picked image becomes — relative inside the project, or a copy into Resources.</summary>
[TestFixture]
public class FormAssetImportTests
{
    private string _root = "";
    private string _project = "";

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "bl-import-" + Guid.NewGuid().ToString("N"));
        _project = Path.Combine(_root, "App");
        Directory.CreateDirectory(_project);
        File.WriteAllText(Path.Combine(_project, "App.blproj"), "<BasicLangProject/>");
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private string Write(string path, string text = "PNG")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private static Func<bool, FormAssetImportChoice> Always(FormAssetImportChoice choice) => _ => choice;

    private static Func<bool, FormAssetImportChoice> NeverAsked => _ => throw new AssertionException("must not ask");

    [Test]
    public void AFileInsideTheProject_IsStoredRelative_WithForwardSlashes()
    {
        var doc = Path.Combine(_project, "LoginForm.blform");
        var picked = Write(Path.Combine(_project, "Images", "sub", "x.png"));

        Assert.That(FormAssetImport.Import(picked, doc, FormTarget.WinForms, NeverAsked), Is.EqualTo("Images/sub/x.png"));
    }

    /// <summary>Part F: "outside" is a first SEGMENT of <c>..</c>; a project-root file whose NAME starts with two dots is inside.</summary>
    [Test]
    public void AProjectRootFileNamedDotDotSomething_IsInside_NotAskedAbout()
    {
        var doc = Path.Combine(_project, "LoginForm.blform");
        var picked = Write(Path.Combine(_project, "..logo.png"));

        Assert.Multiple(() =>
        {
            Assert.That(FormAssetImport.RelativeInsideProject(picked, doc), Is.EqualTo("..logo.png"));
            Assert.That(FormAssetImport.Import(picked, doc, FormTarget.WinForms, NeverAsked), Is.EqualTo("..logo.png"));
        });
    }

    /// <summary>A form in a subfolder still stores paths relative to the PROJECT (the .blproj's folder), as the build reads them.</summary>
    [Test]
    public void AFormInASubfolder_StoresThePathRelativeToTheProject()
    {
        var doc = Path.Combine(_project, "Forms", "LoginForm.blform");
        var picked = Write(Path.Combine(_project, "Images", "x.png"));

        Assert.That(FormAssetImport.Import(picked, doc, FormTarget.WinForms, NeverAsked), Is.EqualTo("Images/x.png"));
    }

    [Test]
    public void AFileOutside_Accepted_IsCopiedIntoResources()
    {
        var picked = Write(Path.Combine(_root, "Downloads", "x.png"), "bytes");

        var stored = FormAssetImport.Import(picked, Path.Combine(_project, "F.blform"), FormTarget.WinForms,
            Always(FormAssetImportChoice.CopyIntoProject));

        Assert.Multiple(() =>
        {
            Assert.That(stored, Is.EqualTo("Resources/x.png"));
            Assert.That(File.ReadAllText(Path.Combine(_project, "Resources", "x.png")), Is.EqualTo("bytes"));
        });
    }

    [Test]
    public void IdenticalBytesAlreadyThere_AreReused()
    {
        Write(Path.Combine(_project, "Resources", "x.png"), "same");
        var picked = Write(Path.Combine(_root, "Downloads", "x.png"), "same");

        var stored = FormAssetImport.Import(picked, Path.Combine(_project, "F.blform"), FormTarget.WinForms,
            Always(FormAssetImportChoice.CopyIntoProject));

        Assert.Multiple(() =>
        {
            Assert.That(stored, Is.EqualTo("Resources/x.png"));
            Assert.That(Directory.GetFiles(Path.Combine(_project, "Resources")), Has.Length.EqualTo(1), "no second copy");
        });
    }

    [Test]
    public void ADifferentFileOfThatName_BecomesNameTwo_NeverOverwrites()
    {
        Write(Path.Combine(_project, "Resources", "x.png"), "theirs");
        var picked = Write(Path.Combine(_root, "Downloads", "x.png"), "mine");

        var stored = FormAssetImport.Import(picked, Path.Combine(_project, "F.blform"), FormTarget.WinForms,
            Always(FormAssetImportChoice.CopyIntoProject));

        Assert.Multiple(() =>
        {
            Assert.That(stored, Is.EqualTo("Resources/x (2).png"));
            Assert.That(File.ReadAllText(Path.Combine(_project, "Resources", "x.png")), Is.EqualTo("theirs"), "never overwritten");
            Assert.That(File.ReadAllText(Path.Combine(_project, "Resources", "x (2).png")), Is.EqualTo("mine"));
        });
    }

    [Test]
    public void DecliningOnWinForms_StoresTheAbsolutePath()
    {
        var picked = Write(Path.Combine(_root, "Downloads", "x.png"));

        var stored = FormAssetImport.Import(picked, Path.Combine(_project, "F.blform"), FormTarget.WinForms,
            Always(FormAssetImportChoice.KeepAbsolutePath));

        Assert.That(stored, Is.EqualTo(Path.GetFullPath(picked)));
    }

    /// <summary>On the web, keeping the absolute path is never OFFERED (a page cannot reach it), and forcing it writes nothing.</summary>
    [Test]
    public void OnTheWeb_KeepingTheAbsolutePath_IsNotOffered_AndWritesNothing()
    {
        var picked = Write(Path.Combine(_root, "Downloads", "x.png"));
        bool? offered = null;

        var stored = FormAssetImport.Import(picked, Path.Combine(_project, "F.blwebform"), FormTarget.Web, offer =>
        {
            offered = offer;
            return FormAssetImportChoice.KeepAbsolutePath;
        });

        Assert.Multiple(() =>
        {
            Assert.That(offered, Is.False, "not offered on the web");
            Assert.That(stored, Is.Null, "nothing written");
            Assert.That(Directory.Exists(Path.Combine(_project, "Resources")), Is.False);
        });
    }

    [Test]
    public void Cancel_WritesNothing()
    {
        var picked = Write(Path.Combine(_root, "Downloads", "x.png"));

        Assert.That(FormAssetImport.Import(picked, Path.Combine(_project, "F.blform"), FormTarget.WinForms,
            Always(FormAssetImportChoice.Cancel)), Is.Null);
    }

    [Test]
    public void WithNoProjectFile_TheDocumentsFolderIsTheProject()
    {
        File.Delete(Path.Combine(_project, "App.blproj"));
        var folder = Path.Combine(_project, "Loose");
        var picked = Write(Path.Combine(folder, "img", "x.png"));

        Assert.That(FormAssetImport.Import(picked, Path.Combine(folder, "F.blform"), FormTarget.WinForms, NeverAsked),
            Is.EqualTo("img/x.png"));
    }
}
