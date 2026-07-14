using System;
using System.Collections.Generic;
using System.Linq;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;

namespace BasicLang.Compiler.CodeGen
{
    /// <summary>
    /// Thrown when an IRModule uses a C++-only passthrough feature (a
    /// <c>#CppInclude</c> header or a <c>::</c>-qualified foreign type), or — on the
    /// LLVM/MSIL backends — a collection type, on a backend that cannot lower it.
    /// The message names the offending feature and the backend so the failure is
    /// a CLEAN diagnostic instead of silently-emitted broken code.
    /// </summary>
    public class ForeignFeatureException : Exception
    {
        public ForeignFeatureException(string message) : base(message) { }
    }

    /// <summary>
    /// Enforces the backend HONESTY MATRIX (spec decision 12): the non-C++ backends
    /// reject C++-passthrough features, and LLVM/MSIL additionally reject collections.
    ///
    /// | Feature                | C++ | C#        | LLVM      | MSIL      |
    /// |------------------------|-----|-----------|-----------|-----------|
    /// | #CppInclude headers    | yes | error     | error     | error     |
    /// | :: foreign types       | yes | error     | error     | error     |
    /// | Collections (List/...) | yes | native    | error     | error     |
    /// | Foreign inline code    | yes | error     | error     | error     |
    ///   (cpp{} passthrough)
    ///
    /// C# supports List/Dictionary/HashSet natively, so it passes
    /// <c>rejectCollections: false</c>; LLVM and MSIL pass <c>true</c>. Each backend
    /// passes its OWN inline-code language tag (<c>ownInlineLanguage</c>) so a
    /// same-language <c>csharp{}</c>/<c>llvm{}</c>/<c>msil{}</c> block is allowed
    /// while a foreign one (notably <c>cpp{}</c>) is rejected.
    /// The scan covers both DECLARED-type positions (via ModuleTypeWalker) AND
    /// function-body instructions (expression-temporary collections/foreign types
    /// built with <c>New</c>, and inline-code blocks), so a construct that never
    /// binds to a declared local/field/param/return cannot slip past.
    /// Call as the FIRST real statement of the backend's Generate(), before any
    /// code is emitted, so a rejected module produces the error and nothing else.
    /// </summary>
    public static class ForeignFeatureChecker
    {
        /// <param name="module">The IR module about to be lowered.</param>
        /// <param name="backendName">Human-readable backend name for the message (e.g. "C#", "LLVM", "MSIL").</param>
        /// <param name="rejectCollections">
        /// True for LLVM/MSIL (no collection lowering yet); false for C# (native collections).
        /// </param>
        /// <param name="ownInlineLanguage">
        /// This backend's OWN inline-code language tag ("csharp"/"llvm"/"msil"), matching
        /// <see cref="IRInlineCode.Language"/> for a block this backend can emit verbatim.
        /// An inline block tagged for any OTHER language (notably <c>cpp{}</c> C++ passthrough)
        /// is rejected — otherwise the non-C++ backends silently DROP it (emitting a warning
        /// comment and a do-nothing program). Pass the lowercased tag; case-insensitive.
        /// </param>
        public static void Check(IRModule module, string backendName, bool rejectCollections, string ownInlineLanguage)
        {
            if (module == null) return;

            // (1) #CppInclude headers — C++-backend-only passthrough.
            if (module.CppIncludes != null && module.CppIncludes.Count > 0)
            {
                throw new ForeignFeatureException(
                    $"The {backendName} backend does not support #CppInclude (C++ header passthrough); " +
                    "it is only available on the C++ backend.");
            }

            // (2) ::-qualified foreign types, and (3, LLVM/MSIL only) collections.
            // Walk EVERY type-bearing DECLARED position in the module (functions,
            // globals, class members, interface signatures) via the shared
            // ModuleTypeWalker, recursing generic arguments and array element types.
            foreach (var type in ModuleTypeWalker.AllTypes(module))
                CheckType(type, backendName, rejectCollections);

            // (4) Instruction-level scan of function bodies. The declared-type walk
            // above misses constructs that never bind to a declared local/field/
            // param/return — an EXPRESSION TEMPORARY. `Return New List(Of Integer)().Count`
            // and `Take(New List(...))` build a collection (or a `::` foreign type)
            // purely as a transient, so no declared TypeInfo carries it and the guard
            // used to wave it through, emitting invalid IL/LLVM (bare `newobj ... List`).
            // Mirror CppCodeGenerator.ModuleUsesCollections' IRNewObject body-scan.
            // (5) Foreign inline-code blocks (cpp{} on C#/LLVM/MSIL) — reject rather
            // than silently drop them (GAP 3).
            if (module.Functions != null)
                foreach (var func in module.Functions)
                {
                    if (func?.Blocks == null) continue;
                    foreach (var block in func.Blocks)
                    {
                        if (block?.Instructions == null) continue;
                        foreach (var inst in block.Instructions)
                            CheckInstruction(inst, backendName, rejectCollections, ownInlineLanguage);
                    }
                }
        }

        /// <summary>
        /// Reject an expression-temporary collection / <c>::</c> foreign construction
        /// (<see cref="IRNewObject"/>) and a foreign <see cref="IRInlineCode"/> block.
        /// Recurses into try/catch/finally nested blocks so a temporary inside a Try
        /// body is not missed.
        /// </summary>
        private static void CheckInstruction(IRInstruction inst, string backendName, bool rejectCollections, string ownInlineLanguage)
        {
            // An INLINE-consumed foreign construct whose result never binds to a declared
            // local/field/param/return (so ModuleTypeWalker's declared-type walk never sees it):
            //   Console.WriteLine(ns::f(...))   -> a foreign-typed IRCall
            //   Console.WriteLine(ns::v)        -> a foreign IRVariable read (name has "::"),
            //                                      which only ever appears as an OPERAND.
            // Without this, the '::' was stripped by the backend's SanitizeName and the managed
            // program compiled "successfully" into broken code (undefined identifier). Reject it
            // here with the same clean foreign-not-supported diagnostic.
            RejectInlineForeign(inst, backendName);

            switch (inst)
            {
                case IRNewObject no:
                    // A collection built inline (LLVM/MSIL cannot lower it).
                    if (rejectCollections && IsCollectionName(no.ClassName))
                        throw new ForeignFeatureException(
                            $"The {backendName} backend does not support the collection type '{no.ClassName}'; " +
                            "List/Dictionary/HashSet are not yet supported on this backend (use the C# or C++ backend).");
                    // A `::`-qualified C++ foreign type constructed inline (any non-C++ backend).
                    if (no.ClassName != null && no.ClassName.Contains("::"))
                        throw new ForeignFeatureException(
                            $"The {backendName} backend does not support the '::'-qualified foreign C++ type " +
                            $"'{no.ClassName}'; C++ passthrough types are only available on the C++ backend.");
                    break;

                case IRInlineCode inline:
                    // An inline block tagged for a DIFFERENT backend's language is
                    // passthrough this backend cannot honour — most importantly cpp{}.
                    // (A backend's OWN-language block, e.g. csharp{} on C#, is allowed.)
                    if (!string.Equals(inline.Language, ownInlineLanguage, StringComparison.OrdinalIgnoreCase))
                        throw new ForeignFeatureException(
                            $"The {backendName} backend does not support inline '{inline.Language}' code " +
                            $"(a '{inline.Language}{{ }}' passthrough block); inline code for another backend " +
                            "cannot be lowered here (use the matching backend, e.g. the C++ backend for cpp{ }).");
                    break;

                case IRTryCatch tc:
                    if (tc.TryBlock?.Instructions != null)
                        foreach (var i in tc.TryBlock.Instructions)
                            CheckInstruction(i, backendName, rejectCollections, ownInlineLanguage);
                    if (tc.CatchClauses != null)
                        foreach (var cc in tc.CatchClauses)
                            if (cc?.Block?.Instructions != null)
                                foreach (var i in cc.Block.Instructions)
                                    CheckInstruction(i, backendName, rejectCollections, ownInlineLanguage);
                    if (tc.FinallyBlock?.Instructions != null)
                        foreach (var i in tc.FinallyBlock.Instructions)
                            CheckInstruction(i, backendName, rejectCollections, ownInlineLanguage);
                    break;
            }
        }

        /// <summary>
        /// Reject an INLINE-consumed ::-qualified foreign C++ construct: a foreign-typed
        /// <see cref="IRCall"/> (a free-function call, which also surfaces as a standalone
        /// instruction) and a foreign value in any OPERAND position — a foreign free-function
        /// call (<see cref="IRCall"/>) or a global/constant read (<see cref="IRVariable"/> whose
        /// name contains "::"). These never bind to a declared position, so the declared-type
        /// walk misses them; the foreign global read never even surfaces as its own instruction.
        /// Managed-backend-only (the C++ backend never runs this checker).
        /// </summary>
        private static void RejectInlineForeign(IRInstruction inst, string backendName)
        {
            // The instruction itself is a foreign free-function call (result discarded).
            if (inst is IRCall selfCall && selfCall.Type?.Kind == TypeKind.Foreign)
                ThrowInlineForeign(backendName, selfCall.FunctionName);

            // A foreign value consumed as an operand (call arg, condition, assignment/return
            // value, ...): the free-function call or the global/constant read.
            foreach (var op in EnumerateValueOperands(inst))
            {
                if (op is IRCall opCall && opCall.Type?.Kind == TypeKind.Foreign)
                    ThrowInlineForeign(backendName, opCall.FunctionName);
                if (op is IRVariable opVar && opVar.Type?.Kind == TypeKind.Foreign
                    && opVar.Name != null && opVar.Name.Contains("::"))
                    ThrowInlineForeign(backendName, opVar.Name);
            }
        }

        private static void ThrowInlineForeign(string backendName, string construct) =>
            throw new ForeignFeatureException(
                $"The {backendName} backend does not support the '::'-qualified foreign C++ " +
                $"construct '{construct}'; C++ passthrough (free functions / globals) is only " +
                "available on the C++ backend.");

        /// <summary>
        /// The direct IRValue operands an instruction CONSUMES — the positions where an inline
        /// foreign value flows into another expression. Mirrors the operand walk in
        /// CppCodeGenerator (kept in step with the IR node kinds the compiler actually emits).
        /// </summary>
        private static IEnumerable<IRValue> EnumerateValueOperands(IRInstruction instruction)
        {
            switch (instruction)
            {
                case IRInstanceMethodCall mc:
                    if (mc.Object != null) yield return mc.Object;
                    foreach (var a in mc.Arguments) yield return a;
                    break;
                case IRCall call:
                    if (call.CalleeValue != null) yield return call.CalleeValue;
                    foreach (var a in call.Arguments) yield return a;
                    break;
                case IRBaseMethodCall bc:
                    foreach (var a in bc.Arguments) yield return a;
                    break;
                case IRNewObject no:
                    foreach (var a in no.Arguments) yield return a;
                    break;
                case IRFieldAccess fa:
                    if (fa.Object != null) yield return fa.Object;
                    break;
                case IRFieldStore fs:
                    if (fs.Object != null) yield return fs.Object;
                    if (fs.Value != null) yield return fs.Value;
                    break;
                case IRStore st:
                    if (st.Value != null) yield return st.Value;
                    if (st.Address != null) yield return st.Address;
                    break;
                case IRAssignment asn:
                    if (asn.Value != null) yield return asn.Value;
                    break;
                case IRReturn ret:
                    if (ret.Value != null) yield return ret.Value;
                    break;
                case IRBinaryOp bin:
                    if (bin.Left != null) yield return bin.Left;
                    if (bin.Right != null) yield return bin.Right;
                    break;
                case IRCompare cmp:
                    if (cmp.Left != null) yield return cmp.Left;
                    if (cmp.Right != null) yield return cmp.Right;
                    break;
                case IRUnaryOp un:
                    if (un.Operand != null) yield return un.Operand;
                    break;
                case IRCast cast:
                    if (cast.Value != null) yield return cast.Value;
                    break;
                case IRConditionalBranch cbr:
                    if (cbr.Condition != null) yield return cbr.Condition;
                    break;
                case IRSwitch sw:
                    if (sw.Value != null) yield return sw.Value;
                    break;
                case IRIndexerAccess ia:
                    if (ia.Collection != null) yield return ia.Collection;
                    foreach (var i in ia.Indices) yield return i;
                    break;
                case IRIndexerStore ist:
                    if (ist.Collection != null) yield return ist.Collection;
                    foreach (var i in ist.Indices) yield return i;
                    if (ist.Value != null) yield return ist.Value;
                    break;
                case IRArrayStore ast:
                    if (ast.Array != null) yield return ast.Array;
                    if (ast.Index != null) yield return ast.Index;
                    if (ast.Value != null) yield return ast.Value;
                    break;
                case IRThrow th:
                    if (th.Exception != null) yield return th.Exception;
                    break;
                case IRYield y:
                    if (y.Value != null) yield return y.Value;
                    break;
                case IRAwait aw:
                    if (aw.Expression != null) yield return aw.Expression;
                    break;
                case IRForEach fe:
                    if (fe.Collection != null) yield return fe.Collection;
                    break;
            }
        }

        private static void CheckType(TypeInfo type, string backendName, bool rejectCollections)
        {
            if (type == null) return;

            // ::-qualified opaque C++ passthrough type — never lowerable off the C++ backend.
            if (type.Kind == TypeKind.Foreign)
            {
                throw new ForeignFeatureException(
                    $"The {backendName} backend does not support the '::'-qualified foreign C++ type " +
                    $"'{type.Name}'; C++ passthrough types are only available on the C++ backend.");
            }

            // Collections — LLVM/MSIL cannot lower them yet.
            if (rejectCollections && IsCollectionName(type.Name))
            {
                throw new ForeignFeatureException(
                    $"The {backendName} backend does not support the collection type '{type.Name}'; " +
                    "List/Dictionary/HashSet are not yet supported on this backend (use the C# or C++ backend).");
            }

            // Recurse into generic arguments and array element types.
            if (type.GenericArguments != null)
                foreach (var ga in type.GenericArguments)
                    CheckType(ga, backendName, rejectCollections);

            CheckType(type.ElementType, backendName, rejectCollections);
        }

        private static bool IsCollectionName(string name)
        {
            return string.Equals(name, "List", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Dictionary", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "HashSet", StringComparison.OrdinalIgnoreCase);
        }
    }
}
