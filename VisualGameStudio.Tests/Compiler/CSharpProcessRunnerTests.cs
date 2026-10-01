using System;
using System.Diagnostics;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// The hang-safe C# runner (<see cref="CSharpProcessRunner"/>) is itself test infrastructure that a loop test's
/// verdict rests on, so it is tested: above all, that it FAILS a program that never ends instead of hanging the
/// run (#256). Every one of these spawns <c>dotnet</c>, so the fixture is Integration; each case takes well under
/// ten seconds, and the two hang cases give the program a SHORT limit (3 s) rather than the 20 s a loop test gets.
///
/// <para>⛔ The hang case is a <c>While True</c> that BasicLang itself emits as <c>while (true) { }</c> — the
/// emitted shape a #256 regression produces for every short-circuit loop (<c>while (__sc0)</c> over a carrier
/// nothing rewrites) — and a hand-written <c>while (true) {}</c> beside it, so the runner is proved against the
/// compiler's own text and not only against C# the test author wrote.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // sets an environment variable; spawns children that burn a core while they hang
public class CSharpProcessRunnerTests
{
    private const string BasicLangHang = """
        Sub Main()
            Console.WriteLine("before")
            While True
            End While
            Console.WriteLine("after")
        End Sub
        """;

    private const string RawHang = """
        using System;
        public static class P
        {
            public static void Main()
            {
                Console.WriteLine("spinning");
                long n = 0;
                while (true) { n++; }
            }
        }
        """;

    private static bool IsRunning(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch (ArgumentException)
        {
            return false; // no such process
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    // ============================================================================================
    // THE HANG: reported within the limit, killed, and a test failure — never a frozen host.
    // ============================================================================================

    [Test]
    public void AProgramThatNeverEnds_IsReportedHung_WithinTheLimit_AndKilled()
    {
        var csharp = ReturnCoercionTests.EmitCSharpForTest(BasicLangHang);
        Assert.That(csharp, Does.Contain("while (true)"), "the probe must be the loop that cannot end");

        var run = CSharpProcessRunner.Run(csharp, timeoutMs: 3_000);

        Assert.Multiple(() =>
        {
            Assert.That(run.Outcome, Is.EqualTo(CSharpRunOutcome.Hung), "a program that never ends is Hung");
            Assert.That(run.Output, Does.Contain("before"), "what it printed before it stuck is kept");
            Assert.That(run.Output, Does.Not.Contain("after"));
            // The limit is 3 s; the kill, the pipe drain and the temp-directory cleanup get slack, but a runner
            // that waited for the program would be at the NUnit test timeout (or never come back).
            Assert.That(run.Elapsed, Is.GreaterThanOrEqualTo(TimeSpan.FromSeconds(2.5)), "it ran for the limit, not less");
            Assert.That(run.Elapsed, Is.LessThan(TimeSpan.FromSeconds(15)), "and was killed soon after it");
            Assert.That(IsRunning(run.ProcessId), Is.False, "the killed child must not be left spinning on a core");
        });
    }

    [Test]
    public void ARawInfiniteLoop_IsReportedHung_TheSameWay()
    {
        var run = CSharpProcessRunner.Run(RawHang, timeoutMs: 3_000);

        Assert.Multiple(() =>
        {
            Assert.That(run.Outcome, Is.EqualTo(CSharpRunOutcome.Hung));
            Assert.That(run.Output, Does.Contain("spinning"));
            Assert.That(run.Elapsed, Is.LessThan(TimeSpan.FromSeconds(15)));
            Assert.That(IsRunning(run.ProcessId), Is.False);
        });
    }

    /// <summary>
    /// ⭐ THE ASSERTION A LOOP TEST MAKES. A hang must surface as an <see cref="AssertionException"/> whose FIRST
    /// line says so — <c>TempExec.AssertMatchesInEveryEntryPoint</c> keeps only that line per entry point — and
    /// not as a pass, a skip, or a frozen run.
    /// </summary>
    [Test]
    public void RunExpectingSuccess_FailsAHang_WithAFirstLineThatNamesIt()
    {
        var csharp = ReturnCoercionTests.EmitCSharpForTest(BasicLangHang);

        var ex = Assert.Throws<AssertionException>(() => CSharpProcessRunner.RunExpectingSuccess(csharp, timeoutMs: 3_000));

        var firstLine = ex!.Message.Split('\n')[0];
        Assert.Multiple(() =>
        {
            Assert.That(firstLine, Does.StartWith("hung"));
            Assert.That(firstLine, Does.Contain("#256"));
            Assert.That(firstLine, Does.Contain("before"), "the partial output is on the line that is reported");
            Assert.That(ex.Message, Does.Contain("while (true)"), "the emitted C# is in the message, so the loop can be read");
        });
    }

    // ============================================================================================
    // THE CONTROLS: the runner does not fail what it should not.
    // ============================================================================================

    [Test]
    public void AProgramThatEnds_RanWithItsOutput()
    {
        var run = CSharpProcessRunner.Run(ReturnCoercionTests.EmitCSharpForTest(
            "Sub Main()\n    Console.WriteLine(\"one\")\n    Console.WriteLine(\"two\")\nEnd Sub\n"));

        Assert.Multiple(() =>
        {
            Assert.That(run.Outcome, Is.EqualTo(CSharpRunOutcome.Ran));
            Assert.That(run.ExitCode, Is.EqualTo(0));
            Assert.That(FourBackends.Norm(run.Output), Is.EqualTo("one\ntwo"));
            Assert.That(run.Elapsed, Is.LessThan(TimeSpan.FromSeconds(15)));
        });
    }

    /// <summary>
    /// The same program through both runners prints the same text — the child process is not a different semantics.
    /// ⛔ NOT a loop: this is the one place a program runs through the IN-PROCESS runner, which has no limit, so it
    /// must be a program no mutant of a loop fix can hang.
    /// </summary>
    [Test]
    public void TheSameProgram_PrintsTheSameInProcessAndOutOfProcess()
    {
        var csharp = ReturnCoercionTests.EmitCSharpForTest(
            "Function Twice(n As Integer) As Integer\n    Return n * 2\nEnd Function\n" +
            "Sub Main()\n    Console.WriteLine(\"v=\" & Twice(21) & \"|\" & If(Twice(1) = 2, \"yes\", \"no\"))\nEnd Sub\n");

        var inProcess = FourBackends.Norm(FourBackends.RunEmittedCSharpText(csharp));
        var outOfProcess = FourBackends.Norm(CSharpProcessRunner.RunExpectingSuccess(csharp));

        Assert.That(outOfProcess, Is.EqualTo(inProcess));
        Assert.That(outOfProcess, Is.EqualTo("v=42|yes"));
    }

    /// <summary>A loop that DOES end runs to vbc's answer through the runner (the in-process leg is not asked: it has no limit).</summary>
    [Test]
    public void ATerminatingLoop_RunsToVbcsAnswer()
    {
        var probe = LoopConditionProbes.ById("W_aa");

        var printed = CSharpProcessRunner.RunExpectingSuccess(ReturnCoercionTests.EmitCSharpForTest(probe.Source));

        Assert.That(FourBackends.Norm(printed), Is.EqualTo(probe.Vb));
    }

    [Test]
    public void AnUnhandledException_IsCrashed_NotHung_AndKeepsItsMessage()
    {
        var csharp = ReturnCoercionTests.EmitCSharpForTest(
            "Sub Main()\n    Console.WriteLine(\"before\")\n    Throw New Exception(\"boom\")\nEnd Sub\n");

        var run = CSharpProcessRunner.Run(csharp);
        var ex = Assert.Throws<AssertionException>(() => CSharpProcessRunner.RunExpectingSuccess(csharp));

        Assert.Multiple(() =>
        {
            Assert.That(run.Outcome, Is.EqualTo(CSharpRunOutcome.Crashed));
            Assert.That(run.ExitCode, Is.Not.EqualTo(0));
            Assert.That(run.Error, Does.Contain("boom"));
            Assert.That(run.Output, Does.Contain("before"));
            Assert.That(ex!.Message, Does.StartWith("the emitted C# threw"), "a crash is reported as a crash, not as a hang");
        });
    }

    [Test]
    public void StdinIsClosed_SoAProgramThatReadsSeesEndOfInput()
    {
        var run = CSharpProcessRunner.Run("""
            using System;
            public static class P
            {
                public static void Main()
                {
                    var line = Console.ReadLine();
                    Console.WriteLine(line == null ? "eof" : "got " + line);
                }
            }
            """, timeoutMs: 10_000);

        Assert.That(run.Outcome, Is.EqualTo(CSharpRunOutcome.Ran), "a read must not block until the limit");
        Assert.That(FourBackends.Norm(run.Output), Is.EqualTo("eof"));
    }

    /// <summary>A program that fills BOTH pipes while the runner waits would deadlock a runner that read them one after the other.</summary>
    [Test]
    public void ALotOfOutputOnBothPipes_DoesNotDeadlock()
    {
        var run = CSharpProcessRunner.Run("""
            using System;
            public static class P
            {
                public static void Main()
                {
                    for (int i = 0; i < 20000; i++)
                    {
                        Console.WriteLine("out line number " + i);
                        Console.Error.WriteLine("err line number " + i);
                    }
                }
            }
            """, timeoutMs: 15_000);

        Assert.Multiple(() =>
        {
            Assert.That(run.Outcome, Is.EqualTo(CSharpRunOutcome.Ran));
            Assert.That(run.Output, Does.Contain("out line number 19999"));
            Assert.That(run.Error, Does.Contain("err line number 19999"));
        });
    }

    /// <summary>The compile is the SAME Roslyn path the in-process runner uses, so a program that does not compile fails the same way — it is never "run".</summary>
    [Test]
    public void CSharpThatDoesNotCompile_FailsInTheSharedCompileStep()
    {
        var ex = Assert.Throws<AssertionException>(() => CSharpProcessRunner.Run("public static class P { public static void Main() { int x = \"s\"; } }"));

        Assert.That(ex!.Message, Does.Contain("the emitted C# does not compile"));
        Assert.That(ex.Message, Does.Contain("CS0029"));
    }

    // ============================================================================================
    // THE LIMIT: 20 s unless a mutant measurement shortens it.
    // ============================================================================================

    [Test]
    public void TheLimit_IsTwentySeconds_UnlessTheMeasuringVariableIsSet()
    {
        var before = Environment.GetEnvironmentVariable(CSharpProcessRunner.TimeoutEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(CSharpProcessRunner.TimeoutEnvironmentVariable, null);
            Assert.That(CSharpProcessRunner.EffectiveTimeoutMs(), Is.EqualTo(20_000));
            Assert.That(CSharpProcessRunner.DefaultTimeoutMs, Is.EqualTo(20_000));

            Environment.SetEnvironmentVariable(CSharpProcessRunner.TimeoutEnvironmentVariable, "4000");
            Assert.That(CSharpProcessRunner.EffectiveTimeoutMs(), Is.EqualTo(4_000));

            Environment.SetEnvironmentVariable(CSharpProcessRunner.TimeoutEnvironmentVariable, "junk");
            Assert.That(CSharpProcessRunner.EffectiveTimeoutMs(), Is.EqualTo(20_000), "garbage falls back to the default");
        }
        finally
        {
            Environment.SetEnvironmentVariable(CSharpProcessRunner.TimeoutEnvironmentVariable, before);
        }
    }
}
