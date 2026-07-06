using System.Text;

namespace BasicLang.Compiler.ProjectSystem
{
    /// <summary>
    /// Escaping for user-controlled values embedded into generated MSBuild
    /// project files (.csproj) — project/assembly names, compile-item file
    /// names, reference names, hint paths, package ids.
    ///
    /// Two layers protect the value, decoded in reverse order by the consumer:
    /// XML entities (&amp;, &lt;, &gt;, &quot;) keep the .csproj parseable
    /// (a raw '&amp;' in a name fails the load with MSB4025), and MSBuild %XX
    /// escapes keep evaluation semantics literal — ';' is the item-list
    /// separator (a project named ";k;lk;lkl;k;l" split obj\Debug\&lt;name&gt;.dll
    /// into multiple items and failed Csc's single-item OutputAssembly with
    /// MSB4094), '$'/'@' introduce expressions, '%' introduces escapes, '*'/'?'
    /// glob in item Includes. The XML parser decodes entities first, then
    /// MSBuild unescapes %XX, so build tasks receive the original string and
    /// output files keep the user's literal name on disk.
    ///
    /// Both csproj generators — the CLI (Program.cs build command) and the IDE
    /// (BuildService.GenerateCsprojContent) — must route every user string
    /// through this method; keeping the rule here stops the two from drifting.
    /// </summary>
    public static class MSBuildText
    {
        /// <summary>
        /// Escape a value for embedding in .csproj element text or attribute
        /// values. Single pass: each source character is mapped once, so
        /// escape sequences never get re-escaped and input that already looks
        /// escaped (e.g. a literal "%3B" in a name) round-trips literally.
        /// </summary>
        public static string EscapeValue(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var sb = new StringBuilder(value.Length + 8);
            foreach (var c in value)
            {
                sb.Append(Escape(c) ?? c.ToString());
            }

            return sb.ToString();
        }

        /// <summary>
        /// The distinct XML/MSBuild-special characters in <paramref name="value"/>,
        /// in first-appearance order (empty when the value embeds verbatim).
        /// Powers UI hints like the New Project dialog's warn-but-allow name
        /// check; sharing the classifier with <see cref="EscapeValue"/> keeps
        /// the warning and the actual escaping in exact agreement.
        /// </summary>
        public static string FindSpecialCharacters(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var sb = new StringBuilder();
            foreach (var c in value)
            {
                if (Escape(c) != null && sb.ToString().IndexOf(c) < 0)
                    sb.Append(c);
            }

            return sb.ToString();
        }

        /// <summary>Per-character escape, or null for characters that embed verbatim.</summary>
        private static string Escape(char c) => c switch
        {
            // MSBuild special characters → %XX (decoded by MSBuild after XML load)
            '%' => "%25",
            ';' => "%3B",
            '$' => "%24",
            '@' => "%40",
            '\'' => "%27",
            '(' => "%28",
            ')' => "%29",
            '*' => "%2A",
            '?' => "%3F",

            // XML special characters → entities (decoded by the XML parser)
            '&' => "&amp;",
            '<' => "&lt;",
            '>' => "&gt;",
            '"' => "&quot;",

            _ => null
        };
    }
}
