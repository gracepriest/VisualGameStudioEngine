using System.Xml;
using System.Xml.Linq;

namespace BasicLang.Forms.Serialization;

/// <summary>
/// Reads a <c>.blwebform</c> into a <see cref="BlWebForm"/>, applying D9's tier rules.
///
/// <para>⛔ Loaded with <c>LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace</c>. SetLineInfo
/// is what lets every diagnostic carry a real <c>file(line,col)</c> instead of pointing at the top
/// of the document — a finding without a position is one nobody acts on. PreserveWhitespace is what
/// lets the writer put the file back byte-identically.</para>
/// </summary>
public static class BlWebFormReader
{
    /// <summary>The only document version v1 understands. A newer one is refused, not guessed at.</summary>
    public const int SupportedVersion = 1;

    public static BlWebForm Read(string filePath, string text)
    {
        var diagnostics = new List<DesignDiagnostic>();
        var degraded = new List<DegradedProperty>();
        var model = new FormDocument { Target = FormTarget.Web, Name = Path.GetFileNameWithoutExtension(filePath) };

        XDocument xml;
        try
        {
            xml = XDocument.Parse(text, LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
        }
        catch (XmlException ex)
        {
            diagnostics.Add(Error(DesignCodes.MalformedDocument,
                $"the document is not well-formed XML — {ex.Message}", filePath, ex.LineNumber, ex.LinePosition));
            return new BlWebForm(model, new XDocument(), text, filePath, diagnostics, degraded);
        }

        var root = xml.Root;
        if (root == null || root.Name.LocalName != "WebForm")
        {
            diagnostics.Add(Error(DesignCodes.MalformedDocument,
                $"the root element must be <WebForm>, not <{root?.Name.LocalName ?? "(none)"}>.",
                filePath, Line(root), Column(root)));
            return new BlWebForm(model, xml, text, filePath, diagnostics, degraded);
        }

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
            return new BlWebForm(model, xml, text, filePath, diagnostics, degraded);
        }

        model.Name = (string?)root.Attribute("Name") ?? model.Name;
        model.Version = version;

        foreach (var attribute in root.Attributes())
        {
            if (attribute.Name.LocalName is not ("Name" or "Version"))
            {
                model.UnknownAttributes[attribute.Name.LocalName] = attribute.Value;
            }
        }

        foreach (var element in root.Elements())
        {
            switch (element.Name.LocalName)
            {
                case "Layout":
                    model.Layout = ReadLayout(element);
                    break;

                case "Controls":
                    foreach (var child in element.Elements())
                    {
                        var control = ReadControl(child, filePath, diagnostics, degraded);
                        if (control != null)
                        {
                            model.Controls.Add(control);
                        }
                    }
                    break;

                // Passes through to the markup untouched and is read-only on the canvas — the
                // runat="server" inversion. Read as raw inner text so CDATA survives verbatim.
                case "Literal":
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

        return new BlWebForm(model, xml, text, filePath, diagnostics, degraded);
    }

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
        XElement element, string filePath,
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
            // BlWebFormWriter.Create (which builds a document from the model alone) would not
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
            Geometry = ReadGeometry(element)
        };

        foreach (var attribute in element.Attributes())
        {
            var name = attribute.Name.LocalName;
            if (FormControlCatalog.IsStructural(name))
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
                var nested = ReadControl(child, filePath, diagnostics, degraded);
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

    private static FormGeometry? ReadGeometry(XElement element)
    {
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
