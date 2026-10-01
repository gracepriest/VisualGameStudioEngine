using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>How a time-limited run of emitted C# ended.</summary>
internal enum CSharpRunOutcome
{
    /// <summary>The program ended on its own with exit code 0.</summary>
    Ran,

    /// <summary>The program was still running when the time limit passed and was killed.</summary>
    Hung,

    /// <summary>The program ended on its own with a non-zero exit code (an unhandled exception).</summary>
    Crashed,
}

/// <param name="Output">What the program wrote to stdout. For <see cref="CSharpRunOutcome.Hung"/> it is whatever
/// arrived before the kill.</param>
/// <param name="Error">What it wrote to stderr (the unhandled exception, for <see cref="CSharpRunOutcome.Crashed"/>).</param>
/// <param name="ProcessId">The child's pid — the hang test asks the OS whether it is still there.</param>
internal sealed record CSharpProcessResult(
    CSharpRunOutcome Outcome, string Output, string Error, int ExitCode, TimeSpan Elapsed, int ProcessId);

/// <summary>
/// ⛔⛔ THE ONLY WAY TO RUN EMITTED C# THAT MIGHT NEVER END (#256).
///
/// <para><see cref="FourBackends.RunEmittedCSharpText"/> loads the program into the TEST HOST and invokes
/// <c>Main</c> on the calling thread, with no limit. A C# loop that hangs there does not fail the test: it
/// freezes the whole host, every other test with it, and the run has to be killed by hand. MEASURED on
/// NUnit 4.0.1 / .NET 8 (<c>S/t256/nunit-to</c>): <c>[CancelAfter]</c> does NOT stop a synchronous spin
/// (the run was killed at 60 s) and <c>[Timeout]</c> is obsolete (CS0618) and leaves the spinning thread
/// running with <c>Console</c> still redirected. Nothing in-process can stop a thread that never yields.</para>
///
/// <para>So this runner compiles through the SAME Roslyn path (<see cref="FourBackends.CompileEmittedCSharp"/>,
/// not a copy) but writes the assembly and a <c>runtimeconfig.json</c> to a temp directory and runs
/// <c>dotnet &lt;dll&gt;</c> as a CHILD PROCESS — the shape <c>MsilHarness.Exec</c> already uses — with stdin
/// closed, both pipes read asynchronously, <see cref="DefaultTimeoutMs"/> to finish, and
/// <c>Kill(entireProcessTree: true)</c> when it does not. A hang is then a <see cref="CSharpRunOutcome.Hung"/>
/// result, and <see cref="RunExpectingSuccess"/> turns it into a test FAILURE whose first line starts with
/// <c>hung</c>.</para>
///
/// <para>Use it for every C# run of a program that holds a <c>While</c>/<c>Do</c> loop whose condition has
/// control flow (<c>AndAlso</c>, <c>OrElse</c>, <c>If()</c>), and for any loop a bug could leave without an
/// exit: #256 is fixed, but #227 (a bottom-tested loop's second copy of its body drops blocks, so
/// <c>Do … Loop Until</c> over a nested loop never ends) still hangs on C#, and a regression of the fix
/// hangs every such loop. ⚠ Not for everything: a process per run costs about 0.2 s, which the ~40 other
/// fixtures that use the in-process runner would pay for nothing.</para>
///
/// <para>⚠ Limit: the child sees only the shared framework and what is beside the dll, so a program that
/// references an assembly outside it (the engine wrapper, BasicLang's own runtime) ends
/// <see cref="CSharpRunOutcome.Crashed"/> with a <c>FileNotFoundException</c>, not <c>Hung</c>. Every loop
/// probe uses <c>System</c> only.</para>
/// </summary>
internal static class CSharpProcessRunner
{
    /// <summary>
    /// What a loop probe is given to finish. A correct probe ends in well under a second; the limit is
    /// generous because a loaded machine (two test hosts, a C++ compile alongside) must not turn a slow
    /// start into a "hang".
    /// </summary>
    internal const int DefaultTimeoutMs = 20_000;

    /// <summary>
    /// ⚠ MEASURING A MUTANT ONLY. A mutant of the #256 fix hangs by design, and every hung cell costs the whole
    /// limit (59 probes x 3 entry points x 20 s is an hour). Setting this variable shortens the limit for such a
    /// run; a gate run never sets it, and the default is what the suite ships.
    /// </summary>
    internal const string TimeoutEnvironmentVariable = "BL_CSHARP_RUN_TIMEOUT_MS";

    internal static int EffectiveTimeoutMs()
        => int.TryParse(Environment.GetEnvironmentVariable(TimeoutEnvironmentVariable), out var ms) && ms > 0
            ? ms
            : DefaultTimeoutMs;

    /// <summary>
    /// Compiles <paramref name="csharp"/> (asserting it compiles, like <see cref="FourBackends.RunEmittedCSharpText"/>)
    /// and runs it in a child process for at most <paramref name="timeoutMs"/>. Never throws for a hang or a crash:
    /// the outcome is the result.
    /// </summary>
    internal static CSharpProcessResult Run(string csharp, int? timeoutMs = null)
    {
        var limit = timeoutMs ?? EffectiveTimeoutMs();
        var dll = FourBackends.CompileEmittedCSharp(csharp);

        var dir = Path.Combine(Path.GetTempPath(), "bl-cs-run-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var dllPath = Path.Combine(dir, "Prog.dll");
            File.WriteAllBytes(dllPath, dll);

            // The shared host needs a runtimeconfig to launch a managed assembly. The same one MsilHarness
            // writes; the test project targets net8.0, so the child can too.
            File.WriteAllText(
                Path.Combine(dir, "Prog.runtimeconfig.json"),
                "{\"runtimeOptions\":{\"tfm\":\"net8.0\",\"framework\":"
                + "{\"name\":\"Microsoft.NETCore.App\",\"version\":\"8.0.0\"}}}");

            var start = new ProcessStartInfo(DotnetHost(), $"\"{dllPath}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                // Not the repo: a global.json up the tree must not pick the child's SDK.
                WorkingDirectory = dir,
            };

            var clock = Stopwatch.StartNew();
            using var process = Process.Start(start);
            Assert.That(process, Is.Not.Null, "could not start the dotnet host for the emitted C#");
            var pid = process!.Id;

            // Closed at once: a program that reads must see end-of-input, not block until the limit.
            process.StandardInput.Close();

            // Both pipes before the wait: a program that fills one while we block on the other deadlocks.
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(limit))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* it ended in the meantime */ }
                try { process.WaitForExit(10_000); } catch { /* best effort */ }
                clock.Stop();
                return new CSharpProcessResult(CSharpRunOutcome.Hung, Drained(stdout), Drained(stderr), -1, clock.Elapsed, pid);
            }

            // The program ended; a grandchild holding the pipes open must not hold the drain open with it.
            var output = Drained(stdout);
            var error = Drained(stderr);
            clock.Stop();
            return new CSharpProcessResult(
                process.ExitCode == 0 ? CSharpRunOutcome.Ran : CSharpRunOutcome.Crashed,
                output, error, process.ExitCode, clock.Elapsed, pid);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* temp; a killed child may still hold the dll briefly */ }
        }
    }

    /// <summary>
    /// ⭐ The run a loop test makes: what the program printed, or a test FAILURE that says why it did not.
    /// The first line of a hang's message starts <c>hung</c> — <c>TempExec.AssertMatchesInEveryEntryPoint</c>
    /// reports only the first line of each entry point's failure, and that line must name the hang.
    /// </summary>
    internal static string RunExpectingSuccess(string csharp, int? timeoutMs = null)
    {
        var limit = timeoutMs ?? EffectiveTimeoutMs();
        var run = Run(csharp, limit);
        switch (run.Outcome)
        {
            case CSharpRunOutcome.Hung:
                Assert.Fail(
                    $"hung (#256/#227): the emitted C# was still running after {limit / 1000.0:0.#} s and was killed; "
                    + $"it had printed [{Flat(run.Output)}]\n--- emitted ---\n{csharp}");
                break;
            case CSharpRunOutcome.Crashed:
                Assert.Fail(
                    $"the emitted C# threw (exit {run.ExitCode}): {Flat(run.Error)}\n--- emitted ---\n{csharp}");
                break;
        }
        return run.Output;
    }

    private static string DotnetHost()
    {
        // The test host IS `dotnet` when run by `dotnet test`; reuse its path, else rely on PATH (as MsilHarness does).
        var current = Environment.ProcessPath;
        return current != null && Path.GetFileNameWithoutExtension(current).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? current
            : "dotnet";
    }

    private static string Drained(Task<string> read)
    {
        try
        {
            return read.Wait(10_000) ? read.Result : "";
        }
        catch (AggregateException)
        {
            return "";
        }
    }

    private static string Flat(string s)
    {
        s = (s ?? "").Replace("\r\n", "\n").Trim().Replace("\n", " | ");
        return s.Length > 300 ? s.Substring(0, 300) + "..." : s;
    }
}
