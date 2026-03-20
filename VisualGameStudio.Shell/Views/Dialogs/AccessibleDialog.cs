using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace VisualGameStudio.Shell.Views.Dialogs;

/// <summary>
/// Base class for accessible dialogs that provides:
/// - Focus trapping: Tab cycles through controls within the dialog only
/// - Escape closes the dialog
/// - Enter activates the default button (if any)
/// - Focus restoration via FocusManager when dialog closes
/// </summary>
public class AccessibleDialog : Window
{
    /// <summary>
    /// When true, pressing Enter will activate the button with Classes containing "primary".
    /// Set to false for dialogs where Enter should not trigger a button (e.g., text input dialogs).
    /// </summary>
    protected bool EnterActivatesDefaultButton { get; set; } = true;

    private static Control? GetCurrentlyFocused(Window window)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(window);
            if (topLevel == null) return null;
            // Avalonia 11.x: IFocusManager.GetFocusedElement may or may not exist.
            // Use reflection to safely call it.
            var fm = topLevel.FocusManager;
            if (fm == null) return null;
            // Try property first (Avalonia 11.1+)
            var prop = fm.GetType().GetProperty("FocusedElement");
            if (prop != null)
                return prop.GetValue(fm) as Control;
            // Try method
            var method = fm.GetType().GetMethod("GetFocusedElement");
            if (method != null)
                return method.Invoke(fm, null) as Control;
        }
        catch { }
        return null;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        var currentFocus = GetCurrentlyFocused(this);
        Shell.Services.FocusManager.Instance.OnDialogOpening(currentFocus);

        // Focus the first focusable control
        Dispatcher.UIThread.Post(() =>
        {
            var first = GetFocusableChildren().FirstOrDefault();
            first?.Focus();
        }, DispatcherPriority.Input);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        Shell.Services.FocusManager.Instance.OnDialogClosed();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && EnterActivatesDefaultButton)
        {
            var defaultButton = this.GetVisualDescendants()
                .OfType<Button>()
                .FirstOrDefault(b => b.Classes.Contains("primary") && b.IsEffectivelyEnabled);

            if (defaultButton != null)
            {
                defaultButton.Focus();
                if (defaultButton.Command?.CanExecute(defaultButton.CommandParameter) == true)
                {
                    defaultButton.Command.Execute(defaultButton.CommandParameter);
                    e.Handled = true;
                    return;
                }
            }
        }

        if (e.Key == Key.Tab)
        {
            var focusables = GetFocusableChildren().ToList();
            if (focusables.Count == 0)
            {
                e.Handled = true;
                return;
            }

            var focused = GetCurrentlyFocused(this);
            var currentIndex = focused != null ? focusables.IndexOf(focused) : -1;

            int nextIndex;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                nextIndex = currentIndex <= 0 ? focusables.Count - 1 : currentIndex - 1;
            }
            else
            {
                nextIndex = currentIndex >= focusables.Count - 1 ? 0 : currentIndex + 1;
            }

            focusables[nextIndex].Focus();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    private IEnumerable<Control> GetFocusableChildren()
    {
        return this.GetVisualDescendants()
            .OfType<Control>()
            .Where(c => c.Focusable && c.IsEffectivelyVisible && c.IsEffectivelyEnabled && c is not Panel && c is not Window);
    }
}
