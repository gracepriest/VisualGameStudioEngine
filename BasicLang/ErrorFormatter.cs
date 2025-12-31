using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BasicLang.Compiler.SemanticAnalysis;

namespace BasicLang.Compiler
{
    /// <summary>
    /// Error codes for BasicLang compiler errors
    /// </summary>
    public enum ErrorCode
    {
        // Lexer errors (BL1xxx)
        BL1001_UnterminatedString,
        BL1002_UnterminatedInterpolatedString,
        BL1003_InvalidCharacter,
        BL1004_InvalidNumberFormat,
        BL1005_InvalidEscapeSequence,
        BL1006_UnrecognizedDirective,

        // Parser errors (BL2xxx)
        BL2001_UnexpectedToken,
        BL2002_MissingToken,
        BL2003_MissingKeyword,
        BL2004_MismatchedBlock,
        BL2005_InvalidSyntax,
        BL2006_UnexpectedEndOfFile,

        // Semantic errors (BL3xxx)
        BL3001_TypeMismatch,
        BL3002_UndefinedSymbol,
        BL3003_RedefinedSymbol,
        BL3004_WrongNumberOfArguments,
        BL3005_InvalidAssignment,
        BL3006_InvalidOperation,
        BL3007_CannotConvert,
        BL3008_MissingReturnType,
        BL3009_AccessibilityError,
        BL3010_CircularDependency,
        BL3011_InvalidMemberAccess,
        BL3012_InvalidArrayAccess,

        // General errors
        BL9999_UnknownError
    }

    /// <summary>
    /// Formats compiler errors in a consistent, helpful way
    /// </summary>
    public static class ErrorFormatter
    {
        /// <summary>
        /// Format a lexer error with context
        /// </summary>
        public static string FormatLexerError(ErrorCode code, string message, int line, int column,
            string sourceCode = null, string suggestion = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Error {GetErrorCodeString(code)}: {message}");
            sb.AppendLine($"  at line {line}, column {column}");

            if (!string.IsNullOrEmpty(sourceCode))
            {
                AppendCodeSnippet(sb, sourceCode, line, column);
            }

            if (!string.IsNullOrEmpty(suggestion))
            {
                sb.AppendLine();
                sb.AppendLine($"Suggestion: {suggestion}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Format a parser error with context
        /// </summary>
        public static string FormatParserError(ErrorCode code, string message, Token token,
            string sourceCode = null, string expected = null, string suggestion = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Error {GetErrorCodeString(code)}: {message}");
            sb.AppendLine($"  at line {token.Line}, column {token.Column}");

            if (!string.IsNullOrEmpty(sourceCode))
            {
                AppendCodeSnippet(sb, sourceCode, token.Line, token.Column, token.Lexeme.Length);
            }

            if (!string.IsNullOrEmpty(expected))
            {
                sb.AppendLine();
                sb.AppendLine($"Expected: {expected}");
                sb.AppendLine($"Found: {token.Type}");
            }

            if (!string.IsNullOrEmpty(suggestion))
            {
                sb.AppendLine();
                sb.AppendLine($"Suggestion: {suggestion}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Format a semantic error with context
        /// </summary>
        public static string FormatSemanticError(ErrorCode code, string message, int line, int column,
            string sourceCode = null, string suggestion = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Error {GetErrorCodeString(code)}: {message}");
            sb.AppendLine($"  at line {line}, column {column}");

            if (!string.IsNullOrEmpty(sourceCode))
            {
                AppendCodeSnippet(sb, sourceCode, line, column);
            }

            if (!string.IsNullOrEmpty(suggestion))
            {
                sb.AppendLine();
                sb.AppendLine($"Suggestion: {suggestion}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Format a type mismatch error with detailed type information
        /// </summary>
        public static string FormatTypeMismatchError(string expectedType, string actualType,
            int line, int column, string sourceCode = null, string context = null)
        {
            var message = $"Type mismatch: cannot convert '{actualType}' to '{expectedType}'";
            if (!string.IsNullOrEmpty(context))
            {
                message = $"{context}: {message}";
            }

            return FormatSemanticError(ErrorCode.BL3001_TypeMismatch, message, line, column,
                sourceCode, GetTypeConversionSuggestion(expectedType, actualType));
        }

        /// <summary>
        /// Format an undefined symbol error with suggestions for similar names
        /// </summary>
        public static string FormatUndefinedSymbolError(string symbolName, int line, int column,
            string sourceCode = null, IEnumerable<string> similarNames = null)
        {
            var message = $"Undefined symbol '{symbolName}'";
            string suggestion = null;

            if (similarNames != null && similarNames.Any())
            {
                var matches = GetSimilarNames(symbolName, similarNames);
                if (matches.Any())
                {
                    suggestion = $"Did you mean: {string.Join(", ", matches.Select(n => $"'{n}'"))}?";
                }
            }

            return FormatSemanticError(ErrorCode.BL3002_UndefinedSymbol, message, line, column,
                sourceCode, suggestion);
        }

        /// <summary>
        /// Format a wrong number of arguments error
        /// </summary>
        public static string FormatArgumentCountError(string functionName, int expected, int actual,
            int line, int column, string sourceCode = null, bool hasOptionalParams = false,
            int minRequired = 0)
        {
            string message;
            string suggestion;

            if (hasOptionalParams && minRequired > 0)
            {
                message = $"Function '{functionName}' expects {minRequired} to {expected} arguments, but {actual} were provided";
                suggestion = $"Check the function signature to see which parameters are optional";
            }
            else if (expected == 1)
            {
                message = $"Function '{functionName}' expects {expected} argument, but {actual} were provided";
                suggestion = actual > expected
                    ? "Remove extra arguments"
                    : "Add the missing argument";
            }
            else
            {
                message = $"Function '{functionName}' expects {expected} arguments, but {actual} were provided";
                suggestion = actual > expected
                    ? $"Remove {actual - expected} extra argument(s)"
                    : $"Add {expected - actual} missing argument(s)";
            }

            return FormatSemanticError(ErrorCode.BL3004_WrongNumberOfArguments, message, line, column,
                sourceCode, suggestion);
        }

        /// <summary>
        /// Format a block mismatch error showing what was opened
        /// </summary>
        public static string FormatBlockMismatchError(string openedBlock, string closedBlock,
            int openLine, int closeLine, int closeColumn, string sourceCode = null)
        {
            var message = $"Block mismatch: '{openedBlock}' opened at line {openLine} but '{closedBlock}' found";
            var suggestion = $"Expected 'End {openedBlock}' to match the '{openedBlock}' at line {openLine}";

            return FormatParserError(ErrorCode.BL2004_MismatchedBlock, message,
                new Token(TokenType.Unknown, closedBlock, null, closeLine, closeColumn),
                sourceCode, $"End {openedBlock}", suggestion);
        }

        /// <summary>
        /// Get error code as string (e.g., "BL1001")
        /// </summary>
        private static string GetErrorCodeString(ErrorCode code)
        {
            var numericPart = ((int)code).ToString();
            if (code.ToString().StartsWith("BL"))
            {
                // Extract the number from the enum name (e.g., BL1001_UnterminatedString -> BL1001)
                var parts = code.ToString().Split('_');
                if (parts.Length > 0)
                {
                    return parts[0];
                }
            }
            return $"BL{numericPart:D4}";
        }

        /// <summary>
        /// Append a code snippet showing the error location
        /// </summary>
        private static void AppendCodeSnippet(StringBuilder sb, string sourceCode, int line, int column, int length = 1)
        {
            if (string.IsNullOrEmpty(sourceCode))
                return;

            var lines = sourceCode.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            if (line < 1 || line > lines.Length)
                return;

            sb.AppendLine();

            // Show context: previous line, error line, next line
            int contextStart = Math.Max(0, line - 2);
            int contextEnd = Math.Min(lines.Length - 1, line);

            for (int i = contextStart; i <= contextEnd; i++)
            {
                var lineNum = i + 1;
                var prefix = lineNum == line ? " > " : "   ";
                sb.AppendLine($"{prefix}{lineNum:D4} | {lines[i]}");

                // Show error indicator on the error line
                if (lineNum == line)
                {
                    var indent = new string(' ', column - 1);
                    var underline = new string('^', Math.Max(1, length));
                    sb.AppendLine($"       | {indent}{underline}");
                }
            }
        }

        /// <summary>
        /// Find similar symbol names using Levenshtein distance
        /// </summary>
        private static IEnumerable<string> GetSimilarNames(string target, IEnumerable<string> candidates, int maxDistance = 3)
        {
            return candidates
                .Select(c => new { Name = c, Distance = LevenshteinDistance(target.ToLower(), c.ToLower()) })
                .Where(x => x.Distance <= maxDistance)
                .OrderBy(x => x.Distance)
                .Take(3)
                .Select(x => x.Name);
        }

        /// <summary>
        /// Calculate Levenshtein distance between two strings
        /// </summary>
        private static int LevenshteinDistance(string s, string t)
        {
            if (string.IsNullOrEmpty(s))
                return string.IsNullOrEmpty(t) ? 0 : t.Length;
            if (string.IsNullOrEmpty(t))
                return s.Length;

            int[,] d = new int[s.Length + 1, t.Length + 1];

            for (int i = 0; i <= s.Length; i++)
                d[i, 0] = i;
            for (int j = 0; j <= t.Length; j++)
                d[0, j] = j;

            for (int j = 1; j <= t.Length; j++)
            {
                for (int i = 1; i <= s.Length; i++)
                {
                    int cost = (s[i - 1] == t[j - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }

            return d[s.Length, t.Length];
        }

        /// <summary>
        /// Get suggestion for type conversion
        /// </summary>
        private static string GetTypeConversionSuggestion(string expectedType, string actualType)
        {
            // Suggest conversion functions
            if (IsNumericType(expectedType) && IsNumericType(actualType))
            {
                return $"Use C{GetShortTypeName(expectedType)}(...) to explicitly convert";
            }

            if (expectedType == "String" && actualType != "String")
            {
                return "Use CStr(...) or .ToString() to convert to String";
            }

            if (IsNumericType(expectedType) && actualType == "String")
            {
                return $"Use Val(...) or C{GetShortTypeName(expectedType)}(...) to convert from String";
            }

            return $"Ensure the expression returns type '{expectedType}'";
        }

        private static bool IsNumericType(string typeName)
        {
            return typeName == "Integer" || typeName == "Long" ||
                   typeName == "Single" || typeName == "Double";
        }

        private static string GetShortTypeName(string typeName)
        {
            switch (typeName)
            {
                case "Integer": return "Int";
                case "Long": return "Lng";
                case "Single": return "Sng";
                case "Double": return "Dbl";
                case "Boolean": return "Bool";
                default: return typeName;
            }
        }

        /// <summary>
        /// Get common typo suggestions for keywords
        /// </summary>
        public static string SuggestKeywordCorrection(string typo)
        {
            var commonTypos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "funciton", "Function" },
                { "fucntion", "Function" },
                { "funtion", "Function" },
                { "functon", "Function" },
                { "subroutine", "Sub" },
                { "elsif", "ElseIf" },
                { "elseif", "ElseIf" },
                { "endif", "End If" },
                { "endsub", "End Sub" },
                { "endfunction", "End Function" },
                { "endclass", "End Class" },
                { "endwhile", "Wend" },
                { "endfor", "Next" },
                { "endloop", "Loop" },
                { "wend", "Wend" },
                { "dimm", "Dim" },
                { "integr", "Integer" },
                { "strig", "String" },
                { "strng", "String" },
                { "boolen", "Boolean" },
                { "booleen", "Boolean" },
                { "privat", "Private" },
                { "publc", "Public" },
                { "protectd", "Protected" },
                { "returnn", "Return" },
                { "retrun", "Return" },
            };

            if (commonTypos.TryGetValue(typo, out var correction))
            {
                return correction;
            }

            return null;
        }
    }

    /// <summary>
    /// Enhanced exception for lexer errors
    /// </summary>
    public class LexerException : Exception
    {
        public ErrorCode ErrorCode { get; }
        public int Line { get; }
        public int Column { get; }
        public string SourceCode { get; }

        public LexerException(ErrorCode code, string message, int line, int column, string sourceCode = null)
            : base(message)
        {
            ErrorCode = code;
            Line = line;
            Column = column;
            SourceCode = sourceCode;
        }

        public override string ToString()
        {
            return ErrorFormatter.FormatLexerError(ErrorCode, Message, Line, Column, SourceCode);
        }
    }

    /// <summary>
    /// Represents a quick fix suggestion for an error
    /// </summary>
    public class QuickFix
    {
        public string Description { get; set; }
        public string Replacement { get; set; }
        public int StartLine { get; set; }
        public int StartColumn { get; set; }
        public int EndLine { get; set; }
        public int EndColumn { get; set; }

        public QuickFix(string description, string replacement, int startLine, int startColumn, int endLine, int endColumn)
        {
            Description = description;
            Replacement = replacement;
            StartLine = startLine;
            StartColumn = startColumn;
            EndLine = endLine;
            EndColumn = endColumn;
        }

        public override string ToString()
        {
            return $"  Quick fix: {Description}\n    Replace with: {Replacement}";
        }
    }

    /// <summary>
    /// Groups related errors together
    /// </summary>
    public class ErrorGroup
    {
        public SemanticError PrimaryError { get; set; }
        public List<SemanticError> RelatedErrors { get; set; }
        public string CommonCause { get; set; }

        public ErrorGroup(SemanticError primaryError, string commonCause = null)
        {
            PrimaryError = primaryError;
            RelatedErrors = new List<SemanticError>();
            CommonCause = commonCause;
        }

        public void AddRelatedError(SemanticError error)
        {
            RelatedErrors.Add(error);
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine(PrimaryError.ToString());

            if (!string.IsNullOrEmpty(CommonCause))
            {
                sb.AppendLine($"  Common cause: {CommonCause}");
            }

            if (RelatedErrors.Count > 0)
            {
                sb.AppendLine($"  Related errors ({RelatedErrors.Count}):");
                foreach (var error in RelatedErrors.Take(3))
                {
                    sb.AppendLine($"    - Line {error.Line}: {error.Message}");
                }
                if (RelatedErrors.Count > 3)
                {
                    sb.AppendLine($"    ... and {RelatedErrors.Count - 3} more");
                }
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// Extended error formatting methods
    /// </summary>
    public static partial class ErrorFormatterExtensions
    {
        /// <summary>
        /// Format an operator type error with available overloads
        /// </summary>
        public static string FormatOperatorError(string op, string leftType, string rightType,
            int line, int column, string sourceCode = null, IEnumerable<string> availableOverloads = null)
        {
            var message = $"Operator '{op}' cannot be applied to operands of type '{leftType}' and '{rightType}'";
            string suggestion = null;

            if (availableOverloads != null && availableOverloads.Any())
            {
                suggestion = $"Available overloads for '{op}':\n  {string.Join("\n  ", availableOverloads)}";
            }
            else
            {
                suggestion = GetOperatorSuggestion(op, leftType, rightType);
            }

            return ErrorFormatter.FormatSemanticError(ErrorCode.BL3006_InvalidOperation, message, line, column,
                sourceCode, suggestion);
        }

        /// <summary>
        /// Format a member access error with similar member suggestions
        /// </summary>
        public static string FormatMemberAccessError(string typeName, string memberName,
            int line, int column, string sourceCode = null, IEnumerable<string> similarMembers = null)
        {
            var message = $"'{typeName}' does not contain a definition for '{memberName}'";
            string suggestion = null;

            if (similarMembers != null && similarMembers.Any())
            {
                suggestion = $"Did you mean: {string.Join(", ", similarMembers.Take(3).Select(m => $"'{m}'"))}?";
            }
            else
            {
                suggestion = $"Check the spelling or ensure '{memberName}' is accessible";
            }

            return ErrorFormatter.FormatSemanticError(ErrorCode.BL3011_InvalidMemberAccess, message, line, column,
                sourceCode, suggestion);
        }

        /// <summary>
        /// Format an overload resolution error showing all candidates
        /// </summary>
        public static string FormatOverloadResolutionError(string functionName, IEnumerable<string> argTypes,
            int line, int column, string sourceCode = null, IEnumerable<string> candidates = null)
        {
            var argTypesStr = string.Join(", ", argTypes);
            var message = $"No overload of '{functionName}' matches argument types ({argTypesStr})";
            string suggestion = null;

            if (candidates != null && candidates.Any())
            {
                var sb = new StringBuilder("Available overloads:\n");
                foreach (var candidate in candidates.Take(5))
                {
                    sb.AppendLine($"  {candidate}");
                }
                suggestion = sb.ToString().TrimEnd();
            }

            return ErrorFormatter.FormatSemanticError(ErrorCode.BL3004_WrongNumberOfArguments, message, line, column,
                sourceCode, suggestion);
        }

        /// <summary>
        /// Format a generic constraint error
        /// </summary>
        public static string FormatGenericConstraintError(string typeParam, string constraint, string actualType,
            int line, int column, string sourceCode = null)
        {
            var message = $"Type argument '{actualType}' does not satisfy constraint '{constraint}' for type parameter '{typeParam}'";
            string suggestion = $"Provide a type that implements or inherits from '{constraint}'";

            return ErrorFormatter.FormatSemanticError(ErrorCode.BL3001_TypeMismatch, message, line, column,
                sourceCode, suggestion);
        }

        /// <summary>
        /// Create a quick fix for adding a missing type annotation
        /// </summary>
        public static QuickFix CreateAddTypeAnnotationFix(string variableName, string inferredType,
            int line, int endColumn)
        {
            return new QuickFix(
                $"Add type annotation 'As {inferredType}'",
                $"{variableName} As {inferredType}",
                line, 1, line, endColumn);
        }

        /// <summary>
        /// Create a quick fix for type conversion
        /// </summary>
        public static QuickFix CreateTypeConversionFix(string expression, string fromType, string toType,
            int line, int startColumn, int endColumn)
        {
            string conversionFunc = GetConversionFunction(toType);
            return new QuickFix(
                $"Convert to {toType}",
                $"{conversionFunc}({expression})",
                line, startColumn, line, endColumn);
        }

        /// <summary>
        /// Create a quick fix for missing End block
        /// </summary>
        public static QuickFix CreateAddEndBlockFix(string blockType, int insertLine)
        {
            return new QuickFix(
                $"Add 'End {blockType}'",
                $"End {blockType}",
                insertLine, 1, insertLine, 1);
        }

        private static string GetOperatorSuggestion(string op, string leftType, string rightType)
        {
            // String concatenation
            if (op == "+" && (leftType == "String" || rightType == "String"))
            {
                return "Use '&' for string concatenation, or convert both operands to the same type";
            }

            // Numeric operations
            if (op == "/" && leftType == "Integer" && rightType == "Integer")
            {
                return "Integer division uses '\\'. For decimal result, convert to Double first";
            }

            // Boolean operations
            if ((op == "And" || op == "Or") && (leftType != "Boolean" || rightType != "Boolean"))
            {
                return "Logical operators require Boolean operands. Use CBool() to convert";
            }

            return null;
        }

        private static string GetConversionFunction(string targetType)
        {
            return targetType switch
            {
                "Integer" => "CInt",
                "Long" => "CLng",
                "Single" => "CSng",
                "Double" => "CDbl",
                "String" => "CStr",
                "Boolean" => "CBool",
                "Char" => "CChar",
                _ => $"CType(..., {targetType})"
            };
        }
    }

    /// <summary>
    /// Context tracker for expression evaluation errors
    /// </summary>
    public class ErrorContext
    {
        private readonly Stack<string> _contextStack;
        private readonly HashSet<string> _poisonedSymbols;

        public ErrorContext()
        {
            _contextStack = new Stack<string>();
            _poisonedSymbols = new HashSet<string>();
        }

        public void PushContext(string context)
        {
            _contextStack.Push(context);
        }

        public void PopContext()
        {
            if (_contextStack.Count > 0)
            {
                _contextStack.Pop();
            }
        }

        public string GetCurrentContext()
        {
            if (_contextStack.Count == 0) return null;
            return string.Join(" -> ", _contextStack.Reverse());
        }

        public void PoisonSymbol(string symbolName)
        {
            _poisonedSymbols.Add(symbolName);
        }

        public bool IsSymbolPoisoned(string symbolName)
        {
            return _poisonedSymbols.Contains(symbolName);
        }

        public void ClearPoisonedSymbols()
        {
            _poisonedSymbols.Clear();
        }

        public string FormatErrorWithContext(string message)
        {
            var context = GetCurrentContext();
            if (string.IsNullOrEmpty(context))
                return message;
            return $"{message}\n  while evaluating: {context}";
        }
    }

    /// <summary>
    /// Groups errors by likely common cause
    /// </summary>
    public static class ErrorGrouper
    {
        public static List<ErrorGroup> GroupErrors(IEnumerable<SemanticError> errors)
        {
            var groups = new List<ErrorGroup>();
            var processed = new HashSet<SemanticError>();

            foreach (var error in errors)
            {
                if (processed.Contains(error)) continue;

                var group = new ErrorGroup(error);
                processed.Add(error);

                // Find related errors (same symbol, same line, cascading from undefined)
                foreach (var other in errors)
                {
                    if (processed.Contains(other)) continue;

                    if (AreErrorsRelated(error, other))
                    {
                        group.AddRelatedError(other);
                        processed.Add(other);
                    }
                }

                // Determine common cause
                group.CommonCause = DetermineCommonCause(group);
                groups.Add(group);
            }

            return groups;
        }

        private static bool AreErrorsRelated(SemanticError a, SemanticError b)
        {
            // Same line errors are often related
            if (a.Line == b.Line && Math.Abs(a.Column - b.Column) < 20)
                return true;

            // Undefined symbol followed by type errors on same symbol
            if (a.Message.Contains("Undefined") && b.Message.Contains("type"))
            {
                // Extract symbol names and compare
                var symbolA = ExtractSymbolName(a.Message);
                var symbolB = ExtractSymbolName(b.Message);
                if (!string.IsNullOrEmpty(symbolA) && symbolA == symbolB)
                    return true;
            }

            return false;
        }

        private static string ExtractSymbolName(string message)
        {
            // Extract symbol name from quotes in error message
            var match = System.Text.RegularExpressions.Regex.Match(message, @"'([^']+)'");
            return match.Success ? match.Groups[1].Value : null;
        }

        private static string DetermineCommonCause(ErrorGroup group)
        {
            var primary = group.PrimaryError.Message;

            if (primary.Contains("Undefined symbol") || primary.Contains("Undefined identifier"))
            {
                var symbol = ExtractSymbolName(primary);
                if (group.RelatedErrors.Count > 0)
                {
                    return $"Symbol '{symbol}' is undefined, causing {group.RelatedErrors.Count} cascading error(s)";
                }
            }

            if (primary.Contains("Unknown type"))
            {
                var typeName = ExtractSymbolName(primary);
                return $"Type '{typeName}' is not defined. Check spelling or add an import";
            }

            return null;
        }
    }
}
