using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Slice 5, review round 5 (coordinator decision, owner delegated): <see cref="FormCodeScan"/> REBUILT on BasicLang's own
/// lexer. The shapes a line-regex scanner kept missing — a body left open mid-edit, statements joined by <c>:</c>,
/// multi-declarator fields, a Property header split from its accessors by a continuation, an attribute line or a
/// directive — plus the lexer's own gaps (<c>#End If</c>, <c>End Event</c>, an unterminated string mid-edit).
/// </summary>
[TestFixture]
public class FormCodeScanLexerTests
{
    private static FormCodeScanResult Scan(params string[] members) =>
        FormCodeScan.Scan("Public Class LoginForm\n" + string.Concat(members.Select(m => m + "\n")) + "End Class\n", "LoginForm");

    private static string[] SubNames(FormCodeScanResult scan) => scan.Subs.Select(s => s.Name).ToArray();

    /// <summary>
    /// Mid-edit: a Sub whose <c>End Sub</c> is not typed yet. The next ACCESS-MODIFIED member declaration closes it — a
    /// local statement never starts with <c>Private</c> — so the Subs and members after it are still read.
    /// </summary>
    [Test]
    public void AnUnterminatedSub_IsClosedByTheNextAccessModifiedMember_AndLaterMembersAreRead()
    {
        var scan = Scan(
            "    Private Sub Half(sender As Object, e As EventArgs)",
            "        Dim local As Integer = 1",
            "    Private Sub Later(sender As Object, e As EventArgs)",
            "    End Sub",
            "    Private counter As Integer",
            "    Sub Bare()",
            "        Dim local2 As Integer",
            "    Private Function F() As Integer",
            "        Return 1",
            "    End Function");

        Assert.Multiple(() =>
        {
            Assert.That(SubNames(scan), Is.EqualTo(new[] { "Half", "Later", "Bare" }));
            Assert.That(scan.OtherMembers, Is.SupersetOf(new[] { "counter", "F" }));
            Assert.That(scan.OtherMembers, Has.No.Member("local").And.No.Member("local2"));
        });
    }

    /// <summary>A one-line Sub — <c>Sub X() : End Sub</c> — closes on its own line; the next Sub is read.</summary>
    [Test]
    public void AOneLineSub_JoinedByAColon_ClosesItself()
    {
        var scan = Scan(
            "    Private Sub X(sender As Object, e As EventArgs) : End Sub",
            "    Private Sub Y(sender As Object, e As EventArgs)",
            "    End Sub",
            "    Private z As Integer : Private w As Integer");

        Assert.Multiple(() =>
        {
            Assert.That(SubNames(scan), Is.EqualTo(new[] { "X", "Y" }));
            Assert.That(scan.Find("X")!.Parameters.Select(p => p.Type), Is.EqualTo(new[] { "Object", "EventArgs" }));
            Assert.That(scan.OtherMembers, Is.SupersetOf(new[] { "z", "w" }));
        });
    }

    /// <summary>Every declarator names a member; <c>WithEvents</c> is a modifier, so its field's name is the identifier after it.</summary>
    [TestCase("    Dim a, b As Integer", "a,b")]
    [TestCase("    Private a As Integer, b As String", "a,b")]
    [TestCase("    Const A = 1, B = 2", "A,B")]
    [TestCase("    Dim WithEvents x As Timer", "x")]
    [TestCase("    Private WithEvents y As Timer, z As Timer", "y,z")]
    [TestCase("    Private grid(,) As Integer, list As New List(Of Integer)(New Integer() {1, 2})", "grid,list")]
    public void EveryDeclarator_NamesAMember(string member, string names)
    {
        var scan = Scan(member);

        Assert.Multiple(() =>
        {
            Assert.That(scan.OtherMembers, Is.SupersetOf(names.Split(',')), $"from: {member}");
            Assert.That(scan.OtherMembers, Has.No.Member("WithEvents"), "a modifier, never a name");
        });
    }

    /// <summary>
    /// An EXPANDED Property's body is skipped however its accessor is reached: a header continued with <c>_</c>, an attribute
    /// on its own line before <c>Get</c>, an <c>#If</c> between header and accessor. The locals inside are free names, and
    /// the members after the body are read.
    /// </summary>
    [TestCase("    Public Property Size _\n        As Integer\n        Get")]
    [TestCase("    Public Property Size As Integer\n        <DebuggerStepThrough>\n        Get")]
    [TestCase("    Public Property Size As Integer\n#If DEBUG Then\n        Get")]
    public void AnExpandedPropertysBody_IsSkipped_HoweverItsAccessorIsReached(string header)
    {
        var tail = header.Contains("#If") ? "        End Set\n#End If\n    End Property" : "        End Set\n    End Property";
        var scan = Scan(
            header,
            "            Dim inner As Integer",
            "            Return 0",
            "        End Get",
            "        Private Set(value As Integer)",
            "            Dim inner2 As Integer",
            tail,
            "    Private after As Integer",
            "    Private Sub Later(sender As Object, e As EventArgs)",
            "    End Sub");

        Assert.Multiple(() =>
        {
            Assert.That(scan.OtherMembers, Has.No.Member("inner").And.No.Member("inner2"), "locals of the accessors");
            Assert.That(scan.OtherMembers, Is.SupersetOf(new[] { "Size", "after" }));
            Assert.That(SubNames(scan), Is.EqualTo(new[] { "Later" }), "a Private Set inside the property is not a member");
        });
    }

    /// <summary>
    /// ⚠ LEXER GAP: BasicLang's lexer has no <c>End Event</c> / <c>End AddHandler</c> keywords and turns each into a bare
    /// <c>End</c> identifier, swallowing the second word. The scanner reads that word from the line itself, so a Custom
    /// Event's body still closes at its <c>End Event</c>, not at the first <c>End AddHandler</c>.
    /// </summary>
    [Test]
    public void ACustomEventsBody_ClosesAtEndEvent_DespiteTheLexersBareEnd()
    {
        var scan = Scan(
            "    Public Custom Event Changed As EventHandler",
            "        AddHandler(v As EventHandler)",
            "            Dim tmp As Integer",
            "        End AddHandler",
            "        RemoveHandler(v As EventHandler)",
            "            Dim tmp2 As Integer",
            "        End RemoveHandler",
            "        RaiseEvent(sender As Object, e As EventArgs)",
            "        End RaiseEvent",
            "    End Event",
            // ⚠ A Dim field FIRST: it does not close a body the way an access-modified member does (a local can be a Dim),
            // so it is a member only if the body really ended at `End Event` (mutation: the raw-text read removed survived
            // with only the Private line after it).
            "    Dim afterDim As Integer",
            "    Private after As Integer");

        Assert.Multiple(() =>
        {
            Assert.That(scan.OtherMembers, Is.SupersetOf(new[] { "Changed", "afterDim", "after" }));
            Assert.That(scan.OtherMembers, Has.No.Member("tmp").And.No.Member("tmp2"));
        });
    }

    /// <summary>
    /// ⚠ LEXER GAP: an unterminated string makes the lexer THROW. Mid-edit that is normal, so the scanner falls back to one
    /// line at a time (each cut at the error) — the rest of the file is still read.
    /// </summary>
    [Test]
    public void AnUnterminatedStringMidEdit_DoesNotStopTheScan()
    {
        var scan = Scan(
            "    Private Sub Typing(sender As Object, e As EventArgs)",
            "        Dim s = \"not closed yet",
            "    End Sub",
            "    Private Sub After(sender As Object, Optional t As String = \"it's ok\")",
            "    End Sub");

        Assert.Multiple(() =>
        {
            Assert.That(SubNames(scan), Is.EqualTo(new[] { "Typing", "After" }));
            Assert.That(scan.Find("After")!.Parameters.Select(p => (p.Name, p.Type)),
                Is.EqualTo(new[] { ("sender", (string?)"Object"), ("t", "String") }), "an apostrophe in a string is not a comment");
        });
    }

    /// <summary>
    /// ⚠ LEXER GAP: <c>#End If</c> (VB's spelling) is an Unknown token to the lexer, and <c>#IfDef</c>/<c>#IfNDef</c> too —
    /// all three still close/open conditional blocks here, exactly as BasicLang's <c>#EndIf</c> does.
    /// </summary>
    [Test]
    public void TheDirectiveSpellingsTheLexerCallsUnknown_StillNest()
    {
        var scan = Scan(
            "#If False Then",
            "#IfDef X",
            "    Private Sub Dead1()",
            "    End Sub",
            "#End If",
            "    Private Sub Dead2()",
            "    End Sub",
            "#End If",
            "#IfNDef Y",
            "    Private Sub Live1()",
            "    End Sub",
            "#EndIf",
            "    Private Sub Live2()",
            "    End Sub");

        Assert.That(SubNames(scan), Is.EqualTo(new[] { "Live1", "Live2" }));
    }

    /// <summary>
    /// ⚠ LEXER GAP: <c>Auto</c> is a BasicLang keyword TOKEN, so <c>Property Auto As String</c> has no identifier after
    /// <c>Property</c>. The word right after a member keyword is the member's name regardless — the regex scanner read it,
    /// and a handler named after it would be a duplicate member.
    /// </summary>
    [Test]
    public void AMemberNamedWithAKeywordSpelling_IsStillItsName()
    {
        var scan = Scan(
            "    Public Property Auto As String",
            "    Public Event Take As EventHandler",
            "    Private Sub Skip(sender As Object, e As EventArgs)",
            "    End Sub");

        Assert.Multiple(() =>
        {
            Assert.That(scan.OtherMembers, Is.SupersetOf(new[] { "Auto", "Take" }));
            Assert.That(SubNames(scan), Is.EqualTo(new[] { "Skip" }));
        });
    }

    /// <summary>
    /// Mid-edit, a parameter list still OPEN (<c>Sub Typing(sender As Object,</c>): the implicit continuation inside parentheses
    /// must not swallow the rest of the file — the next declaration starts a statement of its own.
    /// </summary>
    [Test]
    public void AParameterListLeftOpenMidEdit_DoesNotSwallowTheRestOfTheFile()
    {
        var scan = Scan(
            "    Private Sub Typing(sender As Object,",
            "    Private Sub After(sender As Object, e As EventArgs)",
            "    End Sub",
            "    Private counter As Integer");

        Assert.Multiple(() =>
        {
            Assert.That(SubNames(scan), Is.EqualTo(new[] { "Typing", "After" }));
            Assert.That(scan.OtherMembers, Does.Contain("counter"));
        });
    }

    /// <summary>Line numbers are the PHYSICAL line of the <c>Sub</c> keyword, after continuations and colons.</summary>
    [Test]
    public void TheLine_IsTheSubKeywordsPhysicalLine()
    {
        var scan = Scan(
            "    Private z As Integer : Private Sub OnColon()",
            "    End Sub",
            "    <Obsolete(\"x\")> _",
            "    Private Sub AfterAttribute()",
            "    End Sub");

        Assert.Multiple(() =>
        {
            Assert.That(scan.Find("OnColon")!.Line, Is.EqualTo(2));
            Assert.That(scan.Find("AfterAttribute")!.Line, Is.EqualTo(5));
        });
    }
}
