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
[Category("Integration")]
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
    public void Cpp_ListOfManagedOwnedType_MapsToListOfNetRef()
    {
        // P2a-2 THE FLIP (D-P7): Regex is ManagedOwned now, and a ManagedOwned generic
        // argument composes through MapType with zero extra mapping code — the element
        // is the always-emitted BasicLang::NetRef handle. (Pre-flip pin: rejected as an
        // unmapped .NET type.) An Object element would still reject — Object stays
        // permanently Rejected.
        var source = @"
Sub Main()
    Dim d As New List(Of Regex)()
End Sub";
        var output = CompileToCpp(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("std::shared_ptr<BasicLang::List<BasicLang::NetRef>>"),
            "the declaration must compose: List wrapper (reference semantics) over the NetRef handle");
        Assert.That(output, Does.Contain("std::make_shared<BasicLang::List<BasicLang::NetRef>>"));
        Assert.That(output, Does.Contain("class NetRef"),
            "the always-emitted runtime must carry the NetRef definition (D-P7)");
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

    // ========================================================================
    // VB-STYLE ARRAY LITERALS (cherry-picked from the array-literal fix, 2269540):
    // `Dim arr() As Integer = {10, 20, 30}` — the element type must route through the
    // type mapper (the BasicLang name `Integer` must NOT leak into C++), and the array
    // must be assignable so the literal actually reaches `arr`. Fix lives in the C++ backend.
    // ========================================================================

    [Test]
    public void Cpp_ArrayLiteralLocal_ElementTypeMapped_NotLeaked()
    {
        var source = @"
Sub Main()
    Dim arr() As Integer = {10, 20, 30}
End Sub";
        var output = CompileToCpp(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("int32_t"));
        // Scan with the always-spliced P1 BCL runtime stripped: a body comment mentions
        // "Append(Integer)", which would false-trip this user-code leak check (Task 9).
        Assert.That(CppGeneratedCode.WithoutBclRuntime(output), Does.Not.Contain("Integer"));
    }

    [Test]
    public void Cpp_ArrayLiteralForEach_CompileAndRun()
    {
        var source = @"
Sub Main()
    Dim arr() As Integer = {10, 20, 30}
    Dim total As Integer = 0
    For Each v In arr
        total = total + v
    Next
    Console.WriteLine(total)
End Sub";
        var output = CompileToCpp(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(CompileRun(output), Is.EqualTo("60\n"));
    }

    // ========================================================================
    // REGRESSION: control flow INSIDE a For Each / Try body (through the OPTIMIZER).
    //
    // Root cause (CppCodeGenerator): Visit(IRForEach)/Visit(IRTryCatch) emitted a native
    // C++ range-for / try{}-catch{} but inlined ONLY the single body BLOCK. When the body
    // held control flow (an If, a nested loop, a Try), the CFG split it into SEPARATE blocks
    // (if0.then/if0.end/...) that were NOT part of BodyBlock/TryBlock — the top-level block
    // walker then emitted them at FUNCTION scope, landing after the function `return` as
    // unreachable dead code. The program compiled clean but produced WRONG output:
    //   - If inside a For Each: the then-branch never ran (`for(x:...){ t=x>3; }` computed
    //     the condition temp but never branched); the counter stayed 0.
    //   - conditional Throw inside a Try: the throw was orphaned after `return`, so it never
    //     executed and the catch never fired.
    // Fix: emit the WHOLE body sub-region (BodyBlock/TryBlock + every block it reaches, up to
    // the construct's EndBlock) INSIDE the braces using the flat goto/label model, marking the
    // region consumed. A For Each end-of-iteration branch to EndBlock becomes `continue;`; a Try
    // body's becomes a forward `goto try_end;`. These MUST run through CompileToCppOptimized (the
    // CLI path) — the block-drop only manifests once the optimizer's CFG pass has run.
    // ========================================================================

    [Test]
    public void Cpp_If_Inside_ForEach_Branches_CompileAndRun()
    {
        // Manifestation A: an If inside a For Each body. Correct .NET output is 2 (two of
        // {1,5,10} exceed 3). The bug orphaned the then-block after `return` and printed 0.
        var source = @"
Sub Main()
    Dim l As New List(Of Integer)()
    l.Add(1)
    l.Add(5)
    l.Add(10)
    Dim big As Integer = 0
    For Each x In l
        If x > 3 Then
            big = big + 1
        End If
    Next
    Console.WriteLine(big)
End Sub";
        var output = CompileToCppOptimized(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(CompileRun(output), Is.EqualTo("2\n"),
            "an If inside a For Each body must branch (not be stranded after the return)");
    }

    [Test]
    public void Cpp_If_Throw_Inside_Try_Catches_CompileAndRun()
    {
        // Manifestation B: a conditional Throw (an If) inside a Try body. Correct .NET output
        // is 'caught'. The bug orphaned the throw after `return`, so it never ran and the catch
        // never fired (nothing printed).
        var source = @"
Sub Main()
    Try
        Dim d As Integer = 10
        If d > 5 Then
            Throw New Exception(""boom"")
        End If
        Console.WriteLine(""no throw"")
    Catch ex As Exception
        Console.WriteLine(""caught"")
    End Try
End Sub";
        var output = CompileToCppOptimized(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(CompileRun(output), Is.EqualTo("caught\n"),
            "a conditional Throw inside a Try body must reach the catch");
    }

    [Test]
    public void Cpp_NestedForEach_WithIf_CompileAndRun()
    {
        // Nested For Each, innermost body an If. outer{1,2} x inner{10,20}; pairs summing >15
        // are (1,20) and (2,20) -> 2. Proves inner loop + If compose inside the outer range-for.
        var source = @"
Sub Main()
    Dim outer As New List(Of Integer)()
    outer.Add(1)
    outer.Add(2)
    Dim inner As New List(Of Integer)()
    inner.Add(10)
    inner.Add(20)
    Dim total As Integer = 0
    For Each a In outer
        For Each b In inner
            If a + b > 15 Then
                total = total + 1
            End If
        Next
    Next
    Console.WriteLine(total)
End Sub";
        var output = CompileToCppOptimized(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(CompileRun(output), Is.EqualTo("2\n"));
    }

    [Test]
    public void Cpp_While_Inside_ForEach_CompileAndRun()
    {
        // A While loop inside a For Each body. For each n in {2,3}, count up to n: 2+3 = 5.
        var source = @"
Sub Main()
    Dim l As New List(Of Integer)()
    l.Add(2)
    l.Add(3)
    Dim total As Integer = 0
    For Each n In l
        Dim k As Integer = 0
        While k < n
            total = total + 1
            k = k + 1
        End While
    Next
    Console.WriteLine(total)
End Sub";
        var output = CompileToCppOptimized(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(CompileRun(output), Is.EqualTo("5\n"));
    }

    [Test]
    public void Cpp_IfThrow_InTry_InsideForEach_CompileAndRun()
    {
        // An If (conditional Throw) inside a Try inside a For Each. Only n=9 throws -> caught==1.
        var source = @"
Sub Main()
    Dim l As New List(Of Integer)()
    l.Add(1)
    l.Add(9)
    Dim caught As Integer = 0
    For Each n In l
        Try
            If n > 5 Then
                Throw New Exception(""big"")
            End If
        Catch ex As Exception
            caught = caught + 1
        End Try
    Next
    Console.WriteLine(caught)
End Sub";
        var output = CompileToCppOptimized(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(CompileRun(output), Is.EqualTo("1\n"));
    }

    [Test]
    public void Cpp_NestedForEach_InsideIf_InsideForEach_CompileAndRun()
    {
        // A nested For Each guarded by an If inside a For Each. Inner loop runs only when a=2,
        // summing inner {100,200} -> 300.
        var source = @"
Sub Main()
    Dim outer As New List(Of Integer)()
    outer.Add(1)
    outer.Add(2)
    Dim inner As New List(Of Integer)()
    inner.Add(100)
    inner.Add(200)
    Dim total As Integer = 0
    For Each a In outer
        If a = 2 Then
            For Each b In inner
                total = total + b
            Next
        End If
    Next
    Console.WriteLine(total)
End Sub";
        var output = CompileToCppOptimized(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(CompileRun(output), Is.EqualTo("300\n"));
    }

    [Test]
    public void Cpp_If_Inside_ForEach_Over_DictValues_CompileAndRun()
    {
        // The same If-in-body fix over a Dictionary's .Values (a List). Values {1,5}; one > 3.
        var source = @"
Sub Main()
    Dim d As New Dictionary(Of String, Integer)()
    d.Add(""a"", 1)
    d.Add(""b"", 5)
    Dim big As Integer = 0
    For Each v In d.Values
        If v > 3 Then
            big = big + 1
        End If
    Next
    Console.WriteLine(big)
End Sub";
        var output = CompileToCppOptimized(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(CompileRun(output), Is.EqualTo("1\n"));
    }

    [Test]
    public void Cpp_ForEach_OverDictionary_IsCleanError()
    {
        // Spec decision 7: direct `For Each ... In someDictionary` is a v1 non-goal (iterate
        // .Keys/.Values). BasicLang::Dictionary has no begin()/end(), so the old code emitted an
        // uncompilable `for(... : (*dict))` (cl C3312). It must be a CLEAN capability diagnostic.
        var source = @"
Sub Main()
    Dim d As New Dictionary(Of String, Integer)()
    d.Add(""a"", 1)
    For Each kv In d
        Console.WriteLine(kv)
    Next
End Sub";
        var ex = Assert.Throws<CppCapabilityException>(() => CompileToCppOptimized(source, out _));
        Assert.That(string.Join("\n", ex!.Diagnostics),
            Does.Contain("For Each over a Dictionary").And.Contains(".Keys or .Values"),
            "direct For Each over a Dictionary must be an actionable diagnostic, not broken C++");
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
    public void Cpp_ModuleGlobalManagedOwnedType_DeclaresNetRef()
    {
        // P2a-2 THE FLIP (D-P7): a file-scope `Dim g As Regex` global is a DECLARATION
        // position — it lowers to the null-slot-safe BasicLang::NetRef handle (handle 0,
        // no shim needed). (Pre-flip pin: the globals position rejected the unmapped
        // .NET type.)
        var source = @"
Dim g As Regex
Sub Main()
End Sub";
        var output = CompileToCpp(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("BasicLang::NetRef g"),
            "a module-global ManagedOwned declaration must be a NetRef:\n" + output);
    }

    [Test]
    public void Cpp_InterfaceMethodManagedOwnedReturnType_MapsToNetRef()
    {
        // P2a-2 THE FLIP (D-P7): a pure interface signature returning a ManagedOwned
        // type is a declaration position — the abstract method returns the NetRef
        // handle. (Pre-flip pin: the interface-signature walk rejected Regex; the WALK
        // itself is still load-bearing for genuinely unmapped types — see the
        // KeyValuePair/Nullable rejections in CppBackendTests.)
        var source = @"
Interface IThing
    Function Foo() As Regex
End Interface
Sub Main()
End Sub";
        var output = CompileToCpp(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("BasicLang::NetRef Foo"),
            "an interface method returning a ManagedOwned type must return NetRef:\n" + output);
    }

    [Test]
    public void Cpp_ManagedOwnedLocal_MapsToNetRef()
    {
        // P2a-2 THE FLIP (D-P7): Stream is ManagedOwned — a local declaration is a
        // NetRef handle. (Pre-flip pin: rejected. The anti-miscompile concern the old
        // pin carried — a bare, undefined C++ `Stream` leaking through — is now held by
        // the assertion that the emitted type is EXACTLY BasicLang::NetRef.)
        var source = @"
Sub Main()
    Dim x As Stream
End Sub";
        var output = CompileToCpp(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("BasicLang::NetRef x"),
            "a ManagedOwned local must declare as NetRef:\n" + output);
        Assert.That(output, Does.Not.Contain(" Stream x"),
            "the bare undefined C++ type name must never leak into the emission");
    }

    [Test]
    public void Cpp_InterfaceMethodManagedOwnedStreamReturn_MapsToNetRef()
    {
        // P2a-2 THE FLIP (D-P7): same as the Regex interface-return pin above, on the
        // second flipped name — Stream. (Pre-flip pin: rejected in signature position.)
        var source = @"
Interface IMoney
    Function Balance() As Stream
End Interface
Sub Main()
End Sub";
        var output = CompileToCpp(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("BasicLang::NetRef Balance"),
            "an interface method returning ManagedOwned Stream must return NetRef:\n" + output);
    }

    // ========================================================================
    // COLLECTION LOWERING BUG BATCH (call-vs-indexer, interface generics, indexer
    // typing, .Item, Remove bool, nested indexer, struct ==). Each reproduces a
    // distinct mis-lowering; where a value is produced, compile-and-run (real MSVC)
    // and compare to known .NET behavior.
    // ========================================================================

    // ---- BUG 1: a function whose RETURN type is a collection must lower its CALLS
    //             as calls `f(args)`, never as indexer access `f[args]`. ----

    [Test]
    public void Cpp_CollectionReturningFunctionCall_IsCallNotIndexer_CompileAndRun()
    {
        // MakeList(3).Count used to lower to MakeList[3].Count (indexing the FUNCTION).
        // Fixed: a callable target is always a call regardless of its return type.
        var source = @"
Function MakeList(n As Integer) As List(Of Integer)
    Dim l As New List(Of Integer)()
    l.Add(n)
    l.Add(n)
    Return l
End Function
Sub Main()
    Console.WriteLine(MakeList(3).Count)
End Sub";
        var output = CompileToCpp(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("MakeList(3)"));
        Assert.That(output, Does.Not.Contain("MakeList[3]"));
        Assert.That(CompileRun(output), Is.EqualTo("2\n"));
    }

    [Test]
    public void CSharp_CollectionReturningFunctionCall_IsCallNotIndexer_CompileAndRun()
    {
        // Same program on the C# backend (Roslyn): MakeList(3).Count must be a call.
        // Before the fix the generated C# said MakeList[3].Count (CS0021).
        var source = @"
Function MakeList(n As Integer) As List(Of Integer)
    Dim l As New List(Of Integer)()
    l.Add(n)
    l.Add(n)
    Return l
End Function
Sub Main()
    Console.WriteLine(MakeList(3).Count)
End Sub";
        var cs = CompileToCSharp(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(cs, Does.Contain("MakeList(3)"));
        Assert.That(cs, Does.Not.Contain("MakeList[3]"));
        Assert.That(VisualGameStudio.Tests.Native.CSharpRun.CompileAndRun(cs), Is.EqualTo("2\n"));
    }

    [Test]
    public void CSharp_DictionaryReturningFunctionCall_IsCallNotIndexer_CompileAndRun()
    {
        // Dictionary-returning function: MakeMap().Count must be a call, not MakeMap[].Count.
        var source = @"
Function MakeMap() As Dictionary(Of String, Integer)
    Dim d As New Dictionary(Of String, Integer)()
    d(""a"") = 1
    Return d
End Function
Sub Main()
    Console.WriteLine(MakeMap().Count)
End Sub";
        var cs = CompileToCSharp(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(VisualGameStudio.Tests.Native.CSharpRun.CompileAndRun(cs), Is.EqualTo("1\n"));
    }

    [Test]
    public void Cpp_NormalFunctionCallAndArrayIndex_StillWork_Regression()
    {
        // Guard against over-reach: an ordinary Integer-returning function call and a plain
        // VB array index `arr(i)` must be UNAFFECTED by the call-vs-indexer guard.
        var source = @"
Function Doubler(n As Integer) As Integer
    Return n * 2
End Function
Sub Main()
    Dim a As New List(Of Integer)()
    a.Add(7)
    a.Add(8)
    Console.WriteLine(Doubler(5))
    Console.WriteLine(a(1))
End Sub";
        var output = CompileToCpp(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(CompileRun(output), Is.EqualTo("10\n8\n"));
    }

    // ---- BUG 2: interface method signatures must keep collection generic type args. ----

    [Test]
    public void CSharp_InterfaceCollectionSignatures_KeepGenericArgs_CompilesWithRoslyn()
    {
        // The interface used to emit `List GetItems();` / `void SetMap(Dictionary m);` (generic
        // args dropped), which no longer matched the implementing class -> CS0535/CS0305.
        var source = @"
Interface IStore
    Function GetItems() As List(Of String)
    Sub SetMap(m As Dictionary(Of String, Integer))
End Interface

Class Store
    Implements IStore
    Public Function GetItems() As List(Of String) Implements IStore.GetItems
        Return New List(Of String)()
    End Function
    Public Sub SetMap(m As Dictionary(Of String, Integer)) Implements IStore.SetMap
    End Sub
End Class

Sub Main()
End Sub";
        var cs = CompileToCSharp(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(cs, Does.Contain("List<string> GetItems();"));
        Assert.That(cs, Does.Contain("void SetMap(Dictionary<string, int> m);"));
        // Must compile with Roslyn (signature now matches the implementing class).
        Assert.DoesNotThrow(() => VisualGameStudio.Tests.Native.CSharpRun.CompileAndRun(cs));
    }

    [Test]
    public void Cpp_InterfaceCollectionSignatures_KeepGenericArgs()
    {
        // C++ backend: interface pure-virtual signatures must carry the full wrapper generic
        // types (never the bare `List` / `Dictionary`).
        var source = @"
Interface IStore
    Function GetItems() As List(Of String)
    Sub SetMap(m As Dictionary(Of String, Integer))
End Interface
Sub Main()
End Sub";
        var output = CompileToCpp(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("std::shared_ptr<BasicLang::List<std::string>> GetItems()"));
        Assert.That(output, Does.Contain("std::shared_ptr<BasicLang::Dictionary<std::string, int32_t>>"));
    }

    // ---- BUG 3: an indexer READ of a collection element types as the element type. ----

    [Test]
    public void Cpp_IndexerRead_TypesAsElementType_CompileAndRun()
    {
        // `Dim x As Integer = l(0)` / `Dim v As String = d("k")` must type as the element/value
        // type (not Object). Before the fix the result was Object -> void* in C++ / CS0266 in C#.
        var source = @"
Sub Main()
    Dim l As New List(Of Integer)()
    l.Add(5)
    Dim x As Integer = l(0)
    Console.WriteLine(x)
    Dim d As New Dictionary(Of String, String)()
    d(""k"") = ""hi""
    Dim v As String = d(""k"")
    Console.WriteLine(v)
End Sub";
        var output = CompileToCpp(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(CompileRun(output), Is.EqualTo("5\nhi\n"));
    }

    // ---- BUG 4: `.Item(i)` READ/WRITE lowers to the indexer, not `->item(...)`. ----

    [Test]
    public void Cpp_ItemAccessorReadAndWrite_LowersToIndexer_CompileAndRun()
    {
        // `l.Item(0)` READ and `l.Item(1) = v` WRITE are the explicit VB indexer; they used to
        // emit a nonexistent `l->Item(...)` member. Now they mirror `l(i)` / `l(i) = v`.
        var source = @"
Sub Main()
    Dim l As New List(Of Integer)()
    l.Add(1)
    l.Add(2)
    Console.WriteLine(l.Item(0))
    l.Item(1) = 99
    Console.WriteLine(l.Item(1))
End Sub";
        var output = CompileToCpp(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Not.Contain("->Item("));
        Assert.That(output, Does.Contain("(*l)[0]"));
        Assert.That(output, Does.Contain("(*l)[1] = 99"));
        Assert.That(CompileRun(output), Is.EqualTo("1\n99\n"));
    }

    // ---- BUG 5: List.Remove / HashSet.Remove return Boolean (not Void). ----

    [Test]
    public void Cpp_ListRemove_ReturnsBoolean_CompileAndRun()
    {
        // .NET List<T>.Remove returns whether an element was removed. `Dim ok As Boolean = ...`
        // used to error (Remove typed Void). The wrapper's List::Remove now returns bool too.
        var source = @"
Sub Main()
    Dim l As New List(Of Integer)()
    l.Add(5)
    l.Add(6)
    Dim ok As Boolean = l.Remove(5)
    Dim gone As Boolean = l.Remove(42)
    Console.WriteLine(ok)
    Console.WriteLine(gone)
    Console.WriteLine(l.Count)
End Sub";
        var output = CompileToCpp(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        // Remove(5)=true(1), Remove(42)=false(0), Count=1.
        Assert.That(CompileRun(output), Is.EqualTo("1\n0\n1\n"));
    }

    [Test]
    public void Cpp_HashSetRemove_ReturnsBoolean_CompileAndRun()
    {
        var source = @"
Sub Main()
    Dim s As New HashSet(Of Integer)()
    s.Add(5)
    Dim ok As Boolean = s.Remove(5)
    Dim gone As Boolean = s.Remove(5)
    Console.WriteLine(ok)
    Console.WriteLine(gone)
End Sub";
        var output = CompileToCpp(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(CompileRun(output), Is.EqualTo("1\n0\n"));
    }

    // ---- BUG 6: chained/nested indexer `a(i)(j)` on nested collections. ----

    [Test]
    public void Cpp_NestedIndexerRead_ListOfList_CompileAndRun()
    {
        // `m(0)(1)` used to reference an undeclared temp (t5(1)); the outer indexer over the
        // inner collection value was never wired. Now it reads (*(*m)[0])[1].
        var source = @"
Sub Main()
    Dim m As New List(Of List(Of Integer))()
    Dim inner As New List(Of Integer)()
    inner.Add(10)
    inner.Add(20)
    m.Add(inner)
    Console.WriteLine(m(0)(1))
End Sub";
        var output = CompileToCpp(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(CompileRun(output), Is.EqualTo("20\n"));
    }

    [Test]
    public void Cpp_NestedIndexerRead_DictOfDict_CompileAndRun()
    {
        // Nested-dictionary chained read `d("a")("b")`.
        var source = @"
Sub Main()
    Dim d As New Dictionary(Of String, Dictionary(Of String, Integer))()
    Dim inner As New Dictionary(Of String, Integer)()
    inner(""b"") = 7
    d(""a"") = inner
    Console.WriteLine(d(""a"")(""b""))
End Sub";
        var output = CompileToCpp(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(CompileRun(output), Is.EqualTo("7\n"));
    }

    // ---- BUG 7: List(Of SomeStructure) needs value-equality operator== on the struct. ----

    [Test]
    public void Cpp_ListOfStruct_Contains_UsesValueEquality_CompileAndRun()
    {
        // List(Of Point).Contains/IndexOf call std::find, needing `item == *it`. Generated
        // structs had no operator== (C2676). A C++20 defaulted operator== gives memberwise
        // equality matching .NET ValueType.Equals.
        var source = @"
Structure Point
    Public X As Integer
    Public Y As Integer
End Structure

Sub Main()
    Dim pts As New List(Of Point)()
    Dim p As Point
    p.X = 1
    p.Y = 2
    pts.Add(p)
    Dim q As Point
    q.X = 1
    q.Y = 2
    Dim other As Point
    other.X = 9
    other.Y = 9
    Console.WriteLine(pts.Contains(q))
    Console.WriteLine(pts.Contains(other))
    Console.WriteLine(pts.IndexOf(q))
End Sub";
        var output = CompileToCpp(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("bool operator==(const Point& other) const = default;"));
        // Contains(q)=true(1), Contains(other)=false(0), IndexOf(q)=0.
        Assert.That(CompileRun(output), Is.EqualTo("1\n0\n0\n"));
    }

    // ========================================================================
    // BUG 7 — PLAIN FIXED-SIZE ARRAYS (chip task_c8c3eac9). Two independent defects,
    // both on the most ordinary shape in the language (`Dim a(3) As Integer`):
    //
    //   7a  DECLARATION NEVER ALLOCATED. The C++ local emitter answered
    //       GetDefaultValue -> `{}` for any array, so `std::vector<int32_t> a = {};`
    //       default-constructed EMPTY while the C# backend emitted `new int[3]`.
    //       Every index was then out of bounds.
    //
    //   7b  INDEXING EMITTED NON-COMPILING C++. IRBuilder builds an IRGetElementPtr
    //       carrying the ELEMENT type, and the backend materialized it as
    //       `t = &a[0];` — an `int32_t*` assigned to an `int32_t`
    //       ("invalid conversion from 'int*' to 'int32_t'"). It fired on EVERY
    //       indexed read and write; the write path then ignored the dead temp and
    //       re-derived `a[0] = v` itself, so only reads showed the second half
    //       (`t4 = *t3;`, dereferencing a pointer never materialized).
    //
    // Why it survived: `For Each` never builds a GEP (it iterates the vector
    // directly), and the shipped samples only write through arrays. Surfacing it
    // needs a plain indexed READ.
    //
    // These run through CompileToCppOptimized — the CLI/IDE path — per the repo rule
    // that codegen is validated through the optimizer, not only the bare helper.
    // ========================================================================

    [Test]
    public void Cpp_FixedSizeArray_AllocatesAndIndexes_CompileAndRun()
    {
        // Writes the LAST valid slot and reads every slot back. If the vector were still
        // default-constructed empty this is out-of-bounds rather than 60.
        var source = @"
Sub Main()
    Dim a(3) As Integer
    a(0) = 10
    a(1) = 20
    a(2) = 30
    Console.WriteLine(a(0) + a(1) + a(2))
    Console.WriteLine(a.Length)
End Sub";
        var output = CompileToCppOptimized(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        // 7a: the declaration carries the size (was `= {}`).
        Assert.That(output, Does.Contain("std::vector<int32_t>(3)"));
        // 7b: no address of an element is ever taken or dereferenced.
        Assert.That(output, Does.Not.Contain("&a["));
        Assert.That(CompileRun(output), Is.EqualTo("60\n3\n"));
    }

    [Test]
    public void Cpp_FixedSizeArray_IndexInsideIfAndLoop_CompileAndRun()
    {
        // The backend flattens control flow to labels + goto in ONE function scope, so an
        // indexed read inside a branch or loop body is the shape most likely to break if
        // element access ever reintroduces a block-scoped declaration.
        var source = @"
Sub Main()
    Dim a(3) As Integer
    a(0) = 10
    a(1) = 20
    a(2) = 30
    Dim total As Integer = 0
    For i As Integer = 0 To 2
        total = total + a(i)
    Next
    Console.WriteLine(total)
    If a(2) > a(0) Then
        Console.WriteLine(a(2) - a(0))
    End If
End Sub";
        var output = CompileToCppOptimized(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(CompileRun(output), Is.EqualTo("60\n20\n"));
    }

    [Test]
    public void Cpp_FixedSizeArray_BracketSyntax_LowersIdenticallyToParen()
    {
        // `Dim a[3]` and `Dim a(3)` are two spellings of ONE grammar (Parser: "support both
        // [] (C#-style) and () (VB-style) syntax"), so they must not drift into two lowerings.
        const string body = @"
    a(0) = 10
    a(1) = 20
    Console.WriteLine(a(0) + a(1))
End Sub";
        var paren = CompileToCppOptimized("\nSub Main()\n    Dim a(3) As Integer" + body, out var e1);
        var bracket = CompileToCppOptimized("\nSub Main()\n    Dim a[3] As Integer" + body, out var e2);
        Assert.That(e1, Is.Empty, string.Join("; ", e1));
        Assert.That(e2, Is.Empty, string.Join("; ", e2));
        Assert.That(bracket, Is.EqualTo(paren));
        Assert.That(CompileRun(paren), Is.EqualTo("30\n"));
    }

    /// <summary>
    /// A MODULE-LEVEL fixed-size array allocates too. Globals are emitted by a different site
    /// than locals, and the first cut of this fix touched only the locals loop — which, because
    /// the indexing half had just removed the compile error that masked it, turned this program
    /// from "does not build" into "builds and access-violates". Found by adversarial review.
    /// </summary>
    [Test]
    public void Cpp_ModuleLevelFixedSizeArray_Allocates_CompileAndRun()
    {
        var source = @"
Module Program
    Dim g(4) As Integer
    Sub Main()
        g(0) = 3
        g(3) = 4
        Console.WriteLine(g(0) + g(3))
    End Sub
End Module";
        var output = CompileToCppOptimized(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("std::vector<int32_t>(4)"),
            "a module-level array must carry its size like a local one:\n" + output);
        // Writing index 3 of a 4-element global: an unsized global corrupts the heap here.
        Assert.That(CompileRun(output), Is.EqualTo("7\n"));
    }

    /// <summary>
    /// Writing a FIELD of a struct array element must reach the array, not a copy.
    /// <c>pts(0).X = 5</c> lowers through an IRLoad that materializes <c>t1 = pts[0];</c>, so
    /// writing <c>t1.X</c> lost the store silently — C++ printed 0 where C# printed 5. A wrong
    /// answer is worse than the compile error it replaced, so this is pinned by RUNNING it and
    /// by cross-checking the value the C# backend produces for the same program.
    /// </summary>
    [Test]
    public void Cpp_StructArrayElement_FieldWrite_ReachesTheArray_CompileAndRun()
    {
        var source = @"
Structure Point
    Public X As Integer
End Structure

Sub Main()
    Dim pts(3) As Point
    pts(0).X = 5
    pts(1).X = 9
    Console.WriteLine(pts(0).X + pts(1).X)
End Sub";
        var output = CompileToCppOptimized(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("pts[0].X = 5"),
            "the write must target the element lvalue, not a copied temporary:\n" + output);
        Assert.That(CompileRun(output), Is.EqualTo("14\n"));
    }

    [Test]
    public void Cpp_FixedSizeArray_OfString_CompileAndRun()
    {
        // A non-primitive element type takes the same sized-construction path.
        var source = @"
Sub Main()
    Dim s(2) As String
    s(0) = ""ay""
    s(1) = ""bee""
    Console.WriteLine(s(0) & ""-"" & s(1))
End Sub";
        var output = CompileToCppOptimized(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(CompileRun(output), Is.EqualTo("ay-bee\n"));
    }

    // ========================================================================
    // BUG 7c — A SIZE THAT IS NOT AN INTEGER LITERAL (the hole 8be3fe5 documented).
    //
    // The parser recorded a dimension only when it was already an integer literal and
    // collapsed everything else to a -1 "dynamic size" sentinel, which reached
    // TypeInfo.ArraySize as "unsized". Both backends then emitted an EMPTY array for a
    // program that immediately indexed it, and BOTH compiled clean:
    //
    //   Const N As Integer = 5 : Dim a(N) As Integer : a(0) = 1
    //     C++  -> std::vector<int32_t> a = {};  then a[0] = 1   (ACCESS_VIOLATION)
    //     C#   -> int[] a = default!;           then a[0] = 1   (NullReferenceException)
    //
    // That is silent memory corruption, not a missing feature, and it shipped: the
    // SpaceShooter sample declares `Dim bulletX(MAX_BULLETS) As Single`.
    //
    // The fix is in the FRONT END — dimensions are carried to the analyzer as EXPRESSIONS
    // and folded (SemanticAnalyzer.TryFoldConstantInt) where constants are known — so both
    // backends are fixed by one change and cannot drift. A size that genuinely will not
    // fold is REFUSED (there is no run-time-sizing fallback: ReDim is not implemented), and
    // the refusal tests below have anti-vacuity partners so they cannot pass by refusing
    // everything.
    // ========================================================================

    [Test]
    public void Cpp_ConstSizedArray_Allocates_CompileAndRun()
    {
        // Writes the LAST valid slot: an unsized vector corrupts the heap here rather
        // than printing 5.
        var source = @"
Const N As Integer = 5

Sub Main()
    Dim a(N) As Integer
    a(4) = 40
    Console.WriteLine(a.Length)
    Console.WriteLine(a(4))
End Sub";
        var output = CompileToCppOptimized(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("std::vector<int32_t>(5)"),
            "a Const-sized array must carry its size like a literal-sized one:\n" + output);
        Assert.That(CompileRun(output), Is.EqualTo("5\n40\n"));
    }

    [Test]
    public void Cpp_ConstExpressionSizedArray_Allocates_CompileAndRun()
    {
        // Arithmetic over constants folds too — `K * 2` is as compile-time-known as `6`.
        var source = @"
Const K As Integer = 3

Sub Main()
    Dim b(K * 2) As Integer
    b(5) = 7
    Console.WriteLine(b.Length)
    Console.WriteLine(b(5))
End Sub";
        var output = CompileToCppOptimized(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("std::vector<int32_t>(6)"), output);
        Assert.That(CompileRun(output), Is.EqualTo("6\n7\n"));
    }

    [Test]
    public void Cpp_ModuleLevelConstSizedArray_Allocates_CompileAndRun()
    {
        // Globals are emitted by a different site than locals — the exact split that made
        // the literal-size fix incomplete the first time. Pinned separately on purpose.
        var source = @"
Const K As Integer = 3

Module Program
    Dim g(K) As Integer
    Sub Main()
        g(2) = 11
        Console.WriteLine(g.Length)
        Console.WriteLine(g(2))
    End Sub
End Module";
        var output = CompileToCppOptimized(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("std::vector<int32_t>(3)"), output);
        Assert.That(CompileRun(output), Is.EqualTo("3\n11\n"));
    }

    [Test]
    public void ArraySize_ThatIsNotCompileTimeConstant_IsRefused()
    {
        // A local variable is not a constant. Refusing is the whole point: this shape used
        // to compile to an empty array and then corrupt memory on the first write.
        var source = @"
Sub Main()
    Dim n As Integer = 5
    Dim a(n) As Integer
    a(0) = 1
End Sub";
        CompileToCppOptimized(source, out var errors);
        Assert.That(errors, Is.Not.Empty, "a run-time array size must not compile silently");
        Assert.That(string.Join("; ", errors), Does.Contain("compile-time constant"));
    }

    [Test]
    public void ArraySize_ThatIsNegative_IsRefused()
    {
        var source = @"
Sub Main()
    Dim a(-1) As Integer
End Sub";
        CompileToCppOptimized(source, out var errors);
        Assert.That(errors, Is.Not.Empty);
        Assert.That(string.Join("; ", errors), Does.Contain("cannot be negative"));
    }

    /// <summary>
    /// ANTI-VACUITY PARTNER for the two refusals above: an EMPTY dimension is not a missing
    /// size to complain about, it is a deliberately unsized array declaration
    /// (<c>Dim a() As Integer</c>). If the refusal ever widened to cover this, every program
    /// that declares an array without sizing it would stop compiling.
    /// </summary>
    [Test]
    public void UnsizedArrayDeclaration_IsStillAccepted()
    {
        var source = @"
Sub Main()
    Dim a() As Integer
    Console.WriteLine(1)
End Sub";
        var output = CompileToCppOptimized(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Not.Contain("std::vector<int32_t>(0)"),
            "an unsized declaration must not be given a bogus zero-element allocation:\n" + output);
        Assert.That(CompileRun(output), Is.EqualTo("1\n"));
    }

    // ========================================================================
    // BUG 7d — ARRAY FIELDS WERE NOT TYPED AS ARRAYS.
    //
    //   Class Board
    //       Public Cells(9) As Integer
    //   End Class
    //
    // emitted `int32_t Cells;` — a SCALAR — so `b.Cells(0)` was
    // "invalid types 'int32_t {aka int}[int]' for array subscript".
    //
    // Root cause: the field branch of the parser recorded ArrayDimensions but never set
    // IsArray, and ResolveTypeReference only takes its array branch when IsArray is set.
    // Locals and parameters always set both; fields were the one path that set one half.
    // Because Kind was not Array, neither the sizing helper nor the element-lvalue path was
    // ever reached, so fixing the parser alone was not enough — the chain behind it had to
    // be followed:
    //
    //   * the C++ CLASS-MEMBER sites (private/protected/public, plus the out-of-class
    //     definition for statics) needed their own sizing, since a member cannot borrow the
    //     local or global declaration site;
    //   * `obj.Field(i)` lowered as an INSTANCE METHOD CALL — `b->Cells(0)`, calling a
    //     std::vector — because IRBuilder's array-index branch only covered a bare
    //     identifier callee;
    //   * `obj.Field(i) = v` wrote through the materialized field COPY (`t1[0] = 5`), which
    //     compiles and silently loses the store, exactly like the struct-array-element case
    //     above. ElementLValue re-derives the field so read and write agree by construction.
    // ========================================================================

    [Test]
    public void Cpp_ArrayField_IsTypedAsArrayAndSized_CompileAndRun()
    {
        var source = @"
Class Board
    Public Cells(9) As Integer
End Class

Module Program
    Sub Main()
        Dim b As New Board()
        b.Cells(0) = 5
        b.Cells(8) = 7
        Console.WriteLine(b.Cells(0) + b.Cells(8))
        Console.WriteLine(b.Cells.Length)
    End Sub
End Module";
        var output = CompileToCppOptimized(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("std::vector<int32_t> Cells = std::vector<int32_t>(9)"),
            "the field must be declared as a SIZED array, not a scalar:\n" + output);
        Assert.That(output, Does.Not.Contain("int32_t Cells;"),
            "the scalar declaration is the bug:\n" + output);
        // Writing index 8 of a 9-element field: an unsized member corrupts the heap here.
        Assert.That(CompileRun(output), Is.EqualTo("12\n9\n"));
    }

    /// <summary>
    /// The write must reach the OBJECT, not the copy the field read materialized. `b.Cells(0)`
    /// lowers through an IRFieldAccess that copies the whole std::vector, so `t1[0] = 5` both
    /// compiles and silently discards the store — a wrong answer, not a build failure. Pinned by
    /// the emitted lvalue AND by running it.
    /// </summary>
    [Test]
    public void Cpp_ArrayField_Write_ReachesTheObject_NotACopy()
    {
        var source = @"
Class Board
    Public Cells(4) As Integer
End Class

Module Program
    Sub Main()
        Dim b As New Board()
        b.Cells(2) = 42
        Console.WriteLine(b.Cells(2))
    End Sub
End Module";
        var output = CompileToCppOptimized(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("b->Cells[2] = 42"),
            "the store must target the field lvalue, not a copied temporary:\n" + output);
        Assert.That(CompileRun(output), Is.EqualTo("42\n"));
    }

    [Test]
    public void Cpp_PrivateArrayField_ConstSized_UsedFromMethods_CompileAndRun()
    {
        // A Const-sized PRIVATE field, read and written unqualified from inside the class:
        // a different emission site (private members) and a different receiver shape (implicit
        // this) from the public/external case above.
        var source = @"
Const SLOTS As Integer = 4

Class Board
    Private Scratch(SLOTS) As Integer

    Sub Seed()
        Scratch(3) = 99
    End Sub

    Function Peek() As Integer
        Return Scratch(3)
    End Function
End Class

Module Program
    Sub Main()
        Dim b As New Board()
        b.Seed()
        Console.WriteLine(b.Peek())
    End Sub
End Module";
        var output = CompileToCppOptimized(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("std::vector<int32_t> Scratch = std::vector<int32_t>(4)"), output);
        Assert.That(CompileRun(output), Is.EqualTo("99\n"));
    }

    /// <summary>
    /// A SIZED array member of a Structure is refused. A struct value is produced by
    /// default-initialization, and .NET's `default(T)` bypasses field initializers, so the
    /// storage would never exist however it was declared — C# says so outright (CS8983) and
    /// VB.NET refuses the same declaration. The C++ backend WOULD happily size a value
    /// std::vector member, so without this the two backends disagree silently.
    /// </summary>
    [Test]
    public void SizedArrayMemberOfStructure_IsRefused()
    {
        var source = @"
Structure Bag
    Public Items(2) As Integer
End Structure

Sub Main()
    Dim g As Bag
End Sub";
        CompileToCppOptimized(source, out var errors);
        Assert.That(errors, Is.Not.Empty);
        Assert.That(string.Join("; ", errors), Does.Contain("cannot declare an array size"));
    }

    /// <summary>
    /// ANTI-VACUITY PARTNER: an UNSIZED structure member is an ordinary reference field and
    /// must keep working — it is assigned real storage from outside. Also pins the fix that
    /// made a structure member's resolved type reach the IR at all (it used to be rebuilt from
    /// the bare type NAME, which discarded the array-ness).
    /// </summary>
    [Test]
    public void UnsizedArrayMemberOfStructure_CompilesAndRuns()
    {
        var source = @"
Structure Bag
    Public Items() As Integer
End Structure

Sub Main()
    Dim g As Bag
    Dim src(3) As Integer
    src(1) = 8
    g.Items = src
    Console.WriteLine(g.Items(1))
End Sub";
        var output = CompileToCppOptimized(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("std::vector<int32_t> Items"),
            "the member must be typed as an array, not rebuilt as a scalar from its name:\n" + output);
        Assert.That(CompileRun(output), Is.EqualTo("8\n"));
    }
}
