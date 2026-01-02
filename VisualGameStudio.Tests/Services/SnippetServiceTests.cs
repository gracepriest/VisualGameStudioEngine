using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

[TestFixture]
public class SnippetServiceTests
{
    private SnippetService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _service = new SnippetService();
    }

    [Test]
    public void Constructor_LoadsBuiltInSnippets()
    {
        var snippets = _service.GetSnippets();

        Assert.That(snippets, Is.Not.Empty);
        Assert.That(snippets.Count, Is.GreaterThan(20));
    }

    [Test]
    public void GetSnippet_ReturnsCorrectSnippet()
    {
        var snippet = _service.GetSnippet("if");

        Assert.That(snippet, Is.Not.Null);
        Assert.That(snippet!.Shortcut, Is.EqualTo("if"));
        Assert.That(snippet.Title, Is.EqualTo("If Statement"));
    }

    [Test]
    public void GetSnippet_CaseInsensitive()
    {
        var snippet1 = _service.GetSnippet("IF");
        var snippet2 = _service.GetSnippet("if");
        var snippet3 = _service.GetSnippet("If");

        Assert.That(snippet1, Is.Not.Null);
        Assert.That(snippet2, Is.Not.Null);
        Assert.That(snippet3, Is.Not.Null);
        Assert.That(snippet1!.Shortcut, Is.EqualTo(snippet2!.Shortcut));
    }

    [Test]
    public void GetSnippet_NonExistent_ReturnsNull()
    {
        var snippet = _service.GetSnippet("nonexistent");

        Assert.That(snippet, Is.Null);
    }

    [Test]
    public void SearchSnippets_ByShortcut()
    {
        var results = _service.SearchSnippets("for");

        Assert.That(results, Is.Not.Empty);
        Assert.That(results.Any(s => s.Shortcut == "for"), Is.True);
    }

    [Test]
    public void SearchSnippets_ByTitle()
    {
        var results = _service.SearchSnippets("Loop");

        Assert.That(results, Is.Not.Empty);
        Assert.That(results.All(s => s.Title.Contains("Loop", StringComparison.OrdinalIgnoreCase) ||
                                     s.Description.Contains("Loop", StringComparison.OrdinalIgnoreCase) ||
                                     s.Shortcut.Contains("Loop", StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    [Test]
    public void SearchSnippets_EmptyQuery_ReturnsAll()
    {
        var allSnippets = _service.GetSnippets();
        var searchResults = _service.SearchSnippets("");

        Assert.That(searchResults.Count, Is.EqualTo(allSnippets.Count));
    }

    [Test]
    public void SearchSnippets_WhitespaceQuery_ReturnsAll()
    {
        var allSnippets = _service.GetSnippets();
        var searchResults = _service.SearchSnippets("   ");

        Assert.That(searchResults.Count, Is.EqualTo(allSnippets.Count));
    }

    [Test]
    public void RegisterSnippet_AddsNewSnippet()
    {
        var customSnippet = new CodeSnippet
        {
            Shortcut = "custom",
            Title = "Custom Snippet",
            Description = "Test snippet",
            Category = "Test",
            Body = "Custom body"
        };

        _service.RegisterSnippet(customSnippet);

        var retrieved = _service.GetSnippet("custom");
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved!.Title, Is.EqualTo("Custom Snippet"));
    }

    [Test]
    public void RegisterSnippet_ReplacesExisting()
    {
        var replacement = new CodeSnippet
        {
            Shortcut = "if",
            Title = "Replaced If",
            Description = "Replacement",
            Category = "Test",
            Body = "New body"
        };

        _service.RegisterSnippet(replacement);

        var snippet = _service.GetSnippet("if");
        Assert.That(snippet!.Title, Is.EqualTo("Replaced If"));
    }

    [Test]
    public void RemoveSnippet_RemovesExisting()
    {
        var beforeRemove = _service.GetSnippet("if");
        Assert.That(beforeRemove, Is.Not.Null);

        _service.RemoveSnippet("if");

        var afterRemove = _service.GetSnippet("if");
        Assert.That(afterRemove, Is.Null);
    }

    [Test]
    public void RemoveSnippet_NonExistent_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => _service.RemoveSnippet("nonexistent"));
    }

    [Test]
    public void ExpandSnippet_ReplacesVariables()
    {
        var snippet = new CodeSnippet
        {
            Shortcut = "test",
            Title = "Test",
            Description = "Test",
            Category = "Test",
            Body = "Dim ${1:varName} As ${2:Integer}"
        };

        var variables = new Dictionary<string, string>
        {
            ["1"] = "counter",
            ["2"] = "String"
        };

        var result = _service.ExpandSnippet(snippet, variables);

        Assert.That(result, Is.EqualTo("Dim counter As String"));
    }

    [Test]
    public void ExpandSnippet_UsesDefaults()
    {
        var snippet = new CodeSnippet
        {
            Shortcut = "test",
            Title = "Test",
            Description = "Test",
            Category = "Test",
            Body = "Dim ${1:varName} As ${2:Integer}"
        };

        var result = _service.ExpandSnippet(snippet);

        Assert.That(result, Is.EqualTo("Dim varName As Integer"));
    }

    [Test]
    public void ExpandSnippet_PartialVariables()
    {
        var snippet = new CodeSnippet
        {
            Shortcut = "test",
            Title = "Test",
            Description = "Test",
            Category = "Test",
            Body = "Dim ${1:varName} As ${2:Integer}"
        };

        var variables = new Dictionary<string, string>
        {
            ["1"] = "myVar"
        };

        var result = _service.ExpandSnippet(snippet, variables);

        Assert.That(result, Is.EqualTo("Dim myVar As Integer"));
    }

    [Test]
    public void ExpandSnippet_SimplePlaceholder()
    {
        var snippet = new CodeSnippet
        {
            Shortcut = "test",
            Title = "Test",
            Description = "Test",
            Category = "Test",
            Body = "Value: ${1}"
        };

        var variables = new Dictionary<string, string>
        {
            ["1"] = "test"
        };

        var result = _service.ExpandSnippet(snippet, variables);

        Assert.That(result, Is.EqualTo("Value: test"));
    }

    [Test]
    public void ExpandSnippet_SimplePlaceholder_NoVariable_ReturnsEmpty()
    {
        var snippet = new CodeSnippet
        {
            Shortcut = "test",
            Title = "Test",
            Description = "Test",
            Category = "Test",
            Body = "Value: ${1}"
        };

        var result = _service.ExpandSnippet(snippet);

        Assert.That(result, Is.EqualTo("Value: "));
    }

    [Test]
    public void BuiltInSnippets_ControlFlow_Exist()
    {
        Assert.That(_service.GetSnippet("if"), Is.Not.Null);
        Assert.That(_service.GetSnippet("ife"), Is.Not.Null);
        Assert.That(_service.GetSnippet("for"), Is.Not.Null);
        Assert.That(_service.GetSnippet("while"), Is.Not.Null);
        Assert.That(_service.GetSnippet("sel"), Is.Not.Null);
        Assert.That(_service.GetSnippet("try"), Is.Not.Null);
    }

    [Test]
    public void BuiltInSnippets_Declarations_Exist()
    {
        Assert.That(_service.GetSnippet("sub"), Is.Not.Null);
        Assert.That(_service.GetSnippet("func"), Is.Not.Null);
        Assert.That(_service.GetSnippet("class"), Is.Not.Null);
        Assert.That(_service.GetSnippet("mod"), Is.Not.Null);
        Assert.That(_service.GetSnippet("prop"), Is.Not.Null);
    }

    [Test]
    public void BuiltInSnippets_Variables_Exist()
    {
        Assert.That(_service.GetSnippet("dim"), Is.Not.Null);
        Assert.That(_service.GetSnippet("dims"), Is.Not.Null);
        Assert.That(_service.GetSnippet("dimi"), Is.Not.Null);
        Assert.That(_service.GetSnippet("arr"), Is.Not.Null);
    }

    [Test]
    public void BuiltInSnippets_HaveCategories()
    {
        var snippets = _service.GetSnippets();
        var categories = snippets.Select(s => s.Category).Distinct().ToList();

        Assert.That(categories, Does.Contain("Control Flow"));
        Assert.That(categories, Does.Contain("Declarations"));
        Assert.That(categories, Does.Contain("Variables"));
    }

    [Test]
    public void GetSnippets_ReturnsReadOnlyList()
    {
        var snippets = _service.GetSnippets();

        Assert.That(snippets, Is.InstanceOf<IReadOnlyList<CodeSnippet>>());
    }
}

[TestFixture]
public class CodeSnippetModelTests
{
    [Test]
    public void DefaultSnippet_HasDefaultValues()
    {
        var snippet = new CodeSnippet();

        Assert.That(snippet.Shortcut, Is.EqualTo(""));
        Assert.That(snippet.Title, Is.EqualTo(""));
        Assert.That(snippet.Description, Is.EqualTo(""));
        Assert.That(snippet.Category, Is.EqualTo("General")); // Default category
        Assert.That(snippet.Body, Is.EqualTo(""));
        Assert.That(snippet.Variables, Is.Empty);
    }

    [Test]
    public void Snippet_CanSetAllProperties()
    {
        var variables = new[]
        {
            new SnippetVariable { Name = "1", DefaultValue = "test", Description = "Test var" }
        };

        var snippet = new CodeSnippet
        {
            Shortcut = "test",
            Title = "Test Title",
            Description = "Test Description",
            Category = "Test Category",
            Body = "Test Body ${1:test}",
            Variables = variables
        };

        Assert.That(snippet.Shortcut, Is.EqualTo("test"));
        Assert.That(snippet.Title, Is.EqualTo("Test Title"));
        Assert.That(snippet.Description, Is.EqualTo("Test Description"));
        Assert.That(snippet.Category, Is.EqualTo("Test Category"));
        Assert.That(snippet.Body, Is.EqualTo("Test Body ${1:test}"));
        Assert.That(snippet.Variables, Has.Count.EqualTo(1));
    }
}

[TestFixture]
public class SnippetVariableModelTests
{
    [Test]
    public void DefaultVariable_HasDefaultValues()
    {
        var variable = new SnippetVariable();

        Assert.That(variable.Name, Is.EqualTo(""));
        Assert.That(variable.DefaultValue, Is.EqualTo(""));
        Assert.That(variable.Description, Is.Null); // Nullable, defaults to null
    }

    [Test]
    public void Variable_CanSetAllProperties()
    {
        var variable = new SnippetVariable
        {
            Name = "1",
            DefaultValue = "defaultVal",
            Description = "Variable description"
        };

        Assert.That(variable.Name, Is.EqualTo("1"));
        Assert.That(variable.DefaultValue, Is.EqualTo("defaultVal"));
        Assert.That(variable.Description, Is.EqualTo("Variable description"));
    }
}
