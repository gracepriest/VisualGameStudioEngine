using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using VisualGameStudio.Core.Abstractions.Services;
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
        Closing += OnDialogClosing;
    }

    /// <summary>
    /// Closing the window via the X button or Escape must behave like Cancel: undo every
    /// live-applied change (theme included). Save() sets DialogResult=true first, so an
    /// OK-initiated close is skipped. RevertToSnapshot is guarded, so when Cancel already
    /// reverted before closing this is a harmless no-op.
    /// </summary>
    private void OnDialogClosing(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is SettingsViewModel vm && vm.DialogResult != true)
        {
            vm.RevertToSnapshot();
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is SettingsViewModel vm)
        {
            vm.CloseDialog = () => Close(vm.DialogResult);
            // Reset All must confirm before wiping the active scope (Task 0.5). Wire the VM's
            // confirmation gate to the app's shared Yes/No dialog convention (IDialogService).
            vm.ConfirmResetInteraction = ConfirmResetAsync;
        }
    }

    /// <summary>
    /// Shows the app's standard Yes/No confirmation for Reset All. Returns false (do not reset)
    /// when no dialog service is available, matching the ViewModel's fail-safe contract.
    /// </summary>
    private static Task<bool> ConfirmResetAsync(string title, string message)
    {
        if (App.Services?.GetService(typeof(IDialogService)) is IDialogService dialogService)
        {
            return dialogService.ConfirmAsync(title, message);
        }
        return Task.FromResult(false);
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
