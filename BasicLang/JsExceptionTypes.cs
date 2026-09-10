using System;
using System.Collections.Generic;
using System.Linq;
using BasicLang.Compiler.IR;

namespace BasicLang.Compiler.CodeGen.JavaScript
{
    /// <summary>
    /// The .NET exception hierarchy the JavaScript backend PROVIDES at runtime, as real JS
    /// classes rooted at <c>class Exception extends Error</c>.
    ///
    /// <para><b>Why a hierarchy and not erasure.</b> The original design erased every
    /// exception name to <c>Error</c> — and then never implemented the erasure, so
    /// <c>Throw New ArgumentException("boom")</c> emitted <c>new ArgumentException("boom")</c>
    /// verbatim and threw a ReferenceError instead. Even done properly, erasure cannot support a
    /// typed <c>Catch</c>: with every exception the same JS class there is nothing for
    /// <c>instanceof</c> to test, so a clause either catches everything (the measured over-catch)
    /// or is refused. Real classes make <c>instanceof</c> a faithful type test, let a user
    /// <c>Class MyError : Inherits Exception</c> slot into the chain, and give <c>e.Message</c>
    /// something to read.</para>
    ///
    /// <para><b>Emitted on demand.</b> Only the transitive closure of the names a program
    /// mentions is emitted (<see cref="CollectRequired"/>), so hello world carries none of it
    /// and a program that throws <c>ArgumentNullException</c> gets exactly that class and its
    /// three ancestors. The table is in base-before-derived order and is emitted in that order,
    /// because JS class declarations are not hoisted.</para>
    ///
    /// <para>Shared by the generator (emission, canonical spelling) and
    /// <c>JsCapabilityChecker</c> (the allow-list, BL7011 collisions, BL7012 unknown names) —
    /// one table, so "may be named" and "exists at runtime" cannot drift apart.</para>
    /// </summary>
    public static class JsExceptionTypes
    {
        /// <summary>
        /// Canonical .NET name → base name, base-before-derived. The root's base is null.
        /// <c>ArithmeticException</c> is a real, catchable class here — unlike the C++ backend's
        /// 12-name set it costs nothing to provide, and dropping it would silently make
        /// <c>OverflowException</c> not an <c>ArithmeticException</c>.
        /// </summary>
        private static readonly (string Name, string Base)[] Hierarchy =
        {
            ("Exception", null),
            ("SystemException", "Exception"),
            ("ApplicationException", "Exception"),
            ("ArithmeticException", "SystemException"),
            ("ArgumentException", "SystemException"),
            ("ArgumentNullException", "ArgumentException"),
            ("ArgumentOutOfRangeException", "ArgumentException"),
            ("DivideByZeroException", "ArithmeticException"),
            ("OverflowException", "ArithmeticException"),
            ("FormatException", "SystemException"),
            ("IndexOutOfRangeException", "SystemException"),
            ("InvalidCastException", "SystemException"),
            ("InvalidOperationException", "SystemException"),
            ("KeyNotFoundException", "SystemException"),
            ("NotImplementedException", "SystemException"),
            ("NotSupportedException", "SystemException"),
            ("NullReferenceException", "SystemException"),
            ("IOException", "SystemException"),
            ("FileNotFoundException", "IOException"),
            ("TimeoutException", "SystemException"),
        };

        private static readonly Dictionary<string, string> BaseByName =
            Hierarchy.ToDictionary(e => e.Name, e => e.Base, StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, string> CanonicalByName =
            Hierarchy.ToDictionary(e => e.Name, e => e.Name, StringComparer.OrdinalIgnoreCase);

        /// <summary>Every provided name, canonical casing, base-before-derived.</summary>
        public static IReadOnlyList<string> ProvidedNames { get; } = Hierarchy.Select(e => e.Name).ToArray();

        public static bool IsProvided(string name) => name != null && CanonicalByName.ContainsKey(name);

        /// <summary>The canonical casing of a provided name, or null. BasicLang is case-insensitive; JavaScript is not.</summary>
        public static string Canonical(string name) =>
            name != null && CanonicalByName.TryGetValue(name, out var canonical) ? canonical : null;

        /// <summary>The provided base of a provided name; null for the root.</summary>
        public static string BaseOf(string name) =>
            name != null && BaseByName.TryGetValue(name, out var baseName) ? baseName : null;

        /// <summary>
        /// Whether a name is SPELLED like an exception type. Used only to choose the diagnostic
        /// — an unknown <c>…Exception</c> gets BL7012 (how to declare one) rather than the
        /// generic BL7007.
        /// </summary>
        public static bool LooksLikeException(string name) =>
            name != null && name.EndsWith("Exception", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The provided exception classes a module needs, base-before-derived: the transitive
        /// closure of every provided name it mentions — a <c>New</c>, a <c>Catch</c> type, or a
        /// user class's base.
        ///
        /// <para>Any <c>Catch</c> at all requires the root, because the catch-all arm binds its
        /// variable through <c>Exception.Wrap</c> so a native JS error still has a
        /// <c>Message</c>.</para>
        /// </summary>
        public static List<string> CollectRequired(IRModule module)
        {
            var mentioned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Mention(string name)
            {
                var canonical = Canonical(name);
                if (canonical != null) mentioned.Add(canonical);
            }

            foreach (var irClass in module?.Classes?.Values ?? Enumerable.Empty<IRClass>())
                Mention(irClass.BaseClass);

            foreach (var function in module?.Functions ?? Enumerable.Empty<IRFunction>())
            {
                foreach (var block in function.Blocks ?? Enumerable.Empty<BasicBlock>())
                {
                    foreach (var instruction in block.Instructions ?? Enumerable.Empty<IRInstruction>())
                    {
                        switch (instruction)
                        {
                            case IRNewObject n:
                                Mention(n.ClassName);
                                break;
                            case IRThrow t when t.Exception is IRNewObject thrown:
                                Mention(thrown.ClassName);
                                break;
                            case IRTryCatch tc:
                                if (tc.CatchClauses != null && tc.CatchClauses.Count > 0) Mention("Exception");
                                foreach (var clause in tc.CatchClauses ?? Enumerable.Empty<IRCatchClause>())
                                    Mention(clause.ExceptionType?.Name);
                                break;
                        }
                    }
                }
            }

            // Close over ancestors, then emit in table order so every base precedes its derived.
            foreach (var name in mentioned.ToList())
            {
                for (var b = BaseOf(name); b != null; b = BaseOf(b))
                    mentioned.Add(b);
            }

            return ProvidedNames.Where(mentioned.Contains).ToList();
        }
    }
}
