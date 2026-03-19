namespace VisualGameStudio.Core.Abstractions.Services;

/// <summary>
/// Service for persisting and managing launch configurations.
/// Configurations are stored in .vgs/launch.json within the project directory.
/// </summary>
public interface ILaunchConfigurationService
{
    /// <summary>
    /// Load all launch configurations for the given project directory.
    /// Returns a default configuration if no file exists.
    /// </summary>
    Task<LaunchConfigurationFile> LoadAsync(string projectDirectory);

    /// <summary>
    /// Save launch configurations to .vgs/launch.json in the project directory.
    /// </summary>
    Task SaveAsync(string projectDirectory, LaunchConfigurationFile configFile);

    /// <summary>
    /// Get the active (selected) launch configuration for debugging.
    /// </summary>
    Task<LaunchConfigurationEntry?> GetActiveConfigurationAsync(string projectDirectory);
}

/// <summary>
/// Root object for .vgs/launch.json
/// </summary>
public class LaunchConfigurationFile
{
    public string? ActiveConfiguration { get; set; }
    public List<LaunchConfigurationEntry> Configurations { get; set; } = new();
}

/// <summary>
/// A single launch configuration entry in launch.json
/// </summary>
public class LaunchConfigurationEntry
{
    public string Name { get; set; } = "Debug";
    public string Program { get; set; } = "";
    public string[] Args { get; set; } = Array.Empty<string>();
    public string Cwd { get; set; } = "${ProjectDir}";
    public Dictionary<string, string> Env { get; set; } = new();
    public bool StopOnEntry { get; set; }
    public bool EnableDebugging { get; set; } = true;
    public bool ExternalConsole { get; set; }
}
