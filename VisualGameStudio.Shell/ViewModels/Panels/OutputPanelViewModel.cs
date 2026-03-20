using System.Collections.ObjectModel;
using System.Text;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Panels;

/// <summary>
/// Represents a single line in the output panel.
/// Lines matching error/warning patterns are clickable and navigate to the source location.
/// </summary>
public class OutputLine
{
    public string Text { get; }
    public bool IsClickable { get; }
    public bool IsError => Severity == OutputLineSeverity.Error;
    public bool IsWarning => Severity == OutputLineSeverity.Warning;
    public string? FilePath { get; }
    public int Line { get; }
    public int Column { get; }
    public OutputLineSeverity Severity { get; }

    public OutputLine(string text)
    {
        Text = text;
        IsClickable = false;
        Severity = OutputLineSeverity.Normal;
    }

    public OutputLine(string text, string filePath, int line, int column, OutputLineSeverity severity)
    {
        Text = text;
        IsClickable = true;
        FilePath = filePath;
        Line = line;
        Column = column;
        Severity = severity;
    }
}

public enum OutputLineSeverity
{
    Normal,
    Error,
    Warning,
    Info
}

public partial class OutputPanelViewModel : ViewModelBase
{
    private readonly IOutputService _outputService;
    private readonly IDebugService _debugService;
    private readonly StringBuilder _outputBuffer = new();

    // Patterns for matching error/warning lines in build output
    // BasicLang format:  file.bas(line,col): error BL1001: message
    // C# format:         file.cs(line,col): error CS0103: message
    // MSBuild format:    file.cs(line,col): warning CS0168: message
    // Also matches:      file.bas(line): error BL1001: message (no column)
    private static readonly Regex DiagnosticPattern = new(
        @"^(.*?)\((\d+)(?:,(\d+))?\)\s*:\s*(error|warning|info)\s+\w+\s*:\s*(.*)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [ObservableProperty]
    private string _outputText = "";

    [ObservableProperty]
    private string _inputText = "";

    [ObservableProperty]
    private OutputCategory _selectedCategory = OutputCategory.Build;

    [ObservableProperty]
    private bool _isInputEnabled = false;

    /// <summary>
    /// Collection of parsed output lines for the ItemsControl display.
    /// </summary>
    public ObservableCollection<OutputLine> OutputLines { get; } = new();

    /// <summary>
    /// Raised when the user clicks on an error/warning line to navigate to the source location.
    /// </summary>
    public event EventHandler<OutputLineNavigationEventArgs>? NavigateToSourceRequested;

    public OutputPanelViewModel(IOutputService outputService, IDebugService debugService)
    {
        _outputService = outputService;
        _debugService = debugService;
        _outputService.OutputReceived += OnOutputReceived;
        _debugService.StateChanged += OnDebugStateChanged;
    }

    private void OnDebugStateChanged(object? sender, DebugStateChangedEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            IsInputEnabled = e.NewState == DebugState.Running;
            if (e.NewState == DebugState.Stopped)
            {
                InputText = "";
            }
        });
    }

    private void OnOutputReceived(object? sender, OutputEventArgs e)
    {
        if (e.Category == SelectedCategory)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _outputBuffer.Append(e.Message);
                OutputText = _outputBuffer.ToString();
                ParseAndAppendLines(e.Message);
            });
        }
    }

    public void AppendOutput(string text)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _outputBuffer.Append(text);
            OutputText = _outputBuffer.ToString();
            ParseAndAppendLines(text);
        });
    }

    /// <summary>
    /// Parses incoming text into OutputLine objects, detecting error/warning patterns.
    /// </summary>
    private void ParseAndAppendLines(string text)
    {
        var lines = text.Split('\n');
        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrEmpty(line))
                continue;

            var match = DiagnosticPattern.Match(line);
            if (match.Success)
            {
                var filePath = match.Groups[1].Value.Trim();
                var lineNum = int.Parse(match.Groups[2].Value);
                var colNum = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 1;
                var severityStr = match.Groups[4].Value.ToLowerInvariant();

                var severity = severityStr switch
                {
                    "error" => OutputLineSeverity.Error,
                    "warning" => OutputLineSeverity.Warning,
                    "info" => OutputLineSeverity.Info,
                    _ => OutputLineSeverity.Normal
                };

                OutputLines.Add(new OutputLine(line, filePath, lineNum, colNum, severity));
            }
            else
            {
                OutputLines.Add(new OutputLine(line));
            }
        }
    }

    /// <summary>
    /// Called when the user clicks on a clickable output line.
    /// </summary>
    [RelayCommand]
    private void NavigateToSource(OutputLine? outputLine)
    {
        if (outputLine is { IsClickable: true, FilePath: not null })
        {
            NavigateToSourceRequested?.Invoke(this,
                new OutputLineNavigationEventArgs(outputLine.FilePath, outputLine.Line, outputLine.Column));
        }
    }

    partial void OnSelectedCategoryChanged(OutputCategory value)
    {
        RefreshOutput();
    }

    private void RefreshOutput()
    {
        _outputBuffer.Clear();
        OutputLines.Clear();
        var messages = _outputService.GetMessages(SelectedCategory);
        foreach (var message in messages)
        {
            _outputBuffer.Append(message);
            ParseAndAppendLines(message);
        }
        OutputText = _outputBuffer.ToString();
    }

    [RelayCommand]
    private void Clear()
    {
        _outputService.Clear(SelectedCategory);
        _outputBuffer.Clear();
        OutputText = "";
        OutputLines.Clear();
    }

    [RelayCommand]
    private void ShowBuildOutput()
    {
        SelectedCategory = OutputCategory.Build;
    }

    [RelayCommand]
    private void ShowGeneralOutput()
    {
        SelectedCategory = OutputCategory.General;
    }

    [RelayCommand]
    private void ShowDebugOutput()
    {
        SelectedCategory = OutputCategory.Debug;
    }

    [RelayCommand]
    private void ShowLspOutput()
    {
        SelectedCategory = OutputCategory.LanguageServer;
    }

    [RelayCommand]
    private async Task SendInput()
    {
        if (!string.IsNullOrEmpty(InputText) && IsInputEnabled)
        {
            var input = InputText;
            InputText = "";

            // Echo the input to the output
            AppendOutput($"> {input}\n");

            // Send to the running process
            await _debugService.SendInputAsync(input);
        }
    }
}

/// <summary>
/// Event args for navigating to a source file location from the output panel.
/// </summary>
public class OutputLineNavigationEventArgs : EventArgs
{
    public string FilePath { get; }
    public int Line { get; }
    public int Column { get; }

    public OutputLineNavigationEventArgs(string filePath, int line, int column)
    {
        FilePath = filePath;
        Line = line;
        Column = column;
    }
}
