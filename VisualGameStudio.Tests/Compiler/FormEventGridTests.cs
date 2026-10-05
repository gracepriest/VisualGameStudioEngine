using BasicLang.Forms;
using BasicLang.Forms.Serialization;
using NUnit.Framework;
using VisualGameStudio.Shell.ViewModels.Designer;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Property-grid slice 5, Task 5 (pre-flight D-5): the grid's EVENTS mode, view model only. Rows are exactly the seam's
/// (<see cref="FormEvents.WiredOn"/>) for the selection — the Form with nothing selected; a pick binds, a clear unbinds, a
/// typed new name asks the host for a stub, and nothing here ever writes the code-behind.
/// </summary>
[TestFixture]
public class FormEventGridTests
{
    private static FormFile FileOf(FormDocument model) =>
        FormDocumentReader.Read(model.Name + (model.Target == FormTarget.Web ? ".blwebform" : ".blform"),
            FormDocumentWriter.Create(model));

    private static (FormFile File, FormPropertyGridViewModel Grid) Open(FormTarget target, string kind = "Button")
    {
        var model = new FormDocument
        {
            Target = target, Name = "LoginForm", Width = 400, Height = 300,
            Layout = target == FormTarget.Web ? new FormLayout { Kind = FormLayoutKind.Grid, Cols = "auto", Rows = "auto" } : null
        };
        FormCatalogShapes.Canonical(model, FormControlCatalog.Find(kind)!, "ctl");
        var file = FileOf(model);
        var grid = new FormPropertyGridViewModel();
        grid.Load(file);
        grid.SelectedControl = file.Model.FindById("ctl");
        grid.IsEventsMode = true;
        return (file, grid);
    }

    private static FormEventRow Row(FormPropertyGridViewModel grid, string name) => grid.EventRows.Single(r => r.Name == name);

    private static string Code(FormTarget target, params string[] subs)
    {
        var code = FormScaffolder.Create("LoginForm", target).CodeText;
        var marker = target == FormTarget.Web ? "    ' Your event handlers go here" : "    ' Your event handlers go here.";
        var at = code.IndexOf(marker, StringComparison.Ordinal);
        return code[..at] + string.Concat(subs.Select(s => "    " + s + "\n    End Sub\n")) + code[at..];
    }

    // ==================================================================
    // Rows are the seam's
    // ==================================================================

    private static IEnumerable<TestCaseData> EveryKindOnEveryTarget() =>
        new[] { FormTarget.WinForms, FormTarget.Web }.SelectMany(t => FormControlCatalog.All
            .Where(d => d.SupportsTarget(t))
            .Select(d => new TestCaseData(t, d.Kind).SetName($"{{m}}({t} {d.Kind})")));

    [TestCaseSource(nameof(EveryKindOnEveryTarget))]
    public void TheEventRows_AreExactlyTheSeams_ForEveryKindOnEveryTarget(FormTarget target, string kind)
    {
        var (_, grid) = Open(target, kind);

        Assert.That(grid.EventRows.Select(r => r.Name),
            Is.EqualTo(FormEvents.WiredOn(FormControlCatalog.Find(kind)!, target).Select(e => e.Name)));
    }

    [TestCase(FormTarget.WinForms)]
    [TestCase(FormTarget.Web)]
    public void WithNothingSelected_TheEventRows_AreTheForms(FormTarget target)
    {
        var (_, grid) = Open(target);
        grid.SelectedControl = null;

        Assert.That(grid.EventRows.Select(r => r.Name),
            Is.EqualTo(FormEvents.WiredOn(FormControlCatalog.FormRoot, target).Select(e => e.Name)));
    }

    [Test]
    public void AWebPanel_ShowsNoPaint_AndAWebTimer_OnlyTick()
    {
        var (_, panel) = Open(FormTarget.Web, "Panel");
        var (_, timer) = Open(FormTarget.Web, "Timer");

        Assert.Multiple(() =>
        {
            Assert.That(panel.EventRows.Select(r => r.Name), Has.No.Member("Paint").And.Member("Click"));
            Assert.That(timer.EventRows.Select(r => r.Name), Is.EqualTo(new[] { "Tick" }));
        });
    }

    [Test]
    public void TheHeaders_AreEventCategoryNames_AndSearchFilters()
    {
        var (_, grid) = Open(FormTarget.WinForms);
        var headers = grid.DisplayItems.OfType<FormPropertyCategoryHeader>().Select(h => h.Name).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(headers, Is.SubsetOf(Enum.GetNames<FormEventCategory>()));
            Assert.That(headers, Does.Contain("Mouse"));
            Assert.That(grid.DisplayItems.OfType<FormEventRow>().Count(), Is.EqualTo(grid.EventRows.Count));
        });

        grid.SearchText = "mouse";
        Assert.That(grid.DisplayItems.OfType<FormEventRow>().Select(r => r.Name),
            Is.All.StartsWith("Mouse").And.Not.Empty);
    }

    // ==================================================================
    // Pick / clear / type
    // ==================================================================

    [TestCase(FormTarget.WinForms, "Private Sub Common(sender As Object, e As EventArgs)", "Click")]
    [TestCase(FormTarget.Web, "Private Sub Common(e As DomEvent)", "click")]
    public void PickingAFittingHandler_BindsIt_InTheTargetsVocabulary_OnceEdited_CodeUntouched(
        FormTarget target, string sub, string stored)
    {
        var (file, grid) = Open(target);
        var code = Code(target, sub);
        grid.CodeBehindText = code;
        var edits = 0;
        grid.Edited += (_, _) => edits++;
        var click = Row(grid, "Click");

        Assert.That(click.Choices, Does.Contain("Common"));
        click.Commit("Common");

        Assert.Multiple(() =>
        {
            Assert.That(edits, Is.EqualTo(1));
            Assert.That(file.Model.FindById("ctl")!.Binds.Single().Event, Is.EqualTo(stored));
            Assert.That(file.Model.FindById("ctl")!.Binds.Single().Handler, Is.EqualTo("Common"));
            Assert.That(click.Handler, Is.EqualTo("Common"));
            Assert.That(grid.CodeBehindText, Is.EqualTo(code));
        });
    }

    [Test]
    public void ClearingTheCell_Unbinds_OnceEdited_AndNeverTouchesCode()
    {
        var (file, grid) = Open(FormTarget.WinForms);
        var code = Code(FormTarget.WinForms, "Private Sub ctl_Click(sender As Object, e As EventArgs)");
        grid.CodeBehindText = code;
        file.Model.FindById("ctl")!.Binds.Add(new FormBind { Event = "Click", Handler = "ctl_Click" });
        grid.SelectedControl = null;
        grid.SelectedControl = file.Model.FindById("ctl");
        var edits = 0;
        grid.Edited += (_, _) => edits++;

        Row(grid, "Click").Commit("");

        Assert.Multiple(() =>
        {
            Assert.That(file.Model.FindById("ctl")!.Binds, Is.Empty);
            Assert.That(edits, Is.EqualTo(1));
            Assert.That(grid.CodeBehindText, Is.EqualTo(code));
        });
    }

    [Test]
    public void TypingANewLegalName_AsksTheHostForTheStub_AndWritesNothingItself()
    {
        var (file, grid) = Open(FormTarget.WinForms);
        grid.CodeBehindText = Code(FormTarget.WinForms);
        FormHandlerRequest? asked = null;
        grid.HandlerRequested += (_, r) => asked = r;
        var edits = 0;
        grid.Edited += (_, _) => edits++;

        Row(grid, "Click").Commit("DoIt");

        Assert.Multiple(() =>
        {
            Assert.That(asked, Is.Not.Null);
            Assert.That(asked!.Handler, Is.EqualTo("DoIt"));
            Assert.That(asked.Event.Name, Is.EqualTo("Click"));
            Assert.That(asked.Owner.Control, Is.SameAs(file.Model.FindById("ctl")));
            Assert.That(file.Model.FindById("ctl")!.Binds, Is.Empty, "the host binds once the stub exists");
            Assert.That(edits, Is.Zero);
        });
    }

    [TestCase("9lives", "not a legal")]
    [TestCase("VgsOn_mine", "reserved")]
    [TestCase("Keys", "does not fit")]
    public void AnIllegalOrNonFittingName_IsRefusedInTheDescriptionPane_AndNothingIsWritten(string typed, string because)
    {
        var (file, grid) = Open(FormTarget.WinForms);
        grid.CodeBehindText = Code(FormTarget.WinForms, "Private Sub Keys(sender As Object, e As KeyEventArgs)");
        var asked = false;
        grid.HandlerRequested += (_, _) => asked = true;
        var click = Row(grid, "Click");

        click.Commit(typed);

        Assert.Multiple(() =>
        {
            Assert.That(click.Refusal, Does.Contain(because));
            Assert.That(grid.DescriptionBody, Does.Contain(because));
            Assert.That(file.Model.FindById("ctl")!.Binds, Is.Empty);
            Assert.That(asked, Is.False);
        });
    }

    [Test]
    public void DoubleClickingARow_AsksForItsHandler_WithNoName()
    {
        var (_, grid) = Open(FormTarget.WinForms);
        FormHandlerRequest? asked = null;
        grid.HandlerRequested += (_, r) => asked = r;

        Row(grid, "MouseDown").RequestHandler();

        Assert.Multiple(() =>
        {
            Assert.That(asked!.Event.Name, Is.EqualTo("MouseDown"));
            Assert.That(asked.Handler, Is.Null, "the host computes <Id>_<Event>, or navigates to the bound handler");
        });
    }

    // ==================================================================
    // Choices, modes, memory
    // ==================================================================

    [Test]
    public void Choices_IsTheSameInstance_WhileUnchanged_AndFollowsTheCode()
    {
        var (_, grid) = Open(FormTarget.WinForms);
        grid.CodeBehindText = Code(FormTarget.WinForms, "Private Sub A(sender As Object, e As EventArgs)");
        var click = Row(grid, "Click");
        var first = click.Choices;

        Assert.That(click.Choices, Is.SameAs(first), "a new instance during a pick undoes the pick (slice 4 Task 3)");

        grid.CodeBehindText = Code(FormTarget.WinForms, "Private Sub A(sender As Object, e As EventArgs)",
            "Private Sub B(sender As Object, e As EventArgs)");

        Assert.That(click.Choices, Is.EqualTo(new[] { "A", "B" }));
    }

    [Test]
    public void TheMode_SurvivesASelectionChange_AndPropertiesModeIsUnchanged()
    {
        var (file, grid) = Open(FormTarget.WinForms);
        grid.SelectedControl = null;
        grid.SelectedControl = file.Model.FindById("ctl");

        Assert.Multiple(() =>
        {
            Assert.That(grid.IsEventsMode, Is.True);
            Assert.That(grid.IsPropertiesMode, Is.False);
            Assert.That(grid.DisplayItems.OfType<FormEventRow>(), Is.Not.Empty);
            Assert.That(grid.DisplayItems.OfType<FormPropertyRow>(), Is.Empty);
        });

        grid.IsPropertiesMode = true;
        Assert.Multiple(() =>
        {
            Assert.That(grid.IsEventsMode, Is.False);
            Assert.That(grid.DisplayItems.OfType<FormPropertyRow>(), Is.Not.Empty);
            Assert.That(grid.DisplayItems.OfType<FormEventRow>(), Is.Empty);
        });
    }

    [Test]
    public void CollapseMemory_IsPerMode()
    {
        var (_, grid) = Open(FormTarget.WinForms);
        grid.IsPropertiesMode = true;
        // Appearance is a category of both lists on a Button (BackColor…; Paint).
        var inProperties = grid.DisplayItems.OfType<FormPropertyCategoryHeader>().Single(h => h.Name == "Appearance");
        inProperties.IsExpanded = false;

        grid.IsEventsMode = true;
        var inEvents = grid.DisplayItems.OfType<FormPropertyCategoryHeader>().Single(h => h.Name == "Appearance");

        grid.IsPropertiesMode = true;
        var backInProperties = grid.DisplayItems.OfType<FormPropertyCategoryHeader>().Single(h => h.Name == "Appearance");

        Assert.Multiple(() =>
        {
            Assert.That(inEvents.IsExpanded, Is.True, "collapsing Appearance among properties leaves it open among events");
            Assert.That(backInProperties.IsExpanded, Is.False, "and the properties list remembers its own collapse");
        });
    }

    [Test]
    public void TheDescriptionPane_DescribesTheSelectedEvent()
    {
        var (_, grid) = Open(FormTarget.WinForms);
        grid.SelectedItem = Row(grid, "MouseDown");

        Assert.Multiple(() =>
        {
            Assert.That(grid.DescriptionTitle, Is.EqualTo("MouseDown"));
            Assert.That(grid.DescriptionBody, Is.EqualTo(FormControlCatalog.Find("Button")!.Events!.Single(e => e.Name == "MouseDown").Description));
        });
    }
}
