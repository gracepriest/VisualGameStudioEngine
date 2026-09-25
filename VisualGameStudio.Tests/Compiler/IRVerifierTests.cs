using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using BasicLang.Compiler.SemanticAnalysis;
// `TypeInfo` is ambiguous with System.Reflection.TypeInfo (this file uses Reflection for the
// IRVerifier mode-cache pins) — the compiler's TypeInfo is the one meant everywhere below.
using TypeInfo = BasicLang.Compiler.SemanticAnalysis.TypeInfo;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  PART B of two (see CseDestinationInvalidationTests.cs for Part A — CSE's own half of ADR-0005
//  D2). This file is IRVerifier — the post-optimizer assertion of Invariant S′ ADR-0004 D2 obliged
//  and ADR-0005 D2 widened — plus ControlFlowGraph.SuccessorsOf, extracted unchanged from Build()
//  so IRVerifier can walk the CFG without touching block.Successors/Predecessors (verifying must
//  never change what a backend sees).
//
//  ⛔ Do not fold this file's tests into CseDestinationInvalidationTests.cs — Part A and Part B
//  are committed, and revертable, separately.
// ================================================================================================

/// <summary>
/// Hand-built IR, the only way to isolate Invariant S′ from what CSE happens to decide — every
/// shape here is unreachable from any BasicLang source CSE would produce today (a stray
/// <c>NamedAfterVariable</c> binop with a use count the pass itself would never leave lying
/// around), which is exactly the point: the verifier must be right on IR CSE does NOT (yet, or
/// ever) produce, because a future pass — or a hand-rolled one — can.
///
/// <para>Mirrors <c>scratchpad/f111g/hand/Program.cs</c>'s nine probe shapes one-for-one (that
/// driver's own <c>Check</c> helper is reproduced here as <see cref="AssertVerdict"/>), plus two
/// more the implementer measured as false positives before <c>IRVerifier.NamedDestination</c>
/// excluded them: an <c>IRAlloca</c> (a storage SLOT, not a value re-read by name) and an in-block
/// <c>IRConstant</c> (a literal, never read back by name either).</para>
/// </summary>
[TestFixture]
public class IRVerifierHandBuiltIRTests
{
    private static readonly TypeInfo IntType = new TypeInfo("Integer", TypeKind.Primitive);

    private sealed class Fixture
    {
        public IRModule Module;
        public IRFunction Function;
        public BasicBlock Entry;
        public IRVariable P;
        public IRVariable Q;
        public IRVariable A;
    }

    private static Fixture NewFixture()
    {
        var module = new IRModule("M");
        var function = new IRFunction("Main", IntType);
        module.Functions.Add(function);
        var p = new IRVariable("p", IntType) { IsParameter = true };
        var q = new IRVariable("q", IntType) { IsParameter = true };
        function.Parameters.Add(p);
        function.Parameters.Add(q);
        var a = new IRVariable("a", IntType);
        function.LocalVariables.Add(a);
        var entry = function.CreateBlock("entry");
        return new Fixture { Module = module, Function = function, Entry = entry, P = p, Q = q, A = a };
    }

    /// <summary>A call whose only role is to READ <paramref name="x"/> — the generic "some later
    /// instruction uses this value" site every shape below needs at least twice to make the value
    /// shared.</summary>
    private static IRCall Use(IRValue x)
    {
        var call = new IRCall("", "Show", IntType);
        call.Arguments.Add(x);
        return call;
    }

    /// <summary>Asserts violation-presence and, when a violation is expected, that it is reported
    /// against THIS instruction with the right destination-vs-operand polarity — not merely
    /// "some violation fired somewhere", which a differently-shaped bug could satisfy by
    /// accident.</summary>
    private static void AssertVerdict(IRModule module, bool expectViolation, bool? expectDestination = null, string expectVariable = null)
    {
        var violations = IRVerifier.CheckInvariantSPrime(module);
        if (!expectViolation)
        {
            Assert.That(violations, Is.Empty,
                "expected NO violation; got: " + string.Join(" | ", violations.Select(v => v.ToString())));
            return;
        }

        Assert.That(violations, Is.Not.Empty, "expected a violation and got none");
        Assert.Multiple(() =>
        {
            if (expectDestination.HasValue)
                Assert.That(violations[0].IsDestination, Is.EqualTo(expectDestination.Value),
                    "IsDestination polarity is wrong — got: " + violations[0]);
            if (expectVariable != null)
                Assert.That(violations[0].Variable, Is.EqualTo(expectVariable), "wrong guarded variable — got: " + violations[0]);
        });
    }

    // ---- 1. the ADR's own test: destination reassigned between two uses --------------------

    [Test]
    public void DestinationReassignedBetweenTwoUses_Fails()
    {
        var f = NewFixture();
        var v = new IRBinaryOp("a", BinaryOpKind.Add, f.P, f.Q, IntType) { NamedAfterVariable = true };
        f.Entry.Instructions.Add(v);
        f.Entry.Instructions.Add(Use(v));
        f.Entry.Instructions.Add(new IRAssignment(f.A, new IRConstant(0, IntType)));
        f.Entry.Instructions.Add(Use(v));
        f.Entry.Instructions.Add(new IRReturn());

        AssertVerdict(f.Module, expectViolation: true, expectDestination: true, expectVariable: "a");
    }

    // ---- 2. control: same shape, no reassignment ---------------------------------------------

    [Test]
    public void DestinationNotReassigned_TwoUses_Passes()
    {
        var f = NewFixture();
        var v = new IRBinaryOp("a", BinaryOpKind.Add, f.P, f.Q, IntType) { NamedAfterVariable = true };
        f.Entry.Instructions.Add(v);
        f.Entry.Instructions.Add(Use(v));
        f.Entry.Instructions.Add(Use(v));
        f.Entry.Instructions.Add(new IRReturn());

        AssertVerdict(f.Module, expectViolation: false);
    }

    // ---- 3. THE C4 shape: CSE leaves ONE use (the destination is the second "reader") ---------

    /// <summary>
    /// ⭐⭐ THE b1 MUTANT KILL. This is C4 as CSE leaves it at HEAD: a merge re-points the
    /// duplicate's consumer at the surviving binop, so <c>a</c>'s <c>IRBinaryOp</c> has exactly
    /// ONE literal operand use — <c>l(0) = p + q</c> becomes <c>l(0) = a</c> — plus the variable
    /// itself as a reader. Read the OLD, narrower Invariant S literally ("use count &gt; 1"
    /// counting only operand uses) and this shape has count 1 and is invisible; ADR-0005 D2's own
    /// remarks call this out explicitly (see <c>IRVerifier</c>'s class doc). If the verifier ever
    /// stopped checking the destination at all, this is the shape that would go from "correctly
    /// flagged" to "silently certified" — a verifier built on the narrower S would pass exactly
    /// the miscompile D2 exists to catch.
    /// </summary>
    [Test]
    public void C4Shape_OneUse_DestinationRenamedByACall_Fails()
    {
        var f = NewFixture();
        var v = new IRBinaryOp("a", BinaryOpKind.Add, f.P, f.Q, IntType) { NamedAfterVariable = true };
        f.Entry.Instructions.Add(v);
        f.Entry.Instructions.Add(new IRCall("a", "Seed", IntType)); // renames "a" — the destination write
        f.Entry.Instructions.Add(Use(v));
        f.Entry.Instructions.Add(new IRReturn());

        AssertVerdict(f.Module, expectViolation: true, expectDestination: true, expectVariable: "a");
    }

    // ---- 4. the OPERAND side, for contrast with case 3's destination side --------------------

    [Test]
    public void OperandWrittenBetweenTwoUsesOfAnAnonymousTemp_Fails()
    {
        var f = NewFixture();
        var v = new IRBinaryOp("t0", BinaryOpKind.Add, f.P, f.Q, IntType);
        f.Entry.Instructions.Add(v);
        f.Entry.Instructions.Add(Use(v));
        f.Entry.Instructions.Add(new IRAssignment(f.P, new IRConstant(5, IntType)));
        f.Entry.Instructions.Add(Use(v));
        f.Entry.Instructions.Add(new IRReturn());

        AssertVerdict(f.Module, expectViolation: true, expectDestination: false, expectVariable: "p");
    }

    // ---- 5. an anonymous single-use value is EXEMPT (S′'s literal "use count > 1") ------------

    [Test]
    public void AnonymousTemp_OneUse_OperandWrittenInBetween_IsExempt()
    {
        var f = NewFixture();
        var v = new IRBinaryOp("t0", BinaryOpKind.Add, f.P, f.Q, IntType);
        f.Entry.Instructions.Add(v);
        f.Entry.Instructions.Add(new IRAssignment(f.P, new IRConstant(5, IntType)));
        f.Entry.Instructions.Add(Use(v));
        f.Entry.Instructions.Add(new IRReturn());

        AssertVerdict(f.Module, expectViolation: false);
    }

    // ---- 6. an If-ARM path: dest written on one branch, used only in the merge block ----------

    [Test]
    public void DestinationWrittenInOneIfArm_BeforeAUseInTheMergeBlock_Fails()
    {
        var f = NewFixture();
        var then = f.Function.CreateBlock("if.then");
        var end = f.Function.CreateBlock("if.end");
        var v = new IRBinaryOp("a", BinaryOpKind.Add, f.P, f.Q, IntType) { NamedAfterVariable = true };
        f.Entry.Instructions.Add(v);
        f.Entry.Instructions.Add(Use(v));
        f.Entry.Instructions.Add(new IRConditionalBranch(f.P, then, end));
        then.Instructions.Add(new IRAssignment(f.A, new IRConstant(0, IntType)));
        then.Instructions.Add(new IRBranch(end));
        end.Instructions.Add(Use(v));
        end.Instructions.Add(new IRReturn());

        AssertVerdict(f.Module, expectViolation: true, expectDestination: true, expectVariable: "a");
    }

    // ---- 7. a write AFTER the last use is fine -------------------------------------------------

    [Test]
    public void DestinationWrittenAfterTheLastUse_Passes()
    {
        var f = NewFixture();
        var v = new IRBinaryOp("a", BinaryOpKind.Add, f.P, f.Q, IntType) { NamedAfterVariable = true };
        f.Entry.Instructions.Add(v);
        f.Entry.Instructions.Add(Use(v));
        f.Entry.Instructions.Add(Use(v));
        f.Entry.Instructions.Add(new IRAssignment(f.A, new IRConstant(0, IntType)));
        f.Entry.Instructions.Add(new IRReturn());

        AssertVerdict(f.Module, expectViolation: false);
    }

    // ---- 8. a ByRef ARGUMENT write of the destination between two uses -------------------------

    [Test]
    public void DestinationPassedByRefBetweenTwoUses_Fails()
    {
        var f = NewFixture();
        var v = new IRBinaryOp("a", BinaryOpKind.Add, f.P, f.Q, IntType) { NamedAfterVariable = true };
        var bump = new IRCall("", "Bump", IntType);
        bump.Arguments.Add(f.A);
        bump.ByRefArguments.Add(true);
        f.Entry.Instructions.Add(v);
        f.Entry.Instructions.Add(Use(v));
        f.Entry.Instructions.Add(bump);
        f.Entry.Instructions.Add(Use(v));
        f.Entry.Instructions.Add(new IRReturn());

        AssertVerdict(f.Module, expectViolation: true, expectDestination: true, expectVariable: "a");
    }

    // ---- 9. a LOOP-CYCLE path: dest written in the loop body, re-reaching the use --------------

    [Test]
    public void DestinationWrittenLaterInALoopBody_ThatReReachesTheUse_Fails()
    {
        var f = NewFixture();
        var head = f.Function.CreateBlock("loop.head");
        var body = f.Function.CreateBlock("loop.body");
        var exit = f.Function.CreateBlock("loop.exit");
        var v = new IRBinaryOp("a", BinaryOpKind.Add, f.P, f.Q, IntType) { NamedAfterVariable = true };
        f.Entry.Instructions.Add(v);
        f.Entry.Instructions.Add(new IRBranch(head));
        head.Instructions.Add(new IRConditionalBranch(f.P, body, exit));
        body.Instructions.Add(Use(v));
        body.Instructions.Add(new IRAssignment(f.A, new IRConstant(0, IntType)));
        body.Instructions.Add(new IRBranch(head));
        exit.Instructions.Add(Use(v));
        exit.Instructions.Add(new IRReturn());

        AssertVerdict(f.Module, expectViolation: true, expectDestination: true, expectVariable: "a");
    }

    // ---- 10 & 11. the two MEASURED FALSE POSITIVES NamedDestination now excludes ---------------

    /// <summary>
    /// ⭐ <c>IRAlloca</c> — its <c>Name</c> spells a STORAGE SLOT (<c>V_addr</c> for an array
    /// local), not a re-read-by-name value; no later write to a variable named <c>V</c> moves it.
    /// MEASURED before the exclusion: four false hits over the fast subset, every one an
    /// alloca's address read twice across a call. Reproduced directly: an alloca whose address is
    /// LOADED twice with a call in between must not be flagged, even though (before the fix)
    /// <c>NamedDestination</c> would have returned <c>"V_addr"</c> and <c>IsCallVisibleDestination</c>
    /// would have called it call-visible (it is not declared as a parameter or local, and it does
    /// not look like a temp).
    /// </summary>
    [Test]
    public void AllocaNamedDestination_IsExcluded_NoFalsePositiveAcrossACall()
    {
        var f = NewFixture();
        var alloca = new IRAlloca("V_addr", IntType, size: 4);
        f.Entry.Instructions.Add(alloca);
        f.Entry.Instructions.Add(new IRLoad("t_ld1", alloca, IntType));
        f.Entry.Instructions.Add(new IRCall("", "Seed", IntType)); // a call in between
        f.Entry.Instructions.Add(new IRLoad("t_ld2", alloca, IntType));
        f.Entry.Instructions.Add(new IRReturn());

        AssertVerdict(f.Module, expectViolation: false);
    }

    /// <summary>
    /// ⭐ An in-block <c>IRConstant</c> — a literal every backend emits by VALUE, never reads back
    /// by name, left in the block by <c>ConstantFoldingPass</c> (<c>AssignmentCoercionTests.
    /// ANonNumericStore_IsLeftAlone</c> is the measured live hit). Reproduced directly: the SAME
    /// constant instance used twice with a call in between must not be flagged, even though
    /// (before the fix) its self-name (<c>const_5</c>) is not temp-shaped and would have been
    /// treated as a call-visible destination.
    /// </summary>
    [Test]
    public void ConstantNamedDestination_IsExcluded_NoFalsePositiveAcrossACall()
    {
        var f = NewFixture();
        var literal = new IRConstant(5, IntType); // Name = "const_5"
        f.Entry.Instructions.Add(literal); // left in the block, exactly as ConstantFoldingPass leaves one
        f.Entry.Instructions.Add(Use(literal));
        f.Entry.Instructions.Add(new IRCall("", "Seed", IntType)); // a call in between
        f.Entry.Instructions.Add(Use(literal));
        f.Entry.Instructions.Add(new IRReturn());

        AssertVerdict(f.Module, expectViolation: false);
    }
}

/// <summary>
/// <c>IRVerifier.Mode</c> resolution: env var &gt; runtime switch &gt; build config, and the two
/// side effects (throwing; logging) each mode promises.
///
/// <para>⛔⛔ EVERY TEST HERE MUTATES PROCESS-WIDE STATIC STATE (<c>IRVerifier.Mode</c>,
/// <c>IRVerifier.LogPath</c>, and the <c>BASICLANG_VERIFY_IR</c> environment variable) that every
/// OTHER test in this suite depends on being <c>Throw</c> — <c>VisualGameStudio.Tests.csproj</c>
/// sets the <c>BasicLang.VerifyIR</c> switch specifically so a compile anywhere in the suite
/// verifies. Every test below resets the PRIVATE <c>_mode</c>/<c>_logPath</c> cache via reflection
/// before it acts (so <c>Resolve()</c> genuinely re-runs rather than returning a value some
/// earlier test already pinned) and restores <c>Mode = Throw</c> / <c>LogPath = null</c> / the
/// environment variable to unset in a <c>finally</c>, whether or not the assertion passed.
/// <c>[NonParallelizable]</c> on the fixture, not just Console-redirecting tests, for the same
/// reason.</para>
/// </summary>
[TestFixture]
[NonParallelizable]
public class IRVerifierModeResolutionTests
{
    private static readonly FieldInfo ModeField =
        typeof(IRVerifier).GetField("_mode", BindingFlags.NonPublic | BindingFlags.Static);
    private static readonly FieldInfo LogPathField =
        typeof(IRVerifier).GetField("_logPath", BindingFlags.NonPublic | BindingFlags.Static);

    [SetUp]
    public void EnsureFieldsFound()
    {
        Assert.That(ModeField, Is.Not.Null, "IRVerifier._mode is gone or renamed — re-pin this fixture against wherever the cache moved to.");
        Assert.That(LogPathField, Is.Not.Null, "IRVerifier._logPath is gone or renamed — re-pin this fixture against wherever the cache moved to.");
    }

    /// <summary>Forces the next <c>IRVerifier.Mode</c>/<c>LogPath</c> read to re-run <c>Resolve()</c>
    /// instead of returning whatever the last test (or the suite's own first compile) cached.</summary>
    private static void ResetCache()
    {
        ModeField.SetValue(null, null);
        LogPathField.SetValue(null, null);
    }

    /// <summary>Puts the suite back the way every OTHER fixture needs it, unconditionally.</summary>
    private static void RestoreForTheRestOfTheSuite()
    {
        Environment.SetEnvironmentVariable(IRVerifier.EnvironmentVariable, null);
        ResetCache();
        IRVerifier.Mode = IRVerifierMode.Throw;
        IRVerifier.LogPath = null;
    }

    /// <summary>
    /// ⭐⭐ THE b2 MUTANT KILL. No env var, cache reset so <c>Resolve()</c> genuinely runs: it must
    /// consult the <c>BasicLang.VerifyIR</c> RUNTIME SWITCH <c>VisualGameStudio.Tests.csproj</c>
    /// sets, and resolve to <c>Throw</c>. A mutant that made <c>Resolve()</c> ignore
    /// <c>AppContext.TryGetSwitch</c> falls straight to the DEBUG/RELEASE compile-time default —
    /// and this suite builds <c>BasicLang.dll</c> at <c>-c Release</c>, where that default is
    /// <c>Off</c> — so this assertion goes red exactly when the switch stops being consulted.
    /// </summary>
    [Test]
    public void InTheTestHost_ModeIsThrow()
    {
        ResetCache();
        try
        {
            Assert.That(Environment.GetEnvironmentVariable(IRVerifier.EnvironmentVariable), Is.Null.Or.Empty,
                "the environment variable must be unset for this to test the RUNTIME SWITCH path");
            Assert.That(IRVerifier.Mode, Is.EqualTo(IRVerifierMode.Throw),
                "the test csproj's BasicLang.VerifyIR switch must resolve Mode to Throw when no "
                + "environment variable overrides it. If this reads Off, either the switch is no "
                + "longer wired in VisualGameStudio.Tests.csproj (out of scope to fix here) or "
                + "IRVerifier.Resolve stopped consulting AppContext.TryGetSwitch.");
        }
        finally { RestoreForTheRestOfTheSuite(); }
    }

    [Test]
    public void EnvironmentVariable_Zero_OverridesTheSwitchToOff()
    {
        ResetCache();
        Environment.SetEnvironmentVariable(IRVerifier.EnvironmentVariable, "0");
        try
        {
            Assert.That(IRVerifier.Mode, Is.EqualTo(IRVerifierMode.Off),
                "BASICLANG_VERIFY_IR=0 must override the runtime switch (which the test host sets "
                + "to Throw) to Off — env var wins over the switch, per IRVerifier.Resolve's order.");

            // ⭐ Non-vacuity: with Mode genuinely Off, a KNOWN violation must not throw at all.
            Assert.DoesNotThrow(() => IRVerifier.VerifyAfterOptimization(BuildModuleWithAKnownViolation()),
                "Off must be a true no-op, even over IR that violates Invariant S′.");
        }
        finally { RestoreForTheRestOfTheSuite(); }
    }

    /// <summary>
    /// <c>log:&lt;path&gt;</c> resolves to <see cref="IRVerifierMode.Log"/> with that path, and —
    /// non-vacuously — actually APPENDS a violation to it instead of throwing.
    /// </summary>
    [Test]
    public void EnvironmentVariable_LogPrefix_ResolvesToLogModeAndWritesTheFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "irverifier_log_" + Guid.NewGuid().ToString("N") + ".txt");
        ResetCache();
        Environment.SetEnvironmentVariable(IRVerifier.EnvironmentVariable, "log:" + path);
        try
        {
            Assert.That(IRVerifier.Mode, Is.EqualTo(IRVerifierMode.Log), "log:<path> must resolve to Log mode");
            Assert.That(IRVerifier.LogPath, Is.EqualTo(path), "the path after 'log:' must be threaded through verbatim");

            Assert.That(File.Exists(path), Is.False, "nothing must be written before a violation is ever checked");

            Assert.DoesNotThrow(() => IRVerifier.VerifyAfterOptimization(BuildModuleWithAKnownViolation()),
                "Log mode must record and carry on, never throw — that is the whole point of the mode "
                + "(ADR-0004 D2: 'run the verifier over the suite' without aborting it)");

            Assert.That(File.Exists(path), Is.True, "Log mode must append the violation to LogPath");
            var contents = File.ReadAllText(path);
            Assert.That(contents, Does.Contain("Invariant S"), "the logged line must be the violation's own message");
        }
        finally
        {
            RestoreForTheRestOfTheSuite();
            try { File.Delete(path); } catch { /* best effort */ }
        }
    }

    /// <summary>The same hand-built violation <see cref="IRVerifierHandBuiltIRTests.DestinationReassignedBetweenTwoUses_Fails"/>
    /// uses, built locally so this fixture has no dependency on that one's ordering.</summary>
    private static IRModule BuildModuleWithAKnownViolation()
    {
        var intType = new TypeInfo("Integer", TypeKind.Primitive);
        var module = new IRModule("M");
        var function = new IRFunction("Main", intType);
        module.Functions.Add(function);
        var p = new IRVariable("p", intType) { IsParameter = true };
        var q = new IRVariable("q", intType) { IsParameter = true };
        function.Parameters.Add(p);
        function.Parameters.Add(q);
        var a = new IRVariable("a", intType);
        function.LocalVariables.Add(a);
        var entry = function.CreateBlock("entry");

        var v = new IRBinaryOp("a", BinaryOpKind.Add, p, q, intType) { NamedAfterVariable = true };
        var use1 = new IRCall("", "Show", intType); use1.Arguments.Add(v);
        var use2 = new IRCall("", "Show", intType); use2.Arguments.Add(v);
        entry.Instructions.Add(v);
        entry.Instructions.Add(use1);
        entry.Instructions.Add(new IRAssignment(a, new IRConstant(0, intType)));
        entry.Instructions.Add(use2);
        entry.Instructions.Add(new IRReturn());
        return module;
    }
}

/// <summary>
/// The verifier only ever READS the IR (<c>CheckInvariantSPrime</c> walks instructions and calls
/// <c>ControlFlowGraph.SuccessorsOf</c>, never <c>AddEdge</c>/<c>Build</c>, never a setter on an
/// IR node) — so turning it on must never change what a backend emits.
/// </summary>
[TestFixture]
[NonParallelizable]
public class IRVerifierOutputIdentityTests
{
    private static readonly FieldInfo ModeField =
        typeof(IRVerifier).GetField("_mode", BindingFlags.NonPublic | BindingFlags.Static);

    private static void ResetCache() => ModeField.SetValue(null, null);

    /// <summary>
    /// Two real BasicLang programs — C4 (ADR-0005 D2's own reported shape: a shared value, a
    /// destination write in between) and D1 (the same shape run twice through a loop, so the
    /// verifier's CFG walk covers a back edge too) — compiled through the STANDARD pipeline once
    /// with the verifier <see cref="IRVerifierMode.Off"/> and once <see cref="IRVerifierMode.Throw"/>.
    /// The emitted JavaScript text must be BYTE-IDENTICAL either way.
    /// </summary>
    [Test]
    public void EmittedOutput_IsByteIdentical_WithTheVerifierOnAndOff()
    {
        try
        {
            foreach (var (name, source) in new[]
                     {
                         ("C4", CseDestinationShapes.C4),
                         ("D1 (loop)", CseDestinationShapes.D1),
                     })
            {
                ResetCache();
                IRVerifier.Mode = IRVerifierMode.Off;
                var withoutVerifier = JsTestSupport.CompileOptimized(source, sourceFilePath: "prog.bas");

                ResetCache();
                IRVerifier.Mode = IRVerifierMode.Throw;
                var withVerifier = JsTestSupport.CompileOptimized(source, sourceFilePath: "prog.bas");

                Assert.That(withVerifier, Is.EqualTo(withoutVerifier),
                    $"{name}: turning IRVerifier on must not change one byte of emitted output — "
                    + "it only reads the IR and must never be observable in what ships.");
            }
        }
        finally
        {
            ResetCache();
            IRVerifier.Mode = IRVerifierMode.Throw;
            IRVerifier.LogPath = null;
        }
    }
}

/// <summary>
/// <c>ControlFlowGraph.SuccessorsOf</c> — extracted from <c>Build()</c> "unchanged" per the
/// fixture brief; this is what pins that claim.
/// </summary>
[TestFixture]
public class ControlFlowGraphSuccessorsOfTests
{
    private static readonly string[] Shapes =
    {
        "A02_for_nested", // nested loops: two back edges, two condition/body/inc triples
        "A07_for_if",     // a conditional branch INSIDE a loop body
        "A08_for_try",    // IRTryCatch's structured edges (Try/Catch/Finally/End)
        "A09_foreach",    // IRForEach's structured edges (Body/End)
        "A10_noloop",     // a plain If/Then/Else, no loop at all
    };

    /// <summary>
    /// ⭐⭐ THE b3 MUTANT KILL, and the reason this is NOT written as "recompute SuccessorsOf again
    /// and compare it to itself": <c>ControlFlowGraph.Build()</c> is now DEFINED in terms of
    /// <c>SuccessorsOf</c>, so a self-comparison would agree with itself under ANY mutation of
    /// <c>SuccessorsOf</c>'s edge rule — both sides would drop the same edge and the test would
    /// stay green. This reimplements the edge rule a SECOND time, independently, straight out of
    /// the pre-extraction <c>Build()</c> body (branch/conditional-branch/switch terminator, then a
    /// scan for <c>IRForEach</c>/<c>IRTryCatch</c>'s structured edges) — the same "second oracle"
    /// discipline <c>CfgNaturalLoopTests.IndependentDominators</c> uses for the same reason.
    /// </summary>
    private static List<BasicBlock> IndependentEdgeRule(BasicBlock block)
    {
        var result = new List<BasicBlock>();
        void Add(BasicBlock b) { if (b != null) result.Add(b); }

        var terminator = block.GetTerminator();
        if (terminator is IRBranch branch)
        {
            Add(branch.Target);
        }
        else if (terminator is IRConditionalBranch cond)
        {
            Add(cond.TrueTarget);
            Add(cond.FalseTarget);
        }
        else if (terminator is IRSwitch sw)
        {
            Add(sw.DefaultTarget);
            if (sw.Cases != null) foreach (var (_, target) in sw.Cases) Add(target);
            if (sw.PatternCases != null) foreach (var patternCase in sw.PatternCases) Add(patternCase.Target);
        }
        // IRReturn: no successors.

        foreach (var inst in block.Instructions)
        {
            if (inst is IRForEach forEach)
            {
                Add(forEach.BodyBlock);
                Add(forEach.EndBlock);
            }
            else if (inst is IRTryCatch tryCatch)
            {
                Add(tryCatch.TryBlock);
                if (tryCatch.CatchClauses != null)
                    foreach (var catchClause in tryCatch.CatchClauses) Add(catchClause.Block);
                Add(tryCatch.FinallyBlock);
                Add(tryCatch.EndBlock);
            }
        }
        return result;
    }

    [TestCaseSource(nameof(Shapes))]
    public void SuccessorsOf_MatchesTheEdgeRule_ReimplementedIndependently(string shapeName)
    {
        var shape = CfgLoopShapes.Get(shapeName);
        var module = JsTestSupport.BuildModule(shape.Source, sourceFilePath: "prog.bas");
        var main = module.Functions.Single(f => f.Name == "Main" && !f.IsExternal);

        Assert.That(main.Blocks, Is.Not.Empty, shapeName + ": no blocks — the assertion below is vacuous");

        Assert.Multiple(() =>
        {
            foreach (var block in main.Blocks)
            {
                var actual = ControlFlowGraph.SuccessorsOf(block).ToList();
                var expected = IndependentEdgeRule(block);
                Assert.That(actual, Is.EqualTo(expected),
                    $"{shapeName}, block '{block.Name}': SuccessorsOf must yield exactly the edge "
                    + "rule's targets, in order. Got: [" + string.Join(", ", actual.Select(b => b?.Name ?? "<null>"))
                    + "], expected: [" + string.Join(", ", expected.Select(b => b?.Name ?? "<null>")) + "]");
            }
        });
    }

    /// <summary>
    /// The narrower claim the fixture brief asks for literally: <c>Build()</c> leaves
    /// <c>block.Successors</c> as the first-occurrence-deduplicated reduction of
    /// <c>SuccessorsOf(block)</c> — same edges, same order, <c>AddEdge</c>'s dedup notwithstanding.
    /// Pins the WIRING between the extracted method and its two call sites
    /// (<c>Build</c>/<c>IRVerifier</c>) rather than the edge rule itself (that is
    /// <see cref="SuccessorsOf_MatchesTheEdgeRule_ReimplementedIndependently"/>'s job).
    /// </summary>
    [TestCaseSource(nameof(Shapes))]
    public void BuildLeavesBlockSuccessors_AsTheDeduplicatedSuccessorsOfOrder(string shapeName)
    {
        var shape = CfgLoopShapes.Get(shapeName);
        var module = JsTestSupport.BuildModule(shape.Source, sourceFilePath: "prog.bas");
        var main = module.Functions.Single(f => f.Name == "Main" && !f.IsExternal);
        var cfg = new ControlFlowGraph(main);
        cfg.Build();

        Assert.Multiple(() =>
        {
            foreach (var block in main.Blocks)
            {
                var deduplicated = new List<BasicBlock>();
                foreach (var successor in ControlFlowGraph.SuccessorsOf(block))
                    if (successor != null && !deduplicated.Contains(successor))
                        deduplicated.Add(successor);

                Assert.That(block.Successors, Is.EqualTo(deduplicated),
                    $"{shapeName}, block '{block.Name}': block.Successors after Build() must equal "
                    + "SuccessorsOf(block) with duplicates removed, first occurrence kept.");
            }
        });
    }
}
