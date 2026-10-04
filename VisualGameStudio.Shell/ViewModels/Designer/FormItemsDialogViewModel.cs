using System.Text.RegularExpressions;
using BasicLang.Forms;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VisualGameStudio.Shell.ViewModels.Designer;

/// <summary>
/// VS's String Collection Editor (slice 4 Task 7): one item per line in a multi-line box. OK produces the model's ONE
/// encoding (ADR 0020, <see cref="FormItems.Join"/>), "" for no items — the row turns "" into Reset. Cancel produces none.
///
/// <para>⛔ Lines split on CRLF, LF and lone CR alike (pre-flight M4: a multi-line TextBox gets <c>\r\n</c> for Enter on
/// Windows) — a split on LF alone would leave a CR on every item, and every item would be emitted with it.</para>
/// </summary>
public sealed partial class FormItemsDialogViewModel : ObservableObject
{
    private static readonly Regex LineBreak = new("\r\n|\n|\r", RegexOptions.Compiled);

    public FormItemsDialogViewModel(string? modelValue)
    {
        _text = string.Join(Environment.NewLine, FormItems.Split(modelValue ?? ""));
    }

    /// <summary>The box's text: one item per line.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Items))]
    private string _text;

    /// <summary>The items the box holds — blank lines dropped, spaces around an item kept (<see cref="FormItems.Split"/>).</summary>
    public IReadOnlyList<string> Items => FormItems.Split(LineBreak.Replace(Text ?? "", "\n"));

    /// <summary>What OK produced — the model value ("" for no items) — or null (Cancel, or any other close).</summary>
    public string? Result { get; private set; }

    public string? Accept() => Result = FormItems.Join(Items);

    public void Cancel() => Result = null;
}
