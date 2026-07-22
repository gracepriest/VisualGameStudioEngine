using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using BasicLang.Compiler.ProjectSystem;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 5 (per-backend C++ toolchain overrides): <see cref="CppProjectBuilder.Build"/>
/// grows an optional <c>resolveToolchain</c> param (forwarded to
/// <see cref="CppProjectBuilder.EmitCore"/> as <c>resolveToolchain ?? CppToolchain.Find</c>),
/// the seam the IDE's BuildService (Task 6) will inject an override-aware resolver
/// through. BasicLang stays settings-agnostic: this compiler layer only ever sees a
/// resolved <see cref="CppToolchain"/> factory, never a Settings key. The CLI
/// (<c>Program.cs</c>) calls <c>Build</c> with no override args at all, so its behavior
/// must be unchanged by this additive param.
/// </summary>
[TestFixture]
public class CppProjectBuilderResolveToolchainTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-cpb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    // Retries like CppToolchainExplicitPathTests.cs's TearDown (same "wrote obj/compile_commands.json
    // under a temp dir" scenario) — a transient Windows file lock (or AV scan) on a just-written
    // file must not leak the temp dir.
    [TearDown]
    public void TearDown()
    {
        for (var i = 0; i < 3; i++)
        {
            try { Directory.Delete(_dir, recursive: true); return; }
            catch { Thread.Sleep(200); }
        }
    }

    private ProjectFile MakeMinimalCppProject()
    {
        const string blproj = """
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>App</ProjectName>
                <OutputType>Exe</OutputType>
                <TargetBackend>Cpp</TargetBackend>
              </PropertyGroup>
            </BasicLangProject>
            """;
        File.WriteAllText(Path.Combine(_dir, "App.bas"), "Sub Main()\n    PrintLine 7\nEnd Sub\n");
        var projPath = Path.Combine(_dir, "App.blproj");
        File.WriteAllText(projPath, blproj);
        return ProjectFile.Load(projPath);
    }

    /// <summary>
    /// Proves the override reaches the SAME shared emission path Build and
    /// IntelliSenseEmitter both drive (see CppToolchainExplicitPathTests's sibling
    /// test), not just that the param is accepted. Build's EmitCore writes
    /// obj/compile_commands.json during the emit phase, BEFORE the compile step —
    /// so even though compiling against a fake, nonexistent driver path fails
    /// (expected; RunProcess catches the Win32 "file not found" and returns
    /// Success=false), the override has already reached disk as the driver.
    /// </summary>
    [Test]
    public void Build_With_ResolveToolchain_Override_Reaches_CompileCommandsJson_AsTheDriver()
    {
        var project = MakeMinimalCppProject();

        var overridePath = @"C:\fake-override\llvm\bin\clang++.exe"; // never on disk, never really executed
        var result = CppProjectBuilder.Build(project, "Debug",
            resolveToolchain: () => CppToolchain.FromExplicit("llvm", overridePath));

        // Ignore result.Success: the fake path can't actually compile. What matters
        // is that compile_commands.json was already written with it as the driver.
        var ccPath = Path.Combine(_dir, "obj", "compile_commands.json");
        Assert.That(File.Exists(ccPath), Is.True,
            "compile_commands.json must be written during EmitCore, before the (failing) compile step");

        var db = JsonNode.Parse(File.ReadAllText(ccPath))!;
        var args = db[0]!["arguments"]!.AsArray().Select(a => a!.GetValue<string>()).ToList();
        Assert.That(args[0], Is.EqualTo(overridePath),
            "the resolveToolchain override must reach arguments[0] (the driver a real build invokes) verbatim");
    }

    /// <summary>
    /// No resolveToolchain arg at all (the CLI's Program.cs call shape) must keep today's
    /// behavior: the additive default (<c>resolveToolchain ?? CppToolchain.Find</c>) routes
    /// to the real machine probe, not a null/no-op resolver. Pinned to CppToolchain.Find()'s
    /// OWN result (mirroring CppProjectCliBuildTests.cs's toolchain-availability branch)
    /// rather than only checking "some diagnostic exists on failure": a broken default
    /// (e.g. <c>?? (() => null)</c>) and a correct default that simply finds nothing both
    /// land on the same BL6005 diagnostic, so a bare "failed with a diagnostic" assertion
    /// can't tell them apart on a box with no toolchain, and says nothing at all on a box
    /// that DOES have one (a broken default would silently no-op success/failure
    /// unpredictably there too). Branching on Find()'s own verdict makes both machine
    /// states an actual proof that Build's resolver ran the same probe.
    /// </summary>
    [Test]
    public void Build_With_No_ResolveToolchain_Arg_Defaults_To_Find()
    {
        var project = MakeMinimalCppProject();

        var toolchainAvailable = CppToolchain.Find() != null;
        var result = CppProjectBuilder.Build(project, "Debug"); // no override arg

        if (toolchainAvailable)
            Assert.That(result.Success, Is.True, "default must route to CppToolchain.Find and build");
        else
            Assert.That(result.Diagnostics.Select(d => d.Code), Does.Contain("BL6005"));
    }
}
