using System.Text.Json;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// Hot exit service that preserves unsaved document content across IDE restarts.
/// Backups are stored in ~/.vgs/backups/ as JSON files keyed by a hash of the file path.
/// </summary>
public class HotExitService : IHotExitService
{
    private readonly ISettingsService _settingsService;
    private readonly string _backupDirectory;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    public HotExitMode Mode { get; set; } = HotExitMode.OnExit;

    public HotExitService(ISettingsService settingsService)
    {
        _settingsService = settingsService;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _backupDirectory = Path.Combine(home, ".vgs", "backups");

        LoadSettings();
        _settingsService.SettingChanged += OnSettingChanged;
    }

    private void LoadSettings()
    {
        var modeStr = _settingsService.Get<string>("files.hotExit", "onExit");
        Mode = modeStr?.ToLowerInvariant() switch
        {
            "off" => HotExitMode.Off,
            "onexitandwindowclose" => HotExitMode.OnExitAndWindowClose,
            _ => HotExitMode.OnExit
        };
    }

    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (e.Key == "files.hotExit")
        {
            LoadSettings();
        }
    }

    public async Task SaveBackupsAsync(IEnumerable<HotExitDocumentState> documents)
    {
        if (Mode == HotExitMode.Off) return;

        try
        {
            if (!Directory.Exists(_backupDirectory))
            {
                Directory.CreateDirectory(_backupDirectory);
            }

            // Write a manifest of all backed-up documents
            var manifest = new HotExitManifest
            {
                Timestamp = DateTime.UtcNow,
                Documents = documents.Where(d => d.IsDirty).ToList()
            };

            if (manifest.Documents.Count == 0)
            {
                // No dirty documents - clean up any existing backups
                await CleanupAllBackupsAsync();
                return;
            }

            var manifestPath = Path.Combine(_backupDirectory, "manifest.json");
            var json = JsonSerializer.Serialize(manifest, _jsonOptions);
            await File.WriteAllTextAsync(manifestPath, json);

            // Write individual backup files for content (keeps manifest small)
            foreach (var doc in manifest.Documents)
            {
                var hash = GetFileHash(doc.FilePath);
                var backupPath = Path.Combine(_backupDirectory, $"{hash}.bak");
                await File.WriteAllTextAsync(backupPath, doc.Content);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HotExit] Failed to save backups: {ex.Message}");
        }
    }

    public async Task<bool> HasBackupsAsync()
    {
        var manifestPath = Path.Combine(_backupDirectory, "manifest.json");
        if (!File.Exists(manifestPath)) return false;

        try
        {
            var json = await File.ReadAllTextAsync(manifestPath);
            var manifest = JsonSerializer.Deserialize<HotExitManifest>(json);
            return manifest?.Documents?.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<HotExitDocumentState>> GetBackupsAsync()
    {
        var manifestPath = Path.Combine(_backupDirectory, "manifest.json");
        if (!File.Exists(manifestPath)) return Array.Empty<HotExitDocumentState>();

        try
        {
            var json = await File.ReadAllTextAsync(manifestPath);
            var manifest = JsonSerializer.Deserialize<HotExitManifest>(json);
            if (manifest?.Documents == null) return Array.Empty<HotExitDocumentState>();

            // Load content from individual backup files
            foreach (var doc in manifest.Documents)
            {
                var hash = GetFileHash(doc.FilePath);
                var backupPath = Path.Combine(_backupDirectory, $"{hash}.bak");
                if (File.Exists(backupPath))
                {
                    doc.Content = await File.ReadAllTextAsync(backupPath);
                }
            }

            return manifest.Documents.AsReadOnly();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HotExit] Failed to read backups: {ex.Message}");
            return Array.Empty<HotExitDocumentState>();
        }
    }

    public Task CleanupBackupAsync(string filePath)
    {
        try
        {
            var hash = GetFileHash(filePath);
            var backupPath = Path.Combine(_backupDirectory, $"{hash}.bak");
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HotExit] Failed to cleanup backup for {filePath}: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    public Task CleanupAllBackupsAsync()
    {
        try
        {
            if (Directory.Exists(_backupDirectory))
            {
                foreach (var file in Directory.GetFiles(_backupDirectory))
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HotExit] Failed to cleanup all backups: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    private static string GetFileHash(string filePath)
    {
        // Simple stable hash for file path -> backup filename
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(filePath.ToLowerInvariant());
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    private class HotExitManifest
    {
        public DateTime Timestamp { get; set; }
        public List<HotExitDocumentState> Documents { get; set; } = new();
    }
}
