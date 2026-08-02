using NUnit.Framework;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

[TestFixture]
public class BookmarkServicePersistenceTests
{
    private string _projectDir = null!;

    [SetUp]
    public void SetUp()
    {
        _projectDir = Path.Combine(Path.GetTempPath(), $"BookmarkPersistTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(_projectDir);
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(_projectDir)) Directory.Delete(_projectDir, true); }
        catch { /* ignore cleanup errors */ }
    }

    [Test]
    public async Task SaveThenLoadInNewInstance_RestoresBookmarks()
    {
        var file = Path.Combine(_projectDir, "a.bas");

        var s1 = new BookmarkService();
        s1.SetProjectDirectory(_projectDir);
        s1.ToggleBookmark(file, 5, "note");
        s1.ToggleBookmark(file, 12);
        await s1.SaveAsync();

        // A fresh service (simulating an IDE restart) reloads them.
        var s2 = new BookmarkService();
        s2.SetProjectDirectory(_projectDir);
        await s2.LoadAsync();

        var all = s2.GetAllBookmarks();
        Assert.That(all, Has.Count.EqualTo(2));
        Assert.That(all.Any(b => b.Line == 5 && b.Label == "note"), Is.True);
        Assert.That(all.Any(b => b.Line == 12), Is.True);
    }

    [Test]
    public async Task Save_WritesUnderDotVgs()
    {
        var s = new BookmarkService();
        s.SetProjectDirectory(_projectDir);
        s.ToggleBookmark(Path.Combine(_projectDir, "a.bas"), 1);
        await s.SaveAsync();

        Assert.That(File.Exists(Path.Combine(_projectDir, ".vgs", "bookmarks.json")), Is.True);
    }

    [Test]
    public async Task Load_NoFile_LeavesServiceEmptyAndDoesNotThrow()
    {
        var s = new BookmarkService();
        s.SetProjectDirectory(_projectDir);

        Assert.DoesNotThrowAsync(async () => await s.LoadAsync());
        Assert.That(s.GetAllBookmarks(), Is.Empty);
    }

    [Test]
    public void NoProjectDirectory_SaveIsNoOpAndDoesNotThrow()
    {
        var s = new BookmarkService();
        s.ToggleBookmark("somefile.bas", 3); // triggers fire-and-forget save with no project dir

        Assert.DoesNotThrowAsync(async () => await s.SaveAsync());
    }

    [Test]
    public async Task Load_ReplacesInMemorySet()
    {
        var file = Path.Combine(_projectDir, "a.bas");

        var writer = new BookmarkService();
        writer.SetProjectDirectory(_projectDir);
        writer.ToggleBookmark(file, 7);
        await writer.SaveAsync();

        var reader = new BookmarkService();
        reader.ToggleBookmark(file, 99); // pre-existing in-memory bookmark (no project dir yet => not saved)
        reader.SetProjectDirectory(_projectDir);
        await reader.LoadAsync();          // load replaces the in-memory set with the persisted one

        var all = reader.GetAllBookmarks();
        Assert.That(all, Has.Count.EqualTo(1));
        Assert.That(all[0].Line, Is.EqualTo(7));
    }

    [Test]
    public async Task ConcurrentFireAndForgetSaves_AwaitedSaveThenLoad_YieldsExactPersistedSet()
    {
        // Regression test for the save/load file race: every ToggleBookmark fires an
        // unawaited SaveAsync; without per-instance serialization those writes overlap
        // the awaited save and the subsequent load (sharing violations / torn reads,
        // all swallowed by the best-effort catches), leaving the reader with a stale
        // in-memory set. Post-fix this must be deterministic.
        const int iterations = 50;
        const int lineCount = 300; // large JSON payload widens the write window

        for (int i = 0; i < iterations; i++)
        {
            var iterDir = Path.Combine(_projectDir, $"iter{i}");
            Directory.CreateDirectory(iterDir);
            var file = Path.Combine(iterDir, "a.bas");

            var writer = new BookmarkService();
            writer.SetProjectDirectory(iterDir);
            for (int line = 1; line <= lineCount; line++)
            {
                writer.ToggleBookmark(file, line); // each fires a fire-and-forget save
            }
            writer.ToggleBookmark(file, lineCount + 1); // the racing fire-and-forget save
            await writer.SaveAsync();

            var reader = new BookmarkService();
            reader.ToggleBookmark(file, 99999); // pre-existing in-memory bookmark (no project dir yet => not saved)
            reader.SetProjectDirectory(iterDir);
            await reader.LoadAsync();

            var all = reader.GetAllBookmarks();
            Assert.That(all, Has.Count.EqualTo(lineCount + 1),
                $"Iteration {i}: loaded bookmark count does not match what the writer persisted");
            Assert.That(all.Any(b => b.Line == 99999), Is.False,
                $"Iteration {i}: reader's stale pre-existing in-memory bookmark survived LoadAsync");
        }
    }
}
