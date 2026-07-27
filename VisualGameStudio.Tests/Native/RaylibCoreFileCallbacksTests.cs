using System;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// HEADLESS END-TO-END proof for the rcore half of the deferred callbacks batch (TraceLog + the 4 file-I/O callbacks). These
/// are not ABI smoke: each test installs a managed delegate, triggers the matching raylib function (SaveFileData/LoadFileData/
/// SaveFileText/LoadFileText/TraceLog — all bound + headless), and asserts the callback actually FIRED with the correct
/// marshaled arguments and that its return value propagated. That proves the whole chain: delegate → native fn-pointer →
/// raylib stores it → raylib invokes it → args/return marshal back. [NonParallelizable] because these mutate global raylib
/// callback state; each test resets its callback to Nothing (default) in a finally and GC.KeepAlive's the delegate across the
/// trigger. Self-Ignores when the DLL isn't staged or predates the callback exports.
/// </summary>
[TestFixture]
[NonParallelizable]
public class RaylibCoreFileCallbacksTests
{
    private const string DLL = "VisualGameStudioEngine.dll";
    private const CallingConvention CC = CallingConvention.Cdecl;
    private const int LOG_WARNING = 4;

    [UnmanagedFunctionPointer(CC)] private delegate void TraceLogCb(int logLevel, IntPtr text, IntPtr args);
    [UnmanagedFunctionPointer(CC)] private delegate IntPtr LoadFileDataCb(IntPtr fileName, ref int dataSize);
    [UnmanagedFunctionPointer(CC)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool SaveFileDataCb(IntPtr fileName, IntPtr data, int dataSize);
    [UnmanagedFunctionPointer(CC)] private delegate IntPtr LoadFileTextCb(IntPtr fileName);
    [UnmanagedFunctionPointer(CC)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool SaveFileTextCb(IntPtr fileName, IntPtr text);

    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_SetTraceLogCallback(TraceLogCb callback);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_SetLoadFileDataCallback(LoadFileDataCb callback);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_SetSaveFileDataCallback(SaveFileDataCb callback);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_SetLoadFileTextCallback(LoadFileTextCb callback);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_SetSaveFileTextCallback(SaveFileTextCb callback);

    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern IntPtr Framework_LoadFileData(string fileName, out int dataSize);
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_SaveFileData(string fileName, IntPtr data, int dataSize);
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern IntPtr Framework_LoadFileText(string fileName);
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_SaveFileText(string fileName, string text);
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern void Framework_TraceLog(int logLevel, string text);

    [Test]
    public void SetSaveFileDataCallback_intercepts_SaveFileData()
    {
        bool fired = false; string gotName = null; int gotSize = -1;
        // Return FALSE so the return-propagation check is independent of raylib's default path (which returns true on a
        // successful write) — ok==false can ONLY come from the intercepting callback.
        SaveFileDataCb cb = (fileNamePtr, data, dataSize) => { fired = true; gotName = Marshal.PtrToStringAnsi(fileNamePtr); gotSize = dataSize; return false; };
        if (!TrySet(() => Framework_SetSaveFileDataCallback(cb))) return;
        try
        {
            byte[] payload = { 1, 2, 3, 4, 5 };
            IntPtr p = Marshal.AllocHGlobal(payload.Length);
            Marshal.Copy(payload, 0, p, payload.Length);
            bool ok;
            try { ok = Framework_SaveFileData("vgs_cb_save.bin", p, payload.Length); }
            finally { Marshal.FreeHGlobal(p); }

            Assert.Multiple(() =>
            {
                Assert.That(fired, Is.True, "the custom SaveFileData callback fired");
                Assert.That(gotName, Is.EqualTo("vgs_cb_save.bin"), "callback received the fileName");
                Assert.That(gotSize, Is.EqualTo(5), "callback received the dataSize");
                Assert.That(ok, Is.False, "the callback's false return propagated (default success returns true, so ok==false proves interception)");
            });
        }
        finally { try { Framework_SetSaveFileDataCallback(null); } catch { } GC.KeepAlive(cb); }
    }

    [Test]
    public void SetLoadFileDataCallback_intercepts_LoadFileData()
    {
        IntPtr fakeBuf = Marshal.AllocHGlobal(3);
        Marshal.Copy(new byte[] { 10, 20, 30 }, 0, fakeBuf, 3);
        bool fired = false; string gotName = null;
        LoadFileDataCb cb = (IntPtr fileNamePtr, ref int dataSize) => { fired = true; gotName = Marshal.PtrToStringAnsi(fileNamePtr); dataSize = 3; return fakeBuf; };
        if (!TrySet(() => Framework_SetLoadFileDataCallback(cb))) { Marshal.FreeHGlobal(fakeBuf); return; }
        try
        {
            IntPtr result = Framework_LoadFileData("vgs_cb_load.bin", out int outSize);
            Assert.Multiple(() =>
            {
                Assert.That(fired, Is.True, "the custom LoadFileData callback fired");
                Assert.That(gotName, Is.EqualTo("vgs_cb_load.bin"), "callback received the fileName");
                Assert.That(outSize, Is.EqualTo(3), "the callback's dataSize (int* out) propagated");
                Assert.That(result, Is.EqualTo(fakeBuf), "the callback's returned buffer pointer propagated");
            });
        }
        finally
        {
            try { Framework_SetLoadFileDataCallback(null); } catch { }
            Marshal.FreeHGlobal(fakeBuf); // WE own this buffer (AllocHGlobal) — do NOT UnloadFileData it (mismatched heap).
            GC.KeepAlive(cb);
        }
    }

    [Test]
    public void SetSaveFileTextCallback_intercepts_SaveFileText()
    {
        bool fired = false; string gotName = null; string gotText = null;
        // Return FALSE (independent of the default true) so ok==false can only come from the intercepting callback.
        SaveFileTextCb cb = (fileNamePtr, textPtr) => { fired = true; gotName = Marshal.PtrToStringAnsi(fileNamePtr); gotText = Marshal.PtrToStringAnsi(textPtr); return false; };
        if (!TrySet(() => Framework_SetSaveFileTextCallback(cb))) return;
        try
        {
            bool ok = Framework_SaveFileText("vgs_cb.txt", "hello callback");
            Assert.Multiple(() =>
            {
                Assert.That(fired, Is.True, "the custom SaveFileText callback fired");
                Assert.That(gotName, Is.EqualTo("vgs_cb.txt"), "callback received the fileName");
                Assert.That(gotText, Is.EqualTo("hello callback"), "callback received the text");
                Assert.That(ok, Is.False, "the callback's false return propagated (independent of the default success true)");
            });
        }
        finally { try { Framework_SetSaveFileTextCallback(null); } catch { } GC.KeepAlive(cb); }
    }

    [Test]
    public void SetLoadFileTextCallback_intercepts_LoadFileText()
    {
        IntPtr fakeText = Marshal.StringToHGlobalAnsi("callback file text");
        bool fired = false; string gotName = null;
        LoadFileTextCb cb = (fileNamePtr) => { fired = true; gotName = Marshal.PtrToStringAnsi(fileNamePtr); return fakeText; };
        if (!TrySet(() => Framework_SetLoadFileTextCallback(cb))) { Marshal.FreeHGlobal(fakeText); return; }
        try
        {
            IntPtr result = Framework_LoadFileText("vgs_cb_load.txt");
            Assert.Multiple(() =>
            {
                Assert.That(fired, Is.True, "the custom LoadFileText callback fired");
                Assert.That(gotName, Is.EqualTo("vgs_cb_load.txt"), "callback received the fileName");
                Assert.That(Marshal.PtrToStringAnsi(result), Is.EqualTo("callback file text"), "the callback's returned char* propagated");
            });
        }
        finally
        {
            try { Framework_SetLoadFileTextCallback(null); } catch { }
            Marshal.FreeHGlobal(fakeText);
            GC.KeepAlive(cb);
        }
    }

    [Test]
    public void SetTraceLogCallback_intercepts_TraceLog()
    {
        bool fired = false; bool sawWarning = false;
        // NOTE: text arrives as the format string "%s" here, because Framework_TraceLog (Batch C6) forwards as
        // TraceLog(level, "%s", text) to dodge varargs — the message lives in the va_list (args, opaque on Win64). We
        // therefore assert the callback FIRED and the log level marshaled, not the message text.
        TraceLogCb cb = (logLevel, textPtr, args) => { fired = true; if (logLevel == LOG_WARNING) sawWarning = true; };
        if (!TrySet(() => Framework_SetTraceLogCallback(cb))) return;
        try
        {
            Framework_TraceLog(LOG_WARNING, "hello from a test");
            Assert.Multiple(() =>
            {
                Assert.That(fired, Is.True, "the custom trace-log callback fired");
                Assert.That(sawWarning, Is.True, "callback received the LOG_WARNING level");
            });
        }
        finally { try { Framework_SetTraceLogCallback(null); } catch { } GC.KeepAlive(cb); }
    }

    // Installs a callback; returns false (after Assert.Ignore) if the DLL is missing or predates the callback exports.
    private static bool TrySet(Action set)
    {
        try { set(); return true; }
        catch (DllNotFoundException) { Assert.Ignore($"{DLL} not staged next to the test binary; refresh IDE\\ first."); return false; }
        catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates the deferred-callback exports; refresh IDE\\ first."); return false; }
    }
}
