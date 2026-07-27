using System.Diagnostics;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Blnet;

/// <summary>
/// End-to-end ABI conformance (spec 2026-07-26, conformance suite): a native C++ harness
/// (<c>TestAssets/BlnetHarness/main.cpp.txt</c>) LoadLibrary's the Native-AOT published
/// shim, binds the contract exports by their <c>BLNET_EXPORT_*</c> names, initializes the
/// vtable handshake, and drives one scenario per child process (exit 0 + a
/// <c>PASS &lt;scenario&gt;</c> line = green; fresh process per scenario so shim static
/// state never leaks between scenarios).
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class BlnetConformanceTests
{
    private static string? _harnessExe;
    private static string? _workDir;

    [OneTimeSetUp]
    public void PublishAndCompileOnce()
    {
        if (!OperatingSystem.IsWindows()) Assert.Ignore("Windows-only (LoadLibrary + win-x64 AOT)");
        var compiler = Native.CppCompile.FindRunCompiler();
        if (compiler is null) Assert.Ignore("No C++ compiler available");
        var shimDll = BlnetShimPublisher.PublishOnce();

        _workDir = Path.Combine(Path.GetTempPath(), "blnet_conf_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDir);
        File.WriteAllText(Path.Combine(_workDir, "blnet.h"), BlnetRuntimeSources.BlnetHeader);
        File.WriteAllText(Path.Combine(_workDir, "blnet_runtime.hpp"), BlnetRuntimeSources.BlnetRuntime);
        var mainCpp = File.ReadAllText(Path.Combine(
            TestContext.CurrentContext.TestDirectory, "TestAssets", "BlnetHarness", "main.cpp.txt"));
        File.WriteAllText(Path.Combine(_workDir, "main.cpp"), mainCpp);
        _harnessExe = CompileHarness(compiler.Value, _workDir);
        File.Copy(shimDll, Path.Combine(_workDir, "BlnetTestShim.dll"));
    }

    [OneTimeTearDown]
    public void CleanUp()
    {
        // Best effort: the last scenario's exe/dll can stay briefly locked after Kill.
        if (_workDir is not null)
            try { Directory.Delete(_workDir, recursive: true); } catch { /* leave for temp cleanup */ }
    }

    /// <summary>
    /// Compiles <c>main.cpp</c> in <paramref name="workDir"/> to <c>harness.exe</c> using the
    /// probed compiler's args template (<c>{0}</c> = source, <c>{1}</c> = output exe — same
    /// formatting contract as <see cref="Native.CppCompile"/>). Quote-includes of blnet.h /
    /// blnet_runtime.hpp resolve because the compiler runs with <paramref name="workDir"/> as
    /// its working directory.
    /// </summary>
    private static string CompileHarness((string exe, string argsTemplate) compiler, string workDir)
    {
        var srcPath = Path.Combine(workDir, "main.cpp");
        var exePath = Path.Combine(workDir, "harness.exe");
        var args = string.Format(compiler.argsTemplate, srcPath, exePath);
        var r = Run(new ProcessStartInfo(compiler.exe, args) { WorkingDirectory = workDir }, 120_000);
        Assert.That(r.Exited, Is.True,
            $"harness compile TIMED OUT after 120 s — killed; partial output:\n{r.StdOut}\n{r.StdErr}");
        Assert.That(r.ExitCode, Is.EqualTo(0),
            $"harness compilation failed (exit {r.ExitCode}):\n{r.StdOut}\n{r.StdErr}");
        Assert.That(File.Exists(exePath), Is.True, $"compiler produced no executable at {exePath}");
        return exePath;
    }

    /// <summary>
    /// Runs one scenario in a fresh harness process (WorkingDirectory = the temp dir, so
    /// LoadLibrary("BlnetTestShim.dll") resolves next to the exe). The 60 s timeout is the
    /// hang detector for the later deadlock scenarios; on timeout the whole process tree is
    /// killed. Asserts exit code 0 and returns stdout.
    /// </summary>
    private static string RunScenario(string name)
    {
        Assert.That(_harnessExe, Is.Not.Null, "OneTimeSetUp did not produce a harness exe");
        var psi = new ProcessStartInfo(_harnessExe!) { WorkingDirectory = _workDir! };
        psi.ArgumentList.Add(name);
        var r = Run(psi, 60_000);
        Assert.That(r.Exited, Is.True,
            $"harness scenario '{name}' TIMED OUT after 60 s — killed (possible deadlock); partial output:\n{r.StdOut}\n{r.StdErr}");
        Assert.That(r.ExitCode, Is.EqualTo(0),
            $"harness scenario '{name}' exited with {r.ExitCode}:\n{r.StdOut}\n{r.StdErr}");
        return r.StdOut;
    }

    /// <summary>
    /// Run a child to completion capturing both streams, with a timeout that can ACTUALLY
    /// fire: both streams are read asynchronously so <c>WaitForExit(timeoutMs)</c> is a real
    /// hang detector (a synchronous <c>ReadToEnd()</c> would block until the child exits and
    /// the timeout could never trip — and this fixture's timeout IS the deadlock guard for
    /// the later scenarios). On timeout the whole process tree is killed; killing closes the
    /// pipes, so a bounded wait then collects whatever (partial) output had arrived.
    /// </summary>
    private static (bool Exited, int ExitCode, string StdOut, string StdErr) Run(
        ProcessStartInfo psi, int timeoutMs)
    {
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        using var proc = Process.Start(psi)!;
        var so = proc.StandardOutput.ReadToEndAsync();
        var se = proc.StandardError.ReadToEndAsync();
        bool exited = proc.WaitForExit(timeoutMs);
        if (!exited)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
            try { proc.WaitForExit(5000); } catch { /* best effort */ }
        }
        try { Task.WaitAll(new Task[] { so, se }, 10_000); } catch { /* partial output is fine */ }
        var stdout = so.IsCompletedSuccessfully ? so.Result : string.Empty;
        var stderr = se.IsCompletedSuccessfully ? se.Result : string.Empty;
        return (exited, exited ? proc.ExitCode : -1, stdout, stderr);
    }

    [TestCase("handle_roundtrip")]        // spec test 1
    [TestCase("stale_handle")]            // spec test 2
    [TestCase("double_release_addref")]   // spec test 3
    [TestCase("generation_reuse")]        // spec test 4
    [TestCase("string_roundtrip")]        // spec test 5
    [TestCase("managed_exception")]       // spec test 6
    [TestCase("inline_native_exception")]      // spec test 7
    [TestCase("inline_result_and_out")]        // spec test 8
    [TestCase("struct_slots")]                 // spec test 9
    [TestCase("queued_notification_addref")]   // spec test 10
    [TestCase("cross_thread_result_rejected")] // spec test 11
    [TestCase("pump_error_surfacing")]         // spec test 12
    [TestCase("deadlock_guard")]               // spec test 13
    public void Conformance(string scenario) =>
        Assert.That(RunScenario(scenario), Does.StartWith("PASS " + scenario));
}
