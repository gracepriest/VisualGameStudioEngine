using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.CodeGen.JavaScript;
using BasicLang.Compiler.IR.Optimization;
using NUnit.Framework;
using VisualGameStudio.Core.Models;
using VisualGameStudio.ProjectSystem.Serialization;
using VisualGameStudio.ProjectSystem.Services;
using RecordingOutput = VisualGameStudio.Tests.Services.JavaScriptProjectBuildTests.RecordingOutput;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Spec §4.1 — each build ROUTE defines its target and configuration symbols. CLAUDE.md: a fix verified one way can
/// break the other; the IDE build and the CLI are separate routes into the same engine.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class BuildSymbolRouteTests
{
    private const string Program =
        "Sub Main()\n" +
        "#If WEB AndAlso RELEASE Then\n    Console.WriteLine(\"web release\")\n" +
        "#ElseIf WEB Then\n    Console.WriteLine(\"web debug\")\n" +
        "#ElseIf DESKTOP AndAlso DEBUG Then\n    Console.WriteLine(\"desktop debug\")\n" +
        "#Else\n    Console.WriteLine(\"desktop other\")\n" +
        "#End If\nEnd Sub\n";

    /// <summary>A project's &lt;DefineConstants&gt; reach the preprocessor on the project routes.</summary>
    private const string FeatureProgram =
        "Sub Main()\n" +
        "#If FEATURE Then\n    Console.WriteLine(\"feature on\")\n" +
        "#Else\n    Console.WriteLine(\"feature off\")\n" +
        "#End If\nEnd Sub\n";

    private string _dir = "";

    [SetUp]
    public void SetUp() => _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),
        "bl-symroute-" + Path.GetRandomFileName())).FullName;

    [TearDown]
    public void TearDown() { try { Directory.Delete(_dir, true); } catch { } }

    private string WriteMain(string source = Program)
    {
        var p = Path.Combine(_dir, "Main.bas");
        File.WriteAllText(p, source);
        return p;
    }

    /// <summary>A JavaScript project; <paramref name="releaseDefines"/> goes into a Release-conditioned group.</summary>
    private string WriteSiteProject(string? releaseDefines = null)
    {
        var path = Path.Combine(_dir, "Site.blproj");
        File.WriteAllText(path,
            "<Project>\n  <PropertyGroup>\n    <ProjectName>Site</ProjectName>\n" +
            "    <TargetBackend>JavaScript</TargetBackend>\n  </PropertyGroup>\n" +
            (releaseDefines == null ? "" :
                "  <PropertyGroup Condition=\"'$(Configuration)' == 'Release'\">\n" +
                "    <DefineConstants>" + releaseDefines + "</DefineConstants>\n  </PropertyGroup>\n") +
            "  <ItemGroup>\n    <Compile Include=\"Main.bas\" />\n  </ItemGroup>\n</Project>\n");
        return path;
    }

    private static BasicLang.Compiler.IR.IRModule Optimized(BasicLang.Compiler.IR.IRModule ir)
    {
        var pipeline = new OptimizationPipeline();
        pipeline.AddStandardPasses();
        pipeline.Run(ir);
        return ir;
    }

    private static string Errors(CompilationResult r) => string.Join(" | ", r.AllErrors.Select(e => e.Message));

    // ---------------------------------------------------------------- the compiler API

    [Test]
    public void TheCompilerApi_JavaScriptRelease_IsWebRelease()
    {
        var r = new BasicCompiler(new CompilerOptions { TargetBackend = "javascript", Configuration = "Release" })
            .CompileProjectFiles(new[] { WriteMain() });
        Assert.That(r.HasErrors, Is.False, Errors(r));
        var js = new JavaScriptCodeGenerator().Generate(Optimized(r.CombinedIR!));
        Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunNodeScript(js)), Is.EqualTo("web release"));
    }

    [Test]
    public void TheCompilerApi_CSharpDebug_IsDesktopDebug()
    {
        var r = new BasicCompiler(new CompilerOptions { TargetBackend = "csharp", Configuration = "Debug" })
            .CompileProjectFiles(new[] { WriteMain() });
        Assert.That(r.HasErrors, Is.False, Errors(r));
        var cs = new CSharpCodeGenerator().Generate(Optimized(r.CombinedIR!));
        Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpText(cs)), Is.EqualTo("desktop debug"));
    }

    [Test]
    public void TheCompilerApi_NoConfiguration_DefinesNeither()
    {
        var r = new BasicCompiler(new CompilerOptions { TargetBackend = "csharp" }).CompileProjectFiles(new[] { WriteMain() });
        Assert.That(r.HasErrors, Is.False, Errors(r));
        var cs = new CSharpCodeGenerator().Generate(Optimized(r.CombinedIR!));
        Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpText(cs)), Is.EqualTo("desktop other"));
    }

    // ---------------------------------------------------------------- the debugger and native routes

    /// <summary>The debugger route (DebugSession builds its own BasicCompiler) is a Debug, desktop build.</summary>
    [Test]
    public void TheDebuggerRoute_IsDesktopDebug()
    {
        var options = BasicLang.Debugger.DebugSession.CompilerOptionsForDebugging();
        Assert.That(BuildSymbols.For(options.TargetBackend, options.Configuration, options.DefineConstants),
            Is.EqualTo(new[] { "DESKTOP", "DEBUG" }));
    }

    /// <summary>The debugger's PROJECT branch (a .blproj beside the program) passes the project's Debug
    /// DefineConstants, as the CLI and IDE routes do. Drives the real launch compile, then runs the IR as C#.</summary>
    [Test]
    public void TheDebuggerRoute_ProjectBranch_PassesTheDebugConfigurationsDefineConstants()
    {
        var main = WriteMain(FeatureProgram);
        File.WriteAllText(Path.Combine(_dir, "App.blproj"),
            "<Project>\n  <PropertyGroup>\n    <ProjectName>App</ProjectName>\n  </PropertyGroup>\n" +
            "  <PropertyGroup Condition=\"'$(Configuration)' == 'Debug'\">\n" +
            "    <DefineConstants>DEBUG;FEATURE</DefineConstants>\n  </PropertyGroup>\n" +
            "  <ItemGroup>\n    <Compile Include=\"Main.bas\" />\n  </ItemGroup>\n</Project>\n");

        var r = BasicLang.Debugger.DebugSession.CompileForDebugging(main);
        Assert.That(r.HasErrors, Is.False, Errors(r));
        var cs = new CSharpCodeGenerator().Generate(r.CombinedIR!);
        Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpText(cs)), Is.EqualTo("feature on"));
    }

    /// <summary>The native route (CppProjectBuilder) passes its configuration and that configuration's
    /// DefineConstants. The options are read directly: the native BUILD always uses MSVC and is covered (and
    /// skipped without it — Native/NativeBuildSkip) by the CppProjectBuilder fixtures.</summary>
    [Test]
    public void TheNativeRoute_PassesItsConfiguration()
    {
        var project = new BasicLang.Compiler.ProjectSystem.ProjectFile();
        project.Configurations["Release"].DefineConstants.Add("NATIVE_EXTRA");
        var options = BasicLang.Compiler.ProjectSystem.CppProjectBuilder.CompilerOptionsFor(project, "Release");
        Assert.That(BuildSymbols.For(options.TargetBackend, options.Configuration, options.DefineConstants),
            Is.EqualTo(new[] { "DESKTOP", "RELEASE", "NATIVE_EXTRA" }));
    }

    // ---------------------------------------------------------------- the CLI

    /// <summary>The CLI single-file route defaults to Debug.</summary>
    [Test]
    public async Task TheCli_SingleFile_IsWebDebug()
    {
        WriteMain();
        var (exit, stdout, stderr) = await CliTestHarness.RunCli(_dir, "Main.bas", "--target=javascript");
        Assert.That(exit, Is.Zero, stdout + stderr);
        var js = File.ReadAllText(Path.Combine(_dir, "Main.js"));
        Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunNodeScript(js)), Is.EqualTo("web debug"));
    }

    /// <summary>The single-file Debug default, pinned where it decides the arm: "web debug" above needs only
    /// WEB, so it stays green without DEBUG (measured — that mutant survived it); "desktop debug" needs DEBUG.</summary>
    [Test]
    public async Task TheCli_SingleFile_CSharp_IsDesktopDebug()
    {
        WriteMain();
        var (exit, stdout, stderr) = await CliTestHarness.RunCli(_dir, "Main.bas", "--target=csharp");
        Assert.That(exit, Is.Zero, stdout + stderr);
        var cs = File.ReadAllText(Path.Combine(_dir, "Main.cs"));
        Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpText(cs)), Is.EqualTo("desktop debug"));
    }

    /// <summary>The CLI single-file route's --configuration and --define flags.</summary>
    [Test]
    public async Task TheCli_SingleFile_ConfigurationAndDefineFlags()
    {
        File.WriteAllText(Path.Combine(_dir, "Main.bas"),
            "Sub Main()\n#If RELEASE AndAlso FEATURE Then\n    Console.WriteLine(\"release feature\")\n" +
            "#Else\n    Console.WriteLine(\"other\")\n#End If\nEnd Sub\n");
        var (exit, stdout, stderr) = await CliTestHarness.RunCli(_dir, "Main.bas", "--target=javascript",
            "--configuration=Release", "--define=FEATURE;UNUSED");
        Assert.That(exit, Is.Zero, stdout + stderr);
        var js = File.ReadAllText(Path.Combine(_dir, "Main.js"));
        Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunNodeScript(js)), Is.EqualTo("release feature"));
    }

    /// <summary>⚠ The project's Release group names only TRACE: the CLI loader's DEFAULT Release configuration
    /// carries <c>DefineConstants = RELEASE</c>, which would define RELEASE even if the route dropped the
    /// configuration (measured — that mutant survived a project with no configuration groups).</summary>
    [Test]
    public async Task TheCli_ProjectBuild_Release_IsWebRelease()
    {
        WriteMain();
        WriteSiteProject(releaseDefines: "TRACE");
        var (exit, stdout, stderr) = await CliTestHarness.RunCli(_dir, "build", "Site.blproj", "-c", "Release");
        Assert.That(exit, Is.Zero, stdout + stderr);
        var js = Directory.GetFiles(Path.Combine(_dir, "bin", "Release"), "Site.js", SearchOption.AllDirectories).Single();
        Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunNodeScript(File.ReadAllText(js))), Is.EqualTo("web release"));
    }

    [Test]
    public async Task TheCli_ProjectBuild_PassesTheConfigurationsDefineConstants()
    {
        WriteMain(FeatureProgram);
        WriteSiteProject(releaseDefines: "RELEASE;FEATURE");
        var (exit, stdout, stderr) = await CliTestHarness.RunCli(_dir, "build", "Site.blproj", "-c", "Release");
        Assert.That(exit, Is.Zero, stdout + stderr);
        var js = Directory.GetFiles(Path.Combine(_dir, "bin", "Release"), "Site.js", SearchOption.AllDirectories).Single();
        Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunNodeScript(File.ReadAllText(js))), Is.EqualTo("feature on"));
    }

    // ---------------------------------------------------------------- the IDE

    private static async Task<(BuildResult result, RecordingOutput output)> BuildInIde(string projectPath, string configuration)
    {
        var project = await new ProjectSerializer().LoadAsync(projectPath);
        var output = new RecordingOutput();
        var service = new BuildService(output) { CurrentConfiguration = new BuildConfiguration { Name = configuration } };
        return (await service.BuildProjectAsync(project), output);
    }

    /// <summary>The IDE route: BuildService with the Release configuration selected. Release defines only TRACE
    /// in the project, so RELEASE must come from the configuration name (as on the CLI route above).</summary>
    [Test]
    public async Task TheIde_BuildService_Release_IsWebRelease()
    {
        WriteMain();
        var (result, output) = await BuildInIde(WriteSiteProject(releaseDefines: "TRACE"), "Release");
        Assert.That(result.Success, Is.True, output.Dump());
        Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunNodeScript(result.GeneratedCode!)), Is.EqualTo("web release"));
    }

    [Test]
    public async Task TheIde_BuildService_PassesTheConfigurationsDefineConstants()
    {
        WriteMain(FeatureProgram);
        var (result, output) = await BuildInIde(WriteSiteProject(releaseDefines: "RELEASE;FEATURE"), "Release");
        Assert.That(result.Success, Is.True, output.Dump());
        Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunNodeScript(result.GeneratedCode!)), Is.EqualTo("feature on"));
    }

    // ---------------------------------------------------------------- the test helper

    /// <summary>With no configuration the JS helper defines only WEB, so the chain takes the "web debug" arm.</summary>
    [Test]
    public void TheJsTestHelper_DefinesWeb()
    {
        var js = JsTestSupport.Compile(Program, runPreprocessor: true);
        Assert.Multiple(() =>
        {
            Assert.That(js, Does.Contain("web debug"));
            Assert.That(js, Does.Not.Contain("web release"));
            Assert.That(js, Does.Not.Contain("desktop"));
        });
    }
}
