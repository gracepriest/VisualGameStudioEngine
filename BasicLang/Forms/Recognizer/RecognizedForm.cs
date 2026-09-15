namespace BasicLang.Forms.Recognizer;

/// <summary>
/// A source construct the recognizer understood but will not import, with the reason.
///
/// <para>D9 distinguishes <b>Refused</b> from <b>ignored</b>, and the distinction is the whole
/// point: an ignored construct produces a form that looks complete and is silently wrong, while a
/// refusal stops the import and says why. Both refusals in v1 are shapes that would otherwise
/// produce a designer view that disagrees with what the program actually does.</para>
/// </summary>
/// <param name="Code">A <c>BL8xxx</c> code; the diagnostic band is registered in Task 6.</param>
public sealed record RecognitionRefusal(string Code, string Message, int Line, int Column);

/// <summary>One control recovered from source.</summary>
public sealed class RecognizedControl
{
    /// <summary>The identifier the source uses — the field name, or the local in a DOM page.</summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// The source type: a WinForms type name (<c>Label</c>) or an HTML tag (<c>h1</c>).
    /// Not necessarily a catalog kind — see <see cref="CatalogKind"/>.
    /// </summary>
    public string TypeName { get; set; } = "";

    /// <summary>
    /// The catalog kind this maps to, or null when the catalog has no row for it. Null is reported
    /// rather than dropped: a form containing one unsupported control must say so, not come back
    /// looking complete with the control missing.
    /// </summary>
    public string? CatalogKind { get; set; }

    /// <summary>
    /// Property assignments, in source order, keyed by property name. Values are the RAW source
    /// text of the right-hand side — <c>New Point(20, 20)</c>, not a parsed point. The recognizer
    /// is an importer (D12): preserving what the user wrote lets a value it cannot interpret still
    /// round-trip, and the tiering in D9 decides later what is editable.
    /// </summary>
    public Dictionary<string, string> Properties { get; } = new(StringComparer.Ordinal);

    public List<RecognizedHandler> Handlers { get; } = new();

    /// <summary>Where the field was declared, if it was. 0 when the control is a local.</summary>
    public int DeclarationLine { get; set; }

    /// <summary>Where the control was constructed (<c>= New Label()</c> / <c>createElement</c>).</summary>
    public int ConstructionLine { get; set; }

    /// <summary>True once something parented it (<c>Controls.Add</c> / <c>appendChild</c>).</summary>
    public bool IsParented { get; set; }
}

/// <summary>One event wiring recovered from source.</summary>
/// <param name="Event">The event name in the target's vocabulary: <c>Click</c> / <c>click</c>.</param>
/// <param name="Handler">
/// The named <c>Sub</c>, or null for an inline lambda. Null is not a failure — the web template
/// wires <c>Sub(e As DomEvent) counter.Bump()</c> inline, and that is idiomatic.
/// </param>
/// <param name="IsInline">True when the handler was a lambda rather than an <c>AddressOf</c> target.</param>
public sealed record RecognizedHandler(string Event, string? Handler, bool IsInline, int Line);

/// <summary>What one dialect recovered from one source file.</summary>
public sealed class RecognizedForm
{
    /// <summary>The class the controls belong to, or null for a DOM page with no class.</summary>
    public string? ClassName { get; set; }

    /// <summary>
    /// The member the controls were built in — <c>New</c>, <c>InitializeComponent</c>, <c>Main</c>,
    /// or any other. Recorded rather than assumed: neither shipped template uses
    /// <c>InitializeComponent</c>, and a reader that required it would recover nothing from both
    /// while its tests stayed green.
    /// </summary>
    public string? BuiltIn { get; set; }

    /// <summary>The base type from <c>Inherits</c>, when present.</summary>
    public string? BaseType { get; set; }

    public List<RecognizedControl> Controls { get; } = new();

    /// <summary>Form-level assignments (<c>Me.Text = …</c>), raw right-hand sides.</summary>
    public Dictionary<string, string> FormProperties { get; } = new(StringComparer.Ordinal);

    public List<RecognitionRefusal> Refusals { get; } = new();

    /// <summary>
    /// Why the file could not be read at all, or null when it could. Set when the LEXER refused the
    /// source — an unterminated string literal being the common case while the user is mid-edit.
    /// Distinct from a refusal: a refusal means "this shape is understood and must not be imported",
    /// this means "the text could not be turned into tokens".
    /// </summary>
    public string? UnreadableReason { get; set; }

    /// <summary>True when nothing may be imported from this file.</summary>
    public bool IsRefused => Refusals.Count > 0;

    /// <summary>Controls whose type has no catalog row — reported so the import cannot look complete.</summary>
    public IEnumerable<RecognizedControl> UnsupportedControls =>
        Controls.Where(c => c.CatalogKind == null);

    public RecognizedControl? this[string id] =>
        Controls.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.Ordinal));
}
