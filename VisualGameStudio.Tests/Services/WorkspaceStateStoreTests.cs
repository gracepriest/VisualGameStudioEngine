using NUnit.Framework;
using VisualGameStudio.Core.Models;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

[TestFixture]
public class WorkspaceStateStoreTests
{
    private string _storageRoot = null!;
    private string _projectDir = null!;
    private WorkspaceStateStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        // storageRoot is injected so tests never touch the user's real ~/.vgs/workspaceStorage.
        _storageRoot = Path.Combine(Path.GetTempPath(), $"WorkspaceStateTest_{Guid.NewGuid()}");
        _projectDir = Path.Combine(Path.GetTempPath(), $"WorkspaceStateProj_{Guid.NewGuid()}");
        Directory.CreateDirectory(_projectDir);
        _store = new WorkspaceStateStore(_storageRoot);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var dir in new[] { _storageRoot, _projectDir })
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
            catch { /* ignore cleanup errors */ }
        }
    }

    [Test]
    public void Load_NoSavedState_ReturnsNull()
    {
        Assert.That(_store.Load(_projectDir), Is.Null);
    }

    [Test]
    public void SaveThenLoad_RoundTripsState()
    {
        var state = new WorkspaceStateModel
        {
            SavedAtWidth = 1280,
            SavedAtHeight = 800,
            ActiveDocumentPath = @"C:\proj\Main.bas",
            OpenDocuments =
            {
                new OpenDocumentState { Path = @"C:\proj\Main.bas", CaretLine = 12, CaretColumn = 3 },
                new OpenDocumentState { Path = @"C:\proj\Other.bas" }
            },
            DockLayout = new DockNode
            {
                Kind = DockNodeKind.Root,
                Id = "Root",
                Children =
                {
                    new DockNode { Kind = DockNodeKind.ToolDock, Id = "BottomLeftTools", ActiveDockableId = "Output" }
                }
            }
        };

        _store.Save(_projectDir, state);
        var loaded = _store.Load(_projectDir);

        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.SavedAtWidth, Is.EqualTo(1280));
        Assert.That(loaded.ActiveDocumentPath, Is.EqualTo(@"C:\proj\Main.bas"));
        Assert.That(loaded.OpenDocuments, Has.Count.EqualTo(2));
        Assert.That(loaded.OpenDocuments[0].CaretLine, Is.EqualTo(12));
        Assert.That(loaded.OpenDocuments[0].CaretColumn, Is.EqualTo(3));
        Assert.That(loaded.DockLayout, Is.Not.Null);
        Assert.That(loaded.DockLayout!.Children[0].ActiveDockableId, Is.EqualTo("Output"));
    }

    [Test]
    public void Save_WritesUnderStorageRootKeyedByHash()
    {
        _store.Save(_projectDir, new WorkspaceStateModel());

        var expectedDir = _store.GetWorkspaceDirectory(_projectDir);
        Assert.That(expectedDir, Does.StartWith(_storageRoot));
        Assert.That(File.Exists(Path.Combine(expectedDir, "state.json")), Is.True);
        // workspace.json records the real path for debuggability (VS Code parity).
        Assert.That(File.Exists(Path.Combine(expectedDir, "workspace.json")), Is.True);
    }

    [Test]
    public void Save_WritesNothingInsideTheProjectFolder()
    {
        _store.Save(_projectDir, new WorkspaceStateModel());

        // Personal layout must never land in the project (stays out of git).
        Assert.That(Directory.Exists(Path.Combine(_projectDir, ".vgs")), Is.False);
        Assert.That(Directory.GetFileSystemEntries(_projectDir), Is.Empty);
    }

    [Test]
    public void ComputeHash_IsStableAndCaseInsensitiveOnWindows()
    {
        var h1 = _store.ComputeHash(_projectDir);
        var h2 = _store.ComputeHash(_projectDir);
        Assert.That(h1, Is.EqualTo(h2));

        if (OperatingSystem.IsWindows())
        {
            var upper = _store.ComputeHash(_projectDir.ToUpperInvariant());
            Assert.That(upper, Is.EqualTo(h1), "hash should be case-insensitive on Windows");
        }
    }

    [Test]
    public void Load_CorruptJson_ReturnsNullInsteadOfThrowing()
    {
        var dir = _store.GetWorkspaceDirectory(_projectDir);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "state.json"), "{ this is not valid json ]");

        Assert.That(_store.Load(_projectDir), Is.Null);
    }

    [Test]
    public void Load_IncompatibleVersion_ReturnsNull()
    {
        _store.Save(_projectDir, new WorkspaceStateModel());

        // Rewrite with a future version the current build can't understand.
        var file = Path.Combine(_store.GetWorkspaceDirectory(_projectDir), "state.json");
        File.WriteAllText(file, "{ \"version\": 9999 }");

        Assert.That(_store.Load(_projectDir), Is.Null);
    }

    [Test]
    public void Clear_RemovesSavedState()
    {
        _store.Save(_projectDir, new WorkspaceStateModel());
        Assert.That(_store.Load(_projectDir), Is.Not.Null);

        _store.Clear(_projectDir);

        Assert.That(_store.Load(_projectDir), Is.Null);
    }
}
