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

    /// <summary>
    /// The generated blnet runtime guards its callback and invocation-queue tables with
    /// <c>std::mutex</c>, so a .NET-enabled native project needs a threading library at LINK
    /// time. <c>-pthread</c> is the portable spelling and must be in the GCC/Clang flag set.
    ///
    /// <para><b>⛔ Why a flag test rather than a build test.</b> This defect is INVISIBLE on
    /// Linux. Since glibc 2.34 the pthread symbols live in libc, so clang++ resolves
    /// <c>std::mutex</c> with no flag and the identical project links clean — there is no
    /// Linux build that can go red for it. On MinGW the POSIX threading model routes those
    /// calls through <c>gthr-default.h</c> to winpthreads, which is not on the link line by
    /// default, and EVERY translation unit fails with <c>undefined reference to
    /// pthread_mutex_init</c> (measured: helper.o, Main.o, and the generated blnet_startup.o).
    /// Pinning the flag is therefore the only oracle that works on both platforms.</para>
    /// </summary>
    [Test]
    public void ClangLike_PerTuArguments_RequestAThreadingLibrary()
    {
        var args = CppToolchain.BuildCompileCommandArguments(
            CppToolchainKind.ClangLike, "clang++", Request(), @"C:\proj\main.cpp");

        Assert.That(args, Does.Contain("-pthread"),
            "the GCC/Clang flag set lost -pthread. blnet_runtime.hpp uses std::mutex, so a MinGW "
            + "link of any .NET-enabled native project will fail with `undefined reference to "
            + "pthread_mutex_*` from every object file — including generated ones the user never "
            + "wrote. A Linux build will NOT reproduce it: glibc 2.34+ puts those symbols in "
            + "libc. Do not delete this because 'the build is green here'.");
    }

    /// <summary>MSVC's standard library threading is built in; -pthread is a GCC/Clang spelling
    /// and must never reach a <c>cl</c> command line.</summary>
    [Test]
    public void Msvc_PerTuArguments_DoNotRequestPthread()
    {
        var args = CppToolchain.BuildCompileCommandArguments(
            CppToolchainKind.Msvc, "cl", Request(), @"C:\proj\main.cpp");

        Assert.That(args, Does.Not.Contain("-pthread"),
            "-pthread is a GCC/Clang flag; cl would reject it as an unknown option.");
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

    [Test]
    public void QuoteToken_SpaceToken_WrappedInQuotes()
    {
        Assert.That(CppToolchain.QuoteToken(@"/IC:\path with spaces"),
            Is.EqualTo("\"" + @"/IC:\path with spaces" + "\""));
    }

    [Test]
    public void QuoteToken_CmdMetacharacterWithoutSpace_WrappedInQuotes()
    {
        // Unquoted '&' would split the cmd.exe command line on the MSVC path.
        Assert.That(CppToolchain.QuoteToken(@"/IC:\proj\a&b\inc"),
            Is.EqualTo("\"" + @"/IC:\proj\a&b\inc" + "\""));
    }

    [Test]
    public void QuoteToken_SpacePlusTrailingBackslash_DoublesTrailingBackslashes()
    {
        // MSVC CRT arg parsing: a trailing \ right before the closing quote
        // would escape it and swallow subsequent tokens, so it must double.
        Assert.That(CppToolchain.QuoteToken(@"/IC:\Program Files\"),
            Is.EqualTo("\"/IC:\\Program Files\\\\\""));
    }
}
