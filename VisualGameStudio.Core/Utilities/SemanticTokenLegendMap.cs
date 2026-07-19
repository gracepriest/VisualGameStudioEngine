using System.Runtime.CompilerServices;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.Core.Utilities;

/// <summary>
/// Duplicate-tolerant remap from a foreign LSP server's semantic-token legend onto
/// BasicLang's canonical client-side legend — the index space the IDE's
/// <c>SemanticTokenHighlighter</c> brushes and modifier tests are keyed to.
/// </summary>
/// <remarks>
/// <para>
/// Semantic-token integers are meaningless without the legend they were defined
/// against (see <see cref="Abstractions.Services.SemanticTokensLegend"/>): a token's
/// type is an index into the SERVER's tokenTypes table, its modifiers a bit set over
/// the SERVER's tokenModifiers table. The highlighter only understands BasicLang's tables, so
/// foreign data (clangd's) must be rewritten index-by-index before display. Without
/// the modifier remap, clangd's readonly bit (4) lands on the highlighter's
/// Deprecated test (canonical bit 4) and every <c>const</c> renders struck-through.
/// </para>
/// <para>
/// ⚠ Server legends repeat names — real clangd 22.1.6 sends "variable" at [0], [1]
/// and [7] (measured, Phase 3b Step 0.2). <see cref="Build"/> therefore iterates
/// SERVER indices and looks each name up in a canonical name→index dictionary; it
/// never <c>Dictionary.Add</c>s over server names (which would throw), and it never
/// dedups (which would silently shift every index after the first duplicate).
/// </para>
/// </remarks>
public sealed class SemanticTokenLegendMap
{
    /// <summary>
    /// The remapped type index for a server token with no canonical slot (e.g. clangd's
    /// "unknown", "concept", "macro", "bracket", "label").
    /// </summary>
    /// <remarks>
    /// 999 is deliberate and load-bearing: any value ≥ 19 falls into
    /// <c>SemanticTokenHighlighter.GetBrushForTokenType</c>'s null-brush default arm, so
    /// the token renders uncolored (the lexer highlighting shows through). The absurd
    /// magnitude is the point — do NOT "tidy" it to 19 or any other near-canonical
    /// value, where a future legend extension would silently give these tokens a real
    /// color that lies about what the server said.
    /// </remarks>
    public const int UncoloredIndex = 999;

    // KEEP IN SYNC — client-side mirror of THE canonical legend, defined by BasicLang's
    // server registration in BasicLang/LSP/SemanticTokensHandler.cs (CreateRegistrationOptions):
    // same wire names, same order, 19 types + 10 modifier bits. That order is also what the
    // IDE's SemanticTokenHighlighter brush switch and modifier bit tests assume. Reorder or
    // extend ALL of them together, or every remapped foreign token silently shifts color.
    // (Matching against server names is case-insensitive ordinal; the entries here are the
    // LSP camelCase wire forms.)
    private static readonly string[] CanonicalTokenTypes =
    {
        "namespace",     // 0
        "type",          // 1
        "class",         // 2
        "enum",          // 3
        "interface",     // 4
        "struct",        // 5
        "typeParameter", // 6
        "parameter",     // 7
        "variable",      // 8
        "property",      // 9
        "enumMember",    // 10
        "function",      // 11
        "method",        // 12
        "keyword",       // 13
        "modifier",      // 14
        "comment",       // 15
        "string",        // 16
        "number",        // 17
        "operator"       // 18
    };

    private static readonly string[] CanonicalTokenModifiers =
    {
        "declaration",    // bit 0
        "definition",     // bit 1
        "readonly",       // bit 2
        "static",         // bit 3
        "deprecated",     // bit 4
        "abstract",       // bit 5
        "async",          // bit 6
        "modification",   // bit 7
        "documentation",  // bit 8
        "defaultLibrary"  // bit 9
    };

    // Built with Dictionary.Add on purpose: the canonical side has no duplicates by
    // construction, and Add is the executable invariant — introduce one and the type
    // initializer throws before any test can pass.
    private static readonly Dictionary<string, int> CanonicalTypeIndex =
        BuildNameIndex(CanonicalTokenTypes);

    private static readonly Dictionary<string, int> CanonicalModifierBit =
        BuildNameIndex(CanonicalTokenModifiers);

    /// <summary>
    /// The no-op map, for servers whose legend IS the canonical one (BasicLang's own
    /// <c>--lsp</c> server) or whose handshake carried no legend at all.
    /// <see cref="RemapData"/> on this instance returns its input array by reference —
    /// the keystroke-path fetch for BasicLang documents allocates nothing here.
    /// </summary>
    public static SemanticTokenLegendMap Identity { get; } = CreateIdentity();

    private readonly int[] _typeMap;      // server type index -> canonical index, or -1
    private readonly int[] _modifierMap;  // server modifier bit -> canonical bit, or -1
    private readonly bool _isIdentity;
    private readonly IReadOnlyList<int> _typeMapView;

    private SemanticTokenLegendMap(int[] typeMap, int[] modifierMap, bool isIdentity)
    {
        _typeMap = typeMap;
        _modifierMap = modifierMap;
        _isIdentity = isIdentity;
        // A read-only VIEW over the live array, never the array itself: exposing _typeMap
        // as IReadOnlyList would let a consumer downcast to int[] and rewrite the shared
        // (cached, Identity-shared) map in place.
        _typeMapView = Array.AsReadOnly(typeMap);
    }

    /// <summary>
    /// Server type index → canonical type index, positionally; <c>-1</c> = no canonical
    /// slot (those tokens are emitted as <see cref="UncoloredIndex"/> by
    /// <see cref="RemapData"/> and render uncolored). Exposed so the measured clangd
    /// table can be pinned as data; consumers rewrite through <see cref="RemapData"/>.
    /// </summary>
    public IReadOnlyList<int> TokenTypeMap => _typeMapView;

    /// <summary>
    /// One built map per captured legend instance. ConditionalWeakTable keys by
    /// REFERENCE identity (it ignores <c>Equals</c>/<c>GetHashCode</c> overrides
    /// entirely), holds keys weakly (a restarted server's dead legend does not pin its
    /// map forever), and is thread-safe — all three properties are load-bearing here.
    /// </summary>
    private static readonly ConditionalWeakTable<SemanticTokensLegend, SemanticTokenLegendMap> BuiltMaps = new();

    /// <summary>
    /// The fetch-seam entry point: <see cref="Build"/>, cached per legend INSTANCE.
    /// The token refresh runs on every debounced keystroke, but a server's legend
    /// object is captured exactly once at the initialize handshake and handed out by
    /// reference from <c>ServerCapabilities</c> — so reference identity is precisely
    /// "same handshake", and the map is built once per server session instead of once
    /// per fetch. Null (no provider, or a provider whose handshake carried no tables)
    /// resolves to <see cref="Identity"/> without touching the cache.
    /// </summary>
    /// <remarks>
    /// ⚠ Reference-keyed on purpose, and not merely because CWT works that way:
    /// <see cref="Abstractions.Services.SemanticTokensLegend"/>'s generated record
    /// equality compares its <c>IReadOnlyList</c>s by reference, so "value equality"
    /// does not exist for this type to key on. Two handshakes that happen to send
    /// identical tables get two maps — cheap, correct, and gone with their legends.
    /// </remarks>
    public static SemanticTokenLegendMap GetOrBuild(SemanticTokensLegend? serverLegend)
    {
        if (serverLegend == null)
            return Identity;

        return BuiltMaps.GetValue(serverLegend, static legend => Build(legend));
    }

    /// <summary>
    /// Builds the remap for <paramref name="serverLegend"/>. Duplicate-tolerant: the
    /// server arrays are iterated positionally and each name is looked up in the
    /// canonical tables (case-insensitive ordinal), so repeated names simply map to the
    /// same canonical slot. A legend equal to the canonical list returns the shared
    /// <see cref="Identity"/> instance (reference), so BasicLang's path allocates
    /// nothing per fetch.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A NULL legend (provider announced, handshake carried no tables — a real state,
    /// see <see cref="ServerCapabilities.SemanticTokensLegend"/>) is primarily
    /// the CALLER's Identity case: the fetch-seam wiring resolves null to
    /// <see cref="Identity"/> before ever calling here. Build tolerates null with the
    /// same answer as defense in depth, not as an invitation to route nulls through it.
    /// </para>
    /// <para>
    /// An EMPTY legend is not null: <c>"tokenTypes":[]</c> arrives as a non-null legend
    /// with zero entries, and builds a real (non-identity) map under which every type
    /// index is out of range → <see cref="UncoloredIndex"/> and every modifier bit is
    /// undeclared → masked out.
    /// </para>
    /// <para>
    /// ⚠ Do not compare legends with <c>==</c> to decide anything here:
    /// <see cref="Abstractions.Services.SemanticTokensLegend"/> is a record over
    /// <c>IReadOnlyList</c>s, so its generated equality compares the LISTS by reference,
    /// not element-wise. The canonical check below compares names positionally itself.
    /// </para>
    /// </remarks>
    public static SemanticTokenLegendMap Build(SemanticTokensLegend serverLegend)
    {
        if (serverLegend == null)
            return Identity;

        IReadOnlyList<string> serverTypes = serverLegend.TokenTypes ?? Array.Empty<string>();
        IReadOnlyList<string> serverModifiers = serverLegend.TokenModifiers ?? Array.Empty<string>();

        if (MatchesCanonical(serverTypes, CanonicalTokenTypes) &&
            MatchesCanonical(serverModifiers, CanonicalTokenModifiers))
        {
            return Identity;
        }

        var typeMap = new int[serverTypes.Count];
        for (int i = 0; i < typeMap.Length; i++)
            typeMap[i] = CanonicalTypeIndex.TryGetValue(serverTypes[i], out int idx) ? idx : -1;

        var modifierMap = new int[serverModifiers.Count];
        for (int bit = 0; bit < modifierMap.Length; bit++)
            modifierMap[bit] = CanonicalModifierBit.TryGetValue(serverModifiers[bit], out int b) ? b : -1;

        return new SemanticTokenLegendMap(typeMap, modifierMap, isIdentity: false);
    }

    /// <summary>
    /// Rewrites LSP-encoded semantic-token data — quintuples of
    /// <c>[deltaLine, deltaStartChar, length, tokenType, tokenModifiers]</c> — from the
    /// server's legend onto the canonical one. Positions and lengths pass through
    /// untouched; only fields 3 (type, via the table) and 4 (modifiers, via the bit map;
    /// unmapped bits are masked out) are rewritten, into a fresh copy. On the identity
    /// map, or for data whose length is not a multiple of five (mirroring
    /// <c>SemanticTokenHighlighter.Update</c>'s guard), the INPUT ARRAY REFERENCE is
    /// returned unchanged.
    /// </summary>
    public int[] RemapData(int[] encodedData)
    {
        if (encodedData == null || _isIdentity ||
            encodedData.Length == 0 || encodedData.Length % 5 != 0)
        {
            return encodedData;
        }

        var result = new int[encodedData.Length];
        for (int i = 0; i < encodedData.Length; i += 5)
        {
            result[i] = encodedData[i];         // deltaLine
            result[i + 1] = encodedData[i + 1]; // deltaStartChar
            result[i + 2] = encodedData[i + 2]; // length

            int serverType = encodedData[i + 3];
            int mapped = (uint)serverType < (uint)_typeMap.Length ? _typeMap[serverType] : -1;
            result[i + 3] = mapped >= 0 ? mapped : UncoloredIndex;

            result[i + 4] = RemapModifierBits(encodedData[i + 4]);
        }

        return result;
    }

    private int RemapModifierBits(int serverModifiers)
    {
        int remapped = 0;
        // Bits at or beyond the server legend's length have no meaning the server ever
        // declared — the loop never visits them, so they are masked out along with the
        // declared-but-unmappable ones.
        for (int bit = 0; bit < _modifierMap.Length && bit < 32; bit++)
        {
            if ((serverModifiers & (1 << bit)) != 0 && _modifierMap[bit] >= 0)
                remapped |= 1 << _modifierMap[bit];
        }

        return remapped;
    }

    private static SemanticTokenLegendMap CreateIdentity()
    {
        var typeMap = new int[CanonicalTokenTypes.Length];
        for (int i = 0; i < typeMap.Length; i++)
            typeMap[i] = i;

        var modifierMap = new int[CanonicalTokenModifiers.Length];
        for (int bit = 0; bit < modifierMap.Length; bit++)
            modifierMap[bit] = bit;

        return new SemanticTokenLegendMap(typeMap, modifierMap, isIdentity: true);
    }

    private static Dictionary<string, int> BuildNameIndex(string[] canonicalNames)
    {
        var index = new Dictionary<string, int>(canonicalNames.Length, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < canonicalNames.Length; i++)
            index.Add(canonicalNames[i], i);
        return index;
    }

    private static bool MatchesCanonical(IReadOnlyList<string> serverNames, string[] canonicalNames)
    {
        if (serverNames.Count != canonicalNames.Length)
            return false;

        for (int i = 0; i < canonicalNames.Length; i++)
        {
            if (!string.Equals(serverNames[i], canonicalNames[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }
}
