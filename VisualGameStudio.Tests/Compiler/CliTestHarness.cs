using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Shared harness for spawned-process end-to-end tests: locates the real
/// BasicLang.exe deployed next to the tests and drives it (or any produced
/// executable) as a child process. The spawn/kill-tree/timeout/output-drain
/// policy lives here — <see cref="CppProjectCliBuildTests"/> and
/// <see cref="MixedProjectBuildTests"/> drive the CLI through
/// <see cref="RunCli"/>, and <see cref="NativeBclFrontEndTests"/> uses
/// <see cref="RunProcess"/> directly. New spawn code should call
/// <see cref="RunProcess"/> instead of hand-rolling a Process (a few older
/// fixtures still carry small pre-existing local run helpers).
/// </summary>
internal static class CliTestHarness
{
    public static string CliPath()
    {
        var cliPath = Path.Combine(AppContext.BaseDirectory, "BasicLang.exe");
        Assert.That(File.Exists(cliPath), Is.True,
            "BasicLang.exe not deployed next to the tests — project reference output changed?");
        return cliPath;
    }

    public static Task<(int ExitCode, string StdOut, string StdErr)> RunCli(
        string workingDir, params string[] args)
        => Task.Run(() => RunProcess(CliPath(), args, workingDir, timeoutMs: 300_000));

    /// <summary>
    /// Spawns a process with redirected output, a hard timeout, and kill-tree on
    /// hang (BasicLang.exe spawns dotnet build / the C++ toolchain — a timed-out
    /// compile must not leak child processes). Output reads are drained with a
    /// bound after exit: a child that inherited the pipe handles must not hang
    /// the drain forever. Timeout failures include whatever partial output was
    /// captured.
    /// </summary>
    public static (int ExitCode, string StdOut, string StdErr) RunProcess(
        string fileName, string[] args, string workingDir, int timeoutMs)
    {
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = workingDir,
            }
        };
        foreach (var a in args) process.StartInfo.ArgumentList.Add(a);
        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(timeoutMs))
        {
            // Kill the whole tree — otherwise a timed-out compile leaks
            // cl.exe/clang++/dotnet children.
            try { process.Kill(entireProcessTree: true); } catch { }
            BoundedDrain(stdoutTask, stderrTask);
            Assert.Fail($"process timed out after {timeoutMs / 1000}s: {fileName} {string.Join(" ", args)}\n" +
                        $"Partial STDOUT:\n{DrainedOrNote(stdoutTask)}\nPartial STDERR:\n{DrainedOrNote(stderrTask)}");
        }

        // The process exited, but a grandchild that inherited the pipe handles
        // can keep ReadToEndAsync alive indefinitely — bound the drain.
        BoundedDrain(stdoutTask, stderrTask);
        return (process.ExitCode, DrainedOrNote(stdoutTask), DrainedOrNote(stderrTask));
    }

    private static void BoundedDrain(params Task[] reads)
    {
        // WaitAll rethrows a faulted read as AggregateException — swallow it
        // here; DrainedOrNote reports any non-completed read as a note instead.
        try { Task.WaitAll(reads, 10_000); } catch (AggregateException) { }
    }

    private static string DrainedOrNote(Task<string> read)
        => read.Status == TaskStatus.RanToCompletion
            ? read.Result
            : "<output not fully captured: pipe still open, likely inherited by a surviving child process>";
}
