using NUnit.Framework;
using BasicLang.Compiler.LSP;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace VisualGameStudio.Tests.LSP;

[TestFixture]
public class SymbolServiceTests
{
    private SymbolService _symbolService = null!;

    [SetUp]
    public void SetUp()
    {
        _symbolService = new SymbolService();
    }

    #region GetHoverInfo - Built-in Keywords

    [Test]
    public void GetHoverInfo_SubKeyword_ReturnsDocumentation()
    {
        var result = _symbolService.GetHoverInfo(null!, "Sub");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("Sub"));
        Assert.That(result, Does.Contain("subroutine"));
    }

    [Test]
    public void GetHoverInfo_FunctionKeyword_ReturnsDocumentation()
    {
        var result = _symbolService.GetHoverInfo(null!, "Function");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("Function"));
        Assert.That(result, Does.Contain("returns"));
    }

    [Test]
    public void GetHoverInfo_IfKeyword_ReturnsDocumentation()
    {
        var result = _symbolService.GetHoverInfo(null!, "If");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("If"));
        Assert.That(result, Does.Contain("Conditional"));
    }

    [Test]
    public void GetHoverInfo_ForKeyword_ReturnsDocumentation()
    {
        var result = _symbolService.GetHoverInfo(null!, "For");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("For"));
        Assert.That(result, Does.Contain("loop"));
    }

    [Test]
    public void GetHoverInfo_WhileKeyword_ReturnsDocumentation()
    {
        var result = _symbolService.GetHoverInfo(null!, "While");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("While"));
        Assert.That(result, Does.Contain("Loop"));
    }

    [Test]
    public void GetHoverInfo_ClassKeyword_ReturnsDocumentation()
    {
        var result = _symbolService.GetHoverInfo(null!, "Class");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("Class"));
    }

    [Test]
    public void GetHoverInfo_DimKeyword_ReturnsDocumentation()
    {
        var result = _symbolService.GetHoverInfo(null!, "Dim");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("Dim"));
        Assert.That(result, Does.Contain("variable"));
    }

    [Test]
    public void GetHoverInfo_TryKeyword_ReturnsDocumentation()
    {
        var result = _symbolService.GetHoverInfo(null!, "Try");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("Try"));
        Assert.That(result, Does.Contain("Exception"));
    }

    #endregion

    #region GetHoverInfo - Built-in Functions

    [Test]
    public void GetHoverInfo_PrintLine_ReturnsDocumentation()
    {
        var result = _symbolService.GetHoverInfo(null!, "PrintLine");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("PrintLine"));
        Assert.That(result, Does.Contain("console"));
    }

    [Test]
    public void GetHoverInfo_ReadLine_ReturnsDocumentation()
    {
        var result = _symbolService.GetHoverInfo(null!, "ReadLine");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("ReadLine"));
        Assert.That(result, Does.Contain("String"));
    }

    [Test]
    public void GetHoverInfo_Len_ReturnsDocumentation()
    {
        var result = _symbolService.GetHoverInfo(null!, "Len");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("Len"));
        Assert.That(result, Does.Contain("length"));
    }

    [Test]
    public void GetHoverInfo_Left_ReturnsDocumentation()
    {
        var result = _symbolService.GetHoverInfo(null!, "Left");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("Left"));
        Assert.That(result, Does.Contain("leftmost"));
    }

    [Test]
    public void GetHoverInfo_Mid_ReturnsDocumentation()
    {
        var result = _symbolService.GetHoverInfo(null!, "Mid");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("Mid"));
        Assert.That(result, Does.Contain("substring"));
    }

    [Test]
    public void GetHoverInfo_Abs_ReturnsDocumentation()
    {
        var result = _symbolService.GetHoverInfo(null!, "Abs");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("Abs"));
        Assert.That(result, Does.Contain("absolute"));
    }

    [Test]
    public void GetHoverInfo_Sqrt_ReturnsDocumentation()
    {
        var result = _symbolService.GetHoverInfo(null!, "Sqrt");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("Sqrt"));
        Assert.That(result, Does.Contain("square root"));
    }

    [Test]
    public void GetHoverInfo_CInt_ReturnsDocumentation()
    {
        var result = _symbolService.GetHoverInfo(null!, "CInt");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("CInt"));
        Assert.That(result, Does.Contain("Integer"));
    }

    [Test]
    public void GetHoverInfo_CreateList_ReturnsDocumentation()
    {
        var result = _symbolService.GetHoverInfo(null!, "CreateList");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("CreateList"));
        Assert.That(result, Does.Contain("list"));
    }

    [Test]
    public void GetHoverInfo_Where_ReturnsDocumentation()
    {
        var result = _symbolService.GetHoverInfo(null!, "Where");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("Where"));
        Assert.That(result, Does.Contain("Filter"));
    }

    #endregion

    #region GetHoverInfo - Types

    [Test]
    public void GetHoverInfo_IntegerType_ReturnsDocumentation()
    {
        var result = _symbolService.GetHoverInfo(null!, "Integer");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("Integer"));
        Assert.That(result, Does.Contain("32-bit"));
    }

    [Test]
    public void GetHoverInfo_StringType_ReturnsDocumentation()
    {
        var result = _symbolService.GetHoverInfo(null!, "String");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("String"));
        Assert.That(result, Does.Contain("Unicode"));
    }

    [Test]
    public void GetHoverInfo_BooleanType_ReturnsDocumentation()
    {
        var result = _symbolService.GetHoverInfo(null!, "Boolean");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("Boolean"));
        Assert.That(result, Does.Contain("True"));
    }

    #endregion

    #region GetHoverInfo - Case Insensitivity

    [Test]
    public void GetHoverInfo_IsCaseInsensitive()
    {
        var result1 = _symbolService.GetHoverInfo(null!, "sub");
        var result2 = _symbolService.GetHoverInfo(null!, "SUB");
        var result3 = _symbolService.GetHoverInfo(null!, "Sub");

        Assert.That(result1, Is.Not.Null);
        Assert.That(result2, Is.Not.Null);
        Assert.That(result3, Is.Not.Null);
        Assert.That(result1, Is.EqualTo(result2));
        Assert.That(result2, Is.EqualTo(result3));
    }

    #endregion

    #region GetHoverInfo - Unknown Symbols

    [Test]
    public void GetHoverInfo_UnknownSymbol_ReturnsNull()
    {
        var result = _symbolService.GetHoverInfo(null!, "UnknownSymbol12345");

        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetHoverInfo_EmptyString_ReturnsNull()
    {
        var result = _symbolService.GetHoverInfo(null!, "");

        Assert.That(result, Is.Null);
    }

    #endregion

    #region GetDocumentSymbols

    [Test]
    public void GetDocumentSymbols_NullState_ReturnsEmptyList()
    {
        var result = _symbolService.GetDocumentSymbols(null!);

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetDocumentSymbols_StateWithNullAST_ReturnsEmptyList()
    {
        var state = CreateDocumentState("");
        // AST is null for empty document

        var result = _symbolService.GetDocumentSymbols(state);

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }

    #endregion

    #region FindDefinition

    [Test]
    public void FindDefinition_NullState_ReturnsNull()
    {
        var result = _symbolService.FindDefinition(null!, "SomeSymbol");

        Assert.That(result, Is.Null);
    }

    [Test]
    public void FindDefinition_StateWithNullAST_ReturnsNull()
    {
        var state = CreateDocumentState("");

        var result = _symbolService.FindDefinition(state, "SomeSymbol");

        Assert.That(result, Is.Null);
    }

    #endregion

    #region Built-in Operators

    [Test]
    public void GetHoverInfo_AndOperator_ReturnsDocumentation()
    {
        var result = _symbolService.GetHoverInfo(null!, "And");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("And"));
        Assert.That(result, Does.Contain("Logical"));
    }

    [Test]
    public void GetHoverInfo_OrOperator_ReturnsDocumentation()
    {
        var result = _symbolService.GetHoverInfo(null!, "Or");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("Or"));
        Assert.That(result, Does.Contain("Logical"));
    }

    [Test]
    public void GetHoverInfo_NotOperator_ReturnsDocumentation()
    {
        var result = _symbolService.GetHoverInfo(null!, "Not");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("Not"));
        Assert.That(result, Does.Contain("Logical"));
    }

    [Test]
    public void GetHoverInfo_ModOperator_ReturnsDocumentation()
    {
        var result = _symbolService.GetHoverInfo(null!, "Mod");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("Mod"));
        Assert.That(result, Does.Contain("remainder"));
    }

    #endregion

    #region Date/Time Functions

    [Test]
    public void GetHoverInfo_Now_ReturnsDocumentation()
    {
        var result = _symbolService.GetHoverInfo(null!, "Now");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("Now"));
        Assert.That(result, Does.Contain("date"));
    }

    [Test]
    public void GetHoverInfo_Today_ReturnsDocumentation()
    {
        var result = _symbolService.GetHoverInfo(null!, "Today");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("Today"));
        Assert.That(result, Does.Contain("date"));
    }

    #endregion

    #region File I/O Functions

    [Test]
    public void GetHoverInfo_FileExists_ReturnsDocumentation()
    {
        var result = _symbolService.GetHoverInfo(null!, "FileExists");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("FileExists"));
        Assert.That(result, Does.Contain("file"));
    }

    [Test]
    public void GetHoverInfo_ReadAllText_ReturnsDocumentation()
    {
        var result = _symbolService.GetHoverInfo(null!, "ReadAllText");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("ReadAllText"));
        Assert.That(result, Does.Contain("file"));
    }

    #endregion

    private DocumentState CreateDocumentState(string sourceCode)
    {
        var uri = DocumentUri.From("file:///test.bas");
        return new DocumentState(uri, sourceCode);
    }
}

[TestFixture]
public class SymbolServiceBuiltInDocsTests
{
    private SymbolService _symbolService = null!;

    [SetUp]
    public void SetUp()
    {
        _symbolService = new SymbolService();
    }

    [TestCase("Sub")]
    [TestCase("Function")]
    [TestCase("If")]
    [TestCase("For")]
    [TestCase("While")]
    [TestCase("Do")]
    [TestCase("Class")]
    [TestCase("Module")]
    [TestCase("Interface")]
    [TestCase("Dim")]
    [TestCase("Const")]
    [TestCase("Property")]
    [TestCase("Public")]
    [TestCase("Private")]
    [TestCase("Try")]
    [TestCase("Return")]
    [TestCase("Exit")]
    [TestCase("New")]
    [TestCase("Nothing")]
    [TestCase("True")]
    [TestCase("False")]
    public void GetHoverInfo_Keyword_ReturnsNonNull(string keyword)
    {
        var result = _symbolService.GetHoverInfo(null!, keyword);
        Assert.That(result, Is.Not.Null, $"Keyword '{keyword}' should have documentation");
    }

    [TestCase("PrintLine")]
    [TestCase("Print")]
    [TestCase("ReadLine")]
    [TestCase("ReadKey")]
    [TestCase("Len")]
    [TestCase("Left")]
    [TestCase("Right")]
    [TestCase("Mid")]
    [TestCase("UCase")]
    [TestCase("LCase")]
    [TestCase("Trim")]
    [TestCase("InStr")]
    [TestCase("Replace")]
    [TestCase("Split")]
    [TestCase("Join")]
    public void GetHoverInfo_StringFunction_ReturnsNonNull(string func)
    {
        var result = _symbolService.GetHoverInfo(null!, func);
        Assert.That(result, Is.Not.Null, $"Function '{func}' should have documentation");
    }

    [TestCase("Abs")]
    [TestCase("Sqrt")]
    [TestCase("Pow")]
    [TestCase("Sin")]
    [TestCase("Cos")]
    [TestCase("Tan")]
    [TestCase("Log")]
    [TestCase("Floor")]
    [TestCase("Ceiling")]
    [TestCase("Round")]
    [TestCase("Min")]
    [TestCase("Max")]
    [TestCase("Rnd")]
    public void GetHoverInfo_MathFunction_ReturnsNonNull(string func)
    {
        var result = _symbolService.GetHoverInfo(null!, func);
        Assert.That(result, Is.Not.Null, $"Function '{func}' should have documentation");
    }

    [TestCase("CInt")]
    [TestCase("CLng")]
    [TestCase("CDbl")]
    [TestCase("CStr")]
    [TestCase("CBool")]
    [TestCase("CChar")]
    [TestCase("CByte")]
    public void GetHoverInfo_ConversionFunction_ReturnsNonNull(string func)
    {
        var result = _symbolService.GetHoverInfo(null!, func);
        Assert.That(result, Is.Not.Null, $"Function '{func}' should have documentation");
    }

    [TestCase("CreateList")]
    [TestCase("ListAdd")]
    [TestCase("ListGet")]
    [TestCase("ListCount")]
    [TestCase("CreateDictionary")]
    [TestCase("DictSet")]
    [TestCase("DictGet")]
    [TestCase("CreateHashSet")]
    [TestCase("SetAdd")]
    [TestCase("SetContains")]
    public void GetHoverInfo_CollectionFunction_ReturnsNonNull(string func)
    {
        var result = _symbolService.GetHoverInfo(null!, func);
        Assert.That(result, Is.Not.Null, $"Function '{func}' should have documentation");
    }

    [TestCase("Where")]
    [TestCase("Select")]
    [TestCase("OrderBy")]
    [TestCase("First")]
    [TestCase("FirstOrDefault")]
    [TestCase("Take")]
    [TestCase("Skip")]
    [TestCase("Any")]
    [TestCase("All")]
    [TestCase("Count")]
    [TestCase("Sum")]
    [TestCase("ToList")]
    [TestCase("ToArray")]
    public void GetHoverInfo_LinqFunction_ReturnsNonNull(string func)
    {
        var result = _symbolService.GetHoverInfo(null!, func);
        Assert.That(result, Is.Not.Null, $"Function '{func}' should have documentation");
    }

    [TestCase("Integer")]
    [TestCase("Long")]
    [TestCase("Double")]
    [TestCase("String")]
    [TestCase("Boolean")]
    [TestCase("Char")]
    [TestCase("Byte")]
    [TestCase("Object")]
    [TestCase("Variant")]
    public void GetHoverInfo_Type_ReturnsNonNull(string typeName)
    {
        var result = _symbolService.GetHoverInfo(null!, typeName);
        Assert.That(result, Is.Not.Null, $"Type '{typeName}' should have documentation");
    }
}
