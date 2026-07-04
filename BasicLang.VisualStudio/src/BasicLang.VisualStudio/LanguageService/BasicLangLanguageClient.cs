using System.ComponentModel.Composition;
using System.Diagnostics;
using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities;
using Newtonsoft.Json.Linq;
using StreamJsonRpc;

namespace BasicLang.VisualStudio.LanguageService;

/// <summary>
/// BasicLang Language Server Protocol (LSP) client.
/// Connects to the BasicLang.exe language server for IntelliSense support.
/// </summary>
[ContentType(BasicLangConstants.ContentTypeName)]
[Export(typeof(ILanguageClient))]
[RunOnContext(RunningContext.RunOnHost)]
public class BasicLangLanguageClient : ILanguageClient, ILanguageClientCustomMessage2, IDisposable
{
    /// <inheritdoc />
    public string Name => "BasicLang Language Server";

    /// <inheritdoc />
    public IEnumerable<string>? ConfigurationSections => new[] { "basiclang" };

    /// <summary>
    /// Initialization options sent to the server with the LSP "initialize" request.
    /// Evaluated by the LSP platform each time the server is (re)started, so a
    /// "Restart Language Server" after changing Tools > Options > BasicLang > General
    /// picks up the new values. Reads the thread-safe options snapshot (populated on
    /// the UI thread by the package/options page), so this property is safe on any thread.
    /// </summary>
    public object? InitializationOptions
    {
        get
        {
            var options = Options.GeneralOptionsPage.Snapshot;
            return new
            {
                enableSemanticHighlighting = options.EnableSemanticHighlighting,
                enableInlayHints = options.EnableInlayHints,
                enableCodeLens = options.EnableCodeLens,
                enableDiagnostics = options.EnableDiagnostics,
                logLevel = options.LogLevel.ToString()
            };
        }
    }

    /// <inheritdoc />
    public IEnumerable<string> FilesToWatch => new[]
    {
        "**/*.bl",
        "**/*.bas"
    };

    /// <summary>
    /// Show the failure info bar only for real failures. When activation returned
    /// null because the user disabled auto-start, the LSP platform still treats it
    /// as an initialization failure — suppress the notification for that case.
    /// </summary>
    public bool ShowNotificationOnInitializeFailed => !_startSuppressedByOptions;

    /// <inheritdoc />
    public event AsyncEventHandler<EventArgs>? StartAsync;

    /// <inheritdoc />
    public event AsyncEventHandler<EventArgs>? StopAsync;

    /// <summary>
    /// Custom message handler for additional LSP messages.
    /// </summary>
    public object? CustomMessageTarget { get; set; }

    /// <summary>
    /// Middle layer for message transformation.
    /// </summary>
    public object? MiddleLayer { get; set; }

    private Process? _serverProcess;
    private JsonRpc? _rpc;
    private bool _disposed;
    private volatile bool _manualStartRequested;
    private volatile bool _startSuppressedByOptions;

    /// <summary>
    /// Activates the language client and starts the language server.
    /// </summary>
    public async Task<Connection?> ActivateAsync(CancellationToken token)
    {
        await Task.Yield();

        // MEF activates this client on content-type load (e.g. VS restoring an open
        // .bas document), which can run before the package's background autoload has
        // primed GeneralOptionsPage.Snapshot from persisted settings. Force-load the
        // package first so the AutoStart gate and LanguageServerPath override below
        // see the user's real settings instead of compiled defaults.
        await EnsureOptionsLoadedAsync(token);

        // Honor "Auto-Start Language Server" from Tools > Options > BasicLang > General.
        // An explicit "Restart Language Server" command always starts the server.
        if (!Options.GeneralOptionsPage.Snapshot.AutoStartLanguageServer && !_manualStartRequested)
        {
            _startSuppressedByOptions = true;
            Debug.WriteLine("BasicLang language server auto-start is disabled in options");
            OutputMessage("Language server auto-start is disabled in Tools > Options > BasicLang > General. Use BasicLang > Restart Language Server to start it manually.");
            return null;
        }

        _startSuppressedByOptions = false;

        var serverPath = FindLanguageServer();
        if (string.IsNullOrEmpty(serverPath))
        {
            Debug.WriteLine("BasicLang language server not found");
            OutputMessage("BasicLang language server not found. Please ensure BasicLang is installed.", Options.LogLevel.Error);
            return null;
        }

        Debug.WriteLine($"Starting BasicLang language server: {serverPath}");
        OutputMessage($"Starting BasicLang language server: {serverPath}");

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = serverPath,
                Arguments = "--lsp",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(serverPath)
            };

            _serverProcess = Process.Start(startInfo);
            if (_serverProcess == null)
            {
                Debug.WriteLine("Failed to start BasicLang language server process");
                OutputMessage("Failed to start BasicLang language server process", Options.LogLevel.Error);
                return null;
            }

            // Log stderr output for debugging
            _serverProcess.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    Debug.WriteLine($"[BasicLang LSP stderr]: {e.Data}");
                }
            };
            _serverProcess.BeginErrorReadLine();

            Debug.WriteLine("BasicLang language server started successfully");
            OutputMessage("BasicLang language server started successfully");

            return new Connection(
                _serverProcess.StandardOutput.BaseStream,
                _serverProcess.StandardInput.BaseStream);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error starting BasicLang language server: {ex.Message}");
            OutputMessage($"Error starting BasicLang language server: {ex.Message}", Options.LogLevel.Error);
            return null;
        }
    }

    /// <summary>
    /// Called when the language client is loaded.
    /// </summary>
    public async Task OnLoadedAsync()
    {
        if (StartAsync != null)
        {
            await StartAsync.InvokeAsync(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Called when the language server has been initialized.
    /// </summary>
    public Task OnServerInitializedAsync()
    {
        Debug.WriteLine("BasicLang language server initialized");
        OutputMessage("BasicLang language server initialized and ready");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called when server initialization fails.
    /// </summary>
    public Task<InitializationFailureContext?> OnServerInitializeFailedAsync(ILanguageClientInitializationInfo initializationState)
    {
        // Not a failure: activation was intentionally suppressed because the user
        // disabled auto-start. Don't log an error or surface a failure context.
        if (_startSuppressedByOptions)
        {
            return Task.FromResult<InitializationFailureContext?>(null);
        }

        var message = $"BasicLang language server initialization failed: {initializationState.StatusMessage}";
        Debug.WriteLine(message);
        OutputMessage(message, Options.LogLevel.Error);

        return Task.FromResult<InitializationFailureContext?>(new InitializationFailureContext
        {
            FailureMessage = message
        });
    }

    /// <summary>
    /// Ensures BasicLangPackage has finished loading — which primes
    /// <see cref="Options.GeneralOptionsPage.Snapshot"/> from persisted settings —
    /// before the snapshot is consulted. No-op once primed. Any failure falls back
    /// to the current snapshot (compiled defaults) rather than blocking activation.
    /// </summary>
    private static async Task EnsureOptionsLoadedAsync(CancellationToken token)
    {
        if (Options.GeneralOptionsPage.SnapshotPrimed)
            return;

        try
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(token);

            if (await AsyncServiceProvider.GlobalProvider.GetServiceAsync(typeof(Microsoft.VisualStudio.Shell.Interop.SVsShell))
                is Microsoft.VisualStudio.Shell.Interop.IVsShell7 shell)
            {
                var packageGuid = Guids.Package;
                await shell.LoadPackageAsync(ref packageGuid);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"BasicLang: failed to load package for options snapshot: {ex.Message}");
        }
        finally
        {
            // Don't keep server-start work on the UI thread.
            await TaskScheduler.Default;
        }
    }

    /// <summary>
    /// Attaches the JSON-RPC channel.
    /// </summary>
    public Task AttachForCustomMessageAsync(JsonRpc rpc)
    {
        _rpc = rpc;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Finds the BasicLang language server executable.
    /// </summary>
    private string? FindLanguageServer()
    {
        return BasicLangExeLocator.FindBasicLangExe();
    }

    private readonly object _outputLock = new object();
    private Microsoft.VisualStudio.Threading.JoinableTask? _outputChain;

    /// <summary>
    /// Outputs a message to the Visual Studio output window without blocking the caller.
    ///
    /// This used to be a synchronous <c>JoinableTaskFactory.Run</c> around a
    /// <c>SwitchToMainThreadAsync</c>, which blocks the calling thread until the UI
    /// thread services the switch. OutputMessage is called from LSP activation paths
    /// that run on background threads while the UI thread may itself be blocked
    /// waiting on that same activation — a classic deadlock. The fire-and-forget
    /// <c>RunAsync</c> below cannot deadlock because the caller never waits on the
    /// posted work: RunAsync returns immediately, and the main-thread switch inside
    /// simply completes whenever the UI thread pumps. Because the work runs as a
    /// JoinableTask of the global factory, it also remains join-inlinable and cannot
    /// itself create a cycle. Messages are chained on the previous write so output
    /// ordering is preserved.
    /// </summary>
    private void OutputMessage(string message, Options.LogLevel level = Options.LogLevel.Information)
    {
        // Honor the user's Tools > Options > BasicLang > General > Log Level setting.
        var configuredLevel = Options.GeneralOptionsPage.Snapshot.LogLevel;
        if (configuredLevel == Options.LogLevel.None || level < configuredLevel)
            return;

        lock (_outputLock)
        {
            var previous = _outputChain;
            _outputChain = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                // Preserve message order; the previous link never faults (see catch below).
                if (previous != null)
                {
                    await previous.JoinAsync();
                }

                try
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                    var outputWindow = await AsyncServiceProvider.GlobalProvider.GetServiceAsync(typeof(Microsoft.VisualStudio.Shell.Interop.SVsOutputWindow)) as Microsoft.VisualStudio.Shell.Interop.IVsOutputWindow;
                    if (outputWindow != null)
                    {
                        var guidPane = Microsoft.VisualStudio.VSConstants.OutputWindowPaneGuid.GeneralPane_guid;
                        outputWindow.CreatePane(ref guidPane, "BasicLang", 1, 1);
                        outputWindow.GetPane(ref guidPane, out var pane);
                        pane?.OutputStringThreadSafe($"[BasicLang] {message}\n");
                    }
                }
                catch (Exception ex)
                {
                    // Never fault the chain; just log the failure.
                    Debug.WriteLine($"BasicLang output window write failed: {ex.Message}");
                }
            });
        }
    }

    /// <summary>
    /// Restarts the language server.
    /// </summary>
    public async Task RestartServerAsync()
    {
        Debug.WriteLine("Restarting BasicLang language server...");
        OutputMessage("Restarting language server...");

        // An explicit restart is a manual start request: it must start the server
        // (and pick up freshly applied options) even if auto-start is disabled.
        _manualStartRequested = true;

        // Stop current server
        if (StopAsync != null)
        {
            await StopAsync.InvokeAsync(this, EventArgs.Empty);
        }

        StopServer();

        // Small delay before restarting
        await Task.Delay(500);

        // Start new server
        if (StartAsync != null)
        {
            await StartAsync.InvokeAsync(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Stops the language server process.
    /// </summary>
    private void StopServer()
    {
        try
        {
            if (_serverProcess != null && !_serverProcess.HasExited)
            {
                _serverProcess.Kill();
            }
            _serverProcess?.Dispose();
            _serverProcess = null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error stopping language server: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopServer();
        _rpc?.Dispose();
    }
}
