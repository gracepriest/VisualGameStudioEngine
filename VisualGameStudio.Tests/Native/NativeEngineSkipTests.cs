using System.Runtime.InteropServices;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// The skip reasons the native engine fixtures report when a P/Invoke cannot bind.
///
/// <para><b>Not an Integration fixture on purpose.</b> These assert the DECISION, not a native
/// call, so they run everywhere and in the fast subset. That matters because the decision is
/// platform-dependent: only one of its three branches can execute on any given host, so observing
/// a skip on Linux says nothing about what a Windows developer would be told. Feeding the
/// environment in as arguments is what makes all three checkable at once.</para>
/// </summary>
[TestFixture]
public class NativeEngineSkipTests
{
    private const string Dll = "VisualGameStudioEngine.dll";
    private const string Staged = "/somewhere/bin/VisualGameStudioEngine.dll";

    /// <summary>
    /// ⛔ The message this whole change exists for. On a non-Windows host the library is a Windows
    /// PE that cannot load however it is staged, so the old "not staged … refresh IDE\" line sent
    /// the reader to fix something that was not broken and never said the one true thing.
    /// </summary>
    [Test]
    public void OnANonWindowsHost_ItNamesThePlatform_AndDoesNotBlameStaging()
    {
        var reason = NativeEngineSkip.DllNotFoundReason(
            Dll, isWindows: false, stagedPath: Staged, isStaged: false, osDescription: "Ubuntu 24.04.4 LTS");

        Assert.Multiple(() =>
        {
            Assert.That(reason, Does.Contain("Ubuntu 24.04.4 LTS"),
                "it must name the platform actually running: " + reason);
            Assert.That(reason, Does.Contain("only run on Windows"),
                "and say what the real constraint is: " + reason);
            Assert.That(reason, Does.Not.Contain("refresh IDE"),
                "and must NOT give staging advice that cannot help here: " + reason);
        });
    }

    /// <summary>
    /// The case the old message was written for, and the one it got right — kept working.
    /// </summary>
    [Test]
    public void OnWindowsWithTheDllMissing_ItBlamesStaging_AndNamesThePath()
    {
        var reason = NativeEngineSkip.DllNotFoundReason(
            Dll, isWindows: true, stagedPath: Staged, isStaged: false, osDescription: "Windows 11");

        Assert.Multiple(() =>
        {
            Assert.That(reason, Does.Contain("not staged"), reason);
            Assert.That(reason, Does.Contain(Staged),
                "naming the path it looked at saves a round of guessing: " + reason);
            Assert.That(reason, Does.Contain("refresh IDE"), reason);
        });
    }

    /// <summary>
    /// ⛔ The third case, which the old single message actively mis-diagnosed: the file is sitting
    /// exactly where it belongs and the loader still refused it. "Not staged" sends the reader to
    /// stare at a file that is already correct.
    /// </summary>
    [Test]
    public void OnWindowsWithTheDllPresent_ItSaysTheLoaderRefusedIt_NotThatItIsMissing()
    {
        var reason = NativeEngineSkip.DllNotFoundReason(
            Dll, isWindows: true, stagedPath: Staged, isStaged: true, osDescription: "Windows 11");

        Assert.Multiple(() =>
        {
            Assert.That(reason, Does.Contain("IS present"), reason);
            Assert.That(reason, Does.Not.Contain("not staged"),
                "the file is where it belongs; blaming staging is the mis-diagnosis: " + reason);
            Assert.That(reason, Does.Contain("dependency").Or.Contain("mismatch"),
                "and it must point at the causes that actually produce this: " + reason);
        });
    }

    /// <summary>
    /// The live entry point agrees with the branch for THIS host — so the wiring from environment
    /// to decision is exercised too, not just the decision.
    /// </summary>
    [Test]
    public void TheLiveReason_MatchesThisHostsBranch()
    {
        var reason = NativeEngineSkip.DllNotFound(Dll);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.That(reason, Does.Contain("not staged").Or.Contain("IS present"), reason);
        }
        else
        {
            Assert.That(reason, Does.Contain("only run on Windows"),
                "a non-Windows host must get the platform message: " + reason);
        }
    }
}
