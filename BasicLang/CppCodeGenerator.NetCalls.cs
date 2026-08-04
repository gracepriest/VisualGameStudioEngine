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

            EmitNetResult(resultNode, target, BuildNetProxyCall(target, receiver, arguments));
            return true;
        }

        /// <summary>
        /// One argument's contribution to a proxy call: the expression(s) that occupy its wire
        /// slot(s), plus any statements that must run BEFORE the call and any that must run
        /// AFTER it.
        ///
        /// <para><b>Why a bare expression was not enough.</b> §8.3's <c>ref</c>/<c>out</c> row
        /// is a pointer slot, which lowers to <c>int32_t t = n; proxy(&amp;t); n = t;</c> — a
        /// prologue and an epilogue around one call. §8.6's outbound collection copy (Task 10)
        /// needs a copy-out plus a release, and §8.4's callbacks (Task 11) need a register plus
        /// an unregister. All three are statements, and a function returning a string can only
        /// ever produce sub-expressions.</para>
        ///
        /// <para><see cref="Expressions"/> is a LIST because a §6.4 multi-slot pair occupies
        /// several wire slots from one BasicLang argument (Decimal's four <c>GetBits</c> words,
        /// DateTimeOffset's declared scalar pair). Ordinary rows contribute exactly one.</para>
        /// </summary>
        private readonly struct NetArgEmission
        {
            private NetArgEmission(
                IReadOnlyList<string> expressions,
                IReadOnlyList<string> prologue,
                IReadOnlyList<string> epilogue)
            {
                Expressions = expressions;
                Prologue = prologue ?? Array.Empty<string>();
                Epilogue = epilogue ?? Array.Empty<string>();
            }

            internal IReadOnlyList<string> Expressions { get; }
            internal IReadOnlyList<string> Prologue { get; }
            internal IReadOnlyList<string> Epilogue { get; }

            /// <summary>A row that needs no statements — every §8.3 by-value row.</summary>
            internal static NetArgEmission Value(string expression) =>
                new(new[] { expression }, null, null);

            internal static NetArgEmission Statements(
                IReadOnlyList<string> expressions,
                IReadOnlyList<string> prologue = null,
                IReadOnlyList<string> epilogue = null) =>
                new(expressions, prologue, epilogue);
        }

        /// <summary>
        /// A whole proxy call, accumulated from its arguments: the statements that must precede
        /// it, the call expression itself, and the statements that must follow it.
        /// <see cref="EmitNetResult"/> writes them in that order, and computes the result
        /// statement (which may REFUSE) before writing anything — a refusal must never leave a
        /// half-emitted prologue behind.
        /// </summary>
        private sealed class NetCallEmission
        {
            internal List<string> Prologue { get; } = new List<string>();
            internal string Expression { get; set; }
            internal List<string> Epilogue { get; } = new List<string>();
        }

        /// <summary>
        /// Mints a unique temporary name for a marshaling prologue. Per-GENERATOR rather than
        /// per-call: two proxy calls in one C++ scope would otherwise both declare
        /// <c>blnet_t0</c> and the second is a redefinition error.
        /// </summary>
        private int _netTempSequence;

        private string NextNetTemp() =>
            "blnet_t" + (_netTempSequence++).ToString(CultureInfo.InvariantCulture);

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
        private NetCallEmission BuildNetProxyCall(
            NetMemberDescriptor target, IRValue receiver, IReadOnlyList<IRValue> arguments)
        {
            var emission = new NetCallEmission();
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
            {
                var argument = MarshalNetArgument(targetDisplay, parameters[i], i, arguments[i]);
                emission.Prologue.AddRange(argument.Prologue);
                callArgs.AddRange(argument.Expressions);
                emission.Epilogue.AddRange(argument.Epilogue);
            }

            emission.Expression = "BasicLang::net::" + NetNameMangler.Mangle(target)
                                  + "(" + string.Join(", ", callArgs) + ")";
            return emission;
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
        private NetArgEmission MarshalNetArgument(
            string targetDisplay, NetParameterDescriptor parameter, int index, IRValue argument)
        {
            var position = (index + 1).ToString(CultureInfo.InvariantCulture);

            if (parameter.RefKind != NetRefKind.None)
                return MarshalNetByRefArgument(targetDisplay, parameter, position, argument);

            var isNullConstant = argument is IRConstant { Value: null };
            var paramType = parameter.TypeFullName;

            // §8.3's by-value rows, projected from NetMarshalTable.WireRows — the SAME table
            // NetProxyEmitter.WireOf and NetShimGenerator.WireOf project from. A row that
            // exists in the emitters but not here is a SILENT wire mismatch (the emitters
            // would declare, say, a uint16_t slot while the call site passes a handle), which
            // is why the branch is a table lookup and not a switch over spellings.
            if (NetMarshalTable.TryGetWireRow(paramType, out var row))
            {
                switch (row.Shape)
                {
                    case NetWireShape.String:
                        if (isNullConstant)
                            return NetArgEmission.Value("nullptr");   // §8.2: Nothing is the null wire form
                        if (argument is IRConstant { Value: string } stringConstant)
                            return NetArgEmission.Value(EmitConstant(stringConstant));  // a literal IS a const char*
                        if (string.Equals(argument.Type?.Name, "String", StringComparison.OrdinalIgnoreCase))
                            return NetArgEmission.Value("(" + GetValueName(argument) + ").c_str()");
                        throw NetLoweringRefusal("BL6019",
                            $"'{targetDisplay}': argument {position} (BasicLang type "
                            + $"'{argument.Type?.Name}') cannot cross a String slot — pass a "
                            + "String value or literal (spec §8.3).");

                    case NetWireShape.Char:
                        // §8.3: outbound zero-extends the 1-byte native Char onto the UTF-16
                        // wire. The `unsigned char` hop is LOAD-BEARING and not a stylistic
                        // double cast: plain `char` is signed on this backend, so a bare
                        // static_cast<uint16_t> SIGN-EXTENDS every byte >= 0x80 into 0xFFxx.
                        // (The §14.10 divergence lives on the INBOUND side.)
                        return NetArgEmission.Value(
                            "static_cast<uint16_t>(static_cast<unsigned char>("
                            + GetValueName(argument) + "))");

                    case NetWireShape.Boolean:   // the proxy parameter is C++ bool
                    case NetWireShape.Scalar:    // numeric scalars pass by value
                        return NetArgEmission.Value(GetValueName(argument));

                    case NetWireShape.Conversion:
                        if (row.IsMultiSlot)
                            throw NetLoweringRefusal("BL6019",
                                $"'{targetDisplay}': parameter {position} has type "
                                + $"'{paramType}', whose §6.4 wire form is not a single slot — "
                                + "it is not lowered at the native boundary yet.");
                        return NetArgEmission.Value(
                            row.NativeToNet + "(" + GetValueName(argument) + ")");
                }
            }

            // Everything else in §8.3 is a HANDLE slot: Nothing crosses as handle 0, and a
            // NetRef-backed value passes through (genuinely free). A NATIVE value here —
            // an array against byte[], a std::string against Object, a collection against
            // IEnumerable — has no handle to pass: §8.6's outbound copy is Task 10.
            // ONE spelling everywhere the generator names this type: `BasicLang::NetRef`,
            // matching MapType's declaration form. `BasicLang::blnet::NetRef` is the same type
            // (the blnet namespace re-exports it with a using-declaration), but two spellings
            // for one type in generated code is a reader trap.
            if (isNullConstant)
                return NetArgEmission.Value("BasicLang::NetRef()");
            if (IsNetRefBacked(argument))
                return NetArgEmission.Value(GetValueName(argument));

            throw NetLoweringRefusal("BL6019",
                $"'{targetDisplay}': argument {position} (BasicLang type "
                + $"'{argument.Type?.Name}') is not a handle-backed .NET value, but parameter "
                + $"{position} has reference type '{paramType}' — a native value crossing "
                + "into a .NET reference slot is §8.6 outbound-copy territory, which is not "
                + "lowered at the native boundary yet. Pass Nothing or a ManagedOwned-typed "
                + "value.");
        }

        /// <summary>
        /// §8.3's <c>ref</c>/<c>out</c> row: a POINTER SLOT.
        ///
        /// <para><b>The &amp;-taking is the proxy's, not the call site's.</b>
        /// <c>NetProxyEmitter</c> shapes a ByRef parameter as a C++ REFERENCE over the WIRE type
        /// (<c>int32_t&amp; a0</c>), copies it into a temporary, passes <c>&amp;temp</c> across
        /// the C ABI and writes the temporary back on return. So the call site's whole job is to
        /// hand it an lvalue OF THE WIRE TYPE — which for the by-value scalar rows the native
        /// variable already is, and for the rows whose native representation differs from their
        /// wire (Char, and §6.4's single-slot pairs) is what the prologue/epilogue produce:
        /// <c>uint64_t t = to_net_datetime(d); proxy(t); d = from_net_datetime(t);</c></para>
        ///
        /// <para><b><c>out</c> does not read the caller's value.</b> The temporary is
        /// value-initialized instead of converted from the native variable: an <c>out</c>
        /// parameter's incoming value is ignored by the callee (the generated shim assigns
        /// <c>default</c> before the call), and converting an uninitialized BasicLang local
        /// through a range-checking §6.4 converter would throw on a value nobody passed.</para>
        ///
        /// <para><b>What still refuses, and why it is not a guess we could make.</b> A ByRef
        /// STRING has no single wire type — <c>const char*</c> in, <c>char**</c> out, with
        /// opposite ownership — and a ByRef HANDLE leaves ownership undefined: writing a NEW
        /// handle over the caller's releases one the callee may have returned unchanged, a
        /// double release. §8.3 says "ref/out → pointer slot" and stops there, so both refuse
        /// here exactly as <c>NetProxyEmitter.PlanMember</c> refuses to emit them. The analyzer
        /// reports the same shapes at their source positions first.</para>
        /// </summary>
        private NetArgEmission MarshalNetByRefArgument(
            string targetDisplay, NetParameterDescriptor parameter, string position, IRValue argument)
        {
            var paramType = parameter.TypeFullName;
            var passing = parameter.RefKind.ToString().ToLowerInvariant();

            if (!NetMarshalTable.TryGetWireRow(paramType, out var row)
                || row.IsMultiSlot || string.IsNullOrEmpty(row.CWire))
            {
                throw NetLoweringRefusal("BL6019",
                    $"'{targetDisplay}': parameter {position} ('{parameter}') is passed "
                    + $"{passing} and its type '{paramType}' has no single by-value wire slot. "
                    + "§8.3 pins ByRef slots to by-value scalars only: a ByRef String has "
                    + "opposite ownership in each direction, and a ByRef .NET object would "
                    + "leave handle ownership undefined (a double release). Pass it by value, "
                    + "or use an overload that returns the value instead.");
            }

            // The C++ reference the proxy takes must bind to something assignable. An IRVariable
            // is the only IR value that is; a temporary or a constant would either fail to
            // compile or silently discard the callee's write.
            if (argument is not IRVariable)
            {
                throw NetLoweringRefusal("BL6019",
                    $"'{targetDisplay}': parameter {position} is passed {passing}, so the "
                    + "argument must be a variable the call can write back into — an expression "
                    + "or literal has nowhere to receive the result. Assign it to a local first "
                    + "and pass that.");
            }

            var native = GetValueName(argument);

            // Boolean and the numeric scalars: MapType already spells the native variable as the
            // proxy's C++ parameter type (Integer -> int32_t, Boolean -> bool), so it binds
            // directly and no temporary can go stale.
            if (row.Shape == NetWireShape.Scalar || row.Shape == NetWireShape.Boolean)
                return NetArgEmission.Value(native);

            var temp = NextNetTemp();
            var isOut = parameter.RefKind == NetRefKind.Out;
            string inbound, outbound;
            if (row.Shape == NetWireShape.Char)
            {
                // Same zero-extend/narrow pair as the by-value Char row, including the
                // load-bearing `unsigned char` hop (plain char is signed here, so a bare cast
                // sign-extends every byte >= 0x80 into 0xFFxx) and §14.10's lossy inbound.
                inbound = "static_cast<uint16_t>(static_cast<unsigned char>(" + native + "))";
                outbound = "static_cast<char>(" + temp + ")";
            }
            else
            {
                inbound = row.NativeToNet + "(" + native + ")";
                outbound = row.NativeFromNet + "(" + temp + ")";
            }

            return NetArgEmission.Statements(
                new[] { temp },
                prologue: new[] { row.CWire + " " + temp + (isOut ? "{};" : " = " + inbound + ";") },
                epilogue: new[] { native + " = " + outbound + ";" });
        }

        /// <summary>
        /// Emits the statement carrying the proxy call: a bare statement for void, otherwise
        /// an assignment into the node's pre-declared destination, wrapping the §8.3 return
        /// row (Boolean/scalars direct; Char narrowed — the documented §14.10 lossy inbound
        /// divergence; String is <c>std::string</c> which IS <c>BasicLang::String</c>;
        /// DateTime/TimeSpan through the inverse converters; handles into a
        /// <c>NetRef</c>-typed destination). Results this task cannot represent refuse.
        /// </summary>
        private void EmitNetResult(
            IRValue resultNode, NetMemberDescriptor target, NetCallEmission emission)
        {
            // Computed BEFORE anything is written: every arm below can REFUSE, and a refusal
            // that had already emitted the argument prologue would leave dangling temporaries
            // in a translation unit the build is about to reject anyway — but it would also
            // make the failure look like a codegen bug rather than the §8.3 gap it is.
            var statement = NetResultStatement(resultNode, target, emission.Expression);

            foreach (var line in emission.Prologue) WriteLine(line);
            WriteLine(statement);
            foreach (var line in emission.Epilogue) WriteLine(line);
        }

        /// <summary>The single statement carrying <paramref name="expression"/>. See
        /// <see cref="EmitNetResult"/> for why it is computed before emission.</summary>
        private string NetResultStatement(
            IRValue resultNode, NetMemberDescriptor target, string expression)
        {
            var targetDisplay = target.DeclaringTypeFullName + "."
                + (target.Kind == NetMemberCategory.Constructor ? "New" : target.Name);

            // Constructors return the created object's handle (§8.2), not their metadata
            // System.Void.
            var resultType = target.Kind == NetMemberCategory.Constructor
                ? target.DeclaringTypeFullName
                : target.TypeFullName;

            if (string.Equals(resultType, "System.Void", StringComparison.Ordinal))
                return expression + ";";

            var destination = GetValueName(resultNode);

            // Same NetMarshalTable.WireRows projection the argument side uses — one table, so
            // a row can never be carried outbound and dropped inbound.
            if (NetMarshalTable.TryGetWireRow(resultType, out var row))
            {
                switch (row.Shape)
                {
                    case NetWireShape.Char:
                        // §14.10 shipped divergence: a code unit above U+00FF cannot fit the
                        // 1-byte native Char — the narrowing is lossy and documented.
                        return $"{destination} = static_cast<char>({expression});";

                    case NetWireShape.Conversion:
                        if (row.IsMultiSlot)
                            throw NetLoweringRefusal("BL6019",
                                $"'{targetDisplay}' returns '{resultType}', whose §6.4 wire "
                                + "form is not a single slot — it is not lowered at the native "
                                + "boundary yet.");
                        return $"{destination} = {row.NativeFromNet}({expression});";

                    default:
                        // Boolean, the numeric scalars, String (std::string IS
                        // BasicLang::String on this backend — the proxy already copied out of
                        // the transfer buffer and freed it with blnet_free).
                        return $"{destination} = {expression};";
                }
            }

            // A HANDLE result (§8.3's reference-type row, "other non-ref value types → handle
            // (boxed)", and a constructor's created object). P2a-2 Task 9 widened this from the
            // five ManagedOwned registry names to every handle the ANALYZER typed as one: the
            // gate is the DESTINATION's representation, not a name list, so the two can never
            // disagree — SemanticAnalyzer.NetHandleResultTypeInfo decides admissibility once,
            // from the same §8.3 rules NetSurfaceCollector.FirstUnmarshalable applies to the
            // surface, and stamps TypeInfo.NetHandleTypeFullName; MapType turns exactly that
            // into BasicLang::NetRef.
            if (IsNetRefBacked(resultNode))
                return $"{destination} = {expression};";

            if (BoundaryTypeRegistry.Categorize(resultType) == BoundaryTypeCategory.ManagedOwned
                || target.Kind == NetMemberCategory.Constructor)
            {
                // The result IS a handle but the destination is not NetRef-backed, so the
                // assignment would be unsound C++. The analyzer types these from the same
                // descriptor, so a mismatch means an untyped destination (e.g. a legacy
                // Object-typed shape) and must refuse, not degrade.
                throw NetLoweringRefusal("BL6019",
                    $"the result of '{targetDisplay}' is a .NET object handle, but the "
                    + $"destination (BasicLang type '{resultNode.Type?.Name}') is not "
                    + "NetRef-backed — declare the receiving variable as the ManagedOwned "
                    + "type the member returns.");
            }

            throw NetLoweringRefusal("BL6019",
                $"'{targetDisplay}' returns '{resultType}', which has no native representation "
                + "under P2a — a result must be a §8.3/§6.4 value, String, or a .NET type that "
                + "can cross as an opaque handle (§8.5). Types §8.3 can never carry are "
                + "System.Object, a ref struct (Span/ReadOnlySpan), a pointer, and an open "
                + "generic.");
        }

        /// <summary>
        /// P2a-2 Task 9 (spec §8.5) — the <c>IRIndexerStore</c> arm: a WRITE through the
        /// synthesized <c>set_Item(index…, value)</c> slot. The value goes LAST, which is the
        /// order <see cref="NetAccessorSynthesis.SetterFor"/> and
        /// <see cref="NetAccessorSynthesis.ArraySetFor"/> both build and the order the generated
        /// shim's <c>target[i] = v</c> spelling reads back.
        /// </summary>
        private bool TryLowerNetIndexerStore(IRIndexerStore store)
        {
            if (store.ResolvedNetTarget == null) return false;
            if (IsNativelyHandledCategory(store.NetCategory)) return false;

            RequireExactNetTarget(store.ResolvedNetTarget, store.ResolvedNetTargetIsExact);

            var arguments = new List<IRValue>(store.Indices.Count + 1);
            arguments.AddRange(store.Indices);
            arguments.Add(store.Value);

            var emission = BuildNetProxyCall(store.ResolvedNetTarget, store.Collection, arguments);
            foreach (var line in emission.Prologue) WriteLine(line);
            WriteLine(emission.Expression + ";");
            foreach (var line in emission.Epilogue) WriteLine(line);
            return true;
        }

        /// <summary>
        /// P2a-2 Task 9 (spec §8.5) — the <c>IRForEach</c> arm for a HANDLE-represented
        /// collection. Emits the enumerator protocol EXPLICITLY, driven through
        /// <c>IEnumerable&lt;T&gt;</c>/<c>IEnumerator&lt;T&gt;</c>:
        ///
        /// <code>
        /// {
        ///     BasicLang::NetRef e = net::GetEnumerator(collection);
        ///     while (net::MoveNext(e)) { T x = net::Current(e); …body… }
        ///     net::Dispose(e);
        /// }
        /// </code>
        ///
        /// <para><b>Why not a range-for over anything.</b> A handle supports no operation the
        /// surface collector did not emit an export for (§8.5's opening sentence) — there is no
        /// <c>begin()</c>/<c>end()</c> to find. And the enumerator must be reached through the
        /// INTERFACE, never through the concrete struct-returning <c>GetEnumerator()</c>: see
        /// <see cref="IRNetEnumeration"/> for the boxed-mutable-struct infinite loop that
        /// choice prevents.</para>
        ///
        /// <para><b>The braces are load-bearing.</b> The enumerator temporary is scoped to the
        /// loop so two <c>For Each</c>es in one function do not redeclare it, and so the
        /// <c>NetRef</c>'s destructor releases the handle even when the body throws — the
        /// §8.6/§11 "epilogue is success-path only" hazard, answered here with RAII rather than
        /// a trailing statement, exactly as the plan's emission-seam contract requires.
        /// <c>Dispose</c> is the MANAGED half and does run on the normal and <c>Exit For</c>
        /// paths; a <c>Return</c> out of the loop body skips it, which is the same known
        /// Try/Finally limitation this backend already documents.</para>
        /// </summary>
        private bool TryLowerNetForEach(IRForEach forEach)
        {
            var enumeration = forEach.NetEnumeration;
            if (enumeration == null) return false;

            if (!IsNetRefBacked(forEach.Collection))
                throw NetLoweringRefusal("BL6019",
                    "internal: a For Each carrying §8.5 enumeration reached the lowering with a "
                    + $"collection (BasicLang type '{forEach.Collection?.Type?.Name}') that is "
                    + "not handle-backed. This is a compiler routing defect — the analyzer only "
                    + "stamps the enumeration onto a handle-represented collection.");

            var collection = GetValueName(forEach.Collection);
            var enumerator = NextNetTemp();
            var elementType = MapType(forEach.ElementType);
            var variable = SanitizeName(forEach.VariableName);

            WriteLine("{");
            Indent();
            WriteLine("// §8.5: the enumerator is obtained and driven through IEnumerable<T>/");
            WriteLine("// IEnumerator<T>, NEVER the concrete struct-returning GetEnumerator() —");
            WriteLine("// a boxed mutable-struct enumerator would MoveNext() forever (see");
            WriteLine("// IRNetEnumeration). The NetRef releases the handle on every exit path.");
            WriteLine($"BasicLang::NetRef {enumerator} = BasicLang::net::"
                      + NetNameMangler.Mangle(enumeration.GetEnumerator) + $"({collection});");
            WriteLine($"while (BasicLang::net::{NetNameMangler.Mangle(enumeration.MoveNext)}({enumerator}))");
            WriteLine("{");
            Indent();
            WriteLine($"{elementType} {variable} = BasicLang::net::"
                      + NetNameMangler.Mangle(enumeration.Current) + $"({enumerator});");

            // Same region contract as the native arm: the whole body region goes inside the loop
            // braces, and an end-of-iteration branch to EndBlock is `continue;` (the while owns
            // iteration), never a goto that would leave the loop.
            EmitInlineRegion(forEach.BodyBlock, forEach.EndBlock, RegionEnd.LoopContinue);

            Unindent();
            WriteLine("}");
            WriteLine($"BasicLang::net::{NetNameMangler.Mangle(enumeration.Dispose)}({enumerator});");
            Unindent();
            WriteLine("}");
            return true;
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

            var emission = BuildNetProxyCall(
                store.ResolvedNetTarget,
                store.ResolvedNetTarget.IsStatic ? null : store.Object,
                new[] { store.Value });
            foreach (var line in emission.Prologue) WriteLine(line);
            WriteLine(emission.Expression + ";");
            foreach (var line in emission.Epilogue) WriteLine(line);
            return true;
        }
    }
}
