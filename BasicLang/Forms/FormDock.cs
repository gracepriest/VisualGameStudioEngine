namespace BasicLang.Forms;

/// <summary>
/// ⛔⛔ THE one parser for a POSITIONED control's <c>Dock</c> (<see cref="PixelGeometry.Dock"/>) — WinForms'
/// <c>DockStyle</c>. Three readers ask it and nothing else: the region writer's refusal
/// (<c>RegionWriter.CheckDocks</c>, BL8033), the WinForms emitter (<c>RegionWriter.AppendPixelGeometry</c>) and the
/// dock resolver the canvas and the page share (<see cref="FormDockLayout.EdgeOf"/>). Before it existed the emitter
/// spliced the value verbatim and the resolver matched it loosely, so <c>Dock="fill"</c> docked on the canvas and
/// emitted <c>DockStyle.fill</c> — CS0117 at csc, BasicLang silent (plan 2026-09-27 S11) — and <c>Dock="Rigth"</c>
/// did not dock on the canvas and emitted <c>DockStyle.Rigth</c>.
///
/// <para>⛔ <b>The rule.</b> A value is matched TRIMMED and CASE-INSENSITIVELY against the six members
/// (<see cref="Styles"/>); <see cref="Canonical"/> returns the member's own spelling and the emitter writes only
/// that. A blank value means "not docked" and is never an error. Anything else — a misspelling, a list, a source
/// form such as <c>DockStyle.Fill</c> — is not a member and is refused before anything is emitted.</para>
///
/// <para>⚠ A STRIP's Dock is a different thing: a <see cref="FormPlace.Docked"/> row's catalog PROPERTY, read
/// through <see cref="FormControl.IsDockedToBottom"/> and emitted through the row's Enum rule (case-insensitive,
/// NOT trimmed). A value the row does not accept (<c>Left</c>, <c> Bottom </c>) is Degraded, not refused (D9): never
/// emitted, and read as the row default by the canvas and the page — where WinForms then runs it too.</para>
/// </summary>
public static class FormDock
{
    /// <summary>DockStyle's members, in the enum's own spelling and order.</summary>
    public static readonly IReadOnlyList<string> Styles = new[] { "None", "Top", "Bottom", "Left", "Right", "Fill" };

    /// <summary>True when <paramref name="value"/> sets no dock at all — absent or whitespace.</summary>
    public static bool IsBlank(string? value) => string.IsNullOrWhiteSpace(value);

    /// <summary>
    /// The DockStyle member <paramref name="value"/> names, in the enum's own spelling — trimmed, any case — or null
    /// when it is blank or names no member.
    /// </summary>
    public static string? Canonical(string? value)
    {
        if (IsBlank(value))
        {
            return null;
        }

        var name = value!.Trim();
        foreach (var style in Styles)
        {
            if (string.Equals(style, name, StringComparison.OrdinalIgnoreCase))
            {
                return style;
            }
        }

        return null;
    }

    /// <summary>
    /// The edge <paramref name="value"/> docks to, or null when it does not dock: blank, <c>None</c>, or not a member
    /// (which the region writer refuses — this never guesses one).
    /// </summary>
    public static FormDockEdge? EdgeOf(string? value) =>
        Canonical(value) is { } style && style != "None" ? Enum.Parse<FormDockEdge>(style) : null;
}
