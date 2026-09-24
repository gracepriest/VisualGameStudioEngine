using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.MSIL;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using BasicLang.Compiler.SemanticAnalysis;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Msil;

/// <summary>
/// THE MSIL backend harness: BasicLang source → <c>.il</c> → <c>ilasm</c> → a real process →
/// its stdout.
///
/// <para><b>Why a round trip and not a string comparison.</b> The MSIL backend had no tests at
/// all — 2,136 lines of generator, nothing exercising it — and the first probe of it found a
/// <c>Select Case</c> that assembles, runs, and returns the WRONG ANSWER: it emits no
/// comparison and no conditional branch, so every case falls to <c>Else</c>. A test asserting
/// on emitted IL text would have had to already know that <c>beq</c> was missing to catch that.
/// Running the program catches it without knowing anything.</para>
///
/// <para><b>Three verdicts, deliberately distinguished</b> (<see cref="MsilOutcome"/>). "It
/// didn't work" is not one failure: <c>ILASM-FAIL</c> means the generator emitted text that is
/// not IL, <c>RUN-FAIL</c> means it emitted well-formed IL that is not a valid program, and a
/// wrong <c>RUNS</c> output means it emitted a valid program that computes the wrong thing.
/// They have different causes and different fixes, and collapsing them loses the diagnosis.</para>
///
/// <para><b>Out of process, with a timeout</b>, unlike <c>CSharpRun</c> which loads the
/// assembly in-process. Reaching backend parity means generating a great deal of invalid IL
/// along the way; an <c>InvalidProgramException</c>, a stack overflow or a runaway loop in
/// generated code must fail one test rather than take down the test host.</para>
/// </summary>
internal static class MsilHarness
{
    /// <summary>What happened when the generated program was taken all the way to a process.</summary>
    internal enum MsilOutcome
    {
        /// <summary>The generator refused or crashed — no <c>.il</c> to assemble.</summary>
        GenerateFailed,

        /// <summary><c>ilasm</c> rejected the text: the generator did not emit valid IL.</summary>
        AssembleFailed,

        /// <summary>Assembled, but the CLR rejected or the program threw.</summary>
        RunFailed,

        /// <summary>Ran to completion. <see cref="MsilRun.Output"/> is what it printed.</summary>
        Ran,
    }

    internal sealed record MsilRun(MsilOutcome Outcome, string Output, string Il, string Detail)
    {
        /// <summary>Everything a failure message needs, without the caller assembling it.</summary>
        internal string Report =>
            $"{Outcome}: {Detail}".TrimEnd(':', ' ') + "\n--- generated IL ---\n" + Il;
    }

    // ====================================================================================
    // Locating ilasm.
    // ====================================================================================

    private static readonly Lazy<string> IlasmPath = new(ProbeForIlasm);

    /// <summary>
    /// Where <c>ilasm</c> might be, best first. Mirrors <c>CppCompile.FindRunCompiler</c>'s
    /// contract: return null rather than throw, and let callers
    /// <see cref="Assert.Ignore(string)"/> — a machine without the tool must skip these tests,
    /// not fail them.
    ///
    /// <para><b>Windows needs no setup.</b> <c>%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\
    /// ilasm.exe</c> ships with the OS, and the generated IL already references
    /// <c>mscorlib 4:0:0:0</c> under the Framework public key token, so that pairing is the
    /// natural one. Elsewhere the CoreCLR build comes from the
    /// <c>runtime.&lt;rid&gt;.Microsoft.NETCore.ILAsm</c> NuGet package; a developer who wants
    /// these tests on Linux restores it once and it is found in the package cache.</para>
    /// </summary>
    private static string ProbeForIlasm()
    {
        // 1. An explicit override always wins — a dev pointing at a specific build.
        var overridden = Environment.GetEnvironmentVariable("BASICLANG_ILASM");
        if (!string.IsNullOrWhiteSpace(overridden) && File.Exists(overridden)) return overridden;

        // 2. Windows' in-box Framework assembler.
        var windir = Environment.GetEnvironmentVariable("WINDIR");
        if (!string.IsNullOrEmpty(windir))
        {
            foreach (var fx in new[] { "Framework64", "Framework" })
            {
                var p = Path.Combine(windir, "Microsoft.NET", fx, "v4.0.30319", "ilasm.exe");
                if (File.Exists(p)) return p;
            }
        }

        // 3. The NuGet package cache, newest version first.
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var packages = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
            ?? Path.Combine(home, ".nuget", "packages");
        if (Directory.Exists(packages))
        {
            var rid = OperatingSystem.IsWindows() ? "win-x64"
                : OperatingSystem.IsMacOS() ? "osx-x64" : "linux-x64";
            var root = Path.Combine(packages, $"runtime.{rid}.microsoft.netcore.ilasm");
            if (Directory.Exists(root))
            {
                var exe = OperatingSystem.IsWindows() ? "ilasm.exe" : "ilasm";
                var hit = Directory.EnumerateDirectories(root)
                    .OrderByDescending(d => d, StringComparer.Ordinal)
                    .Select(v => Path.Combine(v, "runtimes", rid, "native", exe))
                    .FirstOrDefault(File.Exists);
                if (hit != null) return hit;
            }
        }

        // 4. Bare PATH.
        foreach (var name in new[] { "ilasm", "ilasm.exe" })
        {
            try
            {
                using var probe = Process.Start(new ProcessStartInfo(name)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                if (probe != null) { probe.WaitForExit(10_000); return name; }
            }
            catch { /* not on PATH */ }
        }

        return null;
    }

    /// <summary>
    /// The assembler, or an Ignore for a machine that has none.
    ///
    /// <para>⛔ Most MSIL legs run INSIDE a cross-backend <c>Assert.Multiple</c>, where NUnit
    /// refuses <c>Assert.Ignore</c> outright ("Assert.Ignore may not be used in a multiple
    /// assertion block") — so on a machine without ilasm every such test FAILED instead of being
    /// skipped. Inside a block the skip is therefore raised as a bare <c>IgnoreException</c>,
    /// which NUnit records as Ignored. It must not hide a real failure from a leg that already
    /// ran in the same block: if one is pending, those failures are thrown instead.</para>
    /// </summary>
    internal static string RequireIlasm()
    {
        if (IlasmPath.Value == null)
        {
            const string message =
                "No ilasm found. Windows ships one at %WINDIR%\\Microsoft.NET\\Framework64\\"
                + "v4.0.30319; elsewhere restore runtime.<rid>.Microsoft.NETCore.ILAsm, or point "
                + "BASICLANG_ILASM at a build.";

            // NUnit 4 keeps the "am I inside Assert.Multiple" level internal, so ask Assert.Ignore:
            // outside a block it throws IgnoreException (let it go); inside one it throws a plain
            // Exception refusing to run, which is the case handled below.
            try
            {
                Assert.Ignore(message);
            }
            catch (Exception ex) when (ex is not IgnoreException)
            {
                var result = NUnit.Framework.Internal.TestExecutionContext.CurrentContext.CurrentResult;
                if (result.PendingFailures > 0)
                    throw new MultipleAssertException(result);
                throw new IgnoreException(message);
            }
        }
        return IlasmPath.Value;
    }

    // ====================================================================================
    // BasicLang → IL.
    // ====================================================================================

    /// <summary>
    /// Source → generated IL, through the SAME seams the CLI's <c>--target=msil</c> uses.
    ///
    /// <para><b>The optimizer runs by default</b>, because the repo's standing rule is that
    /// codegen is validated through the optimizer and not only through a non-optimizing helper
    /// — the C++ backend shipped an entire class of optimizer-only bugs behind 2,300 green
    /// tests that skipped it. A backend aiming at parity should not repeat that.</para>
    /// </summary>
    /// <param name="aggressive">
    /// Run <c>AddAggressivePasses()</c> instead of <c>AddStandardPasses()</c> — the MSIL leg of
    /// <c>FourBackends.RunsOnEveryBackendAggressive</c>, through the suite's single definition of
    /// aggressive (<c>AggressivePipeline.Apply</c>). Ignored when <paramref name="optimize"/> is
    /// false.
    ///
    /// <para>⭐ <b>SAFE FOR LOOPS SINCE ADR-0003 — this used to say it was not.</b> It assembled a
    /// program that ran a counted <c>For</c> ZERO times: issue #114, the same
    /// <c>LoopInvariantCodeMotionPass</c> condition-sinking that broke the C++ leg, for the same
    /// reason — MSIL emits the CFG as labels and branches and cannot recover a condition that
    /// moved into the latch. ADR-0003 unregisters all three loop passes. RE-MEASURED on the
    /// 13-shape CFG corpus: every loop shape now assembles, runs the right number of iterations
    /// and prints what the non-aggressive path prints — see
    /// <c>VisualGameStudio.Tests.Compiler.CfgLoopShapesAggressiveTests</c>.</para>
    /// </param>
    internal static string CompileToIl(string source, string moduleName = "MsilProbe",
        bool optimize = true, bool aggressive = false)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty,
            "parse errors:\n" + string.Join("\n", parser.Errors.Select(e => e.Message)));

        var analyzer = new SemanticAnalyzer();
        Assert.That(analyzer.Analyze(ast), Is.True,
            "semantic errors:\n" + string.Join("\n", analyzer.Errors.Select(e => e.ToString())));

        var module = new IRBuilder(analyzer).Build(ast, moduleName);

        if (optimize && aggressive)
        {
            VisualGameStudio.Tests.Compiler.AggressivePipeline.Apply(module);
        }
        else if (optimize)
        {
            var pipeline = new OptimizationPipeline();
            pipeline.AddStandardPasses();
            pipeline.Run(module);
        }

        return new MSILCodeGenerator().Generate(module);
    }

    // ====================================================================================
    // IL → a running process.
    // ====================================================================================

    /// <summary>
    /// The whole round trip. Never throws for a BACKEND failure — the outcome is the result,
    /// so a test can pin "this shape does not work yet" as precisely as it pins one that does.
    /// </summary>
    internal static MsilRun Run(string source, string moduleName = "MsilProbe", string stdin = null,
        bool aggressive = false)
    {
        RequireIlasm();

        string il;
        try { il = CompileToIl(source, moduleName, aggressive: aggressive); }
        catch (Exception ex) { return new MsilRun(MsilOutcome.GenerateFailed, "", "", ex.Message); }

        return RunIl(il, moduleName, stdin);
    }

    /// <summary>
    /// The IL → process half on its own, for IL generated from a COMBINED multi-file module
    /// (which <see cref="Run"/>'s single-source front half cannot produce).
    /// </summary>
    internal static MsilRun RunIl(string il, string moduleName = "MsilProbe", string stdin = null)
    {
        var ilasm = RequireIlasm();

        var dir = Path.Combine(Path.GetTempPath(), "blmsil-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var ilPath = Path.Combine(dir, moduleName + ".il");
            var exePath = Path.Combine(dir, moduleName + ".exe");
            File.WriteAllText(ilPath, il);

            var asm = Exec(ilasm, $"\"{ilPath}\" -exe -output=\"{exePath}\"", dir);
            if (asm.ExitCode != 0 || !File.Exists(exePath))
            {
                var why = (asm.Output + asm.Error)
                    .Split('\n')
                    .FirstOrDefault(l => l.Contains("error", StringComparison.OrdinalIgnoreCase))
                    ?.Trim() ?? "ilasm exited " + asm.ExitCode;
                return new MsilRun(MsilOutcome.AssembleFailed, "", il, why);
            }

            // A generated assembly needs a runtimeconfig to be launched by the shared host. It
            // is written beside the exe rather than baked into the generator: the .il is the
            // backend's artifact, and a host config file is not part of what MSIL means.
            File.WriteAllText(
                Path.Combine(dir, moduleName + ".runtimeconfig.json"),
                "{\"runtimeOptions\":{\"tfm\":\"net8.0\",\"framework\":"
                + "{\"name\":\"Microsoft.NETCore.App\",\"version\":\"8.0.0\"}}}");

            var run = Exec("dotnet", $"\"{exePath}\"", dir, stdin);
            var combined = (run.Output + run.Error).Replace("\r\n", "\n");

            // The CLR reports a rejected program on stderr with a zero-ish exit in some hosts,
            // so the TEXT is the oracle, not just the code.
            if (combined.Contains("Unhandled exception", StringComparison.OrdinalIgnoreCase)
                || combined.Contains("InvalidProgramException", StringComparison.Ordinal)
                || combined.Contains("BadImageFormat", StringComparison.Ordinal))
            {
                return new MsilRun(MsilOutcome.RunFailed, combined, il,
                    combined.Split('\n').FirstOrDefault()?.Trim() ?? "");
            }

            return new MsilRun(MsilOutcome.Ran, run.Output.Replace("\r\n", "\n"), il, "");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* temp */ }
        }
    }

    /// <summary>The round trip, asserting it ran and returning what it printed.</summary>
    internal static string RunExpectingSuccess(
        string source, string moduleName = "MsilProbe", string stdin = null)
    {
        var r = Run(source, moduleName, stdin);
        Assert.That(r.Outcome, Is.EqualTo(MsilOutcome.Ran), r.Report);
        return r.Output;
    }

    /// <summary>
    /// The round trip through the AGGRESSIVE pipeline, asserting it ran. The MSIL leg of
    /// <c>FourBackends.RunsOnEveryBackendAggressive</c>. See <see cref="CompileToIl"/>'s
    /// <c>aggressive</c> parameter — the #114 loop caveat it used to carry is closed by ADR-0003.
    /// </summary>
    internal static string RunAggressiveExpectingSuccess(
        string source, string moduleName = "MsilProbe", string stdin = null)
    {
        var r = Run(source, moduleName, stdin, aggressive: true);
        Assert.That(r.Outcome, Is.EqualTo(MsilOutcome.Ran), r.Report);
        return r.Output;
    }

    /// <summary><see cref="RunIl"/>, asserting it ran and returning what it printed.</summary>
    internal static string RunIlExpectingSuccess(string il, string moduleName = "MsilProbe")
    {
        var r = RunIl(il, moduleName);
        Assert.That(r.Outcome, Is.EqualTo(MsilOutcome.Ran), r.Report);
        return r.Output;
    }

    private sealed record ExecResult(int ExitCode, string Output, string Error);

    /// <param name="stdin">
    /// Text to feed the program, for a shape that reads input (<c>Console.ReadLine</c>). Always
    /// CLOSED after writing, even when null: a program that reads with nothing on the pipe must
    /// see end-of-input and return null rather than block until the 30s timeout.
    /// </param>
    private static ExecResult Exec(string exe, string args, string workingDir, string stdin = null)
    {
        using var p = Process.Start(new ProcessStartInfo(exe, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDir,
        });
        Assert.That(p, Is.Not.Null, $"could not start {exe}");

        if (!string.IsNullOrEmpty(stdin)) p!.StandardInput.Write(stdin);
        p!.StandardInput.Close();

        // Read both pipes before waiting: a program that fills one while we block on the other
        // deadlocks, and generated code is exactly the thing that produces surprising output.
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();

        if (!p.WaitForExit(30_000))
        {
            try { p.Kill(entireProcessTree: true); } catch { /* already gone */ }
            return new ExecResult(-1, "", "TIMEOUT after 30s");
        }

        return new ExecResult(p.ExitCode, stdout.Result, stderr.Result);
    }
}
