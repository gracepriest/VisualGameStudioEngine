using System;
using System.Collections.Generic;
using System.Globalization;
using BasicLang.Compiler.IR;
using BasicLang.Net;

namespace BasicLang.Compiler.CodeGen.CPlusPlus
{
    /// <summary>
    /// P2a-2 Task 7a — resolved-call lowering to <c>g_net</c> proxies (spec §8.2/§8.3, §9.2).
    ///
    /// <para><b>What lowers.</b> An IR node carrying a non-null, EXACT
    /// <c>ResolvedNetTarget</c> whose receiver category is not natively handled becomes a
    /// call to the typed inline proxy in <c>blnet_proxies.g.hpp</c> — the proxy's name IS
    /// <see cref="NetNameMangler.Mangle"/> of the descriptor (one name, three places:
    /// proxy-table slot, shim export, C++ proxy function; §7.3/§12.4). Static call →
    /// receiver-less; instance call → the receiver <c>NetRef</c> first; <c>New</c> → the
    /// ctor proxy returning a fresh handle into the declared <c>NetRef</c>; property/field
    /// READ → the getter-shaped property slot; WRITE → the synthesized <c>set_X</c>
    /// accessor slot (stamped by <c>IRBuilder</c> from <see cref="NetAccessorSynthesis"/>).</para>
    ///
    /// <para><b>THE NAME-ONLY GATE (the plan's mandatory blockquote).</b> A descriptor with
    /// <c>ResolvedNetTargetIsExact == false</c> was matched by NAME only (first name match in
    /// metadata order — the Task-2 recording) and is NEVER lowered: it could be the wrong
    /// overload, and silently calling it is a miscompile, not a fallback. The analyzer
    /// reports the unprobeable shapes at their positions first (native errors); this gate is
    /// the defense-in-depth layer and throws a refusal instead.</para>
    ///
    /// <para><b>Refusals are loud, never silent.</b> Every §8.3 shape this task cannot carry
    /// (ref/out slots, enum wire, the multi-slot §6.4 pairs, native values against handle
    /// slots, unrepresentable results) throws <see cref="CppCapabilityException"/> with a
    /// BL-coded, fix-teaching message; <c>CppProjectBuilder</c> maps it onto the build
    /// result (D-P3's residual positionless channel — the analyzer catches the common
    /// shapes with positions before codegen ever runs).</para>
    /// </summary>
    public partial class CppCodeGenerator
    {
        /// <summary>
        /// True when THE WHOLE COMPILATION draws a non-empty .NET surface — decided by the
        /// SAME walk the phase-3 collector uses (<see cref="NetSurfaceCollector"/>), so the
        /// header includes and the emitted proxy artifacts can never disagree about whether
        /// the boundary exists. Stays false for every existing program (the standing
        /// inertness rule).
        ///
        /// <para><b>Granularity is PROJECT-WIDE, not per-module</b>, and deliberately so:
        /// <see cref="DetectNetSurface"/> runs once over the COMBINED module (both emission
        /// modes), while <see cref="EmitNetBoundaryIncludes"/> runs per emitted header. Split
        /// emission has exactly one aggregate header anyway, so no per-module header is
        /// affected; and matching the collector's input exactly is what keeps "the includes
        /// exist iff the artifacts exist" true — a per-module recomputation could answer
        /// false for a module that references a .NET-typed value produced elsewhere.</para>
        /// </summary>
        private bool _moduleUsesNetSurface;

        private void DetectNetSurface(IRModule module)
        {
            _moduleUsesNetSurface = module != null && NetSurfaceCollector.Collect(
                new[] { module },
                project: null,
                resolverFactory: null,
                diagnostics: new List<NetReferenceDiagnostic>()).IsNonEmpty;
        }

        /// <summary>
        /// The two boundary includes for a surface-drawing module, in the order the
        /// include-order contract requires: the proxies header is self-contained; the §6.4
        /// marshal header REQUIRES the P1 splices already in scope, which both emission
        /// modes guarantee by emitting these AFTER the runtime preamble.
        /// </summary>
        private void EmitNetBoundaryIncludes()
        {
            if (!_moduleUsesNetSurface) return;
            WriteLine("// P2a §9.1: the .NET boundary artifacts (present because this module's");
            WriteLine("// collected surface is non-empty; emitted into obj/gen by NetProxyEmitter).");
            WriteLine("#include \"blnet_proxies.g.hpp\"");
            WriteLine("#include \"blnet_marshal.hpp\"");
            WriteLine();
        }

        // ------------------------------------------------------------------------------
        // The lowering arms (called first thing by the Visit methods).
        // ------------------------------------------------------------------------------

        private static bool IsNativelyHandledCategory(BoundaryTypeCategory category) =>
            category == BoundaryTypeCategory.NativeOwned
            || category == BoundaryTypeCategory.Bridged;

        /// <summary>Shared entry: false = not a .NET-lowered node, caller keeps its legacy path.</summary>
        private bool TryLowerNetInvocation(
            IRValue resultNode,
            NetMemberDescriptor target,
            bool exact,
            BoundaryTypeCategory category,
            IRValue receiver,
            IReadOnlyList<IRValue> arguments)
        {
            if (target == null) return false;
            if (IsNativelyHandledCategory(category)) return false;

            RequireExactNetTarget(target, exact);

            var expression = BuildNetProxyCall(target, receiver, arguments);
            EmitNetResult(resultNode, target, expression);
            return true;
        }

        /// <summary>The name-only gate. See the class remarks.</summary>
        private static void RequireExactNetTarget(NetMemberDescriptor target, bool exact)
        {
            if (exact) return;
            throw NetLoweringRefusal("BL6017",
                $".NET member '{target.DeclaringTypeFullName}.{target.Name}' was resolved by "
                + "name only — its overload identity is unverified, and lowering it could call "
                + "the wrong overload (the Task-7a name-only gate; a silent lowering here would "
                + "be a miscompile). The analyzer normally reports this shape at its source "
                + "position; reaching this guard means a call shape bypassed the overload probe.");
        }

        /// <summary>
        /// A lowering refusal, carrying its §11.4 <paramref name="code"/> STRUCTURALLY —
        /// <c>CppProjectBuilder</c> reports <see cref="CppCapabilityException.DiagnosticCode"/>,
        /// so the message text must NOT repeat it (that mismatch is what made users see BL6001
        /// over BL6019 text).
        /// </summary>
        private static CppCapabilityException NetLoweringRefusal(string code, string message) =>
            new CppCapabilityException(new List<string> { message }, code);

        /// <summary>
        /// The proxy-call expression: <c>BasicLang::net::&lt;mangled&gt;(receiver?, args…)</c>.
        /// Marshaling per §8.3 happens argument-by-argument here; the proxy converts nothing
        /// the call site can express (it stays include-free of the P1 types).
        /// </summary>
        private string BuildNetProxyCall(
            NetMemberDescriptor target, IRValue receiver, IReadOnlyList<IRValue> arguments)
        {
            var targetDisplay = target.DeclaringTypeFullName + "."
                + (target.Kind == NetMemberCategory.Constructor ? "New" : target.Name);

            var parameters = target.Parameters;
            if (arguments.Count != parameters.Count)
            {
                throw NetLoweringRefusal("BL6019",
                    $"'{targetDisplay}' resolved with "
                    + parameters.Count.ToString(CultureInfo.InvariantCulture)
                    + " declared parameter(s) but the call site supplies "
                    + arguments.Count.ToString(CultureInfo.InvariantCulture)
                    + " argument(s) — optional-parameter and params binding is not lowered at "
                    + "the native boundary (spec §8.3). Pass every declared argument "
                    + "explicitly.");
            }

            var callArgs = new List<string>(parameters.Count + 1);

            var hasReceiver = !target.IsStatic && target.Kind != NetMemberCategory.Constructor;
            if (hasReceiver)
            {
                // VB's shared-through-instance leniency means an INSTANCE-SHAPED node can
                // carry a static winner (handled above by hasReceiver = false — the receiver
                // expression is a variable reference with no effects to lose). Here the
                // winner is an instance member: the receiver must be a handle.
                // INTERNAL INVARIANT, not a user-facing failure mode: IRBuilder routes by the
                // descriptor's static-ness (its C1 fix), so an instance member always arrives
                // with its receiver. If this ever fires it is a compiler bug — refusing is
                // still right (a receiver-less instance proxy call would not compile), but the
                // shape to fix is the ROUTING, never the user's program.
                if (receiver == null)
                    throw NetLoweringRefusal("BL6017",
                        $"internal: instance member '{targetDisplay}' reached the lowering with "
                        + "no receiver value. This is a compiler routing defect (IRBuilder's "
                        + "static-vs-instance call arm), not a problem with the program.");
                if (!IsNetRefBacked(receiver))
                    throw NetLoweringRefusal("BL6019",
                        $"the receiver of '{targetDisplay}' (BasicLang type "
                        + $"'{receiver.Type?.Name}') is not a handle-backed .NET value — only "
                        + "ManagedOwned-typed values (Regex/Uri/Stream/FileInfo/DirectoryInfo) "
                        + "can receive .NET instance calls under P2a.");
                callArgs.Add(GetValueName(receiver));
            }

            for (var i = 0; i < parameters.Count; i++)
                callArgs.Add(MarshalNetArgument(targetDisplay, parameters[i], i, arguments[i]));

            return "BasicLang::net::" + NetNameMangler.Mangle(target)
                   + "(" + string.Join(", ", callArgs) + ")";
        }

        /// <summary>True when the value's declared type lowers to <c>BasicLang::NetRef</c>.</summary>
        private bool IsNetRefBacked(IRValue value) =>
            value?.Type != null
            && string.Equals(MapType(value.Type), "BasicLang::NetRef", StringComparison.Ordinal);

        /// <summary>
        /// One argument, marshaled per its DECLARED parameter's §8.3 row. The rows this task
        /// carries: primitives by value; Boolean as C++ <c>bool</c> (the proxy converts to
        /// int32); Char zero-extended to its UTF-16 wire width (§8.3/§14.10); String as a
        /// borrowed UTF-8 <c>const char*</c> (literals are already <c>const char*</c>;
        /// String values borrow via <c>.c_str()</c> — the proxy copies during the call, so
        /// the borrow never outlives the full-expression); DateTime/TimeSpan through Task
        /// 6's converters; <c>Nothing</c> as the 0-handle / null pointer (§8.2 — never
        /// Table-reaching); a handle slot takes a <c>NetRef</c>-backed value. Everything
        /// else refuses with the §8.3 row that owns it.
        /// </summary>
        private string MarshalNetArgument(
            string targetDisplay, NetParameterDescriptor parameter, int index, IRValue argument)
        {
            var position = (index + 1).ToString(CultureInfo.InvariantCulture);

            if (parameter.RefKind != NetRefKind.None)
                throw NetLoweringRefusal("BL6019",
                    $"'{targetDisplay}': parameter {position} ('{parameter}') is passed "
                    + $"{parameter.RefKind.ToString().ToLowerInvariant()} — ref/out parameters "
                    + "have no wire form at the native boundary yet (spec §8.3 pointer slots).");

            var isNullConstant = argument is IRConstant { Value: null };
            var paramType = parameter.TypeFullName;

            switch (paramType)
            {
                case "System.String":
                    if (isNullConstant)
                        return "nullptr";   // §8.2: Nothing crosses as the null wire form
                    if (argument is IRConstant { Value: string } stringConstant)
                        return EmitConstant(stringConstant);   // a literal IS a const char*
                    if (string.Equals(argument.Type?.Name, "String", StringComparison.OrdinalIgnoreCase))
                        return "(" + GetValueName(argument) + ").c_str()";
                    throw NetLoweringRefusal("BL6019",
                        $"'{targetDisplay}': argument {position} (BasicLang type "
                        + $"'{argument.Type?.Name}') cannot cross a String slot — pass a "
                        + "String value or literal (spec §8.3).");

                case "System.Boolean":
                    return GetValueName(argument);   // proxy parameter is bool

                case "System.Char":
                    // §8.3: outbound zero-extends the 1-byte native Char onto the UTF-16
                    // wire (the §14.10 divergence lives on the INBOUND side).
                    return "static_cast<uint16_t>(static_cast<unsigned char>("
                           + GetValueName(argument) + "))";

                case "System.DateTime":
                    return "BasicLang::net::to_net_datetime(" + GetValueName(argument) + ")";

                case "System.TimeSpan":
                    return "BasicLang::net::to_net_timespan(" + GetValueName(argument) + ")";
            }

            if (NetMarshalTable.MultiSlotConversionPairs.Contains(paramType))
                throw NetLoweringRefusal("BL6019",
                    $"'{targetDisplay}': parameter {position} has type '{paramType}', whose "
                    + "§6.4 wire form is not a single slot — it is not lowered at the native "
                    + "boundary yet.");

            if (NetMarshalTable.IsSingleSlotValue(paramType))
                return GetValueName(argument);   // numeric scalars pass by value

            // Everything else in §8.3 is a HANDLE slot: Nothing crosses as handle 0, and a
            // NetRef-backed value passes through (genuinely free). A NATIVE value here —
            // an array against byte[], a std::string against Object, a collection against
            // IEnumerable — has no handle to pass: §8.6's outbound copy is Task 10.
            // ONE spelling everywhere the generator names this type: `BasicLang::NetRef`,
            // matching MapType's declaration form. `BasicLang::blnet::NetRef` is the same type
            // (the blnet namespace re-exports it with a using-declaration), but two spellings
            // for one type in generated code is a reader trap.
            if (isNullConstant)
                return "BasicLang::NetRef()";
            if (IsNetRefBacked(argument))
                return GetValueName(argument);

            throw NetLoweringRefusal("BL6019",
                $"'{targetDisplay}': argument {position} (BasicLang type "
                + $"'{argument.Type?.Name}') is not a handle-backed .NET value, but parameter "
                + $"{position} has reference type '{paramType}' — a native value crossing "
                + "into a .NET reference slot is §8.6 outbound-copy territory, which is not "
                + "lowered at the native boundary yet. Pass Nothing or a ManagedOwned-typed "
                + "value.");
        }

        /// <summary>
        /// Emits the statement carrying the proxy call: a bare statement for void, otherwise
        /// an assignment into the node's pre-declared destination, wrapping the §8.3 return
        /// row (Boolean/scalars direct; Char narrowed — the documented §14.10 lossy inbound
        /// divergence; String is <c>std::string</c> which IS <c>BasicLang::String</c>;
        /// DateTime/TimeSpan through the inverse converters; handles into a
        /// <c>NetRef</c>-typed destination). Results this task cannot represent refuse.
        /// </summary>
        private void EmitNetResult(IRValue resultNode, NetMemberDescriptor target, string expression)
        {
            var targetDisplay = target.DeclaringTypeFullName + "."
                + (target.Kind == NetMemberCategory.Constructor ? "New" : target.Name);

            // Constructors return the created object's handle (§8.2), not their metadata
            // System.Void.
            var resultType = target.Kind == NetMemberCategory.Constructor
                ? target.DeclaringTypeFullName
                : target.TypeFullName;

            if (string.Equals(resultType, "System.Void", StringComparison.Ordinal))
            {
                WriteLine(expression + ";");
                return;
            }

            var destination = GetValueName(resultNode);

            switch (resultType)
            {
                case "System.DateTime":
                    WriteLine($"{destination} = BasicLang::net::from_net_datetime({expression});");
                    return;
                case "System.TimeSpan":
                    WriteLine($"{destination} = BasicLang::net::from_net_timespan({expression});");
                    return;
                case "System.Char":
                    // §14.10 shipped divergence: a code unit above U+00FF cannot fit the
                    // 1-byte native Char — the narrowing is lossy and documented.
                    WriteLine($"{destination} = static_cast<char>({expression});");
                    return;
            }

            if (NetMarshalTable.MultiSlotConversionPairs.Contains(resultType))
                throw NetLoweringRefusal("BL6019",
                    $"'{targetDisplay}' returns '{resultType}', whose §6.4 wire form is not a "
                    + "single slot — it is not lowered at the native boundary yet.");

            if (NetMarshalTable.IsSingleSlotValue(resultType))
            {
                // Boolean, the numeric scalars, String (std::string IS BasicLang::String on
                // this backend — the proxy already copied out of the transfer buffer and
                // freed it with blnet_free).
                WriteLine($"{destination} = {expression};");
                return;
            }

            if (BoundaryTypeRegistry.Categorize(resultType) == BoundaryTypeCategory.ManagedOwned
                || target.Kind == NetMemberCategory.Constructor)
            {
                // A handle result. The destination must itself be NetRef-backed, or the
                // assignment would be unsound C++ — the analyzer types these from the same
                // descriptor, so a mismatch means an untyped destination (e.g. a legacy
                // Object-typed shape) and must refuse, not degrade.
                if (!IsNetRefBacked(resultNode))
                    throw NetLoweringRefusal("BL6019",
                        $"the result of '{targetDisplay}' is a .NET object handle, but the "
                        + $"destination (BasicLang type '{resultNode.Type?.Name}') is not "
                        + "NetRef-backed — declare the receiving variable as the ManagedOwned "
                        + "type the member returns.");
                WriteLine($"{destination} = {expression};");
                return;
            }

            throw NetLoweringRefusal("BL6019",
                $"'{targetDisplay}' returns '{resultType}', which has no native representation "
                + "under P2a — a result must be a §8.3/§6.4 value, String, or one of the "
                + "handle-backed registry types (Regex/Uri/Stream/FileInfo/DirectoryInfo). "
                + "Consuming arbitrary .NET objects lands with §8.5.");
        }

        /// <summary>
        /// The <c>IRFieldStore</c> arm: a .NET property/field WRITE through the synthesized
        /// <c>set_X</c> slot — <c>proxy(receiver?, value)</c> as a statement.
        /// </summary>
        private bool TryLowerNetFieldStore(IRFieldStore store)
        {
            if (store.ResolvedNetTarget == null) return false;
            if (IsNativelyHandledCategory(store.NetCategory)) return false;

            RequireExactNetTarget(store.ResolvedNetTarget, store.ResolvedNetTargetIsExact);

            var expression = BuildNetProxyCall(
                store.ResolvedNetTarget,
                store.ResolvedNetTarget.IsStatic ? null : store.Object,
                new[] { store.Value });
            WriteLine(expression + ";");
            return true;
        }
    }
}
