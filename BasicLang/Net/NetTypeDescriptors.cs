using System.Collections.Generic;
using System.Linq;

namespace BasicLang.Net
{
    // ----------------------------------------------------------------------
    // The resolver's data model. Split out of NetTypeResolver.cs so the resolver file stays
    // about resolution — Task 5's overload resolution lands there next.
    //
    // NAMING: these are the "…Descriptor"/"…Category" types ON PURPOSE.
    //
    // BasicLang.Compiler.SemanticAnalysis ALREADY declares public NetTypeInfo, NetMemberInfo,
    // NetParameterInfo and NetMemberKind (TypeRegistry.cs:670-755), consumed by
    // SemanticAnalyzer.cs, SymbolTable.cs, ExternalLibraryLoader.cs and four LSP files. Those are
    // the LSP's loose IntelliSense shapes; these are the resolver's precise ones. They are
    // deliberately DISTINCT types and must not be "unified":
    //
    //   * the enums disagree on ordering — the old NetMemberKind is Method=0 … Constructor=5,
    //     this one is Constructor=0 … Field=3 — so any int round-trip between them silently
    //     mismaps a member's kind;
    //   * Task 7 modifies TypeRegistry.cs and Task 8 modifies SemanticAnalyzer.cs, both files
    //     where the OLD names are already in scope, and any file importing both namespaces would
    //     get CS0104 on the unqualified name.
    //
    // Renaming them here rather than there is the cheap direction: this namespace has one
    // consumer, that one has seven files.
    // ----------------------------------------------------------------------

    /// <summary>What a .NET type is, coarsely. Spec §6.1 makes kind part of the answer.</summary>
    internal enum NetTypeCategory { Class, Struct, Interface, Enum, Delegate, Other }

    /// <summary>
    /// The four member categories spec §7.2 admits into a generated surface: "public
    /// constructors, methods, properties and fields".
    /// </summary>
    internal enum NetMemberCategory { Constructor, Method, Property, Field }

    /// <summary>
    /// How a parameter is passed. Part of a CLR signature, so it must be carried: a method taking
    /// <c>ref T</c> and one taking <c>T</c> are two different members, and mangling both from the
    /// bare type name would give them one export name (§7.3).
    /// </summary>
    internal enum NetRefKind { None, Ref, Out, In, RefReadOnly }

    /// <summary>
    /// P2a-2 Task 9 (spec §8.5) — whether a descriptor names a REAL metadata member or one the
    /// compiler SYNTHESIZED, and which shape it synthesized.
    ///
    /// <para><b>Why intent is carried rather than guessed.</b>
    /// <see cref="NetAccessorSynthesis.IsSyntheticSetterShape"/> recognized a synthesized setter
    /// by its SHAPE (a void method named <c>set_X</c>), and its own remarks record the hole that
    /// leaves: metadata from another compiler may declare an ordinary method called
    /// <c>set_X</c>, and the shim would then spell it as an assignment. Task 9 adds three more
    /// synthesized shapes whose spellings are not merely different but UNRELATED to their names
    /// (<c>a[i]</c>, <c>a[i] = v</c>, <c>a.Length</c> for members called <c>get_Item</c> /
    /// <c>set_Item</c> / <c>get_Length</c>), so shape matching would have to guess three more
    /// times. The generator ASKS instead.</para>
    ///
    /// <para><b>Deliberately NOT part of the CLR signature</b>, exactly like
    /// <see cref="NetMemberDescriptor.IsSettable"/>: it is absent from
    /// <see cref="NetNameMangler"/>'s <c>CanonicalIdentity</c> and from
    /// <c>NetTypeResolver</c>'s duplicate-collapse key, so adding it moves NO export name.
    /// Identity is already unique without it — a synthesized array accessor's declaring type is
    /// an ARRAY spelling (<c>System.Int32[]</c>), which <c>DescribeMember</c> can never
    /// produce.</para>
    /// </summary>
    internal enum NetSyntheticKind
    {
        /// <summary>A real metadata member.</summary>
        None,

        /// <summary>
        /// <c>set_X(value)</c> for a property/field write, or <c>set_Item(index…, value)</c> for
        /// an indexer write. Spelled as an ASSIGNMENT — C# cannot call an accessor by its
        /// metadata name (CS0571).
        /// </summary>
        Setter,

        /// <summary>
        /// <c>get_Item(int32)</c> on a <c>T[]</c>. §8.5: .NET arrays expose NO indexer in
        /// metadata, and <c>Array.GetValue(int)</c> is not a fallback — it returns
        /// <c>Object</c>, which is permanently <c>Rejected</c>.
        /// </summary>
        ArrayGet,

        /// <summary><c>set_Item(int32, T)</c> on a <c>T[]</c>.</summary>
        ArraySet,

        /// <summary><c>get_Length()</c> on a <c>T[]</c>.</summary>
        ArrayLength,
    }

    /// <summary>
    /// Why a type lookup answered the way it did.
    ///
    /// <para><b>The three cases must stay distinct.</b>
    /// <c>Compilation.GetTypeByMetadataName</c> returns null for BOTH absence and ambiguity, and
    /// spec §11.4 gives those two different diagnostics — BL6016 ".NET type not found" versus
    /// BL6023 "ambiguous .NET type reference". Collapsing them here makes one of the two a lie in
    /// Task 8, and "type not found" for a type the user can see declared twice is the least
    /// actionable message the compiler could produce.</para>
    /// </summary>
    internal enum NetTypeLookupOutcome { Resolved, NotFound, Ambiguous }

    /// <summary>
    /// How the call site names its receiver, which C# — and therefore
    /// <see cref="NetTypeResolver.ResolveOverload"/> — cannot infer from the argument types.
    ///
    /// <para><b>Why the caller must say.</b> <c>Regex.IsMatch(s)</c> and <c>r.IsMatch(s)</c> select
    /// different members: the first names the type and can only reach <c>Shared</c> members, the
    /// second has a receiver and reaches both. Guessing would silently resolve the instance
    /// <c>IsMatch(String)</c> for a static call site that VB rejects, and Task 12 would emit the
    /// wrong export. Task 8 knows which it has — it is the difference between an identifier that
    /// resolved to a type and one that resolved to a variable.</para>
    ///
    /// <para><see cref="Instance"/> also covers VB's leniency about calling a <c>Shared</c> member
    /// through an instance, which C# forbids (CS0176); see
    /// <see cref="NetTypeResolver.ResolveOverload"/>.</para>
    /// </summary>
    internal enum NetCallForm { Instance, Static, Constructor }

    /// <summary>
    /// Why overload resolution answered the way it did.
    ///
    /// <para><b>The three cases must stay distinct, for the same reason
    /// <see cref="NetTypeLookupOutcome"/>'s three do.</b> Spec §11.4 gives BL6017 ".NET member not
    /// found / no matching overload" and BL6018 "ambiguous overload" different codes, and §6.5
    /// pins the split down further: "BL6018 covers ambiguous <i>overloads</i> only". Reporting "no
    /// such overload" for a call the user can see two candidates for is the least actionable
    /// message available, so the distinction has to survive to Task 8 rather than being recovered
    /// there.</para>
    ///
    /// <para><b><see cref="TypeUnavailable"/> exists so the ordering requirement cannot be
    /// forgotten.</b> Overload resolution presupposes a resolved type, and the three type-level
    /// answers — not found (BL6016), declared twice (BL6023), and resolved but not effectively
    /// public (also BL6016, because a shim referencing it fails in <c>csc</c> with CS0122) — are
    /// none of them a statement about overloads. Folding them into
    /// <see cref="NoMatch"/> would make Task 8 report "no matching overload" for a type that was
    /// never found, and §6.5 is explicit that BL6018 "covers ambiguous <i>overloads</i> only". A
    /// doc comment telling the caller to ask <see cref="NetTypeResolver.ResolveTypeDetailed"/> first
    /// is not enforcement; a distinct outcome is. Ask
    /// <see cref="NetTypeResolver.ResolveTypeDetailed"/> for WHICH of the three it was.</para>
    ///
    /// <para><b>Appended, not inserted.</b> New members go on the end for the reason this file's
    /// header gives about <c>NetMemberKind</c>: anything that round-trips an outcome through an int
    /// silently remaps every value after an insertion.</para>
    /// </summary>
    internal enum NetOverloadOutcome { Resolved, NoMatch, Ambiguous, TypeUnavailable }

    /// <summary>
    /// The winning member, or why there is none. <see cref="Member"/> is non-null only for
    /// <see cref="NetOverloadOutcome.Resolved"/> — an ambiguous call has no winner, and naming one
    /// would send Task 12's proxy at whichever candidate happened to be enumerated first.
    ///
    /// <para>Note the equality caveat <see cref="NetMemberDescriptor"/> documents: that type is
    /// deliberately not a record, so this record's synthesized <c>==</c> compares
    /// <see cref="Outcome"/> by value and <see cref="Member"/> by REFERENCE. Compare
    /// <c>Member.ToString()</c> when content equality is what is wanted.</para>
    /// </summary>
    internal sealed record NetOverloadResult(NetOverloadOutcome Outcome, NetMemberDescriptor? Member);

    /// <summary>
    /// A resolved .NET type. A record is safe here — every member is a string, enum or bool, so
    /// value equality means what it appears to mean (contrast <see cref="NetMemberDescriptor"/>).
    /// </summary>
    /// <param name="FullName">
    /// Namespace-qualified, nested types spelled with '.', generic type parameters spelled
    /// <c>&lt;T&gt;</c>. See <see cref="NetTypeResolver.ResolveType"/> for the round-trip caveat on
    /// generics.
    /// </param>
    /// <param name="IsPublic">
    /// EFFECTIVE accessibility, not declared: a public type nested inside an internal one reports
    /// false. See <see cref="NetTypeResolver.ResolveType"/>.
    /// </param>
    internal sealed record NetTypeDescriptor(string FullName, NetTypeCategory Kind, bool IsPublic);

    /// <summary>
    /// The outcome of a type lookup. <see cref="Type"/> is non-null only for
    /// <see cref="NetTypeLookupOutcome.Resolved"/> — an ambiguous reference has no single winner,
    /// and picking one silently is how a build ends up bound to whichever assembly happened to be
    /// enumerated first.
    /// </summary>
    internal sealed record NetTypeLookupResult(NetTypeLookupOutcome Outcome, NetTypeDescriptor Type);

    /// <summary>
    /// One parameter: how it is passed, and what type. A record is safe — both members are
    /// scalars, so value equality is real value equality.
    /// </summary>
    /// <param name="TypeFullName">
    /// Fully qualified and never a C# keyword or shorthand: <c>System.Int32</c> not <c>int</c>,
    /// <c>System.IntPtr</c> not <c>nint</c>, <c>System.ValueTuple&lt;…&gt;</c> not <c>(…)</c>. See
    /// <c>NetTypeResolver.TypeName</c> for why <c>ToDisplayString</c> alone is not enough.
    /// </param>
    internal sealed record NetParameterDescriptor(NetRefKind RefKind, string TypeFullName)
    {
        public override string ToString() =>
            (RefKind == NetRefKind.None ? "" : RefKind.ToString().ToLowerInvariant() + " ") + TypeFullName;
    }

    /// <summary>
    /// One member of a .NET type, carrying a COMPLETE CLR signature: declaring type, metadata
    /// name, kind, static-ness, generic arity, and each parameter's ref-kind and type.
    ///
    /// <para><b>"Complete" is the whole point, and it is what §7.3 needs.</b>
    /// <c>NetNameMangler</c> mangles from (declaring type, member name, parameter types) — but
    /// those three ALONE do not identify a member. Two axes are invisible in them and both occur
    /// in the real framework:</para>
    /// <list type="bullet">
    /// <item><description><b>Generic arity.</b> <c>IMethodSymbol.MetadataName</c> carries no arity
    /// suffix, so <c>Task.FromException(Exception)</c> and
    /// <c>Task.FromException&lt;T&gt;(Exception)</c> are indistinguishable without
    /// <see cref="Arity"/>. Measured across the public framework surface: 37 types, 186 members —
    /// including all 6 <c>Expression.Lambda</c>/<c>Lambda&lt;TDelegate&gt;</c> pairs and both
    /// <c>IQueryProvider.CreateQuery</c> overloads.</description></item>
    /// <item><description><b>Parameter ref-kind.</b>
    /// <c>EventSource.Write(String, EventSourceOptions, T)</c> and
    /// <c>Write(String, ref EventSourceOptions, ref T)</c> differ only in
    /// <see cref="NetParameterDescriptor.RefKind"/>.</description></item>
    /// </list>
    /// <para>Omitting either does not merely produce a bad export name — it makes
    /// <see cref="NetTypeResolver.GetMembers"/>'s duplicate collapse DELETE the overload, and a
    /// deleted overload is worse than a duplicated one: Task 5 then reports "no such overload"
    /// with no signal that the resolver ate it.</para>
    ///
    /// <para><b>Deliberately a class and not a record</b>, for the reason
    /// <see cref="NetReferenceClosure"/> documents: record equality over an
    /// <see cref="IReadOnlyList{T}"/> member degenerates to REFERENCE equality, so
    /// <c>==</c> on two members with identical parameter lists would look like a content
    /// comparison and behave like an identity one. Reference equality is the honest default here,
    /// and <see cref="ToString"/> covers the only thing record synthesis was actually buying us —
    /// readable assertion failures.</para>
    /// </summary>
    internal sealed class NetMemberDescriptor
    {
        public NetMemberDescriptor(
            string name,
            string declaringTypeFullName,
            NetMemberCategory kind,
            bool isStatic,
            int arity,
            string typeFullName,
            IReadOnlyList<NetParameterDescriptor> parameters,
            bool isSettable = true,
            NetSyntheticKind synthesis = NetSyntheticKind.None)
        {
            Name = name;
            DeclaringTypeFullName = declaringTypeFullName;
            Kind = kind;
            IsStatic = isStatic;
            Arity = arity;
            TypeFullName = typeFullName;
            Parameters = parameters;
            IsSettable = isSettable;
            Synthesis = synthesis;
        }

        /// <summary>
        /// The member's METADATA name: <c>".ctor"</c> for a constructor, <c>"Item"</c> for an
        /// indexer, the plain name otherwise. CLR method metadata names carry no generic-arity
        /// suffix — that is what <see cref="Arity"/> is for. <see cref="Kind"/> is what
        /// distinguishes a constructor, not the name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The type that DECLARES this member, fully qualified — for an inherited member that is
        /// the base type, not the type that was queried. §7.3 requires the mangler to be
        /// collision-free over the fully-qualified declaring type, so reporting the queried type
        /// here would collapse <c>Base.Foo</c> and <c>Derived.Foo</c> into one export.
        /// </summary>
        public string DeclaringTypeFullName { get; }

        public NetMemberCategory Kind { get; }

        public bool IsStatic { get; }

        /// <summary>
        /// Generic parameter count: 0 for everything except a generic method.
        /// <c>Task.FromException&lt;T&gt;</c> is arity 1, <c>Task.FromException</c> arity 0, and
        /// they are otherwise identical.
        /// </summary>
        public int Arity { get; }

        /// <summary>
        /// Return type for a method, value type for a property or field. For a constructor this
        /// is <c>System.Void</c>, which is what metadata says.
        /// </summary>
        public string TypeFullName { get; }

        /// <summary>
        /// Parameters in declaration order. Empty for fields and for ordinary properties; an
        /// INDEXER's parameters ARE recorded, because an indexer is a parameterized member and two
        /// of them differ only by those types.
        ///
        /// <para>Recording indexer parameters is load-bearing, not tidiness: it is what stops
        /// <see cref="NetTypeResolver.GetMembers"/>'s duplicate collapse from eating one of them.
        /// Measured on <c>System.Collections.Specialized.NameValueCollection</c>, whose
        /// <c>this[int]</c> and <c>this[string]</c> present one identity without this and two with
        /// it.</para>
        /// </summary>
        public IReadOnlyList<NetParameterDescriptor> Parameters { get; }

        /// <summary>
        /// P2a-2 Task 7b: whether a WRITE to this member is possible at all — a property with a
        /// publicly-callable, non-init setter, or a field that is neither <c>readonly</c> nor
        /// <c>const</c>. Meaningless (and <c>true</c>) for methods and constructors.
        ///
        /// <para><b>Deliberately NOT part of the CLR signature</b>, so it is absent from
        /// <see cref="NetNameMangler"/>'s input and from <c>NetTypeResolver</c>'s duplicate-collapse
        /// key. Two descriptors differing only here are the same member.</para>
        ///
        /// <para><b>Why it has to be carried rather than asked for later.</b> A write to a
        /// read-only .NET property synthesizes a <c>set_X</c> accessor
        /// (<see cref="NetAccessorSynthesis.SetterFor"/>) that names a member which does not exist;
        /// the generated shim then spells <c>target.X = value</c> and dies in <c>csc</c> — after
        /// the ~27 s AOT publish, against a file under <c>obj/gen/shim</c> the user never wrote,
        /// with no line in their program. The analyzer refuses the assignment instead (BL6017,
        /// positioned), and this bit is what it refuses on. Downstream (IRBuilder, the collector,
        /// the emitters) is deliberately left tolerant: the analyzer's finding does not abort
        /// <c>CompileUnit</c>, so a throw at the synthesis point would turn a clean diagnostic into
        /// a compiler crash.</para>
        ///
        /// <para>Defaults to <c>true</c> so hand-constructed descriptors — every test surface, and
        /// every §7.2 declared surface member before the resolver fills it in — behave exactly as
        /// they did before this bit existed.</para>
        /// </summary>
        public bool IsSettable { get; }

        /// <summary>
        /// P2a-2 Task 9: whether this descriptor names a real metadata member or a shape the
        /// compiler synthesized, and which. See <see cref="NetSyntheticKind"/> for why intent is
        /// carried rather than recovered from the member's name, and why it is deliberately not
        /// part of the CLR signature identity (so it moves no export name).
        /// </summary>
        public NetSyntheticKind Synthesis { get; }

        /// <summary>Parameter types only, for callers that do not care how they are passed.</summary>
        public IEnumerable<string> ParameterTypeFullNames => Parameters.Select(p => p.TypeFullName);

        public override string ToString() =>
            $"{(IsStatic ? "static " : "")}{Kind} {TypeFullName} {DeclaringTypeFullName}.{Name}"
            + (Arity == 0 ? "" : "`" + Arity)
            + (Parameters.Count == 0
               && (Kind == NetMemberCategory.Property || Kind == NetMemberCategory.Field)
                ? ""
                : "(" + string.Join(", ", Parameters) + ")");
    }
}
