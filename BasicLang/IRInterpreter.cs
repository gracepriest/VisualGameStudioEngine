using System;
using System.Collections.Generic;
using System.Linq;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;

namespace BasicLang.Compiler.Interpreter
{
    /// <summary>
    /// Interprets IR directly for REPL execution
    /// </summary>
    public class IRInterpreter
    {
        private readonly Dictionary<string, object> _globals;
        private readonly Dictionary<string, IRFunction> _functions;
        private readonly Stack<StackFrame> _callStack;
        private Random _random;
        private object _lastResult;

        public IRInterpreter()
        {
            _globals = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            _functions = new Dictionary<string, IRFunction>(StringComparer.OrdinalIgnoreCase);
            _callStack = new Stack<StackFrame>();
            _random = new Random();
        }

        /// <summary>
        /// Get all currently defined variables
        /// </summary>
        public IReadOnlyDictionary<string, object> Variables => _globals;

        /// <summary>
        /// Get the last evaluated result
        /// </summary>
        public object LastResult => _lastResult;

        /// <summary>
        /// Execute an IR module
        /// </summary>
        public object Execute(IRModule module)
        {
            // Register all functions
            foreach (var func in module.Functions)
            {
                _functions[func.Name] = func;
            }

            // Register global variables
            foreach (var global in module.GlobalVariables)
            {
                if (!_globals.ContainsKey(global.Key))
                {
                    _globals[global.Key] = GetDefaultValue(global.Value.Type);
                }
            }

            // Look for Main or execute top-level code
            if (_functions.TryGetValue("Main", out var mainFunc))
            {
                _lastResult = ExecuteFunction(mainFunc, new object[0]);
            }
            else if (module.Functions.Count > 0)
            {
                // Execute the first/only function (for REPL expressions)
                var func = module.Functions.First();
                _lastResult = ExecuteFunction(func, new object[0]);
            }

            return _lastResult;
        }

        /// <summary>
        /// Execute a single function
        /// </summary>
        private object ExecuteFunction(IRFunction function, object[] args)
        {
            var frame = new StackFrame(function.Name);
            _callStack.Push(frame);

            try
            {
                // Bind parameters
                for (int i = 0; i < function.Parameters.Count && i < args.Length; i++)
                {
                    frame.Locals[function.Parameters[i].Name] = args[i];
                }

                // Initialize local variables
                foreach (var local in function.LocalVariables)
                {
                    if (!frame.Locals.ContainsKey(local.Name))
                    {
                        frame.Locals[local.Name] = GetDefaultValue(local.Type);
                    }
                }

                // Execute entry block
                if (function.EntryBlock != null)
                {
                    return ExecuteBlock(function.EntryBlock, frame);
                }

                return null;
            }
            finally
            {
                _callStack.Pop();
            }
        }

        /// <summary>
        /// Execute a basic block
        /// </summary>
        private object ExecuteBlock(BasicBlock block, StackFrame frame)
        {
            foreach (var instruction in block.Instructions)
            {
                var result = ExecuteInstruction(instruction, frame);

                // Check for return
                if (instruction is IRReturn)
                {
                    return result;
                }
            }

            // Handle terminator (last instruction if it's a branch/return)
            var terminator = block.GetTerminator();
            if (terminator != null && !(terminator is IRStore) && !(terminator is IRCall))
            {
                return ExecuteTerminatorInstruction(terminator, frame);
            }

            return null;
        }

        /// <summary>
        /// Execute a single instruction
        /// </summary>
        private object ExecuteInstruction(IRInstruction instruction, StackFrame frame)
        {
            switch (instruction)
            {
                case IRStore store:
                    var value = EvaluateValue(store.Value, frame);
                    var targetName = store.Address?.Name ?? store.Address?.ToString() ?? "temp";
                    SetVariable(targetName, value, frame);
                    return value;

                case IRCall call:
                    return ExecuteCall(call, frame);

                case IRReturn ret:
                    return ret.Value != null ? EvaluateValue(ret.Value, frame) : null;

                case IRBranch branch:
                    return ExecuteBlock(branch.Target, frame);

                case IRConditionalBranch condBranch:
                    var cond = EvaluateValue(condBranch.Condition, frame);
                    var tgt = IsTruthy(cond) ? condBranch.TrueTarget : condBranch.FalseTarget;
                    return ExecuteBlock(tgt, frame);

                case IRInlineCode inlineCode:
                    // Inline code cannot be executed by the interpreter
                    // This would require a JIT compiler or embedding the target runtime
                    throw new InvalidOperationException($"Cannot interpret inline {inlineCode.Language} code in the interpreter. Use code generation instead.");

                default:
                    return null;
            }
        }

        /// <summary>
        /// Execute a block terminator instruction
        /// </summary>
        private object ExecuteTerminatorInstruction(IRInstruction terminator, StackFrame frame)
        {
            switch (terminator)
            {
                case IRBranch branch:
                    return ExecuteBlock(branch.Target, frame);

                case IRConditionalBranch condBranch:
                    var condition = EvaluateValue(condBranch.Condition, frame);
                    var target = IsTruthy(condition) ? condBranch.TrueTarget : condBranch.FalseTarget;
                    return ExecuteBlock(target, frame);

                case IRReturn ret:
                    return ret.Value != null ? EvaluateValue(ret.Value, frame) : null;

                default:
                    return null;
            }
        }

        /// <summary>
        /// Execute a function call
        /// </summary>
        private object ExecuteCall(IRCall call, StackFrame frame)
        {
            var funcName = call.FunctionName;
            var args = call.Arguments.Select(a => EvaluateValue(a, frame)).ToArray();

            // Check for built-in functions
            var result = ExecuteBuiltIn(funcName, args);
            if (result.handled)
            {
                return result.value;
            }

            // Check for user-defined functions
            if (_functions.TryGetValue(funcName, out var func))
            {
                return ExecuteFunction(func, args);
            }

            throw new InterpreterException($"Unknown function: {funcName}");
        }

        /// <summary>
        /// Execute built-in standard library functions
        /// </summary>
        private (bool handled, object value) ExecuteBuiltIn(string funcName, object[] args)
        {
            switch (funcName.ToLower())
            {
                // Console I/O
                case "printline":
                case "console.writeline":
                    Console.WriteLine(args.Length > 0 ? args[0]?.ToString() ?? "" : "");
                    return (true, null);

                case "print":
                case "console.write":
                    Console.Write(args.Length > 0 ? args[0]?.ToString() ?? "" : "");
                    return (true, null);

                case "readline":
                case "console.readline":
                    return (true, Console.ReadLine());

                case "readkey":
                    return (true, Console.ReadKey().KeyChar.ToString());

                // String functions
                case "len":
                case "string.length":
                    return (true, args[0]?.ToString()?.Length ?? 0);

                case "left":
                    var leftStr = args[0]?.ToString() ?? "";
                    var leftLen = Convert.ToInt32(args[1]);
                    return (true, leftStr.Substring(0, Math.Min(leftLen, leftStr.Length)));

                case "right":
                    var rightStr = args[0]?.ToString() ?? "";
                    var rightLen = Convert.ToInt32(args[1]);
                    return (true, rightStr.Substring(Math.Max(0, rightStr.Length - rightLen)));

                case "mid":
                    var midStr = args[0]?.ToString() ?? "";
                    var midStart = Convert.ToInt32(args[1]) - 1;
                    var midLen = args.Length > 2 ? Convert.ToInt32(args[2]) : midStr.Length - midStart;
                    return (true, midStr.Substring(Math.Max(0, midStart), Math.Min(midLen, midStr.Length - midStart)));

                case "ucase":
                case "string.toupper":
                    return (true, args[0]?.ToString()?.ToUpper() ?? "");

                case "lcase":
                case "string.tolower":
                    return (true, args[0]?.ToString()?.ToLower() ?? "");

                case "trim":
                case "string.trim":
                    return (true, args[0]?.ToString()?.Trim() ?? "");

                case "instr":
                case "string.indexof":
                    var instrStr = args[0]?.ToString() ?? "";
                    var instrFind = args[1]?.ToString() ?? "";
                    return (true, instrStr.IndexOf(instrFind) + 1);

                case "replace":
                case "string.replace":
                    var replStr = args[0]?.ToString() ?? "";
                    var replOld = args[1]?.ToString() ?? "";
                    var replNew = args[2]?.ToString() ?? "";
                    return (true, replStr.Replace(replOld, replNew));

                // Math functions
                case "abs":
                case "math.abs":
                    return (true, Math.Abs(Convert.ToDouble(args[0])));

                case "sqrt":
                case "math.sqrt":
                    return (true, Math.Sqrt(Convert.ToDouble(args[0])));

                case "pow":
                case "math.pow":
                    return (true, Math.Pow(Convert.ToDouble(args[0]), Convert.ToDouble(args[1])));

                case "sin":
                case "math.sin":
                    return (true, Math.Sin(Convert.ToDouble(args[0])));

                case "cos":
                case "math.cos":
                    return (true, Math.Cos(Convert.ToDouble(args[0])));

                case "tan":
                case "math.tan":
                    return (true, Math.Tan(Convert.ToDouble(args[0])));

                case "floor":
                case "math.floor":
                    return (true, Math.Floor(Convert.ToDouble(args[0])));

                case "ceiling":
                case "math.ceiling":
                    return (true, Math.Ceiling(Convert.ToDouble(args[0])));

                case "round":
                case "math.round":
                    return (true, Math.Round(Convert.ToDouble(args[0])));

                case "min":
                case "math.min":
                    return (true, Math.Min(Convert.ToDouble(args[0]), Convert.ToDouble(args[1])));

                case "max":
                case "math.max":
                    return (true, Math.Max(Convert.ToDouble(args[0]), Convert.ToDouble(args[1])));

                case "rnd":
                case "random":
                    return (true, _random.NextDouble());

                case "randomize":
                    _random = new Random(Convert.ToInt32(args[0]));
                    return (true, null);

                // Type conversion
                case "cint":
                case "convert.toint32":
                    return (true, Convert.ToInt32(args[0]));

                case "cdbl":
                case "convert.todouble":
                    return (true, Convert.ToDouble(args[0]));

                case "cstr":
                case "convert.tostring":
                    return (true, args[0]?.ToString() ?? "");

                case "cbool":
                case "convert.toboolean":
                    return (true, Convert.ToBoolean(args[0]));

                // Array functions
                case "ubound":
                    if (args[0] is Array arr)
                        return (true, arr.Length - 1);
                    return (true, -1);

                case "lbound":
                    return (true, 0);

                default:
                    return (false, null);
            }
        }

        /// <summary>
        /// Evaluate an IR value to a runtime value
        /// </summary>
        private object EvaluateValue(IRValue value, StackFrame frame)
        {
            switch (value)
            {
                case IRConstant constant:
                    return constant.Value;

                case IRVariable variable:
                    return GetVariable(variable.Name, frame);

                case IRBinaryOp binOp:
                    return EvaluateBinaryOp(binOp, frame);

                case IRUnaryOp unaryOp:
                    return EvaluateUnaryOp(unaryOp, frame);

                case IRCall call:
                    return ExecuteCall(call, frame);

                case IRLoad load:
                    var loadName = load.Address?.Name ?? load.Name ?? "temp";
                    return GetVariable(loadName, frame);

                case IRAlloca alloca:
                    // Return a reference to the allocated variable
                    return GetVariable(alloca.Name, frame);

                default:
                    return null;
            }
        }

        /// <summary>
        /// Evaluate a binary operation
        /// </summary>
        private object EvaluateBinaryOp(IRBinaryOp binOp, StackFrame frame)
        {
            var left = EvaluateValue(binOp.Left, frame);
            var right = EvaluateValue(binOp.Right, frame);

            // String concatenation
            if (binOp.Operation == BinaryOpKind.Concat || (binOp.Operation == BinaryOpKind.Add && (left is string || right is string)))
            {
                return (left?.ToString() ?? "") + (right?.ToString() ?? "");
            }

            // Numeric operations
            var leftNum = Convert.ToDouble(left ?? 0);
            var rightNum = Convert.ToDouble(right ?? 0);

            switch (binOp.Operation)
            {
                case BinaryOpKind.Add: return leftNum + rightNum;
                case BinaryOpKind.Sub: return leftNum - rightNum;
                case BinaryOpKind.Mul: return leftNum * rightNum;
                case BinaryOpKind.Div: return rightNum != 0 ? leftNum / rightNum : 0;
                case BinaryOpKind.IntDiv: return rightNum != 0 ? (int)(leftNum / rightNum) : 0;
                case BinaryOpKind.Mod: return rightNum != 0 ? leftNum % rightNum : 0;

                // Comparison
                case BinaryOpKind.Eq: return Equals(left, right);
                case BinaryOpKind.Ne: return !Equals(left, right);
                case BinaryOpKind.Lt: return leftNum < rightNum;
                case BinaryOpKind.Gt: return leftNum > rightNum;
                case BinaryOpKind.Le: return leftNum <= rightNum;
                case BinaryOpKind.Ge: return leftNum >= rightNum;

                // Bitwise / Logical
                case BinaryOpKind.And: return IsTruthy(left) && IsTruthy(right);
                case BinaryOpKind.Or: return IsTruthy(left) || IsTruthy(right);
                case BinaryOpKind.Xor: return IsTruthy(left) ^ IsTruthy(right);
                case BinaryOpKind.Shl: return (int)leftNum << (int)rightNum;
                case BinaryOpKind.Shr: return (int)leftNum >> (int)rightNum;

                case BinaryOpKind.Concat: return (left?.ToString() ?? "") + (right?.ToString() ?? "");

                default:
                    throw new InterpreterException($"Unknown binary operator: {binOp.Operation}");
            }
        }

        /// <summary>
        /// Evaluate a unary operation
        /// </summary>
        private object EvaluateUnaryOp(IRUnaryOp unaryOp, StackFrame frame)
        {
            var operand = EvaluateValue(unaryOp.Operand, frame);

            switch (unaryOp.Operation)
            {
                case UnaryOpKind.Neg:
                    return -Convert.ToDouble(operand ?? 0);

                case UnaryOpKind.Not:
                    return !IsTruthy(operand);

                case UnaryOpKind.BitwiseNot:
                    return ~Convert.ToInt32(operand ?? 0);

                case UnaryOpKind.Inc:
                    return Convert.ToDouble(operand ?? 0) + 1;

                case UnaryOpKind.Dec:
                    return Convert.ToDouble(operand ?? 0) - 1;

                default:
                    throw new InterpreterException($"Unknown unary operator: {unaryOp.Operation}");
            }
        }

        /// <summary>
        /// Get a variable value
        /// </summary>
        private object GetVariable(string name, StackFrame frame)
        {
            // Check locals first
            if (frame.Locals.TryGetValue(name, out var localValue))
            {
                return localValue;
            }

            // Check globals
            if (_globals.TryGetValue(name, out var globalValue))
            {
                return globalValue;
            }

            // Return default
            return 0;
        }

        /// <summary>
        /// Set a variable value
        /// </summary>
        private void SetVariable(string name, object value, StackFrame frame)
        {
            // If it exists in locals, update there
            if (frame.Locals.ContainsKey(name))
            {
                frame.Locals[name] = value;
                return;
            }

            // If it exists in globals, update there
            if (_globals.ContainsKey(name))
            {
                _globals[name] = value;
                return;
            }

            // Store as global (REPL mode)
            _globals[name] = value;
        }

        /// <summary>
        /// Check if a value is truthy
        /// </summary>
        private bool IsTruthy(object value)
        {
            if (value == null) return false;
            if (value is bool b) return b;
            if (value is int i) return i != 0;
            if (value is long l) return l != 0;
            if (value is double d) return d != 0;
            if (value is string s) return !string.IsNullOrEmpty(s);
            return true;
        }

        /// <summary>
        /// Get default value for a type
        /// </summary>
        private object GetDefaultValue(TypeInfo type)
        {
            if (type == null) return 0;

            // Check by type name for primitive types
            var typeName = type.Name?.ToLower() ?? "";
            switch (typeName)
            {
                case "integer":
                case "int":
                case "long":
                    return 0;
                case "single":
                case "double":
                case "float":
                    return 0.0;
                case "boolean":
                case "bool":
                    return false;
                case "string":
                    return "";
                default:
                    break;
            }

            // Check by type kind
            switch (type.Kind)
            {
                case TypeKind.Primitive:
                    return 0;
                case TypeKind.Array:
                    return Array.CreateInstance(typeof(object), 10);
                case TypeKind.Class:
                case TypeKind.Structure:
                    return null;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Clear all state
        /// </summary>
        public void Clear()
        {
            _globals.Clear();
            _functions.Clear();
            _lastResult = null;
        }
    }

    /// <summary>
    /// Represents a call stack frame
    /// </summary>
    internal class StackFrame
    {
        public string FunctionName { get; }
        public Dictionary<string, object> Locals { get; }

        public StackFrame(string functionName)
        {
            FunctionName = functionName;
            Locals = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Exception thrown during interpretation
    /// </summary>
    public class InterpreterException : Exception
    {
        public InterpreterException(string message) : base(message) { }
    }
}
