using System;
using System.Collections.Generic;
using System.Globalization;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;   // TypeInfo / TypeKind — §8.6's native-vs-handle test
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
        /// AFTER it on the SUCCESS path.
        ///
        /// <para><b>Why a bare expression was not enough.</b> §8.3's <c>ref</c>/<c>out</c> row
        /// is a pointer slot, which lowers to <c>int32_t t = n; proxy(&amp;t); n = t;</c> — a
        /// prologue and a write-back around one call. §8.6's outbound collection copy needs a
        /// copy-in plus a release, and §8.4's callbacks (Task 11) need a register plus an
        /// unregister. All of them are statements, and a function returning a string can only
        /// ever produce sub-expressions.</para>
        ///
        /// <para><see cref="Expressions"/> is a LIST because a §6.4 multi-slot pair occupies
        /// several wire slots from one BasicLang argument (Decimal's four <c>GetBits</c> words,
        /// DateTimeOffset's declared scalar pair). Ordinary rows contribute exactly one.</para>
        ///
        /// <para>⛔ <b>THE EMISSION-SEAM CONTRACT (P2a-2 Task-10 Step 0; Task-8 review I1).</b>
        /// <see cref="EmitNetCallStatements"/> writes prologue → call → write-back as
        /// STRAIGHT-LINE C++, and the call statement contains <c>NetCheckTyped</c>, <b>which
        /// throws</b>. Therefore:</para>
        /// <list type="number">
        /// <item><description><b><see cref="WriteBack"/> is SUCCESS-PATH ONLY.</b> A thrown
        /// managed exception skips every line in it. That is CORRECT for the ref/out row it was
        /// built for — .NET would not write back either — and is why nothing is broken today.
        /// It is named <c>WriteBack</c> rather than "epilogue" precisely so the next author
        /// cannot read it as "runs afterwards".</description></item>
        /// <item><description><b>Anything that MUST run on the throwing path goes in
        /// <see cref="Prologue"/> as an RAII GUARD</b> — a C++ object whose destructor performs
        /// the release — never as a trailing statement. §8.6's copy-in guard is a
        /// <c>BasicLang::NetRef</c> local (see <see cref="MarshalNetArrayArgument"/>): its
        /// deleter calls <c>blnet_release</c> when the enclosing scope unwinds, exception or
        /// not. §8.4's callback registration inherits the same slot in Task 11.</description></item>
        /// <item><description><b>Release ORDER is free, and that is the second reason for
        /// RAII.</b> Prologue lines accumulate in ARGUMENT order while releases want the
        /// REVERSE; C++ destroys automatic objects in reverse declaration order, so guards
        /// declared front-to-back release back-to-front with no list to maintain. A
        /// hand-rolled epilogue list would have had to be reversed by hand, correctly, forever.</description></item>
        /// </list>
        /// </summary>
        private readonly struct NetArgEmission
        {
            private NetArgEmission(
                IReadOnlyList<string> expressions,
                IReadOnlyList<string> prologue,
                IReadOnlyList<string> writeBack)
            {
                Expressions = expressions;
                Prologue = prologue ?? Array.Empty<string>();
                WriteBack = writeBack ?? Array.Empty<string>();
            }

            internal IReadOnlyList<string> Expressions { get; }

            /// <summary>
            /// Statements emitted BEFORE the call — including every RAII guard whose destructor
            /// owns a release. Runs unconditionally. See the type remarks.
            /// </summary>
            internal IReadOnlyList<string> Prologue { get; }

            /// <summary>
            /// SUCCESS-PATH-ONLY statements emitted after the call. A managed throw skips them.
            /// Never put a release here — see the type remarks.
            /// </summary>
            internal IReadOnlyList<string> WriteBack { get; }

            /// <summary>A row that needs no statements — every §8.3 by-value row.</summary>
            internal static NetArgEmission Value(string expression) =>
                new(new[] { expression }, null, null);

            internal static NetArgEmission Statements(
                IReadOnlyList<string> expressions,
                IReadOnlyList<string> prologue = null,
                IReadOnlyList<string> writeBack = null) =>
                new(expressions, prologue, writeBack);
        }

        /// <summary>
        /// A whole proxy call, accumulated from its arguments: the statements that must precede
        /// it, the call expression itself, and the SUCCESS-PATH-ONLY statements that follow it.
        /// See <see cref="NetArgEmission"/> for the seam contract both lists obey.
        /// </summary>
        private sealed class NetCallEmission
        {
            internal List<string> Prologue { get; } = new List<string>();
            internal string Expression { get; set; }
            internal List<string> WriteBack { get; } = new List<string>();
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
                emission.WriteBack.AddRange(argument.WriteBack);
            }

            emission.Expression = "BasicLang::net::" + NetNameMangler.Mangle(target)
                                  + "(" + string.Join(", ", callArgs) + ")";
            return emission;
        }

        /// <summary>
        /// The C++ type a value ACTUALLY has in the emitted code — the DECLARATION when the value
        /// is a named local, <see cref="MapType"/> of its IR type otherwise.
        ///
        /// <para><b>Why the declaration has to win, measured rather than assumed.</b> The full
        /// PROJECT build fuses <c>t = call(); v = t;</c> into <c>v = call();</c> and leaves the
        /// result node spelled <c>v</c> while its IR <c>Type</c> is still the call's. For
        /// <c>Dim V() As Integer = obj.GetValues()</c> that is one IR value saying "handle array"
        /// against one C++ declaration saying <c>std::vector&lt;int32_t&gt;</c>; asking
        /// <see cref="MapType"/> alone then emits <c>V = &lt;NetRef&gt;;</c> followed by
        /// <c>V = read(V);</c> — two hard C++ errors in generated code the user never wrote.
        ///
        /// <para><b>Where the fusion comes from.</b> Neither <c>Generate</c> nor
        /// <c>GenerateSplit</c> with <c>AddStandardPasses</c> run directly over an
        /// <c>IRBuilder</c> module produces it — both keep the temp — so the fast test subjects
        /// do not exhibit it. The pin for this therefore lives in an Integration test
        /// (<c>NetShimPipelineTests.Section86_ByValueCopyIsOneWay_ButARefSlotReadsBack</c>),
        /// which is the shape that needs live .NET resolution anyway.</para>
        ///
        /// <para>⛔ <b>An earlier version of this comment claimed the fusion "takes the whole
        /// CppProjectBuilder → CompileProjectFiles path" and that "the fast subject cannot
        /// construct the shape", labelled as measured. That was wrong and is retracted.</b> The
        /// plain CLI single-file path performs the fusion too — <c>BasicLang.exe f.bas
        /// --target=cpp</c> on <c>Dim v As Integer = F()</c> emits <c>v = F();</c> with no
        /// intermediate temp. What the Integration test uniquely supplies is .NET RESOLUTION,
        /// not the fusion. The distinction matters because "only the project path can build this
        /// shape" would send the next author looking in the wrong component.</para>
        ///
        /// <para>It remains precisely the hazard the repo law about testing BOTH entry points
        /// exists for — a fix verified only through the test helper still breaks the CLI and the
        /// IDE.</para>
        ///
        /// <para>It is also the right rule on its own terms: §8.6 row 2 says the DECLARATION is
        /// what decides whether a .NET array materializes by copy.</para>
        /// </summary>
        private string EffectiveCppType(IRValue value)
        {
            if (value == null) return null;

            // Keyed on the value's NAME, not on the node being an IRVariable: the project build
            // fuses a call into its destination and the result arrives as a non-variable node
            // carrying `Name = "V"` (the same channel GetValueName's own "honor the destination
            // name" arm reads). A node-SHAPE test would miss exactly the case this exists for.
            //
            // Read straight off the node rather than through GetValueName, which is NOT a pure
            // lookup on this backend — it mints temp names and even renders whole lambda bodies.
            // This runs from IsNetRefBacked, i.e. speculatively, on values that may still refuse.
            if (_localCppTypeByName.Count > 0 && !string.IsNullOrEmpty(value.Name)
                && !(value is IRConstant)
                && _localCppTypeByName.TryGetValue(SanitizeName(value.Name), out var declared))
            {
                return declared;
            }
            return value.Type == null ? null : MapType(value.Type);
        }

        /// <summary>True when the value's declared type lowers to <c>BasicLang::NetRef</c>.</summary>
        private bool IsNetRefBacked(IRValue value) =>
            string.Equals(EffectiveCppType(value), "BasicLang::NetRef", StringComparison.Ordinal);

        /// <summary>
        /// Spec §8.6 row 2 — <c>Dim a() As Integer = obj.GetValues()</c>. The expression for
        /// <paramref name="value"/> as seen by an assignment into
        /// <paramref name="destinationType"/>: normally just its name, but a HANDLE array landing
        /// in a DECLARED NATIVE array materializes by copy — a one-way snapshot into the
        /// <c>std::vector</c>.
        ///
        /// <para><b>Row 1 is the default and stays untouched.</b> An INFERRED
        /// <c>Dim a = obj.GetValues()</c> types the local as the handle array itself
        /// (<c>TypeInfo.NetHandleTypeFullName</c> set), so both sides are <c>NetRef</c>, the test
        /// below does not fire, and there is no copy — indexing and iteration go through §8.5's
        /// synthetic exports. The DECLARATION is the only thing that asks for a copy, which is
        /// exactly the distinction §8.6's table draws.</para>
        ///
        /// <para><b>Why the guard is the DESTINATION marker rather than the source shape.</b> A
        /// handle <c>System.Int32[]</c> and a native <c>Integer()</c> have the same
        /// <c>Name</c>/<c>Kind</c>/<c>ElementType</c> — the marker is the only difference — so a
        /// name- or kind-based test here would either copy every array or none. It is the same
        /// by-name hazard §8.5 records for <c>MapType</c>, at the one site that survives it.</para>
        /// </summary>
        private string NetMaterializedValue(IRValue value, TypeInfo destinationType)
        {
            var expression = GetValueName(value);

            var source = value?.Type;
            if (destinationType == null) return expression;

            // The INVERSE pairing, reachable since the two representations became type-identical
            // (they differ only by the marker): a NATIVE array assigned into a HANDLE-typed
            // variable. §8.6's table has no row for it — copying IN on a bare assignment would
            // silently mint a handle nothing asked for — so it refuses here rather than emitting
            // `NetRef = std::vector`, a C++ error in generated code the user never wrote.
            if (destinationType.NetHandleTypeFullName != null
                && destinationType.Kind == TypeKind.Array
                && source?.Kind == TypeKind.Array
                && source.NetHandleTypeFullName == null
                && !IsNetRefBacked(value))
            {
                throw NetLoweringRefusal("BL6019",
                    "a native BasicLang array cannot be assigned into a variable holding a "
                    + $"handle-represented '{destinationType.NetHandleTypeFullName}'. §8.6 copies "
                    + "a native array outbound only as a CALL ARGUMENT; an assignment has no "
                    + "boundary to cross. Declare the destination as a native array (with '()') "
                    + "so the .NET array materializes into it instead.");
            }

            if (source?.NetHandleTypeFullName == null) return expression;

            // The DECLARATION is the authority on both sides (see EffectiveCppType): once the
            // optimizer has fused a NetRef temp into a native-array local, the IR node still
            // claims the handle type while the emitted variable is a std::vector. Reading the
            // marker alone here would emit `V = bl_net_array_read_int32(V)` — a readback of the
            // vector itself, which is what the project build (optimizer on, split emission)
            // actually produced before this guard existed.
            if (!IsNetRefBacked(value)) return expression;

            if (destinationType.Kind != TypeKind.Array) return expression;
            if (destinationType.NetHandleTypeFullName != null) return expression;   // handle -> handle
            if (destinationType.ElementType == null) return expression;

            // Only a .NET ARRAY can materialize into a native array. Anything else landing here
            // is a program the analyzer should already have rejected; leave it alone so the
            // failure keeps whatever diagnostic it already had.
            if (!source.NetHandleTypeFullName.EndsWith("[]", StringComparison.Ordinal))
                return expression;

            // Same re-derivation guard as the argument side: the table's NativeElement column and
            // the live MapType are separate code, and a divergence must not reach the C++
            // compiler as a std::vector<> mismatch in generated code. REFUSING rather than
            // returning the value unchanged is the point — `std::vector<X> a = <NetRef>;` is a
            // C++ error in generated code the user never wrote, which is the late-failure shape
            // this whole pipeline exists to move earlier.
            if (!NetArrayCopy.TryGetFormForArray(source.NetHandleTypeFullName, out var form)
                || !string.Equals(MapType(destinationType.ElementType), form.NativeElement,
                                  StringComparison.Ordinal))
            {
                throw NetLoweringRefusal("BL6019",
                    $"'{source.NetHandleTypeFullName}' cannot be materialized into the declared "
                    + $"native array (element C++ type '{MapType(destinationType.ElementType)}'): "
                    + "§8.6 copies a .NET array into a BasicLang array only for a SIMPLE element "
                    + "type (a §8.3 by-value row, or String) that corresponds exactly. Declare "
                    + "the variable without '()' to keep the .NET array as a handle and index it "
                    + "through §8.5 instead.");
            }

            return "BasicLang::net::" + form.ReadExportName + "(" + expression + ")";
        }

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

            // §8.4 FIRST, and authoritatively: delegate-ness rides the surface (D-P9) rather
            // than being guessed from a type name, and a delegate parameter's name is never in
            // WireRows, so without this arm it would fall to the handle row and then the
            // terminal BL6019 — refusing with an ARRAY explanation for a delegate.
            if (parameter.IsDelegate)
                return MarshalNetDelegateArgument(targetDisplay, parameter, position, argument);

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
            // NetRef-backed value passes through (genuinely free).
            // ONE spelling everywhere the generator names this type: `BasicLang::NetRef`,
            // matching MapType's declaration form. `BasicLang::blnet::NetRef` is the same type
            // (the blnet namespace re-exports it with a using-declaration), but two spellings
            // for one type in generated code is a reader trap.
            if (isNullConstant)
                return NetArgEmission.Value("BasicLang::NetRef()");
            if (IsNetRefBacked(argument))
                return NetArgEmission.Value(GetValueName(argument));

            // §8.6 row 3: a NATIVE array against a T[] parameter COPIES IN. Reached only after
            // the handle test above, which is the representation rule stated once — a value that
            // already IS a handle is never copied, whatever its declared shape.
            if (TryMarshalNetArrayArgument(
                    targetDisplay, paramType, position, argument, byRef: false, out var copyIn))
            {
                return copyIn;
            }

            throw NetLoweringRefusal("BL6019",
                $"'{targetDisplay}': argument {position} (BasicLang type "
                + $"'{argument.Type?.Name}') is not a handle-backed .NET value, but parameter "
                + $"{position} has reference type '{paramType}'. §8.6 copies a native value in "
                + "only for a one-dimensional array of a SIMPLE element type (a §8.3 by-value "
                + "row, or String) against a matching T[] parameter; List/Dictionary/HashSet, "
                + "nested element types and handle element types have no v1 copy. Pass Nothing, "
                + "a .NET-typed value, or a simple array.");
        }

        /// <summary>
        /// Spec §8.4 — a BasicLang lambda or <c>AddressOf</c> crossing as a delegate ARGUMENT.
        ///
        /// <para><b>The RAII slot, same contract §8.6 uses.</b> Registration is ONE prologue line
        /// declaring a <c>CallbackRef</c>, and the guard's handle is what crosses. It must not be
        /// a paired register/release: <c>WriteBack</c> is success-path only (NetCheckTyped throws
        /// from inside the call statement), so a release parked after the call would be skipped
        /// whenever the managed callee throws and the entry — reclaimed only through
        /// <c>blnet_callback_release</c>'s freelist — would leak permanently. D-P8.</para>
        ///
        /// <para><b>The adapter is the non-obvious part.</b> <c>NativeCallbackFn</c> is
        /// <c>int32_t(const uint64_t*, int32_t, uint64_t*)</c> — nothing like the user's
        /// function — so the lowered lambda cannot be handed over directly. The emitted adapter
        /// unpacks each slot, calls through, and writes the result.</para>
        ///
        /// <para><b>v1 admits <see cref="NetWireShape.Scalar"/> rows only</b>, and that predicate
        /// is doing real work rather than being a rough cut: it excludes <c>Boolean</c> and
        /// <c>Char</c> precisely because those are the two rows whose WIRE spelling differs from
        /// their C++ spelling (int32_t/bool and uint16_t/char). <c>NetWireRow.CWire</c>'s own
        /// remarks warn that a generalization needing the bindable type must take the proxy's C++
        /// parameter type and never the wire — restricting to Scalar sidesteps that entirely,
        /// because for those rows the two coincide. Widening to Boolean/Char or to handle slots
        /// needs the §8.4 marshaling contract written first.</para>
        /// </summary>
        private NetArgEmission MarshalNetDelegateArgument(
            string targetDisplay, NetParameterDescriptor parameter, string position,
            IRValue argument)
        {
            if (!NetDelegateDispatch.TryParseInvokeSignature(
                    parameter.DelegateInvokeSignature, out var returnType, out var invokeParams))
            {
                throw NetLoweringRefusal("BL6019",
                    $"'{targetDisplay}': argument {position} is a delegate whose invoke signature "
                    + $"('{parameter.DelegateInvokeSignature}') could not be read.");
            }

            string CppSlotType(string netFullName, string what)
            {
                if (NetMarshalTable.TryGetWireRow(netFullName, out var row)
                    && row.Shape == NetWireShape.Scalar
                    && !string.IsNullOrEmpty(row.CWire))
                {
                    return row.CWire;
                }

                throw NetLoweringRefusal("BL6019",
                    $"'{targetDisplay}': argument {position} is a delegate whose {what} type "
                    + $"'{netFullName}' is not a §8.3 by-value scalar. §8.4 v1 carries blittable "
                    + "scalars only — Boolean and Char are excluded because their wire spelling "
                    + "differs from their C++ spelling, and handle/String slots need a "
                    + "marshaling contract §8.4 does not yet specify.");
            }

            var slotTypes = invokeParams.Select(t => CppSlotType(t, "parameter")).ToList();
            var isVoid = string.Equals(returnType, "System.Void", StringComparison.Ordinal);
            var cppReturn = isVoid ? "void" : CppSlotType(returnType, "return");

            var guard = NextNetTemp();
            var callee = GetValueName(argument);
            var prologue = new List<string>();

            var slots = NetDelegateDispatch.CppSlotDescriptors(parameter.DelegateInvokeSignature);
            var slotsArgument = "nullptr";
            if (slots != null)
            {
                prologue.Add("BlnetSlotDesc " + guard + "_slots[] = " + slots + ";");
                slotsArgument = guard + "_slots";
            }

            // §8.4: codegen always registers immediate = false; Immediate stays P0's rare opt-in.
            // result_bearing is what makes the thunk run a value-returning callback INLINE under
            // a BlnetCallScope instead of refusing it as cross-thread.
            prologue.Add("BasicLang::blnet::CallbackFlags " + guard + "_flags{};");
            if (!isVoid)
                prologue.Add(guard + "_flags.result_bearing = true;");

            var unpacked = string.Join(", ",
                slotTypes.Select((t, i) => "static_cast<" + t + ">(blnet_a["
                    + i.ToString(CultureInfo.InvariantCulture) + "])"));
            var invoke = guard + "_fn(" + unpacked + ")";

            var body = isVoid
                ? invoke + "; return 0;"
                : "auto blnet_r = " + invoke + "; if (blnet_result) *blnet_result = "
                  + "static_cast<uint64_t>(blnet_r); return 0;";

            prologue.Add(
                "BasicLang::blnet::CallbackRef " + guard + "(["
                + guard + "_fn = " + callee + "]"
                + "(const uint64_t* blnet_a, int32_t, uint64_t* blnet_result) -> int32_t { "
                + body + " }, "
                + slotsArgument + ", "
                + invokeParams.Count.ToString(CultureInfo.InvariantCulture) + ", "
                + guard + "_flags);");

            return NetArgEmission.Statements(new[] { guard + ".get()" }, prologue);
        }

        /// <summary>
        /// Spec §8.6 rows 3 and 4 — a NATIVE BasicLang array crossing OUTBOUND, by copy.
        ///
        /// <para><b>The representation rule, restated at the one place that could break it.</b>
        /// A .NET <c>T[]</c> VALUE is always a handle; copying happens ONLY when a native array
        /// (a <c>std::vector&lt;T&gt;</c> — no <c>GCHandle</c>, no handle) is on the other side.
        /// Both callers therefore test <see cref="IsNetRefBacked"/> FIRST and reach this only
        /// when the argument genuinely has no handle. Getting that order wrong would copy an
        /// array that was already managed, silently detaching the callee's view from the
        /// caller's.</para>
        ///
        /// <para><b>The RAII slot (the plan's emission-seam contract).</b> The prologue declares
        /// a <c>BasicLang::NetRef</c> holding the freshly-minted array handle. Its
        /// <c>shared_ptr</c> deleter calls <c>blnet_release</c> when the enclosing C++ scope
        /// unwinds — on the throwing path too, which a trailing release statement would miss
        /// because <c>NetCheckTyped</c> throws from inside the call statement. Multiple array
        /// arguments declare multiple guards front-to-back and C++ destroys them back-to-front,
        /// which is exactly the reverse release order the contract asks for, at no cost.</para>
        ///
        /// <para><b>The native element type is RE-DERIVED, not trusted.</b>
        /// <see cref="NetArrayCopyForm.NativeElement"/> records what <see cref="MapType"/> is
        /// expected to produce, and the two are compared here. They are separate code — the
        /// table is a hand-written row, <c>MapType</c> is the live mapper — so a divergence would
        /// otherwise emit a call to a proxy whose <c>std::vector&lt;…&gt;</c> parameter does not
        /// match the argument, and the failure would land in the C++ compiler on generated code
        /// rather than here.</para>
        ///
        /// <para><b>§14.11, precisely scoped.</b> For <paramref name="byRef"/> = false the copy
        /// is ONE-WAY: mutations the .NET callee makes are invisible to the caller's
        /// <c>std::vector</c>, where the C# backend WOULD see them. That is the shipped
        /// divergence. A <c>ref</c>/<c>out</c> slot is EXEMPT — it emits a write-back — which is
        /// what lets §12.1's parity program have one correct expected output instead of two.</para>
        /// </summary>
        private bool TryMarshalNetArrayArgument(
            string targetDisplay, string paramType, string position, IRValue argument,
            bool byRef, out NetArgEmission emission)
        {
            emission = default;

            if (!NetArrayCopy.TryGetFormForArray(paramType, out var form)) return false;

            var argumentType = argument?.Type;
            if (argumentType == null
                || argumentType.Kind != TypeKind.Array
                || argumentType.NetHandleTypeFullName != null      // already a handle — never copy
                || argumentType.ArrayRank > 1
                || argumentType.ElementType == null)
            {
                return false;
            }

            var nativeElement = MapType(argumentType.ElementType);
            if (!string.Equals(nativeElement, form.NativeElement, StringComparison.Ordinal))
            {
                throw NetLoweringRefusal("BL6019",
                    $"'{targetDisplay}': argument {position} is a native array of "
                    + $"'{argumentType.ElementType.Name}' (C++ '{nativeElement}') but parameter "
                    + $"{position} is '{paramType}', whose §8.6 copy expects "
                    + $"'{form.NativeElement}' elements. The element types must correspond "
                    + "exactly — §8.6 copies element by element and cannot convert between "
                    + "representations.");
            }

            var guard = NextNetTemp();
            var native = GetValueName(argument);

            // THE RAII SLOT. `BasicLang::NetRef` (never the blnet:: re-export — one spelling in
            // generated code) owns the minted handle for the rest of the enclosing scope.
            var prologue = new[]
            {
                "BasicLang::NetRef " + guard + " = BasicLang::net::"
                + form.NewExportName + "(" + native + ");",
            };

            emission = byRef
                ? NetArgEmission.Statements(
                    new[] { guard },
                    prologue,
                    // SUCCESS-PATH ONLY, and right that way: a managed throw means the callee
                    // wrote nothing back, so neither do we. §8.6's ref/out EXEMPTION from
                    // §14.11 lives on this line — delete it and the by-value divergence
                    // silently swallows a ref slot.
                    writeBack: new[]
                    {
                        native + " = BasicLang::net::" + form.ReadExportName + "(" + guard + ");",
                    })
                : NetArgEmission.Statements(new[] { guard }, prologue);
            return true;
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

            // §8.6 row 4 — a ref/out ARRAY slot: copies in AND reads back. Handled before the
            // by-value-scalar gate below, which would otherwise refuse it for having no single
            // wire slot. The ownership objection that gate names does not apply here: the handle
            // in the slot is one this frame MINTED, not one it borrowed.
            if (argument is IRVariable
                && TryMarshalNetArrayArgument(
                    targetDisplay, paramType, position, argument, byRef: true, out var byRefArray))
            {
                return byRefArray;
            }

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
                // SUCCESS-PATH ONLY, and correct that way: a managed throw means the callee
                // wrote nothing, so .NET would not write back either (NetArgEmission's contract).
                writeBack: new[] { native + " = " + outbound + ";" });
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
            IRValue resultNode, NetMemberDescriptor target, NetCallEmission emission) =>
            // The Func is not ceremony: it is what makes "compute the statement BEFORE writing
            // the prologue" STRUCTURAL rather than conventional. See EmitNetCallStatements.
            EmitNetCallStatements(emission, () => NetResultStatement(resultNode, target, emission.Expression));

        /// <summary>
        /// THE one place a proxy call becomes C++ statements (P2a-2 Task-10 Step 0, quality
        /// review M1): prologue → call statement → SUCCESS-PATH-ONLY write-back. Extracted
        /// because the identical three-part loop was written out at three call sites
        /// (<see cref="EmitNetResult"/>, <see cref="TryLowerNetFieldStore"/>, and — since Task 9
        /// — <see cref="TryLowerNetIndexerStore"/>; the Task-8 review counted two because the
        /// third did not exist yet). Three copies means the seam contract had to be re-obeyed
        /// three times.
        ///
        /// <para><b><paramref name="callStatement"/> is a FACTORY, not a string, and that is the
        /// point.</b> Every result arm can REFUSE (<see cref="NetLoweringRefusal"/>), and a
        /// refusal that had already written the argument prologue would leave dangling
        /// temporaries in a translation unit the build is about to reject anyway — but it would
        /// also make the failure read as a codegen bug rather than the §8.3 gap it is. Taking
        /// the factory means "compute before write" is enforced by the shape of this method
        /// instead of by each caller remembering to order two lines correctly.</para>
        ///
        /// <para>For the write-back's success-path-only status and why releases live in the
        /// prologue as RAII guards instead, see <see cref="NetArgEmission"/>.</para>
        /// </summary>
        private void EmitNetCallStatements(NetCallEmission emission, Func<string> callStatement)
        {
            var statement = callStatement();

            // ⛔ THE BRACES ARE LOAD-BEARING — and only when there IS a prologue, because only a
            // prologue DECLARES anything.
            //
            // This backend flattens ALL control flow to labels + `goto` inside ONE flat function
            // scope (see EmitInlineRegion). A prologue line like §8.6's
            // `BasicLang::NetRef t0 = net::New_…(a);` is therefore a declaration-with-initializer
            // sitting in the same scope as every label in the function — so ANY forward `goto`
            // emitted for an enclosing `If`/loop jumps ACROSS its initialization, which C++
            // forbids outright:
            //
            //     error: jump to label 'if0_end' crosses initialization of 'NetRef t0'
            //
            // i.e. `If x Then list.Sort(arr)` — a §8.6 copy-in anywhere inside a branch or loop —
            // emitted C++ that did not compile. Every fixture for this seam is straight-line,
            // which is why the whole suite stayed green over it. The same hazard covers Task 8's
            // scalar ref/out prologue and Task 11's callback register/unregister slot, since all
            // three share this one seam.
            //
            // A brace-enclosed region fixes it for all of them at once: a goto only ever targets
            // labels at the OUTER level (the region contains none of its own), so no jump can
            // cross into the middle of it. It also TIGHTENS the RAII guards — they now release at
            // the end of the call's own scope rather than the function's — while preserving the
            // reverse-release ordering that C++'s reverse destruction of automatics gives free.
            // Same answer, same reasons, as the For Each arm's braces in EmitNetEnumeration.
            var scoped = emission.Prologue.Count > 0;
            if (scoped)
            {
                WriteLine("{");
                Indent();
            }

            foreach (var line in emission.Prologue) WriteLine(line);
            WriteLine(statement);
            foreach (var line in emission.WriteBack) WriteLine(line);

            if (scoped)
            {
                Unindent();
                WriteLine("}");
            }
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

            // §8.6 row 2, AT THE CALL SITE. `Dim V() As Integer = obj.GetValues()` normally
            // lowers as `t = call(); V = t;` and the store materializes — but the optimizer fuses
            // that pair into `V = call();`, and then this is the only place the copy can happen.
            // The DECLARATION decides, which is row 2's own rule (see EffectiveCppType). The call
            // expression is a NetRef prvalue and the readback proxy takes `const NetRef&`, so the
            // handle lives exactly as long as the full-expression that consumes it.
            if (NetArrayCopy.TryGetFormForArray(resultType, out var arrayForm)
                && string.Equals(EffectiveCppType(resultNode),
                                 "std::vector<" + arrayForm.NativeElement + ">",
                                 StringComparison.Ordinal))
            {
                return $"{destination} = BasicLang::net::{arrayForm.ReadExportName}({expression});";
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
            EmitNetCallStatements(emission, () => emission.Expression + ";");
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
        /// a trailing statement, exactly as the plan's emission-seam contract requires. The
        /// managed <c>Dispose</c> is a trailing STATEMENT and therefore success-path only: it
        /// runs when the loop ends normally, and a <c>Return</c> out of the body skips it — the
        /// same known limitation this backend already documents for <c>Return</c> inside a
        /// <c>Try</c>. The native handle is released either way.</para>
        ///
        /// <para><b><c>Exit For</c> does not reach it, and does not exit either — that is
        /// PRE-EXISTING and shared with the native <c>For Each</c> arm.</b>
        /// <c>IRBuilder.Visit(ForEachLoopNode)</c> pushes
        /// <c>LoopContext(endBlock, endBlock)</c>, so a loop's BREAK target and its CONTINUE
        /// target are the same block, and <see cref="RegionEnd.LoopContinue"/> lowers every
        /// branch to it as <c>continue;</c>. An <c>Exit For</c> in a <c>For Each</c> therefore
        /// starts the next iteration on this backend rather than leaving the loop. Faithfully
        /// reproduced here rather than silently diverging from the arm beside it; the lowering
        /// itself is chipped separately (<c>task_4cc381f1</c>).</para>
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
            EmitNetCallStatements(emission, () => emission.Expression + ";");
            return true;
        }
    }
}
