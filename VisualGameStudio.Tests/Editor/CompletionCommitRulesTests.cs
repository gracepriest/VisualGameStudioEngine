using NUnit.Framework;
using VisualGameStudio.Editor.Completion;

namespace VisualGameStudio.Tests.Editor;

/// <summary>
/// Pins the typed-character commit contract for the completion window.
/// Server items are frequently block snippets, so ordinary typing must never
/// commit them: only '.' and '(' commit (word-only), space and every other
/// non-identifier char close the window without committing.
/// </summary>
[TestFixture]
public class CompletionCommitRulesTests
{
    [TestCase('a')]
    [TestCase('Z')]
    [TestCase('5')]
    [TestCase('_')]
    public void IdentifierChars_AreIgnored_ListRefiltersInPlace(char c)
    {
        Assert.That(CompletionCommitRules.GetActionForTypedChar(c),
            Is.EqualTo(CompletionCommitRules.TypedCharAction.Ignore));
    }

    [TestCase('\r')]
    [TestCase('\n')]
    [TestCase('\t')]
    public void EnterAndTab_AreIgnored_AvaloniaEditOwnsFullCommit(char c)
    {
        Assert.That(CompletionCommitRules.GetActionForTypedChar(c),
            Is.EqualTo(CompletionCommitRules.TypedCharAction.Ignore));
    }

    [TestCase('.')]
    [TestCase('(')]
    public void DotAndOpenParen_CommitWordOnly(char c)
    {
        Assert.That(CompletionCommitRules.GetActionForTypedChar(c),
            Is.EqualTo(CompletionCommitRules.TypedCharAction.CommitWord));
    }

    [TestCase(' ')]
    [TestCase('=')]
    [TestCase(',')]
    [TestCase(')')]
    [TestCase('"')]
    [TestCase('[')]
    [TestCase('{')]
    [TestCase('+')]
    [TestCase(';')]
    public void OtherChars_CloseWithoutCommitting(char c)
    {
        // Typing 'For' then space must yield "For " — never a For...Next block
        Assert.That(CompletionCommitRules.GetActionForTypedChar(c),
            Is.EqualTo(CompletionCommitRules.TypedCharAction.CloseWithoutCommit));
    }

    [Test]
    public void GetCommitWord_IdentifierLabel_ReturnsLabel()
    {
        Assert.That(CompletionCommitRules.GetCommitWord("WriteLine", null), Is.EqualTo("WriteLine"));
        Assert.That(CompletionCommitRules.GetCommitWord("For", "For"), Is.EqualTo("For"));
    }

    [Test]
    public void GetCommitWord_NonIdentifierLabel_FallsBackToFilterText()
    {
        // e.g. Label "For Each" with FilterText "For" — the identifier word
        // is what a '.'/'(' commit inserts.
        Assert.That(CompletionCommitRules.GetCommitWord("For Each", "For"), Is.EqualTo("For"));
    }

    [Test]
    public void GetCommitWord_NoIdentifierWord_ReturnsNull_SoTypedCharNeverCommits()
    {
        Assert.That(CompletionCommitRules.GetCommitWord("If...Else", "If...Else"), Is.Null);
        Assert.That(CompletionCommitRules.GetCommitWord(null, null), Is.Null);
        Assert.That(CompletionCommitRules.GetCommitWord("", ""), Is.Null);
    }

    [TestCase("WriteLine", true)]
    [TestCase("_private", true)]
    [TestCase("x1", true)]
    [TestCase("If...Else", false)]
    [TestCase("For Each", false)]
    [TestCase("1abc", false)]
    [TestCase("", false)]
    [TestCase(null, false)]
    [TestCase("Name(", false)]
    public void IsIdentifierWord_MatchesIdentifierPattern(string? text, bool expected)
    {
        Assert.That(CompletionCommitRules.IsIdentifierWord(text), Is.EqualTo(expected));
    }
}
