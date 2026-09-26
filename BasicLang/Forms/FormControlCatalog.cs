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

        return value;
    }

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
            Type is FormPropertyType.Enum or FormPropertyType.Color
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);

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
            _ => null
        };
    }

    /// <summary>
    /// Document text as a BasicLang string literal, quotes included — the ONE escape every generated
    /// string goes through.
    ///
    /// <para>⛔ Not just <c>"</c> → <c>""</c>. The BasicLang lexer treats <c>\</c> as an escape
    /// (<c>\n \r \t \\ \"</c>, and any other character as itself), so a caption <c>a\b</c> written
    /// raw lexes as <c>ab</c> — the backslash silently gone from the running program. A raw line break
    /// is worse: it lands inside the generated region, and a CR LF caption in an LF file makes the next
    /// write read the file's newline style from the caption and rewrite every line ending.</para>
    /// </summary>
    public static string StringLiteral(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length + 2).Append('"');
        foreach (var c in text)
        {
            sb.Append(c switch
            {
                '"' => "\"\"",
                '\\' => @"\\",
                '\r' => @"\r",
                '\n' => @"\n",
                '\t' => @"\t",
                _ => c.ToString()
            });
        }

        return sb.Append('"').ToString();
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
            _ => false
        };
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
    public string DescribeRefusal(string value, FormTarget target)
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
                   "web form cannot use it. The value is preserved exactly as written.";
        }

        if (IsUnknownColourNameRefusedOn(value, target))
        {
            return $"'{value}' is not a named colour WinForms knows (System.Drawing.Color has no such " +
                   "member, and it is not a system colour), so a WinForms form cannot use it. " +
                   "The value is preserved exactly as written.";
        }

        return $"'{value}' is not a valid {Type}" +
               (AllowedValues is { Count: > 0 } ? $" (expected one of: {string.Join(", ", AllowedValues)})" : "") +
               ". The value is preserved exactly as written.";
    }

    /// <summary>
    /// THE target-specific refusal — the one predicate <see cref="Accepts(string?, FormTarget)"/> and
    /// <see cref="DescribeRefusal"/> share, each arm of it also asked by DescribeRefusal for its reason.
    /// Only a COLOUR row refuses by target: a String caption reading "Window" is text, and an Enum
    /// member named "Menu" is that enum's business.
    /// </summary>
    private bool IsRefusedOn(string value, FormTarget target) =>
        IsSystemColourRefusedOn(value, target) || IsUnknownColourNameRefusedOn(value, target);

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
    private static string? FromArgbSource(string value)
    {
        const string prefix = "Color.FromArgb(";
        if (!value.StartsWith(prefix, StringComparison.Ordinal) || !value.EndsWith(")", StringComparison.Ordinal))
        {
            return null;
        }

        var parts = value.Substring(prefix.Length, value.Length - prefix.Length - 1).Split(',');
        if (parts.Length != 4)
        {
            return null;
        }

        var argb = new int[4];
        for (var i = 0; i < 4; i++)
        {
            if (!int.TryParse(parts[i].Trim(' '), NumberStyles.None, CultureInfo.InvariantCulture, out argb[i]) ||
                argb[i] > 255)
            {
                return null;
            }
        }

        return FromArgbLiteral(argb[0], argb[1], argb[2], argb[3]);
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
/// The kind's events (spec §2.5). The <see cref="FormEventDef.IsDefault"/> entry is what a double-click
/// means and what the old single-event fields now DERIVE from — <see cref="WinFormsEvent"/>,
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
    string? WebCss = null)
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

    private static readonly FormPropertyDef WindowBackColor = BackColor with { Default = "Window", WebDefault = "" };

    private static readonly FormPropertyDef HighlightForeColor = ForeColor with { Default = "Highlight", WebDefault = "" };

    /// <summary>
    /// A shared colour row on a kind where WinForms marks it <c>[Browsable(false)]</c> (measured by
    /// reflection, Task 8): VS does not list it, so the snapshot has nothing to judge. The designer keeps
    /// offering it — removing a row is a product decision recorded for slice 3, not a parity fix.
    /// </summary>
    private static FormPropertyDef HiddenInWinForms(FormPropertyDef shared, string kind, bool onTheWeb) =>
        shared with
        {
            OracleExemption = $"{kind}.{shared.Name} is [Browsable(false)] in WinForms (measured by reflection) — " +
                              "VS does not list it. The designer offers it through the shared colour rows; csc " +
                              "gates the name" + (onTheWeb ? ", and on the web it is the element's CSS" : "") +
                              ". Whether a WinForms-hidden colour should be offered at all is a slice-3 decision."
        };

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

    private static IReadOnlyList<FormEventDef> StripItemClicked() =>
        Ev("ItemClicked", "click", args: "ToolStripItemClickedEventArgs",
            category: FormEventCategory.Action, description: ItemClickedDescription);

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
    private const string BackgroundWorkerHasNoMetadata =
        "System.ComponentModel.BackgroundWorker carries no [DefaultValue]/[Category]/[Description] on .NET " +
        "(measured by reflection), so the snapshot's 'serialized'/Misc/empty text records the ABSENCE of " +
        "metadata, not a value. A fresh instance reads False (measured) — what an absent attribute means.";

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

    private static IReadOnlyList<FormPropertyDef> Common(params FormPropertyDef[] own) =>
        CommonColoured(ForeColor, BackColor, own);

    /// <summary>
    /// <see cref="Common"/> with a kind's OWN colour rows — where the parity run showed the colour is not
    /// ambient on that kind (TextBox's Window) or is hidden there. Same order as <see cref="Common"/>, so
    /// the grid does not reshuffle.
    /// </summary>
    private static IReadOnlyList<FormPropertyDef> CommonColoured(
        FormPropertyDef foreColor, FormPropertyDef backColor, params FormPropertyDef[] own) =>
        own.Concat(new[] { Enabled, Visible, foreColor, backColor }).ToList();

    // Control.Click's WinForms metadata — the most-shared default event (Task 8's parity run).
    private const string ClickedDescription = "Occurs when the component is clicked.";

    // A ToolStripItem's Click and a strip's ItemClicked share this text in WinForms.
    private const string ItemClickedDescription = "Occurs when the item is clicked.";

    // Shared by ComboBox, ListBox, CheckedListBox and TabControl (ListView words its own differently).
    private const string SelectedIndexChangedDescription = "Occurs when the value of the SelectedIndex property changes.";

    // ListBox and CheckedListBox (ComboBox says "combo box").
    private const string ListBoxItemsDescription = "The items in the list box.";

    // DateTimePicker and TrackBar.
    private const string ControlValueChangedDescription = "Occurs when the value of the control changes.";

    /// <summary>
    /// A row's events when it declares only its DEFAULT one — every row today (the D1 event lists
    /// arrive in slice 5). Category and Description are WinForms' own, from the parity run (Task 8).
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
        new("Label",       "Label",       "label",    null,       false, Common(Text, LabelTextAlign),
            DefaultWidth: 100, DefaultHeight: 23, Schematic: FormSchematic.Text,
            Events: Ev("Click", "click", category: FormEventCategory.Action, description: ClickedDescription)),
        new("TextBox",     "TextBox",     "input",    "text",     false, CommonColoured(WindowTextForeColor, WindowBackColor,
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
                Description: "Indicates the character to display for password input for single-line edit controls.")),
            DefaultWidth: 100, DefaultHeight: 23,
            // ⚠ The DOM has no TextChanged. `input` fires per keystroke, which is what TextChanged
            // means; `change` fires on blur and would be a different gesture wearing the same name.
            Events: Ev("TextChanged", "input", category: FormEventCategory.PropertyChanged,
                description: "Event raised when the value of the Text property is changed on Control.")),
        new("Button",      "Button",      "button",   null,       false, Common(Text, ButtonTextAlign),
            DefaultWidth: 75, DefaultHeight: 23, Schematic: FormSchematic.Button,
            Events: Ev("Click", "click", category: FormEventCategory.Action, description: ClickedDescription)),
        new("CheckBox",    "CheckBox",    "input",    "checkbox", false, Common(Text, Checked),
            DefaultWidth: 104, DefaultHeight: 24, Schematic: FormSchematic.Check,
            Events: Ev("CheckedChanged", "change", category: FormEventCategory.Misc,
                description: "Occurs whenever the Check property is changed.")),
        new("RadioButton", "RadioButton", "input",    "radio",    false, Common(
            Text,
            RadioChecked,
            // ⛔ WEB ONLY. The DOM groups radios by name=; WinForms groups them by CONTAINER and
            // has no GroupName property at all — csc says CS1061, BasicLang says nothing.
            new FormPropertyDef("GroupName", FormPropertyType.String,
                Targets: new[] { FormTarget.Web },
                Category: FormPropertyCategory.Behavior,
                Description: "The radio group this button belongs to on the page: buttons sharing a name are mutually exclusive.")),
            DefaultWidth: 104, DefaultHeight: 24, Schematic: FormSchematic.Radio,
            Events: Ev("CheckedChanged", "change", category: FormEventCategory.Misc,
                description: "Occurs whenever the 'checked' property changes value.")),
        new("ComboBox",    "ComboBox",    "select",   null,       false, CommonColoured(WindowTextForeColor, WindowBackColor,
            Text,
            // Items is a get-only collection on WinForms — assigning it is CS0200.
            new FormPropertyDef("Items", FormPropertyType.String, IsItemCollection: true,
                Category: FormPropertyCategory.Data, Description: "The items in the combo box."),
            SelectedIndex(SelectedIndexForTheWeb)),
            DefaultWidth: 121, DefaultHeight: 23, Schematic: FormSchematic.Dropdown,
            Events: Ev("SelectedIndexChanged", "change", category: FormEventCategory.Behavior,
                description: SelectedIndexChangedDescription)),
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
                Description: "Allows more than one item to be selected at a time on the page.")),
            DefaultWidth: 120, DefaultHeight: 95, Schematic: FormSchematic.List,
            Events: Ev("SelectedIndexChanged", "change", category: FormEventCategory.Behavior,
                description: SelectedIndexChangedDescription)),
        new("Panel",       "Panel",       "div",      null,       true,  Common(
            new FormPropertyDef("BorderStyle", FormPropertyType.Enum, "None",
                new[] { "None", "FixedSingle", "Fixed3D" },
                WinFormsEnumType: "BorderStyle",
                Category: FormPropertyCategory.Appearance,
                Description: "Indicates whether the panel should have a border.")),
            DefaultWidth: 200, DefaultHeight: 100, Schematic: FormSchematic.Container,
            // ⚠ VS opens a Panel on Paint. That handler takes a PaintEventArgs and is for drawing,
            // not for a gesture — Click is the event a double-click in THIS designer can honestly
            // stub, and the Events tab (Task 23) is where the rest will be reachable.
            Events: Ev("Click", "click", category: FormEventCategory.Action, description: ClickedDescription)),
        new("GroupBox",    "GroupBox",    "fieldset", null,       true,  Common(Text),
            DefaultWidth: 200, DefaultHeight: 100, Schematic: FormSchematic.Group,
            // ⚠ GroupBox.Click is [Browsable(false)] (VS's default event is Enter) — measured by reflection
            // in Task 8, which also measured that a real click DOES raise it. Kept: it is the gesture a
            // double-click here can honestly stub on both targets. Metadata is Control.Click's.
            Events: Ev("Click", "click", category: FormEventCategory.Action, description: ClickedDescription,
                exemption: "GroupBox.Click is [Browsable(false)] in WinForms — VS lists Enter as the default " +
                           "event instead — but it is raised by a real click (measured by reflection and a " +
                           "simulated WM_LBUTTONDOWN/UP, Task 8), so the stub is live; csc gates the name")),
        new("PictureBox",  "PictureBox",  "img",      null,       false, CommonColoured(
            HiddenInWinForms(ForeColor, "PictureBox", onTheWeb: true), BackColor,
            // WinForms Image is a System.Drawing.Image, not a path string (CS0029).
            new FormPropertyDef("Image", FormPropertyType.String,
                WinFormsFactory: "Image.FromFile",
                Category: FormPropertyCategory.Appearance,
                Description: "The image displayed in the PictureBox."),
            new FormPropertyDef("SizeMode", FormPropertyType.Enum, "Normal",
                new[] { "Normal", "StretchImage", "AutoSize", "CenterImage", "Zoom" },
                WinFormsEnumType: "PictureBoxSizeMode",
                Category: FormPropertyCategory.Behavior,
                Description: "Controls how the PictureBox will handle image placement and control sizing.")),
            DefaultWidth: 100, DefaultHeight: 50, Schematic: FormSchematic.Image,
            Events: Ev("Click", "click", category: FormEventCategory.Action, description: ClickedDescription)),

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
        new("LinkLabel",   "LinkLabel",   "a",        null,       false, Common(Text, LinkLabelTextAlign),
            DefaultWidth: 100, DefaultHeight: 23, Schematic: FormSchematic.Link,
            // ⚠ The typed args (Task 8's parity run): EventArgs compiled by contravariance but hid e.Link.
            Events: Ev("LinkClicked", "click", args: "LinkLabelLinkClickedEventArgs",
                category: FormEventCategory.Action, description: "Occurs when the link is clicked.")),

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
                Description: "Indicates the number of decimal places to display.")),
            DefaultWidth: 120, DefaultHeight: 23, Schematic: FormSchematic.Spinner,
            Events: Ev("ValueChanged", "input", category: FormEventCategory.Action,
                description: "Occurs when the value in the up-down control changes.")),

        new("DateTimePicker", "DateTimePicker", "input", "date",  false, CommonColoured(
            HiddenInWinForms(ForeColor, "DateTimePicker", onTheWeb: true),
            HiddenInWinForms(BackColor, "DateTimePicker", onTheWeb: true),
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
                Description: "Indicates whether a spin box rather than a drop-down calendar is displayed for modifying the control value.")),
            DefaultWidth: 200, DefaultHeight: 23, Schematic: FormSchematic.DatePicker,
            Events: Ev("ValueChanged", "change", category: FormEventCategory.Action,
                description: ControlValueChangedDescription)),

        new("TrackBar",    "TrackBar",    "input",    "range",    false, CommonColoured(
            HiddenInWinForms(ForeColor, "TrackBar", onTheWeb: true), BackColor,
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
                Description: "The orientation of the control.")),
            DefaultWidth: 150, DefaultHeight: 45, Schematic: FormSchematic.Slider,
            Events: Ev("ValueChanged", "input", category: FormEventCategory.Action,
                description: ControlValueChangedDescription)),

        new("ProgressBar", "ProgressBar", "progress", null,       false, CommonColoured(HighlightForeColor, BackColor,
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
            Events: Ev("Click", "click", category: FormEventCategory.Action, description: ClickedDescription)),

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
                Description: "Indicates if the check box should be toggled with the first click on an item.")),
            DefaultWidth: 160, DefaultHeight: 95, Schematic: FormSchematic.CheckList,
            Events: Ev("SelectedIndexChanged", category: FormEventCategory.Behavior,
                description: SelectedIndexChangedDescription)),

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
            Events: Ev("SelectedIndexChanged", category: FormEventCategory.Behavior,
                description: "Occurs whenever the 'SelectedIndex' property for this ListView changes.")),

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
                Description: "The indentation width of child nodes in pixels.")),
            DefaultWidth: 180, DefaultHeight: 140, Schematic: FormSchematic.Tree,
            // ⚠ The typed args (Task 8's parity run): EventArgs compiled by contravariance but hid e.Node.
            Events: Ev("AfterSelect", args: "TreeViewEventArgs", category: FormEventCategory.Behavior,
                description: "Occurs when the selection has been changed.")),

        new("DataGridView", "DataGridView", null,     null,       false, CommonColoured(
            HiddenInWinForms(ForeColor, "DataGridView", onTheWeb: false),
            HiddenInWinForms(BackColor, "DataGridView", onTheWeb: false),
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
            Events: Ev("CellClick", args: "DataGridViewCellEventArgs", category: FormEventCategory.Mouse,
                description: "Occurs when any part of the cell is clicked.")),

        new("TabControl",  "TabControl",  null,       null,       true,  CommonColoured(
            HiddenInWinForms(ForeColor, "TabControl", onTheWeb: false),
            HiddenInWinForms(BackColor, "TabControl", onTheWeb: false),
            new FormPropertyDef("Alignment", FormPropertyType.Enum, "Top",
                new[] { "Top", "Bottom", "Left", "Right" },
                WinFormsEnumType: "TabAlignment",
                Category: FormPropertyCategory.Behavior,
                Description: "Determines whether the tabs appear on the top, bottom, left, or right side of the Control (left or right are implicitly multilined)."),
            new FormPropertyDef("Multiline", FormPropertyType.Bool, "false",
                Category: FormPropertyCategory.Behavior,
                Description: "Indicates if more than one row of tabs is allowed."),
            SelectedIndex(SelectedIndexWinFormsOnly)),
            DefaultWidth: 240, DefaultHeight: 160, Schematic: FormSchematic.Tabs,
            Events: Ev("SelectedIndexChanged", category: FormEventCategory.Behavior,
                description: SelectedIndexChangedDescription)),

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
                Description: "Determines if the splitter can move.")),
            DefaultWidth: 260, DefaultHeight: 140, Schematic: FormSchematic.Split,
            // ⚠ The typed args (Task 8's parity run).
            Events: Ev("SplitterMoved", args: "SplitterEventArgs", category: FormEventCategory.Behavior,
                description: "Occurs when the splitter is done being moved.")),

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
            Events: Ev("Click", category: FormEventCategory.Action, description: ClickedDescription)),

        new("TableLayoutPanel", "TableLayoutPanel", null, null,   true,  Common(
            // ⚠ WinForms' [DefaultValue] is 0 for both (the parity run). VS DROPS a TableLayoutPanel as
            // 2×2 — a designer PREFERENCE, which spec §2.7 says is written at placement, never encoded as
            // a false default. Placement does not write it in slice 1 (plan Task 8 step 4); recorded for
            // the owner as a slice-3 decision.
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
            Events: Ev("Click", category: FormEventCategory.Action, description: ClickedDescription)),

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
            Events: Ev("Popup", args: "PopupEventArgs", category: FormEventCategory.Behavior,
                description: "Occurs whenever a ToolTip is about to be shown."),
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
        // what VS shows for it on .NET; the descriptions are the designer's own.
        new("BackgroundWorker", "System.ComponentModel.BackgroundWorker", null, null, false, new List<FormPropertyDef>
            {
                new("WorkerReportsProgress", FormPropertyType.Bool, "false",
                    Category: FormPropertyCategory.Misc,
                    Description: "Whether the worker can report progress (ReportProgress raises ProgressChanged).",
                    OracleExemption: BackgroundWorkerHasNoMetadata),
                new("WorkerSupportsCancellation", FormPropertyType.Bool, "false",
                    Category: FormPropertyCategory.Misc,
                    Description: "Whether the worker supports cancellation (CancelAsync sets CancellationPending).",
                    OracleExemption: BackgroundWorkerHasNoMetadata)
            },
            Schematic: FormSchematic.Worker,
            Events: Ev("DoWork", args: "System.ComponentModel.DoWorkEventArgs", category: FormEventCategory.Misc,
                description: "Occurs when RunWorkerAsync is called; the handler runs on a background thread.",
                exemption: "System.ComponentModel.BackgroundWorker carries no [Category]/[Description] on .NET " +
                           "(measured), so the snapshot's Misc/empty text records the absence of metadata; the " +
                           "description is the designer's own. The args are still WinForms' (DoWorkEventArgs)."),
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
            Events: StripItemClicked(),
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
            Events: StripItemClicked(),
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
            Events: StripItemClicked(),
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
                ItemToolTipText()
            },
            Schematic: FormSchematic.MenuItem,
            Events: Ev("Click", "click", category: FormEventCategory.Action, description: ItemClickedDescription),
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
            Events: Ev("Click", "click", category: FormEventCategory.Action, description: ItemClickedDescription),
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
            Events: Ev("Click", "click", category: FormEventCategory.Action, description: ItemClickedDescription),
            Place: FormPlace.Item),
    };

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
    /// its storage. Properties-stored root rows (FormBorderStyle…) arrive in slice 3.</para>
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
            // ClientSize, never a second Size (spec §2.3).
            new("ClientSize", FormPropertyType.Size, Targets: new[] { FormTarget.WinForms },
                Category: FormPropertyCategory.Layout,
                Description: "The size of the client area of the form, in pixels.",
                OracleExemption: "ClientSize is not browsable in WinForms — VS shows Size. The designer " +
                                 "shows the CLIENT size because that is what its surface draws and what the " +
                                 "region writer emits (spec §2.3)."),

            // Web only — kept verbatim as CSS track lists; the browser is the renderer (FormGridLayout).
            new("Cols", FormPropertyType.String, Targets: new[] { FormTarget.Web },
                Category: FormPropertyCategory.Layout,
                Description: "The page's column tracks, as a comma-separated CSS grid track list (e.g. 120px,1fr)."),
            new("Rows", FormPropertyType.String, Targets: new[] { FormTarget.Web },
                Category: FormPropertyCategory.Layout,
                Description: "The page's row tracks, as a comma-separated CSS grid track list (e.g. auto,auto)."),
            new("Gap", FormPropertyType.String, Targets: new[] { FormTarget.Web },
                Category: FormPropertyCategory.Layout,
                Description: "The space between the page's grid cells, as a CSS length (e.g. 8px)."),
        },
        Schematic: FormSchematic.Container);

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
