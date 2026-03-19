using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace VisualGameStudio.Shell.Services;

/// <summary>
/// Manages keyboard focus across the IDE, tracking focus history, enabling
/// panel-to-panel navigation with F6/Shift+F6, and providing focus restoration
/// after dialogs close.
/// </summary>
public sealed class FocusManager
{
    private static FocusManager? _instance;
    public static FocusManager Instance => _instance ??= new FocusManager();

    /// <summary>
    /// Ordered list of panel identifiers for F6/Shift+F6 cycling.
    /// </summary>
    private static readonly string[] PanelOrder =
    {
        "SolutionExplorer",
        "Editor",
        "Output",
        "Terminal",
        "ErrorList",
        "Variables",
    };

    private readonly Stack<Control> _focusHistory = new();
    private int _currentPanelIndex = 1; // default: Editor
    private Control? _priorToDialog;

    private FocusManager() { }

    // ------------------------------------------------------------------
    // Focus History
    // ------------------------------------------------------------------

    /// <summary>
    /// Push the currently-focused control onto the history stack.
    /// Called automatically when a dialog opens or panel focus changes.
    /// </summary>
    public void PushFocus(Control? control)
    {
        if (control != null)
            _focusHistory.Push(control);
    }

    /// <summary>
    /// Pop and focus the previously-focused control (e.g., when a dialog closes).
    /// </summary>
    public void RestoreFocus()
    {
        while (_focusHistory.Count > 0)
        {
            var target = _focusHistory.Pop();
            if (target.IsEffectivelyVisible)
            {
                Dispatcher.UIThread.Post(() => target.Focus(), DispatcherPriority.Background);
                return;
            }
        }
    }

    // ------------------------------------------------------------------
    // Dialog Focus Trap helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Called when a dialog is about to open.
    /// Saves the currently-focused control so it can be restored later.
    /// </summary>
    public void OnDialogOpening(Control? currentFocus)
    {
        _priorToDialog = currentFocus;
    }

    /// <summary>
    /// Called when a dialog closes.
    /// Restores focus to the control that was focused before the dialog opened.
    /// </summary>
    public void OnDialogClosed()
    {
        if (_priorToDialog != null && _priorToDialog.IsEffectivelyVisible)
        {
            Dispatcher.UIThread.Post(() => _priorToDialog.Focus(), DispatcherPriority.Background);
            _priorToDialog = null;
        }
    }

    // ------------------------------------------------------------------
    // Panel cycling (F6 / Shift+F6)
    // ------------------------------------------------------------------

    /// <summary>
    /// Returns the panel identifier for the next panel in the cycle.
    /// </summary>
    public string FocusNextPanel()
    {
        _currentPanelIndex = (_currentPanelIndex + 1) % PanelOrder.Length;
        return PanelOrder[_currentPanelIndex];
    }

    /// <summary>
    /// Returns the panel identifier for the previous panel in the cycle.
    /// </summary>
    public string FocusPreviousPanel()
    {
        _currentPanelIndex = (_currentPanelIndex - 1 + PanelOrder.Length) % PanelOrder.Length;
        return PanelOrder[_currentPanelIndex];
    }

    /// <summary>
    /// Returns the panel identifier for a given Ctrl+N shortcut (1..9, 0 maps to 10).
    /// </summary>
    public string? FocusPanelByIndex(int index)
    {
        // Ctrl+1 => 0, Ctrl+2 => 1, ... Ctrl+0 => 5 (Variables)
        int mapped = index == 0 ? PanelOrder.Length - 1 : index - 1;
        if (mapped >= 0 && mapped < PanelOrder.Length)
        {
            _currentPanelIndex = mapped;
            return PanelOrder[mapped];
        }
        return null;
    }

    /// <summary>
    /// Updates the current panel index when a panel is focused directly.
    /// </summary>
    public void SetCurrentPanel(string panelName)
    {
        for (int i = 0; i < PanelOrder.Length; i++)
        {
            if (PanelOrder[i] == panelName)
            {
                _currentPanelIndex = i;
                return;
            }
        }
    }

    // ------------------------------------------------------------------
    // Focus indicator helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Finds the first focusable control within the given parent and focuses it.
    /// Returns true if a control was focused.
    /// </summary>
    public static bool FocusFirst(Control parent)
    {
        var focusable = FindFirstFocusable(parent);
        if (focusable != null)
        {
            focusable.Focus();
            return true;
        }
        return false;
    }

    private static Control? FindFirstFocusable(Control parent)
    {
        foreach (var child in parent.GetVisualDescendants())
        {
            if (child is Control c && c.Focusable && c.IsEffectivelyVisible && c is not Panel)
                return c;
        }
        return null;
    }
}
