using System.IO;
using BasicLang.Compiler.ILReader;
using NUnit.Framework;

namespace VisualGameStudio.Tests.ILReader;

[TestFixture]
public class ILCppEmitterControlFlowTests
{
    [Test]
    public void Emit_IfProgram_ProducesCpp()
    {
        var dir = @"C:\Users\melvi\AppData\Local\Temp\opencode\il2cpp-cf";
        Directory.CreateDirectory(dir);
        var bas = "Module M\n  Sub Main()\n    Dim x As Integer = 5\n    If x > 3 Then\n      Console.WriteLine(\"big\")\n    Else\n      Console.WriteLine(\"small\")\n    End If\n  End Sub\nEnd Module";
        File.WriteAllText(Path.Combine(dir, "cf.bas"), bas);
        // Compile to MSIL
        var psi = new System.Diagnostics.ProcessStartInfo("C:\\Users\\melvi\\source\\repos\\VisualGameStudioEngine\\BasicLang\\bin\\Release\\net8.0\\BasicLang.exe", $"\"{dir}\\cf.bas\" --target=msil")
        { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        var p = System.Diagnostics.Process.Start(psi)!;
        p.WaitForExit();
        // Assemble
        var ilasm = new System.Diagnostics.ProcessStartInfo("C:\\Windows\\Microsoft.NET\\Framework64\\v4.0.30319\\ilasm.exe", $"\"{dir}\\cf.il\" /exe /output=\"{dir}\\cf.exe\"")
        { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        var p2 = System.Diagnostics.Process.Start(ilasm)!;
        p2.WaitForExit();
        Assert.That(File.Exists(Path.Combine(dir, "cf.exe")), Is.True);

        var emitter = new ILCppEmitter(Path.Combine(dir, "cf.exe"));
        var cpp = emitter.Emit();
        TestContext.WriteLine("=== Emitted C++ ===");
        TestContext.WriteLine(cpp);
        Assert.That(cpp, Does.Contain("#include <iostream>"));
    }
}