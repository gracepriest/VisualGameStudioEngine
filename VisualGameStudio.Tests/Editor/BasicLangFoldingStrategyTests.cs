using AvaloniaEdit.Document;
using NUnit.Framework;
using VisualGameStudio.Editor.Folding;

namespace VisualGameStudio.Tests.Editor;

[TestFixture]
public class BasicLangFoldingStrategyTests
{
    private BasicLangFoldingStrategy _strategy = null!;

    [SetUp]
    public void SetUp()
    {
        _strategy = new BasicLangFoldingStrategy();
    }

    [Test]
    public void CreateFoldings_EmptyDocument_ReturnsNoFoldings()
    {
        var document = new TextDocument("");

        var foldings = _strategy.CreateFoldings(document).ToList();

        Assert.That(foldings, Is.Empty);
    }

    [Test]
    public void CreateFoldings_SubBlock_CreatesFolding()
    {
        var code = """
            Sub TestMethod()
                Dim x = 1
            End Sub
            """;
        var document = new TextDocument(code);

        var foldings = _strategy.CreateFoldings(document).ToList();

        Assert.That(foldings, Has.Count.EqualTo(1));
        Assert.That(foldings[0].Name, Does.StartWith("Sub TestMethod"));
    }

    [Test]
    public void CreateFoldings_FunctionBlock_CreatesFolding()
    {
        var code = """
            Function Calculate(x As Integer) As Integer
                Return x * 2
            End Function
            """;
        var document = new TextDocument(code);

        var foldings = _strategy.CreateFoldings(document).ToList();

        Assert.That(foldings, Has.Count.EqualTo(1));
        Assert.That(foldings[0].Name, Does.StartWith("Function Calculate"));
    }

    [Test]
    public void CreateFoldings_ClassBlock_CreatesFolding()
    {
        var code = """
            Class MyClass
                Dim value As Integer
            End Class
            """;
        var document = new TextDocument(code);

        var foldings = _strategy.CreateFoldings(document).ToList();

        Assert.That(foldings, Has.Count.EqualTo(1));
        Assert.That(foldings[0].Name, Does.StartWith("Class MyClass"));
    }

    [Test]
    public void CreateFoldings_ModuleBlock_CreatesFolding()
    {
        var code = """
            Module TestModule
                Sub Main()
                End Sub
            End Module
            """;
        var document = new TextDocument(code);

        var foldings = _strategy.CreateFoldings(document).ToList();

        Assert.That(foldings, Has.Count.EqualTo(2));
    }

    [Test]
    public void CreateFoldings_NestedBlocks_CreatesMultipleFoldings()
    {
        var code = """
            Namespace TestNamespace
                Class TestClass
                    Sub TestMethod()
                        Dim x = 1
                    End Sub
                End Class
            End Namespace
            """;
        var document = new TextDocument(code);

        var foldings = _strategy.CreateFoldings(document).ToList();

        Assert.That(foldings, Has.Count.EqualTo(3));
    }

    [Test]
    public void CreateFoldings_IfBlock_CreatesFolding()
    {
        var code = """
            If condition Then
                DoSomething()
            End If
            """;
        var document = new TextDocument(code);

        var foldings = _strategy.CreateFoldings(document).ToList();

        Assert.That(foldings, Has.Count.EqualTo(1));
        Assert.That(foldings[0].Name, Does.StartWith("If condition"));
    }

    [Test]
    public void CreateFoldings_SingleLineIf_DoesNotCreateFolding()
    {
        var code = "If x > 0 Then Return x";
        var document = new TextDocument(code);

        var foldings = _strategy.CreateFoldings(document).ToList();

        Assert.That(foldings, Is.Empty);
    }

    [Test]
    public void CreateFoldings_ForLoop_CreatesFolding()
    {
        var code = """
            For i = 0 To 10
                Console.WriteLine(i)
            Next
            """;
        var document = new TextDocument(code);

        var foldings = _strategy.CreateFoldings(document).ToList();

        Assert.That(foldings, Has.Count.EqualTo(1));
        Assert.That(foldings[0].Name, Does.StartWith("For i"));
    }

    [Test]
    public void CreateFoldings_WhileLoop_CreatesFolding()
    {
        var code = """
            While condition
                DoWork()
            Wend
            """;
        var document = new TextDocument(code);

        var foldings = _strategy.CreateFoldings(document).ToList();

        Assert.That(foldings, Has.Count.EqualTo(1));
        Assert.That(foldings[0].Name, Does.StartWith("While condition"));
    }

    [Test]
    public void CreateFoldings_DoLoop_CreatesFolding()
    {
        var code = """
            Do
                DoWork()
            Loop
            """;
        var document = new TextDocument(code);

        var foldings = _strategy.CreateFoldings(document).ToList();

        Assert.That(foldings, Has.Count.EqualTo(1));
    }

    [Test]
    public void CreateFoldings_SelectCase_CreatesFolding()
    {
        var code = """
            Select Case value
                Case 1
                    DoOne()
                Case 2
                    DoTwo()
            End Select
            """;
        var document = new TextDocument(code);

        var foldings = _strategy.CreateFoldings(document).ToList();

        Assert.That(foldings, Has.Count.EqualTo(1));
        Assert.That(foldings[0].Name, Does.StartWith("Select Case"));
    }

    [Test]
    public void CreateFoldings_TryCatch_CreatesFolding()
    {
        var code = """
            Try
                RiskyOperation()
            End Try
            """;
        var document = new TextDocument(code);

        var foldings = _strategy.CreateFoldings(document).ToList();

        Assert.That(foldings, Has.Count.EqualTo(1));
        Assert.That(foldings[0].Name, Does.StartWith("Try"));
    }

    [Test]
    public void CreateFoldings_PropertyBlock_CreatesFolding()
    {
        var code = """
            Property Value As Integer
                Get
                    Return _value
                End Get
            End Property
            """;
        var document = new TextDocument(code);

        var foldings = _strategy.CreateFoldings(document).ToList();

        Assert.That(foldings, Has.Count.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void CreateFoldings_InterfaceBlock_CreatesFolding()
    {
        var code = """
            Interface ITestable
                ' Interface members
            End Interface
            """;
        var document = new TextDocument(code);

        var foldings = _strategy.CreateFoldings(document).ToList();

        Assert.That(foldings, Has.Count.EqualTo(1));
        Assert.That(foldings[0].Name, Does.StartWith("Interface ITestable"));
    }

    [Test]
    public void CreateFoldings_EnumBlock_CreatesFolding()
    {
        var code = """
            Enum Colors
                Red
                Green
                Blue
            End Enum
            """;
        var document = new TextDocument(code);

        var foldings = _strategy.CreateFoldings(document).ToList();

        Assert.That(foldings, Has.Count.EqualTo(1));
        Assert.That(foldings[0].Name, Does.StartWith("Enum Colors"));
    }

    [Test]
    public void CreateFoldings_StructureBlock_CreatesFolding()
    {
        var code = """
            Structure Point
                Dim X As Integer
                Dim Y As Integer
            End Structure
            """;
        var document = new TextDocument(code);

        var foldings = _strategy.CreateFoldings(document).ToList();

        Assert.That(foldings, Has.Count.EqualTo(1));
        Assert.That(foldings[0].Name, Does.StartWith("Structure Point"));
    }

    [Test]
    public void CreateFoldings_WithModifiers_CreatesFolding()
    {
        var code = """
            Public Shared Sub SharedMethod()
                DoWork()
            End Sub
            """;
        var document = new TextDocument(code);

        var foldings = _strategy.CreateFoldings(document).ToList();

        Assert.That(foldings, Has.Count.EqualTo(1));
        Assert.That(foldings[0].Name, Does.Contain("Sub SharedMethod"));
    }

    [Test]
    public void CreateFoldings_PrivateMethod_CreatesFolding()
    {
        var code = """
            Private Sub PrivateMethod()
                DoWork()
            End Sub
            """;
        var document = new TextDocument(code);

        var foldings = _strategy.CreateFoldings(document).ToList();

        Assert.That(foldings, Has.Count.EqualTo(1));
    }

    [Test]
    public void CreateFoldings_UnclosedBlock_MarksAsUnclosed()
    {
        var code = """
            Sub TestMethod()
                Dim x = 1
            """;
        var document = new TextDocument(code);

        var foldings = _strategy.CreateFoldings(document).ToList();

        Assert.That(foldings, Has.Count.EqualTo(1));
        Assert.That(foldings[0].Name, Does.Contain("(unclosed)"));
    }

    [Test]
    public void CreateFoldings_MultipleUnclosedBlocks_AllMarkedAsUnclosed()
    {
        var code = """
            Class TestClass
                Sub TestMethod()
                    If condition Then
            """;
        var document = new TextDocument(code);

        var foldings = _strategy.CreateFoldings(document).ToList();

        Assert.That(foldings, Has.Count.EqualTo(3));
        Assert.That(foldings.All(f => f.Name.Contains("(unclosed)")), Is.True);
    }

    [Test]
    public void CreateFoldings_FoldingsAreSortedByStartOffset()
    {
        var code = """
            Class TestClass
                Sub Method1()
                End Sub
                Sub Method2()
                End Sub
            End Class
            """;
        var document = new TextDocument(code);

        var foldings = _strategy.CreateFoldings(document).ToList();

        Assert.That(foldings, Has.Count.EqualTo(3));
        for (int i = 1; i < foldings.Count; i++)
        {
            Assert.That(foldings[i].StartOffset, Is.GreaterThanOrEqualTo(foldings[i - 1].StartOffset));
        }
    }

    [Test]
    public void CreateFoldings_LongFoldingName_IsTruncated()
    {
        var code = """
            Sub ThisIsAVeryLongMethodNameThatExceedsFiftyCharactersAndShouldBeTruncated()
                DoWork()
            End Sub
            """;
        var document = new TextDocument(code);

        var foldings = _strategy.CreateFoldings(document).ToList();

        Assert.That(foldings, Has.Count.EqualTo(1));
        Assert.That(foldings[0].Name.Length, Is.LessThanOrEqualTo(53)); // 50 + "..."
    }

    [Test]
    public void CreateFoldings_CaseInsensitive_MatchesDifferentCases()
    {
        var code = """
            SUB TestMethod()
                Dim x = 1
            END SUB
            """;
        var document = new TextDocument(code);

        var foldings = _strategy.CreateFoldings(document).ToList();

        Assert.That(foldings, Has.Count.EqualTo(1));
    }

    [Test]
    public void CreateFoldings_WithBlock_CreatesFolding()
    {
        var code = """
            With object
                .Property1 = value1
                .Property2 = value2
            End With
            """;
        var document = new TextDocument(code);

        var foldings = _strategy.CreateFoldings(document).ToList();

        Assert.That(foldings, Has.Count.EqualTo(1));
        Assert.That(foldings[0].Name, Does.StartWith("With"));
    }

    [Test]
    public void CreateFoldings_TemplateBlock_CreatesFolding()
    {
        var code = """
            Template MyTemplate(Of T)
                Dim value As T
            End Template
            """;
        var document = new TextDocument(code);

        var foldings = _strategy.CreateFoldings(document).ToList();

        Assert.That(foldings, Has.Count.EqualTo(1));
        Assert.That(foldings[0].Name, Does.StartWith("Template"));
    }

    [Test]
    public void CreateFoldings_DefaultClosed_IsFalse()
    {
        var code = """
            Sub TestMethod()
                DoWork()
            End Sub
            """;
        var document = new TextDocument(code);

        var foldings = _strategy.CreateFoldings(document).ToList();

        Assert.That(foldings, Has.Count.EqualTo(1));
        Assert.That(foldings[0].DefaultClosed, Is.False);
    }

    [Test]
    public void CreateFoldings_EndSubKeyword_MatchesCorrectly()
    {
        var code = """
            Sub Test()
                Dim x = 1
            EndSub
            """;
        var document = new TextDocument(code);

        var foldings = _strategy.CreateFoldings(document).ToList();

        Assert.That(foldings, Has.Count.EqualTo(1));
        Assert.That(foldings[0].Name, Does.Not.Contain("(unclosed)"));
    }

    [Test]
    public void CreateFoldings_OverridableMethod_CreatesFolding()
    {
        var code = """
            Public Overridable Sub VirtualMethod()
                DoWork()
            End Sub
            """;
        var document = new TextDocument(code);

        var foldings = _strategy.CreateFoldings(document).ToList();

        Assert.That(foldings, Has.Count.EqualTo(1));
    }

    [Test]
    public void CreateFoldings_OverridesMethod_CreatesFolding()
    {
        var code = """
            Public Overrides Sub OverriddenMethod()
                DoWork()
            End Sub
            """;
        var document = new TextDocument(code);

        var foldings = _strategy.CreateFoldings(document).ToList();

        Assert.That(foldings, Has.Count.EqualTo(1));
    }
}
