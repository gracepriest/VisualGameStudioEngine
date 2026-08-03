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
        /// The synthesized <c>set_X</c> accessor-method descriptor for a property or field
        /// write. Throws for other member kinds — a caller asking for a "setter" of a method
        /// is a logic error, not a degradable input.
        ///
        /// <para><b>An INDEXER is refused here, at the single synthesis point.</b>
        /// <c>NetTypeResolver.DescribeMember</c> records an indexer's index parameters on the
        /// Property descriptor, so a blind one-parameter <c>set_X</c> would silently describe
        /// <c>set_Item(value)</c> for <c>this[i]</c> — a wrong descriptor, therefore a wrong
        /// mangled slot, a wrong export, and a generated shim that spells
        /// <c>target.Item = value</c> and dies in csc with CS1546. Refusing where the
        /// discipline lives beats any of those. §8.5's <c>get_Item</c>/<c>set_Item</c> pair is
        /// Task 9's work; when it lands, build the two-parameter descriptor HERE (index…,
        /// value) rather than at a call site.</para>
        /// </summary>
        internal static NetMemberDescriptor SetterFor(NetMemberDescriptor member)
        {
            if (member == null) throw new ArgumentNullException(nameof(member));
            if (member.Kind != NetMemberCategory.Property && member.Kind != NetMemberCategory.Field)
                throw new ArgumentException(
                    $"Only a property or field has a synthesized setter; got {member.Kind} '{member.Name}'.",
                    nameof(member));
            if (member.Parameters is { Count: > 0 })
                throw new NotSupportedException(
                    $"BL6019: '{member.DeclaringTypeFullName}.{member.Name}' is an INDEXER "
                    + $"({member.Parameters.Count} index parameter(s)); §8.5's get_Item/set_Item "
                    + "accessor pair is not lowered at the native boundary yet. Synthesizing a "
                    + "one-parameter set_X here would name a member that does not exist.");

            return new NetMemberDescriptor(
                SetterPrefix + member.Name,
                member.DeclaringTypeFullName,
                NetMemberCategory.Method,
                member.IsStatic,
                arity: 0,
                "System.Void",
                new List<NetParameterDescriptor>
                {
                    new NetParameterDescriptor(NetRefKind.None, member.TypeFullName),
                });
        }

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
