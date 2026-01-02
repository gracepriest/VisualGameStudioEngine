using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

[TestFixture]
public class FindReplaceServiceTests
{
    private FindReplaceService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _service = new FindReplaceService();
    }

    #region FindInDocument Tests

    [Test]
    public void FindInDocument_EmptyContent_ReturnsEmpty()
    {
        var result = _service.FindInDocument("", "test");
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void FindInDocument_EmptyPattern_ReturnsEmpty()
    {
        var result = _service.FindInDocument("test content", "");
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void FindInDocument_NoMatch_ReturnsEmpty()
    {
        var result = _service.FindInDocument("hello world", "foo");
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void FindInDocument_SingleMatch_ReturnsOne()
    {
        var result = _service.FindInDocument("hello world", "world");

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].MatchedText, Is.EqualTo("world"));
        Assert.That(result[0].StartOffset, Is.EqualTo(6));
        Assert.That(result[0].Length, Is.EqualTo(5));
    }

    [Test]
    public void FindInDocument_MultipleMatches_ReturnsAll()
    {
        var result = _service.FindInDocument("the cat and the dog", "the");

        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result[0].StartOffset, Is.EqualTo(0));
        Assert.That(result[1].StartOffset, Is.EqualTo(12));
    }

    [Test]
    public void FindInDocument_CaseInsensitive_ByDefault()
    {
        var result = _service.FindInDocument("Hello HELLO hello", "hello");

        Assert.That(result, Has.Count.EqualTo(3));
    }

    [Test]
    public void FindInDocument_CaseSensitive_WhenEnabled()
    {
        _service.Options.CaseSensitive = true;

        var result = _service.FindInDocument("Hello HELLO hello", "hello");

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].StartOffset, Is.EqualTo(12));
    }

    [Test]
    public void FindInDocument_WholeWord_MatchesOnlyWholeWords()
    {
        _service.Options.WholeWord = true;

        var result = _service.FindInDocument("testing test tested", "test");

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].StartOffset, Is.EqualTo(8));
    }

    [Test]
    public void FindInDocument_Regex_WorksCorrectly()
    {
        _service.Options.UseRegex = true;

        var result = _service.FindInDocument("cat bat hat mat", @"\b.at\b");

        Assert.That(result, Has.Count.EqualTo(4));
    }

    [Test]
    public void FindInDocument_TracksLineNumbers()
    {
        var content = "line1\nline2\nline3";

        var result = _service.FindInDocument(content, "line2");

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Line, Is.EqualTo(2));
    }

    [Test]
    public void FindInDocument_TracksColumnNumbers()
    {
        var content = "hello world";

        var result = _service.FindInDocument(content, "world");

        Assert.That(result[0].Column, Is.EqualTo(7));
    }

    [Test]
    public void FindInDocument_IncludesLineText()
    {
        var content = "first line\nsecond line\nthird line";

        var result = _service.FindInDocument(content, "second");

        Assert.That(result[0].LineText, Is.EqualTo("second line"));
    }

    [Test]
    public void FindInDocument_Regex_CapturesGroups()
    {
        _service.Options.UseRegex = true;

        var result = _service.FindInDocument("test 123", @"(\w+)\s+(\d+)");

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Groups, Has.Count.EqualTo(2));
        Assert.That(result[0].Groups[0], Is.EqualTo("test"));
        Assert.That(result[0].Groups[1], Is.EqualTo("123"));
    }

    #endregion

    #region FindNext Tests

    [Test]
    public void FindNext_FindsFromOffset()
    {
        var content = "test test test";

        var result = _service.FindNext(content, "test", 5);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StartOffset, Is.EqualTo(5));
    }

    [Test]
    public void FindNext_WrapsAround_WhenEnabled()
    {
        _service.Options.WrapAround = true;
        var content = "test test test";

        var result = _service.FindNext(content, "test", 11);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StartOffset, Is.EqualTo(0));
    }

    [Test]
    public void FindNext_NoWrapAround_WhenDisabled()
    {
        _service.Options.WrapAround = false;
        var content = "test test test";

        var result = _service.FindNext(content, "test", 11);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void FindNext_ReturnsNull_WhenNoMatch()
    {
        var result = _service.FindNext("hello world", "foo", 0);

        Assert.That(result, Is.Null);
    }

    #endregion

    #region FindPrevious Tests

    [Test]
    public void FindPrevious_FindsBeforeOffset()
    {
        var content = "test test test";

        var result = _service.FindPrevious(content, "test", 10);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StartOffset, Is.EqualTo(5));
    }

    [Test]
    public void FindPrevious_WrapsAround_WhenEnabled()
    {
        _service.Options.WrapAround = true;
        var content = "aaa bbb aaa";

        // Search for "aaa" before offset 0 (beginning of doc)
        // No match before 0, so should wrap and find the last "aaa" at offset 8
        var result = _service.FindPrevious(content, "aaa", 0);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StartOffset, Is.EqualTo(8));
    }

    [Test]
    public void FindPrevious_ReturnsNull_WhenNoMatch()
    {
        var result = _service.FindPrevious("hello world", "foo", 5);

        Assert.That(result, Is.Null);
    }

    #endregion

    #region ReplaceOne Tests

    [Test]
    public void ReplaceOne_ReplacesMatch()
    {
        var content = "hello world";
        var match = new FindMatch { StartOffset = 6, Length = 5, MatchedText = "world" };

        var result = _service.ReplaceOne(content, match, "universe");

        Assert.That(result, Is.EqualTo("hello universe"));
    }

    [Test]
    public void ReplaceOne_PreservesCase_WhenEnabled()
    {
        _service.Options.PreserveCase = true;
        var content = "HELLO world";
        var match = new FindMatch { StartOffset = 0, Length = 5, MatchedText = "HELLO" };

        var result = _service.ReplaceOne(content, match, "goodbye");

        Assert.That(result, Is.EqualTo("GOODBYE world"));
    }

    [Test]
    public void ReplaceOne_PreservesTitleCase()
    {
        _service.Options.PreserveCase = true;
        var content = "Hello world";
        var match = new FindMatch { StartOffset = 0, Length = 5, MatchedText = "Hello" };

        var result = _service.ReplaceOne(content, match, "goodbye");

        Assert.That(result, Is.EqualTo("Goodbye world"));
    }

    #endregion

    #region ReplaceAll Tests

    [Test]
    public void ReplaceAll_ReplacesAllOccurrences()
    {
        var result = _service.ReplaceAll("cat cat cat", "cat", "dog");

        Assert.That(result.Content, Is.EqualTo("dog dog dog"));
        Assert.That(result.ReplacementCount, Is.EqualTo(3));
    }

    [Test]
    public void ReplaceAll_NoMatch_ReturnsOriginal()
    {
        var result = _service.ReplaceAll("hello world", "foo", "bar");

        Assert.That(result.Content, Is.EqualTo("hello world"));
        Assert.That(result.ReplacementCount, Is.EqualTo(0));
    }

    [Test]
    public void ReplaceAll_TracksReplacements()
    {
        var result = _service.ReplaceAll("a b a", "a", "x");

        Assert.That(result.Replacements, Has.Count.EqualTo(2));
        Assert.That(result.Replacements[0].OriginalText, Is.EqualTo("a"));
        Assert.That(result.Replacements[0].ReplacementText, Is.EqualTo("x"));
    }

    [Test]
    public void ReplaceAll_Regex_WorksCorrectly()
    {
        _service.Options.UseRegex = true;

        var result = _service.ReplaceAll("test123 test456", @"\d+", "000");

        Assert.That(result.Content, Is.EqualTo("test000 test000"));
    }

    #endregion

    #region FindInFilesAsync Tests

    [Test]
    public async Task FindInFilesAsync_EmptyPattern_ReturnsEmpty()
    {
        var result = await _service.FindInFilesAsync(new[] { "file.txt" }, "");

        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task FindInFilesAsync_FiresEvents()
    {
        var startedFired = false;
        var completedFired = false;

        _service.SearchStarted += (s, e) => startedFired = true;
        _service.SearchCompleted += (s, e) => completedFired = true;

        await _service.FindInFilesAsync(Array.Empty<string>(), "test");

        Assert.That(startedFired, Is.True);
        Assert.That(completedFired, Is.True);
    }

    #endregion

    #region Pattern Validation Tests

    [Test]
    public void IsValidPattern_ValidLiteral_ReturnsTrue()
    {
        Assert.That(_service.IsValidPattern("hello"), Is.True);
    }

    [Test]
    public void IsValidPattern_ValidRegex_ReturnsTrue()
    {
        _service.Options.UseRegex = true;

        Assert.That(_service.IsValidPattern(@"\d+"), Is.True);
    }

    [Test]
    public void IsValidPattern_InvalidRegex_ReturnsFalse()
    {
        _service.Options.UseRegex = true;

        Assert.That(_service.IsValidPattern("[invalid"), Is.False);
    }

    [Test]
    public void IsValidPattern_EmptyPattern_ReturnsFalse()
    {
        Assert.That(_service.IsValidPattern(""), Is.False);
    }

    [Test]
    public void GetPatternError_ValidPattern_ReturnsNull()
    {
        Assert.That(_service.GetPatternError("hello"), Is.Null);
    }

    [Test]
    public void GetPatternError_InvalidRegex_ReturnsMessage()
    {
        _service.Options.UseRegex = true;

        var error = _service.GetPatternError("[invalid");

        Assert.That(error, Is.Not.Null);
        Assert.That(error!.Length, Is.GreaterThan(0));
    }

    #endregion
}

#region Model Tests

[TestFixture]
public class FindReplaceOptionsTests
{
    [Test]
    public void DefaultOptions_HasExpectedDefaults()
    {
        var options = new FindReplaceOptions();

        Assert.That(options.CaseSensitive, Is.False);
        Assert.That(options.WholeWord, Is.False);
        Assert.That(options.UseRegex, Is.False);
        Assert.That(options.IncludeSubdirectories, Is.True);
        Assert.That(options.WrapAround, Is.True);
        Assert.That(options.PreserveCase, Is.False);
        Assert.That(options.Scope, Is.EqualTo(SearchScope.CurrentDocument));
        Assert.That(options.IncludePatterns, Is.Null);
        Assert.That(options.ExcludePatterns, Is.Null);
    }

    [Test]
    public void Options_CanSetAllProperties()
    {
        var options = new FindReplaceOptions
        {
            CaseSensitive = true,
            WholeWord = true,
            UseRegex = true,
            IncludeSubdirectories = false,
            WrapAround = false,
            PreserveCase = true,
            Scope = SearchScope.EntireSolution,
            IncludePatterns = "*.bl",
            ExcludePatterns = "*.bak"
        };

        Assert.That(options.CaseSensitive, Is.True);
        Assert.That(options.WholeWord, Is.True);
        Assert.That(options.UseRegex, Is.True);
        Assert.That(options.IncludeSubdirectories, Is.False);
        Assert.That(options.WrapAround, Is.False);
        Assert.That(options.PreserveCase, Is.True);
        Assert.That(options.Scope, Is.EqualTo(SearchScope.EntireSolution));
        Assert.That(options.IncludePatterns, Is.EqualTo("*.bl"));
        Assert.That(options.ExcludePatterns, Is.EqualTo("*.bak"));
    }
}

[TestFixture]
public class FindMatchTests
{
    [Test]
    public void DefaultMatch_HasExpectedDefaults()
    {
        var match = new FindMatch();

        Assert.That(match.StartOffset, Is.EqualTo(0));
        Assert.That(match.Length, Is.EqualTo(0));
        Assert.That(match.MatchedText, Is.EqualTo(""));
        Assert.That(match.Line, Is.EqualTo(0));
        Assert.That(match.Column, Is.EqualTo(0));
        Assert.That(match.LineText, Is.EqualTo(""));
        Assert.That(match.Groups, Is.Empty);
    }

    [Test]
    public void EndOffset_CalculatesCorrectly()
    {
        var match = new FindMatch { StartOffset = 10, Length = 5 };

        Assert.That(match.EndOffset, Is.EqualTo(15));
    }
}

[TestFixture]
public class ReplaceAllResultTests
{
    [Test]
    public void DefaultResult_HasExpectedDefaults()
    {
        var result = new ReplaceAllResult();

        Assert.That(result.Content, Is.EqualTo(""));
        Assert.That(result.ReplacementCount, Is.EqualTo(0));
        Assert.That(result.Replacements, Is.Empty);
    }
}

[TestFixture]
public class FileSearchResultTests
{
    [Test]
    public void DefaultResult_HasExpectedDefaults()
    {
        var result = new FileSearchResult();

        Assert.That(result.FilePath, Is.EqualTo(""));
        Assert.That(result.FileName, Is.EqualTo(""));
        Assert.That(result.Matches, Is.Empty);
        Assert.That(result.MatchCount, Is.EqualTo(0));
    }

    [Test]
    public void MatchCount_ReflectsMatchesCount()
    {
        var result = new FileSearchResult
        {
            Matches = new List<FindMatch> { new(), new(), new() }
        };

        Assert.That(result.MatchCount, Is.EqualTo(3));
    }
}

[TestFixture]
public class SearchScopeEnumTests
{
    [Test]
    public void AllScopeValues_AreDefined()
    {
        Assert.That(Enum.IsDefined(typeof(SearchScope), SearchScope.CurrentDocument), Is.True);
        Assert.That(Enum.IsDefined(typeof(SearchScope), SearchScope.Selection), Is.True);
        Assert.That(Enum.IsDefined(typeof(SearchScope), SearchScope.OpenDocuments), Is.True);
        Assert.That(Enum.IsDefined(typeof(SearchScope), SearchScope.CurrentProject), Is.True);
        Assert.That(Enum.IsDefined(typeof(SearchScope), SearchScope.EntireSolution), Is.True);
        Assert.That(Enum.IsDefined(typeof(SearchScope), SearchScope.CustomFolders), Is.True);
    }
}

[TestFixture]
public class FindReplaceEventArgsTests
{
    [Test]
    public void Constructor_SetsProperties()
    {
        var args = new FindReplaceEventArgs("test", 5);

        Assert.That(args.Pattern, Is.EqualTo("test"));
        Assert.That(args.TotalMatches, Is.EqualTo(5));
    }
}

[TestFixture]
public class FindReplaceProgressEventArgsTests
{
    [Test]
    public void Constructor_SetsProperties()
    {
        var args = new FindReplaceProgressEventArgs("file.txt", 5, 10, 20);

        Assert.That(args.CurrentFile, Is.EqualTo("file.txt"));
        Assert.That(args.FilesSearched, Is.EqualTo(5));
        Assert.That(args.TotalFiles, Is.EqualTo(10));
        Assert.That(args.MatchesFound, Is.EqualTo(20));
    }

    [Test]
    public void PercentComplete_CalculatesCorrectly()
    {
        var args = new FindReplaceProgressEventArgs("file.txt", 5, 10, 0);

        Assert.That(args.PercentComplete, Is.EqualTo(50));
    }

    [Test]
    public void PercentComplete_HandlesZeroTotal()
    {
        var args = new FindReplaceProgressEventArgs("file.txt", 0, 0, 0);

        Assert.That(args.PercentComplete, Is.EqualTo(0));
    }
}

#endregion
