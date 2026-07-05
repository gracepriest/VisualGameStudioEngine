using NUnit.Framework;
using VisualGameStudio.Core.Utilities;

namespace VisualGameStudio.Tests.Core;

[TestFixture]
public class CompletionRequestCoordinatorTests
{
    [Test]
    public void BeginRequest_ReturnsMonotonicallyIncreasingIds()
    {
        var coordinator = new CompletionRequestCoordinator();

        var (id1, _) = coordinator.BeginRequest();
        var (id2, _) = coordinator.BeginRequest();
        var (id3, _) = coordinator.BeginRequest();

        Assert.That(id2, Is.GreaterThan(id1));
        Assert.That(id3, Is.GreaterThan(id2));
    }

    [Test]
    public void BeginRequest_CancelsPreviousToken()
    {
        var coordinator = new CompletionRequestCoordinator();

        var (_, token1) = coordinator.BeginRequest();
        Assert.That(token1.IsCancellationRequested, Is.False);

        var (_, token2) = coordinator.BeginRequest();

        Assert.That(token1.IsCancellationRequested, Is.True);
        Assert.That(token2.IsCancellationRequested, Is.False);
    }

    [Test]
    public void IsCurrent_OnlyLatestRequestIsCurrent()
    {
        var coordinator = new CompletionRequestCoordinator();

        var (id1, _) = coordinator.BeginRequest();
        Assert.That(coordinator.IsCurrent(id1), Is.True);

        var (id2, _) = coordinator.BeginRequest();

        Assert.That(coordinator.IsCurrent(id1), Is.False, "stale request must not be current");
        Assert.That(coordinator.IsCurrent(id2), Is.True);
    }

    [Test]
    public void CancelAll_CancelsTokenAndInvalidatesLatestRequest()
    {
        var coordinator = new CompletionRequestCoordinator();

        var (id, token) = coordinator.BeginRequest();
        coordinator.CancelAll();

        Assert.That(token.IsCancellationRequested, Is.True);
        Assert.That(coordinator.IsCurrent(id), Is.False,
            "after CancelAll a late response must be detectable as stale");
    }

    [Test]
    public void IsCurrent_UnknownId_ReturnsFalse()
    {
        var coordinator = new CompletionRequestCoordinator();
        Assert.That(coordinator.IsCurrent(42), Is.False);
    }
}
