using System.Reflection;
using BasicLang.Compiler;
using BasicLang.Compiler.ProjectSystem;
using NUnit.Framework;
using VisualGameStudio.Core.Constants;
using VisualGameStudio.Core.Utilities;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 10: the form-document extensions, and the eleven-odd independent places that have to agree
/// about them.
///
/// <para>⛔ The knowledge "which extensions are source" is DUPLICATED in this repo, not shared —
/// three separate lists, two of which must never gain these extensions and one of which must. These
/// tests exist because nothing else makes the three disagree loudly.</para>
/// </summary>
[TestFixture]
public class FormDocumentExtensionTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-formext-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown() { try { Directory.Delete(_dir, true); } catch { } }

    // ==================================================================
    // The three lists
    // ==================================================================

    [Test]
    public void IdeSourceList_IncludesTheFormExtensions()
    {
        // D11: form documents ride as <Compile> items so the build can find them. "Source" on the
        // IDE side means "a project item the IDE compiles into the program", not "text the lexer
        // reads" — the compiler skips them on both routes.
        Assert.That(FileExtensions.SourceExtensions, Does.Contain(".blform").And.Contain(".blwebform"));
        Assert.That(FileExtensions.IsSourceFile("MainForm.blwebform"), Is.True,
            "ProjectService uses this to choose Compile over Content");
    }

    [Test]
    public void CompilerSourceList_ExcludesTheFormExtensions()
    {
        // ⛔ This list drives the DEFAULT SOURCE GLOB, which feeds File.ReadAllText straight into
        // the BasicLang lexer. A form document swept in by the glob would arrive with no <Compile>
        // item and no diagnostic — the exact shape that has bitten this repo before.
        Assert.That(ProjectFile.BasicLangSourceExtensions,
            Does.Not.Contain(".blform").And.Not.Contain(".blwebform"));
    }

    [Test]
    public void ModuleResolverList_ExcludesTheFormExtensions()
    {
        // The second copy of the same list. It resolves an Import/Using to a file that will be
        // lexed as BasicLang; a form document is XML and is not importable.
        var field = typeof(ModuleResolver).GetField("SupportedExtensions",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(field, Is.Not.Null,
            "ModuleResolver.SupportedExtensions was renamed — this test pins that it must never " +
            "gain the form extensions, so update it rather than deleting it");

        var extensions = (string[])field!.GetValue(null)!;
        Assert.That(extensions, Does.Not.Contain(".blform").And.Not.Contain(".blwebform"));
    }

    [Test]
    public void TheThreeListsDisagree_Deliberately()
    {
        // Stated as an assertion so the difference reads as intent rather than an oversight: if
        // someone "fixes" the inconsistency by unifying them, this fails and says why.
        var ide = FileExtensions.SourceExtensions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var compiler = ProjectFile.BasicLangSourceExtensions.ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.That(ide.Except(compiler), Is.EquivalentTo(new[] { ".blform", ".blwebform" }),
            "the ONLY difference between the IDE's source list and the compiler's is the two form " +
            "document formats. Any other divergence is a bug in one of them.");
    }

    // ==================================================================
    // 8.3 short-name safety
    // ==================================================================

    [Test]
    public void FormExtensions_AreSafeAgainstTheShortNameGlobSweep()
    {
        // A 3-character glob sweeps longer extensions on Windows: *.bas really does return
        // Lib.basic. Any extension beginning bas/cls/mod/bli would be swept into the compile set
        // with no <Compile> item and no diagnostic.
        // The sweep is on the 3-character truncation: *.bas matches anything whose extension starts
        // "bas". So a form extension is safe exactly when its first three characters differ from
        // every compiler source extension's first three.
        static string Short(string extension)
        {
            var body = extension.TrimStart('.');
            return body.Length <= 3 ? body : body.Substring(0, 3);
        }

        var sourceShorts = ProjectFile.BasicLangSourceExtensions
            .Select(Short).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var ext in FileExtensions.FormDocumentExtensions)
        {
            Assert.That(sourceShorts, Does.Not.Contain(Short(ext)),
                $"'{ext}' truncates to '{Short(ext)}', which a source-extension glob would sweep in " +
                "with no <Compile> item and no diagnostic");
        }

        Assert.That(Short(".blform"), Is.EqualTo("blf"));
        Assert.That(Short(".blwebform"), Is.EqualTo("blw"));
    }

    // ==================================================================
    // The compiler skips them
    // ==================================================================

    [TestCase("MainForm.blform")]
    [TestCase("LoginForm.blwebform")]
    [TestCase("MainForm.BLWEBFORM")]
    public void IsFormDocument_RecognisesBothExtensions_CaseInsensitively(string path)
    {
        Assert.That(BasicCompiler.IsFormDocument(path), Is.True);
    }

    [TestCase("Program.bas")]
    [TestCase("dom-core.bli")]
    [TestCase("Widget.cls")]
    public void IsFormDocument_IsFalse_ForRealSource(string path)
    {
        Assert.That(BasicCompiler.IsFormDocument(path), Is.False);
    }

    [Test]
    public void AFormDocument_IsNotAnEntryPointCandidate()
    {
        // IsEntryLikeFile decides which units may hold Main. A form document is not source at all,
        // so treating it as a candidate would make "no entry point" diagnostics point at XML.
        var method = typeof(BasicCompiler).GetMethod("IsEntryLikeFile",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(method, Is.Not.Null);

        Assert.Multiple(() =>
        {
            Assert.That(method!.Invoke(null, new object[] { "MainForm.blform" }), Is.False);
            Assert.That(method.Invoke(null, new object[] { "LoginForm.blwebform" }), Is.False);
            Assert.That(method.Invoke(null, new object[] { "Program.bas" }), Is.True, "sanity");
        });
    }

    // ==================================================================
    // ⛔ The glob trap — the most dangerous thing in this task
    // ==================================================================

    [Test]
    public void AddingTheFirstExplicitCompileItem_TurnsTheDefaultGlobCompletelyOff()
    {
        // ⛔⛔ This is not a hypothetical. GetSourceFiles globs **/*.bas ONLY while SourceFiles is
        // empty; the first <Compile> item switches it to the explicit list. So adding a form
        // document to a project that had been relying on the glob SILENTLY DROPS EVERY .bas FROM
        // THE BUILD. Whoever implements "create a form" (Task 13) must materialise the globbed
        // files as explicit items in the same edit, or write the form as something other than the
        // project's first <Compile>.
        File.WriteAllText(Path.Combine(_dir, "Program.bas"), "Module Program\n Sub Main()\n End Sub\nEnd Module\n");
        File.WriteAllText(Path.Combine(_dir, "Helper.bas"), "Module Helper\nEnd Module\n");
        var projPath = Path.Combine(_dir, "App.blproj");

        File.WriteAllText(projPath, """
            <BasicLangProject Version="1.0">
              <PropertyGroup><ProjectName>App</ProjectName></PropertyGroup>
            </BasicLangProject>
            """);
        var globbed = ProjectFile.Load(projPath)!.GetSourceFiles().Select(Path.GetFileName).ToList();

        File.WriteAllText(projPath, """
            <BasicLangProject Version="1.0">
              <PropertyGroup><ProjectName>App</ProjectName></PropertyGroup>
              <ItemGroup><Compile Include="MainForm.blwebform" /></ItemGroup>
            </BasicLangProject>
            """);
        var explicitly = ProjectFile.Load(projPath)!.GetSourceFiles().Select(Path.GetFileName).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(globbed, Does.Contain("Program.bas").And.Contain("Helper.bas"),
                "sanity: with no <Compile> items the glob finds both sources");
            Assert.That(explicitly, Does.Not.Contain("Program.bas"),
                "⛔ adding ONE explicit <Compile> item removed every globbed source from the build. " +
                "This is pinned, not endorsed — see Task 13.");
        });
    }

    [Test]
    public void ARecursiveGlobInAnExplicitCompileItem_IsSilentlySkipped()
    {
        // The explicit branch is Directory.GetFiles(dir, pattern) with no SearchOption, i.e.
        // top-directory-only, and Path.GetDirectoryName("**\*.blwebform") is a directory that does
        // not exist — so the item matches NOTHING and says nothing about it.
        Directory.CreateDirectory(Path.Combine(_dir, "Forms"));
        File.WriteAllText(Path.Combine(_dir, "Forms", "Login.blwebform"), "<WebForm Name=\"Login\" Version=\"1\"/>");
        var projPath = Path.Combine(_dir, "App.blproj");

        File.WriteAllText(projPath, """
            <BasicLangProject Version="1.0">
              <PropertyGroup><ProjectName>App</ProjectName></PropertyGroup>
              <ItemGroup><Compile Include="**\*.blwebform" /></ItemGroup>
            </BasicLangProject>
            """);

        Assert.That(ProjectFile.Load(projPath)!.GetSourceFiles(), Is.Empty,
            "a recursive glob in an explicit <Compile> item matches nothing and reports nothing — " +
            "the template must list form documents individually");
    }

    // ==================================================================
    // Editor surfaces
    // ==================================================================

    [Test]
    public void AFormDocument_IsColouredAsXml_NotAsBasicLang()
    {
        // It rides as a <Compile> item, which makes it tempting to register beside the BasicLang
        // extensions — but in Code view it IS an XML document.
        Assert.Multiple(() =>
        {
            Assert.That(LanguageFileTypes.GetEditorLanguageId("MainForm.blform"), Is.EqualTo("xml"));
            Assert.That(LanguageFileTypes.GetEditorLanguageId("Login.blwebform"), Is.EqualTo("xml"));
        });
    }

    [Test]
    public void AFormDocument_IsNotRoutedToALanguageServer()
    {
        // Pointing the BasicLang language server at XML would have it report a syntax error on
        // every line of a perfectly valid form.
        Assert.Multiple(() =>
        {
            Assert.That(LanguageFileTypes.GetLspLanguageId("MainForm.blform"), Is.Null);
            Assert.That(LanguageFileTypes.GetLspLanguageId("Login.blwebform"), Is.Null);
            Assert.That(LanguageFileTypes.LspRoutedExtensions,
                Does.Not.Contain(".blform").And.Not.Contain(".blwebform"));
        });
    }

    // ==================================================================
    // The CLI, end to end
    // ==================================================================

    [Test]
    [Category("Integration")]
    public async Task Cli_CompilingAFormDocumentDirectly_FailsWithBL8001()
    {
        // Before this, CompileFile gated on nothing but .bli, so this sent XML straight to the
        // BasicLang lexer.
        var path = Path.Combine(_dir, "LoginForm.blwebform");
        File.WriteAllText(path, """<WebForm Name="LoginForm" Version="1"><Controls/></WebForm>""");

        var (exitCode, stdout, stderr) = await CliTestHarness.RunCli(_dir, path, "--target=csharp");

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.Not.Zero, $"stdout:\n{stdout}\nstderr:\n{stderr}");
            Assert.That(stdout + stderr, Does.Contain("BL8001"));
            Assert.That(stdout + stderr, Does.Contain("Build the project"),
                "the diagnostic must say what to do instead");
        });
    }

    [Test]
    [Category("Integration")]
    public async Task Cli_ProjectBuild_SkipsTheFormDocument_AndSucceeds()
    {
        // The other route. A form document listed as <Compile> must reach the build (so the markup
        // emitter can find it) and never reach the lexer.
        File.WriteAllText(Path.Combine(_dir, "Program.bas"), """
            Module Program
                Sub Main()
                    Console.WriteLine("hello")
                End Sub
            End Module
            """);
        File.WriteAllText(Path.Combine(_dir, "LoginForm.blwebform"), """
            <WebForm Name="LoginForm" Version="1">
              <Controls><Button Id="btnLogin" TabIndex="0" Text="Sign in"/></Controls>
            </WebForm>
            """);
        var projPath = Path.Combine(_dir, "App.blproj");
        File.WriteAllText(projPath, """
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>App</ProjectName>
                <OutputType>Exe</OutputType>
                <TargetBackend>CSharp</TargetBackend>
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="Program.bas" />
                <Compile Include="LoginForm.blwebform" />
              </ItemGroup>
            </BasicLangProject>
            """);

        var (exitCode, stdout, stderr) = await CliTestHarness.RunCli(_dir, "build", projPath);
        var output = stdout + stderr;

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.Zero, $"stdout:\n{stdout}\nstderr:\n{stderr}");
            Assert.That(output, Does.Not.Contain("BL8001"),
                "a form document in a PROJECT build is correct, not an error");
            // The tell-tale of XML reaching the BasicLang lexer: it would report unexpected
            // characters, and "the pollution is invisible in diagnostics" is how this repo has been
            // bitten before — so assert on the form's own text, not just on the exit code.
            Assert.That(output, Does.Not.Contain("WebForm").And.Not.Contain("btnLogin"),
                "the form document's XML must never appear in a compiler diagnostic");
        });
    }
}
