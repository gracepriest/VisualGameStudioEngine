using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;

namespace BasicLang.Debugger
{
    /// <summary>
    /// Debug Adapter Protocol session handler
    /// </summary>
    public class DebugSession
    {
        private readonly Stream _input;
        private readonly Stream _outputStream;
        private readonly Dictionary<int, Breakpoint> _breakpoints = new();
        private readonly Dictionary<int, VariableInfo> _variables = new();
        private readonly List<StackFrameInfo> _stackFrames = new();

        private DebuggableInterpreter? _interpreter;
        private string _currentFile = string.Empty;
        private bool _running;
        private bool _stopOnEntry;
        private int _nextVariableRef = 1;
        private int _seq = 0;
        private int _nextBreakpointId = 1;
        private readonly object _writeLock = new();

        public DebugSession(Stream input, Stream output)
        {
            _input = input;
            _outputStream = output;
        }

        public async Task RunAsync()
        {
            var buffer = new StringBuilder();
            var reader = new StreamReader(_input, Encoding.UTF8);

            while (true)
            {
                var line = await reader.ReadLineAsync();
                if (line == null) break;

                if (line.StartsWith("Content-Length:"))
                {
                    var length = int.Parse(line.Substring(15).Trim());
                    await reader.ReadLineAsync(); // Empty line

                    var chars = new char[length];
                    await reader.ReadBlockAsync(chars, 0, length);
                    var content = new string(chars);

                    await HandleMessageAsync(content);
                }
            }
        }

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
                    "dataBreakpointInfo" => HandleDataBreakpointInfo(message),
                    "setDataBreakpoints" => HandleSetDataBreakpoints(message),
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
                    "disconnect" => HandleDisconnect(message),
                    "evaluate" => HandleEvaluate(message),
                    "gotoTargets" => HandleGotoTargets(message),
                    "goto" => HandleGoto(message),
                    "setExceptionBreakpoints" => HandleSetExceptionBreakpoints(message),
                    _ => CreateResponse(message, true)
                };

                await SendResponseAsync(response);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Debug session error: {ex.Message}");
            }
        }

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
                ["supportsDataBreakpoints"] = true,
                ["supportsStepBack"] = false,
                ["supportsSetVariable"] = false,
                ["supportsRestartFrame"] = false,
                ["supportsGotoTargetsRequest"] = true,
                ["supportsStepInTargetsRequest"] = false,
                ["supportsCompletionsRequest"] = false,
                ["supportsModulesRequest"] = false,
                ["supportsExceptionOptions"] = true,
                ["supportsDataBreakpoints"] = true,
                ["exceptionBreakpointFilters"] = new object[]
                {
                    new Dictionary<string, object> { ["filter"] = "all", ["label"] = "All Exceptions", ["default"] = false },
                    new Dictionary<string, object> { ["filter"] = "uncaught", ["label"] = "Uncaught Exceptions", ["default"] = true }
                },
                ["supportsValueFormattingOptions"] = false,
                ["supportsExceptionInfoRequest"] = false,
                ["supportTerminateDebuggee"] = true,
                ["supportsDelayedStackTraceLoading"] = false,
                ["supportsLoadedSourcesRequest"] = false
            };

            // Send initialized event after response is sent
            // Use a completion source to ensure proper ordering
            _ = Task.Run(async () =>
            {
                await Task.Delay(50);
                await SendEventAsync("initialized", null);
            });

            return response;
        }

        private async Task<DAPResponse> HandleLaunchAsync(DAPMessage request)
        {
            var args = request.Arguments;

            if (args.TryGetProperty("program", out var programProp))
            {
                _currentFile = programProp.GetString() ?? string.Empty;
            }

            if (args.TryGetProperty("stopOnEntry", out var stopProp))
            {
                _stopOnEntry = stopProp.GetBoolean();
            }

            // Compile and prepare the program using full multi-file compiler
            if (!string.IsNullOrEmpty(_currentFile) && File.Exists(_currentFile))
            {
                try
                {
                    // Check if there's a .blproj file in the working directory
                    var projectDir = Path.GetDirectoryName(_currentFile) ?? ".";
                    var blprojFiles = Directory.GetFiles(projectDir, "*.blproj", SearchOption.TopDirectoryOnly);

                    IRModule module;

                    if (blprojFiles.Length > 0)
                    {
                        // Multi-file project: compile via project file
                        var project = BasicLang.Compiler.ProjectSystem.ProjectFile.Load(blprojFiles[0]);
                        var sourceFiles = project.GetSourceFiles().ToList();

                        // Find the entry point file and compile with all project files
                        var compiler = new BasicCompiler();
                        var additionalFiles = sourceFiles.Where(f =>
                            !string.Equals(f, _currentFile, StringComparison.OrdinalIgnoreCase)).ToList();

                        var compilationResult = additionalFiles.Count > 0
                            ? compiler.CompileProject(_currentFile, additionalFiles)
                            : compiler.CompileFile(_currentFile);

                        if (!compilationResult.Success || compilationResult.CombinedIR == null)
                        {
                            var errors = string.Join("\n", compilationResult.AllErrors.Select(e => e.Message));
                            await SendEventAsync("output", new Dictionary<string, object>
                            {
                                ["category"] = "stderr",
                                ["output"] = $"Compilation errors:\n{errors}\n"
                            });
                            return CreateResponse(request, true);
                        }

                        module = compilationResult.CombinedIR;
                    }
                    else
                    {
                        // Single file or no project: use compiler which handles Import directives
                        var compiler = new BasicCompiler();
                        var compilationResult = compiler.CompileFile(_currentFile);

                        if (!compilationResult.Success || compilationResult.CombinedIR == null)
                        {
                            var errors = string.Join("\n", compilationResult.AllErrors.Select(e => e.Message));
                            await SendEventAsync("output", new Dictionary<string, object>
                            {
                                ["category"] = "stderr",
                                ["output"] = $"Compilation errors:\n{errors}\n"
                            });
                            return CreateResponse(request, true);
                        }

                        module = compilationResult.CombinedIR;
                    }

                    // Diagnostic: report what was compiled
                    var funcNames = string.Join(", ", module.Functions.Select(f => f.Name));
                    await SendEventAsync("output", new Dictionary<string, object>
                    {
                        ["category"] = "console",
                        ["output"] = $"[DAP] Compiled {module.Functions.Count} function(s): {funcNames}\n"
                    });
                    await SendEventAsync("output", new Dictionary<string, object>
                    {
                        ["category"] = "console",
                        ["output"] = $"[DAP] Engine DLL available: {EngineBindings.IsAvailable()}\n"
                    });

                    _interpreter = new DebuggableInterpreter(module);
                    _interpreter.SetCurrentFile(_currentFile);
                    _interpreter.BreakpointHit += OnBreakpointHit;
                    _interpreter.StepComplete += OnStepComplete;
                    _interpreter.OutputProduced += OnOutputProduced;
                    _interpreter.LogpointHit += OnLogpointHit;
                    _interpreter.DataBreakpointHit += OnDataBreakpointHit;

                    // Set breakpoints
                    foreach (var bp in _breakpoints.Values)
                    {
                        _interpreter.AddBreakpoint(bp);
                    }

                    _running = true;

                    if (_stopOnEntry)
                    {
                        await SendEventAsync("stopped", new Dictionary<string, object>
                        {
                            ["reason"] = "entry",
                            ["threadId"] = 1
                        });
                    }
                }
                catch (Exception ex)
                {
                    await SendEventAsync("output", new Dictionary<string, object>
                    {
                        ["category"] = "stderr",
                        ["output"] = $"Error: {ex.Message}\n"
                    });
                }
            }

            return CreateResponse(request, true);
        }

        private DAPResponse HandleSetBreakpoints(DAPMessage request)
        {
            var args = request.Arguments;
            var breakpoints = new List<object>();

            if (args.TryGetProperty("source", out var source) &&
                source.TryGetProperty("path", out var pathProp))
            {
                var path = pathProp.GetString();

                // Clear existing breakpoints for this file
                var toRemove = _breakpoints.Where(kvp =>
                    string.Equals(kvp.Value.FilePath, path, StringComparison.OrdinalIgnoreCase))
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var id in toRemove)
                {
                    _breakpoints.Remove(id);
                    _interpreter?.RemoveBreakpoint(id);
                }

                if (args.TryGetProperty("breakpoints", out var bpArray))
                {
                    foreach (var bp in bpArray.EnumerateArray())
                    {
                        if (bp.TryGetProperty("line", out var lineProp))
                        {
                            var line = lineProp.GetInt32();
                            var column = 0;

                            if (bp.TryGetProperty("column", out var colProp))
                            {
                                column = colProp.GetInt32();
                            }

                            var breakpoint = new Breakpoint
                            {
                                FilePath = path,
                                Line = line,
                                Column = column,
                                Type = BreakpointType.Line
                            };

                            // Check for conditional breakpoint
                            if (bp.TryGetProperty("condition", out var condProp) &&
                                !string.IsNullOrWhiteSpace(condProp.GetString()))
                            {
                                breakpoint.Type = BreakpointType.Conditional;
                                breakpoint.Condition = condProp.GetString();
                            }

                            // Check for hit count condition
                            if (bp.TryGetProperty("hitCondition", out var hitCondProp) &&
                                !string.IsNullOrWhiteSpace(hitCondProp.GetString()))
                            {
                                breakpoint.Type = BreakpointType.HitCount;
                                ParseHitCondition(hitCondProp.GetString(), breakpoint);
                            }

                            // Check for logpoint
                            if (bp.TryGetProperty("logMessage", out var logMsgProp) &&
                                !string.IsNullOrWhiteSpace(logMsgProp.GetString()))
                            {
                                breakpoint.Type = BreakpointType.Logpoint;
                                breakpoint.LogMessage = logMsgProp.GetString();
                            }

                            // Validate breakpoint line
                            breakpoint.Verified = ValidateBreakpointLine(path, ref line, breakpoint);

                            var id = _nextBreakpointId++;
                            breakpoint.Id = id;
                            _breakpoints[id] = breakpoint;
                            _interpreter?.AddBreakpoint(breakpoint);

                            var bpResponse = new Dictionary<string, object>
                            {
                                ["id"] = id,
                                ["verified"] = breakpoint.Verified,
                                ["line"] = breakpoint.Line
                            };

                            if (column > 0)
                                bpResponse["column"] = column;

                            if (!breakpoint.Verified && !string.IsNullOrEmpty(breakpoint.Message))
                                bpResponse["message"] = breakpoint.Message;

                            breakpoints.Add(bpResponse);
                        }
                    }
                }
            }

            var response = CreateResponse(request, true);
            response.Body = new Dictionary<string, object>
            {
                ["breakpoints"] = breakpoints
            };
            return response;
        }

        private DAPResponse HandleSetFunctionBreakpoints(DAPMessage request)
        {
            var args = request.Arguments;
            var breakpoints = new List<object>();

            // Clear existing function breakpoints
            var toRemove = _breakpoints.Where(kvp =>
                kvp.Value.Type == BreakpointType.Function)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var id in toRemove)
            {
                _breakpoints.Remove(id);
                _interpreter?.RemoveBreakpoint(id);
            }

            if (args.TryGetProperty("breakpoints", out var bpArray))
            {
                foreach (var bp in bpArray.EnumerateArray())
                {
                    if (bp.TryGetProperty("name", out var nameProp))
                    {
                        var functionName = nameProp.GetString();

                        var breakpoint = new Breakpoint
                        {
                            Type = BreakpointType.Function,
                            FunctionName = functionName,
                            Verified = true
                        };

                        // Check for condition on function breakpoint
                        if (bp.TryGetProperty("condition", out var condProp) &&
                            !string.IsNullOrWhiteSpace(condProp.GetString()))
                        {
                            breakpoint.Condition = condProp.GetString();
                        }

                        // Check for hit condition
                        if (bp.TryGetProperty("hitCondition", out var hitCondProp) &&
                            !string.IsNullOrWhiteSpace(hitCondProp.GetString()))
                        {
                            ParseHitCondition(hitCondProp.GetString(), breakpoint);
                        }

                        var id = _nextBreakpointId++;
                        breakpoint.Id = id;
                        _breakpoints[id] = breakpoint;
                        _interpreter?.AddBreakpoint(breakpoint);

                        breakpoints.Add(new Dictionary<string, object>
                        {
                            ["id"] = id,
                            ["verified"] = breakpoint.Verified
                        });
                    }
                }
            }

            var response = CreateResponse(request, true);
            response.Body = new Dictionary<string, object>
            {
                ["breakpoints"] = breakpoints
            };
            return response;
        }

        /// <summary>
        /// Handle dataBreakpointInfo request - returns whether a variable can be watched
        /// </summary>
        private DAPResponse HandleDataBreakpointInfo(DAPMessage request)
        {
            var args = request.Arguments;
            string variableName = null;
            string dataId = null;
            string description = null;
            var accessTypes = new List<string>();

            if (args.TryGetProperty("name", out var nameProp))
            {
                variableName = nameProp.GetString();
            }

            // Also check variablesReference for scoped variable access
            if (args.TryGetProperty("variablesReference", out var varRefProp))
            {
                var varRef = varRefProp.GetInt32();
                // If we have a variables reference, the name is relative to that scope
                // For simplicity, we use the name directly since BasicLang uses flat scoping
            }

            if (!string.IsNullOrEmpty(variableName))
            {
                // All variables in BasicLang can be watched
                dataId = variableName;
                description = $"Break when '{variableName}' changes";
                accessTypes.Add("write");
                accessTypes.Add("read");
                accessTypes.Add("readWrite");
            }

            var response = CreateResponse(request, true);
            var body = new Dictionary<string, object>
            {
                ["dataId"] = dataId ?? (object)DBNull.Value,
                ["description"] = description ?? ""
            };

            if (accessTypes.Count > 0)
            {
                body["accessTypes"] = accessTypes;
                body["canPersist"] = false; // Data breakpoints don't persist across sessions
            }

            response.Body = body;
            return response;
        }

        /// <summary>
        /// Handle setDataBreakpoints request - set watchpoints on variables
        /// </summary>
        private DAPResponse HandleSetDataBreakpoints(DAPMessage request)
        {
            var args = request.Arguments;
            var breakpoints = new List<object>();

            // Clear existing data breakpoints
            _interpreter?.ClearDataBreakpoints();

            var toRemove = _breakpoints.Where(kvp =>
                kvp.Value.Type == BreakpointType.Data)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var id in toRemove)
            {
                _breakpoints.Remove(id);
            }

            if (args.TryGetProperty("breakpoints", out var bpArray))
            {
                foreach (var bp in bpArray.EnumerateArray())
                {
                    string dataId = null;
                    string condition = null;
                    var accessType = DataBreakpointAccessType.Write;

                    if (bp.TryGetProperty("dataId", out var dataIdProp))
                    {
                        dataId = dataIdProp.GetString();
                    }

                    if (bp.TryGetProperty("accessType", out var accessProp))
                    {
                        var accessStr = accessProp.GetString();
                        accessType = accessStr switch
                        {
                            "read" => DataBreakpointAccessType.Read,
                            "readWrite" => DataBreakpointAccessType.ReadWrite,
                            _ => DataBreakpointAccessType.Write
                        };
                    }

                    if (bp.TryGetProperty("condition", out var condProp) &&
                        !string.IsNullOrWhiteSpace(condProp.GetString()))
                    {
                        condition = condProp.GetString();
                    }

                    if (!string.IsNullOrEmpty(dataId))
                    {
                        // Add data breakpoint to interpreter
                        var breakpoint = _interpreter?.AddDataBreakpoint(dataId, accessType, condition);

                        if (breakpoint != null)
                        {
                            var id = _nextBreakpointId++;
                            breakpoint.Id = id;
                            _breakpoints[id] = breakpoint;

                            breakpoints.Add(new Dictionary<string, object>
                            {
                                ["id"] = id,
                                ["verified"] = true
                            });
                        }
                        else
                        {
                            // Interpreter not started yet, store for later
                            var pendingBp = new Breakpoint
                            {
                                Type = BreakpointType.Data,
                                VariableName = dataId,
                                DataAccessType = accessType,
                                Condition = condition ?? string.Empty,
                                Verified = true
                            };

                            var id = _nextBreakpointId++;
                            pendingBp.Id = id;
                            _breakpoints[id] = pendingBp;

                            breakpoints.Add(new Dictionary<string, object>
                            {
                                ["id"] = id,
                                ["verified"] = true
                            });
                        }
                    }
                    else
                    {
                        breakpoints.Add(new Dictionary<string, object>
                        {
                            ["id"] = 0,
                            ["verified"] = false,
                            ["message"] = "Invalid data breakpoint: no variable specified"
                        });
                    }
                }
            }

            var response = CreateResponse(request, true);
            response.Body = new Dictionary<string, object>
            {
                ["breakpoints"] = breakpoints
            };
            return response;
        }

        private void ParseHitCondition(string hitCondition, Breakpoint breakpoint)
        {
            // Format: "== 5", "> 10", "% 3 == 0", etc.
            hitCondition = hitCondition.Trim();

            if (hitCondition.StartsWith("=="))
            {
                if (int.TryParse(hitCondition.Substring(2).Trim(), out var value))
                {
                    breakpoint.HitCountCondition = HitCountCondition.Equals;
                    breakpoint.HitCountTarget = value;
                }
            }
            else if (hitCondition.StartsWith(">="))
            {
                if (int.TryParse(hitCondition.Substring(2).Trim(), out var value))
                {
                    breakpoint.HitCountCondition = HitCountCondition.GreaterThanOrEquals;
                    breakpoint.HitCountTarget = value;
                }
            }
            else if (hitCondition.StartsWith(">"))
            {
                if (int.TryParse(hitCondition.Substring(1).Trim(), out var value))
                {
                    breakpoint.HitCountCondition = HitCountCondition.GreaterThan;
                    breakpoint.HitCountTarget = value;
                }
            }
            else if (hitCondition.StartsWith("<="))
            {
                if (int.TryParse(hitCondition.Substring(2).Trim(), out var value))
                {
                    breakpoint.HitCountCondition = HitCountCondition.LessThanOrEquals;
                    breakpoint.HitCountTarget = value;
                }
            }
            else if (hitCondition.StartsWith("<"))
            {
                if (int.TryParse(hitCondition.Substring(1).Trim(), out var value))
                {
                    breakpoint.HitCountCondition = HitCountCondition.LessThan;
                    breakpoint.HitCountTarget = value;
                }
            }
            else if (hitCondition.StartsWith("%"))
            {
                // Format: "% 3" means every 3rd hit
                if (int.TryParse(hitCondition.Substring(1).Trim(), out var value))
                {
                    breakpoint.HitCountCondition = HitCountCondition.Modulo;
                    breakpoint.HitCountTarget = value;
                }
            }
            else if (int.TryParse(hitCondition, out var value))
            {
                // Just a number means "equals"
                breakpoint.HitCountCondition = HitCountCondition.Equals;
                breakpoint.HitCountTarget = value;
            }
        }

        private DAPResponse HandleConfigurationDone(DAPMessage request)
        {
            // Start execution if not stopping on entry
            if (!_stopOnEntry && _interpreter != null)
            {
                // Log breakpoint count
                var allBps = _interpreter.GetAllBreakpoints();
                _ = SendEventAsync("output", new Dictionary<string, object>
                {
                    ["category"] = "console",
                    ["output"] = $"[DAP] Starting execution with {allBps.Count} breakpoint(s): {string.Join(", ", allBps.Select(b => $"{Path.GetFileName(b.FilePath ?? "?")}:{b.Line}"))}\n"
                });

                Task.Run(async () =>
                {
                    int exitCode = 0;
                    try
                    {
                        _interpreter.Run();
                    }
                    catch (Exception ex)
                    {
                        exitCode = 1;
                        await SendEventAsync("output", new Dictionary<string, object>
                        {
                            ["category"] = "stderr",
                            ["output"] = $"Runtime error: {ex.Message}\n"
                        });
                    }
                    finally
                    {
                        // Wait a bit to ensure all output events are flushed
                        await Task.Delay(100);

                        // Send output message with exit code (like run mode does)
                        await SendEventAsync("output", new Dictionary<string, object>
                        {
                            ["category"] = "stdout",
                            ["output"] = $"\nProgram exited with code {exitCode}\n"
                        });

                        // Send exited event with exit code (DAP protocol)
                        await SendEventAsync("exited", new Dictionary<string, object>
                        {
                            ["exitCode"] = exitCode
                        });

                        // Send terminated event when program completes
                        await SendEventAsync("terminated", new Dictionary<string, object>());
                    }
                });
            }
            return CreateResponse(request, true);
        }

        private DAPResponse HandleThreads(DAPMessage request)
        {
            var response = CreateResponse(request, true);
            response.Body = new Dictionary<string, object>
            {
                ["threads"] = new[]
                {
                    new Dictionary<string, object>
                    {
                        ["id"] = 1,
                        ["name"] = "Main Thread"
                    }
                }
            };
            return response;
        }

        private DAPResponse HandleStackTrace(DAPMessage request)
        {
            var frames = _interpreter?.GetStackFrames() ?? new List<StackFrameInfo>();
            var stackFrames = new List<object>();

            for (int i = 0; i < frames.Count; i++)
            {
                var frame = frames[i];
                stackFrames.Add(new Dictionary<string, object>
                {
                    ["id"] = i,
                    ["name"] = frame.FunctionName,
                    ["source"] = new Dictionary<string, object>
                    {
                        ["path"] = _currentFile,
                        ["name"] = Path.GetFileName(_currentFile)
                    },
                    ["line"] = frame.Line,
                    ["column"] = 1
                });
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
            var frameId = 0;
            if (args.TryGetProperty("frameId", out var frameProp))
            {
                frameId = frameProp.GetInt32();
            }

            var localVarRef = _nextVariableRef++;
            _variables[localVarRef] = new VariableInfo { FrameId = frameId, Type = "locals" };

            var response = CreateResponse(request, true);
            response.Body = new Dictionary<string, object>
            {
                ["scopes"] = new[]
                {
                    new Dictionary<string, object>
                    {
                        ["name"] = "Locals",
                        ["variablesReference"] = localVarRef,
                        ["expensive"] = false
                    }
                }
            };
            return response;
        }

        private DAPResponse HandleVariables(DAPMessage request)
        {
            var args = request.Arguments;
            var varRef = 0;
            if (args.TryGetProperty("variablesReference", out var refProp))
            {
                varRef = refProp.GetInt32();
            }

            var variables = new List<object>();

            if (_variables.TryGetValue(varRef, out var varInfo) && _interpreter != null)
            {
                if (varInfo.Type == "locals")
                {
                    // Get local variables for this frame
                    var locals = _interpreter.GetLocalVariables(varInfo.FrameId);
                    foreach (var local in locals)
                    {
                        variables.Add(CreateVariableResponse(local.Key, local.Value));
                    }
                }
                else if (varInfo.Type == "object" && varInfo.Value != null)
                {
                    // Expand object properties
                    var obj = varInfo.Value;
                    var type = obj.GetType();

                    // Get all public properties
                    foreach (var prop in type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                    {
                        try
                        {
                            var value = prop.GetValue(obj);
                            variables.Add(CreateVariableResponse(prop.Name, value, $"({prop.PropertyType.Name})"));
                        }
                        catch
                        {
                            variables.Add(new Dictionary<string, object>
                            {
                                ["name"] = prop.Name,
                                ["value"] = "<error reading property>",
                                ["type"] = prop.PropertyType.Name,
                                ["variablesReference"] = 0
                            });
                        }
                    }

                    // Get all public fields
                    foreach (var field in type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                    {
                        try
                        {
                            var value = field.GetValue(obj);
                            variables.Add(CreateVariableResponse(field.Name, value, $"({field.FieldType.Name})"));
                        }
                        catch
                        {
                            variables.Add(new Dictionary<string, object>
                            {
                                ["name"] = field.Name,
                                ["value"] = "<error reading field>",
                                ["type"] = field.FieldType.Name,
                                ["variablesReference"] = 0
                            });
                        }
                    }
                }
                else if (varInfo.Type == "array" && varInfo.Value is System.Collections.IEnumerable enumerable)
                {
                    // Expand array/collection elements
                    int index = 0;
                    foreach (var item in enumerable)
                    {
                        variables.Add(CreateVariableResponse($"[{index}]", item));
                        index++;
                        if (index > 1000) // Limit to first 1000 elements
                        {
                            variables.Add(new Dictionary<string, object>
                            {
                                ["name"] = "...",
                                ["value"] = $"({index} more items)",
                                ["type"] = "",
                                ["variablesReference"] = 0
                            });
                            break;
                        }
                    }
                }
            }

            var response = CreateResponse(request, true);
            response.Body = new Dictionary<string, object>
            {
                ["variables"] = variables
            };
            return response;
        }

        private Dictionary<string, object> CreateVariableResponse(string name, object value, string typePrefix = "")
        {
            if (value == null)
            {
                return new Dictionary<string, object>
                {
                    ["name"] = name,
                    ["value"] = "Nothing",
                    ["type"] = "Object",
                    ["variablesReference"] = 0
                };
            }

            var type = value.GetType();
            var typeName = typePrefix + type.Name;
            int varRef = 0;
            string displayValue;

            // Handle primitive types
            if (type.IsPrimitive || value is string || value is decimal)
            {
                displayValue = FormatValue(value);
            }
            // Handle arrays and collections
            else if (value is System.Collections.IEnumerable enumerable && !(value is string))
            {
                int count = 0;
                foreach (var _ in enumerable) count++;

                displayValue = $"{type.Name} ({count} items)";
                varRef = _nextVariableRef++;
                _variables[varRef] = new VariableInfo
                {
                    Type = "array",
                    Value = value
                };
            }
            // Handle complex objects
            else
            {
                displayValue = $"{{{type.Name}}}";
                varRef = _nextVariableRef++;
                _variables[varRef] = new VariableInfo
                {
                    Type = "object",
                    Value = value
                };
            }

            return new Dictionary<string, object>
            {
                ["name"] = name,
                ["value"] = displayValue,
                ["type"] = typeName,
                ["variablesReference"] = varRef
            };
        }

        private string FormatValue(object value)
        {
            if (value == null) return "Nothing";
            if (value is string s) return $"\"{s}\"";
            if (value is char c) return $"'{c}'";
            if (value is bool b) return b ? "True" : "False";
            if (value is float f) return f.ToString("G") + "F";
            if (value is double d) return d.ToString("G");
            if (value is decimal dec) return dec.ToString("G") + "D";
            return value.ToString();
        }

        private void ClearVariableReferences()
        {
            _variables.Clear();
            _stackFrames.Clear();
            _nextVariableRef = 1;
        }

        private DAPResponse HandleContinue(DAPMessage request)
        {
            ClearVariableReferences();
            Task.Run(() => _interpreter?.Continue());
            var response = CreateResponse(request, true);
            response.Body = new Dictionary<string, object>
            {
                ["allThreadsContinued"] = true
            };
            return response;
        }

        private DAPResponse HandleNext(DAPMessage request)
        {
            ClearVariableReferences();
            Task.Run(() => _interpreter?.StepOver());
            return CreateResponse(request, true);
        }

        private DAPResponse HandleStepIn(DAPMessage request)
        {
            ClearVariableReferences();
            Task.Run(() => _interpreter?.StepInto());
            return CreateResponse(request, true);
        }

        private DAPResponse HandleStepOut(DAPMessage request)
        {
            ClearVariableReferences();
            Task.Run(() => _interpreter?.StepOut());
            return CreateResponse(request, true);
        }

        private DAPResponse HandlePause(DAPMessage request)
        {
            _interpreter?.Pause();
            return CreateResponse(request, true);
        }

        private DAPResponse HandleDisconnect(DAPMessage request)
        {
            _running = false;
            _interpreter?.Stop();
            return CreateResponse(request, true);
        }

        private DAPResponse HandleEvaluate(DAPMessage request)
        {
            var args = request.Arguments;
            var expression = "";
            if (args.TryGetProperty("expression", out var exprProp))
            {
                expression = exprProp.GetString();
            }

            var result = _interpreter?.EvaluateExpression(expression);

            var response = CreateResponse(request, true);
            response.Body = new Dictionary<string, object>
            {
                ["result"] = result?.ToString() ?? "undefined",
                ["variablesReference"] = 0
            };
            return response;
        }

        private DAPResponse HandleGotoTargets(DAPMessage request)
        {
            var args = request.Arguments;
            var response = CreateResponse(request, true);

            var targets = new List<object>();
            if (_interpreter != null && args.TryGetProperty("line", out var lineProp))
            {
                var targetLine = lineProp.GetInt32();
                var executableLines = _interpreter.GetExecutableLines();

                // Return the target line if executable, or nearest executable lines
                for (int d = 0; d <= 5; d++)
                {
                    if (executableLines.Contains(targetLine + d))
                    {
                        var line = targetLine + d;
                        targets.Add(new Dictionary<string, object>
                        {
                            ["id"] = line,
                            ["label"] = $"Line {line}",
                            ["line"] = line
                        });
                    }
                    if (d > 0 && executableLines.Contains(targetLine - d))
                    {
                        var line = targetLine - d;
                        targets.Add(new Dictionary<string, object>
                        {
                            ["id"] = line,
                            ["label"] = $"Line {line}",
                            ["line"] = line
                        });
                    }
                }
            }

            response.Body = new Dictionary<string, object>
            {
                ["targets"] = targets
            };
            return response;
        }

        private DAPResponse HandleGoto(DAPMessage request)
        {
            var args = request.Arguments;
            var response = CreateResponse(request, true);

            if (_interpreter != null && args.TryGetProperty("targetId", out var targetIdProp))
            {
                var targetLine = targetIdProp.GetInt32();
                var success = _interpreter.SetNextStatement(_currentFile, targetLine);

                if (success)
                {
                    _ = Task.Run(async () =>
                    {
                        await SendEventAsync("stopped", new Dictionary<string, object>
                        {
                            ["reason"] = "goto",
                            ["threadId"] = 1,
                            ["allThreadsStopped"] = true
                        });
                    });
                }
                else
                {
                    response.Success = false;
                    response.Body = new Dictionary<string, object>
                    {
                        ["error"] = new Dictionary<string, object>
                        {
                            ["id"] = 1,
                            ["format"] = "Cannot set next statement while running"
                        }
                    };
                }
            }

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

            _interpreter?.SetExceptionFilters(filters);

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

        // HandleSetDataBreakpoints and HandleDataBreakpointInfo defined earlier in the file

        private bool ValidateBreakpointLine(string path, ref int line, Breakpoint breakpoint)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return true; // Can't validate, assume ok

            try
            {
                var lines = File.ReadAllLines(path);
                if (line < 1 || line > lines.Length)
                {
                    breakpoint.Message = $"Line {line} is beyond end of file ({lines.Length} lines)";
                    return false;
                }

                var lineText = lines[line - 1].Trim();

                // Empty line or comment-only
                if (string.IsNullOrWhiteSpace(lineText) || lineText.StartsWith("'") ||
                    lineText.StartsWith("REM ", StringComparison.OrdinalIgnoreCase))
                {
                    // Try to find nearest executable line
                    if (_interpreter != null)
                    {
                        var nearest = _interpreter.FindNearestExecutableLine(line, 5);
                        if (nearest > 0)
                        {
                            breakpoint.Line = nearest;
                            line = nearest;
                            return true;
                        }
                    }

                    // Fallback: search adjacent lines in source
                    for (int d = 1; d <= 5; d++)
                    {
                        var checkIdx = line - 1 + d;
                        if (checkIdx < lines.Length)
                        {
                            var checkText = lines[checkIdx].Trim();
                            if (!string.IsNullOrWhiteSpace(checkText) && !checkText.StartsWith("'") &&
                                !checkText.StartsWith("REM ", StringComparison.OrdinalIgnoreCase))
                            {
                                line = checkIdx + 1;
                                breakpoint.Line = line;
                                return true;
                            }
                        }
                    }

                    breakpoint.Message = "No executable code at this line";
                    return false;
                }

                return true;
            }
            catch
            {
                return true; // Can't read file, assume ok
            }
        }

        private void OnBreakpointHit(object sender, DebugEventArgs e)
        {
            // Send synchronously on the interpreter thread -- completes before _pauseEvent.Wait() blocks
            SendEventAsync("stopped", new Dictionary<string, object>
            {
                ["reason"] = "breakpoint",
                ["threadId"] = 1,
                ["allThreadsStopped"] = true
            }).GetAwaiter().GetResult();
        }

        private void OnStepComplete(object sender, DebugEventArgs e)
        {
            // Send synchronously on the interpreter thread
            SendEventAsync("stopped", new Dictionary<string, object>
            {
                ["reason"] = "step",
                ["threadId"] = 1,
                ["allThreadsStopped"] = true
            }).GetAwaiter().GetResult();
        }

        private void OnOutputProduced(object sender, OutputEventArgs e)
        {
            // Send synchronously to ensure output ordering is preserved
            SendEventAsync("output", new Dictionary<string, object>
            {
                ["category"] = "stdout",
                ["output"] = e.Text
            }).GetAwaiter().GetResult();
        }

        private void OnLogpointHit(object sender, LogpointEventArgs e)
        {
            Task.Run(() => SendEventAsync("output", new Dictionary<string, object>
            {
                ["category"] = "console",
                ["output"] = $"[Logpoint] {e.Message}\n",
                ["source"] = new Dictionary<string, object>
                {
                    ["path"] = e.File,
                    ["name"] = Path.GetFileName(e.File)
                },
                ["line"] = e.Line
            }));
        }

        private void OnDataBreakpointHit(object sender, DataBreakpointEventArgs e)
        {
            // Send stopped event with reason "data breakpoint" synchronously on the interpreter thread
            // The interpreter will wait on _pauseEvent after this event fires
            SendEventAsync("stopped", new Dictionary<string, object>
            {
                ["reason"] = "data breakpoint",
                ["description"] = $"Data breakpoint on '{e.VariableName}': {FormatDataBreakpointMessage(e)}",
                ["threadId"] = 1,
                ["allThreadsStopped"] = true
            }).GetAwaiter().GetResult();
        }

        private string FormatDataBreakpointMessage(DataBreakpointEventArgs e)
        {
            var oldStr = e.OldValue?.ToString() ?? "Nothing";
            var newStr = e.NewValue?.ToString() ?? "Nothing";

            return e.AccessType switch
            {
                DataBreakpointAccessType.Write => $"value changed from {oldStr} to {newStr}",
                DataBreakpointAccessType.Read => $"value read: {newStr}",
                DataBreakpointAccessType.ReadWrite => $"accessed (value: {newStr})",
                _ => $"triggered"
            };
        }

        private DAPResponse CreateResponse(DAPMessage request, bool success)
        {
            return new DAPResponse
            {
                Seq = Interlocked.Increment(ref _seq),
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
                Seq = Interlocked.Increment(ref _seq),
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
    }

    public class DAPMessage
    {
        [JsonPropertyName("seq")]
        public int Seq { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("command")]
        public string Command { get; set; } = string.Empty;

        [JsonPropertyName("arguments")]
        public JsonElement Arguments { get; set; }
    }

    public class DAPResponse
    {
        [JsonPropertyName("seq")]
        public int Seq { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; } = "response";

        [JsonPropertyName("request_seq")]
        public int RequestSeq { get; set; }

        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("command")]
        public string Command { get; set; } = string.Empty;

        [JsonPropertyName("message")]
        public string Message { get; set; }

        [JsonPropertyName("body")]
        public object? Body { get; set; }
    }

    public class DAPEvent
    {
        [JsonPropertyName("seq")]
        public int Seq { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; } = "event";

        [JsonPropertyName("event")]
        public string Event { get; set; } = string.Empty;

        [JsonPropertyName("body")]
        public object? Body { get; set; }
    }

    public class VariableInfo
    {
        public int FrameId { get; set; }
        public string Type { get; set; } = string.Empty;
        public object? Value { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class StackFrameInfo
    {
        public string FunctionName { get; set; } = string.Empty;
        public int Line { get; set; }
    }

    public class DebugEventArgs : EventArgs
    {
        public int Line { get; set; }
        public string File { get; set; } = string.Empty;
    }

    public class OutputEventArgs : EventArgs
    {
        public string Text { get; set; } = string.Empty;
    }
}
