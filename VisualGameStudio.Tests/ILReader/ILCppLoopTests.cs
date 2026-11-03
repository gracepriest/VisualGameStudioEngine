using System.IO;
using BasicLang.Compiler.ILReader;
using NUnit.Framework;

namespace VisualGameStudio.Tests.ILReader;

[TestFixture]
public class ILCppLoopTests
{
    [Test]
    public void E2E_Loop_PrintsCorrectResult()
    {
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp-loop");
        Directory.CreateDirectory(dir);
        var bas = "Module M\n  Sub Main()\n    Dim sum As Integer = 0\n    For i As Integer = 1 To 5\n      sum = sum + i\n    Next\n    Console.WriteLine(sum)\n  End Sub\nEnd Module";
        File.WriteAllText(Path.Combine(dir, "loop.bas"), bas);

        var psi = new System.Diagnostics.ProcessStartInfo(
            @"C:\Users\melvi\source\repos\VisualGameStudioEngine\BasicLang\bin\Release\net8.0\BasicLang.exe",
            $"\"{dir}\\loop.bas\" --target=msil")
        { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        var p = System.Diagnostics.Process.Start(psi)!; p.WaitForExit();
        Assert.That(p.ExitCode, Is.EqualTo(0), "MSIL compilation failed");

        var ilasm = new System.Diagnostics.ProcessStartInfo(
            @"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\ilasm.exe",
            $"\"{dir}\\loop.il\" /exe /output=\"{dir}\\loop.exe\"")
        { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        var p2 = System.Diagnostics.Process.Start(ilasm)!; p2.WaitForExit();
        Assert.That(File.Exists(Path.Combine(dir, "loop.exe")), Is.True, "ilasm failed");

        var emitter = new ILCppEmitter(Path.Combine(dir, "loop.exe"));
        var cpp = emitter.Emit();
        var cppPath = Path.Combine(dir, "loop_il2cpp.cpp");
        File.WriteAllText(cppPath, cpp);

        var vsPath = @"C:\Program Files\Microsoft Visual Studio\2022\Enterprise";
        var vcvars = Path.Combine(vsPath, "VC", "Auxiliary", "Build", "vcvars64.bat");
        var shimDir = @"C:\Users\melvi\source\repos\VisualGameStudioEngine\BasicLang\Compiler\CodeGen\CPlusPlus";
        var binPath = Path.Combine(dir, "loop_il2cpp.exe");
        var compilePsi = new System.Diagnostics.ProcessStartInfo("cmd.exe",
            $"/c \"\"{vcvars}\" && cl /EHsc /I\"{shimDir}\" /Fe:\"{binPath}\" \"{cppPath}\"\"")
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        var cp = System.Diagnostics.Process.Start(compilePsi)!;
        var compileErr = cp.StandardError.ReadToEnd();
        cp.WaitForExit();
        Assert.That(cp.ExitCode, Is.EqualTo(0), $"C++ compilation failed:\n{compileErr}");

        var runPsi = new System.Diagnostics.ProcessStartInfo(binPath)
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        var rp = System.Diagnostics.Process.Start(runPsi)!;
        var runOut = rp.StandardOutput.ReadToEnd();
        rp.WaitForExit();
        TestContext.WriteLine("Output: " + runOut.Trim());
        Assert.That(runOut.Trim(), Does.Contain("15"));
    }
}