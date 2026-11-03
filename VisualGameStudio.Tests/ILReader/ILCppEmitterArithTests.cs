using System.IO;
using BasicLang.Compiler.ILReader;
using NUnit.Framework;

namespace VisualGameStudio.Tests.ILReader;

[TestFixture]
public class ILCppEmitterArithTests
{
    [Test]
    public void Emit_ArithProgram_ProducesCpp()
    {
        var path = @"C:\Users\melvi\AppData\Local\Temp\opencode\il2cpp-spike2\arith.exe";
        Assert.That(File.Exists(path), Is.True);
        var emitter = new ILCppEmitter(path);
        var cpp = emitter.Emit();
        TestContext.WriteLine("=== Emitted C++ ===");
        TestContext.WriteLine(cpp);
        Assert.That(cpp, Does.Contain("#include <iostream>"));
    }
}