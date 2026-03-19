using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace BasicLang.Debugger
{
    /// <summary>
    /// DAP (Debug Adapter Protocol) server for .NET CLR debugging.
    /// Uses ICorDebug via dbgshim to debug compiled BasicLang executables.
    /// Follows the same DAP JSON-RPC message format as DebugSession (interpreter-based).
    /// </summary>
    public class NetDebugAdapter
    {
        private readonly Stream _input;
        private readonly Stream _outputStream;

        // Components
        private NetDebugProcess _process;
        private SourceMapper _sourceMapper;
        private ClrBreakpointManager _breakpointManager;
        private VariableInspector _variableInspector;

        // Module tracking — needed for binding breakpoints
        private readonly Dictionary<string, ICorDebugModule> _loadedModules = new(StringComparer.OrdinalIgnoreCase);

        // State
        private readonly Dictionary<int, ICorDebugThread> _threads = new();
        private int _requestSeq;
        private bool _exceptionFilterAll;
        private bool _exceptionFilterUncaught = true;

        // Thread tracking — maps OS thread IDs to their last-known state
        private readonly Dictionary<int, ThreadState> _threadStates = new();
        private int _stoppedThreadId;    // which thread caused the last stop event
        private int _focusedThreadId;    // which thread the IDE is currently inspecting
        private readonly HashSet<int> _frozenThreads = new(); // threads that should not resume

        // Scope / frame tracking for variables requests
        private readonly Dictionary<int, ScopeInfo> _scopeReferences = new();
        private readonly Dictionary<int, FrameInfo> _frameInfoMap = new();
        private int _nextScopeRef = 1;
        private int _nextFrameId = 1;

        // Exception info — stored when OnExceptionThrown fires, returned by exceptionInfo request
        private string _lastExceptionType;
        private string _lastExceptionMessage;
        private string _lastExceptionDescription;
        private bool _lastExceptionIsUnhandled;

        private readonly object _writeLock = new();

        public NetDebugAdapter(Stream input, Stream output)
        {
            _input = input;
            _outputStream = output;
            _breakpointManager = new ClrBreakpointManager();
            _variableInspector = new VariableInspector();
        }

        // =====================================================================
        // Main message loop — same pattern as DebugSession.RunAsync()
        // =====================================================================

        public async Task RunAsync()
        {
            var reader = new StreamReader(_input, Encoding.UTF8);

            while (true)
            {
                var line = await reader.ReadLineAsync();
                if (line == null) break;

                if (line.StartsWith("Content-Length:"))
                {
                    var length = int.Parse(line.Substring(15).Trim());
                    await reader.ReadLineAsync(); // empty line separator

                    var chars = new char[length];
                    await reader.ReadBlockAsync(chars, 0, length);
                    var content = new string(chars);

                    await HandleMessageAsync(content);
                }
            }
        }

        // =====================================================================
        // DAP message dispatch
        // =====================================================================

        private async Task HandleMessageAsync(string content)
        {
            try
            {
                var message = JsonSerializer.Deserialize<DAPMessage>(content);
                if (message == null) return;

                DAPResponse response = message.Command switch
                {
                    "initialize" => HandleInitialize(message),
                    "launch" => await HandleLaunchAsync(message),
                    "attach" => await HandleAttachAsync(message),
                    "setBreakpoints" => HandleSetBreakpoints(message),
                    "setFunctionBreakpoints" => HandleSetFunctionBreakpoints(message),
                    "configurationDone" => HandleConfigurationDone(message),
                    "threads" => HandleThreads(message),
                    "stackTrace" => HandleStackTrace(message),
                    "scopes" => HandleScopes(message),
                    "variables" => HandleVariables(message),
                    "setVariable" => HandleSetVariable(message),
                    "continue" => HandleContinue(message),
                    "next" => HandleNext(message),
                    "stepIn" => HandleStepIn(message),
                    "stepOut" => HandleStepOut(message),
                    "pause" => HandlePause(message),
                    "evaluate" => HandleEvaluate(message),
                    "setExceptionBreakpoints" => HandleSetExceptionBreakpoints(message),
                    "exceptionInfo" => HandleExceptionInfo(message),
                    "disconnect" => HandleDisconnect(message),
                    _ => CreateResponse(message, true)
                };

                await SendResponseAsync(response);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"NetDebugAdapter error: {ex.Message}");
            }
        }

        // =====================================================================
        // Command handlers
        // =====================================================================

        private DAPResponse HandleInitialize(DAPMessage request)
        {
            var response = CreateResponse(request, true);
            response.Body = new Dictionary<string, object>
            {
                ["supportsConfigurationDoneRequest"] = true,
                ["supportsFunctionBreakpoints"] = true,
                ["supportsConditionalBreakpoints"] = true,
                ["supportsHitConditionalBreakpoints"] = true,
                ["supportsLogPoints"] = true,
                ["supportsEvaluateForHovers"] = true,
                ["supportsExceptionInfoRequest"] = true,
                ["supportsSetVariable"] = true,
                ["supportsStepBack"] = false,
                ["supportsRestartFrame"] = false,
                ["supportsGotoTargetsRequest"] = false,
                ["supportsCompletionsRequest"] = false,
                ["supportsModulesRequest"] = false,
                ["supportsExceptionOptions"] = true,
                ["exceptionBreakpointFilters"] = new object[]
                {
                    new Dictionary<string, object>
                    {
                        ["filter"] = "all",
                        ["label"] = "All Exceptions",
                        ["default"] = false
                    },
                    new Dictionary<string, object>
                    {
                        ["filter"] = "uncaught",
                        ["label"] = "Uncaught Exceptions",
                        ["default"] = true
                    }
                },
                ["supportsValueFormattingOptions"] = false,
                ["supportTerminateDebuggee"] = true,
                ["supportsDelayedStackTraceLoading"] = false,
                ["supportsLoadedSourcesRequest"] = false
            };
            return response;
        }

        private async Task<DAPResponse> HandleLaunchAsync(DAPMessage request)
        {
            var args = request.Arguments;

            // Extract launch parameters
            string exePath = null;
            string cwd = null;
            string[] launchArgs = null;

            if (args.TryGetProperty("program", out var programProp))
                exePath = programProp.GetString();

            if (args.TryGetProperty("cwd", out var cwdProp))
                cwd = cwdProp.GetString();

            if (args.TryGetProperty("args", out var argsProp) && argsProp.ValueKind == JsonValueKind.Array)
            {
                var argList = new List<string>();
                foreach (var arg in argsProp.EnumerateArray())
                {
                    var s = arg.GetString();
                    if (s != null) argList.Add(s);
                }
                launchArgs = argList.ToArray();
            }

            // If processId is provided instead of program, delegate to attach
            if (string.IsNullOrEmpty(exePath) && args.TryGetProperty("processId", out var pidProp))
            {
                return await HandleAttachAsync(request);
            }

            if (string.IsNullOrEmpty(exePath))
            {
                var response = CreateResponse(request, false);
                response.Body = new Dictionary<string, object>
                {
                    ["error"] = new Dictionary<string, object>
                    {
                        ["id"] = 1,
                        ["format"] = "No program specified in launch configuration"
                    }
                };
                return response;
            }

            // Default working directory to exe directory
            if (string.IsNullOrEmpty(cwd))
                cwd = Path.GetDirectoryName(exePath);

            // Find and load matching PDB
            var pdbPath = Path.ChangeExtension(exePath, ".pdb");
            _sourceMapper = new SourceMapper();
            if (File.Exists(pdbPath))
            {
                _sourceMapper.LoadPdb(pdbPath);
            }

            // Create process and wire events
            _process = new NetDebugProcess();
            _process.BreakpointHit += OnBreakpointHit;
            _process.StepCompleted += OnStepCompleted;
            _process.ExceptionThrown += OnExceptionThrown;
            _process.ModuleLoaded += OnModuleLoaded;
            _process.ProcessExited += OnProcessExited;
            _process.ThreadCreated += OnThreadCreated;
            _process.ThreadExited += OnThreadExited;

            // Launch the process and attach ICorDebug
            var launched = await _process.LaunchAsync(exePath, cwd, launchArgs);
            if (!launched)
            {
                await SendEventAsync("output", new Dictionary<string, object>
                {
                    ["category"] = "stderr",
                    ["output"] = $"Failed to launch and attach debugger to: {exePath}\nReason: {_process?.LastError ?? "Unknown error"}\n"
                });
                var response = CreateResponse(request, false);
                var errorMsg = _process?.LastError ?? "Failed to launch process or attach debugger.";
                response.Body = new Dictionary<string, object>
                {
                    ["error"] = new Dictionary<string, object>
                    {
                        ["id"] = 2,
                        ["format"] = errorMsg
                    }
                };
                return response;
            }

            // Send initialized event — tells IDE it can send setBreakpoints
            await SendEventAsync("initialized", null);

            return CreateResponse(request, true);
        }

        private async Task<DAPResponse> HandleAttachAsync(DAPMessage request)
        {
            var args = request.Arguments;

            // Extract attach parameters
            int processId = 0;
            if (args.TryGetProperty("processId", out var pidProp))
            {
                processId = pidProp.GetInt32();
            }

            if (processId <= 0)
            {
                var response = CreateResponse(request, false);
                response.Body = new Dictionary<string, object>
                {
                    ["error"] = new Dictionary<string, object>
                    {
                        ["id"] = 1,
                        ["format"] = "No processId specified in attach configuration"
                    }
                };
                return response;
            }

            // Initialize source mapper (try to find PDB from the process main module)
            _sourceMapper = new SourceMapper();
            try
            {
                var targetProcess = System.Diagnostics.Process.GetProcessById(processId);
                var mainModule = targetProcess.MainModule?.FileName;
                if (mainModule != null)
                {
                    var pdbPath = Path.ChangeExtension(mainModule, ".pdb");
                    if (File.Exists(pdbPath))
                        _sourceMapper.LoadPdb(pdbPath);
                }
            }
            catch { /* best-effort PDB loading */ }

            // Create process and wire events
            _process = new NetDebugProcess();
            _process.BreakpointHit += OnBreakpointHit;
            _process.StepCompleted += OnStepCompleted;
            _process.ExceptionThrown += OnExceptionThrown;
            _process.ModuleLoaded += OnModuleLoaded;
            _process.ProcessExited += OnProcessExited;
            _process.ThreadCreated += OnThreadCreated;
            _process.ThreadExited += OnThreadExited;

            // Attach ICorDebug to the running process
            var attached = await _process.AttachAsync(processId);
            if (!attached)
            {
                await SendEventAsync("output", new Dictionary<string, object>
                {
                    ["category"] = "stderr",
                    ["output"] = $"Failed to attach debugger to process {processId}\nReason: {_process?.LastError ?? "Unknown error"}\n"
                });
                var response = CreateResponse(request, false);
                var errorMsg = _process?.LastError ?? "Failed to attach to process.";
                response.Body = new Dictionary<string, object>
                {
                    ["error"] = new Dictionary<string, object>
                    {
                        ["id"] = 2,
                        ["format"] = errorMsg
                    }
                };
                return response;
            }

            // Send initialized event — tells IDE it can send setBreakpoints
            await SendEventAsync("initialized", null);

            return CreateResponse(request, true);
        }

        private DAPResponse HandleSetBreakpoints(DAPMessage request)
        {
            var args = request.Arguments;

            // Get source file path
            string sourcePath = null;
            if (args.TryGetProperty("source", out var sourceProp) &&
                sourceProp.TryGetProperty("path", out var pathProp))
            {
                sourcePath = pathProp.GetString();
            }

            if (string.IsNullOrEmpty(sourcePath))
                return CreateResponse(request, true);

            // Deactivate existing CLR breakpoints for this file before clearing
            DeactivateBreakpointsForFile(sourcePath);
            _breakpointManager.ClearFile(sourcePath);

            var resultBreakpoints = new List<object>();

            if (args.TryGetProperty("breakpoints", out var bpArray) && bpArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var bp in bpArray.EnumerateArray())
                {
                    int line = bp.TryGetProperty("line", out var lineProp) ? lineProp.GetInt32() : 0;
                    string condition = bp.TryGetProperty("condition", out var condProp) ? condProp.GetString() : null;
                    string hitCondition = bp.TryGetProperty("hitCondition", out var hitProp) ? hitProp.GetString() : null;
                    string logMessage = bp.TryGetProperty("logMessage", out var logProp) ? logProp.GetString() : null;

                    int bpId = _breakpointManager.AddPendingBreakpoint(sourcePath, line, condition, hitCondition, logMessage);
                    var entry = _breakpointManager.GetBreakpoint(bpId);

                    // Try to bind immediately if we have PDB data and a loaded module
                    bool verified = false;
                    int actualLine = line;

                    var ilInfo = _sourceMapper?.GetILOffsetForLine(sourcePath, line);
                    if (ilInfo.HasValue && _loadedModules.Count > 0)
                    {
                        actualLine = _sourceMapper.FindNearestExecutableLine(sourcePath, line);
                        // Try to bind on user modules (not system assemblies)
                        foreach (var kvp in _loadedModules)
                        {
                            var modName = Path.GetFileName(kvp.Key);
                            bool isSystem = modName.StartsWith("System.", StringComparison.OrdinalIgnoreCase) ||
                                modName.Equals("mscorlib.dll", StringComparison.OrdinalIgnoreCase) ||
                                modName.Equals("netstandard.dll", StringComparison.OrdinalIgnoreCase);
                            if (!isSystem && TryBindBreakpoint(entry, ilInfo.Value.methodToken, ilInfo.Value.ilOffset, kvp.Value))
                            {
                                verified = true;
                                break;
                            }
                        }
                    }

                    resultBreakpoints.Add(new Dictionary<string, object>
                    {
                        ["id"] = bpId,
                        ["verified"] = verified,
                        ["line"] = actualLine
                    });
                }
            }

            var response = CreateResponse(request, true);
            response.Body = new Dictionary<string, object>
            {
                ["breakpoints"] = resultBreakpoints
            };
            return response;
        }

        private DAPResponse HandleSetFunctionBreakpoints(DAPMessage request)
        {
            // Function breakpoints are not yet supported for CLR debugging
            var response = CreateResponse(request, true);
            response.Body = new Dictionary<string, object>
            {
                ["breakpoints"] = new List<object>()
            };
            return response;
        }

        private DAPResponse HandleConfigurationDone(DAPMessage request)
        {
            // Resume the process — all initial breakpoints have been set
            _process?.Continue();
            return CreateResponse(request, true);
        }

        private DAPResponse HandleThreads(DAPMessage request)
        {
            var threadList = new List<object>();

            // Enumerate all real managed threads from ICorDebugProcess
            if (_process?.CorDebugProcess != null)
            {
                try
                {
                    var threads = EnumerateAllThreads();
                    bool firstThread = true;
                    foreach (var (threadId, thread) in threads)
                    {
                        // Track the thread
                        _threads[threadId] = thread;

                        // Try to get thread debug state for status
                        string status = "running";
                        if (_threadStates.TryGetValue(threadId, out var ts))
                            status = ts.ToString().ToLower();

                        // First thread is typically the main thread
                        string name = firstThread ? "Main Thread" : $"Worker Thread";
                        firstThread = false;

                        // Try to read thread name via ICorDebugThread2 or metadata
                        string customName = TryGetThreadName(thread);
                        if (!string.IsNullOrEmpty(customName))
                            name = customName;

                        threadList.Add(new Dictionary<string, object>
                        {
                            ["id"] = threadId,
                            ["name"] = $"{name} ({threadId})"
                        });
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[DAP] EnumerateThreads error: {ex.Message}");
                }
            }

            // Fallback: if no threads enumerated, report at least thread 1
            if (threadList.Count == 0)
            {
                threadList.Add(new Dictionary<string, object>
                {
                    ["id"] = 1,
                    ["name"] = "Main Thread (1)"
                });
            }

            var response = CreateResponse(request, true);
            response.Body = new Dictionary<string, object>
            {
                ["threads"] = threadList
            };
            return response;
        }

        private DAPResponse HandleStackTrace(DAPMessage request)
        {
            var args = request.Arguments;
            int threadId = args.TryGetProperty("threadId", out var tidProp) ? tidProp.GetInt32() : 1;

            var stackFrames = new List<object>();

            // Clear previous frame mapping
            _frameInfoMap.Clear();
            _nextFrameId = 1;

            const int MaxFrames = 50;

            try
            {
                // Try raw vtable walk of the thread's frames
                // Use the requested thread, not just the first one
                ICorDebugThread thread = GetThreadForId(threadId) ?? GetFirstThread();

                if (thread != null)
                {
                    var threadPtr = Marshal.GetIUnknownForObject(thread);
                    try
                    {
                        var vtable = Marshal.ReadIntPtr(threadPtr);

                        // ICorDebugThread::GetActiveFrame is slot 12 (IUnknown(3) + slot 9=CreateStepper... wait)
                        // From CorDebugWrappers: GetProcess(0), GetID(1), GetHandle(2), GetAppDomain(3),
                        // SetDebugState(4), GetDebugState(5), GetUserState(6), GetCurrentException(7),
                        // ClearCurrentException(8), CreateStepper(9), EnumerateChains(10),
                        // GetActiveChain(11), GetActiveFrame(12)
                        // So GetActiveFrame is slot 3+12 = 15
                        var getActiveFrameSlot = Marshal.ReadIntPtr(vtable, 15 * IntPtr.Size);
                        var getActiveFrame = Marshal.GetDelegateForFunctionPointer<GetActiveFrameDelegate>(getActiveFrameSlot);
                        int hr = getActiveFrame(threadPtr, out var framePtr);

                        // Walk the call stack using ICorDebugFrame::GetCaller
                        while (hr >= 0 && framePtr != IntPtr.Zero && stackFrames.Count < MaxFrames)
                        {
                            // ICorDebugFrame vtable layout (after IUnknown):
                            // GetChain(0), GetCode(1), GetFunction(2), GetFunctionToken(3),
                            // GetStackRange(4), GetCaller(5), GetCallee(6), CreateStepper(7)
                            // ICorDebugILFrame adds: GetIP(8), SetIP(9), EnumerateLocalVariables(10),
                            //                        GetLocalVariable(11), EnumerateArguments(12), GetArgument(13)
                            // GetFunctionToken is slot 3+3 = 6
                            // GetCaller is slot 3+5 = 8
                            // GetIP (ICorDebugILFrame) is slot 3+8 = 11

                            var frameVtable = Marshal.ReadIntPtr(framePtr);

                            // Get function token (on ICorDebugFrame -- slot 6)
                            var getFunctionTokenSlot = Marshal.ReadIntPtr(frameVtable, 6 * IntPtr.Size);
                            var getFunctionToken = Marshal.GetDelegateForFunctionPointer<GetFunctionTokenDelegate>(getFunctionTokenSlot);
                            hr = getFunctionToken(framePtr, out uint functionToken);

                            // Get IL offset -- MUST QueryInterface for ICorDebugILFrame first
                            uint ilOffset = 0;
                            Guid iidILFrame = new Guid("03E26311-4F76-11D3-88C6-006097945418");
                            int qiHr = Marshal.QueryInterface(framePtr, ref iidILFrame, out IntPtr ilFramePtr);
                            if (qiHr >= 0 && ilFramePtr != IntPtr.Zero)
                            {
                                var ilFrameVtable = Marshal.ReadIntPtr(ilFramePtr);
                                var getIPSlot = Marshal.ReadIntPtr(ilFrameVtable, 11 * IntPtr.Size);
                                var getIP = Marshal.GetDelegateForFunctionPointer<GetIPDelegate>(getIPSlot);
                                getIP(ilFramePtr, out ilOffset, out _);

                                // Store frame info for variable inspection
                                int frameId = _nextFrameId++;
                                var frameInfo = new FrameInfo
                                {
                                    FunctionToken = (int)functionToken,
                                    RawILFramePtr = ilFramePtr,
                                    ManagedFrame = null
                                };
                                // Add ref so the raw pointer stays valid
                                Marshal.AddRef(ilFramePtr);

                                try
                                {
                                    var ilFrameObj = (ICorDebugILFrame)Marshal.GetObjectForIUnknown(ilFramePtr);
                                    frameInfo.ManagedFrame = ilFrameObj;
                                }
                                catch
                                {
                                    // QI succeeded but managed cast failed -- will use raw vtable or PDB fallback
                                }

                                _frameInfoMap[frameId] = frameInfo;

                                Marshal.Release(ilFramePtr);


                                string frameName = $"Frame {stackFrames.Count}";
                                string sourceFile = null;
                                int sourceLine = 0;
                                int sourceColumn = 1;

                                // Map IL offset to source location via PDB
                                var location = _sourceMapper?.GetSourceLocation((int)functionToken, (int)ilOffset);
                                if (location.HasValue)
                                {
                                    sourceFile = location.Value.file;
                                    sourceLine = location.Value.line;
                                    sourceColumn = location.Value.column;
                                    frameName = Path.GetFileNameWithoutExtension(sourceFile) ?? frameName;
                                }

                                var frameDict = new Dictionary<string, object>
                                {
                                    ["id"] = frameId,
                                    ["name"] = frameName,
                                    ["line"] = sourceLine,
                                    ["column"] = sourceColumn
                                };

                                if (sourceFile != null)
                                {
                                    frameDict["source"] = new Dictionary<string, object>
                                    {
                                        ["path"] = sourceFile,
                                        ["name"] = Path.GetFileName(sourceFile)
                                    };
                                }

                                stackFrames.Add(frameDict);
                            }
                            else
                            {
                                // Increment frame ID even for skipped frames to keep numbering consistent
                                _nextFrameId++;
                            }

                            // Walk to caller frame via ICorDebugFrame::GetCaller (slot 8)
                            var getCallerSlot = Marshal.ReadIntPtr(frameVtable, 8 * IntPtr.Size);
                            var getCaller = Marshal.GetDelegateForFunctionPointer<GetCallerDelegate>(getCallerSlot);
                            int callerHr = getCaller(framePtr, out var callerPtr);
                            Marshal.Release(framePtr);

                            if (callerHr < 0 || callerPtr == IntPtr.Zero)
                            {
                                break;
                            }

                            framePtr = callerPtr;
                            hr = 0; // Reset hr for loop condition
                        }
                    }
                    finally
                    {
                        Marshal.Release(threadPtr);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"StackTrace error: {ex.Message}");
            }

            var response = CreateResponse(request, true);
            response.Body = new Dictionary<string, object>
            {
                ["stackFrames"] = stackFrames,
                ["totalFrames"] = stackFrames.Count
            };
            return response;
        }

        private DAPResponse HandleScopes(DAPMessage request)
        {
            var args = request.Arguments;
            int frameId = args.TryGetProperty("frameId", out var frameProp) ? frameProp.GetInt32() : 0;

            // Clear previous scope references
            _scopeReferences.Clear();
            _nextScopeRef = 1;

            var scopes = new List<object>();

            if (_frameInfoMap.ContainsKey(frameId))
            {
                // Locals scope — always returned; will use COM, raw vtable, or PDB fallback
                int localsRef = _nextScopeRef++;
                _scopeReferences[localsRef] = new ScopeInfo { FrameId = frameId, Kind = ScopeKind.Locals };
                scopes.Add(new Dictionary<string, object>
                {
                    ["name"] = "Locals",
                    ["variablesReference"] = localsRef,
                    ["expensive"] = false
                });

                // Arguments scope
                int argsRef = _nextScopeRef++;
                _scopeReferences[argsRef] = new ScopeInfo { FrameId = frameId, Kind = ScopeKind.Arguments };
                scopes.Add(new Dictionary<string, object>
                {
                    ["name"] = "Arguments",
                    ["variablesReference"] = argsRef,
                    ["expensive"] = false
                });
            }

            var response = CreateResponse(request, true);
            response.Body = new Dictionary<string, object>
            {
                ["scopes"] = scopes
            };
            return response;
        }

        private DAPResponse HandleVariables(DAPMessage request)
        {
            var args = request.Arguments;
            int varRef = args.TryGetProperty("variablesReference", out var refProp) ? refProp.GetInt32() : 0;

            var variables = new List<object>();

            try
            {
                if (_scopeReferences.TryGetValue(varRef, out var scopeInfo))
                {
                    if (_frameInfoMap.TryGetValue(scopeInfo.FrameId, out var frameInfo))
                    {
                        List<DapVariable> dapVars = null;

                        // Strategy 1: Managed COM interface (works when RCW cast succeeded)
                        if (frameInfo.ManagedFrame != null)
                        {
                            try
                            {
                                if (scopeInfo.Kind == ScopeKind.Locals)
                                    dapVars = _variableInspector.GetLocals(frameInfo.ManagedFrame);
                                else
                                    dapVars = _variableInspector.GetArguments(frameInfo.ManagedFrame);

                                if (dapVars != null && dapVars.Count > 0 &&
                                    !dapVars.TrueForAll(v => v.Name == "<error>"))
                                {
                                    if (scopeInfo.Kind == ScopeKind.Locals)
                                        ApplyPdbLocalNames(dapVars, frameInfo.FunctionToken);
                                }
                                else
                                {
                                    dapVars = null;
                                }
                            }
                            catch
                            {
                                dapVars = null;
                            }
                        }

                        // Strategy 2: Raw vtable calls to read locals/arguments
                        if (dapVars == null && frameInfo.RawILFramePtr != IntPtr.Zero)
                        {
                            try
                            {
                                if (scopeInfo.Kind == ScopeKind.Locals)
                                    dapVars = ReadLocalsViaRawVtable(frameInfo);
                                else
                                    dapVars = ReadArgumentsViaRawVtable(frameInfo);
                            }
                            catch (Exception ex)
                            {
                                Console.Error.WriteLine($"Raw vtable variable read failed: {ex.Message}");
                                dapVars = null;
                            }
                        }

                        // Strategy 3: PDB-only fallback — variable names with placeholder values
                        if (dapVars == null && scopeInfo.Kind == ScopeKind.Locals)
                            dapVars = GetPdbLocalsFallback(frameInfo.FunctionToken);

                        if (dapVars == null)
                            dapVars = new List<DapVariable>();

                        foreach (var dv in dapVars)
                        {
                            variables.Add(new Dictionary<string, object>
                            {
                                ["name"] = dv.Name,
                                ["value"] = dv.Value,
                                ["type"] = dv.Type,
                                ["variablesReference"] = dv.VariablesReference
                            });
                        }
                    }
                }
                else
                {
                    // This is a child expansion reference — delegate to VariableInspector
                    var children = _variableInspector.GetChildren(varRef);
                    foreach (var dv in children)
                    {
                        variables.Add(new Dictionary<string, object>
                        {
                            ["name"] = dv.Name,
                            ["value"] = dv.Value,
                            ["type"] = dv.Type,
                            ["variablesReference"] = dv.VariablesReference
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Variables error: {ex.Message}");
            }

            var response = CreateResponse(request, true);
            response.Body = new Dictionary<string, object>
            {
                ["variables"] = variables
            };
            return response;
        }

        private DAPResponse HandleSetVariable(DAPMessage request)
        {
            var args = request.Arguments;
            int varRef = args.TryGetProperty("variablesReference", out var refProp) ? refProp.GetInt32() : 0;
            string varName = args.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : "";
            string newValue = args.TryGetProperty("value", out var valProp) ? valProp.GetString() : "";

            try
            {
                if (!_scopeReferences.TryGetValue(varRef, out var scopeInfo))
                    return CreateSetVariableError(request, "Variable scope not found");

                if (!_frameInfoMap.TryGetValue(scopeInfo.FrameId, out var frameInfo))
                    return CreateSetVariableError(request, "Frame not found");

                if (frameInfo.RawILFramePtr == IntPtr.Zero)
                    return CreateSetVariableError(request, "No IL frame available for variable modification");

                var ilFramePtr = frameInfo.RawILFramePtr;
                var vtable = Marshal.ReadIntPtr(ilFramePtr);

                // Determine variable index by name
                int varIndex = -1;
                int varCount = 0;

                if (scopeInfo.Kind == ScopeKind.Locals)
                {
                    // Get local variable count and find by name using PDB info
                    var enumLocalsSlot = Marshal.ReadIntPtr(vtable, 13 * IntPtr.Size);
                    var enumLocals = Marshal.GetDelegateForFunctionPointer<EnumerateLocalVariablesDelegate>(enumLocalsSlot);
                    int hr = enumLocals(ilFramePtr, out IntPtr valueEnumPtr);
                    if (hr >= 0 && valueEnumPtr != IntPtr.Zero)
                    {
                        var enumVtable = Marshal.ReadIntPtr(valueEnumPtr);
                        var getCountSlot = Marshal.ReadIntPtr(enumVtable, 6 * IntPtr.Size);
                        var getCount = Marshal.GetDelegateForFunctionPointer<ValueEnumGetCountDelegate>(getCountSlot);
                        getCount(valueEnumPtr, out uint count);
                        varCount = (int)count;
                        Marshal.Release(valueEnumPtr);
                    }

                    // Try PDB names first
                    Dictionary<int, string> pdbNames = null;
                    if (_sourceMapper != null)
                        pdbNames = _sourceMapper.GetLocalVariableNames(frameInfo.FunctionToken);

                    if (pdbNames != null)
                    {
                        foreach (var kvp in pdbNames)
                        {
                            if (string.Equals(kvp.Value, varName, StringComparison.Ordinal))
                            {
                                varIndex = kvp.Key;
                                break;
                            }
                        }
                    }

                    // Fall back to local_N naming
                    if (varIndex < 0 && varName.StartsWith("local_") &&
                        int.TryParse(varName.Substring(6), out int idx) && idx >= 0 && idx < varCount)
                    {
                        varIndex = idx;
                    }
                }
                else // Arguments
                {
                    // Arguments are named arg_N
                    if (varName.StartsWith("arg_") &&
                        int.TryParse(varName.Substring(4), out int idx) && idx >= 0)
                    {
                        varIndex = idx;
                    }
                }

                if (varIndex < 0)
                    return CreateSetVariableError(request, $"Variable '{varName}' not found in current scope");

                // Get the ICorDebugValue for this variable
                IntPtr valuePtr = IntPtr.Zero;
                if (scopeInfo.Kind == ScopeKind.Locals)
                {
                    var getLocalSlot = Marshal.ReadIntPtr(vtable, 14 * IntPtr.Size);
                    var getLocal = Marshal.GetDelegateForFunctionPointer<GetLocalVariableDelegate>(getLocalSlot);
                    int hr = getLocal(ilFramePtr, (uint)varIndex, out valuePtr);
                    if (hr < 0 || valuePtr == IntPtr.Zero)
                        return CreateSetVariableError(request, "Failed to access local variable");
                }
                else
                {
                    var getArgSlot = Marshal.ReadIntPtr(vtable, 16 * IntPtr.Size);
                    var getArg = Marshal.GetDelegateForFunctionPointer<GetArgumentDelegate>(getArgSlot);
                    int hr = getArg(ilFramePtr, (uint)varIndex, out valuePtr);
                    if (hr < 0 || valuePtr == IntPtr.Zero)
                        return CreateSetVariableError(request, "Failed to access argument variable");
                }

                try
                {
                    // Get the element type of the variable
                    var valVtable = Marshal.ReadIntPtr(valuePtr);
                    var getTypeSlot = Marshal.ReadIntPtr(valVtable, 3 * IntPtr.Size);
                    var getType = Marshal.GetDelegateForFunctionPointer<ValueGetTypeDelegate>(getTypeSlot);
                    getType(valuePtr, out int elementType);

                    var getSizeSlot = Marshal.ReadIntPtr(valVtable, 4 * IntPtr.Size);
                    var getSize = Marshal.GetDelegateForFunctionPointer<ValueGetSizeDelegate>(getSizeSlot);
                    getSize(valuePtr, out uint size);

                    string typeName = CorElementTypeToString(elementType);

                    // Only support primitive types for v1
                    if (!IsSupportedPrimitiveType(elementType))
                        return CreateSetVariableError(request, $"Setting values of type '{typeName}' is not supported. Only primitive types (int, float, bool, char, byte) are supported.");

                    // QI for ICorDebugGenericValue
                    Guid iidGenericValue = new Guid("CC7BCAF8-8A68-11D2-983C-0000F808342D");
                    int qiHr = Marshal.QueryInterface(valuePtr, ref iidGenericValue, out IntPtr genericPtr);
                    if (qiHr < 0 || genericPtr == IntPtr.Zero)
                        return CreateSetVariableError(request, "Cannot modify this variable (not a generic value)");

                    try
                    {
                        // Parse the new value string into raw bytes
                        IntPtr buffer = Marshal.AllocHGlobal((int)size);
                        try
                        {
                            if (!TryParsePrimitiveValue(newValue, elementType, buffer, size))
                                return CreateSetVariableError(request, $"Cannot parse '{newValue}' as {typeName}");

                            // ICorDebugGenericValue::SetValue is slot 7 (IUnknown(3) + ICorDebugValue(3) + GetValue(0) + SetValue(1))
                            var genVtable = Marshal.ReadIntPtr(genericPtr);
                            var setValueSlot = Marshal.ReadIntPtr(genVtable, 7 * IntPtr.Size);
                            var setValue = Marshal.GetDelegateForFunctionPointer<GenericValueSetValueDelegate>(setValueSlot);

                            int hr2 = setValue(genericPtr, buffer);
                            if (hr2 < 0)
                                return CreateSetVariableError(request, $"Failed to set variable value (HRESULT: 0x{hr2:X8})");

                            // Read back the value to confirm
                            string confirmedValue = FormatPrimitiveValue(elementType, buffer, size);

                            var response = CreateResponse(request, true);
                            response.Body = new Dictionary<string, object>
                            {
                                ["value"] = confirmedValue,
                                ["type"] = typeName,
                                ["variablesReference"] = 0
                            };
                            return response;
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(buffer);
                        }
                    }
                    finally
                    {
                        Marshal.Release(genericPtr);
                    }
                }
                finally
                {
                    Marshal.Release(valuePtr);
                }
            }
            catch (Exception ex)
            {
                return CreateSetVariableError(request, $"Error setting variable: {ex.Message}");
            }
        }

        private DAPResponse CreateSetVariableError(DAPMessage request, string message)
        {
            var response = CreateResponse(request, false);
            response.Body = new Dictionary<string, object>
            {
                ["error"] = new Dictionary<string, object>
                {
                    ["id"] = 1,
                    ["format"] = message
                }
            };
            response.Message = message;
            return response;
        }

        /// <summary>
        /// Check if the CorElementType is a supported primitive type for SetVariable.
        /// </summary>
        private static bool IsSupportedPrimitiveType(int elementType)
        {
            return elementType switch
            {
                0x02 => true, // BOOLEAN
                0x03 => true, // CHAR
                0x04 => true, // I1 (SByte)
                0x05 => true, // U1 (Byte)
                0x06 => true, // I2 (Int16)
                0x07 => true, // U2 (UInt16)
                0x08 => true, // I4 (Int32)
                0x09 => true, // U4 (UInt32)
                0x0A => true, // I8 (Int64)
                0x0B => true, // U8 (UInt64)
                0x0C => true, // R4 (Single)
                0x0D => true, // R8 (Double)
                _ => false
            };
        }

        /// <summary>
        /// Parse a user-provided string value into raw bytes for a primitive CLR type.
        /// Writes the parsed bytes directly into the provided buffer.
        /// </summary>
        private static bool TryParsePrimitiveValue(string text, int elementType, IntPtr buffer, uint size)
        {
            try
            {
                switch (elementType)
                {
                    case 0x02: // BOOLEAN
                        {
                            bool val;
                            if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) || text == "1")
                                val = true;
                            else if (string.Equals(text, "false", StringComparison.OrdinalIgnoreCase) || text == "0")
                                val = false;
                            else
                                return false;
                            Marshal.WriteByte(buffer, val ? (byte)1 : (byte)0);
                            return true;
                        }
                    case 0x03: // CHAR
                        {
                            char val;
                            if (text.Length == 1)
                                val = text[0];
                            else if (text.Length == 3 && text[0] == '\'' && text[2] == '\'')
                                val = text[1];
                            else
                                return false;
                            Marshal.WriteInt16(buffer, (short)val);
                            return true;
                        }
                    case 0x04: // I1 (SByte)
                        {
                            if (!sbyte.TryParse(text, out sbyte val)) return false;
                            Marshal.WriteByte(buffer, (byte)val);
                            return true;
                        }
                    case 0x05: // U1 (Byte)
                        {
                            if (!byte.TryParse(text, out byte val)) return false;
                            Marshal.WriteByte(buffer, val);
                            return true;
                        }
                    case 0x06: // I2 (Int16)
                        {
                            if (!short.TryParse(text, out short val)) return false;
                            Marshal.WriteInt16(buffer, val);
                            return true;
                        }
                    case 0x07: // U2 (UInt16)
                        {
                            if (!ushort.TryParse(text, out ushort val)) return false;
                            Marshal.WriteInt16(buffer, (short)val);
                            return true;
                        }
                    case 0x08: // I4 (Int32)
                        {
                            if (!int.TryParse(text, out int val)) return false;
                            Marshal.WriteInt32(buffer, val);
                            return true;
                        }
                    case 0x09: // U4 (UInt32)
                        {
                            if (!uint.TryParse(text, out uint val)) return false;
                            Marshal.WriteInt32(buffer, (int)val);
                            return true;
                        }
                    case 0x0A: // I8 (Int64)
                        {
                            if (!long.TryParse(text, out long val)) return false;
                            Marshal.WriteInt64(buffer, val);
                            return true;
                        }
                    case 0x0B: // U8 (UInt64)
                        {
                            if (!ulong.TryParse(text, out ulong val)) return false;
                            Marshal.WriteInt64(buffer, (long)val);
                            return true;
                        }
                    case 0x0C: // R4 (Single)
                        {
                            if (!float.TryParse(text, out float val)) return false;
                            float[] arr = new float[] { val };
                            Marshal.Copy(arr, 0, buffer, 1);
                            return true;
                        }
                    case 0x0D: // R8 (Double)
                        {
                            if (!double.TryParse(text, out double val)) return false;
                            double[] arr = new double[] { val };
                            Marshal.Copy(arr, 0, buffer, 1);
                            return true;
                        }
                    default:
                        return false;
                }
            }
            catch
            {
                return false;
            }
        }

        private DAPResponse HandleContinue(DAPMessage request)
        {
            ClearDebugState();

            var args = request.Arguments;
            int threadId = args.TryGetProperty("threadId", out var tidProp) ? tidProp.GetInt32() : 0;
            bool singleThread = args.TryGetProperty("singleThread", out var stProp) && stProp.GetBoolean();

            // Apply frozen thread states before continuing
            ApplyFrozenThreadStates();

            RawContinueProcess();

            var response = CreateResponse(request, true);
            response.Body = new Dictionary<string, object>
            {
                ["allThreadsContinued"] = !singleThread
            };
            return response;
        }

        private DAPResponse HandleNext(DAPMessage request) // Step Over
        {
            ClearDebugState();

            var args = request.Arguments;
            int threadId = args.TryGetProperty("threadId", out var tidProp) ? tidProp.GetInt32() : 0;

            try
            {
                // Use the requested thread, not just the first one
                var thread = GetThreadForId(threadId) ?? GetFirstThread();
                if (thread != null)
                {
                    RawCreateStepperAndStep(thread, stepInto: false);
                    ApplyFrozenThreadStates();
                    RawContinueProcess();
                }
            }
            catch (Exception ex)
            {
                _ = SendEventAsync("output", new Dictionary<string, object>
                {
                    ["category"] = "stderr",
                    ["output"] = $"Step Over failed: {ex.Message}\n"
                });
            }

            return CreateResponse(request, true);
        }

        private DAPResponse HandleStepIn(DAPMessage request)
        {
            ClearDebugState();

            var args = request.Arguments;
            int threadId = args.TryGetProperty("threadId", out var tidProp) ? tidProp.GetInt32() : 0;

            try
            {
                var thread = GetThreadForId(threadId) ?? GetFirstThread();
                if (thread != null)
                {
                    RawCreateStepperAndStep(thread, stepInto: true);
                    ApplyFrozenThreadStates();
                    RawContinueProcess();
                }
            }
            catch (Exception ex)
            {
                _ = SendEventAsync("output", new Dictionary<string, object>
                {
                    ["category"] = "stderr",
                    ["output"] = $"Step In failed: {ex.Message}\n"
                });
            }

            return CreateResponse(request, true);
        }

        private DAPResponse HandleStepOut(DAPMessage request)
        {
            ClearDebugState();

            var args = request.Arguments;
            int threadId = args.TryGetProperty("threadId", out var tidProp) ? tidProp.GetInt32() : 0;

            try
            {
                var thread = GetThreadForId(threadId) ?? GetFirstThread();
                if (thread != null)
                {
                    RawCreateStepperAndStepOut(thread);
                    ApplyFrozenThreadStates();
                    RawContinueProcess();
                }
            }
            catch (Exception ex)
            {
                _ = SendEventAsync("output", new Dictionary<string, object>
                {
                    ["category"] = "stderr",
                    ["output"] = $"Step Out failed: {ex.Message}\n"
                });
            }

            return CreateResponse(request, true);
        }

        private DAPResponse HandlePause(DAPMessage request)
        {
            _process?.Stop();

            // Determine which thread to report as stopped
            int threadId = 0;
            var args = request.Arguments;
            if (args.ValueKind == System.Text.Json.JsonValueKind.Object &&
                args.TryGetProperty("threadId", out var tidProp))
            {
                threadId = tidProp.GetInt32();
            }

            // If no specific thread requested, pick the first one
            if (threadId == 0 && _process?.Threads.Count > 0)
                threadId = _process.Threads.Keys.First();
            if (threadId == 0)
                threadId = 1;

            _stoppedThreadId = threadId;

            _ = Task.Run(async () =>
            {
                await SendEventAsync("stopped", new Dictionary<string, object>
                {
                    ["reason"] = "pause",
                    ["threadId"] = threadId,
                    ["allThreadsStopped"] = true
                });
            });

            return CreateResponse(request, true);
        }

        private DAPResponse HandleEvaluate(DAPMessage request)
        {
            var args = request.Arguments;
            string expression = args.TryGetProperty("expression", out var exprProp) ? exprProp.GetString() : "";
            int frameId = args.TryGetProperty("frameId", out var frameProp) ? frameProp.GetInt32() : 0;

            string resultValue = "<not available>";
            int resultRef = 0;

            try
            {
                if (_frameInfoMap.TryGetValue(frameId, out var frameInfo) && frameInfo.ManagedFrame != null)
                {
                    // Search locals for a matching variable name
                    var locals = _variableInspector.GetLocals(frameInfo.ManagedFrame);
                    var match = locals.FirstOrDefault(v =>
                        string.Equals(v.Name, expression, StringComparison.OrdinalIgnoreCase));

                    if (match == null)
                    {
                        // Try arguments
                        var arguments = _variableInspector.GetArguments(frameInfo.ManagedFrame);
                        match = arguments.FirstOrDefault(v =>
                            string.Equals(v.Name, expression, StringComparison.OrdinalIgnoreCase));
                    }

                    if (match != null)
                    {
                        resultValue = match.Value;
                        resultRef = match.VariablesReference;
                    }
                }
            }
            catch (Exception ex)
            {
                resultValue = $"<error: {ex.Message}>";
            }

            var response = CreateResponse(request, true);
            response.Body = new Dictionary<string, object>
            {
                ["result"] = resultValue,
                ["variablesReference"] = resultRef
            };
            return response;
        }

        private DAPResponse HandleSetExceptionBreakpoints(DAPMessage request)
        {
            var args = request.Arguments;
            var filters = new List<string>();

            if (args.TryGetProperty("filters", out var filterArray))
            {
                foreach (var f in filterArray.EnumerateArray())
                {
                    var filterStr = f.GetString();
                    if (!string.IsNullOrEmpty(filterStr))
                        filters.Add(filterStr);
                }
            }

            _exceptionFilterAll = filters.Contains("all");
            _exceptionFilterUncaught = filters.Contains("uncaught");

            var response = CreateResponse(request, true);
            response.Body = new Dictionary<string, object>
            {
                ["breakpoints"] = filters.Select(f => new Dictionary<string, object>
                {
                    ["verified"] = true,
                    ["message"] = f == "all" ? "Break on all exceptions" : "Break on uncaught exceptions"
                }).ToList()
            };
            return response;
        }

        private DAPResponse HandleExceptionInfo(DAPMessage request)
        {
            var response = CreateResponse(request, true);

            // Determine breakMode based on whether the exception was unhandled
            string breakMode = _lastExceptionIsUnhandled ? "unhandled" : "always";

            // Build the details object with the full description
            var details = new Dictionary<string, object>
            {
                ["message"] = _lastExceptionMessage ?? "",
                ["typeName"] = _lastExceptionType ?? "Exception"
            };

            // Try to read stack trace from the current thread's exception object
            string stackTrace = ReadExceptionStackTrace();
            if (!string.IsNullOrEmpty(stackTrace))
            {
                details["stackTrace"] = stackTrace;
            }

            response.Body = new Dictionary<string, object>
            {
                ["exceptionId"] = _lastExceptionType ?? "Exception",
                ["description"] = _lastExceptionMessage ?? _lastExceptionDescription ?? "An exception was thrown",
                ["breakMode"] = breakMode,
                ["details"] = details
            };
            return response;
        }

        /// <summary>
        /// Attempt to read the StackTrace string from the current exception object on the thread.
        /// Uses ICorDebugThread::GetCurrentException via raw vtable, then reads the _stackTraceString field.
        /// </summary>
        private string ReadExceptionStackTrace()
        {
            try
            {
                var thread = GetFirstThread();
                if (thread == null) return null;

                var threadPtr = Marshal.GetIUnknownForObject(thread);
                try
                {
                    var vtable = Marshal.ReadIntPtr(threadPtr);

                    // ICorDebugThread::GetCurrentException is slot 10 (IUnknown(3) + 7)
                    var getCurrentExceptionSlot = Marshal.ReadIntPtr(vtable, 10 * IntPtr.Size);
                    var getCurrentException = Marshal.GetDelegateForFunctionPointer<GetCurrentExceptionDelegate>(getCurrentExceptionSlot);
                    int hr = getCurrentException(threadPtr, out IntPtr exValuePtr);
                    if (hr < 0 || exValuePtr == IntPtr.Zero) return null;

                    try
                    {
                        // Dereference the reference value to get the object
                        Guid iidRefVal = new Guid("CC7BCAF9-8A68-11D2-983C-0000F808342D");
                        int qiHr = Marshal.QueryInterface(exValuePtr, ref iidRefVal, out IntPtr refPtr);
                        if (qiHr < 0 || refPtr == IntPtr.Zero) return null;

                        try
                        {
                            var refObj = (ICorDebugReferenceValue)Marshal.GetObjectForIUnknown(refPtr);
                            refObj.IsNull(out bool isNull);
                            if (isNull) return null;

                            refObj.Dereference(out ICorDebugValue actualValue);
                            if (actualValue is not ICorDebugObjectValue objVal) return null;

                            // Get the class and metadata to find _stackTraceString field
                            objVal.GetClass(out ICorDebugClass cls);
                            if (cls == null) return null;

                            cls.GetModule(out ICorDebugModule module);
                            cls.GetToken(out uint typeDefToken);
                            if (module == null) return null;

                            Guid imdImportGuid = new Guid("7DAC8207-D3AE-4C75-9B67-92801A497D44");
                            module.GetMetaDataInterface(ref imdImportGuid, out object mdObj);
                            if (mdObj is not IMetaDataImport mdImport) return null;

                            return ReadStringField(mdImport, objVal, cls, typeDefToken, "_stackTraceString");
                        }
                        finally
                        {
                            Marshal.Release(refPtr);
                        }
                    }
                    finally
                    {
                        Marshal.Release(exValuePtr);
                    }
                }
                finally
                {
                    Marshal.Release(threadPtr);
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Read a string field by name from an exception object, walking up the type hierarchy.
        /// </summary>
        private static string ReadStringField(IMetaDataImport mdImport, ICorDebugObjectValue objVal, ICorDebugClass cls, uint typeDefToken, string targetFieldName)
        {
            uint currentTypeDef = typeDefToken;

            for (int depth = 0; depth < 10; depth++)
            {
                IntPtr hEnum = IntPtr.Zero;
                try
                {
                    uint[] fieldTokens = new uint[32];
                    while (true)
                    {
                        int hr = mdImport.EnumFields(ref hEnum, currentTypeDef, fieldTokens, (uint)fieldTokens.Length, out uint fetched);
                        if (hr < 0 || fetched == 0) break;

                        for (uint i = 0; i < fetched; i++)
                        {
                            uint fieldToken = fieldTokens[i];
                            char[] fieldNameBuf = new char[256];
                            hr = mdImport.GetFieldProps(
                                fieldToken, out _, fieldNameBuf, (uint)fieldNameBuf.Length, out uint fieldNameLen,
                                out _, out _, out _, out _, out _, out _);

                            if (hr < 0 || fieldNameLen == 0) continue;

                            string fieldName = new string(fieldNameBuf, 0, (int)(fieldNameLen - 1));
                            if (fieldName == targetFieldName)
                            {
                                try
                                {
                                    objVal.GetFieldValue(cls, fieldToken, out ICorDebugValue fieldValue);
                                    if (fieldValue == null) return null;

                                    if (fieldValue is ICorDebugReferenceValue msgRef)
                                    {
                                        msgRef.IsNull(out bool isNull2);
                                        if (isNull2) return null;
                                        msgRef.Dereference(out ICorDebugValue derefVal);
                                        if (derefVal is ICorDebugStringValue strVal)
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
                                catch { }
                            }
                        }

                        if (fetched < (uint)fieldTokens.Length) break;
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
                if (hr2 < 0 || extendsToken == 0) break;

                if ((extendsToken & 0xFF000000) == 0x02000000)
                    currentTypeDef = extendsToken;
                else
                    break;
            }

            return null;
        }

        private DAPResponse HandleDisconnect(DAPMessage request)
        {
            try
            {
                var args = request.Arguments;
                bool terminateDebuggee = true; // default to terminate
                if (args.ValueKind == System.Text.Json.JsonValueKind.Object &&
                    args.TryGetProperty("terminateDebuggee", out var termProp))
                {
                    terminateDebuggee = termProp.GetBoolean();
                }

                if (_process != null)
                {
                    try
                    {
                        if (terminateDebuggee)
                            _process.Terminate();
                        else
                            _process.Detach();
                    }
                    catch { }
                    try { _process.Dispose(); } catch { }
                    _process = null;
                }

                try { _sourceMapper?.Dispose(); } catch { }
                _sourceMapper = null;
                _breakpointManager?.ClearAll();
            }
            catch { }
            return CreateResponse(request, true);
        }

        // =====================================================================
        // Event handlers (wired to NetDebugProcess events)
        // =====================================================================

        private async void OnBreakpointHit(object sender, BreakpointHitEventArgs e)
        {
            try
            {
                int dapThreadId = MapToDapThreadId(e.ThreadId);
                string reason = "breakpoint";

                // Check if this is a temporary step breakpoint
                if (_isStepBreakpoint)
                {
                    reason = "step";
                    RemoveTempStepBreakpoint();
                }

                // For real breakpoints (not steps), evaluate all conditions
                if (reason == "breakpoint")
                {
                    var entry = FindBreakpointEntryForCurrentLocation();
                    if (entry != null)
                    {
                        // Always increment hit count
                        entry.HitCount++;

                        // --- Conditional breakpoint: evaluate expression ---
                        if (!string.IsNullOrWhiteSpace(entry.Condition))
                        {
                            bool conditionMet = EvaluateConditionExpression(entry.Condition);
                            if (!conditionMet)
                            {
                                // Condition is false — skip this breakpoint and continue
                                RawContinueProcess();
                                return;
                            }
                        }

                        // --- Hit condition: check hit count ---
                        if (!string.IsNullOrWhiteSpace(entry.HitCondition))
                        {
                            if (!EvaluateHitCondition(entry.HitCondition, entry.HitCount))
                            {
                                // Hit condition not met — resume execution silently
                                RawContinueProcess();
                                return;
                            }
                        }

                        // --- Log message (logpoint): output and continue ---
                        if (!string.IsNullOrWhiteSpace(entry.LogMessage))
                        {
                            string output = InterpolateLogMessage(entry.LogMessage);
                            await SendEventAsync("output", new Dictionary<string, object>
                            {
                                ["category"] = "stdout",
                                ["output"] = output + "\n"
                            });
                            // Logpoints do not stop — continue execution
                            RawContinueProcess();
                            return;
                        }
                    }
                }

                await SendEventAsync("stopped", new Dictionary<string, object>
                {
                    ["reason"] = reason,
                    ["threadId"] = dapThreadId,
                    ["allThreadsStopped"] = true
                });
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"OnBreakpointHit error: {ex.Message}");
            }
        }

        /// <summary>
        /// Resolve the current execution point to a ClrBreakpointEntry by reading the
        /// active frame's function token / IL offset and mapping to a source location.
        /// </summary>
        private ClrBreakpointEntry FindBreakpointEntryForCurrentLocation()
        {
            try
            {
                var thread = GetFirstThread();
                if (thread == null || _sourceMapper == null) return null;

                var threadPtr = Marshal.GetIUnknownForObject(thread);
                try
                {
                    var vtable = Marshal.ReadIntPtr(threadPtr);
                    var getActiveFrameSlot = Marshal.ReadIntPtr(vtable, 15 * IntPtr.Size);
                    var getActiveFrame = Marshal.GetDelegateForFunctionPointer<GetActiveFrameDelegate>(getActiveFrameSlot);
                    int hr = getActiveFrame(threadPtr, out var framePtr);
                    if (hr < 0 || framePtr == IntPtr.Zero) return null;

                    try
                    {
                        var frameVtable = Marshal.ReadIntPtr(framePtr);
                        var getFnTokenSlot = Marshal.ReadIntPtr(frameVtable, 6 * IntPtr.Size);
                        var getFnToken = Marshal.GetDelegateForFunctionPointer<GetFunctionTokenDelegate>(getFnTokenSlot);
                        getFnToken(framePtr, out uint functionToken);

                        Guid iidILFrame = new Guid("03E26311-4F76-11D3-88C6-006097945418");
                        int qiHr = Marshal.QueryInterface(framePtr, ref iidILFrame, out IntPtr ilFramePtr);
                        uint ilOffset = 0;
                        if (qiHr >= 0 && ilFramePtr != IntPtr.Zero)
                        {
                            var ilFrameVtable = Marshal.ReadIntPtr(ilFramePtr);
                            var getIPSlot = Marshal.ReadIntPtr(ilFrameVtable, 11 * IntPtr.Size);
                            var getIP = Marshal.GetDelegateForFunctionPointer<GetIPDelegate>(getIPSlot);
                            getIP(ilFramePtr, out ilOffset, out _);
                            Marshal.Release(ilFramePtr);
                        }

                        var loc = _sourceMapper.GetSourceLocation((int)functionToken, (int)ilOffset);
                        if (loc == null) return null;

                        return _breakpointManager.FindByFileAndLine(loc.Value.file, loc.Value.line);
                    }
                    finally
                    {
                        Marshal.Release(framePtr);
                    }
                }
                finally
                {
                    Marshal.Release(threadPtr);
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Evaluate a DAP hit condition string against the current hit count.
        /// Supported formats:
        ///   "5"   — break when hit count equals 5
        ///   ">5"  — break when hit count is greater than 5
        ///   ">=5" — break when hit count is greater than or equal to 5
        ///   "&lt;5"  — break when hit count is less than 5
        ///   "%3"  — break every 3rd hit (hit count is a multiple of 3)
        /// </summary>
        private static bool EvaluateHitCondition(string condition, int hitCount)
        {
            if (string.IsNullOrWhiteSpace(condition)) return true;

            condition = condition.Trim();

            if (condition.StartsWith(">="))
            {
                return int.TryParse(condition.Substring(2).Trim(), out int n) && hitCount >= n;
            }
            if (condition.StartsWith(">"))
            {
                return int.TryParse(condition.Substring(1).Trim(), out int n) && hitCount > n;
            }
            if (condition.StartsWith("<="))
            {
                return int.TryParse(condition.Substring(2).Trim(), out int n) && hitCount <= n;
            }
            if (condition.StartsWith("<"))
            {
                return int.TryParse(condition.Substring(1).Trim(), out int n) && hitCount < n;
            }
            if (condition.StartsWith("%"))
            {
                return int.TryParse(condition.Substring(1).Trim(), out int n) && n > 0 && (hitCount % n) == 0;
            }
            // Plain number — break when hit count equals value
            return int.TryParse(condition, out int exact) && hitCount == exact;
        }

        private async void OnStepCompleted(object sender, StepCompletedEventArgs e)
        {
            try
            {
                // "Just My Code" — if we landed in non-user code (framework/library),
                // automatically step again until we're back in .bas user code.
                // Uses a retry counter (max 100) to prevent infinite loops in case
                // we never land back in user code.
                const int maxRetries = 100;
                int retryCount = 0;

                while (retryCount < maxRetries)
                {
                    var thread = GetFirstThread();
                    if (thread == null || _sourceMapper == null)
                        break;

                    // Get active frame to extract method token and IL offset
                    bool isUser = false;
                    var threadPtr = Marshal.GetIUnknownForObject(thread);
                    try
                    {
                        var vtable = Marshal.ReadIntPtr(threadPtr);
                        // GetActiveFrame is slot 15 (IUnknown(3) + 12)
                        var getActiveFrameSlot = Marshal.ReadIntPtr(vtable, 15 * IntPtr.Size);
                        var getActiveFrame = Marshal.GetDelegateForFunctionPointer<GetActiveFrameDelegate>(getActiveFrameSlot);
                        int hr = getActiveFrame(threadPtr, out var framePtr);
                        if (hr >= 0 && framePtr != IntPtr.Zero)
                        {
                            try
                            {
                                // GetFunctionToken on ICorDebugFrame (slot 6)
                                var frameVtable = Marshal.ReadIntPtr(framePtr);
                                var getFnTokenSlot = Marshal.ReadIntPtr(frameVtable, 6 * IntPtr.Size);
                                var getFnToken = Marshal.GetDelegateForFunctionPointer<GetFunctionTokenDelegate>(getFnTokenSlot);
                                getFnToken(framePtr, out uint functionToken);

                                // GetIP on ICorDebugILFrame — must QueryInterface
                                Guid iidILFrame = new Guid("03E26311-4F76-11D3-88C6-006097945418");
                                int qiHr = Marshal.QueryInterface(framePtr, ref iidILFrame, out IntPtr ilFramePtr);
                                uint ilOffset = 0;
                                if (qiHr >= 0 && ilFramePtr != IntPtr.Zero)
                                {
                                    var ilFrameVtable = Marshal.ReadIntPtr(ilFramePtr);
                                    var getIPSlot = Marshal.ReadIntPtr(ilFrameVtable, 11 * IntPtr.Size);
                                    var getIP = Marshal.GetDelegateForFunctionPointer<GetIPDelegate>(getIPSlot);
                                    getIP(ilFramePtr, out ilOffset, out _);
                                    Marshal.Release(ilFramePtr);
                                }

                                isUser = IsUserCode((int)functionToken, (int)ilOffset);
                            }
                            finally
                            {
                                Marshal.Release(framePtr);
                            }
                        }
                    }
                    catch
                    {
                        // If frame inspection fails, assume not user code on first try,
                        // but stop to avoid infinite loop
                        break;
                    }
                    finally
                    {
                        Marshal.Release(threadPtr);
                    }

                    if (isUser)
                        break; // We're in user code — send the stopped event

                    // Not user code — step again automatically
                    try
                    {
                        RawCreateStepperAndStep(thread, stepInto: false);
                        RawContinueProcess();
                        return; // Don't send stopped event; the next StepCompleted callback will re-check
                    }
                    catch
                    {
                        // If re-stepping fails, fall through and report stopped
                        break;
                    }
                }

                int dapThreadId = MapToDapThreadId(e.ThreadId);
                await SendEventAsync("stopped", new Dictionary<string, object>
                {
                    ["reason"] = "step",
                    ["threadId"] = dapThreadId,
                    ["allThreadsStopped"] = true
                });
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"OnStepCompleted error: {ex.Message}");
            }
        }

        private async void OnExceptionThrown(object sender, ExceptionThrownEventArgs e)
        {
            try
            {
                // Store exception details for the exceptionInfo request
                _lastExceptionType = e.ExceptionType ?? "Exception";
                _lastExceptionDescription = e.ExceptionMessage ?? "Exception";
                _lastExceptionIsUnhandled = e.IsUnhandled;

                // Parse the raw exception message out of the description.
                // ExceptionMessage comes formatted as "First-chance exception: TypeName: message"
                // We want just the message portion for _lastExceptionMessage.
                _lastExceptionMessage = e.ExceptionMessage;
                if (!string.IsNullOrEmpty(e.ExceptionMessage) && !string.IsNullOrEmpty(e.ExceptionType))
                {
                    // Try to extract just the message after "TypeName: "
                    string marker = e.ExceptionType + ": ";
                    int idx = e.ExceptionMessage.IndexOf(marker, StringComparison.Ordinal);
                    if (idx >= 0)
                    {
                        _lastExceptionMessage = e.ExceptionMessage.Substring(idx + marker.Length);
                    }
                }

                // Check if we should break based on exception filters
                bool shouldBreak = e.IsUnhandled
                    ? _exceptionFilterUncaught
                    : _exceptionFilterAll;

                if (shouldBreak)
                {
                    await SendEventAsync("stopped", new Dictionary<string, object>
                    {
                        ["reason"] = "exception",
                        ["description"] = e.ExceptionMessage ?? "Exception",
                        ["text"] = _lastExceptionType,
                        ["threadId"] = e.ThreadId,
                        ["allThreadsStopped"] = true
                    });
                }
                else
                {
                    // Not breaking — continue execution
                    _process?.Continue();
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"OnExceptionThrown error: {ex.Message}");
            }
        }

        private async void OnModuleLoaded(object sender, ModuleLoadedEventArgs e)
        {
            try
            {
                // Track the module
                _loadedModules[e.ModulePath] = e.Module;

                // Try to load PDB for this module if not already loaded
                var pdbPath = Path.ChangeExtension(e.ModulePath, ".pdb");
                if (File.Exists(pdbPath) && _sourceMapper != null)
                {
                    // If we haven't loaded the PDB yet, load it now
                    // SourceMapper.LoadPdb can be called once; check if docs match
                    var docs = _sourceMapper.GetSourceDocuments();
                    if (docs.Count == 0)
                    {
                        _sourceMapper.LoadPdb(pdbPath);
                    }
                }

                // Only try to bind breakpoints on user modules (skip system assemblies)
                bool isSystemModule = e.ModuleName.StartsWith("System.") ||
                    e.ModuleName == "mscorlib.dll" ||
                    e.ModuleName == "netstandard.dll";
                if (!isSystemModule)
                {
                    TryBindPendingBreakpoints(e.Module);
                }

                await SendEventAsync("output", new Dictionary<string, object>
                {
                    ["category"] = "console",
                    ["output"] = $"Module loaded: {e.ModuleName}\n"
                });
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"OnModuleLoaded error: {ex.Message}");
            }
        }

        private async void OnProcessExited(object sender, ProcessExitedEventArgs e)
        {
            try
            {
                await SendEventAsync("exited", new Dictionary<string, object>
                {
                    ["exitCode"] = e.ExitCode
                });
                await SendEventAsync("terminated", new Dictionary<string, object>());
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"OnProcessExited error: {ex.Message}");
            }
        }

        private async void OnThreadCreated(object sender, ThreadEventArgs e)
        {
            try
            {
                await SendEventAsync("thread", new Dictionary<string, object>
                {
                    ["reason"] = "started",
                    ["threadId"] = e.ThreadId
                });
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"OnThreadCreated error: {ex.Message}");
            }
        }

        private async void OnThreadExited(object sender, ThreadEventArgs e)
        {
            try
            {
                await SendEventAsync("thread", new Dictionary<string, object>
                {
                    ["reason"] = "exited",
                    ["threadId"] = e.ThreadId
                });
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"OnThreadExited error: {ex.Message}");
            }
        }

        // =====================================================================
        // Breakpoint binding
        // =====================================================================

        /// <summary>
        /// Try to bind a single breakpoint entry to a CLR breakpoint using the loaded module.
        /// Deactivate all CLR breakpoints for a given source file.
        /// Called before ClearFile when the IDE removes breakpoints during debugging.
        /// </summary>
        private void DeactivateBreakpointsForFile(string filePath)
        {
            var allForFile = _breakpointManager.GetAllForFile(filePath);
            foreach (var bp in allForFile)
            {
                if (bp.ClrBreakpoint is IntPtr bpPtr && bpPtr != IntPtr.Zero)
                {
                    try
                    {
                        // ICorDebugBreakpoint::Activate(false) at vtable slot 3
                        var bpVtable = Marshal.ReadIntPtr(bpPtr);
                        var activateSlot = Marshal.ReadIntPtr(bpVtable, 3 * IntPtr.Size);
                        var activate = Marshal.GetDelegateForFunctionPointer<ActivateBreakpointDelegate>(activateSlot);
                        activate(bpPtr, 0); // 0 = deactivate
                        Marshal.Release(bpPtr);
                    }
                    catch { }
                    // Clear the pointer so it's not released again during cleanup
                    bp.ClrBreakpoint = null;
                }
            }
        }

        /// <summary>
        /// Returns true if the breakpoint was successfully bound.
        /// </summary>
        private bool TryBindBreakpoint(ClrBreakpointEntry entry, int methodToken, int ilOffset, ICorDebugModule targetModule)
        {
            if (targetModule == null)
                return false;

            try
            {
                {
                    var module = targetModule;
                    // Use raw vtable calls to avoid QI issues with .NET Core COM objects
                    // ICorDebugModule::GetFunctionFromToken is vtable slot 6 (after 3 IUnknown slots)
                    var modulePtr = Marshal.GetIUnknownForObject(module);
                    try
                    {
                        // Get vtable
                        var vtable = Marshal.ReadIntPtr(modulePtr);
                        // Slot 9 = IUnknown(3) + GetProcess(0) + GetBaseAddress(1) + GetAssembly(2) + GetName(3) + EnableJITDebugging(4) + EnableClassLoadCallbacks(5) + GetFunctionFromToken(6)
                        var getFunctionSlot = Marshal.ReadIntPtr(vtable, 9 * IntPtr.Size);

                        // Call GetFunctionFromToken(this, methodDef, out ppFunction)
                        var getFunctionFromToken = Marshal.GetDelegateForFunctionPointer<GetFunctionFromTokenDelegate>(getFunctionSlot);
                        int hr = getFunctionFromToken(modulePtr, (uint)methodToken, out var functionPtr);
                        if (hr < 0 || functionPtr == IntPtr.Zero)
                            return false;

                        // ICorDebugFunction::GetILCode is vtable slot 6 (IUnknown(3) + GetModule(0) + GetClass(1) + GetToken(2) + GetILCode(3))
                        var funcVtable = Marshal.ReadIntPtr(functionPtr);
                        var getILCodeSlot = Marshal.ReadIntPtr(funcVtable, 6 * IntPtr.Size);
                        var getILCode = Marshal.GetDelegateForFunctionPointer<GetILCodeDelegate>(getILCodeSlot);
                        hr = getILCode(functionPtr, out var codePtr);
                        if (hr < 0 || codePtr == IntPtr.Zero)
                        {
                            Marshal.Release(functionPtr);
                            return false;
                        }

                        // ICorDebugCode::CreateBreakpoint — vtable slot 7 (IUnknown(3) + IsIL + GetFunction + GetAddress + GetSize + CreateBreakpoint)
                        var codeVtable = Marshal.ReadIntPtr(codePtr);
                        var createBpSlot = Marshal.ReadIntPtr(codeVtable, 7 * IntPtr.Size);
                        var createBreakpointFn = Marshal.GetDelegateForFunctionPointer<CreateBreakpointDelegate>(createBpSlot);
                        hr = createBreakpointFn(codePtr, (uint)ilOffset, out var bpPtr);
                        Marshal.Release(codePtr);
                        Marshal.Release(functionPtr);
                        if (hr < 0 || bpPtr == IntPtr.Zero)
                            return false;

                        // ICorDebugBreakpoint::Activate — vtable slot 3 (IUnknown(3) + Activate(0))
                        var bpVtable = Marshal.ReadIntPtr(bpPtr);
                        var activateSlot = Marshal.ReadIntPtr(bpVtable, 3 * IntPtr.Size);
                        var activate = Marshal.GetDelegateForFunctionPointer<ActivateBreakpointDelegate>(activateSlot);
                        hr = activate(bpPtr, 1); // 1 = true
                        if (hr < 0)
                            return false;

                        // Successfully bound! Store bpPtr so we can deactivate later
                        int actualLine = _sourceMapper?.FindNearestExecutableLine(entry.FilePath, entry.RequestedLine)
                            ?? entry.RequestedLine;
                        Marshal.AddRef(bpPtr); // prevent GC while stored
                        _breakpointManager.MarkBound(entry.Id, actualLine, bpPtr);
                        _breakpointManager.MarkVerified(entry.Id);
                        return true;
                    }
                    finally
                    {
                        Marshal.Release(modulePtr);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"TryBindBreakpoint error: {ex.Message}");
            }

            _breakpointManager.MarkInvalid(entry.Id);
            return false;
        }

        /// <summary>
        /// Attempt to bind all pending breakpoints using the given module.
        /// </summary>
        private void TryBindPendingBreakpoints(ICorDebugModule module)
        {
            var pending = _breakpointManager.GetAllPending();
            foreach (var entry in pending)
            {
                var ilInfo = _sourceMapper?.GetILOffsetForLine(entry.FilePath, entry.RequestedLine);
                if (ilInfo.HasValue)
                {
                    if (TryBindBreakpoint(entry, ilInfo.Value.methodToken, ilInfo.Value.ilOffset, module))
                    {
                        // Send breakpoint changed event to update IDE
                        var bpFilePath = entry.FilePath;
                        var bpId = entry.Id;
                        var bpLine = entry.ActualLine;
                        _ = Task.Run(async () =>
                        {
                            await SendEventAsync("breakpoint", new Dictionary<string, object>
                            {
                                ["reason"] = "changed",
                                ["breakpoint"] = new Dictionary<string, object>
                                {
                                    ["id"] = bpId,
                                    ["verified"] = true,
                                    ["line"] = bpLine,
                                    ["source"] = new Dictionary<string, object>
                                    {
                                        ["path"] = bpFilePath
                                    }
                                }
                            });
                        });
                    }
                }
            }
        }

        // =====================================================================
        // Helpers
        // =====================================================================

        /// <summary>
        /// Map OS thread ID to DAP thread ID. Now uses the real OS thread ID directly.
        /// </summary>
        private int MapToDapThreadId(int osThreadId) => osThreadId;

        /// <summary>
        /// Get the first available ICorDebugThread (for stepping/stack trace when threadId doesn't match)
        /// </summary>
        private ICorDebugThread GetFirstThread()
        {
            if (_process?.Threads.Count > 0)
                return _process.Threads.Values.First();
            return null;
        }

        /// <summary>
        /// Get a specific ICorDebugThread by its OS thread ID.
        /// Falls back to searching the adapter's local thread dictionary,
        /// then the process's tracked threads.
        /// </summary>
        private ICorDebugThread GetThreadForId(int threadId)
        {
            if (threadId <= 0)
                return null;

            // Check adapter-level tracking first
            if (_threads.TryGetValue(threadId, out var thread))
                return thread;

            // Fall back to process-level tracking
            if (_process?.Threads.TryGetValue(threadId, out var pThread) == true)
                return pThread;

            return null;
        }

        /// <summary>
        /// Enumerate all managed threads from ICorDebugProcess via raw vtable calls.
        /// Returns a list of (threadId, ICorDebugThread) pairs.
        /// </summary>
        private List<(int threadId, ICorDebugThread thread)> EnumerateAllThreads()
        {
            var result = new List<(int, ICorDebugThread)>();
            if (_process?.CorDebugProcess == null)
                return result;

            try
            {
                var processPtr = Marshal.GetIUnknownForObject(_process.CorDebugProcess);
                try
                {
                    var vtable = Marshal.ReadIntPtr(processPtr);
                    // ICorDebugController::EnumerateThreads is slot 3+4=7
                    var enumThreadsSlot = Marshal.ReadIntPtr(vtable, 7 * IntPtr.Size);
                    var enumThreadsFn = Marshal.GetDelegateForFunctionPointer<EnumerateThreadsDelegate>(enumThreadsSlot);
                    int hr = enumThreadsFn(processPtr, out IntPtr enumPtr);
                    if (hr < 0 || enumPtr == IntPtr.Zero)
                        return result;

                    try
                    {
                        var enumVtable = Marshal.ReadIntPtr(enumPtr);
                        // ICorDebugThreadEnum::GetCount is slot 6 (IUnknown(3) + Skip(0), Reset(1), Clone(2), GetCount(3))
                        var getCountSlot = Marshal.ReadIntPtr(enumVtable, 6 * IntPtr.Size);
                        var getCount = Marshal.GetDelegateForFunctionPointer<ThreadEnumGetCountDelegate>(getCountSlot);
                        getCount(enumPtr, out uint count);

                        // ICorDebugThreadEnum::Next is slot 7
                        var nextSlot = Marshal.ReadIntPtr(enumVtable, 7 * IntPtr.Size);
                        var nextFn = Marshal.GetDelegateForFunctionPointer<ThreadEnumNextDelegate>(nextSlot);

                        for (uint i = 0; i < count; i++)
                        {
                            hr = nextFn(enumPtr, 1, out IntPtr threadPtr, out uint fetched);
                            if (hr < 0 || fetched == 0 || threadPtr == IntPtr.Zero)
                                break;

                            try
                            {
                                var threadObj = (ICorDebugThread)Marshal.GetObjectForIUnknown(threadPtr);
                                // Get thread ID via raw vtable
                                var threadVtable = Marshal.ReadIntPtr(threadPtr);
                                var getIdSlot = Marshal.ReadIntPtr(threadVtable, 4 * IntPtr.Size); // slot 3+1=4
                                var getId = Marshal.GetDelegateForFunctionPointer<ThreadGetIDDelegate>(getIdSlot);
                                getId(threadPtr, out uint threadId2);
                                result.Add(((int)threadId2, threadObj));
                            }
                            catch
                            {
                                // Skip threads we can't read
                            }
                            finally
                            {
                                Marshal.Release(threadPtr);
                            }
                        }
                    }
                    finally
                    {
                        Marshal.Release(enumPtr);
                    }
                }
                finally
                {
                    Marshal.Release(processPtr);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[DAP] EnumerateAllThreads error: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Try to read a managed thread's name. .NET threads store their name
        /// as a field on System.Threading.Thread, but ICorDebug doesn't expose it directly.
        /// Returns null if the name can't be determined.
        /// </summary>
        private string TryGetThreadName(ICorDebugThread thread)
        {
            // ICorDebug doesn't expose managed thread names directly.
            // A full implementation would need to eval Thread.CurrentThread.Name via ICorDebugEval.
            // For now, return null and use the default naming.
            return null;
        }

        /// <summary>
        /// Apply frozen thread states before continuing.
        /// Frozen threads are set to THREAD_SUSPEND so they don't run.
        /// </summary>
        private void ApplyFrozenThreadStates()
        {
            if (_frozenThreads.Count == 0 || _process?.CorDebugProcess == null)
                return;

            try
            {
                foreach (var frozenId in _frozenThreads)
                {
                    var thread = GetThreadForId(frozenId);
                    if (thread != null)
                    {
                        try
                        {
                            thread.SetDebugState(CorDebugThreadState.THREAD_SUSPEND);
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"[DAP] Failed to freeze thread {frozenId}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[DAP] ApplyFrozenThreadStates error: {ex.Message}");
            }
        }

        /// <summary>
        /// "Just My Code" check — returns true if the current top-of-stack frame
        /// maps to a .bas source file via the PDB, false if it's framework/library code.
        /// </summary>
        private bool IsCurrentFrameUserCode()
        {
            try
            {
                var thread = GetFirstThread();
                if (thread == null || _sourceMapper == null)
                    return false;

                var threadPtr = Marshal.GetIUnknownForObject(thread);
                try
                {
                    var vtable = Marshal.ReadIntPtr(threadPtr);
                    // GetActiveFrame is slot 15 (IUnknown(3) + 12)
                    var getActiveFrameSlot = Marshal.ReadIntPtr(vtable, 15 * IntPtr.Size);
                    var getActiveFrame = Marshal.GetDelegateForFunctionPointer<GetActiveFrameDelegate>(getActiveFrameSlot);
                    int hr = getActiveFrame(threadPtr, out var framePtr);
                    if (hr < 0 || framePtr == IntPtr.Zero)
                        return false;

                    try
                    {
                        // GetFunctionToken on ICorDebugFrame (slot 6)
                        var frameVtable = Marshal.ReadIntPtr(framePtr);
                        var getFnTokenSlot = Marshal.ReadIntPtr(frameVtable, 6 * IntPtr.Size);
                        var getFnToken = Marshal.GetDelegateForFunctionPointer<GetFunctionTokenDelegate>(getFnTokenSlot);
                        getFnToken(framePtr, out uint functionToken);

                        // GetIP on ICorDebugILFrame — must QueryInterface
                        Guid iidILFrame = new Guid("03E26311-4F76-11D3-88C6-006097945418");
                        int qiHr = Marshal.QueryInterface(framePtr, ref iidILFrame, out IntPtr ilFramePtr);
                        uint ilOffset = 0;
                        if (qiHr >= 0 && ilFramePtr != IntPtr.Zero)
                        {
                            var ilFrameVtable = Marshal.ReadIntPtr(ilFramePtr);
                            var getIPSlot = Marshal.ReadIntPtr(ilFrameVtable, 11 * IntPtr.Size);
                            var getIP = Marshal.GetDelegateForFunctionPointer<GetIPDelegate>(getIPSlot);
                            getIP(ilFramePtr, out ilOffset, out _);
                            Marshal.Release(ilFramePtr);
                        }

                        var loc = _sourceMapper.GetSourceLocation((int)functionToken, (int)ilOffset);
                        return loc != null && loc.Value.file.EndsWith(".bas", StringComparison.OrdinalIgnoreCase);
                    }
                    finally
                    {
                        Marshal.Release(framePtr);
                    }
                }
                finally
                {
                    Marshal.Release(threadPtr);
                }
            }
            catch
            {
                // If anything fails reading the frame, assume it's not user code
                // so we keep stepping rather than stopping in an unknown location
                return false;
            }
        }

        /// <summary>
        /// "Just My Code" check for a specific method token and IL offset.
        /// Returns true if the location maps to a .bas source file via the PDB.
        /// </summary>
        private bool IsUserCode(int methodToken, int ilOffset)
        {
            var loc = _sourceMapper?.GetSourceLocation(methodToken, ilOffset);
            return loc != null && loc.Value.file.EndsWith(".bas", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Evaluate a conditional breakpoint expression. Returns true if condition is met.
        /// Simple implementation: treats "true"/"True"/"1" as true, "false"/"False"/"0" as false.
        /// For variable names, attempts to look them up in the current frame.
        /// </summary>
        private bool EvaluateConditionExpression(string condition)
        {
            if (string.IsNullOrWhiteSpace(condition))
                return true;

            try
            {
                // Get the current thread and active IL frame
                var thread = GetFirstThread();
                if (thread == null) return true; // default to break on failure

                var threadPtr = Marshal.GetIUnknownForObject(thread);
                try
                {
                    var vtable = Marshal.ReadIntPtr(threadPtr);
                    var getActiveFrameSlot = Marshal.ReadIntPtr(vtable, 15 * IntPtr.Size);
                    var getActiveFrame = Marshal.GetDelegateForFunctionPointer<GetActiveFrameDelegate>(getActiveFrameSlot);
                    int hr = getActiveFrame(threadPtr, out var framePtr);
                    if (hr < 0 || framePtr == IntPtr.Zero) return true;

                    try
                    {
                        // Get function token for PDB variable name lookup
                        var frameVtable = Marshal.ReadIntPtr(framePtr);
                        var getFnTokenSlot = Marshal.ReadIntPtr(frameVtable, 6 * IntPtr.Size);
                        var getFnToken = Marshal.GetDelegateForFunctionPointer<GetFunctionTokenDelegate>(getFnTokenSlot);
                        getFnToken(framePtr, out uint functionToken);

                        // Get the ICorDebugILFrame managed object
                        Guid iidILFrame = new Guid("03E26311-4F76-11D3-88C6-006097945418");
                        int qiHr = Marshal.QueryInterface(framePtr, ref iidILFrame, out IntPtr ilFramePtr);
                        if (qiHr < 0 || ilFramePtr == IntPtr.Zero) return true;

                        try
                        {
                            var ilFrame = (ICorDebugILFrame)Marshal.GetObjectForIUnknown(ilFramePtr);

                            // Build a name→value dictionary from locals and arguments
                            var varValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                            // Get PDB local variable names (slot index → real name)
                            var localNames = _sourceMapper?.GetLocalVariableNames((int)functionToken)
                                             ?? new Dictionary<int, string>();

                            // Collect locals
                            var locals = _variableInspector.GetLocals(ilFrame);
                            for (int i = 0; i < locals.Count; i++)
                            {
                                var dv = locals[i];
                                // Map PDB name to value; fall back to indexed name
                                if (localNames.TryGetValue(i, out string realName))
                                    varValues[realName] = dv.Value;
                                varValues[dv.Name] = dv.Value; // also store local_N
                            }

                            // Collect arguments
                            var args = _variableInspector.GetArguments(ilFrame);
                            for (int i = 0; i < args.Count; i++)
                            {
                                var dv = args[i];
                                varValues[dv.Name] = dv.Value;
                            }

                            return EvaluateConditionWithVariables(condition.Trim(), varValues);
                        }
                        finally
                        {
                            Marshal.Release(ilFramePtr);
                        }
                    }
                    finally
                    {
                        Marshal.Release(framePtr);
                    }
                }
                finally
                {
                    Marshal.Release(threadPtr);
                }
            }
            catch
            {
                // On any failure, default to breaking
                return true;
            }
        }

        /// <summary>
        /// Parse and evaluate a condition string against variable values.
        /// Supported forms:
        ///   "varname"              — truthy check (non-zero, non-null, non-empty, not "false")
        ///   "varname == value"     — equality
        ///   "varname != value"     — inequality
        ///   "varname > value"      — greater than
        ///   "varname < value"      — less than
        ///   "varname >= value"     — greater than or equal
        ///   "varname <= value"     — less than or equal
        /// Values can be integers, doubles, or quoted strings.
        /// </summary>
        private static bool EvaluateConditionWithVariables(string condition, Dictionary<string, string> varValues)
        {
            // Try to parse as "lhs op rhs" — check multi-char operators first
            string[] operators = { "==", "!=", ">=", "<=", ">", "<" };
            foreach (var op in operators)
            {
                int idx = condition.IndexOf(op, StringComparison.Ordinal);
                if (idx > 0)
                {
                    string lhs = condition.Substring(0, idx).Trim();
                    string rhs = condition.Substring(idx + op.Length).Trim();

                    // Look up the left-hand side variable
                    if (!varValues.TryGetValue(lhs, out string varValue))
                        return true; // variable not found — default to break

                    // Strip surrounding quotes from the variable value (strings come as "\"value\"")
                    string cleanVarValue = StripQuotes(varValue);
                    string cleanRhs = StripQuotes(rhs);

                    // Try numeric comparison first
                    if (double.TryParse(cleanVarValue, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out double numVar) &&
                        double.TryParse(cleanRhs, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out double numRhs))
                    {
                        return op switch
                        {
                            "==" => Math.Abs(numVar - numRhs) < double.Epsilon,
                            "!=" => Math.Abs(numVar - numRhs) >= double.Epsilon,
                            ">"  => numVar > numRhs,
                            "<"  => numVar < numRhs,
                            ">=" => numVar >= numRhs,
                            "<=" => numVar <= numRhs,
                            _    => true
                        };
                    }

                    // Fall back to string comparison
                    int cmp = string.Compare(cleanVarValue, cleanRhs, StringComparison.OrdinalIgnoreCase);
                    return op switch
                    {
                        "==" => cmp == 0,
                        "!=" => cmp != 0,
                        ">"  => cmp > 0,
                        "<"  => cmp < 0,
                        ">=" => cmp >= 0,
                        "<=" => cmp <= 0,
                        _    => true
                    };
                }
            }

            // No operator found — treat as bare variable name (truthy check)
            if (varValues.TryGetValue(condition, out string val))
                return IsTruthy(val);

            // Check for literal "true"/"false"
            if (string.Equals(condition, "true", StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(condition, "false", StringComparison.OrdinalIgnoreCase))
                return false;

            // Variable not found — default to break
            return true;
        }

        /// <summary>
        /// Determine if a variable value string is "truthy".
        /// False: "0", "false", "False", "null", "Nothing", empty string, "\"\"".
        /// True: everything else.
        /// </summary>
        private static bool IsTruthy(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;

            string cleaned = StripQuotes(value);
            if (string.IsNullOrEmpty(cleaned)) return false;

            if (string.Equals(cleaned, "0", StringComparison.Ordinal)) return false;
            if (string.Equals(cleaned, "false", StringComparison.OrdinalIgnoreCase)) return false;
            if (string.Equals(cleaned, "null", StringComparison.OrdinalIgnoreCase)) return false;
            if (string.Equals(cleaned, "Nothing", StringComparison.OrdinalIgnoreCase)) return false;

            return true;
        }

        /// <summary>
        /// Remove surrounding double quotes from a string value.
        /// Handles both the DAP-style quoted values ("\"hello\"") and user-provided quoted literals ("hello").
        /// </summary>
        private static string StripQuotes(string value)
        {
            if (value == null) return "";
            value = value.Trim();
            // Remove escaped quotes first (DAP format: \"value\")
            if (value.StartsWith("\\\"") && value.EndsWith("\\\"") && value.Length >= 4)
                return value.Substring(2, value.Length - 4);
            // Remove regular quotes
            if (value.StartsWith("\"") && value.EndsWith("\"") && value.Length >= 2)
                return value.Substring(1, value.Length - 2);
            return value;
        }

        /// <summary>
        /// Interpolate {expression} placeholders in a logpoint message.
        /// Replaces {varname} with the variable's value from the current frame.
        /// </summary>
        private string InterpolateLogMessage(string message)
        {
            if (string.IsNullOrEmpty(message) || !message.Contains('{'))
                return message;

            // Get the current thread's active IL frame for variable lookup
            ICorDebugILFrame currentILFrame = null;
            try
            {
                var thread = GetFirstThread();
                if (thread != null)
                {
                    var threadPtr = Marshal.GetIUnknownForObject(thread);
                    try
                    {
                        var vtable = Marshal.ReadIntPtr(threadPtr);
                        var getActiveFrameSlot = Marshal.ReadIntPtr(vtable, 15 * IntPtr.Size);
                        var getActiveFrame = Marshal.GetDelegateForFunctionPointer<GetActiveFrameDelegate>(getActiveFrameSlot);
                        int hr = getActiveFrame(threadPtr, out var framePtr);
                        if (hr >= 0 && framePtr != IntPtr.Zero)
                        {
                            try
                            {
                                Guid iidILFrame = new Guid("03E26311-4F76-11D3-88C6-006097945418");
                                int qiHr = Marshal.QueryInterface(framePtr, ref iidILFrame, out IntPtr ilFramePtr);
                                if (qiHr >= 0 && ilFramePtr != IntPtr.Zero)
                                {
                                    try
                                    {
                                        currentILFrame = (ICorDebugILFrame)Marshal.GetObjectForIUnknown(ilFramePtr);
                                    }
                                    catch
                                    {
                                        // Cast failed -- fall through with null frame
                                    }
                                    finally
                                    {
                                        Marshal.Release(ilFramePtr);
                                    }
                                }
                            }
                            finally
                            {
                                Marshal.Release(framePtr);
                            }
                        }
                    }
                    finally
                    {
                        Marshal.Release(threadPtr);
                    }
                }
            }
            catch
            {
                // If we can't get the frame, placeholders will resolve to <undefined>
            }

            // Build lookup of variable name -> display value from locals and arguments
            var variableValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (currentILFrame != null && _variableInspector != null)
            {
                try
                {
                    var locals = _variableInspector.GetLocals(currentILFrame);
                    foreach (var v in locals)
                    {
                        if (!string.IsNullOrEmpty(v.Name))
                            variableValues[v.Name] = v.Value ?? "<null>";
                    }
                }
                catch
                {
                    // Locals unavailable
                }

                try
                {
                    var arguments = _variableInspector.GetArguments(currentILFrame);
                    foreach (var v in arguments)
                    {
                        if (!string.IsNullOrEmpty(v.Name) && !variableValues.ContainsKey(v.Name))
                            variableValues[v.Name] = v.Value ?? "<null>";
                    }
                }
                catch
                {
                    // Arguments unavailable
                }
            }

            // Replace all {expression} placeholders with variable values
            var result = new StringBuilder(message.Length);
            int i = 0;
            while (i < message.Length)
            {
                if (message[i] == '{')
                {
                    int closingBrace = message.IndexOf('}', i + 1);
                    if (closingBrace > i + 1)
                    {
                        string expr = message.Substring(i + 1, closingBrace - i - 1).Trim();
                        if (expr.Length > 0 && variableValues.TryGetValue(expr, out string value))
                        {
                            result.Append(value);
                        }
                        else
                        {
                            result.Append("<undefined>");
                        }
                        i = closingBrace + 1;
                    }
                    else
                    {
                        // No closing brace found -- emit the rest as-is
                        result.Append(message, i, message.Length - i);
                        break;
                    }
                }
                else
                {
                    result.Append(message[i]);
                    i++;
                }
            }

            return result.ToString();
        }

        private void ClearDebugState()
        {
            _variableInspector?.ClearReferences();
            _scopeReferences.Clear();

            // Release raw IL frame pointers before clearing
            foreach (var kvp in _frameInfoMap)
            {
                if (kvp.Value.RawILFramePtr != IntPtr.Zero)
                {
                    try { Marshal.Release(kvp.Value.RawILFramePtr); } catch { }
                    kvp.Value.RawILFramePtr = IntPtr.Zero;
                }
            }
            _frameInfoMap.Clear();

            _nextScopeRef = 1;
            _nextFrameId = 1;
        }

        // =====================================================================
        // DAP message I/O — same format as DebugSession
        // =====================================================================

        private DAPResponse CreateResponse(DAPMessage request, bool success)
        {
            return new DAPResponse
            {
                Seq = Interlocked.Increment(ref _requestSeq),
                Type = "response",
                RequestSeq = request.Seq,
                Success = success,
                Command = request.Command
            };
        }

        private async Task SendResponseAsync(DAPResponse response)
        {
            var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });
            await SendMessageAsync(json);
        }

        private async Task SendEventAsync(string eventName, Dictionary<string, object> body)
        {
            var evt = new DAPEvent
            {
                Seq = Interlocked.Increment(ref _requestSeq),
                Type = "event",
                Event = eventName,
                Body = body
            };
            var json = JsonSerializer.Serialize(evt, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });
            await SendMessageAsync(json);
        }

        private Task SendMessageAsync(string json)
        {
            var contentBytes = Encoding.UTF8.GetBytes(json);
            var header = $"Content-Length: {contentBytes.Length}\r\n\r\n";
            var headerBytes = Encoding.UTF8.GetBytes(header);

            lock (_writeLock)
            {
                _outputStream.Write(headerBytes, 0, headerBytes.Length);
                _outputStream.Write(contentBytes, 0, contentBytes.Length);
                _outputStream.Flush();
            }
            return Task.CompletedTask;
        }

        // =====================================================================
        // Variable reading helpers (PDB fallback + raw vtable)
        // =====================================================================

        /// <summary>
        /// Replace generic "local_N" names with actual names from PDB data.
        /// </summary>
        private void ApplyPdbLocalNames(List<DapVariable> locals, int functionToken)
        {
            if (_sourceMapper == null) return;
            var pdbNames = _sourceMapper.GetLocalVariableNames(functionToken);
            if (pdbNames.Count == 0) return;

            for (int i = 0; i < locals.Count; i++)
            {
                if (pdbNames.TryGetValue(i, out var name) && !string.IsNullOrEmpty(name))
                {
                    locals[i] = new DapVariable
                    {
                        Name = name,
                        Value = locals[i].Value,
                        Type = locals[i].Type,
                        VariablesReference = locals[i].VariablesReference
                    };
                }
            }
        }

        /// <summary>
        /// PDB-only fallback: returns local variable names with placeholder values.
        /// Used when both managed COM and raw vtable approaches fail.
        /// </summary>
        private List<DapVariable> GetPdbLocalsFallback(int functionToken)
        {
            var result = new List<DapVariable>();
            if (_sourceMapper == null)
            {
                result.Add(new DapVariable
                {
                    Name = "(info)",
                    Value = "Variables unavailable in CLR mode",
                    Type = "String",
                    VariablesReference = 0
                });
                return result;
            }

            var pdbNames = _sourceMapper.GetLocalVariableNames(functionToken);
            if (pdbNames.Count == 0)
            {
                result.Add(new DapVariable
                {
                    Name = "(info)",
                    Value = "No local variable info in PDB",
                    Type = "String",
                    VariablesReference = 0
                });
                return result;
            }

            // Sort by slot index for stable ordering
            var sorted = new SortedDictionary<int, string>(pdbNames);
            foreach (var kvp in sorted)
            {
                result.Add(new DapVariable
                {
                    Name = kvp.Value,
                    Value = "<value unavailable>",
                    Type = "Object",
                    VariablesReference = 0
                });
            }
            return result;
        }

        /// <summary>
        /// Read local variables via raw ICorDebugILFrame vtable calls.
        /// ICorDebugILFrame::EnumerateLocalVariables is slot 13 (IUnknown 3 + ICorDebugFrame 8 + offset 2).
        /// ICorDebugILFrame::GetLocalVariable is slot 14.
        /// </summary>
        private List<DapVariable> ReadLocalsViaRawVtable(FrameInfo frameInfo)
        {
            var ilFramePtr = frameInfo.RawILFramePtr;
            if (ilFramePtr == IntPtr.Zero) return null;

            var vtable = Marshal.ReadIntPtr(ilFramePtr);

            // First try to get count via EnumerateLocalVariables (slot 13)
            var enumLocalsSlot = Marshal.ReadIntPtr(vtable, 13 * IntPtr.Size);
            var enumLocals = Marshal.GetDelegateForFunctionPointer<EnumerateLocalVariablesDelegate>(enumLocalsSlot);
            int hr = enumLocals(ilFramePtr, out IntPtr valueEnumPtr);
            if (hr < 0 || valueEnumPtr == IntPtr.Zero) return null;

            try
            {
                var enumVtable = Marshal.ReadIntPtr(valueEnumPtr);

                // GetCount is slot 6 (IUnknown 3 + ICorDebugEnum: Skip(0), Reset(1), Clone(2), GetCount(3))
                var getCountSlot = Marshal.ReadIntPtr(enumVtable, 6 * IntPtr.Size);
                var getCount = Marshal.GetDelegateForFunctionPointer<ValueEnumGetCountDelegate>(getCountSlot);
                hr = getCount(valueEnumPtr, out uint count);
                if (hr < 0) return null;

                // Get PDB names for display
                Dictionary<int, string> pdbNames = null;
                if (_sourceMapper != null)
                    pdbNames = _sourceMapper.GetLocalVariableNames(frameInfo.FunctionToken);

                var result = new List<DapVariable>();

                // Read each local via GetLocalVariable (slot 14) for indexed access
                var getLocalSlot = Marshal.ReadIntPtr(vtable, 14 * IntPtr.Size);
                var getLocal = Marshal.GetDelegateForFunctionPointer<GetLocalVariableDelegate>(getLocalSlot);

                for (uint i = 0; i < count; i++)
                {
                    string name = (pdbNames != null && pdbNames.TryGetValue((int)i, out var pdbName))
                        ? pdbName
                        : $"local_{i}";

                    hr = getLocal(ilFramePtr, i, out IntPtr valuePtr);
                    if (hr >= 0 && valuePtr != IntPtr.Zero)
                    {
                        try
                        {
                            var dapVar = ReadValueFromRawPointer(valuePtr, name);
                            result.Add(dapVar);
                        }
                        catch
                        {
                            result.Add(new DapVariable { Name = name, Value = "<read error>", Type = "Object", VariablesReference = 0 });
                        }
                        finally
                        {
                            Marshal.Release(valuePtr);
                        }
                    }
                    else
                    {
                        result.Add(new DapVariable { Name = name, Value = "<unavailable>", Type = "Object", VariablesReference = 0 });
                    }
                }

                return result.Count > 0 ? result : null;
            }
            finally
            {
                Marshal.Release(valueEnumPtr);
            }
        }

        /// <summary>
        /// Read arguments via raw ICorDebugILFrame vtable calls.
        /// ICorDebugILFrame::EnumerateArguments is slot 15, GetArgument is slot 16.
        /// </summary>
        private List<DapVariable> ReadArgumentsViaRawVtable(FrameInfo frameInfo)
        {
            var ilFramePtr = frameInfo.RawILFramePtr;
            if (ilFramePtr == IntPtr.Zero) return null;

            var vtable = Marshal.ReadIntPtr(ilFramePtr);

            // EnumerateArguments is slot 15
            var enumArgsSlot = Marshal.ReadIntPtr(vtable, 15 * IntPtr.Size);
            var enumArgs = Marshal.GetDelegateForFunctionPointer<EnumerateArgumentsDelegate>(enumArgsSlot);
            int hr = enumArgs(ilFramePtr, out IntPtr valueEnumPtr);
            if (hr < 0 || valueEnumPtr == IntPtr.Zero) return null;

            try
            {
                var enumVtable = Marshal.ReadIntPtr(valueEnumPtr);
                var getCountSlot = Marshal.ReadIntPtr(enumVtable, 6 * IntPtr.Size);
                var getCount = Marshal.GetDelegateForFunctionPointer<ValueEnumGetCountDelegate>(getCountSlot);
                hr = getCount(valueEnumPtr, out uint count);
                if (hr < 0) return null;

                var result = new List<DapVariable>();

                // GetArgument is slot 16
                var getArgSlot = Marshal.ReadIntPtr(vtable, 16 * IntPtr.Size);
                var getArg = Marshal.GetDelegateForFunctionPointer<GetArgumentDelegate>(getArgSlot);

                for (uint i = 0; i < count; i++)
                {
                    string name = $"arg_{i}";

                    hr = getArg(ilFramePtr, i, out IntPtr valuePtr);
                    if (hr >= 0 && valuePtr != IntPtr.Zero)
                    {
                        try
                        {
                            var dapVar = ReadValueFromRawPointer(valuePtr, name);
                            result.Add(dapVar);
                        }
                        catch
                        {
                            result.Add(new DapVariable { Name = name, Value = "<read error>", Type = "Object", VariablesReference = 0 });
                        }
                        finally
                        {
                            Marshal.Release(valuePtr);
                        }
                    }
                    else
                    {
                        result.Add(new DapVariable { Name = name, Value = "<unavailable>", Type = "Object", VariablesReference = 0 });
                    }
                }

                return result.Count > 0 ? result : null;
            }
            finally
            {
                Marshal.Release(valueEnumPtr);
            }
        }

        /// <summary>
        /// Read a CLR value from a raw ICorDebugValue pointer.
        /// Tries to QI for ICorDebugGenericValue to read primitive types.
        /// </summary>
        private DapVariable ReadValueFromRawPointer(IntPtr valuePtr, string name)
        {
            var vtable = Marshal.ReadIntPtr(valuePtr);

            // ICorDebugValue::GetType (slot 3)
            var getTypeSlot = Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size);
            var getType = Marshal.GetDelegateForFunctionPointer<ValueGetTypeDelegate>(getTypeSlot);
            int hr = getType(valuePtr, out int elementType);

            string typeName = hr >= 0 ? CorElementTypeToString(elementType) : "Object";

            // ICorDebugValue::GetSize (slot 4)
            var getSizeSlot = Marshal.ReadIntPtr(vtable, 4 * IntPtr.Size);
            var getSize = Marshal.GetDelegateForFunctionPointer<ValueGetSizeDelegate>(getSizeSlot);
            getSize(valuePtr, out uint size);

            // Try to QI for ICorDebugGenericValue to read the raw bytes
            Guid iidGenericValue = new Guid("CC7BCAF8-8A68-11D2-983C-0000F808342D");
            int qiHr = Marshal.QueryInterface(valuePtr, ref iidGenericValue, out IntPtr genericPtr);
            if (qiHr >= 0 && genericPtr != IntPtr.Zero)
            {
                try
                {
                    var genVtable = Marshal.ReadIntPtr(genericPtr);
                    // ICorDebugGenericValue::GetValue (slot 6 = IUnknown(3) + ICorDebugValue(3) + 0)
                    var getValueSlot = Marshal.ReadIntPtr(genVtable, 6 * IntPtr.Size);
                    var getValue = Marshal.GetDelegateForFunctionPointer<GenericValueGetValueDelegate>(getValueSlot);

                    IntPtr buffer = Marshal.AllocHGlobal((int)size);
                    try
                    {
                        hr = getValue(genericPtr, buffer);
                        if (hr >= 0)
                        {
                            string value = FormatPrimitiveValue(elementType, buffer, size);
                            return new DapVariable { Name = name, Value = value, Type = typeName, VariablesReference = 0 };
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buffer);
                    }
                }
                finally
                {
                    Marshal.Release(genericPtr);
                }
            }

            // If we can't read the value, try QI for ICorDebugReferenceValue to check for null
            Guid iidRefValue = new Guid("CC7BCAF9-8A68-11D2-983C-0000F808342D");
            qiHr = Marshal.QueryInterface(valuePtr, ref iidRefValue, out IntPtr refPtr);
            if (qiHr >= 0 && refPtr != IntPtr.Zero)
            {
                Marshal.Release(refPtr);
                return new DapVariable { Name = name, Value = "{object}", Type = typeName, VariablesReference = 0 };
            }

            return new DapVariable { Name = name, Value = "<complex value>", Type = typeName, VariablesReference = 0 };
        }

        /// <summary>
        /// Format a primitive CLR value from raw bytes based on CorElementType.
        /// </summary>
        private static string FormatPrimitiveValue(int elementType, IntPtr buffer, uint size)
        {
            // CorElementType constants
            const int ELEMENT_TYPE_BOOLEAN = 0x02;
            const int ELEMENT_TYPE_CHAR = 0x03;
            const int ELEMENT_TYPE_I1 = 0x04;
            const int ELEMENT_TYPE_U1 = 0x05;
            const int ELEMENT_TYPE_I2 = 0x06;
            const int ELEMENT_TYPE_U2 = 0x07;
            const int ELEMENT_TYPE_I4 = 0x08;
            const int ELEMENT_TYPE_U4 = 0x09;
            const int ELEMENT_TYPE_I8 = 0x0A;
            const int ELEMENT_TYPE_U8 = 0x0B;
            const int ELEMENT_TYPE_R4 = 0x0C;
            const int ELEMENT_TYPE_R8 = 0x0D;
            const int ELEMENT_TYPE_I = 0x18;
            const int ELEMENT_TYPE_U = 0x19;

            switch (elementType)
            {
                case ELEMENT_TYPE_BOOLEAN:
                    return Marshal.ReadByte(buffer) != 0 ? "True" : "False";
                case ELEMENT_TYPE_CHAR:
                    return $"'{(char)Marshal.ReadInt16(buffer)}'";
                case ELEMENT_TYPE_I1:
                    return ((sbyte)Marshal.ReadByte(buffer)).ToString();
                case ELEMENT_TYPE_U1:
                    return Marshal.ReadByte(buffer).ToString();
                case ELEMENT_TYPE_I2:
                    return Marshal.ReadInt16(buffer).ToString();
                case ELEMENT_TYPE_U2:
                    return ((ushort)Marshal.ReadInt16(buffer)).ToString();
                case ELEMENT_TYPE_I4:
                    return Marshal.ReadInt32(buffer).ToString();
                case ELEMENT_TYPE_U4:
                    return ((uint)Marshal.ReadInt32(buffer)).ToString();
                case ELEMENT_TYPE_I8:
                    return Marshal.ReadInt64(buffer).ToString();
                case ELEMENT_TYPE_U8:
                    return ((ulong)Marshal.ReadInt64(buffer)).ToString();
                case ELEMENT_TYPE_R4:
                    {
                        float[] arr = new float[1];
                        Marshal.Copy(buffer, arr, 0, 1);
                        return arr[0].ToString("G");
                    }
                case ELEMENT_TYPE_R8:
                    {
                        double[] arr = new double[1];
                        Marshal.Copy(buffer, arr, 0, 1);
                        return arr[0].ToString("G");
                    }
                case ELEMENT_TYPE_I:
                    return Marshal.ReadIntPtr(buffer).ToString();
                case ELEMENT_TYPE_U:
                    return ((ulong)(long)Marshal.ReadIntPtr(buffer)).ToString();
                default:
                    return $"<raw {size} bytes>";
            }
        }

        /// <summary>
        /// Map CorElementType to a human-readable type name.
        /// </summary>
        private static string CorElementTypeToString(int elementType)
        {
            return elementType switch
            {
                0x02 => "Boolean",
                0x03 => "Char",
                0x04 => "SByte",
                0x05 => "Byte",
                0x06 => "Int16",
                0x07 => "UInt16",
                0x08 => "Int32",
                0x09 => "UInt32",
                0x0A => "Int64",
                0x0B => "UInt64",
                0x0C => "Single",
                0x0D => "Double",
                0x0E => "String",
                0x1C => "Object",
                0x11 => "ValueType",
                0x12 => "Class",
                0x14 => "Array",
                0x18 => "IntPtr",
                0x19 => "UIntPtr",
                _ => "Object"
            };
        }

        // =====================================================================
        // Internal types
        // =====================================================================

        private enum ScopeKind { Locals, Arguments }

        private class ScopeInfo
        {
            public int FrameId { get; set; }
            public ScopeKind Kind { get; set; }
        }

        private class FrameInfo
        {
            public int FunctionToken { get; set; }
            public IntPtr RawILFramePtr { get; set; }
            public ICorDebugILFrame ManagedFrame { get; set; }
        }

        // =================================================================
        // Step via temporary breakpoint
        // =================================================================

        private IntPtr _tempStepBreakpoint = IntPtr.Zero;
        private bool _isStepBreakpoint; // true if next breakpoint hit is from a step

        /// <summary>
        /// Step to the next source line by setting a temporary breakpoint.
        /// Works across P/Invoke boundaries unlike ICorDebugStepper.
        /// </summary>
        private void StepToNextLineViaBreakpoint()
        {
            var thread = GetFirstThread();
            if (thread == null)
            {
                RawContinueProcess();
                return;
            }

            // Get current frame's function token and IL offset
            var threadPtr = Marshal.GetIUnknownForObject(thread);
            try
            {
                var vtable = Marshal.ReadIntPtr(threadPtr);
                // GetActiveFrame is slot 15 (IUnknown(3) + 12)
                var getActiveFrameSlot = Marshal.ReadIntPtr(vtable, 15 * IntPtr.Size);
                var getActiveFrame = Marshal.GetDelegateForFunctionPointer<GetActiveFrameDelegate>(getActiveFrameSlot);
                int hr = getActiveFrame(threadPtr, out var framePtr);
                if (hr < 0 || framePtr == IntPtr.Zero)
                {
                    RawContinueProcess();
                    return;
                }

                try
                {
                    // GetFunctionToken is on ICorDebugFrame (slot 6) — works on the base frame pointer
                    var frameVtable = Marshal.ReadIntPtr(framePtr);
                    var getFnTokenSlot = Marshal.ReadIntPtr(frameVtable, 6 * IntPtr.Size);
                    var getFnToken = Marshal.GetDelegateForFunctionPointer<GetFunctionTokenDelegate>(getFnTokenSlot);
                    getFnToken(framePtr, out uint functionToken);

                    // GetIP is on ICorDebugILFrame — MUST QueryInterface for the ILFrame vtable
                    Guid iidILFrame = new Guid("03E26311-4F76-11D3-88C6-006097945418");
                    int qiHr = Marshal.QueryInterface(framePtr, ref iidILFrame, out IntPtr ilFramePtr);
                    uint currentIL = 0;
                    if (qiHr >= 0 && ilFramePtr != IntPtr.Zero)
                    {
                        var ilFrameVtable = Marshal.ReadIntPtr(ilFramePtr);
                        var getIPSlot = Marshal.ReadIntPtr(ilFrameVtable, 11 * IntPtr.Size);
                        var getIP = Marshal.GetDelegateForFunctionPointer<GetIPDelegate>(getIPSlot);
                        getIP(ilFramePtr, out currentIL, out _);
                        Marshal.Release(ilFramePtr);
                    }
                    // Find current source line
                    var currentLoc = _sourceMapper?.GetSourceLocation((int)functionToken, (int)currentIL);
                    if (currentLoc == null)
                    {
                        RawContinueProcess();
                        return;
                    }

                    // Find next executable line after current
                    var nextLine = _sourceMapper.GetNextExecutableLine(
                        currentLoc.Value.file, currentLoc.Value.line, (int)functionToken);

                    if (nextLine == null)
                    {
                        RawContinueProcess();
                        return;
                    }

                    // Remove previous temp breakpoint if any
                    RemoveTempStepBreakpoint();

                    // Set temporary breakpoint on next line using the user module
                    foreach (var module in _loadedModules.Values)
                    {
                        var modulePtr = Marshal.GetIUnknownForObject(module);
                        try
                        {
                            var modVtable = Marshal.ReadIntPtr(modulePtr);
                            var getFuncSlot = Marshal.ReadIntPtr(modVtable, 9 * IntPtr.Size);
                            var getFunc = Marshal.GetDelegateForFunctionPointer<GetFunctionFromTokenDelegate>(getFuncSlot);
                            hr = getFunc(modulePtr, (uint)nextLine.Value.methodToken, out var funcPtr);
                            if (hr < 0 || funcPtr == IntPtr.Zero) continue;

                            var funcVtable2 = Marshal.ReadIntPtr(funcPtr);
                            var getCodeSlot = Marshal.ReadIntPtr(funcVtable2, 6 * IntPtr.Size);
                            var getCode = Marshal.GetDelegateForFunctionPointer<GetILCodeDelegate>(getCodeSlot);
                            hr = getCode(funcPtr, out var codePtr);
                            Marshal.Release(funcPtr);
                            if (hr < 0 || codePtr == IntPtr.Zero) continue;

                            var codeVtable2 = Marshal.ReadIntPtr(codePtr);
                            var createBpSlot = Marshal.ReadIntPtr(codeVtable2, 7 * IntPtr.Size);
                            var createBp = Marshal.GetDelegateForFunctionPointer<CreateBreakpointDelegate>(createBpSlot);
                            hr = createBp(codePtr, (uint)nextLine.Value.ilOffset, out var bpPtr);
                            Marshal.Release(codePtr);
                            if (hr < 0 || bpPtr == IntPtr.Zero) continue;

                            var bpVtable2 = Marshal.ReadIntPtr(bpPtr);
                            var activateSlot = Marshal.ReadIntPtr(bpVtable2, 3 * IntPtr.Size);
                            var activate = Marshal.GetDelegateForFunctionPointer<ActivateBreakpointDelegate>(activateSlot);
                            hr = activate(bpPtr, 1);
                            if (hr >= 0)
                            {
                                _tempStepBreakpoint = bpPtr;
                                _isStepBreakpoint = true;
                                RawContinueProcess();
                                return;
                            }
                            Marshal.Release(bpPtr);
                        }
                        finally
                        {
                            Marshal.Release(modulePtr);
                        }
                    }

                    // Fallback: just continue
                    RawContinueProcess();
                }
                finally
                {
                    Marshal.Release(framePtr);
                }
            }
            finally
            {
                Marshal.Release(threadPtr);
            }
        }

        /// <summary>
        /// Remove the temporary step breakpoint.
        /// </summary>
        private void RemoveTempStepBreakpoint()
        {
            if (_tempStepBreakpoint != IntPtr.Zero)
            {
                try
                {
                    // Deactivate: ICorDebugBreakpoint::Activate(false) at slot 3
                    var bpVtable = Marshal.ReadIntPtr(_tempStepBreakpoint);
                    var deactivateSlot = Marshal.ReadIntPtr(bpVtable, 3 * IntPtr.Size);
                    var deactivate = Marshal.GetDelegateForFunctionPointer<ActivateBreakpointDelegate>(deactivateSlot);
                    deactivate(_tempStepBreakpoint, 0); // 0 = false = deactivate
                    Marshal.Release(_tempStepBreakpoint);
                }
                catch { }
                _tempStepBreakpoint = IntPtr.Zero;
                _isStepBreakpoint = false;
            }
        }

        // =================================================================
        // Raw vtable helpers — bypass QI issues with .NET Core COM objects
        // =================================================================

        /// <summary>
        /// Continue the process using raw vtable call on ICorDebugProcess.
        /// ICorDebugController::Continue is vtable slot 4 (IUnknown(3) + Stop(0) + Continue(1))
        /// </summary>
        private void RawContinueProcess()
        {
            if (_process?.CorDebugProcess == null) return;
            var processPtr = Marshal.GetIUnknownForObject(_process.CorDebugProcess);
            try
            {
                var vtable = Marshal.ReadIntPtr(processPtr);
                // ICorDebugController: slot 3=Stop, slot 4=Continue
                var continueSlot = Marshal.ReadIntPtr(vtable, 4 * IntPtr.Size);
                var continueFn = Marshal.GetDelegateForFunctionPointer<ContinueProcessDelegate>(continueSlot);
                int hr = continueFn(processPtr, 0); // 0 = fIsOutOfBand = false
            }
            finally
            {
                Marshal.Release(processPtr);
            }
        }

        /// <summary>
        /// Create a stepper and step using raw vtable calls.
        /// Uses StepRange to step by SOURCE LINE instead of single IL instruction.
        /// Falls back to Step if the IL range for the current line cannot be determined.
        /// ICorDebugThread::CreateStepper is vtable slot 12 (IUnknown(3) + 9 methods)
        /// ICorDebugStepper::Step is vtable slot 7, StepRange is vtable slot 8
        /// </summary>
        private void RawCreateStepperAndStep(ICorDebugThread thread, bool stepInto)
        {
            var threadPtr = Marshal.GetIUnknownForObject(thread);
            try
            {
                // --- Get current frame's method token and IL offset for StepRange ---
                uint methodToken = 0;
                uint currentIL = 0;
                bool haveFrameInfo = false;

                var vtable = Marshal.ReadIntPtr(threadPtr);

                // GetActiveFrame is slot 15 (IUnknown(3) + 12)
                var getActiveFrameSlot = Marshal.ReadIntPtr(vtable, 15 * IntPtr.Size);
                var getActiveFrame = Marshal.GetDelegateForFunctionPointer<GetActiveFrameDelegate>(getActiveFrameSlot);
                int hr = getActiveFrame(threadPtr, out var framePtr);
                if (hr >= 0 && framePtr != IntPtr.Zero)
                {
                    try
                    {
                        // GetFunctionToken is on ICorDebugFrame (slot 6)
                        var frameVtable = Marshal.ReadIntPtr(framePtr);
                        var getFnTokenSlot = Marshal.ReadIntPtr(frameVtable, 6 * IntPtr.Size);
                        var getFnToken = Marshal.GetDelegateForFunctionPointer<GetFunctionTokenDelegate>(getFnTokenSlot);
                        getFnToken(framePtr, out methodToken);

                        // GetIP is on ICorDebugILFrame — must QueryInterface
                        Guid iidILFrame = new Guid("03E26311-4F76-11D3-88C6-006097945418");
                        int qiHr = Marshal.QueryInterface(framePtr, ref iidILFrame, out IntPtr ilFramePtr);
                        if (qiHr >= 0 && ilFramePtr != IntPtr.Zero)
                        {
                            var ilFrameVtable = Marshal.ReadIntPtr(ilFramePtr);
                            var getIPSlot = Marshal.ReadIntPtr(ilFrameVtable, 11 * IntPtr.Size);
                            var getIP = Marshal.GetDelegateForFunctionPointer<GetIPDelegate>(getIPSlot);
                            getIP(ilFramePtr, out currentIL, out _);
                            Marshal.Release(ilFramePtr);
                            haveFrameInfo = true;
                        }
                    }
                    finally
                    {
                        Marshal.Release(framePtr);
                    }
                }

                // --- Create stepper ---
                // ICorDebugThread vtable: IUnknown(3) + ... CreateStepper(9) = slot 12
                var createStepperSlot = Marshal.ReadIntPtr(vtable, 12 * IntPtr.Size);
                var createStepperFn = Marshal.GetDelegateForFunctionPointer<CreateStepperOnThreadDelegate>(createStepperSlot);
                hr = createStepperFn(threadPtr, out var stepperPtr);
                if (hr < 0 || stepperPtr == IntPtr.Zero) return;

                var stepperVtable = Marshal.ReadIntPtr(stepperPtr);

                // --- Try StepRange for source-line stepping ---
                bool usedStepRange = false;
                if (haveFrameInfo && _sourceMapper != null)
                {
                    // Find current source line from IL offset
                    var currentLoc = _sourceMapper.GetSourceLocation((int)methodToken, (int)currentIL);
                    if (currentLoc != null)
                    {
                        // Get the IL range [startOffset, endOffset) for the current source line
                        var ilRange = _sourceMapper.GetILRangeForLine((int)methodToken, currentLoc.Value.line);
                        if (ilRange != null)
                        {
                            // Allocate COR_DEBUG_STEP_RANGE struct (two uint32 = 8 bytes)
                            IntPtr rangePtr = Marshal.AllocHGlobal(8);
                            try
                            {
                                Marshal.WriteInt32(rangePtr, 0, ilRange.Value.startOffset);      // startOffset
                                Marshal.WriteInt32(rangePtr, 4, ilRange.Value.endOffset);         // endOffset

                                // StepRange is at vtable slot 3+5 = 8
                                var stepRangeSlot = Marshal.ReadIntPtr(stepperVtable, 8 * IntPtr.Size);
                                var stepRangeFn = Marshal.GetDelegateForFunctionPointer<StepRangeDelegate>(stepRangeSlot);
                                hr = stepRangeFn(stepperPtr, stepInto ? 1 : 0, rangePtr, 1);
                                if (hr >= 0)
                                    usedStepRange = true;
                            }
                            finally
                            {
                                Marshal.FreeHGlobal(rangePtr);
                            }
                        }
                    }
                }

                // --- Fallback to single-instruction Step if StepRange failed ---
                if (!usedStepRange)
                {
                    // Step is at vtable slot 3+4 = 7
                    var stepSlot = Marshal.ReadIntPtr(stepperVtable, 7 * IntPtr.Size);
                    var stepFn = Marshal.GetDelegateForFunctionPointer<StepDelegate>(stepSlot);
                    hr = stepFn(stepperPtr, stepInto ? 1 : 0);
                }
            }
            finally
            {
                Marshal.Release(threadPtr);
            }
        }

        /// <summary>
        /// Create a stepper and step out using raw vtable calls.
        /// ICorDebugStepper::StepOut is vtable slot 9 (IUnknown(3) + 6 methods)
        /// </summary>
        private void RawCreateStepperAndStepOut(ICorDebugThread thread)
        {
            var threadPtr = Marshal.GetIUnknownForObject(thread);
            try
            {
                var vtable = Marshal.ReadIntPtr(threadPtr);
                // ICorDebugThread::CreateStepper is vtable slot 12 (IUnknown(3) + 9)
                var createStepperSlot = Marshal.ReadIntPtr(vtable, 12 * IntPtr.Size);
                var createStepperFn = Marshal.GetDelegateForFunctionPointer<CreateStepperOnThreadDelegate>(createStepperSlot);
                int hr = createStepperFn(threadPtr, out var stepperPtr);
                if (hr < 0 || stepperPtr == IntPtr.Zero)
                {
                    Console.Error.WriteLine($"[DAP] CreateStepper failed for StepOut: hr=0x{hr:X8}");
                    return;
                }

                try
                {
                    var stepperVtable = Marshal.ReadIntPtr(stepperPtr);

                    // SetInterceptMask (slot 5) — skip interceptors so StepOut lands in user code
                    var setInterceptSlot = Marshal.ReadIntPtr(stepperVtable, 5 * IntPtr.Size);
                    var setInterceptFn = Marshal.GetDelegateForFunctionPointer<SetInterceptMaskDelegate>(setInterceptSlot);
                    setInterceptFn(stepperPtr, 0); // INTERCEPT_NONE — don't stop in class init, security, etc.

                    // SetUnmappedStopMask (slot 6) — skip unmapped code regions
                    var setUnmappedSlot = Marshal.ReadIntPtr(stepperVtable, 6 * IntPtr.Size);
                    var setUnmappedFn = Marshal.GetDelegateForFunctionPointer<SetUnmappedStopMaskDelegate>(setUnmappedSlot);
                    setUnmappedFn(stepperPtr, 0); // STOP_NONE — don't stop in unmapped IL

                    // StepOut is at vtable slot 3+6 = 9
                    var stepOutSlot = Marshal.ReadIntPtr(stepperVtable, 9 * IntPtr.Size);
                    var stepOutFn = Marshal.GetDelegateForFunctionPointer<StepOutDelegate>(stepOutSlot);
                    hr = stepOutFn(stepperPtr);
                    if (hr < 0)
                    {
                        Console.Error.WriteLine($"[DAP] StepOut failed: hr=0x{hr:X8}");
                    }
                }
                finally
                {
                    Marshal.Release(stepperPtr);
                }
            }
            finally
            {
                Marshal.Release(threadPtr);
            }
        }

        // Raw vtable call delegates for ICorDebug COM interop (bypasses QI issues on .NET Core)
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetCurrentExceptionDelegate(IntPtr self, out IntPtr ppExceptionObject);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetFunctionFromTokenDelegate(IntPtr self, uint methodDef, out IntPtr ppFunction);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetILCodeDelegate(IntPtr self, out IntPtr ppCode);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateBreakpointDelegate(IntPtr self, uint offset, out IntPtr ppBreakpoint);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int ActivateBreakpointDelegate(IntPtr self, int bActive);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int ContinueProcessDelegate(IntPtr self, int fIsOutOfBand);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateStepperOnThreadDelegate(IntPtr self, out IntPtr ppStepper);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int StepDelegate(IntPtr self, int bStepIn);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int StepRangeDelegate(IntPtr self, int bStepIn, IntPtr ranges, uint cRangeCount);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int StepOutDelegate(IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetInterceptMaskDelegate(IntPtr self, int mask);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetUnmappedStopMaskDelegate(IntPtr self, int mask);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetActiveFrameDelegate(IntPtr self, out IntPtr ppFrame);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetFunctionTokenDelegate(IntPtr self, out uint pToken);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetIPDelegate(IntPtr self, out uint pnOffset, out int pMappingResult);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetCallerDelegate(IntPtr self, out IntPtr ppFrame);

        // ICorDebugILFrame variable enumeration delegates
        // ICorDebugILFrame vtable: IUnknown(3) + ICorDebugFrame(8) + GetIP(0), SetIP(1), EnumerateLocalVariables(2), GetLocalVariable(3), EnumerateArguments(4), GetArgument(5)
        // EnumerateLocalVariables = slot 3+8+2 = 13, GetLocalVariable = 14, EnumerateArguments = 15, GetArgument = 16
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int EnumerateLocalVariablesDelegate(IntPtr self, out IntPtr ppValueEnum);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetLocalVariableDelegate(IntPtr self, uint dwIndex, out IntPtr ppValue);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int EnumerateArgumentsDelegate(IntPtr self, out IntPtr ppValueEnum);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetArgumentDelegate(IntPtr self, uint dwIndex, out IntPtr ppValue);

        // ICorDebugEnum: IUnknown(3) + Skip(0), Reset(1), Clone(2), GetCount(3)
        // ICorDebugValueEnum: Next(4) at slot 3+4 = 7
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int ValueEnumGetCountDelegate(IntPtr self, out uint pcelt);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int ValueEnumNextDelegate(IntPtr self, uint celt, out IntPtr ppValues, out uint pceltFetched);

        // ICorDebugValue: IUnknown(3) + GetType(0), GetSize(1), GetAddress(2) → slots 3,4,5
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int ValueGetTypeDelegate(IntPtr self, out int pType);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int ValueGetSizeDelegate(IntPtr self, out uint pSize);

        // ICorDebugGenericValue: extends ICorDebugValue + GetValue(0), SetValue(1) → slots 6,7
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GenericValueGetValueDelegate(IntPtr self, IntPtr pTo);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GenericValueSetValueDelegate(IntPtr self, IntPtr pFrom);

        // Thread enumeration delegates
        private delegate int EnumerateThreadsDelegate(IntPtr self, out IntPtr ppThreads);
        private delegate int ThreadEnumGetCountDelegate(IntPtr self, out uint pcelt);
        private delegate int ThreadEnumNextDelegate(IntPtr self, uint celt, out IntPtr ppThreads, out uint pceltFetched);
        private delegate int ThreadGetIDDelegate(IntPtr self, out uint pdwThreadId);
    }
}
