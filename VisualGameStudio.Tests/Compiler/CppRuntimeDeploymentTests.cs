using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler.ProjectSystem;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Pure-filesystem logic behind the MinGW runtime deploy: which runtime DLLs beside a
/// compiler get copied next to a built exe. Fast (no toolchain, no compile) — the real
/// end-to-end deploy is covered by <see cref="CppRuntimeDeployE2ETests"/>.
/// </summary>
[TestFixture]
public class CppRuntimeDeploymentTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-rtdep-unit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        for (var i = 0; i < 3; i++)
        {
            try { Directory.Delete(_dir, recursive: true); return; }
            catch { Thread.Sleep(100); }
        }
    }

    private void Touch(string name) => File.WriteAllText(Path.Combine(_dir, name), "");

    [Test]
    public void Locates_all_mingw_runtime_dlls_present_in_bin_dir()
    {
        foreach (var name in CppRuntimeDeployment.RuntimeDllNames)
            Touch(name);

        var found = CppRuntimeDeployment.LocateRuntimeDlls(_dir)
            .Select(Path.GetFileName)
            .ToList();

        Assert.That(found, Is.EquivalentTo(CppRuntimeDeployment.RuntimeDllNames));
    }

    [Test]
    public void Skips_runtime_dlls_absent_from_bin_dir()
    {
        Touch("libstdc++-6.dll");   // only one of the three present

        var found = CppRuntimeDeployment.LocateRuntimeDlls(_dir)
            .Select(Path.GetFileName)
            .ToList();

        Assert.That(found, Is.EquivalentTo(new[] { "libstdc++-6.dll" }));
    }

    [Test]
    public void Msvc_style_bin_dir_without_runtime_dlls_yields_nothing()
    {
        // An MSVC-ABI toolchain's bin dir holds cl.exe/clang.exe but none of the GNU
        // runtime DLLs — the deploy must be a no-op there.
        Touch("cl.exe");
        Touch("clang.exe");
        Touch("msvcp140.dll");   // MSVC runtime name, NOT in the MinGW list

        Assert.That(CppRuntimeDeployment.LocateRuntimeDlls(_dir), Is.Empty);
    }

    [Test]
    public void Null_or_missing_dir_yields_nothing()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CppRuntimeDeployment.LocateRuntimeDlls(null), Is.Empty);
            Assert.That(CppRuntimeDeployment.LocateRuntimeDlls(""), Is.Empty);
            Assert.That(CppRuntimeDeployment.LocateRuntimeDlls(
                Path.Combine(_dir, "does-not-exist")), Is.Empty);
        });
    }
}
