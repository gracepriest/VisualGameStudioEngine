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

    // --- GetILOffsetForLine tests ---

    [Test]
    public void GetILOffsetForLine_NoDataLoaded_ReturnsNull()
    {
        using var mapper = new SourceMapper();
        var result = mapper.GetILOffsetForLine("test.bas", 5);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetILOffsetForLine_NonExistentFile_ReturnsNull()
    {
        using var mapper = new SourceMapper();
        // No PDB loaded - any file should return null
        var result = mapper.GetILOffsetForLine("nonexistent.bas", 10);
        Assert.That(result, Is.Null);
    }

    // --- GetSourceLocation tests ---

    [Test]
    public void GetSourceLocation_NoDataLoaded_ReturnsNull()
    {
        using var mapper = new SourceMapper();
        var result = mapper.GetSourceLocation(0x06000001, 0);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetSourceLocation_InvalidMethodToken_ReturnsNull()
    {
        using var mapper = new SourceMapper();
        var result = mapper.GetSourceLocation(999999, 50);
        Assert.That(result, Is.Null);
    }

    // --- GetNextExecutableLine tests ---

    [Test]
    public void GetNextExecutableLine_NoDataLoaded_ReturnsNull()
    {
        using var mapper = new SourceMapper();
        var result = mapper.GetNextExecutableLine("test.bas", 5, 0x06000001);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetNextExecutableLine_InvalidMethodToken_ReturnsNull()
    {
        using var mapper = new SourceMapper();
        var result = mapper.GetNextExecutableLine("test.bas", 1, 999999);
        Assert.That(result, Is.Null);
    }

    // --- GetILRangeForLine tests ---

    [Test]
    public void GetILRangeForLine_NoDataLoaded_ReturnsNull()
    {
        using var mapper = new SourceMapper();
        var result = mapper.GetILRangeForLine(0x06000001, 5);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetILRangeForLine_InvalidMethodToken_ReturnsNull()
    {
        using var mapper = new SourceMapper();
        var result = mapper.GetILRangeForLine(999999, 10);
        Assert.That(result, Is.Null);
    }

    // --- GetMethodLines tests ---

    [Test]
    public void GetMethodLines_NoDataLoaded_ReturnsEmpty()
    {
        using var mapper = new SourceMapper();
        var result = mapper.GetMethodLines(0x06000001);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetMethodLines_InvalidMethodToken_ReturnsEmpty()
    {
        using var mapper = new SourceMapper();
        var result = mapper.GetMethodLines(999999);
        Assert.That(result, Is.Empty);
    }

    // --- LoadPdb edge cases ---

    [Test]
    public void LoadPdb_InvalidFile_ReturnsFalse()
    {
        // Create a temp file that is not a valid PDB
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "this is not a PDB");
            using var mapper = new SourceMapper();
            var result = mapper.LoadPdb(tempFile);
            Assert.That(result, Is.False);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public void LoadPdb_EmptyFile_ReturnsFalse()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            // File already exists and is empty
            using var mapper = new SourceMapper();
            var result = mapper.LoadPdb(tempFile);
            Assert.That(result, Is.False);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // --- Dispose tests ---

    [Test]
    public void Dispose_CalledMultipleTimes_DoesNotThrow()
    {
        var mapper = new SourceMapper();
        Assert.DoesNotThrow(() =>
        {
            mapper.Dispose();
            mapper.Dispose();
        });
    }

    [Test]
    public void Dispose_WithoutLoadingPdb_DoesNotThrow()
    {
        var mapper = new SourceMapper();
        Assert.DoesNotThrow(() => mapper.Dispose());
    }

    // --- FindNearestExecutableLine additional tests ---

    [Test]
    public void FindNearestExecutableLine_UnknownFile_ReturnsInputLine()
    {
        using var mapper = new SourceMapper();
        // With no PDB loaded, any file is unknown
        var result = mapper.FindNearestExecutableLine("unknown_file.bas", 42);
        Assert.That(result, Is.EqualTo(42));
    }

    [Test]
    public void FindNearestExecutableLine_LineZero_ReturnsZero()
    {
        using var mapper = new SourceMapper();
        var result = mapper.FindNearestExecutableLine("test.bas", 0);
        Assert.That(result, Is.EqualTo(0));
    }

    [Test]
    public void FindNearestExecutableLine_NegativeLine_ReturnsNegative()
    {
        using var mapper = new SourceMapper();
        var result = mapper.FindNearestExecutableLine("test.bas", -1);
        Assert.That(result, Is.EqualTo(-1));
    }

    // --- GetSourceDocuments additional tests ---

    [Test]
    public void GetSourceDocuments_CalledTwice_ReturnsSameResult()
    {
        using var mapper = new SourceMapper();
        var docs1 = mapper.GetSourceDocuments();
        var docs2 = mapper.GetSourceDocuments();
        Assert.That(docs1.Count, Is.EqualTo(docs2.Count));
    }
}
