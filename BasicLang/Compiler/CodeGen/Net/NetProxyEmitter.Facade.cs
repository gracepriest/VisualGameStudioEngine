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
    /// <para><b>v1 scope (plan Tasks 1-3): every member CATEGORY — methods, constructors,
    /// properties and fields, static and instance.</b> What remains unrendered is shape, not
    /// category: a slot whose result or arguments fan out to multiple scalar slots, and any ByRef
    /// parameter. Every omission is REPORTED through <see cref="FacadeSkip"/> rather than dropped,
    /// so the coverage test fails on a newly unrenderable slot instead of quietly covering less.
    /// That is §7.2's lesson applied to ourselves: an omission nobody is told about leaves the
    /// surface meaning less than it says.</para>
    ///
    /// <para><b>⚠ A property's SETTER is usually absent, and that is upstream of this emitter.</b>
    /// A <c>&lt;NetProxy&gt;</c> declared type draws only property READ slots; a <c>set_X</c>
    /// descriptor is SYNTHESIZED (<see cref="NetSyntheticKind.Setter"/>) only where a BasicLang
    /// program actually writes the member. Measured on a real build over <c>System.Console</c> and
    /// <c>Regex</c>: zero <c>set_</c> slots. The facade can only render slots that EXIST — it
    /// cannot invent an export — so D6's <c>set_X()</c> appears exactly when its slot does. This
    /// is a property of the surface, not a gap in the facade.</para>
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

        /// <summary>
        /// The tag type that keeps the handle-ADOPTING constructor out of D5's way.
        ///
        /// <para><b>Why a tag rather than a plain <c>explicit T(NetRef)</c>.</b> Task 2 spelled the
        /// adopting constructor that way, which was fine while nothing else took the constructor
        /// space. D5 changes that: a .NET constructor whose single argument is a handle-typed value
        /// of a type NOT in the surface renders as <c>T(const NetRef&amp;)</c>, and an overload set
        /// containing both that and <c>T(NetRef)</c> is AMBIGUOUS for every call. This is not a
        /// contrived shape — <c>StreamReader(Stream)</c> is exactly it.</para>
        ///
        /// <para>No .NET type maps to <c>adopt_handle_t</c>, so a real constructor can never
        /// collide with the adopting one, and D5 owns the ordinary constructor space outright.</para>
        /// </summary>
        private const string FacadeAdoptTagType = "adopt_handle_t";
        private const string FacadeAdoptTag = "adopt_handle";

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
        /// A name the facade REFUSES to resolve by guessing (D8), and what collided on it.
        ///
        /// <para>Reported as BL6027 rather than only as a comment in the generated header: a
        /// comment inside a file nobody opens is not a diagnostic, and the whole point of omitting
        /// both sides is that the caller has to know to reach for the mangled slot instead.</para>
        /// </summary>
        internal sealed class FacadeCollision
        {
            internal FacadeCollision(string cppName, IReadOnlyList<string> netNames, string detail)
            {
                CppName = cppName;
                NetNames = netNames;
                Detail = detail;
            }

            /// <summary>The C++ name two or more things would have shared.</summary>
            internal string CppName { get; }

            /// <summary>The .NET members or types that map onto it, in a stable order.</summary>
            internal IReadOnlyList<string> NetNames { get; }

            /// <summary>Why it cannot be resolved here.</summary>
            internal string Detail { get; }

            internal string Message =>
                "the C++ facade omits '" + CppName + "': " + Detail + " Colliding .NET names: "
                + string.Join(", ", NetNames)
                + ". Call these through their mangled slot names in " + ProxiesFileName
                + " — the proxy table is unaffected. (Facade decision D8.)";
        }

        /// <summary>
        /// Everything one pass over the plan list decides: what renders, what does not and why,
        /// which types carry a handle, and which names collided.
        ///
        /// <para><b>Why it is one pass rather than several queries.</b> These answers depend on
        /// each other in one direction only — a name collision removes a type, which changes which
        /// types carry a handle, which changes which SIGNATURES collide — and computing them
        /// separately let the answers disagree. Before this existed, <c>FacadeRendered</c> reported
        /// slots that <c>EmitFacade</c> then dropped for colliding, so the coverage set identity
        /// was satisfied by a "rendered" set that overstated what the header contained.</para>
        /// </summary>
        private sealed class FacadeLayout
        {
            internal FacadeLayout(
                IReadOnlyList<SlotPlan> rendered,
                IReadOnlyList<FacadeSkip> skipped,
                ISet<string> handleTypes,
                IReadOnlyList<FacadeCollision> collisions,
                IReadOnlyList<string> typeNames)
            {
                Rendered = rendered;
                Skipped = skipped;
                HandleTypes = handleTypes;
                Collisions = collisions;
                TypeNames = typeNames;
            }

            internal IReadOnlyList<SlotPlan> Rendered { get; }
            internal IReadOnlyList<FacadeSkip> Skipped { get; }
            internal ISet<string> HandleTypes { get; }
            internal IReadOnlyList<FacadeCollision> Collisions { get; }
            internal IReadOnlyList<string> TypeNames { get; }
        }

        /// <summary>
        /// The slots the facade does not render, with the reason for each. Exposed so the coverage
        /// test can assert <c>rendered ∪ skipped == every slot</c> as a SET — a count would pass
        /// while one slot silently swapped for another.
        /// </summary>
        internal static IReadOnlyList<FacadeSkip> FacadeSkips(NetSurface surface) =>
            LayOutFacade(Plan(surface)).Skipped;

        /// <summary>The slot names the facade DOES render.</summary>
        internal static IReadOnlyList<string> FacadeRendered(NetSurface surface) =>
            LayOutFacade(Plan(surface)).Rendered.Select(p => p.SlotName).ToList();

        /// <summary>
        /// BL6027, one per collided C++ name — ALWAYS a warning.
        ///
        /// <para>Never an error: the proxy table is complete and every colliding member stays
        /// callable under its mangled name, so the build is correct, merely less ergonomic. Failing
        /// it would turn a convenience header into a reason a working project stops building.</para>
        /// </summary>
        internal static IReadOnlyList<NetReferenceDiagnostic> FacadeDiagnostics(NetSurface surface)
        {
            if (surface == null || !surface.IsNonEmpty)
                return Array.Empty<NetReferenceDiagnostic>();

            return LayOutFacade(Plan(surface)).Collisions
                .Select(c => new NetReferenceDiagnostic("BL6027", c.Message, IsWarning: true))
                .ToList();
        }

        /// <summary>
        /// One slot's SHAPE verdict: null when the shape is renderable, a <see cref="FacadeSkip"/>
        /// when it is not. Says nothing about names — collisions are decided later, over the whole
        /// set, because they are a property of a pair rather than of a slot.
        /// </summary>
        private static FacadeSkip ClassifyForFacade(SlotPlan plan)
        {
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

        /// <summary>
        /// THE pass. Order matters and runs one way only: shape, then NAME collisions between
        /// types, then handles, then SIGNATURE collisions between members.
        ///
        /// <para>Handles are computed after the type omissions on purpose. D7 spells a parameter as
        /// a wrapper only for a type in this set, so computing it first would let a signature name
        /// a type the header no longer defines — a dangling reference in generated code.</para>
        /// </summary>
        private static FacadeLayout LayOutFacade(IReadOnlyList<SlotPlan> plans)
        {
            var skipped = new List<FacadeSkip>();
            var admitted = new List<SlotPlan>();
            foreach (var plan in plans)
            {
                var verdict = ClassifyForFacade(plan);
                if (verdict != null) skipped.Add(verdict); else admitted.Add(plan);
            }

            var collisions = new List<FacadeCollision>();
            var droppedTypes = CollidingFacadeTypeNames(admitted, collisions);

            var survivors = new List<SlotPlan>();
            foreach (var plan in admitted)
            {
                if (droppedTypes.Contains(plan.Member.DeclaringTypeFullName))
                    skipped.Add(new FacadeSkip(plan.SlotName,
                        "its declaring type's C++ name collides with another name in the same "
                        + "scope, so the type is omitted whole (BL6027)."));
                else
                    survivors.Add(plan);
            }

            var handleTypes = FacadeHandleTypes(survivors);
            var collidingSlots = CollidingFacadeSignatures(survivors, handleTypes, collisions);

            var rendered = new List<SlotPlan>();
            foreach (var plan in survivors)
            {
                if (collidingSlots.Contains(plan.SlotName))
                    skipped.Add(new FacadeSkip(plan.SlotName,
                        "another slot renders to the SAME C++ signature, so both are omitted "
                        + "rather than one silently winning (BL6027)."));
                else
                    rendered.Add(plan);
            }

            // A handle type whose every member was pruned still needs its wrapper: a D7 signature
            // elsewhere may name it.
            var typeNames = rendered
                .Select(p => p.Member.DeclaringTypeFullName)
                .Concat(handleTypes)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            return new FacadeLayout(rendered, skipped, handleTypes, collisions, typeNames);
        }

        /// <summary>
        /// Declaring types that get a HANDLE and a wrapper: those with at least one rendered
        /// INSTANCE member, or a constructor (D4, D5).
        ///
        /// <para>A CONSTRUCTOR counts: a type you can construct is a type that holds a handle, even
        /// when every other member of it is static.</para>
        ///
        /// <para>A static-only type deliberately gets neither. <c>System.Console</c> has no
        /// instances, so a <c>Console</c> object would be a meaningless value, and D7 must not
        /// offer it as a parameter type.</para>
        /// </summary>
        private static ISet<string> FacadeHandleTypes(IReadOnlyList<SlotPlan> rendered) =>
            rendered
                .Where(p => p.HasReceiver
                            || p.Member.Kind == NetMemberCategory.Constructor)
                .Select(p => p.Member.DeclaringTypeFullName)
                .ToHashSet(StringComparer.Ordinal);

        /// <summary>
        /// .NET types whose C++ NAME cannot coexist with another name in the same scope. Both
        /// cases here produce a header that DOES NOT COMPILE if emitted, which is why they are
        /// dropped rather than merely reported — unlike a signature collision, whose damage is
        /// only a wrong binding.
        ///
        /// <list type="number">
        /// <item><description><b>Two types, one C++ identifier.</b> Sanitization is many-to-one:
        /// a nested <c>A.B+C</c> flattens to <c>A::B_C</c> and collides with a real
        /// <c>A.B_C</c>; a generic arity marker does the same. Emitting both is a
        /// redefinition.</description></item>
        /// <item><description><b>A type whose name is also a NAMESPACE segment.</b> A type
        /// <c>A.B</c> alongside a type <c>A.B.C</c> needs <c>struct B</c> and
        /// <c>namespace B</c> in the same scope. The namespace wins — other types live inside
        /// it — and the type is dropped. C# forbids this within one assembly, but the surface
        /// spans assemblies, where nothing does.</description></item>
        /// </list>
        /// </summary>
        private static ISet<string> CollidingFacadeTypeNames(
            IReadOnlyList<SlotPlan> admitted, List<FacadeCollision> collisions)
        {
            var types = admitted
                .Select(p => p.Member.DeclaringTypeFullName)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            var dropped = new HashSet<string>(StringComparer.Ordinal);

            // (1) many .NET names -> one C++ path.
            foreach (var group in types
                         .GroupBy(FacadeTypePath, StringComparer.Ordinal)
                         .Where(g => g.Count() > 1)
                         .OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                foreach (var netName in group) dropped.Add(netName);
                collisions.Add(new FacadeCollision(
                    "::" + FacadeRootNamespace + "::" + group.Key,
                    group.OrderBy(n => n, StringComparer.Ordinal).ToList(),
                    "two .NET types sanitize to one C++ identifier, and emitting both would be a "
                    + "redefinition."));
            }

            // (2) a type path that is also a namespace path.
            var namespacePaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var type in types)
            {
                var segments = FacadeNamespaceSegments(type, out _);
                for (var i = 1; i <= segments.Count; i++)
                    namespacePaths.Add(string.Join("::", segments.Take(i)));
            }

            foreach (var type in types)
            {
                var path = FacadeTypePath(type);
                if (!namespacePaths.Contains(path) || dropped.Contains(type)) continue;

                dropped.Add(type);
                collisions.Add(new FacadeCollision(
                    "::" + FacadeRootNamespace + "::" + path,
                    new[] { type },
                    "a .NET type of this name shares it with a NAMESPACE that other surface types "
                    + "live inside, so the name cannot be both a struct and a namespace; the "
                    + "namespace wins and the type is omitted."));
            }

            return dropped;
        }

        /// <summary>The sanitized <c>Ns::Ns::Type</c> path a .NET type renders at, without the root.</summary>
        private static string FacadeTypePath(string declaringTypeFullName)
        {
            var segments = FacadeNamespaceSegments(declaringTypeFullName, out var typeName);
            return string.Join("::", segments.Concat(new[] { typeName }));
        }

        /// <summary>
        /// Slot names whose facade signature is shared with another slot (decision D8).
        ///
        /// <para>BOTH are omitted rather than one being chosen. §8.3 collapses every
        /// handle-represented type onto <c>NetRef</c>, so <c>F(Regex)</c> and <c>F(Uri)</c> can be
        /// one C++ signature — picking either would make <c>F(someUri)</c> silently call the
        /// <c>Regex</c> overload, which is a wrong answer rather than a missing one.</para>
        ///
        /// <para><b>Keyed on the FACADE types, not the wire types</b>, because D7 changes the
        /// answer: once both <c>Regex</c> and <c>Uri</c> have wrappers those overloads are
        /// genuinely distinct C++ signatures and must BOTH render. Keying on the wire form would
        /// omit two perfectly callable members. Handle types outside the surface still collapse
        /// onto <c>NetRef</c> and still collide.</para>
        ///
        /// <para><b>Keyed on the RENDERED name, not the member's .NET name.</b> D6 makes a property
        /// <c>X</c> render as <c>get_X</c>, so it can collide with a METHOD literally named
        /// <c>get_X</c> — two different .NET names, one C++ name. Keying on <c>Member.Name</c>
        /// would let that pair through as two overloads of the same signature, which does not
        /// compile.</para>
        ///
        /// <para>The key omits static-ness, which is the shape C++ wants — it forbids overloading a
        /// static and a non-static member function with the same parameter types, so such a pair
        /// would be caught here as the collision it is. No claim that this is reachable: C# will
        /// not let one type declare both, and an INHERITED member reports its base as
        /// <c>DeclaringTypeFullName</c>, so it lands in a different struct rather than colliding.
        /// The key is shaped this way because it costs nothing, not because a case is known.</para>
        /// </summary>
        private static ISet<string> CollidingFacadeSignatures(
            IReadOnlyList<SlotPlan> rendered, ISet<string> handleTypes,
            List<FacadeCollision> collisions)
        {
            var colliding = new HashSet<string>(StringComparer.Ordinal);

            foreach (var group in rendered
                         .GroupBy(p => FacadeSignatureKey(p, handleTypes), StringComparer.Ordinal)
                         .Where(g => g.Count() > 1)
                         .OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                foreach (var plan in group) colliding.Add(plan.SlotName);
                collisions.Add(new FacadeCollision(
                    group.Key,
                    group.Select(p => p.Member.ToString()).OrderBy(n => n, StringComparer.Ordinal).ToList(),
                    "two or more members render to this one C++ signature — §8.3 maps distinct .NET "
                    + "types onto one wire form, so rendering either would silently bind calls "
                    + "meant for the other."));
            }

            return colliding;
        }

        private static string FacadeSignatureKey(SlotPlan plan, ISet<string> handleTypes) =>
            plan.Member.DeclaringTypeFullName + "::" + FacadeMemberName(plan) + "("
            + string.Join(",", plan.Parameters.Select(p => FacadeParamType(p, handleTypes))) + ")";

        /// <summary>
        /// D7: a handle-typed parameter or return is the WRAPPER type when that type has one, and
        /// raw <c>NetRef</c> otherwise. The surface is the whole world the facade can name; a
        /// handle to a type nobody declared has no wrapper to be.
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
        /// The C++ name a slot renders under (D6).
        ///
        /// <para>A property or field becomes <c>get_X</c> / <c>set_X</c> — boring and explicit. An
        /// <c>operator=</c> overload performing a cross-boundary call is a trap: it looks like
        /// assignment and costs a shim round trip.</para>
        ///
        /// <para>A name that ALREADY carries an accessor prefix is left alone. A synthesized setter
        /// is named <c>set_X</c> (<see cref="NetSyntheticKind.Setter"/>) and §8.5's array accessors
        /// are <c>get_Item</c>/<c>set_Item</c>; prefixing those again yields <c>get_get_Item</c>.</para>
        /// </summary>
        private static string FacadeMemberName(SlotPlan plan)
        {
            if (plan.Member.Kind == NetMemberCategory.Constructor) return ".ctor";

            var name = plan.Member.Name;
            if (plan.Member.Kind == NetMemberCategory.Method) return name;

            return name.StartsWith("get_", StringComparison.Ordinal)
                   || name.StartsWith("set_", StringComparison.Ordinal)
                ? name
                : "get_" + name;
        }

        /// <summary>
        /// A facade type's name, qualified from the global scope.
        ///
        /// <para>Fully qualified rather than short, and that is correctness rather than style: a
        /// member of <c>Fac.Probe.Api</c> may name <c>Other.Ns.Counter</c> while
        /// <c>Fac.Probe.Counter</c> also exists, and the short spelling would silently bind to the
        /// nearer one. Generated code can afford the width; binding the wrong type cannot be
        /// afforded at all.</para>
        /// </summary>
        private static string FacadeQualifiedName(string declaringTypeFullName) =>
            "::" + FacadeRootNamespace + "::" + FacadeTypePath(declaringTypeFullName);

        // ------------------------------------------------------------------------------------

        private static string EmitFacade(NetSurface surface, IReadOnlyList<SlotPlan> plans)
        {
            var layout = LayOutFacade(plans);

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

            var byType = layout.Rendered
                .GroupBy(p => p.Member.DeclaringTypeFullName, StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<SlotPlan>)g.ToList(),
                    StringComparer.Ordinal);

            L(sb, "namespace BasicLang { namespace netfx {");

            if (layout.HandleTypes.Count > 0)
            {
                L(sb, "");
                L(sb, "/* Tag for the handle-ADOPTING constructor: T(adopt_handle, h) wraps an existing");
                L(sb, "   handle, where T(...) constructs a NEW .NET object (D5). No .NET type maps to");
                L(sb, "   this tag, so a real constructor can never collide with the adopting one —");
                L(sb, "   which a plain T(NetRef) would, against any .NET constructor taking a single");
                L(sb, "   handle-typed argument, StreamReader(Stream) being the obvious one. */");
                L(sb, "struct " + FacadeAdoptTagType + " { explicit " + FacadeAdoptTagType + "() = default; };");
                L(sb, "inline constexpr " + FacadeAdoptTagType + " " + FacadeAdoptTag + "{};");
            }

            EmitFacadeForwardDeclarations(sb, layout.TypeNames);

            foreach (var name in layout.TypeNames)
                EmitFacadeTypeDeclaration(sb, name, MembersOf(byType, name), layout.HandleTypes);

            foreach (var name in layout.TypeNames)
                EmitFacadeTypeDefinitions(sb, name, MembersOf(byType, name), layout.HandleTypes);

            if (layout.Collisions.Count > 0)
            {
                L(sb, "");
                L(sb, "/* OMITTED — each name below is claimed by two or more .NET things, and the facade");
                L(sb, "   refuses to pick one: a wrong binding is worse than a missing one. Every member");
                L(sb, "   remains callable under its mangled name in " + ProxiesFileName + ".");
                L(sb, "   Reported on the build as BL6027, so this comment is a reference, not the");
                L(sb, "   only notice you get. */");
                foreach (var collision in layout.Collisions)
                {
                    L(sb, "/*   " + Comment(collision.CppName));
                    L(sb, "       " + Comment(collision.Detail));
                    foreach (var netName in collision.NetNames)
                        L(sb, "     - " + Comment(netName));
                    L(sb, "*/");
                }
            }

            L(sb, "");
            L(sb, "}} /* namespace " + FacadeRootNamespace + " */");
            return sb.ToString();
        }

        private static IReadOnlyList<SlotPlan> MembersOf(
            IReadOnlyDictionary<string, IReadOnlyList<SlotPlan>> byType, string name) =>
            byType.TryGetValue(name, out var members) ? members : Array.Empty<SlotPlan>();

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
                L(sb, "    /* Wraps an EXISTING handle; this does NOT create a .NET object — the");
                L(sb, "       constructors below (D5) do that. Tagged so it cannot be reached by");
                L(sb, "       accident, and so it never joins a real constructor's overload set. */");
                L(sb, "    " + typeName + "(" + FacadeAdoptTagType + ", BasicLang::blnet::NetRef "
                      + FacadeHandleParam + ");");
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

            var ordered = members.OrderBy(p => p.SlotName, StringComparer.Ordinal).ToList();

            foreach (var plan in ordered.Where(IsFacadeConstructor))
                L(sb, "    " + FacadeConstructorSignature(plan, typeName, handleTypes, qualified: false) + ";");

            foreach (var plan in ordered.Where(p => !IsFacadeConstructor(p)))
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
                      + "(" + FacadeAdoptTagType + ", BasicLang::blnet::NetRef "
                      + FacadeHandleParam + ")");
                L(sb, "    : " + FacadeHandleField + "(std::move(" + FacadeHandleParam + ")) {}");
                L(sb, "");
                L(sb, "inline const BasicLang::blnet::NetRef& " + typeName + "::raw() const {");
                L(sb, "    return " + FacadeHandleField + ";");
                L(sb, "}");
                L(sb, "");
            }

            foreach (var plan in members.OrderBy(p => p.SlotName, StringComparer.Ordinal)
                         .Where(IsFacadeConstructor))
            {
                // D5: a real constructor CREATES the .NET object. The constructor slot's proxy
                // returns the fresh handle (PlanMember gives every .ctor a Handle return, because
                // metadata's System.Void would emit a slot that constructs and discards), so the
                // handle field is initialised straight from the call.
                var ctorArgs = string.Join(", ",
                    plan.Parameters.Select(p => FacadeArgument(p, handleTypes)));

                L(sb, "inline " + FacadeConstructorSignature(plan, typeName, handleTypes, qualified: true));
                L(sb, "    : " + FacadeHandleField + "(BasicLang::net::" + plan.SlotName
                      + "(" + ctorArgs + ")) {}");
                L(sb, "");
            }

            foreach (var plan in members.OrderBy(p => p.SlotName, StringComparer.Ordinal)
                         .Where(p => !IsFacadeConstructor(p)))
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
                // A wrapper RESULT adopts the handle the proxy returned, so it goes through the
                // tagged constructor — the untagged spelling is now a real .NET constructor.
                L(sb, "    " + (plan.Return.Kind == WireKind.Void ? "" : "return ")
                      + (wrapsResult
                            ? returnType + "(::" + FacadeRootNamespace + "::" + FacadeAdoptTag
                              + ", " + forward + ")"
                            : forward) + ";");
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
                   + FacadeMemberName(plan)
                   + "(" + string.Join(", ", args) + ")"
                   + (plan.HasReceiver ? " const" : "");
        }

        private static bool IsFacadeConstructor(SlotPlan plan) =>
            plan.Member.Kind == NetMemberCategory.Constructor;

        /// <summary>
        /// One constructor's C++ signature (D5), for the declaration or the out-of-line
        /// definition. A constructor takes no <c>static</c>, no <c>const</c> and no return type,
        /// which is why it does not share <see cref="FacadeMemberSignature"/>.
        /// </summary>
        private static string FacadeConstructorSignature(
            SlotPlan plan, string typeName, ISet<string> handleTypes, bool qualified)
        {
            var args = plan.Parameters
                .Select(p => FacadeParamType(p, handleTypes) + " " + p.Name);

            return (qualified ? typeName + "::" : "") + typeName
                   + "(" + string.Join(", ", args) + ")";
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
