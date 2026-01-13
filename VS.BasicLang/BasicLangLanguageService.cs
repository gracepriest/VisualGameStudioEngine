using System.ComponentModel.Composition;
using System.Diagnostics;
using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities;
using Newtonsoft.Json.Linq;

namespace VS.BasicLang;

/// <summary>
/// Content type definition for BasicLang
/// </summary>
public static class BasicLangContentTypeDefinition
{
    [Export]
    [Name(BasicLangConstants.ContentTypeName)]
    [BaseDefinition(CodeRemoteContentDefinition.CodeRemoteContentTypeName)]
    public static ContentTypeDefinition? BasicLangContentType;

    [Export]
    [FileExtension(BasicLangConstants.FileExtension)]
    [ContentType(BasicLangConstants.ContentTypeName)]
    public static FileExtensionToContentTypeDefinition? BasicLangFileExtension;

    [Export]
    [FileExtension(BasicLangConstants.FileExtension2)]
    [ContentType(BasicLangConstants.ContentTypeName)]
    public static FileExtensionToContentTypeDefinition? BasicLangFileExtension2;

    [Export]
    [FileExtension(BasicLangConstants.ProjectExtension)]
    [ContentType(BasicLangConstants.ContentTypeName)]
    public static FileExtensionToContentTypeDefinition? BasicLangProjectExtension;
}

/// <summary>
/// BasicLang Language Server Protocol client
/// </summary>
[ContentType(BasicLangConstants.ContentTypeName)]
[Export(typeof(ILanguageClient))]
public class BasicLangLanguageService : ILanguageClient, IDisposable
{
    public string Name => "BasicLang Language Server";

    public IEnumerable<string>? ConfigurationSections => new[] { "basiclang" };

    public object? InitializationOptions => new
    {
        enableSemanticHighlighting = true,
        enableInlayHints = true,
        enableCodeLens = true
    };

    public IEnumerable<string> FilesToWatch => new[] { "**/*.bl", "**/*.bas", "**/*.blproj" };

    public bool ShowNotificationOnInitializeFailed => true;

    public event AsyncEventHandler<EventArgs>? StartAsync;
    public event AsyncEventHandler<EventArgs>? StopAsync;

    private Process? _serverProcess;
    private bool _disposed;

    public async Task<Connection?> ActivateAsync(CancellationToken token)
    {
        await Task.Yield();

        var serverPath = FindLanguageServer();
        if (string.IsNullOrEmpty(serverPath))
        {
            Debug.WriteLine("BasicLang language server not found");
            return null;
        }

        Debug.WriteLine($"Starting BasicLang language server: {serverPath}");

        var startInfo = new ProcessStartInfo
        {
            FileName = serverPath,
            Arguments = "lsp",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        _serverProcess = Process.Start(startInfo);
        if (_serverProcess == null)
        {
            Debug.WriteLine("Failed to start BasicLang language server process");
            return null;
        }

        Debug.WriteLine("BasicLang language server started successfully");

        return new Connection(
            _serverProcess.StandardOutput.BaseStream,
            _serverProcess.StandardInput.BaseStream);
    }

    private string? FindLanguageServer()
    {
        // Try various common locations
        var possiblePaths = new[]
        {
            // Local installation
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BasicLang", "BasicLang.exe"),
            // Program Files
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "BasicLang", "BasicLang.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "BasicLang", "BasicLang.exe"),
            // PATH
            "BasicLang.exe",
            // Development build
            Path.Combine(Path.GetDirectoryName(typeof(BasicLangLanguageService).Assembly.Location) ?? "", "BasicLang.exe")
        };

        foreach (var path in possiblePaths)
        {
            try
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }
            catch
            {
                // Ignore access errors
            }
        }

        // Try to find in PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (var dir in pathEnv.Split(Path.PathSeparator))
            {
                var fullPath = Path.Combine(dir, "BasicLang.exe");
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
        }

        return null;
    }

    public async Task OnLoadedAsync()
    {
        if (StartAsync != null)
        {
            await StartAsync.InvokeAsync(this, EventArgs.Empty);
        }
    }

    public Task OnServerInitializedAsync()
    {
        Debug.WriteLine("BasicLang language server initialized");
        return Task.CompletedTask;
    }

    public Task<InitializationFailureContext?> OnServerInitializeFailedAsync(ILanguageClientInitializationInfo initializationState)
    {
        Debug.WriteLine($"BasicLang language server initialization failed: {initializationState.StatusMessage}");
        return Task.FromResult<InitializationFailureContext?>(new InitializationFailureContext
        {
            FailureMessage = $"BasicLang language server failed to initialize: {initializationState.StatusMessage}"
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (_serverProcess != null && !_serverProcess.HasExited)
            {
                _serverProcess.Kill();
            }
            _serverProcess?.Dispose();
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
