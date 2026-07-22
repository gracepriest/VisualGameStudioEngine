using System.Text.Json.Nodes;
using BasicLang.Compiler.ProjectSystem;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 7 (per-backend C++ toolchain overrides): <see cref="IntelliSenseEmitter.Emit"/> gains an
/// optional <c>resolveById</c> so a project pinned via <c>&lt;CppToolchain&gt;</c> to a
/// settings-configured, possibly off-PATH compiler resolves through the SAME identity a real
/// build would use.
///
/// <see cref="CppProjectBuilder.EmitCore"/> resolves a pin as
/// <c>(resolveById ?? CppToolchain.TryFindById)(requestedId)</c> — before this task,
/// <see cref="IntelliSenseEmitter.Emit"/> forwarded no <c>resolveById</c> at all, so a pin to an
/// off-PATH toolchain (e.g. winlibs gcc, deliberately never on PATH here — see
/// <see cref="CppToolchainExplicitPathTests"/>) resolved to null and the compile database fell
/// back to the clang++ default identity — clangd drifting from what BuildService's own
/// override-aware resolver (Task 6) actually compiles with. This fixture pins the fix at the
/// same seam <see cref="CppToolchainExplicitPathTests"/> uses for the unpinned case.
/// </summary>
[TestFixture]
public class IntelliSenseEmitterOverrideTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-ise-ov-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    // Retries like IntelliSenseEmitterTests.cs's TearDown / CppToolchainExplicitPathTests.cs's
    // TearDown (same "wrote obj/compile_commands.json under a temp dir" scenario) — a transient
    // Windows file lock (or AV scan) on a just-written file must not leak the temp dir.
    [TearDown]
    public void TearDown()
    {
        for (var i = 0; i < 3; i++)
        {
            try { Directory.Delete(_dir, recursive: true); return; }
            catch { Thread.Sleep(200); }
        }
    }

    private ProjectFile WriteProject(string cppToolchainPin)
    {
        var blproj = $"""
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>App</ProjectName>
                <OutputType>Exe</OutputType>
                <TargetBackend>Cpp</TargetBackend>
                <CppToolchain>{cppToolchainPin}</CppToolchain>
              </PropertyGroup>
            </BasicLangProject>
            """;
        File.WriteAllText(Path.Combine(_dir, "App.bas"), "Sub Main()\n    PrintLine 7\nEnd Sub\n");
        var path = Path.Combine(_dir, "App.blproj");
        File.WriteAllText(path, blproj);
        return ProjectFile.Load(path);
    }

    private List<string> FirstEntryArguments()
    {
        var db = JsonNode.Parse(File.ReadAllText(Path.Combine(_dir, "obj", "compile_commands.json")))!;
        return db[0]!["arguments"]!.AsArray().Select(a => a!.GetValue<string>()).ToList();
    }

    // THE headline case: a project pinned to gcc, resolved through a resolveById that names an
    // off-PATH fake g++ (never touched on disk / never executed — winlibs stays off PATH). The
    // driver in the emitted database must be the override, not the clang++ default that a
    // resolveById-less Emit would fall back to.
    [Test]
    public void Emit_PinnedToGcc_WithOverrideResolver_NamesTheOverrideDriver_NotClang()
    {
        var project = WriteProject("gcc");
        var fakeGpp = @"C:\fake-override\winlibs\bin\g++.exe"; // never touched on disk / never executed

        var result = IntelliSenseEmitter.Emit(
            project, "Debug", toolchain: null,
            resolveById: id => CppToolchain.FromExplicit(id, fakeGpp));

        Assert.That(result.Success, Is.True,
            string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}:{d.Message}")));

        var args = FirstEntryArguments();
        Assert.Multiple(() =>
        {
            // clangd reads arguments[0] as the driver — assert the POSITION, matching
            // CppToolchainExplicitPathTests / IntelliSenseEmitterTests's own pattern.
            Assert.That(args[0], Is.EqualTo(fakeGpp),
                "the pinned override must reach arguments[0] verbatim — NOT clang++, the default "
                + "identity a resolveById-less Emit would have fallen back to for an off-PATH pin");
            Assert.That(args, Has.One.EqualTo("-std=c++20"),
                "gcc is ClangLike-kind (GNU flag style), so the flag spelling must pair with the driver");
            Assert.That(args, Has.None.StartsWith("/std:"),
                "MSVC flags under a g++-family driver mis-parse silently in clangd");
        });
    }

    // Without a resolveById, a pin to an off-PATH id resolves to null (TryFindById probes real
    // PATH and finds nothing for this fake id) and IntelliSense's null-toolchain fallback kicks
    // in — clang++. Pins the BEFORE state so the fix above is proven against a real regression,
    // not a tautology.
    [Test]
    public void Emit_PinnedToGcc_WithoutResolveById_FallsBackToClangDefault()
    {
        var project = WriteProject("gcc");

        var result = IntelliSenseEmitter.Emit(project, "Debug", toolchain: null);

        Assert.That(result.Success, Is.True,
            string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}:{d.Message}")));
        var args = FirstEntryArguments();
        Assert.That(args[0], Is.EqualTo("clang++"),
            "sanity/regression pin: with no resolveById, EmitCore's default TryFindById probes " +
            "the real PATH — this fake id is never installed, so it falls back to the clang++ default");
    }

    // An unpinned project must keep using the toolchain PARAMETER, never resolveById — the two
    // seams serve different callers (Task 6's resolveToolchain-vs-resolveById split) and must not
    // bleed into each other.
    [Test]
    public void Emit_Unpinned_IgnoresResolveById_UsesToolchainParameter()
    {
        var project = WriteProject(cppToolchainPin: "");
        var explicitToolchain = CppToolchain.FromExplicit("llvm", @"C:\fake-override\llvm\bin\clang++.exe");

        var result = IntelliSenseEmitter.Emit(
            project, "Debug", toolchain: explicitToolchain,
            resolveById: _ => throw new InvalidOperationException(
                "resolveById must not be called for an unpinned project"));

        Assert.That(result.Success, Is.True,
            string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}:{d.Message}")));
        Assert.That(FirstEntryArguments()[0], Is.EqualTo(@"C:\fake-override\llvm\bin\clang++.exe"));
    }
}
