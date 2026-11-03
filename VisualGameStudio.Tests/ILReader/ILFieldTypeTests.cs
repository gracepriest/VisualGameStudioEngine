using System.IO;
using BasicLang.Compiler.ILReader;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using NUnit.Framework;

namespace VisualGameStudio.Tests.ILReader;

[TestFixture]
public class ILFieldTypeTests
{
    [Test]
    public void ReadTypes_FieldTypes_Resolved()
    {
        // Use a C# test assembly or VGS-generated one with fields
        var path = @"C:\Users\melvi\AppData\Local\Temp\opencode\il2cpp-fields\f.exe";
        Assert.That(File.Exists(path), Is.True);
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var mr = pe.GetMetadataReader();
        var reader = new ILTypeReader(mr);
        var types = reader.ReadTypes();
        foreach (var t in types)
        {
            TestContext.WriteLine($"Type: {t.FullName}");
            foreach (var f in t.Fields)
                TestContext.WriteLine($"  Field: {f.Name}: {f.Type}");
        }
        Assert.That(types.Count, Is.GreaterThan(0));
    }
}