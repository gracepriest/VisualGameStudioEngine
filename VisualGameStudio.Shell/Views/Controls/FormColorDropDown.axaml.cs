using Avalonia.Controls;
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
/// closes (OK closes it), and only if the user moved the colour: <see cref="ColorView.ColorChanged"/> fires for every step
/// of a drag across the spectrum, and a write per step would be hundreds of document rewrites and undo entries.</para>
/// </summary>
public partial class FormColorDropDown : UserControl
{
    /// <summary>The user moved the Custom colour since the pop-up opened; it is written when the pop-up closes.</summary>
    private bool _customChanged;

    public FormColorDropDown()
    {
        InitializeComponent();
    }

    private FormPropertyRow? Row => DataContext as FormPropertyRow;

    /// <summary>
    /// The pop-up opened: it shows the tab holding the row's value (VS's behaviour), the Custom tab starts at the row's
    /// colour (D-1), and nothing is pending.
    /// </summary>
    private void OnFlyoutOpened(object? sender, EventArgs e)
    {
        Tabs.SelectedIndex = (int)FormColorChoices.TabFor(Row?.DisplayValue);
        CustomView.Color = Row?.SwatchColor ?? Colors.White;

        // ⚠ AFTER the seed: setting Color raises ColorChanged synchronously, and the seed is not the user's move. (A
        // separate "seeding" flag around the set was an EQUIVALENT mutant — this reset already covers it.)
        _customChanged = false;
    }

    private void OnCustomColorChanged(object? sender, ColorChangedEventArgs e)
    {
        _customChanged = true; // written once, on close — never here (D-1c)
    }

    /// <summary>The pop-up closed (OK, a click away, Esc): the Custom colour is written once — if the user moved it.</summary>
    private void OnFlyoutClosed(object? sender, EventArgs e)
    {
        if (!_customChanged)
        {
            return;
        }

        _customChanged = false;
        Row?.ApplyColor(FormColorChoices.ToDocumentText(CustomView.Color));
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
