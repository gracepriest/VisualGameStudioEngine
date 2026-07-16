using System.Text.Json.Nodes;
using BasicLang.Compiler.ProjectSystem;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 9 (C++ Phase 3a): <see cref="IntelliSenseEmitter.Emit"/> is the toolchain-free
/// seam that produces the two things clangd needs — the generated <c>obj/gen</c> headers
/// (so <c>#include "Logic.g.h"</c> resolves) and <c>obj/compile_commands.json</c> — WITHOUT
/// a compiler installed and WITHOUT a build.
///
/// <see cref="CppProjectBuilder.Build"/> rightly refuses a project with no entry point, an
/// unresolvable link-time library, or no toolchain. IntelliSense must not: a project
/// mid-edit still deserves completion, and none of those conditions affect the COMPILE
/// flags a compilation database carries. These tests pin the bypasses.
///
/// Every test passes <c>toolchain: null</c> EXPLICITLY rather than relying on the machine
/// lacking a compiler — this machine has MSVC (vswhere finds VS 2022 + VC.Tools), so a
/// "no toolchain installed" assumption would silently test nothing here.
/// </summary>
[TestFixture]
[NonParallelizable]
public class IntelliSenseEmitterTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-ise-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        for (var i = 0; i < 3; i++)
        {
            try { Directory.Delete(_dir, recursive: true); return; }
            catch { Thread.Sleep(200); }
        }
    }

    // A BasicLang project whose backend is the native C++ toolchain.
    private static string Blproj(string projectName, string itemGroup = "") => $"""
        <BasicLangProject Version="1.0">
          <PropertyGroup>
            <ProjectName>{projectName}</ProjectName>
            <OutputType>Exe</OutputType>
            <TargetBackend>Cpp</TargetBackend>
          </PropertyGroup>
          {itemGroup}
        </BasicLangProject>
        """;

    private ProjectFile WriteProject(string blproj, params (string Name, string Content)[] files)
    {
        foreach (var (name, content) in files)
        {
            var full = Path.Combine(_dir, name);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }
        var path = Path.Combine(_dir, "App.blproj");
        File.WriteAllText(path, blproj);
        return ProjectFile.Load(path);
    }

    private string ObjGen(string fileName) => Path.Combine(_dir, "obj", "gen", fileName);
    private string CompileCommandsPath => Path.Combine(_dir, "obj", "compile_commands.json");

    private static string DiagCodes(CppProjectBuildResult r) =>
        string.Join(", ", r.Diagnostics.Select(d => $"{d.Code}:{d.Message}"));

    // Parsed arguments[0..] of the first compile-database entry.
    private List<string> FirstEntryArguments()
    {
        var db = JsonNode.Parse(File.ReadAllText(CompileCommandsPath))!;
        return db[0]!["arguments"]!.AsArray().Select(a => a!.GetValue<string>()).ToList();
    }

    private const string MainSource = "Sub Main()\n    PrintLine 7\nEnd Sub\n";
    private const string LogicSource = "Function Add(a As Integer, b As Integer) As Integer\n    Return a + b\nEnd Function\n";

    // ---- THE headline test: no compiler installed, still get headers + a compile db ----
    [Test]
    public void Emit_WithNoToolchain_WritesObjGenAndCompileCommands()
    {
        var project = WriteProject(Blproj("App"), ("App.bas", MainSource));

        var r = IntelliSenseEmitter.Emit(project, "Debug", toolchain: null);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(ObjGen("BasicLangRuntime.g.h")), Is.True,
                "the runtime header must exist for clangd to resolve the generated includes");
            Assert.That(File.Exists(CompileCommandsPath), Is.True,
                "clangd cannot work without a compilation database");
            Assert.That(r.Success, Is.True, DiagCodes(r));
        });
    }

    // The driver is arguments[0] SPECIFICALLY — clangd reads that position to pick its
    // parsing mode. A substring assertion over the raw JSON would pass on a path that
    // merely contains "clang++", so parse and assert the POSITION.
    [Test]
    public void Emit_WithNoToolchain_DefaultsToClangDriver()
    {
        var project = WriteProject(Blproj("App"), ("App.bas", MainSource));

        IntelliSenseEmitter.Emit(project, "Debug", toolchain: null);

        var args = FirstEntryArguments();
        Assert.Multiple(() =>
        {
            Assert.That(args[0], Is.EqualTo("clang++"),
                "clangd reads arguments[0] as the driver — it must BE the driver, not merely contain it");
            Assert.That(args, Has.One.EqualTo("-std=c++20"),
                "GNU-style flag as an exact token, not MSVC /std: and not a substring of something else");
            Assert.That(args, Has.None.StartsWith("/std:"),
                "the default driver is clang++, so the flag style must be GNU — a cl-style flag under a "
                + "clang++ driver is exactly the silently-wrong-IntelliSense failure this pins");
        });
    }

    // Kind and driver must always come from the SAME source. When a real toolchain is
    // supplied, the emitter must use ITS identity (on this machine that is MSVC: driver
    // "cl" + /std: flags) — never a clang++ driver over MSVC flags or vice versa.
    [Test]
    public void Emit_WithInstalledToolchain_PairsDriverWithItsOwnFlagStyle()
    {
        var toolchain = CppToolchain.Find();
        if (toolchain == null)
            Assert.Ignore("no C++ toolchain installed — nothing to pair against");

        var project = WriteProject(Blproj("App"), ("App.bas", MainSource));
        IntelliSenseEmitter.Emit(project, "Debug", toolchain);

        var args = FirstEntryArguments();
        var isMsvc = toolchain!.Kind == CppToolchainKind.Msvc;
        Assert.Multiple(() =>
        {
            Assert.That(args[0], Is.EqualTo(toolchain.DriverName),
                "arguments[0] must be the installed toolchain's own driver");
            Assert.That(args, Has.One.EqualTo(isMsvc ? "/std:c++20" : "-std=c++20"),
                "the standard flag's SPELLING must match the driver in arguments[0]");
            Assert.That(args, Has.None.EqualTo(isMsvc ? "-std=c++20" : "/std:c++20"),
                "the other style must not appear at all — mixing them mis-parses in clangd");
        });
    }

    // No Sub Main yet — an ordinary mid-edit state. Build() rightly fails BL6011;
    // IntelliSense must NOT, or the user gets no completion while writing the program.
    [Test]
    public void Emit_WithNoEntryPoint_StillEmitsHeaders()
    {
        var project = WriteProject(Blproj("App"), ("Logic.bas", LogicSource));

        var r = IntelliSenseEmitter.Emit(project, "Debug", toolchain: null);

        Assert.Multiple(() =>
        {
            Assert.That(r.Success, Is.True, DiagCodes(r));
            Assert.That(File.Exists(ObjGen("BasicLangRuntime.g.h")), Is.True);
            Assert.That(File.Exists(ObjGen("Logic.g.h")), Is.True,
                "the module header is what user C++ includes — it must exist without an entry point");
            Assert.That(r.Diagnostics.Select(d => d.Code), Has.None.EqualTo("BL6011"),
                "the entry-point rule is a BUILD rule; it must not reach the IntelliSense path");
        });
    }

    // Broken source yields no IR at all, so there is nothing to regenerate from. The
    // transpile failure returns BEFORE the clean, so the last good headers survive —
    // regen-on-success-only, never wipe on failure.
    [Test]
    public void Emit_WithBrokenSource_LeavesPreviousHeadersIntact()
    {
        var project = WriteProject(Blproj("App"),
            ("App.bas", MainSource), ("Logic.bas", LogicSource));

        var good = IntelliSenseEmitter.Emit(project, "Debug", toolchain: null);
        Assert.That(good.Success, Is.True, DiagCodes(good));
        var before = File.ReadAllText(ObjGen("Logic.g.h"));
        Assert.That(before, Does.StartWith("#pragma once"),
            "sanity: the seeded header must be a real generated header, not empty");

        File.WriteAllText(Path.Combine(_dir, "Logic.bas"), "Function Add(((( As\n");
        var broken = ProjectFile.Load(project.FilePath);
        var r = IntelliSenseEmitter.Emit(broken, "Debug", toolchain: null);

        Assert.Multiple(() =>
        {
            Assert.That(r.Success, Is.False, "a transpile failure is a real failure, not a silent no-op");
            Assert.That(File.Exists(ObjGen("Logic.g.h")), Is.True,
                "a broken edit must not wipe the headers clangd is serving from");
            Assert.That(File.ReadAllText(ObjGen("Logic.g.h")), Is.EqualTo(before),
                "the last good headers must survive byte-for-byte");
        });
    }

    // The include-path ORDER is what makes #include "Logic.g.h" resolve: projectDir first
    // (user quote-includes), then obj/gen (generated shims), then the project's own items.
    [Test]
    public void Emit_IncludePath_ContainsProjectDirAndObjGen()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "vendor"));
        var project = WriteProject(
            Blproj("App", "<ItemGroup><IncludeDir Include=\"vendor\" /></ItemGroup>"),
            ("App.bas", MainSource));

        IntelliSenseEmitter.Emit(project, "Debug", toolchain: null);

        var includes = FirstEntryArguments()
            .Where(a => a.StartsWith("-I"))
            .Select(a => a.Substring(2))
            .ToList();
        Assert.That(includes, Is.EqualTo(new[]
        {
            _dir,
            Path.Combine(_dir, "obj", "gen"),
            Path.Combine(_dir, "vendor"),
        }), "include order is load-bearing: projectDir, then obj/gen, then <IncludeDir> items");
    }

    // MANDATORY — the game-project hole. Build() Fails BL6009 and returns BEFORE the
    // compile_commands.json write, so a game whose engine .lib does not resolve would get
    // headers but no compile db and clangd would have nothing to read. Libraries are
    // LINK-time and never reach FlagsFor, so they contribute nothing to a compilation
    // database: the IntelliSense path skips the block outright and loses nothing.
    [Test]
    public void Emit_GameProject_WithUnresolvableEngineLib_StillWritesCompileCommands()
    {
        var project = WriteProject(
            Blproj("Game", "<ItemGroup><NativeLib Include=\"libs/NoSuchEngine.lib\" /></ItemGroup>"),
            ("App.bas", MainSource));

        // Pin the premise: the very same project through Build() fails at the lib gate.
        var built = CppProjectBuilder.Build(ProjectFile.Load(project.FilePath), "Debug");
        Assert.That(built.Success, Is.False);
        Assert.That(built.Diagnostics.Select(d => d.Code), Does.Contain("BL6009"),
            "premise: Build() must reject the unresolvable library: " + DiagCodes(built));

        var r = IntelliSenseEmitter.Emit(project, "Debug", toolchain: null);

        Assert.Multiple(() =>
        {
            Assert.That(r.Success, Is.True, "a missing link-time .lib must not deny IntelliSense: " + DiagCodes(r));
            Assert.That(File.Exists(CompileCommandsPath), Is.True);
            Assert.That(File.Exists(ObjGen("BasicLangRuntime.g.h")), Is.True);
        });
    }

    // An empty project mid-creation still gets a (degenerate) database rather than BL6007.
    [Test]
    public void Emit_WithNoSources_SucceedsWithEmptyDatabase()
    {
        var project = WriteProject(Blproj("App"));

        var r = IntelliSenseEmitter.Emit(project, "Debug", toolchain: null);

        Assert.Multiple(() =>
        {
            Assert.That(r.Success, Is.True, DiagCodes(r));
            Assert.That(r.Diagnostics.Select(d => d.Code), Has.None.EqualTo("BL6007"),
                "the no-sources BUILD gate must not reach the IntelliSense path");
            Assert.That(JsonNode.Parse(File.ReadAllText(CompileCommandsPath))!.AsArray(), Is.Empty);
        });
    }

    // Emission must never create build-output directories — opening a project in the IDE
    // is not a build.
    [Test]
    public void Emit_DoesNotCreateBinDirectory()
    {
        var project = WriteProject(Blproj("App"), ("App.bas", MainSource));

        IntelliSenseEmitter.Emit(project, "Debug", toolchain: null);

        Assert.That(Directory.Exists(Path.Combine(_dir, "bin")), Is.False,
            "IntelliSense emission must not create bin/<config>");
    }
}
