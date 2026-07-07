using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.CPlusPlus;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// C++ backend collection tests: the backend ACCEPTS List/Dictionary/HashSet and maps them to
/// the BasicLang::List/Dictionary/HashSet wrappers wrapped in std::shared_ptr (REFERENCE
/// semantics — .NET List/Dictionary/HashSet are reference types, so ByVal params/assignment/
/// field stores/lambda capture must ALIAS, matching .NET and the C# backend). It lowers
/// collection OPERATIONS — member calls (via -&gt;), .Count/.Keys/.Values, indexer read/write
/// (deref/-&gt;Get/-&gt;Set), For Each (deref) — to correct, .NET-faithful C++ that compiles
/// AND runs, and the cross-boundary sharing tests below prove the reference semantics match .NET.
/// </summary>
//
// ============================================================================
// TASK 3 INVESTIGATION SPIKE FINDINGS (recorded before implementation)
// ============================================================================
//
// SPIKE 1a — How does `l.Count` arrive in the IR?
//   ANSWER: `.Count` (a property access with NO parens) is an **IRFieldAccess**.
//   IRBuilder.Visit(MemberAccessExpressionNode) builds an IRFieldAccess for any
//   `obj.Member` with no call syntax. The C++ backend raw-emitted it as `l->Count`
//   (a member READ, wrong on two counts: `->` on a value, and missing `()`).
//   => Step 5 property-bridge IS required: rewrite .Count/.Keys/.Values on a
//      collection receiver to a method call `recv.Count()`.
//   (Contrast: `l.Count()` WITH parens would be an IRInstanceMethodCall and would
//    raw-passthrough to `.Count()` on its own — but users write `.Count` w/o parens.)
//
// SPIKE 1b — How does `d("x") = 1` (indexed WRITE) lower?
//   ANSWER (the important one): it was **SILENTLY DROPPED**. In VB, `d("x")` uses
//   PARENS, so the parser produces a **CallExpressionNode** (not ArrayAccessExpressionNode,
//   which needs brackets `d["x"]`). IRBuilder.Visit(AssignmentStatementNode) only handled
//   IdentifierExpressionNode / MemberAccessExpressionNode / ArrayAccessExpressionNode
//   targets — a CallExpressionNode target fell through and emitted NOTHING. Same for
//   `l(0) = 9`. The READ side (`d("x")`, `l(0)`) correctly lowers to IRIndexerAccess.
//   => Fix is in IRBuilder, not just the C++ backend: detect a collection-indexer
//      assignment target (CallExpressionNode or bracket ArrayAccessExpressionNode over an
//      indexable generic type) and emit a NEW **IRIndexerStore** node (Collection, Indices,
//      Value). Backends then lower it faithfully:
//        - C++ Dictionary write -> `.Set(k, v)` (insert-or-update); List/array write -> `[i] = v`
//        - C#  -> `collection[index] = value` (correct for both List and Dictionary in .NET)
//   Dictionary READ of a missing key -> `.Get(k)` which throws (kept .NET-faithful).
// ============================================================================
[TestFixture]
public class CppCollectionTests
{
    /// <summary>
    /// Helper: compile BasicLang source to C++ output string.
    /// Returns null and populates errors list if a pipeline stage fails.
    /// CppCapabilityException from the generator propagates to the caller.
    /// </summary>
    private string? CompileToCpp(string source, out List<string> errors)
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
    /// Compile BasicLang source to C++ AFTER running the SAME standard IR optimizer passes the
    /// CLI (`BasicLang.exe compile`) runs. This is load-bearing for the For Each / Try
    /// codegen tests: the plain <see cref="CompileToCpp"/> path skips optimization, but the
    /// DeadCodeEliminationPass is what exposed the block-drop / temp-hoist bugs — it used to
    /// delete the (CFG-unreachable) For Each / Try body and continuation blocks, so the
    /// post-loop/post-try statements and the temporaries produced inside those bodies
    /// vanished from the emitted code. Tests that must reproduce those bugs have to optimize.
    /// </summary>
    private string? CompileToCppOptimized(string source, out List<string> errors)
    {
        errors = new List<string>();

        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();

        var parser = new Parser(tokens);
        ProgramNode ast;
        try { ast = parser.Parse(); }
        catch (Exception ex) { errors.Add($"Parse error: {ex.Message}"); return null; }

        var analyzer = new SemanticAnalyzer();
        if (!analyzer.Analyze(ast))
        {
            foreach (var err in analyzer.Errors) errors.Add($"Semantic error: {err.Message}");
            return null;
        }

        var irBuilder = new IRBuilder(analyzer);
        var irModule = irBuilder.Build(ast, "TestModule");

        // Run the standard optimizer passes exactly as the CLI compile path does.
        var pipeline = new BasicLang.Compiler.IR.Optimization.OptimizationPipeline();
        pipeline.AddStandardPasses();
        pipeline.Run(irModule);

        var gen = new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false });
        return gen.Generate(irModule);
    }

    [Test]
    public void Cpp_ListLocal_MapsToBasicLangListSharedPtr()
    {
        var source = @"
Sub Main()
    Dim numbers As New List(Of Integer)()
End Sub";
        var output = CompileToCpp(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        // Reference semantics: the local is a shared_ptr and construction is make_shared.
        Assert.That(output, Does.Contain("std::shared_ptr<BasicLang::List<int32_t>>"));
        Assert.That(output, Does.Contain("std::make_shared<BasicLang::List<int32_t>>"));
        // Never a nested/double wrap.
        Assert.That(output, Does.Not.Contain("make_shared<std::shared_ptr"));
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
        Assert.That(output, Does.Contain("std::shared_ptr<BasicLang::List<int32_t>>"));
        Assert.That(output, Does.Contain("class List"));                     // preamble emitted
        Assert.That(output, Does.Not.Contain("std::shared_ptr<list"));       // never lowercase
        Assert.That(output, Does.Not.Contain("std::make_shared<list"));
    }

    [Test]
    public void Cpp_DictionaryLocal_MapsToBasicLangDictionarySharedPtr()
    {
        var source = @"
Sub Main()
    Dim map As New Dictionary(Of String, Integer)()
End Sub";
        var output = CompileToCpp(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("std::shared_ptr<BasicLang::Dictionary<std::string, int32_t>>"));
        Assert.That(output, Does.Contain("std::make_shared<BasicLang::Dictionary<std::string, int32_t>>"));
    }

    [Test]
    public void Cpp_HashSetLocal_MapsToBasicLangHashSetSharedPtr()
    {
        var source = @"
Sub Main()
    Dim seen As New HashSet(Of Integer)()
End Sub";
        var output = CompileToCpp(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("std::shared_ptr<BasicLang::HashSet<int32_t>>"));
        Assert.That(output, Does.Contain("std::make_shared<BasicLang::HashSet<int32_t>>"));
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

    [Test]
    public void Cpp_UnboundCollectionTemporary_StillEmitsPreambleViaFallback()
    {
        // A `New List(...)` result that is NOT bound to a typed local: the fluent
        // call `New List(Of Integer)().Add(1)` produces an unbound IRNewObject whose
        // collection type is not carried on any declared local. Only the
        // ModuleUsesCollections IRNewObject body-scan fallback can detect it — the
        // type-position walk (which covers declared locals/globals/fields/signatures)
        // sees no collection type here. Proves the fallback earns its keep.
        var output = CompileToCpp(
            "Sub Main()\n New List(Of Integer)().Add(1)\nEnd Sub", out var e);
        Assert.That(e, Is.Empty, string.Join("; ", e));
        Assert.That(output, Does.Contain("class List"),
            "an unbound New List(...) temporary must still trigger the wrapper preamble via the IRNewObject fallback");
    }

    // ========================================================================
    // Task 3: collection OPERATIONS lower to correct C++ that compiles AND runs.
    // ========================================================================

    /// <summary>Compile the generated C++ with a real compiler, run it, return normalized stdout.
    /// Ignores when no C++ compiler is available on the machine.</summary>
    private static string CompileRun(string cppSource)
    {
        var compiler = VisualGameStudio.Tests.Native.CppCompile.FindRunCompiler();
        if (compiler == null) Assert.Ignore("No C++ compiler available on this machine");
        return VisualGameStudio.Tests.Native.CppCompile.CompileAndRun(cppSource, compiler.Value)
            .Replace("\r\n", "\n");
    }

    [Test]
    public void Cpp_ListOperations_CompileAndRun()
    {
        var source = @"
Sub Main()
    Dim l As New List(Of Integer)()
    l.Add(10)
    l.Add(20)
    l.Add(30)
    Dim total As Integer = 0
    For Each n In l
        total = total + n
    Next
    Console.WriteLine(total)
    Console.WriteLine(l.Count)
    Console.WriteLine(l(1))
End Sub";
        var output = CompileToCpp(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(CompileRun(output), Is.EqualTo("60\n3\n20\n"));
    }

    // ========================================================================
    // REGRESSION: For Each / Try body codegen through the OPTIMIZER (CLI path).
    //
    // Root cause (ControlFlowGraph.Build): the CFG wired edges only from branch
    // terminators, never from the structured IRForEach (-> BodyBlock/EndBlock) and
    // IRTryCatch (-> Try/Catch/Finally/EndBlock) instructions. So those blocks were
    // UNREACHABLE from entry and DeadCodeEliminationPass.RemoveUnreachableBlocks()
    // deleted them from Function.Blocks. Two symptoms, both only under the optimizer:
    //   BUG 1 — statements AFTER a For Each/Try were silently dropped (the deleted
    //           EndBlock held them), e.g. For-Each-then-Clear-then-Count printed 1,2
    //           instead of 1,2,0.
    //   BUG 2 — a temporary produced INSIDE a For Each/Try body was never declared at
    //           function scope (the deleted body block was skipped by the temp-collection
    //           pass), so the C++ failed to compile (C2065 undeclared identifier).
    // These MUST run through CompileToCppOptimized to reproduce; the non-optimized
    // CompileToCpp path never triggered them (that is why Cpp_ListOperations passed).
    // ========================================================================

    [Test]
    public void Cpp_ForEach_TrailingStatementsAfterLoop_NotDropped_CompileAndRun()
    {
        // BUG 1: the For Each body contains a call (Console.WriteLine) — the emitted-block
        // structure that triggered the drop — and there is code AFTER the loop (Clear, Count).
        // Correct .NET output is 1, 2, 0; the bug dropped Clear()+Count and printed only 1, 2.
        var source = @"
Sub Main()
    Dim l As New List(Of Integer)()
    l.Add(1)
    l.Add(2)
    For Each x In l
        Console.WriteLine(x)
    Next
    l.Clear()
    Console.WriteLine(l.Count)
End Sub";
        var output = CompileToCppOptimized(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(CompileRun(output), Is.EqualTo("1\n2\n0\n"));
    }

    [Test]
    public void Cpp_ForEach_BodyTemporary_DeclaredAndCompiles_CompileAndRun()
    {
        // BUG 2: `x + 1` produces a temporary used inside the loop body. It must be hoisted
        // to a function-scope declaration or the C++ won't compile (C2065). A numeric temp is
        // used deliberately (NOT a String & Integer concat, which hits an unrelated bug).
        var source = @"
Sub Main()
    Dim l As New List(Of Integer)()
    l.Add(5)
    l.Add(6)
    For Each x In l
        Console.WriteLine(x + 1)
    Next
End Sub";
        var output = CompileToCppOptimized(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        // Compiles-and-runs (would be C2065 before the fix): 5+1, 6+1.
        Assert.That(CompileRun(output), Is.EqualTo("6\n7\n"));
    }

    [Test]
    public void Cpp_Try_TrailingStatementsAfterEndTry_NotDropped_CompileAndRun()
    {
        // BUG 1 (Try variant): code AFTER End Try (and the last statement inside the Try body)
        // must survive. The Try body prints 1 then 2; after End Try prints 3. .NET output: 1,2,3.
        var source = @"
Sub Main()
    Try
        Console.WriteLine(1)
        Console.WriteLine(2)
    Catch ex As Exception
        Console.WriteLine(99)
    End Try
    Console.WriteLine(3)
End Sub";
        var output = CompileToCppOptimized(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(CompileRun(output), Is.EqualTo("1\n2\n3\n"));
    }

    [Test]
    public void Cpp_Try_BodyTemporary_DeclaredAndCompiles_CompileAndRun()
    {
        // BUG 2 (Try variant): a temporary produced inside the Try body (`n + 1`) must be
        // hoisted to function scope, or the generated C++ fails to compile (C2065).
        var source = @"
Sub Main()
    Dim n As Integer = 41
    Try
        Console.WriteLine(n + 1)
    Catch ex As Exception
        Console.WriteLine(0)
    End Try
End Sub";
        var output = CompileToCppOptimized(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(CompileRun(output), Is.EqualTo("42\n"));
    }

    [Test]
    public void Cpp_Array_ForEach_TrailingStatements_NotDropped()
    {
        // Bonus: the same fix covers a For Each over a plain ARRAY (not a collection) — the
        // block-drop was collection-agnostic (it lived in the CFG, not the collection lowering).
        // This asserts on the generated C++ rather than compile-and-run because the VB array
        // literal declaration `Dim arr() As Integer = {...}` trips a SEPARATE, pre-existing C++
        // array-declaration type-mapping bug (emits `Integer arr[]` instead of `int32_t arr[]`,
        // MSVC C2065) that is out of scope here. The point being pinned is that the statements
        // AFTER the For Each over the array survive optimization (they used to be dropped).
        var source = @"
Sub Main()
    Dim arr() As Integer = {10, 20, 30}
    Dim total As Integer = 0
    For Each v In arr
        total = total + v
    Next
    Console.WriteLine(total)
    Console.WriteLine(999)
End Sub";
        var output = CompileToCppOptimized(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        // The range-for over the array and BOTH trailing prints must be present.
        Assert.That(output, Does.Contain("for (int32_t v : arr)"));
        Assert.That(output, Does.Contain("cout << total << endl;"));
        Assert.That(output, Does.Contain("cout << 999 << endl;"),
            "the statement after the For Each over an array must survive optimization");
    }

    [Test]
    public void Cpp_MultipleElseIf_NoDuplicateLabels_CompileAndRun()
    {
        // Regression: ElseIf blocks used FIXED names ("elseif.then"/"elseif.else"), so a
        // function with two+ ElseIf clauses (here: one 3-branch chain PLUS a second If/ElseIf
        // statement) emitted duplicate `elseif_then:`/`elseif_else:` C++ labels -> C2045
        // "label redefined". This program only compiles-and-runs once each ElseIf label is
        // unique. Classify() must return one number per input to prove the branches are wired
        // correctly (not just that it compiles).
        var source = @"
Function Classify(n As Integer) As Integer
    If n < 0 Then
        Return 1
    ElseIf n = 0 Then
        Return 2
    ElseIf n < 10 Then
        Return 3
    Else
        Return 4
    End If
End Function

Function Parity(n As Integer) As Integer
    If n = 0 Then
        Return 100
    ElseIf n Mod 2 = 0 Then
        Return 200
    Else
        Return 300
    End If
End Function

Sub Main()
    Console.WriteLine(Classify(-5))
    Console.WriteLine(Classify(0))
    Console.WriteLine(Classify(7))
    Console.WriteLine(Classify(42))
    Console.WriteLine(Parity(4))
    Console.WriteLine(Parity(3))
End Sub";
        var output = CompileToCpp(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(CompileRun(output), Is.EqualTo("1\n2\n3\n4\n200\n300\n"));
    }

    [Test]
    public void Cpp_ListIndexerWrite_CompileAndRun()
    {
        // `l(i) = v` (paren-indexed WRITE) used to be silently dropped (Spike 1b).
        var source = @"
Sub Main()
    Dim l As New List(Of Integer)()
    l.Add(1)
    l.Add(2)
    l(0) = 99
    Console.WriteLine(l(0))
    Console.WriteLine(l(1))
End Sub";
        var output = CompileToCpp(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(CompileRun(output), Is.EqualTo("99\n2\n"));
    }

    [Test]
    public void Cpp_DictionaryOperations_CompileAndRun()
    {
        var source = @"
Sub Main()
    Dim d As New Dictionary(Of String, Integer)()
    d.Add(""a"", 1)
    d(""b"") = 2
    d(""a"") = 10
    Console.WriteLine(d.Count)
    Console.WriteLine(d(""a""))
    Console.WriteLine(d(""b""))
    Console.WriteLine(d.ContainsKey(""a""))
    Console.WriteLine(d.ContainsKey(""z""))
    Dim v As Integer = 0
    If d.TryGetValue(""b"", v) Then
        Console.WriteLine(v)
    End If
    Dim keySum As Integer = 0
    For Each val In d.Values
        keySum = keySum + val
    Next
    Console.WriteLine(keySum)
End Sub";
        var output = CompileToCpp(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        // Count=2, d[a]=10, d[b]=2, ContainsKey(a)=true(1), ContainsKey(z)=false(0),
        // TryGetValue -> 2, sum of values (10+2)=12.
        Assert.That(CompileRun(output), Is.EqualTo("2\n10\n2\n1\n0\n2\n12\n"));
    }

    [Test]
    public void Cpp_HashSetOperations_CompileAndRun()
    {
        var source = @"
Sub Main()
    Dim s As New HashSet(Of Integer)()
    Dim a As Boolean = s.Add(5)
    Dim b As Boolean = s.Add(5)
    s.Add(7)
    Console.WriteLine(a)
    Console.WriteLine(b)
    Console.WriteLine(s.Count)
    Console.WriteLine(s.Contains(5))
    Console.WriteLine(s.Contains(99))
End Sub";
        var output = CompileToCpp(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        // first Add=true(1), dup Add=false(0), Count=2, Contains(5)=true(1), Contains(99)=false(0)
        Assert.That(CompileRun(output), Is.EqualTo("1\n0\n2\n1\n0\n"));
    }

    /// <summary>
    /// Compile BasicLang source to a RUNNABLE C# program (with a Main entry point) via the
    /// same pipeline, for the C# portability test.
    /// </summary>
    private string? CompileToCSharp(string source, out List<string> errors)
    {
        errors = new List<string>();

        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();

        var parser = new Parser(tokens);
        ProgramNode ast;
        try { ast = parser.Parse(); }
        catch (Exception ex) { errors.Add($"Parse error: {ex.Message}"); return null; }

        var analyzer = new SemanticAnalyzer();
        if (!analyzer.Analyze(ast))
        {
            foreach (var err in analyzer.Errors) errors.Add($"Semantic error: {err.Message}");
            return null;
        }

        var irBuilder = new IRBuilder(analyzer);
        var irModule = irBuilder.Build(ast, "TestModule");

        var gen = new BasicLang.Compiler.CodeGen.CSharp.ImprovedCSharpCodeGenerator(
            new CodeGenOptions { GenerateMainMethod = true, GenerateComments = false });
        return gen.Generate(irModule);
    }

    [Test]
    public void Portability_ListOperations_CppAndCSharpAgree()
    {
        // The exact same BasicLang source must produce identical output on BOTH backends.
        // (Same program as Cpp_ListOperations_CompileAndRun.)
        const string source = @"
Sub Main()
    Dim l As New List(Of Integer)()
    l.Add(10)
    l.Add(20)
    l.Add(30)
    Dim total As Integer = 0
    For Each n In l
        total = total + n
    Next
    Console.WriteLine(total)
    Console.WriteLine(l.Count)
    Console.WriteLine(l(1))
End Sub";
        const string expected = "60\n3\n20\n";

        // C++ backend.
        var cpp = CompileToCpp(source, out var cppErrors);
        Assert.That(cppErrors, Is.Empty, string.Join("; ", cppErrors));
        Assert.That(CompileRun(cpp), Is.EqualTo(expected), "C++ backend output");

        // C# backend — real .NET List<int>/Console.WriteLine, run in-process via Roslyn.
        var cs = CompileToCSharp(source, out var csErrors);
        Assert.That(csErrors, Is.Empty, string.Join("; ", csErrors));
        // Sanity: the generated C# uses real .NET collection + Console APIs.
        Assert.That(cs, Does.Contain("List<int>").Or.Contain("List<System.Int32>"));
        Assert.That(cs, Does.Contain("Console.WriteLine"));
        var csOut = VisualGameStudio.Tests.Native.CSharpRun.CompileAndRun(cs);
        Assert.That(csOut, Is.EqualTo(expected), "C# backend output");
    }

    // ========================================================================
    // REFERENCE SEMANTICS: collections lower to std::shared_ptr, so they ALIAS
    // across ByVal params, `Dim b = a`, field stores, and lambda capture — exactly
    // like .NET reference types (and the C# backend). Before the shared_ptr rewrite
    // these all deep-COPIED (value wrappers), so the C++ output diverged from .NET.
    // Each test's expected value is what .NET / the C# backend produces.
    // ========================================================================

    [Test]
    public void Cpp_CollectionByValParam_MutationVisibleToCaller()
    {
        // .NET: a List passed ByVal is passed by REFERENCE (the reference is copied, the
        // object is shared) — AddItem's mutation is visible to the caller. Value wrappers
        // printed 2 here; reference semantics print 3 (matching .NET).
        const string source = @"
Sub AddItem(lst As List(Of Integer))
    lst.Add(99)
End Sub

Sub Main()
    Dim numbers As New List(Of Integer)()
    numbers.Add(1)
    numbers.Add(2)
    AddItem(numbers)
    Console.WriteLine(numbers.Count)
End Sub";
        var output = CompileToCpp(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        // The param is a shared_ptr and the mutation uses ->.
        Assert.That(output, Does.Contain("std::shared_ptr<BasicLang::List<int32_t>> lst"));
        Assert.That(CompileRun(output), Is.EqualTo("3\n"));
    }

    [Test]
    public void Cpp_CollectionAssignment_Aliases()
    {
        // .NET: `Dim b = a` copies the REFERENCE — b and a are the same list, so b.Add is
        // visible through a. Value wrappers printed 1; reference semantics print 2.
        const string source = @"
Sub Main()
    Dim a As New List(Of Integer)()
    a.Add(1)
    Dim b As List(Of Integer) = a
    b.Add(2)
    Console.WriteLine(a.Count)
End Sub";
        var output = CompileToCpp(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(CompileRun(output), Is.EqualTo("2\n"));
    }

    [Test]
    public void Cpp_CollectionFieldStorage_Aliases()
    {
        // .NET: storing a list into a field stores the REFERENCE — mutating the original
        // through its own variable is visible via the field. Value wrappers printed 0
        // (the field held an independent copy taken at assignment); reference semantics print 2.
        const string source = @"
Class Bag
    Public Items As List(Of Integer)
End Class

Sub Main()
    Dim s As New List(Of Integer)()
    Dim bg As New Bag()
    bg.Items = s
    s.Add(1)
    s.Add(2)
    Console.WriteLine(bg.Items.Count)
End Sub";
        var output = CompileToCpp(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(CompileRun(output), Is.EqualTo("2\n"));
    }

    [Test]
    public void Cpp_CollectionLambdaCapture_CompilesAndMutates()
    {
        // A `[=]` lambda that captures a VALUE-type collection captures it CONST — mutating it
        // inside is MSVC C2662 (and a void-returning Action invocation emitted `void* t = a();`,
        // C2440). As a shared_ptr, `[=]` copies the pointer (shared object) and the mutation
        // COMPILES and is visible after invocation. .NET prints 2.
        const string source = @"
Sub Main()
    Dim numbers As New List(Of Integer)()
    numbers.Add(1)
    Dim adder As Action = Sub() numbers.Add(2)
    adder()
    Console.WriteLine(numbers.Count)
End Sub";
        var output = CompileToCpp(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        // The void Action invocation must NOT be assigned to a void* temp.
        Assert.That(output, Does.Not.Contain("= adder();"));
        Assert.That(output, Does.Contain("adder();"));
        Assert.That(CompileRun(output), Is.EqualTo("2\n"));
    }

    [Test]
    public void Portability_CollectionSharing_CppAndCSharpAgree()
    {
        // The KEY reference-semantics proof (spec decision 12): one aliasing scenario run on
        // BOTH backends must produce IDENTICAL output. A ByVal-param mutation plus an alias
        // assignment; .NET reference semantics give 4 (2 initial + 1 from AddTwice via the
        // shared reference + 1 via the alias b). If C++ still used value wrappers it would
        // disagree with C#.
        const string source = @"
Sub AddTwice(lst As List(Of Integer))
    lst.Add(3)
End Sub

Sub Main()
    Dim a As New List(Of Integer)()
    a.Add(1)
    a.Add(2)
    AddTwice(a)
    Dim b As List(Of Integer) = a
    b.Add(4)
    Console.WriteLine(a.Count)
End Sub";
        const string expected = "4\n";

        // C++ backend (shared_ptr reference semantics).
        var cpp = CompileToCpp(source, out var cppErrors);
        Assert.That(cppErrors, Is.Empty, string.Join("; ", cppErrors));
        Assert.That(CompileRun(cpp), Is.EqualTo(expected), "C++ backend output");

        // C# backend — real .NET List<int> (a genuine reference type), run via Roslyn.
        var cs = CompileToCSharp(source, out var csErrors);
        Assert.That(csErrors, Is.Empty, string.Join("; ", csErrors));
        var csOut = VisualGameStudio.Tests.Native.CSharpRun.CompileAndRun(cs);
        Assert.That(csOut, Is.EqualTo(expected), "C# backend output");
    }

    [Test]
    public void Cpp_DictionaryMissingKeyRead_Throws()
    {
        // .NET-faithful: reading an absent key throws (KeyNotFoundException in .NET;
        // std::runtime_error from the C++ wrapper's Dictionary.Get). We prove the throw is
        // real AND catchable by wrapping the read in a BasicLang Try/Catch: the catch branch
        // runs (printing CAUGHT) and the program exits cleanly — if Get did NOT throw, the
        // Try body would print a value and the output would differ.
        var source = @"
Sub Main()
    Dim d As New Dictionary(Of String, Integer)()
    d.Add(""a"", 1)
    Try
        Dim x As Integer = d(""absent"")
        Console.WriteLine(x)
    Catch ex As Exception
        Console.WriteLine(""CAUGHT"")
    End Try
End Sub";
        var output = CompileToCpp(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("->Get("));  // read lowers to throwing ->Get(k) (shared_ptr)
        Assert.That(CompileRun(output), Is.EqualTo("CAUGHT\n"));
    }

    // ========================================================================
    // FILE-SCOPE (module global) coverage: a collection declared at module scope
    // lands in IRModule.GlobalVariables — a position ModuleUsesCollections used to
    // miss, so the wrapper preamble was omitted and the emitted global referenced
    // an undefined type (broken C++).
    // ========================================================================

    [Test]
    public void Cpp_ModuleGlobalCollection_EmitsWrapperPreamble()
    {
        // The List type appears ONLY on a module-level global (no function local
        // carries it) — the preamble must still be emitted.
        var source = @"
Dim g As List(Of Integer)
Sub Main()
End Sub";
        var output = CompileToCpp(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("BasicLang::List<int32_t>"));
        Assert.That(output, Does.Contain("class List"),
            "a file-scope collection global must still trigger the wrapper preamble");
    }

    [Test]
    public void Cpp_ModuleGlobalCollection_CompilesAndRuns()
    {
        // End-to-end: a module-global List is INITIALIZED (New), populated, and read inside
        // Main. Under reference semantics the global is a std::shared_ptr defaulting to null
        // (matching .NET, where an uninitialized module field is Nothing) — so it must be
        // assigned before use, exactly like a real .NET program; calling a method on the
        // unassigned null would be a NullReferenceException on both backends.
        // Before the ModuleUsesCollections globals fix this produced C++ that referenced
        // BasicLang::List with no preamble and failed to compile.
        var source = @"
Dim g As List(Of Integer)
Sub Main()
    g = New List(Of Integer)()
    g.Add(10)
    g.Add(20)
    Console.WriteLine(g.Count)
End Sub";
        var output = CompileToCpp(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("class List"));
        Assert.That(CompileRun(output), Is.EqualTo("2\n"));
    }

    [Test]
    public void Cpp_ModuleGlobalUnmappedType_StillRejected()
    {
        // A file-scope `Dim g As DateTime` global (unmapped .NET type) must still
        // trip the C++ capability checker — the globals position was previously
        // unchecked and would have bypassed the rejection.
        var source = @"
Dim g As DateTime
Sub Main()
End Sub";
        Assert.Throws<CppCapabilityException>(() => CompileToCpp(source, out _));
    }

    [Test]
    public void Cpp_InterfaceMethodUnmappedReturnType_StillRejected()
    {
        // A pure interface method signature carries no impl body, so nothing lowers
        // into module.Functions — the capability checker must walk interface method
        // signatures directly, or a `Function Foo() As DateTime` on an interface
        // degrades to a raw C++ compiler error instead of a clean BasicLang diagnostic.
        var source = @"
Interface IThing
    Function Foo() As DateTime
End Interface
Sub Main()
End Sub";
        Assert.Throws<CppCapabilityException>(() => CompileToCpp(source, out _));
    }

    [Test]
    public void Cpp_DecimalLocal_StillRejected()
    {
        // Decimal is NOT mapped by CppTypeMapper — it must be rejected cleanly. If it
        // were (wrongly) in MappedTypeNames it would pass the check and then MapType
        // would emit a bare, UNDEFINED C++ type `Decimal` (silent miscompile).
        var source = @"
Sub Main()
    Dim x As Decimal
End Sub";
        Assert.Throws<CppCapabilityException>(() => CompileToCpp(source, out _));
    }

    [Test]
    public void Cpp_InterfaceMethodDecimalReturnType_StillRejected()
    {
        // Same, in an interface signature position (M1 context): Decimal has no C++
        // mapping, so a `Function Foo() As Decimal` on an interface must reject cleanly.
        var source = @"
Interface IMoney
    Function Balance() As Decimal
End Interface
Sub Main()
End Sub";
        Assert.Throws<CppCapabilityException>(() => CompileToCpp(source, out _));
    }
}
