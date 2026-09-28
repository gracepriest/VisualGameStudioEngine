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
    /// ⭐ ADR-0014 D1/D2 for a backend with native closures: which locals of one function are
    /// declared at the top of a loop body instead of at function top, and the carrier each one is
    /// copied forward through.
    ///
    /// <para><c>perIter(loop) = loop.BodyLocals ∩ captureSet(function)</c>. Such a variable is not
    /// declared at function top; its CARRIER is, in its place, with the default the variable would
    /// have had there, and is never reset. The top of the loop's body declares the variable from its
    /// carrier (<c>T x = carry;</c> / <c>let x = carry;</c>), so a lambda created in the iteration
    /// captures that iteration's variable, and the value copied in is the previous iteration's —
    /// VB's copy-forward. An initializer is the ordinary IR assignment after it (D4). The carrier is
    /// written back at the CONTINUE TARGET only: before the step of a counted <c>For</c>
    /// (<see cref="AtContinueBlock"/>), at the end of the body of every other loop
    /// (<see cref="AtBodyEnd"/>). BasicLang has no <c>Continue</c> statement, so the only way to
    /// reach it is normal completion of the body; an <c>Exit</c>, a <c>Return</c> or an exception
    /// never writes it (D2).</para>
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
        private readonly Dictionary<BasicBlock, List<Entry>> _byContinue = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<IRVariable, Entry> _byVariable = new(ReferenceEqualityComparer.Instance);

        public bool IsEmpty => _byVariable.Count == 0;

        /// <summary>The declarations at the top of the body entered at <paramref name="body"/>.</summary>
        public IReadOnlyList<Entry> AtBody(BasicBlock body) =>
            body != null && _byBody.TryGetValue(body, out var list) ? list : Array.Empty<Entry>();

        /// <summary>The carrier writes placed just before <paramref name="block"/>'s instructions —
        /// non-empty only for a counted <c>For</c>'s step block.</summary>
        public IReadOnlyList<Entry> AtContinueBlock(BasicBlock block) =>
            block != null && _byContinue.TryGetValue(block, out var list) ? list : Array.Empty<Entry>();

        /// <summary>The carrier writes at the textual end of the body entered at
        /// <paramref name="body"/> — every loop but a counted <c>For</c>, whose writes are
        /// <see cref="AtContinueBlock"/>'s.</summary>
        public IReadOnlyList<Entry> AtBodyEnd(BasicBlock body)
        {
            var list = AtBody(body);
            return list.Count > 0 && list[0].Loop.Kind != IRLoopKind.For ? list : Array.Empty<Entry>();
        }

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
                    Add(plan._byBody, loop.Body, entry);
                    if (loop.Kind == IRLoopKind.For && loop.Continue != null) Add(plan._byContinue, loop.Continue, entry);
                }
            }
            return plan ?? Empty;
        }

        private static void Add(Dictionary<BasicBlock, List<Entry>> map, BasicBlock key, Entry entry)
        {
            if (!map.TryGetValue(key, out var list)) map[key] = list = new List<Entry>();
            list.Add(entry);
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
