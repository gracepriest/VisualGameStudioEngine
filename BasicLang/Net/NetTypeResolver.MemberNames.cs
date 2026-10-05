using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace BasicLang.Net
{
    internal sealed partial class NetTypeResolver
    {
        /// <summary>
        /// One resolver per distinct reference closure, shared across compilations in this process; a rebuilt
        /// referenced assembly (a changed size or write time) is never served stale.
        ///
        /// <para>⛔ Task 7d review (perf): a WinForms closure is ~230 assemblies; building its resolver, and the
        /// extension-method scan behind <see cref="DeclaresNameableMember"/>, cost ~390 ms on EVERY compile
        /// (each <c>CompilerOptions</c> made its own). The class remarks already say "build ONE per reference
        /// closure and keep it"; this is the keeping. Used for the WinForms closure only — the other routes keep
        /// their per-options instance.</para>
        ///
        /// <para>⚠ Keyed on the PATH SET, the stamps stored beside it: a changed stamp REPLACES the entry (the old
        /// resolver dropped), so one closure never accumulates a resolver per rebuild of a referenced assembly — the
        /// cache holds one entry per distinct set of paths (Task 7d review).</para>
        /// </summary>
        internal static NetTypeResolver CreateShared(IReadOnlyList<string> assemblyPaths)
        {
            var key = string.Join("|", assemblyPaths);
            var stamps = string.Join("|", assemblyPaths.Select(StampOf));
            var entry = SharedResolvers.AddOrUpdate(key,
                _ => new SharedEntry(stamps, new Lazy<NetTypeResolver>(() => Create(assemblyPaths))),
                (_, existing) => existing.Stamps == stamps
                    ? existing
                    : new SharedEntry(stamps, new Lazy<NetTypeResolver>(() => Create(assemblyPaths))));
            return entry.Resolver.Value;
        }

        private static string StampOf(string path)
        {
            try
            {
                var info = new System.IO.FileInfo(path);
                return info.Exists ? $"{info.Length}*{info.LastWriteTimeUtc.Ticks}" : "-";
            }
            catch (Exception) { return "?"; }
        }

        private sealed record SharedEntry(string Stamps, Lazy<NetTypeResolver> Resolver);

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SharedEntry> SharedResolvers = new();

        /// <summary>Entries in the shared cache — for the test that a changed stamp REPLACES rather than adds.</summary>
        internal static int SharedResolverCountForTest => SharedResolvers.Count;

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
            for (var type = symbol; type != null; type = type.BaseType)
                types.Add(type);
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
