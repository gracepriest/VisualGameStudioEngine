namespace BlnetTestShim;

// GENERATED from BlnetContract.StatusCodes — do not edit by hand.
public enum BlnetStatus
{
    /// <summary>Success.</summary>
    BLNET_OK = 0,
    /// <summary>Generation mismatch on an object handle: use-after-release or double-release.</summary>
    BLNET_E_STALE_HANDLE = 1,
    /// <summary>Generation mismatch on a callback handle.</summary>
    BLNET_E_STALE_CALLBACK = 2,
    /// <summary>A .NET exception was caught at the boundary; details via blnet_last_error.</summary>
    BLNET_E_MANAGED_EXCEPTION = 3,
    /// <summary>A native exception was caught inside a callback; details via blnet_last_error.</summary>
    BLNET_E_NATIVE_EXCEPTION = 4,
    /// <summary>Result-bearing callback invoked cross-thread without the Immediate flag.</summary>
    BLNET_E_CROSS_THREAD_RESULT = 5,
    /// <summary>blnet_pump entered concurrently from a second thread.</summary>
    BLNET_E_PUMP_REENTRY = 6,
    /// <summary>blnet_initialize ABI version check failed.</summary>
    BLNET_E_VERSION_MISMATCH = 7,
    /// <summary>Allocation failed at the boundary.</summary>
    BLNET_E_ALLOC = 8,
}

// ---- hand-appended below this line (NOT generated; keep when regenerating the enum above) ----
public static class ShimAbi { public const int AbiVersion = 1; } // keep equal to BlnetContract.AbiVersion (drift-tested)
