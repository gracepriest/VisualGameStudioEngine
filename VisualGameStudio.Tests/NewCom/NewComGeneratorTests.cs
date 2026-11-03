using System.IO;
using System.Diagnostics;
using NUnit.Framework;

namespace VisualGameStudio.Tests.NewCom;

[TestFixture]
public class NewComGeneratorTests
{
    [Test]
    public void Generator_ProducesValidFiles()
    {
        var genPath = @"C:\Users\melvi\source\repos\VisualGameStudioEngine\NewCom\tools\newcom_gen.py";
        var inputPath = @"C:\Users\melvi\source\repos\VisualGameStudioEngine\NewCom\examples\gns\gns.h";
        var outDir = Path.Combine(Path.GetTempPath(), "newcom-gen-test");
        Directory.CreateDirectory(outDir);

        // Clean output dir
        foreach (var f in Directory.GetFiles(outDir)) File.Delete(f);

        var psi = new ProcessStartInfo("python3", $"\"{genPath}\" \"{inputPath}\" -o \"{outDir}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();

        TestContext.WriteLine("stdout: " + stdout);
        TestContext.WriteLine("stderr: " + stderr);
        Assert.That(p.ExitCode, Is.EqualTo(0), $"Generator failed:\n{stderr}");

        // Verify expected files
        Assert.That(File.Exists(Path.Combine(outDir, "gnsconnection_newcom.h")), Is.True);
        Assert.That(File.Exists(Path.Combine(outDir, "gnsconnection_newcom.cpp")), Is.True);
        Assert.That(File.Exists(Path.Combine(outDir, "gnsconnection.bas")), Is.True);
        Assert.That(File.Exists(Path.Combine(outDir, "gnsserver_newcom.h")), Is.True);
        Assert.That(File.Exists(Path.Combine(outDir, "gnsserver_newcom.cpp")), Is.True);
        Assert.That(File.Exists(Path.Combine(outDir, "gnsserver.bas")), Is.True);

        // Verify generated C++ compiles
        var incDir = @"C:\Users\melvi\source\repos\VisualGameStudioEngine\NewCom";
        var cppPath = Path.Combine(outDir, "gnsconnection_newcom.cpp");
        var vsPath = @"C:\Program Files\Microsoft Visual Studio\2022\Enterprise";
        var vcvars = Path.Combine(vsPath, "VC", "Auxiliary", "Build", "vcvars64.bat");
        var compilePsi = new ProcessStartInfo("cmd.exe",
            $"/c \"\"{vcvars}\" && cl /LD /EHsc /I\"{incDir}\" /Fe:\"{outDir}\\test.dll\" \"{cppPath}\"\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        var cp = Process.Start(compilePsi)!;
        var compileErr = cp.StandardError.ReadToEnd();
        cp.WaitForExit();
        TestContext.WriteLine("compile stderr: " + compileErr);
        Assert.That(cp.ExitCode, Is.EqualTo(0), $"Generated C++ failed to compile:\n{compileErr}");
    }
}
