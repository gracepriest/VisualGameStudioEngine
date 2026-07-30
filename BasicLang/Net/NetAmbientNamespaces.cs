namespace BasicLang.Net
{
    /// <summary>
    /// The one .NET namespace set the C# backend seeds every generated program's candidate
    /// <c>using</c> list with, unconditionally (spec §6.5). Shared with
    /// <see cref="NetTypeResolver"/> so the two cannot drift apart: before this extraction the
    /// list lived only inline in <c>CSharpBackend.cs</c> (the FILE — the class inside it is
    /// <see cref="global::BasicLang.Compiler.CodeGen.CSharp.ImprovedCSharpCodeGenerator"/>, there
    /// is no type literally named <c>CSharpBackend</c>), and nothing kept a resolver's idea of
    /// "ambient" in sync with what the backend actually emits. A namespace the C# backend
    /// auto-imports but the resolver does not know about means a program that compiles on the C#
    /// backend becomes a BL6016 natively — which is exactly what spec §6.3's "valid programs
    /// behave identically on both backends" claim rules out.
    ///
    /// <para><b>This is the CANDIDATE set, not a promise every program's output contains all of
    /// these.</b> <c>ImprovedCSharpCodeGenerator.Generate</c> adds every entry here to a candidate
    /// <c>HashSet&lt;string&gt;</c>; a separate post-generation text scan
    /// (<c>DetectUsedNamespaces</c>) decides which of the candidates the program actually
    /// triggered, and only those are emitted — alphabetically re-sorted, so the ORDER of the
    /// entries below has no effect on emitted output.</para>
    ///
    /// <para><b>Pure extraction — nothing here is conditional.</b> All 17 were added
    /// unconditionally, one <c>HashSet&lt;string&gt;.Add</c> call apiece, with no surrounding
    /// <c>if</c> keyed on backend, project type, or option, at the single call site this was moved
    /// from (<c>CSharpBackend.cs</c>, historically lines 171-187). If a future change makes any
    /// entry conditional, this comment and <c>NetAmbientNamespaceTests</c> both need to be
    /// revisited — a resolver that assumes an unconditional ambient set would then answer wrong
    /// for programs where the condition does not hold.</para>
    /// </summary>
    internal static class NetAmbientNamespaces
    {
        /// <summary>
        /// The 17 namespaces, in the order historically added in <c>CSharpBackend.cs</c>. Order is
        /// preserved here only for a clean diff against that history — see the type remarks for why
        /// it has no effect on emitted C#.
        /// </summary>
        internal static readonly string[] All =
        {
            "System",
            "System.Collections.Generic",
            "System.Threading.Tasks",
            "System.Collections",
            "System.Runtime.InteropServices",
            "System.Text",
            "System.IO",
            "System.Linq",
            "System.Net",
            "System.Net.Http",
            "System.Net.Sockets",
            "System.Text.Json",
            "System.Text.Json.Nodes",
            "System.Text.RegularExpressions",
            "System.Security.Cryptography",
            "System.Diagnostics",
            "System.Threading",
        };
    }
}
