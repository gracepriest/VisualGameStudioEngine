using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BasicLang.Forms;

public sealed record DomUsageFinding(string Code, string Message, int Line, int Column);

public sealed record FormCodeConversionResult(
    string Text,
    IReadOnlyList<DesignDiagnostic> Diagnostics,
    IReadOnlyList<DomUsageFinding>? DomFindings = null)
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
        var domFindings = new List<DomUsageFinding>();
        var filePath = string.IsNullOrEmpty(document.Name) ? "Form.bas" : document.Name + ".bas";

        // A different base type cannot be turned into a Form without changing the user's code.
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
                return new FormCodeConversionResult(codeText, diagnostics, domFindings);
            }
        }

        // A wired DOM-style Timer implies Enabled=true. The document is the caller's object: remember
        // the old value and restore it after generation, so Convert stays pure.
        var timerRestores = new List<(FormControl Control, bool Had, string? Value)>();
        foreach (var control in document.AllControls().Concat(document.AllComponents()))
        {
            if (control.Kind == "Timer" &&
                control.Binds.Any(b => string.Equals(b.Event, "Tick", StringComparison.OrdinalIgnoreCase)))
            {
                var had = control.Properties.TryGetValue("Enabled", out var oldValue);
                timerRestores.Add((control, had, oldValue));
                control.Properties["Enabled"] = "true";
            }
        }

        var text = codeText;

        // The portable header is these three lines, in this order. Remove any existing copies
        // (wherever the DOM file put them) and put the canonical block first.
        text = Regex.Replace(text, @"^\s*Using\s+System\s*\r?\n", "", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"^\s*Using\s+System\.Drawing\s*\r?\n", "", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"^\s*Using\s+System\.Windows\.Forms\s*\r?\n", "", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        text = "Using System\nUsing System.Drawing\nUsing System.Windows.Forms\n" + text;

        if (!Regex.IsMatch(text, @"^\s*Inherits\s+Form\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            var classMatch = Regex.Match(text, @"^(\s*Public\s+Class\s+\w+.*)$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            if (classMatch.Success)
            {
                text = text.Insert(classMatch.Index + classMatch.Length, "\n    Inherits Form");
            }
        }

        // Convert both open markers to the portable style. The first pattern covers a marker with no
        // style attribute; the second keeps the existing hash attribute in place.
        text = Regex.Replace(
            text,
            @"(<vgs:designer\s+region=""[^""]*""\s+form=""[^""]*"")(\s+hash=)",
            "$1 style=\"portable\"$2",
            RegexOptions.IgnoreCase);

        RegionWriteResult write;
        try
        {
            write = RegionWriter.Write(filePath, text, document, (document.Name ?? "Form") + document.FileExtension);
        }
        finally
        {
            foreach (var (control, had, value) in timerRestores)
            {
                if (had) control.Properties["Enabled"] = value!;
                else control.Properties.Remove("Enabled");
            }
        }

        if (write.Refused)
        {
            diagnostics.AddRange(write.Diagnostics);
            return new FormCodeConversionResult(text, diagnostics, domFindings);
        }

        text = write.Text;
        diagnostics.AddRange(write.Diagnostics);

        // Rewrite only the signatures the conversion can prove are the DOM form.
        foreach (var control in document.AllControls().Concat(document.AllComponents()))
        {
            foreach (var bind in control.Binds)
            {
                var evt = FindEvent(control.Kind, bind.Event);
                var argsType = string.IsNullOrEmpty(evt?.WinFormsArgs) ? "EventArgs" : evt!.WinFormsArgs!;
                var handler = bind.Handler;
                if (string.IsNullOrEmpty(handler)) continue;

                var oneDomEvent = new Regex(
                    @"\bSub\s+" + Regex.Escape(handler) + @"\s*\(\s*(\w+)\s+As\s+DomEvent\s*\)",
                    RegexOptions.IgnoreCase);
                var match = oneDomEvent.Match(text);
                if (match.Success)
                {
                    text = oneDomEvent.Replace(
                        text,
                        m => $"Sub {handler}(sender As Object, {m.Groups[1].Value} As {argsType})");
                    continue;
                }

                var noParameters = new Regex(
                    @"\bSub\s+" + Regex.Escape(handler) + @"\s*\(\s*\)",
                    RegexOptions.IgnoreCase);
                if (control.Kind == "Timer" && noParameters.IsMatch(text))
                {
                    text = noParameters.Replace(text, $"Sub {handler}(sender As Object, e As {argsType})");
                    continue;
                }

                if (Regex.IsMatch(text, @"\b(Function|Sub)\s+" + Regex.Escape(handler) + @"\b", RegexOptions.IgnoreCase))
                {
                    diagnostics.Add(new DesignDiagnostic(
                        DesignCodes.NonSimpleHandler,
                        $"handler '{handler}' is not a simple Sub(e As DomEvent) or a Timer Sub(), so its signature was left unchanged.",
                        filePath, 1, 1, IsWarning: true));
                }
            }
        }

        // Every DOM use outside the designer regions gets a warning with a catalog-shaped suggestion.
        var regions = RegionMarkers.Scan(text);
        var regionSpans = regions.Select(r => (r.StartOffset, r.EndOffset)).ToList();
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var offset = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var lineStart = offset;
            var lineEnd = offset + line.Length;
            offset = lineEnd + (i < lines.Length - 1
                ? (text.Substring(lineEnd, Math.Min(2, text.Length - lineEnd)) == "\r\n" ? 2 : 1)
                : 0);

            if (regionSpans.Any(s => lineStart >= s.StartOffset && lineEnd <= s.EndOffset))
            {
                continue;
            }

            string? suggestion = null;
            if (line.Contains(".value", StringComparison.OrdinalIgnoreCase)) suggestion = "use .Text";
            else if (line.Contains(".textContent", StringComparison.OrdinalIgnoreCase)) suggestion = "use .Text";
            else if (line.Contains(".checked", StringComparison.OrdinalIgnoreCase)) suggestion = "use .Checked";
            else if (line.Contains(".disabled", StringComparison.OrdinalIgnoreCase)) suggestion = "use .Enabled = False";
            else if (line.Contains(".hidden", StringComparison.OrdinalIgnoreCase) ||
                     line.Contains(".style.display", StringComparison.OrdinalIgnoreCase)) suggestion = "use .Visible = False";
            else if (line.Contains("getElementById", StringComparison.OrdinalIgnoreCase)) suggestion = "use the control directly";
            else if (line.Contains("e.target", StringComparison.OrdinalIgnoreCase)) suggestion = "use sender";

            if (suggestion == null) continue;

            var message = $"BL8036: line {i + 1} uses the DOM directly: {suggestion}";
            var finding = new DomUsageFinding(DesignCodes.DomUsage, message, i + 1, 1);
            domFindings.Add(finding);
            diagnostics.Add(new DesignDiagnostic(
                finding.Code, message, filePath, finding.Line, finding.Column, IsWarning: true));
        }

        return new FormCodeConversionResult(text, diagnostics, domFindings);
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
