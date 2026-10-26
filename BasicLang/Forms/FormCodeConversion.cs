using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace BasicLang.Forms;

public sealed record FormCodeConversionResult(
    string Text,
    IReadOnlyList<DesignDiagnostic> Diagnostics)
{
    public string Code => Text;
    public string CodeText => Text;
    public string ConvertedText => Text;
    public string NewText => Text;
    public IReadOnlyList<DesignDiagnostic> Findings => Diagnostics;
    public IReadOnlyList<DesignDiagnostic> Messages => Diagnostics;
}

public static class FormCodeConversion
{
    public static FormCodeConversionResult Convert(FormDocument document, string codeText)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (codeText is null) codeText = "";

        var diagnostics = new List<DesignDiagnostic>();
        var filePath = string.IsNullOrEmpty(document.Name) ? "Form.bas" : document.Name + ".bas";

        var inheritsMatch = Regex.Match(codeText, @"^\s*Inherits\s+(.+?)\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        if (inheritsMatch.Success)
        {
            var baseType = inheritsMatch.Groups[1].Value.Trim();
            if (!string.Equals(baseType, "Form", StringComparison.OrdinalIgnoreCase))
            {
                var line = codeText.Substring(0, inheritsMatch.Index).Count(c => c == '\n') + 1;
                diagnostics.Add(new DesignDiagnostic(
                    DesignCodes.RegionMalformed,
                    $"this file inherits {baseType}, not Form; conversion refused.",
                    filePath, line, 0, IsWarning: false));
                return new FormCodeConversionResult(codeText, diagnostics);
            }
        }

        foreach (var control in document.AllControls().Concat(document.AllComponents()))
        {
            if (control.Kind == "Timer" && control.Binds.Any(b => string.Equals(b.Event, "Tick", StringComparison.OrdinalIgnoreCase)))
            {
                if (!control.Properties.ContainsKey("Enabled"))
                {
                    control.Properties["Enabled"] = "true";
                }
            }
        }

        var text = codeText;

        if (!Regex.IsMatch(text, @"^\s*Using\s+System\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            text = "Using System\n" + text;
        }
        if (!Regex.IsMatch(text, @"^\s*Using\s+System\.Drawing\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            text = "Using System.Drawing\n" + text;
        }
        if (!Regex.IsMatch(text, @"^\s*Using\s+System\.Windows\.Forms\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            text = "Using System.Windows.Forms\n" + text;
        }

        if (!Regex.IsMatch(text, @"^\s*Inherits\s+Form\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            var classMatch = Regex.Match(text, @"^(\s*Public\s+Class\s+\w+.*)$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            if (classMatch.Success)
            {
                text = text.Insert(classMatch.Index + classMatch.Length, "\n    Inherits Form");
            }
        }

        text = Regex.Replace(text, @"(<vgs:designer\s+region=""[^""]*""\s+form=""[^""]*"")(hash=)", "$1 style=\"portable\" $2", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"(<vgs:designer\s+region=""[^""]*""\s+form=""[^""]*"")(\s+hash=)", "$1 style=\"portable\"$2", RegexOptions.IgnoreCase);

        var write = RegionWriter.Write(filePath, text, document, (document.Name ?? "Form") + document.FileExtension);
        if (write.Refused)
        {
            diagnostics.AddRange(write.Diagnostics);
            return new FormCodeConversionResult(text, diagnostics);
        }
        text = write.Text;
        diagnostics.AddRange(write.Diagnostics);

        foreach (var control in document.AllControls().Concat(document.AllComponents()))
        {
            foreach (var bind in control.Binds)
            {
                var evt = FindEvent(control.Kind, bind.Event);
                var argsType = evt?.WinFormsArgs;
                if (string.IsNullOrEmpty(argsType)) argsType = "EventArgs";

                var handler = bind.Handler;
                if (string.IsNullOrEmpty(handler)) continue;

                var pattern = new Regex(@"\bSub\s+" + Regex.Escape(handler) + @"\s*\(\s*(\w+)\s+As\s+DomEvent\s*\)", RegexOptions.IgnoreCase);
                text = pattern.Replace(text, m => $"Sub {handler}(sender As Object, {m.Groups[1].Value} As {argsType})");

                var pattern2 = new Regex(@"\bSub\s+" + Regex.Escape(handler) + @"\s*\(\s*\)", RegexOptions.IgnoreCase);
                text = pattern2.Replace(text, $"Sub {handler}(sender As Object, e As {argsType})");
            }
        }

        var regions = RegionMarkers.Scan(text);
        var regionSpans = regions.Select(r => (r.StartOffset, r.EndOffset)).ToList();
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        int offset = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var lineStart = offset;
            var lineEnd = offset + line.Length;
            offset = lineEnd + (i < lines.Length - 1 ? (text.Substring(lineEnd, Math.Min(2, text.Length - lineEnd)) == "\r\n" ? 2 : 1) : 0);

            if (regionSpans.Any(s => lineStart >= s.StartOffset && lineEnd <= s.EndOffset))
            {
                continue;
            }

            string? suggestion = null;
            if (line.Contains(".value", StringComparison.OrdinalIgnoreCase)) suggestion = "use .Text";
            else if (line.Contains(".textContent", StringComparison.OrdinalIgnoreCase)) suggestion = "use .Text";
            else if (line.Contains(".checked", StringComparison.OrdinalIgnoreCase)) suggestion = "use .Checked";
            else if (line.Contains(".disabled", StringComparison.OrdinalIgnoreCase)) suggestion = "use .Enabled = False";
            else if (line.Contains(".hidden", StringComparison.OrdinalIgnoreCase)) suggestion = "use .Visible = False";
            else if (line.Contains("getElementById", StringComparison.OrdinalIgnoreCase)) suggestion = "use the control directly";
            else if (line.Contains("e.target", StringComparison.OrdinalIgnoreCase)) suggestion = "use sender";

            if (suggestion != null)
            {
                diagnostics.Add(new DesignDiagnostic(
                    "BL8036",
                    $"uses the DOM directly: {suggestion}",
                    filePath, i + 1, 1, IsWarning: true));
            }
        }

        return new FormCodeConversionResult(text, diagnostics);
    }

    private static FormEventDef? FindEvent(string kind, string eventName)
    {
        var def = FormControlCatalog.Find(kind);
        if (def == null) return null;
        foreach (var evt in def.Events ?? Array.Empty<FormEventDef>())
        {
            if (string.Equals(evt.Name, eventName, StringComparison.OrdinalIgnoreCase)) return evt;
            if (string.Equals(evt.WebEvent, eventName, StringComparison.OrdinalIgnoreCase)) return evt;
        }
        return null;
    }
}
