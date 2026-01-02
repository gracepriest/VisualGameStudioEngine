using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.Tests.Core;

[TestFixture]
public class CodeSnippetTests
{
    [Test]
    public void DefaultValues_AreSetCorrectly()
    {
        var snippet = new CodeSnippet();

        Assert.That(snippet.Shortcut, Is.EqualTo(""));
        Assert.That(snippet.Title, Is.EqualTo(""));
        Assert.That(snippet.Description, Is.EqualTo(""));
        Assert.That(snippet.Body, Is.EqualTo(""));
        Assert.That(snippet.Category, Is.EqualTo("General"));
        Assert.That(snippet.Variables, Is.Empty);
    }

    [Test]
    public void Shortcut_CanBeSetAndRetrieved()
    {
        var snippet = new CodeSnippet { Shortcut = "for" };

        Assert.That(snippet.Shortcut, Is.EqualTo("for"));
    }

    [Test]
    public void Title_CanBeSetAndRetrieved()
    {
        var snippet = new CodeSnippet { Title = "For Loop" };

        Assert.That(snippet.Title, Is.EqualTo("For Loop"));
    }

    [Test]
    public void Description_CanBeSetAndRetrieved()
    {
        var snippet = new CodeSnippet { Description = "Creates a for loop" };

        Assert.That(snippet.Description, Is.EqualTo("Creates a for loop"));
    }

    [Test]
    public void Body_CanBeSetAndRetrieved()
    {
        var body = "For i = 0 To $end$\n    $body$\nNext";
        var snippet = new CodeSnippet { Body = body };

        Assert.That(snippet.Body, Is.EqualTo(body));
    }

    [Test]
    public void Category_CanBeSetAndRetrieved()
    {
        var snippet = new CodeSnippet { Category = "Loops" };

        Assert.That(snippet.Category, Is.EqualTo("Loops"));
    }

    [Test]
    public void Variables_CanBeSetAndRetrieved()
    {
        var variables = new List<SnippetVariable>
        {
            new() { Name = "end", DefaultValue = "10" },
            new() { Name = "body", DefaultValue = "' code here" }
        };
        var snippet = new CodeSnippet { Variables = variables };

        Assert.That(snippet.Variables, Has.Count.EqualTo(2));
    }

    [Test]
    public void AllProperties_CanBeSetTogether()
    {
        var variables = new List<SnippetVariable>
        {
            new() { Name = "var", DefaultValue = "x" }
        };

        var snippet = new CodeSnippet
        {
            Shortcut = "if",
            Title = "If Statement",
            Description = "Creates an if statement",
            Body = "If $condition$ Then\n    $body$\nEnd If",
            Category = "Control Flow",
            Variables = variables
        };

        Assert.That(snippet.Shortcut, Is.EqualTo("if"));
        Assert.That(snippet.Title, Is.EqualTo("If Statement"));
        Assert.That(snippet.Description, Is.EqualTo("Creates an if statement"));
        Assert.That(snippet.Body, Does.Contain("If"));
        Assert.That(snippet.Category, Is.EqualTo("Control Flow"));
        Assert.That(snippet.Variables, Has.Count.EqualTo(1));
    }

    [Test]
    public void Body_CanContainMultilineText()
    {
        var body = @"Sub $name$()
    $body$
End Sub";
        var snippet = new CodeSnippet { Body = body };

        Assert.That(snippet.Body, Does.Contain("Sub"));
        Assert.That(snippet.Body, Does.Contain("End Sub"));
    }

    [Test]
    public void Body_CanContainPlaceholders()
    {
        var snippet = new CodeSnippet
        {
            Body = "Dim $varName$ As $varType$ = $value$"
        };

        Assert.That(snippet.Body, Does.Contain("$varName$"));
        Assert.That(snippet.Body, Does.Contain("$varType$"));
        Assert.That(snippet.Body, Does.Contain("$value$"));
    }
}

[TestFixture]
public class SnippetVariableTests
{
    [Test]
    public void DefaultValues_AreSetCorrectly()
    {
        var variable = new SnippetVariable();

        Assert.That(variable.Name, Is.EqualTo(""));
        Assert.That(variable.DefaultValue, Is.EqualTo(""));
        Assert.That(variable.Description, Is.Null);
    }

    [Test]
    public void Name_CanBeSetAndRetrieved()
    {
        var variable = new SnippetVariable { Name = "counter" };

        Assert.That(variable.Name, Is.EqualTo("counter"));
    }

    [Test]
    public void DefaultValue_CanBeSetAndRetrieved()
    {
        var variable = new SnippetVariable { DefaultValue = "0" };

        Assert.That(variable.DefaultValue, Is.EqualTo("0"));
    }

    [Test]
    public void Description_CanBeSetAndRetrieved()
    {
        var variable = new SnippetVariable { Description = "The loop counter variable" };

        Assert.That(variable.Description, Is.EqualTo("The loop counter variable"));
    }

    [Test]
    public void Description_CanBeNull()
    {
        var variable = new SnippetVariable
        {
            Name = "test",
            DefaultValue = "value",
            Description = null
        };

        Assert.That(variable.Description, Is.Null);
    }

    [Test]
    public void AllProperties_CanBeSetTogether()
    {
        var variable = new SnippetVariable
        {
            Name = "index",
            DefaultValue = "i",
            Description = "Loop index variable"
        };

        Assert.That(variable.Name, Is.EqualTo("index"));
        Assert.That(variable.DefaultValue, Is.EqualTo("i"));
        Assert.That(variable.Description, Is.EqualTo("Loop index variable"));
    }

    [Test]
    public void DefaultValue_CanContainCode()
    {
        var variable = new SnippetVariable
        {
            Name = "body",
            DefaultValue = "Console.WriteLine(i)"
        };

        Assert.That(variable.DefaultValue, Is.EqualTo("Console.WriteLine(i)"));
    }

    [Test]
    public void DefaultValue_CanBeEmpty()
    {
        var variable = new SnippetVariable
        {
            Name = "optional",
            DefaultValue = ""
        };

        Assert.That(variable.DefaultValue, Is.EqualTo(""));
    }

    [Test]
    public void Name_CanContainSpecialCharacters()
    {
        var variable = new SnippetVariable { Name = "my_var_1" };

        Assert.That(variable.Name, Is.EqualTo("my_var_1"));
    }
}
