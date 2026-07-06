using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.CPlusPlus;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// C++ backend collection tests (Tasks 2 &amp; 3): the backend ACCEPTS List/Dictionary/HashSet,
/// maps them to the BasicLang::List/Dictionary/HashSet wrappers (value types, never shared_ptr),
/// and (Task 3) lowers collection OPERATIONS — member calls, .Count/.Keys/.Values, indexer
/// read/write, For Each — to correct, .NET-faithful C++ that actually compiles AND runs.
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
        Assert.That(output, Does.Contain(".Get("));  // read lowers to throwing .Get(k)
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
        // End-to-end: a module-global List is populated and read inside Main.
        // Before the ModuleUsesCollections globals fix this produced C++ that
        // referenced BasicLang::List with no preamble and failed to compile.
        var source = @"
Dim g As List(Of Integer)
Sub Main()
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
}
