using BasicLang.Compiler.ProjectSystem;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 8 (C++ Phase 3a): <see cref="CppProjectBuilder.Build"/> must generate the split
/// C++ file set into memory BEFORE cleaning obj/gen, so a codegen failure (an unsupported
/// construct raising <c>CppCapabilityException</c>, or the reserved-name
/// <c>ArgumentException</c>) can never wipe the previously-generated IntelliSense headers.
///
/// Before the fix, <c>CleanGeneratedDir</c> ran first and <c>GenerateSplit</c> second, so a
/// single unsupported construct silently destroyed every header clangd depends on between
/// builds. These tests seed obj/gen with a good build, then drive a throwing build and assert
/// the earlier headers survive byte-for-byte.
/// </summary>
[TestFixture]
[NonParallelizable]
public class CppProjectBuilderCleanTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-clean-" + Guid.NewGuid().ToString("N"));
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

    // A BasicLang project whose backend is the native C++ toolchain (no <Language>Cpp>).
    private static string BackendCppBlproj(string projectName) => $"""
        <BasicLangProject Version="1.0">
          <PropertyGroup>
            <ProjectName>{projectName}</ProjectName>
            <OutputType>Exe</OutputType>
            <TargetBackend>Cpp</TargetBackend>
          </PropertyGroup>
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

    private static string DiagCodes(CppProjectBuildResult r) =>
        string.Join(", ", r.Diagnostics.Select(d => $"{d.Code}:{d.Message}"));

    // A construct that transpiles cleanly (parse + semantic + IR) but that the C++ backend's
    // capability checker rejects at GenerateSplit: a *binding* pattern in a Select Case.
    // Mirror of CppSelectCaseTests.Cpp_SelectCase_BindingPattern_RejectedWithCleanCapabilityError.
    private const string ThrowingSource = """
        Sub Main()
            Dim y As Integer = 5
            Select Case y
                Case n When n > 0
                    Console.WriteLine("pos")
                Case Else
                    Console.WriteLine("nonpos")
            End Select
        End Sub
        """;

    [Test]
    public void CodegenFailure_DoesNotWipe_PreviouslyGeneratedHeaders()
    {
        var objGen = Path.Combine(_dir, "obj", "gen");
        var runtimeHeader = Path.Combine(objGen, "BasicLangRuntime.g.h");
        var projectHeader = Path.Combine(objGen, "SafeApp.g.h");
        var moduleHeader = Path.Combine(objGen, "App.g.h");

        // ---- Step 1: a GOOD build seeds obj/gen. -----------------------------------------
        // Without a toolchain Build() returns Success=false at BL6005, but it has already
        // written obj/gen (that write precedes the toolchain gate). With a toolchain it
        // succeeds; either way the generated headers exist on disk afterwards. That the
        // headers exist BEFORE the toolchain gate is exactly the ordering Task 9 depends on.
        var good = WriteProject(BackendCppBlproj("SafeApp"),
            ("App.bas", "Sub Main()\n    PrintLine 7\nEnd Sub\n"));
        CppProjectBuilder.Build(good, "Debug");

        Assert.That(File.Exists(runtimeHeader), Is.True, "good build must seed BasicLangRuntime.g.h");
        Assert.That(File.Exists(projectHeader), Is.True, "good build must seed SafeApp.g.h");
        Assert.That(File.Exists(moduleHeader), Is.True, "good build must seed App.g.h");

        var runtimeBefore = File.ReadAllText(runtimeHeader);
        var projectBefore = File.ReadAllText(projectHeader);
        var moduleBefore = File.ReadAllText(moduleHeader);
        Assert.That(projectBefore, Does.StartWith("#pragma once"),
            "sanity: the seeded project header must be a real generated header, not empty");

        // ---- Step 2: overwrite the source with a construct codegen rejects, rebuild. ------
        File.WriteAllText(Path.Combine(_dir, "App.bas"), ThrowingSource);
        var bad = ProjectFile.Load(good.FilePath);
        var badResult = CppProjectBuilder.Build(bad, "Debug");

        // The build fails AT CODEGEN (BL6001), proving GenerateSplit actually threw — not at
        // the transpile stage and not at the toolchain gate. If this changes, the test below
        // would be asserting survival of headers past the wrong failure point.
        Assert.That(badResult.Success, Is.False);
        Assert.That(badResult.Diagnostics.Select(d => d.Code), Does.Contain("BL6001"),
            "the throwing build must fail at codegen (BL6001), not elsewhere: " + DiagCodes(badResult));

        // ---- The point of the whole task: the earlier headers survive, byte-for-byte. -----
        Assert.That(File.Exists(runtimeHeader), Is.True,
            "a codegen failure must NOT wipe the previously-generated runtime header");
        Assert.That(File.Exists(projectHeader), Is.True,
            "a codegen failure must NOT wipe the previously-generated project header");
        Assert.That(File.Exists(moduleHeader), Is.True,
            "a codegen failure must NOT wipe the previously-generated module header");
        Assert.That(File.ReadAllText(runtimeHeader), Is.EqualTo(runtimeBefore),
            "runtime header content must be untouched by the failed build");
        Assert.That(File.ReadAllText(projectHeader), Is.EqualTo(projectBefore),
            "project header content must be untouched by the failed build");
        Assert.That(File.ReadAllText(moduleHeader), Is.EqualTo(moduleBefore),
            "module header content must be untouched by the failed build");
    }
}
