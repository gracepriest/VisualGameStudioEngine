namespace BasicLang.Forms;

/// <summary>
/// The declared type of a catalog property. Drives D9's <b>Canon</b> tier: a property is fully
/// editable only when the catalog knows the attribute AND its value parses to this type.
/// </summary>
public enum FormPropertyType
{
    String,
    Int,
    Bool,
    Color,
    /// <summary>One of <see cref="FormPropertyDef.AllowedValues"/>.</summary>
    Enum
}

/// <summary>One editable property of one control kind.</summary>
/// <param name="Name">The document attribute name, which is also the WinForms property name.</param>
/// <param name="Type">Declared type; a value that does not parse to it drops out of the Canon tier.</param>
/// <param name="Default">Written only when the value differs from this. Null = always write when set.</param>
/// <param name="AllowedValues">For <see cref="FormPropertyType.Enum"/>; empty otherwise.</param>
/// <param name="WinFormsEnumType">
/// The <c>System.Windows.Forms</c> enum an <see cref="FormPropertyType.Enum"/> value belongs to,
/// e.g. <c>ContentAlignment</c>.
///
/// <para>⛔ Required for every Enum property, and MEASURED, not assumed. The generated source is
/// C# by way of BasicLang, and an unqualified <c>Center</c> is not a value in either — BasicLang
/// compiles <c>lbl.TextAlign = Center</c> happily (WinForms member access degrades to
/// <c>Object</c> with no diagnostic) and csc then rejects the emitted C# with CS0103. Nothing
/// short of csc catches it.</para>
/// </param>
/// <param name="WinFormsMemberNames">
/// Where the designer's vocabulary and the WinForms member name DIFFER, keyed by the designer's
/// value. <c>ContentAlignment</c> has no <c>Left</c> — it has <c>MiddleLeft</c> — so the catalog's
/// own Left/Center/Right cannot be emitted verbatim. Omit when the names already agree.
/// </param>
/// <param name="Targets">
/// The targets this property actually EXISTS on; null means both.
///
/// <para>⛔ Not a preference — a fact about the platform, and every entry here was found by csc
/// rather than by reading docs. WinForms <c>RadioButton</c> has no <c>GroupName</c> (it groups by
/// container) and WinForms <c>ListBox</c> has no <c>MultiSelect</c> (it has <c>SelectionMode</c>).
/// Both compiled green through BasicLang and were rejected by csc with CS1061.</para>
/// </param>
/// <param name="WinFormsFactory">
/// A function to wrap the value in, when the WinForms property type is not the string the designer
/// edits. <c>PasswordChar</c> is a <c>char</c> and <c>Image</c> is a <c>System.Drawing.Image</c>;
/// assigning a string to either is CS0029.
///
/// <para>⚠ <c>Convert.ToChar</c>, not <c>CChar</c> — measured. BasicLang passes <c>CChar</c>
/// through to the C# backend verbatim and C# has no such function, so it fails at csc with
/// CS0103.</para>
/// </param>
/// <param name="IsItemCollection">
/// True when the value is a comma-separated list that must be ADDED to a read-only collection
/// rather than assigned. <c>ComboBox.Items</c> and <c>ListBox.Items</c> are get-only, so assigning
/// one is CS0200.
/// </param>
public sealed record FormPropertyDef(
    string Name,
    FormPropertyType Type,
    string? Default = null,
    IReadOnlyList<string>? AllowedValues = null,
    string? WinFormsEnumType = null,
    IReadOnlyDictionary<string, string>? WinFormsMemberNames = null,
    IReadOnlyList<FormTarget>? Targets = null,
    string? WinFormsFactory = null,
    bool IsItemCollection = false)
{
    /// <summary>True when this property exists on <paramref name="target"/>.</summary>
    public bool AppliesTo(FormTarget target) => Targets == null || Targets.Contains(target);

    /// <summary>
    /// The value as WinForms SOURCE — what the region writer splices after the <c>=</c>.
    ///
    /// <para>Returns null when this property has nothing special to say, leaving the caller's
    /// default formatting in charge.</para>
    /// </summary>
    public string? WinFormsLiteral(string value)
    {
        if (Type == FormPropertyType.Enum && WinFormsEnumType != null)
        {
            var member = WinFormsMemberNames != null &&
                         WinFormsMemberNames.TryGetValue(value, out var mapped)
                ? mapped
                : value;
            return $"{WinFormsEnumType}.{member}";
        }

        if (WinFormsFactory != null)
        {
            return $"{WinFormsFactory}(\"{value.Replace("\"", "\"\"")}\")";
        }

        return Type == FormPropertyType.Color ? ColorLiteral(value) : null;
    }

    /// <summary>The individual items of an <see cref="IsItemCollection"/> value.</summary>
    public static IEnumerable<string> SplitItems(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// A colour as WinForms source.
    ///
    /// <para>⛔ <c>#RRGGBB</c> is a value the catalog ACCEPTS (so it is Canon in the document and
    /// round-trips untouched) and which cannot be emitted verbatim: <c>#</c> starts a preprocessor
    /// directive, so <c>lbl.ForeColor = #FF0000</c> does not even LEX. It has to become a
    /// <c>Color.FromArgb</c> call. A named colour passes through as <c>Color.Name</c>.</para>
    /// </summary>
    private static string ColorLiteral(string value)
    {
        if (value.Length == 0 || value[0] != '#')
        {
            return "Color." + value;
        }

        var digits = value.Substring(1);

        // #rgb -> #rrggbb, so one path handles both.
        if (digits.Length == 3)
        {
            digits = string.Concat(digits.Select(c => new string(c, 2)));
        }

        if (digits.Length == 6)
        {
            digits = "FF" + digits;
        }

        if (digits.Length != 8 || !digits.All(Uri.IsHexDigit))
        {
            // Not a shape this understands. Degraded values reach here (the tier freezes the
            // property-grid row, it does not stop the writer), and inventing a colour for one
            // would put a value on screen the document never carried.
            return "Color." + value;
        }

        static int Hex(string text, int start) =>
            Convert.ToInt32(text.Substring(start, 2), 16);

        return $"Color.FromArgb({Hex(digits, 0)}, {Hex(digits, 2)}, {Hex(digits, 4)}, {Hex(digits, 6)})";
    }

    /// <summary>True when <paramref name="value"/> parses to this property's declared type.</summary>
    public bool Accepts(string? value)
    {
        if (value == null)
        {
            return false;
        }

        return Type switch
        {
            FormPropertyType.String => true,
            FormPropertyType.Int => int.TryParse(value, out _),
            FormPropertyType.Bool => bool.TryParse(value, out _),
            // "#rrggbb", "#rgb", or a bare name the target resolves (WinForms KnownColor / CSS name).
            FormPropertyType.Color => IsColor(value),
            FormPropertyType.Enum => AllowedValues != null &&
                                     AllowedValues.Any(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase)),
            _ => false
        };
    }

    private static bool IsColor(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        if (value[0] == '#')
        {
            var digits = value.Substring(1);
            return (digits.Length == 3 || digits.Length == 6 || digits.Length == 8) &&
                   digits.All(Uri.IsHexDigit);
        }

        // A bare identifier — a KnownColor or a CSS named colour. The catalog does not carry the
        // 140-odd names; the target resolves it, and an unknown one degrades to D9's Opaque tier
        // rather than being rejected here.
        return value.All(c => char.IsLetter(c));
    }
}

/// <summary>One control kind, in both target vocabularies.</summary>
/// <param name="Kind">The document element name, e.g. <c>Button</c>. Shared by both formats.</param>
/// <param name="WinFormsType">Unqualified <c>System.Windows.Forms</c> type name, or null if web-only.</param>
/// <param name="HtmlTag">HTML element the web emitter produces, or null if WinForms-only.</param>
/// <param name="HtmlInputType">
/// <c>type=</c> for an <c>&lt;input&gt;</c>, e.g. <c>checkbox</c>. Null when <see cref="HtmlTag"/>
/// is not <c>input</c>.
/// </param>
/// <param name="IsContainer">True when the control may hold child controls (Panel, GroupBox).</param>
/// <param name="Properties">Editable properties, in the order a property grid should show them.</param>
public sealed record FormControlDef(
    string Kind,
    string? WinFormsType,
    string? HtmlTag,
    string? HtmlInputType,
    bool IsContainer,
    IReadOnlyList<FormPropertyDef> Properties)
{
    public bool SupportsTarget(FormTarget target) => target switch
    {
        FormTarget.WinForms => WinFormsType != null,
        FormTarget.Web => HtmlTag != null,
        _ => false
    };

    public FormPropertyDef? Property(string name) =>
        Properties.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// The single source of truth for "which controls exist and what can be set on them".
///
/// <para>⛔ Everything that needs to enumerate controls — the toolbox, the property grid, the markup
/// emitter, the region writer and above all the CI gate — MUST read this table rather than carry its
/// own list. That is not tidiness. A WinForms control catalog is otherwise <b>unfalsifiable</b> in
/// this repo: <c>EnableNetResolution</c> returns early for <c>UseWindowsForms</c>
/// (<c>Compiler.cs:145</c>), the resolver closure cannot reach <c>System.Windows.Forms.dll</c>
/// (<c>NetReferenceResolver.cs:154-179</c>), <c>CommonNetTypes</c> has zero WinForms names, and every
/// <c>Form</c>/<c>Button</c>/<c>Point</c> member access therefore types as <c>Object</c> with no
/// diagnostic. A misspelled property name compiles green. The stand-in for the type system that does
/// not exist is a gate that generates EVERY control with EVERY property and requires the real CLI to
/// exit 0 — and that gate is only meaningful if it is driven from this table. Add a row; never
/// hand-write a <c>[TestCase]</c> list beside it.</para>
/// </summary>
public static class FormControlCatalog
{
    // Shared property definitions. Declared once so a control kind cannot drift from its peers
    // in the spelling or the declared type of a property they both carry.
    private static readonly FormPropertyDef Text = new("Text", FormPropertyType.String);
    private static readonly FormPropertyDef Enabled = new("Enabled", FormPropertyType.Bool, "true");
    private static readonly FormPropertyDef Visible = new("Visible", FormPropertyType.Bool, "true");
    private static readonly FormPropertyDef ForeColor = new("ForeColor", FormPropertyType.Color);
    private static readonly FormPropertyDef BackColor = new("BackColor", FormPropertyType.Color);
    private static readonly FormPropertyDef Checked = new("Checked", FormPropertyType.Bool, "false");

    // ⛔ ContentAlignment has no Left/Center/Right — it is a 3x3 grid of Top/Middle/Bottom by
    // Left/Center/Right. The designer keeps the simple horizontal vocabulary and maps to the
    // middle row, which is what a single-line Label or Button actually wants.
    private static readonly FormPropertyDef TextAlign = new(
        "TextAlign", FormPropertyType.Enum, "Left", new[] { "Left", "Center", "Right" },
        WinFormsEnumType: "ContentAlignment",
        WinFormsMemberNames: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Left"] = "MiddleLeft",
            ["Center"] = "MiddleCenter",
            ["Right"] = "MiddleRight"
        });

    private static IReadOnlyList<FormPropertyDef> Common(params FormPropertyDef[] own) =>
        own.Concat(new[] { Enabled, Visible, ForeColor, BackColor }).ToList();

    /// <summary>
    /// The ten kinds of v1. Ten is a deliberate floor, not a ceiling: it is the smallest set that
    /// covers a login form, a settings pane and a list-detail pane — the three shapes the designer
    /// has to handle before it is worth using at all.
    ///
    /// <para>⛔ This field MUST stay below the shared <c>FormPropertyDef</c> fields it reads through
    /// <c>Common(…)</c>. Static field initializers run in textual order, so moving it above them
    /// leaves every shared property null inside the concat — with no compile error and no exception
    /// at init, just a NullReferenceException later from <c>Property()</c> or <c>Accepts()</c>.</para>
    /// </summary>
    public static readonly IReadOnlyList<FormControlDef> All = new List<FormControlDef>
    {
        new("Label",       "Label",       "label",    null,       false, Common(Text, TextAlign)),
        new("TextBox",     "TextBox",     "input",    "text",     false, Common(
            Text,
            new FormPropertyDef("Multiline", FormPropertyType.Bool, "false"),
            new FormPropertyDef("ReadOnly", FormPropertyType.Bool, "false"),
            new FormPropertyDef("MaxLength", FormPropertyType.Int),
            // WinForms PasswordChar is a char, not a string — assigning one is CS0029.
            new FormPropertyDef("PasswordChar", FormPropertyType.String,
                WinFormsFactory: "Convert.ToChar"))),
        new("Button",      "Button",      "button",   null,       false, Common(Text, TextAlign)),
        new("CheckBox",    "CheckBox",    "input",    "checkbox", false, Common(Text, Checked)),
        new("RadioButton", "RadioButton", "input",    "radio",    false, Common(
            Text,
            Checked,
            // ⛔ WEB ONLY. The DOM groups radios by name=; WinForms groups them by CONTAINER and
            // has no GroupName property at all — csc says CS1061, BasicLang says nothing.
            new FormPropertyDef("GroupName", FormPropertyType.String,
                Targets: new[] { FormTarget.Web }))),
        new("ComboBox",    "ComboBox",    "select",   null,       false, Common(
            Text,
            // Items is a get-only collection on WinForms — assigning it is CS0200.
            new FormPropertyDef("Items", FormPropertyType.String, IsItemCollection: true),
            new FormPropertyDef("SelectedIndex", FormPropertyType.Int, "-1"))),
        new("ListBox",     "ListBox",     "select",   null,       false, Common(
            new FormPropertyDef("Items", FormPropertyType.String, IsItemCollection: true),
            new FormPropertyDef("SelectedIndex", FormPropertyType.Int, "-1"),
            // ⛔ WEB ONLY. WinForms ListBox has no MultiSelect — it has SelectionMode, an enum.
            // Mapping a Bool onto it is a design decision v1 has not made, so the property stays
            // web-only rather than being silently approximated.
            new FormPropertyDef("MultiSelect", FormPropertyType.Bool, "false",
                Targets: new[] { FormTarget.Web }))),
        new("Panel",       "Panel",       "div",      null,       true,  Common(
            new FormPropertyDef("BorderStyle", FormPropertyType.Enum, "None",
                new[] { "None", "FixedSingle", "Fixed3D" },
                WinFormsEnumType: "BorderStyle"))),
        new("GroupBox",    "GroupBox",    "fieldset", null,       true,  Common(Text)),
        new("PictureBox",  "PictureBox",  "img",      null,       false, Common(
            // WinForms Image is a System.Drawing.Image, not a path string (CS0029).
            new FormPropertyDef("Image", FormPropertyType.String,
                WinFormsFactory: "Image.FromFile"),
            new FormPropertyDef("SizeMode", FormPropertyType.Enum, "Normal",
                new[] { "Normal", "StretchImage", "AutoSize", "CenterImage", "Zoom" },
                WinFormsEnumType: "PictureBoxSizeMode"))),
    };

    public static FormControlDef? Find(string kind) =>
        All.FirstOrDefault(c => string.Equals(c.Kind, kind, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The single kind that emits this HTML tag, or null when none does OR more than one does.
    ///
    /// <para>Ambiguity returns null deliberately. <c>select</c> is both ComboBox and ListBox, and
    /// <c>input</c> is three kinds separated only by <c>type=</c> — which
    /// <c>document.createElement("input")</c> does not carry. Guessing would silently rewrite a
    /// multi-select as a dropdown on the next save; reporting "no catalog row" lets the caller say
    /// so instead.</para>
    /// </summary>
    public static FormControlDef? FindByHtmlTag(string tag)
    {
        var matches = All.Where(c => string.Equals(c.HtmlTag, tag, StringComparison.OrdinalIgnoreCase)).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    /// <summary>The kinds usable in a given document format.</summary>
    public static IEnumerable<FormControlDef> For(FormTarget target) => All.Where(c => c.SupportsTarget(target));

    /// <summary>
    /// Attributes every control carries regardless of kind. Held apart from the per-kind properties
    /// so a reader can tell "structural" from "editable property", and so the property grid does not
    /// offer <c>Id</c> beside <c>Text</c>.
    /// </summary>
    public static readonly IReadOnlyList<string> StructuralAttributes = new[]
    {
        "Id", "TabIndex",
        "X", "Y", "Width", "Height", "Anchor", "Dock",   // .blform  (D3)
        "Col", "Row", "ColSpan", "RowSpan"               // .blwebform (D3)
    };

    /// <summary>The layout vocabulary of one format, and only that one (D3).</summary>
    private static readonly IReadOnlyList<string> PixelAttributes = new[]
    {
        "X", "Y", "Width", "Height", "Anchor", "Dock"
    };

    private static readonly IReadOnlyList<string> GridAttributes = new[]
    {
        "Col", "Row", "ColSpan", "RowSpan"
    };

    /// <summary>Structural in EITHER format. Use the target-aware overload when the target is known.</summary>
    public static bool IsStructural(string attributeName) =>
        StructuralAttributes.Any(a => string.Equals(a, attributeName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Structural in <paramref name="target"/>'s own vocabulary.
    ///
    /// <para>⛔ The two vocabularies overlap in spelling and not in meaning, so "structural" is not a
    /// property of the attribute name alone. <c>Width</c> is a <c>.blform</c> control's pixel width
    /// and is read into its geometry; on a <c>.blwebform</c> control nothing reads it, so calling it
    /// structural there would make it neither a property nor an unknown attribute — absent from the
    /// model entirely, and silently dropped by any path that rebuilds the document from the
    /// model.</para>
    /// </summary>
    public static bool IsStructural(string attributeName, FormTarget target)
    {
        if (string.Equals(attributeName, "Id", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(attributeName, "TabIndex", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var vocabulary = target == FormTarget.WinForms ? PixelAttributes : GridAttributes;
        return vocabulary.Any(a => string.Equals(a, attributeName, StringComparison.OrdinalIgnoreCase));
    }
}
