using BasicLang.Forms;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Events;
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

        public IFileService Service
        {
            get
            {
                var mock = new Mock<IFileService>();

                mock.Setup(f => f.ReadFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .Returns((string p, CancellationToken _) => Task.FromResult(Contents[p]));

                mock.Setup(f => f.FileExistsAsync(It.IsAny<string>()))
                    .Returns((string p) => Task.FromResult(Contents.ContainsKey(p)));

                mock.Setup(f => f.WriteFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .Returns((string p, string text, CancellationToken _) =>
                    {
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
}
