using System.IO;
using NUnit.Framework;
using BasicLang.Compiler.ProjectSystem;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// <see cref="CppToolchain.ResolveCompilerDirectory"/> — where the build looks for a
/// MinGW toolchain's runtime DLLs to deploy. The load-bearing case is an explicit
/// (off-PATH winlibs) path, which resolves purely from the path with no filesystem or
/// process work; MSVC must resolve to null (system runtime, nothing to deploy).
/// </summary>
[TestFixture]
public class CppToolchainCompilerDirectoryTests
{
    [Test]
    public void Explicit_rooted_compiler_path_resolves_to_its_own_directory()
    {
        var binDir = Path.Combine("C:", "winlibs", "mingw64", "bin");
        var tc = CppToolchain.FromExplicit("gcc", Path.Combine(binDir, "g++.exe"));

        Assert.That(tc, Is.Not.Null);
        Assert.That(tc!.ResolveCompilerDirectory(), Is.EqualTo(binDir));
    }

    [Test]
    public void Explicit_clang_rooted_path_resolves_to_its_own_directory()
    {
        var binDir = Path.Combine("C:", "winlibs", "mingw64", "bin");
        var tc = CppToolchain.FromExplicit("llvm", Path.Combine(binDir, "clang++.exe"));

        Assert.That(tc!.ResolveCompilerDirectory(), Is.EqualTo(binDir));
    }

    [Test]
    public void Msvc_toolchain_resolves_to_null()
    {
        // MSVC links the system UCRT/vcruntime — there is nothing MinGW-style to deploy,
        // so the directory (and hence the deploy) must be null / a no-op.
        var tc = CppToolchain.FromExplicit("msvc", @"C:\VS\VC\Auxiliary\Build\vcvars64.bat");

        Assert.That(tc, Is.Not.Null);
        Assert.That(tc!.ResolveCompilerDirectory(), Is.Null);
    }
}
