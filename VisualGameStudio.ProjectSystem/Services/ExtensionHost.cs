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

    private readonly List<(string extensionId, string extensionPath)> _activeExtensions = new();

    // NOTE: a JsonSerializerOptions with CamelCase + PropertyNameCaseInsensitive used to live here,
    // referenced by nothing. It was System.Text.Json configuration sitting on what was then a
    // Newtonsoft channel, and it advertised case-INSENSITIVE binding that this wire has never had:
    // StreamJsonRpc matches named arguments to parameter names EXACTLY and case-sensitively.
    // Deleted rather than wired up, so nobody infers a tolerance that does not exist.

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

    /// <summary>
    /// Raised when an extension publishes diagnostics.
    /// </summary>
    public event EventHandler<ExtensionDiagnosticsEventArgs>? DiagnosticsReceived;

    /// <summary>
    /// Raised when an extension registers a language provider.
    /// </summary>
    public event EventHandler<ProviderRegisteredEventArgs>? ProviderRegistered;

    /// <summary>
    /// Raised when an extension creates a tree view.
    /// </summary>
    public event EventHandler<TreeViewEventArgs>? TreeViewCreated;

    /// <summary>
    /// Raised when an extension requests a tree view refresh.
    /// </summary>
    public event EventHandler<TreeViewEventArgs>? TreeViewRefreshRequested;

    /// <summary>
    /// Raised when an extension creates a webview panel.
    /// </summary>
    public event EventHandler<WebViewEventArgs>? WebViewCreated;

    /// <summary>
    /// Raised when an extension updates webview HTML content.
    /// </summary>
    public event EventHandler<WebViewHtmlEventArgs>? WebViewHtmlChanged;

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
            // MUST be BOM-less: accessing Process.StandardInput sets AutoFlush=true,
            // which flushes the wrapper StreamWriter and writes the encoding preamble.
            // With Encoding.UTF8 (BOM) that injects EF BB BF into the host's stdin,
            // corrupting the first Content-Length header of the JSON-RPC channel.
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
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
            // The formatter is chosen EXPLICITLY. The no-formatter overload defaults to the
            // Newtonsoft one, which cannot materialize a System.Text.Json.JsonElement — it yields
            // default(JsonElement) silently instead of throwing. Every JsonElement parameter and
            // every JsonElement? result on this channel therefore arrived EMPTY: the diagnostics
            // payload, workspace/applyEdit, all twelve provider results and both tree-view results.
            // Nothing failed loudly; the values were simply blank.
            var handler = new HeaderDelimitedMessageHandler(
                _hostProcess.StandardInput.BaseStream,
                _hostProcess.StandardOutput.BaseStream,
                new SystemTextJsonFormatter());

            _rpc = new JsonRpc(handler);

            // Register methods the extension host can call back into the IDE
            _rpc.AddLocalRpcMethod("registerCommand", new Action<string, string>(OnRegisterCommand));
            _rpc.AddLocalRpcMethod("window/showMessage", new Func<string, string, string, string[]?, Task<string?>>(OnShowMessageAsync));
            _rpc.AddLocalRpcMethod("outputChannel/create", new Action<string, string>(OnCreateOutputChannel));
            _rpc.AddLocalRpcMethod("outputChannel/append", new Action<string, string>(OnOutputChannelAppend));
            _rpc.AddLocalRpcMethod("statusBar/update", new Action<string, string, string?, string?>(OnSetStatusBarItem));
            _rpc.AddLocalRpcMethod("languages/registerProvider", new Action<string, string, string, JsonElement, JsonElement>(OnRegisterProvider));
            _rpc.AddLocalRpcMethod("languages/publishDiagnostics", new Action<string, JsonElement, string?>(OnPublishDiagnostics));
            _rpc.AddLocalRpcMethod("treeView/create", new Action<string, string, string?>(OnTreeViewCreate));
            _rpc.AddLocalRpcMethod("treeView/refresh", new Action<string, string?>(OnTreeViewRefresh));
            _rpc.AddLocalRpcMethod("webview/create", new Action<string, string, string, string?>(OnWebviewCreate));
            _rpc.AddLocalRpcMethod("webview/setHtml", new Action<string, string>(OnWebviewSetHtml));
            _rpc.AddLocalRpcMethod("workspace/applyEdit", new Func<JsonElement, Task<bool>>(OnApplyEditAsync));
            _rpc.AddLocalRpcMethod("extensionActivated", new Action<string>(OnExtensionActivated));
            _rpc.AddLocalRpcMethod("log", new Action<string, string>(OnLog));
            _rpc.AddLocalRpcMethod("ready", new Action(OnReady));

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
    /// What <c>activateExtension</c> answers with (main.js:156/171/233/241). It is an OBJECT, not a
    /// boolean — deserializing it as <c>bool</c> threw
    /// "Error reading boolean. Unexpected token: StartObject" and reported every successful
    /// activation as a failure.
    ///
    /// <para>⛔ Every member carries an explicit <c>[JsonPropertyName]</c>. This channel used to run
    /// the Newtonsoft formatter, which matches property names case-INSENSITIVELY, so PascalCase
    /// members bound the host's camelCase JSON for free. System.Text.Json is case-SENSITIVE by
    /// default, so moving the formatter silently turned every property back to its default —
    /// <c>Activated</c> became false and a successful activation was reported as
    /// "JS activation failed". Naming each property explicitly makes the binding independent of
    /// whichever formatter or naming policy the channel happens to use.</para>
    ///
    /// <para>Public so a test can bind real host JSON against it; these types describe the wire
    /// contract rather than any internal state.</para>
    /// </summary>
    public sealed class ActivationResult
    {
        [JsonPropertyName("activated")] public bool Activated { get; set; }
        [JsonPropertyName("hasMain")] public bool HasMain { get; set; }
        [JsonPropertyName("error")] public string? Error { get; set; }
    }

    /// <summary>
    /// Shape of <c>deactivateExtension</c>'s reply (main.js:251/271/273). Explicitly named for the
    /// same reason as <see cref="ActivationResult"/>.
    /// </summary>
    public sealed class DeactivationResult
    {
        [JsonPropertyName("deactivated")] public bool Deactivated { get; set; }
        [JsonPropertyName("error")] public string? Error { get; set; }
    }

    /// <summary>
    /// Sends a request to activate an extension in the host.
    /// </summary>
    public async Task<bool> ActivateExtensionAsync(string extensionId, string extensionPath, string? mainEntry, CancellationToken cancellationToken = default)
    {
        if (!IsRunning || _rpc == null) return false;

        try
        {
            // Named parameters, not positional: main.js reads params.extensionPath / params.extensionId.
            // mainEntry rides along for forward compatibility but the host derives `main` from the
            // extension's own package.json, so it is currently ignored on the far side.
            var result = await _rpc.InvokeWithParameterObjectAsync<ActivationResult?>(
                "activateExtension",
                new { extensionId, extensionPath, mainEntry = mainEntry ?? "" },
                cancellationToken);

            if (result?.Activated == true)
            {
                _activeExtensions.Add((extensionId, extensionPath));
                return true;
            }

            if (!string.IsNullOrEmpty(result?.Error))
            {
                _outputService.WriteError(
                    $"[ExtensionHost] {extensionId} did not activate: {result!.Error}", OutputCategory.General);
            }

            return false;
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
            var result = await _rpc.InvokeWithParameterObjectAsync<DeactivationResult?>(
                "deactivateExtension",
                new { extensionId },
                cancellationToken);

            if (result?.Deactivated == true)
            {
                _activeExtensions.RemoveAll(x => x.extensionId == extensionId);
                return true;
            }

            return false;
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
            var result = await _rpc.InvokeWithParameterObjectAsync<object?>(
                "executeCommand",
                new { command = commandId, args = args ?? Array.Empty<object?>() },
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
            await _rpc.InvokeWithParameterObjectAsync(
                "fireActivationEvent",
                new { @event = activationEvent },
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
            // NOTE: main.js registers no "provideCompletions" handler — the live provider path is
            // textDocument/* through RequestProviderAsync. Corrected for shape consistency, but
            // this method is currently a no-op on the far side.
            return await _rpc.InvokeWithParameterObjectAsync<JsonElement?>(
                "provideCompletions",
                new { languageId, uri = ToDocumentUri(uri), line, column },
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
            // Same as provideCompletions: no handler exists on the JS side today.
            return await _rpc.InvokeWithParameterObjectAsync<JsonElement?>(
                "provideHover",
                new { languageId, uri = ToDocumentUri(uri), line, column },
                cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    #region Document Sync Methods

    /// <summary>
    /// Notifies the extension host that a document was opened.
    /// </summary>
    public async Task NotifyDocumentOpenedAsync(string uri, string languageId, int version, string text, CancellationToken ct = default)
    {
        if (!IsRunning || _rpc == null) return;
        try { await _rpc.NotifyWithParameterObjectAsync("textDocument/didOpen", new { uri = ToDocumentUri(uri),languageId, version, text }); }
        catch { }
    }

    /// <summary>
    /// Notifies the extension host that a document was changed.
    /// </summary>
    public async Task NotifyDocumentChangedAsync(string uri, int version, string text, CancellationToken ct = default)
    {
        if (!IsRunning || _rpc == null) return;
        try { await _rpc.NotifyWithParameterObjectAsync("textDocument/didChange", new { uri = ToDocumentUri(uri),version, text }); }
        catch { }
    }

    /// <summary>
    /// Notifies the extension host that a document was closed.
    /// </summary>
    public async Task NotifyDocumentClosedAsync(string uri, CancellationToken ct = default)
    {
        if (!IsRunning || _rpc == null) return;
        try { await _rpc.NotifyWithParameterObjectAsync("textDocument/didClose", new { uri = ToDocumentUri(uri) }); }
        catch { }
    }

    /// <summary>
    /// Notifies the extension host that a document was saved.
    /// </summary>
    public async Task NotifyDocumentSavedAsync(string uri, string? text = null, CancellationToken ct = default)
    {
        if (!IsRunning || _rpc == null) return;
        try { await _rpc.NotifyWithParameterObjectAsync("textDocument/didSave", new { uri = ToDocumentUri(uri),text }); }
        catch { }
    }

    /// <summary>
    /// Notifies the extension host that configuration changed.
    /// </summary>
    public async Task NotifyConfigurationChangedAsync(object settings, CancellationToken ct = default)
    {
        if (!IsRunning || _rpc == null) return;
        try { await _rpc.NotifyWithParameterObjectAsync("workspace/didChangeConfiguration", new { settings }); }
        catch { }
    }

    /// <summary>
    /// Notifies the extension host that the active editor changed.
    /// </summary>
    public async Task NotifyActiveEditorChangedAsync(string? uri, string? languageId, CancellationToken ct = default)
    {
        if (!IsRunning || _rpc == null) return;
        try { await _rpc.NotifyWithParameterObjectAsync("activeEditor/didChange", new { uri = ToDocumentUri(uri),languageId }); }
        catch { }
    }

    #endregion

    #region Provider Request Methods

    /// <summary>
    /// Sends a generic provider request to the extension host.
    /// </summary>
    public async Task<JsonElement?> RequestProviderAsync(string method, object parameters, CancellationToken ct = default)
    {
        if (!IsRunning || _rpc == null) return null;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));
            // `parameters` is ALREADY the parameter object — the old `new[] { parameters }` wrapped
            // it in an array, so every provider handler read undefined off it.
            return await _rpc.InvokeWithParameterObjectAsync<JsonElement?>(method, parameters, cts.Token);
        }
        catch { return null; }
    }

    /// <summary>
    /// Normalises a document identity for the extension host, which keys documents by URI while the
    /// IDE identifies them by raw filesystem path.
    ///
    /// <para>Without this the two ends can never agree. The host stores a document under
    /// <c>Uri.parse(path).toString()</c>; for <c>C:\proj\a.js</c> the JS scheme regex is lazy, so it
    /// captures scheme <c>"C"</c>, the backslash normalisation (gated on <c>scheme === 'file'</c>)
    /// never runs, and the key becomes <c>C:%5Cproj%5Ca.js</c>. Lookup then passes the raw string
    /// verbatim. Every provider request missed, permanently and silently.</para>
    ///
    /// <para>It also decides selector matching: with a raw path the derived scheme is <c>"C"</c>, so
    /// <c>{ scheme: 'file' }</c> — the most common VS Code selector shape — scores zero.</para>
    ///
    /// <para>⚠ BEHAVIOUR CHANGE: every uri string every extension sees changes. That is a correction
    /// toward the real VS Code contract, which is <c>file:///</c> URIs rather than Windows paths.</para>
    /// </summary>
    public static string ToDocumentUri(string pathOrUri)
    {
        if (string.IsNullOrWhiteSpace(pathOrUri)) return pathOrUri;

        // Anything that already carries a scheme is left exactly as-is: untitled:, vscode-userdata:
        // and https: are all legitimate document identities, and rewriting them would break them.
        if (HasUriScheme(pathOrUri)) return pathOrUri;

        try
        {
            return new Uri(pathOrUri).AbsoluteUri;
        }
        catch
        {
            // Notifications are fire-and-forget; a malformed path must degrade to itself rather
            // than throw on a path the caller cannot observe.
            return pathOrUri;
        }
    }

    /// <summary>
    /// Whether a string already carries a URI scheme.
    ///
    /// <para>The single-character check is the important part: a Windows drive letter looks exactly
    /// like a scheme, and treating <c>C:</c> as one is precisely the bug this class of code keeps
    /// reproducing — it is what the host's own lazy regex does.</para>
    /// </summary>
    private static bool HasUriScheme(string value)
    {
        var colon = value.IndexOf(':');

        // No colon at all, or a one-letter prefix — i.e. a drive letter, not a scheme.
        if (colon <= 1) return false;
        if (!char.IsLetter(value[0])) return false;

        for (var i = 1; i < colon; i++)
        {
            var c = value[i];
            if (!char.IsLetterOrDigit(c) && c != '+' && c != '.' && c != '-') return false;
        }

        return true;
    }

    public Task<JsonElement?> RequestCompletionAsync(string uri, int line, int character, CancellationToken ct = default)
        => RequestProviderAsync("textDocument/completion", new { uri = ToDocumentUri(uri),position = new { line, character } }, ct);

    public Task<JsonElement?> RequestHoverAsync(string uri, int line, int character, CancellationToken ct = default)
        => RequestProviderAsync("textDocument/hover", new { uri = ToDocumentUri(uri),position = new { line, character } }, ct);

    public Task<JsonElement?> RequestDefinitionAsync(string uri, int line, int character, CancellationToken ct = default)
        => RequestProviderAsync("textDocument/definition", new { uri = ToDocumentUri(uri),position = new { line, character } }, ct);

    public Task<JsonElement?> RequestReferencesAsync(string uri, int line, int character, CancellationToken ct = default)
        => RequestProviderAsync("textDocument/references", new { uri = ToDocumentUri(uri),position = new { line, character } }, ct);

    public Task<JsonElement?> RequestFormattingAsync(string uri, CancellationToken ct = default)
        => RequestProviderAsync("textDocument/formatting", new { uri = ToDocumentUri(uri) }, ct);

    public Task<JsonElement?> RequestCodeActionsAsync(string uri, int startLine, int startChar, int endLine, int endChar, CancellationToken ct = default)
        => RequestProviderAsync("textDocument/codeAction", new { uri = ToDocumentUri(uri),range = new { start = new { line = startLine, character = startChar }, end = new { line = endLine, character = endChar } } }, ct);

    public Task<JsonElement?> RequestDocumentSymbolsAsync(string uri, CancellationToken ct = default)
        => RequestProviderAsync("textDocument/documentSymbol", new { uri = ToDocumentUri(uri) }, ct);

    public Task<JsonElement?> RequestSignatureHelpAsync(string uri, int line, int character, CancellationToken ct = default)
        => RequestProviderAsync("textDocument/signatureHelp", new { uri = ToDocumentUri(uri),position = new { line, character } }, ct);

    public Task<JsonElement?> RequestRenameAsync(string uri, int line, int character, string newName, CancellationToken ct = default)
        => RequestProviderAsync("textDocument/rename", new { uri = ToDocumentUri(uri),position = new { line, character }, newName }, ct);

    public Task<JsonElement?> RequestFoldingRangesAsync(string uri, CancellationToken ct = default)
        => RequestProviderAsync("textDocument/foldingRange", new { uri = ToDocumentUri(uri) }, ct);

    public Task<JsonElement?> RequestInlayHintsAsync(string uri, int startLine, int startChar, int endLine, int endChar, CancellationToken ct = default)
        => RequestProviderAsync("textDocument/inlayHint", new { uri = ToDocumentUri(uri),range = new { start = new { line = startLine, character = startChar }, end = new { line = endLine, character = endChar } } }, ct);

    public Task<JsonElement?> RequestSemanticTokensAsync(string uri, CancellationToken ct = default)
        => RequestProviderAsync("textDocument/semanticTokens", new { uri = ToDocumentUri(uri) }, ct);

    #endregion

    #region Tree View Request Methods

    /// <summary>
    /// Requests children of a tree view element from the extension host.
    /// Pass null for element to get root-level children.
    /// </summary>
    public async Task<JsonElement?> RequestTreeChildrenAsync(string viewId, string? element, CancellationToken ct = default)
    {
        if (!IsRunning || _rpc == null) return null;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));
            return await _rpc.InvokeWithParameterObjectAsync<JsonElement?>(
                "treeView/getChildren",
                new { viewId, element },
                cts.Token);
        }
        catch { return null; }
    }

    /// <summary>
    /// Requests the tree item representation for a given element from the extension host.
    /// </summary>
    public async Task<JsonElement?> RequestTreeItemAsync(string viewId, string element, CancellationToken ct = default)
    {
        if (!IsRunning || _rpc == null) return null;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));
            return await _rpc.InvokeWithParameterObjectAsync<JsonElement?>(
                "treeView/getTreeItem",
                new { viewId, element },
                cts.Token);
        }
        catch { return null; }
    }

    #endregion

    /// <summary>
    /// Notifies the extension host of the current workspace folder.
    /// </summary>
    public async Task SetWorkspaceFolderAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!IsRunning || _rpc == null) return;

        try
        {
            // NOTE: main.js registers no "setWorkspaceFolder" handler; workspace roots reach the
            // host through `initialize` (params.workspaceFolders) and
            // workspace/didChangeWorkspaceFolders. Shape corrected, but this is currently a no-op.
            await _rpc.NotifyWithParameterObjectAsync("setWorkspaceFolder", new { path });
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

    /// <summary>
    /// Handles <c>languages/registerProvider</c>. All 30 <c>registerXxxProvider</c> entry points in
    /// <c>vscode-api/languages.js</c> funnel through this one notification, so nothing an extension
    /// contributes to the language surface exists until it binds.
    ///
    /// <para>The parameter list mirrors <c>languages.js:53-59</c> EXACTLY —
    /// <c>{ type, id, extensionId, selector, metadata }</c> — because StreamJsonRpc matches named
    /// arguments to parameter names case-sensitively and by exact spelling. The previous signature
    /// took four parameters in a different order, naming two of them <c>selectorJson</c> and
    /// <c>metadataJson</c>, so it never bound: a notification that fails to bind produces no error
    /// response and no log line anywhere. ⛔ Renaming any parameter here silently unbinds the call
    /// again, with no compiler error.</para>
    ///
    /// <para><paramref name="metadata"/> MUST keep its default: <c>languages.js:58</c> sends
    /// <c>metadata || undefined</c>, which omits the key entirely, and a required parameter whose
    /// key is absent fails to bind.</para>
    ///
    /// <para><c>selector</c> arrives as a JSON ARRAY of filter objects and <c>metadata</c> as an
    /// object; both are re-serialised to their raw text because
    /// <c>ExtensionService.OnProviderRegistered</c> parses them with <c>JsonDocument.Parse</c>.</para>
    /// </summary>
    private void OnRegisterProvider(
        string type, string id, string extensionId,
        JsonElement selector, JsonElement metadata = default)
    {
        _outputService.WriteLine(
            $"[ExtensionHost] Provider registered: {type} (by {extensionId})", OutputCategory.General);

        ProviderRegistered?.Invoke(this, new ProviderRegisteredEventArgs
        {
            ExtensionId = extensionId,
            Type = type,
            SelectorJson = selector.ValueKind == JsonValueKind.Undefined ? null : selector.GetRawText(),
            MetadataJson = metadata.ValueKind == JsonValueKind.Undefined ? null : metadata.GetRawText()
        });
    }

    private void OnPublishDiagnostics(string uri, JsonElement diagnostics, string? collectionName)
    {
        DiagnosticsReceived?.Invoke(this, new ExtensionDiagnosticsEventArgs
        {
            Uri = uri,
            Diagnostics = diagnostics,
            CollectionName = collectionName ?? ""
        });
    }

    private void OnTreeViewCreate(string extensionId, string viewId, string? title)
    {
        _outputService.WriteLine($"[ExtensionHost] Tree view created: {viewId} (by {extensionId})", OutputCategory.General);
        TreeViewCreated?.Invoke(this, new TreeViewEventArgs
        {
            ExtensionId = extensionId,
            ViewId = viewId,
            Title = title
        });
    }

    private void OnTreeViewRefresh(string viewId, string? element)
    {
        TreeViewRefreshRequested?.Invoke(this, new TreeViewEventArgs
        {
            ViewId = viewId,
            Element = element
        });
    }

    private void OnWebviewCreate(string extensionId, string panelId, string viewType, string? title)
    {
        _outputService.WriteLine($"[ExtensionHost] Webview created: {panelId} ({viewType}) (by {extensionId})", OutputCategory.General);
        WebViewCreated?.Invoke(this, new WebViewEventArgs
        {
            ExtensionId = extensionId,
            PanelId = panelId,
            ViewType = viewType,
            Title = title
        });
    }

    private void OnWebviewSetHtml(string panelId, string html)
    {
        WebViewHtmlChanged?.Invoke(this, new WebViewHtmlEventArgs
        {
            PanelId = panelId,
            Html = html
        });
    }

    private async Task<bool> OnApplyEditAsync(JsonElement edit)
    {
        _outputService.WriteLine("[ExtensionHost] workspace/applyEdit requested.", OutputCategory.General);
        return true; // TODO: apply workspace edit to IDE
    }

    private void OnExtensionActivated(string extensionId)
    {
        _outputService.WriteLine($"[ExtensionHost] Extension activated: {extensionId}", OutputCategory.General);
    }

    private void OnLog(string level, string message)
    {
        _outputService.WriteLine($"[ExtensionHost] [{level}] {message}", OutputCategory.General);
    }

    private void OnReady()
    {
        _outputService.WriteLine("[ExtensionHost] Host ready.", OutputCategory.General);
    }

    #endregion

    #region Private Helpers

    private void OnHostProcessExited(object? sender, EventArgs e)
    {
        if (!IsRunning) return;

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

    /// <summary>
    /// Delegates to <see cref="BasicLang.Runtime.NodeLocator"/>, which owns the probe
    /// chain. The JavaScript backend's test tier needs the same discovery, and a second
    /// copy would let the IDE and the compiler disagree about which Node they found —
    /// CLAUDE.md's shared-resolver rule ("change it once, not per-consumer").
    /// </summary>
    private string? FindNodeExecutable() => BasicLang.Runtime.NodeLocator.Find();

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

/// <summary>
/// Event args for provider registration.
/// </summary>
public class ProviderRegisteredEventArgs : EventArgs
{
    public string ExtensionId { get; set; } = "";
    public string Type { get; set; } = "";
    public string? SelectorJson { get; set; }
    public string? MetadataJson { get; set; }
}

/// <summary>
/// Event args for tree view operations.
/// </summary>
public class TreeViewEventArgs : EventArgs
{
    public string ExtensionId { get; set; } = "";
    public string ViewId { get; set; } = "";
    public string? Title { get; set; }
    public string? Element { get; set; }
}

/// <summary>
/// Event args for webview creation.
/// </summary>
public class WebViewEventArgs : EventArgs
{
    public string ExtensionId { get; set; } = "";
    public string PanelId { get; set; } = "";
    public string ViewType { get; set; } = "";
    public string? Title { get; set; }
}

/// <summary>
/// Event args for webview HTML content changes.
/// </summary>
public class WebViewHtmlEventArgs : EventArgs
{
    public string PanelId { get; set; } = "";
    public string Html { get; set; } = "";
}

#endregion
