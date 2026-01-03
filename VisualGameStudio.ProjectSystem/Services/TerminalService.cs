using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// Provides integrated terminal/console functionality.
/// </summary>
public class TerminalService : ITerminalService
{
    private readonly ConcurrentDictionary<string, TerminalSession> _sessions = new();
    private readonly ConcurrentDictionary<string, Process> _processes = new();
    private readonly ConcurrentDictionary<string, List<TerminalOutput>> _history = new();
    private string? _activeSessionId;
    private bool _disposed;

    /// <inheritdoc/>
    public IReadOnlyList<TerminalSession> Sessions => _sessions.Values.ToList();

    /// <inheritdoc/>
    public TerminalSession? ActiveSession => _activeSessionId != null && _sessions.TryGetValue(_activeSessionId, out var session) ? session : null;

    /// <inheritdoc/>
    public event EventHandler<TerminalOutputEventArgs>? OutputReceived;

    /// <inheritdoc/>
    public event EventHandler<TerminalSessionEventArgs>? SessionCreated;

    /// <inheritdoc/>
    public event EventHandler<TerminalSessionEventArgs>? SessionClosed;

    /// <inheritdoc/>
    public event EventHandler<TerminalSessionEventArgs>? ActiveSessionChanged;

    /// <inheritdoc/>
    public event EventHandler<CommandCompletedEventArgs>? CommandCompleted;

    /// <inheritdoc/>
    public TerminalSession CreateSession(TerminalOptions? options = null)
    {
        options ??= new TerminalOptions();

        var shell = options.Shell ?? GetDefaultShell();
        var workingDir = options.WorkingDirectory ?? Environment.CurrentDirectory;

        var session = new TerminalSession
        {
            Name = options.Name ?? $"Terminal {_sessions.Count + 1}",
            Shell = shell,
            WorkingDirectory = workingDir,
            IsRunning = true
        };

        var startInfo = new ProcessStartInfo
        {
            FileName = shell,
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (options.EnvironmentVariables != null)
        {
            foreach (var kvp in options.EnvironmentVariables)
            {
                startInfo.Environment[kvp.Key] = kvp.Value;
            }
        }

        try
        {
            var process = new Process { StartInfo = startInfo };
            process.OutputDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    OnOutputReceived(session.Id, e.Data, TerminalOutputType.StandardOutput);
                }
            };
            process.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    OnOutputReceived(session.Id, e.Data, TerminalOutputType.StandardError);
                }
            };
            process.Exited += (s, e) =>
            {
                session.IsRunning = false;
                session.LastExitCode = process.ExitCode;
            };
            process.EnableRaisingEvents = true;

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            _processes[session.Id] = process;
        }
        catch (Exception ex)
        {
            OnOutputReceived(session.Id, $"Failed to start shell: {ex.Message}", TerminalOutputType.System);
            session.IsRunning = false;
        }

        _sessions[session.Id] = session;
        _history[session.Id] = new List<TerminalOutput>();

        if (_activeSessionId == null)
        {
            _activeSessionId = session.Id;
        }

        SessionCreated?.Invoke(this, new TerminalSessionEventArgs(session));

        return session;
    }

    /// <inheritdoc/>
    public void CloseSession(string sessionId)
    {
        if (_sessions.TryRemove(sessionId, out var session))
        {
            try
            {
                if (_processes.TryRemove(sessionId, out var process) && !process.HasExited)
                {
                    process.Kill(true);
                    process.Dispose();
                }
            }
            catch
            {
                // Ignore errors during cleanup
            }

            session.IsRunning = false;
            _history.TryRemove(sessionId, out _);

            if (_activeSessionId == sessionId)
            {
                _activeSessionId = _sessions.Keys.FirstOrDefault();
                if (_activeSessionId != null && _sessions.TryGetValue(_activeSessionId, out var newActive))
                {
                    ActiveSessionChanged?.Invoke(this, new TerminalSessionEventArgs(newActive));
                }
            }

            SessionClosed?.Invoke(this, new TerminalSessionEventArgs(session));
        }
    }

    /// <inheritdoc/>
    public void SetActiveSession(string sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            _activeSessionId = sessionId;
            ActiveSessionChanged?.Invoke(this, new TerminalSessionEventArgs(session));
        }
    }

    /// <inheritdoc/>
    public void SendInput(string input)
    {
        if (_activeSessionId != null)
        {
            SendInput(_activeSessionId, input);
        }
    }

    /// <inheritdoc/>
    public void SendInput(string sessionId, string input)
    {
        if (_sessions.TryGetValue(sessionId, out var session) &&
            _processes.TryGetValue(sessionId, out var process) && !process.HasExited)
        {
            try
            {
                process.StandardInput.WriteLine(input);
                session.CurrentCommand = input;
                OnOutputReceived(sessionId, input, TerminalOutputType.Input);
            }
            catch
            {
                // Process may have exited
            }
        }
    }

    /// <inheritdoc/>
    public async Task<CommandResult> ExecuteCommandAsync(string command, string? workingDirectory = null, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.Now;
        var shell = GetDefaultShell();
        var shellArgs = GetShellArguments(shell, command);

        var startInfo = new ProcessStartInfo
        {
            FileName = shell,
            Arguments = shellArgs,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        var stdout = new List<string>();
        var stderr = new List<string>();

        process.OutputDataReceived += (s, e) =>
        {
            if (e.Data != null) stdout.Add(e.Data);
        };
        process.ErrorDataReceived += (s, e) =>
        {
            if (e.Data != null) stderr.Add(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            throw;
        }

        return new CommandResult
        {
            Command = command,
            ExitCode = process.ExitCode,
            StandardOutput = string.Join(Environment.NewLine, stdout),
            StandardError = string.Join(Environment.NewLine, stderr),
            StartTime = startTime,
            Duration = DateTime.Now - startTime
        };
    }

    /// <inheritdoc/>
    public TerminalSession ExecuteInBackground(string command, string? workingDirectory = null)
    {
        var session = CreateSession(new TerminalOptions
        {
            WorkingDirectory = workingDirectory,
            Name = $"Command: {command.Substring(0, Math.Min(20, command.Length))}..."
        });

        SendInput(session.Id, command);

        return session;
    }

    /// <inheritdoc/>
    public void Clear(string? sessionId = null)
    {
        var id = sessionId ?? _activeSessionId;
        if (id != null && _history.TryGetValue(id, out var history))
        {
            history.Clear();
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<TerminalOutput> GetHistory(string sessionId)
    {
        return _history.TryGetValue(sessionId, out var history) ? history.ToList() : Array.Empty<TerminalOutput>();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var sessionId in _sessions.Keys.ToList())
        {
            CloseSession(sessionId);
        }
    }

    private void OnOutputReceived(string sessionId, string text, TerminalOutputType type)
    {
        var output = new TerminalOutput
        {
            Text = text,
            Type = type,
            Timestamp = DateTime.Now
        };

        if (_history.TryGetValue(sessionId, out var history))
        {
            history.Add(output);

            // Limit history size
            while (history.Count > 10000)
            {
                history.RemoveAt(0);
            }
        }

        OutputReceived?.Invoke(this, new TerminalOutputEventArgs(sessionId, output));
    }

    private static string GetDefaultShell()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Prefer PowerShell if available
            var pwsh = Environment.GetEnvironmentVariable("PWSH_PATH");
            if (!string.IsNullOrEmpty(pwsh) && File.Exists(pwsh))
                return pwsh;

            return "cmd.exe";
        }

        // Unix-like systems
        var shell = Environment.GetEnvironmentVariable("SHELL");
        return !string.IsNullOrEmpty(shell) ? shell : "/bin/bash";
    }

    private static string GetShellArguments(string shell, string command)
    {
        var shellName = Path.GetFileNameWithoutExtension(shell).ToLowerInvariant();

        return shellName switch
        {
            "cmd" => $"/c {command}",
            "powershell" or "pwsh" => $"-Command {command}",
            _ => $"-c \"{command.Replace("\"", "\\\"")}\""
        };
    }
}
