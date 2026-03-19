using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace VisualGameStudio.Shell.Converters;

/// <summary>
/// Converts a diagnostic count to a color: highlighted when count > 0, dimmed when 0.
/// </summary>
public class DiagnosticCountColorConverter : IValueConverter
{
    /// <summary>
    /// Converter instance for error counts (red when active, gray when zero).
    /// </summary>
    public static readonly DiagnosticCountColorConverter ErrorInstance = new(
        new SolidColorBrush(Color.Parse("#E51400")),
        new SolidColorBrush(Color.Parse("#888888"))
    );

    /// <summary>
    /// Converter instance for warning counts (yellow when active, gray when zero).
    /// </summary>
    public static readonly DiagnosticCountColorConverter WarningInstance = new(
        new SolidColorBrush(Color.Parse("#F0AD4E")),
        new SolidColorBrush(Color.Parse("#888888"))
    );

    private readonly IBrush _activeBrush;
    private readonly IBrush _inactiveBrush;

    public DiagnosticCountColorConverter(IBrush activeBrush, IBrush inactiveBrush)
    {
        _activeBrush = activeBrush;
        _inactiveBrush = inactiveBrush;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int count && count > 0)
            return _activeBrush;
        return _inactiveBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Converts a positive integer to true (for IsVisible binding).
/// </summary>
public class PositiveIntToBoolConverter : IValueConverter
{
    public static readonly PositiveIntToBoolConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is int count && count > 0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Converters for the status bar indicators.
/// </summary>
public static class StatusBarConverters
{
    /// <summary>
    /// Converts a boolean (IsLspRunning) to a Color for the LSP status dot.
    /// Green when running, red when stopped.
    /// </summary>
    public static readonly IValueConverter BoolToLspColorConverter = new BoolToColorConverter(
        Color.Parse("#89D185"),  // green - running
        Color.Parse("#E51400")   // red - stopped
    );
}

/// <summary>
/// Converts a boolean to one of two Color values.
/// </summary>
public class BoolToColorConverter : IValueConverter
{
    private readonly Color _trueColor;
    private readonly Color _falseColor;

    public BoolToColorConverter(Color trueColor, Color falseColor)
    {
        _trueColor = trueColor;
        _falseColor = falseColor;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? _trueColor : _falseColor;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
