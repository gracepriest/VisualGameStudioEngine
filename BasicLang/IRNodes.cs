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

        /// <summary>
        /// Source line number from the original source code (1-based). 0 means unknown.
        /// </summary>
        public int SourceLine { get; set; }

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

        // Default no-op so existing visitors (pretty-printer, LLVM, MSIL) are unaffected;
        // backends that support exceptions override this.
        void Visit(IRThrow throwInst) { }

        // Default no-op so visitors that don't yet lower collection-indexer writes
        // (pretty-printer, LLVM, MSIL) are unaffected; C++ and C# backends override this.
        // TODO(Task 6): LLVM/MSIL silently drop collection indexed writes until ForeignFeatureChecker rejects collections on those backends.
        void Visit(IRIndexerStore indexerStore) { }
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
        /// <summary>
        /// True when this local's type was INFERRED from its initializer (`Dim x = expr`
        /// / `Auto x = expr`) rather than explicitly declared with an `As` clause. The C++
        /// backend needs this for opaque foreign (::-qualified) initializers: an inferred
        /// foreign type is a synthetic member-path pseudo-type (e.g. "probe::Widget::compute"
        /// from a member access) that is NOT a real C++ type, so the declaration must be
        /// emitted as `auto` instead of the pseudo-type. An EXPLICIT foreign type
        /// (`Dim it As std::vector(Of Integer)::iterator`) is a real type and stays verbatim.
        /// </summary>
        public bool IsInferredType { get; set; }
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
    /// P2a-2 Task-8 Step 0 (M1) — the .NET CARRIAGE an IR node can hold: the resolved member,
    /// its receiver's boundary category, and whether the descriptor's signature identity is
    /// exact. <b>SEVEN</b> node types carry the trio (<see cref="IRCall"/>,
    /// <see cref="IRInstanceMethodCall"/>, <see cref="IRNewObject"/>,
    /// <see cref="IRFieldAccess"/>, <see cref="IRFieldStore"/>, and — since Task 9 (§8.5) —
    /// <see cref="IRIndexerAccess"/> and <see cref="IRIndexerStore"/>) — exactly the seven the
    /// C++ lowering has an arm for.
    ///
    /// <para><b><see cref="IRForEach"/> is the deliberate exception</b>, and it is why this
    /// count is worth stating rather than implying: §8.5's enumeration needs FOUR descriptors
    /// (<c>GetEnumerator</c>/<c>MoveNext</c>/<c>Current</c>/<c>Dispose</c>) and this interface
    /// holds one, so it carries an <see cref="IRNetEnumeration"/> bundle instead and the surface
    /// collector handles it in its own arm. Making it implement this interface by naming one of
    /// the four would collect one export and leave the loop unlinkable.</para>
    ///
    /// <para><b>What this buys.</b> <c>NetSurfaceCollector</c> collapses five near-identical
    /// <c>case</c> arms into one, and the optimizer's "carry the carriage across" obligation
    /// (<c>NetIrCarriageTests</c>) gets a single anchor to name instead of a per-node-type
    /// list that a sixth carrier would silently fall off.</para>
    ///
    /// <para><b>Implemented EXPLICITLY on every carrier, deliberately.</b> The three
    /// auto-properties are <c>internal</c> — "carriage adds nothing to the compiler's public
    /// API" — and an internal member cannot implicitly implement an (implicitly public)
    /// interface member. Making them public to satisfy the interface would widen the API this
    /// interface exists to keep narrow, so each carrier forwards instead.</para>
    ///
    /// <para>Read-only: this is a QUERY seam for consumers that do not care which node type
    /// they have. Producers (<c>IRBuilder</c>, the optimizer's clone paths) keep writing the
    /// concrete node's settable properties, which is where the per-node documentation lives.</para>
    /// </summary>
    internal interface INetCarrying
    {
        BasicLang.Net.NetMemberDescriptor ResolvedNetTarget { get; }
        BoundaryTypeCategory NetCategory { get; }
        bool ResolvedNetTargetIsExact { get; }
    }

    /// <summary>
    /// Function call: result = call function(args)
    /// </summary>
    public class IRCall : IRValue, INetCarrying
    {
        public string FunctionName { get; set; }
        public List<IRValue> Arguments { get; set; }
        public List<bool> ByRefArguments { get; set; }  // Track which arguments are by-ref

        /// <summary>
        /// P2a-2 Task 9 (Task-8 quality review I5) — HOW each by-ref argument is passed, for a
        /// call whose target is a resolved .NET member. Parallel to
        /// <see cref="ByRefArguments"/>, and consulted only where an entry exists.
        ///
        /// <para><b>Why <c>List&lt;bool&gt;</c> was not enough.</b> VB has one by-reference
        /// form and the CLR has three, so a <c>bool</c> cannot tell <c>ref</c> from <c>out</c>.
        /// <c>CSharpBackend</c> emitted <c>ref x</c> for a .NET <c>out</c> parameter, which is a
        /// raw <b>CS1620</b> — not a regression (both forms failed before Task 8 populated this
        /// list at all), but the descriptor now carries the fact, so the backend should not have
        /// to guess. <c>in</c>/<c>RefReadOnly</c> arguments need no modifier in C# at all.</para>
        ///
        /// <para>INTERNAL and <c>NetRefKind</c>-typed deliberately: this is .NET carriage, and
        /// carriage adds nothing to the compiler's public API (the same rule
        /// <see cref="ResolvedNetTarget"/> follows). A user function's ByRef argument records
        /// nothing here and keeps <c>ref</c>, which is VB's only by-reference form.</para>
        /// </summary>
        internal List<BasicLang.Net.NetRefKind> NetArgumentRefKinds { get; set; }
            = new List<BasicLang.Net.NetRefKind>();

        public bool IsTailCall { get; set; }

        /// <summary>
        /// When set, the call target is this delegate VALUE rather than a named
        /// function — e.g. invoking the delegate returned by another call:
        /// f(a)(b). Backends render (calleeValue)(args) and ignore FunctionName.
        /// </summary>
        public IRValue CalleeValue { get; set; }

        /// <summary>
        /// Explicit generic type arguments on the call — Method(Of T1, T2)() —
        /// rendered by backends as Method&lt;T1, T2&gt;(...).
        /// </summary>
        public List<TypeInfo> GenericArguments { get; set; } = new List<TypeInfo>();

        /// <summary>
        /// P2a-1 CARRIAGE — <b>read by nobody until P2a-2.</b> The .NET member this call was
        /// resolved to, or NULL when the call has no resolved .NET target (which is every call
        /// in every program that exists today: <see cref="IRBuilder"/> has no
        /// <c>NetTypeResolver</c>, so only P2a-2 ever writes a non-null value here).
        ///
        /// <para><b>Why the descriptor and not a Roslyn <c>ISymbol</c>.</b> An <c>ISymbol</c> is
        /// owned by the <c>Compilation</c> that produced it; putting one on an IR node would
        /// drag Roslyn into every IR consumer and tie node lifetime to a compilation that the
        /// optimizer has no reason to keep alive. <c>NetMemberDescriptor</c> is a detached
        /// value the optimizer carries opaquely.</para>
        ///
        /// <para><b>Why this exists before anything reads it.</b> P2a-2's lowering dispatches on
        /// this field; if any IR copy/clone path drops it the lowering silently falls back to
        /// name-based dispatch, which is the wild-pointer class spec §8.5 exists to prevent.
        /// Pinned by <c>NetIrCarriageTests</c>.</para>
        ///
        /// <para>INTERNAL on purpose: P2a-1 adds <b>nothing</b> to the compiler's public API.</para>
        /// </summary>
        internal BasicLang.Net.NetMemberDescriptor ResolvedNetTarget { get; set; }

        /// <summary>
        /// P2a-1 CARRIAGE — <b>read by nobody until P2a-2.</b> Spec C1 boundary category of this
        /// call's RECEIVER type, as <see cref="BoundaryTypeRegistry.Categorize"/> sees it.
        /// P2a-2 reads it to decide whether a call is natively handled
        /// ({<c>NativeOwned</c>, <c>Bridged</c>}) or must route through the .NET shim
        /// (<c>ManagedOwned</c>).
        ///
        /// <para><b>The initializer is load-bearing.</b> <c>NativeOwned</c> is 0, so the implicit
        /// enum default would mark every call in the program "natively handled" — the most
        /// dangerous possible wrong answer. It must start at <c>Unknown</c>.</para>
        /// </summary>
        internal BoundaryTypeCategory NetCategory { get; set; } = BoundaryTypeCategory.Unknown;

        /// <summary>
        /// P2a-2 Task 7a — TRUE only when <see cref="ResolvedNetTarget"/> is the OVERLOAD
        /// PROBE'S winner (or a member with no overload axis: a property/field access, a
        /// probed constructor), i.e. the descriptor's signature is known to be the one the
        /// call selects. A name-only record (first name match in metadata order) stays
        /// false, and the C++ lowering REFUSES it — lowering a name-matched descriptor
        /// could call the wrong overload, a silent miscompile (the plan's mandatory
        /// name-only gate). Defaults false: absent carriage is never trusted.
        /// </summary>
        internal bool ResolvedNetTargetIsExact { get; set; }

        // INetCarrying, explicitly — see the interface for why forwarding beats going public.
        BasicLang.Net.NetMemberDescriptor INetCarrying.ResolvedNetTarget => ResolvedNetTarget;
        BoundaryTypeCategory INetCarrying.NetCategory => NetCategory;
        bool INetCarrying.ResolvedNetTargetIsExact => ResolvedNetTargetIsExact;

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

        /// <summary>True for TryCast - backends emit a null-on-failure cast (C# 'as')</summary>
        public bool IsTryCast { get; set; }
        
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
    public class IRIndexerAccess : IRValue, INetCarrying
    {
        public IRValue Collection { get; set; }
        public List<IRValue> Indices { get; set; }

        /// <summary>
        /// P2a-2 Task 9 (spec §8.5) CARRIAGE — the .NET member this READ lowers to when the
        /// collection is a handle: an <c>ArrayGet</c> synthetic for a <c>T[]</c>, or the real
        /// <c>get_Item</c> indexer property for a collection that declares one — "which the
        /// collector must collect even though the source never names it".
        /// Null for every native collection, which is every program that existed before this.
        /// </summary>
        internal BasicLang.Net.NetMemberDescriptor ResolvedNetTarget { get; set; }

        internal BoundaryTypeCategory NetCategory { get; set; }

        internal bool ResolvedNetTargetIsExact { get; set; }

        BasicLang.Net.NetMemberDescriptor INetCarrying.ResolvedNetTarget => ResolvedNetTarget;
        BoundaryTypeCategory INetCarrying.NetCategory => NetCategory;
        bool INetCarrying.ResolvedNetTargetIsExact => ResolvedNetTargetIsExact;

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
    /// IR Indexer store - represents `collection[index] = value` / `dictionary[key] = value`.
    /// Distinct from <see cref="IRArrayStore"/> (raw fixed arrays) so backends can lower the
    /// write faithfully per collection kind (e.g. C++ Dictionary -> .Set(k,v), List -> [i]=v).
    /// </summary>
    public class IRIndexerStore : IRInstruction, INetCarrying
    {
        public IRValue Collection { get; set; }
        public List<IRValue> Indices { get; set; }
        public IRValue Value { get; set; }

        /// <summary>
        /// P2a-2 Task 9 (spec §8.5) CARRIAGE — the synthesized <c>set_Item(index…, value)</c>
        /// accessor this WRITE lowers to when the collection is a handle
        /// (<c>NetAccessorSynthesis.ArraySetFor</c> for a <c>T[]</c>,
        /// <c>NetAccessorSynthesis.SetterFor</c> over the resolved indexer otherwise). Null for
        /// every native collection.
        /// </summary>
        internal BasicLang.Net.NetMemberDescriptor ResolvedNetTarget { get; set; }

        internal BoundaryTypeCategory NetCategory { get; set; }

        internal bool ResolvedNetTargetIsExact { get; set; }

        BasicLang.Net.NetMemberDescriptor INetCarrying.ResolvedNetTarget => ResolvedNetTarget;
        BoundaryTypeCategory INetCarrying.NetCategory => NetCategory;
        bool INetCarrying.ResolvedNetTargetIsExact => ResolvedNetTargetIsExact;

        public IRIndexerStore(IRValue collection, IRValue value)
        {
            Collection = collection;
            Value = value;
            Indices = new List<IRValue>();
        }

        public override void Accept(IIRVisitor visitor) => visitor.Visit(this);

        public override string ToString()
        {
            var indices = string.Join(", ", Indices.Select(i => i?.Name ?? "?"));
            return $"{Collection?.Name}[{indices}] = {Value?.Name}";
        }
    }

    /// <summary>
    /// P2a-2 Task 9 (spec §8.5) — the four .NET members a <c>For Each</c> over a
    /// HANDLE-represented collection is driven by, obtained and driven <b>through
    /// <c>IEnumerable&lt;T&gt;</c>/<c>IEnumerator&lt;T&gt;</c></b>.
    ///
    /// <para><b>⛔ NEVER the concrete struct-returning <c>GetEnumerator()</c> Roslyn would
    /// otherwise select.</b> For <c>List&lt;T&gt;</c>, <c>Dictionary&lt;K,V&gt;</c>,
    /// <c>HashSet&lt;T&gt;</c> and <c>ImmutableArray&lt;T&gt;</c> that enumerator is a MUTABLE
    /// STRUCT. Boxed into a handle (§8.3), a generated
    /// <c>((List&lt;int&gt;.Enumerator)o!).MoveNext()</c> mutates the TEMPORARY produced by the
    /// unboxing conversion; the box is untouched, <c>MoveNext</c> returns true forever and
    /// <c>Current</c> yields element 0 — an INFINITE LOOP, not a diagnostic. Interface dispatch
    /// on a boxed value type operates on the box, which is what makes this route correct rather
    /// than merely conservative.</para>
    ///
    /// <para>Carried as a bundle rather than through <c>INetCarrying</c> (which holds ONE
    /// descriptor) because all four must reach the surface together: a loop whose
    /// <c>MoveNext</c> export exists and whose <c>Current</c> export does not is a shim that
    /// fails to link, not a degraded loop.</para>
    /// </summary>
    internal sealed class IRNetEnumeration
    {
        internal IRNetEnumeration(
            BasicLang.Net.NetMemberDescriptor getEnumerator,
            BasicLang.Net.NetMemberDescriptor moveNext,
            BasicLang.Net.NetMemberDescriptor current,
            BasicLang.Net.NetMemberDescriptor dispose)
        {
            GetEnumerator = getEnumerator;
            MoveNext = moveNext;
            Current = current;
            Dispose = dispose;
        }

        internal BasicLang.Net.NetMemberDescriptor GetEnumerator { get; }
        internal BasicLang.Net.NetMemberDescriptor MoveNext { get; }
        internal BasicLang.Net.NetMemberDescriptor Current { get; }
        internal BasicLang.Net.NetMemberDescriptor Dispose { get; }

        internal IEnumerable<BasicLang.Net.NetMemberDescriptor> Members
        {
            get
            {
                yield return GetEnumerator;
                yield return MoveNext;
                yield return Current;
                yield return Dispose;
            }
        }
    }

    /// <summary>
    /// IR ForEach loop - represents iteration over a collection.
    /// </summary>
    public class IRForEach : IRInstruction
    {
        public string VariableName { get; set; }
        public TypeInfo ElementType { get; set; }
        public IRValue Collection { get; set; }
        public BasicBlock BodyBlock { get; set; }
        public BasicBlock EndBlock { get; set; }

        /// <summary>
        /// P2a-2 Task 9 CARRIAGE — non-null when <see cref="Collection"/> is a
        /// handle-represented .NET collection, holding the four interface members the loop is
        /// driven by. See <see cref="IRNetEnumeration"/> for why the interfaces are mandatory.
        /// Null for every native collection, which is every program that existed before this.
        /// </summary>
        internal IRNetEnumeration NetEnumeration { get; set; }

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
    /// Throw statement: throw Exception, or a bare rethrow when Exception is null.
    /// </summary>
    public class IRThrow : IRInstruction
    {
        public IRValue Exception { get; set; }

        public IRThrow(IRValue exception)
        {
            Exception = exception;
        }

        public override void Accept(IIRVisitor visitor) => visitor.Visit(this);
        public override string ToString() => Exception == null ? "rethrow" : $"throw {Exception.Name}";
    }

    /// <summary>
    /// Represents a catch clause in a try-catch block
    /// </summary>
    public class IRCatchClause
    {
        public TypeInfo ExceptionType { get; set; }
        public string VariableName { get; set; }
        public BasicBlock Block { get; set; }

        /// <summary>
        /// P2a-2 Task 4 (spec §11.1's ladder-trigger completion): the fully-qualified .NET name
        /// the analyzer's resolver answered for this clause's exception type, when that type is
        /// OUTSIDE <c>CppExceptionTypes</c>' 12-name set (e.g.
        /// <c>System.IO.FileNotFoundException</c>). Null for the 12 known names (the generator
        /// maps those itself), for user-defined exception types, and for every compilation
        /// without a resolver factory. The C++ backend's NetException ladder emits a
        /// <c>Matches("&lt;this&gt;")</c> arm for it — without the carriage the clause silently
        /// binds to a later <c>Exception</c> clause.
        /// </summary>
        public string NetExceptionFullName { get; set; }

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
        /// Absolute path to the source .bas file this function was compiled from
        /// </summary>
        public string SourceFilePath { get; set; }

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

        /// <summary>
        /// C++ headers from #CppInclude directives, as fully delimited tokens
        /// (e.g. "&lt;mutex&gt;" or "\"grid.h\""). Emitted verbatim as #include lines
        /// by the C++ backend. Ignored by all other backends.
        /// </summary>
        public List<string> CppIncludes { get; set; }

        /// <summary>
        /// <c>#JsImport</c> directives in source order, emitted as real ES <c>import</c>
        /// statements by the JavaScript backend. Sibling of <see cref="CppIncludes"/>: same
        /// role, different target language.
        ///
        /// <para>A RECORD rather than a bare specifier string, because an import has two
        /// independent parts — WHAT to load and WHAT NAMES it brings in — and only the first is
        /// a file path. <see cref="JavaScriptEmitter"/> copies by
        /// <see cref="JsImportDirective.Specifier"/> while the backend emits by
        /// <see cref="JsImportDirective.Clause"/>; a single string could serve one or the other,
        /// never both.</para>
        /// </summary>
        public List<JsImportDirective> JsImports { get; set; }

        /// <summary>
        /// Source path → the number of lines the front end INSERTED above that file's original
        /// content, keyed the way <c>IRFunction.SourceFilePath</c> spells it.
        ///
        /// <para><b>Why this had to leave CompilationUnit.</b> A <c>.mod</c> is wrapped in an
        /// implicit <c>Module</c> block and a <c>.cls</c> in an implicit <c>Class</c> block
        /// before parsing, so every <c>SourceLine</c> in the IR for those files is one greater
        /// than the line the user sees. The correction has always lived on
        /// <c>CompilationUnit.LineOffset</c>, which no backend can reach — fine while only
        /// diagnostics needed it, wrong once a source map has to point a debugger at a real
        /// line. Ignoring it yields a map that is correct for <c>.bas</c> and off by one for
        /// everything else, which the obvious test never catches.</para>
        ///
        /// <para>⚠ The offset is NOT derivable from the extension: an <c>Option Public</c>
        /// <c>.cls</c> substitutes the directive line in place and has offset 0, while every
        /// other <c>.cls</c> has 1.</para>
        /// </summary>
        public Dictionary<string, int> SourceLineOffsets { get; set; }

        public IRModule(string name)
        {
            Name = name;
            SourceLineOffsets = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
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
            CppIncludes = new List<string>();
            JsImports = new List<JsImportDirective>();
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
            AddGlobalVariable(variable);
            return variable;
        }

        /// <summary>
        /// Registers a module-scope variable, qualifying the DICTIONARY KEY when the bare name
        /// is already taken.
        ///
        /// <para><b>Why the key must not simply be the name.</b> Separate <c>Module</c> blocks
        /// are separate namespaces, and each may legitimately declare its own <c>Scale</c>.
        /// This dictionary is shared by every Module in the combined IR, so a bare key silently
        /// DROPPED one of them — and the emitted code still referenced it, leaving an
        /// identifier declared nowhere from a build that reported success.</para>
        ///
        /// <para><b>Why only on collision.</b> Nearly every consumer iterates <c>.Values</c>
        /// (and the C# backend groups those by <c>ModuleName</c> into per-module static
        /// classes, so bare references resolve by ordinary lexical scoping once both survive).
        /// Exactly one consumer looks a global up by bare name —
        /// <c>CppCodeGenerator</c>'s delegate-invocation path — so keeping the key bare in the
        /// common case preserves it, and only genuine collisions pay the qualified form.</para>
        /// </summary>
        public void AddGlobalVariable(IRVariable variable)
        {
            if (variable == null) return;

            var key = variable.Name;
            if (GlobalVariables.ContainsKey(key))
                key = $"{variable.ModuleName ?? Name}.{variable.Name}";

            GlobalVariables[key] = variable;
        }

        /// <summary>
        /// Every <see cref="IRFunction"/> in <see cref="Functions"/> that is really a class or
        /// interface MEMBER body, identified by reference.
        ///
        /// <para>Class members flatten into <c>Functions</c> under their UNQUALIFIED name —
        /// <c>Class A.Handle</c> and <c>Class B.Handle</c> are both just "Handle" — while the
        /// owning member keeps a pointer to the same object. Two consumers need to tell them
        /// apart and must not each invent their own rule:</para>
        /// <list type="bullet">
        /// <item><description>Combining units must NOT dedupe them by name, or one class's
        /// method is discarded outright.</description></item>
        /// <item><description>A backend that walks <c>Functions</c> must NOT emit them as free
        /// functions, or two same-named definitions collide and the later silently wins.</description></item>
        /// </list>
        ///
        /// <para>Identity, never name: the names are exactly what is ambiguous.</para>
        /// </summary>
        public HashSet<IRFunction> CollectMemberImplementations()
        {
            var members = new HashSet<IRFunction>();

            foreach (var cls in Classes.Values)
            {
                foreach (var m in cls.Methods)
                    if (m?.Implementation != null) members.Add(m.Implementation);
                foreach (var c in cls.Constructors)
                    if (c?.Implementation != null) members.Add(c.Implementation);
                foreach (var p in cls.Properties)
                {
                    if (p?.Getter != null) members.Add(p.Getter);
                    if (p?.Setter != null) members.Add(p.Setter);
                }
            }

            foreach (var iface in Interfaces.Values)
                foreach (var m in iface.Methods)
                    if (m?.DefaultImplementation != null) members.Add(m.DefaultImplementation);

            return members;
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
        /// <summary>True for BasicLang Structure declarations: value semantics on all backends.</summary>
        public bool IsStruct { get; set; }

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
    public class IRNewObject : IRValue, INetCarrying
    {
        public string ClassName { get; set; }
        public List<IRValue> Arguments { get; set; }

        /// <summary>
        /// P2a-2 Task 7a CARRIAGE — the .NET CONSTRUCTOR this construction resolved to, or
        /// null for every non-.NET construction (user classes, collections, P1 BCL values —
        /// i.e. every construction in every pre-P2a program). Written by <see cref="IRBuilder"/>
        /// from the analyzer's constructor-probe annotation; read by the surface collector and
        /// the C++ call lowering. See <see cref="IRCall.ResolvedNetTarget"/> for why this is a
        /// detached descriptor and why an optimizer clone path dropping it is the §8.5
        /// wild-pointer class.
        /// </summary>
        internal BasicLang.Net.NetMemberDescriptor ResolvedNetTarget { get; set; }

        /// <summary>
        /// P2a-2 Task 7a CARRIAGE — spec C1 category of the constructed type. Written only
        /// alongside <see cref="ResolvedNetTarget"/>. <b>The initializer is load-bearing</b>
        /// (<c>NativeOwned</c> is 0 — the P2a-1 trap): it must start at <c>Unknown</c>.
        /// </summary>
        internal BoundaryTypeCategory NetCategory { get; set; } = BoundaryTypeCategory.Unknown;

        /// <summary>See <see cref="IRCall.ResolvedNetTargetIsExact"/> — the name-only gate.</summary>
        internal bool ResolvedNetTargetIsExact { get; set; }

        // INetCarrying, explicitly — see the interface for why forwarding beats going public.
        BasicLang.Net.NetMemberDescriptor INetCarrying.ResolvedNetTarget => ResolvedNetTarget;
        BoundaryTypeCategory INetCarrying.NetCategory => NetCategory;
        bool INetCarrying.ResolvedNetTargetIsExact => ResolvedNetTargetIsExact;

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
    public class IRInstanceMethodCall : IRValue, INetCarrying
    {
        public IRValue Object { get; set; }
        public string MethodName { get; set; }
        public List<IRValue> Arguments { get; set; }
        public bool IsVirtual { get; set; }

        /// <summary>
        /// Which arguments the callee takes BY REFERENCE — the instance-call twin of
        /// <see cref="IRCall.ByRefArguments"/>, indexed in lockstep with
        /// <see cref="Arguments"/> and consulted only where an entry exists.
        ///
        /// <para>Its absence was a real defect, not a gap in coverage: an IRCall records the
        /// fact and an instance call did not, so <c>obj.Method(ByRef x)</c> reached the C#
        /// backend with no <c>ref</c> at the call site (CS1620, a hard build failure) and
        /// reached the optimizer looking side-effect-free, which let a copy fact for <c>x</c>
        /// survive a call that writes it.</para>
        /// </summary>
        public List<bool> ByRefArguments { get; set; }

        /// <summary>Explicit generic type arguments: obj.Method(Of T)() -> obj.Method&lt;T&gt;().</summary>
        public List<TypeInfo> GenericArguments { get; set; } = new List<TypeInfo>();

        /// <summary>
        /// P2a-2 CARRIAGE (Task 2) — the .NET member the analyzer resolved this instance call to,
        /// or NULL when the call has no resolved .NET target (which is every call on a
        /// user-defined receiver, every claimed-name receiver, and every compilation without a
        /// <c>NetResolverFactory</c> — i.e. every C#-backend build and the LSP today). Written by
        /// <see cref="IRBuilder"/> from the analyzer's <c>NetAstAnnotations</c> side table; read
        /// by nobody until the surface collector / call lowering (P2a-2 Tasks 3/7a).
        ///
        /// <para><b>Why the descriptor and not a Roslyn <c>ISymbol</c>.</b> Same reason as
        /// <see cref="IRCall.ResolvedNetTarget"/>: an <c>ISymbol</c> is owned by the
        /// <c>Compilation</c> that produced it; <c>NetMemberDescriptor</c> is a detached value the
        /// optimizer carries opaquely.</para>
        ///
        /// <para><b>Why this exists before anything reads it.</b> P2a-2's lowering dispatches on
        /// this field; if any IR copy/clone path drops it the lowering silently falls back to
        /// name-based dispatch, which is the wild-pointer class spec §8.5 exists to prevent.
        /// Pinned by <c>NetIrCarriageTests</c>.</para>
        ///
        /// <para>INTERNAL on purpose: carriage adds <b>nothing</b> to the compiler's public API.</para>
        /// </summary>
        internal BasicLang.Net.NetMemberDescriptor ResolvedNetTarget { get; set; }

        /// <summary>
        /// P2a-2 CARRIAGE (Task 2) — spec C1 boundary category of this call's RECEIVER type, as
        /// <see cref="BoundaryTypeRegistry.Categorize"/> sees it. Written only alongside
        /// <see cref="ResolvedNetTarget"/> (a call with no resolved target keeps the default);
        /// P2a-2 reads it to decide whether a call is natively handled
        /// ({<c>NativeOwned</c>, <c>Bridged</c>}) or must route through the .NET shim
        /// (<c>ManagedOwned</c>).
        ///
        /// <para><b>The initializer is load-bearing.</b> <c>NativeOwned</c> is 0, so the implicit
        /// enum default would mark every instance call in the program "natively handled" — the
        /// most dangerous possible wrong answer. It must start at <c>Unknown</c>.</para>
        /// </summary>
        internal BoundaryTypeCategory NetCategory { get; set; } = BoundaryTypeCategory.Unknown;

        /// <summary>See <see cref="IRCall.ResolvedNetTargetIsExact"/> — the name-only gate.</summary>
        internal bool ResolvedNetTargetIsExact { get; set; }

        // INetCarrying, explicitly — see the interface for why forwarding beats going public.
        BasicLang.Net.NetMemberDescriptor INetCarrying.ResolvedNetTarget => ResolvedNetTarget;
        BoundaryTypeCategory INetCarrying.NetCategory => NetCategory;
        bool INetCarrying.ResolvedNetTargetIsExact => ResolvedNetTargetIsExact;

        public IRInstanceMethodCall(string resultName, IRValue obj, string methodName, TypeInfo returnType)
            : base(resultName, returnType)
        {
            Object = obj;
            MethodName = methodName;
            Arguments = new List<IRValue>();
            ByRefArguments = new List<bool>();
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

        // NO .NET CARRIAGE HERE, DELIBERATELY (P2a-2 Task 7a removed the Task-2 fields).
        //
        // A base call exists for exactly one source shape — `MyBase.Method(args)`
        // (IRBuilder.cs's MyBaseExpressionNode arm, the only `new IRBaseMethodCall` site) — so
        // its receiver is the ENCLOSING CLASS'S BASE. Since the flip, a native `Inherits` of a
        // .NET class stays checker-rejected (CppCapabilityChecker's unresolved-base gate), so a
        // .NET base call is unreachable BY CONSTRUCTION, and IRBuilder accordingly has no stamp
        // site for one.
        //
        // Keeping unstamped fields was the actual hazard: the surface collector had an arm for
        // this node type while the C++ lowering had none, so a future stamp would have put the
        // member in the SURFACE (costing a proxy slot and a shim export) while the call itself
        // fell through to the legacy `Base::Method(...)` emission with NO exactness gate — a
        // silent miscompile behind a passing slots-≡-exports invariant. If .NET base types ever
        // become legal, add the fields back TOGETHER WITH a lowering arm that calls
        // RequireExactNetTarget.

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
    public class IRFieldAccess : IRValue, INetCarrying
    {
        public IRValue Object { get; set; }
        public string FieldName { get; set; }

        /// <summary>
        /// P2a-2 Task 7a CARRIAGE — the .NET PROPERTY or FIELD this member READ resolved to
        /// (the descriptor is the getter-shaped proxy slot), or null for every non-.NET read.
        /// Written by <see cref="IRBuilder"/> from the member-probe annotation; read by the
        /// surface collector and the C++ call lowering. See
        /// <see cref="IRCall.ResolvedNetTarget"/> for the detached-descriptor rationale.
        /// </summary>
        internal BasicLang.Net.NetMemberDescriptor ResolvedNetTarget { get; set; }

        /// <summary>
        /// P2a-2 Task 7a CARRIAGE — spec C1 category of the receiver type. Written only
        /// alongside <see cref="ResolvedNetTarget"/>. <b>The initializer is load-bearing</b>
        /// (<c>NativeOwned</c> is 0 — the P2a-1 trap): it must start at <c>Unknown</c>.
        /// </summary>
        internal BoundaryTypeCategory NetCategory { get; set; } = BoundaryTypeCategory.Unknown;

        /// <summary>See <see cref="IRCall.ResolvedNetTargetIsExact"/> — the name-only gate.</summary>
        internal bool ResolvedNetTargetIsExact { get; set; }

        // INetCarrying, explicitly — see the interface for why forwarding beats going public.
        BasicLang.Net.NetMemberDescriptor INetCarrying.ResolvedNetTarget => ResolvedNetTarget;
        BoundaryTypeCategory INetCarrying.NetCategory => NetCategory;
        bool INetCarrying.ResolvedNetTargetIsExact => ResolvedNetTargetIsExact;

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
    public class IRFieldStore : IRInstruction, INetCarrying
    {
        public IRValue Object { get; set; }
        public string FieldName { get; set; }
        public IRValue Value { get; set; }

        /// <summary>
        /// P2a-2 Task 7a CARRIAGE — the SYNTHESIZED accessor-method descriptor
        /// (<c>set_X</c>: void return, one value parameter of the property/field type) for a
        /// .NET member WRITE, or null for every non-.NET store. Synthesized in exactly ONE
        /// place — <see cref="IRBuilder"/>'s store stamping — so the surface collector and
        /// the C++ lowering mangle the identical descriptor and §12.4's slots-≡-exports
        /// invariant holds by construction. See <see cref="IRCall.ResolvedNetTarget"/> for
        /// the detached-descriptor rationale.
        /// </summary>
        internal BasicLang.Net.NetMemberDescriptor ResolvedNetTarget { get; set; }

        /// <summary>
        /// P2a-2 Task 7a CARRIAGE — spec C1 category of the receiver type. Written only
        /// alongside <see cref="ResolvedNetTarget"/>. <b>The initializer is load-bearing</b>
        /// (<c>NativeOwned</c> is 0 — the P2a-1 trap): it must start at <c>Unknown</c>.
        /// </summary>
        internal BoundaryTypeCategory NetCategory { get; set; } = BoundaryTypeCategory.Unknown;

        /// <summary>See <see cref="IRCall.ResolvedNetTargetIsExact"/> — the name-only gate.</summary>
        internal bool ResolvedNetTargetIsExact { get; set; }

        // INetCarrying, explicitly — see the interface for why forwarding beats going public.
        BasicLang.Net.NetMemberDescriptor INetCarrying.ResolvedNetTarget => ResolvedNetTarget;
        BoundaryTypeCategory INetCarrying.NetCategory => NetCategory;
        bool INetCarrying.ResolvedNetTargetIsExact => ResolvedNetTargetIsExact;

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
