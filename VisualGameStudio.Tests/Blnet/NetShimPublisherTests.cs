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
}
