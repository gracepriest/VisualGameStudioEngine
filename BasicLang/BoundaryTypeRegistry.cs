namespace BasicLang
{
    /// <summary>Spec C1 category of a type name at the .NET⇄native boundary.</summary>
    public enum BoundaryTypeCategory
    {
        /// <summary>Pure C++ implementation; never crosses as a handle (populated by P1).</summary>
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
    /// (spec C1, docs/superpowers/specs/2026-07-26-dotnet-native-boundary-contract-design.md).
    /// Replaces the previously hand-synchronized CppCapabilityChecker.MappedTypeNames /
    /// UnmappedNetTypes sets; CppTypeMapper._typeMap keys are held to this registry by
    /// BlnetContractTests.MapperInvariant. INVARIANT: Bridged must be exactly the key set
    /// of CppTypeMapper._typeMap MINUS 'Object' (which is Rejected: void* erasure is
    /// unsound). SByte and Decimal are NOT mapped by CppTypeMapper and must stay Rejected.
    /// </summary>
    public static class BoundaryTypeRegistry
    {
        private static readonly HashSet<string> Bridged = new(StringComparer.OrdinalIgnoreCase)
        {
            "Integer", "Long", "Single", "Double", "String", "Boolean", "Char", "Void",
            "Byte", "Short", "UByte", "UShort", "UInteger", "ULong"
        };

        private static readonly HashSet<string> Rejected = new(StringComparer.OrdinalIgnoreCase)
        {
            "Object",
            "Decimal", "SByte",
            "DateTime", "DateTimeOffset", "TimeSpan", "Guid", "StringBuilder", "Regex",
            "Uri", "Stream", "FileInfo", "DirectoryInfo"
        };

        private static readonly HashSet<string> NativeOwned = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> ManagedOwned = new(StringComparer.OrdinalIgnoreCase);

        public static BoundaryTypeCategory Categorize(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return BoundaryTypeCategory.Unknown;
            if (NativeOwned.Contains(typeName)) return BoundaryTypeCategory.NativeOwned;
            if (ManagedOwned.Contains(typeName)) return BoundaryTypeCategory.ManagedOwned;
            if (Bridged.Contains(typeName)) return BoundaryTypeCategory.Bridged;
            if (Rejected.Contains(typeName)) return BoundaryTypeCategory.Rejected;
            return BoundaryTypeCategory.Unknown;
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
