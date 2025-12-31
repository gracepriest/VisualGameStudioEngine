using Avalonia.Input;
using AvaloniaEdit;
using AvaloniaEdit.Editing;

namespace VisualGameStudio.Editor.MultiCursor;

/// <summary>
/// Handles keyboard and mouse input for multi-cursor functionality
/// </summary>
public class MultiCursorInputHandler
{
    private readonly TextEditor _editor;
    private readonly MultiCursorManager _manager;

    public MultiCursorInputHandler(TextEditor editor, MultiCursorManager manager)
    {
        _editor = editor;
        _manager = manager;
    }

    /// <summary>
    /// Handles pointer pressed events for Alt+Click cursor placement
    /// </summary>
    public bool HandlePointerPressed(PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(_editor.TextArea.TextView);

        // Alt+Click adds a cursor
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt) && point.Properties.IsLeftButtonPressed)
        {
            var position = _editor.TextArea.TextView.GetPositionFloor(point.Position + _editor.TextArea.TextView.ScrollOffset);
            if (position.HasValue)
            {
                var offset = _editor.Document.GetOffset(position.Value.Location);
                _manager.AddCursor(offset);
                e.Handled = true;
                return true;
            }
        }

        // Any click without Alt clears multi-cursor mode
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Alt) && _manager.IsEnabled)
        {
            _manager.Disable();
        }

        return false;
    }

    /// <summary>
    /// Handles key down events for multi-cursor operations
    /// Returns true if the event was handled
    /// </summary>
    public bool HandleKeyDown(KeyEventArgs e)
    {
        // Escape clears multi-cursor mode
        if (e.Key == Key.Escape && _manager.IsEnabled)
        {
            _manager.Disable();
            e.Handled = true;
            return true;
        }

        // Ctrl+Alt+Up - Add cursor above
        if (e.Key == Key.Up && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Alt))
        {
            _manager.AddCursorAbove();
            e.Handled = true;
            return true;
        }

        // Ctrl+Alt+Down - Add cursor below
        if (e.Key == Key.Down && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Alt))
        {
            _manager.AddCursorBelow();
            e.Handled = true;
            return true;
        }

        // Ctrl+D - Add next occurrence
        if (e.Key == Key.D && e.KeyModifiers == KeyModifiers.Control)
        {
            _manager.AddNextOccurrence();
            e.Handled = true;
            return true;
        }

        // Ctrl+Shift+L - Select all occurrences
        if (e.Key == Key.L && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
        {
            _manager.SelectAllOccurrences();
            e.Handled = true;
            return true;
        }

        // Handle text input for multi-cursor mode
        if (_manager.IsEnabled)
        {
            // Backspace
            if (e.Key == Key.Back)
            {
                _manager.Backspace();
                e.Handled = true;
                return true;
            }

            // Delete
            if (e.Key == Key.Delete)
            {
                _manager.Delete();
                e.Handled = true;
                return true;
            }

            // Enter/Return
            if (e.Key == Key.Enter || e.Key == Key.Return)
            {
                _manager.TypeText(Environment.NewLine);
                e.Handled = true;
                return true;
            }

            // Tab
            if (e.Key == Key.Tab)
            {
                _manager.TypeText("\t");
                e.Handled = true;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Handles text input for multi-cursor mode
    /// </summary>
    public bool HandleTextInput(TextInputEventArgs e)
    {
        if (_manager.IsEnabled && !string.IsNullOrEmpty(e.Text))
        {
            _manager.TypeText(e.Text);
            e.Handled = true;
            return true;
        }

        return false;
    }
}
