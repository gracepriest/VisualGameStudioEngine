using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using BasicLang.Net;

namespace BasicLang.Compiler.CodeGen.Net
{
    /// <summary>
    /// <c>blnet_facade.g.hpp</c> — an ergonomic C++ rendering of the same slots
    /// <c>blnet_proxies.g.hpp</c> exposes under their §7.3 mangled names.
    ///
    /// <para><b>Why it exists.</b> A mangled name folds the declaring type, member, static-ness,
    /// generic arity and per-parameter ref-kind into one flat C identifier plus a signature hash,
    /// because the export lives in a flat symbol namespace with no overloading and 37 public
    /// framework types contain member pairs that collide without it. That is not negotiable — but
    /// it makes hand-written C++ both unreadable AND FRAGILE, because the hash moves whenever the
    /// signature does. Naming a slot by hand breaks silently on an unrelated edit.</para>
    ///
    /// <para><b>The load-bearing design choice: this is a SECOND RENDERING of the same
    /// <c>SlotPlan</c> list</b> the proxy emitter already computes, not an independent walk of the
    /// surface. The facade therefore cannot disagree with the proxy table about a signature — both
    /// are projections of one plan. Only COVERAGE can drift, which is what
    /// <c>NetFacadeEmitterTests</c> pins as a set identity.</para>
    ///
    /// <para><b>v1 scope (plan Task 1): STATIC METHODS ONLY.</b> Everything else — instance
    /// members, constructors, properties, fields, and any slot whose result or arguments fan out
    /// to multiple scalar slots — is deliberately unrendered and REPORTED through
    /// <see cref="FacadeSkip"/> rather than dropped, so the coverage test fails on a newly
    /// unrenderable slot instead of quietly covering less. That is §7.2's lesson applied to
    /// ourselves: an omission nobody is told about leaves the surface meaning less than it says.
    /// </para>
    /// </summary>
    internal static partial class NetProxyEmitter
    {
        internal const string FacadeFileName = "blnet_facade.g.hpp";

        /// <summary>
        /// The root namespace every facade name sits under (decision D1).
        ///
        /// <para>⛔ NEVER emit a bare <c>namespace System</c> at global scope. This header is
        /// generated into a project the user did not write and cannot edit; a global
        /// <c>System</c> would collide with any user namespace or type of that name and turn a
        /// program that compiled into one that does not. The root makes the header inert, and
        /// <c>using namespace BasicLang::netfx;</c> is one opt-in line away from
        /// <c>System::Console::WriteLine(...)</c>.</para>
        /// </summary>
        internal const string FacadeRootNamespace = "BasicLang::netfx";

        /// <summary>Why one slot is absent from the facade — the skip list is DATA, not silence.</summary>
        internal sealed class FacadeSkip
        {
            internal FacadeSkip(string slotName, string reason)
            {
                SlotName = slotName;
                Reason = reason;
            }

            internal string SlotName { get; }
            internal string Reason { get; }
        }

        /// <summary>
        /// The slots v1 does not render, with the reason for each. Exposed so the coverage test
        /// can assert <c>rendered ∪ skipped == every slot</c> as a SET — a count would pass while
        /// one slot silently swapped for another.
        /// </summary>
        internal static IReadOnlyList<FacadeSkip> FacadeSkips(NetSurface surface) =>
            Plan(surface).Select(ClassifyForFacade).Where(s => s != null).ToList();

        /// <summary>The slot names v1 DOES render.</summary>
        internal static IReadOnlyList<string> FacadeRendered(NetSurface surface) =>
            Plan(surface).Where(p => ClassifyForFacade(p) == null).Select(p => p.SlotName).ToList();

        /// <summary>
        /// One slot's verdict: null when it renders, a <see cref="FacadeSkip"/> when it does not.
        /// ONE function so the emitter and the coverage test cannot disagree about what v1 covers.
        /// </summary>
        private static FacadeSkip ClassifyForFacade(SlotPlan plan)
        {
            if (plan.Member.Kind != NetMemberCategory.Method)
                return new FacadeSkip(plan.SlotName,
                    "v1 renders methods only; this is a " + plan.Member.Kind
                    + " (constructors are plan Task 3, properties and fields Task 3).");

            if (!plan.Member.IsStatic || plan.HasReceiver)
                return new FacadeSkip(plan.SlotName,
                    "v1 renders STATIC methods only; an instance method needs the handle-holding "
                    + "wrapper from plan Task 2.");

            if (plan.Return.Kind == WireKind.MultiScalar)
                return new FacadeSkip(plan.SlotName,
                    "the result fans out to multiple scalar slots, which the proxy returns through "
                    + "out-references; rendering that ergonomically needs a decision v1 does not make.");

            if (plan.Parameters.Any(p => p.Wire.Kind == WireKind.MultiScalar))
                return new FacadeSkip(plan.SlotName,
                    "a parameter fans out to multiple scalar slots; same open decision as a "
                    + "multi-slot result.");

            if (plan.Parameters.Any(p => p.ByRef))
                return new FacadeSkip(plan.SlotName,
                    "a ByRef parameter's ergonomic C++ spelling (reference vs pointer vs a "
                    + "returned tuple) is an open decision; the mangled slot remains available.");

            return null;
        }

        // ------------------------------------------------------------------------------------

        private static string EmitFacade(NetSurface surface, IReadOnlyList<SlotPlan> plans)
        {
            var sb = new StringBuilder();
            Banner(sb, FacadeFileName,
                "An ergonomic rendering of the proxy slots (see the emitter's remarks).",
                "OPT-IN: nothing includes this header for you. Everything in it is inline, so",
                "an unused facade costs nothing.",
                "",
                "    #include \"" + FacadeFileName + "\"",
                "    using namespace " + FacadeRootNamespace + ";",
                "    System::Console::WriteLine(\"hello\");");
            L(sb, "#pragma once");
            L(sb, "#include \"" + ProxiesFileName + "\"");
            L(sb, "#include <string>");
            L(sb, "#include <cstdint>");
            L(sb, "");

            var rendered = plans.Where(p => ClassifyForFacade(p) == null).ToList();
            var collisions = CollidingFacadeSignatures(rendered);
            rendered = rendered.Where(p => !collisions.Contains(p.SlotName)).ToList();

            L(sb, "namespace BasicLang { namespace netfx {");

            foreach (var group in rendered
                         .GroupBy(p => p.Member.DeclaringTypeFullName, StringComparer.Ordinal)
                         .OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                EmitFacadeType(sb, group.Key, group.ToList());
            }

            if (collisions.Count > 0)
            {
                L(sb, "");
                L(sb, "/* OMITTED — two or more slots render to the SAME C++ signature, so a facade");
                L(sb, "   name would silently pick one. §8.3 maps distinct .NET types onto one wire");
                L(sb, "   form (every handle-represented type is NetRef), so this is reachable, not");
                L(sb, "   theoretical. Call these through their mangled slot names in " + ProxiesFileName + ":");
                foreach (var slot in collisions.OrderBy(s => s, StringComparer.Ordinal))
                    L(sb, "     - " + Comment(slot));
                L(sb, "*/");
            }

            L(sb, "");
            L(sb, "}} /* namespace " + FacadeRootNamespace + " */");
            return sb.ToString();
        }

        /// <summary>
        /// Slot names whose facade signature is shared with another slot (decision D8).
        ///
        /// <para>BOTH are omitted rather than one being chosen. §8.3 collapses every
        /// handle-represented type onto <c>NetRef</c>, so <c>F(Regex)</c> and <c>F(Uri)</c> are
        /// one C++ signature — picking either would make <c>F(someUri)</c> silently call the
        /// <c>Regex</c> overload, which is a wrong answer rather than a missing one.</para>
        /// </summary>
        private static ISet<string> CollidingFacadeSignatures(IReadOnlyList<SlotPlan> rendered) =>
            rendered
                .GroupBy(FacadeSignatureKey, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .SelectMany(g => g.Select(p => p.SlotName))
                .ToHashSet(StringComparer.Ordinal);

        private static string FacadeSignatureKey(SlotPlan plan) =>
            plan.Member.DeclaringTypeFullName + "::" + plan.Member.Name + "("
            + string.Join(",", plan.Parameters.Select(p => p.Wire.CppParamType)) + ")";

        private static void EmitFacadeType(
            StringBuilder sb, string declaringTypeFullName, IReadOnlyList<SlotPlan> members)
        {
            var segments = FacadeNamespaceSegments(declaringTypeFullName, out var typeName);

            L(sb, "");
            foreach (var ns in segments) L(sb, "namespace " + ns + " {");
            L(sb, "");
            L(sb, "/* " + Comment(declaringTypeFullName) + " */");
            L(sb, "struct " + typeName + " {");

            foreach (var plan in members.OrderBy(p => p.SlotName, StringComparer.Ordinal))
            {
                var args = plan.Parameters
                    .Select(p => p.Wire.CppParamType + " " + p.Name)
                    .ToList();
                var call = string.Join(", ", plan.Parameters.Select(p => p.Name));
                var ret = plan.Return.Kind == WireKind.Void ? "void" : plan.Return.CppReturnType;
                var forward = "BasicLang::net::" + plan.SlotName + "(" + call + ")";

                L(sb, "    static " + ret + " " + plan.Member.Name
                      + "(" + string.Join(", ", args) + ") {");
                L(sb, "        " + (plan.Return.Kind == WireKind.Void ? "" : "return ") + forward + ";");
                L(sb, "    }");
            }

            L(sb, "};");
            L(sb, "");
            for (var i = segments.Count - 1; i >= 0; i--) L(sb, "} /* namespace " + segments[i] + " */");
        }

        /// <summary>
        /// Splits a .NET full name into C++ namespace segments plus the struct name.
        ///
        /// <para>A NESTED type's <c>+</c> separator becomes <c>_</c> rather than another
        /// namespace: C++ has no way to reopen an enclosing <c>struct</c> to add a nested one
        /// from a different translation unit, and flattening keeps the emission order-independent.
        /// Every segment is sanitized because a .NET identifier may contain characters
        /// (<c>`</c> on a generic arity marker, most obviously) that are not legal in C++.</para>
        /// </summary>
        private static IReadOnlyList<string> FacadeNamespaceSegments(
            string declaringTypeFullName, out string typeName)
        {
            var parts = declaringTypeFullName.Split('.');
            typeName = SanitizeFacadeIdentifier(parts[parts.Length - 1].Replace('+', '_'));
            return parts.Take(parts.Length - 1).Select(SanitizeFacadeIdentifier).ToList();
        }

        private static string SanitizeFacadeIdentifier(string raw)
        {
            var sb = new StringBuilder(raw.Length);
            foreach (var c in raw)
                sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
            if (sb.Length == 0 || char.IsDigit(sb[0])) sb.Insert(0, '_');
            return sb.ToString();
        }
    }
}
