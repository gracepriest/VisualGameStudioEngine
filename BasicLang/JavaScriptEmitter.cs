using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace BasicLang.Compiler.CodeGen.JavaScript
{
    /// <summary>
    /// Writes generated JavaScript to disk as something a browser can actually open.
    ///
    /// <para><b>Why an emitter exists at all for this backend and no other.</b>
    /// <c>ICodeGenerator.Generate</c> returns ONE string, and every other backend needs
    /// exactly one file, so each driver writes it inline with a bare
    /// <c>File.WriteAllText</c>. A web target needs three — the script, a page that loads it
    /// and (task 26) a source map — so the single-string contract has to widen somewhere.
    /// Widening <c>ICodeGenerator</c> would force C#/C++/LLVM/MSIL to answer a question none
    /// of them has; widening here does not.</para>
    ///
    /// <para>Shaped after <c>NetProxyEmitter.WriteTo</c>, the repo's existing precedent for
    /// "compute content, then write a directory of it".</para>
    /// </summary>
    public static class JavaScriptEmitter
    {
        public const string DefaultScriptName = "app.js";
        public const string HarnessName = "index.html";

        /// <summary>
        /// Writes <paramref name="javaScript"/> as <paramref name="scriptFileName"/> into
        /// <paramref name="outputDirectory"/>, plus an <c>index.html</c> that loads it and,
        /// when supplied, a <c>.map</c> beside it. Returns every path actually written.
        /// </summary>
        /// <param name="scriptFileName">
        /// File name only, never a path. The project route names its output after the
        /// assembly (<c>MyGame.js</c>), so the harness cannot hardcode <c>app.js</c>.
        /// </param>
        /// <param name="title">Document title; defaults to the script's base name.</param>
        /// <param name="sourceMapJson">
        /// Optional Source Map v3 document. When null NO map is written and NO
        /// <c>sourceMappingURL</c> comment is appended — an unconditional comment pointing at
        /// a file that does not exist makes devtools log a 404 on every page load.
        /// </param>
        /// <param name="jsImports">
        /// Module specifiers from <c>#JsImport</c>. A RELATIVE one names a file the user owns,
        /// and the emitted <c>import "./helper.js"</c> resolves against the SCRIPT's URL — so
        /// unless the module travels with the script the browser 404s on it. The project routes
        /// write into <c>bin/…</c> while the module stays in the project directory, which is
        /// exactly that case: without this the feature is broken in every real project while
        /// every test that hand-places the file passes.
        ///
        /// <para>Bare specifiers (<c>"lodash"</c>) and URLs are package-manager territory — a
        /// stated non-goal — and are left completely alone.</para>
        /// </param>
        /// <param name="importBaseDirectory">
        /// What a relative specifier resolves against: the project directory for the project
        /// routes, the source file's directory for the single-file one. <c>IRModule.JsImports</c>
        /// is a flat list with no per-file origin, so no finer base exists — per-file resolution
        /// would need the LIST to change shape first. Defaults to
        /// <paramref name="outputDirectory"/>.
        /// </param>
        /// <param name="warn">
        /// Receives one message per import that could not be copied. A missing module is NOT a
        /// build failure: the user may be serving it from elsewhere, or about to add it, and
        /// failing the build over a file the compiler never reads would be an overreach.
        /// </param>
        public static IReadOnlyList<string> Emit(
            string outputDirectory,
            string scriptFileName,
            string javaScript,
            string title = null,
            string sourceMapJson = null,
            IReadOnlyList<BasicLang.Compiler.IR.JsImportDirective> jsImports = null,
            string importBaseDirectory = null,
            Action<string> warn = null)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory))
                throw new ArgumentException("An output directory is required.", nameof(outputDirectory));
            if (string.IsNullOrWhiteSpace(scriptFileName))
                throw new ArgumentException("A script file name is required.", nameof(scriptFileName));

            Directory.CreateDirectory(outputDirectory);

            var written = new List<string>();
            var mapFileName = scriptFileName + ".map";

            var script = javaScript ?? string.Empty;
            if (sourceMapJson != null)
            {
                var mapPath = Path.Combine(outputDirectory, mapFileName);
                File.WriteAllText(mapPath, sourceMapJson);
                written.Add(mapPath);

                if (script.Length > 0 && !script.EndsWith("\n", StringComparison.Ordinal))
                    script += "\n";
                script += "//# sourceMappingURL=" + mapFileName + "\n";
            }

            var scriptPath = Path.Combine(outputDirectory, scriptFileName);
            File.WriteAllText(scriptPath, script);
            written.Add(scriptPath);

            // ⛔ NEVER overwrite the harness. The single-file CLI route writes its output
            // NEXT TO THE SOURCE FILE, which is precisely where a hand-authored index.html
            // lives — so clobbering it would destroy the user's own page on every build. The
            // script is different: it is build output and is always replaced.
            var harnessPath = Path.Combine(outputDirectory, HarnessName);
            if (!File.Exists(harnessPath))
            {
                File.WriteAllText(harnessPath, Harness(scriptFileName, title));
                written.Add(harnessPath);
            }

            CopyImportedModules(jsImports, outputDirectory,
                importBaseDirectory ?? outputDirectory, warn, written);

            WriteModulePackageJson(jsImports, outputDirectory, written);

            return written;
        }

        /// <summary>
        /// Declares the output directory an ES module scope, so <c>node Site.js</c> works.
        ///
        /// <para><b>Only when the script actually has imports, and only when absent.</b> The
        /// browser never needs this — <c>type="module"</c> on the script tag settles it there —
        /// but Node parses a <c>.js</c> file as CommonJS unless told otherwise, and an
        /// <c>import</c> statement then dies with "Cannot use import statement outside a
        /// module". Automatic detection only became Node's default in 22.7, so without this the
        /// emitted site runs or does not depending on the reader's Node version.</para>
        ///
        /// <para>Gated on <paramref name="jsImports"/> so a program with no imports leaves the
        /// user's directory exactly as it was — which matters because the single-file route
        /// emits NEXT TO THE SOURCE. And never overwritten, for the same reason
        /// <c>index.html</c> is not: a real <c>package.json</c> there belongs to the user.</para>
        /// </summary>
        private static void WriteModulePackageJson(
            IReadOnlyList<BasicLang.Compiler.IR.JsImportDirective> jsImports,
            string outputDirectory, List<string> written)
        {
            if (jsImports == null || jsImports.Count == 0) return;

            var path = Path.Combine(outputDirectory, "package.json");
            if (File.Exists(path)) return;

            File.WriteAllText(path, "{\n  \"type\": \"module\"\n}\n");
            written.Add(path);
        }

        /// <summary>
        /// Copies every RELATIVE <c>#JsImport</c> target beside the emitted script, preserving
        /// its sub-path so the specifier the generator emitted still resolves.
        ///
        /// <para><b>Here, not at the call sites.</b> There are THREE routes into
        /// <see cref="Emit"/> — CLI single-file, CLI project, and the IDE's BuildService — and
        /// this repo has already paid for a decision duplicated across backend dispatch maps
        /// where one arm went missing and the build silently produced the wrong thing. One copy
        /// covers all three, and a fourth route gets it for free.</para>
        /// </summary>
        private static void CopyImportedModules(
            IReadOnlyList<BasicLang.Compiler.IR.JsImportDirective> jsImports,
            string outputDirectory, string baseDirectory, Action<string> warn, List<string> written)
        {
            if (jsImports == null || jsImports.Count == 0) return;

            var outputRoot = Path.GetFullPath(outputDirectory);
            var seen = new HashSet<string>(StringComparer.Ordinal);

            // ⛔ By SPECIFIER, never by the whole directive. Two clauses importing different
            // names from one module are two imports to emit but ONE file to copy — deduping on
            // the directive would copy it twice, and the second copy would race the first.
            foreach (var specifier in jsImports.Select(i => i.Specifier))
            {
                if (!IsRelativeFileSpecifier(specifier)) continue;
                if (!seen.Add(specifier)) continue;   // two files may import the same module

                // ⛔ Containment first, before touching the filesystem. `../shared/util.js` is a
                // legal ES specifier that resolves ABOVE the output directory — copying it would
                // write outside the build output and could overwrite a source file. Reuses the
                // repo's one containment predicate (rooted paths, both separators, and the
                // directory-boundary comparison that stops `/out_evil` counting as inside
                // `/out`) rather than growing a second, subtly different one.
                if (!BasicLang.Runtime.SafeZip.IsWithin(outputRoot, specifier))
                {
                    warn?.Invoke($"#JsImport \"{specifier}\" was not copied — it resolves outside " +
                                 "the output directory. Move the module below the project " +
                                 "directory, or serve it yourself.");
                    continue;
                }

                var relative = specifier.Replace('/', Path.DirectorySeparatorChar);
                var source = Path.GetFullPath(Path.Combine(baseDirectory, relative));
                var destination = Path.GetFullPath(Path.Combine(outputRoot, relative));

                if (!File.Exists(source))
                {
                    warn?.Invoke($"#JsImport \"{specifier}\" was not found at '{source}' — the " +
                                 "emitted import will fail to load unless the module is served " +
                                 "from somewhere else.");
                    continue;
                }

                // The single-file route writes its output NEXT TO THE SOURCE, so base and output
                // are the same directory and the module is already where it needs to be.
                // File.Copy throws IOException when told to copy a file over itself.
                if (string.Equals(source, destination, PathComparison)) continue;

                var destinationDir = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(destinationDir)) Directory.CreateDirectory(destinationDir);

                File.Copy(source, destination, overwrite: true);
                written.Add(destination);
            }
        }

        /// <summary>
        /// True for a specifier that names a FILE this build owns — the only kind worth copying.
        ///
        /// <para>ES module specifier rules, not path rules: a leading <c>./</c> or <c>../</c>
        /// with FORWARD slashes is what a browser treats as relative. A bare name is a package,
        /// a leading <c>/</c> is server-root-absolute, and anything with a scheme or a leading
        /// <c>//</c> is a URL — all of them the user's business, none of them ours. Windows
        /// <c>.\</c> is deliberately NOT accepted: a browser would not resolve it either, so
        /// accepting it here would copy a file for an import that still cannot load.</para>
        /// </summary>
        private static bool IsRelativeFileSpecifier(string specifier)
        {
            if (string.IsNullOrWhiteSpace(specifier)) return false;
            if (specifier.StartsWith("//", StringComparison.Ordinal)) return false;
            if (specifier.Contains("://", StringComparison.Ordinal)) return false;

            return specifier.StartsWith("./", StringComparison.Ordinal)
                || specifier.StartsWith("../", StringComparison.Ordinal);
        }

        /// <summary>
        /// Windows and macOS compare paths case-insensitively; an ordinal test there would call
        /// two spellings of the same file different and try to copy it over itself.
        /// </summary>
        private static StringComparison PathComparison =>
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        /// <summary>
        /// <c>type="module"</c> is REQUIRED on the tag, and stays required either way.
        ///
        /// <para>The generated JavaScript ends in a bare call to <c>Main()</c> and may now
        /// contain real <c>import</c> statements, emitted from <c>#JsImport</c> directives. A
        /// program with no imports still has nothing in its TEXT marking it as a module, so
        /// the attribute is what supplies module semantics; a program WITH imports would fail
        /// to parse as a classic script. Both roads lead here. (The execution tests get the
        /// same effect by naming their temp file <c>.mjs</c>.)</para>
        /// </summary>
        private static string Harness(string scriptFileName, string title)
        {
            var name = Escape(title ?? Path.GetFileNameWithoutExtension(scriptFileName));
            var src = Escape(scriptFileName);

            var html = new StringBuilder();
            html.Append("<!DOCTYPE html>\n");
            html.Append("<html lang=\"en\">\n");
            html.Append("<head>\n");
            html.Append("  <meta charset=\"utf-8\">\n");
            html.Append("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
            html.Append($"  <title>{name}</title>\n");
            html.Append("</head>\n");
            html.Append("<body>\n");
            html.Append("  <!-- Generated by the BasicLang JavaScript backend. This file is created\n");
            html.Append("       only when absent, so it is safe to edit — rebuilding will not\n");
            html.Append("       overwrite it. Console output goes to the browser devtools console. -->\n");
            html.Append($"  <script type=\"module\" src=\"{src}\"></script>\n");
            html.Append("</body>\n");
            html.Append("</html>\n");
            return html.ToString();
        }

        /// <summary>
        /// An assembly name reaches both the title and the src attribute, and <c>&amp;</c> is
        /// legal in a file name on every platform this targets.
        /// </summary>
        private static string Escape(string text) => (text ?? string.Empty)
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
    }
}
