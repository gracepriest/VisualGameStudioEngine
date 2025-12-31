using Avalonia.Controls;
using Avalonia.Input;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Shell.Views.Controls;

public partial class PeekDefinitionControl : UserControl
{
    public PeekDefinitionControl()
    {
        InitializeComponent();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (DataContext is PeekDefinitionViewModel vm)
        {
            if (e.Key == Key.Escape)
            {
                vm.CloseCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.F12)
            {
                vm.GoToDefinitionCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.Down && e.KeyModifiers == KeyModifiers.Alt)
            {
                vm.NextDefinitionCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.Up && e.KeyModifiers == KeyModifiers.Alt)
            {
                vm.PreviousDefinitionCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}
