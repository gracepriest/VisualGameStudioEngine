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
        /// Determine the implicit container for a document.
        ///
        /// Compiler parity:
        /// - .mod with an explicit Module header is parsed unwrapped. The
        ///   compiler technically double-wraps it (PreprocessModFile warns and
        ///   wraps anyway), but nested modules parse and compile, so both
        ///   sides accept the file and the flat parse keeps positions exact.
        /// - .cls is ALWAYS wrapped — PreprocessClassFile wraps unconditionally,
        ///   so an explicit Class header nests inside the implicit class and
        ///   the build fails with "Class 'X' is already defined". Wrapping here
        ///   reproduces that exact diagnostic in the editor.
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
                    return ImplicitContainerKind.Class;

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
    }
}
