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
}
