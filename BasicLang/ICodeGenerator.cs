using System;
using System.Collections.Generic;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;

namespace BasicLang.Compiler.CodeGen
{
    /// <summary>
    /// Target platforms supported by the compiler
    /// </summary>
    public enum TargetPlatform
    {
        CSharp,
        Cpp,
        LLVM,
        MSIL
    }

    /// <summary>
    /// Abstract interface for all code generators
    /// Enables pluggable backends for different target languages
    /// </summary>
    public interface ICodeGenerator : IIRVisitor
    {
        /// <summary>
        /// Generate code from IR module
        /// </summary>
        string Generate(IRModule module);

        /// <summary>
        /// Get the backend name
        /// </summary>
        string BackendName { get; }

        /// <summary>
        /// Get the target platform
        /// </summary>
        TargetPlatform Target { get; }

        /// <summary>
        /// Get the type mapper for this backend
        /// </summary>
        ITypeMapper TypeMapper { get; }
    }

    /// <summary>
    /// Interface for mapping BasicLang types to target language types
    /// </summary>
    public interface ITypeMapper
    {
        /// <summary>
        /// Map a BasicLang type to target language type string
        /// </summary>
        string MapType(TypeInfo type);

        /// <summary>
        /// Map a binary operator to target language operator
        /// </summary>
        string MapBinaryOperator(BinaryOpKind op);

        /// <summary>
        /// Map a comparison operator to target language operator
        /// </summary>
        string MapComparisonOperator(CompareKind op);

        /// <summary>
        /// Map a unary operator to target language operator
        /// </summary>
        string MapUnaryOperator(UnaryOpKind op);

        /// <summary>
        /// Get default value expression for a type
        /// </summary>
        string GetDefaultValue(TypeInfo type);
    }
    
    /// <summary>
    /// Base class for code generators with common functionality
    /// </summary>
    public abstract class CodeGeneratorBase : ICodeGenerator
    {
        protected int _indentLevel;
        protected int _tempCounter;
        protected IRFunction _currentFunction;
        protected readonly Dictionary<IRValue, string> _valueNames;
        protected readonly HashSet<string> _usings;
        protected readonly Dictionary<string, string> _typeMap;
        protected ITypeMapper _typeMapper;

        public abstract string BackendName { get; }
        public abstract TargetPlatform Target { get; }
        public ITypeMapper TypeMapper => _typeMapper;

        protected CodeGeneratorBase()
        {
            _indentLevel = 0;
            _tempCounter = 0;
            _valueNames = new Dictionary<IRValue, string>();
            _usings = new HashSet<string>();
            _typeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            InitializeTypeMap();
        }

        /// <summary>
        /// Initialize language-specific type mappings
        /// Override in derived classes
        /// </summary>
        protected virtual void InitializeTypeMap()
        {
            _typeMap["Integer"] = "int";
            _typeMap["Long"] = "long";
            _typeMap["Single"] = "float";
            _typeMap["Double"] = "double";
            _typeMap["String"] = "string";
            _typeMap["Boolean"] = "bool";
            _typeMap["Char"] = "char";
            _typeMap["Void"] = "void";
            _typeMap["Object"] = "object";
        }
        
        public abstract string Generate(IRModule module);
        
        // Visitor methods - implement in derived classes
        public abstract void Visit(IRFunction function);
        public abstract void Visit(BasicBlock block);
        public abstract void Visit(IRConstant constant);
        public abstract void Visit(IRVariable variable);
        public abstract void Visit(IRBinaryOp binaryOp);
        public abstract void Visit(IRUnaryOp unaryOp);
        public abstract void Visit(IRAssignment assignment);
        public abstract void Visit(IRLoad load);
        public abstract void Visit(IRStore store);
        public abstract void Visit(IRCall call);
        public abstract void Visit(IRReturn ret);
        public abstract void Visit(IRBranch branch);
        public abstract void Visit(IRConditionalBranch condBranch);
        public abstract void Visit(IRPhi phi);
        public abstract void Visit(IRAlloca alloca);
        public abstract void Visit(IRGetElementPtr gep);
        public abstract void Visit(IRCast cast);
        public abstract void Visit(IRCompare compare);
        public abstract void Visit(IRSwitch switchInst);
        public abstract void Visit(IRLabel label);
        public abstract void Visit(IRComment comment);
        public abstract void Visit(IRArrayAlloc arrayAlloc);
        public abstract void Visit(IRArrayStore arrayStore);
        public abstract void Visit(IRAwait awaitInst);
        public abstract void Visit(IRYield yieldInst);
        public abstract void Visit(IRNewObject newObj);
        public abstract void Visit(IRInstanceMethodCall methodCall);
        public abstract void Visit(IRBaseMethodCall baseCall);
        public abstract void Visit(IRFieldAccess fieldAccess);
        public abstract void Visit(IRFieldStore fieldStore);
        public abstract void Visit(IRTupleElement tupleElement);
        public abstract void Visit(IRTryCatch tryCatch);
        public abstract void Visit(IRInlineCode inlineCode);

        /// <summary>
        /// Map IR type to target language type
        /// </summary>
        protected virtual string MapType(TypeInfo type)
        {
            if (type == null) return "object";
            
            if (_typeMap.TryGetValue(type.Name, out var mapped))
                return mapped;
            
            return type.Name;
        }
        
        /// <summary>
        /// Get or generate name for IR value
        /// </summary>
        protected string GetValueName(IRValue value)
        {
            if (value is IRConstant constant)
                return EmitConstant(constant);
            
            if (_valueNames.TryGetValue(value, out var name))
                return name;
            
            // Generate new name
            if (value is IRVariable variable)
                name = SanitizeName(variable.Name);
            else
                name = $"t{_tempCounter++}";
            
            _valueNames[value] = name;
            return name;
        }
        
        /// <summary>
        /// Emit a constant value in target language syntax
        /// Override in derived classes for language-specific formatting
        /// </summary>
        protected virtual string EmitConstant(IRConstant constant)
        {
            if (constant.Value == null) return "null";
            if (constant.Value is bool b) return b ? "true" : "false";
            if (constant.Value is string s) return $"\"{EscapeString(s)}\"";
            if (constant.Value is char c) return $"'{EscapeChar(c)}'";
            if (constant.Value is float f) return $"{f}f";
            return constant.Value.ToString();
        }
        
        /// <summary>
        /// Sanitize identifiers for target language
        /// </summary>
        protected virtual string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "_unnamed";

            // Convert VB.NET's "Me" to "this" (C#/C++/etc.)
            if (name.Equals("Me", StringComparison.OrdinalIgnoreCase))
                return "this";

            var sanitized = "";
            foreach (var ch in name)
            {
                if (char.IsLetterOrDigit(ch) || ch == '_')
                    sanitized += ch;
            }

            if (sanitized.Length == 0) return "_unnamed";
            if (char.IsDigit(sanitized[0])) sanitized = "_" + sanitized;

            return sanitized;
        }
        
        protected string EscapeString(string str)
        {
            return str.Replace("\\", "\\\\")
                     .Replace("\"", "\\\"")
                     .Replace("\n", "\\n")
                     .Replace("\r", "\\r")
                     .Replace("\t", "\\t");
        }
        
        protected string EscapeChar(char ch)
        {
            return ch switch
            {
                '\'' => "\\'",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ => ch.ToString()
            };
        }
        
        protected void Indent() => _indentLevel++;
        protected void Unindent() => _indentLevel = Math.Max(0, _indentLevel - 1);
    }
}
