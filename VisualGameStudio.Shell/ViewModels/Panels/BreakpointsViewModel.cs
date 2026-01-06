using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.Shell.ViewModels.Panels;

public partial class BreakpointsViewModel : Tool
{
    private readonly IDebugService _debugService;
    private readonly Dictionary<string, List<BreakpointItem>> _breakpointsByFile = new();
    private readonly List<FunctionBreakpointItem> _functionBreakpointsList = new();

    [ObservableProperty]
    private ObservableCollection<BreakpointItem> _breakpoints = new();

    [ObservableProperty]
    private ObservableCollection<FunctionBreakpointItem> _functionBreakpoints = new();

    [ObservableProperty]
    private BreakpointItem? _selectedBreakpoint;

    [ObservableProperty]
    private FunctionBreakpointItem? _selectedFunctionBreakpoint;

    [ObservableProperty]
    private string _newFunctionName = "";

    public event EventHandler<BreakpointItem>? BreakpointNavigated;
    public event EventHandler<BreakpointItem>? EditConditionRequested;
    public event EventHandler<FunctionBreakpointItem>? EditFunctionConditionRequested;
    public event EventHandler? BreakpointsChanged;

    public BreakpointsViewModel(IDebugService debugService)
    {
        _debugService = debugService;
        Id = "Breakpoints";
        Title = "Breakpoints";

        _debugService.BreakpointsChanged += OnBreakpointsChanged;
    }

    private void OnBreakpointsChanged(object? sender, BreakpointsChangedEventArgs e)
    {
        // Update verified status from debugger
        if (_breakpointsByFile.TryGetValue(e.FilePath, out var bps))
        {
            foreach (var bp in bps)
            {
                var verified = e.Breakpoints.FirstOrDefault(b => b.Line == bp.Line);
                if (verified != null)
                {
                    bp.Id = verified.Id;
                    bp.IsVerified = verified.Verified;
                    bp.Message = verified.Message;
                }
            }
        }
    }

    public void AddBreakpoint(string filePath, int line, string? condition = null, string? hitCondition = null, string? logMessage = null)
    {
        var bp = new BreakpointItem
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            Line = line,
            Condition = condition,
            HitCondition = hitCondition,
            LogMessage = logMessage,
            IsEnabled = true
        };

        if (!_breakpointsByFile.TryGetValue(filePath, out var list))
        {
            list = new List<BreakpointItem>();
            _breakpointsByFile[filePath] = list;
        }

        // Check if breakpoint already exists at this line
        var existing = list.FirstOrDefault(b => b.Line == line);
        if (existing != null)
        {
            // Remove existing
            list.Remove(existing);
            Breakpoints.Remove(existing);
        }
        else
        {
            list.Add(bp);
            Breakpoints.Add(bp);
        }

        SyncBreakpointsToDebugger(filePath);
        BreakpointsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveBreakpoint(string filePath, int line)
    {
        if (_breakpointsByFile.TryGetValue(filePath, out var list))
        {
            var bp = list.FirstOrDefault(b => b.Line == line);
            if (bp != null)
            {
                list.Remove(bp);
                Breakpoints.Remove(bp);
                SyncBreakpointsToDebugger(filePath);
                BreakpointsChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public void ToggleBreakpoint(string filePath, int line)
    {
        if (_breakpointsByFile.TryGetValue(filePath, out var list))
        {
            var bp = list.FirstOrDefault(b => b.Line == line);
            if (bp != null)
            {
                bp.IsEnabled = !bp.IsEnabled;
                SyncBreakpointsToDebugger(filePath);
                BreakpointsChanged?.Invoke(this, EventArgs.Empty);
                return;
            }
        }

        // Add new breakpoint
        AddBreakpoint(filePath, line);
    }

    public bool HasBreakpoint(string filePath, int line)
    {
        if (_breakpointsByFile.TryGetValue(filePath, out var list))
        {
            return list.Any(b => b.Line == line && b.IsEnabled);
        }
        return false;
    }

    public IEnumerable<BreakpointItem> GetBreakpointsForFile(string filePath)
    {
        if (_breakpointsByFile.TryGetValue(filePath, out var list))
        {
            return list.Where(b => b.IsEnabled);
        }
        return Enumerable.Empty<BreakpointItem>();
    }

    public Dictionary<string, IEnumerable<SourceBreakpoint>> GetAllBreakpoints()
    {
        var result = new Dictionary<string, IEnumerable<SourceBreakpoint>>();
        foreach (var kvp in _breakpointsByFile)
        {
            var bps = kvp.Value
                .Where(b => b.IsEnabled)
                .Select(b => new SourceBreakpoint
                {
                    Line = b.Line,
                    Condition = b.Condition,
                    HitCondition = b.HitCondition,
                    LogMessage = b.LogMessage
                })
                .ToList();
            if (bps.Any())
            {
                result[kvp.Key] = bps;
            }
        }
        return result;
    }

    [RelayCommand]
    private void NavigateToBreakpoint(BreakpointItem? breakpoint)
    {
        if (breakpoint != null)
        {
            BreakpointNavigated?.Invoke(this, breakpoint);
        }
    }

    [RelayCommand]
    private void RemoveBreakpoint(BreakpointItem? breakpoint)
    {
        if (breakpoint != null)
        {
            RemoveBreakpoint(breakpoint.FilePath, breakpoint.Line);
        }
    }

    [RelayCommand]
    private void EditCondition(BreakpointItem? breakpoint)
    {
        if (breakpoint != null)
        {
            EditConditionRequested?.Invoke(this, breakpoint);
        }
    }

    public void UpdateBreakpointCondition(BreakpointItem breakpoint, string? condition, string? hitCondition, string? logMessage)
    {
        breakpoint.Condition = condition;
        breakpoint.HitCondition = hitCondition;
        breakpoint.LogMessage = logMessage;
        SyncBreakpointsToDebugger(breakpoint.FilePath);
        BreakpointsChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void EditFunctionCondition(FunctionBreakpointItem? breakpoint)
    {
        if (breakpoint != null)
        {
            EditFunctionConditionRequested?.Invoke(this, breakpoint);
        }
    }

    public async Task UpdateFunctionBreakpointConditionAsync(FunctionBreakpointItem breakpoint, string? condition, string? hitCondition)
    {
        breakpoint.Condition = condition;
        breakpoint.HitCondition = hitCondition;
        await SyncFunctionBreakpointsToDebuggerAsync();
        BreakpointsChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void RemoveAllBreakpoints()
    {
        var files = _breakpointsByFile.Keys.ToList();
        _breakpointsByFile.Clear();
        Breakpoints.Clear();

        foreach (var file in files)
        {
            SyncBreakpointsToDebugger(file);
        }

        BreakpointsChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void EnableAllBreakpoints()
    {
        foreach (var bp in Breakpoints)
        {
            bp.IsEnabled = true;
        }

        foreach (var file in _breakpointsByFile.Keys)
        {
            SyncBreakpointsToDebugger(file);
        }

        BreakpointsChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void DisableAllBreakpoints()
    {
        foreach (var bp in Breakpoints)
        {
            bp.IsEnabled = false;
        }

        foreach (var file in _breakpointsByFile.Keys)
        {
            SyncBreakpointsToDebugger(file);
        }

        BreakpointsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SyncBreakpointsToDebugger(string filePath)
    {
        if (!_debugService.IsDebugging) return;

        var breakpoints = GetBreakpointsForFile(filePath)
            .Select(bp => new SourceBreakpoint
            {
                Line = bp.Line,
                Condition = bp.Condition,
                HitCondition = bp.HitCondition,
                LogMessage = bp.LogMessage
            });

        // Fire and forget with exception handling
        _ = SyncBreakpointsToDebuggerAsync(filePath, breakpoints);
    }

    private async Task SyncBreakpointsToDebuggerAsync(string filePath, IEnumerable<SourceBreakpoint> breakpoints)
    {
        try
        {
            await _debugService.SetBreakpointsAsync(filePath, breakpoints);
        }
        catch (Exception)
        {
            // Ignore exceptions when syncing breakpoints
        }
    }

    public async Task SyncAllBreakpointsAsync()
    {
        foreach (var file in _breakpointsByFile.Keys)
        {
            var breakpoints = GetBreakpointsForFile(file)
                .Select(bp => new SourceBreakpoint
                {
                    Line = bp.Line,
                    Condition = bp.Condition,
                    HitCondition = bp.HitCondition,
                    LogMessage = bp.LogMessage
                });

            await _debugService.SetBreakpointsAsync(file, breakpoints);
        }

        // Sync function breakpoints
        await SyncFunctionBreakpointsToDebuggerAsync();
    }

    // Function breakpoint methods
    [RelayCommand]
    private async Task AddFunctionBreakpointAsync()
    {
        if (string.IsNullOrWhiteSpace(NewFunctionName)) return;

        var funcName = NewFunctionName.Trim();

        // Check if already exists
        if (_functionBreakpointsList.Any(fb => fb.FunctionName.Equals(funcName, StringComparison.OrdinalIgnoreCase)))
        {
            NewFunctionName = "";
            return;
        }

        var bp = new FunctionBreakpointItem
        {
            FunctionName = funcName,
            IsEnabled = true
        };

        _functionBreakpointsList.Add(bp);
        FunctionBreakpoints.Add(bp);
        NewFunctionName = "";

        await SyncFunctionBreakpointsToDebuggerAsync();
        BreakpointsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task AddFunctionBreakpointAsync(string functionName, string? condition = null, string? hitCondition = null)
    {
        if (string.IsNullOrWhiteSpace(functionName)) return;

        var funcName = functionName.Trim();

        // Check if already exists
        if (_functionBreakpointsList.Any(fb => fb.FunctionName.Equals(funcName, StringComparison.OrdinalIgnoreCase)))
            return;

        var bp = new FunctionBreakpointItem
        {
            FunctionName = funcName,
            Condition = condition,
            HitCondition = hitCondition,
            IsEnabled = true
        };

        _functionBreakpointsList.Add(bp);
        FunctionBreakpoints.Add(bp);

        await SyncFunctionBreakpointsToDebuggerAsync();
        BreakpointsChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task RemoveFunctionBreakpointAsync(FunctionBreakpointItem? breakpoint)
    {
        if (breakpoint == null) return;

        _functionBreakpointsList.Remove(breakpoint);
        FunctionBreakpoints.Remove(breakpoint);

        await SyncFunctionBreakpointsToDebuggerAsync();
        BreakpointsChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task ToggleFunctionBreakpointAsync(FunctionBreakpointItem? breakpoint)
    {
        if (breakpoint == null) return;

        breakpoint.IsEnabled = !breakpoint.IsEnabled;

        await SyncFunctionBreakpointsToDebuggerAsync();
        BreakpointsChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task RemoveAllFunctionBreakpointsAsync()
    {
        _functionBreakpointsList.Clear();
        FunctionBreakpoints.Clear();

        await SyncFunctionBreakpointsToDebuggerAsync();
        BreakpointsChanged?.Invoke(this, EventArgs.Empty);
    }

    public IEnumerable<FunctionBreakpoint> GetAllFunctionBreakpoints()
    {
        return _functionBreakpointsList
            .Where(fb => fb.IsEnabled)
            .Select(fb => new FunctionBreakpoint
            {
                Name = fb.FunctionName,
                Condition = fb.Condition,
                HitCondition = fb.HitCondition
            });
    }

    private async Task SyncFunctionBreakpointsToDebuggerAsync()
    {
        if (!_debugService.IsDebugging) return;

        var breakpoints = GetAllFunctionBreakpoints();
        var result = await _debugService.SetFunctionBreakpointsAsync(breakpoints);

        // Update verified status
        var resultList = result.ToList();
        for (int i = 0; i < _functionBreakpointsList.Count && i < resultList.Count; i++)
        {
            var fb = _functionBreakpointsList.Where(f => f.IsEnabled).ElementAtOrDefault(i);
            if (fb != null && i < resultList.Count)
            {
                fb.Id = resultList[i].Id;
                fb.IsVerified = resultList[i].Verified;
                fb.Message = resultList[i].Message;
            }
        }
    }
}

public partial class BreakpointItem : ObservableObject
{
    public int Id { get; set; }

    [ObservableProperty]
    private string _filePath = "";

    [ObservableProperty]
    private string _fileName = "";

    [ObservableProperty]
    private int _line;

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private bool _isVerified;

    [ObservableProperty]
    private string? _condition;

    [ObservableProperty]
    private string? _hitCondition;

    [ObservableProperty]
    private string? _logMessage;

    [ObservableProperty]
    private string? _message;

    public string DisplayText => $"{FileName}:{Line}" + (Condition != null ? $" (when: {Condition})" : "");
}

public partial class FunctionBreakpointItem : ObservableObject
{
    public int Id { get; set; }

    [ObservableProperty]
    private string _functionName = "";

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private bool _isVerified;

    [ObservableProperty]
    private string? _condition;

    [ObservableProperty]
    private string? _hitCondition;

    [ObservableProperty]
    private string? _message;

    public string DisplayText => FunctionName + (Condition != null ? $" (when: {Condition})" : "");
}
