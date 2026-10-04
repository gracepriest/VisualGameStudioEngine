using System.Globalization;
using Avalonia.Media;
using BasicLang.Forms;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VisualGameStudio.Shell.ViewModels.Designer;

/// <summary>
/// The Font dialog (slice 4 D-2): family, size, Bold / Italic / Underline / Strikeout and a live preview; OK produces ONE
/// canonical Font value (<see cref="Result"/>), Cancel produces none. It writes nothing itself — the grid's row commits
/// the result through its own Commit (fan-in: one statement, Judge decides).
///
/// <para>⛔ The families are a SEAM (<c>Func&lt;IEnumerable&lt;string&gt;&gt;</c>, the installed fonts in the IDE), so a test
/// is machine-independent (pre-flight M6: the list differs per machine). They are filtered to the families
/// <see cref="FormFontValue.TryParse"/> accepts — letters, digits, spaces and hyphens — so the dialog never offers a family
/// the catalog would refuse. The current value's family and WinForms' default <c>Segoe UI</c> are always offered, even when
/// not installed: a Linux IDE must still show the form's own font.</para>
/// </summary>
public sealed partial class FormFontDialogViewModel : ObservableObject
{
    /// <summary>WinForms' default family, offered whether or not this machine has it.</summary>
    public const string DefaultFamily = "Segoe UI";

    /// <summary>VS's own size list; any other size (a decimal, up to two places) may be typed.</summary>
    public static IReadOnlyList<string> StandardSizes { get; } =
        new[] { "8", "9", "10", "11", "12", "14", "16", "18", "20", "22", "24", "26", "28", "36", "48", "72" };

    private readonly IReadOnlyList<string> _families;

    public FormFontDialogViewModel(IEnumerable<string> installedFamilies, FormFontValue start, FormTarget target)
    {
        _families = installedFamilies
            .Append(start.Family)
            .Append(DefaultFamily)
            .Select(f => f.Trim())
            .Where(IsOfferable)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _selectedFamily = _families.FirstOrDefault(f => string.Equals(f, start.Family, StringComparison.OrdinalIgnoreCase));
        _sizeText = start.Size.ToString("0.##", CultureInfo.InvariantCulture);
        _bold = start.Bold;
        _italic = start.Italic;
        _underline = start.Underline;
        _strikeout = start.Strikeout;
        IsWeb = target == FormTarget.Web;
    }

    /// <summary>A family the catalog accepts — the parser's own rule, asked through the parser.</summary>
    private static bool IsOfferable(string family) =>
        family.Length > 0 && FormFontValue.TryParse(family + ", 9pt", out var parsed) &&
        string.Equals(parsed.Family, family, StringComparison.Ordinal);

    /// <summary>Every family offered, before the filter box narrows it.</summary>
    public IReadOnlyList<string> AllFamilies => _families;

    /// <summary>The families matching <see cref="FilterText"/> (a case-insensitive substring), in order.</summary>
    public IReadOnlyList<string> Families =>
        string.IsNullOrWhiteSpace(FilterText)
            ? _families
            : _families.Where(f => f.Contains(FilterText.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Families))]
    private string _filterText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview), nameof(CanAccept), nameof(PreviewFamily))]
    private string? _selectedFamily;

    public IReadOnlyList<string> Sizes => StandardSizes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview), nameof(CanAccept), nameof(PreviewSize))]
    private string _sizeText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview), nameof(PreviewWeight))]
    private bool _bold;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview), nameof(PreviewStyle))]
    private bool _italic;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview), nameof(PreviewDecorations))]
    private bool _underline;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview), nameof(PreviewDecorations))]
    private bool _strikeout;

    /// <summary>The font the choices make, or null while they do not make one (no family, a size the parser refuses).</summary>
    public FormFontValue? Preview
    {
        get
        {
            if (string.IsNullOrEmpty(SelectedFamily))
            {
                return null;
            }

            var styles = new[] { (Bold, "Bold"), (Italic, "Italic"), (Underline, "Underline"), (Strikeout, "Strikeout") }
                .Where(s => s.Item1).Select(s => s.Item2).ToList();
            var text = $"{SelectedFamily}, {SizeText?.Trim()}pt" + (styles.Count > 0 ? ", style=" + string.Join(", ", styles) : "");
            return FormFontValue.TryParse(text, out var font) ? font : null;
        }
    }

    /// <summary>OK is offered only while the choices make a font.</summary>
    public bool CanAccept => Preview != null;

    /// <summary>The web hint (D-2): a page shows a font only if the viewer has it installed.</summary>
    public bool IsWeb { get; }

    public string WebHint => "A web page uses this font only if the viewer's machine has it installed.";

    // The live preview's bindings (points → Avalonia's device-independent pixels: 96 / 72).
    public FontFamily PreviewFamily => new(SelectedFamily ?? DefaultFamily);

    public double PreviewSize => Preview is { } f ? (double)f.Size * 96.0 / 72.0 : 12;

    public FontWeight PreviewWeight => Bold ? FontWeight.Bold : FontWeight.Normal;

    public FontStyle PreviewStyle => Italic ? FontStyle.Italic : FontStyle.Normal;

    public TextDecorationCollection? PreviewDecorations
    {
        get
        {
            var decorations = new TextDecorationCollection();
            if (Underline)
            {
                decorations.AddRange(TextDecorations.Underline);
            }

            if (Strikeout)
            {
                decorations.AddRange(TextDecorations.Strikethrough);
            }

            return decorations.Count == 0 ? null : decorations;
        }
    }

    /// <summary>What OK produced — the canonical Font text — or null (Cancel, or the dialog closed any other way).</summary>
    public string? Result { get; private set; }

    /// <summary>OK: the result is the canonical text of the choices, or null when they make no font.</summary>
    public string? Accept() => Result = Preview?.Canonical;

    /// <summary>Cancel: no result, so nothing is written.</summary>
    public void Cancel() => Result = null;
}
