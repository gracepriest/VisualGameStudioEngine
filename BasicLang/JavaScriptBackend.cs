using System;
using System.Text;
using BasicLang.Compiler.IR;

namespace BasicLang.Compiler.CodeGen.JavaScript
{
    /// <summary>
    /// Emits ES-module JavaScript from BasicLang IR.
    ///
    /// <para><b>Design principle (spec): "lowers cleanly to JS, or is rejected."</b> Only
    /// features with a 1:1 native JS construct are emitted; the emulation tail (ByRef,
    /// method overloading, Long, Char, value Structure, operator overloading, .NET BCL
    /// types) is refused at build time by <c>JsCapabilityChecker</c> with BL70xx codes.
    /// Every open C++ backend bug is a feature that LOOKED supported and was silently
    /// wrong at runtime; a build-time refusal is strictly better than a half
    /// implementation.</para>
    ///
    /// <para><b>Why this implements <see cref="IIRVisitor"/> directly instead of extending
    /// <see cref="CodeGeneratorBase"/>.</b> Two reasons, both load-bearing:</para>
    /// <list type="number">
    /// <item><description>The base class names every SSA temp (<c>t0</c>, <c>t1</c>, ...),
    /// which produces output nobody can read in devtools. Readable output is a
    /// requirement here, not a nicety — it is half of what source maps are for.
    /// <c>ImprovedCSharpCodeGenerator</c> made the same call for the same reason.</description></item>
    /// <item><description>The base class declares <c>Visit(IRThrow)</c> and
    /// <c>Visit(IRIndexerStore)</c> as <c>virtual {}</c> — silent no-ops. That is exactly
    /// how LLVM and MSIL came to silently drop collection indexed writes (see the TODO at
    /// ICodeGenerator.cs). Here every unimplemented visitor THROWS.</description></item>
    /// </list>
    /// </summary>
    public class JavaScriptCodeGenerator : ICodeGenerator
    {
        private readonly StringBuilder _output = new StringBuilder();
        private readonly CodeGenOptions _options;

        public string BackendName => "JavaScript";
        public TargetPlatform Target => TargetPlatform.JavaScript;
        public ITypeMapper TypeMapper { get; }

        /// <summary>Generated JavaScript from the last <see cref="Generate"/> call.</summary>
        public string GeneratedCode => _output.ToString();

        public JavaScriptCodeGenerator(CodeGenOptions options = null)
        {
            _options = options ?? new CodeGenOptions();
            TypeMapper = new JavaScriptTypeMapper();
        }

        public string Generate(IRModule module) =>
            throw new NotImplementedException("JavaScript backend: Generate lands in plan task 2.");

        // ------------------------------------------------------------------
        // IIRVisitor
        //
        // Task 2 implements the Hello-World subset (IRFunction, BasicBlock, IRCall,
        // IRConstant, IRReturn). Everything else throws until its own task lands.
        //
        // NEVER convert one of these to an empty body to make a test pass. A silent
        // no-op here is indistinguishable from correct codegen at build time and shows
        // up as wrong output at runtime — the exact bug class this backend exists to
        // avoid inheriting.
        // ------------------------------------------------------------------

        private static Exception NotYet(string node) =>
            new NotSupportedException(
                $"JavaScript backend: {node} lowering is not implemented yet. " +
                "If this construct should be REJECTED rather than lowered, add it to " +
                "JsCapabilityChecker with a BL70xx code instead of implementing it here.");

        public void Visit(IRFunction function) => throw NotYet(nameof(IRFunction));
        public void Visit(BasicBlock block) => throw NotYet(nameof(BasicBlock));
        public void Visit(IRConstant constant) => throw NotYet(nameof(IRConstant));
        public void Visit(IRVariable variable) => throw NotYet(nameof(IRVariable));
        public void Visit(IRBinaryOp binaryOp) => throw NotYet(nameof(IRBinaryOp));
        public void Visit(IRUnaryOp unaryOp) => throw NotYet(nameof(IRUnaryOp));
        public void Visit(IRAssignment assignment) => throw NotYet(nameof(IRAssignment));
        public void Visit(IRLoad load) => throw NotYet(nameof(IRLoad));
        public void Visit(IRStore store) => throw NotYet(nameof(IRStore));
        public void Visit(IRCall call) => throw NotYet(nameof(IRCall));
        public void Visit(IRReturn ret) => throw NotYet(nameof(IRReturn));
        public void Visit(IRBranch branch) => throw NotYet(nameof(IRBranch));
        public void Visit(IRConditionalBranch condBranch) => throw NotYet(nameof(IRConditionalBranch));
        public void Visit(IRPhi phi) => throw NotYet(nameof(IRPhi));
        public void Visit(IRAlloca alloca) => throw NotYet(nameof(IRAlloca));
        public void Visit(IRGetElementPtr gep) => throw NotYet(nameof(IRGetElementPtr));
        public void Visit(IRCast cast) => throw NotYet(nameof(IRCast));
        public void Visit(IRCompare compare) => throw NotYet(nameof(IRCompare));
        public void Visit(IRSwitch switchInst) => throw NotYet(nameof(IRSwitch));
        public void Visit(IRLabel label) => throw NotYet(nameof(IRLabel));
        public void Visit(IRComment comment) => throw NotYet(nameof(IRComment));
        public void Visit(IRArrayAlloc arrayAlloc) => throw NotYet(nameof(IRArrayAlloc));
        public void Visit(IRArrayStore arrayStore) => throw NotYet(nameof(IRArrayStore));
        public void Visit(IRAwait awaitInst) => throw NotYet(nameof(IRAwait));
        public void Visit(IRYield yieldInst) => throw NotYet(nameof(IRYield));
        public void Visit(IRNewObject newObj) => throw NotYet(nameof(IRNewObject));
        public void Visit(IRInstanceMethodCall methodCall) => throw NotYet(nameof(IRInstanceMethodCall));
        public void Visit(IRBaseMethodCall baseCall) => throw NotYet(nameof(IRBaseMethodCall));
        public void Visit(IRFieldAccess fieldAccess) => throw NotYet(nameof(IRFieldAccess));
        public void Visit(IRFieldStore fieldStore) => throw NotYet(nameof(IRFieldStore));
        public void Visit(IRTupleElement tupleElement) => throw NotYet(nameof(IRTupleElement));
        public void Visit(IRTryCatch tryCatch) => throw NotYet(nameof(IRTryCatch));
        public void Visit(IRInlineCode inlineCode) => throw NotYet(nameof(IRInlineCode));
        public void Visit(IRForEach forEach) => throw NotYet(nameof(IRForEach));
        public void Visit(IRIndexerAccess indexer) => throw NotYet(nameof(IRIndexerAccess));
        public void Visit(IRThrow throwInst) => throw NotYet(nameof(IRThrow));
        public void Visit(IRIndexerStore indexerStore) => throw NotYet(nameof(IRIndexerStore));
    }
}
