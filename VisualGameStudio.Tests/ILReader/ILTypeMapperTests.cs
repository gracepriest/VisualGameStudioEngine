using BasicLang.Compiler.ILReader;
using NUnit.Framework;

namespace VisualGameStudio.Tests.ILReader;

[TestFixture]
public class ILTypeMapperTests
{
    [TestCase("System.Int32", "int32_t")]
    [TestCase("System.String", "std::string")]
    [TestCase("System.Boolean", "bool")]
    [TestCase("System.Void", "void")]
    [TestCase("System.Int32[]", "int32_t*")]
    [TestCase("System.Collections.Generic.List`1<System.Int32>", "BasicLang::List<int32_t>")]
    public void Map_KnownType_ReturnsExpected(string ilType, string expected)
    {
        Assert.That(ILTypeMapper.Map(ilType), Is.EqualTo(expected));
    }

    [Test]
    public void Map_UnknownType_Sanitizes()
    {
        Assert.That(ILTypeMapper.Map("Foo.Bar.Baz"), Is.EqualTo("Baz"));
    }
}