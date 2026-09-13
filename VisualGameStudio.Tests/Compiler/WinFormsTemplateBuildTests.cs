using BasicLang.Forms;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 18: build coverage for the WinForms templates, and the equivalence that keeps them from
/// drifting apart again.
///
/// <para>⛔⛔ <b>The canonical VSIX template had ZERO build coverage its whole life.</b>
/// <c>ProjectTemplates.All</c> is hard-coded and <c>TemplateBuildSweepTests</c>' cases are
/// hand-written strings, so the VSIX files — which are not in either list — gained nothing
/// automatically. They had recognizer coverage (<c>FormRecognizerTests</c> reads them) and nothing
/// that asked whether they COMPILE.</para>
///
/// <para>Each template file is compiled by the real BasicLang CLI and the resulting C# files are
/// type-checked TOGETHER by csc against the WinForms reference assemblies. That is the whole
/// pipeline a user gets, minus the final link — and every defect a template can carry (a property
/// the control does not have, a struct return assigned through, a handler shape that does not
/// match) is a compile error, not a link error.</para>
///
/// <para>⚠ Compiled one file at a time on purpose. The CLI takes exactly ONE source file; naming
/// two used to compile the first and silently ignore the rest (measured on this very template,
/// which is why it now refuses). Multi-file is what a project is for, and a project build here
/// would need the WindowsDesktop MSBuild SDK, which does not exist off Windows.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class WinFormsTemplateBuildTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-wftpl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown() { try { Directory.Delete(_dir, true); } catch { } }

    private static string? FindRepoFile(params string[] relativeParts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        return null;
    }

    private static string ReadVsixFile(string name)
    {
        var path = FindRepoFile("BasicLang.VisualStudio", "src", "BasicLang.VisualStudio",
            "Templates", "Projects", "WinFormsApp", name);
        Assert.That(path, Is.Not.Null, $"the VSIX WinFormsApp/{name} template is missing");
        return File.ReadAllText(path!).Replace("$safeprojectname$", "VsixApp");
    }

    // ==================================================================
    // The shipped templates must compile
    // ==================================================================

    [Test]
    public void TheVsixTemplate_TypeChecks()
    {
        // Owner decision 3 makes this shape canonical. Nothing had ever asked whether it builds.
        var generated = new[]
        {
            CompileToCSharp("MainForm.bas", ReadVsixFile("MainForm.bas")),
            CompileToCSharp("Program.bas", ReadVsixFile("Program.bas"))
        };

        WinFormsCompile.AssertCompiles(generated,
            "the canonical VSIX WinForms template must compile.");
    }

    [Test]
    public async Task TheIdeTemplate_TypeChecks()
    {
        var files = await GenerateIdeWinFormsProjectAsync();

        var generated = new[]
        {
            CompileToCSharp("MainForm.bas", files["MainForm.bas"]),
            CompileToCSharp("Main.bas", files["Main.bas"])
        };

        WinFormsCompile.AssertCompiles(generated,
            "the IDE's File -> New Project WinForms template must compile.");
    }

    // ==================================================================
    // ...and must not drift apart again
    // ==================================================================

    [Test]
    public async Task TheIdeAndVsixTemplates_AgreeOnTheShapeTheDesignerWritesInto()
    {
        // ⛔ These are two separate implementations of the same template and only a test stops them
        // diverging. They DID diverge: until 2026-09-13 the IDE emitted one file with everything
        // inline in Public Sub New(), no InitializeComponent at all, and a handler called
        // OnButtonClick, while the VSIX emitted two files with an InitializeComponent and
        // btnClick_Click. The designer has to write into whichever one the user happens to have.
        var ide = await GenerateIdeWinFormsProjectAsync();
        var vsix = ReadVsixFile("MainForm.bas");

        foreach (var (label, source) in new[] { ("IDE", ide["MainForm.bas"]), ("VSIX", vsix) })
        {
            var form = BasicLang.Forms.Recognizer.WinFormsDialect.Read(source);

            Assert.Multiple(() =>
            {
                Assert.That(form.ClassName, Is.EqualTo("MainForm"), $"{label}: the form class name");
                Assert.That(form.BuiltIn, Is.EqualTo("InitializeComponent"),
                    $"{label}: controls must be built in InitializeComponent — that is the region " +
                    "the designer owns and regenerates");
                Assert.That(form.Controls.Select(c => c.Id),
                    Is.EqualTo(new[] { "lblMessage", "btnClick" }), $"{label}: the same two controls");
                Assert.That(form["btnClick"]!.Handlers.Single().Handler, Is.EqualTo("btnClick_Click"),
                    $"{label}: handlers are named <control>_<Event>, which is what the designer " +
                    "generates and what its recognizer expects on import");
                Assert.That(form.IsRefused, Is.False, $"{label}: the designer must be able to read it");
            });
        }
    }

    [Test]
    public async Task TheIdeTemplate_ListsEverySourceFileItWrites()
    {
        // ⛔⛔ GetSourceFiles() globs **/*.bas ONLY while a project has no explicit <Compile>
        // items, and these templates ARE explicit. A file written to disk but left off the list is
        // an orphan excluded from the build, and nothing reports it — the project just compiles
        // without its form.
        var files = await GenerateIdeWinFormsProjectAsync();
        var project = BasicLang.Compiler.ProjectSystem.ProjectFile.Load(_projectPath!);

        var listed = project.GetSourceFiles().Select(Path.GetFileName).ToList();

        Assert.That(listed, Is.SupersetOf(files.Keys),
            "every .bas the template wrote must reach the build");
    }

    // ==================================================================
    // Harness
    // ==================================================================

    private string? _projectPath;

    private async Task<Dictionary<string, string>> GenerateIdeWinFormsProjectAsync()
    {
        var result = await new ProjectTemplateService().CreateProjectAsync(new CreateProjectOptions
        {
            Name = "IdeApp",
            Location = _dir,
            SolutionType = SolutionTypes.DotNet,
            Template = ProjectTemplates.All.Single(t => t.Id == "winforms-app"),
            CreateSolutionFolder = false,
            CreateGitRepository = false
        });

        Assert.That(result.Success, Is.True, result.Error);
        _projectPath = result.ProjectPath;

        var dir = Path.GetDirectoryName(result.ProjectPath!)!;
        return Directory.GetFiles(dir, "*.bas")
            .ToDictionary(Path.GetFileName, File.ReadAllText)!;
    }

    /// <summary>One BasicLang file through the REAL compiler, returning the C# it emitted.</summary>
    private string CompileToCSharp(string fileName, string source)
    {
        var dir = Path.Combine(_dir, "c-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        var path = Path.Combine(dir, fileName);
        File.WriteAllText(path, source);

        var (exit, stdout, stderr) = CliTestHarness.RunProcess(
            CliTestHarness.CliPath(), new[] { path, "--target=csharp" }, dir, timeoutMs: 120_000);

        Assert.That(exit, Is.Zero,
            $"the real compiler rejected {fileName}.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");

        var emitted = Path.ChangeExtension(path, ".cs");
        Assert.That(File.Exists(emitted), Is.True,
            $"compiler reported success but emitted no C# for {fileName}.\nSTDOUT:\n{stdout}");

        return File.ReadAllText(emitted);
    }
}
