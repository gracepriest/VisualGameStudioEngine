using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace BasicLang.Compiler.ProjectSystem
{
    /// <summary>One parsed C++ compiler/linker diagnostic.</summary>
    public sealed class CppDiagnostic
    {
        public string FilePath { get; set; }
        public int Line { get; set; }          // 1-based; 0 = no line (linker)
        public int Column { get; set; }        // 1-based; 0 = no column
        public bool IsWarning { get; set; }
        public string Code { get; set; }       // MSVC/linker code, or CPP1001/CPP1002
        public string Message { get; set; }
    }

    /// <summary>
    /// Parses clang/gcc (`file:line:col: severity: msg`), MSVC
    /// (`file(line[,col]): severity CODE: msg`) and MSVC linker
    /// (`file : error LNKnnnn: msg`) diagnostics out of raw toolchain output.
    /// Notes, caret/source-echo lines and banner chatter are skipped.
    /// </summary>
    public static class CppDiagnosticsParser
    {
        public const string GenericErrorCode = "CPP1001";
        public const string GenericWarningCode = "CPP1002";

        // clang/gcc: C:\p\main.cpp:12:5: error: msg   (drive colon is safe: the
        // line/col groups anchor the last two ':'-separated numeric fields)
        private static readonly Regex GccClang = new Regex(
            @"^(?<file>.+?):(?<line>\d+):(?<col>\d+):\s+(?:fatal\s+)?(?<sev>error|warning):\s+(?<msg>.*)$",
            RegexOptions.Compiled);

        // MSVC: main.cpp(5): error C2065: msg   |   main.cpp(7,12): warning C4189: msg
        private static readonly Regex Msvc = new Regex(
            @"^(?<file>.+?)\((?<line>\d+)(?:,(?<col>\d+))?\)\s*:\s*(?:fatal\s+)?(?<sev>error|warning)\s+(?<code>[A-Z]+\d+)\s*:\s*(?<msg>.*)$",
            RegexOptions.Compiled);

        // MSVC linker: main.obj : error LNK2019: msg. The optional drive spec
        // lets link.exe's absolute output paths (LNK1120/LNK1104) match; the
        // '('-excluding class still rejects banner lines like
        // "Microsoft (R) C/C++ Optimizing Compiler".
        private static readonly Regex Linker = new Regex(
            @"^(?<file>(?:[A-Za-z]:)?[^:(]+?)\s*:\s*(?:fatal\s+)?error\s+(?<code>LNK\d+)\s*:\s*(?<msg>.*)$",
            RegexOptions.Compiled);

        public static List<CppDiagnostic> Parse(string toolchainOutput, string workingDirectory)
        {
            var result = new List<CppDiagnostic>();
            if (string.IsNullOrEmpty(toolchainOutput))
                return result;

            foreach (var raw in toolchainOutput.Split('\n'))
            {
                var line = raw.TrimEnd('\r');

                var m = Msvc.Match(line);
                if (m.Success)
                {
                    // TryParse: a pathological line/col (bigger than Int32)
                    // means this isn't a real diagnostic — skip the line
                    // rather than throw and discard everything.
                    if (!int.TryParse(m.Groups["line"].Value, out var msvcLine))
                        continue;
                    var msvcCol = 0;
                    if (m.Groups["col"].Success && !int.TryParse(m.Groups["col"].Value, out msvcCol))
                        continue;
                    result.Add(new CppDiagnostic
                    {
                        FilePath = Absolutize(m.Groups["file"].Value.Trim(), workingDirectory),
                        Line = msvcLine,
                        Column = msvcCol,
                        IsWarning = m.Groups["sev"].Value == "warning",
                        Code = m.Groups["code"].Value,
                        Message = m.Groups["msg"].Value.Trim()
                    });
                    continue;
                }

                m = GccClang.Match(line);
                if (m.Success)
                {
                    if (!int.TryParse(m.Groups["line"].Value, out var gccLine) ||
                        !int.TryParse(m.Groups["col"].Value, out var gccCol))
                        continue;
                    var isWarning = m.Groups["sev"].Value == "warning";
                    result.Add(new CppDiagnostic
                    {
                        FilePath = Absolutize(m.Groups["file"].Value.Trim(), workingDirectory),
                        Line = gccLine,
                        Column = gccCol,
                        IsWarning = isWarning,
                        Code = isWarning ? GenericWarningCode : GenericErrorCode,
                        Message = m.Groups["msg"].Value.Trim()
                    });
                    continue;
                }

                m = Linker.Match(line);
                if (m.Success)
                {
                    result.Add(new CppDiagnostic
                    {
                        FilePath = Absolutize(m.Groups["file"].Value.Trim(), workingDirectory),
                        Line = 0,
                        Column = 0,
                        IsWarning = false,
                        Code = m.Groups["code"].Value,
                        Message = m.Groups["msg"].Value.Trim()
                    });
                }
            }

            return result;
        }

        /// <summary>
        /// MSBuild-style single line: `path(line,col): error CODE: message` —
        /// the format the IDE Output panel's click-to-navigate regex matches.
        /// </summary>
        public static string FormatNormalized(CppDiagnostic d)
        {
            var kind = d.IsWarning ? "warning" : "error";
            var location = d.Line > 0
                ? (d.Column > 0 ? $"{d.FilePath}({d.Line},{d.Column})" : $"{d.FilePath}({d.Line})")
                : d.FilePath;
            return $"{location}: {kind} {d.Code}: {d.Message}";
        }

        private static string Absolutize(string file, string workingDirectory)
        {
            try
            {
                if (!Path.IsPathRooted(file) && !string.IsNullOrEmpty(workingDirectory))
                    return Path.Combine(workingDirectory, file);
            }
            catch (ArgumentException) { /* illegal chars — keep as-is */ }
            return file;
        }
    }
}
