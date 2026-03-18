using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

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

    /// <summary>
    /// Raised when a breadcrumb item is clicked to navigate (scroll to symbol).
    /// </summary>
    public event EventHandler<BreadcrumbItem>? ItemClicked;

    /// <summary>
    /// Raised when a sibling is selected from a dropdown (open file or navigate to symbol).
    /// </summary>
    public event EventHandler<BreadcrumbSiblingSelectedEventArgs>? SiblingSelected;

    public BreadcrumbControl()
    {
        InitializeComponent();
        DataContext = this;
    }

    protected override void OnLoaded(RoutedEventArgs e)
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
                IconBackground = GetIconBackground(symbol.Kind),
                SegmentForeground = GetForeground(symbol.Kind),
                ShowSeparator = i < symbolList.Count - 1
            });
        }
    }

    /// <summary>
    /// Sets the full breadcrumb path including project, file, and symbol segments.
    /// </summary>
    public void SetFullPath(
        string? projectName,
        string? fileName,
        string? filePath,
        IEnumerable<(string Name, int Line, string Kind)> symbols,
        IReadOnlyList<BreadcrumbSibling>? siblingFiles = null,
        IReadOnlyList<BreadcrumbSibling>? siblingSymbols = null)
    {
        Items.Clear();

        // Project segment
        if (!string.IsNullOrEmpty(projectName))
        {
            Items.Add(new BreadcrumbItem
            {
                Name = projectName!,
                Line = 0,
                Kind = "Project",
                Icon = "P",
                IconBackground = new SolidColorBrush(Color.Parse("#6A9955")),
                SegmentForeground = new SolidColorBrush(Color.Parse("#CCCCCC")),
                ItemType = BreadcrumbItemType.Project,
                ShowSeparator = true
            });
        }

        // File segment
        if (!string.IsNullOrEmpty(fileName))
        {
            var fileItem = new BreadcrumbItem
            {
                Name = fileName!,
                FullPath = filePath,
                Line = 0,
                Kind = "File",
                Icon = GetFileIcon(fileName!),
                IconBackground = new SolidColorBrush(Color.Parse("#4FC1FF")),
                SegmentForeground = new SolidColorBrush(Color.Parse("#CCCCCC")),
                ItemType = BreadcrumbItemType.File,
                ShowSeparator = true
            };
            if (siblingFiles != null)
            {
                foreach (var s in siblingFiles) fileItem.Siblings.Add(s);
            }
            Items.Add(fileItem);
        }

        // Symbol segments
        var symbolList = symbols.ToList();
        for (int i = 0; i < symbolList.Count; i++)
        {
            var symbol = symbolList[i];
            var item = new BreadcrumbItem
            {
                Name = symbol.Name,
                Line = symbol.Line,
                Kind = symbol.Kind,
                Icon = GetIconForKind(symbol.Kind),
                IconBackground = GetIconBackground(symbol.Kind),
                SegmentForeground = GetForeground(symbol.Kind),
                ItemType = BreadcrumbItemType.Symbol,
                ShowSeparator = i < symbolList.Count - 1
            };

            // The last symbol segment gets sibling symbols for dropdown
            if (i == symbolList.Count - 1 && siblingSymbols != null)
            {
                foreach (var s in siblingSymbols) item.Siblings.Add(s);
            }
            // Container segments (Module, Class) get their child members
            else if (siblingSymbols != null && (symbol.Kind == "Module" || symbol.Kind == "Class" || symbol.Kind == "Interface"))
            {
                foreach (var s in siblingSymbols.Where(s =>
                    s.Kind == "Function" || s.Kind == "Sub" || s.Kind == "Property"))
                {
                    item.Siblings.Add(s);
                }
            }

            Items.Add(item);
        }

        UpdateSeparators();
    }

    private void OnSegmentClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not BreadcrumbItem item) return;

        if (item.HasSiblings)
        {
            ShowDropdown(btn, item);
        }
        else
        {
            // Direct navigation for items without siblings
            ItemClicked?.Invoke(this, item);
        }
    }

    private void ShowDropdown(Button anchor, BreadcrumbItem item)
    {
        var menu = new ContextMenu();
        menu.Background = new SolidColorBrush(Color.Parse("#2D2D30"));

        foreach (var sibling in item.Siblings)
        {
            var menuItem = new MenuItem
            {
                Header = FormatSiblingHeader(sibling),
                Tag = sibling,
                Foreground = new SolidColorBrush(Color.Parse("#CCCCCC")),
                FontSize = 12,
            };

            // Highlight the currently active item
            if (sibling.Name == item.Name ||
                (item.ItemType == BreadcrumbItemType.File && sibling.Name == item.Name))
            {
                menuItem.FontWeight = FontWeight.Bold;
            }

            menuItem.Click += (s, e) =>
            {
                if (s is MenuItem mi && mi.Tag is BreadcrumbSibling selected)
                {
                    SiblingSelected?.Invoke(this, new BreadcrumbSiblingSelectedEventArgs(selected, item.ItemType));
                }
            };
            menu.Items.Add(menuItem);
        }

        if (menu.Items.Count > 0)
        {
            menu.Open(anchor);
        }
    }

    private static string FormatSiblingHeader(BreadcrumbSibling sibling)
    {
        var icon = GetIconForKind(sibling.Kind);
        return $"{icon}  {sibling.Name}";
    }

    internal static string GetIconForKind(string kind)
    {
        return kind.ToLowerInvariant() switch
        {
            "project" => "P",
            "file" => "F",
            "module" => "M",
            "class" => "C",
            "interface" => "I",
            "function" => "F",
            "sub" => "S",
            "property" => "P",
            "enum" => "E",
            "field" => "f",
            "struct" => "S",
            _ => "?"
        };
    }

    private static string GetFileIcon(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".bas" => "B",
            ".bl" => "B",
            ".blproj" => "P",
            ".cs" => "C#",
            ".vb" => "VB",
            ".cpp" or ".h" => "C++",
            ".json" => "J",
            ".xml" => "X",
            _ => "F"
        };
    }

    private static SolidColorBrush GetIconBackground(string kind)
    {
        return kind.ToLowerInvariant() switch
        {
            "project" => new SolidColorBrush(Color.Parse("#6A9955")),
            "file" => new SolidColorBrush(Color.Parse("#4FC1FF")),
            "module" => new SolidColorBrush(Color.Parse("#C586C0")),
            "class" => new SolidColorBrush(Color.Parse("#DCDCAA")),
            "interface" => new SolidColorBrush(Color.Parse("#4EC9B0")),
            "function" or "sub" => new SolidColorBrush(Color.Parse("#569CD6")),
            "property" => new SolidColorBrush(Color.Parse("#9CDCFE")),
            "enum" => new SolidColorBrush(Color.Parse("#B5CEA8")),
            _ => new SolidColorBrush(Color.Parse("#808080"))
        };
    }

    private static SolidColorBrush GetForeground(string kind)
    {
        return kind.ToLowerInvariant() switch
        {
            "module" => new SolidColorBrush(Color.Parse("#C586C0")),
            "class" => new SolidColorBrush(Color.Parse("#DCDCAA")),
            "interface" => new SolidColorBrush(Color.Parse("#4EC9B0")),
            "function" or "sub" => new SolidColorBrush(Color.Parse("#DCDCAA")),
            "property" => new SolidColorBrush(Color.Parse("#9CDCFE")),
            "enum" => new SolidColorBrush(Color.Parse("#B5CEA8")),
            _ => new SolidColorBrush(Color.Parse("#CCCCCC"))
        };
    }
}

/// <summary>
/// Represents a single breadcrumb segment in the navigation bar.
/// </summary>
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

    [ObservableProperty]
    private IBrush _iconBackground = new SolidColorBrush(Color.Parse("#808080"));

    [ObservableProperty]
    private IBrush _segmentForeground = new SolidColorBrush(Color.Parse("#CCCCCC"));

    [ObservableProperty]
    private BreadcrumbItemType _itemType = BreadcrumbItemType.Symbol;

    [ObservableProperty]
    private string? _fullPath;

    /// <summary>
    /// Sibling items shown in the dropdown when clicking this segment.
    /// </summary>
    public ObservableCollection<BreadcrumbSibling> Siblings { get; } = new();

    /// <summary>
    /// True when there are siblings available for a dropdown.
    /// </summary>
    public bool HasSiblings => Siblings.Count > 0;

    /// <summary>
    /// True when an icon should be displayed.
    /// </summary>
    public bool HasIcon => !string.IsNullOrEmpty(Icon);
}

/// <summary>
/// Represents a sibling item in a breadcrumb dropdown menu.
/// </summary>
public class BreadcrumbSibling
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public int Line { get; set; }
    public string? FullPath { get; set; }
}

/// <summary>
/// The type of breadcrumb segment.
/// </summary>
public enum BreadcrumbItemType
{
    Project,
    File,
    Symbol
}

/// <summary>
/// Event args when a sibling is selected from a breadcrumb dropdown.
/// </summary>
public class BreadcrumbSiblingSelectedEventArgs : EventArgs
{
    public BreadcrumbSibling Sibling { get; }
    public BreadcrumbItemType SourceType { get; }

    public BreadcrumbSiblingSelectedEventArgs(BreadcrumbSibling sibling, BreadcrumbItemType sourceType)
    {
        Sibling = sibling;
        SourceType = sourceType;
    }
}
