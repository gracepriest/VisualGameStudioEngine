using System;
using System.Collections.Generic;
using System.Linq;
using BasicLang.Compiler.IR.Optimization;

namespace BasicLang.Compiler.IR
{
    /// <summary>The five loop forms, as the IR holds them (both <c>Do</c> forms are <see cref="Do"/>).</summary>
    public enum IRLoopKind { For, ForEach, While, Do }

    /// <summary>
    /// One loop of a function, read off the IR (ADR-0014). Every field is a block the IR already
    /// has; nothing here is stored anywhere.
    /// </summary>
    public sealed class IRLoop
    {
        public IRLoopKind Kind { get; init; }

        /// <summary>The body's entry block — the block that carries <see cref="BasicBlock.BodyLocals"/>.</summary>
        public BasicBlock Body { get; init; }

        /// <summary>
        /// The block the ordinary end of an iteration branches to: <c>forN.inc</c> for a counted
        /// <c>For</c>, <c>whileN.cond</c> / <c>doN.cond</c> for the others, and the
        /// <see cref="IRForEach.EndBlock"/> of a <c>For Each</c> (reached by a branch with
        /// <see cref="IRBranch.IsLoopExit"/> false). Null when the optimizer removed it as
        /// unreachable, i.e. no iteration can complete normally.
        /// </summary>
        public BasicBlock Continue { get; init; }

        /// <summary>The block after the loop, where <c>Exit</c> goes.</summary>
        public BasicBlock End { get; init; }

        /// <summary>The node, for a <c>For Each</c>; null otherwise.</summary>
        public IRForEach ForEach { get; init; }
    }

    /// <summary>
    /// ⭐ ADR-0014: the loops of a function, and the per-iteration plan the backends with native
    /// closures (C#, JavaScript) emit from.
    ///
    /// <para><b>How a loop is found.</b> A <c>For Each</c> is its <see cref="IRForEach"/>. The other
    /// four are the blocks IRBuilder creates under one prefix — <c>forN.cond/.body/.inc/.end</c>,
    /// <c>whileN.cond/.body/.end</c>, <c>doN.cond/.body/.end</c> — which is how every backend
    /// already recognises them (C#'s <c>IsLoopHeader</c>, JavaScript's <c>FindSibling</c>). Block
    /// names are unique across a module, and no optimization pass renames or creates a block.</para>
    /// </summary>
    public static class IRLoops
    {
        /// <summary>Every loop of <paramref name="function"/> whose body block is still in it, in block order.</summary>
        public static List<IRLoop> Of(IRFunction function)
        {
            var loops = new List<IRLoop>();
            if (function?.Blocks == null) return loops;

            var byName = new Dictionary<string, BasicBlock>(StringComparer.Ordinal);
            foreach (var block in function.Blocks)
                if (block?.Name != null) byName.TryAdd(block.Name, block);
            var present = new HashSet<BasicBlock>(function.Blocks.Where(b => b != null), ReferenceEqualityComparer.Instance);

            foreach (var block in function.Blocks)
            {
                if (block == null) continue;

                foreach (var inst in block.Instructions)
                    if (inst is IRForEach fe && fe.BodyBlock != null && present.Contains(fe.BodyBlock))
                        loops.Add(new IRLoop
                        {
                            Kind = IRLoopKind.ForEach,
                            Body = fe.BodyBlock,
                            Continue = fe.EndBlock,
                            End = fe.EndBlock,
                            ForEach = fe,
                        });

                if (!TryParseBody(block.Name, out var prefix, out var kind)) continue;
                BasicBlock Sibling(string suffix) => byName.TryGetValue(prefix + "." + suffix, out var b) ? b : null;
                loops.Add(new IRLoop
                {
                    Kind = kind,
                    Body = block,
                    Continue = Sibling(kind == IRLoopKind.For ? "inc" : "cond"),
                    End = Sibling("end"),
                });
            }
            return loops;
        }

        /// <summary><c>forN.body</c>, <c>whileN.body</c> or <c>doN.body</c> — never <c>foreachN.body</c>
        /// (a For Each is found by its node) or <c>tryN.body</c>.</summary>
        private static bool TryParseBody(string name, out string prefix, out IRLoopKind kind)
        {
            prefix = null;
            kind = IRLoopKind.For;
            if (name == null || !name.EndsWith(".body", StringComparison.Ordinal)) return false;
            var head = name.Substring(0, name.Length - ".body".Length);
            foreach (var (word, k) in new[] { ("for", IRLoopKind.For), ("while", IRLoopKind.While), ("do", IRLoopKind.Do) })
            {
                if (head.Length > word.Length && head.StartsWith(word, StringComparison.Ordinal)
                    && head.Substring(word.Length).All(char.IsDigit))
                {
                    prefix = head;
                    kind = k;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Each loop by its end block — what an <c>Exit</c> branches to.</summary>
        public static Dictionary<BasicBlock, IRLoop> ByEnd(IEnumerable<IRLoop> loops)
        {
            var byEnd = new Dictionary<BasicBlock, IRLoop>(ReferenceEqualityComparer.Instance);
            foreach (var loop in loops) if (loop.End != null) byEnd.TryAdd(loop.End, loop);
            return byEnd;
        }

        /// <summary>
        /// The blocks of <paramref name="loop"/>'s body: everything reachable from its body block
        /// without passing its continue block or its end, and without following an <c>Exit</c> out of
        /// a loop that ENCLOSES it (an <c>Exit Do</c> inside a For inside a Do leaves both). An exit is
        /// inside the body of the loop it leaves, so when that loop is nested in this one its body
        /// block has already been reached by the time the exit is.
        /// </summary>
        public static HashSet<BasicBlock> BodyRegion(IRLoop loop, IReadOnlyDictionary<BasicBlock, IRLoop> byEnd)
        {
            var region = new HashSet<BasicBlock>(ReferenceEqualityComparer.Instance);
            var stack = new Stack<BasicBlock>();
            stack.Push(loop.Body);
            while (stack.Count > 0)
            {
                var b = stack.Pop();
                if (b == null || ReferenceEquals(b, loop.Continue) || ReferenceEquals(b, loop.End) || !region.Add(b)) continue;
                var exit = b.GetTerminator() as IRBranch;
                foreach (var s in ControlFlowGraph.SuccessorsOf(b))
                {
                    if (exit != null && exit.IsLoopExit && ReferenceEquals(exit.Target, s)
                        && byEnd.TryGetValue(s, out var left) && !ReferenceEquals(left, loop) && !region.Contains(left.Body))
                        continue;
                    stack.Push(s);
                }
            }
            return region;
        }

        /// <summary>
        /// The names of the non-global VARIABLES <paramref name="inst"/> reads or writes, as the
        /// backends see them: an assignment's or a variable store's target, a value renamed after a
        /// variable, the variable of an <c>x_addr</c> slot, and every <see cref="IRVariable"/>
        /// operand (descending only into operand trees that live in no block, such as a When guard —
        /// an operand that is itself an instruction is that instruction's business). ADR-0014 A2's
        /// "IR reference", shared by IRBuilder (which records a Dim only when S″ already holds for it)
        /// and the verifier (which checks S″ after every pass), so the two cannot disagree.
        /// </summary>
        public static IEnumerable<string> VariableMentions(IRInstruction inst)
        {
            if (inst == null) yield break;
            switch (inst)
            {
                case IRAssignment a when a.Target?.Name != null && !a.Target.IsGlobal:
                    yield return a.Target.Name;
                    break;
                case IRStore { Address: IRVariable address } when address.Name != null && !address.IsGlobal:
                    yield return address.Name;
                    break;
                case IRAlloca slot when slot.Name != null && slot.Name.EndsWith("_addr", StringComparison.Ordinal):
                    yield return slot.Name.Substring(0, slot.Name.Length - "_addr".Length);
                    break;
            }
            if (inst is IRValue value && !(inst is IRVariable) && OptimizationPass.NamedDestination(value) is string named)
                yield return named;

            var seen = new HashSet<IRValue>(ReferenceEqualityComparer.Instance);
            var stack = new Stack<IRValue>(OptimizationPass.UsesOf(inst));
            while (stack.Count > 0)
            {
                var v = stack.Pop();
                if (v == null || !seen.Add(v)) continue;
                if (v is IRVariable variable)
                {
                    if (variable.Name != null && !variable.IsGlobal) yield return variable.Name;
                    continue;
                }
                if (v is IRInstruction nested && nested.ParentBlock == null)
                    foreach (var u in OptimizationPass.UsesOf(nested)) stack.Push(u);
            }
        }

        /// <summary>
        /// ADR-0014 A2: <c>perIter(L)</c> for every loop of <paramref name="function"/> as the IR stands
        /// now — its body's <see cref="BasicBlock.BodyLocals"/> the function's lambdas capture — with
        /// the loop's body region. Empty for a function with none, which is every lambda-free one.
        /// </summary>
        public static List<(IRLoop Loop, HashSet<BasicBlock> Region, List<IRVariable> PerIter)> PerIteration(
            IRModule module, IRFunction function)
        {
            var result = new List<(IRLoop, HashSet<BasicBlock>, List<IRVariable>)>();
            if (function?.Blocks == null || !function.Blocks.Any(b => b?.BodyLocals?.Count > 0)) return result;
            if (OptimizationPass.LambdaReferences(function).Count == 0) return result;

            var loops = Of(function);
            Dictionary<BasicBlock, IRLoop> byEnd = null;
            foreach (var loop in loops)
            {
                var perIter = loop.Body.BodyLocals
                    .Where(v => v?.Name != null && IsCaptured(module, function, v.Name)).ToList();
                if (perIter.Count == 0) continue;
                byEnd ??= ByEnd(loops);
                result.Add((loop, BodyRegion(loop, byEnd), perIter));
            }
            return result;
        }

        /// <summary>
        /// Whether a lambda <paramref name="creator"/> creates may capture its variable
        /// <paramref name="name"/> — #122's capture set as <see cref="OptimizationPass.IsLambdaCaptured"/>
        /// answers it (the interim "every local" rule included), plus every name a lambda it creates,
        /// or one nested in those, CALLS by bare name: a delegate variable invoked as <c>g()</c> is a
        /// read of <c>g</c> that #122's set does not list, and <c>ClosureLowering</c> adds the same
        /// names to the set it hoists from (ADR-0010's implementation notes). A function that creates
        /// no lambda captures nothing, which is what keeps every such function byte-identical.
        /// </summary>
        public static bool IsCaptured(IRModule module, IRFunction creator, string name)
        {
            if (creator == null || string.IsNullOrEmpty(name)) return false;
            if (OptimizationPass.LambdaReferences(creator).Count == 0) return false;
            if (OptimizationPass.IsLambdaCaptured(name, creator)) return true;
            return BareCallNames(module, creator).Contains(name);
        }

        private static HashSet<string> BareCallNames(IRModule module, IRFunction creator)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (module?.Functions == null) return names;
            var lambdas = new Dictionary<string, IRFunction>(StringComparer.Ordinal);
            foreach (var f in module.Functions)
                if (f != null && f.IsLambda && f.Name != null) lambdas.TryAdd(f.Name, f);

            var seen = new HashSet<IRFunction>(ReferenceEqualityComparer.Instance);
            var pending = new Stack<IRFunction>();
            void Enqueue(IRFunction from)
            {
                foreach (var reference in OptimizationPass.LambdaReferences(from))
                    if (lambdas.TryGetValue(reference, out var lambda) && seen.Add(lambda)) pending.Push(lambda);
            }
            Enqueue(creator);
            while (pending.Count > 0)
            {
                var lambda = pending.Pop();
                var visited = new HashSet<IRValue>(ReferenceEqualityComparer.Instance);
                var stack = new Stack<IRValue>();
                foreach (var block in lambda.Blocks ?? new List<BasicBlock>())
                    foreach (var inst in block.Instructions)
                    {
                        if (inst == null) continue;
                        if (inst is IRValue self) stack.Push(self);
                        foreach (var u in OptimizationPass.UsesOf(inst)) stack.Push(u);
                        while (stack.Count > 0)
                        {
                            var v = stack.Pop();
                            if (v == null || !visited.Add(v)) continue;
                            if (v is IRCall { CalleeValue: null } call && !string.IsNullOrEmpty(call.FunctionName)
                                && call.FunctionName.IndexOf('.') < 0 && !call.FunctionName.Contains("::"))
                                names.Add(call.FunctionName);
                            if (v is IRInstruction nested && !(v is IRVariable))
                                foreach (var u in OptimizationPass.UsesOf(nested)) stack.Push(u);
                        }
                    }
                Enqueue(lambda);
            }
            return names;
        }
    }

    /// <summary>
    /// ⭐ ADR-0014 D1 and A1 for a backend with native closures: which locals of one function are
    /// declared at the top of a loop body instead of at function top, and the carrier each one is
    /// copied forward through.
    ///
    /// <para><c>perIter(loop) = loop.BodyLocals ∩ captureSet(function)</c>. Such a variable is not
    /// declared at function top; its CARRIER is, in its place, with the default the variable would
    /// have had there, and is never reset. The top of the loop's body declares the variable from its
    /// carrier (<c>T x = carry;</c> / <c>let x = carry;</c>), so a lambda created in the iteration
    /// captures that iteration's variable, and the value copied in is the previous iteration's —
    /// VB's copy-forward. An initializer is the ordinary IR assignment after it (D4).</para>
    ///
    /// <para><b>A1:</b> the rest of the body is a <c>try</c> whose <c>finally</c> writes the carrier
    /// back, so it is written however the iteration is left — its end, an <c>Exit</c>, a
    /// <c>Return</c>, an exception — after every user <c>Finally</c> it crosses, in nesting order.
    /// A counted <c>For</c>'s step block is emitted AFTER that finally
    /// (<see cref="DeferredStep"/>): the step is not part of the body.</para>
    ///
    /// <para><see cref="Empty"/> — every function with no captured loop-body <c>Dim</c> — changes
    /// nothing a backend emits (D6).</para>
    /// </summary>
    public sealed class PerIterationPlan
    {
        public sealed class Entry
        {
            public IRVariable Variable { get; init; }
            /// <summary>The carrier's IR-level name, unique in the function.</summary>
            public string Carrier { get; init; }
            public IRLoop Loop { get; init; }
        }

        public static readonly PerIterationPlan Empty = new PerIterationPlan();

        private readonly Dictionary<BasicBlock, List<Entry>> _byBody = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<BasicBlock> _deferredSteps = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<IRVariable, Entry> _byVariable = new(ReferenceEqualityComparer.Instance);

        public bool IsEmpty => _byVariable.Count == 0;

        /// <summary>The captured locals of the body entered at <paramref name="body"/>: declared at its
        /// top, and written back by the finally that closes it.</summary>
        public IReadOnlyList<Entry> AtBody(BasicBlock body) =>
            body != null && _byBody.TryGetValue(body, out var list) ? list : Array.Empty<Entry>();

        /// <summary>A counted For's step block when the body entered at <paramref name="body"/> is
        /// wrapped: emitted after the finally, never from inside the body. Null otherwise.</summary>
        public BasicBlock DeferredStep(BasicBlock body)
        {
            var list = AtBody(body);
            return list.Count > 0 && list[0].Loop.Kind == IRLoopKind.For ? list[0].Loop.Continue : null;
        }

        /// <summary>Whether <paramref name="block"/> is a wrapped counted For's step block, which a
        /// branch from inside the body must not emit (the body's end falls to the finally, and the
        /// step follows it).</summary>
        public bool IsDeferredStep(BasicBlock block) => block != null && _deferredSteps.Contains(block);

        /// <summary>The entry for a local of the function, or null when it is declared at function top as before.</summary>
        public Entry For(IRVariable local) =>
            local != null && _byVariable.TryGetValue(local, out var entry) ? entry : null;

        public static PerIterationPlan Build(IRModule module, IRFunction function)
        {
            if (function?.Blocks == null || !function.Blocks.Any(b => b?.BodyLocals?.Count > 0)) return Empty;
            if (OptimizationPass.LambdaReferences(function).Count == 0) return Empty;

            PerIterationPlan plan = null;
            HashSet<string> taken = null;
            foreach (var loop in IRLoops.Of(function))
            {
                foreach (var variable in loop.Body.BodyLocals)
                {
                    if (variable?.Name == null || !IRLoops.IsCaptured(module, function, variable.Name)) continue;
                    if (plan == null)
                    {
                        plan = new PerIterationPlan();
                        taken = NamesIn(module, function);
                    }
                    if (plan._byVariable.ContainsKey(variable)) continue;

                    var carrier = "__carry_" + variable.Name;
                    for (var k = 1; !taken.Add(carrier); k++) carrier = $"__carry_{variable.Name}_{k}";

                    var entry = new PerIterationPlan.Entry { Variable = variable, Carrier = carrier, Loop = loop };
                    plan._byVariable[variable] = entry;
                    if (!plan._byBody.TryGetValue(loop.Body, out var list)) plan._byBody[loop.Body] = list = new List<Entry>();
                    list.Add(entry);
                    if (loop.Kind == IRLoopKind.For && loop.Continue != null) plan._deferredSteps.Add(loop.Continue);
                }
            }
            return plan ?? Empty;
        }

        /// <summary>Every name a carrier must not collide with: the function's parameters, locals and
        /// values, and the module's globals.</summary>
        private static HashSet<string> NamesIn(IRModule module, IRFunction function)
        {
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in function.Parameters ?? new List<IRVariable>()) if (p?.Name != null) taken.Add(p.Name);
            foreach (var l in function.LocalVariables ?? new List<IRVariable>()) if (l?.Name != null) taken.Add(l.Name);
            foreach (var block in function.Blocks)
                foreach (var inst in block.Instructions)
                {
                    if (inst is IRValue v && v.Name != null) taken.Add(v.Name);
                    foreach (var u in OptimizationPass.UsesOf(inst)) if (u?.Name != null) taken.Add(u.Name);
                }
            if (module?.GlobalVariables != null)
                foreach (var g in module.GlobalVariables.Values) if (g?.Name != null) taken.Add(g.Name);
            return taken;
        }
    }
}
