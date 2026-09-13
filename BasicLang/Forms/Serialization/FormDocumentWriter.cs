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

        if (model.Target == FormTarget.WinForms)
        {
            // The window, not the page: a client size and a caption. D3's divergence, at the root.
            root.SetAttributeValue("Width", model.Width);
            root.SetAttributeValue("Height", model.Height);
            root.SetAttributeValue("Text", model.Text);
        }
        else if (model.Layout != null)
        {
            root.Add(LayoutElement(model.Layout));
        }

        foreach (var (name, value) in model.UnknownAttributes)
        {
            root.SetAttributeValue(name, value);
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

        // Reserved and empty in v1, but written so the shape of a designer-created document matches
        // the shape of one a later version will produce.
        root.Add(new XElement("Components"));
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
        SetAttributeIfMeaningful(root, "Version", model.Version.ToString(),
            FormDocumentReader.SupportedVersion.ToString());

        // D3, at the root: a window has a size and a caption, a page has a layout and may carry
        // literal markup. Each side writes only its own vocabulary — writing both would put a
        // <Layout> into a .blform on the first save, and the file would then be refused by its own
        // reader on the next open.
        if (model.Target == FormTarget.WinForms)
        {
            ApplyFormAttributes(root, model);
        }
        else
        {
            ApplyLayout(root, model);
        }

        ApplyControls(root, model);

        if (model.Target == FormTarget.Web)
        {
            ApplyLiteral(root, model);
        }
    }

    /// <summary>
    /// The <c>.blform</c> root's <c>Width</c>/<c>Height</c>/<c>Text</c>.
    ///
    /// <para>⛔ A null model value means "the document did not say" — either the attribute was
    /// absent, or it was present and unparseable, in which case the reader left it unmodelled and
    /// the unknown-attribute round trip is the only thing preserving it. Either way, writing null
    /// here as a removal would delete an attribute the user wrote and the designer never
    /// understood. Clearing the caption from the designer sets <c>Text</c> to the empty string,
    /// which IS written; it is not the same state as null.</para>
    /// </summary>
    private static void ApplyFormAttributes(XElement root, FormDocument model)
    {
        if (model.Width != null)
        {
            SetAttributeIfChanged(root, "Width", model.Width.Value.ToString());
        }

        if (model.Height != null)
        {
            SetAttributeIfChanged(root, "Height", model.Height.Value.ToString());
        }

        if (model.Text != null)
        {
            SetAttributeIfChanged(root, "Text", model.Text);
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

    private static void ApplyControlList(XElement container, List<FormControl> controls)
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
                InsertPreservingIndent(container, ControlElement(control), before: null);
                continue;
            }

            ApplyControl(element, control);
        }
    }

    private static void ApplyControl(XElement element, FormControl control)
    {
        // TabIndex is written on EVERY control in v1, defaulting to document order on creation —
        // explicit rather than implied, so reordering the XML cannot silently reorder tab focus.
        SetAttributeIfChanged(element, "Id", control.Id);

        // Defaults again: the reader reads an absent TabIndex/Col/Row as 0, so writing "0" back into
        // a document that omitted them is inventing content. TabIndex IS written on every control the
        // designer creates — Create does that — but adopting a hand-written document must not
        // rewrite it wholesale on the first unrelated edit.
        SetAttributeIfMeaningful(element, "TabIndex", control.TabIndex.ToString(), "0");

        // Each geometry writes only its own vocabulary. The model carries exactly one shape, the
        // reader selected it from the document's target, and nothing here converts between them.
        switch (control.Geometry)
        {
            case PixelGeometry pixel:
                SetAttributeIfMeaningful(element, "X", pixel.X.ToString(), "0");
                SetAttributeIfMeaningful(element, "Y", pixel.Y.ToString(), "0");
                SetAttributeIfMeaningful(element, "Width", pixel.Width.ToString(), "0");
                SetAttributeIfMeaningful(element, "Height", pixel.Height.ToString(), "0");
                SetAttributeIfChanged(element, "Anchor", pixel.Anchor);
                SetAttributeIfChanged(element, "Dock", pixel.Dock);
                break;

            case GridGeometry grid:
                SetAttributeIfMeaningful(element, "Col", grid.Col.ToString(), "0");
                SetAttributeIfMeaningful(element, "Row", grid.Row.ToString(), "0");
                SetAttributeIfChanged(element, "ColSpan", grid.ColSpan == 1 ? null : grid.ColSpan.ToString());
                SetAttributeIfChanged(element, "RowSpan", grid.RowSpan == 1 ? null : grid.RowSpan.ToString());
                break;
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
        ApplyControlList(element, control.Children);
    }

    private static void ApplyBinds(XElement element, FormControl control)
    {
        var existing = element.Elements("Bind").ToList();

        for (var i = 0; i < control.Binds.Count; i++)
        {
            var bind = control.Binds[i];
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
                InsertPreservingIndent(element, BindElement(bind), before: null);
            }
        }

        for (var i = control.Binds.Count; i < existing.Count; i++)
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

    private static XElement ControlElement(FormControl control)
    {
        var element = new XElement(control.Kind);

        element.SetAttributeValue("Id", control.Id);

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

        element.SetAttributeValue("TabIndex", control.TabIndex);

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

        // Children in z-order — document order IS z-order, so nothing sorts them.
        foreach (var child in control.Children)
        {
            element.Add(ControlElement(child));
        }

        return element;
    }

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
