using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Shared harness for spawned-CLI end-to-end tests: locates the real
/// BasicLang.exe deployed next to the tests and drives it as a child process.
/// Both <see cref="CppProjectCliBuildTests"/> and <see cref="MixedProjectBuildTests"/>
/// use this so the spawn/kill-tree/timeout policy lives in exactly one place.
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

    public static async Task<(int ExitCode, string StdOut, string StdErr)> RunCli(
        string workingDir, params string[] args)
    {
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = CliPath(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = workingDir,
            }
        };
        foreach (var a in args) process.StartInfo.ArgumentList.Add(a);
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromMinutes(5)).Token);
        }
        catch (OperationCanceledException)
        {
            // Kill the whole tree (BasicLang.exe spawns the C++ toolchain) —
            // otherwise a timed-out compile leaks cl.exe/clang++ processes.
            try { process.Kill(entireProcessTree: true); } catch { }
            Assert.Fail($"CLI timed out after 5 minutes: BasicLang.exe {string.Join(" ", args)}");
        }
        return (process.ExitCode, await stdout, await stderr);
    }
}
