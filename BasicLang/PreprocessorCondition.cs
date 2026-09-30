using System;
using System.Collections.Generic;

namespace BasicLang.Compiler
{
    /// <summary>
    /// The condition of <c>#If … Then</c> / <c>#ElseIf … Then</c> (spec 2026-09-29 §4.1): defined-symbol names,
    /// <c>Not</c>, <c>And</c>/<c>AndAlso</c>, <c>Or</c>/<c>OrElse</c>, parentheses, <c>True</c>/<c>False</c>.
    /// VB precedence: Not binds tightest, then And, then Or. No values, no comparisons — a symbol is defined or
    /// it is not. A PURE function: the preprocessor's compile mode and editor mode both call it.
    /// </summary>
    internal static class PreprocessorCondition
    {
        public static bool TryEvaluate(string text, Func<string, bool> isDefined, out bool value, out string error)
        {
            value = false;
            var tokens = Tokenize(text ?? "", out error);
            if (tokens == null) return false;

            var pos = 0;
            try
            {
                value = ParseOr(tokens, ref pos, isDefined);
                if (pos != tokens.Count)
                {
                    error = $"unexpected '{tokens[pos]}'";
                    return false;
                }
                return true;
            }
            catch (FormatException ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static List<string> Tokenize(string text, out string error)
        {
            error = null;
            var tokens = new List<string>();
            var i = 0;
            while (i < text.Length)
            {
                var c = text[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (c == '(' || c == ')') { tokens.Add(c.ToString()); i++; continue; }
                if (char.IsLetter(c) || c == '_')
                {
                    var start = i;
                    while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                    tokens.Add(text.Substring(start, i - start));
                    continue;
                }
                error = $"unexpected character '{c}'";
                return null;
            }
            if (tokens.Count == 0)
            {
                error = "the condition is empty";
                return null;
            }
            return tokens;
        }

        private static bool Is(List<string> t, int pos, string word) =>
            pos < t.Count && string.Equals(t[pos], word, StringComparison.OrdinalIgnoreCase);

        private static bool ParseOr(List<string> t, ref int pos, Func<string, bool> d)
        {
            var value = ParseAnd(t, ref pos, d);
            while (Is(t, pos, "Or") || Is(t, pos, "OrElse"))
            {
                pos++;
                var right = ParseAnd(t, ref pos, d);   // always parsed: a malformed right side is an error either way
                value = value || right;
            }
            return value;
        }

        private static bool ParseAnd(List<string> t, ref int pos, Func<string, bool> d)
        {
            var value = ParseNot(t, ref pos, d);
            while (Is(t, pos, "And") || Is(t, pos, "AndAlso"))
            {
                pos++;
                var right = ParseNot(t, ref pos, d);
                value = value && right;
            }
            return value;
        }

        private static bool ParseNot(List<string> t, ref int pos, Func<string, bool> d)
        {
            if (Is(t, pos, "Not"))
            {
                pos++;
                return !ParseNot(t, ref pos, d);
            }
            return ParsePrimary(t, ref pos, d);
        }

        private static readonly HashSet<string> Keywords =
            new(StringComparer.OrdinalIgnoreCase) { "And", "AndAlso", "Or", "OrElse", "Not", "Then" };

        private static bool ParsePrimary(List<string> t, ref int pos, Func<string, bool> d)
        {
            if (pos >= t.Count) throw new FormatException("the condition ends too early");
            var token = t[pos++];
            if (token == "(")
            {
                var inner = ParseOr(t, ref pos, d);
                if (!Is(t, pos, ")")) throw new FormatException("missing ')'");
                pos++;
                return inner;
            }
            if (token == ")") throw new FormatException("unexpected ')'");
            if (string.Equals(token, "True", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(token, "False", StringComparison.OrdinalIgnoreCase)) return false;
            if (Keywords.Contains(token)) throw new FormatException($"unexpected '{token}'");
            return d(token);
        }
    }
}
