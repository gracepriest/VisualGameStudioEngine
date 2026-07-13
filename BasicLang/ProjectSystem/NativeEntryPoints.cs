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
    /// `auto main(...) -> int`, `wmain` and `WinMain` count as candidates only
    /// when they are DEFINITIONS (parameter list followed by `{`, not `;`).
    /// KNOWN LIMITATION: the heuristic does not evaluate the preprocessor, so
    /// mains under mutually exclusive `#if`/`#else` branches all count and can
    /// overcount. That is accepted by design — the BL6012 message names every
    /// candidate file so the user can restructure.
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
        // (declaration) and must be followed by '{' (optionally via a trailing
        // return type for `auto main(...) -> int`). WinMain is matched by name
        // alone because real signatures interpose WINAPI/CALLBACK between the
        // return type and the name.
        private static readonly Regex EntryPointDefinition = new Regex(
            @"(?:\b(?:int|auto)\s+(?:main|wmain)\s*\([^;{)]*\)\s*(?:->\s*int\s*)?\{)" +
            @"|(?:\bWinMain\s*\([^;{)]*\)\s*\{)",
            RegexOptions.Compiled);

        /// <summary>
        /// Counts C++ entry-point definitions (main/wmain/WinMain) in a source
        /// text using the D9 textual heuristic. Line comments, block comments,
        /// string literals (honoring \" and \\ escapes) and char literals are
        /// stripped before matching.
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
                        "BasicLang 'Sub Main' and a C++ 'main'/'wmain'/'WinMain' definition; " +
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
        /// Replaces // and /* */ comments, "..." string literals and '...'
        /// char literals with a single space each so the entry-point regex
        /// only sees real code. Escape handling: a backslash inside a string/
        /// char literal always consumes the next character, so \" stays inside
        /// the literal and "path\\" still terminates at its real closing quote.
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
                    // Line comment: skip to end of line (newline itself is kept).
                    i += 2;
                    while (i < n && source[i] != '\n')
                        i++;
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
                else if (c == '"' || c == '\'')
                {
                    // String or char literal; backslash escapes the next char.
                    var quote = c;
                    i++;
                    while (i < n)
                    {
                        if (source[i] == '\\') { i += 2; continue; }
                        if (source[i] == quote) { i++; break; }
                        i++;
                    }
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
    }
}
