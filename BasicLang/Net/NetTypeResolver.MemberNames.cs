using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace BasicLang.Net
{
    internal sealed partial class NetTypeResolver
    {
        /// <summary>
        /// Whether a member named <paramref name="name"/> (VB's case-insensitive match) is NAMEABLE on
        /// <paramref name="fullName"/>: declared by the type or a base with an accessibility the caller has —
        /// public, or protected too when <paramref name="includeProtected"/> (the caller is a derived class
        /// naming its own inherited member) — of ANY kind, events and nested types included; or an extension
        /// method of that name exists anywhere in the closure. TRUE when the type itself is unknown: unknown
        /// is never evidence that a member is missing.
        ///
        /// <para>⛔ Portable-controls Task 7d. <see cref="GetMembers"/> is the CALL surface — public
        /// methods/properties/fields only, events and protected members deliberately excluded (it feeds
        /// overload resolution and the shim) — so "not in GetMembers" is not "does not exist":
        /// <c>btn.Click</c> (an event) and <c>Me.OnLoad(e)</c> (protected) are absent from it. A
        /// misspelled-member ERROR has to ask this question instead, or every WinForms handler wiring is a
        /// false error.</para>
        /// </summary>
        /// <param name="includeExtensions">False for a BARE name (VB never reaches an extension method without a
        /// receiver), true for <c>x.Name</c>.</param>
        internal bool DeclaresNameableMember(string fullName, string name, bool includeProtected, bool includeExtensions = true)
        {
            if (string.IsNullOrEmpty(name)) return true;
            var symbol = Lookup(fullName).Symbol;
            if (symbol == null) return true;

            var types = new List<INamedTypeSymbol>();
            for (var type = symbol; type != null; type = type.BaseType) types.Add(type);
            types.AddRange(symbol.AllInterfaces);

            foreach (var type in types)
            {
                foreach (var member in type.GetMembers())
                {
                    if (!string.Equals(member.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                    if (member.IsImplicitlyDeclared) continue;                    if (IsNameable(member.DeclaredAccessibility, includeProtected)) return true;
                }
                foreach (var nested in type.GetTypeMembers())
                {
                    if (string.Equals(nested.Name, name, StringComparison.OrdinalIgnoreCase)
                        && IsNameable(nested.DeclaredAccessibility, includeProtected))
                        return true;
                }
            }

            return includeExtensions && ExtensionMethodNames.Value.Contains(name);
        }

        private static bool IsNameable(Accessibility accessibility, bool includeProtected) => accessibility switch
        {
            Accessibility.Public => true,
            Accessibility.Protected or Accessibility.ProtectedOrInternal => includeProtected,
            _ => false
        };

        /// <summary>
        /// Every extension method name in the closure — a name the receiver's own type need not declare (LINQ's
        /// <c>OfType</c> on a <c>Control.ControlCollection</c>). Scanned once per resolver, skipping every
        /// assembly and type Roslyn already knows holds none.
        /// </summary>
        private Lazy<HashSet<string>> _extensionMethodNames;

        private Lazy<HashSet<string>> ExtensionMethodNames =>
            _extensionMethodNames ??= new Lazy<HashSet<string>>(() =>
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var assembly in _assemblies)
                {
                    if (!assembly.MightContainExtensionMethods) continue;
                    var stack = new Stack<INamespaceSymbol>();
                    stack.Push(assembly.GlobalNamespace);
                    while (stack.Count > 0)
                    {
                        var ns = stack.Pop();
                        foreach (var child in ns.GetNamespaceMembers()) stack.Push(child);
                        foreach (var type in ns.GetTypeMembers())
                        {
                            if (!type.MightContainExtensionMethods || type.DeclaredAccessibility != Accessibility.Public) continue;
                            foreach (var member in type.GetMembers())
                                if (member is IMethodSymbol { IsExtensionMethod: true, DeclaredAccessibility: Accessibility.Public } method)
                                    names.Add(method.Name);
                        }
                    }
                }
                return names;
            });
    }
}
