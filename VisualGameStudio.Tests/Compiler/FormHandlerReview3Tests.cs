using BasicLang.Forms;
using BasicLang.Forms.Serialization;
using NUnit.Framework;
using VisualGameStudio.Shell.ViewModels.Designer;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Slice 5, review of <c>cfb94a29</c>/<c>b5a5022d</c> (coordinator rulings 1–6): BasicLang's OWN directive spellings, a
/// bound name is never renamed, names the host would turn into a broken member are refused, and the Events tab scans the
/// code-behind once per refresh.
/// </summary>
[TestFixture]
public class FormHandlerReview3Tests
{
    private static string Names(string code) =>
        string.Join(",", FormCodeScan.DeclaredSubs(code, "LoginForm").Select(s => s.Name));

    // ==================================================================
    // 1 (CRITICAL) — BasicLang's directive spellings
    // ==================================================================

    /// <summary>
    /// BasicLang spells it <c>#EndIf</c> (BasicLangLexer.ScanDirective, Preprocessor.Process) — <c>#End If</c> is VB's,
    /// which the lexer marks Unknown. Both close; a Sub after the block is the form's.
    /// </summary>
    [TestCase("#EndIf")]
    [TestCase("#End If")]
    [TestCase("#endif")]
    public void AnIfFalseBlock_ClosedByEitherSpelling_HidesOnlyItsOwnSubs(string endIf)
    {
        var code = "Public Class LoginForm\n#If False Then\n    Private Sub Dead()\n    End Sub\n" + endIf + "\n" +
                   "    Private Sub Alive()\n    End Sub\nEnd Class\n";

        Assert.That(Names(code), Is.EqualTo("Alive"));
    }

    /// <summary>
    /// <c>#IfDef</c>/<c>#IfNDef</c> (the Preprocessor's) are pushed as LIVE blocks, so their own <c>#Else</c>/<c>#EndIf</c>
    /// never pop the enclosing <c>#If False</c>.
    /// </summary>
    [Test]
    public void AnIfDefNestedInIfFalse_DoesNotCloseIt()
    {
        const string code =
            "Public Class LoginForm\n" +
            "#If False Then\n" +
            "#IfDef DEBUG\n    Private Sub Dead1()\n    End Sub\n#Else\n    Private Sub Dead2()\n    End Sub\n#EndIf\n" +
            "#IfNDef RELEASE\n    Private Sub Dead3()\n    End Sub\n#EndIf\n" +
            "    Private Sub Dead4()\n    End Sub\n" +
            "#EndIf\n" +
            "#IfDef DEBUG\n    Private Sub Live1()\n    End Sub\n#Else\n    Private Sub Live2()\n    End Sub\n#EndIf\n" +
            "    Private Sub Live3()\n    End Sub\n" +
            "End Class\n";

        Assert.That(Names(code), Is.EqualTo("Live1,Live2,Live3"));
    }

    [Test]
    public void ASubInBothBranches_IsOfferedOnce()
    {
        var form = new FormDocument { Target = FormTarget.WinForms, Name = "LoginForm" };
        var code = "Public Class LoginForm\n" +
                   "#IfDef DEBUG\n    Private Sub Twice(sender As Object, e As EventArgs)\n    End Sub\n" +
                   "#Else\n    Private Sub Twice(sender As Object, e As EventArgs)\n    End Sub\n#EndIf\n" +
                   "    ' <vgs:designer region=\"init\" form=\"LoginForm.blform\" hash=\"x\">\n    ' </vgs:designer>\nEnd Class\n";

        Assert.That(FormHandlers.FittingHandlers(form, new FormBindOwner(form),
            FormControlCatalog.FormRoot.Events!.Single(e => e.Name == "Load"), code), Is.EqualTo(new[] { "Twice" }));
    }

    [Test]
    public void AGenericSubContinuedAfterOfT_AndAClassHeaderSplitWithUnderscore_AreRead()
    {
        const string code =
            "Public Class Helper\nEnd Class\n" +
            "Partial Public Class _\n    LoginForm\n" +
            "    Private Sub G(Of T) _\n        (item As T, e As EventArgs)\n    End Sub\n" +
            "End Class\n";

        var subs = FormCodeScan.DeclaredSubs(code, "LoginForm");

        Assert.Multiple(() =>
        {
            Assert.That(subs.Select(s => s.Name), Is.EqualTo(new[] { "G" }), "the split header names LoginForm");
            Assert.That(subs.Single().Parameters.Select(p => p.Name), Is.EqualTo(new[] { "item", "e" }));
        });
    }

    // ==================================================================
    // 2 — a name that came from a BIND is never renamed
    // ==================================================================

    private static (FormDocument Form, FormControl Pic, string Code) PicForm(FormTarget target)
    {
        var form = new FormDocument { Target = target, Name = "Pic" };
        var pic = new FormControl { Kind = "Button", Id = "pic", TabIndex = 0 };
        form.Controls.Add(pic);
        var root = new FormBindOwner(form);
        var click = FormControlCatalog.FormRoot.Events!.Single(e => e.Name == "Click");
        var plan = FormHandlers.Plan(form, root, click, FormScaffolder.Create("Pic", target).CodeText);
        FormHandlers.EnsureBind(root, plan.EventName, plan.Handler);
        return (form, pic, plan.CodeText);
    }

    [TestCase(FormTarget.WinForms)]
    [TestCase(FormTarget.Web)]
    public void AHandlerNamedByAnExistingBind_IsNavigated_NeverSuffixed(FormTarget target)
    {
        var (form, pic, code) = PicForm(target);
        FormHandlers.EnsureBind(pic, target == FormTarget.Web ? "click" : "Click", "pic_Click");

        var plan = FormHandlers.PlanDefault(form, pic, code);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Outcome, Is.EqualTo(HandlerOutcome.Navigated), "the bind names a member that exists (ignoring case)");
            Assert.That(plan.CodeText, Is.EqualTo(code), "no orphan stub");
        });
    }

    [Test]
    public void ARetargetedPair_WithTwoBindsDifferingOnlyInCase_GetsNoOrphanStub()
    {
        var source = new FormDocument { Target = FormTarget.WinForms, Name = "Login", Width = 300, Height = 200 };
        foreach (var (id, handler, y) in new[] { ("btnA", "Go", 8), ("btnB", "go", 40) })
        {
            var b = new FormControl
            {
                Kind = "Button", Id = id, TabIndex = y, Geometry = new PixelGeometry { X = 8, Y = y, Width = 75, Height = 23 }
            };
            b.Binds.Add(new FormBind { Event = "Click", Handler = handler });
            source.Controls.Add(b);
        }

        var pair = FormRetarget.ConvertToPair(source, FormTarget.Web);

        Assert.Multiple(() =>
        {
            Assert.That(pair.CodeText, Does.Not.Contain("go_1"));
            Assert.That(FormCodeScan.DeclaredSubs(pair.CodeText, "Login").Count(s => s.Name.Equals("go", StringComparison.OrdinalIgnoreCase)),
                Is.EqualTo(1));
        });
    }

    // ==================================================================
    // 4 — suffix edge cases, EnsureBind's reserved-binding pin
    // ==================================================================

    [Test]
    public void TheSuffix_SkipsATaken_1()
    {
        var (form, pic, code) = PicForm(FormTarget.WinForms);
        code = code.Replace("' Your event handlers go here.", "Private Sub pic_Click_1()\n    End Sub");

        Assert.That(FormHandlers.PlanDefault(form, pic, code).Handler, Is.EqualTo("pic_Click_2"));
    }

    [Test]
    public void AComputedName_BoundByAnotherOwner_WhoseSubIsMissing_IsStillSuffixed()
    {
        var form = new FormDocument { Target = FormTarget.WinForms, Name = "Pic" };
        var pic = new FormControl { Kind = "Button", Id = "pic", TabIndex = 0 };
        form.Controls.Add(pic);
        form.Binds.Add(new FormBind { Event = "Click", Handler = "Pic_Click" });   // bound, never written yet

        var plan = FormHandlers.PlanDefault(form, pic, FormScaffolder.Create("Pic", FormTarget.WinForms).CodeText);

        Assert.That(plan.Handler, Is.EqualTo("pic_Click_1"), "pic_Click is the form's member to BasicLang the moment it is written");
    }

    [Test]
    public void EnsureBind_IgnoresAReservedDataBindingOnTheSameEvent()
    {
        var btn = new FormControl { Kind = "Button", Id = "btn" };
        btn.Binds.Add(new FormBind { Event = "Click", Property = "Text", Source = "model" });

        Assert.Multiple(() =>
        {
            Assert.That(FormHandlers.EnsureBind(btn, "Click", "btn_Click"), Is.True);
            Assert.That(btn.Binds.Count(b => b.Handler == "btn_Click"), Is.EqualTo(1));
        });
    }

    // ==================================================================
    // 3 / 6 — names that would break, refused with the reason
    // ==================================================================

    private static (FormDocument Form, FormPropertyGridViewModel Grid) Grid(FormTarget target, string code)
    {
        var model = new FormDocument
        {
            Target = target, Name = "LoginForm", Width = 400, Height = 300,
            Layout = target == FormTarget.Web ? new FormLayout { Kind = FormLayoutKind.Grid, Cols = "auto", Rows = "auto" } : null
        };
        FormCatalogShapes.Canonical(model, FormControlCatalog.Find("Button")!, "ctl");
        var file = FormDocumentReader.Read("LoginForm" + (target == FormTarget.Web ? ".blwebform" : ".blform"),
            FormDocumentWriter.Create(model));
        var grid = new FormPropertyGridViewModel();
        grid.Load(file);
        grid.SelectedControl = file.Model.FindById("ctl");
        grid.CodeBehindText = code;
        grid.IsEventsMode = true;
        return (file.Model, grid);
    }

    private static string Code(FormTarget target, params string[] members)
    {
        var code = FormScaffolder.Create("LoginForm", target).CodeText;
        var marker = target == FormTarget.Web ? "    ' Your event handlers go here" : "    ' Your event handlers go here.";
        var at = code.IndexOf(marker, StringComparison.Ordinal);
        return code[..at] + string.Concat(members.Select(m => "    " + m + "\n")) + code[at..];
    }

    [TestCase(FormTarget.Web, "New", "Load", "constructor")]
    [TestCase(FormTarget.WinForms, "InitializeComponent", "Click", "designer")]
    [TestCase(FormTarget.WinForms, "Class", "Click", "keyword")]
    [TestCase(FormTarget.WinForms, "ctl", "Click", "control")]
    [TestCase(FormTarget.WinForms, "loginform", "Click", "form")]
    [TestCase(FormTarget.WinForms, "Compute", "Click", "Function")]
    [TestCase(FormTarget.WinForms, "Refd", "Click", "ByRef")]
    [TestCase(FormTarget.Web, "Stat", "Click", "Shared")]
    public void ANameTheHostWouldTurnIntoABrokenMember_IsRefused_WithTheReason(
        FormTarget target, string typed, string eventName, string because)
    {
        var code = Code(target,
            "Public Sub New()\n    End Sub",
            "Private Function Compute() As Integer\n        Return 1\n    End Function",
            target == FormTarget.Web ? "Private Sub Refd(ByRef e As DomEvent)\n    End Sub" : "Private Sub Refd(sender As Object, ByRef e As EventArgs)\n    End Sub",
            target == FormTarget.Web ? "Private Shared Sub Stat(e As DomEvent)\n    End Sub" : "Private Shared Sub Stat(sender As Object, e As EventArgs)\n    End Sub");
        var (form, grid) = Grid(target, code);
        if (eventName == "Load")
        {
            grid.SelectedControl = null;
        }

        var asked = false;
        grid.HandlerRequested += (_, _) => asked = true;
        var row = grid.EventRows.Single(r => r.Name == eventName);

        row.Commit(typed);

        Assert.Multiple(() =>
        {
            Assert.That(row.Refusal, Does.Contain(because).IgnoreCase);
            Assert.That(asked, Is.False, "the host is never asked to write it");
            Assert.That(form.AllControls().SelectMany(c => c.Binds).Concat(form.Binds), Is.Empty);
            Assert.That(row.Choices, Has.No.Member(typed));
        });
    }

    [Test]
    public void RetypingTheBoundNameInAnotherCase_ChangesNothing()
    {
        var (form, grid) = Grid(FormTarget.WinForms, Code(FormTarget.WinForms, "Private Sub ctl_Click(sender As Object, e As EventArgs)\n    End Sub"));
        grid.EventRows.Single(r => r.Name == "Click").Commit("ctl_Click");
        var edits = 0;
        grid.Edited += (_, _) => edits++;

        grid.EventRows.Single(r => r.Name == "Click").Commit("CTL_CLICK");

        Assert.Multiple(() =>
        {
            Assert.That(edits, Is.Zero);
            Assert.That(form.FindById("ctl")!.Binds.Single().Handler, Is.EqualTo("ctl_Click"));
        });
    }

    [Test]
    public void ASelectionWithNoEvents_SaysSo()
    {
        var (form, grid) = Grid(FormTarget.WinForms, Code(FormTarget.WinForms));
        var mystery = new FormControl { Kind = "Mystery", Id = "m", TabIndex = 1, Geometry = new PixelGeometry { Width = 10, Height = 10 } };
        form.Controls.Add(mystery);
        grid.SelectedControl = mystery;

        Assert.Multiple(() =>
        {
            Assert.That(grid.EventRows, Is.Empty);
            Assert.That(grid.DescriptionBody, Does.Contain("no events"));
        });
    }

    // ==================================================================
    // 5 — one scan per refresh
    // ==================================================================

    [Test]
    public void PushingTheCodeBehind_ScansItOnce_ForAllRows()
    {
        var (_, grid) = Grid(FormTarget.WinForms, Code(FormTarget.WinForms));
        Assert.That(grid.EventRows.Count, Is.GreaterThan(5));

        var before = FormCodeScan.ScanCount;
        grid.CodeBehindText = Code(FormTarget.WinForms, "Private Sub X(sender As Object, e As EventArgs)\n    End Sub");

        Assert.That(FormCodeScan.ScanCount - before, Is.EqualTo(1));
    }
}
