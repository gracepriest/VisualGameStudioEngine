using NUnit.Framework;
using BasicLang.Compiler.CodeGen.Net;

namespace VisualGameStudio.Tests.Blnet;

/// <summary>
/// Pins the publisher's environment hardening. The PATH workaround (spec §10.5) is the reason
/// the first native build on a machine with NoDefaultCurrentDirectoryInExePath=1 succeeds;
/// without it ILCompiler's linker discovery corrupts CppLinker and the build fails MSB3073
/// exit 123, looking like a P2a bug rather than an environment one.
/// </summary>
[TestFixture]
public class NetShimPublisherTests
{
    [Test]
    public void BuildPublishArguments_UsesTheProvenRecipe()
    {
        var args = NetShimPublisher.BuildPublishArguments("C:\\proj\\shim.csproj", "C:\\out", "win-x64");

        Assert.That(args, Is.EqualTo(new[]
        {
            "publish", "C:\\proj\\shim.csproj",
            "-c", "Release",
            "-r", "win-x64",
            "-p:PublishAot=true",
            "-p:NativeLib=Shared",
            "-o", "C:\\out",
        }), "Publish recipe drifted from the P0-proven one (spec §8.1) — update the spec too if this is intentional.");
    }

    [Test]
    public void HardenChildPath_AppendsVsInstallerWhenPresent()
    {
        var env = new Dictionary<string, string?> { ["PATH"] = "C:\\existing" };

        NetShimPublisher.HardenChildPath(env, vsInstallerDir: "C:\\VS\\Installer", installerExists: true);

        Assert.That(env["PATH"], Is.EqualTo("C:\\existing;C:\\VS\\Installer"));
    }

    [Test]
    public void HardenChildPath_LeavesPathAloneWhenInstallerMissing()
    {
        var env = new Dictionary<string, string?> { ["PATH"] = "C:\\existing" };

        NetShimPublisher.HardenChildPath(env, vsInstallerDir: "C:\\nope", installerExists: false);

        Assert.That(env["PATH"], Is.EqualTo("C:\\existing"));
    }

    /// <summary>
    /// ProcessStartInfo.Environment (the real caller) is a case-insensitive dictionary on
    /// Windows, so a "Path" entry must still be found and updated when looked up as "PATH".
    /// A plain case-sensitive Dictionary in the two tests above would not catch a regression
    /// back to the `env["PATH"]` indexer form, which happens to work there only by coincidence
    /// of the test's own key casing.
    /// </summary>
    [Test]
    public void HardenChildPath_IsCaseInsensitiveLikeProcessEnvironment()
    {
        var env = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["Path"] = "C:\\existing" };

        NetShimPublisher.HardenChildPath(env, vsInstallerDir: "C:\\VS\\Installer", installerExists: true);

        Assert.That(env["Path"], Is.EqualTo("C:\\existing;C:\\VS\\Installer"));
    }

    [Test]
    public void HardenChildPath_DoesNotThrowWhenPathKeyMissing()
    {
        var env = new Dictionary<string, string?>();

        Assert.DoesNotThrow(() => NetShimPublisher.HardenChildPath(env, vsInstallerDir: "C:\\VS\\Installer", installerExists: true));
        Assert.That(env.ContainsKey("PATH"), Is.False);
    }

    [Test]
    public void ExpectedDllPath_NamesTheDllAfterTheProject()
    {
        var dll = NetShimPublisher.ExpectedDllPath("C:\\proj\\BlnetTestShim.csproj", "C:\\out");

        Assert.That(dll, Is.EqualTo(Path.Combine("C:\\out", "BlnetTestShim.dll")));
    }

    [Test]
    public void BuildResult_SucceedsWhenExitZeroAndDllPresent()
    {
        var result = NetShimPublisher.BuildResult("C:\\proj\\Shim.csproj", "C:\\out", exitCode: 0, output: "ok", dllExists: true);

        Assert.That(result.Success, Is.True);
        Assert.That(result.DllPath, Is.EqualTo(Path.Combine("C:\\out", "Shim.dll")));
        Assert.That(result.Output, Is.EqualTo("ok"));
        Assert.That(result.ExitCode, Is.EqualTo(0));
    }

    [Test]
    public void BuildResult_FailsOnNonZeroExit_ButStillReportsExpectedDllPath()
    {
        var result = NetShimPublisher.BuildResult("C:\\proj\\Shim.csproj", "C:\\out", exitCode: 1, output: "MSB3073", dllExists: false);

        Assert.That(result.Success, Is.False);
        Assert.That(result.ExitCode, Is.EqualTo(1));
        Assert.That(result.DllPath, Is.EqualTo(Path.Combine("C:\\out", "Shim.dll")),
            "even on failure, DllPath should say where the shim was expected — callers (and the wrapper) rely on this instead of re-deriving the naming rule");
    }

    [Test]
    public void BuildResult_FailsWhenExitZeroButDllMissing_DistinguishableFromABuildFailureByExitCode()
    {
        var result = NetShimPublisher.BuildResult("C:\\proj\\Shim.csproj", "C:\\out", exitCode: 0, output: "clean build log", dllExists: false);

        Assert.That(result.Success, Is.False);
        Assert.That(result.ExitCode, Is.EqualTo(0), "the confusing case: a clean exit with no DLL — ExitCode==0 is what distinguishes it from a build failure");
        Assert.That(result.DllPath, Is.EqualTo(Path.Combine("C:\\out", "Shim.dll")));
    }
}
