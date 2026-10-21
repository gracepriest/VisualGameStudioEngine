using BasicLang.Compiler;
using BasicLang.Compiler.SemanticAnalysis;

namespace BasicLang.Forms;

/// <summary>
/// Task 34's source-level web errors. They run once after parsing, before semantic analysis, so
/// the user sees the named error rather than a follow-on conversion or missing-member error.
///
/// <para>Everything here is a TOKEN scan over the preprocessed source, never a text search:
/// comments and string literals cannot trigger a diagnostic (§11b note 3).</para>
/// </summary>
public static class PortableWebSourceChecks
{
    public const string RuntimeCreation =
        "RuntimeControlCreation: Creating a {0} at run time is not supported on the web yet.";

    public const string MixedStyle =
        "MixedCodeStyle: this web form's designer regions are DOM-style, but its code uses " +
        "System.Windows.Forms/Inherits Form. Convert the form to the portable style or remove the WinForms lines.";

    public static IReadOnlyList<SemanticError> Check(
        IEnumerable<CompilationUnit> units, Func<string, string, string> preprocess)
    {
        var errors = new List<SemanticError>();
        foreach (var unit in units)
        {
            var source = unit.SourceCode;
            if (string.IsNullOrEmpty(source)) continue;

            var processed = preprocess(source, unit.FilePath);
            var tokens = VisibleTokens(processed);

            var style = FormCodeStyle.Of(processed, formFileName: null, FormTarget.Web);
            var init = style.Regions.FirstOrDefault(r =>
                string.Equals(r.Name, RegionMarkers.Init, StringComparison.OrdinalIgnoreCase)
                && r.State is RegionState.Canon or RegionState.HashMismatch);

            CheckRuntimeCreation(tokens, init, errors);
            CheckMessageBoxButtons(tokens, errors);

            if (HasMixedCodeStyle(processed, style))
            {
                errors.Add(new SemanticError(MixedStyle, style.Regions[0].Line, 1));
            }
        }
        return errors;
    }

    /// <summary>
    /// The shared MixedCodeStyle predicate: DOM-style markers over code that names the WinForms
    /// shape. Reads tokens, never text (§11b note 3). Used by the web build and by the designer's
    /// region writer (§8.4).
    /// </summary>
    public static bool HasMixedCodeStyle(string source, FormStyleRead style)
    {
        if (style.Problem != null || style.Style != FormStyle.Dom) return false;
        if (!style.Regions.Any(r =>
                string.Equals(r.Name, RegionMarkers.Controls, StringComparison.OrdinalIgnoreCase)
                || string.Equals(r.Name, RegionMarkers.Init, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var tokens = VisibleTokens(source);
        return HasUsing(tokens, "System", "Windows", "Forms") || HasInheritsForm(tokens);
    }

    private static List<Token> VisibleTokens(string source) =>
        new Lexer(source).Tokenize()
            .Where(t => t.Type != TokenType.Comment
                     && t.Type != TokenType.StringLiteral
                     && t.Type != TokenType.CharLiteral
                     && t.Type != TokenType.InterpolatedStringLiteral)
            .ToList();

    private static void CheckRuntimeCreation(
        IReadOnlyList<Token> tokens, FormRegion? init, List<SemanticError> errors)
    {
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Type != TokenType.New) continue;

            string? typeName = null;
            for (var j = i + 1; j < tokens.Count; j++)
            {
                if (tokens[j].Type == TokenType.Identifier)
                {
                    typeName = tokens[j].Lexeme;
                    continue;
                }
                if (tokens[j].Type == TokenType.Dot) continue;
                break;
            }

            if (typeName == null || !PortableLibraryTypes.IsControlClass(typeName)) continue;
            if (init != null && InRegion(init, tokens[i].Line)) continue;

            errors.Add(new SemanticError(
                string.Format(RuntimeCreation, typeName), tokens[i].Line, tokens[i].Column));
        }
    }

    private static void CheckMessageBoxButtons(IReadOnlyList<Token> tokens, List<SemanticError> errors)
    {
        for (var i = 0; i + 2 < tokens.Count; i++)
        {
            if (tokens[i].Type != TokenType.Identifier
                || !string.Equals(tokens[i].Lexeme, "MessageBoxButtons", StringComparison.OrdinalIgnoreCase)
                || tokens[i + 1].Type != TokenType.Dot
                || tokens[i + 2].Type != TokenType.Identifier)
            {
                continue;
            }

            var member = tokens[i + 2].Lexeme;
            if (string.Equals(member, "OK", StringComparison.OrdinalIgnoreCase)
                || string.Equals(member, "OKCancel", StringComparison.OrdinalIgnoreCase)
                || string.Equals(member, "YesNo", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            errors.Add(new SemanticError(
                $"WebUnavailableMember: 'MessageBoxButtons.{member}' is a WinForms member that is not " +
                "available on the web. The web supports OK, OKCancel and YesNo. Put desktop-only code " +
                "inside #If DESKTOP Then ... #End If.",
                tokens[i].Line, tokens[i].Column));
        }
    }

    private static bool InRegion(FormRegion region, int line)
    {
        if (line <= region.Line) return false;
        var lines = region.Content.Count(c => c == '\n');
        if (region.Content.Length > 0 && region.Content[^1] != '\n') lines++;
        return line <= region.Line + lines;
    }

    private static bool HasUsing(IReadOnlyList<Token> tokens, params string[] parts)
    {
        for (var i = 1; i < tokens.Count; i++)
        {
            if (tokens[i - 1].Type != TokenType.Using) continue;
            if (MatchesNamespace(tokens, i, parts)) return true;
        }
        return false;
    }

    private static bool HasInheritsForm(IReadOnlyList<Token> tokens)
    {
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Type != TokenType.Inherits) continue;
            if (i + 1 < tokens.Count && tokens[i + 1].Type == TokenType.Identifier
                && string.Equals(tokens[i + 1].Lexeme, "Form", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (i + 7 < tokens.Count
                && tokens[i + 1].Type == TokenType.Identifier
                && string.Equals(tokens[i + 1].Lexeme, "System", StringComparison.OrdinalIgnoreCase)
                && MatchesNamespace(tokens, i + 1, "System", "Windows", "Forms", "Form"))
            {
                return true;
            }
        }
        return false;
    }

    private static bool MatchesNamespace(IReadOnlyList<Token> tokens, int start, params string[] parts)
    {
        if (start + parts.Length * 2 - 1 >= tokens.Count) return false;
        for (var p = 0; p < parts.Length; p++)
        {
            var token = tokens[start + p * 2];
            if (token.Type != TokenType.Identifier
                || !string.Equals(token.Lexeme, parts[p], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (p < parts.Length - 1 && tokens[start + p * 2 + 1].Type != TokenType.Dot) return false;
        }
        return true;
    }
}
