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
    /// <para><b>v1 scope (plan Tasks 1-2): METHODS, static and instance.</b> Constructors,
    /// properties and fields are plan Task 3; any slot whose result or arguments fan out to
    /// multiple scalar slots, and any ByRef parameter, remain deliberately unrendered. Every
    /// omission is REPORTED through <see cref="FacadeSkip"/> rather than dropped, so the coverage
    /// test fails on a newly unrenderable slot instead of quietly covering less. That is §7.2's
    /// lesson applied to ourselves: an omission nobody is told about leaves the surface meaning
    /// less than it says.</para>
    ///
    /// <para><b>Why the file is emitted in three phases.</b> D7 lets one facade type appear in
    /// another's signature, and .NET name order says nothing about which has to come first
    /// (<c>Fac.Probe.Api</c> sorts before <c>Fac.Probe.Counter</c> but returns one). So every type
    /// is forward-declared, then declared, and only then are the member bodies defined out of
    /// line — a body needs its parameter and return types COMPLETE, which a forward declaration
    /// is not. Emitting bodies inside the struct, as Task 1 did while everything was a scalar,
    /// cannot work once a signature can name a sibling.</para>
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

        /// <summary>The wrapper's handle field, and the constructor parameter that fills it.</summary>
        private const string FacadeHandleField = "blnet_handle_";
        private const string FacadeHandleParam = "blnet_handle";

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
                    + " (constructors, properties and fields are plan Task 3).");

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

        /// <summary>
        /// Declaring types that get a HANDLE and a wrapper: those with at least one rendered
        /// INSTANCE member (D4).
        ///
        /// <para>A static-only type deliberately gets neither. <c>System.Console</c> has no
        /// instances, so a <c>Console</c> object would be a meaningless value, and D7 must not
        /// offer it as a parameter type.</para>
        ///
        /// <para>Computed BEFORE collision pruning, on purpose. Pruning can remove a type's last
        /// instance member, which would remove it from this set, which could change which
        /// signatures collide — a fixpoint nobody needs. Reading it once leaves at worst a wrapper
        /// with a handle and no methods, which compiles and stays referenceable by D7.</para>
        /// </summary>
        private static ISet<string> FacadeHandleTypes(IReadOnlyList<SlotPlan> rendered) =>
            rendered.Where(p => p.HasReceiver)
                .Select(p => p.Member.DeclaringTypeFullName)
                .ToHashSet(StringComparer.Ordinal);

        /// <summary>
        /// D7: a handle-typed parameter is the WRAPPER type when that type has a wrapper, and raw
        /// <c>NetRef</c> otherwise. The surface is the whole world the facade can name; a handle to
        /// a type nobody declared has no wrapper to be.
        /// </summary>
        private static bool RendersAsWrapper(
            WireForm wire, string netTypeFullName, ISet<string> handleTypes) =>
            wire.Kind == WireKind.Handle && handleTypes.Contains(netTypeFullName);

        private static string FacadeParamType(ParameterPlan p, ISet<string> handleTypes) =>
            RendersAsWrapper(p.Wire, p.Descriptor.TypeFullName, handleTypes)
                ? "const " + FacadeQualifiedName(p.Descriptor.TypeFullName) + "&"
                : p.Wire.CppParamType;

        private static string FacadeArgument(ParameterPlan p, ISet<string> handleTypes) =>
            RendersAsWrapper(p.Wire, p.Descriptor.TypeFullName, handleTypes)
                ? p.Name + ".raw()"
                : p.Name;

        private static string FacadeReturnType(SlotPlan plan, ISet<string> handleTypes) =>
            plan.Return.Kind == WireKind.Void
                ? "void"
                : RendersAsWrapper(plan.Return, plan.Member.TypeFullName, handleTypes)
                    ? FacadeQualifiedName(plan.Member.TypeFullName)
                    : plan.Return.CppReturnType;

        /// <summary>
        /// A facade type's name, qualified from the global scope.
        ///
        /// <para>Fully qualified rather than short, and that is correctness rather than style: a
        /// member of <c>Fac.Probe.Api</c> may name <c>Other.Ns.Counter</c> while
        /// <c>Fac.Probe.Counter</c> also exists, and the short spelling would silently bind to the
        /// nearer one. Generated code can afford the width; binding the wrong type cannot be
        /// afforded at all.</para>
        /// </summary>
        private static string FacadeQualifiedName(string declaringTypeFullName)
        {
            var segments = FacadeNamespaceSegments(declaringTypeFullName, out var typeName);
            return "::" + FacadeRootNamespace
                   + string.Concat(segments.Select(s => "::" + s))
                   + "::" + typeName;
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
            L(sb, "#include <utility>   /* std::move, for the handle constructor */");
            L(sb, "");

            var rendered = plans.Where(p => ClassifyForFacade(p) == null).ToList();
            var handleTypes = FacadeHandleTypes(rendered);
            var collisions = CollidingFacadeSignatures(rendered, handleTypes);
            rendered = rendered.Where(p => !collisions.Contains(p.SlotName)).ToList();

            var byType = rendered
                .GroupBy(p => p.Member.DeclaringTypeFullName, StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<SlotPlan>)g.ToList(),
                    StringComparer.Ordinal);

            // A handle type with every method pruned still needs its wrapper: D7 signatures
            // elsewhere may name it.
            var typeNames = byType.Keys
                .Concat(handleTypes)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            L(sb, "namespace BasicLang { namespace netfx {");

            EmitFacadeForwardDeclarations(sb, typeNames);

            foreach (var name in typeNames)
                EmitFacadeTypeDeclaration(sb, name, MembersOf(byType, name), handleTypes);

            foreach (var name in typeNames)
                EmitFacadeTypeDefinitions(sb, name, MembersOf(byType, name), handleTypes);

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

        private static IReadOnlyList<SlotPlan> MembersOf(
            IReadOnlyDictionary<string, IReadOnlyList<SlotPlan>> byType, string name) =>
            byType.TryGetValue(name, out var members) ? members : Array.Empty<SlotPlan>();

        /// <summary>
        /// Slot names whose facade signature is shared with another slot (decision D8).
        ///
        /// <para>BOTH are omitted rather than one being chosen. §8.3 collapses every
        /// handle-represented type onto <c>NetRef</c>, so <c>F(Regex)</c> and <c>F(Uri)</c> are
        /// one C++ signature — picking either would make <c>F(someUri)</c> silently call the
        /// <c>Regex</c> overload, which is a wrong answer rather than a missing one.</para>
        ///
        /// <para><b>Keyed on the FACADE types, not the wire types</b>, because D7 changes the
        /// answer: once both <c>Regex</c> and <c>Uri</c> have wrappers those overloads are
        /// genuinely distinct C++ signatures and must BOTH render. Keying on the wire form would
        /// omit two perfectly callable members. Handle types outside the surface still collapse
        /// onto <c>NetRef</c> and still collide.</para>
        ///
        /// <para>The key omits static-ness, which is the shape C++ wants — it forbids overloading a
        /// static and a non-static member function with the same parameter types, so such a pair
        /// would be caught here as the collision it is. No claim that this is reachable: C# will
        /// not let one type declare both, and an INHERITED member reports its base as
        /// <c>DeclaringTypeFullName</c>, so it lands in a different struct rather than colliding.
        /// The key is shaped this way because it costs nothing, not because a case is known.</para>
        /// </summary>
        private static ISet<string> CollidingFacadeSignatures(
            IReadOnlyList<SlotPlan> rendered, ISet<string> handleTypes) =>
            rendered
                .GroupBy(p => FacadeSignatureKey(p, handleTypes), StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .SelectMany(g => g.Select(p => p.SlotName))
                .ToHashSet(StringComparer.Ordinal);

        private static string FacadeSignatureKey(SlotPlan plan, ISet<string> handleTypes) =>
            plan.Member.DeclaringTypeFullName + "::" + plan.Member.Name + "("
            + string.Join(",", plan.Parameters.Select(p => FacadeParamType(p, handleTypes))) + ")";

        private static void EmitFacadeForwardDeclarations(
            StringBuilder sb, IReadOnlyList<string> typeNames)
        {
            if (typeNames.Count == 0) return;

            L(sb, "");
            L(sb, "/* Forward declarations. A D7 signature may name a sibling facade type, and the");
            L(sb, "   .NET name order says nothing about which has to come first, so every type is");
            L(sb, "   declared before any body is defined. */");
            foreach (var name in typeNames)
            {
                var segments = FacadeNamespaceSegments(name, out var typeName);
                L(sb, string.Concat(segments.Select(s => "namespace " + s + " { "))
                      + "struct " + typeName + ";"
                      + string.Concat(segments.Select(_ => " }")));
            }
        }

        private static void EmitFacadeTypeDeclaration(
            StringBuilder sb, string declaringTypeFullName,
            IReadOnlyList<SlotPlan> members, ISet<string> handleTypes)
        {
            var segments = FacadeNamespaceSegments(declaringTypeFullName, out var typeName);
            var hasHandle = handleTypes.Contains(declaringTypeFullName);

            L(sb, "");
            foreach (var ns in segments) L(sb, "namespace " + ns + " {");
            L(sb, "");
            L(sb, "/* " + Comment(declaringTypeFullName) + " */");
            L(sb, "struct " + typeName + " {");

            if (hasHandle)
            {
                L(sb, "    /* Wraps an EXISTING handle; this does NOT create the .NET object (D5's real");
                L(sb, "       constructors are plan Task 3). explicit on purpose: a NetRef is not a");
                L(sb, "       " + Comment(typeName) + ", and an implicit conversion would let any handle bind to any");
                L(sb, "       wrapper of any type. */");
                L(sb, "    explicit " + typeName + "(BasicLang::blnet::NetRef " + FacadeHandleParam + ");");
                L(sb, "");
                L(sb, "    /* The underlying handle, for the mangled slots and for proxies this facade");
                L(sb, "       does not render. Held privately (D4) so the wrapper cannot be rebound to a");
                L(sb, "       different object behind its own back, and returned BY CONST REFERENCE so");
                L(sb, "       the common use costs no refcount traffic.");
                L(sb, "");
                L(sb, "       NOTE what the hazard actually is. NetRef holds a shared_ptr, so COPYING one");
                L(sb, "       is safe and shares ownership — the release runs once, when the last copy");
                L(sb, "       dies. What is NOT safe is rebuilding one from a raw handle:");
                L(sb, "       NetRef(x.raw().get()) opens a SECOND control block over the same managed");
                L(sb, "       object and both will release it. Use NetRef::Duplicate(x.raw()) when an");
                L(sb, "       independent reference is what you want. */");
                L(sb, "    const BasicLang::blnet::NetRef& raw() const;");
                L(sb, "");
            }

            foreach (var plan in members.OrderBy(p => p.SlotName, StringComparer.Ordinal))
                L(sb, "    " + FacadeMemberSignature(plan, handleTypes, qualifier: null) + ";");

            if (hasHandle)
            {
                L(sb, "");
                L(sb, "private:");
                L(sb, "    BasicLang::blnet::NetRef " + FacadeHandleField + ";");
            }

            L(sb, "};");
            L(sb, "");
            for (var i = segments.Count - 1; i >= 0; i--) L(sb, "} /* namespace " + segments[i] + " */");
        }

        private static void EmitFacadeTypeDefinitions(
            StringBuilder sb, string declaringTypeFullName,
            IReadOnlyList<SlotPlan> members, ISet<string> handleTypes)
        {
            var segments = FacadeNamespaceSegments(declaringTypeFullName, out var typeName);
            var hasHandle = handleTypes.Contains(declaringTypeFullName);
            if (!hasHandle && members.Count == 0) return;

            L(sb, "");
            foreach (var ns in segments) L(sb, "namespace " + ns + " {");
            L(sb, "");

            if (hasHandle)
            {
                L(sb, "inline " + typeName + "::" + typeName
                      + "(BasicLang::blnet::NetRef " + FacadeHandleParam + ")");
                L(sb, "    : " + FacadeHandleField + "(std::move(" + FacadeHandleParam + ")) {}");
                L(sb, "");
                L(sb, "inline const BasicLang::blnet::NetRef& " + typeName + "::raw() const {");
                L(sb, "    return " + FacadeHandleField + ";");
                L(sb, "}");
                L(sb, "");
            }

            foreach (var plan in members.OrderBy(p => p.SlotName, StringComparer.Ordinal))
            {
                var callArgs = new List<string>();

                // The receiver is the proxy's FIRST argument (EmitProxyBody prepends
                // `const NetRef& self`); the facade's whole ergonomic win is that the caller no
                // longer writes it.
                if (plan.HasReceiver) callArgs.Add(FacadeHandleField);
                callArgs.AddRange(plan.Parameters.Select(p => FacadeArgument(p, handleTypes)));

                var forward = "BasicLang::net::" + plan.SlotName
                              + "(" + string.Join(", ", callArgs) + ")";
                var returnType = FacadeReturnType(plan, handleTypes);
                var wrapsResult =
                    RendersAsWrapper(plan.Return, plan.Member.TypeFullName, handleTypes);

                L(sb, "inline " + FacadeMemberSignature(plan, handleTypes, typeName) + " {");
                L(sb, "    " + (plan.Return.Kind == WireKind.Void ? "" : "return ")
                      + (wrapsResult ? returnType + "(" + forward + ")" : forward) + ";");
                L(sb, "}");
                L(sb, "");
            }

            for (var i = segments.Count - 1; i >= 0; i--) L(sb, "} /* namespace " + segments[i] + " */");
        }

        /// <summary>
        /// One member's C++ signature, for the in-class DECLARATION (<paramref name="qualifier"/>
        /// null) or the out-of-line DEFINITION (qualified with the struct name). ONE function so
        /// the two can never disagree — a mismatch is a link error at best and an overload the
        /// caller did not mean at worst.
        ///
        /// <para><c>static</c> appears on the declaration only; C++ rejects it on the out-of-line
        /// definition. <c>const</c> appears on both, and means the WRAPPER is not modified — the
        /// .NET object behind the handle may be mutated freely. It is required rather than
        /// cosmetic: D7 passes wrappers as <c>const&amp;</c>, so a non-const member function could
        /// not be called on one.</para>
        /// </summary>
        private static string FacadeMemberSignature(
            SlotPlan plan, ISet<string> handleTypes, string qualifier)
        {
            var args = plan.Parameters
                .Select(p => FacadeParamType(p, handleTypes) + " " + p.Name);

            return (plan.HasReceiver || qualifier != null ? "" : "static ")
                   + FacadeReturnType(plan, handleTypes) + " "
                   + (qualifier == null ? "" : qualifier + "::")
                   + plan.Member.Name
                   + "(" + string.Join(", ", args) + ")"
                   + (plan.HasReceiver ? " const" : "");
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
