using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Models;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

public partial class BuildConfigurationDialogViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<BuildConfigurationItem> _configurations = new();

    [ObservableProperty]
    private BuildConfigurationItem? _selectedConfiguration;

    [ObservableProperty]
    private string _newConfigurationName = "";

    public BuildConfigurationDialogViewModel()
    {
        // Initialize with default Debug and Release configurations
        Configurations.Add(new BuildConfigurationItem
        {
            Name = "Debug",
            OutputPath = "bin\\Debug",
            DebugSymbols = true,
            Optimize = false,
            WarningLevel = WarningLevel.Default,
            TreatWarningsAsErrors = false
        });

        Configurations.Add(new BuildConfigurationItem
        {
            Name = "Release",
            OutputPath = "bin\\Release",
            DebugSymbols = false,
            Optimize = true,
            WarningLevel = WarningLevel.High,
            TreatWarningsAsErrors = false
        });

        SelectedConfiguration = Configurations.FirstOrDefault();
    }

    public void LoadFromProject(BasicLangProject? project)
    {
        if (project?.Configurations == null || project.Configurations.Count == 0) return;

        Configurations.Clear();
        foreach (var kvp in project.Configurations)
        {
            var config = kvp.Value;
            Configurations.Add(new BuildConfigurationItem
            {
                Name = config.Name,
                OutputPath = config.OutputPath,
                DebugSymbols = config.DebugSymbols,
                Optimize = config.Optimize,
                DefineConstants = config.DefineConstants,
                WarningLevel = config.WarningLevel,
                TreatWarningsAsErrors = config.TreatWarningsAsErrors
            });
        }

        SelectedConfiguration = Configurations.FirstOrDefault();
    }

    public List<BuildConfiguration> GetConfigurations()
    {
        return Configurations.Select(c => new BuildConfiguration
        {
            Name = c.Name,
            OutputPath = c.OutputPath,
            DebugSymbols = c.DebugSymbols,
            Optimize = c.Optimize,
            DefineConstants = c.DefineConstants,
            WarningLevel = c.WarningLevel,
            TreatWarningsAsErrors = c.TreatWarningsAsErrors
        }).ToList();
    }

    [RelayCommand]
    private void AddConfiguration()
    {
        if (string.IsNullOrWhiteSpace(NewConfigurationName)) return;

        // Check for duplicate
        if (Configurations.Any(c => c.Name.Equals(NewConfigurationName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var newConfig = new BuildConfigurationItem
        {
            Name = NewConfigurationName,
            OutputPath = $"bin\\{NewConfigurationName}",
            DebugSymbols = true,
            Optimize = false,
            WarningLevel = WarningLevel.Default
        };

        Configurations.Add(newConfig);
        SelectedConfiguration = newConfig;
        NewConfigurationName = "";
    }

    [RelayCommand]
    private void RemoveConfiguration()
    {
        if (SelectedConfiguration == null) return;
        if (Configurations.Count <= 1) return; // Keep at least one configuration

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

        var newConfig = new BuildConfigurationItem
        {
            Name = newName,
            OutputPath = $"bin\\{newName.Replace(" ", "")}",
            DebugSymbols = SelectedConfiguration.DebugSymbols,
            Optimize = SelectedConfiguration.Optimize,
            DefineConstants = SelectedConfiguration.DefineConstants,
            WarningLevel = SelectedConfiguration.WarningLevel,
            TreatWarningsAsErrors = SelectedConfiguration.TreatWarningsAsErrors
        };

        Configurations.Add(newConfig);
        SelectedConfiguration = newConfig;
    }
}

public partial class BuildConfigurationItem : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _outputPath = "";

    [ObservableProperty]
    private bool _debugSymbols;

    [ObservableProperty]
    private bool _optimize;

    [ObservableProperty]
    private string? _defineConstants;

    [ObservableProperty]
    private WarningLevel _warningLevel = WarningLevel.Default;

    [ObservableProperty]
    private bool _treatWarningsAsErrors;

    public static IEnumerable<WarningLevel> WarningLevels => Enum.GetValues<WarningLevel>();
}
