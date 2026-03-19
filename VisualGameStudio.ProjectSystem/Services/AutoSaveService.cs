using System.Collections.Concurrent;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// Auto-save service with VS Code-like modes: Off, AfterDelay, OnFocusChange, OnWindowChange.
/// Uses per-document timers that reset on each edit for AfterDelay mode.
/// </summary>
public class AutoSaveService : IAutoSaveService
{
    private readonly ISettingsService _settingsService;
    private readonly ConcurrentDictionary<string, DocumentAutoSaveEntry> _documents = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, System.Timers.Timer> _timers = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public AutoSaveMode Mode { get; set; } = AutoSaveMode.Off;
    public int DelayMilliseconds { get; set; } = 1000;
    public bool SkipOnErrors { get; set; }

    public event EventHandler<AutoSaveEventArgs>? DocumentAutoSaved;

    public AutoSaveService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        LoadSettings();

        // Listen for settings changes
        _settingsService.SettingChanged += OnSettingChanged;
    }

    private void LoadSettings()
    {
        var modeStr = _settingsService.Get<string>(SettingsKeys.AutoSave, "off");
        Mode = modeStr?.ToLowerInvariant() switch
        {
            "afterdelay" => AutoSaveMode.AfterDelay,
            "onfocuschange" => AutoSaveMode.OnFocusChange,
            "onwindowchange" => AutoSaveMode.OnWindowChange,
            _ => AutoSaveMode.Off
        };

        DelayMilliseconds = _settingsService.Get(SettingsKeys.AutoSaveDelay, 1000);
        SkipOnErrors = _settingsService.Get("files.autoSaveSkipOnErrors", false);
    }

    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (e.Key == SettingsKeys.AutoSave || e.Key == SettingsKeys.AutoSaveDelay ||
            e.Key == "files.autoSaveSkipOnErrors")
        {
            LoadSettings();
        }
    }

    public void RegisterDocument(string filePath, Func<Task<bool>> saveCallback, Func<bool> isDirtyFunc, Func<bool> isReadOnlyFunc)
    {
        if (string.IsNullOrEmpty(filePath)) return;

        var entry = new DocumentAutoSaveEntry
        {
            FilePath = filePath,
            SaveCallback = saveCallback,
            IsDirtyFunc = isDirtyFunc,
            IsReadOnlyFunc = isReadOnlyFunc
        };

        _documents[filePath] = entry;
    }

    public void UnregisterDocument(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return;

        _documents.TryRemove(filePath, out _);
        StopTimer(filePath);
    }

    public void NotifyDocumentChanged(string filePath)
    {
        if (Mode != AutoSaveMode.AfterDelay) return;
        if (string.IsNullOrEmpty(filePath)) return;
        if (!_documents.ContainsKey(filePath)) return;

        // Reset or create the per-document debounce timer
        StopTimer(filePath);

        var timer = new System.Timers.Timer(DelayMilliseconds)
        {
            AutoReset = false
        };

        timer.Elapsed += async (s, e) =>
        {
            timer.Dispose();
            _timers.TryRemove(filePath, out _);
            await TrySaveDocumentAsync(filePath);
        };

        _timers[filePath] = timer;
        timer.Start();
    }

    public void NotifyEditorLostFocus(string filePath)
    {
        if (Mode != AutoSaveMode.OnFocusChange) return;
        if (string.IsNullOrEmpty(filePath)) return;

        _ = TrySaveDocumentAsync(filePath);
    }

    public void NotifyWindowLostFocus()
    {
        if (Mode != AutoSaveMode.OnWindowChange) return;

        // Save all dirty registered documents
        foreach (var entry in _documents.Values)
        {
            _ = TrySaveDocumentAsync(entry.FilePath);
        }
    }

    private async Task TrySaveDocumentAsync(string filePath)
    {
        if (!_documents.TryGetValue(filePath, out var entry)) return;

        try
        {
            // Skip if not dirty
            if (!entry.IsDirtyFunc()) return;

            // Skip if readonly
            if (entry.IsReadOnlyFunc()) return;

            var success = await entry.SaveCallback();
            DocumentAutoSaved?.Invoke(this, new AutoSaveEventArgs(filePath, success));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AutoSave] Failed to save {filePath}: {ex.Message}");
            DocumentAutoSaved?.Invoke(this, new AutoSaveEventArgs(filePath, false));
        }
    }

    private void StopTimer(string filePath)
    {
        if (_timers.TryRemove(filePath, out var existing))
        {
            existing.Stop();
            existing.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _settingsService.SettingChanged -= OnSettingChanged;

        foreach (var timer in _timers.Values)
        {
            timer.Stop();
            timer.Dispose();
        }
        _timers.Clear();
        _documents.Clear();
    }

    private class DocumentAutoSaveEntry
    {
        public string FilePath { get; set; } = "";
        public Func<Task<bool>> SaveCallback { get; set; } = () => Task.FromResult(false);
        public Func<bool> IsDirtyFunc { get; set; } = () => false;
        public Func<bool> IsReadOnlyFunc { get; set; } = () => false;
    }
}
