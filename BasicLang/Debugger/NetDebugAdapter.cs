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

        // Scope / frame tracking for variables requests
        private readonly Dictionary<int, ScopeInfo> _scopeReferences = new();
        private readonly Dictionary<int, ICorDebugILFrame> _frameMap = new();
        private int _nextScopeRef = 1;
        private int _nextFrameId = 1;

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
                    "setBreakpoints" => HandleSetBreakpoints(message),
                    "setFunctionBreakpoints" => HandleSetFunctionBreakpoints(message),
                    "configurationDone" => HandleConfigurationDone(message),
                    "threads" => HandleThreads(message),
                    "stackTrace" => HandleStackTrace(message),
                    "scopes" => HandleScopes(message),
                    "variables" => HandleVariables(message),
                    "continue" => HandleContinue(message),
                    "next" => HandleNext(message),
                    "stepIn" => HandleStepIn(message),
                    "stepOut" => HandleStepOut(message),
                    "pause" => HandlePause(message),
                    "evaluate" => HandleEvaluate(message),
                    "setExceptionBreakpoints" => HandleSetExceptionBreakpoints(message),
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
                ["supportsSetVariable"] = false,
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

            // Clear existing breakpoints for this file
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
                        // Try to bind on the first user module (not system assemblies)
                        var userModule = _loadedModules.Values.FirstOrDefault();
                        verified = TryBindBreakpoint(entry, ilInfo.Value.methodToken, ilInfo.Value.ilOffset, userModule);
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

            // Always report thread ID 1 as the main thread
            // The actual OS thread IDs are mapped internally
            threadList.Add(new Dictionary<string, object>
            {
                ["id"] = 1,
                ["name"] = "Main Thread"
            });

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
            _frameMap.Clear();
            _nextFrameId = 1;

            const int MaxFrames = 50;

            try
            {
                // Try raw vtable walk of the thread's frames
                ICorDebugThread thread = GetFirstThread();

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

                                // Store ICorDebugILFrame in frame map for variable inspection
                                int frameId = _nextFrameId++;
                                try
                                {
                                    var ilFrameObj = (ICorDebugILFrame)Marshal.GetObjectForIUnknown(ilFramePtr);
                                    _frameMap[frameId] = ilFrameObj;
                                }
                                catch
                                {
                                    // QI succeeded but cast failed -- still add the frame to stack trace
                                }

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

            if (_frameMap.TryGetValue(frameId, out var ilFrame))
            {
                // Locals scope
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
                    // This is a scope reference — fetch locals or arguments from the frame
                    if (_frameMap.TryGetValue(scopeInfo.FrameId, out var ilFrame))
                    {
                        List<DapVariable> dapVars;
                        if (scopeInfo.Kind == ScopeKind.Locals)
                            dapVars = _variableInspector.GetLocals(ilFrame);
                        else
                            dapVars = _variableInspector.GetArguments(ilFrame);

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

        private DAPResponse HandleContinue(DAPMessage request)
        {
            ClearDebugState();
            RawContinueProcess();

            var response = CreateResponse(request, true);
            response.Body = new Dictionary<string, object>
            {
                ["allThreadsContinued"] = true
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
                // Use ICorDebugStepper for step over (skips P/Invoke automatically)
                var thread = GetFirstThread();
                if (thread != null)
                {
                    RawCreateStepperAndStep(thread, stepInto: false);
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

            try
            {
                var thread = GetFirstThread();
                if (thread != null)
                {
                    RawCreateStepperAndStep(thread, stepInto: true);
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

            try
            {
                var thread = GetFirstThread();
                if (thread != null)
                {
                    RawCreateStepperAndStepOut(thread);
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
            int threadId = 1;
            if (_process?.Threads.Count > 0)
                threadId = _process.Threads.Keys.First();

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
                if (_frameMap.TryGetValue(frameId, out var ilFrame))
                {
                    // Search locals for a matching variable name
                    var locals = _variableInspector.GetLocals(ilFrame);
                    var match = locals.FirstOrDefault(v =>
                        string.Equals(v.Name, expression, StringComparison.OrdinalIgnoreCase));

                    if (match == null)
                    {
                        // Try arguments
                        var arguments = _variableInspector.GetArguments(ilFrame);
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

        private DAPResponse HandleDisconnect(DAPMessage request)
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
                if (terminateDebuggee)
                {
                    // Terminate the debuggee process (kills it)
                    _process.Terminate();
                }
                else
                {
                    // Detach and let the process continue running
                    _process.Detach();
                }
                _process.Dispose();
                _process = null;
            }

            _sourceMapper?.Dispose();
            _sourceMapper = null;
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
                if (!IsCurrentFrameUserCode())
                {
                    var thread = GetFirstThread();
                    if (thread != null)
                    {
                        try
                        {
                            RawCreateStepperAndStep(thread, stepInto: false);
                            RawContinueProcess();
                            return; // Don't send stopped event; we're still stepping
                        }
                        catch
                        {
                            // If re-stepping fails, fall through and report stopped
                        }
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

                        // Successfully bound!
                        int actualLine = _sourceMapper?.FindNearestExecutableLine(entry.FilePath, entry.RequestedLine)
                            ?? entry.RequestedLine;
                        _breakpointManager.MarkBound(entry.Id, actualLine, null);
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
        /// Map OS thread ID to DAP thread ID. We always use 1 for the main thread.
        /// </summary>
        private int MapToDapThreadId(int osThreadId) => 1;

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
        /// Evaluate a conditional breakpoint expression. Returns true if condition is met.
        /// Simple implementation: treats "true"/"True"/"1" as true, "false"/"False"/"0" as false.
        /// For variable names, attempts to look them up in the current frame.
        /// </summary>
        private bool EvaluateConditionExpression(string condition)
        {
            // For v1: always break (condition evaluation requires full expression parser)
            // TODO: Implement proper expression evaluation using ICorDebugEval
            return true;
        }

        /// <summary>
        /// Interpolate {expression} placeholders in a logpoint message.
        /// Replaces {varname} with the variable's value from the current frame.
        /// </summary>
        private string InterpolateLogMessage(string message)
        {
            // For v1: return the message as-is (no interpolation)
            // TODO: Replace {varname} with actual variable values
            return message;
        }

        private void ClearDebugState()
        {
            _variableInspector?.ClearReferences();
            _scopeReferences.Clear();
            _frameMap.Clear();
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
        // Internal types
        // =====================================================================

        private enum ScopeKind { Locals, Arguments }

        private class ScopeInfo
        {
            public int FrameId { get; set; }
            public ScopeKind Kind { get; set; }
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
    }
}
