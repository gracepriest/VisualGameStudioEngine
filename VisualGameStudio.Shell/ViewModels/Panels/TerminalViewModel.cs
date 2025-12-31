using System.Diagnostics;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Panels;

public partial class TerminalViewModel : ViewModelBase
{
    private Process? _shellProcess;
    private StreamWriter? _shellInput;
    private readonly StringBuilder _outputBuffer = new();
    private readonly object _lock = new();

    [ObservableProperty]
    private string _outputText = "";

    [ObservableProperty]
    private string _inputText = "";

    [ObservableProperty]
    private string _workingDirectory = "";

    [ObservableProperty]
    private string _title = "Terminal";

    [ObservableProperty]
    private bool _isRunning;

    public TerminalViewModel()
    {
        WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    public void SetWorkingDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            WorkingDirectory = path;
            if (IsRunning)
            {
                SendCommand($"cd \"{path}\"");
            }
        }
    }

    [RelayCommand]
    private void Start()
    {
        if (IsRunning) return;

        try
        {
            var shell = Environment.OSVersion.Platform == PlatformID.Win32NT
                ? "cmd.exe"
                : "/bin/bash";

            var startInfo = new ProcessStartInfo
            {
                FileName = shell,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = WorkingDirectory,
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
            Title = $"Terminal - {(Environment.OSVersion.Platform == PlatformID.Win32NT ? "cmd" : "bash")}";

            AppendOutput($"Terminal started in {WorkingDirectory}\r\n\r\n");
        }
        catch (Exception ex)
        {
            AppendOutput($"Failed to start terminal: {ex.Message}\r\n");
        }
    }

    [RelayCommand]
    private void Stop()
    {
        if (!IsRunning) return;

        try
        {
            _shellInput?.Close();
            _shellProcess?.Kill();
            _shellProcess?.Dispose();
        }
        catch
        {
        }
        finally
        {
            _shellProcess = null;
            _shellInput = null;
            IsRunning = false;
            Title = "Terminal";
            AppendOutput("\r\n[Terminal closed]\r\n");
        }
    }

    [RelayCommand]
    private void Clear()
    {
        lock (_lock)
        {
            _outputBuffer.Clear();
            OutputText = "";
        }
    }

    [RelayCommand]
    private void SendInput()
    {
        if (string.IsNullOrEmpty(InputText)) return;

        SendCommand(InputText);
        InputText = "";
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
            Title = "Terminal";
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

                // Limit buffer size to prevent memory issues
                if (_outputBuffer.Length > 100000)
                {
                    _outputBuffer.Remove(0, _outputBuffer.Length - 80000);
                }

                OutputText = _outputBuffer.ToString();
            }
        });
    }
}
