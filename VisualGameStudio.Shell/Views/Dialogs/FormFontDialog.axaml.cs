using Avalonia.Controls;
using Avalonia.Interactivity;
using VisualGameStudio.Shell.ViewModels.Designer;

namespace VisualGameStudio.Shell.Views.Dialogs;

/// <summary>
/// The designer's Font dialog (slice 4 D-2). Closes with <see cref="FormFontDialogViewModel.Result"/> — the canonical Font
/// text on OK, null on Cancel or any other close — for <c>ShowDialog&lt;string?&gt;</c>.
/// </summary>
public partial class FormFontDialog : Window
{
    public FormFontDialog()
    {
        InitializeComponent();
    }

    private FormFontDialogViewModel? Vm => DataContext as FormFontDialogViewModel;

    private void OnOk(object? sender, RoutedEventArgs e) => Close(Vm?.Accept());

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Vm?.Cancel();
        Close(null);
    }

    /// <summary>A real pick in the family list — never the null a filter's narrowing pushes (see the AXAML).</summary>
    private void OnFamilyPicked(object? sender, SelectionChangedEventArgs e)
    {
        if (Vm != null && e.AddedItems.Count == 1 && e.AddedItems[0] is string family)
        {
            Vm.SelectedFamily = family;
        }
    }

    /// <summary>A real pick in the size list; a typed size lives in the box and is never overwritten by a null.</summary>
    private void OnSizePicked(object? sender, SelectionChangedEventArgs e)
    {
        if (Vm != null && e.AddedItems.Count == 1 && e.AddedItems[0] is string size)
        {
            Vm.SizeText = size;
        }
    }
}
