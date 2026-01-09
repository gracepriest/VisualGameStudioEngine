using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VisualGameStudio.Editor.Controls;

public partial class BreadcrumbControl : UserControl
{
    private ItemsControl? _itemsControl;

    public static readonly StyledProperty<ObservableCollection<BreadcrumbItem>> ItemsProperty =
        AvaloniaProperty.Register<BreadcrumbControl, ObservableCollection<BreadcrumbItem>>(
            nameof(Items), defaultValue: new ObservableCollection<BreadcrumbItem>());

    public ObservableCollection<BreadcrumbItem> Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public event EventHandler<BreadcrumbItem>? ItemClicked;

    public BreadcrumbControl()
    {
        InitializeComponent();
        DataContext = this;
    }

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _itemsControl = this.FindControl<ItemsControl>("BreadcrumbItems");
        if (_itemsControl != null)
        {
            _itemsControl.ItemsSource = Items;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemsProperty && _itemsControl != null)
        {
            _itemsControl.ItemsSource = Items;
            UpdateSeparators();
        }
    }

    private void UpdateSeparators()
    {
        for (int i = 0; i < Items.Count; i++)
        {
            Items[i].ShowSeparator = i < Items.Count - 1;
        }
    }

    public void UpdateBreadcrumb(IEnumerable<(string Name, int Line, string Kind)> symbols)
    {
        Items.Clear();
        var symbolList = symbols.ToList();
        for (int i = 0; i < symbolList.Count; i++)
        {
            var symbol = symbolList[i];
            Items.Add(new BreadcrumbItem
            {
                Name = symbol.Name,
                Line = symbol.Line,
                Kind = symbol.Kind,
                Icon = GetIconForKind(symbol.Kind),
                ShowSeparator = i < symbolList.Count - 1
            });
        }
    }

    private static string GetIconForKind(string kind)
    {
        return kind.ToLowerInvariant() switch
        {
            "module" => "M",
            "class" => "C",
            "interface" => "I",
            "function" => "F",
            "sub" => "S",
            "property" => "P",
            "enum" => "E",
            "field" => "f",
            _ => "?"
        };
    }

    [RelayCommand]
    private void Navigate(BreadcrumbItem? item)
    {
        if (item != null)
        {
            ItemClicked?.Invoke(this, item);
        }
    }
}

public partial class BreadcrumbItem : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private int _line;

    [ObservableProperty]
    private string _kind = "";

    [ObservableProperty]
    private string _icon = "";

    [ObservableProperty]
    private bool _showSeparator;
}
