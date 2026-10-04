using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using VisualGameStudio.Shell.ViewModels.Designer;

namespace VisualGameStudio.Shell.Views.Controls;

/// <summary>
/// The colour row's drop-down (slice 4 D-1): Custom / Web / System in a pop-up beside the row's text box. View-only: every
/// write goes through <see cref="FormPropertyRow.ApplyColor"/>, the row's own Commit, so Judge decides as it does for
/// typed text.
///
/// <para>⛔ D-1c — how often it writes. A Web or System pick is ONE write. The Custom tab writes ONCE, when the pop-up
/// closes by OK or a click away, and only if the user moved the colour: <see cref="ColorView.ColorChanged"/> fires for every
/// step of a drag across the spectrum, and a write per step would be hundreds of document rewrites and undo entries.
/// ⛔ Esc CANCELS the Custom move (no write), as VS's drop-down does.</para>
/// </summary>
public partial class FormColorDropDown : UserControl
{
    /// <summary>The user moved the Custom colour since the pop-up opened; it is written when the pop-up closes.</summary>
    private bool _customChanged;

    public FormColorDropDown()
    {
        InitializeComponent();

        // ⚠ TUNNEL, on both the pop-up's content and this control: Esc reaches whichever holds focus (the pop-up's content,
        // or the swatch button that opened it), and the flyout closes itself on the key afterwards — so the pending
        // Custom move must be dropped BEFORE Closed runs.
        AddHandler(KeyDownEvent, OnKeyDownTunnel, RoutingStrategies.Tunnel);
        Tabs.AddHandler(KeyDownEvent, OnKeyDownTunnel, RoutingStrategies.Tunnel);
    }

    private FormPropertyRow? Row => DataContext as FormPropertyRow;

    private void OnKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _customChanged = false;
        }
    }

    /// <summary>
    /// The pop-up opened: it shows the tab holding the row's value (VS's behaviour), the Custom tab starts at the row's
    /// colour (D-1) with an alpha channel only where the row takes one, and nothing is pending.
    /// </summary>
    private void OnFlyoutOpened(object? sender, EventArgs e)
    {
        Tabs.SelectedIndex = (int)FormColorChoices.TabFor(Row?.DisplayValue);

        // ⛔ WinForms run-time truth: a TextBox's (or a Form's) BackColor THROWS on a translucent colour, so the catalog
        // refuses one there and the editor does not offer the channel (FormPropertyDef.AcceptsTranslucentOn).
        CustomView.IsAlphaEnabled = Row?.AllowsAlpha ?? true;
        CustomView.Color = Row?.SwatchColor ?? Colors.White;

        // ⚠ AFTER the seed: setting Color raises ColorChanged synchronously, and the seed is not the user's move. (A
        // separate "seeding" flag around the set was an EQUIVALENT mutant — this reset already covers it.)
        _customChanged = false;
    }

    private void OnCustomColorChanged(object? sender, ColorChangedEventArgs e)
    {
        _customChanged = true; // written once, on close — never here (D-1c)
    }

    /// <summary>The pop-up closed (OK, a click away): the Custom colour is written once — if the user moved it and did not press Esc.</summary>
    private void OnFlyoutClosed(object? sender, EventArgs e)
    {
        if (!_customChanged)
        {
            return;
        }

        _customChanged = false;
        var color = CustomView.Color;
        if (Row is { AllowsAlpha: false })
        {
            color = Color.FromRgb(color.R, color.G, color.B); // the channel is off: never write a translucent value here
        }

        Row?.ApplyColor(FormColorChoices.ToDocumentText(color));
    }

    private void OnCustomOk(object? sender, RoutedEventArgs e) => SwatchButton.Flyout?.Hide();

    /// <summary>A Web or System entry: one write, then the pop-up closes (a pending Custom move is dropped — the pick wins).</summary>
    private void OnNamedColorPicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: FormColorChoice choice } || Row is not { } row)
        {
            return;
        }

        _customChanged = false;
        row.ApplyColor(choice.Name);
        SwatchButton.Flyout?.Hide();
    }
}
