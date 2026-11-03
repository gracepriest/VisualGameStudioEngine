using System.IO;
using BasicLang.Compiler.ILReader;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using NUnit.Framework;

namespace VisualGameStudio.Tests.ILReader;

[TestFixture]
public class ILTypeReaderSpikeTests
{
    [Test]
    public void ReadTypes_PrintsTypeAndMethodInfo()
    {
        var path = @"C:\Users\melvi\AppData\Local\Temp\opencode\il2cpp-spike\hello.exe";
        Assert.That(File.Exists(path), Is.True);
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        var mr = peReader.GetMetadataReader();
        var reader = new ILTypeReader(mr);
        var types = reader.ReadTypes();
        foreach (var t in types)
        {
            TestContext.WriteLine($"Type: {t.FullName} (base: {t.BaseType})");
            foreach (var m in t.Methods)
                TestContext.WriteLine($"  Method: {m.Name} -> {m.ReturnType}({string.Join(",", m.ParameterTypes)})");
            foreach (var f in t.Fields)
                TestContext.WriteLine($"  Field: {f.Name}: {f.Type}");
        }
        Assert.That(types.Count, Is.GreaterThan(0));
    }
}