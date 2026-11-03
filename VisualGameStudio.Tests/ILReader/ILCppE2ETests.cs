using System.IO;
using BasicLang.Compiler.ILReader;
using NUnit.Framework;

namespace VisualGameStudio.Tests.ILReader;

[TestFixture]
public class ILCppE2ETests
{
    [Test]
    public void FullPipeline_EmitsAndCompiles()
    {
        var dir = @"C:\Users\melvi\AppData\Local\Temp\opencode\il2cpp-e2e";
        var pePath = Path.Combine(dir, "e.exe");
        Assert.That(File.Exists(pePath), Is.True);

        var emitter = new ILCppEmitter(pePath);
        var cpp = emitter.Emit();
        var cppPath = Path.Combine(dir, "e_il2cpp.cpp");
        File.WriteAllText(cppPath, cpp);
        TestContext.WriteLine("=== Emitted C++ ===");
        TestContext.WriteLine(cpp);

        // Compile with MSVC
        var vsPath = @"C:\Program Files\Microsoft Visual Studio\2022\Enterprise";
        var vcvars = Path.Combine(vsPath, "VC", "Auxiliary", "Build", "vcvars64.bat");
        var shimDir = @"C:\Users\melvi\source\repos\VisualGameStudioEngine\BasicLang\Compiler\CodeGen\CPlusPlus";
        var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe",
            $"/c \"\"{vcvars}\" && cl /EHsc /I\"{shimDir}\" /Fe:\"{dir}\\e_il2cpp.exe\" \"{cppPath}\"\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        var p = System.Diagnostics.Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        TestContext.WriteLine("=== Compile stdout ===");
        TestContext.WriteLine(stdout);
        TestContext.WriteLine("=== Compile stderr ===");
        TestContext.WriteLine(stderr);

        Assert.That(p.ExitCode, Is.EqualTo(0), $"Compilation failed:\n{stderr}");
        Assert.That(File.Exists(Path.Combine(dir, "e_il2cpp.exe")), Is.True);

        // Run
        var runPsi = new System.Diagnostics.ProcessStartInfo(Path.Combine(dir, "e_il2cpp.exe"))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        var rp = System.Diagnostics.Process.Start(runPsi)!;
        var runOut = rp.StandardOutput.ReadToEnd();
        var runErr = rp.StandardError.ReadToEnd();
        rp.WaitForExit();
        TestContext.WriteLine("=== Run output ===");
        TestContext.WriteLine(runOut);
        TestContext.WriteLine(runErr);

        Assert.That(runOut, Does.Contain("Hello from IL2CPP!"));
    }
}