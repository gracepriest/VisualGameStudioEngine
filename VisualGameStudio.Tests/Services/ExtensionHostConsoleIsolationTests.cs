using System;
using System.IO;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Services;

/// <summary>
/// Guards the extension host against an extension's own <c>console.log</c> killing it.
///
/// <para>The JSON-RPC channel IS stdout: <c>rpc.js:38</c> does
/// <c>process.stdout.write(header + body)</c>. Node's <c>console.log</c> writes to that same
/// stream, so an extension that logs emits raw text into the middle of a framed message. The C#
/// side can no longer parse the frame, the connection drops, stdin ends, and
/// <c>rpc.js:217</c>'s <c>process.stdin.on('end', () =&gt; process.exit(0))</c> fires — so the host
/// dies with exit code ZERO and the failure reads as a clean shutdown rather than a crash.</para>
///
/// <para>This was found by a probe extension whose <c>activate()</c> logged one line. It is not a
/// probe defect: <c>console.log</c> is the single most common thing extension code does, and real
/// VS Code redirects console inside its extension host for precisely this reason. Any extension
/// that logs during activation takes the host down with it.</para>
/// </summary>
[TestFixture]
public class ExtensionHostConsoleIsolationTests
{
    private static string MainJs()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(
                dir.FullName, "VisualGameStudio.ProjectSystem", "Services", "ExtensionHost", "main.js");
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            dir = dir.Parent;
        }
        Assert.Fail("could not locate ExtensionHost/main.js");
        return "";
    }

    [Test]
    public void TheHostRedirectsConsoleAwayFromStdout()
    {
        Assert.That(MainJs(), Does.Contain("console.log ="),
            "the host must replace console.log before any extension runs; stdout is the RPC "
            + "channel, so an extension logging one line corrupts the frame and kills the host");
    }

    /// <summary>
    /// Every console method an extension might reach for has to be covered. Leaving one unshimmed
    /// leaves one way to kill the host — and <c>console.error</c> is the one an extension is most
    /// likely to call from a catch block, i.e. exactly when something is already going wrong.
    /// </summary>
    [TestCase("console.log =")]
    [TestCase("console.info =")]
    [TestCase("console.warn =")]
    [TestCase("console.error =")]
    [TestCase("console.debug =")]
    [TestCase("console.trace =")]
    public void EveryConsoleMethodIsRedirected(string assignment)
    {
        Assert.That(MainJs(), Does.Contain(assignment),
            $"'{assignment}' is unshimmed, so an extension calling it writes raw text into the "
            + "JSON-RPC stream");
    }

    /// <summary>
    /// The ordering is the whole point. The shim must be installed before any extension module can
    /// be required, or the first extension to log during load still kills the host.
    /// </summary>
    [Test]
    public void TheRedirectIsInstalledBeforeAnyExtensionCanLoad()
    {
        var src = MainJs();

        var shim = src.IndexOf("console.log =", StringComparison.Ordinal);
        var activate = src.IndexOf("async function activateExtension", StringComparison.Ordinal);

        Assert.That(shim, Is.GreaterThanOrEqualTo(0), "no console shim at all");
        Assert.That(activate, Is.GreaterThanOrEqualTo(0), "premise: activateExtension still exists");
        Assert.That(shim, Is.LessThan(activate),
            "the console shim must be installed before the extension loader is even defined — a "
            + "redirect that happens after the first require is a redirect that happens too late");
    }
}
