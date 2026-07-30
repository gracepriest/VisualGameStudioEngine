using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace BasicLang.Net
{
    /// <summary>
    /// Produces the export name that appears on BOTH sides of the P2a-1 ABI (spec §7.3): the
    /// shim's <c>[UnmanagedCallersOnly(EntryPoint = "...")]</c> name (Task 14) and the matching
    /// <c>BlnetProxyTable</c> function-pointer slot (Task 12). §12.4 requires the two sets to
    /// match exactly, so a collision here is not cosmetic — two distinct .NET members would share
    /// one export slot, and a call would silently reach the wrong member.
    ///
    /// <para><b>A PURE FUNCTION. No statics, no caching, no ambient state.</b> Task 15's content-hash
    /// cache key is derived from the mangled set, so any instability here — two runs of the same
    /// process producing different names for the same member, or the same input producing a
    /// different name depending on what else was mangled first — turns into a false cache hit: a
    /// stale shim shipping silently. That failure mode is the reason this class must never grow a
    /// field.</para>
    ///
    /// <para><b>Mangles from (fully-qualified declaring type, kind, name, static-ness, generic
    /// arity, [ref-kind + type]… per parameter) — not just (declaring type, member name, parameter
    /// types).</b> The plan's original Step 3 named only the latter three, and that is NOT
    /// collision-free: two axes of a CLR signature are invisible in them, and both occur in the
    /// real framework surface (measured via <see cref="NetTypeResolver.GetMembers"/> during Task
    /// 4's review):</para>
    /// <list type="bullet">
    /// <item><description><b>Generic method arity.</b> <c>IMethodSymbol.MetadataName</c> carries no
    /// arity suffix, so <c>Task.FromException(Exception)</c> and
    /// <c>Task.FromException&lt;T&gt;(Exception)</c> have IDENTICAL name and parameter types.
    /// Measured across the public framework surface: 37 public types contain such a pair, including
    /// every <c>Expression.Lambda</c>/<c>Lambda&lt;TDelegate&gt;</c> pair, <c>ValueTask.FromCanceled</c>,
    /// both <c>IQueryProvider.CreateQuery</c>/<c>Execute</c> overloads, and two <c>Marshal</c>
    /// members.</description></item>
    /// <item><description><b>Parameter ref-kind.</b> <c>ref</c>/<c>out</c>/<c>in</c> are not part of
    /// a parameter's TYPE. <c>EventSource.Write(String, EventSourceOptions, T)</c> and
    /// <c>Write(String, ref EventSourceOptions, ref T)</c> differ only in
    /// <see cref="NetParameterDescriptor.RefKind"/>.</description></item>
    /// </list>
    /// <para><see cref="NetMemberDescriptor.Arity"/> and <see cref="NetParameterDescriptor.RefKind"/>
    /// exist specifically so both axes can be carried through to here.</para>
    ///
    /// <para><b>Kind and static-ness are ALSO folded in, beyond what the corrected formula
    /// strictly required.</b> Under ordinary C#-compiled metadata this is provably redundant: C#
    /// forbids two members of the same declaring type sharing a (name, arity, parameter-list)
    /// triple unless they are overloaded methods (CS0102/CS0111), so kind and static-ness can never
    /// vary independently of that triple for a single declaring type. But <see cref="NetTypeResolver"/>
    /// reads arbitrary metadata — spec D1 is "any assembly" — and the CLR itself (unlike C#) does
    /// not forbid a field and a method from sharing a name in IL emitted by another compiler or a
    /// tool. Carrying kind and static-ness costs nothing and closes that theoretical gap, so both
    /// are part of the identity this class hashes over (see <see cref="CanonicalIdentity"/>).</para>
    ///
    /// <para><b>Six design decisions, each forced by a concrete failure mode:</b></para>
    /// <list type="number">
    /// <item><description><b>Legal C identifier / escaping.</b> A NAIVE "replace illegal characters
    /// with <c>_</c>" is itself a collision source: <c>MyLib.Customer</c> and a hypothetical
    /// <c>MyLib_Customer</c> both sanitize to <c>MyLib_Customer</c>. This class does NOT rely on the
    /// sanitized text for correctness — <see cref="Sanitize"/> is one-way and lossy, used only to
    /// build a human-readable STEM. The actual uniqueness guarantee is decision 3's hash, computed
    /// from the UNSANITIZED, unambiguously-delimited identity, so the two examples above hash
    /// differently (one contains a literal <c>.</c>, the other a literal <c>_</c>) even though their
    /// stems collide.</description></item>
    /// <item><description><b>Length.</b> Capped at <see cref="MaxIdentifierLength"/> (300)
    /// characters total, chosen as a generous, documented, arbitrary ceiling — no C++ toolchain in
    /// this project's supported set (MSVC, GCC, Clang) imposes a hard symbol-length limit that low,
    /// so this exists only to keep pathological generic signatures (deeply nested generics, long
    /// tuples) from producing multi-kilobyte identifiers. The hash suffix (decision 3) is NEVER
    /// truncated — only the readable stem is, and only when it would push the total over the cap —
    /// so truncation can never reintroduce a collision the hash would otherwise have
    /// prevented.</description></item>
    /// <item><description><b>Hashing.</b> SHA-256 over the UTF-8 bytes of the full canonical
    /// identity, truncated to its first 8 bytes (64 bits) and rendered as 16 lowercase hex digits —
    /// ALWAYS appended, not only when the stem is truncated (see decision 1). Deliberately NOT
    /// <c>string.GetHashCode()</c>, which is randomized per process in .NET Core and would make two
    /// runs of the same build produce two different export names for the same member — exactly the
    /// instability Task 15's cache key cannot tolerate. SHA-256 is a fixed, standardized algorithm
    /// with no process-local seed, so it is stable across processes, runs, and machines. 64 bits is
    /// not mathematically injective over an unbounded input space, but the birthday-bound collision
    /// probability is negligible at any realistic surface size (~n²/2^65; for n = 10,000,000 members
    /// that is ~2.7×10⁻⁸) — and every member the corpus sweep in
    /// <c>NetNameManglerTests</c> actually hashed additionally differs in its STEM, so decision 3 is
    /// this class's defense in depth, not its only line of defense.</description></item>
    /// <item><description><b>Readability vs. safety.</b> These names appear in generated C++ and in
    /// ILC diagnostics Task 16 maps back to <c>.bas</c> lines, so a fully-hashed name would be
    /// undebuggable. The chosen balance: keep the declaring type, member name, arity marker and
    /// parameter list in the stem, sanitized for legality but not for uniqueness, and let the hash
    /// suffix (decision 3) carry the actual correctness guarantee. A human reading
    /// <c>bl_net_System_Text_RegularExpressions_Regex_Match__string_&lt;16 hex&gt;</c> can identify
    /// the member at a glance; the compiler never needs to parse the name back apart to know it is
    /// unique.</description></item>
    /// <item><description><b>Constructors.</b> <see cref="NetMemberDescriptor.Name"/> is the CLR
    /// metadata name <c>".ctor"</c> for every constructor (<see cref="NetMemberDescriptor.Kind"/> is
    /// what actually distinguishes one). <c>.</c> is not a legal identifier character, so the
    /// readable stem spells it <c>ctor</c> — chosen over dropping the segment entirely because a
    /// dropped segment would make a constructor's stem look exactly like a zero-arg member named
    /// after its declaring type, which is confusing in a diagnostic.</description></item>
    /// <item><description><b>Spec §4.2's <c>Regex_Match__string</c> shape.</b> The general shape —
    /// dotted type, then member, then <c>__</c>, then parameter types — is kept, but the worked
    /// example is NOT treated as a binding format where it conflicts with collision-freedom, and it
    /// does, in two ways: it spells the declaring type as the bare <c>Regex</c> rather than
    /// <c>System.Text.RegularExpressions.Regex</c> (§7.3 explicitly requires the FULLY-QUALIFIED
    /// declaring type — its own worked example: <c>MyLib.Customer</c> vs <c>OtherLib.Customer</c>),
    /// and it carries neither an arity marker nor a hash suffix (the exact omissions this class's
    /// header explains above). This class's output for that member is instead
    /// <c>bl_net_System_Text_RegularExpressions_Regex_Match__string_&lt;16 hex&gt;</c> — same
    /// recognizable shape, fully qualified, with the hash suffix that shape alone cannot
    /// guarantee.</description></item>
    /// </list>
    /// </summary>
    internal static class NetNameMangler
    {
        /// <summary>Every mangled name starts with this, so the RESULT always starts with a letter
        /// regardless of what <see cref="Sanitize"/> does to the rest — <c>Sanitize</c>'s output
        /// alphabet is a subset of <c>[A-Za-z0-9_]</c> and can begin with a digit if the source name
        /// did (foreign/obfuscated metadata), which alone would not be a legal C identifier.</summary>
        private const string Prefix = "bl_net_";

        /// <summary>See this class's header, design decision 2.</summary>
        internal const int MaxIdentifierLength = 300;

        /// <summary>16 lowercase hex digits = 8 bytes = 64 bits. See design decision 3.</summary>
        private const int HashHexLength = 16;

        /// <summary>
        /// Non-printable field separators for <see cref="CanonicalIdentity"/>, spelled as character
        /// arithmetic rather than a <c>'\uXXXX'</c> literal so the three stay exactly the raw
        /// control characters they need to be — vanishingly unlikely to occur inside any real .NET
        /// type, member or parameter spelling — without depending on an escape sequence surviving
        /// byte-for-byte through every tool that may touch this source file.
        /// </summary>
        private static readonly char FieldSeparator = (char)1;

        private static readonly char ParameterMarker = (char)2;

        private static readonly char RefKindSeparator = (char)3;

        /// <summary>
        /// The mangled export name for <paramref name="member"/>. Same input (by value, not by
        /// reference — see <see cref="CanonicalIdentity"/>) always produces the same output, in any
        /// process, on any machine, regardless of what else has been mangled before or since.
        /// </summary>
        public static string Mangle(NetMemberDescriptor member)
        {
            if (member == null)
                throw new ArgumentNullException(nameof(member));

            var hash = StableHashHex(CanonicalIdentity(member));

            var parameters = member.Parameters ?? Array.Empty<NetParameterDescriptor>();
            var stem = new StringBuilder(Prefix)
                .Append(Sanitize(member.DeclaringTypeFullName))
                .Append('_')
                .Append(Sanitize(MemberSpelling(member)))
                .Append(member.Arity > 0
                    ? "_arity" + member.Arity.ToString(CultureInfo.InvariantCulture)
                    : string.Empty)
                .Append("__")
                .Append(string.Join("_", parameters.Select(p => Sanitize(p.ToString()))))
                .ToString();

            // The hash is NEVER truncated (decision 2) — only the readable stem is, and only when
            // the two together would exceed the cap. Truncating the stem can only make two
            // DIFFERENT identities look more alike; it can never make the hash suffix collide,
            // because the hash is computed from the untruncated identity below.
            var budget = MaxIdentifierLength - 1 - HashHexLength;   // room for the "_" + hash
            if (stem.Length > budget)
                stem = stem.Substring(0, Math.Max(Prefix.Length, budget));

            return stem + "_" + hash;
        }

        private static string MemberSpelling(NetMemberDescriptor member) =>
            member.Kind == NetMemberCategory.Constructor ? "ctor" : member.Name;

        /// <summary>
        /// Every axis of the member's identity this class promises collision-freedom over, joined
        /// with separators that cannot occur in a .NET type, member or parameter spelling — so, for
        /// example, a declaring type ending in the same character used as a field separator can
        /// never be confused with the boundary between two fields.
        ///
        /// <para><b>Built from VALUES copied out of <paramref name="member"/>, not from the object
        /// itself</b> — <see cref="NetMemberDescriptor"/> is deliberately not a record (its own
        /// remarks explain why: reference equality over its parameter list), so two descriptors
        /// with identical field values but different identity must still mangle identically. This
        /// method only ever reads scalar fields and one list of scalar-field parameter records, so
        /// that holds without any special-casing here.</para>
        /// </summary>
        private static string CanonicalIdentity(NetMemberDescriptor member)
        {
            var identity = new StringBuilder()
                .Append(member.DeclaringTypeFullName).Append(FieldSeparator)
                .Append(member.Kind).Append(FieldSeparator)
                .Append(member.Name).Append(FieldSeparator)
                .Append(member.IsStatic).Append(FieldSeparator)
                .Append(member.Arity.ToString(CultureInfo.InvariantCulture));

            foreach (var p in member.Parameters ?? Array.Empty<NetParameterDescriptor>())
            {
                identity.Append(ParameterMarker).Append(p.RefKind)
                    .Append(RefKindSeparator).Append(p.TypeFullName);
            }

            return identity.ToString();
        }

        /// <summary>
        /// SHA-256 over the UTF-8 bytes of <paramref name="identity"/>, truncated to its first 8
        /// bytes and rendered as 16 lowercase hex digits. See this class's header, design decision
        /// 3, for why SHA-256 rather than <c>string.GetHashCode()</c> and why 64 bits is enough.
        /// </summary>
        private static string StableHashHex(string identity)
        {
            var digest = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
            var hex = new StringBuilder(HashHexLength);
            for (var i = 0; i < HashHexLength / 2; i++)
                hex.Append(digest[i].ToString("x2", CultureInfo.InvariantCulture));
            return hex.ToString();
        }

        /// <summary>
        /// Replaces every character outside <c>[A-Za-z0-9_]</c> with <c>_</c>, one character at a
        /// time — a lossy, NON-reversible mapping used only for the readable stem (design decision
        /// 1). Deliberately not collision-safe on its own (<c>"A.B"</c> and a hypothetical literal
        /// <c>"A_B"</c> both sanitize to <c>"A_B"</c>) — <see cref="Mangle"/> never relies on this
        /// method's output for uniqueness, only on <see cref="StableHashHex"/> over the unsanitized
        /// <see cref="CanonicalIdentity"/>.
        /// </summary>
        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s))
                return string.Empty;

            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
                sb.Append(IsIdentifierChar(c) ? c : '_');
            return sb.ToString();
        }

        private static bool IsIdentifierChar(char c) =>
            (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_';
    }
}
