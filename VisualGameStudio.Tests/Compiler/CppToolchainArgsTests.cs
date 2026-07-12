using BasicLang.Compiler.ProjectSystem;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

[TestFixture]
public class CppToolchainArgsTests
{
    private static CppCompileRequest Request()
    {
        var r = new CppCompileRequest
        {
            OutputPath = @"C:\proj\bin\Debug\App.exe",
            CppStandard = "c++20",
            WorkingDirectory = @"C:\proj\bin\Debug",
            DebugSymbols = true,
            Optimize = false,
        };
        r.SourceFiles.Add(@"C:\proj\main.cpp");
        r.SourceFiles.Add(@"C:\proj\util.cpp");
        r.IncludeDirs.Add(@"C:\proj\vendor\include");
        r.Defines.Add("MY_FLAG");
        r.Libraries.Add(@"C:\tools\VisualGameStudioEngine.lib");
        return r;
    }

    [Test]
    public void ClangLike_PerTuArguments_ContainStdIncludeDefine()
    {
        var args = CppToolchain.BuildCompileCommandArguments(
            CppToolchainKind.ClangLike, "clang++", Request(), @"C:\proj\main.cpp");
        Assert.That(args[0], Is.EqualTo("clang++"));
        Assert.That(args, Does.Contain("-std=c++20"));
        Assert.That(args, Does.Contain(@"-IC:\proj\vendor\include"));
        Assert.That(args, Does.Contain("-DMY_FLAG"));
        Assert.That(args, Does.Contain("-g"), "DebugSymbols=true emits -g");
        Assert.That(args, Does.Contain(@"C:\proj\main.cpp"));
        Assert.That(args, Does.Not.Contain(@"C:\proj\util.cpp"), "per-TU entry lists only its own file");
    }

    [Test]
    public void Msvc_PerTuArguments_UseSlashFlags()
    {
        var args = CppToolchain.BuildCompileCommandArguments(
            CppToolchainKind.Msvc, "cl", Request(), @"C:\proj\main.cpp");
        Assert.That(args[0], Is.EqualTo("cl"));
        Assert.That(args, Does.Contain("/std:c++20"));
        Assert.That(args, Does.Contain("/EHsc"));
        Assert.That(args, Does.Contain(@"/IC:\proj\vendor\include"));
        Assert.That(args, Does.Contain("/DMY_FLAG"));
        Assert.That(args, Does.Contain("/Zi"), "DebugSymbols=true emits /Zi");
    }

    private static CppCompileRequest OptimizedRequest()
    {
        var r = Request();
        r.Optimize = true;
        r.DebugSymbols = false;
        return r;
    }

    [Test]
    public void ClangLike_Optimize_EmitsO2()
    {
        var args = CppToolchain.BuildCompileCommandArguments(
            CppToolchainKind.ClangLike, "clang++", OptimizedRequest(), @"C:\proj\main.cpp");
        Assert.That(args, Does.Contain("-O2"));
        Assert.That(args, Does.Not.Contain("-O0"));
        Assert.That(args, Does.Not.Contain("-g"), "no debug flag when DebugSymbols=false");
    }

    [Test]
    public void Msvc_Optimize_EmitsO2()
    {
        var args = CppToolchain.BuildCompileCommandArguments(
            CppToolchainKind.Msvc, "cl", OptimizedRequest(), @"C:\proj\main.cpp");
        Assert.That(args, Does.Contain("/O2"));
        Assert.That(args, Does.Not.Contain("/Od"));
        Assert.That(args, Does.Not.Contain("/Zi"), "no debug flag when DebugSymbols=false");
    }

    [Test]
    public void FindDuplicateBasename_DetectsCollisionAcrossDirectories()
    {
        // cl /c and g++ -c drop basename-derived .obj/.o into one directory,
        // so same-named TUs in different folders would overwrite each other.
        var dup = CppToolchain.FindDuplicateBasename(new[]
        {
            @"C:\proj\audio\util.cpp",
            @"C:\proj\video\Util.cpp",
        });
        Assert.That(dup, Is.EqualTo("util").IgnoreCase);
    }

    [Test]
    public void FindDuplicateBasename_NullWhenAllDistinct()
    {
        var dup = CppToolchain.FindDuplicateBasename(new[]
        {
            @"C:\proj\audio\util.cpp",
            @"C:\proj\video\main.cpp",
        });
        Assert.That(dup, Is.Null);
    }
}
