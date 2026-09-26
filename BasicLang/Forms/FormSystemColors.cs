namespace BasicLang.Forms;

/// <summary>
/// Every <c>System.Drawing.SystemColors</c> member, and the CSS Color 4 system colour that means the
/// same thing — null where CSS has none (spec §2.2). The ONE table: the WinForms literal, the web
/// declaration and the web refusal all read it.
///
/// <para>⚠ Conservative on purpose. A mapping that is only approximately right (ControlDark → a
/// deprecated ThreeDShadow) is a colour the page shows and the window does not; null makes the value
/// WinForms-only and SAYS so, which is the honest answer.</para>
/// </summary>
public static class FormSystemColors
{
    private static readonly (string Name, string? Css)[] Table =
    {
        ("ActiveBorder", null), ("ActiveCaption", null), ("ActiveCaptionText", null),
        ("AppWorkspace", null), ("ButtonFace", "ButtonFace"), ("ButtonHighlight", null),
        ("ButtonShadow", null), ("Control", "ButtonFace"), ("ControlDark", null),
        ("ControlDarkDark", null), ("ControlLight", null), ("ControlLightLight", null),
        ("ControlText", "ButtonText"), ("Desktop", null), ("GradientActiveCaption", null),
        ("GradientInactiveCaption", null), ("GrayText", "GrayText"), ("Highlight", "Highlight"),
        ("HighlightText", "HighlightText"),
        // HotTrack is the hot-tracked item's colour; CSS LinkText is an unvisited link's — related,
        // not the same, so by the rule above it is refused rather than approximated.
        ("HotTrack", null), ("InactiveBorder", null),
        ("InactiveCaption", null), ("InactiveCaptionText", null), ("Info", null), ("InfoText", null),
        ("Menu", null), ("MenuBar", null), ("MenuHighlight", null), ("MenuText", null),
        ("ScrollBar", null), ("Window", "Canvas"), ("WindowFrame", null), ("WindowText", "CanvasText"),
    };

    // ⛔ Declared AFTER Table: static initializers run in textual order.
    private static readonly Dictionary<string, (string Name, string? Css)> ByName =
        Table.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Every system colour, in SystemColors' own spelling.</summary>
    public static IReadOnlyList<string> Names { get; } = Table.Select(t => t.Name).ToList();

    /// <summary>True when <paramref name="value"/> names a system colour; <paramref name="name"/> is its canonical spelling.</summary>
    public static bool TryCanonical(string value, out string name)
    {
        if (ByName.TryGetValue(value, out var entry))
        {
            name = entry.Name;
            return true;
        }

        name = "";
        return false;
    }

    /// <summary>The CSS system colour for a canonical system-colour name, or null when CSS has none.</summary>
    public static string? CssFor(string name) => ByName.TryGetValue(name, out var entry) ? entry.Css : null;
}
