using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Slice 5, review of <c>a5c5955c</c>/<c>eec527f6</c> (coordinator rulings 3–4): the scanner's member names are the CLASS's
/// members — every declaration shape at member level, never a local inside a body — and the double-click's COMPUTED name
/// obeys the same guards a typed name does.
/// </summary>
[TestFixture]
public class FormHandlerReview4Tests
{
    private static FormCodeScanResult Scan(params string[] members) =>
        FormCodeScan.Scan("Public Class LoginForm\n" + string.Concat(members.Select(m => m + "\n")) + "End Class\n", "LoginForm");

    // ==================================================================
    // 3 — member names: every member-level shape, no locals
    // ==================================================================

    /// <summary>
    /// A field needs no <c>Dim</c> (<c>Private counter As Integer</c>), may be <c>WithEvents</c>, may declare several names;
    /// nested types and Delegates/Declares are members too. Each would make a same-named handler a duplicate member (CS0102).
    /// </summary>
    [TestCase("    Private counter As Integer", "counter")]
    [TestCase("    Private WithEvents tmr2 As Timer", "tmr2")]
    [TestCase("    Public a, b As Integer", "a,b")]
    [TestCase("    Shared ReadOnly limit As Integer = 3", "limit")]
    [TestCase("    Protected items() As String", "items")]
    [TestCase("    Friend Const Max = 10", "Max")]
    [TestCase("    Private Enum Mode\n        One\n    End Enum", "Mode")]
    [TestCase("    Private Structure Pt\n        Public X As Integer\n    End Structure", "Pt")]
    [TestCase("    Private Class Inner\n    End Class", "Inner")]
    [TestCase("    Public Delegate Sub Notify(x As Integer)", "Notify")]
    [TestCase("    Public Delegate Function Pick() As Integer", "Pick")]
    [TestCase("    Private Declare Function Beep Lib \"kernel32\" (f As Integer) As Integer", "Beep")]
    [TestCase("    Public Custom Event Changed As EventHandler\n        AddHandler(v As EventHandler)\n        End AddHandler\n    End Event", "Changed")]
    public void AMemberLevelDeclaration_NamesAMember(string member, string names)
    {
        var scan = Scan(member);

        Assert.That(scan.OtherMembers, Is.SupersetOf(names.Split(',')), $"from: {member}");
    }

    /// <summary>
    /// A field inside a NESTED type is that type's, not the form's: <c>X</c> inside <c>Structure Pt</c> is free.
    /// </summary>
    [Test]
    public void AFieldOfANestedType_IsNotAFormMember()
    {
        var scan = Scan("    Private Structure Pt\n        Public X As Integer\n    End Structure");

        Assert.That(scan.OtherMembers, Has.No.Member("X"));
    }

    /// <summary>
    /// ⛔ The other direction (over-strict refusal): a LOCAL is not a member. <c>Dim total</c> in a Sub, a <c>Static</c> in a
    /// Function, a <c>Const</c> in a Property's Get, and the locals of a multi-line lambda are all free names — and the
    /// scanner still reads the members AFTER those bodies (a lambda's <c>End Sub</c> did not end the enclosing Sub early).
    /// </summary>
    [Test]
    public void LocalsInsideBodies_AreNotMembers_AndMembersAfterTheBodiesStillAre()
    {
        var scan = Scan(
            "    Private Sub Work()",
            "        Dim total As Integer",
            "        Dim handler = Sub(x As Integer)",
            "                          Dim inner As Integer",
            "                      End Sub",
            "        Dim afterLambda As Integer",
            "    End Sub",
            "    Private Function Count() As Integer",
            "        Static calls As Integer",
            "        Return calls",
            "    End Function",
            "    Public Property Size As Integer",
            "        Get",
            "            Const k = 2",
            "            Return k",
            "        End Get",
            "        Set(value As Integer)",
            "        End Set",
            "    End Property",
            "    Public Property Auto As String",
            "    Private realField As Integer",
            "    Private Sub Later(sender As Object, e As EventArgs)",
            "    End Sub");

        Assert.Multiple(() =>
        {
            Assert.That(scan.OtherMembers, Has.No.Member("total").And.No.Member("inner").And.No.Member("afterLambda")
                .And.No.Member("calls").And.No.Member("k"), "locals are free names");
            Assert.That(scan.OtherMembers, Is.SupersetOf(new[] { "Count", "Size", "Auto", "realField" }),
                "the members around and after the bodies");
            Assert.That(scan.Subs.Select(s => s.Name), Is.EqualTo(new[] { "Work", "Later" }),
                "a Sub after a lambda and a property body is still read");
        });
    }

    /// <summary>The refusal the user sees, both directions: a field's name is refused; a local's name is accepted.</summary>
    [Test]
    public void TypingAFieldsName_IsRefused_ALocalsNameIsNot()
    {
        var form = new FormDocument { Target = FormTarget.WinForms, Name = "LoginForm" };
        var click = FormControlCatalog.FormRoot.Events!.Single(e => e.Name == "Click");
        var scan = Scan("    Private counter As Integer", "    Private Sub Work()\n        Dim total As Integer\n    End Sub");

        Assert.Multiple(() =>
        {
            Assert.That(FormHandlers.DescribeUnusableHandler(form, new FormBindOwner(form), click, "counter", scan),
                Does.Contain("member"));
            Assert.That(FormHandlers.DescribeUnusableHandler(form, new FormBindOwner(form), click, "total", scan), Is.Null);
        });
    }

    // ==================================================================
    // 4 — the computed name obeys the same guards
    // ==================================================================

    private static (FormDocument Form, FormControl Btn) ButtonForm()
    {
        var form = new FormDocument { Target = FormTarget.Web, Name = "LoginForm" };
        var btn = new FormControl { Kind = "Button", Id = "btn", TabIndex = 0 };
        form.Controls.Add(btn);
        return (form, btn);
    }

    private static string WebCode(string member)
    {
        var code = FormScaffolder.Create("LoginForm", FormTarget.Web).CodeText;
        var at = code.IndexOf("    ' Your event handlers go here", StringComparison.Ordinal);
        return code[..at] + "    " + member + "\n" + code[at..];
    }

    /// <summary>
    /// ⛔ A double-click whose computed <c>btn_Click</c> is an existing SHARED Sub must not navigate to it and bind it —
    /// <c>AddressOf</c> a Shared Sub is a runtime ReferenceError on the page (measured, round 3). It is taken: <c>btn_Click_1</c>.
    /// The same for an existing Sub of the wrong SHAPE (it would not compile) and a Function of that name.
    /// </summary>
    [TestCase("Private Shared Sub btn_Click(e As DomEvent)\n    End Sub")]
    [TestCase("Private Sub btn_Click(x As Integer)\n    End Sub")]
    [TestCase("Private Sub btn_Click(ByRef e As DomEvent)\n    End Sub")]
    [TestCase("Private Function btn_Click() As Integer\n        Return 0\n    End Function")]
    public void AComputedNameThatIsUnusable_IsTaken_AndSuffixed(string member)
    {
        var (form, btn) = ButtonForm();

        var plan = FormHandlers.PlanDefault(form, btn, WebCode(member));

        Assert.Multiple(() =>
        {
            Assert.That(plan.Outcome, Is.EqualTo(HandlerOutcome.Created), "never navigated to an unusable member");
            Assert.That(plan.Handler, Is.EqualTo("btn_Click_1"));
            Assert.That(plan.CodeText, Does.Contain("Private Sub btn_Click_1(e As DomEvent)"));
        });
    }

    /// <summary>The regression direction: a computed name that IS a fitting instance Sub is still navigated, never suffixed.</summary>
    [Test]
    public void AComputedNameThatFits_IsStillNavigated()
    {
        var (form, btn) = ButtonForm();
        var code = WebCode("Private Sub btn_Click(e As DomEvent)\n    End Sub");

        var plan = FormHandlers.PlanDefault(form, btn, code);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Outcome, Is.EqualTo(HandlerOutcome.Navigated));
            Assert.That(plan.Handler, Is.EqualTo("btn_Click"));
        });
    }
}
