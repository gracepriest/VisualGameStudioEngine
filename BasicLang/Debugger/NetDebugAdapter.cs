using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
                    ["output"] = $"Failed to launch and attach debugger to: {exePath}\n"
                });
                var response = CreateResponse(request, false);
                response.Body = new Dictionary<string, object>
                {
                    ["error"] = new Dictionary<string, object>
                    {
                        ["id"] = 2,
                        ["format"] = "Failed to launch process or attach debugger. Ensure dbgshim.dll is available."
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
                    if (ilInfo.HasValue)
                    {
                        actualLine = _sourceMapper.FindNearestExecutableLine(sourcePath, line);
                        verified = TryBindBreakpoint(entry, ilInfo.Value.methodToken, ilInfo.Value.ilOffset);
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

            if (_process != null && _process.IsAttached)
            {
                foreach (var kvp in _process.Threads)
                {
                    threadList.Add(new Dictionary<string, object>
                    {
                        ["id"] = kvp.Key,
                        ["name"] = $"Thread {kvp.Key}"
                    });
                }
            }

            // Always return at least one thread
            if (threadList.Count == 0)
            {
                threadList.Add(new Dictionary<string, object>
                {
                    ["id"] = 1,
                    ["name"] = "Main Thread"
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
            _frameMap.Clear();
            _nextFrameId = 1;

            try
            {
                ICorDebugThread thread = null;
                if (_process?.Threads.ContainsKey(threadId) == true)
                    thread = _process.Threads[threadId];

                if (thread != null)
                {
                    // Get the active chain and enumerate its frames
                    thread.GetActiveChain(out var chain);
                    if (chain != null)
                    {
                        chain.EnumerateFrames(out var frameEnum);
                        if (frameEnum != null)
                        {
                            frameEnum.GetCount(out uint frameCount);
                            var frames = new ICorDebugFrame[frameCount];
                            frameEnum.Next(frameCount, frames, out uint fetched);

                            for (int i = 0; i < (int)fetched; i++)
                            {
                                var frame = frames[i];
                                if (frame is ICorDebugILFrame ilFrame)
                                {
                                    int frameId = _nextFrameId++;
                                    _frameMap[frameId] = ilFrame;

                                    // Get function token and IL offset
                                    ilFrame.GetFunctionToken(out uint functionToken);
                                    ilFrame.GetIP(out uint ilOffset, out _);

                                    string frameName = $"Frame {frameId}";
                                    string sourceFile = null;
                                    int sourceLine = 0;
                                    int sourceColumn = 1;

                                    // Try to map IL offset to source location
                                    var location = _sourceMapper?.GetSourceLocation((int)functionToken, (int)ilOffset);
                                    if (location.HasValue)
                                    {
                                        sourceFile = location.Value.file;
                                        sourceLine = location.Value.line;
                                        sourceColumn = location.Value.column;
                                        frameName = Path.GetFileNameWithoutExtension(sourceFile) ?? frameName;
                                    }

                                    // Try to get a better name from the function token
                                    ilFrame.GetFunction(out var function);
                                    if (function != null)
                                    {
                                        function.GetToken(out uint methodDef);
                                        // Use method token as display name fallback
                                        if (frameName.StartsWith("Frame"))
                                            frameName = $"0x{methodDef:X8}";
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
                            }
                        }
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
            _process?.Continue();

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
                if (_process?.Threads.ContainsKey(threadId) == true)
                {
                    var thread = _process.Threads[threadId];
                    var stepper = _process.CreateStepper(thread);
                    if (stepper != null)
                    {
                        stepper.Step(false); // false = step over (don't step into calls)
                        _process.Continue();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"StepOver error: {ex.Message}");
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
                if (_process?.Threads.ContainsKey(threadId) == true)
                {
                    var thread = _process.Threads[threadId];
                    var stepper = _process.CreateStepper(thread);
                    if (stepper != null)
                    {
                        stepper.Step(true); // true = step into calls
                        _process.Continue();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"StepIn error: {ex.Message}");
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
                if (_process?.Threads.ContainsKey(threadId) == true)
                {
                    var thread = _process.Threads[threadId];
                    var stepper = _process.CreateStepper(thread);
                    if (stepper != null)
                    {
                        stepper.StepOut();
                        _process.Continue();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"StepOut error: {ex.Message}");
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
            _process?.Detach();
            _process?.Dispose();
            _sourceMapper?.Dispose();
            return CreateResponse(request, true);
        }

        // =====================================================================
        // Event handlers (wired to NetDebugProcess events)
        // =====================================================================

        private async void OnBreakpointHit(object sender, BreakpointHitEventArgs e)
        {
            try
            {
                await SendEventAsync("stopped", new Dictionary<string, object>
                {
                    ["reason"] = "breakpoint",
                    ["threadId"] = e.ThreadId,
                    ["allThreadsStopped"] = true
                });
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"OnBreakpointHit error: {ex.Message}");
            }
        }

        private async void OnStepCompleted(object sender, StepCompletedEventArgs e)
        {
            try
            {
                await SendEventAsync("stopped", new Dictionary<string, object>
                {
                    ["reason"] = "step",
                    ["threadId"] = e.ThreadId,
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

                // Try to bind any pending breakpoints now that a module is loaded
                TryBindPendingBreakpoints(e.Module);

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
        private bool TryBindBreakpoint(ClrBreakpointEntry entry, int methodToken, int ilOffset)
        {
            if (_loadedModules.Count == 0)
                return false;

            try
            {
                // Try each loaded module — the method token should match one of them
                foreach (var module in _loadedModules.Values)
                {
                    int hr = module.GetFunctionFromToken((uint)methodToken, out var function);
                    if (hr < 0 || function == null)
                        continue;

                    hr = function.GetILCode(out var code);
                    if (hr < 0 || code == null)
                        continue;

                    hr = code.CreateBreakpoint((uint)ilOffset, out var clrBreakpoint);
                    if (hr < 0 || clrBreakpoint == null)
                        continue;

                    hr = clrBreakpoint.Activate(true);
                    if (hr < 0)
                        continue;

                    // Successfully bound
                    int actualLine = _sourceMapper?.FindNearestExecutableLine(entry.FilePath, entry.RequestedLine)
                        ?? entry.RequestedLine;
                    _breakpointManager.MarkBound(entry.Id, actualLine, clrBreakpoint);
                    _breakpointManager.MarkVerified(entry.Id);
                    return true;
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
                    if (TryBindBreakpoint(entry, ilInfo.Value.methodToken, ilInfo.Value.ilOffset))
                    {
                        // Send breakpoint changed event to update IDE
                        _ = Task.Run(async () =>
                        {
                            await SendEventAsync("breakpoint", new Dictionary<string, object>
                            {
                                ["reason"] = "changed",
                                ["breakpoint"] = new Dictionary<string, object>
                                {
                                    ["id"] = entry.Id,
                                    ["verified"] = true,
                                    ["line"] = entry.ActualLine
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
    }
}
