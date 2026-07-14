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

    // ==================================================================
    // Bug 1 — keyword-colliding C++ name segments must not drop the statement.
    // A '::'/'.' segment whose spelling collides with a BasicLang keyword
    // (iterator, First, Where, Take, ...) previously threw a ParseException in
    // the stitch loop (Consume(Identifier)); ParseBlock caught it and
    // Synchronize()'d away the whole statement. Foreign C++ segments must
    // accept ANY word-like token verbatim.
    // ==================================================================

    [Test]
    public void Cpp_ForeignType_KeywordSegment_Iterator_ParsesVerbatim()
    {
        var source = @"
#CppInclude <string>
Sub Main()
    Dim it As std::string::iterator
End Sub";
        var output = CompileToCppFull(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("std::string::iterator it"),
            "a C++ segment named like a BasicLang keyword ('iterator') must be emitted verbatim");
    }

    [Test]
    public void Cpp_ForeignType_SoftKeywordSegment_First_Parses()
    {
        // 'First' is a LINQ soft-keyword; as a foreign C++ segment it must pass through.
        var source = @"
#CppInclude <foo.h>
Sub Main()
    Dim x As ns::First
End Sub";
        var output = CompileToCppFull(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("ns::First x"));
    }

    // ==================================================================
    // Bug 2 — a foreign call consumed as an argument must emit exactly once.
    // Previously the call was emitted BOTH as a standalone statement AND
    // inline at the consumption site (double side effect).
    // ==================================================================

    [Test]
    public void Cpp_ForeignCall_InArgumentPosition_EmittedExactlyOnce()
    {
        var source = @"
#CppInclude <mutex>
Sub Main()
    Dim m As std::mutex
    Console.WriteLine(m.try_lock())
End Sub";
        var output = CompileToCppFull(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));

        // Exactly one occurrence of the call.
        var calls = output!.Split(new[] { "m.try_lock()" }, System.StringSplitOptions.None).Length - 1;
        Assert.That(calls, Is.EqualTo(1),
            "a foreign call consumed as an argument must be emitted once, not duplicated");
        // The standalone `m.try_lock();` statement must NOT appear when the result is consumed.
        Assert.That(output, Does.Not.Contain("m.try_lock();"),
            "no standalone side-effecting statement when the foreign result is consumed inline");
    }

    // ==================================================================
    // Bug 3 — a foreign result may be captured in a typed foreign local.
    // Foreign<->Foreign assignment is opaquely compatible; the local with an
    // initializer must emit a single `type name = expr;` (foreign types may be
    // non-default-constructible / non-assignable).
    // ==================================================================

    [Test]
    public void Cpp_ForeignResult_CapturedInTypedLocal_CompilesAndRuns()
    {
        var source = @"
#CppInclude <vector>
Sub Main()
    Dim v As std::vector(Of Integer)
    v.push_back(10)
    v.push_back(20)
    Dim n As std::size_t = v.size()
    Console.WriteLine(""ok"")
End Sub";
        var output = CompileToCppFull(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        // Single-line init, not declare-then-assign (foreign types may be non-assignable).
        Assert.That(output, Does.Match(@"std::size_t n = v\.size\(\);"),
            "a foreign local with an initializer must emit `type name = expr;` in one line");
        // No bare default declaration `std::size_t n;` on its own line.
        Assert.That(output, Does.Not.Match(@"std::size_t n;\s"),
            "no separate default declaration when the foreign local has an initializer");

        var compiler = VisualGameStudio.Tests.Native.CppCompile.FindRunCompiler();
        if (compiler == null) Assert.Ignore("No C++ compiler");
        Assert.That(VisualGameStudio.Tests.Native.CppCompile.CompileAndRun(output, compiler.Value).Replace("\r\n", "\n"),
            Is.EqualTo("ok\n"));
    }

    [Test]
    public void Cpp_ForeignTemplatedIteratorType_PostGenericScope_MapsCorrectly()
    {
        // std::vector(Of Integer)::iterator -> std::vector<int32_t>::iterator:
        // a ::-qualified segment AFTER the generic arguments.
        var source = @"
#CppInclude <vector>
Sub Main()
    Dim it As std::vector(Of Integer)::iterator
End Sub";
        var output = CompileToCppFull(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("std::vector<int32_t>::iterator it"));
    }

    // ==================================================================
    // Bug 3 regression (C2362) — a foreign local whose FIRST write is inside a
    // branch/loop must NOT have its declaration deferred to that write: a `goto`
    // over the label would skip the initialization (MSVC C2362). Deferral is
    // only safe when the first write is in the entry block (which dominates all
    // uses). Otherwise the plain top-level `type name;` declaration is emitted.
    // ==================================================================

    [Test]
    public void Cpp_ForeignLocal_FirstWriteInsideIf_CompilesWithoutC2362()
    {
        var source = @"
#CppInclude <vector>
Sub Main()
    Dim v As std::vector(Of Integer)
    v.push_back(10)
    Dim n As std::size_t
    Dim flag As Boolean = True
    If flag Then
        n = v.size()
    End If
    Console.WriteLine(""ok"")
End Sub";
        var output = CompileToCppFull(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        // The declaration must be hoisted to the top (default-construct), NOT deferred
        // into the If body where a goto would jump over its initialization.
        Assert.That(output, Does.Contain("std::size_t n;"),
            "first-write-in-branch must fall back to a top-level plain declaration");

        var compiler = VisualGameStudio.Tests.Native.CppCompile.FindRunCompiler();
        if (compiler == null) Assert.Ignore("No C++ compiler");
        // Before the fix this failed to compile with C2362 (init skipped by goto).
        Assert.That(VisualGameStudio.Tests.Native.CppCompile.CompileAndRun(output, compiler.Value).Replace("\r\n", "\n"),
            Is.EqualTo("ok\n"));
    }

    [Test]
    public void Cpp_ForeignLocal_FirstWriteInsideFor_CompilesWithoutC2362()
    {
        var source = @"
#CppInclude <vector>
Sub Main()
    Dim v As std::vector(Of Integer)
    v.push_back(10)
    Dim n As std::size_t
    Dim i As Integer
    For i = 1 To 3
        n = v.size()
    Next
    Console.WriteLine(""ok"")
End Sub";
        var output = CompileToCppFull(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("std::size_t n;"),
            "first-write-in-loop must fall back to a top-level plain declaration");

        var compiler = VisualGameStudio.Tests.Native.CppCompile.FindRunCompiler();
        if (compiler == null) Assert.Ignore("No C++ compiler");
        Assert.That(VisualGameStudio.Tests.Native.CppCompile.CompileAndRun(output, compiler.Value).Replace("\r\n", "\n"),
            Is.EqualTo("ok\n"));
    }

    // ==================================================================
    // Bug (foreign New) — `New <::-foreign-type>(...)` must keep '::' and emit a
    // VALUE construction with a single, correctly-declared temp.
    //
    // Before the fix Visit(IRNewObject) built the constructed type with
    // SanitizeName(ClassName), which STRIPS '::' (std::string -> stdstring,
    // std::vector -> stdvector, my::ns::Foo -> mynsFoo), and named the holding temp
    // inconsistently (declared `t0`, assigned `t1` / never declared). The generated
    // C++ failed to compile (C3861 undeclared type + C2065 undeclared temp). Foreign
    // types are VALUES (not std::shared_ptr / make_shared like collections/classes),
    // so the fix emits `auto <temp> = <foreign-type-verbatim>(<args>);`.
    // ==================================================================

    [Test]
    public void Cpp_ForeignNew_StdString_KeepsScopeResolution_CompilesAndRuns()
    {
        var source = @"
#CppInclude <string>
Sub Main()
    Dim s As std::string = New std::string(""hello"")
    Console.WriteLine(s.size())
End Sub";
        var output = CompileToCppFull(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        // '::' preserved (NOT stripped to `stdstring`); value construction of the verbatim type.
        Assert.That(output, Does.Contain("std::string(\"hello\")"),
            "New std::string(...) must construct the verbatim '::'-qualified type, not 'stdstring'");
        Assert.That(output, Does.Not.Contain("stdstring"),
            "SanitizeName must not strip '::' from a constructed foreign type name");

        var compiler = VisualGameStudio.Tests.Native.CppCompile.FindRunCompiler();
        if (compiler == null) Assert.Ignore("No C++ compiler");
        // "hello".size() == 5.
        Assert.That(VisualGameStudio.Tests.Native.CppCompile.CompileAndRun(output, compiler.Value).Replace("\r\n", "\n"),
            Is.EqualTo("5\n"));
    }

    [Test]
    public void Cpp_ForeignNew_StdVectorOfInteger_MapsGenericArgs_Compiles()
    {
        var source = @"
#CppInclude <vector>
Sub Main()
    Dim v As std::vector(Of Integer) = New std::vector(Of Integer)()
    v.push_back(7)
    Console.WriteLine(v.size())
End Sub";
        var output = CompileToCppFull(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        // Generic args mapped ((Of Integer) -> <int32_t>) with '::' intact.
        Assert.That(output, Does.Contain("std::vector<int32_t>()"),
            "New std::vector(Of Integer)() must construct std::vector<int32_t>, not 'stdvector'");
        Assert.That(output, Does.Not.Contain("stdvector"));

        var compiler = VisualGameStudio.Tests.Native.CppCompile.FindRunCompiler();
        if (compiler == null) Assert.Ignore("No C++ compiler");
        Assert.That(VisualGameStudio.Tests.Native.CppCompile.CompileAndRun(output, compiler.Value).Replace("\r\n", "\n"),
            Is.EqualTo("1\n"));
    }

    [Test]
    public void Cpp_ForeignNew_MultiSegmentNamespace_WithArgs_CompilesAndRuns()
    {
        // my::ns::Foo(1, 2) — a multi-segment '::' name with constructor arguments.
        var source = @"
#CppInclude ""myns.h""
Sub Main()
    Dim f As my::ns::Foo = New my::ns::Foo(1, 2)
    Console.WriteLine(f.sum())
End Sub";
        var output = CompileToCppFull(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("my::ns::Foo(1, 2)"),
            "multi-segment '::' names must be emitted verbatim with their constructor args");
        Assert.That(output, Does.Not.Contain("mynsFoo"));

        var compiler = VisualGameStudio.Tests.Native.CppCompile.FindRunCompiler();
        if (compiler == null) Assert.Ignore("No C++ compiler");
        var header = new KeyValuePair<string, string>(
            "myns.h",
            "#pragma once\n#include <cstdint>\n" +
            "namespace my { namespace ns { struct Foo { int a; int b; " +
            "Foo(int x, int y):a(x),b(y){} int sum(){return a+b;} }; } }\n");
        var run = VisualGameStudio.Tests.Native.CppCompile.CompileAndRun(
            output, compiler.Value, new[] { header });
        Assert.That(run.Replace("\r\n", "\n"), Is.EqualTo("3\n"));  // 1 + 2
    }

    // ==================================================================
    // Bug (type-inferred foreign local) — `Dim x = <foreignCall>()` inferred the
    // synthetic member-path pseudo-type ("probe::Widget::compute") as the DECLARED
    // C++ type (`probe::Widget::compute x = ...;`), which is not a real type (C2146/
    // C3867/C2065). The inferred type must be emitted as `auto`.
    // ==================================================================

    [Test]
    public void Cpp_TypeInferredForeignLocal_UsesAuto_CompilesAndRuns()
    {
        var source = @"
#CppInclude ""counter.h""
Sub Main()
    Dim w As probe::Widget
    Dim x = w.compute()
    Console.WriteLine(x)
End Sub";
        var output = CompileToCppFull(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        // Declared `auto`, not the synthetic member-path pseudo-type.
        Assert.That(output, Does.Contain("auto x = w.compute();"),
            "a type-inferred opaque foreign local must be declared 'auto', not its pseudo-type");
        Assert.That(output, Does.Not.Contain("probe::Widget::compute x"),
            "the synthetic member-path pseudo-type must never be used as a declaration type");

        var compiler = VisualGameStudio.Tests.Native.CppCompile.FindRunCompiler();
        if (compiler == null) Assert.Ignore("No C++ compiler");
        var header = new KeyValuePair<string, string>(
            "counter.h",
            "#pragma once\nnamespace probe { struct Widget { int compute(){ return 42; } }; }\n");
        var run = VisualGameStudio.Tests.Native.CppCompile.CompileAndRun(
            output, compiler.Value, new[] { header });
        Assert.That(run.Replace("\r\n", "\n"), Is.EqualTo("42\n"));
    }

    [Test]
    public void Cpp_TypeInferredForeignLocal_ExplicitTypeStillWorks()
    {
        // Regression guard: an EXPLICITLY named foreign type must NOT be forced to `auto`
        // or rejected — `Dim n As std::size_t = v.size()` keeps its verbatim declared type.
        var source = @"
#CppInclude <vector>
Sub Main()
    Dim v As std::vector(Of Integer)
    v.push_back(1)
    Dim n As std::size_t = v.size()
    Console.WriteLine(""ok"")
End Sub";
        var output = CompileToCppFull(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Match(@"std::size_t n = v\.size\(\);"),
            "an explicit foreign local type must stay verbatim, not become 'auto'");
    }

    [Test]
    public void Cpp_TypeInferredForeignLocal_InsideControlFlowBlock_CleanRejection()
    {
        // The HARDER form: a type-inferred foreign capture whose declaration sits inside a
        // control-flow body would need a deferred `auto x = expr;` that a goto could jump over
        // (MSVC C2362). Rather than emit non-compiling C++, the compiler rejects it with a clean,
        // actionable diagnostic (explicit-type declarations are the escape hatch).
        var source = @"
#CppInclude ""counter.h""
Sub Main()
    Dim w As probe::Widget
    Dim flag As Boolean = True
    If flag Then
        Dim x = w.compute()
        Console.WriteLine(x)
    End If
    Console.WriteLine(""end"")
End Sub";
        var output = CompileToCppFull(source, out var errors);
        Assert.That(output, Is.Null, "a type-inferred opaque foreign capture in a control-flow block must be rejected");
        Assert.That(string.Join("; ", errors),
            Does.Contain("Cannot infer the type of an opaque C++ expression"),
            "the rejection must be a clean, actionable BasicLang diagnostic (never non-compiling C++)");
    }

    // ==================================================================
    // Bug (foreign data-member read) — reading a data member off a '::'-qualified
    // foreign value (`Dim x = c.n`) materialized an opaque foreign-typed temp that
    // DeclareLocalsAndTemporaries skips (its pseudo-type "mathlib::Calc::n" is not a
    // real C++ type), then emitted `t1 = c.n;` — an assignment to an UNDECLARED temp
    // (C2065) plus a cascade `operator <<` ambiguity. Foreign METHOD calls worked
    // (inlined via GetValueName); foreign FIELD reads were never given the same inline
    // treatment. Fix: render a foreign data-member read inline at each use site,
    // exactly like a foreign method call.
    // ==================================================================

    [Test]
    public void Cpp_ForeignDataMemberRead_InlinesAtUseSite_CompilesAndRuns()
    {
        var source = @"
#CppInclude ""calc.h""
Sub Main()
    Dim c As New mathlib::Calc()
    Dim sum = c.Add(2, 3)   ' foreign method call  (already worked)
    Dim nn = c.n            ' foreign data member  (the bug)
    Dim mm = c.m
    Console.WriteLine(sum)
    Console.WriteLine(nn)
    Console.WriteLine(mm)
End Sub";
        var output = CompileToCppFull(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        // The read is inlined into its consumer, never stored to an undeclared temp.
        Assert.That(output, Does.Contain("auto nn = c.n;"),
            "a foreign data-member read must be inlined into its consumer");
        Assert.That(output, Does.Contain("auto mm = c.m;"));

        var compiler = VisualGameStudio.Tests.Native.CppCompile.FindRunCompiler();
        if (compiler == null) Assert.Ignore("No C++ compiler");
        var header = new KeyValuePair<string, string>(
            "calc.h",
            "#pragma once\nnamespace mathlib { struct Calc { " +
            "int Add(int a, int b) const { return a + b; } int n = 10; int m = 20; }; }\n");
        var run = VisualGameStudio.Tests.Native.CppCompile.CompileAndRun(
            output, compiler.Value, new[] { header });
        Assert.That(run.Replace("\r\n", "\n"), Is.EqualTo("5\n10\n20\n"));  // Add(2,3), n, m
    }
}
