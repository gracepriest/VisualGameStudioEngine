using System.Runtime.InteropServices;
using System.Text;

namespace BlnetTestShim;

public static unsafe class Exports
{
    internal static readonly HandleTable Table = new();
    private static delegate* unmanaged[Cdecl]<ulong, ulong*, int, ulong*, int> _thunk;
    private static delegate* unmanaged[Cdecl]<byte**, int> _getNativeError;

    [ThreadStatic] private static string? _lastErrorType;
    [ThreadStatic] private static string? _lastErrorMessage;

    private static int Fail(Exception ex)
    {
        try { _lastErrorType = ex.GetType().FullName; _lastErrorMessage = ex.Message; }
        catch { /* C4: the handler itself must be non-throwing; degrade to status-only */ }
        return (int)BlnetStatus.BLNET_E_MANAGED_EXCEPTION;
    }

    private static byte* AllocUtf8(string s)
    {
        var bytes = Encoding.UTF8.GetByteCount(s);
        var buf = (byte*)NativeMemory.Alloc((nuint)(bytes + 1));
        fixed (char* c = s) Encoding.UTF8.GetBytes(c, s.Length, buf, bytes);
        buf[bytes] = 0;
        return buf;
    }
    private static string? Utf8ToString(byte* p) => p == null ? null : Marshal.PtrToStringUTF8((nint)p);

    [UnmanagedCallersOnly(EntryPoint = "blnet_abi_version", CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    public static int AbiVersion() => ShimAbi.AbiVersion; // single source: drift-tested against BlnetContract.AbiVersion

    [UnmanagedCallersOnly(EntryPoint = "blnet_initialize", CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    public static int Initialize(int expectedAbi, void* vtable)
    {
        if (expectedAbi != ShimAbi.AbiVersion) return (int)BlnetStatus.BLNET_E_VERSION_MISMATCH;
        var vt = (void**)vtable;
        _thunk = (delegate* unmanaged[Cdecl]<ulong, ulong*, int, ulong*, int>)vt[0];
        _getNativeError = (delegate* unmanaged[Cdecl]<byte**, int>)vt[1];
        return (int)BlnetStatus.BLNET_OK;
    }

    [UnmanagedCallersOnly(EntryPoint = "blnet_addref", CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    public static int AddRef(ulong h) { try { return (int)Table.AddRef(h); } catch (Exception ex) { return Fail(ex); } }

    [UnmanagedCallersOnly(EntryPoint = "blnet_release", CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    public static int Release(ulong h) { try { return (int)Table.Release(h); } catch (Exception ex) { return Fail(ex); } }

    [UnmanagedCallersOnly(EntryPoint = "blnet_alloc", CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    public static void* Alloc(long size) { try { return NativeMemory.Alloc((nuint)size); } catch { return null; } }

    [UnmanagedCallersOnly(EntryPoint = "blnet_free", CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    public static void Free(void* p) { if (p != null) NativeMemory.Free(p); }

    [UnmanagedCallersOnly(EntryPoint = "blnet_last_error", CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    public static int LastError(byte** typeName, byte** message)
    {
        try
        {
            if (typeName != null) *typeName = _lastErrorType is null ? null : AllocUtf8(_lastErrorType);
            if (message != null) *message = _lastErrorMessage is null ? null : AllocUtf8(_lastErrorMessage);
            return (int)BlnetStatus.BLNET_OK;
        }
        catch (Exception ex) { return Fail(ex); }
    }

    // ---- test exports (drive conformance scenarios; NOT part of the contract) ----

    [UnmanagedCallersOnly(EntryPoint = "blnet_test_create_list", CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    public static int TestCreateList(ulong* outHandle)
    { try { *outHandle = Table.Create(new List<int>()); return (int)BlnetStatus.BLNET_OK; } catch (Exception ex) { return Fail(ex); } }

    [UnmanagedCallersOnly(EntryPoint = "blnet_test_list_add", CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    public static int TestListAdd(ulong h, int value)
    {
        try
        {
            var st = Table.TryGet(h, out var o);
            if (st != BlnetStatus.BLNET_OK) return (int)st;
            ((List<int>)o!).Add(value);
            return (int)BlnetStatus.BLNET_OK;
        }
        catch (Exception ex) { return Fail(ex); }
    }

    [UnmanagedCallersOnly(EntryPoint = "blnet_test_list_count", CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    public static int TestListCount(ulong h, int* outCount)
    {
        try
        {
            var st = Table.TryGet(h, out var o);
            if (st != BlnetStatus.BLNET_OK) return (int)st;
            *outCount = ((List<int>)o!).Count;
            return (int)BlnetStatus.BLNET_OK;
        }
        catch (Exception ex) { return Fail(ex); }
    }

    [UnmanagedCallersOnly(EntryPoint = "blnet_test_echo", CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    public static int TestEcho(byte* input, byte** output)
    { try { *output = AllocUtf8(Utf8ToString(input) ?? ""); return (int)BlnetStatus.BLNET_OK; } catch (Exception ex) { return Fail(ex); } }

    [UnmanagedCallersOnly(EntryPoint = "blnet_test_throw", CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    public static int TestThrow()
    { try { throw new ArgumentException("bøøm from managed"); } catch (Exception ex) { return Fail(ex); } }

    [UnmanagedCallersOnly(EntryPoint = "blnet_test_invoke", CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    public static int TestInvoke(ulong cb, ulong* args, int argc, ulong* result)
    {
        try
        {
            int st = _thunk(cb, args, argc, result);
            if (st == (int)BlnetStatus.BLNET_E_NATIVE_EXCEPTION && _getNativeError != null)
            {
                byte* msg = null;
                if (_getNativeError(&msg) == (int)BlnetStatus.BLNET_OK && msg != null)
                { _lastErrorType = "BasicLangNativeException"; _lastErrorMessage = Utf8ToString(msg); NativeMemory.Free(msg); /* == blnet_free's allocator: the buffer came from blnet_alloc */ }
            }
            return st;
        }
        catch (Exception ex) { return Fail(ex); }
    }

    [UnmanagedCallersOnly(EntryPoint = "blnet_test_invoke_from_thread", CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    public static int TestInvokeFromThread(ulong cb, ulong* args, int argc)
    {
        try
        {
            // Copy args: the .NET thread outlives this frame's pointers validity window otherwise.
            var local = new ulong[argc];
            for (int i = 0; i < argc; i++) local[i] = args[i];
            int st = 0;
            // NB: 'fixed' over a ZERO-length array yields a null pointer — fine for argc == 0
            // (the thunk never dereferences args then); do not "fix" this.
            var t = new Thread(() => { fixed (ulong* p = local) st = _thunk(cb, p, argc, null); });
            t.Start(); t.Join();
            return st; // cross-thread: queued (notification) / BLNET_E_CROSS_THREAD_RESULT (result-bearing)
        }
        catch (Exception ex) { return Fail(ex); }
    }
}
