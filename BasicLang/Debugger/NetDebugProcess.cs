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
                return false;

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
                return false;

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
            catch (Exception)
            {
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

            Detach();

            _threads.Clear();
            _attachedEvent.Dispose();

            // Kill the process if still running
            try
            {
                if (_process != null && !_process.HasExited)
                    _process.Kill();
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

        internal void RaiseExceptionThrown(int threadId, bool unhandled, string message) =>
            ExceptionThrown?.Invoke(this, new ExceptionThrownEventArgs { ThreadId = threadId, IsUnhandled = unhandled, ExceptionMessage = message });

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

            // Helper: get thread ID from an ICorDebugThread
            private static int GetThreadId(ICorDebugThread thread)
            {
                if (thread == null) return 0;
                thread.GetID(out uint id);
                return (int)id;
            }

            // Helper: get process from app domain and continue
            private static void ContinueFromAppDomain(ICorDebugAppDomain pAppDomain)
            {
                if (pAppDomain == null) return;
                pAppDomain.GetProcess(out var process);
                process?.Continue(false);
            }

            // Helper: get process from app domain
            private static ICorDebugProcess GetProcess(ICorDebugAppDomain pAppDomain)
            {
                if (pAppDomain == null) return null;
                pAppDomain.GetProcess(out var process);
                return process;
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
            // ICorDebugManagedCallback
            // =================================================================

            public int Breakpoint(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugBreakpoint pBreakpoint)
            {
                var process = GetProcess(pAppDomain);
                process?.Stop(0);

                int threadId = GetThreadId(pThread);
                _owner.RaiseBreakpointHit(threadId, pBreakpoint);
                // Do NOT continue — the adapter will call Continue() when ready
                return 0; // S_OK
            }

            public int StepComplete(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugStepper pStepper, CorDebugStepReason reason)
            {
                var process = GetProcess(pAppDomain);
                process?.Stop(0);

                int threadId = GetThreadId(pThread);
                _owner.RaiseStepCompleted(threadId);
                // Do NOT continue — the adapter will call Continue() when ready
                return 0;
            }

            public int Break(ICorDebugAppDomain pAppDomain, ICorDebugThread thread)
            {
                var process = GetProcess(pAppDomain);
                process?.Stop(0);
                // Treat debugger break like a step complete
                int threadId = GetThreadId(thread);
                _owner.RaiseStepCompleted(threadId);
                return 0;
            }

            public int Exception(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, bool unhandled)
            {
                if (unhandled)
                {
                    var process = GetProcess(pAppDomain);
                    process?.Stop(0);

                    int threadId = GetThreadId(pThread);
                    _owner.RaiseExceptionThrown(threadId, true, "Unhandled exception");
                    // Do NOT continue — the adapter will decide
                }
                else
                {
                    // First-chance exception — continue
                    ContinueFromAppDomain(pAppDomain);
                }
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
                if (dwEventType == CorDebugExceptionCallbackType.DEBUG_EXCEPTION_UNHANDLED)
                {
                    var process = GetProcess(pAppDomain);
                    process?.Stop(0);

                    int threadId = GetThreadId(pThread);
                    _owner.RaiseExceptionThrown(threadId, true, "Unhandled exception (callback2)");
                    // Do NOT continue — let the adapter decide
                }
                else
                {
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
