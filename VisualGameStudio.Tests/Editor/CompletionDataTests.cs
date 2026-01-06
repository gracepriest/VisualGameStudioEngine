using Avalonia.Controls;
using NUnit.Framework;
using VisualGameStudio.Editor.Completion;

namespace VisualGameStudio.Tests.Editor;

[TestFixture]
public class CompletionDataTests
{
    [Test]
    public void Constructor_SetsTextCorrectly()
    {
        var data = new CompletionData("TestItem");

        Assert.That(data.Text, Is.EqualTo("TestItem"));
    }

    [Test]
    public void Constructor_DefaultDescription_IsNull()
    {
        var data = new CompletionData("TestItem");

        Assert.That(data.Description, Is.Null);
    }

    [Test]
    public void Constructor_WithDescription_SetsDescription()
    {
        var data = new CompletionData("TestItem", "Test description");

        Assert.That(data.Description, Is.EqualTo("Test description"));
    }

    [Test]
    public void Constructor_DefaultKind_IsText()
    {
        var data = new CompletionData("TestItem");

        Assert.That(data.Kind, Is.EqualTo(CompletionItemKind.Text));
    }

    [Test]
    public void Constructor_WithKind_SetsKind()
    {
        var data = new CompletionData("TestMethod", kind: CompletionItemKind.Method);

        Assert.That(data.Kind, Is.EqualTo(CompletionItemKind.Method));
    }

    [Test]
    public void Constructor_DefaultInsertText_EqualsText()
    {
        var data = new CompletionData("TestItem");

        Assert.That(data.InsertText, Is.EqualTo("TestItem"));
    }

    [Test]
    public void Constructor_WithInsertText_SetsInsertText()
    {
        var data = new CompletionData("DisplayText", insertText: "ActualInsertText");

        Assert.That(data.InsertText, Is.EqualTo("ActualInsertText"));
    }

    [Test]
    public void Content_ReturnsTextBlock()
    {
        var data = new CompletionData("TestItem", "Description");

        Assert.That(data.Content, Is.InstanceOf<TextBlock>());
        var textBlock = (TextBlock)data.Content;
        Assert.That(textBlock.Text, Is.EqualTo("TestItem"));
    }

    [Test]
    public void Image_IsNull()
    {
        var data = new CompletionData("TestItem");

        Assert.That(data.Image, Is.Null);
    }

    [Test]
    public void Priority_Keyword_HasHighestPriority()
    {
        var keyword = new CompletionData("If", kind: CompletionItemKind.Keyword);
        var method = new CompletionData("DoSomething", kind: CompletionItemKind.Method);

        Assert.That(keyword.Priority, Is.LessThan(method.Priority));
    }

    [Test]
    public void Priority_Snippet_HasHighPriority()
    {
        var snippet = new CompletionData("forloop", kind: CompletionItemKind.Snippet);
        var method = new CompletionData("DoSomething", kind: CompletionItemKind.Method);

        Assert.That(snippet.Priority, Is.LessThan(method.Priority));
    }

    [Test]
    public void Priority_Method_HasHigherPriorityThanVariable()
    {
        var method = new CompletionData("DoSomething", kind: CompletionItemKind.Method);
        var variable = new CompletionData("myVar", kind: CompletionItemKind.Variable);

        Assert.That(method.Priority, Is.LessThan(variable.Priority));
    }

    [Test]
    public void Priority_Function_HasHigherPriorityThanField()
    {
        var function = new CompletionData("Calculate", kind: CompletionItemKind.Function);
        var field = new CompletionData("_value", kind: CompletionItemKind.Field);

        Assert.That(function.Priority, Is.LessThan(field.Priority));
    }

    [Test]
    public void Priority_Variable_HasHigherPriorityThanClass()
    {
        var variable = new CompletionData("myVar", kind: CompletionItemKind.Variable);
        var classItem = new CompletionData("MyClass", kind: CompletionItemKind.Class);

        Assert.That(variable.Priority, Is.LessThan(classItem.Priority));
    }

    [Test]
    public void Priority_Class_HasHigherPriorityThanModule()
    {
        var classItem = new CompletionData("MyClass", kind: CompletionItemKind.Class);
        var module = new CompletionData("MyModule", kind: CompletionItemKind.Module);

        Assert.That(classItem.Priority, Is.LessThan(module.Priority));
    }

    [Test]
    public void Priority_Interface_EqualToClass()
    {
        var classItem = new CompletionData("MyClass", kind: CompletionItemKind.Class);
        var interfaceItem = new CompletionData("IMyInterface", kind: CompletionItemKind.Interface);

        Assert.That(classItem.Priority, Is.EqualTo(interfaceItem.Priority));
    }

    [Test]
    public void Priority_Text_HasLowestPriority()
    {
        var text = new CompletionData("sometext", kind: CompletionItemKind.Text);
        var module = new CompletionData("MyModule", kind: CompletionItemKind.Module);

        Assert.That(text.Priority, Is.GreaterThan(module.Priority));
    }

    [Test]
    public void Priority_UnknownKind_HasLowestPriority()
    {
        var property = new CompletionData("MyProperty", kind: CompletionItemKind.Property);
        var keyword = new CompletionData("If", kind: CompletionItemKind.Keyword);

        Assert.That(property.Priority, Is.GreaterThan(keyword.Priority));
    }

    [TestCase(CompletionItemKind.Text, 1)]
    [TestCase(CompletionItemKind.Method, 2)]
    [TestCase(CompletionItemKind.Function, 3)]
    [TestCase(CompletionItemKind.Constructor, 4)]
    [TestCase(CompletionItemKind.Field, 5)]
    [TestCase(CompletionItemKind.Variable, 6)]
    [TestCase(CompletionItemKind.Class, 7)]
    [TestCase(CompletionItemKind.Interface, 8)]
    [TestCase(CompletionItemKind.Module, 9)]
    [TestCase(CompletionItemKind.Property, 10)]
    [TestCase(CompletionItemKind.Keyword, 14)]
    [TestCase(CompletionItemKind.Snippet, 15)]
    public void CompletionItemKind_HasCorrectValue(CompletionItemKind kind, int expectedValue)
    {
        Assert.That((int)kind, Is.EqualTo(expectedValue));
    }

    [Test]
    public void Constructor_AllParameters_SetsAllCorrectly()
    {
        var data = new CompletionData(
            text: "DisplayName",
            description: "This is a description",
            kind: CompletionItemKind.Method,
            insertText: "InsertedCode()"
        );

        Assert.That(data.Text, Is.EqualTo("DisplayName"));
        Assert.That(data.Description, Is.EqualTo("This is a description"));
        Assert.That(data.Kind, Is.EqualTo(CompletionItemKind.Method));
        Assert.That(data.InsertText, Is.EqualTo("InsertedCode()"));
    }

    [Test]
    public void CompletionItemKind_Enum_ContainsExpectedValues()
    {
        Assert.That(Enum.GetNames<CompletionItemKind>(), Has.Length.EqualTo(25));
    }

    [Test]
    public void CompletionItemKind_Unit_HasCorrectValue()
    {
        Assert.That((int)CompletionItemKind.Unit, Is.EqualTo(11));
    }

    [Test]
    public void CompletionItemKind_Enum_HasCorrectValue()
    {
        Assert.That((int)CompletionItemKind.Enum, Is.EqualTo(13));
    }

    [Test]
    public void CompletionItemKind_TypeParameter_HasHighestEnumValue()
    {
        Assert.That((int)CompletionItemKind.TypeParameter, Is.EqualTo(25));
    }
}
