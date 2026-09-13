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

        // What the FILE NAME claims this is, or null when the path carries neither form extension.
        // Only a claim: the root element is the document's own self-description and wins below.
        var claimed = TargetOfExtension(filePath);

        var model = new FormDocument
        {
            Target = claimed ?? FormTarget.Web,
            Name = Path.GetFileNameWithoutExtension(filePath)
        };

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

        if (target == FormTarget.WinForms)
        {
            // The form's own client size and caption. WinForms only — D3 gives the web document a
            // <Layout> instead, and a page has no window to size.
            //
            // ⚠ An unparseable Width/Height is left null here and falls through to UnknownAttributes
            // below, which is what round-trips it verbatim. It is NOT a Degraded row: Degraded is
            // per-property on a CONTROL (it freezes one property-grid row), and the form root is not
            // a control — there is no row to freeze and FormFile.TierOf could not find one.
            model.Width = IntAttribute(root, "Width");
            model.Height = IntAttribute(root, "Height");
            model.Text = (string?)root.Attribute("Text");
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
                        var control = ReadControl(child, target.Value, filePath, diagnostics, degraded);
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

                case "Components":
                    model.Components.AddRange(element.Elements().Select(e => new XElement(e)));
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

        CheckDuplicateIds(model, filePath, diagnostics);

        return new FormFile(model, xml, text, filePath, diagnostics, degraded);
    }

    /// <summary>
    /// Refuses a document where two controls share an <c>Id</c>.
    ///
    /// <para>⛔ Checked across the WHOLE tree, not per container: the generated fields are all
    /// members of one class, so a Button inside a Panel and a Button on the form collide just as
    /// surely as two siblings. The generated code would declare the field twice and every
    /// reference to it would be ambiguous.</para>
    /// </summary>
    private static void CheckDuplicateIds(
        FormDocument model, string filePath, List<DesignDiagnostic> diagnostics)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var control in model.AllControls())
        {
            if (control.Id.Length > 0 && !seen.Add(control.Id))
            {
                diagnostics.Add(Error(DesignCodes.DuplicateControlId,
                    $"more than one control has the Id '{control.Id}'. Ids become field names in " +
                    "the generated code, so they must be unique across the whole form.",
                    filePath, 0, 0));
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
    /// <para>⚠ <paramref name="root"/> is passed because "known" is not purely a matter of spelling:
    /// on a WinForms document a <c>Width</c> the reader could not parse is left unmodelled, and the
    /// only thing that then preserves it is the unknown-attribute round trip.</para>
    /// </summary>
    private static bool IsKnownRootAttribute(string name, FormTarget target, XElement root)
    {
        if (name is "Name" or "Version")
        {
            return true;
        }

        if (target != FormTarget.WinForms)
        {
            return false;
        }

        return name switch
        {
            "Width" or "Height" => IntAttribute(root, name) != null,
            "Text" => true,
            _ => false
        };
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

    private static FormControl? ReadControl(
        XElement element, FormTarget target, string filePath,
        List<DesignDiagnostic> diagnostics, List<DegradedProperty> degraded)
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

        var control = new FormControl
        {
            Kind = definition.Kind,
            Id = (string?)element.Attribute("Id") ?? "",
            TabIndex = IntAttribute(element, "TabIndex") ?? 0,
            Geometry = ReadGeometry(element, target)
        };

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
            if (FormControlCatalog.IsStructural(name, target))
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

            // D9 Degraded: the catalog knows the attribute but the value does not parse. The row is
            // frozen with a reason and the value round-trips UNCHANGED — the whole point is that one
            // bad value costs one row, not the control and not the document.
            if (!property.Accepts(attribute.Value))
            {
                degraded.Add(new DegradedProperty(control.Id, name, attribute.Value,
                    $"'{attribute.Value}' is not a valid {property.Type}" +
                    (property.AllowedValues is { Count: > 0 }
                        ? $" (expected one of: {string.Join(", ", property.AllowedValues)})"
                        : "") +
                    ". The value is preserved exactly as written."));
            }
        }

        foreach (var child in element.Elements())
        {
            if (child.Name.LocalName == "Bind")
            {
                var bind = new FormBind
                {
                    Event = (string?)child.Attribute("Event") ?? "",
                    Handler = (string?)child.Attribute("Handler") ?? "",
                    Property = (string?)child.Attribute("Property"),
                    Source = (string?)child.Attribute("Source"),
                    Path = (string?)child.Attribute("Path")
                };

                // ⛔ Reserved means parsed and round-tripped, never acted on. A populated data
                // binding is refused rather than ignored: ignoring it leaves the user believing a
                // binding exists, and nothing in the running page would ever tell them otherwise.
                if (bind.UsesReservedDataBinding)
                {
                    diagnostics.Add(Error(DesignCodes.ReservedBindingPopulated,
                        $"'{control.Id}' has a <Bind> using the reserved data-binding attributes " +
                        "(Property/Source/Path). v1 reads only <Bind Event= Handler=>.",
                        filePath, Line(child), Column(child)));
                }

                control.Binds.Add(bind);
                continue;
            }

            if (FormControlCatalog.Find(child.Name.LocalName) != null)
            {
                var nested = ReadControl(child, target, filePath, diagnostics, degraded);
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

    /// <summary>An integer attribute, or null when absent OR unparseable. Never throws.</summary>
    private static int? IntAttribute(XElement element, string name) =>
        int.TryParse((string?)element.Attribute(name), out var value) ? value : null;

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
