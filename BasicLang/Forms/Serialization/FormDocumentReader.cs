using System.Xml;
using System.Xml.Linq;

namespace BasicLang.Forms.Serialization;

/// <summary>
/// Reads a form document — <c>.blform</c> or <c>.blwebform</c> — into a <see cref="FormFile"/>,
/// applying D9's tier rules.
///
/// <para><b>One reader for both formats, deliberately.</b> D2 makes them two formats that share the
/// element grammar, the <c>Id</c> rules, the <c>&lt;Bind Event= Handler=&gt;</c> shape, the reserved
/// sections and the unknown-content round trip, and diverge only in layout vocabulary and catalog.
/// Duplicating the reader would duplicate all of the shared part — including every refusal path and
/// every tier rule — and the two copies would drift on the first fix applied to one of them.</para>
///
/// <para>⛔ Loaded with <c>LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace</c>. SetLineInfo
/// is what lets every diagnostic carry a real <c>file(line,col)</c> instead of pointing at the top
/// of the document — a finding without a position is one nobody acts on. PreserveWhitespace is what
/// lets the writer put the file back byte-identically.</para>
/// </summary>
public static class FormDocumentReader
{
    /// <summary>The only document version v1 understands. A newer one is refused, not guessed at.</summary>
    public const int SupportedVersion = 1;

    public static FormFile Read(string filePath, string text)
    {
        var diagnostics = new List<DesignDiagnostic>();
        var degraded = new List<DegradedProperty>();
        var degradedRoot = new List<DegradedProperty>();

        // What the FILE NAME claims this is, or null when the path carries neither form extension.
        // Only a claim: the root element is the document's own self-description and wins below.
        var claimed = TargetOfExtension(filePath);

        var model = new FormDocument
        {
            Target = claimed ?? FormTarget.Web,
            Name = Path.GetFileNameWithoutExtension(filePath),
            SourcePath = filePath
        };

        var positions = new Dictionary<FormControl, XElement>();

        XDocument xml;
        try
        {
            xml = XDocument.Parse(text, LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
        }
        catch (XmlException ex)
        {
            diagnostics.Add(Error(DesignCodes.MalformedDocument,
                $"the document is not well-formed XML — {ex.Message}", filePath, ex.LineNumber, ex.LinePosition));
            return new FormFile(model, new XDocument(), text, filePath, diagnostics, degraded);
        }

        var root = xml.Root;
        var target = TargetOfRoot(root?.Name.LocalName);

        if (root == null || target == null)
        {
            diagnostics.Add(Error(DesignCodes.MalformedDocument,
                $"the root element must be <Form> (.blform) or <WebForm> (.blwebform), not " +
                $"<{root?.Name.LocalName ?? "(none)"}>.",
                filePath, Line(root), Column(root)));
            return new FormFile(model, xml, text, filePath, diagnostics, degraded);
        }

        // ⛔ A file whose name disagrees with its root is refused rather than resolved in favour of
        // either one. The two formats have DIFFERENT layout vocabularies (D3), so whichever side is
        // believed, the other reading of the same file is a lossy save: trust the root and the
        // writer emits <Form> geometry into a file the project system compiles as a web form; trust
        // the extension and every X/Y in it is read as an unknown attribute and the canvas shows an
        // empty form. Neither is recoverable by the user, and neither is visible until they save.
        if (claimed != null && claimed != target)
        {
            diagnostics.Add(Error(DesignCodes.MalformedDocument,
                $"this file is named '{Path.GetExtension(filePath)}' but its root is " +
                $"<{root.Name.LocalName}>. A {ExtensionOf(claimed.Value)} must have a " +
                $"<{RootOf(claimed.Value)}> root — the two formats use different layout vocabularies " +
                "and reading it as either one would lose the other's.",
                filePath, Line(root), Column(root)));
            return new FormFile(model, xml, text, filePath, diagnostics, degraded);
        }

        model.Target = target.Value;

        // ⛔ int.TryParse, never a (int?) cast. The XLinq cast THROWS FormatException on a value that
        // is not an integer — so `Version="1.0"` or `TabIndex="one"` would escape this method
        // entirely and surface as "design failed: The input string was not in a correct format",
        // exit 2, a tool failure. The contract here is that a bad document is a FINDING. The
        // asymmetry was the tell: a bad catalog value got a careful Degraded tier while a bad
        // structural value crashed.
        var version = IntAttribute(root, "Version") ?? SupportedVersion;
        if (version > SupportedVersion)
        {
            // Refusing a newer document is safer than reading the parts we recognise: writing it
            // back would drop whatever the newer version added, and unknown-element round-tripping
            // only covers additions we can still SEE.
            diagnostics.Add(Error(DesignCodes.MalformedDocument,
                $"this document is version {version}; this designer understands version " +
                $"{SupportedVersion}. Opening it read-only rather than risking a lossy save.",
                filePath, Line(root), Column(root)));
            return new FormFile(model, xml, text, filePath, diagnostics, degraded);
        }

        model.Name = (string?)root.Attribute("Name") ?? model.Name;
        model.Version = version;

        // Text is ONE vocabulary on both targets (D2, spec §2.3): the window caption, the page title.
        model.Text = (string?)root.Attribute("Text");

        if (target == FormTarget.WinForms)
        {
            // The form's own client size. WinForms only — D3 gives the web document a <Layout> instead.
            model.Width = IntAttribute(root, "Width");
            model.Height = IntAttribute(root, "Height");

            // ⚠ An unparseable Width/Height is left null here and still falls through to
            // UnknownAttributes below — that round trip is what preserves the text byte-for-byte. What
            // changed (spec §2.3) is the TIER: the ClientSize row is Degraded — frozen, explained — not
            // Unknown. The storage stayed where it was on purpose: the writer's Width/Height guard
            // exists to never overwrite text it could not parse.
            //
            // ⚠ A size that parses but is not POSITIVE is Degraded too: FormRootValues.Set refuses it and
            // the region writer emits nothing for it, so showing it Canon would promise a size the
            // program never gets. A parsed value stays modelled (and so round-trips through the model).
            var rawWidth = (string?)root.Attribute("Width");
            var rawHeight = (string?)root.Attribute("Height");
            var unusable =
                (rawWidth != null && model.Width is null or <= 0) ||
                (rawHeight != null && model.Height is null or <= 0);
            if (unusable)
            {
                // Name only the attributes the document CARRIES — never an invented `Height=""`.
                var present = new List<string>();
                if (rawWidth != null) present.Add($"Width=\"{rawWidth}\"");
                if (rawHeight != null) present.Add($"Height=\"{rawHeight}\"");

                degradedRoot.Add(new DegradedProperty("", "ClientSize",
                    string.Join(" ", present),
                    $"the form's client size could not be used — {string.Join(" and ", present)} " +
                    (present.Count == 1 ? "must be a positive whole number" : "must both be positive whole numbers") +
                    ". The attributes are preserved exactly as written."));
            }
        }

        foreach (var attribute in root.Attributes())
        {
            if (!IsKnownRootAttribute(attribute.Name.LocalName, target.Value, root))
            {
                model.UnknownAttributes[attribute.Name.LocalName] = attribute.Value;
            }
        }

        foreach (var element in root.Elements())
        {
            switch (element.Name.LocalName)
            {
                // Web only (D3). On a .blform it is not a layout — it is an element this designer
                // does not model, and it round-trips untouched like any other.
                case "Layout" when target == FormTarget.Web:
                    model.Layout = ReadLayout(element);
                    break;

                case "Controls":
                    foreach (var child in element.Elements())
                    {
                        var control = ReadControl(child, target.Value, filePath, diagnostics, degraded, positions);
                        if (control != null)
                        {
                            model.Controls.Add(control);
                        }
                    }
                    break;

                // Passes through to the markup untouched and is read-only on the canvas — the
                // runat="server" inversion. Read as raw inner text so CDATA survives verbatim.
                //
                // Web only: a .blform has no markup to pass through, so on that side it is an
                // unknown element rather than a literal.
                case "Literal" when target == FormTarget.Web:
                    model.Literal = string.Concat(element.Nodes().Select(n =>
                        n is XCData cdata ? cdata.Value : n is XText t ? t.Value : n.ToString()));
                    break;

                // The tray (Task 25): the same element grammar as a control — id, catalog
                // properties, <Bind>, unknown content — read through the same method, which is
                // what stops the two shapes drifting; the isComponent flag is what keeps a
                // component from acquiring a position or a tab index it cannot have.
                case "Components":
                    foreach (var child in element.Elements())
                    {
                        var component = ReadControl(
                            child, target.Value, filePath, diagnostics, degraded, positions, isComponent: true);
                        if (component != null)
                        {
                            model.Components.Add(component);
                        }
                    }
                    break;

                // The FORM's own event wiring (spec §2.3), the same shape a control's is — read through
                // the same ReadBind, so the reserved-data-binding refusal cannot drift between them.
                case "Bind":
                    model.Binds.Add(ReadBind(element, "form", filePath, diagnostics));
                    break;

                case "Resources":
                    model.Resources.AddRange(element.Elements().Select(e => new XElement(e)));
                    break;

                default:
                    // Unknown elements are neither Degraded nor Refused — they round-trip untouched.
                    model.UnknownChildren.Add(new XElement(element));
                    break;
            }
        }

        CheckDuplicateIds(model, filePath, diagnostics, positions);

        return new FormFile(model, xml, text, filePath, diagnostics, degraded, degradedRoot);
    }

    /// <summary>
    /// Refuses a document where two controls share an <c>Id</c>.
    ///
    /// <para>⛔ Checked across the WHOLE tree, not per container: the generated fields are all
    /// members of one class, so a Button inside a Panel and a Button on the form collide just as
    /// surely as two siblings. The generated code would declare the field twice and every
    /// reference to it would be ambiguous. Components are in the same class, so they are in the
    /// same check.</para>
    /// </summary>
    private static void CheckDuplicateIds(
        FormDocument model, string filePath, List<DesignDiagnostic> diagnostics,
        Dictionary<FormControl, XElement> positions)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var control in model.AllControls().Concat(model.AllComponents()))
        {
            if (control.Id.Length > 0 && !seen.Add(control.Id))
            {
                // ⚠ Reported AT THE SECOND OCCURRENCE, not at (0,0). This walks the MODEL, which
                // carries no positions, so the element each control was read from is kept aside as
                // it is read — without it the one diagnostic that names two places in the file was
                // the only one that could not point at either, and the sibling Id check right
                // beside it does carry a position.
                var at = positions.GetValueOrDefault(control);

                diagnostics.Add(Error(DesignCodes.DuplicateControlId,
                    $"more than one control has the Id '{control.Id}'. Ids become field names in " +
                    "the generated code, so they must be unique across the whole form.",
                    filePath, Line(at), Column(at)));
            }
        }
    }

    // ==================================================================
    // Which format is this?
    // ==================================================================

    /// <summary>The format the file's extension claims, or null when it claims neither.</summary>
    public static FormTarget? TargetOfExtension(string filePath) =>
        Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".blform" => FormTarget.WinForms,
            ".blwebform" => FormTarget.Web,
            _ => null
        };

    /// <summary>The format a root element name names, or null when it names neither.</summary>
    public static FormTarget? TargetOfRoot(string? rootName) => rootName switch
    {
        "Form" => FormTarget.WinForms,
        "WebForm" => FormTarget.Web,
        _ => null
    };

    private static string RootOf(FormTarget target) => target == FormTarget.WinForms ? "Form" : "WebForm";

    private static string ExtensionOf(FormTarget target) => target == FormTarget.WinForms ? ".blform" : ".blwebform";

    /// <summary>
    /// True when the root attribute is one this reader models, so it must NOT also be recorded as an
    /// unknown attribute and written back twice.
    ///
    /// <para>⛔ Asks <see cref="FormRootValues.RowForAttribute"/> — the one map from a FormRoot row to its
    /// storage — rather than keeping a list here.</para>
    ///
    /// <para>⚠ <paramref name="root"/> is passed because "known" is not purely a matter of spelling: on
    /// a WinForms document a <c>Width</c> the reader could not parse is left unmodelled, and the only
    /// thing that then preserves it is the unknown-attribute round trip (its row is Degraded).</para>
    /// </summary>
    private static bool IsKnownRootAttribute(string name, FormTarget target, XElement root)
    {
        if (name is "Name" or "Version")
        {
            return true;
        }

        var row = FormRootValues.RowForAttribute(name, target);
        if (row == null)
        {
            return false;
        }

        return row.Type == FormPropertyType.Size ? IntAttribute(root, name) != null : true;
    }

    // ==================================================================
    // Elements
    // ==================================================================

    private static FormLayout ReadLayout(XElement element)
    {
        var layout = new FormLayout
        {
            Cols = (string?)element.Attribute("Cols"),
            Rows = (string?)element.Attribute("Rows"),
            Gap = (string?)element.Attribute("Gap"),
            Dir = (string?)element.Attribute("Dir")
        };

        if (Enum.TryParse<FormLayoutKind>((string?)element.Attribute("Kind"), ignoreCase: true, out var kind))
        {
            layout.Kind = kind;
        }

        return layout;
    }

    /// <param name="isComponent">
    /// True when the element sits under <c>&lt;Components&gt;</c>. A component is a control with no
    /// place: it never acquires geometry or a tab index — a stray <c>X=</c> or <c>TabIndex=</c> on
    /// one is an unknown attribute that round-trips untouched (D9) — and it cannot nest.
    /// </param>
    /// <param name="parent">
    /// The control this element is nested under, or null at the top of <c>&lt;Controls&gt;</c> /
    /// <c>&lt;Components&gt;</c>. The CONTROL rather than its <c>FormControlDef</c> because all
    /// three BL8030 refusals name the parent's <c>Id</c> as well as its row, and two parameters
    /// carrying two halves of one parent are two parameters that can disagree.
    /// </param>
    private static FormControl? ReadControl(
        XElement element, FormTarget target, string filePath,
        List<DesignDiagnostic> diagnostics, List<DegradedProperty> degraded,
        Dictionary<FormControl, XElement> positions, bool isComponent = false,
        FormControl? parent = null)
    {
        var definition = FormControlCatalog.Find(element.Name.LocalName);
        if (definition == null)
        {
            // An element the catalog does not know is NOT a control and NOT an error — it belongs to
            // a newer designer.
            //
            // ⚠ Precisely what happens to it, because the obvious reading is wrong: it is NOT added
            // to the model anywhere. It survives an edit-and-save only because the writer's removal
            // sweep skips elements the catalog does not know, so nothing ever deletes it — survival
            // by omission, not by a round-trip mechanism. The consequences are real and bounded:
            // FormDocumentWriter.Create (which builds a document from the model alone) would not
            // reproduce it, and passes that walk the model — id uniqueness, tab order — cannot see
            // it. Modelling it properly needs a per-container unknown-children list, which is more
            // machinery than v1 earns; this comment exists so the next reader does not assume the
            // machinery is already here.
            return null;
        }

        // The row's SHAPE (Task 24, spec §1). Every decision below that once asked "is this a
        // component?" asks this instead, because the old bool could express neither of the two new
        // answers: a Docked strip and an Item are geometry-less and tab-index-less LIKE a component,
        // yet unlike one they live under <Controls> and they do nest.
        var place = definition.Place;
        var parentDefinition = parent?.Definition;
        var misplacedId = (string?)element.Attribute("Id") ?? "";
        var parentId = parent?.Id ?? "";

        // ⛔ BL8020: a component kind under <Controls>, or a control kind under <Components>. Either
        // would generate code csc rejects — Me.Controls.Add(tmr) for a Timer, or a Button that is
        // constructed and never added — so the document is refused rather than half-read.
        if ((place == FormPlace.Tray) != isComponent)
        {
            diagnostics.Add(Error(DesignCodes.ComponentMisplaced,
                isComponent
                    ? $"'{misplacedId}' is a {definition.Kind}, a control with a position, but it sits " +
                      "under <Components>. Move it under <Controls>: constructed here it would never " +
                      "be added to the form."
                    : $"'{misplacedId}' is a {definition.Kind}, a component with no position, but it " +
                      "sits under <Controls>. Move it under <Components>: emitting " +
                      $"Me.Controls.Add({misplacedId}) for it does not compile.",
                filePath, Line(element), Column(element)));
            return null;
        }

        // ⛔ BL8030, a refusal three ways (spec §3): an item outside a host that lists its kind, a
        // non-item inside a host, and a strip below the top level. Each is a document whose nesting
        // says something the generated code cannot honour — reading it anyway would put the control
        // somewhere the user did not write it and then emit code csc rejects, from a designer that
        // reported the file clean.
        //
        // ⚠ All three run BEFORE the FormControl is built, so a refused element contributes EXACTLY
        // ONE diagnostic — never a second one about its Id or a property of a control that is not
        // going to exist.
        if (place == FormPlace.Item && parentDefinition?.Items?.Accepts(definition.Kind) != true)
        {
            // Two of the spec's three cases behind ONE condition, because "no host at all" and "a
            // host that does not hold this kind" are the same fact about the item. The wording
            // still differs, because the fix does: one moves the item under a host, the other moves
            // it under a DIFFERENT one.
            //
            // ⚠ The third arm is not one of the spec's three and is not dead: a non-host PARENT (an
            // item under a Panel) reaches here with a definition whose Items is null, and the
            // wrong-host wording has no list of kinds to name.
            diagnostics.Add(Error(DesignCodes.StripMisplaced,
                parentDefinition == null
                    ? $"'{misplacedId}' is a {definition.Kind}, which lives inside " +
                      $"{HostsOf(definition.Kind)} — under <Controls> it would be added with " +
                      "Me.Controls.Add, which does not compile."
                    : parentDefinition.Items is { } rule
                        ? $"'{misplacedId}' is a {definition.Kind}, but '{parentId}' is a " +
                          $"{parentDefinition.Kind}, which holds only {string.Join(", ", rule.Kinds)}."
                        : $"'{misplacedId}' is a {definition.Kind}, which lives inside " +
                          $"{HostsOf(definition.Kind)} — it sits under '{parentId}', a " +
                          $"{parentDefinition.Kind}, which is not one.",
                filePath, Line(element), Column(element)));
            return null;
        }

        if (parentDefinition is { IsHost: true } host && place != FormPlace.Item)
        {
            diagnostics.Add(Error(DesignCodes.StripMisplaced,
                $"'{misplacedId}' is a {definition.Kind}, but it sits under '{parentId}'. " +
                $"A {host.Kind} holds only {string.Join(", ", host.Items!.Kinds)}.",
                filePath, Line(element), Column(element)));
            return null;
        }

        if (place == FormPlace.Docked && parentDefinition != null)
        {
            diagnostics.Add(Error(DesignCodes.StripMisplaced,
                $"'{misplacedId}' is a {definition.Kind}, which docks to the form — " +
                $"it sits under '{parentId}'.",
                filePath, Line(element), Column(element)));
            return null;
        }

        var control = new FormControl
        {
            Kind = definition.Kind,
            Id = misplacedId,

            // ⛔ Only a Positioned row has either. A Docked strip's position is its Dock PROPERTY
            // and an Item's is its order among its host's children; a stray X= or TabIndex= on
            // either falls through to UnknownAttributes below and round-trips untouched (D9, the
            // component rule), never to geometry — which would otherwise fire, because Dock is one
            // of ReadGeometry's six trigger attributes.
            TabIndex = place == FormPlace.Positioned ? IntAttribute(element, "TabIndex") ?? 0 : 0,
            Geometry = place == FormPlace.Positioned ? ReadGeometry(element, target) : null
        };

        // Where this control came from, for the checks that run over the finished MODEL and would
        // otherwise have no position to report. FormControl is a class, so the default comparer is
        // reference equality — two controls with the same Id are still two keys, which is the whole
        // case this serves.
        positions[control] = element;

        // ⛔⛔ The Id becomes a FIELD NAME in the user's own .bas. Until this check existed,
        // FormDocument.IsLegalControlId had no caller outside its own tests, and a document with
        // Id="" or Id="my-button" passed `design --check` with zero findings while the region
        // writer generated `Private  As Button` and `my-button = New Button()` into a file the
        // user owns. Refused, not warned: there is no partially-usable version of a control the
        // generated code cannot name.
        if (!FormDocument.IsLegalControlId(control.Id))
        {
            diagnostics.Add(Error(DesignCodes.IllegalControlId,
                control.Id.Length == 0
                    ? $"a <{element.Name.LocalName}> has no Id. Every control needs one — it " +
                      "becomes the field name the generated code declares and wires."
                    : $"'{control.Id}' is not a legal control Id. It becomes a BasicLang " +
                      "identifier, so it must start with a letter or underscore and contain only " +
                      "letters, digits and underscores.",
                filePath, Line(element), Column(element)));
        }

        foreach (var attribute in element.Attributes())
        {
            var name = attribute.Name.LocalName;

            // ⛔ Target-aware, because the two vocabularies overlap in spelling and not in meaning.
            // A flat "structural" list would swallow a .blwebform's Width="200" — neither a property
            // nor an unknown attribute, so absent from the model entirely, and Create would not
            // reproduce it. Each format treats only its OWN layout vocabulary as structural; the
            // other format's spelling is just an attribute this document does not model, which is
            // exactly what the unknown round trip is for.
            //
            // ⚠ Only a POSITIONED control has a structural vocabulary; everything else skips Id
            // alone. Its X= or TabIndex= are neither geometry nor tab order (it has none) nor a
            // catalog property, so they fall through to UnknownAttributes and round-trip untouched —
            // the same rule as any attribute the designer does not model.
            //
            // ⛔ This is also what lets a strip's Dock reach definition.Property("Dock") and land in
            // Properties. Dock is in StructuralAttributes, so while a Docked row took the structural
            // branch the catalog's own Dock property was skipped out of the loop and never modelled —
            // and FormDocumentWriter's "a catalog property the model dropped" sweep then DELETED
            // Dock="Top" from every strip on the first save.
            if (place == FormPlace.Positioned
                    ? FormControlCatalog.IsStructural(name, target)
                    : string.Equals(name, "Id", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // ⛔ A reserved resource reference cannot be resolved in v1 — <Resources> is empty by
            // definition — so the document is refused rather than half-understood.
            //
            // ⚠ The value is still recorded in Properties. Skipping it here left the attribute
            // present in the document but absent from the model, and the writer's
            // dropped-catalog-property sweep then DELETED it — so the document refused specifically
            // to preserve the reference was the one that lost it. Recording it keeps the round trip
            // honest; the refusal is what stops it being acted on.
            if (LooksLikeResourceReference(attribute.Value))
            {
                diagnostics.Add(Error(DesignCodes.ReservedResourceReference,
                    $"'{control.Id}.{name}' uses the reserved resource syntax " +
                    $"'{attribute.Value}', which v1 cannot resolve.",
                    filePath, Line(element), Column(element)));

                control.Properties[name] = attribute.Value;
                continue;
            }

            var property = definition.Property(name);
            if (property == null)
            {
                control.UnknownAttributes[name] = attribute.Value;
                continue;
            }

            control.Properties[name] = attribute.Value;

            // D9 Degraded: the catalog knows the attribute but the value does not parse — or parses and
            // cannot be used on THIS target (a Windows system colour with no CSS equivalent on a web form,
            // spec §2.2). The row is frozen with a reason and the value round-trips UNCHANGED — the whole
            // point is that one bad value costs one row, not the control and not the document.
            if (!property.Accepts(attribute.Value, target))
            {
                degraded.Add(new DegradedProperty(control.Id, name, attribute.Value,
                    property.DescribeRefusal(attribute.Value, target)));
            }
        }

        foreach (var child in element.Elements())
        {
            if (child.Name.LocalName == "Bind")
            {
                control.Binds.Add(ReadBind(child, control.Id, filePath, diagnostics));
                continue;
            }

            // A component cannot nest, so a catalog kind under one is an unknown child. Everything
            // else does: a Positioned container holds controls, a Docked strip holds its items and
            // an Item holds its sub-items — and which of those are LEGAL is BL8030's answer above,
            // reached by passing this control down as the parent, not by refusing to recurse.
            if (place != FormPlace.Tray && FormControlCatalog.Find(child.Name.LocalName) != null)
            {
                var nested = ReadControl(
                    child, target, filePath, diagnostics, degraded, positions, parent: control);
                if (nested != null)
                {
                    control.Children.Add(nested);
                }

                continue;
            }

            control.UnknownChildren.Add(new XElement(child));
        }

        return control;
    }

    /// <summary>
    /// One <c>&lt;Bind&gt;</c>, for a control or the form (<paramref name="owner"/> is the Id, or
    /// <c>form</c>). ⛔ Reserved means parsed and round-tripped, never acted on: a populated data binding
    /// is refused rather than ignored — ignoring it leaves the user believing a binding exists, and
    /// nothing in the running page would ever tell them otherwise.
    /// </summary>
    private static FormBind ReadBind(XElement element, string owner, string filePath, List<DesignDiagnostic> diagnostics)
    {
        var bind = new FormBind
        {
            Event = (string?)element.Attribute("Event") ?? "",
            Handler = (string?)element.Attribute("Handler") ?? "",
            Property = (string?)element.Attribute("Property"),
            Source = (string?)element.Attribute("Source"),
            Path = (string?)element.Attribute("Path")
        };

        if (bind.UsesReservedDataBinding)
        {
            diagnostics.Add(Error(DesignCodes.ReservedBindingPopulated,
                $"'{owner}' has a <Bind> using the reserved data-binding attributes " +
                "(Property/Source/Path). v1 reads only <Bind Event= Handler=>.",
                filePath, Line(element), Column(element)));
        }

        return bind;
    }

    /// <summary>
    /// The kinds whose item rule lists <paramref name="kind"/>, as prose: "a MenuStrip or a
    /// ToolStripMenuItem".
    ///
    /// <para>⚠ Derived from the catalog, never spelled out in the message. A row added later that
    /// accepts this kind changes the sentence with it; a hand-written list would go on naming the
    /// hosts of 2026 at a user who has just been told to use one of them.</para>
    /// </summary>
    private static string HostsOf(string kind)
    {
        var hosts = FormControlCatalog.All
            .Where(d => d.Items?.Accepts(kind) == true)
            .Select(d => "a " + d.Kind)
            .ToList();

        return hosts.Count switch
        {
            // No row holds it. Unreachable for the shipped rows, and NOT an exception: this is the
            // text of a refusal that is already being reported, and throwing here would replace a
            // precise diagnostic with a crash in the reader.
            0 => "a strip",
            1 => hosts[0],
            _ => string.Join(", ", hosts.Take(hosts.Count - 1)) + " or " + hosts[^1]
        };
    }

    /// <summary>
    /// The control's position, in the vocabulary of the document's own format (D3).
    ///
    /// <para>⛔ Selected by the TARGET, not by sniffing which attributes are present. Sniffing reads
    /// a stray <c>Col</c> on a <c>.blform</c> control as a grid cell, and the writer then emits grid
    /// geometry into a document whose every other control is absolute — a document that is half one
    /// format and half the other, produced by a save the user did not know was a conversion.</para>
    /// </summary>
    private static FormGeometry? ReadGeometry(XElement element, FormTarget target)
    {
        if (target == FormTarget.WinForms)
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
    /// An integer attribute, or null when absent OR unparseable. Never throws.
    ///
    /// <para>⛔ Culture-free (<see cref="FormPropertyDef.TryParseInt"/>), the same parser the writer's
    /// no-op comparison uses: a document means the same number on every machine, and U+2212 is not a
    /// minus in it even under the culture that spells negatives that way.</para>
    /// </summary>
    private static int? IntAttribute(XElement element, string name) =>
        (string?)element.Attribute(name) is { } text && FormPropertyDef.TryParseInt(text, out var value) ? value : null;

    /// <summary>`{res:Key}` — the reserved resource syntax.</summary>
    private static bool LooksLikeResourceReference(string value) =>
        value.StartsWith("{res:", StringComparison.OrdinalIgnoreCase) &&
        value.EndsWith("}", StringComparison.Ordinal);

    private static DesignDiagnostic Error(string code, string message, string filePath, int line, int column) =>
        new(code, $"{code}: {message}", filePath, line, column, IsWarning: false);

    // IXmlLineInfo is why LoadOptions.SetLineInfo is passed; without it these are always 0.
    private static int Line(XObject? node) => (node as IXmlLineInfo)?.HasLineInfo() == true
        ? ((IXmlLineInfo)node).LineNumber : 0;

    private static int Column(XObject? node) => (node as IXmlLineInfo)?.HasLineInfo() == true
        ? ((IXmlLineInfo)node).LinePosition : 0;
}
