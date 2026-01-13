using System.Collections.Concurrent;
using VisualGameStudio.Core.Extensions;
using VisualGameStudio.Core.LSP;

namespace VisualGameStudio.ProjectSystem.LSP;

/// <summary>
/// Interface for managing multiple LSP clients
/// </summary>
public interface ILspClientManager : IDisposable
{
    /// <summary>
    /// Get or create an LSP client for a language
    /// </summary>
    Task<ILspClient?> GetClientAsync(string languageId, string workspaceRoot, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get an existing client for a file extension
    /// </summary>
    ILspClient? GetClientForExtension(string fileExtension);

    /// <summary>
    /// Register a language server configuration
    /// </summary>
    void RegisterServer(LanguageServerConfig config);

    /// <summary>
    /// Get all registered language server configurations
    /// </summary>
    IReadOnlyList<LanguageServerConfig> GetRegisteredServers();

    /// <summary>
    /// Stop all running clients
    /// </summary>
    Task StopAllAsync();

    /// <summary>
    /// Event raised when diagnostics are received for a file
    /// </summary>
    event EventHandler<LspDiagnosticsEventArgs>? DiagnosticsReceived;
}

/// <summary>
/// Event args for diagnostics from any language server
/// </summary>
public class LspDiagnosticsEventArgs : EventArgs
{
    public string FilePath { get; set; } = "";
    public string LanguageId { get; set; } = "";
    public IReadOnlyList<Diagnostic> Diagnostics { get; set; } = Array.Empty<Diagnostic>();
}

/// <summary>
/// Manages multiple LSP clients for different languages
/// </summary>
public class LspClientManager : ILspClientManager
{
    private readonly ConcurrentDictionary<string, LanguageServerConfig> _serverConfigs = new();
    private readonly ConcurrentDictionary<string, ILspClient> _activeClients = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _clientLocks = new();
    private bool _disposed;

    public event EventHandler<LspDiagnosticsEventArgs>? DiagnosticsReceived;

    public LspClientManager()
    {
        // Register built-in language servers
        RegisterBuiltInServers();
    }

    private void RegisterBuiltInServers()
    {
        // Python - pylsp
        RegisterServer(new LanguageServerConfig
        {
            Id = "pylsp",
            Name = "Python Language Server",
            Description = "Python language support via pylsp",
            LanguageIds = new List<string> { "python" },
            FileExtensions = new List<string> { ".py", ".pyw" },
            StartInfo = new ServerStartInfo
            {
                Command = "pylsp",
                Transport = TransportType.Stdio
            }
        });

        // TypeScript/JavaScript - typescript-language-server
        RegisterServer(new LanguageServerConfig
        {
            Id = "typescript",
            Name = "TypeScript Language Server",
            Description = "TypeScript and JavaScript language support",
            LanguageIds = new List<string> { "typescript", "javascript", "typescriptreact", "javascriptreact" },
            FileExtensions = new List<string> { ".ts", ".tsx", ".js", ".jsx" },
            StartInfo = new ServerStartInfo
            {
                Command = "typescript-language-server",
                Arguments = new List<string> { "--stdio" },
                Transport = TransportType.Stdio
            }
        });

        // C# - OmniSharp
        RegisterServer(new LanguageServerConfig
        {
            Id = "omnisharp",
            Name = "OmniSharp",
            Description = "C# language support via OmniSharp",
            LanguageIds = new List<string> { "csharp" },
            FileExtensions = new List<string> { ".cs" },
            StartInfo = new ServerStartInfo
            {
                Command = "OmniSharp",
                Arguments = new List<string> { "-lsp" },
                Transport = TransportType.Stdio
            }
        });

        // Rust - rust-analyzer
        RegisterServer(new LanguageServerConfig
        {
            Id = "rust-analyzer",
            Name = "Rust Analyzer",
            Description = "Rust language support via rust-analyzer",
            LanguageIds = new List<string> { "rust" },
            FileExtensions = new List<string> { ".rs" },
            StartInfo = new ServerStartInfo
            {
                Command = "rust-analyzer",
                Transport = TransportType.Stdio
            }
        });

        // Go - gopls
        RegisterServer(new LanguageServerConfig
        {
            Id = "gopls",
            Name = "Go Language Server",
            Description = "Go language support via gopls",
            LanguageIds = new List<string> { "go" },
            FileExtensions = new List<string> { ".go" },
            StartInfo = new ServerStartInfo
            {
                Command = "gopls",
                Arguments = new List<string> { "serve" },
                Transport = TransportType.Stdio
            }
        });

        // C/C++ - clangd
        RegisterServer(new LanguageServerConfig
        {
            Id = "clangd",
            Name = "Clangd",
            Description = "C/C++ language support via clangd",
            LanguageIds = new List<string> { "c", "cpp" },
            FileExtensions = new List<string> { ".c", ".h", ".cpp", ".hpp", ".cc", ".hh", ".cxx", ".hxx" },
            StartInfo = new ServerStartInfo
            {
                Command = "clangd",
                Transport = TransportType.Stdio
            }
        });

        // JSON - vscode-json-languageserver
        RegisterServer(new LanguageServerConfig
        {
            Id = "json-languageserver",
            Name = "JSON Language Server",
            Description = "JSON language support",
            LanguageIds = new List<string> { "json", "jsonc" },
            FileExtensions = new List<string> { ".json", ".jsonc" },
            StartInfo = new ServerStartInfo
            {
                Command = "vscode-json-language-server",
                Arguments = new List<string> { "--stdio" },
                Transport = TransportType.Stdio
            }
        });

        // HTML - vscode-html-languageserver
        RegisterServer(new LanguageServerConfig
        {
            Id = "html-languageserver",
            Name = "HTML Language Server",
            Description = "HTML language support",
            LanguageIds = new List<string> { "html" },
            FileExtensions = new List<string> { ".html", ".htm" },
            StartInfo = new ServerStartInfo
            {
                Command = "vscode-html-language-server",
                Arguments = new List<string> { "--stdio" },
                Transport = TransportType.Stdio
            }
        });

        // CSS - vscode-css-languageserver
        RegisterServer(new LanguageServerConfig
        {
            Id = "css-languageserver",
            Name = "CSS Language Server",
            Description = "CSS/SCSS/LESS language support",
            LanguageIds = new List<string> { "css", "scss", "less" },
            FileExtensions = new List<string> { ".css", ".scss", ".less" },
            StartInfo = new ServerStartInfo
            {
                Command = "vscode-css-language-server",
                Arguments = new List<string> { "--stdio" },
                Transport = TransportType.Stdio
            }
        });

        // YAML - yaml-language-server
        RegisterServer(new LanguageServerConfig
        {
            Id = "yaml-languageserver",
            Name = "YAML Language Server",
            Description = "YAML language support",
            LanguageIds = new List<string> { "yaml" },
            FileExtensions = new List<string> { ".yaml", ".yml" },
            StartInfo = new ServerStartInfo
            {
                Command = "yaml-language-server",
                Arguments = new List<string> { "--stdio" },
                Transport = TransportType.Stdio
            }
        });

        // Lua - lua-language-server
        RegisterServer(new LanguageServerConfig
        {
            Id = "lua-language-server",
            Name = "Lua Language Server",
            Description = "Lua language support",
            LanguageIds = new List<string> { "lua" },
            FileExtensions = new List<string> { ".lua" },
            StartInfo = new ServerStartInfo
            {
                Command = "lua-language-server",
                Transport = TransportType.Stdio
            }
        });
    }

    public void RegisterServer(LanguageServerConfig config)
    {
        _serverConfigs[config.Id] = config;
    }

    public IReadOnlyList<LanguageServerConfig> GetRegisteredServers()
    {
        return _serverConfigs.Values.ToList();
    }

    public async Task<ILspClient?> GetClientAsync(string languageId, string workspaceRoot, CancellationToken cancellationToken = default)
    {
        // Find a config for this language
        var config = _serverConfigs.Values.FirstOrDefault(c => c.LanguageIds.Contains(languageId));
        if (config == null || !config.IsEnabled)
        {
            return null;
        }

        // Check if we already have an active client
        if (_activeClients.TryGetValue(config.Id, out var existingClient) && existingClient.IsConnected)
        {
            return existingClient;
        }

        // Get or create a lock for this server
        var clientLock = _clientLocks.GetOrAdd(config.Id, _ => new SemaphoreSlim(1, 1));

        await clientLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock
            if (_activeClients.TryGetValue(config.Id, out existingClient) && existingClient.IsConnected)
            {
                return existingClient;
            }

            // Create and initialize new client
            var client = new LspClient(config);
            client.DiagnosticsReceived += OnClientDiagnosticsReceived;

            if (await client.InitializeAsync(workspaceRoot, cancellationToken))
            {
                _activeClients[config.Id] = client;
                return client;
            }

            // Failed to initialize
            client.Dispose();
            return null;
        }
        finally
        {
            clientLock.Release();
        }
    }

    public ILspClient? GetClientForExtension(string fileExtension)
    {
        var ext = fileExtension.StartsWith('.') ? fileExtension : $".{fileExtension}";

        var config = _serverConfigs.Values.FirstOrDefault(c =>
            c.FileExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase));

        if (config == null)
        {
            return null;
        }

        _activeClients.TryGetValue(config.Id, out var client);
        return client?.IsConnected == true ? client : null;
    }

    private void OnClientDiagnosticsReceived(object? sender, PublishDiagnosticsEventArgs e)
    {
        // Convert URI to file path
        var filePath = e.Uri;
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            filePath = uri.LocalPath;
        }

        // Find which language this client handles
        var languageId = "unknown";
        if (sender is LspClient client)
        {
            foreach (var kvp in _activeClients)
            {
                if (kvp.Value == client)
                {
                    var config = _serverConfigs.Values.FirstOrDefault(c => c.Id == kvp.Key);
                    if (config != null)
                    {
                        languageId = config.LanguageIds.FirstOrDefault() ?? "unknown";
                    }
                    break;
                }
            }
        }

        DiagnosticsReceived?.Invoke(this, new LspDiagnosticsEventArgs
        {
            FilePath = filePath,
            LanguageId = languageId,
            Diagnostics = e.Diagnostics
        });
    }

    public async Task StopAllAsync()
    {
        var shutdownTasks = new List<Task>();

        foreach (var client in _activeClients.Values)
        {
            shutdownTasks.Add(Task.Run(async () =>
            {
                try
                {
                    await client.ShutdownAsync();
                }
                catch
                {
                    // Ignore shutdown errors
                }
                finally
                {
                    client.Dispose();
                }
            }));
        }

        await Task.WhenAll(shutdownTasks);
        _activeClients.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var client in _activeClients.Values)
        {
            try
            {
                client.Dispose();
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        _activeClients.Clear();

        foreach (var lockObj in _clientLocks.Values)
        {
            lockObj.Dispose();
        }
        _clientLocks.Clear();
    }
}
