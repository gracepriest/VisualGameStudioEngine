using NUnit.Framework;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

/// <summary>
/// Pins the auto-restart budget policy: attempts are only refunded after a
/// connection survives the stability window, so a server that crashes shortly
/// after every reconnect runs out of attempts instead of restarting forever.
/// </summary>
[TestFixture]
public class RestartPolicyTests
{
    private static readonly DateTime T0 = new(2026, 7, 5, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public void FreshPolicy_HasFullBudget()
    {
        var policy = new RestartPolicy();

        Assert.That(policy.Attempts, Is.EqualTo(0));
        Assert.That(policy.CanAttempt, Is.True);
    }

    [Test]
    public void BeginAttempt_ConsumesBudget_WithExponentialBackoff()
    {
        var policy = new RestartPolicy();

        Assert.That(policy.BeginAttempt(), Is.EqualTo(TimeSpan.FromSeconds(1)));
        Assert.That(policy.BeginAttempt(), Is.EqualTo(TimeSpan.FromSeconds(2)));
        Assert.That(policy.BeginAttempt(), Is.EqualTo(TimeSpan.FromSeconds(4)));
        Assert.That(policy.Attempts, Is.EqualTo(3));
        Assert.That(policy.CanAttempt, Is.False, "budget exhausted after MaxAttempts");
    }

    [Test]
    public void CrashShortlyAfterReconnect_DoesNotRefundBudget()
    {
        // The restart-storm scenario: server reconnects, then crashes 5s
        // later (e.g. the same poisonous didOpen re-sent on every connect).
        var policy = new RestartPolicy();

        policy.BeginAttempt();                                  // restart 1
        policy.OnConnected(T0);                                 // reconnect OK
        policy.OnDisconnected(T0 + TimeSpan.FromSeconds(5));    // crash again

        Assert.That(policy.Attempts, Is.EqualTo(1), "an unstable reconnect must not reset the budget");

        policy.BeginAttempt();                                  // restart 2
        policy.OnConnected(T0 + TimeSpan.FromSeconds(10));
        policy.OnDisconnected(T0 + TimeSpan.FromSeconds(15));
        policy.BeginAttempt();                                  // restart 3

        Assert.That(policy.CanAttempt, Is.False,
            "the kill/spawn cycle must terminate at MaxAttempts instead of restarting forever");
    }

    [Test]
    public void StableConnection_RefundsBudgetOnDisconnect()
    {
        var policy = new RestartPolicy();
        policy.BeginAttempt();
        policy.BeginAttempt();

        policy.OnConnected(T0);
        policy.OnDisconnected(T0 + RestartPolicy.StabilityWindow);

        Assert.That(policy.Attempts, Is.EqualTo(0),
            "a connection that survived the stability window earns the budget back");
        Assert.That(policy.CanAttempt, Is.True);
    }

    [Test]
    public void DisconnectWithoutConnection_DoesNotRefund()
    {
        var policy = new RestartPolicy();
        policy.BeginAttempt();

        policy.OnDisconnected(T0 + TimeSpan.FromHours(1));

        Assert.That(policy.Attempts, Is.EqualTo(1));
    }

    [Test]
    public void SecondDisconnect_AfterUnstableOne_DoesNotRefundFromStaleTimestamp()
    {
        var policy = new RestartPolicy();
        policy.BeginAttempt();
        policy.OnConnected(T0);
        policy.OnDisconnected(T0 + TimeSpan.FromSeconds(5)); // unstable, no refund

        // A later spurious disconnect signal must not observe the stale
        // connection timestamp and refund by accident.
        policy.OnDisconnected(T0 + TimeSpan.FromMinutes(30));

        Assert.That(policy.Attempts, Is.EqualTo(1));
    }
}
