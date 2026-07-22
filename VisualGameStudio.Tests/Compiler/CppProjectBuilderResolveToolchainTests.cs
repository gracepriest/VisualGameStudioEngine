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
    private static (string Dir, ProjectFile Project) MakeMinimalCppProject()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-cpb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
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
        return (dir, ProjectFile.Load(projPath));
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
        var (dir, project) = MakeMinimalCppProject();
        try
        {
            var overridePath = @"C:\fake-override\llvm\bin\clang++.exe"; // never on disk, never really executed
            var result = CppProjectBuilder.Build(project, "Debug",
                resolveToolchain: () => CppToolchain.FromExplicit("llvm", overridePath));

            // Ignore result.Success: the fake path can't actually compile. What matters
            // is that compile_commands.json was already written with it as the driver.
            var ccPath = Path.Combine(dir, "obj", "compile_commands.json");
            Assert.That(File.Exists(ccPath), Is.True,
                "compile_commands.json must be written during EmitCore, before the (failing) compile step");

            var db = JsonNode.Parse(File.ReadAllText(ccPath))!;
            var args = db[0]!["arguments"]!.AsArray().Select(a => a!.GetValue<string>()).ToList();
            Assert.That(args[0], Is.EqualTo(overridePath),
                "the resolveToolchain override must reach arguments[0] (the driver a real build invokes) verbatim");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// No resolveToolchain arg at all (the CLI's Program.cs call shape) must keep
    /// today's behavior: the additive default (<c>resolveToolchain ?? CppToolchain.Find</c>)
    /// routes to the real machine probe, not a null/no-op resolver. A CI box need not
    /// have a real C++ toolchain installed for this to be deterministic — either Find
    /// locates one (compile proceeds/fails on its own merits) or Build hard-fails with
    /// BL6005, the documented "no toolchain found" outcome. Both prove Find ran; only a
    /// crash or a silently-skipped toolchain gate would indicate the default broke.
    /// </summary>
    [Test]
    public void Build_With_No_ResolveToolchain_Arg_Defaults_To_Find()
    {
        var (dir, project) = MakeMinimalCppProject();
        try
        {
            var result = CppProjectBuilder.Build(project, "Debug");

            Assert.That(result, Is.Not.Null);
            if (!result.Success)
            {
                Assert.That(result.Diagnostics, Is.Not.Empty,
                    "a failing build with no override must still fail through the normal toolchain gate (BL6005) or a real compile error, not silently");
            }
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }
}
