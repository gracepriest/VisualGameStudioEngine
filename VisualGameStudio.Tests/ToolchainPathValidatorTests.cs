using NUnit.Framework;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests;

[TestFixture]
public class ToolchainPathValidatorTests
{
    // fake filesystem: only these paths "exist"
    private static System.Func<string, bool> Files(params string[] present) =>
        p => System.Array.IndexOf(present, p) >= 0;

    [Test]
    public void Empty_Path_Is_Empty()
    {
        var r = ToolchainPathValidator.Validate("llvm", ToolchainSlotKind.Compiler, "  ");
        Assert.That(r.Status, Is.EqualTo(ToolchainPathStatus.Empty));
        Assert.That(r.Usable, Is.False);
    }

    [Test]
    public void Missing_Compiler_Is_Invalid()
    {
        var r = ToolchainPathValidator.Validate("llvm", ToolchainSlotKind.Compiler,
            @"C:\nope\clang++.exe", fileExists: Files());
        Assert.That(r.Status, Is.EqualTo(ToolchainPathStatus.Invalid));
    }

    [Test]
    public void Recognized_Clang_That_Smokes_Is_Valid_With_Version()
    {
        var r = ToolchainPathValidator.Validate("llvm", ToolchainSlotKind.Compiler,
            @"C:\llvm\bin\clang++.exe",
            fileExists: Files(@"C:\llvm\bin\clang++.exe"),
            versionProbe: _ => new VersionProbeResult(Ran: true, Ok: true, Version: "clang version 18.1.8"));
        Assert.That(r.Status, Is.EqualTo(ToolchainPathStatus.Valid));
        Assert.That(r.DetectedVersion, Does.Contain("18.1.8"));
        Assert.That(r.Usable, Is.True);
    }

    [Test]
    public void Existing_But_Unrecognized_Basename_Is_Warning_And_Never_Executes()
    {
        var executed = false;
        var r = ToolchainPathValidator.Validate("llvm", ToolchainSlotKind.Compiler,
            @"C:\tools\my-wrapper.exe",
            fileExists: Files(@"C:\tools\my-wrapper.exe"),
            versionProbe: _ => { executed = true; return new VersionProbeResult(true, true, "x"); });
        Assert.That(r.Status, Is.EqualTo(ToolchainPathStatus.Warning));
        Assert.That(executed, Is.False, "must not run --version on an unrecognized binary");
        Assert.That(r.Usable, Is.True);
    }

    [Test]
    public void Msvc_Compiler_Direct_Vcvars_Is_Valid()
    {
        var bat = @"C:\VS\VC\Auxiliary\Build\vcvars64.bat";
        var r = ToolchainPathValidator.Validate("msvc", ToolchainSlotKind.Compiler, bat,
            fileExists: Files(bat));
        Assert.That(r.Status, Is.EqualTo(ToolchainPathStatus.Valid));
        Assert.That(r.ResolvedPath, Is.EqualTo(bat));
    }

    [Test]
    public void Msvc_Compiler_Install_Dir_Derives_Vcvars()
    {
        var dir = @"C:\VS";
        var bat = @"C:\VS\VC\Auxiliary\Build\vcvars64.bat";
        var r = ToolchainPathValidator.Validate("msvc", ToolchainSlotKind.Compiler, dir,
            fileExists: Files(bat), dirExists: Files(dir));
        Assert.That(r.Status, Is.EqualTo(ToolchainPathStatus.Valid));
        Assert.That(r.ResolvedPath, Is.EqualTo(bat));
    }

    [Test]
    public void Msvc_Compiler_Pointed_At_ClExe_Is_Invalid()  // the silent trap
    {
        var cl = @"C:\VS\bin\cl.exe";
        var r = ToolchainPathValidator.Validate("msvc", ToolchainSlotKind.Compiler, cl,
            fileExists: Files(cl));
        Assert.That(r.Status, Is.EqualTo(ToolchainPathStatus.Invalid));
    }

    [Test]
    public void Msvc_Debugger_Valid_LldbDap_Is_Warning_With_Pdb_Advisory()
    {
        var p = @"C:\llvm\bin\lldb-dap.exe";
        var r = ToolchainPathValidator.Validate("msvc", ToolchainSlotKind.Debugger, p,
            fileExists: Files(p),
            versionProbe: _ => new VersionProbeResult(true, true, "lldb-dap 22"));
        Assert.That(r.Status, Is.EqualTo(ToolchainPathStatus.Warning));
        Assert.That(r.Message, Does.Contain("PDB"));
        Assert.That(r.Usable, Is.True);
    }
}
