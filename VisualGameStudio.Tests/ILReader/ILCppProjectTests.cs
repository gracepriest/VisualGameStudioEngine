using System.IO;
using BasicLang.Compiler.ILReader;
using NUnit.Framework;

namespace VisualGameStudio.Tests.ILReader;

[TestFixture]
public class ILCppProjectTests
{
    [Test]
    public void E2E_Project_PrintsCorrectResult()
    {
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp-proj3");
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(Path.Combine(dir, "src"));
        var proj = "<Project>\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net8.0</TargetFramework>\n    <AssemblyName>projtest3</AssemblyName>\n    <Backend>IL2CPP</Backend>\n  </PropertyGroup>\n</Project>";
        File.WriteAllText(Path.Combine(dir, "projtest3.blproj"), proj);
        File.WriteAllText(Path.Combine(dir, "src", "main.bas"), "Module M\n  Sub Main()\n    Console.WriteLine(\"Hello from project IL2CPP!\")\n  End Sub\nEnd Module");

        var exe = @"C:\Users\melvi\source\repos\VisualGameStudioEngine\BasicLang\bin\Release\net8.0\BasicLang.exe";
        var psi = new System.Diagnostics.ProcessStartInfo(exe, $"\"{dir}\\projtest3.blproj\"")
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        var p = System.Diagnostics.Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        TestContext.WriteLine(stdout);
        TestContext.WriteLine(stderr);
        Assert.That(p.ExitCode, Is.EqualTo(0), $"Build failed:\n{stderr}");

        var binPath = Path.Combine(dir, "bin", "Debug", "net8.0", "projtest3_il2cpp.exe");
        Assert.That(File.Exists(binPath), Is.True);

        var runPsi = new System.Diagnostics.ProcessStartInfo(binPath)
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        var rp = System.Diagnostics.Process.Start(runPsi)!;
        var runOut = rp.StandardOutput.ReadToEnd();
        rp.WaitForExit();
        TestContext.WriteLine("Output: " + runOut.Trim());
        Assert.That(runOut.Trim(), Does.Contain("Hello from project IL2CPP!"));
    }
}
