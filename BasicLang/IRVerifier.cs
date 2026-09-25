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

    /// <summary>One breach of Invariant S′, of Invariant V (ADR-0006 D1), or of Invariant F
    /// (ADR-0007).</summary>
    public sealed class InvariantViolation
    {
        /// <summary>Which invariant: <c>"S′"</c> (a shared value's guarded variable written
        /// before a use), <c>"V"</c> (an instruction kind the kill vocabulary does not
        /// classify; only <see cref="Function"/>, <see cref="Writer"/> and
        /// <see cref="WriterBlock"/> are set) or <c>"F"</c> (an <see cref="IRVariable"/> spelling
        /// an accessor-backed property of the function's class; <see cref="Function"/>,
        /// <see cref="Variable"/>, <see cref="IsDestination"/>, <see cref="Writer"/> — the
        /// instruction holding it —, <see cref="WriterBlock"/> and <see cref="DeclaringClass"/>
        /// are set).</summary>
        public string Invariant { get; init; } = "S′";
        /// <summary>For Invariant F: the class that declares the property.</summary>
        public string DeclaringClass { get; init; }
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
        /// <summary>True when the use in <see cref="UseBlock"/> lies in a loop that does not
        /// contain the definition, so it runs again before the definition does (ADR-0006 D2).
        /// <see cref="UseCount"/> stays the STATIC count; this says the reported use is
        /// dynamically repeated, which is what made a single anonymous use shared.</summary>
        public bool UseRepeats { get; init; }

        public override string ToString() => Invariant == "V"
            ? $"Invariant V violated in {Function}: {Writer?.GetType().Name} in {WriterBlock} is an instruction "
              + "kind the kill vocabulary (OptimizationPass.NamesWrittenBy) does not classify, so every pass "
              + "treats it as writing everything. Give it an arm there, stating what it writes."
            : Invariant == "F"
            ? $"Invariant F violated in {Function}: {Writer?.GetType().Name} in {WriterBlock} "
              + $"{(IsDestination ? "writes" : "reads")} a VARIABLE '{Variable}', which spells a property of "
              + $"{DeclaringClass} whose accessors run user code. A bare property name must lower to the node "
              + "its qualified form produces (IRFieldStore / IRFieldAccess), never to a variable (ADR-0007)."
            : $"Invariant S′ violated in {Function}: '{Value?.Name}' ({Value?.GetType().Name}, {UseCount} use(s), "
            + $"defined in {DefinitionBlock}{(UseRepeats ? $"; the use in {UseBlock} repeats in a loop that does not contain the definition" : "")}) "
            + $"— {(IsDestination ? "its destination" : "operand")} '{Variable}' "
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
    /// widened it, ADR-0006 D2 made dynamic and ADR-0008 D1 made blind to replicability:
    ///
    /// <para><b>S′:</b> for any instruction <c>v</c> with use count &gt; 1, no variable in
    /// <c>Guard(v)</c> is assigned between <c>v</c>'s definition and its last use, where
    /// <c>Guard(v)</c> = the storage <see cref="OptimizationPass.CollectReads"/> finds in
    /// <c>v</c> ∪ { <c>v</c>'s named destination, if any }.</para>
    ///
    /// <para><b>What S′ certifies</b> (ADR-0008 D1): a value the optimizer moved or shared keeps
    /// its source semantics, judged under IR semantics. A value is computed once per execution of
    /// its definition, and a named destination materialises it. How a backend materialises a
    /// value (the C# backend inlines a single-use one) is not the verifier's concern. A backend
    /// that re-evaluates a value away from its definition has an IR-to-backend fidelity defect,
    /// never an S′ one.</para>
    ///
    /// <para><b>The use count is DYNAMIC</b> (ADR-0006 D2): a use that lies in a loop that does
    /// not contain the definition counts as repeated, because it runs again before the definition
    /// does — the shape <see cref="LoopInvariantCodeMotionPass"/> leaves behind, one static use of
    /// a hoisted value that executes every iteration. "Lies in a loop that does not contain the
    /// definition" is read off the CFG as: the use's block is on a cycle that avoids the
    /// definition's block (<see cref="DefUsePaths.UseRepeats"/>). A use in the definition's own
    /// block, or one every cycle reaches only through the definition, is not repeated: each
    /// execution of it sees a fresh value. Before D2 a hoisted anonymous value with one use was
    /// never checked, so a hoist across a write was invisible to the verifier.</para>
    ///
    /// <para><b>V</b> (ADR-0006 D1): no reachable instruction kind is unclassified — every
    /// instruction's kind has an arm in <see cref="OptimizationPass.NamesWrittenBy"/>. See
    /// <see cref="CheckInvariantV"/>. This is the verifier's independence from the vocabulary it
    /// shares with CSE: not a second write model, but completeness of the one model.</para>
    ///
    /// <para><b>F</b> (ADR-0007, "lowering fidelity"): for each function f, no
    /// <see cref="IRVariable"/> — operand or destination — spells a member with accessors declared
    /// on f's enclosing class or on a base class present in the IR module. See
    /// <see cref="CheckInvariantF"/>. V is the vocabulary's COMPLETENESS arm; F is its FIDELITY
    /// arm: user code must reach the IR as a node the vocabulary classifies as a call, not hide
    /// behind a variable the vocabulary rightly treats as storage.</para>
    ///
    /// <para><b>"Assigned"</b> is <see cref="OptimizationPass.NamesWrittenBy"/> — the one kill
    /// vocabulary CSE's <c>Invalidate</c> also uses, so the verifier and the pass cannot
    /// disagree about what a write is — plus its call arm: a call writes EVERY name in
    /// <c>Guard(v)</c> a callee can reach, operand or destination alike
    /// (<see cref="OptimizationPass.IsCallVisible(IRVariable, IRFunction)"/>, the one
    /// call-visibility rule, ADR-0006 D3), exactly as CSE kills on it. Before D3 this arm
    /// checked the destination only, so a merge across a call that writes a class field read
    /// bare as an OPERAND (Q3: <c>a = K + q : Inc() : l(0) = K + q</c>) was certified. An
    /// instruction KIND missing from the vocabulary is caught by Invariant V; user code lowered
    /// to a non-call kind (a bare property as a variable) by Invariant F; a classified kind
    /// whose answer is too narrow is invisible to the passes and to all three checks (only
    /// execution probes see it) — see the note on <c>NamesWrittenBy</c>. An instruction that may
    /// write anything (<see cref="WriteKind.Universal"/>) hits every guarded name.</para>
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
    /// <para><b>Guard, operand half</b> (ADR-0008 D1): <see cref="OptimizationPass.CollectReads"/>,
    /// the ONE operand walk <see cref="OptimizationPass.ReadsCallVisible"/> also answers from, so
    /// the verifier and the passes cannot disagree about what a value reads. It descends through
    /// the pure operators (<see cref="IRBinaryOp"/>, <see cref="IRUnaryOp"/>,
    /// <see cref="IRCompare"/>, <see cref="IRCast"/>) and collects every variable it reaches, and
    /// every instruction it reaches that has a named destination, by that name (every backend
    /// reads it back by name). It still descends into a named pure operand, as
    /// <c>ReadsCallVisible</c> does. It stops at a call-shaped node (a call, an instance or base
    /// call, an allocation, a field or indexer load, an await): the node is evaluated once where
    /// it is defined, so its arguments are not re-read and contribute nothing. A non-pure value
    /// guards only its own name. <b>Destination half:</b> the value's named destination.</para>
    ///
    /// <para>⛔ <b>Blind to replicability</b> (ADR-0008 D1, striking ADR-0005 D2's "non-replicable
    /// operands are left out"). A <c>ByRef</c> parameter or a non-<c>Const</c> global is
    /// guarded like any other variable, for every value S′ checks, statically or dynamically
    /// shared. The old prune certified backend agreement instead of the optimizer: a hoisted
    /// <c>n * 2</c> over a <c>ByRef</c> <c>n</c> across a call that writes <c>n</c>'s storage
    /// printed 6 for 12 on C++ and MSIL and was reported by no one (MEASURED with LICM's
    /// call-visible read check disabled). C# printed 12 only because it re-reads <c>n</c> inline.
    /// Today's LICM refuses that hoist on its own (it never treats a global as invariant, and it
    /// counts a call-visible read as written). <see cref="IRReplicability"/> is a backend concept
    /// (the C# backend's "may I inline?") and is not consulted here.</para>
    ///
    /// <para><b>"Between"</b> is path-based over <see cref="ControlFlowGraph.SuccessorsOf"/>:
    /// every write on some path from the definition to a use that does not re-execute the
    /// definition. For a repeated use that is <c>region(v)</c> as ADR-0006 D2 defines it: it
    /// includes the FULL body of the loop the use repeats in — the back edge, and the use's own
    /// block after the use — so a write textually after the use but dynamically before its next
    /// execution is between. A write on a path that leaves the loop and never reaches the use
    /// again (an exit path) is not. The CFG is read, never rebuilt, so verifying cannot change
    /// what a backend sees.</para>
    ///
    /// <para>⚠ <b>Structured loops are not cycles.</b> <c>For Each</c> lowers to an
    /// <see cref="IRForEach"/> whose body has no back edge in the CFG (ADR-0003 D3), so a use in
    /// a For Each body is not seen as repeated. No pass moves a value into one today: CSE is
    /// block-local and LICM hoists only out of natural loops, which a For Each is not.</para>
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

            var violations = CheckInvariantV(module).Concat(CheckInvariantF(module))
                .Concat(CheckInvariantSPrime(module)).ToList();
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

        /// <summary>
        /// Every breach of Invariant F (ADR-0007, "lowering fidelity") in <paramref name="module"/>:
        /// for each function f, no <see cref="IRVariable"/> — operand or destination — spells a
        /// member with accessors declared on f's enclosing class or on a base class present in the
        /// IR module. Reads the IR only.
        ///
        /// <para><b>Why.</b> A property used by its bare name inside its own class runs its
        /// accessor. Lowered as a variable (<c>IRAssignment %P = 10</c>, a read of
        /// <c>%Tick</c>), the IR states something false, and no per-kind answer of the kill
        /// vocabulary can see it: V is quiet (both kinds are classified) while CSE merges across
        /// user code — MEASURED, JavaScript and MSIL printed stale values (P4, P5). IRBuilder now
        /// lowers such a name to <see cref="IRFieldStore"/> / <see cref="IRFieldAccess"/>; this is
        /// what makes a regression of that lowering structurally detectable rather than visible
        /// only to an execution probe on a non-C# backend.</para>
        ///
        /// <para><b>The terms, as read off the IR.</b></para>
        /// <list type="bullet">
        /// <item><b>f's enclosing class</b>: the class whose method, constructor or property
        /// accessor f implements; a lambda (<c>__lambda_N</c>) belongs to the class of the
        /// function that creates it. A function in no class is not checked.</item>
        /// <item><b>A member with accessors</b>: <see cref="IRProperty.IsAccessorBacked"/> — a
        /// Get/Set accessor function, or Overridable/Overrides. A plain field and a plain
        /// auto-property are storage, and a bare name for one is a variable: NOT a breach. The
        /// NEAREST member of a name wins, walking up <see cref="IRClass.BaseClass"/> while the base
        /// is in the module, so a derived field shadowing a base property is not a breach
        /// either.</item>
        /// <item><b>Spells</b>: the same name, ignoring case (BasicLang is case-insensitive),
        /// unless f DECLARES it — a parameter, a local, a For Each / Catch / pattern variable, or
        /// (for a lambda) a declaration of an enclosing function — or the variable is a module
        /// global. Those shadow the member, exactly as every backend resolves them.</item>
        /// <item><b>Operand</b>: every value reachable through <see cref="OptimizationPass.UsesOf"/>,
        /// operand trees included. <b>Destination</b>: an <see cref="IRAssignment"/> or
        /// <see cref="IRStore"/> target, and a value renamed after a variable
        /// (<see cref="OptimizationPass.NamedDestination"/> — <c>P = K + 1</c> lowered by renaming
        /// the add to <c>P</c> is a variable write too).</item>
        /// </list>
        /// <para>Checks every instruction of every block in <see cref="IRFunction.Blocks"/>, the
        /// same superset of the reachable ones <see cref="CheckInvariantV"/> checks.</para>
        /// </summary>
        public static IReadOnlyList<InvariantViolation> CheckInvariantF(IRModule module)
        {
            var violations = new List<InvariantViolation>();
            if (module?.Functions == null || module.Classes == null || module.Classes.Count == 0) return violations;

            // f -> the class it is a member of.
            var owners = new Dictionary<IRFunction, IRClass>(ReferenceEqualityComparer.Instance);
            void Own(IRFunction function, IRClass cls)
            {
                if (function != null && cls != null) owners.TryAdd(function, cls);
            }
            foreach (var cls in module.Classes.Values)
            {
                if (cls == null) continue;
                foreach (var method in cls.Methods ?? new List<IRMethod>()) Own(method?.Implementation, cls);
                foreach (var ctor in cls.Constructors ?? new List<IRConstructor>()) Own(ctor?.Implementation, cls);
                foreach (var prop in cls.Properties ?? new List<IRProperty>())
                {
                    Own(prop?.Getter, cls);
                    Own(prop?.Setter, cls);
                }
            }

            // A lambda belongs to its creator's class and sees its creator's declarations. IRBuilder
            // lowers a lambda to its own function named __lambda_N and leaves the creator a
            // variable of that name (the reference OptimizationPass.ContainsLambda keys on too).
            var lambdas = new Dictionary<string, IRFunction>(StringComparer.Ordinal);
            foreach (var function in module.Functions)
                if (function != null && function.IsLambda && function.Name != null) lambdas.TryAdd(function.Name, function);
            var creators = new Dictionary<IRFunction, IRFunction>(ReferenceEqualityComparer.Instance);
            if (lambdas.Count > 0)
            {
                var pending = new Queue<IRFunction>(owners.Keys);
                while (pending.Count > 0)
                {
                    var creator = pending.Dequeue();
                    foreach (var variable in VariablesIn(creator))
                    {
                        if (variable.Name == null || !lambdas.TryGetValue(variable.Name, out var lambda)) continue;
                        if (ReferenceEquals(lambda, creator) || owners.ContainsKey(lambda)) continue;
                        owners[lambda] = owners[creator];
                        creators[lambda] = creator;
                        pending.Enqueue(lambda);
                    }
                }
            }

            var accessorsByClass = new Dictionary<IRClass, Dictionary<string, string>>(ReferenceEqualityComparer.Instance);
            foreach (var function in module.Functions)
            {
                if (function == null || function.IsExternal || function.Blocks == null) continue;
                if (!owners.TryGetValue(function, out var cls)) continue;

                if (!accessorsByClass.TryGetValue(cls, out var accessors))
                    accessorsByClass[cls] = accessors = AccessorBackedMembers(cls, module);
                if (accessors.Count == 0) continue;

                var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (var scope = function; scope != null; scope = creators.TryGetValue(scope, out var outer) ? outer : null)
                    AddDeclarations(scope, declared);

                var seen = new HashSet<IRValue>(ReferenceEqualityComparer.Instance);
                foreach (var block in function.Blocks)
                {
                    if (block?.Instructions == null) continue;
                    foreach (var inst in block.Instructions)
                    {
                        if (inst == null) continue;

                        void Report(string name, bool isDestination) => violations.Add(new InvariantViolation
                        {
                            Invariant = "F",
                            Function = function.Name,
                            Variable = name,
                            IsDestination = isDestination,
                            Writer = inst,
                            WriterBlock = block.Name,
                            DeclaringClass = accessors[name],
                        });
                        bool Spells(IRVariable v) =>
                            v?.Name != null && !v.IsGlobal && !v.IsParameter
                            && accessors.ContainsKey(v.Name) && !declared.Contains(v.Name);

                        // Destinations.
                        if (inst is IRAssignment assignment && Spells(assignment.Target))
                            Report(assignment.Target.Name, true);
                        else if (inst is IRStore store && store.Address is IRVariable address && Spells(address))
                            Report(address.Name, true);
                        else if (inst is IRValue value && OptimizationPass.NamedDestination(value) is string named
                                 && accessors.ContainsKey(named) && !declared.Contains(named))
                            Report(named, true);

                        // Operands, through operand trees.
                        var stack = new Stack<IRValue>();
                        foreach (var used in OptimizationPass.UsesOf(inst)) stack.Push(used);
                        while (stack.Count > 0)
                        {
                            var operand = stack.Pop();
                            if (operand == null || !seen.Add(operand)) continue;
                            if (operand is IRVariable variable)
                            {
                                if (Spells(variable)) Report(variable.Name, false);
                                continue;
                            }
                            if (operand is IRInstruction nested)
                                foreach (var used in OptimizationPass.UsesOf(nested)) stack.Push(used);
                        }
                    }
                }
            }
            return violations;
        }

        /// <summary>
        /// The accessor-backed property names in scope in <paramref name="cls"/>'s methods, each
        /// mapped to its declaring class: the class's own members, then each base's while the base
        /// is in <paramref name="module"/>, the NEAREST member of a name winning — so a field, an
        /// auto-property or a method that shadows a base property removes the name.
        /// </summary>
        private static Dictionary<string, string> AccessorBackedMembers(IRClass cls, IRModule module)
        {
            var accessors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var visited = new HashSet<IRClass>(ReferenceEqualityComparer.Instance);
            for (var current = cls; current != null && visited.Add(current);)
            {
                foreach (var prop in current.Properties ?? new List<IRProperty>())
                    if (prop?.Name != null && claimed.Add(prop.Name) && prop.IsAccessorBacked)
                        accessors[prop.Name] = current.Name;
                foreach (var field in current.Fields ?? new List<IRField>())
                    if (field?.Name != null) claimed.Add(field.Name);
                foreach (var method in current.Methods ?? new List<IRMethod>())
                    if (method?.Name != null) claimed.Add(method.Name);
                foreach (var evt in current.Events ?? new List<IREvent>())
                    if (evt?.Name != null) claimed.Add(evt.Name);

                if (string.IsNullOrEmpty(current.BaseClass) || !module.Classes.TryGetValue(current.BaseClass, out current))
                    break;
            }
            return accessors;
        }

        /// <summary>The names <paramref name="function"/> declares: parameters, locals, and the
        /// variables a For Each, a Catch or a Select Case pattern binds (IRBuilder leaves those out
        /// of <see cref="IRFunction.LocalVariables"/>; the kill vocabulary names them as the
        /// variables those constructs write).</summary>
        private static void AddDeclarations(IRFunction function, HashSet<string> declared)
        {
            foreach (var parameter in function.Parameters ?? new List<IRVariable>())
                if (parameter?.Name != null) declared.Add(parameter.Name);
            foreach (var local in function.LocalVariables ?? new List<IRVariable>())
                if (local?.Name != null) declared.Add(local.Name);
            foreach (var block in function.Blocks ?? new List<BasicBlock>())
            {
                if (block?.Instructions == null) continue;
                foreach (var inst in block.Instructions)
                    if (inst is IRForEach || inst is IRTryCatch || inst is IRSwitch)
                        foreach (var name in OptimizationPass.NamesWrittenBy(inst, function).Names)
                            declared.Add(name);
            }
        }

        /// <summary>Every <see cref="IRVariable"/> <paramref name="function"/>'s instructions use,
        /// through operand trees.</summary>
        private static IEnumerable<IRVariable> VariablesIn(IRFunction function)
        {
            if (function?.Blocks == null) yield break;
            var seen = new HashSet<IRValue>(ReferenceEqualityComparer.Instance);
            var stack = new Stack<IRValue>();
            foreach (var block in function.Blocks)
            {
                if (block?.Instructions == null) continue;
                foreach (var inst in block.Instructions)
                {
                    if (inst == null) continue;
                    foreach (var used in OptimizationPass.UsesOf(inst)) stack.Push(used);
                    while (stack.Count > 0)
                    {
                        var operand = stack.Pop();
                        if (operand == null || !seen.Add(operand)) continue;
                        if (operand is IRVariable variable) yield return variable;
                        else if (operand is IRInstruction nested)
                            foreach (var used in OptimizationPass.UsesOf(nested)) stack.Push(used);
                    }
                }
            }
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

                // The STATIC count (ADR-0005 D2): more than one use, or a named destination and one
                // (the variable is itself a reader; see the class remarks).
                bool staticallyShared = useSites.Count > 1 || (destination != null && useSites.Count >= 1);

                // Otherwise the value is anonymous with ONE use, and the DYNAMIC count decides
                // (ADR-0006 D2): it is shared when that use lies in a loop that does not contain
                // the definition. Only a pure operator can then have a guard (an anonymous
                // non-pure value is evaluated once and has no destination to be re-read through),
                // so nothing else is worth a walk.
                if (!staticallyShared && !IsPureOperator(value)) continue;

                // Guard(v) = CollectReads(v).Names ∪ { v's destination } (ADR-0008 D1): the same
                // walk ReadsCallVisible answers from, blind to replicability. A non-pure value
                // stops the walk at once, so it guards only its own name, which is its destination.
                var guard = new Dictionary<string, GuardName>(StringComparer.OrdinalIgnoreCase);
                foreach (var read in OptimizationPass.CollectReads(value).Names)
                    AddOperandGuard(guard, read.Name ?? "", OptimizationPass.IsCallVisible(read, function));
                if (destination != null)
                    guard[destination] = new GuardName(true,
                        OptimizationPass.IsCallVisible(destination, function)
                        || (guard.TryGetValue(destination, out var asOperand) && asOperand.CallVisible));
                if (guard.Count == 0) continue;

                DefUsePaths PathsTo(Site use) => DefUsePaths.Between(def, use, inFunction,
                    () => predecessors ??= Predecessors(function, inFunction));

                // The dynamic count needs the one use's paths; the region below reuses them.
                DefUsePaths onlyUse = null;
                if (!staticallyShared)
                {
                    onlyUse = PathsTo(useSites[0]);
                    if (!onlyUse.UseRepeats) continue;
                }

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
                    var paths = onlyUse ?? PathsTo(use);
                    foreach (var (block, index) in paths.Region())
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
                            UseRepeats = paths.UseRepeats,
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
        /// The paths from a definition to ONE of its uses that do not pass through the definition
        /// again (re-executing it makes a new value), over
        /// <see cref="ControlFlowGraph.SuccessorsOf"/>. Computed once per use, it answers both
        /// questions S′ asks of that use: is it repeated (<see cref="UseRepeats"/>, ADR-0006 D2),
        /// and which instructions lie between (<see cref="Region"/>). One walk answers both, so
        /// the loop that makes a use count as repeated is by construction the loop whose body
        /// the region includes.
        /// </summary>
        private sealed class DefUsePaths
        {
            private readonly Site _def;
            private readonly Site _use;
            // Blocks reachable from the definition's successors without re-entering its block, and
            // blocks that reach the use's block without passing through the definition's. Both
            // null for the straight-line case, which needs neither.
            private readonly HashSet<BasicBlock> _forward;
            private readonly HashSet<BasicBlock> _backward;

            /// <summary>Some path runs from the definition to the use without re-executing the
            /// definition. False only for a use the definition never reaches (dead, or a CFG a
            /// pass left disconnected): nothing then lies between.</summary>
            public bool Reachable { get; }

            /// <summary>
            /// ADR-0006 D2: the use lies in a loop that does not contain the definition, so it runs
            /// again before the definition does — ONE static use, a dynamic count &gt; 1. Read off
            /// the CFG as: the use's block is reachable from the definition and reaches itself
            /// without passing through the definition's block. False for a use in the definition's
            /// own block (every cycle through it passes the definition) and for a use that every
            /// cycle reaches only through the definition (a loop that contains it).
            ///
            /// <para>A CYCLE, not a natural loop: a definition inside a loop on an arm the loop can
            /// skip (<c>If c Then t = p + q</c> … use <c>t</c> after the <c>End If</c>) leaves the
            /// use on a cycle that avoids it, and that use IS repeated — one execution of the
            /// definition reaches the use on this iteration and again on the next one that skips
            /// the arm. A natural-loop reading says the loop contains the definition and would
            /// exempt it.</para>
            /// </summary>
            public bool UseRepeats { get; }

            private DefUsePaths(Site def, Site use, HashSet<BasicBlock> forward, HashSet<BasicBlock> backward,
                bool reachable, bool useRepeats)
            {
                _def = def;
                _use = use;
                _forward = forward;
                _backward = backward;
                Reachable = reachable;
                UseRepeats = useRepeats;
            }

            public static DefUsePaths Between(Site def, Site use, HashSet<BasicBlock> inFunction,
                Func<Dictionary<BasicBlock, List<BasicBlock>>> predecessors)
            {
                var d = def.Block;
                var u = use.Block;

                // Straight-line: the use follows the definition in its own block.
                if (ReferenceEquals(d, u) && use.Index > def.Index)
                    return new DefUsePaths(def, use, null, null, reachable: true, useRepeats: false);

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
                if (!reachable) return new DefUsePaths(def, use, null, null, reachable: false, useRepeats: false);

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

                // u reaches itself without passing d: it sits on a cycle that avoids the definition.
                bool repeats = !ReferenceEquals(d, u) && backward.Contains(u);
                return new DefUsePaths(def, use, forward, backward, reachable: true, useRepeats: repeats);
            }

            /// <summary>
            /// Every instruction position on some path from the definition to the use that does not
            /// pass through the definition again, exclusive of both ends — except that for a
            /// repeated use it is <c>region(v)</c> as ADR-0006 D2 defines it, which includes the
            /// full body of the loop the use repeats in: every block of it is on such a path
            /// (it reaches the use round the back edge), and so is the use's own block AFTER the
            /// use, which precedes the use's next execution. A block that leaves the loop and
            /// never reaches the use again is on no such path, so an exit-path write is not in it.
            /// </summary>
            public IEnumerable<(BasicBlock Block, int Index)> Region()
            {
                var d = _def.Block;
                var u = _use.Block;

                if (!Reachable) yield break;

                if (_forward == null)
                {
                    for (int i = _def.Index + 1; i < _use.Index; i++) yield return (d, i);
                    yield break;
                }

                // The rest of the definition's block.
                for (int i = _def.Index + 1; i < d.Instructions.Count; i++) yield return (d, i);

                // Blocks strictly between: on a path from d that reaches u without passing d.
                foreach (var b in _forward)
                {
                    if (ReferenceEquals(b, u) || !_backward.Contains(b)) continue;
                    for (int i = 0; i < b.Instructions.Count; i++) yield return (b, i);
                }

                // The use's own block: all of it when the use repeats (including the use itself:
                // its own write precedes its next execution), otherwise up to the use.
                int end = UseRepeats ? u.Instructions.Count : _use.Index;
                for (int i = 0; i < end; i++) yield return (u, i);
            }
        }
    }
}
