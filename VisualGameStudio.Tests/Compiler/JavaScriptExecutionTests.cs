using System.Diagnostics;
using System.IO;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// THE tier that proves the JavaScript backend works.
///
/// <para>Text assertions on generated JS prove only that the generator emitted the text
/// the test author expected — they cannot tell correct lowering from lowering that
/// compiles and behaves wrongly. Every currently-open C++ backend defect is exactly that
/// shape (ByRef dropped, arrays unsized, copy-prop ignoring writes), and every one of
/// them would sail through a text assertion. So: compile, RUN under Node, assert on what
/// it actually printed.</para>
///
/// <para>Marked Integration because it spawns a process, matching the C++ compile/run
/// tests. It is excluded from the fast subset and MUST be run before any completion
/// claim.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class JavaScriptExecutionTests
{
    /// <summary>Compile BasicLang → JS, run it under Node, return trimmed stdout.</summary>
    internal static string RunJs(string basicLangSource)
    {
        var node = BasicLang.Runtime.NodeLocator.Find();
        if (node == null)
            Assert.Ignore("Node.js not found — the JS execution tier cannot run on this machine.");

        var js = JsTestSupport.Compile(basicLangSource);

        var dir = Path.Combine(Path.GetTempPath(), "BasicLang_JsExec_" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            // .mjs, so Node parses it as an ES module regardless of any package.json
            // in a parent directory of the temp path.
            var file = Path.Combine(dir, "program.mjs");
            File.WriteAllText(file, js);

            var psi = new ProcessStartInfo(node!)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            // ArgumentList, not a hand-quoted Arguments string: the runtime escapes each
            // entry, so a temp path containing a space or a quote cannot split the command.
            psi.ArgumentList.Add(file);

            using var p = Process.Start(psi)!;

            // ⛔ ASYNC reads, then wait, then KILL on timeout. The obvious ordering —
            // ReadToEnd() before WaitForExit — makes the timeout unreachable: the read blocks
            // until the child closes the pipe, so a wedged node hangs the test run forever and
            // WaitForExit is never even called. This repo has already had Node wedge machine-
            // wide, and the same trap is documented in BlnetConformanceTests.
            var stdoutTask = p.StandardOutput.ReadToEndAsync();
            var stderrTask = p.StandardError.ReadToEndAsync();

            if (!p.WaitForExit(30000))
            {
                try { p.Kill(entireProcessTree: true); } catch { }
                Assert.Fail($"node did not exit within 30s.\n--- generated JS ---\n{js}");
            }

            var stdout = stdoutTask.GetAwaiter().GetResult();
            var stderr = stderrTask.GetAwaiter().GetResult();

            Assert.That(p.ExitCode, Is.Zero,
                $"node exited {p.ExitCode}.\n--- stderr ---\n{stderr}\n--- generated JS ---\n{js}");

            return stdout.Trim();
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* best effort */ }
        }
    }

    [Test]
    public void HelloWorld_ActuallyRuns()
    {
        Assert.That(RunJs("Sub Main()\nConsole.WriteLine(\"Hello\")\nEnd Sub"), Is.EqualTo("Hello"));
    }

    [Test]
    public void UserFunction_IsCalled()
    {
        Assert.That(
            RunJs("Sub Greet()\nConsole.WriteLine(\"hi\")\nEnd Sub\nSub Main()\nGreet()\nEnd Sub"),
            Is.EqualTo("hi"));
    }

    /// <summary>
    /// Guards the tier itself. Assert.Ignore when Node is missing keeps the suite usable
    /// on a machine without Node, but a silently-skipped tier reads as green — the same
    /// false-green shape as the raylib DLL staging trap. If Node IS present, every test
    /// here must actually have run.
    /// </summary>
    [Test]
    public void ExecutionTier_IsNotSilentlySkipped()
    {
        Assert.That(BasicLang.Runtime.NodeLocator.Find(), Is.Not.Null,
            "Node.js was not found, so the entire JS execution tier is skipping. That reads " +
            "as green while proving nothing. Install Node.js — this tier is load-bearing for " +
            "the JavaScript backend.");
    }
}
