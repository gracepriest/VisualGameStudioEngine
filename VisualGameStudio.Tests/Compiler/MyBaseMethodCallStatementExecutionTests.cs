using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using VisualGameStudio.Tests.Msil;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #139 — on C#, a statement-level `MyBase.M(...)` call is WRITTEN. RUN, against vbc, through every entry point.
//
//  ⛔ THE BUG. IRBaseMethodCall is an IR value, and CSharpBackend.ShouldEmitInstruction had an arm for IRCall and one for IRInstanceMethodCall but none for it, so it fell to the
//  catch-all for values: "write it only if it is named after a declared variable". A base call never is. So `MyBase.Show(n + 1)` as a statement — a Sub, or a Function whose
//  result is discarded — was never written: the override ran without it and printed nothing, and the field it should have bumped stayed 0 (measured: a Sub, a discarded
//  Function (also inside the override of that Function), no arguments; a constructor after MyBase.New, a property Get and Set, a Sub lambda and a multi-line Function lambda, an
//  If/Else, a For, While and Do loop, a For Each, Select Case, Try/Catch/Finally; a grandparent and a three-level chain; a generic derived class and a generic method; an Object
//  parameter, an Optional argument left out, a ParamArray). A base call whose result is USED was inlined into its use and ran, which is why only the statement form was wrong.
//  C++, JavaScript and MSIL wrote the call. The fix (CSharpBackend only, no IR change) gives IRBaseMethodCall IRCall's rule — a statement when void or unused, inlined at its
//  one use otherwise — and Visit(IRBaseMethodCall) IRCall's statement forms (a bare `base.M(args);`, no `T tN = ...`).
//
//  ⭐ THE ORACLE IS vbc, not a backend. Every row of `MyBaseCallProbes` is a program taken verbatim from the implementer's probes (S/t139/probes/m) or written for this fixture
//  (S/t139/tw/x), and its expected text is what the SDK's vbc prints for it (the program wrapped in a VB Module). A row's `Agrees` is the set of backends whose three entry points
//  print that text on this build: C# on all 33; C++, JavaScript and MSIL where the matrix says they do (their cells did NOT change with #139 — they are CONTROLS: they are what says
//  the expected text is the program's own answer and not C#'s). ⭐ EVERY ROW COUNTS: the base method prints or bumps a counter, so a call that is DROPPED (the bug) and a call that is
//  DOUBLED (a mutant that writes a used result as a statement AND inlines it, M3) both change the text. The 9 value rows (`v1`..`v8`) are the ones that were right before the fix.
//
//  ⭐ ENTRY POINTS (CLAUDE.md: "test both entry points", "validate codegen through the CLI and the IR optimizer"): the real BasicLang CLI (standard passes), the real CLI with
//  `--optimize` (aggressive) and `BasicCompiler.CompileProjectFiles` with `OptimizeAggressive` (what a Release .blproj build and the IDE call) — plus `BasicLang build App.blproj -c
//  Release` for three programs. The FAST half is MyBaseMethodCallStatementShapeTests, which reads the text.
//
//  ⛔⛔ EVERY C# RUN HERE IS HANG-SAFE (`hangSafe: true` -> CSharpProcessRunner): some rows hold loops, and a C# loop that never ends freezes the whole test host if it runs in process.
//
//  ⛔ CELLS WITH NO EXPECTATION — each is a defect that is NOT #139's, measured on the fixed build and on the one before it. Asserting one would pin the defect:
//
//    C#   #265 the ByRef rows (`p1_byref`, `p1b_byrefinh`, and KillVocabularyExtensions B2): IRBaseMethodCall carries no ByRef flags, so the call has no `ref` and C# refuses CS1620.
//         It printed the unchanged variable before #139 wrote the call at all. PINNED as a refusal (here and in the shape fixture), so it flips when #265 lands.
//    MSIL #142/#265 the same ByRef rows: MissingMethodException — the call names `SetIt(int32)` for a method declared ByRef. Pinned here (`…ThrowsAtRunTimeOnMsil…`). ⚠ It is NOT specific to an
//         INHERITED method as #142's title says: `p1_byref` (a method the base declares Overridable and Derived overrides) throws the same.
//    JS   the ByRef rows: BL7002, a refusal by design (JavaScript has no reference parameters); KillVocabularyExtensions.B2_JavaScript_RefusesByRef_BL7002 pins it.
//    C++  `p2_object` (`'Object' has no C++ mapping`: the parameter itself, #213's neighbour), `p3_optional` (clang: too few arguments), `p4_paramarray` (clang: too many arguments),
//         `s2d_tostring` (clang: `override` on ToString).
//    JS   `p3_optional` (prints `base 1,undefined`: the omitted Optional is not defaulted), `p4_paramarray` (TypeError: `xs is not iterable`: three loose arguments are not packed).
//    MSIL `p2_object` (#213: names `Show(int32)` for `Show(object)`; MsilObjectBoxingExecutionTests pins it), `p3_optional`, `p4_paramarray` (MissingMethodException), and every row with a
//         base call inside a LAMBDA (`c3_lambda`, `c3b_lamvalue`, `n1_tempname`: "has no IL lowering" — a refusal by design), and the generic rows `c9b_genderived`, `c9c_genmethod`
//         ("Reference to undefined class 'T'").
//         ⚠ `p2_object`, `p3_optional`, `p4_paramarray` share a root with the ByRef rows: IRBaseMethodCall carries none of the declared method's parameter facts (ByRef, Optional defaults,
//         ParamArray packing, parameter types), so a backend that needs them has nothing to read. That root is #265; ParamArray was found by this fixture.
//    all  `Inherits Base(Of T)` (a generic BASE class: `c9_generic`, measured 2026-10-04): a parse error on every backend before and after; there is no row. The generic DERIVED class and the
//         generic METHOD of a base are rows.
//
//  ⚠ Side findings of the same measurement, NOT #139's and with no row (S/t139/probes/fu): a counted `For i = 1 To F()` re-evaluates its call bound each iteration on all four backends, and a
//  compound assignment `a(F()) += 7` evaluates the call in the index twice (C++ 2x, JavaScript 3x) — #266. A class method's `Dim a(5)` is unallocated on C# and MSIL (named in
//  LambdaBodyEmissionExecutionTests' header). Other statement-level expressions C# drops (`New C()`, `l(Tag())`, `CType(Tag(), Object)`, a property Get) are all REJECTED by vbc; see HANDOFF.
//
//  ⭐ MUTANTS (S/t139/mut and S/t139/tw/mut: each is the fix plus ONE change, built in a detached worktree and run against a copy of the test output with its BasicLang.dll swapped; the number is how
//  many of the 33 `…_CSharp` cells of THIS fixture fail; the FAST shape fixture kills every one of them but M4 and M6 and is the cheap kill). M1 the arm removed 23 (every row with a statement-level
//  call; the 10 value rows are right without it); M2 the arm answers only for a Sub 5 (`s2_func`, `s2b_funcself`, `s2c_types`, `s2d_tostring`, `n1_tempname`: the counter stays 0);
//  M3 every base call a statement while the visit writes it 10 (`v1`..`v8`, `v6b`, `c3b`: a used result runs TWICE, `calls=2` where vbc prints 1); M8 off by one, a result used once is a statement
//  too 10 (the same ten); M9 `IsExpressionLambda` does not ask about a base call 2 (`c3_lambda`, `n1_tempname`: the Function lambda is `() => K`, its call gone). NOT killed here, text only:
//  M3a (a used result's expression lambda becomes a block lambda: the same answer), M5 (the old visit declares an unused `T tN = base.F(...)`: the same answer), M10 (the arm before ADR-0001's
//  materialised check: no source shape gives a base call two uses). NOT killable at all: M4, M6 (the named-destination disjunct: IsNamedDestination is false for every base call).
//
//  ⚠ Named "…ExecutionTests" on purpose: its controls RUN under Node, so it is in JsExecutionTierRosterTests' roster.
// ================================================================================================

/// <summary>
/// #139 RUN: a statement-level <c>MyBase.M(...)</c> call — a Sub, a Function whose result is discarded, in every context — is written on C# and prints vbc's answer, through every
/// entry point, and so do the used-result forms that were right before; on every backend that prints it, the same text.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the JavaScript legs and the C++ compiles share the machine with the spawned runners
public class MyBaseMethodCallStatementExecutionTests
{
    private static IEnumerable<TestCaseData> CSharpCells()
        => MyBaseCallProbes.All.Select(p => new TestCaseData(p).SetName($"{p.Id}_CSharp"));

    private static IEnumerable<TestCaseData> ControlCells()
        => MyBaseCallProbes.All.SelectMany(p => TempExec.Backends(p.Agrees & ~Bk.CSharp).Select(b => new TestCaseData(p, b).SetName($"{p.Id}_{b}")));

    private static IEnumerable<TestCaseData> ByRefCells()
        => MyBaseCallProbes.ByRef.Select(p => new TestCaseData(p).SetName(p.Id));

    private static IEnumerable<TestCaseData> BuildCommandCells()
        => new[] { "s1_sub", "s2_func", "c3_lambda" }.Select(id => new TestCaseData(MyBaseCallProbes.All.Single(p => p.Id == id)).SetName($"{id}_BuildRelease"));

    private static string Ids(IEnumerable<TempProbe> probes) => string.Join(",", probes.Select(p => p.Id));

    /// <summary>
    /// The table IS the proof, so its shape is pinned: a row cannot vanish, and a backend cannot be dropped from one, without this test saying so. It is also the fixture's
    /// one plain [Test] besides the [TestCase]s: <c>JsExecutionTierRosterTests</c> counts attributes, and a fixture whose tests are all [TestCaseSource] counts as empty.
    /// </summary>
    [Test]
    public void TheTable_HasItsRows()
    {
        var all = MyBaseCallProbes.All;

        Assert.Multiple(() =>
        {
            Assert.That(all, Has.Count.EqualTo(33));
            Assert.That(all.Select(p => p.Id).Distinct().Count(), Is.EqualTo(all.Count), "no id twice");
            Assert.That(all.Concat(MyBaseCallProbes.ByRef).Where(p => !p.HangSafe).Select(p => p.Id), Is.Empty, "every C# run of a base call is hang-safe");
            Assert.That(all.Where(p => !p.Agrees.HasFlag(Bk.CSharp)).Select(p => p.Id), Is.Empty, "C# has a cell on every row: it is what #139 changed");
            Assert.That(MyBaseCallProbes.ByRef.Select(p => p.Id), Is.EqualTo(new[] { "p1_byref", "p1b_byrefinh" }));
            Assert.That(MyBaseCallProbes.ByRef.Select(p => p.Agrees), Is.All.EqualTo(Bk.Cpp), "the ByRef rows: C++ is the control; C# and MSIL are pinned refusals");

            // the cells that have no control on a backend, by backend — each is an unrelated defect, #213/#265's root, or a refusal by design
            Assert.That(Ids(all.Where(p => !p.Agrees.HasFlag(Bk.Cpp))), Is.EqualTo("s2d_tostring,p2_object,p3_optional,p4_paramarray"),
                "C++ cells with no expectation: `override` on ToString (clang); an Object parameter (no mapping); an Optional left out; a ParamArray");
            Assert.That(Ids(all.Where(p => !p.Agrees.HasFlag(Bk.JavaScript))), Is.EqualTo("p3_optional,p4_paramarray"),
                "JavaScript cells with no expectation: an Optional left out prints `undefined`; a ParamArray is not packed");
            Assert.That(Ids(all.Where(p => !p.Agrees.HasFlag(Bk.Msil))), Is.EqualTo("c3_lambda,c3b_lamvalue,c9b_genderived,c9c_genmethod,n1_tempname,p2_object,p3_optional,p4_paramarray"),
                "MSIL cells with no expectation: a MyBase call in a lambda (a refusal by design); a generic class or method (undefined class 'T'); #213; #265's root");

            // the cell counts
            Assert.That(CSharpCells().Count(), Is.EqualTo(33), "one C# cell per row, each through three entry points");
            Assert.That(ControlCells().Count(), Is.EqualTo(33 * 3 - 4 - 2 - 8), "33 rows x C++, JavaScript, MSIL, less the 14 cells above");
            Assert.That(ByRefCells().Count(), Is.EqualTo(2));
            Assert.That(BuildCommandCells().Count(), Is.EqualTo(3));

            // every shape class of the brief has a row
            foreach (var id in new[]
            {
                "s1_sub", "s2_func", "s2b_funcself", "s3_noargs",                                    // a Sub; a Function whose result is discarded (and inside that Function's own override); no arguments
                "c1_ctor", "c2_prop", "c3_lambda", "c3b_lamvalue",                                   // a constructor after MyBase.New; a property Get and Set; a Sub lambda and a Function lambda
                "c4_if", "c5_loop", "c5b_dowhile", "c5c_foreach", "c6_select", "c7_try",             // If/Else, For/While, Do, For Each, Select Case, Try/Catch/Finally
                "c8_twolevel", "c8b_chain", "c9b_genderived", "c9c_genmethod",                       // a grandparent and a three-level chain; a generic derived class and a generic method
                "p2_object", "p3_optional", "p4_paramarray", "n1_tempname",                          // an Object parameter (#213's shape), Optional, ParamArray; user locals spelled like temps
                "v1_dim", "v2_local", "v3_field", "v4_param", "v5_twice", "v6_select", "v8_ifcond", "v7_inline", // the value forms: Dim, local, field, parameter, two calls, selector, If condition + Return, nested
            })
                Assert.That(all.Select(p => p.Id), Does.Contain(id), $"no row {id}");
        });
    }

    // ============================================================================================
    // C# — vbc's answer, through the CLI, the CLI with --optimize and CompileProjectFiles.
    // ============================================================================================

    /// <summary>
    /// A statement-level base call is written, once, where it is: a Sub, a Function whose result is discarded, in a constructor, a property accessor, a lambda, a branch, a loop, a Select
    /// arm, a Try, a Catch and a Finally, through a grandparent and a chain, from a generic class — and a used result is still inlined exactly once. Each row prints what vbc prints, in all
    /// three entry points. A regression that drops the call changes the text (the counter, or the line the base method prints); one that writes a used result twice changes the counter.
    /// </summary>
    [TestCaseSource(nameof(CSharpCells))]
    public void ABaseMethodCall_PrintsVbcsAnswer_OnCSharp_InEveryEntryPoint(TempProbe probe)
        => TempExec.AssertMatchesInEveryEntryPoint(Bk.CSharp, probe.Source, probe.Vb, probe.Id, hangSafe: true);

    // ============================================================================================
    // THE CONTROLS — C++, JavaScript and MSIL, which #139 did not change.
    // ============================================================================================

    /// <summary>
    /// The same programs on the other three backends (where the matrix says they print vbc's answer): the text above is the PROGRAM's answer, not C#'s. C++ and MSIL through their own
    /// base-call lowering, JavaScript under Node.
    /// </summary>
    [TestCaseSource(nameof(ControlCells))]
    public void TheSameProgram_PrintsVbcsAnswer_OnTheOtherBackends(TempProbe probe, Bk backend)
        => TempExec.AssertMatchesInEveryEntryPoint(backend, probe.Source, probe.Vb, probe.Id);

    // ============================================================================================
    // THE KNOWN GAP (#265) — a ByRef argument to a base method
    // ============================================================================================

    /// <summary>
    /// ⛔ C# refuses it: the compiler writes `base.SetIt(p);` with no `ref` (IRBaseMethodCall carries no ByRef flags), and the C# compiler says CS1620. Through the CLI, the CLI with
    /// `--optimize` and CompileProjectFiles (the shape fixture pins the in-process pipelines). Before #139 the call was not written, and the program printed the unchanged variable
    /// (`5` for vbc's `105`): a silent wrong answer has become a refusal. Pinned so that it flips when #265 lands — update the pin, do not delete it.
    /// </summary>
    [TestCaseSource(nameof(ByRefCells))]
    public void AByRefArgumentToABaseMethod_IsRefusedByCSharp_KnownGap_Task265(TempProbe probe)
    {
        var failures = new List<string>();
        foreach (var entry in Enum.GetValues<EntryPoint>())
        {
            var errors = MyBaseMethodCallStatementShapeTests.RoslynErrors(TempExec.Emit(Bk.CSharp, entry, probe.Source));
            if (errors.Length != 1 || !errors[0].Contains("error CS1620", StringComparison.Ordinal))
                failures.Add($"{entry}: [{string.Join(" | ", errors)}] where CS1620 is the one refusal (#265)");
        }

        Assert.That(failures, Is.Empty, $"{probe.Id} on CSharp:\n" + string.Join("\n", failures));
    }

    /// <summary>C++ is the control for the ByRef rows: it passes the variable by reference and prints vbc's answer (`105`) — what #265 must give C# and MSIL.</summary>
    [TestCaseSource(nameof(ByRefCells))]
    public void AByRefArgumentToABaseMethod_PrintsVbcsAnswer_OnCpp(TempProbe probe)
        => TempExec.AssertMatchesInEveryEntryPoint(Bk.Cpp, probe.Source, probe.Vb, probe.Id);

    /// <summary>
    /// ⛔ MSIL assembles the program and the CLR throws MissingMethodException: the call site names `Base.SetIt(int32)` where the method is declared with a ByRef parameter (`int32&amp;`).
    /// (#142's title says "an INHERITED method"; `p1_byref` calls an Overridable one and fails the same way.) Pinned like #213's: a different outcome means #265 / #142 moved —
    /// update the pin, do not delete it.
    /// </summary>
    [TestCaseSource(nameof(ByRefCells))]
    public void AByRefArgumentToABaseMethod_ThrowsAtRunTimeOnMsil_KnownGap(TempProbe probe)
    {
        var run = MsilHarness.Run(probe.Source);
        Assert.That(run.Outcome, Is.EqualTo(MsilHarness.MsilOutcome.RunFailed), run.Report);
        Assert.That(run.Output, Does.Contain("MissingMethodException"), "the specific exception this shape throws today.\n" + run.Report);
    }

    // ============================================================================================
    // THE TEXT — the spawned CLI writes the base calls the library writes.
    // ============================================================================================

    /// <summary>
    /// The shape tests read the text the library produces in process; the CLI is a separate process with its own entry (<c>BasicCompiler.CompileFile</c>, module registry, preprocessing).
    /// The base calls it writes — standard and with <c>--optimize</c> — are the lines <c>CompileProjectFiles</c> writes, in order, `#line`s left out: a fix verified only through the
    /// in-process helper can still break via the CLI.
    /// </summary>
    [TestCase("s1_sub", TestName = "TheCli_WritesTheBaseCalls_ASub")]
    [TestCase("s2_func", TestName = "TheCli_WritesTheBaseCalls_ADiscardedFunctionResult")]
    [TestCase("c3_lambda", TestName = "TheCli_WritesTheBaseCalls_InBlockLambdas")]
    [TestCase("v5_twice", TestName = "TheCli_WritesTheBaseCalls_TwoUsedResultsInOneExpression")]
    [TestCase("n1_tempname", TestName = "TheCli_WritesTheBaseCalls_BesideUserLocalsNamedLikeTemps")]
    public void TheCli_WritesTheBaseCalls_TheProjectEntryPointWrites(string id)
    {
        var source = MyBaseCallProbes.All.Single(p => p.Id == id).Source;

        var project = MyBaseMethodCallStatementShapeTests.BaseLines(TempExec.Emit(Bk.CSharp, EntryPoint.ProjectRelease, source));
        Assert.That(project, Is.Not.Empty);
        Assert.Multiple(() =>
        {
            Assert.That(MyBaseMethodCallStatementShapeTests.BaseLines(TempExec.Emit(Bk.CSharp, EntryPoint.Cli, source)), Is.EqualTo(project), "BasicLang Prog.bas --target=csharp");
            Assert.That(MyBaseMethodCallStatementShapeTests.BaseLines(TempExec.Emit(Bk.CSharp, EntryPoint.CliOptimize, source)), Is.EqualTo(project), "BasicLang Prog.bas --target=csharp --optimize");
        });
    }

    // ============================================================================================
    // THE REAL BUILD — `BasicLang build App.blproj -c Release`, the route the IDE's build service takes.
    // ============================================================================================

    /// <summary>
    /// The project build writes the exe and the test RUNS it with a 20 s limit, killing the tree: a hang is a failure. `s1_sub` (a Sub), `s2_func` (a Function whose result is
    /// discarded) and `c3_lambda` (a base call in block lambdas).
    /// </summary>
    [TestCaseSource(nameof(BuildCommandCells))]
    public void TheReleaseProjectBuild_PrintsVbcsAnswer(TempProbe probe)
    {
        if (!CliTestHarness.DotnetOnPath()) Assert.Ignore("dotnet not found on PATH.");

        var dir = Path.Combine(Path.GetTempPath(), "bl-t139-build-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Main.bas"), probe.Source);
            File.WriteAllText(Path.Combine(dir, "App.blproj"), """
                <?xml version="1.0" encoding="utf-8"?>
                <BasicLangProject Version="1.0">
                  <PropertyGroup>
                    <ProjectName>App</ProjectName>
                    <OutputType>Exe</OutputType>
                    <TargetBackend>CSharp</TargetBackend>
                  </PropertyGroup>
                  <ItemGroup>
                    <Compile Include="Main.bas" />
                  </ItemGroup>
                </BasicLangProject>
                """);

            var (buildExit, buildOut, buildErr) = CliTestHarness.RunProcess(
                CliTestHarness.CliPath(), new[] { "build", Path.Combine(dir, "App.blproj"), "-c", "Release" }, dir, timeoutMs: 180_000);
            Assert.That(buildExit, Is.EqualTo(0), $"BasicLang build -c Release failed:\n{buildOut}\n{buildErr}");

            var exes = Directory.GetFiles(dir, CliTestHarness.AppHostName("App"), SearchOption.AllDirectories);
            Assert.That(exes, Is.Not.Empty, $"the build produced no {CliTestHarness.AppHostName("App")}:\n{buildOut}");

            // RunProcess kills the process tree and FAILS the test when the program is still running at the limit.
            var (runExit, runOut, runErr) = CliTestHarness.RunProcess(exes[0], Array.Empty<string>(), Path.GetDirectoryName(exes[0])!, timeoutMs: CSharpProcessRunner.DefaultTimeoutMs);

            Assert.Multiple(() =>
            {
                Assert.That(runExit, Is.EqualTo(0), $"the built program exited {runExit}:\n{runErr}");
                Assert.That(TempExec.Norm(runOut), Is.EqualTo(TempExec.Norm(probe.Vb)));
            });
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* temp */ }
        }
    }
}
