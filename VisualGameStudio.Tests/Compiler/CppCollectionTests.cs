using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.CPlusPlus;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// C++ backend collection tests (Task 2): the backend ACCEPTS List/Dictionary/HashSet and
/// maps them to the BasicLang::List/Dictionary/HashSet wrappers (value types, never shared_ptr),
/// and emits the wrapper runtime preamble only when the module actually uses a collection.
/// Member operations (.Add/.Count/indexer) are Task 3 and are not exercised here.
/// </summary>
[TestFixture]
public class CppCollectionTests
{
    /// <summary>
    /// Helper: compile BasicLang source to C++ output string.
    /// Returns null and populates errors list if a pipeline stage fails.
    /// CppCapabilityException from the generator propagates to the caller.
    /// </summary>
    private string CompileToCpp(string source, out List<string> errors)
    {
        errors = new List<string>();

        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();

        var parser = new Parser(tokens);
        ProgramNode ast;
        try
        {
            ast = parser.Parse();
        }
        catch (Exception ex)
        {
            errors.Add($"Parse error: {ex.Message}");
            return null;
        }

        var analyzer = new SemanticAnalyzer();
        bool success = analyzer.Analyze(ast);
        if (!success)
        {
            foreach (var err in analyzer.Errors)
                errors.Add($"Semantic error: {err.Message}");
            return null;
        }

        var irBuilder = new IRBuilder(analyzer);
        var irModule = irBuilder.Build(ast, "TestModule");

        var gen = new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false });
        return gen.Generate(irModule);
    }

    [Test]
    public void Cpp_ListLocal_MapsToBasicLangListValue()
    {
        var source = @"
Sub Main()
    Dim numbers As New List(Of Integer)()
End Sub";
        var output = CompileToCpp(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("BasicLang::List<int32_t>"));
        Assert.That(output, Does.Not.Contain("std::make_shared<List"));
        Assert.That(output, Does.Not.Contain("std::shared_ptr<BasicLang::List"));
    }

    [Test]
    public void Cpp_ListLocal_LowercaseName_StillMapsToCanonicalWrapper()
    {
        // BasicLang is case-insensitive; `list` must map exactly like `List` — never fall
        // through to std::shared_ptr<list<...>> (an undefined type) with no preamble.
        var source = @"
Sub Main()
    Dim l As New list(Of Integer)()
End Sub";
        var output = CompileToCpp(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("BasicLang::List<int32_t>"));
        Assert.That(output, Does.Contain("class List"));                     // preamble emitted
        Assert.That(output, Does.Not.Contain("std::shared_ptr<list"));
        Assert.That(output, Does.Not.Contain("std::make_shared<list"));
    }

    [Test]
    public void Cpp_DictionaryLocal_MapsToBasicLangDictionaryValue()
    {
        var source = @"
Sub Main()
    Dim map As New Dictionary(Of String, Integer)()
End Sub";
        var output = CompileToCpp(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("BasicLang::Dictionary<std::string, int32_t>"));
        Assert.That(output, Does.Not.Contain("std::make_shared<Dictionary"));
        Assert.That(output, Does.Not.Contain("std::shared_ptr<BasicLang::Dictionary"));
    }

    [Test]
    public void Cpp_HashSetLocal_MapsToBasicLangHashSetValue()
    {
        var source = @"
Sub Main()
    Dim seen As New HashSet(Of Integer)()
End Sub";
        var output = CompileToCpp(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("BasicLang::HashSet<int32_t>"));
        Assert.That(output, Does.Not.Contain("std::make_shared<HashSet"));
        Assert.That(output, Does.Not.Contain("std::shared_ptr<BasicLang::HashSet"));
    }

    [Test]
    public void Cpp_ListOfUnmappedType_StillRejected()
    {
        // Generic args are still capability-checked: DateTime has no C++ mapping.
        var source = @"
Sub Main()
    Dim d As New List(Of DateTime)()
End Sub";
        Assert.Throws<CppCapabilityException>(() => CompileToCpp(source, out _));
    }

    [Test]
    public void Cpp_UsesCollections_EmitsWrapperPreamble()
    {
        var output = CompileToCpp("Sub Main()\n Dim l As New List(Of Integer)()\nEnd Sub", out var e);
        Assert.That(e, Is.Empty, string.Join("; ", e));
        Assert.That(output, Does.Contain("class List"));            // wrapper preamble present
        Assert.That(output, Does.Contain("#include <unordered_map>"));
    }

    [Test]
    public void Cpp_NoCollections_OmitsWrapperPreamble()
    {
        var output = CompileToCpp("Sub Main()\n Dim x As Integer = 1\nEnd Sub", out var e);
        Assert.That(e, Is.Empty, string.Join("; ", e));
        Assert.That(output, Does.Not.Contain("class List"));
    }
}
