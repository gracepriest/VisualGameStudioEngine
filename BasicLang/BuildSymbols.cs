using System;
using System.Collections.Generic;
using System.Linq;

namespace BasicLang.Compiler
{
    /// <summary>
    /// ⛔ THE one answer to "which conditional-compilation symbols does this build define" (spec 2026-09-29 §4.1,
    /// O16, O19): <c>WEB</c> for a JavaScript build and <c>DESKTOP</c> for every other backend; <c>DEBUG</c> in a
    /// Debug configuration and <c>RELEASE</c> in a Release one (null or any other name: neither); then the project's
    /// <c>&lt;DefineConstants&gt;</c>, split on ';' or ',', trimmed, a <c>NAME=value</c> entry contributing NAME.
    /// Case-insensitive and de-duplicated, first spelling wins. Called by the <see cref="BasicCompiler"/>
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
            string? targetBackend, string? configuration, IEnumerable<string?>? defineConstants)
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
                    Add(part.Split('=')[0]);
                }
            }

            return symbols;
        }
    }
}
