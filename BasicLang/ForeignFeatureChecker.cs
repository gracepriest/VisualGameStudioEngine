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
    ///
    /// C# supports List/Dictionary/HashSet natively, so it passes
    /// <c>rejectCollections: false</c>; LLVM and MSIL pass <c>true</c>.
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
        public static void Check(IRModule module, string backendName, bool rejectCollections)
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
            // Walk EVERY type-bearing position in the module (functions, globals,
            // class members, interface signatures) via the shared ModuleTypeWalker,
            // recursing generic arguments and array element types at each one.
            foreach (var type in ModuleTypeWalker.AllTypes(module))
                CheckType(type, backendName, rejectCollections);
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
