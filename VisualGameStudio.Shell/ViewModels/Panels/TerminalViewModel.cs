using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Abstractions.ViewModels;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Shell.ViewModels.Panels;

/// <summary>
/// Indicates which pane is currently focused in a split terminal layout.
/// </summary>
public enum SplitPaneFocus
{
    Left,
    Right
}

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

    /// <summary>Maximum characters in scroll-back buffer (~10,000 lines at ~80 chars).</summary>
    private const int MaxBufferChars = 800_000;
    /// <summary>Target size after truncation.</summary>
    private const int TruncateTargetChars = 640_000;

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

    /// <summary>
    /// Tracked current working directory of the shell session.
    /// Updated by detecting cd commands and prompt patterns.
    /// </summary>
    [ObservableProperty]
    private string _currentDirectory = "";

    /// <summary>
    /// Exit code of the last command that completed in this session.
    /// Null if no command has completed yet.
    /// </summary>
    [ObservableProperty]
    private int? _lastExitCode;

    /// <summary>
    /// History of commands entered in this session.
    /// </summary>
    public List<string> CommandHistory { get; } = new();

    /// <summary>
    /// Current position in command history for up/down arrow navigation.
    /// </summary>
    public int HistoryIndex { get; set; } = -1;

    /// <summary>
    /// Environment variables to set for the shell process.
    /// Must be set before calling Start().
    /// </summary>
    public Dictionary<string, string>? EnvironmentVariables { get; set; }

    public int Id { get; }

    public string TabTitle => IsRunning ? ShellName : $"{ShellName} (stopped)";

    public TerminalSession()
    {
        Id = _nextId++;
    }

    /// <summary>
    /// Start the terminal session using a specific shell profile.
    /// </summary>
    public void Start(string workingDirectory, ShellProfile? profile = null)
    {
        if (IsRunning) return;

        try
        {
            string shell;
            string shellDisplayName;
            string arguments = "";

            if (profile != null)
            {
                shell = profile.Path;
                shellDisplayName = profile.Name;
                arguments = profile.Arguments;
            }
            else
            {
                shell = Environment.OSVersion.Platform == PlatformID.Win32NT
                    ? "powershell.exe"
                    : "/bin/bash";

                shellDisplayName = Environment.OSVersion.Platform == PlatformID.Win32NT
                    ? "PowerShell"
                    : "bash";
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = shell,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            // Apply custom environment variables
            if (EnvironmentVariables != null)
            {
                foreach (var kvp in EnvironmentVariables)
                {
                    startInfo.Environment[kvp.Key] = kvp.Value;
                }
            }

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
            CurrentDirectory = workingDirectory;
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

            // Track command history
            if (!string.IsNullOrWhiteSpace(command))
            {
                CommandHistory.Add(command);
                HistoryIndex = CommandHistory.Count; // past the end
            }

            // Track CWD changes from cd commands
            TrackDirectoryChange(command);
        }
        catch (Exception ex)
        {
            AppendOutput($"Error: {ex.Message}\r\n");
        }
    }

    /// <summary>
    /// Sends a SIGINT (Ctrl+C) signal to the shell process to interrupt the current command.
    /// </summary>
    public void SendInterrupt()
    {
        if (!IsRunning || _shellProcess == null) return;

        try
        {
            // On Windows, send Ctrl+C via GenerateConsoleCtrlEvent is not possible
            // for processes with no console. Instead, send Ctrl+C character to stdin.
            _shellInput?.Write("\x03");
            _shellInput?.Flush();
        }
        catch
        {
            // Ignore - process may have exited
        }
    }

    /// <summary>
    /// Detects directory changes from common cd commands and updates CurrentDirectory.
    /// </summary>
    private void TrackDirectoryChange(string command)
    {
        var trimmed = command.Trim();

        // Match: cd path, cd "path", Set-Location path, pushd path
        string? newDir = null;

        if (trimmed.StartsWith("cd ", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("cd\t", StringComparison.OrdinalIgnoreCase))
        {
            newDir = trimmed.Substring(3).Trim().Trim('"', '\'');
        }
        else if (trimmed.StartsWith("Set-Location ", StringComparison.OrdinalIgnoreCase))
        {
            newDir = trimmed.Substring(13).Trim().Trim('"', '\'');
        }
        else if (trimmed.StartsWith("pushd ", StringComparison.OrdinalIgnoreCase))
        {
            newDir = trimmed.Substring(6).Trim().Trim('"', '\'');
        }
        else if (trimmed.Equals("cd", StringComparison.OrdinalIgnoreCase) ||
                 trimmed.Equals("cd ~", StringComparison.OrdinalIgnoreCase) ||
                 trimmed.Equals("cd ~\\", StringComparison.OrdinalIgnoreCase) ||
                 trimmed.Equals("cd ~/", StringComparison.OrdinalIgnoreCase))
        {
            newDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        else if (trimmed.Equals("cd ..", StringComparison.OrdinalIgnoreCase) ||
                 trimmed.Equals("cd..", StringComparison.OrdinalIgnoreCase))
        {
            var parent = Directory.GetParent(CurrentDirectory);
            if (parent != null)
                newDir = parent.FullName;
        }

        if (newDir == null) return;

        try
        {
            string resolved;
            if (Path.IsPathRooted(newDir))
            {
                resolved = Path.GetFullPath(newDir);
            }
            else
            {
                resolved = Path.GetFullPath(Path.Combine(CurrentDirectory, newDir));
            }

            if (Directory.Exists(resolved))
            {
                CurrentDirectory = resolved;
            }
        }
        catch
        {
            // Invalid path - ignore
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

                // Limit buffer size to ~10,000 lines (truncate at line boundary)
                bool truncated = false;
                if (_outputBuffer.Length > MaxBufferChars)
                {
                    var excess = _outputBuffer.Length - TruncateTargetChars;
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
    private static readonly string SettingsFilePath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VisualGameStudio",
        "settings.json");

    /// <summary>
    /// The single live settings store (Task 2.2). Null in design-time / lightweight test
    /// construction, in which case the terminal falls back to its built-in font defaults and does
    /// no persistence.
    /// </summary>
    private readonly ISettingsService? _settingsService;

    /// <summary>
    /// Guards <see cref="OnSelectedProfileChanged"/> so that assigning <see cref="SelectedProfile"/>
    /// from settings during load does not write the value straight back to settings.
    /// </summary>
    private bool _suppressProfilePersist;

    static TerminalViewModel()
    {
        // Task 2.2 — name the terminal consumers so the Phase 3 settings-consumer contract test can
        // prove these dialog settings are actually wired (registered at type initialization, like
        // CodeEditorDocumentView). fontFamily/fontSize feed the output + input font; defaultProfile
        // feeds the shell chosen for NEW sessions.
        SettingsConsumerRegistry.RegisterConsumer("terminal.integrated.fontFamily", "TerminalViewModel.ApplyTerminalFontSettings → output/input font family (falls back to editor.fontFamily)");
        SettingsConsumerRegistry.RegisterConsumer("terminal.integrated.fontSize", "TerminalViewModel.ApplyTerminalFontSettings → output/input font size");
        SettingsConsumerRegistry.RegisterConsumer("terminal.integrated.defaultProfile", "TerminalViewModel.LoadDefaultShellPreference → default shell profile for new sessions");
    }

    /// <summary>
    /// Effective terminal font family, bound by the view onto the output <c>SelectableTextBlock</c>
    /// and the input <c>TextBox</c>. Fed from <c>terminal.integrated.fontFamily</c> (falling back to
    /// the editor font, then a built-in monospace stack). Live-updates on <see cref="OnSettingChanged"/>.
    /// </summary>
    [ObservableProperty]
    private string _terminalFontFamily = "Cascadia Code, Consolas, monospace";

    /// <summary>
    /// Effective terminal font size (points), bound onto the same output/input surfaces. Fed from
    /// <c>terminal.integrated.fontSize</c> (schema default 14 — the old hardcoded 13 didn't even
    /// match it). Live-updates on <see cref="OnSettingChanged"/>.
    /// </summary>
    [ObservableProperty]
    private double _terminalFontSize = 14;

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

    [ObservableProperty]
    private ShellProfile? _selectedProfile;

    [ObservableProperty]
    private bool _isProfileDropdownOpen;

    /// <summary>
    /// All detected shell profiles available on this machine.
    /// </summary>
    public ObservableCollection<ShellProfile> AvailableProfiles { get; } = new();

    public ObservableCollection<TerminalSession> Sessions { get; } = new();

    /// <summary>
    /// Whether the terminal area is currently split into two panes.
    /// </summary>
    [ObservableProperty]
    private bool _isSplit;

    /// <summary>
    /// The session shown in the right (secondary) split pane.
    /// When not split, this is null.
    /// </summary>
    [ObservableProperty]
    private TerminalSession? _splitSession;

    /// <summary>
    /// Tracks which pane is currently focused: Left or Right.
    /// Commands go to the focused pane's session.
    /// </summary>
    [ObservableProperty]
    private SplitPaneFocus _focusedPane = SplitPaneFocus.Left;

    /// <summary>
    /// Raised when the active session changes so the view can update scroll, etc.
    /// </summary>
    public event EventHandler? ActiveSessionSwitched;

    /// <summary>
    /// Raised when the split session output is appended.
    /// </summary>
    public event Action<string>? SplitOutputAppended;

    /// <summary>
    /// Raised when the split session output is cleared.
    /// </summary>
    public event Action? SplitOutputCleared;

    /// <summary>
    /// Raised when the split state changes (split/unsplit).
    /// </summary>
    public event EventHandler? SplitStateChanged;

    /// <summary>
    /// Raised when the user clicks a file path link in terminal output.
    /// Parameters: filePath, line, column.
    /// </summary>
    public event Action<string, int, int>? FileNavigationRequested;

    /// <summary>
    /// Environment variables to apply to new terminal sessions.
    /// Loaded from workspace settings (terminal.integrated.env.windows).
    /// </summary>
    public Dictionary<string, string> TerminalEnvironmentVariables { get; } = new();

    /// <summary>
    /// Raised when the current working directory of the focused session changes.
    /// </summary>
    public event Action<string>? CurrentDirectoryChanged;

    public TerminalViewModel(ISettingsService? settingsService = null)
    {
        _settingsService = settingsService;

        WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        DetectShellProfiles();
        ApplyTerminalFontSettings();
        LoadDefaultShellPreference();
        LoadTerminalEnvironmentVariables();

        // Live-update the font + default profile when the Settings dialog (or an external edit to
        // ~/.vgs/settings.json via the file watcher) changes the relevant keys.
        if (_settingsService != null)
            _settingsService.SettingChanged += OnSettingChanged;
    }

    /// <summary>
    /// Applies the effective terminal font (family + size) from settings onto the bound VM
    /// properties. Called at construction and on every relevant <see cref="OnSettingChanged"/>.
    /// </summary>
    private void ApplyTerminalFontSettings()
    {
        var (family, size) = ResolveTerminalFont(_settingsService);
        TerminalFontFamily = family;
        TerminalFontSize = size;
    }

    /// <summary>
    /// Resolves the effective terminal font (family + size) from settings. Static + pure so it can
    /// be unit-tested without an Avalonia app (repo static-seam precedent). The family inherits the
    /// editor font when <c>terminal.integrated.fontFamily</c> is empty (VS Code behavior), then a
    /// built-in monospace stack; the size clamps to the schema's 6..72 range (default 14).
    /// </summary>
    public static (string family, double size) ResolveTerminalFont(ISettingsService? settings)
    {
        if (settings == null) return ("Cascadia Code, Consolas, monospace", 14);

        var family = settings.Get("terminal.integrated.fontFamily", "");
        if (string.IsNullOrWhiteSpace(family))
            family = settings.Get("editor.fontFamily", "");
        if (string.IsNullOrWhiteSpace(family))
            family = "Cascadia Code, Consolas, monospace";

        var size = settings.Get("terminal.integrated.fontSize", 14);
        if (size < 6) size = 6;
        else if (size > 72) size = 72;

        return (family, size);
    }

    /// <summary>
    /// Reads the saved default-shell name from settings, honoring the VS Code key precedence:
    /// the platform-specific key (<c>terminal.integrated.defaultProfile.windows</c>/<c>.linux</c>)
    /// wins, then the generic <c>terminal.integrated.defaultProfile</c>, then the legacy
    /// <c>Terminal.DefaultShell</c> / <c>Terminal.Shell</c> keys. Static + pure for headless
    /// testing. Returns "" when nothing is saved.
    /// </summary>
    public static string ResolveSavedShellName(ISettingsService? settings)
    {
        if (settings == null) return "";

        var platformKey = Environment.OSVersion.Platform == PlatformID.Win32NT
            ? "terminal.integrated.defaultProfile.windows"
            : "terminal.integrated.defaultProfile.linux";

        var saved = settings.Get(platformKey, "");
        if (string.IsNullOrEmpty(saved)) saved = settings.Get("terminal.integrated.defaultProfile", "");
        if (string.IsNullOrEmpty(saved)) saved = settings.Get("Terminal.DefaultShell", "");
        if (string.IsNullOrEmpty(saved)) saved = settings.Get("Terminal.Shell", "");
        return saved ?? "";
    }

    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        switch (e.Key)
        {
            case "terminal.integrated.fontFamily":
            case "terminal.integrated.fontSize":
            case "editor.fontFamily": // the terminal font falls back to the editor font
                RunOnUiThread(ApplyTerminalFontSettings);
                break;
            case "terminal.integrated.defaultProfile":
            case "terminal.integrated.defaultProfile.windows":
            case "terminal.integrated.defaultProfile.linux":
                // Re-resolve the default for NEW sessions only — never disturb running shells.
                RunOnUiThread(LoadDefaultShellPreference);
                break;
        }
    }

    /// <summary>
    /// Runs <paramref name="action"/> on the UI thread. The SettingChanged event can arrive from the
    /// settings file watcher (a background thread), and these actions touch bound VM properties.
    /// </summary>
    private static void RunOnUiThread(Action action)
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            action();
        else
            Avalonia.Threading.Dispatcher.UIThread.Post(action);
    }

    /// <summary>
    /// Detects all available shells and populates AvailableProfiles.
    /// </summary>
    private void DetectShellProfiles()
    {
        var detected = ShellProfileDetector.DetectProfiles();
        AvailableProfiles.Clear();
        foreach (var profile in detected)
        {
            AvailableProfiles.Add(profile);
        }
    }

    /// <summary>
    /// Loads the user's default shell preference from the single live settings store (Task 2.2 —
    /// was raw %APPDATA% JSON before). Honors the VS Code key precedence
    /// (platform-specific → generic → legacy) via <see cref="ResolveSavedShellName"/> and matches it
    /// to an available profile by name, then by path. Falls back to the first available profile.
    /// Assigning <see cref="SelectedProfile"/> here is guarded so it never writes straight back.
    /// </summary>
    private void LoadDefaultShellPreference()
    {
        var savedShell = ResolveSavedShellName(_settingsService);

        ShellProfile? resolved = null;
        if (!string.IsNullOrEmpty(savedShell))
        {
            // Try to match by name first, then by path.
            resolved = AvailableProfiles.FirstOrDefault(p =>
                string.Equals(p.Name, savedShell, StringComparison.OrdinalIgnoreCase))
                ?? AvailableProfiles.FirstOrDefault(p =>
                    string.Equals(p.Path, savedShell, StringComparison.OrdinalIgnoreCase));
        }

        // Fall back to first profile.
        resolved ??= AvailableProfiles.FirstOrDefault();

        // Assigning from settings must not persist back (would be a redundant write, and on the
        // file-watcher re-entry path could stomp a just-made choice).
        _suppressProfilePersist = true;
        try { SelectedProfile = resolved; }
        finally { _suppressProfilePersist = false; }
    }

    /// <summary>
    /// Saves the default shell preference to the single live settings store (Task 2.2). Writes both
    /// the VS Code platform-specific key (which takes precedence on read) and the generic key.
    /// </summary>
    private void SaveDefaultShellPreference()
    {
        if (SelectedProfile == null || _settingsService == null) return;

        var platformKey = Environment.OSVersion.Platform == PlatformID.Win32NT
            ? "terminal.integrated.defaultProfile.windows"
            : "terminal.integrated.defaultProfile.linux";

        _settingsService.Set(platformKey, SelectedProfile.Name);
        _settingsService.Set("terminal.integrated.defaultProfile", SelectedProfile.Name);
    }

    /// <summary>
    /// Loads terminal environment variables from settings (terminal.integrated.env.windows).
    /// </summary>
    private void LoadTerminalEnvironmentVariables()
    {
        try
        {
            if (!File.Exists(SettingsFilePath)) return;

            var json = File.ReadAllText(SettingsFilePath);
            var doc = System.Text.Json.JsonDocument.Parse(json);

            // Look for terminal.integrated.env.windows (or platform-appropriate key)
            var envKey = Environment.OSVersion.Platform == PlatformID.Win32NT
                ? "Terminal.Env.Windows"
                : "Terminal.Env.Linux";

            if (doc.RootElement.TryGetProperty(envKey, out var envProp) &&
                envProp.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                foreach (var prop in envProp.EnumerateObject())
                {
                    TerminalEnvironmentVariables[prop.Name] = prop.Value.GetString() ?? "";
                }
            }

            // Also check generic key
            if (doc.RootElement.TryGetProperty("Terminal.Env", out var genericEnv) &&
                genericEnv.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                foreach (var prop in genericEnv.EnumerateObject())
                {
                    if (!TerminalEnvironmentVariables.ContainsKey(prop.Name))
                        TerminalEnvironmentVariables[prop.Name] = prop.Value.GetString() ?? "";
                }
            }
        }
        catch
        {
            // Ignore load errors
        }
    }

    /// <summary>
    /// Sets environment variables that will be applied to all new terminal sessions.
    /// </summary>
    public void SetEnvironmentVariables(Dictionary<string, string> envVars)
    {
        TerminalEnvironmentVariables.Clear();
        foreach (var kvp in envVars)
        {
            TerminalEnvironmentVariables[kvp.Key] = kvp.Value;
        }
    }

    partial void OnSelectedProfileChanged(ShellProfile? value)
    {
        if (value != null && !_suppressProfilePersist)
        {
            SaveDefaultShellPreference();
        }
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
    /// Creates a new terminal session using the currently selected shell profile.
    /// </summary>
    [RelayCommand]
    private void CreateNewSession()
    {
        var session = new TerminalSession();
        ApplyEnvironmentVariables(session);
        session.PropertyChanged += OnAnySessionPropertyChanged;
        Sessions.Add(session);
        ActiveSession = session;
        session.Start(WorkingDirectory, SelectedProfile);
    }

    /// <summary>
    /// Creates a new terminal session with a specific shell profile.
    /// </summary>
    [RelayCommand]
    private void CreateSessionWithProfile(ShellProfile? profile)
    {
        if (profile == null) return;

        var session = new TerminalSession();
        ApplyEnvironmentVariables(session);
        session.PropertyChanged += OnAnySessionPropertyChanged;
        Sessions.Add(session);
        ActiveSession = session;
        session.Start(WorkingDirectory, profile);

        IsProfileDropdownOpen = false;
    }

    /// <summary>
    /// Creates a new terminal session opened at a specific directory.
    /// Used by "Open Terminal Here" from Solution Explorer.
    /// </summary>
    public void CreateSessionAtDirectory(string directory)
    {
        if (!Directory.Exists(directory)) return;

        var session = new TerminalSession();
        ApplyEnvironmentVariables(session);
        session.PropertyChanged += OnAnySessionPropertyChanged;
        Sessions.Add(session);
        ActiveSession = session;
        session.Start(directory, SelectedProfile);
    }

    /// <summary>
    /// Applies configured environment variables to a session before starting.
    /// </summary>
    private void ApplyEnvironmentVariables(TerminalSession session)
    {
        if (TerminalEnvironmentVariables.Count > 0)
        {
            session.EnvironmentVariables = new Dictionary<string, string>(TerminalEnvironmentVariables);
        }
    }

    /// <summary>
    /// Tracks CWD changes in any session and fires CurrentDirectoryChanged for the focused one.
    /// </summary>
    private void OnAnySessionPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TerminalSession.CurrentDirectory) && sender == FocusedSession)
        {
            var dir = FocusedSession?.CurrentDirectory ?? "";
            if (!string.IsNullOrEmpty(dir))
            {
                CurrentDirectoryChanged?.Invoke(dir);
            }
        }
    }

    /// <summary>
    /// Sets the selected profile as default without creating a session.
    /// </summary>
    [RelayCommand]
    private void SetDefaultProfile(ShellProfile? profile)
    {
        if (profile == null) return;
        SelectedProfile = profile;
    }

    /// <summary>
    /// Start command - creates a new session if none exists, or starts the focused one.
    /// </summary>
    [RelayCommand]
    private void Start()
    {
        if (Sessions.Count == 0 || ActiveSession == null)
        {
            CreateNewSession();
        }
        else
        {
            var target = FocusedSession;
            if (target != null && !target.IsRunning)
            {
                target.Start(WorkingDirectory, SelectedProfile);
            }
        }
    }

    [RelayCommand]
    private void Stop()
    {
        FocusedSession?.Stop();
    }

    [RelayCommand]
    private void Clear()
    {
        FocusedSession?.Clear();
    }

    [RelayCommand]
    private void SendInput()
    {
        var target = FocusedSession;
        if (string.IsNullOrEmpty(InputText) || target == null) return;

        target.SendCommand(InputText);
        InputText = "";
    }

    [RelayCommand]
    private void CloseSession(TerminalSession? session)
    {
        if (session == null) return;

        // If closing the split session, unsplit first
        if (IsSplit && session == SplitSession)
        {
            UnsplitTerminal();
        }

        session.PropertyChanged -= OnAnySessionPropertyChanged;
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
    /// Splits the terminal pane, creating a new session on the right side.
    /// If already split, toggles back to single pane.
    /// </summary>
    [RelayCommand]
    private void SplitTerminal()
    {
        if (IsSplit)
        {
            // Unsplit: close the split session and go back to single pane
            UnsplitTerminal();
            return;
        }

        // Create a new session for the right pane
        var session = new TerminalSession();
        ApplyEnvironmentVariables(session);
        session.PropertyChanged += OnAnySessionPropertyChanged;
        Sessions.Add(session);
        SplitSession = session;
        session.Start(WorkingDirectory, SelectedProfile);

        IsSplit = true;
        FocusedPane = SplitPaneFocus.Right;
        SplitStateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Closes the split pane and returns to a single terminal view.
    /// The split session remains in the sessions list as a tab.
    /// </summary>
    [RelayCommand]
    private void UnsplitTerminal()
    {
        if (!IsSplit) return;

        // Unsubscribe from split session events
        if (SplitSession != null)
        {
            SplitSession.OutputAppended -= OnSplitOutputAppended;
            SplitSession.OutputCleared -= OnSplitOutputCleared;
        }

        SplitSession = null;
        IsSplit = false;
        FocusedPane = SplitPaneFocus.Left;
        SplitStateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Sets the focused pane (left or right) so commands go to the correct session.
    /// </summary>
    [RelayCommand]
    private void SetFocusedPane(SplitPaneFocus pane)
    {
        FocusedPane = pane;
    }

    partial void OnSplitSessionChanged(TerminalSession? oldValue, TerminalSession? newValue)
    {
        if (oldValue != null)
        {
            oldValue.OutputAppended -= OnSplitOutputAppended;
            oldValue.OutputCleared -= OnSplitOutputCleared;
        }

        if (newValue != null)
        {
            newValue.OutputAppended += OnSplitOutputAppended;
            newValue.OutputCleared += OnSplitOutputCleared;
        }
    }

    private void OnSplitOutputAppended(string text)
    {
        SplitOutputAppended?.Invoke(text);
    }

    private void OnSplitOutputCleared()
    {
        SplitOutputCleared?.Invoke();
    }

    /// <summary>
    /// Gets the session that should receive commands based on the focused pane.
    /// </summary>
    public TerminalSession? FocusedSession =>
        IsSplit && FocusedPane == SplitPaneFocus.Right ? SplitSession : ActiveSession;

    /// <summary>
    /// Sends a command to the active terminal session.
    /// </summary>
    public void SendCommand(string command)
    {
        FocusedSession?.SendCommand(command);
    }

    /// <summary>
    /// Called by the view when a file path link is clicked in terminal output.
    /// </summary>
    public void NavigateToFile(string filePath, int line, int column)
    {
        FileNavigationRequested?.Invoke(filePath, line, column);
    }

    public void Dispose()
    {
        if (_settingsService != null)
            _settingsService.SettingChanged -= OnSettingChanged;

        foreach (var session in Sessions)
        {
            session.Dispose();
        }
        Sessions.Clear();
    }
}
