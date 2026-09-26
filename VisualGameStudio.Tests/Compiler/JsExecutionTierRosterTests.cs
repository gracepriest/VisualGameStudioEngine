using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Plan task 30 — the guard that keeps the JavaScript execution tier from vanishing quietly.
///
/// <para><b>Why the existing guard is not enough.</b>
/// <c>JavaScriptExecutionTests.ExecutionTier_IsNotSilentlySkipped</c> asserts that Node was
/// FOUND. That is a different claim from "the tier ran": delete every execution fixture,
/// empty every test body, or strip every <c>[Category("Integration")]</c>, and it still
/// passes. Worse, it lives INSIDE the Integration fixture, so
/// <c>--filter "TestCategory!=Integration"</c> — the command this project uses to declare
/// green — never runs it at all.</para>
///
/// <para>So this fixture carries NO category. It loads nothing, spawns nothing, and cannot
/// skip; it is structural, and it runs in the gate that gets used. The idiom is the one
/// <c>RaylibColorParityTests</c> already established: an explicit roster closed by a length
/// pin, so shrinking the roster fails instead of quietly weakening the guard.</para>
///
/// <para><b>The roster is <c>typeof</c>, not strings, deliberately.</b> Deleting a fixture
/// then becomes a COMPILE error rather than a runtime one — the earliest possible failure.</para>
/// </summary>
[TestFixture]
public class JsExecutionTierRosterTests
{
    /// <summary>
    /// Every fixture that compiles BasicLang and RUNS the result under Node. Keyed on TYPES,
    /// not files: JavaScriptStringTests.cs holds two fixtures and only one is in the tier, so
    /// a file-based count would be wrong.
    /// </summary>
    private static readonly Type[] ExecutionTier =
    {
        typeof(JavaScriptExecutionTests),
        typeof(JavaScriptNumericTests),
        typeof(JavaScriptControlFlowTests),
        typeof(JavaScriptDoLoopTests),
        typeof(JavaScriptStringExecutionTests),
        typeof(JavaScriptArrayTests),
        typeof(JavaScriptClassTests),
        typeof(JavaScriptCollectionTests),
        typeof(JavaScriptForEachTests),
        typeof(JavaScriptSelectCaseTests),
        typeof(JavaScriptExceptionTests),
        typeof(JavaScriptCatchDiscriminationTests),
        typeof(BaseConstructorCallExecutionTests),
        typeof(JavaScriptLambdaTests),
        typeof(JavaScriptMethodLambdaTests),
        typeof(JavaScriptModuleVariableTests),
        typeof(SubLambdaStatementExecutionTests),
        typeof(JavaScriptAddressOfTests),
        typeof(JavaScriptEventTests),
        typeof(JavaScriptForeignStateTests),
        typeof(DomDeclarationsExecutionTests),
        typeof(JavaScriptAsyncTests),
        typeof(JavaScriptIteratorTests),
        typeof(JavaScriptGenericsLinqTests),
        typeof(JavaScriptStdLibTests),
        typeof(JavaScriptOptimizedExecutionTests),
        typeof(JavaScriptCliProcessTests),
        typeof(JavaScriptInteropExecutionTests),
        typeof(ExternClassExecutionTests),

        // ⛔ These two were MISSING for as long as they have existed. Both drive
        // JavaScriptExecutionTests.RunJs — they are squarely in the tier — but neither name
        // starts with "JavaScript" or "Js", and the discovery guard below matched on exactly
        // those prefixes. So the test whose whole purpose is "catch a node-spawning fixture
        // that never got added to the roster" could not see them, and the floor never counted
        // their cases. Found only when the predicate was widened for ExternClassExecutionTests.
        typeof(BooleanOperatorExecutionTests),
        typeof(MemberCasingExecutionTests),

        // Task 24a. Mixed-backend fixture: its JavaScript rows drive
        // JavaScriptExecutionTests.RunJs AND the optimized JsTestSupport.CompileOptimized route,
        // so it is squarely in the tier even though it also runs the same programs on C# and C++.
        // ⛔ It belongs in the ROSTER, not in NotJavaScriptExecution below — the deny-list is only
        // for fixtures that never touch RunJs, and parking a node-spawning fixture there to quiet
        // this guard would silence the exact drift the guard exists to catch. Caught by
        // RosterCoversEveryJavaScriptIntegrationFixture on the 24a gate, which is the discovery
        // guard doing its job: the fixture was added with five node rows and never registered.
        typeof(TypedArrayLiteralExecutionTests),

        // The CSE invalidation / key-encoding fixtures. Their JavaScript leg is
        // JavaScriptOptimizedExecutionTests.RunOptimized — the STANDARD-pipeline runner, which is
        // the right one for CSE (a standard pass) and which spawns Node like any other row here.
        // ⚠ They are four-backend fixtures, so Node is one leg of four rather than the whole test;
        // they still belong in this roster, because if the tier stops running they stop proving
        // the JavaScript half of what they claim.
        typeof(CseInvalidationExecutionTests),
        typeof(CseKeyInjectivityExecutionTests),

        // Cross-backend (C#/C++/JS); its JS leg runs under Node via
        // JavaScriptOptimizedExecutionTests.RunOptimized.
        typeof(NegativeCaseLabelExecutionTests),

        // Runs one program under Node AND a C++ compiler, and compares both against .NET.
        typeof(InterpolatedStringExecutionTests),
        typeof(JavaScriptBooleanTextExecutionTests),

        // Both array spellings, run under Node, a C++ compiler and dotnet.
        typeof(ArrayBoundsExecutionTests),
        typeof(ReDimExecutionTests),
        typeof(SingleLineIfExecutionTests),
        // C# placement program; its JS leg runs under Node (the `_sel0` fix).
        typeof(CSharpNestedTerminatorExecutionTests),
        // Array literals on JavaScript, with C++ and C# legs.
        typeof(JavaScriptArrayLiteralExecutionTests),
        // Empty array literals, target-typed; C++ and C# legs too.
        typeof(EmptyArrayLiteralExecutionTests),

        // ADR-0005 D2 — CSE guards a shared value's own destination, not only its operands.
        // CseDestinationInvalidationExecutionTests / DestinationInvalidation_D4_ByRefExecutionTests
        // are four-backend fixtures (FourBackends.RunsOnEveryBackend[Aggressive]); their JS legs
        // run under JavaScriptExecutionTests.RunJs / FourBackends.RunAggressiveJs like any other
        // row here. CseDestinationKnownGapsTask133Tests (six of its eight pins are now CORRECT
        // under ADR-0006 D1; A1's C# leg alone stays known-wrong, task #136) is NOT caught by the
        // widened name match below — it neither starts with "JavaScript"/"Js" nor ends with
        // "ExecutionTests" — but its A1/A6 legs DO spawn Node (FourBackends.RunAggressiveJs), so
        // it belongs here for the same reason BooleanOperatorExecutionTests/MemberCasingExecutionTests
        // do (see their own note above).
        typeof(CseDestinationInvalidationExecutionTests),
        typeof(DestinationInvalidation_D4_ByRefExecutionTests),
        typeof(CseDestinationKnownGapsTask133Tests),

        // LICM's shared kill vocabulary (IROptimizer.cs, LoopInvariantCodeMotionPass; see
        // docs/superpowers/decisions/0003-cfg-loop-representation.md's Amendment section).
        // LicmKillVocabularyControlExecutionTests and LicmKillVocabularyExecutionTests both end
        // in "ExecutionTests" and would be caught by the widened name match below on their own;
        // listed explicitly anyway for the same reason every row above is. Their JS legs spawn
        // Node via FourBackends.RunsOnEveryBackendAggressive / RunAggressiveJs / the CLI
        // --optimize entry point's JavaScriptCodeGenerator + JavaScriptExecutionTests.RunNodeScript.
        typeof(LicmKillVocabularyControlExecutionTests),
        typeof(LicmKillVocabularyExecutionTests),
        // LicmKillVocabularyKnownGapsTask122Tests is NOT caught by the widened name match below —
        // it neither starts with "JavaScript"/"Js" nor ends with "ExecutionTests" — same reason
        // CseDestinationKnownGapsTask133Tests needed a manual entry above. Its JavaScript leg is
        // CORRECT under ADR-0006 D1's closure rule — x is genuinely in bump's capture set (task
        // #122 is now DISCHARGED, not merely the coarser interim fallback); C++ stays known-wrong
        // for an unrelated backend defect (task #140). Either way it DOES spawn Node
        // (FourBackends.RunAggressiveJs), so it belongs here.
        typeof(LicmKillVocabularyKnownGapsTask122Tests),

        // ADR-0005 D1 — `\` with a floating operand. Named "...ExecutionTests", so the widened
        // match below WOULD catch it on its own; listed explicitly anyway, matching every row
        // above. Its JS legs run through FourBackends.RunsOnEveryBackend[Aggressive] (contract
        // values, the When-guard SC6/SC7 value pins) and JavaScriptExecutionTests.RunJs directly
        // (the Z0/Z1/Z2 divide-by-zero pins) — all spawn Node.
        // FloatingIntegerDivisionStructuralTests is NOT here: it is pure front-end/codegen-text,
        // spawns nothing, and carries no [Category("Integration")] on purpose.
        typeof(FloatingIntegerDivisionExecutionTests),

        // ADR-0006 D3 — the one call-visibility rule (OptimizationPass.IsCallVisible). Named
        // "...ExecutionTests", so the widened match below WOULD catch both on its own; listed
        // explicitly anyway, matching every row above. CallVisibilityQ3ExecutionTests' JS legs run
        // through FourBackends.RunsOnEveryBackend[Aggressive] (Q3/Q3m); CallVisibilityQ3n
        // ExecutionTests' C#/C++/MSIL row spawns no Node, but its known-gap pin
        // (Q3n_JavaScript_KnownGap_ReferenceErrorOnMeK) does, via a local Node runner — it belongs
        // here for the same reason CseDestinationKnownGapsTask133Tests and
        // LicmKillVocabularyKnownGapsTask122Tests do (see their own notes above).
        // CallVisibilityHandBuiltIRTests and CallVisibilityDecisionTests are NOT here: pure
        // in-process IR/front-end fixtures, spawn nothing, carry no [Category("Integration")].
        typeof(CallVisibilityQ3ExecutionTests),
        typeof(CallVisibilityQ3nExecutionTests),

        // ADR-0006 D1 — the total kill vocabulary's EXTENSION arms (IRBaseMethodCall as a call;
        // IRFieldAccess/IRFieldStore as calls; the ByRef aliasing converse; the closure rule's
        // by-value-parameter half; IRInlineCode Universal; IRYield as a call). Named
        // "...ExecutionTests", so the widened match below WOULD catch both on its own; listed
        // explicitly anyway, matching every row above. KillVocabularyExtensionsExecutionTests'
        // JS legs run through FourBackends.RunsOnEveryBackend[Aggressive] / JavaScriptExecutionTests.
        // RunJs / FourBackends.RunAggressiveJs (B1r/B1/B2's JS-refusal check/A1o/A1p/P1/P3/
        // IN_javascript); KillVocabularyExtensionsAggressiveLoopExecutionTests' (B1L/W1L) JS legs
        // run through FourBackends.RunAggressiveJs.
        typeof(KillVocabularyExtensionsExecutionTests),
        typeof(KillVocabularyExtensionsAggressiveLoopExecutionTests),

        // ADR-0007 — bare-name property lowering fidelity. Named "...ExecutionTests", so the
        // widened match below WOULD catch it on its own; listed explicitly anyway, matching every
        // row above. BarePropertyLoweringExecutionTests' JS legs run through
        // JavaScriptOptimizedExecutionTests.RunOptimized (the standard pipeline — deliberately NOT
        // JavaScriptExecutionTests.RunJs, which runs no optimizer at all and would silently
        // certify a CopyPropagation/CSE regression, see that fixture's own doc comment) and
        // FourBackends.RunAggressiveJs (the aggressive pipeline); both spawn Node.
        typeof(BarePropertyLoweringExecutionTests),

        // Task #146 — CopyPropagationPass becomes a consumer of the shared kill vocabulary
        // (ADR-0006 D1/D3). Named "...ExecutionTests", so the widened match below WOULD catch it
        // on its own; listed explicitly anyway, matching every row above. Its JS legs run through
        // JavaScriptOptimizedExecutionTests.RunOptimized (the standard pipeline — deliberately NOT
        // JavaScriptExecutionTests.RunJs, which runs no optimizer at all and would silently
        // certify a CopyPropagation regression, same trap BarePropertyLoweringExecutionTests'
        // note above names) and FourBackends.RunAggressiveJs (the aggressive pipeline); both spawn
        // Node. CopyPropagationSharedVocabularyStructuralTests and
        // CopyPropagationSharedVocabularyUnitTests are NOT here: pure in-process IR fixtures,
        // spawn nothing, carry no [Category("Integration")].
        typeof(CopyPropagationSharedVocabularyExecutionTests),

        // A call's result stored into a variable read elsewhere (a lambda, another function).
        typeof(JsStoreOfCallResultRunTests),

        // Integral `\` / `Mod` by zero. Its name matches none of the patterns below, so the
        // coverage test cannot see it — listed by hand; its JS leg spawns Node.
        typeof(IntegerDivisionByZeroRunTests),

        // VB compound assignments (\=, &=, <<=, >>=) and `x =-1`. Named outside the patterns
        // below, so listed by hand; its JS leg spawns Node.
        typeof(CompoundAssignmentOperatorRunTests),

        // A Boolean as .NET text ("True"), and x.ToString() on a primitive.
        typeof(JsBooleanTextRunTests),

        // String interpolation with non-String holes. Named outside the patterns below, so listed
        // by hand; its JS leg spawns Node.
        typeof(StringInterpolationRunTests),

        // ADR-0006 D2 (task #137) — the use count Invariant S′ checks is dynamic, not static.
        // Named "...ExecutionTests", so the widened match below WOULD catch it on its own; listed
        // explicitly anyway, matching every row above. L1/L2's JS legs spawn Node via
        // JavaScriptCodeGenerator directly (the BL7002 refusal check); L3/L4/L5/L6/L7's JS legs
        // run through FourBackends.RunsOnEveryBackendAggressive / FourBackends.RunAggressiveJs.
        typeof(DynamicUseSPrimeExecutionTests),

        // Task #161 — CopyPropagationPass.Mentions becomes the union of ADR-0008's CollectReads
        // walk and the old structural descent into call-shaped operands (MentionsPastTheWalk).
        // Named "...ExecutionTests", so the widened match below WOULD catch it on its own; listed
        // explicitly anyway, matching every row above. E3/E4's JS leg runs through
        // FourBackends.RunAggressiveJs (inside RunsOnEveryBackendAggressive), which spawns Node.
        // CopyPropagationMentionsTests and CopyPropagationMentionsStructuralTests are NOT here:
        // pure in-process IR/front-end fixtures, spawn nothing, carry no [Category("Integration")].
        typeof(CopyPropagationMentionsExecutionTests),

        // Task #122 — the lambda capture set (ADR-0006 D1's Obligation). Named
        // "...ExecutionTests", so the widened match below WOULD catch it on its own; listed
        // explicitly anyway, matching every row above. Its JS legs all run through
        // FourBackends.RunAggressiveJs (K1/K8/K11/K12/N1/N8m/N8n). LambdaCaptureSetIrLevelTests,
        // LambdaCaptureSetFallbackTests and LambdaCaptureSetPrecisionStructuralTests are NOT
        // here: pure in-process IR/front-end/pass fixtures, spawn nothing, carry no
        // [Category("Integration")].
        typeof(LambdaCaptureSetExecutionTests),

        // Task #168 — a bare `For Each x In coll` over an existing variable REUSES it (ADR-0009).
        // Named "...ReuseTests", so the widened match below cannot see it — listed by hand. Its
        // JS legs run through FourBackends.RunsOnEveryBackend[Aggressive] (which call
        // JavaScriptExecutionTests.RunJs / FourBackends.RunAggressiveJs) for every reuse and
        // control shape but G4 (ByRef — JavaScript refuses that by design, so its own test never
        // asks the JS leg at all). ForEachControlVariableDiagnosticsTests is NOT here: pure
        // front-end/IR fixture, spawns nothing, carries no [Category("Integration")].
        typeof(ForEachControlVariableReuseTests),
    };

    /// <summary>
    /// The floor, not the current count (measured at 191 when this landed). Pinned BELOW the
    /// real total so adding tests never breaks the build while removing a meaningful chunk
    /// does — a guard that must be edited on every commit gets edited without thought.
    ///
    /// <para>Per-fixture deletion is caught precisely by the roster instead: <c>typeof</c>
    /// makes it a COMPILE error, and EveryFixtureStillHasTests catches an emptied one. This
    /// number is the backstop for the diffuse case — many tests removed across many files.</para>
    /// </summary>
    private const int MinimumExecutionCases = 150;

    /// <summary>
    /// Fixtures the widened name match sweeps up that are NOT part of the JavaScript execution
    /// tier — they run a different toolchain entirely.
    ///
    /// <para>An explicit deny-list is not ideal, but it is strictly better than the alternative
    /// it replaced: matching on a name PREFIX silently excluded real JS fixtures (see the
    /// roster's note on BooleanOperatorExecutionTests). Landing here is now a deliberate edit
    /// with a stated reason, not an accident of naming. Add an entry only for a fixture that
    /// genuinely does not use <c>JavaScriptExecutionTests.RunJs</c>.</para>
    /// </summary>
    private static readonly HashSet<string> NotJavaScriptExecution = new()
    {
        // Compile and run real C++ through CppToolchain — nothing to do with Node.
        "CppFinallyExecutionTests",
        "CppExitForExecutionTests",
        // Builds and runs the C# backend's output through the CLI and dotnet — no Node.
        "CSharpFieldAssignmentExecutionTests",
        "CSharpInlinedOperandExecutionTests",
    };

    /// <summary>Counts NUnit cases: a [TestCase]-driven method contributes one per attribute.</summary>
    private static int CaseCount(Type fixture)
    {
        var total = 0;
        foreach (var method in fixture.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            var cases = method.GetCustomAttributes<TestCaseAttribute>(inherit: true).Count();
            if (cases > 0) { total += cases; continue; }
            if (method.GetCustomAttributes<TestAttribute>(inherit: true).Any()) total++;
        }
        return total;
    }

    [Test]
    public void RosterIsPinned()
        => Assert.That(ExecutionTier, Has.Length.EqualTo(65), // task #168 added ForEachControlVariableReuseTests
            "The execution-tier roster changed. That is fine — update the number — but it must " +
            "be a deliberate edit, not a silent shrink.");

    /// <summary>
    /// Every fixture in the roster must still be a fixture with tests in it. An emptied
    /// fixture is the shape a false green takes when someone comments a file out.
    /// </summary>
    [Test]
    public void EveryFixtureStillHasTests()
    {
        foreach (var fixture in ExecutionTier)
        {
            Assert.That(fixture.GetCustomAttributes<TestFixtureAttribute>(true).Any(), Is.True,
                $"{fixture.Name} is no longer a [TestFixture].");
            Assert.That(CaseCount(fixture), Is.GreaterThan(0),
                $"{fixture.Name} has no tests left — it is in the roster but proves nothing.");
        }
    }

    /// <summary>
    /// The tier is defined by running under Node, which is why it is excluded from the fast
    /// subset. A fixture that loses its category silently changes which gate covers it.
    /// </summary>
    [Test]
    public void EveryFixtureIsStillCategorisedIntegration()
    {
        foreach (var fixture in ExecutionTier)
        {
            var categories = fixture.GetCustomAttributes<CategoryAttribute>(true)
                .Select(c => c.Name).ToList();

            Assert.That(categories, Does.Contain("Integration"),
                $"{fixture.Name} lost [Category(\"Integration\")]. It spawns a process, so it " +
                "must stay out of the fast subset — and the full run is what proves the " +
                "backend works.");
        }
    }

    /// <summary>
    /// THE headline assertion: the tier has not been hollowed out. Counts CASES rather than
    /// methods, because a [TestCase]-driven method is worth as many checks as it has rows.
    /// </summary>
    [Test]
    public void ExecutionTierHasNotShrunk()
    {
        var perFixture = ExecutionTier.ToDictionary(t => t.Name, CaseCount);
        var total = perFixture.Values.Sum();

        Assert.That(total, Is.GreaterThanOrEqualTo(MinimumExecutionCases),
            $"The JavaScript execution tier has shrunk to {total} cases (floor is " +
            $"{MinimumExecutionCases}). This tier is the ONLY thing that proves the backend " +
            "emits JavaScript that actually runs — text assertions cannot tell correct " +
            "lowering from lowering that compiles and behaves wrongly.\nPer fixture: " +
            string.Join(", ", perFixture.OrderBy(kv => kv.Value).Select(kv => $"{kv.Key}={kv.Value}")));
    }

    /// <summary>
    /// Catches a NEW node-spawning fixture that never got added to the roster — otherwise the
    /// roster slowly stops describing the tier and the floor stops meaning anything.
    /// </summary>
    [Test]
    public void RosterCoversEveryJavaScriptIntegrationFixture()
    {
        var rostered = new HashSet<Type>(ExecutionTier);

        var discovered = typeof(JavaScriptExecutionTests).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract)
            .Where(t => t.Namespace == typeof(JavaScriptExecutionTests).Namespace)
            // ⛔ WIDENED, and it immediately paid for itself. A prefix list is a guard that
            // stops working the moment a fixture is given a reasonable name:
            // ExternClassExecutionTests, BooleanOperatorExecutionTests and
            // MemberCasingExecutionTests all spawn Node and matched NEITHER "JavaScript" nor
            // "Js", so the last two sat outside the roster for their whole lives — the exact
            // omission this test exists to catch, invisible to it.
            .Where(t => t.Name.StartsWith("JavaScript", StringComparison.Ordinal) ||
                        t.Name.StartsWith("Js", StringComparison.Ordinal) ||
                        t.Name.EndsWith("ExecutionTests", StringComparison.Ordinal))
            .Where(t => !NotJavaScriptExecution.Contains(t.Name))
            .Where(t => t.GetCustomAttributes<CategoryAttribute>(true).Any(c => c.Name == "Integration"))
            .ToList();

        var missing = discovered.Where(t => !rostered.Contains(t)).Select(t => t.Name).ToList();

        Assert.That(missing, Is.Empty,
            "These JavaScript Integration fixtures are not in the roster, so the floor above " +
            "does not account for them: " + string.Join(", ", missing));
    }
}
