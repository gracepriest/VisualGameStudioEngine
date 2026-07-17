using System.Diagnostics;
using NUnit.Framework;
using VisualGameStudio.Core.Utilities;

namespace VisualGameStudio.Tests.Core;

[TestFixture]
public class TrailingEdgeDebouncerTests
{
    // Short quiet period keeps the tests fast; every positive wait is a bounded poll
    // (WaitBound) and every negative assertion uses a settle window several times the
    // quiet period, so timing jitter on a loaded machine cannot flip a result.
    private static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(75);
    private static readonly TimeSpan SettleWindow = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan WaitBound = TimeSpan.FromSeconds(5);

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < timeout)
        {
            if (condition()) return true;
            await Task.Delay(10);
        }
        return condition();
    }

    [Test]
    public async Task Signal_FiresOnceAfterTheQuietPeriod()
    {
        int fireCount = 0;
        using var debouncer = new TrailingEdgeDebouncer(QuietPeriod, () => Interlocked.Increment(ref fireCount));

        debouncer.Signal();

        Assert.That(await WaitUntilAsync(() => Volatile.Read(ref fireCount) >= 1, WaitBound),
            Is.True, "expected the fire callback within the wait bound");
        await Task.Delay(SettleWindow);
        Assert.That(Volatile.Read(ref fireCount), Is.EqualTo(1),
            "a single signal must produce exactly one fire");
    }

    [Test]
    public async Task Burst_CoalescesToOneTrailingFire()
    {
        int fireCount = 0;
        var clock = Stopwatch.StartNew();
        long fireAtMs = -1;
        using var debouncer = new TrailingEdgeDebouncer(QuietPeriod, () =>
        {
            Interlocked.Exchange(ref fireAtMs, clock.ElapsedMilliseconds);
            Interlocked.Increment(ref fireCount);
        });

        // 5 signals ~10ms apart — each one inside the previous quiet period.
        for (int i = 0; i < 4; i++)
        {
            debouncer.Signal();
            await Task.Delay(10);
        }
        debouncer.Signal();
        long lastSignalAtMs = clock.ElapsedMilliseconds;

        Assert.That(await WaitUntilAsync(() => Volatile.Read(ref fireCount) >= 1, WaitBound),
            Is.True, "expected the trailing fire within the wait bound");
        await Task.Delay(SettleWindow);
        Assert.That(Volatile.Read(ref fireCount), Is.EqualTo(1),
            "a burst must coalesce to exactly one trailing fire");
        Assert.That(Interlocked.Read(ref fireAtMs), Is.GreaterThanOrEqualTo(lastSignalAtMs),
            "the one fire must come AFTER the last signal of the burst (trailing edge)");
    }

    [Test]
    public async Task SignalAfterAFire_ArmsAgain()
    {
        int fireCount = 0;
        using var debouncer = new TrailingEdgeDebouncer(QuietPeriod, () => Interlocked.Increment(ref fireCount));

        debouncer.Signal();
        Assert.That(await WaitUntilAsync(() => Volatile.Read(ref fireCount) == 1, WaitBound),
            Is.True, "first cycle must fire");

        debouncer.Signal();
        Assert.That(await WaitUntilAsync(() => Volatile.Read(ref fireCount) == 2, WaitBound),
            Is.True, "the debouncer must re-arm: a signal after a fire starts a new cycle");
    }

    [Test]
    public async Task Dispose_CancelsThePendingFire()
    {
        int fireCount = 0;
        var debouncer = new TrailingEdgeDebouncer(QuietPeriod, () => Interlocked.Increment(ref fireCount));

        debouncer.Signal();
        debouncer.Dispose();

        bool fired = await WaitUntilAsync(() => Volatile.Read(ref fireCount) > 0, SettleWindow);
        Assert.That(fired, Is.False, "Dispose must CANCEL the pending fire, not flush it");

        // Signal after dispose is a documented no-op.
        debouncer.Signal();
        fired = await WaitUntilAsync(() => Volatile.Read(ref fireCount) > 0, SettleWindow);
        Assert.That(fired, Is.False, "Signal after Dispose must be a no-op");
    }

    [Test]
    public void Callback_RunsOffTheCallersThread()
    {
        int callbackThreadId = -1;
        using var done = new ManualResetEventSlim(false);
        using var debouncer = new TrailingEdgeDebouncer(QuietPeriod, () =>
        {
            callbackThreadId = Environment.CurrentManagedThreadId;
            done.Set();
        });

        int callerThreadId = Environment.CurrentManagedThreadId;
        var clock = Stopwatch.StartNew();
        debouncer.Signal();
        var signalReturnTime = clock.Elapsed;

        Assert.That(signalReturnTime, Is.LessThan(QuietPeriod),
            "Signal must return immediately, not block for the quiet period");
        Assert.That(done.Wait(WaitBound), Is.True, "expected the fire callback within the wait bound");
        Assert.That(callbackThreadId, Is.Not.EqualTo(callerThreadId),
            "the fire callback must run off the signalling thread");
    }

    [Test]
    public async Task ThrowingCallback_DoesNotPreventLaterCycles()
    {
        int fireCount = 0;
        using var debouncer = new TrailingEdgeDebouncer(QuietPeriod, () =>
        {
            int n = Interlocked.Increment(ref fireCount);
            if (n == 1) throw new InvalidOperationException("boom");
        });

        debouncer.Signal();
        Assert.That(await WaitUntilAsync(() => Volatile.Read(ref fireCount) == 1, WaitBound),
            Is.True, "first (throwing) fire must happen");

        debouncer.Signal();
        Assert.That(await WaitUntilAsync(() => Volatile.Read(ref fireCount) == 2, WaitBound),
            Is.True, "a throwing fire must not prevent later cycles");
    }
}
