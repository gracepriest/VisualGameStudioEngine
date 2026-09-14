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

    // --- .blform only (D3) -------------------------------------------------

    /// <summary>The form's client size. WinForms only; null on a web document.</summary>
    public int? Width { get; set; }
    public int? Height { get; set; }

    /// <summary>The window caption. WinForms only.</summary>
    public string? Text { get; set; }

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

    /// <summary>Top-level controls, in document order.</summary>
    public List<FormControl> Controls { get; } = new();

    /// <summary>Reserved and empty in v1; parsed and re-emitted so a future document round-trips.</summary>
    public List<XElement> Components { get; } = new();
    public List<XElement> Resources { get; } = new();

    /// <summary>Root attributes and child elements the reader did not recognise (D9).</summary>
    public Dictionary<string, string> UnknownAttributes { get; } = new(StringComparer.Ordinal);
    public List<XElement> UnknownChildren { get; } = new();

    /// <summary>Every control in the document, containers before their children.</summary>
    public IEnumerable<FormControl> AllControls() => Controls.SelectMany(c => c.SelfAndDescendants());

    public FormControl? FindById(string id) =>
        AllControls().FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.Ordinal));

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

    /// <summary>Assigns <see cref="FormControl.TabIndex"/> in document order, starting at 0.</summary>
    public void RenumberTabIndexes()
    {
        var index = 0;
        foreach (var control in AllControls())
        {
            control.TabIndex = index++;
        }
    }
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
    /// Serializes controls (with their descendants) to a self-describing XML fragment.
    /// The target is recorded so a paste into the other format can be refused rather than
    /// producing a WinForms control on a page.
    /// </summary>
    public static string SerializeSubtree(FormTarget target, IEnumerable<FormControl> controls)
    {
        var root = new XElement(ClipboardRoot,
            new XAttribute("Target", target.ToString()),
            new XAttribute("Version", 1));

        foreach (var control in controls)
        {
            root.Add(ToElement(control));
        }

        return root.ToString();
    }

    /// <summary>
    /// Reads a fragment produced by <see cref="SerializeSubtree"/>, renaming every control whose id
    /// is already taken and retargeting the binds that named it.
    /// </summary>
    /// <param name="isTaken">
    /// Whether an id is in use. The caller passes the destination document's lookup; ids minted
    /// during this paste are added to it internally, so two pasted siblings cannot collide.
    /// </param>
    /// <returns>The controls to add, already renamed. Empty when the fragment targets the other format.</returns>
    public static IReadOnlyList<FormControl> DeserializeSubtree(
        string xml, FormTarget target, Func<string, bool> isTaken)
    {
        XElement root;
        try
        {
            root = XElement.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            return Array.Empty<FormControl>();
        }

        if (root.Name.LocalName != ClipboardRoot)
        {
            return Array.Empty<FormControl>();
        }

        // A .blform subtree pasted into a .blwebform would produce controls the web catalog cannot
        // emit. Refusing is the honest answer; silently dropping the unsupported ones is not.
        if (!Enum.TryParse<FormTarget>((string?)root.Attribute("Target"), out var sourceTarget) ||
            sourceTarget != target)
        {
            return Array.Empty<FormControl>();
        }

        var minted = new HashSet<string>(StringComparer.Ordinal);
        bool Taken(string id) => minted.Contains(id) || isTaken(id);

        var result = new List<FormControl>();
        foreach (var element in root.Elements())
        {
            var control = FromElement(element, target);
            if (control != null)
            {
                Rename(control, Taken, minted);
                result.Add(control);
            }
        }

        return result;
    }

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
        var element = new XElement(control.Kind,
            new XAttribute("Id", control.Id),
            new XAttribute("TabIndex", control.TabIndex));

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

        foreach (var (name, value) in control.Properties)
        {
            element.SetAttributeValue(name, value);
        }

        foreach (var (name, value) in control.UnknownAttributes)
        {
            element.SetAttributeValue(name, value);
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

        foreach (var child in control.Children)
        {
            element.Add(ToElement(child));
        }

        return element;
    }

    /// <param name="target">
    /// ⛔ The pasted subtree's format. Without it this used the target-AGNOSTIC
    /// <c>IsStructural</c>, which treats both vocabularies as structural — so copying a web
    /// control silently dropped its <c>Width</c>, and copying a WinForms control dropped its
    /// <c>Col</c>, because each is structural in the OTHER format and so was skipped without ever
    /// reaching <c>Properties</c> or <c>UnknownAttributes</c>. That is precisely the trap the
    /// target-aware overload was added to document, reached through the one call site that still
    /// used the old one.
    /// </param>
    private static FormControl? FromElement(XElement element, FormTarget target)
    {
        var definition = FormControlCatalog.Find(element.Name.LocalName);
        if (definition == null)
        {
            return null;
        }

        var control = new FormControl
        {
            Kind = definition.Kind,
            Id = (string?)element.Attribute("Id") ?? "",
            TabIndex = IntAttribute(element, "TabIndex") ?? 0
        };

        control.Geometry = ReadGeometry(element);

        foreach (var attribute in element.Attributes())
        {
            var name = attribute.Name.LocalName;
            if (FormControlCatalog.IsStructural(name, target))
            {
                continue;
            }

            if (definition.Property(name) != null)
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
            else if (FormControlCatalog.Find(child.Name.LocalName) != null)
            {
                var nested = FromElement(child, target);
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

        return control;
    }

    private static FormGeometry? ReadGeometry(XElement element)
    {
        // ⛔ int.TryParse, never a (int?) cast. The XLinq cast THROWS FormatException on a value it
        // cannot parse, and the clipboard is exactly where unvetted text arrives — a paste of a
        // fragment carrying X="20px" would take the IDE down rather than declining the paste.
        if (element.Attribute("X") != null || element.Attribute("Y") != null)
        {
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

        if (element.Attribute("Col") != null || element.Attribute("Row") != null)
        {
            return new GridGeometry
            {
                Col = IntAttribute(element, "Col") ?? 0,
                Row = IntAttribute(element, "Row") ?? 0,
                ColSpan = IntAttribute(element, "ColSpan") ?? 1,
                RowSpan = IntAttribute(element, "RowSpan") ?? 1
            };
        }

        return null;
    }

    /// <summary>An integer attribute, or null when absent OR unparseable. Never throws.</summary>
    private static int? IntAttribute(XElement element, string name) =>
        int.TryParse((string?)element.Attribute(name), out var value) ? value : null;
}
