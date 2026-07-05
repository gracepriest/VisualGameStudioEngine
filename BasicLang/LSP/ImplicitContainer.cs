using System;
using System.IO;
using BasicLang.Compiler.AST;

namespace BasicLang.Compiler.LSP
{
    /// <summary>
    /// The implicit container a document's declarations live in, keyed off the
    /// file extension (compiler parity: .mod files are wrapped in an implicit
    /// Module, .cls/.class files in an implicit Class).
    /// </summary>
    internal enum ImplicitContainerKind
    {
        None,
        Module,
        Class
    }

    /// <summary>
    /// Extension-aware parsing for LSP documents. Unlike the compiler's
    /// preprocessors (which prepend a wrapper LINE to the source text and
    /// shift every line number), the LSP synthesizes the Module/Class wrapper
    /// in the AST so diagnostics and completion positions stay exact.
    /// </summary>
    internal static class ImplicitContainer
    {
        /// <summary>
        /// Determine the implicit container for a document. Returns None when
        /// the file is a regular source file or already carries an explicit
        /// Module/Class header (matching the compiler's checks).
        /// </summary>
        public static ImplicitContainerKind GetKind(string filePath, string content)
        {
            string extension;
            try
            {
                extension = Path.GetExtension(filePath)?.ToLowerInvariant();
            }
            catch
            {
                return ImplicitContainerKind.None;
            }

            switch (extension)
            {
                case ".mod":
                    return StartsWithKeyword(content, "Module")
                        ? ImplicitContainerKind.None
                        : ImplicitContainerKind.Module;

                case ".cls":
                case ".class":
                    return HasExplicitClassHeader(content)
                        ? ImplicitContainerKind.None
                        : ImplicitContainerKind.Class;

                default:
                    return ImplicitContainerKind.None;
            }
        }

        /// <summary>
        /// Parse using the container mode appropriate for the document's
        /// extension. The container name derives from the file name.
        /// </summary>
        public static ProgramNode Parse(Parser parser, string filePath, string content)
        {
            var kind = GetKind(filePath, content);
            if (kind == ImplicitContainerKind.None)
                return parser.Parse();

            string name;
            try
            {
                name = Path.GetFileNameWithoutExtension(filePath);
            }
            catch
            {
                name = null;
            }

            if (string.IsNullOrEmpty(name))
                return parser.Parse();

            return kind == ImplicitContainerKind.Module
                ? parser.ParseAsImplicitModule(name)
                : parser.ParseAsImplicitClass(name);
        }

        private static bool StartsWithKeyword(string content, string keyword)
        {
            var trimmed = (content ?? string.Empty).TrimStart('﻿').TrimStart();
            if (!trimmed.StartsWith(keyword, StringComparison.OrdinalIgnoreCase))
                return false;
            return trimmed.Length == keyword.Length || char.IsWhiteSpace(trimmed[keyword.Length]);
        }

        private static bool HasExplicitClassHeader(string content)
        {
            var trimmed = (content ?? string.Empty).TrimStart('﻿').TrimStart();
            return System.Text.RegularExpressions.Regex.IsMatch(
                trimmed,
                @"^(?:(?:Public|Private|Friend|MustInherit)\s+)*Class\s",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
    }
}
