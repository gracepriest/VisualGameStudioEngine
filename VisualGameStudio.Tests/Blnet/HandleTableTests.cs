using BlnetTestShim;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Blnet;

[TestFixture]
public class HandleTableTests
{
    [Test]
    public void Create_TryGet_RoundTrips()
    {
        var t = new HandleTable();
        var obj = new List<int> { 1, 2, 3 };
        var h = t.Create(obj);
        Assert.That(h, Is.Not.Zero);
        Assert.That(t.TryGet(h, out var got), Is.EqualTo(BlnetStatus.BLNET_OK));
        Assert.That(got, Is.SameAs(obj));
    }

    [Test]
    public void Release_ThenUse_IsStale_NotCorruption()
    {
        var t = new HandleTable();
        var h = t.Create(new object());
        Assert.That(t.Release(h), Is.EqualTo(BlnetStatus.BLNET_OK));
        Assert.That(t.TryGet(h, out _), Is.EqualTo(BlnetStatus.BLNET_E_STALE_HANDLE));
        Assert.That(t.Release(h), Is.EqualTo(BlnetStatus.BLNET_E_STALE_HANDLE)); // double release
    }

    [Test]
    public void AddRef_KeepsAlive_UntilLastRelease()
    {
        var t = new HandleTable();
        var h = t.Create(new object());
        Assert.That(t.AddRef(h), Is.EqualTo(BlnetStatus.BLNET_OK));
        Assert.That(t.Release(h), Is.EqualTo(BlnetStatus.BLNET_OK));
        Assert.That(t.TryGet(h, out _), Is.EqualTo(BlnetStatus.BLNET_OK)); // still alive
        Assert.That(t.Release(h), Is.EqualTo(BlnetStatus.BLNET_OK));
        Assert.That(t.TryGet(h, out _), Is.EqualTo(BlnetStatus.BLNET_E_STALE_HANDLE));
    }

    [Test]
    public void GenerationReuse_OldHandleStillFails()
    {
        var t = new HandleTable();
        var h1 = t.Create(new object());
        t.Release(h1);
        var h2 = t.Create(new object());       // reuses slot index 1
        Assert.That((uint)(h2 & 0xFFFFFFFF), Is.EqualTo((uint)(h1 & 0xFFFFFFFF)), "slot must be reused for this test to bite");
        Assert.That(t.TryGet(h1, out _), Is.EqualTo(BlnetStatus.BLNET_E_STALE_HANDLE));
        Assert.That(t.TryGet(h2, out _), Is.EqualTo(BlnetStatus.BLNET_OK));
    }

    [Test]
    public void ZeroHandle_IsAlwaysStale()
    {
        var t = new HandleTable();
        Assert.That(t.TryGet(0, out _), Is.EqualTo(BlnetStatus.BLNET_E_STALE_HANDLE));
    }

    [Test]
    public void Concurrency_ParallelCreateReleaseHammer_NoCorruption()
    {
        var t = new HandleTable();
        Parallel.For(0, 8, _ =>
        {
            for (int i = 0; i < 5_000; i++)
            {
                var h = t.Create(i);
                Assert.That(t.TryGet(h, out var v), Is.EqualTo(BlnetStatus.BLNET_OK));
                Assert.That(v, Is.EqualTo(i));
                Assert.That(t.Release(h), Is.EqualTo(BlnetStatus.BLNET_OK));
            }
        });
        Assert.That(t.AliveCount, Is.Zero);
    }
}
