using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using BasicLang.Compiler.SemanticAnalysis;
using TypeInfo = BasicLang.Compiler.SemanticAnalysis.TypeInfo;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  ADR-0006 D1 — "Writes the vocabulary does not name; does the verifier keep sharing it?" ONE
//  vocabulary (OptimizationPass.NamesWrittenBy), now TOTAL: every IRInstruction kind sits on an
//  explicit arm and answers None/Named/Universal (WriteSet/WriteKind); an unlisted kind answers
//  Universal with IsClassified=false, and IRVerifier's new Invariant V fires on it in test builds.
//
//  ⛔ THIS FILE IS PART 1 OF TWO, COMMITTED SEPARATELY (commit A — "step 1" alone,
//  S/adr6-d1/01-step1.patch) from KillVocabularyExtensionsTests.cs (commit B — steps 2-7, every
//  kill the ruling ADDS beyond the pre-existing vocabulary). Step 1 is a restructuring, not a
//  behavior change: it wraps the vocabulary that existed at HEAD before ADR-0006 into the
//  total/classified shape, without widening a single kind's answer. EVERY assertion in this file
//  was independently verified to hold two ways: (1) against this repo's actual working tree (all
//  ten patches applied), and (2) against a clean export of HEAD with ONLY 01-step1.patch applied
//  (a temp dir, `patch -p1 < 01-step1.patch`, `dotnet build BasicLang -c Release`) — so this file
//  is safe to build on top of step 1 alone, exactly as the fixture brief requires, AND it keeps
//  passing once steps 2-7 land (this same working tree). A per-kind row whose answer CHANGES
//  between step 1 and the full patch stack (IRBaseMethodCall's call flag; IRUnaryOp Inc/Dec's
//  operand write; IRForEach; IRInlineCode; IRYield; IRFieldAccess; IRFieldStore; IRAwait — the
//  eight kinds ADR-0006 D1's extension arms (steps 2-7, ADR items 6a/6b/6c and the ruling's own
//  "IRBaseMethodCall is a call") touch — is deliberately NOT pinned here; those eight live in
//  KillVocabularyExtensionsTests.cs, where the pinned value is the FINAL one.</para>
//
//  Do not fold this file's tests into KillVocabularyExtensionsTests.cs — the two land in separate
//  commits and are reviewed/reverted independently.
// ================================================================================================

/// <summary>
/// The reflection totality pin: every concrete <see cref="IRInstruction"/> subclass in the
/// BasicLang assembly must be CLASSIFIED (<see cref="WriteSet.IsClassified"/> true) by
/// <see cref="OptimizationPass.NamesWrittenBy"/>. Two tests share one hand-maintained roster
/// (<see cref="Instances"/>) of one minimal, hand-built instance per kind:
///
/// <list type="bullet">
/// <item><see cref="Roster_CoversEveryConcreteIRInstructionSubclass"/> — the roster's key set
/// must equal what reflection discovers, both ways: a kind ADDED to IRNodes.cs with no roster
/// entry fails this test by NAME (not silently absorbed into "35 kinds" going stale), and a
/// roster entry for a kind that stopped existing fails it too.</item>
/// <item><see cref="EveryInstructionKind_IsClassified"/> — for every rostered kind, build the
/// instance and assert <c>IsClassified</c>. This is the ADR's own totality claim, executable.</item>
/// </list>
///
/// Every instance below is built to be a MINIMAL, "vanilla" example of its kind — no <c>ByRef</c>
/// parameter on the enclosing function, no arguments on a call/constructor, no catch clauses or
/// pattern cases, no resolved .NET accessor — so a single roster serves BOTH this totality check
/// and, for the 28 rows whose answer step 1 already gives correctly and no later step touches
/// (IRUnaryOp included, via its non-Inc/Dec case), <see cref="KillVocabularyPerKindStepOneTests"/>'s
/// exact-value pins below.
/// </summary>
[TestFixture]
public class KillVocabularyReflectionTotalityTests
{
    private static readonly TypeInfo IntType = new TypeInfo("Integer", TypeKind.Primitive);
    private static readonly TypeInfo VoidType = new TypeInfo("Void", TypeKind.Primitive);

    internal sealed class Fixture
    {
        public IRModule Module;
        public IRFunction Function;
        public BasicBlock Entry;
        public BasicBlock Other;
        public IRVariable P;
        public IRVariable Q;
    }

    /// <summary>A function with two BY-VALUE parameters and no <c>ByRef</c> at all — the
    /// declarations rule's default fixture throughout this suite (<c>CallVisibilityDeclarationsRuleTests</c>,
    /// <c>IRVerifierTests</c>). No <c>ByRef</c> parameter means the vocabulary's aliasing/escaping
    /// rules (D1 (b) and its converse) never fire, which is exactly what keeps the "vanilla"
    /// instances below identical whether step 1 alone or the full patch stack classifies them.</summary>
    internal static Fixture NewFixture()
    {
        var module = new IRModule("M");
        var function = new IRFunction("Main", IntType);
        module.Functions.Add(function);
        var p = new IRVariable("p", IntType) { IsParameter = true };
        var q = new IRVariable("q", IntType) { IsParameter = true };
        function.Parameters.Add(p);
        function.Parameters.Add(q);
        var entry = function.CreateBlock("entry");
        var other = function.CreateBlock("other");
        return new Fixture { Module = module, Function = function, Entry = entry, Other = other, P = p, Q = q };
    }

    /// <summary>
    /// One minimal instance per concrete <see cref="IRInstruction"/> kind, keyed by the kind's own
    /// <see cref="Type"/> (so a rename of the class is a compile error here, not a silent roster
    /// drift — matching <c>JsExecutionTierRosterTests</c>' <c>typeof</c> idiom). MEASURED against
    /// this exact working tree (a fresh, isolated <c>dotnet build BasicLang -c Release</c>) and,
    /// separately, against a clean export of HEAD with ONLY <c>01-step1.patch</c> applied.
    /// </summary>
    internal static readonly Dictionary<Type, Func<Fixture, IRInstruction>> Instances = new()
    {
        [typeof(IRBranch)] = f => new IRBranch(f.Other),
        [typeof(IRConditionalBranch)] = f => new IRConditionalBranch(f.P, f.Entry, f.Other),
        [typeof(IRReturn)] = f => new IRReturn(null),
        [typeof(IRLabel)] = f => new IRLabel("L1"),
        [typeof(IRComment)] = f => new IRComment("hi"),
        [typeof(IRAssignment)] = f => new IRAssignment(f.P, f.Q),
        [typeof(IRStore)] = f => new IRStore(f.Q, f.P),
        [typeof(IRCall)] = f => new IRCall("t0", "Show", VoidType),
        [typeof(IRInstanceMethodCall)] = f => new IRInstanceMethodCall("t0", f.P, "M", IntType),
        [typeof(IRNewObject)] = f => new IRNewObject("t0", "Box", IntType),
        [typeof(IRBaseMethodCall)] = f => new IRBaseMethodCall("t0", "M", IntType),
        [typeof(IRConstant)] = f => new IRConstant(5, IntType),
        [typeof(IRVariable)] = f => new IRVariable("x", IntType),
        [typeof(IRBinaryOp)] = f => new IRBinaryOp("t0", BinaryOpKind.Add, f.P, f.Q, IntType),
        [typeof(IRCompare)] = f => new IRCompare("t0", CompareKind.Eq, f.P, f.Q, IntType),
        [typeof(IRCast)] = f => new IRCast("t0", f.P, IntType, IntType, CastKind.Bitcast),
        [typeof(IRLoad)] = f => new IRLoad("t0", f.P, IntType),
        [typeof(IRAlloca)] = f => new IRAlloca("V_addr", IntType, 4),
        [typeof(IRGetElementPtr)] = f => new IRGetElementPtr("t0", f.P, IntType),
        [typeof(IRPhi)] = f => new IRPhi("t0", IntType),
        [typeof(IRArrayAlloc)] = f => new IRArrayAlloc("arr", IntType, 4),
        [typeof(IRTupleElement)] = f => new IRTupleElement(f.P, 0, IntType),
        [typeof(IRIndexerAccess)] = f => new IRIndexerAccess("t0", f.P, IntType),
        [typeof(IRUnaryOp)] = f => new IRUnaryOp("t0", UnaryOpKind.Neg, f.P, IntType),
        [typeof(IRForEach)] = f => new IRForEach("item", IntType, f.P, f.Other, f.Other),
        [typeof(IRTryCatch)] = f => new IRTryCatch(f.Other, new List<IRCatchClause>(), null, f.Other),
        [typeof(IRSwitch)] = f => new IRSwitch(f.P, f.Other),
        [typeof(IRThrow)] = f => new IRThrow(f.P),
        [typeof(IRInlineCode)] = f => new IRInlineCode("csharp", "// x"),
        [typeof(IRArrayStore)] = f => new IRArrayStore(f.P, f.Q, f.P),
        [typeof(IRYield)] = f => new IRYield(f.P),
        [typeof(IRIndexerStore)] = f => new IRIndexerStore(f.P, f.Q),
        [typeof(IRFieldAccess)] = f => new IRFieldAccess("t0", f.P, "K", IntType),
        [typeof(IRFieldStore)] = f => new IRFieldStore(f.P, "K", f.Q),
        [typeof(IRAwait)] = f => new IRAwait("t0", f.P, IntType),
    };

    /// <summary>
    /// ⭐⭐ THE COVERAGE HALF of the totality pin. A kind added to <c>IRNodes.cs</c> with no entry
    /// here (the shape a future <c>IRSomethingNew</c> takes) is caught by NAME, not folded into a
    /// stale "35" count — mirrors <c>JsExecutionTierRosterTests.RosterCoversEveryJavaScriptIntegrationFixture</c>'s
    /// discovery-vs-roster idiom.
    /// </summary>
    [Test]
    public void Roster_CoversEveryConcreteIRInstructionSubclass()
    {
        var rostered = new HashSet<Type>(Instances.Keys);
        var discovered = typeof(IRInstruction).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IRInstruction).IsAssignableFrom(t))
            .ToList();

        var missing = discovered.Where(t => !rostered.Contains(t)).Select(t => t.Name).OrderBy(n => n).ToList();
        var stale = rostered.Where(t => !discovered.Contains(t)).Select(t => t.Name).OrderBy(n => n).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(missing, Is.Empty,
                "A new IRInstruction subclass exists with no instance in this roster: " + string.Join(", ", missing)
                + " — add it here AND give OptimizationPass.NamesWrittenBy an explicit arm for it "
                + "(ADR-0006 D1: an unlisted kind is Universal/unclassified and fails Invariant V).");
            Assert.That(stale, Is.Empty,
                "This roster names a type that is no longer a concrete IRInstruction subclass: "
                + string.Join(", ", stale) + " — it was renamed or removed; update the roster.");
        });
    }

    /// <summary>
    /// ⭐⭐ THE CLASSIFICATION HALF, and the ADR's own totality claim made executable: EVERY
    /// concrete <c>IRInstruction</c> kind must be classified. A kind that fails this is Universal
    /// AND unclassified — <see cref="IRVerifier.CheckInvariantV"/> fires on it the moment any test
    /// builds it, which <see cref="KillVocabularyVInvariantHandBuiltIRTests"/> exercises directly.
    /// </summary>
    [TestCaseSource(nameof(RosteredTypeNames))]
    public void EveryInstructionKind_IsClassified(string typeName)
    {
        var type = Instances.Keys.Single(t => t.Name == typeName);
        var inst = Instances[type](NewFixture());
        var writes = OptimizationPass.NamesWrittenBy(inst, NewFixture().Function);

        Assert.That(writes.IsClassified, Is.True,
            $"{typeName} is UNCLASSIFIED — OptimizationPass.NamesWrittenBy has no arm for it, so "
            + "every pass (CSE, LICM) treats it as writing everything and IRVerifier's Invariant V "
            + "fires on it in test builds (ADR-0006 D1).");
    }

    private static IEnumerable<string> RosteredTypeNames() => Instances.Keys.Select(t => t.Name).OrderBy(n => n);
}

/// <summary>
/// Per-kind unit rows for step 1's arms: TODAY'S EXACT ANSWER for the 28 rows whose
/// classification step 1 already gives correctly, and which NO later step (2-7) changes — verified
/// identical against both the full working tree and a clean step-1-only export (see this file's
/// header). The eight kinds a later step DOES change (IRBaseMethodCall; IRUnaryOp Inc/Dec;
/// IRForEach; IRInlineCode; IRYield; IRFieldAccess; IRFieldStore; IRAwait) are deliberately absent
/// here — they are <c>KillVocabularyExtensionsTests.cs</c>'s per-kind rows, pinned at their FINAL
/// (post-D1) answer, not their step-1 one.
///
/// <para>Each row names the exact <see cref="WriteKind"/>, the exact <see cref="WriteSet.Names"/>
/// (order-independent), and <see cref="WriteSet.IsCall"/> — not merely "classified", which the
/// totality fixture above already covers. A regression that widens or narrows one of these 27
/// kinds' answer without touching IRNodes.cs (so the totality pin stays green) is exactly what a
/// value pin, and only a value pin, catches.</para>
/// </summary>
[TestFixture]
public class KillVocabularyPerKindStepOneTests
{
    private sealed record Row(Type Kind, WriteKind ExpectedKind, string[] ExpectedNames, bool ExpectedIsCall);

    private static readonly Row[] Rows =
    {
        new(typeof(IRBranch), WriteKind.None, Array.Empty<string>(), false),
        new(typeof(IRConditionalBranch), WriteKind.None, Array.Empty<string>(), false),
        new(typeof(IRReturn), WriteKind.None, Array.Empty<string>(), false),
        new(typeof(IRLabel), WriteKind.None, Array.Empty<string>(), false),
        new(typeof(IRComment), WriteKind.None, Array.Empty<string>(), false),
        new(typeof(IRAssignment), WriteKind.Named, new[] { "p" }, false),
        new(typeof(IRStore), WriteKind.Named, new[] { "p" }, false), // IRStore(value: q, address: p) -> names "p"
        new(typeof(IRCall), WriteKind.Named, new[] { "t0" }, true),
        new(typeof(IRInstanceMethodCall), WriteKind.Named, new[] { "t0" }, true),
        new(typeof(IRNewObject), WriteKind.Named, new[] { "t0" }, true),
        new(typeof(IRConstant), WriteKind.Named, new[] { "const_5" }, false),
        new(typeof(IRVariable), WriteKind.Named, new[] { "x" }, false),
        new(typeof(IRBinaryOp), WriteKind.Named, new[] { "t0" }, false),
        new(typeof(IRCompare), WriteKind.Named, new[] { "t0" }, false),
        new(typeof(IRCast), WriteKind.Named, new[] { "t0" }, false),
        new(typeof(IRLoad), WriteKind.Named, new[] { "t0" }, false),
        new(typeof(IRAlloca), WriteKind.Named, new[] { "V_addr" }, false),
        new(typeof(IRGetElementPtr), WriteKind.Named, new[] { "t0" }, false),
        new(typeof(IRPhi), WriteKind.Named, new[] { "t0" }, false),
        new(typeof(IRArrayAlloc), WriteKind.Named, new[] { "arr" }, false),
        new(typeof(IRTupleElement), WriteKind.Named, new[] { "_tuple_elem_0" }, false),
        new(typeof(IRIndexerAccess), WriteKind.Named, new[] { "t0" }, false),
        new(typeof(IRUnaryOp), WriteKind.Named, new[] { "t0" }, false), // UnaryOpKind.Neg — NOT Inc/Dec, see file header
        new(typeof(IRTryCatch), WriteKind.None, Array.Empty<string>(), false), // no CatchClauses
        new(typeof(IRSwitch), WriteKind.None, Array.Empty<string>(), false), // no PatternCases
        new(typeof(IRThrow), WriteKind.None, Array.Empty<string>(), false),
        new(typeof(IRArrayStore), WriteKind.None, Array.Empty<string>(), false), // no ByRef parameter to escape to
        new(typeof(IRIndexerStore), WriteKind.None, Array.Empty<string>(), false), // no ResolvedNetTarget, no ByRef parameter
    };

    [Test]
    public void RowCount_Is28_TheSevenExtensionKindsAreDeliberatelyExcluded()
        => Assert.That(Rows, Has.Length.EqualTo(28),
            "28 kinds pinned here (IRUnaryOp included, via its NON-Inc/Dec case) + 7 kinds whose "
            + "classification a later step changes OUTRIGHT, for ANY instance (IRBaseMethodCall, "
            + "IRForEach, IRInlineCode, IRYield, IRFieldAccess, IRFieldStore, IRAwait — pinned at "
            + "their FINAL answer in KillVocabularyExtensionsTests.cs) = 35, the full roster "
            + "KillVocabularyReflectionTotalityTests.Instances covers. IRUnaryOp ALSO gets an "
            + "Inc/Dec-specific extension row over there — one KIND, two OPERATIONS, two answers. "
            + "If this count changed, a kind's step-1 stability changed too — update this row set "
            + "or the extensions file, not just the number.");

    [TestCaseSource(nameof(RowNames))]
    public void TodaysExactAnswer(string kindName)
    {
        var row = Rows.Single(r => r.Kind.Name == kindName);
        var f = KillVocabularyReflectionTotalityTests.NewFixture();
        var inst = KillVocabularyReflectionTotalityTests.Instances[row.Kind](f);
        var writes = OptimizationPass.NamesWrittenBy(inst, f.Function);

        Assert.Multiple(() =>
        {
            Assert.That(writes.Kind, Is.EqualTo(row.ExpectedKind), $"{kindName}: WriteKind");
            Assert.That(writes.Names, Is.EquivalentTo(row.ExpectedNames), $"{kindName}: Names");
            Assert.That(writes.IsCall, Is.EqualTo(row.ExpectedIsCall), $"{kindName}: IsCall");
            Assert.That(writes.IsClassified, Is.True, $"{kindName}: IsClassified");
        });
    }

    private static IEnumerable<string> RowNames() => Rows.Select(r => r.Kind.Name);
}

/// <summary>
/// Hand-built Invariant V: a FOREIGN instruction kind — one <c>OptimizationPass.NamesWrittenBy</c>
/// has no arm for, reproducing exactly <c>S/adr6-d1/vcheck/Program.cs</c>'s own construction (the
/// implementer's evidence) — must be Universal/unclassified, and every consumer of the vocabulary
/// must behave accordingly. MEASURED against this exact working tree via an isolated
/// <c>dotnet run</c> of that same construction: <c>foreign=false</c> gives 0 V violations and 1
/// CSE merge (the control); <c>foreign=true</c> gives exactly 1 V violation, 0 CSE merges, the
/// standard pipeline THROWS in <see cref="IRVerifierMode.Throw"/> with that one V violation, LICM
/// hoists nothing from a loop containing it, and Invariant S′ fires when it sits between a shared
/// value's definition and a later use.
/// </summary>
[TestFixture]
[NonParallelizable] // mutates IRVerifier.Mode, like IRVerifierModeResolutionTests
public class KillVocabularyVInvariantHandBuiltIRTests
{
    private static readonly TypeInfo IntType = new TypeInfo("Integer", TypeKind.Primitive);
    private static readonly TypeInfo VoidType = new TypeInfo("Void", TypeKind.Primitive);

    /// <summary>An instruction kind the vocabulary was never told about — <c>OptimizationPass.
    /// NamesWrittenBy</c>'s <c>switch</c> has no arm for it, so it falls to <c>default</c> and
    /// answers <see cref="WriteSet.Unclassified"/>. Verbatim from <c>S/adr6-d1/vcheck/Program.cs</c>.</summary>
    private sealed class Foreign : IRInstruction
    {
        public override void Accept(IIRVisitor v) { }
        public override string ToString() => "foreign";
    }

    /// <summary>
    /// <c>t0 = p + q : Show(t0) : [Foreign?] : t1 = p + q : Show(t1)</c> — the same shape
    /// <c>vcheck</c> builds. With no Foreign instruction this is an ordinary CSE-mergeable pair
    /// (the control); with one, the Foreign sits between the two occurrences.
    /// </summary>
    private static (IRModule Module, IRFunction Function) BuildModule(bool includeForeign)
    {
        var module = new IRModule("M");
        var function = new IRFunction("F", VoidType);
        var p = new IRVariable("p", IntType) { IsParameter = true };
        var q = new IRVariable("q", IntType) { IsParameter = true };
        function.Parameters.Add(p);
        function.Parameters.Add(q);
        var a = new IRVariable("a", IntType);
        function.LocalVariables.Add(a);
        var block = function.CreateBlock("entry");

        var t0 = new IRBinaryOp("t0", BinaryOpKind.Add, p, q, IntType);
        block.AddInstruction(t0);
        var show1 = new IRCall("", "Show", VoidType); show1.Arguments.Add(t0);
        block.AddInstruction(show1);
        if (includeForeign) block.AddInstruction(new Foreign());
        var t1 = new IRBinaryOp("t1", BinaryOpKind.Add, p, q, IntType);
        block.AddInstruction(t1);
        var show2 = new IRCall("", "Show", VoidType); show2.Arguments.Add(t1);
        block.AddInstruction(show2);
        block.AddInstruction(new IRReturn(null));

        module.Functions.Add(function);
        return (module, function);
    }

    [Test]
    public void ForeignKind_ProducesExactlyOneVViolation()
    {
        var (module, function) = BuildModule(includeForeign: true);
        var violations = IRVerifier.CheckInvariantV(module);

        Assert.That(violations, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(violations[0].Invariant, Is.EqualTo("V"));
            Assert.That(violations[0].Function, Is.EqualTo(function.Name));
            Assert.That(violations[0].Writer, Is.InstanceOf<Foreign>());
            Assert.That(violations[0].WriterBlock, Is.EqualTo("entry"));
        });
    }

    [Test]
    public void NoForeignKind_ProducesNoVViolations()
    {
        var (module, _) = BuildModule(includeForeign: false);
        Assert.That(IRVerifier.CheckInvariantV(module), Is.Empty);
    }

    [Test]
    public void VerifyAfterOptimization_ThrowsInThrowMode_OnAForeignKind()
    {
        var previousMode = IRVerifier.Mode;
        try
        {
            IRVerifier.Mode = IRVerifierMode.Throw;
            var (module, _) = BuildModule(includeForeign: true);
            var pipeline = new OptimizationPipeline();
            pipeline.AddStandardPasses();

            var ex = Assert.Throws<IRVerificationException>(() => pipeline.Run(module));
            Assert.That(ex!.Violations, Has.Count.EqualTo(1));
            Assert.That(ex.Violations[0].Invariant, Is.EqualTo("V"));
        }
        finally { IRVerifier.Mode = previousMode; }
    }

    [Test]
    public void Cse_DoesNotMergeAcrossAForeignKind()
    {
        var (moduleWithForeign, _) = BuildModule(includeForeign: true);
        var withForeign = new CommonSubexpressionEliminationPass();
        withForeign.Run(moduleWithForeign);
        Assert.That(withForeign.ModificationCount, Is.EqualTo(0),
            "a Universal/unclassified instruction must clear CSE's records, not merely fail to "
            + "kill by name — otherwise a foreign kind silently degrades to 'private'.");

        var (moduleWithoutForeign, _) = BuildModule(includeForeign: false);
        var control = new CommonSubexpressionEliminationPass();
        control.Run(moduleWithoutForeign);
        Assert.That(control.ModificationCount, Is.EqualTo(1),
            "non-vacuity control: without the foreign instruction the same shape MUST merge — a "
            + "vocabulary that over-kills everything would pass the test above for the wrong reason.");
    }

    /// <summary>A loop whose body contains ONLY a loop-invariant computation (<c>t0 = n * 2</c>,
    /// <c>n</c> a by-value parameter never written in the loop) plus, optionally, a Foreign
    /// instruction. MEASURED: without Foreign, LICM hoists it (1 modification); with Foreign
    /// present, LICM hoists NOTHING (<c>VariablesWrittenIn</c>'s <c>writesEverything</c> out
    /// parameter short-circuits <c>TryHoistFromLoop</c>).</summary>
    private static IRModule BuildLoop(bool includeForeign)
    {
        var module = new IRModule("M");
        var function = new IRFunction("F", VoidType);
        var n = new IRVariable("n", IntType) { IsParameter = true };
        function.Parameters.Add(n);
        var i = new IRVariable("i", IntType);
        function.LocalVariables.Add(i);
        var entry = function.CreateBlock("entry");
        var head = function.CreateBlock("head");
        var body = function.CreateBlock("body");
        var exit = function.CreateBlock("exit");

        entry.AddInstruction(new IRAssignment(i, new IRConstant(0, IntType)));
        entry.AddInstruction(new IRBranch(head));

        var cond = new IRCompare("cond", CompareKind.Lt, i, n, IntType);
        head.AddInstruction(cond);
        head.AddInstruction(new IRConditionalBranch(cond, body, exit));

        var t0 = new IRBinaryOp("t0", BinaryOpKind.Mul, n, new IRConstant(2, IntType), IntType);
        body.AddInstruction(t0);
        var show = new IRCall("", "Show", VoidType); show.Arguments.Add(t0);
        body.AddInstruction(show);
        if (includeForeign) body.AddInstruction(new Foreign());
        var inc = new IRBinaryOp("i", BinaryOpKind.Add, i, new IRConstant(1, IntType), IntType) { NamedAfterVariable = true };
        body.AddInstruction(inc);
        body.AddInstruction(new IRBranch(head));

        exit.AddInstruction(new IRReturn(null));
        module.Functions.Add(function);
        return module;
    }

    [Test]
    public void Licm_HoistsNothing_FromALoopContainingAForeignKind()
    {
        var withForeign = BuildLoop(includeForeign: true);
        var licmWithForeign = new LoopInvariantCodeMotionPass();
        licmWithForeign.Run(withForeign);
        Assert.That(licmWithForeign.ModificationCount, Is.EqualTo(0),
            "a Universal/unclassified instruction anywhere in the loop must leave nothing "
            + "invariant, not merely fail to name the loop-invariant computation's own operands.");

        var withoutForeign = BuildLoop(includeForeign: false);
        var control = new LoopInvariantCodeMotionPass();
        control.Run(withoutForeign);
        Assert.That(control.ModificationCount, Is.EqualTo(1),
            "non-vacuity control: without the foreign instruction the loop-invariant computation "
            + "MUST hoist — otherwise the assertion above passes for the wrong reason.");
    }

    /// <summary>S′ (ADR-0005 D2 / ADR-0006 D1): a shared value <c>a</c> read twice with the
    /// Foreign instruction sitting between the definition and the second use, as its DESTINATION
    /// writer — the same polarity <c>IRVerifierHandBuiltIRTests.C4Shape_OneUse_DestinationRenamedByACall_Fails</c>
    /// exercises for an ordinary call.</summary>
    [Test]
    public void SPrime_Fires_ForASharedValue_WithAForeignWriterBetweenDefinitionAndUse()
    {
        var module = new IRModule("M");
        var function = new IRFunction("Main", IntType);
        var p = new IRVariable("p", IntType) { IsParameter = true };
        var q = new IRVariable("q", IntType) { IsParameter = true };
        function.Parameters.Add(p);
        function.Parameters.Add(q);
        var a = new IRVariable("a", IntType);
        function.LocalVariables.Add(a);
        var entry = function.CreateBlock("entry");

        var v = new IRBinaryOp("a", BinaryOpKind.Add, p, q, IntType) { NamedAfterVariable = true };
        var use1 = new IRCall("", "Show", IntType); use1.Arguments.Add(v);
        var use2 = new IRCall("", "Show", IntType); use2.Arguments.Add(v);
        entry.AddInstruction(v);
        entry.AddInstruction(use1);
        entry.AddInstruction(new Foreign());
        entry.AddInstruction(use2);
        entry.AddInstruction(new IRReturn());
        module.Functions.Add(function);

        var violations = IRVerifier.CheckInvariantSPrime(module);

        Assert.That(violations, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(violations[0].Invariant, Is.EqualTo("S′"));
            Assert.That(violations[0].IsDestination, Is.True);
            Assert.That(violations[0].Variable, Is.EqualTo("a"));
            Assert.That(violations[0].Writer, Is.InstanceOf<Foreign>());
        });
    }
}
