using System;
using System.Collections.Generic;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.SemanticAnalysis;

namespace BasicLang.Compiler.IR
{
    /// <summary>
    /// Base class for all IR instructions
    /// SSA-based three-address code representation
    /// </summary>
    public abstract class IRInstruction
    {
        public int Id { get; set; }
        public BasicBlock ParentBlock { get; set; }
        public TypeInfo Type { get; set; }
        
        protected IRInstruction(TypeInfo type = null)
        {
            Type = type;
        }
        
        public abstract void Accept(IIRVisitor visitor);
        public abstract override string ToString();
    }
    
    /// <summary>
    /// Visitor interface for IR traversal
    /// </summary>
    public interface IIRVisitor
    {
        void Visit(IRFunction function);
        void Visit(BasicBlock block);
        void Visit(IRConstant constant);
        void Visit(IRVariable variable);
        void Visit(IRBinaryOp binaryOp);
        void Visit(IRUnaryOp unaryOp);
        void Visit(IRAssignment assignment);
        void Visit(IRLoad load);
        void Visit(IRStore store);
        void Visit(IRCall call);
        void Visit(IRReturn ret);
        void Visit(IRBranch branch);
        void Visit(IRConditionalBranch condBranch);
        void Visit(IRPhi phi);
        void Visit(IRAlloca alloca);
        void Visit(IRGetElementPtr gep);
        void Visit(IRCast cast);
        void Visit(IRCompare compare);
        void Visit(IRSwitch switchInst);
        void Visit(IRLabel label);
        void Visit(IRComment comment);
        void Visit(IRArrayAlloc arrayAlloc);
        void Visit(IRArrayStore arrayStore);
        void Visit(IRAwait awaitInst);
        void Visit(IRYield yieldInst);
        void Visit(IRNewObject newObj);
        void Visit(IRInstanceMethodCall methodCall);
        void Visit(IRBaseMethodCall baseCall);
        void Visit(IRFieldAccess fieldAccess);
        void Visit(IRFieldStore fieldStore);
        void Visit(IRTupleElement tupleElement);
        void Visit(IRTryCatch tryCatch);
        void Visit(IRInlineCode inlineCode);
        void Visit(IRForEach forEach);
        void Visit(IRIndexerAccess indexer);
    }
    
    // ============================================================================
    // IR Values (can be used as operands)
    // ============================================================================
    
    /// <summary>
    /// Base class for values that can be used in expressions
    /// </summary>
    public abstract class IRValue : IRInstruction
    {
        public string Name { get; set; }
        
        protected IRValue(string name, TypeInfo type) : base(type)
        {
            Name = name;
        }
    }
    
    /// <summary>
    /// Constant value
    /// </summary>
    public class IRConstant : IRValue
    {
        public object Value { get; set; }
        
        public IRConstant(object value, TypeInfo type) 
            : base($"const_{value}", type)
        {
            Value = value;
        }
        
        public override void Accept(IIRVisitor visitor) => visitor.Visit(this);
        
        public override string ToString() => $"{Value}";
    }
    
    /// <summary>
    /// Variable (SSA register)
    /// </summary>
    public class IRVariable : IRValue
    {
        public int Version { get; set; }
        public bool IsParameter { get; set; }
        public bool IsGlobal { get; set; }
        public bool IsConst { get; set; }
        public bool IsOptional { get; set; }
        public bool IsParamArray { get; set; }
        public bool IsByRef { get; set; }
        public IRValue DefaultValue { get; set; }
        public IRValue InitialValue { get; set; }

        /// <summary>
        /// Source module name for multi-file compilation
        /// </summary>
        public string ModuleName { get; set; }

        /// <summary>
        /// Access modifier for the variable (Public, Private, Friend)
        /// </summary>
        public AccessModifier Access { get; set; } = AccessModifier.Private;

        public IRVariable(string name, TypeInfo type, int version = 0)
            : base(name, type)
        {
            Version = version;
        }

        public override void Accept(IIRVisitor visitor) => visitor.Visit(this);
        
        public override string ToString()
        {
            if (IsParameter)
                return $"%{Name}";
            if (IsGlobal)
                return $"@{Name}";
            return Version > 0 ? $"%{Name}.{Version}" : $"%{Name}";
        }
    }
    
    // ============================================================================
    // Arithmetic and Logic Operations
    // ============================================================================
    
    /// <summary>
    /// Binary operation: result = left op right
    /// </summary>
    public class IRBinaryOp : IRValue
    {
        public IRValue Left { get; set; }
        public IRValue Right { get; set; }
        public BinaryOpKind Operation { get; set; }
        
        public IRBinaryOp(string resultName, BinaryOpKind op, IRValue left, IRValue right, TypeInfo type)
            : base(resultName, type)
        {
            Operation = op;
            Left = left;
            Right = right;
        }
        
        public override void Accept(IIRVisitor visitor) => visitor.Visit(this);
        
        public override string ToString() => 
            $"{Name} = {Operation.ToString().ToLower()} {Left}, {Right}";
    }
    
    public enum BinaryOpKind
    {
        // Arithmetic
        Add, Sub, Mul, Div, Mod, IntDiv,

        // Logical (short-circuit)
        And, Or,

        // Bitwise
        BitwiseAnd, BitwiseOr, Xor, Shl, Shr,

        // Comparison (returns boolean)
        Eq, Ne, Lt, Le, Gt, Ge,

        // String
        Concat
    }
    
    /// <summary>
    /// Unary operation: result = op operand
    /// </summary>
    public class IRUnaryOp : IRValue
    {
        public IRValue Operand { get; set; }
        public UnaryOpKind Operation { get; set; }
        
        public IRUnaryOp(string resultName, UnaryOpKind op, IRValue operand, TypeInfo type)
            : base(resultName, type)
        {
            Operation = op;
            Operand = operand;
        }
        
        public override void Accept(IIRVisitor visitor) => visitor.Visit(this);
        
        public override string ToString() => 
            $"{Name} = {Operation.ToString().ToLower()} {Operand}";
    }
    
    public enum UnaryOpKind
    {
        Neg,        // Arithmetic negation
        Not,        // Logical negation
        BitwiseNot, // Bitwise negation
        Inc,        // Increment
        Dec,        // Decrement
        AddressOf   // Method reference for delegates/events
    }
    
    /// <summary>
    /// Comparison operation: result = compare left, right
    /// </summary>
    public class IRCompare : IRValue
    {
        public IRValue Left { get; set; }
        public IRValue Right { get; set; }
        public CompareKind Comparison { get; set; }
        
        public IRCompare(string resultName, CompareKind cmp, IRValue left, IRValue right, TypeInfo type)
            : base(resultName, type)
        {
            Comparison = cmp;
            Left = left;
            Right = right;
        }
        
        public override void Accept(IIRVisitor visitor) => visitor.Visit(this);
        
        public override string ToString() => 
            $"{Name} = cmp {Comparison.ToString().ToLower()} {Left}, {Right}";
    }
    
    public enum CompareKind
    {
        Eq,  // Equal
        Ne,  // Not equal
        Lt,  // Less than
        Le,  // Less or equal
        Gt,  // Greater than
        Ge   // Greater or equal
    }
    
    // ============================================================================
    // Memory Operations
    // ============================================================================
    
    /// <summary>
    /// Load value from memory: result = load ptr
    /// </summary>
    public class IRLoad : IRValue
    {
        public IRValue Address { get; set; }
        
        public IRLoad(string resultName, IRValue address, TypeInfo type)
            : base(resultName, type)
        {
            Address = address;
        }
        
        public override void Accept(IIRVisitor visitor) => visitor.Visit(this);
        
        public override string ToString() => 
            $"{Name} = load {Type} {Address}";
    }
    
    /// <summary>
    /// Store value to memory: store value, ptr
    /// </summary>
    public class IRStore : IRInstruction
    {
        public IRValue Value { get; set; }
        public IRValue Address { get; set; }
        
        public IRStore(IRValue value, IRValue address)
        {
            Value = value;
            Address = address;
        }
        
        public override void Accept(IIRVisitor visitor) => visitor.Visit(this);
        
        public override string ToString() => 
            $"store {Value}, {Address}";
    }
    
    /// <summary>
    /// Allocate stack memory: result = alloca type
    /// </summary>
    public class IRAlloca : IRValue
    {
        public int Size { get; set; }
        
        public IRAlloca(string resultName, TypeInfo type, int size = 1)
            : base(resultName, type)
        {
            Size = size;
        }
        
        public override void Accept(IIRVisitor visitor) => visitor.Visit(this);
        
        public override string ToString() => 
            $"{Name} = alloca {Type}" + (Size > 1 ? $", {Size}" : "");
    }
    
    /// <summary>
    /// Get element pointer: result = gep ptr, indices
    /// </summary>
    public class IRGetElementPtr : IRValue
    {
        public IRValue BasePointer { get; set; }
        public List<IRValue> Indices { get; set; }
        
        public IRGetElementPtr(string resultName, IRValue basePtr, TypeInfo type)
            : base(resultName, type)
        {
            BasePointer = basePtr;
            Indices = new List<IRValue>();
        }
        
        public override void Accept(IIRVisitor visitor) => visitor.Visit(this);
        
        public override string ToString() => 
            $"{Name} = gep {BasePointer}, [{string.Join(", ", Indices)}]";
    }
    
    // ============================================================================
    // Control Flow Operations
    // ============================================================================
    
    /// <summary>
    /// Unconditional branch: br label
    /// </summary>
    public class IRBranch : IRInstruction
    {
        public BasicBlock Target { get; set; }
        
        public IRBranch(BasicBlock target)
        {
            Target = target;
        }
        
        public override void Accept(IIRVisitor visitor) => visitor.Visit(this);
        
        public override string ToString() => $"br {Target.Name}";
    }
    
    /// <summary>
    /// Conditional branch: br cond, trueLabel, falseLabel
    /// </summary>
    public class IRConditionalBranch : IRInstruction
    {
        public IRValue Condition { get; set; }
        public BasicBlock TrueTarget { get; set; }
        public BasicBlock FalseTarget { get; set; }
        
        public IRConditionalBranch(IRValue condition, BasicBlock trueTarget, BasicBlock falseTarget)
        {
            Condition = condition;
            TrueTarget = trueTarget;
            FalseTarget = falseTarget;
        }
        
        public override void Accept(IIRVisitor visitor) => visitor.Visit(this);
        
        public override string ToString() => 
            $"br {Condition}, {TrueTarget.Name}, {FalseTarget.Name}";
    }
    
    // ============================================================================
    // Pattern Matching IR Types
    // ============================================================================

    /// <summary>
    /// Base class for pattern cases in switch statements
    /// </summary>
    public abstract class IRPatternCase
    {
        public BasicBlock Target { get; set; }
        public string BindingVariable { get; set; }  // Optional variable binding
        public IRValue WhenGuard { get; set; }  // Optional When clause condition

        protected IRPatternCase(BasicBlock target)
        {
            Target = target;
        }
    }

    /// <summary>
    /// Type pattern case: checks if value is of a specific type
    /// </summary>
    public class IRTypePatternCase : IRPatternCase
    {
        public string TypeName { get; set; }

        public IRTypePatternCase(string typeName, BasicBlock target) : base(target)
        {
            TypeName = typeName;
        }
    }

    /// <summary>
    /// Range pattern case: checks if value is within a range (1 To 10)
    /// </summary>
    public class IRRangePatternCase : IRPatternCase
    {
        public IRValue LowerBound { get; set; }
        public IRValue UpperBound { get; set; }

        public IRRangePatternCase(IRValue lower, IRValue upper, BasicBlock target) : base(target)
        {
            LowerBound = lower;
            UpperBound = upper;
        }
    }

    /// <summary>
    /// Comparison pattern case: checks if value matches a comparison (> 10, < 5)
    /// </summary>
    public class IRComparisonPatternCase : IRPatternCase
    {
        public string Operator { get; set; }  // ">", "<", ">=", "<=", "=", "<>"
        public IRValue CompareValue { get; set; }

        public IRComparisonPatternCase(string op, IRValue value, BasicBlock target) : base(target)
        {
            Operator = op;
            CompareValue = value;
        }
    }

    /// <summary>
    /// Constant pattern case: checks if value equals a constant
    /// </summary>
    public class IRConstantPatternCase : IRPatternCase
    {
        public IRValue Value { get; set; }

        public IRConstantPatternCase(IRValue value, BasicBlock target) : base(target)
        {
            Value = value;
        }
    }

    /// <summary>
    /// Nothing (null) pattern case: checks if value is null
    /// </summary>
    public class IRNothingPatternCase : IRPatternCase
    {
        public IRNothingPatternCase(BasicBlock target) : base(target) { }
    }

    /// <summary>
    /// Or pattern case: matches if any of the alternatives match
    /// </summary>
    public class IROrPatternCase : IRPatternCase
    {
        public List<IRPatternCase> Alternatives { get; set; }

        public IROrPatternCase(BasicBlock target) : base(target)
        {
            Alternatives = new List<IRPatternCase>();
        }
    }

    /// <summary>
    /// Tuple/deconstruction pattern case: matches and deconstructs a tuple
    /// </summary>
    public class IRTuplePatternCase : IRPatternCase
    {
        public List<IRPatternCase> Elements { get; set; }

        public IRTuplePatternCase(BasicBlock target) : base(target)
        {
            Elements = new List<IRPatternCase>();
        }
    }

    /// <summary>
    /// Binding pattern case: captures value with variable and optional guard (var pattern)
    /// </summary>
    public class IRBindingPatternCase : IRPatternCase
    {
        public IRBindingPatternCase(BasicBlock target) : base(target) { }
    }

    /// <summary>
    /// Multi-way branch: switch value, default, [case: label, ...]
    /// </summary>
    public class IRSwitch : IRInstruction
    {
        public IRValue Value { get; set; }
        public BasicBlock DefaultTarget { get; set; }
        public BasicBlock EndBlock { get; set; }  // The block after the switch statement
        public List<(IRValue CaseValue, BasicBlock Target)> Cases { get; set; }
        public List<IRPatternCase> PatternCases { get; set; }  // Pattern-based cases

        public IRSwitch(IRValue value, BasicBlock defaultTarget)
        {
            Value = value;
            DefaultTarget = defaultTarget;
            Cases = new List<(IRValue, BasicBlock)>();
            PatternCases = new List<IRPatternCase>();
        }

        public override void Accept(IIRVisitor visitor) => visitor.Visit(this);

        public override string ToString() =>
            $"switch {Value}, {DefaultTarget.Name}, [{Cases.Count} cases, {PatternCases.Count} patterns]";
    }
    
    /// <summary>
    /// Return from function: ret value
    /// </summary>
    public class IRReturn : IRInstruction
    {
        public IRValue Value { get; set; }
        
        public IRReturn(IRValue value = null)
        {
            Value = value;
        }
        
        public override void Accept(IIRVisitor visitor) => visitor.Visit(this);
        
        public override string ToString() => 
            Value != null ? $"ret {Value}" : "ret void";
    }
    
    // ============================================================================
    // Function Operations
    // ============================================================================
    
    /// <summary>
    /// Function call: result = call function(args)
    /// </summary>
    public class IRCall : IRValue
    {
        public string FunctionName { get; set; }
        public List<IRValue> Arguments { get; set; }
        public List<bool> ByRefArguments { get; set; }  // Track which arguments are by-ref
        public bool IsTailCall { get; set; }

        public IRCall(string resultName, string functionName, TypeInfo returnType)
            : base(resultName, returnType)
        {
            FunctionName = functionName;
            Arguments = new List<IRValue>();
            ByRefArguments = new List<bool>();
        }
        
        public override void Accept(IIRVisitor visitor) => visitor.Visit(this);
        
        public override string ToString()
        {
            var args = string.Join(", ", Arguments);
            var tail = IsTailCall ? "tail " : "";
            return Type != null && Type.Name != "Void"
                ? $"{Name} = {tail}call {FunctionName}({args})"
                : $"{tail}call {FunctionName}({args})";
        }
    }
    
    // ============================================================================
    // SSA Operations
    // ============================================================================
    
    /// <summary>
    /// Phi node: result = phi [value1, block1], [value2, block2], ...
    /// Used for SSA form to merge values from different control flow paths
    /// </summary>
    public class IRPhi : IRValue
    {
        public List<(IRValue Value, BasicBlock Block)> Operands { get; set; }
        
        public IRPhi(string resultName, TypeInfo type)
            : base(resultName, type)
        {
            Operands = new List<(IRValue, BasicBlock)>();
        }
        
        public override void Accept(IIRVisitor visitor) => visitor.Visit(this);
        
        public override string ToString()
        {
            var operands = string.Join(", ", 
                Operands.ConvertAll(op => $"[{op.Value}, {op.Block.Name}]"));
            return $"{Name} = phi {Type} {operands}";
        }
    }
    
    // ============================================================================
    // Type Operations
    // ============================================================================
    
    /// <summary>
    /// Type cast: result = cast value to type
    /// </summary>
    public class IRCast : IRValue
    {
        public IRValue Value { get; set; }
        public TypeInfo SourceType { get; set; }
        public CastKind Kind { get; set; }
        
        public IRCast(string resultName, IRValue value, TypeInfo sourceType, TypeInfo targetType, CastKind kind)
            : base(resultName, targetType)
        {
            Value = value;
            SourceType = sourceType;
            Kind = kind;
        }
        
        public override void Accept(IIRVisitor visitor) => visitor.Visit(this);
        
        public override string ToString() => 
            $"{Name} = {Kind.ToString().ToLower()} {Value} to {Type}";
    }
    
    public enum CastKind
    {
        Bitcast,    // No-op cast (pointer types)
        Trunc,      // Truncate to smaller type
        ZExt,       // Zero extend to larger type
        SExt,       // Sign extend to larger type
        FPTrunc,    // Float truncate
        FPExt,      // Float extend
        FPToUI,     // Float to unsigned int
        FPToSI,     // Float to signed int
        UIToFP,     // Unsigned int to float
        SIToFP,     // Signed int to float
        PtrToInt,   // Pointer to integer
        IntToPtr    // Integer to pointer
    }
    
    // ============================================================================
    // Misc Operations
    // ============================================================================
    
    /// <summary>
    /// Assignment (for non-SSA variables): var = value
    /// </summary>
    public class IRAssignment : IRInstruction
    {
        public IRVariable Target { get; set; }
        public IRValue Value { get; set; }
        
        public IRAssignment(IRVariable target, IRValue value)
        {
            Target = target;
            Value = value;
        }
        
        public override void Accept(IIRVisitor visitor) => visitor.Visit(this);
        
        public override string ToString() => $"{Target} = {Value}";
    }
    
    /// <summary>
    /// Label (for legacy support)
    /// </summary>
    public class IRLabel : IRInstruction
    {
        public string Name { get; set; }
        
        public IRLabel(string name)
        {
            Name = name;
        }
        
        public override void Accept(IIRVisitor visitor) => visitor.Visit(this);
        
        public override string ToString() => $"{Name}:";
    }
    
    /// <summary>
    /// Comment for debugging
    /// </summary>
    public class IRComment : IRInstruction
    {
        public string Text { get; set; }

        public IRComment(string text)
        {
            Text = text;
        }

        public override void Accept(IIRVisitor visitor) => visitor.Visit(this);

        public override string ToString() => $"; {Text}";
    }

    /// <summary>
    /// Inline code - raw code for a specific target language (C#, C++, LLVM, MSIL)
    /// </summary>
    public class IRInlineCode : IRInstruction
    {
        public string Language { get; set; }  // "csharp", "cpp", "llvm", "msil"
        public string Code { get; set; }

        public IRInlineCode(string language, string code)
        {
            Language = language;
            Code = code;
        }

        public override void Accept(IIRVisitor visitor) => visitor.Visit(this);

        public override string ToString() => $"inline {Language} {{ {Code.Length} chars }}";
    }

    /// <summary>
    /// Array allocation - allocates an array of a given size
    /// </summary>
    public class IRArrayAlloc : IRValue
    {
        public TypeInfo ElementType { get; set; }
        public int Size { get; set; }

        public IRArrayAlloc(string name, TypeInfo elementType, int size)
            : base(name, new TypeInfo($"{elementType.Name}[]", TypeKind.Array) { ElementType = elementType })
        {
            ElementType = elementType;
            Size = size;
        }

        public override void Accept(IIRVisitor visitor) => visitor.Visit(this);

        public override string ToString() => $"{Name} = new {ElementType.Name}[{Size}]";
    }

    /// <summary>
    /// Array store - stores a value at an index in an array
    /// </summary>
    public class IRArrayStore : IRInstruction
    {
        public IRValue Array { get; set; }
        public IRValue Index { get; set; }
        public IRValue Value { get; set; }

        public IRArrayStore(IRValue array, IRValue index, IRValue value)
        {
            Array = array;
            Index = index;
            Value = value;
        }

        public override void Accept(IIRVisitor visitor) => visitor.Visit(this);

        public override string ToString() => $"{Array.Name}[{Index}] = {Value}";
    }

    /// <summary>
    /// IR Await - awaits an async expression
    /// </summary>
    public class IRAwait : IRValue
    {
        public IRValue Expression { get; set; }

        public IRAwait(string resultName, IRValue expression, TypeInfo resultType)
            : base(resultName, resultType)
        {
            Expression = expression;
        }

        public override void Accept(IIRVisitor visitor) => visitor.Visit(this);

        public override string ToString() => $"{Name} = await {Expression}";
    }

    /// <summary>
    /// IR Yield - yields a value from an iterator
    /// </summary>
    public class IRYield : IRInstruction
    {
        public IRValue Value { get; set; }
        public bool IsBreak { get; set; }

        public IRYield(IRValue value, bool isBreak = false)
        {
            Value = value;
            IsBreak = isBreak;
        }

        public override void Accept(IIRVisitor visitor) => visitor.Visit(this);

        public override string ToString() => IsBreak ? "yield break" : $"yield return {Value}";
    }

    /// <summary>
    /// IR Indexer access - represents collection[index] or dictionary[key]
    /// </summary>
    public class IRIndexerAccess : IRValue
    {
        public IRValue Collection { get; set; }
        public List<IRValue> Indices { get; set; }

        public IRIndexerAccess(string name, IRValue collection, TypeInfo resultType)
            : base(name, resultType)
        {
            Collection = collection;
            Indices = new List<IRValue>();
        }

        public override void Accept(IIRVisitor visitor) => visitor.Visit(this);

        public override string ToString()
        {
            var indices = string.Join(", ", Indices.Select(i => i?.Name ?? "?"));
            return $"{Name} = {Collection?.Name}[{indices}]";
        }
    }

    /// <summary>
    /// IR ForEach loop - represents iteration over a collection
    /// </summary>
    public class IRForEach : IRInstruction
    {
        public string VariableName { get; set; }
        public TypeInfo ElementType { get; set; }
        public IRValue Collection { get; set; }
        public BasicBlock BodyBlock { get; set; }
        public BasicBlock EndBlock { get; set; }

        public IRForEach(string variableName, TypeInfo elementType, IRValue collection, BasicBlock bodyBlock, BasicBlock endBlock)
        {
            VariableName = variableName;
            ElementType = elementType;
            Collection = collection;
            BodyBlock = bodyBlock;
            EndBlock = endBlock;
        }

        public override void Accept(IIRVisitor visitor) => visitor.Visit(this);

        public override string ToString()
        {
            return $"foreach ({ElementType?.Name ?? "var"} {VariableName} in {Collection.Name}) {{ goto {BodyBlock.Name} }} end {{ goto {EndBlock.Name} }}";
        }
    }

    /// <summary>
    /// IR Try-Catch structure - represents exception handling
    /// </summary>
    public class IRTryCatch : IRInstruction
    {
        public BasicBlock TryBlock { get; set; }
        public List<IRCatchClause> CatchClauses { get; set; }
        public BasicBlock FinallyBlock { get; set; }
        public BasicBlock EndBlock { get; set; }

        public IRTryCatch(BasicBlock tryBlock, List<IRCatchClause> catchClauses, BasicBlock finallyBlock, BasicBlock endBlock)
        {
            TryBlock = tryBlock;
            CatchClauses = catchClauses ?? new List<IRCatchClause>();
            FinallyBlock = finallyBlock;
            EndBlock = endBlock;
        }

        public override void Accept(IIRVisitor visitor) => visitor.Visit(this);

        public override string ToString()
        {
            var result = $"try {{ goto {TryBlock.Name} }}";
            foreach (var clause in CatchClauses)
            {
                result += $" catch ({clause.ExceptionType?.Name ?? "Exception"} {clause.VariableName}) {{ goto {clause.Block.Name} }}";
            }
            if (FinallyBlock != null)
            {
                result += $" finally {{ goto {FinallyBlock.Name} }}";
            }
            return result;
        }
    }

    /// <summary>
    /// Represents a catch clause in a try-catch block
    /// </summary>
    public class IRCatchClause
    {
        public TypeInfo ExceptionType { get; set; }
        public string VariableName { get; set; }
        public BasicBlock Block { get; set; }

        public IRCatchClause(TypeInfo exceptionType, string variableName, BasicBlock block)
        {
            ExceptionType = exceptionType;
            VariableName = variableName;
            Block = block;
        }
    }

    // ============================================================================
    // Basic Block and Function
    // ============================================================================
    
    /// <summary>
    /// Basic block - a sequence of instructions with a single entry and exit
    /// </summary>
    public class BasicBlock
    {
        public string Name { get; set; }
        public List<IRInstruction> Instructions { get; set; }
        public List<BasicBlock> Predecessors { get; set; }
        public List<BasicBlock> Successors { get; set; }
        public IRFunction ParentFunction { get; set; }
        public int Id { get; set; }
        
        // For optimization passes
        public bool IsVisited { get; set; }
        public HashSet<BasicBlock> Dominators { get; set; }
        public BasicBlock ImmediateDominator { get; set; }
        public HashSet<BasicBlock> DominanceFrontier { get; set; }
        
        public BasicBlock(string name)
        {
            Name = name;
            Instructions = new List<IRInstruction>();
            Predecessors = new List<BasicBlock>();
            Successors = new List<BasicBlock>();
            Dominators = new HashSet<BasicBlock>();
            DominanceFrontier = new HashSet<BasicBlock>();
        }
        
        public void AddInstruction(IRInstruction instruction)
        {
            Instructions.Add(instruction);
            instruction.ParentBlock = this;
        }
        
        public IRInstruction GetTerminator()
        {
            return Instructions.Count > 0 ? Instructions[Instructions.Count - 1] : null;
        }
        
        public bool IsTerminated()
        {
            var terminator = GetTerminator();
            return terminator is IRBranch || 
                   terminator is IRConditionalBranch || 
                   terminator is IRReturn ||
                   terminator is IRSwitch;
        }
        
        public void Accept(IIRVisitor visitor)
        {
            visitor.Visit(this);
        }
        
        public override string ToString() => Name;
    }
    
    /// <summary>
    /// IR Function - contains basic blocks
    /// </summary>
    public class IRFunction
    {
        public string Name { get; set; }
        public TypeInfo ReturnType { get; set; }
        public List<IRVariable> Parameters { get; set; }
        public List<BasicBlock> Blocks { get; set; }
        public BasicBlock EntryBlock { get; set; }
        public List<IRVariable> LocalVariables { get; set; }
        public bool IsExternal { get; set; }
        public List<string> GenericParameters { get; set; }
        public List<GenericTypeParameter> GenericTypeParams { get; set; }  // With constraints
        public bool IsAsync { get; set; }
        public bool IsIterator { get; set; }
        public bool IsExtension { get; set; }
        public string ExtendedType { get; set; }
        public bool IsLambda { get; set; }
        public List<(string name, TypeInfo type)> CapturedVariables { get; set; }

        /// <summary>
        /// Source module name for multi-file compilation
        /// </summary>
        public string ModuleName { get; set; }

        /// <summary>
        /// Access modifier for the function (Public, Private, Friend)
        /// </summary>
        public AccessModifier Access { get; set; } = AccessModifier.Private;

        private int _nextBlockId = 0;
        private int _nextTempId = 0;

        public IRFunction(string name, TypeInfo returnType)
        {
            Name = name;
            ReturnType = returnType;
            Parameters = new List<IRVariable>();
            Blocks = new List<BasicBlock>();
            LocalVariables = new List<IRVariable>();
            GenericParameters = new List<string>();
            GenericTypeParams = new List<GenericTypeParameter>();
            CapturedVariables = new List<(string name, TypeInfo type)>();
        }
        
        public BasicBlock CreateBlock(string name = null)
        {
            if (name == null)
                name = $"bb{_nextBlockId}";
            
            var block = new BasicBlock(name)
            {
                Id = _nextBlockId++,
                ParentFunction = this
            };
            
            Blocks.Add(block);
            
            if (EntryBlock == null)
                EntryBlock = block;
            
            return block;
        }
        
        public string GetNextTempName()
        {
            return $"t{_nextTempId++}";
        }
        
        public void Accept(IIRVisitor visitor)
        {
            visitor.Visit(this);
        }
        
        public override string ToString() => $"function {Name}";
    }
    
    /// <summary>
    /// Represents a .NET using directive
    /// </summary>
    public class NetUsingDirective
    {
        public string Namespace { get; set; }
        public string Alias { get; set; }

        public NetUsingDirective(string ns, string alias = null)
        {
            Namespace = ns;
            Alias = alias;
        }
    }

    /// <summary>
    /// IR Module - top-level container
    /// </summary>
    public class IRModule
    {
        public string Name { get; set; }
        public List<IRFunction> Functions { get; set; }
        public Dictionary<string, IRVariable> GlobalVariables { get; set; }
        public Dictionary<string, TypeInfo> Types { get; set; }
        public Dictionary<string, IRExternDeclaration> ExternDeclarations { get; set; }
        public Dictionary<string, IRClass> Classes { get; set; }
        public Dictionary<string, IRInterface> Interfaces { get; set; }
        public Dictionary<string, IREnum> Enums { get; set; }
        public Dictionary<string, IRDelegate> Delegates { get; set; }
        public List<string> Namespaces { get; set; }

        /// <summary>
        /// .NET namespace imports (e.g., System.IO, System.Text)
        /// These are passed through to the C# backend as using directives
        /// </summary>
        public List<NetUsingDirective> NetUsings { get; set; }

        public IRModule(string name)
        {
            Name = name;
            Functions = new List<IRFunction>();
            GlobalVariables = new Dictionary<string, IRVariable>();
            Types = new Dictionary<string, TypeInfo>();
            ExternDeclarations = new Dictionary<string, IRExternDeclaration>(StringComparer.OrdinalIgnoreCase);
            Classes = new Dictionary<string, IRClass>(StringComparer.OrdinalIgnoreCase);
            Interfaces = new Dictionary<string, IRInterface>(StringComparer.OrdinalIgnoreCase);
            Enums = new Dictionary<string, IREnum>(StringComparer.OrdinalIgnoreCase);
            Delegates = new Dictionary<string, IRDelegate>(StringComparer.OrdinalIgnoreCase);
            Namespaces = new List<string>();
            NetUsings = new List<NetUsingDirective>();
        }

        public IRFunction CreateFunction(string name, TypeInfo returnType)
        {
            var function = new IRFunction(name, returnType);
            Functions.Add(function);
            return function;
        }

        public IRVariable CreateGlobalVariable(string name, TypeInfo type)
        {
            var variable = new IRVariable(name, type)
            {
                IsGlobal = true
            };
            GlobalVariables[name] = variable;
            return variable;
        }

        /// <summary>
        /// Check if a function is an extern
        /// </summary>
        public bool IsExtern(string name) => ExternDeclarations.ContainsKey(name);

        /// <summary>
        /// Get extern declaration by name
        /// </summary>
        public IRExternDeclaration GetExtern(string name)
        {
            ExternDeclarations.TryGetValue(name, out var externDecl);
            return externDecl;
        }
    }

    /// <summary>
    /// Represents an extern (platform-native) function declaration
    /// </summary>
    public class IRExternDeclaration
    {
        public string Name { get; set; }
        public bool IsFunction { get; set; }
        public TypeInfo ReturnType { get; set; }
        public List<IRParameter> Parameters { get; set; }
        public Dictionary<string, string> PlatformImplementations { get; set; }

        // C library interop properties
        public string LibraryName { get; set; }      // The DLL/SO library name
        public string AliasName { get; set; }        // The actual function name in the library
        public string CallingConvention { get; set; } // Calling convention (CDecl, StdCall, etc.)

        public IRExternDeclaration()
        {
            Parameters = new List<IRParameter>();
            PlatformImplementations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            CallingConvention = "Default";
        }

        /// <summary>
        /// Gets the actual name to call in the library (AliasName if specified, otherwise Name)
        /// </summary>
        public string GetActualName() => !string.IsNullOrEmpty(AliasName) ? AliasName : Name;

        /// <summary>
        /// Get the implementation string for a specific platform
        /// </summary>
        public string GetImplementation(string platform)
        {
            if (PlatformImplementations.TryGetValue(platform, out var impl))
                return impl;
            return null;
        }

        /// <summary>
        /// Check if this extern has an implementation for the given platform
        /// </summary>
        public bool HasImplementation(string platform)
        {
            return PlatformImplementations.ContainsKey(platform);
        }
    }

    /// <summary>
    /// Represents a parameter in an IR function or extern
    /// </summary>
    public class IRParameter
    {
        public string Name { get; set; }
        public string TypeName { get; set; }
        public TypeInfo Type { get; set; }
        public bool IsOptional { get; set; }
        public bool IsParamArray { get; set; }
        public bool IsByRef { get; set; }
        public IRValue DefaultValue { get; set; }
    }

    /// <summary>
    /// Represents an interface definition in IR
    /// </summary>
    public class IRInterface
    {
        public string Name { get; set; }
        public string Namespace { get; set; }
        public List<IRInterfaceMethod> Methods { get; set; }
        public List<IRInterfaceProperty> Properties { get; set; }
        public List<string> BaseInterfaces { get; set; }

        public IRInterface(string name)
        {
            Name = name;
            Methods = new List<IRInterfaceMethod>();
            Properties = new List<IRInterfaceProperty>();
            BaseInterfaces = new List<string>();
        }
    }

    /// <summary>
    /// Represents a method signature in an interface
    /// </summary>
    public class IRInterfaceMethod
    {
        public string Name { get; set; }
        public TypeInfo ReturnType { get; set; }
        public List<IRParameter> Parameters { get; set; }
        public bool HasDefaultImplementation { get; set; }
        public IRFunction DefaultImplementation { get; set; }

        public IRInterfaceMethod()
        {
            Parameters = new List<IRParameter>();
        }
    }

    /// <summary>
    /// Represents a property signature in an interface
    /// </summary>
    public class IRInterfaceProperty
    {
        public string Name { get; set; }
        public TypeInfo Type { get; set; }
        public bool HasGetter { get; set; }
        public bool HasSetter { get; set; }
    }

    /// <summary>
    /// Represents an enum definition in IR
    /// </summary>
    public class IREnum
    {
        public string Name { get; set; }
        public string Namespace { get; set; }
        public TypeInfo UnderlyingType { get; set; }
        public List<IREnumMember> Members { get; set; }

        public IREnum(string name)
        {
            Name = name;
            Members = new List<IREnumMember>();
        }
    }

    /// <summary>
    /// Represents an enum member
    /// </summary>
    public class IREnumMember
    {
        public string Name { get; set; }
        public object Value { get; set; }
    }

    /// <summary>
    /// Represents a delegate definition in IR
    /// </summary>
    public class IRDelegate
    {
        public string Name { get; set; }
        public string Namespace { get; set; }
        public TypeInfo ReturnType { get; set; }
        public List<IRParameter> Parameters { get; set; }

        public IRDelegate(string name)
        {
            Name = name;
            Parameters = new List<IRParameter>();
        }
    }

    /// <summary>
    /// Represents an event definition in IR
    /// </summary>
    public class IREvent
    {
        public string Name { get; set; }
        public AccessModifier Access { get; set; }
        public string DelegateType { get; set; }
        public bool IsStatic { get; set; }
    }

    /// <summary>
    /// Represents a class definition in IR
    /// </summary>
    public class IRClass
    {
        public string Name { get; set; }
        public string Namespace { get; set; }
        public string BaseClass { get; set; }
        public List<string> Interfaces { get; set; }
        public List<IRField> Fields { get; set; }
        public List<IRMethod> Methods { get; set; }
        public List<IRProperty> Properties { get; set; }
        public List<IRConstructor> Constructors { get; set; }
        public List<IREvent> Events { get; set; }
        public List<string> GenericParameters { get; set; }
        public List<GenericTypeParameter> GenericTypeParams { get; set; }  // With constraints
        public bool IsAbstract { get; set; }

        public IRClass(string name)
        {
            Name = name;
            Interfaces = new List<string>();
            Fields = new List<IRField>();
            Methods = new List<IRMethod>();
            Properties = new List<IRProperty>();
            Constructors = new List<IRConstructor>();
            Events = new List<IREvent>();
            GenericParameters = new List<string>();
            GenericTypeParams = new List<GenericTypeParameter>();
        }
    }

    /// <summary>
    /// Represents a field in an IR class
    /// </summary>
    public class IRField
    {
        public string Name { get; set; }
        public TypeInfo Type { get; set; }
        public AccessModifier Access { get; set; }
        public bool IsStatic { get; set; }
        public IRValue Initializer { get; set; }
    }

    /// <summary>
    /// Represents a method in an IR class
    /// </summary>
    public class IRMethod
    {
        public string Name { get; set; }
        public TypeInfo ReturnType { get; set; }
        public AccessModifier Access { get; set; }
        public bool IsStatic { get; set; }
        public bool IsVirtual { get; set; }
        public bool IsOverride { get; set; }
        public bool IsAbstract { get; set; }
        public bool IsSealed { get; set; }
        public List<IRVariable> Parameters { get; set; }
        public IRFunction Implementation { get; set; }
        public List<string> GenericParameters { get; set; }

        public IRMethod()
        {
            Parameters = new List<IRVariable>();
            GenericParameters = new List<string>();
        }
    }

    /// <summary>
    /// Represents a property in an IR class
    /// </summary>
    public class IRProperty
    {
        public string Name { get; set; }
        public TypeInfo Type { get; set; }
        public AccessModifier Access { get; set; }
        public bool IsStatic { get; set; }
        public bool IsReadOnly { get; set; }
        public bool IsWriteOnly { get; set; }
        public IRFunction Getter { get; set; }
        public IRFunction Setter { get; set; }
    }

    /// <summary>
    /// Represents a constructor in an IR class
    /// </summary>
    public class IRConstructor
    {
        public AccessModifier Access { get; set; }
        public List<IRVariable> Parameters { get; set; }
        public IRFunction Implementation { get; set; }
        public List<IRValue> BaseConstructorArgs { get; set; }

        public IRConstructor()
        {
            Parameters = new List<IRVariable>();
            BaseConstructorArgs = new List<IRValue>();
        }
    }

    /// <summary>
    /// Represents a new object instantiation: new ClassName(args)
    /// </summary>
    public class IRNewObject : IRValue
    {
        public string ClassName { get; set; }
        public List<IRValue> Arguments { get; set; }

        public IRNewObject(string resultName, string className, TypeInfo type)
            : base(resultName, type)
        {
            ClassName = className;
            Arguments = new List<IRValue>();
        }

        public override void Accept(IIRVisitor visitor) => visitor.Visit(this);
        public override string ToString()
        {
            var args = string.Join(", ", Arguments.Select(a => a.Name));
            return $"{Name} = new {ClassName}({args})";
        }
    }

    /// <summary>
    /// Represents a method call on an object instance: obj.Method(args)
    /// </summary>
    public class IRInstanceMethodCall : IRValue
    {
        public IRValue Object { get; set; }
        public string MethodName { get; set; }
        public List<IRValue> Arguments { get; set; }
        public bool IsVirtual { get; set; }

        public IRInstanceMethodCall(string resultName, IRValue obj, string methodName, TypeInfo returnType)
            : base(resultName, returnType)
        {
            Object = obj;
            MethodName = methodName;
            Arguments = new List<IRValue>();
        }

        public override void Accept(IIRVisitor visitor) => visitor.Visit(this);
        public override string ToString()
        {
            var args = string.Join(", ", Arguments.Select(a => a.Name));
            return $"{Name} = {Object.Name}.{MethodName}({args})";
        }
    }

    /// <summary>
    /// Represents a base class method call: base.Method(args) or MyBase.Method(args)
    /// </summary>
    public class IRBaseMethodCall : IRValue
    {
        public string MethodName { get; set; }
        public List<IRValue> Arguments { get; set; }

        public IRBaseMethodCall(string resultName, string methodName, TypeInfo returnType)
            : base(resultName, returnType)
        {
            MethodName = methodName;
            Arguments = new List<IRValue>();
        }

        public override void Accept(IIRVisitor visitor) => visitor.Visit(this);
        public override string ToString()
        {
            var args = string.Join(", ", Arguments.Select(a => a.Name));
            return $"{Name} = base.{MethodName}({args})";
        }
    }

    /// <summary>
    /// Represents a field access on an object: obj.Field
    /// </summary>
    public class IRFieldAccess : IRValue
    {
        public IRValue Object { get; set; }
        public string FieldName { get; set; }

        public IRFieldAccess(string resultName, IRValue obj, string fieldName, TypeInfo type)
            : base(resultName, type)
        {
            Object = obj;
            FieldName = fieldName;
        }

        public override void Accept(IIRVisitor visitor) => visitor.Visit(this);
        public override string ToString() => $"{Name} = {Object.Name}.{FieldName}";
    }

    /// <summary>
    /// Represents a field store operation: obj.Field = value
    /// </summary>
    public class IRFieldStore : IRInstruction
    {
        public IRValue Object { get; set; }
        public string FieldName { get; set; }
        public IRValue Value { get; set; }

        public IRFieldStore(IRValue obj, string fieldName, IRValue value)
        {
            Object = obj;
            FieldName = fieldName;
            Value = value;
        }

        public override void Accept(IIRVisitor visitor) => visitor.Visit(this);
        public override string ToString() => $"{Object.Name}.{FieldName} = {Value.Name}";
    }

    /// <summary>
    /// Represents accessing an element from a tuple by index
    /// </summary>
    public class IRTupleElement : IRValue
    {
        public IRValue Tuple { get; set; }
        public int Index { get; set; }

        public IRTupleElement(IRValue tuple, int index, TypeInfo elementType)
            : base($"_tuple_elem_{index}", elementType)
        {
            Tuple = tuple;
            Index = index;
        }

        public override void Accept(IIRVisitor visitor) => visitor.Visit(this);
        public override string ToString() => $"{Name} = {Tuple.Name}.Item{Index + 1}";
    }

    /// <summary>
    /// Access modifier for class members
    /// </summary>
    public enum AccessModifier
    {
        Public,
        Private,
        Protected,
        Friend
    }
}
