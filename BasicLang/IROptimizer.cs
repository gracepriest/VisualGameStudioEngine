using System;
using System.Collections.Generic;
using System.Linq;
using BasicLang.Compiler.SemanticAnalysis;

namespace BasicLang.Compiler.IR.Optimization
{
    /// <summary>
    /// Base class for optimization passes
    /// </summary>
    public abstract class OptimizationPass
    {
        public string Name { get; protected set; }
        public int ModificationCount { get; protected set; }
        
        protected OptimizationPass(string name)
        {
            Name = name;
        }
        
        public abstract bool Run(IRModule module);
        
        protected void ReportModification()
        {
            ModificationCount++;
        }
    }
    
    /// <summary>
    /// Constant folding - evaluate constant expressions at compile time
    /// </summary>
    public class ConstantFoldingPass : OptimizationPass
    {
        public ConstantFoldingPass() : base("Constant Folding") { }
        
        public override bool Run(IRModule module)
        {
            ModificationCount = 0;
            
            foreach (var function in module.Functions)
            {
                if (function.IsExternal) continue;
                
                foreach (var block in function.Blocks)
                {
                    FoldBlock(block);
                }
            }
            
            return ModificationCount > 0;
        }
        
        private void FoldBlock(BasicBlock block)
        {
            for (int i = 0; i < block.Instructions.Count; i++)
            {
                var instruction = block.Instructions[i];

                if (instruction is IRBinaryOp binaryOp)
                {
                    var folded = TryFoldBinary(binaryOp);
                    if (folded != null)
                    {
                        // Update all references to the old instruction
                        ReplaceAllReferences(block, binaryOp, folded);

                        // If this was a named variable (not a temp), preserve assignment
                        if (IsNamedVariable(binaryOp.Name))
                        {
                            var targetVar = new IRVariable(binaryOp.Name, binaryOp.Type);
                            block.Instructions[i] = new IRAssignment(targetVar, folded);
                        }
                        else
                        {
                            block.Instructions[i] = folded;
                        }
                        ReportModification();
                    }
                }
                else if (instruction is IRUnaryOp unaryOp)
                {
                    var folded = TryFoldUnary(unaryOp);
                    if (folded != null)
                    {
                        // Update all references to the old instruction
                        ReplaceAllReferences(block, unaryOp, folded);

                        // If this was a named variable (not a temp), preserve assignment
                        if (IsNamedVariable(unaryOp.Name))
                        {
                            var targetVar = new IRVariable(unaryOp.Name, unaryOp.Type);
                            block.Instructions[i] = new IRAssignment(targetVar, folded);
                        }
                        else
                        {
                            block.Instructions[i] = folded;
                        }
                        ReportModification();
                    }
                }
                else if (instruction is IRCompare compare)
                {
                    var folded = TryFoldCompare(compare);
                    if (folded != null)
                    {
                        // Update all references to the old instruction
                        ReplaceAllReferences(block, compare, folded);

                        // If this was a named variable (not a temp), preserve assignment
                        if (IsNamedVariable(compare.Name))
                        {
                            var targetVar = new IRVariable(compare.Name, compare.Type);
                            block.Instructions[i] = new IRAssignment(targetVar, folded);
                        }
                        else
                        {
                            block.Instructions[i] = folded;
                        }
                        ReportModification();
                    }
                }
            }
        }

        /// <summary>
        /// Check if a name represents a real variable (not a temp)
        /// </summary>
        private bool IsNamedVariable(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            // Temp names typically start with _tmp, t, or are numeric
            if (name.StartsWith("_tmp", StringComparison.OrdinalIgnoreCase)) return false;
            if (name.StartsWith("t", StringComparison.OrdinalIgnoreCase) && name.Length > 1 && char.IsDigit(name[1])) return false;
            return true;
        }

        /// <summary>
        /// Replace all references to oldValue with newValue in the block
        /// </summary>
        private void ReplaceAllReferences(BasicBlock block, IRValue oldValue, IRValue newValue)
        {
            foreach (var inst in block.Instructions)
            {
                if (inst is IRBinaryOp binOp)
                {
                    if (ReferenceEquals(binOp.Left, oldValue)) binOp.Left = newValue;
                    if (ReferenceEquals(binOp.Right, oldValue)) binOp.Right = newValue;
                }
                else if (inst is IRUnaryOp unOp)
                {
                    if (ReferenceEquals(unOp.Operand, oldValue)) unOp.Operand = newValue;
                }
                else if (inst is IRCompare cmp)
                {
                    if (ReferenceEquals(cmp.Left, oldValue)) cmp.Left = newValue;
                    if (ReferenceEquals(cmp.Right, oldValue)) cmp.Right = newValue;
                }
                else if (inst is IRAssignment asg)
                {
                    if (ReferenceEquals(asg.Value, oldValue)) asg.Value = newValue;
                }
                else if (inst is IRStore store)
                {
                    if (ReferenceEquals(store.Value, oldValue)) store.Value = newValue;
                    if (ReferenceEquals(store.Address, oldValue)) store.Address = newValue;
                }
                else if (inst is IRCall call)
                {
                    for (int j = 0; j < call.Arguments.Count; j++)
                    {
                        if (ReferenceEquals(call.Arguments[j], oldValue))
                            call.Arguments[j] = newValue;
                    }
                }
                else if (inst is IRReturn ret)
                {
                    if (ReferenceEquals(ret.Value, oldValue)) ret.Value = newValue;
                }
                else if (inst is IRConditionalBranch condBr)
                {
                    if (ReferenceEquals(condBr.Condition, oldValue)) condBr.Condition = newValue;
                }
            }
        }
        
        private IRConstant TryFoldBinary(IRBinaryOp op)
        {
            if (!(op.Left is IRConstant left) || !(op.Right is IRConstant right))
                return null;
            
            try
            {
                object result = op.Operation switch
                {
                    BinaryOpKind.Add => FoldAdd(left.Value, right.Value),
                    BinaryOpKind.Sub => FoldSub(left.Value, right.Value),
                    BinaryOpKind.Mul => FoldMul(left.Value, right.Value),
                    BinaryOpKind.Div => FoldDiv(left.Value, right.Value),
                    BinaryOpKind.IntDiv => FoldIntDiv(left.Value, right.Value),
                    BinaryOpKind.Mod => FoldMod(left.Value, right.Value),
                    BinaryOpKind.And => FoldAnd(left.Value, right.Value),
                    BinaryOpKind.Or => FoldOr(left.Value, right.Value),
                    BinaryOpKind.BitwiseAnd => FoldBitwiseAnd(left.Value, right.Value),
                    BinaryOpKind.BitwiseOr => FoldBitwiseOr(left.Value, right.Value),
                    BinaryOpKind.Xor => FoldXor(left.Value, right.Value),
                    BinaryOpKind.Shl => FoldShl(left.Value, right.Value),
                    BinaryOpKind.Shr => FoldShr(left.Value, right.Value),
                    _ => null
                };
                
                if (result != null)
                {
                    return new IRConstant(result, op.Type);
                }
            }
            catch (DivideByZeroException)
            {
                // Division by zero cannot be folded at compile time - let runtime handle it
            }
            catch (OverflowException)
            {
                // Arithmetic overflow cannot be folded - let runtime handle it
            }
            catch (InvalidCastException)
            {
                // Type conversion failed - cannot fold
            }

            return null;
        }

        private IRConstant TryFoldUnary(IRUnaryOp op)
        {
            if (!(op.Operand is IRConstant operand))
                return null;

            try
            {
                object result = op.Operation switch
                {
                    UnaryOpKind.Neg => FoldNeg(operand.Value),
                    UnaryOpKind.Not => FoldNot(operand.Value),
                    UnaryOpKind.Inc => FoldInc(operand.Value),
                    UnaryOpKind.Dec => FoldDec(operand.Value),
                    _ => null
                };

                if (result != null)
                {
                    return new IRConstant(result, op.Type);
                }
            }
            catch (OverflowException)
            {
                // Arithmetic overflow cannot be folded - let runtime handle it
            }
            catch (InvalidCastException)
            {
                // Type conversion failed - cannot fold
            }
            
            return null;
        }
        
        private IRConstant TryFoldCompare(IRCompare cmp)
        {
            if (!(cmp.Left is IRConstant left) || !(cmp.Right is IRConstant right))
                return null;
            
            try
            {
                bool result = cmp.Comparison switch
                {
                    CompareKind.Eq => CompareEq(left.Value, right.Value),
                    CompareKind.Ne => !CompareEq(left.Value, right.Value),
                    CompareKind.Lt => CompareLt(left.Value, right.Value),
                    CompareKind.Le => !CompareGt(left.Value, right.Value),
                    CompareKind.Gt => CompareGt(left.Value, right.Value),
                    CompareKind.Ge => !CompareLt(left.Value, right.Value),
                    _ => false
                };
                
                return new IRConstant(result, cmp.Type);
            }
            catch { }
            
            return null;
        }
        
        // Arithmetic operations
        private object FoldAdd(object a, object b)
        {
            if (a is int ia && b is int ib) return ia + ib;
            if (a is long la && b is long lb) return la + lb;
            if (a is float fa && b is float fb) return fa + fb;
            if (a is double da && b is double db) return da + db;
            if (a is string sa && b is string sb) return sa + sb;
            return null;
        }
        
        private object FoldSub(object a, object b)
        {
            if (a is int ia && b is int ib) return ia - ib;
            if (a is long la && b is long lb) return la - lb;
            if (a is float fa && b is float fb) return fa - fb;
            if (a is double da && b is double db) return da - db;
            return null;
        }
        
        private object FoldMul(object a, object b)
        {
            if (a is int ia && b is int ib) return ia * ib;
            if (a is long la && b is long lb) return la * lb;
            if (a is float fa && b is float fb) return fa * fb;
            if (a is double da && b is double db) return da * db;
            return null;
        }
        
        private object FoldDiv(object a, object b)
        {
            if (a is int ia && b is int ib && ib != 0) return ia / ib;
            if (a is long la && b is long lb && lb != 0) return la / lb;
            if (a is float fa && b is float fb && fb != 0) return fa / fb;
            if (a is double da && b is double db && db != 0) return da / db;
            return null;
        }
        
        private object FoldIntDiv(object a, object b)
        {
            if (a is int ia && b is int ib && ib != 0) return ia / ib;
            if (a is long la && b is long lb && lb != 0) return la / lb;
            return null;
        }
        
        private object FoldMod(object a, object b)
        {
            if (a is int ia && b is int ib && ib != 0) return ia % ib;
            if (a is long la && b is long lb && lb != 0) return la % lb;
            return null;
        }
        
        private object FoldAnd(object a, object b)
        {
            // Logical AND (short-circuit)
            if (a is bool ba && b is bool bb) return ba && bb;
            return null;
        }

        private object FoldOr(object a, object b)
        {
            // Logical OR (short-circuit)
            if (a is bool ba && b is bool bb) return ba || bb;
            return null;
        }

        private object FoldBitwiseAnd(object a, object b)
        {
            // Bitwise AND
            if (a is int ia && b is int ib) return ia & ib;
            if (a is long la && b is long lb) return la & lb;
            if (a is byte ba && b is byte bb) return (byte)(ba & bb);
            if (a is short sa && b is short sb) return (short)(sa & sb);
            return null;
        }

        private object FoldBitwiseOr(object a, object b)
        {
            // Bitwise OR
            if (a is int ia && b is int ib) return ia | ib;
            if (a is long la && b is long lb) return la | lb;
            if (a is byte ba && b is byte bb) return (byte)(ba | bb);
            if (a is short sa && b is short sb) return (short)(sa | sb);
            return null;
        }
        
        private object FoldXor(object a, object b)
        {
            if (a is int ia && b is int ib) return ia ^ ib;
            if (a is long la && b is long lb) return la ^ lb;
            return null;
        }
        
        private object FoldShl(object a, object b)
        {
            if (a is int ia && b is int ib) return ia << ib;
            if (a is long la && b is int lb) return la << lb;
            return null;
        }
        
        private object FoldShr(object a, object b)
        {
            if (a is int ia && b is int ib) return ia >> ib;
            if (a is long la && b is int lb) return la >> lb;
            return null;
        }
        
        private object FoldNeg(object a)
        {
            if (a is int ia) return -ia;
            if (a is long la) return -la;
            if (a is float fa) return -fa;
            if (a is double da) return -da;
            return null;
        }
        
        private object FoldNot(object a)
        {
            if (a is bool ba) return !ba;
            return null;
        }
        
        private object FoldInc(object a)
        {
            if (a is int ia) return ia + 1;
            if (a is long la) return la + 1;
            return null;
        }
        
        private object FoldDec(object a)
        {
            if (a is int ia) return ia - 1;
            if (a is long la) return la - 1;
            return null;
        }
        
        // Comparison operations
        private bool CompareEq(object a, object b)
        {
            return Equals(a, b);
        }
        
        private bool CompareLt(object a, object b)
        {
            if (a is int ia && b is int ib) return ia < ib;
            if (a is long la && b is long lb) return la < lb;
            if (a is float fa && b is float fb) return fa < fb;
            if (a is double da && b is double db) return da < db;
            return false;
        }
        
        private bool CompareGt(object a, object b)
        {
            if (a is int ia && b is int ib) return ia > ib;
            if (a is long la && b is long lb) return la > lb;
            if (a is float fa && b is float fb) return fa > fb;
            if (a is double da && b is double db) return da > db;
            return false;
        }
    }
    
    /// <summary>
    /// Dead code elimination - remove instructions that don't affect program output
    /// </summary>
    public class DeadCodeEliminationPass : OptimizationPass
    {
        public DeadCodeEliminationPass() : base("Dead Code Elimination") { }
        
        public override bool Run(IRModule module)
        {
            ModificationCount = 0;
            
            foreach (var function in module.Functions)
            {
                if (function.IsExternal) continue;
                
                // Build CFG
                var cfg = new ControlFlowGraph(function);
                cfg.Build();
                
                // Remove unreachable blocks
                int removed = cfg.RemoveUnreachableBlocks();
                ModificationCount += removed;
                
                // Remove dead instructions
                foreach (var block in function.Blocks)
                {
                    RemoveDeadInstructions(block);
                }
            }
            
            return ModificationCount > 0;
        }
        
        private void RemoveDeadInstructions(BasicBlock block)
        {
            var used = new HashSet<IRValue>();

            // Mark instructions that are used
            foreach (var inst in block.Instructions)
            {
                MarkUsed(inst, used);
            }

            // Remove unused assignments
            for (int i = block.Instructions.Count - 1; i >= 0; i--)
            {
                var inst = block.Instructions[i];

                // Don't remove instructions that represent assignments to named variables
                // (non-temp names indicate the result is assigned to a real variable)
                if (inst is IRValue v && !string.IsNullOrEmpty(v.Name) && !v.Name.StartsWith("_tmp"))
                {
                    continue;
                }

                if (inst is IRBinaryOp binaryOp && !used.Contains(binaryOp))
                {
                    block.Instructions.RemoveAt(i);
                    ReportModification();
                }
                else if (inst is IRUnaryOp unaryOp && !used.Contains(unaryOp))
                {
                    block.Instructions.RemoveAt(i);
                    ReportModification();
                }
                else if (inst is IRCompare compare && !used.Contains(compare))
                {
                    block.Instructions.RemoveAt(i);
                    ReportModification();
                }
                else if (inst is IRLoad load && !used.Contains(load))
                {
                    block.Instructions.RemoveAt(i);
                    ReportModification();
                }
            }
        }
        
        private void MarkUsed(IRInstruction inst, HashSet<IRValue> used)
        {
            if (inst is IRBinaryOp binaryOp)
            {
                used.Add(binaryOp.Left);
                used.Add(binaryOp.Right);
            }
            else if (inst is IRUnaryOp unaryOp)
            {
                used.Add(unaryOp.Operand);
            }
            else if (inst is IRCompare compare)
            {
                used.Add(compare.Left);
                used.Add(compare.Right);
            }
            else if (inst is IRStore store)
            {
                used.Add(store.Value);
                used.Add(store.Address);
            }
            else if (inst is IRLoad load)
            {
                used.Add(load.Address);
            }
            else if (inst is IRCall call)
            {
                foreach (var arg in call.Arguments)
                {
                    used.Add(arg);
                }
            }
            else if (inst is IRReturn ret && ret.Value != null)
            {
                used.Add(ret.Value);
            }
            else if (inst is IRConditionalBranch condBr)
            {
                used.Add(condBr.Condition);
            }
            else if (inst is IRSwitch switchInst)
            {
                used.Add(switchInst.Value);
            }
            else if (inst is IRAssignment assignment)
            {
                used.Add(assignment.Value);
            }
        }
    }
    
    /// <summary>
    /// Copy propagation - replace uses of copied variables with their source
    /// </summary>
    public class CopyPropagationPass : OptimizationPass
    {
        public CopyPropagationPass() : base("Copy Propagation") { }
        
        public override bool Run(IRModule module)
        {
            ModificationCount = 0;
            
            foreach (var function in module.Functions)
            {
                if (function.IsExternal) continue;
                
                foreach (var block in function.Blocks)
                {
                    PropagateCopies(block);
                }
            }
            
            return ModificationCount > 0;
        }
        
        private void PropagateCopies(BasicBlock block)
        {
            var copies = new Dictionary<IRVariable, IRValue>();
            
            foreach (var inst in block.Instructions)
            {
                // Replace uses
                ReplaceUses(inst, copies);
                
                // Track copy assignments
                if (inst is IRAssignment assignment && 
                    assignment.Target is IRVariable target &&
                    assignment.Value is IRValue value)
                {
                    copies[target] = value;
                }
            }
        }
        
        private void ReplaceUses(IRInstruction inst, Dictionary<IRVariable, IRValue> copies)
        {
            if (inst is IRBinaryOp binaryOp)
            {
                if (binaryOp.Left is IRVariable leftVar && copies.ContainsKey(leftVar))
                {
                    binaryOp.Left = copies[leftVar];
                    ReportModification();
                }
                if (binaryOp.Right is IRVariable rightVar && copies.ContainsKey(rightVar))
                {
                    binaryOp.Right = copies[rightVar];
                    ReportModification();
                }
            }
            else if (inst is IRUnaryOp unaryOp)
            {
                if (unaryOp.Operand is IRVariable operandVar && copies.ContainsKey(operandVar))
                {
                    unaryOp.Operand = copies[operandVar];
                    ReportModification();
                }
            }
            else if (inst is IRCompare compare)
            {
                if (compare.Left is IRVariable leftVar && copies.ContainsKey(leftVar))
                {
                    compare.Left = copies[leftVar];
                    ReportModification();
                }
                if (compare.Right is IRVariable rightVar && copies.ContainsKey(rightVar))
                {
                    compare.Right = copies[rightVar];
                    ReportModification();
                }
            }
        }
    }
    
    /// <summary>
    /// Common subexpression elimination - avoid recomputing identical expressions
    /// </summary>
    public class CommonSubexpressionEliminationPass : OptimizationPass
    {
        public CommonSubexpressionEliminationPass() : base("Common Subexpression Elimination") { }
        
        public override bool Run(IRModule module)
        {
            ModificationCount = 0;
            
            foreach (var function in module.Functions)
            {
                if (function.IsExternal) continue;
                
                foreach (var block in function.Blocks)
                {
                    EliminateCommonSubexpressions(block);
                }
            }
            
            return ModificationCount > 0;
        }
        
        private void EliminateCommonSubexpressions(BasicBlock block)
        {
            var expressions = new Dictionary<string, IRValue>();

            for (int i = 0; i < block.Instructions.Count; i++)
            {
                var inst = block.Instructions[i];

                if (inst is IRBinaryOp binaryOp)
                {
                    var key = $"{binaryOp.Operation}_{binaryOp.Left.Name}_{binaryOp.Right.Name}";

                    if (expressions.ContainsKey(key))
                    {
                        // Found a duplicate expression
                        var replacement = expressions[key];

                        // If the current instruction is a named destination (actual variable, not a temp),
                        // we should NOT remove it. Instead, convert to an assignment.
                        if (IsNamedVariable(binaryOp.Name))
                        {
                            // Convert to assignment: target = existingResult
                            var targetVar = new IRVariable(binaryOp.Name, binaryOp.Type);
                            block.Instructions[i] = new IRAssignment(targetVar, replacement);
                            ReportModification();
                        }
                        else
                        {
                            // Temp variable - safe to remove and replace uses
                            ReplaceAllUses(block, binaryOp, replacement);
                            block.Instructions.RemoveAt(i);
                            i--;
                            ReportModification();
                        }
                    }
                    else
                    {
                        expressions[key] = binaryOp;
                    }
                }
            }
        }

        // Check if a name represents a real variable (not a temp)
        private bool IsNamedVariable(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            // Temp names typically start with _tmp, _t, or are like "t0", "t1", etc.
            if (name.StartsWith("_tmp", StringComparison.OrdinalIgnoreCase)) return false;
            if (name.StartsWith("_t", StringComparison.OrdinalIgnoreCase) && name.Length > 2 && char.IsDigit(name[2])) return false;
            // Also check for temp patterns like "t0", "t1"
            if (name.Length >= 2 && name[0] == 't' && char.IsDigit(name[1])) return false;
            return true;
        }
        
        private void ReplaceAllUses(BasicBlock block, IRValue oldValue, IRValue newValue)
        {
            foreach (var inst in block.Instructions)
            {
                if (inst is IRBinaryOp binaryOp)
                {
                    if (ReferenceEquals(binaryOp.Left, oldValue))
                        binaryOp.Left = newValue;
                    if (ReferenceEquals(binaryOp.Right, oldValue))
                        binaryOp.Right = newValue;
                }
                else if (inst is IRUnaryOp unaryOp)
                {
                    if (ReferenceEquals(unaryOp.Operand, oldValue))
                        unaryOp.Operand = newValue;
                }
                else if (inst is IRStore store)
                {
                    if (ReferenceEquals(store.Value, oldValue))
                        store.Value = newValue;
                }
                else if (inst is IRAssignment assignment)
                {
                    if (ReferenceEquals(assignment.Value, oldValue))
                        assignment.Value = newValue;
                }
            }
        }
    }
    
    /// <summary>
    /// Loop invariant code motion - move loop-invariant code outside loops
    /// </summary>
    public class LoopInvariantCodeMotionPass : OptimizationPass
    {
        public LoopInvariantCodeMotionPass() : base("Loop Invariant Code Motion") { }
        
        public override bool Run(IRModule module)
        {
            ModificationCount = 0;
            
            foreach (var function in module.Functions)
            {
                if (function.IsExternal) continue;
                
                var cfg = new ControlFlowGraph(function);
                cfg.Build();
                cfg.ComputeDominators();
                cfg.IdentifyLoops();
                
                foreach (var loop in cfg.NaturalLoops)
                {
                    HoistInvariants(loop, cfg);
                }
            }
            
            return ModificationCount > 0;
        }
        
        private void HoistInvariants(List<BasicBlock> loop, ControlFlowGraph cfg)
        {
            var loopSet = new HashSet<BasicBlock>(loop);
            var header = loop.FirstOrDefault(b => b.Predecessors.Any(p => !loopSet.Contains(p)));
            
            if (header == null) return;
            
            // Find preheader (block before loop header)
            var preheader = header.Predecessors.FirstOrDefault(p => !loopSet.Contains(p));
            if (preheader == null) return;
            
            var invariants = new HashSet<IRInstruction>();
            
            // Find loop-invariant instructions
            bool changed = true;
            while (changed)
            {
                changed = false;
                
                foreach (var block in loop)
                {
                    foreach (var inst in block.Instructions)
                    {
                        if (IsLoopInvariant(inst, loopSet, invariants))
                        {
                            if (invariants.Add(inst))
                            {
                                changed = true;
                            }
                        }
                    }
                }
            }
            
            // Move invariants to preheader
            foreach (var block in loop)
            {
                for (int i = block.Instructions.Count - 1; i >= 0; i--)
                {
                    var inst = block.Instructions[i];
                    
                    if (invariants.Contains(inst))
                    {
                        block.Instructions.RemoveAt(i);
                        
                        // Insert before preheader's terminator
                        int insertPos = preheader.Instructions.Count;
                        if (insertPos > 0 && preheader.Instructions[insertPos - 1] is IRBranch)
                            insertPos--;
                        
                        preheader.Instructions.Insert(insertPos, inst);
                        ReportModification();
                    }
                }
            }
        }
        
        private bool IsLoopInvariant(IRInstruction inst, HashSet<BasicBlock> loop, HashSet<IRInstruction> knownInvariants)
        {
            // Terminators and side-effect instructions are not invariant
            if (inst is IRBranch || inst is IRConditionalBranch || inst is IRReturn ||
                inst is IRStore || inst is IRCall)
            {
                return false;
            }
            
            // Check if all operands are invariant
            if (inst is IRBinaryOp binaryOp)
            {
                return IsValueInvariant(binaryOp.Left, loop, knownInvariants) &&
                       IsValueInvariant(binaryOp.Right, loop, knownInvariants);
            }
            else if (inst is IRUnaryOp unaryOp)
            {
                return IsValueInvariant(unaryOp.Operand, loop, knownInvariants);
            }
            else if (inst is IRCompare compare)
            {
                return IsValueInvariant(compare.Left, loop, knownInvariants) &&
                       IsValueInvariant(compare.Right, loop, knownInvariants);
            }
            
            return false;
        }
        
        private bool IsValueInvariant(IRValue value, HashSet<BasicBlock> loop, HashSet<IRInstruction> knownInvariants)
        {
            if (value is IRConstant)
                return true;
            
            if (value is IRVariable variable)
            {
                // Parameter variables are invariant
                if (variable.IsParameter)
                    return true;
                
                // Global variables could change
                if (variable.IsGlobal)
                    return false;
            }
            
            // Check if the defining instruction is a known invariant
            if (value is IRInstruction inst)
            {
                return !loop.Contains(inst.ParentBlock) || knownInvariants.Contains(inst);
            }
            
            return false;
        }
    }
    
    /// <summary>
    /// Strength reduction - replace expensive operations with cheaper equivalents
    /// </summary>
    public class StrengthReductionPass : OptimizationPass
    {
        public StrengthReductionPass() : base("Strength Reduction") { }
        
        public override bool Run(IRModule module)
        {
            ModificationCount = 0;
            
            foreach (var function in module.Functions)
            {
                if (function.IsExternal) continue;
                
                foreach (var block in function.Blocks)
                {
                    ReduceStrength(block);
                }
            }
            
            return ModificationCount > 0;
        }
        
        private void ReduceStrength(BasicBlock block)
        {
            for (int i = 0; i < block.Instructions.Count; i++)
            {
                var inst = block.Instructions[i];
                
                if (inst is IRBinaryOp binaryOp)
                {
                    var reduced = TryReduceBinary(binaryOp);
                    if (reduced != null)
                    {
                        block.Instructions[i] = reduced;
                        ReportModification();
                    }
                }
            }
        }
        
        private IRInstruction TryReduceBinary(IRBinaryOp op)
        {
            // Multiplication by power of 2 Ã¢â€ â€™ shift
            if (op.Operation == BinaryOpKind.Mul)
            {
                if (op.Right is IRConstant constant && constant.Value is int power)
                {
                    if (IsPowerOfTwo(power))
                    {
                        int shift = (int)Math.Log(power, 2);
                        var shiftAmount = new IRConstant(shift, op.Right.Type);
                        return new IRBinaryOp(op.Name, BinaryOpKind.Shl, op.Left, shiftAmount, op.Type);
                    }
                }
            }
            
            // Division by power of 2 Ã¢â€ â€™ shift
            if (op.Operation == BinaryOpKind.Div)
            {
                if (op.Right is IRConstant constant && constant.Value is int power)
                {
                    if (IsPowerOfTwo(power))
                    {
                        int shift = (int)Math.Log(power, 2);
                        var shiftAmount = new IRConstant(shift, op.Right.Type);
                        return new IRBinaryOp(op.Name, BinaryOpKind.Shr, op.Left, shiftAmount, op.Type);
                    }
                }
            }
            
            // Modulo by power of 2 Ã¢â€ â€™ bitwise AND
// NOTE: Modulo by power of 2 -> bitwise AND optimization is disabled
            // because BinaryOpKind.And maps to logical && in C#, not bitwise &
            // TODO: Add BinaryOpKind.BitwiseAnd to properly support this optimization

            return null;
        }
        
        private bool IsPowerOfTwo(int n)
        {
            return n > 0 && (n & (n - 1)) == 0;
        }
    }
    
    /// <summary>
    /// Optimization pipeline - runs multiple passes in sequence
    /// </summary>
    public class OptimizationPipeline
    {
        private readonly List<OptimizationPass> _passes;
        private int _maxIterations;
        
        public OptimizationPipeline(int maxIterations = 10)
        {
            _passes = new List<OptimizationPass>();
            _maxIterations = maxIterations;
        }
        
        public void AddPass(OptimizationPass pass)
        {
            _passes.Add(pass);
        }
        
        public void AddStandardPasses()
        {
            AddPass(new ConstantFoldingPass());
            // ConstantPropagationPass disabled - incorrectly propagates across control flow merges
            // AddPass(new ConstantPropagationPass());
            AddPass(new CopyPropagationPass());
            AddPass(new DeadCodeEliminationPass());
            AddPass(new CommonSubexpressionEliminationPass());
            AddPass(new StrengthReductionPass());
            AddPass(new PeepholeOptimizationPass());
        }

        public void AddAggressivePasses()
        {
            AddStandardPasses();
            AddPass(new LoopInvariantCodeMotionPass());
            AddPass(new FunctionInliningPass());
            AddPass(new TailCallOptimizationPass());
            AddPass(new AlgebraicSimplificationPass());
            AddPass(new LoopFusionPass());  // Fuse adjacent loops before unrolling
            AddPass(new LoopUnrollingPass(4));  // 4x unrolling
            AddPass(new InductionVariablePass());
        }
        
        public OptimizationResult Run(IRModule module)
        {
            var result = new OptimizationResult();
            
            for (int iteration = 0; iteration < _maxIterations; iteration++)
            {
                bool anyChanges = false;
                
                foreach (var pass in _passes)
                {
                    bool changed = pass.Run(module);
                    
                    result.PassResults.Add(new PassResult
                    {
                        PassName = pass.Name,
                        Iteration = iteration,
                        ModificationCount = pass.ModificationCount,
                        MadeChanges = changed
                    });
                    
                    if (changed)
                    {
                        anyChanges = true;
                        result.TotalModifications += pass.ModificationCount;
                    }
                }
                
                if (!anyChanges)
                {
                    result.IterationsRun = iteration + 1;
                    break;
                }
                
                result.IterationsRun = iteration + 1;
            }
            
            return result;
        }
    }
    
    public class OptimizationResult
    {
        public int IterationsRun { get; set; }
        public int TotalModifications { get; set; }
        public List<PassResult> PassResults { get; set; }
        
        public OptimizationResult()
        {
            PassResults = new List<PassResult>();
        }
        
        public override string ToString()
        {
            return $"Ran {IterationsRun} iterations, made {TotalModifications} total modifications";
        }
    }
    
    public class PassResult
    {
        public string PassName { get; set; }
        public int Iteration { get; set; }
        public int ModificationCount { get; set; }
        public bool MadeChanges { get; set; }

        public override string ToString()
        {
            return $"[Iteration {Iteration}] {PassName}: {ModificationCount} modifications";
        }
    }

    /// <summary>
    /// Function inlining - inline small functions to reduce call overhead
    /// </summary>
    public class FunctionInliningPass : OptimizationPass
    {
        private readonly int _maxInlineSize;
        private readonly int _maxInlineDepth;

        public FunctionInliningPass(int maxInlineSize = 10, int maxInlineDepth = 3)
            : base("Function Inlining")
        {
            _maxInlineSize = maxInlineSize;
            _maxInlineDepth = maxInlineDepth;
        }

        public override bool Run(IRModule module)
        {
            ModificationCount = 0;

            // Build a map of inlineable functions
            var inlineableFunctions = new Dictionary<string, IRFunction>();
            foreach (var func in module.Functions)
            {
                if (IsInlineable(func))
                {
                    inlineableFunctions[func.Name] = func;
                }
            }

            // Process each function looking for call sites to inline
            foreach (var function in module.Functions)
            {
                if (function.IsExternal) continue;

                foreach (var block in function.Blocks)
                {
                    InlineCallsInBlock(block, inlineableFunctions, function, 0);
                }
            }

            return ModificationCount > 0;
        }

        private bool IsInlineable(IRFunction func)
        {
            // Don't inline external functions
            if (func.IsExternal) return false;

            // Don't inline recursive functions (simple check)
            if (ContainsSelfCall(func)) return false;

            // Don't inline functions with too many instructions
            int instructionCount = func.Blocks.Sum(b => b.Instructions.Count);
            if (instructionCount > _maxInlineSize) return false;

            // Don't inline functions with complex control flow (multiple blocks)
            if (func.Blocks.Count > 2) return false;

            // Don't inline functions with exception handling
            // (would need to check for try/catch in IR)

            return true;
        }

        private bool ContainsSelfCall(IRFunction func)
        {
            foreach (var block in func.Blocks)
            {
                foreach (var inst in block.Instructions)
                {
                    if (inst is IRCall call && call.FunctionName == func.Name)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private void InlineCallsInBlock(
            BasicBlock block,
            Dictionary<string, IRFunction> inlineableFunctions,
            IRFunction currentFunction,
            int depth)
        {
            if (depth >= _maxInlineDepth) return;

            for (int i = 0; i < block.Instructions.Count; i++)
            {
                if (block.Instructions[i] is IRCall call &&
                    inlineableFunctions.TryGetValue(call.FunctionName, out var targetFunc))
                {
                    // Inline the function
                    var inlinedInstructions = InlineFunction(call, targetFunc, currentFunction);
                    if (inlinedInstructions != null)
                    {
                        // Replace the call with the inlined instructions
                        block.Instructions.RemoveAt(i);
                        block.Instructions.InsertRange(i, inlinedInstructions);
                        i += inlinedInstructions.Count - 1;
                        ReportModification();
                    }
                }
            }
        }

        private List<IRInstruction> InlineFunction(IRCall call, IRFunction targetFunc, IRFunction caller)
        {
            var result = new List<IRInstruction>();
            var paramMapping = new Dictionary<string, IRValue>();

            // Map parameters to arguments
            for (int i = 0; i < targetFunc.Parameters.Count && i < call.Arguments.Count; i++)
            {
                paramMapping[targetFunc.Parameters[i].Name] = call.Arguments[i];
            }

            // Clone and transform instructions from the target function
            string prefix = $"_inline_{call.Name}_";
            int tempCounter = 0;

            foreach (var block in targetFunc.Blocks)
            {
                foreach (var inst in block.Instructions)
                {
                    var cloned = CloneAndRemap(inst, paramMapping, prefix, ref tempCounter, call.Name);
                    if (cloned != null)
                    {
                        // Handle return - assign to the call's result variable
                        if (cloned is IRReturn ret && ret.Value != null)
                        {
                            if (!string.IsNullOrEmpty(call.Name))
                            {
                                var resultVar = new IRVariable(call.Name, call.Type);
                                result.Add(new IRAssignment(resultVar, ret.Value));
                            }
                        }
                        else if (!(cloned is IRReturn))
                        {
                            result.Add(cloned);
                        }
                    }
                }
            }

            return result;
        }

        private IRInstruction CloneAndRemap(
            IRInstruction inst,
            Dictionary<string, IRValue> paramMapping,
            string prefix,
            ref int tempCounter,
            string resultName)
        {
            // Clone instruction and remap variable references
            switch (inst)
            {
                case IRAssignment assign:
                    var newTarget = RemapValue(assign.Target, paramMapping, prefix, ref tempCounter) as IRVariable;
                    var newValue = RemapValue(assign.Value, paramMapping, prefix, ref tempCounter);
                    return new IRAssignment(newTarget ?? assign.Target, newValue);

                case IRBinaryOp binOp:
                    var newLeft = RemapValue(binOp.Left, paramMapping, prefix, ref tempCounter);
                    var newRight = RemapValue(binOp.Right, paramMapping, prefix, ref tempCounter);
                    return new IRBinaryOp($"{prefix}{tempCounter++}", binOp.Operation, newLeft, newRight, binOp.Type);

                case IRReturn ret:
                    var retVal = ret.Value != null
                        ? RemapValue(ret.Value, paramMapping, prefix, ref tempCounter)
                        : null;
                    return new IRReturn(retVal);

                default:
                    return inst;
            }
        }

        private IRValue RemapValue(
            IRValue value,
            Dictionary<string, IRValue> paramMapping,
            string prefix,
            ref int tempCounter)
        {
            if (value is IRVariable var)
            {
                if (paramMapping.TryGetValue(var.Name, out var mapped))
                {
                    return mapped;
                }
                // Rename local variables with prefix
                return new IRVariable($"{prefix}{var.Name}", var.Type);
            }
            return value;
        }
    }

    /// <summary>
    /// Tail call optimization - convert tail-recursive calls to loops
    /// </summary>
    public class TailCallOptimizationPass : OptimizationPass
    {
        public TailCallOptimizationPass() : base("Tail Call Optimization") { }

        public override bool Run(IRModule module)
        {
            ModificationCount = 0;

            foreach (var function in module.Functions)
            {
                if (function.IsExternal) continue;

                OptimizeTailCalls(function);
            }

            return ModificationCount > 0;
        }

        private void OptimizeTailCalls(IRFunction function)
        {
            foreach (var block in function.Blocks)
            {
                for (int i = 0; i < block.Instructions.Count; i++)
                {
                    var inst = block.Instructions[i];

                    // Look for pattern: call followed immediately by return of call result
                    if (inst is IRCall call && call.FunctionName == function.Name)
                    {
                        // Check if this is a tail call (followed by return)
                        if (i + 1 < block.Instructions.Count &&
                            block.Instructions[i + 1] is IRReturn ret &&
                            ret.Value is IRVariable retVar &&
                            retVar.Name == call.Name)
                        {
                            // Mark as tail call
                            call.IsTailCall = true;
                            ReportModification();
                        }
                        else if (i + 1 < block.Instructions.Count &&
                                 block.Instructions[i + 1] is IRReturn ret2 &&
                                 ret2.Value == call)
                        {
                            call.IsTailCall = true;
                            ReportModification();
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Constant propagation - propagate known constant values through the code
    /// </summary>
    public class ConstantPropagationPass : OptimizationPass
    {
        public ConstantPropagationPass() : base("Constant Propagation") { }

        public override bool Run(IRModule module)
        {
            ModificationCount = 0;

            foreach (var function in module.Functions)
            {
                if (function.IsExternal) continue;

                // Track known constant values
                var constants = new Dictionary<string, IRConstant>();

                foreach (var block in function.Blocks)
                {
                    PropagateInBlock(block, constants);
                }
            }

            return ModificationCount > 0;
        }

        private void PropagateInBlock(BasicBlock block, Dictionary<string, IRConstant> constants)
        {
            // Don't propagate constants into loop bodies or increment blocks
            // because loop variables change each iteration
            if (IsLoopBlock(block.Name))
            {
                constants.Clear();
            }

            for (int i = 0; i < block.Instructions.Count; i++)
            {
                var inst = block.Instructions[i];

                // Track constant assignments
                if (inst is IRAssignment assign)
                {
                    if (assign.Value is IRConstant constant && assign.Target is IRVariable target)
                    {
                        constants[target.Name] = constant;
                    }
                    else if (assign.Target is IRVariable t)
                    {
                        // Assignment of non-constant kills the known value
                        constants.Remove(t.Name);
                    }
                }

                // Propagate constants in expressions
                if (inst is IRBinaryOp binOp)
                {
                    bool changed = false;

                    if (binOp.Left is IRVariable leftVar && constants.TryGetValue(leftVar.Name, out var leftConst))
                    {
                        binOp.Left = leftConst;
                        changed = true;
                    }

                    if (binOp.Right is IRVariable rightVar && constants.TryGetValue(rightVar.Name, out var rightConst))
                    {
                        binOp.Right = rightConst;
                        changed = true;
                    }

                    if (changed) ReportModification();
                }

                if (inst is IRUnaryOp unaryOp)
                {
                    if (unaryOp.Operand is IRVariable opVar && constants.TryGetValue(opVar.Name, out var opConst))
                    {
                        unaryOp.Operand = opConst;
                        ReportModification();
                    }
                }

                if (inst is IRCall call)
                {
                    for (int j = 0; j < call.Arguments.Count; j++)
                    {
                        if (call.Arguments[j] is IRVariable argVar && constants.TryGetValue(argVar.Name, out var argConst))
                        {
                            call.Arguments[j] = argConst;
                            ReportModification();
                        }
                    }
                }

                if (inst is IRReturn ret && ret.Value is IRVariable retVar)
                {
                    if (constants.TryGetValue(retVar.Name, out var retConst))
                    {
                        ret.Value = retConst;
                        ReportModification();
                    }
                }

                if (inst is IRConditionalBranch condBr && condBr.Condition is IRVariable condVar)
                {
                    if (constants.TryGetValue(condVar.Name, out var condConst))
                    {
                        condBr.Condition = condConst;
                        ReportModification();
                    }
                }

                if (inst is IRStore store)
                {
                    if (store.Value is IRVariable storeVar && constants.TryGetValue(storeVar.Name, out var storeConst))
                    {
                        store.Value = storeConst;
                        ReportModification();
                    }
                    // Store to a variable kills its constant value
                    if (store.Address is IRVariable addrVar)
                    {
                        constants.Remove(addrVar.Name);
                    }
                }
            }
        }

        /// <summary>
        /// Check if a block is part of a loop (body, increment, or condition after first iteration)
        /// </summary>
        private bool IsLoopBlock(string blockName)
        {
            if (string.IsNullOrEmpty(blockName)) return false;

            // Loop body blocks
            if (blockName.Contains(".body")) return true;

            // Loop increment blocks
            if (blockName.Contains(".inc")) return true;

            // Loop condition blocks (may be re-entered)
            if (blockName.Contains(".cond")) return true;

            return false;
        }
    }

    /// <summary>
    /// Peephole optimizations - pattern-based local optimizations
    /// </summary>
    public class PeepholeOptimizationPass : OptimizationPass
    {
        public PeepholeOptimizationPass() : base("Peephole Optimization") { }

        public override bool Run(IRModule module)
        {
            ModificationCount = 0;

            foreach (var function in module.Functions)
            {
                if (function.IsExternal) continue;

                foreach (var block in function.Blocks)
                {
                    OptimizeBlock(block);
                }
            }

            return ModificationCount > 0;
        }

        private void OptimizeBlock(BasicBlock block)
        {
            bool changed;
            do
            {
                changed = false;

                for (int i = 0; i < block.Instructions.Count; i++)
                {
                    var inst = block.Instructions[i];

                    // Pattern: x + 0 or x - 0 -> x
                    if (inst is IRBinaryOp binOp)
                    {
                        var replacement = OptimizeBinaryOp(binOp);
                        if (replacement != null && replacement != inst)
                        {
                            block.Instructions[i] = replacement;
                            changed = true;
                            ReportModification();
                        }
                    }

                    // Pattern: Remove redundant assignments (x = x)
                    if (inst is IRAssignment assign)
                    {
                        if (assign.Target is IRVariable target &&
                            assign.Value is IRVariable source &&
                            target.Name == source.Name)
                        {
                            block.Instructions.RemoveAt(i);
                            i--;
                            changed = true;
                            ReportModification();
                        }
                    }

                    // Pattern: Double negation --x -> x
                    if (inst is IRUnaryOp unary && unary.Operation == UnaryOpKind.Neg)
                    {
                        if (unary.Operand is IRUnaryOp innerUnary && innerUnary.Operation == UnaryOpKind.Neg)
                        {
                            var newAssign = new IRAssignment(
                                new IRVariable(unary.Name, unary.Type),
                                innerUnary.Operand);
                            block.Instructions[i] = newAssign;
                            changed = true;
                            ReportModification();
                        }
                    }

                    // Pattern: Boolean not not -> identity
                    if (inst is IRUnaryOp notOp && notOp.Operation == UnaryOpKind.Not)
                    {
                        if (notOp.Operand is IRUnaryOp innerNot && innerNot.Operation == UnaryOpKind.Not)
                        {
                            var newAssign = new IRAssignment(
                                new IRVariable(notOp.Name, notOp.Type),
                                innerNot.Operand);
                            block.Instructions[i] = newAssign;
                            changed = true;
                            ReportModification();
                        }
                    }
                }

                // Pattern: Remove dead stores followed by another store to same location
                for (int i = 0; i < block.Instructions.Count - 1; i++)
                {
                    if (block.Instructions[i] is IRAssignment first &&
                        block.Instructions[i + 1] is IRAssignment second)
                    {
                        if (first.Target is IRVariable t1 &&
                            second.Target is IRVariable t2 &&
                            t1.Name == t2.Name)
                        {
                            // Check that the first value isn't used in the second
                            if (!ValueUsedIn(t1, second.Value))
                            {
                                block.Instructions.RemoveAt(i);
                                changed = true;
                                ReportModification();
                            }
                        }
                    }
                }

            } while (changed);
        }

        private IRInstruction OptimizeBinaryOp(IRBinaryOp binOp)
        {
            // x + 0 -> x
            if (binOp.Operation == BinaryOpKind.Add)
            {
                if (IsZero(binOp.Right))
                    return new IRAssignment(new IRVariable(binOp.Name, binOp.Type), binOp.Left);
                if (IsZero(binOp.Left))
                    return new IRAssignment(new IRVariable(binOp.Name, binOp.Type), binOp.Right);
            }

            // x - 0 -> x
            if (binOp.Operation == BinaryOpKind.Sub && IsZero(binOp.Right))
            {
                return new IRAssignment(new IRVariable(binOp.Name, binOp.Type), binOp.Left);
            }

            // x * 1 -> x
            if (binOp.Operation == BinaryOpKind.Mul)
            {
                if (IsOne(binOp.Right))
                    return new IRAssignment(new IRVariable(binOp.Name, binOp.Type), binOp.Left);
                if (IsOne(binOp.Left))
                    return new IRAssignment(new IRVariable(binOp.Name, binOp.Type), binOp.Right);
            }

            // x * 0 -> 0
            if (binOp.Operation == BinaryOpKind.Mul)
            {
                if (IsZero(binOp.Right) || IsZero(binOp.Left))
                    return new IRAssignment(
                        new IRVariable(binOp.Name, binOp.Type),
                        new IRConstant(0, binOp.Type));
            }

            // x / 1 -> x
            if (binOp.Operation == BinaryOpKind.Div && IsOne(binOp.Right))
            {
                return new IRAssignment(new IRVariable(binOp.Name, binOp.Type), binOp.Left);
            }

            // x - x -> 0
            if (binOp.Operation == BinaryOpKind.Sub &&
                binOp.Left is IRVariable left &&
                binOp.Right is IRVariable right &&
                left.Name == right.Name)
            {
                return new IRAssignment(
                    new IRVariable(binOp.Name, binOp.Type),
                    new IRConstant(0, binOp.Type));
            }

            // x / x -> 1 (when x != 0)
            if (binOp.Operation == BinaryOpKind.Div &&
                binOp.Left is IRVariable divLeft &&
                binOp.Right is IRVariable divRight &&
                divLeft.Name == divRight.Name)
            {
                return new IRAssignment(
                    new IRVariable(binOp.Name, binOp.Type),
                    new IRConstant(1, binOp.Type));
            }

            // x And True -> x, x And False -> False
            if (binOp.Operation == BinaryOpKind.And)
            {
                if (IsTrue(binOp.Right))
                    return new IRAssignment(new IRVariable(binOp.Name, binOp.Type), binOp.Left);
                if (IsTrue(binOp.Left))
                    return new IRAssignment(new IRVariable(binOp.Name, binOp.Type), binOp.Right);
                if (IsFalse(binOp.Right) || IsFalse(binOp.Left))
                    return new IRAssignment(
                        new IRVariable(binOp.Name, binOp.Type),
                        new IRConstant(false, new TypeInfo("Boolean", TypeKind.Primitive)));
            }

            // x Or False -> x, x Or True -> True
            if (binOp.Operation == BinaryOpKind.Or)
            {
                if (IsFalse(binOp.Right))
                    return new IRAssignment(new IRVariable(binOp.Name, binOp.Type), binOp.Left);
                if (IsFalse(binOp.Left))
                    return new IRAssignment(new IRVariable(binOp.Name, binOp.Type), binOp.Right);
                if (IsTrue(binOp.Right) || IsTrue(binOp.Left))
                    return new IRAssignment(
                        new IRVariable(binOp.Name, binOp.Type),
                        new IRConstant(true, new TypeInfo("Boolean", TypeKind.Primitive)));
            }

            return binOp;
        }

        private bool IsZero(IRValue value)
        {
            if (value is IRConstant c)
            {
                if (c.Value is int i) return i == 0;
                if (c.Value is long l) return l == 0;
                if (c.Value is double d) return d == 0.0;
                if (c.Value is float f) return f == 0.0f;
            }
            return false;
        }

        private bool IsOne(IRValue value)
        {
            if (value is IRConstant c)
            {
                if (c.Value is int i) return i == 1;
                if (c.Value is long l) return l == 1;
                if (c.Value is double d) return d == 1.0;
                if (c.Value is float f) return f == 1.0f;
            }
            return false;
        }

        private bool IsTrue(IRValue value)
        {
            return value is IRConstant c && c.Value is bool b && b;
        }

        private bool IsFalse(IRValue value)
        {
            return value is IRConstant c && c.Value is bool b && !b;
        }

        private bool ValueUsedIn(IRVariable var, IRValue value)
        {
            if (value is IRVariable v && v.Name == var.Name) return true;
            if (value is IRBinaryOp bin)
            {
                return ValueUsedIn(var, bin.Left) || ValueUsedIn(var, bin.Right);
            }
            if (value is IRUnaryOp un)
            {
                return ValueUsedIn(var, un.Operand);
            }
            return false;
        }
    }

    /// <summary>
    /// Algebraic simplification - simplify complex expressions
    /// </summary>
    public class AlgebraicSimplificationPass : OptimizationPass
    {
        public AlgebraicSimplificationPass() : base("Algebraic Simplification") { }

        public override bool Run(IRModule module)
        {
            ModificationCount = 0;

            foreach (var function in module.Functions)
            {
                if (function.IsExternal) continue;

                foreach (var block in function.Blocks)
                {
                    SimplifyBlock(block);
                }
            }

            return ModificationCount > 0;
        }

        private void SimplifyBlock(BasicBlock block)
        {
            for (int i = 0; i < block.Instructions.Count; i++)
            {
                var inst = block.Instructions[i];

                if (inst is IRBinaryOp binOp)
                {
                    var simplified = SimplifyBinaryOp(binOp);
                    if (simplified != binOp)
                    {
                        block.Instructions[i] = simplified;
                        ReportModification();
                    }
                }
            }
        }

        private IRInstruction SimplifyBinaryOp(IRBinaryOp binOp)
        {
            // (a + b) - b -> a
            if (binOp.Operation == BinaryOpKind.Sub && binOp.Left is IRBinaryOp leftAdd)
            {
                if (leftAdd.Operation == BinaryOpKind.Add &&
                    leftAdd.Right is IRVariable addRight &&
                    binOp.Right is IRVariable subRight &&
                    addRight.Name == subRight.Name)
                {
                    return new IRAssignment(new IRVariable(binOp.Name, binOp.Type), leftAdd.Left);
                }
            }

            // (a - b) + b -> a
            if (binOp.Operation == BinaryOpKind.Add && binOp.Left is IRBinaryOp leftSub)
            {
                if (leftSub.Operation == BinaryOpKind.Sub &&
                    leftSub.Right is IRVariable subRightVar &&
                    binOp.Right is IRVariable addRightVar &&
                    subRightVar.Name == addRightVar.Name)
                {
                    return new IRAssignment(new IRVariable(binOp.Name, binOp.Type), leftSub.Left);
                }
            }

            // (a * b) / b -> a (when b != 0)
            if (binOp.Operation == BinaryOpKind.Div && binOp.Left is IRBinaryOp leftMul)
            {
                if (leftMul.Operation == BinaryOpKind.Mul &&
                    leftMul.Right is IRVariable mulRight &&
                    binOp.Right is IRVariable divRight &&
                    mulRight.Name == divRight.Name)
                {
                    return new IRAssignment(new IRVariable(binOp.Name, binOp.Type), leftMul.Left);
                }
            }

            // 2 * x -> x + x (sometimes faster)
            if (binOp.Operation == BinaryOpKind.Mul)
            {
                if (binOp.Left is IRConstant c && c.Value is int i && i == 2)
                {
                    return new IRBinaryOp(binOp.Name, BinaryOpKind.Add, binOp.Right, binOp.Right, binOp.Type);
                }
                if (binOp.Right is IRConstant c2 && c2.Value is int i2 && i2 == 2)
                {
                    return new IRBinaryOp(binOp.Name, BinaryOpKind.Add, binOp.Left, binOp.Left, binOp.Type);
                }
            }

            return binOp;
        }
    }

    /// <summary>
    /// Loop unrolling - unroll small loops to reduce loop overhead
    /// </summary>
    public class LoopUnrollingPass : OptimizationPass
    {
        private readonly int _unrollFactor;
        private readonly int _maxBodySize;

        public LoopUnrollingPass(int unrollFactor = 4, int maxBodySize = 20)
            : base("Loop Unrolling")
        {
            _unrollFactor = unrollFactor;
            _maxBodySize = maxBodySize;
        }

        public override bool Run(IRModule module)
        {
            ModificationCount = 0;

            foreach (var function in module.Functions)
            {
                if (function.IsExternal) continue;

                var cfg = new ControlFlowGraph(function);
                cfg.Build();
                cfg.ComputeDominators();
                cfg.IdentifyLoops();

                foreach (var loop in cfg.NaturalLoops.ToList())
                {
                    if (CanUnroll(loop, function))
                    {
                        UnrollLoop(loop, function);
                    }
                }
            }

            return ModificationCount > 0;
        }

        private bool CanUnroll(List<BasicBlock> loop, IRFunction function)
        {
            if (loop.Count == 0) return false;

            // Find the loop header and get loop info
            var header = loop.FirstOrDefault(b => b.Name.Contains(".cond") || b.Name.Contains("for.cond") || b.Name.Contains("while.cond"));
            if (header == null) return false;

            // Check loop body size
            int totalInstructions = loop.Sum(b => b.Instructions.Count);
            if (totalInstructions > _maxBodySize) return false;

            // Don't unroll loops with function calls (side effects)
            foreach (var block in loop)
            {
                foreach (var inst in block.Instructions)
                {
                    if (inst is IRCall) return false;
                }
            }

            // Check for constant trip count
            var tripCount = GetConstantTripCount(loop, header);
            if (tripCount == null || tripCount < _unrollFactor) return false;

            // Don't unroll loops with complex control flow (multiple exits)
            int exitCount = 0;
            foreach (var block in loop)
            {
                foreach (var succ in block.Successors)
                {
                    if (!loop.Contains(succ)) exitCount++;
                }
            }
            if (exitCount > 1) return false;

            return true;
        }

        private int? GetConstantTripCount(List<BasicBlock> loop, BasicBlock header)
        {
            // Look for pattern: compare loop variable against constant
            foreach (var inst in header.Instructions)
            {
                if (inst is IRCompare compare)
                {
                    // Check if one operand is a constant
                    if (compare.Right is IRConstant endConst && endConst.Value is int endValue)
                    {
                        // Try to find the initial value from before the loop
                        var initValue = FindInitialValue(loop, compare.Left);
                        if (initValue.HasValue)
                        {
                            // Calculate trip count based on comparison type
                            return compare.Comparison switch
                            {
                                CompareKind.Le => endValue - initValue.Value + 1,
                                CompareKind.Lt => endValue - initValue.Value,
                                CompareKind.Ge => initValue.Value - endValue + 1,
                                CompareKind.Gt => initValue.Value - endValue,
                                _ => null
                            };
                        }
                    }
                }
            }
            return null;
        }

        private int? FindInitialValue(List<BasicBlock> loop, IRValue loopVar)
        {
            if (loopVar is IRVariable variable)
            {
                // Look in predecessor blocks (before loop)
                var header = loop.FirstOrDefault(b => b.Predecessors.Any(p => !loop.Contains(p)));
                if (header != null)
                {
                    foreach (var pred in header.Predecessors)
                    {
                        if (loop.Contains(pred)) continue;

                        // Search backwards for assignment to loop variable
                        for (int i = pred.Instructions.Count - 1; i >= 0; i--)
                        {
                            if (pred.Instructions[i] is IRAssignment assign &&
                                assign.Target is IRVariable target &&
                                target.Name == variable.Name &&
                                assign.Value is IRConstant initConst &&
                                initConst.Value is int initValue)
                            {
                                return initValue;
                            }
                        }
                    }
                }
            }
            return null;
        }

        private void UnrollLoop(List<BasicBlock> loop, IRFunction function)
        {
            // Find loop structure
            var header = loop.FirstOrDefault(b => b.Name.Contains(".cond"));
            var body = loop.FirstOrDefault(b => b.Name.Contains(".body"));
            var increment = loop.FirstOrDefault(b => b.Name.Contains(".inc"));

            if (header == null || body == null) return;

            // Get the loop variable name
            string loopVarName = null;
            foreach (var inst in header.Instructions)
            {
                if (inst is IRCompare cmp && cmp.Left is IRVariable v)
                {
                    loopVarName = v.Name;
                    break;
                }
            }
            if (loopVarName == null) return;

            // Get increment amount (default to 1)
            int incrementAmount = 1;
            if (increment != null)
            {
                foreach (var inst in increment.Instructions)
                {
                    if (inst is IRBinaryOp binOp &&
                        binOp.Operation == BinaryOpKind.Add &&
                        binOp.Right is IRConstant incConst &&
                        incConst.Value is int incVal)
                    {
                        incrementAmount = incVal;
                        break;
                    }
                }
            }

            // Clone body instructions for unrolling
            var originalBodyInstructions = new List<IRInstruction>(body.Instructions);

            // Remove the branch at end of body if present
            if (originalBodyInstructions.Count > 0 &&
                originalBodyInstructions[originalBodyInstructions.Count - 1] is IRBranch)
            {
                originalBodyInstructions.RemoveAt(originalBodyInstructions.Count - 1);
            }

            // Create unrolled body instructions
            var unrolledInstructions = new List<IRInstruction>();
            int tempCounter = 0;

            for (int unroll = 0; unroll < _unrollFactor; unroll++)
            {
                foreach (var inst in originalBodyInstructions)
                {
                    var cloned = CloneInstruction(inst, $"_u{unroll}_", ref tempCounter);
                    if (cloned != null)
                    {
                        unrolledInstructions.Add(cloned);
                    }
                }

                // Add increment for this iteration (except last which goes through normal increment)
                if (unroll < _unrollFactor - 1 && increment != null)
                {
                    foreach (var inst in increment.Instructions)
                    {
                        if (inst is IRBranch) continue;
                        var cloned = CloneInstruction(inst, $"_u{unroll}_", ref tempCounter);
                        if (cloned != null)
                        {
                            unrolledInstructions.Add(cloned);
                        }
                    }
                }
            }

            // Replace body instructions
            body.Instructions.Clear();
            body.Instructions.AddRange(unrolledInstructions);

            // Add back the branch to increment
            if (increment != null)
            {
                body.Instructions.Add(new IRBranch(increment));
            }
            else
            {
                body.Instructions.Add(new IRBranch(header));
            }

            // Update loop increment to multiply by unroll factor
            if (increment != null)
            {
                for (int i = 0; i < increment.Instructions.Count; i++)
                {
                    var inst = increment.Instructions[i];
                    if (inst is IRBinaryOp binOp &&
                        binOp.Operation == BinaryOpKind.Add &&
                        binOp.Left is IRVariable leftVar &&
                        leftVar.Name == loopVarName)
                    {
                        // Change increment to: i = i + (incrementAmount * unrollFactor)
                        var newIncrement = new IRConstant(incrementAmount * _unrollFactor, binOp.Right.Type);
                        increment.Instructions[i] = new IRBinaryOp(
                            binOp.Name,
                            BinaryOpKind.Add,
                            binOp.Left,
                            newIncrement,
                            binOp.Type);
                        break;
                    }
                }
            }

            ReportModification();
        }

        private IRInstruction CloneInstruction(IRInstruction inst, string prefix, ref int tempCounter)
        {
            switch (inst)
            {
                case IRAssignment assign:
                    return new IRAssignment(
                        CloneVariable(assign.Target, prefix),
                        CloneValue(assign.Value, prefix));

                case IRBinaryOp binOp:
                    return new IRBinaryOp(
                        $"{prefix}t{tempCounter++}",
                        binOp.Operation,
                        CloneValue(binOp.Left, prefix),
                        CloneValue(binOp.Right, prefix),
                        binOp.Type);

                case IRUnaryOp unOp:
                    return new IRUnaryOp(
                        $"{prefix}t{tempCounter++}",
                        unOp.Operation,
                        CloneValue(unOp.Operand, prefix),
                        unOp.Type);

                case IRStore store:
                    return new IRStore(
                        CloneValue(store.Address, prefix),
                        CloneValue(store.Value, prefix));

                case IRLoad load:
                    return new IRLoad(
                        $"{prefix}t{tempCounter++}",
                        CloneValue(load.Address, prefix),
                        load.Type);

                default:
                    return inst;
            }
        }

        private IRVariable CloneVariable(IRVariable variable, string prefix)
        {
            if (variable == null) return null;
            // Don't rename loop variables or globals
            if (variable.IsGlobal || variable.IsParameter)
                return variable;
            return new IRVariable($"{prefix}{variable.Name}", variable.Type);
        }

        private IRValue CloneValue(IRValue value, string prefix)
        {
            if (value is IRVariable variable)
            {
                // Don't rename globals or parameters
                if (variable.IsGlobal || variable.IsParameter)
                    return variable;
                return new IRVariable($"{prefix}{variable.Name}", variable.Type);
            }
            return value;
        }
    }

    /// <summary>
    /// Induction variable strength reduction - optimize loop-dependent calculations
    /// </summary>
    public class InductionVariablePass : OptimizationPass
    {
        public InductionVariablePass() : base("Induction Variable Optimization") { }

        public override bool Run(IRModule module)
        {
            ModificationCount = 0;

            foreach (var function in module.Functions)
            {
                if (function.IsExternal) continue;

                var cfg = new ControlFlowGraph(function);
                cfg.Build();
                cfg.ComputeDominators();
                cfg.IdentifyLoops();

                foreach (var loop in cfg.NaturalLoops)
                {
                    OptimizeInductionVariables(loop, function);
                }
            }

            return ModificationCount > 0;
        }

        private void OptimizeInductionVariables(List<BasicBlock> loop, IRFunction function)
        {
            // Find basic induction variables (variables that are incremented by constant each iteration)
            var basicIVs = FindBasicInductionVariables(loop);

            // Find derived induction variables (linear functions of basic IVs)
            foreach (var block in loop)
            {
                for (int i = 0; i < block.Instructions.Count; i++)
                {
                    var inst = block.Instructions[i];

                    // Pattern: x = i * c (where i is basic IV, c is constant)
                    if (inst is IRBinaryOp binOp && binOp.Operation == BinaryOpKind.Mul)
                    {
                        IRVariable ivVar = null;
                        IRConstant constant = null;

                        if (binOp.Left is IRVariable leftVar && basicIVs.ContainsKey(leftVar.Name) &&
                            binOp.Right is IRConstant rightConst)
                        {
                            ivVar = leftVar;
                            constant = rightConst;
                        }
                        else if (binOp.Right is IRVariable rightVar && basicIVs.ContainsKey(rightVar.Name) &&
                                 binOp.Left is IRConstant leftConst)
                        {
                            ivVar = rightVar;
                            constant = leftConst;
                        }

                        if (ivVar != null && constant != null && constant.Value is int constVal)
                        {
                            // Replace multiplication with addition
                            // Create a derived IV that's updated each iteration
                            var derivedIV = new IRVariable($"_div_{binOp.Name}", binOp.Type);
                            var (increment, _) = basicIVs[ivVar.Name];
                            int derivedIncrement = increment * constVal;

                            // Find increment block and add update for derived IV
                            var incBlock = loop.FirstOrDefault(b => b.Name.Contains(".inc"));
                            if (incBlock != null)
                            {
                                // Add: derivedIV = derivedIV + derivedIncrement
                                var updateInst = new IRBinaryOp(
                                    derivedIV.Name,
                                    BinaryOpKind.Add,
                                    derivedIV,
                                    new IRConstant(derivedIncrement, constant.Type),
                                    binOp.Type);

                                // Insert before the branch
                                int insertPos = incBlock.Instructions.Count;
                                if (insertPos > 0 && incBlock.Instructions[insertPos - 1] is IRBranch)
                                    insertPos--;
                                incBlock.Instructions.Insert(insertPos, updateInst);

                                // Replace original multiplication with derived IV
                                block.Instructions[i] = new IRAssignment(
                                    new IRVariable(binOp.Name, binOp.Type),
                                    derivedIV);

                                ReportModification();
                            }
                        }
                    }
                }
            }
        }

        private Dictionary<string, (int increment, BasicBlock incBlock)> FindBasicInductionVariables(List<BasicBlock> loop)
        {
            var result = new Dictionary<string, (int, BasicBlock)>();

            foreach (var block in loop)
            {
                foreach (var inst in block.Instructions)
                {
                    // Pattern: i = i + c or i = i - c
                    if (inst is IRBinaryOp binOp &&
                        (binOp.Operation == BinaryOpKind.Add || binOp.Operation == BinaryOpKind.Sub))
                    {
                        if (binOp.Left is IRVariable leftVar &&
                            binOp.Name == leftVar.Name &&
                            binOp.Right is IRConstant constant &&
                            constant.Value is int increment)
                        {
                            int actualIncrement = binOp.Operation == BinaryOpKind.Sub ? -increment : increment;
                            result[leftVar.Name] = (actualIncrement, block);
                        }
                    }

                    // Pattern via assignment: i = i + c
                    if (inst is IRAssignment assign &&
                        assign.Target is IRVariable target &&
                        assign.Value is IRBinaryOp assignBinOp &&
                        (assignBinOp.Operation == BinaryOpKind.Add || assignBinOp.Operation == BinaryOpKind.Sub))
                    {
                        if (assignBinOp.Left is IRVariable innerLeftVar &&
                            innerLeftVar.Name == target.Name &&
                            assignBinOp.Right is IRConstant innerConst &&
                            innerConst.Value is int innerIncrement)
                        {
                            int actualIncrement = assignBinOp.Operation == BinaryOpKind.Sub ? -innerIncrement : innerIncrement;
                            result[target.Name] = (actualIncrement, block);
                        }
                    }
                }
            }

            return result;
        }
    }

    /// <summary>
    /// Loop fusion - fuse adjacent loops with same bounds to reduce loop overhead
    /// </summary>
    public class LoopFusionPass : OptimizationPass
    {
        public LoopFusionPass() : base("Loop Fusion") { }

        public override bool Run(IRModule module)
        {
            ModificationCount = 0;

            foreach (var function in module.Functions)
            {
                if (function.IsExternal) continue;

                var cfg = new ControlFlowGraph(function);
                cfg.Build();
                cfg.ComputeDominators();
                cfg.IdentifyLoops();

                // Find pairs of adjacent loops that can be fused
                var fusionCandidates = FindFusionCandidates(cfg.NaturalLoops, function);

                foreach (var (loop1, loop2) in fusionCandidates)
                {
                    if (CanFuse(loop1, loop2, function))
                    {
                        FuseLoops(loop1, loop2, function);
                    }
                }
            }

            return ModificationCount > 0;
        }

        private List<(List<BasicBlock>, List<BasicBlock>)> FindFusionCandidates(
            List<List<BasicBlock>> loops, IRFunction function)
        {
            var candidates = new List<(List<BasicBlock>, List<BasicBlock>)>();
            if (loops.Count < 2) return candidates;

            // Sort loops by their header position in the block list
            var sortedLoops = loops
                .Where(l => l.Count > 0)
                .OrderBy(l => function.Blocks.IndexOf(l.First()))
                .ToList();

            for (int i = 0; i < sortedLoops.Count - 1; i++)
            {
                var loop1 = sortedLoops[i];
                var loop2 = sortedLoops[i + 1];

                // Check if loops are adjacent (no blocks in between)
                if (AreLoopsAdjacent(loop1, loop2, function))
                {
                    candidates.Add((loop1, loop2));
                }
            }

            return candidates;
        }

        private bool AreLoopsAdjacent(List<BasicBlock> loop1, List<BasicBlock> loop2, IRFunction function)
        {
            // Find the exit block of loop1 and entry block of loop2
            var loop1Blocks = new HashSet<BasicBlock>(loop1);
            var loop2Blocks = new HashSet<BasicBlock>(loop2);

            // Find successors of loop1 that are not in loop1
            foreach (var block in loop1)
            {
                foreach (var succ in block.Successors)
                {
                    if (!loop1Blocks.Contains(succ))
                    {
                        // Check if this successor leads directly to loop2's header
                        if (loop2Blocks.Contains(succ) || succ.Successors.Any(s => loop2Blocks.Contains(s)))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private bool CanFuse(List<BasicBlock> loop1, List<BasicBlock> loop2, IRFunction function)
        {
            if (loop1.Count == 0 || loop2.Count == 0) return false;

            // Find loop headers
            var header1 = loop1.FirstOrDefault(b =>
                b.Name.Contains(".cond") || b.Name.Contains("for.cond") || b.Name.Contains("while.cond"));
            var header2 = loop2.FirstOrDefault(b =>
                b.Name.Contains(".cond") || b.Name.Contains("for.cond") || b.Name.Contains("while.cond"));

            if (header1 == null || header2 == null) return false;

            // Check if loops have same bounds
            var bounds1 = GetLoopBounds(header1);
            var bounds2 = GetLoopBounds(header2);

            if (bounds1 == null || bounds2 == null) return false;
            if (bounds1.Value.start != bounds2.Value.start || bounds1.Value.end != bounds2.Value.end) return false;

            // Check for data dependencies between loops
            if (HasDataDependency(loop1, loop2)) return false;

            // Don't fuse loops with function calls
            foreach (var block in loop1.Concat(loop2))
            {
                foreach (var inst in block.Instructions)
                {
                    if (inst is IRCall) return false;
                }
            }

            return true;
        }

        private (int start, int end)? GetLoopBounds(BasicBlock header)
        {
            foreach (var inst in header.Instructions)
            {
                if (inst is IRCompare compare)
                {
                    if (compare.Right is IRConstant endConst && endConst.Value is int endValue)
                    {
                        // Assume loop starts at 0 if we can't determine
                        return (0, endValue);
                    }
                }
            }
            return null;
        }

        private bool HasDataDependency(List<BasicBlock> loop1, List<BasicBlock> loop2)
        {
            // Collect variables written in loop1
            var writtenInLoop1 = new HashSet<string>();
            foreach (var block in loop1)
            {
                foreach (var inst in block.Instructions)
                {
                    if (inst is IRAssignment assign && assign.Target is IRVariable target)
                    {
                        writtenInLoop1.Add(target.Name);
                    }
                    else if (inst is IRBinaryOp binOp && !string.IsNullOrEmpty(binOp.Name))
                    {
                        writtenInLoop1.Add(binOp.Name);
                    }
                    else if (inst is IRArrayStore store)
                    {
                        if (store.Array is IRVariable arrayVar)
                        {
                            writtenInLoop1.Add(arrayVar.Name);
                        }
                    }
                }
            }

            // Check if loop2 reads from variables written in loop1
            foreach (var block in loop2)
            {
                foreach (var inst in block.Instructions)
                {
                    var usedVars = GetUsedVariables(inst);
                    if (usedVars.Any(v => writtenInLoop1.Contains(v)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private HashSet<string> GetUsedVariables(IRInstruction inst)
        {
            var used = new HashSet<string>();

            switch (inst)
            {
                case IRBinaryOp binOp:
                    if (binOp.Left is IRVariable leftVar) used.Add(leftVar.Name);
                    if (binOp.Right is IRVariable rightVar) used.Add(rightVar.Name);
                    break;
                case IRUnaryOp unaryOp:
                    if (unaryOp.Operand is IRVariable opVar) used.Add(opVar.Name);
                    break;
                case IRAssignment assign:
                    if (assign.Value is IRVariable valVar) used.Add(valVar.Name);
                    break;
                case IRCompare compare:
                    if (compare.Left is IRVariable cmpLeft) used.Add(cmpLeft.Name);
                    if (compare.Right is IRVariable cmpRight) used.Add(cmpRight.Name);
                    break;
                case IRGetElementPtr gep:
                    if (gep.BasePointer is IRVariable gepVar) used.Add(gepVar.Name);
                    foreach (var idx in gep.Indices)
                    {
                        if (idx is IRVariable gepIdx) used.Add(gepIdx.Name);
                    }
                    break;
                case IRArrayStore arrStore:
                    if (arrStore.Value is IRVariable storeVal) used.Add(storeVal.Name);
                    if (arrStore.Index is IRVariable storeIdx) used.Add(storeIdx.Name);
                    break;
            }

            return used;
        }

        private void FuseLoops(List<BasicBlock> loop1, List<BasicBlock> loop2, IRFunction function)
        {
            // Find loop body blocks (exclude header and latch)
            var body1 = loop1.Where(b =>
                !b.Name.Contains(".cond") && !b.Name.Contains(".latch")).ToList();
            var body2 = loop2.Where(b =>
                !b.Name.Contains(".cond") && !b.Name.Contains(".latch")).ToList();

            if (body1.Count == 0 || body2.Count == 0) return;

            // Append loop2's body instructions to loop1's body
            var lastBody1Block = body1.Last();
            var firstBody2Block = body2.First();

            // Clone instructions from loop2 body to loop1 body
            foreach (var block in body2)
            {
                foreach (var inst in block.Instructions.ToList())
                {
                    // Skip terminators
                    if (inst is IRBranch || inst is IRConditionalBranch) continue;

                    lastBody1Block.Instructions.Add(inst);
                }
            }

            // Remove loop2 blocks from function
            foreach (var block in loop2)
            {
                function.Blocks.Remove(block);
            }

            // Update branch target from loop1 exit to skip loop2
            var loop1Exit = loop1.FirstOrDefault(b =>
                b.Successors.Any(s => !loop1.Contains(s)));
            if (loop1Exit != null)
            {
                var terminator = loop1Exit.GetTerminator();
                if (terminator is IRConditionalBranch condBranch)
                {
                    // Update false branch to point past loop2
                    var loop2Exit = loop2.FirstOrDefault(b =>
                        b.Successors.Any(s => !loop2.Contains(s)));
                    if (loop2Exit != null)
                    {
                        var nextBlock = loop2Exit.Successors.FirstOrDefault(s => !loop2.Contains(s));
                        if (nextBlock != null)
                        {
                            condBranch.FalseTarget = nextBlock;
                        }
                    }
                }
            }

            ModificationCount++;
        }
    }
}
