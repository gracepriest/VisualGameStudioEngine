using Avalonia.Controls;
using Avalonia.Interactivity;
using VisualGameStudio.Shell.ViewModels.Designer;

namespace VisualGameStudio.Shell.Views.Dialogs;

/// <summary>
/// The designer's String Collection Editor (slice 4 Task 7). Closes with <see cref="FormItemsDialogViewModel.Result"/> —
/// the model value on OK, null on Cancel or any other close — for <c>ShowDialog&lt;string?&gt;</c>.
/// </summary>
public partial class FormItemsDialog : Window
{
    public FormItemsDialog()
    {
        InitializeComponent();
    }

    private FormItemsDialogViewModel? Vm => DataContext as FormItemsDialogViewModel;

    private void OnOk(object? sender, RoutedEventArgs e) => Close(Vm?.Accept());

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Vm?.Cancel();
        Close(null);
    }
}
