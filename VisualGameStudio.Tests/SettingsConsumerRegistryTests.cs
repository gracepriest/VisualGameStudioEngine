using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.Tests;

/// <summary>
/// Pins the <see cref="SettingsConsumerRegistry"/> contract: registration, retrieval, idempotence,
/// multi-consumer combination, and thread-safety. The registry backs the Phase 3 contract test
/// that guards against "persists-but-dead" settings, so its own behavior must be exact.
///
/// Every test uses unique, dotted test keys ("test.*") so it never collides with the real editor /
/// workbench / terminal consumers other fixtures register in the same process.
/// </summary>
[TestFixture]
public class SettingsConsumerRegistryTests
{
    [Test]
    public void RegisterConsumer_ThenConsumers_ContainsKeyAndDescription()
    {
        var key = $"test.register.{System.Guid.NewGuid():N}";
        SettingsConsumerRegistry.RegisterConsumer(key, "MyConsumer → does a thing");

        Assert.That(SettingsConsumerRegistry.IsRegistered(key), Is.True);
        Assert.That(SettingsConsumerRegistry.Consumers, Contains.Key(key));
        Assert.That(SettingsConsumerRegistry.Consumers[key], Is.EqualTo("MyConsumer → does a thing"));
    }

    [Test]
    public void IsRegistered_UnknownKey_IsFalse()
    {
        var key = $"test.unknown.{System.Guid.NewGuid():N}";
        Assert.That(SettingsConsumerRegistry.IsRegistered(key), Is.False);
    }

    [Test]
    public void RegisterConsumer_SameKeyAndDescriptionTwice_IsIdempotent()
    {
        var key = $"test.idempotent.{System.Guid.NewGuid():N}";
        SettingsConsumerRegistry.RegisterConsumer(key, "Consumer A");
        SettingsConsumerRegistry.RegisterConsumer(key, "Consumer A");

        // No duplication: the combined description is still just the single entry.
        Assert.That(SettingsConsumerRegistry.Consumers[key], Is.EqualTo("Consumer A"));
    }

    [Test]
    public void RegisterConsumer_MultipleDistinctConsumersForOneKey_AreCombined()
    {
        var key = $"test.multi.{System.Guid.NewGuid():N}";
        SettingsConsumerRegistry.RegisterConsumer(key, "Consumer A");
        SettingsConsumerRegistry.RegisterConsumer(key, "Consumer B");

        var combined = SettingsConsumerRegistry.Consumers[key];
        Assert.That(combined, Does.Contain("Consumer A"));
        Assert.That(combined, Does.Contain("Consumer B"));
        Assert.That(combined, Is.EqualTo("Consumer A; Consumer B"),
            "distinct descriptions must be joined in registration order with '; '");
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public void RegisterConsumer_BlankKey_IsIgnored(string? key)
    {
        // Should not throw and should not create an empty entry.
        Assert.DoesNotThrow(() => SettingsConsumerRegistry.RegisterConsumer(key!, "desc"));
        if (key != null)
            Assert.That(SettingsConsumerRegistry.IsRegistered(key), Is.False);
    }

    [Test]
    public void RegisterConsumer_BlankDescription_RegistersKeyWithNoDescription()
    {
        var key = $"test.blankdesc.{System.Guid.NewGuid():N}";
        SettingsConsumerRegistry.RegisterConsumer(key, "");

        // The key counts as consumed (the contract test only needs a consumer to exist), even
        // though there is no human-readable description to combine.
        Assert.That(SettingsConsumerRegistry.IsRegistered(key), Is.True);
        Assert.That(SettingsConsumerRegistry.Consumers[key], Is.EqualTo(""));
    }

    [Test]
    public void Consumers_IsSnapshot_NotLiveView()
    {
        var key = $"test.snapshot.{System.Guid.NewGuid():N}";
        SettingsConsumerRegistry.RegisterConsumer(key, "first");
        var snapshot = SettingsConsumerRegistry.Consumers;

        SettingsConsumerRegistry.RegisterConsumer($"test.snapshot2.{System.Guid.NewGuid():N}", "second");

        // The previously-captured snapshot must not have grown.
        Assert.That(snapshot, Contains.Key(key));
        Assert.That(snapshot.Count(kvp => kvp.Key.StartsWith("test.snapshot2.")), Is.EqualTo(0));
    }

    [Test]
    public void RegisterConsumer_ConcurrentRegistrations_AreAllRecorded()
    {
        var keys = Enumerable.Range(0, 200)
            .Select(i => $"test.concurrent.{System.Guid.NewGuid():N}.{i}")
            .ToList();

        Parallel.ForEach(keys, k => SettingsConsumerRegistry.RegisterConsumer(k, $"consumer for {k}"));

        var consumers = SettingsConsumerRegistry.Consumers;
        Assert.That(keys.All(consumers.ContainsKey), Is.True,
            "every concurrently-registered key must be present (no lost updates under contention)");
    }
}
