using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

[TestFixture]
public class SymbolSearchServiceTests
{
    private SymbolSearchService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _service = new SymbolSearchService();
    }

    #region GetFileSymbols Tests

    [Test]
    public void GetFileSymbols_EmptySource_ReturnsEmpty()
    {
        var result = _service.GetFileSymbols("");
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetFileSymbols_NullSource_ReturnsEmpty()
    {
        var result = _service.GetFileSymbols(null!);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetFileSymbols_FindsClass()
    {
        var source = @"Class MyClass
End Class";

        var result = _service.GetFileSymbols(source);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Name, Is.EqualTo("MyClass"));
        Assert.That(result[0].Kind, Is.EqualTo(SymbolKind.Class));
    }

    [Test]
    public void GetFileSymbols_FindsModule()
    {
        var source = @"Module MyModule
End Module";

        var result = _service.GetFileSymbols(source);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Name, Is.EqualTo("MyModule"));
        Assert.That(result[0].Kind, Is.EqualTo(SymbolKind.Module));
    }

    [Test]
    public void GetFileSymbols_FindsSub()
    {
        var source = @"Sub MyMethod()
End Sub";

        var result = _service.GetFileSymbols(source);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Name, Is.EqualTo("MyMethod"));
        Assert.That(result[0].Kind, Is.EqualTo(SymbolKind.Method));
    }

    [Test]
    public void GetFileSymbols_FindsFunction()
    {
        var source = @"Function Calculate() As Integer
End Function";

        var result = _service.GetFileSymbols(source);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Name, Is.EqualTo("Calculate"));
        Assert.That(result[0].Kind, Is.EqualTo(SymbolKind.Function));
        Assert.That(result[0].ReturnType, Is.EqualTo("Integer"));
    }

    [Test]
    public void GetFileSymbols_FindsProperty()
    {
        var source = @"Property Name As String
End Property";

        var result = _service.GetFileSymbols(source);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Name, Is.EqualTo("Name"));
        Assert.That(result[0].Kind, Is.EqualTo(SymbolKind.Property));
    }

    [Test]
    public void GetFileSymbols_FindsField()
    {
        var source = "Dim myField As Integer";

        var result = _service.GetFileSymbols(source);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Name, Is.EqualTo("myField"));
        Assert.That(result[0].Kind, Is.EqualTo(SymbolKind.Field));
    }

    [Test]
    public void GetFileSymbols_FindsConstant()
    {
        var source = "Const MAX_VALUE As Integer = 100";

        var result = _service.GetFileSymbols(source);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Name, Is.EqualTo("MAX_VALUE"));
        Assert.That(result[0].Kind, Is.EqualTo(SymbolKind.Constant));
    }

    [Test]
    public void GetFileSymbols_FindsEnum()
    {
        var source = @"Enum Colors
End Enum";

        var result = _service.GetFileSymbols(source);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Name, Is.EqualTo("Colors"));
        Assert.That(result[0].Kind, Is.EqualTo(SymbolKind.Enum));
    }

    [Test]
    public void GetFileSymbols_FindsEvent()
    {
        var source = "Event OnClick";

        var result = _service.GetFileSymbols(source);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Name, Is.EqualTo("OnClick"));
        Assert.That(result[0].Kind, Is.EqualTo(SymbolKind.Event));
    }

    [Test]
    public void GetFileSymbols_BuildsHierarchy()
    {
        var source = @"Class MyClass
    Sub MyMethod()
    End Sub

    Function Calculate() As Integer
    End Function
End Class";

        var result = _service.GetFileSymbols(source);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Name, Is.EqualTo("MyClass"));
        Assert.That(result[0].Children, Has.Count.EqualTo(2));
        Assert.That(result[0].Children[0].Name, Is.EqualTo("MyMethod"));
        Assert.That(result[0].Children[1].Name, Is.EqualTo("Calculate"));
    }

    [Test]
    public void GetFileSymbols_SetsContainerName()
    {
        var source = @"Class Parent
    Sub Child()
    End Sub
End Class";

        var result = _service.GetFileSymbols(source);

        Assert.That(result[0].Children[0].ContainerName, Is.EqualTo("Parent"));
        Assert.That(result[0].Children[0].FullName, Is.EqualTo("Parent.Child"));
    }

    [Test]
    public void GetFileSymbols_RecognizesAccessModifiers()
    {
        var source = @"Public Class PublicClass
End Class

Private Sub PrivateSub()
End Sub";

        var result = _service.GetFileSymbols(source);

        Assert.That(result[0].AccessModifier, Is.EqualTo(AccessModifier.Public));
        Assert.That(result[1].AccessModifier, Is.EqualTo(AccessModifier.Private));
    }

    [Test]
    public void GetFileSymbols_TracksLineNumbers()
    {
        var source = @"Class MyClass
    Sub MyMethod()
    End Sub
End Class";

        var result = _service.GetFileSymbols(source);

        Assert.That(result[0].StartLine, Is.EqualTo(1));
        Assert.That(result[0].EndLine, Is.EqualTo(4));
        Assert.That(result[0].Children[0].StartLine, Is.EqualTo(2));
        Assert.That(result[0].Children[0].EndLine, Is.EqualTo(3));
    }

    #endregion

    #region SearchInFileAsync Tests

    [Test]
    public async Task SearchInFileAsync_EmptyQuery_ReturnsEmpty()
    {
        var source = "Class MyClass\nEnd Class";

        var result = await _service.SearchInFileAsync(source, "");

        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task SearchInFileAsync_ExactMatch_ReturnsHighScore()
    {
        var source = @"Class MyClass
End Class

Class OtherClass
End Class";

        var result = await _service.SearchInFileAsync(source, "MyClass");

        Assert.That(result, Has.Count.GreaterThanOrEqualTo(1));
        Assert.That(result[0].Symbol.Name, Is.EqualTo("MyClass"));
        Assert.That(result[0].Score, Is.EqualTo(100));
    }

    [Test]
    public async Task SearchInFileAsync_PartialMatch_ReturnsResults()
    {
        var source = @"Class MyClass
End Class

Class MyOtherClass
End Class";

        var result = await _service.SearchInFileAsync(source, "My");

        Assert.That(result, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task SearchInFileAsync_CaseInsensitive()
    {
        var source = @"Class MyClass
End Class";

        var result = await _service.SearchInFileAsync(source, "myclass");

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Symbol.Name, Is.EqualTo("MyClass"));
    }

    [Test]
    public async Task SearchInFileAsync_SearchesNestedSymbols()
    {
        var source = @"Class Parent
    Sub ChildMethod()
    End Sub
End Class";

        var result = await _service.SearchInFileAsync(source, "Child");

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Symbol.Name, Is.EqualTo("ChildMethod"));
    }

    [Test]
    public async Task SearchInFileAsync_OrdersByScore()
    {
        var source = @"Sub Calculate()
End Sub

Sub MyCalculation()
End Sub

Sub CalculateSum()
End Sub";

        var result = await _service.SearchInFileAsync(source, "Calculate");

        Assert.That(result[0].Symbol.Name, Is.EqualTo("Calculate")); // Exact match first
    }

    #endregion

    #region GetSymbolAtLocation Tests

    [Test]
    public void GetSymbolAtLocation_FindsSymbol()
    {
        var source = @"Class MyClass
    Sub MyMethod()
    End Sub
End Class";

        var result = _service.GetSymbolAtLocation(source, 2, 10);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.EqualTo("MyMethod"));
    }

    [Test]
    public void GetSymbolAtLocation_ReturnsNull_WhenNotOnSymbol()
    {
        var source = @"Class MyClass
End Class

' Just a comment";

        var result = _service.GetSymbolAtLocation(source, 4, 1);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetSymbolAtLocation_FindsMostSpecificSymbol()
    {
        var source = @"Class MyClass
    Sub MyMethod()
        ' Inside method
    End Sub
End Class";

        var result = _service.GetSymbolAtLocation(source, 3, 10);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.EqualTo("MyMethod")); // Method, not class
    }

    #endregion

    #region GetBreadcrumb Tests

    [Test]
    public void GetBreadcrumb_ReturnsPath()
    {
        var source = @"Class MyClass
    Sub MyMethod()
        ' Inside
    End Sub
End Class";

        var result = _service.GetBreadcrumb(source, 3);

        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result[0].Name, Is.EqualTo("MyClass"));
        Assert.That(result[1].Name, Is.EqualTo("MyMethod"));
    }

    [Test]
    public void GetBreadcrumb_EmptyForLineOutsideSymbols()
    {
        var source = @"' Comment
Class MyClass
End Class
' Another comment";

        var result = _service.GetBreadcrumb(source, 4);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetBreadcrumb_SingleLevel()
    {
        var source = @"Sub TopLevel()
End Sub";

        var result = _service.GetBreadcrumb(source, 1);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Name, Is.EqualTo("TopLevel"));
    }

    #endregion
}

#region Model Tests

[TestFixture]
public class SymbolSearchResultTests
{
    [Test]
    public void DefaultResult_HasExpectedDefaults()
    {
        var result = new SymbolSearchResult();

        Assert.That(result.Symbol, Is.Not.Null);
        Assert.That(result.FilePath, Is.EqualTo(""));
        Assert.That(result.Score, Is.EqualTo(0));
        Assert.That(result.MatchedText, Is.EqualTo(""));
    }

    [Test]
    public void Result_CanSetAllProperties()
    {
        var symbol = new SymbolInfo { Name = "Test" };
        var result = new SymbolSearchResult
        {
            Symbol = symbol,
            FilePath = "/path/to/file.bl",
            Score = 100,
            MatchedText = "Test"
        };

        Assert.That(result.Symbol.Name, Is.EqualTo("Test"));
        Assert.That(result.FilePath, Is.EqualTo("/path/to/file.bl"));
        Assert.That(result.Score, Is.EqualTo(100));
        Assert.That(result.MatchedText, Is.EqualTo("Test"));
    }
}

[TestFixture]
public class SymbolInfoTests
{
    [Test]
    public void DefaultSymbol_HasExpectedDefaults()
    {
        var symbol = new SymbolInfo();

        Assert.That(symbol.Name, Is.EqualTo(""));
        Assert.That(symbol.FullName, Is.EqualTo(""));
        Assert.That(symbol.Kind, Is.EqualTo(default(SymbolKind)));
        Assert.That(symbol.StartLine, Is.EqualTo(0));
        Assert.That(symbol.StartColumn, Is.EqualTo(0));
        Assert.That(symbol.EndLine, Is.EqualTo(0));
        Assert.That(symbol.EndColumn, Is.EqualTo(0));
        Assert.That(symbol.ContainerName, Is.Null);
        Assert.That(symbol.AccessModifier, Is.EqualTo(AccessModifier.None));
        Assert.That(symbol.Signature, Is.Null);
        Assert.That(symbol.ReturnType, Is.Null);
        Assert.That(symbol.Children, Is.Not.Null);
        Assert.That(symbol.Children, Is.Empty);
    }

    [Test]
    public void Symbol_CanSetAllProperties()
    {
        var symbol = new SymbolInfo
        {
            Name = "MyMethod",
            FullName = "MyClass.MyMethod",
            Kind = SymbolKind.Function,
            StartLine = 10,
            StartColumn = 5,
            EndLine = 20,
            EndColumn = 10,
            ContainerName = "MyClass",
            AccessModifier = AccessModifier.Public,
            Signature = "MyMethod(x As Integer)",
            ReturnType = "String"
        };

        symbol.Children.Add(new SymbolInfo { Name = "Child" });

        Assert.That(symbol.Name, Is.EqualTo("MyMethod"));
        Assert.That(symbol.FullName, Is.EqualTo("MyClass.MyMethod"));
        Assert.That(symbol.Kind, Is.EqualTo(SymbolKind.Function));
        Assert.That(symbol.StartLine, Is.EqualTo(10));
        Assert.That(symbol.StartColumn, Is.EqualTo(5));
        Assert.That(symbol.EndLine, Is.EqualTo(20));
        Assert.That(symbol.EndColumn, Is.EqualTo(10));
        Assert.That(symbol.ContainerName, Is.EqualTo("MyClass"));
        Assert.That(symbol.AccessModifier, Is.EqualTo(AccessModifier.Public));
        Assert.That(symbol.Signature, Is.EqualTo("MyMethod(x As Integer)"));
        Assert.That(symbol.ReturnType, Is.EqualTo("String"));
        Assert.That(symbol.Children, Has.Count.EqualTo(1));
    }
}

[TestFixture]
public class SymbolKindEnumTests
{
    [Test]
    public void CommonKindValues_AreDefined()
    {
        // Using the existing SymbolKind enum from ILanguageService
        Assert.That(Enum.IsDefined(typeof(SymbolKind), SymbolKind.Class), Is.True);
        Assert.That(Enum.IsDefined(typeof(SymbolKind), SymbolKind.Module), Is.True);
        Assert.That(Enum.IsDefined(typeof(SymbolKind), SymbolKind.Struct), Is.True);
        Assert.That(Enum.IsDefined(typeof(SymbolKind), SymbolKind.Enum), Is.True);
        Assert.That(Enum.IsDefined(typeof(SymbolKind), SymbolKind.Interface), Is.True);
        Assert.That(Enum.IsDefined(typeof(SymbolKind), SymbolKind.Method), Is.True);
        Assert.That(Enum.IsDefined(typeof(SymbolKind), SymbolKind.Function), Is.True);
        Assert.That(Enum.IsDefined(typeof(SymbolKind), SymbolKind.Property), Is.True);
        Assert.That(Enum.IsDefined(typeof(SymbolKind), SymbolKind.Event), Is.True);
        Assert.That(Enum.IsDefined(typeof(SymbolKind), SymbolKind.Field), Is.True);
        Assert.That(Enum.IsDefined(typeof(SymbolKind), SymbolKind.Constant), Is.True);
        Assert.That(Enum.IsDefined(typeof(SymbolKind), SymbolKind.Variable), Is.True);
        Assert.That(Enum.IsDefined(typeof(SymbolKind), SymbolKind.EnumMember), Is.True);
        Assert.That(Enum.IsDefined(typeof(SymbolKind), SymbolKind.Namespace), Is.True);
    }
}

[TestFixture]
public class AccessModifierEnumTests
{
    [Test]
    public void AllModifierValues_AreDefined()
    {
        Assert.That(Enum.IsDefined(typeof(AccessModifier), AccessModifier.None), Is.True);
        Assert.That(Enum.IsDefined(typeof(AccessModifier), AccessModifier.Public), Is.True);
        Assert.That(Enum.IsDefined(typeof(AccessModifier), AccessModifier.Private), Is.True);
        Assert.That(Enum.IsDefined(typeof(AccessModifier), AccessModifier.Protected), Is.True);
        Assert.That(Enum.IsDefined(typeof(AccessModifier), AccessModifier.Friend), Is.True);
        Assert.That(Enum.IsDefined(typeof(AccessModifier), AccessModifier.ProtectedFriend), Is.True);
    }
}

#endregion
