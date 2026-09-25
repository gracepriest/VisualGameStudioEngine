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

    /// <summary>
    /// Attributes the reader did not recognise, preserved verbatim (D9).
    ///
    /// <para>⚠ Case-INSENSITIVE, matching <see cref="Properties"/>. XML attribute names are
    /// case-sensitive, so a document could carry both <c>Text</c> and <c>text</c>; with an ordinal
    /// comparer here, the first would land in <see cref="Properties"/> and the second here, both
    /// would serialize, and on read-back the catalog's case-insensitive lookup would route them into
    /// the same slot — silently dropping one. Sharing the comparer means the collision cannot be
    /// created in the first place.</para>
    /// </summary>
    public Dictionary<string, string> UnknownAttributes { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Child elements the reader did not recognise, preserved verbatim (D9).</summary>
    public List<XElement> UnknownChildren { get; } = new();

    public FormControlDef? Definition => FormControlCatalog.Find(Kind);

    /// <summary>
    /// ⛔⛔ <b>THE one answer to "which edge is this strip docked to".</b> Every consumer asks it
    /// here — the web emitter (<c>FormAssetEmitter.Html</c>, which puts a Bottom strip's
    /// <c>&lt;footer&gt;</c> after the form div) and the designer canvas
    /// (<c>FormCanvasTransform.Bands</c>, which draws its band at the bottom of the surface).
    ///
    /// <para>⛔ Those two were written as a MIRRORED PAIR and consolidated here the moment the
    /// second one was needed. Two copies of this lookup is not a style problem: they read ONE
    /// document, so a drifted copy makes the designer draw the status band on one edge while the
    /// page puts the footer on the other, with nothing on screen looking wrong and nothing failing
    /// anywhere. The repo already carries that scar twice — <c>FormAssetEmitter.Tracks</c> ↔
    /// <c>FormGridLayout.ParseTracks</c>, and the four private copies of "how big is the form"
    /// that became <c>FormCanvasTransform.SurfaceSize</c>.</para>
    ///
    /// <para>⚠ This is a <see cref="FormPlace.Docked"/> row's <c>Dock</c> PROPERTY — not
    /// <c>PixelGeometry.Dock</c>, which is a positioned control's DockStyle and a different
    /// question entirely.</para>
    /// </summary>
    public bool IsDockedToBottom =>
        Definition?.Place == FormPlace.Docked &&
        string.Equals(DockEdge, "Bottom", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The resolved edge: the DOCUMENT's own value first, then the catalog row's default, then Top.
    ///
    /// <para>⚠ The row default is not a nicety. A hand-written <c>.blform</c>/<c>.blwebform</c> need
    /// not carry the attribute — the designer writes it on every save, but the file is the user's —
    /// and reading only the property would silently put a hand-written StatusStrip at the TOP.</para>
    ///
    /// <para>⚠ The empty-string guard is REACHABLE, not defensive padding: <c>Dock=""</c> is legal
    /// XML, and <c>FormDocumentReader</c> stores an attribute's value into
    /// <see cref="Properties"/> verbatim BEFORE it asks whether the catalog accepts it (the D9
    /// Degraded tier keeps a bad value so it round-trips). Without the guard, <c>""</c> is simply
    /// "not Bottom", so a StatusStrip written <c>Dock=""</c> docks to the TOP instead of falling
    /// back to its row's own Bottom.</para>
    /// </summary>
    private string DockEdge =>
        Properties.TryGetValue("Dock", out var dock) && !string.IsNullOrEmpty(dock)
            ? dock
            : Definition?.Property("Dock")?.Default ?? "Top";

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
