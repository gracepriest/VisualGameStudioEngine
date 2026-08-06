using System;
using System.Collections.Generic;
using System.IO;
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
        public static IReadOnlyList<string> Emit(
            string outputDirectory,
            string scriptFileName,
            string javaScript,
            string title = null,
            string sourceMapJson = null)
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

            return written;
        }

        /// <summary>
        /// The generated JavaScript is a FLAT script — it contains no import or export and
        /// ends in a bare call to Main(). Nothing in the text marks it as a module, so
        /// <c>type="module"</c> on the tag is what supplies module semantics in a browser.
        /// (The execution tests get the same effect by naming their temp file <c>.mjs</c>.)
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
