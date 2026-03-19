using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Panels;

/// <summary>
/// Represents a single terminal session (shell process).
/// </summary>
public partial class TerminalSession : ObservableObject, IDisposable
{
    private Process? _shellProcess;
    private StreamWriter? _shellInput;
    private readonly StringBuilder _outputBuffer = new();
    private readonly object _lock = new();
    private static int _nextId = 1;

    /// <summary>
    /// Raised when new output text is appended (carries the raw text with ANSI codes).
    /// </summary>
    public event Action<string>? OutputAppended;

    /// <summary>
    /// Raised when the output buffer is cleared.
    /// </summary>
    public event Action? OutputCleared;

    [ObservableProperty]
    private string _outputText = "";

    [ObservableProperty]
    private string _shellName = "Terminal";

    [ObservableProperty]
    private bool _isRunning;

    public int Id { get; }

    public string TabTitle => IsRunning ? ShellName : $"{ShellName} (stopped)";

    public TerminalSession()
    {
        Id = _nextId++;
    }

    public void Start(string workingDirectory)
    {
        if (IsRunning) return;

        try
        {
            var shell = Environment.OSVersion.Platform == PlatformID.Win32NT
                ? "powershell.exe"
                : "/bin/bash";

            var shellDisplayName = Environment.OSVersion.Platform == PlatformID.Win32NT
                ? "PowerShell"
                : "bash";

            var startInfo = new ProcessStartInfo
            {
                FileName = shell,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            _shellProcess = new Process { StartInfo = startInfo };
            _shellProcess.OutputDataReceived += OnOutputDataReceived;
            _shellProcess.ErrorDataReceived += OnErrorDataReceived;
            _shellProcess.Exited += OnProcessExited;
            _shellProcess.EnableRaisingEvents = true;

            _shellProcess.Start();
            _shellInput = _shellProcess.StandardInput;
            _shellProcess.BeginOutputReadLine();
            _shellProcess.BeginErrorReadLine();

            IsRunning = true;
            ShellName = shellDisplayName;
            OnPropertyChanged(nameof(TabTitle));

            AppendOutput($"Terminal started in {workingDirectory}\r\n\r\n");
        }
        catch (Exception ex)
        {
            AppendOutput($"Failed to start terminal: {ex.Message}\r\n");
        }
    }

    public void Stop()
    {
        if (!IsRunning) return;

        try
        {
            _shellInput?.Close();
            if (_shellProcess != null)
            {
                _shellProcess.OutputDataReceived -= OnOutputDataReceived;
                _shellProcess.ErrorDataReceived -= OnErrorDataReceived;
                _shellProcess.Exited -= OnProcessExited;
                _shellProcess.Kill();
                _shellProcess.Dispose();
            }
        }
        catch (Exception)
        {
        }
        finally
        {
            _shellProcess = null;
            _shellInput = null;
            IsRunning = false;
            OnPropertyChanged(nameof(TabTitle));
            AppendOutput("\r\n[Terminal closed]\r\n");
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _outputBuffer.Clear();
            OutputText = "";
            OutputCleared?.Invoke();
        }
    }

    public void SendCommand(string command)
    {
        if (!IsRunning || _shellInput == null) return;

        try
        {
            _shellInput.WriteLine(command);
            _shellInput.Flush();
        }
        catch (Exception ex)
        {
            AppendOutput($"Error: {ex.Message}\r\n");
        }
    }

    public void SetWorkingDirectory(string path)
    {
        if (IsRunning && Directory.Exists(path))
        {
            SendCommand($"cd \"{path}\"");
        }
    }

    private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (e.Data != null)
        {
            AppendOutput(e.Data + "\r\n");
        }
    }

    private void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (e.Data != null)
        {
            AppendOutput(e.Data + "\r\n");
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            IsRunning = false;
            OnPropertyChanged(nameof(TabTitle));
            AppendOutput("\r\n[Process exited]\r\n");
        });
    }

    private void AppendOutput(string text)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            lock (_lock)
            {
                _outputBuffer.Append(text);

                // Limit buffer size to prevent memory issues (truncate at line boundary)
                bool truncated = false;
                if (_outputBuffer.Length > 100000)
                {
                    var excess = _outputBuffer.Length - 80000;
                    var newlineIdx = _outputBuffer.ToString().IndexOf('\n', excess);
                    var removeCount = newlineIdx >= 0 ? newlineIdx + 1 : excess;
                    _outputBuffer.Remove(0, removeCount);
                    truncated = true;
                }

                OutputText = _outputBuffer.ToString();

                if (truncated)
                {
                    // Buffer was truncated - rebuild inlines from full buffer
                    OutputCleared?.Invoke();
                    OutputAppended?.Invoke(OutputText);
                }
                else
                {
                    OutputAppended?.Invoke(text);
                }
            }
        });
    }

    public void Dispose()
    {
        if (IsRunning)
        {
            Stop();
        }
    }
}

/// <summary>
/// Multi-tab terminal view model that manages multiple terminal sessions.
/// </summary>
public partial class TerminalViewModel : ViewModelBase, IDisposable
{
    /// <summary>
    /// Raised when new output text is appended on the active session.
    /// </summary>
    public event Action<string>? OutputAppended;

    /// <summary>
    /// Raised when the active session's output buffer is cleared.
    /// </summary>
    public event Action? OutputCleared;

    [ObservableProperty]
    private string _inputText = "";

    [ObservableProperty]
    private string _workingDirectory = "";

    [ObservableProperty]
    private string _title = "Terminal";

    [ObservableProperty]
    private TerminalSession? _activeSession;

    [ObservableProperty]
    private string _outputText = "";

    [ObservableProperty]
    private bool _isRunning;

    public ObservableCollection<TerminalSession> Sessions { get; } = new();

    /// <summary>
    /// Raised when the active session changes so the view can update scroll, etc.
    /// </summary>
    public event EventHandler? ActiveSessionSwitched;

    public TerminalViewModel()
    {
        WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    public void SetWorkingDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            WorkingDirectory = path;
            ActiveSession?.SetWorkingDirectory(path);
        }
    }

    partial void OnActiveSessionChanging(TerminalSession? oldValue, TerminalSession? newValue)
    {
        if (oldValue != null)
        {
            oldValue.PropertyChanged -= OnSessionPropertyChanged;
            oldValue.OutputAppended -= OnActiveOutputAppended;
            oldValue.OutputCleared -= OnActiveOutputCleared;
        }
    }

    partial void OnActiveSessionChanged(TerminalSession? value)
    {
        if (value != null)
        {
            value.PropertyChanged += OnSessionPropertyChanged;
            value.OutputAppended += OnActiveOutputAppended;
            value.OutputCleared += OnActiveOutputCleared;
            OutputText = value.OutputText;
            IsRunning = value.IsRunning;
            Title = $"Terminal - {value.ShellName}";
            // Rebuild output from active session
            OutputCleared?.Invoke();
            if (!string.IsNullOrEmpty(value.OutputText))
            {
                OutputAppended?.Invoke(value.OutputText);
            }
        }
        else
        {
            OutputText = "";
            IsRunning = false;
            Title = "Terminal";
            OutputCleared?.Invoke();
        }
        ActiveSessionSwitched?.Invoke(this, EventArgs.Empty);
    }

    private void OnActiveOutputAppended(string text)
    {
        OutputText = ActiveSession?.OutputText ?? "";
        OutputAppended?.Invoke(text);
    }

    private void OnActiveOutputCleared()
    {
        OutputText = "";
        OutputCleared?.Invoke();
    }

    private void OnSessionPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender != ActiveSession) return;

        if (e.PropertyName == nameof(TerminalSession.IsRunning))
        {
            IsRunning = ActiveSession!.IsRunning;
        }
        else if (e.PropertyName == nameof(TerminalSession.TabTitle))
        {
            // Notify UI to refresh tab display
        }
    }

    /// <summary>
    /// Creates a new terminal session and starts it.
    /// </summary>
    [RelayCommand]
    private void CreateNewSession()
    {
        var session = new TerminalSession();
        Sessions.Add(session);
        ActiveSession = session;
        session.Start(WorkingDirectory);
    }

    /// <summary>
    /// Start command - creates a new session if none exists, or starts the active one.
    /// </summary>
    [RelayCommand]
    private void Start()
    {
        if (Sessions.Count == 0 || ActiveSession == null)
        {
            CreateNewSession();
        }
        else if (!ActiveSession.IsRunning)
        {
            ActiveSession.Start(WorkingDirectory);
        }
    }

    [RelayCommand]
    private void Stop()
    {
        ActiveSession?.Stop();
    }

    [RelayCommand]
    private void Clear()
    {
        ActiveSession?.Clear();
    }

    [RelayCommand]
    private void SendInput()
    {
        if (string.IsNullOrEmpty(InputText) || ActiveSession == null) return;

        ActiveSession.SendCommand(InputText);
        InputText = "";
    }

    [RelayCommand]
    private void CloseSession(TerminalSession? session)
    {
        if (session == null) return;

        session.Dispose();
        var idx = Sessions.IndexOf(session);
        Sessions.Remove(session);

        if (ActiveSession == session || ActiveSession == null)
        {
            if (Sessions.Count > 0)
            {
                // Select the nearest remaining tab
                var newIdx = Math.Min(idx, Sessions.Count - 1);
                ActiveSession = Sessions[newIdx];
            }
            else
            {
                ActiveSession = null;
            }
        }

        if (Sessions.Count == 0)
        {
            Title = "Terminal";
            IsRunning = false;
            OutputText = "";
        }
    }

    /// <summary>
    /// Sends a command to the active terminal session.
    /// </summary>
    public void SendCommand(string command)
    {
        ActiveSession?.SendCommand(command);
    }

    public void Dispose()
    {
        foreach (var session in Sessions)
        {
            session.Dispose();
        }
        Sessions.Clear();
    }
}
