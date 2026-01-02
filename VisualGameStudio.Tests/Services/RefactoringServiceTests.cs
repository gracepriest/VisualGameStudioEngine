using NUnit.Framework;
using Moq;
using System.Threading;
using System.Threading.Tasks;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

[TestFixture]
public class RefactoringServiceTests
{
    private Mock<IFileService> _fileServiceMock = null!;
    private Mock<IProjectService> _projectServiceMock = null!;
    private RefactoringService _service = null!;

    [SetUp]
    public void Setup()
    {
        _fileServiceMock = new Mock<IFileService>();
        _projectServiceMock = new Mock<IProjectService>();
        _service = new RefactoringService(_projectServiceMock.Object, _fileServiceMock.Object);
    }

    #region GetCodeActionsAsync Tests

    [Test]
    public async Task GetCodeActionsAsync_ReturnsAvailableActions()
    {
        var actions = await _service.GetCodeActionsAsync("test.bas", 1, 1);

        Assert.That(actions, Is.Not.Null);
        Assert.That(actions.Count, Is.GreaterThan(0));
    }

    [Test]
    public async Task GetCodeActionsAsync_ContainsRenameAction()
    {
        var actions = await _service.GetCodeActionsAsync("test.bas", 1, 1);

        Assert.That(actions.Any(a => a.Id == "rename"), Is.True);
    }

    [Test]
    public async Task GetCodeActionsAsync_ContainsExtractMethodAction()
    {
        var actions = await _service.GetCodeActionsAsync("test.bas", 1, 1);

        Assert.That(actions.Any(a => a.Id == "extract-method"), Is.True);
    }

    [Test]
    public async Task GetCodeActionsAsync_ActionsHaveTitles()
    {
        var actions = await _service.GetCodeActionsAsync("test.bas", 1, 1);

        foreach (var action in actions)
        {
            Assert.That(action.Title, Is.Not.Null.And.Not.Empty);
        }
    }

    #endregion

    #region ApplyCodeActionAsync Tests

    [Test]
    public async Task ApplyCodeActionAsync_WithoutContext_ReturnsEmpty()
    {
        var action = new CodeAction { Id = "rename" };

        var edits = await _service.ApplyCodeActionAsync(action);

        Assert.That(edits, Is.Empty);
    }

    [Test]
    public async Task ApplyCodeActionAsync_GenerateSub_CreatesSubroutine()
    {
        var context = new RefactoringService.CodeActionContext
        {
            FilePath = "test.bas",
            Line = 10,
            MethodName = "TestMethod"
        };
        var action = new CodeAction
        {
            Id = "generate-sub",
            Data = context
        };

        var edits = await _service.ApplyCodeActionAsync(action);

        Assert.That(edits.Length, Is.EqualTo(1));
        Assert.That(edits[0].NewText, Does.Contain("Sub TestMethod"));
        Assert.That(edits[0].NewText, Does.Contain("End Sub"));
    }

    [Test]
    public async Task ApplyCodeActionAsync_GenerateFunction_CreatesFunction()
    {
        var context = new RefactoringService.CodeActionContext
        {
            FilePath = "test.bas",
            Line = 10,
            MethodName = "GetValue",
            VariableType = "Integer"
        };
        var action = new CodeAction
        {
            Id = "generate-function",
            Data = context
        };

        var edits = await _service.ApplyCodeActionAsync(action);

        Assert.That(edits.Length, Is.EqualTo(1));
        Assert.That(edits[0].NewText, Does.Contain("Function GetValue"));
        Assert.That(edits[0].NewText, Does.Contain("As Integer"));
        Assert.That(edits[0].NewText, Does.Contain("End Function"));
    }

    [Test]
    public async Task ApplyCodeActionAsync_GenerateFunction_DefaultsToObject()
    {
        var context = new RefactoringService.CodeActionContext
        {
            FilePath = "test.bas",
            Line = 10,
            MethodName = "GetValue"
        };
        var action = new CodeAction
        {
            Id = "generate-function",
            Data = context
        };

        var edits = await _service.ApplyCodeActionAsync(action);

        Assert.That(edits[0].NewText, Does.Contain("As Object"));
    }

    [Test]
    public async Task ApplyCodeActionAsync_UnknownAction_ReturnsEmpty()
    {
        var context = new RefactoringService.CodeActionContext
        {
            FilePath = "test.bas"
        };
        var action = new CodeAction
        {
            Id = "unknown-action",
            Data = context
        };

        var edits = await _service.ApplyCodeActionAsync(action);

        Assert.That(edits, Is.Empty);
    }

    #endregion

    #region RenameSymbolAsync Tests

    [Test]
    public async Task RenameSymbolAsync_WithValidContent_ReturnsResult()
    {
        var content = @"Sub Test()
    Dim x As Integer
    x = 10
    PrintLine(x)
End Sub";
        _fileServiceMock.Setup(f => f.ReadFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(content);

        var result = await _service.RenameSymbolAsync("test.bas", 2, 9, "newName");

        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task RenameSymbolAsync_WithInvalidLine_ReturnsFailure()
    {
        _fileServiceMock.Setup(f => f.ReadFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Single line");

        var result = await _service.RenameSymbolAsync("test.bas", 100, 1, "newName");

        Assert.That(result.Success, Is.False);
    }

    #endregion

    #region FindAllReferencesAsync Tests

    [Test]
    public async Task FindAllReferencesAsync_WithValidSymbol_ReturnsReferences()
    {
        var content = @"Dim counter As Integer
counter = 0
counter = counter + 1
PrintLine(counter)";
        _fileServiceMock.Setup(f => f.ReadFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(content);

        var references = await _service.FindAllReferencesAsync("test.bas", 1, 5);

        Assert.That(references, Is.Not.Null);
    }

    #endregion

    #region ExtractMethodAsync Tests

    [Test]
    public async Task ExtractMethodAsync_WithValidSelection_ReturnsResult()
    {
        var content = @"Sub Main()
    Dim x As Integer
    x = 10
    x = x + 5
    PrintLine(x)
End Sub";
        _fileServiceMock.Setup(f => f.ReadFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(content);

        var result = await _service.ExtractMethodAsync("test.bas", 3, 1, 4, 15, "NewMethod");

        Assert.That(result, Is.Not.Null);
    }

    #endregion

    #region GetSurroundWithOptionsAsync Tests

    [Test]
    public async Task GetSurroundWithOptionsAsync_ReturnsOptions()
    {
        var options = await _service.GetSurroundWithOptionsAsync("test.bas", 1, 1, 5, 10);

        Assert.That(options, Is.Not.Null);
        Assert.That(options.Count, Is.GreaterThan(0));
    }

    [Test]
    public async Task GetSurroundWithOptionsAsync_ContainsTryCatch()
    {
        var options = await _service.GetSurroundWithOptionsAsync("test.bas", 1, 1, 5, 10);

        Assert.That(options.Any(o => o.Type == SurroundWithType.TryCatch), Is.True);
    }

    [Test]
    public async Task GetSurroundWithOptionsAsync_ContainsIfThen()
    {
        var options = await _service.GetSurroundWithOptionsAsync("test.bas", 1, 1, 5, 10);

        Assert.That(options.Any(o => o.Type == SurroundWithType.IfThen), Is.True);
    }

    #endregion

    #region GoToDefinitionAsync Tests

    [Test]
    public async Task GoToDefinitionAsync_WithValidSymbol_ReturnsResult()
    {
        var content = @"Sub MyMethod()
    PrintLine(""Hello"")
End Sub

Sub Main()
    MyMethod()
End Sub";
        _fileServiceMock.Setup(f => f.ReadFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(content);

        var result = await _service.GoToDefinitionAsync("test.bas", 6, 5);

        Assert.That(result, Is.Not.Null);
    }

    #endregion

    #region IntroduceVariableAsync Tests

    [Test]
    public async Task IntroduceVariableAsync_WithValidSelection_ReturnsResult()
    {
        var content = @"Sub Main()
    PrintLine(10 + 20)
End Sub";
        _fileServiceMock.Setup(f => f.ReadFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(content);

        var result = await _service.IntroduceVariableAsync("test.bas", 2, 14, 2, 21, "sum");

        Assert.That(result, Is.Not.Null);
    }

    #endregion
}
