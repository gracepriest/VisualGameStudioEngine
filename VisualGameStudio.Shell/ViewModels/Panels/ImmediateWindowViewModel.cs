using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Panels;

public partial class ImmediateWindowViewModel : ViewModelBase, IDisposable
{
    private readonly IDebugService _debugService;
    private readonly StringBuilder _outputBuffer = new();
    private readonly List<string> _commandHistory = new();
    private int _historyIndex = -1;

    [ObservableProperty]
    private string _inputText = "";

    [ObservableProperty]
    private string _outputText = "";

    [ObservableProperty]
    private bool _isEnabled = true;

    /// <summary>
    /// Prompt string displayed before the input. Shows ">" when debugging is paused,
    /// "[running]" when running, or "[no debug]" otherwise.
    /// </summary>
    [ObservableProperty]
    private string _prompt = ">";

    public ImmediateWindowViewModel(IDebugService debugService)
    {
        _debugService = debugService;
        _debugService.StateChanged += OnDebugStateChanged;
        _debugService.Stopped += OnDebugStopped;

        // Add welcome message
        AppendOutput("Immediate Window - Enter expressions to evaluate during debugging.\n");
        AppendOutput("Type an expression to evaluate (e.g., x + 1, name.Length)\n");
        AppendOutput("Type 'clear' to clear, 'help' for more commands.\n\n");

        UpdatePrompt();
    }

    public void Dispose()
    {
        _debugService.StateChanged -= OnDebugStateChanged;
        _debugService.Stopped -= OnDebugStopped;
    }

    private void OnDebugStateChanged(object? sender, DebugStateChangedEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (e.NewState == DebugState.Paused)
            {
                AppendOutput("[Debugger paused - ready for expressions]\n");
            }
            else if (e.NewState == DebugState.Running)
            {
                AppendOutput("[Debugger running...]\n");
            }
            else if (e.NewState == DebugState.Stopped)
            {
                AppendOutput("[Debug session ended]\n");
            }

            UpdatePrompt();
        });
    }

    private void OnDebugStopped(object? sender, StoppedEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            UpdatePrompt();
        });
    }

    private void UpdatePrompt()
    {
        if (!_debugService.IsDebugging)
        {
            Prompt = "[no debug] >";
        }
        else if (_debugService.State == DebugState.Paused)
        {
            Prompt = ">";
        }
        else
        {
            Prompt = "[running] >";
        }
    }

    public void AppendOutput(string text)
    {
        _outputBuffer.Append(text);
        OutputText = _outputBuffer.ToString();
    }

    [RelayCommand]
    private async Task ExecuteAsync()
    {
        var input = InputText.Trim();
        if (string.IsNullOrEmpty(input))
            return;

        // Add to history
        if (_commandHistory.Count == 0 || _commandHistory[^1] != input)
        {
            _commandHistory.Add(input);
        }
        _historyIndex = _commandHistory.Count;

        // Clear input
        InputText = "";

        // Handle special commands
        if (input.Equals("clear", StringComparison.OrdinalIgnoreCase) ||
            input.Equals(".cls", StringComparison.OrdinalIgnoreCase))
        {
            Clear();
            return;
        }

        if (input.Equals("help", StringComparison.OrdinalIgnoreCase) ||
            input.Equals("?", StringComparison.Ordinal))
        {
            ShowHelp();
            return;
        }

        // Show the command in output with prompt
        AppendOutput($"> {input}\n");

        // Check if we're debugging
        if (!_debugService.IsDebugging)
        {
            AppendOutput("Error: Not currently debugging. Start a debug session first.\n\n");
            return;
        }

        if (_debugService.State != DebugState.Paused)
        {
            AppendOutput("Error: Debugger must be paused to evaluate expressions.\n\n");
            return;
        }

        // Remove leading ? if present (common convention)
        var expression = input.StartsWith("?") ? input.Substring(1).Trim() : input;

        if (string.IsNullOrWhiteSpace(expression))
        {
            AppendOutput("(empty expression)\n\n");
            return;
        }

        try
        {
            // Use "repl" context for immediate window evaluations
            var result = await _debugService.EvaluateAsync(expression, context: "repl");
            if (result != null)
            {
                var value = result.Result ?? "";
                // Detect error results from the adapter
                if (value.StartsWith("<error") || value.StartsWith("<'") || value.StartsWith("<member") || value.StartsWith("<no frame"))
                {
                    AppendOutput($"Error: {value}\n\n");
                }
                else if (string.IsNullOrEmpty(value))
                {
                    AppendOutput("(no result)\n\n");
                }
                else
                {
                    var typeInfo = !string.IsNullOrEmpty(result.Type) ? $" [{result.Type}]" : "";
                    AppendOutput($"{value}{typeInfo}\n\n");
                }
            }
            else
            {
                AppendOutput("(no result)\n\n");
            }
        }
        catch (Exception ex)
        {
            AppendOutput($"Error: {ex.Message}\n\n");
        }
    }

    [RelayCommand]
    private void Clear()
    {
        _outputBuffer.Clear();
        OutputText = "";
        AppendOutput("Immediate Window cleared.\n\n");
    }

    [RelayCommand]
    private void HistoryUp()
    {
        if (_commandHistory.Count == 0)
            return;

        if (_historyIndex > 0)
        {
            _historyIndex--;
            InputText = _commandHistory[_historyIndex];
        }
        else if (_historyIndex == -1 && _commandHistory.Count > 0)
        {
            _historyIndex = _commandHistory.Count - 1;
            InputText = _commandHistory[_historyIndex];
        }
    }

    [RelayCommand]
    private void HistoryDown()
    {
        if (_commandHistory.Count == 0)
            return;

        if (_historyIndex < _commandHistory.Count - 1)
        {
            _historyIndex++;
            InputText = _commandHistory[_historyIndex];
        }
        else
        {
            _historyIndex = _commandHistory.Count;
            InputText = "";
        }
    }

    private void ShowHelp()
    {
        AppendOutput(@"
Immediate Window Commands:
--------------------------
<expression>   - Evaluate an expression (e.g., x + 1)
?<expression>  - Same as above, ? prefix is optional
variable       - Show value of a variable (e.g., counter)
var.member     - Access member (e.g., name.Length, person.Age)
clear / .cls   - Clear the window
help / ?       - Show this help message

Examples:
  counter           - Show value of 'counter' variable
  x + y             - Not yet supported (simple variable lookup only)
  name.Length        - Access member of an object
  person.Name       - Access field/property of an object

Note: Expressions can only be evaluated when the debugger is paused.

");
    }
}
