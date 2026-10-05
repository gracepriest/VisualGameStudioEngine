using System.Xml.Linq;

namespace BasicLang.Forms;

/// <summary>
/// A form document — the truth the designer edits. One of the two formats of D2, selected by
/// <see cref="Target"/>.
///
/// <para>This type is the MODEL only: it holds no file path and does no I/O. The
/// structure-preserving reader and writer are a separate concern (Task 9) precisely because
/// round-tripping unknown content is the hard part and does not belong in the shape everything
/// else depends on.</para>
/// </summary>
public sealed class FormDocument
{
    /// <summary>Which format this is. Decides the layout vocabulary and the usable catalog (D2/D3).</summary>
    public FormTarget Target { get; set; } = FormTarget.Web;

    /// <summary>
    /// The form's name, and the BasicLang class name the region writer generates against.
    ///
    /// <para>⛔ Must not contain an underscore. The PascalCase heuristic at
    /// <c>SemanticAnalyzer.cs:7894</c> passes the TYPE name, and a type name containing <c>_</c>
    /// falls out of it — rename <c>MainForm</c> to <c>Main_Form</c> and every <c>Me.&lt;inherited
    /// member&gt;</c> becomes a hard error. Control <see cref="FormControl.Id"/>s are identifiers,
    /// not type names, and may contain underscores freely.</para>
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>Document format version. 1 in v1; a reader must refuse a version it does not know.</summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Where this document was read from, or empty for one built in memory.
    ///
    /// <para>⚠ Not part of the document — it is never written and never round-tripped. It is here
    /// because the build has to find the <c>.bas</c> that pairs with the form (D7's dispatch may
    /// only name a class that exists), and threading the path alongside every list of documents
    /// meant every caller could forget it.</para>
    /// </summary>
    public string SourcePath { get; set; } = "";

    /// <summary>The document element name: <c>Form</c> for .blform, <c>WebForm</c> for .blwebform.</summary>
    public string RootElementName => Target == FormTarget.WinForms ? "Form" : "WebForm";

    /// <summary>The file extension this document is persisted under, including the dot.</summary>
    public string FileExtension => Target == FormTarget.WinForms ? ".blform" : ".blwebform";

    // --- pixel documents: .blform, and a web Canvas page (spec 2026-09-27 D2) ---

    /// <summary>The form's client size — a window's, or a Canvas page's design size. Null on a Grid/Flow page.</summary>
    public int? Width { get; set; }
    public int? Height { get; set; }

    /// <summary>The design size a pixel document is given when it carries none (or a non-positive one).</summary>
    public const int DefaultDesignWidth = 400;
    public const int DefaultDesignHeight = 300;

    /// <summary>
    /// ⛔⛔ THE one answer to "how big is the form" (spec 2026-09-27 §2.4, scope call S3): the client size, or
    /// <see cref="DefaultDesignWidth"/>×<see cref="DefaultDesignHeight"/>. The canvas surface
    /// (<c>FormCanvasTransform.SurfaceSize</c>), the dock resolver and the page emitter all read it — a second
    /// copy of the fallback puts the page's anchors somewhere other than the surface the user designed on.
    /// </summary>
    public (int Width, int Height) DesignSize =>
        (Width is > 0 ? Width.Value : DefaultDesignWidth, Height is > 0 ? Height.Value : DefaultDesignHeight);

    // --- .blwebform only (D3) ----------------------------------------------

    /// <summary>How the page arranges its controls. Web only; null on a WinForms document.</summary>
    public FormLayout? Layout { get; set; }

    /// <summary>
    /// Markup that passes through to the output untouched and is shown read-only on the canvas —
    /// the <c>runat="server"</c> inversion of D9. Web only: a <c>.blform</c> has no markup to pass
    /// through.
    /// </summary>
    public string? Literal { get; set; }

    // --- both --------------------------------------------------------------

    /// <summary>
    /// The window caption on WinForms and the page <c>&lt;title&gt;</c> on the web (spec §2.3, D2 — one
    /// vocabulary). Null = the document does not say; the page title then falls back to <see cref="Name"/>.
    /// </summary>
    public string? Text { get; set; }

    /// <summary>Top-level controls, in document order.</summary>
    public List<FormControl> Controls { get; } = new();

    /// <summary>
    /// The tray (Task 25): non-visual components, in document order. Each is a
    /// <see cref="FormControl"/> with NO geometry, NO children and NO tab index — the reader is the
    /// one place that invariant is enforced (a component element never acquires them), and every
    /// walker chooses explicitly whether it visits this list: the region writer, the id namespace
    /// and the retarget do; the canvas, the markup emitter and tab renumbering do not.
    ///
    /// <para>⚠ Was a list of raw <c>XElement</c>s while the slot was reserved — and write-never:
    /// <c>Create</c> emitted an empty element and <c>Apply</c> never visited it.</para>
    /// </summary>
    public List<FormControl> Components { get; } = new();

    /// <summary>
    /// The FORM's own event wiring (spec §2.3) — the same <c>&lt;Bind&gt;</c> shape a control uses,
    /// written directly under the root element. Since slice 5 the region writer EMITS them against
    /// <c>FormControlCatalog.FormRoot</c>'s events: <c>AddHandler Me.Load, …</c> last on WinForms; on the page a
    /// <c>document.body</c>/<c>window</c> listener, or Load as <c>Me.&lt;handler&gt;()</c> at the end of
    /// <c>InitializeComponent</c> (ADR 0021). A web bind the Form does not wire there is refused (BL8032).
    /// </summary>
    public List<FormBind> Binds { get; } = new();

    /// <summary>
    /// Every control or component whose Id is EXACTLY the FORM's own name — refused with the duplicate-id code (BL8017) by
    /// the reader and the region writer alike (slice 5 review fix 2): a field named like its enclosing class is CS0542.
    /// ⚠ Exact (Ordinal), measured against the existing suite: a form <c>Pic</c> holding a control <c>pic</c> builds and runs
    /// on both targets (the image-copy acceptance fixtures), so refusing case variants refused working documents. The one
    /// real case-variant collision — two generated <c>VgsOn_</c> wrappers whose names differ only in case — is refused by
    /// the region writer where it actually arises (<c>RegionWriter.CheckWrapperNames</c>).
    /// </summary>
    public IEnumerable<FormControl> ControlsNamedLikeTheForm() =>
        string.IsNullOrEmpty(Name)
            ? Enumerable.Empty<FormControl>()
            : AllControls().Concat(AllComponents()).Where(c => string.Equals(c.Id, Name, StringComparison.Ordinal));

    /// <summary>The ONE BL8017 text for <see cref="ControlsNamedLikeTheForm"/>, shared by the reader and the region writer.</summary>
    public string NamedLikeTheFormMessage(FormControl control) =>
        $"the control Id '{control.Id}' is the form's own name '{Name}'. Ids become members of the form's class, and a " +
        "member cannot share its class's name (CS0542 on WinForms); on a web page its generated VgsOn_ wrappers would " +
        "collide with the form's. Rename the control.";

    /// <summary>
    /// The FORM's catalog-only root attributes (spec §2.3, slice 3) — every <see cref="FormControlCatalog.FormRoot"/> row
    /// that is not one of the typed fields above (FormBorderStyle, StartPosition, BackColor, Font, AcceptButton…), keyed by
    /// the row's name, holding the DOCUMENT's text exactly as a control's <see cref="FormControl.Properties"/> does: a
    /// value the row cannot use stays here verbatim and is Degraded (<c>FormFile.DegradedRoot</c>), never coerced.
    ///
    /// <para>⚠ ORDINAL, unlike a control's case-insensitive bag: a root row IS its XML attribute's spelling, and the
    /// reader, the tier lookup and <see cref="FormRootValues.RowForAttribute"/> all match attributes ordinally.</para>
    /// <para>⛔ Read and written only through <see cref="FormRootValues"/> (the one row→storage map) by everything but
    /// the reader and the writer, which own the attribute round trip.</para>
    /// </summary>
    public Dictionary<string, string> Properties { get; } = new(StringComparer.Ordinal);

    /// <summary>Reserved and empty in v1; parsed and re-emitted so a future document round-trips.</summary>
    public List<XElement> Resources { get; } = new();

    /// <summary>Root attributes and child elements the reader did not recognise (D9).</summary>
    public Dictionary<string, string> UnknownAttributes { get; } = new(StringComparer.Ordinal);
    public List<XElement> UnknownChildren { get; } = new();

    /// <summary>Every control in the document, containers before their children. NOT the components.</summary>
    public IEnumerable<FormControl> AllControls() => Controls.SelectMany(c => c.SelfAndDescendants());

    /// <summary>The tray's components. A separate walk on purpose — see <see cref="Components"/>.</summary>
    public IEnumerable<FormControl> AllComponents() => Components;

    /// <summary>
    /// A control OR a component by id. One class, one field namespace: a Timer called <c>btn</c>
    /// and a Button called <c>btn</c> collide in the generated code exactly as two Buttons do.
    /// </summary>
    public FormControl? FindById(string id) =>
        AllControls().Concat(Components).FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.Ordinal));

    /// <summary>
    /// The CONTAINER control <paramref name="control"/> is a child of, or null when it sits on the form itself (or is a
    /// tray component, or is not in this document).
    /// </summary>
    public FormControl? ParentOf(FormControl control) =>
        AllControls().FirstOrDefault(candidate => candidate.Children.Any(c => ReferenceEquals(c, control)));

    /// <summary>
    /// The list <paramref name="control"/> lives in — this document's own, or its container's.
    ///
    /// <para>⚠ The list, not the parent control, because removing and re-adding are what callers
    /// actually need and a container exposes its children as a plain list. Returns null when the
    /// control is not in this document at all, which a caller must treat as "do nothing" rather
    /// than as "top level".</para>
    /// </summary>
    public List<FormControl>? ListContaining(FormControl control)
    {
        if (Controls.Contains(control))
        {
            return Controls;
        }

        foreach (var candidate in AllControls())
        {
            if (candidate.Children.Contains(control))
            {
                return candidate.Children;
            }
        }

        // The tray: Delete and Cut remove a component through this, like anything else.
        if (Components.Contains(control))
        {
            return Components;
        }

        return null;
    }

    /// <summary>
    /// Removes <paramref name="control"/> (a control, a container with its subtree, or a tray component) from the list it
    /// lives in — and, as VS does, every Form-level reference to it or to anything inside it goes WITH it
    /// (<see cref="FormReferences.ForgetRemoved"/>: an <c>AcceptButton</c> naming a deleted Button is removed, never left
    /// dangling for BL8034 to find at build).
    ///
    /// <para>⛔ The ONE model path for taking a control out of the document: the designer's Delete (canvas and tray) and
    /// Cut both call it, so no route can remove the Button and keep the reference. Returns false — and changes nothing —
    /// when the control is not in this document.</para>
    /// </summary>
    public bool RemoveControl(FormControl control)
    {
        var siblings = ListContaining(control);
        if (siblings == null || !siblings.Remove(control))
        {
            return false;
        }

        FormReferences.ForgetRemoved(this, control.SelfAndDescendants().Select(c => c.Id));
        return true;
    }

    /// <summary>
    /// True when <paramref name="id"/> is a legal control identifier: a BasicLang identifier, which
    /// permits underscores. Deliberately NOT the type-name rule — see <see cref="Name"/>.
    /// </summary>
    public static bool IsLegalControlId(string? id) =>
        !string.IsNullOrEmpty(id) &&
        (char.IsLetter(id[0]) || id[0] == '_') &&
        id.All(c => char.IsLetterOrDigit(c) || c == '_');

    /// <summary>
    /// Returns <paramref name="desired"/> if free, else the first <c>desired</c> + N that is.
    /// A trailing run of digits is treated as a counter, so pasting <c>btnLogin2</c> next to an
    /// existing <c>btnLogin2</c> yields <c>btnLogin3</c> rather than <c>btnLogin21</c>.
    /// </summary>
    public static string MakeUniqueId(string desired, Func<string, bool> isTaken)
    {
        if (!isTaken(desired))
        {
            return desired;
        }

        var stem = desired.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        if (stem.Length == 0)
        {
            stem = desired;
        }

        var suffix = desired.Length > stem.Length ? desired.Substring(stem.Length) : null;
        var next = suffix != null && int.TryParse(suffix, out var parsed) ? parsed + 1 : 1;

        while (isTaken(stem + next))
        {
            next++;
        }

        return stem + next;
    }

    /// <summary>
    /// Moves <paramref name="control"/> to the front of its siblings. Returns whether it moved.
    ///
    /// <para><b>Last in the list is in FRONT</b> — the canvas draws in list order and hit-tests
    /// backwards, and the DOM paints in document order. See <c>FormZOrderTests</c> for why WinForms,
    /// which numbers z-order the other way round, is the one that has to adapt.</para>
    ///
    /// <para>⛔ Reorders the list the control actually LIVES in. Reordering <see cref="Controls"/>
    /// unconditionally would silently do nothing for anything inside a Panel — the same trap Delete
    /// had, where a command looks broken for nested controls only.</para>
    /// </summary>
    public bool BringToFront(FormControl control) => MoveWithin(control, toFront: true);

    /// <summary>Moves <paramref name="control"/> behind its siblings. Returns whether it moved.</summary>
    public bool SendToBack(FormControl control) => MoveWithin(control, toFront: false);

    private bool MoveWithin(FormControl control, bool toFront)
    {
        ArgumentNullException.ThrowIfNull(control);

        var siblings = ListContaining(control);
        if (siblings == null)
        {
            return false;
        }

        var at = siblings.IndexOf(control);
        var target = toFront ? siblings.Count - 1 : 0;
        if (at < 0 || at == target)
        {
            return false;
        }

        siblings.RemoveAt(at);
        siblings.Insert(target, control);
        return true;
    }

    /// <summary>
    /// Assigns <see cref="FormControl.TabIndex"/> in document order, starting at 0.
    ///
    /// <para>⛔ Only POSITIONED controls are numbered (Task 24). Strips and their items live inside
    /// <see cref="AllControls"/> — they are in the visual tree, unlike the tray, which is excluded a
    /// list at a time — so the exclusion here is per ROW. Numbering them would both give a MenuStrip
    /// a tab order it cannot have and push the one real control in the document from index 0 to
    /// index 7.</para>
    ///
    /// <para>⛔ <c>is null or FormPlace.Positioned</c>, never <c>== FormPlace.Positioned</c>. A
    /// control whose kind is not in the catalog has a null <c>Definition</c> and IS positioned;
    /// the equality answers false for it and would silently drop it out of the tab order.</para>
    /// </summary>
    public void RenumberTabIndexes()
    {
        var index = 0;
        foreach (var control in AllControls().Where(c => c.Definition?.Place is null or FormPlace.Positioned))
        {
            control.TabIndex = index++;
        }
    }
}

/// <summary>
/// What a paste produced: the controls to add (already renamed), or why nothing was pasted.
/// </summary>
/// <param name="Refusal">
/// A reason a user can act on, or null. ⚠ Null with no controls means a fragment that is not ours or is
/// malformed — nothing to explain; a refusal is a fragment we understood and cannot honour here.
/// </param>
public sealed record FormPasteResult(IReadOnlyList<FormControl> Controls, string? Refusal)
{
    internal static readonly FormPasteResult Nothing = new(Array.Empty<FormControl>(), null);
}

/// <summary>
/// Subtree copy/paste for the canvas.
///
/// <para>Built in Task 4 alongside the model rather than when Ctrl+C is wired, because it is nearly
/// free here and very expensive later: paste has to rename colliding ids AND retarget the handler
/// names that follow from them, and retrofitting that once a writer already exists means teaching
/// the writer about a second identity-assignment path.</para>
/// </summary>
public static class FormClipboard
{
    private const string ClipboardRoot = "FormSubtree";

    /// <summary>
    /// Serializes controls (with their descendants) to a self-describing XML fragment. The target — and,
    /// for a web form, its LAYOUT (spec 2026-09-27 §2.1) — is recorded so a paste into a document of another
    /// vocabulary can be refused rather than producing a WinForms control on a page, or a cell on a pixel page.
    /// </summary>
    /// <param name="layout">The source web document's layout; null means Grid. Ignored for WinForms.</param>
    public static string SerializeSubtree(
        FormTarget target, IEnumerable<FormControl> controls, FormLayoutKind? layout = null)
    {
        var root = new XElement(ClipboardRoot,
            new XAttribute("Target", target.ToString()),
            new XAttribute("Version", 1));

        if (target == FormTarget.Web)
        {
            root.SetAttributeValue("Layout", (layout ?? FormLayoutKind.Grid).ToString());
        }

        foreach (var control in controls)
        {
            root.Add(ToElement(control));
        }

        return root.ToString();
    }

    /// <summary>
    /// The pre-layout entry point. A web destination is taken to be Grid, i.e. a CELL page — which is how this
    /// signature always read a web paste (Grid and Flow alike), before Canvas pages had pixel controls. ⚠ Its
    /// remaining callers are TESTS; no shipping code calls it (the Paste command calls <see cref="Paste"/>).
    /// New code calls <see cref="Paste"/>.
    /// </summary>
    public static IReadOnlyList<FormControl> DeserializeSubtree(
        string xml, FormTarget target, Func<string, bool> isTaken) =>
        Paste(xml, target, target == FormTarget.Web ? FormLayoutKind.Grid : null, isTaken).Controls;

    /// <summary>
    /// Reads a fragment produced by <see cref="SerializeSubtree"/> into a document of (<paramref name="target"/>,
    /// <paramref name="layout"/>), renaming every control whose id is already taken and retargeting the binds
    /// that named it — or REFUSES, with a reason, a fragment from another target or another VOCABULARY.
    ///
    /// <para>⛔ A paste between pixels (Canvas) and cells (Grid/Flow) is refused, never converted (spec
    /// 2026-09-27 §2.1, "Canvas ↔ Grid"): a grid cell has no pixel position and a pixel position has no cell,
    /// so every pasted control would land somewhere nobody designed. Converting is piece 4's job.</para>
    ///
    /// <para>⚠ By VOCABULARY — <see cref="FormVocabulary.IsPixel(FormTarget, FormLayoutKind?)"/> on each side
    /// — never by layout NAME. Grid and Flow both read Col/Row into a <see cref="GridGeometry"/>, so a paste
    /// between them is lossless and was always accepted; refusing it by name would be a regression with a
    /// false reason (Task 3 review).</para>
    /// </summary>
    /// <param name="layout">The destination web document's layout; null means Grid. Ignored for WinForms.</param>
    /// <param name="isTaken">
    /// Whether an id is in use. The caller passes the destination document's lookup; ids minted
    /// during this paste are added to it internally, so two pasted siblings cannot collide.
    /// </param>
    public static FormPasteResult Paste(
        string xml, FormTarget target, FormLayoutKind? layout, Func<string, bool> isTaken)
    {
        XElement root;
        try
        {
            root = XElement.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            return FormPasteResult.Nothing;
        }

        if (root.Name.LocalName != ClipboardRoot ||
            !Enum.TryParse<FormTarget>((string?)root.Attribute("Target"), out var sourceTarget))
        {
            return FormPasteResult.Nothing;
        }

        // A web fragment that names no layout is a CELL fragment: before layouts were recorded, every web paste
        // was read as cells (Grid and Flow alike), so Grid stands for both. ⚠ Defensive, not a migration path:
        // the designer's clipboard is process-static, so no fragment written before this change can reach a
        // process running it — only hand-built or test text omits the attribute.
        FormLayoutKind? sourceLayout = null;
        if (sourceTarget == FormTarget.Web)
        {
            var recorded = (string?)root.Attribute("Layout");
            if (recorded == null)
            {
                sourceLayout = FormLayoutKind.Grid;
            }
            else if (TryLayoutName(recorded, out var kind))
            {
                sourceLayout = kind;
            }
            else
            {
                return FormPasteResult.Nothing;
            }
        }

        FormLayoutKind? destination = target == FormTarget.Web ? layout ?? FormLayoutKind.Grid : null;

        if (sourceTarget != target ||
            FormVocabulary.IsPixel(sourceTarget, sourceLayout) != FormVocabulary.IsPixel(target, destination))
        {
            return new FormPasteResult(Array.Empty<FormControl>(),
                $"These controls were copied from a {Describe(sourceTarget, sourceLayout)} and this is a " +
                $"{Describe(target, destination)}, so nothing was pasted. " +
                (sourceTarget != target
                    ? "The two targets have different control catalogs."
                    : "A grid cell has no pixel position and a pixel position has no cell — every pasted " +
                      "control would have landed somewhere it was not designed."));
        }

        var minted = new HashSet<string>(StringComparer.Ordinal);
        bool Taken(string id) => minted.Contains(id) || isTaken(id);

        var result = new List<FormControl>();
        foreach (var element in root.Elements())
        {
            var control = FromElement(element, target, destination);
            if (control != null)
            {
                Rename(control, Taken, minted);
                result.Add(control);
            }
        }

        return new FormPasteResult(result, null);
    }

    /// <summary>
    /// ⛔ By NAME only. <c>Enum.TryParse</c> accepts a numeric string ("2" is Canvas), and a fragment is
    /// unvetted text — a number is not a layout name.
    ///
    /// <para>⚠ Deliberately STRICTER than the reader's <c>Enum.TryParse(ignoreCase: true)</c> for
    /// <c>&lt;Layout Kind=&gt;</c>: a document is hand-edited text, but a fragment's <c>Layout=</c> is written
    /// only by <see cref="SerializeSubtree"/>, which always writes the exact <c>ToString()</c> — so anything
    /// else is not one of ours.</para>
    /// </summary>
    private static bool TryLayoutName(string text, out FormLayoutKind kind)
    {
        foreach (var candidate in Enum.GetValues<FormLayoutKind>())
        {
            if (string.Equals(candidate.ToString(), text, StringComparison.Ordinal))
            {
                kind = candidate;
                return true;
            }
        }

        kind = default;
        return false;
    }

    private static string Describe(FormTarget target, FormLayoutKind? layout) => target == FormTarget.WinForms
        ? "WinForms form"
        : layout switch
        {
            FormLayoutKind.Canvas => "pixel-layout (Canvas) web form",
            FormLayoutKind.Flow => "Flow-layout web form",
            _ => "Grid-layout web form"
        };

    /// <summary>
    /// Gives every control in the subtree a free id, and rewrites any bind handler that was named
    /// after the old id so the convention survives the paste — a handler on <c>btnLogin</c> called
    /// <c>btnLogin_Click</c> becomes <c>btnLogin1_Click</c> on the copy. A handler that does NOT
    /// follow the convention is left alone: it names a Sub the user wrote deliberately, and both
    /// copies calling it is the behaviour a copy should have.
    /// </summary>
    private static void Rename(FormControl control, Func<string, bool> isTaken, HashSet<string> minted)
    {
        foreach (var node in control.SelfAndDescendants())
        {
            var oldId = node.Id;
            var newId = FormDocument.MakeUniqueId(oldId, isTaken);
            minted.Add(newId);

            if (string.Equals(oldId, newId, StringComparison.Ordinal))
            {
                continue;
            }

            node.Id = newId;

            var conventionPrefix = oldId + "_";
            foreach (var bind in node.Binds)
            {
                if (bind.Handler.StartsWith(conventionPrefix, StringComparison.Ordinal))
                {
                    bind.Handler = newId + "_" + bind.Handler.Substring(conventionPrefix.Length);
                }
            }
        }
    }

    private static XElement ToElement(FormControl control)
    {
        var element = new XElement(control.Kind, new XAttribute("Id", control.Id));

        // ⛔ The row's SHAPE (Task 24), mirroring FormDocumentWriter.ControlElement — never the old
        // IsComponent bool, which is TRAY-ONLY. Under that bool a Docked strip took the positioned
        // path on BOTH sides of the clipboard: the fragment carried whatever stale TabIndex the model
        // held, and the paste came back with a PixelGeometry built out of its own Dock (one of
        // ReadGeometry's six trigger attributes) while Dock itself — structural, so skipped by the
        // property loop — never reached Properties at all.
        var place = control.Definition?.Place ?? FormPlace.Positioned;

        if (place == FormPlace.Positioned)
        {
            element.SetAttributeValue("TabIndex", control.TabIndex);

            switch (control.Geometry)
            {
                case PixelGeometry pixel:
                    element.SetAttributeValue("X", pixel.X);
                    element.SetAttributeValue("Y", pixel.Y);
                    element.SetAttributeValue("Width", pixel.Width);
                    element.SetAttributeValue("Height", pixel.Height);
                    element.SetAttributeValue("Anchor", pixel.Anchor);
                    element.SetAttributeValue("Dock", pixel.Dock);
                    break;

                case GridGeometry grid:
                    element.SetAttributeValue("Col", grid.Col);
                    element.SetAttributeValue("Row", grid.Row);
                    if (grid.ColSpan != 1) element.SetAttributeValue("ColSpan", grid.ColSpan);
                    if (grid.RowSpan != 1) element.SetAttributeValue("RowSpan", grid.RowSpan);
                    break;
            }
        }

        var itemsRow = control.Definition?.Properties.FirstOrDefault(FormItems.IsCollection);
        foreach (var (name, value) in control.Properties)
        {
            // ⛔ ADR 0020 (Part E): an item list travels as <Item> children, exactly as the document stores it — never as an
            // attribute, where a one-item list containing a comma is indistinguishable from a legacy two-item one.
            if (itemsRow != null && string.Equals(name, itemsRow.Name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            element.SetAttributeValue(name, value);
        }

        foreach (var (name, value) in control.UnknownAttributes)
        {
            element.SetAttributeValue(name, value);
        }

        if (itemsRow != null && control.Properties.TryGetValue(itemsRow.Name, out var items))
        {
            element.Add(FormItems.Elements(items));
        }

        foreach (var bind in control.Binds)
        {
            var bindElement = new XElement("Bind",
                new XAttribute("Event", bind.Event),
                new XAttribute("Handler", bind.Handler));
            bindElement.SetAttributeValue("Property", bind.Property);
            bindElement.SetAttributeValue("Source", bind.Source);
            bindElement.SetAttributeValue("Path", bind.Path);
            element.Add(bindElement);
        }

        foreach (var unknown in control.UnknownChildren)
        {
            element.Add(new XElement(unknown));
        }

        // A tray component cannot nest; a strip and an item both do — their children are items.
        if (place != FormPlace.Tray)
        {
            foreach (var child in control.Children)
            {
                element.Add(ToElement(child));
            }
        }

        return element;
    }

    /// <param name="target">
    /// ⛔ The pasted subtree's (target, layout) — the target half here, the layout half below. Without
    /// the target this used the target-AGNOSTIC
    /// <c>IsStructural</c>, which treats both vocabularies as structural — so copying a web
    /// control silently dropped its <c>Width</c>, and copying a WinForms control dropped its
    /// <c>Col</c>, because each is structural in the OTHER format and so was skipped without ever
    /// reaching <c>Properties</c> or <c>UnknownAttributes</c>. That is precisely the trap the
    /// target-aware overload was added to document, reached through the one call site that still
    /// used the old one.
    /// </param>
    /// <param name="layout">
    /// The pasted subtree's layout (it equals the destination's — <see cref="Paste"/> refuses otherwise);
    /// with the target, it picks the vocabulary (spec 2026-09-27 §2.1).
    /// </param>
    private static FormControl? FromElement(XElement element, FormTarget target, FormLayoutKind? layout)
    {
        var definition = FormControlCatalog.Find(element.Name.LocalName);
        if (definition == null)
        {
            return null;
        }

        // ⛔ The row's SHAPE (Task 24), the exact branch FormDocumentReader.ReadControl takes — only a
        // POSITIONED control takes geometry and a tab index from a fragment, and only a Positioned
        // control has a structural vocabulary to skip. A paste must not be the one path that
        // positions a Timer, nor the one that turns a MenuStrip's Dock into pixels.
        var place = definition.Place;

        var control = new FormControl
        {
            Kind = definition.Kind,
            Id = (string?)element.Attribute("Id") ?? "",
            TabIndex = place == FormPlace.Positioned ? IntAttribute(element, "TabIndex") ?? 0 : 0
        };

        control.Geometry = place == FormPlace.Positioned ? ReadGeometry(element, target, layout) : null;

        var itemsRow = definition.Properties.FirstOrDefault(FormItems.IsCollection);
        XAttribute? legacyItems = null;
        var itemElements = new List<XElement>();

        foreach (var attribute in element.Attributes())
        {
            var name = attribute.Name.LocalName;

            // ⛔ Everything but a Positioned control skips Id ALONE. Dock is in StructuralAttributes,
            // so while a strip took the structural branch the catalog's own Dock property was skipped
            // out of this loop and the pasted strip arrived with no Dock at all.
            if (place == FormPlace.Positioned
                    ? FormControlCatalog.IsStructural(name, target, layout)
                    : string.Equals(name, "Id", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var property = definition.Property(name);

            // ADR 0020: an items attribute is a LEGACY payload, decided with the <Item> children below by the reader's own
            // rule (FormItems.Read) — comma split, or Degraded beside children.
            if (FormItems.IsCollection(property))
            {
                legacyItems = attribute;
                continue;
            }

            if (property != null)
            {
                control.Properties[name] = attribute.Value;
            }
            else
            {
                control.UnknownAttributes[name] = attribute.Value;
            }
        }

        foreach (var child in element.Elements())
        {
            if (child.Name.LocalName == "Bind")
            {
                control.Binds.Add(new FormBind
                {
                    Event = (string?)child.Attribute("Event") ?? "",
                    Handler = (string?)child.Attribute("Handler") ?? "",
                    Property = (string?)child.Attribute("Property"),
                    Source = (string?)child.Attribute("Source"),
                    Path = (string?)child.Attribute("Path")
                });
            }
            else if (itemsRow != null && child.Name.LocalName == FormItems.ElementName)
            {
                itemElements.Add(child);
            }
            else if (place != FormPlace.Tray && FormControlCatalog.Find(child.Name.LocalName) != null)
            {
                var nested = FromElement(child, target, layout);
                if (nested != null)
                {
                    control.Children.Add(nested);
                }
            }
            else
            {
                control.UnknownChildren.Add(new XElement(child));
            }
        }

        if (itemsRow != null)
        {
            _ = FormItems.Read(control, itemsRow, legacyItems, itemElements); // the reader's own rule; a paste has no Degraded list
        }

        return control;
    }

    /// <summary>
    /// The pasted control's geometry, in the vocabulary of the document being pasted into.
    ///
    /// <para>⛔⛔ Selected by (target, layout) — the same <see cref="FormVocabulary.IsPixel(FormTarget, FormLayoutKind?)"/>
    /// <c>FormDocumentReader.ReadGeometry</c> asks — never by sniffing which attributes are present, and
    /// for the same reason. Sniffing read
    /// <c>&lt;Button X="10" Col="2" Row="1"/&gt;</c> as PIXEL geometry on a web form: the paste
    /// landed with Col and Row silently dropped, every pasted control stacked at grid cell 0,0, and
    /// the document then wrote back X/Y a web form has no meaning for. A stray attribute from the
    /// other format is exactly what a clipboard carries, since copying from a .blform and pasting
    /// into a .blwebform is a thing a user can do in two keystrokes.</para>
    ///
    /// <para>⛔ int.TryParse, never a (int?) cast. The XLinq cast THROWS FormatException on a value
    /// it cannot parse, and the clipboard is exactly where unvetted text arrives — a paste of a
    /// fragment carrying X="20px" would take the IDE down rather than declining the paste.</para>
    /// </summary>
    private static FormGeometry? ReadGeometry(XElement element, FormTarget target, FormLayoutKind? layout)
    {
        // ⚠ (target, layout) picks the VOCABULARY; absence still means null WITHIN that vocabulary,
        // exactly as FormDocumentReader.ReadGeometry decides it. Always returning a zeroed geometry
        // instead would give a pasted control a position it never had, and the writer would then
        // persist X="0" Y="0" into a document that carried neither — the same byte-identity wound
        // as the no-op-save defects, arriving through the clipboard.
        if (FormVocabulary.IsPixel(target, layout))
        {
            if (element.Attribute("X") == null && element.Attribute("Y") == null &&
                element.Attribute("Width") == null && element.Attribute("Height") == null &&
                element.Attribute("Anchor") == null && element.Attribute("Dock") == null)
            {
                return null;
            }

            return new PixelGeometry
            {
                X = IntAttribute(element, "X") ?? 0,
                Y = IntAttribute(element, "Y") ?? 0,
                Width = IntAttribute(element, "Width") ?? 0,
                Height = IntAttribute(element, "Height") ?? 0,
                Anchor = (string?)element.Attribute("Anchor"),
                Dock = (string?)element.Attribute("Dock")
            };
        }

        if (element.Attribute("Col") == null && element.Attribute("Row") == null)
        {
            return null;
        }

        return new GridGeometry
        {
            Col = IntAttribute(element, "Col") ?? 0,
            Row = IntAttribute(element, "Row") ?? 0,
            ColSpan = IntAttribute(element, "ColSpan") ?? 1,
            RowSpan = IntAttribute(element, "RowSpan") ?? 1
        };
    }

    /// <summary>
    /// An integer attribute, or null when absent OR unparseable. Never throws. ⛔ Culture-free, exactly
    /// as <c>FormDocumentReader</c> reads one — a paste means the same position on every machine.
    /// </summary>
    private static int? IntAttribute(XElement element, string name) =>
        (string?)element.Attribute(name) is { } text && FormPropertyDef.TryParseInt(text, out var value) ? value : null;
}
