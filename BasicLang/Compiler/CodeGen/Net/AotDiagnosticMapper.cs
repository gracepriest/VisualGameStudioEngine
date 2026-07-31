using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BasicLang.Compiler.CodeGen.Net
{
    /// <summary>
    /// Which surface source a generated wrapper came from (spec §7.1 vs §7.2). The two need
    /// different wording in a BL6020: a user whose surface came from a <c>&lt;NetProxy&gt;</c>
    /// declaration wrote no BasicLang call site, and telling them to look at one would send them
    /// hunting for something that does not exist.
    /// </summary>
    internal enum NetWrapperOriginKind
    {
        /// <summary>§7.1 — inferred from a BasicLang call site.</summary>
        BasicLangCallSite,

        /// <summary>§7.2 — declared by a <c>&lt;NetProxy Include="…"/&gt;</c> item in the <c>.blproj</c>.</summary>
        NetProxyDeclaration,
    }

    /// <summary>
    /// Where one generated wrapper came from. <see cref="Line"/>/<see cref="Column"/> are 1-based
    /// and may be 0 when the producer has no finer location than the file (a
    /// <c>&lt;NetProxy&gt;</c> item read without recording its XML line, for instance).
    /// </summary>
    /// <param name="Description">
    /// What to name in the message: the BasicLang member spelling for a call site, the declared
    /// type name for a <c>&lt;NetProxy&gt;</c>.
    /// </param>
    internal sealed record NetWrapperOrigin(
        NetWrapperOriginKind Kind, string File, int Line, int Column, string Description)
    {
        internal static NetWrapperOrigin BasCallSite(string file, int line, int column, string description) =>
            new(NetWrapperOriginKind.BasicLangCallSite, file, line, column, description);

        internal static NetWrapperOrigin NetProxyDeclaration(string projectFile, string declaredTypeName, int line = 0) =>
            new(NetWrapperOriginKind.NetProxyDeclaration, projectFile, line, 0, declaredTypeName);

        /// <summary>
        /// <c>file(line,col)</c>, degrading to <c>file(line)</c> and then to <c>file</c> as the
        /// producer's precision runs out. Matches §11.3's worked example, which ends in
        /// <c>MyGame.bas(42,17)</c>.
        /// </summary>
        internal string FormatLocation()
        {
            if (Line <= 0) return File;
            var line = Line.ToString(CultureInfo.InvariantCulture);
            if (Column <= 0) return File + "(" + line + ")";
            return File + "(" + line + "," + Column.ToString(CultureInfo.InvariantCulture) + ")";
        }
    }

    /// <summary>
    /// Mangled wrapper name → originating BasicLang location (spec §11.3). ILC reports against the
    /// GENERATED C#, so without this table every tier-1 diagnostic would point at a file under
    /// <c>obj/gen</c> that the user never wrote.
    ///
    /// <para><b>P2a-1 never populates this.</b> The surface collector that would build it is
    /// P2a-2's (§17), and with no collector every surface is <c>NetSurface.Empty</c>, so phase 5
    /// never runs and nothing calls the mapper. The type exists now so that the mapper it feeds
    /// can be written and tested against real ILC text ahead of the flip.</para>
    /// </summary>
    internal sealed class NetProvenanceMap
    {
        private readonly Dictionary<string, NetWrapperOrigin> _byWrapperName;

        internal NetProvenanceMap(IEnumerable<KeyValuePair<string, NetWrapperOrigin>> entries)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            _byWrapperName = new Dictionary<string, NetWrapperOrigin>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                if (string.IsNullOrEmpty(entry.Key) || entry.Value is null) continue;
                // Last write wins: the same member reached from two call sites mangles to ONE
                // name (NetShimGenerator de-duplicates on exactly this key), so a collision here
                // is expected and is not an error.
                _byWrapperName[entry.Key] = entry.Value;
            }
        }

        internal static NetProvenanceMap Empty { get; } =
            new(Array.Empty<KeyValuePair<string, NetWrapperOrigin>>());

        internal int Count => _byWrapperName.Count;

        internal bool TryGet(string wrapperName, out NetWrapperOrigin origin)
        {
            if (wrapperName is null)
            {
                origin = null!;
                return false;
            }
            return _byWrapperName.TryGetValue(wrapperName, out origin!);
        }
    }

    /// <summary>Spec §11.3's three attribution tiers, plus the honest fourth.</summary>
    internal enum AotAttribution
    {
        /// <summary>Tier 1 — the origin member IS a generated wrapper; resolved to a BasicLang location.</summary>
        WrapperCallSite = 1,

        /// <summary>Tier 2 — the origin member lives inside a referenced assembly.</summary>
        ReferencedAssembly = 2,

        /// <summary>Tier 3 — an assembly-level aggregate (IL2104 / IL3053); the assembly is all there is.</summary>
        AssemblyAggregate = 3,

        /// <summary>
        /// Not one of §11.3's tiers: the line carries an IL code but no origin member and no
        /// assembly — a csc/analyzer-stage diagnostic, or a shape this parser does not know.
        /// It is REPORTED anyway. Dropping it would hide exactly the ceiling BL6020 exists to
        /// make visible, and §11.3 already concedes the granularity varies.
        /// </summary>
        Unattributed = 4,
    }

    /// <summary>
    /// One ILC trim/AOT diagnostic re-expressed as BL6020. Structured rather than pre-formatted so
    /// the eventual caller (P2a-2's phase 5) can route it into whatever diagnostic channel the
    /// build path uses; <see cref="Format"/> renders §11.3's shape for the ones that just want text.
    /// </summary>
    internal sealed record AotDiagnostic(
        string Code,
        string IlCode,
        bool IsWarning,
        AotAttribution Tier,
        string? OriginMember,
        NetWrapperOriginKind? OriginKind,
        string? AssemblyName,
        string? File,
        int Line,
        int Column,
        string IlMessage,
        string RawText)
    {
        /// <summary>§11.3's rendering. The IL code always survives — it is the only token a user can search.</summary>
        internal string Format()
        {
            var sb = new StringBuilder();
            switch (Tier)
            {
                case AotAttribution.WrapperCallSite:
                    var subject = QuotedMember(IlMessage) ?? OriginMember ?? IlCode;
                    sb.Append(Code).Append(": '").Append(subject)
                      .Append("' cannot be used under the AOT shim transport (")
                      .Append(IlCode).Append(").");
                    if (OriginKind == NetWrapperOriginKind.NetProxyDeclaration)
                        sb.Append("\n        Reached through <NetProxy Include=\"")
                          .Append(OriginDescription).Append("\" />.");
                    break;

                case AotAttribution.ReferencedAssembly:
                    sb.Append(Code).Append(": ");
                    if (AssemblyName is not null) sb.Append("assembly '").Append(AssemblyName).Append("', ");
                    sb.Append("member '").Append(OriginMember)
                      .Append("' is not AOT-compatible (").Append(IlCode).Append(").");
                    break;

                case AotAttribution.AssemblyAggregate:
                    sb.Append(Code).Append(": assembly '").Append(AssemblyName)
                      .Append("' is not AOT-compatible (").Append(IlCode).Append(", aggregated).");
                    break;

                default:
                    sb.Append(Code).Append(" (").Append(IlCode).Append("): ").Append(IlMessage);
                    break;
            }

            sb.Append("\n        ").Append(AotDiagnosticMapper.Remedy);

            if (Tier != AotAttribution.AssemblyAggregate && File is not null)
                sb.Append("\n        ").Append(FormatLocation());

            return sb.ToString();
        }

        /// <summary>
        /// Carried only so <see cref="Format"/> can name a <c>&lt;NetProxy&gt;</c> declaration.
        /// Set by the mapper from the resolved <see cref="NetWrapperOrigin"/>.
        /// </summary>
        internal string? OriginDescription { get; init; }

        private string FormatLocation()
        {
            if (File is null) return string.Empty;
            if (Line <= 0) return File;
            var line = Line.ToString(CultureInfo.InvariantCulture);
            return Column <= 0
                ? File + "(" + line + ")"
                : File + "(" + line + "," + Column.ToString(CultureInfo.InvariantCulture) + ")";
        }

        /// <summary>
        /// ILC's "Using member 'X' which has …" names the offending member in quotes, which is what
        /// §11.3's worked example puts in the BL6020. Not every code uses that phrasing (IL2070
        /// does not), hence nullable rather than assumed.
        /// </summary>
        private static string? QuotedMember(string message)
        {
            var m = Regex.Match(message, @"Using member '(?<m>[^']+)'");
            return m.Success ? m.Groups["m"].Value : null;
        }
    }

    /// <summary>
    /// Maps ILC's trim/AOT output onto BL6020 (spec §11.3, D5), at the three attribution tiers and
    /// with §15.10's severity split. Pure: text in, diagnostics out, no I/O and no process spawn —
    /// the publish that produces the text is <see cref="NetShimPublisher"/>'s job, and
    /// <see cref="NetShimPublishResult.Output"/> is the intended input.
    ///
    /// <para><b>Not wired in P2a-1.</b> Phase 5 never runs while every surface is empty (§17), so
    /// nothing calls this yet. P2a-2 wires it.</para>
    ///
    /// <para><b>Four real line shapes, all handled.</b> These were established by capturing actual
    /// ILC output rather than reasoning about the format, and three of the four are NOT what a
    /// reader of the docs would guess:</para>
    /// <list type="number">
    /// <item><description><c>file(line): Trim analysis warning IL2070: &lt;origin&gt;: &lt;msg&gt; [proj]</c>
    /// — ILC stage with source. <b>No column</b>, unlike every csc diagnostic.</description></item>
    /// <item><description><c>ILC : AOT analysis warning IL3050: &lt;origin&gt;: &lt;msg&gt; [proj]</c>
    /// — ILC stage with no source info. This is the NORMAL shape for the reference closure,
    /// because §8.1's <c>&lt;Reference&gt;&lt;HintPath&gt;</c> assemblies come from NuGet and ship
    /// no PDB.</description></item>
    /// <item><description><c>&lt;assembly.dll&gt; : warning IL2104: Assembly 'X' produced trim warnings. … [proj]</c>
    /// — the tier-3 aggregate. Note the SPACE before the colon and the total absence of an origin
    /// member.</description></item>
    /// <item><description><c>file(line,col): warning IL2057: &lt;msg&gt; [proj]</c> — the
    /// csc/analyzer stage, which has a column but <b>no origin member</b> and no "Trim analysis"
    /// prefix. Nothing can attribute it to a wrapper, so it lands
    /// <see cref="AotAttribution.Unattributed"/> rather than being dropped.</description></item>
    /// </list>
    ///
    /// <para><b>The same problem can legitimately arrive TWICE, and is reported twice.</b> Verified
    /// against captured output: the csc trim/AOT analyzer reports a call at
    /// <c>file(line,col)</c> with no origin member, and ILC then reports the same call at
    /// <c>file(line)</c> WITH one. The two lines differ in text, position and available detail, so
    /// no exact-match collapse would merge them and a fuzzy one would risk discarding genuinely
    /// distinct findings — which §11.3's "reported, never dropped" rule forbids. Whether to
    /// present both is a presentation decision for P2a-2's phase 5, made with the full diagnostic
    /// list in hand; it is deliberately not made here.</para>
    ///
    /// <para><b>The assembly name in tier 2 is a heuristic, and deliberately a degrading one.</b>
    /// ILC's per-warning text does NOT carry the declaring assembly — only the aggregate lines do.
    /// So tier 2 matches the origin's declaring type against the reference closure's simple
    /// assembly names on a dot boundary, longest match wins. That is right whenever the assembly
    /// name prefixes its own root namespace (the overwhelmingly common case) and yields
    /// <c>null</c> otherwise. It never GUESSES: an unmatched origin is still reported as tier 2
    /// with its member named, just without an assembly. Reading real metadata to do better would
    /// mean re-opening the closure at diagnostic time for a cosmetic gain.</para>
    /// </summary>
    internal static class AotDiagnosticMapper
    {
        /// <summary>Spec §11.4's row for the AOT ceiling.</summary>
        internal const string DiagnosticCode = "BL6020";

        /// <summary>
        /// §11.3: until P2b ships this names a transport that does not exist yet. That is
        /// deliberate — naming the real reason and the real fix makes the ceiling discoverable
        /// instead of mysterious.
        /// </summary>
        internal const string Remedy = "Switch this project to the CoreCLR hosting transport.";

        /// <summary>
        /// The two assembly-level aggregates. §15.10 pins BOTH to warning — including IL3053,
        /// which would otherwise be caught by the IL3xxx error rule — because an aggregate does
        /// not prove the program's own paths break.
        /// </summary>
        private static readonly HashSet<string> AggregateCodes =
            new(StringComparer.Ordinal) { "IL2104", "IL3053" };

        /// <summary>Any ILxxxx token. The scan is over ALL of them (§11.3), never an allowlist.</summary>
        private static readonly Regex AnyIlCode = new(@"\bIL\d{4}\b", RegexOptions.Compiled);

        /// <summary>
        /// The common skeleton of all four shapes: an optional location prefix, an optional
        /// analysis-stage word, the severity, the code, then the payload. <c>loc</c> is lazy so it
        /// stops at the FIRST severity keyword rather than swallowing one out of the message.
        /// </summary>
        private static readonly Regex DiagnosticLine = new(
            @"^(?<loc>.*?)(?:(?:Trim|AOT|Single-file) analysis\s+)?(?<sev>warning|error)\s+(?<code>IL\d{4})\s*:\s*(?<rest>.*)$",
            RegexOptions.Compiled);

        /// <summary>MSBuild's trailing <c>[C:\path\to.csproj]</c>, which is noise for every consumer here.</summary>
        private static readonly Regex ProjectSuffix = new(@"\s\[[^\]]*\]\s*$", RegexOptions.Compiled);

        private static readonly Regex FileLineColumn = new(
            @"^(?<file>.+)\((?<line>\d+)(?:,(?<col>\d+))?\)$", RegexOptions.Compiled);

        private static readonly Regex AggregateMessage = new(
            @"^Assembly '(?<assembly>[^']+)' produced\b", RegexOptions.Compiled);

        /// <summary>
        /// §15.10's severity split, by DIAGNOSTIC CLASS not by tier: <c>RequiresDynamicCode</c>
        /// (IL3050 and friends) is an error because the call throws at runtime and building would
        /// ship a known crash; trim analysis (IL2026 and friends) is a warning because Microsoft
        /// documents many of them as conservative and not end-developer actionable; the two
        /// assembly-level aggregates are warnings.
        ///
        /// <para>IL3000–IL3002 are single-file-publish codes rather than AOT ones and would be
        /// classified as errors by the IL3xxx rule. They cannot fire here: the shim publishes as
        /// <c>NativeLib=Shared</c>, never <c>PublishSingleFile</c>. Left unspecial-cased rather
        /// than carved out, because a carve-out is an allowlist by another name.</para>
        /// </summary>
        internal static bool IsErrorCode(string ilCode) =>
            !string.IsNullOrEmpty(ilCode)
            && ilCode.StartsWith("IL3", StringComparison.Ordinal)
            && !AggregateCodes.Contains(ilCode);

        /// <summary>
        /// Every ILC trim/AOT diagnostic in <paramref name="ilcOutput"/>, as BL6020.
        /// </summary>
        /// <param name="ilcOutput">
        /// <see cref="NetShimPublishResult.Output"/> — combined stdout+stderr of the publish.
        /// Null or empty yields an empty list, which is what a clean publish produces.
        /// </param>
        /// <param name="provenance">
        /// Task 16's wrapper→BasicLang map. Null is "nothing to resolve", not an error: it simply
        /// means no diagnostic can reach tier 1.
        /// </param>
        /// <param name="referencedAssemblyNames">
        /// Simple assembly names of the §5 reference closure, used for tier 2's attribution. Null
        /// or empty means tier 2 diagnostics are reported without an assembly name.
        /// </param>
        internal static IReadOnlyList<AotDiagnostic> Map(
            string? ilcOutput,
            NetProvenanceMap? provenance = null,
            IReadOnlyCollection<string>? referencedAssemblyNames = null)
        {
            var results = new List<AotDiagnostic>();
            if (string.IsNullOrEmpty(ilcOutput)) return results;

            provenance ??= NetProvenanceMap.Empty;

            // Split on '\n' and strip '\r' rather than splitting on Environment.NewLine: the text
            // arrives from a captured child process and may carry either ending regardless of the
            // host OS, and a stray '\r' would leak into RawText and the project-suffix match.
            foreach (var rawLine in ilcOutput.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r', ' ', '\t');
                if (line.Length == 0) continue;

                var codeMatch = AnyIlCode.Match(line);
                if (!codeMatch.Success) continue;      // ordinary build chatter

                results.Add(Parse(line, codeMatch.Value, provenance, referencedAssemblyNames));
            }

            return results;
        }

        private static AotDiagnostic Parse(
            string line,
            string scannedCode,
            NetProvenanceMap provenance,
            IReadOnlyCollection<string>? referencedAssemblyNames)
        {
            var body = ProjectSuffix.Replace(line, string.Empty);
            var m = DiagnosticLine.Match(body);

            if (!m.Success)
                return Unattributed(scannedCode, body, line);

            var ilCode = m.Groups["code"].Value;
            var rest = m.Groups["rest"].Value.Trim();
            var isWarning = !IsErrorCode(ilCode);

            // ---- Tier 3: assembly-level aggregate --------------------------------------------
            var aggregate = AggregateMessage.Match(rest);
            if (aggregate.Success || AggregateCodes.Contains(ilCode))
            {
                var assembly = aggregate.Success ? aggregate.Groups["assembly"].Value : null;
                return new AotDiagnostic(
                    DiagnosticCode, ilCode, isWarning, AotAttribution.AssemblyAggregate,
                    OriginMember: null, OriginKind: null, AssemblyName: assembly,
                    File: null, Line: 0, Column: 0, IlMessage: rest, RawText: line);
            }

            SplitOrigin(rest, out var originMember, out var message);

            // ---- Tier 1: the origin IS a generated wrapper -----------------------------------
            if (originMember is not null
                && provenance.TryGet(WrapperNameOf(originMember), out var origin))
            {
                return new AotDiagnostic(
                    DiagnosticCode, ilCode, isWarning, AotAttribution.WrapperCallSite,
                    OriginMember: originMember, OriginKind: origin.Kind, AssemblyName: null,
                    File: origin.File, Line: origin.Line, Column: origin.Column,
                    IlMessage: message, RawText: line)
                { OriginDescription = origin.Description };
            }

            // ---- Tier 2: the origin is inside a referenced assembly --------------------------
            if (originMember is not null)
            {
                return new AotDiagnostic(
                    DiagnosticCode, ilCode, isWarning, AotAttribution.ReferencedAssembly,
                    OriginMember: originMember, OriginKind: null,
                    AssemblyName: AttributeToAssembly(originMember, referencedAssemblyNames),
                    File: null, Line: 0, Column: 0, IlMessage: message, RawText: line);
            }

            // ---- No origin member: keep whatever location the line did carry -----------------
            ParseLocation(m.Groups["loc"].Value, out var file, out var fileLine, out var column);
            return new AotDiagnostic(
                DiagnosticCode, ilCode, isWarning, AotAttribution.Unattributed,
                OriginMember: null, OriginKind: null, AssemblyName: null,
                File: file, Line: fileLine, Column: column, IlMessage: rest, RawText: line);
        }

        private static AotDiagnostic Unattributed(string ilCode, string message, string rawText) =>
            new(DiagnosticCode, ilCode, !IsErrorCode(ilCode), AotAttribution.Unattributed,
                OriginMember: null, OriginKind: null, AssemblyName: null,
                File: null, Line: 0, Column: 0, IlMessage: message, RawText: rawText);

        /// <summary>
        /// ILC prefixes its payload with the origin member and a <c>": "</c>. The member always
        /// ends in a parameter list, so the split point is the first <c>"): "</c> — anchoring on
        /// the first bare <c>": "</c> instead would cut a namespace-qualified message in half.
        ///
        /// <para>A candidate containing a quote is rejected: quotes appear only inside ILC's
        /// prose ("Using member 'X'…"), never in an origin member, so that single guard keeps a
        /// message-internal <c>"): "</c> from being mistaken for the separator.</para>
        /// </summary>
        private static void SplitOrigin(string rest, out string? originMember, out string message)
        {
            var i = rest.IndexOf("): ", StringComparison.Ordinal);
            if (i > 0)
            {
                var candidate = rest.Substring(0, i + 1);
                if (candidate.IndexOf('\'') < 0 && candidate.IndexOf('(') > 0)
                {
                    originMember = candidate;
                    message = rest.Substring(i + 3).Trim();
                    return;
                }
            }

            originMember = null;
            message = rest;
        }

        /// <summary>
        /// The mangled export name inside an origin member:
        /// <c>BlnetTestShim.Exports.bl_net_Foo_Bar_1a2b3c4d(UInt64,UInt64*)</c> →
        /// <c>bl_net_Foo_Bar_1a2b3c4d</c>. That is <c>NetNameMangler</c>'s output and therefore
        /// the provenance map's key.
        /// </summary>
        private static string WrapperNameOf(string originMember)
        {
            var paren = originMember.IndexOf('(');
            var qualified = paren > 0 ? originMember.Substring(0, paren) : originMember;
            var dot = qualified.LastIndexOf('.');
            return dot >= 0 ? qualified.Substring(dot + 1) : qualified;
        }

        /// <summary>
        /// Longest reference-closure assembly name that prefixes the origin's DECLARING TYPE on a
        /// dot boundary, or null. See this class's remarks for why this is a heuristic and why it
        /// degrades to null rather than guessing.
        /// </summary>
        private static string? AttributeToAssembly(
            string originMember, IReadOnlyCollection<string>? referencedAssemblyNames)
        {
            if (referencedAssemblyNames is null || referencedAssemblyNames.Count == 0) return null;

            var paren = originMember.IndexOf('(');
            var qualified = paren > 0 ? originMember.Substring(0, paren) : originMember;
            var dot = qualified.LastIndexOf('.');
            if (dot < 0) return null;
            var declaringType = qualified.Substring(0, dot);

            string? best = null;
            foreach (var name in referencedAssemblyNames)
            {
                if (string.IsNullOrEmpty(name)) continue;
                var matches = declaringType.Equals(name, StringComparison.Ordinal)
                    || declaringType.StartsWith(name + ".", StringComparison.Ordinal);
                if (matches && (best is null || name.Length > best.Length)) best = name;
            }
            return best;
        }

        /// <summary>
        /// The location prefix, which is <c>file(line)</c> at the ILC stage, <c>file(line,col)</c>
        /// at the csc stage, the literal <c>ILC</c> when there is no source info, and an assembly
        /// path on an aggregate line.
        /// </summary>
        private static void ParseLocation(string loc, out string? file, out int line, out int column)
        {
            file = null;
            line = 0;
            column = 0;

            var trimmed = loc.Trim().TrimEnd(':').Trim();
            if (trimmed.Length == 0 || trimmed.Equals("ILC", StringComparison.Ordinal)) return;

            var m = FileLineColumn.Match(trimmed);
            if (!m.Success) return;

            file = m.Groups["file"].Value;
            line = int.Parse(m.Groups["line"].Value, CultureInfo.InvariantCulture);
            if (m.Groups["col"].Success)
                column = int.Parse(m.Groups["col"].Value, CultureInfo.InvariantCulture);
        }
    }
}
