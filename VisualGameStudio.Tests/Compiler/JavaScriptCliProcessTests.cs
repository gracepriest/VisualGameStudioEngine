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
