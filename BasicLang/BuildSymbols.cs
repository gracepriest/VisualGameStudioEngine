using System;
using System.Collections.Generic;
using System.Linq;

namespace BasicLang.Compiler
{
    /// <summary>
    /// ⛔ THE one answer to "which conditional-compilation symbols does this build define" (spec 2026-09-29 §4.1,
    /// O16, O19): <c>WEB</c> for a JavaScript build and <c>DESKTOP</c> for every other backend; <c>DEBUG</c> in a
    /// Debug configuration and <c>RELEASE</c> in a Release one (null or any other name: neither); then the project's
    /// <c>&lt;DefineConstants&gt;</c>, split on ';' or ',', trimmed. A <c>NAME=value</c> entry follows VB:
    /// <c>False</c>/<c>0</c> means NOT defined (and un-defines an earlier definition — the last word wins),
    /// <c>True</c>/<c>-1</c>/<c>1</c>/no value defines NAME, and any other value defines NAME with a warning that
    /// names the entry. An entry naming WEB or DESKTOP (with or without a value) is ignored with a warning — the
    /// target decides those, never DefineConstants. Case-insensitive and de-duplicated, first spelling wins. Called by the <see cref="BasicCompiler"/>
    /// constructor (every build route) and the LSP (Task 6) — never re-derived.
    /// </summary>
    public static class BuildSymbols
    {
        public const string Web = "WEB";
        public const string Desktop = "DESKTOP";
        public const string Debug = "DEBUG";
        public const string Release = "RELEASE";

        public static bool IsWebBackend(string? targetBackend) =>
            targetBackend?.Trim().ToLowerInvariant() is "javascript" or "js";

        public static IReadOnlyList<string> For(
            string? targetBackend, string? configuration, IEnumerable<string?>? defineConstants,
            ICollection<string>? warnings = null)
        {
            var symbols = new List<string>();
            void Add(string? name)
            {
                name = name?.Trim();
                if (string.IsNullOrEmpty(name)) return;
                if (symbols.Any(s => string.Equals(s, name, StringComparison.OrdinalIgnoreCase))) return;
                symbols.Add(name);
            }

            Add(IsWebBackend(targetBackend) ? Web : Desktop);

            var config = configuration?.Trim();
            if (string.Equals(config, "Debug", StringComparison.OrdinalIgnoreCase)) Add(Debug);
            else if (string.Equals(config, "Release", StringComparison.OrdinalIgnoreCase)) Add(Release);

            foreach (var entry in defineConstants ?? Enumerable.Empty<string>())
            {
                foreach (var part in (entry ?? "").Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var equals = part.IndexOf('=');
                    var name = (equals < 0 ? part : part.Substring(0, equals)).Trim();
                    if (name.Length == 0) continue;

                    // ⛔ WEB and DESKTOP are facts about the TARGET, never settable here: a web build with
                    // WEB=False would take the #If DESKTOP code and break. Ignored, with a warning.
                    if (string.Equals(name, Web, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(name, Desktop, StringComparison.OrdinalIgnoreCase))
                    {
                        warnings?.Add($"DefineConstants entry '{part.Trim()}' is ignored: " +
                            "WEB/DESKTOP are set by the build target and can't be changed in DefineConstants.");
                        continue;
                    }

                    switch (DefineValue(equals < 0 ? null : part.Substring(equals + 1)))
                    {
                        case Defined.No:
                            // VB: NAME=False is "not defined" — and the last word wins, so it also
                            // un-defines what the configuration or an earlier entry defined.
                            symbols.RemoveAll(s => string.Equals(s, name, StringComparison.OrdinalIgnoreCase));
                            break;
                        case Defined.Unrecognised:
                            warnings?.Add($"DefineConstants entry '{part.Trim()}': '{name}' is defined, but its value " +
                                "is not True/False/-1/1/0 — conditional compilation only tests whether a symbol is defined.");
                            Add(name);
                            break;
                        default:
                            Add(name);
                            break;
                    }
                }
            }

            return symbols;
        }

        private enum Defined { Yes, No, Unrecognised }

        /// <summary>No value, True, -1 or 1 define the symbol; False or 0 do not (VB); anything else defines it
        /// and is reported. Trimmed and case-insensitive.</summary>
        private static Defined DefineValue(string? value)
        {
            var v = value?.Trim();
            if (string.IsNullOrEmpty(v)) return Defined.Yes;
            if (string.Equals(v, "False", StringComparison.OrdinalIgnoreCase) || v == "0") return Defined.No;
            if (string.Equals(v, "True", StringComparison.OrdinalIgnoreCase) || v == "-1" || v == "1") return Defined.Yes;
            return Defined.Unrecognised;
        }
    }
}
