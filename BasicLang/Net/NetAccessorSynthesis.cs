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
        /// </summary>
        internal static NetMemberDescriptor SetterFor(NetMemberDescriptor member)
        {
            if (member == null) throw new ArgumentNullException(nameof(member));
            if (member.Kind != NetMemberCategory.Property && member.Kind != NetMemberCategory.Field)
                throw new ArgumentException(
                    $"Only a property or field has a synthesized setter; got {member.Kind} '{member.Name}'.",
                    nameof(member));

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
        /// named <c>set_X</c> with exactly one by-value parameter. The shim generator spells
        /// such a member as a property/field ASSIGNMENT (<c>target.X = value</c>) — C# cannot
        /// call an accessor by its metadata name (CS0571).
        ///
        /// <para>Caveat, stated rather than hidden: metadata from another compiler could
        /// declare an ORDINARY method named <c>set_X</c>; it would take this spelling and the
        /// generated shim would fail to compile — loudly, in csc, never silently calling the
        /// wrong thing.</para>
        /// </summary>
        internal static bool IsSyntheticSetterShape(NetMemberDescriptor member) =>
            member != null
            && member.Kind == NetMemberCategory.Method
            && member.Name.StartsWith(SetterPrefix, StringComparison.Ordinal)
            && member.Name.Length > SetterPrefix.Length
            && member.Arity == 0
            && member.TypeFullName == "System.Void"
            && member.Parameters is { Count: 1 }
            && member.Parameters[0].RefKind == NetRefKind.None;
    }
}
