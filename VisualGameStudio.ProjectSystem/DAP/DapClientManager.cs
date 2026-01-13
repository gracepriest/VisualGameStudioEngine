using System.Collections.Concurrent;
using VisualGameStudio.Core.DAP;
using VisualGameStudio.Core.Extensions;

namespace VisualGameStudio.ProjectSystem.DAP;

/// <summary>
/// Interface for managing debug adapters
/// </summary>
public interface IDapClientManager : IDisposable
{
    /// <summary>
    /// Create a new debug client for a debug type
    /// </summary>
    Task<IDapClient?> CreateClientAsync(string debugType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Register a debug adapter configuration
    /// </summary>
    void RegisterAdapter(DebugAdapterConfig config);

    /// <summary>
    /// Get all registered debug adapter configurations
    /// </summary>
    IReadOnlyList<DebugAdapterConfig> GetRegisteredAdapters();

    /// <summary>
    /// Get adapter config for a language
    /// </summary>
    DebugAdapterConfig? GetAdapterForLanguage(string languageId);

    /// <summary>
    /// Stop all running clients
    /// </summary>
    Task StopAllAsync();

    /// <summary>
    /// Event raised when debug output is received
    /// </summary>
    event EventHandler<DebugOutputEventArgs>? OutputReceived;

    /// <summary>
    /// Event raised when debugging is stopped (breakpoint, exception, etc.)
    /// </summary>
    event EventHandler<DebugStoppedEventArgs>? Stopped;

    /// <summary>
    /// Event raised when debugging terminates
    /// </summary>
    event EventHandler<DebugTerminatedEventArgs>? Terminated;
}

/// <summary>
/// Event args for debug output
/// </summary>
public class DebugOutputEventArgs : EventArgs
{
    public string Category { get; set; } = "";
    public string Output { get; set; } = "";
    public string? SourcePath { get; set; }
    public int? Line { get; set; }
}

/// <summary>
/// Event args for debug stopped
/// </summary>
public class DebugStoppedEventArgs : EventArgs
{
    public string Reason { get; set; } = "";
    public int ThreadId { get; set; }
    public string? Description { get; set; }
}

/// <summary>
/// Event args for debug terminated
/// </summary>
public class DebugTerminatedEventArgs : EventArgs
{
    public bool Restart { get; set; }
}

/// <summary>
/// Manages debug adapter clients
/// </summary>
public class DapClientManager : IDapClientManager
{
    private readonly ConcurrentDictionary<string, DebugAdapterConfig> _adapterConfigs = new();
    private readonly ConcurrentBag<IDapClient> _activeClients = new();
    private bool _disposed;

    public event EventHandler<DebugOutputEventArgs>? OutputReceived;
    public event EventHandler<DebugStoppedEventArgs>? Stopped;
    public event EventHandler<DebugTerminatedEventArgs>? Terminated;

    public DapClientManager()
    {
        // Register built-in debug adapters
        RegisterBuiltInAdapters();
    }

    private void RegisterBuiltInAdapters()
    {
        // Python - debugpy
        RegisterAdapter(new DebugAdapterConfig
        {
            Id = "debugpy",
            Name = "Python Debugger",
            Type = "python",
            Languages = new List<string> { "python" },
            StartInfo = new ServerStartInfo
            {
                Command = "python",
                Arguments = new List<string> { "-m", "debugpy.adapter" },
                Transport = TransportType.Stdio
            }
        });

        // Node.js
        RegisterAdapter(new DebugAdapterConfig
        {
            Id = "node-debug",
            Name = "Node.js Debugger",
            Type = "node",
            Languages = new List<string> { "javascript", "typescript" },
            StartInfo = new ServerStartInfo
            {
                Command = "node",
                Arguments = new List<string> { "--inspect-brk" },
                Transport = TransportType.Stdio
            }
        });

        // C/C++ - cppvsdbg (Windows) or lldb/gdb
        RegisterAdapter(new DebugAdapterConfig
        {
            Id = "cppdbg",
            Name = "C/C++ Debugger",
            Type = "cppdbg",
            Languages = new List<string> { "c", "cpp" },
            StartInfo = new ServerStartInfo
            {
                Command = "OpenDebugAD7",
                Transport = TransportType.Stdio
            }
        });

        // C# - netcoredbg
        RegisterAdapter(new DebugAdapterConfig
        {
            Id = "netcoredbg",
            Name = ".NET Core Debugger",
            Type = "coreclr",
            Languages = new List<string> { "csharp", "fsharp", "vb" },
            StartInfo = new ServerStartInfo
            {
                Command = "netcoredbg",
                Arguments = new List<string> { "--interpreter=vscode" },
                Transport = TransportType.Stdio
            }
        });

        // Go - delve
        RegisterAdapter(new DebugAdapterConfig
        {
            Id = "delve",
            Name = "Go Debugger (Delve)",
            Type = "go",
            Languages = new List<string> { "go" },
            StartInfo = new ServerStartInfo
            {
                Command = "dlv",
                Arguments = new List<string> { "dap" },
                Transport = TransportType.Stdio
            }
        });

        // Rust - codelldb
        RegisterAdapter(new DebugAdapterConfig
        {
            Id = "codelldb",
            Name = "CodeLLDB",
            Type = "lldb",
            Languages = new List<string> { "rust", "c", "cpp" },
            StartInfo = new ServerStartInfo
            {
                Command = "codelldb",
                Arguments = new List<string> { "--port", "0" },
                Transport = TransportType.Stdio
            }
        });

        // Java
        RegisterAdapter(new DebugAdapterConfig
        {
            Id = "java-debug",
            Name = "Java Debugger",
            Type = "java",
            Languages = new List<string> { "java" },
            StartInfo = new ServerStartInfo
            {
                Command = "java",
                Arguments = new List<string>
                {
                    "-agentlib:jdwp=transport=dt_socket,server=y,suspend=n,address=*:0",
                    "-Dfile.encoding=UTF-8",
                    "-jar",
                    "java-debug.jar"
                },
                Transport = TransportType.Stdio
            }
        });
    }

    public void RegisterAdapter(DebugAdapterConfig config)
    {
        _adapterConfigs[config.Id] = config;
    }

    public IReadOnlyList<DebugAdapterConfig> GetRegisteredAdapters()
    {
        return _adapterConfigs.Values.Where(c => c.IsEnabled).ToList();
    }

    public DebugAdapterConfig? GetAdapterForLanguage(string languageId)
    {
        return _adapterConfigs.Values.FirstOrDefault(c =>
            c.IsEnabled && c.Languages.Contains(languageId, StringComparer.OrdinalIgnoreCase));
    }

    public async Task<IDapClient?> CreateClientAsync(string debugType, CancellationToken cancellationToken = default)
    {
        // Find a config for this debug type
        var config = _adapterConfigs.Values.FirstOrDefault(c =>
            c.IsEnabled && (c.Type.Equals(debugType, StringComparison.OrdinalIgnoreCase) ||
                           c.Id.Equals(debugType, StringComparison.OrdinalIgnoreCase)));

        if (config == null)
        {
            return null;
        }

        var client = new DapClient(config);

        // Wire up events
        client.Output += OnClientOutput;
        client.Stopped += OnClientStopped;
        client.Terminated += OnClientTerminated;

        if (await client.InitializeAsync(config.Type, cancellationToken))
        {
            _activeClients.Add(client);
            return client;
        }

        // Failed to initialize
        client.Dispose();
        return null;
    }

    private void OnClientOutput(object? sender, OutputEventArgs e)
    {
        OutputReceived?.Invoke(this, new DebugOutputEventArgs
        {
            Category = e.Category,
            Output = e.Output,
            SourcePath = e.Source?.Path,
            Line = e.Line
        });
    }

    private void OnClientStopped(object? sender, StoppedEventArgs e)
    {
        Stopped?.Invoke(this, new DebugStoppedEventArgs
        {
            Reason = e.Reason,
            ThreadId = e.ThreadId,
            Description = e.Description
        });
    }

    private void OnClientTerminated(object? sender, TerminatedEventArgs e)
    {
        Terminated?.Invoke(this, new DebugTerminatedEventArgs
        {
            Restart = e.Restart
        });
    }

    public async Task StopAllAsync()
    {
        var disconnectTasks = new List<Task>();

        while (_activeClients.TryTake(out var client))
        {
            disconnectTasks.Add(Task.Run(async () =>
            {
                try
                {
                    await client.DisconnectAsync();
                }
                catch
                {
                    // Ignore disconnect errors
                }
                finally
                {
                    client.Dispose();
                }
            }));
        }

        await Task.WhenAll(disconnectTasks);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        while (_activeClients.TryTake(out var client))
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
    }
}
