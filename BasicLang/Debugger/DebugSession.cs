using System;
using System.Collections.Generic;
using System.IO;
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
        private readonly Stream _output;
        private readonly Dictionary<int, Breakpoint> _breakpoints = new();
        private readonly Dictionary<int, VariableInfo> _variables = new();
        private readonly List<StackFrameInfo> _stackFrames = new();

        private DebuggableInterpreter _interpreter;
        private string _currentFile;
        private bool _running;
        private bool _stopOnEntry;
        private int _nextVariableRef = 1;
        private int _seq = 0;
        private int _nextBreakpointId = 1;

        public DebugSession(Stream input, Stream output)
        {
            _input = input;
            _output = output;
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
                ["supportsStepBack"] = false,
                ["supportsSetVariable"] = false,
                ["supportsRestartFrame"] = false,
                ["supportsGotoTargetsRequest"] = false,
                ["supportsStepInTargetsRequest"] = false,
                ["supportsCompletionsRequest"] = false,
                ["supportsModulesRequest"] = false,
                ["supportsExceptionOptions"] = false,
                ["supportsValueFormattingOptions"] = false,
                ["supportsExceptionInfoRequest"] = false,
                ["supportTerminateDebuggee"] = true,
                ["supportsDelayedStackTraceLoading"] = false,
                ["supportsLoadedSourcesRequest"] = false
            };

            // Send initialized event
            Task.Run(async () =>
            {
                await Task.Delay(100);
                await SendEventAsync("initialized", null);
            });

            return response;
        }

        private async Task<DAPResponse> HandleLaunchAsync(DAPMessage request)
        {
            var args = request.Arguments;

            if (args.TryGetProperty("program", out var programProp))
            {
                _currentFile = programProp.GetString();
            }

            if (args.TryGetProperty("stopOnEntry", out var stopProp))
            {
                _stopOnEntry = stopProp.GetBoolean();
            }

            // Compile and prepare the program
            if (!string.IsNullOrEmpty(_currentFile) && File.Exists(_currentFile))
            {
                try
                {
                    var source = await File.ReadAllTextAsync(_currentFile);
                    var lexer = new Lexer(source);
                    var tokens = lexer.Tokenize();
                    var parser = new Parser(tokens);
                    var ast = parser.Parse();

                    var semanticAnalyzer = new SemanticAnalyzer();
                    semanticAnalyzer.Analyze(ast);

                    var irBuilder = new IRBuilder(semanticAnalyzer);
                    var module = irBuilder.Build(ast);

                    _interpreter = new DebuggableInterpreter(module);
                    _interpreter.SetCurrentFile(_currentFile);
                    _interpreter.BreakpointHit += OnBreakpointHit;
                    _interpreter.StepComplete += OnStepComplete;
                    _interpreter.OutputProduced += OnOutputProduced;
                    _interpreter.LogpointHit += OnLogpointHit;

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

                            // Validate breakpoint (would need source code access)
                            breakpoint.Verified = true; // For now, always verified

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
                Task.Run(() => _interpreter.Run());
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

        private DAPResponse HandleContinue(DAPMessage request)
        {
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
            Task.Run(() => _interpreter?.StepOver());
            return CreateResponse(request, true);
        }

        private DAPResponse HandleStepIn(DAPMessage request)
        {
            Task.Run(() => _interpreter?.StepInto());
            return CreateResponse(request, true);
        }

        private DAPResponse HandleStepOut(DAPMessage request)
        {
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

        private void OnBreakpointHit(object sender, DebugEventArgs e)
        {
            Task.Run(() => SendEventAsync("stopped", new Dictionary<string, object>
            {
                ["reason"] = "breakpoint",
                ["threadId"] = 1,
                ["allThreadsStopped"] = true
            }));
        }

        private void OnStepComplete(object sender, DebugEventArgs e)
        {
            Task.Run(() => SendEventAsync("stopped", new Dictionary<string, object>
            {
                ["reason"] = "step",
                ["threadId"] = 1,
                ["allThreadsStopped"] = true
            }));
        }

        private void OnOutputProduced(object sender, OutputEventArgs e)
        {
            Task.Run(() => SendEventAsync("output", new Dictionary<string, object>
            {
                ["category"] = "stdout",
                ["output"] = e.Text
            }));
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

        private DAPResponse CreateResponse(DAPMessage request, bool success)
        {
            return new DAPResponse
            {
                Seq = ++_seq,
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
                Seq = ++_seq,
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

        private async Task SendMessageAsync(string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            var header = $"Content-Length: {bytes.Length}\r\n\r\n";
            var headerBytes = Encoding.UTF8.GetBytes(header);

            await _output.WriteAsync(headerBytes, 0, headerBytes.Length);
            await _output.WriteAsync(bytes, 0, bytes.Length);
            await _output.FlushAsync();
        }
    }

    public class DAPMessage
    {
        [JsonPropertyName("seq")]
        public int Seq { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("command")]
        public string Command { get; set; }

        [JsonPropertyName("arguments")]
        public JsonElement Arguments { get; set; }
    }

    public class DAPResponse
    {
        [JsonPropertyName("seq")]
        public int Seq { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("request_seq")]
        public int RequestSeq { get; set; }

        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("command")]
        public string Command { get; set; }

        [JsonPropertyName("body")]
        public object Body { get; set; }
    }

    public class DAPEvent
    {
        [JsonPropertyName("seq")]
        public int Seq { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("event")]
        public string Event { get; set; }

        [JsonPropertyName("body")]
        public object Body { get; set; }
    }

    public class VariableInfo
    {
        public int FrameId { get; set; }
        public string Type { get; set; }
        public object Value { get; set; }
        public string Name { get; set; }
    }

    public class StackFrameInfo
    {
        public string FunctionName { get; set; }
        public int Line { get; set; }
    }

    public class DebugEventArgs : EventArgs
    {
        public int Line { get; set; }
        public string File { get; set; }
    }

    public class OutputEventArgs : EventArgs
    {
        public string Text { get; set; }
    }
}
