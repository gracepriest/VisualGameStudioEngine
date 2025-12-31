using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Panels;

public partial class ImmediateWindowViewModel : ViewModelBase
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

    public ImmediateWindowViewModel(IDebugService debugService)
    {
        _debugService = debugService;
        _debugService.StateChanged += OnDebugStateChanged;

        // Add welcome message
        AppendOutput("Immediate Window - Enter expressions to evaluate during debugging.\n");
        AppendOutput("Type '?' followed by an expression to evaluate (e.g., ?x + 1)\n");
        AppendOutput("Type 'clear' to clear the window.\n\n");
    }

    private void OnDebugStateChanged(object? sender, DebugStateChangedEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (e.NewState == DebugState.Paused)
            {
                AppendOutput("[Debugger paused]\n");
            }
            else if (e.NewState == DebugState.Running)
            {
                AppendOutput("[Debugger running...]\n");
            }
            else if (e.NewState == DebugState.Stopped)
            {
                AppendOutput("[Debug session ended]\n");
            }
        });
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

        // Show the command in output
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

        // Remove leading ? if present
        var expression = input.StartsWith("?") ? input.Substring(1).Trim() : input;

        try
        {
            var result = await _debugService.EvaluateAsync(expression);
            if (result != null)
            {
                var typeInfo = !string.IsNullOrEmpty(result.Type) ? $" ({result.Type})" : "";
                AppendOutput($"{result.Result}{typeInfo}\n\n");
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
?<expression>  - Evaluate an expression (e.g., ?x + 1)
<expression>   - Same as above, ? is optional
clear          - Clear the window
help           - Show this help message

Examples:
  ?counter           - Show value of 'counter' variable
  ?x + y             - Evaluate expression
  ?CalculateSum(5)   - Call a function

Note: Expressions can only be evaluated when the debugger is paused.

");
    }
}
