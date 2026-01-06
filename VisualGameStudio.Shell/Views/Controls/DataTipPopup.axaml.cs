using Avalonia.Controls;
using Avalonia.Interactivity;

namespace VisualGameStudio.Shell.Views.Controls;

public partial class DataTipPopup : UserControl
{
    public string Expression { get; private set; } = "";
    public string Value { get; private set; } = "";
    public string Type { get; private set; } = "";

    public event EventHandler<string>? AddToWatchClicked;

    public DataTipPopup()
    {
        InitializeComponent();
    }

    public void SetContent(string expression, string value, string? type)
    {
        Expression = expression;
        Value = value;
        Type = type ?? "";

        ExpressionText.Text = expression;
        ValueText.Text = value;
        TypeText.Text = type ?? "";
        TypeText.IsVisible = !string.IsNullOrEmpty(type);
    }

    public void SetError(string expression, string errorMessage)
    {
        Expression = expression;
        Value = errorMessage;
        Type = "";

        ExpressionText.Text = expression;
        ValueText.Text = errorMessage;
        ValueText.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#F48771"));
        TypeText.IsVisible = false;

        // Hide add to watch button for errors
        AddToWatchButton.IsVisible = false;
    }

    private void OnAddToWatchClick(object? sender, RoutedEventArgs e)
    {
        AddToWatchClicked?.Invoke(this, Expression);
    }

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            var clipboard = topLevel?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(Value);
            }
        }
        catch { }
    }
}
