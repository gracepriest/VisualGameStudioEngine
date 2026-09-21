using BasicLang.Forms.Recognizer;

namespace BasicLang.Forms;

/// <summary>
/// One design-time finding. <b>Collected into a list, never thrown</b> (D10).
///
/// <para>Modelled on <c>CppDiagnostic</c> (<c>ProjectSystem/CppDiagnosticsParser.cs</c>), which
/// already carries exactly these six fields and is already collected rather than thrown. A separate
/// record because <c>CppDiagnostic</c> is C++-named, mutable, and lives in the project-system
/// namespace; a design finding is neither of the first two and should not be either.</para>
///
/// <para>⛔ The code appears in BOTH <see cref="Code"/> and the rendered message. That is not
/// redundancy. Two of the CLI's own print sites emit only <c>{label}: {Message}</c> and drop the
/// code field entirely, so a code carried only in the field is invisible on those routes —
/// <c>Compiler.cs</c> puts the code in the message string for the same reason.</para>
/// </summary>
public sealed record DesignDiagnostic(
    string Code,
    string Message,
    string FilePath,
    int Line,
    int Column,
    bool IsWarning)
{
    /// <summary>
    /// MSBuild-style single line: <c>path(line,col): error CODE: message</c>.
    ///
    /// <para>⛔ Deliberately <c>CppDiagnostic.FormatNormalized</c>'s shape, character for character,
    /// because that is the format the IDE Output panel's click-to-navigate regex matches. The other
    /// shape in the CLI — <c>{label}: {Code}: {Message}</c> — carries no file, line or column, and
    /// a finding a user cannot click through to is a finding they will not act on.</para>
    /// </summary>
    public string Format()
    {
        var kind = IsWarning ? "warning" : "error";
        var location = Line > 0
            ? (Column > 0 ? $"{FilePath}({Line},{Column})" : $"{FilePath}({Line})")
            : FilePath;
        return $"{location}: {kind} {Code}: {Message}";
    }
}

/// <summary>
/// The <c>BL8xxx</c> band: design-time findings.
///
/// <para>Held as constants beside their meaning rather than only in
/// <c>BasicLang.Compiler.ErrorCode</c>, because the string is what reaches the user and the string
/// is what a CI grep matches. The enum registration exists so the band is discoverable from the
/// compiler's own error catalogue.</para>
/// </summary>
public static class DesignCodes
{
    // ⛔ THE BAND ALLOCATION. The spec and plan assign specific numbers to specific tasks, and the
    // recognizer's codes were originally numbered from BL8001 without checking — colliding with the
    // number the plan reserves for the compile-route skip. Claim from the free range below, and
    // check BOTH documents before taking a number:
    //
    //   BL8001          reserved — "a form document is not a program; build the project"
    //   BL8002..BL8010  recognizer and design-check findings (here)
    //   BL8010          illegal/missing control id (here)
    //   BL8011..BL8017  designer-owned region states, region-writer and document findings
    //   BL8018          the generated D7 dispatch helper is never called (here)
    //   BL8019          a toolbox drop the designer refused (here)
    //   BL8020          a component under <Controls>, or a control under <Components> (here)
    //   BL8021, BL8022  document-level refusals in a form document (here)
    //   BL8023..BL8026  retarget findings — what could not cross between the two formats (here)
    //   BL8027          retarget: "wired means running" crossed by rule (here)
    //   BL8028, BL8029  region-writer findings for a component the target cannot wire / does not have (here)
    //   BL8030          a strip item outside a host / a non-item in a host / a strip below the
    //                   top level (here)
    //   BL8031          reserved — the spec and plan both name it for ONE thing: the --check
    //                   collision between a user's own top-level name and the dispatch helper.
    //                   BL8018 below is a different finding and takes a different number.
    //
    // The ENUMERATED table above is now exhausted: the next claim starts at BL8032. That is not
    // the band being full — BL8032..BL8999 are simply unclaimed, and "full" would wrongly send the
    // next task looking for another band.
    //
    // Nothing in this band lives in BasicLang.Compiler.ErrorCode as a string; the enum registration
    // there is for discoverability only, and adding a member there does not claim a number here.

    /// <summary>A <c>Handles</c> clause — lexed but never parsed, so the file cannot build.</summary>
    public const string HandlesClause = "BL8002";

    /// <summary>A <c>With</c> block over a control — its property assignments are silently discarded.</summary>
    public const string WithBlock = "BL8003";

    /// <summary>
    /// The project has form pages but nothing calls the generated dispatch helper, so every page
    /// loads its script and shows nothing.
    /// </summary>
    public const string DispatchNotCalled = "BL8018";

    /// <summary>A control whose type has no catalog row, so the designer cannot edit it.</summary>
    public const string UnsupportedControl = "BL8004";

    /// <summary>A file the designer found no form shape in.</summary>
    public const string NoFormShape = "BL8005";

    /// <summary>A control that is constructed but never parented, so it never appears at run time.</summary>
    public const string OrphanedControl = "BL8006";

    /// <summary>The file's text could not be turned into tokens — typically an unterminated string.</summary>
    public const string Unreadable = "BL8007";

    /// <summary>
    /// A designer region was edited by hand, so regenerating it would discard that edit. The form
    /// opens read-only; Code view stays fully editable.
    /// </summary>
    public const string RegionHandEdited = "BL8011";

    /// <summary>A designer marker is missing its partner, nested, or duplicated. Never written to.</summary>
    public const string RegionMalformed = "BL8012";

    /// <summary>
    /// A control dragged from the toolbox could not be placed where it was dropped.
    ///
    /// <para>⛔ Exists so a refused drop is never SILENT. A drop that does nothing and says nothing
    /// is indistinguishable from a designer that ignored the gesture, crashed, or was never wired
    /// up — which is exactly how a user experiences a missing feature. The reasons are all
    /// actionable: an unknown kind, a kind the target does not have, or a web document, whose
    /// controls are positioned by its layout rather than by pixels.</para>
    /// </summary>
    public const string PlacementRefused = "BL8019";

    /// <summary>
    /// A handler is declared AFTER the designer region that wires it. An <c>AddressOf</c> naming a
    /// later-declared <c>Sub</c> erases its parameter types and then fails to match the event's
    /// delegate.
    /// </summary>
    public const string HandlerDeclaredAfterWiring = "BL8013";

    /// <summary>The file has no designer regions — the import case, not an error.</summary>
    public const string RegionAbsent = "BL8014";

    /// <summary>
    /// An <c>Anchor</c> naming an edge <c>AnchorStyles</c> does not have.
    ///
    /// <para>⚠ This USED to mean "anchored to more than one edge", which BasicLang could not express
    /// at all. MEASURED 2026-09-13 and again 2026-09-18: <c>AnchorStyles.Left Or AnchorStyles.Top</c>
    /// is still rejected ("Logical operator 'Or' requires Boolean operands") and <c>|</c> still lexes
    /// without parsing — but <c>CType(7, AnchorStyles)</c> now works, because
    /// <c>SemanticAnalyzer.RejectImpossibleConversion</c> exempts unresolvable .NET types. csc
    /// accepts <c>(AnchorStyles)7</c>; it was the front end refusing, not the language lacking a
    /// form. Multi-edge anchors are therefore EMITTED, and this code is left for the one case that
    /// is still wrong: a misspelled edge, which would sum to zero and silently anchor the control
    /// to nothing.</para>
    /// </summary>
    public const string AnchorNotExpressible = "BL8015";

    /// <summary>
    /// A property the document carries that does not exist on the target being generated — a
    /// <c>GroupName</c> in a <c>.blform</c>, say. A WARNING: the value round-trips untouched and
    /// the other target still uses it, but it is skipped here rather than emitted into code that
    /// would not compile. Saying so beats dropping it silently.
    /// </summary>
    public const string PropertyNotOnTarget = "BL8016";

    /// <summary>
    /// A <c>&lt;Bind&gt;</c> carrying the reserved data-binding attributes (<c>Property</c>,
    /// <c>Source</c>, <c>Path</c>). Reserved means parsed and round-tripped, never acted on — so a
    /// document that populates them is refused rather than silently ignored, which would leave the
    /// user believing a binding exists.
    /// </summary>
    public const string ReservedBindingPopulated = "BL8021";

    /// <summary>
    /// A <c>{res:Key}</c> resource reference. <c>&lt;Resources&gt;</c> is reserved and empty in v1,
    /// so a value referring into it cannot be resolved; refusing beats writing back a reference to
    /// nothing.
    /// </summary>
    public const string ReservedResourceReference = "BL8022";

    // ==================================================================
    // Retarget (Task 21). All four are WARNINGS: the conversion succeeded, and each one names a
    // thing the other format could not take so the user can review it. A retarget that failed
    // outright is the SOURCE being refused, which is the reader's own error, not one of these.
    // ==================================================================

    /// <summary>
    /// A control whose kind has no catalog row on the destination — a <c>DataGridView</c> going to
    /// the web. Removed, with its children moved up into its place so their work survives.
    /// </summary>
    public const string RetargetControlLost = "BL8023";

    /// <summary>
    /// A property the destination does not have — <c>DecimalPlaces</c> going to the web,
    /// <c>GroupName</c> going to WinForms — or an attribute the destination would read as layout.
    /// Dropped rather than carried as dead data the property grid would then show.
    /// </summary>
    public const string RetargetPropertyLost = "BL8024";

    /// <summary>
    /// The hard edge: absolute pixels ⇄ grid cells. One per control, saying what it had and where
    /// it landed; one for the document, saying what the window (size, caption) or the page
    /// (<c>&lt;Layout&gt;</c>, <c>&lt;Literal&gt;</c>) lost.
    /// </summary>
    public const string RetargetLayoutCrossed = "BL8025";

    /// <summary>
    /// A <c>&lt;Bind&gt;</c> on an event the catalog cannot name on the destination. Only a kind's
    /// default event has a measured name on both sides (D8); anything else is dropped and named,
    /// because carrying <c>MouseEnter</c> into <c>addEventListener</c> registers cleanly and never
    /// fires.
    /// </summary>
    public const string RetargetBindLost = "BL8026";

    /// <summary>
    /// "Wired means running" crossed by RULE (Task 25 review). A web script component runs the
    /// moment it is wired; WinForms says the same thing with a property — a Timer's
    /// <c>Enabled=True</c>. The catalog row states the equivalence (<c>FormWebScript.Implies</c>),
    /// and the retarget applies it in both directions and names what it did. The loss this replaces
    /// was silent either way: a wired web Timer arriving on the window with <c>Enabled</c> absent
    /// (default False) never fired, and a disabled WinForms Timer arriving on the page ran from load.
    /// </summary>
    public const string RetargetRunStateCrossed = "BL8027";

    /// <summary>
    /// A <c>&lt;Bind&gt;</c> the target being generated cannot wire — a web component's bind on
    /// anything but its default event, when its only wiring is its construct template. A WARNING:
    /// the document keeps it and WinForms wires it, but it is not written here. It used to vanish
    /// silently while STILL driving the BL8013 ordering refusal, so the user was told to move a
    /// handler above a region that never wired it (review, 2026-09-19).
    /// </summary>
    public const string BindNotOnTarget = "BL8028";

    /// <summary>
    /// A component whose kind has no row on the target being generated — a ToolTip in a
    /// <c>.blwebform</c>, which the toolbox never offers but a hand edit can carry. A WARNING: its
    /// field is declared so code naming it still builds, but nothing constructs or wires it and it
    /// does nothing on the page. The retarget names the same kind as BL8023; the design route used
    /// to name it nowhere (review, 2026-09-19).
    /// </summary>
    public const string KindNotOnTarget = "BL8029";

    /// <summary>
    /// A component kind under <c>&lt;Controls&gt;</c>, or a control kind under
    /// <c>&lt;Components&gt;</c> (Task 25). A REFUSAL: either would generate code csc rejects —
    /// <c>Me.Controls.Add(tmr)</c> for a Timer, or a Button that is constructed and never added —
    /// and a document that says two different things about where a thing lives is refused rather
    /// than half-read.
    /// </summary>
    public const string ComponentMisplaced = "BL8020";

    /// <summary>
    /// A strip or a strip item that is not where its kind can live (Task 24, spec §3). A REFUSAL
    /// <b>three ways</b>, because all three shapes generate code that does not compile — or a
    /// document that says something the designer cannot mean:
    ///
    /// <list type="bullet">
    /// <item>an ITEM outside a host that lists its kind — a <c>ToolStripMenuItem</c> under
    /// <c>&lt;Controls&gt;</c> would be added with <c>Me.Controls.Add</c>, which does not compile
    /// (a <c>ToolStripItem</c> is not a <c>Control</c>);</item>
    /// <item>a NON-ITEM under a host — a <c>Button</c> under a <c>MenuStrip</c>, which holds only
    /// the item kinds its row lists;</item>
    /// <item>a STRIP below the top level — a <c>MenuStrip</c> under a <c>Panel</c>. A strip docks
    /// to the FORM, and its <c>Dock</c> property means nothing anywhere else.</item>
    /// </list>
    ///
    /// <para>⛔ Refused rather than warned, for the reason BL8020 is: a document that says two
    /// different things about where a thing lives is refused rather than half-read. Reading it
    /// anyway would put the control somewhere the user did not write it and then generate code
    /// csc rejects, from a designer that reported the document clean.</para>
    /// </summary>
    public const string StripMisplaced = "BL8030";

    /// <summary>The form document itself is not well-formed XML, or its root/version is not one we know.</summary>
    public const string MalformedDocument = "BL8008";

    /// <summary>
    /// A control whose <c>Id</c> is missing or is not a legal BasicLang identifier.
    ///
    /// <para>⛔⛔ A refusal, not a warning. The <c>Id</c> becomes a FIELD NAME in the user's own
    /// <c>.bas</c>: an empty one generates <c>Private  As Button</c> and
    /// <c>Me.Controls.Add()</c>, and <c>my-button</c> generates <c>my-button = New Button()</c>.
    /// Both are syntax errors written into a file the user owns, by a designer that reported the
    /// document clean.</para>
    /// </summary>
    public const string IllegalControlId = "BL8010";

    /// <summary>
    /// Two controls sharing an <c>Id</c>. Also a refusal: the generated field is declared twice,
    /// and every reference to it is ambiguous.
    /// </summary>
    public const string DuplicateControlId = "BL8017";

    /// <summary>
    /// A property whose attribute the catalog knows but whose value does not parse — D9's
    /// <b>Degraded</b> tier. One frozen property-grid row, the rest of the control still editable,
    /// and the value round-trips unchanged.
    /// </summary>
    public const string DegradedProperty = "BL8009";
}

/// <summary>
/// Validates what the designer would read from a source file — the engine behind
/// <c>basiclang design --check</c>.
///
/// <para>It checks two kinds of input: a form DOCUMENT (<c>.blform</c> or <c>.blwebform</c>), read
/// through the structure-preserving reader and reported with its tiers; and <b>recognizer input</b>
/// — a <c>.bas</c> with a recoverable form shape, for the import route of D12.</para>
/// </summary>
public static class DesignCheck
{
    /// <summary>
    /// Findings for one file, dispatched on its extension.
    ///
    /// <para>⛔ Both document extensions route here, and the set comes from
    /// <c>FormDocumentReader.TargetOfExtension</c> rather than from a literal repeated in this file.
    /// A second list of extensions is a second thing to update, and the failure mode of missing one
    /// is silent: a <c>.blform</c> would be handed to the SOURCE checker, which would lex XML as
    /// BasicLang, find no form shape, and report BL8005 — a clean-looking answer to a question
    /// nobody asked.</para>
    /// </summary>
    public static IReadOnlyList<DesignDiagnostic> Check(string filePath, string text) =>
        Serialization.FormDocumentReader.TargetOfExtension(filePath) != null
            ? CheckFormDocument(filePath, text)
            : CheckSource(filePath, text);

    /// <summary>
    /// Findings for a form document: everything the reader reported, plus the per-property Degraded
    /// rows as warnings. Both formats — the reader picks the vocabulary from the document itself.
    /// </summary>
    public static IReadOnlyList<DesignDiagnostic> CheckFormDocument(string filePath, string text)
    {
        var form = Serialization.FormDocumentReader.Read(filePath, text);
        var findings = form.Diagnostics.ToList();

        if (form.IsRefused)
        {
            // Document-level refusal: the form opens read-only and the naming diagnostic is the
            // point. Listing frozen rows underneath it buries the reason the document is refused.
            return findings;
        }

        foreach (var degraded in form.Degraded)
        {
            findings.Add(new DesignDiagnostic(
                DesignCodes.DegradedProperty,
                $"{DesignCodes.DegradedProperty}: '{degraded.ControlId}.{degraded.Property}' is " +
                $"frozen in the property grid — {degraded.Reason}",
                filePath, 0, 0, IsWarning: true));
        }

        return findings;
    }

    /// <summary>Findings for one source file. Never throws for bad content — that is a finding, not a crash.</summary>
    public static IReadOnlyList<DesignDiagnostic> CheckSource(string filePath, string source)
    {
        var findings = new List<DesignDiagnostic>();

        // Pick the dialect by what the file actually contains. A file that looks like neither is
        // reported as having no form shape rather than being run through an arbitrary reader.
        var isWinForms = WinFormsDialect.Looks(source);
        var isDom = DomDialect.Looks(source);

        if (!isWinForms && !isDom)
        {
            findings.Add(new DesignDiagnostic(
                DesignCodes.NoFormShape,
                $"{DesignCodes.NoFormShape}: no form or page shape was found in this file. The " +
                "designer reads a WinForms class (Inherits Form) or a page that calls createElement.",
                filePath, 0, 0, IsWarning: true));
            return findings;
        }

        var form = isWinForms ? WinFormsDialect.Read(source) : DomDialect.Read(source);

        if (form.UnreadableReason != null)
        {
            // A WARNING, not an error. A file the lexer refuses will fail the compiler anyway with
            // its own diagnostic, and `design --check` has no business double-reporting it as a
            // second build failure. What matters here is only that the designer could not read it.
            findings.Add(new DesignDiagnostic(
                DesignCodes.Unreadable,
                $"{DesignCodes.Unreadable}: the designer could not read this file — " +
                $"{form.UnreadableReason}",
                filePath, 0, 0, IsWarning: true));
            return findings;
        }

        // Refusals first, and they are errors: each names a construct that makes the designer view
        // and the running program disagree (D9 Refused).
        foreach (var refusal in form.Refusals)
        {
            findings.Add(new DesignDiagnostic(
                refusal.Code,
                $"{refusal.Code}: {refusal.Message}",
                filePath, refusal.Line, refusal.Column, IsWarning: false));
        }

        if (form.IsRefused)
        {
            // Nothing below is meaningful once the file is refused — reporting orphans in a form
            // that cannot be imported is noise that buries the reason it cannot be.
            return findings;
        }

        if (form.Controls.Count == 0)
        {
            findings.Add(new DesignDiagnostic(
                DesignCodes.NoFormShape,
                $"{DesignCodes.NoFormShape}: the file looks like a form but no controls were " +
                "recovered from it.",
                filePath, 0, 0, IsWarning: true));
        }

        foreach (var control in form.Controls)
        {
            if (control.CatalogKind == null)
            {
                findings.Add(new DesignDiagnostic(
                    DesignCodes.UnsupportedControl,
                    $"{DesignCodes.UnsupportedControl}: '{control.Id}' is a '{control.TypeName}', " +
                    "which the designer catalog has no row for. It is preserved but cannot be " +
                    "edited on the canvas.",
                    filePath, LineOf(control), 0, IsWarning: true));
            }

            if (!control.IsParented && control.ConstructionLine > 0)
            {
                findings.Add(new DesignDiagnostic(
                    DesignCodes.OrphanedControl,
                    $"{DesignCodes.OrphanedControl}: '{control.Id}' is created but never added to " +
                    "the form, so it will not appear when the program runs.",
                    filePath, control.ConstructionLine, 0, IsWarning: true));
            }
        }

        return findings;
    }

    public static IReadOnlyList<DesignDiagnostic> CheckFile(string filePath)
    {
        try
        {
            return Check(filePath, File.ReadAllText(filePath));
        }
        // UnauthorizedAccessException derives from SystemException, NOT IOException — a read-only or
        // ACL-denied file, or a directory passed where a file was meant, would otherwise escape a
        // method whose contract is "a bad file is a finding, not a crash".
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new[]
            {
                new DesignDiagnostic(
                    DesignCodes.NoFormShape,
                    $"{DesignCodes.NoFormShape}: could not read the file — {ex.Message}",
                    filePath, 0, 0, IsWarning: false)
            };
        }
    }

    private static int LineOf(RecognizedControl control) =>
        control.ConstructionLine > 0 ? control.ConstructionLine : control.DeclarationLine;
}
