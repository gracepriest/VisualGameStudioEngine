using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Net;

namespace BasicLang.Compiler.CodeGen.Net
{
    /// <summary>
    /// <c>blnet_bindings.g.hpp</c>'s text together with the ORDERED list of proxy-table slot
    /// names it declares. The names are what spec §12.4 holds equal to the generated shim's
    /// surface-derived <c>UnmanagedCallersOnly</c> entry points, so they are surfaced as data
    /// rather than left to be re-parsed out of the header.
    /// </summary>
    internal sealed record NetBindingsResult(string Text, IReadOnlyList<string> SlotNames);

    /// <summary>
    /// Emits the native half of the .NET boundary for one project (spec §9.1–§9.3): the P0
    /// contract headers, the transport seam (<c>BlnetProxyTable</c>), the typed inline C++
    /// proxies that are the public API, and the startup translation unit that loads the shim,
    /// performs P0's handshake and fills both tables.
    ///
    /// <para><b>THE property this class exists to preserve: an EMPTY surface emits NOTHING.</b>
    /// Not an empty file, not an empty directory — nothing. Every project in the repo today has
    /// an empty surface (<see cref="NetSurface.Empty"/>), so this is what keeps P2a-1 from
    /// changing the behavior of a single existing program. <see cref="WriteTo"/> does not even
    /// create <c>obj/gen</c> in that case. Anything that makes emission unconditional — a
    /// "harmless" always-written header, a banner file, a placeholder — breaks the claim, and
    /// <c>NetProxyEmitterTests.EmptySurfaceWritesNoFilesAtAll</c> is the assertion that catches
    /// it.</para>
    ///
    /// <para><b>Keyed on the surface, NOT on the presence of BasicLang sources</b> (§9.5). A
    /// pure-C++ project that declares <c>&lt;NetProxy&gt;</c> types gets the full artifact set
    /// with no <c>.bas</c> file in sight; <c>CppProjectBuilder</c> MERGES this set with
    /// <c>GenerateSplit</c>'s output rather than gating one on the other (that merge is plan
    /// Task 13 — this class is deliberately drivable from a test with a hand-fed surface and
    /// knows nothing about projects, IO layout or the build).</para>
    ///
    /// <para><b>One name, three places.</b> A member's <see cref="NetNameMangler"/> output is
    /// simultaneously the proxy-table slot name, the shim's export entry point, and the name of
    /// the generated C++ proxy function. §9.2's illustrative <c>Customer_Recalculate</c> is not
    /// used: readable per-type names collide across overloads, which is the exact failure §7.3
    /// exists to prevent, and <see cref="NetNameMangler"/>'s own header records that the spec's
    /// worked example is not a binding format. Making all three the same string is what makes
    /// §12.4's slots-≡-exports invariant checkable by set comparison.</para>
    ///
    /// <para><b>Known gaps, all of them belonging to the surface collector rather than here</b>
    /// (nothing populates a surface in P2a-1, so none of them ships behavior):</para>
    /// <list type="bullet">
    /// <item><description><b>Property and field setters.</b>
    /// <see cref="NetMemberDescriptor"/> carries no get/set availability, so a
    /// <see cref="NetMemberCategory.Property"/> or <see cref="NetMemberCategory.Field"/>
    /// descriptor emits exactly ONE slot, shaped as the GETTER. Emitting a setter too would
    /// produce an uncompilable shim for a read-only property. §8.5 already implies the fix —
    /// it speaks of lowering an indexer to <c>get_Item</c>/<c>set_Item</c>, i.e. the collector
    /// is expected to hand accessors over as members.</description></item>
    /// <item><description><b>Enums.</b> §8.3 maps an enum to its underlying integral, which is
    /// not recoverable from a type NAME. An enum-typed parameter therefore lands in the handle
    /// row here. Correcting it needs the collector to record the underlying type.</description></item>
    /// <item><description><b>Delegate parameters (§8.4).</b> Their wire form is already right —
    /// a callback handle is a <c>uint64_t</c>, same as an object handle — but the C++ proxy
    /// spells the parameter <c>NetRef</c>, where a lowered BasicLang lambda produces a
    /// <c>blnet_callback</c>. Registration and release are <c>CppCodeGenerator</c>'s half of
    /// §8.4 (spec §4.3), and the surface must mark delegate parameters for either side to do
    /// better.</description></item>
    /// <item><description><b>Generic methods.</b> A member with
    /// <see cref="NetMemberDescriptor.Arity"/> &gt; 0 gets a slot like any other, but
    /// <c>[UnmanagedCallersOnly]</c> forbids generic type parameters (§8.2), so only a
    /// CONSTRUCTED instantiation is exportable. The descriptor carries arity but not type
    /// arguments.</description></item>
    /// <item><description><b>Arrays and collections crossing outbound (§8.6)</b> need generated
    /// shim copy helpers, which are <c>NetShimGenerator</c>'s.</description></item>
    /// </list>
    /// </summary>
    internal static class NetProxyEmitter
    {
        /// <summary>P0's C contract header — spliced verbatim from <see cref="BlnetRuntimeSources"/>.</summary>
        internal const string ContractHeaderFileName = "blnet.h";

        /// <summary>P0's header-only native runtime — spliced verbatim from <see cref="BlnetRuntimeSources"/>.</summary>
        internal const string RuntimeHeaderFileName = "blnet_runtime.hpp";

        /// <summary>The transport seam: <c>BlnetProxyTable</c> + <c>g_net</c> + the startup declarations.</summary>
        internal const string BindingsFileName = "blnet_bindings.g.hpp";

        /// <summary>The typed inline C++ proxies — the public API both consumers call.</summary>
        internal const string ProxiesFileName = "blnet_proxies.g.hpp";

        /// <summary>
        /// The ONE translation unit in the set: defines <c>g_net</c>, <c>blnet_bind_all</c>,
        /// <c>blnet_startup</c>/<c>blnet_shutdown</c> and the static-initializer object that
        /// runs them. Task 13 must get this into <c>request.SourceFiles</c> or the artifacts
        /// are emitted and never linked.
        /// </summary>
        internal const string StartupFileName = "blnet_startup.g.cpp";

        /// <summary>
        /// §9.3's exit code for every startup failure. Public as a constant so Task 14's
        /// handshake tests and this emitter cannot disagree about it.
        /// </summary>
        internal const int StartupFailureExitCode = 3;

        /// <summary>
        /// The shim module file name the generated startup TU passes to
        /// <c>blnet_load_module</c>. Derived here, in one place, so the emitter, the shim
        /// generator and the deploy step cannot each invent their own spelling.
        ///
        /// <para><b>Windows naming, deliberately</b>, matching
        /// <c>NetShimPublisher.ExpectedDllPath</c>, which already hard-codes <c>.dll</c>:
        /// <c>dotnet publish -p:NativeLib=Shared</c> names its output after the project.
        /// Cross-platform shim publishing is not something P2a-1 does, and inventing a
        /// <c>lib*.so</c> rule here that nothing produces would be a guess, not portability.</para>
        /// </summary>
        internal static string ShimModuleFileName(string shimAssemblyName) =>
            shimAssemblyName + ".dll";

        /// <summary>
        /// The complete <c>obj/gen</c> artifact set for <paramref name="surface"/>, keyed by
        /// file name, or an EMPTY dictionary when the surface is empty. Pure — no IO. Callers
        /// that need the files on disk use <see cref="WriteTo"/>; <c>CppProjectBuilder</c> uses
        /// this overload so it can merge the set with <c>GenerateSplit</c>'s before writing.
        /// </summary>
        /// <param name="shimModuleFileName">
        /// What the startup TU loads — see <see cref="ShimModuleFileName"/>.
        /// </param>
        internal static IReadOnlyDictionary<string, string> Emit(
            NetSurface surface, string shimModuleFileName)
        {
            if (surface == null) throw new ArgumentNullException(nameof(surface));
            if (string.IsNullOrEmpty(shimModuleFileName))
                throw new ArgumentException("A shim module file name is required.", nameof(shimModuleFileName));

            if (!surface.IsNonEmpty)
                return new Dictionary<string, string>(0);

            var plans = Plan(surface);
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ContractHeaderFileName] = BlnetRuntimeSources.BlnetHeader,
                [RuntimeHeaderFileName] = BlnetRuntimeSources.BlnetRuntime,
                [BindingsFileName] = EmitBindingsText(surface, plans),
                [ProxiesFileName] = EmitProxies(surface, plans),
                [StartupFileName] = EmitStartup(surface, plans, shimModuleFileName),
            };
        }

        /// <summary>
        /// The translation units in this set — the ones a compiler must be handed, as opposed
        /// to the headers it only includes. Empty when the surface is empty.
        /// </summary>
        internal static IReadOnlyList<string> TranslationUnitFileNames(NetSurface surface) =>
            surface != null && surface.IsNonEmpty
                ? new[] { StartupFileName }
                : Array.Empty<string>();

        /// <summary>
        /// Writes <see cref="Emit"/>'s set into <paramref name="objGenDir"/> and returns the
        /// full paths written, in file-name order.
        ///
        /// <para><b>On an empty surface this touches the file system not at all</b> — it does
        /// not create <paramref name="objGenDir"/>, which is the observable form of this
        /// class's inertness claim and what the test fixture asserts against. Directory
        /// creation lives INSIDE the non-empty branch for exactly that reason; hoisting it out
        /// as a "harmless" precondition silently breaks the property.</para>
        /// </summary>
        internal static IReadOnlyList<string> WriteTo(
            string objGenDir, NetSurface surface, string shimModuleFileName)
        {
            if (string.IsNullOrEmpty(objGenDir))
                throw new ArgumentException("A destination directory is required.", nameof(objGenDir));

            var files = Emit(surface, shimModuleFileName);
            if (files.Count == 0)
                return Array.Empty<string>();

            Directory.CreateDirectory(objGenDir);
            var written = new List<string>(files.Count);
            foreach (var name in files.Keys.OrderBy(n => n, StringComparer.Ordinal))
            {
                var path = Path.Combine(objGenDir, name);
                File.WriteAllText(path, files[name]);
                written.Add(path);
            }
            return written;
        }

        /// <summary>
        /// <c>blnet_bindings.g.hpp</c> plus its slot names. Task 14 compares
        /// <see cref="NetBindingsResult.SlotNames"/> against the generated shim's
        /// surface-derived exports (§12.4).
        /// </summary>
        internal static NetBindingsResult EmitBindings(NetSurface surface)
        {
            if (surface == null) throw new ArgumentNullException(nameof(surface));
            var plans = Plan(surface);
            return new NetBindingsResult(
                EmitBindingsText(surface, plans),
                plans.Select(p => p.SlotName).ToList());
        }

        // ------------------------------------------------------------------------------
        // Planning: one slot per DISTINCT member, in first-seen order.
        // ------------------------------------------------------------------------------

        /// <summary>
        /// Turns the surface's members into slot plans, collapsing duplicates by mangled name.
        ///
        /// <para><b>The de-duplication is not defensive tidying.</b> §7.1's collector walks call
        /// sites, so the same member reached from three of them arrives three times; emitting
        /// three identically-named struct fields would not compile. Collapsing on the MANGLED
        /// name (rather than on descriptor identity, which is reference equality — see
        /// <see cref="NetMemberDescriptor"/>) is what keeps this in step with the shim
        /// generator, which must collapse on the same key or §12.4's invariant breaks on a
        /// surface nobody thought to de-duplicate.</para>
        /// </summary>
        private static IReadOnlyList<SlotPlan> Plan(NetSurface surface)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var plans = new List<SlotPlan>();
            foreach (var member in surface.Members)
            {
                var slot = NetNameMangler.Mangle(member);
                if (!seen.Add(slot)) continue;
                plans.Add(PlanMember(slot, member));
            }
            return plans;
        }

        private static SlotPlan PlanMember(string slot, NetMemberDescriptor member)
        {
            // A constructor's metadata return type is System.Void, but its EXPORT hands back
            // the object it just created (§8.2's worked shape ends in Table.Create). Reading
            // TypeFullName literally here would emit a slot that constructs and discards.
            var returnWire = member.Kind == NetMemberCategory.Constructor
                ? Wire.Handle
                : WireOf(member.TypeFullName);

            var parameters = new List<ParameterPlan>();
            var index = 0;
            foreach (var p in member.Parameters ?? Array.Empty<NetParameterDescriptor>())
            {
                var wire = WireOf(p.TypeFullName);
                if (p.RefKind != NetRefKind.None && wire.Kind != WireKind.Scalar)
                {
                    // Loud rather than wrong. §8.3 says "ref/out -> pointer slot" and stops
                    // there; for a handle that leaves ownership undefined (writing back a NEW
                    // NetRef over the caller's releases a handle the callee may have returned
                    // unchanged — a double release), and for a string it leaves the in
                    // direction untransmittable through a char**. Neither is guessable, and
                    // shipping a guess here is a use-after-free in generated C++.
                    throw new NotSupportedException(
                        $"Cannot emit a proxy for '{member}': parameter {index} is passed "
                        + $"{p.RefKind} with wire form {wire.Kind}. Spec §8.3 pins ByRef slots "
                        + "only for by-value scalars; ByRef handle and ByRef String ownership "
                        + "is unspecified. Specify it in §8.3 and extend NetProxyEmitter.WireOf "
                        + "— do not widen this check.");
                }
                parameters.Add(new ParameterPlan(p, wire, index++));
            }

            return new SlotPlan(
                slot,
                member,
                hasReceiver: !member.IsStatic && member.Kind != NetMemberCategory.Constructor,
                returnWire,
                parameters);
        }

        // ------------------------------------------------------------------------------
        // §8.3 marshaling: .NET type name -> wire form.
        // ------------------------------------------------------------------------------

        private enum WireKind { Void, Scalar, String, Handle }

        /// <summary>
        /// One row of §8.3. <see cref="CType"/> is the INBOUND C spelling and
        /// <see cref="COutType"/> the pointer a result or ByRef slot uses — the two differ for
        /// <c>String</c> (<c>const char*</c> in, <c>char**</c> out) because the directions have
        /// different ownership: in-params borrow, out-params transfer a <c>blnet_alloc</c>'d
        /// buffer the receiver frees.
        /// </summary>
        private readonly record struct WireForm(
            WireKind Kind, string CType, string COutType, string CppParamType, string CppReturnType);

        private static class Wire
        {
            internal static readonly WireForm Void = new(WireKind.Void, "void", "", "", "void");

            internal static readonly WireForm String =
                new(WireKind.String, "const char*", "char**", "const char*", "std::string");

            internal static readonly WireForm Handle = new(
                WireKind.Handle, "uint64_t", "uint64_t*",
                "const BasicLang::blnet::NetRef&", "BasicLang::blnet::NetRef");

            /// <summary>
            /// <c>Boolean</c> is the one scalar whose C++ spelling differs from its wire
            /// spelling: <c>bool</c> is not blittable for <c>[UnmanagedCallersOnly]</c>, so it
            /// travels as <c>int32_t</c> 0/1 and the proxy converts at both ends.
            /// </summary>
            internal static readonly WireForm Boolean =
                new(WireKind.Scalar, "int32_t", "int32_t*", "bool", "bool");

            internal static WireForm Scalar(string cType) =>
                new(WireKind.Scalar, cType, cType + "*", cType, cType);
        }

        /// <summary>
        /// §8.3, read as a table. Everything not listed is a HANDLE — that is the table's
        /// "other non-ref value types -> handle (boxed)" row plus every reference type, and it
        /// is the safe default: a handle is opaque, so being wrong about a type's shape costs a
        /// missing convenience, never a misinterpreted 64 bits.
        ///
        /// <para><c>Char</c> travels as its .NET width, a <c>uint16_t</c> UTF-16 code unit.
        /// §8.3's shipped divergence — BasicLang's native <c>Char</c> is ONE byte, so inbound
        /// values above U+00FF narrow lossily — belongs to the lowering in
        /// <c>CppCodeGenerator</c>, not here: truncating at the boundary would destroy the value
        /// before the compiler ever got the chance to diagnose it.</para>
        /// </summary>
        private static WireForm WireOf(string netTypeFullName) => netTypeFullName switch
        {
            "System.Void" => Wire.Void,
            "System.Boolean" => Wire.Boolean,
            "System.SByte" => Wire.Scalar("int8_t"),
            "System.Byte" => Wire.Scalar("uint8_t"),
            "System.Int16" => Wire.Scalar("int16_t"),
            "System.UInt16" => Wire.Scalar("uint16_t"),
            "System.Int32" => Wire.Scalar("int32_t"),
            "System.UInt32" => Wire.Scalar("uint32_t"),
            "System.Int64" => Wire.Scalar("int64_t"),
            "System.UInt64" => Wire.Scalar("uint64_t"),
            "System.Single" => Wire.Scalar("float"),
            "System.Double" => Wire.Scalar("double"),
            "System.Char" => Wire.Scalar("uint16_t"),
            "System.String" => Wire.String,
            _ => Wire.Handle,
        };

        // ------------------------------------------------------------------------------
        // blnet_bindings.g.hpp — the transport seam (§4.2).
        // ------------------------------------------------------------------------------

        private static string EmitBindingsText(NetSurface surface, IReadOnlyList<SlotPlan> plans)
        {
            var sb = new StringBuilder();
            Banner(sb, BindingsFileName,
                "The transport seam (spec §4.2): one function-pointer slot per member of the",
                "discovered .NET surface. Slot NAMES are NetNameMangler output and must match the",
                "shim's [UnmanagedCallersOnly(EntryPoint = ...)] strings exactly (§12.4).");
            DeclaredTypesComment(sb, surface);
            L(sb, "#pragma once");
            L(sb, "#include \"" + ContractHeaderFileName + "\"");
            L(sb, "");
            L(sb, "struct BlnetProxyTable {");
            if (plans.Count == 0)
            {
                L(sb, "    /* The surface declared types but contributed no callable members. */");
            }
            foreach (var plan in plans)
            {
                L(sb, "    /* " + Comment(plan.Member.ToString()) + " */");
                L(sb, "    int32_t (BLNET_CALL *" + plan.SlotName + ")(" + CSignature(plan) + ");");
            }
            L(sb, "};");
            L(sb, "");
            L(sb, "/* Defined in " + StartupFileName + ". Every slot is null until blnet_bind_all runs;");
            L(sb, "   the proxies in " + ProxiesFileName + " guard on that (§9.2). */");
            L(sb, "extern BlnetProxyTable g_net;");
            L(sb, "");
            L(sb, "/* Fills g_net from an already-loaded shim module. A slot whose export is missing");
            L(sb, "   stays null rather than failing the whole process: the proxy's null-slot guard");
            L(sb, "   then names that one slot at its call site, which is far more actionable than");
            L(sb, "   refusing to start a program that may never call it. */");
            L(sb, "void blnet_bind_all(void* module);");
            L(sb, "");
            L(sb, "/* Run automatically by a static-initializer object in " + StartupFileName + ".");
            L(sb, "   Declared here so a hand-written C++ main can also call them explicitly. */");
            L(sb, "void blnet_startup();");
            L(sb, "void blnet_shutdown();");
            return sb.ToString();
        }

        /// <summary>The C ABI parameter list of one slot: receiver, parameters, result pointer.</summary>
        private static string CSignature(SlotPlan plan)
        {
            var args = new List<string>();
            if (plan.HasReceiver) args.Add("uint64_t self");
            foreach (var p in plan.Parameters)
                args.Add((p.ByRef ? p.Wire.COutType : p.Wire.CType) + " " + p.Name);
            if (plan.Return.Kind != WireKind.Void) args.Add(plan.Return.COutType + " result");
            return args.Count == 0 ? "void" : string.Join(", ", args);
        }

        // ------------------------------------------------------------------------------
        // blnet_proxies.g.hpp — the public API (§9.2).
        // ------------------------------------------------------------------------------

        private static string EmitProxies(NetSurface surface, IReadOnlyList<SlotPlan> plans)
        {
            var sb = new StringBuilder();
            Banner(sb, ProxiesFileName,
                "The typed C++ proxies (spec §9.2) — the public API. BasicLang codegen and",
                "hand-written C++ call THESE; nothing should touch g_net directly.");
            DeclaredTypesComment(sb, surface);
            L(sb, "#pragma once");
            L(sb, "#include \"" + ContractHeaderFileName + "\"");
            L(sb, "#include \"" + RuntimeHeaderFileName + "\"");
            L(sb, "#include \"" + BindingsFileName + "\"");
            L(sb, "#include <stdexcept>");
            L(sb, "#include <string>");
            L(sb, "");
            L(sb, "namespace BasicLang { namespace net {");
            L(sb, "");
            L(sb, "/* §9.2's null-slot guard. A null slot means one of two things and the message");
            L(sb, "   names both: blnet_startup() has not run (the static-initialization-order");
            L(sb, "   constraint documented in " + StartupFileName + " was violated — another");
            L(sb, "   translation unit's static initializer reached a proxy before ours ran), or the");
            L(sb, "   shim exports no such entry point. Either way this is a clear diagnostic");
            L(sb, "   instead of a jump through a null function pointer. */");
            L(sb, "inline void BlnetRequireSlot(bool bound, const char* slot) {");
            L(sb, "    if (!bound)");
            L(sb, "        throw std::runtime_error(");
            L(sb, "            std::string(\"blnet: proxy slot '\") + slot + \"' is not bound — either \"");
            L(sb, "            \"blnet_startup() has not run yet (static-initialization-order violation) \"");
            L(sb, "            \"or the shim does not export it.\");");
            L(sb, "}");
            foreach (var plan in plans)
            {
                L(sb, "");
                L(sb, "/* " + Comment(plan.Member.ToString()) + " */");
                EmitProxyBody(sb, plan);
            }
            L(sb, "");
            L(sb, "}} /* namespace BasicLang::net */");
            return sb.ToString();
        }

        private static void EmitProxyBody(StringBuilder sb, SlotPlan plan)
        {
            var cppArgs = new List<string>();
            if (plan.HasReceiver) cppArgs.Add("const BasicLang::blnet::NetRef& self");
            foreach (var p in plan.Parameters)
                cppArgs.Add(p.ByRef
                    ? p.Wire.CppParamType + "& " + p.Name
                    : p.Wire.CppParamType + " " + p.Name);

            L(sb, "inline " + plan.Return.CppReturnType + " " + plan.SlotName
                  + "(" + (cppArgs.Count == 0 ? "" : string.Join(", ", cppArgs)) + ") {");
            L(sb, "    BlnetRequireSlot(g_net." + plan.SlotName + " != nullptr, \"" + plan.SlotName + "\");");

            // Temporaries for ByRef slots and for the result.
            foreach (var p in plan.Parameters.Where(p => p.ByRef))
                L(sb, "    " + p.Wire.CType + " " + p.Temp + " = " + ToWire(p.Wire, p.Name) + ";");
            switch (plan.Return.Kind)
            {
                case WireKind.Scalar: L(sb, "    " + plan.Return.CType + " blnet_result{};"); break;
                case WireKind.String: L(sb, "    char* blnet_result = nullptr;"); break;
                case WireKind.Handle: L(sb, "    uint64_t blnet_result = 0;"); break;
            }

            var callArgs = new List<string>();
            if (plan.HasReceiver) callArgs.Add("self.get()");
            foreach (var p in plan.Parameters)
                callArgs.Add(p.ByRef ? "&" + p.Temp : ToWire(p.Wire, p.Name));
            if (plan.Return.Kind != WireKind.Void) callArgs.Add("&blnet_result");

            L(sb, "    int32_t blnet_status;");
            L(sb, "    {");
            L(sb, "        /* §9.2: REQUIRED across every managed call. P0's thunk classifies a");
            L(sb, "           callback as cross-thread when g_call_depth == 0, so without this scope a");
            L(sb, "           result-bearing delegate fails BLNET_E_CROSS_THREAD_RESULT and an Action");
            L(sb, "           is silently queued instead of running inside the call. */");
            L(sb, "        BasicLang::blnet::BlnetCallScope blnet_scope;");
            L(sb, "        blnet_status = g_net." + plan.SlotName + "(" + string.Join(", ", callArgs) + ");");
            L(sb, "    }");
            L(sb, "    /* Outside the scope on purpose: NetCheck throws, and unwinding through the");
            L(sb, "       scope's destructor while it is still counted corrupts the depth counter. */");
            L(sb, "    BasicLang::blnet::NetCheck(blnet_status);");
            L(sb, "    /* §15.12: drain anything a foreign thread queued during the call, but only at");
            L(sb, "       the OUTERMOST boundary call — a nested proxy must not pump. */");
            L(sb, "    if (BasicLang::blnet::g_call_depth == 0) (void)BasicLang::blnet::blnet_pump();");

            foreach (var p in plan.Parameters.Where(p => p.ByRef))
                L(sb, "    " + p.Name + " = " + FromWire(p.Wire, p.Temp) + ";");

            switch (plan.Return.Kind)
            {
                case WireKind.Void:
                    break;
                case WireKind.Scalar:
                    L(sb, "    return " + FromWire(plan.Return, "blnet_result") + ";");
                    break;
                case WireKind.String:
                    L(sb, "    /* P0 string ownership: the callee handed back a blnet_alloc'd buffer and");
                    L(sb, "       the receiver frees it. */");
                    L(sb, "    std::string blnet_text;");
                    L(sb, "    if (blnet_result) {");
                    L(sb, "        blnet_text.assign(blnet_result);");
                    L(sb, "        if (BasicLang::blnet::g_shim.free_) BasicLang::blnet::g_shim.free_(blnet_result);");
                    L(sb, "    }");
                    L(sb, "    return blnet_text;");
                    break;
                case WireKind.Handle:
                    L(sb, "    /* §8.3: a returned reference type is born at refcount 1 and ownership");
                    L(sb, "       transfers to this NetRef; handle 0 means Nothing and stays empty. */");
                    L(sb, "    return BasicLang::blnet::NetRef(blnet_result);");
                    break;
            }
            L(sb, "}");
        }

        /// <summary>C++ value to wire value. Only <c>Boolean</c> actually converts.</summary>
        private static string ToWire(WireForm wire, string expression) =>
            wire.Kind == WireKind.Handle ? expression + ".get()"
            : wire.CppParamType == "bool" ? "(" + expression + " ? 1 : 0)"
            : expression;

        /// <summary>Wire value back to C++ value, for ByRef write-back and scalar returns.</summary>
        private static string FromWire(WireForm wire, string expression) =>
            wire.CppReturnType == "bool" ? expression + " != 0" : expression;

        // ------------------------------------------------------------------------------
        // blnet_startup.g.cpp — §9.3's startup contract.
        // ------------------------------------------------------------------------------

        private static string EmitStartup(
            NetSurface surface, IReadOnlyList<SlotPlan> plans, string shimModuleFileName)
        {
            var sb = new StringBuilder();
            Banner(sb, StartupFileName,
                "Loads the AOT-published shim, performs P0's ABI handshake, and fills both the",
                "core ShimApi and the generated proxy table (spec §9.3). THE ONLY translation",
                "unit in the generated .NET artifact set.");
            DeclaredTypesComment(sb, surface);
            L(sb, "/* BLNET_IMPLEMENT_LOADER must be defined BEFORE the first include of");
            L(sb, "   " + RuntimeHeaderFileName + ": it is #pragma once, and it hides the platform");
            L(sb, "   loader definitions (<windows.h> / <dlfcn.h>) behind this macro so they reach");
            L(sb, "   exactly this TU and never a TU holding generated BasicLang code. */");
            L(sb, "#define BLNET_IMPLEMENT_LOADER");
            L(sb, "#include \"" + ContractHeaderFileName + "\"");
            L(sb, "#include \"" + RuntimeHeaderFileName + "\"");
            L(sb, "#include \"" + BindingsFileName + "\"");
            L(sb, "");
            L(sb, "#include <cstdio>");
            L(sb, "#include <cstdlib>");
            L(sb, "#include <string>");
            L(sb, "");
            L(sb, "/* THE definition of the table " + BindingsFileName + " declares extern.");
            L(sb, "   Value-initialized: every slot is a null pointer until blnet_bind_all runs. */");
            L(sb, "BlnetProxyTable g_net{};");
            L(sb, "");
            L(sb, "namespace {");
            L(sb, "");
            L(sb, "const char* const kBlnetShimModule = \"" + CppStringLiteral(shimModuleFileName) + "\";");
            L(sb, "");
            L(sb, "/* §9.3: every startup failure writes ONE line to stderr and exits "
                  + StartupFailureExitCode.ToString(CultureInfo.InvariantCulture) + ".");
            L(sb, "   std::exit and NOT a throw, deliberately: this runs from a static initializer,");
            L(sb, "   where an escaping exception is std::terminate — an implementation-defined exit");
            L(sb, "   code plus a runtime banner on stderr — which cannot honor the normative");
            L(sb, "   (message, stream, exit code) contract the handshake tests assert on. */");
            L(sb, "[[noreturn]] void BlnetStartupFail(const std::string& message) {");
            L(sb, "    std::fflush(stdout);");
            L(sb, "    std::fprintf(stderr, \"%s\\n\", message.c_str());");
            L(sb, "    std::fflush(stderr);");
            L(sb, "    std::exit(" + StartupFailureExitCode.ToString(CultureInfo.InvariantCulture) + ");");
            L(sb, "}");
            L(sb, "");
            L(sb, "} /* anonymous namespace */");
            L(sb, "");
            L(sb, "void blnet_bind_all(void* module) {");
            if (plans.Count == 0)
                L(sb, "    (void)module; /* the surface contributed no callable members */");
            foreach (var plan in plans)
            {
                L(sb, "    g_net." + plan.SlotName + " = reinterpret_cast<decltype(g_net." + plan.SlotName + ")>(");
                L(sb, "        BasicLang::blnet::blnet_get_symbol(module, \"" + plan.SlotName + "\"));");
            }
            L(sb, "}");
            L(sb, "");
            L(sb, "void blnet_startup() {");
            L(sb, "    void* module = BasicLang::blnet::blnet_load_module(kBlnetShimModule);");
            L(sb, "    if (!module)");
            L(sb, "        BlnetStartupFail(std::string(\"blnet: failed to load '\") + kBlnetShimModule");
            L(sb, "                         + \"' (\" + BasicLang::blnet::blnet_load_error() + \")\");");
            L(sb, "");
            L(sb, "    if (const char* missing = BasicLang::blnet::blnet_bind_core(module))");
            L(sb, "        BlnetStartupFail(std::string(\"blnet: shim is missing export '\") + missing + \"'\");");
            L(sb, "");
            L(sb, "    const int32_t abi = BasicLang::blnet::g_shim.abi_version();");
            L(sb, "    if (abi != BLNET_ABI_VERSION)");
            L(sb, "        BlnetStartupFail(\"blnet: shim ABI \" + std::to_string(abi) + \", expected \"");
            L(sb, "                         + std::to_string(BLNET_ABI_VERSION));");
            L(sb, "");
            L(sb, "    /* Two arguments — P0's frozen signature is");
            L(sb, "       initialize(int32_t expected_abi, const BlnetNativeVtable*). */");
            L(sb, "    const int32_t status = BasicLang::blnet::g_shim.initialize(");
            L(sb, "        BLNET_ABI_VERSION, &BasicLang::blnet::g_native_vtable);");
            L(sb, "    if (status != BLNET_OK)");
            L(sb, "        BlnetStartupFail(\"blnet: initialize failed (status \" + std::to_string(status) + \")\");");
            L(sb, "");
            L(sb, "    blnet_bind_all(module);");
            L(sb, "}");
            L(sb, "");
            L(sb, "void blnet_shutdown() {");
            L(sb, "    /* Deliberately does NOT unload the shim and does NOT clear the tables.");
            L(sb, "");
            L(sb, "       Unloading is unsupported: the shim carries a Native-AOT .NET runtime, and a");
            L(sb, "       NetRef held by a static in another translation unit is destroyed AFTER this");
            L(sb, "       object (destruction order is the reverse of construction, and ours is built");
            L(sb, "       first by design) — its blnet_release would then call into freed code.");
            L(sb, "       Clearing g_shim instead would turn those late releases into silent leaks at");
            L(sb, "       a point where the process is about to exit anyway.");
            L(sb, "");
            L(sb, "       So this is a deliberate no-op that exists to OWN the shape: §9.5 puts");
            L(sb, "       shutdown in this object's destructor, and a later transport (P2b hosts the");
            L(sb, "       runtime itself and can shut it down properly) fills it in here rather than");
            L(sb, "       having to re-plumb the ownership. */");
            L(sb, "}");
            L(sb, "");
            L(sb, "namespace {");
            L(sb, "");
            L(sb, "/* §9.5 ownership: ONE static-initializer object covers both executable shapes —");
            L(sb, "   a BasicLang Sub Main the compiler emits main() for, and a user-written C++");
            L(sb, "   main() — without the user having to remember anything.");
            L(sb, "");
            L(sb, "   THE CONSTRAINT THIS BUYS: static initialization order across translation units");
            L(sb, "   is unspecified, so ANOTHER TU's static initializer must not call a .NET proxy.");
            L(sb, "   If one does, it may run before this object and find g_net still null. That is");
            L(sb, "   why the proxies guard their slot (§9.2): the violation surfaces as a named,");
            L(sb, "   readable error instead of a jump through a null function pointer. */");
            L(sb, "struct BlnetStartupGuard {");
            L(sb, "    BlnetStartupGuard()  { blnet_startup();  }");
            L(sb, "    ~BlnetStartupGuard() { blnet_shutdown(); }");
            L(sb, "};");
            L(sb, "");
            L(sb, "BlnetStartupGuard g_blnet_startup_guard;");
            L(sb, "");
            L(sb, "} /* anonymous namespace */");
            return sb.ToString();
        }

        // ------------------------------------------------------------------------------
        // Text helpers. Newlines are '\n' EXPLICITLY, never Environment.NewLine: Task 15's
        // content-hash cache keys on this text, and a hash that changed with the host OS
        // would produce a false miss on one machine and a false hit on another.
        // ------------------------------------------------------------------------------

        private static void L(StringBuilder sb, string line) => sb.Append(line).Append('\n');

        private static void Banner(StringBuilder sb, string fileName, params string[] description)
        {
            L(sb, "/* " + fileName + " — GENERATED by NetProxyEmitter. Do not edit; edits are lost on");
            L(sb, "   the next build (obj/gen is regenerated). Source of truth: BasicLang");
            L(sb, "   NetProxyEmitter.cs.");
            L(sb, " *");
            foreach (var line in description)
                L(sb, " * " + line);
            L(sb, " */");
        }

        private static void DeclaredTypesComment(StringBuilder sb, NetSurface surface)
        {
            if (surface.DeclaredTypeNames.Count == 0) return;
            L(sb, "/* <NetProxy> declared types (§7.2):");
            foreach (var name in surface.DeclaredTypeNames)
                L(sb, " *   " + Comment(name));
            L(sb, " */");
        }

        /// <summary>
        /// Makes text safe inside a C block comment. A .NET signature can legitimately contain
        /// a pointer type (<c>System.Byte*</c>), and one that lands next to a <c>/</c> would
        /// close the comment early and break the whole header.
        /// </summary>
        private static string Comment(string text) => text.Replace("*/", "* /");

        /// <summary>
        /// Escapes a file name for a C++ string literal — Windows paths carry backslashes, and
        /// an unescaped one before a <c>n</c> silently becomes a newline in the module name.
        /// </summary>
        private static string CppStringLiteral(string text) =>
            text.Replace("\\", "\\\\").Replace("\"", "\\\"");

        // ------------------------------------------------------------------------------

        private sealed class ParameterPlan
        {
            internal ParameterPlan(NetParameterDescriptor descriptor, WireForm wire, int index)
            {
                Descriptor = descriptor;
                Wire = wire;
                Name = "a" + index.ToString(CultureInfo.InvariantCulture);
                Temp = "blnet_" + Name;
            }

            internal NetParameterDescriptor Descriptor { get; }
            internal WireForm Wire { get; }
            internal string Name { get; }
            internal string Temp { get; }
            internal bool ByRef => Descriptor.RefKind != NetRefKind.None;
        }

        private sealed class SlotPlan
        {
            internal SlotPlan(
                string slotName, NetMemberDescriptor member, bool hasReceiver,
                WireForm returnWire, IReadOnlyList<ParameterPlan> parameters)
            {
                SlotName = slotName;
                Member = member;
                HasReceiver = hasReceiver;
                Return = returnWire;
                Parameters = parameters;
            }

            internal string SlotName { get; }
            internal NetMemberDescriptor Member { get; }
            internal bool HasReceiver { get; }
            internal WireForm Return { get; }
            internal IReadOnlyList<ParameterPlan> Parameters { get; }
        }
    }
}
