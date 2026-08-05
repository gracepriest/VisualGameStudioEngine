using System;
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
        /// Intentionally empty. The detection arms land one per task, each independently
        /// revertable:
        /// BL7002 ByRef, BL7003 Long, BL7004 Char, BL7005 value Structure,
        /// BL7006 operator overloading, BL7007 .NET BCL types.
        /// The seam is wired first so those arms have somewhere to go, and so the
        /// passthrough rejection above it is live from the start.
        /// </remarks>
        public static void Check(IRModule module)
        {
            if (module == null) return;
        }
    }
}
