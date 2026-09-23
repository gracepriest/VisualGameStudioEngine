using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace VisualGameStudio.Shell.Controls;

/// <summary>
/// Task 22 (commit 24d) — the "Type Here" in-place editor that floats over
/// <see cref="FormCanvasControl"/>: a single-line box the user types a strip item's caption into,
/// positioned over the slot the canvas just drew (spec §6).
///
/// <para>A <see cref="Canvas"/> with exactly ONE child, the <see cref="TextBox"/>, placed at
/// <see cref="SlotBounds"/> (canvas coordinates — the canvas publishes them as
/// <c>TypeHereBounds</c>). Enter commits the typed caption through <see cref="CommitCommand"/> and
/// clears the box, ready for the next item in the same run; Escape fires
/// <see cref="CancelCommand"/>. <see cref="Text"/> is two-way with the box.</para>
///
/// <para>⛔ Deliberately has NO <c>Background</c>. A Panel with a Background is hit-testable across
/// its whole area, and this overlay is laid over the entire design surface — it would swallow every
/// click aimed at the canvas beneath it. <see cref="FormCanvasControl"/> fills its viewport with
/// <c>Brushes.Transparent</c> for the OPPOSITE reason (it WANTS the whole viewport to be input —
/// see the comment at the top of its <c>Render</c>); copying that here is the natural mistake.
/// With no Background only the box itself is ever hit-testable, even while active.</para>
/// </summary>
public class FormTypeHereEditor : Canvas
{
    private readonly TextBox _box = new();

    public FormTypeHereEditor()
    {
        // ⛔⛔ BORN HIDDEN (plan PRE-FLIGHT BLOCKER 4) — here, in the constructor, and NOT only in
        // OnPropertyChanged. Avalonia's IsVisible/IsHitTestVisible default TRUE while IsActive
        // defaults FALSE, and a styled property set to the value it already holds raises no change
        // notification: when Task 24's binding resolves IsActive to false at startup, NOTHING
        // fires. An OnPropertyChanged-only implementation therefore leaves an empty, focusable,
        // hit-testable TextBox sitting permanently over the design surface at (0,0).
        IsVisible = false;
        IsHitTestVisible = false;

        // ⛔ The FluentTheme clamp. FluentTheme's TextBox theme sets MinHeight 32 / MinWidth 64
        // (and a padding sized for a 32px box), so a 22px slot would render 32px tall and the
        // overlay would overhang the slot by 10px. Measured red before this: 0,0,64,32 against an
        // expected 30,40,120,22.
        //
        // ⚠ These are LOCAL VALUES on purpose, never a Style. The Shell (App.axaml) loads
        // FluentTheme AND Resources/Styles/AppStyles.axaml, whose global Style Selector="TextBox"
        // sets Padding="8,4" — 8px of the 22 gone before a glyph is drawn. The headless test app
        // (DesignerHeadlessApp) loads FluentTheme ALONE, so a Style-based fix could pass headless
        // and then lose to AppStyles in the IDE depending on style order. A local value outranks
        // every style and theme setter in Avalonia's priority order, which is what makes the
        // headless green hold in the real IDE. The Shell's own AXAML neutralises small hosted
        // controls the same way, with attributes (also local values).
        _box.MinHeight = 0;
        _box.MinWidth = 0;
        _box.Padding = new Thickness(2, 0);

        // Text is two-way with the box: the view model's Text (Task 24 binds it) is what the user
        // is typing, and clearing the box after a commit flows back out through the same binding.
        _box[!!TextBox.TextProperty] = this[!!TextProperty];

        _box.KeyDown += OnBoxKeyDown;

        Children.Add(_box);
    }

    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<FormTypeHereEditor, bool>(nameof(IsActive));

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public static readonly StyledProperty<object?> HostProperty =
        AvaloniaProperty.Register<FormTypeHereEditor, object?>(nameof(Host));

    public object? Host
    {
        get => GetValue(HostProperty);
        set => SetValue(HostProperty, value);
    }

    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<FormTypeHereEditor, string>(nameof(Text), defaultValue: "");

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly StyledProperty<Rect> SlotBoundsProperty =
        AvaloniaProperty.Register<FormTypeHereEditor, Rect>(nameof(SlotBounds));

    public Rect SlotBounds
    {
        get => GetValue(SlotBoundsProperty);
        set => SetValue(SlotBoundsProperty, value);
    }

    public static readonly StyledProperty<ICommand?> CommitCommandProperty =
        AvaloniaProperty.Register<FormTypeHereEditor, ICommand?>(nameof(CommitCommand));

    public ICommand? CommitCommand
    {
        get => GetValue(CommitCommandProperty);
        set => SetValue(CommitCommandProperty, value);
    }

    public static readonly StyledProperty<ICommand?> CancelCommandProperty =
        AvaloniaProperty.Register<FormTypeHereEditor, ICommand?>(nameof(CancelCommand));

    public ICommand? CancelCommand
    {
        get => GetValue(CancelCommandProperty);
        set => SetValue(CancelCommandProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // ⛔ A Host change while ACTIVE re-focuses too, not only an IsActive change. That is the
        // whole of "type a whole menu in one run": each Begin on a new host moves the editor while
        // IsActive stays true, so an IsActive-only rule would focus the box exactly once. And the
        // press that began it went through FormCanvasControl.OnPointerPressed, which calls its own
        // Focus() unconditionally before firing BeginTypeHereCommand — so without this the CANVAS
        // holds keyboard focus while the user believes they are typing into the slot, and the next
        // Delete reaches the canvas's OnKeyDown → DeleteCommand and deletes the selected control.
        if (change.Property == IsActiveProperty
            || (change.Property == HostProperty && IsActive))
        {
            var active = IsActive;
            IsVisible = active;
            IsHitTestVisible = active;

            if (active)
            {
                // POSTED, not called: the canvas's Focus() runs in the same press handler that
                // fired Begin, and a direct call here would be overwritten by it. The posted call
                // runs after that handler returns. It re-checks IsActive because a Begin and a
                // Cancel in the same dispatcher turn must not leave focus in a hidden box.
                Dispatcher.UIThread.Post(() =>
                {
                    if (IsActive)
                    {
                        _box.Focus();
                    }
                });
            }
        }
        else if (change.Property == SlotBoundsProperty)
        {
            var slot = SlotBounds;
            Canvas.SetLeft(_box, slot.X);
            Canvas.SetTop(_box, slot.Y);
            _box.Width = slot.Width;
            _box.Height = slot.Height;
        }
    }

    /// <summary>
    /// Enter commits, Escape cancels; both are marked handled whether or not a command ran, so
    /// neither key falls through past the editor to a window-level binding.
    ///
    /// <para>Guards with <c>CanExecute</c>, as FormCanvasControl's command sites do: a caller that
    /// skips it runs a CommunityToolkit <c>RelayCommand</c> the view model has just refused (its
    /// <c>Execute</c> does not re-check). A REFUSED commit leaves the typed text in the box rather
    /// than discarding what the user typed with nothing having happened.</para>
    /// </summary>
    private void OnBoxKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
            {
                var text = _box.Text ?? "";
                var commit = CommitCommand;
                if (commit?.CanExecute(text) == true)
                {
                    commit.Execute(text);
                    _box.Text = "";
                }

                e.Handled = true;
                break;
            }

            case Key.Escape:
            {
                var cancel = CancelCommand;
                if (cancel?.CanExecute(null) == true)
                {
                    cancel.Execute(null);
                }

                e.Handled = true;
                break;
            }
        }
    }
}
