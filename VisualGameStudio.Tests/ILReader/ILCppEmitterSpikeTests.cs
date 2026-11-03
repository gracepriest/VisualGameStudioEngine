using System.IO;
using BasicLang.Compiler.ILReader;
using NUnit.Framework;

namespace VisualGameStudio.Tests.ILReader;

[TestFixture]
public class ILCppEmitterSpikeTests
{
    [Test]
    public void Emit_ProducesCppWithHelloWorld()
    {
        var path = @"C:\Users\melvi\AppData\Local\Temp\opencode\il2cpp-spike\hello.exe";
        Assert.That(File.Exists(path), Is.True);
        var emitter = new ILCppEmitter(path);
        var cpp = emitter.Emit();
        TestContext.WriteLine("=== Emitted C++ ===");
        TestContext.WriteLine(cpp);
        Assert.That(cpp, Does.Contain("#include <iostream>"));
        Assert.That(cpp, Does.Contain("Hello, IL2CPP!"));
        Assert.That(cpp, Does.Contain("std::cout"));
    }
}