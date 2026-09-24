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

    /// <summary>One breach of Invariant S′.</summary>
    public sealed class InvariantViolation
    {
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

        public override string ToString() =>
            $"Invariant S′ violated in {Function}: '{Value?.Name}' ({Value?.GetType().Name}, {UseCount} use(s), "
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
    /// <para><b>"Assigned"</b> is <see cref="OptimizationPass.NamesWrittenBy"/> — the one kill
    /// vocabulary CSE's <c>Invalidate</c> also uses, so the verifier and the pass cannot
    /// disagree about what a write is — plus its call arm: a call writes a destination a callee
    /// can reach (<see cref="OptimizationPass.IsCallVisibleDestination"/>), exactly as CSE kills
    /// on it. A write form missing from that vocabulary is invisible to BOTH; see the gap list
    /// on <c>NamesWrittenBy</c>.</para>
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

            var violations = CheckInvariantSPrime(module);
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
            var writes = new Dictionary<IRInstruction, (List<string> Names, bool IsCall)>(ReferenceEqualityComparer.Instance);
            (List<string> Names, bool IsCall) WritesOf(IRInstruction inst)
            {
                if (!writes.TryGetValue(inst, out var w))
                {
                    var names = OptimizationPass.NamesWrittenBy(inst, out bool isCall);
                    writes[inst] = w = (names, isCall);
                }
                return w;
            }

            Dictionary<BasicBlock, List<BasicBlock>> predecessors = null;

            foreach (var (value, useSites) in uses)
            {
                if (value is not IRInstruction valueInst || !definitions.TryGetValue(valueInst, out var def))
                    continue;

                string destination = NamedDestination(value);
                bool shared = useSites.Count > 1 || (destination != null && useSites.Count >= 1);
                if (!shared) continue;

                var guard = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                if (IsPureOperator(value)) CollectOperandGuard(value, guard, isRoot: true);
                if (destination != null) guard[destination] = true;
                if (guard.Count == 0) continue;
                bool destinationCallVisible = destination != null
                    && OptimizationPass.IsCallVisibleDestination(destination, function);

                foreach (var use in useSites)
                {
                    InvariantViolation found = null;
                    foreach (var (block, index) in InstructionsBetween(def, use, inFunction,
                                 () => predecessors ??= Predecessors(function, inFunction)))
                    {
                        var writer = block.Instructions[index];
                        if (writer == null || ReferenceEquals(writer, valueInst)) continue;
                        var (names, isCall) = WritesOf(writer);

                        string hit = null;
                        if (names != null)
                            foreach (var name in names)
                                if (guard.ContainsKey(name)) { hit = name; break; }
                        if (hit == null && isCall && destinationCallVisible) hit = destination;
                        if (hit == null) continue;

                        found = new InvariantViolation
                        {
                            Function = function.Name,
                            Value = value,
                            UseCount = useSites.Count,
                            Variable = hit,
                            IsDestination = guard[hit],
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

        /// <summary>
        /// The destination a backend reads the value back through, or null for an anonymous
        /// temp — the same test CSE's named-destination arm applies.
        /// </summary>
        private static string NamedDestination(IRValue value)
        {
            if (string.IsNullOrEmpty(value.Name)) return null;
            // Two instruction kinds carry a name that is NOT a variable, and each was a false hit
            // when treated as one (measured over the suite at HEAD's passes):
            //  * IRAlloca — the name spells a STORAGE SLOT (`V_addr` for an array local); its
            //    uses read the slot's address, which no later write to `V` moves. Four hits
            //    over the fast subset, all `V_addr`/`N_addr` across a call.
            //  * IRConstant — a literal left in the block by ConstantFoldingPass
            //    (`const_there,` for a folded `s & ","`); every backend emits its VALUE, never
            //    reads it back by name, so nothing can make it stale. One hit, in
            //    AssignmentCoercionTests.ANonNumericStore_IsLeftAlone.
            if (value is IRAlloca || value is IRConstant) return null;
            if (value.NamedAfterVariable || !OptimizationPass.IsTempDestination(value.Name)) return value.Name;
            return null;
        }

        private static bool IsPureOperator(IRValue value) =>
            value is IRBinaryOp || value is IRUnaryOp || value is IRCompare || value is IRCast;

        /// <summary>The operand half of Guard(v); the bool records "is the destination" (false here).</summary>
        private static void CollectOperandGuard(IRValue value, Dictionary<string, bool> guard, bool isRoot)
        {
            switch (value)
            {
                case null:
                case IRConstant:
                    return;
                case IRVariable variable:
                    if (!string.IsNullOrEmpty(variable.Name) && IRReplicability.IsReplicable(variable)
                        && !guard.ContainsKey(variable.Name))
                        guard[variable.Name] = false;
                    return;
            }

            if (!isRoot)
            {
                if (!IRReplicability.IsReplicable(value)) return; // evaluated once, read back by name
                var named = NamedDestination(value);
                if (named != null)
                {
                    if (!guard.ContainsKey(named)) guard[named] = false;
                    return;
                }
            }

            switch (value)
            {
                case IRBinaryOp binary:
                    CollectOperandGuard(binary.Left, guard, false);
                    CollectOperandGuard(binary.Right, guard, false);
                    break;
                case IRUnaryOp unary:
                    CollectOperandGuard(unary.Operand, guard, false);
                    break;
                case IRCompare compare:
                    CollectOperandGuard(compare.Left, guard, false);
                    CollectOperandGuard(compare.Right, guard, false);
                    break;
                case IRCast cast:
                    CollectOperandGuard(cast.Value, guard, false);
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
