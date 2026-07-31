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

        /// <summary>
        /// C4: P0's seven core shim exports, in the order <c>blnet.h</c> declares them and
        /// <c>blnet_bind_core</c> binds them — macro name, the exported symbol, and the C
        /// signature the doc comment carries.
        ///
        /// <para><b>Why this lives here.</b> The same seven names were being spelled out in
        /// three unrelated places: the literal <c>#define</c> block in
        /// <see cref="BlnetRuntimeSources"/>, a hand-typed array in <c>BlnetContractTests</c>,
        /// and (as of P2a-1 Task 14) the generated shim's <c>[UnmanagedCallersOnly]</c>
        /// attributes. Three copies of a list whose members must agree exactly is the drift
        /// shape this class exists to prevent — a name present in the header but missing from
        /// the shim is not a compile error anywhere, it is a startup failure reading
        /// "blnet: shim is missing export '…'". Adding an eighth export here is an ABI change
        /// and bumps <see cref="AbiVersion"/> (rule C7).</para>
        /// </summary>
        public static readonly IReadOnlyList<(string Macro, string Name, string Signature)> CoreExports = new[]
        {
            ("BLNET_EXPORT_ABI_VERSION", "blnet_abi_version", "int32_t (void)"),
            ("BLNET_EXPORT_INITIALIZE", "blnet_initialize", "int32_t (int32_t expected_abi, const BlnetNativeVtable*)"),
            ("BLNET_EXPORT_ADDREF", "blnet_addref", "int32_t (blnet_handle)"),
            ("BLNET_EXPORT_RELEASE", "blnet_release", "int32_t (blnet_handle)"),
            ("BLNET_EXPORT_ALLOC", "blnet_alloc", "void*   (int64_t size) — NULL on failure"),
            ("BLNET_EXPORT_FREE", "blnet_free", "void    (void*)"),
            ("BLNET_EXPORT_LAST_ERROR", "blnet_last_error", "int32_t (char** type_name, char** message) — buffers freed via blnet_free"),
        };

        /// <summary>
        /// Just the exported symbol names of <see cref="CoreExports"/>, which is what both the
        /// generated shim (its <c>EntryPoint</c> strings) and any drift test actually compare on.
        /// </summary>
        public static IReadOnlyList<string> CoreExportNames { get; } =
            CoreExports.Select(e => e.Name).ToArray();

        /// <summary>
        /// The <c>#define BLNET_EXPORT_* "name"</c> block of <c>blnet.h</c>, generated from
        /// <see cref="CoreExports"/>. Column-aligned to the widest macro and the widest quoted
        /// name so the emitted header reads like the hand-written one it replaced.
        /// </summary>
        public static string GenerateCoreExportHeader()
        {
            const int macroColumn = 27;   // widest macro (BLNET_EXPORT_ABI_VERSION, 24) + 3
            const int nameColumn = 22;    // widest quoted name ("blnet_abi_version", 19) + 3
            var sb = new System.Text.StringBuilder();
            foreach (var (macro, name, signature) in CoreExports)
            {
                sb.Append("#define ")
                  .Append(macro.PadRight(macroColumn))
                  .Append(('"' + name + '"').PadRight(nameColumn))
                  .Append("/* ").Append(signature).Append(" */\n");
            }
            return sb.ToString();
        }

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
