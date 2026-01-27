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

    /// <inheritdoc />
    public object? InitializationOptions => new
    {
        enableSemanticHighlighting = true,
        enableInlayHints = true,
        enableCodeLens = true,
        enableDiagnostics = true
    };

    /// <inheritdoc />
    public IEnumerable<string> FilesToWatch => new[]
    {
        "**/*.bl",
        "**/*.bas",
        "**/*.blproj"
    };

    /// <inheritdoc />
    public bool ShowNotificationOnInitializeFailed => true;

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

    /// <summary>
    /// Activates the language client and starts the language server.
    /// </summary>
    public async Task<Connection?> ActivateAsync(CancellationToken token)
    {
        await Task.Yield();

        var serverPath = FindLanguageServer();
        if (string.IsNullOrEmpty(serverPath))
        {
            Debug.WriteLine("BasicLang language server not found");
            OutputMessage("BasicLang language server not found. Please ensure BasicLang is installed.");
            return null;
        }

        Debug.WriteLine($"Starting BasicLang language server: {serverPath}");
        OutputMessage($"Starting BasicLang language server: {serverPath}");

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = serverPath,
                Arguments = "lsp",
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
                OutputMessage("Failed to start BasicLang language server process");
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
            OutputMessage($"Error starting BasicLang language server: {ex.Message}");
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
        var message = $"BasicLang language server initialization failed: {initializationState.StatusMessage}";
        Debug.WriteLine(message);
        OutputMessage(message);

        return Task.FromResult<InitializationFailureContext?>(new InitializationFailureContext
        {
            FailureMessage = message
        });
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
        var possiblePaths = new[]
        {
            // Extension directory
            Path.Combine(Path.GetDirectoryName(typeof(BasicLangLanguageClient).Assembly.Location) ?? "", "BasicLang.exe"),
            // Local app data installation
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BasicLang", "BasicLang.exe"),
            // Program Files installation
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "BasicLang", "BasicLang.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "BasicLang", "BasicLang.exe"),
            // Development build (relative to solution)
            @"..\..\..\..\BasicLang\bin\Release\net8.0\BasicLang.exe",
            @"..\..\..\..\BasicLang\bin\Debug\net8.0\BasicLang.exe",
            @"..\..\..\..\IDE\BasicLang.exe"
        };

        foreach (var path in possiblePaths)
        {
            try
            {
                var fullPath = Path.GetFullPath(path);
                if (File.Exists(fullPath))
                {
                    Debug.WriteLine($"Found BasicLang language server at: {fullPath}");
                    return fullPath;
                }
            }
            catch
            {
                // Ignore path errors
            }
        }

        // Search in PATH environment variable
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (var dir in pathEnv.Split(Path.PathSeparator))
            {
                try
                {
                    var fullPath = Path.Combine(dir, "BasicLang.exe");
                    if (File.Exists(fullPath))
                    {
                        Debug.WriteLine($"Found BasicLang language server in PATH: {fullPath}");
                        return fullPath;
                    }
                }
                catch
                {
                    // Ignore path errors
                }
            }
        }

        Debug.WriteLine("BasicLang language server not found in any location");
        return null;
    }

    /// <summary>
    /// Outputs a message to the Visual Studio output window.
    /// </summary>
    private void OutputMessage(string message)
    {
        try
        {
            ThreadHelper.JoinableTaskFactory.Run(async () =>
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
            });
        }
        catch
        {
            // Ignore output window errors
        }
    }

    /// <summary>
    /// Restarts the language server.
    /// </summary>
    public async Task RestartServerAsync()
    {
        Debug.WriteLine("Restarting BasicLang language server...");
        OutputMessage("Restarting language server...");

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
