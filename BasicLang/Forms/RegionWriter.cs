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
        var initBody = GenerateInit(form, IndentOf(index, init), newline, filePath, diagnostics);

        CheckAnchors(filePath, form, diagnostics);
        CheckTargetProperties(filePath, form, diagnostics);
        CheckComponentTargets(filePath, form, diagnostics);
        CheckComponentBinds(filePath, form, diagnostics);
        CheckControlBinds(filePath, form, diagnostics);
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
    /// <summary>
    /// The <c>AnchorStyles</c> flag values, verified against the official enum documentation.
    ///
    /// <para>⛔ <c>DockStyle</c> numbers DIFFERENTLY — its <c>Left</c> is 3, not 4 — and is not a
    /// flags enum at all. The two must never share a conversion; Dock keeps its named member.</para>
    /// </summary>
    private static readonly Dictionary<string, int> AnchorFlags =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["None"] = 0, ["Top"] = 1, ["Bottom"] = 2, ["Left"] = 4, ["Right"] = 8
        };

    /// <summary>
    /// Checks that every named anchor edge exists. Multi-edge anchors are EMITTABLE.
    ///
    /// <para>⛔⛔ <b>This used to refuse anything with more than one edge</b>, and
    /// <c>docs/HANDOFF.md</c> carried that as an open decision: BasicLang had no way to write a
    /// combined flags value, measured three ways — <c>Or</c> demands Boolean operands,
    /// <c>CType</c> was refused because <c>AnchorStyles</c> is unresolvable, and <c>|</c> lexes but
    /// does not parse. The choice on the table was a parser change or single-edge-only.</para>
    ///
    /// <para>⚠ Re-measured 2026-09-18: all three still fail, but a fourth route had never been
    /// tried. <c>btn.Anchor = 7</c> COMPILES in BasicLang and csc rejects it with CS0266, while
    /// <c>(AnchorStyles)7</c> is ACCEPTED — so the cast was the entire gap, and it was refused by
    /// one arm of <c>SemanticAnalyzer.RejectImpossibleConversion</c> rather than by the parser.
    /// That arm now exempts unresolvable .NET types, and this emits <c>CType(n, AnchorStyles)</c>.
    /// </para>
    ///
    /// <para>⛔ An UNKNOWN edge name is still refused. Summing it as zero would silently anchor the
    /// control to nothing — the designer/runtime divergence D9 exists to prevent — and the whole
    /// reason this check survives rather than being deleted.</para>
    /// </summary>
    private static void CheckAnchors(
        string filePath, FormDocument form, List<DesignDiagnostic> diagnostics)
    {
        if (form.Target != FormTarget.WinForms)
        {
            return;
        }

        foreach (var control in form.AllControls())
        {
            if (control.Geometry is not PixelGeometry { Anchor: { } anchor } ||
                string.IsNullOrWhiteSpace(anchor))
            {
                continue;
            }

            var unknown = SplitAnchor(anchor).Where(e => !AnchorFlags.ContainsKey(e)).ToList();
            if (unknown.Count > 0)
            {
                diagnostics.Add(Error(DesignCodes.AnchorNotExpressible,
                    $"'{control.Id}' is anchored to '{anchor}', which names an edge AnchorStyles " +
                    $"does not have: {string.Join(", ", unknown)}. The edges are None, Top, " +
                    "Bottom, Left and Right. Emitting it anyway would anchor the control to " +
                    "nothing, with the designer and the running program disagreeing silently.",
                    filePath, 0));
            }
        }
    }

    private static string[] SplitAnchor(string anchor) =>
        anchor.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Warns about properties the document carries that the target does not have, which
    /// <see cref="AppendControlInit"/> skips. A warning, never a refusal: the value is valid in
    /// the other format and round-trips untouched, so the document is not wrong — only unusable
    /// here. Reporting it is what stops the skip from being silent.
    /// </summary>
    private static void CheckTargetProperties(
        string filePath, FormDocument form, List<DesignDiagnostic> diagnostics)
    {
        // Components too (Task 25): a Timer's Enabled is WinForms-only and is skipped on the web.
        foreach (var control in form.AllControls().Concat(form.AllComponents()))
        {
            var definition = control.Definition;
            if (definition == null)
            {
                continue;
            }

            foreach (var name in control.Properties.Keys)
            {
                var property = definition.Property(name);
                if (property != null && !property.AppliesTo(form.Target))
                {
                    diagnostics.Add(new DesignDiagnostic(
                        DesignCodes.PropertyNotOnTarget,
                        $"{DesignCodes.PropertyNotOnTarget}: '{control.Id}.{name}' does not exist " +
                        $"on {form.Target}, so it is not written into the generated code. The " +
                        "value is preserved in the document.",
                        filePath, 0, 0, IsWarning: true));
                }
            }
        }
    }

    /// <summary>
    /// Warns about a component whose kind the target does not have — a ToolTip in a
    /// <c>.blwebform</c>. The toolbox never offers one, but a hand edit can carry one, and it used to
    /// be modelled, declared as an <c>Element</c>, then constructed by nothing and reported by
    /// nothing (review, 2026-09-19). The field stays declared so code naming it still builds.
    /// </summary>
    private static void CheckComponentTargets(
        string filePath, FormDocument form, List<DesignDiagnostic> diagnostics)
    {
        foreach (var component in form.AllComponents())
        {
            var definition = component.Definition;
            if (definition == null || definition.SupportsTarget(form.Target))
            {
                continue;
            }

            diagnostics.Add(new DesignDiagnostic(
                DesignCodes.KindNotOnTarget,
                $"{DesignCodes.KindNotOnTarget}: '{component.Id}' is a {definition.Kind}, which has no " +
                $"{form.Target} form. Its field is declared so code naming it still builds, but " +
                "nothing constructs or wires it here. Retarget the form to see what crosses, or " +
                "remove it.",
                filePath, 0, 0, IsWarning: true));
        }
    }

    /// <summary>
    /// Warns about a bind <see cref="AppendComponentInit"/> will not emit: on the web a component's
    /// only wiring is its construct template, on its default event, so a bind on any other event has
    /// no form here. It used to vanish silently while STILL driving the ordering refusal (review,
    /// 2026-09-19); now it is named, and <see cref="CheckHandlerOrdering"/> ignores it.
    /// </summary>
    private static void CheckComponentBinds(
        string filePath, FormDocument form, List<DesignDiagnostic> diagnostics)
    {
        if (form.Target != FormTarget.Web)
        {
            return;
        }

        foreach (var component in form.AllComponents())
        {
            var definition = component.Definition;
            if (definition == null || !definition.SupportsTarget(form.Target))
            {
                // The KIND is the finding (BL8029), not each of its binds.
                continue;
            }

            foreach (var bind in component.Binds)
            {
                if (bind.UsesReservedDataBinding || string.IsNullOrEmpty(bind.Handler) ||
                    IsEmittedBind(form, component, bind))
                {
                    continue;
                }

                var wired = DeclaredEvents(definition, FormTarget.Web).ToList();

                diagnostics.Add(new DesignDiagnostic(
                    DesignCodes.BindNotOnTarget,
                    $"{DesignCodes.BindNotOnTarget}: '{component.Id}' wires its '{bind.Event}' event to " +
                    $"{bind.Handler}, but a web {definition.Kind} is wired only through " +
                    (wired.Count > 0
                        ? $"its {string.Join(" and ", wired.Select(e => $"'{e}'"))} event"
                        : "no event at all") +
                    ", so the wiring is not written " +
                    "into the generated code. The document keeps it, and WinForms wires it.",
                    filePath, 0, 0, IsWarning: true));
            }
        }
    }

    /// <summary>
    /// Every event name a catalog row WIRES on <paramref name="target"/> — the whole vocabulary the
    /// emitter is allowed to write, the whole vocabulary <see cref="CheckControlBinds"/> accepts, and
    /// (for a tray component) the whole of what <see cref="IsEmittedBind"/> and the BL8028 warning in
    /// <see cref="CheckComponentBinds"/> accept.
    ///
    /// <para>⛔ Delegates to <see cref="FormEvents.WiredOn"/>, the public seam (spec §5), rather than
    /// reading the row itself — including the web tray rule (template, default event only), which
    /// lives inside the seam. Every caller here and the grid's Events tab (slice 5) therefore ask the
    /// same question of the same code, and none restates a rule another could drift from. Followup
    /// 18's widening happens in the ROWS.</para>
    /// </summary>
    private static IEnumerable<string> DeclaredEvents(FormControlDef definition, FormTarget target) =>
        FormEvents.WiredOn(definition, target).Select(e => FormEvents.NameOn(e, target)!);

    /// <summary>
    /// The CATALOG's spelling of the web event <paramref name="bind"/> names, matched ignoring case —
    /// or null when the row does not declare that event at all.
    ///
    /// <para>This is the ONE answer to "what does this bind mean on the web", shared by the emitter
    /// and by <see cref="CheckControlBinds"/>, for the same reason <see cref="IsEmittedBind"/> is
    /// shared: a bind must not be canonicalised one way while being judged another.</para>
    /// </summary>
    private static string? CanonicalWebEvent(FormControlDef definition, FormBind bind) =>
        DeclaredEvents(definition, FormTarget.Web)
            .FirstOrDefault(e => string.Equals(e, bind.Event, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Refuses a CONTROL's web bind on an event its catalog row does not declare (BL8032).
    ///
    /// <para>⛔⛔ The gap this closes: <see cref="CheckComponentBinds"/> (BL8028) iterates
    /// <c>component.Binds</c> only, so a control's binds were never checked against the row's
    /// <c>WebEvent</c> by anything at all — and the emitter wrote whatever the document said
    /// straight into <c>addEventListener</c>. A typo (<c>clik</c>), the DOM's own <c>on</c> prefix
    /// (<c>onclick</c>) and the WinForms spelling (<c>Click</c>) all behaved identically: accepted,
    /// built, shipped, never called. Refused rather than warned for the reason BL8010 and BL8020
    /// are — the designer would otherwise report a document clean and then generate code whose
    /// behaviour silently contradicts it, which is the divergence D9 exists to prevent.</para>
    ///
    /// <para>⚠ WinForms is deliberately NOT checked here. There a wrong name reaches csc as a member
    /// (<c>AddHandler btn.Clik</c> → CS1061), so it fails loudly on its own; and refusing a
    /// non-default WinForms event would break <c>MouseEnter</c> on a Button, which compiles and runs
    /// today. This is only for the target where the failure is silent.</para>
    /// </summary>
    private static void CheckControlBinds(
        string filePath, FormDocument form, List<DesignDiagnostic> diagnostics)
    {
        if (form.Target != FormTarget.Web)
        {
            return;
        }

        foreach (var control in form.AllControls())
        {
            // No row is no truth to check against — that control is already BL8004's finding, and
            // inventing a vocabulary for a kind the catalog does not know would be worse than quiet.
            if (control.Definition is not { } definition)
            {
                continue;
            }

            foreach (var bind in control.Binds)
            {
                // A reserved data-binding attribute is BL8021's refusal, and a bind with no handler
                // wires nothing — neither is this check's business.
                if (bind.UsesReservedDataBinding || string.IsNullOrEmpty(bind.Handler) ||
                    CanonicalWebEvent(definition, bind) != null)
                {
                    continue;
                }

                var declared = DeclaredEvents(definition, FormTarget.Web).ToList();

                diagnostics.Add(Error(DesignCodes.UnknownWebEvent,
                    $"'{control.Id}' wires its '{bind.Event}' event to {bind.Handler}, but a web " +
                    $"{definition.Kind} does not have that event — it has " +
                    (declared.Count > 0
                        ? $"{string.Join(" and ", declared.Select(e => $"'{e}'"))}. "
                        : "no web event at all. ") +
                    "DOM event names are case-sensitive and addEventListener takes a string, so " +
                    "emitting this would register a listener that is never called: the build would " +
                    "succeed, the page would load, and the handler would simply never run.",
                    filePath, 0));
            }
        }
    }

    /// <summary>
    /// Whether the init region will actually wire this bind — the ONE answer the emitter, the
    /// ordering check and the bind warning share, so a bind cannot be refused over in one place and
    /// never emitted in another. A control's binds all reach <c>addEventListener</c> or
    /// <c>AddHandler</c>; a component's do on WinForms; on the web a component is wired only through
    /// its template, on its default event.
    /// </summary>
    private static bool IsEmittedBind(FormDocument form, FormControl control, FormBind bind)
    {
        if (string.IsNullOrEmpty(bind.Handler))
        {
            return false;
        }

        if (form.Target != FormTarget.Web || control.Definition is not { IsComponent: true } definition)
        {
            return true;
        }

        // ⛔ The template-and-default-event rule is the seam's (FormEvents.WiredOn), not restated here.
        return DeclaredEvents(definition, FormTarget.Web)
            .Any(e => string.Equals(e, bind.Event, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Refuses a handler declared AFTER the region that wires it — <b>on the web only</b>.
    ///
    /// <para>⛔⛔ MEASURED 2026-09-13, because applying it to both targets refuses the very shape
    /// Owner decision 3 makes canonical. The erasure is real but narrow:</para>
    ///
    /// <list type="table">
    ///   <item><term>Web, <c>addEventListener("click", AddressOf H)</c>, H declared after</term>
    ///     <description><b>FAILS</b> — "cannot convert from 'Action&lt;Object&gt;' to
    ///     'Action&lt;DomEvent&gt;'". The DOM signature declares the parameter type, so the erased
    ///     <c>Action&lt;Object&gt;</c> has something concrete to fail against.</description></item>
    ///   <item><term>WinForms, <c>AddHandler btn.Click, AddressOf H</c>, H declared after</term>
    ///     <description><b>Compiles</b>, through BasicLang AND csc, and the handler binds with its
    ///     full parameter types (<c>object sender, EventArgs e</c>). The event is an unresolvable
    ///     .NET member typed as <c>Object</c>, so there is no declared delegate to mismatch.</description></item>
    ///   <item><term>Module-level Sub into a declared <c>Action(Of Integer)</c>, declared after</term>
    ///     <description><b>Compiles.</b></description></item>
    /// </list>
    ///
    /// <para>The shipped VSIX template — canonical per Owner decision 3 — declares
    /// <c>btnClick_Click</c> below the <c>InitializeComponent</c> that wires it. Refusing that on
    /// WinForms would make the designer reject the template it is supposed to generate, and would
    /// block the D12 import route for every existing WinForms file.</para>
    /// </summary>
    private static void CheckHandlerOrdering(
        string filePath, SourceIndex index, FormDocument form,
        FormRegion init, List<DesignDiagnostic> diagnostics)
    {
        if (form.Target != FormTarget.Web)
        {
            return;
        }

        // Components too (Task 25). A Timer's parameterless callback is not bitten by the erasure
        // (measured), so for it this check is stricter than the compiler — the safe side.
        // ⛔ Only binds the region EMITS: refusing over a wiring that is never written told the user
        // to move a handler above a region that did not reference it (review, 2026-09-19).
        var handlers = form.AllControls().Concat(form.AllComponents())
            .SelectMany(c => c.Binds.Where(b => IsEmittedBind(form, c, b)))
            .Select(b => b.Handler)
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
                    "region that wires it. On the web an AddressOf naming a Sub declared later " +
                    "erases its parameter types, and addEventListener then rejects it — " +
                    "'cannot convert from Action(Of Object) to Action(Of DomEvent)'. Move the " +
                    "handler above the init region.",
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

        // Components first, as VS declares them (Task 25). A component's field is declared even
        // when nothing constructs it — a web Timer with no handler yet — so the user's own
        // clearInterval compiles before the handler is wired.
        foreach (var component in form.Components)
        {
            body.Append($"{inner}Private {component.Id} As {DeclaredType(form, component)}").Append(newline);
        }

        foreach (var control in form.AllControls())
        {
            body.Append($"{inner}Private {control.Id} As {DeclaredType(form, control)}").Append(newline);
        }

        return body.ToString();
    }

    private static string GenerateInit(
        FormDocument form, string indent, string newline, string filePath, List<DesignDiagnostic> diagnostics)
    {
        var body = new StringBuilder();
        var inner = indent + "    ";

        body.Append($"{indent}Private Sub InitializeComponent()").Append(newline);

        if (form.Target == FormTarget.Web)
        {
            // Declared locally so the region is self-contained — it must not depend on a field the
            // user could rename or remove.
            body.Append($"{inner}Dim doc As Document = ::document").Append(newline);

            // ⛔ The TYPED Window, and only when a script component exists (Task 25). Typed rather
            // than the ::window hatch so that the compiler — not a runtime TypeError — rejects a
            // wrong callback: Window.setInterval takes an Action and refuses Action(Of DomEvent),
            // which the hatch would have let through (measured 2026-09-19). Keyed on a script
            // component EXISTING, not on one being wired — an unused local is the cheaper wrong.
            if (form.Components.Any(c => c.Definition?.WebScript != null))
            {
                body.Append($"{inner}Dim w As Window = ::window").Append(newline);
            }
        }
        else
        {
            // The form's own caption and client size, before any control — the order the shipped
            // VSIX template uses, and the shape Owner decision 3 makes canonical. ClientSize fans
            // in exactly as a control's Size does, and for the same CS1612 reason.
            if (form.Text != null)
            {
                body.Append($"{inner}Me.Text = \"{form.Text.Replace("\"", "\"\"")}\"").Append(newline);
            }

            if (form.Width is > 0 && form.Height is > 0)
            {
                body.Append($"{inner}Me.ClientSize = New Size({form.Width}, {form.Height})").Append(newline);
            }
        }

        // Components before controls, as VS constructs them (Task 25).
        foreach (var component in form.Components)
        {
            AppendComponentInit(body, form, component, inner, newline, filePath, diagnostics);
        }

        AppendSiblings(
            body, form, form.Controls, parent: "Me", parentControl: null, inner, newline, filePath, diagnostics);

        if (form.Target == FormTarget.WinForms)
        {
            // The form property a row claims — MainMenuStrip for a MenuStrip — for the FIRST control
            // in document order whose row declares one, AFTER the whole add run (the strip must be
            // populated and parented before the form points at it). By rule from the catalog, never
            // by switching on the kind, and exactly one such line.
            var main = form.Controls.FirstOrDefault(c => c.Definition?.FormProperty != null);
            if (main != null)
            {
                body.Append($"{inner}Me.{main.Definition!.FormProperty} = {main.Id}").Append(newline);
            }
        }

        body.Append($"{indent}End Sub").Append(newline);
        return body.ToString();
    }

    /// <summary>
    /// Emits a run of siblings: each control's initialization in DOCUMENT order, then — on WinForms —
    /// their <c>Controls.Add</c> calls in REVERSE.
    ///
    /// <para>⛔⛔ <b>The reversal is the z-order, and it is not a stylistic choice.</b> The two
    /// targets number z-order in opposite directions and neither can be changed:</para>
    /// <list type="bullet">
    ///   <item><description>the DOM paints in document order, so the LAST element is on top;</description></item>
    ///   <item><description>WinForms' <c>Controls</c> index 0 is the TOP of the z-order — the official
    ///     API docs for <c>GetChildIndex</c>, <c>SetChildIndex</c> and <c>BringToFront</c> each state
    ///     it — and <c>Controls.Add</c> APPENDS, so the first control added ends up in front.</description></item>
    /// </list>
    /// <para>The document takes the web's meaning, because the web's cannot be negotiated, and the
    /// canvas draws and hit-tests to match: <b>last in the document is in front</b>. Adding siblings
    /// in reverse is what makes the WinForms program agree. Emitting them in document order instead
    /// puts the front-most control at the highest index — the very back — so the designer's
    /// "Bring to Front" would visibly send it behind everything, and only when controls overlap.</para>
    ///
    /// <para>⚠ Initialization stays in document order; only the ADD calls reverse. The properties a
    /// control is given have nothing to do with its layering, and generating the whole region
    /// backwards would make it needlessly hard to read against the document it came from.</para>
    ///
    /// <para>⛔ <b>None of that applies to a HOST's items</b> (spec §5). <c>parentControl</c> is the
    /// control these siblings belong to — null at the form root — and it is the ROW, not the id in
    /// <c>parent</c>, that says how a child is added. When that row carries a
    /// <see cref="FormItemRule"/>, the children are added with THAT row's verb — <c>Items.Add</c>
    /// under a strip, <c>DropDownItems.Add</c> under a menu item — in DOCUMENT order, because a
    /// menu's items are ordered, not layered. Reversing them would run File/Edit/Help as
    /// Help/Edit/File, and <c>Controls.Add</c> would not compile at all (a
    /// <c>ToolStripMenuItem</c> is not a <c>Control</c>: csc CS1503).</para>
    /// </summary>
    private static void AppendSiblings(
        StringBuilder body, FormDocument form, IReadOnlyList<FormControl> controls, string parent,
        FormControl? parentControl, string inner, string newline, string filePath,
        List<DesignDiagnostic> diagnostics)
    {
        foreach (var control in controls)
        {
            AppendControlInit(body, form, control, parent, inner, newline, filePath, diagnostics);
        }

        if (form.Target != FormTarget.WinForms)
        {
            return;
        }

        var rule = parentControl?.Definition?.Items;
        if (rule != null)
        {
            // A host's items: the HOST row's verb, in DOCUMENT order — items are ORDERED, not
            // layered. ⛔ Reversing them here would run File/Edit/Help as Help/Edit/File from a
            // green build. The verb is on the PARENT's row because the same ToolStripMenuItem is
            // Items.Added under a strip and DropDownItems.Added under a menu item.
            foreach (var child in controls)
            {
                body.Append(inner)
                    .Append(rule.Add.Replace("{parent}", parent).Replace("{child}", child.Id))
                    .Append(newline);
            }

            return;
        }

        for (var i = controls.Count - 1; i >= 0; i--)
        {
            body.Append($"{inner}{parent}.Controls.Add({controls[i].Id})").Append(newline);
        }
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
        StringBuilder body, FormDocument form, FormControl control, string parent, string inner,
        string newline, string filePath, List<DesignDiagnostic> diagnostics)
    {
        if (form.Target == FormTarget.Web)
        {
            body.Append($"{inner}{control.Id} = doc.getElementById(\"{control.Id}\")").Append(newline);
        }
        else
        {
            body.Append($"{inner}{control.Id} = New {DeclaredType(form, control)}()").Append(newline);

            AppendPixelGeometry(body, control, inner, newline);
            AppendProperties(body, control, inner, newline, filePath, diagnostics);
        }

        AppendBinds(body, form, control, inner, newline);

        // ⚠ The container's own children, parented to IT — and their adds happen here, so a
        // container is fully populated before the caller adds it to its own parent, which is the
        // order the shipped template uses.
        AppendSiblings(body, form, control.Children, control.Id, control, inner, newline, filePath, diagnostics);
    }

    /// <summary>
    /// A tray component (Task 25): the same declare → construct → set → wire shape as a control,
    /// minus the geometry and the <c>Controls.Add</c> — a component is not a control.
    ///
    /// <para><b>Web:</b> a script-backed component is built from the catalog's template, and its
    /// one bind IS the constructor argument: <c>tmr = w.setInterval(AddressOf tmr_Tick, 100)</c>.
    /// Emitted only when that bind exists (setInterval needs a callback), and never followed by
    /// <c>addEventListener</c> — the field is an Integer handle. A component with no
    /// <see cref="FormWebScript"/> has no web form at all and emits nothing here; the catalog says
    /// it does not support the target, so the toolbox never offered it.</para>
    /// </summary>
    private static void AppendComponentInit(
        StringBuilder body, FormDocument form, FormControl component, string inner,
        string newline, string filePath, List<DesignDiagnostic> diagnostics)
    {
        var definition = component.Definition;

        if (form.Target == FormTarget.Web)
        {
            // The same predicate the ordering check and the bind warning use — one definition of
            // "this bind is wired", so the three cannot disagree about a document.
            var script = definition?.WebScript;
            var bind = component.Binds.FirstOrDefault(b => IsEmittedBind(form, component, b));

            if (script == null || bind == null)
            {
                return;
            }

            var construct = ExpandWebScript(script.Construct, definition!, component, bind.Handler, filePath, diagnostics);
            body.Append($"{inner}{component.Id} = {construct}").Append(newline);
            return;
        }

        body.Append($"{inner}{component.Id} = New {DeclaredType(form, component)}()").Append(newline);
        AppendProperties(body, component, inner, newline, filePath, diagnostics);
        AppendBinds(body, form, component, inner, newline);
    }

    /// <summary>
    /// Fills a <see cref="FormWebScript.Construct"/> template: <c>{handler}</c> is the bind's Sub,
    /// and <c>{Name}</c> is that catalog property's document value when set and valid, else the
    /// row's default. A placeholder the row does not declare is left in place, visibly — a catalog
    /// typo must not become a silently empty argument.
    ///
    /// <para>⚠ A Degraded value falls back to the default AND is reported (BL8009), as
    /// <see cref="AppendProperties"/> reports the same document on WinForms. The number was always
    /// right; the silence meant one document's Error List differed by target (review, 2026-09-19).</para>
    /// </summary>
    private static string ExpandWebScript(
        string template, FormControlDef definition, FormControl component, string handler,
        string filePath, List<DesignDiagnostic> diagnostics)
    {
        return System.Text.RegularExpressions.Regex.Replace(template, @"\{(\w+)\}", match =>
        {
            var name = match.Groups[1].Value;
            if (string.Equals(name, "handler", StringComparison.Ordinal))
            {
                return handler;
            }

            var property = definition.Property(name);
            if (property == null)
            {
                return match.Value;
            }

            if (component.Properties.TryGetValue(property.Name, out var value))
            {
                // This template is WEB code, so the value is judged as the web would judge it.
                if (property.Accepts(value, FormTarget.Web))
                {
                    return value;
                }

                // ⛔ The reason comes from the catalog (DescribeRefusal) — the same text the reader
                // freezes the grid row with — so a target refusal is never mislabelled "not a valid X".
                diagnostics.Add(new DesignDiagnostic(
                    DesignCodes.DegradedProperty,
                    $"{DesignCodes.DegradedProperty}: '{component.Id}.{property.Name}': " +
                    property.DescribeRefusal(value, FormTarget.Web) +
                    $" The catalog default '{property.Default}' is written in its place.",
                    filePath, 0, 0, IsWarning: true));
            }

            return property.Default ?? "";
        });
    }

    /// <summary>
    /// One statement per catalog property, WinForms only — the web's properties reach the page as
    /// markup, never this region.
    ///
    /// <para>Shared by controls and components so the Degraded and collection rules below exist
    /// once; a second copy for the tray would drift on the first fix applied to one of them.</para>
    /// </summary>
    private static void AppendProperties(
        StringBuilder body, FormControl control, string inner, string newline,
        string filePath, List<DesignDiagnostic> diagnostics)
    {
        // ⛔ One statement per property. There is no object-initializer syntax, and `With` must
        // NEVER be emitted: `.Prop = value` inside a With block is silently discarded by the IR
        // builder, so the running program would not set what the designer shows.
        foreach (var (name, value) in control.Properties)
        {
            var property = control.Definition?.Property(name);

            // A property that does not exist on this target is skipped, not emitted. The
            // document keeps it (the other target uses it) and the caller reports it.
            if (property != null && !property.AppliesTo(FormTarget.WinForms))
            {
                continue;
            }

            // ⛔⛔ A DEGRADED value never reaches generated source. The catalog knows the
            // attribute but cannot parse the value, and splicing it in produces a file the
            // user cannot build: `lbl.TextAlign = ContentAlignment.Bogus` is CS0117, and
            // `lbl.Enabled = maybe` does not even get past BasicLang. D9 requires the value to
            // be preserved in the DOCUMENT and shown frozen in the property grid — it says
            // nothing about emitting it, and emitting it breaks the build for a value the
            // designer has already told the user it cannot use.
            //
            // ⚠ A value that is ALREADY this property's source form is not degraded — it is
            // what the catalog would itself have written. `ContentAlignment.MiddleLeft` does
            // not parse as the designer's `Left`, but it is exactly what emitting `Left`
            // produces, so skipping it would strip the property for no reason. The catalog
            // answers that per row; a shape test cannot (FormPropertyDef.IsSourceForm).
            //
            // ⛔ DescribeRefusal is reached ONLY for a value that is truly Degraded: the
            // IsSourceForm test above short-circuits first, and DescribeRefusal throws for a value
            // the target accepts.
            if (property != null && !property.Accepts(value, FormTarget.WinForms) && !property.IsSourceForm(value))
            {
                diagnostics.Add(new DesignDiagnostic(
                    DesignCodes.DegradedProperty,
                    $"{DesignCodes.DegradedProperty}: '{control.Id}.{name}': " +
                    property.DescribeRefusal(value, FormTarget.WinForms) +
                    " It is not written into the generated code.",
                    filePath, 0, 0, IsWarning: true));
                continue;
            }

            // ⛔ A get-only collection is ADDED to, never assigned — `cmb.Items = "a,b"` is
            // CS0200, and BasicLang reports nothing because Items types as Object.
            if (property is { IsItemCollection: true })
            {
                foreach (var item in FormPropertyDef.SplitItems(value))
                {
                    body.Append($"{inner}{control.Id}.{name}.Add(\"{item.Replace("\"", "\"\"")}\")")
                        .Append(newline);
                }

                continue;
            }

            body.Append($"{inner}{control.Id}.{name} = {Literal(control, name, value)}").Append(newline);
        }
    }

    /// <summary>The wiring, shared by controls and components: <c>AddHandler</c> or <c>addEventListener</c> per bind.</summary>
    private static void AppendBinds(
        StringBuilder body, FormDocument form, FormControl control, string inner, string newline)
    {
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
                //
                // ⛔⛔ The CATALOG's spelling, never the document's. DOM event types are
                // case-SENSITIVE and addEventListener takes a STRING, so `addEventListener("Click",
                // …)` registers cleanly and is never called: a green build, a page that loads, and a
                // dead handler with no diagnostic anywhere. Every check that consults
                // `IsEmittedBind` compares OrdinalIgnoreCase and therefore AGREES the bind is
                // wired — only the string that reaches the browser disagreed. `FormBind.Event` is
                // deliberately left un-normalised in the DOCUMENT (it says so), so the one place
                // this can be put right is here, where the name leaves for the browser.
                //
                // An event the row does not declare never gets this far: `CheckControlBinds`
                // refuses the write with BL8032 first. The fallback keeps this honest anyway,
                // because the body is built BEFORE the checks run and thrown away when one refuses.
                var eventName = control.Definition is { } definition
                    ? CanonicalWebEvent(definition, bind) ?? bind.Event
                    : bind.Event;

                body.Append($"{inner}{control.Id}.addEventListener(\"{eventName}\", AddressOf {bind.Handler})")
                    .Append(newline);
            }
            else
            {
                // ⛔ NEVER `Handles` — it is lexed but never parsed, so the file would not build.
                body.Append($"{inner}AddHandler {control.Id}.{bind.Event}, AddressOf {bind.Handler}")
                    .Append(newline);
            }
        }
    }

    /// <summary>
    /// A control's position and size, in the WinForms idiom.
    ///
    /// <para>⛔⛔ <b>Geometry FANS IN.</b> <c>X="96" Y="80"</c> becomes ONE statement,
    /// <c>Location = New Point(96, 80)</c> — never <c>Location.X = 96</c>. <c>Location</c> returns
    /// a <c>Point</c> STRUCT, so assigning through it modifies a temporary and csc rejects it with
    /// <b>CS1612</b> ("cannot modify the return value ... because it is not a variable"). Measured
    /// 2026-09-13; the plan says not to assert the code without measuring it, so that is the
    /// measurement. BasicLang itself catches none of this — WinForms member access degrades to
    /// <c>Object</c> with no diagnostic — so the per-statement shape here is load-bearing.</para>
    /// </summary>
    private static void AppendPixelGeometry(
        StringBuilder body, FormControl control, string inner, string newline)
    {
        if (control.Geometry is not PixelGeometry pixel)
        {
            return;
        }

        if (pixel.X != 0 || pixel.Y != 0)
        {
            body.Append($"{inner}{control.Id}.Location = New Point({pixel.X}, {pixel.Y})").Append(newline);
        }

        if (pixel.Width != 0 || pixel.Height != 0)
        {
            body.Append($"{inner}{control.Id}.Size = New Size({pixel.Width}, {pixel.Height})").Append(newline);
        }

        if (!string.IsNullOrWhiteSpace(pixel.Dock))
        {
            body.Append($"{inner}{control.Id}.Dock = DockStyle.{pixel.Dock.Trim()}").Append(newline);
        }

        if (!string.IsNullOrWhiteSpace(pixel.Anchor))
        {
            body.Append($"{inner}{control.Id}.Anchor = {AnchorExpression(pixel.Anchor)}").Append(newline);
        }
    }

    /// <summary>
    /// A control's <c>Anchor</c> as BasicLang source.
    ///
    /// <para>⚠ A SINGLE edge keeps the readable named member — <c>AnchorStyles.Top</c>. Emitting
    /// <c>CType(1, AnchorStyles)</c> for it would be a readability regression for the common case,
    /// to no benefit.</para>
    ///
    /// <para>⛔ MULTIPLE edges become <c>CType(n, AnchorStyles)</c>, because BasicLang still has no
    /// bitwise <c>Or</c>: <c>AnchorStyles.Left Or AnchorStyles.Top</c> is rejected ("requires
    /// Boolean operands") and <c>|</c> lexes but does not parse. The cast is the only expressible
    /// form, and it is only expressible at all because
    /// <c>SemanticAnalyzer.RejectImpossibleConversion</c> now exempts unresolvable .NET types.</para>
    ///
    /// <para>⚠ The edge names ride along as a trailing comment. <c>CType(13, AnchorStyles)</c> tells
    /// a reader nothing on its own, and this region is code the user will read in their own file.</para>
    /// </summary>
    private static string AnchorExpression(string anchor)
    {
        var edges = SplitAnchor(anchor);
        if (edges.Length == 1)
        {
            return $"AnchorStyles.{edges[0]}";
        }

        // Unknown names are refused by CheckAnchors before this runs, so a miss here would be a
        // bug in that check rather than bad input — sum defensively rather than throwing mid-write.
        var value = edges.Sum(e => AnchorFlags.TryGetValue(e, out var flag) ? flag : 0);
        return $"CType({value}, AnchorStyles)   ' {string.Join(", ", edges)}";
    }

    /// <summary>
    /// True when the value is already BasicLang SOURCE rather than a document value.
    ///
    /// <para>⛔ Two conventions share one <c>Properties</c> dictionary: the document reader stores
    /// the RAW attribute text (<c>Sign in</c>, unquoted), while a value that came from source keeps
    /// what was read (<c>"Sign in"</c> with its quotes, <c>ContentAlignment.MiddleLeft</c>). The
    /// CATALOG decides which is which — see <see cref="FormPropertyDef.IsSourceForm"/> for why the
    /// shape of the string is not a safe answer. Only a property the catalog does not know falls
    /// back to a shape, and then only to the two that are unambiguous in any language emitted
    /// here.</para>
    /// </summary>
    private static bool IsAlreadySource(FormPropertyDef? property, string value) =>
        property?.IsSourceForm(value) ??
        (value.StartsWith("\"", StringComparison.Ordinal) ||
         value.StartsWith("New ", StringComparison.Ordinal));

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
        var property = control.Definition?.Property(name);

        // Already a source literal — leave it exactly as read. Re-formatting it would produce
        // `ContentAlignment.ContentAlignment.MiddleLeft` for an enum and `""Sign in""` for a string.
        if (IsAlreadySource(property, value))
        {
            return value;
        }

        // ⛔ An enum or a colour is NOT the bare text. MEASURED, both ways:
        //   lbl.TextAlign = Center    lexes, compiles, and csc then rejects it (CS0103).
        //   lbl.ForeColor = #FF0000   does not even LEX -- '#' starts a preprocessor directive.
        // The catalog carries the WinForms enum type and the member mapping precisely so this
        // can be qualified rather than guessed at.
        var typed = property?.WinFormsLiteral(value);
        if (typed != null)
        {
            return typed;
        }

        return property?.Type switch
        {
            FormPropertyType.Int => value,
            FormPropertyType.Bool => bool.TryParse(value, out var flag) ? (flag ? "True" : "False") : value,
            FormPropertyType.Enum => value,
            FormPropertyType.Color => value,
            // ⛔ UNREACHABLE by construction, and loud if that ever stops being true. A Size reaches
            // here only when WinFormsLiteral declined it — i.e. it does not parse — and such a value
            // is either already source (returned above) or Degraded, which AppendProperties skips
            // before calling this. Quoting it (the default arm) would emit `X.ClientSize = "800x450"`,
            // CS0029 at csc with BasicLang silent; verbatim would splice unparsed text into source.
            // Both hide a broken invariant as a broken build, so this names the invariant instead.
            FormPropertyType.Size => throw new InvalidOperationException(
                $"'{control.Id}.{name}' = '{value}' is not a parsable Size and reached the region writer; " +
                "a Degraded value must be skipped before Literal is called."),
            _ => "\"" + value.Replace("\"", "\"\"") + "\""
        };
    }

    /// <summary>
    /// The BasicLang type a control's field is declared as: the catalog's WinForms type on the
    /// desktop, and <c>Element</c> on the web — the typed DOM handle every element comes back as.
    /// A script-backed component (Task 25) declares its <see cref="FormWebScript.FieldType"/>
    /// instead: a Timer is an <c>Integer</c> handle, not an element.
    /// </summary>
    private static string DeclaredType(FormDocument form, FormControl control)
    {
        if (form.Target == FormTarget.Web)
        {
            return control.Definition?.WebScript?.FieldType ?? "Element";
        }

        return control.Definition?.WinFormsType ?? "Control";
    }

    private static DesignDiagnostic Error(string code, string message, string filePath, int line) =>
        new(code, $"{code}: {message}", filePath, line, 0, IsWarning: false);
}
