using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #136 — on C#, a lambda body is written by the SAME emitter as a function body. RUN, against vbc, through every entry point.
//
//  ⛔ THE BUG. The C# backend wrote a block lambda with its own loop over the lambda's ENTRY BLOCK only, and that loop had one rule for a
//  statement: "an IR value that is not a call is a temp — skip it — unless it is named after a variable the ENCLOSING function declares".
//  So a write to the lambda's own parameter was skipped; a Function lambda that wrote before its Return became an EXPRESSION lambda
//  (`() => 0`) and lost the write; every block after the entry block (an If, a loop, a Select Case, a Try) was never written
//  (`() => { ; }`, CS1643, or a Sub lambda that silently did nothing); a lambda's own Dim was never declared (CS0103, #165) and, spelled
//  like a field or a global, read and wrote that instead; a call whose result was used was written twice (#179); a method call through
//  Me or an object was skipped as a temp (#237, a NullReferenceException); a ByRef call lost its `ref` (#166's statement form); a lambda in
//  a module global's initialiser dropped its writes to globals. C++, JavaScript and MSIL ran all of it. The fix (CSharpBackend,
//  `EnterLambdaScope`/`ExitLambdaScope`/`GenerateLambdaBlockBody`) makes the lambda the function being emitted.
//
//  ⭐ THE ORACLE IS vbc, not a backend. Every row of `LambdaBodyProbes` is a program taken verbatim from the implementer's probes
//  (S/t136/probes/m, m2, m3, m4) or written for this fixture, and its expected text is what the SDK's vbc prints for it (the program wrapped
//  in a VB Module). A row's `Agrees` is the set of backends whose three entry points print that text on this build: C# on all 74; C++,
//  JavaScript and MSIL where the matrix says they do (their cells did NOT change with #136 — they are CONTROLS: they are what says the
//  expected text is the program's own answer and not C#'s).
//
//  ⭐ ENTRY POINTS (CLAUDE.md: "test both entry points", "validate codegen through the CLI and the IR optimizer"): the real BasicLang CLI (standard
//  passes), the real CLI with `--optimize` (aggressive) and `BasicCompiler.CompileProjectFiles` with `OptimizeAggressive` (what a Release .blproj
//  build and the IDE call) — plus `BasicLang build App.blproj -c Release` for three programs.
//
//  ⛔⛔ EVERY C# RUN HERE IS HANG-SAFE (`TempProbe.HangSafe` -> CSharpProcessRunner): a lambda body holds loops, and a C# loop that never ends
//  freezes the whole test host if it runs in process (the in-process harness has no timeout). The fast shape tests are
//  `LambdaBodyEmissionShapeTests`.
//
//  ⛔ CELLS WITH NO EXPECTATION — each is a defect that is NOT #136's, measured on the fixed build and on the one before it. Asserting one would
//  pin the defect. They are not rows here:
//
//    C#  #227   E07w                          a bottom-tested loop (`Do … Loop While/Until`) over a nested loop never ends.
//    C#  #228   E15, E15n                     a sized array `Dim a(2)` in a loop body is allocated once, at function top.
//    C#/JS #229 E16, E20                      a name with two `Dim`s in one function stays function-level. E20 on C# prints 50|2|2 now (JavaScript's
//                                             answer, vbc's is 50|1|2): `h()` is right since #136, the loop's `y` is #229. It is pinned in
//                                             PerIterationLoopBodyDimExecutionTests.E20_…_PinnedForTask229.
//    C#  #232   R3, and m3/h166_inline        a ByRef call INSIDE AN EXPRESSION loses `ref` (CS1620): `Console.WriteLine(Twice(a))`, or
//                                             `Dim r = Bump(n) + 1`. ⚠ It is NOT lambda-specific and #232's title ("a ByRef parameter plus a lambda")
//                                             names the wrong shape: the same program with NO lambda anywhere fails identically on 0f6d2ced and now
//                                             (measured: `Function Twice(ByRef n) … Return copy * 2`, `Console.WriteLine(Twice(a))`), while
//                                             `Dim r = Twice(a)` (the call is the whole right side of a Dim, with the same lambda in the callee) runs. The statement form
//                                             inside a lambda is #166's and IS a row here (h166_byref*).
//    C#  #182   j_foreach                     a local named `out` is not escaped: `string out = ""` is CS1001. j_foreach2 is the same program with the
//                                             variable renamed and IS a row.
//    all       e_meth, k_generic              BL-FAIL in the front end (`Private items As New List(Of Integer)` as a field; a generic Function called `Twice(Of Integer)(…)`) on every
//                                             backend before and after. e_meth2 is e_meth with the field initialised in the constructor and IS a row.
//    C#        a class method's `Dim a(3)`    `int[] a = default!`, then a NullReferenceException: sized arrays in METHODS are allocated nowhere
//                                             (`DeclareLocals(sizedArrays: false)`); a LAMBDA's is (j_lambdaarr).
//
//  ⚠ Named "…ExecutionTests" on purpose: its controls RUN under Node, so it is in JsExecutionTierRosterTests' roster.
// ================================================================================================

/// <summary>
/// #136 RUN: a lambda body — its writes, its Dims, its calls, its loops, its Try — prints vbc's answer on C#, and on every backend that
/// prints it, through every entry point.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the JavaScript legs and the C++ compiles share the machine with the spawned runners
public class LambdaBodyEmissionExecutionTests
{
    private static IEnumerable<TestCaseData> CSharpCells()
        => LambdaBodyProbes.All.Select(p => new TestCaseData(p).SetName($"{p.Id}_CSharp"));

    private static IEnumerable<TestCaseData> ControlCells()
        => LambdaBodyProbes.All.SelectMany(p => TempExec.Backends(p.Agrees & ~Bk.CSharp).Select(b => new TestCaseData(p, b).SetName($"{p.Id}_{b}")));

    private static IEnumerable<TestCaseData> BuildCommandCells()
        => new[] { "c_asg", "k_global", "e_mybase" }.Select(id => new TestCaseData(LambdaBodyProbes.All.Single(p => p.Id == id)).SetName($"{id}_BuildRelease"));

    private static string Ids(IEnumerable<TempProbe> probes) => string.Join(",", probes.Select(p => p.Id));

    /// <summary>
    /// The table IS the proof, so its shape is pinned: a row cannot vanish, and a backend cannot be dropped from one, without this test saying so.
    /// It is also the fixture's one plain [Test]: <c>JsExecutionTierRosterTests</c> counts attributes, and a fixture whose tests are all
    /// [TestCaseSource] counts as empty.
    /// </summary>
    [Test]
    public void TheTable_HasItsRows()
    {
        var all = LambdaBodyProbes.All;

        Assert.Multiple(() =>
        {
            Assert.That(all, Has.Count.EqualTo(74));
            Assert.That(all.Select(p => p.Id).Distinct().Count(), Is.EqualTo(all.Count), "no id twice");
            Assert.That(all.Where(p => !p.HangSafe).Select(p => p.Id), Is.Empty, "every C# run of a lambda is hang-safe");
            Assert.That(all.Where(p => !p.Agrees.HasFlag(Bk.CSharp)).Select(p => p.Id), Is.Empty, "C# has a cell on every row: it is what #136 changed");

            // the cells that have no control on a backend, by backend — each is an unrelated defect or a refusal by design
            Assert.That(Ids(all.Where(p => !p.Agrees.HasFlag(Bk.Cpp))), Is.EqualTo("k_global,k_global2,h166_byref"),
                "C++ cells with no expectation: a non-constant module initialiser; a ByRef capture the lowering refuses");
            Assert.That(Ids(all.Where(p => !p.Agrees.HasFlag(Bk.JavaScript))), Is.EqualTo("k_global,k_global2,h166_byref,h166_byref_loc,h166_byref_par"),
                "JavaScript cells with no expectation: a non-constant module initialiser (not built); a ByRef parameter (BL7002, by design)");
            Assert.That(Ids(all.Where(p => !p.Agrees.HasFlag(Bk.Msil))), Is.EqualTo("j_lambdaarr,k_global,k_global2,h166_byref"),
                "MSIL cells with no expectation: j_lambdaarr (InvalidProgramException), a non-constant module initialiser, a ByRef capture");

            // the cell counts
            Assert.That(CSharpCells().Count(), Is.EqualTo(74), "one C# cell per row, each through three entry points");
            Assert.That(ControlCells().Count(), Is.EqualTo(74 * 3 - 3 - 5 - 4), "74 rows x C++, JavaScript, MSIL, less the 12 cells above");
            Assert.That(BuildCommandCells().Count(), Is.EqualTo(3));

            // every shape class of the brief has a row
            foreach (var id in new[]
            {
                "a_loc", "a_par", "a_fld", "a_glob", "k_shared",                                     // a Sub lambda writing a capture / parameter / field / global
                "k4_ownparam", "b_fnpar", "b_parloop",                                               // a lambda writing its own parameter
                "c_asg", "c_call", "c_if", "c_loop", "j_names", "c_sel", "c_try",                    // non-final statements: assignment, call, If, loop, Dim, Select, Try
                "d_nest", "d_nest_par", "k5_nested_dim",                                             // nested lambdas
                "e_meth2", "e_ctor", "e_mybase", "k_global", "k_global2",                            // method, constructor, MyBase.New(...), module global initialiser
                "f_pass", "f_list", "f_ret",                                                         // passed, stored, returned
                "n8_hide_field", "n9_hide_global", "n_uninit",                                       // #165
                "c_call2", "g_once", "g_once_sub",                                                   // #179
                "e09a_me_method", "e09b_me_ctor",                                                    // #237
                "h166_byref", "h166_byref_loc", "h166_byref_par",                                    // #166
                "n_leakparam", "n_leaklocal", "n_leaklocal2",                                        // a lambda's name must not leak
                "p_loopdim_read", "p_loopdim_write", "p_elseif_lambda",                              // ADR-0014's per-iteration plan, an ElseIf chain's merge claim
            })
                Assert.That(all.Select(p => p.Id), Does.Contain(id), $"no row {id}");
        });
    }

    // ============================================================================================
    // C# — vbc's answer, through the CLI, the CLI with --optimize and CompileProjectFiles.
    // ============================================================================================

    /// <summary>
    /// The lambda body is written as a function body is: its writes to a capture, a parameter, a field, a global; its own Dims; its calls once;
    /// its If, loops, Select Case and Try; a lambda in a nested lambda, a constructor, <c>MyBase.New(...)</c> arguments, a property accessor and
    /// a module global's initialiser. Each row prints what vbc prints, in all three entry points. A regression that drops a statement, a
    /// declaration or a block, or writes a call twice, changes the text; one that loses a loop's exit hangs and fails as <c>hung</c>.
    /// </summary>
    [TestCaseSource(nameof(CSharpCells))]
    public void ALambdaBody_PrintsVbcsAnswer_OnCSharp_InEveryEntryPoint(TempProbe probe)
    {
        if (!ModuleInitialiserRows.Contains(probe.Id))
        {
            TempExec.AssertMatchesInEveryEntryPoint(Bk.CSharp, probe.Source, probe.Vb, probe.Id, hangSafe: true);
            return;
        }

        // ⚠ k_global and k_global2: a lambda in a module global's initialiser is an orphan to IRVerifier's Invariant P(d), which the test host has ON, so the
        // IN-PROCESS entry point (CompileProjectFiles) throws IRVerificationException before it emits anything. Pre-existing, not #136's (see
        // LambdaBodyEmissionShapeTests.ModuleInitialiserRows). The two spawned entry points run with the verifier off, as the shipping CLI does, and the
        // real `BasicLang build -c Release` below runs k_global through the project route.
        var failures = new List<string>();
        foreach (var entry in new[] { EntryPoint.Cli, EntryPoint.CliOptimize })
        {
            try
            {
                var got = TempExec.Norm(TempExec.Run(Bk.CSharp, entry, probe.Source, hangSafe: true));
                if (got != TempExec.Norm(probe.Vb)) failures.Add($"{entry}: printed [{got.Replace("\n", " | ")}] where vbc prints [{TempExec.Norm(probe.Vb).Replace("\n", " | ")}]");
            }
            catch (AssertionException ex)
            {
                failures.Add($"{entry}: {ex.Message.Split('\n')[0]}");
            }
        }

        Assert.That(failures, Is.Empty, $"{probe.Id} on CSharp, through Cli, CliOptimize:\n" + string.Join("\n", failures));
    }

    /// <summary>The rows whose lambda sits in a module global's initialiser: the in-process entry point cannot run them (the verifier's Invariant P(d), pre-existing).</summary>
    private static readonly HashSet<string> ModuleInitialiserRows = new(StringComparer.Ordinal) { "k_global", "k_global2" };

    // ============================================================================================
    // THE CONTROLS — C++, JavaScript and MSIL, which #136 did not change.
    // ============================================================================================

    /// <summary>
    /// The same programs on the other three backends (where the matrix says they print vbc's answer): the text above is the PROGRAM's answer,
    /// not C#'s. C++ and MSIL through the closure lowering (ADR-0019 / ADR-0010), JavaScript under Node.
    /// </summary>
    [TestCaseSource(nameof(ControlCells))]
    public void TheSameProgram_PrintsVbcsAnswer_OnTheOtherBackends(TempProbe probe, Bk backend)
        => TempExec.AssertMatchesInEveryEntryPoint(backend, probe.Source, probe.Vb, probe.Id);

    // ============================================================================================
    // THE TEXT — the spawned CLI writes the lambda blocks the library writes.
    // ============================================================================================

    /// <summary>
    /// The shape tests (<c>LambdaBodyEmissionShapeTests</c>) read the text the library produces in process; the CLI is a separate process with its own entry
    /// (<c>BasicCompiler.CompileFile</c>, module registry, preprocessing). The block of each lambda it writes — standard and with <c>--optimize</c> — is the
    /// block <c>CompileProjectFiles</c> writes, line for line, `#line`s left out: a fix verified only through the in-process helper can still break via the CLI.
    /// </summary>
    [TestCase("c_asg", TestName = "TheCli_WritesTheBlock_AWriteBeforeTheReturn")]
    [TestCase("d_nest", TestName = "TheCli_WritesTheBlock_NestedLambdas")]
    [TestCase("e_mybase", TestName = "TheCli_WritesTheBlock_MyBaseArguments")]
    [TestCase("h166_byref_par", TestName = "TheCli_WritesTheBlock_AByRefCall")]
    [TestCase("g_once_sub", TestName = "TheCli_WritesTheBlock_ASubLambdaWithUsedCalls")]
    public void TheCli_WritesTheLambdaBlocks_TheProjectEntryPointWrites(string id)
    {
        var source = LambdaBodyProbes.All.Single(p => p.Id == id).Source;
        static string[][] BlockLines(string csharp)
            => LambdaBodyEmissionShapeTests.Blocks(csharp.Replace("\r\n", "\n")).Select(b => b.Lines.Select(l => l.TrimEnd()).ToArray()).ToArray();

        var project = BlockLines(TempExec.Emit(Bk.CSharp, EntryPoint.ProjectRelease, source));
        Assert.That(project, Is.Not.Empty);
        Assert.Multiple(() =>
        {
            Assert.That(BlockLines(TempExec.Emit(Bk.CSharp, EntryPoint.Cli, source)), Is.EqualTo(project), "BasicLang Prog.bas --target=csharp");
            Assert.That(BlockLines(TempExec.Emit(Bk.CSharp, EntryPoint.CliOptimize, source)), Is.EqualTo(project), "BasicLang Prog.bas --target=csharp --optimize");
        });
    }

    // ============================================================================================
    // THE REAL BUILD — `BasicLang build App.blproj -c Release`, the route the IDE's build service takes.
    // ============================================================================================

    /// <summary>
    /// The project build writes the exe and the test RUNS it with a 20 s limit, killing the tree: a hang is a failure. c_asg (a write before a
    /// Return), k_global (lambdas in module-global initialisers: the one placement that has no enclosing function) and e_mybase (a block
    /// lambda with its own Dim in the arguments of <c>MyBase.New</c>).
    /// </summary>
    [TestCaseSource(nameof(BuildCommandCells))]
    public void TheReleaseProjectBuild_PrintsVbcsAnswer(TempProbe probe)
    {
        if (!CliTestHarness.DotnetOnPath()) Assert.Ignore("dotnet not found on PATH.");

        var dir = Path.Combine(Path.GetTempPath(), "bl-t136-build-" + Guid.NewGuid().ToString("N"));
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
