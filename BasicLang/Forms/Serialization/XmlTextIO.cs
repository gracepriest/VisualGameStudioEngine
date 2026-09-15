using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace BasicLang.Forms.Serialization;

/// <summary>
/// Serializes an <see cref="XDocument"/> back to text without disturbing the original file's
/// prologue or line endings.
///
/// <para>⚠ This duplicates logic that also lives in <c>VisualGameStudio.ProjectSystem</c>'s
/// <c>ProjectSerializer</c>. It is duplicated rather than shared because the reference edge runs
/// <c>ProjectSystem → BasicLang</c> and not the other way, and <c>basiclang build</c> has to write
/// form documents on a machine with no IDE. The two copies are small and the alternative — a shared
/// assembly nothing else needs — is worse. Every trap below was found the expensive way once
/// already; do not simplify either copy without re-reading this comment.</para>
/// </summary>
internal static class XmlTextIO
{
    /// <summary>
    /// The text for <paramref name="doc"/>, reproducing <paramref name="originalText"/>'s XML
    /// declaration (or absence of one) and its line endings.
    /// </summary>
    public static string Serialize(XDocument doc, string originalText)
    {
        var usesCrLf = originalText.Contains("\r\n", StringComparison.Ordinal);

        var settings = new XmlWriterSettings
        {
            // Pairs with LoadOptions.PreserveWhitespace on the read side: together they reproduce
            // the original layout instead of re-indenting the whole document around one edit.
            Indent = false,
            // ⛔ Always omitted here. XDocument.Save(TextWriter, SaveOptions) calls
            // WriteStartDocument() unconditionally and leaves OmitXmlDeclaration false, so it
            // PREPENDS <?xml …?> even to a document that never had one — and with formatting off,
            // with no newline after it. The original prologue is restored verbatim below instead.
            OmitXmlDeclaration = true,
            // ⛔ The XML parser is REQUIRED to normalise CRLF to LF, so every whitespace node in
            // memory holds bare LF; XmlWriter then re-expands using NewLineChars, which defaults to
            // CRLF. Left alone, editing one attribute rewrites every line ending in the file.
            NewLineHandling = usesCrLf ? NewLineHandling.Replace : NewLineHandling.None,
            NewLineChars = usesCrLf ? "\r\n" : "\n"
        };

        var body = new StringWriter();
        using (var writer = XmlWriter.Create(body, settings))
        {
            doc.WriteTo(writer);
        }

        return Recombine(originalText, body.ToString());
    }

    /// <summary>Writes BOM-less UTF-8, but only when the text differs from what is already there.</summary>
    /// <returns>True when the file was written.</returns>
    public static bool SaveIfChanged(string path, string text)
    {
        if (File.Exists(path) && File.ReadAllText(path) == text)
        {
            // "A no-op patch writes nothing." Not merely an optimisation: a save that rewrites the
            // file on every canvas tick makes every Build/F5 look like an edit to file watchers and
            // to source control.
            return false;
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = path + ".tmp" + Environment.ProcessId;
        try
        {
            File.WriteAllText(temp, text, new UTF8Encoding(false));
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                try { File.Delete(temp); } catch { /* best-effort cleanup */ }
            }
        }

        return true;
    }

    /// <summary>
    /// Puts the original XML declaration back in front of the serialized body, separated exactly as
    /// it was. Leading newlines are stripped from the body and re-supplied from the original rather
    /// than trusting either alone: whether XDocument keeps the whitespace between the declaration
    /// and the root as a document-level node is an implementation detail, and being wrong in either
    /// direction gives a spurious blank line or a declaration jammed against the root.
    /// </summary>
    private static string Recombine(string originalText, string body)
    {
        if (!originalText.StartsWith("<?xml", StringComparison.Ordinal))
        {
            return body;
        }

        var end = originalText.IndexOf("?>", StringComparison.Ordinal);
        if (end < 0)
        {
            return body;
        }

        end += 2;
        var separatorStart = end;
        while (end < originalText.Length && (originalText[end] == '\r' || originalText[end] == '\n'))
        {
            end++;
        }

        return originalText.Substring(0, separatorStart)
             + originalText.Substring(separatorStart, end - separatorStart)
             + body.TrimStart('\r', '\n');
    }
}
