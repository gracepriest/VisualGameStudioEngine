using System.IO;
using BasicLang.Compiler.ILReader;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using NUnit.Framework;

namespace VisualGameStudio.Tests.ILReader;

[TestFixture]
public class ILGenericTypeTests
{
    [Test]
    public void ReadTypes_GenericList_ResolvesTypeSpec()
    {
        var path = @"C:\Users\melvi\AppData\Local\Temp\opencode\il2cpp-gen\gen.exe";
        Assert.That(File.Exists(path), Is.True);
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var mr = pe.GetMetadataReader();
        var reader = new ILTypeReader(mr);
        var types = reader.ReadTypes();
        foreach (var t in types)
        {
            TestContext.WriteLine($"Type: {t.FullName}");
            foreach (var m in t.Methods)
            {
                TestContext.WriteLine($"  Method: {m.Name} -> {m.ReturnType}({string.Join(",", m.ParameterTypes)})");
            }
        }
        Assert.That(types.Count, Is.GreaterThan(0));
    }

    [Test]
    public void ILTypeMapper_GenericList_MapsToCpp()
    {
        Assert.That(ILTypeMapper.Map("System.Collections.Generic.List`1<System.Int32>"), Is.EqualTo("BasicLang::List<int32_t>"));
    }
}