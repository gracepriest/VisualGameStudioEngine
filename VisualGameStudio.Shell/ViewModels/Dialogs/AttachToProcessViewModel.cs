using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

public partial class AttachToProcessViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _filterText = "";

    [ObservableProperty]
    private ProcessItemViewModel? _selectedProcess;

    private readonly List<ProcessItemViewModel> _allProcesses = new();

    public ObservableCollection<ProcessItemViewModel> FilteredProcesses { get; } = new();

    /// <summary>
    /// The PID selected by the user, or -1 if cancelled.
    /// </summary>
    public int SelectedProcessId { get; private set; } = -1;

    public event EventHandler? ProcessSelected;
    public event EventHandler? Cancelled;

    public AttachToProcessViewModel()
    {
        RefreshProcessList();
    }

    partial void OnFilterTextChanged(string value)
    {
        ApplyFilter();
    }

    [RelayCommand]
    private void Refresh()
    {
        RefreshProcessList();
    }

    [RelayCommand]
    private void Attach()
    {
        if (SelectedProcess != null)
        {
            SelectedProcessId = SelectedProcess.Pid;
            ProcessSelected?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        SelectedProcessId = -1;
        Cancelled?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshProcessList()
    {
        _allProcesses.Clear();

        try
        {
            var currentPid = Environment.ProcessId;
            var processes = Process.GetProcesses()
                .Where(p => p.Id != currentPid)
                .OrderBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase);

            foreach (var proc in processes)
            {
                try
                {
                    _allProcesses.Add(new ProcessItemViewModel
                    {
                        Pid = proc.Id,
                        Name = proc.ProcessName,
                        Title = TryGetTitle(proc),
                        Type = IsDotNetProcess(proc) ? "Managed" : ""
                    });
                }
                catch
                {
                    // Some processes can't be inspected (access denied)
                }
            }
        }
        catch
        {
            // Ignore enumeration errors
        }

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        FilteredProcesses.Clear();

        var filter = FilterText.Trim();
        foreach (var p in _allProcesses)
        {
            if (string.IsNullOrEmpty(filter)
                || p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || p.Pid.ToString().Contains(filter)
                || (p.Title != null && p.Title.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            {
                FilteredProcesses.Add(p);
            }
        }
    }

    private static string TryGetTitle(Process proc)
    {
        try { return proc.MainWindowTitle; }
        catch { return ""; }
    }

    private static bool IsDotNetProcess(Process proc)
    {
        try
        {
            var mainModule = proc.MainModule?.FileName;
            if (mainModule == null) return false;

            // Check for dotnet.exe host or .dll alongside a .runtimeconfig.json
            var name = Path.GetFileName(mainModule);
            if (name.Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
                return true;

            var runtimeConfig = Path.ChangeExtension(mainModule, ".runtimeconfig.json");
            if (File.Exists(runtimeConfig))
                return true;

            var depsJson = Path.ChangeExtension(mainModule, ".deps.json");
            if (File.Exists(depsJson))
                return true;

            return false;
        }
        catch
        {
            return false;
        }
    }
}

public partial class ProcessItemViewModel : ObservableObject
{
    [ObservableProperty]
    private int _pid;

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string _type = "";
}
