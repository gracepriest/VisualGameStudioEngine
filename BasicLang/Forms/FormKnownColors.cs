namespace BasicLang.Forms;

/// <summary>
/// Every named colour that is a static property of <c>System.Drawing.Color</c> — <c>Color.Red</c>,
/// <c>Color.Transparent</c> — in Color's own spelling. The ONE list that lets the catalog PROVE a
/// <c>Color.X</c> value is source rather than guess from its shape (<see cref="FormPropertyDef.IsSourceForm"/>).
///
/// <para>⛔ Not the system colours: <c>Color.Control</c> does not exist (they are
/// <see cref="FormSystemColors"/>, emitted as <c>SystemColors.X</c>). And not <c>RebeccaPurple</c>, which
/// .NET Framework's WinForms does not have. <c>FormPropertyDefTests.TheKnownColorTable_IsExactlyColorsNamedProperties</c>
/// checks this hand list against the real type by reflection.</para>
/// </summary>
public static class FormKnownColors
{
    private static readonly string[] Table =
    {
        "Transparent", "AliceBlue", "AntiqueWhite", "Aqua", "Aquamarine", "Azure", "Beige", "Bisque", "Black",
        "BlanchedAlmond", "Blue", "BlueViolet", "Brown", "BurlyWood", "CadetBlue", "Chartreuse", "Chocolate",
        "Coral", "CornflowerBlue", "Cornsilk", "Crimson", "Cyan", "DarkBlue", "DarkCyan", "DarkGoldenrod",
        "DarkGray", "DarkGreen", "DarkKhaki", "DarkMagenta", "DarkOliveGreen", "DarkOrange", "DarkOrchid",
        "DarkRed", "DarkSalmon", "DarkSeaGreen", "DarkSlateBlue", "DarkSlateGray", "DarkTurquoise", "DarkViolet",
        "DeepPink", "DeepSkyBlue", "DimGray", "DodgerBlue", "Firebrick", "FloralWhite", "ForestGreen", "Fuchsia",
        "Gainsboro", "GhostWhite", "Gold", "Goldenrod", "Gray", "Green", "GreenYellow", "Honeydew", "HotPink",
        "IndianRed", "Indigo", "Ivory", "Khaki", "Lavender", "LavenderBlush", "LawnGreen", "LemonChiffon",
        "LightBlue", "LightCoral", "LightCyan", "LightGoldenrodYellow", "LightGray", "LightGreen", "LightPink",
        "LightSalmon", "LightSeaGreen", "LightSkyBlue", "LightSlateGray", "LightSteelBlue", "LightYellow", "Lime",
        "LimeGreen", "Linen", "Magenta", "Maroon", "MediumAquamarine", "MediumBlue", "MediumOrchid",
        "MediumPurple", "MediumSeaGreen", "MediumSlateBlue", "MediumSpringGreen", "MediumTurquoise",
        "MediumVioletRed", "MidnightBlue", "MintCream", "MistyRose", "Moccasin", "NavajoWhite", "Navy", "OldLace",
        "Olive", "OliveDrab", "Orange", "OrangeRed", "Orchid", "PaleGoldenrod", "PaleGreen", "PaleTurquoise",
        "PaleVioletRed", "PapayaWhip", "PeachPuff", "Peru", "Pink", "Plum", "PowderBlue", "Purple", "Red",
        "RosyBrown", "RoyalBlue", "SaddleBrown", "Salmon", "SandyBrown", "SeaGreen", "SeaShell", "Sienna", "Silver",
        "SkyBlue", "SlateBlue", "SlateGray", "Snow", "SpringGreen", "SteelBlue", "Tan", "Teal", "Thistle", "Tomato",
        "Turquoise", "Violet", "Wheat", "White", "WhiteSmoke", "Yellow", "YellowGreen",
    };

    // ⛔ Declared AFTER Table: static initializers run in textual order. IsMember is ORDINAL — `Color.red`
    // is CS0117 in the generated C#, whatever BasicLang's own case rules say — while a DOCUMENT value is
    // matched case-insensitively and written in the table's spelling (TryCanonical).
    private static readonly HashSet<string> Exact = new(Table, StringComparer.Ordinal);

    private static readonly Dictionary<string, string> ByName =
        Table.ToDictionary(n => n, StringComparer.OrdinalIgnoreCase);

    /// <summary>Every named colour, in Color's own spelling.</summary>
    public static IReadOnlyList<string> Names { get; } = Table;

    /// <summary>True when <paramref name="member"/> is, exactly and case-sensitively, a named <c>Color</c> property.</summary>
    public static bool IsMember(string member) => Exact.Contains(member);

    /// <summary>True when <paramref name="value"/> names a colour, in any case; <paramref name="name"/> is Color's spelling.</summary>
    public static bool TryCanonical(string value, out string name)
    {
        if (ByName.TryGetValue(value, out var found))
        {
            name = found;
            return true;
        }

        name = "";
        return false;
    }
}
