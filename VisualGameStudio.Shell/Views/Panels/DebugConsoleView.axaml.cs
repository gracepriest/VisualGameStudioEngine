using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Media;
using VisualGameStudio.Shell.ViewModels.Panels;

namespace VisualGameStudio.Shell.Views.Panels;

public partial class DebugConsoleView : UserControl
{
    public DebugConsoleView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is DebugConsoleViewModel vm)
        {
            // Auto-scroll to bottom when entries change
            vm.Entries.CollectionChanged += (s, args) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    OutputScroller?.ScrollToEnd();
                });
            };

            // Focus input when requested (e.g., on breakpoint hit)
            vm.FocusInputRequested += () =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    InputTextBox?.Focus();
                });
            };
        }
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not DebugConsoleViewModel vm)
            return;

        switch (e.Key)
        {
            case Key.Enter:
                if (vm.IsSuggestionsVisible)
                {
                    vm.AcceptSuggestion();
                }
                else
                {
                    vm.EvaluateCommand.Execute(null);
                }
                e.Handled = true;
                break;

            case Key.Up:
                if (vm.IsSuggestionsVisible)
                {
                    if (vm.SelectedSuggestionIndex > 0)
                        vm.SelectedSuggestionIndex--;
                }
                else
                {
                    vm.HistoryUpCommand.Execute(null);
                }
                e.Handled = true;
                break;

            case Key.Down:
                if (vm.IsSuggestionsVisible)
                {
                    if (vm.SelectedSuggestionIndex < vm.Suggestions.Count - 1)
                        vm.SelectedSuggestionIndex++;
                }
                else
                {
                    vm.HistoryDownCommand.Execute(null);
                }
                e.Handled = true;
                break;

            case Key.Tab:
                if (vm.IsSuggestionsVisible)
                {
                    vm.AcceptSuggestion();
                    e.Handled = true;
                }
                break;

            case Key.Escape:
                if (vm.IsSuggestionsVisible)
                {
                    vm.IsSuggestionsVisible = false;
                }
                else
                {
                    vm.InputText = "";
                }
                e.Handled = true;
                break;

            // Ctrl+L to clear
            case Key.L when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                vm.ClearCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void OnInputTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (DataContext is DebugConsoleViewModel vm && vm.IsPaused)
        {
            // Debounce: only fetch suggestions after a short pause
            _ = vm.UpdateSuggestionsAsync();
        }
    }

    private void OnSuggestionDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is DebugConsoleViewModel vm)
        {
            vm.AcceptSuggestion();
            InputTextBox?.Focus();
        }
    }
}

/// <summary>
/// Converters used by the DebugConsoleView AXAML.
/// </summary>
public static class DebugConsoleConverters
{
    /// <summary>
    /// Converts a boolean IsExpanded to an expand/collapse glyph.
    /// </summary>
    public static readonly IValueConverter ExpanderGlyph = new ExpanderGlyphConverter();

    /// <summary>
    /// Converts DebugConsoleEntryType to a foreground color brush.
    /// </summary>
    public static readonly IValueConverter EntryTypeForeground = new EntryTypeForegroundConverter();

    /// <summary>
    /// Converts DebugConsoleEntryType to a prefix string (e.g., "> " for input).
    /// </summary>
    public static readonly IValueConverter EntryTypePrefix = new EntryTypePrefixConverter();
}

public class ExpanderGlyphConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? "\u25BC" : "\u25B6"; // Down triangle / Right triangle
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class EntryTypeForegroundConverter : IValueConverter
{
    private static readonly IBrush InputBrush = new SolidColorBrush(Color.Parse("#569CD6"));      // Blue
    private static readonly IBrush OutputBrush = new SolidColorBrush(Color.Parse("#DCDCAA"));     // Light yellow
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#F44747"));      // Red
    private static readonly IBrush InfoBrush = new SolidColorBrush(Color.Parse("#808080"));       // Gray
    private static readonly IBrush WarningBrush = new SolidColorBrush(Color.Parse("#CCA700"));    // Yellow/amber

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            DebugConsoleEntryType.Input => InputBrush,
            DebugConsoleEntryType.Output => OutputBrush,
            DebugConsoleEntryType.Error => ErrorBrush,
            DebugConsoleEntryType.Info => InfoBrush,
            DebugConsoleEntryType.Warning => WarningBrush,
            _ => OutputBrush
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class EntryTypePrefixConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            DebugConsoleEntryType.Input => "> ",
            DebugConsoleEntryType.Error => "E ",
            DebugConsoleEntryType.Warning => "W ",
            DebugConsoleEntryType.Info => "i ",
            _ => "  "
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
