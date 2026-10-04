using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Property-grid slice 5, Task 3 (pre-flight D-4, D-10, D-11): plans for ANY event of ANY owner (the Form included),
/// the ONE case-insensitive Sub scanner, and which of the user's Subs FIT an event — the Events tab's drop-down.
/// </summary>
[TestFixture]
public class FormHandlerFitTests
{
    private static FormDocument Form(FormTarget target, string kind = "Button", string id = "btn")
    {
        var form = new FormDocument { Target = target, Name = "LoginForm" };
        form.Controls.Add(new FormControl { Kind = kind, Id = id, TabIndex = 0 });
        return form;
    }

    private static string Scaffold(FormTarget target) => FormScaffolder.Create("LoginForm", target).CodeText;

    private static FormEventDef Event(FormControlDef definition, string name) => definition.Events!.Single(e => e.Name == name);

    private static string WithSub(string code, FormTarget target, string declaration)
    {
        // Above the region on the web (where handlers go), below it on WinForms — either is fine for the scanner.
        var marker = target == FormTarget.Web ? "    ' Your event handlers go here" : "    ' Your event handlers go here.";
        var at = code.IndexOf(marker, StringComparison.Ordinal);
        Assert.That(at, Is.GreaterThan(0));
        return code[..at] + "    " + declaration + "\n    End Sub\n" + code[at..];
    }

    // ==================================================================
    // Owners — the Form plans like a control
    // ==================================================================

    [Test]
    public void TheFormsLoad_OnWinForms_IsAnEventArgsStub_BelowTheRegion()
    {
        var form = Form(FormTarget.WinForms);
        var plan = FormHandlers.PlanDefault(form, new FormBindOwner(form), Scaffold(FormTarget.WinForms));

        Assert.Multiple(() =>
        {
            Assert.That(plan.Outcome, Is.EqualTo(HandlerOutcome.Created));
            Assert.That(plan.Handler, Is.EqualTo("LoginForm_Load"));
            Assert.That(plan.EventName, Is.EqualTo("Load"));
            Assert.That(plan.CodeText, Does.Contain("Private Sub LoginForm_Load(sender As Object, e As EventArgs)"));
            Assert.That(plan.CodeText.IndexOf("Private Sub LoginForm_Load", StringComparison.Ordinal),
                Is.GreaterThan(plan.CodeText.IndexOf("Private Sub InitializeComponent", StringComparison.Ordinal)));
        });
    }

    [Test]
    public void TheFormsLoad_OnTheWeb_IsParameterless_AboveTheRegion()
    {
        var form = Form(FormTarget.Web);
        var plan = FormHandlers.PlanDefault(form, new FormBindOwner(form), Scaffold(FormTarget.Web));

        Assert.Multiple(() =>
        {
            Assert.That(plan.EventName, Is.EqualTo("load"));
            Assert.That(plan.CodeText, Does.Contain("Private Sub LoginForm_Load()\n"));
            Assert.That(plan.CodeText.IndexOf("Private Sub LoginForm_Load", StringComparison.Ordinal),
                Is.LessThan(plan.CodeText.IndexOf("Private Sub InitializeComponent", StringComparison.Ordinal)));
        });
    }

    [Test]
    public void ANonDefaultEvent_TakesItsOwnArgs_MouseDown()
    {
        var form = Form(FormTarget.WinForms);
        var owner = new FormBindOwner(form, form.Controls[0]);
        var plan = FormHandlers.Plan(form, owner, Event(owner.Definition!, "MouseDown"), Scaffold(FormTarget.WinForms));

        Assert.That(plan.CodeText, Does.Contain("Private Sub btn_MouseDown(sender As Object, e As MouseEventArgs)"));
    }

    [Test]
    public void TheFormsResize_OnTheWeb_IsAListener_TakingADomEvent()
    {
        var form = Form(FormTarget.Web);
        var plan = FormHandlers.Plan(form, new FormBindOwner(form), Event(FormControlCatalog.FormRoot, "Resize"),
            Scaffold(FormTarget.Web));

        Assert.That(plan.CodeText, Does.Contain("Private Sub LoginForm_Resize(e As DomEvent)"));
    }

    [Test]
    public void ATypedName_IsTheHandlerName()
    {
        var form = Form(FormTarget.WinForms);
        var owner = new FormBindOwner(form, form.Controls[0]);
        var plan = FormHandlers.Plan(form, owner, Event(owner.Definition!, "Click"), Scaffold(FormTarget.WinForms), "DoIt");

        Assert.That(plan.CodeText, Does.Contain("Private Sub DoIt(sender As Object, e As EventArgs)"));
    }

    [Test]
    public void AnExistingBindsHandler_Wins()
    {
        var form = Form(FormTarget.WinForms);
        form.Binds.Add(new FormBind { Event = "Load", Handler = "Startup" });
        var plan = FormHandlers.PlanDefault(form, new FormBindOwner(form), Scaffold(FormTarget.WinForms));

        Assert.That(plan.Handler, Is.EqualTo("Startup"));
    }

    /// <summary>⛔ M6: BasicLang names are case-insensitive — the old Ordinal scan wrote a SECOND, duplicate Sub.</summary>
    [Test]
    public void AHandWrittenHandler_InAnotherCase_IsNavigatedTo_NeverDuplicated()
    {
        var form = Form(FormTarget.WinForms);
        var code = WithSub(Scaffold(FormTarget.WinForms), FormTarget.WinForms,
            "Private Sub btn_click(sender As Object, e As EventArgs)");

        var plan = FormHandlers.PlanDefault(form, form.Controls[0], code);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Outcome, Is.EqualTo(HandlerOutcome.Navigated));
            Assert.That(plan.CodeText, Is.EqualTo(code));
            Assert.That(plan.Handler, Is.EqualTo("btn_click"), "the user's own spelling, so the bind names the real Sub");
        });
    }

    [Test]
    public void AMidLineComment_MentioningSub_IsNotADeclaration()
    {
        var form = Form(FormTarget.WinForms);
        var code = Scaffold(FormTarget.WinForms).Replace("' Your event handlers go here.",
            "Dim x As Integer = 1 ' see Sub btn_Click\n    ' Your event handlers go here.");

        Assert.That(FormHandlers.PlanDefault(form, form.Controls[0], code).Outcome, Is.EqualTo(HandlerOutcome.Created));
    }

    [Test]
    public void Unbind_RemovesTheBind_AndNeverTheCode()
    {
        var form = Form(FormTarget.WinForms);
        var owner = new FormBindOwner(form, form.Controls[0]);
        FormHandlers.EnsureBind(owner, "Click", "btn_Click");

        Assert.Multiple(() =>
        {
            Assert.That(FormHandlers.Unbind(owner, "click"), Is.True);
            Assert.That(form.Controls[0].Binds, Is.Empty);
            Assert.That(FormHandlers.Unbind(owner, "Click"), Is.False);
        });
    }

    [Test]
    public void TheFormOwner_BindsOnTheDocument()
    {
        var form = Form(FormTarget.Web);
        Assert.That(FormHandlers.EnsureBind(new FormBindOwner(form), "load", "LoginForm_Load"), Is.True);
        Assert.That(form.Binds.Single().Handler, Is.EqualTo("LoginForm_Load"));
    }

    // ==================================================================
    // The scanner
    // ==================================================================

    [Test]
    public void TheScanner_ReadsModifiers_ContinuedParameters_AndSkipsTheRegionsAndNonDeclarations()
    {
        var code = WithSub(Scaffold(FormTarget.WinForms), FormTarget.WinForms,
            "Protected Overridable Sub Multi(sender As Object,\n        ByVal e As System.Windows.Forms.MouseEventArgs)\n" +
            "    End Sub\n    Private Function NotASub() As Integer\n        Return 1\n    End Function\n" +
            "    Private Sub Generic(items As List(Of Integer), Optional n As Integer = 3)");

        var subs = FormCodeScan.DeclaredSubs(code);

        Assert.Multiple(() =>
        {
            var multi = subs.Single(s => s.Name == "Multi");
            Assert.That(multi.Parameters.Select(p => (p.Name, p.Type)),
                Is.EqualTo(new[] { ("sender", (string?)"Object"), ("e", "System.Windows.Forms.MouseEventArgs") }));
            Assert.That(subs.Single(s => s.Name == "Generic").Parameters.Select(p => p.Type),
                Is.EqualTo(new[] { "List(Of Integer)", "Integer" }));
            Assert.That(subs.Select(s => s.Name), Has.No.Member("NotASub").And.No.Member("InitializeComponent"),
                "a Function is not a handler, and the region's Subs are the designer's");
            Assert.That(subs.Select(s => s.Name), Does.Contain("New"));
        });
    }

    /// <summary>A <c>'</c> outside a string ends the line — a comment inside a continued parameter list is not a parameter.</summary>
    [Test]
    public void ACommentInsideAContinuedParameterList_IsNotPartOfIt()
    {
        var code = WithSub(Scaffold(FormTarget.WinForms), FormTarget.WinForms,
            "Private Sub Commented(sender As Object, ' who raised it, (the control)\n        e As EventArgs)");

        Assert.That(FormCodeScan.FindSub(code, "Commented")!.Parameters.Select(p => (p.Name, p.Type)),
            Is.EqualTo(new[] { ("sender", (string?)"Object"), ("e", "EventArgs") }));
    }

    // ==================================================================
    // Fitting (D-4)
    // ==================================================================

    private static FormDeclaredSub Sub(params (string Name, string? Type)[] parameters) =>
        new("H", 1, parameters.Select(p => new FormCodeParameter(p.Name, p.Type)).ToList());

    private static IEnumerable<TestCaseData> EveryWinFormsEvent() =>
        FormControlCatalog.All.Append(FormControlCatalog.FormRoot)
            .Where(d => d.SupportsTarget(FormTarget.WinForms))
            .SelectMany(d => FormEvents.WiredOn(d, FormTarget.WinForms).Select(e =>
                new TestCaseData(d.Kind, e.Name).SetName($"{{m}}({d.Kind}.{e.Name})")));

    private static IEnumerable<TestCaseData> EveryWebEvent() =>
        FormControlCatalog.All.Append(FormControlCatalog.FormRoot)
            .Where(d => d.SupportsTarget(FormTarget.Web))
            .SelectMany(d => FormEvents.WiredOn(d, FormTarget.Web).Select(e =>
                new TestCaseData(d.Kind, e.Name).SetName($"{{m}}({d.Kind}.{e.Name})")));

    private static FormBindOwner OwnerOf(string kind, FormTarget target)
    {
        var form = new FormDocument { Target = target, Name = "LoginForm" };
        if (kind == FormControlCatalog.FormRoot.Kind)
        {
            return new FormBindOwner(form);
        }

        var control = new FormControl { Kind = kind, Id = "c" };
        form.Controls.Add(control);
        return new FormBindOwner(form, control);
    }

    [TestCaseSource(nameof(EveryWinFormsEvent))]
    public void OnWinForms_EventArgs_AndTheEventsOwnArgs_Fit(string kind, string eventName)
    {
        var owner = OwnerOf(kind, FormTarget.WinForms);
        var evt = Event(owner.Definition!, eventName);

        Assert.Multiple(() =>
        {
            Assert.That(FormHandlers.Fits(owner, evt, FormTarget.WinForms, Sub(("sender", "Object"), ("e", "EventArgs"))), Is.True);
            Assert.That(FormHandlers.Fits(owner, evt, FormTarget.WinForms,
                Sub(("sender", "Object"), ("e", evt.WinFormsArgs ?? "EventArgs"))), Is.True);
            Assert.That(FormHandlers.Fits(owner, evt, FormTarget.WinForms, Sub(("sender", null), ("e", null))), Is.True,
                "untyped is Object, which takes anything by contravariance");
            Assert.That(FormHandlers.Fits(owner, evt, FormTarget.WinForms, Sub(("e", "EventArgs"))), Is.False, "one parameter");
            Assert.That(FormHandlers.Fits(owner, evt, FormTarget.WinForms, Sub(("sender", "String"), ("e", "EventArgs"))),
                Is.False, "the sender must be Object");
            Assert.That(FormHandlers.Fits(owner, evt, FormTarget.WinForms, Sub(("e", "DomEvent"))), Is.False);
        });
    }

    [TestCaseSource(nameof(EveryWebEvent))]
    public void OnTheWeb_AListenerTakesExactlyADomEvent_AndLoadAndTickTakeNothing(string kind, string eventName)
    {
        var owner = OwnerOf(kind, FormTarget.Web);
        var evt = Event(owner.Definition!, eventName);
        var parameterless = evt.WebWiring == FormWebWiring.AfterInit || !owner.Definition!.WebHandlerTakesEvent;

        Assert.Multiple(() =>
        {
            Assert.That(FormHandlers.Fits(owner, evt, FormTarget.Web, Sub(("e", "DomEvent"))), Is.EqualTo(!parameterless));
            Assert.That(FormHandlers.Fits(owner, evt, FormTarget.Web, Sub()), Is.EqualTo(parameterless));
            Assert.That(FormHandlers.Fits(owner, evt, FormTarget.Web, Sub(("e", null))), Is.False,
                "an untyped parameter is erased to Object, which addEventListener refuses");
            Assert.That(FormHandlers.Fits(owner, evt, FormTarget.Web, Sub(("sender", "Object"), ("e", "EventArgs"))), Is.False);
        });
    }

    [Test]
    public void OnTheWeb_OnlyLoadAndTick_AreParameterless()
    {
        var parameterless = EveryWebEvent().Select(c => ((string)c.Arguments[0]!, (string)c.Arguments[1]!))
            .Where(x =>
            {
                var owner = OwnerOf(x.Item1, FormTarget.Web);
                return FormHandlers.Shape(owner, Event(owner.Definition!, x.Item2), FormTarget.Web).Parameters.Count == 0;
            })
            .Select(x => $"{x.Item1}.{x.Item2}");

        Assert.That(parameterless, Is.EquivalentTo(new[] { "Form.Load", "Timer.Tick" }));
    }

    [Test]
    public void AKeyEventArgsHandler_DoesNotFitMouseDown_AndMouseEventArgsFitsNodeMouseClick()
    {
        var button = OwnerOf("Button", FormTarget.WinForms);
        var tree = OwnerOf("TreeView", FormTarget.WinForms);
        var form = OwnerOf("Form", FormTarget.WinForms);

        Assert.Multiple(() =>
        {
            Assert.That(FormHandlers.Fits(button, Event(button.Definition!, "MouseDown"), FormTarget.WinForms,
                Sub(("sender", "Object"), ("e", "KeyEventArgs"))), Is.False);
            Assert.That(FormHandlers.Fits(tree, Event(tree.Definition!, "NodeMouseClick"), FormTarget.WinForms,
                Sub(("sender", "Object"), ("e", "MouseEventArgs"))), Is.True, "TreeNodeMouseClickEventArgs : MouseEventArgs");
            Assert.That(FormHandlers.Fits(form, Event(FormControlCatalog.FormRoot, "FormClosing"), FormTarget.WinForms,
                Sub(("sender", "Object"), ("e", "System.ComponentModel.CancelEventArgs"))), Is.True,
                "FormClosingEventArgs : CancelEventArgs");
        });
    }

    [Test]
    public void FittingHandlers_AreTheFittingSubs_InDocumentOrder_NeverNewOrAFunction()
    {
        var form = Form(FormTarget.WinForms);
        var owner = new FormBindOwner(form, form.Controls[0]);
        var code = WithSub(WithSub(WithSub(Scaffold(FormTarget.WinForms), FormTarget.WinForms,
                    "Private Sub Zeta(sender As Object, e As EventArgs)"), FormTarget.WinForms,
                "Private Sub Keys(sender As Object, e As KeyEventArgs)"), FormTarget.WinForms,
            "Private Sub Alpha(sender As Object, e As EventArgs)");

        Assert.Multiple(() =>
        {
            Assert.That(FormHandlers.FittingHandlers(form, owner, Event(owner.Definition!, "Click"), code),
                Is.EqualTo(new[] { "Zeta", "Alpha" }));
            Assert.That(FormHandlers.FittingHandlers(form, owner, Event(owner.Definition!, "KeyDown"), code),
                Is.EqualTo(new[] { "Zeta", "Keys", "Alpha" }));
        });
    }
}
