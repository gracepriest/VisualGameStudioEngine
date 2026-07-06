using System.IO;
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
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "BasicLang_CppPassthrough_" + Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    /// <summary>
    /// END-TO-END helper: drive the REAL BasicCompiler through CompileFile (which runs
    /// the Preprocessor AND the CombineIRModules -> CombinedIR.CppIncludes.AddRange wiring
    /// in Compiler.cs), then feed the resulting <c>CombinedIR</c> to the C++ backend.
    /// This exercises the actual compiler join that the manual CompileToCppFull helper
    /// bypasses. Returns generated C++ (or null with populated errors on failure).
    /// </summary>
    private string? CompileFileToCpp(string source, out List<string> errors)
    {
        errors = new List<string>();

        var path = Path.Combine(_tempDir, "Program.bas");
        File.WriteAllText(path, source);

        // Fresh compiler per compile - the module registry is stateful.
        var result = new BasicCompiler().CompileFile(path);
        if (result.AllErrors.Count > 0 || result.CombinedIR == null)
        {
            foreach (var e in result.AllErrors) errors.Add(e.Message);
            if (result.CombinedIR == null) errors.Add("CombinedIR was null");
            return null;
        }

        // Generate from the SAME module instance the real compiler populated.
        return new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false }).Generate(result.CombinedIR);
    }

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

    // ------------------------------------------------------------------
    // END-TO-END: drive the REAL compiler (BasicCompiler.CompileFile), which
    // runs the Preprocessor and the Compiler.cs CombineIRModules ->
    // CombinedIR.CppIncludes.AddRange wiring. Proves the full join that the
    // manual CompileToCppFull helper bypasses.
    // ------------------------------------------------------------------

    [Test]
    public void Cpp_CppInclude_EndToEndThroughRealCompiler_EmitsInclude()
    {
        var source = "#CppInclude <mutex>\nSub Main()\nEnd Sub";
        var output = CompileFileToCpp(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("#include <mutex>"),
            "#CppInclude must survive the real compiler's CombineIRModules -> CombinedIR.CppIncludes join");
    }

    // ------------------------------------------------------------------
    // Conditional gating: #CppInclude inside an INACTIVE #IfDef/#IfNDef block
    // must NOT be collected; inside an ACTIVE block it must be. Only the
    // header COLLECTION is gated - the directive line is still commented out.
    // ------------------------------------------------------------------

    [Test]
    public void Preprocessor_CppInclude_InInactiveConditional_NotCollected()
    {
        // WINDOWS is defined, so the #IfNDef WINDOWS block is INACTIVE.
        var pre = new Preprocessor();
        pre.Define("WINDOWS");
        pre.Process("#IfNDef WINDOWS\n#CppInclude <unistd.h>\n#EndIf\nSub Main()\nEnd Sub", "test.bas");

        Assert.That(pre.Errors, Is.Empty, string.Join("; ", pre.Errors.ConvertAll(e => e.Message)));
        Assert.That(pre.CppIncludes, Is.Empty,
            "#CppInclude inside an inactive conditional block must not be collected");
    }

    [Test]
    public void Preprocessor_CppInclude_InActiveConditional_Collected()
    {
        // WINDOWS is NOT defined, so the #IfNDef WINDOWS block is ACTIVE.
        var pre = new Preprocessor();
        pre.Process("#IfNDef WINDOWS\n#CppInclude <unistd.h>\n#EndIf\nSub Main()\nEnd Sub", "test.bas");

        Assert.That(pre.Errors, Is.Empty, string.Join("; ", pre.Errors.ConvertAll(e => e.Message)));
        Assert.That(pre.CppIncludes, Has.Count.EqualTo(1));
        Assert.That(pre.CppIncludes[0], Is.EqualTo("<unistd.h>"),
            "#CppInclude inside an active conditional block must be collected");
    }

    // ------------------------------------------------------------------
    // Dedup: duplicate #CppInclude lines emit exactly one #include line.
    // ------------------------------------------------------------------

    [Test]
    public void Cpp_DuplicateCppIncludes_EmittedOnce()
    {
        var source = "#CppInclude <mutex>\n#CppInclude <mutex>\nSub Main()\nEnd Sub";
        var output = CompileToCppFull(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));

        var occurrences = output!.Split(new[] { "#include <mutex>" }, System.StringSplitOptions.None).Length - 1;
        Assert.That(occurrences, Is.EqualTo(1),
            "duplicate #CppInclude <mutex> lines must produce exactly one #include <mutex>");
    }

    // ==================================================================
    // Task 5 — ::-qualified opaque foreign C++ types.
    //
    // SPIKE FINDING (recorded per task instructions):
    //   The lexer tokenizes ':' as a single TokenType.Colon; there is NO
    //   multi-char '::' handling (BasicLangLexer.cs ScanToken). The Parser's
    //   ParseTypeReference only stitches identifiers across TokenType.Dot,
    //   so a bare "std::mutex" would parse the type name as just "std" and
    //   leave "::mutex" dangling as Colon/Colon tokens (statement separators).
    //   => a lexer + parser change WAS required. See ScopeResolution token in
    //   BasicLangLexer.cs (emitted only for '::', so single ':' statement
    //   separators / For-loop colons are untouched) and the "::" stitch loop
    //   in Parser.ParseTypeReference. Confirmed by Cpp_Spike_ScopeResolution_*.
    // ==================================================================

    [Test]
    public void Cpp_Spike_ScopeResolution_ParsesAsSingleTypeName()
    {
        // Probe: does "Dim m As std::mutex" carry "std::mutex" as one type name?
        var source = "#CppInclude <mutex>\nSub Main()\nDim m As std::mutex\nEnd Sub";
        var output = CompileToCppFull(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("std::mutex m"),
            "the '::'-qualified name must survive lexing+parsing as a single type name");
    }

    [Test]
    public void Cpp_ForeignType_OpaquePassthrough_CompilesAndRuns()
    {
        var source = @"
#CppInclude <mutex>
Sub Main()
    Dim m As std::mutex
    m.lock()
    m.unlock()
    Console.WriteLine(""ok"")
End Sub";
        var output = CompileToCppFull(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("std::mutex m"));    // value decl, not shared_ptr
        Assert.That(output, Does.Contain("m.lock()"));         // opaque passthrough with '.'
        var compiler = VisualGameStudio.Tests.Native.CppCompile.FindRunCompiler();
        if (compiler == null) Assert.Ignore("No C++ compiler");
        Assert.That(VisualGameStudio.Tests.Native.CppCompile.CompileAndRun(output, compiler.Value).Replace("\r\n", "\n"), Is.EqualTo("ok\n"));
    }

    [Test]
    public void Cpp_ForeignTemplateType_MapsAngleBrackets()
    {
        var source = @"
#CppInclude <deque>
Sub Main()
    Dim q As std::deque(Of Integer)
End Sub";
        var output = CompileToCppFull(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("std::deque<int32_t>"));
    }

    [Test]
    public void Cpp_ForeignType_NoCapabilityError_OnCppBackend()
    {
        // A ::-qualified foreign type must NOT trip the C++ capability checker
        // (which permanently rejects unmapped .NET types like List/Dictionary).
        var source = @"
#CppInclude <mutex>
Sub Main()
    Dim m As std::mutex
End Sub";
        var output = CompileToCppFull(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
    }

    [Test]
    public void Cpp_ForeignType_ChainedMemberAccess_StaysOpaque()
    {
        // a.b.c on a foreign receiver must remain opaque all the way down
        // (no "type does not have a member" error at any link).
        var source = @"
#CppInclude <mutex>
Sub Main()
    Dim m As std::mutex
    m.foo().bar()
End Sub";
        var output = CompileToCppFull(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("m.foo().bar()"));
    }
}
