using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Editor.Margins;

namespace VisualGameStudio.Shell.ViewModels.Panels;

public partial class BreakpointsViewModel : Tool
{
    private readonly IDebugService _debugService;
    private readonly Dictionary<string, List<BreakpointItem>> _breakpointsByFile = new();
    private readonly List<FunctionBreakpointItem> _functionBreakpointsList = new();
    private readonly List<DataBreakpointItem> _dataBreakpointsList = new();
    private string? _projectDirectory;

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [ObservableProperty]
    private ObservableCollection<BreakpointItem> _breakpoints = new();

    [ObservableProperty]
    private ObservableCollection<FunctionBreakpointItem> _functionBreakpoints = new();

    [ObservableProperty]
    private ObservableCollection<DataBreakpointItem> _dataBreakpoints = new();

    [ObservableProperty]
    private BreakpointItem? _selectedBreakpoint;

    [ObservableProperty]
    private FunctionBreakpointItem? _selectedFunctionBreakpoint;

    [ObservableProperty]
    private DataBreakpointItem? _selectedDataBreakpoint;

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
        bool anyChanged = false;

        // Determine which files to search for matching breakpoints
        IEnumerable<List<BreakpointItem>> bpLists;
        if (!string.IsNullOrEmpty(e.FilePath) && _breakpointsByFile.TryGetValue(e.FilePath, out var fileBps))
        {
            bpLists = new[] { fileBps };
        }
        else if (string.IsNullOrEmpty(e.FilePath))
        {
            // No file path in event (e.g., DAP breakpoint event without source) — search all files by ID
            bpLists = _breakpointsByFile.Values;
        }
        else
        {
            bpLists = Enumerable.Empty<List<BreakpointItem>>();
        }

        foreach (var bps in bpLists)
        {
            foreach (var bp in bps)
            {
                var verified = e.Breakpoints.FirstOrDefault(b => b.Line == bp.Line && b.Line > 0);
                if (verified != null)
                {
                    bp.Id = verified.Id;
                    bp.IsVerified = verified.Verified;
                    bp.Message = verified.Message;
                    anyChanged = true;
                }
                else if (e.Breakpoints.Count > 0)
                {
                    // Also try matching by ID for breakpoint events that don't carry line info
                    var byId = e.Breakpoints.FirstOrDefault(b => b.Id == bp.Id && b.Id != 0);
                    if (byId != null)
                    {
                        bp.IsVerified = byId.Verified;
                        bp.Message = byId.Message;
                        if (byId.Line > 0) bp.Line = byId.Line;
                        anyChanged = true;
                    }
                }
            }
        }

        if (anyChanged)
        {
            // Notify the UI to refresh breakpoint visuals (e.g., filled vs hollow circles)
            BreakpointsChanged?.Invoke(this, EventArgs.Empty);
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
            // Remove existing (toggle off)
            list.Remove(existing);
            Breakpoints.Remove(existing);
        }
        else
        {
            // Add new (toggle on)
            list.Add(bp);
            Breakpoints.Add(bp);
        }

        SyncBreakpointsToDebugger(filePath);
        BreakpointsChanged?.Invoke(this, EventArgs.Empty);
        _ = SaveBreakpointsAsync();
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
                _ = SaveBreakpointsAsync();
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
                _ = SaveBreakpointsAsync();
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

    /// <summary>
    /// Gets breakpoint visual info for a file, including verified/unverified state and kind.
    /// Used by the editor margin to render filled vs hollow breakpoint indicators.
    /// When not debugging, all breakpoints show as verified (filled circles).
    /// During debugging, unverified breakpoints show as hollow circles until the debugger binds them.
    /// </summary>
    public Dictionary<int, BreakpointVisualInfo> GetBreakpointVisualsForFile(string filePath)
    {
        var isDebugging = _debugService.IsDebugging;
        var result = new Dictionary<int, BreakpointVisualInfo>();
        if (_breakpointsByFile.TryGetValue(filePath, out var list))
        {
            foreach (var bp in list)
            {
                var kind = BreakpointKind.Normal;
                if (!string.IsNullOrEmpty(bp.LogMessage))
                    kind = BreakpointKind.Logpoint;
                else if (!string.IsNullOrEmpty(bp.HitCondition))
                    kind = BreakpointKind.HitCount;
                else if (!string.IsNullOrEmpty(bp.Condition))
                    kind = BreakpointKind.Conditional;

                result[bp.Line] = new BreakpointVisualInfo
                {
                    IsEnabled = bp.IsEnabled,
                    // When not debugging, always show as verified (filled circle).
                    // During debugging, show actual verified state from the debug adapter.
                    IsVerified = isDebugging ? bp.IsVerified : true,
                    Kind = kind
                };
            }
        }
        return result;
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
        _ = SaveBreakpointsAsync();
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
        _ = SaveBreakpointsAsync();
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
        _ = SaveBreakpointsAsync();
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
        _ = SaveBreakpointsAsync();
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
        _ = SaveBreakpointsAsync();
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

        // Sync data breakpoints
        await SyncDataBreakpointsToDebuggerAsync();
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
        _ = SaveBreakpointsAsync();
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
        _ = SaveBreakpointsAsync();
    }

    [RelayCommand]
    private async Task RemoveFunctionBreakpointAsync(FunctionBreakpointItem? breakpoint)
    {
        if (breakpoint == null) return;

        _functionBreakpointsList.Remove(breakpoint);
        FunctionBreakpoints.Remove(breakpoint);

        await SyncFunctionBreakpointsToDebuggerAsync();
        BreakpointsChanged?.Invoke(this, EventArgs.Empty);
        _ = SaveBreakpointsAsync();
    }

    [RelayCommand]
    private async Task ToggleFunctionBreakpointAsync(FunctionBreakpointItem? breakpoint)
    {
        if (breakpoint == null) return;

        breakpoint.IsEnabled = !breakpoint.IsEnabled;

        await SyncFunctionBreakpointsToDebuggerAsync();
        BreakpointsChanged?.Invoke(this, EventArgs.Empty);
        _ = SaveBreakpointsAsync();
    }

    [RelayCommand]
    private async Task RemoveAllFunctionBreakpointsAsync()
    {
        _functionBreakpointsList.Clear();
        FunctionBreakpoints.Clear();

        await SyncFunctionBreakpointsToDebuggerAsync();
        BreakpointsChanged?.Invoke(this, EventArgs.Empty);
        _ = SaveBreakpointsAsync();
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

    // Data breakpoint methods
    public async Task AddDataBreakpointAsync(string dataId, string variableName, string accessType, string? condition = null, string? hitCondition = null)
    {
        if (string.IsNullOrWhiteSpace(dataId)) return;

        // Check if already exists
        if (_dataBreakpointsList.Any(db => db.DataId == dataId))
            return;

        var bp = new DataBreakpointItem
        {
            DataId = dataId,
            VariableName = variableName,
            AccessType = accessType,
            Condition = condition,
            HitCondition = hitCondition,
            IsEnabled = true
        };

        _dataBreakpointsList.Add(bp);
        DataBreakpoints.Add(bp);

        await SyncDataBreakpointsToDebuggerAsync();
        BreakpointsChanged?.Invoke(this, EventArgs.Empty);
        _ = SaveBreakpointsAsync();
    }

    [RelayCommand]
    private async Task RemoveDataBreakpointAsync(DataBreakpointItem? breakpoint)
    {
        if (breakpoint == null) return;

        _dataBreakpointsList.Remove(breakpoint);
        DataBreakpoints.Remove(breakpoint);

        await SyncDataBreakpointsToDebuggerAsync();
        BreakpointsChanged?.Invoke(this, EventArgs.Empty);
        _ = SaveBreakpointsAsync();
    }

    [RelayCommand]
    private async Task ToggleDataBreakpointAsync(DataBreakpointItem? breakpoint)
    {
        if (breakpoint == null) return;

        breakpoint.IsEnabled = !breakpoint.IsEnabled;

        await SyncDataBreakpointsToDebuggerAsync();
        BreakpointsChanged?.Invoke(this, EventArgs.Empty);
        _ = SaveBreakpointsAsync();
    }

    [RelayCommand]
    private async Task RemoveAllDataBreakpointsAsync()
    {
        _dataBreakpointsList.Clear();
        DataBreakpoints.Clear();

        await SyncDataBreakpointsToDebuggerAsync();
        BreakpointsChanged?.Invoke(this, EventArgs.Empty);
        _ = SaveBreakpointsAsync();
    }

    public async Task SyncDataBreakpointsToDebuggerAsync()
    {
        if (!_debugService.IsDebugging) return;

        var breakpoints = _dataBreakpointsList
            .Where(db => db.IsEnabled)
            .Select(db => new DataBreakpoint
            {
                DataId = db.DataId,
                AccessType = db.AccessType,
                Condition = db.Condition,
                HitCondition = db.HitCondition
            })
            .ToList();

        var result = await _debugService.SetDataBreakpointsAsync(breakpoints);

        // Update verified status
        var resultList = result.ToList();
        var enabledList = _dataBreakpointsList.Where(db => db.IsEnabled).ToList();
        for (int i = 0; i < enabledList.Count && i < resultList.Count; i++)
        {
            enabledList[i].Id = resultList[i].Id;
            enabledList[i].IsVerified = resultList[i].Verified;
            enabledList[i].Message = resultList[i].Message;
        }
    }

    // --- Breakpoint Persistence ---

    public void SetProjectDirectory(string path)
    {
        _projectDirectory = path;
    }

    private string? GetPersistencePath()
    {
        if (string.IsNullOrEmpty(_projectDirectory)) return null;
        return Path.Combine(_projectDirectory, ".vgs", "breakpoints.json");
    }

    public async Task SaveBreakpointsAsync()
    {
        var filePath = GetPersistencePath();
        if (filePath == null) return;

        try
        {
            var data = new BreakpointsPersistenceData
            {
                Version = 1,
                Breakpoints = Breakpoints.Select(bp => new BreakpointPersistenceItem
                {
                    FilePath = bp.FilePath,
                    Line = bp.Line,
                    IsEnabled = bp.IsEnabled,
                    Condition = bp.Condition,
                    HitCondition = bp.HitCondition,
                    LogMessage = bp.LogMessage
                }).ToList(),
                FunctionBreakpoints = _functionBreakpointsList.Select(fb => new FunctionBreakpointPersistenceItem
                {
                    Name = fb.FunctionName,
                    IsEnabled = fb.IsEnabled,
                    Condition = fb.Condition,
                    HitCondition = fb.HitCondition
                }).ToList(),
                DataBreakpoints = _dataBreakpointsList.Select(db => new DataBreakpointPersistenceItem
                {
                    DataId = db.DataId,
                    VariableName = db.VariableName,
                    AccessType = db.AccessType,
                    IsEnabled = db.IsEnabled,
                    Condition = db.Condition,
                    HitCondition = db.HitCondition
                }).ToList()
            };

            var dir = Path.GetDirectoryName(filePath)!;
            Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(data, s_jsonOptions);
            await File.WriteAllTextAsync(filePath, json);
        }
        catch (Exception)
        {
            // Ignore persistence errors — non-critical
        }
    }

    public async Task LoadBreakpointsAsync()
    {
        var filePath = GetPersistencePath();
        if (filePath == null || !File.Exists(filePath)) return;

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            var data = JsonSerializer.Deserialize<BreakpointsPersistenceData>(json, s_jsonOptions);
            if (data == null) return;

            // Clear existing
            _breakpointsByFile.Clear();
            Breakpoints.Clear();
            _functionBreakpointsList.Clear();
            FunctionBreakpoints.Clear();
            _dataBreakpointsList.Clear();
            DataBreakpoints.Clear();

            // Load source breakpoints
            foreach (var item in data.Breakpoints)
            {
                var bp = new BreakpointItem
                {
                    FilePath = item.FilePath,
                    FileName = Path.GetFileName(item.FilePath),
                    Line = item.Line,
                    IsEnabled = item.IsEnabled,
                    Condition = item.Condition,
                    HitCondition = item.HitCondition,
                    LogMessage = item.LogMessage
                };

                if (!_breakpointsByFile.TryGetValue(item.FilePath, out var list))
                {
                    list = new List<BreakpointItem>();
                    _breakpointsByFile[item.FilePath] = list;
                }
                list.Add(bp);
                Breakpoints.Add(bp);
            }

            // Load function breakpoints
            foreach (var item in data.FunctionBreakpoints)
            {
                var fb = new FunctionBreakpointItem
                {
                    FunctionName = item.Name,
                    IsEnabled = item.IsEnabled,
                    Condition = item.Condition,
                    HitCondition = item.HitCondition
                };

                _functionBreakpointsList.Add(fb);
                FunctionBreakpoints.Add(fb);
            }

            // Load data breakpoints
            foreach (var item in data.DataBreakpoints)
            {
                var db = new DataBreakpointItem
                {
                    DataId = item.DataId,
                    VariableName = item.VariableName,
                    AccessType = item.AccessType,
                    IsEnabled = item.IsEnabled,
                    Condition = item.Condition,
                    HitCondition = item.HitCondition
                };

                _dataBreakpointsList.Add(db);
                DataBreakpoints.Add(db);
            }

            BreakpointsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception)
        {
            // Ignore deserialization errors — start with empty breakpoints
        }
    }

    // --- Persistence Models ---

    private class BreakpointsPersistenceData
    {
        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;

        [JsonPropertyName("breakpoints")]
        public List<BreakpointPersistenceItem> Breakpoints { get; set; } = new();

        [JsonPropertyName("functionBreakpoints")]
        public List<FunctionBreakpointPersistenceItem> FunctionBreakpoints { get; set; } = new();

        [JsonPropertyName("dataBreakpoints")]
        public List<DataBreakpointPersistenceItem> DataBreakpoints { get; set; } = new();
    }

    private class BreakpointPersistenceItem
    {
        [JsonPropertyName("filePath")]
        public string FilePath { get; set; } = "";

        [JsonPropertyName("line")]
        public int Line { get; set; }

        [JsonPropertyName("isEnabled")]
        public bool IsEnabled { get; set; } = true;

        [JsonPropertyName("condition")]
        public string? Condition { get; set; }

        [JsonPropertyName("hitCondition")]
        public string? HitCondition { get; set; }

        [JsonPropertyName("logMessage")]
        public string? LogMessage { get; set; }
    }

    private class FunctionBreakpointPersistenceItem
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("isEnabled")]
        public bool IsEnabled { get; set; } = true;

        [JsonPropertyName("condition")]
        public string? Condition { get; set; }

        [JsonPropertyName("hitCondition")]
        public string? HitCondition { get; set; }
    }

    private class DataBreakpointPersistenceItem
    {
        [JsonPropertyName("dataId")]
        public string DataId { get; set; } = "";

        [JsonPropertyName("variableName")]
        public string VariableName { get; set; } = "";

        [JsonPropertyName("accessType")]
        public string AccessType { get; set; } = "write";

        [JsonPropertyName("isEnabled")]
        public bool IsEnabled { get; set; } = true;

        [JsonPropertyName("condition")]
        public string? Condition { get; set; }

        [JsonPropertyName("hitCondition")]
        public string? HitCondition { get; set; }
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

public partial class DataBreakpointItem : ObservableObject
{
    public int Id { get; set; }

    [ObservableProperty]
    private string _dataId = "";

    [ObservableProperty]
    private string _variableName = "";

    [ObservableProperty]
    private string _accessType = "write";

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

    public string DisplayText => $"{VariableName} ({AccessType})" + (Condition != null ? $" (when: {Condition})" : "");
}
