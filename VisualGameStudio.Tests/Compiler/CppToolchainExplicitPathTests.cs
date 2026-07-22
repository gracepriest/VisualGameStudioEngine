using System.Text.Json.Nodes;
using BasicLang.Compiler.ProjectSystem;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 4 (per-backend C++ toolchain overrides): <see cref="CppToolchain.FromExplicit"/>
/// turns a resolved, user-configured path (a Settings override — see
/// <see cref="VisualGameStudio.ProjectSystem.Services.CppToolchainOverrides"/> in Tasks 2/3)
/// into a real <see cref="CppToolchain"/>, the same object <see cref="CppToolchain.Find"/> /
/// <see cref="CppToolchain.TryFindById"/> produce from a PATH probe. BasicLang itself stays
/// settings-agnostic: the override arrives here only as a resolved path string.
/// </summary>
[TestFixture]
public class CppToolchainExplicitPathTests
{
    [Test]
    public void FromExplicit_Llvm_DriverName_Is_Full_Path()
    {
        // llvm/gcc invoke the compiler by its full configured path (bypassing PATH
        // entirely), so compile_commands.json's arguments[0] names that exact path —
        // clangd (and a real build) must launch THIS binary, not whatever "clang++"
        // resolves to on PATH.
        var tc = CppToolchain.FromExplicit("llvm", @"C:\llvm\bin\clang++.exe");
        Assert.That(tc, Is.Not.Null);
        Assert.That(tc!.DriverName, Is.EqualTo(@"C:\llvm\bin\clang++.exe"));
        Assert.That(tc.Kind, Is.EqualTo(CppToolchainKind.ClangLike));
    }

    [Test]
    public void FromExplicit_Gcc_DriverName_Is_Full_Path()
    {
        var tc = CppToolchain.FromExplicit("gcc", @"C:\w\mingw64\bin\g++.exe");
        Assert.That(tc, Is.Not.Null);
        Assert.That(tc!.DriverName, Is.EqualTo(@"C:\w\mingw64\bin\g++.exe"));
        Assert.That(tc.Kind, Is.EqualTo(CppToolchainKind.ClangLike));
    }

    [Test]
    public void FromExplicit_Msvc_Is_Msvc_Kind()
    {
        // MSVC reuses the existing cmd.exe + vcvars mechanism: the configured path is
        // the vcvars64.bat, never cl.exe directly, and the driver stays "cl" (Kind keys
        // off the vcvars path, exactly like the PATH-probed FindMsvc()).
        var tc = CppToolchain.FromExplicit("msvc", @"C:\VS\VC\Auxiliary\Build\vcvars64.bat");
        Assert.That(tc, Is.Not.Null);
        Assert.That(tc!.Kind, Is.EqualTo(CppToolchainKind.Msvc));
        Assert.That(tc.DriverName, Is.EqualTo("cl"));
    }

    [Test]
    public void FromExplicit_UnknownId_ReturnsNull()
        => Assert.That(CppToolchain.FromExplicit("borland", @"C:\borland\bcc.exe"), Is.Null);

    /// <summary>
    /// Proves the override path reaches a real compile command, not just the two
    /// property getters above. Drives the SAME <see cref="CppProjectBuilder.EmitCore"/>
    /// path <see cref="CppProjectBuilder.Build"/> and <see cref="IntelliSenseEmitter"/>
    /// share (transpile a real BasicLang project to C++, then write
    /// obj/compile_commands.json) — this is the CLI/IDE-shared emission path, not the
    /// isolated CppCodeGenerator string helper (CompileToCppOptimized in
    /// CppCollectionTests.cs), which only asserts generated C++ SOURCE text and never
    /// touches CppToolchain/compile_commands.json at all, so it cannot exercise a driver
    /// path. <see cref="IntelliSenseEmitter.Emit"/> is used (not <see cref="CppProjectBuilder.Build"/>)
    /// so the test needs no real compiler installed and never spawns the fake path as a
    /// process — existence of the override is BasicLang's caller's job (Task 1's
    /// ToolchainPathValidator / Task 2's CppToolchainOverrides), not this factory's.
    /// arguments[0] is asserted by POSITION (matching Emit_WithInstalledToolchain_
    /// PairsDriverWithItsOwnFlagStyle in IntelliSenseEmitterTests.cs): clangd reads that
    /// slot to pick its parsing mode, so a substring/Contains check over the raw JSON
    /// would pass even if the override path only showed up as an argument, not the driver.
    /// </summary>
    [Test]
    public void FromExplicit_Llvm_ReachesCompileCommandsJson_AsTheDriver()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-tcx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
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
            File.WriteAllText(Path.Combine(dir, "App.bas"), "Sub Main()\n    PrintLine 7\nEnd Sub\n");
            var projPath = Path.Combine(dir, "App.blproj");
            File.WriteAllText(projPath, blproj);
            var project = ProjectFile.Load(projPath);

            var overridePath = @"C:\fake-override\llvm\bin\clang++.exe"; // never touched on disk / never executed
            var toolchain = CppToolchain.FromExplicit("llvm", overridePath);

            var result = IntelliSenseEmitter.Emit(project, "Debug", toolchain);
            Assert.That(result.Success, Is.True,
                string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}:{d.Message}")));

            var ccPath = Path.Combine(dir, "obj", "compile_commands.json");
            Assert.That(File.Exists(ccPath), Is.True);
            var db = JsonNode.Parse(File.ReadAllText(ccPath))!;
            var args = db[0]!["arguments"]!.AsArray().Select(a => a!.GetValue<string>()).ToList();

            Assert.That(args[0], Is.EqualTo(overridePath),
                "the override path must reach arguments[0] (the driver clangd/a real build invokes) verbatim");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }
}
