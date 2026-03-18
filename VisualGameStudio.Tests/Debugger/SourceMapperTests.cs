using NUnit.Framework;
using BasicLang.Debugger;

namespace VisualGameStudio.Tests.Debugger;

[TestFixture]
public class SourceMapperTests
{
    [Test]
    public void LoadPdb_NonExistentPath_ReturnsFalse()
    {
        var mapper = new SourceMapper();
        var result = mapper.LoadPdb("nonexistent.pdb");
        Assert.That(result, Is.False);
    }

    [Test]
    public void FindNearestExecutableLine_NoDataLoaded_ReturnsInputLine()
    {
        var mapper = new SourceMapper();
        var result = mapper.FindNearestExecutableLine("test.bas", 5);
        Assert.That(result, Is.EqualTo(5));
    }

    [Test]
    public void GetSourceDocuments_NoDataLoaded_ReturnsEmpty()
    {
        var mapper = new SourceMapper();
        var docs = mapper.GetSourceDocuments();
        Assert.That(docs, Is.Empty);
    }
}
