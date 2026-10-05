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

        /// <summary>
        /// ADR-0016 D3 (amended): this instruction is a local's <c>Dim</c> initializer — the
        /// assignment, renamed value, array-slot store or tuple element IRBuilder emits for
        /// <c>Dim v = …</c>. It DECLARES a variable (a fresh one in VB, however the flat IR spells
        /// it) rather than writing an existing one. Read only by the C++ backend's
        /// captured-variable rule; no pass reads or preserves it on an instruction it creates.
        /// </summary>
        public bool IsDimInitializer { get; set; }

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

        // ⛔ THROWS by default, deliberately (ADR-0010 D1). IRDelegateCreate exists only in the
        // output of ClosureLowering, which only the MSIL and C++ backends run, on a CLONE of the
        // module. A C#, JavaScript or LLVM visitor reaching one means the lowered form leaked into a
        // backend that must never see it — a silent no-op here would drop the delegate value.
        void Visit(IRDelegateCreate delegateCreate) =>
            throw new InvalidOperationException(
                $"{GetType().Name} reached an IRDelegateCreate. That node is produced only by "
                + "ClosureLowering, which only the MSIL backend runs, on a clone of the module "
                + "(ADR-0010 D1); every other backend lowers lambdas itself and must never see it.");

        // ⛔ THROWS by default, deliberately (ADR-0016 D1). The base-constructor call is an
        // instruction every backend must place itself — after its own argument evaluation, before
        // the body. A visitor that silently skipped it would construct an object whose base never
        // ran, from a green build.
        void Visit(IRBaseConstructorCall baseConstructorCall) =>
            throw new InvalidOperationException(
                $"{GetType().Name} has no lowering for IRBaseConstructorCall (MyBase.New, ADR-0016). "
                + "Every backend must emit the base call where the IR places it.");

        // ⛔ THROWS by default, deliberately (ADR-0011 D5). `Is` / `IsNot` must never degrade to a
        // value comparison or to nothing at all: a visitor that has not implemented reference
        // identity fails LOUDLY here. CodeGeneratorBase makes it abstract, so the C++, MSIL and
        // LLVM backends cannot compile without an implementation.
        void Visit(IRIdentityCompare identityCompare) =>
            throw new InvalidOperationException(
                $"{GetType().Name} has no lowering for IRIdentityCompare (`Is` / `IsNot`, "
                + "ADR-0011). Reference identity must be implemented explicitly — never by "
                + "falling back to a value comparison.");
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

        /// <summary>
        /// True when IRBuilder RENAMED this result after the variable it initialises or assigns
        /// (<c>x = a + b</c> is one IRBinaryOp named <c>x</c>; no IRAssignment follows). This is
        /// how a backend tells "a result that IS an assignment to <c>x</c>" from "a compiler temp
        /// that merely shares <c>x</c>'s name" — a class with a field called <c>t0</c> used to have
        /// every SSA temp <c>t0</c> emitted as a write to that field on C#.
        /// </summary>
        public bool NamedAfterVariable { get; set; }

        /// <summary>
        /// ⭐ True when this value is a COMPILER TEMP (ADR-0017): IRBuilder built it under a name
        /// <see cref="IRFunction.GetNextTempName"/> MINTED, and it is not storage the program
        /// declared — never an <see cref="IRVariable"/> or <see cref="IRConstant"/>, never a value
        /// <see cref="NamedAfterVariable"/>. It is the only licence
        /// <c>DeadCodeEliminationPass</c> has to delete an unused value instruction.
        ///
        /// <para>⛔ A fact about where the NAME came from, never about how it is SPELLED. A program
        /// may call its own variable <c>t5</c>, <c>T5</c>, <c>_tmp1</c> or <c>_t3</c>. MEASURED
        /// (#118) with the guard on spelling (<c>IsTempDestination</c>): 37 of 62 removals were
        /// user variables, and <c>Dim t5 As Integer = a + b</c> printed 12 for 82 on all four
        /// backends and all three entry points.</para>
        ///
        /// <para><b>Who writes it.</b> Set once, by <c>IRBuilder.MarkCompilerTemps</c>, after the
        /// builder's last rename. An optimizer pass that REPLACES a value carries it to the
        /// replacement with the rest of the value's identity (<c>OptimizationPass.InheritIdentity</c>);
        /// a value built without it reads false, which only costs a removal. No REGISTERED optimizer
        /// pass mints a temp name today; one that does mints through
        /// <see cref="IRFunction.DeclareTemp"/>, which sets this on the variable it declares
        /// (ADR-0018 D2). The minted name is never one the program owns, and the verifier refuses a
        /// value carrying this flag under a reserved name (ADR-0018 D4, Invariant T).</para>
        /// </summary>
        public bool IsCompilerTemp { get; set; }

        protected IRValue(string name, TypeInfo type) : base(type)
        {
            Name = name;
        }
    }
    
    /// <summary>
    /// Constant value.
    ///
    /// <para>A null <see cref="Value"/> is VB's <c>Nothing</c> of the constant's type. For a
    /// reference type that is a null reference. For a VALUE type
    /// (<c>TypeInfo.NothingIsDefaultValue</c>: a Structure, an Enum, a type parameter…) it is the
    /// type's DEFAULT value (#186, <c>IRBuilder.NothingAs</c>). A Nothing converted to a primitive
    /// gets its zero literal instead.</para>
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
        /// #144 — VB's COPY-IN TEMPORARY: a local IRBuilder declares when a constructor call
        /// (<c>New C(…)</c> or <c>MyBase.New(…)</c>) passes a value with no storage of its own — a
        /// literal, an expression, a call result — to a ByRef parameter. VB evaluates such an
        /// argument into a temporary, passes the temporary by reference and discards the write
        /// back. The carrier IS ordinary storage (C# <c>ref</c>, MSIL <c>ldloca</c>, a C++ lvalue);
        /// the flag is for a backend that has no references at all: JavaScript passes it by value,
        /// which is exactly VB's semantics for it, instead of refusing it as BL7002.
        /// </summary>
        public bool IsByRefCopyIn { get; set; }

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

        // Logical. VB has FOUR keywords where C# has two, and the difference is REAL:
        //   And / Or         evaluate BOTH operands ALWAYS   (C# `&` / `|`)
        //   AndAlso / OrElse skip the right operand           (C# `&&` / `||`)
        // The result VALUE is identical for both pairs; only the right operand's SIDE
        // EFFECTS differ, which is the entire observable content of the distinction.
        //
        // ⛔ These were ONE pair until 2026-08-07, and this comment used to label And/Or
        // "short-circuit", which was simply wrong. IRBuilder collapsed andalso->And and
        // orelse->Or, so the distinction died at the IR boundary and NO backend could be
        // correct for both spellings — each got a different half wrong. Measured: the VB
        // guard idiom `If i <> 0 AndAlso Risky(i)` called Risky anyway on the native
        // backend and died with STATUS_INTEGER_DIVIDE_BY_ZERO.
        And, Or,
        AndAlso, OrElse,

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

        // ================================================================================
        // ⭐ VB's STRING EQUALITY (#206). THE ONE RULE every backend and the optimizer's fold
        // read for `=` / `<>` on Strings, and for a Select Case over a String.
        // ================================================================================

        /// <summary>
        /// True when <c>left = right</c> is VB's String equality: both operands are Strings, or one
        /// is a String and the other the <c>Nothing</c> literal. VB answers it with
        /// <c>Operators.CompareString(left, right, TextCompare:=False)</c>: an ORDINAL comparison in
        /// which <c>Nothing</c> is <c>""</c>. So <c>Nothing = ""</c> is True, and an unassigned
        /// String equals <c>""</c>.
        ///
        /// <para>⛔ Not <c>Is</c> / <c>IsNot</c>. Those are <see cref="IRIdentityCompare"/>,
        /// reference identity (ADR-0011): <c>s Is Nothing</c> stays False for <c>""</c>. An
        /// <c>Object</c> operand is not this either; it is the late-bound comparison (ADR-0012).</para>
        /// </summary>
        public static bool IsStringEquality(IRValue left, IRValue right) =>
            (IsScalarString(left) && (IsScalarString(right) || IRIdentityCompare.IsNothing(right)))
            || (IsScalarString(right) && IRIdentityCompare.IsNothing(left));

        /// <summary>
        /// Under <see cref="IsStringEquality"/>, whether <paramref name="operand"/> has to read
        /// <c>Nothing</c> as <c>""</c> at run time: it is not a string literal (a literal is never
        /// Nothing), and <paramref name="other"/> could be <c>""</c>. Against a NON-EMPTY string
        /// literal, Nothing and <c>""</c> are both unequal, so the operand is left as it is.
        /// A backend that answers True writes the <c>Nothing</c> literal as <c>""</c> and any other
        /// operand as "the value, or <c>""</c> when it is Nothing".
        /// </summary>
        public static bool ReadsNothingAsEmpty(IRValue operand, IRValue other) =>
            IsStringEquality(operand, other)
            && operand is not IRConstant { Value: string }
            && other is not IRConstant { Value: string { Length: > 0 } };

        /// <summary>A scalar String operand (not an array of them).</summary>
        private static bool IsScalarString(IRValue value) =>
            value?.Type is { } type && type.Kind != TypeKind.Array && type.ArrayRank == 0
            && (string.Equals(type.Name, "String", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type.Name, "System.String", StringComparison.OrdinalIgnoreCase));
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
    
    /// <summary>
    /// Reference identity: <c>result = Left Is Right</c>, or <c>Left IsNot Right</c> when
    /// <see cref="Negated"/> (task #185, ADR-0011 D5). <c>x Is Nothing</c> is this node with the
    /// <c>Nothing</c> literal (an <see cref="IRConstant"/> whose value is null) as an operand.
    ///
    /// <para>⛔ DELIBERATELY NOT a <see cref="BinaryOpKind"/> or <see cref="CompareKind"/>.
    /// <c>Eq</c>/<c>Ne</c> are VALUE comparisons: a user <c>Operator =</c>,
    /// <c>Delegate.op_Equality</c> or String value equality may answer them, and every existing
    /// <c>default:</c> arm over those enums would have rendered a new member as <c>==</c> —
    /// silently. A new node reaches no backend that has not implemented it
    /// (<see cref="IIRVisitor.Visit(IRIdentityCompare)"/> throws by default). The optimizer never
    /// rewrites this node to or from an <c>Eq</c>/<c>Ne</c>, and folds it only when BOTH operands
    /// are <c>Nothing</c>.</para>
    ///
    /// <para>Kill vocabulary (ADR-0006): PURE — a read of both operands and a definition of its
    /// own name, nothing else. No backend's rendering of it can run user code.</para>
    ///
    /// <para>⚠ The C++ backend has no null state for a String (#173 writes Nothing as the EMPTY
    /// value), so there a Nothing test on a String is an EMPTINESS test; an array has a real null
    /// state (#196). One helper, <c>CppCodeGenerator.EmitNullTest</c>, shared with
    /// <see cref="IRNothingPatternCase"/> (ADR-0011 D3).</para>
    /// </summary>
    public class IRIdentityCompare : IRValue
    {
        public IRValue Left { get; set; }
        public IRValue Right { get; set; }

        /// <summary>True for <c>IsNot</c>.</summary>
        public bool Negated { get; set; }

        public IRIdentityCompare(string resultName, IRValue left, IRValue right, bool negated, TypeInfo type)
            : base(resultName, type)
        {
            Left = left;
            Right = right;
            Negated = negated;
        }

        /// <summary>
        /// Whether <paramref name="value"/> is <c>Nothing</c>: a null constant, whatever type a
        /// store site or the optimizer gave it (the literal itself is typed Object; a propagated
        /// one keeps the type of the variable it was stored into).
        /// </summary>
        public static bool IsNothing(IRValue value) => value is IRConstant { Value: null };

        /// <summary>
        /// The operand a Nothing test is ABOUT — the one that is not <c>Nothing</c> — or null when
        /// neither operand is <c>Nothing</c> (a two-operand identity) or both are.
        /// <para>⚠ A METHOD, not a property: <c>OperandWalkerTotalityTests</c> treats every public
        /// <see cref="IRValue"/>-typed property as an operand SLOT that every walker must rewrite,
        /// and this is a view of <see cref="Left"/>/<see cref="Right"/>, not a third slot.</para>
        /// </summary>
        public IRValue GetNullTestSubject() =>
            IsNothing(Right) && !IsNothing(Left) ? Left
            : IsNothing(Left) && !IsNothing(Right) ? Right
            : null;

        public override void Accept(IIRVisitor visitor) => visitor.Visit(this);

        public override string ToString() =>
            $"{Name} = {(Negated ? "isnot" : "is")} {Operand(Left)}, {Operand(Right)}";

        // A null IRConstant prints as an empty string; spell it.
        private static string Operand(IRValue value) => IsNothing(value) ? "nothing" : value?.ToString();
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

        /// <summary>
        /// True when this branch is an explicit <c>Exit For/Do/While</c> — a deliberate escape
        /// from the loop — rather than the branch that ends an ordinary iteration.
        ///
        /// <para><b>Both are <c>IRBranch(loop.BreakTarget)</c> and are otherwise identical</b>,
        /// which is a real problem for a backend: C++ needs <c>break;</c> for the first and
        /// <c>continue;</c> for the second, and JavaScript needs <c>break</c> vs fall-through.
        /// Without this flag each backend has to guess the difference back from block position,
        /// and the obvious guess is WRONG — an <c>If</c> inside the body produces a merge block
        /// that also branches to the loop's end block and must NOT become a break. That guess
        /// shipped as a silent miscompile: <c>Exit For</c> inside a <c>For Each</c> behaved as
        /// <c>Continue For</c> on the C++ backend (chip task_4cc381f1).</para>
        ///
        /// <para>Set by <c>IRBuilder.Visit(ExitStatementNode)</c>, which is the only place that
        /// knows the user wrote <c>Exit</c>. Consumers that do not care may ignore it: a branch
        /// is still a branch to the same target.</para>
        /// </summary>
        public bool IsLoopExit { get; set; }

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

        /// <summary>
        /// True for <c>Case Is Nothing</c>, the reference-IDENTITY test (ADR-0011 D2 (2)). False
        /// for <c>Case Nothing</c>, VB's VALUE comparison <c>subject = Nothing</c>. Carried from
        /// <c>NothingPatternNode.WrittenWithIs</c>.
        ///
        /// <para>The two spellings answer differently only when the subject is an Object holding a
        /// value that equals its type's default: an Object holding 0 is <c>= Nothing</c> but is not
        /// <c>Is Nothing</c>. The MSIL backend reads this flag to give an Object subject VB's
        /// late-bound comparison for <c>Case Nothing</c> and keep the null test for
        /// <c>Case Is Nothing</c> (task #177). On a String subject the two differ for <c>""</c>,
        /// which is <c>= Nothing</c> (VB's String equality, #206) but is not <c>Is Nothing</c>, so
        /// the C#, JavaScript and MSIL backends read it there too (<see cref="IRCompare.IsStringEquality"/>).
        /// C++ needs no flag: its String has no null state, so both are emptiness. A pattern clone copies it
        /// with the rest of the node (<c>ClosureLowering</c>'s memberwise <c>Shallow</c>).</para>
        /// </summary>
        public bool WrittenWithIs { get; set; }
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
        /// The <c>Module</c> that declares the callee, for a user procedure; null for anything
        /// else (a class method, a stdlib or .NET call, a delegate invocation).
        ///
        /// <para>⛔ Carried BESIDE a bare <see cref="FunctionName"/>, never folded into it. The
        /// old wire form for a cross-unit call was the dotted <c>"Helpers.Twice"</c>, and exactly
        /// one backend honoured it: C++ strips the qualifier back off, JavaScript refused it
        /// outright ("no lowering for 'Helpers.Twice'") and MSIL sanitised the dot away into a
        /// call to <c>Combined::HelpersTwice</c>, a method nothing defines. Three backends spell
        /// a module procedure by its bare (or owner-qualified) IR name; only C#, which emits one
        /// static class per module, needs to know the owner — and reads it from here.</para>
        /// </summary>
        public string CalleeModule { get; set; }

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
        /// Set when this call is a user <c>Operator</c> applied in an expression (#198): the VB
        /// symbol (<c>=</c>, <c>+</c>, …). The call targets the class's static <c>op_*</c>
        /// function, which C++ calls by name; C# cannot (CS0571 — an operator is not callable by
        /// name) and renders the call as the infix operator instead.
        /// </summary>
        public string UserOperatorSymbol { get; set; }

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
    
    /// <summary>
    /// ⭐ A delegate VALUE bound to a method: <c>result = delegate DelegateType(Target, Method)</c>
    /// (ADR-0010 D8). The one node a lambda value and <c>AddressOf</c> both lower to.
    ///
    /// <para><b>Produced ONLY by <see cref="ClosureLowering"/></b>, which runs only for a
    /// backend that opts in (MSIL, and C++ since #140), after the optimizer and the verifier, on a
    /// clone of the module. The optimizer never sees one, and every visitor but MSIL's and C++'s
    /// throws on it (<see cref="IIRVisitor.Visit(IRDelegateCreate)"/>).</para>
    ///
    /// <list type="bullet">
    /// <item><see cref="DelegateType"/> is the delegate the value is TARGET-TYPED to (the
    /// declared type of the slot it is assigned to or passed as, ADR-0010 D7), also this
    /// value's <see cref="IRInstruction.Type"/>. The lowering has already checked that
    /// <see cref="Method"/>'s signature matches its <c>Invoke</c> exactly.</item>
    /// <item><see cref="Target"/> is the receiver the delegate is bound to — a closure
    /// environment for a lowered lambda, the object for <c>AddressOf obj.M</c> — or null for a
    /// static method (<c>ldnull</c>).</item>
    /// <item><see cref="IsVirtual"/> binds through the receiver's vtable
    /// (<c>dup; ldvirtftn</c>) rather than to <see cref="Method"/> itself (<c>ldftn</c>).</item>
    /// </list>
    ///
    /// <para>Kill vocabulary: a definition of its own name and nothing else — building a
    /// delegate runs no user code (<c>OptimizationPass.NamesWrittenBy</c>).</para>
    /// </summary>
    public class IRDelegateCreate : IRValue
    {
        public TypeInfo DelegateType { get; set; }
        public IRValue Target { get; set; }
        public IRFunction Method { get; set; }
        public bool IsVirtual { get; set; }

        public IRDelegateCreate(string resultName, TypeInfo delegateType, IRValue target, IRFunction method, bool isVirtual)
            : base(resultName, delegateType)
        {
            DelegateType = delegateType;
            Target = target;
            Method = method;
            IsVirtual = isVirtual;
        }

        public override void Accept(IIRVisitor visitor) => visitor.Visit(this);

        public override string ToString() =>
            $"{Name} = delegate {DelegateType?.Name}({Target?.Name ?? "null"}, {Method?.Name}{(IsVirtual ? ", virtual" : "")})";
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

        /// <summary>
        /// The TryCast is a <c>TypeOf x Is T</c> test (#197). JavaScript refuses one it cannot
        /// actually test (BL7013) — its TryCast to an interface passes the value through.
        /// </summary>
        public bool IsTypeOfTest { get; set; }

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

        /// <summary>
        /// The elements, carried ON the node — set only for an allocation built with emission
        /// suppressed (inside a <c>When</c> guard), where the element stores that normally follow
        /// it never reach a block. A guard is rendered inline, so each backend spells this as an
        /// array literal; without it the array was a name nothing declared (C# CS0103) or a
        /// <c>new Array(n)</c> of holes (JavaScript). Null everywhere else.
        /// </summary>
        public List<IRValue> InlineElements { get; set; }

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

        /// <summary>
        /// ⭐ ADR-0014 D1: the function-level locals whose <c>Dim</c> executes in the body of the
        /// loop this block is the BODY ENTRY of — innermost loop only (D3). Empty on every other
        /// block, and empty means inert.
        ///
        /// <para><b>Why a block.</b> Only <c>For Each</c> is an IR node; a counted <c>For</c>, a
        /// <c>While</c> and both <c>Do</c> forms are branches between blocks IRBuilder names
        /// <c>forN.body</c>, <c>whileN.body</c>, <c>doN.body</c> — the blocks every backend already
        /// recognises its loops by (see <see cref="IRLoops"/>). The body entry block stands for the
        /// loop node for all five kinds, For Each included (<see cref="IRForEach.BodyBlock"/>), so the
        /// fact has one home. The variables stay in <see cref="IRFunction.LocalVariables"/>; the IR is
        /// still flat.</para>
        ///
        /// <para>Written only by IRBuilder. Consumers act on
        /// <c>BodyLocals ∩ captureSet(function)</c> and only on that: C# and JavaScript declare such a
        /// variable at the top of the body from a carrier, ClosureLowering gives the loop one
        /// per-iteration environment. C++ ignores it until #140. The IR verifier checks every entry
        /// is in its function's <see cref="IRFunction.LocalVariables"/> and appears in one loop only.</para>
        /// </summary>
        public List<IRVariable> BodyLocals { get; set; }

        public BasicBlock(string name)
        {
            Name = name;
            Instructions = new List<IRInstruction>();
            Predecessors = new List<BasicBlock>();
            Successors = new List<BasicBlock>();
            Dominators = new HashSet<BasicBlock>();
            DominanceFrontier = new HashSet<BasicBlock>();
            BodyLocals = new List<IRVariable>();
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

        /// <summary>
        /// For a lambda (<see cref="IsLambda"/>): its capture set (task #122), as (name, type)
        /// pairs, filled by <c>IRBuilder</c> from the lambda's own IR. It is the NAME-based
        /// over-approximation <see cref="LambdaCapturedNames"/> is built from — every name the
        /// lambda's IR mentions, including its own locals, temps, members and globals, minus its
        /// parameters. It is NOT a declared free-variable list a backend could hoist the lambda
        /// with. The type is that of an <see cref="IRVariable"/> or named value the IR carries
        /// under the name, and null when the name only appears as a written slot (a For Each,
        /// Catch or pattern variable, a member store). Empty for a lambda whose IR holds names
        /// that cannot be enumerated (<see cref="IRInlineCode"/>), and for any other function.
        /// </summary>
        public List<(string name, TypeInfo type)> CapturedVariables { get; set; }

        /// <summary>
        /// ⭐ THE CAPTURE SET OF THE LAMBDAS THIS FUNCTION CREATES (task #122, ADR-0006 D1's
        /// Obligation): every name any of them may read or write, nested lambdas included.
        /// <c>IRBuilder</c> fills it at the end of each lambda it lowers here, from that lambda's
        /// IR. <c>OptimizationPass.IsCallVisible</c> reads it: a by-value parameter or a declared
        /// local of this function is call-visible when its name is in this set (compared ignoring
        /// case). <b>Null means NOT COMPUTED, never "nothing captured"</b>: a function IRBuilder
        /// did not record (hand-built IR) falls back to ADR-0006 D1's interim rule, every local
        /// visible. Names are kept with their exact spelling.
        /// </summary>
        public HashSet<string> LambdaCapturedNames { get; set; }

        /// <summary>
        /// The <c>__lambda_N</c> names whose captures <see cref="LambdaCapturedNames"/> accounts
        /// for. A lambda this function references that is NOT listed here — hand-built IR, or a
        /// lambda whose names could not be enumerated — makes
        /// <c>OptimizationPass.IsCallVisible</c> fall back to the interim rule for the whole
        /// function. Null means none recorded.
        /// </summary>
        public HashSet<string> LambdaCaptureSources { get; set; }

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

        /// <summary>Every name <see cref="GetNextTempName"/> has handed out (ADR-0017).</summary>
        private readonly HashSet<string> _mintedTempNames = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// ⭐ ADR-0018 D1: every name this function's PROGRAM owns — each name a user or a
        /// lowering declares in it, whatever its shape: a <c>Dim</c>, a <c>Const</c>, a
        /// parameter, a counted <c>For</c>'s variable, a <c>For Each</c> control variable, a
        /// <c>Catch</c> variable, a pattern binding, a LINQ range variable, a lambda parameter,
        /// a lowering's own local (<c>__sc</c>N, <c>__with</c>, <c>__foreach_</c>N,
        /// ClosureLowering's environment locals and carriers), and — for a lambda — every name
        /// of the function that creates it. Compared ignoring case (ADR-0013).
        ///
        /// <para>⛔ NOT a declaration list, and no backend may read it to decide what to emit.
        /// <see cref="LocalVariables"/> keeps its one meaning, "what a backend declares at the top
        /// of the function"; this set is a SUPERSET of what the function declares, and
        /// over-reserving costs a temp number while under-reserving is a collision. A For Each or
        /// Catch variable is declared by its own construct, so putting it in
        /// <see cref="LocalVariables"/> would declare it twice and widen a clause scope to the
        /// function (CS0136 for two <c>Catch ex</c> clauses).</para>
        ///
        /// <para><b>Who writes it.</b> IRBuilder RECORDS each name at the one primitive every
        /// declaration passes through (the version-stack push; <c>__with</c>, which never pushes,
        /// records at its site) and PUBLISHES the record here in <c>CompleteReservations</c>, once
        /// the walk is over and before anything reads the set, together with a lambda's creator
        /// set (ADR-0018 E1: published at the push, it renumbered programs whose temp-shaped names
        /// were already reserved). ClosureLowering writes it at every local it adds and for every
        /// lambda it hoists. <b>Who reads it.</b> <see cref="GetNextTempName"/> (it never mints a
        /// reserved name), <see cref="IRTempNames.UserOwned"/> (the one reader that filters it by
        /// shape, for IRBuilder's renamer and every backend's temp counter) and <c>IRVerifier</c>.
        /// The program's MODULE-level names are not copied in: every function shares one
        /// <see cref="ModuleReservedNames"/>, and every reader of "is this reserved" asks
        /// <see cref="IsReserved"/>.</para>
        ///
        /// <para>⛔ A MINTED name never enters it: "reserved" means "the program owns this", and
        /// the verifier refuses a compiler temp whose name is reserved (ADR-0018 D4), which needs
        /// the two sets disjoint.</para>
        /// </summary>
        public ISet<string> ReservedNames { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// ⭐ ADR-0018 E3: the program's MODULE-level names — globals, class fields, properties and
        /// methods, and every function's name (<see cref="IRTempNames.ModuleLevelNames"/>, the list
        /// <see cref="IRTempNames.UserOwned"/> collects) — as ONE set SHARED by every function of the
        /// module, published by <see cref="IRTempNames.PublishModuleNames"/> (IRBuilder at the end
        /// of the build, <c>CombineIRModules</c> for a multi-file build). Shared rather than copied
        /// into each <see cref="ReservedNames"/>: the names are the same for every function, and a
        /// copy would be quadratic.
        ///
        /// <para>Why the skip needs it: <see cref="ReservedNames"/> is per function, so without it
        /// <see cref="DeclareTemp"/> could mint <c>t9</c> in a function that CALLS a module
        /// function <c>t9()</c> or reads a global <c>t9</c>, and the new local would hide it (C#:
        /// "method name expected"; C++: the local shadows the function). Empty until published;
        /// hand-built IR never publishes, and <see cref="IRTempNames.UserOwned"/> still reads
        /// the module itself.</para>
        /// </summary>
        public ISet<string> ModuleReservedNames { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Whether the program owns <paramref name="name"/> here: this function reserves it
        /// (<see cref="ReservedNames"/>) or it is a module-level name (<see cref="ModuleReservedNames"/>).
        /// Ignoring case. What <see cref="GetNextTempName"/> skips and ADR-0018 D4 refuses.</summary>
        public bool IsReserved(string name) =>
            !string.IsNullOrEmpty(name)
            && (ReservedNames.Contains(name) || (ModuleReservedNames?.Contains(name) ?? false));

        /// <summary>
        /// True when the builder that made this function records every declaration in
        /// <see cref="ReservedNames"/> — IRBuilder, and ClosureLowering's clones of what IRBuilder
        /// built. The verifier's reservation invariant (ADR-0018 D1) applies only to such a
        /// function; hand-built IR reads false, and <see cref="IRTempNames.UserOwned"/> still
        /// reserves its locals and parameters through the union it reads.
        /// </summary>
        public bool TracksReservedNames { get; set; }

        /// <summary>Adds <paramref name="name"/> to <see cref="ReservedNames"/> (null and empty are
        /// ignored). ⛔ Never for a name this function minted (ADR-0018 D2).</summary>
        public void Reserve(string name)
        {
            if (!string.IsNullOrEmpty(name)) ReservedNames.Add(name);
        }

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
        
        /// <summary>
        /// ⭐ THE ONE MINTER of compiler-temp names (<c>t0</c>, <c>t1</c>, …), and it RECORDS what
        /// it hands out: that record is what <see cref="IRValue.IsCompilerTemp"/> is read from
        /// (ADR-0017), so the flag says "the compiler made this name up", not "this name looks
        /// made up".
        ///
        /// <para>It never hands out a name the program owns (<see cref="IsReserved"/>: this
        /// function's <see cref="ReservedNames"/> and the module-level names, ignoring case) or one it
        /// already handed out (ADR-0018, clarification C1 as amended by E1). IRBuilder names its
        /// VALUES through it during the walk, when nothing is published yet, so there it skips
        /// nothing and <c>IRBuilder.SeparateTempsFromUserNames</c> separates every collision, as it
        /// always has; every minter AFTER publication — that renamer's own re-mint and
        /// <see cref="DeclareTemp"/> — skips the full set.</para>
        ///
        /// <para>⛔ An optimizer pass never calls it: a pass mints through <see cref="DeclareTemp"/>,
        /// which also declares what it mints (a test enforces this).</para>
        /// </summary>
        public string GetNextTempName()
        {
            string name;
            do { name = $"t{_nextTempId++}"; }
            while (IsReserved(name) || _mintedTempNames.Contains(name));
            _mintedTempNames.Add(name);
            return name;
        }

        /// <summary>
        /// ⭐ ADR-0018 D2: THE ONLY DOOR through which an optimizer pass gets a named temp. Mints a
        /// name through <see cref="GetNextTempName"/> (so it skips every reserved and every
        /// already-minted name, and is recorded), marks the variable a compiler temp, and DECLARES
        /// it — adds it to <see cref="LocalVariables"/>, so every backend emits its declaration.
        ///
        /// <para>Post-condition: <c>v.IsCompilerTemp &amp;&amp; LocalVariables.Contains(v)
        /// &amp;&amp; IsMintedTempName(v.Name) &amp;&amp; !IsReserved(v.Name)</c> — not a name this
        /// function reserves, and not a module-level name (E3).</para>
        ///
        /// <para>Function level only. A pass that needs the temp fresh per loop iteration adds it
        /// to the loop's <see cref="BasicBlock.BodyLocals"/> itself (ADR-0014's obligation on the
        /// pass). Every defect of the three unregistered passes (inlining, loop unrolling,
        /// induction variables) was "minted a name, declared nothing"; a bare minter invites that
        /// shape again, which is why there is no other door.</para>
        /// </summary>
        public IRVariable DeclareTemp(TypeInfo type)
        {
            var variable = new IRVariable(GetNextTempName(), type) { IsCompilerTemp = true };
            LocalVariables.Add(variable);
            return variable;
        }

        /// <summary>
        /// Whether <see cref="GetNextTempName"/> of THIS function handed out
        /// <paramref name="name"/> — exactly, ordinal: the minter's own output, not a name that
        /// merely looks like it (a user's <c>T5</c> is not the minted <c>t5</c>).
        /// </summary>
        public bool IsMintedTempName(string name) => name != null && _mintedTempNames.Contains(name);

        /// <summary>
        /// Makes this function a COPY of <paramref name="original"/> as far as minting goes: the same
        /// minted-name record and the same counter. For a clone (ClosureLowering's), whose values carry
        /// <see cref="IRValue.IsCompilerTemp"/> over from the original: without the record the clone
        /// would hold marked temps its minter never handed out, and a temp an optimizer pass declared
        /// (<see cref="DeclareTemp"/>) would read as an undeclared-and-unreserved local to the
        /// verifier's Invariant R (MEASURED: a lambda's minted temp, on MSIL, ADR-0018).
        /// </summary>
        internal void InheritTempRecordFrom(IRFunction original)
        {
            if (original == null) return;
            _mintedTempNames.UnionWith(original._mintedTempNames);
            _nextTempId = Math.Max(_nextTempId, original._nextTempId);
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
        /// The classes with every base before the classes that derive from it, otherwise in
        /// declaration order. Declaration order was the only order, and it held only because the
        /// analyzer refused a derived class declared above its base. With that fixed, C++ failed
        /// to compile `class Sq : public Base` ahead of `Base` ("invalid use of incomplete type"),
        /// and JavaScript built clean and died on load, because `class Sq extends Base` ran first
        /// ("ReferenceError: Cannot access 'Base' before initialization").
        /// Every backend that emits class bodies in one pass orders them through here.
        /// </summary>
        public IEnumerable<IRClass> ClassesBaseFirst()
        {
            var emitted = new HashSet<IRClass>();
            var ordered = new List<IRClass>();

            void Place(IRClass cls, int depth)
            {
                if (cls == null || depth > 256 || !emitted.Add(cls)) return;
                if (!string.IsNullOrEmpty(cls.BaseClass)
                    && Classes.TryGetValue(cls.BaseClass, out var baseClass)
                    && !ReferenceEquals(baseClass, cls))
                {
                    // `emitted` is filled before recursing, so a (refused) cycle still terminates.
                    Place(baseClass, depth + 1);
                }
                ordered.Add(cls);
            }

            foreach (var cls in Classes.Values)
                Place(cls, 0);
            return ordered;
        }

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

        /// <summary>
        /// The interface DECLARES a getter — not "the getter has a body"; an interface accessor
        /// never has one. True unless the property is <see cref="IsWriteOnly"/> (ADR-0002).
        /// </summary>
        public bool HasGetter { get; set; }

        /// <summary>The interface DECLARES a setter. True unless <see cref="IsReadOnly"/> (ADR-0002).</summary>
        public bool HasSetter { get; set; }

        /// <summary><c>ReadOnly Property</c>, as written — the source of truth for <see cref="HasSetter"/>.</summary>
        public bool IsReadOnly { get; set; }

        /// <summary><c>WriteOnly Property</c>, as written — the source of truth for <see cref="HasGetter"/>.</summary>
        public bool IsWriteOnly { get; set; }
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

        /// <summary>
        /// The event's delegate type with its generic arguments. <see cref="DelegateType"/> is
        /// only the NAME, which is why C# emitted <c>event Action Clicked</c> for
        /// <c>Event Clicked As Action(Of Integer)</c>. Null only for IR built without an analyzer.
        /// </summary>
        public TypeInfo Type { get; set; }
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

        /// <summary>
        /// True for <c>Extern Class</c>: the type ALREADY EXISTS in the target runtime, so the
        /// backend must emit NOTHING for it and its members carry signatures, not bodies.
        ///
        /// <para>⛔ A backend that emits a declaration anyway SHADOWS the real type —
        /// <c>class Element {}</c> over the DOM's Element — which fails at run time from a build
        /// that reported success. A backend with no such runtime concept must REFUSE the
        /// program rather than ignore the flag.</para>
        /// </summary>
        public bool IsExtern { get; set; }

        /// <summary>
        /// The name of the class this one is DECLARED INSIDE, or null for a top-level class —
        /// which is every class the front end builds.
        ///
        /// <para>Set only by <see cref="ClosureLowering"/> (ADR-0010), on the environment class
        /// of a lambda whose creator is a member of <see cref="EnclosingClass"/>. The nesting is
        /// what lets a lambda reach its creator's PRIVATE members through the captured
        /// <c>Me</c>: measured on .NET 8, a top-level class reading another class's private field
        /// dies with FieldAccessException, while a nested one may (ECMA-335: a nested type has
        /// access to everything its enclosing type has).</para>
        /// </summary>
        public string EnclosingClass { get; set; }

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

        /// <summary>
        /// Overridable / Overrides, carried for the same reason <see cref="IRMethod"/> carries
        /// them: a backend cannot mark an accessor virtual from information it never receives.
        /// Dropping these here made every property override answer the BASE's value on MSIL and
        /// C#, silently — see <c>PropertyNode.IsVirtual</c> for the measurement.
        /// </summary>
        public bool IsVirtual { get; set; }
        public bool IsOverride { get; set; }

        public IRFunction Getter { get; set; }
        public IRFunction Setter { get; set; }

        /// <summary>
        /// ⭐ ADR-0007's "ACCESSOR-BACKED", read off the IR: using this property may run user code
        /// — it has a Get or Set accessor function, or it is Overridable/Overrides (a derived
        /// class's accessor may run in its place). A plain auto-property has neither and is
        /// storage, like a field. The same fact as <c>PropertyNode.IsAccessorBacked</c>, which
        /// IRBuilder's bare-name lowering reads through the analyzer's symbol; this copy is what
        /// <see cref="Optimization.IRVerifier.CheckInvariantF"/> checks the lowering against.
        /// </summary>
        public bool IsAccessorBacked => Getter != null || Setter != null || IsVirtual || IsOverride;
    }

    /// <summary>
    /// Represents a constructor in an IR class
    /// </summary>
    public class IRConstructor
    {
        public AccessModifier Access { get; set; }
        public List<IRVariable> Parameters { get; set; }
        public IRFunction Implementation { get; set; }

        /// <summary>
        /// ⭐ ADR-0016 D1: the explicit base-constructor call, an ordinary instruction in
        /// <see cref="Implementation"/>'s prologue region (<see cref="IRBaseConstructorCall.Find"/>),
        /// or null for the IMPLICIT parameterless base call every backend emits on its own.
        /// A view, never a second home: the arguments live only on the instruction.
        /// </summary>
        public IRBaseConstructorCall BaseCall => IRBaseConstructorCall.Find(Implementation);

        /// <summary>
        /// The base call's operands, read off <see cref="BaseCall"/> — empty when there is none.
        /// Read-only on purpose: <c>IRConstructor.BaseConstructorArgs</c> used to be a list outside
        /// every block, invisible to the capture analysis, <c>UsesOf</c>, DCE and the verifier
        /// (#170, #240); ADR-0016 deleted it. What remains is this derived view, which no pass or
        /// backend writes through.
        /// </summary>
        public IReadOnlyList<IRValue> BaseConstructorArgs =>
            (IReadOnlyList<IRValue>)BaseCall?.Args ?? Array.Empty<IRValue>();

        public IRConstructor()
        {
            Parameters = new List<IRVariable>();
        }
    }

    /// <summary>
    /// ⭐ ADR-0016 D1: <c>MyBase.New(a1, …, an)</c> as an INSTRUCTION. Its arguments are ordinary
    /// instructions evaluated before it — the constructor's PROLOGUE — and it is their one
    /// consumer, so every walker (the capture analysis, <c>UsesOf</c>, DCE, CSE, the verifier) sees
    /// the use without a special case. Built by IRBuilder only when the base call has arguments
    /// (written ones, or Optional defaults filled for an implicit call); the implicit
    /// parameterless call stays backend-side exactly as before.
    ///
    /// <list type="bullet">
    /// <item>No result value; <see cref="HasSideEffects"/> is true. Never removable, never
    /// reordered: a barrier like <see cref="IRThrow"/>, and a FULL one in the kill vocabulary
    /// (<c>NamesWrittenBy</c> answers Everything): no expression or copy available before it is
    /// available after it.</item>
    /// <item>The PROLOGUE REGION is the blocks from the entry up to and including the block
    /// holding this instruction; within that block, the instructions before it. Nothing outside
    /// the region branches into it; a branch inside it is only the lowering of an argument
    /// expression itself (<c>AndAlso</c>/<c>OrElse</c>). The verifier checks the region
    /// (<c>IRVerifier.CheckInvariantP</c>).</item>
    /// <item>Every backend emits it after its own argument evaluation and before the body: C#
    /// renders the operands as EXPRESSIONS into <c>: base(…)</c>, JavaScript and MSIL emit the
    /// prologue before <c>super(…)</c> / <c>call Base::.ctor</c>, C++ emits it in place in
    /// <c>ctor_</c> (ADR-0015).</item>
    /// </list>
    /// </summary>
    public sealed class IRBaseConstructorCall : IRInstruction
    {
        private List<IRValue> _args;
        private readonly List<bool> _byRef;

        public IRBaseConstructorCall(IEnumerable<IRValue> args, IEnumerable<bool> byRefArguments = null)
        {
            _args = new List<IRValue>(args ?? Enumerable.Empty<IRValue>());
            _byRef = new List<bool>(byRefArguments ?? Enumerable.Empty<bool>());
        }

        /// <summary>The arguments, in the base constructor's parameter order (Optional
        /// defaults already filled).</summary>
        public IReadOnlyList<IRValue> Args => _args;

        /// <summary>
        /// Which arguments the base constructor takes BY REFERENCE — the twin of
        /// <see cref="IRNewObject.ByRefArguments"/>, indexed in lockstep with <see cref="Args"/>
        /// and consulted only where an entry exists (#144: <c>MyBase.New(m)</c> into a ByRef
        /// parameter passed <c>m</c> by value on every backend). Never rewritten, so a shallow
        /// clone may share it.
        /// </summary>
        public IReadOnlyList<bool> ByRefArguments => _byRef;

        /// <summary>The operand slots, for the one use walker (<c>OptimizationPass.MapUses</c>)
        /// and the module cloner — the only writers.</summary>
        internal List<IRValue> ArgSlots => _args;

        /// <summary>Gives a shallow clone (<c>ClosureLowering</c>'s module cloner) its own operand
        /// list, so rewriting the clone's operands never writes into the original module.</summary>
        internal void DetachArgs() => _args = new List<IRValue>(_args);

        /// <summary>Always true: the base constructor runs user code.</summary>
        public bool HasSideEffects => true;

        /// <summary>The one base call of <paramref name="implementation"/>, or null. D5(b): at most
        /// one, in the prologue region.</summary>
        public static IRBaseConstructorCall Find(IRFunction implementation)
        {
            if (implementation?.Blocks == null) return null;
            foreach (var block in implementation.Blocks)
                foreach (var inst in block.Instructions)
                    if (inst is IRBaseConstructorCall call) return call;
            return null;
        }

        public override void Accept(IIRVisitor visitor) => visitor.Visit(this);
        public override string ToString() =>
            $"call base.New({string.Join(", ", _args.Select(a => a is IRConstant c ? c.ToString() : a?.Name))})";
    }

    /// <summary>
    /// Represents a new object instantiation: new ClassName(args)
    /// </summary>
    public class IRNewObject : IRValue, INetCarrying
    {
        public string ClassName { get; set; }
        public List<IRValue> Arguments { get; set; }

        /// <summary>
        /// Which arguments the constructor takes BY REFERENCE — the construction twin of
        /// <see cref="IRInstanceMethodCall.ByRefArguments"/>, indexed in lockstep with
        /// <see cref="Arguments"/>, filled by IRBuilder from the constructor the analyzer bound
        /// the site to, and consulted only where an entry exists.
        ///
        /// <para>⛔ Its absence was #144: <c>New Box(p)</c> against <c>Sub New(ByRef n As
        /// Integer)</c> passed <c>p</c> BY VALUE on every backend, so the constructor's write
        /// never reached the caller (vbc prints the written value).</para>
        /// </summary>
        public List<bool> ByRefArguments { get; set; }

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
            ByRefArguments = new List<bool>();
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

        /// <summary>
        /// Which arguments the base method takes BY REFERENCE — the base-call twin of
        /// <see cref="IRInstanceMethodCall.ByRefArguments"/>, indexed in lockstep with
        /// <see cref="Arguments"/> and filled by the same IRBuilder helper from the same resolved
        /// symbol.
        ///
        /// <para>⛔ Its absence was #142/#265: the base call carried none of its target's
        /// parameter facts, so <c>MyBase.SetIt(p)</c> against a ByRef parameter was CS1620 on C#
        /// (no <c>ref</c>) and a MissingMethodException on MSIL (the call named
        /// <c>SetIt(int32)</c>). The other facts — an omitted Optional, a ParamArray tail, the
        /// declared parameter type — reach the node as ARGUMENTS, exactly as for an instance
        /// call.</para>
        /// </summary>
        public List<bool> ByRefArguments { get; set; }

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
            ByRefArguments = new List<bool>();
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

        /// <summary>
        /// True when the producer KNOWS this names STORAGE — a plain field, or a plain
        /// auto-property (ADR-0007's "storage") — so the read runs no user code. The kill
        /// vocabulary then treats it as a read of <see cref="FieldName"/> rather than as a call
        /// that may run a Property Get (<c>OptimizationPass.NamesWrittenBy</c>).
        ///
        /// <para>False by default, which is every node the front end builds: IRBuilder lowers
        /// <c>obj.P</c> to this node for a property and a field alike and cannot say which.
        /// Set only by <see cref="ClosureLowering"/> (ADR-0010), for the closure-environment
        /// loads it synthesises and for a creator's field that a lambda reads through the
        /// captured <c>Me</c> — reads that were bare variables (not calls) before lowering, so the
        /// verifier re-run on the lowered IR must not see a call where none was.</para>
        /// </summary>
        internal bool IsStorageAccess { get; set; }

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

        /// <summary>The write twin of <see cref="IRFieldAccess.IsStorageAccess"/>: true when the
        /// producer knows this writes a plain field, so no Property Set runs. Set only by
        /// <see cref="ClosureLowering"/>; false for every node the front end builds.</summary>
        internal bool IsStorageAccess { get; set; }

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
