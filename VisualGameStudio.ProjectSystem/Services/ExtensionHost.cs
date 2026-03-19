using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using StreamJsonRpc;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// Manages the Node.js extension host subprocess that runs VS Code extensions.
/// Communication uses JSON-RPC over stdin/stdout via StreamJsonRpc.
/// </summary>
public class ExtensionHost : IDisposable
{
    private Process? _hostProcess;
    private JsonRpc? _rpc;
    private CancellationTokenSource? _cts;
    private Task? _heartbeatTask;
    private readonly IOutputService _outputService;
    private readonly string _extensionHostScriptPath;
    private readonly object _lock = new();
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Whether the extension host process is running and connected.
    /// </summary>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// Raised when the host process exits unexpectedly.
    /// </summary>
    public event EventHandler? HostCrashed;

    /// <summary>
    /// Raised when the host state changes (started/stopped).
    /// </summary>
    public event EventHandler<bool>? StateChanged;

    /// <summary>
    /// Raised when an extension registers a command in the host.
    /// </summary>
    public event EventHandler<ExtensionCommandRegisteredArgs>? CommandRegistered;

    /// <summary>
    /// Raised when an extension calls vscode.window.showInformationMessage/showErrorMessage/showWarningMessage.
    /// </summary>
    public event EventHandler<ExtensionMessageEventArgs>? MessageReceived;

    /// <summary>
    /// Raised when an extension creates an output channel.
    /// </summary>
    public event EventHandler<OutputChannelEventArgs>? OutputChannelCreated;

    /// <summary>
    /// Raised when an extension writes to an output channel.
    /// </summary>
    public event EventHandler<OutputChannelMessageArgs>? OutputChannelMessage;

    /// <summary>
    /// Raised when an extension sets a status bar item.
    /// </summary>
    public event EventHandler<StatusBarItemArgs>? StatusBarItemChanged;

    /// <summary>
    /// Raised when an extension registers a completion provider.
    /// </summary>
    public event EventHandler<CompletionProviderRegisteredArgs>? CompletionProviderRegistered;

    /// <summary>
    /// Raised when an extension registers a hover provider.
    /// </summary>
    public event EventHandler<HoverProviderRegisteredArgs>? HoverProviderRegistered;

    public ExtensionHost(IOutputService outputService, string extensionHostScriptPath)
    {
        _outputService = outputService;
        _extensionHostScriptPath = extensionHostScriptPath;
    }

    /// <summary>
    /// Starts the Node.js extension host process.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning) return;

        _cts = new CancellationTokenSource();

        var nodePath = FindNodeExecutable();
        if (nodePath == null)
        {
            _outputService.WriteError("[ExtensionHost] Node.js not found. Install Node.js to enable extension support.", OutputCategory.General);
            return;
        }

        if (!File.Exists(_extensionHostScriptPath))
        {
            _outputService.WriteError($"[ExtensionHost] Extension host script not found: {_extensionHostScriptPath}", OutputCategory.General);
            return;
        }

        _outputService.WriteLine($"[ExtensionHost] Starting: {nodePath} \"{_extensionHostScriptPath}\"", OutputCategory.General);

        var startInfo = new ProcessStartInfo
        {
            FileName = nodePath,
            Arguments = $"\"{_extensionHostScriptPath}\"",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardInputEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8
        };

        _hostProcess = new Process { StartInfo = startInfo };
        _hostProcess.EnableRaisingEvents = true;
        _hostProcess.Exited += OnHostProcessExited;
        _hostProcess.ErrorDataReceived += (s, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                _outputService.WriteLine($"[ExtensionHost] {e.Data}", OutputCategory.General);
            }
        };

        try
        {
            _hostProcess.Start();
            _hostProcess.BeginErrorReadLine();

            // Set up JSON-RPC over stdin/stdout
            var handler = new HeaderDelimitedMessageHandler(
                _hostProcess.StandardInput.BaseStream,
                _hostProcess.StandardOutput.BaseStream);

            _rpc = new JsonRpc(handler);

            // Register methods the extension host can call back into the IDE
            _rpc.AddLocalRpcMethod("host/registerCommand", new Action<string, string>(OnRegisterCommand));
            _rpc.AddLocalRpcMethod("host/showMessage", new Func<string, string, string, string[]?, Task<string?>>(OnShowMessageAsync));
            _rpc.AddLocalRpcMethod("host/createOutputChannel", new Action<string, string>(OnCreateOutputChannel));
            _rpc.AddLocalRpcMethod("host/outputChannelAppend", new Action<string, string>(OnOutputChannelAppend));
            _rpc.AddLocalRpcMethod("host/outputChannelAppendLine", new Action<string, string>(OnOutputChannelAppendLine));
            _rpc.AddLocalRpcMethod("host/setStatusBarItem", new Action<string, string, string?, string?>(OnSetStatusBarItem));
            _rpc.AddLocalRpcMethod("host/registerCompletionProvider", new Action<string, string, string[]?>(OnRegisterCompletionProvider));
            _rpc.AddLocalRpcMethod("host/registerHoverProvider", new Action<string, string>(OnRegisterHoverProvider));
            _rpc.AddLocalRpcMethod("host/log", new Action<string, string>(OnLog));

            _rpc.StartListening();

            // Start heartbeat monitoring
            _heartbeatTask = RunHeartbeatAsync(_cts.Token);

            IsRunning = true;
            StateChanged?.Invoke(this, true);
            _outputService.WriteLine("[ExtensionHost] Started successfully.", OutputCategory.General);
        }
        catch (Exception ex)
        {
            _outputService.WriteError($"[ExtensionHost] Failed to start: {ex.Message}", OutputCategory.General);
            CleanupProcess();
        }
    }

    /// <summary>
    /// Stops the extension host process gracefully.
    /// </summary>
    public async Task StopAsync()
    {
        if (!IsRunning) return;

        _outputService.WriteLine("[ExtensionHost] Stopping...", OutputCategory.General);

        _cts?.Cancel();

        // Try graceful shutdown via RPC
        if (_rpc != null)
        {
            try
            {
                using var shutdownCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _rpc.InvokeWithCancellationAsync("shutdown", cancellationToken: shutdownCts.Token);
            }
            catch
            {
                // Ignore errors during shutdown
            }
        }

        CleanupProcess();

        IsRunning = false;
        StateChanged?.Invoke(this, false);
        _outputService.WriteLine("[ExtensionHost] Stopped.", OutputCategory.General);
    }

    /// <summary>
    /// Sends a request to activate an extension in the host.
    /// </summary>
    public async Task<bool> ActivateExtensionAsync(string extensionId, string extensionPath, string? mainEntry, CancellationToken cancellationToken = default)
    {
        if (!IsRunning || _rpc == null) return false;

        try
        {
            var result = await _rpc.InvokeWithCancellationAsync<bool>(
                "activateExtension",
                new object[] { extensionId, extensionPath, mainEntry ?? "" },
                cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            _outputService.WriteError($"[ExtensionHost] Failed to activate {extensionId}: {ex.Message}", OutputCategory.General);
            return false;
        }
    }

    /// <summary>
    /// Sends a request to deactivate an extension in the host.
    /// </summary>
    public async Task<bool> DeactivateExtensionAsync(string extensionId, CancellationToken cancellationToken = default)
    {
        if (!IsRunning || _rpc == null) return false;

        try
        {
            var result = await _rpc.InvokeWithCancellationAsync<bool>(
                "deactivateExtension",
                new object[] { extensionId },
                cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            _outputService.WriteError($"[ExtensionHost] Failed to deactivate {extensionId}: {ex.Message}", OutputCategory.General);
            return false;
        }
    }

    /// <summary>
    /// Executes a command registered by an extension.
    /// </summary>
    public async Task<object?> ExecuteCommandAsync(string commandId, object?[]? args = null, CancellationToken cancellationToken = default)
    {
        if (!IsRunning || _rpc == null)
        {
            throw new InvalidOperationException("Extension host is not running.");
        }

        try
        {
            var result = await _rpc.InvokeWithCancellationAsync<object?>(
                "executeCommand",
                new object[] { commandId, args ?? Array.Empty<object?>() },
                cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            _outputService.WriteError($"[ExtensionHost] Command '{commandId}' failed: {ex.Message}", OutputCategory.General);
            throw;
        }
    }

    /// <summary>
    /// Sends an activation event to the extension host (e.g., "onLanguage:python").
    /// </summary>
    public async Task FireActivationEventAsync(string activationEvent, CancellationToken cancellationToken = default)
    {
        if (!IsRunning || _rpc == null) return;

        try
        {
            await _rpc.InvokeWithCancellationAsync(
                "fireActivationEvent",
                new object[] { activationEvent },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _outputService.WriteError($"[ExtensionHost] Activation event '{activationEvent}' failed: {ex.Message}", OutputCategory.General);
        }
    }

    /// <summary>
    /// Requests completion items from extension-registered completion providers.
    /// </summary>
    public async Task<JsonElement?> RequestCompletionsAsync(string languageId, string uri, int line, int column, CancellationToken cancellationToken = default)
    {
        if (!IsRunning || _rpc == null) return null;

        try
        {
            return await _rpc.InvokeWithCancellationAsync<JsonElement?>(
                "provideCompletions",
                new object[] { languageId, uri, line, column },
                cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Requests hover info from extension-registered hover providers.
    /// </summary>
    public async Task<JsonElement?> RequestHoverAsync(string languageId, string uri, int line, int column, CancellationToken cancellationToken = default)
    {
        if (!IsRunning || _rpc == null) return null;

        try
        {
            return await _rpc.InvokeWithCancellationAsync<JsonElement?>(
                "provideHover",
                new object[] { languageId, uri, line, column },
                cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Notifies the extension host that a document was opened.
    /// </summary>
    public async Task NotifyDocumentOpenedAsync(string uri, string languageId, string text, CancellationToken cancellationToken = default)
    {
        if (!IsRunning || _rpc == null) return;

        try
        {
            await _rpc.NotifyAsync("documentOpened", new object[] { uri, languageId, text });
        }
        catch { }
    }

    /// <summary>
    /// Notifies the extension host that a document was changed.
    /// </summary>
    public async Task NotifyDocumentChangedAsync(string uri, string text, int version, CancellationToken cancellationToken = default)
    {
        if (!IsRunning || _rpc == null) return;

        try
        {
            await _rpc.NotifyAsync("documentChanged", new object[] { uri, text, version });
        }
        catch { }
    }

    /// <summary>
    /// Notifies the extension host that a document was closed.
    /// </summary>
    public async Task NotifyDocumentClosedAsync(string uri, CancellationToken cancellationToken = default)
    {
        if (!IsRunning || _rpc == null) return;

        try
        {
            await _rpc.NotifyAsync("documentClosed", new object[] { uri });
        }
        catch { }
    }

    /// <summary>
    /// Notifies the extension host of the current workspace folder.
    /// </summary>
    public async Task SetWorkspaceFolderAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!IsRunning || _rpc == null) return;

        try
        {
            await _rpc.NotifyAsync("setWorkspaceFolder", new object[] { path });
        }
        catch { }
    }

    #region JSON-RPC Callback Handlers (called by Extension Host)

    private void OnRegisterCommand(string extensionId, string commandId)
    {
        _outputService.WriteLine($"[ExtensionHost] Command registered: {commandId} (by {extensionId})", OutputCategory.General);
        CommandRegistered?.Invoke(this, new ExtensionCommandRegisteredArgs
        {
            ExtensionId = extensionId,
            CommandId = commandId
        });
    }

    private async Task<string?> OnShowMessageAsync(string extensionId, string severity, string message, string[]? actions)
    {
        _outputService.WriteLine($"[ExtensionHost] [{severity}] {extensionId}: {message}", OutputCategory.General);

        var eventArgs = new ExtensionMessageEventArgs
        {
            ExtensionId = extensionId,
            Severity = severity,
            Message = message,
            Actions = actions?.ToList() ?? new List<string>(),
            ResponseSource = new TaskCompletionSource<string?>()
        };

        MessageReceived?.Invoke(this, eventArgs);

        // Wait for the IDE to respond (e.g., user clicks a button)
        if (eventArgs.Actions.Count > 0 && eventArgs.ResponseSource != null)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                cts.Token.Register(() => eventArgs.ResponseSource.TrySetResult(null));
                return await eventArgs.ResponseSource.Task;
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private void OnCreateOutputChannel(string extensionId, string channelName)
    {
        _outputService.WriteLine($"[ExtensionHost] Output channel created: {channelName} (by {extensionId})", OutputCategory.General);
        OutputChannelCreated?.Invoke(this, new OutputChannelEventArgs
        {
            ExtensionId = extensionId,
            ChannelName = channelName
        });
    }

    private void OnOutputChannelAppend(string channelName, string text)
    {
        OutputChannelMessage?.Invoke(this, new OutputChannelMessageArgs
        {
            ChannelName = channelName,
            Text = text,
            AppendLine = false
        });
    }

    private void OnOutputChannelAppendLine(string channelName, string text)
    {
        OutputChannelMessage?.Invoke(this, new OutputChannelMessageArgs
        {
            ChannelName = channelName,
            Text = text,
            AppendLine = true
        });
    }

    private void OnSetStatusBarItem(string extensionId, string text, string? tooltip, string? command)
    {
        StatusBarItemChanged?.Invoke(this, new StatusBarItemArgs
        {
            ExtensionId = extensionId,
            Text = text,
            Tooltip = tooltip,
            Command = command
        });
    }

    private void OnRegisterCompletionProvider(string extensionId, string languageId, string[]? triggerCharacters)
    {
        _outputService.WriteLine($"[ExtensionHost] Completion provider registered for {languageId} (by {extensionId})", OutputCategory.General);
        CompletionProviderRegistered?.Invoke(this, new CompletionProviderRegisteredArgs
        {
            ExtensionId = extensionId,
            LanguageId = languageId,
            TriggerCharacters = triggerCharacters?.ToList() ?? new List<string>()
        });
    }

    private void OnRegisterHoverProvider(string extensionId, string languageId)
    {
        _outputService.WriteLine($"[ExtensionHost] Hover provider registered for {languageId} (by {extensionId})", OutputCategory.General);
        HoverProviderRegistered?.Invoke(this, new HoverProviderRegisteredArgs
        {
            ExtensionId = extensionId,
            LanguageId = languageId
        });
    }

    private void OnLog(string level, string message)
    {
        _outputService.WriteLine($"[ExtensionHost] [{level}] {message}", OutputCategory.General);
    }

    #endregion

    #region Private Helpers

    private void OnHostProcessExited(object? sender, EventArgs e)
    {
        if (!IsRunning) return; // Expected shutdown

        var exitCode = _hostProcess?.ExitCode ?? -1;
        _outputService.WriteError($"[ExtensionHost] Process exited unexpectedly with code {exitCode}.", OutputCategory.General);

        IsRunning = false;
        StateChanged?.Invoke(this, false);
        HostCrashed?.Invoke(this, EventArgs.Empty);
    }

    private async Task RunHeartbeatAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);

                if (_rpc != null && IsRunning)
                {
                    using var hbCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, hbCts.Token);
                    await _rpc.InvokeWithCancellationAsync<bool>("heartbeat", cancellationToken: linked.Token);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _outputService.WriteError($"[ExtensionHost] Heartbeat failed: {ex.Message}", OutputCategory.General);
                // Process may have crashed - the Exited event will handle it
                break;
            }
        }
    }

    private string? FindNodeExecutable()
    {
        // Try 'node' directly (on PATH)
        var nodeNames = new[] { "node", "node.exe" };

        foreach (var name in nodeNames)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = name,
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    proc.WaitForExit(5000);
                    if (proc.ExitCode == 0)
                    {
                        return name;
                    }
                }
            }
            catch
            {
                // Not found, try next
            }
        }

        // Try common install locations
        var commonPaths = new[]
        {
            @"C:\Program Files\nodejs\node.exe",
            @"C:\Program Files (x86)\nodejs\node.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe"),
            "/usr/local/bin/node",
            "/usr/bin/node"
        };

        foreach (var path in commonPaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        // Try NVM for Windows
        var nvmDir = Environment.GetEnvironmentVariable("NVM_HOME");
        if (!string.IsNullOrEmpty(nvmDir))
        {
            var nvmSymlink = Environment.GetEnvironmentVariable("NVM_SYMLINK");
            if (!string.IsNullOrEmpty(nvmSymlink))
            {
                var nvmNode = Path.Combine(nvmSymlink, "node.exe");
                if (File.Exists(nvmNode)) return nvmNode;
            }
        }

        return null;
    }

    private void CleanupProcess()
    {
        _rpc?.Dispose();
        _rpc = null;

        if (_hostProcess != null)
        {
            try
            {
                if (!_hostProcess.HasExited)
                {
                    _hostProcess.Kill(entireProcessTree: true);
                    _hostProcess.WaitForExit(3000);
                }
            }
            catch { }

            _hostProcess.Dispose();
            _hostProcess = null;
        }
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts?.Cancel();
        CleanupProcess();
        _cts?.Dispose();
    }
}

#region Extension Host Event Args

/// <summary>
/// Event args for when an extension registers a command.
/// </summary>
public class ExtensionCommandRegisteredArgs : EventArgs
{
    public string ExtensionId { get; set; } = "";
    public string CommandId { get; set; } = "";
}

/// <summary>
/// Event args for output channel creation.
/// </summary>
public class OutputChannelEventArgs : EventArgs
{
    public string ExtensionId { get; set; } = "";
    public string ChannelName { get; set; } = "";
}

/// <summary>
/// Event args for output channel messages.
/// </summary>
public class OutputChannelMessageArgs : EventArgs
{
    public string ChannelName { get; set; } = "";
    public string Text { get; set; } = "";
    public bool AppendLine { get; set; }
}

/// <summary>
/// Event args for status bar item changes.
/// </summary>
public class StatusBarItemArgs : EventArgs
{
    public string ExtensionId { get; set; } = "";
    public string Text { get; set; } = "";
    public string? Tooltip { get; set; }
    public string? Command { get; set; }
}

/// <summary>
/// Event args for completion provider registration.
/// </summary>
public class CompletionProviderRegisteredArgs : EventArgs
{
    public string ExtensionId { get; set; } = "";
    public string LanguageId { get; set; } = "";
    public List<string> TriggerCharacters { get; set; } = new();
}

/// <summary>
/// Event args for hover provider registration.
/// </summary>
public class HoverProviderRegisteredArgs : EventArgs
{
    public string ExtensionId { get; set; } = "";
    public string LanguageId { get; set; } = "";
}

#endregion
