using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using AvaloniaEdit;

namespace VisualGameStudio.Editor.Controls;

public partial class MinimapControl : UserControl
{
    private TextEditor? _editor;
    private Canvas? _codeCanvas;
    private Border? _viewportIndicator;
    private double _scale = 0.15;
    private bool _isDragging;

    public MinimapControl()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _codeCanvas = this.FindControl<Canvas>("CodeCanvas");
        _viewportIndicator = this.FindControl<Border>("ViewportIndicator");
    }

    public void AttachEditor(TextEditor editor)
    {
        if (_editor != null)
        {
            _editor.TextChanged -= OnTextChanged;
            _editor.TextArea.TextView.ScrollOffsetChanged -= OnScrollChanged;
        }

        _editor = editor;

        if (_editor != null)
        {
            _editor.TextChanged += OnTextChanged;
            _editor.TextArea.TextView.ScrollOffsetChanged += OnScrollChanged;
            UpdateMinimap();
        }
    }

    private void OnTextChanged(object? sender, EventArgs e)
    {
        UpdateMinimap();
    }

    private void OnScrollChanged(object? sender, EventArgs e)
    {
        UpdateViewportIndicator();
    }

    private void UpdateMinimap()
    {
        if (_codeCanvas == null || _editor == null) return;

        _codeCanvas.Children.Clear();

        var document = _editor.Document;
        if (document == null) return;

        var lineHeight = 2.0;
        var y = 0.0;

        for (var i = 1; i <= document.LineCount && y < Bounds.Height; i++)
        {
            var line = document.GetLineByNumber(i);
            var text = document.GetText(line.Offset, line.Length);

            if (!string.IsNullOrWhiteSpace(text))
            {
                var indent = text.TakeWhile(char.IsWhiteSpace).Count();
                var contentLength = text.TrimEnd().Length - indent;

                if (contentLength > 0)
                {
                    var rect = new Rectangle
                    {
                        Width = Math.Min(contentLength * 0.8, Width - 10),
                        Height = lineHeight,
                        Fill = GetLineColor(text)
                    };

                    Canvas.SetLeft(rect, indent * 0.5 + 2);
                    Canvas.SetTop(rect, y);
                    _codeCanvas.Children.Add(rect);
                }
            }

            y += lineHeight + 0.5;
        }

        UpdateViewportIndicator();
    }

    private IBrush GetLineColor(string text)
    {
        var trimmed = text.TrimStart();

        // Keywords and control flow
        if (trimmed.StartsWith("Sub ", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("Function ", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("Class ", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("Module ", StringComparison.OrdinalIgnoreCase))
        {
            return new SolidColorBrush(Color.Parse("#569CD6"));
        }

        // Comments
        if (trimmed.StartsWith("'") || trimmed.StartsWith("REM ", StringComparison.OrdinalIgnoreCase))
        {
            return new SolidColorBrush(Color.Parse("#608B4E"));
        }

        // Strings
        if (trimmed.Contains("\""))
        {
            return new SolidColorBrush(Color.Parse("#CE9178"));
        }

        // Default code
        return new SolidColorBrush(Color.Parse("#808080"));
    }

    private void UpdateViewportIndicator()
    {
        if (_viewportIndicator == null || _editor == null) return;

        var textView = _editor.TextArea.TextView;
        var document = _editor.Document;

        if (document == null || document.LineCount == 0) return;

        var totalLines = document.LineCount;
        var lineHeight = 2.5;
        var totalHeight = totalLines * lineHeight;

        // Calculate visible area
        var firstVisibleLine = Math.Max(1, (int)(textView.ScrollOffset.Y / _editor.TextArea.TextView.DefaultLineHeight) + 1);
        var visibleLines = (int)(textView.Bounds.Height / _editor.TextArea.TextView.DefaultLineHeight);
        var lastVisibleLine = Math.Min(totalLines, firstVisibleLine + visibleLines);

        // Position viewport indicator
        var top = (firstVisibleLine - 1) * lineHeight;
        var height = Math.Max(10, (lastVisibleLine - firstVisibleLine + 1) * lineHeight);

        _viewportIndicator.Margin = new Thickness(0, top, 0, 0);
        _viewportIndicator.Height = height;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _isDragging = true;
        ScrollToPosition(e.GetPosition(this).Y);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_isDragging)
        {
            ScrollToPosition(e.GetPosition(this).Y);
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _isDragging = false;
    }

    private void ScrollToPosition(double y)
    {
        if (_editor == null) return;

        var document = _editor.Document;
        if (document == null || document.LineCount == 0) return;

        var lineHeight = 2.5;
        var clickedLine = (int)(y / lineHeight) + 1;
        clickedLine = Math.Clamp(clickedLine, 1, document.LineCount);

        _editor.ScrollToLine(clickedLine);
    }
}
