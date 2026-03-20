using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace VisualGameStudio.Shell.Controls;

/// <summary>
/// A small circular badge control that displays a count.
/// Automatically hides when Count is 0.
/// Supports BadgeKind for color: Error (red) or Info (blue).
/// </summary>
public partial class BadgeControl : UserControl
{
    public static readonly StyledProperty<int> CountProperty =
        AvaloniaProperty.Register<BadgeControl, int>(nameof(Count), defaultValue: 0);

    public static readonly StyledProperty<BadgeKind> KindProperty =
        AvaloniaProperty.Register<BadgeControl, BadgeKind>(nameof(Kind), defaultValue: BadgeKind.Info);

    public int Count
    {
        get => GetValue(CountProperty);
        set => SetValue(CountProperty, value);
    }

    public BadgeKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    private Border? _badgeBorder;
    private TextBlock? _badgeText;

    private static readonly IBrush InfoBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC));   // VS Code blue
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0x14, 0x00));   // Red

    public BadgeControl()
    {
        InitializeComponent();
        _badgeBorder = this.FindControl<Border>("BadgeBorder");
        _badgeText = this.FindControl<TextBlock>("BadgeText");
        UpdateBadge();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CountProperty || change.Property == KindProperty)
        {
            UpdateBadge();
        }
    }

    private void UpdateBadge()
    {
        if (_badgeBorder == null || _badgeText == null) return;

        var count = Count;
        _badgeBorder.IsVisible = count > 0;
        _badgeText.Text = count > 99 ? "99+" : count.ToString();
        _badgeBorder.Background = Kind == BadgeKind.Error ? ErrorBrush : InfoBrush;
    }
}

public enum BadgeKind
{
    Info,
    Error
}
