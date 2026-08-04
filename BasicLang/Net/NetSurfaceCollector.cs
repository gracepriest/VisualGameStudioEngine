using System;
using System.Collections.Generic;
using System.Linq;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.Net;   // NetWrapperOrigin — §11.3's provenance element
using BasicLang.Compiler.IR;
using BasicLang.Compiler.ProjectSystem;
using Microsoft.CodeAnalysis;

namespace BasicLang.Net
{
    /// <summary>
    /// Spec §10.1 phase 3 — builds the project's <see cref="NetSurface"/> from its two sources
    /// (P2a-2 Task 3):
    ///
    /// <para><b>§7.1 BL-inferred (used-only).</b> Walks the OPTIMIZED IR — the same modules
    /// <c>CppCodeGenerator.GenerateSplit</c> emits from, so a call the optimizer deleted never
    /// costs a proxy slot — and collects the descriptor of every node carrying non-null,
    /// EXACT carriage (<c>ResolvedNetTarget</c> on <c>IRCall</c> / <c>IRInstanceMethodCall</c>
    /// from Task 2, plus <c>IRNewObject</c> / <c>IRFieldAccess</c> / <c>IRFieldStore</c> from
    /// Task 7a — the five node types the C++ lowering has arms for) whose <c>NetCategory</c>
    /// is NOT natively handled ({<c>NativeOwned</c>, <c>Bridged</c>}). Never the reference
    /// closure: used-only is what keeps the shim, and therefore the AOT publish, small.</para>
    ///
    /// <para><b>⚠ "Resolved" does NOT imply "annotated" — this collector sees exactly what
    /// carries carriage, nothing more.</b> The analyzer's probe deliberately suppresses
    /// recording for: claimed names and claimed calls (spec §6.5 — <c>Console.WriteLine</c>
    /// stays native), <c>NativeBclSurface</c>-owned members (the P1 six's native surface),
    /// <c>System.Object</c> members (<c>x.ToString()</c> is valid on every type the resolver
    /// deliberately reports no surface for), unresolvable and zero-surface types, and every
    /// compilation without a <c>NetResolverFactory</c>. See <see cref="NetAstAnnotations"/> for
    /// the enumerated list. Do not "fix" an absent member here — absence of carriage is the
    /// designed inert state, and it is what keeps every existing program's surface
    /// <see cref="NetSurface.Empty"/>.</para>
    ///
    /// <para><b>§7.2 declared (<c>&lt;NetProxy Include="Full.Type.Name"/&gt;</c>).</b> Nothing
    /// walks a hand-written <c>.cpp</c>, so declared types expand to their FULL public surface —
    /// through <see cref="NetTypeResolver.CandidateMembers"/>, which is THE seam and already
    /// encodes the corrected §7.2 rules (methods/properties/fields from the whole base chain
    /// minus <c>System.Object</c>; <b>constructors from the queried type only</b> — the
    /// <c>FileNotFoundException</c> 5-vs-15 measurement; signature-complete identity). This
    /// class consumes that seam and NEVER re-derives member rules. The D-P1 two-name
    /// <c>System.Object</c> allowlist (<c>ToString</c>/<c>GetHashCode</c>) is not in the seam
    /// yet — it lands in Task 4 Step 2a and declared surfaces inherit it automatically.</para>
    ///
    /// <para><b>§7.2 omission (BL6026, always a warning).</b> A declared-surface member is
    /// SKIPPED — never an error — when its signature contains a type §8.3 cannot carry, or when
    /// it, its accessors, or its declaring type carries <c>[RequiresDynamicCode]</c> /
    /// <c>[RequiresUnreferencedCode]</c>. Both signals are read from Roslyn symbols HERE, at
    /// phase 3 — never from ILC output, which does not run until phase 5: keying omission on ILC
    /// would be circular (the omission set determines the phase-4 proxy header that phase 5
    /// compiles) and would break §12.4's "proxy slots ≡ shim exports" by construction. The
    /// omission set is therefore final before any proxy header is emitted. Consequence stated
    /// plainly: the generated proxy overload set is a SUBSET of the .NET overload set — without
    /// this rule <c>&lt;NetProxy Include="…Regex"/&gt;</c> would fail on
    /// <c>IsMatch(ReadOnlySpan&lt;Char&gt;)</c>. BL-INFERRED members are deliberately NOT
    /// filtered here: a member the program actually calls with an unmarshalable shape is
    /// BL6019/BL6020 territory (an error at the use site), not a silent omission.</para>
    ///
    /// <para><b>Diagnostics.</b> Transport-neutral <see cref="NetReferenceDiagnostic"/>s
    /// appended to the caller's list, exactly the channel <see cref="NetReferenceResolver"/>
    /// uses; <c>CppProjectBuilder</c> merges them into the closure and maps them onto the build
    /// result. BL6022 (unknown declared type, ERROR — §11.4 marks only BL6026 as a warning);
    /// BL6023 (declared type found in two references, ERROR here: the declaration cannot be
    /// honored and a silent zero-member expansion would be the worst outcome); BL6022 also
    /// covers a resolved-but-not-effectively-public type, because a shim referencing it dies in
    /// <c>csc</c> with CS0122 — the late failure this pipeline exists to move earlier.</para>
    ///
    /// <para><b>Dedup and determinism.</b> Members are de-duplicated by
    /// <see cref="NetNameMangler.Mangle"/> — the same key <c>NetProxyEmitter</c> and the shim
    /// generator collapse on, so §12.4's "slots ≡ exports" survives — and collected in a stable
    /// order (IR program order, then declared types in declaration order, each in
    /// <see cref="NetTypeResolver.CandidateMembers"/>'s derived-to-base order), because the
    /// mangled set is part of the shim cache key (§10.2).</para>
    ///
    /// <para><b><see cref="NetSurface.DeclaredTypeNames"/> keeps every declaration verbatim,
    /// including ones that failed BL6022.</b> A declaration is a user statement of intent;
    /// dropping a failed one would let a Library project skip BL6025 and build a shim-less
    /// binary that looks clean and does nothing.</para>
    /// </summary>
    internal static class NetSurfaceCollector
    {
        /// <summary>
        /// Collects the surface. <paramref name="optimizedModules"/> tolerates null/empty (a
        /// pure-C++ project has no IR at all); <paramref name="resolverFactory"/> is forced ONLY
        /// when the project declares a <c>&lt;NetProxy&gt;</c>, so a project that never mentions
        /// .NET keeps paying nothing for Roslyn.
        /// </summary>
        /// <param name="declaredProvenance">
        /// P2a-2 Task 7b (spec §11.3, tier 1): when non-null, every member a <c>&lt;NetProxy&gt;</c>
        /// expansion contributes is recorded here as (mangled export name →
        /// <see cref="NetWrapperOriginKind.NetProxyDeclaration"/> origin), so a BL6020 raised
        /// against the generated wrapper can say "reached through &lt;NetProxy Include='X'/&gt;"
        /// instead of pointing at a <c>.bas</c> call site the user never wrote. Recorded HERE
        /// because this is the only place that still knows WHICH declaration produced a member —
        /// <see cref="NetSurface.Members"/> is a flat list, and a base-chain member's
        /// <c>DeclaringTypeFullName</c> is a base type, not the declared name, so the mapping
        /// cannot be recovered downstream. §7.1 call-site origins come from the analyzer's
        /// annotation table instead and are merged by the caller.
        /// </param>
        public static NetSurface Collect(
            IEnumerable<IRModule> optimizedModules,
            ProjectFile project,
            Func<NetTypeResolver> resolverFactory,
            ICollection<NetReferenceDiagnostic> diagnostics,
            ICollection<KeyValuePair<string, NetWrapperOrigin>> declaredProvenance = null)
        {
            if (diagnostics == null)
                throw new ArgumentNullException(nameof(diagnostics));

            var members = new List<NetMemberDescriptor>();
            var seenMangled = new HashSet<string>(StringComparer.Ordinal);

            // ---- §7.1: BL-inferred, used-only ------------------------------------------------
            foreach (var module in optimizedModules ?? Enumerable.Empty<IRModule>())
            {
                if (module?.Functions == null)
                    continue;

                var visited = new HashSet<IRInstruction>();   // reference identity — IR nodes
                                                              // have no Equals override
                foreach (var function in module.Functions)
                {
                    if (function?.Blocks == null)
                        continue;
                    foreach (var block in function.Blocks)
                        foreach (var instruction in block.Instructions)
                            CollectFromInstruction(instruction, visited, members, seenMangled);
                }
            }

            // Verbatim declarations, malformed entries INCLUDED: a whitespace Include is
            // still a user statement of intent (BL6022 below), and DeclaredTypeNames must
            // carry it so a Library project cannot skip BL6025 through a typo.
            var declared = project?.NetProxyTypes == null
                ? new List<string>()
                : new List<string>(project.NetProxyTypes);

            // ---- §7.2: declared -------------------------------------------------------------
            if (declared.Count > 0)
            {
                NetTypeResolver resolver = null;
                var expandedTypes = new HashSet<string>(StringComparer.Ordinal);
                foreach (var typeName in declared)
                {
                    if (!expandedTypes.Add(typeName ?? string.Empty))
                        continue;   // the same Include twice is one surface, not two diagnostics

                    // An empty/whitespace Include must not vanish silently: no type can be
                    // meant, so it is the same "names an unknown type" failure BL6022 exists
                    // for. Checked BEFORE the resolver is forced — a declaration that names
                    // nothing needs no Roslyn.
                    if (string.IsNullOrWhiteSpace(typeName))
                    {
                        diagnostics.Add(new NetReferenceDiagnostic("BL6022",
                            "<NetProxy> Include names no type (it is empty or whitespace). "
                            + "Name a fully-qualified .NET type, e.g. "
                            + "<NetProxy Include=\"System.Text.RegularExpressions.Regex\" />.",
                            IsWarning: false));
                        continue;
                    }

                    if (resolver == null)
                    {
                        if (resolverFactory == null)
                            throw new ArgumentNullException(nameof(resolverFactory),
                                "<NetProxy> declarations require a resolver factory.");
                        resolver = resolverFactory();
                    }

                    ExpandDeclaredType(resolver, typeName, members, seenMangled, diagnostics,
                        declaredProvenance, project?.FilePath);
                }
            }

            return members.Count == 0 && declared.Count == 0
                ? NetSurface.Empty
                : new NetSurface(members, declared);
        }

        // ------------------------------------------------------------------------------
        // §7.1 — the IR walk.
        // ------------------------------------------------------------------------------

        /// <summary>
        /// Natively handled categories (spec C1): calls whose receiver the native side owns
        /// outright never need a proxy slot. EXPLICIT set membership on purpose — the P2a-1
        /// trap: <see cref="BoundaryTypeCategory.NativeOwned"/> is enum value 0, so any
        /// default-comparison shortcut silently marks every call "natively handled".
        /// </summary>
        private static bool IsNativelyHandled(BoundaryTypeCategory category) =>
            category == BoundaryTypeCategory.NativeOwned
            || category == BoundaryTypeCategory.Bridged;

        /// <summary>
        /// Whether one node's carriage contributes to the surface: resolved, not natively
        /// handled, and — since P2a-2 Task 7a — EXACT.
        ///
        /// <para><b>The exactness condition is defence in depth for §12.4.</b> A name-only
        /// descriptor (first name match in metadata order, no overload probe) can never be
        /// LOWERED — <c>CppCodeGenerator</c> refuses it — so collecting one would mint a proxy
        /// slot and a shim export for a member no call site reaches, and could name the wrong
        /// overload while doing it. Today that cannot happen on the build path for a structural
        /// reason (<c>GenerateSplit</c> runs, and throws, before <c>NetProxyEmitter.Emit</c>),
        /// which means the invariant rests on CALL ORDER. This check moves it onto the DATA, so
        /// a future reordering — or any caller that collects without generating, as the tests
        /// and the phase-3 IntelliSense path do — cannot quietly break it.</para>
        /// </summary>
        private static bool IsCollectable(
            NetMemberDescriptor target, bool exact, BoundaryTypeCategory category) =>
            target != null && exact && !IsNativelyHandled(category);

        /// <summary>
        /// Visits one instruction, its structured children (<see cref="IRTryCatch"/> holds
        /// nested instruction lists — the one structured statement, mirroring
        /// <c>CppCapabilityChecker.CheckInstruction</c>'s recursion; loop and branch bodies are
        /// ordinary CFG blocks already covered by the function walk), and its operand
        /// expressions (<see cref="IROperandWalker.EnumerateOperands"/> — a resolved call can
        /// sit in argument position of another call and never appear as a top-level
        /// instruction). The visited set makes aliased nodes (an instruction that is also a
        /// later instruction's operand) cost one visit, and makes any accidental cycle safe.
        /// </summary>
        private static void CollectFromInstruction(
            IRInstruction instruction,
            HashSet<IRInstruction> visited,
            List<NetMemberDescriptor> members,
            HashSet<string> seenMangled)
        {
            if (instruction == null || !visited.Add(instruction))
                return;

            // The carriers are exactly the FIVE node types the C++ lowering has an arm for
            // (IRCall, IRInstanceMethodCall, IRNewObject, IRFieldAccess, IRFieldStore — a
            // construction carries its resolved ctor; a member READ carries the property/field
            // descriptor, i.e. the getter-shaped slot; a member WRITE carries the synthesized
            // set_X accessor from NetAccessorSynthesis, collected verbatim so §12.4's
            // slots ≡ exports holds). Asked through INetCarrying rather than five near-identical
            // `case` arms: making a SIXTH node type carry .NET resolution without giving the
            // lowering an arm for it is the hazard IRBaseMethodCall's deletion comment records —
            // the member would reach the surface (a proxy slot AND a shim export) while its call
            // site fell through to legacy emission with no exactness gate. The interface makes
            // that a deliberate act on the node rather than an omission here.
            if (instruction is INetCarrying carrying
                && IsCollectable(carrying.ResolvedNetTarget, carrying.ResolvedNetTargetIsExact,
                                 carrying.NetCategory))
            {
                AddMember(carrying.ResolvedNetTarget, members, seenMangled);
            }

            if (instruction is IRTryCatch tryCatch)
            {
                if (tryCatch.TryBlock != null)
                    foreach (var nested in tryCatch.TryBlock.Instructions)
                        CollectFromInstruction(nested, visited, members, seenMangled);
                foreach (var catchClause in tryCatch.CatchClauses)
                    if (catchClause?.Block != null)
                        foreach (var nested in catchClause.Block.Instructions)
                            CollectFromInstruction(nested, visited, members, seenMangled);
                if (tryCatch.FinallyBlock != null)
                    foreach (var nested in tryCatch.FinallyBlock.Instructions)
                        CollectFromInstruction(nested, visited, members, seenMangled);
            }

            foreach (var operand in IROperandWalker.EnumerateOperands(instruction))
                CollectFromInstruction(operand, visited, members, seenMangled);
        }

        private static void AddMember(
            NetMemberDescriptor descriptor,
            List<NetMemberDescriptor> members,
            HashSet<string> seenMangled,
            ICollection<KeyValuePair<string, NetWrapperOrigin>> provenance = null,
            NetWrapperOrigin origin = null)
        {
            var mangled = NetNameMangler.Mangle(descriptor);
            if (!seenMangled.Add(mangled))
                return;
            members.Add(descriptor);

            // Recorded only for the member that actually WON the dedup, so the map's key set is a
            // subset of the emitted export set by construction (NetShimGenerator.Provenance
            // intersects again — belt and braces, because the two dedups are different code).
            if (provenance != null && origin != null)
                provenance.Add(new KeyValuePair<string, NetWrapperOrigin>(mangled, origin));
        }

        // ------------------------------------------------------------------------------
        // §7.2 — declared-type expansion.
        // ------------------------------------------------------------------------------

        private static void ExpandDeclaredType(
            NetTypeResolver resolver,
            string typeName,
            List<NetMemberDescriptor> members,
            HashSet<string> seenMangled,
            ICollection<NetReferenceDiagnostic> diagnostics,
            ICollection<KeyValuePair<string, NetWrapperOrigin>> provenance,
            string projectFilePath)
        {
            var lookup = resolver.ResolveTypeDetailed(typeName);
            switch (lookup.Outcome)
            {
                case NetTypeLookupOutcome.NotFound:
                    diagnostics.Add(new NetReferenceDiagnostic("BL6022",
                        $"<NetProxy Include=\"{typeName}\" /> names an unknown .NET type: "
                        + $"'{typeName}' was not found in the project's referenced assemblies. "
                        + "Add a <Reference> for the assembly that declares it, or correct the "
                        + "name (generic types are spelled with their arity, e.g. "
                        + "System.Collections.Generic.List`1).",
                        IsWarning: false));
                    return;

                case NetTypeLookupOutcome.Ambiguous:
                    // BL6023 is "ambiguous .NET TYPE reference" (§6.5/§11.4) and this is
                    // exactly that shape. An ERROR on this path — unlike the analyzer's
                    // pre-flip warning-only probe — because the declaration cannot be honored:
                    // expanding to zero members silently would strand the user with proxy
                    // artifacts that export nothing.
                    diagnostics.Add(new NetReferenceDiagnostic("BL6023",
                        $"<NetProxy Include=\"{typeName}\" />: '{typeName}' is declared in more "
                        + "than one referenced assembly, so the surface to generate is ambiguous. "
                        + "Drop one of the references.",
                        IsWarning: false));
                    return;
            }

            if (lookup.Type != null && !lookup.Type.IsPublic)
            {
                // Effective accessibility (a public type nested inside an internal one reports
                // false). The generated shim would reference the type and die in csc with
                // CS0122 — the late-failure shape phase 3 exists to move earlier.
                diagnostics.Add(new NetReferenceDiagnostic("BL6022",
                    $"<NetProxy Include=\"{typeName}\" />: '{typeName}' resolved but is not "
                    + "publicly accessible, so no proxy surface can be generated for it.",
                    IsWarning: false));
                return;
            }

            // D-P1 interaction (P2a-2 Task 4): the QUERIED type's own AOT-hostility covers its
            // whole declared surface. FindAotHostileCarrier checks each member's DECLARING
            // type, which for the D-P1 System.Object allowlist entries is System.Object — an
            // AOT-hostile declared type would otherwise leak exactly ToString()/GetHashCode()
            // into its surface (caught by DeclaredType_AotHostileDeclaringType_OmitsEveryMember).
            // One origin object per declaration, shared by every member it expands to: the
            // <NetProxy> item IS the location, and there is no finer one — the .blproj is read
            // through ProjectFile, which does not record the item's XML line (hence line 0, which
            // NetWrapperOrigin.FormatLocation degrades to a bare file name).
            var declarationOrigin = provenance == null
                ? null
                : NetWrapperOrigin.NetProxyDeclaration(projectFilePath ?? string.Empty, typeName);

            string queriedTypeAttribute = null;
            var queriedSymbol = resolver.TypeSymbol(typeName);
            var queriedTypeHostile = queriedSymbol != null
                && HasAotHostileAttribute(queriedSymbol, out queriedTypeAttribute);

            foreach (var (symbol, descriptor) in resolver.CandidateMembers(typeName))
            {
                if (queriedTypeHostile)
                {
                    diagnostics.Add(new NetReferenceDiagnostic("BL6026",
                        $"<NetProxy> type '{typeName}': member '{descriptor}' was omitted from "
                        + $"the generated surface: the declared type itself is marked "
                        + $"[{queriedTypeAttribute}], which cannot run under Native AOT. The "
                        + "generated proxy overload set is a subset of the .NET overload set "
                        + "(spec §7.2).",
                        IsWarning: true));
                    continue;
                }

                if (TryGetOmissionReason(symbol, descriptor, out var reason))
                {
                    diagnostics.Add(new NetReferenceDiagnostic("BL6026",
                        $"<NetProxy> type '{typeName}': member '{descriptor}' was omitted from "
                        + $"the generated surface: {reason} The generated proxy overload set is "
                        + "a subset of the .NET overload set (spec §7.2).",
                        IsWarning: true));   // ALWAYS a warning — §11.4's one warning-only row
                    continue;
                }

                AddMember(descriptor, members, seenMangled, provenance, declarationOrigin);
            }
        }

        // ------------------------------------------------------------------------------
        // §7.2 omission rules — Roslyn symbols at phase 3, never ILC output.
        // ------------------------------------------------------------------------------

        private const string RequiresDynamicCode =
            "System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute";
        private const string RequiresUnreferencedCode =
            "System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute";

        private static bool TryGetOmissionReason(
            ISymbol symbol, NetMemberDescriptor descriptor, out string reason)
        {
            // §7.2's two omission bullets, checked in reverse bullet order (an attribute
            // probe is cheaper than a signature walk).
            // (a) AOT-hostility: the member, its accessors, or its declaring type.
            var hostileCarrier = FindAotHostileCarrier(symbol, out var attributeName);
            if (hostileCarrier != null)
            {
                reason = $"{hostileCarrier} is marked [{attributeName}], which cannot run "
                       + "under Native AOT.";
                return true;
            }

            // (b) a signature type outside §8.3's rows. A GENERIC METHOD is the degenerate
            // case of the same rule: its type parameters are types §8.3 has no wire form for,
            // whether or not they appear in the parameter list — a monomorphic C export cannot
            // carry an open T.
            if (symbol is IMethodSymbol { Arity: > 0 } genericMethod)
            {
                reason = $"it is a generic method (type parameter "
                       + $"'{genericMethod.TypeParameters[0].Name}' has no wire form at the "
                       + "native boundary — spec §8.3).";
                return true;
            }

            foreach (var signatureType in SignatureTypes(symbol))
            {
                var offender = FirstUnmarshalable(signatureType);
                if (offender != null)
                {
                    reason = $"its signature uses '{offender.ToDisplayString()}', which has no "
                           + "wire form at the native boundary (spec §8.3).";
                    return true;
                }
            }

            reason = null;
            return false;
        }

        /// <summary>
        /// The symbol that carries a <c>[RequiresDynamicCode]</c> /
        /// <c>[RequiresUnreferencedCode]</c>, phrased for the BL6026 message, or null. Checks
        /// the member itself, a property's accessors (the attributes commonly sit on the getter,
        /// e.g. <c>Exception.TargetSite</c>), and the declaring type.
        /// </summary>
        private static string FindAotHostileCarrier(ISymbol symbol, out string attributeName)
        {
            if (HasAotHostileAttribute(symbol, out attributeName))
                return "it";

            if (symbol is IPropertySymbol property)
            {
                if (property.GetMethod != null && HasAotHostileAttribute(property.GetMethod, out attributeName))
                    return "its getter";
                if (property.SetMethod != null && HasAotHostileAttribute(property.SetMethod, out attributeName))
                    return "its setter";
            }

            if (symbol.ContainingType != null && HasAotHostileAttribute(symbol.ContainingType, out attributeName))
                return $"its declaring type '{symbol.ContainingType.ToDisplayString()}'";

            attributeName = null;
            return null;
        }

        private static bool HasAotHostileAttribute(ISymbol symbol, out string attributeName)
        {
            foreach (var attribute in symbol.GetAttributes())
            {
                var name = attribute.AttributeClass?.ToDisplayString();
                if (name == RequiresDynamicCode || name == RequiresUnreferencedCode)
                {
                    attributeName = attribute.AttributeClass.Name;
                    return true;
                }
            }
            attributeName = null;
            return false;
        }

        /// <summary>
        /// Every type position a member's signature exposes at the boundary: return type
        /// (skipped for constructors — their export returns the created handle, §8.2),
        /// parameter types, a property's value type and its indexer parameters, a field's type.
        /// </summary>
        private static IEnumerable<ITypeSymbol> SignatureTypes(ISymbol symbol)
        {
            switch (symbol)
            {
                case IMethodSymbol method:
                    if (method.MethodKind != MethodKind.Constructor)
                        yield return method.ReturnType;
                    foreach (var parameter in method.Parameters)
                        yield return parameter.Type;
                    break;

                case IPropertySymbol property:
                    yield return property.Type;
                    foreach (var parameter in property.Parameters)   // an indexer's parameters
                        yield return parameter.Type;
                    break;

                case IFieldSymbol field:
                    yield return field.Type;
                    break;
            }
        }

        /// <summary>
        /// §8.3 projected onto Roslyn symbols. The UNMARSHALABLE set is small and closed:
        /// <c>System.Object</c> (permanently <c>Rejected</c> — void* erasure is unsound, and it
        /// is why every inherited <c>Equals(Object)</c> override is BL6026-omitted), open type
        /// parameters, pointers and function pointers, <c>ref struct</c> types
        /// (<c>Span&lt;T&gt;</c>, <c>ReadOnlySpan&lt;T&gt;</c>, <c>TypedReference</c>, the
        /// struct enumerators — cannot be boxed, <c>GCHandle.Alloc</c> cannot take one), and
        /// error types (a signature naming a type the closure cannot resolve is omitted
        /// honestly rather than guessed at). EVERYTHING else has a §8.3 row: primitives and
        /// enums by value, <c>Boolean</c>/<c>Char</c>/<c>String</c> with their pinned wire
        /// forms, the P1 <c>NativeOwned</c> six through §6.4 conversion pairs, other non-ref
        /// value types boxed, and every reference type (classes, interfaces, delegates, arrays)
        /// as an opaque handle — being a handle is the safe default, so an unlisted reference
        /// type costs a missing convenience, never a misinterpreted 64 bits.
        ///
        /// <para>Returns the OFFENDING type (for the diagnostic) or null when marshalable. For
        /// an array the element is judged (an array of an unmarshalable element would hand out
        /// handles no export can ever consume); for a closed generic only OPEN type-parameter
        /// occurrences in its arguments are hunted — <c>List&lt;Object&gt;</c> is itself a
        /// perfectly good handle.</para>
        ///
        /// <para><b>The openness hunt walks the CONTAINING chain, not just the type's own
        /// arguments</b> (P2a-2 Task 8b). <c>List&lt;T&gt;.Enumerator</c> declares no type
        /// parameters of its OWN, so the loop below saw an empty argument list and admitted it —
        /// yet the <c>T</c> it inherits from its container is every bit as unspellable in a
        /// monomorphic C export. The member then reached <c>csc</c> as
        /// <c>global::…List&lt;T&gt;.Enumerator</c> and failed there, positionless, in generated
        /// code the user never wrote — the late-failure shape §7.2's omission rules exist to move
        /// earlier. It is now a positioned BL6026 naming the open parameter instead.</para>
        ///
        /// <para>This omits MORE than it did: a <c>&lt;NetProxy Include="System.Collections.
        /// Generic.List`1" /&gt;</c> surface loses <c>GetEnumerator()</c> (and
        /// <c>Dictionary`2</c> loses <c>Keys</c>/<c>Values</c>/<c>GetEnumerator</c>) — members
        /// that could never have compiled. A CONSTRUCTED container is untouched:
        /// <c>List&lt;Int32&gt;.Enumerator</c> has a closed <c>Int32</c> in the chain and stays
        /// admissible, which is the shape §8.5's value-type receiver path actually wants.</para>
        /// </summary>
        private static ITypeSymbol FirstUnmarshalable(ITypeSymbol type)
        {
            if (type == null)
                return null;

            if (type is ITypeParameterSymbol)
                return type;

            if (type is IPointerTypeSymbol || type is IFunctionPointerTypeSymbol)
                return type;

            if (type.TypeKind == TypeKind.Error)
                return type;

            if (type.SpecialType == SpecialType.System_Object
                || type.SpecialType == SpecialType.System_TypedReference)
                return type;

            if (type.IsRefLikeType)
                return type;

            if (type is IArrayTypeSymbol array)
                return FirstUnmarshalable(array.ElementType);

            for (var named = type as INamedTypeSymbol; named != null; named = named.ContainingType)
            {
                foreach (var argument in named.TypeArguments)
                {
                    var open = FirstOpenTypeParameter(argument);
                    if (open != null)
                        return open;
                }
            }

            return null;
        }

        /// <summary>
        /// An open type parameter anywhere inside <paramref name="type"/>, or null. Distinct
        /// from <see cref="FirstUnmarshalable"/> on purpose: a generic REFERENCE type's
        /// arguments never cross through this member (the value is one opaque handle), so only
        /// OPENNESS disqualifies them — <c>Task&lt;T&gt;</c> with an unbound method-level
        /// <c>T</c> cannot be exported, while <c>Task&lt;Object&gt;</c> can.
        /// </summary>
        private static ITypeSymbol FirstOpenTypeParameter(ITypeSymbol type)
        {
            switch (type)
            {
                case ITypeParameterSymbol parameter:
                    return parameter;
                case IArrayTypeSymbol array:
                    return FirstOpenTypeParameter(array.ElementType);
                case IPointerTypeSymbol pointer:
                    return FirstOpenTypeParameter(pointer.PointedAtType);
                case INamedTypeSymbol named:
                    foreach (var argument in named.TypeArguments)
                    {
                        var open = FirstOpenTypeParameter(argument);
                        if (open != null)
                            return open;
                    }
                    return null;
                default:
                    return null;
            }
        }
    }
}
