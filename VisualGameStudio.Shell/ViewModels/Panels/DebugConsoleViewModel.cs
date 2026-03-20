using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Panels;

/// <summary>
/// VS Code-style Debug Console that allows evaluating expressions while debugging,
/// showing output, and interacting with the debugged program.
/// </summary>
public partial class DebugConsoleViewModel : ViewModelBase, IDisposable
{
    private readonly IDebugService _debugService;

    /// <summary>
    /// All output entries displayed in the console.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<DebugConsoleEntry> _entries = new();

    /// <summary>
    /// Current text in the input field.
    /// </summary>
    [ObservableProperty]
    private string _inputText = "";

    /// <summary>
    /// Command history for up/down arrow navigation.
    /// </summary>
    private readonly List<string> _inputHistory = new();
    private int _historyIndex = -1;

    /// <summary>
    /// Whether the debugger is currently active.
    /// </summary>
    [ObservableProperty]
    private bool _isDebugging;

    /// <summary>
    /// Whether the debugger is paused (expression eval is only possible when paused).
    /// </summary>
    [ObservableProperty]
    private bool _isPaused;

    /// <summary>
    /// Auto-complete suggestions for expression input.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<string> _suggestions = new();

    /// <summary>
    /// Whether the suggestions popup is visible.
    /// </summary>
    [ObservableProperty]
    private bool _isSuggestionsVisible;

    /// <summary>
    /// The selected suggestion index.
    /// </summary>
    [ObservableProperty]
    private int _selectedSuggestionIndex = -1;

    /// <summary>
    /// Current filter for entries (All, Errors, Warnings).
    /// </summary>
    [ObservableProperty]
    private string _filterMode = "All";

    /// <summary>
    /// Available filter modes.
    /// </summary>
    public ObservableCollection<string> FilterModes { get; } = new() { "All", "Errors", "Warnings", "Info" };

    /// <summary>
    /// Whether to show timestamps on entries.
    /// </summary>
    [ObservableProperty]
    private bool _showTimestamps;

    public DebugConsoleViewModel(IDebugService debugService)
    {
        _debugService = debugService;
        _debugService.StateChanged += OnDebugStateChanged;
        _debugService.Stopped += OnDebugStopped;
        _debugService.OutputReceived += OnDebugOutputReceived;

        AddInfoEntry("Debug Console ready. Evaluate expressions when paused at a breakpoint.");
    }

    public void Dispose()
    {
        _debugService.StateChanged -= OnDebugStateChanged;
        _debugService.Stopped -= OnDebugStopped;
        _debugService.OutputReceived -= OnDebugOutputReceived;
    }

    // =========================================================================
    // Debug service event handlers
    // =========================================================================

    private void OnDebugStateChanged(object? sender, DebugStateChangedEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            IsDebugging = e.NewState == DebugState.Running || e.NewState == DebugState.Paused;
            IsPaused = e.NewState == DebugState.Paused;

            switch (e.NewState)
            {
                case DebugState.Running:
                    if (e.OldState == DebugState.NotStarted || e.OldState == DebugState.Stopped)
                    {
                        AddInfoEntry("Debug session started.");
                    }
                    break;

                case DebugState.Paused:
                    AddInfoEntry("Debugger paused. You can evaluate expressions.");
                    break;

                case DebugState.Stopped:
                    AddInfoEntry("Debug session ended.");
                    IsDebugging = false;
                    IsPaused = false;
                    break;
            }
        });
    }

    private void OnDebugStopped(object? sender, StoppedEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var reason = e.Reason switch
            {
                StopReason.Breakpoint => "Breakpoint hit",
                StopReason.Step => "Step completed",
                StopReason.Exception => $"Exception: {e.Text}",
                StopReason.Pause => "Paused by user",
                StopReason.FunctionBreakpoint => "Function breakpoint hit",
                StopReason.DataBreakpoint => "Data breakpoint hit",
                _ => "Stopped"
            };

            if (!string.IsNullOrEmpty(e.Description))
            {
                reason += $" - {e.Description}";
            }

            AddInfoEntry(reason);
        });
    }

    private void OnDebugOutputReceived(object? sender, DebugOutputEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Output)) return;

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var entryType = e.Category switch
            {
                "stderr" => DebugConsoleEntryType.Error,
                "console" => DebugConsoleEntryType.Info,
                _ => DebugConsoleEntryType.Output
            };

            // Strip trailing newline for cleaner display
            var text = e.Output.TrimEnd('\r', '\n');
            if (!string.IsNullOrEmpty(text))
            {
                AddEntry(entryType, text);
            }
        });
    }

    // =========================================================================
    // Commands
    // =========================================================================

    /// <summary>
    /// Evaluate the current expression (Enter key).
    /// </summary>
    [RelayCommand]
    private async Task EvaluateAsync()
    {
        var input = InputText.Trim();
        if (string.IsNullOrEmpty(input))
            return;

        // Add to history
        if (_inputHistory.Count == 0 || _inputHistory[^1] != input)
        {
            _inputHistory.Add(input);
        }
        _historyIndex = _inputHistory.Count;

        // Clear input
        InputText = "";
        HideSuggestions();

        // Handle special commands
        if (input.Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            Clear();
            return;
        }

        if (input.Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            ShowHelp();
            return;
        }

        // Show the input in the console
        AddEntry(DebugConsoleEntryType.Input, input);

        // Check debugging state
        if (!_debugService.IsDebugging)
        {
            AddEntry(DebugConsoleEntryType.Error, "Cannot evaluate: not debugging. Start a debug session first.");
            return;
        }

        if (_debugService.State != DebugState.Paused)
        {
            AddEntry(DebugConsoleEntryType.Error, "Cannot evaluate: debugger must be paused.");
            return;
        }

        // Evaluate the expression
        try
        {
            var result = await _debugService.EvaluateAsync(input, context: "repl");
            if (result != null)
            {
                var entry = new DebugConsoleEntry
                {
                    EntryType = DebugConsoleEntryType.Output,
                    Text = result.Result ?? "(null)",
                    TypeName = result.Type ?? "",
                    VariablesReference = result.VariablesReference,
                    IsExpandable = result.VariablesReference > 0,
                    Timestamp = DateTime.Now
                };

                Entries.Add(entry);
            }
            else
            {
                AddEntry(DebugConsoleEntryType.Output, "(no result)");
            }
        }
        catch (Exception ex)
        {
            AddEntry(DebugConsoleEntryType.Error, $"Evaluation error: {ex.Message}");
        }
    }

    /// <summary>
    /// Clear all console entries.
    /// </summary>
    [RelayCommand]
    private void Clear()
    {
        Entries.Clear();
        AddInfoEntry("Console cleared.");
    }

    /// <summary>
    /// Navigate up through command history.
    /// </summary>
    [RelayCommand]
    private void HistoryUp()
    {
        if (_inputHistory.Count == 0) return;

        if (_historyIndex > 0)
        {
            _historyIndex--;
            InputText = _inputHistory[_historyIndex];
        }
        else if (_historyIndex == -1 && _inputHistory.Count > 0)
        {
            _historyIndex = _inputHistory.Count - 1;
            InputText = _inputHistory[_historyIndex];
        }
    }

    /// <summary>
    /// Navigate down through command history.
    /// </summary>
    [RelayCommand]
    private void HistoryDown()
    {
        if (_inputHistory.Count == 0) return;

        if (_historyIndex < _inputHistory.Count - 1)
        {
            _historyIndex++;
            InputText = _inputHistory[_historyIndex];
        }
        else
        {
            _historyIndex = _inputHistory.Count;
            InputText = "";
        }
    }

    /// <summary>
    /// Copy an entry's text to the clipboard.
    /// </summary>
    [RelayCommand]
    private async Task CopyEntryAsync(DebugConsoleEntry? entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.Text)) return;

        try
        {
            var topLevel = Avalonia.Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;
            var clipboard = topLevel?.Clipboard;
            if (clipboard != null)
            {
                var copyText = string.IsNullOrEmpty(entry.TypeName)
                    ? entry.Text
                    : $"{entry.Text} ({entry.TypeName})";
                await clipboard.SetTextAsync(copyText);
            }
        }
        catch (Exception) { }
    }

    /// <summary>
    /// Expand an entry to load its children from the debugger.
    /// </summary>
    [RelayCommand]
    private async Task ExpandEntryAsync(DebugConsoleEntry? entry)
    {
        if (entry == null || !entry.IsExpandable || entry.ChildrenLoaded) return;

        if (!_debugService.IsDebugging || entry.VariablesReference <= 0)
            return;

        try
        {
            var children = await _debugService.GetVariablesAsync(entry.VariablesReference);
            entry.Children.Clear();

            foreach (var child in children)
            {
                entry.Children.Add(new DebugConsoleEntry
                {
                    EntryType = DebugConsoleEntryType.Output,
                    Text = $"{child.Name}: {child.Value}",
                    TypeName = child.Type ?? "",
                    VariablesReference = child.VariablesReference,
                    IsExpandable = child.VariablesReference > 0,
                    Timestamp = DateTime.Now
                });
            }

            entry.ChildrenLoaded = true;
            entry.IsExpanded = true;
        }
        catch (Exception ex)
        {
            entry.Children.Add(new DebugConsoleEntry
            {
                EntryType = DebugConsoleEntryType.Error,
                Text = $"Failed to expand: {ex.Message}",
                Timestamp = DateTime.Now
            });
            entry.ChildrenLoaded = true;
        }
    }

    /// <summary>
    /// Update auto-complete suggestions based on current input.
    /// Called when the input text changes.
    /// </summary>
    public async Task UpdateSuggestionsAsync()
    {
        if (string.IsNullOrEmpty(InputText) || !IsPaused)
        {
            HideSuggestions();
            return;
        }

        try
        {
            // Get variable completions from the debugger by fetching scopes and variables
            var frames = await _debugService.GetStackTraceAsync();
            var topFrame = frames.FirstOrDefault();
            if (topFrame == null)
            {
                HideSuggestions();
                return;
            }

            var scopes = await _debugService.GetScopesAsync(topFrame.Id);
            var completionItems = new List<string>();

            foreach (var scope in scopes)
            {
                var variables = await _debugService.GetVariablesAsync(scope.VariablesReference);
                foreach (var v in variables)
                {
                    if (v.Name.StartsWith(InputText, StringComparison.OrdinalIgnoreCase)
                        || InputText.Length <= 1)
                    {
                        completionItems.Add(v.Name);
                    }
                }
            }

            if (completionItems.Count > 0)
            {
                Suggestions.Clear();
                foreach (var item in completionItems.Distinct().OrderBy(x => x).Take(20))
                {
                    Suggestions.Add(item);
                }
                IsSuggestionsVisible = true;
                SelectedSuggestionIndex = 0;
            }
            else
            {
                HideSuggestions();
            }
        }
        catch
        {
            HideSuggestions();
        }
    }

    /// <summary>
    /// Accept the currently selected suggestion.
    /// </summary>
    public void AcceptSuggestion()
    {
        if (IsSuggestionsVisible && SelectedSuggestionIndex >= 0 && SelectedSuggestionIndex < Suggestions.Count)
        {
            // If input contains a dot, replace only the part after the last dot
            var dotIndex = InputText.LastIndexOf('.');
            if (dotIndex >= 0)
            {
                InputText = InputText.Substring(0, dotIndex + 1) + Suggestions[SelectedSuggestionIndex];
            }
            else
            {
                InputText = Suggestions[SelectedSuggestionIndex];
            }
        }
        HideSuggestions();
    }

    /// <summary>
    /// Focus the input field. Called when a breakpoint is hit.
    /// </summary>
    public event Action? FocusInputRequested;

    public void RequestFocusInput()
    {
        FocusInputRequested?.Invoke();
    }

    // =========================================================================
    // Helper methods
    // =========================================================================

    private void AddEntry(DebugConsoleEntryType type, string text)
    {
        Entries.Add(new DebugConsoleEntry
        {
            EntryType = type,
            Text = text,
            Timestamp = DateTime.Now
        });
    }

    private void AddInfoEntry(string text)
    {
        AddEntry(DebugConsoleEntryType.Info, text);
    }

    private void HideSuggestions()
    {
        IsSuggestionsVisible = false;
        Suggestions.Clear();
        SelectedSuggestionIndex = -1;
    }

    private void ShowHelp()
    {
        AddInfoEntry("Debug Console Commands:");
        AddInfoEntry("  <expression>  - Evaluate an expression (e.g., myVar, obj.Property, arr[0])");
        AddInfoEntry("  clear         - Clear the console");
        AddInfoEntry("  help          - Show this help message");
        AddInfoEntry("");
        AddInfoEntry("Supported expressions:");
        AddInfoEntry("  Variable names:    myVar");
        AddInfoEntry("  Property access:   obj.Property");
        AddInfoEntry("  Array indexing:    arr[0]");
        AddInfoEntry("  Method calls:      str.Length");
        AddInfoEntry("  Simple math:       x + y");
        AddInfoEntry("  Comparison:        x > 5");
        AddInfoEntry("");
        AddInfoEntry("Use Up/Down arrows to navigate command history.");
        AddInfoEntry("Tab to accept auto-complete suggestion.");
    }
}
