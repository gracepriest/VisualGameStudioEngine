using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using BasicLang.Compiler.ProjectSystem;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Owner directive (Sep 11 2026): a BasicLang native project ALWAYS compiles with
/// MSVC — no machine probe, never clang++ or g++. Pure C++ projects
/// (<c>Language=Cpp</c>), whose wizard offers an explicit llvm/gcc/msvc pick, are
/// deliberately untouched.
///
/// The rule lives in ONE place — <see cref="ProjectFile.EffectiveCppToolchain"/> —
/// because two consumers read it (<c>CppProjectBuilder.EmitCore</c>'s toolchain gate
/// and the IDE BuildService's invalid-override pre-check), and CLAUDE.md's
/// shared-resolver rule says change such source once, not per-consumer.
/// </summary>
[TestFixture]
public class BasicLangNativeMsvcPolicyTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-msvc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    // Retries like CppProjectBuilderResolveToolchainTests' TearDown (same "wrote
    // obj/compile_commands.json under a temp dir" scenario) — a transient Windows
    // file lock or AV scan must not leak the temp dir.
    [TearDown]
    public void TearDown()
    {
        for (var i = 0; i < 3; i++)
        {
            try { Directory.Delete(_dir, recursive: true); return; }
            catch { Thread.Sleep(200); }
        }
    }

    /// <summary>A BasicLang project targeting the C++ backend: no &lt;Language&gt;, a .bas source.</summary>
    private ProjectFile BasicLangNative(string? pin = null)
    {
        File.WriteAllText(Path.Combine(_dir, "App.bas"), "Sub Main()\n    PrintLine 7\nEnd Sub\n");
        return WriteProject(pin, language: null);
    }

    /// <summary>A pure C++ project: &lt;Language&gt;Cpp&lt;/Language&gt;, a .cpp source, no BasicLang.</summary>
    private ProjectFile PureCpp(string? pin = null)
    {
        File.WriteAllText(Path.Combine(_dir, "main.cpp"), "int main() { return 0; }\n");
        return WriteProject(pin, language: "Cpp");
    }

    private ProjectFile WriteProject(string? pin, string? language)
    {
        var languageLine = language == null ? "" : $"\n    <Language>{language}</Language>";
        var pinLine = pin == null ? "" : $"\n    <CppToolchain>{pin}</CppToolchain>";
        var path = Path.Combine(_dir, "App.blproj");
        File.WriteAllText(path, $"""
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>App</ProjectName>
                <OutputType>Exe</OutputType>{languageLine}
                <TargetBackend>Cpp</TargetBackend>{pinLine}
              </PropertyGroup>
            </BasicLangProject>
            """);
        return ProjectFile.Load(path);
    }

    /// <summary>An MSVC-kind toolchain that never runs: Kind=Msvc, DriverName="cl".</summary>
    private static CppToolchain FakeMsvc() =>
        CppToolchain.FromExplicit("msvc", Path.Combine(Path.GetTempPath(), "fake-vcvars64.bat"))!;

    // ---- The policy itself ----

    [Test]
    public void BasicLangNative_WithNoPin_ResolvesMsvc()
    {
        Assert.That(BasicLangNative().EffectiveCppToolchain, Is.EqualTo("msvc"));
    }

    [Test]
    public void BasicLangNative_IgnoresAnExplicitLlvmPin()
    {
        Assert.That(BasicLangNative(pin: "llvm").EffectiveCppToolchain, Is.EqualTo("msvc"),
            "owner directive: a hand-edited pin must not route a BasicLang native build away from MSVC");
    }

    [Test]
    public void PureCpp_KeepsItsExplicitPin()
    {
        Assert.That(PureCpp(pin: "llvm").EffectiveCppToolchain, Is.EqualTo("llvm"),
            "the pure C++ project type offers llvm/gcc/msvc in the wizard — the MSVC rule must not reach it");
    }

    [Test]
    public void PureCpp_WithNoPin_StaysUnpinned()
    {
        Assert.That(PureCpp().EffectiveCppToolchain, Is.Null,
            "null is what keeps the machine probe alive for pure C++ projects");
    }

    // ---- What the BUILD actually does with it ----

    /// <summary>
    /// The discriminating half of the pair: that the property returns "msvc" says
    /// nothing about whether emission honors it. EmitCore must resolve through
    /// <c>resolveById("msvc")</c> and never call the machine probe at all.
    /// </summary>
    [Test]
    public void EmitCore_ForBasicLangNative_NeverCallsTheMachineProbe()
    {
        var probed = false;
        var askedFor = new List<string>();

        CppProjectBuilder.EmitCore(BasicLangNative(), "Debug", new CppProjectBuildResult(),
            resolveToolchain: () => { probed = true; return FakeMsvc(); },
            forIntelliSense: false,
            resolveById: id => { askedFor.Add(id); return FakeMsvc(); },
            publishShim: false);

        Assert.Multiple(() =>
        {
            Assert.That(probed, Is.False, "a BasicLang native build must not probe for clang++/g++");
            Assert.That(askedFor, Is.EqualTo(new[] { "msvc" }), "it must ask for msvc by id, exactly once");
        });
    }

    /// <summary>
    /// The other half: a rule that forced MSVC for every native project would pass
    /// the test above and still be wrong. An unpinned pure C++ project keeps the probe.
    /// </summary>
    [Test]
    public void EmitCore_ForUnpinnedPureCpp_StillCallsTheMachineProbe()
    {
        var probed = false;
        var askedFor = new List<string>();

        CppProjectBuilder.EmitCore(PureCpp(), "Debug", new CppProjectBuildResult(),
            resolveToolchain: () => { probed = true; return FakeMsvc(); },
            forIntelliSense: false,
            resolveById: id => { askedFor.Add(id); return FakeMsvc(); },
            publishShim: false);

        Assert.Multiple(() =>
        {
            Assert.That(probed, Is.True, "an unpinned pure C++ project keeps the machine probe");
            Assert.That(askedFor, Is.Empty, "...and must not be routed through the by-id path");
        });
    }

    /// <summary>
    /// The whole chain, not just the resolution. clangd picks its parsing mode from
    /// arguments[0], so the driver and the flag spelling have to move together — a
    /// "cl" driver carrying <c>-std=c++20</c> parses silently wrong.
    /// </summary>
    [Test]
    public void EmitCore_ForBasicLangNative_WritesClAndMsvcFlagsToCompileCommands()
    {
        CppProjectBuilder.EmitCore(BasicLangNative(), "Debug", new CppProjectBuildResult(),
            resolveToolchain: () => throw new InvalidOperationException("the machine probe must not run"),
            forIntelliSense: false,
            resolveById: _ => FakeMsvc(),
            publishShim: false);

        var db = JsonNode.Parse(File.ReadAllText(Path.Combine(_dir, "obj", "compile_commands.json")))!;
        var args = db[0]!["arguments"]!.AsArray().Select(a => a!.GetValue<string>()).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(args[0], Is.EqualTo("cl"), "arguments[0] is what clangd reads to pick cl-driver mode");
            Assert.That(args, Does.Contain("/std:c++20"), "MSVC flag spelling, not -std=c++20");
            Assert.That(args, Has.None.StartsWith("-std="), "no GNU flag may survive on the MSVC path");
        });
    }
}
