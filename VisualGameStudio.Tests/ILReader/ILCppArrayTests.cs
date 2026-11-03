using System.IO;
using BasicLang.Compiler.ILReader;
using NUnit.Framework;

namespace VisualGameStudio.Tests.ILReader;

[TestFixture]
public class ILCppArrayTests
{
    [Test]
    public void Emit_ArrayProgram_ProducesCpp()
    {
        var dir = @"C:\Users\melvi\AppData\Local\Temp\opencode\il2cpp-arr";
        Directory.CreateDirectory(dir);
        var bas = "Module M\n  Sub Main()\n    Dim a(2) As Integer\n    a(0) = 10\n    a(1) = 20\n    Console.WriteLine(a(0) + a(1))\n  End Sub\nEnd Module";
        File.WriteAllText(Path.Combine(dir, "arr.bas"), bas);
        var psi = new System.Diagnostics.ProcessStartInfo("C:\\Users\\melvi\\source\\repos\\VisualGameStudioEngine\\BasicLang\\bin\\Release\\net8.0\\BasicLang.exe", $"\"{dir}\\arr.bas\" --target=msil")
        { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        var p = System.Diagnostics.Process.Start(psi)!; p.WaitForExit();
        var ilasm = new System.Diagnostics.ProcessStartInfo("C:\\Windows\\Microsoft.NET\\Framework64\\v4.0.30319\\ilasm.exe", $"\"{dir}\\arr.il\" /exe /output=\"{dir}\\arr.exe\"")
        { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        var p2 = System.Diagnostics.Process.Start(ilasm)!; p2.WaitForExit();
        Assert.That(File.Exists(Path.Combine(dir, "arr.exe")), Is.True);

        var emitter = new ILCppEmitter(Path.Combine(dir, "arr.exe"));
        var cpp = emitter.Emit();
        TestContext.WriteLine("=== Emitted C++ ===");
        TestContext.WriteLine(cpp);
        Assert.That(cpp, Does.Contain("#include <iostream>"));
    }
}