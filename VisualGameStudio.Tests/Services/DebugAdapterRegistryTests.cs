using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

/// <summary>
/// Phase 4 Task 8 — the pluggable-debugger roster. The registry is pure bookkeeping
/// (it owns NO processes — DAP sessions are per-launch, owned by DebugService), so
/// these tests pin exactly its three contracts: duplicate ids are refused at the
/// door (the two-process orphan lesson from the LSP registry, ordinal compare),
/// GetFor scans in registration order filtered by Serves, and GetById is an ordinal
/// lookup with null for a miss.
/// </summary>
[TestFixture]
public class DebugAdapterRegistryTests
{
    /// <summary>CSharp-backend BasicLang project — the managed adapter's territory.</summary>
    private static BasicLangProject ManagedProject() => new() { Name = "ManagedApp" };

    /// <summary>C++-backend project — IsNativeBuild, lldb-dap's territory.</summary>
    private static BasicLangProject NativeProject() =>
        new() { Name = "NativeApp", TargetBackend = TargetBackend.Cpp };

    [Test]
    public void Register_DuplicateId_Throws()
    {
        // The LSP lesson: a second registration under one id is how the IDE ends up
        // with two adapter processes where nothing routes to (or disposes) the second.
        // The registry must refuse at the door, not at first use.
        var registry = new DebugAdapterRegistry();
        registry.Register(DebugAdapterDescriptor.BasicLangManaged(() => null));

        var duplicate = DebugAdapterDescriptor.BasicLangManaged(() => null);
        var ex = Assert.Throws<ArgumentException>(() => registry.Register(duplicate));

        Assert.That(ex!.Message, Does.Contain(DebugAdapterDescriptor.BasicLangManagedId),
            "the refusal must name the colliding id");
        Assert.That(registry.All, Has.Count.EqualTo(1),
            "the refused duplicate must not have been added");
    }

    [Test]
    public void GetFor_PicksTheFirstServingDescriptor_InRegistrationOrder()
    {
        var registry = new DebugAdapterRegistry();
        var managed = DebugAdapterDescriptor.BasicLangManaged(() => null);
        var lldb = DebugAdapterDescriptor.LldbDap(() => null);
        registry.Register(managed);
        registry.Register(lldb);

        // The scan is the registration-ordered list filtered by Serves: for a native
        // project the FIRST registered descriptor (managed) does not serve and must be
        // skipped, not returned by position.
        Assert.That(registry.GetFor(NativeProject()), Is.SameAs(lldb),
            "a native project must route past the non-serving managed descriptor to lldb-dap");
        Assert.That(registry.GetFor(ManagedProject()), Is.SameAs(managed),
            "a managed project must route to the managed descriptor");

        // All preserves registration order — combined with a first-match scan, that IS
        // the "first serving in registration order" contract. (The built-in predicates
        // partition projects, and the descriptor ctor is deliberately private, so two
        // descriptors overlapping on one project cannot be constructed to observe the
        // tie-break directly; order preservation plus Serves-filtering pins it.)
        Assert.That(registry.All, Is.EqualTo(new[] { managed, lldb }),
            "All must preserve registration order");
    }

    [Test]
    public void GetFor_NullProject_ReturnsNull()
    {
        var registry = new DebugAdapterRegistry();
        registry.Register(DebugAdapterDescriptor.BasicLangManaged(() => null));
        registry.Register(DebugAdapterDescriptor.LldbDap(() => null));

        // No project in hand (no project open, F5 on a loose file) is a real state —
        // the answer is "no routed adapter", never a throw.
        Assert.That(registry.GetFor(null), Is.Null);
    }

    [Test]
    public void GetById_OrdinalLookup_NullOnMiss()
    {
        var registry = new DebugAdapterRegistry();
        var managed = DebugAdapterDescriptor.BasicLangManaged(() => null);
        var lldb = DebugAdapterDescriptor.LldbDap(() => null);
        registry.Register(managed);
        registry.Register(lldb);

        Assert.That(registry.GetById(DebugAdapterDescriptor.LldbDapId), Is.SameAs(lldb));
        Assert.That(registry.GetById(DebugAdapterDescriptor.BasicLangManagedId), Is.SameAs(managed));

        // Ordinal means ORDINAL: a case-variant id is a different id and must miss —
        // ids are wire-stable tokens (DebugConfiguration.AdapterId), not display text.
        Assert.That(registry.GetById("LLDB-DAP"), Is.Null,
            "GetById must compare ordinally, not case-insensitively");
        Assert.That(registry.GetById("no-such-adapter"), Is.Null,
            "an unknown id is null, never a throw");
    }
}
