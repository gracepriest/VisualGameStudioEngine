using System.IO;
using BasicLang.Compiler.ILReader;
using NUnit.Framework;

namespace VisualGameStudio.Tests.ILReader;

[TestFixture]
public class ILReaderSpikeTests
{
    [Test]
    public void ReadSummary_PrintsTypesAndMethods()
    {
        var path = @"C:\Users\melvi\AppData\Local\Temp\opencode\il2cpp-spike\hello.exe";
        Assert.That(File.Exists(path), Is.True, "PE file must exist");
        var reader = new ILAssemblyReader(path);
        var summary = reader.ReadSummary();
        TestContext.WriteLine(summary);
        Assert.That(summary, Does.Contain("Combined"));
        Assert.That(summary, Does.Contain("Main"));
    }

    [Test]
    public void ReadMethodBody_Main_ReturnsBytes()
    {
        var path = @"C:\Users\melvi\AppData\Local\Temp\opencode\il2cpp-spike\hello.exe";
        var reader = new ILAssemblyReader(path);
        var body = reader.ReadMethodBody("Main");
        Assert.That(body, Is.Not.Null);
        Assert.That(body.Length, Is.GreaterThan(0));
        TestContext.WriteLine($"Main body: {body.Length} bytes");
    }
}