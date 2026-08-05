using System;
using System.Collections.Generic;
using System.Linq;
using BasicLang.Compiler.IR;

namespace BasicLang.Compiler.CodeGen.JavaScript
{
    /// <summary>
    /// Refuses BasicLang features that cannot lower cleanly to JavaScript.
    ///
    /// <para><b>The governing rule:</b> a feature with a natural JS equivalent is emitted;
    /// a feature that would need emulation is REJECTED with a BL70xx code. That posture is
    /// the point of this backend. The features on the reject list are almost exactly the
    /// open C++ backend bug list — ByRef silently dropped, value semantics diverging — and
    /// every one of those shipped as a build that succeeded and then did the wrong thing at
    /// runtime. A build-time refusal is strictly better than a half implementation, and a
    /// rejection cannot be retrofitted credibly once a half implementation exists.</para>
    ///
    /// <para><b>Why this runs on the IR rather than the AST.</b> The spec proposed checking
    /// after semantic analysis and before IR construction, for the source positions AST
    /// nodes carry. Measurement (recorded in the plan's Phase 1 recon) showed every rejected
    /// feature except overloading survives IR construction intact, so the IR is sufficient —
    /// and calling from <c>Generate</c> puts the check on a seam no entry point can bypass.
    /// A separate pre-IR pass would be one more call site each of the CLI, the IDE and the
    /// LSP must remember to make, which is this repo's most common failure mode.</para>
    ///
    /// <para><b>Diagnostics name the offender; they do not cite a line.</b> Declaration
    /// <c>SourceLine</c> is 0 throughout the IR — it exists only on <c>IRInstruction</c>,
    /// and <c>IRFunction</c>/<c>IRClass</c>/<c>IRParameter</c> do not carry it at all. So a
    /// message says <i>"ByRef parameter 'x' in Sub 'Bump'"</i> rather than <i>file(4,12)</i>.
    /// Statement positions ARE populated, which is what source-map emission uses.</para>
    ///
    /// <para><b>Method overloading is deliberately absent from the reject list.</b> BasicLang
    /// has none — the SemanticAnalyzer already errors with "Subroutine 'F' is already
    /// defined in this scope" for both free subs and class methods. A BL7001 arm could never
    /// fire. Note also that duplicate names in <c>module.Functions</c> are LEGAL: two classes
    /// with a method of the same name both flatten to unqualified <c>IRFunction</c>s, so
    /// "duplicate name implies overloading" is a false positive waiting to happen.</para>
    /// </summary>
    public static class JsCapabilityChecker
    {
        /// <summary>
        /// Walks <paramref name="module"/> and throws <see cref="ForeignFeatureException"/>
        /// on the first construct the JavaScript backend refuses to lower.
        /// </summary>
        /// <remarks>
        /// Arms land one per task, each independently revertable:
        /// BL7002 ByRef, BL7003 Long, BL7004 Char, BL7005 value Structure,
        /// BL7006 operator overloading, BL7007 .NET BCL types.
        /// </remarks>
        public static void Check(IRModule module)
        {
            if (module == null) return;

            CheckByRef(module);
        }

        /// <summary>
        /// BL7002 — <c>ByRef</c> parameters.
        ///
        /// <para>JavaScript passes primitives by value with no way to opt out, so a write
        /// through a ByRef parameter is invisible to the caller. Emulating it means boxing
        /// every argument, which D1 forbids.</para>
        ///
        /// <para><b>Five containers, two unrelated types.</b> ByRef reaches the IR as
        /// <c>IRVariable.IsByRef</c> (free functions and class methods, which flatten into
        /// <c>module.Functions</c>) and as <c>IRParameter.IsByRef</c> (interface methods,
        /// delegates, extern declarations). Checking one channel silently accepts the other.
        /// Do not "simplify" this to a single loop over <c>module.Functions</c>.</para>
        ///
        /// <para>Note <c>IRClass.Methods[].Parameters</c> is NOT one of the containers: it
        /// was measured empty, because a method's parameters live on its
        /// <c>Implementation</c>, and that implementation is already in
        /// <c>module.Functions</c>.</para>
        /// </summary>
        private static void CheckByRef(IRModule module)
        {
            foreach (var function in module.Functions ?? Enumerable.Empty<IRFunction>())
            {
                if (function.Parameters == null) continue;
                foreach (var p in function.Parameters)
                    if (p != null && p.IsByRef)
                        throw ByRefRejection(p.Name, DescribeFunction(function));
            }

            foreach (var iface in module.Interfaces?.Values ?? Enumerable.Empty<IRInterface>())
            foreach (var method in iface.Methods ?? Enumerable.Empty<IRInterfaceMethod>())
            foreach (var p in method.Parameters ?? Enumerable.Empty<IRParameter>())
                if (p != null && p.IsByRef)
                    throw ByRefRejection(p.Name, $"interface method '{iface.Name}.{method.Name}'");

            foreach (var del in module.Delegates?.Values ?? Enumerable.Empty<IRDelegate>())
            foreach (var p in del.Parameters ?? Enumerable.Empty<IRParameter>())
                if (p != null && p.IsByRef)
                    throw ByRefRejection(p.Name, $"delegate '{del.Name}'");

            foreach (var ext in module.ExternDeclarations?.Values ?? Enumerable.Empty<IRExternDeclaration>())
            foreach (var p in ext.Parameters ?? Enumerable.Empty<IRParameter>())
                if (p != null && p.IsByRef)
                    throw ByRefRejection(p.Name, $"extern declaration '{ext.Name}'");
        }

        private static string DescribeFunction(IRFunction f) =>
            $"{(f.ReturnType == null || f.ReturnType.Name == "Void" ? "Sub" : "Function")} '{f.Name}'";

        /// <summary>
        /// Names the offender rather than citing a line: declaration <c>SourceLine</c> is 0
        /// throughout the IR (it exists only on <c>IRInstruction</c>), so "parameter 'x' in
        /// Sub 'Bump'" is the most locating information available here.
        /// </summary>
        private static ForeignFeatureException ByRefRejection(string parameterName, string container) =>
            new ForeignFeatureException(
                $"BL7002: ByRef parameter '{parameterName}' in {container} cannot be lowered to " +
                "JavaScript. JavaScript has no reference parameters, so a write through a ByRef " +
                "argument would be invisible to the caller. Return a value instead.");
    }
}
