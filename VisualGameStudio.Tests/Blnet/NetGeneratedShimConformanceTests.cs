using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using BasicLang.Compiler.ProjectSystem;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Blnet;

/// <summary>
/// §12.3's generated-shim conformance rows — P2a-2 Task 12.
///
/// <para>⛔ <b>NOT <c>BlnetConformanceTests</c>.</b> That file is the FROZEN P0 §12.2 suite: 16
/// hand-shim scenarios driven by a C++ harness whose binder expects seven <c>blnet_test_*</c>
/// exports a GENERATED shim does not have. Its scenarios stay exactly as they are. The names are
/// four letters apart on purpose-avoidance grounds: a careless
/// <c>--filter "…ConformanceTests"</c> would re-run 16 AOT-backed processes, so read the name
/// before editing.</para>
///
/// <para><b>The template is the publish-and-run pipeline</b>
/// (<c>NetShimPipelineTests.Milestone_</c> / <c>Section85_</c>), because a §12.3 row is a
/// BasicLang PROGRAM built through the shipping CLI, not a C++ TU.
/// <c>NetShimPipelineFixture.Run</c> already carries the async-read-before-<c>WaitForExit</c>
/// ordering and the 60 s hang guard the plan asks for.</para>
///
/// <para>⛔⛔ <b>THE RULE FOR EVERY ROW IN THIS FILE — shape substitution is the failure mode.</b>
/// When a §12.3 row will not compile, the natural repair is to rewrite it into a shape that does,
/// and every such rewrite silently deletes the coverage the row existed to add: the suite goes
/// fully green while the defect stays live. Author the row in §12.3's shape FIRST. If it will not
/// build or runs wrong, it is finished only as (a) a PINNED DIVERGENCE asserting the exact current
/// diagnostic/exit/output, or (b) an explicitly deferred row naming its chip — never as a
/// rewritten shape that passes. Record the rejected shape next to any row you substituted.</para>
///
/// <para>⚠ <b>Builds are expensive (~25 s plus an AOT publish), so programs are FEW and RICH.</b>
/// Adding a scenario to an existing program costs nothing; adding a program costs a publish.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class NetGeneratedShimConformanceTests
{
    private static readonly ConcurrentDictionary<string, Lazy<SharedBuild>> Builds = new();
    private static readonly ConcurrentBag<string> Dirs = new();

    private sealed record SharedBuild(string Dir, CppProjectBuildResult Result);

    /// <summary>
    /// Build a program once per fixture run and memoize it. Mirrors
    /// <c>NetShimPipelineTests.BuildOnce</c> rather than sharing it, because that one is private
    /// and its <c>[OneTimeTearDown]</c> owns its own directories.
    /// </summary>
    private static SharedBuild BuildOnce(string projectName, IReadOnlyDictionary<string, string> files)
    {
        var key = projectName;
        return Builds.GetOrAdd(key, _ => new Lazy<SharedBuild>(() =>
        {
            var dir = NetShimPipelineFixture.NewTempDir("blnet-conf-");
            Dirs.Add(dir);
            foreach (var file in files)
                File.WriteAllText(Path.Combine(dir, file.Key), file.Value);

            var projectPath = NetShimPipelineFixture.WriteProject(dir, projectName);
            var stopwatch = Stopwatch.StartNew();
            var result = CppProjectBuilder.Build(ProjectFile.Load(projectPath), "Release");
            stopwatch.Stop();
            TestContext.Out.WriteLine(
                $"{projectName}: conformance build took {stopwatch.Elapsed.TotalSeconds:F1}s");
            foreach (var message in result.Messages)
                TestContext.Out.WriteLine("  " + message);
            return new SharedBuild(dir, result);
        })).Value;
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        Builds.Clear();
        while (Dirs.TryTake(out var dir))
            NetShimPipelineFixture.TryDeleteDir(dir);
    }

    private static void AssertBuilt(CppProjectBuildResult result, string label)
    {
        Assert.That(result.Success, Is.True,
            label + " did not build.\n" + NetShimPipelineFixture.Diagnostics(result)
            + "\n" + result.RawToolchainOutput);
        Assert.That(File.Exists(result.ExecutablePath), Is.True, result.ExecutablePath);
    }

    // =====================================================================================
    // §12.3 — a NAMED property setter, and handle identity across statements.
    // =====================================================================================

    /// <summary>
    /// THE property row, and the reason it can exist at all.
    ///
    /// <para>§12.3 wants a named property WRITE proven at run level. The indexer-setter half was
    /// already run-proven (<c>Section85_</c>), but a named <c>set_X</c> was not — and writing one
    /// honestly needs the write to be OBSERVABLE, which needs the SAME handle in two statements.
    /// </para>
    ///
    /// <para>⛔ <b>Why the documented workaround cannot express this row.</b> The recorded way to
    /// use a non-curated .NET type without the §8.5 fifth-site fix is a static factory called
    /// INLINE — <c>Zoo.MakeDog().Speak()</c>. That mints a FRESH handle per call, so
    /// <c>Make().Value = 5</c> followed by <c>Make().Value</c> never touches the same object and
    /// a write-then-read row built on it would pass while observing nothing. That is precisely
    /// the shape substitution this fixture's header forbids.</para>
    ///
    /// <para><c>FileInfo</c> is one of the curated five, so <c>New FileInfo(…)</c> is legal
    /// anywhere. <c>fi.Create()</c> returns a <c>FileStream</c>, which is NOT curated — that local
    /// is admitted only because an inferred local off a member RESULT carries the handle marker,
    /// which <c>CppCapabilityChecker</c> began honouring in the same change that made this row
    /// writable. A real <c>FileStream</c> is seekable, so <c>Position</c> genuinely round-trips;
    /// <c>Stream.Null</c> would have read back 0 whether or not the setter ever crossed.</para>
    /// </summary>
    [Test]
    public void ANamedPropertySetter_CrossesAndTheGetterReadsItBack()
    {
        var built = BuildOnce("ConfProperty", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Program.bas"] = """
                Using System.IO

                Module Program
                 Sub Main()
                  Dim fi As New FileInfo("blnet_conf_probe.dat")
                  Dim s = fi.Create()
                  s.Position = 3
                  Console.WriteLine(s.Position)
                  s.Position = 11
                  Console.WriteLine(s.Position)
                  Console.WriteLine(s.CanWrite)
                 End Sub
                End Module
                """,
        });

        AssertBuilt(built.Result, "the §12.3 named-property program");

        Assert.That(NetShimPipelineFixture.Run(built.Result.ExecutablePath!),
            Is.EqualTo("3\n11\nTrue\n"),
            "These are .NET's own answers for a seekable FileStream, asserted rather than "
            + "whatever the backend happens to produce. A repeated first value means the setter "
            + "never crossed and the getter is reading a stale or default Position; '0' twice "
            + "means the handle is not the same object across statements (the fresh-handle-per-"
            + "call failure the summary describes); a missing 'True' means the Boolean result row "
            + "regressed.");
    }
}
