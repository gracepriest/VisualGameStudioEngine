using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

[TestFixture]
public class TextMateServiceTests
{
    private TextMateService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _service = new TextMateService();
    }

    #region Initial State Tests

    [Test]
    public void Grammars_Initially_IsEmpty()
    {
        Assert.That(_service.Grammars, Is.Empty);
    }

    [Test]
    public void Themes_Initially_IsEmpty()
    {
        Assert.That(_service.Themes, Is.Empty);
    }

    [Test]
    public void CurrentTheme_Initially_IsNull()
    {
        Assert.That(_service.CurrentTheme, Is.Null);
    }

    #endregion

    #region LoadGrammarAsync Tests

    [Test]
    public async Task LoadGrammarAsync_NonExistentFile_ReturnsNull()
    {
        var result = await _service.LoadGrammarAsync("nonexistent.json");
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task LoadGrammarAsync_InvalidExtension_ReturnsNull()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var result = await _service.LoadGrammarAsync(tempFile);
            Assert.That(result, Is.Null);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    #endregion

    #region LoadGrammarFromJson Tests

    [Test]
    public void LoadGrammarFromJson_ValidJson_ReturnsGrammar()
    {
        var json = """
        {
            "scopeName": "source.test",
            "name": "Test Language",
            "fileTypes": ["test", "tst"],
            "patterns": [
                { "match": "\\b(if|else|while)\\b", "name": "keyword.control" }
            ]
        }
        """;

        var grammar = _service.LoadGrammarFromJson(json, "source.test");

        Assert.That(grammar, Is.Not.Null);
        Assert.That(grammar!.ScopeName, Is.EqualTo("source.test"));
        Assert.That(grammar.Name, Is.EqualTo("Test Language"));
        Assert.That(grammar.FileTypes, Has.Count.EqualTo(2));
        Assert.That(grammar.Patterns, Has.Count.EqualTo(1));
    }

    [Test]
    public void LoadGrammarFromJson_InvalidJson_ReturnsNull()
    {
        var result = _service.LoadGrammarFromJson("not valid json", "test");
        Assert.That(result, Is.Null);
    }

    [Test]
    public void LoadGrammarFromJson_EmptyJson_ReturnsNull()
    {
        var result = _service.LoadGrammarFromJson("", "test");
        Assert.That(result, Is.Null);
    }

    [Test]
    public void LoadGrammarFromJson_AddsToGrammars()
    {
        var json = """{ "scopeName": "source.added", "patterns": [] }""";
        _service.LoadGrammarFromJson(json, "source.added");

        Assert.That(_service.Grammars, Does.ContainKey("source.added"));
    }

    [Test]
    public void LoadGrammarFromJson_RaisesGrammarLoadedEvent()
    {
        var eventRaised = false;
        _service.GrammarLoaded += (s, e) => eventRaised = true;

        var json = """{ "scopeName": "source.event", "patterns": [] }""";
        _service.LoadGrammarFromJson(json, "source.event");

        Assert.That(eventRaised, Is.True);
    }

    #endregion

    #region RegisterExtension Tests

    [Test]
    public void RegisterExtension_AddsMapping()
    {
        var json = """{ "scopeName": "source.ext", "patterns": [] }""";
        _service.LoadGrammarFromJson(json, "source.ext");

        _service.RegisterExtension(".ext", "source.ext");

        var grammar = _service.GetGrammarForExtension(".ext");
        Assert.That(grammar, Is.Not.Null);
        Assert.That(grammar!.ScopeName, Is.EqualTo("source.ext"));
    }

    [Test]
    public void GetGrammarForExtension_UnknownExtension_ReturnsNull()
    {
        var result = _service.GetGrammarForExtension(".unknown");
        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetGrammarForExtension_NormalizesExtension()
    {
        var json = """{ "scopeName": "source.norm", "patterns": [] }""";
        _service.LoadGrammarFromJson(json, "source.norm");
        _service.RegisterExtension(".norm", "source.norm");

        // Should work without leading dot
        var grammar = _service.GetGrammarForExtension("norm");
        Assert.That(grammar, Is.Not.Null);
    }

    #endregion

    #region LoadThemeAsync Tests

    [Test]
    public async Task LoadThemeAsync_NonExistentFile_ReturnsNull()
    {
        var result = await _service.LoadThemeAsync("nonexistent.json");
        Assert.That(result, Is.Null);
    }

    #endregion

    #region LoadThemeFromJson Tests

    [Test]
    public void LoadThemeFromJson_ValidJson_ReturnsTheme()
    {
        var json = """
        {
            "name": "Test Theme",
            "type": "dark",
            "colors": {
                "editor.background": "#1e1e1e",
                "editor.foreground": "#d4d4d4"
            },
            "tokenColors": [
                {
                    "name": "Keywords",
                    "scope": "keyword",
                    "settings": { "foreground": "#569cd6" }
                }
            ]
        }
        """;

        var theme = _service.LoadThemeFromJson(json, "Test Theme");

        Assert.That(theme, Is.Not.Null);
        Assert.That(theme!.Name, Is.EqualTo("Test Theme"));
        Assert.That(theme.Type, Is.EqualTo("dark"));
        Assert.That(theme.Colors, Has.Count.EqualTo(2));
        Assert.That(theme.TokenColors, Has.Count.EqualTo(1));
    }

    [Test]
    public void LoadThemeFromJson_InvalidJson_ReturnsNull()
    {
        var result = _service.LoadThemeFromJson("not valid json", "test");
        Assert.That(result, Is.Null);
    }

    [Test]
    public void LoadThemeFromJson_AddsToThemes()
    {
        var json = """{ "name": "Added Theme", "tokenColors": [] }""";
        _service.LoadThemeFromJson(json, "Added Theme");

        Assert.That(_service.Themes, Does.ContainKey("Added Theme"));
    }

    [Test]
    public void CurrentTheme_Set_RaisesThemeChangedEvent()
    {
        var eventRaised = false;
        _service.ThemeChanged += (s, e) => eventRaised = true;

        var json = """{ "name": "Event Theme", "tokenColors": [] }""";
        var theme = _service.LoadThemeFromJson(json, "Event Theme");
        _service.CurrentTheme = theme;

        Assert.That(eventRaised, Is.True);
    }

    #endregion

    #region TokenizeLine Tests

    [Test]
    public void TokenizeLine_EmptyLine_ReturnsEmptyTokens()
    {
        var json = """{ "scopeName": "source.empty", "patterns": [] }""";
        var grammar = _service.LoadGrammarFromJson(json, "source.empty")!;

        var result = _service.TokenizeLine("", grammar);

        Assert.That(result.Tokens, Is.Empty);
    }

    [Test]
    public void TokenizeLine_WithPattern_ReturnsTokens()
    {
        var json = """
        {
            "scopeName": "source.keyword",
            "patterns": [
                { "match": "\\b(if|else|while)\\b", "name": "keyword.control" }
            ]
        }
        """;
        var grammar = _service.LoadGrammarFromJson(json, "source.keyword")!;

        var result = _service.TokenizeLine("if (x) else y", grammar);

        Assert.That(result.Tokens, Has.Count.GreaterThan(0));
    }

    [Test]
    public void TokenizeLine_ReturnsEndState()
    {
        var json = """{ "scopeName": "source.state", "patterns": [] }""";
        var grammar = _service.LoadGrammarFromJson(json, "source.state")!;

        var result = _service.TokenizeLine("test", grammar);

        Assert.That(result.EndState, Is.Not.Null);
    }

    #endregion

    #region TokenizeDocument Tests

    [Test]
    public void TokenizeDocument_EmptyContent_ReturnsSingleResult()
    {
        var json = """{ "scopeName": "source.doc", "patterns": [] }""";
        var grammar = _service.LoadGrammarFromJson(json, "source.doc")!;

        var results = _service.TokenizeDocument("", grammar);

        // Empty string still produces one line result
        Assert.That(results, Has.Count.EqualTo(1));
    }

    [Test]
    public void TokenizeDocument_MultipleLines_ReturnsResultPerLine()
    {
        var json = """{ "scopeName": "source.multi", "patterns": [] }""";
        var grammar = _service.LoadGrammarFromJson(json, "source.multi")!;

        var results = _service.TokenizeDocument("line1\nline2\nline3", grammar);

        Assert.That(results, Has.Count.EqualTo(3));
    }

    #endregion

    #region GetStyleForScopes Tests

    [Test]
    public void GetStyleForScopes_NoTheme_ReturnsNull()
    {
        var result = _service.GetStyleForScopes(new[] { "keyword.control" });
        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetStyleForScopes_WithTheme_ReturnsStyle()
    {
        var json = """
        {
            "name": "Style Theme",
            "tokenColors": [
                {
                    "scope": "keyword",
                    "settings": { "foreground": "#ff0000", "fontStyle": "bold" }
                }
            ]
        }
        """;
        var theme = _service.LoadThemeFromJson(json, "Style Theme");
        _service.CurrentTheme = theme;

        var result = _service.GetStyleForScopes(new[] { "keyword.control" });

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Foreground, Is.EqualTo("#ff0000"));
    }

    #endregion

    #region TextMateGrammar Tests

    [Test]
    public void TextMateGrammar_Defaults_AreCorrect()
    {
        var grammar = new TextMateGrammar();
        Assert.That(grammar.ScopeName, Is.Empty);
        Assert.That(grammar.Name, Is.Empty);
        Assert.That(grammar.FileTypes, Is.Empty);
        Assert.That(grammar.Patterns, Is.Empty);
        Assert.That(grammar.Repository, Is.Empty);
    }

    [Test]
    public void TextMateGrammar_CanSetProperties()
    {
        var grammar = new TextMateGrammar
        {
            ScopeName = "source.test",
            Name = "Test",
            FirstLineMatch = "^#!.*test",
            FoldingStartMarker = "{",
            FoldingEndMarker = "}",
            FilePath = "/path/to/grammar.json"
        };

        Assert.That(grammar.ScopeName, Is.EqualTo("source.test"));
        Assert.That(grammar.FirstLineMatch, Is.EqualTo("^#!.*test"));
        Assert.That(grammar.FoldingStartMarker, Is.EqualTo("{"));
        Assert.That(grammar.FoldingEndMarker, Is.EqualTo("}"));
        Assert.That(grammar.FilePath, Is.EqualTo("/path/to/grammar.json"));
    }

    #endregion

    #region TextMatePattern Tests

    [Test]
    public void TextMatePattern_Defaults_AreCorrect()
    {
        var pattern = new TextMatePattern();
        Assert.That(pattern.Name, Is.Null);
        Assert.That(pattern.Match, Is.Null);
        Assert.That(pattern.Begin, Is.Null);
        Assert.That(pattern.End, Is.Null);
        Assert.That(pattern.ApplyEndPatternLast, Is.False);
    }

    [Test]
    public void TextMatePattern_CanSetAllProperties()
    {
        var pattern = new TextMatePattern
        {
            Name = "string.quoted",
            Match = "\"[^\"]*\"",
            Begin = "\"",
            End = "\"",
            While = ".*",
            ContentName = "string.content",
            Include = "#strings",
            ApplyEndPatternLast = true,
            Captures = new Dictionary<string, CapturePattern> { ["0"] = new CapturePattern { Name = "cap" } },
            BeginCaptures = new Dictionary<string, CapturePattern> { ["0"] = new CapturePattern { Name = "begin" } },
            EndCaptures = new Dictionary<string, CapturePattern> { ["0"] = new CapturePattern { Name = "end" } },
            WhileCaptures = new Dictionary<string, CapturePattern> { ["0"] = new CapturePattern { Name = "while" } },
            Patterns = new List<TextMatePattern> { new TextMatePattern { Name = "nested" } }
        };

        Assert.That(pattern.Name, Is.EqualTo("string.quoted"));
        Assert.That(pattern.Captures, Has.Count.EqualTo(1));
        Assert.That(pattern.BeginCaptures, Has.Count.EqualTo(1));
        Assert.That(pattern.EndCaptures, Has.Count.EqualTo(1));
        Assert.That(pattern.WhileCaptures, Has.Count.EqualTo(1));
        Assert.That(pattern.Patterns, Has.Count.EqualTo(1));
    }

    #endregion

    #region CapturePattern Tests

    [Test]
    public void CapturePattern_Defaults_AreCorrect()
    {
        var capture = new CapturePattern();
        Assert.That(capture.Name, Is.Null);
        Assert.That(capture.Patterns, Is.Null);
    }

    #endregion

    #region TextMateTheme Tests

    [Test]
    public void TextMateTheme_Defaults_AreCorrect()
    {
        var theme = new TextMateTheme();
        Assert.That(theme.Name, Is.Empty);
        Assert.That(theme.Type, Is.EqualTo("dark"));
        Assert.That(theme.TokenColors, Is.Empty);
        Assert.That(theme.Colors, Is.Empty);
    }

    [Test]
    public void TextMateTheme_CanSetProperties()
    {
        var theme = new TextMateTheme
        {
            Name = "My Theme",
            Type = "light",
            SemanticTokenColors = new Dictionary<string, TokenStyle>
            {
                ["variable"] = new TokenStyle { Foreground = "#000000" }
            }
        };

        Assert.That(theme.Name, Is.EqualTo("My Theme"));
        Assert.That(theme.Type, Is.EqualTo("light"));
        Assert.That(theme.SemanticTokenColors, Has.Count.EqualTo(1));
    }

    #endregion

    #region TokenColorRule Tests

    [Test]
    public void TokenColorRule_Defaults_AreCorrect()
    {
        var rule = new TokenColorRule();
        Assert.That(rule.Name, Is.Null);
        Assert.That(rule.Scope, Is.Null);
        Assert.That(rule.Settings, Is.Not.Null);
    }

    [Test]
    public void TokenColorRule_GetScopes_StringScope_ReturnsList()
    {
        var rule = new TokenColorRule { Scope = "keyword" };
        var scopes = rule.GetScopes();
        Assert.That(scopes, Has.Count.EqualTo(1));
        Assert.That(scopes[0], Is.EqualTo("keyword"));
    }

    [Test]
    public void TokenColorRule_GetScopes_ListScope_ReturnsList()
    {
        var rule = new TokenColorRule { Scope = new List<string> { "keyword", "storage" } };
        var scopes = rule.GetScopes();
        Assert.That(scopes, Has.Count.EqualTo(2));
    }

    [Test]
    public void TokenColorRule_GetScopes_NullScope_ReturnsEmpty()
    {
        var rule = new TokenColorRule();
        var scopes = rule.GetScopes();
        Assert.That(scopes, Is.Empty);
    }

    #endregion

    #region TokenStyle Tests

    [Test]
    public void TokenStyle_Defaults_AreCorrect()
    {
        var style = new TokenStyle();
        Assert.That(style.Foreground, Is.Null);
        Assert.That(style.Background, Is.Null);
        Assert.That(style.FontStyle, Is.Null);
    }

    [Test]
    public void TokenStyle_IsBold_ReturnsCorrectly()
    {
        var bold = new TokenStyle { FontStyle = "bold" };
        var notBold = new TokenStyle { FontStyle = "italic" };
        var empty = new TokenStyle();

        Assert.That(bold.IsBold, Is.True);
        Assert.That(notBold.IsBold, Is.False);
        Assert.That(empty.IsBold, Is.False);
    }

    [Test]
    public void TokenStyle_IsItalic_ReturnsCorrectly()
    {
        var italic = new TokenStyle { FontStyle = "italic" };
        var notItalic = new TokenStyle { FontStyle = "bold" };

        Assert.That(italic.IsItalic, Is.True);
        Assert.That(notItalic.IsItalic, Is.False);
    }

    [Test]
    public void TokenStyle_IsUnderline_ReturnsCorrectly()
    {
        var underline = new TokenStyle { FontStyle = "underline" };
        var notUnderline = new TokenStyle { FontStyle = "bold" };

        Assert.That(underline.IsUnderline, Is.True);
        Assert.That(notUnderline.IsUnderline, Is.False);
    }

    [Test]
    public void TokenStyle_CombinedFontStyle_WorksCorrectly()
    {
        var combined = new TokenStyle { FontStyle = "bold italic underline" };

        Assert.That(combined.IsBold, Is.True);
        Assert.That(combined.IsItalic, Is.True);
        Assert.That(combined.IsUnderline, Is.True);
    }

    #endregion

    #region TokenizerState Tests

    [Test]
    public void TokenizerState_Defaults_AreCorrect()
    {
        var state = new TokenizerState();
        Assert.That(state.RuleStack, Is.Empty);
        Assert.That(state.ScopeStack, Is.Empty);
    }

    [Test]
    public void TokenizerState_Clone_CreatesDeepCopy()
    {
        var state = new TokenizerState
        {
            RuleStack = new List<string> { "rule1", "rule2" },
            ScopeStack = new List<string> { "scope1", "scope2" }
        };

        var clone = state.Clone();
        clone.RuleStack.Add("rule3");
        clone.ScopeStack.Add("scope3");

        Assert.That(state.RuleStack, Has.Count.EqualTo(2));
        Assert.That(state.ScopeStack, Has.Count.EqualTo(2));
        Assert.That(clone.RuleStack, Has.Count.EqualTo(3));
        Assert.That(clone.ScopeStack, Has.Count.EqualTo(3));
    }

    [Test]
    public void TokenizerState_Equals_SameContent_ReturnsTrue()
    {
        var state1 = new TokenizerState
        {
            RuleStack = new List<string> { "rule1" },
            ScopeStack = new List<string> { "scope1" }
        };
        var state2 = new TokenizerState
        {
            RuleStack = new List<string> { "rule1" },
            ScopeStack = new List<string> { "scope1" }
        };

        Assert.That(state1.Equals(state2), Is.True);
    }

    [Test]
    public void TokenizerState_Equals_DifferentContent_ReturnsFalse()
    {
        var state1 = new TokenizerState { RuleStack = new List<string> { "rule1" } };
        var state2 = new TokenizerState { RuleStack = new List<string> { "rule2" } };

        Assert.That(state1.Equals(state2), Is.False);
    }

    [Test]
    public void TokenizerState_Equals_Null_ReturnsFalse()
    {
        var state = new TokenizerState();
        Assert.That(state.Equals(null), Is.False);
    }

    #endregion

    #region TokenizationResult Tests

    [Test]
    public void TokenizationResult_Defaults_AreCorrect()
    {
        var result = new TokenizationResult();
        Assert.That(result.Tokens, Is.Empty);
        Assert.That(result.EndState, Is.Not.Null);
        Assert.That(result.IsMultiLine, Is.False);
    }

    #endregion

    #region TextMateToken Tests

    [Test]
    public void TextMateToken_Defaults_AreCorrect()
    {
        var token = new TextMateToken();
        Assert.That(token.StartIndex, Is.EqualTo(0));
        Assert.That(token.EndIndex, Is.EqualTo(0));
        Assert.That(token.Scopes, Is.Empty);
    }

    [Test]
    public void TextMateToken_Length_CalculatesCorrectly()
    {
        var token = new TextMateToken { StartIndex = 5, EndIndex = 15 };
        Assert.That(token.Length, Is.EqualTo(10));
    }

    [Test]
    public void TextMateToken_Scope_ReturnsLastScope()
    {
        var token = new TextMateToken
        {
            Scopes = new List<string> { "source.test", "keyword.control", "keyword.control.if" }
        };
        Assert.That(token.Scope, Is.EqualTo("keyword.control.if"));
    }

    [Test]
    public void TextMateToken_Scope_EmptyScopes_ReturnsNull()
    {
        var token = new TextMateToken();
        Assert.That(token.Scope, Is.Null);
    }

    #endregion

    #region Event Args Tests

    [Test]
    public void GrammarLoadedEventArgs_StoresValues()
    {
        var grammar = new TextMateGrammar { ScopeName = "source.test" };
        var args = new GrammarLoadedEventArgs(grammar, "/path/to/grammar.json");

        Assert.That(args.Grammar.ScopeName, Is.EqualTo("source.test"));
        Assert.That(args.FilePath, Is.EqualTo("/path/to/grammar.json"));
    }

    [Test]
    public void ThemeChangedEventArgs_StoresValues()
    {
        var oldTheme = new TextMateTheme { Name = "Old" };
        var newTheme = new TextMateTheme { Name = "New" };
        var args = new ThemeChangedEventArgs(oldTheme, newTheme);

        Assert.That(args.OldTheme!.Name, Is.EqualTo("Old"));
        Assert.That(args.NewTheme!.Name, Is.EqualTo("New"));
    }

    #endregion
}
