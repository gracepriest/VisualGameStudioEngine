using NUnit.Framework;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

[TestFixture]
public class RecentProjectsServiceTests
{
    private RecentProjectsService _service = null!;
    private string _testDir = null!;

    [SetUp]
    public void SetUp()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"RecentProjectsTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
        // Storage dir is injected so tests never touch the user's real
        // %APPDATA%\VisualGameStudio\recentProjects.json store.
        _service = new RecentProjectsService(_testDir);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    #region Initial State Tests

    [Test]
    public void InitialState_RecentProjectsIsEmpty()
    {
        Assert.That(_service.RecentProjects, Is.Empty);
    }

    [Test]
    public void RecentProjects_ReturnsReadOnlyList()
    {
        Assert.That(_service.RecentProjects, Is.InstanceOf<IReadOnlyList<RecentProject>>());
    }

    #endregion

    #region AddRecentProjectAsync Tests

    [Test]
    public async Task AddRecentProjectAsync_AddsToList()
    {
        var projectPath = CreateTestProjectFile("TestProject.blproj");

        await _service.AddRecentProjectAsync("TestProject", projectPath);

        Assert.That(_service.RecentProjects, Has.Count.EqualTo(1));
        Assert.That(_service.RecentProjects[0].Name, Is.EqualTo("TestProject"));
        Assert.That(_service.RecentProjects[0].FilePath, Is.EqualTo(projectPath));
    }

    [Test]
    public async Task AddRecentProjectAsync_SetsLastOpened()
    {
        var projectPath = CreateTestProjectFile("TestProject.blproj");
        var before = DateTime.Now;

        await _service.AddRecentProjectAsync("TestProject", projectPath);

        Assert.That(_service.RecentProjects[0].LastOpened, Is.GreaterThanOrEqualTo(before));
    }

    [Test]
    public async Task AddRecentProjectAsync_DuplicatePath_MovesToTop()
    {
        var path1 = CreateTestProjectFile("Project1.blproj");
        var path2 = CreateTestProjectFile("Project2.blproj");

        await _service.AddRecentProjectAsync("Project1", path1);
        await _service.AddRecentProjectAsync("Project2", path2);
        await _service.AddRecentProjectAsync("Project1 Updated", path1);

        Assert.That(_service.RecentProjects, Has.Count.EqualTo(2));
        Assert.That(_service.RecentProjects[0].Name, Is.EqualTo("Project1 Updated"));
        Assert.That(_service.RecentProjects[0].FilePath, Is.EqualTo(path1));
    }

    [Test]
    public async Task AddRecentProjectAsync_LimitsToMaxProjects()
    {
        // Add 22 projects (max is 20)
        for (int i = 0; i < 22; i++)
        {
            var path = CreateTestProjectFile($"Project{i}.blproj");
            await _service.AddRecentProjectAsync($"Project{i}", path);
        }

        Assert.That(_service.RecentProjects.Count, Is.LessThanOrEqualTo(20));
    }

    [Test]
    public async Task AddRecentProjectAsync_FiresRecentProjectsChangedEvent()
    {
        var eventFired = false;
        _service.RecentProjectsChanged += (s, e) => eventFired = true;

        var projectPath = CreateTestProjectFile("TestProject.blproj");
        await _service.AddRecentProjectAsync("TestProject", projectPath);

        Assert.That(eventFired, Is.True);
    }

    [Test]
    public async Task AddRecentProjectAsync_NewestFirst()
    {
        var path1 = CreateTestProjectFile("Project1.blproj");
        var path2 = CreateTestProjectFile("Project2.blproj");
        var path3 = CreateTestProjectFile("Project3.blproj");

        await _service.AddRecentProjectAsync("Project1", path1);
        await _service.AddRecentProjectAsync("Project2", path2);
        await _service.AddRecentProjectAsync("Project3", path3);

        Assert.That(_service.RecentProjects[0].Name, Is.EqualTo("Project3"));
        Assert.That(_service.RecentProjects[1].Name, Is.EqualTo("Project2"));
        Assert.That(_service.RecentProjects[2].Name, Is.EqualTo("Project1"));
    }

    #endregion

    #region RemoveRecentProjectAsync Tests

    [Test]
    public async Task RemoveRecentProjectAsync_RemovesFromList()
    {
        var path1 = CreateTestProjectFile("Project1.blproj");
        var path2 = CreateTestProjectFile("Project2.blproj");

        await _service.AddRecentProjectAsync("Project1", path1);
        await _service.AddRecentProjectAsync("Project2", path2);

        await _service.RemoveRecentProjectAsync(path1);

        Assert.That(_service.RecentProjects, Has.Count.EqualTo(1));
        Assert.That(_service.RecentProjects[0].FilePath, Is.EqualTo(path2));
    }

    [Test]
    public async Task RemoveRecentProjectAsync_CaseInsensitive()
    {
        var path = CreateTestProjectFile("Project.blproj");

        await _service.AddRecentProjectAsync("Project", path);
        await _service.RemoveRecentProjectAsync(path.ToUpperInvariant());

        Assert.That(_service.RecentProjects, Is.Empty);
    }

    [Test]
    public async Task RemoveRecentProjectAsync_NonExistent_DoesNotThrow()
    {
        await _service.RemoveRecentProjectAsync("/nonexistent/path.blproj");

        Assert.Pass("No exception thrown");
    }

    [Test]
    public async Task RemoveRecentProjectAsync_FiresRecentProjectsChangedEvent()
    {
        var path = CreateTestProjectFile("Project.blproj");
        await _service.AddRecentProjectAsync("Project", path);

        var eventFired = false;
        _service.RecentProjectsChanged += (s, e) => eventFired = true;

        await _service.RemoveRecentProjectAsync(path);

        Assert.That(eventFired, Is.True);
    }

    #endregion

    #region ClearAsync Tests

    [Test]
    public async Task ClearAsync_RemovesAllProjects()
    {
        var path1 = CreateTestProjectFile("Project1.blproj");
        var path2 = CreateTestProjectFile("Project2.blproj");

        await _service.AddRecentProjectAsync("Project1", path1);
        await _service.AddRecentProjectAsync("Project2", path2);

        await _service.ClearAsync();

        Assert.That(_service.RecentProjects, Is.Empty);
    }

    [Test]
    public async Task ClearAsync_FiresRecentProjectsChangedEvent()
    {
        var path = CreateTestProjectFile("Project.blproj");
        await _service.AddRecentProjectAsync("Project", path);

        var eventFired = false;
        _service.RecentProjectsChanged += (s, e) => eventFired = true;

        await _service.ClearAsync();

        Assert.That(eventFired, Is.True);
    }

    [Test]
    public async Task ClearAsync_WhenEmpty_DoesNotThrow()
    {
        await _service.ClearAsync();

        Assert.Pass("No exception thrown");
    }

    #endregion

    #region LoadAsync Tests

    [Test]
    public async Task LoadAsync_WhenNoFile_DoesNotThrow()
    {
        await _service.LoadAsync();

        Assert.That(_service.RecentProjects, Is.Empty);
    }

    [Test]
    public async Task LoadAsync_RoundTripsPersistedProjects()
    {
        var path = CreateTestProjectFile("Project.blproj");
        await _service.AddRecentProjectAsync("Project", path);

        var newService = new RecentProjectsService(_testDir);
        await newService.LoadAsync();

        Assert.That(newService.RecentProjects, Has.Count.EqualTo(1));
        Assert.That(newService.RecentProjects[0].FilePath, Is.EqualTo(path));
    }

    [Test]
    public async Task GetRecentProjects_HidesEntriesWhoseFileIsMissing()
    {
        var path = CreateTestProjectFile("Project.blproj");
        await _service.AddRecentProjectAsync("Project", path);

        File.Delete(path);

        Assert.That(_service.GetRecentProjects(), Is.Empty);
    }

    [Test]
    public async Task LoadAsync_MissingFileEntry_IsHiddenButNotErasedFromStore()
    {
        // A project whose file is temporarily unavailable (e.g. OneDrive offline,
        // unplugged drive) must be hidden from the menu but survive in storage,
        // so it reappears when the file comes back.
        var path = CreateTestProjectFile("Project.blproj");
        await _service.AddRecentProjectAsync("Project", path);

        File.Delete(path);

        var whileMissing = new RecentProjectsService(_testDir);
        await whileMissing.LoadAsync();
        Assert.That(whileMissing.GetRecentProjects(), Is.Empty, "missing entry should be hidden");

        // File comes back; a fresh service must still know about the entry.
        File.WriteAllText(path, "test project content");
        var afterRestore = new RecentProjectsService(_testDir);
        await afterRestore.LoadAsync();
        Assert.That(afterRestore.GetRecentProjects(), Has.Count.EqualTo(1),
            "entry must survive a load performed while its file was missing");
        Assert.That(afterRestore.GetRecentProjects()[0].Path, Is.EqualTo(path));
    }

    [Test]
    public async Task LoadAsync_MergesWithEntriesAddedBeforeLoadCompleted()
    {
        // Startup race: the shell may open a project (AddRecentProject) before
        // LoadAsync has finished reading the store. The loaded list must merge
        // with, not replace, in-memory entries.
        var persistedPath = CreateTestProjectFile("Persisted.blproj");
        await _service.AddRecentProjectAsync("Persisted", persistedPath);

        var newService = new RecentProjectsService(_testDir);
        var earlyPath = CreateTestProjectFile("AddedBeforeLoad.blproj");
        newService.AddRecentProject(earlyPath, "AddedBeforeLoad");
        await newService.LoadAsync();

        var paths = newService.GetRecentProjects().Select(p => p.Path).ToList();
        Assert.That(paths, Does.Contain(earlyPath), "entry added before load must survive");
        Assert.That(paths, Does.Contain(persistedPath), "persisted entry must be loaded");
    }

    [Test]
    public async Task Storage_WritesOnlyToInjectedDirectory()
    {
        var path = CreateTestProjectFile("Project.blproj");
        await _service.AddRecentProjectAsync("Project", path);

        Assert.That(File.Exists(Path.Combine(_testDir, "recentProjects.json")), Is.True,
            "store must live in the injected directory");
    }

    #endregion

    #region Helper Methods

    private string CreateTestProjectFile(string fileName)
    {
        var filePath = Path.Combine(_testDir, fileName);
        File.WriteAllText(filePath, "test project content");
        return filePath;
    }

    #endregion
}

[TestFixture]
public class RecentProjectModelTests
{
    [Test]
    public void DefaultProject_HasDefaultValues()
    {
        var project = new RecentProject();

        Assert.That(project.Name, Is.EqualTo(""));
        Assert.That(project.FilePath, Is.EqualTo(""));
        Assert.That(project.LastOpened, Is.EqualTo(default(DateTime)));
    }

    [Test]
    public void Project_CanSetAllProperties()
    {
        var dateTime = DateTime.Now;
        var project = new RecentProject
        {
            Name = "MyProject",
            FilePath = "/path/to/project.blproj",
            LastOpened = dateTime
        };

        Assert.That(project.Name, Is.EqualTo("MyProject"));
        Assert.That(project.FilePath, Is.EqualTo("/path/to/project.blproj"));
        Assert.That(project.LastOpened, Is.EqualTo(dateTime));
    }
}
