using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using VisualGameStudio.Tests.Compiler;

namespace VisualGameStudio.Tests.Msil;

// =====================================================================================
//  Task #172 — ADR-0014 A1's own MSIL dependency, #226 (fix commit 4cdf2dd1): an `Exit` inside a
//  Try that a WRAPPED loop (one with captured body locals) encloses used to be walked by
//  MSILBackend.CollectRegionBlocks as though it stayed inside the Try's arm, dragging the loop's
//  END block — and everything textually after the loop — into `.try { }`, which ilasm/the CLR
//  reject with InvalidProgramException. `stopAtOuterLoopExits` (MSILBackend.cs) now stops that
//  walk at exactly such an Exit; P226/P226b/P226c/P226w below are the four loop kinds, none of
//  which even needs a captured lambda — #226 is a REGION-COLLECTION bug the wrapping Try/Finally
//  merely exposed, not a per-iteration-capture one.
//
//  This fixture also carries M9 (task #172's own mutation-proof shape, from E09): a For Each
//  whose declaring variable AND a body Dim are BOTH captured must share ONE per-iteration
//  environment (D5), never two.
// =====================================================================================

/// <summary>Verbatim from the architect's own probes, S/t172/edge (P226, P226b, P226c) and this
/// fixture's own P226w (the fourth loop kind, `While`/`Exit While`, mirroring the same shape).</summary>
internal static class PerIterationLoopBodyDimMsilProbes
{
    internal const string P226 = """
        Sub Main()
            Dim t As Integer = 0
            For i As Integer = 1 To 3
                Try
                    t = t + i
                    If i = 2 Then Exit For
                Finally
                    t = t * 10
                End Try
            Next
            Console.WriteLine(t)
        End Sub
        """;
    internal const string P226Expected = "120";

    internal const string P226b = """
        Sub Main()
            Dim t As Integer = 0
            Dim n As Integer = 0
            Do
                n = n + 1
                Try
                    t = t + n
                    If n = 3 Then Exit Do
                Finally
                    t = t * 2
                End Try
            Loop
            Console.WriteLine(t)
        End Sub
        """;
    internal const string P226bExpected = "22";

    internal const string P226c = """
        Sub Main()
            Dim items As New List(Of Integer)()
            items.Add(5)
            items.Add(10)
            items.Add(15)
            items.Add(20)
            Dim t As Integer = 0
            For Each v As Integer In items
                Try
                    t = t + v
                    If v = 15 Then Exit For
                Finally
                    t = t + 1
                End Try
            Next
            Console.WriteLine(t)
        End Sub
        """;
    internal const string P226cExpected = "33";

    /// <summary>The fourth loop kind — `While...End While` / `Exit While` — none of P226/P226b/
    /// P226c exercises. Same shape and the same arithmetic as P226 (a coincidence of the numbers
    /// chosen, not a claim the two are otherwise identical: this is a `While`, not a counted
    /// `For`, so it is `whileN.body`/`whileN.cond`/`whileN.end`, a different loop-block family
    /// entirely, walked by the SAME `stopAtOuterLoopExits` fix.</summary>
    internal const string P226w = """
        Sub Main()
            Dim t As Integer = 0
            Dim i As Integer = 0
            While i < 3
                i = i + 1
                Try
                    t = t + i
                    If i = 2 Then Exit While
                Finally
                    t = t * 10
                End Try
            End While
            Console.WriteLine(t)
        End Sub
        """;
    internal const string P226wExpected = "120";

    /// <summary>M9's own shape: E09 from <c>PerIterationLoopBodyDimProbes</c>, reused here by
    /// reference so the IL-structural check below and the execution check
    /// (PerIterationLoopBodyDimExecutionTests.E09_...) stay the SAME program.</summary>
    internal const string E09 = PerIterationLoopBodyDimProbes.E09;
}

[TestFixture]
[Category("Integration")]
public class PerIterationLoopBodyDimMsilTests
{
    // ============================================================================================
    // #226: an Exit inside a Try that a wrapped loop encloses must be a `leave`, not a walk into
    // the protected region. Before the fix: InvalidProgramException (For/Do/ForEach — measured);
    // P226b additionally printed 11 instead of 22 on a build that DID assemble on other branches
    // of the investigation (S/t172/ruling.md's own implementation notes name both symptoms).
    // ============================================================================================

    [Test]
    public void P226_ExitForInsideTry_StandardPipeline()
        => Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(PerIterationLoopBodyDimMsilProbes.P226)),
            Is.EqualTo(PerIterationLoopBodyDimMsilProbes.P226Expected));

    [Test]
    public void P226_ExitForInsideTry_AggressivePipeline()
        => Assert.That(FourBackends.Norm(MsilHarness.RunAggressiveExpectingSuccess(PerIterationLoopBodyDimMsilProbes.P226)),
            Is.EqualTo(PerIterationLoopBodyDimMsilProbes.P226Expected));

    [Test]
    public void P226b_ExitDoInsideTry_StandardPipeline()
        => Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(PerIterationLoopBodyDimMsilProbes.P226b)),
            Is.EqualTo(PerIterationLoopBodyDimMsilProbes.P226bExpected));

    [Test]
    public void P226b_ExitDoInsideTry_AggressivePipeline()
        => Assert.That(FourBackends.Norm(MsilHarness.RunAggressiveExpectingSuccess(PerIterationLoopBodyDimMsilProbes.P226b)),
            Is.EqualTo(PerIterationLoopBodyDimMsilProbes.P226bExpected));

    [Test]
    public void P226c_ExitForEachInsideTry_StandardPipeline()
        => Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(PerIterationLoopBodyDimMsilProbes.P226c)),
            Is.EqualTo(PerIterationLoopBodyDimMsilProbes.P226cExpected));

    [Test]
    public void P226c_ExitForEachInsideTry_AggressivePipeline()
        => Assert.That(FourBackends.Norm(MsilHarness.RunAggressiveExpectingSuccess(PerIterationLoopBodyDimMsilProbes.P226c)),
            Is.EqualTo(PerIterationLoopBodyDimMsilProbes.P226cExpected));

    /// <summary>The fourth loop kind, not covered by P226/P226b/P226c: `Exit While` inside a Try
    /// that the enclosing `While` loop itself owns.</summary>
    [Test]
    public void P226w_ExitWhileInsideTry_StandardPipeline()
        => Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(PerIterationLoopBodyDimMsilProbes.P226w)),
            Is.EqualTo(PerIterationLoopBodyDimMsilProbes.P226wExpected));

    [Test]
    public void P226w_ExitWhileInsideTry_AggressivePipeline()
        => Assert.That(FourBackends.Norm(MsilHarness.RunAggressiveExpectingSuccess(PerIterationLoopBodyDimMsilProbes.P226w)),
            Is.EqualTo(PerIterationLoopBodyDimMsilProbes.P226wExpected));

    // ============================================================================================
    // M9: a For Each whose declaring variable AND a body Dim are BOTH captured get ONE shared
    // per-iteration environment (D5), never two. Structural — no ilasm needed, so this half of
    // the fixture is fast; kept in this [Category("Integration")] file because its sibling tests
    // above need ilasm and the probe (E09) is shared.
    // ============================================================================================

    /// <summary>Measured shape (this exact build, CompileToIl non-optimizing): the loop body
    /// creates exactly ONE <c>&lt;&gt;c__EnvN</c> instance (<c>newobj instance void
    /// '&lt;&gt;c__Env1'::.ctor()</c>, once, inside <c>foreach0body:</c>), and that ONE class
    /// declares fields for BOTH the For Each element (<c>n</c>) and the body Dim (<c>sq</c>) —
    /// never two separate environment classes chained together for one iteration.</summary>
    [Test]
    public void ForEachElementAndBodyDim_ShareOnePerIterationEnvironment_NotTwo()
    {
        var il = MsilHarness.CompileToIl(PerIterationLoopBodyDimMsilProbes.E09, optimize: false);

        var envClasses = Regex.Matches(il, @"\.class public auto ansi beforefieldinit '(<>c__Env\d+)'")
            .Select(m => m.Groups[1].Value).ToList();
        Assert.That(envClasses, Has.Count.EqualTo(2),
            "expected exactly two environment classes — the function-level one and ONE shared " +
            "per-iteration one — got: " + string.Join(", ", envClasses) + "\n--- IL ---\n" + il);

        // The per-iteration one is whichever holds fields — the function-level environment here
        // (Main captures nothing of its own) has none.
        var perIterationClass = envClasses.Single(name =>
        {
            var body = ClassBody(il, name);
            return body.Contains(".field");
        });

        var classText = ClassBody(il, perIterationClass);
        Assert.Multiple(() =>
        {
            Assert.That(classText, Does.Match(@"\.field public int32 'n'"),
                "the shared environment must hold the For Each element 'n'");
            Assert.That(classText, Does.Match(@"\.field public int32 'sq'"),
                "the shared environment must hold the body Dim 'sq' TOO — one environment, not two");
        });

        var newobjCount = Regex.Matches(il, $"newobj instance void '{Regex.Escape(perIterationClass)}'::\\.ctor\\(\\)").Count;
        Assert.That(newobjCount, Is.EqualTo(1),
            "the loop body must create the shared environment EXACTLY ONCE per iteration, not once " +
            "per captured name — got " + newobjCount + " newobj sites\n--- IL ---\n" + il);
    }

    /// <summary>The text between a class header and its matching <c>end of class</c> marker.</summary>
    private static string ClassBody(string il, string className)
    {
        var header = $"beforefieldinit '{className}'";
        var start = il.IndexOf(header, System.StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"class '{className}' not found in the IL");
        var end = il.IndexOf($"end of class '{className}'", start, System.StringComparison.Ordinal);
        Assert.That(end, Is.GreaterThanOrEqualTo(0), $"no matching 'end of class' for '{className}'");
        return il.Substring(start, end - start);
    }
}
