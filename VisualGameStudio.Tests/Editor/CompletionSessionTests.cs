using NUnit.Framework;
using VisualGameStudio.Editor.Completion;

namespace VisualGameStudio.Tests.Editor;

[TestFixture]
public class CompletionSessionTests
{
    [Test]
    public void InitialState_IsNone()
    {
        var session = new CompletionSession();
        Assert.That(session.State, Is.EqualTo(CompletionSession.SessionState.None));
    }

    [Test]
    public void Begin_MovesToRequested_AndRecordsTriggerPoint()
    {
        var session = new CompletionSession();

        session.Begin(caretOffset: 42, caretLine: 3);

        Assert.That(session.State, Is.EqualTo(CompletionSession.SessionState.Requested));
        Assert.That(session.TriggerOffset, Is.EqualTo(42));
        Assert.That(session.TriggerLine, Is.EqualTo(3));
    }

    [Test]
    public void WindowOpened_FromRequested_MovesToOpen()
    {
        var session = new CompletionSession();
        session.Begin(10, 1);

        session.WindowOpened();

        Assert.That(session.State, Is.EqualTo(CompletionSession.SessionState.Open));
    }

    [Test]
    public void WindowOpened_FromNone_StaysNone()
    {
        var session = new CompletionSession();

        session.WindowOpened();

        Assert.That(session.State, Is.EqualTo(CompletionSession.SessionState.None));
    }

    [Test]
    public void End_ResetsToNone()
    {
        var session = new CompletionSession();
        session.Begin(10, 1);
        session.WindowOpened();

        session.End();

        Assert.That(session.State, Is.EqualTo(CompletionSession.SessionState.None));
    }

    [Test]
    public void ShouldOpenWindow_WhenRequestedAndCaretAtTrigger_ReturnsTrue()
    {
        var session = new CompletionSession();
        session.Begin(10, 1);

        Assert.That(session.ShouldOpenWindow(caretOffset: 10, caretLine: 1, textSinceTrigger: ""), Is.True);
    }

    [Test]
    public void ShouldOpenWindow_WhenUserKeptTypingOnSameLine_ReturnsTrue()
    {
        var session = new CompletionSession();
        session.Begin(10, 1);

        // Two more identifier chars typed while the request was in flight
        Assert.That(session.ShouldOpenWindow(caretOffset: 12, caretLine: 1, textSinceTrigger: "ab"), Is.True);
    }

    [Test]
    public void ShouldOpenWindow_WhenSessionEnded_ReturnsFalse()
    {
        var session = new CompletionSession();
        session.Begin(10, 1);
        session.End();

        // A late response must never reopen a dismissed popup
        Assert.That(session.ShouldOpenWindow(10, 1, ""), Is.False);
    }

    [Test]
    public void ShouldOpenWindow_WhenNoSessionEverStarted_ReturnsFalse()
    {
        var session = new CompletionSession();
        Assert.That(session.ShouldOpenWindow(0, 1, ""), Is.False);
    }

    [Test]
    public void ShouldOpenWindow_WhenCaretMovedToDifferentLine_ReturnsFalse()
    {
        var session = new CompletionSession();
        session.Begin(10, 1);

        Assert.That(session.ShouldOpenWindow(caretOffset: 50, caretLine: 2, textSinceTrigger: ""), Is.False);
    }

    [Test]
    public void ShouldOpenWindow_WhenCaretMovedBeforeTrigger_ReturnsFalse()
    {
        var session = new CompletionSession();
        session.Begin(10, 1);

        Assert.That(session.ShouldOpenWindow(caretOffset: 5, caretLine: 1, textSinceTrigger: ""), Is.False);
    }

    [Test]
    public void ShouldOpenWindow_WhenDotTypedAfterTrigger_ReturnsFalse()
    {
        // Word session for "foo", then '.' typed while the response was in
        // flight: the stale word-scope response must NOT open a popup at the
        // member-access position (text between trigger and caret is not
        // identifier-only).
        var session = new CompletionSession();
        session.Begin(10, 1);

        Assert.That(session.ShouldOpenWindow(caretOffset: 11, caretLine: 1, textSinceTrigger: "."), Is.False);
    }

    [Test]
    public void ShouldOpenWindow_WhenCaretJumpedPastOtherText_ReturnsFalse()
    {
        // Request fired at offset 10, user clicked at offset 40 on the SAME
        // line (e.g. right after an existing word): the text between the
        // trigger and the clicked caret is arbitrary code, so a late response
        // must not open the popup at the navigated-to position.
        var session = new CompletionSession();
        session.Begin(10, 1);

        Assert.That(session.ShouldOpenWindow(caretOffset: 40, caretLine: 1, textSinceTrigger: " = player"), Is.False);
    }

    [Test]
    public void ShouldOpenWindow_WhenTextSinceTriggerUnknown_ReturnsFalse()
    {
        // Null means the caller could not reconstruct the typed range
        // (document shrank, offsets invalid) — never open on uncertainty.
        var session = new CompletionSession();
        session.Begin(10, 1);

        Assert.That(session.ShouldOpenWindow(caretOffset: 12, caretLine: 1, textSinceTrigger: null), Is.False);
    }

    [Test]
    public void ShouldOpenWindow_WhenWindowAlreadyOpen_ReturnsFalse()
    {
        var session = new CompletionSession();
        session.Begin(10, 1);
        session.WindowOpened();

        // Data arriving while a window is open updates in place instead
        Assert.That(session.ShouldOpenWindow(10, 1, ""), Is.False);
    }

    [Test]
    public void Begin_AfterEnd_StartsFreshSession()
    {
        var session = new CompletionSession();
        session.Begin(10, 1);
        session.End();

        session.Begin(20, 2);

        Assert.That(session.State, Is.EqualTo(CompletionSession.SessionState.Requested));
        Assert.That(session.ShouldOpenWindow(20, 2, ""), Is.True);
    }

    [Test]
    public void IsCaretConsistentWithTyping_TypedIdentifierChars_IsTrue()
    {
        var session = new CompletionSession();
        session.Begin(10, 1);

        Assert.That(session.IsCaretConsistentWithTyping(13, 1, "ab_"), Is.True);
    }

    [Test]
    public void IsCaretConsistentWithTyping_CaretMovedAway_IsFalse()
    {
        var session = new CompletionSession();
        session.Begin(10, 1);

        Assert.That(session.IsCaretConsistentWithTyping(5, 1, ""), Is.False, "moved before trigger");
        Assert.That(session.IsCaretConsistentWithTyping(10, 2, ""), Is.False, "moved to another line");
        Assert.That(session.IsCaretConsistentWithTyping(20, 1, "x + yyyy"), Is.False, "jumped past other code");
        Assert.That(session.IsCaretConsistentWithTyping(20, 1, null), Is.False, "typed range unknown");
    }

    [Test]
    public void Begin_DefaultTriggerKind_IsWord()
    {
        var session = new CompletionSession();
        session.Begin(10, 1);

        Assert.That(session.TriggerKind, Is.EqualTo(CompletionSession.CompletionTriggerKind.Word));
    }

    [Test]
    public void Begin_RecordsTriggerKind()
    {
        var session = new CompletionSession();

        session.Begin(10, 1, CompletionSession.CompletionTriggerKind.MemberAccess);
        Assert.That(session.TriggerKind, Is.EqualTo(CompletionSession.CompletionTriggerKind.MemberAccess));

        session.Begin(12, 1, CompletionSession.CompletionTriggerKind.Invoked);
        Assert.That(session.TriggerKind, Is.EqualTo(CompletionSession.CompletionTriggerKind.Invoked));
    }

    [Test]
    public void HasPendingRequest_WhileRequested_IsTrue()
    {
        var session = new CompletionSession();
        session.Begin(10, 1);

        Assert.That(session.HasPendingRequest, Is.True);
    }

    [Test]
    public void HasPendingRequest_ExpiresAfterTimeout_SoTypingCanRecover()
    {
        var session = new CompletionSession();
        session.Begin(10, 1);

        var afterTimeout = DateTime.UtcNow + CompletionSession.PendingRequestTimeout + TimeSpan.FromSeconds(1);

        Assert.That(session.HasPendingRequestAt(afterTimeout), Is.False,
            "a session whose response never arrived must stop blocking new word triggers");
    }

    [Test]
    public void HasPendingRequest_WhenNoneOrOpen_IsFalse()
    {
        var session = new CompletionSession();
        Assert.That(session.HasPendingRequest, Is.False);

        session.Begin(10, 1);
        session.WindowOpened();
        Assert.That(session.HasPendingRequest, Is.False);
    }
}
