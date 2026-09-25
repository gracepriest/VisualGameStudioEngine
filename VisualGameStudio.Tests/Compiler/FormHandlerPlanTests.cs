using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 22: the double-click gesture — create the default handler if it is absent, navigate to it if
/// it is present.
///
/// <para>⛔ The placement tests are the ones that matter. A stub in the wrong place still compiles on
/// WinForms and fails on the web with <i>"cannot convert from 'Action(Of Object)' to
/// 'Action(Of DomEvent)'"</i> — an error about delegate types, in generated code, naming neither the
/// control the user double-clicked nor the rule that was broken. D8's ordering rule is the whole
/// reason this planner exists rather than an append.</para>
///
/// <para>⚠ Pure text in, pure text out, like <see cref="FormScaffolder"/> and
/// <see cref="RegionWriter"/> — no file system and no IDE, so the CLI, the designer and these tests
/// drive one implementation.</para>
/// </summary>
[TestFixture]
public class FormHandlerPlanTests
{
    private static FormDocument Form(FormTarget target, string kind = "Button", string id = "btnLogin")
    {
        var form = new FormDocument { Target = target, Name = "LoginForm" };
        form.Controls.Add(new FormControl { Kind = kind, Id = id, TabIndex = 0 });
        return form;
    }

    /// <summary>The real file a new form is born with — the one a user actually double-clicks into.</summary>
    private static string Scaffold(FormTarget target) =>
        FormScaffolder.Create("LoginForm", target).CodeText;

    private static FormControl Only(FormDocument form) => form.Controls[0];

    // ==================================================================
    // Which event a double-click means
    // ==================================================================

    [Test]
    public void AButtonDoubleClicksToItsClickEvent()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FormHandlers.DefaultEvent("Button", FormTarget.WinForms), Is.EqualTo("Click"));
            Assert.That(FormHandlers.DefaultEvent("Button", FormTarget.Web), Is.EqualTo("click"));
        });
    }

    /// <summary>
    /// ⚠ Not every control means "Click". VS opens a TextBox on TextChanged, and a designer that
    /// always produced a Click handler would be wrong for most of the catalog while looking right
    /// for the control people test with first.
    /// </summary>
    [Test]
    public void ATextBoxDoubleClicksToItsChangeEvent_NotClick()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FormHandlers.DefaultEvent("TextBox", FormTarget.WinForms), Is.EqualTo("TextChanged"));
            Assert.That(FormHandlers.DefaultEvent("TextBox", FormTarget.Web), Is.EqualTo("input"),
                "the DOM has no TextChanged — 'input' is the honest equivalent");
        });
    }

    [Test]
    public void EveryCatalogControlHasADefaultEventOnEveryTargetItSupports()
    {
        foreach (var def in FormControlCatalog.All)
        {
            foreach (var target in new[] { FormTarget.WinForms, FormTarget.Web })
            {
                if (!def.SupportsTarget(target))
                {
                    continue;
                }

                Assert.That(FormHandlers.DefaultEvent(def.Kind, target), Is.Not.Null.And.Not.Empty,
                    $"'{def.Kind}' on {target} — a control with no default event cannot be " +
                    "double-clicked, and the catalog is the only place that can say what it means");
            }
        }
    }

    /// <summary>
    /// ⛔ The web's events are NOT falsifiable the way WinForms' are.
    /// <c>WinFormsCatalogSweepTests</c> puts every WinForms event through csc, which rejects a
    /// misspelling with CS1061 — but <c>addEventListener</c> takes a <b>string</b>, so
    /// <c>addEventListener("Click", …)</c> compiles, runs, and simply never fires. Nothing
    /// downstream can catch that.
    ///
    /// <para>DOM event types are lowercase without exception, so this catches the one error anyone
    /// is actually going to make: copying the WinForms spelling into the web column.</para>
    /// </summary>
    [Test]
    public void EveryWebEventIsLowercase_BecauseNothingDownstreamCanCatchItIfItIsNot()
    {
        foreach (var def in FormControlCatalog.All.Where(c => c.SupportsTarget(FormTarget.Web)))
        {
            var web = FormHandlers.DefaultEvent(def.Kind, FormTarget.Web)!;

            Assert.That(web, Is.EqualTo(web.ToLowerInvariant()),
                $"'{def.Kind}' declares the web event '{web}'. addEventListener would accept it " +
                "silently and the handler would never fire.");
        }
    }

    /// <summary>
    /// The handler NAME keeps VS's PascalCase shape on both targets, even though the web's event is
    /// lowercase: <c>btnLogin_Click</c>, never <c>btnLogin_click</c>.
    /// </summary>
    [Test]
    public void TheHandlerIsNamedControlUnderscoreEvent_PascalCaseOnBothTargets()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FormHandlers.NameFor("btnLogin", "Click"), Is.EqualTo("btnLogin_Click"));
            Assert.That(FormHandlers.NameFor("btnLogin", "click"), Is.EqualTo("btnLogin_Click"));
            Assert.That(FormHandlers.NameFor("txtName", "input"), Is.EqualTo("txtName_Input"));
        });
    }

    // ==================================================================
    // Creating the stub
    // ==================================================================

    [Test]
    public void AnAbsentHandlerIsCreated()
    {
        var form = Form(FormTarget.Web);
        var plan = FormHandlers.PlanDefault(form, Only(form), Scaffold(FormTarget.Web));

        Assert.Multiple(() =>
        {
            Assert.That(plan.Outcome, Is.EqualTo(HandlerOutcome.Created));
            Assert.That(plan.Handler, Is.EqualTo("btnLogin_Click"));
            Assert.That(plan.CodeText, Does.Contain("btnLogin_Click"));
        });
    }

    /// <summary>⛔ The web signature is <c>(e As DomEvent)</c>; <c>addEventListener</c> rejects anything else.</summary>
    [Test]
    public void AWebStubTakesADomEvent()
    {
        var form = Form(FormTarget.Web);
        var plan = FormHandlers.PlanDefault(form, Only(form), Scaffold(FormTarget.Web));

        Assert.That(plan.CodeText, Does.Contain("Private Sub btnLogin_Click(e As DomEvent)"));
    }

    [Test]
    public void AWinFormsStubTakesSenderAndEventArgs()
    {
        var form = Form(FormTarget.WinForms);
        var plan = FormHandlers.PlanDefault(form, Only(form), Scaffold(FormTarget.WinForms));

        Assert.That(plan.CodeText,
            Does.Contain("Private Sub btnLogin_Click(sender As Object, e As EventArgs)"));
    }

    /// <summary>
    /// ⛔⛔ D8, and the reason a plain append is wrong. On the web an <c>AddressOf</c> naming a
    /// <c>Sub</c> declared BELOW the region that wires it erases the parameter types to
    /// <c>Action(Of Object)</c>, and <c>addEventListener</c> refuses it.
    /// </summary>
    [Test]
    public void AWebStubIsInsertedAboveTheInitRegion()
    {
        var form = Form(FormTarget.Web);
        var plan = FormHandlers.PlanDefault(form, Only(form), Scaffold(FormTarget.Web));

        var handlerAt = plan.CodeText.IndexOf("Private Sub btnLogin_Click", StringComparison.Ordinal);
        var initAt = plan.CodeText.IndexOf("region=\"init\"", StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(handlerAt, Is.GreaterThan(-1));
            Assert.That(initAt, Is.GreaterThan(-1));
            Assert.That(handlerAt, Is.LessThan(initAt),
                "a handler below the init region loses its parameter types on the web");
        });
    }

    /// <summary>
    /// ⚠ The mirror of the above, and deliberately the OTHER order. Measured 2026-09-13: on WinForms
    /// the event is an unresolvable .NET member typed <c>Object</c>, so there is no declared delegate
    /// to mismatch and the shipped VSIX template declares its handler below <c>InitializeComponent</c>.
    /// The scaffold puts its "your event handlers go here" comment there, so a stub that landed above
    /// the region would appear somewhere the file itself says handlers do not go.
    /// </summary>
    [Test]
    public void AWinFormsStubIsInsertedBelowTheInitRegion_WhereTheScaffoldSaysHandlersGo()
    {
        var form = Form(FormTarget.WinForms);
        var plan = FormHandlers.PlanDefault(form, Only(form), Scaffold(FormTarget.WinForms));

        var handlerAt = plan.CodeText.IndexOf("Private Sub btnLogin_Click", StringComparison.Ordinal);
        var initAt = plan.CodeText.IndexOf("region=\"init\"", StringComparison.Ordinal);

        Assert.That(handlerAt, Is.GreaterThan(initAt));
    }

    [Test]
    public void TheStubIsInsideTheFormsClass()
    {
        var form = Form(FormTarget.WinForms);
        var plan = FormHandlers.PlanDefault(form, Only(form), Scaffold(FormTarget.WinForms));

        var handlerAt = plan.CodeText.IndexOf("Private Sub btnLogin_Click", StringComparison.Ordinal);
        var endClassAt = plan.CodeText.LastIndexOf("End Class", StringComparison.Ordinal);

        Assert.That(handlerAt, Is.LessThan(endClassAt), "a handler outside the class is not a handler");
    }

    /// <summary>The caret lands where the user types, not on the signature they just generated.</summary>
    [Test]
    public void TheCaretLandsOnTheEmptyBodyLine()
    {
        var form = Form(FormTarget.Web);
        var plan = FormHandlers.PlanDefault(form, Only(form), Scaffold(FormTarget.Web));

        var lines = plan.CodeText.Replace("\r\n", "\n").Split('\n');

        Assert.Multiple(() =>
        {
            Assert.That(plan.CaretLine, Is.GreaterThan(0));
            Assert.That(lines[plan.CaretLine - 1].Trim(), Is.Empty, "the caret is on the body line");
            Assert.That(lines[plan.CaretLine - 2], Does.Contain("Private Sub btnLogin_Click"));
            Assert.That(lines[plan.CaretLine], Does.Contain("End Sub"));
        });
    }

    // ==================================================================
    // Navigating to one that already exists
    // ==================================================================

    [Test]
    public void AnExistingHandlerIsNavigatedTo_AndTheFileIsNotTouched()
    {
        var form = Form(FormTarget.Web);
        var once = FormHandlers.PlanDefault(form, Only(form), Scaffold(FormTarget.Web));

        var twice = FormHandlers.PlanDefault(form, Only(form), once.CodeText);

        Assert.Multiple(() =>
        {
            Assert.That(twice.Outcome, Is.EqualTo(HandlerOutcome.Navigated));
            Assert.That(twice.CodeText, Is.EqualTo(once.CodeText), "navigating must not edit the file");
        });
    }

    [Test]
    public void NavigatingLandsInsideTheExistingHandler()
    {
        var form = Form(FormTarget.Web);
        var once = FormHandlers.PlanDefault(form, Only(form), Scaffold(FormTarget.Web));
        var twice = FormHandlers.PlanDefault(form, Only(form), once.CodeText);

        Assert.That(twice.CaretLine, Is.EqualTo(once.CaretLine),
            "the second double-click goes where the first one left the caret");
    }

    /// <summary>
    /// ⛔ Double-clicking the same control twice must not declare the Sub twice — the file would stop
    /// compiling, and the designer would have broken the user's build for them.
    /// </summary>
    [Test]
    public void DoubleClickingTwiceDoesNotDeclareTheHandlerTwice()
    {
        var form = Form(FormTarget.Web);
        var once = FormHandlers.PlanDefault(form, Only(form), Scaffold(FormTarget.Web));
        var twice = FormHandlers.PlanDefault(form, Only(form), once.CodeText);

        var count = twice.CodeText.Split("Private Sub btnLogin_Click").Length - 1;
        Assert.That(count, Is.EqualTo(1));
    }

    // ==================================================================
    // The document side — the wiring the stub is for
    // ==================================================================

    /// <summary>
    /// A stub nothing wires is dead code. The bind is what makes the region emit the
    /// <c>addEventListener</c>/<c>AddHandler</c> that calls it.
    /// </summary>
    [Test]
    public void PlanningAHandlerWiresItInTheDocument()
    {
        var form = Form(FormTarget.Web);
        var control = Only(form);

        FormHandlers.EnsureBind(control, "click", "btnLogin_Click");

        Assert.Multiple(() =>
        {
            Assert.That(control.Binds, Has.Count.EqualTo(1));
            Assert.That(control.Binds[0].Event, Is.EqualTo("click"));
            Assert.That(control.Binds[0].Handler, Is.EqualTo("btnLogin_Click"));
        });
    }

    [Test]
    public void WiringTheSameEventTwiceAddsOneBind()
    {
        var form = Form(FormTarget.Web);
        var control = Only(form);

        FormHandlers.EnsureBind(control, "click", "btnLogin_Click");
        var second = FormHandlers.EnsureBind(control, "click", "btnLogin_Click");

        Assert.Multiple(() =>
        {
            Assert.That(control.Binds, Has.Count.EqualTo(1));
            Assert.That(second, Is.False, "the second call reports that it added nothing");
        });
    }

    /// <summary>
    /// ⚠ An existing bind the USER wrote names their handler, not ours. Double-clicking must open
    /// the handler that is actually wired, or the user edits a Sub the form never calls.
    /// </summary>
    [Test]
    public void AnExistingBindDecidesWhichHandlerIsOpened()
    {
        var form = Form(FormTarget.Web);
        var control = Only(form);
        control.Binds.Add(new FormBind { Event = "click", Handler = "SignIn" });

        var plan = FormHandlers.PlanDefault(form, control, Scaffold(FormTarget.Web));

        Assert.That(plan.Handler, Is.EqualTo("SignIn"));
    }

    // ==================================================================
    // Refusals
    // ==================================================================

    /// <summary>
    /// ⛔ No regions means this file is not designer-owned (D12's import case). Inserting a handler
    /// into it would be the designer editing a file it does not own.
    /// </summary>
    [Test]
    public void AFileWithNoDesignerRegionsIsRefused()
    {
        var form = Form(FormTarget.Web);
        var plan = FormHandlers.PlanDefault(form, Only(form), "Public Class LoginForm\nEnd Class\n");

        Assert.Multiple(() =>
        {
            Assert.That(plan.Outcome, Is.EqualTo(HandlerOutcome.Refused));
            Assert.That(plan.Refusal, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public void AControlWithNoDefaultEventIsRefusedRatherThanGuessed()
    {
        var form = Form(FormTarget.Web, kind: "NotAControl", id: "x");
        var plan = FormHandlers.PlanDefault(form, Only(form), Scaffold(FormTarget.Web));

        Assert.Multiple(() =>
        {
            Assert.That(plan.Outcome, Is.EqualTo(HandlerOutcome.Refused));
            Assert.That(plan.Refusal, Does.Contain("NotAControl"));
        });
    }

    /// <summary>
    /// ⚠ Line endings. The scaffold is LF; a user's file on Windows is very often CRLF, and a stub
    /// inserted with the wrong terminator leaves a file with both — which shows up as a whole-file
    /// diff the next time anything touches it.
    /// </summary>
    [Test]
    public void TheStubTakesTheFilesOwnLineEndings()
    {
        var form = Form(FormTarget.WinForms);
        var crlf = Scaffold(FormTarget.WinForms).Replace("\r\n", "\n").Replace("\n", "\r\n");

        var plan = FormHandlers.PlanDefault(form, Only(form), crlf);

        Assert.Multiple(() =>
        {
            Assert.That(plan.CodeText, Does.Contain("\r\n"));
            Assert.That(plan.CodeText.Replace("\r\n", ""), Does.Not.Contain("\n"),
                "a file must not end up with mixed terminators");
        });
    }
}
