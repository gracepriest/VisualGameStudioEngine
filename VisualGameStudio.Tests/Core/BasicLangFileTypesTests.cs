using NUnit.Framework;
using VisualGameStudio.Core.Utilities;

namespace VisualGameStudio.Tests.Core;

[TestFixture]
public class BasicLangFileTypesTests
{
    [TestCase(@"C:\proj\Main.bas")]
    [TestCase(@"C:\proj\Main.bl")]
    [TestCase(@"C:\proj\Player.mod")]
    [TestCase(@"C:\proj\Player.cls")]
    [TestCase(@"C:\proj\Player.class")]
    public void IsBasicLangSourceFile_KnownExtensions_ReturnsTrue(string path)
    {
        Assert.That(BasicLangFileTypes.IsBasicLangSourceFile(path), Is.True);
    }

    [TestCase(@"C:\proj\Main.BAS")]
    [TestCase(@"C:\proj\Main.Bl")]
    [TestCase(@"C:\proj\Player.MOD")]
    [TestCase(@"C:\proj\Player.CLS")]
    [TestCase(@"C:\proj\Player.CLASS")]
    public void IsBasicLangSourceFile_IsCaseInsensitive(string path)
    {
        Assert.That(BasicLangFileTypes.IsBasicLangSourceFile(path), Is.True);
    }

    [TestCase(@"C:\proj\Main.cs")]
    [TestCase(@"C:\proj\Main.txt")]
    [TestCase(@"C:\proj\Main.blproj")]
    [TestCase(@"C:\proj\Main.basx")]
    [TestCase(@"C:\proj\basfile")]
    public void IsBasicLangSourceFile_OtherExtensions_ReturnsFalse(string path)
    {
        Assert.That(BasicLangFileTypes.IsBasicLangSourceFile(path), Is.False);
    }

    [TestCase(null)]
    [TestCase("")]
    public void IsBasicLangSourceFile_NullOrEmpty_ReturnsFalse(string? path)
    {
        Assert.That(BasicLangFileTypes.IsBasicLangSourceFile(path), Is.False);
    }

    [Test]
    public void IsBasicLangSourceFile_ExtensionMustFollowDot()
    {
        // A file literally named "bas" (no dot) is not a source file
        Assert.That(BasicLangFileTypes.IsBasicLangSourceFile(@"C:\proj\bas"), Is.False);
    }
}
