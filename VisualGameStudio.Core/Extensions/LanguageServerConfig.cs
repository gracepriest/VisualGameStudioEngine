using System.Text.Json.Serialization;

namespace VisualGameStudio.Core.Extensions;

/// <summary>
/// Configuration for a language server that can be used with VGS
/// </summary>
public class LanguageServerConfig
{
    /// <summary>
    /// Unique identifier for this language server
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Display name
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Description of what this language server provides
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// File extensions this server handles (e.g., ".py", ".cs")
    /// </summary>
    public List<string> FileExtensions { get; set; } = new();

    /// <summary>
    /// Language IDs this server handles (e.g., "python", "csharp")
    /// </summary>
    public List<string> LanguageIds { get; set; } = new();

    /// <summary>
    /// How to start the language server
    /// </summary>
    public ServerStartInfo StartInfo { get; set; } = new();

    /// <summary>
    /// Initialization options to send to the server
    /// </summary>
    public Dictionary<string, object>? InitializationOptions { get; set; }

    /// <summary>
    /// Whether this server is enabled
    /// </summary>
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// How to start a language server process
/// </summary>
public class ServerStartInfo
{
    /// <summary>
    /// The command to run (e.g., "python", "node", "dotnet")
    /// </summary>
    public string Command { get; set; } = "";

    /// <summary>
    /// Arguments to pass to the command
    /// </summary>
    public List<string> Arguments { get; set; } = new();

    /// <summary>
    /// Transport type for communication
    /// </summary>
    public TransportType Transport { get; set; } = TransportType.Stdio;

    /// <summary>
    /// For socket transport, the port to connect to
    /// </summary>
    public int? Port { get; set; }

    /// <summary>
    /// Environment variables to set
    /// </summary>
    public Dictionary<string, string>? Environment { get; set; }

    /// <summary>
    /// Working directory for the server process
    /// </summary>
    public string? WorkingDirectory { get; set; }
}

/// <summary>
/// Transport type for LSP communication
/// </summary>
public enum TransportType
{
    /// <summary>
    /// Standard input/output (most common)
    /// </summary>
    Stdio,

    /// <summary>
    /// TCP socket
    /// </summary>
    Socket,

    /// <summary>
    /// Named pipe
    /// </summary>
    Pipe
}

/// <summary>
/// Configuration for a debug adapter
/// </summary>
public class DebugAdapterConfig
{
    /// <summary>
    /// Unique identifier
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Display name
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Debug type (e.g., "python", "cppdbg", "node")
    /// </summary>
    public string Type { get; set; } = "";

    /// <summary>
    /// Languages this adapter supports
    /// </summary>
    public List<string> Languages { get; set; } = new();

    /// <summary>
    /// How to start the debug adapter
    /// </summary>
    public ServerStartInfo StartInfo { get; set; } = new();

    /// <summary>
    /// Whether this adapter is enabled
    /// </summary>
    public bool IsEnabled { get; set; } = true;
}
