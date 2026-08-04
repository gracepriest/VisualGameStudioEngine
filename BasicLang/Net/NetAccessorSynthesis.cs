using System;
using System.Collections.Generic;

namespace BasicLang.Net
{
    /// <summary>
    /// P2a-2 Task 7a — the ONE place accessor-method descriptors are synthesized from a
    /// property/field descriptor.
    ///
    /// <para><b>Why synthesis exists.</b> <c>NetTypeResolver.GetMembers</c> deliberately
    /// models a property as ONE member (its own remarks: "a property is ONE member, or every
    /// property costs three exports"), and <c>NetProxyEmitter</c> shapes that descriptor as
    /// the GETTER slot. A member WRITE therefore has no descriptor of its own — the setter is
    /// synthesized here, as a METHOD named with the CLR's real accessor metadata name
    /// (<c>set_X</c>), void return, one value parameter of the member's type. Static-ness is
    /// inherited from the member.</para>
    ///
    /// <para><b>Why one place is load-bearing.</b> The synthesized descriptor is mangled by
    /// three consumers — the IR stamping (<c>IRBuilder</c>), the surface collector, and the
    /// C++ lowering all reach it through the SAME <c>IRFieldStore.ResolvedNetTarget</c> stamp
    /// — and the shim generator's export must be the identical mangle. Synthesizing in two
    /// places would let the §12.4 slots-≡-exports invariant drift by construction.</para>
    /// </summary>
    internal static class NetAccessorSynthesis
    {
        /// <summary>The CLR accessor-name prefix a synthesized setter carries.</summary>
        internal const string SetterPrefix = "set_";

        /// <summary>
        /// True when a WRITE to <paramref name="member"/> can synthesize a <c>set_X</c>
        /// descriptor — i.e. exactly when <see cref="SetterFor"/> will not throw: a property or
        /// field with NO parameters.
        ///
        /// <para><b>P2a-2 Task-8 Step 0 (7b-I5): this predicate was spelled in three places,
        /// and the three had to agree.</b> <c>IRBuilder</c> PRODUCES the stamp,
        /// <c>NetAstAnnotations.CallSiteOrigins</c> ATTRIBUTES it (§11.3 tier 1), and
        /// <c>SemanticAnalyzer.RefuseWriteToUnsettableNetMember</c> REFUSES the unsettable case.
        /// Each carried its own copy of the same three clauses.</para>
        ///
        /// <para><b>P2a-2 Task 9 LIFTED the <c>Parameters.Count == 0</c> clause</b> — it was
        /// the indexer refusal, and §8.5's <c>get_Item</c>/<c>set_Item</c> pair is exactly what
        /// this task owns. The lift happened HERE, in the one shared predicate, which is what
        /// keeps the Task-7b guarantee intact: the analyzer's
        /// <c>RefuseWriteToUnsettableNetMember</c> reads the same predicate, so a write to a
        /// READ-ONLY indexer still draws the positioned BL6017 rather than minting a
        /// <c>set_Item</c> export that dies as CS0200/CS1546 inside generated C# after the
        /// ~27 s AOT publish. Relaxing the producer and the attributor while missing the
        /// analyzer copy is the failure this predicate exists to make impossible; there is
        /// nothing to miss when there is one copy.</para>
        ///
        /// <para>A FIELD can never be parameterized, so the widened clause changes nothing for
        /// fields.</para>
        ///
        /// <para>Says nothing about whether the write is LEGAL — that is
        /// <see cref="NetMemberDescriptor.IsSettable"/>, a separate question the analyzer asks
        /// on top of this one.</para>
        /// </summary>
        internal static bool HasSynthesizableSetter(NetMemberDescriptor member) =>
            member != null
            && (member.Kind == NetMemberCategory.Property || member.Kind == NetMemberCategory.Field);

        /// <summary>
        /// The synthesized <c>set_X</c> accessor-method descriptor for a property or field
        /// write. Throws for other member kinds — a caller asking for a "setter" of a method
        /// is a logic error, not a degradable input.
        ///
        /// <para><b>An INDEXER is now BUILT here (P2a-2 Task 9), where Task 7b refused it.</b>
        /// <c>NetTypeResolver.DescribeMember</c> records an indexer's index parameters on the
        /// Property descriptor, so the synthesized setter's parameter list is
        /// <c>(index…, value)</c> — the indices FIRST, the value LAST, which is both the CLR's
        /// own <c>set_Item</c> order and the order
        /// <see cref="IsSyntheticSetterShape"/> was already written to recognize ("last
        /// parameter is the value", never "exactly one parameter"). A blind one-parameter
        /// <c>set_X</c> would have described <c>set_Item(value)</c> for <c>this[i]</c> — a wrong
        /// descriptor, therefore a wrong mangled slot, a wrong export, and a generated shim that
        /// spells <c>target.Item = value</c> and dies in csc with CS1546. Building it in the one
        /// place the discipline lives is what keeps that impossible.</para>
        /// </summary>
        internal static NetMemberDescriptor SetterFor(NetMemberDescriptor member)
        {
            if (member == null) throw new ArgumentNullException(nameof(member));
            if (member.Kind != NetMemberCategory.Property && member.Kind != NetMemberCategory.Field)
                throw new ArgumentException(
                    $"Only a property or field has a synthesized setter; got {member.Kind} '{member.Name}'.",
                    nameof(member));

            // Indices first, value last — see the remarks. An ordinary property/field has no
            // indices and this degenerates to the Task-7a one-parameter shape byte for byte.
            var parameters = new List<NetParameterDescriptor>(member.Parameters.Count + 1);
            foreach (var index in member.Parameters)
                parameters.Add(index);
            parameters.Add(new NetParameterDescriptor(NetRefKind.None, member.TypeFullName));

            return new NetMemberDescriptor(
                SetterPrefix + member.Name,
                member.DeclaringTypeFullName,
                NetMemberCategory.Method,
                member.IsStatic,
                arity: 0,
                "System.Void",
                parameters,
                isSettable: true,
                synthesis: NetSyntheticKind.Setter);
        }

        // ------------------------------------------------------------------------------
        // §8.5's synthetic ARRAY accessors. .NET arrays expose NO indexer and no Length
        // METHOD in metadata — `Length` is a property on System.Array but reaching it through
        // the base type would describe `System.Array.Length`, which is not per-element and
        // gives the shim no element type to cast the receiver to. Array.GetValue(int) is not a
        // fallback either: it returns Object, which is permanently Rejected
        // (BoundaryTypeRegistry). So the three accessors are synthesized per ELEMENT type, and
        // NetShimGenerator spells their bodies as real C# indexing.
        // ------------------------------------------------------------------------------

        /// <summary>The <c>T[]</c> spelling these accessors are declared on.</summary>
        internal static string ArrayTypeFullName(string elementTypeFullName)
        {
            if (string.IsNullOrEmpty(elementTypeFullName))
                throw new ArgumentException("An element type is required.", nameof(elementTypeFullName));
            return elementTypeFullName + "[]";
        }

        /// <summary><c>T[] this[int]</c> read — spec §8.5's <c>bl_net_Array_Get</c> row.</summary>
        internal static NetMemberDescriptor ArrayGetFor(string elementTypeFullName) =>
            new NetMemberDescriptor(
                "get_Item",
                ArrayTypeFullName(elementTypeFullName),
                NetMemberCategory.Method,
                isStatic: false,
                arity: 0,
                elementTypeFullName,
                new List<NetParameterDescriptor>
                {
                    new NetParameterDescriptor(NetRefKind.None, "System.Int32"),
                },
                isSettable: true,
                synthesis: NetSyntheticKind.ArrayGet);

        /// <summary><c>T[] this[int] = v</c> write — §8.5's <c>_Set</c> row.</summary>
        internal static NetMemberDescriptor ArraySetFor(string elementTypeFullName) =>
            new NetMemberDescriptor(
                SetterPrefix + "Item",
                ArrayTypeFullName(elementTypeFullName),
                NetMemberCategory.Method,
                isStatic: false,
                arity: 0,
                "System.Void",
                new List<NetParameterDescriptor>
                {
                    new NetParameterDescriptor(NetRefKind.None, "System.Int32"),
                    new NetParameterDescriptor(NetRefKind.None, elementTypeFullName),
                },
                isSettable: true,
                synthesis: NetSyntheticKind.ArraySet);

        /// <summary><c>T[].Length</c> — §8.5's <c>_Length</c> row.</summary>
        internal static NetMemberDescriptor ArrayLengthFor(string elementTypeFullName) =>
            new NetMemberDescriptor(
                "get_Length",
                ArrayTypeFullName(elementTypeFullName),
                NetMemberCategory.Method,
                isStatic: false,
                arity: 0,
                "System.Int32",
                Array.Empty<NetParameterDescriptor>(),
                isSettable: true,
                synthesis: NetSyntheticKind.ArrayLength);

        // ------------------------------------------------------------------------------
        // §8.5's ENUMERATION trio, obtained and driven THROUGH THE INTERFACES.
        //
        // ⛔ NEVER the concrete struct-returning GetEnumerator() Roslyn would otherwise
        // select. For List<T>, Dictionary<K,V>, HashSet<T> and ImmutableArray<T> that
        // enumerator is a MUTABLE STRUCT; boxed into a handle (§8.3), a generated
        // ((List<int>.Enumerator)o!).MoveNext() mutates the TEMPORARY the unboxing conversion
        // produces — the box is untouched, MoveNext returns true forever and Current yields
        // element 0. An INFINITE LOOP, not a diagnostic. Interface dispatch on a boxed value
        // type operates on the box itself, which is what makes this route correct rather than
        // merely conservative — and it is why these descriptors declare IEnumerable<T> /
        // IEnumerator / IEnumerator<T> (all three REFERENCE types, so NetShimGenerator's
        // ordinary cast receiver is right and Unsafe.Unbox never enters the picture).
        //
        // The two obvious §12.3 tests — a .NET array, and an IEnumerable<T> from a
        // compiler-generated iterator (a CLASS) — BOTH PASS with the bug present. Only a
        // CONCRETE List<T> iteration catches it.
        // ------------------------------------------------------------------------------

        internal const string EnumerableInterfacePrefix = "System.Collections.Generic.IEnumerable<";
        internal const string EnumeratorInterfacePrefix = "System.Collections.Generic.IEnumerator<";
        internal const string NonGenericEnumerator = "System.Collections.IEnumerator";
        internal const string DisposableInterface = "System.IDisposable";

        internal static string EnumerableInterfaceFor(string elementTypeFullName) =>
            EnumerableInterfacePrefix + elementTypeFullName + ">";

        internal static string EnumeratorInterfaceFor(string elementTypeFullName) =>
            EnumeratorInterfacePrefix + elementTypeFullName + ">";

        /// <summary><c>IEnumerable&lt;T&gt;.GetEnumerator()</c> — returns the enumerator HANDLE.</summary>
        internal static NetMemberDescriptor EnumerableGetEnumeratorFor(string elementTypeFullName) =>
            new NetMemberDescriptor(
                "GetEnumerator",
                EnumerableInterfaceFor(elementTypeFullName),
                NetMemberCategory.Method,
                isStatic: false,
                arity: 0,
                EnumeratorInterfaceFor(elementTypeFullName),
                Array.Empty<NetParameterDescriptor>());

        /// <summary><c>IEnumerator.MoveNext()</c> — declared on the NON-generic interface, which
        /// is where the CLR declares it; <c>IEnumerator&lt;T&gt;</c> merely inherits it.</summary>
        internal static NetMemberDescriptor EnumeratorMoveNext() =>
            new NetMemberDescriptor(
                "MoveNext",
                NonGenericEnumerator,
                NetMemberCategory.Method,
                isStatic: false,
                arity: 0,
                "System.Boolean",
                Array.Empty<NetParameterDescriptor>());

        /// <summary><c>IEnumerator&lt;T&gt;.Current</c> — the GENERIC one, so the element arrives
        /// typed rather than as the permanently-<c>Rejected</c> <c>Object</c>.</summary>
        internal static NetMemberDescriptor EnumeratorCurrentFor(string elementTypeFullName) =>
            new NetMemberDescriptor(
                "Current",
                EnumeratorInterfaceFor(elementTypeFullName),
                NetMemberCategory.Property,
                isStatic: false,
                arity: 0,
                elementTypeFullName,
                Array.Empty<NetParameterDescriptor>(),
                isSettable: false);

        /// <summary><c>IDisposable.Dispose()</c> — <c>IEnumerator&lt;T&gt;</c> extends it, and
        /// C#'s own foreach lowering disposes, so the native loop does too.</summary>
        internal static NetMemberDescriptor EnumeratorDispose() =>
            new NetMemberDescriptor(
                "Dispose",
                DisposableInterface,
                NetMemberCategory.Method,
                isStatic: false,
                arity: 0,
                "System.Void",
                Array.Empty<NetParameterDescriptor>());

        /// <summary>
        /// True when <paramref name="member"/> is a synthesized setter shape: a void METHOD
        /// named <c>set_X</c> whose LAST parameter is the by-value value (any preceding ones
        /// being an indexer's indices). The shim generator spells such a member as a
        /// property/field ASSIGNMENT (<c>target.X = value</c>, or <c>target[i] = value</c>
        /// once §8.5 lands) — C# cannot call an accessor by its metadata name (CS0571).
        ///
        /// <para><b>"Last parameter is the value", not "exactly one parameter"</b>, on
        /// purpose: <see cref="SetterFor"/> refuses indexers today, but Task 9's correct
        /// <c>set_Item(index…, value)</c> descriptor must still be RECOGNIZED as synthetic
        /// when it arrives — a Count == 1 test would silently stop recognizing it and emit
        /// <c>target.set_Item(i, v)</c>, i.e. CS0571 in the generated shim.</para>
        ///
        /// <para>Caveat, stated rather than hidden: this is still SHAPE matching, so metadata
        /// from another compiler could declare an ORDINARY method named <c>set_X</c> and take
        /// this spelling; the generated shim would then fail to compile — loudly, in csc,
        /// never silently calling the wrong thing. Carrying intent explicitly (a synthesized-
        /// accessor flag on the descriptor, so the generator ASKS instead of guessing) is the
        /// robust fix and is noted for Task 9, which owns the indexer pair.</para>
        /// </summary>
        internal static bool IsSyntheticSetterShape(NetMemberDescriptor member) =>
            member != null
            && member.Kind == NetMemberCategory.Method
            && member.Name.StartsWith(SetterPrefix, StringComparison.Ordinal)
            && member.Name.Length > SetterPrefix.Length
            && member.Arity == 0
            && member.TypeFullName == "System.Void"
            && member.Parameters is { Count: >= 1 }
            && member.Parameters[member.Parameters.Count - 1].RefKind == NetRefKind.None;
    }
}
