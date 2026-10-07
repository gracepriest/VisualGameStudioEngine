using System;
using System.Collections.Generic;

namespace BasicLang
{
    /// <summary>
    /// #222 — the ReadOnly facts of the .NET properties BasicLang types from its own hand-built
    /// member tables rather than from metadata: <c>String</c>, <c>List</c> / <c>Dictionary</c> /
    /// <c>HashSet</c> (<c>SemanticAnalyzer.ResolveCollectionMemberType</c> and the string table
    /// beside it) and arrays. Those receivers are claimed by native handling, so the .NET resolver
    /// never sees them (spec §6.5's claim predicate) and nothing else can say that
    /// <c>s.Length</c> or <c>l.Count</c> has no setter.
    ///
    /// <para>Every entry is a get-only property of the real .NET type, checked against .NET 8's
    /// reflection metadata (no <c>SetMethod</c>): a write to it is vbc's BC30526, which
    /// <c>SemanticAnalyzer.ReadOnlyNetPropertyName</c> reports on every backend. A member NOT
    /// listed is not known ReadOnly and is never refused — <c>List.Capacity</c> has a public
    /// setter, and <c>String.Empty</c> is a FIELD, which vbc refuses under another code.</para>
    ///
    /// <para>The other two hand-built sources answer for themselves:
    /// <see cref="NativeBclMember.IsReadOnly"/> for the P1 types (DateTime, TimeSpan, …) and the
    /// built-in <c>Exception</c>'s own members (<c>SymbolTable</c>, #220) for the .NET exception
    /// classes, which inherit them.</para>
    /// </summary>
    internal static class BclReadOnlyProperties
    {
        private static readonly Dictionary<string, HashSet<string>> ByTypeName =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["String"] = Names("Length"),
                ["List"] = Names("Count"),
                ["Dictionary"] = Names("Count", "Keys", "Values", "Comparer"),
                ["HashSet"] = Names("Count", "Comparer"),
            };

        /// <summary><c>System.Array</c>'s instance properties — every one of them get-only.</summary>
        private static readonly HashSet<string> ArrayProperties =
            Names("Length", "LongLength", "Rank", "IsFixedSize", "IsReadOnly", "IsSynchronized", "SyncRoot");

        private static HashSet<string> Names(params string[] names) =>
            new(names, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// True when <paramref name="memberName"/> is a get-only property of the receiver: an array
        /// when <paramref name="isArray"/>, otherwise the hand-built type <paramref name="typeName"/>.
        /// <paramref name="canonicalName"/> is the member as .NET spells it, for vbc's message.
        /// The caller has already ruled out a BasicLang-declared receiver.
        /// </summary>
        internal static bool TryGet(string typeName, bool isArray, string memberName, out string canonicalName)
        {
            canonicalName = null;
            if (string.IsNullOrEmpty(memberName)) return false;
            var names = isArray
                ? ArrayProperties
                : typeName != null && ByTypeName.TryGetValue(typeName, out var listed) ? listed : null;
            return names != null && names.TryGetValue(memberName, out canonicalName);
        }
    }
}
