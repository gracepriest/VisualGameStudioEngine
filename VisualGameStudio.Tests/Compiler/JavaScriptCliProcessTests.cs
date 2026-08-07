using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Plan task 29 steps 1 and 2 — BOTH shipping entry points, end to end, with the emitted
/// JavaScript actually RUN under Node.
///
/// <para><b>What this adds over the existing fixtures.</b> JavaScriptCliSiteTests calls
/// <c>Program.Main</c> IN-PROCESS with Console redirected, and JavaScriptProjectBuildTests
/// drives <c>BuildService</c> in-process. Between them they prove argv parsing, dispatch and
/// file writing — and neither crosses the argv→process boundary, and neither ever RUNS the
/// result. So "the compiler emits a .js" was covered and "the .js it emits actually works"
/// was not.</para>
///
/// <para>Running matters most here because both of these routes feed the backend OPTIMIZED
/// IR — <c>OptimizationPipeline.AddStandardPasses()</c> runs unconditionally on both, and
/// never in the non-optimizing test helper the other 300-odd tests use.</para>
/// </summary>
[TestFixture]
[Category("Integration")]   // spawns BasicLang.exe and node
[NonParallelizable]
public class JavaScriptCliProcessTests
{
    private string _dir = "";

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "BasicLang_JsProc_" + Path.GetRandomFileName());
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch { /* a locked temp dir must not fail a passing test */ }
    }

    private const string Source =
        "Sub Main()\n" +
        "Dim total As Integer = 0\n" +
        "For i As Integer = 1 To 4\n" +
        "total = total + i\n" +
        "Next\n" +
        "Console.WriteLine(total)\n" +
        "Console.WriteLine(\"done\")\n" +
        "End Sub\n";

    /// <summary>
    /// Runs an already-emitted .js under Node. No such helper existed — every JS execution
    /// fixture routes through RunJs, which compiles BasicLang source in-process and cannot
    /// run a file the CLI produced.
    /// </summary>
    private static string RunNodeFile(string jsPath)
    {
        var node = BasicLang.Runtime.NodeLocator.Find();
        if (node == null)
            Assert.Ignore("Node.js not found — the JS execution tier cannot run on this machine.");

        var (exit, stdout, stderr) = CliTestHarness.RunProcess(
            node!, new[] { jsPath }, Path.GetDirectoryName(jsPath)!, timeoutMs: 60_000);

        Assert.That(exit, Is.Zero, $"node exited {exit}.\n--- stderr ---\n{stderr}");
        return stdout.Replace("\r\n", "\n").Trim();
    }

    // ---------------------------------------------------------------- single file

    /// <summary>
    /// argv → file → Node. The leg the in-process fixture structurally cannot cover.
    /// </summary>
    [Test]
    public async Task SingleFile_ThroughTheRealExe_EmitsJavaScriptThatRuns()
    {
        var bas = Path.Combine(_dir, "prog.bas");
        await File.WriteAllTextAsync(bas, Source);

        var (exit, stdout, stderr) = await CliTestHarness.RunCli(_dir, bas, "--target=javascript");
        Assert.That(exit, Is.Zero, $"CLI failed.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");

        var js = Path.Combine(_dir, "prog.js");
        Assert.That(File.Exists(js), Is.True, $"prog.js was not written.\nSTDOUT:\n{stdout}");
        Assert.That(File.Exists(Path.Combine(_dir, "index.html")), Is.True, "harness");
        Assert.That(File.Exists(js + ".map"), Is.True, "source map");

        Assert.That(RunNodeFile(js), Is.EqualTo("10\ndone"));
    }

    /// <summary>
    /// The map's `//# sourceMappingURL=` comment is appended by the emitter AFTER the plain
    /// write, so the file Node executes is the one carrying the comment. Node must tolerate
    /// it — a trailing comment that broke parsing would break every built site.
    /// </summary>
    [Test]
    public async Task SingleFile_SourceMapCommentDoesNotBreakExecution()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "prog.bas"), Source);
        await CliTestHarness.RunCli(_dir, Path.Combine(_dir, "prog.bas"), "--target=javascript");

        var js = Path.Combine(_dir, "prog.js");
        Assert.That((await File.ReadAllTextAsync(js)).TrimEnd(),
            Does.EndWith("//# sourceMappingURL=prog.js.map"));
        Assert.That(RunNodeFile(js), Is.EqualTo("10\ndone"));
    }

    /// <summary>
    /// A BL70xx capability rejection must fail the real process with a non-zero exit and a
    /// readable message — not a stack trace, and not a zero exit with no output.
    /// </summary>
    [Test]
    public async Task SingleFile_CapabilityRejection_FailsTheProcessCleanly()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "prog.bas"),
            "Sub Bump(ByRef n As Integer)\nn = n + 1\nEnd Sub\n" +
            "Sub Main()\nDim x As Integer = 1\nBump(x)\nEnd Sub\n");

        var (exit, stdout, stderr) = await CliTestHarness.RunCli(
            _dir, Path.Combine(_dir, "prog.bas"), "--target=javascript");

        Assert.That(exit, Is.Not.Zero, $"a refused construct must fail the build.\nSTDOUT:\n{stdout}");
        Assert.That(stdout + stderr, Does.Contain("BL7002"));
        Assert.That(stdout + stderr, Does.Not.Contain("Unhandled exception"));
    }

    // ---------------------------------------------------------------- project

    private async Task<string> WriteProject()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "Main.bas"), Source);

        var proj = Path.Combine(_dir, "Site.blproj");
        await File.WriteAllTextAsync(proj,
            "<Project>\n" +
            "  <PropertyGroup>\n" +
            "    <ProjectName>Site</ProjectName>\n" +
            "    <AssemblyName>Site</AssemblyName>\n" +
            "    <TargetBackend>JavaScript</TargetBackend>\n" +
            "  </PropertyGroup>\n" +
            "  <ItemGroup>\n" +
            "    <Compile Include=\"Main.bas\" />\n" +
            "  </ItemGroup>\n" +
            "</Project>\n");
        return proj;
    }

    /// <summary>
    /// `BasicLang.exe build Site.blproj` through the real binary, and the emitted site RUNS.
    /// This is the route that used to write a .cs file and report success.
    /// </summary>
    [Test]
    public async Task ProjectRoute_ThroughTheRealExe_EmitsASiteThatRuns()
    {
        var (exit, stdout, stderr) = await CliTestHarness.RunCli(_dir, "build", await WriteProject());
        Assert.That(exit, Is.Zero, $"CLI build failed.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");

        var scripts = Directory.GetFiles(Path.Combine(_dir, "bin"), "*.js", SearchOption.AllDirectories);
        Assert.That(scripts, Is.Not.Empty, $"no .js emitted.\nSTDOUT:\n{stdout}");
        Assert.That(Directory.GetFiles(Path.Combine(_dir, "bin"), "*.cs", SearchOption.AllDirectories),
            Is.Empty, "a JavaScript project must never emit C#");

        var siteDir = Path.GetDirectoryName(scripts[0])!;
        Assert.That(File.Exists(Path.Combine(siteDir, "index.html")), Is.True, "harness");

        Assert.That(RunNodeFile(scripts[0]), Is.EqualTo("10\ndone"));
    }

    /// <summary>
    /// ⭐ THE OPTIMIZER LEG. The CLI runs OptimizationPipeline.AddStandardPasses()
    /// unconditionally — there is no way to switch it off, `--optimize` only upgrades it to
    /// aggressive — so this is the only test in the suite where the SHIPPED binary's
    /// optimized output is executed. A constant-folded expression here is the exact shape
    /// that made Visit(IRConstant) throw.
    /// </summary>
    [Test]
    public async Task ProjectRoute_OptimizedOutput_StillRuns()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "Main.bas"),
            "Sub Main()\n" +
            "Dim folded As Integer = 2 + 3 * 4\n" +   // constant-folded away
            "Console.WriteLine(folded)\n" +
            "Console.WriteLine(6 - 1)\n" +            // folded in argument position
            "End Sub\n");
        await WriteProjectOnly();

        var (exit, stdout, stderr) = await CliTestHarness.RunCli(
            _dir, "build", Path.Combine(_dir, "Site.blproj"));
        Assert.That(exit, Is.Zero, $"CLI build failed.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");

        var scripts = Directory.GetFiles(Path.Combine(_dir, "bin"), "*.js", SearchOption.AllDirectories);
        Assert.That(RunNodeFile(scripts[0]), Is.EqualTo("14\n5"));
    }

    // ---------------------------------------------------------------- IDE entry point

    /// <summary>
    /// Plan task 29 step 2 — the IDE's build path, with the result RUN.
    ///
    /// <para>The IDE does not share the CLI's backend dispatch: BuildService hand-rolls its
    /// own switch. So "the CLI emits working JavaScript" is not evidence about this route,
    /// which is exactly what CLAUDE.md means by testing both entry points. Like the CLI, it
    /// feeds the backend OPTIMIZED IR via CompileProjectFiles.</para>
    /// </summary>
    [Test]
    public async Task IdeBuildPath_EmitsJavaScriptThatRuns()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "Main.bas"), Source);
        await WriteProjectOnly();

        var project = await new VisualGameStudio.ProjectSystem.Serialization.ProjectSerializer()
            .LoadAsync(Path.Combine(_dir, "Site.blproj"));
        var result = await new VisualGameStudio.ProjectSystem.Services.BuildService(
            new SilentOutput()).BuildProjectAsync(project);

        Assert.That(result.Success, Is.True, "the IDE build failed");
        Assert.That(result.OutputPath, Is.Not.Null.And.Not.Empty);

        var js = Path.Combine(result.OutputPath!, result.GeneratedFileName);
        Assert.That(File.Exists(js), Is.True, $"no script at {js}");

        Assert.That(RunNodeFile(js), Is.EqualTo("10\ndone"));
    }

    /// <summary>
    /// The same constant-folding shape as the CLI case, through the IDE. This is the arm that
    /// made Visit(IRConstant) throw, and the IDE reaches it by a different code path.
    /// </summary>
    [Test]
    public async Task IdeBuildPath_OptimizedOutput_StillRuns()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "Main.bas"),
            "Sub Main()\nDim folded As Integer = 2 + 3 * 4\nConsole.WriteLine(folded)\nEnd Sub\n");
        await WriteProjectOnly();

        var project = await new VisualGameStudio.ProjectSystem.Serialization.ProjectSerializer()
            .LoadAsync(Path.Combine(_dir, "Site.blproj"));
        var result = await new VisualGameStudio.ProjectSystem.Services.BuildService(
            new SilentOutput()).BuildProjectAsync(project);

        Assert.That(result.Success, Is.True, "the IDE build failed");
        Assert.That(RunNodeFile(Path.Combine(result.OutputPath!, result.GeneratedFileName)),
            Is.EqualTo("14"));
    }

    // ---------------------------------------------------------------- #JsImport reaches output
    //
    // Plan 2 task 5. Without the copy the feature is FALSE for every shipping route while every
    // unit test passes: the project routes emit into bin/<config>/<tfm>/ and the user's
    // ./helper.js stays in the project directory, so the emitted `import "./helper.js"` 404s in
    // the browser from a build that reported success. These live here, not in the interop
    // fixture, because the question is an ENTRY-POINT one — what lands in each route's output
    // directory — and this fixture already owns both routes plus the Node harness.

    /// <summary>
    /// Writes a project whose Main.bas imports a sibling helper module.
    ///
    /// <para>The ORDINARY module shape — <c>export function greet()</c> reached by a named
    /// import. It could not be written this way at first: <c>#JsImport</c> only had the
    /// side-effect form, which binds no names, so an exporting module was imported, evaluated,
    /// and then unreachable (<c>greet is not defined</c>, from a build that reported success).
    /// The binding forms exist for exactly this.</para>
    /// </summary>
    private async Task WriteImportingProject(string specifier = "./helper.js")
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "helper.js"),
            "export function greet() { return \"hi from the module\"; }\n");
        await File.WriteAllTextAsync(Path.Combine(_dir, "Main.bas"),
            $"#JsImport {{ greet }} From \"{specifier}\"\n" +
            "Sub Main()\n" +
            "javascript{ console.log(greet()); }\n" +
            "End Sub\n");
        await WriteProjectOnly();
    }

    /// <summary>
    /// THE headline case. Note the assertion is on the module sitting BESIDE the script, not on
    /// it existing somewhere — a test that hand-places the file in the output directory would
    /// pass with the copy removed entirely.
    /// </summary>
    [Test]
    public async Task ProjectRoute_RelativeJsImport_IsCopiedBesideTheScript()
    {
        await WriteImportingProject();

        var (exit, stdout, stderr) = await CliTestHarness.RunCli(
            _dir, "build", Path.Combine(_dir, "Site.blproj"));
        Assert.That(exit, Is.Zero, $"CLI build failed.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");

        var script = Directory.GetFiles(Path.Combine(_dir, "bin"), "Site.js", SearchOption.AllDirectories)
            .Single();
        var siteDir = Path.GetDirectoryName(script)!;

        Assert.That(File.Exists(Path.Combine(siteDir, "helper.js")), Is.True,
            "an imported relative module must be copied beside the emitted script, or the " +
            "browser 404s on it");
        Assert.That(RunNodeFile(script), Is.EqualTo("hi from the module"));
    }

    /// <summary>
    /// The IDE route reaches JavaScriptEmitter through BuildService, which is a SEPARATE call
    /// site from the CLI's two. Putting the copy inside Emit is what makes one implementation
    /// cover all three; this is the test that would have caught patching only the CLI.
    /// </summary>
    [Test]
    public async Task IdeBuildPath_RelativeJsImport_IsCopiedBesideTheScript()
    {
        await WriteImportingProject();

        var project = await new VisualGameStudio.ProjectSystem.Serialization.ProjectSerializer()
            .LoadAsync(Path.Combine(_dir, "Site.blproj"));
        var result = await new VisualGameStudio.ProjectSystem.Services.BuildService(
            new SilentOutput()).BuildProjectAsync(project);

        Assert.That(result.Success, Is.True, "the IDE build failed");
        Assert.That(File.Exists(Path.Combine(result.OutputPath!, "helper.js")), Is.True,
            "the IDE route did not copy the imported module");
        Assert.That(RunNodeFile(Path.Combine(result.OutputPath!, result.GeneratedFileName)),
            Is.EqualTo("hi from the module"));
    }

    /// <summary>
    /// ⛔ THE SELF-COPY. The single-file route writes its output NEXT TO THE SOURCE, so source
    /// and destination are the same file and <c>File.Copy</c> throws IOException — a crash on
    /// the most ordinary program this feature has.
    /// </summary>
    [Test]
    public async Task SingleFile_RelativeJsImport_DoesNotFailCopyingTheModuleOntoItself()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "helper.js"),
            "globalThis.greet = function () { return \"hi from the module\"; };\n");
        await File.WriteAllTextAsync(Path.Combine(_dir, "prog.bas"),
            "#JsImport \"./helper.js\"\nSub Main()\njavascript{ console.log(greet()); }\nEnd Sub\n");

        var (exit, stdout, stderr) = await CliTestHarness.RunCli(
            _dir, Path.Combine(_dir, "prog.bas"), "--target=javascript");

        Assert.That(exit, Is.Zero, $"CLI failed.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        Assert.That(await File.ReadAllTextAsync(Path.Combine(_dir, "helper.js")),
            Does.Contain("hi from the module"), "the source module must be left intact");
        Assert.That(RunNodeFile(Path.Combine(_dir, "prog.js")), Is.EqualTo("hi from the module"));
    }

    /// <summary>
    /// Bare specifiers are package-manager territory — a stated non-goal — so they must be left
    /// completely alone: emitted as written, nothing copied, no error. (Not RUN: resolving
    /// "lodash" would need a node_modules, which is exactly the thing being declined.)
    /// </summary>
    [Test]
    public async Task ProjectRoute_BareSpecifier_IsNotCopiedAndIsNotAnError()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "Main.bas"),
            "#JsImport \"lodash\"\nSub Main()\nConsole.WriteLine(1)\nEnd Sub\n");
        await WriteProjectOnly();

        var (exit, stdout, stderr) = await CliTestHarness.RunCli(
            _dir, "build", Path.Combine(_dir, "Site.blproj"));

        Assert.That(exit, Is.Zero, $"a bare specifier must not fail the build.\nSTDERR:\n{stderr}");

        var script = Directory.GetFiles(Path.Combine(_dir, "bin"), "Site.js", SearchOption.AllDirectories)
            .Single();
        Assert.That(await File.ReadAllTextAsync(script), Does.Contain("import \"lodash\";"));
        Assert.That(Directory.GetFiles(Path.GetDirectoryName(script)!, "lodash*"), Is.Empty);
    }

    /// <summary>
    /// A module the compiler never reads must not fail the build — the user may be serving it
    /// from elsewhere, or about to add it. But it must SAY so: silence here is how a 404 in the
    /// browser becomes a mystery.
    /// </summary>
    [Test]
    public async Task ProjectRoute_MissingRelativeTarget_WarnsRatherThanFails()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "Main.bas"),
            "#JsImport \"./absent.js\"\nSub Main()\nConsole.WriteLine(1)\nEnd Sub\n");
        await WriteProjectOnly();

        var (exit, stdout, stderr) = await CliTestHarness.RunCli(
            _dir, "build", Path.Combine(_dir, "Site.blproj"));

        Assert.That(exit, Is.Zero, $"a missing module must not fail the build.\nSTDERR:\n{stderr}");
        Assert.That(stdout + stderr, Does.Contain("absent.js"), "the warning must name the file");
    }

    /// <summary>
    /// ⛔ CONTAINMENT. `../escape.js` is a legal ES specifier that resolves ABOVE the output
    /// directory. Copying it would write outside the build output — and one more `..` would
    /// reach the project directory and overwrite a source file. Refused, warned, build still
    /// succeeds.
    /// </summary>
    [Test]
    public async Task ProjectRoute_ParentRelativeImport_IsNotCopiedOutsideTheOutputDirectory()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "escape.js"), "export const x = 1;\n");
        await File.WriteAllTextAsync(Path.Combine(_dir, "Main.bas"),
            "#JsImport \"../escape.js\"\nSub Main()\nConsole.WriteLine(1)\nEnd Sub\n");
        await WriteProjectOnly();

        var (exit, stdout, stderr) = await CliTestHarness.RunCli(
            _dir, "build", Path.Combine(_dir, "Site.blproj"));

        Assert.That(exit, Is.Zero, $"STDERR:\n{stderr}");
        Assert.That(stdout + stderr, Does.Contain("escape.js"), "the refusal must name the file");

        var script = Directory.GetFiles(Path.Combine(_dir, "bin"), "Site.js", SearchOption.AllDirectories)
            .Single();
        var siteParent = Path.GetDirectoryName(Path.GetDirectoryName(script)!)!;
        Assert.That(File.Exists(Path.Combine(siteParent, "escape.js")), Is.False,
            "nothing may be written above the output directory");
    }

    /// <summary>
    /// A site with imports must carry a package.json declaring module scope, or `node Site.js`
    /// dies with "Cannot use import statement outside a module" on any Node before 22.7 — so
    /// whether the emitted site runs would depend on the reader's Node version. A site WITHOUT
    /// imports must not get one: the single-file route emits next to the user's source.
    /// </summary>
    [Test]
    public async Task ProjectRoute_PackageJsonIsWrittenOnlyWhenThereAreImports()
    {
        await WriteImportingProject();
        var (exit, _, stderr) = await CliTestHarness.RunCli(
            _dir, "build", Path.Combine(_dir, "Site.blproj"));
        Assert.That(exit, Is.Zero, stderr);

        var withImports = Path.GetDirectoryName(
            Directory.GetFiles(Path.Combine(_dir, "bin"), "Site.js", SearchOption.AllDirectories).Single())!;
        Assert.That(await File.ReadAllTextAsync(Path.Combine(withImports, "package.json")),
            Does.Contain("\"module\""));

        Directory.Delete(Path.Combine(_dir, "bin"), recursive: true);
        await File.WriteAllTextAsync(Path.Combine(_dir, "Main.bas"),
            "Sub Main()\nConsole.WriteLine(1)\nEnd Sub\n");

        (exit, _, stderr) = await CliTestHarness.RunCli(_dir, "build", Path.Combine(_dir, "Site.blproj"));
        Assert.That(exit, Is.Zero, stderr);

        var noImports = Path.GetDirectoryName(
            Directory.GetFiles(Path.Combine(_dir, "bin"), "Site.js", SearchOption.AllDirectories).Single())!;
        Assert.That(File.Exists(Path.Combine(noImports, "package.json")), Is.False,
            "a program with no imports must leave the output directory alone");
    }

    /// <summary>
    /// The BARE form still binds nothing — and that is CORRECT ES, not a shortfall. A
    /// side-effect import runs a module for what it does, not for what it exports.
    ///
    /// <para>Kept as an executable statement of the semantics because it was once a real gap:
    /// with only this form, the ordinary <c>export function greet()</c> module was unusable and
    /// the failure surfaced in a browser rather than in the build. It now sits beside the
    /// binding-form tests so the difference reads as a deliberate distinction.</para>
    /// </summary>
    [Test]
    public async Task ProjectRoute_BareJsImport_RunsTheModuleButBindsNoNames()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "helper.js"),
            "console.log(\"side effect\");\nexport function greet() { return \"exported\"; }\n");
        await File.WriteAllTextAsync(Path.Combine(_dir, "Main.bas"),
            "#JsImport \"./helper.js\"\nSub Main()\nConsole.WriteLine(\"main\")\nEnd Sub\n");
        await WriteProjectOnly();

        var (exit, _, stderr) = await CliTestHarness.RunCli(
            _dir, "build", Path.Combine(_dir, "Site.blproj"));
        Assert.That(exit, Is.Zero, stderr);

        var script = Directory.GetFiles(Path.Combine(_dir, "bin"), "Site.js", SearchOption.AllDirectories)
            .Single();
        Assert.That(await File.ReadAllTextAsync(script), Does.Contain("import \"./helper.js\";"),
            "no binding clause — the module runs, nothing is named");

        // The side effect happens; the export is simply never referenced.
        Assert.That(RunNodeFile(script), Is.EqualTo("side effect\nmain"));
    }

    /// <summary>
    /// ⭐ THE ONE THAT CLOSES THE GAP: an ordinary exporting module, reached by a named import,
    /// through the real binary, RUN. Text assertions cannot tell a correct import statement
    /// from one that parses and links to nothing — ES named imports fail at LINK time, so a
    /// wrong name renders a blank page rather than throwing where you can see it.
    /// </summary>
    [TestCase("{ greet }", "greet()", TestName = "ProjectRoute_NamedImport_Runs")]
    [TestCase("{ greet As hi }", "hi()", TestName = "ProjectRoute_AliasedImport_Runs")]
    [TestCase("* As lib", "lib.greet()", TestName = "ProjectRoute_NamespaceImport_Runs")]
    public async Task ProjectRoute_BindingForm_ReachesTheExport(string clause, string call)
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "helper.js"),
            "export function greet() { return \"reached the export\"; }\n");
        await File.WriteAllTextAsync(Path.Combine(_dir, "Main.bas"),
            $"#JsImport {clause} From \"./helper.js\"\n" +
            $"Sub Main()\njavascript{{ console.log({call}); }}\nEnd Sub\n");
        await WriteProjectOnly();

        var (exit, stdout, stderr) = await CliTestHarness.RunCli(
            _dir, "build", Path.Combine(_dir, "Site.blproj"));
        Assert.That(exit, Is.Zero, $"CLI build failed.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");

        var script = Directory.GetFiles(Path.Combine(_dir, "bin"), "Site.js", SearchOption.AllDirectories)
            .Single();
        Assert.That(RunNodeFile(script), Is.EqualTo("reached the export"));
    }

    /// <summary>A default export, the shape most npm packages present.</summary>
    [Test]
    public async Task ProjectRoute_DefaultImport_ReachesTheExport()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "helper.js"),
            "export default function () { return \"the default\"; }\n");
        await File.WriteAllTextAsync(Path.Combine(_dir, "Main.bas"),
            "#JsImport helper From \"./helper.js\"\n" +
            "Sub Main()\njavascript{ console.log(helper()); }\nEnd Sub\n");
        await WriteProjectOnly();

        var (exit, stdout, stderr) = await CliTestHarness.RunCli(
            _dir, "build", Path.Combine(_dir, "Site.blproj"));
        Assert.That(exit, Is.Zero, $"CLI build failed.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");

        var script = Directory.GetFiles(Path.Combine(_dir, "bin"), "Site.js", SearchOption.AllDirectories)
            .Single();
        Assert.That(RunNodeFile(script), Is.EqualTo("the default"));
    }

    /// <summary>
    /// ⛔ An imported name is reachable through <c>::</c> too, not only from inside a
    /// <c>javascript{ }</c> block — which matters because <c>::</c> is the ergonomic form a user
    /// reaches for first. Nothing in the call path was changed for this, so it is a claim that
    /// needs measuring rather than assuming.
    /// </summary>
    [Test]
    public async Task ProjectRoute_ImportedName_IsCallableThroughForeignSyntax()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "helper.js"),
            "export function shout(s) { console.log(s.toUpperCase()); }\n");
        await File.WriteAllTextAsync(Path.Combine(_dir, "Main.bas"),
            "#JsImport { shout } From \"./helper.js\"\n" +
            "Sub Main()\n::shout(\"through colons\")\nEnd Sub\n");
        await WriteProjectOnly();

        var (exit, stdout, stderr) = await CliTestHarness.RunCli(
            _dir, "build", Path.Combine(_dir, "Site.blproj"));
        Assert.That(exit, Is.Zero, $"CLI build failed.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");

        var script = Directory.GetFiles(Path.Combine(_dir, "bin"), "Site.js", SearchOption.AllDirectories)
            .Single();
        Assert.That(RunNodeFile(script), Is.EqualTo("THROUGH COLONS"));
    }

    /// <summary>
    /// The BL7010 collision, through the real binary: it must fail the BUILD, cleanly, rather
    /// than emit a module that fails to parse in the browser and renders nothing.
    /// </summary>
    [Test]
    public async Task ProjectRoute_ImportCollidingWithAFunction_FailsTheBuildCleanly()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "helper.js"),
            "export function greet() { return \"x\"; }\n");
        await File.WriteAllTextAsync(Path.Combine(_dir, "Main.bas"),
            "#JsImport { greet } From \"./helper.js\"\n" +
            "Sub greet()\nEnd Sub\nSub Main()\nEnd Sub\n");
        await WriteProjectOnly();

        var (exit, stdout, stderr) = await CliTestHarness.RunCli(
            _dir, "build", Path.Combine(_dir, "Site.blproj"));

        Assert.That(exit, Is.Not.Zero, $"a colliding import must fail the build.\nSTDOUT:\n{stdout}");
        Assert.That(stdout + stderr, Does.Contain("BL7010"));
        Assert.That(stdout + stderr, Does.Not.Contain("Unhandled exception"));
    }

    /// <summary>Swallows build chatter — this fixture asserts on behaviour, not on logs.</summary>
    private sealed class SilentOutput : VisualGameStudio.Core.Abstractions.Services.IOutputService
    {
        public void WriteLine(string message, VisualGameStudio.Core.Abstractions.Services.OutputCategory category = VisualGameStudio.Core.Abstractions.Services.OutputCategory.General) { }
        public void Write(string message, VisualGameStudio.Core.Abstractions.Services.OutputCategory category = VisualGameStudio.Core.Abstractions.Services.OutputCategory.General) { }
        public void WriteError(string message, VisualGameStudio.Core.Abstractions.Services.OutputCategory category = VisualGameStudio.Core.Abstractions.Services.OutputCategory.General) { }
        public void Clear(VisualGameStudio.Core.Abstractions.Services.OutputCategory category) { }
        public void ClearAll() { }
        public void Activate(VisualGameStudio.Core.Abstractions.Services.OutputCategory category) { }
        public IReadOnlyList<string> GetMessages(VisualGameStudio.Core.Abstractions.Services.OutputCategory category) => Array.Empty<string>();
        public event EventHandler<VisualGameStudio.Core.Abstractions.Services.OutputEventArgs>? OutputReceived { add { } remove { } }
        public VisualGameStudio.Core.Abstractions.Services.IOutputChannel CreateChannel(string name) => throw new NotSupportedException();
        public VisualGameStudio.Core.Abstractions.Services.IOutputChannel? GetChannel(string name) => null;
        public IReadOnlyList<VisualGameStudio.Core.Abstractions.Services.IOutputChannel> Channels => Array.Empty<VisualGameStudio.Core.Abstractions.Services.IOutputChannel>();
        public VisualGameStudio.Core.Abstractions.Services.IOutputChannel? ActiveChannel { get; set; }
        public event EventHandler<string>? ChannelCreated { add { } remove { } }
        public event EventHandler<VisualGameStudio.Core.Abstractions.Services.IOutputChannel?>? ActiveChannelChanged { add { } remove { } }
        public void ShowOutput() { }
    }

    /// <summary>Writes only the .blproj, leaving Main.bas as the caller wrote it.</summary>
    private async Task WriteProjectOnly()
        => await File.WriteAllTextAsync(Path.Combine(_dir, "Site.blproj"),
            "<Project>\n" +
            "  <PropertyGroup>\n" +
            "    <ProjectName>Site</ProjectName>\n" +
            "    <AssemblyName>Site</AssemblyName>\n" +
            "    <TargetBackend>JavaScript</TargetBackend>\n" +
            "  </PropertyGroup>\n" +
            "  <ItemGroup>\n" +
            "    <Compile Include=\"Main.bas\" />\n" +
            "  </ItemGroup>\n" +
            "</Project>\n");
}
