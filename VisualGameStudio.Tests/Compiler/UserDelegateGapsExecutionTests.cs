using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #202, RUN. The fast half is `UserDelegateGapsDiagnosticsTests` (read its header for the three gaps, the oracle and the mutants). This fixture compiles each probe and RUNS it on C#, C++, JavaScript and
//  MSIL, and every one must print what vbc prints (S/t202/probes/*.exp, mprobes/*.exp: the program wrapped in a VB Module, run with vbc).
//
//    * single-file probes go through the spawned CLI, the CLI with `--optimize` and `BasicCompiler.CompileProjectFiles` (aggressive): `TempExec.AssertMatchesInEveryEntryPoint`;
//    * the multi-file probes go through `CompileProjectFiles` with the standard passes and with the aggressive ones (`BindingProjectExec.Emit`) and, on C#, JavaScript and MSIL, the real CLI's
//      `build P.blproj -c Release` (a native C++ project build is MSVC's), each with the project's files listed in BOTH orders;
//    * the C# leg of every cell runs in a child process with a time limit (`TempExec.Run(hangSafe: true)` -> `CSharpProcessRunner`): never the in-process runner (#256).
//
//  A backend whose tool is missing (a C++ compiler, Node, ilasm) SKIPS its cells: never a failure. The test is ignored only when no cell could run.
//  ⚠ Named "...ExecutionTests" and runs JavaScript under Node: it is in `JsExecutionTierRosterTests`' roster.
//
//  ⛔ KNOWN GAPS — listed, deliberately NOT tested (asserting one would pin a defect). Each is outside #202's three gaps:
//    * A Delegate NESTED in a Module or a Class does not parse at all, bare or modified (S05, S06) — a ruling about nested-type access, not a measurement (#290).
//    * A file-level `Private Class` is accepted (vbc: BC31089); the Delegate arm refuses it and the Class arm was not touched (#290).
//    * S08 on MSIL: `InvalidProgramException` for a `Func(Of Double, Double)` lambda — the same with no `.Invoke` in the program (ctl/C08e). S08 runs on the other three; MSIL is left out of that row.
//    * S13 on JavaScript: `F.Invoke(2)` on a bare FIELD inside its OWN class is `ReferenceError: F is not defined` — #187's named-receiver path, the same for a user Delegate field (ctl/C13u).
//      S13 runs on the other three; JavaScript is left out of that row.
//
//  MUTANTS killed by this fixture (the whole list, with the fast fixture's killers, is in `UserDelegateGapsDiagnosticsTests`' header): MA by S01-S03, MB by M1-M4 in the reversed order, MD and MJ by the `.Invoke`
//  rows (a refusal), ME and MJ by the same rows on C++, JavaScript and MSIL (C# still runs). MG survives at run time: it leaves a dead temp and nothing else.
// ================================================================================================

/// <summary>#202 — a file-level Public / Friend Delegate, a Delegate in a sibling file, and `.Invoke` on a Func / Action, each printing vbc's answer on every backend.</summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the g++, Node, ilasm and C# child runs share the machine with the spawned CLI
public class UserDelegateGapsExecutionTests
{
    /// <summary>One group of single-file probes, each on every backend it runs on, through every entry point. A failing cell is collected, not thrown, so every other one still reports.</summary>
    private static void AssertSingleFile(params TempProbe[] probes)
    {
        var failures = new List<string>();
        int ran = 0, skipped = 0;
        foreach (var probe in probes)
        {
            foreach (var backend in TempExec.Backends(probe.Agrees))
            {
                try
                {
                    TempExec.AssertMatchesInEveryEntryPoint(backend, probe.Source, probe.Vb, probe.Id, probe.HangSafe);
                    ran++;
                }
                catch (IgnoreException)
                {
                    skipped++;
                }
                catch (AssertionException ex)
                {
                    ran++;
                    failures.Add(ex.Message);
                }
            }
        }

        Assert.That(failures, Is.Empty, string.Join("\n", failures));
        if (ran == 0) Assert.Ignore($"no execution tool on this machine ({skipped} cells skipped).");
    }

    /// <summary>The project entry points a multi-file probe goes through: <c>CompileProjectFiles</c>, standard passes, and with the aggressive ones (what a Release project build and the IDE call).</summary>
    private static readonly ProjectEntry[] ProjectEntries = { ProjectEntry.ProjectStandard, ProjectEntry.ProjectAggressive };

    /// <summary>
    /// <c>BasicLang build P.blproj -c Release</c>, the real CLI binary, over a project whose <c>Compile</c> items are written in the order the files are LISTED — an explicit list, so the compile order is the
    /// probe's, not the directory's. Returns the generated text (<c>bin/**/P.cs</c> / <c>.js</c> / <c>.il</c>). Not for C++: a native project build is MSVC's, and on a machine without it the CLI refuses
    /// (BL6015), which is the gate <c>BindingProjectExec.Entries</c> already drops the CLI legs for.
    /// </summary>
    private static string EmitViaCliBuild(Bk backend, (string Name, string Source)[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-t202-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var (name, source) in files) File.WriteAllText(Path.Combine(dir, name), source);
            var element = backend switch { Bk.CSharp => "CSharp", Bk.JavaScript => "JavaScript", Bk.Msil => "MSIL", _ => throw new ArgumentException(backend.ToString()) };
            File.WriteAllText(Path.Combine(dir, "P.blproj"),
                "<BasicLangProject>\n  <PropertyGroup><ProjectName>P</ProjectName><AssemblyName>P</AssemblyName><OutputType>Exe</OutputType>"
                + $"<TargetBackend>{element}</TargetBackend></PropertyGroup>\n  <ItemGroup>{string.Concat(files.Select(f => $"<Compile Include=\"{f.Name}\" />"))}</ItemGroup>\n</BasicLangProject>\n");
            var (exit, stdout, stderr) = CliTestHarness.RunProcess(CliTestHarness.CliPath(), new[] { "build", "P.blproj", "-c", "Release" }, dir, timeoutMs: 300_000);
            var built = Directory.Exists(Path.Combine(dir, "bin"))
                ? Directory.GetFiles(Path.Combine(dir, "bin"), "P" + TempExec.Extension(backend), SearchOption.AllDirectories).FirstOrDefault()
                : null;
            Assert.That(exit, Is.EqualTo(0), $"BasicLang build ({backend}) failed:\n{stdout}{stderr}");
            Assert.That(built, Is.Not.Null, $"the CLI wrote no P{TempExec.Extension(backend)}:\n{stdout}{stderr}");
            return File.ReadAllText(built!);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* temp */ } }
    }

    /// <summary>
    /// One multi-file probe in BOTH compile orders on every backend, through <c>CompileProjectFiles</c> (<see cref="ProjectEntries"/>) and, on C#, JavaScript and MSIL, the real CLI's
    /// <c>build P.blproj -c Release</c> with the files listed in that order. A failing cell is collected, not thrown.
    /// </summary>
    private static void AssertProjects(params ProjectProbe[] probes)
    {
        var failures = new List<string>();
        int ran = 0, skipped = 0;
        foreach (var probe in probes)
        {
            foreach (var shape in new[] { probe, UserDelegateGapProbes.Reversed(probe) })
            {
                var order = string.Join(",", shape.Files.Select(f => f.Name));
                foreach (var backend in TempExec.Backends(shape.Agrees))
                {
                    try
                    {
                        TempExec.RequireTool(backend);
                    }
                    catch (IgnoreException)
                    {
                        skipped++;
                        continue;
                    }

                    ran++;
                    var entries = ProjectEntries.Select(e => (Name: e.ToString(), Emit: (Func<string>)(() => BindingProjectExec.Emit(backend, e, shape.Files))))
                        .ToList();
                    if (backend != Bk.Cpp && (backend != Bk.CSharp || CliTestHarness.DotnetOnPath()))
                        entries.Add(("CliRelease", () => EmitViaCliBuild(backend, shape.Files)));

                    foreach (var (entry, emit) in entries)
                    {
                        try
                        {
                            var got = TempExec.Norm(TempExec.Run(backend, emit(), hangSafe: true));
                            if (got != TempExec.Norm(shape.Vb))
                                failures.Add($"{shape.Id} [{order}] on {backend}, {entry}: printed [{got.Replace("\n", " | ")}] where vbc prints [{TempExec.Norm(shape.Vb).Replace("\n", " | ")}]");
                        }
                        catch (AssertionException ex)
                        {
                            failures.Add($"{shape.Id} [{order}] on {backend}, {entry}: {ex.Message.Split('\n')[0]}");
                        }
                    }
                }
            }
        }

        Assert.That(failures, Is.Empty, string.Join("\n", failures));
        if (ran == 0) Assert.Ignore($"no execution tool on this machine ({skipped} cells skipped).");
    }

    /// <summary>
    /// (1) A file-level <c>Public Delegate Sub</c>, <c>Public Delegate Function</c> and two <c>Friend</c> Delegates (S01-S03) RUN, each through the CLI, the CLI with <c>--optimize</c> and
    /// <c>CompileProjectFiles</c>: <c>Public Delegate Sub D(...)</c> was a parse error. MA removes the parser arm.
    /// </summary>
    [Test]
    public void AFileLevelPublicOrFriendDelegate_RunsOnEveryBackend_PrintingVbcsAnswer()
        => AssertSingleFile(UserDelegateGapProbes.S01, UserDelegateGapProbes.S02, UserDelegateGapProbes.S03);

    /// <summary>
    /// (2) A Delegate declared in a SIBLING file, used as a field type, a parameter type and a local type (M1 Public, M2 two bare ones with `.Invoke`, M3 Friend with its Friend class) and, in M4, by a sibling
    /// CLASS in a third file — each with the project's files in BOTH orders, on every backend, through <c>CompileProjectFiles</c> (standard and aggressive) and the CLI project build. The reversed order was a refusal ("Cannot assign
    /// value of type 'Func' to variable of type 'Joiner'"). MB removes the pending-sibling sweep (M2, M4 and M1 reversed fail).
    /// </summary>
    [Test]
    public void ASiblingFilesDelegate_RunsOnEveryBackend_InBothCompileOrders()
        => AssertProjects(UserDelegateGapProbes.M1, UserDelegateGapProbes.M2, UserDelegateGapProbes.M3, UserDelegateGapProbes.M4);

    /// <summary>
    /// (3) <c>.Invoke</c> on a Func / Action VALUE RUNS: into a typed local (S07), as an argument (S09), on an Action in every arity (S10), concatenated with a String (S11), on a FIELD of another object (S12), on a
    /// parameter and on a call's result (S14), through the CLI, the CLI with <c>--optimize</c> and <c>CompileProjectFiles</c>. It was a refusal on C#, and where it compiled a METHOD call: "no member named
    /// 'Invoke'" on C++, "f.Invoke is not a function" on JavaScript, MissingMethodException on MSIL. MD puts the old analyzer gate back (S07, S09, S11, S12, S14 are refused); ME the old IR gate (S10, S11 and
    /// the rest fail on C++, JavaScript and MSIL, and still run on C#).
    /// </summary>
    [Test]
    public void ADotInvokeOnAFuncOrAction_RunsOnEveryBackend_PrintingVbcsAnswer()
        => AssertSingleFile(UserDelegateGapProbes.S07, UserDelegateGapProbes.S09, UserDelegateGapProbes.S10, UserDelegateGapProbes.S11, UserDelegateGapProbes.S12, UserDelegateGapProbes.S14);

    /// <summary>
    /// (3, around the two known gaps) S08 (`.Invoke` in arithmetic and on a Double Func) on C#, C++ and JavaScript — not MSIL, where a <c>Func(Of Double, Double)</c> lambda is an InvalidProgramException with
    /// or without `.Invoke` — and S13 (`.Invoke` on a Func, a user Delegate and an Action FIELD inside their own class, bare and through Me) on C#, C++ and MSIL — not JavaScript, where a bare field read inside
    /// its own class is a ReferenceError (#187's named-receiver path). The excluded cells are known failures and are not pinned as correct (see the header).
    /// </summary>
    [Test]
    public void ADotInvokeInArithmeticAndOnAFieldInItsOwnClass_RunsOnTheBackendsThatCanRunIt()
        => AssertSingleFile(UserDelegateGapProbes.S08, UserDelegateGapProbes.S13);
}
