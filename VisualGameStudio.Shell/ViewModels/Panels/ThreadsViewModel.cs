using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.Shell.ViewModels.Panels;

public partial class ThreadsViewModel : Tool, IDisposable
{
    private readonly IDebugService _debugService;

    [ObservableProperty]
    private ObservableCollection<ThreadItem> _threads = new();

    [ObservableProperty]
    private ThreadItem? _selectedThread;

    [ObservableProperty]
    private bool _isDebugging;

    /// <summary>
    /// Fires when the user switches to a different thread, so the call stack and variables
    /// can be refreshed for that thread.
    /// </summary>
    public event EventHandler<ThreadItem>? ThreadSwitched;

    public ThreadsViewModel(IDebugService debugService)
    {
        _debugService = debugService;
        Id = "Threads";
        Title = "Threads";

        _debugService.Stopped += OnDebugStopped;
        _debugService.StateChanged += OnDebugStateChanged;
    }

    private async void OnDebugStopped(object? sender, StoppedEventArgs e)
    {
        try
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                IsDebugging = true;
                await RefreshThreadsAsync(e.ThreadId);
            });
        }
        catch (Exception)
        {
            // Ignore exceptions in event handler to prevent crashes
        }
    }

    private void OnDebugStateChanged(object? sender, DebugStateChangedEventArgs e)
    {
        try
        {
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (e.NewState == DebugState.Running)
                {
                    IsDebugging = true;
                }
                else if (e.NewState == DebugState.Stopped || e.NewState == DebugState.NotStarted)
                {
                    IsDebugging = false;
                    Threads.Clear();
                    SelectedThread = null;
                }
            });
        }
        catch (Exception)
        {
            // Ignore exceptions in event handler to prevent crashes
        }
    }

    [RelayCommand]
    private async Task RefreshThreadsAsync(int stoppedThreadId = 0)
    {
        var threadInfos = await _debugService.GetThreadsAsync();
        Threads.Clear();

        foreach (var info in threadInfos)
        {
            var item = new ThreadItem
            {
                Id = info.Id,
                Name = info.Name,
                Status = info.Status,
                IsCurrent = info.Id == stoppedThreadId,
                IsFrozen = info.IsFrozen,
                CallStackPreview = info.CallStackPreview
            };
            Threads.Add(item);
        }

        // Select the stopped thread (or the first one)
        SelectedThread = Threads.FirstOrDefault(t => t.IsCurrent)
                      ?? Threads.FirstOrDefault();
    }

    [RelayCommand]
    private async Task SwitchThread(ThreadItem? thread)
    {
        if (thread == null) return;

        // Mark the new thread as current
        foreach (var t in Threads)
        {
            t.IsCurrent = t.Id == thread.Id;
        }

        SelectedThread = thread;
        ThreadSwitched?.Invoke(this, thread);
    }

    [RelayCommand]
    private void FreezeThread(ThreadItem? thread)
    {
        if (thread == null) return;
        thread.IsFrozen = true;
        thread.Status = "Frozen";
    }

    [RelayCommand]
    private void ThawThread(ThreadItem? thread)
    {
        if (thread == null) return;
        thread.IsFrozen = false;
        thread.Status = "Paused";
    }

    partial void OnSelectedThreadChanged(ThreadItem? oldValue, ThreadItem? newValue)
    {
        // Only fire switch when user explicitly changes selection, not programmatic updates
        // The SwitchThread command handles the explicit user action
    }

    public void Dispose()
    {
        _debugService.Stopped -= OnDebugStopped;
        _debugService.StateChanged -= OnDebugStateChanged;
    }
}

public class ThreadItem : ObservableObject
{
    private bool _isCurrent;
    private bool _isFrozen;
    private string _status = "";

    public int Id { get; set; }
    public string Name { get; set; } = "";

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public bool IsCurrent
    {
        get => _isCurrent;
        set => SetProperty(ref _isCurrent, value);
    }

    public bool IsFrozen
    {
        get => _isFrozen;
        set => SetProperty(ref _isFrozen, value);
    }

    public string CallStackPreview { get; set; } = "";

    /// <summary>
    /// Display text combining thread ID, name, and status for the list view.
    /// </summary>
    public string DisplayText => IsCurrent
        ? $"\u25B6 {Name} [{Status}]"
        : $"  {Name} [{Status}]";
}
