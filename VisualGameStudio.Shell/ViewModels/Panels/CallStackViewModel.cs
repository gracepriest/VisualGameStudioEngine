using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.Shell.ViewModels.Panels;

public partial class CallStackViewModel : Tool
{
    private readonly IDebugService _debugService;

    [ObservableProperty]
    private ObservableCollection<StackFrameItem> _stackFrames = new();

    [ObservableProperty]
    private StackFrameItem? _selectedFrame;

    /// <summary>
    /// When true, consecutive external-code frames are collapsed into a single "[External Code]" entry.
    /// </summary>
    public bool CollapseExternalFrames { get; set; } = true;

    public event EventHandler<StackFrameItem>? FrameSelected;

    public CallStackViewModel(IDebugService debugService)
    {
        _debugService = debugService;
        Id = "CallStack";
        Title = "Call Stack";

        _debugService.Stopped += OnDebugStopped;
        _debugService.StateChanged += OnDebugStateChanged;
    }

    private async void OnDebugStopped(object? sender, StoppedEventArgs e)
    {
        try
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await RefreshStackTraceAsync();
            });
        }
        catch (Exception)
        {
            // Ignore exceptions in event handler to prevent crashes
        }
    }

    private async void OnDebugStateChanged(object? sender, DebugStateChangedEventArgs e)
    {
        try
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (e.NewState == DebugState.Stopped || e.NewState == DebugState.NotStarted)
                {
                    StackFrames.Clear();
                }
            });
        }
        catch (Exception)
        {
            // Ignore exceptions in event handler to prevent crashes
        }
    }

    [RelayCommand]
    private async Task RefreshStackTraceAsync()
    {
        var frames = await _debugService.GetStackTraceAsync();
        StackFrames.Clear();

        bool lastWasExternal = false;

        foreach (var frame in frames)
        {
            bool isExternal = IsExternalCodeFrame(frame);

            // Collapse consecutive external frames into one entry
            if (CollapseExternalFrames && isExternal)
            {
                if (!lastWasExternal)
                {
                    StackFrames.Add(new StackFrameItem
                    {
                        Id = frame.Id,
                        Name = "[External Code]",
                        FilePath = null,
                        Line = 0,
                        Column = 0,
                        IsExternalCode = true,
                        DisplayText = "[External Code]"
                    });
                }
                lastWasExternal = true;
                continue;
            }

            lastWasExternal = false;

            StackFrames.Add(new StackFrameItem
            {
                Id = frame.Id,
                Name = frame.Name,
                FilePath = frame.FilePath,
                Line = frame.Line,
                Column = frame.Column,
                IsExternalCode = isExternal,
                DisplayText = FormatFrameDisplay(frame)
            });
        }
    }

    partial void OnSelectedFrameChanged(StackFrameItem? value)
    {
        if (value != null && !value.IsExternalCode)
        {
            FrameSelected?.Invoke(this, value);
        }
    }

    /// <summary>
    /// Determines if a frame represents external (non-user) code.
    /// A frame is external if it has no source file, the source is not a .bas file,
    /// or its name starts with "[External Code]".
    /// </summary>
    private static bool IsExternalCodeFrame(StackFrameInfo frame)
    {
        // Explicitly marked as external by the debug adapter
        if (frame.Name.StartsWith("[External Code]", StringComparison.OrdinalIgnoreCase))
            return true;

        // No source file means framework/library code
        if (string.IsNullOrEmpty(frame.FilePath))
            return true;

        // Only .bas files are user code
        if (!frame.FilePath.EndsWith(".bas", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static string FormatFrameDisplay(StackFrameInfo frame)
    {
        if (IsExternalCodeFrame(frame))
            return frame.Name;

        var location = frame.FilePath != null
            ? $" at {Path.GetFileName(frame.FilePath)}:{frame.Line}"
            : "";
        return $"{frame.Name}{location}";
    }
}

public class StackFrameItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? FilePath { get; set; }
    public int Line { get; set; }
    public int Column { get; set; }
    public bool IsExternalCode { get; set; }
    public string DisplayText { get; set; } = "";
}
