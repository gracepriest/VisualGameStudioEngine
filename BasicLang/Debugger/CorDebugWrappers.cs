using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BasicLang.Debugger
{
    // =========================================================================
    // Enums and Structs
    // =========================================================================

    [StructLayout(LayoutKind.Sequential)]
    public struct COR_DEBUG_STEP_RANGE
    {
        public uint startOffset;
        public uint endOffset;
    }

    public enum CorDebugStepReason
    {
        STEP_NORMAL = 0,
        STEP_RETURN = 1,
        STEP_CALL = 2,
        STEP_EXCEPTION_FILTER = 3,
        STEP_EXCEPTION_HANDLER = 4,
        STEP_INTERCEPT = 5,
        STEP_EXIT = 6
    }

    public enum CorElementType : byte
    {
        ELEMENT_TYPE_END = 0x00,
        ELEMENT_TYPE_VOID = 0x01,
        ELEMENT_TYPE_BOOLEAN = 0x02,
        ELEMENT_TYPE_CHAR = 0x03,
        ELEMENT_TYPE_I1 = 0x04,
        ELEMENT_TYPE_U1 = 0x05,
        ELEMENT_TYPE_I2 = 0x06,
        ELEMENT_TYPE_U2 = 0x07,
        ELEMENT_TYPE_I4 = 0x08,
        ELEMENT_TYPE_U4 = 0x09,
        ELEMENT_TYPE_I8 = 0x0a,
        ELEMENT_TYPE_U8 = 0x0b,
        ELEMENT_TYPE_R4 = 0x0c,
        ELEMENT_TYPE_R8 = 0x0d,
        ELEMENT_TYPE_STRING = 0x0e,
        ELEMENT_TYPE_PTR = 0x0f,
        ELEMENT_TYPE_BYREF = 0x10,
        ELEMENT_TYPE_VALUETYPE = 0x11,
        ELEMENT_TYPE_CLASS = 0x12,
        ELEMENT_TYPE_VAR = 0x13,
        ELEMENT_TYPE_ARRAY = 0x14,
        ELEMENT_TYPE_GENERICINST = 0x15,
        ELEMENT_TYPE_TYPEDBYREF = 0x16,
        ELEMENT_TYPE_I = 0x18,
        ELEMENT_TYPE_U = 0x19,
        ELEMENT_TYPE_FNPTR = 0x1b,
        ELEMENT_TYPE_OBJECT = 0x1c,
        ELEMENT_TYPE_SZARRAY = 0x1d,
        ELEMENT_TYPE_MVAR = 0x1e,
        ELEMENT_TYPE_CMOD_REQD = 0x1f,
        ELEMENT_TYPE_CMOD_OPT = 0x20,
        ELEMENT_TYPE_INTERNAL = 0x21,
        ELEMENT_TYPE_MAX = 0x22,
        ELEMENT_TYPE_MODIFIER = 0x40,
        ELEMENT_TYPE_SENTINEL = 0x41,
        ELEMENT_TYPE_PINNED = 0x45
    }

    public enum CorDebugExceptionCallbackType
    {
        DEBUG_EXCEPTION_FIRST_CHANCE = 1,
        DEBUG_EXCEPTION_USER_FIRST_CHANCE = 2,
        DEBUG_EXCEPTION_CATCH_HANDLER_FOUND = 3,
        DEBUG_EXCEPTION_UNHANDLED = 4
    }

    public enum CorDebugIntercept
    {
        INTERCEPT_NONE = 0x0,
        INTERCEPT_CLASS_INIT = 0x01,
        INTERCEPT_EXCEPTION_FILTER = 0x02,
        INTERCEPT_SECURITY = 0x04,
        INTERCEPT_CONTEXT_POLICY = 0x08,
        INTERCEPT_INTERCEPTION = 0x10,
        INTERCEPT_ALL = 0xffff
    }

    public enum CorDebugUnmappedStop
    {
        STOP_NONE = 0x0,
        STOP_PROLOG = 0x01,
        STOP_EPILOG = 0x02,
        STOP_NO_MAPPING_INFO = 0x04,
        STOP_OTHER_UNMAPPED = 0x08,
        STOP_UNMANAGED = 0x10,
        STOP_ALL = 0xffff
    }

    // =========================================================================
    // ICorDebugController — base interface for ICorDebugProcess and ICorDebugAppDomain
    // Note: ICorDebugProcess GUID is reused here as the controller GUID per the IDL.
    // =========================================================================

    [ComImport]
    [Guid("3D6F5F62-7538-11D3-8D5B-00104B35E7EF")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICorDebugController
    {
        // vtable slot 0
        [PreserveSig]
        int Stop(uint dwTimeoutIgnored);

        // vtable slot 1
        [PreserveSig]
        int Continue([MarshalAs(UnmanagedType.Bool)] bool fIsOutOfBand);

        // vtable slot 2
        [PreserveSig]
        int IsRunning([MarshalAs(UnmanagedType.Bool)] out bool pbRunning);

        // vtable slot 3
        [PreserveSig]
        int HasQueuedCallbacks(
            [MarshalAs(UnmanagedType.Interface)] ICorDebugThread pThread,
            [MarshalAs(UnmanagedType.Bool)] out bool pbQueued);

        // vtable slot 4
        [PreserveSig]
        int EnumerateThreads(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugThreadEnum ppThreads);

        // vtable slot 5
        [PreserveSig]
        int SetAllThreadsDebugState(
            CorDebugThreadState state,
            [MarshalAs(UnmanagedType.Interface)] ICorDebugThread pExceptThisThread);

        // vtable slot 6
        [PreserveSig]
        int Detach();

        // vtable slot 7
        [PreserveSig]
        int Terminate(uint exitCode);

        // vtable slot 8
        [PreserveSig]
        int CanCommitChanges(
            uint cSnapshots,
            [MarshalAs(UnmanagedType.Interface)] ref ICorDebugEditAndContinueSnapshot pSnapshots,
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugErrorInfoEnum pError);

        // vtable slot 9
        [PreserveSig]
        int CommitChanges(
            uint cSnapshots,
            [MarshalAs(UnmanagedType.Interface)] ref ICorDebugEditAndContinueSnapshot pSnapshots,
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugErrorInfoEnum pError);
    }

    // =========================================================================
    // ICorDebug
    // =========================================================================

    [ComImport]
    [Guid("3D6F5F61-7538-11D3-8D5B-00104B35E7EF")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICorDebug
    {
        // vtable slot 0
        [PreserveSig]
        int Initialize();

        // vtable slot 1
        [PreserveSig]
        int Terminate();

        // vtable slot 2
        [PreserveSig]
        int SetManagedHandler(
            [MarshalAs(UnmanagedType.Interface)] ICorDebugManagedCallback pCallback);

        // vtable slot 3
        [PreserveSig]
        int SetUnmanagedHandler(
            [MarshalAs(UnmanagedType.Interface)] ICorDebugUnmanagedCallback pCallback);

        // vtable slot 4
        [PreserveSig]
        int CreateProcess(
            [MarshalAs(UnmanagedType.LPWStr)] string lpApplicationName,
            [MarshalAs(UnmanagedType.LPWStr)] string lpCommandLine,
            IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            [MarshalAs(UnmanagedType.LPWStr)] string lpCurrentDirectory,
            IntPtr lpStartupInfo,
            IntPtr lpProcessInformation,
            CorDebugCreateProcessFlags debuggingFlags,
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugProcess ppProcess);

        // vtable slot 5
        [PreserveSig]
        int DebugActiveProcess(
            uint id,
            [MarshalAs(UnmanagedType.Bool)] bool win32Attach,
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugProcess ppProcess);

        // vtable slot 6
        [PreserveSig]
        int EnumerateProcesses(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugProcessEnum ppProcess);

        // vtable slot 7
        [PreserveSig]
        int GetProcess(
            uint dwProcessId,
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugProcess ppProcess);

        // vtable slot 8
        [PreserveSig]
        int CanLaunchOrAttach(
            uint dwProcessId,
            [MarshalAs(UnmanagedType.Bool)] bool win32DebuggingEnabled);
    }

    // =========================================================================
    // ICorDebugProcess (extends ICorDebugController)
    // =========================================================================

    [ComImport]
    [Guid("3D6F5F64-7538-11D3-8D5B-00104B35E7EF")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICorDebugProcess
    {
        // --- ICorDebugController methods (vtable slots 0-9) ---

        [PreserveSig]
        int Stop(uint dwTimeoutIgnored);

        [PreserveSig]
        int Continue([MarshalAs(UnmanagedType.Bool)] bool fIsOutOfBand);

        [PreserveSig]
        int IsRunning([MarshalAs(UnmanagedType.Bool)] out bool pbRunning);

        [PreserveSig]
        int HasQueuedCallbacks(
            [MarshalAs(UnmanagedType.Interface)] ICorDebugThread pThread,
            [MarshalAs(UnmanagedType.Bool)] out bool pbQueued);

        [PreserveSig]
        int EnumerateThreads(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugThreadEnum ppThreads);

        [PreserveSig]
        int SetAllThreadsDebugState(
            CorDebugThreadState state,
            [MarshalAs(UnmanagedType.Interface)] ICorDebugThread pExceptThisThread);

        [PreserveSig]
        int Detach();

        [PreserveSig]
        int Terminate(uint exitCode);

        [PreserveSig]
        int CanCommitChanges(
            uint cSnapshots,
            [MarshalAs(UnmanagedType.Interface)] ref ICorDebugEditAndContinueSnapshot pSnapshots,
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugErrorInfoEnum pError);

        [PreserveSig]
        int CommitChanges(
            uint cSnapshots,
            [MarshalAs(UnmanagedType.Interface)] ref ICorDebugEditAndContinueSnapshot pSnapshots,
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugErrorInfoEnum pError);

        // --- ICorDebugProcess-specific methods (vtable slots 10+) ---

        // vtable slot 10
        [PreserveSig]
        int GetID(out uint pdwProcessId);

        // vtable slot 11
        [PreserveSig]
        int GetHandle(out IntPtr phProcessHandle);

        // vtable slot 12
        [PreserveSig]
        int GetThread(
            uint dwThreadId,
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugThread ppThread);

        // vtable slot 13
        [PreserveSig]
        int EnumerateObjects(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugObjectEnum ppObjects);

        // vtable slot 14
        [PreserveSig]
        int IsTransitionStub(
            ulong address,
            [MarshalAs(UnmanagedType.Bool)] out bool pbTransitionStub);

        // vtable slot 15
        [PreserveSig]
        int IsOSSuspended(
            uint threadID,
            [MarshalAs(UnmanagedType.Bool)] out bool pbSuspended);

        // vtable slot 16
        [PreserveSig]
        int GetThreadContext(
            uint threadID,
            uint contextSize,
            IntPtr context);

        // vtable slot 17
        [PreserveSig]
        int SetThreadContext(
            uint threadID,
            uint contextSize,
            IntPtr context);

        // vtable slot 18
        [PreserveSig]
        int ReadMemory(
            ulong address,
            uint size,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] byte[] buffer,
            out uint read);

        // vtable slot 19
        [PreserveSig]
        int WriteMemory(
            ulong address,
            uint size,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] byte[] buffer,
            out uint written);

        // vtable slot 20
        [PreserveSig]
        int ClearCurrentException(uint threadID);

        // vtable slot 21
        [PreserveSig]
        int EnableLogMessages([MarshalAs(UnmanagedType.Bool)] bool fOnOff);

        // vtable slot 22
        [PreserveSig]
        int ModifyLogSwitch(
            [MarshalAs(UnmanagedType.LPWStr)] string pLogSwitchName,
            int lLevel);

        // vtable slot 23
        [PreserveSig]
        int EnumerateAppDomains(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugAppDomainEnum ppAppDomains);

        // vtable slot 24
        [PreserveSig]
        int GetObject(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugValue ppObject);

        // vtable slot 25
        [PreserveSig]
        int ThreadForFiberCookie(
            uint fiberCookie,
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugThread ppThread);

        // vtable slot 26
        [PreserveSig]
        int GetHelperThreadID(out uint pThreadID);
    }

    // =========================================================================
    // ICorDebugAppDomain (extends ICorDebugController)
    // =========================================================================

    [ComImport]
    [Guid("3D6F5F63-7538-11D3-8D5B-00104B35E7EF")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICorDebugAppDomain
    {
        // --- ICorDebugController methods (vtable slots 0-9) ---

        [PreserveSig]
        int Stop(uint dwTimeoutIgnored);

        [PreserveSig]
        int Continue([MarshalAs(UnmanagedType.Bool)] bool fIsOutOfBand);

        [PreserveSig]
        int IsRunning([MarshalAs(UnmanagedType.Bool)] out bool pbRunning);

        [PreserveSig]
        int HasQueuedCallbacks(
            [MarshalAs(UnmanagedType.Interface)] ICorDebugThread pThread,
            [MarshalAs(UnmanagedType.Bool)] out bool pbQueued);

        [PreserveSig]
        int EnumerateThreads(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugThreadEnum ppThreads);

        [PreserveSig]
        int SetAllThreadsDebugState(
            CorDebugThreadState state,
            [MarshalAs(UnmanagedType.Interface)] ICorDebugThread pExceptThisThread);

        [PreserveSig]
        int Detach();

        [PreserveSig]
        int Terminate(uint exitCode);

        [PreserveSig]
        int CanCommitChanges(
            uint cSnapshots,
            [MarshalAs(UnmanagedType.Interface)] ref ICorDebugEditAndContinueSnapshot pSnapshots,
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugErrorInfoEnum pError);

        [PreserveSig]
        int CommitChanges(
            uint cSnapshots,
            [MarshalAs(UnmanagedType.Interface)] ref ICorDebugEditAndContinueSnapshot pSnapshots,
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugErrorInfoEnum pError);

        // --- ICorDebugAppDomain-specific methods (vtable slots 10+) ---

        // vtable slot 10
        [PreserveSig]
        int GetProcess(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugProcess ppProcess);

        // vtable slot 11
        [PreserveSig]
        int EnumerateAssemblies(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugAssemblyEnum ppAssemblies);

        // vtable slot 12
        [PreserveSig]
        int GetModuleFromMetaDataInterface(
            [MarshalAs(UnmanagedType.IUnknown)] object pIMetaData,
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugModule ppModule);

        // vtable slot 13
        [PreserveSig]
        int EnumerateBreakpoints(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugBreakpointEnum ppBreakpoints);

        // vtable slot 14
        [PreserveSig]
        int EnumerateSteppers(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugStepperEnum ppSteppers);

        // vtable slot 15
        [PreserveSig]
        int IsAttached([MarshalAs(UnmanagedType.Bool)] out bool pbAttached);

        // vtable slot 16
        [PreserveSig]
        int GetName(
            uint cchName,
            out uint pcchName,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] char[] szName);

        // vtable slot 17
        [PreserveSig]
        int GetObject(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugValue ppObject);

        // vtable slot 18
        [PreserveSig]
        int Attach();

        // vtable slot 19
        [PreserveSig]
        int GetID(out uint pId);
    }

    // =========================================================================
    // ICorDebugThread
    // =========================================================================

    [ComImport]
    [Guid("938C6D66-7FB6-4F69-B389-425B8987329B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICorDebugThread
    {
        // vtable slot 0
        [PreserveSig]
        int GetProcess(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugProcess ppProcess);

        // vtable slot 1
        [PreserveSig]
        int GetID(out uint pdwThreadId);

        // vtable slot 2
        [PreserveSig]
        int GetHandle(out IntPtr phThreadHandle);

        // vtable slot 3
        [PreserveSig]
        int GetAppDomain(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugAppDomain ppAppDomain);

        // vtable slot 4
        [PreserveSig]
        int SetDebugState(CorDebugThreadState state);

        // vtable slot 5
        [PreserveSig]
        int GetDebugState(out CorDebugThreadState pState);

        // vtable slot 6
        [PreserveSig]
        int GetUserState(out CorDebugUserState pState);

        // vtable slot 7
        [PreserveSig]
        int GetCurrentException(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugValue ppExceptionObject);

        // vtable slot 8
        [PreserveSig]
        int ClearCurrentException();

        // vtable slot 9
        [PreserveSig]
        int CreateStepper(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugStepper ppStepper);

        // vtable slot 10
        [PreserveSig]
        int EnumerateChains(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugChainEnum ppChains);

        // vtable slot 11
        [PreserveSig]
        int GetActiveChain(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugChain ppChain);

        // vtable slot 12
        [PreserveSig]
        int GetActiveFrame(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugFrame ppFrame);

        // vtable slot 13
        [PreserveSig]
        int GetRegisterSet(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugRegisterSet ppRegisters);

        // vtable slot 14
        [PreserveSig]
        int CreateEval(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugEval ppEval);

        // vtable slot 15
        [PreserveSig]
        int GetObject(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugValue ppObject);
    }

    // =========================================================================
    // ICorDebugModule
    // =========================================================================

    [ComImport]
    [Guid("DBA2D8C1-E5C5-4069-8C13-10A7C6ABF43D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICorDebugModule
    {
        // vtable slot 0
        [PreserveSig]
        int GetProcess(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugProcess ppProcess);

        // vtable slot 1
        [PreserveSig]
        int GetBaseAddress(out ulong pAddress);

        // vtable slot 2
        [PreserveSig]
        int GetAssembly(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugAssembly ppAssembly);

        // vtable slot 3
        [PreserveSig]
        int GetName(
            uint cchName,
            out uint pcchName,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] char[] szName);

        // vtable slot 4
        [PreserveSig]
        int EnableJITDebugging(
            [MarshalAs(UnmanagedType.Bool)] bool bTrackJITInfo,
            [MarshalAs(UnmanagedType.Bool)] bool bAllowJitOpts);

        // vtable slot 5
        [PreserveSig]
        int EnableClassLoadCallbacks([MarshalAs(UnmanagedType.Bool)] bool bClassLoadCallbacks);

        // vtable slot 6
        [PreserveSig]
        int GetFunctionFromToken(
            uint methodDef,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppFunction);

        // vtable slot 7
        [PreserveSig]
        int GetFunctionFromRVA(
            ulong rva,
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugFunction ppFunction);

        // vtable slot 8
        [PreserveSig]
        int GetClassFromToken(
            uint typeDef,
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugClass ppClass);

        // vtable slot 9
        [PreserveSig]
        int CreateBreakpoint(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugModuleBreakpoint ppBreakpoint);

        // vtable slot 10
        [PreserveSig]
        int GetEditAndContinueSnapshot(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugEditAndContinueSnapshot ppEditAndContinueSnapshot);

        // vtable slot 11
        [PreserveSig]
        int GetMetaDataInterface(
            ref Guid riid,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppObj);

        // vtable slot 12
        [PreserveSig]
        int GetToken(out uint pToken);

        // vtable slot 13
        [PreserveSig]
        int IsDynamic([MarshalAs(UnmanagedType.Bool)] out bool pDynamic);

        // vtable slot 14
        [PreserveSig]
        int GetGlobalVariableValue(
            uint fieldDef,
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugValue ppValue);

        // vtable slot 15
        [PreserveSig]
        int GetSize(out uint pcBytes);

        // vtable slot 16
        [PreserveSig]
        int IsInMemory([MarshalAs(UnmanagedType.Bool)] out bool pInMemory);
    }

    // =========================================================================
    // ICorDebugFunction
    // =========================================================================

    [ComImport]
    [Guid("CC7BCAF6-8A68-11D2-983C-0000F808342D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICorDebugFunction
    {
        // vtable slot 0
        [PreserveSig]
        int GetModule(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugModule ppModule);

        // vtable slot 1
        [PreserveSig]
        int GetClass(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugClass ppClass);

        // vtable slot 2
        [PreserveSig]
        int GetToken(out uint pMethodDef);

        // vtable slot 3
        [PreserveSig]
        int GetILCode(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugCode ppCode);

        // vtable slot 4
        [PreserveSig]
        int GetNativeCode(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugCode ppCode);

        // vtable slot 5
        [PreserveSig]
        int CreateBreakpoint(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugFunctionBreakpoint ppBreakpoint);

        // vtable slot 6
        [PreserveSig]
        int GetLocalVarSigToken(out uint pmdSig);

        // vtable slot 7
        [PreserveSig]
        int GetCurrentVersionNumber(out uint pnCurrentVersion);
    }

    // =========================================================================
    // ICorDebugCode
    // =========================================================================

    [ComImport]
    [Guid("CC7BCAF4-8A68-11D2-983C-0000F808342D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICorDebugCode
    {
        // vtable slot 0
        [PreserveSig]
        int IsIL([MarshalAs(UnmanagedType.Bool)] out bool pbIL);

        // vtable slot 1
        [PreserveSig]
        int GetFunction(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugFunction ppFunction);

        // vtable slot 2
        [PreserveSig]
        int GetAddress(out ulong pStart);

        // vtable slot 3
        [PreserveSig]
        int GetSize(out uint pcBytes);

        // vtable slot 4
        [PreserveSig]
        int CreateBreakpoint(
            uint offset,
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugFunctionBreakpoint ppBreakpoint);

        // vtable slot 5
        [PreserveSig]
        int GetCode(
            uint startOffset,
            uint endOffset,
            uint cBufferAlloc,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] byte[] buffer,
            out uint pcBufferSize);

        // vtable slot 6
        [PreserveSig]
        int GetVersionNumber(out uint nVersion);

        // vtable slot 7
        [PreserveSig]
        int GetILToNativeMapping(
            uint cMap,
            out uint pcMap,
            IntPtr map); // TODO: COR_IL_MAP[]

        // vtable slot 8
        [PreserveSig]
        int GetEnCRemapSequencePoints(
            uint cMap,
            out uint pcMap,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] uint[] offsets);
    }

    // =========================================================================
    // ICorDebugFrame
    // =========================================================================

    [ComImport]
    [Guid("CC7BCAEF-8A68-11D2-983C-0000F808342D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICorDebugFrame
    {
        // vtable slot 0
        [PreserveSig]
        int GetChain(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugChain ppChain);

        // vtable slot 1
        [PreserveSig]
        int GetCode(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugCode ppCode);

        // vtable slot 2
        [PreserveSig]
        int GetFunction(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugFunction ppFunction);

        // vtable slot 3
        [PreserveSig]
        int GetFunctionToken(out uint pToken);

        // vtable slot 4
        [PreserveSig]
        int GetStackRange(
            out ulong pStart,
            out ulong pEnd);

        // vtable slot 5
        [PreserveSig]
        int GetCaller(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugFrame ppFrame);

        // vtable slot 6
        [PreserveSig]
        int GetCallee(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugFrame ppFrame);

        // vtable slot 7
        [PreserveSig]
        int CreateStepper(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugStepper ppStepper);
    }

    // =========================================================================
    // ICorDebugILFrame
    // =========================================================================

    [ComImport]
    [Guid("03E26311-4F76-11D3-88C6-006097945418")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICorDebugILFrame
    {
        // --- ICorDebugFrame methods (vtable slots 0-7) ---

        [PreserveSig]
        int GetChain(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugChain ppChain);

        [PreserveSig]
        int GetCode(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugCode ppCode);

        [PreserveSig]
        int GetFunction(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugFunction ppFunction);

        [PreserveSig]
        int GetFunctionToken(out uint pToken);

        [PreserveSig]
        int GetStackRange(
            out ulong pStart,
            out ulong pEnd);

        [PreserveSig]
        int GetCaller(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugFrame ppFrame);

        [PreserveSig]
        int GetCallee(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugFrame ppFrame);

        [PreserveSig]
        int CreateStepper(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugStepper ppStepper);

        // --- ICorDebugILFrame-specific methods (vtable slots 8+) ---

        // vtable slot 8
        [PreserveSig]
        int GetIP(
            out uint pnOffset,
            out CorDebugMappingResult pMappingResult);

        // vtable slot 9
        [PreserveSig]
        int SetIP(uint nOffset);

        // vtable slot 10
        [PreserveSig]
        int EnumerateLocalVariables(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugValueEnum ppValueEnum);

        // vtable slot 11
        [PreserveSig]
        int GetLocalVariable(
            uint dwIndex,
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugValue ppValue);

        // vtable slot 12
        [PreserveSig]
        int EnumerateArguments(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugValueEnum ppValueEnum);

        // vtable slot 13
        [PreserveSig]
        int GetArgument(
            uint dwIndex,
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugValue ppValue);

        // vtable slot 14
        [PreserveSig]
        int GetStackDepth(out uint pDepth);

        // vtable slot 15
        [PreserveSig]
        int GetStackValue(
            uint dwIndex,
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugValue ppValue);

        // vtable slot 16
        [PreserveSig]
        int CanSetIP(uint nOffset);
    }

    // =========================================================================
    // ICorDebugValue
    // =========================================================================

    [ComImport]
    [Guid("CC7BCAEC-8A68-11D2-983C-0000F808342D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICorDebugValue
    {
        // vtable slot 0
        [PreserveSig]
        int GetType(out CorElementType pType);

        // vtable slot 1
        [PreserveSig]
        int GetSize(out uint pSize);

        // vtable slot 2
        [PreserveSig]
        int GetAddress(out ulong pAddress);

        // vtable slot 3
        [PreserveSig]
        int CreateBreakpoint(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugValueBreakpoint ppBreakpoint);
    }

    // =========================================================================
    // ICorDebugGenericValue
    // =========================================================================

    [ComImport]
    [Guid("CC7BCAF8-8A68-11D2-983C-0000F808342D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICorDebugGenericValue
    {
        // --- ICorDebugValue methods (vtable slots 0-3) ---

        [PreserveSig]
        int GetType(out CorElementType pType);

        [PreserveSig]
        int GetSize(out uint pSize);

        [PreserveSig]
        int GetAddress(out ulong pAddress);

        [PreserveSig]
        int CreateBreakpoint(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugValueBreakpoint ppBreakpoint);

        // --- ICorDebugGenericValue-specific methods (vtable slots 4+) ---

        // vtable slot 4
        [PreserveSig]
        int GetValue(IntPtr pTo);

        // vtable slot 5
        [PreserveSig]
        int SetValue(IntPtr pFrom);
    }

    // =========================================================================
    // ICorDebugStringValue
    // =========================================================================

    [ComImport]
    [Guid("CC7BCAFD-8A68-11D2-983C-0000F808342D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICorDebugStringValue
    {
        // --- ICorDebugValue methods (vtable slots 0-3) ---

        [PreserveSig]
        int GetType(out CorElementType pType);

        [PreserveSig]
        int GetSize(out uint pSize);

        [PreserveSig]
        int GetAddress(out ulong pAddress);

        [PreserveSig]
        int CreateBreakpoint(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugValueBreakpoint ppBreakpoint);

        // --- ICorDebugHeapValue methods (vtable slots 4-5) ---

        // vtable slot 4 — IsValid (ICorDebugHeapValue)
        [PreserveSig]
        int IsValid([MarshalAs(UnmanagedType.Bool)] out bool pbValid);

        // vtable slot 5 — CreateRelocBreakpoint (ICorDebugHeapValue)
        [PreserveSig]
        int CreateRelocBreakpoint(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugValueBreakpoint ppBreakpoint);

        // --- ICorDebugStringValue-specific methods (vtable slots 6+) ---

        // vtable slot 6
        [PreserveSig]
        int GetLength(out uint pcchString);

        // vtable slot 7
        [PreserveSig]
        int GetString(
            uint cchString,
            out uint pcchString,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] char[] szString);
    }

    // =========================================================================
    // ICorDebugObjectValue
    // =========================================================================

    [ComImport]
    [Guid("18AD3D6E-B7D2-11D2-BD04-0000F80849BD")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICorDebugObjectValue
    {
        // --- ICorDebugValue methods (vtable slots 0-3) ---

        [PreserveSig]
        int GetType(out CorElementType pType);

        [PreserveSig]
        int GetSize(out uint pSize);

        [PreserveSig]
        int GetAddress(out ulong pAddress);

        [PreserveSig]
        int CreateBreakpoint(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugValueBreakpoint ppBreakpoint);

        // --- ICorDebugObjectValue-specific methods (vtable slots 4+) ---

        // vtable slot 4
        [PreserveSig]
        int GetClass(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugClass ppClass);

        // vtable slot 5
        [PreserveSig]
        int GetFieldValue(
            [MarshalAs(UnmanagedType.Interface)] ICorDebugClass pClass,
            uint fieldDef,
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugValue ppValue);

        // vtable slot 6
        [PreserveSig]
        int GetVirtualMethod(
            uint memberRef,
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugFunction ppFunction);

        // vtable slot 7
        [PreserveSig]
        int GetContext(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugContext ppContext);

        // vtable slot 8
        [PreserveSig]
        int IsValueClass([MarshalAs(UnmanagedType.Bool)] out bool pbIsValueClass);

        // vtable slot 9
        [PreserveSig]
        int GetManagedCopy(
            [MarshalAs(UnmanagedType.IUnknown)] out object ppObject);

        // vtable slot 10
        [PreserveSig]
        int SetFromManagedCopy(
            [MarshalAs(UnmanagedType.IUnknown)] object pObject);
    }

    // =========================================================================
    // ICorDebugArrayValue
    // =========================================================================

    [ComImport]
    [Guid("0405B0DF-A660-11D2-BD02-0000F80849BD")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICorDebugArrayValue
    {
        // --- ICorDebugValue methods (vtable slots 0-3) ---

        [PreserveSig]
        int GetType(out CorElementType pType);

        [PreserveSig]
        int GetSize(out uint pSize);

        [PreserveSig]
        int GetAddress(out ulong pAddress);

        [PreserveSig]
        int CreateBreakpoint(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugValueBreakpoint ppBreakpoint);

        // --- ICorDebugHeapValue methods (vtable slots 4-5) ---

        // vtable slot 4 — IsValid (ICorDebugHeapValue)
        [PreserveSig]
        int IsValid([MarshalAs(UnmanagedType.Bool)] out bool pbValid);

        // vtable slot 5 — CreateRelocBreakpoint (ICorDebugHeapValue)
        [PreserveSig]
        int CreateRelocBreakpoint(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugValueBreakpoint ppBreakpoint);

        // --- ICorDebugArrayValue-specific methods (vtable slots 6+) ---

        // vtable slot 6
        [PreserveSig]
        int GetElementType(out CorElementType pType);

        // vtable slot 7
        [PreserveSig]
        int GetRank(out uint pnRank);

        // vtable slot 8
        [PreserveSig]
        int GetCount(out uint pnCount);

        // vtable slot 9
        [PreserveSig]
        int GetDimensions(
            uint cdim,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] uint[] dims);

        // vtable slot 10
        [PreserveSig]
        int HasBaseIndicies([MarshalAs(UnmanagedType.Bool)] out bool pbHasBaseIndicies);

        // vtable slot 11
        [PreserveSig]
        int GetBaseIndicies(
            uint cdim,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] uint[] indicies);

        // vtable slot 12
        [PreserveSig]
        int GetElement(
            uint cdim,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] uint[] indices,
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugValue ppValue);

        // vtable slot 13
        [PreserveSig]
        int GetElementAtPosition(
            uint nPosition,
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugValue ppValue);
    }

    // =========================================================================
    // ICorDebugReferenceValue
    // =========================================================================

    [ComImport]
    [Guid("CC7BCAF9-8A68-11D2-983C-0000F808342D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICorDebugReferenceValue
    {
        // --- ICorDebugValue methods (vtable slots 0-3) ---

        [PreserveSig]
        int GetType(out CorElementType pType);

        [PreserveSig]
        int GetSize(out uint pSize);

        [PreserveSig]
        int GetAddress(out ulong pAddress);

        [PreserveSig]
        int CreateBreakpoint(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugValueBreakpoint ppBreakpoint);

        // --- ICorDebugReferenceValue-specific methods (vtable slots 4+) ---

        // vtable slot 4
        [PreserveSig]
        int IsNull([MarshalAs(UnmanagedType.Bool)] out bool pbNull);

        // vtable slot 5
        [PreserveSig]
        int GetValue(out ulong pValue);

        // vtable slot 6
        [PreserveSig]
        int SetValue(ulong value);

        // vtable slot 7
        [PreserveSig]
        int Dereference(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugValue ppValue);

        // vtable slot 8
        [PreserveSig]
        int DereferenceStrong(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugValue ppValue);
    }

    // =========================================================================
    // ICorDebugBoxedValue
    // =========================================================================

    [ComImport]
    [Guid("CC7BCAFC-8A68-11D2-983C-0000F808342D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICorDebugBoxedValue
    {
        // --- ICorDebugValue methods (vtable slots 0-3) ---

        [PreserveSig]
        int GetType(out CorElementType pType);

        [PreserveSig]
        int GetSize(out uint pSize);

        [PreserveSig]
        int GetAddress(out ulong pAddress);

        [PreserveSig]
        int CreateBreakpoint(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugValueBreakpoint ppBreakpoint);

        // --- ICorDebugHeapValue methods (vtable slots 4-5) ---

        // vtable slot 4 — IsValid (ICorDebugHeapValue)
        [PreserveSig]
        int IsValid([MarshalAs(UnmanagedType.Bool)] out bool pbValid);

        // vtable slot 5 — CreateRelocBreakpoint (ICorDebugHeapValue)
        [PreserveSig]
        int CreateRelocBreakpoint(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugValueBreakpoint ppBreakpoint);

        // --- ICorDebugBoxedValue-specific methods (vtable slots 6+) ---

        // vtable slot 6
        [PreserveSig]
        int GetObject(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugObjectValue ppObject);
    }

    // =========================================================================
    // ICorDebugStepper
    // =========================================================================

    [ComImport]
    [Guid("CC7BCAEC-8A68-11D2-983C-0000F808342D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICorDebugStepper
    {
        // vtable slot 0
        [PreserveSig]
        int IsActive([MarshalAs(UnmanagedType.Bool)] out bool pbStepping);

        // vtable slot 1
        [PreserveSig]
        int Deactivate();

        // vtable slot 2
        [PreserveSig]
        int SetInterceptMask(CorDebugIntercept mask);

        // vtable slot 3
        [PreserveSig]
        int SetUnmappedStopMask(CorDebugUnmappedStop mask);

        // vtable slot 4
        [PreserveSig]
        int Step([MarshalAs(UnmanagedType.Bool)] bool bStepIn);

        // vtable slot 5
        [PreserveSig]
        int StepRange(
            [MarshalAs(UnmanagedType.Bool)] bool bStepIn,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] COR_DEBUG_STEP_RANGE[] ranges,
            uint cRangeCount);

        // vtable slot 6
        [PreserveSig]
        int StepOut();
    }

    // =========================================================================
    // ICorDebugBreakpoint
    // =========================================================================

    [ComImport]
    [Guid("CC7BCAE8-8A68-11D2-983C-0000F808342D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICorDebugBreakpoint
    {
        // vtable slot 0
        [PreserveSig]
        int Activate([MarshalAs(UnmanagedType.Bool)] bool bActive);

        // vtable slot 1
        [PreserveSig]
        int IsActive([MarshalAs(UnmanagedType.Bool)] out bool pbActive);
    }

    // =========================================================================
    // ICorDebugFunctionBreakpoint
    // =========================================================================

    [ComImport]
    [Guid("CC7BCAE9-8A68-11D2-983C-0000F808342D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICorDebugFunctionBreakpoint
    {
        // --- ICorDebugBreakpoint methods (vtable slots 0-1) ---

        [PreserveSig]
        int Activate([MarshalAs(UnmanagedType.Bool)] bool bActive);

        [PreserveSig]
        int IsActive([MarshalAs(UnmanagedType.Bool)] out bool pbActive);

        // --- ICorDebugFunctionBreakpoint-specific methods (vtable slots 2+) ---

        // vtable slot 2
        [PreserveSig]
        int GetFunction(
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugFunction ppFunction);

        // vtable slot 3
        [PreserveSig]
        int GetOffset(out uint pnOffset);
    }

    // =========================================================================
    // ICorDebugManagedCallback (implemented by C# class, not ComImport)
    // =========================================================================

    [ComImport]
    [Guid("3D6F5F60-7538-11D3-8D5B-00104B35E7EF")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICorDebugManagedCallback
    {
        // vtable slot 0
        [PreserveSig]
        int Breakpoint(
            [MarshalAs(UnmanagedType.Interface)] ICorDebugAppDomain pAppDomain,
            [MarshalAs(UnmanagedType.Interface)] ICorDebugThread pThread,
            [MarshalAs(UnmanagedType.Interface)] ICorDebugBreakpoint pBreakpoint);

        // vtable slot 1
        // NOTE: pStepper is IntPtr to avoid QI failure during COM callback marshaling
        [PreserveSig]
        int StepComplete(
            [MarshalAs(UnmanagedType.Interface)] ICorDebugAppDomain pAppDomain,
            [MarshalAs(UnmanagedType.Interface)] ICorDebugThread pThread,
            IntPtr pStepper,
            CorDebugStepReason reason);

        // vtable slot 2
        [PreserveSig]
        int Break(
            [MarshalAs(UnmanagedType.Interface)] ICorDebugAppDomain pAppDomain,
            [MarshalAs(UnmanagedType.Interface)] ICorDebugThread thread);

        // vtable slot 3
        [PreserveSig]
        int Exception(
            [MarshalAs(UnmanagedType.Interface)] ICorDebugAppDomain pAppDomain,
            [MarshalAs(UnmanagedType.Interface)] ICorDebugThread pThread,
            [MarshalAs(UnmanagedType.Bool)] bool unhandled);

        // vtable slot 4
        [PreserveSig]
        int EvalComplete(
            [MarshalAs(UnmanagedType.Interface)] ICorDebugAppDomain pAppDomain,
            [MarshalAs(UnmanagedType.Interface)] ICorDebugThread pThread,
            [MarshalAs(UnmanagedType.Interface)] ICorDebugEval pEval);

        // vtable slot 5
        [PreserveSig]
        int EvalException(
            [MarshalAs(UnmanagedType.Interface)] ICorDebugAppDomain pAppDomain,
            [MarshalAs(UnmanagedType.Interface)] ICorDebugThread pThread,
            [MarshalAs(UnmanagedType.Interface)] ICorDebugEval pEval);

        // vtable slot 6
        [PreserveSig]
        int CreateProcess(
            [MarshalAs(UnmanagedType.Interface)] ICorDebugProcess pProcess);

        // vtable slot 7
        [PreserveSig]
        int ExitProcess(
            [MarshalAs(UnmanagedType.Interface)] ICorDebugProcess pProcess);

        // vtable slot 8
        [PreserveSig]
        int CreateThread(
            [MarshalAs(UnmanagedType.Interface)] ICorDebugAppDomain pAppDomain,
            [MarshalAs(UnmanagedType.Interface)] ICorDebugThread thread);

        // vtable slot 9
        [PreserveSig]
        int ExitThread(
            [MarshalAs(UnmanagedType.Interface)] ICorDebugAppDomain pAppDomain,
            [MarshalAs(UnmanagedType.Interface)] ICorDebugThread thread);

        // vtable slot 10
        [PreserveSig]
        int LoadModule(
            [MarshalAs(UnmanagedType.Interface)] ICorDebugAppDomain pAppDomain,
            [MarshalAs(UnmanagedType.Interface)] ICorDebugModule pModule);

        // vtable slot 11
        [PreserveSig]
        int UnloadModule(
            [MarshalAs(UnmanagedType.Interface)] ICorDebugAppDomain pAppDomain,
            [MarshalAs(UnmanagedType.Interface)] ICorDebugModule pModule);

        // vtable slot 12
        [PreserveSig]
        int LoadClass(
            [MarshalAs(UnmanagedType.Interface)] ICorDebugAppDomain pAppDomain,
            [MarshalAs(UnmanagedType.Interface)] ICorDebugClass c);

        // vtable slot 13
        [PreserveSig]
        int UnloadClass(
            [MarshalAs(UnmanagedType.Interface)] ICorDebugAppDomain pAppDomain,
            [MarshalAs(UnmanagedType.Interface)] ICorDebugClass c);

        // vtable slot 14
        [PreserveSig]
        int DebuggerError(
            [MarshalAs(UnmanagedType.Interface)] ICorDebugProcess pProcess,
            int errorHR,
            uint errorCode);

        // vtable slot 15
        [PreserveSig]
        int LogMessage(
            [MarshalAs(UnmanagedType.Interface)] ICorDebugAppDomain pAppDomain,
            [MarshalAs(UnmanagedType.Interface)] ICorDebugThread pThread,
            int lLevel,
            [MarshalAs(UnmanagedType.LPWStr)] string pLogSwitchName,
            [MarshalAs(UnmanagedType.LPWStr)] string pMessage);

        // vtable slot 16
        [PreserveSig]
        int LogSwitch(
            [MarshalAs(UnmanagedType.Interface)] ICorDebugAppDomain pAppDomain,
            [MarshalAs(UnmanagedType.Interface)] ICorDebugThread pThread,
            int lLevel,
            uint ulReason,
            [MarshalAs(UnmanagedType.LPWStr)] string pLogSwitchName,
            [MarshalAs(UnmanagedType.LPWStr)] string pParentName);

        // vtable slot 17
        [PreserveSig]
        int CreateAppDomain(
            [MarshalAs(UnmanagedType.Interface)] ICorDebugProcess pProcess,
            [MarshalAs(UnmanagedType.Interface)] ICorDebugAppDomain pAppDomain);

        // vtable slot 18
        [PreserveSig]
        int ExitAppDomain(
            [MarshalAs(UnmanagedType.Interface)] ICorDebugProcess pProcess,
            [MarshalAs(UnmanagedType.Interface)] ICorDebugAppDomain pAppDomain);

        // vtable slot 19
        [PreserveSig]
        int LoadAssembly(
            [MarshalAs(UnmanagedType.Interface)] ICorDebugAppDomain pAppDomain,
            [MarshalAs(UnmanagedType.Interface)] ICorDebugAssembly pAssembly);

        // vtable slot 20
        [PreserveSig]
        int UnloadAssembly(
            [MarshalAs(UnmanagedType.Interface)] ICorDebugAppDomain pAppDomain,
            [MarshalAs(UnmanagedType.Interface)] ICorDebugAssembly pAssembly);

        // vtable slot 21
        [PreserveSig]
        int ControlCTrap(
            [MarshalAs(UnmanagedType.Interface)] ICorDebugProcess pProcess);

        // vtable slot 22
        [PreserveSig]
        int NameChange(
            [MarshalAs(UnmanagedType.Interface)] ICorDebugAppDomain pAppDomain,
            [MarshalAs(UnmanagedType.Interface)] ICorDebugThread pThread);

        // vtable slot 23
        [PreserveSig]
        int UpdateModuleSymbols(
            [MarshalAs(UnmanagedType.Interface)] ICorDebugAppDomain pAppDomain,
            [MarshalAs(UnmanagedType.Interface)] ICorDebugModule pModule,
            [MarshalAs(UnmanagedType.Interface)] object pSymbolStream); // IStream

        // vtable slot 24
        [PreserveSig]
        int EditAndContinueRemap(
            [MarshalAs(UnmanagedType.Interface)] ICorDebugAppDomain pAppDomain,
            [MarshalAs(UnmanagedType.Interface)] ICorDebugThread pThread,
            [MarshalAs(UnmanagedType.Interface)] ICorDebugFunction pFunction,
            [MarshalAs(UnmanagedType.Bool)] bool fAccurate);

        // vtable slot 25
        [PreserveSig]
        int BreakpointSetError(
            [MarshalAs(UnmanagedType.Interface)] ICorDebugAppDomain pAppDomain,
            [MarshalAs(UnmanagedType.Interface)] ICorDebugThread pThread,
            [MarshalAs(UnmanagedType.Interface)] ICorDebugBreakpoint pBreakpoint,
            uint dwError);
    }

    // =========================================================================
    // ICorDebugManagedCallback2 (implemented by C# class, not ComImport)
    // =========================================================================

    [ComImport]
    [Guid("250E5EEA-DB5C-4C76-B6F3-8C46F12E3203")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICorDebugManagedCallback2
    {
        // vtable slot 0
        [PreserveSig]
        int FunctionRemapOpportunity(
            [MarshalAs(UnmanagedType.Interface)] ICorDebugAppDomain pAppDomain,
            [MarshalAs(UnmanagedType.Interface)] ICorDebugThread pThread,
            [MarshalAs(UnmanagedType.Interface)] ICorDebugFunction pOldFunction,
            [MarshalAs(UnmanagedType.Interface)] ICorDebugFunction pNewFunction,
            uint oldILOffset);

        // vtable slot 1
        [PreserveSig]
        int CreateConnection(
            [MarshalAs(UnmanagedType.Interface)] ICorDebugProcess pProcess,
            uint dwConnectionId,
            [MarshalAs(UnmanagedType.LPWStr)] string pConnName);

        // vtable slot 2
        [PreserveSig]
        int ChangeConnection(
            [MarshalAs(UnmanagedType.Interface)] ICorDebugProcess pProcess,
            uint dwConnectionId);

        // vtable slot 3
        [PreserveSig]
        int DestroyConnection(
            [MarshalAs(UnmanagedType.Interface)] ICorDebugProcess pProcess,
            uint dwConnectionId);

        // vtable slot 4
        [PreserveSig]
        int Exception(
            [MarshalAs(UnmanagedType.Interface)] ICorDebugAppDomain pAppDomain,
            [MarshalAs(UnmanagedType.Interface)] ICorDebugThread pThread,
            [MarshalAs(UnmanagedType.Interface)] ICorDebugFrame pFrame,
            uint nOffset,
            CorDebugExceptionCallbackType dwEventType,
            uint dwFlags);

        // vtable slot 5
        [PreserveSig]
        int FunctionRemapComplete(
            [MarshalAs(UnmanagedType.Interface)] ICorDebugAppDomain pAppDomain,
            [MarshalAs(UnmanagedType.Interface)] ICorDebugThread pThread,
            [MarshalAs(UnmanagedType.Interface)] ICorDebugFunction pFunction);

        // vtable slot 6
        [PreserveSig]
        int MDANotification(
            [MarshalAs(UnmanagedType.Interface)] ICorDebugController pController,
            [MarshalAs(UnmanagedType.Interface)] ICorDebugThread pThread,
            [MarshalAs(UnmanagedType.Interface)] ICorDebugMDA pMDA);
    }

    // =========================================================================
    // ICorDebugUnmanagedCallback (forward declaration)
    // =========================================================================

    [ComImport]
    [Guid("5263E909-8CB5-11D3-BD2F-0000F80849BD")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICorDebugUnmanagedCallback
    {
        // vtable slot 0
        [PreserveSig]
        int DebugEvent(IntPtr pDebugEvent, [MarshalAs(UnmanagedType.Bool)] bool fOutOfBand);
    }

    // =========================================================================
    // Supporting enums referenced by interfaces above
    // =========================================================================

    public enum CorDebugThreadState
    {
        THREAD_RUN = 0,
        THREAD_SUSPEND = 1
    }

    public enum CorDebugUserState
    {
        USER_STOP_REQUESTED = 0x01,
        USER_SUSPEND_REQUESTED = 0x02,
        USER_BACKGROUND = 0x04,
        USER_UNSTARTED = 0x08,
        USER_STOPPED = 0x10,
        USER_WAIT_SLEEP_JOIN = 0x20,
        USER_SUSPENDED = 0x40,
        USER_UNSAFE_POINT = 0x80
    }

    public enum CorDebugMappingResult
    {
        MAPPING_PROLOG = 0x1,
        MAPPING_EPILOG = 0x2,
        MAPPING_NO_INFO = 0x4,
        MAPPING_UNMAPPED_ADDRESS = 0x8,
        MAPPING_EXACT = 0x10,
        MAPPING_APPROXIMATE = 0x20
    }

    public enum CorDebugCreateProcessFlags
    {
        DEBUG_NO_SPECIAL_OPTIONS = 0
    }

    // =========================================================================
    // Forward-declared interfaces (stubs to allow compilation)
    // =========================================================================

    [ComImport]
    [Guid("CC7BCAF3-8A68-11D2-983C-0000F808342D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICorDebugClass
    {
        // vtable slot 0
        [PreserveSig]
        int GetModule([MarshalAs(UnmanagedType.Interface)] out ICorDebugModule ppModule);

        // vtable slot 1
        [PreserveSig]
        int GetToken(out uint pTypeDef);

        // vtable slot 2
        [PreserveSig]
        int GetStaticFieldValue(
            uint fieldDef,
            [MarshalAs(UnmanagedType.Interface)] ICorDebugFrame pFrame,
            [MarshalAs(UnmanagedType.Interface)] out ICorDebugValue ppValue);
    }

    [ComImport]
    [Guid("DF59507C-D47A-459E-BCE2-6427EAC8FD06")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICorDebugAssembly
    {
        // vtable slot 0
        [PreserveSig]
        int GetProcess([MarshalAs(UnmanagedType.Interface)] out ICorDebugProcess ppProcess);

        // vtable slot 1
        [PreserveSig]
        int GetAppDomain([MarshalAs(UnmanagedType.Interface)] out ICorDebugAppDomain ppAppDomain);

        // vtable slot 2
        [PreserveSig]
        int EnumerateModules([MarshalAs(UnmanagedType.Interface)] out ICorDebugModuleEnum ppModules);

        // vtable slot 3
        [PreserveSig]
        int GetCodeBase(
            uint cchName,
            out uint pcchName,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] char[] szName);

        // vtable slot 4
        [PreserveSig]
        int GetName(
            uint cchName,
            out uint pcchName,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] char[] szName);
    }

    [ComImport]
    [Guid("B92CC7F7-9D2D-45C4-BC0B-C09F5B44A9CC")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICorDebugChain
    {
        // vtable slot 0
        [PreserveSig]
        int GetThread([MarshalAs(UnmanagedType.Interface)] out ICorDebugThread ppThread);

        // vtable slot 1
        [PreserveSig]
        int GetStackRange(out ulong pStart, out ulong pEnd);

        // vtable slot 2
        [PreserveSig]
        int GetContext([MarshalAs(UnmanagedType.Interface)] out ICorDebugContext ppContext);

        // vtable slot 3
        [PreserveSig]
        int GetCaller([MarshalAs(UnmanagedType.Interface)] out ICorDebugChain ppChain);

        // vtable slot 4
        [PreserveSig]
        int GetCallee([MarshalAs(UnmanagedType.Interface)] out ICorDebugChain ppChain);

        // vtable slot 5
        [PreserveSig]
        int GetPrevious([MarshalAs(UnmanagedType.Interface)] out ICorDebugChain ppChain);

        // vtable slot 6
        [PreserveSig]
        int GetNext([MarshalAs(UnmanagedType.Interface)] out ICorDebugChain ppChain);

        // vtable slot 7
        [PreserveSig]
        int IsManaged([MarshalAs(UnmanagedType.Bool)] out bool pManaged);

        // vtable slot 8
        [PreserveSig]
        int EnumerateFrames([MarshalAs(UnmanagedType.Interface)] out ICorDebugFrameEnum ppFrames);

        // vtable slot 9
        [PreserveSig]
        int GetActiveFrame([MarshalAs(UnmanagedType.Interface)] out ICorDebugFrame ppFrame);

        // vtable slot 10
        [PreserveSig]
        int GetRegisterSet([MarshalAs(UnmanagedType.Interface)] out ICorDebugRegisterSet ppRegisters);

        // vtable slot 11
        [PreserveSig]
        int GetReason(out uint pReason); // TODO: CorDebugChainReason
    }

    [ComImport]
    [Guid("CC7BCB00-8A68-11D2-983C-0000F808342D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICorDebugEval
    {
        // vtable slot 0 — TODO: placeholder
        [PreserveSig]
        int placeholder_CallFunction(
            [MarshalAs(UnmanagedType.Interface)] ICorDebugFunction pFunction,
            uint nArgs,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] ICorDebugValue[] ppArgs);

        // vtable slot 1
        [PreserveSig]
        int placeholder_NewObject(
            [MarshalAs(UnmanagedType.Interface)] ICorDebugFunction pConstructor,
            uint nArgs,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] ICorDebugValue[] ppArgs);

        // vtable slot 2
        [PreserveSig]
        int GetResult([MarshalAs(UnmanagedType.Interface)] out ICorDebugValue ppResult);

        // vtable slot 3
        [PreserveSig]
        int GetThread([MarshalAs(UnmanagedType.Interface)] out ICorDebugThread ppThread);

        // vtable slot 4
        [PreserveSig]
        int IsActive([MarshalAs(UnmanagedType.Bool)] out bool pbActive);

        // vtable slot 5
        [PreserveSig]
        int Abort();
    }

    [ComImport]
    [Guid("CC7BCAF5-8A68-11D2-983C-0000F808342D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICorDebugContext
    {
        // Inherits ICorDebugObjectValue — stub for compilation
        [PreserveSig]
        int placeholder_GetType(out CorElementType pType);
    }

    [ComImport]
    [Guid("CC7BCAEA-8A68-11D2-983C-0000F808342D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICorDebugValueBreakpoint
    {
        // Inherits ICorDebugBreakpoint
        [PreserveSig]
        int Activate([MarshalAs(UnmanagedType.Bool)] bool bActive);

        [PreserveSig]
        int IsActive([MarshalAs(UnmanagedType.Bool)] out bool pbActive);

        // vtable slot 2
        [PreserveSig]
        int GetValue([MarshalAs(UnmanagedType.Interface)] out ICorDebugValue ppValue);
    }

    [ComImport]
    [Guid("CC7BCAEB-8A68-11D2-983C-0000F808342D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICorDebugModuleBreakpoint
    {
        // Inherits ICorDebugBreakpoint
        [PreserveSig]
        int Activate([MarshalAs(UnmanagedType.Bool)] bool bActive);

        [PreserveSig]
        int IsActive([MarshalAs(UnmanagedType.Bool)] out bool pbActive);

        // vtable slot 2
        [PreserveSig]
        int GetModule([MarshalAs(UnmanagedType.Interface)] out ICorDebugModule ppModule);
    }

    [ComImport]
    [Guid("55E96461-9645-45E4-A2FF-0367877ABCDE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICorDebugEditAndContinueSnapshot
    {
        // TODO: placeholder — not typically needed for basic debugging
        [PreserveSig]
        int placeholder_CopyMetaData(IntPtr pIStream, out Guid pMvid);
    }

    [ComImport]
    [Guid("F0E0512B-CCCE-11D1-AB2D-00A0C9B0C4D3")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICorDebugRegisterSet
    {
        // vtable slot 0
        [PreserveSig]
        int GetRegistersAvailable(out ulong pAvailable);

        // vtable slot 1
        [PreserveSig]
        int GetRegisters(
            ulong mask,
            uint regCount,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] ulong[] regBuffer);

        // vtable slot 2
        [PreserveSig]
        int SetRegisters(
            ulong mask,
            uint regCount,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] ulong[] regBuffer);

        // vtable slot 3
        [PreserveSig]
        int GetThreadContext(uint contextSize, IntPtr context);

        // vtable slot 4
        [PreserveSig]
        int SetThreadContext(uint contextSize, IntPtr context);
    }

    [ComImport]
    [Guid("BFD2C6F7-DFE4-4890-9B71-5A84A7E11E3A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICorDebugMDA
    {
        // vtable slot 0
        [PreserveSig]
        int GetName(
            uint cchName,
            out uint pcchName,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] char[] szName);

        // vtable slot 1
        [PreserveSig]
        int GetDescription(
            uint cchName,
            out uint pcchName,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] char[] szName);

        // vtable slot 2
        [PreserveSig]
        int GetXML(
            uint cchName,
            out uint pcchName,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] char[] szName);

        // vtable slot 3
        [PreserveSig]
        int GetFlags(out uint pFlags);

        // vtable slot 4
        [PreserveSig]
        int GetOSThreadId(out uint pOsTid);
    }

    // =========================================================================
    // Enumerator interfaces (stubs — minimal methods needed for compilation)
    // =========================================================================

    [ComImport]
    [Guid("CC7BCB09-8A68-11D2-983C-0000F808342D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICorDebugThreadEnum
    {
        [PreserveSig]
        int Skip(uint celt);
        [PreserveSig]
        int Reset();
        [PreserveSig]
        int Clone([MarshalAs(UnmanagedType.Interface)] out ICorDebugThreadEnum ppEnum);
        [PreserveSig]
        int GetCount(out uint pcelt);
        [PreserveSig]
        int Next(
            uint celt,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] ICorDebugThread[] threads,
            out uint pceltFetched);
    }

    [ComImport]
    [Guid("63CA1B24-4359-4883-BD57-13F815F58744")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICorDebugProcessEnum
    {
        [PreserveSig]
        int Skip(uint celt);
        [PreserveSig]
        int Reset();
        [PreserveSig]
        int Clone([MarshalAs(UnmanagedType.Interface)] out ICorDebugProcessEnum ppEnum);
        [PreserveSig]
        int GetCount(out uint pcelt);
        [PreserveSig]
        int Next(
            uint celt,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] ICorDebugProcess[] processes,
            out uint pceltFetched);
    }

    [ComImport]
    [Guid("4A2A1EC9-85EC-4BFB-9F15-A89FDFE0FE83")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICorDebugAppDomainEnum
    {
        [PreserveSig]
        int Skip(uint celt);
        [PreserveSig]
        int Reset();
        [PreserveSig]
        int Clone([MarshalAs(UnmanagedType.Interface)] out ICorDebugAppDomainEnum ppEnum);
        [PreserveSig]
        int GetCount(out uint pcelt);
        [PreserveSig]
        int Next(
            uint celt,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] ICorDebugAppDomain[] appDomains,
            out uint pceltFetched);
    }

    [ComImport]
    [Guid("CC7BCB04-8A68-11D2-983C-0000F808342D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICorDebugAssemblyEnum
    {
        [PreserveSig]
        int Skip(uint celt);
        [PreserveSig]
        int Reset();
        [PreserveSig]
        int Clone([MarshalAs(UnmanagedType.Interface)] out ICorDebugAssemblyEnum ppEnum);
        [PreserveSig]
        int GetCount(out uint pcelt);
        [PreserveSig]
        int Next(
            uint celt,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] ICorDebugAssembly[] assemblies,
            out uint pceltFetched);
    }

    [ComImport]
    [Guid("CC7BCB05-8A68-11D2-983C-0000F808342D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICorDebugModuleEnum
    {
        [PreserveSig]
        int Skip(uint celt);
        [PreserveSig]
        int Reset();
        [PreserveSig]
        int Clone([MarshalAs(UnmanagedType.Interface)] out ICorDebugModuleEnum ppEnum);
        [PreserveSig]
        int GetCount(out uint pcelt);
        [PreserveSig]
        int Next(
            uint celt,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] ICorDebugModule[] modules,
            out uint pceltFetched);
    }

    [ComImport]
    [Guid("CC7BCB06-8A68-11D2-983C-0000F808342D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICorDebugBreakpointEnum
    {
        [PreserveSig]
        int Skip(uint celt);
        [PreserveSig]
        int Reset();
        [PreserveSig]
        int Clone([MarshalAs(UnmanagedType.Interface)] out ICorDebugBreakpointEnum ppEnum);
        [PreserveSig]
        int GetCount(out uint pcelt);
        [PreserveSig]
        int Next(
            uint celt,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] ICorDebugBreakpoint[] breakpoints,
            out uint pceltFetched);
    }

    [ComImport]
    [Guid("CC7BCB07-8A68-11D2-983C-0000F808342D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICorDebugStepperEnum
    {
        [PreserveSig]
        int Skip(uint celt);
        [PreserveSig]
        int Reset();
        [PreserveSig]
        int Clone([MarshalAs(UnmanagedType.Interface)] out ICorDebugStepperEnum ppEnum);
        [PreserveSig]
        int GetCount(out uint pcelt);
        [PreserveSig]
        int Next(
            uint celt,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] ICorDebugStepper[] steppers,
            out uint pceltFetched);
    }

    [ComImport]
    [Guid("CC7BCAEE-8A68-11D2-983C-0000F808342D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICorDebugChainEnum
    {
        [PreserveSig]
        int Skip(uint celt);
        [PreserveSig]
        int Reset();
        [PreserveSig]
        int Clone([MarshalAs(UnmanagedType.Interface)] out ICorDebugChainEnum ppEnum);
        [PreserveSig]
        int GetCount(out uint pcelt);
        [PreserveSig]
        int Next(
            uint celt,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] ICorDebugChain[] chains,
            out uint pceltFetched);
    }

    [ComImport]
    [Guid("CC7BCAF1-8A68-11D2-983C-0000F808342D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICorDebugFrameEnum
    {
        [PreserveSig]
        int Skip(uint celt);
        [PreserveSig]
        int Reset();
        [PreserveSig]
        int Clone([MarshalAs(UnmanagedType.Interface)] out ICorDebugFrameEnum ppEnum);
        [PreserveSig]
        int GetCount(out uint pcelt);
        [PreserveSig]
        int Next(
            uint celt,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] ICorDebugFrame[] frames,
            out uint pceltFetched);
    }

    [ComImport]
    [Guid("CC7BCAF2-8A68-11D2-983C-0000F808342D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICorDebugValueEnum
    {
        [PreserveSig]
        int Skip(uint celt);
        [PreserveSig]
        int Reset();
        [PreserveSig]
        int Clone([MarshalAs(UnmanagedType.Interface)] out ICorDebugValueEnum ppEnum);
        [PreserveSig]
        int GetCount(out uint pcelt);
        [PreserveSig]
        int Next(
            uint celt,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] ICorDebugValue[] values,
            out uint pceltFetched);
    }

    [ComImport]
    [Guid("CC7BCB08-8A68-11D2-983C-0000F808342D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICorDebugObjectEnum
    {
        [PreserveSig]
        int Skip(uint celt);
        [PreserveSig]
        int Reset();
        [PreserveSig]
        int Clone([MarshalAs(UnmanagedType.Interface)] out ICorDebugObjectEnum ppEnum);
        [PreserveSig]
        int GetCount(out uint pcelt);
        [PreserveSig]
        int Next(
            uint celt,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] ulong[] objects,
            out uint pceltFetched);
    }

    [ComImport]
    [Guid("F0E0512C-CCCE-11D1-AB2D-00A0C9B0C4D3")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICorDebugErrorInfoEnum
    {
        [PreserveSig]
        int Skip(uint celt);
        [PreserveSig]
        int Reset();
        [PreserveSig]
        int Clone([MarshalAs(UnmanagedType.Interface)] out ICorDebugErrorInfoEnum ppEnum);
        [PreserveSig]
        int GetCount(out uint pcelt);
        [PreserveSig]
        int Next(
            uint celt,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IntPtr[] errors,
            out uint pceltFetched);
    }
}
