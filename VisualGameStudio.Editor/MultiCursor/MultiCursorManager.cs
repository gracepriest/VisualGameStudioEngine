using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;

namespace VisualGameStudio.Editor.MultiCursor;

/// <summary>
/// Represents a single cursor position with optional selection
/// </summary>
public class CursorPosition
{
    public int Offset { get; set; }
    public int? SelectionStart { get; set; }
    public int? SelectionEnd { get; set; }

    public bool HasSelection => SelectionStart.HasValue && SelectionEnd.HasValue && SelectionStart != SelectionEnd;
    public int SelectionLength => HasSelection ? Math.Abs(SelectionEnd!.Value - SelectionStart!.Value) : 0;

    public CursorPosition(int offset)
    {
        Offset = offset;
    }

    public CursorPosition(int offset, int selectionStart, int selectionEnd)
    {
        Offset = offset;
        SelectionStart = selectionStart;
        SelectionEnd = selectionEnd;
    }

    public CursorPosition Clone()
    {
        return new CursorPosition(Offset)
        {
            SelectionStart = SelectionStart,
            SelectionEnd = SelectionEnd
        };
    }
}

/// <summary>
/// Manages multiple cursor positions in the editor
/// </summary>
public class MultiCursorManager
{
    private readonly TextEditor _editor;
    private readonly List<CursorPosition> _cursors = new();
    private bool _isEnabled;

    public event EventHandler? CursorsChanged;

    public IReadOnlyList<CursorPosition> Cursors => _cursors.AsReadOnly();
    public bool IsEnabled => _isEnabled && _cursors.Count > 0;
    public int CursorCount => _cursors.Count + 1; // +1 for main caret

    public MultiCursorManager(TextEditor editor)
    {
        _editor = editor;
    }

    /// <summary>
    /// Enables multi-cursor mode
    /// </summary>
    public void Enable()
    {
        _isEnabled = true;
    }

    /// <summary>
    /// Disables multi-cursor mode and clears all additional cursors
    /// </summary>
    public void Disable()
    {
        _isEnabled = false;
        _cursors.Clear();
        CursorsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Adds a cursor at the specified offset
    /// </summary>
    public void AddCursor(int offset)
    {
        if (!_isEnabled) Enable();

        // Don't add duplicate cursors
        if (_cursors.Any(c => c.Offset == offset))
            return;

        // Don't add at main caret position
        if (_editor.CaretOffset == offset)
            return;

        _cursors.Add(new CursorPosition(offset));
        SortCursors();
        CursorsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Adds a cursor with selection at the specified position
    /// </summary>
    public void AddCursorWithSelection(int offset, int selectionStart, int selectionEnd)
    {
        if (!_isEnabled) Enable();

        // Don't add duplicate cursors
        if (_cursors.Any(c => c.Offset == offset))
            return;

        _cursors.Add(new CursorPosition(offset, selectionStart, selectionEnd));
        SortCursors();
        CursorsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Adds a cursor above the current line
    /// </summary>
    public void AddCursorAbove()
    {
        var caret = _editor.TextArea.Caret;
        var currentLine = caret.Line;
        var column = caret.Column;

        if (currentLine <= 1) return;

        var targetLine = currentLine - 1;

        // Find the topmost cursor to add above
        if (_cursors.Count > 0)
        {
            var topCursor = _cursors.OrderBy(c => c.Offset).First();
            var topLine = _editor.Document.GetLineByOffset(topCursor.Offset).LineNumber;
            if (topLine < currentLine)
            {
                targetLine = topLine - 1;
            }
        }

        if (targetLine < 1) return;

        var line = _editor.Document.GetLineByNumber(targetLine);
        var targetColumn = Math.Min(column, line.Length + 1);
        var offset = line.Offset + targetColumn - 1;

        AddCursor(offset);
    }

    /// <summary>
    /// Adds a cursor below the current line
    /// </summary>
    public void AddCursorBelow()
    {
        var caret = _editor.TextArea.Caret;
        var currentLine = caret.Line;
        var column = caret.Column;
        var lineCount = _editor.Document.LineCount;

        if (currentLine >= lineCount) return;

        var targetLine = currentLine + 1;

        // Find the bottommost cursor to add below
        if (_cursors.Count > 0)
        {
            var bottomCursor = _cursors.OrderByDescending(c => c.Offset).First();
            var bottomLine = _editor.Document.GetLineByOffset(bottomCursor.Offset).LineNumber;
            if (bottomLine > currentLine)
            {
                targetLine = bottomLine + 1;
            }
        }

        if (targetLine > lineCount) return;

        var line = _editor.Document.GetLineByNumber(targetLine);
        var targetColumn = Math.Min(column, line.Length + 1);
        var offset = line.Offset + targetColumn - 1;

        AddCursor(offset);
    }

    /// <summary>
    /// Adds next occurrence of the selected word as a new cursor
    /// </summary>
    public void AddNextOccurrence()
    {
        var selection = _editor.TextArea.Selection;
        string searchText;
        int searchStart;

        if (selection.IsEmpty)
        {
            // Select the word under cursor first
            var word = GetWordAtCaret();
            if (string.IsNullOrEmpty(word)) return;

            var wordStart = GetWordStartAtCaret();
            var wordEnd = wordStart + word.Length;

            _editor.Select(wordStart, word.Length);
            _editor.CaretOffset = wordEnd;
            CursorsChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        searchText = _editor.SelectedText;
        if (string.IsNullOrEmpty(searchText)) return;

        // Start searching from after the last cursor/selection
        searchStart = _editor.SelectionStart + _editor.SelectionLength;

        // Check additional cursors for the furthest selection end
        foreach (var cursor in _cursors)
        {
            if (cursor.HasSelection && cursor.SelectionEnd.HasValue)
            {
                searchStart = Math.Max(searchStart, cursor.SelectionEnd.Value);
            }
        }

        // Find next occurrence
        var text = _editor.Document.Text;
        var foundIndex = text.IndexOf(searchText, searchStart, StringComparison.Ordinal);

        // Wrap around if not found
        if (foundIndex < 0)
        {
            foundIndex = text.IndexOf(searchText, 0, StringComparison.Ordinal);
        }

        // Don't add if it's at the main selection or any existing cursor
        if (foundIndex >= 0 &&
            foundIndex != _editor.SelectionStart &&
            !_cursors.Any(c => c.SelectionStart == foundIndex))
        {
            AddCursorWithSelection(foundIndex + searchText.Length, foundIndex, foundIndex + searchText.Length);
        }
    }

    /// <summary>
    /// Selects all occurrences of the current word or selection
    /// </summary>
    public void SelectAllOccurrences()
    {
        var selection = _editor.TextArea.Selection;
        string searchText;

        if (selection.IsEmpty)
        {
            searchText = GetWordAtCaret();
            if (string.IsNullOrEmpty(searchText)) return;

            var wordStart = GetWordStartAtCaret();
            _editor.Select(wordStart, searchText.Length);
            _editor.CaretOffset = wordStart + searchText.Length;
        }
        else
        {
            searchText = _editor.SelectedText;
        }

        if (string.IsNullOrEmpty(searchText)) return;

        // Clear existing additional cursors
        _cursors.Clear();
        Enable();

        // Find all occurrences
        var text = _editor.Document.Text;
        var index = 0;
        var mainSelectionStart = _editor.SelectionStart;

        while ((index = text.IndexOf(searchText, index, StringComparison.Ordinal)) >= 0)
        {
            if (index != mainSelectionStart)
            {
                _cursors.Add(new CursorPosition(index + searchText.Length, index, index + searchText.Length));
            }
            index++;
        }

        SortCursors();
        CursorsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Types text at all cursor positions
    /// </summary>
    public void TypeText(string text)
    {
        if (!IsEnabled || string.IsNullOrEmpty(text)) return;

        var document = _editor.Document;
        document.BeginUpdate();

        try
        {
            // Process cursors from end to start to maintain correct offsets
            var allCursors = GetAllCursorsDescending();

            foreach (var cursor in allCursors)
            {
                if (cursor.HasSelection)
                {
                    var start = Math.Min(cursor.SelectionStart!.Value, cursor.SelectionEnd!.Value);
                    var length = cursor.SelectionLength;
                    document.Replace(start, length, text);
                }
                else
                {
                    document.Insert(cursor.Offset, text);
                }
            }

            // Update cursor positions
            UpdateCursorPositionsAfterEdit(text.Length);
        }
        finally
        {
            document.EndUpdate();
        }

        CursorsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Deletes text at all cursor positions (backspace)
    /// </summary>
    public void Backspace()
    {
        if (!IsEnabled) return;

        var document = _editor.Document;
        document.BeginUpdate();

        try
        {
            var allCursors = GetAllCursorsDescending();

            foreach (var cursor in allCursors)
            {
                if (cursor.HasSelection)
                {
                    var start = Math.Min(cursor.SelectionStart!.Value, cursor.SelectionEnd!.Value);
                    var length = cursor.SelectionLength;
                    document.Remove(start, length);
                }
                else if (cursor.Offset > 0)
                {
                    document.Remove(cursor.Offset - 1, 1);
                }
            }

            // Update cursor positions
            UpdateCursorPositionsAfterDelete();
        }
        finally
        {
            document.EndUpdate();
        }

        CursorsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Deletes text at all cursor positions (delete key)
    /// </summary>
    public void Delete()
    {
        if (!IsEnabled) return;

        var document = _editor.Document;
        document.BeginUpdate();

        try
        {
            var allCursors = GetAllCursorsDescending();

            foreach (var cursor in allCursors)
            {
                if (cursor.HasSelection)
                {
                    var start = Math.Min(cursor.SelectionStart!.Value, cursor.SelectionEnd!.Value);
                    var length = cursor.SelectionLength;
                    document.Remove(start, length);
                }
                else if (cursor.Offset < document.TextLength)
                {
                    document.Remove(cursor.Offset, 1);
                }
            }

            UpdateCursorPositionsAfterDelete();
        }
        finally
        {
            document.EndUpdate();
        }

        CursorsChanged?.Invoke(this, EventArgs.Empty);
    }

    private List<CursorPosition> GetAllCursorsDescending()
    {
        var all = new List<CursorPosition>();

        // Add main cursor
        var mainSelection = _editor.TextArea.Selection;
        if (!mainSelection.IsEmpty)
        {
            var seg = mainSelection.SurroundingSegment;
            all.Add(new CursorPosition(_editor.CaretOffset, seg.Offset, seg.EndOffset));
        }
        else
        {
            all.Add(new CursorPosition(_editor.CaretOffset));
        }

        // Add additional cursors
        all.AddRange(_cursors.Select(c => c.Clone()));

        // Sort by offset descending
        return all.OrderByDescending(c => c.Offset).ToList();
    }

    private void UpdateCursorPositionsAfterEdit(int insertLength)
    {
        // After an insert, we need to adjust cursor positions
        // Since we process from end to start, earlier cursors need adjustment
        // This is a simplified version - a full implementation would track exact changes
        _cursors.Clear(); // Clear selections after edit
    }

    private void UpdateCursorPositionsAfterDelete()
    {
        _cursors.Clear(); // Clear selections after delete
    }

    private void SortCursors()
    {
        _cursors.Sort((a, b) => a.Offset.CompareTo(b.Offset));
    }

    private string? GetWordAtCaret()
    {
        var document = _editor.Document;
        var offset = _editor.CaretOffset;

        if (offset <= 0 || offset > document.TextLength)
            return null;

        var start = offset;
        var end = offset;

        // Find word start
        while (start > 0 && IsWordChar(document.GetCharAt(start - 1)))
            start--;

        // Find word end
        while (end < document.TextLength && IsWordChar(document.GetCharAt(end)))
            end++;

        if (start < end)
            return document.GetText(start, end - start);

        return null;
    }

    private int GetWordStartAtCaret()
    {
        var document = _editor.Document;
        var offset = _editor.CaretOffset;

        var start = offset;
        while (start > 0 && IsWordChar(document.GetCharAt(start - 1)))
            start--;

        return start;
    }

    private bool IsWordChar(char c)
    {
        return char.IsLetterOrDigit(c) || c == '_';
    }
}
