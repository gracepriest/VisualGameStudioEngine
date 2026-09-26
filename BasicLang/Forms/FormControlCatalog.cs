using System.Globalization;

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
    /// <summary>One of <see cref="FormPropertyDef.AllowedValues"/>, or one of its <see cref="FormPropertyDef.Aliases"/>.</summary>
    Enum,
    /// <summary>
    /// <c>75, 23</c> — WinForms' <c>SizeConverter</c> text. Emitted as ONE <c>New Size(w, h)</c>
    /// statement: the fan-in rule, because <c>X.Width = …</c> through a struct return is CS1612.
    /// </summary>
    Size
}

/// <summary>
/// Visual Studio's property-grid groups (spec §2.1). ⚠ Events use their OWN enum,
/// <see cref="FormEventCategory"/> — WinForms files events under Action, Mouse, Key, Property
/// Changed… which are not property groups.
/// </summary>
public enum FormPropertyCategory
{
    Accessibility,
    Appearance,
    Behavior,
    Data,
    Design,
    Focus,
    Layout,
    Misc,
    WindowStyle
}

/// <summary>
/// How a row's value becomes a CSS declaration when it is not the value verbatim (spec §2.1). ONE
/// place owns each conversion — <see cref="FormCss"/> — so the page cannot carry two opinions.
/// </summary>
public enum FormCssConverter
{
    /// <summary>The document value is the CSS value.</summary>
    None,

    /// <summary>A colour: system colours map to CSS system colours; <c>#AARRGGBB</c> becomes <c>rgba()</c>.</summary>
    Color,

    /// <summary>A <c>ContentAlignment</c>: only its horizontal part (<c>…Left</c> → <c>left</c>).</summary>
    ContentAlignmentHorizontal,

    /// <summary><c>Visible=false</c> → <c>display: none</c>; <c>true</c> → no declaration.</summary>
    VisibleToDisplay
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
/// <param name="HtmlAttribute">See <see cref="HtmlAttributeName"/>.</param>
/// <param name="Category">
/// The Visual Studio group this row appears under. ⛔ Required in practice: the parity test compares
/// it with WinForms for every WinForms row, and a completeness test requires it on EVERY row
/// (web-only and <see cref="FormControlCatalog.FormRoot"/> included). Nullable only so a missing one
/// is detectable.
/// </param>
/// <param name="Description">
/// The description-pane text. For a WinForms row, WinForms' own <c>[Description]</c> — the parity
/// test compares it with the snapshot, so it cannot drift from what VS shows.
/// </param>
/// <param name="CssProperty">
/// The CSS property this row becomes on the web (<c>BackColor</c> → <c>background-color</c>), walked
/// generically by <c>FormAssetEmitter</c> exactly as <see cref="HtmlAttribute"/> is. Null when the row
/// has no single-declaration CSS meaning.
/// </param>
/// <param name="CssConverter">How the value becomes the CSS value — see <see cref="FormCssConverter"/>.</param>
/// <param name="WebDefault">
/// The web's default where the browser's differs from WinForms' (spec §2.7). Null = same as
/// <see cref="Default"/>; the EMPTY string = "no static default on the web". Read through
/// <see cref="DefaultFor"/>, never directly.
/// </param>
/// <param name="Aliases">
/// Legacy document values, keyed by the legacy spelling, mapped to a canonical
/// <see cref="AllowedValues"/> member (spec §2.8 — TextAlign's <c>Left</c> → <c>MiddleLeft</c>).
/// ACCEPTED (Canon, round-trips byte-for-byte), EMITTED as the canonical member, never OFFERED.
/// Build it with <c>StringComparer.OrdinalIgnoreCase</c>.
/// </param>
/// <param name="OracleExemption">
/// Why the WinForms snapshot is NOT the truth for this row — a stated reason, carried on the row so
/// the parity test prints it rather than keeping a hand list. Null for every row the snapshot judges.
/// </param>
public sealed record FormPropertyDef(
    string Name,
    FormPropertyType Type,
    string? Default = null,
    IReadOnlyList<string>? AllowedValues = null,
    string? WinFormsEnumType = null,
    IReadOnlyList<FormTarget>? Targets = null,
    string? WinFormsFactory = null,
    bool IsItemCollection = false,
    string? HtmlAttribute = null,
    FormPropertyCategory? Category = null,
    string? Description = null,
    string? CssProperty = null,
    FormCssConverter CssConverter = FormCssConverter.None,
    string? WebDefault = null,
    IReadOnlyDictionary<string, string>? Aliases = null,
    string? OracleExemption = null)
{
    // ⛔ Normalised to OrdinalIgnoreCase whatever comparer the caller built the dictionary with —
    // Accepts and Canonical are case-insensitive for members, and an alias lookup that silently
    // used a different comparer would make `left` Degraded while `Left` is Canon. The init accessor
    // normalises too, so a `with` copy cannot bypass it.
    private readonly IReadOnlyDictionary<string, string>? _aliases = NormaliseAliases(Aliases);

    /// <summary>See the <c>Aliases</c> parameter. Always case-insensitive.</summary>
    public IReadOnlyDictionary<string, string>? Aliases
    {
        get => _aliases;
        init => _aliases = NormaliseAliases(value);
    }

    private static IReadOnlyDictionary<string, string>? NormaliseAliases(IReadOnlyDictionary<string, string>? aliases) =>
        aliases == null
            ? null
            : aliases is Dictionary<string, string> d && ReferenceEquals(d.Comparer, StringComparer.OrdinalIgnoreCase)
                ? d
                // Throws on two keys differing only by case — a table that ambiguous is a catalog bug.
                : new Dictionary<string, string>(aliases, StringComparer.OrdinalIgnoreCase);

    /// <summary>True when this property exists on <paramref name="target"/>.</summary>
    public bool AppliesTo(FormTarget target) => Targets == null || Targets.Contains(target);

    /// <summary>
    /// The HTML attribute this property becomes, or null when the emitter handles it specially (or
    /// not at all).
    ///
    /// <para>⛔ Here rather than in the emitter because the emitter's alternative is a hand-written
    /// <c>if</c> per property — which is a second list beside this table and goes stale the moment a
    /// row is added. WinForms <c>Minimum</c> is HTML <c>min</c>; nothing but the catalog can know
    /// that, and nothing else should have to.</para>
    ///
    /// <para>⚠ Not every web property is a plain attribute. <c>Checked</c>, <c>ReadOnly</c> and
    /// <c>MultiSelect</c> are BOOLEAN attributes whose presence is the value, and <c>Items</c>
    /// becomes child elements — those stay in the emitter, which is where that shape lives.</para>
    /// </summary>
    public string? HtmlAttributeName => HtmlAttribute;

    /// <summary>
    /// The default a user SEES for an absent property on <paramref name="target"/> (spec §2.7). Every
    /// reader of a default goes through here — the grid, the web Timer's <c>{Interval}</c>
    /// placeholder — so the web can differ from WinForms in exactly one place.
    /// </summary>
    public string? DefaultFor(FormTarget target) =>
        target == FormTarget.Web && WebDefault != null
            ? (WebDefault.Length == 0 ? null : WebDefault)
            : Default;

    /// <summary>
    /// The value in this row's canonical spelling: an Enum member as <see cref="AllowedValues"/>
    /// spells it, an <see cref="Aliases"/> key resolved to its member, a Bool in lower case. A value
    /// the row does not know is returned UNCHANGED — Degraded values are preserved, never coerced.
    /// </summary>
    public string Canonical(string value)
    {
        if (Type == FormPropertyType.Enum && AllowedValues != null)
        {
            var member = AllowedValues.FirstOrDefault(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase));
            if (member != null)
            {
                return member;
            }

            if (Aliases != null && Aliases.TryGetValue(value, out var target))
            {
                return target;
            }

            return value;
        }

        if (Type == FormPropertyType.Bool && bool.TryParse(value, out var flag))
        {
            return flag ? "true" : "false";
        }

        // A system colour in the table's spelling, so `control` and `Control` compare equal wherever
        // a caller asks "is this the same value?" (the grid's no-op rule). Other colours unchanged.
        if (Type == FormPropertyType.Color && FormSystemColors.TryCanonical(value, out var system))
        {
            return system;
        }

        return value;
    }

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
            // ⛔ The CANONICAL member: an alias (Left) is emitted as its member (MiddleLeft), never
            // verbatim — ContentAlignment has no Left, and csc would say CS0117.
            return $"{WinFormsEnumType}.{Canonical(value)}";
        }

        if (WinFormsFactory != null)
        {
            return $"{WinFormsFactory}(\"{value.Replace("\"", "\"\"")}\")";
        }

        return Type switch
        {
            FormPropertyType.Color => ColorLiteral(value),
            FormPropertyType.Size => TryParseSize(value, out var w, out var h) ? SizeLiteral(w, h) : null,
            _ => null
        };
    }

    // ⛔ INVARIANT formatting, not interpolation: under sv-SE an int formats its minus as U+2212, and
    // `New Size(−5, 10)` is CS1056 at csc.
    private static string SizeLiteral(int width, int height) =>
        string.Create(CultureInfo.InvariantCulture, $"New Size({width}, {height})");

    /// <summary><c>w, h</c> — WinForms' SizeConverter text, culture-invariant. Exactly two integers.</summary>
    public static bool TryParseSize(string value, out int width, out int height)
    {
        width = height = 0;
        var parts = value.Split(',');
        return parts.Length == 2 &&
               int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out width) &&
               int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out height);
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
        // ⛔ A system colour is NOT a Color member: `Color.Control` does not exist (CS0117 at csc,
        // BasicLang silent). It is SystemColors.Control — spec §2.2, and a live defect before this.
        if (FormSystemColors.TryCanonical(value, out var system))
        {
            return "SystemColors." + system;
        }

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

    /// <summary>
    /// True when <paramref name="value"/> is already this property's WinForms SOURCE form rather
    /// than a document value — the two conventions that share one <c>Properties</c> dictionary.
    ///
    /// <para>⛔⛔ <b>Ask the catalog, never the shape of the string.</b> A shape test
    /// ("does it look like <c>Type.Member</c>?") was tried and was wrong in both directions: it
    /// made <c>Text="config.json"</c> emit as the bare identifier <c>config.json</c>, and it waved
    /// <c>TextAlign="ContentAlignment.Bogus"</c> straight through the Degraded check into CS0117 —
    /// the exact failure that check exists to stop. Only the row knows its own enum type and its
    /// own members, so only the row can tell a value it could have WRITTEN from one it must
    /// QUOTE.</para>
    ///
    /// <para>An Enum value qualifies only when it is, exactly, what <see cref="WinFormsLiteral"/>
    /// would produce for one of this row's <see cref="AllowedValues"/>. A member the catalog does
    /// not list is Degraded even though the real enum may have it: the catalog is the single source
    /// of truth, so the fix for a missing member is a catalog row, not a value spliced in
    /// unchecked.</para>
    /// </summary>
    public bool IsSourceForm(string value) => Type switch
    {
        FormPropertyType.Enum =>
            WinFormsEnumType != null && AllowedValues != null &&
            AllowedValues.Any(v => string.Equals(WinFormsLiteral(v), value, StringComparison.Ordinal)),

        // `Color.Red` / `Color.FromArgb(...)`. The 140-odd KnownColor names are not enumerated here
        // (see IsColor), so a Color member cannot be checked the way an enum is. A SystemColors
        // member CAN — the table has every one — so it is checked exactly (ordinal): a
        // `SystemColors.Bogus` spliced in as source is CS0117 at csc, BasicLang silent.
        FormPropertyType.Color =>
            value.StartsWith("SystemColors.", StringComparison.Ordinal)
                ? IsSystemColorsSource(value)
                : value.StartsWith("Color.", StringComparison.Ordinal) ||
                  value.StartsWith("New ", StringComparison.Ordinal),

        // Exactly `New Size(w, h)` around two integers — never a prefix match, which would pass
        // `New Size(1, 2) + junk` straight into the generated source.
        FormPropertyType.Size =>
            value.StartsWith("New Size(", StringComparison.Ordinal) &&
            value.EndsWith(")", StringComparison.Ordinal) &&
            TryParseSize(value.Substring("New Size(".Length, value.Length - "New Size(".Length - 1), out _, out _),

        // A string arrives from the document unquoted, so quotes mean it is already source.
        FormPropertyType.String =>
            value.StartsWith("\"", StringComparison.Ordinal) ||
            value.StartsWith("New ", StringComparison.Ordinal),

        // An Int or a Bool has no source form that differs from its document text.
        _ => false
    };

    /// <summary>True when <paramref name="value"/> parses to this property's declared type on SOME target.</summary>
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
            // "#rrggbb", "#rgb", "#aarrggbb", or a bare name the target resolves (KnownColor / system / CSS).
            FormPropertyType.Color => IsColor(value),
            FormPropertyType.Enum => AllowedValues != null &&
                                     (AllowedValues.Any(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase)) ||
                                      Aliases?.ContainsKey(value) == true),
            FormPropertyType.Size => TryParseSize(value, out _, out _),
            _ => false
        };
    }

    /// <summary>
    /// True when <paramref name="value"/> is usable on <paramref name="target"/>. Stricter than
    /// <see cref="Accepts(string?)"/> in one place today: a Windows system colour with no CSS
    /// equivalent is WinForms-only as a VALUE (spec §2.2) — Degraded on a web form, with a reason.
    /// </summary>
    public bool Accepts(string? value, FormTarget target) =>
        Accepts(value) && !IsSystemColourRefusedOn(value!, target);

    /// <summary>
    /// Why <paramref name="value"/> is not usable on <paramref name="target"/> — the Degraded reason.
    ///
    /// <para>⛔ Asks the SAME predicate as <see cref="Accepts(string?, FormTarget)"/>, so the reason
    /// can never describe a refusal that check did not make. Called for a value that IS usable there
    /// is nothing to describe, and inventing a reason would put a false one in front of the user — so
    /// it throws.</para>
    /// </summary>
    public string DescribeRefusal(string value, FormTarget target)
    {
        if (Accepts(value, target))
        {
            throw new ArgumentException(
                $"'{value}' is usable for {Name} on {target}; there is no refusal to describe.", nameof(value));
        }

        if (IsSystemColourRefusedOn(value, target))
        {
            FormSystemColors.TryCanonical(value, out var system);
            return $"'{value}' is the Windows system colour {system}, which has no CSS equivalent, so a " +
                   "web form cannot use it. The value is preserved exactly as written.";
        }

        return $"'{value}' is not a valid {Type}" +
               (AllowedValues is { Count: > 0 } ? $" (expected one of: {string.Join(", ", AllowedValues)})" : "") +
               ". The value is preserved exactly as written.";
    }

    /// <summary>
    /// THE target-specific refusal — the one predicate <see cref="Accepts(string?, FormTarget)"/> and
    /// <see cref="DescribeRefusal"/> share. Only a COLOUR row can refuse a system colour: a String
    /// caption reading "Window" is text, and an Enum member named "Menu" is that enum's business.
    /// </summary>
    private bool IsSystemColourRefusedOn(string value, FormTarget target) =>
        Type == FormPropertyType.Color && target == FormTarget.Web &&
        FormSystemColors.TryCanonical(value, out var system) && FormSystemColors.CssFor(system) == null;

    /// <summary><c>SystemColors.X</c> where X is, exactly and case-sensitively, a member the table names.</summary>
    private static bool IsSystemColorsSource(string value)
    {
        var member = value.Substring("SystemColors.".Length);
        return FormSystemColors.TryCanonical(member, out var canonical) &&
               string.Equals(member, canonical, StringComparison.Ordinal);
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
/// <param name="WinFormsType">
/// The WinForms type name, or null if web-only. Unqualified for a control (<c>Button</c>);
/// <b>fully qualified for a component</b> (<c>System.Windows.Forms.Timer</c>), and that is
/// measured, not stylistic: the C# backend adds <c>using System.Threading;</c> whenever the
/// generated body contains the substring <c>Thread</c>, which makes a bare <c>Timer</c> CS0104,
/// and the scaffold never imports <c>System.ComponentModel</c>, which makes a bare
/// <c>BackgroundWorker</c> CS0246 — BasicLang silent on both.
/// </param>
/// <param name="HtmlTag">HTML element the web emitter produces, or null if WinForms-only.</param>
/// <param name="HtmlInputType">
/// <c>type=</c> for an <c>&lt;input&gt;</c>, e.g. <c>checkbox</c>. Null when <see cref="HtmlTag"/>
/// is not <c>input</c>.
/// </param>
/// <param name="IsContainer">True when the control may hold child controls (Panel, GroupBox).</param>
/// <param name="Properties">Editable properties, in the order a property grid should show them.</param>
/// <param name="DefaultWidth">Width in form pixels given to one dropped from the toolbox.</param>
/// <param name="DefaultHeight">Height in form pixels given to one dropped from the toolbox.</param>
/// <summary>
/// How the designer canvas DRAWS a control kind.
///
/// <para>⛔ Still a schematic, never a preview (D-WYSIWYG): the IDE has no browser and no WinForms
/// surface, so none of these shapes claims to be what the running program looks like. What they do
/// is tell the kinds APART. Drawing every control as the same rectangle made a form of ten controls
/// unreadable — the user could only identify one by reading its label, and a Button, a CheckBox and
/// a TextBox were pixel-identical.</para>
///
/// <para>⛔ Declared HERE rather than switched on in the canvas, for the reason the whole catalog
/// exists: a <c>switch</c> over kinds inside <c>FormCanvasControl</c> is a second list of controls,
/// and it silently falls to its default the day someone adds a row. A new row picks its shape or
/// gets <see cref="Input"/>; it can never go missing.</para>
/// </summary>
public enum FormSchematic
{
    /// <summary>A plain bordered box. The fallback, and right for anything text-entry shaped.</summary>
    Input,

    /// <summary>Text with no box at all — a Label is not a widget, it is words on the form.</summary>
    Text,

    /// <summary>A rounded box with a centred caption.</summary>
    Button,

    /// <summary>A small square to the left, label beside it. No box around the whole bounds.</summary>
    Check,

    /// <summary>A small circle to the left, label beside it.</summary>
    Radio,

    /// <summary>A box with a chevron at the right edge.</summary>
    Dropdown,

    /// <summary>A box with horizontal rules, suggesting rows.</summary>
    List,

    /// <summary>A dashed border — it holds other controls and has no face of its own.</summary>
    Container,

    /// <summary>A border broken at the top left by its caption.</summary>
    Group,

    /// <summary>A box crossed corner to corner, the universal "picture goes here".</summary>
    Image,

    // ==================================================================
    // Task 23's widening. ⛔ Each of these MUST draw differently from every other shape —
    // FormCanvasRenderTests.EveryControlKindRendersDistinctly hashes a frame per kind over identical
    // geometry and an identical id, so two schematics that merely differ in intent collide and fail.
    // That guard is why adding a kind cannot quietly produce another plain box.
    // ==================================================================

    /// <summary>Underlined text, no box — a LinkLabel is a Label that looks clickable.</summary>
    Link,

    /// <summary>Rows, each with a small tick box at its left edge.</summary>
    CheckList,

    /// <summary>A box with a stacked up/down arrow pair at the right edge.</summary>
    Spinner,

    /// <summary>A box with a small calendar grid at the right edge.</summary>
    DatePicker,

    /// <summary>A horizontal groove with a raised thumb and tick marks below it.</summary>
    Slider,

    /// <summary>A sunken trough filled part-way with segmented blocks.</summary>
    Progress,

    /// <summary>A header band across the top with column dividers, and rows beneath.</summary>
    ListDetail,

    /// <summary>Indented rows, each with a small expander box at its left.</summary>
    Tree,

    /// <summary>A full lattice of cells, with a header row and a row-selector column.</summary>
    DataGrid,

    /// <summary>A tab strip along the top with one raised tab, and a body below it.</summary>
    Tabs,

    /// <summary>Two panes divided by a raised splitter bar.</summary>
    Split,

    /// <summary>A container whose contents flow — marked with chevrons along the top edge.</summary>
    FlowContainer,

    /// <summary>A container ruled into cells, so its layout is visible when it is empty.</summary>
    TableContainer,

    // ==================================================================
    // Task 25's tray components. NEVER drawn on the canvas — a component has no position — so these
    // members name the tray and toolbox GLYPH only, one per kind, as every control kind has its own:
    // four Timers in a tray must not look like four of the same thing. FormCanvasRenderTests excludes
    // IsComponent rows and pins that Layout never yields bounds for one.
    // ==================================================================

    /// <summary>A Timer: a clock face.</summary>
    Clock,

    /// <summary>A ToolTip: a hint bubble.</summary>
    Hint,

    /// <summary>An ErrorProvider: the alert badge it puts beside a control.</summary>
    Alert,

    /// <summary>A BackgroundWorker: work off the UI thread.</summary>
    Worker,

    // ==================================================================
    // Task 24 — menus, toolbars and status bars. Declared in commit 24b, BEFORE any row uses one
    // (spec Decision 12), so the per-VALUE pin exists before there is anything to draw: the canvas
    // routes every kind through ONE seam and FormSchematicPinTests hashes a frame per value at one
    // bounds with one label, ALL PAIRWISE DISTINCT. The row-driven gate cannot give that — an item
    // schematic has no document of its own to render (§7), so nothing else would notice a MenuItem
    // and a Separator painting the same pixels.
    //
    // ⛔ The three BAND values draw NO caption: a strip is chrome, and its id painted across the
    //   band would be the only pixel a render gate ever sees.
    // ==================================================================

    /// <summary>A menu strip: a flat band across the top with a rule along its bottom edge.</summary>
    MenuBar,

    /// <summary>A tool strip: a band with the drag grip at its left edge.</summary>
    ToolBar,

    /// <summary>A status strip: a band with a rule along its top edge and a sizing grip at its right.</summary>
    StatusBar,

    /// <summary>A menu item: a client-coloured cell behind its caption, no border — a menu is not a widget.</summary>
    MenuItem,

    /// <summary>A separator: one rule, across or down, whichever way its cell runs.</summary>
    Separator,

    /// <summary>A tool button: a small raised box with its caption, inset inside its cell.</summary>
    ToolButton,

    /// <summary>A status panel: its caption centred, behind the divider rule that starts the panel.</summary>
    StatusLabel
}

/// <summary>
/// The SHAPE of a catalog row — what a control of this kind is, on the canvas and in the code (Task 24,
/// spec §1). One enum rather than a third bool: every site that once asked <c>IsComponent</c> must
/// decide what a Docked strip and an Item are, and a switch without a default is what forces it.
/// </summary>
public enum FormPlace
{
    /// <summary>Pixels or a cell, a TabIndex, children if <c>IsContainer</c>; added with <c>Controls.Add</c>, reversed.</summary>
    Positioned,

    /// <summary>The tray (Task 25): no geometry, no TabIndex, no children, in <c>FormDocument.Components</c>.</summary>
    Tray,

    /// <summary>A strip docked to the form: no geometry, a <c>Dock</c> PROPERTY, items under its rule, in <c>Controls</c>.</summary>
    Docked,

    /// <summary>A strip item: no geometry, no TabIndex, in its host's <c>Children</c>, added by the HOST row's verb in document order.</summary>
    Item
}

/// <summary>
/// What a host row holds and how it adds one (spec §1). <paramref name="Kinds"/>[0] is the default kind
/// Type Here creates; <paramref name="Add"/> is a template with <c>{parent}</c> and <c>{child}</c>,
/// emitted in DOCUMENT order (items are ordered, not layered). On the PARENT's row, because the same
/// ToolStripMenuItem is <c>Items.Add</c>ed under a strip and <c>DropDownItems.Add</c>ed under a menu item.
/// </summary>
public sealed record FormItemRule(IReadOnlyList<string> Kinds, string Add)
{
    public bool Accepts(string kind) => Kinds.Any(k => string.Equals(k, kind, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// How a script-backed component is built on the web — a kind that is not an element. A Timer is a
/// <c>setInterval</c> handle (measured 2026-09-19 under node), not a tag.
/// </summary>
/// <param name="FieldType">The BasicLang type of the generated field, e.g. <c>Integer</c> for a handle.</param>
/// <param name="Construct">
/// A template for the construct statement's right-hand side. Two placeholder kinds:
/// <c>{handler}</c> — the default-event bind's Sub — and <c>{PropertyName}</c> — that property's
/// document value, or the catalog default when unset. It may name <c>w</c>, the typed
/// <c>Window</c> the init region declares once whenever any script component exists.
///
/// <para>⛔ Emitted only when the default-event bind exists: <c>setInterval</c> needs a callback,
/// and a component with no handler does nothing visible on either target. The field is declared
/// regardless.</para>
/// </param>
/// <param name="Implies">
/// What constructing this script MEANS in WinForms' vocabulary — a web Timer runs the moment it is
/// wired, and WinForms says that with <c>Enabled=True</c>. Stated on the row so the retarget can
/// cross "wired means running" by rule in both directions and name it (BL8027), rather than switch
/// on the kind or let a Timer silently stop on the window or silently start on the page.
/// </param>
public sealed record FormWebScript(string FieldType, string Construct, FormImpliedProperty? Implies = null);

/// <summary>A property and the value the other target's construct implies for it — see <see cref="FormWebScript.Implies"/>.</summary>
public sealed record FormImpliedProperty(string Name, string Value);

/// <param name="WinFormsEventArgs">
/// The <c>e</c> type of the default event's handler, qualified; null means <c>EventArgs</c>. A
/// <c>BackgroundWorker.DoWork</c> handler declared with <c>EventArgs</c> compiles by contravariance
/// but cannot reach <c>e.Argument</c>; the typed stub is the useful one.
/// </param>
/// <param name="WebHandlerTakesEvent">
/// False when the web callback is a plain <c>Action</c> rather than an <c>Action(Of DomEvent)</c>.
/// Measured: the typed <c>Window.setInterval</c> REFUSES a <c>(e As DomEvent)</c> handler
/// ("cannot convert from 'Action&lt;DomEvent&gt;' to 'Action'"), so a Timer's stub is parameterless.
/// </param>
/// <param name="WebScript">How a script-backed component is built on the web; null for elements.</param>
/// <param name="Place">
/// The row's SHAPE — see <see cref="FormPlace"/>. It replaces the old <c>IsComponent</c> bool, which
/// survives as a derived property: a strip is "no geometry BUT children", which neither
/// <see cref="IsContainer"/> nor a tray flag could express, and a third bool would have let every
/// consumer keep two of the three answers.
/// </param>
/// <param name="Items">
/// On a HOST row (a strip, or a menu item that drops down), which item kinds it holds and the verb
/// that adds one. Null on every other row — <see cref="IsHost"/> is exactly this being non-null.
/// </param>
/// <param name="FormProperty">
/// A property of the FORM this row's first instance is assigned to, e.g. <c>MainMenuStrip</c> for a
/// MenuStrip. Null when the row is only ever a child. Stated on the row so the region writer emits it
/// by rule rather than switching on the kind.
/// </param>
/// <param name="HtmlChildrenWrapper">
/// An element wrapping this row's CHILDREN on the web, e.g. <c>ul</c> — the children are list items,
/// and the wrapper is not the control's own tag.
/// </param>
/// <param name="HtmlRole">
/// A fixed ARIA <c>role</c> attribute for the emitted element, e.g. <c>toolbar</c>, <c>status</c>,
/// <c>separator</c>. Chrome is the one part of a form whose meaning the DOM cannot infer from its tag.
/// </param>
/// <param name="WebCss">
/// A stylesheet block appended ONCE per kind present on the page — the horizontal bar, the hidden
/// submenu shown on hover. Per KIND, not per control: two menus must not emit the rules twice.
/// </param>
public sealed record FormControlDef(
    string Kind,
    string? WinFormsType,
    string? HtmlTag,
    string? HtmlInputType,
    bool IsContainer,
    IReadOnlyList<FormPropertyDef> Properties,
    int DefaultWidth = 100,
    int DefaultHeight = 24,
    FormSchematic Schematic = FormSchematic.Input,
    string? WinFormsEvent = null,
    string? WebEvent = null,
    string? WinFormsEventArgs = null,
    bool WebHandlerTakesEvent = true,
    FormWebScript? WebScript = null,
    FormPlace Place = FormPlace.Positioned,
    FormItemRule? Items = null,
    string? FormProperty = null,
    string? HtmlChildrenWrapper = null,
    string? HtmlRole = null,
    string? WebCss = null)
{
    /// <summary>Task 25's flag, now derived: the eleven sites that read it keep reading it.</summary>
    public bool IsComponent => Place == FormPlace.Tray;

    /// <summary>A strip or a menu item — something whose children are items.</summary>
    public bool IsHost => Items != null;

    /// <summary>
    /// Whether the kind exists on <paramref name="target"/>. On the web an element has a tag and a
    /// script component has a <see cref="WebScript"/>; a kind with neither is honestly absent.
    /// </summary>
    public bool SupportsTarget(FormTarget target) => target switch
    {
        FormTarget.WinForms => WinFormsType != null,
        FormTarget.Web => HtmlTag != null || WebScript != null,
        _ => false
    };

    /// <summary>
    /// The event a double-click on this control means, in the TARGET's vocabulary (D8) — the
    /// WinForms <c>Click</c> against the DOM's <c>click</c>, and genuinely different events where the
    /// two platforms disagree: a <c>TextBox</c> raises <c>TextChanged</c> and an <c>&lt;input&gt;</c>
    /// fires <c>input</c>.
    ///
    /// <para>⛔ Deliberately NOT defaulted to Click. A row that forgets to declare its event returns
    /// null here and the gesture refuses by name, which someone fixes; a silent default would give a
    /// new control kind a Click handler that is wrong for most of the catalog and wrong invisibly —
    /// the same "widen the default" failure this table exists to prevent.</para>
    /// </summary>
    public string? DefaultEvent(FormTarget target) => target switch
    {
        FormTarget.WinForms => WinFormsEvent,
        FormTarget.Web => WebEvent,
        _ => null
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
    //
    // ⚠ INTERIM (slice 1 Task 2): the canonical members are the three Middle* ones and the old
    // Left/Center/Right are ALIASES, so emission is unchanged (Left → ContentAlignment.MiddleLeft).
    // Task 5 replaces this with per-row TextAlign over all nine ContentAlignment members.
    private static readonly FormPropertyDef TextAlign = new(
        "TextAlign", FormPropertyType.Enum, "MiddleLeft", new[] { "MiddleLeft", "MiddleCenter", "MiddleRight" },
        WinFormsEnumType: "ContentAlignment",
        Aliases: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
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
        new("Label",       "Label",       "label",    null,       false, Common(Text, TextAlign),
            DefaultWidth: 100, DefaultHeight: 23, Schematic: FormSchematic.Text,
            WinFormsEvent: "Click", WebEvent: "click"),
        new("TextBox",     "TextBox",     "input",    "text",     false, Common(
            Text,
            new FormPropertyDef("Multiline", FormPropertyType.Bool, "false"),
            new FormPropertyDef("ReadOnly", FormPropertyType.Bool, "false"),
            new FormPropertyDef("MaxLength", FormPropertyType.Int, HtmlAttribute: "maxlength"),
            // WinForms PasswordChar is a char, not a string — assigning one is CS0029.
            new FormPropertyDef("PasswordChar", FormPropertyType.String,
                WinFormsFactory: "Convert.ToChar")),
            DefaultWidth: 100, DefaultHeight: 23,
            // ⚠ The DOM has no TextChanged. `input` fires per keystroke, which is what TextChanged
            // means; `change` fires on blur and would be a different gesture wearing the same name.
            WinFormsEvent: "TextChanged", WebEvent: "input"),
        new("Button",      "Button",      "button",   null,       false, Common(Text, TextAlign),
            DefaultWidth: 75, DefaultHeight: 23, Schematic: FormSchematic.Button,
            WinFormsEvent: "Click", WebEvent: "click"),
        new("CheckBox",    "CheckBox",    "input",    "checkbox", false, Common(Text, Checked),
            DefaultWidth: 104, DefaultHeight: 24, Schematic: FormSchematic.Check,
            WinFormsEvent: "CheckedChanged", WebEvent: "change"),
        new("RadioButton", "RadioButton", "input",    "radio",    false, Common(
            Text,
            Checked,
            // ⛔ WEB ONLY. The DOM groups radios by name=; WinForms groups them by CONTAINER and
            // has no GroupName property at all — csc says CS1061, BasicLang says nothing.
            new FormPropertyDef("GroupName", FormPropertyType.String,
                Targets: new[] { FormTarget.Web })),
            DefaultWidth: 104, DefaultHeight: 24, Schematic: FormSchematic.Radio,
            WinFormsEvent: "CheckedChanged", WebEvent: "change"),
        new("ComboBox",    "ComboBox",    "select",   null,       false, Common(
            Text,
            // Items is a get-only collection on WinForms — assigning it is CS0200.
            new FormPropertyDef("Items", FormPropertyType.String, IsItemCollection: true),
            new FormPropertyDef("SelectedIndex", FormPropertyType.Int, "-1")),
            DefaultWidth: 121, DefaultHeight: 23, Schematic: FormSchematic.Dropdown,
            WinFormsEvent: "SelectedIndexChanged", WebEvent: "change"),
        new("ListBox",     "ListBox",     "select",   null,       false, Common(
            new FormPropertyDef("Items", FormPropertyType.String, IsItemCollection: true),
            new FormPropertyDef("SelectedIndex", FormPropertyType.Int, "-1"),
            // ⛔ WEB ONLY. WinForms ListBox has no MultiSelect — it has SelectionMode, an enum.
            // Mapping a Bool onto it is a design decision v1 has not made, so the property stays
            // web-only rather than being silently approximated.
            new FormPropertyDef("MultiSelect", FormPropertyType.Bool, "false",
                Targets: new[] { FormTarget.Web })),
            DefaultWidth: 120, DefaultHeight: 95, Schematic: FormSchematic.List,
            WinFormsEvent: "SelectedIndexChanged", WebEvent: "change"),
        new("Panel",       "Panel",       "div",      null,       true,  Common(
            new FormPropertyDef("BorderStyle", FormPropertyType.Enum, "None",
                new[] { "None", "FixedSingle", "Fixed3D" },
                WinFormsEnumType: "BorderStyle")),
            DefaultWidth: 200, DefaultHeight: 100, Schematic: FormSchematic.Container,
            // ⚠ VS opens a Panel on Paint. That handler takes a PaintEventArgs and is for drawing,
            // not for a gesture — Click is the event a double-click in THIS designer can honestly
            // stub, and the Events tab (Task 23) is where the rest will be reachable.
            WinFormsEvent: "Click", WebEvent: "click"),
        new("GroupBox",    "GroupBox",    "fieldset", null,       true,  Common(Text),
            DefaultWidth: 200, DefaultHeight: 100, Schematic: FormSchematic.Group,
            WinFormsEvent: "Click", WebEvent: "click"),
        new("PictureBox",  "PictureBox",  "img",      null,       false, Common(
            // WinForms Image is a System.Drawing.Image, not a path string (CS0029).
            new FormPropertyDef("Image", FormPropertyType.String,
                WinFormsFactory: "Image.FromFile"),
            new FormPropertyDef("SizeMode", FormPropertyType.Enum, "Normal",
                new[] { "Normal", "StretchImage", "AutoSize", "CenterImage", "Zoom" },
                WinFormsEnumType: "PictureBoxSizeMode")),
            DefaultWidth: 100, DefaultHeight: 50, Schematic: FormSchematic.Image,
            WinFormsEvent: "Click", WebEvent: "click"),

        // ==================================================================
        // Task 23 — the rest of the common-controls tier.
        //
        // ⛔ Every property below is UNFALSIFIABLE except through csc. A misspelling types as
        // Object and compiles green, so nothing here is trustworthy until
        // WinFormsCatalogSweepTests has generated it and the real compiler has accepted it.
        // ==================================================================

        // ⚠ A LinkLabel IS a Label that looks clickable, and <a> is the honest tag. LinkColor and
        // friends are deliberately omitted: they are Color properties whose WinForms defaults are
        // system colours, and a designer that wrote them out would freeze today's theme into the
        // form.
        new("LinkLabel",   "LinkLabel",   "a",        null,       false, Common(Text, TextAlign),
            DefaultWidth: 100, DefaultHeight: 23, Schematic: FormSchematic.Link,
            WinFormsEvent: "LinkClicked", WebEvent: "click"),

        new("NumericUpDown", "NumericUpDown", "input", "number",  false, Common(
            // ⛔ These are DECIMAL on WinForms. An Int literal widens implicitly, so the catalog
            // models them as Int and the emitted `n.Minimum = 0` compiles — but a decimal default
            // written as "0.00" would not round-trip through Int, which is why the defaults are
            // whole numbers.
            new FormPropertyDef("Minimum", FormPropertyType.Int, "0", HtmlAttribute: "min"),
            new FormPropertyDef("Maximum", FormPropertyType.Int, "100", HtmlAttribute: "max"),
            new FormPropertyDef("Value", FormPropertyType.Int, "0", HtmlAttribute: "value"),
            new FormPropertyDef("Increment", FormPropertyType.Int, "1", HtmlAttribute: "step"),
            // ⛔ WinForms only: <input type="number"> has no decimal-places concept, it has step.
            new FormPropertyDef("DecimalPlaces", FormPropertyType.Int, "0",
                Targets: new[] { FormTarget.WinForms })),
            DefaultWidth: 120, DefaultHeight: 23, Schematic: FormSchematic.Spinner,
            WinFormsEvent: "ValueChanged", WebEvent: "input"),

        new("DateTimePicker", "DateTimePicker", "input", "date",  false, Common(
            // ⛔ All WinForms-only. <input type="date"> renders per the user's locale and has no
            // format control at all, so emitting these to the web would be describing a behaviour
            // the page cannot have.
            new FormPropertyDef("Format", FormPropertyType.Enum, "Long",
                new[] { "Long", "Short", "Time", "Custom" },
                WinFormsEnumType: "DateTimePickerFormat",
                Targets: new[] { FormTarget.WinForms }),
            new FormPropertyDef("CustomFormat", FormPropertyType.String,
                Targets: new[] { FormTarget.WinForms }),
            new FormPropertyDef("ShowUpDown", FormPropertyType.Bool, "false",
                Targets: new[] { FormTarget.WinForms })),
            DefaultWidth: 200, DefaultHeight: 23, Schematic: FormSchematic.DatePicker,
            WinFormsEvent: "ValueChanged", WebEvent: "change"),

        new("TrackBar",    "TrackBar",    "input",    "range",    false, Common(
            new FormPropertyDef("Minimum", FormPropertyType.Int, "0", HtmlAttribute: "min"),
            new FormPropertyDef("Maximum", FormPropertyType.Int, "10", HtmlAttribute: "max"),
            new FormPropertyDef("Value", FormPropertyType.Int, "0", HtmlAttribute: "value"),
            new FormPropertyDef("TickFrequency", FormPropertyType.Int, "1",
                Targets: new[] { FormTarget.WinForms }),
            new FormPropertyDef("Orientation", FormPropertyType.Enum, "Horizontal",
                new[] { "Horizontal", "Vertical" },
                WinFormsEnumType: "Orientation",
                Targets: new[] { FormTarget.WinForms })),
            DefaultWidth: 150, DefaultHeight: 45, Schematic: FormSchematic.Slider,
            WinFormsEvent: "ValueChanged", WebEvent: "input"),

        new("ProgressBar", "ProgressBar", "progress", null,       false, Common(
            new FormPropertyDef("Minimum", FormPropertyType.Int, "0",
                Targets: new[] { FormTarget.WinForms }),
            new FormPropertyDef("Maximum", FormPropertyType.Int, "100", HtmlAttribute: "max"),
            new FormPropertyDef("Value", FormPropertyType.Int, "0", HtmlAttribute: "value"),
            new FormPropertyDef("Style", FormPropertyType.Enum, "Blocks",
                new[] { "Blocks", "Continuous", "Marquee" },
                WinFormsEnumType: "ProgressBarStyle",
                Targets: new[] { FormTarget.WinForms })),
            DefaultWidth: 150, DefaultHeight: 23, Schematic: FormSchematic.Progress,
            // ⚠ A ProgressBar reports; it does not notify. Click is what Control gives it and the
            // only thing a double-click here can honestly stub.
            WinFormsEvent: "Click", WebEvent: "click"),

        // ==================================================================
        // ⛔⛔ WinForms-ONLY, by decision rather than omission. None of these has a single honest
        // HTML tag: a DataGridView is not a <table>, a TabControl needs script the designer does
        // not write, and a SplitContainer is a CSS layout rather than an element. Emitting a
        // <div> for them would produce a page that silently is not the control the user drew.
        // FormCatalogCoverageTests pins this, and Task 21's retarget reports it as explicit loss.
        // ==================================================================

        new("CheckedListBox", "CheckedListBox", null, null,       false, Common(
            new FormPropertyDef("Items", FormPropertyType.String, IsItemCollection: true),
            new FormPropertyDef("SelectedIndex", FormPropertyType.Int, "-1"),
            new FormPropertyDef("CheckOnClick", FormPropertyType.Bool, "false")),
            DefaultWidth: 160, DefaultHeight: 95, Schematic: FormSchematic.CheckList,
            WinFormsEvent: "SelectedIndexChanged"),

        new("ListView",    "ListView",    null,       null,       false, Common(
            new FormPropertyDef("View", FormPropertyType.Enum, "LargeIcon",
                new[] { "LargeIcon", "Details", "SmallIcon", "List", "Tile" },
                WinFormsEnumType: "View"),
            new FormPropertyDef("FullRowSelect", FormPropertyType.Bool, "false"),
            new FormPropertyDef("GridLines", FormPropertyType.Bool, "false"),
            new FormPropertyDef("MultiSelect", FormPropertyType.Bool, "true")),
            DefaultWidth: 240, DefaultHeight: 120, Schematic: FormSchematic.ListDetail,
            WinFormsEvent: "SelectedIndexChanged"),

        new("TreeView",    "TreeView",    null,       null,       false, Common(
            new FormPropertyDef("ShowLines", FormPropertyType.Bool, "true"),
            new FormPropertyDef("ShowRootLines", FormPropertyType.Bool, "true"),
            new FormPropertyDef("HideSelection", FormPropertyType.Bool, "true"),
            new FormPropertyDef("Indent", FormPropertyType.Int, "19")),
            DefaultWidth: 180, DefaultHeight: 140, Schematic: FormSchematic.Tree,
            WinFormsEvent: "AfterSelect"),

        new("DataGridView", "DataGridView", null,     null,       false, Common(
            new FormPropertyDef("AllowUserToAddRows", FormPropertyType.Bool, "true"),
            new FormPropertyDef("AllowUserToDeleteRows", FormPropertyType.Bool, "true"),
            new FormPropertyDef("ReadOnly", FormPropertyType.Bool, "false"),
            new FormPropertyDef("RowHeadersVisible", FormPropertyType.Bool, "true"),
            new FormPropertyDef("ColumnHeadersVisible", FormPropertyType.Bool, "true")),
            DefaultWidth: 280, DefaultHeight: 150, Schematic: FormSchematic.DataGrid,
            WinFormsEvent: "CellClick"),

        new("TabControl",  "TabControl",  null,       null,       true,  Common(
            new FormPropertyDef("Alignment", FormPropertyType.Enum, "Top",
                new[] { "Top", "Bottom", "Left", "Right" },
                WinFormsEnumType: "TabAlignment"),
            new FormPropertyDef("Multiline", FormPropertyType.Bool, "false"),
            new FormPropertyDef("SelectedIndex", FormPropertyType.Int, "-1")),
            DefaultWidth: 240, DefaultHeight: 160, Schematic: FormSchematic.Tabs,
            WinFormsEvent: "SelectedIndexChanged"),

        new("SplitContainer", "SplitContainer", null, null,       true,  Common(
            new FormPropertyDef("Orientation", FormPropertyType.Enum, "Vertical",
                new[] { "Horizontal", "Vertical" },
                WinFormsEnumType: "Orientation"),
            new FormPropertyDef("SplitterDistance", FormPropertyType.Int, "80"),
            new FormPropertyDef("SplitterWidth", FormPropertyType.Int, "4"),
            new FormPropertyDef("IsSplitterFixed", FormPropertyType.Bool, "false")),
            DefaultWidth: 260, DefaultHeight: 140, Schematic: FormSchematic.Split,
            WinFormsEvent: "SplitterMoved"),

        new("FlowLayoutPanel", "FlowLayoutPanel", null, null,     true,  Common(
            new FormPropertyDef("FlowDirection", FormPropertyType.Enum, "LeftToRight",
                new[] { "LeftToRight", "TopDown", "RightToLeft", "BottomUp" },
                WinFormsEnumType: "FlowDirection"),
            new FormPropertyDef("WrapContents", FormPropertyType.Bool, "true"),
            new FormPropertyDef("AutoScroll", FormPropertyType.Bool, "false")),
            DefaultWidth: 220, DefaultHeight: 120, Schematic: FormSchematic.FlowContainer,
            WinFormsEvent: "Click"),

        new("TableLayoutPanel", "TableLayoutPanel", null, null,   true,  Common(
            new FormPropertyDef("ColumnCount", FormPropertyType.Int, "2"),
            new FormPropertyDef("RowCount", FormPropertyType.Int, "2"),
            new FormPropertyDef("CellBorderStyle", FormPropertyType.Enum, "None",
                new[] { "None", "Single", "Inset", "Outset" },
                WinFormsEnumType: "TableLayoutPanelCellBorderStyle")),
            DefaultWidth: 220, DefaultHeight: 120, Schematic: FormSchematic.TableContainer,
            WinFormsEvent: "Click"),

        // ==================================================================
        // Task 25 — the component tray. Non-visual: no place on the canvas, no Controls.Add.
        //
        // ⛔ No Common(): Visible, ForeColor and BackColor do not exist on a component; a row built
        //   with the helper compiles green through BasicLang and fails only at csc.
        // ⛔ QUALIFIED types — see the WinFormsType doc comment; both failures were measured
        //   2026-09-19 with BasicLang silent (spec M10, M11), the qualified shape ran (M13).
        // ⚠ Every property and event name below is unfalsifiable except through csc:
        //   WinFormsCatalogSweepTests builds each component into Components and gates it.
        // ==================================================================

        new("Timer", "System.Windows.Forms.Timer", null, null, false, new List<FormPropertyDef>
            {
                new("Interval", FormPropertyType.Int, "100"),
                // ⚠ WinForms only: a JS interval cannot exist disabled — wired means running.
                new("Enabled", FormPropertyType.Bool, "false", Targets: new[] { FormTarget.WinForms })
            },
            Schematic: FormSchematic.Clock, WinFormsEvent: "Tick", WebEvent: "tick",
            Place: FormPlace.Tray,
            // ⛔ The TYPED call over the Window the init region declares, and a parameterless
            // callback: Window.setInterval takes an Action and refuses Action(Of DomEvent) (M7).
            WebHandlerTakesEvent: false,
            // ⚠ Wired means running here, and Enabled=True is how WinForms says the same thing: the
            // retarget crosses that by this rule (BL8027) rather than losing it in either direction.
            WebScript: new FormWebScript("Integer", "w.setInterval(AddressOf {handler}, {Interval})",
                Implies: new FormImpliedProperty("Enabled", "true"))),

        // ⚠ A ToolTip's per-control text (SetToolTip / the web's title attribute) is an EXTENDER
        // property the catalog cannot express yet — docs/form-designer-followups.md 19. Present,
        // inert, callable from code: what VS gives you before you set a tooltip on anything.
        new("ToolTip", "System.Windows.Forms.ToolTip", null, null, false, new List<FormPropertyDef>
            {
                new("InitialDelay", FormPropertyType.Int, "500"),
                new("AutoPopDelay", FormPropertyType.Int, "5000"),
                new("ReshowDelay", FormPropertyType.Int, "100"),
                new("ShowAlways", FormPropertyType.Bool, "false"),
                new("IsBalloon", FormPropertyType.Bool, "false"),
                new("ToolTipTitle", FormPropertyType.String)
            },
            Schematic: FormSchematic.Hint, WinFormsEvent: "Popup",
            Place: FormPlace.Tray, WinFormsEventArgs: "PopupEventArgs"),

        new("ErrorProvider", "System.Windows.Forms.ErrorProvider", null, null, false, new List<FormPropertyDef>
            {
                new("BlinkStyle", FormPropertyType.Enum, "BlinkIfDifferentError",
                    new[] { "BlinkIfDifferentError", "AlwaysBlink", "NeverBlink" },
                    WinFormsEnumType: "ErrorBlinkStyle"),
                new("BlinkRate", FormPropertyType.Int, "250")
            },
            Schematic: FormSchematic.Alert, WinFormsEvent: "RightToLeftChanged",
            Place: FormPlace.Tray),

        new("BackgroundWorker", "System.ComponentModel.BackgroundWorker", null, null, false, new List<FormPropertyDef>
            {
                new("WorkerReportsProgress", FormPropertyType.Bool, "false"),
                new("WorkerSupportsCancellation", FormPropertyType.Bool, "false")
            },
            Schematic: FormSchematic.Worker, WinFormsEvent: "DoWork",
            Place: FormPlace.Tray, WinFormsEventArgs: "System.ComponentModel.DoWorkEventArgs"),

        // ==================================================================
        // Task 24 — menus, toolbars and status bars. Strips are Docked (no geometry, a Dock
        // PROPERTY); items live in their host's Children and are added by the HOST row's verb in
        // document order. ⛔ No Common(). ⛔ Enabled is WinForms-only except on the <input> —
        // a browser ignores `disabled` on <nav>/<li>/<span> (spec §4).
        // ==================================================================

        new("MenuStrip", "MenuStrip", "nav", null, false, new List<FormPropertyDef>
            {
                new("Dock", FormPropertyType.Enum, "Top", new[] { "Top", "Bottom" }, WinFormsEnumType: "DockStyle"),
                new("Enabled", FormPropertyType.Bool, "true", Targets: new[] { FormTarget.WinForms }),
                new("Visible", FormPropertyType.Bool, "true")
            },
            DefaultHeight: 24, Schematic: FormSchematic.MenuBar,
            WinFormsEvent: "ItemClicked", WebEvent: "click", WinFormsEventArgs: "ToolStripItemClickedEventArgs",
            Place: FormPlace.Docked,
            Items: new FormItemRule(new[] { "ToolStripMenuItem", "ToolStripSeparator" }, "{parent}.Items.Add({child})"),
            FormProperty: "MainMenuStrip", HtmlChildrenWrapper: "ul",
            WebCss: ".vgs-MenuStrip ul{list-style:none;margin:0;padding:0;display:flex;background:#f0f0f0}" +
                    ".vgs-MenuStrip li{position:relative;padding:4px 10px;cursor:default}" +
                    ".vgs-MenuStrip li ul{display:none;position:absolute;left:0;top:100%;flex-direction:column;min-width:10em;border:1px solid #ccc;background:#fff}" +
                    ".vgs-MenuStrip li:hover>ul{display:flex}" +
                    ".vgs-MenuStrip li ul li ul{left:100%;top:0}"),

        new("ToolStrip", "ToolStrip", "menu", null, false, new List<FormPropertyDef>
            {
                new("Dock", FormPropertyType.Enum, "Top", new[] { "Top", "Bottom" }, WinFormsEnumType: "DockStyle"),
                new("GripStyle", FormPropertyType.Enum, "Hidden", new[] { "Hidden", "Visible" }, WinFormsEnumType: "ToolStripGripStyle", Targets: new[] { FormTarget.WinForms }),
                new("Enabled", FormPropertyType.Bool, "true", Targets: new[] { FormTarget.WinForms }),
                new("Visible", FormPropertyType.Bool, "true")
            },
            DefaultHeight: 25, Schematic: FormSchematic.ToolBar,
            WinFormsEvent: "ItemClicked", WebEvent: "click", WinFormsEventArgs: "ToolStripItemClickedEventArgs",
            Place: FormPlace.Docked,
            Items: new FormItemRule(new[] { "ToolStripButton", "ToolStripSeparator" }, "{parent}.Items.Add({child})"),
            HtmlRole: "toolbar",
            WebCss: ".vgs-ToolStrip{display:flex;gap:4px;margin:0;padding:2px;background:#f0f0f0}"),

        new("StatusStrip", "StatusStrip", "footer", null, false, new List<FormPropertyDef>
            {
                new("Dock", FormPropertyType.Enum, "Bottom", new[] { "Top", "Bottom" }, WinFormsEnumType: "DockStyle"),
                new("SizingGrip", FormPropertyType.Bool, "false", Targets: new[] { FormTarget.WinForms }),
                new("Enabled", FormPropertyType.Bool, "true", Targets: new[] { FormTarget.WinForms }),
                new("Visible", FormPropertyType.Bool, "true")
            },
            DefaultHeight: 22, Schematic: FormSchematic.StatusBar,
            WinFormsEvent: "ItemClicked", WebEvent: "click", WinFormsEventArgs: "ToolStripItemClickedEventArgs",
            Place: FormPlace.Docked,
            Items: new FormItemRule(new[] { "ToolStripStatusLabel" }, "{parent}.Items.Add({child})"),
            HtmlRole: "status",
            WebCss: ".vgs-StatusStrip{display:flex;gap:8px;padding:2px 6px;background:#f0f0f0;border-top:1px solid #ccc}"),

        new("ToolStripMenuItem", "ToolStripMenuItem", "li", null, false, new List<FormPropertyDef>
            {
                new("Text", FormPropertyType.String),
                new("Enabled", FormPropertyType.Bool, "true", Targets: new[] { FormTarget.WinForms }),
                new("Visible", FormPropertyType.Bool, "true"),
                new("Checked", FormPropertyType.Bool, "false", Targets: new[] { FormTarget.WinForms }),
                new("CheckOnClick", FormPropertyType.Bool, "false", Targets: new[] { FormTarget.WinForms }),
                new("ToolTipText", FormPropertyType.String, HtmlAttribute: "title")
            },
            Schematic: FormSchematic.MenuItem, WinFormsEvent: "Click", WebEvent: "click",
            Place: FormPlace.Item,
            Items: new FormItemRule(new[] { "ToolStripMenuItem", "ToolStripSeparator" }, "{parent}.DropDownItems.Add({child})"),
            HtmlChildrenWrapper: "ul"),

        new("ToolStripSeparator", "ToolStripSeparator", "li", null, false, new List<FormPropertyDef>
            {
                new("Visible", FormPropertyType.Bool, "true")
            },
            Schematic: FormSchematic.Separator, WinFormsEvent: "Click", WebEvent: "click",
            Place: FormPlace.Item, HtmlRole: "separator"),

        new("ToolStripButton", "ToolStripButton", "input", "button", false, new List<FormPropertyDef>
            {
                new("Text", FormPropertyType.String),
                new("Enabled", FormPropertyType.Bool, "true"),
                new("Visible", FormPropertyType.Bool, "true"),
                new("Checked", FormPropertyType.Bool, "false", Targets: new[] { FormTarget.WinForms }),
                new("CheckOnClick", FormPropertyType.Bool, "false", Targets: new[] { FormTarget.WinForms }),
                new("ToolTipText", FormPropertyType.String, HtmlAttribute: "title"),
                new("DisplayStyle", FormPropertyType.Enum, "Text", new[] { "None", "Text", "Image", "ImageAndText" }, WinFormsEnumType: "ToolStripItemDisplayStyle", Targets: new[] { FormTarget.WinForms })
            },
            Schematic: FormSchematic.ToolButton, WinFormsEvent: "Click", WebEvent: "click", Place: FormPlace.Item),

        new("ToolStripStatusLabel", "ToolStripStatusLabel", "span", null, false, new List<FormPropertyDef>
            {
                new("Text", FormPropertyType.String),
                new("Enabled", FormPropertyType.Bool, "true", Targets: new[] { FormTarget.WinForms }),
                new("Visible", FormPropertyType.Bool, "true"),
                new("Spring", FormPropertyType.Bool, "false", Targets: new[] { FormTarget.WinForms }),
                new("ToolTipText", FormPropertyType.String, HtmlAttribute: "title")
            },
            Schematic: FormSchematic.StatusLabel, WinFormsEvent: "Click", WebEvent: "click",
            Place: FormPlace.Item),
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
