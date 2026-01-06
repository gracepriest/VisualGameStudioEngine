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

        foreach (var frame in frames)
        {
            StackFrames.Add(new StackFrameItem
            {
                Id = frame.Id,
                Name = frame.Name,
                FilePath = frame.FilePath,
                Line = frame.Line,
                Column = frame.Column,
                DisplayText = FormatFrameDisplay(frame)
            });
        }
    }

    partial void OnSelectedFrameChanged(StackFrameItem? value)
    {
        if (value != null)
        {
            FrameSelected?.Invoke(this, value);
        }
    }

    private static string FormatFrameDisplay(StackFrameInfo frame)
    {
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
    public string DisplayText { get; set; } = "";
}
