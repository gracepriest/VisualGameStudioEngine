using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

public partial class ListSelectionDialogViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private string _prompt;

    [ObservableProperty]
    private int _selectedIndex = -1;

    [ObservableProperty]
    private string? _selectedItem;

    public ObservableCollection<string> Items { get; }

    public ListSelectionDialogViewModel(string title, string prompt, IEnumerable<string> items)
    {
        _title = title;
        _prompt = prompt;
        Items = new ObservableCollection<string>(items);

        if (Items.Count > 0)
        {
            SelectedIndex = 0;
            SelectedItem = Items[0];
        }
    }

    partial void OnSelectedIndexChanged(int value)
    {
        if (value >= 0 && value < Items.Count)
        {
            SelectedItem = Items[value];
        }
    }
}
