using System.Text;
using BasicLang.Forms.Recognizer;

namespace BasicLang.Forms;

/// <summary>What a region write did, or refused to do.</summary>
/// <param name="Changed">False when the file already said exactly this.</param>
/// <param name="Text">The new file text. Equal to the input when <paramref name="Changed"/> is false or the write was refused.</param>
public sealed record RegionWriteResult(
    bool Changed,
    string Text,
    IReadOnlyList<DesignDiagnostic> Diagnostics)
{
    /// <summary>True when a refusal stopped the write. The designer opens the form read-only.</summary>
    public bool Refused => Diagnostics.Any(d => !d.IsWarning);
}

/// <summary>
/// Generates the two designer-owned regions and writes them into the user's own file (D1).
///
/// <para><b>This is the most dangerous code in the feature</b> — it edits a file the user owns and
/// did not ask it to touch. Everything outside the two marked regions is never read, never parsed
/// and never rewritten: the write is a pair of substring replacements at offsets, not a
/// regeneration.</para>
///
/// <para>The recovery policy is refuse-by-default. A region is regenerated only when both markers
/// are present, balanced, and the content hash matches what the open marker claims. Anything else —
/// a hand edit inside the region, a missing or duplicated marker — stops the write and reports.
/// <b>The designer never silently discards hand-written code.</b></para>
/// </summary>
public static class RegionWriter
{
    /// <summary>
    /// Rewrites the designer regions of <paramref name="source"/> from <paramref name="form"/>.
    /// Refuses rather than writing when any region is not <see cref="RegionState.Canon"/>.
    /// </summary>
    public static RegionWriteResult Write(string filePath, string source, FormDocument form, string formFileName)
    {
        var diagnostics = new List<DesignDiagnostic>();
        var regions = RegionMarkers.Scan(source);
        var index = new SourceIndex(source);

        foreach (var region in regions)
        {
            switch (region.State)
            {
                case RegionState.HashMismatch:
                    diagnostics.Add(Error(DesignCodes.RegionHandEdited,
                        $"the designer region '{region.Name}' in this file has been edited by hand, " +
                        "so regenerating it would discard that edit. The form opens read-only; revert " +
                        "the edit inside the region, or re-import the file, to hand it back to the designer.",
                        filePath, region.Line));
                    break;

                case RegionState.Malformed:
                    diagnostics.Add(Error(DesignCodes.RegionMalformed,
                        $"the designer markers for region '{region.Name}' are missing a partner, " +
                        "nested, or duplicated. The designer will not write to this file until the " +
                        "markers are balanced.",
                        filePath, region.Line));
                    break;
            }
        }

        if (diagnostics.Count > 0)
        {
            return new RegionWriteResult(Changed: false, source, diagnostics);
        }

        var controls = RegionMarkers.Find(regions, RegionMarkers.Controls);
        var init = RegionMarkers.Find(regions, RegionMarkers.Init);

        if (controls == null && init == null)
        {
            // NEITHER marker is the IMPORT case, not an error: `design --import` offers to adopt the
            // file and until then the designer writes nothing (D1). A warning so a caller can tell
            // "nothing to do" from "refused".
            diagnostics.Add(new DesignDiagnostic(
                DesignCodes.RegionAbsent,
                $"{DesignCodes.RegionAbsent}: this file has no designer regions, so nothing was " +
                "written. Import it first to let the designer own part of it.",
                filePath, 0, 0, IsWarning: true));
            return new RegionWriteResult(Changed: false, source, diagnostics);
        }

        if (controls == null || init == null)
        {
            // ⚠ HALF-adopted is not the import case, and telling the user the file "has no designer
            // regions" when it visibly has one is worse than saying nothing. A file with one region
            // is closer to Malformed: the designer cannot write a complete form into it, and
            // silently writing only the half that exists would leave the field declarations and the
            // wiring out of step.
            var present = controls != null ? RegionMarkers.Controls : RegionMarkers.Init;
            var missing = controls != null ? RegionMarkers.Init : RegionMarkers.Controls;

            diagnostics.Add(Error(DesignCodes.RegionMalformed,
                $"this file has a designer '{present}' region but no '{missing}' region. Both are " +
                "required — the declarations and the wiring must be regenerated together. Add the " +
                $"missing '{missing}' markers, or remove the '{present}' ones and re-import.",
                filePath, (controls ?? init)!.Line));
            return new RegionWriteResult(Changed: false, source, diagnostics);
        }

        // ⛔ The body is built with the line ending the FILE uses, not Environment.NewLine. Building
        // it with AppendLine would emit LF bodies into a CRLF file when the designer runs on Linux
        // (or the reverse), leaving the user's file with mixed endings after a save it did not ask
        // for. The hash normalises line endings, so it stays stable either way — which means this
        // would have gone unnoticed by every hash check.
        var newline = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

        var controlsBody = GenerateControls(form, IndentOf(index, controls), newline);
        var initBody = GenerateInit(form, IndentOf(index, init), newline);

        CheckHandlerOrdering(filePath, index, form, init, diagnostics);
        if (diagnostics.Any(d => !d.IsWarning))
        {
            return new RegionWriteResult(Changed: false, source, diagnostics);
        }

        // Replace the LATER region first so the earlier one's offsets stay valid.
        var ordered = new[] { controls, init }.OrderByDescending(r => r.StartOffset).ToList();
        var text = source;

        foreach (var region in ordered)
        {
            var body = region.Name.Equals(RegionMarkers.Controls, StringComparison.OrdinalIgnoreCase)
                ? controlsBody
                : initBody;

            text = ReplaceRegion(text, region, body, formFileName, IndentOf(index, region), newline);
        }

        return new RegionWriteResult(!string.Equals(text, source, StringComparison.Ordinal), text, diagnostics);
    }

    /// <summary>
    /// ⛔⛔ D8's ordering rule. An <c>AddressOf</c> naming a <c>Sub</c> declared LATER in the file
    /// erases its parameter types to <c>Action(Of Object)</c> and then hard-errors against the
    /// expected delegate — measured, in both a Class and a Module. So a handler must be declared
    /// before the region that wires it.
    ///
    /// <para>⚠ The spec's own worked example in D1 violates this: it shows the <c>init</c> region
    /// above <c>Private Sub btnLogin_Click</c>. The example is illustrating the marker shape rather
    /// than the ordering, but a reader copying its layout gets a file that does not build on the web
    /// target. This check turns that into a diagnostic instead of a confusing compile error.</para>
    /// </summary>
    private static void CheckHandlerOrdering(
        string filePath, SourceIndex index, FormDocument form,
        FormRegion init, List<DesignDiagnostic> diagnostics)
    {
        var handlers = form.AllControls()
            .SelectMany(c => c.Binds)
            .Select(b => b.Handler)
            .Where(h => !string.IsNullOrEmpty(h))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var handler in handlers)
        {
            var declaration = FindHandlerDeclarationLine(index, handler);
            if (declaration == 0)
            {
                // Not declared at all is Task 15's missing-handler check, not this one's business.
                continue;
            }

            if (index.OffsetOf(declaration, 1) > init.StartOffset)
            {
                diagnostics.Add(Error(DesignCodes.HandlerDeclaredAfterWiring,
                    $"'{handler}' is declared on line {declaration}, AFTER the designer's init " +
                    "region that wires it. An AddressOf naming a Sub declared later erases its " +
                    "parameter types and then fails to match the event's delegate. Move the handler " +
                    "above the init region.",
                    filePath, declaration));
            }
        }
    }

    /// <summary>The 1-based line declaring <c>Sub &lt;name&gt;</c>, or 0. Text scan: this must work on a file that does not compile.</summary>
    private static int FindHandlerDeclarationLine(SourceIndex index, string handler)
    {
        for (var line = 1; line <= index.LineCount; line++)
        {
            var text = index.LineText(line).TrimStart();
            if (text.StartsWith("'", StringComparison.Ordinal))
            {
                continue;
            }

            var sub = text.IndexOf("Sub ", StringComparison.OrdinalIgnoreCase);
            if (sub < 0)
            {
                continue;
            }

            var after = text.Substring(sub + 4).TrimStart();
            if (after.StartsWith(handler, StringComparison.Ordinal) &&
                (after.Length == handler.Length || !char.IsLetterOrDigit(after[handler.Length]) && after[handler.Length] != '_'))
            {
                return line;
            }
        }

        return 0;
    }

    private static string ReplaceRegion(
        string text, FormRegion region, string body, string formFileName, string indent, string newline)
    {
        var hash = RegionMarkers.HashContent(body);

        var replacement = new StringBuilder()
            .Append(RegionMarkers.FormatOpen(region.Name, formFileName, hash, indent)).Append(newline)
            .Append(body)
            .Append(RegionMarkers.FormatClose(indent))
            .ToString();

        return text.Substring(0, region.StartOffset) + replacement + text.Substring(region.EndOffset);
    }

    private static string IndentOf(SourceIndex index, FormRegion region)
    {
        var line = index.LineText(region.Line);
        return line.Substring(0, line.Length - line.TrimStart().Length);
    }

    // ==================================================================
    // Generation
    // ==================================================================

    /// <summary>
    /// The field declarations.
    ///
    /// <para>ℹ️ <c>Private</c> is correct: the region and the user's handlers are the same class in
    /// the same file, so nothing outside needs to see these. (D1's worked example shows
    /// <c>Protected</c>; either compiles here, and the plan's note that Private is right is the more
    /// considered statement. Recorded because the two documents differ.)</para>
    /// </summary>
    private static string GenerateControls(FormDocument form, string indent, string newline)
    {
        var body = new StringBuilder();
        var inner = indent;

        foreach (var control in form.AllControls())
        {
            body.Append($"{inner}Private {control.Id} As {DeclaredType(form, control)}").Append(newline);
        }

        return body.ToString();
    }

    private static string GenerateInit(FormDocument form, string indent, string newline)
    {
        var body = new StringBuilder();
        var inner = indent + "    ";

        body.Append($"{indent}Private Sub InitializeComponent()").Append(newline);

        if (form.Target == FormTarget.Web)
        {
            // Declared locally so the region is self-contained — it must not depend on a field the
            // user could rename or remove.
            body.Append($"{inner}Dim doc As Document = ::document").Append(newline);
        }

        foreach (var control in form.Controls)
        {
            AppendControlInit(body, form, control, parent: "Me", inner, newline);
        }

        body.Append($"{indent}End Sub").Append(newline);
        return body.ToString();
    }

    /// <summary>
    /// Emits one control and then its children, parenting each child to <b>its own container</b>.
    ///
    /// <para>⚠ This used to walk the flat <c>AllControls()</c> list and emit
    /// <c>Me.Controls.Add(x)</c> for every control including nested ones — so a Button inside a
    /// Panel was added to the FORM, and a nested layout rendered flat at run time. That is a silent
    /// divergence between the designer view and the running program, which is the whole class of
    /// bug D9 exists to prevent. The catalog ships Panel and GroupBox as containers, so this is
    /// reachable from ordinary use.</para>
    /// </summary>
    private static void AppendControlInit(
        StringBuilder body, FormDocument form, FormControl control, string parent, string inner, string newline)
    {
        if (form.Target == FormTarget.Web)
        {
            body.Append($"{inner}{control.Id} = doc.getElementById(\"{control.Id}\")").Append(newline);
        }
        else
        {
            body.Append($"{inner}{control.Id} = New {DeclaredType(form, control)}()").Append(newline);

            // ⛔ One statement per property. There is no object-initializer syntax, and `With` must
            // NEVER be emitted: `.Prop = value` inside a With block is silently discarded by the IR
            // builder, so the running program would not set what the designer shows.
            foreach (var (name, value) in control.Properties)
            {
                body.Append($"{inner}{control.Id}.{name} = {Literal(control, name, value)}").Append(newline);
            }
        }

        foreach (var bind in control.Binds)
        {
            if (string.IsNullOrEmpty(bind.Handler))
            {
                continue;
            }

            if (form.Target == FormTarget.Web)
            {
                // ⛔ NEVER `AddHandler el.click, AddressOf H` on the web: the event-call rewrite
                // emits `{recv}.add(handler)` unconditionally, so that becomes `el.click.add(H)` →
                // a runtime TypeError. addEventListener is the only correct form, and AddressOf
                // (not a lambda) is what produces a bound handler.
                body.Append($"{inner}{control.Id}.addEventListener(\"{bind.Event}\", AddressOf {bind.Handler})")
                    .Append(newline);
            }
            else
            {
                // ⛔ NEVER `Handles` — it is lexed but never parsed, so the file would not build.
                body.Append($"{inner}AddHandler {control.Id}.{bind.Event}, AddressOf {bind.Handler}")
                    .Append(newline);
            }
        }

        foreach (var child in control.Children)
        {
            AppendControlInit(body, form, child, control.Id, inner, newline);
        }

        if (form.Target == FormTarget.WinForms)
        {
            // Parented to its container — `Me` only for a top-level control. Emitted AFTER the
            // children so a container is populated before it is added, which is the order the
            // shipped template uses.
            body.Append($"{inner}{parent}.Controls.Add({control.Id})").Append(newline);
        }
    }

    /// <summary>
    /// Formats a property value as BasicLang SOURCE, driven off the catalog's declared type.
    ///
    /// <para>⛔ The same <c>Properties</c> dictionary means two different things to two consumers:
    /// the document reader stores the RAW attribute text (<c>Sign in</c>, unquoted, because XML
    /// attributes are not quoted values), while this writer splices the value into generated source.
    /// Emitting the raw text produced <c>btnLogin.Text = Sign in</c> — a syntax error. The recognizer
    /// meanwhile stores already-quoted source text, because that is what it read. Typing the
    /// formatting off the catalog is what lets both feed the same writer.</para>
    /// </summary>
    private static string Literal(FormControl control, string name, string value)
    {
        // Already a source literal (the recognizer's convention) — leave it exactly as read.
        if (value.StartsWith("\"", StringComparison.Ordinal) ||
            value.StartsWith("New ", StringComparison.Ordinal))
        {
            return value;
        }

        var property = control.Definition?.Property(name);
        return property?.Type switch
        {
            FormPropertyType.Int => value,
            FormPropertyType.Bool => bool.TryParse(value, out var flag) ? (flag ? "True" : "False") : value,
            // A colour and an enum both name a member the target resolves; a string is a string.
            FormPropertyType.Enum => value,
            FormPropertyType.Color => value,
            _ => "\"" + value.Replace("\"", "\"\"") + "\""
        };
    }

    /// <summary>
    /// The BasicLang type a control's field is declared as: the catalog's WinForms type on the
    /// desktop, and <c>Element</c> on the web — the typed DOM handle every element comes back as.
    /// </summary>
    private static string DeclaredType(FormDocument form, FormControl control)
    {
        if (form.Target == FormTarget.Web)
        {
            return "Element";
        }

        return control.Definition?.WinFormsType ?? "Control";
    }

    private static DesignDiagnostic Error(string code, string message, string filePath, int line) =>
        new(code, $"{code}: {message}", filePath, line, 0, IsWarning: false);
}
