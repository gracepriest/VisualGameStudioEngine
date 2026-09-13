using System.Xml.Linq;

namespace BasicLang.Forms;

/// <summary>
/// An event wiring: <c>&lt;Bind Event="click" Handler="btnLogin_Click"/&gt;</c>.
///
/// <para>This is the only binding form v1 reads. <c>Property</c>/<c>Source</c>/<c>Path</c> are
/// reserved for data binding and are <b>refused</b> by <c>design --check</c> if populated — parsed
/// so a document carrying them round-trips, never acted on.</para>
/// </summary>
public sealed class FormBind
{
    /// <summary>
    /// The event name in the TARGET's vocabulary: <c>Click</c> on WinForms, <c>click</c> on the web
    /// (D8). Deliberately not normalised — the two targets never mix, and rewriting the case would
    /// make the document lie about what it wires.
    /// </summary>
    public string Event { get; set; } = "";

    /// <summary>The BasicLang <c>Sub</c> the event calls. Underscores are fine — the PascalCase
    /// type-name heuristic applies to TYPE names, not identifiers.</summary>
    public string Handler { get; set; } = "";

    // --- reserved (D2 "Shared rules"); parsed for round-trip, refused by --check ---
    public string? Property { get; set; }
    public string? Source { get; set; }
    public string? Path { get; set; }

    public bool UsesReservedDataBinding => Property != null || Source != null || Path != null;

    public FormBind Clone() => new()
    {
        Event = Event, Handler = Handler, Property = Property, Source = Source, Path = Path
    };
}

/// <summary>
/// One control in a form document.
///
/// <para>⚠ <see cref="UnknownAttributes"/> and <see cref="UnknownChildren"/> are not defensive
/// padding — they are the mechanism behind D9's "elements and attributes the reader does not
/// recognise round-trip untouched". Without them, opening a document written by a newer designer and
/// saving it would silently delete whatever that version added, which is the same class of defect as
/// the project-file destruction Slice 0 fixes.</para>
/// </summary>
public sealed class FormControl
{
    /// <summary>The catalog kind — the document element name, e.g. <c>Button</c>.</summary>
    public string Kind { get; set; } = "";

    /// <summary>
    /// The control's BasicLang identifier and its DOM <c>id</c>. Unique per form.
    /// Must be a legal BasicLang identifier; underscores ARE permitted.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Written on every control in v1, defaulting to document order on creation. Explicit rather
    /// than implied by document order so reordering the XML cannot silently reorder tab focus.
    /// </summary>
    public int TabIndex { get; set; }

    public FormGeometry? Geometry { get; set; }

    /// <summary>
    /// Catalog properties, keyed by the catalog's spelling. Ordered so a save does not reshuffle
    /// attributes the user has read a hundred times.
    /// </summary>
    public Dictionary<string, string> Properties { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<FormBind> Binds { get; } = new();

    /// <summary>Child controls. Only meaningful when the catalog marks the kind a container.</summary>
    public List<FormControl> Children { get; } = new();

    /// <summary>Attributes the reader did not recognise, preserved verbatim (D9).</summary>
    public Dictionary<string, string> UnknownAttributes { get; } = new(StringComparer.Ordinal);

    /// <summary>Child elements the reader did not recognise, preserved verbatim (D9).</summary>
    public List<XElement> UnknownChildren { get; } = new();

    public FormControlDef? Definition => FormControlCatalog.Find(Kind);

    /// <summary>
    /// Every control in this subtree, parents before children. The order the markup emitter and
    /// the region writer both walk in, so a container's declaration precedes its children's.
    /// </summary>
    public IEnumerable<FormControl> SelfAndDescendants()
    {
        yield return this;
        foreach (var descendant in Children.SelectMany(c => c.SelfAndDescendants()))
        {
            yield return descendant;
        }
    }

    public FormControl Clone()
    {
        var copy = new FormControl
        {
            Kind = Kind,
            Id = Id,
            TabIndex = TabIndex,
            Geometry = Geometry?.Clone()
        };

        foreach (var (key, value) in Properties)
        {
            copy.Properties[key] = value;
        }

        foreach (var bind in Binds)
        {
            copy.Binds.Add(bind.Clone());
        }

        foreach (var child in Children)
        {
            copy.Children.Add(child.Clone());
        }

        foreach (var (key, value) in UnknownAttributes)
        {
            copy.UnknownAttributes[key] = value;
        }

        foreach (var unknown in UnknownChildren)
        {
            // XElement is mutable and parented; copy so the clone cannot reparent the original.
            copy.UnknownChildren.Add(new XElement(unknown));
        }

        return copy;
    }
}
