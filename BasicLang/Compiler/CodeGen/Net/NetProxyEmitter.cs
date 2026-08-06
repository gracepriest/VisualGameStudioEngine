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
    /// </list>
    ///
    /// <para><b>§8.6's outbound array copy (P2a-2 Task 10) is NOT in that list — it is emitted
    /// here.</b> <see cref="NetArrayCopy.RequiredForms"/> yields one element wire form per array
    /// type in the surface's signatures, and each contributes two ORDINARY proxy-table slots
    /// (<c>bl_net_array_new_…</c>, <c>bl_net_array_read_…</c>) plus their typed C++ proxies. They
    /// are ordinary slots on purpose: as a side channel they would have been exempt from §12.4's
    /// slots-≡-exports set comparison, which is the one invariant holding the two halves of this
    /// boundary together. <see cref="NetShimGenerator"/> derives its export set from the SAME
    /// function, so the two agree by construction.</para>
    /// </summary>
    internal static class NetProxyEmitter
    {
        /// <summary>P0's C contract header — spliced verbatim from <see cref="BlnetRuntimeSources"/>.</summary>
        internal const string ContractHeaderFileName = "blnet.h";

        /// <summary>P0's header-only native runtime — spliced verbatim from <see cref="BlnetRuntimeSources"/>.</summary>
        internal const string RuntimeHeaderFileName = "blnet_runtime.hpp";

        /// <summary>
        /// Spec §6.4's conversion pairs, native side — spliced verbatim from
        /// <see cref="CppNetMarshal"/>. Constant text (hence no <c>.g.</c> infix, like the two P0
        /// headers), but SURFACE-KEYED like everything here: the converters only have call sites
        /// inside proxy invocations, and carrying them in this set — rather than the
        /// always-emitted runtime preamble — is what keeps the flip's emission-identity claim
        /// intact for programs with no .NET surface. NOT included by the other artifacts: it
        /// requires the P1 BCL types, which only a generated program TU has in scope
        /// (include-order contract on <see cref="CppNetMarshal"/>).
        /// </summary>
        internal const string MarshalHeaderFileName = "blnet_marshal.hpp";

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
                [MarshalHeaderFileName] = CppNetMarshal.GuardedSource,
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
                plans.Select(p => p.SlotName)
                    .Concat(NetArrayCopy.RequiredExportNames(surface))
                    .ToList());
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
                var wire = WireOf(p);
                if (p.RefKind != NetRefKind.None && wire.Kind != WireKind.Scalar
                    && !NetArrayCopy.TryGetFormForArray(p.TypeFullName, out _))
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
                // The ONE widening, and it is specified: §8.6's `ref`/`out` array slot. The
                // ownership objection above does not apply, because the caller MINTED the
                // incoming handle itself (bl_net_array_new_… copies a std::vector in at
                // refcount 1) rather than borrowing one the callee might hand straight back.
                // Writing a new handle over it therefore releases something this frame owns,
                // which is the definition of well-defined. §8.6 also makes ref/out the EXEMPTION
                // from §14.11's one-way divergence, so the write-back is not an optimisation —
                // the whole row exists to carry it.
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

        private enum WireKind { Void, Scalar, String, Handle, Callback }

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
            /// §8.4's delegate parameter. The WIRE is identical to <see cref="Handle"/> — a
            /// callback handle is a <c>uint64_t</c> like an object handle — so the C signature
            /// this contributes is byte-identical to the handle row's and
            /// <c>ExportSignaturesMatchTheProxyTableSlotSignatures</c> keeps comparing equal.
            /// Only the C++ SPELLING differs.
            ///
            /// <para>⛔ <b>It must not be spelled <c>NetRef</c>, and that is a correctness rule,
            /// not a style one.</b> <c>NetRef</c>'s deleter routes to <c>g_shim.release</c> — the
            /// MANAGED object table. A callback handle lives in the NATIVE callback table
            /// (<c>detail::g_callbacks</c>) and is freed by <c>blnet_callback_release</c>.
            /// Wrapping one in a <c>NetRef</c> would release an unrelated managed object and
            /// leave the callback entry alive: a wrong-table release, not a leak. (It also would
            /// not compile implicitly — <c>explicit NetRef(std::uint64_t)</c> — which is the
            /// only reason the mistake is not already latent.)</para>
            ///
            /// <para>Ownership of the callback belongs to <c>CppCodeGenerator</c>'s RAII guard in
            /// the call prologue (D-P8), NOT to this parameter: the proxy only carries the
            /// handle across.</para>
            /// </summary>
            /// <para>⛔ <b><c>blnet_callback</c> is spelled UNQUALIFIED, and that is not a style
            /// choice.</b> It is a C typedef in <c>blnet.h</c> — <c>typedef uint64_t
            /// blnet_callback;</c> — at GLOBAL scope, not a member of the <c>BasicLang::blnet</c>
            /// C++ namespace that <c>blnet_runtime.hpp</c> opens. Qualifying it is
            /// <c>error C2039: 'blnet_callback': is not a member of 'BasicLang::blnet'</c>.
            /// Caught by Task 11's run-level proof, NOT by any emission assertion: a test that
            /// checks the generated text contains the spelling you chose cannot tell you the
            /// spelling compiles.</para>
            internal static readonly WireForm Callback = new(
                WireKind.Callback, "uint64_t", "uint64_t*",
                "blnet_callback", "blnet_callback");

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
        /// <para><b>The other two encodings of these rows, and what ties each to this one.</b>
        /// <c>NetShimGenerator.WireOf</c> is the C# column — same rows, same ORDER, compared
        /// signature-for-signature by
        /// <c>NetShimGeneratorTests.ExportSignaturesMatchTheProxyTableSlotSignatures</c> over
        /// <c>NetProxyEmitterTests.WireShapeSurface</c>, so a row missing from that fixture is a
        /// row the comparison is blind to. <c>NetMarshalTable.WireRows</c> is the CALL SITE's
        /// column (BasicLang spelling, §6.4 converter names, and the <c>CWire</c> a ByRef
        /// temporary is declared with); <c>NetConversionPairTests</c> ties it to
        /// <c>blnet_marshal.hpp</c>.</para>
        ///
        /// <para><b>What may ARRIVE here is decided elsewhere</b>, by
        /// <c>NetSurfaceCollector.FirstUnmarshalable</c> — the §7.2 admissibility filter. A type
        /// it rejects (a pointer, an open type parameter, <c>Object</c>, a <c>ref struct</c>)
        /// never reaches a slot at all, which is why this table has no arm for any of them and
        /// must not grow one: a wire form here would silently re-admit a type the collector
        /// refused.</para>
        ///
        /// <para><c>Char</c> travels as its .NET width, a <c>uint16_t</c> UTF-16 code unit.
        /// §8.3's shipped divergence — BasicLang's native <c>Char</c> is ONE byte, so inbound
        /// values above U+00FF narrow lossily — belongs to the lowering in
        /// <c>CppCodeGenerator</c>, not here: truncating at the boundary would destroy the value
        /// before the compiler ever got the chance to diagnose it.</para>
        /// </summary>
        /// <summary>
        /// A PARAMETER's wire form. Delegate-ness cannot be recovered from a type name — the
        /// surface carries it (D-P9) — so this overload exists and the name-keyed one below is
        /// reached only for non-delegate parameters and for RETURNS.
        ///
        /// <para>Returns deliberately stay on the name-keyed path: §8.4 / spec D6 scopes P2a to
        /// delegate ARGUMENTS, so a member returning a delegate keeps the handle row it has
        /// always had, and <see cref="WireKind.Callback"/> can never appear in a return position.
        /// That is what keeps the result-shaped switches below correct without a Callback arm.
        /// </para>
        /// </summary>
        private static WireForm WireOf(NetParameterDescriptor parameter) =>
            parameter != null && parameter.IsDelegate
                ? Wire.Callback
                : WireOf(parameter?.TypeFullName);

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
            // §6.4 SINGLE-SLOT conversion pairs (Task 7a): the P1 native values must never
            // become handles, and these two have one-scalar wire forms. The proxy carries the
            // RAW wire scalar — the CALL SITE converts through blnet_marshal.hpp's
            // to_net_datetime/from_net_datetime (etc.), because this header is deliberately
            // include-free of the P1 types (the marshal header's include-order contract).
            // Decimal (4 scalars), Guid (16 bytes), DateTimeOffset (the DECLARED scalar
            // pair) and StringBuilder (directional String) need multi-slot/directional
            // machinery — Task 8's §8.3 drift completion; until then they stay in the handle
            // row here AND the analyzer refuses them at resolved call sites, so no BL call
            // can reach a handle-shaped §6.4 slot.
            "System.DateTime" => Wire.Scalar("uint64_t"),
            "System.TimeSpan" => Wire.Scalar("int64_t"),
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
            foreach (var form in NetArrayCopy.RequiredForms(surface))
            {
                L(sb, "    /* §8.6 outbound copy for " + Comment(form.ArrayFullName) + " */");
                L(sb, "    int32_t (BLNET_CALL *" + form.NewExportName + ")(int32_t count, "
                      + form.CSourcePointer + " src, uint64_t* result);");
                L(sb, "    int32_t (BLNET_CALL *" + form.ReadExportName + ")(uint64_t self, "
                      + "int32_t capacity, " + form.CWireOut + "* dst, int32_t* result);");
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
            // §8.6's copy proxies take and return std::vector — the native representation of a
            // BasicLang array (CppCodeGenerator.MapType). Unconditional rather than gated on the
            // helper set: this header is not part of the emission-identity claim (it only exists
            // for a NON-empty surface at all), and a conditional include is a way for a
            // helper-free surface to compile while a helper-bearing one does not.
            L(sb, "#include <vector>");
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
            foreach (var form in NetArrayCopy.RequiredForms(surface))
                EmitArrayCopyProxies(sb, form);
            L(sb, "");
            L(sb, "}} /* namespace BasicLang::net */");
            return sb.ToString();
        }

        // ------------------------------------------------------------------------------
        // §8.6 — the outbound array copy proxies.
        // ------------------------------------------------------------------------------

        /// <summary>
        /// The two typed C++ proxies for one element wire form. They take and return
        /// <c>std::vector&lt;native&gt;</c> — the native representation of a BasicLang array —
        /// so a call site emits exactly ONE line per copy and every awkward detail lives here,
        /// generated once, instead of being re-emitted per call site:
        ///
        /// <list type="bullet">
        /// <item><description><b>The staging buffer.</b> <c>std::vector&lt;bool&gt;</c> is the C++
        /// BITSET specialization and has no <c>.data()</c> at all, and <c>Char</c>/<c>String</c>
        /// convert per element. Staging unconditionally keeps ONE shape for thirteen rows; for
        /// the ten where native and wire coincide it is a plain copy.</description></item>
        /// <item><description><b>Two calls for a readback, and the ORDER of the second one's
        /// steps.</b> The length is not knowable in advance (an <c>out</c> slot's callee decides
        /// it), so the first call probes with a null buffer. In the second, the convert-and-free
        /// loop runs BEFORE <c>NetCheckTyped</c> — for <c>String</c> the buffer holds
        /// <c>blnet_alloc</c>'d elements, and checking first would leak every one of them on the
        /// managed-exception path. Zero-initialising the buffer is what makes running the loop
        /// on a failed call safe.</description></item>
        /// <item><description><b>The empty-array guard.</b> <c>.data()</c> on an empty vector may
        /// legitimately be null, and passing count 0 with a null pointer is the shape the shim
        /// expects — spelled explicitly rather than relying on it.</description></item>
        /// </list>
        ///
        /// <para>Both bodies carry §9.2's full protocol — slot guard, <c>BlnetCallScope</c>,
        /// <c>NetCheckTyped</c> outside the scope, depth-0 <c>blnet_pump</c> — because they ARE
        /// boundary calls. A copy helper that skipped the scope would misdispatch callbacks
        /// exactly as a member proxy would.</para>
        /// </summary>
        private static void EmitArrayCopyProxies(StringBuilder sb, NetArrayCopyForm form)
        {
            var newName = form.NewExportName;
            var readName = form.ReadExportName;
            var vector = "std::vector<" + form.NativeElement + ">";

            L(sb, "");
            L(sb, "/* §8.6 copy-IN: a native " + vector + " becomes a managed "
                  + Comment(form.ArrayFullName) + " handle. ONE-WAY for a by-value argument");
            L(sb, "   (§14.11) — the callee's mutations land in the managed copy, not here. */");
            L(sb, "inline BasicLang::blnet::NetRef " + newName + "(const " + vector + "& src) {");
            L(sb, "    BlnetRequireSlot(g_net." + newName + " != nullptr, \"" + newName + "\");");
            L(sb, "    std::vector<" + form.CWireIn + "> blnet_buf;");
            L(sb, "    blnet_buf.reserve(src.size());");
            L(sb, "    for (const auto& blnet_e : src)");
            L(sb, "        blnet_buf.push_back(" + string.Format(CultureInfo.InvariantCulture,
                      form.CppToWire, "blnet_e") + ");");
            L(sb, "    uint64_t blnet_result = 0;");
            L(sb, "    int32_t blnet_status;");
            L(sb, "    {");
            L(sb, "        BasicLang::blnet::BlnetCallScope blnet_scope;");
            L(sb, "        blnet_status = g_net." + newName + "(");
            L(sb, "            static_cast<int32_t>(blnet_buf.size()),");
            L(sb, "            blnet_buf.empty() ? nullptr : blnet_buf.data(), &blnet_result);");
            L(sb, "    }");
            L(sb, "    BasicLang::blnet::NetCheckTyped(blnet_status);");
            L(sb, "    if (BasicLang::blnet::g_call_depth == 0) (void)BasicLang::blnet::blnet_pump();");
            L(sb, "    /* §8.3: a returned reference type is born at refcount 1 and ownership");
            L(sb, "       transfers to this NetRef — which is the call site's RAII release slot. */");
            L(sb, "    return BasicLang::blnet::NetRef(blnet_result);");
            L(sb, "}");

            L(sb, "");
            L(sb, "/* §8.6 readback: the managed " + Comment(form.ArrayFullName)
                  + " behind `self` becomes a fresh " + vector + ". */");
            L(sb, "inline " + vector + " " + readName + "(const BasicLang::blnet::NetRef& self) {");
            L(sb, "    BlnetRequireSlot(g_net." + readName + " != nullptr, \"" + readName + "\");");
            L(sb, "    int32_t blnet_len = 0;");
            L(sb, "    int32_t blnet_status;");
            L(sb, "    {");
            L(sb, "        /* Length probe: capacity 0 / null buffer writes nothing and reports the");
            L(sb, "           array's real length, which an `out` slot's caller cannot know. */");
            L(sb, "        BasicLang::blnet::BlnetCallScope blnet_scope;");
            L(sb, "        blnet_status = g_net." + readName + "(self.get(), 0, nullptr, &blnet_len);");
            L(sb, "    }");
            L(sb, "    BasicLang::blnet::NetCheckTyped(blnet_status);");
            L(sb, "    " + vector + " blnet_out;");
            L(sb, "    if (blnet_len > 0) {");
            L(sb, "        /* Value-initialised, so the loop below is safe to run even when the");
            L(sb, "           second call failed part-way through. */");
            L(sb, "        std::vector<" + form.CWireOut + "> blnet_buf(static_cast<size_t>(blnet_len));");
            L(sb, "        {");
            L(sb, "            BasicLang::blnet::BlnetCallScope blnet_scope;");
            L(sb, "            blnet_status = g_net." + readName
                  + "(self.get(), blnet_len, blnet_buf.data(), &blnet_len);");
            L(sb, "        }");
            L(sb, "        blnet_out.reserve(blnet_buf.size());");
            L(sb, "        for (auto& blnet_e : blnet_buf) {");
            L(sb, "            blnet_out.push_back(" + string.Format(CultureInfo.InvariantCulture,
                      form.CppFromWire, "blnet_e") + ");");
            if (form.ReadElementIsOwned)
            {
                L(sb, "            /* P0 string ownership, per element: the shim handed back");
                L(sb, "               blnet_alloc'd buffers and the receiver frees them. Freed HERE,");
                L(sb, "               before NetCheckTyped, so a managed exception on the second call");
                L(sb, "               cannot leak the elements it did manage to write. */");
                L(sb, "            if (blnet_e && BasicLang::blnet::g_shim.free_)");
                L(sb, "                BasicLang::blnet::g_shim.free_(blnet_e);");
            }
            L(sb, "        }");
            L(sb, "        BasicLang::blnet::NetCheckTyped(blnet_status);");
            L(sb, "    }");
            L(sb, "    if (BasicLang::blnet::g_call_depth == 0) (void)BasicLang::blnet::blnet_pump();");
            L(sb, "    return blnet_out;");
            L(sb, "}");
        }

        private static void EmitProxyBody(StringBuilder sb, SlotPlan plan)
        {
            var cppArgs = new List<string>();
            if (plan.HasReceiver) cppArgs.Add("const BasicLang::blnet::NetRef& self");
            foreach (var p in plan.Parameters)
                cppArgs.Add(p.ByRef
                    ? CppByRefParamType(p.Wire) + " " + p.Name
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
            L(sb, "    /* Outside the scope on purpose: NetCheckTyped throws, and unwinding through");
            L(sb, "       the scope's destructor while it is still counted corrupts the depth");
            L(sb, "       counter. Typed (§11.1): a managed failure arrives as BasicLang::NetException");
            L(sb, "       carrying the shim-reported inheritance chain, so BL typed Catch matches. */");
            L(sb, "    BasicLang::blnet::NetCheckTyped(blnet_status);");
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

        /// <summary>
        /// The C++ spelling of a BY-REFERENCE parameter. Not <c>CppParamType + "&amp;"</c> for
        /// the handle row: that is <c>const NetRef&amp;</c>, and appending another <c>&amp;</c>
        /// yields <c>const NetRef&amp; &amp;</c> — reference collapsing makes it
        /// <c>const NetRef&amp;</c> again, so the parameter would silently stay READ-ONLY and the
        /// §8.6 write-back would fail to compile (or, worse, bind a temporary). §8.6's ref/out
        /// array row needs a mutable <c>NetRef&amp;</c>.
        /// </summary>
        private static string CppByRefParamType(WireForm wire) =>
            wire.Kind == WireKind.Handle
                ? "BasicLang::blnet::NetRef&"
                : wire.CppParamType + "&";

        /// <summary>C++ value to wire value. Only <c>Boolean</c> actually converts.</summary>
        private static string ToWire(WireForm wire, string expression) =>
            wire.Kind == WireKind.Handle ? expression + ".get()"
            : wire.CppParamType == "bool" ? "(" + expression + " ? 1 : 0)"
            : expression;

        /// <summary>
        /// Wire value back to C++ value, for ByRef write-back and scalar returns. The handle arm
        /// is reached only by §8.6's ref/out array row: assigning a fresh <c>NetRef</c> over the
        /// parameter releases the handle this frame minted and takes ownership of whatever the
        /// callee left in the slot — including the SAME array re-handled, which is two table
        /// references to one object and therefore two independent releases, not a double free.
        /// </summary>
        private static string FromWire(WireForm wire, string expression) =>
            wire.Kind == WireKind.Handle ? "BasicLang::blnet::NetRef(" + expression + ")"
            : wire.CppReturnType == "bool" ? expression + " != 0"
            : expression;

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
            var arrayHelpers = NetArrayCopy.RequiredExportNames(surface);
            L(sb, "void blnet_bind_all(void* module) {");
            if (plans.Count == 0 && arrayHelpers.Count == 0)
                L(sb, "    (void)module; /* the surface contributed no callable members */");
            foreach (var slot in plans.Select(p => p.SlotName).Concat(arrayHelpers))
            {
                L(sb, "    g_net." + slot + " = reinterpret_cast<decltype(g_net." + slot + ")>(");
                L(sb, "        BasicLang::blnet::blnet_get_symbol(module, \"" + slot + "\"));");
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
