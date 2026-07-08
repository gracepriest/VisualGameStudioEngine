using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// User-local, per-project window/session state store (VS Code's workspaceStorage model).
/// State for a project lives at {storageRoot}/{hash}/state.json where hash is a SHA-256 of
/// the normalized project path, and {storageRoot} defaults to ~/.vgs/workspaceStorage.
/// A sibling workspace.json records the real path for debuggability. All IO is best-effort:
/// failures and corrupt/incompatible files never throw (Load returns null).
/// The optional storageRoot constructor argument lets tests use a temp directory instead of
/// the user's real store.
/// </summary>
public class WorkspaceStateStore : IWorkspaceStateStore
{
    private readonly string _storageRoot;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public WorkspaceStateStore() : this(null)
    {
    }

    public WorkspaceStateStore(string? storageRoot)
    {
        _storageRoot = storageRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".vgs", "workspaceStorage");
    }

    /// <summary>The stable per-project storage key (lowercase SHA-256 hex of the normalized path).</summary>
    public string ComputeHash(string projectDirectory)
    {
        var normalized = Normalize(projectDirectory);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>The per-project storage folder (may not exist yet).</summary>
    public string GetWorkspaceDirectory(string projectDirectory)
        => Path.Combine(_storageRoot, ComputeHash(projectDirectory));

    public WorkspaceStateModel? Load(string projectDirectory)
    {
        if (string.IsNullOrWhiteSpace(projectDirectory)) return null;

        try
        {
            var file = StateFilePath(projectDirectory);
            if (!File.Exists(file)) return null;

            var json = File.ReadAllText(file);
            var model = JsonSerializer.Deserialize<WorkspaceStateModel>(json, JsonOptions);
            if (model == null) return null;

            // Hard-invalidate incompatible schema so stale trees can't corrupt the layout.
            if (model.Version != WorkspaceStateModel.CurrentVersion) return null;

            return model;
        }
        catch
        {
            return null;
        }
    }

    public void Save(string projectDirectory, WorkspaceStateModel state)
    {
        if (string.IsNullOrWhiteSpace(projectDirectory) || state == null) return;

        try
        {
            state.Version = WorkspaceStateModel.CurrentVersion;

            var dir = GetWorkspaceDirectory(projectDirectory);
            Directory.CreateDirectory(dir);

            // Record which path this hash maps to (VS Code parity; purely diagnostic).
            var mapFile = Path.Combine(dir, "workspace.json");
            if (!File.Exists(mapFile))
            {
                var map = JsonSerializer.Serialize(
                    new WorkspacePathMap { Folder = NormalizeForDisplay(projectDirectory) }, JsonOptions);
                File.WriteAllText(mapFile, map);
            }

            var stateJson = JsonSerializer.Serialize(state, JsonOptions);
            File.WriteAllText(StateFilePath(projectDirectory), stateJson);
        }
        catch
        {
            // Persistence is best-effort; never surface a failure to the user.
        }
    }

    public void Clear(string projectDirectory)
    {
        if (string.IsNullOrWhiteSpace(projectDirectory)) return;

        try
        {
            var file = StateFilePath(projectDirectory);
            if (File.Exists(file)) File.Delete(file);
        }
        catch
        {
        }
    }

    private string StateFilePath(string projectDirectory)
        => Path.Combine(GetWorkspaceDirectory(projectDirectory), "state.json");

    private static string Normalize(string path)
    {
        var normalized = NormalizeForDisplay(path);
        // Windows/macOS filesystems are case-insensitive; hash case-folded so the same
        // folder always maps to the same key regardless of how it was typed.
        return OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? normalized.ToLowerInvariant()
            : normalized;
    }

    private static string NormalizeForDisplay(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    private class WorkspacePathMap
    {
        public string Folder { get; set; } = "";
    }
}
