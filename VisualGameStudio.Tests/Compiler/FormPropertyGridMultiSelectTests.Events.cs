using BasicLang.Forms;
using BasicLang.Forms.Serialization;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Events;
using VisualGameStudio.Shell.ViewModels.Designer;
using VisualGameStudio.Shell.ViewModels.Documents;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Slice 6 Task 5 (D-8): the Events tab for a multi-selection — rows are the events EVERY member wires, a pick/clear binds
/// or unbinds every member with ONE Edited, a typed name is refused if ANY member refuses it, and the host plans ONE stub for
/// the primary and binds every owner with ONE write, never collapsing the selection. The canvas double-click on a member
/// wires every selected control that has the clicked control's default event.
/// </summary>
public partial class FormPropertyGridMultiSelectTests
{
    private const string WebMultiDoc = """
        <WebForm Name="GridForm" Version="1">
          <Layout Kind="Grid" Cols="1fr,1fr" Rows="auto"/>
          <Controls>
            <Button Id="btn" Col="0" Row="0" TabIndex="0" Text="A"/>
            <Button Id="btn2" Col="1" Row="0" TabIndex="1" Text="B"/>
          </Controls>
        </WebForm>
        """;

    private static string WithSubs(FormTarget target, params string[] subs)
    {
        var code = FormScaffolder.Create("GridForm", target).CodeText;
        var marker = target == FormTarget.Web ? "    ' Your event handlers go here" : "    ' Your event handlers go here.";
        var at = code.IndexOf(marker, StringComparison.Ordinal);
        return code[..at] + string.Concat(subs.Select(s => "    " + s + "\n    End Sub\n")) + code[at..];
    }

    private static FormEventRow EventRow(FormPropertyGridViewModel grid, string name) => grid.EventRows.Single(r => r.Name == name);

    // ==================================================================
    // The rows (VM)
    // ==================================================================

    /// <summary>
    /// Catalog-driven over every pair of kinds on both targets: the rows are the primary's wired events, in its order, that
    /// the other member wires as the SAME event (name, args, name on the target). ⚠ The expectation mirrors
    /// <c>SharedEvents</c>; the named cases below (a TrackBar has no Click; a web Panel has no Paint) are the independent pins.
    /// </summary>
    [Test]
    public void EveryPairOfKinds_TheEventRows_AreTheEventsBothWire_InThePrimarysOrder()
    {
        var findings = new List<string>();
        var pairs = 0;
        foreach (var target in new[] { FormTarget.WinForms, FormTarget.Web })
        {
            var kinds = FormControlCatalog.For(target).ToList();
            foreach (var first in kinds)
            {
                foreach (var primary in kinds)
                {
                    var document = new FormDocument
                    {
                        Target = target, Name = "GridForm", Width = 640, Height = 480,
                        Layout = target == FormTarget.Web ? new FormLayout { Kind = FormLayoutKind.Grid, Cols = "1fr", Rows = "auto" } : null
                    };
                    FormCatalogShapes.Canonical(document, first, "a");
                    FormCatalogShapes.Canonical(document, primary, "b");
                    var file = FormDocumentReader.Read(target == FormTarget.Web ? "GridForm.blwebform" : "GridForm.blform",
                        FormDocumentWriter.Create(document));
                    var grid = new FormPropertyGridViewModel();
                    grid.Load(file);
                    grid.SetSelection(new[] { file.Model.FindById("a")!, file.Model.FindById("b")! });
                    pairs++;

                    var theirs = FormEvents.WiredOn(first, target).ToList();
                    var expected = FormEvents.WiredOn(primary, target)
                        .Where(e => theirs.Any(t => t.Name == e.Name && (t.WinFormsArgs ?? "EventArgs") == (e.WinFormsArgs ?? "EventArgs") &&
                                                    FormEvents.NameOn(t, target) == FormEvents.NameOn(e, target)))
                        .Select(e => e.Name).ToList();
                    var actual = grid.EventRows.Select(r => r.Name).ToList();
                    if (!actual.SequenceEqual(expected))
                    {
                        findings.Add($"{target} {first.Kind}+{primary.Kind}: [{string.Join(",", actual)}] expected [{string.Join(",", expected)}]");
                    }

                    if (grid.EventRows.Any(r => r.Owners.Count != 2 || !ReferenceEquals(r.Owner.Control, file.Model.FindById("b"))))
                    {
                        findings.Add($"{target} {first.Kind}+{primary.Kind}: a row's owners are not [a, b] with b the primary");
                    }
                }
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(pairs, Is.GreaterThan(500));
            Assert.That(findings, Is.Empty, string.Join("\n", findings.Take(30)));
        });
    }

    [Test]
    public void AButtonAndATrackBar_ShareNoClick_AndAWebPanelAndButton_ShareNoPaint()
    {
        var doc = MultiDoc.Replace("</Controls>",
            """<TrackBar Id="trk" X="300" Y="16" Width="100" Height="45" TabIndex="5"/></Controls>""");
        var (_, winforms) = OpenDoc(doc, "btn", "trk");
        winforms.IsEventsMode = true;

        var web = new FormPropertyGridViewModel();
        var webFile = FormDocumentReader.Read("GridForm.blwebform", WebMultiDoc.Replace("</Controls>",
            """<Panel Id="pnl" Col="0" Row="0" TabIndex="2"/></Controls>"""));
        web.Load(webFile);
        web.SetSelection(new[] { webFile.Model.FindById("btn")!, webFile.Model.FindById("pnl")! });

        Assert.Multiple(() =>
        {
            Assert.That(winforms.EventRows.Select(r => r.Name), Has.No.Member("Click"), "a TrackBar has no Click on its row");
            Assert.That(web.EventRows.Select(r => r.Name), Has.No.Member("Paint").And.Member("Click"));
        });
    }

    [Test]
    public void AHandlerTheMembersDoNotShare_IsBlank_AndASharedOneIsShown()
    {
        var doc = MultiDoc.Replace("""Text="OK" BackColor="Red"/>""",
            """Text="OK" BackColor="Red"><Bind Event="Click" Handler="Shared"/></Button>""");
        var (file, grid) = OpenDoc(doc, "btn", "btn2");
        grid.IsEventsMode = true;
        Assert.That(file.Model.FindById("btn2")!.Binds.Single().Handler, Is.EqualTo("Shared"), "precondition: btn2 bound");

        Assert.That(EventRow(grid, "Click").Handler, Is.Empty, "btn unbound, btn2 bound: blank");

        file.Model.FindById("btn")!.Binds.Add(new FormBind { Event = "Click", Handler = "Shared" });
        EventRow(grid, "Click").HandlerChanged();
        Assert.That(EventRow(grid, "Click").Handler, Is.EqualTo("Shared"), "both bound to it: shown");
    }

    /// <summary>
    /// ⚠ Today the intersection changes nothing: a shared row is the SAME event under <c>SameEvent</c> (name, args, name on
    /// the target, handler shape), so every owner's <c>FittingHandlers</c> is the same list. The intersection is the
    /// defence for a future fitting rule that depends on the owner; this test pins the list and its instance stability.
    /// </summary>
    [Test]
    public void TheChoices_AreTheHandlersEveryMemberFits_TheSameInstanceWhileUnchanged()
    {
        var (_, grid) = Open("btn", "lbl");
        grid.IsEventsMode = true;
        grid.CodeBehindText = WithSubs(FormTarget.WinForms,
            "Private Sub Common(sender As Object, e As EventArgs)",
            "Private Sub Mice(sender As Object, e As MouseEventArgs)");
        var click = EventRow(grid, "Click");
        var first = click.Choices;

        grid.CodeBehindText = grid.CodeBehindText + "\n"; // a new text, the same Subs

        Assert.Multiple(() =>
        {
            Assert.That(first, Does.Contain("Common").And.Not.Contain("Mice"), "Mice does not fit Click");
            Assert.That(click.Choices, Is.SameAs(first), "the same instance while unchanged (a pick must not swap it)");
        });
    }

    [Test]
    public void PickingAHandler_BindsEveryMember_WithOneEdited_AndClearingUnbindsThem_CodeUntouched()
    {
        var (file, grid) = Open("btn", "btn2");
        grid.IsEventsMode = true;
        var code = WithSubs(FormTarget.WinForms, "Private Sub Common(sender As Object, e As EventArgs)");
        grid.CodeBehindText = code;
        var edits = Edits(grid);

        EventRow(grid, "Click").Commit("Common");

        Assert.Multiple(() =>
        {
            Assert.That(edits(), Is.EqualTo(1), "ONE Edited for both");
            Assert.That(new[] { "btn", "btn2" }.Select(id => file.Model.FindById(id)!.Binds.SingleOrDefault()?.Handler),
                Is.All.EqualTo("Common"));
            Assert.That(EventRow(grid, "Click").Handler, Is.EqualTo("Common"));
        });

        EventRow(grid, "Click").Commit("");

        Assert.Multiple(() =>
        {
            Assert.That(edits(), Is.EqualTo(2), "ONE more Edited for the clear");
            Assert.That(new[] { "btn", "btn2" }.SelectMany(id => file.Model.FindById(id)!.Binds), Is.Empty);
            Assert.That(grid.CodeBehindText, Is.EqualTo(code), "the code is never touched");
        });
    }

    [Test]
    public void ATypedNameAnyMemberRefuses_IsRefused_AndANewLegalName_AsksOnceForEveryOwner()
    {
        var (file, grid) = Open("btn", "btn2");
        grid.IsEventsMode = true;
        grid.CodeBehindText = WithSubs(FormTarget.WinForms);
        var requests = new List<FormHandlerRequest>();
        grid.HandlerRequested += (_, r) => requests.Add(r);
        var click = EventRow(grid, "Click");

        click.Commit("btn2"); // a member's id

        Assert.Multiple(() =>
        {
            Assert.That(click.Refusal, Is.Not.Null.And.Contains("btn2"));
            Assert.That(requests, Is.Empty);
        });

        click.Commit("DoIt");

        Assert.Multiple(() =>
        {
            Assert.That(requests, Has.Count.EqualTo(1), "ONE request");
            Assert.That(requests[0].Owners.Select(o => o.Control!.Id), Is.EqualTo(new[] { "btn", "btn2" }), "carrying both, primary last");
            Assert.That(requests[0].Owner.Control, Is.SameAs(file.Model.FindById("btn2")));
        });
    }

    // ==================================================================
    // The host (the REAL document view model): one stub, every owner bound, one write, the selection kept
    // ==================================================================

    private static (CodeEditorDocumentViewModel Vm, Dictionary<string, string> Files) OpenHost(
        string doc, FormTarget target, string code, params string[] select)
    {
        var scaffold = FormScaffolder.Create("GridForm", target);
        var contents = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/proj/" + scaffold.DocumentFileName] = doc,
            ["/proj/" + scaffold.CodeFileName] = code
        };
        var files = new Mock<IFileService>();
        files.Setup(f => f.ReadFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string p, CancellationToken _) =>
            {
                // A slow disk on demand: the NEXT read of the .bas waits on the gate (then the gate clears), its continuation
                // running synchronously on the thread that opens it (the test's).
                if (_readGate is { } gate && p.EndsWith(".bas", StringComparison.Ordinal))
                {
                    _readGate = null;
                    return gate.Task.ContinueWith(_ => contents[p], CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                }

                return Task.FromResult(contents[p]);
            });
        files.Setup(f => f.FileExistsAsync(It.IsAny<string>())).Returns((string p) => Task.FromResult(contents.ContainsKey(p)));
        files.Setup(f => f.WriteFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string p, string text, CancellationToken _) =>
            {
                contents[p] = text;
                return Task.CompletedTask;
            });

        var events = new Mock<IEventAggregator>();
        _diagnostics = new List<DesignerDiagnosticsEvent>();
        var diagnostics = _diagnostics;
        events.Setup(e => e.Publish(It.IsAny<DesignerDiagnosticsEvent>())).Callback((DesignerDiagnosticsEvent e) => diagnostics.Add(e));
        var vm = new CodeEditorDocumentViewModel(files.Object, events.Object)
        {
            FilePath = "/proj/" + scaffold.DocumentFileName
        };
        vm.SetContent(doc);
        Assert.That(vm.EnterDesignModeForFormDocument(), Is.True, "precondition: the designer opens");
        vm.Selection.SetRange(select.Select(id => vm.DesignDocument!.FindById(id)!).ToList());
        vm.PropertyGrid.IsEventsMode = true;
        return (vm, contents);
    }

    /// <summary>When set, the next .bas read waits on it (see <see cref="OpenHost"/>).</summary>
    [ThreadStatic] private static TaskCompletionSource<bool>? _readGate;

    /// <summary>What the host published to the Error List in the last <see cref="OpenHost"/>.</summary>
    [ThreadStatic] private static List<DesignerDiagnosticsEvent>? _diagnostics;

    private static int Count(string text, string what)
    {
        var n = 0;
        for (var at = text.IndexOf(what, StringComparison.Ordinal); at >= 0; at = text.IndexOf(what, at + 1, StringComparison.Ordinal))
        {
            n++;
        }

        return n;
    }

    [Test]
    public void DoubleClickingAMergedRow_WritesOneStubForThePrimary_BindsBoth_OneStep_AndKeepsTheSelection()
    {
        var (vm, files) = OpenHost(MultiDoc, FormTarget.WinForms, WithSubs(FormTarget.WinForms), "btn", "btn2");
        var before = vm.Text;

        vm.PropertyGrid.EventRows.Single(r => r.Name == "Click").RequestHandler();

        var code = files["/proj/GridForm.bas"];
        Assert.Multiple(() =>
        {
            Assert.That(Count(code, "Sub btn2_Click("), Is.EqualTo(1), "ONE stub, named after the PRIMARY (selected last)");
            Assert.That(Count(code, "Sub btn_Click("), Is.Zero);
            Assert.That(new[] { "btn", "btn2" }.Select(id => vm.DesignDocument!.FindById(id)!.Binds.SingleOrDefault()?.Handler),
                Is.All.EqualTo("btn2_Click"), "bound on BOTH");
            Assert.That(vm.Selection.Controls.Select(c => c.Id), Is.EqualTo(new[] { "btn", "btn2" }), "the selection is kept");
        });

        vm.UndoDesignerEditCommand.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(vm.Text, Is.EqualTo(before), "ONE undo removes both binds");
            Assert.That(vm.TextDocument.UndoStack.CanUndo, Is.False, "it was ONE step");
        });
    }

    [Test]
    public void APrimaryAlreadyBound_IsNavigatedTo_AndTheOtherMemberGetsItsHandler()
    {
        var doc = MultiDoc.Replace("""Text="OK" BackColor="Red"/>""",
            """Text="OK" BackColor="Red"><Bind Event="Click" Handler="AnyClick"/></Button>""");
        var (vm, files) = OpenHost(doc, FormTarget.WinForms,
            WithSubs(FormTarget.WinForms, "Private Sub AnyClick(sender As Object, e As EventArgs)"), "btn", "btn2");
        var code = files["/proj/GridForm.bas"];

        vm.PropertyGrid.EventRows.Single(r => r.Name == "Click").RequestHandler();

        Assert.Multiple(() =>
        {
            Assert.That(files["/proj/GridForm.bas"], Is.EqualTo(code), "navigated: no stub written");
            Assert.That(vm.DesignDocument!.FindById("btn")!.Binds.SingleOrDefault()?.Handler, Is.EqualTo("AnyClick"));
        });
    }

    /// <summary>
    /// The HOST route (review of f4d8007d, item 5): the grid passed a typed name (its pushed code-behind did not have it),
    /// but the code-behind on disk has a <c>DoIt</c> that does not fit Click — the host's own check refuses it, first for
    /// the NON-primary owner btn. The refusal reaches the merged row (its pane says why, the cell reverts) and NOTHING
    /// reaches the Error List; nothing is written.
    /// </summary>
    [Test]
    public void AHostRefusalOfATypedName_ForANonPrimaryOwner_RevertsTheMergedCell_AndNeverReachesTheErrorList()
    {
        var (vm, files) = OpenHost(MultiDoc, FormTarget.WinForms,
            WithSubs(FormTarget.WinForms, "Private Sub DoIt(sender As Object, e As MouseEventArgs)"), "btn", "btn2");
        vm.PropertyGrid.CodeBehindText = WithSubs(FormTarget.WinForms); // the grid's view: no DoIt yet
        var click = vm.PropertyGrid.EventRows.Single(r => r.Name == "Click");
        var reverted = new List<FormHandlerCellRevert>();
        vm.PropertyGrid.HandlerCellReverted += (_, r) => reverted.Add(r);
        var code = files["/proj/GridForm.bas"];
        var text = vm.Text;

        click.Commit("DoIt");

        Assert.Multiple(() =>
        {
            Assert.That(click.Refusal, Is.Not.Null.And.Contains("DoIt"), "the merged row's pane says why");
            Assert.That(reverted.Select(r => r.Row), Has.Member(click), "the merged cell reverts");
            Assert.That(_diagnostics, Is.Empty, "nothing in the Error List");
            Assert.That(files["/proj/GridForm.bas"], Is.EqualTo(code), "no stub written");
            Assert.That(vm.Text, Is.EqualTo(text), "no bind written");
        });
    }

    /// <summary>
    /// Review of f4d8007d, item 2: the gesture queue's de-dupe key covers EVERY owner. Two requests for the same event with
    /// the same primary (btn2) but a different NON-primary owner — {btn, btn2} then {lbl, btn2} — while the first is still
    /// waiting on a slow disk: BOTH run (the second binds lbl too). Keyed on the primary alone, the second was dropped.
    /// </summary>
    [Test]
    public async Task TwoRequestsDifferingOnlyInANonPrimaryOwner_BothRun()
    {
        var (vm, _) = OpenHost(MultiDoc, FormTarget.WinForms, WithSubs(FormTarget.WinForms), "btn", "btn2");
        var gate = new TaskCompletionSource<bool>();
        _readGate = gate;

        vm.PropertyGrid.EventRows.Single(r => r.Name == "Click").RequestHandler(); // waits on the gate
        vm.Selection.SetRange(new[] { vm.DesignDocument!.FindById("lbl")!, vm.DesignDocument.FindById("btn2")! });
        vm.PropertyGrid.EventRows.Single(r => r.Name == "Click").RequestHandler(); // queued behind it
        gate.SetResult(true);

        var lbl = vm.DesignDocument!.FindById("lbl")!;
        for (var i = 0; i < 250 && lbl.Binds.Count == 0; i++)
        {
            await Task.Delay(20);
        }

        Assert.Multiple(() =>
        {
            Assert.That(vm.DesignDocument!.FindById("btn")!.Binds.SingleOrDefault()?.Handler, Is.EqualTo("btn2_Click"), "the first ran");
            Assert.That(lbl.Binds.SingleOrDefault()?.Handler, Is.EqualTo("btn2_Click"), "the second ran too — never de-duped away");
        });
    }

    [Test]
    public void AWebPair_GetsOneDomEventStub_AndBothBindClick()
    {
        var (vm, files) = OpenHost(WebMultiDoc, FormTarget.Web, WithSubs(FormTarget.Web), "btn", "btn2");

        vm.PropertyGrid.EventRows.Single(r => r.Name == "Click").RequestHandler();

        Assert.Multiple(() =>
        {
            Assert.That(Count(files["/proj/GridForm.bas"], "Sub btn2_Click(e As DomEvent)"), Is.EqualTo(1));
            Assert.That(new[] { "btn", "btn2" }.Select(id => vm.DesignDocument!.FindById(id)!.Binds.SingleOrDefault()),
                Has.All.Matches<FormBind?>(b => b is { Event: "click", Handler: "btn2_Click" }));
        });
    }

    /// <summary>
    /// The canvas route (D-8): with btn2 and btn selected (btn the primary, as the double-click's first press makes it),
    /// activating btn writes ONE <c>btn_Click</c> bound on BOTH Buttons — a TrackBar also selected gets nothing (it has no
    /// Click) — one write, the selection kept. An unselected control is wired alone, as today.
    /// </summary>
    [Test]
    public async Task TheCanvasDoubleClickOnAMember_WiresEveryMemberWithTheEvent_ButNotATrackBar()
    {
        var doc = MultiDoc.Replace("</Controls>",
            """<TrackBar Id="trk" X="300" Y="16" Width="100" Height="45" TabIndex="5"/></Controls>""");
        var (vm, files) = OpenHost(doc, FormTarget.WinForms, WithSubs(FormTarget.WinForms), "trk", "btn2", "btn");
        var before = vm.Text;

        await vm.ActivateControlCommand.ExecuteAsync(vm.DesignDocument!.FindById("btn"));

        Assert.Multiple(() =>
        {
            Assert.That(Count(files["/proj/GridForm.bas"], "Sub btn_Click("), Is.EqualTo(1), "ONE stub after the clicked control");
            Assert.That(new[] { "btn", "btn2" }.Select(id => vm.DesignDocument!.FindById(id)!.Binds.SingleOrDefault()?.Handler),
                Is.All.EqualTo("btn_Click"));
            Assert.That(vm.DesignDocument!.FindById("trk")!.Binds, Is.Empty, "a TrackBar has no Click: left unbound");
            Assert.That(vm.Selection.Controls.Select(c => c.Id), Is.EqualTo(new[] { "trk", "btn2", "btn" }), "kept");
        });

        vm.UndoDesignerEditCommand.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(vm.Text, Is.EqualTo(before));
            Assert.That(vm.TextDocument.UndoStack.CanUndo, Is.False, "ONE step");
        });

        // An UNSELECTED control alone, as today.
        var (single, singleFiles) = OpenHost(MultiDoc, FormTarget.WinForms, WithSubs(FormTarget.WinForms), "btn", "btn2");
        await single.ActivateControlCommand.ExecuteAsync(single.DesignDocument!.FindById("lbl"));
        Assert.Multiple(() =>
        {
            Assert.That(Count(singleFiles["/proj/GridForm.bas"], "Sub lbl_Click("), Is.EqualTo(1));
            Assert.That(single.DesignDocument!.FindById("btn")!.Binds, Is.Empty, "the selection's members are not wired");
        });
    }
}
