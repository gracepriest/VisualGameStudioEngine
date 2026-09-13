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
public sealed record FormPropertyDef(
    string Name,
    FormPropertyType Type,
    string? Default = null,
    IReadOnlyList<string>? AllowedValues = null)
{
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

    private static readonly FormPropertyDef TextAlign = new(
        "TextAlign", FormPropertyType.Enum, "Left", new[] { "Left", "Center", "Right" });

    private static IReadOnlyList<FormPropertyDef> Common(params FormPropertyDef[] own) =>
        own.Concat(new[] { Enabled, Visible, ForeColor, BackColor }).ToList();

    /// <summary>
    /// The ten kinds of v1. Ten is a deliberate floor, not a ceiling: it is the smallest set that
    /// covers a login form, a settings pane and a list-detail pane — the three shapes the designer
    /// has to handle before it is worth using at all.
    /// </summary>
    public static readonly IReadOnlyList<FormControlDef> All = new List<FormControlDef>
    {
        new("Label",       "Label",       "label",    null,       false, Common(Text, TextAlign)),
        new("TextBox",     "TextBox",     "input",    "text",     false, Common(
            Text,
            new FormPropertyDef("Multiline", FormPropertyType.Bool, "false"),
            new FormPropertyDef("ReadOnly", FormPropertyType.Bool, "false"),
            new FormPropertyDef("MaxLength", FormPropertyType.Int),
            new FormPropertyDef("PasswordChar", FormPropertyType.String))),
        new("Button",      "Button",      "button",   null,       false, Common(Text, TextAlign)),
        new("CheckBox",    "CheckBox",    "input",    "checkbox", false, Common(Text, Checked)),
        new("RadioButton", "RadioButton", "input",    "radio",    false, Common(
            Text,
            Checked,
            // The DOM groups radios by name=; WinForms groups them by container. Modeled
            // explicitly so the two targets can be made to agree rather than diverging silently.
            new FormPropertyDef("GroupName", FormPropertyType.String))),
        new("ComboBox",    "ComboBox",    "select",   null,       false, Common(
            Text,
            new FormPropertyDef("Items", FormPropertyType.String),
            new FormPropertyDef("SelectedIndex", FormPropertyType.Int, "-1"))),
        new("ListBox",     "ListBox",     "select",   null,       false, Common(
            new FormPropertyDef("Items", FormPropertyType.String),
            new FormPropertyDef("SelectedIndex", FormPropertyType.Int, "-1"),
            new FormPropertyDef("MultiSelect", FormPropertyType.Bool, "false"))),
        new("Panel",       "Panel",       "div",      null,       true,  Common(
            new FormPropertyDef("BorderStyle", FormPropertyType.Enum, "None",
                new[] { "None", "FixedSingle", "Fixed3D" }))),
        new("GroupBox",    "GroupBox",    "fieldset", null,       true,  Common(Text)),
        new("PictureBox",  "PictureBox",  "img",      null,       false, Common(
            new FormPropertyDef("Image", FormPropertyType.String),
            new FormPropertyDef("SizeMode", FormPropertyType.Enum, "Normal",
                new[] { "Normal", "StretchImage", "AutoSize", "CenterImage", "Zoom" }))),
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

    public static bool IsStructural(string attributeName) =>
        StructuralAttributes.Any(a => string.Equals(a, attributeName, StringComparison.OrdinalIgnoreCase));
}
