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

        /// <summary>
        /// True when <paramref name="name"/> is an SSA temp minted by
        /// <see cref="IRFunction.GetNextTempName"/> (<c>t0</c>, <c>t1</c>, …) or one of the
        /// historical <c>_tmp</c>/<c>_t0</c> spellings, rather than a name the USER wrote.
        ///
        /// <para><b>Why a pass that rewrites a value needs this.</b> The two destinations behave
        /// oppositely when a rewrite replaces an <see cref="IRValue"/> with an
        /// <see cref="IRAssignment"/>. A user-named destination is a declared local, so the
        /// assignment must STAY and consumers are re-pointed at its target. A temp is declared
        /// ONLY because some <c>IRValue</c> instruction in the block carries that name — an
        /// <c>IRAssignment</c> is not an <c>IRValue</c>, so swapping one in DELETES the
        /// declaration while leaving the write behind. MEASURED on <c>Show(a + 0)</c>, whose
        /// peephole rewrite emitted <c>t0 = a;</c> against an undeclared <c>t0</c>: CS0103 on C#,
        /// "use of undeclared identifier" on C++, ReferenceError on JavaScript. For a temp the
        /// definition must therefore be REMOVED and its consumers forwarded to the value itself.</para>
        ///
        /// <para>⚠ <see cref="ConstantFoldingPass"/> keeps its own copy of this test deliberately;
        /// see the note there. It is not a second implementation of this one — it answers
        /// differently for a user variable spelled <c>T5</c>, and reconciling the two is a
        /// behaviour change that has to be measured on its own rather than smuggled into a fix
        /// for something else.</para>
        /// </summary>
        protected internal static bool IsTempDestination(string name)
        {
            if (string.IsNullOrEmpty(name)) return true;
            if (name.StartsWith("_tmp", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.StartsWith("_t", StringComparison.OrdinalIgnoreCase) && name.Length > 2 && char.IsDigit(name[2])) return true;
            if (name.Length >= 2 && name[0] == 't' && char.IsDigit(name[1])) return true;
            return false;
        }

        /// <summary>
        /// Whether a call can write the variable a value instruction's DESTINATION names, without
        /// the write appearing in the caller — the destination's half of CSE's
        /// <c>ReadsCallVisible</c> (ADR-0005 D2: the destination is guarded like a read). The
        /// instruction carries only the NAME, so the answer comes from the function's own
        /// declarations: a by-value parameter or a declared non-global local is private to the
        /// frame; a module variable, a class member or a <c>ByRef</c> parameter is storage a
        /// callee can reach. An undeclared non-temp name is treated as reachable — over-killing
        /// only costs a merge.
        /// </summary>
        protected internal static bool IsCallVisibleDestination(string name, IRFunction function)
        {
            if (string.IsNullOrEmpty(name) || function == null) return false;
            if (function.Parameters != null)
                foreach (var parameter in function.Parameters)
                    if (string.Equals(parameter.Name, name, StringComparison.OrdinalIgnoreCase))
                        return parameter.IsByRef;
            if (function.LocalVariables != null)
                foreach (var local in function.LocalVariables)
                    if (string.Equals(local.Name, name, StringComparison.OrdinalIgnoreCase))
                        return local.IsGlobal && !local.IsConst;
            return !IsTempDestination(name);
        }

        /// <summary>
        /// THE KILL VOCABULARY: every variable name <paramref name="inst"/> may WRITE, and whether
        /// it is a call (which additionally writes any storage a callee can reach — see
        /// <c>CommonSubexpressionEliminationPass.ReadsCallVisible</c>). Null when it writes no
        /// name.
        ///
        /// <para>⭐ SHARED, and the sharing is the point (ADR-0005 D2): CSE's
        /// <c>Invalidate</c> kills on exactly these writes, and <see cref="IRVerifier"/>'s
        /// Invariant S′ calls exactly these writes "assigned". A write form missing here is
        /// missing for BOTH — the verifier cannot catch a pass for a write the vocabulary
        /// does not name, and that is deliberate: a gap is fixed once, here.</para>
        ///
        /// <para>Forms: an <see cref="IRAssignment"/> target; an <see cref="IRStore"/> address;
        /// a NAMED value instruction (a rename — how both <c>Dim p = Seed(1)</c> and
        /// <c>p = p + 10</c> lower); a call's <c>ByRef</c> arguments.</para>
        ///
        /// <para>⛔ KNOWN GAPS — writes this vocabulary does not name, so CSE merges across them
        /// and the verifier cannot see them. Each MEASURED as a live wrong answer with a merge:
        /// <list type="bullet">
        /// <item>an <see cref="IRFieldStore"/> through <c>Me.</c> to a member the function also
        /// names bare — operand side (<c>a = K + q : Me.K = 10 : l(0) = K + q</c>, C++/JS/MSIL
        /// print 3 for 12) and destination side (<c>K = p + q : Me.K = 0 : l(0) = p + q</c>,
        /// C++/MSIL);</item>
        /// <item>a write through a second <c>ByRef</c> parameter aliasing the destination
        /// (<c>n = p + q : m = 0 : l(0) = p + q</c> called as <c>Work(v, v)</c>, C++/MSIL) —
        /// destination side only, since a <c>ByRef</c> operand is never replicable;</item>
        /// <item>a local captured by reference and written inside a lambda (JavaScript; C++ and
        /// MSIL do not build the shape). The operand side is recorded in HANDOFF.</item>
        /// </list>
        /// Recorded rather than fixed: ADR-0005 D2 rules the vocabulary is shared and a gap is
        /// flagged, not patched on one side.</para>
        /// </summary>
        protected internal static List<string> NamesWrittenBy(IRInstruction inst, out bool isCall)
        {
            isCall = false;
            List<string> killed = null;
            void Kill(string name)
            {
                if (!string.IsNullOrEmpty(name)) (killed ??= new List<string>()).Add(name);
            }

            switch (inst)
            {
                case IRAssignment assignment when assignment.Target is IRVariable target:
                    Kill(target.Name);
                    break;
                case IRStore store when store.Address is IRVariable stored:
                    Kill(stored.Name);
                    break;
            }

            // A NAMED non-assignment instruction redefines that name. `Dim p = Seed(1)` is an
            // IRCall renamed `p`; `p = p + 10` is an IRBinaryOp renamed `p`. Neither produces an
            // IRAssignment, so without this arm the two measured shapes are not covered at all.
            if (inst is IRValue defined) Kill(defined.Name);

            // A call WRITES its ByRef arguments, and the write is invisible in this block — there
            // is no IRAssignment and no rename for it. MEASURED on `Bump(p)` with a ByRef `p`:
            // C++ and MSIL printed the stale answer (JavaScript refuses ByRef by design).
            List<bool> byRefFlags = null;
            List<IRValue> arguments = null;
            switch (inst)
            {
                case IRCall call:
                    isCall = true; byRefFlags = call.ByRefArguments; arguments = call.Arguments; break;
                case IRInstanceMethodCall methodCall:
                    isCall = true; byRefFlags = methodCall.ByRefArguments; arguments = methodCall.Arguments; break;
                case IRNewObject:
                    isCall = true; break;
            }
            if (byRefFlags != null && arguments != null)
            {
                for (int i = 0; i < arguments.Count && i < byRefFlags.Count; i++)
                    if (byRefFlags[i]) CollectNames(arguments[i], killed ??= new List<string>());
            }

            return killed;
        }

        /// <summary>
        /// Gathers every variable name read anywhere in <paramref name="value"/>'s operand tree,
        /// plus the value's own destination name (the IRBuilder names result values after their
        /// assignment target, so a renamed temp IS a definition). Mirrors CopyPropagationPass's
        /// own <c>Mentions</c> arm for arm; an unrecognized shape still contributes its own
        /// name, and the caller
        /// pairs this with its own invalidation step, which additionally kills facts whose
        /// recorded value MENTIONS the name. Over-collecting only costs optimization —
        /// keeping a stale fact is the outcome that miscompiles.
        ///
        /// <para>⭐ SHARED. <see cref="CopyPropagationPass"/> and
        /// <see cref="CommonSubexpressionEliminationPass"/> both need the same answer to
        /// "which names does this expression read", and a second private copy is the defect
        /// commit 67782af removed from CSE for the operand walker. One implementation, two
        /// consumers — the ModuleResolver/ModuleTypeWalker rule in CLAUDE.md.</para>
        /// </summary>
        protected internal static void CollectNames(IRValue value, List<string> into)
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


        private static void MapList(List<IRValue> operands, Func<IRValue, IRValue> map)
        {
            if (operands == null) return;
            for (int i = 0; i < operands.Count; i++)
            {
                // Written only on a change: a List<T> store bumps its version even for the same
                // element, which would break a caller enumerating this list around the walk.
                var mapped = map(operands[i]);
                if (!ReferenceEquals(mapped, operands[i])) operands[i] = mapped;
            }
        }

        private static void ReplaceUsesIn(IRInstruction inst, IRValue oldValue, IRValue newValue)
            => MapUses(inst, v => ReferenceEquals(v, oldValue) ? newValue : v);

        /// <summary>
        /// Every value <paramref name="inst"/> USES, one entry per operand slot (a value used
        /// twice appears twice), from the same arm set <see cref="ReplaceUses"/> rewrites — so the
        /// two can never disagree about what a use is. <see cref="IRVerifier"/> counts uses
        /// through this.
        /// </summary>
        protected internal static List<IRValue> UsesOf(IRInstruction inst)
        {
            var uses = new List<IRValue>();
            MapUses(inst, v =>
            {
                if (v != null) uses.Add(v);
                return v;
            });
            return uses;
        }

        /// <summary>
        /// THE total use walker: one arm per IR node that CONSUMES a value, each operand slot
        /// replaced by <paramref name="map"/>'s answer for it (an identity map rewrites nothing).
        /// Definition slots are deliberately absent: <c>IRAssignment.Target</c> is an
        /// <see cref="IRVariable"/> being written, not a use.
        /// </summary>
        private static void MapUses(IRInstruction inst, Func<IRValue, IRValue> map)
        {
            switch (inst)
            {
                case IRBinaryOp binOp:
                    binOp.Left = map(binOp.Left);
                    binOp.Right = map(binOp.Right);
                    break;
                case IRUnaryOp unOp:
                    unOp.Operand = map(unOp.Operand);
                    break;
                case IRCompare cmp:
                    cmp.Left = map(cmp.Left);
                    cmp.Right = map(cmp.Right);
                    break;
                case IRLoad load:
                    load.Address = map(load.Address);
                    break;
                case IRStore store:
                    store.Value = map(store.Value);
                    store.Address = map(store.Address);
                    break;
                case IRGetElementPtr gep:
                    gep.BasePointer = map(gep.BasePointer);
                    MapList(gep.Indices, map);
                    break;
                case IRConditionalBranch condBr:
                    condBr.Condition = map(condBr.Condition);
                    break;
                case IRSwitch sw:
                    sw.Value = map(sw.Value);
                    if (sw.Cases != null)
                        for (int i = 0; i < sw.Cases.Count; i++)
                        {
                            var mapped = map(sw.Cases[i].CaseValue);
                            if (!ReferenceEquals(mapped, sw.Cases[i].CaseValue))
                                sw.Cases[i] = (mapped, sw.Cases[i].Target);
                        }
                    if (sw.PatternCases != null)
                        foreach (var patternCase in sw.PatternCases)
                            MapPatternUses(patternCase, map);
                    break;
                case IRReturn ret:
                    ret.Value = map(ret.Value);
                    break;
                case IRCall call:
                    call.CalleeValue = map(call.CalleeValue);
                    MapList(call.Arguments, map);
                    break;
                case IRCast cast:
                    cast.Value = map(cast.Value);
                    break;
                case IRAssignment asg:
                    asg.Value = map(asg.Value);
                    break;
                case IRArrayStore arrayStore:
                    arrayStore.Array = map(arrayStore.Array);
                    arrayStore.Index = map(arrayStore.Index);
                    arrayStore.Value = map(arrayStore.Value);
                    break;
                case IRAwait await:
                    await.Expression = map(await.Expression);
                    break;
                case IRYield yield:
                    yield.Value = map(yield.Value);
                    break;
                case IRIndexerAccess indexerAccess:
                    indexerAccess.Collection = map(indexerAccess.Collection);
                    MapList(indexerAccess.Indices, map);
                    break;
                case IRIndexerStore indexerStore:
                    indexerStore.Collection = map(indexerStore.Collection);
                    MapList(indexerStore.Indices, map);
                    indexerStore.Value = map(indexerStore.Value);
                    break;
                case IRForEach forEach:
                    forEach.Collection = map(forEach.Collection);
                    break;
                case IRThrow thrown:
                    thrown.Exception = map(thrown.Exception);
                    break;
                case IRNewObject newObject:
                    MapList(newObject.Arguments, map);
                    break;
                case IRInstanceMethodCall instanceCall:
                    instanceCall.Object = map(instanceCall.Object);
                    MapList(instanceCall.Arguments, map);
                    break;
                case IRBaseMethodCall baseCall:
                    MapList(baseCall.Arguments, map);
                    break;
                case IRFieldAccess fieldAccess:
                    fieldAccess.Object = map(fieldAccess.Object);
                    break;
                case IRFieldStore fieldStore:
                    fieldStore.Object = map(fieldStore.Object);
                    fieldStore.Value = map(fieldStore.Value);
                    break;
                case IRTupleElement tupleElement:
                    tupleElement.Tuple = map(tupleElement.Tuple);
                    break;
                case IRPhi phi:
                    // The only operand-bearing node this walk was missing. Nothing in the
                    // pipeline BUILDS an IRPhi today (IRBuilder emits none, and no pass
                    // introduces one), so no program measured here reaches this arm — it is
                    // here because a walker that is total except for one node kind gives a
                    // WRONG answer the day that kind appears, not an absent feature. The
                    // operand list is a value tuple, so it is rewritten by index.
                    if (phi.Operands != null)
                        for (int i = 0; i < phi.Operands.Count; i++)
                        {
                            var mapped = map(phi.Operands[i].Value);
                            if (!ReferenceEquals(mapped, phi.Operands[i].Value))
                                phi.Operands[i] = (mapped, phi.Operands[i].Block);
                        }
                    break;
            }
        }

        private static void MapPatternUses(IRPatternCase patternCase, Func<IRValue, IRValue> map)
        {
            if (patternCase == null) return;

            patternCase.WhenGuard = map(patternCase.WhenGuard);

            switch (patternCase)
            {
                case IRRangePatternCase range:
                    range.LowerBound = map(range.LowerBound);
                    range.UpperBound = map(range.UpperBound);
                    break;
                case IRComparisonPatternCase comparison:
                    comparison.CompareValue = map(comparison.CompareValue);
                    break;
                case IRConstantPatternCase constant:
                    constant.Value = map(constant.Value);
                    break;
                case IROrPatternCase or:
                    if (or.Alternatives != null)
                        foreach (var alternative in or.Alternatives)
                            MapPatternUses(alternative, map);
                    break;
                case IRTuplePatternCase tuple:
                    if (tuple.Elements != null)
                        foreach (var element in tuple.Elements)
                            MapPatternUses(element, map);
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
                        if (IsNamedVariable(binaryOp))
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
                        if (IsNamedVariable(unaryOp))
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
                        if (IsNamedVariable(compare))
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
        /// Whether <paramref name="value"/> is a STORE into a user variable, which folding must
        /// keep as an assignment rather than delete. The IRBuilder's flag is authoritative; the
        /// name-shape guess below is only the fallback for values that carry no flag.
        ///
        /// <para>⛔ The guess alone treated a user variable named like a temp as a temp: with
        /// <c>Dim t3 As Integer = 6 \ 2</c> the store folded away and t3 stayed 0. MEASURED on
        /// master 883fb1d. See IRBuilder.SeparateTempsFromUserNames.</para>
        /// </summary>
        private bool IsNamedVariable(IRValue value) =>
            value.NamedAfterVariable || IsNamedVariable(value.Name);

        /// <summary>
        /// Check if a name represents a real variable (not a temp)
        ///
        /// <para>⚠ DELIBERATELY NOT <see cref="OptimizationPass.IsTempDestination"/>, which CSE and
        /// the peephole pass share. This copy's <c>t</c> test is case-INSENSITIVE, so it calls a
        /// user variable spelled <c>T5</c> a temp and folds away the assignment to it; the shared
        /// one does not. Only <c>t{N}</c> is ever minted by
        /// <see cref="IRFunction.GetNextTempName"/>, so the two agree on everything the compiler
        /// itself produces and the difference is reachable only from user source. Reconciling them
        /// is a behaviour change to constant folding that has to be measured on its own, not
        /// carried along by a fix to a different pass.</para>
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
            var expressions = new Dictionary<string, Candidate>();

            for (int i = 0; i < block.Instructions.Count; i++)
            {
                var inst = block.Instructions[i];

                // ⛔ Only a REPLICABLE binop is a candidate (ADR-0001 Obligations, defence in depth):
                // merging two evaluations of a value that is not replicable deletes one of them,
                // which no backend can repair. The same predicate the C# backend and
                // AlgebraicSimplificationPass use — not ReadsCallVisible, which answers a different
                // question (when to KILL an entry) and must stay separate (ADR-0004 D2). A
                // non-replicable binop is neither recorded nor merged; the invalidation step below
                // still runs for it.
                if (inst is IRBinaryOp binaryOp && IRReplicability.IsReplicable(binaryOp))
                {
                    var key = ExpressionKey(binaryOp);

                    if (expressions.ContainsKey(key))
                    {
                        // Found a duplicate expression
                        var replacement = expressions[key].Value;

                        // If the current instruction is a named destination (actual variable, not a temp),
                        // we should NOT remove it. Instead, convert to an assignment.
                        //
                        // ⚠ `NamedAfterVariable` is the second half of that question and was missing
                        // here too, for the same reason and with the same consequence as in
                        // PeepholeOptimizationPass.ApplyRewrite: the name test is a test on SPELLING,
                        // and a user variable spelled `t0` was therefore treated as a temp and its
                        // write REMOVED. MEASURED on a member named `t0` assigned a duplicated
                        // expression — the field kept its old value on all four backends. This is a
                        // PRE-EXISTING defect of the pass, not one introduced by pointing it at the
                        // shared walker; it is fixed here because it is the same predicate on the
                        // same line and leaving it would mean shipping a known silent miscompile.
                        if (!IsTempDestination(binaryOp.Name) || binaryOp.NamedAfterVariable)
                        {
                            // Convert to assignment: target = existingResult
                            var targetVar = InheritIdentity(new IRVariable(binaryOp.Name, binaryOp.Type), binaryOp);
                            block.Instructions[i] = new IRAssignment(targetVar, replacement);
                            // The binop OBJECT is gone from the stream even though its name lives on
                            // in targetVar, so its consumers are re-pointed here too. They would
                            // currently resolve by NAME COINCIDENCE — a declared user variable spelled
                            // the same — which is precisely the accident AlgebraicSimplificationPass
                            // documents at its own ReplaceUses call and which fails the moment a
                            // backend keys a temp by object identity instead.
                            ReplaceUses(function.Blocks.SelectMany(b => b.Instructions), binaryOp, targetVar);
                            ReportModification();
                        }
                        else
                        {
                            // Temp variable - safe to remove and replace uses
                            ReplaceUses(function.Blocks.SelectMany(b => b.Instructions), binaryOp, replacement);
                            block.Instructions.RemoveAt(i);
                            i--;
                            ReportModification();
                        }
                    }
                    else
                    {
                        expressions[key] = Candidate.For(binaryOp, function);
                    }
                }

                // ⛔ THE INVALIDATION STEP. Runs AFTER this instruction's own lookup/record and
                // BEFORE the next instruction's, because an instruction can both READ and WRITE
                // the same name: `p = p + 10` lowers to ONE IRBinaryOp renamed `p`, whose key is
                // computed against the OLD p and whose record is stale the instant it is made.
                // Recording then killing gets both right; killing first would leave that entry
                // alive. Mirrors CopyPropagationPass.PropagateCopies, which orders its own
                // use/record/kill exactly this way for exactly this reason.
                //
                // ⚠ `inst`, NOT `block.Instructions[i]`. The temp arm above does `RemoveAt(i); i--`,
                // so by here the index can point at the PREVIOUS instruction (already invalidated
                // on its own pass) — or, for a duplicate at index 0, at -1. Both the redundant
                // re-kill and the latent IndexOutOfRange go away by naming the instruction this
                // iteration actually processed. The named arm's replacement IRAssignment carries
                // the same Target.Name as the IRBinaryOp it replaced, so the kill is unchanged.
                Invalidate(expressions, inst);
            }
        }

        /// <summary>
        /// One recorded candidate: the defining instruction, every variable name its operand tree
        /// READS, its own DESTINATION name, and whether any of those reads is storage a CALL can
        /// write behind our back.
        ///
        /// <para>⛔ THE DESTINATION IS GUARDED TOO (ADR-0005 D2, Invariant S′). A merge re-points
        /// a later duplicate's consumers at THIS instruction, and a backend that materialises it
        /// (C++, JavaScript, MSIL) reads it back by its destination NAME. So the value is stale
        /// the moment that name is written, exactly as it is when an operand is written. MEASURED
        /// on <c>Dim a = p + q : a = Seed(0) : l(0) = p + q</c> at HEAD: C++, JavaScript and MSIL
        /// printed <c>0,0</c> where <c>3,0</c> is correct; C# printed the right answer only
        /// because it re-emits <c>p + q</c> as text.</para>
        ///
        /// <para>The destination is killed by the SAME vocabulary as the operands — assignment
        /// target, store address, rename, ByRef argument — with one exception: the defining
        /// instruction itself carries the destination name (it is a rename), and must not kill
        /// its own record through it. It still kills through its OPERANDS, which is what keeps
        /// the self-redefining <c>p = p + 10</c> record dead (see the use → record → kill note in
        /// <see cref="EliminateCommonSubexpressions"/>).</para>
        /// </summary>
        private sealed class Candidate
        {
            public IRBinaryOp Value;
            public List<string> Reads;
            public string Destination;
            public bool ReadsCallVisibleStorage;

            public static Candidate For(IRBinaryOp op, IRFunction function)
            {
                var reads = new List<string>();
                CollectNames(op.Left, reads);
                CollectNames(op.Right, reads);
                var destination = string.IsNullOrEmpty(op.Name) ? null : op.Name;
                return new Candidate
                {
                    Value = op,
                    Reads = reads,
                    Destination = destination,
                    ReadsCallVisibleStorage =
                        ReadsCallVisible(op.Left) || ReadsCallVisible(op.Right)
                        || IsCallVisibleDestination(destination, function)
                };
            }
        }

        /// <summary>
        /// The dictionary key for a candidate expression.
        ///
        /// <para>⛔ The key this replaces was <c>$"{Operation}_{Left.Name}_{Right.Name}"</c>, and
        /// its <c>_</c> delimiter is NOT escaped, so the encoding is not injective: a BasicLang
        /// identifier may contain <c>_</c>, and <see cref="IRConstant"/> names itself
        /// <c>const_{value}</c>. MEASURED — <c>p + q_r</c> and <c>p_q + r</c> both key
        /// <c>Add_p_q_r</c> and the second is rewritten to the first, and
        /// <c>"a" &amp; b_c</c> collides with <c>"a_b" &amp; c</c> the same way. Those are two
        /// UNRELATED expressions, so unlike the redefinition case below the C# backend's
        /// inline-always policy does not rescue it: all FOUR backends print the wrong answer.
        /// Length-prefixing each part makes the encoding injective.</para>
        ///
        /// <para>The result type is part of the key because <c>Operation</c> plus operand names
        /// does not determine it, and an entry is substituted for its match by NAME.</para>
        ///
        /// <para>⚠ Operand names are NOT case-folded, so two spellings of one VB-case-insensitive
        /// identifier simply miss each other — today's behaviour, and a missed merge is safe.
        /// <see cref="Invalidate"/> is case-INsensitive, which is the safe polarity on that
        /// side: it must kill an entry spelled <c>P</c> when <c>p</c> is redefined.</para>
        /// </summary>
        private static string ExpressionKey(IRBinaryOp op)
        {
            var sb = new System.Text.StringBuilder();
            AppendPart(sb, op.Operation.ToString());
            AppendPart(sb, op.Left?.Name);
            AppendPart(sb, op.Right?.Name);
            AppendPart(sb, op.Type?.Name);
            return sb.ToString();
        }

        private static void AppendPart(System.Text.StringBuilder sb, string part)
        {
            part ??= string.Empty;
            sb.Append(part.Length).Append(':').Append(part).Append('|');
        }

        /// <summary>
        /// Drops every recorded expression that <paramref name="inst"/> may have made stale.
        ///
        /// <para>⛔ CSE had NO invalidation at all. Its key is a pair of NAMES, and a name is not
        /// a value: <c>a = p + q</c>, <c>p = Seed(100)</c>, <c>b = p + q</c> recorded
        /// <c>Add_p_q</c> against the first binop and then rewrote the second to
        /// <c>IRAssignment(b, a)</c> — <c>b</c> got the value computed BEFORE <c>p</c> changed.
        /// MEASURED in the DEFAULT pipeline (CSE is in <see cref="OptimizationPipeline.AddStandardPasses"/>),
        /// on both entry points: C++, JavaScript and MSIL all printed <c>b=4</c> where 106 is
        /// correct.</para>
        ///
        /// <para>⚠ C# printed the RIGHT answer, and that is not reassurance — it is the trap
        /// ADR-0001 records. C# is right only because its inline-always policy re-emits the
        /// binop's expression TEXT (<c>b = p + q;</c>) instead of honouring the IR's merge. A
        /// fixture that asserts only C# cannot see this defect, and a backend change that
        /// materialises more aggressively than ADR-0001's E1 requires would make the oracle wrong
        /// too. The repair belongs HERE, in the pass.</para>
        ///
        /// <para>Object identity is NOT an alternative key: MEASURED, IRBuilder hands BOTH reads
        /// of <c>p</c> the SAME <see cref="IRVariable"/> instance, and the redefinition mints no
        /// new one. Neither is <see cref="IRVariable.Version"/> — the redefinition above is an
        /// <see cref="IRCall"/> renamed <c>p</c>, which does not version anything. Without real
        /// SSA, killing on redefinition is the only available answer.</para>
        ///
        /// <para>The four definition forms are the ones
        /// <see cref="CopyPropagationPass"/> already enumerates, and they are enumerated here for
        /// the same measured reasons — in particular the renamed-<see cref="IRValue"/> form, which
        /// is how BOTH <c>Dim p = Seed(1)</c> and <c>p = p + 10</c> lower (no IRAssignment at all).
        /// The fifth, calls, is CSE-specific: see <see cref="ReadsCallVisible"/>.</para>
        /// </summary>
        private static void Invalidate(Dictionary<string, Candidate> expressions, IRInstruction inst)
        {
            if (expressions.Count == 0 || inst == null) return;

            var killed = NamesWrittenBy(inst, out bool isCall);

            List<string> stale = null;
            foreach (var entry in expressions)
            {
                bool dead = isCall && entry.Value.ReadsCallVisibleStorage;
                if (!dead && killed != null)
                {
                    // The defining instruction renames its own destination; that is the value
                    // being BORN, not a write that makes it stale (see Candidate).
                    string destination = ReferenceEquals(entry.Value.Value, inst) ? null : entry.Value.Destination;
                    foreach (var name in killed)
                    {
                        if (destination != null && string.Equals(destination, name, StringComparison.OrdinalIgnoreCase))
                        {
                            dead = true;
                            break;
                        }
                        foreach (var read in entry.Value.Reads)
                        {
                            if (string.Equals(read, name, StringComparison.OrdinalIgnoreCase))
                            {
                                dead = true;
                                break;
                            }
                        }
                        if (dead) break;
                    }
                }
                if (dead) (stale ??= new List<string>()).Add(entry.Key);
            }
            if (stale != null)
                foreach (var key in stale) expressions.Remove(key);
        }

        /// <summary>
        /// Whether an operand reads storage that a CALL can write without that write appearing in
        /// this block: a mutable global, or anything whose shape this walk does not recognise.
        ///
        /// <para>⛔ MEASURED. <c>a = Counter + q</c>, <c>z = Seed(100)</c>, <c>b = Counter + q</c>
        /// has NO syntactic redefinition of <c>Counter</c> anywhere in <c>Main</c> — the write
        /// happens inside <c>Seed</c> — and C++, JavaScript and MSIL all printed <c>b=4</c> where
        /// 104 is correct. Name-based invalidation over the block alone cannot see it.</para>
        ///
        /// <para>⚠ A <c>Const</c> global is exempt, and that exemption is what keeps the pass
        /// worth having: MEASURED, all 11 merges CSE makes across the repo's sample games read
        /// locals and <c>Const</c> globals (<c>TILE_SIZE</c>, <c>px</c>, <c>py</c>) with
        /// <c>DrawLine</c>/<c>DrawRectangle</c> calls interleaved. Killing on every call
        /// unconditionally would take all 11.</para>
        ///
        /// <para>⚠ NOT closed by this predicate: a local captured BY REFERENCE by a lambda that a
        /// call then invokes. Such a local has IsGlobal false and is indistinguishable here. That
        /// hazard is live TODAY and is NOT CSE's alone — measured on
        /// <c>Dim bump = Sub() n = n + 100</c>, CopyPropagation plus ConstantFolding already fold
        /// <c>n + q</c> to a constant on BOTH sides of <c>bump()</c>, so ALL FOUR backends
        /// (C# included) print the stale answer with CSE out of the picture. Closing it needs a
        /// capture set on IRFunction, which is a separate change.</para>
        /// </summary>
        private static bool ReadsCallVisible(IRValue value)
        {
            switch (value)
            {
                case null:
                case IRConstant:
                    return false;
                case IRVariable variable:
                    return (variable.IsGlobal && !variable.IsConst) || variable.IsByRef;
                case IRBinaryOp binary:
                    return ReadsCallVisible(binary.Left) || ReadsCallVisible(binary.Right);
                case IRUnaryOp unary:
                    return ReadsCallVisible(unary.Operand);
                case IRCompare compare:
                    return ReadsCallVisible(compare.Left) || ReadsCallVisible(compare.Right);
                case IRCast cast:
                    return ReadsCallVisible(cast.Value);
                default:
                    // Calls, field/indexer loads, allocations, and anything not enumerated: a
                    // call may change what they read. Over-killing costs optimization; keeping a
                    // stale entry is the outcome that miscompiles.
                    return true;
            }
        }

        // ⛔ A PRIVATE FOUR-ARM `ReplaceAllUses` USED TO LIVE HERE, and a private copy of the
        // temp-name test beside it. Both are gone: the base class already owns one TOTAL operand
        // walker (OptimizationPass.ReplaceUses) and one temp-name test (IsTempDestination), and
        // ConstantFoldingPass was pointed at the walker for exactly this reason. Two incomplete
        // walkers in one file is the ModuleResolver/ModuleTypeWalker rule in CLAUDE.md being
        // broken — the shared logic changes once, not per consumer.
        //
        // The copy rewrote IRBinaryOp.Left/Right, IRUnaryOp.Operand, IRStore.Value and
        // IRAssignment.Value — FOUR of the twenty-six consumer kinds. Every other kind kept
        // pointing at the instruction removed on the line below, and the backends then rendered
        // an identifier that is never declared. MEASURED on this four-line program, with NO
        // optimizer flag (CSE is in AddStandardPasses):
        //     Sub Run(a As Integer)
        //      Show(a + 7)
        //      Show(a + 7)   ' merged; this argument kept the REMOVED node
        //     End Sub
        // C++ emitted `t0 = a + 7; Show(t0); Show(t1);` and refused to compile ("use of
        // undeclared identifier 't1'"); MSIL assembled and threw InvalidProgramException at run
        // time. C# and JavaScript were RIGHT BY LUCK — both re-materialise the orphan's
        // expression text inline (`Show(a + 7)`), which happens to be correct here and is the
        // same inline-always policy ADR-0001 constrains for the opposite reason.
        //
        // Missing arms confirmed live by measurement, one program each: IRCall.Arguments,
        // IRReturn.Value, IRCompare.Left/Right, IRCast.Value, IRNewObject.Arguments and
        // IRInstanceMethodCall.Arguments. IRStore.Value was covered, which is why `arr(0) = a + b`
        // was green and looked like evidence the defect was narrow.
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

                // ⛔ A LOCAL is NOT invariant. It must return false HERE, before the
                // ParentBlock arm below, and this early return is the whole fix.
                //
                // IRValue derives from IRInstruction (IRNodes.cs), so an IRVariable falls into
                // `value is IRInstruction` below. But a bare IRVariable used as an OPERAND is not
                // itself placed in any block: MEASURED, every such operand carries
                // IsParameter=False, IsGlobal=False, ParentBlock=<null>. `loop.Contains(null)` is
                // false, so `!loop.Contains(inst.ParentBlock)` was TRUE and EVERY local read as
                // loop-invariant — including the loop's own induction variable.
                //
                // This is a hoist-anything licence, not a conservative approximation. It was
                // masked while ControlFlowGraph.FindBackEdges was inverted: the only usable bogus
                // loop set was [for0.cond, entry], which holds no body, so LICM could only reach
                // the condition. With the back-edge predicate corrected the body becomes visible
                // and the unfixed test hoists the induction variable straight out of its own
                // loop — MEASURED, it turned `For i = 0 To 7 : acc = acc + i` into
                // `while (0 <= 7) { i = 1; }`, breaking 8/8 loop programs on 4/4 backends.
                //
                // Deciding a local is invariant needs a reaching-definition check (no definition
                // of it inside the loop); nothing here computes one, and "not invariant" is the
                // safe answer in its absence — it costs a missed hoist, never a wrong program.
                //
                // LoopInvariantCodeMotionPass is UNREGISTERED (see AddAggressivePasses), so this
                // code is inert today. It is fixed now for exactly that reason: leaving a
                // known-wrong invariance test parked behind a disabled pass, to be re-enabled by
                // someone who trusts it, is the trap that produced this defect.
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
            // INTEGRAL OPERANDS ONLY. The guard used to test just the constant, so any
            // `x * 2^k` with an Integer literal matched whatever x was. MEASURED on
            // `Const Half As Double = 2.5` / `CStr(Half * 2)`: C# emitted `Half << 1` (CS0019),
            // C++ `Half << 1` (ill-formed on a double), and JavaScript `(Half << 1)`, which
            // COMPILES and prints 4 — `<<` truncates its operand to int32 first. Single, a
            // non-Const Double local and Decimal all matched the same way. Both the operand and
            // the result must be integral: the result type alone is not enough if a front end
            // ever widens, and the operand alone is not enough if the product is promoted.
            if (op.Operation == BinaryOpKind.Mul
                && op.Type != null && op.Type.IsIntegral()
                && op.Left.Type != null && op.Left.Type.IsIntegral())
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

            // LoopInvariantCodeMotionPass DISABLED — see the shared note above
            // LoopFusionPass/LoopUnrollingPass below. All three loop passes are unregistered
            // together, because they share one substrate (ControlFlowGraph.IdentifyLoops) and
            // there is no per-consumer opt-out of it.
            //
            // MEASURED at ef69a2c, all 13 CFG shapes compiled AND RUN out of process on all four
            // backends, CLI `--optimize` against CLI default. LICM is the ONLY one of the three
            // that fires today, and it is the whole of the aggressive pipeline's loop damage:
            //   * C++ and MSIL run every counted loop ZERO times (8 of 13 shapes: single/nested/
            //     sibling For, While, Do While, Exit For, For+If, For+Try).
            //   * JavaScript throws ReferenceError on For+Try (`t2`), Exit For (`t3`) and nested
            //     For (`t5`) — the hoisted temp is referenced where it was never declared.
            //   * ⛔ SILENT WRONG ANSWER on C# — the reference oracle — and on JavaScript: two
            //     sibling loops accumulating 0..7 each print 65 where 29 is correct. The bogus
            //     loop sets span BOTH loops, so loop 0's body `a = a + i` is moved into loop 1's
            //     latch, where it runs 8 more times with `i` frozen at 8: 1 + 8*8 = 65.
            // Default (non-optimize) output is correct on all 13 shapes on all four backends, so
            // every one of these is damage this pass adds.
            //
            // Repairing the substrate does NOT make this pass shippable. With the back-edge
            // predicate corrected (ControlFlowGraph.FindBackEdges), loop sets are right on 13/13
            // and LICM gets STRICTLY WORSE — 8/8 loop programs break on all four backends —
            // because correct sets expose a SECOND LICM defect that the bogus sets were masking:
            // IsValueInvariant read every local as invariant, so it hoisted the induction
            // variable itself out of its own loop. That defect is fixed in this same change (see
            // IsValueInvariant below); it is fixed rather than left behind precisely because a
            // known-wrong invariance test sitting behind a disabled pass is what produced this
            // incident in the first place.
            // AddPass(new LoopInvariantCodeMotionPass());

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

            // LoopFusionPass and LoopUnrollingPass DISABLED — they ship with LICM above and go
            // with it, on the same terms as ConstantPropagationPass, FunctionInliningPass and
            // InductionVariablePass below: the class stays in the file so a test can add it
            // explicitly, but nothing registers it.
            //
            // ⛔ NEITHER HAS EVER EXECUTED. MEASURED at ef69a2c across all 13 CFG shapes,
            // including shapes built to satisfy every gate each pass names: both report zero
            // modifications on every program. They refuse early, on the broken loop sets —
            // LoopUnrolling at CanUnroll's trip-count gate (FindInitialValue needs a predecessor
            // outside the loop, and `entry` is inside every bogus set), LoopFusion at
            // GetLoopBounds returning null. So "disabling" them removes nothing that any program
            // has ever received.
            //
            // They are unregistered rather than left alone because fixing the substrate would
            // TURN THEM ON for the first time, and both are broken when they fire. MEASURED with
            // the corrected back-edge predicate plus the IsValueInvariant fix, i.e. the exact
            // state of this file otherwise:
            //   * LoopUnrolling, counted call-free loop: emits doubly-prefixed undeclared names
            //     (`_u0__u0_i`, `_u0__u1_acc`) because the pass re-runs over its own output and
            //     CloneVariable mints names the optimizer has no facility to declare. CS0103 /
            //     C++ undeclared identifier / JS ReferenceError / MSIL InvalidProgramException —
            //     all four backends.
            //   * LoopFusion, two same-bound sibling loops: FuseLoops removes loop 2's blocks
            //     from function.Blocks while branches still target them. C++ "undeclared label
            //     'for0_inc'"; MSIL "Unable to find forward reference label 'for0inc'";
            //     JavaScript REFUSES the function outright; and C# is SILENTLY WRONG — 29,37
            //     where 29,29 is correct.
            // Turning on two never-run passes is not a side effect a substrate repair gets to
            // have, so the repair ships with them unregistered.
            // AddPass(new LoopFusionPass());  // Fuse adjacent loops before unrolling
            // AddPass(new LoopUnrollingPass(4));  // 4x unrolling

            // InductionVariablePass DISABLED — third entry in the list this method already keeps
            // (ConstantPropagationPass above, FunctionInliningPass above that), for the same
            // reason and on the same terms: it has never produced a correct program for any loop
            // it actually rewrites, and the aggressive pipeline SHIPS. `ProjectFile()` seeds
            // Configurations["Release"].OptimizationsEnabled = true (ProjectFile.cs:122-127), and
            // Program.cs:502 / BuildService.cs:629 pass that straight into
            // CompilerOptions.OptimizeAggressive, which reaches AddAggressivePasses at
            // Compiler.cs:292 and Compiler.cs:459. So a Release .blproj build took this pass.
            //
            // MEASURED at 67782af on `For i = 0 To n : Show(i * 3) : Next`, compiled AND RUN out
            // of process on all four backends, both entry points (CLI --optimize and a Release
            // .blproj through CompileProjectFiles). Emitted C# was:
            //     t2 = _div_t2 + 3;      // CS0103 on BOTH names
            //     Show(i * 3);           // ...and the original multiply is still here
            // C++ "use of undeclared identifier '_div_t2'"/"'t3'"; JavaScript ReferenceError
            // ("Cannot access '_div_t2' before initialization"); MSIL assembled and threw
            // InvalidProgramException. No backend was right by luck — unusually, all four fail.
            //
            // FIVE defects, not the two that were written down, and the two written-down ones are
            // not the ones that matter:
            //  1. The uses are never re-pointed. `block.Instructions[i]` is swapped for an
            //     IRAssignment while every consumer still holds the removed IRBinaryOp, so the
            //     derived IV is dead weight and the multiply is re-materialised from the orphan.
            //     Same omission as CSE and Peephole at 67782af.
            //  2. The derived IV is never added to IRFunction.LocalVariables. No pass in this file
            //     has ever written LocalVariables — the optimizer has no facility for declaring a
            //     variable it mints, and the only two passes that ever wanted one are this and
            //     FunctionInliningPass.
            //  3. ⛔ IT IS NEVER INITIALISED. There is no preheader store of `i_init * c`, and
            //     FindBasicInductionVariables does not even collect the initial value. So the
            //     first iteration reads garbage. `For i = 1 To n` needs the derived IV to start at
            //     3; nothing puts it there. THIS is the defect that makes a repair a rewrite,
            //     because placing the init needs a loop preheader — see below.
            //  4. The minted name `_div_{binOp.Name}` lives in the USER's namespace, which
            //     ADR-0001's Contract forbids for exactly this reason. MEASURED: a program with
            //     `Dim _div_x As Integer = 99` and `x = i * 3` COMPILES CLEANLY and prints
            //     198/204/210/216 where 99/102/105/108 is correct — a silent wrong answer, today,
            //     on C# (the reference oracle) and JavaScript. C++ and MSIL print neither the
            //     right nor the wrong numbers, because the separate LICM defect below stops the
            //     loop running at all; they are not evidence that this defect is narrow.
            //     And two multiplies onto one local (`x = i * 3` then
            //     `x = i * 5`) both mint `_div_x`, so both updates land in the same block;
            //     JavaScript reports "Identifier '_div_x' has already been declared".
            //  5. `basicIVs[ivVar.Name]` carries the increment block and the pass DISCARDS it
            //     (`var (increment, _) = ...`), then re-finds one with
            //     `loop.FirstOrDefault(b => b.Name.Contains(".inc"))` — any `.inc` block in the
            //     loop list, which for a nested loop can be the wrong loop's latch. There is also
            //     no check that the recognised increment is the ONLY definition of `i` in the
            //     loop: on `For i = 0 To n : Show(i * 3) : i = i + 1 : Next` the correct output is
            //     0, 6 and a derived IV stepping by 3 gives 0, 3.
            //  6. Even with 1-5 repaired, the pass's OWN minted update (`_div_t2 = _div_t2 + 3`)
            //     is then seen as loop-invariant by LoopInvariantCodeMotionPass on the next
            //     fixed-point iteration — both its operands read invariant — and gets moved out.
            //     MEASURED: running the pass before LICM takes LICM's modification count 4 -> 5;
            //     in the shipping order an extra `it1 Loop Invariant Code Motion mods=1` appears.
            //     The derived variable would be frozen across the loop even if everything above
            //     were fixed. Removing this pass therefore also shrinks the LICM defect's blast
            //     radius by one hoist per derived IV.
            //
            // ⛔ WHY REPAIR IS A REWRITE, MEASURED: fixing exactly the two defects that were
            // written down (1 and 2) does NOT fix the program — `t2` is a TEMP, so replacing the
            // IRValue that carried the name with an IRAssignment takes its DECLARATION away, and
            // C# still gave CS0103 on `t2`, JavaScript still threw. It also CONVERTED the
            // two-multiplies-onto-one-local shape from a loud build failure into a silent wrong
            // answer: 0,0,8,8,16,16,24,24 where 0,0,3,5,6,10,9,15 is correct, measured on C# and
            // JavaScript (the two backends whose emitters survive the LICM defect below, so the
            // two that can show a wrong VALUE at all). A loud failure traded for a quiet one is a
            // regression.
            // And defect 3 cannot be fixed inside this pass at all: it needs a preheader, and
            // ControlFlowGraph.IdentifyLoops — the substrate all four loop passes call — is wrong.
            // For a five-block function holding ONE loop it reports FOUR natural loops, every one
            // of them containing `entry`, and one containing the exit block. LICM's
            // `header.Predecessors.FirstOrDefault(p => !loopSet.Contains(p))` therefore resolves
            // the "preheader" to the loop's own LATCH (`for0.inc`). Repairing that is shared-
            // substrate work for the LICM task, not something to smuggle in here, and a pass that
            // cannot place an initialisation cannot fire correctly even once.
            //
            // Nothing is lost by not shipping it. LLVM's loop-strength-reduction, the CLR JIT and
            // V8 all perform this exact transform, better, downstream of every one of our
            // backends; and because defect 1 means the multiply was emitted anyway, the pass never
            // removed a single multiply even when it "succeeded". The class stays in the file, as
            // FunctionInliningPass does, so a test can add it explicitly.
            // AddPass(new InductionVariablePass());
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

            // ADR-0004 D2 / ADR-0005 D2: assert Invariant S′ over what the passes produced. A
            // no-op unless enabled (DEBUG builds, the test suite, BASICLANG_VERIFY_IR); it only
            // reads the IR, so output is identical either way.
            IRVerifier.VerifyAfterOptimization(module);

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
        /// Installs <paramref name="replacement"/> in place of <paramref name="original"/> and
        /// re-points every consumer, which is the half this pass used to omit entirely.
        ///
        /// <para>Returns true when the instruction was REMOVED rather than swapped, so the caller
        /// can step its index back.</para>
        ///
        /// <para>Two cases, and they behave oppositely — see <see cref="IsTempDestination"/>:</para>
        /// <list type="bullet">
        /// <item>A USER-NAMED destination is a declared local. The assignment stays, and consumers
        /// are re-pointed at its target, matching what StrengthReductionPass and
        /// AlgebraicSimplificationPass already do.</item>
        /// <item>A TEMP destination is declared only because an <c>IRValue</c> carried its name.
        /// Swapping in an <c>IRAssignment</c> takes the declaration away and leaves the write, so
        /// the definition is dropped and consumers are forwarded to the VALUE. The discarded
        /// operand's own defining instruction stays in the block, so a call on the side that an
        /// arm like <c>x * 0 -&gt; 0</c> discards is still evaluated.</item>
        /// </list>
        ///
        /// <para>Scoped to the whole FUNCTION, not this block, for the reason the other two passes
        /// state: a use may live in a later block.</para>
        /// </summary>
        private bool ApplyRewrite(IRFunction function, BasicBlock block, int index, IRValue original, IRInstruction replacement)
        {
            var stream = function.Blocks.SelectMany(b => b.Instructions);

            // ⛔ BOTH conjuncts, and the second is not belt-and-braces — it is a MEASURED
            // miscompile. `IsTempDestination` is a test on the SPELLING of the name, and a user
            // may spell a variable `t0`; CSharpFieldAssignmentTests already has a fixture for a
            // MEMBER named `t0`. On the name test alone, `t0 = n + 0` inside
            // `Class Timer : Public t0 As Integer` dropped the field write and emitted an EMPTY
            // method body — `Set1(5)` then printed 0 instead of 5, silently, on ALL FOUR backends.
            // `NamedAfterVariable` is the flag IRBuilder sets when it actually renames a value
            // after a variable, which is what the backends themselves consult to decide a value
            // IS a store; a genuine SSA temp never carries it. Keeping the assignment when EITHER
            // test says "real variable" costs only a missed rewrite; dropping it costs the write.
            if (replacement is IRAssignment assignment
                && IsTempDestination(original.Name)
                && !original.NamedAfterVariable)
            {
                ReplaceUses(stream, original, assignment.Value);
                block.Instructions.RemoveAt(index);
                ReportModification();
                return true;
            }

            if (replacement is IRAssignment named)
                InheritIdentity(named.Target, original);
            else if (replacement is IRValue value)
                InheritIdentity(value, original);

            var definition = replacement is IRAssignment a ? (IRValue)a.Target : replacement as IRValue;
            if (definition != null)
                ReplaceUses(stream, original, definition);

            block.Instructions[index] = replacement;
            ReportModification();
            return false;
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
                            // ⛔ THE MISSING HALF, and this pass is in AddStandardPasses — it runs
                            // with NO flag. It swapped the instruction and never re-pointed the
                            // CONSUMERS, which the base class's ReplaceUses doc says a pass doing so
                            // MUST; StrengthReductionPass and AlgebraicSimplificationPass both call
                            // it, and only this one did not. Worse than an orphan alone: every arm
                            // here returns an IRAssignment, which is NOT an IRValue, so a temp
                            // destination also loses its DECLARATION.
                            //
                            // MEASURED on `Show(a + 0)` — six lines, no flags — where C# emitted
                            //     t0 = a;            // CS0103: 't0' does not exist
                            //     Show(a + 0);       // orphan re-materialised the whole expression
                            // C++ gave "use of undeclared identifier 't0'" and JavaScript threw
                            // ReferenceError. MSIL was right BY LUCK (it declares locals from its
                            // own slot table, not from the IRValue stream). `Show(Tag() * 0)` broke
                            // the same way AND called Tag() twice, because the orphan re-rendered
                            // its operand tree.
                            if (ApplyRewrite(function, block, i, binOp, replacement)) i--;
                            changed = true;
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
                    // Same missing half as the binary arm above, and just as live: MEASURED on
                    // `Show(-(-a))`, which gave CS0103 't1' on C#, "use of undeclared identifier
                    // 't1'" on C++ and a ReferenceError on JavaScript.
                    if (inst is IRUnaryOp unary && unary.Operation == UnaryOpKind.Neg)
                    {
                        if (unary.Operand is IRUnaryOp innerUnary && innerUnary.Operation == UnaryOpKind.Neg)
                        {
                            var newAssign = new IRAssignment(
                                new IRVariable(unary.Name, unary.Type),
                                innerUnary.Operand);
                            if (ApplyRewrite(function, block, i, unary, newAssign)) i--;
                            changed = true;
                        }
                    }

                    // Pattern: Boolean not not -> identity
                    // MEASURED on `ShowB(Not (Not a))`: the same three failures.
                    if (inst is IRUnaryOp notOp && notOp.Operation == UnaryOpKind.Not)
                    {
                        if (notOp.Operand is IRUnaryOp innerNot && innerNot.Operation == UnaryOpKind.Not)
                        {
                            var newAssign = new IRAssignment(
                                new IRVariable(notOp.Name, notOp.Type),
                                innerNot.Operand);
                            if (ApplyRewrite(function, block, i, notOp, newAssign)) i--;
                            changed = true;
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

            // x * 0 -> 0. INTEGRAL ONLY — in IEEE 754 it is not the identity three ways, all
            // MEASURED on the JavaScript backend before this guard: Infinity * 0 is NaN (the rule
            // gave 0), NaN * 0 is NaN, and -5.0 * 0 is -0.0 (the rule gave +0.0, so `1.0 / e`
            // printed Infinity instead of -Infinity). Decimal is excluded too: .NET keeps the
            // scale, so 1.5D * 0 prints "0.0", not "0".
            if (binOp.Operation == BinaryOpKind.Mul && IsIntegralArithmetic(binOp))
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

            // x - x -> 0. INTEGRAL ONLY, for the same reason: Infinity - Infinity and NaN - NaN
            // are NaN. MEASURED: `Dim a As Double = inf - inf` compiled to `a = 0`.
            if (binOp.Operation == BinaryOpKind.Sub && IsIntegralArithmetic(binOp) &&
                binOp.Left is IRVariable left &&
                binOp.Right is IRVariable right &&
                left.Name == right.Name)
            {
                return new IRAssignment(
                    new IRVariable(binOp.Name, binOp.Type),
                    new IRConstant(0, binOp.Type));
            }

            // `x / x -> 1` REMOVED — unsound for EVERY type, and its "(when x != 0)" was never
            // checked. A float 0, Infinity or NaN gives NaN (MEASURED: `r = x / x` compiled to
            // `r = 1`, so DivSelf(0.0) returned 1); an integral 0 throws DivideByZeroException.
            // No type restriction rescues it, and nobody writes the shape on purpose.

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

        /// <summary>
        /// True when the op and both operands are integral, so the ring identities (x - x = 0,
        /// x * 0 = 0) hold. A null type counts as NOT integral: refusing costs one missed fold,
        /// guessing wrong produces a wrong answer. The same test StrengthReductionPass applies.
        /// </summary>
        private static bool IsIntegralArithmetic(IRBinaryOp binOp) =>
            binOp.Type != null && binOp.Type.IsIntegral()
            && binOp.Left?.Type != null && binOp.Left.Type.IsIntegral()
            && binOp.Right?.Type != null && binOp.Right.Type.IsIntegral();

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

            // 2 * x -> x + x. The VALUE is right: `x + x` is exactly `2 * x` in IEEE 754 (one rounding
            // either way) and wraps identically on integer overflow. That argument says NOTHING about
            // how many times `x` is EVALUATED, and this rewrite writes the SAME operand object into
            // both slots — a use count of 2. For `2 * Tag()` that is `Tag() + Tag()` on any backend
            // that inlines (the C# oracle printed "tag" twice, measured — ADR-0001's Premise status).
            // A value-preserving rewrite is not automatically an effect-preserving one, so the arm
            // fires only for an operand that may be evaluated twice: ADR-0001/ADR-0004 D4's gate,
            // the SAME predicate the C# backend's materialisation uses (IRReplicability), never a
            // private copy. It also satisfies ADR-0004's Invariant S trivially: both uses sit in one
            // instruction, so nothing can be assigned between them.
            if (binOp.Operation == BinaryOpKind.Mul)
            {
                if (binOp.Left is IRConstant c && c.Value is int i && i == 2
                    && IRReplicability.IsReplicable(binOp.Right))
                {
                    return new IRBinaryOp(binOp.Name, BinaryOpKind.Add, binOp.Right, binOp.Right, binOp.Type);
                }
                if (binOp.Right is IRConstant c2 && c2.Value is int i2 && i2 == 2
                    && IRReplicability.IsReplicable(binOp.Left))
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
