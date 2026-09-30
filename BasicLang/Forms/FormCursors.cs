namespace BasicLang.Forms;

/// <summary>
/// The <see cref="FormPropertyType.Cursor"/> vocabulary (spec §2.2): the static members of
/// <c>System.Windows.Forms.Cursors</c> — reflected on the owner's machine (slice 3 pre-flight §3) and gated by csc
/// (<c>WinFormsCatalogSweepTests.EveryCursor_…</c>) — each with its CSS <c>cursor</c> keyword, owned HERE and nowhere else.
///
/// <para>⛔ A member with no CSS equivalent (the up arrow, the middle-button pan cursors) maps to NULL: it is
/// WinForms-only as a VALUE, refused on a web form with a reason — the rule system colours already follow — never
/// approximated with a different cursor the user did not choose.</para>
/// </summary>
public static class FormCursors
{
    // ⚠ Reflection order of Cursors' properties — also the order the grid offers them.
    private static readonly (string Name, string? Css)[] Table =
    {
        ("AppStarting", "progress"),
        ("Arrow", "default"),
        ("Cross", "crosshair"),
        ("Default", "default"),
        ("IBeam", "text"),
        ("No", "not-allowed"),
        ("SizeAll", "move"),
        ("SizeNESW", "nesw-resize"),
        ("SizeNS", "ns-resize"),
        ("SizeNWSE", "nwse-resize"),
        ("SizeWE", "ew-resize"),
        ("UpArrow", null),
        ("WaitCursor", "wait"),
        ("Help", "help"),
        ("HSplit", "row-resize"),
        ("VSplit", "col-resize"),
        ("NoMove2D", "all-scroll"),
        ("NoMoveHoriz", null),
        ("NoMoveVert", null),
        ("PanEast", null),
        ("PanNE", null),
        ("PanNorth", null),
        ("PanNW", null),
        ("PanSE", null),
        ("PanSouth", null),
        ("PanSW", null),
        ("PanWest", null),
        ("Hand", "pointer")
    };

    /// <summary>Every member name, in the table's order.</summary>
    public static IReadOnlyList<string> Names { get; } = Table.Select(t => t.Name).ToList();

    /// <summary>The member as <c>Cursors</c> spells it, matched ignoring case.</summary>
    public static bool TryCanonical(string? value, out string canonical)
    {
        canonical = Names.FirstOrDefault(n => string.Equals(n, value, StringComparison.OrdinalIgnoreCase)) ?? "";
        return canonical.Length > 0;
    }

    /// <summary>The CSS <c>cursor</c> keyword for a member, or null when CSS has none (or it is not a member).</summary>
    public static string? CssFor(string value) =>
        TryCanonical(value, out var canonical) ? Table.First(t => t.Name == canonical).Css : null;
}
