using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace VisualGameStudio.Shell.Views.Panels;

public partial class VariablesView : UserControl
{
    public static readonly IValueConverter ScopeColorConverter = new ScopeToBrushConverter();

    public VariablesView()
    {
        InitializeComponent();
    }
}

public class ScopeToBrushConverter : IValueConverter
{
    private static readonly IBrush ScopeBrush = new SolidColorBrush(Color.Parse("#569CD6"));
    private static readonly IBrush VariableBrush = new SolidColorBrush(Color.Parse("#9CDCFE"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isScope)
        {
            return isScope ? ScopeBrush : VariableBrush;
        }
        return VariableBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
