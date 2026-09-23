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

        /// <summary>
        /// Rewrite every USE of <paramref name="oldValue"/> to <paramref name="newValue"/>
        /// across <paramref name="instructions"/>.
        ///
        /// <para><b>Why a pass that swaps an instruction MUST call this.</b> A pass that
        /// rewrites <c>block.Instructions[i]</c> in place produces a NEW <see cref="IRValue"/>
        /// object. Consumers elsewhere in the function still hold a reference to the DISCARDED
        /// one. Backends name temporaries by OBJECT IDENTITY — <c>ICodeGenerator.GetValueName</c>
        /// keys its <c>_valueNames</c> dictionary on the value instance and mints a fresh
        /// <c>t{N}</c> for any instance it has not seen — so an orphaned consumer silently
        /// renders an identifier that is never declared and never assigned. Carrying the old
        /// <c>Name</c> onto the replacement does NOT help: the emitted assignment then looks
        /// correct while the consumer still refers to a different object. That is exactly how
        /// strength reduction shipped <c>t0 = v &lt;&lt; 1; return t1;</c>.</para>
        ///
        /// <para>Uses reference equality throughout: two structurally identical operands are
        /// distinct values here, and only the one being replaced may be rewritten.</para>
        /// </summary>
        /// <summary>
        /// Carries the IDENTITY of a replaced value onto the value standing in for it, and returns
        /// the replacement so it can be used inline.
        ///
        /// <para><b>Why a pass that rewrites a value MUST call this.</b> Passing the old
        /// <c>Name</c> to the replacement's constructor is NOT enough — a value's identity is the
        /// name PLUS <see cref="IRValue.NamedAfterVariable"/>, which is what tells a backend that
        /// the result IS an assignment to that variable rather than a temp that shares its name.
        /// A replacement built without it defaults to false, so the C# and JavaScript backends
        /// stop recognising the store: the write to a CLASS MEMBER is emitted as a fresh local
        /// (<c>const K = ...</c>) on JavaScript and DROPPED ENTIRELY on C#, and the field silently
        /// keeps its old value. Nothing fails to compile and a plausible number is printed.</para>
        ///
        /// <para>MEASURED: <c>K = p * 2</c> on a Shared field printed the field's initial value,
        /// because strength reduction rewrote the multiply to a shift and the new node carried the
        /// name but not the flag. C++ and MSIL were unaffected — they do not consult it — so two
        /// backends were right and two were silently wrong.</para>
        ///
        /// <para><see cref="IRInstruction.SourceLine"/> rides along for the reason strength
        /// reduction already carried it by hand: the replacement stands in for the user's own
        /// statement, and dropping the line leaves <c>SourceLine 0</c> ("synthesized"), which makes
        /// a debug build emit a <c>#line</c> reset onto generated glue for a line the user wrote,
        /// so stepping lands in the wrong place.</para>
        ///
        /// <para>⚠ The NAME is deliberately NOT copied here: every call site already passes it to
        /// the replacement's constructor, so an assignment would act and change nothing. MEASURED —
        /// removing it left all 18 tests green, while corrupting it failed 11, so the tests are
        /// sensitive to the name without the line being needed. This helper carries only what was
        /// being LOST. A future caller that does not name its replacement wants a different
        /// signature, not a silent re-assignment here.</para>
        /// </summary>
        protected static T InheritIdentity<T>(T replacement, IRValue original) where T : IRValue
        {
            if (replacement == null || original == null) return replacement;

            replacement.NamedAfterVariable = original.NamedAfterVariable;
            replacement.SourceLine = original.SourceLine;
            return replacement;
        }

        protected static void ReplaceUses(IEnumerable<IRInstruction> instructions, IRValue oldValue, IRValue newValue)
        {
            if (instructions == null) return;
            foreach (var inst in instructions)
                ReplaceUsesIn(inst, oldValue, newValue);
        }

        private static void ReplaceInList(List<IRValue> operands, IRValue oldValue, IRValue newValue)
        {
            if (operands == null) return;
            for (int i = 0; i < operands.Count; i++)
                if (ReferenceEquals(operands[i], oldValue)) operands[i] = newValue;
        }

        /// <summary>
        /// One arm per IR node that CONSUMES a value. Definition slots are deliberately absent:
        /// <c>IRAssignment.Target</c> is an <see cref="IRVariable"/> being written, not a use.
        /// </summary>
        private static void ReplaceUsesIn(IRInstruction inst, IRValue oldValue, IRValue newValue)
        {
            switch (inst)
            {
                case IRBinaryOp binOp:
                    if (ReferenceEquals(binOp.Left, oldValue)) binOp.Left = newValue;
                    if (ReferenceEquals(binOp.Right, oldValue)) binOp.Right = newValue;
                    break;
                case IRUnaryOp unOp:
                    if (ReferenceEquals(unOp.Operand, oldValue)) unOp.Operand = newValue;
                    break;
                case IRCompare cmp:
                    if (ReferenceEquals(cmp.Left, oldValue)) cmp.Left = newValue;
                    if (ReferenceEquals(cmp.Right, oldValue)) cmp.Right = newValue;
                    break;
                case IRLoad load:
                    if (ReferenceEquals(load.Address, oldValue)) load.Address = newValue;
                    break;
                case IRStore store:
                    if (ReferenceEquals(store.Value, oldValue)) store.Value = newValue;
                    if (ReferenceEquals(store.Address, oldValue)) store.Address = newValue;
                    break;
                case IRGetElementPtr gep:
                    if (ReferenceEquals(gep.BasePointer, oldValue)) gep.BasePointer = newValue;
                    ReplaceInList(gep.Indices, oldValue, newValue);
                    break;
                case IRConditionalBranch condBr:
                    if (ReferenceEquals(condBr.Condition, oldValue)) condBr.Condition = newValue;
                    break;
                case IRSwitch sw:
                    if (ReferenceEquals(sw.Value, oldValue)) sw.Value = newValue;
                    if (sw.Cases != null)
                        for (int i = 0; i < sw.Cases.Count; i++)
                            if (ReferenceEquals(sw.Cases[i].CaseValue, oldValue))
                                sw.Cases[i] = (newValue, sw.Cases[i].Target);
                    if (sw.PatternCases != null)
                        foreach (var patternCase in sw.PatternCases)
                            ReplaceUsesInPattern(patternCase, oldValue, newValue);
                    break;
                case IRReturn ret:
                    if (ReferenceEquals(ret.Value, oldValue)) ret.Value = newValue;
                    break;
                case IRCall call:
                    if (ReferenceEquals(call.CalleeValue, oldValue)) call.CalleeValue = newValue;
                    ReplaceInList(call.Arguments, oldValue, newValue);
                    break;
                case IRCast cast:
                    if (ReferenceEquals(cast.Value, oldValue)) cast.Value = newValue;
                    break;
                case IRAssignment asg:
                    if (ReferenceEquals(asg.Value, oldValue)) asg.Value = newValue;
                    break;
                case IRArrayStore arrayStore:
                    if (ReferenceEquals(arrayStore.Array, oldValue)) arrayStore.Array = newValue;
                    if (ReferenceEquals(arrayStore.Index, oldValue)) arrayStore.Index = newValue;
                    if (ReferenceEquals(arrayStore.Value, oldValue)) arrayStore.Value = newValue;
                    break;
                case IRAwait await:
                    if (ReferenceEquals(await.Expression, oldValue)) await.Expression = newValue;
                    break;
                case IRYield yield:
                    if (ReferenceEquals(yield.Value, oldValue)) yield.Value = newValue;
                    break;
                case IRIndexerAccess indexerAccess:
                    if (ReferenceEquals(indexerAccess.Collection, oldValue)) indexerAccess.Collection = newValue;
                    ReplaceInList(indexerAccess.Indices, oldValue, newValue);
                    break;
                case IRIndexerStore indexerStore:
                    if (ReferenceEquals(indexerStore.Collection, oldValue)) indexerStore.Collection = newValue;
                    ReplaceInList(indexerStore.Indices, oldValue, newValue);
                    if (ReferenceEquals(indexerStore.Value, oldValue)) indexerStore.Value = newValue;
                    break;
                case IRForEach forEach:
                    if (ReferenceEquals(forEach.Collection, oldValue)) forEach.Collection = newValue;
                    break;
                case IRThrow thrown:
                    if (ReferenceEquals(thrown.Exception, oldValue)) thrown.Exception = newValue;
                    break;
                case IRNewObject newObject:
                    ReplaceInList(newObject.Arguments, oldValue, newValue);
                    break;
                case IRInstanceMethodCall instanceCall:
                    if (ReferenceEquals(instanceCall.Object, oldValue)) instanceCall.Object = newValue;
                    ReplaceInList(instanceCall.Arguments, oldValue, newValue);
                    break;
                case IRBaseMethodCall baseCall:
                    ReplaceInList(baseCall.Arguments, oldValue, newValue);
                    break;
                case IRFieldAccess fieldAccess:
                    if (ReferenceEquals(fieldAccess.Object, oldValue)) fieldAccess.Object = newValue;
                    break;
                case IRFieldStore fieldStore:
                    if (ReferenceEquals(fieldStore.Object, oldValue)) fieldStore.Object = newValue;
                    if (ReferenceEquals(fieldStore.Value, oldValue)) fieldStore.Value = newValue;
                    break;
                case IRTupleElement tupleElement:
                    if (ReferenceEquals(tupleElement.Tuple, oldValue)) tupleElement.Tuple = newValue;
                    break;
            }
        }

        private static void ReplaceUsesInPattern(IRPatternCase patternCase, IRValue oldValue, IRValue newValue)
        {
            if (patternCase == null) return;

            if (ReferenceEquals(patternCase.WhenGuard, oldValue)) patternCase.WhenGuard = newValue;

            switch (patternCase)
            {
                case IRRangePatternCase range:
                    if (ReferenceEquals(range.LowerBound, oldValue)) range.LowerBound = newValue;
                    if (ReferenceEquals(range.UpperBound, oldValue)) range.UpperBound = newValue;
                    break;
                case IRComparisonPatternCase comparison:
                    if (ReferenceEquals(comparison.CompareValue, oldValue)) comparison.CompareValue = newValue;
                    break;
                case IRConstantPatternCase constant:
                    if (ReferenceEquals(constant.Value, oldValue)) constant.Value = newValue;
                    break;
                case IROrPatternCase or:
                    if (or.Alternatives != null)
                        foreach (var alternative in or.Alternatives)
                            ReplaceUsesInPattern(alternative, oldValue, newValue);
                    break;
                case IRTuplePatternCase tuple:
                    if (tuple.Elements != null)
                        foreach (var element in tuple.Elements)
                            ReplaceUsesInPattern(element, oldValue, newValue);
                    break;
            }
        }
    }
    
    /// <summary>
    /// Constant folding - evaluate constant expressions at compile time
    /// </summary>
    /// <summary>
    /// Folds an <see cref="IRCast"/> of a constant, for LOSSLESS WIDENING conversions only.
    ///
    /// <para>⛔ NOT IN THE DEFAULT PIPELINE. It exists for
    /// <c>IRBuilder.BuildModuleScopeInitializer</c>, whose contract is "reduce to a single
    /// constant or refuse", and it is not registered anywhere else because nothing else needs it:
    /// folding <c>(double)7</c> in ordinary optimized code buys no measured benefit and changes
    /// the IR every backend sees.</para>
    ///
    /// <para>⚠ Stated honestly, because the tempting rationale is WRONG. The worry was that the
    /// base <c>EmitConstant</c> ends in <c>Value.ToString()</c>, so a Double constant of 7.0
    /// renders as <c>7</c> and <c>(double)7 / x</c> would become an INTEGER division. It does not:
    /// measured by adding this pass to the pipeline and running it, C# still prints 3.5 for both
    /// <c>7 / 2</c> and <c>7 / x</c> — the optimizer loops its passes to a fixpoint, so a
    /// constant/constant division folds away entirely, and in <c>7 / x</c> the surviving operand
    /// keeps its own cast (<c>7 / (double)(x)</c>) which still promotes. So this pass staying out
    /// of the pipeline is a SCOPE decision, not a safety one, and no test holds it.</para>
    ///
    /// <para>⛔ WIDENING ONLY, and only the three conversions that are EXACT: Integer→Long,
    /// Integer→Double (32 bits fit a 53-bit mantissa) and Single→Double. Integer→Single is not
    /// exact past 2^24, and Long→Double is not exact past 2^53.</para>
    ///
    /// <para>⚠ The widening-only restriction is, today, UNREACHABLE — measured: adding a
    /// Double→Integer arm to <see cref="TryWiden"/> leaves every test passing, because no
    /// narrowing IRCast reaches this pass from the one site that calls it. An assignment narrowing
    /// is folded earlier by <c>IRBuilder.TryConvertConstant</c> without a cast, and
    /// <c>CInt(...)</c> lowers to an IRCall. It stays as a fail-safe that no test can hold,
    /// because what it guards is real and expensive to get wrong — see the next paragraph.</para>
    ///
    /// <para>⛔ NARROWING IS NOT FOLDED, and that is not caution — the backends DISAGREE about it.
    /// Measured on <c>CInt(7.5)</c>, <c>CInt(8.5)</c>, <c>CInt(7.9)</c>, <c>CInt(-7.5)</c>:
    /// C# prints <c>8,8,8,-8</c> (rounds, the VB answer) while MSIL, JavaScript and C++ all print
    /// <c>7,8,7,-7</c> (truncate). Any single compile-time answer would therefore CHANGE one of
    /// them. That divergence is a real pre-existing defect, and it has to be settled for the
    /// backends at run time before a constant folder is allowed an opinion about it.</para>
    /// </summary>
    public class WideningCastFoldingPass : OptimizationPass
    {
        public WideningCastFoldingPass() : base("Widening Cast Folding") { }

        public override bool Run(IRModule module)
        {
            ModificationCount = 0;

            foreach (var function in module.Functions)
            {
                if (function.IsExternal) continue;
                foreach (var block in function.Blocks)
                    FoldBlock(block);
            }

            return ModificationCount > 0;
        }

        private void FoldBlock(BasicBlock block)
        {
            for (int i = 0; i < block.Instructions.Count; i++)
            {
                if (!(block.Instructions[i] is IRCast cast)) continue;
                if (!(cast.Value is IRConstant operand)) continue;

                var folded = TryWiden(operand.Value, cast.SourceType?.Name, cast.Type?.Name);
                if (folded == null) continue;

                var constant = new IRConstant(folded, cast.Type);
                ReplaceUses(block.Instructions, cast, constant);
                block.Instructions[i] = constant;
                ReportModification();
            }
        }

        /// <summary>
        /// The widened value, or null when the pair is not one of the three exact conversions.
        /// ⚠ The SOURCE type is checked as well as the CLR value: a constant carrying an int is
        /// only an Integer widening if the cast says it came from one.
        /// </summary>
        private static object TryWiden(object value, string sourceName, string targetName)
        {
            if (value == null || sourceName == null || targetName == null) return null;

            switch (sourceName)
            {
                case "Integer" when value is int i:
                    if (targetName == "Long") return (long)i;
                    if (targetName == "Double") return (double)i;
                    return null;
                case "Single" when value is float f:
                    if (targetName == "Double") return (double)f;
                    return null;
                default:
                    return null;
            }
        }
    }

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
        /// Replace all references to oldValue with newValue in the block.
        ///
        /// <para>Delegates to <see cref="OptimizationPass.ReplaceUses"/> — one implementation
        /// for every pass that swaps an instruction, per the repo rule that shared resolver
        /// logic changes once rather than per consumer. The block scope is unchanged from the
        /// hand-rolled version this replaced; what widened is NODE coverage, which previously
        /// stopped at eight consumer kinds and silently missed field stores, indexer accesses,
        /// array stores, casts, throws and instance-call receivers.</para>
        /// </summary>
        private void ReplaceAllReferences(BasicBlock block, IRValue oldValue, IRValue newValue)
        {
            ReplaceUses(block.Instructions, oldValue, newValue);
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
                    // `&` is VB's string concatenation, and FoldAdd's string branch already IS
                    // concatenation. Without this a module-scope `Dim S As String = "a" & "b"`
                    // has no constant to fold to and gets refused, because a global's initializer
                    // must be a constant. Mixed operands (`"a" & 5`) still fold to null here —
                    // FoldAdd matches string+string only — so the VB coercion is never guessed at.
                    BinaryOpKind.Concat => FoldAdd(left.Value, right.Value),
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

            // System.Decimal constants never fold here (spec 6.1): unlike the
            // arithmetic Fold* helpers, which return null for unmatched operand
            // types, this switch folds UNCONDITIONALLY — and CompareLt/CompareGt
            // blindly report false for type pairs outside int/long/float/double,
            // while CompareEq's Equals treats a mixed decimal/int pair (0.1m vs
            // 5) as unequal boxed types. Either path would MISCOMPILE a decimal
            // comparison (e.g. 0.1m < 0.2m folding to False). Skipping keeps the
            // exact comparison at runtime.
            if (left.Value is decimal || right.Value is decimal)
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
            catch (Exception ex) when (ex is FormatException || ex is OverflowException || ex is InvalidCastException || ex is ArithmeticException)
            {
                // Constant folding skipped for this expression: not foldable at compile time
            }
            
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
        
        // `/` is FLOATING-POINT division in VB.NET even on two integers: 7 / 2 is 3.5.
        // These two arms used C# integer division, so the CONSTANT path truncated
        // independently of the analyzer's result type — proven by the JS backend, which is
        // otherwise correct yet still printed 3 for `7 / 2` while printing 3.5 for `a / b`.
        // Must ship with the analyzer's Double result type: TryFoldBinary stamps op.Type onto
        // the folded IRConstant, so fixing either one alone yields a mismatched pair
        // (IRConstant(3 as int) typed Double, or IRConstant(3.5) typed Integer).
        // FoldIntDiv below is deliberately left truncating — `\` is the integer operator.
        private object FoldDiv(object a, object b)
        {
            if (a is int ia && b is int ib && ib != 0) return (double)ia / ib;
            if (a is long la && b is long lb && lb != 0) return (double)la / lb;
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

                // A CALL WRITES ITS ByRef ARGUMENTS. That write is invisible in this block —
                // there is no IRAssignment and no rename for it — so without this kill the
                // argument keeps whatever copy fact preceded the call and a later read folds
                // against the STALE value. `Dim n = 0 : Int32.TryParse("42", n) : If n = 42`
                // recorded `n -> 0`, survived the call, and folded the comparison to FALSE:
                // a silent miscompile on EVERY backend (the C# backend, whose `ref` signature
                // is correct, emitted `if (false)` just the same). Runs before the redefinition
                // bookkeeping below because the callee's write happens during the call, ahead
                // of any binding of the call's own result.
                InvalidateByRefWrites(copies, inst);

                // Track copy assignments. ANY assignment to a variable
                // redefines it — invalidation must run unconditionally
                // (an await-valued assignment `x = Await F()` records no
                // fact, but must still kill a stale earlier `x -> 5`);
                // recording is restricted to the safe subset.
                if (inst is IRAssignment assignment && assignment.Target is IRVariable target)
                {
                    // Kill every stale fact about the target (including other
                    // entries whose recorded value MENTIONS it) before
                    // recording the new one.
                    InvalidateRedefined(copies, target.Name);
                    // Never propagate awaits - duplicating them would
                    // re-execute the awaited task.
                    if (assignment.Value is IRValue value &&
                        value is not IRAwait &&
                        !Mentions(value, target.Name))
                    {
                        copies[target] = value;
                    }
                }
                else if (inst is IRStore store && store.Address is IRVariable storedVar)
                {
                    InvalidateRedefined(copies, storedVar.Name);
                }
                else if (inst is IRValue defined && !string.IsNullOrEmpty(defined.Name))
                {
                    // A NAMED non-assignment instruction redefines that name.
                    // The live case is the compound-assignment lowering:
                    // `d1 += ts` emits an IRBinaryOp RENAMED "d1" with no
                    // IRAssignment (IRBuilder's rename optimization), so
                    // without this kill the pre-compound copy fact
                    // (d1 -> its initializer value) survives and a later
                    // `d1 < d2` in the same block propagates the STALE
                    // initializer — a miscompile (caught by
                    // NativeBclFrontEndTests.DateTime_CrossTypeOperators_TypeAndRun).
                    // SSA temps are defined exactly once and are never copy
                    // keys, so this only fires for renamed real variables.
                    InvalidateRedefined(copies, defined.Name);
                }
            }
        }

        /// <summary>
        /// Kills the copy facts invalidated by a call's by-reference writes. A ByRef (or .NET
        /// <c>ref</c>/<c>out</c>) argument is an OUTPUT of the call, so every variable its
        /// expression reads may hold a different value afterwards.
        ///
        /// <para>Invalidation is by NAME over the argument's whole operand tree, not just the
        /// top-level variable: an argument such as <c>h.F</c> or <c>arr(i)</c> writes storage
        /// reachable through <c>h</c> / <c>arr</c>, and killing a fact is always safe while
        /// keeping a stale one is not.</para>
        /// </summary>
        private static void InvalidateByRefWrites(Dictionary<IRVariable, IRValue> copies, IRInstruction inst)
        {
            if (copies.Count == 0) return;

            List<bool> byRefFlags;
            List<IRValue> arguments;
            switch (inst)
            {
                case IRCall call:
                    byRefFlags = call.ByRefArguments; arguments = call.Arguments; break;
                case IRInstanceMethodCall methodCall:
                    byRefFlags = methodCall.ByRefArguments; arguments = methodCall.Arguments; break;
                default:
                    return;
            }
            if (byRefFlags == null || arguments == null) return;

            for (int i = 0; i < arguments.Count && i < byRefFlags.Count; i++)
            {
                if (!byRefFlags[i]) continue;
                var written = new List<string>();
                CollectNames(arguments[i], written);
                foreach (var name in written)
                    InvalidateRedefined(copies, name);
            }
        }

        /// <summary>
        /// Gathers every variable name read anywhere in <paramref name="value"/>'s operand tree,
        /// plus the value's own destination name (the IRBuilder names result values after their
        /// assignment target, so a renamed temp IS a definition). Mirrors <see cref="Mentions"/>
        /// arm for arm; an unrecognized shape still contributes its own name, and the caller
        /// pairs this with <see cref="InvalidateRedefined"/>, which additionally kills facts
        /// whose recorded value MENTIONS the name. Over-collecting only costs optimization —
        /// keeping a stale fact is the outcome that miscompiles.
        /// </summary>
        private static void CollectNames(IRValue value, List<string> into)
        {
            switch (value)
            {
                case null:
                case IRConstant:
                    return;
                case IRVariable v:
                    if (!string.IsNullOrEmpty(v.Name)) into.Add(v.Name);
                    return;
                case IRNewObject n:
                    foreach (var a in n.Arguments) CollectNames(a, into);
                    break;
                case IRBinaryOp b:
                    CollectNames(b.Left, into); CollectNames(b.Right, into);
                    break;
                case IRUnaryOp u:
                    CollectNames(u.Operand, into);
                    break;
                case IRCompare c:
                    CollectNames(c.Left, into); CollectNames(c.Right, into);
                    break;
                case IRCast cast:
                    CollectNames(cast.Value, into);
                    break;
                case IRFieldAccess f:
                    CollectNames(f.Object, into);
                    break;
                case IRInstanceMethodCall m:
                    CollectNames(m.Object, into);
                    foreach (var a in m.Arguments) CollectNames(a, into);
                    break;
                case IRCall call:
                    foreach (var a in call.Arguments) CollectNames(a, into);
                    break;
                case IRLoad load:
                    CollectNames(load.Address, into);
                    break;
                case IRGetElementPtr gep:
                    CollectNames(gep.BasePointer, into);
                    foreach (var idx in gep.Indices) CollectNames(idx, into);
                    break;
            }

            // A value that names its own destination temp (the IRBuilder names result values
            // after their assignment target) is itself a definition worth killing.
            if (value != null && value is not IRVariable && !string.IsNullOrEmpty(value.Name))
                into.Add(value.Name);
        }

        /// <summary>
        /// Removes every copy fact made stale by a (re)definition of
        /// <paramref name="definedName"/>: entries keyed by that variable and
        /// entries whose recorded value mentions it (propagating those later
        /// would read the NEW value at the use site).
        /// </summary>
        private static void InvalidateRedefined(Dictionary<IRVariable, IRValue> copies, string definedName)
        {
            if (string.IsNullOrEmpty(definedName) || copies.Count == 0) return;

            List<IRVariable> stale = null;
            foreach (var kvp in copies)
            {
                if (string.Equals(kvp.Key.Name, definedName, StringComparison.OrdinalIgnoreCase) ||
                    Mentions(kvp.Value, definedName))
                {
                    (stale ??= new List<IRVariable>()).Add(kvp.Key);
                }
            }
            if (stale != null)
            {
                foreach (var key in stale)
                    copies.Remove(key);
            }
        }

        /// <summary>
        /// Whether a recorded copy value reads the named variable anywhere in
        /// its operand tree. Unknown value shapes conservatively answer TRUE
        /// (killing a copy fact is always safe; keeping a stale one is not).
        /// </summary>
        private static bool Mentions(IRValue value, string name)
        {
            switch (value)
            {
                case null:
                case IRConstant:
                    return false;
                case IRVariable v:
                    return string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase);
                case IRNewObject n:
                    return n.Arguments.Any(a => Mentions(a, name));
                case IRBinaryOp b:
                    return Mentions(b.Left, name) || Mentions(b.Right, name);
                case IRUnaryOp u:
                    return Mentions(u.Operand, name);
                case IRCompare c:
                    return Mentions(c.Left, name) || Mentions(c.Right, name);
                case IRCast cast:
                    return Mentions(cast.Value, name);
                case IRFieldAccess f:
                    return Mentions(f.Object, name);
                case IRInstanceMethodCall m:
                    return Mentions(m.Object, name) || m.Arguments.Any(a => Mentions(a, name));
                case IRCall call:
                    return call.Arguments.Any(a => Mentions(a, name));
                default:
                    return true;
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
                    EliminateCommonSubexpressions(function, block);
                }
            }

            return ModificationCount > 0;
        }

        private void EliminateCommonSubexpressions(IRFunction function, BasicBlock block)
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
                            // Temp variable - safe to remove and replace uses.
                            // ⛔ Through the base ReplaceUses, over the whole FUNCTION. This pass
                            // had its own copy that knew only binary/unary ops, stores and
                            // assignments, so a duplicate consumed by a compare, a call argument or
                            // a Return kept the REMOVED node and rendered an undeclared temp.
                            // MEASURED on master: `ShowI(n + 1)` then `If n + 1 = 5` / `Return n + 1`
                            // emitted `t2 = t4 == 5;` and `return t5;` on C++ (JavaScript survived
                            // only by re-rendering the expression inline).
                            ReplaceUses(function.Blocks.SelectMany(b => b.Instructions), binaryOp, replacement);
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
                    ReduceStrength(function, block);
                }
            }

            return ModificationCount > 0;
        }

        private void ReduceStrength(IRFunction function, BasicBlock block)
        {
            for (int i = 0; i < block.Instructions.Count; i++)
            {
                var inst = block.Instructions[i];

                if (inst is IRBinaryOp binaryOp)
                {
                    var reduced = TryReduceBinary(binaryOp);
                    if (reduced != null)
                    {
                        // The replacement stands in for the user's own statement, so it must keep
                        // the original's IDENTITY — its source location AND the flag saying it is
                        // named after a variable. Carrying only the line (which this used to do)
                        // left NamedAfterVariable false, and the write to a class member was then
                        // emitted as a fresh local on JavaScript and dropped on C#. See
                        // InheritIdentity for the measurements.
                        InheritIdentity(reduced, binaryOp);

                        // The rewrite produces a NEW IRValue object, so every consumer still
                        // pointing at the discarded multiply/divide/modulo has to be re-pointed
                        // at it. Reusing binaryOp.Name on the replacement is NOT sufficient —
                        // backends key temporaries by object identity, so an un-updated consumer
                        // renders a fresh, undeclared t{N} ("t0 = v << 1; return t1;"). Scoped to
                        // the whole function because a use may live in a later block (a compare
                        // feeding an If, a Return after a branch) — see ReplaceUses.
                        ReplaceUses(function.Blocks.SelectMany(b => b.Instructions), binaryOp, reduced);

                        block.Instructions[i] = reduced;
                        ReportModification();
                    }
                }
            }
        }
        
        // Returns IRBinaryOp (not IRInstruction): the replacement is always a value, and the
        // caller must be able to hand it to ReplaceUses as the new definition.
        private IRBinaryOp TryReduceBinary(IRBinaryOp op)
        {
            // Multiplication by power of 2 Ã¢â€ â€™ shift
            // ⛔ INTEGRAL RESULT ONLY. The check below looks at the CONSTANT's CLR type, never the
            // product's, and a BasicLang `2` or `1` is an int literal even when the other operand
            // is Double — so `x * 2` on a Double became `x << 1`. MEASURED: C# rejects it
            // (CS0019, `<<` on double), so any program multiplying a floating value by an integer
            // literal power of two failed to build under the optimizer. `x * 1` went the same way
            // (`x << 0`) before PeepholeOptimizationPass could fold it.
            if (op.Operation == BinaryOpKind.Mul && op.Type?.IsIntegral() == true)
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
            // REMOVED - this arm was UNSOUND for signed operands. An arithmetic shift FLOORS,
            // while .NET/VB integer division TRUNCATES TOWARD ZERO. Measured against the C#
            // backend: -10 / 4 is -2, but -10 >> 2 is -3. The identity holds only when the
            // left operand is provably non-negative, and the IR carries no range analysis to
            // establish that. Every C++ and IL compiler already performs this reduction
            // downstream when it is legal, so nothing of value is lost by refusing it here.
            
            // Modulo by power of 2 Ã¢â€ â€™ bitwise AND
            // Modulo by power of 2 -> bitwise AND (x % 8 == x & 7)
            // REMOVED - unsound for the same reason, and worse: it gets the SIGN wrong.
            // .NET: -10 Mod 4 is -2. The rewrite gives -10 & 3, which is 2.
            //
            // WHY BOTH ARMS WERE LATENT RATHER THAN HARMLESS: each produced a NEW IRValue
            // whose consumers were never re-pointed at it (the defect this commit's parent
            // fixes), so affected programs failed to COMPILE and nobody ever saw the wrong
            // answer underneath. Repairing the re-pointing without removing these would have
            // converted two compile errors into two SILENT MISCOMPILES - and on BOTH
            // backends, because the optimizer is shared. That is also why cross-backend
            // agreement cannot be the oracle for an optimizer bug: it moves both backends
            // together. These were caught by comparing against real .NET semantics instead.

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

            // FunctionInliningPass DISABLED — it has never produced correct output for any
            // function it actually inlines, and it MISCOMPILES SILENTLY. Same call as the
            // ConstantPropagationPass line in AddStandardPasses above: the pass stays in the file
            // (its CloneAndRemap is still the only clone path an IRCall can reach, which
            // NetIrCarriageTests exercises by adding it explicitly) but nothing ships it.
            //
            // MEASURED on `Function F(p As Integer) As Integer : Return p * 2` called as `F(6)`,
            // which produced this Main:
            //     _inline_t1_0 = 12;         // undeclared, and nothing reads it
            //     t1 = (p << 1);             // undeclared t1, and the CALLEE'S PARAMETER p leaked
            //     const t0 = String(F(6));   // ...and the original call still happens
            // Six of seven call shapes measured this way fail at run time (ReferenceError); the
            // seventh only survives because IsInlineable REFUSES it for block count. All seven are
            // correct without this pass. FIVE separate defects, not one:
            //  1. Inlined locals are never added to the caller's LocalVariables, so every one is
            //     emitted undeclared.
            //  2. A definition is renamed by `tempCounter` while its USES are renamed by
            //     `prefix + name` (RemapValue) — two schemes that can never agree, which is the
            //     `_inline_t1_0` / `_inline_t1_x` mismatch above.
            //  3. RemapValue only rewrites an IRVariable and returns anything else untouched, so a
            //     nested operand tree keeps the callee's own variables and parameters.
            //  4. InlineCallsInBlock never calls ReplaceUses, so consumers still reference the
            //     removed IRCall and the callee is emitted and CALLED anyway — the very thing
            //     ReplaceUses' own doc comment says a pass that swaps an instruction must do.
            //  5. `depth` is passed 0 and never incremented, so _maxInlineDepth is dead.
            // Repairing it is a rewrite, not a patch, and inlining buys nothing here: clang, the
            // CLR JIT and V8 all inline far better than this pass could downstream.
            // AddPass(new FunctionInliningPass());

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

                // NOTE (P2a-1 Task 10; widened P2a-2 Tasks 2/7a, and again by Task 9): there is
                // deliberately NO case here for IRCall, IRInstanceMethodCall, IRBaseMethodCall,
                // IRNewObject, IRFieldAccess, IRFieldStore, IRIndexerAccess, IRIndexerStore or
                // IRForEach, and adding one is a breaking change. Falling through to `default`
                // returns the SAME node, which is what carries ResolvedNetTarget / NetCategory /
                // ResolvedNetTargetIsExact — plus Task 9's IRForEach.NetEnumeration bundle and
                // IRCall.NetArgumentRefKinds — across inlining. This is the only clone path any
                // of them can reach. Any case added here MUST copy every carriage field;
                // NetIrCarriageTests (.AggressivePipelinePreservesCarriageThroughTheInliningClonePath
                // and its siblings) fails if it does not.
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
                    OptimizeBlock(function, block);
                }
            }

            return ModificationCount > 0;
        }

        /// <summary>
        /// Installs <paramref name="replacement"/> (a fold of the value <paramref name="original"/>,
        /// which is <c>block.Instructions[i]</c>) and re-points every consumer. Returns true when the
        /// instruction was REMOVED, so the caller can step its index back.
        ///
        /// <para>⛔ THE MISSING HALF, the same one <see cref="AlgebraicSimplificationPass"/> and
        /// <see cref="StrengthReductionPass"/> already had to fix: this pass swapped the instruction
        /// for an <see cref="IRAssignment"/> to <c>original.Name</c> and never re-pointed the
        /// consumers. For a named local (<c>Dim r = n - n</c>) that was survivable — the variable is
        /// declared and the consumer reads it by name. For a TEMP it was not: no backend declares
        /// an assignment's target temp, and the orphaned consumer still held the discarded node.
        /// MEASURED on <c>Show(n - n)</c> (Integer) under the standard pipeline:</para>
        /// <code>
        ///   C++:  t0 = 0;  Show(t8);        // both undeclared — does not compile
        ///   C#:   t0 = 0;  Show(n - n);     // CS0103, and the consumer re-renders the original
        ///   JS:   t0 = 0;  Show(...)        // ReferenceError in an ES module
        /// </code>
        /// <para>The re-render was a second defect hidden behind the first: <c>Show(P() * 0)</c>
        /// ran P TWICE on C# and JavaScript (once as the call's own statement, once inside the
        /// resurrected <c>P() * 0</c>).</para>
        ///
        /// <para>So a temp is not assigned at all: its consumers receive the folded value itself and
        /// the instruction goes. Nothing is lost by dropping it — every operand is a separate
        /// instruction that still executes (<c>P()</c> above stays as its own statement), and the
        /// fold only discards the arithmetic. A named value keeps its assignment, and its consumers
        /// are re-pointed at the variable being written.</para>
        /// </summary>
        private bool InstallFold(IRFunction function, BasicBlock block, int i, IRValue original, IRAssignment replacement)
        {
            var allInstructions = function.Blocks.SelectMany(b => b.Instructions);

            if (original.NamedAfterVariable)
            {
                block.Instructions[i] = replacement;
                ReplaceUses(allInstructions, original, replacement.Target);
                return false;
            }

            block.Instructions.RemoveAt(i);
            ReplaceUses(allInstructions, original, replacement.Value);
            return true;
        }

        private void OptimizeBlock(IRFunction function, BasicBlock block)
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
                            changed = true;
                            ReportModification();
                            if (replacement is IRAssignment folded)
                            {
                                if (InstallFold(function, block, i, binOp, folded)) i--;
                                continue;
                            }
                            block.Instructions[i] = replacement;
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
                            changed = true;
                            ReportModification();
                            if (InstallFold(function, block, i, unary, newAssign)) i--;
                            continue;
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
                            changed = true;
                            ReportModification();
                            if (InstallFold(function, block, i, notOp, newAssign)) i--;
                            continue;
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
            // ⛔ FOUR OF THE IDENTITIES BELOW ARE INTEGER-ONLY, and are gated on this. In IEEE 754
            // they are false, and the fold miscompiled SILENTLY on every backend, because the
            // optimizer is shared. MEASURED (C# and C++ agreed on every wrong answer):
            //   x - x   x = +Inf or NaN   ->  NaN     the fold gave 0
            //   x * 0   x = +Inf or NaN   ->  NaN     the fold gave 0
            //   x * 0   x = -5            ->  -0      the fold gave +0 (1/r: -Inf vs +Inf)
            //   x + 0   x = -0            ->  +0      the fold gave x, i.e. -0
            //   x / x   x = 0, Inf, NaN   ->  NaN     the fold gave 1
            // (Single `b - b` with b = 1.0E+30F * 1.0E+30F = +Inf also printed 0, not NaN.)
            // `x - (+0)`, `x * 1` and `x / 1` ARE exact in IEEE 754 for every x, so they stay
            // ungated; only a NEGATIVE-zero subtrahend is refused, since `x - (-0)` is `x + 0`.
            //
            // Gated on the RESULT type: any floating operand promotes the result to floating,
            // and Integer `/` is typed Double by the builder (its operands arrive as IRCasts,
            // so `n / n` never reached the `x / x` arm anyway). Decimal is excluded too — it
            // keeps a scale (`1.50D - 1.50D` is `0.00`) and throws on `0 / 0`.
            bool integral = binOp.Type?.IsIntegral() == true;

            // x + 0 -> x
            if (binOp.Operation == BinaryOpKind.Add && integral)
            {
                if (IsZero(binOp.Right))
                    return new IRAssignment(new IRVariable(binOp.Name, binOp.Type), binOp.Left);
                if (IsZero(binOp.Left))
                    return new IRAssignment(new IRVariable(binOp.Name, binOp.Type), binOp.Right);
            }

            // x - 0 -> x
            if (binOp.Operation == BinaryOpKind.Sub && IsZero(binOp.Right)
                && (integral || !IsNegativeZero(binOp.Right)))
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
            if (binOp.Operation == BinaryOpKind.Mul && integral)
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
            if (binOp.Operation == BinaryOpKind.Sub && integral &&
                binOp.Left is IRVariable left &&
                binOp.Right is IRVariable right &&
                left.Name == right.Name)
            {
                return new IRAssignment(
                    new IRVariable(binOp.Name, binOp.Type),
                    new IRConstant(0, binOp.Type));
            }

            // x / x -> 1 (integral only; the x != 0 precondition is still NOT checked)
            if (binOp.Operation == BinaryOpKind.Div && integral &&
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
                // ⛔ UNLIKE THE TWO ARMS ABOVE, THIS ONE DISCARDS THE OTHER OPERAND ENTIRELY.
                // Those discard whichever side IsTrue matched, and IsTrue only matches an
                // IRConstant — so they drop a literal and can never lose work. This arm drops
                // the OTHER side, which may be a call.
                //
                // VB's `And` is NON-short-circuiting: `False And Probe()` must still call
                // Probe. Ungated, this fired on exactly that shape and produced two defects at
                // once, both measured: a dead store `t3 = false;` to a temp the declaration
                // pass never emitted (CS0103 — generated C# that will not compile), and a
                // DOUBLE evaluation, because the consumer re-renders the original operand tree
                // inline rather than reading the folded temp. `AndAlso` was unaffected only
                // because it is a different BinaryOpKind and never reached here.
                if ((IsFalse(binOp.Right) && IsSideEffectFree(binOp.Left))
                    || (IsFalse(binOp.Left) && IsSideEffectFree(binOp.Right)))
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
                // Same asymmetry as the And arm above — this one discards the OTHER operand,
                // so it needs the same guard. `True Or Probe()` must still call Probe.
                if ((IsTrue(binOp.Right) && IsSideEffectFree(binOp.Left))
                    || (IsTrue(binOp.Left) && IsSideEffectFree(binOp.Right)))
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

        private static bool IsNegativeZero(IRValue value) =>
            value is IRConstant c
            && ((c.Value is double d && d == 0.0 && double.IsNegative(d))
                || (c.Value is float f && f == 0.0f && float.IsNegative(f)));

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

        /// <summary>
        /// True when evaluating <paramref name="value"/> cannot be OBSERVED, so a rewrite may
        /// drop it: a literal, or a read of an already-computed temp or local.
        ///
        /// <para>An <see cref="IRVariable"/> counts as free precisely BECAUSE the work that
        /// produced it is a separate instruction that still executes — dropping the reference
        /// loses the value, never the effect. Anything else (a call, or an operator tree that
        /// may contain one) does NOT qualify: the operand IS the work.</para>
        ///
        /// <para>Deliberately conservative. A false negative costs one missed constant fold; a
        /// false positive silently deletes a user's function call.</para>
        /// </summary>
        private static bool IsSideEffectFree(IRValue value) =>
            value is IRConstant or IRVariable;

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
                    SimplifyBlock(function, block);
                }
            }

            return ModificationCount > 0;
        }

        private void SimplifyBlock(IRFunction function, BasicBlock block)
        {
            for (int i = 0; i < block.Instructions.Count; i++)
            {
                var inst = block.Instructions[i];

                if (inst is IRBinaryOp binOp)
                {
                    var simplified = SimplifyBinaryOp(binOp);
                    if (simplified != binOp)
                    {
                        // Same identity transfer strength reduction needs, for the same reason: the
                        // `2 * x -> x + x` arm builds a NEW value carrying the old name, and without
                        // the flag a write to a class member was emitted as a fresh local on
                        // JavaScript and dropped on C#. MEASURED on `K = 2 * p` under --optimize,
                        // which is the only pipeline this pass runs in.
                        if (simplified is IRValue replacement) InheritIdentity(replacement, binOp);

                        block.Instructions[i] = simplified;

                        // ⛔ THE MISSING HALF. This pass swapped the instruction and never
                        // re-pointed the CONSUMERS, which the base class's ReplaceUses doc states
                        // a pass doing so MUST. Every consumer still held the discarded node, so
                        // the simplification did not merely fail to apply — it left broken code:
                        //     const t0 = ((a + b) | 0);
                        //     t1 = a;                             // UNDECLARED -> ReferenceError
                        //     return ((((a + b) | 0) - b) | 0);   // consumer re-materialised the
                        //                                         // WHOLE original expression
                        // MEASURED on `Return (a + b) - b`. Carrying the old NAME is not enough,
                        // exactly as that doc says: `2 * x -> x + x` survived only by NAME
                        // COINCIDENCE (its replacement is a value with the same name, so the
                        // orphaned consumer resolved by accident), while an arm replacing a value
                        // with an IRAssignment gives the consumer nothing to resolve at all.
                        //
                        // Scoped to the whole FUNCTION, not this block: a use may live in a later
                        // block (a compare feeding an If, a Return after a branch), which is why
                        // StrengthReductionPass scopes it the same way.
                        var definition = simplified is IRAssignment assignment
                            ? (IRValue)assignment.Target
                            : simplified as IRValue;
                        if (definition != null)
                            ReplaceUses(function.Blocks.SelectMany(b => b.Instructions), binOp, definition);

                        ReportModification();
                    }
                }
            }
        }

        private IRInstruction SimplifyBinaryOp(IRBinaryOp binOp)
        {
            // `(a + b) - b -> a`, `(a - b) + b -> a` and `(a * b) / b -> a` REMOVED — all three were
            // UNSOUND for floating-point operands, and this pass's missing ReplaceUses is the only
            // reason nobody ever saw a wrong answer. The same story as the Div and Mod arms removed
            // from StrengthReductionPass, and recorded there in the same words: latent rather than
            // harmless, because the broken machinery around them hid the bad arithmetic behind a
            // compile or run error.
            //
            // MEASURED, correct answer first and what the arm would have produced second:
            //   (a + b) - b   a=1e-19, b=1e18   ->   0      the arm gives a (1e-19).
            //                                           Catastrophic cancellation: adding b makes a
            //                                           vanish, and subtracting it does not bring a
            //                                           back.
            //   (a * b) / b   a=4, b=0          ->   NaN    the arm gives 4. Its own comment claimed
            //                                           "when b != 0" — the code NEVER CHECKED IT.
            //   (a * b) / b   a=0.1, b=3        ->   0.10000000000000002
            //                                           the arm gives 0.1. Plain rounding: the
            //                                           round trip is not the identity.
            //
            // Restricting them to integers would be sound for the two additive ones, but `(a*b)/b`
            // stays wrong at b = 0 there too (DivideByZeroException versus `a`), and none of the
            // three is a shape anyone writes. Every backend's own optimizer does this legally
            // downstream where it is legal at all.

            // 2 * x -> x + x. KEPT, and sound on both fronts: `x + x` is EXACTLY `2 * x` in IEEE 754
            // (one rounding either way, same result), and it wraps identically on integer overflow.
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

                // NOTE (P2a-1 Task 10; widened P2a-2 Tasks 2/7a): an IRCall cannot reach this
                // switch — a loop containing one is refused for unrolling upstream
                // (IsSimpleLoop, "if (inst is IRCall) return false"). IRInstanceMethodCall /
                // IRBaseMethodCall / IRNewObject / IRFieldAccess / IRFieldStore CAN reach it
                // (IsSimpleLoop does not refuse them) and land here in `default`, which returns
                // the SAME node — so their ResolvedNetTarget / NetCategory /
                // ResolvedNetTargetIsExact survive by aliasing, exactly as every other field of
                // theirs always has across unrolling. If any `case` is ever added for one of
                // the carriage-bearing node types it MUST copy all three fields across, for the
                // reason spelled out on FunctionInliningPass.CloneAndRemap's default arm.
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
