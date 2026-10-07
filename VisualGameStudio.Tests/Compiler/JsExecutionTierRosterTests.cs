using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using VisualGameStudio.Tests.Msil;

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
        // Task #170 / ADR-0016 — B1-B5, W1/W2, C1 and the S/t170/edge probes through the standard
        // pipeline, the aggressive pipeline and CompileProjectFiles; the JavaScript legs run under
        // Node (JavaScriptExecutionTests.RunJs / FourBackends.RunAggressiveJs / RunNodeScript).
        // Named "...ExecutionTests", so the discovery guard below WOULD catch it on its own.
        typeof(BaseConstructorCallLoweringExecutionTests),
        // ADR-0016 D3 (amended) — the C++ refusal, #140's regression fence and the FE1/CR1 witnesses. Its
        // FE1/CR1 rows run the other three backends, JavaScript under Node (RunJs), so it is in the tier.
        typeof(BaseConstructorCallCppRefusalTests),
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
        // Array types written on the type (`As Integer()`); C++ and C# legs too.
        typeof(ArrayTypeSuffixExecutionTests),
        // Get/Set properties, now reachable on C++ (task #148); every leg incl. Node.
        typeof(PropertyAccessorExecutionTests),
        // Chained indexing over array values; C++ and C# legs too.
        typeof(ChainedIndexingExecutionTests),
        // Arrays as references (C++ BasicLang::Array); C++ and C# legs too.
        typeof(ArrayReferenceSemanticsExecutionTests),
        // ParamArray packing at the call site; C++ and C# legs too.
        typeof(ParamArrayExecutionTests),
        // CType/conversions/reference casts with VB rules; C++ and C# legs too.
        typeof(CTypeConversionExecutionTests),
        // Method calls through an interface, typed; C++ and C# legs too.
        typeof(InterfaceMethodTypingExecutionTests),

        // ADR-0005 D2 — CSE guards a shared value's own destination, not only its operands.
        // CseDestinationInvalidationExecutionTests / DestinationInvalidation_D4_ByRefExecutionTests
        // are four-backend fixtures (FourBackends.RunsOnEveryBackend[Aggressive]); their JS legs
        // run under JavaScriptExecutionTests.RunJs / FourBackends.RunAggressiveJs like any other
        // row here. CseDestinationKnownGapsTask133Tests (six of its eight pins are now CORRECT
        // under ADR-0006 D1; A1's C# leg alone stayed known-wrong until task #136) is NOT caught by the
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
        // ExecutionTests' C#/C++/MSIL row spawns no Node, but its JavaScript rows do (#143: Q3n and
        // the Q3b/Q3d/Q3e/Q3h/Q3i field-named-value read-back rows, through TempExec — the CLI, the
        // CLI with --optimize and CompileProjectFiles, run under Node).
        // CallVisibilityHandBuiltIRTests, CallVisibilityDecisionTests and CallVisibilityQ3nJavaScriptTextTests
        // are NOT here: pure in-process IR/front-end/codegen-text fixtures, spawn nothing, carry no
        // [Category("Integration")].
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

        // The type keywords' Shared members (String.Format, Integer.Parse, …). Named outside the
        // patterns below, so listed by hand; its JS legs spawn Node.
        typeof(PrimitiveStaticSurfaceRunTests),
        // Select Case When guards: calls, AndAlso/OrElse, casts, a binding pattern.
        typeof(WhenGuardCallRunTests),

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

        // Task #164 — a multi-line `Function(...) [As T] ... End Function` lambda. Named
        // "...ExecutionTests", so the widened match below WOULD catch it on its own; listed
        // explicitly anyway, matching every row above. Its JS legs run through
        // JavaScriptExecutionTests.RunJs (standard pipeline) and FourBackends.RunAggressiveJs
        // (aggressive), both spawning Node. MultiLineFunctionLambdaTests (front end only, no
        // process spawned, no [Category("Integration")]) is NOT here.
        typeof(MultiLineFunctionLambdaExecutionTests),

        // Task #171 — a String For Each collection enumerates as Char (VB's rule). Named
        // "...ExecutionTests", so the widened match below WOULD catch it on its own; listed
        // explicitly anyway, matching every row above. Its JS legs run through
        // FourBackends.RunsOnEveryBackend (S2/S3/S7/E1/E2/E6/E7/E12, which call
        // JavaScriptExecutionTests.RunJs) and directly via JsTestSupport.BuildModule +
        // JavaScriptCodeGenerator (the pinned S1/S5 BL7004 refusal texts). ForEachOverStringTests
        // (front end/IR only, no process spawned, no [Category("Integration")]) is NOT here.
        typeof(ForEachOverStringExecutionTests),

        // Task #173 — Nothing converts to any reference type at every conversion site. Named
        // "...ExecutionTests", so the widened match below WOULD catch it on its own; listed
        // explicitly anyway, matching every row above. Its JS legs run through
        // FourBackends.RunsOnEveryBackend[Aggressive] (the 15 probes that run on every backend,
        // N7 included since #189) and JavaScriptExecutionTests.RunJs directly (the S1-S3 rows,
        // which since #189 assert the SAME "" text every other backend does). NothingConversionTests
        // (front end/IR only, no process spawned, no [Category("Integration")]) is NOT here.
        typeof(NothingConversionExecutionTests),

        // Task #183 — MSIL `&` with a value operand, and Console.Write/WriteLine of every value
        // type. Lives in the VisualGameStudio.Tests.Msil namespace, NOT
        // VisualGameStudio.Tests.Compiler, so RosterCoversEveryJavaScriptIntegrationFixture's own
        // namespace filter below cannot discover it automatically — listed here by hand, same as
        // every manually-added row above. Its JS legs run through FourBackends.RunsOnEveryBackend
        // / RunsOnEveryBackendAggressive (C2/C3/W2/E7, and E8 too since #189 made JS agree with
        // the other three backends there). MsilValueToStringTests (pure in-process IL-text
        // fixture, spawns nothing, no [Category("Integration")]) is NOT here.
        typeof(MsilValueToStringExecutionTests),

        // Task #185 — `Is` / `IsNot` reference identity (ADR-0011). Named "...ExecutionTests", so
        // the widened match below WOULD catch it on its own; listed explicitly anyway, matching
        // every row above. Its JS legs run through FourBackends.RunsOnEveryBackend[Aggressive]
        // (the kind-table/two-operand/E-series probes, C1/C2/E7's promoted #189 rows, E10/E11's
        // lambda rows, P13's fold) and JavaScriptExecutionTests.RunJs / JsTestSupport.CompileOptimized
        // directly (P12's named C++ divergence and E11's parentheses-wrap mutant check).
        typeof(IsIsNotOperatorExecutionTests),
        typeof(NotPrecedenceExecutionTests),

        // Task #176 — one `Me` per member, typed as its own class. Named "...ExecutionTests", so
        // the widened match below WOULD catch it on its own; listed explicitly anyway, matching
        // every row above. Its JS legs run through FourBackends.RunsOnEveryBackend[Aggressive]
        // (the V6 family and most edge probes) and JavaScriptExecutionTests.RunJs /
        // FourBackends.RunAggressiveJs directly (X1/X2, which exclude C++, and X3b's JS leg;
        // X3b's C# leg went through a #136 pin until that was fixed).
        typeof(MeReceiverTypingExecutionTests),

        // Task #187 — a lambda or AddressOf into a user Delegate type; invoking a user delegate
        // returns its type. Named "...ExecutionTests", so the widened match below WOULD catch it
        // on its own; listed explicitly anyway, matching every row above. Its JS legs run
        // through FourBackends.RunsOnEveryBackend[Aggressive] (D1-D6, and — since #188 — E10/J2/
        // E5/E5b/J1, promoted into that same runner) and JavaScriptExecutionTests.RunJs directly
        // (the edge probes, E13's own JS-only pass).
        typeof(UserDelegateConversionExecutionTests),

        // Task #188 — invoking a delegate-typed field or property through its MEMBER spelling
        // (bare, Me., obj., Class.), own or inherited, Shared included. Named "...ExecutionTests",
        // so the widened match below WOULD catch it on its own; listed explicitly anyway, matching
        // every row above. Its JS legs run through FourBackends.RunsOnEveryBackend[Aggressive]
        // (F0-F9 and the G/J2f probes) and JavaScriptExecutionTests.RunJs / RunNodeScript directly
        // (L1's IIFE, the multi-file project's JS leg, G8's Nothing-raises pin, and G5's
        // BL7005-by-design refusal check).
        typeof(DelegateMemberInvocationExecutionTests),

        // Task #189 — a Nothing String in `&`/Write (JS, C#); a Catch variable captured by a
        // lambda (C++). Named "...ExecutionTests", so the widened match below WOULD catch it on
        // its own; listed explicitly anyway, matching every row above. Its JS legs run through
        // FourBackends.RunsOnEveryBackend[Aggressive] (J1-J6/C1-C3 and the edge probes that run
        // everywhere) and JavaScriptExecutionTests.RunJs directly (J6's silent-6-vs-123 pin, E10's
        // JavaScript List-bounds leg, now `same=True` since #207). NothingStringTextTests (pure codegen-text fixture,
        // spawns nothing, no [Category("Integration")]) is NOT here.
        typeof(NothingStringTextExecutionTests),

        // Task #177 — MSIL boxes a value into an Object slot, converts out of one, and compares
        // Objects late-bound. Lives in the VisualGameStudio.Tests.Msil namespace, NOT
        // VisualGameStudio.Tests.Compiler, so RosterCoversEveryJavaScriptIntegrationFixture's own
        // namespace filter cannot discover it automatically — listed here by hand, same as
        // MsilValueToStringExecutionTests above. Its JS legs run through
        // JavaScriptExecutionTests.RunJs directly (most O/E/C/L probes) and
        // JavaScriptOptimizedExecutionTests.RunOptimized (L11 and L11b, the Object compares the optimizer
        // used to fold WRONG (#214, fixed; L11b's JavaScript leg prints vbc's answer since #215), which need the
        // STANDARD-pipeline runner — JavaScriptExecutionTests.RunJs runs no optimizer at all and would silently
        // miss the constant fold they are about, the same trap BarePropertyLoweringExecutionTests' own note
        // above names).
        typeof(MsilObjectBoxingExecutionTests),

        // Task #178 — BC30526 (write to a ReadOnly property) / BC30524 (read of a WriteOnly one).
        // Named "...ExecutionTests", so the widened match below WOULD catch it on its own; listed
        // explicitly anyway, matching every row above. Its JS legs run through
        // FourBackends.RunsOnEveryBackend[Aggressive] (L1) and JavaScriptExecutionTests.RunJs
        // directly (the N1/N2 #222 pins).
        typeof(PropertyAccessExecutionTests),

        // Task #169 (plus #199), ADR-0013 — case-insensitive name binding between the front end
        // and the IR. Named "...ExecutionTests", so the widened match below WOULD catch it on
        // its own; listed explicitly anyway, matching every row above. Its JS legs run through
        // FourBackends.RunsOnEveryBackend[Aggressive] (the K/leak/X1/edge probes and E18)
        // and JavaScriptExecutionTests.RunJs/RunNodeScript directly (the K4/K9 pins' JS leg and
        // E19's multi-file project-entry-point leg). NameBindingTests (front end/IR only, no
        // process spawned, no [Category("Integration")]) is NOT here.
        typeof(NameBindingExecutionTests),

        // Task #172, ADR-0014 (D1-D6, amended A1/A2) — VB's per-iteration loop-body Dim, with
        // copy-forward, on every backend. Both named "...ExecutionTests", so the widened match
        // below WOULD catch them on their own; listed explicitly anyway, matching every row
        // above. PerIterationLoopBodyDimExecutionTests' JS legs run through
        // FourBackends.RunsOnEveryBackend[Aggressive] (the headline L/E-probe table) and
        // JavaScriptExecutionTests.RunJs/RunNodeScript directly (the pinned edge cases and the
        // project-entry-point leg); PerIterationLoopBodyDimOptimizerExecutionTests' JS legs run
        // through JavaScriptExecutionTests.RunJs and FourBackends.RunAggressiveJs (K1-K5, A2's
        // own LICM regression set — the reason this pair exists is to prove JavaScript no longer
        // throws ReferenceError under -O). PerIterationLoopBodyDimByteIdentityTests/
        // IrFactTests/VerifierTests (Compiler namespace, no process spawned) and
        // PerIterationLoopBodyDimMsilTests (Msil namespace — ilasm/dotnet, no Node) are NOT here.
        typeof(PerIterationLoopBodyDimExecutionTests),
        typeof(PerIterationLoopBodyDimOptimizerExecutionTests),

        // Task #174 — VB's lambda-boundary diagnostics (BC36639/BC30616/BC30734/BC36667). Named
        // "...ExecutionTests", so the widened match below WOULD catch it on its own; listed
        // explicitly anyway, matching every row above. Its JS legs run through
        // FourBackends.RunsOnEveryBackend[Aggressive] (N5/R6/S5) and JsTestSupport.Compile /
        // JavaScriptExecutionTests.RunJs directly (N8/N9's run, and R3/R7's own BL7002-by-design
        // refusal pins). LambdaBoundaryDiagnosticsTests (Compiler namespace, no process spawned —
        // the fast-subset analyzer/LSP fixture) is NOT here.
        typeof(LambdaBoundaryDiagnosticsExecutionTests),

        // A class or interface used above its declaration; its JS legs run under Node (the
        // base-first class order is what keeps `class D extends B` out of the TDZ).
        typeof(ForwardDeclaredTypeExecutionTests),

        // #197 — `TypeOf x Is T`; its JS legs run under Node (a class target; interfaces are BL7013).
        typeof(TypeOfExecutionTests),
        // Task #163 / ADR-0017 — every probe of the dead-code temp marker (R1-R11, U1-U8, the by-name rule's
        // CT_wbr_t0 witness) through the CLI, the CLI with --optimize and CompileProjectFiles on C#, C++,
        // JavaScript (Node: TempExec.Run -> JavaScriptExecutionTests.RunNodeScript) and MSIL. Named
        // "...ExecutionTests", so the widened match below would catch it on its own; listed explicitly anyway.
        typeof(CompilerTempExecutionTests),
        // ...and #121's regression fence, which runs the same programs on the same four backends (its JavaScript
        // cells pin a Node ReferenceError). Named "...FenceTests", so the widened match below does NOT see it:
        // listed by hand, like every row that is not named "...ExecutionTests".
        typeof(CompilerTempCollisionFenceTests),
        // Task #121 / ADR-0018 — the collision fence flipped, and the two fixtures that prove name reservation by RUNNING programs.
        // TempMintingFacilityTests: a test-only optimizer pass, registered through OptimizationPipeline.AddPass, mints one temp through
        // DeclareTemp in a function that spells the name it would take (Dim, parameter, For Each, Catch, pattern, lambda parameter,
        // captured name, module function), and each program runs on C#, C++, JavaScript (Node: TempExec.Run -> RunNodeScript) and MSIL.
        // Named "...FacilityTests", so the widened match below does NOT see it: listed by hand.
        typeof(TempMintingFacilityTests),
        // NameReservationExecutionTests: the whole ADR-0017 Findings 3 witness matrix (t0..t3, T0..T3 and the v0..v3 controls) through the
        // CLI, the CLI with --optimize and CompileProjectFiles, on the same four backends. Named "...ExecutionTests", so the widened
        // match below would catch it on its own; listed explicitly anyway.
        typeof(NameReservationExecutionTests),

        // Task #124 / ADR-0013 D3 — every probe the fix moved to vbc's answer (a bare field, an inherited field, a Shared field,
        // a property, a For / For Each control over a local, a parameter, a field or a global, AddressOf, MyBase.m, a Shared method
        // through its class, New, AddHandler, RaiseEvent, an Await callee, a Module variable before its Module, and the multi-file
        // MF1/MF2/MF5), each in another case from its declaration and with its same-case control, through the CLI, the CLI with
        // --optimize and CompileProjectFiles. Named "...ExecutionTests", so the widened match below would catch it on its own; listed
        // explicitly anyway. Its JavaScript cells run under Node (TempExec.Run -> JavaScriptExecutionTests.RunNodeScript).
        typeof(NameBindingResolutionExecutionTests),

        // Task #123 — an untyped Const (D1) and VB's conditional If(cond, a, b) (D2), every probe vbc answers, through the CLI, the CLI with
        // --optimize and CompileProjectFiles on C#, C++, JavaScript (Node: TempExec.Run -> JavaScriptExecutionTests.RunNodeScript) and MSIL, plus
        // the multi-file MC1/MC3 through BasicLang build. Named "...ExecutionTests", so the widened match below would catch it on its own; listed
        // explicitly anyway. The only-the-chosen-operand side-effect rows run on JavaScript too (i1side, i3nest, i10sc).
        typeof(UntypedConstAndConditionalExecutionTests),

        // Task #123 — the mixed-width numeric compare fold (ConstantFoldingPass.TryFoldCompare), executed on every backend under
        // both pipelines against vbc's output. Its JavaScript legs run RunJs (no optimizer) AND JavaScriptOptimizedExecutionTests.
        // RunOptimized, the one that reaches the fold. Named "...ExecutionTests", so the widened match below would catch it on
        // its own; listed explicitly anyway.
        typeof(MixedNumericCompareFoldExecutionTests),

        // Task #256 — a While/Do condition holding AndAlso, OrElse or If() runs once per iteration on C# (the grid of 5 loop forms x 7 condition
        // kinds, Exit, nesting, a class method, a Try, per-iteration Dims, an If in the body), against vbc, through the CLI, the CLI with
        // --optimize and CompileProjectFiles. Its JavaScript CONTROL cells (the plain-condition and one-call rows) RUN under Node (TempExec.Run ->
        // JavaScriptExecutionTests.RunNodeScript); every other JavaScript row is a pinned refusal (#257) that spawns nothing. Its C# cells run in a
        // child process with a time limit (CSharpProcessRunner). Named "...ExecutionTests", so the widened match below would catch it on its own.
        typeof(LoopConditionReevaluationExecutionTests),

        // Task #141 — BasicLang's own `++` / `--` write their operand, with C's meaning (a statement, `y = x++`, `y = ++x`, `x++ + x++`, a field, an
        // array element, a module member, a property, a ByRef parameter, `Do While j-- > 0`) on C#, C++, JavaScript and MSIL, through the CLI, the CLI with
        // --optimize and CompileProjectFiles. Its JavaScript cells RUN under Node (TempExec.Run -> JavaScriptExecutionTests.RunNodeScript); JavaScript has no
        // ByRef row (BL7002) and no Long/ULong program (BL7003). Its C# cells run in a child process with a time limit (CSharpProcessRunner). Named
        // "...ExecutionTests", so the widened match below would catch it on its own; listed explicitly anyway. Its cases are [TestCase] rows, which CaseCount counts.
        typeof(IncrementDecrementExecutionTests),

        // Task #136 — a lambda body is written by the function-body emitter on C#: 74 programs (a Sub lambda writing a capture / parameter / field /
        // global, a multi-line Function lambda with an assignment, call, If, loop, Dim, Select Case and Try before its Return, nested lambdas, lambdas in a
        // constructor, MyBase.New(...) arguments and a module global's initialiser, #165's own Dims, #179's counters, #237's Me capture, #166's statement-form
        // ByRef call, the name-leak shapes) against vbc through the CLI, the CLI with --optimize and CompileProjectFiles on C# (hang-safe), plus the same programs
        // as controls on C++, JavaScript (Node: TempExec.Run -> JavaScriptExecutionTests.RunNodeScript) and MSIL, and `BasicLang build -c Release` for three. Named
        // "...ExecutionTests", so the widened match below would catch it on its own; listed explicitly anyway.
        typeof(LambdaBodyEmissionExecutionTests),

        // Property-grid slice 5 — a designer-written page (RegionWriter's listeners, the VgsOn_ wrappers, the Form's Load
        // call) built by the CLI on the project route and RUN under node. Named "...RunTests", so the widened match below
        // cannot see it — listed by hand.
        typeof(FormEventWebRunTests),
        // Task #139 — a statement-level `MyBase.M(...)` call is written on C#: 33 programs (a Sub, a Function whose result is discarded, no arguments; a constructor, a property Get and Set,
        // a Sub lambda and a Function lambda, If/Else, For/While/Do/For Each, Select Case, Try/Catch/Finally, a grandparent and a three-level chain, a generic derived class and a generic
        // method, an Object parameter, Optional, ParamArray; the value forms that were right before: Dim, local, field, parameter, two calls, a Select selector, an If condition, Return, a
        // nested call) against vbc through the CLI, the CLI with --optimize and CompileProjectFiles on C# (hang-safe), plus the same programs as controls on C++, JavaScript (Node:
        // TempExec.Run -> JavaScriptExecutionTests.RunNodeScript) and MSIL, `BasicLang build -c Release` for three, and the ByRef rows (#265) pinned as refusals. Named "...ExecutionTests",
        // so the widened match below would catch it on its own; listed explicitly anyway.
        typeof(MyBaseMethodCallStatementExecutionTests),
        // Tasks #142, #265, #213 — a `MyBase.M(...)` call carries its target's ByRef, Optional, ParamArray and declared-parameter-type facts: 8 programs (a ByRef Sub and Function, a ByRef
        // field with an Optional left out, an Object parameter, Optional one/all left out, a ParamArray with 0/1/3 arguments, a grandparent, a statement and a value call, a captured ByRef)
        // against vbc through the CLI, the CLI with --optimize and CompileProjectFiles on C# (hang-safe), C++, MSIL and — where the program has no ByRef and no Long — JavaScript (Node:
        // TempExec.Run -> JavaScriptExecutionTests.RunNodeScript); the captured-ByRef refusal on C++ and MSIL; `BasicLang build -c Release` for two. Named "...ExecutionTests", so the
        // widened match below would catch it on its own; listed explicitly anyway.
        typeof(MyBaseCallArgumentsExecutionTests),
        // Task #145 — on JavaScript an Iterator Function / Iterator method returns a re-iterable generator: 8 probes (a class iterator, a method with parameters, Exit Function, a result walked twice,
        // a Shared iterator, MyBase inside an iterator, a lambda inside one) against vbc through the CLI, the CLI with --optimize and CompileProjectFiles, run under Node. Named "...ExecutionTests",
        // so the widened match below would catch it on its own; listed explicitly anyway. Its cases are [TestCase] rows, which CaseCount counts.
        typeof(JavaScriptIteratorExecutionTests),
        // Task #150 — an Overridable auto-property's override dispatches on JavaScript (a get/set pair over a slot named by its declaring class,
        // not a class field that shadows the derived accessor): 9 programs (P16/P16q, a base-typed variable, the base's own methods, a ReadOnly
        // constructor write, a three-level chain, a base-constructor write, a derived auto-override keeping it, an interface receiver) against vbc
        // through the CLI, the CLI with --optimize and CompileProjectFiles under Node (TempExec.Run -> JavaScriptExecutionTests.RunNodeScript), plus
        // the ReadOnly constructor write on C++. Named "...ExecutionTests", so the widened match below would catch it on its own; listed explicitly
        // anyway. OverridableAutoPropertyEmissionTests (JS text only, no process spawned, no [Category("Integration")]) is NOT here.
        typeof(OverridableAutoPropertyExecutionTests),
        // Task #151 — a user class that `Inherits Exception` reads `Message` (bare, `Me.`, through a Catch variable, down a two-level chain, beside a user member) on JavaScript and MSIL: 9 programs against vbc
        // through the CLI, the CLI with --optimize and CompileProjectFiles. Its JavaScript cells RUN under Node (TempExec.Run -> JavaScriptExecutionTests.RunNodeScript); its MSIL cells skip without ilasm. Named
        // "...ExecutionTests", so the widened match below would catch it on its own; listed explicitly anyway. UserExceptionSubclassMsilIlShapeTests (IL text only, no process, no [Category("Integration")]) is NOT here.
        typeof(UserExceptionSubclassExecutionTests),
        // Task #144 — a ByRef CONSTRUCTOR parameter writes back on every backend, and a value passed to one is copied in (VB's rule): 14 programs (a local, a String, a loop local, a field, an array
        // element, `MyBase.New` through two constructors, beside an Optional, in an assignment and a Return, an Optional ByRef through an implicit and an explicit base; a literal, an expression, a
        // Const, a call, the evaluation order, a loop) against vbc through the CLI, the CLI with --optimize and CompileProjectFiles on C# (hang-safe), C++, MSIL and — for the copy-in rows, which
        // have no storage a write could be lost from — JavaScript (Node: TempExec.Run -> JavaScriptExecutionTests.RunNodeScript); JavaScript's BL7002 refusal of a variable argument. Named
        // "...ExecutionTests", so the widened match below would catch it on its own; listed explicitly anyway. ConstructorByRefShapeTests (C# text, no process) is NOT here.
        typeof(ConstructorByRefExecutionTests),
        // Task #152 — a Private property, auto-property, field, Const, Sub, Function and Shared member named bare in a method ABOVE its declaration resolves inside its
        // own class: 9 rows (P17, P17a+P17o, P17f+P17c, P17s, P17sh, a derived class above and below its base, a derived class's own same-named P, a lambda, a class in
        // another file) on C#, C++, JavaScript and MSIL through the CLI, the CLI with --optimize and CompileProjectFiles (JavaScript under Node via
        // TempExec.Run -> JavaScriptExecutionTests.RunNodeScript). Named "...ExecutionTests", so the widened match below would catch it on its own; listed explicitly anyway.
        // PrivateMemberDeclaredBelowRefusalTests (front end only, no process spawned, no [Category("Integration")]) is NOT here.
        typeof(PrivateMemberDeclaredBelowExecutionTests),
        // Task #181 — AscW/Asc, ChrW/Chr and CByte..CULng typed and lowered as VB does: 21 vbc-answered probes (a Char vs a String argument, half-to-even at .5, CByte(True) = 255, a user function named like an
        // intrinsic, #171's S6) in 6 groups, on every backend each runs on, through the CLI, the CLI with --optimize and CompileProjectFiles. Its JavaScript cells RUN under Node (TempExec.Run ->
        // JavaScriptExecutionTests.RunNodeScript); its C++ and MSIL cells skip without a compiler / ilasm. Named "...ExecutionTests", so the widened match below would catch it on its own; listed explicitly
        // anyway. VbConversionIntrinsicCompileTests (in process, no Node, no [Category("Integration")]) is NOT here.
        typeof(VbConversionIntrinsicExecutionTests),
        // Task #184 — a Char widens to a String, the VB way: 19 vbc-answered probes (a Dim, Chr(65), a Const, a Return, an argument, a delegate call, a lambda return, a field, a module variable, an array
        // literal, a constructor and MyBase argument, an Optional default, a For Each variable, a Char in a String Select, `c = "a"`) in 5 groups on C#, C++ and MSIL, plus a JavaScript group of the 7 that
        // hold no Char local, each through the CLI, the CLI with --optimize and CompileProjectFiles. Its JavaScript group RUNS under Node (TempExec.Run -> JavaScriptExecutionTests.RunNodeScript); its C++
        // and MSIL cells skip without a compiler / ilasm. Named "...ExecutionTests", so the widened match below would catch it on its own; listed explicitly anyway.
        // CharWidensToStringCompileTests (in process, no Node, no [Category("Integration")]) is NOT here.
        typeof(CharWidensToStringExecutionTests),
        // Task #186 — Nothing into a value type is its default, the VB way: 28 vbc-answered probes (an Integer, Boolean and Double Dim and assignment, a field, a module variable, a member, a Return, an argument, a `New`
        // and `MyBase.New` argument, an Optional, a delegate call, an array element, a typed array literal, an `If()` operand, `n = Nothing`, `Case Nothing`, Long/Char/Decimal/narrow integers, a Structure, a DateTime,
        // a type parameter, an Enum, a tuple) in 8 groups, each on every backend it runs on (groups 1-5 on all four, JavaScript included) through the CLI, the CLI with --optimize and CompileProjectFiles. Its JavaScript
        // cells RUN under Node (TempExec.Run -> JavaScriptExecutionTests.RunNodeScript); its C++ and MSIL cells skip without a compiler / ilasm. Named "...ExecutionTests", so the widened match below would catch
        // it on its own; listed explicitly anyway. NothingIntoValueTypeCompileTests (in process, no Node, no [Category("Integration")]) is NOT here.
        typeof(NothingIntoValueTypeExecutionTests),
        // Task #190 — `&` converts both operands to String, the VB way: 12 vbc-answered probes (two Integers, an Integer and a Double, a Boolean, a chain `1 & 2 & 3`, Nothing on either side, an
        // append-assign, a function argument, a Char, an Object, a Decimal beside a Double literal, a Long, and the String-side controls) in 3 groups on every backend each runs on, each through the CLI,
        // the CLI with --optimize and CompileProjectFiles, plus the class-operand refusal through the CLI and the project build. Its JavaScript cells RUN under Node (TempExec.Run ->
        // JavaScriptExecutionTests.RunNodeScript); its C++ and MSIL cells skip without a compiler / ilasm. Named "...ExecutionTests", so the widened match below would catch it on its own; listed explicitly
        // anyway. AmpersandConcatCompileTests (in process, no Node, no [Category("Integration")]) is NOT here.
        typeof(AmpersandConcatExecutionTests),
        // Task #206 (and #205) — a String `=` / `<>` reads Nothing as "" the VB way: 11 vbc-answered rows (`Nothing = ""` folded in both orders, a String never assigned against "" with `=` and `<>`,
        // `s = Nothing`, two String variables, two equal strings built separately, a String field never assigned, a Function returning Nothing As String, a Select Case over a Nothing String,
        // a When guard with `Case Is Nothing`, and `Case ""` / `Case Is Nothing` in both orders) on C#, C++, JavaScript and MSIL, each through the CLI, the CLI with --optimize and
        // CompileProjectFiles. Its JavaScript cells RUN under Node (TempExec.Run -> JavaScriptExecutionTests.RunNodeScript); its C++ and MSIL cells skip without a compiler / ilasm. Named
        // "...ExecutionTests", so the widened match below would catch it on its own; listed explicitly anyway. StringNothingEqualityFoldTests (hand-built IR, no process, no
        // [Category("Integration")]) is NOT here. Its cases are [TestCase] rows and [Test]s, which CaseCount counts.
        typeof(StringNothingEqualityExecutionTests),
        // Task #217 — a lambda parameter named like a class FIELD or a MODULE GLOBAL is NOT hidden-by-error (BC36641 reports only a local or parameter of the procedure around the lambda): two vbc-answered probes
        // (14 and 9) on C#, C++ and JavaScript, each through the CLI, the CLI with --optimize and CompileProjectFiles. Its JavaScript cells RUN under Node (TempExec.Run ->
        // JavaScriptExecutionTests.RunNodeScript); its C++ cells skip without a compiler. Named "...ExecutionTests", so the widened match below would catch it on its own; listed explicitly anyway.
        // LambdaParameterHidesDiagnosticsTests (in process, no Node, no [Category("Integration")]) is NOT here.
        typeof(LambdaParameterHidesExecutionTests),
        // Task #202 — a user Delegate's three gaps, RUN: a file-level `Public` / `Friend` Delegate (S01-S03), a Delegate declared in a SIBLING file used as a field, parameter and local type in both compile orders
        // (M1-M4, through CompileProjectFiles), and `.Invoke` on a Func / Action value (S07-S14, S08 not on MSIL and S13 not on JavaScript: known failing cells), each printing vbc's answer on C#, C++, JavaScript and MSIL
        // through the CLI, the CLI with --optimize and CompileProjectFiles. Its JavaScript cells RUN under Node (TempExec.Run -> JavaScriptExecutionTests.RunNodeScript); its C++ and MSIL cells skip without a compiler /
        // ilasm. Named "...ExecutionTests", so the widened match below would catch it on its own; listed explicitly anyway. UserDelegateGapsDiagnosticsTests (in process, no Node, no [Category("Integration")]) is NOT here.
        typeof(UserDelegateGapsExecutionTests),
        // Task #267 — the expression statements VB refuses (BC30035 / BC30545 / BC30057 / BC30454) are refused, and the statements it accepts still run: three vbc-answered programs (Me.M() / MyBase.M() /
        // a bare Helper() / a discarded Function, a delegate array element INVOKED as fs(0)(); RaiseEvent / AddHandler on C# and JavaScript only) on C#, C++ and JavaScript, each through the CLI, the CLI with --optimize and
        // CompileProjectFiles; plus the real CLI refusing one shape of each family on five targets. Its JavaScript cells RUN under Node (TempExec.Run -> JavaScriptExecutionTests.RunNodeScript); its
        // C++ cells skip without a compiler. Named "...ExecutionTests", so the widened match below would catch it on its own; listed explicitly anyway. ExpressionStatementVbRefusalTests (in process,
        // no Node, no [Category("Integration")]) is NOT here.
        typeof(ExpressionStatementVbRefusalExecutionTests),
        // Task #204 — `b.Items(0)`, a paren element read through a qualified receiver, lowers like the bare `Items(0)`: 13 vbc-answered programs (List / Dictionary / List(Of Action) / array members read, written and
        // `+=`; a computed index; a call as the receiver evaluated once; a method returning an array stays a call; Me. / MyBase. / Shared / Module / inherited / property / nested receivers; the controls) on C#, C++,
        // JavaScript and MSIL, each through the CLI, the CLI with --optimize and CompileProjectFiles. Its JavaScript cells RUN under Node (TempExec.Run -> JavaScriptExecutionTests.RunNodeScript); its C++ and MSIL
        // cells skip without a compiler / ilasm. Named "...ExecutionTests", so the widened match below would catch it on its own; listed explicitly anyway. QualifiedElementReadEmissionTests (in process, no
        // Node, no [Category("Integration")]) is NOT here.
        typeof(QualifiedElementReadExecutionTests),
        // Task #203 — a bare field / Shared field / module global / ByRef operand is read BEFORE a later operand's call, as VB reads it: 13 vbc-answered groups (`K + Bump()`, `K * Bump()`, `If K < Bump()`, `K & Bump()`,
        // `K + Me.Bump()`, `F(K, Bump())`, a ParamArray, a Shared field, a module global bare and qualified, a ByRef local and parameter, a ByRef ARGUMENT left uncopied, an `If()` operand, a While / Do condition, a
        // constructor, a lambda body, a bare delegate field and AddressOf as the callee, a .NET static call, and the controls that were already right) on every backend each runs on, each through the CLI, the CLI with
        // --optimize and CompileProjectFiles. Its JavaScript cells RUN under Node (TempExec.Run -> JavaScriptExecutionTests.RunNodeScript); its C++ and MSIL cells skip without a compiler / ilasm. Named
        // "...ExecutionTests", so the widened match below would catch it on its own; listed explicitly anyway. OperandEvaluationOrderTextTests (in process, no Node, no [Category("Integration")]) is NOT here.
        typeof(OperandEvaluationOrderExecutionTests),
        // Task #207 — JavaScript bounds-checks List / array element access and Dictionary reads: 14 rows (a List read past the end typed / catch-all / at Count / negative / .Item / through a
        // Function and a Select Case; a List write that does not grow; an array read and write incl. a literal and a module array; a 2-D array on every level; a Dictionary missing key with the key in
        // the message; .NET's message text; `+=` / `++` out of range; a When guard; a field, a lambda, a qualified receiver, a constructor, a property and a Shared member; a List(Of Integer()) chain; a loop
        // ended by the exception; an UNCAUGHT read dying with AOORE; an unused read still throwing; the controls), JavaScript only, each through the CLI, the CLI with --optimize and CompileProjectFiles
        // (TempExec.AssertMatchesInEveryEntryPoint -> JavaScriptExecutionTests.RunNodeScript; B10 through TempExec.Emit + RunNodeScriptForOutcome). Named "...ExecutionTests", so the widened match below
        // would catch it on its own; listed explicitly anyway. JavaScriptBoundsCheckEmissionTests (JS text only, no process spawned, no [Category("Integration")]) is NOT here.
        typeof(JavaScriptBoundsCheckExecutionTests),
        // Task #208 — a `Shared Sub New` runs as the type initializer on first use: 12 vbc-answered groups (a Shared field and a Shared auto-property set in it and read first; an instance method after `New`; a Shared method called first;
        // print order on first use, a Shared store as a use, state changed by Main before the first use; two classes and one using the other; a class never touched; beside an instance Sub New; a field initializer before the
        // body; a derived class's before its base's; the controls that ran right before; `+=` / ByRef / an array element on a Shared field; a bare inherited Shared field; a body with locals, a loop and a Try) on every backend each
        // runs on, each through the CLI, the CLI with --optimize and CompileProjectFiles. Its JavaScript cells RUN under Node (TempExec.Run -> JavaScriptExecutionTests.RunNodeScript); its C++ and MSIL cells skip without a compiler /
        // ilasm. Named "...ExecutionTests", so the widened match below would catch it on its own; listed explicitly anyway. SharedConstructorDiagnosticsAndEmissionTests (in process, no Node, no [Category("Integration")]) is NOT here.
        typeof(SharedConstructorExecutionTests),
        // Task #210 — an auto-property initializer (`Public Property P As Integer = 7`, and its ReadOnly and Shared variants) parses and runs: 11 vbc-answered groups (an Integer / String / Double / Single / Long / Boolean
        // initializer; ReadOnly, and overwritten in the class's own constructor; Shared; a declared constructor's order; a base constructor reading an Overridable property; a synthesized constructor over an Optional base
        // and a MustInherit one; `Nothing`, widening and a `Const`; `MyBase.New(x + 1)`; and three #208 x #210 shapes: a Shared property beside a `Shared Sub New`, a class whose only constructor is a Shared Sub New, a Shared
        // method called first) on every backend each runs on, each through the CLI, the CLI with --optimize and CompileProjectFiles. Its JavaScript cells RUN under Node (TempExec.Run ->
        // JavaScriptExecutionTests.RunNodeScript); its C++ and MSIL cells skip without a compiler / ilasm. Named "...ExecutionTests", so the widened match below would catch it on its own; listed explicitly anyway.
        // AutoPropertyInitializerDiagnosticsTests (in process, no Node, no [Category("Integration")]) is NOT here.
        typeof(AutoPropertyInitializerExecutionTests),
        // Task #214 — an Object comparison under the optimizer (copy propagation keeps the Object operand, so the late-bound comparison survives): 11 vbc-answered cases on C# and MSIL through the CLI, the CLI with
        // --optimize, CompileProjectFiles and the in-process emitters, and two JavaScript rows through JavaScriptOptimizedExecutionTests.RunOptimized (the standard pipeline), which spawns Node: that is why it is here
        // and NOT in NotJavaScriptExecution. Its MSIL cells skip without ilasm. ObjectComparisonUnderOptimizerShapeTests (the emitted C# text, no process, no [Category("Integration")]) is NOT here.
        typeof(ObjectComparisonUnderOptimizerExecutionTests),
        // Task #215 — an Object comparison on JavaScript is VB's late-bound comparison (ADR-0012; JavaScriptBackend.IsLateBoundComparison / the `__blCompareObject` prelude helper): 13 vbc-answered programs (Nothing, String / Boolean /
        // Object against a number, what throws, Select Case, a comparison only in a When guard, the typed and `Is` controls), each through the CLI, the CLI with --optimize, CompileProjectFiles and the two in-process routes (RunJs,
        // RunOptimized), every one RUN under Node. Named "...ExecutionTests", so the widened match below would catch it on its own; listed explicitly anyway. JavaScriptLateBoundComparisonShapeTests (the emitted text, no process, no
        // [Category("Integration")]) is NOT here.
        typeof(JavaScriptLateBoundComparisonExecutionTests),
        // Tasks #219 / #249 — VB's implicit return variable (`F = v` in a Function, `P = v` in a Get; `Exit Function` / `Exit Property` return it): 11 vbc-answered programs (falling off the end, `Exit Function`, a Return that wins,
        // recursion beside the variable, `AddressOf F`, a class / Shared / Module Function, a Get, `Exit Property`, a lambda that reads and writes it, `For F = …`, a name in another case, the controls), each through the CLI,
        // the CLI with --optimize and CompileProjectFiles, on C#, C++, JavaScript and MSIL: the JavaScript cells RUN under Node (TempExec.Run -> JavaScriptExecutionTests.RunNodeScript). Named "...ExecutionTests", so the widened
        // match below would catch it on its own; listed explicitly anyway. ImplicitReturnVariableDiagnosticsTests (in process, no Node, no [Category("Integration")]) is NOT here.
        typeof(ImplicitReturnVariableExecutionTests),
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
        // Task #134: builds a Release C++ .blproj and compiles its obj/gen with clang++/g++ (CppCompile).
        "CppReleaseProjectExecutionTests",
        // Builds and runs the C# backend's output through the CLI and dotnet — no Node.
        "CSharpFieldAssignmentExecutionTests",
        "CSharpInlinedOperandExecutionTests",
        // Task #182: a reserved C# keyword used as a name (`@out`, `@lock`) — C# only, so it runs the emitted C# through CSharpProcessRunner and never Node.
        "CSharpKeywordIdentifierExecutionTests",
        // Task #191: an MSIL-only fixture. A class / interface / array reference through `&` and Console.Write, run through the CLI, --optimize and CompileProjectFiles, assembled with ilasm — no Node.
        "MsilObjectConcatAndWriteExecutionTests",
        // Task #194: a .NET class widens to its base class and interfaces. C# (and one MSIL row, the Exception base) through the CLI, --optimize and CompileProjectFiles — no Node.
        "NetSubtypeWideningExecutionTests",
        // Runs user operators on C# and C++ only — JavaScript refuses them (BL7006), which
        // UserOperatorTests asserts without spawning Node.
        "UserOperatorExecutionTests",
        // Task #196: a C++-only fixture. A C++ array is Nothing (unsized Dim, a field, a Function result) and an empty one is not, run through the CLI, --optimize and CompileProjectFiles with a C++ compiler - no Node.
        "CppArrayNothingExecutionTests",
        // Task #201: a C++-only fixture. AddressOf on the by-copy FALLBACK path (an instance / Shared / bare method, a Return on both arms, a Select arm, an Iterator root) through the CLI, --optimize and CompileProjectFiles with a C++ compiler - no Node.
        "CppAddressOfFallbackExecutionTests",
        // Task #209: a property passed ByRef is copied in and written back, on C#, C++ and MSIL through the CLI, --optimize and CompileProjectFiles. JavaScript refuses a ByRef parameter by design (BL7002), so its one row only COMPILES (CLI and generator) and asserts the refusal - it never runs Node, and the roster's count stays as it was.
        "PropertyByRefCopyOutExecutionTests",
        // Task #211: an Object comparison on C# is VB's late-bound comparison (ADR-0012), run against vbc on C# only, through the CLI, --optimize and CompileProjectFiles, every run in a child process (CSharpProcessRunner) - no Node, and the roster's count stays as it was.
        "CSharpLateBoundComparisonExecutionTests",
        // Task #212: a VB conversion intrinsic of an Object operand (CInt, CBool, CByte, ...) is VB's Conversions.ToXxx(object), run against vbc on C# and MSIL through the CLI, --optimize and CompileProjectFiles, every C# run in a child process (CSharpProcessRunner) - no Node, and the roster's count stays as it was. JavaScript's Object conversions disagree with VB and are a known gap.
        "ObjectConversionIntrinsicExecutionTests",
        // Task #216: an Optional parameter typed Object with a non-Nothing default (`= 5`, `= "x"`, `= True`, `= 2.5`, `= "a"c`, `= 5000000000L`) is VB's [Optional, DefaultParameterValue] encoding on C#, run against vbc through the CLI, --optimize and CompileProjectFiles, every run in a child process (CSharpProcessRunner) - no Node, and the roster's count stays as it was. JavaScript and MSIL already ran these programs.
        "OptionalObjectDefaultExecutionTests",
        // Tasks #218 / #254: a bare store to a plain auto-property (`P = P + 10`, `P += 20`, `Count += 1`, `S = S * 10`, `P++`) lands on C++ (CppCodeGenerator.IsStorageAutoProperty), run against vbc on C++ with C# as the reference, through the CLI, --optimize and CompileProjectFiles, every C# run in a child process (CSharpProcessRunner) - no Node, and the roster's count stays as it was. JavaScript, MSIL and C# already answered these programs.
        "CppAutoPropertyBareStoreExecutionTests",
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
        => Assert.That(ExecutionTier, Has.Length.EqualTo(128), // + ImplicitReturnVariableExecutionTests (tasks #219 / #249: VB's implicit return variable, `F = v` in a Function and `P = v` in a Get, runs under Node on four backends); 127 = JavaScriptLateBoundComparisonExecutionTests (task #215: an Object comparison on JavaScript is VB's late-bound comparison, `__blCompareObject`; 13 vbc-answered programs run under Node through the CLI, --optimize, CompileProjectFiles, RunJs and RunOptimized); 126 = ObjectComparisonUnderOptimizerExecutionTests (task #214: an Object comparison keeps its late-bound form under the optimizer; two JavaScript rows run under Node via RunOptimized); 125 = AutoPropertyInitializerExecutionTests (task #210, an auto-property initializer `Property P As Integer = 7` parses and runs, instance and Shared: after the base call through the property, in a synthesized constructor when the class declares none, in the Shared type initializer before a `Shared Sub New` body); + SharedConstructorExecutionTests (task #208, a `Shared Sub New` runs as the type initializer on first use: on JavaScript lazily through `$typeInit`, on C#, C++ and MSIL natively); + JavaScriptBoundsCheckExecutionTests (task #207, JavaScript bounds-checks List / array element access and Dictionary reads); + OperandEvaluationOrderExecutionTests (task #203, a bare field / global operand is read before a later operand's call); + QualifiedElementReadExecutionTests (task #204, a paren element read through a qualified receiver, `b.Items(0)`, lowers like the bare `Items(0)` on four backends); + ExpressionStatementVbRefusalExecutionTests (task #267, the expression statements VB refuses are refused and the ones it accepts still run); + UserDelegateGapsExecutionTests (task #202, a file-level Public/Friend Delegate, a sibling file's Delegate in both compile orders and `.Invoke` on a Func/Action, run on four backends); + LambdaParameterHidesExecutionTests (task #217, a lambda parameter named like a field or a module global still runs); + StringNothingEqualityExecutionTests (task #206, a String `=` / `<>` reads Nothing as "" the VB way; fixes #205); + AmpersandConcatExecutionTests (task #190, `&` converts both operands to String); + NothingIntoValueTypeExecutionTests (task #186, Nothing into a value type is its default the VB way); + CharWidensToStringExecutionTests (task #184, a Char widens to a String the VB way); + VbConversionIntrinsicExecutionTests (task #181, AscW/Asc/ChrW/Chr and CByte..CULng typed and lowered); + PrivateMemberDeclaredBelowExecutionTests (task #152, a Private member declared below its bare use); + ConstructorByRefExecutionTests (task #144, ByRef constructor parameters write back; JS copy-in for values); + UserExceptionSubclassExecutionTests (task #151, Message on a user Exception subclass on JS and MSIL); + OverridableAutoPropertyExecutionTests (task #150, an Overridable auto-property's override dispatches on JavaScript); + JavaScriptIteratorExecutionTests (task #145, a re-iterable JS Iterator Function); + MyBaseCallArgumentsExecutionTests (tasks #142/#265/#213, a MyBase call's ByRef/Optional/ParamArray/parameter types); + IncrementDecrementExecutionTests (task #141, `++`/`--` write their operand); + FormEventWebRunTests (property-grid slice 5); + MyBaseMethodCallStatementExecutionTests (task #139, a statement-level MyBase call on C#); + LambdaBodyEmissionExecutionTests (task #136, a lambda body on C#); + LoopConditionReevaluationExecutionTests (task #256, the loop condition re-evaluation); + MixedNumericCompareFoldExecutionTests (task #123, the compare fold); + UntypedConstAndConditionalExecutionTests (task #123, D1/D2); + TempMintingFacilityTests + NameReservationExecutionTests (task #121, ADR-0018); + CompilerTempExecutionTests + CompilerTempCollisionFenceTests (task #163, ADR-0017); + BaseConstructorCallLoweringExecutionTests + BaseConstructorCallCppRefusalTests (task #170, ADR-0016), MsilValueToStringExecutionTests (task #183), InterfaceMethodTypingExecutionTests, IsIsNotOperatorExecutionTests (task #185), MeReceiverTypingExecutionTests (task #176), UserDelegateConversionExecutionTests (task #187), DelegateMemberInvocationExecutionTests (task #188), NothingStringTextExecutionTests (task #189), MsilObjectBoxingExecutionTests (task #177), NotPrecedenceExecutionTests (#195), PropertyAccessExecutionTests (task #178), NameBindingExecutionTests (task #169/#199), PerIterationLoopBodyDimExecutionTests + PerIterationLoopBodyDimOptimizerExecutionTests (task #172, ADR-0014), LambdaBoundaryDiagnosticsExecutionTests (task #174); + WhenGuardCallRunTests, TypeOfExecutionTests (#197), ForwardDeclaredTypeExecutionTests; + NameBindingResolutionExecutionTests (task #124, ADR-0013 D3)
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
