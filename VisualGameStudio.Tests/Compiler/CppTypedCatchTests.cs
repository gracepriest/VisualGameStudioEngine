using System.Text.RegularExpressions;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.CPlusPlus;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// P2a-2 Task 1 (spec §11.1): typed catch on the C++ backend.
///
/// A managed exception crossing the .NET boundary arrives as a
/// <c>BasicLang::NetException</c> carrying the thrown type's fully-qualified
/// inheritance chain (most-derived first, ';'-separated). A <c>Try</c> with at
/// least one .NET-typed clause emits ONE leading
/// <c>catch (const BasicLang::NetException&amp; __nex)</c> holding an if/else-if
/// ladder over ALL clauses in source order (chain element-equality matching),
/// ending in a bare <c>throw;</c>. The existing MapCatchType-derived per-clause
/// handlers follow unchanged for the locally-thrown shape
/// (<c>Throw New ArgumentException(...)</c> stays <c>std::runtime_error</c>).
/// <c>&lt;catchVar&gt;.Message</c> lowers to <c>BasicLang::String(&lt;var&gt;.what())</c>.
/// </summary>
[TestFixture]
public class CppTypedCatchTests
{
    // ========================================================================
    // Helpers (emit-level: no preprocessor; full: preprocessor for #CppInclude)
    // ========================================================================

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

    /// <summary>
    /// Full-pipeline variant running the Preprocessor first so <c>#CppInclude</c>
    /// directives reach the generated C++ (mirrors CppPassthroughTests.CompileToCppFull).
    /// </summary>
    private string CompileToCppFull(string source, out List<string> errors)
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
        irModule.CppIncludes.AddRange(pre.CppIncludes);

        return new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false }).Generate(irModule);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += needle.Length;
        }
        return count;
    }

    /// <summary>The text of Main's DEFINITION onward (skips the runtime preamble and
    /// the forward declaration, so handler-shape assertions can't match splice text).</summary>
    private static string MainRegion(string output)
    {
        var idx = output.LastIndexOf("void Main()", StringComparison.Ordinal);
        Assert.That(idx, Is.GreaterThanOrEqualTo(0), "generated output has no Main definition:\n" + output);
        return output.Substring(idx);
    }

    // ========================================================================
    // Step 1a: the per-Try NetException ladder
    // ========================================================================

    [Test]
    public void Cpp_TypedCatch_TwoNetClauses_EmitsLeadingNetExceptionLadder()
    {
        var source = @"
Sub Main()
    Try
        Dim x As Integer = 1
    Catch a As ArgumentNullException
        Console.WriteLine(""ane"")
    Catch b As Exception
        Console.WriteLine(""ex"")
    End Try
End Sub";

        var output = CompileToCpp(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));

        var main = MainRegion(output);

        // Exactly ONE leading combined handler for the whole Try.
        Assert.That(CountOccurrences(main, "catch (const BasicLang::NetException&"), Is.EqualTo(1),
            "expected exactly one NetException handler for the Try:\n" + main);

        var ladderStart = main.IndexOf("catch (const BasicLang::NetException& __nex)", StringComparison.Ordinal);
        Assert.That(ladderStart, Is.GreaterThanOrEqualTo(0),
            "missing the leading combined NetException handler:\n" + main);

        // ORDER IS LOAD-BEARING: NetException derives from std::runtime_error, so the
        // combined handler must precede every MapCatchType-derived handler.
        var firstStdHandler = main.IndexOf("catch (const std::", StringComparison.Ordinal);
        Assert.That(firstStdHandler, Is.GreaterThan(ladderStart),
            "the NetException handler must precede all std:: handlers:\n" + main);

        // The ladder holds ALL clauses in source order (chain element matching), then
        // a bare rethrow reached only when no clause matched.
        var aneArm = main.IndexOf("if (__nex.Matches(\"System.ArgumentNullException\"))", StringComparison.Ordinal);
        var exArm = main.IndexOf("else if (__nex.Matches(\"System.Exception\"))", StringComparison.Ordinal);
        var rethrow = main.IndexOf("throw;", StringComparison.Ordinal);
        Assert.That(aneArm, Is.GreaterThan(ladderStart), "missing the ArgumentNullException arm:\n" + main);
        Assert.That(exArm, Is.GreaterThan(aneArm), "the Exception arm must follow in source order:\n" + main);
        Assert.That(rethrow, Is.GreaterThan(exArm).And.LessThan(firstStdHandler),
            "the ladder must end in a bare throw; before the per-clause handlers:\n" + main);

        // The per-clause handlers follow UNCHANGED (the locally-thrown shape).
        Assert.That(main, Does.Contain("catch (const std::runtime_error& a)"));
        Assert.That(main, Does.Contain("catch (const std::exception& b)"));
    }

    [Test]
    public void Cpp_TypedCatch_NoNetTypedClause_EmitsNoLadder()
    {
        // Finally-only: no catch clause at all -> byte-identical shape to before
        // (no NetException handler; the catch(...) finally pair only).
        var source = @"
Sub Main()
    Dim n As Integer = 0
    Try
        n = 1
    Finally
        n = 2
    End Try
End Sub";

        var output = CompileToCpp(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));

        var main = MainRegion(output);
        Assert.That(main, Does.Not.Contain("catch (const BasicLang::NetException&"),
            "a Try with no .NET-typed clause must not emit the ladder:\n" + main);
        Assert.That(main, Does.Contain("catch (...)"));
    }

    // ========================================================================
    // Step 1b: ladder label suffixing (_nex) — no duplicate C++ labels
    // ========================================================================

    [Test]
    public void Cpp_TypedCatch_CatchBodyControlFlow_LadderLabelsSuffixed_NoDuplicates()
    {
        var source = @"
Sub Main()
    Dim x As Integer = 1
    Try
        x = 2
    Catch ex As Exception
        If x > 0 Then
            Console.WriteLine(""pos"")
        Else
            Console.WriteLine(""neg"")
        End If
    End Try
End Sub";

        var output = CompileToCpp(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));

        var main = MainRegion(output);

        // The catch body is emitted twice (ladder arm + per-clause handler); C++ labels
        // are function-scoped, so the ladder copy must carry the _nex suffix.
        var labels = Regex.Matches(main, @"(?m)^\s*([A-Za-z_][A-Za-z0-9_]*): ;\s*$")
                          .Select(m => m.Groups[1].Value)
                          .ToList();
        Assert.That(labels, Is.Not.Empty, "expected interior labels for the If inside the catch body:\n" + main);
        Assert.That(labels, Is.Unique,
            "duplicate C++ label definition (clang 'redefinition of label' / MSVC C2045):\n" + main);
        Assert.That(labels.Any(l => l.EndsWith("_nex", StringComparison.Ordinal)), Is.True,
            "the ladder copy of the catch body must emit its labels with the _nex suffix:\n" + main);
        Assert.That(labels.Any(l => !l.EndsWith("_nex", StringComparison.Ordinal)
                                 && !l.EndsWith("_fex", StringComparison.Ordinal)
                                 && !l.EndsWith("_fnorm", StringComparison.Ordinal)), Is.True,
            "the per-clause copy keeps its labels unsuffixed:\n" + main);
    }

    /// <summary>
    /// P2a-2 Task 4: the three <c>_regionLabelSuffix</c> sites now SAVE/RESTORE and COMPOSE
    /// instead of literal-assign/reset — which retires the whole nested-copy label-collision
    /// class. This is the pre-existing finally-inside-finally variant: the outer finally body is
    /// emitted twice, the inner finally twice per copy, and with literal suffixes both outer
    /// copies emitted the inner labels as "_fex"/"_fnorm" (and the literal RESET to "" also
    /// stripped the outer suffix mid-region). Composition gives the four inner copies
    /// _fex_fex / _fex_fnorm / _fnorm_fex / _fnorm_fnorm.
    /// </summary>
    [Test]
    public void Cpp_FinallyInsideFinally_ControlFlow_NoDuplicateLabels()
    {
        var source = @"
Sub Main()
    Dim x As Integer = 1
    Try
        x = 2
    Finally
        Try
            x = 3
        Finally
            If x > 0 Then
                Console.WriteLine(""pos"")
            Else
                Console.WriteLine(""neg"")
            End If
        End Try
    End Try
End Sub";

        var output = CompileToCpp(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));

        var main = MainRegion(output);
        var labels = Regex.Matches(main, @"(?m)^\s*([A-Za-z_][A-Za-z0-9_]*): ;\s*$")
                          .Select(m => m.Groups[1].Value)
                          .ToList();
        Assert.That(labels, Is.Not.Empty,
            "expected interior labels for the If inside the nested finally body:\n" + main);
        Assert.That(labels, Is.Unique,
            "duplicate C++ label definition (clang 'redefinition of label' / MSVC C2045) — the "
            + "nested finally's copies must COMPOSE suffixes (_fex_fex, _fex_fnorm, …), not "
            + "re-assign literals:\n" + main);
        Assert.That(labels.Any(l => l.EndsWith("_fex_fnorm", StringComparison.Ordinal)
                                 || l.EndsWith("_fnorm_fnorm", StringComparison.Ordinal)), Is.True,
            "composed suffixes expected for the inner finally's copies:\n" + main);
    }

    // ========================================================================
    // Step 1c: <catchVar>.Message lowers to what()
    // ========================================================================

    [Test]
    public void Cpp_TypedCatch_CatchVarMessage_LowersToWhat()
    {
        var source = @"
Sub Main()
    Try
        Dim x As Integer = 1
    Catch ex As Exception
        Console.WriteLine(ex.Message)
    End Try
End Sub";

        var output = CompileToCpp(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));

        var main = MainRegion(output);

        // The body is emitted twice (ladder arm + per-clause handler); the lowering is
        // identical in both — NetException's message IS its what(), and the per-clause
        // std::exception/std::runtime_error bindings carry the message the same way.
        Assert.That(CountOccurrences(main, "BasicLang::String(ex.what())"), Is.EqualTo(2),
            "ex.Message must lower to BasicLang::String(ex.what()) in BOTH copies:\n" + main);
        Assert.That(main, Does.Not.Contain("->Message"),
            "raw member access on an exception binding is not valid C++:\n" + main);
        Assert.That(main, Does.Not.Contain(".Message"),
            "raw member access on an exception binding is not valid C++:\n" + main);
    }

    // ========================================================================
    // Step 2: the NetException runtime splice — unconditional in BOTH modes
    // ========================================================================

    [Test]
    public void Cpp_CombinedEmission_SplicesNetExceptionRuntime_Once()
    {
        // A program with no Try, no .NET anything: the declaration is ALWAYS there
        // (spec §11.1 — the trigger is source-level; four pre-existing fixtures carry
        // typed Catch with no .NET surface).
        var output = CompileToCpp("Sub Main()\nEnd Sub", out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(CountOccurrences(output, "class NetException : public std::runtime_error"),
            Is.EqualTo(1), "combined emission must splice NetException exactly once");
    }

    [Test]
    public void Cpp_SplitEmission_SplicesNetExceptionRuntime_OnceInRuntimeHeader()
    {
        var source = "Sub Main()\nEnd Sub";
        var tokens = new Lexer(source).Tokenize();
        var ast = new Parser(tokens).Parse();
        var analyzer = new SemanticAnalyzer();
        Assert.That(analyzer.Analyze(ast), Is.True);
        var irModule = new IRBuilder(analyzer).Build(ast, "TestModule");

        var gen = new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false });
        var split = gen.GenerateSplit(irModule, "TestProj", new[] { irModule }, emitMain: true);

        var runtime = split.Files[CppCodeGenerator.RuntimeHeaderFileName];
        Assert.That(CountOccurrences(runtime, "class NetException : public std::runtime_error"),
            Is.EqualTo(1), "split emission must splice NetException exactly once, in the runtime header");
    }

    // ========================================================================
    // Step 3: compile-and-run (the real proof) — a #CppInclude helper throws a
    // real BasicLang::NetException with a full inheritance chain.
    // ========================================================================

    /// <summary>Declaration-only helper header (included BEFORE the runtime splice, so it
    /// cannot reference NetException itself); the definition is appended after the
    /// generated output, where NetException is in scope.</summary>
    private const string ThrowHelperHeader =
        "#pragma once\n" +
        "namespace testnet {\n" +
        "    void boom();\n" +
        "}\n";

    private const string ThrowHelperDefinition = "\n" +
        "namespace testnet {\n" +
        "    void boom() {\n" +
        "        throw BasicLang::NetException(\n" +
        "            \"System.ArgumentNullException;System.ArgumentException;System.SystemException;System.Exception\",\n" +
        "            \"boom\");\n" +
        "    }\n" +
        "}\n";

    private string CompileAndRunWithThrowHelper(string source)
    {
        var output = CompileToCppFull(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));

        var compiler = VisualGameStudio.Tests.Native.CppCompile.FindRunCompiler();
        if (compiler == null) Assert.Ignore("No C++ compiler available");

        var header = new KeyValuePair<string, string>("netthrow.h", ThrowHelperHeader);
        return VisualGameStudio.Tests.Native.CppCompile.CompileAndRun(
            output + ThrowHelperDefinition, compiler.Value, new[] { header })
            .Replace("\r\n", "\n");
    }

    [Test]
    [Category("Integration")]
    public void Cpp_Run_TypedCatch_SubclassMatch_CatchesViaChain()
    {
        // Thrown: ArgumentNullException. Clause: ArgumentException — a SUBCLASS match
        // through the inheritance chain, not a name-equality match.
        var run = CompileAndRunWithThrowHelper(@"
#CppInclude ""netthrow.h""
Sub Main()
    Try
        testnet::boom()
        Console.WriteLine(""no-throw"")
    Catch e As ArgumentException
        Console.WriteLine(""caught"")
        Console.WriteLine(e.Message)
    End Try
End Sub");
        Assert.That(run, Is.EqualTo("caught\nboom\n"));
    }

    [Test]
    [Category("Integration")]
    public void Cpp_Run_TypedCatch_SourceOrderPreference_FirstMatchingClauseWins()
    {
        // BOTH clauses match the chain (thrown most-derived: ArgumentNullException);
        // the FIRST in source order wins (.NET semantics: first assignable clause).
        // A broken ladder that preferred the broadest/last match would print "EX".
        //
        // Clause pair constraint (PRE-EXISTING, unchanged by §11.1): the per-clause
        // MapCatchType handlers still collapse every non-Exception name to
        // catch (const std::runtime_error&), so two such clauses on one Try emit a
        // duplicate C++ handler — MSVC rejects that outright (C2312). The pair here
        // (ArgumentException -> runtime_error, Exception -> exception) keeps the
        // per-clause emission derived-then-base and compilable.
        var run = CompileAndRunWithThrowHelper(@"
#CppInclude ""netthrow.h""
Sub Main()
    Try
        testnet::boom()
    Catch a As ArgumentException
        Console.WriteLine(""AE"")
    Catch b As Exception
        Console.WriteLine(""EX"")
    End Try
End Sub");
        Assert.That(run, Is.EqualTo("AE\n"));
    }

    [Test]
    [Category("Integration")]
    public void Cpp_Run_TypedCatch_NoClauseMatches_PropagatesOut()
    {
        // The inner Try's only clause does not match the chain: the ladder's bare
        // throw; must propagate PAST the inner per-clause handlers to the outer Try.
        var run = CompileAndRunWithThrowHelper(@"
#CppInclude ""netthrow.h""
Sub Main()
    Try
        Try
            testnet::boom()
        Catch e As InvalidOperationException
            Console.WriteLine(""inner"")
        End Try
    Catch o As Exception
        Console.WriteLine(""outer"")
        Console.WriteLine(o.Message)
    End Try
End Sub");
        Assert.That(run, Is.EqualTo("outer\nboom\n"));
    }

    [Test]
    [Category("Integration")]
    public void Cpp_Run_TypedCatch_CatchException_MatchesAnyChain()
    {
        // Every chain ends "System.Exception" — element equality on that suffix works.
        var run = CompileAndRunWithThrowHelper(@"
#CppInclude ""netthrow.h""
Sub Main()
    Try
        testnet::boom()
    Catch e As Exception
        Console.WriteLine(""any"")
        Console.WriteLine(e.Message)
    End Try
End Sub");
        Assert.That(run, Is.EqualTo("any\nboom\n"));
    }

    [Test]
    [Category("Integration")]
    public void Cpp_Run_TypedCatch_DualShape_BlThrownCaughtByOwnTypedClause()
    {
        // BL-thrown Throw New ArgumentException(...) stays std::runtime_error: it falls
        // THROUGH the ladder (not a NetException) to the per-clause handler in the very
        // same method that carries the ladder.
        var run = CompileAndRunWithThrowHelper(@"
#CppInclude ""netthrow.h""
Sub Main()
    Try
        Throw New ArgumentException(""x"")
    Catch e As ArgumentException
        Console.WriteLine(""bl-caught"")
        Console.WriteLine(e.Message)
    End Try
End Sub");
        Assert.That(run, Is.EqualTo("bl-caught\nx\n"));
    }
}
