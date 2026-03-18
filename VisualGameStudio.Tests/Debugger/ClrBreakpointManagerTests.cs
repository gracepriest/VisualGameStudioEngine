using NUnit.Framework;
using BasicLang.Debugger;

namespace VisualGameStudio.Tests.Debugger;

[TestFixture]
public class ClrBreakpointManagerTests
{
    [Test]
    public void AddBreakpoint_NewBreakpoint_StatusIsPending()
    {
        var mgr = new ClrBreakpointManager();
        var id = mgr.AddPendingBreakpoint("Main.bas", 5);
        var bp = mgr.GetBreakpoint(id);
        Assert.That(bp.Status, Is.EqualTo(ClrBreakpointStatus.Pending));
    }

    [Test]
    public void MarkBound_PendingBreakpoint_StatusChangesToBound()
    {
        var mgr = new ClrBreakpointManager();
        var id = mgr.AddPendingBreakpoint("Main.bas", 5);
        mgr.MarkBound(id, actualLine: 5);
        var bp = mgr.GetBreakpoint(id);
        Assert.That(bp.Status, Is.EqualTo(ClrBreakpointStatus.Bound));
    }

    [Test]
    public void MarkInvalid_NoExecutableCode_StatusIsInvalid()
    {
        var mgr = new ClrBreakpointManager();
        var id = mgr.AddPendingBreakpoint("Main.bas", 1);
        mgr.MarkInvalid(id);
        var bp = mgr.GetBreakpoint(id);
        Assert.That(bp.Status, Is.EqualTo(ClrBreakpointStatus.Invalid));
    }

    [Test]
    public void GetPendingForFile_ReturnsOnlyPending()
    {
        var mgr = new ClrBreakpointManager();
        mgr.AddPendingBreakpoint("Main.bas", 5);
        mgr.AddPendingBreakpoint("Main.bas", 10);
        var id3 = mgr.AddPendingBreakpoint("Main.bas", 15);
        mgr.MarkBound(id3, 15);

        var pending = mgr.GetPendingForFile("Main.bas");
        Assert.That(pending.Count, Is.EqualTo(2));
    }

    [Test]
    public void ClearFile_RemovesAllForFile()
    {
        var mgr = new ClrBreakpointManager();
        mgr.AddPendingBreakpoint("Main.bas", 5);
        mgr.AddPendingBreakpoint("func.bas", 10);
        mgr.ClearFile("Main.bas");

        Assert.That(mgr.GetPendingForFile("Main.bas"), Is.Empty);
        Assert.That(mgr.GetPendingForFile("func.bas"), Has.Count.EqualTo(1));
    }

    // --- GetAllPending tests ---

    [Test]
    public void GetAllPending_EmptyManager_ReturnsEmpty()
    {
        var mgr = new ClrBreakpointManager();
        var pending = mgr.GetAllPending();
        Assert.That(pending, Is.Empty);
    }

    [Test]
    public void GetAllPending_MultiplePending_ReturnsAll()
    {
        var mgr = new ClrBreakpointManager();
        mgr.AddPendingBreakpoint("Main.bas", 5);
        mgr.AddPendingBreakpoint("Main.bas", 10);
        mgr.AddPendingBreakpoint("Other.bas", 3);

        var pending = mgr.GetAllPending();
        Assert.That(pending.Count, Is.EqualTo(3));
    }

    [Test]
    public void GetAllPending_MixedStatuses_ReturnsOnlyPending()
    {
        var mgr = new ClrBreakpointManager();
        mgr.AddPendingBreakpoint("Main.bas", 5);
        var id2 = mgr.AddPendingBreakpoint("Main.bas", 10);
        var id3 = mgr.AddPendingBreakpoint("Other.bas", 3);
        mgr.MarkBound(id2, 10);
        mgr.MarkInvalid(id3);

        var pending = mgr.GetAllPending();
        Assert.That(pending.Count, Is.EqualTo(1));
        Assert.That(pending[0].RequestedLine, Is.EqualTo(5));
    }

    [Test]
    public void GetAllPending_ExcludesVerified()
    {
        var mgr = new ClrBreakpointManager();
        var id1 = mgr.AddPendingBreakpoint("Main.bas", 5);
        mgr.AddPendingBreakpoint("Main.bas", 10);
        mgr.MarkVerified(id1);

        var pending = mgr.GetAllPending();
        Assert.That(pending.Count, Is.EqualTo(1));
        Assert.That(pending[0].RequestedLine, Is.EqualTo(10));
    }

    // --- MarkVerified tests ---

    [Test]
    public void MarkVerified_PendingBreakpoint_StatusChangesToVerified()
    {
        var mgr = new ClrBreakpointManager();
        var id = mgr.AddPendingBreakpoint("Main.bas", 5);
        mgr.MarkVerified(id);
        var bp = mgr.GetBreakpoint(id);
        Assert.That(bp.Status, Is.EqualTo(ClrBreakpointStatus.Verified));
    }

    [Test]
    public void MarkVerified_BoundBreakpoint_StatusChangesToVerified()
    {
        var mgr = new ClrBreakpointManager();
        var id = mgr.AddPendingBreakpoint("Main.bas", 5);
        mgr.MarkBound(id, 5);
        mgr.MarkVerified(id);
        var bp = mgr.GetBreakpoint(id);
        Assert.That(bp.Status, Is.EqualTo(ClrBreakpointStatus.Verified));
    }

    [Test]
    public void MarkVerified_NonExistentId_DoesNotThrow()
    {
        var mgr = new ClrBreakpointManager();
        Assert.DoesNotThrow(() => mgr.MarkVerified(999));
    }

    // --- GetBreakpoint tests ---

    [Test]
    public void GetBreakpoint_NonExistentId_ReturnsNull()
    {
        var mgr = new ClrBreakpointManager();
        var bp = mgr.GetBreakpoint(999);
        Assert.That(bp, Is.Null);
    }

    [Test]
    public void GetBreakpoint_AfterClearAll_ReturnsNull()
    {
        var mgr = new ClrBreakpointManager();
        var id = mgr.AddPendingBreakpoint("Main.bas", 5);
        mgr.ClearAll();
        var bp = mgr.GetBreakpoint(id);
        Assert.That(bp, Is.Null);
    }

    [Test]
    public void GetBreakpoint_AfterClearFile_ReturnsNull()
    {
        var mgr = new ClrBreakpointManager();
        var id = mgr.AddPendingBreakpoint("Main.bas", 5);
        mgr.ClearFile("Main.bas");
        var bp = mgr.GetBreakpoint(id);
        Assert.That(bp, Is.Null);
    }

    // --- AddPendingBreakpoint additional tests ---

    [Test]
    public void AddPendingBreakpoint_ReturnsIncrementingIds()
    {
        var mgr = new ClrBreakpointManager();
        var id1 = mgr.AddPendingBreakpoint("Main.bas", 5);
        var id2 = mgr.AddPendingBreakpoint("Main.bas", 10);
        var id3 = mgr.AddPendingBreakpoint("Main.bas", 15);
        Assert.That(id2, Is.EqualTo(id1 + 1));
        Assert.That(id3, Is.EqualTo(id2 + 1));
    }

    [Test]
    public void AddPendingBreakpoint_StoresCondition()
    {
        var mgr = new ClrBreakpointManager();
        var id = mgr.AddPendingBreakpoint("Main.bas", 5, condition: "x > 10");
        var bp = mgr.GetBreakpoint(id);
        Assert.That(bp.Condition, Is.EqualTo("x > 10"));
    }

    [Test]
    public void AddPendingBreakpoint_StoresHitCondition()
    {
        var mgr = new ClrBreakpointManager();
        var id = mgr.AddPendingBreakpoint("Main.bas", 5, hitCondition: "3");
        var bp = mgr.GetBreakpoint(id);
        Assert.That(bp.HitCondition, Is.EqualTo("3"));
    }

    [Test]
    public void AddPendingBreakpoint_StoresLogMessage()
    {
        var mgr = new ClrBreakpointManager();
        var id = mgr.AddPendingBreakpoint("Main.bas", 5, logMessage: "hit bp at line 5");
        var bp = mgr.GetBreakpoint(id);
        Assert.That(bp.LogMessage, Is.EqualTo("hit bp at line 5"));
    }

    [Test]
    public void AddPendingBreakpoint_ActualLineEqualsRequestedLine()
    {
        var mgr = new ClrBreakpointManager();
        var id = mgr.AddPendingBreakpoint("Main.bas", 42);
        var bp = mgr.GetBreakpoint(id);
        Assert.That(bp.ActualLine, Is.EqualTo(42));
        Assert.That(bp.RequestedLine, Is.EqualTo(42));
    }

    // --- MarkBound additional tests ---

    [Test]
    public void MarkBound_UpdatesActualLine()
    {
        var mgr = new ClrBreakpointManager();
        var id = mgr.AddPendingBreakpoint("Main.bas", 5);
        mgr.MarkBound(id, actualLine: 7);
        var bp = mgr.GetBreakpoint(id);
        Assert.That(bp.ActualLine, Is.EqualTo(7));
        Assert.That(bp.RequestedLine, Is.EqualTo(5));
    }

    [Test]
    public void MarkBound_StoresClrBreakpointObject()
    {
        var mgr = new ClrBreakpointManager();
        var id = mgr.AddPendingBreakpoint("Main.bas", 5);
        var sentinelObject = new object();
        mgr.MarkBound(id, actualLine: 5, clrBreakpoint: sentinelObject);
        var bp = mgr.GetBreakpoint(id);
        Assert.That(bp.ClrBreakpoint, Is.SameAs(sentinelObject));
    }

    [Test]
    public void MarkBound_NonExistentId_DoesNotThrow()
    {
        var mgr = new ClrBreakpointManager();
        Assert.DoesNotThrow(() => mgr.MarkBound(999, actualLine: 5));
    }

    [Test]
    public void MarkInvalid_NonExistentId_DoesNotThrow()
    {
        var mgr = new ClrBreakpointManager();
        Assert.DoesNotThrow(() => mgr.MarkInvalid(999));
    }

    // --- ClearAll tests ---

    [Test]
    public void ClearAll_RemovesEverything()
    {
        var mgr = new ClrBreakpointManager();
        mgr.AddPendingBreakpoint("Main.bas", 5);
        mgr.AddPendingBreakpoint("Main.bas", 10);
        mgr.AddPendingBreakpoint("Other.bas", 3);
        mgr.ClearAll();

        Assert.That(mgr.GetAllPending(), Is.Empty);
        Assert.That(mgr.GetPendingForFile("Main.bas"), Is.Empty);
        Assert.That(mgr.GetPendingForFile("Other.bas"), Is.Empty);
    }

    [Test]
    public void ClearAll_EmptyManager_DoesNotThrow()
    {
        var mgr = new ClrBreakpointManager();
        Assert.DoesNotThrow(() => mgr.ClearAll());
    }

    // --- ClearFile additional tests ---

    [Test]
    public void ClearFile_CaseInsensitive()
    {
        var mgr = new ClrBreakpointManager();
        mgr.AddPendingBreakpoint("Main.bas", 5);
        mgr.ClearFile("MAIN.BAS");
        Assert.That(mgr.GetPendingForFile("Main.bas"), Is.Empty);
    }

    [Test]
    public void ClearFile_NonExistentFile_DoesNotThrow()
    {
        var mgr = new ClrBreakpointManager();
        Assert.DoesNotThrow(() => mgr.ClearFile("nofile.bas"));
    }

    [Test]
    public void ClearFile_RemovesAllStatuses()
    {
        var mgr = new ClrBreakpointManager();
        var id1 = mgr.AddPendingBreakpoint("Main.bas", 5);
        var id2 = mgr.AddPendingBreakpoint("Main.bas", 10);
        var id3 = mgr.AddPendingBreakpoint("Main.bas", 15);
        mgr.MarkBound(id2, 10);
        mgr.MarkVerified(id3);

        mgr.ClearFile("Main.bas");

        Assert.That(mgr.GetBreakpoint(id1), Is.Null);
        Assert.That(mgr.GetBreakpoint(id2), Is.Null);
        Assert.That(mgr.GetBreakpoint(id3), Is.Null);
    }

    // --- GetPendingForFile additional tests ---

    [Test]
    public void GetPendingForFile_CaseInsensitive()
    {
        var mgr = new ClrBreakpointManager();
        mgr.AddPendingBreakpoint("Main.bas", 5);
        var pending = mgr.GetPendingForFile("MAIN.BAS");
        Assert.That(pending.Count, Is.EqualTo(1));
    }

    [Test]
    public void GetPendingForFile_EmptyManager_ReturnsEmpty()
    {
        var mgr = new ClrBreakpointManager();
        var pending = mgr.GetPendingForFile("Main.bas");
        Assert.That(pending, Is.Empty);
    }

    [Test]
    public void GetPendingForFile_DifferentFilesIsolated()
    {
        var mgr = new ClrBreakpointManager();
        mgr.AddPendingBreakpoint("Main.bas", 5);
        mgr.AddPendingBreakpoint("Main.bas", 10);
        mgr.AddPendingBreakpoint("Other.bas", 3);

        var mainPending = mgr.GetPendingForFile("Main.bas");
        var otherPending = mgr.GetPendingForFile("Other.bas");
        Assert.That(mainPending.Count, Is.EqualTo(2));
        Assert.That(otherPending.Count, Is.EqualTo(1));
    }

    // --- State transition full lifecycle tests ---

    [Test]
    public void FullLifecycle_PendingToBoundToVerified()
    {
        var mgr = new ClrBreakpointManager();
        var id = mgr.AddPendingBreakpoint("Main.bas", 5);

        Assert.That(mgr.GetBreakpoint(id).Status, Is.EqualTo(ClrBreakpointStatus.Pending));

        mgr.MarkBound(id, actualLine: 6);
        Assert.That(mgr.GetBreakpoint(id).Status, Is.EqualTo(ClrBreakpointStatus.Bound));
        Assert.That(mgr.GetBreakpoint(id).ActualLine, Is.EqualTo(6));

        mgr.MarkVerified(id);
        Assert.That(mgr.GetBreakpoint(id).Status, Is.EqualTo(ClrBreakpointStatus.Verified));
    }

    [Test]
    public void FullLifecycle_PendingToInvalid()
    {
        var mgr = new ClrBreakpointManager();
        var id = mgr.AddPendingBreakpoint("Main.bas", 1);

        Assert.That(mgr.GetBreakpoint(id).Status, Is.EqualTo(ClrBreakpointStatus.Pending));

        mgr.MarkInvalid(id);
        Assert.That(mgr.GetBreakpoint(id).Status, Is.EqualTo(ClrBreakpointStatus.Invalid));
    }

    [Test]
    public void MultipleBreakpoints_IndependentStateTransitions()
    {
        var mgr = new ClrBreakpointManager();
        var id1 = mgr.AddPendingBreakpoint("Main.bas", 5);
        var id2 = mgr.AddPendingBreakpoint("Main.bas", 10);
        var id3 = mgr.AddPendingBreakpoint("Main.bas", 15);

        mgr.MarkBound(id1, 5);
        mgr.MarkInvalid(id2);
        // id3 stays pending

        Assert.That(mgr.GetBreakpoint(id1).Status, Is.EqualTo(ClrBreakpointStatus.Bound));
        Assert.That(mgr.GetBreakpoint(id2).Status, Is.EqualTo(ClrBreakpointStatus.Invalid));
        Assert.That(mgr.GetBreakpoint(id3).Status, Is.EqualTo(ClrBreakpointStatus.Pending));
    }

    [Test]
    public void AddAfterClear_NewIdsAreStillIncrementing()
    {
        var mgr = new ClrBreakpointManager();
        var id1 = mgr.AddPendingBreakpoint("Main.bas", 5);
        mgr.ClearAll();
        var id2 = mgr.AddPendingBreakpoint("Main.bas", 10);
        // IDs should still increment even after clear
        Assert.That(id2, Is.GreaterThan(id1));
    }
}
