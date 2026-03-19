using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

public partial class LaunchConfigurationDialogViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<LaunchConfigurationItem> _configurations = new();

    [ObservableProperty]
    private LaunchConfigurationItem? _selectedConfiguration;

    [ObservableProperty]
    private string _newConfigurationName = "";

    private string? _activeConfigurationName;

    public LaunchConfigurationDialogViewModel()
    {
        // Default configuration
        Configurations.Add(new LaunchConfigurationItem
        {
            Name = "Default",
            WorkingDirectory = "${ProjectDir}",
            CommandLineArgs = "",
            EnableDebugging = true
        });

        SelectedConfiguration = Configurations.FirstOrDefault();
    }

    /// <summary>
    /// Initialize from a persisted LaunchConfigurationFile.
    /// </summary>
    public LaunchConfigurationDialogViewModel(LaunchConfigurationFile configFile)
    {
        _activeConfigurationName = configFile.ActiveConfiguration;

        foreach (var entry in configFile.Configurations)
        {
            var item = new LaunchConfigurationItem
            {
                Name = entry.Name,
                WorkingDirectory = entry.Cwd,
                CommandLineArgs = entry.Args.Length > 0 ? string.Join(" ", entry.Args) : "",
                EnvironmentVariablesText = string.Join("\n",
                    entry.Env.Select(kv => $"{kv.Key}={kv.Value}")),
                EnableDebugging = entry.EnableDebugging,
                ExternalConsole = entry.ExternalConsole,
                StopOnEntry = entry.StopOnEntry
            };
            Configurations.Add(item);
        }

        if (Configurations.Count == 0)
        {
            Configurations.Add(new LaunchConfigurationItem
            {
                Name = "Debug",
                WorkingDirectory = "${ProjectDir}",
                EnableDebugging = true
            });
        }

        // Select the active configuration, or the first one
        SelectedConfiguration = Configurations.FirstOrDefault(
            c => c.Name.Equals(_activeConfigurationName, StringComparison.OrdinalIgnoreCase))
            ?? Configurations.FirstOrDefault();
    }

    /// <summary>
    /// Convert the current UI state back to a LaunchConfigurationFile for persistence.
    /// </summary>
    public LaunchConfigurationFile ToConfigurationFile()
    {
        return new LaunchConfigurationFile
        {
            ActiveConfiguration = SelectedConfiguration?.Name ?? Configurations.FirstOrDefault()?.Name,
            Configurations = Configurations.Select(c => new LaunchConfigurationEntry
            {
                Name = c.Name,
                Cwd = c.WorkingDirectory,
                Args = string.IsNullOrWhiteSpace(c.CommandLineArgs)
                    ? Array.Empty<string>()
                    : SplitCommandLineArgs(c.CommandLineArgs),
                Env = c.GetEnvironmentVariables(),
                EnableDebugging = c.EnableDebugging,
                ExternalConsole = c.ExternalConsole,
                StopOnEntry = c.StopOnEntry
            }).ToList()
        };
    }

    public List<LaunchConfiguration> GetConfigurations()
    {
        return Configurations.Select(c => new LaunchConfiguration
        {
            Name = c.Name,
            WorkingDirectory = c.WorkingDirectory,
            CommandLineArgs = c.CommandLineArgs,
            EnvironmentVariables = c.GetEnvironmentVariables(),
            EnableDebugging = c.EnableDebugging,
            ExternalConsole = c.ExternalConsole,
            StopOnEntry = c.StopOnEntry
        }).ToList();
    }

    [RelayCommand]
    private void AddConfiguration()
    {
        if (string.IsNullOrWhiteSpace(NewConfigurationName)) return;

        if (Configurations.Any(c => c.Name.Equals(NewConfigurationName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var newConfig = new LaunchConfigurationItem
        {
            Name = NewConfigurationName,
            WorkingDirectory = "${ProjectDir}",
            EnableDebugging = true
        };

        Configurations.Add(newConfig);
        SelectedConfiguration = newConfig;
        NewConfigurationName = "";
    }

    [RelayCommand]
    private void RemoveConfiguration()
    {
        if (SelectedConfiguration == null) return;
        if (Configurations.Count <= 1) return;

        var index = Configurations.IndexOf(SelectedConfiguration);
        Configurations.Remove(SelectedConfiguration);
        SelectedConfiguration = Configurations.ElementAtOrDefault(Math.Max(0, index - 1));
    }

    [RelayCommand]
    private void DuplicateConfiguration()
    {
        if (SelectedConfiguration == null) return;

        var baseName = SelectedConfiguration.Name;
        var copyNum = 1;
        var newName = $"{baseName} (Copy)";

        while (Configurations.Any(c => c.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
        {
            copyNum++;
            newName = $"{baseName} (Copy {copyNum})";
        }

        var newConfig = new LaunchConfigurationItem
        {
            Name = newName,
            WorkingDirectory = SelectedConfiguration.WorkingDirectory,
            CommandLineArgs = SelectedConfiguration.CommandLineArgs,
            EnvironmentVariablesText = SelectedConfiguration.EnvironmentVariablesText,
            EnableDebugging = SelectedConfiguration.EnableDebugging,
            ExternalConsole = SelectedConfiguration.ExternalConsole,
            StopOnEntry = SelectedConfiguration.StopOnEntry
        };

        Configurations.Add(newConfig);
        SelectedConfiguration = newConfig;
    }

    /// <summary>
    /// Splits a command line string into individual arguments, respecting quoted strings.
    /// </summary>
    private static string[] SplitCommandLineArgs(string commandLine)
    {
        var args = new List<string>();
        var current = "";
        var inQuotes = false;

        for (int i = 0; i < commandLine.Length; i++)
        {
            var ch = commandLine[i];

            if (ch == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (ch == ' ' && !inQuotes)
            {
                if (current.Length > 0)
                {
                    args.Add(current);
                    current = "";
                }
            }
            else
            {
                current += ch;
            }
        }

        if (current.Length > 0)
            args.Add(current);

        return args.ToArray();
    }
}

public partial class LaunchConfigurationItem : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _workingDirectory = "";

    [ObservableProperty]
    private string _commandLineArgs = "";

    [ObservableProperty]
    private string _environmentVariablesText = "";

    [ObservableProperty]
    private bool _enableDebugging = true;

    [ObservableProperty]
    private bool _externalConsole;

    [ObservableProperty]
    private bool _stopOnEntry;

    public Dictionary<string, string> GetEnvironmentVariables()
    {
        var result = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(EnvironmentVariablesText)) return result;

        var lines = EnvironmentVariablesText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#')) continue;

            var eqIndex = trimmed.IndexOf('=');
            if (eqIndex > 0)
            {
                var key = trimmed.Substring(0, eqIndex).Trim();
                var value = trimmed.Substring(eqIndex + 1).Trim();
                if (!string.IsNullOrEmpty(key))
                {
                    result[key] = value;
                }
            }
        }

        return result;
    }
}

public class LaunchConfiguration
{
    public string Name { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public string CommandLineArgs { get; set; } = "";
    public Dictionary<string, string> EnvironmentVariables { get; set; } = new();
    public bool EnableDebugging { get; set; } = true;
    public bool ExternalConsole { get; set; }
    public bool StopOnEntry { get; set; }
}
