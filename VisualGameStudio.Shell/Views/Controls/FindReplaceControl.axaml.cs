using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace VisualGameStudio.Shell.Views.Controls;

public partial class FindReplaceControl : UserControl
{
    public static readonly StyledProperty<string> SearchTextProperty =
        AvaloniaProperty.Register<FindReplaceControl, string>(nameof(SearchText), defaultValue: "");

    public static readonly StyledProperty<string> ReplaceTextProperty =
        AvaloniaProperty.Register<FindReplaceControl, string>(nameof(ReplaceText), defaultValue: "");

    public static readonly StyledProperty<bool> ShowReplaceProperty =
        AvaloniaProperty.Register<FindReplaceControl, bool>(nameof(ShowReplace), defaultValue: false);

    public static readonly StyledProperty<bool> MatchCaseProperty =
        AvaloniaProperty.Register<FindReplaceControl, bool>(nameof(MatchCase), defaultValue: false);

    public static readonly StyledProperty<bool> WholeWordProperty =
        AvaloniaProperty.Register<FindReplaceControl, bool>(nameof(WholeWord), defaultValue: false);

    public static readonly StyledProperty<bool> UseRegexProperty =
        AvaloniaProperty.Register<FindReplaceControl, bool>(nameof(UseRegex), defaultValue: false);

    public static readonly StyledProperty<bool> SearchInSelectionProperty =
        AvaloniaProperty.Register<FindReplaceControl, bool>(nameof(SearchInSelection), defaultValue: false);

    public static readonly StyledProperty<bool> PreserveCaseProperty =
        AvaloniaProperty.Register<FindReplaceControl, bool>(nameof(PreserveCase), defaultValue: false);

    public static readonly StyledProperty<string> MatchCountTextProperty =
        AvaloniaProperty.Register<FindReplaceControl, string>(nameof(MatchCountText), defaultValue: "");

    public string SearchText
    {
        get => GetValue(SearchTextProperty);
        set => SetValue(SearchTextProperty, value);
    }

    public string ReplaceText
    {
        get => GetValue(ReplaceTextProperty);
        set => SetValue(ReplaceTextProperty, value);
    }

    public bool ShowReplace
    {
        get => GetValue(ShowReplaceProperty);
        set => SetValue(ShowReplaceProperty, value);
    }

    public bool MatchCase
    {
        get => GetValue(MatchCaseProperty);
        set => SetValue(MatchCaseProperty, value);
    }

    public bool WholeWord
    {
        get => GetValue(WholeWordProperty);
        set => SetValue(WholeWordProperty, value);
    }

    public bool UseRegex
    {
        get => GetValue(UseRegexProperty);
        set => SetValue(UseRegexProperty, value);
    }

    public bool SearchInSelection
    {
        get => GetValue(SearchInSelectionProperty);
        set => SetValue(SearchInSelectionProperty, value);
    }

    public bool PreserveCase
    {
        get => GetValue(PreserveCaseProperty);
        set => SetValue(PreserveCaseProperty, value);
    }

    public string MatchCountText
    {
        get => GetValue(MatchCountTextProperty);
        set => SetValue(MatchCountTextProperty, value);
    }

    public event EventHandler? FindNext;
    public event EventHandler? FindPrevious;
    public event EventHandler? ReplaceRequested;
    public event EventHandler? ReplaceAllRequested;
    public event EventHandler? CloseRequested;
    public event EventHandler? SearchTextChangedEvent;

    public FindReplaceControl()
    {
        InitializeComponent();
    }

    public void FocusSearchBox()
    {
        var searchBox = this.FindControl<TextBox>("SearchTextBox");
        searchBox?.Focus();
        searchBox?.SelectAll();
    }

    public void FocusReplaceBox()
    {
        var replaceBox = this.FindControl<TextBox>("ReplaceTextBox");
        replaceBox?.Focus();
        replaceBox?.SelectAll();
    }

    public void SetInitialSearchText(string? text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            SearchText = text;
        }
    }

    private void OnToggleReplace(object? sender, RoutedEventArgs e)
    {
        ShowReplace = !ShowReplace;
        if (ShowReplace)
        {
            FocusReplaceBox();
        }
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    FindPrevious?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    FindNext?.Invoke(this, EventArgs.Empty);
                }
                e.Handled = true;
                break;

            case Key.Escape:
                CloseRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;

            case Key.F3:
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    FindPrevious?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    FindNext?.Invoke(this, EventArgs.Empty);
                }
                e.Handled = true;
                break;
        }

        // Alt shortcuts for options
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            switch (e.Key)
            {
                case Key.C:
                    MatchCase = !MatchCase;
                    e.Handled = true;
                    break;
                case Key.W:
                    WholeWord = !WholeWord;
                    e.Handled = true;
                    break;
                case Key.R:
                    UseRegex = !UseRegex;
                    e.Handled = true;
                    break;
            }
        }
    }

    private void OnReplaceKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    ReplaceAllRequested?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    ReplaceRequested?.Invoke(this, EventArgs.Empty);
                    FindNext?.Invoke(this, EventArgs.Empty);
                }
                e.Handled = true;
                break;

            case Key.Escape:
                CloseRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
        }
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        SearchTextChangedEvent?.Invoke(this, EventArgs.Empty);
    }

    private void OnFindNext(object? sender, RoutedEventArgs e)
    {
        FindNext?.Invoke(this, EventArgs.Empty);
    }

    private void OnFindPrevious(object? sender, RoutedEventArgs e)
    {
        FindPrevious?.Invoke(this, EventArgs.Empty);
    }

    private void OnReplace(object? sender, RoutedEventArgs e)
    {
        ReplaceRequested?.Invoke(this, EventArgs.Empty);
        FindNext?.Invoke(this, EventArgs.Empty);
    }

    private void OnReplaceAll(object? sender, RoutedEventArgs e)
    {
        ReplaceAllRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnClose(object? sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
