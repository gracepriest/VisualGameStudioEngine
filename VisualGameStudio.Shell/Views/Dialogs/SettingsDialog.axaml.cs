using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Shell.Views.Dialogs;

public partial class SettingsDialog : AccessibleDialog
{
    // Static converters referenced from AXAML via x:Static
    public static readonly IValueConverter BoldIfTrue = new BoolToFontWeightConverter();
    public static readonly IValueConverter ScopeTabBg = new ScopeTabBackgroundConverter();
    public static readonly IValueConverter ScopeTabFg = new ScopeTabForegroundConverter();
    public static readonly IValueConverter CategoryBg = new CategoryBackgroundConverter();
    public static readonly IValueConverter IsKeyboardCategory = new IsKeyboardCategoryConverter();

    public SettingsDialog()
    {
        InitializeComponent();
        EnterActivatesDefaultButton = false; // Settings has many interactive controls
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is SettingsViewModel vm)
        {
            vm.CloseDialog = () => Close(vm.DialogResult);
        }
    }
}

/// <summary>
/// Converts bool to FontWeight: true = Bold, false = Normal.
/// </summary>
internal class BoolToFontWeightConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? FontWeight.Bold : FontWeight.Normal;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Converts bool (isActive) to scope tab background color.
/// </summary>
internal class ScopeTabBackgroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true)
            return new SolidColorBrush(Color.Parse("#3794FF"));
        return new SolidColorBrush(Colors.Transparent);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Converts bool (isActive) to scope tab foreground color.
/// </summary>
internal class ScopeTabForegroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true)
            return new SolidColorBrush(Colors.White);
        return new SolidColorBrush(Color.Parse("#CCCCCC"));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Converts bool (isSelected) to category sidebar item background.
/// </summary>
internal class CategoryBackgroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true)
            return new SolidColorBrush(Color.Parse("#37373D"));
        return new SolidColorBrush(Colors.Transparent);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Returns true when the category ID is "keyboard" for showing the keyboard shortcuts grid.
/// </summary>
internal class IsKeyboardCategoryConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is string s && s.Equals("keyboard", StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
