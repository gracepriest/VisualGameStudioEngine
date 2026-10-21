namespace BasicLang.Forms;

/// <summary>Client-origin borders and docking padding are DIFFERENT coordinate rules.</summary>
public readonly record struct FormInsets(int Left, int Top, int Right, int Bottom);

/// <summary>The catalog's client-area rule; no consumer switches on a control kind.</summary>
public sealed record FormClientRule(string? BorderProperty = null, FormInsets DockPadding = default);

/// <summary>
/// The one client-area answer (§7.5). Positioned children use OriginInset only; docked
/// children additionally use DockPadding. GroupBox's 19px is the measured default-font
/// WinForms display rectangle, not a font/DPI-independent claim.
/// </summary>
public readonly record struct FormClientArea(int OriginInset, FormInsets DockPadding)
{
    public static FormClientArea Of(FormControl control)
    {
        var rule = control.Definition?.ClientArea;
        var inset = 0;
        if (rule?.BorderProperty is { } name && control.Definition?.Property(name) is { } row &&
            control.Properties.TryGetValue(name, out var value) && row.Accepts(value, FormTarget.WinForms))
        {
            inset = row.Canonical(value) switch { "FixedSingle" => 1, "Fixed3D" => 2, _ => 0 };
        }
        return new FormClientArea(inset, rule?.DockPadding ?? default);
    }

    public (int Width, int Height) ClientSize(int outerWidth, int outerHeight) =>
        (Math.Max(0, outerWidth - 2 * OriginInset), Math.Max(0, outerHeight - 2 * OriginInset));
}

public enum FormEnableTarget { Native, InnerInput, Aria }
