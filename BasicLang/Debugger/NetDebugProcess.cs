using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace BasicLang.Debugger
{
    // =========================================================================
    // Event argument classes
    // =========================================================================

    public class BreakpointHitEventArgs : EventArgs
    {
        public int ThreadId { get; set; }
        public ICorDebugBreakpoint BreakpointToken { get; set; }
    }

    public class StepCompletedEventArgs : EventArgs
    {
        public int ThreadId { get; set; }
    }

    public class ExceptionThrownEventArgs : EventArgs
    {
        public int ThreadId { get; set; }
        public bool IsUnhandled { get; set; }
        public string ExceptionType { get; set; }
        public string ExceptionMessage { get; set; }
    }

    public class ModuleLoadedEventArgs : EventArgs
    {
        public string ModuleName { get; set; }
        public string ModulePath { get; set; }
        public ICorDebugModule Module { get; set; }
    }

    public class ProcessExitedEventArgs : EventArgs
    {
        public int ExitCode { get; set; }
    }

    public class ThreadEventArgs : EventArgs
    {
        public int ThreadId { get; set; }
    }

    // =========================================================================
    // NetDebugProcess — launches a .NET process, attaches ICorDebug via dbgshim
    // =========================================================================

    public class NetDebugProcess : IDisposable
    {
        // Events for the DAP adapter to subscribe to
        public event EventHandler<BreakpointHitEventArgs> BreakpointHit;
        public event EventHandler<StepCompletedEventArgs> StepCompleted;
        public event EventHandler<ExceptionThrownEventArgs> ExceptionThrown;
        public event EventHandler<ModuleLoadedEventArgs> ModuleLoaded;
        public event EventHandler<ProcessExitedEventArgs> ProcessExited;
        public event EventHandler<ThreadEventArgs> ThreadCreated;
        public event EventHandler<ThreadEventArgs> ThreadExited;

        private Process _process;
        private ICorDebug _corDebug;
        private ICorDebugProcess _corDebugProcess;
        private IntPtr _runtimeStartupToken;
        private readonly Dictionary<int, ICorDebugThread> _threads = new();
        private readonly ManualResetEventSlim _attachedEvent = new(false);
        private bool _attached;
        private bool _disposed;

        // Must prevent the delegate from being GC'd while dbgshim holds it
        private DbgShim.RuntimeStartupCallback _startupCallback;

        /// <summary>
        /// The underlying OS process being debugged.
        /// </summary>
        public Process Process => _process;

        /// <summary>
        /// The ICorDebugProcess for the attached CLR runtime.
        /// </summary>
        public ICorDebugProcess CorDebugProcess => _corDebugProcess;

        /// <summary>
        /// Whether the debugger is currently attached to the CLR.
        /// </summary>
        public bool IsAttached => _attached;

        /// <summary>
        /// Last error message if launch/attach failed.
        /// </summary>
        public string LastError { get; private set; }

        /// <summary>
        /// Snapshot of tracked managed thread IDs to ICorDebugThread instances.
        /// </summary>
        public IReadOnlyDictionary<int, ICorDebugThread> Threads => _threads;

        // =====================================================================
        // Launch and attach
        // =====================================================================

        /// <summary>
        /// Launch the target .exe and wait for the CLR to load, then attach ICorDebug.
        /// </summary>
        public async Task<bool> LaunchAsync(string exePath, string workingDir, string[] args)
        {
            // 1. Verify dbgshim.dll is available
            if (!DbgShim.TryLoad())
            {
                LastError = "dbgshim.dll not found. Install Visual Studio 2022 or copy dbgshim.dll next to BasicLang.exe.";
                return false;
            }

            // 2. Launch the process normally
            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = workingDir ?? Path.GetDirectoryName(exePath),
                UseShellExecute = false,
                // Don't redirect stdio for game processes (they need their own window)
            };
            if (args != null && args.Length > 0)
                startInfo.Arguments = string.Join(" ", args);

            try
            {
                _process = System.Diagnostics.Process.Start(startInfo);
            }
            catch (Exception)
            {
                return false;
            }

            if (_process == null)
                return false;

            // 3. Register for runtime startup notification
            //    Pin the delegate so the GC doesn't collect it while dbgshim holds a pointer
            _startupCallback = new DbgShim.RuntimeStartupCallback(OnRuntimeStartup);

            var hr = DbgShim.RegisterForRuntimeStartup(
                _process.Id, _startupCallback, IntPtr.Zero, out _runtimeStartupToken);
            if (hr < 0)
            {
                LastError = $"RegisterForRuntimeStartup failed with HRESULT 0x{hr:X8}. dbgshim loaded from: {DbgShim.GetLoadedPath()}";
                return false;
            }

            // 4. Wait for the CLR to load and our callback to fire
            return await Task.Run(() =>
            {
                _attachedEvent.Wait(TimeSpan.FromSeconds(10));
                return _attached;
            });
        }

        private void OnRuntimeStartup(object pCordb, IntPtr parameter, int hresult)
        {
            // Called by dbgshim when CLR loads in the target process
            if (hresult < 0 || pCordb == null)
                return;

            try
            {
                _corDebug = (ICorDebug)pCordb;
                _corDebug.Initialize();
                _corDebug.SetManagedHandler(new ManagedCallbackHandler(this));

                _corDebug.DebugActiveProcess((uint)_process.Id, false, out _corDebugProcess);
                // Process is now paused (all managed threads frozen)

                _attached = true;
            }
            catch (Exception ex)
            {
                LastError = $"ICorDebug attach failed: {ex.Message}";
                // Attach failed — leave _attached = false
            }
            finally
            {
                _attachedEvent.Set();
            }
        }

        // =====================================================================
        // Process control
        // =====================================================================

        /// <summary>
        /// Resume execution of all managed threads.
        /// </summary>
        public void Continue()
        {
            _corDebugProcess?.Continue(false);
        }

        /// <summary>
        /// Pause all managed threads.
        /// </summary>
        public void Stop()
        {
            _corDebugProcess?.Stop(0);
        }

        /// <summary>
        /// Create a stepper on the active IL frame of the given thread.
        /// Returns null if no IL frame is available.
        /// </summary>
        public ICorDebugStepper CreateStepper(ICorDebugThread thread)
        {
            if (thread == null)
                return null;

            thread.GetActiveFrame(out var frame);
            if (frame is ICorDebugILFrame ilFrame)
            {
                ilFrame.CreateStepper(out var stepper);
                return stepper;
            }
            return null;
        }

        /// <summary>
        /// Terminate the debuggee process.
        /// </summary>
        public void Terminate()
        {
            try
            {
                if (_corDebugProcess != null)
                {
                    _corDebugProcess.Stop(0);
                    _corDebugProcess.Terminate(1);
                }
            }
            catch (Exception)
            {
                // Best-effort — process may already be dead
            }

            // Fallback: force-kill the OS process if CLR terminate didn't work
            try
            {
                if (_process != null && !_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception)
            {
                // Process may already be dead or access denied
            }
        }

        /// <summary>
        /// Detach ICorDebug from the process (lets it continue running undebugged).
        /// </summary>
        public void Detach()
        {
            try
            {
                if (_corDebugProcess != null)
                {
                    _corDebugProcess.Stop(0);
                    _corDebugProcess.Detach();
                    _corDebugProcess = null;
                }

                if (_corDebug != null)
                {
                    _corDebug.Terminate();
                    _corDebug = null;
                }
            }
            catch (Exception)
            {
                // Best-effort cleanup
            }

            _attached = false;
        }

        // =====================================================================
        // Dispose
        // =====================================================================

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            // Unregister the runtime startup callback
            if (_runtimeStartupToken != IntPtr.Zero)
            {
                try { DbgShim.UnregisterForRuntimeStartup(_runtimeStartupToken); }
                catch (Exception) { /* ignore */ }
                _runtimeStartupToken = IntPtr.Zero;
            }

            // Detach ICorDebug first so it releases its hold on the process
            Detach();

            _threads.Clear();
            _attachedEvent.Dispose();

            // Kill the entire process tree if still running
            // (entireProcessTree: true ensures child processes like game windows are also killed)
            try
            {
                if (_process != null && !_process.HasExited)
                    _process.Kill(entireProcessTree: true);
            }
            catch (Exception) { /* ignore */ }

            _process?.Dispose();
            _process = null;
        }

        // =====================================================================
        // Helpers for raising events (called from callback handler)
        // =====================================================================

        internal void RaiseBreakpointHit(int threadId, ICorDebugBreakpoint bp) =>
            BreakpointHit?.Invoke(this, new BreakpointHitEventArgs { ThreadId = threadId, BreakpointToken = bp });

        internal void RaiseStepCompleted(int threadId) =>
            StepCompleted?.Invoke(this, new StepCompletedEventArgs { ThreadId = threadId });

        internal void RaiseExceptionThrown(int threadId, bool unhandled, string exceptionType, string message) =>
            ExceptionThrown?.Invoke(this, new ExceptionThrownEventArgs { ThreadId = threadId, IsUnhandled = unhandled, ExceptionType = exceptionType, ExceptionMessage = message });

        internal void RaiseModuleLoaded(string name, string path, ICorDebugModule module) =>
            ModuleLoaded?.Invoke(this, new ModuleLoadedEventArgs { ModuleName = name, ModulePath = path, Module = module });

        internal void RaiseProcessExited(int exitCode) =>
            ProcessExited?.Invoke(this, new ProcessExitedEventArgs { ExitCode = exitCode });

        internal void RaiseThreadCreated(int threadId) =>
            ThreadCreated?.Invoke(this, new ThreadEventArgs { ThreadId = threadId });

        internal void RaiseThreadExited(int threadId) =>
            ThreadExited?.Invoke(this, new ThreadEventArgs { ThreadId = threadId });

        internal void TrackThread(int threadId, ICorDebugThread thread) =>
            _threads[threadId] = thread;

        internal void UntrackThread(int threadId) =>
            _threads.Remove(threadId);

        // =====================================================================
        // ManagedCallbackHandler — implements ICorDebugManagedCallback + Callback2
        // =====================================================================

        private class ManagedCallbackHandler : ICorDebugManagedCallback, ICorDebugManagedCallback2
        {
            private readonly NetDebugProcess _owner;

            public ManagedCallbackHandler(NetDebugProcess owner)
            {
                _owner = owner;
            }

            // Helper: get thread ID using raw vtable call (bypasses QI)
            // ICorDebugThread::GetID is slot 4 (IUnknown(3) + GetProcess(0) + GetID(1))
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            private delegate int RawGetIDDelegate(IntPtr self, out uint pdwThreadId);

            // ICorDebugAppDomain::GetProcess is slot 3 (IUnknown(3) + GetProcess(0))
            // But ICorDebugAppDomain extends ICorDebugController which has:
            // Stop(0), Continue(1), IsRunning(2), HasQueuedCallbacks(3), EnumerateThreads(4), GetID(5)
            // Then ICorDebugAppDomain adds: GetProcess(6)...
            // Actually ICorDebugAppDomain inherits ICorDebugController methods first.
            // ICorDebugController: Stop=0, Continue=1, IsRunning=2, HasQueuedCallbacks=3, EnumerateThreads=4, GetID=5
            // ICorDebugAppDomain: GetProcess=6, ...
            // So GetProcess is slot 3+6 = 9
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            private delegate int RawGetProcessDelegate(IntPtr self, out IntPtr ppProcess);

            // ICorDebugController::Continue is slot 3+1 = 4
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            private delegate int RawContinueDelegate(IntPtr self, int fIsOutOfBand);

            // ICorDebugController::Stop is slot 3+0 = 3
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            private delegate int RawStopDelegate(IntPtr self, uint dwTimeoutIgnored);

            private static int GetThreadId(ICorDebugThread thread)
            {
                if (thread == null) return 0;
                try
                {
                    var ptr = Marshal.GetIUnknownForObject(thread);
                    try
                    {
                        var vtable = Marshal.ReadIntPtr(ptr);
                        // ICorDebugThread::GetID is slot 3+1 = 4
                        var getIdSlot = Marshal.ReadIntPtr(vtable, 4 * IntPtr.Size);
                        var getId = Marshal.GetDelegateForFunctionPointer<RawGetIDDelegate>(getIdSlot);
                        getId(ptr, out uint id);
                        return (int)id;
                    }
                    finally { Marshal.Release(ptr); }
                }
                catch { return 0; }
            }

            private static void ContinueFromAppDomain(ICorDebugAppDomain pAppDomain)
            {
                if (pAppDomain == null) return;
                try
                {
                    var adPtr = Marshal.GetIUnknownForObject(pAppDomain);
                    try
                    {
                        var vtable = Marshal.ReadIntPtr(adPtr);
                        // ICorDebugAppDomain: Continue is inherited from ICorDebugController at slot 3+1=4
                        var continueSlot = Marshal.ReadIntPtr(vtable, 4 * IntPtr.Size);
                        var continueFn = Marshal.GetDelegateForFunctionPointer<RawContinueDelegate>(continueSlot);
                        continueFn(adPtr, 0);
                    }
                    finally { Marshal.Release(adPtr); }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[DAP] ContinueFromAppDomain error: {ex.Message}");
                }
            }

            private static void StopProcess(ICorDebugAppDomain pAppDomain)
            {
                if (pAppDomain == null) return;
                try
                {
                    var adPtr = Marshal.GetIUnknownForObject(pAppDomain);
                    try
                    {
                        var vtable = Marshal.ReadIntPtr(adPtr);
                        // ICorDebugAppDomain::GetProcess — slot 3+6=9 (after ICorDebugController's 6 methods + IUnknown's 3)
                        // Wait: ICorDebugController has Stop,Continue,IsRunning,HasQueuedCallbacks,EnumerateThreads,GetID = 6 methods
                        // ICorDebugAppDomain adds GetProcess as first method = index 6
                        // So GetProcess = slot 3+6 = 9
                        var getProcessSlot = Marshal.ReadIntPtr(vtable, 9 * IntPtr.Size);
                        var getProcess = Marshal.GetDelegateForFunctionPointer<RawGetProcessDelegate>(getProcessSlot);
                        int hr = getProcess(adPtr, out var processPtr);
                        if (hr >= 0 && processPtr != IntPtr.Zero)
                        {
                            var pVtable = Marshal.ReadIntPtr(processPtr);
                            // ICorDebugProcess inherits ICorDebugController: Stop = slot 3+0 = 3
                            var stopSlot = Marshal.ReadIntPtr(pVtable, 3 * IntPtr.Size);
                            var stopFn = Marshal.GetDelegateForFunctionPointer<RawStopDelegate>(stopSlot);
                            stopFn(processPtr, 0);
                            Marshal.Release(processPtr);
                        }
                    }
                    finally { Marshal.Release(adPtr); }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[DAP] StopProcess error: {ex.Message}");
                }
            }

            // Helper: get module name from ICorDebugModule
            private static string GetModuleName(ICorDebugModule module)
            {
                var nameBuffer = new char[1024];
                module.GetName((uint)nameBuffer.Length, out uint actualLen, nameBuffer);
                if (actualLen > 0)
                    return new string(nameBuffer, 0, (int)actualLen - 1); // trim null terminator
                return string.Empty;
            }

            // =================================================================
            // Exception info extraction
            // =================================================================

            /// <summary>
            /// Extract exception type name and message from the current exception on the thread.
            /// Uses ICorDebugThread.GetCurrentException() to get the exception object,
            /// then reads the type via IMetaDataImport and the _message field via field enumeration.
            /// Returns (typeName, message) — either may be null/empty on failure.
            /// </summary>
            private static (string typeName, string message) ExtractExceptionInfo(ICorDebugThread pThread)
            {
                string typeName = "Exception";
                string message = null;

                try
                {
                    if (pThread == null)
                        return (typeName, message);

                    // Get the exception object from the thread
                    int hr = pThread.GetCurrentException(out ICorDebugValue exValue);
                    if (hr < 0 || exValue == null)
                        return (typeName, message);

                    // The exception value is typically a reference — dereference it
                    ICorDebugValue actualValue = exValue;
                    if (exValue is ICorDebugReferenceValue refVal)
                    {
                        refVal.IsNull(out bool isNull);
                        if (isNull)
                            return (typeName, message);
                        hr = refVal.Dereference(out actualValue);
                        if (hr < 0 || actualValue == null)
                            return (typeName, message);
                    }

                    // Get the object value to access class info and fields
                    if (actualValue is not ICorDebugObjectValue objVal)
                        return (typeName, message);

                    // --- Extract type name ---
                    objVal.GetClass(out ICorDebugClass cls);
                    if (cls != null)
                    {
                        cls.GetModule(out ICorDebugModule module);
                        cls.GetToken(out uint typeDefToken);

                        if (module != null)
                        {
                            Guid imdImportGuid = new Guid("7DAC8207-D3AE-4C75-9B67-92801A497D44");
                            module.GetMetaDataInterface(ref imdImportGuid, out object mdObj);

                            if (mdObj is IMetaDataImport mdImport)
                            {
                                char[] nameBuffer = new char[1024];
                                hr = mdImport.GetTypeDefProps(
                                    typeDefToken,
                                    nameBuffer,
                                    (uint)nameBuffer.Length,
                                    out uint nameLen,
                                    out _,
                                    out _);

                                if (hr >= 0 && nameLen > 0)
                                {
                                    typeName = new string(nameBuffer, 0, (int)(nameLen - 1));
                                }

                                // --- Extract _message field ---
                                message = ReadExceptionMessageField(mdImport, objVal, cls, typeDefToken);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[DAP] ExtractExceptionInfo error: {ex.Message}");
                }

                return (typeName, message);
            }

            /// <summary>
            /// Read the _message field from an exception object by enumerating fields.
            /// Walks up the type hierarchy (via extends token) to find the field in base classes.
            /// </summary>
            private static string ReadExceptionMessageField(IMetaDataImport mdImport, ICorDebugObjectValue objVal, ICorDebugClass cls, uint typeDefToken)
            {
                // Try the current class and its base classes (Exception._message is defined on System.Exception)
                uint currentTypeDef = typeDefToken;

                for (int depth = 0; depth < 10; depth++) // max 10 levels of inheritance
                {
                    IntPtr hEnum = IntPtr.Zero;
                    try
                    {
                        uint[] fieldTokens = new uint[32];
                        while (true)
                        {
                            int hr = mdImport.EnumFields(ref hEnum, currentTypeDef, fieldTokens, (uint)fieldTokens.Length, out uint fetched);
                            if (hr < 0 || fetched == 0)
                                break;

                            for (uint i = 0; i < fetched; i++)
                            {
                                uint fieldToken = fieldTokens[i];
                                char[] fieldNameBuf = new char[256];
                                hr = mdImport.GetFieldProps(
                                    fieldToken,
                                    out _,
                                    fieldNameBuf,
                                    (uint)fieldNameBuf.Length,
                                    out uint fieldNameLen,
                                    out _,
                                    out _,
                                    out _,
                                    out _,
                                    out _,
                                    out _);

                                if (hr < 0 || fieldNameLen == 0)
                                    continue;

                                string fieldName = new string(fieldNameBuf, 0, (int)(fieldNameLen - 1));
                                if (fieldName == "_message")
                                {
                                    try
                                    {
                                        objVal.GetFieldValue(cls, fieldToken, out ICorDebugValue fieldValue);
                                        if (fieldValue == null)
                                            return null;

                                        // _message is a string reference — dereference and read
                                        if (fieldValue is ICorDebugReferenceValue msgRef)
                                        {
                                            msgRef.IsNull(out bool isNull);
                                            if (isNull)
                                                return null;
                                            msgRef.Dereference(out ICorDebugValue derefMsg);
                                            if (derefMsg is ICorDebugStringValue strVal)
                                            {
                                                strVal.GetLength(out uint strLen);
                                                char[] strBuf = new char[strLen + 1];
                                                strVal.GetString((uint)strBuf.Length, out _, strBuf);
                                                return new string(strBuf, 0, (int)strLen);
                                            }
                                        }
                                        else if (fieldValue is ICorDebugStringValue directStr)
                                        {
                                            directStr.GetLength(out uint strLen);
                                            char[] strBuf = new char[strLen + 1];
                                            directStr.GetString((uint)strBuf.Length, out _, strBuf);
                                            return new string(strBuf, 0, (int)strLen);
                                        }
                                    }
                                    catch
                                    {
                                        // Field read failed — continue searching
                                    }
                                }
                            }

                            if (fetched < (uint)fieldTokens.Length)
                                break;
                        }
                    }
                    finally
                    {
                        if (hEnum != IntPtr.Zero)
                        {
                            try { mdImport.CloseEnum(hEnum); } catch { }
                        }
                    }

                    // Walk up to the base class
                    char[] baseNameBuf = new char[1024];
                    int hr2 = mdImport.GetTypeDefProps(currentTypeDef, baseNameBuf, (uint)baseNameBuf.Length, out _, out _, out uint extendsToken);
                    if (hr2 < 0 || extendsToken == 0)
                        break;

                    // extendsToken may be a TypeDef or TypeRef — only follow TypeDefs (0x02xxxxxx)
                    if ((extendsToken & 0xFF000000) == 0x02000000)
                    {
                        currentTypeDef = extendsToken;
                    }
                    else
                    {
                        // TypeRef to another module — can't follow, stop here
                        break;
                    }
                }

                return null;
            }

            // IMetaDataImport — needed locally for field name reading in exception extraction.
            // The full interface is defined in VariableInspector.cs; we reference it via the COM Guid.
            // We use the same interface but cast the metadata object obtained from GetMetaDataInterface.

            // =================================================================
            // ICorDebugManagedCallback
            // =================================================================

            public int Breakpoint(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugBreakpoint pBreakpoint)
            {
                try
                {
                    StopProcess(pAppDomain);
                    int threadId = GetThreadId(pThread);
                    Console.Error.WriteLine($"[DAP] Breakpoint callback: threadId={threadId}");
                    _owner.RaiseBreakpointHit(threadId, pBreakpoint);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[DAP] Breakpoint callback error: {ex.Message}");
                }
                // Do NOT continue — the adapter will call Continue() when ready
                return 0;
            }

            public int StepComplete(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, IntPtr pStepper, CorDebugStepReason reason)
            {
                try
                {
                    Console.Error.WriteLine($"[DAP] StepComplete callback fired! reason={reason}");
                    StopProcess(pAppDomain);
                    int threadId = GetThreadId(pThread);
                    _owner.RaiseStepCompleted(threadId);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[DAP] StepComplete callback error: {ex.Message}");
                }
                // Do NOT continue — the adapter will call Continue() when ready
                return 0;
            }

            public int Break(ICorDebugAppDomain pAppDomain, ICorDebugThread thread)
            {
                try
                {
                    StopProcess(pAppDomain);
                    int threadId = GetThreadId(thread);
                    _owner.RaiseStepCompleted(threadId);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[DAP] Break callback error: {ex.Message}");
                }
                return 0;
            }

            public int Exception(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, bool unhandled)
            {
                // Callback1 Exception: always raise to the adapter and let it decide
                // whether to stop (based on exception filters) or continue.
                try
                {
                    StopProcess(pAppDomain);
                    int threadId = GetThreadId(pThread);
                    var (exType, exMsg) = ExtractExceptionInfo(pThread);
                    string label = unhandled ? "Unhandled" : "First-chance";
                    string description = !string.IsNullOrEmpty(exMsg)
                        ? $"{label} exception: {exType}: {exMsg}"
                        : $"{label} exception: {exType}";
                    Console.Error.WriteLine($"[DAP] Exception callback: threadId={threadId}, unhandled={unhandled}, type={exType}");
                    _owner.RaiseExceptionThrown(threadId, unhandled, exType, description);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[DAP] Exception callback error: {ex.Message}");
                }
                // Do NOT continue — the adapter will decide based on filters
                return 0;
            }

            public int EvalComplete(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugEval pEval)
            {
                ContinueFromAppDomain(pAppDomain);
                return 0;
            }

            public int EvalException(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugEval pEval)
            {
                ContinueFromAppDomain(pAppDomain);
                return 0;
            }

            public int CreateProcess(ICorDebugProcess pProcess)
            {
                pProcess?.Continue(false);
                return 0;
            }

            public int ExitProcess(ICorDebugProcess pProcess)
            {
                int exitCode = 0;
                try
                {
                    if (_owner._process != null && _owner._process.HasExited)
                        exitCode = _owner._process.ExitCode;
                }
                catch (Exception) { /* ignore */ }

                _owner._attached = false;
                _owner.RaiseProcessExited(exitCode);
                // Do NOT continue — the process is gone
                return 0;
            }

            public int CreateThread(ICorDebugAppDomain pAppDomain, ICorDebugThread thread)
            {
                int threadId = GetThreadId(thread);
                _owner.TrackThread(threadId, thread);
                _owner.RaiseThreadCreated(threadId);
                ContinueFromAppDomain(pAppDomain);
                return 0;
            }

            public int ExitThread(ICorDebugAppDomain pAppDomain, ICorDebugThread thread)
            {
                int threadId = GetThreadId(thread);
                _owner.UntrackThread(threadId);
                _owner.RaiseThreadExited(threadId);
                ContinueFromAppDomain(pAppDomain);
                return 0;
            }

            public int LoadModule(ICorDebugAppDomain pAppDomain, ICorDebugModule pModule)
            {
                string path = GetModuleName(pModule);
                string name = Path.GetFileName(path);
                _owner.RaiseModuleLoaded(name, path, pModule);
                ContinueFromAppDomain(pAppDomain);
                return 0;
            }

            public int UnloadModule(ICorDebugAppDomain pAppDomain, ICorDebugModule pModule)
            {
                ContinueFromAppDomain(pAppDomain);
                return 0;
            }

            public int LoadClass(ICorDebugAppDomain pAppDomain, ICorDebugClass c)
            {
                ContinueFromAppDomain(pAppDomain);
                return 0;
            }

            public int UnloadClass(ICorDebugAppDomain pAppDomain, ICorDebugClass c)
            {
                ContinueFromAppDomain(pAppDomain);
                return 0;
            }

            public int DebuggerError(ICorDebugProcess pProcess, int errorHR, uint errorCode)
            {
                pProcess?.Continue(false);
                return 0;
            }

            public int LogMessage(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, int lLevel, string pLogSwitchName, string pMessage)
            {
                ContinueFromAppDomain(pAppDomain);
                return 0;
            }

            public int LogSwitch(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, int lLevel, uint ulReason, string pLogSwitchName, string pParentName)
            {
                ContinueFromAppDomain(pAppDomain);
                return 0;
            }

            public int CreateAppDomain(ICorDebugProcess pProcess, ICorDebugAppDomain pAppDomain)
            {
                // Attach to the app domain so we get callbacks for it
                pAppDomain?.Attach();
                pProcess?.Continue(false);
                return 0;
            }

            public int ExitAppDomain(ICorDebugProcess pProcess, ICorDebugAppDomain pAppDomain)
            {
                pProcess?.Continue(false);
                return 0;
            }

            public int LoadAssembly(ICorDebugAppDomain pAppDomain, ICorDebugAssembly pAssembly)
            {
                ContinueFromAppDomain(pAppDomain);
                return 0;
            }

            public int UnloadAssembly(ICorDebugAppDomain pAppDomain, ICorDebugAssembly pAssembly)
            {
                ContinueFromAppDomain(pAppDomain);
                return 0;
            }

            public int ControlCTrap(ICorDebugProcess pProcess)
            {
                pProcess?.Continue(false);
                return 0;
            }

            public int NameChange(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread)
            {
                ContinueFromAppDomain(pAppDomain);
                return 0;
            }

            public int UpdateModuleSymbols(ICorDebugAppDomain pAppDomain, ICorDebugModule pModule, object pSymbolStream)
            {
                ContinueFromAppDomain(pAppDomain);
                return 0;
            }

            public int EditAndContinueRemap(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugFunction pFunction, bool fAccurate)
            {
                ContinueFromAppDomain(pAppDomain);
                return 0;
            }

            public int BreakpointSetError(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugBreakpoint pBreakpoint, uint dwError)
            {
                ContinueFromAppDomain(pAppDomain);
                return 0;
            }

            // =================================================================
            // ICorDebugManagedCallback2
            // =================================================================

            public int FunctionRemapOpportunity(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugFunction pOldFunction, ICorDebugFunction pNewFunction, uint oldILOffset)
            {
                ContinueFromAppDomain(pAppDomain);
                return 0;
            }

            public int CreateConnection(ICorDebugProcess pProcess, uint dwConnectionId, string pConnName)
            {
                pProcess?.Continue(false);
                return 0;
            }

            public int ChangeConnection(ICorDebugProcess pProcess, uint dwConnectionId)
            {
                pProcess?.Continue(false);
                return 0;
            }

            public int DestroyConnection(ICorDebugProcess pProcess, uint dwConnectionId)
            {
                pProcess?.Continue(false);
                return 0;
            }

            public int Exception(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugFrame pFrame, uint nOffset, CorDebugExceptionCallbackType dwEventType, uint dwFlags)
            {
                // Callback2 Exception: provides richer exception type classification.
                // DEBUG_EXCEPTION_FIRST_CHANCE = thrown but may be caught
                // DEBUG_EXCEPTION_USER_FIRST_CHANCE = first-chance in user code
                // DEBUG_EXCEPTION_CATCH_HANDLER_FOUND = a catch handler exists
                // DEBUG_EXCEPTION_UNHANDLED = no catch handler, will crash
                bool isUnhandled = dwEventType == CorDebugExceptionCallbackType.DEBUG_EXCEPTION_UNHANDLED;
                bool isFirstChance = dwEventType == CorDebugExceptionCallbackType.DEBUG_EXCEPTION_FIRST_CHANCE
                                  || dwEventType == CorDebugExceptionCallbackType.DEBUG_EXCEPTION_USER_FIRST_CHANCE;

                if (isUnhandled || isFirstChance)
                {
                    try
                    {
                        StopProcess(pAppDomain);
                        int threadId = GetThreadId(pThread);
                        var (exType, exMsg) = ExtractExceptionInfo(pThread);
                        string label = isUnhandled ? "Unhandled" : "First-chance";
                        string description = !string.IsNullOrEmpty(exMsg)
                            ? $"{label} exception: {exType}: {exMsg}"
                            : $"{label} exception: {exType}";
                        Console.Error.WriteLine($"[DAP] Exception2 callback: threadId={threadId}, type={dwEventType}, exType={exType}");
                        _owner.RaiseExceptionThrown(threadId, isUnhandled, exType, description);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[DAP] Exception2 callback error: {ex.Message}");
                    }
                    // Do NOT continue — let the adapter decide based on filters
                }
                else
                {
                    // CATCH_HANDLER_FOUND — a handler exists, continue execution
                    ContinueFromAppDomain(pAppDomain);
                }
                return 0;
            }

            public int FunctionRemapComplete(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugFunction pFunction)
            {
                ContinueFromAppDomain(pAppDomain);
                return 0;
            }

            public int MDANotification(ICorDebugController pController, ICorDebugThread pThread, ICorDebugMDA pMDA)
            {
                // ICorDebugController can be either process or app domain
                pController?.Continue(false);
                return 0;
            }
        }
    }
}
