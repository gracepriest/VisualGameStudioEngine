namespace BasicLang
{
    /// <summary>Spec C1 category of a type name at the .NET⇄native boundary.</summary>
    public enum BoundaryTypeCategory
    {
        /// <summary>Pure C++ implementation; never crosses as a handle (the P1 six).</summary>
        NativeOwned,
        /// <summary>GC-heap object; crosses only as a generation-tagged handle (populated by P2).</summary>
        ManagedOwned,
        /// <summary>Value-converted at the edge; both sides have a native representation.</summary>
        Bridged,
        /// <summary>Known to the registry, no permitted use in native projects (e.g. Object: void* erasure is unsound).</summary>
        Rejected,
        /// <summary>Not a registry name — user-defined / generic / foreign types resolve elsewhere.</summary>
        Unknown,
    }

    /// <summary>
    /// Single source of truth for boundary type ownership
    /// (spec C1, docs/superpowers/specs/2026-07-26-dotnet-native-boundary-contract-design.md;
    /// P1 native BCL types per docs/superpowers/specs/2026-07-27-p1-native-bcl-types-design.md §2).
    /// Replaces the previously hand-synchronized CppCapabilityChecker.MappedTypeNames /
    /// UnmappedNetTypes sets.
    /// <para>
    /// POST-P1 INVARIANTS — two mechanical drift tests hold these:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>
    ///     <b>Bridged == CppTypeMapper._typeMap keys MINUS 'Object'</b> (which is Rejected:
    ///     void* erasure is unsound). Held by BlnetContractTests.MapperInvariant. Bridged
    ///     types have a plain primitive C++ representation and NOTHING else to declare —
    ///     SByte joined this set in P1 as <c>int8_t</c>.
    ///   </description></item>
    ///   <item><description>
    ///     <b>NativeOwned == NativeBclSurface.TypeNames</b> (both directions). Held by
    ///     NativeBclFrontEndTests.SurfaceCoherence_AllSevenTypesHaveEntries. NativeOwned
    ///     types NEVER enter any _typeMap: CppCodeGenerator.MapType handles them BY
    ///     CATEGORY (→ <c>BasicLang::&lt;Name&gt;</c>, or a shared_ptr for the one
    ///     reference type, StringBuilder), and every member they expose must appear in
    ///     NativeBclSurface or the capability checker's member pass rejects it.
    ///   </description></item>
    /// </list>
    /// Adding a name to NativeOwned therefore requires a surface table AND a native C++
    /// implementation; adding one to Bridged requires a CppTypeMapper entry.
    /// </summary>
    public static class BoundaryTypeRegistry
    {
        private static readonly HashSet<string> Bridged = new(StringComparer.OrdinalIgnoreCase)
        {
            "Integer", "Long", "Single", "Double", "String", "Boolean", "Char", "Void",
            "Byte", "SByte", "Short", "UByte", "UShort", "UInteger", "ULong"
        };

        private static readonly HashSet<string> Rejected = new(StringComparer.OrdinalIgnoreCase)
        {
            // Object stays permanently Rejected (void* erasure is unsound). The five former
            // P2-territory names moved to ManagedOwned at the P2a-2 flip (spec §11.4).
            "Object",
        };

        /// <summary>
        /// The P1 six (spec §2): pure C++ implementations in <c>namespace BasicLang</c>
        /// (bl_bcltypes.hpp / bl_decimal.hpp), never crossing the boundary as a handle.
        /// Must stay in lockstep with <see cref="NativeBclSurface.TypeNames"/>.
        /// </summary>
        private static readonly HashSet<string> NativeOwned = new(StringComparer.OrdinalIgnoreCase)
        {
            "DateTime", "TimeSpan", "Guid", "StringBuilder", "Decimal", "DateTimeOffset"
        };

        /// <summary>
        /// P2a-2 THE FLIP (spec §11.4): the five curated GC-heap names cross the boundary as
        /// generation-tagged handles (<c>BasicLang::NetRef</c> at every declaration position —
        /// CppCodeGenerator.MapType keys off this category). The registry stays a static,
        /// curated, simple-name-keyed table BY DESIGN: arbitrary resolved .NET types remain
        /// <see cref="BoundaryTypeCategory.Unknown"/> here and are handle-represented by spec
        /// §8.3's wire-form rule through <c>NetTypeResolver</c>. §12.4 invariants (held by
        /// NetFlipTests): ManagedOwned ∩ Rejected = ∅ (<see cref="Categorize"/> checks
        /// ManagedOwned first, so an overlap would resolve silently), and every name here —
        /// and no other registry name — maps to <c>NetRef</c>.
        /// </summary>
        private static readonly HashSet<string> ManagedOwned = new(StringComparer.OrdinalIgnoreCase)
        {
            "Regex", "Uri", "Stream", "FileInfo", "DirectoryInfo"
        };

        public static BoundaryTypeCategory Categorize(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return BoundaryTypeCategory.Unknown;
            var name = Normalize(typeName);
            if (NativeOwned.Contains(name)) return BoundaryTypeCategory.NativeOwned;
            if (ManagedOwned.Contains(name)) return BoundaryTypeCategory.ManagedOwned;
            if (Bridged.Contains(name)) return BoundaryTypeCategory.Bridged;
            if (Rejected.Contains(name)) return BoundaryTypeCategory.Rejected;
            return BoundaryTypeCategory.Unknown;
        }

        /// <summary>
        /// Strips a <c>System.</c> namespace qualifier ("System.DateTime" /
        /// "System.Text.StringBuilder" → last segment), mirroring
        /// <see cref="NativeBclSurface"/>'s own normalization so both spellings land in
        /// the same category. Without this a qualified name categorized Unknown: a hole
        /// in the reject net before P1, and after the flip it would bypass EVERY piece of
        /// NativeOwned codegen machinery (all of which keys off <see cref="Categorize"/>)
        /// and emit a bare, undefined C++ type name. Anything not starting with
        /// "System." passes through untouched — a user type named <c>MyApp.Guid</c> is
        /// still Unknown and resolves as a user-defined type.
        /// </summary>
        private static string Normalize(string typeName)
        {
            if (!typeName.StartsWith("System.", StringComparison.OrdinalIgnoreCase)) return typeName;
            var lastDot = typeName.LastIndexOf('.');
            return typeName.Substring(lastDot + 1);
        }

        public static IReadOnlyCollection<string> NamesInCategory(BoundaryTypeCategory category) =>
            category switch
            {
                BoundaryTypeCategory.NativeOwned => NativeOwned,
                BoundaryTypeCategory.ManagedOwned => ManagedOwned,
                BoundaryTypeCategory.Bridged => Bridged,
                BoundaryTypeCategory.Rejected => Rejected,
                _ => Array.Empty<string>(),
            };
    }
}
