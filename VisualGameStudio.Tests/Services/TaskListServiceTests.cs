using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

[TestFixture]
public class TaskListServiceTests
{
    private TaskListService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _service = new TaskListService();
    }

    #region ScanDocument Tests

    [Test]
    public void ScanDocument_EmptyContent_ReturnsEmpty()
    {
        var tasks = _service.ScanDocument("", "test.bl");
        Assert.That(tasks, Is.Empty);
    }

    [Test]
    public void ScanDocument_NoTasks_ReturnsEmpty()
    {
        var code = @"
Function Hello()
    Print ""Hello World""
End Function";

        var tasks = _service.ScanDocument(code, "test.bl");
        Assert.That(tasks, Is.Empty);
    }

    [Test]
    public void ScanDocument_TodoComment_FindsTask()
    {
        var code = @"
' TODO: Implement this feature
Function Hello()
End Function";

        var tasks = _service.ScanDocument(code, "test.bl");

        Assert.That(tasks, Has.Count.EqualTo(1));
        Assert.That(tasks[0].Token, Is.EqualTo("TODO"));
        Assert.That(tasks[0].Type, Is.EqualTo(TaskType.Todo));
    }

    [Test]
    public void ScanDocument_FixmeComment_FindsTask()
    {
        var code = @"
' FIXME: This is broken
Function Broken()
End Function";

        var tasks = _service.ScanDocument(code, "test.bl");

        Assert.That(tasks, Has.Count.EqualTo(1));
        Assert.That(tasks[0].Token, Is.EqualTo("FIXME"));
        Assert.That(tasks[0].Type, Is.EqualTo(TaskType.Fixme));
    }

    [Test]
    public void ScanDocument_HackComment_FindsTask()
    {
        var code = @"
' HACK: Workaround for bug
Function Workaround()
End Function";

        var tasks = _service.ScanDocument(code, "test.bl");

        Assert.That(tasks, Has.Count.EqualTo(1));
        Assert.That(tasks[0].Token, Is.EqualTo("HACK"));
        Assert.That(tasks[0].Type, Is.EqualTo(TaskType.Hack));
    }

    [Test]
    public void ScanDocument_BugComment_FindsTask()
    {
        var code = @"
' BUG: This causes crash
Function Buggy()
End Function";

        var tasks = _service.ScanDocument(code, "test.bl");

        Assert.That(tasks, Has.Count.EqualTo(1));
        Assert.That(tasks[0].Token, Is.EqualTo("BUG"));
        Assert.That(tasks[0].Type, Is.EqualTo(TaskType.Bug));
    }

    [Test]
    public void ScanDocument_NoteComment_FindsTask()
    {
        var code = @"
' NOTE: This is important
Function Important()
End Function";

        var tasks = _service.ScanDocument(code, "test.bl");

        Assert.That(tasks, Has.Count.EqualTo(1));
        Assert.That(tasks[0].Token, Is.EqualTo("NOTE"));
        Assert.That(tasks[0].Type, Is.EqualTo(TaskType.Note));
    }

    [Test]
    public void ScanDocument_MultipleTasks_FindsAll()
    {
        var code = @"
' TODO: First task
' FIXME: Second task
' BUG: Third task
Function Test()
End Function";

        var tasks = _service.ScanDocument(code, "test.bl");

        Assert.That(tasks, Has.Count.EqualTo(3));
    }

    [Test]
    public void ScanDocument_CaseInsensitive_ByDefault()
    {
        var code = @"
' todo: lowercase todo
' Todo: Mixed case todo";

        var tasks = _service.ScanDocument(code, "test.bl");

        Assert.That(tasks, Has.Count.EqualTo(2));
    }

    [Test]
    public void ScanDocument_CaseSensitive_WhenEnabled()
    {
        _service.Options.CaseSensitive = true;
        var code = @"
' todo: lowercase todo
' TODO: Uppercase todo";

        var tasks = _service.ScanDocument(code, "test.bl");

        Assert.That(tasks, Has.Count.EqualTo(1));
        Assert.That(tasks[0].Text, Is.EqualTo("Uppercase todo"));
    }

    [Test]
    public void ScanDocument_ExtractsTaskText()
    {
        var code = "' TODO: Implement feature X";

        var tasks = _service.ScanDocument(code, "test.bl");

        Assert.That(tasks[0].Text, Is.EqualTo("Implement feature X"));
    }

    [Test]
    public void ScanDocument_ExtractsLineNumber()
    {
        var code = @"
Line 1
' TODO: On line 3";

        var tasks = _service.ScanDocument(code, "test.bl");

        Assert.That(tasks[0].Line, Is.EqualTo(3));
    }

    [Test]
    public void ScanDocument_ExtractsFileName()
    {
        var tasks = _service.ScanDocument("' TODO: Test", "path/to/file.bl");

        Assert.That(tasks[0].FileName, Is.EqualTo("file.bl"));
    }

    [Test]
    public void ScanDocument_ExtractsFilePath()
    {
        var tasks = _service.ScanDocument("' TODO: Test", "path/to/file.bl");

        Assert.That(tasks[0].FilePath, Is.EqualTo("path/to/file.bl"));
    }

    [Test]
    public void ScanDocument_ExtractsAssignee()
    {
        var code = "' TODO: @john Fix this bug";

        var tasks = _service.ScanDocument(code, "test.bl");

        Assert.That(tasks[0].Assignee, Is.EqualTo("john"));
    }

    [Test]
    public void ScanDocument_ExtractsTag()
    {
        var code = "' TODO: #performance Optimize this";

        var tasks = _service.ScanDocument(code, "test.bl");

        Assert.That(tasks[0].Tag, Is.EqualTo("performance"));
    }

    [Test]
    public void ScanDocument_DoubleSlashComment_FindsTask()
    {
        var code = "// TODO: C-style comment";

        var tasks = _service.ScanDocument(code, "test.bl");

        Assert.That(tasks, Has.Count.EqualTo(1));
    }

    [Test]
    public void ScanDocument_RemComment_FindsTask()
    {
        var code = "REM TODO: Old-style comment";

        var tasks = _service.ScanDocument(code, "test.bl");

        Assert.That(tasks, Has.Count.EqualTo(1));
    }

    [Test]
    public void ScanDocument_TodoWithColon_ExtractsTextAfterColon()
    {
        var code = "' TODO: Description here";

        var tasks = _service.ScanDocument(code, "test.bl");

        Assert.That(tasks[0].Text, Is.EqualTo("Description here"));
    }

    [Test]
    public void ScanDocument_TodoWithoutColon_ExtractsText()
    {
        var code = "' TODO Description without colon";

        var tasks = _service.ScanDocument(code, "test.bl");

        Assert.That(tasks[0].Text, Is.EqualTo("Description without colon"));
    }

    #endregion

    #region UpdateDocument Tests

    [Test]
    public void UpdateDocument_AddsTasks()
    {
        _service.UpdateDocument("' TODO: Test", "test.bl");

        Assert.That(_service.Tasks, Has.Count.EqualTo(1));
    }

    [Test]
    public void UpdateDocument_ReplacesExistingTasks()
    {
        _service.UpdateDocument("' TODO: First", "test.bl");
        _service.UpdateDocument("' FIXME: Second", "test.bl");

        Assert.That(_service.Tasks, Has.Count.EqualTo(1));
        Assert.That(_service.Tasks[0].Token, Is.EqualTo("FIXME"));
    }

    [Test]
    public void UpdateDocument_RaisesTasksAddedEvent()
    {
        TaskListEventArgs? args = null;
        _service.TasksAdded += (s, e) => args = e;

        _service.UpdateDocument("' TODO: Test", "test.bl");

        Assert.That(args, Is.Not.Null);
        Assert.That(args!.Tasks, Has.Count.EqualTo(1));
    }

    [Test]
    public void UpdateDocument_RaisesTasksRemovedEvent_WhenReplacing()
    {
        _service.UpdateDocument("' TODO: First", "test.bl");

        TaskListEventArgs? args = null;
        _service.TasksRemoved += (s, e) => args = e;

        _service.UpdateDocument("' FIXME: Second", "test.bl");

        Assert.That(args, Is.Not.Null);
        Assert.That(args!.Tasks, Has.Count.EqualTo(1));
    }

    [Test]
    public void UpdateDocument_RaisesTaskListUpdatedEvent()
    {
        bool updated = false;
        _service.TaskListUpdated += (s, e) => updated = true;

        _service.UpdateDocument("' TODO: Test", "test.bl");

        Assert.That(updated, Is.True);
    }

    #endregion

    #region RemoveFile Tests

    [Test]
    public void RemoveFile_RemovesTasks()
    {
        _service.UpdateDocument("' TODO: Test", "test.bl");
        _service.RemoveFile("test.bl");

        Assert.That(_service.Tasks, Is.Empty);
    }

    [Test]
    public void RemoveFile_RaisesTasksRemovedEvent()
    {
        _service.UpdateDocument("' TODO: Test", "test.bl");

        TaskListEventArgs? args = null;
        _service.TasksRemoved += (s, e) => args = e;

        _service.RemoveFile("test.bl");

        Assert.That(args, Is.Not.Null);
    }

    [Test]
    public void RemoveFile_NonExistentFile_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => _service.RemoveFile("nonexistent.bl"));
    }

    #endregion

    #region Clear Tests

    [Test]
    public void Clear_RemovesAllTasks()
    {
        _service.UpdateDocument("' TODO: First", "file1.bl");
        _service.UpdateDocument("' TODO: Second", "file2.bl");

        _service.Clear();

        Assert.That(_service.Tasks, Is.Empty);
    }

    [Test]
    public void Clear_RaisesTasksRemovedEvent()
    {
        _service.UpdateDocument("' TODO: Test", "test.bl");

        TaskListEventArgs? args = null;
        _service.TasksRemoved += (s, e) => args = e;

        _service.Clear();

        Assert.That(args, Is.Not.Null);
    }

    #endregion

    #region Filtering Tests

    [Test]
    public void GetByType_FiltersByType()
    {
        _service.UpdateDocument("' TODO: Todo item\n' FIXME: Fixme item", "test.bl");

        var todos = _service.GetByType(TaskType.Todo);

        Assert.That(todos, Has.Count.EqualTo(1));
        Assert.That(todos[0].Type, Is.EqualTo(TaskType.Todo));
    }

    [Test]
    public void GetByPriority_FiltersByPriority()
    {
        _service.UpdateDocument("' TODO: Normal\n' FIXME: High priority", "test.bl");

        var highPriority = _service.GetByPriority(TaskPriority.High);

        Assert.That(highPriority, Has.Count.EqualTo(1));
        Assert.That(highPriority[0].Token, Is.EqualTo("FIXME"));
    }

    [Test]
    public void GetByFile_ReturnsTasksForFile()
    {
        _service.UpdateDocument("' TODO: In file 1", "file1.bl");
        _service.UpdateDocument("' TODO: In file 2", "file2.bl");

        var tasks = _service.GetByFile("file1.bl");

        Assert.That(tasks, Has.Count.EqualTo(1));
        Assert.That(tasks[0].Text, Is.EqualTo("In file 1"));
    }

    [Test]
    public void GetByFile_NonExistentFile_ReturnsEmpty()
    {
        var tasks = _service.GetByFile("nonexistent.bl");
        Assert.That(tasks, Is.Empty);
    }

    #endregion

    #region Search Tests

    [Test]
    public void Search_EmptyText_ReturnsAllTasks()
    {
        _service.UpdateDocument("' TODO: First\n' FIXME: Second", "test.bl");

        var results = _service.Search("");

        Assert.That(results, Has.Count.EqualTo(2));
    }

    [Test]
    public void Search_ByText_ReturnsMatching()
    {
        _service.UpdateDocument("' TODO: Fix the bug\n' TODO: Add feature", "test.bl");

        var results = _service.Search("bug");

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Text, Does.Contain("bug"));
    }

    [Test]
    public void Search_ByFileName_ReturnsMatching()
    {
        _service.UpdateDocument("' TODO: In main", "main.bl");
        _service.UpdateDocument("' TODO: In utils", "utils.bl");

        var results = _service.Search("main");

        Assert.That(results, Has.Count.EqualTo(1));
    }

    [Test]
    public void Search_ByToken_ReturnsMatching()
    {
        _service.UpdateDocument("' TODO: Item\n' FIXME: Item", "test.bl");

        var results = _service.Search("FIXME");

        Assert.That(results, Has.Count.EqualTo(1));
    }

    [Test]
    public void Search_ByAssignee_ReturnsMatching()
    {
        _service.UpdateDocument("' TODO: @john Fix bug\n' TODO: @jane Add feature", "test.bl");

        var results = _service.Search("john");

        Assert.That(results, Has.Count.EqualTo(1));
    }

    [Test]
    public void Search_ByTag_ReturnsMatching()
    {
        _service.UpdateDocument("' TODO: #urgent Fix now\n' TODO: #later Do later", "test.bl");

        var results = _service.Search("urgent");

        Assert.That(results, Has.Count.EqualTo(1));
    }

    [Test]
    public void Search_CaseInsensitive()
    {
        _service.UpdateDocument("' TODO: Important Task", "test.bl");

        var results = _service.Search("IMPORTANT");

        Assert.That(results, Has.Count.EqualTo(1));
    }

    #endregion

    #region Statistics Tests

    [Test]
    public void GetStatistics_ReturnsCorrectTotalCount()
    {
        _service.UpdateDocument("' TODO: One\n' FIXME: Two\n' BUG: Three", "test.bl");

        var stats = _service.GetStatistics();

        Assert.That(stats.TotalCount, Is.EqualTo(3));
    }

    [Test]
    public void GetStatistics_ReturnsCorrectFileCount()
    {
        _service.UpdateDocument("' TODO: One", "file1.bl");
        _service.UpdateDocument("' TODO: Two", "file2.bl");

        var stats = _service.GetStatistics();

        Assert.That(stats.FileCount, Is.EqualTo(2));
    }

    [Test]
    public void GetStatistics_CountsByType()
    {
        _service.UpdateDocument("' TODO: One\n' TODO: Two\n' FIXME: Three", "test.bl");

        var stats = _service.GetStatistics();

        Assert.That(stats.ByType[TaskType.Todo], Is.EqualTo(2));
        Assert.That(stats.ByType[TaskType.Fixme], Is.EqualTo(1));
    }

    [Test]
    public void GetStatistics_CountsByPriority()
    {
        _service.UpdateDocument("' TODO: Normal\n' FIXME: High", "test.bl");

        var stats = _service.GetStatistics();

        Assert.That(stats.ByPriority[TaskPriority.Normal], Is.EqualTo(1));
        Assert.That(stats.ByPriority[TaskPriority.High], Is.EqualTo(1));
    }

    [Test]
    public void GetStatistics_CountsByFile()
    {
        _service.UpdateDocument("' TODO: One\n' TODO: Two", "file1.bl");
        _service.UpdateDocument("' TODO: Three", "file2.bl");

        var stats = _service.GetStatistics();

        Assert.That(stats.ByFile["file1.bl"], Is.EqualTo(2));
        Assert.That(stats.ByFile["file2.bl"], Is.EqualTo(1));
    }

    #endregion

    #region Custom Token Tests

    [Test]
    public void AddCustomToken_AddsToken()
    {
        _service.AddCustomToken("REVIEW", TaskType.UserDefined, TaskPriority.Normal);

        var tasks = _service.ScanDocument("' REVIEW: Needs review", "test.bl");

        Assert.That(tasks, Has.Count.EqualTo(1));
        Assert.That(tasks[0].Token, Is.EqualTo("REVIEW"));
    }

    [Test]
    public void RemoveCustomToken_RemovesToken()
    {
        _service.AddCustomToken("CUSTOM", TaskType.UserDefined, TaskPriority.Normal);
        _service.RemoveCustomToken("CUSTOM");

        var tasks = _service.ScanDocument("' CUSTOM: Should not match", "test.bl");

        Assert.That(tasks, Is.Empty);
    }

    [Test]
    public void AddCustomToken_EmptyToken_DoesNothing()
    {
        var originalCount = _service.Options.Tokens.Count;

        _service.AddCustomToken("", TaskType.UserDefined, TaskPriority.Normal);

        Assert.That(_service.Options.Tokens.Count, Is.EqualTo(originalCount));
    }

    [Test]
    public void RemoveCustomToken_EmptyToken_DoesNothing()
    {
        Assert.DoesNotThrow(() => _service.RemoveCustomToken(""));
    }

    #endregion

    #region ScanFilesAsync Tests

    [Test]
    public async Task ScanFilesAsync_EmptyList_ReturnsEmpty()
    {
        var tasks = await _service.ScanFilesAsync(Array.Empty<string>());
        Assert.That(tasks, Is.Empty);
    }

    [Test]
    public async Task ScanFilesAsync_NonExistentFile_SkipsFile()
    {
        var tasks = await _service.ScanFilesAsync(new[] { "nonexistent.bl" });
        Assert.That(tasks, Is.Empty);
    }

    [Test]
    public async Task ScanFilesAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await _service.ScanFilesAsync(new[] { "test.bl" }, cts.Token));
    }

    #endregion

    #region Model Tests

    [Test]
    public void TaskItem_Defaults_AreCorrect()
    {
        var item = new TaskItem();

        Assert.That(item.Id, Is.Not.Empty);
        Assert.That(item.FilePath, Is.Empty);
        Assert.That(item.FileName, Is.Empty);
        Assert.That(item.Line, Is.EqualTo(0));
        Assert.That(item.Text, Is.Empty);
        Assert.That(item.Token, Is.Empty);
        Assert.That(item.Type, Is.EqualTo(TaskType.Todo));
        Assert.That(item.Priority, Is.EqualTo(TaskPriority.Low));
        Assert.That(item.FoundAt, Is.GreaterThan(DateTime.MinValue));
    }

    [Test]
    public void TaskListOptions_DefaultTokens_AreConfigured()
    {
        var options = new TaskListOptions();

        Assert.That(options.Tokens, Does.ContainKey("TODO"));
        Assert.That(options.Tokens, Does.ContainKey("FIXME"));
        Assert.That(options.Tokens, Does.ContainKey("HACK"));
        Assert.That(options.Tokens, Does.ContainKey("BUG"));
        Assert.That(options.Tokens, Does.ContainKey("NOTE"));
    }

    [Test]
    public void TaskListOptions_DefaultCommentPrefixes_AreConfigured()
    {
        var options = new TaskListOptions();

        Assert.That(options.CommentPrefixes, Does.Contain("'"));
        Assert.That(options.CommentPrefixes, Does.Contain("//"));
        Assert.That(options.CommentPrefixes, Does.Contain("REM"));
    }

    [Test]
    public void TaskStatistics_Defaults_AreCorrect()
    {
        var stats = new TaskStatistics();

        Assert.That(stats.TotalCount, Is.EqualTo(0));
        Assert.That(stats.FileCount, Is.EqualTo(0));
        Assert.That(stats.ByType, Is.Empty);
        Assert.That(stats.ByPriority, Is.Empty);
        Assert.That(stats.ByFile, Is.Empty);
    }

    [Test]
    public void TaskType_HasExpectedValues()
    {
        var values = Enum.GetValues<TaskType>();
        Assert.That(values, Does.Contain(TaskType.Todo));
        Assert.That(values, Does.Contain(TaskType.Fixme));
        Assert.That(values, Does.Contain(TaskType.Hack));
        Assert.That(values, Does.Contain(TaskType.Bug));
        Assert.That(values, Does.Contain(TaskType.Note));
    }

    [Test]
    public void TaskPriority_HasCorrectOrder()
    {
        Assert.That((int)TaskPriority.Low, Is.EqualTo(0));
        Assert.That((int)TaskPriority.Normal, Is.EqualTo(1));
        Assert.That((int)TaskPriority.High, Is.EqualTo(2));
        Assert.That((int)TaskPriority.Critical, Is.EqualTo(3));
    }

    [Test]
    public void TaskListEventArgs_StoresValues()
    {
        var tasks = new List<TaskItem> { new TaskItem() };
        var args = new TaskListEventArgs(tasks, "test.bl");

        Assert.That(args.Tasks, Is.SameAs(tasks));
        Assert.That(args.FilePath, Is.EqualTo("test.bl"));
    }

    #endregion

    #region Priority Tests

    [Test]
    public void ScanDocument_FixmeHasHighPriority()
    {
        var tasks = _service.ScanDocument("' FIXME: Fix me", "test.bl");
        Assert.That(tasks[0].Priority, Is.EqualTo(TaskPriority.High));
    }

    [Test]
    public void ScanDocument_BugHasHighPriority()
    {
        var tasks = _service.ScanDocument("' BUG: Bug here", "test.bl");
        Assert.That(tasks[0].Priority, Is.EqualTo(TaskPriority.High));
    }

    [Test]
    public void ScanDocument_TodoHasNormalPriority()
    {
        var tasks = _service.ScanDocument("' TODO: Do this", "test.bl");
        Assert.That(tasks[0].Priority, Is.EqualTo(TaskPriority.Normal));
    }

    [Test]
    public void ScanDocument_NoteHasLowPriority()
    {
        var tasks = _service.ScanDocument("' NOTE: Note this", "test.bl");
        Assert.That(tasks[0].Priority, Is.EqualTo(TaskPriority.Low));
    }

    #endregion
}
