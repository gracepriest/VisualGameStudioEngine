using System.Globalization;
using System.Xml.Linq;

namespace BasicLang.Forms.Serialization;

/// <summary>
/// Writes a form document — <c>.blform</c> or <c>.blwebform</c> — back out, touching only what the
/// model actually changed.
///
/// <para><b>The algebra this must satisfy</b>, because D1 puts designer-written regions inside files
/// the user owns and D9 is what makes that safe:</para>
/// <list type="bullet">
///   <item>a no-op patch writes <b>nothing</b>;</item>
///   <item>a round trip is <b>byte-identical</b>;</item>
///   <item><c>Read∘Apply == Apply∘Read</c> — editing the model then writing gives the same document
///         as writing then re-reading and editing.</item>
/// </list>
///
/// <para>Achieved the same way <c>ProjectSerializer</c> achieves it: edit the ORIGINAL parsed
/// document in place rather than rebuilding from the model, so anything the reader did not model —
/// comments, unknown elements, attribute spelling, indentation — is never touched because it is
/// never visited.</para>
/// </summary>
public static class FormDocumentWriter
{
    /// <summary>
    /// The document text for the current model state.
    ///
    /// <para>⛔ When the model changed nothing, the ORIGINAL TEXT is returned verbatim rather than a
    /// re-serialization of an untouched tree. That is not an optimisation — it is what makes
    /// byte-identity unconditional. <c>XmlWriter</c> normalises on the way out whatever the tree
    /// held: an empty element written <c>&lt;Layout/&gt;</c> by hand comes back <c>&lt;Layout /&gt;</c>,
    /// and quote style and entity spelling are normalised too. So a re-serialized no-op would differ
    /// from the user's file in ways nobody asked for, and "a round trip is byte-identical" would be
    /// true only for documents this writer had already written.</para>
    ///
    /// <para>⚠ The corollary, stated rather than hidden: the FIRST real edit to a hand-written
    /// document does normalise it in those ways. That is unavoidable without a bespoke serializer,
    /// and it is a one-time change to a file the designer now co-owns — not a per-save churn.
    /// <c>Write_NormalisesAHandWrittenDocument_OnlyOnTheFirstRealEdit</c> pins the boundary.</para>
    /// </summary>
    public static string Write(FormFile form)
    {
        // ⛔ A REFUSED document is never written. The refusal paths in the reader return the full
        // parsed tree but an EMPTY model — a newer-version document, or one with an unexpected root,
        // stops being read before Name, Version, controls, layout and literal are populated. Writing
        // from that model would delete every control, downgrade the version to 1, stamp the filename
        // over the document's name and drop the <Literal>. Refusing a document specifically to avoid
        // a lossy save, and then performing exactly that save, is the worst outcome available.
        if (form.IsRefused)
        {
            return form.CurrentText;
        }

        // Serialize before and after applying: identical output means the model asked for nothing.
        // The comparison is against the tree's CURRENT serialization, not the original text, because
        // the tree is mutated in place and may already carry an earlier edit.
        var before = XmlTextIO.Serialize(form.Xml, form.OriginalText);
        ApplyToDocument(form.Xml, form.Model, Path.GetFileNameWithoutExtension(form.FilePath));
        var after = XmlTextIO.Serialize(form.Xml, form.OriginalText);

        if (string.Equals(before, after, StringComparison.Ordinal))
        {
            return form.CurrentText;
        }

        form.CurrentText = after;
        return after;
    }

    /// <summary>Writes to disk only when the bytes would differ. Returns true when it wrote.</summary>
    public static bool Save(FormFile form) =>
        !form.IsRefused && XmlTextIO.SaveIfChanged(form.FilePath, Write(form));

    /// <summary>
    /// Builds a document from scratch, for a form the designer is creating. Separate from
    /// <see cref="Write"/> because there is nothing to preserve yet — and because keeping the two
    /// apart is what stops the preserving path from quietly becoming a rebuild.
    /// </summary>
    public static string Create(FormDocument model)
    {
        // The element name IS the format (D2): <Form> or <WebForm>. Taken from the model rather
        // than from a parameter so a document cannot be created with a root that disagrees with the
        // geometry its controls carry.
        var root = new XElement(model.RootElementName,
            new XAttribute("Name", model.Name),
            new XAttribute("Version", model.Version));

        // Text is ONE vocabulary on both targets (D2). Null writes nothing.
        root.SetAttributeValue("Text", model.Text);

        if (model.Target == FormTarget.WinForms)
        {
            // The window's client size: D3's divergence, at the root.
            root.SetAttributeValue("Width", model.Width);
            root.SetAttributeValue("Height", model.Height);
        }
        else if (model.Layout != null)
        {
            root.Add(LayoutElement(model.Layout));
        }

        foreach (var (name, value) in model.UnknownAttributes)
        {
            root.SetAttributeValue(name, value);
        }

        // The form's own event wiring, before <Controls> — the same place Apply inserts it.
        foreach (var bind in model.Binds)
        {
            root.Add(BindElement(bind));
        }

        var controls = new XElement("Controls");
        foreach (var control in model.Controls)
        {
            controls.Add(ControlElement(control));
        }

        root.Add(controls);

        // Web only. A .blform has no markup to pass through, and a <Literal> in one would be an
        // element the WinForms emitter has nowhere to put.
        if (model.Target == FormTarget.Web && model.Literal != null)
        {
            root.Add(new XElement("Literal", new XCData(model.Literal)));
        }

        foreach (var unknown in model.UnknownChildren)
        {
            root.Add(new XElement(unknown));
        }

        // The tray (Task 25). Written even when empty, so the shape of a designer-created document
        // is unchanged from before the tray existed.
        var components = new XElement("Components");
        foreach (var component in model.Components)
        {
            components.Add(ControlElement(component, isComponent: true));
        }

        root.Add(components);

        // Reserved and empty in v1, but written so the shape of a designer-created document matches
        // the shape of one a later version will produce.
        root.Add(new XElement("Resources"));

        var doc = new XDocument(root);
        return doc.ToString() + Environment.NewLine;
    }

    // ==================================================================
    // Applying the model to an existing document
    // ==================================================================

    /// <param name="nameWhenAbsent">
    /// What the READER would have used for <c>Name</c> had the document omitted it — the file's own
    /// base name. Passed in because the writer cannot otherwise tell "the document said this" from
    /// "the reader filled this in".
    /// </param>
    private static void ApplyToDocument(XDocument xml, FormDocument model, string nameWhenAbsent)
    {
        var root = xml.Root;
        if (root == null)
        {
            return;
        }

        // ⚠ "Meaningful", not "Changed", for anything with a default. The reader fills Name from the
        // FILENAME and Version from SupportedVersion when the document omits them — so writing them
        // unconditionally would add `Name="…" Version="1"` to a hand-authored document that
        // deliberately left them out, and "a no-op patch writes nothing" would be true only for
        // documents this writer had already produced. Same trap as materialising a configuration
        // default in ProjectSerializer.
        SetAttributeIfMeaningful(root, "Name", model.Name, nameWhenAbsent);
        SetIntAttributeIfChanged(root, "Version", model.Version, FormDocumentReader.SupportedVersion);

        // ⛔ Text is written on BOTH targets (D2). Before this, a web form's caption edit reached the
        // model and never the file — the grid showed it, the next open lost it. Null removes it: a
        // string cannot be "present but unparseable", so there is nothing to protect by keeping it.
        SetAttributeIfChanged(root, "Text", model.Text);

        // D3, at the root: a window has a size, a page has a layout and may carry literal markup. Each
        // side writes only its own vocabulary — writing both would put a <Layout> into a .blform on the
        // first save, and the file would then be refused by its own reader on the next open.
        if (model.Target == FormTarget.WinForms)
        {
            ApplyFormAttributes(root, model);
        }
        else
        {
            ApplyLayout(root, model);
        }

        ApplyBindList(root, model.Binds, insertBefore: root.Element("Controls") ?? root.Element("Components"));

        ApplyControls(root, model);
        ApplyComponents(root, model);

        if (model.Target == FormTarget.Web)
        {
            ApplyLiteral(root, model);
        }
    }

    /// <summary>
    /// The tray (Task 25): <c>&lt;Components&gt;</c> is patched in place exactly as
    /// <c>&lt;Controls&gt;</c> is — find by Id, set-if-changed, remove what the model dropped,
    /// reorder — so the D9 algebra holds for a document that has components.
    ///
    /// <para>⛔⛔ Before this existed the slot was WRITE-NEVER: <c>Apply</c> skipped the element and
    /// <c>Create</c> wrote an empty one. A component added on the model would have reached the
    /// canvas and never the file, and undo — which rewinds TEXT — could not have seen it.</para>
    ///
    /// <para>⚠ A document that never had the element keeps not having it while the model has no
    /// components: "a no-op patch writes nothing". The element is created, before
    /// <c>&lt;Resources&gt;</c>, only when there is something to put in it.</para>
    /// </summary>
    private static void ApplyComponents(XElement root, FormDocument model)
    {
        var container = root.Element("Components");
        if (container == null)
        {
            if (model.Components.Count == 0)
            {
                return;
            }

            container = new XElement("Components");
            InsertPreservingIndent(root, container, before: root.Element("Resources"));
        }

        ApplyControlList(container, model.Components, isComponent: true);
    }

    /// <summary>
    /// The <c>.blform</c> root's <c>Width</c>/<c>Height</c>.
    ///
    /// <para>⛔ A null model value means "the document did not say" — either the attribute was
    /// absent, or it was present and unparseable, in which case the reader left it unmodelled and
    /// the unknown-attribute round trip is the only thing preserving it (its ClientSize row is
    /// Degraded). Either way, writing null here as a removal would delete an attribute the user wrote
    /// and the designer never understood.</para>
    /// </summary>
    private static void ApplyFormAttributes(XElement root, FormDocument model)
    {
        // ⛔ Invariant: a Degraded non-positive size is kept modelled, and under sv-SE `-5` formats with a
        // U+2212 minus — a no-op save would rewrite the user's text.
        if (model.Width != null)
        {
            SetAttributeIfChanged(root, "Width", model.Width.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (model.Height != null)
        {
            SetAttributeIfChanged(root, "Height", model.Height.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    private static void ApplyLayout(XElement root, FormDocument model)
    {
        var element = root.Element("Layout");

        if (model.Layout == null)
        {
            if (element != null)
            {
                RemoveWithLeadingWhitespace(element);
            }

            return;
        }

        if (element == null)
        {
            InsertPreservingIndent(root, LayoutElement(model.Layout), before: root.Element("Controls"));
            return;
        }

        SetAttributeIfMeaningful(element, "Kind", model.Layout.Kind.ToString(), FormLayoutKind.Grid.ToString());
        SetAttributeIfChanged(element, "Cols", model.Layout.Cols);
        SetAttributeIfChanged(element, "Rows", model.Layout.Rows);
        SetAttributeIfChanged(element, "Gap", model.Layout.Gap);
        SetAttributeIfChanged(element, "Dir", model.Layout.Dir);
    }

    private static void ApplyControls(XElement root, FormDocument model)
    {
        var container = root.Element("Controls");
        if (container == null)
        {
            container = new XElement("Controls");
            // <Literal> is web-only and <Components> is in both, so this anchors correctly for
            // either format and falls through to "append" when neither is present.
            InsertPreservingIndent(root, container,
                before: root.Element("Literal") ?? root.Element("Components"));
        }

        ApplyControlList(container, model.Controls);
    }

    /// <param name="isComponent">
    /// The list is the tray's. Forwarded rather than acted on here — every place decision is made
    /// per CONTROL, from its catalog row, by <see cref="PlaceOf"/>.
    /// </param>
    private static void ApplyControlList(XElement container, List<FormControl> controls, bool isComponent = false)
    {
        var wanted = controls.Select(c => c.Id).ToList();

        // Remove elements for controls the model no longer has. Only elements the catalog knows are
        // candidates — an unknown element inside <Controls> belongs to a newer designer and is not
        // ours to delete.
        foreach (var element in container.Elements().ToList())
        {
            if (FormControlCatalog.Find(element.Name.LocalName) == null)
            {
                continue;
            }

            var id = (string?)element.Attribute("Id") ?? "";
            if (!wanted.Contains(id, StringComparer.Ordinal))
            {
                RemoveWithLeadingWhitespace(element);
            }
        }

        foreach (var control in controls)
        {
            // ⚠ The catalog guard mirrors the removal loop above. Without it, a future element that
            // happens to carry a matching Id would have TabIndex/Col/Row and catalog properties
            // stamped onto it, and its catalog-named attributes stripped — the writer would edit an
            // element it does not understand.
            var element = container.Elements()
                .FirstOrDefault(e => FormControlCatalog.Find(e.Name.LocalName) != null &&
                                     string.Equals((string?)e.Attribute("Id"), control.Id, StringComparison.Ordinal));

            if (element == null)
            {
                InsertPreservingIndent(container, ControlElement(control, isComponent), before: null);
                continue;
            }

            ApplyControl(element, control, isComponent);
        }

        ReorderToMatchModel(container, controls);
    }

    /// <summary>
    /// Puts the known control elements back in the model's order.
    ///
    /// <para>⛔⛔ <b>Without this, z-order is a lie.</b> The loop above finds each element BY ID and
    /// edits it in place, which is what makes the writer structure-preserving — and means element
    /// order never changes. So "Bring to Front" reordered the model, the canvas redrew correctly, the
    /// document on disk kept the old order, and the next open — and every build — used it. The
    /// command would appear to work until you reloaded the file.</para>
    ///
    /// <para>⚠ Only when the order actually differs. Detaching and re-inserting every element
    /// rewrites their whitespace, so doing it unconditionally would produce a whole-file diff on
    /// every save of a document nobody had reordered.</para>
    ///
    /// <para>⚠ Elements the catalog does not know are left where they are (D9). They belong to a
    /// newer designer, their order is not ours to interpret, and the model has no opinion about
    /// where they sit.</para>
    /// </summary>
    private static void ReorderToMatchModel(XElement container, List<FormControl> controls)
    {
        var known = container.Elements()
            .Where(e => FormControlCatalog.Find(e.Name.LocalName) != null)
            .ToList();

        var current = known.Select(e => (string?)e.Attribute("Id") ?? "").ToList();
        var wanted = controls.Select(c => c.Id).ToList();

        if (current.SequenceEqual(wanted, StringComparer.Ordinal))
        {
            return;
        }

        // First-wins rather than ToDictionary: a malformed document can carry a duplicate id, and
        // throwing here would fail a SAVE of a file the designer had already agreed to open.
        var byId = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (var element in known)
        {
            byId.TryAdd((string?)element.Attribute("Id") ?? "", element);
        }

        foreach (var element in known)
        {
            RemoveWithLeadingWhitespace(element);
        }

        foreach (var id in wanted)
        {
            if (byId.TryGetValue(id, out var element))
            {
                InsertPreservingIndent(container, element, before: null);
            }
        }
    }

    /// <param name="isComponent">
    /// The element is the tray's. Forwarded from the list; the decisions below read
    /// <see cref="PlaceOf"/>.
    /// </param>
    private static void ApplyControl(XElement element, FormControl control, bool isComponent = false)
    {
        // TabIndex is written on every POSITIONED control in v1, defaulting to document order on
        // creation — explicit rather than implied, so reordering the XML cannot silently reorder tab
        // focus. Nothing else has a tab order to reorder.
        SetAttributeIfChanged(element, "Id", control.Id);

        // ⛔ Anything that is not POSITIONED has no tab order and no geometry — a tray component, a
        // Docked strip, an item — and the reader put any such attribute it carried into
        // UnknownAttributes. Running the TabIndex write here would rewrite a strip's TabIndex="5" to
        // "0" on the first unrelated edit: the model holds 0, the text parses to 5, and
        // SetIntAttributeIfChanged would "correct" it.
        //
        // ⚠ Passing isComponent: false for a strip was harmless only by accident until now — that
        // helper writes nothing when the attribute is absent AND the value equals the default, which
        // is every strip the reader has just read. A renumber that gave one a non-zero TabIndex made
        // it write. This guard is what makes it true by construction.
        var place = PlaceOf(control, isComponent);

        if (place == FormPlace.Positioned)
        {
            // Defaults again: the reader reads an absent TabIndex/Col/Row as 0, so writing "0" back
            // into a document that omitted them is inventing content. TabIndex IS written on every
            // control the designer creates — Create does that — but adopting a hand-written
            // document must not rewrite it wholesale on the first unrelated edit.
            SetIntAttributeIfChanged(element, "TabIndex", control.TabIndex, 0);

            // Each geometry writes only its own vocabulary. The model carries exactly one shape,
            // the reader selected it from the document's target, and nothing here converts between
            // them.
            switch (control.Geometry)
            {
                case PixelGeometry pixel:
                    SetIntAttributeIfChanged(element, "X", pixel.X, 0);
                    SetIntAttributeIfChanged(element, "Y", pixel.Y, 0);
                    SetIntAttributeIfChanged(element, "Width", pixel.Width, 0);
                    SetIntAttributeIfChanged(element, "Height", pixel.Height, 0);
                    SetAttributeIfChanged(element, "Anchor", pixel.Anchor);
                    SetAttributeIfChanged(element, "Dock", pixel.Dock);
                    break;

                case GridGeometry grid:
                    SetIntAttributeIfChanged(element, "Col", grid.Col, 0);
                    SetIntAttributeIfChanged(element, "Row", grid.Row, 0);
                    SetOptionalIntAttribute(element, "ColSpan", grid.ColSpan, 1);
                    SetOptionalIntAttribute(element, "RowSpan", grid.RowSpan, 1);
                    break;
            }
        }

        foreach (var (name, value) in control.Properties)
        {
            SetAttributeIfChanged(element, name, value);
        }

        // A catalog property the model dropped must leave the document too.
        var definition = control.Definition;
        if (definition != null)
        {
            foreach (var property in definition.Properties)
            {
                if (!control.Properties.ContainsKey(property.Name) &&
                    element.Attribute(property.Name) != null)
                {
                    element.Attribute(property.Name)!.Remove();
                }
            }
        }

        ApplyBinds(element, control);

        // A tray component cannot nest; a strip holds its items and an item holds its sub-items.
        if (place != FormPlace.Tray)
        {
            ApplyControlList(element, control.Children);
        }
    }

    private static void ApplyBinds(XElement element, FormControl control) =>
        ApplyBindList(element, control.Binds, insertBefore: null);

    /// <summary>
    /// Patches <paramref name="owner"/>'s <c>&lt;Bind&gt;</c> children in place — for a control, or for
    /// the form's root. ONE implementation, so the two cannot drift.
    /// </summary>
    /// <param name="insertBefore">Where a NEW bind goes; null appends after the last element (a control's rule).</param>
    private static void ApplyBindList(XElement owner, IReadOnlyList<FormBind> binds, XElement? insertBefore)
    {
        var existing = owner.Elements("Bind").ToList();

        for (var i = 0; i < binds.Count; i++)
        {
            var bind = binds[i];
            if (i < existing.Count)
            {
                SetAttributeIfChanged(existing[i], "Event", bind.Event);
                SetAttributeIfChanged(existing[i], "Handler", bind.Handler);
                SetAttributeIfChanged(existing[i], "Property", bind.Property);
                SetAttributeIfChanged(existing[i], "Source", bind.Source);
                SetAttributeIfChanged(existing[i], "Path", bind.Path);
            }
            else
            {
                InsertPreservingIndent(owner, BindElement(bind), before: insertBefore);
            }
        }

        for (var i = binds.Count; i < existing.Count; i++)
        {
            RemoveWithLeadingWhitespace(existing[i]);
        }
    }

    private static void ApplyLiteral(XElement root, FormDocument model)
    {
        var element = root.Element("Literal");

        if (model.Literal == null)
        {
            if (element != null)
            {
                RemoveWithLeadingWhitespace(element);
            }

            return;
        }

        if (element == null)
        {
            InsertPreservingIndent(root, new XElement("Literal", new XCData(model.Literal)), before: root.Element("Components"));
            return;
        }

        // ⛔ Only rewritten when it actually differs. <Literal> passes through to the markup
        // untouched, so re-emitting equal content as a fresh CDATA node would churn the file for
        // nothing — and the canvas shows it read-only precisely because the designer does not own it.
        var current = string.Concat(element.Nodes().Select(n =>
            n is XCData cdata ? cdata.Value : n is XText t ? t.Value : n.ToString()));

        if (!string.Equals(current, model.Literal, StringComparison.Ordinal))
        {
            element.ReplaceNodes(new XCData(model.Literal));
        }
    }

    // ==================================================================
    // Element construction (deterministic order)
    // ==================================================================

    private static XElement LayoutElement(FormLayout layout)
    {
        var element = new XElement("Layout", new XAttribute("Kind", layout.Kind.ToString()));
        element.SetAttributeValue("Cols", layout.Cols);
        element.SetAttributeValue("Rows", layout.Rows);
        element.SetAttributeValue("Gap", layout.Gap);
        element.SetAttributeValue("Dir", layout.Dir);
        return element;
    }

    /// <param name="isComponent">The element is the tray's — see <see cref="PlaceOf"/>.</param>
    private static XElement ControlElement(FormControl control, bool isComponent = false)
    {
        var element = new XElement(control.Kind);

        element.SetAttributeValue("Id", control.Id);

        // The row's SHAPE (Task 24), not a bool. Only a Positioned control has a place in pixels or
        // a cell and a place in the tab order; a Docked strip's position is its Dock PROPERTY and an
        // Item's is its order among its host's children.
        var place = PlaceOf(control, isComponent);

        if (place == FormPlace.Positioned)
        {
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

            // ⛔ UNCONDITIONAL, unlike the Apply path's SetIntAttributeIfChanged — Create stamps the
            // tab order explicitly even at 0 so reordering the XML cannot silently reorder focus.
            // Which is exactly why the guard has to be HERE as well: without it a strip and every
            // item under it came out carrying TabIndex="0", an attribute their own reader would then
            // read back as an unknown attribute.
            element.SetAttributeValue("TabIndex", control.TabIndex);
        }

        // Catalog order, not dictionary order: the property grid shows them in this order and the
        // file should read the same way.
        var definition = control.Definition;
        if (definition != null)
        {
            foreach (var property in definition.Properties)
            {
                if (control.Properties.TryGetValue(property.Name, out var value))
                {
                    element.SetAttributeValue(property.Name, value);
                }
            }
        }

        foreach (var (name, value) in control.UnknownAttributes)
        {
            element.SetAttributeValue(name, value);
        }

        foreach (var bind in control.Binds)
        {
            element.Add(BindElement(bind));
        }

        foreach (var unknown in control.UnknownChildren)
        {
            element.Add(new XElement(unknown));
        }

        // Children in z-order — document order IS z-order, so nothing sorts them. A tray component
        // cannot nest; a strip and an item both can, and their children are ITEMS rather than
        // controls, which is the one shape the old isComponent bool could not express.
        if (place != FormPlace.Tray)
        {
            foreach (var child in control.Children)
            {
                element.Add(ControlElement(child));
            }
        }

        return element;
    }

    /// <summary>
    /// The shape of the row this control came from — the one question the writer asks about where a
    /// control belongs (Task 24, spec §1).
    ///
    /// <para>⚠ <paramref name="isComponent"/> survives as the TRAY's answer rather than being
    /// dropped, for parity with <c>FormDocumentReader.ReadControl</c>: the reader gives everything it
    /// reads under <c>&lt;Components&gt;</c> the component treatment, and a writer that instead
    /// trusted the row alone would disagree with it for a control whose kind is not in the catalog —
    /// <c>Definition</c> is then null, which reads as Positioned, and the tray would acquire geometry
    /// and a tab order on the way out.</para>
    ///
    /// <para>⛔ <c>?? FormPlace.Positioned</c>, never a null-propagated comparison. A control with no
    /// catalog row IS positioned as far as every walker here is concerned, and asking
    /// <c>Definition?.Place == FormPlace.Positioned</c> answers false for it.</para>
    /// </summary>
    private static FormPlace PlaceOf(FormControl control, bool isComponent) =>
        isComponent ? FormPlace.Tray : control.Definition?.Place ?? FormPlace.Positioned;

    private static XElement BindElement(FormBind bind)
    {
        var element = new XElement("Bind",
            new XAttribute("Event", bind.Event),
            new XAttribute("Handler", bind.Handler));
        element.SetAttributeValue("Property", bind.Property);
        element.SetAttributeValue("Source", bind.Source);
        element.SetAttributeValue("Path", bind.Path);
        return element;
    }

    // ==================================================================
    // Minimal-touch primitives
    // ==================================================================

    /// <summary>
    /// Sets or removes an attribute, but ONLY when the value actually differs.
    ///
    /// <para>This is the whole byte-identity mechanism. <c>SetAttributeValue</c> with an equal value
    /// still replaces the attribute node, which is invisible in the tree but is exactly the kind of
    /// churn that makes "a no-op patch writes nothing" false.</para>
    /// </summary>
    /// <summary>
    /// Sets an attribute only when the element already carries it, or when the value differs from
    /// what an ABSENT attribute would be read as. Leaves a deliberately-omitted attribute omitted.
    /// </summary>
    private static void SetAttributeIfMeaningful(XElement element, string name, string value, string absentMeans)
    {
        if (element.Attribute(name) != null || !string.Equals(value, absentMeans, StringComparison.Ordinal))
        {
            SetAttributeIfChanged(element, name, value);
        }
    }

    /// <summary>
    /// Writes an integer attribute, <b>never overwriting text the reader could not parse</b>.
    ///
    /// <para>⛔⛔ The reader answers an unparseable <c>Version="1.0"</c> or <c>TabIndex="two"</c>
    /// with a DEFAULT it invented — 1 and 0. Comparing the model against that default the way
    /// <c>SetAttributeIfMeaningful</c> does then wrote the default straight over the user's text:
    /// a NO-OP save silently downgraded the document version and reset the tab order, with no
    /// diagnostic and no Degraded row. The file changed on a save the user never made.</para>
    ///
    /// <para>So the existing text decides what the model value MEANS:</para>
    /// <list type="bullet">
    ///   <item>absent — write only when the model differs from what absence reads as;</item>
    ///   <item>parseable — write only when the model differs from the parsed value. This also
    ///         preserves spelling: <c>"007"</c> stays <c>"007"</c> while the model says 7;</item>
    ///   <item>unparseable — the model holds the invented default, so write only when it has moved
    ///         AWAY from that default, which is the one case that is a real edit. Renumbering the
    ///         tab order still reaches the document; re-saving an untouched one does not.</item>
    /// </list>
    ///
    /// <para>⛔⛔ Culture-INVARIANT both ways, and parsed with the READER's parser
    /// (<see cref="FormPropertyDef.TryParseInt"/>). <c>value.ToString()</c> wrote X with a U+2212
    /// minus under sv-SE — a file that round-tripped on its author's machine and fell silently to 0 on
    /// en-US/CI. And a no-op comparison with a DIFFERENT parser than the reader's would call a value
    /// "unparseable" that the reader parsed (or the reverse), and a save that changed nothing would
    /// rewrite the user's text.</para>
    /// </summary>
    private static void SetIntAttributeIfChanged(XElement element, string name, int value, int absentMeans)
    {
        var existing = element.Attribute(name);

        if (existing == null)
        {
            if (value != absentMeans)
            {
                element.SetAttributeValue(name, Number(value));
            }

            return;
        }

        if (FormPropertyDef.TryParseInt(existing.Value, out var current))
        {
            if (current != value)
            {
                existing.Value = Number(value);
            }

            return;
        }

        if (value != absentMeans)
        {
            existing.Value = Number(value);
        }
    }

    /// <summary>A document integer. ⛔ Invariant — see <see cref="SetIntAttributeIfChanged"/>.</summary>
    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The same rule for an attribute whose default means ABSENT — <c>ColSpan</c>, <c>RowSpan</c>.
    ///
    /// <para>⚠ Here the damage would be a REMOVAL rather than a rewrite: an unparseable
    /// <c>ColSpan="x"</c> reads as 1, and "1 means omit it" would then delete the user's text on a
    /// no-op save.</para>
    ///
    /// <para>⚠ …and the parseable case still has to preserve SPELLING, exactly as its sibling does.
    /// Handling only the unparseable case left <c>ColSpan="02"</c> rewritten to <c>"2"</c> by a save
    /// that changed nothing — a smaller wound than resetting the tab order, and the same broken
    /// promise: D9 says a no-op patch writes nothing. An explicit <c>ColSpan="1"</c> is kept for the
    /// same reason; "the default means omit it" governs a value the DESIGNER produced, not text the
    /// user typed.</para>
    /// </summary>
    private static void SetOptionalIntAttribute(XElement element, string name, int value, int defaultValue)
    {
        var existing = element.Attribute(name);

        if (existing != null)
        {
            if (FormPropertyDef.TryParseInt(existing.Value, out var current))
            {
                if (current == value)
                {
                    return;
                }
            }
            else if (value == defaultValue)
            {
                // The model holds what absence reads as, so the value never moved.
                return;
            }
        }

        SetAttributeIfChanged(element, name, value == defaultValue ? null : Number(value));
    }

    private static void SetAttributeIfChanged(XElement element, string name, string? value)
    {
        var existing = element.Attribute(name);

        if (value == null)
        {
            existing?.Remove();
            return;
        }

        if (existing == null)
        {
            element.SetAttributeValue(name, value);
            return;
        }

        if (!string.Equals(existing.Value, value, StringComparison.Ordinal))
        {
            existing.Value = value;
        }
    }

    private static void InsertPreservingIndent(XElement parent, XElement child, XElement? before)
    {
        var anchor = before ?? parent.Elements().LastOrDefault();
        if (anchor == null)
        {
            parent.Add(child);
            return;
        }

        var indent = (anchor.PreviousNode as XText)?.Value;

        if (before != null)
        {
            anchor.AddBeforeSelf(child);
        }
        else
        {
            anchor.AddAfterSelf(child);
        }

        if (indent != null)
        {
            child.AddBeforeSelf(new XText(indent));
        }
    }

    private static void RemoveWithLeadingWhitespace(XElement element)
    {
        var leading = element.PreviousNode as XText;
        element.Remove();
        if (leading != null && string.IsNullOrWhiteSpace(leading.Value))
        {
            leading.Remove();
        }
    }
}
