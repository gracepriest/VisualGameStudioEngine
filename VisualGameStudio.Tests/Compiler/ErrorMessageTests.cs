using NUnit.Framework;
using BasicLang.Compiler;
using System.Linq;

namespace VisualGameStudio.Tests.Compiler;

[TestFixture]
public class ErrorMessageTests
{
    #region Parser Error Message Tests

    [Test]
    public void Parser_MissingThen_ProvideSuggestion()
    {
        var source = @"Sub Test()
    If x = 10
        PrintLine(x)
    End If
End Sub";
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);

        parser.Parse();

        Assert.That(parser.Errors.Count, Is.GreaterThan(0), "Expected parser errors");
        // Error should mention missing 'Then'
        var error = parser.Errors.FirstOrDefault();
        Assert.That(error, Is.Not.Null);
        Assert.That(error.Message, Does.Contain("Then").IgnoreCase.Or.Contain("expected").IgnoreCase);
    }

    [Test]
    public void Parser_TopLevelCode_ProvideSuggestion()
    {
        var source = @"x = 10";  // Code at top level without declaration
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);

        parser.Parse();

        Assert.That(parser.Errors.Count, Is.GreaterThan(0), "Expected parser errors");
        var error = parser.Errors.FirstOrDefault();
        Assert.That(error, Is.Not.Null);
        Assert.That(error.Message, Does.Contain("top level").IgnoreCase.Or.Contain("unexpected").IgnoreCase);
        // Should suggest valid declarations
        Assert.That(error.Suggestion, Is.Not.Null.Or.Not.Empty);
    }

    [Test]
    public void Parser_MissingAs_ProvideSuggestion()
    {
        var source = @"Sub Test()
    Dim x Integer  ' Missing 'As'
End Sub";
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);

        parser.Parse();

        Assert.That(parser.Errors.Count, Is.GreaterThan(0), "Expected parser errors");
        var error = parser.Errors.FirstOrDefault();
        Assert.That(error, Is.Not.Null);
        // Should suggest adding 'As'
        Assert.That(error.Suggestion, Does.Contain("As").IgnoreCase);
    }

    [Test]
    public void Parser_MissingEndSub_ProvideSuggestion()
    {
        var source = @"Sub Test()
    Dim x As Integer";  // Missing 'End Sub'
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);

        parser.Parse();

        Assert.That(parser.Errors.Count, Is.GreaterThan(0), "Expected parser errors");
        var error = parser.Errors.FirstOrDefault();
        Assert.That(error, Is.Not.Null);
        // Should mention End Sub or unclosed block
        Assert.That(error.Message, Does.Contain("End").IgnoreCase);
    }

    [Test]
    public void Parser_MissingCloseParen_ProvideSuggestion()
    {
        var source = @"Sub Test()
    PrintLine(x
End Sub";  // Missing close paren
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);

        parser.Parse();

        Assert.That(parser.Errors.Count, Is.GreaterThan(0), "Expected parser errors");
        var error = parser.Errors.FirstOrDefault();
        Assert.That(error, Is.Not.Null);
        // Should mention missing parenthesis
        Assert.That(error.Message, Does.Contain(")").Or.Contain("paren").IgnoreCase);
    }

    #endregion

    #region Error Formatter Tests

    [Test]
    public void ErrorFormatter_SuggestKeywordCorrection_DetectsCommonTypos()
    {
        // Test common typos
        Assert.That(ErrorFormatter.SuggestKeywordCorrection("funciton"), Is.EqualTo("Function"));
        Assert.That(ErrorFormatter.SuggestKeywordCorrection("subroutine"), Is.EqualTo("Sub"));
        Assert.That(ErrorFormatter.SuggestKeywordCorrection("elsif"), Is.EqualTo("ElseIf"));
        Assert.That(ErrorFormatter.SuggestKeywordCorrection("retrun"), Is.EqualTo("Return"));
        Assert.That(ErrorFormatter.SuggestKeywordCorrection("dimm"), Is.EqualTo("Dim"));
    }

    [Test]
    public void ErrorFormatter_SuggestKeywordCorrection_CaseInsensitive()
    {
        Assert.That(ErrorFormatter.SuggestKeywordCorrection("FUNCITON"), Is.EqualTo("Function"));
        Assert.That(ErrorFormatter.SuggestKeywordCorrection("Funciton"), Is.EqualTo("Function"));
    }

    [Test]
    public void ErrorFormatter_SuggestKeywordCorrection_ReturnsNullForUnknown()
    {
        Assert.That(ErrorFormatter.SuggestKeywordCorrection("xyz"), Is.Null);
        Assert.That(ErrorFormatter.SuggestKeywordCorrection("randomword"), Is.Null);
    }

    [Test]
    public void ErrorFormatter_FormatTypeMismatchError_IncludesTypes()
    {
        var result = ErrorFormatter.FormatTypeMismatchError("Integer", "String", 5, 10);

        Assert.That(result, Does.Contain("Integer"));
        Assert.That(result, Does.Contain("String"));
        Assert.That(result, Does.Contain("line 5"));
    }

    [Test]
    public void ErrorFormatter_FormatTypeMismatchError_IncludesSuggestion()
    {
        var result = ErrorFormatter.FormatTypeMismatchError("Integer", "String", 5, 10);

        // Should suggest conversion for string to numeric
        Assert.That(result, Does.Contain("Val").Or.Contain("CInt").IgnoreCase);
    }

    [Test]
    public void ErrorFormatter_FormatArgumentCountError_SingleArgument()
    {
        var result = ErrorFormatter.FormatArgumentCountError("DoSomething", 1, 3, 5, 10);

        Assert.That(result, Does.Contain("DoSomething"));
        Assert.That(result, Does.Contain("1 argument"));
        Assert.That(result, Does.Contain("Remove"));
    }

    [Test]
    public void ErrorFormatter_FormatArgumentCountError_MultipleArguments()
    {
        var result = ErrorFormatter.FormatArgumentCountError("Calculate", 3, 1, 5, 10);

        Assert.That(result, Does.Contain("Calculate"));
        Assert.That(result, Does.Contain("3 arguments"));
        Assert.That(result, Does.Contain("Add"));
    }

    [Test]
    public void ErrorFormatter_FormatUndefinedSymbolError_WithSimilarNames()
    {
        var similarNames = new[] { "counter", "count", "Counter" };
        var result = ErrorFormatter.FormatUndefinedSymbolError("cont", 5, 10, null, similarNames);

        Assert.That(result, Does.Contain("'cont'"));
        Assert.That(result, Does.Contain("Did you mean"));
    }

    #endregion

    #region Error Code Tests

    [Test]
    public void ErrorCode_LexerErrors_StartWithBL1()
    {
        Assert.That(ErrorCode.BL1001_UnterminatedString.ToString(), Does.StartWith("BL1"));
        Assert.That(ErrorCode.BL1002_UnterminatedInterpolatedString.ToString(), Does.StartWith("BL1"));
    }

    [Test]
    public void ErrorCode_ParserErrors_StartWithBL2()
    {
        Assert.That(ErrorCode.BL2001_UnexpectedToken.ToString(), Does.StartWith("BL2"));
        Assert.That(ErrorCode.BL2004_MismatchedBlock.ToString(), Does.StartWith("BL2"));
    }

    [Test]
    public void ErrorCode_SemanticErrors_StartWithBL3()
    {
        Assert.That(ErrorCode.BL3001_TypeMismatch.ToString(), Does.StartWith("BL3"));
        Assert.That(ErrorCode.BL3002_UndefinedSymbol.ToString(), Does.StartWith("BL3"));
    }

    #endregion

    #region Quick Fix Tests

    [Test]
    public void QuickFix_HasDescriptionAndReplacement()
    {
        var fix = new QuickFix("Add type annotation", "x As Integer", 5, 1, 5, 10);

        Assert.That(fix.Description, Is.EqualTo("Add type annotation"));
        Assert.That(fix.Replacement, Is.EqualTo("x As Integer"));
        Assert.That(fix.StartLine, Is.EqualTo(5));
    }

    [Test]
    public void QuickFix_ToString_ContainsInfo()
    {
        var fix = new QuickFix("Add type annotation", "x As Integer", 5, 1, 5, 10);
        var str = fix.ToString();

        Assert.That(str, Does.Contain("Quick fix"));
        Assert.That(str, Does.Contain("Add type annotation"));
    }

    #endregion

    #region Error Context Tests

    [Test]
    public void ErrorContext_TracksPushPop()
    {
        var ctx = new ErrorContext();

        ctx.PushContext("Function Calculate");
        ctx.PushContext("If statement");

        var context = ctx.GetCurrentContext();
        Assert.That(context, Does.Contain("Calculate"));
        Assert.That(context, Does.Contain("If"));

        ctx.PopContext();
        context = ctx.GetCurrentContext();
        Assert.That(context, Does.Contain("Calculate"));
        Assert.That(context, Does.Not.Contain("If"));
    }

    [Test]
    public void ErrorContext_PoisonedSymbols_AreTracked()
    {
        var ctx = new ErrorContext();

        Assert.That(ctx.IsSymbolPoisoned("x"), Is.False);

        ctx.PoisonSymbol("x");
        Assert.That(ctx.IsSymbolPoisoned("x"), Is.True);

        ctx.ClearPoisonedSymbols();
        Assert.That(ctx.IsSymbolPoisoned("x"), Is.False);
    }

    #endregion

    #region Error Grouping Tests

    [Test]
    public void ErrorGrouper_GroupsRelatedErrors()
    {
        // This test verifies the error grouping logic exists
        // Full integration would require semantic analyzer errors
        var group = new ErrorGroup(
            new BasicLang.Compiler.SemanticAnalysis.SemanticError("Undefined symbol 'x'", 5, 10));

        group.AddRelatedError(
            new BasicLang.Compiler.SemanticAnalysis.SemanticError("Cannot determine type of 'x'", 5, 15));

        Assert.That(group.RelatedErrors.Count, Is.EqualTo(1));
    }

    #endregion

    #region Extension Methods Tests

    [Test]
    public void ErrorFormatterExtensions_FormatOperatorError_IncludesTypes()
    {
        var result = ErrorFormatterExtensions.FormatOperatorError("+", "String", "Integer", 5, 10);

        Assert.That(result, Does.Contain("String"));
        Assert.That(result, Does.Contain("Integer"));
        Assert.That(result, Does.Contain("+"));
    }

    [Test]
    public void ErrorFormatterExtensions_FormatMemberAccessError_IncludesSuggestions()
    {
        var similarMembers = new[] { "GetName", "Name", "SetName" };
        var result = ErrorFormatterExtensions.FormatMemberAccessError("Player", "GetNam", 5, 10, null, similarMembers);

        Assert.That(result, Does.Contain("Player"));
        Assert.That(result, Does.Contain("GetNam"));
        Assert.That(result, Does.Contain("Did you mean"));
    }

    [Test]
    public void ErrorFormatterExtensions_CreateTypeConversionFix_GeneratesCorrectFix()
    {
        var fix = ErrorFormatterExtensions.CreateTypeConversionFix("x", "String", "Integer", 5, 10, 15);

        Assert.That(fix.Replacement, Does.Contain("CInt"));
        Assert.That(fix.Replacement, Does.Contain("x"));
    }

    [Test]
    public void ErrorFormatterExtensions_CreateAddEndBlockFix_GeneratesCorrectFix()
    {
        var fix = ErrorFormatterExtensions.CreateAddEndBlockFix("Sub", 10);

        Assert.That(fix.Description, Does.Contain("End Sub"));
        Assert.That(fix.Replacement, Is.EqualTo("End Sub"));
    }

    #endregion
}
