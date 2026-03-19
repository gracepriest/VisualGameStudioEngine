using System.Text.Json;
using System.Text.Json.Serialization;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// Persists launch configurations to .vgs/launch.json in the project directory.
/// </summary>
public class LaunchConfigurationService : ILaunchConfigurationService
{
    private const string VgsFolder = ".vgs";
    private const string LaunchFileName = "launch.json";

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<LaunchConfigurationFile> LoadAsync(string projectDirectory)
    {
        var filePath = GetLaunchFilePath(projectDirectory);

        if (!File.Exists(filePath))
        {
            return CreateDefaultFile();
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            var configFile = JsonSerializer.Deserialize<LaunchConfigurationFile>(json, s_jsonOptions);
            if (configFile == null || configFile.Configurations.Count == 0)
            {
                return CreateDefaultFile();
            }
            return configFile;
        }
        catch
        {
            return CreateDefaultFile();
        }
    }

    public async Task SaveAsync(string projectDirectory, LaunchConfigurationFile configFile)
    {
        var vgsDir = Path.Combine(projectDirectory, VgsFolder);
        Directory.CreateDirectory(vgsDir);

        var filePath = Path.Combine(vgsDir, LaunchFileName);
        var json = JsonSerializer.Serialize(configFile, s_jsonOptions);
        await File.WriteAllTextAsync(filePath, json);
    }

    public async Task<LaunchConfigurationEntry?> GetActiveConfigurationAsync(string projectDirectory)
    {
        var configFile = await LoadAsync(projectDirectory);

        if (!string.IsNullOrEmpty(configFile.ActiveConfiguration))
        {
            var active = configFile.Configurations.FirstOrDefault(
                c => c.Name.Equals(configFile.ActiveConfiguration, StringComparison.OrdinalIgnoreCase));
            if (active != null) return active;
        }

        return configFile.Configurations.FirstOrDefault();
    }

    private static string GetLaunchFilePath(string projectDirectory)
    {
        return Path.Combine(projectDirectory, VgsFolder, LaunchFileName);
    }

    private static LaunchConfigurationFile CreateDefaultFile()
    {
        return new LaunchConfigurationFile
        {
            ActiveConfiguration = "Debug",
            Configurations = new List<LaunchConfigurationEntry>
            {
                new LaunchConfigurationEntry
                {
                    Name = "Debug",
                    Cwd = "${ProjectDir}",
                    EnableDebugging = true
                }
            }
        };
    }
}
