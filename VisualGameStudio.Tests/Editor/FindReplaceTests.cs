using NUnit.Framework;
using System.Collections.ObjectModel;

namespace VisualGameStudio.Tests.Editor;

/// <summary>
/// Tests for FindResult model class
/// </summary>
[TestFixture]
public class FindResultTests
{
    [Test]
    public void DefaultFindResult_HasEmptyStrings()
    {
        var result = new FindResult();

        Assert.That(result.FilePath, Is.EqualTo(""));
        Assert.That(result.FileName, Is.EqualTo(""));
        Assert.That(result.PreviewText, Is.EqualTo(""));
    }

    [Test]
    public void DefaultFindResult_HasZeroValues()
    {
        var result = new FindResult();

        Assert.That(result.Line, Is.EqualTo(0));
        Assert.That(result.Column, Is.EqualTo(0));
        Assert.That(result.StartOffset, Is.EqualTo(0));
        Assert.That(result.Length, Is.EqualTo(0));
    }

    [Test]
    public void FindResult_CanSetAllProperties()
    {
        var result = new FindResult
        {
            FilePath = "/path/to/file.bas",
            FileName = "file.bas",
            Line = 42,
            Column = 10,
            StartOffset = 500,
            Length = 5,
            PreviewText = "Dim myVar As Integer"
        };

        Assert.That(result.FilePath, Is.EqualTo("/path/to/file.bas"));
        Assert.That(result.FileName, Is.EqualTo("file.bas"));
        Assert.That(result.Line, Is.EqualTo(42));
        Assert.That(result.Column, Is.EqualTo(10));
        Assert.That(result.StartOffset, Is.EqualTo(500));
        Assert.That(result.Length, Is.EqualTo(5));
        Assert.That(result.PreviewText, Is.EqualTo("Dim myVar As Integer"));
    }

    [Test]
    public void DisplayText_FormatsCorrectly()
    {
        var result = new FindResult
        {
            FileName = "test.bas",
            Line = 10,
            Column = 5,
            PreviewText = "Print Hello"
        };

        Assert.That(result.DisplayText, Is.EqualTo("test.bas(10,5): Print Hello"));
    }

    [Test]
    public void DisplayText_WithEmptyPreviewText()
    {
        var result = new FindResult
        {
            FileName = "test.bas",
            Line = 1,
            Column = 1,
            PreviewText = ""
        };

        Assert.That(result.DisplayText, Is.EqualTo("test.bas(1,1): "));
    }

    [Test]
    public void DisplayText_WithLongPreviewText()
    {
        var result = new FindResult
        {
            FileName = "test.bas",
            Line = 1,
            Column = 1,
            PreviewText = "This is a very long preview text that should still display correctly"
        };

        Assert.That(result.DisplayText, Does.Contain("This is a very long preview text"));
    }

    [Test]
    public void FindResult_LineAndColumn_CanBeZero()
    {
        var result = new FindResult
        {
            FileName = "test.bas",
            Line = 0,
            Column = 0,
            PreviewText = "First line"
        };

        Assert.That(result.DisplayText, Is.EqualTo("test.bas(0,0): First line"));
    }

    [Test]
    public void FindResult_LargeOffsets_WorkCorrectly()
    {
        var result = new FindResult
        {
            StartOffset = 1000000,
            Length = 50000
        };

        Assert.That(result.StartOffset, Is.EqualTo(1000000));
        Assert.That(result.Length, Is.EqualTo(50000));
    }
}

/// <summary>
/// Tests for FindResultGroup model class
/// </summary>
[TestFixture]
public class FindResultGroupTests
{
    [Test]
    public void DefaultGroup_HasEmptyStrings()
    {
        var group = new FindResultGroup();

        Assert.That(group.FilePath, Is.EqualTo(""));
        Assert.That(group.FileName, Is.EqualTo(""));
    }

    [Test]
    public void DefaultGroup_HasZeroMatchCount()
    {
        var group = new FindResultGroup();

        Assert.That(group.MatchCount, Is.EqualTo(0));
    }

    [Test]
    public void DefaultGroup_HasEmptyResults()
    {
        var group = new FindResultGroup();

        Assert.That(group.Results, Is.Empty);
    }

    [Test]
    public void Results_IsObservableCollection()
    {
        var group = new FindResultGroup();

        Assert.That(group.Results, Is.InstanceOf<ObservableCollection<FindResult>>());
    }

    [Test]
    public void FindResultGroup_CanSetAllProperties()
    {
        var group = new FindResultGroup
        {
            FilePath = "/path/to/file.bas",
            FileName = "file.bas",
            MatchCount = 5
        };

        Assert.That(group.FilePath, Is.EqualTo("/path/to/file.bas"));
        Assert.That(group.FileName, Is.EqualTo("file.bas"));
        Assert.That(group.MatchCount, Is.EqualTo(5));
    }

    [Test]
    public void DisplayText_FormatsCorrectly()
    {
        var group = new FindResultGroup
        {
            FileName = "test.bas",
            MatchCount = 3
        };

        Assert.That(group.DisplayText, Is.EqualTo("test.bas (3 matches)"));
    }

    [Test]
    public void DisplayText_SingleMatch()
    {
        var group = new FindResultGroup
        {
            FileName = "test.bas",
            MatchCount = 1
        };

        Assert.That(group.DisplayText, Is.EqualTo("test.bas (1 matches)"));
    }

    [Test]
    public void DisplayText_ZeroMatches()
    {
        var group = new FindResultGroup
        {
            FileName = "test.bas",
            MatchCount = 0
        };

        Assert.That(group.DisplayText, Is.EqualTo("test.bas (0 matches)"));
    }

    [Test]
    public void Results_CanAddItems()
    {
        var group = new FindResultGroup();
        group.Results.Add(new FindResult { Line = 1 });
        group.Results.Add(new FindResult { Line = 2 });
        group.Results.Add(new FindResult { Line = 3 });

        Assert.That(group.Results, Has.Count.EqualTo(3));
    }

    [Test]
    public void Results_CanClearItems()
    {
        var group = new FindResultGroup();
        group.Results.Add(new FindResult());
        group.Results.Add(new FindResult());

        group.Results.Clear();

        Assert.That(group.Results, Is.Empty);
    }

    [Test]
    public void Results_CanRemoveItems()
    {
        var group = new FindResultGroup();
        var result = new FindResult { Line = 42 };
        group.Results.Add(result);

        group.Results.Remove(result);

        Assert.That(group.Results, Is.Empty);
    }

    [Test]
    public void MatchCount_IsIndependentOfResultsCollection()
    {
        var group = new FindResultGroup { MatchCount = 10 };
        group.Results.Add(new FindResult());
        group.Results.Add(new FindResult());

        // MatchCount is independent of the actual Results collection
        Assert.That(group.MatchCount, Is.EqualTo(10));
        Assert.That(group.Results, Has.Count.EqualTo(2));
    }

    [Test]
    public void LargeMatchCount_DisplaysCorrectly()
    {
        var group = new FindResultGroup
        {
            FileName = "large.bas",
            MatchCount = 10000
        };

        Assert.That(group.DisplayText, Is.EqualTo("large.bas (10000 matches)"));
    }
}

/// <summary>
/// Model class representing a single find result
/// </summary>
public class FindResult
{
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; }
    public int StartOffset { get; set; }
    public int Length { get; set; }
    public string PreviewText { get; set; } = "";

    public string DisplayText => $"{FileName}({Line},{Column}): {PreviewText}";
}

/// <summary>
/// Model class representing a group of find results from a single file
/// </summary>
public class FindResultGroup
{
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public int MatchCount { get; set; }
    public ObservableCollection<FindResult> Results { get; } = new();

    public string DisplayText => $"{FileName} ({MatchCount} matches)";
}
