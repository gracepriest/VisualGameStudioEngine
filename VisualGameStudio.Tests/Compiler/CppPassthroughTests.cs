using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.CPlusPlus;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// C++ std passthrough tests: the <c>#CppInclude</c> directive emits a real C++
/// <c>#include</c> into generated C++ output. This is DISTINCT from the existing
/// <c>#Include</c> directive (VB-style source-file splicing in the Preprocessor).
/// </summary>
[TestFixture]
public class CppPassthroughTests
{
    /// <summary>
    /// Helper: compile BasicLang source to C++ output, running the Preprocessor FIRST
    /// so that <c>#CppInclude</c> directives (collected during preprocessing) are
    /// threaded onto the module the C++ backend generates from. This mirrors what
    /// Compiler.cs does. The plain CompileToCpp helpers in the other Cpp test files
    /// skip the preprocessor, so they never see <c>#CppInclude</c>.
    /// Returns null and populates <paramref name="errors"/> if a pipeline stage fails.
    /// </summary>
    private string? CompileToCppFull(string source, out List<string> errors)
    {
        errors = new List<string>();

        var pre = new Preprocessor();
        var processed = pre.Process(source, "test.bas");
        if (pre.Errors.Count > 0)
        {
            foreach (var e in pre.Errors) errors.Add(e.Message);
            return null;
        }

        var tokens = new Lexer(processed).Tokenize();
        var ast = new Parser(tokens).Parse();

        var analyzer = new SemanticAnalyzer();
        if (!analyzer.Analyze(ast))
        {
            foreach (var e in analyzer.Errors) errors.Add(e.Message);
            return null;
        }

        var irModule = new IRBuilder(analyzer).Build(ast, "TestModule");
        // Thread collected C++ headers onto the module (mirrors Compiler.cs wiring).
        irModule.CppIncludes.AddRange(pre.CppIncludes);

        return new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false }).Generate(irModule);
    }

    [Test]
    public void Cpp_CppInclude_EmitsRealInclude()
    {
        var source = "#CppInclude <mutex>\nSub Main()\nEnd Sub";
        var output = CompileToCppFull(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("#include <mutex>"));
    }

    [Test]
    public void Cpp_CppInclude_QuotedHeader_EmitsQuotedInclude()
    {
        var source = "#CppInclude \"grid.h\"\nSub Main()\nEnd Sub";
        var output = CompileToCppFull(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("#include \"grid.h\""));
    }

    [Test]
    public void Cpp_CppInclude_InvalidSyntax_Errors()
    {
        var source = "#CppInclude nonsense\nSub Main()\nEnd Sub";
        var output = CompileToCppFull(source, out var errors);
        Assert.That(output, Is.Null);
        Assert.That(errors, Is.Not.Empty);
        Assert.That(string.Join("; ", errors), Does.Contain("Invalid #CppInclude"));
    }

    [Test]
    public void Cpp_MultipleCppIncludes_AllEmitted()
    {
        var source = "#CppInclude <mutex>\n#CppInclude <thread>\n#CppInclude \"grid.h\"\nSub Main()\nEnd Sub";
        var output = CompileToCppFull(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("#include <mutex>"));
        Assert.That(output, Does.Contain("#include <thread>"));
        Assert.That(output, Does.Contain("#include \"grid.h\""));
    }

    // ------------------------------------------------------------------
    // Regression: #CppInclude must NOT be dispatched to the #Include
    // source-file splicer. The splicer would try to resolve a file named
    // "mutex" and emit "Cannot find include file". #CppInclude collects
    // the header instead, and leaves the splicer's state untouched.
    // ------------------------------------------------------------------

    [Test]
    public void Preprocessor_CppInclude_NotDispatchedToIncludeSplicer()
    {
        var pre = new Preprocessor();
        pre.Process("#CppInclude <mutex>\nSub Main()\nEnd Sub", "test.bas");

        Assert.That(pre.Errors, Is.Empty,
            "#CppInclude must not be routed to the #Include file splicer");
        Assert.That(pre.CppIncludes, Has.Count.EqualTo(1));
        Assert.That(pre.CppIncludes[0], Is.EqualTo("<mutex>"));
    }

    [Test]
    public void Preprocessor_Include_DoesNotPopulateCppIncludes()
    {
        // A plain #Include of a missing source file errors via the splicer
        // (Cannot find include file) and never touches CppIncludes. This
        // proves the two directives own separate collections.
        var pre = new Preprocessor();
        pre.Process("#Include \"missing_source.bas\"\nSub Main()\nEnd Sub", "test.bas");

        Assert.That(pre.CppIncludes, Is.Empty,
            "#Include (source splicer) must never populate CppIncludes");
        Assert.That(string.Join("; ", pre.Errors.ConvertAll(e => e.Message)),
            Does.Contain("Cannot find include file"),
            "#Include of a missing file should error via the splicer, unchanged");
    }
}
