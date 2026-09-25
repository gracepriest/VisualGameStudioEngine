using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BasicLang.Compiler.IR.Optimization
{
    /// <summary>What <see cref="IRVerifier.VerifyAfterOptimization"/> does with a violation.</summary>
    public enum IRVerifierMode
    {
        /// <summary>Not run. The production default.</summary>
        Off,
        /// <summary>Throw <see cref="IRVerificationException"/>. Debug builds, and the test suite.</summary>
        Throw,
        /// <summary>Append each violation to <see cref="IRVerifier.LogPath"/> and carry on — for
        /// measuring a whole suite's IR in one run (ADR-0004 D2's "run the verifier over the
        /// suite").</summary>
        Log,
    }

    /// <summary>One breach of Invariant S′, or of Invariant V (ADR-0006 D1).</summary>
    public sealed class InvariantViolation
    {
        /// <summary>Which invariant: <c>"S′"</c> (a shared value's guarded variable written
        /// before a use) or <c>"V"</c> (an instruction kind the kill vocabulary does not
        /// classify; only <see cref="Function"/>, <see cref="Writer"/> and
        /// <see cref="WriterBlock"/> are set).</summary>
        public string Invariant { get; init; } = "S′";
        public string Function { get; init; }
        /// <summary>The shared value.</summary>
        public IRValue Value { get; init; }
        public int UseCount { get; init; }
        /// <summary>The guarded variable that was written.</summary>
        public string Variable { get; init; }
        /// <summary>True when <see cref="Variable"/> is the value's own named destination.</summary>
        public bool IsDestination { get; init; }
        /// <summary>The instruction that wrote it.</summary>
        public IRInstruction Writer { get; init; }
        public string DefinitionBlock { get; init; }
        public string WriterBlock { get; init; }
        public string UseBlock { get; init; }

        public override string ToString() => Invariant == "V"
            ? $"Invariant V violated in {Function}: {Writer?.GetType().Name} in {WriterBlock} is an instruction "
              + "kind the kill vocabulary (OptimizationPass.NamesWrittenBy) does not classify, so every pass "
              + "treats it as writing everything. Give it an arm there, stating what it writes."
            : $"Invariant S′ violated in {Function}: '{Value?.Name}' ({Value?.GetType().Name}, {UseCount} use(s), "
            + $"defined in {DefinitionBlock}) — {(IsDestination ? "its destination" : "operand")} '{Variable}' "
            + $"is written by {Writer?.GetType().Name}{(Writer is IRValue w && !string.IsNullOrEmpty(w.Name) ? " '" + w.Name + "'" : "")} "
            + $"in {WriterBlock} before a use in {UseBlock}.";
    }

    public sealed class IRVerificationException : Exception
    {
        public IReadOnlyList<InvariantViolation> Violations { get; }

        public IRVerificationException(IReadOnlyList<InvariantViolation> violations)
            : base("IR verification failed after optimization:\n  "
                   + string.Join("\n  ", violations.Select(v => v.ToString())))
        {
            Violations = violations;
        }
    }

    /// <summary>
    /// The post-optimizer IR verifier ADR-0004 D2 obliges, asserting Invariant S′ as ADR-0005 D2
    /// widened it:
    ///
    /// <para><b>S′:</b> for any instruction <c>v</c> with use count &gt; 1, no variable in
    /// <c>Guard(v)</c> is assigned between <c>v</c>'s definition and its last use, where
    /// <c>Guard(v)</c> = the variables reachable through <c>v</c>'s replicable operands ∪
    /// { <c>v</c>'s named destination, if any }.</para>
    ///
    /// <para><b>V</b> (ADR-0006 D1): no reachable instruction kind is unclassified — every
    /// instruction's kind has an arm in <see cref="OptimizationPass.NamesWrittenBy"/>. See
    /// <see cref="CheckInvariantV"/>. This is the verifier's independence from the vocabulary it
    /// shares with CSE: not a second write model, but completeness of the one model.</para>
    ///
    /// <para><b>"Assigned"</b> is <see cref="OptimizationPass.NamesWrittenBy"/> — the one kill
    /// vocabulary CSE's <c>Invalidate</c> also uses, so the verifier and the pass cannot
    /// disagree about what a write is — plus its call arm: a call writes EVERY name in
    /// <c>Guard(v)</c> a callee can reach, operand or destination alike
    /// (<see cref="OptimizationPass.IsCallVisible(IRVariable, IRFunction)"/>, the one
    /// call-visibility rule, ADR-0006 D3), exactly as CSE kills on it. Before D3 this arm
    /// checked the destination only, so a merge across a call that writes a class field read
    /// bare as an OPERAND (Q3: <c>a = K + q : Inc() : l(0) = K + q</c>) was certified. A write
    /// form missing from the vocabulary is invisible to BOTH; see the gap list on
    /// <c>NamesWrittenBy</c>.</para>
    ///
    /// <para>⚠ <b>"Use count" for a value with a named destination counts the destination as
    /// a reader.</b> A merge re-points a duplicate's consumers at the surviving instruction, and
    /// for the shape ADR-0005 D2 was written against (<c>Dim a = p + q : a = Seed(0) :
    /// l(0) = p + q</c>) that leaves <c>a</c>'s binop with ONE operand use — MEASURED at HEAD,
    /// on every destination-reassignment shape probed. Read literally ("&gt; 1 operand use"),
    /// the verifier would certify exactly the miscompile the ADR says it must not. The variable
    /// is itself a consumer of the value (every later read of <c>a</c> by name sees it), so a
    /// named value is shared once it has one operand use. Anonymous values keep the literal
    /// "&gt; 1". This reading is recorded as an assumption pending the architect.</para>
    ///
    /// <para><b>Guard, operand half:</b> only a pure operator (<see cref="IRBinaryOp"/>,
    /// <see cref="IRUnaryOp"/>, <see cref="IRCompare"/>, <see cref="IRCast"/>) is ever
    /// re-emitted, so only its operand tree contributes: a replicable variable by name; an
    /// operand instruction with a named destination by that name (every backend reads it by
    /// name); an anonymous pure operand recursively. A call's arguments are never re-read — the
    /// call is evaluated once — so they contribute nothing. <b>Destination half:</b>
    /// unconditional on replicability, per the ADR.</para>
    ///
    /// <para><b>"Between"</b> is path-based over <see cref="ControlFlowGraph.SuccessorsOf"/>:
    /// every write on some path from the definition to a use that does not re-execute the
    /// definition. The CFG is read, never rebuilt, so verifying cannot change what a backend
    /// sees.</para>
    ///
    /// <para><b>When it runs:</b> at the end of <see cref="OptimizationPipeline.Run"/>, so every
    /// shipping route (CLI, CLI <c>--optimize</c>, <c>.blproj</c>, the IDE) and every in-process
    /// test helper that runs a pipeline is covered. <see cref="Mode"/> is resolved once from, in
    /// order: the environment variable <c>BASICLANG_VERIFY_IR</c> (<c>0</c>/<c>off</c>,
    /// <c>1</c>/<c>throw</c>, or <c>log:&lt;path&gt;</c>); the runtime switch
    /// <c>BasicLang.VerifyIR</c> (the test project sets it, so a <c>-c Release</c> suite run
    /// verifies — a <c>Debug.Assert</c> would compile away there); otherwise <c>Throw</c> in a
    /// DEBUG build of the compiler and <c>Off</c> in Release. Release output is unchanged: in
    /// <c>Off</c> nothing is computed, and in the other modes nothing is written to the IR.</para>
    /// </summary>
    public static class IRVerifier
    {
        public const string SwitchName = "BasicLang.VerifyIR";
        public const string EnvironmentVariable = "BASICLANG_VERIFY_IR";

        private static readonly object LogLock = new object();
        private static IRVerifierMode? _mode;
        private static string _logPath;

        /// <summary>The active mode. Settable so a test can pin it; see the class remarks for
        /// how it is resolved when nothing has set it.</summary>
        public static IRVerifierMode Mode
        {
            get
            {
                if (_mode == null) Resolve();
                return _mode.Value;
            }
            set => _mode = value;
        }

        /// <summary>Where <see cref="IRVerifierMode.Log"/> appends.</summary>
        public static string LogPath
        {
            get
            {
                if (_mode == null) Resolve();
                return _logPath;
            }
            set => _logPath = value;
        }

        private static void Resolve()
        {
            var env = Environment.GetEnvironmentVariable(EnvironmentVariable)?.Trim();
            if (!string.IsNullOrEmpty(env))
            {
                if (env.StartsWith("log:", StringComparison.OrdinalIgnoreCase))
                {
                    _logPath = env.Substring(4);
                    _mode = IRVerifierMode.Log;
                    return;
                }
                if (env == "0" || env.Equals("off", StringComparison.OrdinalIgnoreCase) || env.Equals("false", StringComparison.OrdinalIgnoreCase))
                {
                    _mode = IRVerifierMode.Off;
                    return;
                }
                _mode = IRVerifierMode.Throw;
                return;
            }

            if (AppContext.TryGetSwitch(SwitchName, out bool enabled))
            {
                _mode = enabled ? IRVerifierMode.Throw : IRVerifierMode.Off;
                return;
            }

#if DEBUG
            _mode = IRVerifierMode.Throw;
#else
            _mode = IRVerifierMode.Off;
#endif
        }

        /// <summary>
        /// Run by <see cref="OptimizationPipeline.Run"/> after its last iteration. Does nothing in
        /// <see cref="IRVerifierMode.Off"/>.
        /// </summary>
        public static void VerifyAfterOptimization(IRModule module)
        {
            var mode = Mode;
            if (mode == IRVerifierMode.Off || module == null) return;

            var violations = CheckInvariantV(module).Concat(CheckInvariantSPrime(module)).ToList();
            if (violations.Count == 0) return;

            if (mode == IRVerifierMode.Log)
            {
                var path = LogPath;
                if (string.IsNullOrEmpty(path)) return;
                lock (LogLock)
                {
                    File.AppendAllLines(path, violations.Select(v => v.ToString()));
                }
                return;
            }

            throw new IRVerificationException(violations);
        }

        /// <summary>
        /// Every breach of Invariant V (ADR-0006 D1) in <paramref name="module"/>: "no reachable
        /// instruction kind is unclassified". An instruction whose kind
        /// <see cref="OptimizationPass.NamesWrittenBy"/> has no arm for is treated by every pass as
        /// writing everything, which is safe for output — but it means someone added an IR node
        /// kind without deciding what it writes, and the next such kind may be one whose
        /// classification matters. Reads the IR only.
        ///
        /// <para>Checks every instruction of every block the passes can see, a superset of the
        /// reachable ones (a pass does not skip an unreachable block, so neither does this).</para>
        /// </summary>
        public static IReadOnlyList<InvariantViolation> CheckInvariantV(IRModule module)
        {
            var violations = new List<InvariantViolation>();
            if (module?.Functions == null) return violations;
            foreach (var function in module.Functions)
            {
                if (function == null || function.IsExternal || function.Blocks == null) continue;
                foreach (var block in function.Blocks)
                {
                    if (block?.Instructions == null) continue;
                    foreach (var inst in block.Instructions)
                    {
                        if (inst == null || OptimizationPass.NamesWrittenBy(inst, function).IsClassified) continue;
                        violations.Add(new InvariantViolation
                        {
                            Invariant = "V",
                            Function = function.Name,
                            Writer = inst,
                            WriterBlock = block.Name,
                        });
                    }
                }
            }
            return violations;
        }

        /// <summary>Every breach of Invariant S′ in <paramref name="module"/>. Reads the IR only.</summary>
        public static IReadOnlyList<InvariantViolation> CheckInvariantSPrime(IRModule module)
        {
            var violations = new List<InvariantViolation>();
            if (module?.Functions == null) return violations;
            foreach (var function in module.Functions)
            {
                if (function == null || function.IsExternal || function.Blocks == null || function.Blocks.Count == 0)
                    continue;
                CheckFunction(function, violations);
            }
            return violations;
        }

        private readonly struct Site
        {
            public readonly BasicBlock Block;
            public readonly int Index;
            public Site(BasicBlock block, int index) { Block = block; Index = index; }
        }

        private static void CheckFunction(IRFunction function, List<InvariantViolation> violations)
        {
            var definitions = new Dictionary<IRInstruction, Site>(ReferenceEqualityComparer.Instance);
            var uses = new Dictionary<IRValue, List<Site>>(ReferenceEqualityComparer.Instance);
            var inFunction = new HashSet<BasicBlock>(function.Blocks, ReferenceEqualityComparer.Instance);

            foreach (var block in function.Blocks)
            {
                for (int i = 0; i < block.Instructions.Count; i++)
                {
                    var inst = block.Instructions[i];
                    if (inst == null) continue;
                    definitions[inst] = new Site(block, i);
                    foreach (var used in OptimizationPass.UsesOf(inst))
                    {
                        if (used is not IRInstruction) continue; // variables and constants are not definitions
                        if (!uses.TryGetValue(used, out var sites)) uses[used] = sites = new List<Site>();
                        sites.Add(new Site(block, i));
                    }
                }
            }

            // The kill vocabulary, per instruction, computed once.
            var writes = new Dictionary<IRInstruction, WriteSet>(ReferenceEqualityComparer.Instance);
            WriteSet WritesOf(IRInstruction inst)
            {
                if (!writes.TryGetValue(inst, out var w))
                    writes[inst] = w = OptimizationPass.NamesWrittenBy(inst, function);
                return w;
            }

            Dictionary<BasicBlock, List<BasicBlock>> predecessors = null;

            foreach (var (value, useSites) in uses)
            {
                if (value is not IRInstruction valueInst || !definitions.TryGetValue(valueInst, out var def))
                    continue;

                string destination = OptimizationPass.NamedDestination(value);
                bool shared = useSites.Count > 1 || (destination != null && useSites.Count >= 1);
                if (!shared) continue;

                var guard = new Dictionary<string, GuardName>(StringComparer.OrdinalIgnoreCase);
                if (IsPureOperator(value)) CollectOperandGuard(value, guard, function, isRoot: true);
                if (destination != null)
                    guard[destination] = new GuardName(true,
                        OptimizationPass.IsCallVisible(destination, function)
                        || (guard.TryGetValue(destination, out var asOperand) && asOperand.CallVisible));
                if (guard.Count == 0) continue;

                // The call arm's candidates, destination first so a report names it when it is
                // one (the pre-D3 arm checked only the destination).
                string callHit = destination != null && guard[destination].CallVisible ? destination : null;
                if (callHit == null)
                    foreach (var (name, entry) in guard)
                        if (entry.CallVisible) { callHit = name; break; }

                // A writer of EVERYTHING (ADR-0006 D1: an unclassified kind, or one classified as
                // universal) hits every guarded name; the report names the destination first.
                string anyHit = destination;
                if (anyHit == null)
                    foreach (var name in guard.Keys) { anyHit = name; break; }

                foreach (var use in useSites)
                {
                    InvariantViolation found = null;
                    foreach (var (block, index) in InstructionsBetween(def, use, inFunction,
                                 () => predecessors ??= Predecessors(function, inFunction)))
                    {
                        var writer = block.Instructions[index];
                        if (writer == null || ReferenceEquals(writer, valueInst)) continue;
                        var written = WritesOf(writer);

                        string hit = null;
                        if (written.IsUniversal)
                            hit = anyHit;
                        else
                        {
                            foreach (var name in written.Names)
                                if (guard.ContainsKey(name)) { hit = name; break; }
                            if (hit == null && written.IsCall) hit = callHit;
                        }
                        if (hit == null) continue;

                        found = new InvariantViolation
                        {
                            Function = function.Name,
                            Value = value,
                            UseCount = useSites.Count,
                            Variable = hit,
                            IsDestination = guard[hit].IsDestination,
                            Writer = writer,
                            DefinitionBlock = def.Block.Name,
                            WriterBlock = block.Name,
                            UseBlock = use.Block.Name,
                        };
                        break;
                    }
                    if (found != null)
                    {
                        violations.Add(found);
                        break; // one report per value is enough to name it
                    }
                }
            }
        }

        // The destination a backend reads the value back through is
        // OptimizationPass.NamedDestination — shared with CSE, which asks the same "does this
        // destination name a variable?" before asking whether a call can write it. It carries the
        // two measured exclusions (IRAlloca, IRConstant) this file used to keep privately.

        private static bool IsPureOperator(IRValue value) =>
            value is IRBinaryOp || value is IRUnaryOp || value is IRCompare || value is IRCast;

        /// <summary>One name in Guard(v): whether it is v's own destination, and whether a call
        /// can write it (<see cref="OptimizationPass.IsCallVisible(IRVariable, IRFunction)"/>).</summary>
        private readonly record struct GuardName(bool IsDestination, bool CallVisible);

        private static void AddOperandGuard(Dictionary<string, GuardName> guard, string name, bool callVisible)
        {
            // The same name reached twice (`p + p`, or a variable and a renamed value spelled
            // alike) is ONE guarded variable; it is call-visible if either route says so.
            guard[name] = guard.TryGetValue(name, out var existing)
                ? existing with { CallVisible = existing.CallVisible || callVisible }
                : new GuardName(false, callVisible);
        }

        /// <summary>The operand half of Guard(v) (<see cref="GuardName.IsDestination"/> false here).</summary>
        private static void CollectOperandGuard(IRValue value, Dictionary<string, GuardName> guard,
            IRFunction function, bool isRoot)
        {
            switch (value)
            {
                case null:
                case IRConstant:
                    return;
                case IRVariable variable:
                    if (!string.IsNullOrEmpty(variable.Name) && IRReplicability.IsReplicable(variable))
                        AddOperandGuard(guard, variable.Name, OptimizationPass.IsCallVisible(variable, function));
                    return;
            }

            if (!isRoot)
            {
                if (!IRReplicability.IsReplicable(value)) return; // evaluated once, read back by name
                var named = OptimizationPass.NamedDestination(value);
                if (named != null)
                {
                    AddOperandGuard(guard, named, OptimizationPass.IsCallVisible(named, function));
                    return;
                }
            }

            switch (value)
            {
                case IRBinaryOp binary:
                    CollectOperandGuard(binary.Left, guard, function, false);
                    CollectOperandGuard(binary.Right, guard, function, false);
                    break;
                case IRUnaryOp unary:
                    CollectOperandGuard(unary.Operand, guard, function, false);
                    break;
                case IRCompare compare:
                    CollectOperandGuard(compare.Left, guard, function, false);
                    CollectOperandGuard(compare.Right, guard, function, false);
                    break;
                case IRCast cast:
                    CollectOperandGuard(cast.Value, guard, function, false);
                    break;
            }
        }

        private static IEnumerable<BasicBlock> Successors(BasicBlock block, HashSet<BasicBlock> inFunction)
        {
            foreach (var successor in ControlFlowGraph.SuccessorsOf(block))
                if (successor != null && inFunction.Contains(successor))
                    yield return successor;
        }

        private static Dictionary<BasicBlock, List<BasicBlock>> Predecessors(IRFunction function, HashSet<BasicBlock> inFunction)
        {
            var map = new Dictionary<BasicBlock, List<BasicBlock>>(ReferenceEqualityComparer.Instance);
            foreach (var block in function.Blocks) map[block] = new List<BasicBlock>();
            foreach (var block in function.Blocks)
                foreach (var successor in Successors(block, inFunction))
                    map[successor].Add(block);
            return map;
        }

        /// <summary>
        /// Every instruction position on some path from <paramref name="def"/> to
        /// <paramref name="use"/> that does not pass through the definition again (re-executing
        /// it makes a new value), exclusive of both ends.
        /// </summary>
        private static IEnumerable<(BasicBlock Block, int Index)> InstructionsBetween(
            Site def, Site use, HashSet<BasicBlock> inFunction,
            Func<Dictionary<BasicBlock, List<BasicBlock>>> predecessors)
        {
            var d = def.Block;
            var u = use.Block;

            // Straight-line: the use follows the definition in its own block.
            if (ReferenceEquals(d, u) && use.Index > def.Index)
            {
                for (int i = def.Index + 1; i < use.Index; i++) yield return (d, i);
                yield break;
            }

            // Forward from d's successors without re-entering d.
            var forward = new HashSet<BasicBlock>(ReferenceEqualityComparer.Instance);
            bool loopsBackToDefinition = false;
            var stack = new Stack<BasicBlock>();
            foreach (var s in Successors(d, inFunction)) stack.Push(s);
            while (stack.Count > 0)
            {
                var b = stack.Pop();
                if (ReferenceEquals(b, d)) { loopsBackToDefinition = true; continue; }
                if (!forward.Add(b)) continue;
                foreach (var s in Successors(b, inFunction)) stack.Push(s);
            }

            bool reachable = ReferenceEquals(d, u) ? loopsBackToDefinition : forward.Contains(u);
            if (!reachable) yield break;

            // Backward from u's predecessors without re-entering d.
            var backward = new HashSet<BasicBlock>(ReferenceEqualityComparer.Instance);
            var preds = predecessors();
            if (preds.TryGetValue(u, out var up))
                foreach (var p in up) stack.Push(p);
            while (stack.Count > 0)
            {
                var b = stack.Pop();
                if (ReferenceEquals(b, d)) continue;
                if (!backward.Add(b)) continue;
                if (preds.TryGetValue(b, out var bp))
                    foreach (var p in bp) stack.Push(p);
            }

            // The rest of the definition's block.
            for (int i = def.Index + 1; i < d.Instructions.Count; i++) yield return (d, i);

            // Blocks strictly between (and the use's own block when it sits on a cycle that
            // avoids the definition — then all of it precedes some execution of the use).
            bool useBlockCycles = !ReferenceEquals(d, u) && forward.Contains(u) && backward.Contains(u);
            foreach (var b in forward)
            {
                if (ReferenceEquals(b, u) || !backward.Contains(b)) continue;
                for (int i = 0; i < b.Instructions.Count; i++) yield return (b, i);
            }

            if (useBlockCycles)
            {
                // Including the use itself: its own write precedes its next execution.
                for (int i = 0; i < u.Instructions.Count; i++) yield return (u, i);
            }
            else
            {
                for (int i = 0; i < use.Index; i++) yield return (u, i);
            }
        }
    }
}
