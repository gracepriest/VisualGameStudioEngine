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
    //   BL8011, BL8012  designer-owned region states
    //   BL8021, BL8022  document-level refusals in a form document (here)
    //   BL8031          reserved
    //
    // Nothing in this band lives in BasicLang.Compiler.ErrorCode as a string; the enum registration
    // there is for discoverability only, and adding a member there does not claim a number here.

    /// <summary>A <c>Handles</c> clause — lexed but never parsed, so the file cannot build.</summary>
    public const string HandlesClause = "BL8002";

    /// <summary>A <c>With</c> block over a control — its property assignments are silently discarded.</summary>
    public const string WithBlock = "BL8003";

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
    /// A handler is declared AFTER the designer region that wires it. An <c>AddressOf</c> naming a
    /// later-declared <c>Sub</c> erases its parameter types and then fails to match the event's
    /// delegate.
    /// </summary>
    public const string HandlerDeclaredAfterWiring = "BL8013";

    /// <summary>The file has no designer regions — the import case, not an error.</summary>
    public const string RegionAbsent = "BL8014";

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

    /// <summary>The form document itself is not well-formed XML, or its root/version is not one we know.</summary>
    public const string MalformedDocument = "BL8008";

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
/// <para>In this slice it checks <b>recognizer input</b>: a <c>.bas</c> with a recoverable form
/// shape. It gains the <c>.blform</c>/<c>.blwebform</c> document formats when those exist.</para>
/// </summary>
public static class DesignCheck
{
    /// <summary>Findings for one file, dispatched on its extension.</summary>
    public static IReadOnlyList<DesignDiagnostic> Check(string filePath, string text) =>
        Path.GetExtension(filePath).Equals(".blwebform", StringComparison.OrdinalIgnoreCase)
            ? CheckWebForm(filePath, text)
            : CheckSource(filePath, text);

    /// <summary>
    /// Findings for a <c>.blwebform</c> document: everything the reader reported, plus the
    /// per-property Degraded rows as warnings.
    /// </summary>
    public static IReadOnlyList<DesignDiagnostic> CheckWebForm(string filePath, string text)
    {
        var form = Serialization.BlWebFormReader.Read(filePath, text);
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
