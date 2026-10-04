using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Slice 5 D-4 (coordinator ruling 4, ADR 0021 §4): <see cref="FormHandlers.Shape"/> is the ONE place a handler's
/// signature and placement are decided. The pin: for every event of every owner on every target, the stub the planner
/// WRITES is a Sub that <see cref="FormHandlers.Fits"/> ACCEPTS — so the drop-down always offers the stub the
/// double-click wrote — and the planner's source spells no signature of its own.
/// </summary>
[TestFixture]
public class FormHandlerShapeTests
{
    private static IEnumerable<TestCaseData> EveryEventOnEveryTarget() =>
        new[] { FormTarget.WinForms, FormTarget.Web }.SelectMany(target =>
            FormControlCatalog.All.Append(FormControlCatalog.FormRoot)
                .Where(d => d.SupportsTarget(target))
                .SelectMany(d => FormEvents.WiredOn(d, target).Select(e =>
                    new TestCaseData(target, d.Kind, e.Name).SetName($"{{m}}({target} {d.Kind}.{e.Name})"))));

    [Test]
    public void TheSweep_HasSomethingToSweep() =>
        Assert.That(EveryEventOnEveryTarget().Count(), Is.GreaterThan(300));

    [TestCaseSource(nameof(EveryEventOnEveryTarget))]
    public void TheStubThePlannerWrites_FitsItsOwnEvent_OnTheShapesSide(FormTarget target, string kind, string eventName)
    {
        var form = new FormDocument { Target = target, Name = "LoginForm" };
        FormBindOwner owner;
        if (kind == FormControlCatalog.FormRoot.Kind)
        {
            owner = new FormBindOwner(form);
        }
        else
        {
            var control = new FormControl { Kind = kind, Id = "c" };
            form.Controls.Add(control);
            owner = new FormBindOwner(form, control);
        }

        var evt = owner.Definition!.Events!.Single(e => e.Name == eventName);
        var plan = FormHandlers.Plan(form, owner, evt, FormScaffolder.Create("LoginForm", target).CodeText);
        var written = FormCodeScan.FindSub(plan.CodeText, plan.Handler);
        var init = plan.CodeText.IndexOf("Private Sub InitializeComponent", StringComparison.Ordinal);
        var stubAt = plan.CodeText.IndexOf("Private Sub " + plan.Handler + "(", StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Outcome, Is.EqualTo(HandlerOutcome.Created), plan.Refusal);
            Assert.That(written, Is.Not.Null, "the scanner must find the stub the planner wrote");
            Assert.That(FormHandlers.Fits(owner, evt, target, written!), Is.True,
                $"the written stub does not fit its own event: {plan.CodeText.Split('\n').FirstOrDefault(l => l.Contains(plan.Handler))}");
            Assert.That(stubAt < init, Is.EqualTo(FormHandlers.Shape(owner, evt, target).Placement == FormHandlerPlacement.AboveInitRegion),
                "the stub lands on the side Shape names");
        });
    }

    /// <summary>⛔ The "one site" pin, structurally: no parameter list is spelled in the planner outside Shape's records.</summary>
    [Test]
    public void ThePlanner_SpellsNoSignatureLiteral()
    {
        string? path = null;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null && path == null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "BasicLang", "Forms", "FormHandlers.cs");
            path = File.Exists(candidate) ? candidate : null;
        }

        Assert.That(path, Is.Not.Null, "FormHandlers.cs not found above the test binaries");
        var source = File.ReadAllText(path!);

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Not.Contain("(sender As Object"));
            Assert.That(source, Does.Not.Contain("(e As DomEvent)"));
            Assert.That(source.Split("?? \"EventArgs\"").Length - 1, Is.EqualTo(1), "the EventArgs fallback lives in Shape only");
        });
    }
}
