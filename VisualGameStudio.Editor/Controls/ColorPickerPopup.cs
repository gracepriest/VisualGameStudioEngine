using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace VisualGameStudio.Editor.Controls;

/// <summary>
/// A simple inline color picker popup with RGB(A) sliders and a color preview.
/// Used by the code editor to edit color values detected in BasicLang source code.
/// </summary>
public class ColorPickerPopup : UserControl
{
    private Slider _redSlider = null!;
    private Slider _greenSlider = null!;
    private Slider _blueSlider = null!;
    private Slider _alphaSlider = null!;
    private Border _previewBorder = null!;
    private TextBlock _hexLabel = null!;
    private TextBox _redBox = null!;
    private TextBox _greenBox = null!;
    private TextBox _blueBox = null!;
    private TextBox _alphaBox = null!;
    private bool _suppressUpdate;

    /// <summary>
    /// Raised when the user confirms the color selection.
    /// </summary>
    public event EventHandler<ColorPickedEventArgs>? ColorPicked;

    /// <summary>
    /// Raised when the user cancels the color picker.
    /// </summary>
    public event EventHandler? Cancelled;

    /// <summary>
    /// The document offset range for the color text to replace.
    /// </summary>
    public int ColorTextStartOffset { get; set; }
    public int ColorTextEndOffset { get; set; }
    public int Line { get; set; }

    public int Red => (int)_redSlider.Value;
    public int Green => (int)_greenSlider.Value;
    public int Blue => (int)_blueSlider.Value;
    public int Alpha => (int)_alphaSlider.Value;

    public ColorPickerPopup()
    {
        BuildUI();
    }

    public ColorPickerPopup(int r, int g, int b, int a = 255) : this()
    {
        SetColor(r, g, b, a);
    }

    /// <summary>
    /// Sets the current color values in the picker.
    /// </summary>
    public void SetColor(int r, int g, int b, int a = 255)
    {
        _suppressUpdate = true;
        _redSlider.Value = Clamp(r);
        _greenSlider.Value = Clamp(g);
        _blueSlider.Value = Clamp(b);
        _alphaSlider.Value = Clamp(a);
        _redBox.Text = r.ToString();
        _greenBox.Text = g.ToString();
        _blueBox.Text = b.ToString();
        _alphaBox.Text = a.ToString();
        _suppressUpdate = false;
        UpdatePreview();
    }

    private static int Clamp(int value) => Math.Max(0, Math.Min(255, value));

    private void BuildUI()
    {
        var bgColor = Color.FromRgb(30, 30, 30);
        var fgBrush = new SolidColorBrush(Color.FromRgb(204, 204, 204));
        var borderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60));

        var mainPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 6,
            Margin = new Thickness(10)
        };

        // Title
        mainPanel.Children.Add(new TextBlock
        {
            Text = "Color Picker",
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Foreground = fgBrush
        });

        // Preview
        var previewPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

        // Checkerboard background for transparency
        var checkerBorder = new Border
        {
            Width = 48,
            Height = 48,
            CornerRadius = new CornerRadius(4),
            ClipToBounds = true,
            Background = CreateCheckerboardBrush()
        };

        _previewBorder = new Border
        {
            Width = 48,
            Height = 48,
            CornerRadius = new CornerRadius(4),
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            Background = Brushes.Black
        };

        // Stack preview on top of checkerboard
        var previewGrid = new Grid { Width = 48, Height = 48 };
        previewGrid.Children.Add(checkerBorder);
        previewGrid.Children.Add(_previewBorder);

        _hexLabel = new TextBlock
        {
            Text = "#000000",
            FontSize = 12,
            FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
            Foreground = fgBrush,
            VerticalAlignment = VerticalAlignment.Center
        };

        previewPanel.Children.Add(previewGrid);
        previewPanel.Children.Add(_hexLabel);
        mainPanel.Children.Add(previewPanel);

        // Sliders
        _redSlider = CreateSlider();
        _greenSlider = CreateSlider();
        _blueSlider = CreateSlider();
        _alphaSlider = CreateSlider();

        _redBox = CreateValueBox();
        _greenBox = CreateValueBox();
        _blueBox = CreateValueBox();
        _alphaBox = CreateValueBox();

        mainPanel.Children.Add(CreateSliderRow("R", _redSlider, _redBox,
            new SolidColorBrush(Color.FromRgb(220, 80, 80))));
        mainPanel.Children.Add(CreateSliderRow("G", _greenSlider, _greenBox,
            new SolidColorBrush(Color.FromRgb(80, 200, 80))));
        mainPanel.Children.Add(CreateSliderRow("B", _blueSlider, _blueBox,
            new SolidColorBrush(Color.FromRgb(80, 130, 220))));
        mainPanel.Children.Add(CreateSliderRow("A", _alphaSlider, _alphaBox,
            new SolidColorBrush(Color.FromRgb(180, 180, 180))));

        // Wire slider change events
        _redSlider.PropertyChanged += OnSliderChanged;
        _greenSlider.PropertyChanged += OnSliderChanged;
        _blueSlider.PropertyChanged += OnSliderChanged;
        _alphaSlider.PropertyChanged += OnSliderChanged;

        // Wire text box changes
        _redBox.LostFocus += (_, _) => OnTextBoxValueChanged(_redBox, _redSlider);
        _greenBox.LostFocus += (_, _) => OnTextBoxValueChanged(_greenBox, _greenSlider);
        _blueBox.LostFocus += (_, _) => OnTextBoxValueChanged(_blueBox, _blueSlider);
        _alphaBox.LostFocus += (_, _) => OnTextBoxValueChanged(_alphaBox, _alphaSlider);

        // Buttons
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 4, 0, 0)
        };

        var okButton = new Button
        {
            Content = "Apply",
            MinWidth = 60,
            FontSize = 11,
            Padding = new Thickness(8, 4),
            Background = new SolidColorBrush(Color.FromRgb(0, 122, 204)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(3)
        };
        okButton.Click += (_, _) =>
        {
            ColorPicked?.Invoke(this, new ColorPickedEventArgs
            {
                R = Red, G = Green, B = Blue, A = Alpha,
                Line = Line,
                ColorTextStartOffset = ColorTextStartOffset,
                ColorTextEndOffset = ColorTextEndOffset
            });
        };

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 60,
            FontSize = 11,
            Padding = new Thickness(8, 4),
            Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
            Foreground = fgBrush,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(3)
        };
        cancelButton.Click += (_, _) =>
        {
            Cancelled?.Invoke(this, EventArgs.Empty);
        };

        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);
        mainPanel.Children.Add(buttonPanel);

        // Wrap in a styled border
        Content = new Border
        {
            Background = new SolidColorBrush(bgColor),
            BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            BoxShadow = new BoxShadows(new BoxShadow
            {
                OffsetX = 0,
                OffsetY = 4,
                Blur = 12,
                Color = Color.FromArgb(80, 0, 0, 0)
            }),
            Child = mainPanel,
            MinWidth = 240
        };
    }

    private Slider CreateSlider()
    {
        return new Slider
        {
            Minimum = 0,
            Maximum = 255,
            Value = 0,
            Width = 130,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private TextBox CreateValueBox()
    {
        return new TextBox
        {
            Width = 40,
            FontSize = 11,
            FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
            Text = "0",
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(2, 1),
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
            Foreground = new SolidColorBrush(Color.FromRgb(204, 204, 204)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(70, 70, 70)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2)
        };
    }

    private Panel CreateSliderRow(string label, Slider slider, TextBox valueBox, IBrush labelBrush)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6
        };

        row.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Foreground = labelBrush,
            Width = 14,
            VerticalAlignment = VerticalAlignment.Center
        });

        row.Children.Add(slider);
        row.Children.Add(valueBox);

        return row;
    }

    private void OnSliderChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Slider.ValueProperty || _suppressUpdate) return;

        _suppressUpdate = true;
        _redBox.Text = ((int)_redSlider.Value).ToString();
        _greenBox.Text = ((int)_greenSlider.Value).ToString();
        _blueBox.Text = ((int)_blueSlider.Value).ToString();
        _alphaBox.Text = ((int)_alphaSlider.Value).ToString();
        _suppressUpdate = false;

        UpdatePreview();
    }

    private void OnTextBoxValueChanged(TextBox box, Slider slider)
    {
        if (_suppressUpdate) return;

        if (int.TryParse(box.Text, out int value))
        {
            value = Clamp(value);
            box.Text = value.ToString();
            _suppressUpdate = true;
            slider.Value = value;
            _suppressUpdate = false;
            UpdatePreview();
        }
        else
        {
            box.Text = ((int)slider.Value).ToString();
        }
    }

    private void UpdatePreview()
    {
        var color = Color.FromArgb((byte)Alpha, (byte)Red, (byte)Green, (byte)Blue);
        _previewBorder.Background = new SolidColorBrush(color);

        if (Alpha < 255)
            _hexLabel.Text = $"#{Alpha:X2}{Red:X2}{Green:X2}{Blue:X2}";
        else
            _hexLabel.Text = $"#{Red:X2}{Green:X2}{Blue:X2}";
    }

    private static DrawingBrush CreateCheckerboardBrush()
    {
        var size = 8.0;
        var group = new DrawingGroup();

        group.Children.Add(new GeometryDrawing
        {
            Brush = Brushes.White,
            Geometry = new RectangleGeometry(new Rect(0, 0, size * 2, size * 2))
        });
        group.Children.Add(new GeometryDrawing
        {
            Brush = new SolidColorBrush(Color.FromRgb(204, 204, 204)),
            Geometry = new RectangleGeometry(new Rect(0, 0, size, size))
        });
        group.Children.Add(new GeometryDrawing
        {
            Brush = new SolidColorBrush(Color.FromRgb(204, 204, 204)),
            Geometry = new RectangleGeometry(new Rect(size, size, size, size))
        });

        return new DrawingBrush
        {
            Drawing = group,
            TileMode = TileMode.Tile,
            DestinationRect = new RelativeRect(0, 0, size * 2, size * 2, RelativeUnit.Absolute)
        };
    }
}

/// <summary>
/// Event args when a color is picked from the color picker popup.
/// </summary>
public class ColorPickedEventArgs : EventArgs
{
    public int R { get; set; }
    public int G { get; set; }
    public int B { get; set; }
    public int A { get; set; }
    public int Line { get; set; }
    public int ColorTextStartOffset { get; set; }
    public int ColorTextEndOffset { get; set; }
}
