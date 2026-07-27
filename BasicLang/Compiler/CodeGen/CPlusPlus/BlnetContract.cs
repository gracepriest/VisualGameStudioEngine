namespace BasicLang.Compiler.CodeGen.CPlusPlus
{
    /// <summary>
    /// Single source of truth for the .NET⇄native boundary ABI constants
    /// (spec: docs/superpowers/specs/2026-07-26-dotnet-native-boundary-contract-design.md, C4/C7).
    /// The C header status section and the shim's C# enum are BOTH generated from this
    /// table (drift-tested in BlnetContractTests) — never edit those by hand.
    /// </summary>
    public static class BlnetContract
    {
        /// <summary>C7: bumped on ANY change to the ABI — status codes, slot encoding, export signatures.</summary>
        public const int AbiVersion = 1;

        public static readonly IReadOnlyList<(string Name, int Value, string Doc)> StatusCodes = new[]
        {
            ("BLNET_OK", 0, "Success."),
            ("BLNET_E_STALE_HANDLE", 1, "Generation mismatch on an object handle: use-after-release or double-release."),
            ("BLNET_E_STALE_CALLBACK", 2, "Generation mismatch on a callback handle."),
            ("BLNET_E_MANAGED_EXCEPTION", 3, "A .NET exception was caught at the boundary; details via blnet_last_error."),
            ("BLNET_E_NATIVE_EXCEPTION", 4, "A native exception was caught inside a callback; details via blnet_last_error."),
            ("BLNET_E_CROSS_THREAD_RESULT", 5, "Result-bearing callback invoked cross-thread without the Immediate flag."),
            ("BLNET_E_PUMP_REENTRY", 6, "blnet_pump entered concurrently from a second thread."),
            ("BLNET_E_VERSION_MISMATCH", 7, "blnet_initialize ABI version check failed."),
            ("BLNET_E_ALLOC", 8, "Allocation failed at the boundary."),
        };

        public static string GenerateStatusHeader()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("/* GENERATED from BlnetContract — do not edit by hand. */\n");
            sb.Append($"#define BLNET_ABI_VERSION {AbiVersion}\n");
            foreach (var (name, value, doc) in StatusCodes)
                sb.Append($"#define {name} {value} /* {doc} */\n");
            return sb.ToString();
        }

        public static string GenerateStatusEnumCs()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("// GENERATED from BlnetContract.StatusCodes — do not edit by hand.\n");
            sb.Append("public enum BlnetStatus\n{\n");
            foreach (var (name, value, doc) in StatusCodes)
                sb.Append($"    /// <summary>{doc}</summary>\n    {name} = {value},\n");
            sb.Append("}\n");
            return sb.ToString();
        }
    }
}
