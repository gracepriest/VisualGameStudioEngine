using System;
using System.IO;
using NUnit.Framework;
using BasicLang.Compiler.ProjectSystem;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// The compiler auto-injects the RaylibWrapper reference and copies the native
/// engine DLL whenever a program uses the game engine, so game projects build
/// and run without the author wiring up engine references by hand.
/// </summary>
[TestFixture]
public class EngineDeploymentTests
{
    // The backend emits `using RaylibWrapper;` whenever it emits engine calls
    // (its using auto-detection keys on "FrameworkWrapper."), so the directive
    // is the single reliable marker. Bare substring matching would false-
    // positive on user string literals mentioning the wrapper.

    [Test]
    public void UsesEngine_DetectsLeadingUsingDirective()
    {
        var code = "using RaylibWrapper;\nusing System;\npublic static class Program { }";
        Assert.That(EngineDeployment.UsesEngine(code), Is.True);
    }

    [Test]
    public void UsesEngine_DetectsUsingDirectiveAfterOtherUsings()
    {
        var code = "using System;\nusing RaylibWrapper;\n\npublic static class Program { static void M() { FrameworkWrapper.Framework_GameInit(800, 600, \"g\"); } }";
        Assert.That(EngineDeployment.UsesEngine(code), Is.True);
    }

    [Test]
    public void UsesEngine_DetectsUsingDirectiveWithWindowsLineEndings()
    {
        var code = "using System;\r\nusing RaylibWrapper;\r\npublic static class Program { }";
        Assert.That(EngineDeployment.UsesEngine(code), Is.True);
    }

    [Test]
    public void UsesEngine_PlainConsoleProgram_False()
    {
        var code = "using System;\npublic static class Program { static void Main() { Console.WriteLine(\"hi\"); } }";
        Assert.That(EngineDeployment.UsesEngine(code), Is.False);
    }

    [Test]
    public void UsesEngine_WrapperNameInsideStringLiteral_False()
    {
        // A non-game program that merely MENTIONS the wrapper (URL, docs text)
        // must not get an engine reference injected.
        var code = "using System;\npublic static class Program { static void Main() { Console.WriteLine(\"see RaylibWrapper and FrameworkWrapper docs\"); } }";
        Assert.That(EngineDeployment.UsesEngine(code), Is.False);
    }

    [Test]
    public void UsesEngine_UsingDirectiveTextInsideStringLiteral_False()
    {
        var code = "using System;\npublic static class Program { static void Main() { Console.WriteLine(\"using RaylibWrapper;\"); } }";
        Assert.That(EngineDeployment.UsesEngine(code), Is.False);
    }

    [Test]
    public void UsesEngine_NullOrEmpty_False()
    {
        Assert.That(EngineDeployment.UsesEngine(null), Is.False);
        Assert.That(EngineDeployment.UsesEngine(""), Is.False);
    }

    // ------------------------------------------------------------------
    // Duplicate-reference detection must handle every legal Include form
    // ------------------------------------------------------------------

    [Test]
    public void IsWrapperReference_MatchesPlainName()
    {
        var r = new AssemblyReference { Name = "RaylibWrapper" };
        Assert.That(EngineDeployment.IsWrapperReference(r), Is.True);
    }

    [Test]
    public void IsWrapperReference_MatchesDllSuffixedName()
    {
        var r = new AssemblyReference { Name = "RaylibWrapper.dll" };
        Assert.That(EngineDeployment.IsWrapperReference(r), Is.True);
    }

    [Test]
    public void IsWrapperReference_MatchesFullPathName()
    {
        var r = new AssemblyReference { Name = @"C:\libs\RaylibWrapper.dll" };
        Assert.That(EngineDeployment.IsWrapperReference(r), Is.True);
    }

    [Test]
    public void IsWrapperReference_MatchesByHintPath()
    {
        var r = new AssemblyReference { Name = "MyEngineAlias", HintPath = @"..\libs\RaylibWrapper.dll" };
        Assert.That(EngineDeployment.IsWrapperReference(r), Is.True);
    }

    [Test]
    public void IsWrapperReference_OtherLibrary_False()
    {
        var r = new AssemblyReference { Name = "Newtonsoft.Json", HintPath = @"..\libs\Newtonsoft.Json.dll" };
        Assert.That(EngineDeployment.IsWrapperReference(r), Is.False);
        Assert.That(EngineDeployment.IsWrapperReference(null), Is.False);
    }

    // ------------------------------------------------------------------
    // Wrapper presence probe (guards reference injection)
    // ------------------------------------------------------------------

    [Test]
    public void WrapperExists_TrueWhenDllPresent_FalseWhenAbsent()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "bl-engine-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baseDir);
        try
        {
            Assert.That(EngineDeployment.WrapperExists(baseDir), Is.False);
            File.WriteAllText(Path.Combine(baseDir, "RaylibWrapper.dll"), "stub");
            Assert.That(EngineDeployment.WrapperExists(baseDir), Is.True);
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }

    [Test]
    public void GetEngineReference_HasWrapperNameAndHintPathInBaseDir()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "bl-engine-" + Guid.NewGuid().ToString("N"));
        var reference = EngineDeployment.GetEngineReference(baseDir);

        Assert.That(reference, Is.Not.Null);
        Assert.That(reference.Name, Is.EqualTo("RaylibWrapper"));
        Assert.That(reference.HintPath, Is.EqualTo(Path.Combine(baseDir, "RaylibWrapper.dll")));
    }

    [Test]
    public void GetNativeDllPaths_ReturnsExistingNativeEngineDll()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "bl-engine-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baseDir);
        try
        {
            var enginePath = Path.Combine(baseDir, "VisualGameStudioEngine.dll");
            File.WriteAllText(enginePath, "native-stub");

            var paths = EngineDeployment.GetNativeDllPaths(baseDir);

            Assert.That(paths, Does.Contain(enginePath));
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }

    [Test]
    public void GetNativeDllPaths_MissingDll_ReturnsEmpty()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "bl-engine-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baseDir);
        try
        {
            Assert.That(EngineDeployment.GetNativeDllPaths(baseDir), Is.Empty);
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }
}
