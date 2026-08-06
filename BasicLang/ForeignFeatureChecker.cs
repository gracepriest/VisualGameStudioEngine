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
    /// | Feature                  | C++ | C#        | JavaScript | LLVM      | MSIL      |
    /// |--------------------------|-----|-----------|------------|-----------|-----------|
    /// | #CppInclude headers      | yes | error     | error      | error     | error     |
    /// | :: foreign TYPES         | yes | error     | error      | error     | error     |
    /// | :: foreign EXPRESSIONS   | yes | error     | VERBATIM   | error     | error     |
    /// | Collections (List/...)   | yes | native    | native     | error     | error     |
    /// | Foreign inline code      | yes | error     | error      | error     | error     |
    ///   (cpp{} passthrough)
    ///
    /// ⛔ <b>The two ':: foreign' rows are one syntax with two meanings, split by POSITION.</b>
    /// A <c>::</c> name in TYPE position (<c>Dim m As std::mutex</c>) is an opaque C++ type that
    /// no managed backend can lower — <see cref="CheckType"/> rejects it everywhere, forever. A
    /// <c>::</c> name in EXPRESSION position is just an already-qualified foreign IDENTIFIER, and
    /// on the JavaScript backend that is exactly what a raw JS global is (<c>::console.log(x)</c>),
    /// so JavaScript opts in via <c>Check(allowForeignIdentifiers: true)</c> and emits it verbatim.
    /// C#/LLVM/MSIL have no such reading and keep refusing both. Never widen the flag to
    /// <see cref="CheckType"/>: emitting <c>std::mutex</c> as <c>stdmutex</c> is a silent miscompile.
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
        /// <param name="allowForeignIdentifiers">
        /// True only for a backend that can lower a <c>::</c>-qualified name in EXPRESSION
        /// (VALUE) position — today, JavaScript, where <c>::console</c> IS a real global.
        ///
        /// <para>⛔ This relaxes the two VALUE sites only (the inline-operand scan and the
        /// <see cref="IRNewObject"/> arm). It does NOT touch <see cref="CheckType"/>: a <c>::</c>
        /// TYPE stays rejected on every backend that runs this checker. Nor does it touch the
        /// <c>#CppInclude</c> rejection — a C++ header in a JavaScript program is still an error.</para>
        ///
        /// <para>⛔ Defaults to FALSE, and C#/LLVM/MSIL must keep the default. An opted-in backend
        /// takes on the OBLIGATION to handle every <c>::</c> name it now receives — emitting it
        /// verbatim where that is meaningful and refusing it where it is not. Setting this true
        /// without that handling does not produce an error; it produces a mangled identifier in a
        /// build that reported success. See <c>JavaScriptCodeGenerator.ForeignName</c>.</para>
        /// </param>
        public static void Check(IRModule module, string backendName, bool rejectCollections,
            string ownInlineLanguage, bool allowForeignIdentifiers = false)
        {
            if (module == null) return;

            // (1) #CppInclude headers — C++-backend-only passthrough.
            // ⛔ NOT covered by allowForeignIdentifiers. A #CppInclude names a C++ HEADER; there
            // is nothing for a JavaScript (or any managed) backend to do with one, so this stays
            // an error even on a backend that accepts '::' expressions.
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
            //
            // ⛔ DELIBERATELY NOT gated on allowForeignIdentifiers. This is the TYPE side of the
            // split; see the class remarks. It also does the load-bearing work of keeping the
            // JavaScript relaxation narrow: `Dim el = ::document.getElementById("x")` infers a
            // Foreign-typed local and is refused HERE, which is what stops a '::' value being
            // stored and reused as if the backend had a type for it.
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
                            CheckInstruction(inst, backendName, rejectCollections, ownInlineLanguage,
                                allowForeignIdentifiers);
                    }
                }
        }

        /// <summary>
        /// Reject an expression-temporary collection / <c>::</c> foreign construction
        /// (<see cref="IRNewObject"/>) and a foreign <see cref="IRInlineCode"/> block.
        /// Recurses into try/catch/finally nested blocks so a temporary inside a Try
        /// body is not missed.
        /// </summary>
        private static void CheckInstruction(IRInstruction inst, string backendName, bool rejectCollections,
            string ownInlineLanguage, bool allowForeignIdentifiers)
        {
            // An INLINE-consumed foreign construct whose result never binds to a declared
            // local/field/param/return (so ModuleTypeWalker's declared-type walk never sees it):
            //   Console.WriteLine(ns::f(...))   -> a foreign-typed IRCall
            //   Console.WriteLine(ns::v)        -> a foreign IRVariable read (name has "::"),
            //                                      which only ever appears as an OPERAND.
            // Without this, the '::' was stripped by the backend's SanitizeName and the managed
            // program compiled "successfully" into broken code (undefined identifier). Reject it
            // here with the same clean foreign-not-supported diagnostic.
            //
            // VALUE SITE 1 of 2. An opted-in backend handles these itself — the '::' name reaches
            // its renderer, which must emit it verbatim or refuse it. Skipping the scan is what
            // hands that responsibility over.
            if (!allowForeignIdentifiers)
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
                    //
                    // VALUE SITE 2 of 2. `New ::Chart(ctx)` is a legitimate raw-JS construction on
                    // an opted-in backend, so the arm steps aside there — but ONLY because that
                    // backend's own NewObject renderer re-checks the name and refuses an interior
                    // '::' (`New std::mutex()`), which has no JavaScript meaning. Without that
                    // second check this relaxation would emit `new stdmutex()`.
                    if (!allowForeignIdentifiers && no.ClassName != null && no.ClassName.Contains("::"))
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
                            CheckInstruction(i, backendName, rejectCollections, ownInlineLanguage, allowForeignIdentifiers);
                    if (tc.CatchClauses != null)
                        foreach (var cc in tc.CatchClauses)
                            if (cc?.Block?.Instructions != null)
                                foreach (var i in cc.Block.Instructions)
                                    CheckInstruction(i, backendName, rejectCollections, ownInlineLanguage, allowForeignIdentifiers);
                    if (tc.FinallyBlock?.Instructions != null)
                        foreach (var i in tc.FinallyBlock.Instructions)
                            CheckInstruction(i, backendName, rejectCollections, ownInlineLanguage, allowForeignIdentifiers);
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
            if (inst is IRCall selfCall && IROperandWalker.ForeignName(selfCall) is string selfName)
                ThrowInlineForeign(backendName, selfName);

            // A foreign value consumed as an operand (call arg, condition, assignment/return
            // value, a Select-Case value, a When guard, ...): the free-function call or the
            // global/constant read. Walked via the shared IROperandWalker so the checker and the
            // C++ backend traverse the SAME operand set (incl. switch case/pattern operands).
            // IROperandWalker.ForeignName detects both Foreign-typed values AND (for un-analyzed
            // Case/When positions) values whose verbatim "::" name survived.
            foreach (var op in IROperandWalker.EnumerateOperands(inst))
                if (IROperandWalker.ForeignName(op) is string opName)
                    ThrowInlineForeign(backendName, opName);
        }

        private static void ThrowInlineForeign(string backendName, string construct) =>
            throw new ForeignFeatureException(
                $"The {backendName} backend does not support the '::'-qualified foreign C++ " +
                $"construct '{construct}'; C++ passthrough (free functions / globals) is only " +
                "available on the C++ backend.");

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
