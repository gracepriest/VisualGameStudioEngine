using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VisualGameStudio.Shell.ViewModels.Panels;

/// <summary>
/// Type of entry in the debug console output.
/// </summary>
public enum DebugConsoleEntryType
{
    Input,
    Output,
    Error,
    Info,
    Warning
}

/// <summary>
/// Represents a single entry (line) in the debug console output.
/// Supports expandable objects/arrays with child properties.
/// </summary>
public partial class DebugConsoleEntry : ObservableObject
{
    [ObservableProperty]
    private DebugConsoleEntryType _entryType;

    [ObservableProperty]
    private string _text = "";

    /// <summary>
    /// The type name for structured results (e.g., "MyClass", "Int32[]").
    /// </summary>
    [ObservableProperty]
    private string _typeName = "";

    /// <summary>
    /// DAP variablesReference — non-zero means this value has children that can be fetched.
    /// </summary>
    [ObservableProperty]
    private int _variablesReference;

    /// <summary>
    /// Whether this entry can be expanded to show child properties.
    /// </summary>
    [ObservableProperty]
    private bool _isExpandable;

    /// <summary>
    /// Whether the entry is currently expanded in the tree view.
    /// </summary>
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>
    /// Child entries (object fields, array elements) loaded on demand.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<DebugConsoleEntry> _children = new();

    /// <summary>
    /// Whether children have been fetched from the debugger.
    /// </summary>
    public bool ChildrenLoaded { get; set; }

    /// <summary>
    /// Timestamp of when this entry was created.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;
}
