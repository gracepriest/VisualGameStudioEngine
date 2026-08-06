using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace BasicLang.Compiler.CodeGen.JavaScript
{
    /// <summary>
    /// A Source Map v3 document — what makes a browser show <c>.bas</c> lines in devtools
    /// instead of the generated JavaScript.
    ///
    /// <para><b>Every position here is 0-BASED,</b> which the format requires and which
    /// BasicLang's own line numbers are not: <c>IRInstruction.SourceLine</c> is 1-based, so
    /// <see cref="Add"/> takes 1-based source lines and converts. Getting this backwards
    /// shifts every breakpoint by one line and still produces a map that loads and decodes
    /// cleanly, so nothing complains.</para>
    ///
    /// <para><b>Column data is deliberately absent.</b> The IR carries no column at all —
    /// "Column" does not appear in IRNodes.cs — so every segment maps to column 0 of its
    /// source line. That is valid v3 and gives correct LINE-level stepping, which is what a
    /// breakpoint needs; inventing a column would be a fabricated position.</para>
    /// </summary>
    public sealed class JavaScriptSourceMap
    {
        private readonly List<string> _sources = new List<string>();
        private readonly Dictionary<string, int> _sourceIndex =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly List<Segment> _segments = new List<Segment>();

        private readonly struct Segment
        {
            public Segment(int generatedLine, int generatedColumn, int sourceIndex, int sourceLine)
            {
                GeneratedLine = generatedLine;
                GeneratedColumn = generatedColumn;
                SourceIndex = sourceIndex;
                SourceLine = sourceLine;
            }

            public int GeneratedLine { get; }
            public int GeneratedColumn { get; }
            public int SourceIndex { get; }
            public int SourceLine { get; }
        }

        public int Count => _segments.Count;

        /// <summary>Source paths referenced by at least one segment, in <c>sources</c> order.</summary>
        public IReadOnlyList<string> Sources => _sources;

        /// <summary>
        /// Records one mapping. <paramref name="sourceLine"/> is 1-BASED, as it arrives from
        /// the IR; a value of 0 or less means "unknown" and is DROPPED rather than mapped to
        /// line 0 — the IR optimizer leaves SourceLine unset on almost every node it
        /// rewrites, and a segment pointing at line 0 sends the debugger to the top of the
        /// file rather than admitting it does not know.
        /// </summary>
        public void Add(int generatedLine, int generatedColumn, string sourcePath, int sourceLine)
        {
            if (generatedLine < 0 || generatedColumn < 0) return;
            if (sourceLine <= 0) return;
            if (string.IsNullOrEmpty(sourcePath)) return;

            if (!_sourceIndex.TryGetValue(sourcePath, out var index))
            {
                index = _sources.Count;
                _sources.Add(sourcePath);
                _sourceIndex[sourcePath] = index;
            }

            _segments.Add(new Segment(generatedLine, generatedColumn, index, sourceLine - 1));
        }

        /// <summary>Discards every segment recorded after <paramref name="count"/>.</summary>
        /// <remarks>
        /// Needed because the generator RENDERS INTO the output buffer and then rewinds it
        /// (RenderLambda), so mappings taken during that render describe output lines that no
        /// longer exist. See the generator for why those are dropped rather than re-based.
        /// </remarks>
        public void TruncateTo(int count)
        {
            if (count >= 0 && count < _segments.Count)
                _segments.RemoveRange(count, _segments.Count - count);
        }

        /// <summary>
        /// Serialises to a Source Map v3 JSON document.
        /// </summary>
        /// <param name="generatedFileName">The <c>file</c> field — the .js this maps.</param>
        /// <param name="sourceRoot">
        /// When given, source paths are emitted RELATIVE to it. A browser resolves
        /// <c>sources</c> against the map's own URL, so an absolute Windows path like
        /// <c>C:\proj\prog.bas</c> resolves to nothing servable and devtools silently shows
        /// no original source.
        /// </param>
        /// <param name="sourcesContent">
        /// The text of each entry in <see cref="Sources"/>, in the same order, or null to omit
        /// the field. Worth supplying whenever the sources do not sit inside the served
        /// directory — the project route emits into <c>bin/…</c> while the <c>.bas</c> files
        /// stay in the project root, so a browser resolving relative paths would 404 on every
        /// one and devtools would show no original source at all. Inlining makes the map
        /// self-contained, which is what bundlers do for the same reason.
        /// </param>
        public string ToJson(string generatedFileName, string sourceRoot = null,
            IReadOnlyList<string> sourcesContent = null)
        {
            var sources = new List<string>(_sources.Count);
            foreach (var s in _sources) sources.Add(Relativise(s, sourceRoot));

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteNumber("version", 3);
                if (!string.IsNullOrEmpty(generatedFileName))
                    writer.WriteString("file", generatedFileName);

                writer.WriteStartArray("sources");
                foreach (var s in sources) writer.WriteStringValue(s);
                writer.WriteEndArray();

                if (sourcesContent != null)
                {
                    // Must be exactly parallel to `sources`; a null entry means "not
                    // available", which is the spec's own way of saying so.
                    writer.WriteStartArray("sourcesContent");
                    for (var i = 0; i < sources.Count; i++)
                    {
                        var content = i < sourcesContent.Count ? sourcesContent[i] : null;
                        if (content == null) writer.WriteNullValue();
                        else writer.WriteStringValue(content);
                    }
                    writer.WriteEndArray();
                }

                // No symbol renaming happens in this backend, so `names` is always empty —
                // but the field is REQUIRED by the spec and some consumers reject its absence.
                writer.WriteStartArray("names");
                writer.WriteEndArray();

                writer.WriteString("mappings", EncodeMappings());
                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static string Relativise(string path, string root)
        {
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(path)) return Slash(path);

            try
            {
                var rel = Path.GetRelativePath(root, path);
                // A path on another drive comes back absolute — keep the original rather than
                // emitting something that claims to be relative and is not.
                return Path.IsPathRooted(rel) ? Slash(path) : Slash(rel);
            }
            catch
            {
                return Slash(path);
            }
        }

        /// <summary>URLs use forward slashes even when the path came from Windows.</summary>
        private static string Slash(string path) => path?.Replace('\\', '/');

        /// <summary>
        /// The <c>mappings</c> string: one <c>;</c>-separated group per generated line, each
        /// a <c>,</c>-separated list of segments.
        ///
        /// <para><b>The delta rules are not uniform, and that asymmetry is the whole trick.</b>
        /// generatedColumn resets to 0 at every new line, while sourceIndex and sourceLine
        /// accumulate across the ENTIRE file. Resetting all four per line produces a map that
        /// decodes without error and points everywhere except the right place.</para>
        /// </summary>
        private string EncodeMappings()
        {
            if (_segments.Count == 0) return string.Empty;

            var ordered = new List<Segment>(_segments);
            ordered.Sort((a, b) => a.GeneratedLine != b.GeneratedLine
                ? a.GeneratedLine.CompareTo(b.GeneratedLine)
                : a.GeneratedColumn.CompareTo(b.GeneratedColumn));

            var sb = new StringBuilder();
            var line = 0;
            var previousColumn = 0;      // resets per line
            var previousSource = 0;      // carries across lines
            var previousSourceLine = 0;  // carries across lines
            var firstOnLine = true;

            foreach (var seg in ordered)
            {
                while (line < seg.GeneratedLine)
                {
                    sb.Append(';');
                    line++;
                    previousColumn = 0;
                    firstOnLine = true;
                }

                if (!firstOnLine) sb.Append(',');
                firstOnLine = false;

                EncodeVlq(sb, seg.GeneratedColumn - previousColumn);
                EncodeVlq(sb, seg.SourceIndex - previousSource);
                EncodeVlq(sb, seg.SourceLine - previousSourceLine);
                EncodeVlq(sb, 0);   // source column — see the class remarks

                previousColumn = seg.GeneratedColumn;
                previousSource = seg.SourceIndex;
                previousSourceLine = seg.SourceLine;
            }

            return sb.ToString();
        }

        private const string Base64Alphabet =
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

        /// <summary>
        /// Base64 VLQ: the SIGN travels in the low bit, then 5-bit groups little-endian with
        /// 0x20 as the continuation flag.
        /// </summary>
        internal static void EncodeVlq(StringBuilder sb, int value)
        {
            // Shifting the magnitude left by one would overflow at int.MinValue; no real
            // line delta approaches that, but the encoder must not produce garbage if it does.
            var magnitude = value < 0 ? -(long)value : value;
            var vlq = (ulong)(magnitude << 1) | (value < 0 ? 1UL : 0UL);

            do
            {
                var digit = (int)(vlq & 31);
                vlq >>= 5;
                if (vlq > 0) digit |= 32;
                sb.Append(Base64Alphabet[digit]);
            }
            while (vlq > 0);
        }

        /// <summary>Decodes a VLQ run — the inverse of <see cref="EncodeVlq"/>, for tests.</summary>
        internal static List<int> DecodeVlq(string text)
        {
            var values = new List<int>();
            var shift = 0;
            long accumulator = 0;

            foreach (var c in text)
            {
                var digit = Base64Alphabet.IndexOf(c);
                if (digit < 0) throw new FormatException($"'{c}' is not a Base64 VLQ digit.");

                var hasContinuation = (digit & 32) != 0;
                accumulator += (long)(digit & 31) << shift;

                if (hasContinuation) { shift += 5; continue; }

                var negative = (accumulator & 1) == 1;
                var value = accumulator >> 1;
                values.Add((int)(negative ? -value : value));

                accumulator = 0;
                shift = 0;
            }

            return values;
        }
    }
}
