using System;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.IR;

namespace BasicLang.Net
{
    /// <summary>
    /// Spec §6.5's claim predicate: a name is <b>claimed by native handling</b> iff it appears in
    /// one of exactly three sources, and a claimed name is <b>never</b> routed through the .NET
    /// shim and never reaches <see cref="NetTypeResolver"/>.
    ///
    /// <list type="table">
    ///   <item><term>(a)</term><description>
    ///     <see cref="BoundaryTypeRegistry.Categorize"/> ∈ { NativeOwned, Bridged } — per type name.
    ///   </description></item>
    ///   <item><term>(b)</term><description>
    ///     <c>CppCapabilityChecker</c>'s early returns (CppCapabilityChecker.cs:590-625) —
    ///     per type name.
    ///   </description></item>
    ///   <item><term>(c)</term><description>
    ///     a call routed by <see cref="IRBuilder.KnownNetStaticTypeNames"/> (IRBuilder.cs:3644-3664)
    ///     <b>for which <see cref="CppCodeGenerator.HasStdLibEmission"/> is true</b> —
    ///     <b>per call</b> (type + member).
    ///   </description></item>
    /// </list>
    ///
    /// <para><b>Getting this wrong is the single most dangerous mistake in P2a.</b> Reading
    /// "claimed" as "registry + CppCapabilityChecker" — i.e. rows (a) and (b) only — drops
    /// <c>Console</c>, which appears in NEITHER, and routes <c>Console.WriteLine</c> through the
    /// shim. That rewrites the behavior of every existing program, P1's 13-program parity battery
    /// included.</para>
    ///
    /// <para><b>Row (a) is { NativeOwned, Bridged }, not <c>!= Unknown</c>.</b> ManagedOwned is the
    /// SHIM-ROUTED category — P2a-2's flip moves <c>Regex</c>/<c>Uri</c>/<c>Stream</c>/
    /// <c>FileInfo</c>/<c>DirectoryInfo</c> into it from Rejected — and writing the predicate as
    /// <c>!= Unknown</c> would claim them for native handling, making spec §4.2's
    /// <c>Regex_Match__string</c> slot ungeneratable. <c>Rejected</c> is diagnosed (BL6019), never
    /// claimed. Written this way the predicate is <b>flip-stable</b>: it gives the same answer
    /// before and after the registry move, which
    /// <c>NetClaimPredicateTests.PredicateIsStableAcrossTheP2a2RegistryFlip</c> holds
    /// mechanically.</para>
    ///
    /// <para><b>Row (c) is per call, not per type.</b> <c>KnownNetStaticTypes</c> is a call-SHAPE
    /// classifier — 51 of its 58 entries have no emit arm for any member. <c>Console.WriteLine</c>
    /// is claimed; <c>Console.ReadKey</c> is not.</para>
    ///
    /// <para><b>Direction rule (§6.5).</b> A SOURCE-DECLARED claimed name never reaches the
    /// resolver — <c>Dim a As Action</c> stays <c>std::function</c>. A claimed name appearing in a
    /// .NET member SIGNATURE is governed by §8.3/§8.4/§8.5 instead, so this predicate must not be
    /// applied there.</para>
    /// </summary>
    internal static class NetClaimPredicate
    {
        /// <summary>
        /// Rows (a) and (b): is this TYPE NAME natively handled, independent of any call?
        /// </summary>
        /// <param name="genericArgumentCount">
        /// How many generic arguments the source wrote. Only <c>IEnumerable</c> reads it:
        /// <c>CppCapabilityChecker</c> claims <c>IEnumerable(Of T)</c> (it lowers to
        /// <c>BasicLang::Generator&lt;T&gt;</c>) but NOT the non-generic
        /// <c>System.Collections.IEnumerable</c>, which has no native lowering and is a legitimate
        /// .NET type for the resolver to answer for. Defaulting to 0 therefore keeps the
        /// non-generic spelling unclaimed, matching the checker.
        /// </param>
        internal static bool IsClaimedTypeName(string name, int genericArgumentCount = 0)
        {
            if (string.IsNullOrEmpty(name)) return false;

            // ---- Row (a): the boundary registry, {NativeOwned, Bridged} ONLY ----
            var category = BoundaryTypeRegistry.Categorize(name);
            if (category == BoundaryTypeCategory.NativeOwned
                || category == BoundaryTypeCategory.Bridged)
            {
                return true;
            }

            // ---- Row (b): CppCapabilityChecker's early returns ----
            // Mirrors CppCapabilityChecker.CheckType arm for arm. The checker's copy cannot be
            // called (private, and it reports diagnostics as a side effect), so equivalence is held
            // by NetClaimPredicateTests.RowBAgreesWithTheCapabilityCheckersEarlyReturns rather than
            // by sharing code. NativeOwned and Bridged are checker early returns too — row (a)
            // already covered them.
            if (CppExceptionTypes.IsNetException(name)) return true;      // -> std::runtime_error
            if (name.IndexOf("::", StringComparison.Ordinal) >= 0) return true;  // foreign C++ passthrough

            if (name.Equals("Task", StringComparison.OrdinalIgnoreCase)) return true;      // BasicLang::Task<T>
            if (name.Equals("Func", StringComparison.OrdinalIgnoreCase)) return true;      // std::function
            if (name.Equals("Action", StringComparison.OrdinalIgnoreCase)) return true;    // std::function
            if (name.Equals("List", StringComparison.OrdinalIgnoreCase)) return true;      // BasicLang::List<T>
            if (name.Equals("Dictionary", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.Equals("HashSet", StringComparison.OrdinalIgnoreCase)) return true;
            if (genericArgumentCount > 0
                && name.Equals("IEnumerable", StringComparison.OrdinalIgnoreCase))
            {
                return true;   // BasicLang::Generator<T>
            }

            return false;
        }

        /// <summary>
        /// The full three-source predicate for a CALL SITE <c>typeName.memberName(...)</c>.
        ///
        /// <para>Rows (a)/(b) still apply — a call on a natively-handled type is natively handled —
        /// and row (c) adds the per-call case that neither of them covers.</para>
        /// </summary>
        internal static bool IsClaimedCall(string typeName, string memberName)
        {
            if (string.IsNullOrEmpty(typeName) || string.IsNullOrEmpty(memberName)) return false;

            if (IsClaimedTypeName(typeName)) return true;

            // ---- Row (c): table membership AND a real emit arm ----
            // BOTH halves are required. Membership alone would claim File.ReadAllText,
            // Activator.CreateInstance and 49 other names the C++ backend cannot emit; the arm
            // check alone would be answering a question about a name the IR never routes here.
            if (!IRBuilder.IsKnownNetStaticTypeName(typeName)) return false;

            return CppCodeGenerator.HasStdLibEmission(typeName + "." + memberName);
        }
    }
}
