using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.CodeGen.LLVM;
using BasicLang.Compiler.CodeGen.MSIL;
using BasicLang.Compiler.CodeGen.CPlusPlus;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Backend HONESTY MATRIX (spec decision 12): the non-C++ backends must REJECT
/// C++-passthrough features (#CppInclude headers and ::-qualified foreign types)
/// and — for LLVM/MSIL — collections, with a CLEAN typed error rather than
/// silently emitting broken code.
///
/// | Feature                | C++ | C#        | LLVM      | MSIL      |
/// |------------------------|-----|-----------|-----------|-----------|
/// | #CppInclude headers    | ✅  | ❌ error  | ❌ error  | ❌ error  |
/// | :: foreign types       | ✅  | ❌ error  | ❌ error  | ❌ error  |
/// | Collections (List/...) | ✅  | ✅ native | ❌ error  | ❌ error  |
///
/// C# accepts collections natively; it only rejects the passthrough features.
/// </summary>
[TestFixture]
public class ForeignFeatureGuardTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "BasicLang_ForeignGuard_" + Path.GetRandomFileName());
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
    /// Build an IRModule from BasicLang source. When <paramref name="runPreprocessor"/>
    /// is true, the Preprocessor runs first and its collected #CppInclude headers are
    /// threaded onto the module (mirrors Compiler.cs), so the passthrough guard sees them.
    /// </summary>
    private IRModule BuildModule(string source, bool runPreprocessor)
    {
        string processed = source;
        List<string> cppIncludes = new();

        if (runPreprocessor)
        {
            var pre = new Preprocessor();
            processed = pre.Process(source, "test.bas");
            Assert.That(pre.Errors, Is.Empty,
                string.Join("; ", pre.Errors.ConvertAll(e => e.Message)));
            cppIncludes = new List<string>(pre.CppIncludes);
        }

        return BuildModuleFromProcessed(processed, cppIncludes);
    }

    /// <summary>
    /// Build an IRModule from ALREADY-preprocessed text (and any collected
    /// #CppInclude headers). Used when the caller must run the preprocessor with a
    /// real on-disk file path (e.g. #Include splicing), which BuildModule's hardcoded
    /// "test.bas" path cannot resolve.
    /// </summary>
    private IRModule BuildModuleFromProcessed(string processed, List<string> cppIncludes)
    {
        var tokens = new Lexer(processed).Tokenize();
        var ast = new Parser(tokens).Parse();

        var analyzer = new SemanticAnalyzer();
        Assert.That(analyzer.Analyze(ast), Is.True,
            string.Join("; ", analyzer.Errors.ConvertAll(e => e.Message)));

        var module = new IRBuilder(analyzer).Build(ast, "TestModule");
        module.CppIncludes.AddRange(cppIncludes ?? new List<string>());
        return module;
    }

    // ------------------------------------------------------------------
    // C# backend: rejects passthrough (#CppInclude, :: foreign), accepts collections.
    // ------------------------------------------------------------------

    [Test]
    public void CSharp_ForeignType_ThrowsCleanError()
    {
        var module = BuildModule(
            "Sub Main()\nDim m As std::mutex\nEnd Sub",
            runPreprocessor: false);

        var ex = Assert.Throws<ForeignFeatureException>(
            () => new ImprovedCSharpCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("C#"));
        Assert.That(ex.Message, Does.Contain("std::mutex"));
    }

    [Test]
    public void CSharp_CppInclude_ThrowsCleanError()
    {
        var module = BuildModule(
            "#CppInclude <mutex>\nSub Main()\nEnd Sub",
            runPreprocessor: true);

        var ex = Assert.Throws<ForeignFeatureException>(
            () => new ImprovedCSharpCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("C#"));
        Assert.That(ex.Message, Does.Contain("CppInclude"));
    }

    [Test]
    public void CSharp_Collections_DoNotThrow()
    {
        // C# supports collections natively — the guard must NOT reject them.
        var module = BuildModule(
            "Sub Main()\nDim l As New List(Of Integer)()\nEnd Sub",
            runPreprocessor: false);

        Assert.DoesNotThrow(() => new ImprovedCSharpCodeGenerator().Generate(module));
    }

    // ------------------------------------------------------------------
    // LLVM backend: rejects passthrough AND collections.
    // ------------------------------------------------------------------

    [Test]
    public void LLVM_Collections_ThrowCleanError()
    {
        var module = BuildModule(
            "Sub Main()\nDim l As New List(Of Integer)()\nEnd Sub",
            runPreprocessor: false);

        var ex = Assert.Throws<ForeignFeatureException>(
            () => new LLVMCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("LLVM"));
        Assert.That(ex.Message, Does.Contain("List"));
    }

    [Test]
    public void LLVM_ForeignType_ThrowsCleanError()
    {
        var module = BuildModule(
            "Sub Main()\nDim m As std::mutex\nEnd Sub",
            runPreprocessor: false);

        var ex = Assert.Throws<ForeignFeatureException>(
            () => new LLVMCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("LLVM"));
    }

    // ------------------------------------------------------------------
    // MSIL backend: rejects passthrough AND collections.
    // ------------------------------------------------------------------

    [Test]
    public void MSIL_Collections_ThrowCleanError()
    {
        var module = BuildModule(
            "Sub Main()\nDim l As New List(Of Integer)()\nEnd Sub",
            runPreprocessor: false);

        var ex = Assert.Throws<ForeignFeatureException>(
            () => new MSILCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("MSIL"));
        Assert.That(ex.Message, Does.Contain("List"));
    }

    [Test]
    public void MSIL_CppInclude_ThrowsCleanError()
    {
        var module = BuildModule(
            "#CppInclude <mutex>\nSub Main()\nEnd Sub",
            runPreprocessor: true);

        var ex = Assert.Throws<ForeignFeatureException>(
            () => new MSILCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("MSIL"));
        Assert.That(ex.Message, Does.Contain("CppInclude"));
    }

    // ------------------------------------------------------------------
    // FILE-SCOPE (module global) bypass regression: a Dim moved from inside a
    // Sub to module scope becomes an IRModule.GlobalVariable, which the type walk
    // must also visit. Otherwise LLVM/MSIL silently emit broken code.
    // ------------------------------------------------------------------

    [Test]
    public void LLVM_ModuleGlobalCollection_ThrowsCleanError()
    {
        // Module-level Dim -> IRModule.GlobalVariables. The declared List type must
        // still trip the guard even though no function local carries it.
        var module = BuildModule(
            "Dim g As List(Of Integer)\nSub Main()\nEnd Sub",
            runPreprocessor: false);
        Assert.That(module.GlobalVariables, Is.Not.Empty,
            "sanity: a module-level Dim must land in GlobalVariables");

        var ex = Assert.Throws<ForeignFeatureException>(
            () => new LLVMCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("LLVM"));
        Assert.That(ex.Message, Does.Contain("List"));
    }

    [Test]
    public void MSIL_ModuleGlobalCollection_ThrowsCleanError()
    {
        var module = BuildModule(
            "Dim g As List(Of Integer)\nSub Main()\nEnd Sub",
            runPreprocessor: false);

        var ex = Assert.Throws<ForeignFeatureException>(
            () => new MSILCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("MSIL"));
        Assert.That(ex.Message, Does.Contain("List"));
    }

    [Test]
    public void LLVM_ModuleGlobalForeignType_ThrowsCleanError()
    {
        // Module-level ::-qualified foreign type global.
        var module = BuildModule(
            "#CppInclude <mutex>\nDim g As std::mutex\nSub Main()\ng.lock()\nEnd Sub",
            runPreprocessor: true);

        // (#CppInclude is present too, but this asserts the foreign-type-in-global
        // walk specifically; either reject reason is acceptable so long as it throws.)
        var ex = Assert.Throws<ForeignFeatureException>(
            () => new LLVMCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("LLVM"));
    }

    [Test]
    public void LLVM_ModuleGlobalForeignType_NoCppInclude_ThrowsCleanError()
    {
        // Foreign-type global WITHOUT #CppInclude, so the ONLY thing that can trip
        // the guard is the GlobalVariables type walk (proves the global walk, not
        // the CppIncludes check, is what rejects).
        var module = BuildModule(
            "Dim g As std::mutex\nSub Main()\ng.lock()\nEnd Sub",
            runPreprocessor: false);
        Assert.That(module.CppIncludes, Is.Empty, "sanity: no #CppInclude present");
        Assert.That(module.GlobalVariables, Is.Not.Empty);

        var ex = Assert.Throws<ForeignFeatureException>(
            () => new LLVMCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("LLVM"));
        Assert.That(ex.Message, Does.Contain("std::mutex"));
    }

    // ------------------------------------------------------------------
    // BUG 2 HONESTY GAP: an interface method's collection PARAMETER used to carry only a
    // bare TypeName (its full Type was null), so ModuleTypeWalker yielded null for it and the
    // LLVM/MSIL guard silently skipped it — a `Sub AddItems(items As List(Of Integer))` on an
    // interface compiled to LLVM/MSIL as broken code. Now that IRBuilder populates the full
    // Type, the guard sees the collection and rejects cleanly on both backends.
    // ------------------------------------------------------------------

    [Test]
    public void LLVM_InterfaceCollectionParam_ThrowsCleanError()
    {
        var module = BuildModule(
            "Interface IStore\nSub AddItems(items As List(Of Integer))\nEnd Interface\nSub Main()\nEnd Sub",
            runPreprocessor: false);

        var ex = Assert.Throws<ForeignFeatureException>(
            () => new LLVMCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("LLVM"));
        Assert.That(ex.Message, Does.Contain("List"));
    }

    [Test]
    public void MSIL_InterfaceCollectionParam_ThrowsCleanError()
    {
        var module = BuildModule(
            "Interface IStore\nSub AddItems(items As List(Of Integer))\nEnd Interface\nSub Main()\nEnd Sub",
            runPreprocessor: false);

        var ex = Assert.Throws<ForeignFeatureException>(
            () => new MSILCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("MSIL"));
        Assert.That(ex.Message, Does.Contain("List"));
    }

    // ------------------------------------------------------------------
    // REGRESSION: the plain #Include source splicer is UNTOUCHED by the guard.
    // The guard only rejects #CppInclude / :: foreign / collections; a #Include
    // of a BasicLang source file must still textually splice its content in.
    // ------------------------------------------------------------------

    [Test]
    public void Include_SourceSplicing_StillWorks()
    {
        // Included file defines a Sub; the main file #Include's it then calls it.
        var includedPath = Path.Combine(_tempDir, "helper.bas");
        File.WriteAllText(includedPath, "Sub Helper()\nConsole.WriteLine(\"from helper\")\nEnd Sub\n");

        var mainSource = "#Include \"helper.bas\"\nSub Main()\nHelper()\nEnd Sub";

        // Run the preprocessor with the REAL on-disk file path so relative #Include
        // resolution finds helper.bas next to Program.bas.
        var progPath = Path.Combine(_tempDir, "Program.bas");
        var pre = new Preprocessor();
        var processed = pre.Process(mainSource, progPath);

        Assert.That(pre.Errors, Is.Empty,
            string.Join("; ", pre.Errors.ConvertAll(e => e.Message)));
        // The included Sub's content must be spliced into the preprocessed text.
        Assert.That(processed, Does.Contain("Sub Helper()"),
            "#Include must textually splice the included source file's content");
        // #Include must NOT populate CppIncludes (that collection is #CppInclude-only).
        Assert.That(pre.CppIncludes, Is.Empty);

        // And the spliced program must generate cleanly on the C# backend
        // (no ForeignFeatureException — #Include is not a passthrough feature).
        var module = BuildModuleFromProcessed(processed, new List<string>());
        Assert.DoesNotThrow(() => new ImprovedCSharpCodeGenerator().Generate(module));
    }

    // ------------------------------------------------------------------
    // HONESTY GAP 2: a collection built ONLY as an EXPRESSION TEMPORARY (never
    // bound to a declared local/field/param/return) bypassed the declared-type
    // walk, so `Return New List(Of Integer)().Count` and `Take(New List(...))`
    // compiled "successfully" on LLVM/MSIL, emitting invalid IL (bare unqualified
    // `newobj ... List`). The guard now also scans function-body IRNewObject
    // instructions and rejects the collection cleanly on LLVM/MSIL. The SAME
    // program still compiles on C# (native collections) and C++.
    // ------------------------------------------------------------------

    private const string ExprTempListSource =
        "Module Program\nFunction GetCount() As Integer\nReturn New List(Of Integer)().Count\nEnd Function\nSub Main()\nEnd Sub\nEnd Module";

    [Test]
    public void LLVM_ExpressionTempCollection_ThrowsCleanError()
    {
        var module = BuildModule(ExprTempListSource, runPreprocessor: false);

        var ex = Assert.Throws<ForeignFeatureException>(
            () => new LLVMCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("LLVM"));
        Assert.That(ex.Message, Does.Contain("List"));
    }

    [Test]
    public void MSIL_ExpressionTempCollection_ThrowsCleanError()
    {
        var module = BuildModule(ExprTempListSource, runPreprocessor: false);

        var ex = Assert.Throws<ForeignFeatureException>(
            () => new MSILCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("MSIL"));
        Assert.That(ex.Message, Does.Contain("List"));
    }

    [Test]
    public void CSharp_ExpressionTempCollection_DoesNotThrow()
    {
        // C# supports collections natively — the expression-temp scan must NOT
        // reject them (only LLVM/MSIL pass rejectCollections: true).
        var module = BuildModule(ExprTempListSource, runPreprocessor: false);
        Assert.DoesNotThrow(() => new ImprovedCSharpCodeGenerator().Generate(module));
    }

    [Test]
    public void Cpp_ExpressionTempCollection_DoesNotThrowForeignFeature()
    {
        // C++ supports collections; the ForeignFeatureChecker is not even wired
        // into the C++ backend, so this program generates without a
        // ForeignFeatureException.
        var module = BuildModule(ExprTempListSource, runPreprocessor: false);
        Assert.DoesNotThrow(
            () => new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false }).Generate(module));
    }

    [Test]
    public void LLVM_ExpressionTempDictionary_ThrowsCleanError()
    {
        var module = BuildModule(
            "Module Program\nFunction GetCount() As Integer\nReturn New Dictionary(Of String, Integer)().Count\nEnd Function\nSub Main()\nEnd Sub\nEnd Module",
            runPreprocessor: false);

        var ex = Assert.Throws<ForeignFeatureException>(
            () => new LLVMCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("LLVM"));
        Assert.That(ex.Message, Does.Contain("Dictionary"));
    }

    [Test]
    public void MSIL_ExpressionTempHashSet_ThrowsCleanError()
    {
        var module = BuildModule(
            "Module Program\nFunction GetCount() As Integer\nReturn New HashSet(Of Integer)().Count\nEnd Function\nSub Main()\nEnd Sub\nEnd Module",
            runPreprocessor: false);

        var ex = Assert.Throws<ForeignFeatureException>(
            () => new MSILCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("MSIL"));
        Assert.That(ex.Message, Does.Contain("HashSet"));
    }

    [Test]
    public void LLVM_CollectionPassedInlineAsArg_ThrowsCleanError()
    {
        // A collection built inline and passed straight as a call argument — again
        // a pure expression temporary with no declared collection-typed position.
        var module = BuildModule(
            "Module Program\nSub Take(items As List(Of Integer))\nEnd Sub\nSub Main()\nTake(New List(Of Integer)())\nEnd Sub\nEnd Module",
            runPreprocessor: false);

        var ex = Assert.Throws<ForeignFeatureException>(
            () => new LLVMCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("LLVM"));
        Assert.That(ex.Message, Does.Contain("List"));
    }

    // ------------------------------------------------------------------
    // HONESTY GAP 3: an inline `cpp{}` block (C++ passthrough) used to be silently
    // DROPPED on C#/LLVM/MSIL (a warning comment + do-nothing program). It must now
    // be REJECTED with a clean ForeignFeatureException. A backend's OWN-language
    // inline block (csharp{} on C#, etc.) must still be allowed.
    // ------------------------------------------------------------------

    private const string CppInlineSource =
        "Module Program\nSub Main()\ncpp{\nstd::cout << \"hi\" << std::endl;\n}\nEnd Sub\nEnd Module";

    [Test]
    public void CSharp_InlineCppBlock_ThrowsCleanError()
    {
        var module = BuildModule(CppInlineSource, runPreprocessor: false);

        var ex = Assert.Throws<ForeignFeatureException>(
            () => new ImprovedCSharpCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("C#"));
        Assert.That(ex.Message, Does.Contain("cpp"));
    }

    [Test]
    public void LLVM_InlineCppBlock_ThrowsCleanError()
    {
        var module = BuildModule(CppInlineSource, runPreprocessor: false);

        var ex = Assert.Throws<ForeignFeatureException>(
            () => new LLVMCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("LLVM"));
        Assert.That(ex.Message, Does.Contain("cpp"));
    }

    [Test]
    public void MSIL_InlineCppBlock_ThrowsCleanError()
    {
        var module = BuildModule(CppInlineSource, runPreprocessor: false);

        var ex = Assert.Throws<ForeignFeatureException>(
            () => new MSILCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("MSIL"));
        Assert.That(ex.Message, Does.Contain("cpp"));
    }

    [Test]
    public void CSharp_OwnLanguageInlineBlock_DoesNotThrow()
    {
        // A csharp{} block is this backend's OWN language — it must be allowed
        // through (and emitted verbatim), not rejected.
        var module = BuildModule(
            "Module Program\nSub Main()\ncsharp{\nSystem.Console.WriteLine(\"hi\");\n}\nEnd Sub\nEnd Module",
            runPreprocessor: false);

        Assert.DoesNotThrow(() => new ImprovedCSharpCodeGenerator().Generate(module));
    }

    [Test]
    public void Cpp_InlineCppBlock_DoesNotThrowForeignFeature()
    {
        // cpp{} passthrough is exactly what the C++ backend is FOR — it generates
        // without a ForeignFeatureException.
        var module = BuildModule(CppInlineSource, runPreprocessor: false);
        Assert.DoesNotThrow(
            () => new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false }).Generate(module));
    }

    // ------------------------------------------------------------------
    // Phase 2 — ::-qualified FREE FUNCTIONS and GLOBAL VARIABLES in expression
    // position must ALSO be honesty-gated. A `Dim x = ns::f(...)` / `Dim x = ns::v`
    // binds an inferred Foreign-typed local, so the shared ModuleTypeWalker declared-
    // type walk rejects it on the non-C++ backends (proved WITHOUT #CppInclude, so the
    // ONLY thing that can trip the guard is the foreign local's type, not the header).
    //
    // NOTE: these BOUND-form tests (`Dim x = ns::f()` / `Dim x = ns::v`) are caught by the
    // pre-existing DECLARED-type walk (ModuleTypeWalker.AllTypes sees the inferred Foreign
    // local), NOT by RejectInlineForeign. The INLINE-form tests further below
    // (`Console.WriteLine(ns::...)`, `Case ns::const`, `When ns::call(...)`) are the ones that
    // exercise the operand scan — keep both so a regression in either guard path is caught.
    // ------------------------------------------------------------------

    [Test]
    public void CSharp_ForeignFreeFunctionCall_ThrowsCleanError()
    {
        var module = BuildModule(
            "Sub Main()\nDim x = mathlib::freeAdd(3, 4)\nEnd Sub",
            runPreprocessor: false);
        Assert.That(module.CppIncludes, Is.Empty, "sanity: no #CppInclude present");

        var ex = Assert.Throws<ForeignFeatureException>(
            () => new ImprovedCSharpCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("C#"));
        Assert.That(ex.Message, Does.Contain("mathlib::freeAdd"));
    }

    [Test]
    public void LLVM_ForeignFreeFunctionCall_ThrowsCleanError()
    {
        var module = BuildModule(
            "Sub Main()\nDim x = mathlib::freeAdd(3, 4)\nEnd Sub",
            runPreprocessor: false);

        var ex = Assert.Throws<ForeignFeatureException>(
            () => new LLVMCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("LLVM"));
        Assert.That(ex.Message, Does.Contain("mathlib::freeAdd"));
    }

    [Test]
    public void MSIL_ForeignFreeFunctionCall_ThrowsCleanError()
    {
        var module = BuildModule(
            "Sub Main()\nDim x = mathlib::freeAdd(3, 4)\nEnd Sub",
            runPreprocessor: false);

        var ex = Assert.Throws<ForeignFeatureException>(
            () => new MSILCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("MSIL"));
        Assert.That(ex.Message, Does.Contain("mathlib::freeAdd"));
    }

    [Test]
    public void CSharp_ForeignGlobalRead_ThrowsCleanError()
    {
        var module = BuildModule(
            "Sub Main()\nDim ans = mathlib::kAnswer\nEnd Sub",
            runPreprocessor: false);
        Assert.That(module.CppIncludes, Is.Empty, "sanity: no #CppInclude present");

        var ex = Assert.Throws<ForeignFeatureException>(
            () => new ImprovedCSharpCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("C#"));
        Assert.That(ex.Message, Does.Contain("mathlib::kAnswer"));
    }

    [Test]
    public void LLVM_ForeignGlobalRead_ThrowsCleanError()
    {
        var module = BuildModule(
            "Sub Main()\nDim ans = mathlib::kAnswer\nEnd Sub",
            runPreprocessor: false);

        var ex = Assert.Throws<ForeignFeatureException>(
            () => new LLVMCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("LLVM"));
        Assert.That(ex.Message, Does.Contain("mathlib::kAnswer"));
    }

    [Test]
    public void MSIL_ForeignGlobalRead_ThrowsCleanError()
    {
        var module = BuildModule(
            "Sub Main()\nDim ans = mathlib::kAnswer\nEnd Sub",
            runPreprocessor: false);

        var ex = Assert.Throws<ForeignFeatureException>(
            () => new MSILCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("MSIL"));
        Assert.That(ex.Message, Does.Contain("mathlib::kAnswer"));
    }

    // ------------------------------------------------------------------
    // HONESTY GAP (Phase 2 inline): the foreign free-function call / global read consumed
    // INLINE — its result never bound to a declared local/field/param/return — bypassed the
    // ModuleTypeWalker declared-type walk AND the old IRNewObject-only instruction scan, so
    // `Console.WriteLine(ns::f(...))` / `Console.WriteLine(ns::v)` compiled "successfully" on
    // C#/LLVM/MSIL and emitted BROKEN code (SanitizeName stripped '::' -> undefined identifier).
    // The guard now also rejects a foreign-typed IRCall and a foreign IRVariable read (name
    // contains '::') found in any OPERAND position. Proved WITHOUT #CppInclude (the inline
    // foreign construct itself is what must trip the guard, not the header).
    // ------------------------------------------------------------------

    private const string InlineForeignCallSource =
        "Sub Main()\nConsole.WriteLine(mathlib::freeAdd(1, 2))\nEnd Sub";
    private const string InlineForeignGlobalSource =
        "Sub Main()\nConsole.WriteLine(mathlib::kAnswer)\nEnd Sub";

    [Test]
    public void CSharp_InlineForeignFreeFunctionCall_ThrowsCleanError()
    {
        var module = BuildModule(InlineForeignCallSource, runPreprocessor: false);
        Assert.That(module.CppIncludes, Is.Empty, "sanity: no #CppInclude present");

        var ex = Assert.Throws<ForeignFeatureException>(
            () => new ImprovedCSharpCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("C#"));
        Assert.That(ex.Message, Does.Contain("mathlib::freeAdd"));
    }

    [Test]
    public void LLVM_InlineForeignFreeFunctionCall_ThrowsCleanError()
    {
        var module = BuildModule(InlineForeignCallSource, runPreprocessor: false);

        var ex = Assert.Throws<ForeignFeatureException>(
            () => new LLVMCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("LLVM"));
        Assert.That(ex.Message, Does.Contain("mathlib::freeAdd"));
    }

    [Test]
    public void MSIL_InlineForeignFreeFunctionCall_ThrowsCleanError()
    {
        var module = BuildModule(InlineForeignCallSource, runPreprocessor: false);

        var ex = Assert.Throws<ForeignFeatureException>(
            () => new MSILCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("MSIL"));
        Assert.That(ex.Message, Does.Contain("mathlib::freeAdd"));
    }

    [Test]
    public void CSharp_InlineForeignGlobalRead_ThrowsCleanError()
    {
        var module = BuildModule(InlineForeignGlobalSource, runPreprocessor: false);
        Assert.That(module.CppIncludes, Is.Empty, "sanity: no #CppInclude present");

        var ex = Assert.Throws<ForeignFeatureException>(
            () => new ImprovedCSharpCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("C#"));
        Assert.That(ex.Message, Does.Contain("mathlib::kAnswer"));
    }

    [Test]
    public void LLVM_InlineForeignGlobalRead_ThrowsCleanError()
    {
        var module = BuildModule(InlineForeignGlobalSource, runPreprocessor: false);

        var ex = Assert.Throws<ForeignFeatureException>(
            () => new LLVMCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("LLVM"));
        Assert.That(ex.Message, Does.Contain("mathlib::kAnswer"));
    }

    [Test]
    public void MSIL_InlineForeignGlobalRead_ThrowsCleanError()
    {
        var module = BuildModule(InlineForeignGlobalSource, runPreprocessor: false);

        var ex = Assert.Throws<ForeignFeatureException>(
            () => new MSILCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("MSIL"));
        Assert.That(ex.Message, Does.Contain("mathlib::kAnswer"));
    }

    // ------------------------------------------------------------------
    // HONESTY GAP (Phase 2 switch/case/when): a foreign ::-qualified value used in a
    // Select-Case value, a `Case ns::const`, or a `When ns::call(...)` guard is ANOTHER inline
    // operand position that never binds to a declared local. The declared-type walk misses it,
    // and the old operand scan yielded only IRSwitch.Value (not Cases[]/PatternCases[]). Left
    // alone the managed backends emitted broken code (`case mathlib:` / `when mathlibisValid(y)`).
    // The shared IROperandWalker now surfaces case + pattern operands (When guard, range,
    // comparison, constant, recursing Or/Tuple) so the guard rejects them cleanly. Proved
    // WITHOUT #CppInclude (the foreign switch construct itself is what trips the guard).
    // ------------------------------------------------------------------

    private const string CaseForeignConstSource =
        "Sub Main()\nDim y As Integer = 42\nSelect Case y\nCase mathlib::kAnswer\nConsole.WriteLine(\"m\")\nEnd Select\nEnd Sub";
    private const string WhenForeignGuardSource =
        "Sub Main()\nDim y As Integer = 5\nSelect Case y\nCase Is > 0 When mathlib::isValid(y)\nConsole.WriteLine(\"v\")\nEnd Select\nEnd Sub";

    [Test]
    public void CSharp_CaseForeignConstant_ThrowsCleanError()
    {
        var module = BuildModule(CaseForeignConstSource, runPreprocessor: false);
        Assert.That(module.CppIncludes, Is.Empty, "sanity: no #CppInclude present");

        var ex = Assert.Throws<ForeignFeatureException>(
            () => new ImprovedCSharpCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("C#"));
        Assert.That(ex.Message, Does.Contain("mathlib::kAnswer"));
    }

    [Test]
    public void LLVM_CaseForeignConstant_ThrowsCleanError()
    {
        var module = BuildModule(CaseForeignConstSource, runPreprocessor: false);

        var ex = Assert.Throws<ForeignFeatureException>(
            () => new LLVMCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("LLVM"));
        Assert.That(ex.Message, Does.Contain("mathlib::kAnswer"));
    }

    [Test]
    public void MSIL_CaseForeignConstant_ThrowsCleanError()
    {
        var module = BuildModule(CaseForeignConstSource, runPreprocessor: false);

        var ex = Assert.Throws<ForeignFeatureException>(
            () => new MSILCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("MSIL"));
        Assert.That(ex.Message, Does.Contain("mathlib::kAnswer"));
    }

    [Test]
    public void CSharp_WhenForeignGuard_ThrowsCleanError()
    {
        var module = BuildModule(WhenForeignGuardSource, runPreprocessor: false);
        Assert.That(module.CppIncludes, Is.Empty, "sanity: no #CppInclude present");

        var ex = Assert.Throws<ForeignFeatureException>(
            () => new ImprovedCSharpCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("C#"));
        Assert.That(ex.Message, Does.Contain("mathlib::isValid"));
    }

    [Test]
    public void LLVM_WhenForeignGuard_ThrowsCleanError()
    {
        var module = BuildModule(WhenForeignGuardSource, runPreprocessor: false);

        var ex = Assert.Throws<ForeignFeatureException>(
            () => new LLVMCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("LLVM"));
        Assert.That(ex.Message, Does.Contain("mathlib::isValid"));
    }

    [Test]
    public void MSIL_WhenForeignGuard_ThrowsCleanError()
    {
        var module = BuildModule(WhenForeignGuardSource, runPreprocessor: false);

        var ex = Assert.Throws<ForeignFeatureException>(
            () => new MSILCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("MSIL"));
        Assert.That(ex.Message, Does.Contain("mathlib::isValid"));
    }

    // ------------------------------------------------------------------
    // C++ side of the switch/case/when gap: the C++ switch lowering drops PATTERN cases and
    // cannot carry a foreign case value / guard (verified: even a plain `Case 42` is dropped —
    // a separate pre-existing gap). Rather than SILENTLY miscompile a foreign construct, the C++
    // backend rejects foreign values in switch positions with a clean CppCapabilityException.
    // This keeps the honesty matrix intact on the NATIVE backend too (no silent miscompile
    // anywhere). Foreign free-calls / globals OUTSIDE a switch still compile+run on C++
    // (covered by CppPassthroughTests) — only the switch positions are rejected.
    // ------------------------------------------------------------------

    [Test]
    public void Cpp_CaseForeignConstant_RejectedWithCleanCapabilityError()
    {
        var module = BuildModule(CaseForeignConstSource, runPreprocessor: false);

        var ex = Assert.Throws<CppCapabilityException>(
            () => new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false }).Generate(module));
        Assert.That(string.Join("; ", ex!.Diagnostics), Does.Contain("mathlib::kAnswer"));
        Assert.That(string.Join("; ", ex.Diagnostics), Does.Contain("Select Case"));
    }

    [Test]
    public void Cpp_WhenForeignGuard_RejectedWithCleanCapabilityError()
    {
        var module = BuildModule(WhenForeignGuardSource, runPreprocessor: false);

        var ex = Assert.Throws<CppCapabilityException>(
            () => new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false }).Generate(module));
        Assert.That(string.Join("; ", ex!.Diagnostics), Does.Contain("mathlib::isValid"));
    }
}
