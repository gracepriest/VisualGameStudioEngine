using System;
using System.Collections.Generic;
using BasicLang.Compiler.IR;

namespace BasicLang.Debugger
{
    /// <summary>
    /// Represents different types of breakpoints
    /// </summary>
    public enum BreakpointType
    {
        Line,
        Conditional,
        HitCount,
        Logpoint,
        Function,
        Data,        // Break when a variable changes
        Exception    // Break when an exception is thrown/caught
    }

    /// <summary>
    /// Exception breakpoint mode
    /// </summary>
    public enum ExceptionBreakMode
    {
        Never,      // Don't break on exceptions
        Always,     // Break on all exceptions
        Unhandled,  // Break only on unhandled exceptions
        UserUnhandled // Break on user-unhandled exceptions
    }

    /// <summary>
    /// Data breakpoint access type
    /// </summary>
    public enum DataBreakpointAccessType
    {
        Write,      // Break when variable is written
        Read,       // Break when variable is read
        ReadWrite   // Break on any access
    }

    /// <summary>
    /// Represents a breakpoint with advanced features
    /// </summary>
    public class Breakpoint
    {
        public int Id { get; set; }
        public BreakpointType Type { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public int Line { get; set; }
        public int Column { get; set; }
        public bool Verified { get; set; }
        public string Message { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;

        // Conditional breakpoint support
        public string Condition { get; set; } = string.Empty;

        // Hit count breakpoint support
        public int HitCount { get; set; }
        public int HitCountTarget { get; set; }
        public HitCountCondition HitCountCondition { get; set; }

        // Logpoint support
        public string LogMessage { get; set; } = string.Empty;

        // Function breakpoint support
        public string FunctionName { get; set; } = string.Empty;

        // Data breakpoint support
        public string VariableName { get; set; } = string.Empty;
        public DataBreakpointAccessType DataAccessType { get; set; } = DataBreakpointAccessType.Write;
        public object PreviousValue { get; set; }

        // Exception breakpoint support
        public string ExceptionType { get; set; } = string.Empty; // e.g., "DivisionByZeroException" or "*" for all
        public ExceptionBreakMode ExceptionMode { get; set; } = ExceptionBreakMode.Always;

        public Breakpoint()
        {
            Verified = true;
            Enabled = true;
        }

        /// <summary>
        /// Check if this breakpoint should trigger based on its conditions
        /// </summary>
        public bool ShouldBreak(DebuggableInterpreter interpreter)
        {
            if (!Enabled)
                return false;

            // Increment hit count
            HitCount++;

            // Check hit count condition
            if (Type == BreakpointType.HitCount || HitCountTarget > 0)
            {
                if (!CheckHitCountCondition())
                    return false;
            }

            // Check conditional expression
            if (Type == BreakpointType.Conditional && !string.IsNullOrEmpty(Condition))
            {
                try
                {
                    var result = interpreter.EvaluateExpression(Condition);
                    if (result == null || !Convert.ToBoolean(result))
                        return false;
                }
                catch
                {
                    // If condition evaluation fails, don't break
                    return false;
                }
            }

            // Logpoints don't break, they just log
            if (Type == BreakpointType.Logpoint)
            {
                return false; // Handled separately in LogMessage
            }

            return true;
        }

        /// <summary>
        /// Check if the hit count condition is satisfied
        /// </summary>
        private bool CheckHitCountCondition()
        {
            if (HitCountTarget <= 0)
                return true;

            return HitCountCondition switch
            {
                HitCountCondition.Equals => HitCount == HitCountTarget,
                HitCountCondition.GreaterThan => HitCount > HitCountTarget,
                HitCountCondition.GreaterThanOrEquals => HitCount >= HitCountTarget,
                HitCountCondition.LessThan => HitCount < HitCountTarget,
                HitCountCondition.LessThanOrEquals => HitCount <= HitCountTarget,
                HitCountCondition.Modulo => HitCount % HitCountTarget == 0,
                _ => true
            };
        }

        /// <summary>
        /// Format the log message with variable substitution
        /// </summary>
        public string FormatLogMessage(DebuggableInterpreter interpreter)
        {
            if (string.IsNullOrEmpty(LogMessage))
                return string.Empty;

            var message = LogMessage;

            // Replace {expression} with evaluated values
            var startIndex = 0;
            while (true)
            {
                var openBrace = message.IndexOf('{', startIndex);
                if (openBrace == -1) break;

                var closeBrace = message.IndexOf('}', openBrace);
                if (closeBrace == -1) break;

                var expression = message.Substring(openBrace + 1, closeBrace - openBrace - 1);
                try
                {
                    var value = interpreter.EvaluateExpression(expression);
                    var valueStr = value?.ToString() ?? "Nothing";
                    message = message.Substring(0, openBrace) + valueStr + message.Substring(closeBrace + 1);
                    startIndex = openBrace + valueStr.Length;
                }
                catch
                {
                    // If evaluation fails, keep the original expression
                    startIndex = closeBrace + 1;
                }
            }

            return message;
        }

        public override string ToString()
        {
            var parts = new List<string>();

            if (!string.IsNullOrEmpty(FilePath))
                parts.Add($"{FilePath}:{Line}");
            else if (!string.IsNullOrEmpty(FunctionName))
                parts.Add($"Function: {FunctionName}");

            if (Column > 0)
                parts.Add($"Col {Column}");

            if (Type == BreakpointType.Conditional && !string.IsNullOrEmpty(Condition))
                parts.Add($"Condition: {Condition}");

            if (Type == BreakpointType.HitCount && HitCountTarget > 0)
                parts.Add($"Hit: {HitCount}/{HitCountTarget}");

            if (Type == BreakpointType.Logpoint && !string.IsNullOrEmpty(LogMessage))
                parts.Add($"Log: {LogMessage}");

            return string.Join(", ", parts);
        }
    }

    /// <summary>
    /// Hit count comparison operators
    /// </summary>
    public enum HitCountCondition
    {
        Equals,
        GreaterThan,
        GreaterThanOrEquals,
        LessThan,
        LessThanOrEquals,
        Modulo
    }

    /// <summary>
    /// Manages a collection of breakpoints
    /// </summary>
    public class BreakpointManager
    {
        private readonly Dictionary<int, Breakpoint> _breakpoints = new();
        private readonly Dictionary<string, List<Breakpoint>> _lineBreakpoints = new();
        private readonly Dictionary<string, Breakpoint> _functionBreakpoints = new();
        private readonly Dictionary<string, Breakpoint> _dataBreakpoints = new();  // variable name -> breakpoint
        private readonly List<Breakpoint> _exceptionBreakpoints = new();
        private int _nextId = 1;

        /// <summary>
        /// Add a new breakpoint
        /// </summary>
        public Breakpoint AddBreakpoint(Breakpoint breakpoint)
        {
            breakpoint.Id = _nextId++;
            _breakpoints[breakpoint.Id] = breakpoint;

            switch (breakpoint.Type)
            {
                case BreakpointType.Function:
                    var funcName = breakpoint.FunctionName.ToLowerInvariant();
                    _functionBreakpoints[funcName] = breakpoint;
                    break;

                case BreakpointType.Data:
                    var varName = breakpoint.VariableName.ToLowerInvariant();
                    _dataBreakpoints[varName] = breakpoint;
                    break;

                case BreakpointType.Exception:
                    _exceptionBreakpoints.Add(breakpoint);
                    break;

                default:
                    var key = GetLocationKey(breakpoint.FilePath, breakpoint.Line);
                    if (!_lineBreakpoints.ContainsKey(key))
                        _lineBreakpoints[key] = new List<Breakpoint>();
                    _lineBreakpoints[key].Add(breakpoint);
                    break;
            }

            return breakpoint;
        }

        /// <summary>
        /// Remove a breakpoint by ID
        /// </summary>
        public bool RemoveBreakpoint(int id)
        {
            if (!_breakpoints.TryGetValue(id, out var breakpoint))
                return false;

            _breakpoints.Remove(id);

            switch (breakpoint.Type)
            {
                case BreakpointType.Function:
                    var funcName = breakpoint.FunctionName.ToLowerInvariant();
                    _functionBreakpoints.Remove(funcName);
                    break;

                case BreakpointType.Data:
                    var varName = breakpoint.VariableName.ToLowerInvariant();
                    _dataBreakpoints.Remove(varName);
                    break;

                case BreakpointType.Exception:
                    _exceptionBreakpoints.Remove(breakpoint);
                    break;

                default:
                    var key = GetLocationKey(breakpoint.FilePath, breakpoint.Line);
                    if (_lineBreakpoints.TryGetValue(key, out var list))
                    {
                        list.Remove(breakpoint);
                        if (list.Count == 0)
                            _lineBreakpoints.Remove(key);
                    }
                    break;
            }

            return true;
        }

        /// <summary>
        /// Get all breakpoints at a specific location
        /// </summary>
        public List<Breakpoint> GetBreakpointsAtLocation(string filePath, int line)
        {
            var key = GetLocationKey(filePath, line);
            return _lineBreakpoints.TryGetValue(key, out var list)
                ? new List<Breakpoint>(list)
                : new List<Breakpoint>();
        }

        /// <summary>
        /// Get function breakpoint by name
        /// </summary>
        public Breakpoint GetFunctionBreakpoint(string functionName)
        {
            var funcName = functionName.ToLowerInvariant();
            return _functionBreakpoints.TryGetValue(funcName, out var bp) ? bp : null;
        }

        /// <summary>
        /// Get data breakpoint by variable name
        /// </summary>
        public Breakpoint GetDataBreakpoint(string variableName)
        {
            var varName = variableName.ToLowerInvariant();
            return _dataBreakpoints.TryGetValue(varName, out var bp) ? bp : null;
        }

        /// <summary>
        /// Get all data breakpoints
        /// </summary>
        public List<Breakpoint> GetAllDataBreakpoints()
        {
            return new List<Breakpoint>(_dataBreakpoints.Values);
        }

        /// <summary>
        /// Get exception breakpoint by exception type
        /// </summary>
        public Breakpoint GetExceptionBreakpoint(string exceptionType)
        {
            return _exceptionBreakpoints.FirstOrDefault(bp =>
                bp.ExceptionType == "*" ||
                string.Equals(bp.ExceptionType, exceptionType, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Get all exception breakpoints
        /// </summary>
        public List<Breakpoint> GetAllExceptionBreakpoints()
        {
            return new List<Breakpoint>(_exceptionBreakpoints);
        }

        /// <summary>
        /// Get all breakpoints
        /// </summary>
        public List<Breakpoint> GetAllBreakpoints()
        {
            return new List<Breakpoint>(_breakpoints.Values);
        }

        /// <summary>
        /// Clear all breakpoints
        /// </summary>
        public void ClearAll()
        {
            _breakpoints.Clear();
            _lineBreakpoints.Clear();
            _functionBreakpoints.Clear();
            _dataBreakpoints.Clear();
            _exceptionBreakpoints.Clear();
        }

        /// <summary>
        /// Clear breakpoints for a specific file
        /// </summary>
        public void ClearFile(string filePath)
        {
            var toRemove = new List<int>();
            foreach (var bp in _breakpoints.Values)
            {
                if (string.Equals(bp.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                    toRemove.Add(bp.Id);
            }

            foreach (var id in toRemove)
                RemoveBreakpoint(id);
        }

        private string GetLocationKey(string filePath, int line)
        {
            return $"{filePath?.ToLowerInvariant() ?? ""}:{line}";
        }
    }
}
