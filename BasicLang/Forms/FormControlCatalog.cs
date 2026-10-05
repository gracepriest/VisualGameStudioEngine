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
    Size,

    /// <summary>
    /// <c>Segoe UI, 9pt, style=Bold, Italic</c> — WinForms' <c>FontConverter</c> text (<see cref="FormFontValue"/>).
    /// ONE <c>New Font(…)</c> statement; five CSS declarations at most.
    /// </summary>
    Font,

    /// <summary><c>4</c> or <c>4, 2, 4, 2</c> (Left, Top, Right, Bottom — <see cref="FormPaddingValue"/>). ONE <c>New Padding(…)</c>.</summary>
    Padding,

    /// <summary>A <c>System.Windows.Forms.Cursors</c> member (<see cref="FormCursors"/>). The web's cursor through ONE table.</summary>
    Cursor,

    /// <summary>
    /// A proportion from 0 to 1, stored as an invariant decimal (<c>0.85</c>) — WinForms' <c>Double</c> Opacity, the one
    /// row of this shape (slice 3 pre-flight §3). Out of range is Degraded rather than clamped as WinForms would. ⚠ A
    /// Double row with a different range needs a range facet, not this type.
    /// </summary>
    Fraction,

    /// <summary>
    /// The Id of another control on the same form (<c>AcceptButton</c>) — emitted as that field, AFTER the controls are
    /// constructed. <see cref="FormPropertyDef.ReferenceKinds"/> names the kinds it may point at; the region writer,
    /// which has the document, warns BL8034 for a reference that names none of them.
    /// </summary>
    Reference,

    /// <summary>
    /// Space-separated CSS class names (D2's web-only <c>CssClass</c>): each token a letter, <c>_</c> or <c>-</c> then
    /// letters, digits, <c>_</c> and <c>-</c>. Anything else is Degraded — it lands inside the element's class attribute.
    /// </summary>
    CssClasses,

    /// <summary>
    /// An image file (slice 4 D-5a, PictureBox.Image): a path relative to the PROJECT, forward slashes
    /// (<see cref="FormAssetPaths"/>). WinForms: <c>System.Drawing.Image.FromFile(Path.Combine(AppContext.BaseDirectory, …))</c>
    /// — never .svg/.webp (GDI+ cannot decode them), never a URL. Web: <c>&lt;img src&gt;</c> relative to the site, or a URL.
    /// A rooted path is WinForms-only; <c>..</c> out of the project is Degraded on both.
    /// </summary>
    Image,

    /// <summary>
    /// A window or page icon (slice 4 D-5a, Form.Icon). The Image rules, except WinForms requires <c>.ico</c>
    /// (<c>New Icon</c> throws on a png) and the web takes <c>.ico</c>/<c>.png</c>/<c>.svg</c>/<c>.gif</c>
    /// (<c>&lt;link rel="icon"&gt;</c>).
    /// </summary>
    Icon
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
    VisibleToDisplay,

    /// <summary>A Font → <c>font-family</c>, <c>font-size</c> and, only when set, <c>font-weight</c>/<c>font-style</c>/<c>text-decoration</c>.</summary>
    Font,

    /// <summary>A Padding → <c>padding</c>, in CSS order (top right bottom left).</summary>
    Padding,

    /// <summary>A Cursors member → its CSS <c>cursor</c> keyword (<see cref="FormCursors.CssFor"/>).</summary>
    Cursor,

    /// <summary><c>AutoScroll=true</c> → <c>overflow: auto</c>; <c>false</c> → no declaration.</summary>
    AutoScrollToOverflow
}

/// <summary>What the property grid does with a value an editor pushed — see <see cref="FormPropertyDef.Judge"/>.</summary>
public enum FormEditVerdict
{
    /// <summary>The value is what the row already shows: write nothing, raise nothing.</summary>
    NoOp,

    /// <summary>The editor was cleared on a row that cannot hold "": remove the property.</summary>
    Reset,

    /// <summary>Not a value this row accepts on the target: write nothing, snap the editor back.</summary>
    Refuse,

    /// <summary>A real edit: write it.</summary>
    Write
}

/// <summary>
/// Whether a Color row's WinForms setter takes a translucent colour (WinFormsTranslucentBackColorRunTests measures it per
/// kind): <see cref="Allowed"/>, or it throws <c>ArgumentException</c> — on a control (<see cref="ThrowsOnControl"/>) or
/// on the Form itself (<see cref="ThrowsOnForm"/>, whose refusal points at Opacity).
/// </summary>
public enum FormTranslucency
{
    Allowed,
    ThrowsOnControl,
    ThrowsOnForm
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
/// True when the value is a list that must be ADDED to a read-only collection rather than assigned.
/// <c>ComboBox.Items</c> and <c>ListBox.Items</c> are get-only, so assigning one is CS0200. ⛔ ADR 0020: the document
/// stores one <c>&lt;Item&gt;</c> child per item (a legacy comma attribute is still read); the model holds the items
/// joined by LF (<see cref="FormItems"/>).
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
/// <param name="WebLayouts">
/// On the web, the page layouts this row exists on (spec 2026-09-27 §2.3); null = every layout. Read by
/// <see cref="FormRootValues.Applies(FormPropertyDef, FormTarget, FormLayoutKind?)"/> and by NOTHING else —
/// ⛔ a FormRoot row's applicability is never <see cref="AppliesTo"/> alone: ClientSize targets the web (a
/// Canvas page has a design size) and does not exist on a Grid page. A catalog gate
/// (<c>FormRootLayoutTests.WebLayouts_IsDeclaredOnlyOnFormRootRows_ThatExistOnTheWeb</c>) refuses it on a
/// control row, where no consumer reads it.
/// </param>
/// <param name="ReferenceKinds">
/// For a <see cref="FormPropertyType.Reference"/> row: the control kinds it may name (<c>AcceptButton</c> → Button, the
/// catalog's one <c>IButtonControl</c>). Null on every other row.
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
    string? OracleExemption = null,
    IReadOnlyList<FormLayoutKind>? WebLayouts = null,
    IReadOnlyList<string>? ReferenceKinds = null,
    FormTranslucency WinFormsTranslucency = FormTranslucency.Allowed)
{
    // WinFormsTranslucency: for a Color row, whether the WinForms setter THROWS on a translucent colour (alpha < 255, or
    // Transparent) — Control.BackColor on a control without ControlStyles.SupportsTransparentBackColor ("does not support
    // transparent background colors"). Measured per kind, and pinned, by WinFormsTranslucentBackColorRunTests. Such a
    // value is refused on WinForms (Degraded in a document, refused in the editor); the web keeps alpha (rgba).

    /// <summary>True when the WinForms setter throws on a translucent colour — a control's, or the Form's own.</summary>
    public bool OpaqueOnWinForms => WinFormsTranslucency != FormTranslucency.Allowed;

    /// <summary>The Form's own row: its refusal points at Opacity, what a see-through window is for.</summary>
    private bool OpaqueForm => WinFormsTranslucency == FormTranslucency.ThrowsOnForm;
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

        if (Type == FormPropertyType.Bool)
        {
            return BoolWord(value);
        }

        // ⛔ An Int is its parsed number in invariant text, so "007", " 5 " and "+7" compare equal to
        // the number the editor pushes back — without this the grid's no-op rule saw the numeric
        // editor's "7" as an EDIT of a document holding "007", and rewrote the user's text for a
        // selection click. A value TryParseInt refuses is Degraded and returned unchanged.
        if (Type == FormPropertyType.Int && TryParseInt(value, out var number))
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }

        // A system or named colour in its table's spelling, so `control`/`Control` and `red`/`Red` compare
        // equal wherever a caller asks "is this the same value?" (the grid's no-op rule). Other colours —
        // #hex, and a name neither table knows — unchanged.
        if (Type == FormPropertyType.Color &&
            (FormSystemColors.TryCanonical(value, out var colour) || FormKnownColors.TryCanonical(value, out colour)))
        {
            return colour;
        }

        // The slice-3 types: each in its converter's own shape, re-emitted from what was parsed (FontConverter's
        // "Family, 9pt, style=Bold", PaddingConverter's "4" or "4, 2, 4, 2", a Cursors member's spelling).
        return Type switch
        {
            FormPropertyType.Font when FormFontValue.TryParse(value, out var font) => font.Canonical,
            FormPropertyType.Padding when FormPaddingValue.TryParse(value, out var padding) => padding.Canonical,
            FormPropertyType.Cursor when FormCursors.TryCanonical(value, out var cursor) => cursor,
            FormPropertyType.Fraction when TryParseFraction(value, out var fraction) => FractionText(fraction),
            // A project path in its ONE stored spelling, forward slashes (D-5a); a URL and a rooted path unchanged.
            FormPropertyType.Image or FormPropertyType.Icon => FormAssetPaths.Normalise(value),
            _ => value
        };
    }

    /// <summary>
    /// A <see cref="FormPropertyType.Fraction"/> (the Form's Opacity) — read EXACTLY as Visual Studio's OpacityConverter
    /// reads it (owner decision 2026-09-29; measured on .NET Framework 4.8):
    /// <list type="bullet">
    /// <item>a trailing <c>%</c> (spaces allowed around it) means percent: <c>80%</c> is 0.8, <c>1%</c> is 0.01;</item>
    /// <item>a bare number up to 1 is the FRACTION itself: <c>1</c> is 100%, <c>0.5</c> is 50% — which is also why every
    /// stored document value (<c>0.85</c>) still reads as itself;</item>
    /// <item>a bare number above 1 is a percent: <c>80</c> is 0.8, <c>1.5</c> is 0.015;</item>
    /// <item>anything outside 0%–100% is refused, as VS refuses it.</item>
    /// </list>
    /// An invariant decimal — point, no grouping, no sign, and (beyond VS, approved) no exponent — with only ASCII spaces
    /// around it, the TryParseInt rule for what may surround a number.
    /// </summary>
    public static bool TryParseFraction(string? value, out decimal fraction)
    {
        fraction = 0;
        if (value == null)
        {
            return false;
        }

        var text = value.Trim(' ');
        var percent = text.EndsWith('%');
        if (percent)
        {
            text = text[..^1].TrimEnd(' ');
        }

        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
        {
            return false;
        }

        fraction = percent || number > 1 ? number / 100 : number;
        return fraction is >= 0 and <= 1;
    }

    /// <summary>The number as a BasicLang/C# literal and as canonical document text: invariant, no trailing zeros.</summary>
    private static string FractionText(decimal fraction) => fraction.ToString("0.##########", CultureInfo.InvariantCulture);

    /// <summary>
    /// What the grid SHOWS for a fraction: a percentage, invariant, with up to two decimals — <c>85.5%</c>. (VS rounds to a
    /// whole percent, hiding a stored 0.855 as 85%; showing it is an approved deviation.)
    /// </summary>
    private static string PercentText(decimal fraction) =>
        (fraction * 100).ToString("0.##", CultureInfo.InvariantCulture) + "%";

    /// <summary>
    /// The DOCUMENT text a value typed into the grid is stored as. The typed text itself for every type — except one whose
    /// grid vocabulary is not its document's: a Fraction is typed and shown as a percentage (<c>80%</c>) and stored the way
    /// WinForms stores it, the 0–1 Double (<c>0.8</c>). Call only for a value the row accepts.
    /// </summary>
    public string ToDocument(string value) => Type switch
    {
        FormPropertyType.Fraction when TryParseFraction(value, out var fraction) => FractionText(fraction),
        // ⛔ The grid's Bool drop-down offers "True"/"False" (VS's spelling); the document says "true"/"false" (slice 4 D-3).
        FormPropertyType.Bool => BoolWord(value),
        _ => value
    };

    /// <summary>
    /// A Bool in the DOCUMENT's vocabulary — <c>true</c>/<c>false</c>, lower case — or the text unchanged when it is not a
    /// Bool at all (a Degraded value is preserved, never coerced). ⛔ THE one spelling rule: <see cref="Canonical"/>,
    /// <see cref="ToDocument"/> and the property grid's catalog-less Bool rows (the Font's Bold/Italic/Underline parts)
    /// all ask it, so a drop-down pushing <c>True</c> over a stored <c>true</c> is the same value on every path.
    /// </summary>
    public static string BoolWord(string value) =>
        bool.TryParse(value, out var flag) ? (flag ? "true" : "false") : value;

    /// <summary>
    /// What an editor OFFERS for this row: an Enum's <see cref="AllowedValues"/>, a Cursor row's
    /// <see cref="FormCursors.Names"/>, null for a free-text row. ⛔ The grid asks this, never the type itself.
    /// </summary>
    public IReadOnlyList<string>? Choices => Type == FormPropertyType.Cursor ? FormCursors.Names : AllowedValues;

    /// <summary>
    /// Equality in this row's own terms: <c>True</c> and <c>true</c> are one Bool; <c>Left</c> and
    /// <c>MiddleLeft</c> are one TextAlign; <c>007</c> and <c>7</c> are one Int. ⛔ THE one answer —
    /// FormRetarget and the property grid's bold and no-op rules all ask it; a private copy in each
    /// would be a mirrored pair.
    ///
    /// <para>⚠ An Enum or Color compares case-insensitively after canonicalising, so a case-only hex
    /// edit (<c>#ff0000</c> → <c>#FF0000</c>) is the same colour. A String, Int or Size is ordinal:
    /// a caption's case is the user's text.</para>
    /// </summary>
    public bool SameValue(string a, string b) =>
        string.Equals(Canonical(a), Canonical(b),
            // ⚠ A Font's family is case-insensitive on Windows ("segoe ui" IS Segoe UI), and a Cursor canonicalises
            // to its member — both compare like an Enum.
            Type is FormPropertyType.Enum or FormPropertyType.Color or FormPropertyType.Font or FormPropertyType.Cursor
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);

    /// <summary>
    /// What an editor SHOWS for this property on <paramref name="target"/> (spec §2.7): the document's
    /// value in its canonical spelling when present, the target's default when absent, "" when absent
    /// with no default. <paramref name="present"/> is the document's text, or null when it carries none.
    /// </summary>
    public string Displayed(string? present, FormTarget target)
    {
        var shown = present ?? DefaultFor(target);
        if (shown == null)
        {
            return "";
        }

        // ⚠ A Fraction is SHOWN in the grid's vocabulary, a percentage (owner decision 2026-09-29) — SameValue still
        // compares it with the stored 0–1 text, because both canonicalise to the same fraction.
        return Type == FormPropertyType.Fraction && TryParseFraction(shown, out var fraction)
            ? PercentText(fraction)
            : Canonical(shown);
    }

    /// <summary>
    /// What a typed editor pushes back when it renders "" — a NumericUpDown cannot show empty, so it
    /// shows (and pushes) 0; a CheckBox shows unchecked. Every other editor pushes "" itself.
    /// </summary>
    private string EmptyEcho => Type switch
    {
        FormPropertyType.Int => "0",
        FormPropertyType.Bool => "false",
        _ => ""
    };

    /// <summary>
    /// The property grid's decision about a value an editor pushed (spec §2.7, §7) — a PURE function of
    /// this row, so every rule is table-testable; the grid row only carries the verdict out.
    ///
    /// <list type="number">
    /// <item><b>NoOp</b> — the value is what the row already DISPLAYS, in this row's own terms
    /// (<see cref="SameValue"/>): an absent row's default coming back on LostFocus, an alias pushed back
    /// as its member, <c>7</c> for a document holding <c>007</c>, the grid's re-push on selection. ⛔ Also
    /// the typed editor's ECHO of an absent row with no default (<see cref="EmptyEcho"/>): a web
    /// TextBox's MaxLength displays "" (no limit), the NumericUpDown renders it as 0 and pushes 0 back,
    /// and writing that made a text box that accepts no input — for a selection click.</item>
    /// <item><b>Reset</b> — "" on a row whose type cannot hold "": the user emptied the box to get the
    /// default back (spec §7). A String accepts "", so its empty caption is a real value.</item>
    /// <item><b>Refuse</b> — a value this row does not accept on <paramref name="target"/> (spec §7):
    /// never written, so the designer cannot manufacture a Degraded value of its own.</item>
    /// <item><b>Write</b> — everything else.</item>
    /// </list>
    /// </summary>
    /// <param name="value">The pushed value.</param>
    /// <param name="present">The document's text, or null when the document does not carry the property.</param>
    /// <param name="target">Whose default and whose value rules apply.</param>
    public FormEditVerdict Judge(string value, string? present, FormTarget target)
    {
        if (SameValue(value, Displayed(present, target)))
        {
            return FormEditVerdict.NoOp;
        }

        if (present == null && DefaultFor(target) == null && EmptyEcho.Length > 0 &&
            string.Equals(value, EmptyEcho, StringComparison.Ordinal))
        {
            return FormEditVerdict.NoOp;
        }

        if (value.Length == 0 && !Accepts("", target))
        {
            return FormEditVerdict.Reset;
        }

        return Accepts(value, target) ? FormEditVerdict.Write : FormEditVerdict.Refuse;
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
            return $"{WinFormsFactory}({StringLiteral(value)})";
        }

        // ⛔ Re-emitted from the PARSED value, never the input text — whatever the parser tolerated around
        // the digits stays out of the user's file.
        return Type switch
        {
            FormPropertyType.Color => ColorLiteral(value),
            FormPropertyType.Size => TryParseSize(value, out var w, out var h) ? SizeLiteral(w, h) : null,
            FormPropertyType.Int => TryParseInt(value, out var n) ? n.ToString(CultureInfo.InvariantCulture) : null,
            FormPropertyType.Font => FormFontValue.TryParse(value, out var font) ? font.WinFormsLiteral : null,
            FormPropertyType.Padding => FormPaddingValue.TryParse(value, out var padding) ? padding.WinFormsLiteral : null,
            FormPropertyType.Cursor => FormCursors.TryCanonical(value, out var cursor) ? "Cursors." + cursor : null,
            FormPropertyType.Fraction => TryParseFraction(value, out var fraction) ? FractionText(fraction) : null,
            // The field the Id names. ⚠ Whether such a field EXISTS is a document question — the region writer's (BL8034).
            FormPropertyType.Reference => FormDocument.IsLegalControlId(value) ? value : null,
            FormPropertyType.Image => AssetLiteral("System.Drawing.Image.FromFile", value),
            FormPropertyType.Icon => AssetLiteral("New System.Drawing.Icon", value),
            _ => null
        };
    }

    /// <summary>
    /// An image/icon as WinForms source (D-5b), FULLY QUALIFIED (a user's <c>Using</c> can make a bare <c>Image</c> or
    /// <c>Icon</c> ambiguous — CS0104, BasicLang silent). A project path is anchored to the program's own folder,
    /// <c>System.AppContext.BaseDirectory</c> — measured (pre-flight M8): a bare relative path resolves against the WORKING
    /// directory and threw FileNotFoundException under both launch shapes. A rooted path is written as it stands (and is
    /// not copied — BL8036 says so at build). Null for anything else.
    /// </summary>
    private static string? AssetLiteral(string call, string value)
    {
        if (FormAssetPaths.IsInsideProject(value))
        {
            return $"{call}(System.IO.Path.Combine(System.AppContext.BaseDirectory, " +
                   $"{StringLiteral(FormAssetPaths.Normalise(value))}))";
        }

        return FormAssetPaths.IsRooted(value) ? $"{call}({StringLiteral(value)})" : null;
    }

    /// <summary>
    /// Document text as a BasicLang string EXPRESSION — the ONE escape every generated string goes
    /// through. Usually a single quoted literal; a value with a line break or tab becomes literals
    /// joined with <c>&amp;</c> to <c>vbCr</c> / <c>vbLf</c> / <c>vbCrLf</c> / <c>vbTab</c>, so every
    /// caller must splice it where an expression is allowed (all of them do).
    ///
    /// <para>⛔ The lexer reads a literal as VB does: <c>""</c> is the only escape and a backslash
    /// is an ORDINARY character, so <c>a\b</c> is written raw. (It used to be a C-style escape, and
    /// this method wrote <c>\\</c>; since master 3cec5030 that reaches the running program as TWO
    /// backslashes, with the build green.)</para>
    ///
    /// <para>⛔ A line break or tab must still never be written raw: it lands inside the generated
    /// region, and a CR LF caption in an LF file makes the next write read the file's newline style
    /// from the caption and rewrite every line ending. With no escape left inside a literal, it is
    /// spelled with the VB constant (<c>SemanticAnalyzer.VbStringConstants</c>, lowered to a plain
    /// string constant on every backend).</para>
    /// </summary>
    public static string StringLiteral(string text)
    {
        var parts = new System.Collections.Generic.List<string>();
        var run = new System.Text.StringBuilder();

        void FlushRun()
        {
            if (run.Length > 0)
            {
                parts.Add("\"" + run + "\"");
                run.Clear();
            }
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            string? constant = c switch
            {
                '\r' when i + 1 < text.Length && text[i + 1] == '\n' => "vbCrLf",
                '\r' => "vbCr",
                '\n' => "vbLf",
                '\t' => "vbTab",
                _ => null
            };

            if (constant == null)
            {
                run.Append(c == '"' ? "\"\"" : c.ToString());
                continue;
            }

            FlushRun();
            parts.Add(constant);
            if (constant == "vbCrLf")
            {
                i++;
            }
        }

        FlushRun();
        return parts.Count == 0 ? "\"\"" : string.Join(" & ", parts);
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
        return parts.Length == 2 && TryParseInt(parts[0], out width) && TryParseInt(parts[1], out height);
    }

    /// <summary>
    /// One document integer: an optional ASCII sign and decimal digits, culture-invariant, with only
    /// ASCII SPACE allowed around it.
    ///
    /// <para>⛔ Not <c>int.TryParse(value)</c>, which skips every character from U+0009 to U+000D — so
    /// <c>"5\r\n"</c> parsed, was Canon, and the writer spliced the raw text (a line break inside the
    /// generated region). Space is what a person types beside a number; a tab, a line break or an NBSP
    /// there is never meant, and the one honest answer is Degraded — preserved, reported, not written.
    /// Refusing rather than trimming also keeps every OTHER consumer of the raw value (the web's markup
    /// attributes, a script template) safe without each needing its own trim. Current culture is out
    /// for the U+2212 reason SizeLiteral states.</para>
    /// </summary>
    public static bool TryParseInt(string value, out int result) =>
        int.TryParse(value.Trim(' '), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out result);

    /// <summary>The individual items of an <see cref="IsItemCollection"/> MODEL value — one per line (ADR 0020, <see cref="FormItems.Split"/>).</summary>
    public static IEnumerable<string> SplitItems(string value) => FormItems.Split(value);

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

        // A named colour in Color's OWN spelling: `Color.red` is CS0117 in the generated C#.
        if (FormKnownColors.TryCanonical(value, out var named))
        {
            return "Color." + named;
        }

        if (value.Length == 0 || value[0] != '#')
        {
            // A name neither table knows is refused on WinForms (IsRefusedOn), so this is reached only
            // by a caller that skipped the Degraded check — kept as the document's text, never invented.
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

        return FromArgbLiteral(Hex(digits, 0), Hex(digits, 2), Hex(digits, 4), Hex(digits, 6));
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
    ///
    /// <para>⚠ A String row always answers false — see the String arm.</para>
    /// </summary>
    public bool IsSourceForm(string value) => SourceLiteral(value) != null;

    /// <summary>
    /// The WinForms source to write for a value that <see cref="IsSourceForm"/> recognises — RE-EMITTED
    /// from what was parsed, never the input text — or null when the value is not a source form.
    ///
    /// <para>⛔ A table match is its own canonical text (it matched ORDINALLY). A parsed form —
    /// <c>Color.FromArgb(…)</c>, <c>New Size(…)</c> — is rebuilt from its numbers, so the spacing, the
    /// leading zeros and anything else the parser tolerated can never reach the user's file.</para>
    /// </summary>
    public string? SourceLiteral(string value) => Type switch
    {
        FormPropertyType.Enum =>
            WinFormsEnumType != null && AllowedValues != null &&
            AllowedValues.Any(v => string.Equals(WinFormsLiteral(v), value, StringComparison.Ordinal))
                ? value
                : null,

        // ⛔⛔ Exactly the shapes ColorLiteral WRITES, each proved by a table or a full parse — never a
        // prefix. The grid's Color row is a free-text box, and the old arm (anything starting `Color.`
        // or `New `) spliced `New Foo`, `Color.Bogus` and `Color.Red + junk` verbatim into the user's
        // file: csc errors, BasicLang silent. `New …` is gone entirely — ColorLiteral never emits it.
        FormPropertyType.Color =>
            IsSystemColorsSource(value) || IsNamedColorSource(value) ? value : FromArgbSource(value),

        // Exactly `New Size(w, h)` around two integers — never a prefix match, which would pass
        // `New Size(1, 2) + junk` straight into the generated source.
        FormPropertyType.Size =>
            value.StartsWith("New Size(", StringComparison.Ordinal) &&
            value.EndsWith(")", StringComparison.Ordinal) &&
            TryParseSize(value.Substring("New Size(".Length, value.Length - "New Size(".Length - 1), out var w, out var h)
                ? SizeLiteral(w, h)
                : null,

        // ⛔⛔ A String has NO source form in the document: `Properties` holds DOCUMENT text, one
        // convention. The old arm (`"…` or `New …` is already source) was a SHAPE test — the thing this
        // method exists to replace — and it spliced a caption `New Customer` unquoted (build broken) and
        // `"quoted"` without its quotes. It was added (525aa88b) for the D12 recognizer, which stores raw
        // source text; nothing feeds recognizer output into Properties, and when the importer is built it
        // must UNQUOTE string literals into document text at that boundary, never teach this method a shape.
        FormPropertyType.String => null,

        // An Int or a Bool has no source form that differs from its document text (both are
        // re-emitted from their parsed value by WinFormsLiteral / the writer instead).
        _ => null
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
            FormPropertyType.Int => TryParseInt(value, out _),
            FormPropertyType.Bool => bool.TryParse(value, out _),
            // "#rrggbb", "#rgb", "#aarrggbb", or a bare name the target resolves (KnownColor / system / CSS).
            FormPropertyType.Color => IsColor(value),
            FormPropertyType.Enum => AllowedValues != null &&
                                     (AllowedValues.Any(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase)) ||
                                      Aliases?.ContainsKey(value) == true),
            FormPropertyType.Size => TryParseSize(value, out _, out _),
            FormPropertyType.Font => FormFontValue.TryParse(value, out _),
            FormPropertyType.Padding => FormPaddingValue.TryParse(value, out _),
            FormPropertyType.Cursor => FormCursors.TryCanonical(value, out _),
            FormPropertyType.Fraction => TryParseFraction(value, out _),
            FormPropertyType.Reference => FormDocument.IsLegalControlId(value),
            FormPropertyType.CssClasses => IsCssClassList(value),
            FormPropertyType.Image or FormPropertyType.Icon => IsAssetPath(value),
            _ => false
        };
    }

    /// <summary>
    /// An image/icon value usable on SOME target: a URL, a rooted path, or a relative path inside the project — no control
    /// characters (it lands in a string literal and an HTML attribute). <c>..</c> out of the project is usable on neither
    /// (Degraded): nothing would copy it, and the program would look for it beside itself.
    /// </summary>
    private static bool IsAssetPath(string value) =>
        value.Length > 0 && !value.Any(char.IsControl) &&
        (FormAssetPaths.IsUrl(value) || FormAssetPaths.IsRooted(value) || FormAssetPaths.IsInsideProject(value));

    private static readonly string[] WebIconExtensions = { ".ico", ".png", ".svg", ".gif" };

    /// <summary>
    /// The target rules of an Image/Icon value (D-5a), each a run-time failure otherwise: WinForms reads FILES (a URL is
    /// refused), GDI+ cannot decode .svg/.webp, <c>New Icon</c> throws on anything but .ico; the web cannot reach the
    /// author's disk (a rooted path is refused) and a page icon is .ico/.png/.svg/.gif.
    /// </summary>
    private bool IsAssetRefusedOn(string value, FormTarget target)
    {
        if (Type is not (FormPropertyType.Image or FormPropertyType.Icon))
        {
            return false;
        }

        var extension = FormAssetPaths.Extension(value);
        return target switch
        {
            FormTarget.WinForms => FormAssetPaths.IsUrl(value) ||
                                   (Type == FormPropertyType.Image && extension is ".svg" or ".webp") ||
                                   (Type == FormPropertyType.Icon && extension != ".ico"),
            FormTarget.Web => FormAssetPaths.IsRooted(value) ||
                              (Type == FormPropertyType.Icon && !FormAssetPaths.IsUrl(value) &&
                               !WebIconExtensions.Contains(extension)),
            _ => false
        };
    }

    /// <summary>
    /// One or more CSS class names separated by spaces: each starts with a letter, <c>_</c> or <c>-</c> (then not a digit)
    /// and continues with letters, digits, <c>_</c>, <c>-</c>. ⛔ ASCII only — the value lands inside the element's
    /// <c>class</c> attribute, and a quote or an angle bracket there would be the page's problem, not the user's.
    /// </summary>
    private static bool IsCssClassList(string value)
    {
        var tokens = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length > 0 && tokens.All(t =>
        {
            var body = t.StartsWith('-') ? t[1..] : t;
            return body.Length > 0 && (char.IsAsciiLetter(body[0]) || body[0] == '_') &&
                   body.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
        });
    }

    /// <summary>
    /// True when <paramref name="value"/> is usable on <paramref name="target"/>. Stricter than
    /// <see cref="Accepts(string?)"/> in two places today, both colours (<see cref="IsRefusedOn"/>): a
    /// Windows system colour with no CSS equivalent is WinForms-only as a VALUE (spec §2.2), and a
    /// colour NAME WinForms' tables do not know is web-only.
    /// </summary>
    public bool Accepts(string? value, FormTarget target) =>
        Accepts(value) && !IsRefusedOn(value!, target);

    /// <summary>
    /// Why <paramref name="value"/> is not usable on <paramref name="target"/> — the Degraded reason.
    ///
    /// <para>⛔ Asks the SAME predicate as <see cref="Accepts(string?, FormTarget)"/>, so the reason
    /// can never describe a refusal that check did not make. Called for a value that IS usable there
    /// is nothing to describe, and inventing a reason would put a false one in front of the user — so
    /// it throws.</para>
    /// </summary>
    public string DescribeRefusal(string value, FormTarget target) =>
        RefusalReason(value, target) + " The value is preserved exactly as written.";

    /// <summary>
    /// Why a value TYPED into the property grid was refused (spec §7 "refused in the editor, never
    /// written") — the same reason as <see cref="DescribeRefusal"/>, with the ending an edit needs: the
    /// Degraded ending ("preserved exactly as written") would be false here, because the typed value was
    /// never written anywhere. Throws for a usable value, as <see cref="DescribeRefusal"/> does.
    /// </summary>
    public string DescribeRefusedEdit(string value, FormTarget target) =>
        RefusalReason(value, target) + $" It was not applied; {Name} is unchanged.";

    /// <summary>The reason alone, shared by both endings — ONE predicate, ONE set of texts.</summary>
    private string RefusalReason(string value, FormTarget target)
    {
        if (Accepts(value, target))
        {
            throw new ArgumentException(
                $"'{value}' is usable for {Name} on {target}; there is no refusal to describe.", nameof(value));
        }

        if (IsSystemColourRefusedOn(value, target))
        {
            // The result is known true: IsSystemColourRefusedOn just answered yes by the same lookup.
            _ = FormSystemColors.TryCanonical(value, out var system);
            return $"'{value}' is the Windows system colour {system}, which has no CSS equivalent, so a " +
                   "web form cannot use it.";
        }

        if (IsUnknownColourNameRefusedOn(value, target))
        {
            return $"'{value}' is not a named colour WinForms knows (System.Drawing.Color has no such " +
                   "member, and it is not a system colour), so a WinForms form cannot use it.";
        }

        if (IsTranslucentRefusedOn(value, target))
        {
            // ⚠ The Form's own row says what a see-through WINDOW is for: Opacity (OpaqueForm marks the FormRoot rows).
            return OpaqueForm
                ? $"'{value}' is a transparent colour, and a WinForms Form does not support a transparent {Name} — it " +
                  "throws ArgumentException when the form is created. For a see-through window set Opacity instead; " +
                  "use an opaque colour (alpha FF) here."
                : $"'{value}' is a transparent colour, and this WinForms control does not support a transparent " +
                  $"{Name} — it throws ArgumentException when the form is created. Use an opaque colour (alpha FF).";
        }

        if (IsAssetRefusedOn(value, target))
        {
            var extension = FormAssetPaths.Extension(value);
            return target == FormTarget.WinForms
                ? FormAssetPaths.IsUrl(value)
                    ? $"'{value}' is a web address; a WinForms program reads {Name} from a FILE (Image.FromFile and " +
                      "New Icon take a path), so it cannot use a URL. Put the file in the project instead."
                    : Type == FormPropertyType.Icon
                        ? $"'{value}' is not an .ico file; a WinForms window icon requires .ico (New Icon throws " +
                          "ArgumentException on anything else when the form is created)."
                        : $"'{value}' is a {extension} file, which WinForms cannot decode (GDI+ has no {extension} codec; " +
                          "Image.FromFile throws when the form is created). Use .png, .jpg, .gif, .bmp or .ico."
                : FormAssetPaths.IsRooted(value)
                    ? $"'{value}' is a path on the author's machine; a web page cannot reach it. Put the file in the " +
                      "project (it is copied beside the page) or use a web address."
                    : $"'{value}' is not an icon a browser shows for a page (expected .ico, .png, .svg or .gif).";
        }

        if (IsCursorRefusedOn(value, target))
        {
            _ = FormCursors.TryCanonical(value, out var cursor);
            return $"'{value}' is the WinForms cursor Cursors.{cursor}, which has no CSS equivalent, so a web form " +
                   "cannot use it.";
        }

        if (Type == FormPropertyType.Cursor)
        {
            return $"'{value}' is not a member of System.Windows.Forms.Cursors (expected one of: " +
                   $"{string.Join(", ", FormCursors.Names)}).";
        }

        return $"'{value}' is not a valid {Type}" +
               (AllowedValues is { Count: > 0 } ? $" (expected one of: {string.Join(", ", AllowedValues)})" : "") +
               Type switch
               {
                   FormPropertyType.Font => " (expected WinForms' font text: a family of letters, digits, spaces or " +
                                            "hyphens, a size in points, optional styles — e.g. 'Segoe UI, 9pt, style=Bold')",
                   FormPropertyType.Padding => " (expected one non-negative whole number, or four: Left, Top, Right, Bottom)",
                   FormPropertyType.Fraction => " (expected a percentage from 0% to 100%, e.g. 85% — a bare number up " +
                                                "to 1 is read as a fraction, as Visual Studio reads it, so 0.85 is also 85%)",
                   FormPropertyType.Reference => " (expected the Id of a control on this form)",
                   FormPropertyType.Image or FormPropertyType.Icon =>
                       " (expected a file inside the project, e.g. Resources/logo.png — a path that climbs out of the " +
                       "project with '..' is never copied, and the program would not find it)",
                   _ => ""
               } +
               ".";
    }

    /// <summary>
    /// THE target-specific refusal — the one predicate <see cref="Accepts(string?, FormTarget)"/> and
    /// <see cref="DescribeRefusal"/> share, each arm of it also asked by DescribeRefusal for its reason.
    /// Only a COLOUR row refuses by target: a String caption reading "Window" is text, and an Enum
    /// member named "Menu" is that enum's business.
    /// </summary>
    private bool IsRefusedOn(string value, FormTarget target) =>
        IsSystemColourRefusedOn(value, target) || IsUnknownColourNameRefusedOn(value, target) ||
        IsCursorRefusedOn(value, target) || IsTranslucentRefusedOn(value, target) || IsAssetRefusedOn(value, target);

    /// <summary>
    /// A translucent colour (<c>#AARRGGBB</c> with AA below FF, or the named <c>Transparent</c>) on a WinForms row marked
    /// <see cref="OpaqueOnWinForms"/>: the control's setter throws <c>ArgumentException</c> when the form is constructed —
    /// a green build and a dead window. Measured by <c>WinFormsTranslucentBackColorRunTests</c>.
    /// </summary>
    private bool IsTranslucentRefusedOn(string value, FormTarget target) =>
        !AcceptsTranslucentOn(target) && IsTranslucent(value);

    /// <summary>
    /// Whether this colour row takes a translucent value on <paramref name="target"/> — the colour editor turns its alpha
    /// channel off where this is false (the same answer the refusal gives, never a second rule).
    /// </summary>
    public bool AcceptsTranslucentOn(FormTarget target) =>
        !(Type == FormPropertyType.Color && target == FormTarget.WinForms && OpaqueOnWinForms);

    /// <summary>
    /// True for a colour whose alpha is below 255, in every spelling the writer would EMIT: an 8-digit hex with AA ≠ FF,
    /// the name <c>Transparent</c>, and the SOURCE forms <c>Color.Transparent</c> and <c>Color.FromArgb(a, r, g, b)</c> with
    /// a &lt; 255 (Part C review: a source form skipped the Accepts gate and reached the generated code). An unparseable
    /// <c>Color.FromArgb(…)</c> is no source form at all, so it is Degraded and never emitted already.
    /// </summary>
    internal static bool IsTranslucent(string value)
    {
        if (value.Length == 9 && value[0] == '#' && value[1..].All(Uri.IsHexDigit))
        {
            return !string.Equals(value.Substring(1, 2), "FF", StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(value, "Color.Transparent", StringComparison.Ordinal))
        {
            return true;
        }

        if (TryParseFromArgb(value, out var argb))
        {
            return argb[0] < 255;
        }

        return FormKnownColors.TryCanonical(value, out var name) && name == "Transparent";
    }

    /// <summary>
    /// Whether the region writer may write <paramref name="value"/> on <paramref name="target"/>: a value the target
    /// accepts, or a SOURCE form (<see cref="IsSourceForm"/>) the target does not refuse. ⛔ The ONE gate both of the
    /// writer's property paths (controls and the Form's own rows) ask — a source form used to bypass the target's refusals
    /// (<c>Color.Transparent</c> on a TextBox: green build, ArgumentException at run time).
    /// </summary>
    public bool IsWritableOn(string value, FormTarget target) =>
        Accepts(value, target) || (IsSourceForm(value) && !IsRefusedOn(value, target));

    /// <summary>
    /// A Cursors member with no CSS equivalent (<see cref="FormCursors.CssFor"/> null — the up arrow, the pan cursors) on
    /// a web form: WinForms-only as a VALUE, the system-colour rule.
    /// </summary>
    private bool IsCursorRefusedOn(string value, FormTarget target) =>
        Type == FormPropertyType.Cursor && target == FormTarget.Web &&
        FormCursors.TryCanonical(value, out _) && FormCursors.CssFor(value) == null;

    private bool IsSystemColourRefusedOn(string value, FormTarget target) =>
        Type == FormPropertyType.Color && target == FormTarget.Web &&
        FormSystemColors.TryCanonical(value, out var system) && FormSystemColors.CssFor(system) == null;

    /// <summary>
    /// A bare colour NAME on WinForms that neither table knows — <c>Bogus</c>, or a CSS name such as
    /// <c>RebeccaPurple</c> that System.Drawing.Color lacks. Written, it is <c>Color.Bogus</c>: CS0117 at
    /// csc, BasicLang silent. The web keeps today's acceptance — CSS names are case-insensitive, include
    /// names WinForms lacks, and the browser is the judge.
    /// </summary>
    private bool IsUnknownColourNameRefusedOn(string value, FormTarget target) =>
        Type == FormPropertyType.Color && target == FormTarget.WinForms &&
        IsColorName(value) &&
        !FormSystemColors.TryCanonical(value, out _) && !FormKnownColors.TryCanonical(value, out _);

    /// <summary><c>SystemColors.X</c> where X is, exactly and case-sensitively, a member the table names.</summary>
    private static bool IsSystemColorsSource(string value)
    {
        const string prefix = "SystemColors.";
        if (!value.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var member = value.Substring(prefix.Length);
        return FormSystemColors.TryCanonical(member, out var canonical) &&
               string.Equals(member, canonical, StringComparison.Ordinal);
    }

    /// <summary><c>Color.X</c> where X is, exactly and case-sensitively, a named Color property.</summary>
    private static bool IsNamedColorSource(string value)
    {
        const string prefix = "Color.";
        return value.StartsWith(prefix, StringComparison.Ordinal) &&
               FormKnownColors.IsMember(value.Substring(prefix.Length));
    }

    /// <summary>
    /// <c>Color.FromArgb(a, r, g, b)</c> — the one call ColorLiteral emits — around exactly four
    /// decimal integers 0-255 and nothing after the closing parenthesis, RE-EMITTED canonically from
    /// the parsed numbers (null when it does not parse). Only ASCII SPACE is allowed around the commas:
    /// <c>Trim()</c> would also strip CR, LF, NBSP and U+2028, and before re-emission that spliced a
    /// multi-line statement into the user's file. The three-argument overload is refused because nothing
    /// here writes it.
    /// </summary>
    private static string? FromArgbSource(string value) =>
        TryParseFromArgb(value, out var argb) ? FromArgbLiteral(argb[0], argb[1], argb[2], argb[3]) : null;

    /// <summary>The four numbers of a <see cref="FromArgbSource"/> value — the ONE parser, for the literal and the alpha test.</summary>
    private static bool TryParseFromArgb(string value, out int[] argb)
    {
        argb = new int[4];
        const string prefix = "Color.FromArgb(";
        if (!value.StartsWith(prefix, StringComparison.Ordinal) || !value.EndsWith(")", StringComparison.Ordinal))
        {
            return false;
        }

        var parts = value.Substring(prefix.Length, value.Length - prefix.Length - 1).Split(',');
        if (parts.Length != 4)
        {
            return false;
        }

        for (var i = 0; i < 4; i++)
        {
            if (!int.TryParse(parts[i].Trim(' '), NumberStyles.None, CultureInfo.InvariantCulture, out argb[i]) ||
                argb[i] > 255)
            {
                return false;
            }
        }

        return true;
    }

    // The ONE spelling of the call, shared by ColorLiteral and FromArgbSource so what is written and what
    // is recognised cannot drift. Invariant for the SizeLiteral reason.
    private static string FromArgbLiteral(int a, int r, int g, int b) =>
        string.Create(CultureInfo.InvariantCulture, $"Color.FromArgb({a}, {r}, {g}, {b})");

    /// <summary>A bare colour name — IsColor's letters-only arm.</summary>
    private static bool IsColorName(string value) => value.Length > 0 && value.All(char.IsLetter);

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

        // A bare name — a KnownColor, a system colour or a CSS named colour. Usable on SOME target, so
        // accepted here; WinForms refuses a name its tables do not know (IsUnknownColourNameRefusedOn).
        return IsColorName(value);
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

/// <param name="Events">
/// The kind's events (spec §2.5) — since slice 5 a LIST per kind (pre-flight D-1: the default, the kind-specific
/// events a VS user wires, and the Mouse/Key/Focus events WinForms browses on that kind; the Events tab, the emitter
/// and the retarget all read it through <see cref="FormEvents.WiredOn"/>). The <see cref="FormEventDef.IsDefault"/>
/// entry is what a double-click means and what the old single-event fields now DERIVE from — <see cref="WinFormsEvent"/>,
/// <see cref="WebEvent"/>, <see cref="WinFormsEventArgs"/> — so every reader of those keeps its
/// behaviour until it is migrated. ⚠ An event's <see cref="FormEventDef.WinFormsArgs"/> is qualified:
/// a <c>BackgroundWorker.DoWork</c> handler declared with <c>EventArgs</c> compiles by contravariance
/// but cannot reach <c>e.Argument</c>; the typed stub is the useful one.
/// <para>⚠ NULL means "declares no events" — exactly what an empty list means, and every reader treats
/// the two alike (<c>?.</c>, or <see cref="FormEvents.WiredOn"/>, which returns empty for both). It
/// stays nullable rather than defaulting to empty because a positional record parameter's default
/// must be a compile-time constant (an empty array is not), and redeclaring the member non-null
/// beside the parameter risks CS8866/CS8907 on the synthesized <c>Deconstruct</c>. A row that DOES
/// declare events must name exactly one <see cref="FormEventDef.IsDefault"/> entry
/// (<c>FormEventsTests</c>).</para>
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
/// <param name="StretchesWhenStacked">
/// Below a Canvas page's phone breakpoint (spec 2026-09-27 §5), the control spans the column instead of keeping its
/// designed width — inputs and pictures (TextBox, which covers multi-line text, ComboBox, ListBox, PictureBox). Only
/// on a Positioned row the web has (FormAssetEmitterTests pins it). ⛔ The emitter reads this; it never switches on
/// the kind.
/// </param>
/// <param name="DropValues">
/// What a control of this kind is given when it is DROPPED from the toolbox, beyond its caption — a designer
/// PREFERENCE, which spec §2.7 says is written explicitly at placement, never encoded as a false default. Owner
/// decision O1 (2026-09-29): a TableLayoutPanel drops 2×2, as Visual Studio drops one, while its WinForms default stays
/// 0×0. ⛔ Read by <c>FormPlacement</c> and nothing else; <c>FormPlacementTests.EveryDropValue_…</c> requires each
/// entry to be a row of the kind with a value that row accepts. Null for every other kind.
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
    IReadOnlyList<FormEventDef>? Events = null,
    bool WebHandlerTakesEvent = true,
    FormWebScript? WebScript = null,
    FormPlace Place = FormPlace.Positioned,
    FormItemRule? Items = null,
    string? FormProperty = null,
    string? HtmlChildrenWrapper = null,
    string? HtmlRole = null,
    string? WebCss = null,
    bool StretchesWhenStacked = false,
    IReadOnlyDictionary<string, string>? DropValues = null)
{
    /// <summary>Task 25's flag, now derived: the eleven sites that read it keep reading it.</summary>
    public bool IsComponent => Place == FormPlace.Tray;

    /// <summary>A strip or a menu item — something whose children are items.</summary>
    public bool IsHost => Items != null;

    /// <summary>The event a double-click means — the row's <see cref="FormEventDef.IsDefault"/> entry.</summary>
    public FormEventDef? DefaultEventDef => Events?.FirstOrDefault(e => e.IsDefault);

    /// <summary>Derived (spec §2.5): the default entry's WinForms name.</summary>
    public string? WinFormsEvent => DefaultEventDef?.Name;

    /// <summary>Derived: the default entry's DOM event type.</summary>
    public string? WebEvent => DefaultEventDef?.WebEvent;

    /// <summary>Derived: the default entry's handler <c>e</c> type; null means <c>EventArgs</c>.</summary>
    public string? WinFormsEventArgs => DefaultEventDef?.WinFormsArgs;

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
    public string? DefaultEvent(FormTarget target) =>
        DefaultEventDefOn(target) is { } evt ? FormEvents.NameOn(evt, target) : null;

    /// <summary>
    /// The EVENT a double-click opens on <paramref name="target"/> — the row's <see cref="FormEventDef.IsDefault"/>
    /// entry when it has a name there; on the web, else the row's declared <see cref="FormEventDef.IsWebDefault"/>
    /// (a Panel: Paint has no page equivalent, so Click); else null. Its <see cref="FormEventDef.Name"/> names the
    /// handler on both targets (owner decision 2026-09-29); <see cref="DefaultEvent"/> is what the bind listens to.
    /// </summary>
    public FormEventDef? DefaultEventDefOn(FormTarget target)
    {
        if (DefaultEventDef is { } evt && FormEvents.NameOn(evt, target) != null)
        {
            return evt;
        }

        return target == FormTarget.Web
            ? Events?.FirstOrDefault(e => e.IsWebDefault && FormEvents.NameOn(e, target) != null)
            : null;
    }

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
    //
    // Description strings are reproduced verbatim from dotnet/winforms and dotnet/runtime (MIT, © .NET
    // Foundation and Contributors) — see THIRD-PARTY-NOTICES.md at the repository root.
    //
    // ⛔ Category and Description are WinForms' own (the parity test compares them with the
    // snapshot). A row whose Description differs on one kind gets its OWN definition — the parity
    // run is what finds those (Task 8), never a guess here.
    private static readonly FormPropertyDef Text = new("Text", FormPropertyType.String,
        Category: FormPropertyCategory.Appearance, Description: "The text associated with the control.");

    private static readonly FormPropertyDef Enabled = new("Enabled", FormPropertyType.Bool, "true",
        Category: FormPropertyCategory.Behavior, Description: "Indicates whether the control is enabled.");

    // ⛔ Visible=false is CSS, not a missing element: the element must still exist for getElementById.
    private static readonly FormPropertyDef Visible = new("Visible", FormPropertyType.Bool, "true",
        Category: FormPropertyCategory.Behavior, Description: "Determines whether the control is visible or hidden.",
        CssProperty: "display", CssConverter: FormCssConverter.VisibleToDisplay);

    // ⚠ A ToolStripItem's own Visible — its description differs from Control's (output.txt:1159), and
    // an UNPARENTED item reads Visible=False, which is the parent-dependent measurement the tool cannot
    // judge (spec claim 2 in the plan). The designer's absent-means-visible is WinForms' real default.
    private static readonly FormPropertyDef ItemVisible = new("Visible", FormPropertyType.Bool, "true",
        Category: FormPropertyCategory.Behavior, Description: "Determines whether the item is visible or hidden.",
        CssProperty: "display", CssConverter: FormCssConverter.VisibleToDisplay,
        OracleExemption: "an unparented ToolStripItem reports Visible=False; the item's own visibility " +
                         "state defaults to true, which is what an absent attribute means at run time");

    // ⛔ No static default: WinForms' ForeColor/BackColor are AMBIENT (ShouldSerialize, no
    // [DefaultValue]) — spec §2.7. A kind whose colour is NOT ambient (TextBox's Window) gets its own.
    private static readonly FormPropertyDef ForeColor = new("ForeColor", FormPropertyType.Color,
        Category: FormPropertyCategory.Appearance,
        Description: "The foreground color of this component, which is used to display text.",
        CssProperty: "color", CssConverter: FormCssConverter.Color);

    private static readonly FormPropertyDef BackColor = new("BackColor", FormPropertyType.Color,
        Category: FormPropertyCategory.Appearance, Description: "The background color of the component.",
        CssProperty: "background-color", CssConverter: FormCssConverter.Color);

    // ==================================================================
    // Colours that are NOT ambient on some kinds — found by the parity run (Task 8), never guessed.
    // An edit control or a list paints with the Window/WindowText system colours (WinForms 'reset'),
    // a ProgressBar fills with Highlight. Same CSS as the shared rows, so nothing loses its page
    // declaration; WebDefault is EMPTY because the browser's user-agent sheet — not WinForms — decides
    // what an <input>/<select>/<progress> looks like with no declaration (spec §2.7).
    // ==================================================================
    private static readonly FormPropertyDef WindowTextForeColor = ForeColor with { Default = "WindowText", WebDefault = "" };

    // ⛔ Every kind that uses WindowBackColor (TextBox, ComboBox, ListBox, NumericUpDown, CheckedListBox, ListView, TreeView)
    // THROWS on a translucent BackColor — measured (WinFormsTranslucentBackColorRunTests), so the row says so.
    private static readonly FormPropertyDef WindowBackColor =
        BackColor with { Default = "Window", WebDefault = "", WinFormsTranslucency = FormTranslucency.ThrowsOnControl };

    /// <summary>The plain BackColor of a kind whose WinForms control refuses a translucent one (TrackBar, ProgressBar — measured).</summary>
    private static readonly FormPropertyDef OpaqueBackColor = BackColor with { WinFormsTranslucency = FormTranslucency.ThrowsOnControl };

    private static readonly FormPropertyDef HighlightForeColor = ForeColor with { Default = "Highlight", WebDefault = "" };

    // ⛔ Owner decision O2 (2026-09-29): a colour WinForms marks [Browsable(false)] on a kind is NOT offered — the
    // grid shows what VS shows, on both targets (one vocabulary, D2). The slice-1 HiddenInWinForms rows (PictureBox's
    // ForeColor, DateTimePicker's two, TrackBar's ForeColor, DataGridView's and TabControl's two) were removed; those
    // kinds pass NULL for the missing colour to CommonColoured. WinFormsCatalogParityTests.EveryWinFormsColourRow_…
    // fails if one comes back. A document that still carries such an attribute keeps it as an unknown attribute
    // (preserved, round-tripped, no longer emitted).

    private static readonly FormPropertyDef Checked = new("Checked", FormPropertyType.Bool, "false",
        Category: FormPropertyCategory.Appearance, Description: "Indicates whether the component is in the checked state.");

    // ⚠ RadioButton's own Checked: WinForms describes it differently from CheckBox's (the parity run).
    private static readonly FormPropertyDef RadioChecked = Checked with
    {
        Description = "Indicates whether the radio button is checked or not."
    };

    // ⛔ SelectedIndex is [Browsable(false)] on all four kinds that carry it (measured by reflection,
    // Task 8) — the snapshot is browsable-only, so it reports "no such property". Two reasons, because
    // the kinds offer it for two different reasons.
    private const string SelectedIndexForTheWeb =
        "SelectedIndex is [Browsable(false)] in WinForms — VS does not list it; the designer offers it " +
        "because the web <option selected> needs it (FormAssetEmitter) and WinForms sets it at startup; " +
        "csc gates the name";

    private const string SelectedIndexWinFormsOnly =
        "SelectedIndex is [Browsable(false)] in WinForms — VS does not list it; the designer offers it so " +
        "the form can OPEN on a chosen item/tab, which VS users otherwise set in code after " +
        "InitializeComponent; csc gates the name";

    private static FormPropertyDef SelectedIndex(string reason) => new("SelectedIndex", FormPropertyType.Int, "-1",
        Category: FormPropertyCategory.Behavior,
        Description: "The zero-based index of the item selected when the form opens; -1 selects nothing.",
        OracleExemption: reason);

    // ==================================================================
    // Strip and strip-item rows shared across kinds (Task 24's rows, WinForms' metadata from Task 8).
    // ⛔ Enabled is WinForms-only on these: a browser ignores `disabled` on <nav>/<li>/<span> (spec §4).
    // ==================================================================
    private static readonly FormPropertyDef StripEnabled = Enabled with { Targets = new[] { FormTarget.WinForms } };

    private static FormPropertyDef StripDock(string placementDefault) => new(
        "Dock", FormPropertyType.Enum, placementDefault, new[] { "Top", "Bottom" }, WinFormsEnumType: "DockStyle",
        Category: FormPropertyCategory.Layout,
        Description: "Defines which borders of the control are bound to the container.");

    private static FormPropertyDef ItemText() => new("Text", FormPropertyType.String,
        Category: FormPropertyCategory.Appearance, Description: "The text to display on the item.");

    private static FormPropertyDef ItemToolTipText() => new("ToolTipText", FormPropertyType.String, HtmlAttribute: "title",
        Category: FormPropertyCategory.Behavior, Description: "Specifies the text to show on the ToolTip.");

    private static FormPropertyDef ItemCheckOnClick() => new("CheckOnClick", FormPropertyType.Bool, "false",
        Targets: new[] { FormTarget.WinForms },
        Category: FormPropertyCategory.Behavior,
        Description: "Indicates whether the item should toggle its selected state when clicked.");

    // ⚠ Timer/ToolTip/ErrorProvider-style tray rows are compared like any other; BackgroundWorker is not
    // (see its row): on .NET it carries no [DefaultValue], [Category] or [Description] at all.
    // ⛔ Owner decision O4 (2026-09-29): its descriptions are WinForms' OWN text all the same — .NET Framework 4.8's
    // [Description]s, measured on the owner's machine with TypeDescriptor over System.dll (Windows PowerShell 5.1).
    private const string BackgroundWorkerHasNoMetadata =
        "System.ComponentModel.BackgroundWorker carries no [DefaultValue]/[Category]/[Description] on .NET " +
        "(measured by reflection), so the snapshot's 'serialized'/Misc/empty text records the ABSENCE of " +
        "metadata, not a value. A fresh instance reads False (measured) — what an absent attribute means. The " +
        "description is .NET Framework 4.8's own [Description] for the same property (owner decision O4).";

    // ==================================================================
    // TextAlign — spec §2.8. ONE definition used to serve Label, Button and LinkLabel with default
    // Left and a Left/Center/Right vocabulary, while their WinForms defaults DIFFER (TopLeft,
    // MiddleCenter, TopLeft) — so the grid would have shown a default the program does not run.
    // Now: per-row definitions over the full nine-member ContentAlignment vocabulary, each with its
    // type's WinForms default, and the old words kept as ACCEPTED-but-not-offered aliases so every
    // existing document stays Canon and round-trips byte-for-byte.
    //
    // ⛔ These two fields MUST stay above the TextAlign fields that read them (textual init order).
    // ==================================================================
    private static readonly string[] ContentAlignments =
    {
        "TopLeft", "TopCenter", "TopRight", "MiddleLeft", "MiddleCenter", "MiddleRight",
        "BottomLeft", "BottomCenter", "BottomRight"
    };

    private static readonly IReadOnlyDictionary<string, string> LegacyHorizontalAlign =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Left"] = "MiddleLeft",
            ["Center"] = "MiddleCenter",
            ["Right"] = "MiddleRight"
        };

    private static FormPropertyDef TextAlignDefaulting(string member, string description) => new(
        "TextAlign", FormPropertyType.Enum, member, ContentAlignments,
        WinFormsEnumType: "ContentAlignment",
        Aliases: LegacyHorizontalAlign,
        Category: FormPropertyCategory.Appearance,
        Description: description,
        CssProperty: "text-align",
        CssConverter: FormCssConverter.ContentAlignmentHorizontal);

    private static readonly FormPropertyDef LabelTextAlign =
        TextAlignDefaulting("TopLeft", "Determines the position of the text within the label.");

    private static readonly FormPropertyDef ButtonTextAlign =
        TextAlignDefaulting("MiddleCenter", "The alignment of the text that will be displayed on the control.");

    // ⚠ Identical to LabelTextAlign today (LinkLabel inherits Label.TextAlign) and kept SEPARATE on
    // purpose: parity is judged per row, and if Task 8's run finds LinkLabel's default or description
    // differs, the fix is this one line rather than splitting a shared field under a green suite.
    private static readonly FormPropertyDef LinkLabelTextAlign =
        TextAlignDefaulting("TopLeft", "Determines the position of the text within the label.");

    // ==================================================================
    // Slice 3 — the shared Font / Cursor / Padding rows (spec §2.2, D1). WinForms' own metadata (the snapshot);
    // the web through the named converters in FormCss.
    //
    // ⛔ Font and Cursor are AMBIENT on every kind that has them (no static default — spec §2.7), except where the
    // snapshot says otherwise (TextBox's Cursor resets to IBeam). WinForms hides Font on PictureBox, TrackBar,
    // ProgressBar and DataGridView: those kinds pass NULL for it (ControlRows), exactly as a hidden colour is not
    // offered (owner decision O2) — the parity test names a row the snapshot lacks.
    // ==================================================================
    private static readonly FormPropertyDef FontRow = new("Font", FormPropertyType.Font,
        Category: FormPropertyCategory.Appearance, Description: "The font used to display text in the control.",
        CssProperty: "font", CssConverter: FormCssConverter.Font);

    private static readonly FormPropertyDef CursorRow = new("Cursor", FormPropertyType.Cursor,
        Category: FormPropertyCategory.Appearance,
        Description: "The cursor that appears when the pointer moves over the control.",
        CssProperty: "cursor", CssConverter: FormCssConverter.Cursor);

    // ⚠ Not ambient on a TextBox (the snapshot's 'reset'); on the page an <input> already shows the text cursor, so the
    // browser — not WinForms — decides what an absent value looks like (WebDefault empty).
    private static readonly FormPropertyDef IBeamCursorRow = CursorRow with { Default = "IBeam", WebDefault = "" };

    // ⚠ Content controls only (slice 3 pre-flight §3): a CONTAINER's Padding moves its docked children in WinForms,
    // and FormDockLayout does not model it — so no container carries this row until it does. WebDefault empty: the
    // browser's own padding (a <button>'s is not 0) is what an absent value means on the page.
    private static readonly FormPropertyDef PaddingRow = new("Padding", FormPropertyType.Padding, "0",
        Category: FormPropertyCategory.Layout, Description: "Specifies the interior spacing of a control.",
        CssProperty: "padding", CssConverter: FormCssConverter.Padding, WebDefault: "");

    // ==================================================================
    // D2's web-only extras (spec §1): on EVERY web element kind, added by rule (WithWebExtras) to each row of All that
    // has an HtmlTag — never to a tray component (no element) or a WinForms-only kind.
    // ==================================================================
    private static readonly FormPropertyDef CssClassRow = new("CssClass", FormPropertyType.CssClasses,
        Targets: new[] { FormTarget.Web }, Category: FormPropertyCategory.Appearance,
        Description: "CSS class names added to the element on the page, separated by spaces.");

    // ⚠ The element's style ATTRIBUTE (raw CSS the user wrote), escaped by the emitter as every attribute is — an inline
    // style is exactly "raw CSS" and cannot leave its attribute. It outranks the stylesheet, as inline styles do.
    private static readonly FormPropertyDef StyleRow = new("Style", FormPropertyType.String, HtmlAttribute: "style",
        Targets: new[] { FormTarget.Web }, Category: FormPropertyCategory.Appearance,
        Description: "Extra CSS declarations for the element on the page (its style attribute).");

    // ==================================================================
    // Slice 3 Task 5 — shared D1 rows used by more than one kind. WinForms' own metadata (the snapshot); every one is
    // WinForms-only unless it has a clean page meaning (D2) — the D2 sweep (FormWebVocabularyTests) keeps it that way.
    // ⛔ Methods, not fields: immune to the textual-order initializer trap.
    // ==================================================================
    private static FormPropertyDef LabelAutoSize() => new("AutoSize", FormPropertyType.Bool, "false",
        Targets: new[] { FormTarget.WinForms }, Category: FormPropertyCategory.Layout,
        Description: "Enables automatic resizing based on font size. Note that this is only valid for label controls that do not wrap text.");

    private static FormPropertyDef CheckAlign() => new("CheckAlign", FormPropertyType.Enum, "MiddleLeft", ContentAlignments,
        WinFormsEnumType: "ContentAlignment", Targets: new[] { FormTarget.WinForms },
        Category: FormPropertyCategory.Appearance, Description: "Determines the location of the check box inside the control.");

    private static FormPropertyDef ListSorted() => new("Sorted", FormPropertyType.Bool, "false",
        Targets: new[] { FormTarget.WinForms }, Category: FormPropertyCategory.Behavior,
        Description: "Controls whether the list is sorted.");

    // ⚠ TextBox/NumericUpDown's own TextAlign — WinForms' HorizontalAlignment (Left/Right/Center), NOT ContentAlignment.
    // The page's text-align through the same horizontal converter (it reads the member's Left/Center/Right ending).
    private static FormPropertyDef HorizontalTextAlign(string description) => new("TextAlign", FormPropertyType.Enum, "Left",
        new[] { "Left", "Right", "Center" }, WinFormsEnumType: "HorizontalAlignment",
        Category: FormPropertyCategory.Appearance, Description: description,
        CssProperty: "text-align", CssConverter: FormCssConverter.ContentAlignmentHorizontal);

    /// <summary>
    /// ⚠ An extra is skipped on a kind that already has a row of that NAME: a row is found by name, never by target
    /// (<see cref="FormControlDef.Property"/>), so a second "Style" beside ProgressBar's WinForms <c>ProgressBarStyle</c>
    /// row would make the reader judge the page's raw CSS as that enum. ProgressBar therefore has no raw Style on the web
    /// (<c>FormWebVocabularyTests</c> pins the exception and that no kind carries two rows of one name).
    /// </summary>
    private static FormControlDef WithWebExtras(FormControlDef definition) =>
        definition.HtmlTag == null
            ? definition
            : definition with
            {
                Properties = definition.Properties
                    .Concat(new[] { CssClassRow, StyleRow }.Where(extra => definition.Property(extra.Name) == null))
                    .ToList()
            };

    private static IReadOnlyList<FormPropertyDef> Common(params FormPropertyDef[] own) =>
        CommonColoured(ForeColor, BackColor, own);

    /// <summary>
    /// <see cref="Common"/> with a kind's OWN colour rows — where the parity run showed the colour is not
    /// ambient on that kind (TextBox's Window) — or NULL where WinForms hides it (owner decision O2: not
    /// offered). Same order as <see cref="Common"/>, so the grid does not reshuffle.
    /// </summary>
    private static IReadOnlyList<FormPropertyDef> CommonColoured(
        FormPropertyDef? foreColor, FormPropertyDef? backColor, params FormPropertyDef[] own) =>
        ControlRows(foreColor, backColor, FontRow, CursorRow, own);

    /// <summary>
    /// Every shared row a positioned control carries, each chosen per kind — NULL where WinForms does not browse it
    /// (O2 for colours, the snapshot for Font). Order: the kind's own rows, then Enabled, Visible, the colours, Font,
    /// Cursor.
    /// </summary>
    private static IReadOnlyList<FormPropertyDef> ControlRows(
        FormPropertyDef? foreColor, FormPropertyDef? backColor, FormPropertyDef? font, FormPropertyDef cursor,
        params FormPropertyDef[] own) =>
        own.Concat(new[] { Enabled, Visible, foreColor, backColor, font, cursor }.OfType<FormPropertyDef>()).ToList();

    // Control.Click's WinForms metadata — the most-shared default event (Task 8's parity run).
    private const string ClickedDescription = "Occurs when the component is clicked.";

    // A ToolStripItem's Click and a strip's ItemClicked share this text in WinForms.
    private const string ItemClickedDescription = "Occurs when the item is clicked.";

    // ListBox and CheckedListBox (ComboBox says "combo box").
    private const string ListBoxItemsDescription = "The items in the list box.";

    // ==================================================================
    // Slice 5 D-1 — each kind's event list (pre-flight 2026-10-04 §2 D-1, ADR 0021). The default event comes first; the
    // rest are the kind-specific events a VS user wires plus, from Mouse/Key/Focus, only those WinForms browses on that
    // kind. Name, args and web name were written by hand; Category and Description are WinForms' own (the snapshot,
    // through the parity run). An args type outside System/System.Windows.Forms is written FULLY QUALIFIED — the WinForms
    // scaffold imports nothing else. A null web name means WinForms-only (a web form never shows it); on a kind that is
    // not on the page every web name is null. KeyPress and Enter/Leave carry the WebFilter their page wrapper needs.
    // ⛔ Static METHODS, not fields: immune to the textual-order initializer trap below.
    // ⛔ Panel's Paint is the default (owner decision 2026-09-29: VS opens it) and Click is its declared web default.
    // ==================================================================

    // ⛔ GroupBox.Click is [Browsable(false)]; kept so existing documents that bind it keep working.
    private static FormEventDef GroupBoxClick() => new("Click", WebEvent: "click", Category: FormEventCategory.Action,
        Description: ClickedDescription,
        OracleExemption: "GroupBox.Click is [Browsable(false)] in WinForms — VS does not list it — but a " +
                         "real click raises it (measured by reflection and a simulated WM_LBUTTONDOWN/UP, " +
                         "Task 8); kept as a non-default event so documents that bind it keep working; " +
                         "csc gates the name");

    // Owner decision O4 (2026-09-29) extended to every BackgroundWorker event: .NET Framework 4.8's own [Description]s,
    // measured 2026-10-04 with TypeDescriptor over System.dll (Windows PowerShell 5.1).
    private const string BackgroundWorkerEventHasNoMetadata =
        "System.ComponentModel.BackgroundWorker carries no [Category]/[Description] on .NET " +
        "(measured), so the snapshot's Misc/empty text records the absence of metadata; the " +
        "description is .NET Framework 4.8's own [Description] (owner decision O4). The args are " +
        "still WinForms'.";

    /// <summary>
    /// KeyDown, KeyUp, KeyPress — WinForms' own text, the same on every kind that browses them (review fix 5: one copy,
    /// not one per kind). <paramref name="web"/>: the kind exists on the page, so they carry their DOM names, and
    /// KeyPress its <see cref="FormWebFilter.KeyPressKeys"/> wrapper (ADR 0021). ⛔ Order matters only for reading;
    /// the raise order on the page is RegionWriter's.
    /// </summary>
    private static FormEventDef[] KeyEvents(bool web) =>
    [
        new FormEventDef("KeyDown", "KeyEventArgs", web ? "keydown" : null, FormEventCategory.Key, "Occurs when a key is first pressed."),
        new FormEventDef("KeyUp", "KeyEventArgs", web ? "keyup" : null, FormEventCategory.Key, "Occurs when a key is released."),
        new FormEventDef("KeyPress", "KeyPressEventArgs", web ? "keypress" : null, FormEventCategory.Key,
            "Occurs when the control has focus and the user presses and releases a key.",
            WebFilter: web ? FormWebFilter.KeyPressKeys : FormWebFilter.None)
    ];

    /// <summary>Enter, Leave — on the page the <see cref="FormWebFilter.FromOutside"/> <c>focusin</c>/<c>focusout</c>.</summary>
    private static FormEventDef[] FocusEvents(bool web) =>
    [
        new FormEventDef("Enter", null, web ? "focusin" : null, FormEventCategory.Focus,
            "Occurs when the control becomes the active control of the form.",
            WebFilter: web ? FormWebFilter.FromOutside : FormWebFilter.None),
        new FormEventDef("Leave", null, web ? "focusout" : null, FormEventCategory.Focus,
            "Occurs when the control is no longer the active control of the form.",
            WebFilter: web ? FormWebFilter.FromOutside : FormWebFilter.None)
    ];

    private static IReadOnlyList<FormEventDef> LabelEvents() =>
    [
        new FormEventDef("Click", null, "click", FormEventCategory.Action, "Occurs when the component is clicked.", IsDefault: true),
        new FormEventDef("DoubleClick", null, "dblclick", FormEventCategory.Action, "Occurs when the component is double-clicked."),
        new FormEventDef("MouseDown", "MouseEventArgs", "mousedown", FormEventCategory.Mouse, "Occurs when the mouse pointer is over the component and a mouse button is pressed."),
        new FormEventDef("MouseUp", "MouseEventArgs", "mouseup", FormEventCategory.Mouse, "Occurs when the mouse pointer is over the component and a mouse button is released."),
        new FormEventDef("MouseMove", "MouseEventArgs", "mousemove", FormEventCategory.Mouse, "Occurs when the mouse pointer is moved over the component."),
        new FormEventDef("MouseEnter", null, "mouseenter", FormEventCategory.Mouse, "Occurs when the mouse enters the visible part of the control."),
        new FormEventDef("MouseLeave", null, "mouseleave", FormEventCategory.Mouse, "Occurs when the mouse leaves the visible part of the control."),
        new FormEventDef("TextChanged", null, null, FormEventCategory.PropertyChanged, "Event raised when the value of the Text property is changed on Control."),
        new FormEventDef("Paint", "PaintEventArgs", null, FormEventCategory.Appearance, "Occurs when a control needs repainting.")
    ];

    private static IReadOnlyList<FormEventDef> TextBoxEvents() =>
    [
        new FormEventDef("TextChanged", null, "input", FormEventCategory.PropertyChanged, "Event raised when the value of the Text property is changed on Control.", IsDefault: true),
        new FormEventDef("Click", null, "click", FormEventCategory.Action, "Occurs when the component is clicked."),
        new FormEventDef("DoubleClick", null, "dblclick", FormEventCategory.Action, "Occurs when the component is double-clicked."),
        new FormEventDef("MouseDown", "MouseEventArgs", "mousedown", FormEventCategory.Mouse, "Occurs when the mouse pointer is over the component and a mouse button is pressed."),
        new FormEventDef("MouseUp", "MouseEventArgs", "mouseup", FormEventCategory.Mouse, "Occurs when the mouse pointer is over the component and a mouse button is released."),
        new FormEventDef("MouseMove", "MouseEventArgs", "mousemove", FormEventCategory.Mouse, "Occurs when the mouse pointer is moved over the component."),
        new FormEventDef("MouseEnter", null, "mouseenter", FormEventCategory.Mouse, "Occurs when the mouse enters the visible part of the control."),
        new FormEventDef("MouseLeave", null, "mouseleave", FormEventCategory.Mouse, "Occurs when the mouse leaves the visible part of the control."),
        ..KeyEvents(web: true),
        ..FocusEvents(web: true),
        new FormEventDef("Validating", "System.ComponentModel.CancelEventArgs", null, FormEventCategory.Focus, "Occurs when the control is validating."),
        new FormEventDef("Validated", null, null, FormEventCategory.Focus, "Occurs after a control has been successfully validated.")
    ];

    private static IReadOnlyList<FormEventDef> ButtonEvents() =>
    [
        new FormEventDef("Click", null, "click", FormEventCategory.Action, "Occurs when the component is clicked.", IsDefault: true),
        new FormEventDef("MouseDown", "MouseEventArgs", "mousedown", FormEventCategory.Mouse, "Occurs when the mouse pointer is over the component and a mouse button is pressed."),
        new FormEventDef("MouseUp", "MouseEventArgs", "mouseup", FormEventCategory.Mouse, "Occurs when the mouse pointer is over the component and a mouse button is released."),
        new FormEventDef("MouseMove", "MouseEventArgs", "mousemove", FormEventCategory.Mouse, "Occurs when the mouse pointer is moved over the component."),
        new FormEventDef("MouseEnter", null, "mouseenter", FormEventCategory.Mouse, "Occurs when the mouse enters the visible part of the control."),
        new FormEventDef("MouseLeave", null, "mouseleave", FormEventCategory.Mouse, "Occurs when the mouse leaves the visible part of the control."),
        ..KeyEvents(web: true),
        ..FocusEvents(web: true),
        new FormEventDef("TextChanged", null, null, FormEventCategory.PropertyChanged, "Event raised when the value of the Text property is changed on Control."),
        new FormEventDef("Paint", "PaintEventArgs", null, FormEventCategory.Appearance, "Occurs when a control needs repainting.")
    ];

    private static IReadOnlyList<FormEventDef> CheckBoxEvents() =>
    [
        new FormEventDef("CheckedChanged", null, "change", FormEventCategory.Misc, "Occurs whenever the Check property is changed.", IsDefault: true),
        new FormEventDef("CheckStateChanged", null, null, FormEventCategory.Misc, "Occurs whenever the CheckState property is changed."),
        new FormEventDef("Click", null, "click", FormEventCategory.Action, "Occurs when the component is clicked."),
        new FormEventDef("MouseDown", "MouseEventArgs", "mousedown", FormEventCategory.Mouse, "Occurs when the mouse pointer is over the component and a mouse button is pressed."),
        new FormEventDef("MouseUp", "MouseEventArgs", "mouseup", FormEventCategory.Mouse, "Occurs when the mouse pointer is over the component and a mouse button is released."),
        new FormEventDef("MouseMove", "MouseEventArgs", "mousemove", FormEventCategory.Mouse, "Occurs when the mouse pointer is moved over the component."),
        new FormEventDef("MouseEnter", null, "mouseenter", FormEventCategory.Mouse, "Occurs when the mouse enters the visible part of the control."),
        new FormEventDef("MouseLeave", null, "mouseleave", FormEventCategory.Mouse, "Occurs when the mouse leaves the visible part of the control."),
        ..KeyEvents(web: true),
        ..FocusEvents(web: true)
    ];

    private static IReadOnlyList<FormEventDef> RadioButtonEvents() =>
    [
        new FormEventDef("CheckedChanged", null, "change", FormEventCategory.Misc, "Occurs whenever the 'checked' property changes value.", IsDefault: true),
        new FormEventDef("Click", null, "click", FormEventCategory.Action, "Occurs when the component is clicked."),
        new FormEventDef("MouseDown", "MouseEventArgs", "mousedown", FormEventCategory.Mouse, "Occurs when the mouse pointer is over the component and a mouse button is pressed."),
        new FormEventDef("MouseUp", "MouseEventArgs", "mouseup", FormEventCategory.Mouse, "Occurs when the mouse pointer is over the component and a mouse button is released."),
        new FormEventDef("MouseEnter", null, "mouseenter", FormEventCategory.Mouse, "Occurs when the mouse enters the visible part of the control."),
        new FormEventDef("MouseLeave", null, "mouseleave", FormEventCategory.Mouse, "Occurs when the mouse leaves the visible part of the control."),
        ..KeyEvents(web: true),
        ..FocusEvents(web: true)
    ];

    private static IReadOnlyList<FormEventDef> ComboBoxEvents() =>
    [
        new FormEventDef("SelectedIndexChanged", null, "change", FormEventCategory.Behavior, "Occurs when the value of the SelectedIndex property changes.", IsDefault: true),
        new FormEventDef("SelectedValueChanged", null, null, FormEventCategory.PropertyChanged, "Event raised when the value of the SelectedValue property is changed on ListControl."),
        new FormEventDef("DropDown", null, null, FormEventCategory.Behavior, "Occurs when the drop-down portion of the combo box is shown."),
        new FormEventDef("DropDownClosed", null, null, FormEventCategory.Behavior, "Indicates that the drop-down portion of the combo box has closed."),
        new FormEventDef("TextChanged", null, null, FormEventCategory.PropertyChanged, "Event raised when the value of the Text property is changed on Control."),
        new FormEventDef("Click", null, "click", FormEventCategory.Action, "Occurs when the component is clicked."),
        new FormEventDef("MouseDown", "MouseEventArgs", "mousedown", FormEventCategory.Mouse, "Occurs when the mouse pointer is over the component and a mouse button is pressed."),
        new FormEventDef("MouseUp", "MouseEventArgs", "mouseup", FormEventCategory.Mouse, "Occurs when the mouse pointer is over the component and a mouse button is released."),
        new FormEventDef("MouseEnter", null, "mouseenter", FormEventCategory.Mouse, "Occurs when the mouse enters the visible part of the control."),
        new FormEventDef("MouseLeave", null, "mouseleave", FormEventCategory.Mouse, "Occurs when the mouse leaves the visible part of the control."),
        ..KeyEvents(web: true),
        ..FocusEvents(web: true)
    ];

    private static IReadOnlyList<FormEventDef> ListBoxEvents() =>
    [
        new FormEventDef("SelectedIndexChanged", null, "change", FormEventCategory.Behavior, "Occurs when the value of the SelectedIndex property changes.", IsDefault: true),
        new FormEventDef("SelectedValueChanged", null, null, FormEventCategory.PropertyChanged, "Event raised when the value of the SelectedValue property is changed on ListControl."),
        new FormEventDef("Click", null, "click", FormEventCategory.Action, "Occurs when the component is clicked."),
        new FormEventDef("DoubleClick", null, "dblclick", FormEventCategory.Action, "Occurs when the component is double-clicked."),
        new FormEventDef("MouseDown", "MouseEventArgs", "mousedown", FormEventCategory.Mouse, "Occurs when the mouse pointer is over the component and a mouse button is pressed."),
        new FormEventDef("MouseUp", "MouseEventArgs", "mouseup", FormEventCategory.Mouse, "Occurs when the mouse pointer is over the component and a mouse button is released."),
        new FormEventDef("MouseMove", "MouseEventArgs", "mousemove", FormEventCategory.Mouse, "Occurs when the mouse pointer is moved over the component."),
        new FormEventDef("MouseEnter", null, "mouseenter", FormEventCategory.Mouse, "Occurs when the mouse enters the visible part of the control."),
        new FormEventDef("MouseLeave", null, "mouseleave", FormEventCategory.Mouse, "Occurs when the mouse leaves the visible part of the control."),
        ..KeyEvents(web: true),
        ..FocusEvents(web: true)
    ];

    private static IReadOnlyList<FormEventDef> PanelEvents() =>
    [
        new FormEventDef("Paint", "PaintEventArgs", null, FormEventCategory.Appearance, "Occurs when a control needs repainting.", IsDefault: true),
        new FormEventDef("Click", null, "click", FormEventCategory.Action, "Occurs when the component is clicked.", IsWebDefault: true),
        new FormEventDef("DoubleClick", null, "dblclick", FormEventCategory.Action, "Occurs when the component is double-clicked."),
        new FormEventDef("MouseDown", "MouseEventArgs", "mousedown", FormEventCategory.Mouse, "Occurs when the mouse pointer is over the component and a mouse button is pressed."),
        new FormEventDef("MouseUp", "MouseEventArgs", "mouseup", FormEventCategory.Mouse, "Occurs when the mouse pointer is over the component and a mouse button is released."),
        new FormEventDef("MouseMove", "MouseEventArgs", "mousemove", FormEventCategory.Mouse, "Occurs when the mouse pointer is moved over the component."),
        new FormEventDef("MouseEnter", null, "mouseenter", FormEventCategory.Mouse, "Occurs when the mouse enters the visible part of the control."),
        new FormEventDef("MouseLeave", null, "mouseleave", FormEventCategory.Mouse, "Occurs when the mouse leaves the visible part of the control."),
        ..FocusEvents(web: true),
        new FormEventDef("Resize", null, null, FormEventCategory.Layout, "Occurs when a control is resized."),
        new FormEventDef("Scroll", "ScrollEventArgs", null, FormEventCategory.Action, "Occurs when the user moves the scroll box.")
    ];

    private static IReadOnlyList<FormEventDef> GroupBoxEvents() =>
    [
        new FormEventDef("Enter", null, "focusin", FormEventCategory.Focus, "Occurs when the control becomes the active control of the form.", IsDefault: true, WebFilter: FormWebFilter.FromOutside),
        new FormEventDef("Leave", null, "focusout", FormEventCategory.Focus, "Occurs when the control is no longer the active control of the form.", WebFilter: FormWebFilter.FromOutside),
        GroupBoxClick(),
        new FormEventDef("TextChanged", null, null, FormEventCategory.PropertyChanged, "Event raised when the value of the Text property is changed on Control."),
        new FormEventDef("Paint", "PaintEventArgs", null, FormEventCategory.Appearance, "Occurs when a control needs repainting."),
        new FormEventDef("Resize", null, null, FormEventCategory.Layout, "Occurs when a control is resized.")
    ];

    private static IReadOnlyList<FormEventDef> PictureBoxEvents() =>
    [
        new FormEventDef("Click", null, "click", FormEventCategory.Action, "Occurs when the component is clicked.", IsDefault: true),
        new FormEventDef("DoubleClick", null, "dblclick", FormEventCategory.Action, "Occurs when the component is double-clicked."),
        new FormEventDef("MouseDown", "MouseEventArgs", "mousedown", FormEventCategory.Mouse, "Occurs when the mouse pointer is over the component and a mouse button is pressed."),
        new FormEventDef("MouseUp", "MouseEventArgs", "mouseup", FormEventCategory.Mouse, "Occurs when the mouse pointer is over the component and a mouse button is released."),
        new FormEventDef("MouseMove", "MouseEventArgs", "mousemove", FormEventCategory.Mouse, "Occurs when the mouse pointer is moved over the component."),
        new FormEventDef("MouseEnter", null, "mouseenter", FormEventCategory.Mouse, "Occurs when the mouse enters the visible part of the control."),
        new FormEventDef("MouseLeave", null, "mouseleave", FormEventCategory.Mouse, "Occurs when the mouse leaves the visible part of the control."),
        new FormEventDef("Paint", "PaintEventArgs", null, FormEventCategory.Appearance, "Occurs when a control needs repainting."),
        new FormEventDef("Resize", null, null, FormEventCategory.Layout, "Occurs when a control is resized."),
        new FormEventDef("LoadCompleted", "System.ComponentModel.AsyncCompletedEventArgs", null, FormEventCategory.Asynchronous, "Event raised when loading into a PictureBox finishes.")
    ];

    private static IReadOnlyList<FormEventDef> LinkLabelEvents() =>
    [
        new FormEventDef("LinkClicked", "LinkLabelLinkClickedEventArgs", "click", FormEventCategory.Action, "Occurs when the link is clicked.", IsDefault: true),
        new FormEventDef("Click", null, null, FormEventCategory.Action, "Occurs when the component is clicked."),
        new FormEventDef("DoubleClick", null, "dblclick", FormEventCategory.Action, "Occurs when the component is double-clicked."),
        new FormEventDef("MouseDown", "MouseEventArgs", "mousedown", FormEventCategory.Mouse, "Occurs when the mouse pointer is over the component and a mouse button is pressed."),
        new FormEventDef("MouseUp", "MouseEventArgs", "mouseup", FormEventCategory.Mouse, "Occurs when the mouse pointer is over the component and a mouse button is released."),
        new FormEventDef("MouseMove", "MouseEventArgs", "mousemove", FormEventCategory.Mouse, "Occurs when the mouse pointer is moved over the component."),
        new FormEventDef("MouseEnter", null, "mouseenter", FormEventCategory.Mouse, "Occurs when the mouse enters the visible part of the control."),
        new FormEventDef("MouseLeave", null, "mouseleave", FormEventCategory.Mouse, "Occurs when the mouse leaves the visible part of the control."),
        new FormEventDef("TextChanged", null, null, FormEventCategory.PropertyChanged, "Event raised when the value of the Text property is changed on Control.")
    ];

    private static IReadOnlyList<FormEventDef> NumericUpDownEvents() =>
    [
        new FormEventDef("ValueChanged", null, "input", FormEventCategory.Action, "Occurs when the value in the up-down control changes.", IsDefault: true),
        new FormEventDef("Click", null, "click", FormEventCategory.Action, "Occurs when the component is clicked."),
        new FormEventDef("DoubleClick", null, "dblclick", FormEventCategory.Action, "Occurs when the component is double-clicked."),
        new FormEventDef("MouseDown", "MouseEventArgs", "mousedown", FormEventCategory.Mouse, "Occurs when the mouse pointer is over the component and a mouse button is pressed."),
        new FormEventDef("MouseUp", "MouseEventArgs", "mouseup", FormEventCategory.Mouse, "Occurs when the mouse pointer is over the component and a mouse button is released."),
        ..KeyEvents(web: true),
        ..FocusEvents(web: true),
        new FormEventDef("Validating", "System.ComponentModel.CancelEventArgs", null, FormEventCategory.Focus, "Occurs when the control is validating."),
        new FormEventDef("Validated", null, null, FormEventCategory.Focus, "Occurs after a control has been successfully validated.")
    ];

    private static IReadOnlyList<FormEventDef> DateTimePickerEvents() =>
    [
        new FormEventDef("ValueChanged", null, "change", FormEventCategory.Action, "Occurs when the value of the control changes.", IsDefault: true),
        new FormEventDef("DropDown", null, null, FormEventCategory.Action, "Occurs when the drop-down calendar is about to drop."),
        new FormEventDef("CloseUp", null, null, FormEventCategory.Action, "Occurs when the user is finished selecting a date from the drop-down calendar."),
        new FormEventDef("MouseDown", "MouseEventArgs", "mousedown", FormEventCategory.Mouse, "Occurs when the mouse pointer is over the component and a mouse button is pressed."),
        new FormEventDef("MouseUp", "MouseEventArgs", "mouseup", FormEventCategory.Mouse, "Occurs when the mouse pointer is over the component and a mouse button is released."),
        new FormEventDef("MouseEnter", null, "mouseenter", FormEventCategory.Mouse, "Occurs when the mouse enters the visible part of the control."),
        new FormEventDef("MouseLeave", null, "mouseleave", FormEventCategory.Mouse, "Occurs when the mouse leaves the visible part of the control."),
        ..KeyEvents(web: true),
        ..FocusEvents(web: true),
        new FormEventDef("Validating", "System.ComponentModel.CancelEventArgs", null, FormEventCategory.Focus, "Occurs when the control is validating.")
    ];

    private static IReadOnlyList<FormEventDef> TrackBarEvents() =>
    [
        new FormEventDef("Scroll", null, "input", FormEventCategory.Behavior, "Occurs when the TrackBar slider moves.", IsDefault: true),
        new FormEventDef("ValueChanged", null, "change", FormEventCategory.Action, "Occurs when the value of the control changes."),
        new FormEventDef("MouseDown", "MouseEventArgs", "mousedown", FormEventCategory.Mouse, "Occurs when the mouse pointer is over the component and a mouse button is pressed."),
        new FormEventDef("MouseUp", "MouseEventArgs", "mouseup", FormEventCategory.Mouse, "Occurs when the mouse pointer is over the component and a mouse button is released."),
        new FormEventDef("MouseMove", "MouseEventArgs", "mousemove", FormEventCategory.Mouse, "Occurs when the mouse pointer is moved over the component."),
        new FormEventDef("MouseEnter", null, "mouseenter", FormEventCategory.Mouse, "Occurs when the mouse enters the visible part of the control."),
        new FormEventDef("MouseLeave", null, "mouseleave", FormEventCategory.Mouse, "Occurs when the mouse leaves the visible part of the control."),
        ..KeyEvents(web: true),
        ..FocusEvents(web: true)
    ];

    private static IReadOnlyList<FormEventDef> ProgressBarEvents() =>
    [
        new FormEventDef("Click", null, "click", FormEventCategory.Action, "Occurs when the component is clicked.", IsDefault: true),
        new FormEventDef("MouseDown", "MouseEventArgs", "mousedown", FormEventCategory.Mouse, "Occurs when the mouse pointer is over the component and a mouse button is pressed."),
        new FormEventDef("MouseUp", "MouseEventArgs", "mouseup", FormEventCategory.Mouse, "Occurs when the mouse pointer is over the component and a mouse button is released."),
        new FormEventDef("MouseMove", "MouseEventArgs", "mousemove", FormEventCategory.Mouse, "Occurs when the mouse pointer is moved over the component."),
        new FormEventDef("MouseEnter", null, "mouseenter", FormEventCategory.Mouse, "Occurs when the mouse enters the visible part of the control."),
        new FormEventDef("MouseLeave", null, "mouseleave", FormEventCategory.Mouse, "Occurs when the mouse leaves the visible part of the control."),
        new FormEventDef("Resize", null, null, FormEventCategory.Layout, "Occurs when a control is resized.")
    ];

    private static IReadOnlyList<FormEventDef> CheckedListBoxEvents() =>
    [
        new FormEventDef("SelectedIndexChanged", null, null, FormEventCategory.Behavior, "Occurs when the value of the SelectedIndex property changes.", IsDefault: true),
        new FormEventDef("ItemCheck", "ItemCheckEventArgs", null, FormEventCategory.Behavior, "Indicates that an item is about to have its checked state changed. The value is not updated until after the event occurs."),
        new FormEventDef("SelectedValueChanged", null, null, FormEventCategory.PropertyChanged, "Event raised when the value of the SelectedValue property is changed on ListControl."),
        new FormEventDef("Click", null, null, FormEventCategory.Action, "Occurs when the component is clicked."),
        new FormEventDef("DoubleClick", null, null, FormEventCategory.Action, "Occurs when the component is double-clicked."),
        new FormEventDef("MouseDown", "MouseEventArgs", null, FormEventCategory.Mouse, "Occurs when the mouse pointer is over the component and a mouse button is pressed."),
        new FormEventDef("MouseUp", "MouseEventArgs", null, FormEventCategory.Mouse, "Occurs when the mouse pointer is over the component and a mouse button is released."),
        ..KeyEvents(web: false),
        ..FocusEvents(web: false)
    ];

    private static IReadOnlyList<FormEventDef> ListViewEvents() =>
    [
        new FormEventDef("SelectedIndexChanged", null, null, FormEventCategory.Behavior, "Occurs whenever the 'SelectedIndex' property for this ListView changes.", IsDefault: true),
        new FormEventDef("ItemActivate", null, null, FormEventCategory.Action, "Occurs when an item is activated."),
        new FormEventDef("ItemSelectionChanged", "ListViewItemSelectionChangedEventArgs", null, FormEventCategory.Behavior, "Event raised when the selection state of an item has changed."),
        new FormEventDef("ItemCheck", "ItemCheckEventArgs", null, FormEventCategory.Behavior, "Indicates that an item is about to have its checked state changed. The value is not updated until after the event occurs."),
        new FormEventDef("ItemChecked", "ItemCheckedEventArgs", null, FormEventCategory.Behavior, "Event raised when the checked property of a ListView item changes."),
        new FormEventDef("ColumnClick", "ColumnClickEventArgs", null, FormEventCategory.Action, "Occurs when a column header is clicked."),
        new FormEventDef("Click", null, null, FormEventCategory.Action, "Occurs when the component is clicked."),
        new FormEventDef("DoubleClick", null, null, FormEventCategory.Action, "Occurs when the component is double-clicked."),
        new FormEventDef("MouseDown", "MouseEventArgs", null, FormEventCategory.Mouse, "Occurs when the mouse pointer is over the component and a mouse button is pressed."),
        new FormEventDef("MouseUp", "MouseEventArgs", null, FormEventCategory.Mouse, "Occurs when the mouse pointer is over the component and a mouse button is released."),
        new FormEventDef("KeyDown", "KeyEventArgs", null, FormEventCategory.Key, "Occurs when a key is first pressed."),
        new FormEventDef("KeyUp", "KeyEventArgs", null, FormEventCategory.Key, "Occurs when a key is released."),
        ..FocusEvents(web: false)
    ];

    private static IReadOnlyList<FormEventDef> TreeViewEvents() =>
    [
        new FormEventDef("AfterSelect", "TreeViewEventArgs", null, FormEventCategory.Behavior, "Occurs when the selection has been changed.", IsDefault: true),
        new FormEventDef("BeforeSelect", "TreeViewCancelEventArgs", null, FormEventCategory.Behavior, "Occurs when the selection is about to change."),
        new FormEventDef("AfterCheck", "TreeViewEventArgs", null, FormEventCategory.Behavior, "Occurs when a check box on a tree node has been checked or unchecked."),
        new FormEventDef("BeforeExpand", "TreeViewCancelEventArgs", null, FormEventCategory.Behavior, "Occurs when a node is about to be expanded."),
        new FormEventDef("AfterExpand", "TreeViewEventArgs", null, FormEventCategory.Behavior, "Occurs when a node has been expanded."),
        new FormEventDef("AfterCollapse", "TreeViewEventArgs", null, FormEventCategory.Behavior, "Occurs when a node has been collapsed."),
        new FormEventDef("NodeMouseClick", "TreeNodeMouseClickEventArgs", null, FormEventCategory.Behavior, "Occurs when a node is clicked with the mouse."),
        new FormEventDef("NodeMouseDoubleClick", "TreeNodeMouseClickEventArgs", null, FormEventCategory.Behavior, "Occurs when a node is double-clicked with the mouse."),
        new FormEventDef("Click", null, null, FormEventCategory.Action, "Occurs when the component is clicked."),
        new FormEventDef("DoubleClick", null, null, FormEventCategory.Action, "Occurs when the component is double-clicked."),
        new FormEventDef("KeyDown", "KeyEventArgs", null, FormEventCategory.Key, "Occurs when a key is first pressed."),
        new FormEventDef("KeyUp", "KeyEventArgs", null, FormEventCategory.Key, "Occurs when a key is released."),
        ..FocusEvents(web: false)
    ];

    private static IReadOnlyList<FormEventDef> DataGridViewEvents() =>
    [
        new FormEventDef("CellContentClick", "DataGridViewCellEventArgs", null, FormEventCategory.Mouse, "Occurs when the content within a cell is clicked.", IsDefault: true),
        new FormEventDef("CellClick", "DataGridViewCellEventArgs", null, FormEventCategory.Mouse, "Occurs when any part of the cell is clicked."),
        new FormEventDef("CellDoubleClick", "DataGridViewCellEventArgs", null, FormEventCategory.Mouse, "Occurs when the user double-clicks anywhere in a cell."),
        new FormEventDef("CellContentDoubleClick", "DataGridViewCellEventArgs", null, FormEventCategory.Mouse, "Occurs when a user double-clicks a cell's contents."),
        new FormEventDef("CellValueChanged", "DataGridViewCellEventArgs", null, FormEventCategory.Action, "Occurs when the value of a cell changes."),
        new FormEventDef("CellBeginEdit", "DataGridViewCellCancelEventArgs", null, FormEventCategory.Data, "Occurs when edit mode starts for the selected cell."),
        new FormEventDef("CellEndEdit", "DataGridViewCellEventArgs", null, FormEventCategory.Data, "Occurs when edit mode stops for the currently selected cell."),
        new FormEventDef("CellValidating", "DataGridViewCellValidatingEventArgs", null, FormEventCategory.Focus, "Occurs when the cell is validating."),
        new FormEventDef("CellFormatting", "DataGridViewCellFormattingEventArgs", null, FormEventCategory.Display, "Occurs when the contents of a cell need to be formatted for display."),
        new FormEventDef("CellMouseClick", "DataGridViewCellMouseEventArgs", null, FormEventCategory.Mouse, "Occurs whenever a mouse clicks anywhere on a cell."),
        new FormEventDef("SelectionChanged", null, null, FormEventCategory.Action, "Occurs when the current selection changes."),
        new FormEventDef("RowEnter", "DataGridViewCellEventArgs", null, FormEventCategory.Focus, "Occurs when a row receives input focus and becomes the current row."),
        new FormEventDef("DataError", "DataGridViewDataErrorEventArgs", null, FormEventCategory.Behavior, "Occurs when an external data-parsing or validation operation throws an exception, or when an attempt to commit data to a data source does not succeed."),
        new FormEventDef("KeyDown", "KeyEventArgs", null, FormEventCategory.Key, "Occurs when a key is first pressed.")
    ];

    private static IReadOnlyList<FormEventDef> TabControlEvents() =>
    [
        new FormEventDef("SelectedIndexChanged", null, null, FormEventCategory.Behavior, "Occurs when the value of the SelectedIndex property changes.", IsDefault: true),
        new FormEventDef("Selected", "TabControlEventArgs", null, FormEventCategory.Action, "Occurs after a tab page is selected as the topmost tab page."),
        new FormEventDef("Selecting", "TabControlCancelEventArgs", null, FormEventCategory.Action, "Occurs when a tab page is being selected."),
        new FormEventDef("Deselecting", "TabControlCancelEventArgs", null, FormEventCategory.Action, "Occurs when a tab page is being deselected."),
        new FormEventDef("Click", null, null, FormEventCategory.Action, "Occurs when the component is clicked."),
        new FormEventDef("DoubleClick", null, null, FormEventCategory.Action, "Occurs when the component is double-clicked."),
        new FormEventDef("MouseDown", "MouseEventArgs", null, FormEventCategory.Mouse, "Occurs when the mouse pointer is over the component and a mouse button is pressed."),
        new FormEventDef("MouseUp", "MouseEventArgs", null, FormEventCategory.Mouse, "Occurs when the mouse pointer is over the component and a mouse button is released."),
        new FormEventDef("KeyDown", "KeyEventArgs", null, FormEventCategory.Key, "Occurs when a key is first pressed."),
        new FormEventDef("KeyUp", "KeyEventArgs", null, FormEventCategory.Key, "Occurs when a key is released."),
        ..FocusEvents(web: false)
    ];

    private static IReadOnlyList<FormEventDef> SplitContainerEvents() =>
    [
        new FormEventDef("SplitterMoved", "SplitterEventArgs", null, FormEventCategory.Behavior, "Occurs when the splitter is done being moved.", IsDefault: true),
        new FormEventDef("SplitterMoving", "SplitterCancelEventArgs", null, FormEventCategory.Behavior, "Occurs when the splitter is being moved."),
        new FormEventDef("Paint", "PaintEventArgs", null, FormEventCategory.Appearance, "Occurs when a control needs repainting."),
        new FormEventDef("Click", null, null, FormEventCategory.Action, "Occurs when the component is clicked."),
        new FormEventDef("DoubleClick", null, null, FormEventCategory.Action, "Occurs when the component is double-clicked."),
        new FormEventDef("MouseDown", "MouseEventArgs", null, FormEventCategory.Mouse, "Occurs when the mouse pointer is over the component and a mouse button is pressed."),
        new FormEventDef("MouseUp", "MouseEventArgs", null, FormEventCategory.Mouse, "Occurs when the mouse pointer is over the component and a mouse button is released."),
        new FormEventDef("MouseMove", "MouseEventArgs", null, FormEventCategory.Mouse, "Occurs when the mouse pointer is moved over the component."),
        new FormEventDef("Resize", null, null, FormEventCategory.Layout, "Occurs when a control is resized."),
        ..FocusEvents(web: false)
    ];

    private static IReadOnlyList<FormEventDef> FlowLayoutPanelEvents() =>
    [
        new FormEventDef("Paint", "PaintEventArgs", null, FormEventCategory.Appearance, "Occurs when a control needs repainting.", IsDefault: true),
        new FormEventDef("Click", null, null, FormEventCategory.Action, "Occurs when the component is clicked."),
        new FormEventDef("DoubleClick", null, null, FormEventCategory.Action, "Occurs when the component is double-clicked."),
        new FormEventDef("MouseDown", "MouseEventArgs", null, FormEventCategory.Mouse, "Occurs when the mouse pointer is over the component and a mouse button is pressed."),
        new FormEventDef("MouseUp", "MouseEventArgs", null, FormEventCategory.Mouse, "Occurs when the mouse pointer is over the component and a mouse button is released."),
        new FormEventDef("MouseMove", "MouseEventArgs", null, FormEventCategory.Mouse, "Occurs when the mouse pointer is moved over the component."),
        new FormEventDef("MouseEnter", null, null, FormEventCategory.Mouse, "Occurs when the mouse enters the visible part of the control."),
        new FormEventDef("MouseLeave", null, null, FormEventCategory.Mouse, "Occurs when the mouse leaves the visible part of the control."),
        new FormEventDef("Resize", null, null, FormEventCategory.Layout, "Occurs when a control is resized."),
        new FormEventDef("Scroll", "ScrollEventArgs", null, FormEventCategory.Action, "Occurs when the user moves the scroll box."),
        ..FocusEvents(web: false)
    ];

    private static IReadOnlyList<FormEventDef> TableLayoutPanelEvents() =>
    [
        new FormEventDef("Paint", "PaintEventArgs", null, FormEventCategory.Appearance, "Occurs when a control needs repainting.", IsDefault: true),
        new FormEventDef("Click", null, null, FormEventCategory.Action, "Occurs when the component is clicked."),
        new FormEventDef("DoubleClick", null, null, FormEventCategory.Action, "Occurs when the component is double-clicked."),
        new FormEventDef("MouseDown", "MouseEventArgs", null, FormEventCategory.Mouse, "Occurs when the mouse pointer is over the component and a mouse button is pressed."),
        new FormEventDef("MouseUp", "MouseEventArgs", null, FormEventCategory.Mouse, "Occurs when the mouse pointer is over the component and a mouse button is released."),
        new FormEventDef("MouseMove", "MouseEventArgs", null, FormEventCategory.Mouse, "Occurs when the mouse pointer is moved over the component."),
        new FormEventDef("MouseEnter", null, null, FormEventCategory.Mouse, "Occurs when the mouse enters the visible part of the control."),
        new FormEventDef("MouseLeave", null, null, FormEventCategory.Mouse, "Occurs when the mouse leaves the visible part of the control."),
        new FormEventDef("Resize", null, null, FormEventCategory.Layout, "Occurs when a control is resized."),
        new FormEventDef("Scroll", "ScrollEventArgs", null, FormEventCategory.Action, "Occurs when the user moves the scroll box."),
        ..FocusEvents(web: false),
        new FormEventDef("CellPaint", "TableLayoutCellPaintEventArgs", null, FormEventCategory.Appearance, "Occurs when a cell needs repainting.")
    ];

    private static IReadOnlyList<FormEventDef> ToolTipEvents() =>
    [
        new FormEventDef("Popup", "PopupEventArgs", null, FormEventCategory.Behavior, "Occurs whenever a ToolTip is about to be shown.", IsDefault: true),
        new FormEventDef("Draw", "DrawToolTipEventArgs", null, FormEventCategory.Behavior, "Occurs in OwnerDraw mode when the ToolTip needs to be drawn.")
    ];

    private static IReadOnlyList<FormEventDef> BackgroundWorkerEvents() =>
    [
        new FormEventDef("DoWork", "System.ComponentModel.DoWorkEventArgs", null, FormEventCategory.Misc, "Event handler to be run on a different thread when the operation begins.", IsDefault: true, OracleExemption: BackgroundWorkerEventHasNoMetadata),
        new FormEventDef("ProgressChanged", "System.ComponentModel.ProgressChangedEventArgs", null, FormEventCategory.Misc, "Raised when the worker thread indicates that some progress has been made.", OracleExemption: BackgroundWorkerEventHasNoMetadata),
        new FormEventDef("RunWorkerCompleted", "System.ComponentModel.RunWorkerCompletedEventArgs", null, FormEventCategory.Misc, "Raised when the worker has completed (either through success, failure, or cancellation).", OracleExemption: BackgroundWorkerEventHasNoMetadata)
    ];

    private static IReadOnlyList<FormEventDef> MenuStripEvents() =>
    [
        new FormEventDef("ItemClicked", "ToolStripItemClickedEventArgs", "click", FormEventCategory.Action, "Occurs when the item is clicked.", IsDefault: true),
        new FormEventDef("MenuActivate", null, null, FormEventCategory.Behavior, "Occurs when the user has started accessing the menu through the keyboard or mouse."),
        new FormEventDef("MenuDeactivate", null, null, FormEventCategory.Behavior, "Occurs when the user has finished accessing the menu through the keyboard or mouse."),
        new FormEventDef("Click", null, null, FormEventCategory.Action, "Occurs when the component is clicked."),
        new FormEventDef("MouseDown", "MouseEventArgs", "mousedown", FormEventCategory.Mouse, "Occurs when the mouse pointer is over the component and a mouse button is pressed."),
        new FormEventDef("MouseUp", "MouseEventArgs", "mouseup", FormEventCategory.Mouse, "Occurs when the mouse pointer is over the component and a mouse button is released."),
        new FormEventDef("MouseEnter", null, "mouseenter", FormEventCategory.Mouse, "Occurs when the mouse enters the visible part of the control."),
        new FormEventDef("MouseLeave", null, "mouseleave", FormEventCategory.Mouse, "Occurs when the mouse leaves the visible part of the control."),
        new FormEventDef("Paint", "PaintEventArgs", null, FormEventCategory.Appearance, "Occurs when a control needs repainting.")
    ];

    private static IReadOnlyList<FormEventDef> ToolStripEvents() =>
    [
        new FormEventDef("ItemClicked", "ToolStripItemClickedEventArgs", "click", FormEventCategory.Action, "Occurs when the item is clicked.", IsDefault: true),
        new FormEventDef("Click", null, null, FormEventCategory.Action, "Occurs when the component is clicked."),
        new FormEventDef("MouseDown", "MouseEventArgs", "mousedown", FormEventCategory.Mouse, "Occurs when the mouse pointer is over the component and a mouse button is pressed."),
        new FormEventDef("MouseUp", "MouseEventArgs", "mouseup", FormEventCategory.Mouse, "Occurs when the mouse pointer is over the component and a mouse button is released."),
        new FormEventDef("MouseEnter", null, "mouseenter", FormEventCategory.Mouse, "Occurs when the mouse enters the visible part of the control."),
        new FormEventDef("MouseLeave", null, "mouseleave", FormEventCategory.Mouse, "Occurs when the mouse leaves the visible part of the control."),
        new FormEventDef("Paint", "PaintEventArgs", null, FormEventCategory.Appearance, "Occurs when a control needs repainting.")
    ];

    private static IReadOnlyList<FormEventDef> StatusStripEvents() =>
    [
        new FormEventDef("ItemClicked", "ToolStripItemClickedEventArgs", "click", FormEventCategory.Action, "Occurs when the item is clicked.", IsDefault: true),
        new FormEventDef("Click", null, null, FormEventCategory.Action, "Occurs when the component is clicked."),
        new FormEventDef("MouseDown", "MouseEventArgs", "mousedown", FormEventCategory.Mouse, "Occurs when the mouse pointer is over the component and a mouse button is pressed."),
        new FormEventDef("MouseUp", "MouseEventArgs", "mouseup", FormEventCategory.Mouse, "Occurs when the mouse pointer is over the component and a mouse button is released."),
        new FormEventDef("MouseEnter", null, "mouseenter", FormEventCategory.Mouse, "Occurs when the mouse enters the visible part of the control."),
        new FormEventDef("MouseLeave", null, "mouseleave", FormEventCategory.Mouse, "Occurs when the mouse leaves the visible part of the control."),
        new FormEventDef("Paint", "PaintEventArgs", null, FormEventCategory.Appearance, "Occurs when a control needs repainting.")
    ];

    private static IReadOnlyList<FormEventDef> ToolStripMenuItemEvents() =>
    [
        new FormEventDef("Click", null, "click", FormEventCategory.Action, "Occurs when the item is clicked.", IsDefault: true),
        new FormEventDef("DoubleClick", null, "dblclick", FormEventCategory.Action, "Occurs when the component is double-clicked."),
        new FormEventDef("MouseDown", "MouseEventArgs", "mousedown", FormEventCategory.Mouse, "Occurs when a mouse button is pressed."),
        new FormEventDef("MouseUp", "MouseEventArgs", "mouseup", FormEventCategory.Mouse, "Occurs when a mouse button is released."),
        new FormEventDef("MouseEnter", null, "mouseenter", FormEventCategory.Mouse, "Occurs when the mouse enters the visible part of the item."),
        new FormEventDef("MouseLeave", null, "mouseleave", FormEventCategory.Mouse, "Occurs when the mouse leaves the visible part of the item."),
        new FormEventDef("CheckedChanged", null, null, FormEventCategory.Misc, "Occurs whenever the Check property is changed."),
        new FormEventDef("DropDownOpening", null, null, FormEventCategory.Action, "Occurs when the DropDown is opening."),
        new FormEventDef("DropDownOpened", null, null, FormEventCategory.Action, "Occurs when the DropDown has opened."),
        new FormEventDef("DropDownClosed", null, null, FormEventCategory.Action, "Occurs when the DropDown has closed."),
        new FormEventDef("TextChanged", null, null, FormEventCategory.PropertyChanged, "Occurs when the Text property is changed on the item.")
    ];

    private static IReadOnlyList<FormEventDef> ToolStripButtonEvents() =>
    [
        new FormEventDef("Click", null, "click", FormEventCategory.Action, "Occurs when the item is clicked.", IsDefault: true),
        new FormEventDef("DoubleClick", null, "dblclick", FormEventCategory.Action, "Occurs when the component is double-clicked."),
        new FormEventDef("MouseDown", "MouseEventArgs", "mousedown", FormEventCategory.Mouse, "Occurs when a mouse button is pressed."),
        new FormEventDef("MouseUp", "MouseEventArgs", "mouseup", FormEventCategory.Mouse, "Occurs when a mouse button is released."),
        new FormEventDef("MouseEnter", null, "mouseenter", FormEventCategory.Mouse, "Occurs when the mouse enters the visible part of the item."),
        new FormEventDef("MouseLeave", null, "mouseleave", FormEventCategory.Mouse, "Occurs when the mouse leaves the visible part of the item."),
        new FormEventDef("CheckedChanged", null, null, FormEventCategory.Misc, "Occurs whenever the Check property is changed."),
        new FormEventDef("CheckStateChanged", null, null, FormEventCategory.Misc, "Occurs whenever the CheckState property is changed."),
        new FormEventDef("TextChanged", null, null, FormEventCategory.PropertyChanged, "Occurs when the Text property is changed on the item.")
    ];

    private static IReadOnlyList<FormEventDef> ToolStripStatusLabelEvents() =>
    [
        new FormEventDef("Click", null, "click", FormEventCategory.Action, "Occurs when the item is clicked.", IsDefault: true),
        new FormEventDef("DoubleClick", null, "dblclick", FormEventCategory.Action, "Occurs when the component is double-clicked."),
        new FormEventDef("MouseDown", "MouseEventArgs", "mousedown", FormEventCategory.Mouse, "Occurs when a mouse button is pressed."),
        new FormEventDef("MouseUp", "MouseEventArgs", "mouseup", FormEventCategory.Mouse, "Occurs when a mouse button is released."),
        new FormEventDef("MouseEnter", null, "mouseenter", FormEventCategory.Mouse, "Occurs when the mouse enters the visible part of the item."),
        new FormEventDef("MouseLeave", null, "mouseleave", FormEventCategory.Mouse, "Occurs when the mouse leaves the visible part of the item."),
        new FormEventDef("TextChanged", null, null, FormEventCategory.PropertyChanged, "Occurs when the Text property is changed on the item.")
    ];

    /// <summary>
    /// The Form's events (slice 5 D-3). Default Load, as WinForms' <c>[DefaultEvent]</c>. On the page: Load is a call at the
    /// end of <c>InitializeComponent</c> (a <c>window</c> load can already have fired), Resize listens on the window, and
    /// Click/KeyDown/KeyUp/KeyPress on <c>document.body</c> — so they see bubbled events, as if <c>KeyPreview=True</c>
    /// (a recorded divergence, ADR 0021). Shown, Activated and FormClosing/FormClosed have no honest page event.
    /// </summary>
    private static IReadOnlyList<FormEventDef> FormRootEvents() => new[]
    {
        new FormEventDef("Load", null, "load", FormEventCategory.Behavior, "Occurs whenever the user loads the form.", IsDefault: true, WebWiring: FormWebWiring.AfterInit),
        new FormEventDef("Shown", null, null, FormEventCategory.Behavior, "Occurs whenever the form is first shown."),
        new FormEventDef("Activated", null, null, FormEventCategory.Focus, "Occurs whenever the form is activated."),
        new FormEventDef("FormClosing", "FormClosingEventArgs", null, FormEventCategory.Behavior, "Occurs whenever the user closes the form, before the form has been closed and specifies the close reason."),
        new FormEventDef("FormClosed", "FormClosedEventArgs", null, FormEventCategory.Behavior, "Occurs whenever the user closes the form, after the form has been closed and specifies the close reason."),
        new FormEventDef("Resize", null, "resize", FormEventCategory.Layout, "Occurs when a control is resized.", WebWiring: FormWebWiring.Window),
        new FormEventDef("Click", null, "click", FormEventCategory.Action, "Occurs when the component is clicked."),
        new FormEventDef("KeyDown", "KeyEventArgs", "keydown", FormEventCategory.Key, "Occurs when a key is first pressed."),
        new FormEventDef("KeyUp", "KeyEventArgs", "keyup", FormEventCategory.Key, "Occurs when a key is released."),
        new FormEventDef("KeyPress", "KeyPressEventArgs", "keypress", FormEventCategory.Key, "Occurs when the control has focus and the user presses and releases a key.", WebFilter: FormWebFilter.KeyPressKeys)
    };

    /// <summary>
    /// A row's events when it declares only its DEFAULT one — Timer, ErrorProvider and ToolStripSeparator, the kinds
    /// whose snapshot offers nothing more a VS user wires (D-1). Category and Description are WinForms' own.
    /// ⛔ A static METHOD, not a field, so it is immune to the textual-order initializer trap below.
    /// </summary>
    private static IReadOnlyList<FormEventDef> Ev(
        string winForms, string? web = null, string? args = null,
        FormEventCategory? category = null, string? description = null, string? exemption = null) =>
        new[] { new FormEventDef(winForms, args, web, category, description, IsDefault: true, OracleExemption: exemption) };

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
        new("Label",       "Label",       "label",    null,       false, Common(Text, LabelTextAlign, PaddingRow,
            LabelAutoSize(),
            new FormPropertyDef("BorderStyle", FormPropertyType.Enum, "None", new[] { "None", "FixedSingle", "Fixed3D" },
                WinFormsEnumType: "BorderStyle", Targets: new[] { FormTarget.WinForms },
                Category: FormPropertyCategory.Appearance, Description: "Determines if the label has a visible border.")),
            DefaultWidth: 100, DefaultHeight: 23, Schematic: FormSchematic.Text,
            Events: LabelEvents()),
        new("TextBox",     "TextBox",     "input",    "text",     false, ControlRows(WindowTextForeColor, WindowBackColor,
            FontRow, IBeamCursorRow,
            Text,
            new FormPropertyDef("Multiline", FormPropertyType.Bool, "false",
                Category: FormPropertyCategory.Behavior,
                Description: "Controls whether the text of the edit control can span more than one line."),
            new FormPropertyDef("ReadOnly", FormPropertyType.Bool, "false",
                Category: FormPropertyCategory.Behavior,
                Description: "Controls whether the text in the edit control can be changed or not."),
            // ⚠ WinForms caps an absent MaxLength at 32767; an <input> with no maxlength is unlimited.
            new FormPropertyDef("MaxLength", FormPropertyType.Int, "32767", HtmlAttribute: "maxlength",
                WebDefault: "",
                Category: FormPropertyCategory.Behavior,
                Description: "Specifies the maximum number of characters that can be entered into the edit control."),
            // WinForms PasswordChar is a char, not a string — assigning one is CS0029.
            new FormPropertyDef("PasswordChar", FormPropertyType.String,
                WinFormsFactory: "Convert.ToChar",
                Category: FormPropertyCategory.Behavior,
                Description: "Indicates the character to display for password input for single-line edit controls."),
            new FormPropertyDef("UseSystemPasswordChar", FormPropertyType.Bool, "false", Targets: new[] { FormTarget.WinForms },
                Category: FormPropertyCategory.Behavior,
                Description: "Indicates if the text in the edit control should appear as the default password character."),
            new FormPropertyDef("ScrollBars", FormPropertyType.Enum, "None", new[] { "None", "Horizontal", "Vertical", "Both" },
                WinFormsEnumType: "ScrollBars", Targets: new[] { FormTarget.WinForms },
                Category: FormPropertyCategory.Appearance,
                Description: "Indicates, for multiline edit controls, which scroll bars will be shown for this control."),
            new FormPropertyDef("WordWrap", FormPropertyType.Bool, "true", Targets: new[] { FormTarget.WinForms },
                Category: FormPropertyCategory.Behavior,
                Description: "Indicates if lines are automatically word-wrapped for multiline edit controls."),
            // ⛔ Both targets (D2): the <input>'s own placeholder attribute.
            new FormPropertyDef("PlaceholderText", FormPropertyType.String, HtmlAttribute: "placeholder",
                Category: FormPropertyCategory.Misc,
                Description: "Specifies the PlaceholderText of the TextBox control. The PlaceholderText is displayed in the control when the Text property is null or empty and can be used to guide the user what input is expected by the control."),
            HorizontalTextAlign("Indicates how the text should be aligned for edit controls.")),
            DefaultWidth: 100, DefaultHeight: 23,
            // ⚠ The DOM has no TextChanged. `input` fires per keystroke, which is what TextChanged
            // means; `change` fires on blur and would be a different gesture wearing the same name.
            Events: TextBoxEvents(),
            StretchesWhenStacked: true),
        new("Button",      "Button",      "button",   null,       false, Common(Text, ButtonTextAlign, PaddingRow,
            new FormPropertyDef("DialogResult", FormPropertyType.Enum, "None",
                new[] { "None", "OK", "Cancel", "Abort", "Retry", "Ignore", "Yes", "No", "TryAgain", "Continue" },
                WinFormsEnumType: "DialogResult", Targets: new[] { FormTarget.WinForms },
                Category: FormPropertyCategory.Behavior,
                Description: "The dialog-box result produced in a modal form by clicking the button."),
            new FormPropertyDef("FlatStyle", FormPropertyType.Enum, "Standard", new[] { "Flat", "Popup", "Standard", "System" },
                WinFormsEnumType: "FlatStyle", Targets: new[] { FormTarget.WinForms },
                Category: FormPropertyCategory.Appearance,
                Description: "Determines the appearance of the control when a user moves the mouse over the control and clicks.")),
            DefaultWidth: 75, DefaultHeight: 23, Schematic: FormSchematic.Button,
            Events: ButtonEvents()),
        new("CheckBox",    "CheckBox",    "input",    "checkbox", false, Common(Text, Checked, PaddingRow,
            // ⚠ WinForms only: an HTML checkbox's indeterminate state exists only as a script property, never markup.
            new FormPropertyDef("CheckState", FormPropertyType.Enum, "Unchecked", new[] { "Unchecked", "Checked", "Indeterminate" },
                WinFormsEnumType: "CheckState", Targets: new[] { FormTarget.WinForms },
                Category: FormPropertyCategory.Appearance, Description: "Indicates the state of the component."),
            CheckAlign(),
            new FormPropertyDef("ThreeState", FormPropertyType.Bool, "false", Targets: new[] { FormTarget.WinForms },
                Category: FormPropertyCategory.Behavior,
                Description: "Indicates whether the CheckBox will allow three check states rather than two."),
            new FormPropertyDef("AutoCheck", FormPropertyType.Bool, "true", Targets: new[] { FormTarget.WinForms },
                Category: FormPropertyCategory.Behavior,
                Description: "Causes the check box to automatically change state when clicked."),
            new FormPropertyDef("Appearance", FormPropertyType.Enum, "Normal", new[] { "Normal", "Button" },
                WinFormsEnumType: "Appearance", Targets: new[] { FormTarget.WinForms },
                Category: FormPropertyCategory.Appearance, Description: "Controls the appearance of the check box.")),
            DefaultWidth: 104, DefaultHeight: 24, Schematic: FormSchematic.Check,
            Events: CheckBoxEvents()),
        new("RadioButton", "RadioButton", "input",    "radio",    false, Common(
            Text,
            RadioChecked,
            // ⛔ WEB ONLY. The DOM groups radios by name=; WinForms groups them by CONTAINER and
            // has no GroupName property at all — csc says CS1061, BasicLang says nothing.
            new FormPropertyDef("GroupName", FormPropertyType.String,
                Targets: new[] { FormTarget.Web },
                Category: FormPropertyCategory.Behavior,
                Description: "The radio group this button belongs to on the page: buttons sharing a name are mutually exclusive."),
            PaddingRow,
            CheckAlign(),
            new FormPropertyDef("AutoCheck", FormPropertyType.Bool, "true", Targets: new[] { FormTarget.WinForms },
                Category: FormPropertyCategory.Behavior,
                Description: "Causes the radio button to automatically change state when clicked."),
            new FormPropertyDef("Appearance", FormPropertyType.Enum, "Normal", new[] { "Normal", "Button" },
                WinFormsEnumType: "Appearance", Targets: new[] { FormTarget.WinForms },
                Category: FormPropertyCategory.Appearance,
                Description: "Controls whether the RadioButton appears as normal or as a Windows PushButton.")),
            DefaultWidth: 104, DefaultHeight: 24, Schematic: FormSchematic.Radio,
            Events: RadioButtonEvents()),
        new("ComboBox",    "ComboBox",    "select",   null,       false, CommonColoured(WindowTextForeColor, WindowBackColor,
            Text,
            // Items is a get-only collection on WinForms — assigning it is CS0200.
            new FormPropertyDef("Items", FormPropertyType.String, IsItemCollection: true,
                Category: FormPropertyCategory.Data, Description: "The items in the combo box."),
            SelectedIndex(SelectedIndexForTheWeb),
            // ⚠ WinForms only: a <select> is always a drop-down list — it has no editable text box to style.
            new FormPropertyDef("DropDownStyle", FormPropertyType.Enum, "DropDown", new[] { "Simple", "DropDown", "DropDownList" },
                WinFormsEnumType: "ComboBoxStyle", Targets: new[] { FormTarget.WinForms },
                Category: FormPropertyCategory.Appearance,
                Description: "Controls the appearance and functionality of the combo box."),
            new FormPropertyDef("Sorted", FormPropertyType.Bool, "false", Targets: new[] { FormTarget.WinForms },
                Category: FormPropertyCategory.Behavior,
                Description: "Specifies whether items in the list portion of the combo box are sorted."),
            new FormPropertyDef("MaxDropDownItems", FormPropertyType.Int, "8", Targets: new[] { FormTarget.WinForms },
                Category: FormPropertyCategory.Behavior,
                Description: "The maximum number of entries to display in the drop-down list.")),
            DefaultWidth: 121, DefaultHeight: 23, Schematic: FormSchematic.Dropdown,
            Events: ComboBoxEvents(),
            StretchesWhenStacked: true),
        new("ListBox",     "ListBox",     "select",   null,       false, CommonColoured(WindowTextForeColor, WindowBackColor,
            new FormPropertyDef("Items", FormPropertyType.String, IsItemCollection: true,
                Category: FormPropertyCategory.Data, Description: ListBoxItemsDescription),
            SelectedIndex(SelectedIndexForTheWeb),
            // ⛔ WEB ONLY. WinForms ListBox has no MultiSelect — it has SelectionMode, an enum.
            // Mapping a Bool onto it is a design decision v1 has not made, so the property stays
            // web-only rather than being silently approximated.
            new FormPropertyDef("MultiSelect", FormPropertyType.Bool, "false",
                Targets: new[] { FormTarget.Web },
                Category: FormPropertyCategory.Behavior,
                Description: "Allows more than one item to be selected at a time on the page."),
            // ⚠ WinForms' own selection vocabulary; the page keeps its web-only MultiSelect (above) — mapping one onto
            // the other is still the decision v1 has not made.
            new FormPropertyDef("SelectionMode", FormPropertyType.Enum, "One", new[] { "None", "One", "MultiSimple", "MultiExtended" },
                WinFormsEnumType: "SelectionMode", Targets: new[] { FormTarget.WinForms },
                Category: FormPropertyCategory.Behavior,
                Description: "Indicates if the list box is to be single-select, multi-select, or not selectable."),
            ListSorted(),
            new FormPropertyDef("MultiColumn", FormPropertyType.Bool, "false", Targets: new[] { FormTarget.WinForms },
                Category: FormPropertyCategory.Behavior,
                Description: "Indicates if values should be displayed in columns horizontally.")),
            DefaultWidth: 120, DefaultHeight: 95, Schematic: FormSchematic.List,
            Events: ListBoxEvents(),
            StretchesWhenStacked: true),
        new("Panel",       "Panel",       "div",      null,       true,  Common(
            new FormPropertyDef("BorderStyle", FormPropertyType.Enum, "None",
                new[] { "None", "FixedSingle", "Fixed3D" },
                WinFormsEnumType: "BorderStyle",
                Category: FormPropertyCategory.Appearance,
                Description: "Indicates whether the panel should have a border."),
            // ⛔ Both targets (D2): on the page the <div> scrolls its overflow, as the WinForms Panel does.
            new FormPropertyDef("AutoScroll", FormPropertyType.Bool, "false",
                Category: FormPropertyCategory.Layout,
                Description: "Indicates whether scroll bars automatically appear when the control contents are larger than its visible area.",
                CssProperty: "overflow", CssConverter: FormCssConverter.AutoScrollToOverflow)),
            DefaultWidth: 200, DefaultHeight: 100, Schematic: FormSchematic.Container,
            // ⛔ Owner decision (2026-09-29): a double-click opens what VS opens — Paint, with its PaintEventArgs. A page
            // has no Paint, so on the web the gesture opens the row's DECLARED web default, Click, and says so
            // (BL8035); a Paint bind is dropped-and-named by a retarget like any event with no web name.
            Events: PanelEvents()),
        new("GroupBox",    "GroupBox",    "fieldset", null,       true,  Common(Text),
            DefaultWidth: 200, DefaultHeight: 100, Schematic: FormSchematic.Group,
            // ⛔ Owner decision O3 (2026-09-29, checked in Visual Studio): a double-click creates an ENTER handler,
            // as VS does — the snapshot's DefaultEvent agrees, so Enter needs no exemption. On the page Enter is the
            // fieldset's `focusin`: focus moving INTO the box, which bubbles from its children exactly as WinForms
            // raises Enter for a container when one of its children becomes active (a fieldset itself takes no focus).
            // ⚠ Click stays, NON-default: existing documents bind it, and the retarget crosses any event wired on
            // both targets (FormRetarget.CrossBinds through FormEvents.WiredOn — pre-flight B1, slice 5 D-6).
            Events: GroupBoxEvents()),
        new("PictureBox",  "PictureBox",  "img",      null,       false, ControlRows(
            null, BackColor, null, CursorRow,
            // WinForms Image is a System.Drawing.Image, not a path string (CS0029): the Image TYPE owns its literal (D-5b).
            new FormPropertyDef("Image", FormPropertyType.Image,
                Category: FormPropertyCategory.Appearance,
                Description: "The image displayed in the PictureBox."),
            new FormPropertyDef("SizeMode", FormPropertyType.Enum, "Normal",
                new[] { "Normal", "StretchImage", "AutoSize", "CenterImage", "Zoom" },
                WinFormsEnumType: "PictureBoxSizeMode",
                Category: FormPropertyCategory.Behavior,
                Description: "Controls how the PictureBox will handle image placement and control sizing."),
            new FormPropertyDef("BorderStyle", FormPropertyType.Enum, "None", new[] { "None", "FixedSingle", "Fixed3D" },
                WinFormsEnumType: "BorderStyle", Targets: new[] { FormTarget.WinForms },
                Category: FormPropertyCategory.Appearance,
                Description: "Controls what type of border the PictureBox should have.")),
            DefaultWidth: 100, DefaultHeight: 50, Schematic: FormSchematic.Image,
            Events: PictureBoxEvents(),
            StretchesWhenStacked: true),

        // ==================================================================
        // Task 23 — the rest of the common-controls tier.
        //
        // ⛔ Every property below is UNFALSIFIABLE except through csc. A misspelling types as
        // Object and compiles green, so nothing here is trustworthy until
        // WinFormsCatalogSweepTests has generated it and the real compiler has accepted it.
        // ==================================================================

        // ⚠ A LinkLabel IS a Label that looks clickable, and <a> is the honest tag. Slice 3 (D1) adds LinkColor — the
        // designer writes it only when the user sets it (spec §2.7), so no theme is frozen into the form. ⚠ WinForms-only:
        // on the page the link's colour IS its ForeColor (`color`), and two rows writing one CSS property conflict.
        // ActiveLinkColor/DisabledLinkColor/VisitedLinkColor stay out (not D1).
        new("LinkLabel",   "LinkLabel",   "a",        null,       false, Common(Text, LinkLabelTextAlign, PaddingRow,
            new FormPropertyDef("LinkColor", FormPropertyType.Color, "#FF0000FF", Targets: new[] { FormTarget.WinForms },
                Category: FormPropertyCategory.Appearance,
                Description: "Determines the color of the hyperlink in its default state."),
            new FormPropertyDef("LinkBehavior", FormPropertyType.Enum, "SystemDefault",
                new[] { "SystemDefault", "AlwaysUnderline", "HoverUnderline", "NeverUnderline" },
                WinFormsEnumType: "LinkBehavior", Targets: new[] { FormTarget.WinForms },
                Category: FormPropertyCategory.Behavior, Description: "Determines the underline behavior of the hyperlink."),
            LabelAutoSize()),
            DefaultWidth: 100, DefaultHeight: 23, Schematic: FormSchematic.Link,
            // ⚠ The typed args (Task 8's parity run): EventArgs compiled by contravariance but hid e.Link.
            Events: LinkLabelEvents()),

        new("NumericUpDown", "NumericUpDown", "input", "number",  false, CommonColoured(WindowTextForeColor, WindowBackColor,
            // ⛔ These are DECIMAL on WinForms. An Int literal widens implicitly, so the catalog
            // models them as Int and the emitted `n.Minimum = 0` compiles — but a decimal default
            // written as "0.00" would not round-trip through Int, which is why the defaults are
            // whole numbers.
            new FormPropertyDef("Minimum", FormPropertyType.Int, "0", HtmlAttribute: "min",
                Category: FormPropertyCategory.Data,
                Description: "Indicates the minimum value for the numeric up-down control."),
            new FormPropertyDef("Maximum", FormPropertyType.Int, "100", HtmlAttribute: "max",
                Category: FormPropertyCategory.Data,
                Description: "Indicates the maximum value for the numeric up-down control."),
            new FormPropertyDef("Value", FormPropertyType.Int, "0", HtmlAttribute: "value",
                Category: FormPropertyCategory.Appearance,
                Description: "The current value of the numeric up-down control."),
            new FormPropertyDef("Increment", FormPropertyType.Int, "1", HtmlAttribute: "step",
                Category: FormPropertyCategory.Data,
                Description: "Indicates the amount to increment or decrement on each button click."),
            // ⛔ WinForms only: <input type="number"> has no decimal-places concept, it has step.
            new FormPropertyDef("DecimalPlaces", FormPropertyType.Int, "0",
                Targets: new[] { FormTarget.WinForms },
                Category: FormPropertyCategory.Data,
                Description: "Indicates the number of decimal places to display."),
            new FormPropertyDef("Hexadecimal", FormPropertyType.Bool, "false", Targets: new[] { FormTarget.WinForms },
                Category: FormPropertyCategory.Appearance,
                Description: "Indicates whether the numeric up-down should display its value in hexadecimal."),
            // ⛔ Both targets (D2): the <input>'s readonly (the emitter's flag, through this row).
            new FormPropertyDef("ReadOnly", FormPropertyType.Bool, "false",
                Category: FormPropertyCategory.Behavior, Description: "Indicates whether the edit box is read-only."),
            HorizontalTextAlign("Indicates how the text should be aligned in the edit box.")),
            DefaultWidth: 120, DefaultHeight: 23, Schematic: FormSchematic.Spinner,
            Events: NumericUpDownEvents()),

        new("DateTimePicker", "DateTimePicker", "input", "date",  false, CommonColoured(
            null, null,
            // ⛔ All WinForms-only. <input type="date"> renders per the user's locale and has no
            // format control at all, so emitting these to the web would be describing a behaviour
            // the page cannot have.
            new FormPropertyDef("Format", FormPropertyType.Enum, "Long",
                new[] { "Long", "Short", "Time", "Custom" },
                WinFormsEnumType: "DateTimePickerFormat",
                Targets: new[] { FormTarget.WinForms },
                Category: FormPropertyCategory.Appearance,
                Description: "Determines whether dates and times are displayed using standard or custom formatting."),
            new FormPropertyDef("CustomFormat", FormPropertyType.String,
                Targets: new[] { FormTarget.WinForms },
                Category: FormPropertyCategory.Behavior,
                Description: "The custom format string used to format the date and/or time displayed in the control."),
            new FormPropertyDef("ShowUpDown", FormPropertyType.Bool, "false",
                Targets: new[] { FormTarget.WinForms },
                Category: FormPropertyCategory.Appearance,
                Description: "Indicates whether a spin box rather than a drop-down calendar is displayed for modifying the control value."),
            new FormPropertyDef("ShowCheckBox", FormPropertyType.Bool, "false", Targets: new[] { FormTarget.WinForms },
                Category: FormPropertyCategory.Appearance,
                Description: "Determines whether a check box is displayed in the control. When the box is unchecked, no value is selected.")),
            DefaultWidth: 200, DefaultHeight: 23, Schematic: FormSchematic.DatePicker,
            Events: DateTimePickerEvents()),

        new("TrackBar",    "TrackBar",    "input",    "range",    false, ControlRows(
            null, OpaqueBackColor, null, CursorRow,
            new FormPropertyDef("Minimum", FormPropertyType.Int, "0", HtmlAttribute: "min",
                Category: FormPropertyCategory.Behavior,
                Description: "The minimum value for the position of the slider on the TrackBar."),
            new FormPropertyDef("Maximum", FormPropertyType.Int, "10", HtmlAttribute: "max",
                Category: FormPropertyCategory.Behavior,
                Description: "The maximum value for the position of the slider on the TrackBar."),
            new FormPropertyDef("Value", FormPropertyType.Int, "0", HtmlAttribute: "value",
                Category: FormPropertyCategory.Behavior,
                Description: "The position of the slider."),
            new FormPropertyDef("TickFrequency", FormPropertyType.Int, "1",
                Targets: new[] { FormTarget.WinForms },
                Category: FormPropertyCategory.Appearance,
                Description: "The number of positions between tick marks."),
            new FormPropertyDef("Orientation", FormPropertyType.Enum, "Horizontal",
                new[] { "Horizontal", "Vertical" },
                WinFormsEnumType: "Orientation",
                Targets: new[] { FormTarget.WinForms },
                Category: FormPropertyCategory.Appearance,
                Description: "The orientation of the control."),
            new FormPropertyDef("TickStyle", FormPropertyType.Enum, "BottomRight", new[] { "None", "TopLeft", "BottomRight", "Both" },
                WinFormsEnumType: "TickStyle", Targets: new[] { FormTarget.WinForms },
                Category: FormPropertyCategory.Appearance, Description: "Indicates where the ticks appear on the TrackBar."),
            new FormPropertyDef("LargeChange", FormPropertyType.Int, "5", Targets: new[] { FormTarget.WinForms },
                Category: FormPropertyCategory.Behavior,
                Description: "The number of positions the slider moves in response to mouse clicks or the PAGE UP and PAGE DOWN keys."),
            // ⚠ WinForms only: the page's `step` constrains which VALUES are valid, not how far an arrow key moves.
            new FormPropertyDef("SmallChange", FormPropertyType.Int, "1", Targets: new[] { FormTarget.WinForms },
                Category: FormPropertyCategory.Behavior,
                Description: "The number of positions the slider moves in response to keyboard input (arrow keys).")),
            DefaultWidth: 150, DefaultHeight: 45, Schematic: FormSchematic.Slider,
            // ⛔ Owner decision (2026-09-29): VS opens a TrackBar on Scroll. On the page Scroll is the range input's
            // `input` — it fires per step of a drag, as WinForms' Scroll does for a user's move — and ValueChanged is
            // `change`. Two events cannot share one DOM name (a web bind is stored BY it), so an existing web `input`
            // bind now retargets to WinForms as Scroll; on the page it fires exactly as before.
            Events: TrackBarEvents()),

        new("ProgressBar", "ProgressBar", "progress", null,       false, ControlRows(HighlightForeColor, OpaqueBackColor, null, CursorRow,
            new FormPropertyDef("Minimum", FormPropertyType.Int, "0",
                Targets: new[] { FormTarget.WinForms },
                Category: FormPropertyCategory.Behavior,
                Description: "The lower bound of the range this ProgressBar is working with."),
            new FormPropertyDef("Maximum", FormPropertyType.Int, "100", HtmlAttribute: "max",
                Category: FormPropertyCategory.Behavior,
                Description: "The upper bound of the range this ProgressBar is working with."),
            new FormPropertyDef("Value", FormPropertyType.Int, "0", HtmlAttribute: "value",
                Category: FormPropertyCategory.Behavior,
                Description: "The current value for the ProgressBar, in the range specified by the minimum and maximum properties."),
            new FormPropertyDef("Style", FormPropertyType.Enum, "Blocks",
                new[] { "Blocks", "Continuous", "Marquee" },
                WinFormsEnumType: "ProgressBarStyle",
                Targets: new[] { FormTarget.WinForms },
                Category: FormPropertyCategory.Behavior,
                Description: "This property allows the user to set the style of the ProgressBar.")),
            DefaultWidth: 150, DefaultHeight: 23, Schematic: FormSchematic.Progress,
            // ⚠ A ProgressBar reports; it does not notify. Click is what Control gives it and the
            // only thing a double-click here can honestly stub.
            Events: ProgressBarEvents()),

        // ==================================================================
        // ⛔⛔ WinForms-ONLY, by decision rather than omission. None of these has a single honest
        // HTML tag: a DataGridView is not a <table>, a TabControl needs script the designer does
        // not write, and a SplitContainer is a CSS layout rather than an element. Emitting a
        // <div> for them would produce a page that silently is not the control the user drew.
        // FormCatalogCoverageTests pins this, and Task 21's retarget reports it as explicit loss.
        // ==================================================================

        new("CheckedListBox", "CheckedListBox", null, null,       false, CommonColoured(WindowTextForeColor, WindowBackColor,
            new FormPropertyDef("Items", FormPropertyType.String, IsItemCollection: true,
                Category: FormPropertyCategory.Data, Description: ListBoxItemsDescription),
            SelectedIndex(SelectedIndexWinFormsOnly),
            new FormPropertyDef("CheckOnClick", FormPropertyType.Bool, "false",
                Category: FormPropertyCategory.Behavior,
                Description: "Indicates if the check box should be toggled with the first click on an item."),
            ListSorted()),
            DefaultWidth: 160, DefaultHeight: 95, Schematic: FormSchematic.CheckList,
            Events: CheckedListBoxEvents()),

        new("ListView",    "ListView",    null,       null,       false, CommonColoured(WindowTextForeColor, WindowBackColor,
            new FormPropertyDef("View", FormPropertyType.Enum, "LargeIcon",
                new[] { "LargeIcon", "Details", "SmallIcon", "List", "Tile" },
                WinFormsEnumType: "View",
                Category: FormPropertyCategory.Appearance,
                Description: "Selects one of five different views that items can be shown in."),
            new FormPropertyDef("FullRowSelect", FormPropertyType.Bool, "false",
                Category: FormPropertyCategory.Appearance,
                Description: "Indicates whether all SubItems are highlighted along with the item when selected."),
            new FormPropertyDef("GridLines", FormPropertyType.Bool, "false",
                Category: FormPropertyCategory.Appearance,
                Description: "Displays grid lines around items and SubItems. Only shown when in Details view."),
            new FormPropertyDef("MultiSelect", FormPropertyType.Bool, "true",
                Category: FormPropertyCategory.Behavior,
                Description: "Allows multiple items to be selected.")),
            DefaultWidth: 240, DefaultHeight: 120, Schematic: FormSchematic.ListDetail,
            Events: ListViewEvents()),

        new("TreeView",    "TreeView",    null,       null,       false, CommonColoured(WindowTextForeColor, WindowBackColor,
            new FormPropertyDef("ShowLines", FormPropertyType.Bool, "true",
                Category: FormPropertyCategory.Behavior,
                Description: "Indicates whether lines are displayed between sibling nodes and between parent and child nodes."),
            new FormPropertyDef("ShowRootLines", FormPropertyType.Bool, "true",
                Category: FormPropertyCategory.Behavior,
                Description: "Indicates whether lines are displayed between root nodes."),
            new FormPropertyDef("HideSelection", FormPropertyType.Bool, "true",
                Category: FormPropertyCategory.Behavior,
                Description: "Removes highlight from the selected node when control does not have focus."),
            new FormPropertyDef("Indent", FormPropertyType.Int, "19",
                Category: FormPropertyCategory.Behavior,
                Description: "The indentation width of child nodes in pixels."),
            new FormPropertyDef("CheckBoxes", FormPropertyType.Bool, "false",
                Category: FormPropertyCategory.Appearance,
                Description: "Indicates whether check boxes are displayed beside nodes.")),
            DefaultWidth: 180, DefaultHeight: 140, Schematic: FormSchematic.Tree,
            // ⚠ The typed args (Task 8's parity run): EventArgs compiled by contravariance but hid e.Node.
            Events: TreeViewEvents()),

        new("DataGridView", "DataGridView", null,     null,       false, ControlRows(
            null, null, null, CursorRow,
            new FormPropertyDef("AllowUserToAddRows", FormPropertyType.Bool, "true",
                Category: FormPropertyCategory.Behavior,
                Description: "Indicates whether the option to add rows is displayed to the user."),
            new FormPropertyDef("AllowUserToDeleteRows", FormPropertyType.Bool, "true",
                Category: FormPropertyCategory.Behavior,
                Description: "Indicates whether the user is allowed to delete rows from the DataGridView."),
            new FormPropertyDef("ReadOnly", FormPropertyType.Bool, "false",
                Category: FormPropertyCategory.Behavior,
                Description: "Indicates whether the user can edit the cells of the DataGridView control."),
            new FormPropertyDef("RowHeadersVisible", FormPropertyType.Bool, "true",
                Category: FormPropertyCategory.Appearance,
                Description: "Indicates whether the column that contains row headers is displayed."),
            new FormPropertyDef("ColumnHeadersVisible", FormPropertyType.Bool, "true",
                Category: FormPropertyCategory.Appearance,
                Description: "Indicates whether the column headers row is displayed.")),
            DefaultWidth: 280, DefaultHeight: 150, Schematic: FormSchematic.DataGrid,
            // ⚠ The typed args (Task 8's parity run): EventArgs compiled by contravariance but hid e.RowIndex.
            // ⛔ Owner decision (2026-09-29): VS opens CellContentClick; CellClick stays, non-default.
            Events: DataGridViewEvents()),

        new("TabControl",  "TabControl",  null,       null,       true,  CommonColoured(
            null, null,
            new FormPropertyDef("Alignment", FormPropertyType.Enum, "Top",
                new[] { "Top", "Bottom", "Left", "Right" },
                WinFormsEnumType: "TabAlignment",
                Category: FormPropertyCategory.Behavior,
                Description: "Determines whether the tabs appear on the top, bottom, left, or right side of the Control (left or right are implicitly multilined)."),
            new FormPropertyDef("Multiline", FormPropertyType.Bool, "false",
                Category: FormPropertyCategory.Behavior,
                Description: "Indicates if more than one row of tabs is allowed."),
            SelectedIndex(SelectedIndexWinFormsOnly),
            new FormPropertyDef("Appearance", FormPropertyType.Enum, "Normal", new[] { "Normal", "Buttons", "FlatButtons" },
                WinFormsEnumType: "TabAppearance",
                Category: FormPropertyCategory.Behavior,
                Description: "Indicates whether the tabs are painted as buttons or regular tabs.")),
            DefaultWidth: 240, DefaultHeight: 160, Schematic: FormSchematic.Tabs,
            Events: TabControlEvents()),

        new("SplitContainer", "SplitContainer", null, null,       true,  Common(
            new FormPropertyDef("Orientation", FormPropertyType.Enum, "Vertical",
                new[] { "Horizontal", "Vertical" },
                WinFormsEnumType: "Orientation",
                Category: FormPropertyCategory.Behavior,
                Description: "Determines if the splitter is vertical or horizontal."),
            // ⚠ WinForms' [DefaultValue] is 50 (the parity run). The old "80" was a designer preference
            // encoded as a default — the grid showed 80 while an absent value ran at 50 (spec §2.7).
            new FormPropertyDef("SplitterDistance", FormPropertyType.Int, "50",
                Category: FormPropertyCategory.Layout,
                Description: "Determines pixel distance of the splitter from the left or top edge."),
            new FormPropertyDef("SplitterWidth", FormPropertyType.Int, "4",
                Category: FormPropertyCategory.Layout,
                Description: "Determines the thickness of the splitter."),
            new FormPropertyDef("IsSplitterFixed", FormPropertyType.Bool, "false",
                Category: FormPropertyCategory.Layout,
                Description: "Determines if the splitter can move."),
            new FormPropertyDef("FixedPanel", FormPropertyType.Enum, "None", new[] { "None", "Panel1", "Panel2" },
                WinFormsEnumType: "FixedPanel",
                Category: FormPropertyCategory.Layout,
                Description: "Indicates that a particular SplitContainer's Panel should remain fixed in size during resize events.")),
            DefaultWidth: 260, DefaultHeight: 140, Schematic: FormSchematic.Split,
            // ⚠ The typed args (Task 8's parity run).
            Events: SplitContainerEvents()),

        new("FlowLayoutPanel", "FlowLayoutPanel", null, null,     true,  Common(
            new FormPropertyDef("FlowDirection", FormPropertyType.Enum, "LeftToRight",
                new[] { "LeftToRight", "TopDown", "RightToLeft", "BottomUp" },
                WinFormsEnumType: "FlowDirection",
                Category: FormPropertyCategory.Layout,
                Description: "Specifies the direction in which controls are laid out."),
            new FormPropertyDef("WrapContents", FormPropertyType.Bool, "true",
                Category: FormPropertyCategory.Layout,
                Description: "Indicates whether contents are wrapped or clipped at the control boundary."),
            new FormPropertyDef("AutoScroll", FormPropertyType.Bool, "false",
                Category: FormPropertyCategory.Layout,
                Description: "Indicates whether scroll bars automatically appear when the control contents are larger than its visible area.")),
            DefaultWidth: 220, DefaultHeight: 120, Schematic: FormSchematic.FlowContainer,
            Events: FlowLayoutPanelEvents()),

        new("TableLayoutPanel", "TableLayoutPanel", null, null,   true,  Common(
            // ⚠ WinForms' [DefaultValue] is 0 for both (the parity run). VS DROPS a TableLayoutPanel as
            // 2×2 — a designer PREFERENCE, which spec §2.7 says is written at placement, never encoded as
            // a false default: DropValues below (owner decision O1, 2026-09-29).
            new FormPropertyDef("ColumnCount", FormPropertyType.Int, "0",
                Category: FormPropertyCategory.Layout,
                Description: "The number of columns on the table."),
            new FormPropertyDef("RowCount", FormPropertyType.Int, "0",
                Category: FormPropertyCategory.Layout,
                Description: "The number of rows on the table."),
            new FormPropertyDef("CellBorderStyle", FormPropertyType.Enum, "None",
                new[] { "None", "Single", "Inset", "Outset" },
                WinFormsEnumType: "TableLayoutPanelCellBorderStyle",
                Category: FormPropertyCategory.Appearance,
                Description: "Indicates the appearance of cell borders in a table.")),
            DefaultWidth: 220, DefaultHeight: 120, Schematic: FormSchematic.TableContainer,
            Events: TableLayoutPanelEvents(),
            DropValues: new Dictionary<string, string> { ["ColumnCount"] = "2", ["RowCount"] = "2" }),

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
                // ⚠ "Elapsed" is WinForms' own [Description] text for the Timer (sic — Tick is the event);
                // parity copies it verbatim rather than correcting what VS shows.
                new("Interval", FormPropertyType.Int, "100",
                    Category: FormPropertyCategory.Behavior,
                    Description: "The frequency of Elapsed events in milliseconds."),
                // ⚠ WinForms only: a JS interval cannot exist disabled — wired means running.
                new("Enabled", FormPropertyType.Bool, "false", Targets: new[] { FormTarget.WinForms },
                    Category: FormPropertyCategory.Behavior,
                    Description: "Enables generation of Elapsed events.")
            },
            Schematic: FormSchematic.Clock,
            Events: Ev("Tick", "tick", category: FormEventCategory.Behavior,
                description: "Occurs whenever the specified interval time elapses."),
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
                new("InitialDelay", FormPropertyType.Int, "500",
                    Category: FormPropertyCategory.Misc,
                    Description: "Determines the length of time the pointer must remain stationary within a ToolTip region before the ToolTip window appears."),
                new("AutoPopDelay", FormPropertyType.Int, "5000",
                    Category: FormPropertyCategory.Misc,
                    Description: "Determines the length of time the ToolTip window remains visible if the pointer is stationary inside a ToolTip region."),
                new("ReshowDelay", FormPropertyType.Int, "100",
                    Category: FormPropertyCategory.Misc,
                    Description: "Determines the length of time it takes for subsequent ToolTip windows to appear as the pointer moves from one ToolTip region to another."),
                new("ShowAlways", FormPropertyType.Bool, "false",
                    Category: FormPropertyCategory.Misc,
                    Description: "Determines if the tool tip will be displayed always, even if the parent window is not active."),
                new("IsBalloon", FormPropertyType.Bool, "false",
                    Category: FormPropertyCategory.Misc,
                    Description: "Indicates whether the ToolTip will take on a balloon form."),
                new("ToolTipTitle", FormPropertyType.String,
                    Category: FormPropertyCategory.Misc,
                    Description: "Determines the title of the ToolTip.")
            },
            Schematic: FormSchematic.Hint,
            Events: ToolTipEvents(),
            Place: FormPlace.Tray),

        new("ErrorProvider", "System.Windows.Forms.ErrorProvider", null, null, false, new List<FormPropertyDef>
            {
                new("BlinkStyle", FormPropertyType.Enum, "BlinkIfDifferentError",
                    new[] { "BlinkIfDifferentError", "AlwaysBlink", "NeverBlink" },
                    WinFormsEnumType: "ErrorBlinkStyle",
                    Category: FormPropertyCategory.Behavior,
                    Description: "Controls whether the error icon blinks when an error is set."),
                new("BlinkRate", FormPropertyType.Int, "250",
                    Category: FormPropertyCategory.Behavior,
                    Description: "The rate in milliseconds at which the error icon blinks.")
            },
            Schematic: FormSchematic.Alert,
            Events: Ev("RightToLeftChanged", category: FormEventCategory.PropertyChanged,
                description: "Occurs when the value of the RightToLeft property changes."),
            Place: FormPlace.Tray),

        // ⚠ Every row and the event carry an OracleExemption (BackgroundWorkerHasNoMetadata): on .NET the
        // type has no designer metadata, so the snapshot records absence, not values. Category Misc is
        // what VS shows for it on .NET (Framework files it under Asynchronous); the descriptions are WinForms'
        // own .NET Framework text (owner decision O4), never the designer's invention.
        new("BackgroundWorker", "System.ComponentModel.BackgroundWorker", null, null, false, new List<FormPropertyDef>
            {
                new("WorkerReportsProgress", FormPropertyType.Bool, "false",
                    Category: FormPropertyCategory.Misc,
                    Description: "Whether the worker will report progress.",
                    OracleExemption: BackgroundWorkerHasNoMetadata),
                new("WorkerSupportsCancellation", FormPropertyType.Bool, "false",
                    Category: FormPropertyCategory.Misc,
                    Description: "Whether the worker supports cancellation.",
                    OracleExemption: BackgroundWorkerHasNoMetadata)
            },
            Schematic: FormSchematic.Worker,
            Events: BackgroundWorkerEvents(),
            Place: FormPlace.Tray),

        // ==================================================================
        // Task 24 — menus, toolbars and status bars. Strips are Docked (no geometry, a Dock
        // PROPERTY); items live in their host's Children and are added by the HOST row's verb in
        // document order. ⛔ No Common(). ⛔ Enabled is WinForms-only except on the <input> —
        // a browser ignores `disabled` on <nav>/<li>/<span> (spec §4).
        // ==================================================================

        new("MenuStrip", "MenuStrip", "nav", null, false, new List<FormPropertyDef>
            {
                StripDock("Top"),
                StripEnabled,
                Visible
            },
            DefaultHeight: 24, Schematic: FormSchematic.MenuBar,
            Events: MenuStripEvents(),
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
                StripDock("Top"),
                // ⚠ WinForms' [DefaultValue] is Visible (spec §2.7's known disagreement, confirmed by the
                // parity run). The old "Hidden" displayed a grip the program never hid: placement writes
                // no GripStyle (only Dock), so an absent value always RAN Visible — and the canvas has
                // always drawn the grip. Nothing a placed strip does changes.
                new("GripStyle", FormPropertyType.Enum, "Visible", new[] { "Hidden", "Visible" },
                    WinFormsEnumType: "ToolStripGripStyle", Targets: new[] { FormTarget.WinForms },
                    Category: FormPropertyCategory.Appearance,
                    Description: "Specifies visibility of the grip on the ToolStrip."),
                StripEnabled,
                Visible
            },
            DefaultHeight: 25, Schematic: FormSchematic.ToolBar,
            Events: ToolStripEvents(),
            Place: FormPlace.Docked,
            Items: new FormItemRule(new[] { "ToolStripButton", "ToolStripSeparator" }, "{parent}.Items.Add({child})"),
            HtmlRole: "toolbar",
            WebCss: ".vgs-ToolStrip{display:flex;gap:4px;margin:0;padding:2px;background:#f0f0f0}"),

        new("StatusStrip", "StatusStrip", "footer", null, false, new List<FormPropertyDef>
            {
                // ⚠ Bottom is both WinForms' StatusStrip.Dock default (the parity run agrees) and the
                // placement seed FormPlacement writes.
                StripDock("Bottom"),
                // ⚠ WinForms' [DefaultValue] is true (spec §2.7's known disagreement, confirmed by the
                // parity run). Placement writes no SizingGrip, so an absent value always RAN with the grip,
                // and the canvas has always drawn it. Nothing a placed strip does changes.
                new("SizingGrip", FormPropertyType.Bool, "true", Targets: new[] { FormTarget.WinForms },
                    Category: FormPropertyCategory.Appearance,
                    Description: "Determines whether a StatusStrip has a sizing grip."),
                StripEnabled,
                Visible
            },
            DefaultHeight: 22, Schematic: FormSchematic.StatusBar,
            Events: StatusStripEvents(),
            Place: FormPlace.Docked,
            Items: new FormItemRule(new[] { "ToolStripStatusLabel" }, "{parent}.Items.Add({child})"),
            HtmlRole: "status",
            WebCss: ".vgs-StatusStrip{display:flex;gap:8px;padding:2px 6px;background:#f0f0f0;border-top:1px solid #ccc}"),

        new("ToolStripMenuItem", "ToolStripMenuItem", "li", null, false, new List<FormPropertyDef>
            {
                ItemText(),
                StripEnabled,
                ItemVisible,
                Checked with { Targets = new[] { FormTarget.WinForms } },
                ItemCheckOnClick(),
                ItemToolTipText(),
                // ⚠ WinForms only (D1): the text drawn beside a menu item for its shortcut; the page's <li> has none.
                new("ShortcutKeyDisplayString", FormPropertyType.String, Targets: new[] { FormTarget.WinForms },
                    Category: FormPropertyCategory.Appearance, Description: "The string to be displayed as the shortcut key.")
            },
            Schematic: FormSchematic.MenuItem,
            Events: ToolStripMenuItemEvents(),
            Place: FormPlace.Item,
            Items: new FormItemRule(new[] { "ToolStripMenuItem", "ToolStripSeparator" }, "{parent}.DropDownItems.Add({child})"),
            HtmlChildrenWrapper: "ul"),

        new("ToolStripSeparator", "ToolStripSeparator", "li", null, false, new List<FormPropertyDef>
            {
                ItemVisible
            },
            Schematic: FormSchematic.Separator,
            Events: Ev("Click", "click", category: FormEventCategory.Action, description: ItemClickedDescription),
            Place: FormPlace.Item, HtmlRole: "separator"),

        new("ToolStripButton", "ToolStripButton", "input", "button", false, new List<FormPropertyDef>
            {
                ItemText(),
                // Both targets here: the one strip item that IS an <input>, which honours `disabled`.
                Enabled,
                ItemVisible,
                // ⚠ ToolStripButton's own Checked text (the parity run).
                new("Checked", FormPropertyType.Bool, "false", Targets: new[] { FormTarget.WinForms },
                    Category: FormPropertyCategory.Appearance,
                    Description: "Indicates whether the ToolStripButton is pressed in or not pressed in."),
                ItemCheckOnClick(),
                ItemToolTipText(),
                // ⚠ WinForms' default is ImageAndText (the parity run; 'reset'). The old "Text" displayed a
                // value the program never ran: placement writes no DisplayStyle, so an absent value always
                // RAN ImageAndText — which, with no image, draws the text alone, as "Text" does.
                new("DisplayStyle", FormPropertyType.Enum, "ImageAndText", new[] { "None", "Text", "Image", "ImageAndText" },
                    WinFormsEnumType: "ToolStripItemDisplayStyle", Targets: new[] { FormTarget.WinForms },
                    Category: FormPropertyCategory.Appearance,
                    Description: "Specifies whether the image and text are rendered.")
            },
            Schematic: FormSchematic.ToolButton,
            Events: ToolStripButtonEvents(),
            Place: FormPlace.Item),

        new("ToolStripStatusLabel", "ToolStripStatusLabel", "span", null, false, new List<FormPropertyDef>
            {
                ItemText(),
                StripEnabled,
                ItemVisible,
                new("Spring", FormPropertyType.Bool, "false", Targets: new[] { FormTarget.WinForms },
                    Category: FormPropertyCategory.Appearance,
                    Description: "Specifies whether the item fills up the remaining space."),
                ItemToolTipText()
            },
            Schematic: FormSchematic.StatusLabel,
            Events: ToolStripStatusLabelEvents(),
            Place: FormPlace.Item),
    }.Select(WithWebExtras).ToList();

    /// <summary>
    /// The FORM's own definition (spec §2.3) — ⛔⛔ deliberately NOT in <see cref="All"/>.
    ///
    /// <para>FormDocument is not a FormControl, and ~10 consumers enumerate <see cref="All"/> (the
    /// toolbox, FormCatalogShapes.Canonical and every gate on it, WinFormsCatalogSweepTests, the render
    /// and coverage distinctness gates, the glyph gate, the reader/writer/clipboard's
    /// <c>Find(elementName) != null</c>). A Form row there would be offered in the toolbox, drawn as a
    /// control, and read as a control element. The grid and every root-aware writer ask for this one BY
    /// NAME instead; it gets its own csc sweep, parity rows and retarget sweep.</para>
    ///
    /// <para>⚠ Its values do NOT live in an attribute dictionary: Text, the client size and the web
    /// layout are typed FormDocument fields. <see cref="FormRootValues"/> is the ONE map from a row to
    /// its storage. Every other root row (slice 3's FormBorderStyle…) is Properties-stored: the root attribute of its
    /// own name, in <see cref="FormDocument.Properties"/>.</para>
    ///
    /// <para>⚠ Below <see cref="All"/> is fine: it reads no shared field (textual init order).</para>
    /// </summary>
    public static readonly FormControlDef FormRoot = new(
        Kind: "Form",
        WinFormsType: "Form",
        HtmlTag: "body",
        HtmlInputType: null,
        IsContainer: true,
        Properties: new List<FormPropertyDef>
        {
            // ⛔ Both targets (D2): the window caption, and the page <title> (Text ?? Name).
            new("Text", FormPropertyType.String,
                Category: FormPropertyCategory.Appearance, Description: "The text associated with the control."),

            // ⚠ CLIENT size, emitted `Me.ClientSize = New Size(w, h)` exactly as before — the form shows
            // ClientSize, never a second Size (spec §2.3). ⛔ Also a Canvas PAGE's design size (spec 2026-09-27
            // D2): stored as the root's Width/Height exactly as a .blform stores them. The web code-behind
            // emits no geometry (RegionWriter emits root rows on the WinForms branch only).
            new("ClientSize", FormPropertyType.Size, Targets: new[] { FormTarget.WinForms, FormTarget.Web },
                Category: FormPropertyCategory.Layout,
                Description: "The size of the client area of the form, in pixels.",
                OracleExemption: "ClientSize is not browsable in WinForms — VS shows Size. The designer " +
                                 "shows the CLIENT size because that is what its surface draws and what the " +
                                 "region writer emits (spec §2.3).",
                WebLayouts: new[] { FormLayoutKind.Canvas }),

            // Web only — kept verbatim as CSS track lists; the browser is the renderer (FormGridLayout).
            new("Cols", FormPropertyType.String, Targets: new[] { FormTarget.Web },
                Category: FormPropertyCategory.Layout,
                Description: "The page's column tracks, as a comma-separated CSS grid track list (e.g. 120px,1fr).",
                WebLayouts: new[] { FormLayoutKind.Grid }),
            new("Rows", FormPropertyType.String, Targets: new[] { FormTarget.Web },
                Category: FormPropertyCategory.Layout,
                Description: "The page's row tracks, as a comma-separated CSS grid track list (e.g. auto,auto).",
                WebLayouts: new[] { FormLayoutKind.Grid }),
            // ⚠ Grid AND Flow: the emitter writes `gap` for both (FormAssetEmitter.Css).
            new("Gap", FormPropertyType.String, Targets: new[] { FormTarget.Web },
                Category: FormPropertyCategory.Layout,
                Description: "The space between the page's grid cells or flow items, as a CSS length (e.g. 8px).",
                WebLayouts: new[] { FormLayoutKind.Grid, FormLayoutKind.Flow }),
            // ⛔ Web CANVAS only (spec 2026-09-27 §2.3, §5): below this page width the controls stack into one
            // column. 0 = never stack. Stored on <Layout> as raw text (FormLayout.MobileBreakpoint);
            // FormRootValues.StorageAttributes says so. ⚠ The default is FormLayout's constant, never a second copy.
            new("MobileBreakpoint", FormPropertyType.Int,
                FormLayout.DefaultMobileBreakpoint.ToString(CultureInfo.InvariantCulture),
                Targets: new[] { FormTarget.Web },
                Category: FormPropertyCategory.Layout,
                Description: "Below this page width, in pixels, the controls stack into one column for phones. 0 never stacks.",
                WebLayouts: new[] { FormLayoutKind.Canvas }),

            // ==========================================================
            // Slice 3 — the Form's D1 set (spec §2.3), PROPERTIES-STORED: each lives in FormDocument.Properties as the
            // root attribute of its own name (FormRootValues' default arm). WinForms' own metadata (the snapshot);
            // WinForms-only unless it maps cleanly onto the page's body (D2): BackColor, ForeColor, Font — and Icon
            // (slice 4 D-5f), the window's icon and the page's <link rel="icon">.
            // ==========================================================
            new("Icon", FormPropertyType.Icon,
                Category: FormPropertyCategory.WindowStyle,
                Description: "Indicates the icon for a form. This icon is displayed in the form's system menu box and when the form is minimized."),
            new("FormBorderStyle", FormPropertyType.Enum, "Sizable",
                new[] { "None", "FixedSingle", "Fixed3D", "FixedDialog", "Sizable", "FixedToolWindow", "SizableToolWindow" },
                WinFormsEnumType: "FormBorderStyle", Targets: new[] { FormTarget.WinForms },
                Category: FormPropertyCategory.Appearance,
                Description: "Indicates the appearance and behavior of the border and title bar of the form."),
            new("StartPosition", FormPropertyType.Enum, "WindowsDefaultLocation",
                new[] { "Manual", "CenterScreen", "WindowsDefaultLocation", "WindowsDefaultBounds", "CenterParent" },
                WinFormsEnumType: "FormStartPosition", Targets: new[] { FormTarget.WinForms },
                Category: FormPropertyCategory.Layout,
                Description: "Determines the position of a form when it first appears."),
            new("WindowState", FormPropertyType.Enum, "Normal", new[] { "Normal", "Minimized", "Maximized" },
                WinFormsEnumType: "FormWindowState", Targets: new[] { FormTarget.WinForms },
                Category: FormPropertyCategory.Layout,
                Description: "Determines the initial visual state of the form."),
            new("MinimumSize", FormPropertyType.Size, "0, 0", Targets: new[] { FormTarget.WinForms },
                Category: FormPropertyCategory.Layout,
                Description: "The minimum size the form can be resized to."),
            new("MaximumSize", FormPropertyType.Size, "0, 0", Targets: new[] { FormTarget.WinForms },
                Category: FormPropertyCategory.Layout,
                Description: "The maximum size the form can be resized to."),
            new("ControlBox", FormPropertyType.Bool, "true", Targets: new[] { FormTarget.WinForms },
                Category: FormPropertyCategory.WindowStyle,
                Description: "Determines whether a form has a Control/System menu box."),
            new("MaximizeBox", FormPropertyType.Bool, "true", Targets: new[] { FormTarget.WinForms },
                Category: FormPropertyCategory.WindowStyle,
                Description: "Determines whether a form has a maximize box in the upper-right corner of its caption bar."),
            new("MinimizeBox", FormPropertyType.Bool, "true", Targets: new[] { FormTarget.WinForms },
                Category: FormPropertyCategory.WindowStyle,
                Description: "Determines whether a form has a minimize box in the upper-right corner of its caption bar."),
            new("ShowIcon", FormPropertyType.Bool, "true", Targets: new[] { FormTarget.WinForms },
                Category: FormPropertyCategory.WindowStyle,
                Description: "Indicates whether an icon is displayed in the title bar of the form."),
            new("ShowInTaskbar", FormPropertyType.Bool, "true", Targets: new[] { FormTarget.WinForms },
                Category: FormPropertyCategory.WindowStyle,
                Description: "Determines whether the form appears in the Windows Taskbar."),
            new("TopMost", FormPropertyType.Bool, "false", Targets: new[] { FormTarget.WinForms },
                Category: FormPropertyCategory.WindowStyle,
                Description: "Indicates whether the form always appears above all other forms that do not have this property set to true."),
            // ⛔ References: emitted AFTER the controls (pre-flight B4) — before them the field is still Nothing.
            new("AcceptButton", FormPropertyType.Reference, Targets: new[] { FormTarget.WinForms },
                Category: FormPropertyCategory.Misc,
                Description: "The accept button of the form. If this is set, the button is 'clicked' whenever the user presses the 'ENTER' key.",
                ReferenceKinds: new[] { "Button" }),
            new("CancelButton", FormPropertyType.Reference, Targets: new[] { FormTarget.WinForms },
                Category: FormPropertyCategory.Misc,
                Description: "The cancel button of the form. If this property is set, the button is 'clicked' whenever the user presses the 'ESC' key.",
                ReferenceKinds: new[] { "Button" }),
            new("KeyPreview", FormPropertyType.Bool, "false", Targets: new[] { FormTarget.WinForms },
                Category: FormPropertyCategory.Misc,
                Description: "Determines whether keyboard events for controls on the form are registered with the form."),
            // ⛔ Both targets (D2): the page's BODY, so every control inherits them as WinForms' ambient properties
            // inherit from the Form — the Docked strips included. WebDefault empty: with nothing written the browser's
            // own body colours and font stand, not WinForms' Control/ControlText/Segoe UI.
            // A Form throws on a translucent BackColor (measured; use Opacity for a see-through window).
            new("BackColor", FormPropertyType.Color, "Control", WebDefault: "",
                Category: FormPropertyCategory.Appearance, Description: "The background color of the component.",
                CssProperty: "background-color", CssConverter: FormCssConverter.Color,
                WinFormsTranslucency: FormTranslucency.ThrowsOnForm),
            new("ForeColor", FormPropertyType.Color, "ControlText", WebDefault: "",
                Category: FormPropertyCategory.Appearance,
                Description: "The foreground color of this component, which is used to display text.",
                CssProperty: "color", CssConverter: FormCssConverter.Color),
            new("Font", FormPropertyType.Font, "Segoe UI, 9pt", WebDefault: "",
                Category: FormPropertyCategory.Appearance, Description: "The font used to display text in the control.",
                CssProperty: "font", CssConverter: FormCssConverter.Font),
            new("Opacity", FormPropertyType.Fraction, "1", Targets: new[] { FormTarget.WinForms },
                Category: FormPropertyCategory.WindowStyle, Description: "The opacity percentage of the control."),
        },
        Schematic: FormSchematic.Container,
        // Slice 5 D-3: the Form's events — Load (the default), and the web wiring each needs (ADR 0021).
        Events: FormRootEvents());

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
    /// Structural in the vocabulary a document of (<paramref name="target"/>, <paramref name="layout"/>)
    /// speaks (spec 2026-09-27 §2.1).
    ///
    /// <para>⛔ The two vocabularies overlap in spelling and not in meaning, so "structural" is not a
    /// property of the attribute name alone. <c>Width</c> is a pixel control's width and is read into its
    /// geometry; on a Grid page's control nothing reads it, so calling it structural there would make it
    /// neither a property nor an unknown attribute — absent from the model entirely, and silently dropped
    /// by any path that rebuilds the document from the model.</para>
    ///
    /// <para>⛔ By LAYOUT too, through <see cref="FormVocabulary.IsPixel(FormTarget, FormLayoutKind?)"/>: a
    /// Canvas page's control is laid out in pixels exactly as a .blform's is. Asking by target alone put its
    /// X/Width in UnknownAttributes, and the control had a position nothing read.</para>
    /// </summary>
    public static bool IsStructural(string attributeName, FormTarget target, FormLayoutKind? layout)
    {
        if (string.Equals(attributeName, "Id", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(attributeName, "TabIndex", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var vocabulary = FormVocabulary.IsPixel(target, layout) ? PixelAttributes : GridAttributes;
        return vocabulary.Any(a => string.Equals(a, attributeName, StringComparison.OrdinalIgnoreCase));
    }
}
