using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

public partial class LaunchConfigurationDialogViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<LaunchConfigurationItem> _configurations = new();

    [ObservableProperty]
    private LaunchConfigurationItem? _selectedConfiguration;

    [ObservableProperty]
    private string _newConfigurationName = "";

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
