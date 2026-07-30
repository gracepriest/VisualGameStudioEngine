using System;
using System.Collections.Generic;

namespace BasicLang.Net
{
    /// <summary>
    /// The .NET surface of one project: every member that must get a proxy slot and a shim
    /// export (spec §7.1 + §7.2), plus the <c>&lt;NetProxy&gt;</c> type names the project
    /// declared.
    ///
    /// <para><b>Two sources, one model.</b> §7.1 infers members from BasicLang call sites;
    /// §7.2 lets a hand-written-C++ project DECLARE types, whose full public surface is
    /// expanded into <see cref="Members"/>. <see cref="DeclaredTypeNames"/> is kept alongside
    /// the expansion rather than being thrown away after it, because the two later consumers
    /// need the distinction: BL6022 must name the declaration the user wrote, and BL6026's
    /// "member omitted" warning is only reachable for a declared type (an inferred member is
    /// by definition one the program calls, so omitting it is BL6019, an error).</para>
    ///
    /// <para><b>Every existing project produces <see cref="Empty"/>.</b> That is the whole of
    /// P2a-1's inertness claim on the native side: <see cref="IsNonEmpty"/> is what
    /// <c>NetProxyEmitter</c> keys on, and on an empty surface it writes NOTHING —
    /// no <c>obj/gen</c> entry, no translation unit, no include path, no shim. In P2a-1
    /// nothing populates a surface at all (the phase-3 collector lands later), so every build
    /// in the repo takes the empty path.</para>
    ///
    /// <para><b>Why "empty" also counts declarations.</b> <see cref="IsNonEmpty"/> is
    /// <c>Members.Count &gt; 0 || DeclaredTypeNames.Count &gt; 0</c>, not just the member
    /// count. A <c>&lt;NetProxy&gt;</c> whose type expanded to zero admitted members is a
    /// user statement of intent that has gone wrong; treating that project as "no .NET at
    /// all" would silently drop the shim, skip BL6025 on a library output, and leave the user
    /// with a build that looks clean and does nothing. It costs nothing to be honest here,
    /// because both counts are zero for every project that never mentions .NET.</para>
    ///
    /// <para><b>Record equality is REFERENCE equality over both members</b>, for the reason
    /// <see cref="NetMemberDescriptor"/> and <c>NetReferenceClosure</c> both document: the
    /// compiler-synthesized <c>==</c> over an <see cref="IReadOnlyList{T}"/> compares the list
    /// objects, not their contents. Do not write an assertion that assumes otherwise.</para>
    /// </summary>
    /// <param name="Members">
    /// The members needing a proxy slot / shim export. Order is preserved so generated output
    /// is deterministic; duplicates (the same member reached from two call sites) are
    /// tolerated — <c>NetProxyEmitter</c> collapses them by mangled name, which is exactly the
    /// key the shim generator collapses on too, so §12.4's "slots ≡ exports" survives.
    /// </param>
    /// <param name="DeclaredTypeNames">
    /// The <c>&lt;NetProxy Include="..."/&gt;</c> values verbatim, as the user spelled them.
    /// </param>
    internal sealed record NetSurface(
        IReadOnlyList<NetMemberDescriptor> Members,
        IReadOnlyList<string> DeclaredTypeNames)
    {
        /// <summary>
        /// No members, no declarations — the surface of every project in the repo today, and
        /// what phase 3 returns until the collector exists.
        /// </summary>
        public static NetSurface Empty { get; } =
            new(Array.Empty<NetMemberDescriptor>(), Array.Empty<string>());

        /// <summary>
        /// Whether this project uses .NET at all. THE gate: <c>NetProxyEmitter</c> emits its
        /// artifact set only when this is true, and <c>CppProjectBuilder</c> merges those
        /// artifacts into <c>obj/gen</c>, <c>request.SourceFiles</c> and the include path only
        /// when it is true (spec §9.5).
        /// </summary>
        public bool IsNonEmpty => Members.Count > 0 || DeclaredTypeNames.Count > 0;
    }
}
