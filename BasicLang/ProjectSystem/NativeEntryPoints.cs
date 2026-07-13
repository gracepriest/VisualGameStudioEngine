using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace BasicLang.Compiler.ProjectSystem
{
    /// <summary>
    /// Cross-language entry-point analysis for mixed BasicLang/C++ native
    /// projects. Enforces the Phase 2 rule PRE-LINK (linker errors for the
    /// 0-main / 2-main cases don't parse into clickable diagnostics):
    /// an Exe project must have exactly ONE entry point across both languages
    /// (BL6011 on zero, BL6012 on more than one); a Library project must have
    /// ZERO (BL6013).
    ///
    /// Design decision D9: C++ entry points are found by a comment/string-
    /// stripped TEXTUAL heuristic, not a real parse. `int main(...)`,
    /// `auto main(...) -> int`, `wmain`, `WinMain` and `wWinMain` count as
    /// candidates only when they are DEFINITIONS: the parameter list must be
    /// followed by an optional trailing return type (`-> int`), an optional
    /// `noexcept`/`noexcept(...)` specifier, an optional function-try-block
    /// `try`, and then `{` — never just `;`.
    ///
    /// KNOWN LIMITATIONS (accepted by design, not bugs):
    /// - The heuristic does not evaluate the preprocessor, so mains under
    ///   mutually exclusive `#if`/`#else` branches all count and can
    ///   overcount. The BL6012 message names every candidate file so the
    ///   user can restructure.
    /// - Digit separators (`10'000`, `0xFF'FF`, `1'000'000`) are told apart
    ///   from char-literal quotes by a local rule: a `'` is a separator iff
    ///   BOTH its immediately preceding and following characters are hex
    ///   digits (`[0-9a-fA-F]`); otherwise it opens a char literal. This
    ///   misreads a `u8'a'`-style UTF-8 char literal whose sole content
    ///   character is itself a hex letter (prev `8`, next `a`, both hex
    ///   digits) as a digit separator instead of a char literal —
    ///   astronomically rare in practice, not worth extra state to special-case.
    /// - Raw string literals `R"delim(...)delim"` (optionally preceded by a
    ///   `u8`/`L`/`u`/`U` encoding prefix) are recognized as a unit and their
    ///   entire span is stripped, so embedded quotes/braces inside them never
    ///   desync the scan or get mistaken for real code.
    /// </summary>
    public static class NativeEntryPoints
    {
        /// <summary>Exe project has no entry point in either language.</summary>
        public const string NoEntryPointCode = "BL6011";
        /// <summary>Exe project has more than one entry point across both languages.</summary>
        public const string MultipleEntryPointsCode = "BL6012";
        /// <summary>Library project defines an entry point.</summary>
        public const string LibraryEntryPointCode = "BL6013";

        // Entry-point DEFINITIONS only: the parameter list must not cross a ';'
        // (declaration) and must be followed by '{', optionally via a trailing
        // return type (`auto main(...) -> int`), a `noexcept`/`noexcept(...)`
        // specifier, and/or a function-try-block `try`. WinMain/wWinMain are
        // matched by name alone because real signatures interpose WINAPI/
        // CALLBACK between the return type and the name.
        private const string EntryPointTail =
            @"\s*(?:->\s*int\s*)?(?:noexcept(?:\s*\([^)]*\))?\s*)?(?:try\s*)?\{";

        private static readonly Regex EntryPointDefinition = new Regex(
            @"(?:\b(?:int|auto)\s+(?:main|wmain)\s*\([^;{)]*\)" + EntryPointTail + ")" +
            @"|(?:\b(?:w?WinMain)\s*\([^;{)]*\)" + EntryPointTail + ")",
            RegexOptions.Compiled);

        /// <summary>
        /// Counts C++ entry-point definitions (main/wmain/WinMain/wWinMain) in
        /// a source text using the D9 textual heuristic. Line comments (with
        /// backslash-newline splicing), block comments, string literals
        /// (honoring \" and \\ escapes), raw string literals (`R"(...)"` and
        /// prefixed forms), char literals and C++14 digit separators are all
        /// resolved before matching.
        /// </summary>
        public static int CountCppMains(string source)
        {
            if (string.IsNullOrEmpty(source))
                return 0;
            var stripped = StripCommentsAndLiterals(source);
            return EntryPointDefinition.Matches(stripped).Count;
        }

        /// <summary>
        /// Applies the entry-point rule and returns the resulting diagnostics
        /// (empty when the project is well-formed). <paramref name="cppMains"/>
        /// is one (file, main-count) pair per scanned C++ source; zero-count
        /// entries are ignored. Candidate order in messages is deterministic:
        /// BasicLang's Main first, then C++ files in caller-supplied order.
        /// </summary>
        public static List<CppDiagnostic> Apply(bool isExe, int basicLangMainCount,
            List<(string file, int count)> cppMains)
        {
            var diagnostics = new List<CppDiagnostic>();
            var cppCandidates = (cppMains ?? new List<(string file, int count)>())
                .Where(m => m.count > 0)
                .ToList();
            var total = basicLangMainCount + cppCandidates.Sum(m => m.count);

            if (isExe)
            {
                if (total == 0)
                {
                    diagnostics.Add(MakeError(NoEntryPointCode, string.Empty,
                        "No entry point found for Exe project. Searched every source file for a " +
                        "BasicLang 'Sub Main' and a C++ 'main'/'wmain'/'WinMain'/'wWinMain' definition; " +
                        "define exactly one, or set OutputType to Library."));
                }
                else if (total > 1)
                {
                    diagnostics.Add(MakeError(MultipleEntryPointsCode, FirstCppFile(cppCandidates),
                        $"Multiple entry points found ({total}), but an Exe project must have exactly one: " +
                        $"{DescribeCandidates(basicLangMainCount, cppCandidates)}. Remove all but one."));
                }
            }
            else if (total > 0)
            {
                diagnostics.Add(MakeError(LibraryEntryPointCode, FirstCppFile(cppCandidates),
                    $"Library project must not define an entry point, but found: " +
                    $"{DescribeCandidates(basicLangMainCount, cppCandidates)}. " +
                    "Remove it, or set OutputType to Exe."));
            }

            return diagnostics;
        }

        private static CppDiagnostic MakeError(string code, string filePath, string message)
            => new CppDiagnostic
            {
                FilePath = filePath,
                Line = 0,
                Column = 0,
                IsWarning = false,
                Code = code,
                Message = message
            };

        // The first C++ candidate's file gives the Error List a clickable
        // target; pure-BasicLang violations have no single file, so empty.
        private static string FirstCppFile(List<(string file, int count)> cppCandidates)
            => cppCandidates.Count > 0 ? cppCandidates[0].file : string.Empty;

        private static string DescribeCandidates(int basicLangMainCount,
            List<(string file, int count)> cppCandidates)
        {
            var parts = new List<string>();
            for (var i = 0; i < basicLangMainCount; i++)
                parts.Add("Main (BasicLang)");
            foreach (var (file, count) in cppCandidates)
                parts.Add(count == 1 ? $"{file}: main" : $"{file}: main (x{count})");
            return string.Join(", ", parts);
        }

        /// <summary>
        /// Replaces // and /* */ comments, "..." string literals, raw string
        /// literals (`R"delim(...)delim"`, with optional `u8`/`L`/`u`/`U`
        /// encoding prefix) and '...' char literals with a single space each,
        /// and skips over C++14 digit separators (`10'000`), so the
        /// entry-point regex only sees real code. Escape handling: a backslash
        /// inside a string/char literal always consumes the next character, so
        /// \" stays inside the literal and "path\\" still terminates at its
        /// real closing quote. A trailing backslash at the very end of a //
        /// line splices the next physical line into the comment (C++
        /// translation phase 2), hiding whatever follows it too.
        /// </summary>
        private static string StripCommentsAndLiterals(string source)
        {
            var sb = new StringBuilder(source.Length);
            var i = 0;
            var n = source.Length;

            while (i < n)
            {
                var c = source[i];

                if (c == '/' && i + 1 < n && source[i + 1] == '/')
                {
                    // Line comment: skip to end of line. A trailing backslash
                    // immediately before the newline (translation phase 2)
                    // splices the next physical line into the comment too.
                    i += 2;
                    while (true)
                    {
                        while (i < n && source[i] != '\n')
                            i++;
                        if (i >= n)
                            break;
                        var spliceCheck = i - 1;
                        if (spliceCheck >= 0 && source[spliceCheck] == '\r')
                            spliceCheck--;
                        if (spliceCheck >= 0 && source[spliceCheck] == '\\')
                        {
                            i++; // consume the newline, keep the comment going
                            continue;
                        }
                        break;
                    }
                    sb.Append(' ');
                }
                else if (c == '/' && i + 1 < n && source[i + 1] == '*')
                {
                    // Block comment (possibly spanning lines; unterminated = rest of file).
                    i += 2;
                    while (i + 1 < n && !(source[i] == '*' && source[i + 1] == '/'))
                        i++;
                    i = Math.Min(n, i + 2);
                    sb.Append(' ');
                }
                else if (c == '"')
                {
                    // String literal; backslash escapes the next char.
                    i = ConsumeQuotedLiteral(source, i, n, '"');
                    sb.Append(' ');
                }
                else if (c == '\'')
                {
                    if (IsDigitSeparator(source, i, n))
                    {
                        // C++14 digit separator (e.g. 10'000), not a char-literal opener.
                        sb.Append(c);
                        i++;
                    }
                    else
                    {
                        // Char literal; backslash escapes the next char.
                        i = ConsumeQuotedLiteral(source, i, n, '\'');
                        sb.Append(' ');
                    }
                }
                else if ((c == 'R' || c == 'L' || c == 'u' || c == 'U') &&
                         TryMatchRawStringPrefix(source, i, n, out var rawPrefixLength))
                {
                    // Raw string literal: strip the whole delimiter-matched
                    // span so embedded quotes/braces never desync the scan.
                    i += rawPrefixLength; // now at the opening '"'
                    i++;                  // skip it
                    var delimStart = i;
                    while (i < n && source[i] != '(')
                        i++;
                    var delimiter = source.Substring(delimStart, i - delimStart);
                    if (i < n)
                    {
                        i++; // skip '('
                        var closeSeq = ")" + delimiter + "\"";
                        var closeIdx = source.IndexOf(closeSeq, i, StringComparison.Ordinal);
                        i = closeIdx >= 0 ? closeIdx + closeSeq.Length : n;
                    }
                    // else: no '(' at all before EOF — already consumed to n.
                    sb.Append(' ');
                }
                else
                {
                    sb.Append(c);
                    i++;
                }
            }

            return sb.ToString();
        }

        // Consumes a "..." or '...' literal starting at the opening quote and
        // returns the index just past its closing quote (or n at EOF). A
        // backslash always consumes the next character, so \" / \\ don't
        // terminate the literal early.
        private static int ConsumeQuotedLiteral(string source, int i, int n, char quote)
        {
            i++; // skip opening quote
            while (i < n)
            {
                if (source[i] == '\\') { i += 2; continue; }
                if (source[i] == quote) { i++; break; }
                i++;
            }
            return i;
        }

        private static bool IsHexDigit(char c) =>
            (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

        // A `'` is a C++14 digit separator (not a char-literal opener) iff BOTH
        // neighbors are hex digits — e.g. 10'000, 0xFF'FF, 1'000'000. Known
        // miss (accepted, see class remarks): u8'a' reads as a separator too.
        private static bool IsDigitSeparator(string source, int i, int n) =>
            i > 0 && i + 1 < n && IsHexDigit(source[i - 1]) && IsHexDigit(source[i + 1]);

        // Matches an (optional encoding prefix +) raw-string opener `R"` in
        // code context — R", LR", uR", UR", u8R". Returns the length of the
        // prefix up to and including 'R' (the caller still skips the quote
        // itself). Requires a non-identifier character (or start of input)
        // immediately before the prefix so this never fires inside a longer
        // identifier that merely ends in R/L/u/U (e.g. "URL").
        private static bool TryMatchRawStringPrefix(string source, int i, int n, out int prefixLength)
        {
            prefixLength = 0;
            if (i > 0 && (char.IsLetterOrDigit(source[i - 1]) || source[i - 1] == '_'))
                return false;

            var p = i;
            if (p + 1 < n && source[p] == 'u' && source[p + 1] == '8')
                p += 2;
            else if (p < n && (source[p] == 'L' || source[p] == 'u' || source[p] == 'U'))
                p += 1;

            if (p < n && source[p] == 'R' && p + 1 < n && source[p + 1] == '"')
            {
                prefixLength = p - i + 1;
                return true;
            }
            return false;
        }
    }
}
