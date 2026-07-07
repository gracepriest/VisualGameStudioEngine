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
}
