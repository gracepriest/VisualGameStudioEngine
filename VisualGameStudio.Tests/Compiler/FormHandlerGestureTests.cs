using BasicLang.Forms;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Events;
using VisualGameStudio.Shell.ViewModels.Designer;
using VisualGameStudio.Shell.ViewModels.Documents;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 22 end to end at the host: double-clicking a control on the canvas puts a handler in the
/// user's <c>.bas</c>, wires it in the document, and opens the file at it.
///
/// <para>⛔⛔ <b>Caller tests, like <see cref="FormCodeBehindWriteTests"/>.</b> The planner is
/// covered from thirty angles by <see cref="FormHandlerPlanTests"/> and would stay green if nothing
/// ever called it — which is this repo's signature failure, five times over. These drive the command
/// the canvas actually invokes and assert on what lands in the FILE SERVICE and on the event bus.</para>
/// </summary>
[TestFixture]
public class FormHandlerGestureTests
{
    private const string Dir = "/proj/";

    private sealed class Files
    {
        public readonly Dictionary<string, string> Contents = new(StringComparer.Ordinal);
        public readonly List<string> Writes = new();

        /// <summary>Every write throws (a read-only or locked file), as the disk can.</summary>
        public bool FailWrites;

        /// <summary>When set, the NEXT read of <see cref="GatedPath"/> waits on it (then the gate clears) — a slow disk.</summary>
        public TaskCompletionSource<string>? ReadGate;

        /// <summary>When set, the NEXT write waits on it before landing (then the gate clears).</summary>
        public TaskCompletionSource? WriteGate;

        public string? GatedPath;

        public IFileService Service
        {
            get
            {
                var mock = new Mock<IFileService>();

                mock.Setup(f => f.ReadFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .Returns((string p, CancellationToken _) =>
                    {
                        if (ReadGate is { } gate && p == GatedPath)
                        {
                            ReadGate = null;
                            return gate.Task;
                        }

                        return Task.FromResult(Contents[p]);
                    });

                mock.Setup(f => f.FileExistsAsync(It.IsAny<string>()))
                    .Returns((string p) => Task.FromResult(Contents.ContainsKey(p)));

                mock.Setup(f => f.WriteFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .Returns((string p, string text, CancellationToken _) =>
                    {
                        if (FailWrites)
                        {
                            return Task.FromException(new IOException($"'{p}' is read-only"));
                        }

                        if (WriteGate is { } writeGate)
                        {
                            WriteGate = null;
                            // Synchronously on the thread that opens the gate (the test's): the view model's continuation
                            // touches thread-affine state, as it does on the UI thread in the IDE.
                            return writeGate.Task.ContinueWith(_ =>
                            {
                                Contents[p] = text;
                                Writes.Add(p);
                            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                        }

                        Contents[p] = text;
                        Writes.Add(p);
                        return Task.CompletedTask;
                    });

                return mock.Object;
            }
        }
    }

    private sealed class Harness
    {
        public required CodeEditorDocumentViewModel Vm { get; init; }
        public required Files Files { get; init; }
        public required List<NavigateToFileEvent> Navigations { get; init; }
        public required List<DesignerDiagnosticsEvent> Diagnostics { get; init; }
        public required string CodePath { get; init; }
        public required FormControl Control { get; init; }
    }

    /// <summary>A scaffolded pair on "disk" with one control already placed, open in design mode.</summary>
    private static Harness Open(FormTarget target, string kind = "Button", string id = "btnLogin")
    {
        var scaffold = FormScaffolder.Create("LoginForm", target);

        // Build the document through the real model + writer, so the text under test is the text the
        // designer itself would have saved.
        var document = new FormDocument { Target = target, Name = "LoginForm" };
        if (target == FormTarget.WinForms)
        {
            document.Width = 800;
            document.Height = 450;
            document.Text = "LoginForm";
        }
        else
        {
            document.Layout = new FormLayout
            {
                Kind = FormLayoutKind.Grid, Cols = "auto,1fr", Rows = "auto", Gap = "8px"
            };
        }

        var control = new FormControl { Kind = kind, Id = id, TabIndex = 0 };
        control.Geometry = target == FormTarget.WinForms
            ? new PixelGeometry { X = 40, Y = 40, Width = 75, Height = 23 }
            : new GridGeometry { Col = 1, Row = 1 };
        document.Controls.Add(control);

        var files = new Files();
        files.Contents[Dir + scaffold.DocumentFileName] =
            BasicLang.Forms.Serialization.FormDocumentWriter.Create(document);
        files.Contents[Dir + scaffold.CodeFileName] = scaffold.CodeText;

        var navigations = new List<NavigateToFileEvent>();
        var diagnostics = new List<DesignerDiagnosticsEvent>();
        var events = new Mock<IEventAggregator>();
        events.Setup(e => e.Publish(It.IsAny<NavigateToFileEvent>()))
            .Callback((NavigateToFileEvent e) => navigations.Add(e));
        events.Setup(e => e.Publish(It.IsAny<DesignerDiagnosticsEvent>()))
            .Callback((DesignerDiagnosticsEvent e) => diagnostics.Add(e));

        var vm = new CodeEditorDocumentViewModel(files.Service, events.Object)
        {
            FilePath = Dir + scaffold.DocumentFileName
        };
        vm.Text = files.Contents[vm.FilePath];

        files.Writes.Clear();

        return new Harness
        {
            Vm = vm,
            Files = files,
            Navigations = navigations,
            Diagnostics = diagnostics,
            CodePath = Dir + scaffold.CodeFileName,
            Control = vm.DesignDocument!.Controls[0]
        };
    }

    private static async Task ActivateAsync(Harness h) =>
        await h.Vm.ActivateControlCommand.ExecuteAsync(h.Control);

    // ==================================================================
    // Creating
    // ==================================================================

    [Test]
    public async Task DoubleClickingAButtonWritesAHandlerIntoTheCodeBehind()
    {
        var h = Open(FormTarget.WinForms);

        await ActivateAsync(h);

        Assert.Multiple(() =>
        {
            Assert.That(h.Files.Writes, Does.Contain(h.CodePath));
            Assert.That(h.Files.Contents[h.CodePath],
                Does.Contain("Private Sub btnLogin_Click(sender As Object, e As EventArgs)"));
        });
    }

    /// <summary>
    /// ⛔ The stub and the wiring are one gesture. A Sub nothing wires never fires, and the user has
    /// no way to see why — the designer would have written code that looks finished and does nothing.
    /// </summary>
    [Test]
    public async Task DoubleClickingWiresTheHandlerInTheDocument()
    {
        var h = Open(FormTarget.WinForms);

        await ActivateAsync(h);

        var bind = h.Vm.DesignDocument!.Controls[0].Binds.SingleOrDefault();
        Assert.Multiple(() =>
        {
            Assert.That(bind, Is.Not.Null);
            Assert.That(bind!.Event, Is.EqualTo("Click"));
            Assert.That(bind.Handler, Is.EqualTo("btnLogin_Click"));
        });
    }

    /// <summary>The wiring has to reach the DOCUMENT TEXT, or the next save writes it back out.</summary>
    [Test]
    public async Task TheWiringReachesTheDocumentText()
    {
        var h = Open(FormTarget.WinForms);

        await ActivateAsync(h);

        Assert.That(h.Vm.Text, Does.Contain("btnLogin_Click"));
    }

    [Test]
    public async Task AWebFormGetsTheDomEventSignature()
    {
        var h = Open(FormTarget.Web);

        await ActivateAsync(h);

        Assert.That(h.Files.Contents[h.CodePath],
            Does.Contain("Private Sub btnLogin_Click(e As DomEvent)"));
    }

    /// <summary>
    /// ⛔ The substitution reaches the USER: a web Panel's double-click writes and opens the Click handler AND
    /// publishes an Info finding naming it — a notice only the planner knew would be as silent as none.
    /// </summary>
    [Test]
    public async Task AWebPanelsDoubleClick_OpensClick_AndReportsTheSubstitution()
    {
        var h = Open(FormTarget.Web, "Panel", "pnl");

        await ActivateAsync(h);

        var notice = h.Diagnostics.SelectMany(d => d.Diagnostics).SingleOrDefault();
        Assert.Multiple(() =>
        {
            Assert.That(h.Files.Contents[h.CodePath], Does.Contain("Private Sub pnl_Click(e As DomEvent)"));
            Assert.That(h.Navigations, Has.Count.EqualTo(1), "a notice does not stop the gesture");
            Assert.That(notice, Is.Not.Null, "the substitution must be reported");
            Assert.That(notice!.Id, Is.EqualTo(DesignCodes.DefaultEventNotOnTarget));
            Assert.That(notice.Severity, Is.EqualTo(VisualGameStudio.Core.Models.DiagnosticSeverity.Info));
            Assert.That(notice.Message, Does.Contain("Paint"));
        });
    }

    /// <summary>
    /// Slice 5 Task 6: ⛔ the Bind is written only AFTER the stub is. A code-behind write that fails (read-only, locked)
    /// leaves the document unwired — a Bind to a Sub that exists nowhere is a form that stops building — the Events tab
    /// offering nothing new, nothing opened, and the failure said.
    /// </summary>
    [Test]
    public async Task AFailedStubWrite_LeavesTheDocumentUnbound_AndTheGridsCodeUnchanged_AndIsReported()
    {
        var h = Open(FormTarget.WinForms);
        var textBefore = h.Vm.Text;
        var gridCodeBefore = h.Vm.PropertyGrid.CodeBehindText;
        h.Files.FailWrites = true;

        await ActivateAsync(h);

        Assert.Multiple(() =>
        {
            Assert.That(h.Control.Binds, Is.Empty, "no Bind to a Sub that was never written");
            Assert.That(h.Vm.Text, Is.EqualTo(textBefore), "the document text is untouched");
            Assert.That(h.Vm.PropertyGrid.CodeBehindText, Is.EqualTo(gridCodeBefore), "the grid never saw the unwritten stub");
            Assert.That(h.Navigations, Is.Empty, "nothing to open");
            Assert.That(h.Diagnostics.SelectMany(d => d.Diagnostics).Select(d => d.Message),
                Has.Some.Contains("read-only"), "the failure is said");
        });
    }

    [Test]
    public async Task AnOrdinaryDoubleClick_ReportsNothing()
    {
        var h = Open(FormTarget.Web);

        await ActivateAsync(h);

        Assert.That(h.Diagnostics, Is.Empty);
    }

    // ==================================================================
    // The code-behind already OPEN in its own tab (owner click-through D2, 2026-09-30)
    // ==================================================================

    /// <summary>
    /// Opens the code-behind as a second document — as <c>MainWindowViewModel.OpenFileAsync</c> does — and hands the
    /// designer the same lookup the shell gives it. Returns the open .bas document.
    /// </summary>
    private static CodeEditorDocumentViewModel OpenCodeBehind(Harness h)
    {
        var bas = new CodeEditorDocumentViewModel(h.Files.Service, new Mock<IEventAggregator>().Object)
        {
            FilePath = h.CodePath
        };
        bas.SetContent(h.Files.Contents[h.CodePath]);
        h.Vm.OpenDocumentLookup = path => string.Equals(path, h.CodePath, StringComparison.OrdinalIgnoreCase) ? bas : null;
        return bas;
    }

    /// <summary>
    /// ⛔ D2: the handler was written to disk and the ALREADY-OPEN tab kept showing the old text until it was closed and
    /// reopened. A clean open document is written THROUGH: the tab shows the stub at once, stays clean, and the disk has it.
    /// </summary>
    [Test]
    public async Task AnOpenCleanCodeBehind_ShowsTheNewHandlerAtOnce_AndStaysClean()
    {
        var h = Open(FormTarget.WinForms);
        var bas = OpenCodeBehind(h);

        await ActivateAsync(h);

        Assert.Multiple(() =>
        {
            Assert.That(bas.Text, Does.Contain("Private Sub btnLogin_Click(sender As Object, e As EventArgs)"),
                "the open tab must show the handler without being reopened");
            Assert.That(bas.IsDirty, Is.False, "the buffer and the disk hold the same text");
            Assert.That(h.Files.Contents[h.CodePath], Is.EqualTo(bas.Text), "and the disk has it too");
            Assert.That(h.Navigations, Has.Count.EqualTo(1));
        });
    }

    /// <summary>
    /// ⛔ D2, the other half: an open code-behind with UNSAVED edits is never clobbered. The stub is inserted INTO the
    /// buffer (the user's edits and the handler both there, still unsaved), and nothing is written to disk — writing the
    /// buffer would save the user's edits behind their back, and writing the old disk text would lose them on the next save.
    /// </summary>
    [Test]
    public async Task AnOpenCodeBehindWithUnsavedEdits_GetsTheHandlerInItsBuffer_AndKeepsTheEdits()
    {
        var h = Open(FormTarget.WinForms);
        var bas = OpenCodeBehind(h);
        var onDisk = h.Files.Contents[h.CodePath];
        bas.ReplaceContent(onDisk.Replace("End Class", "    ' my unsaved note\nEnd Class"));
        Assert.That(bas.IsDirty, Is.True, "precondition: the tab has unsaved edits");

        await ActivateAsync(h);

        Assert.Multiple(() =>
        {
            Assert.That(bas.Text, Does.Contain("' my unsaved note"), "the user's unsaved edit survives");
            Assert.That(bas.Text, Does.Contain("Private Sub btnLogin_Click("), "and the handler is in the buffer");
            Assert.That(bas.IsDirty, Is.True, "still unsaved — the user decides when to save");
            Assert.That(h.Files.Writes, Does.Not.Contain(h.CodePath), "nothing was written to disk behind the user's back");
            Assert.That(h.Files.Contents[h.CodePath], Is.EqualTo(onDisk));
            Assert.That(h.Navigations, Has.Count.EqualTo(1), "the gesture still opens the handler");
        });
    }

    /// <summary>A second double-click on an open, dirty code-behind finds the handler IN THE BUFFER and writes nothing.</summary>
    [Test]
    public async Task ASecondDoubleClick_FindsTheHandlerInTheOpenBuffer()
    {
        var h = Open(FormTarget.WinForms);
        var bas = OpenCodeBehind(h);
        bas.ReplaceContent(h.Files.Contents[h.CodePath] + "' edit\n");

        await ActivateAsync(h);
        await ActivateAsync(h);

        Assert.That(bas.Text.Split("Private Sub btnLogin_Click(").Length - 1, Is.EqualTo(1), "one handler, never two");
    }

    /// <summary>
    /// The designer's SAVE regenerates its regions into the same file, through the same route: an open clean
    /// code-behind shows the generated field/wiring at once (it used to be overwritten by the stale tab's next save).
    /// </summary>
    [Test]
    public async Task SavingTheForm_RegeneratesIntoAnOpenCodeBehind()
    {
        var h = Open(FormTarget.WinForms);
        var bas = OpenCodeBehind(h);
        Assert.That(bas.Text, Does.Not.Contain("btnLogin = New Button"), "precondition: the scaffold has no field for it yet");

        Assert.That(await h.Vm.SaveAsync(), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(bas.Text, Does.Contain("btnLogin"), "the open tab shows the regenerated region");
            Assert.That(bas.IsDirty, Is.False);
            Assert.That(h.Files.Contents[h.CodePath], Is.EqualTo(bas.Text));
        });
    }

    /// <summary>
    /// ⛔ Who calls it in a shipping build: the shell's file-open route must hand every document the lookup, or none of the
    /// above ever runs in the IDE. Read from the source, as the AXAML-binding checks do.
    /// </summary>
    [Test]
    public void TheShellsFileOpenRoute_GivesEveryDocumentTheOpenDocumentLookup()
    {
        var source = File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..",
            "VisualGameStudio.Shell", "ViewModels", "MainWindowViewModel.cs"));
        var openRoute = source.IndexOf("private async Task OpenFileAsync(string filePath)", StringComparison.Ordinal);
        Assert.That(openRoute, Is.GreaterThan(0), "OpenFileAsync not found");

        var creation = source.IndexOf("new CodeEditorDocumentViewModel(", openRoute, StringComparison.Ordinal);
        var body = source.Substring(creation, 1200);

        Assert.That(body, Does.Contain("OpenDocumentLookup ="), "the file-open route must wire the open-document lookup");
    }

    // ==================================================================
    // Navigating
    // ==================================================================

    [Test]
    public async Task DoubleClickingOpensTheCodeBehindAtTheHandler()
    {
        var h = Open(FormTarget.WinForms);

        await ActivateAsync(h);

        var navigation = h.Navigations.SingleOrDefault();
        Assert.Multiple(() =>
        {
            Assert.That(navigation, Is.Not.Null, "the gesture is worthless if it does not take you there");
            Assert.That(navigation!.FilePath, Is.EqualTo(h.CodePath));
            Assert.That(navigation.Line, Is.GreaterThan(0));
        });
    }

    /// <summary>⛔ Twice must not declare the Sub twice — that would break the user's build for them.</summary>
    [Test]
    public async Task DoubleClickingTwiceWritesTheHandlerOnce()
    {
        var h = Open(FormTarget.WinForms);

        await ActivateAsync(h);
        await ActivateAsync(h);

        var text = h.Files.Contents[h.CodePath];
        var count = text.Split("Private Sub btnLogin_Click").Length - 1;

        Assert.Multiple(() =>
        {
            Assert.That(count, Is.EqualTo(1));
            Assert.That(h.Navigations, Has.Count.EqualTo(2), "both gestures still navigate");
        });
    }

    [Test]
    public async Task TheSecondGestureLandsOnTheSameLineAsTheFirst()
    {
        var h = Open(FormTarget.WinForms);

        await ActivateAsync(h);
        await ActivateAsync(h);

        Assert.That(h.Navigations[1].Line, Is.EqualTo(h.Navigations[0].Line));
    }

    // ==================================================================
    // Refusing
    // ==================================================================

    /// <summary>
    /// ⚠ A refusal the user never sees is indistinguishable from the designer quietly not working —
    /// the same rule the region writer's refusals follow.
    /// </summary>
    [Test]
    public async Task ARefusalIsReportedRatherThanSilent()
    {
        var h = Open(FormTarget.WinForms);

        // A code-behind the designer does not own: no regions at all (D12's import case).
        h.Files.Contents[h.CodePath] = "Public Class LoginForm\nEnd Class\n";

        await ActivateAsync(h);

        Assert.Multiple(() =>
        {
            Assert.That(h.Files.Writes, Does.Not.Contain(h.CodePath), "a refusal writes nothing");
            Assert.That(h.Diagnostics, Is.Not.Empty, "and says so");
            Assert.That(h.Navigations, Is.Empty, "and does not pretend to navigate");
        });
    }

    /// <summary>
    /// ⛔ The refusal must not blank the file. The natural caller shape is "write the plan's text
    /// back", so a plan that returned an empty string on refusal would turn a polite no into silent
    /// data loss.
    /// </summary>
    [Test]
    public async Task ARefusalLeavesTheCodeBehindIntact()
    {
        var h = Open(FormTarget.WinForms);
        const string original = "Public Class LoginForm\nEnd Class\n";
        h.Files.Contents[h.CodePath] = original;

        await ActivateAsync(h);

        Assert.That(h.Files.Contents[h.CodePath], Is.EqualTo(original));
    }

    [Test]
    public async Task AMissingCodeBehindIsReportedRatherThanThrowing()
    {
        var h = Open(FormTarget.WinForms);
        h.Files.Contents.Remove(h.CodePath);

        await ActivateAsync(h);

        Assert.Multiple(() =>
        {
            Assert.That(h.Diagnostics, Is.Not.Empty);
            Assert.That(h.Navigations, Is.Empty);
        });
    }

    // ==================================================================
    // Review round 4 (of a5c5955c / eec527f6) — the host and the Events tab
    // ==================================================================

    /// <summary>The harness in Events mode with the control selected — the grid's rows for it built.</summary>
    private static FormEventRow ShowEvents(Harness h, string eventName = "Click")
    {
        h.Vm.Selection.Set(h.Control);
        h.Vm.PropertyGrid.IsEventsMode = true;
        Assert.That(h.Vm.PropertyGrid.SelectedControl, Is.SameAs(h.Control), "precondition: the grid shows the control");
        return h.Vm.PropertyGrid.EventRows.Single(r => r.Name == eventName);
    }

    /// <summary>
    /// ⛔ CRITICAL (round 4, ruling 1): a HOST bind — the double-click, the canvas gesture, a typed new name — edits the
    /// FormBind directly, so the row must be TOLD its Handler changed. Without it the combo's one-way Text stays "" with
    /// focus in it, and the next LostFocus commits "" — an Unbind that orphans the stub just written.
    /// </summary>
    [Test]
    public async Task AHostBind_TellsTheRowItsHandlerChanged()
    {
        var h = Open(FormTarget.WinForms);
        var row = ShowEvents(h);
        var notified = new List<string?>();
        row.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        await ActivateAsync(h);

        Assert.Multiple(() =>
        {
            Assert.That(row.Handler, Is.EqualTo("btnLogin_Click"), "precondition: bound");
            Assert.That(notified, Does.Contain(nameof(FormEventRow.Handler)), "the row heard about the host's bind");
        });
    }

    /// <summary>
    /// Ruling 5: a typed name the HOST refuses (the code changed since the grid's scan — here a Function of that name is in
    /// the file the grid has not seen) is said in the ROW's description, never as BL8014 "no code-behind" in the Error List,
    /// and the row is told to re-read its handler so the cell reverts.
    /// </summary>
    [Test]
    public async Task AHostRefusalOfATypedName_IsTheRowsRefusal_NotAnErrorListEntry()
    {
        var h = Open(FormTarget.WinForms);
        var row = ShowEvents(h);
        h.Vm.PropertyGrid.CodeBehindText = h.Files.Contents[h.CodePath];   // the grid's scan: no Foo
        var code = h.Files.Contents[h.CodePath];
        var end = code.LastIndexOf("End Class", StringComparison.Ordinal);
        h.Files.Contents[h.CodePath] = code[..end] + "    Private Function Foo() As Integer\n        Return 0\n    End Function\n" + code[end..];
        var notified = new List<string?>();
        row.PropertyChanged += (_, e) => notified.Add(e.PropertyName);
        var asked = new TaskCompletionSource();
        h.Vm.PropertyGrid.HandlerRequested += (_, _) => asked.TrySetResult();

        row.Commit("Foo");
        await asked.Task;
        await Task.Delay(50);

        Assert.Multiple(() =>
        {
            Assert.That(row.Refusal, Does.Contain("member"), "the row says why");
            Assert.That(h.Diagnostics.SelectMany(d => d.Diagnostics).Select(d => d.Id), Has.No.Member(DesignCodes.RegionAbsent));
            Assert.That(h.Control.Binds, Is.Empty);
            Assert.That(notified, Does.Contain(nameof(FormEventRow.Handler)), "the cell re-reads (reverts)");
        });
    }

    /// <summary>Ruling 5b: a refusal the ROW makes also tells the cell to re-read, so the typed text reverts.</summary>
    [Test]
    public void ARowRefusal_RevertsTheCell()
    {
        var h = Open(FormTarget.WinForms);
        var row = ShowEvents(h);
        var notified = new List<string?>();
        row.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        row.Commit("Dim");

        Assert.Multiple(() =>
        {
            Assert.That(row.Refusal, Is.Not.Null);
            Assert.That(notified, Does.Contain(nameof(FormEventRow.Handler)));
        });
    }

    /// <summary>
    /// Ruling 6a: a code-behind read that was started BEFORE a designer write must not land AFTER it and put the old
    /// text back — the grid would stop offering the Sub just written.
    /// </summary>
    [Test]
    public async Task AStaleCodeBehindRead_NeverOverwritesANewerWrite()
    {
        var h = Open(FormTarget.WinForms);
        ShowEvents(h);
        var old = h.Files.Contents[h.CodePath];
        var slow = new TaskCompletionSource<string>();
        h.Files.GatedPath = h.CodePath;
        h.Files.ReadGate = slow;
        h.Vm.PropertyGrid.RequestCodeBehindRefresh();   // a push, now waiting on the slow read

        await ActivateAsync(h);                          // writes btnLogin_Click; the grid gets the new text
        slow.SetResult(old);                             // the stale read lands last
        await Task.Delay(50);

        Assert.That(h.Vm.PropertyGrid.CodeBehindText, Does.Contain("btnLogin_Click"), "the newer text stands");
    }

    /// <summary>
    /// Ruling 6b: Enter and the LostFocus that follows it can ask for the same handler twice while the first write is
    /// still in flight. The second request is dropped: ONE write, ONE navigation.
    /// </summary>
    [Test]
    public async Task ASecondHandlerRequestWhileOneIsInFlight_IsDropped()
    {
        var h = Open(FormTarget.WinForms);
        ShowEvents(h);
        var gate = new TaskCompletionSource();
        h.Files.WriteGate = gate;

        var first = h.Vm.ActivateControlCommand.ExecuteAsync(h.Control);
        var second = h.Vm.ActivateControlCommand.ExecuteAsync(h.Control);
        gate.SetResult();
        await Task.WhenAll(first, second);

        Assert.Multiple(() =>
        {
            Assert.That(h.Files.Writes.Count(p => p == h.CodePath), Is.EqualTo(1), "one write");
            Assert.That(h.Navigations, Has.Count.EqualTo(1),
                "one navigation; diagnostics: " + string.Join(" | ", h.Diagnostics.SelectMany(d => d.Diagnostics).Select(d => d.Message)));
        });
    }

    /// <summary>
    /// Round 5 fix 2: gestures run ONE AT A TIME, in order — a DIFFERENT request arriving while one is in flight is queued,
    /// never dropped (only an identical one is). Both handlers are written and both open.
    /// </summary>
    [Test]
    public async Task ADifferentHandlerRequestWhileOneIsInFlight_IsQueued_AndBothComplete()
    {
        var h = Open(FormTarget.WinForms);
        ShowEvents(h);
        var gate = new TaskCompletionSource();
        h.Files.WriteGate = gate;

        var control = h.Vm.ActivateControlCommand.ExecuteAsync(h.Control);
        var form = h.Vm.ActivateFormCommand.ExecuteAsync(null);
        gate.SetResult();
        await Task.WhenAll(control, form);

        Assert.Multiple(() =>
        {
            Assert.That(h.Files.Contents[h.CodePath], Does.Contain("Sub btnLogin_Click(").And.Contain("Sub LoginForm_Load("),
                "both stubs, the second written after the first");
            Assert.That(h.Navigations, Has.Count.EqualTo(2));
        });
    }

    /// <summary>
    /// Round 5 fix 2: a TYPED name whose write fails is the ROW's refusal (pane + revert) — not an Error List entry.
    /// </summary>
    [Test]
    public async Task ATypedNameWhoseWriteFails_IsTheRowsRefusal_NotAnErrorListEntry()
    {
        var h = Open(FormTarget.WinForms);
        var row = ShowEvents(h);
        h.Vm.PropertyGrid.CodeBehindText = h.Files.Contents[h.CodePath];
        h.Files.FailWrites = true;
        var asked = new TaskCompletionSource();
        h.Vm.PropertyGrid.HandlerRequested += (_, _) => asked.TrySetResult();

        row.Commit("Typed");
        await asked.Task;
        await Task.Delay(50);

        Assert.Multiple(() =>
        {
            Assert.That(row.Refusal, Does.Contain("read-only"), "the row says why");
            Assert.That(h.Diagnostics.SelectMany(d => d.Diagnostics), Is.Empty, "no Error List entry for a typed name");
            Assert.That(h.Control.Binds, Is.Empty);
        });
    }

    /// <summary>Round 5 fix 2: a typed name the PLANNER refuses (no designer region to write beside) is the row's refusal.</summary>
    [Test]
    public async Task ATypedNameThePlannerRefuses_IsTheRowsRefusal_NotAnErrorListEntry()
    {
        var h = Open(FormTarget.WinForms);
        var row = ShowEvents(h);
        var code = h.Files.Contents[h.CodePath];
        h.Files.Contents[h.CodePath] = code.Replace("<vgs:designer", "<vgs-removed").Replace("</vgs:designer>", "</vgs-removed>");
        h.Vm.PropertyGrid.CodeBehindText = h.Files.Contents[h.CodePath];
        var asked = new TaskCompletionSource();
        h.Vm.PropertyGrid.HandlerRequested += (_, _) => asked.TrySetResult();

        row.Commit("Typed");
        await asked.Task;
        await Task.Delay(50);

        Assert.Multiple(() =>
        {
            Assert.That(row.Refusal, Does.Contain("region"), "the planner's reason, in the row");
            Assert.That(h.Diagnostics.SelectMany(d => d.Diagnostics), Is.Empty);
        });
    }

    /// <summary>
    /// Ruling 8: a row for an event with no name on the target is a defect, thrown at CONSTRUCTION — a throw inside a
    /// binding's getter is swallowed by the binding and the cell would just show nothing.
    /// </summary>
    [Test]
    public void AnEventRow_ForAnEventWithNoNameOnTheTarget_ThrowsAtConstruction()
    {
        var form = new FormDocument { Target = FormTarget.Web, Name = "W" };
        var pnl = new FormControl { Kind = "Panel", Id = "pnl", TabIndex = 0 };
        form.Controls.Add(pnl);
        var owner = new FormBindOwner(form, pnl);
        var paint = owner.Definition!.Events!.Single(e => e.Name == "Paint");
        Assert.That(FormEvents.NameOn(paint, FormTarget.Web), Is.Null, "precondition: Paint has no page name");

        Assert.That(() => new FormEventRow(form, owner, paint, () => FormCodeScan.Scan(""), () => { }, _ => { }),
            Throws.InstanceOf<InvalidOperationException>());
    }

    /// <summary>
    /// Ruling 11: the one-scan-per-refresh pin counts through the GRID's scanner seam, not a static counter in a compiler
    /// type (process-wide: a parallel test's scans would count too).
    /// </summary>
    [Test]
    public void PushingTheCodeBehind_ScansItOnce_ThroughTheGridsScanner()
    {
        var h = Open(FormTarget.WinForms);
        ShowEvents(h);
        var scans = 0;
        h.Vm.PropertyGrid.CodeScanner = (text, name) =>
        {
            scans++;
            return FormCodeScan.Scan(text, name);
        };
        Assert.That(h.Vm.PropertyGrid.EventRows.Count, Is.GreaterThan(5));

        h.Vm.PropertyGrid.CodeBehindText = h.Files.Contents[h.CodePath] + "\n";

        Assert.That(scans, Is.EqualTo(1));
    }
}
