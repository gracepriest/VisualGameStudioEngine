using System.Collections.ObjectModel;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Abstractions.ViewModels;
using VisualGameStudio.ProjectSystem.Services;

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

/// <summary>
/// ViewModel for an individual output channel tab in the dropdown.
/// </summary>
public partial class OutputChannelViewModel : ObservableObject
{
    public string Name { get; }
    public IOutputChannel Channel { get; }

    public OutputChannelViewModel(IOutputChannel channel)
    {
        Channel = channel;
        Name = channel.Name;
    }

    public override string ToString() => Name;
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

    // Pattern for clickable file paths (e.g., C:\path\file.cs:42 or /path/file.cs:42:5)
    private static readonly Regex FilePathPattern = new(
        @"(?:^|\s)([A-Za-z]:[\\/][\w\\/\.\-]+|/[\w/\.\-]+):(\d+)(?::(\d+))?",
        RegexOptions.Compiled);

    [ObservableProperty]
    private string _outputText = "";

    [ObservableProperty]
    private string _inputText = "";

    [ObservableProperty]
    private bool _isInputEnabled;

    [ObservableProperty]
    private bool _wordWrap;

    [ObservableProperty]
    private bool _autoScroll = true;

    [ObservableProperty]
    private int _lineCount;

    /// <summary>
    /// The currently active channel (selected in the dropdown).
    /// </summary>
    [ObservableProperty]
    private OutputChannelViewModel? _activeChannel;

    /// <summary>
    /// All available output channels.
    /// </summary>
    public ObservableCollection<OutputChannelViewModel> Channels { get; } = new();

    /// <summary>
    /// Collection of parsed output lines for the ItemsControl display.
    /// </summary>
    public ObservableCollection<OutputLine> OutputLines { get; } = new();

    /// <summary>
    /// Raised when the user clicks on an error/warning line to navigate to the source location.
    /// </summary>
    public event EventHandler<OutputLineNavigationEventArgs>? NavigateToSourceRequested;

    /// <summary>
    /// Raised when auto-scroll should be triggered (new content added while auto-scroll is on).
    /// </summary>
    public event EventHandler? ScrollToBottomRequested;

    /// <summary>
    /// Raised when the user wants to open the current output in a text editor document.
    /// </summary>
    public event EventHandler<string>? OpenInEditorRequested;

    // === Legacy SelectedCategory property for backward compatibility ===

    /// <summary>
    /// Legacy property: gets/sets the active channel via OutputCategory enum.
    /// Used by MainWindowViewModel for debug/build panel switching.
    /// </summary>
    public OutputCategory SelectedCategory
    {
        get
        {
            if (ActiveChannel == null) return OutputCategory.General;
            var cat = OutputService.GetCategoryForChannel(ActiveChannel.Name);
            return cat ?? OutputCategory.General;
        }
        set
        {
            var channelName = OutputService.GetChannelName(value);
            var channelVm = Channels.FirstOrDefault(c =>
                string.Equals(c.Name, channelName, StringComparison.OrdinalIgnoreCase));
            if (channelVm != null)
            {
                ActiveChannel = channelVm;
            }
        }
    }

    public OutputPanelViewModel(IOutputService outputService, IDebugService debugService)
    {
        _outputService = outputService;
        _debugService = debugService;

        // Subscribe to legacy event for backward compat
        _outputService.OutputReceived += OnOutputReceived;
        _debugService.StateChanged += OnDebugStateChanged;

        // Subscribe to channel events
        _outputService.ChannelCreated += OnChannelCreated;
        _outputService.ActiveChannelChanged += OnActiveChannelChanged;

        // Populate initial channels
        foreach (var channel in _outputService.Channels)
        {
            var vm = new OutputChannelViewModel(channel);
            Channels.Add(vm);
        }

        // Set initial active channel
        if (_outputService.ActiveChannel != null)
        {
            var initial = Channels.FirstOrDefault(c =>
                string.Equals(c.Name, _outputService.ActiveChannel.Name, StringComparison.OrdinalIgnoreCase));
            if (initial != null)
            {
                _activeChannel = initial;
            }
        }
    }

    private void OnChannelCreated(object? sender, string channelName)
    {
        Dispatcher.UIThread.Post(() =>
        {
            // Avoid duplicates
            if (Channels.Any(c => string.Equals(c.Name, channelName, StringComparison.OrdinalIgnoreCase)))
                return;

            var channel = _outputService.GetChannel(channelName);
            if (channel != null)
            {
                Channels.Add(new OutputChannelViewModel(channel));
            }
        });
    }

    private void OnActiveChannelChanged(object? sender, IOutputChannel? channel)
    {
        if (channel == null) return;

        Dispatcher.UIThread.Post(() =>
        {
            var vm = Channels.FirstOrDefault(c =>
                string.Equals(c.Name, channel.Name, StringComparison.OrdinalIgnoreCase));
            if (vm != null && ActiveChannel != vm)
            {
                ActiveChannel = vm;
            }
        });
    }

    private void OnDebugStateChanged(object? sender, DebugStateChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
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
        // The channel-based system handles display now.
        // This event handler is kept for any external code that still
        // directly appends output via the legacy API.
        // The channel's TextAppended event drives the UI refresh.
    }

    partial void OnActiveChannelChanged(OutputChannelViewModel? oldValue, OutputChannelViewModel? newValue)
    {
        // Unsubscribe from old channel events
        if (oldValue != null)
        {
            oldValue.Channel.TextAppended -= OnActiveChannelTextAppended;
            oldValue.Channel.Cleared -= OnActiveChannelCleared;
        }

        // Subscribe to new channel events
        if (newValue != null)
        {
            newValue.Channel.TextAppended += OnActiveChannelTextAppended;
            newValue.Channel.Cleared += OnActiveChannelCleared;
        }

        // Update the service's active channel
        if (newValue != null && _outputService.ActiveChannel != newValue.Channel)
        {
            _outputService.ActiveChannel = newValue.Channel;
        }

        // Refresh the display
        RefreshOutput();

        // Notify legacy SelectedCategory watchers
        OnPropertyChanged(nameof(SelectedCategory));
    }

    private void OnActiveChannelTextAppended(object? sender, string text)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _outputBuffer.Append(text);
            OutputText = _outputBuffer.ToString();
            ParseAndAppendLines(text);
            LineCount = OutputLines.Count;

            if (AutoScroll)
            {
                ScrollToBottomRequested?.Invoke(this, EventArgs.Empty);
            }
        });
    }

    private void OnActiveChannelCleared(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _outputBuffer.Clear();
            OutputText = "";
            OutputLines.Clear();
            LineCount = 0;
        });
    }

    public void AppendOutput(string text)
    {
        // Legacy method: route to the active channel
        ActiveChannel?.Channel.Append(text);
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
                // Check for file path patterns (clickable links)
                var pathMatch = FilePathPattern.Match(line);
                if (pathMatch.Success)
                {
                    var filePath = pathMatch.Groups[1].Value;
                    var lineNum = int.Parse(pathMatch.Groups[2].Value);
                    var colNum = pathMatch.Groups[3].Success ? int.Parse(pathMatch.Groups[3].Value) : 1;
                    OutputLines.Add(new OutputLine(line, filePath, lineNum, colNum, OutputLineSeverity.Normal));
                }
                else
                {
                    OutputLines.Add(new OutputLine(line));
                }
            }
        }
    }

    private void RefreshOutput()
    {
        _outputBuffer.Clear();
        OutputLines.Clear();

        if (ActiveChannel == null)
        {
            OutputText = "";
            LineCount = 0;
            return;
        }

        var content = ActiveChannel.Channel.GetContent();
        _outputBuffer.Append(content);
        OutputText = content;
        ParseAndAppendLines(content);
        LineCount = OutputLines.Count;

        if (AutoScroll)
        {
            ScrollToBottomRequested?.Invoke(this, EventArgs.Empty);
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

    [RelayCommand]
    private void Clear()
    {
        ActiveChannel?.Channel.Clear();

        // Also clear legacy storage for backward compat
        if (ActiveChannel != null)
        {
            var cat = OutputService.GetCategoryForChannel(ActiveChannel.Name);
            if (cat.HasValue)
            {
                _outputService.Clear(cat.Value);
            }
        }
    }

    [RelayCommand]
    private void CopyAll()
    {
        if (ActiveChannel == null) return;
        var content = ActiveChannel.Channel.GetContent();
        if (!string.IsNullOrEmpty(content))
        {
            var mainWindow = (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            var clipboard = mainWindow != null ? Avalonia.Controls.TopLevel.GetTopLevel(mainWindow)?.Clipboard : null;
            clipboard?.SetTextAsync(content);
        }
    }

    [RelayCommand]
    private void ToggleWordWrap()
    {
        WordWrap = !WordWrap;
    }

    [RelayCommand]
    private void ToggleAutoScroll()
    {
        AutoScroll = !AutoScroll;
        if (AutoScroll)
        {
            ScrollToBottomRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    private void LockScroll()
    {
        AutoScroll = false;
    }

    [RelayCommand]
    private void OpenInEditor()
    {
        if (ActiveChannel == null) return;
        var content = ActiveChannel.Channel.GetContent();
        if (!string.IsNullOrEmpty(content))
        {
            OpenInEditorRequested?.Invoke(this, content);
        }
    }

    // === Legacy commands (kept for backward compat) ===

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
