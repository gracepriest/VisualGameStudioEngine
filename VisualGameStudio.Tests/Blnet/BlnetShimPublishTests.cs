using NUnit.Framework;

namespace VisualGameStudio.Tests.Blnet;

[TestFixture]
[Category("Integration")]
public class BlnetShimPublishTests
{
    [Test]
    public void ShimPublishesUnderNativeAot()
    {
        if (!OperatingSystem.IsWindows()) Assert.Ignore("AOT shim conformance is Windows-only (LoadLibrary harness)");
        var dll = BlnetShimPublisher.PublishOnce();
        Assert.That(File.Exists(dll), dll);
    }

    [Test]
    public void ShimPublishHasNoAotAnalysisWarnings()
    {
        if (!OperatingSystem.IsWindows()) Assert.Ignore("AOT shim conformance is Windows-only (LoadLibrary harness)");
        BlnetShimPublisher.PublishOnce();
        var output = BlnetShimPublisher.PublishOutput;
        Assert.That(output, Does.Not.Contain("warning IL"));
    }
}
