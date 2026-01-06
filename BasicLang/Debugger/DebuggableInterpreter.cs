using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;

namespace BasicLang.Debugger
{
    /// <summary>
    /// An interpreter that supports debugging operations
    /// </summary>
    public class DebuggableInterpreter
    {
        private readonly IRModule _module;
        private readonly BreakpointManager _breakpointManager = new();
        private readonly Dictionary<string, object> _globalVariables = new();
        private readonly Stack<CallFrame> _callStack = new();
        private readonly Random _random = new();

        private bool _paused;
        private bool _stepping;
        private StepMode _stepMode;
        private int _stepDepth;
        private bool _stopRequested;
        private int _currentLine;
        private string _currentFile;
        private readonly object _syncLock = new();
        private readonly ManualResetEventSlim _pauseEvent = new(true);

        public event EventHandler<DebugEventArgs> BreakpointHit;
        public event EventHandler<DebugEventArgs> StepComplete;
        public event EventHandler<OutputEventArgs> OutputProduced;
        public event EventHandler<LogpointEventArgs> LogpointHit;

        private enum StepMode
        {
            None,
            Into,
            Over,
            Out
        }

        public DebuggableInterpreter(IRModule module)
        {
            _module = module;
        }

        /// <summary>
        /// Set the current source file path for debugging
        /// </summary>
        public void SetCurrentFile(string filePath)
        {
            _currentFile = filePath;
        }

        /// <summary>
        /// Add a breakpoint
        /// </summary>
        public Breakpoint AddBreakpoint(Breakpoint breakpoint)
        {
            lock (_syncLock)
            {
                return _breakpointManager.AddBreakpoint(breakpoint);
            }
        }

        /// <summary>
        /// Remove a breakpoint by ID
        /// </summary>
        public bool RemoveBreakpoint(int id)
        {
            lock (_syncLock)
            {
                return _breakpointManager.RemoveBreakpoint(id);
            }
        }

        /// <summary>
        /// Legacy method for compatibility
        /// </summary>
        public void SetBreakpoint(string file, int line)
        {
            lock (_syncLock)
            {
                var breakpoint = new Breakpoint
                {
                    Type = BreakpointType.Line,
                    FilePath = file,
                    Line = line,
                    Column = 0
                };
                _breakpointManager.AddBreakpoint(breakpoint);
            }
        }

        /// <summary>
        /// Get all breakpoints
        /// </summary>
        public List<Breakpoint> GetAllBreakpoints()
        {
            lock (_syncLock)
            {
                return _breakpointManager.GetAllBreakpoints();
            }
        }

        /// <summary>
        /// Clear all breakpoints
        /// </summary>
        public void ClearAllBreakpoints()
        {
            lock (_syncLock)
            {
                _breakpointManager.ClearAll();
            }
        }

        public void Run()
        {
            _stopRequested = false;
            _paused = false;
            _pauseEvent.Set();

            try
            {
                // Find and execute Main or first function
                var mainFunc = _module.Functions.FirstOrDefault(f =>
                    f.Name.Equals("Main", StringComparison.OrdinalIgnoreCase));

                if (mainFunc != null)
                {
                    ExecuteFunction(mainFunc, new object[0]);
                }
                else if (_module.Functions.Count > 0)
                {
                    ExecuteFunction(_module.Functions[0], new object[0]);
                }
            }
            catch (Exception ex)
            {
                OnOutput($"Runtime error: {ex.Message}\n");
            }
        }

        public void Continue()
        {
            lock (_syncLock)
            {
                _stepping = false;
                _paused = false;
                _pauseEvent.Set();
            }
        }

        public void StepOver()
        {
            lock (_syncLock)
            {
                _stepping = true;
                _stepMode = StepMode.Over;
                _stepDepth = _callStack.Count;
                _paused = false;
                _pauseEvent.Set();
            }
        }

        public void StepInto()
        {
            lock (_syncLock)
            {
                _stepping = true;
                _stepMode = StepMode.Into;
                _paused = false;
                _pauseEvent.Set();
            }
        }

        public void StepOut()
        {
            lock (_syncLock)
            {
                _stepping = true;
                _stepMode = StepMode.Out;
                _stepDepth = _callStack.Count - 1;
                _paused = false;
                _pauseEvent.Set();
            }
        }

        public void Pause()
        {
            lock (_syncLock)
            {
                _paused = true;
                _pauseEvent.Reset();
            }
        }

        public void Stop()
        {
            lock (_syncLock)
            {
                _stopRequested = true;
                _pauseEvent.Set();
            }
        }

        public List<StackFrameInfo> GetStackFrames()
        {
            var frames = new List<StackFrameInfo>();
            lock (_syncLock)
            {
                foreach (var frame in _callStack)
                {
                    frames.Add(new StackFrameInfo
                    {
                        FunctionName = frame.FunctionName,
                        Line = frame.CurrentLine
                    });
                }
            }
            return frames;
        }

        public Dictionary<string, object> GetLocalVariables(int frameId)
        {
            lock (_syncLock)
            {
                var frames = new List<CallFrame>(_callStack);
                if (frameId >= 0 && frameId < frames.Count)
                {
                    return new Dictionary<string, object>(frames[frameId].LocalVariables);
                }
            }
            return new Dictionary<string, object>();
        }

        public object EvaluateExpression(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression))
                return null;

            lock (_syncLock)
            {
                try
                {
                    var evaluator = new DebugExpressionEvaluator(this);
                    return evaluator.Evaluate(expression);
                }
                catch
                {
                    // Fallback to simple variable lookup
                    if (_callStack.Count > 0)
                    {
                        var frame = _callStack.Peek();
                        if (frame.LocalVariables.TryGetValue(expression, out var value))
                            return value;
                    }
                    if (_globalVariables.TryGetValue(expression, out var globalValue))
                        return globalValue;
                    return null;
                }
            }
        }

        /// <summary>
        /// Get a variable value from current scope
        /// </summary>
        internal object GetVariable(string name)
        {
            if (_callStack.Count > 0)
            {
                var frame = _callStack.Peek();
                if (frame.LocalVariables.TryGetValue(name, out var value))
                    return value;
            }
            if (_globalVariables.TryGetValue(name, out var globalValue))
                return globalValue;
            return null;
        }

        private void CheckBreakpoint(int line, int column = 0)
        {
            if (_stopRequested) return;

            _currentLine = line;

            // Check for breakpoints at this location
            bool shouldBreak = false;
            List<Breakpoint> triggeredLogpoints = new();

            lock (_syncLock)
            {
                var breakpoints = _breakpointManager.GetBreakpointsAtLocation(_currentFile, line);

                foreach (var bp in breakpoints)
                {
                    // Check column if specified
                    if (bp.Column > 0 && column > 0 && bp.Column != column)
                        continue;

                    // Handle logpoints separately
                    if (bp.Type == BreakpointType.Logpoint)
                    {
                        triggeredLogpoints.Add(bp);
                        continue;
                    }

                    // Check if this breakpoint should trigger
                    if (bp.ShouldBreak(this))
                    {
                        shouldBreak = true;
                        break;
                    }
                }
            }

            // Process logpoints (these don't stop execution)
            foreach (var logpoint in triggeredLogpoints)
            {
                var message = logpoint.FormatLogMessage(this);
                LogpointHit?.Invoke(this, new LogpointEventArgs
                {
                    Line = line,
                    File = _currentFile,
                    Message = message
                });
            }

            if (shouldBreak)
            {
                _paused = true;
                BreakpointHit?.Invoke(this, new DebugEventArgs { Line = line, File = _currentFile });
                _pauseEvent.Reset();
            }
            else if (_stepping)
            {
                bool shouldStop = false;
                lock (_syncLock)
                {
                    switch (_stepMode)
                    {
                        case StepMode.Into:
                            shouldStop = true;
                            break;
                        case StepMode.Over:
                            shouldStop = _callStack.Count <= _stepDepth;
                            break;
                        case StepMode.Out:
                            shouldStop = _callStack.Count < _stepDepth;
                            break;
                    }
                }

                if (shouldStop)
                {
                    _stepping = false;
                    _paused = true;
                    StepComplete?.Invoke(this, new DebugEventArgs { Line = line, File = _currentFile });
                    _pauseEvent.Reset();
                }
            }

            // Wait if paused
            _pauseEvent.Wait();
        }

        private object ExecuteFunction(IRFunction function, object[] arguments)
        {
            // Check for function breakpoints
            lock (_syncLock)
            {
                var funcBreakpoint = _breakpointManager.GetFunctionBreakpoint(function.Name);
                if (funcBreakpoint != null && funcBreakpoint.Enabled)
                {
                    if (funcBreakpoint.ShouldBreak(this))
                    {
                        _paused = true;
                        BreakpointHit?.Invoke(this, new DebugEventArgs
                        {
                            Line = 1, // Function entry
                            File = _currentFile
                        });
                        _pauseEvent.Reset();
                        _pauseEvent.Wait();
                    }
                }
            }

            var frame = new CallFrame
            {
                FunctionName = function.Name,
                LocalVariables = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            };

            // Set up parameters
            for (int i = 0; i < function.Parameters.Count && i < arguments.Length; i++)
            {
                frame.LocalVariables[function.Parameters[i].Name] = arguments[i];
            }

            lock (_syncLock)
            {
                _callStack.Push(frame);
            }

            try
            {
                if (function.EntryBlock != null)
                {
                    return ExecuteBlock(function.EntryBlock, frame);
                }
                else if (function.Blocks.Count > 0)
                {
                    return ExecuteBlock(function.Blocks[0], frame);
                }
                return null;
            }
            finally
            {
                lock (_syncLock)
                {
                    _callStack.Pop();
                }
            }
        }

        private object ExecuteBlock(BasicBlock block, CallFrame frame)
        {
            frame.CurrentLine = Math.Abs(block.Name?.GetHashCode() ?? 1) % 1000 + 1; // Approximate line

            foreach (var instruction in block.Instructions)
            {
                if (_stopRequested) return null;

                CheckBreakpoint(frame.CurrentLine);
                var result = ExecuteInstruction(instruction, frame);

                if (result is ReturnValue rv)
                    return rv.Value;
            }

            // Handle terminator
            var terminator = block.GetTerminator();
            if (terminator != null)
            {
                if (terminator is IRBranch branch)
                {
                    return ExecuteBlock(branch.Target, frame);
                }
                else if (terminator is IRConditionalBranch condBranch)
                {
                    var cond = EvaluateValue(condBranch.Condition, frame);
                    var target = Convert.ToBoolean(cond) ? condBranch.TrueTarget : condBranch.FalseTarget;
                    return ExecuteBlock(target, frame);
                }
                else if (terminator is IRReturn ret)
                {
                    return ret.Value != null ? EvaluateValue(ret.Value, frame) : null;
                }
            }

            // Continue to successor if exists
            if (block.Successors.Count > 0)
            {
                return ExecuteBlock(block.Successors[0], frame);
            }

            return null;
        }

        private object ExecuteInstruction(IRInstruction instruction, CallFrame frame)
        {
            switch (instruction)
            {
                case IRBinaryOp binOp:
                    var left = EvaluateValue(binOp.Left, frame);
                    var right = EvaluateValue(binOp.Right, frame);
                    var result = PerformBinaryOp(binOp.Operation, left, right);
                    frame.LocalVariables[binOp.Name] = result;
                    break;

                case IRUnaryOp unaryOp:
                    var operand = EvaluateValue(unaryOp.Operand, frame);
                    var unaryResult = PerformUnaryOp(unaryOp.Operation, operand);
                    frame.LocalVariables[unaryOp.Name] = unaryResult;
                    break;

                case IRCall call:
                    var callResult = ExecuteCall(call, frame);
                    if (!string.IsNullOrEmpty(call.Name))
                        frame.LocalVariables[call.Name] = callResult;
                    break;

                case IRStore store:
                    var storeValue = EvaluateValue(store.Value, frame);
                    if (store.Address is IRVariable varRef)
                        frame.LocalVariables[varRef.Name] = storeValue;
                    break;

                case IRAlloca alloca:
                    frame.LocalVariables[alloca.Name] = GetDefaultValue(alloca.Type);
                    break;

                case IRAssignment assignment:
                    var assignValue = EvaluateValue(assignment.Value, frame);
                    frame.LocalVariables[assignment.Target.Name] = assignValue;
                    break;

                case IRReturn ret:
                    var retValue = ret.Value != null ? EvaluateValue(ret.Value, frame) : null;
                    return new ReturnValue { Value = retValue };
            }

            return null;
        }

        private object EvaluateValue(IRValue value, CallFrame frame)
        {
            switch (value)
            {
                case IRConstant constant:
                    return constant.Value;

                case IRVariable variable:
                    if (frame.LocalVariables.TryGetValue(variable.Name, out var localVal))
                        return localVal;
                    if (_globalVariables.TryGetValue(variable.Name, out var globalVal))
                        return globalVal;
                    return null;

                case IRLoad load:
                    return EvaluateValue(load.Address, frame);

                case IRBinaryOp binOp:
                    var left = EvaluateValue(binOp.Left, frame);
                    var right = EvaluateValue(binOp.Right, frame);
                    return PerformBinaryOp(binOp.Operation, left, right);

                case IRUnaryOp unaryOp:
                    var operand = EvaluateValue(unaryOp.Operand, frame);
                    return PerformUnaryOp(unaryOp.Operation, operand);

                case IRCall call:
                    return ExecuteCall(call, frame);

                case IRFieldAccess fieldAccess:
                    return EvaluateFieldAccess(fieldAccess, frame);
            }

            return null;
        }

        /// <summary>
        /// Evaluate field/property access like Environment.MachineName or DateTime.Now
        /// </summary>
        private object EvaluateFieldAccess(IRFieldAccess fieldAccess, CallFrame frame)
        {
            var obj = EvaluateValue(fieldAccess.Object, frame);
            var fieldName = fieldAccess.FieldName;

            // Check if this is a static property access on a .NET type
            if (fieldAccess.Object is IRVariable varRef)
            {
                var typeName = varRef.Name;

                // Handle DateTime static properties
                if (typeName.Equals("DateTime", StringComparison.OrdinalIgnoreCase))
                {
                    switch (fieldName.ToLowerInvariant())
                    {
                        case "now": return DateTime.Now;
                        case "utcnow": return DateTime.UtcNow;
                        case "today": return DateTime.Today;
                        case "minvalue": return DateTime.MinValue;
                        case "maxvalue": return DateTime.MaxValue;
                    }
                }

                // Handle Environment static properties
                if (typeName.Equals("Environment", StringComparison.OrdinalIgnoreCase))
                {
                    switch (fieldName.ToLowerInvariant())
                    {
                        case "machinename": return Environment.MachineName;
                        case "username": return Environment.UserName;
                        case "userdomain": return Environment.UserDomainName;
                        case "currentdirectory": return Environment.CurrentDirectory;
                        case "osversion": return Environment.OSVersion.ToString();
                        case "newline": return Environment.NewLine;
                        case "tickcount": return Environment.TickCount;
                        case "processorcount": return Environment.ProcessorCount;
                        case "is64bitoperatingsystem": return Environment.Is64BitOperatingSystem;
                        case "is64bitprocess": return Environment.Is64BitProcess;
                    }
                }

                // Handle String static properties
                if (typeName.Equals("String", StringComparison.OrdinalIgnoreCase))
                {
                    if (fieldName.Equals("Empty", StringComparison.OrdinalIgnoreCase))
                        return string.Empty;
                }

                // Handle Console properties
                if (typeName.Equals("Console", StringComparison.OrdinalIgnoreCase))
                {
                    switch (fieldName.ToLowerInvariant())
                    {
                        case "title": return "[Console Title]";
                        case "cursorleft": return 0;
                        case "cursortop": return 0;
                        case "windowwidth": return 80;
                        case "windowheight": return 25;
                    }
                }

                // Handle Directory static properties
                if (typeName.Equals("Directory", StringComparison.OrdinalIgnoreCase))
                {
                    // Directory doesn't have many static properties, but check just in case
                }

                // Handle Path static properties
                if (typeName.Equals("Path", StringComparison.OrdinalIgnoreCase))
                {
                    switch (fieldName.ToLowerInvariant())
                    {
                        case "directoryseparatorchar": return System.IO.Path.DirectorySeparatorChar;
                        case "altdirectoryseparatorchar": return System.IO.Path.AltDirectorySeparatorChar;
                        case "pathseparator": return System.IO.Path.PathSeparator;
                        case "volumeseparatorchar": return System.IO.Path.VolumeSeparatorChar;
                    }
                }

                // Try reflection for other .NET types
                var netResult = GetStaticPropertyViaReflection(typeName, fieldName);
                if (netResult != NetCallNotFound)
                    return netResult;
            }

            // Instance property access
            if (obj != null)
            {
                try
                {
                    var type = obj.GetType();
                    var prop = type.GetProperty(fieldName, System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
                    if (prop != null)
                        return prop.GetValue(obj);

                    var field = type.GetField(fieldName, System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
                    if (field != null)
                        return field.GetValue(obj);
                }
                catch
                {
                    // Ignore reflection errors
                }
            }

            return null;
        }

        /// <summary>
        /// Get a static property value via reflection
        /// </summary>
        private object GetStaticPropertyViaReflection(string typeName, string propertyName)
        {
            // Try to find the type in common assemblies
            Type type = Type.GetType($"System.{typeName}") ??
                        Type.GetType($"System.IO.{typeName}") ??
                        Type.GetType($"System.Text.{typeName}") ??
                        Type.GetType(typeName);

            if (type == null)
                return NetCallNotFound;

            try
            {
                var prop = type.GetProperty(propertyName, System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.IgnoreCase);
                if (prop != null)
                    return prop.GetValue(null);

                var field = type.GetField(propertyName, System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.IgnoreCase);
                if (field != null)
                    return field.GetValue(null);
            }
            catch
            {
                // Ignore reflection errors
            }

            return NetCallNotFound;
        }

        private object ExecuteCall(IRCall call, CallFrame frame)
        {
            var args = new List<object>();
            foreach (var arg in call.Arguments)
            {
                args.Add(EvaluateValue(arg, frame));
            }

            // Check built-in functions first
            var builtInResult = ExecuteBuiltIn(call.FunctionName, args.ToArray());
            if (builtInResult != BuiltInNotFound)
                return builtInResult;

            // Check for .NET static method calls (e.g., Console.WriteLine, Math.Sqrt)
            if (call.FunctionName.Contains("."))
            {
                var netResult = ExecuteNetStaticCall(call.FunctionName, args.ToArray());
                if (netResult != NetCallNotFound)
                    return netResult;
            }

            // User function
            var func = _module.Functions.FirstOrDefault(f =>
                f.Name.Equals(call.FunctionName, StringComparison.OrdinalIgnoreCase));

            if (func != null)
            {
                return ExecuteFunction(func, args.ToArray());
            }

            return null;
        }

        // Sentinel objects to distinguish "not found" from null return
        private static readonly object BuiltInNotFound = new object();
        private static readonly object NetCallNotFound = new object();

        /// <summary>
        /// Execute .NET static method calls like Console.WriteLine, Math.Sqrt, etc.
        /// </summary>
        private object ExecuteNetStaticCall(string fullName, object[] args)
        {
            var lastDot = fullName.LastIndexOf('.');
            if (lastDot < 0)
                return NetCallNotFound;

            var typeName = fullName.Substring(0, lastDot);
            var methodName = fullName.Substring(lastDot + 1);

            // Handle Console output methods - route to debug output
            if (typeName.Equals("Console", StringComparison.OrdinalIgnoreCase))
            {
                switch (methodName.ToLowerInvariant())
                {
                    case "writeline":
                        var text = args.Length > 0 ? args[0]?.ToString() ?? "" : "";
                        OnOutput(text + "\n");
                        return null;
                    case "write":
                        OnOutput(args.Length > 0 ? args[0]?.ToString() ?? "" : "");
                        return null;
                    case "clear":
                        // Just ignore Clear in debugger
                        return null;
                    case "readkey":
                    case "readline":
                        // Can't do input in debugger, return empty
                        OnOutput("[Debug: Input not available during debugging]\n");
                        return "";
                }
            }

            // Handle Math methods
            if (typeName.Equals("Math", StringComparison.OrdinalIgnoreCase))
            {
                return ExecuteMathMethod(methodName, args);
            }

            // Handle DateTime
            if (typeName.Equals("DateTime", StringComparison.OrdinalIgnoreCase))
            {
                return ExecuteDateTimeMethod(methodName, args);
            }

            // Handle Environment
            if (typeName.Equals("Environment", StringComparison.OrdinalIgnoreCase))
            {
                return ExecuteEnvironmentMethod(methodName, args);
            }

            // Handle File (System.IO)
            if (typeName.Equals("File", StringComparison.OrdinalIgnoreCase))
            {
                return ExecuteFileMethod(methodName, args);
            }

            // Handle Directory (System.IO)
            if (typeName.Equals("Directory", StringComparison.OrdinalIgnoreCase))
            {
                return ExecuteDirectoryMethod(methodName, args);
            }

            // Handle Path (System.IO)
            if (typeName.Equals("Path", StringComparison.OrdinalIgnoreCase))
            {
                return ExecutePathMethod(methodName, args);
            }

            // Try reflection for other .NET types
            return ExecuteNetCallViaReflection(typeName, methodName, args);
        }

        private object ExecuteMathMethod(string method, object[] args)
        {
            switch (method.ToLowerInvariant())
            {
                case "sqrt": return Math.Sqrt(Convert.ToDouble(args[0]));
                case "abs": return Math.Abs(Convert.ToDouble(args[0]));
                case "sin": return Math.Sin(Convert.ToDouble(args[0]));
                case "cos": return Math.Cos(Convert.ToDouble(args[0]));
                case "tan": return Math.Tan(Convert.ToDouble(args[0]));
                case "pow": return Math.Pow(Convert.ToDouble(args[0]), Convert.ToDouble(args[1]));
                case "max": return Math.Max(Convert.ToDouble(args[0]), Convert.ToDouble(args[1]));
                case "min": return Math.Min(Convert.ToDouble(args[0]), Convert.ToDouble(args[1]));
                case "floor": return Math.Floor(Convert.ToDouble(args[0]));
                case "ceiling": return Math.Ceiling(Convert.ToDouble(args[0]));
                case "round": return Math.Round(Convert.ToDouble(args[0]));
                case "log": return args.Length > 1 ? Math.Log(Convert.ToDouble(args[0]), Convert.ToDouble(args[1])) : Math.Log(Convert.ToDouble(args[0]));
                case "log10": return Math.Log10(Convert.ToDouble(args[0]));
                case "exp": return Math.Exp(Convert.ToDouble(args[0]));
                default: return NetCallNotFound;
            }
        }

        private object ExecuteDateTimeMethod(string method, object[] args)
        {
            switch (method.ToLowerInvariant())
            {
                case "now": return DateTime.Now;
                case "utcnow": return DateTime.UtcNow;
                case "today": return DateTime.Today;
                case "parse": return args.Length > 0 ? DateTime.Parse(args[0]?.ToString() ?? "") : DateTime.MinValue;
                default: return NetCallNotFound;
            }
        }

        private object ExecuteEnvironmentMethod(string method, object[] args)
        {
            switch (method.ToLowerInvariant())
            {
                case "machinename": return Environment.MachineName;
                case "username": return Environment.UserName;
                case "currentdirectory": return Environment.CurrentDirectory;
                case "osversion": return Environment.OSVersion.ToString();
                case "getenvironmentvariable": return args.Length > 0 ? Environment.GetEnvironmentVariable(args[0]?.ToString() ?? "") : null;
                case "newline": return Environment.NewLine;
                case "tickcount": return Environment.TickCount;
                default: return NetCallNotFound;
            }
        }

        private object ExecuteFileMethod(string method, object[] args)
        {
            try
            {
                switch (method.ToLowerInvariant())
                {
                    case "exists": return args.Length > 0 && System.IO.File.Exists(args[0]?.ToString() ?? "");
                    case "readalltext": return args.Length > 0 ? System.IO.File.ReadAllText(args[0]?.ToString() ?? "") : "";
                    case "writealltext":
                        if (args.Length >= 2)
                            System.IO.File.WriteAllText(args[0]?.ToString() ?? "", args[1]?.ToString() ?? "");
                        return null;
                    case "appendalltext":
                        if (args.Length >= 2)
                            System.IO.File.AppendAllText(args[0]?.ToString() ?? "", args[1]?.ToString() ?? "");
                        return null;
                    case "delete":
                        if (args.Length > 0)
                            System.IO.File.Delete(args[0]?.ToString() ?? "");
                        return null;
                    case "copy":
                        if (args.Length >= 2)
                            System.IO.File.Copy(args[0]?.ToString() ?? "", args[1]?.ToString() ?? "", args.Length > 2 && Convert.ToBoolean(args[2]));
                        return null;
                    case "move":
                        if (args.Length >= 2)
                            System.IO.File.Move(args[0]?.ToString() ?? "", args[1]?.ToString() ?? "");
                        return null;
                    default: return NetCallNotFound;
                }
            }
            catch (Exception ex)
            {
                OnOutput($"[File operation error: {ex.Message}]\n");
                return null;
            }
        }

        private object ExecuteDirectoryMethod(string method, object[] args)
        {
            try
            {
                switch (method.ToLowerInvariant())
                {
                    case "exists": return args.Length > 0 && System.IO.Directory.Exists(args[0]?.ToString() ?? "");
                    case "getcurrentdirectory": return System.IO.Directory.GetCurrentDirectory();
                    case "createdirectory":
                        if (args.Length > 0)
                            System.IO.Directory.CreateDirectory(args[0]?.ToString() ?? "");
                        return null;
                    case "delete":
                        if (args.Length > 0)
                            System.IO.Directory.Delete(args[0]?.ToString() ?? "", args.Length > 1 && Convert.ToBoolean(args[1]));
                        return null;
                    default: return NetCallNotFound;
                }
            }
            catch (Exception ex)
            {
                OnOutput($"[Directory operation error: {ex.Message}]\n");
                return null;
            }
        }

        private object ExecutePathMethod(string method, object[] args)
        {
            switch (method.ToLowerInvariant())
            {
                case "combine":
                    return args.Length >= 2 ? System.IO.Path.Combine(args[0]?.ToString() ?? "", args[1]?.ToString() ?? "") : "";
                case "getfilename":
                    return args.Length > 0 ? System.IO.Path.GetFileName(args[0]?.ToString() ?? "") : "";
                case "getdirectoryname":
                    return args.Length > 0 ? System.IO.Path.GetDirectoryName(args[0]?.ToString() ?? "") : "";
                case "getextension":
                    return args.Length > 0 ? System.IO.Path.GetExtension(args[0]?.ToString() ?? "") : "";
                case "getfilenamewithoutextension":
                    return args.Length > 0 ? System.IO.Path.GetFileNameWithoutExtension(args[0]?.ToString() ?? "") : "";
                default: return NetCallNotFound;
            }
        }

        private object ExecuteNetCallViaReflection(string typeName, string methodName, object[] args)
        {
            // Try to find the type in common assemblies
            Type type = null;

            // Try System namespace first
            type = Type.GetType($"System.{typeName}") ??
                   Type.GetType($"System.IO.{typeName}") ??
                   Type.GetType($"System.Text.{typeName}") ??
                   Type.GetType(typeName);

            if (type == null)
                return NetCallNotFound;

            try
            {
                // Try to find a matching static method
                var methods = type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                    .Where(m => m.Name.Equals(methodName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var method in methods)
                {
                    var parameters = method.GetParameters();
                    if (parameters.Length == args.Length)
                    {
                        try
                        {
                            // Convert arguments to expected types
                            var convertedArgs = new object[args.Length];
                            for (int i = 0; i < args.Length; i++)
                            {
                                convertedArgs[i] = Convert.ChangeType(args[i], parameters[i].ParameterType);
                            }
                            return method.Invoke(null, convertedArgs);
                        }
                        catch
                        {
                            continue; // Try next overload
                        }
                    }
                }

                // Try property getter
                var prop = type.GetProperty(methodName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (prop != null)
                {
                    return prop.GetValue(null);
                }
            }
            catch
            {
                // Reflection failed, return not found
            }

            return NetCallNotFound;
        }

        private object ExecuteBuiltIn(string name, object[] args)
        {
            switch (name.ToLowerInvariant())
            {
                case "printline":
                    var text = args.Length > 0 ? args[0]?.ToString() ?? "" : "";
                    OnOutput(text + "\n");
                    return null;

                case "print":
                    OnOutput(args.Length > 0 ? args[0]?.ToString() ?? "" : "");
                    return null;

                case "cstr":
                    return args.Length > 0 ? args[0]?.ToString() ?? "" : "";

                case "cint":
                    return args.Length > 0 ? Convert.ToInt32(args[0]) : 0;

                case "cdbl":
                    return args.Length > 0 ? Convert.ToDouble(args[0]) : 0.0;

                case "sqrt":
                    return args.Length > 0 ? Math.Sqrt(Convert.ToDouble(args[0])) : 0.0;

                case "abs":
                    return args.Length > 0 ? Math.Abs(Convert.ToDouble(args[0])) : 0.0;

                case "len":
                    return args.Length > 0 ? (args[0]?.ToString()?.Length ?? 0) : 0;

                case "rnd":
                    return _random.NextDouble();
            }

            return BuiltInNotFound;
        }

        private object PerformBinaryOp(BinaryOpKind op, object left, object right)
        {
            // Handle string concatenation
            if (op == BinaryOpKind.Concat)
            {
                return (left?.ToString() ?? "") + (right?.ToString() ?? "");
            }

            // Handle string comparison
            if (left is string || right is string)
            {
                var ls = left?.ToString() ?? "";
                var rs = right?.ToString() ?? "";
                return op switch
                {
                    BinaryOpKind.Eq => ls == rs,
                    BinaryOpKind.Ne => ls != rs,
                    BinaryOpKind.Add => ls + rs, // String concatenation fallback
                    _ => 0
                };
            }

            var l = Convert.ToDouble(left ?? 0);
            var r = Convert.ToDouble(right ?? 0);

            return op switch
            {
                BinaryOpKind.Add => l + r,
                BinaryOpKind.Sub => l - r,
                BinaryOpKind.Mul => l * r,
                BinaryOpKind.Div => r != 0 ? l / r : 0,
                BinaryOpKind.Mod => r != 0 ? l % r : 0,
                BinaryOpKind.IntDiv => r != 0 ? (int)l / (int)r : 0,
                BinaryOpKind.Eq => l == r,
                BinaryOpKind.Ne => l != r,
                BinaryOpKind.Lt => l < r,
                BinaryOpKind.Le => l <= r,
                BinaryOpKind.Gt => l > r,
                BinaryOpKind.Ge => l >= r,
                BinaryOpKind.And => (l != 0) && (r != 0),
                BinaryOpKind.Or => (l != 0) || (r != 0),
                BinaryOpKind.Xor => ((l != 0) && (r == 0)) || ((l == 0) && (r != 0)),
                _ => 0
            };
        }

        private object PerformUnaryOp(UnaryOpKind op, object operand)
        {
            var v = Convert.ToDouble(operand ?? 0);

            return op switch
            {
                UnaryOpKind.Neg => -v,
                UnaryOpKind.Not => v == 0,
                UnaryOpKind.Inc => v + 1,
                UnaryOpKind.Dec => v - 1,
                _ => v
            };
        }

        private object GetDefaultValue(TypeInfo type)
        {
            if (type == null) return null;

            return type.Name?.ToLowerInvariant() switch
            {
                "integer" => 0,
                "long" => 0L,
                "single" => 0.0f,
                "double" => 0.0,
                "string" => "",
                "boolean" => false,
                _ => null
            };
        }

        private void OnOutput(string text)
        {
            OutputProduced?.Invoke(this, new OutputEventArgs { Text = text });
        }

        private class CallFrame
        {
            public string FunctionName { get; set; }
            public int CurrentLine { get; set; }
            public Dictionary<string, object> LocalVariables { get; set; }
        }

        private class ReturnValue
        {
            public object Value { get; set; }
        }
    }

    /// <summary>
    /// Event args for logpoint hits
    /// </summary>
    public class LogpointEventArgs : EventArgs
    {
        public int Line { get; set; }
        public string File { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    /// Expression evaluator for debugging - parses and evaluates expressions at breakpoints
    /// Supports: variables, literals, arithmetic, comparisons, logical operators, property access
    /// </summary>
    internal class DebugExpressionEvaluator
    {
        private readonly DebuggableInterpreter _interpreter;
        private string _expression;
        private int _pos;

        public DebugExpressionEvaluator(DebuggableInterpreter interpreter)
        {
            _interpreter = interpreter;
        }

        public object Evaluate(string expression)
        {
            _expression = expression.Trim();
            _pos = 0;
            return ParseOrExpression();
        }

        private char Current => _pos < _expression.Length ? _expression[_pos] : '\0';
        private char Peek(int offset = 1) => _pos + offset < _expression.Length ? _expression[_pos + offset] : '\0';

        private void SkipWhitespace()
        {
            while (_pos < _expression.Length && char.IsWhiteSpace(_expression[_pos]))
                _pos++;
        }

        private bool Match(string text)
        {
            SkipWhitespace();
            if (_pos + text.Length <= _expression.Length &&
                _expression.Substring(_pos, text.Length).Equals(text, StringComparison.OrdinalIgnoreCase))
            {
                // Make sure it's not part of a longer identifier
                if (text.All(char.IsLetter) && _pos + text.Length < _expression.Length &&
                    char.IsLetterOrDigit(_expression[_pos + text.Length]))
                    return false;

                _pos += text.Length;
                return true;
            }
            return false;
        }

        private object ParseOrExpression()
        {
            var left = ParseAndExpression();

            while (Match("Or") || Match("OrElse") || Match("||"))
            {
                var right = ParseAndExpression();
                left = Convert.ToBoolean(left) || Convert.ToBoolean(right);
            }

            return left;
        }

        private object ParseAndExpression()
        {
            var left = ParseNotExpression();

            while (Match("And") || Match("AndAlso") || Match("&&"))
            {
                var right = ParseNotExpression();
                left = Convert.ToBoolean(left) && Convert.ToBoolean(right);
            }

            return left;
        }

        private object ParseNotExpression()
        {
            SkipWhitespace();
            if (Match("Not") || Match("!"))
            {
                var operand = ParseNotExpression();
                return !Convert.ToBoolean(operand);
            }
            return ParseComparisonExpression();
        }

        private object ParseComparisonExpression()
        {
            var left = ParseAdditiveExpression();

            SkipWhitespace();
            string op = null;

            if (Match("<=")) op = "<=";
            else if (Match(">=")) op = ">=";
            else if (Match("<>")) op = "<>";
            else if (Match("!=")) op = "<>";
            else if (Match("==")) op = "=";
            else if (Match("=")) op = "=";
            else if (Match("<")) op = "<";
            else if (Match(">")) op = ">";

            if (op != null)
            {
                var right = ParseAdditiveExpression();
                return Compare(left, right, op);
            }

            return left;
        }

        private object ParseAdditiveExpression()
        {
            var left = ParseMultiplicativeExpression();

            while (true)
            {
                SkipWhitespace();
                if (Match("+"))
                {
                    var right = ParseMultiplicativeExpression();
                    left = Add(left, right);
                }
                else if (Match("-"))
                {
                    var right = ParseMultiplicativeExpression();
                    left = Subtract(left, right);
                }
                else if (Match("&"))
                {
                    var right = ParseMultiplicativeExpression();
                    left = Convert.ToString(left) + Convert.ToString(right);
                }
                else break;
            }

            return left;
        }

        private object ParseMultiplicativeExpression()
        {
            var left = ParseUnaryExpression();

            while (true)
            {
                SkipWhitespace();
                if (Match("*"))
                {
                    var right = ParseUnaryExpression();
                    left = Multiply(left, right);
                }
                else if (Match("/"))
                {
                    var right = ParseUnaryExpression();
                    left = Divide(left, right);
                }
                else if (Match("\\") || Match("Mod"))
                {
                    var right = ParseUnaryExpression();
                    left = Convert.ToInt64(left) % Convert.ToInt64(right);
                }
                else break;
            }

            return left;
        }

        private object ParseUnaryExpression()
        {
            SkipWhitespace();
            if (Match("-"))
            {
                var operand = ParseUnaryExpression();
                return Negate(operand);
            }
            return ParsePrimaryExpression();
        }

        private object ParsePrimaryExpression()
        {
            SkipWhitespace();

            // Parentheses
            if (Match("("))
            {
                var result = ParseOrExpression();
                Match(")");
                return result;
            }

            // String literal
            if (Current == '"')
            {
                _pos++;
                var start = _pos;
                while (_pos < _expression.Length && _expression[_pos] != '"')
                    _pos++;
                var str = _expression.Substring(start, _pos - start);
                if (Current == '"') _pos++;
                return str;
            }

            // Number literal
            if (char.IsDigit(Current) || (Current == '.' && char.IsDigit(Peek())))
            {
                var start = _pos;
                while (char.IsDigit(Current)) _pos++;
                if (Current == '.')
                {
                    _pos++;
                    while (char.IsDigit(Current)) _pos++;
                    return double.Parse(_expression.Substring(start, _pos - start));
                }
                return long.Parse(_expression.Substring(start, _pos - start));
            }

            // Boolean literals
            if (Match("True")) return true;
            if (Match("False")) return false;
            if (Match("Nothing") || Match("null")) return null;

            // Identifier (variable or property chain)
            if (char.IsLetter(Current) || Current == '_')
            {
                var start = _pos;
                while (char.IsLetterOrDigit(Current) || Current == '_') _pos++;
                var name = _expression.Substring(start, _pos - start);

                var value = _interpreter.GetVariable(name);

                // Handle property/method access chain
                while (true)
                {
                    SkipWhitespace();
                    if (Match("."))
                    {
                        value = AccessMember(value);
                    }
                    else if (Current == '(')
                    {
                        // Array/indexer access
                        _pos++;
                        var index = ParseOrExpression();
                        Match(")");
                        value = AccessIndex(value, index);
                    }
                    else break;
                }

                return value;
            }

            return null;
        }

        private object AccessMember(object obj)
        {
            if (obj == null) return null;

            SkipWhitespace();
            var start = _pos;
            while (char.IsLetterOrDigit(Current) || Current == '_') _pos++;
            var memberName = _expression.Substring(start, _pos - start);

            var type = obj.GetType();

            // Try property
            var prop = type.GetProperty(memberName, System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
            if (prop != null)
                return prop.GetValue(obj);

            // Try field
            var field = type.GetField(memberName, System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
            if (field != null)
                return field.GetValue(obj);

            return null;
        }

        private object AccessIndex(object obj, object index)
        {
            if (obj == null) return null;

            if (obj is Array arr)
                return arr.GetValue(Convert.ToInt32(index));

            if (obj is System.Collections.IList list)
                return list[Convert.ToInt32(index)];

            if (obj is System.Collections.IDictionary dict)
                return dict[index];

            // Try indexer property
            var type = obj.GetType();
            var indexer = type.GetProperty("Item");
            if (indexer != null)
                return indexer.GetValue(obj, new[] { index });

            return null;
        }

        private bool Compare(object left, object right, string op)
        {
            if (left == null && right == null) return op == "=" || op == "<=>" ? false : op == "<>";
            if (left == null || right == null) return op == "<>";

            if (left is string || right is string)
            {
                int cmp = string.Compare(Convert.ToString(left), Convert.ToString(right), StringComparison.OrdinalIgnoreCase);
                return op switch
                {
                    "=" => cmp == 0,
                    "<>" => cmp != 0,
                    "<" => cmp < 0,
                    ">" => cmp > 0,
                    "<=" => cmp <= 0,
                    ">=" => cmp >= 0,
                    _ => false
                };
            }

            double l = Convert.ToDouble(left);
            double r = Convert.ToDouble(right);
            return op switch
            {
                "=" => l == r,
                "<>" => l != r,
                "<" => l < r,
                ">" => l > r,
                "<=" => l <= r,
                ">=" => l >= r,
                _ => false
            };
        }

        private object Add(object left, object right)
        {
            if (left is string || right is string)
                return Convert.ToString(left) + Convert.ToString(right);
            return Convert.ToDouble(left) + Convert.ToDouble(right);
        }

        private object Subtract(object left, object right)
            => Convert.ToDouble(left) - Convert.ToDouble(right);

        private object Multiply(object left, object right)
            => Convert.ToDouble(left) * Convert.ToDouble(right);

        private object Divide(object left, object right)
            => Convert.ToDouble(left) / Convert.ToDouble(right);

        private object Negate(object value)
            => -Convert.ToDouble(value);
    }
}
