using BasicLang.Forms;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Events;
using VisualGameStudio.Shell.ViewModels.Documents;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// The designer actually writing the user's <c>.bas</c> — the call that turns the whole feature
/// from a drawing into a program.
///
/// <para>⛔⛔ <b>Every one of these tests is a caller test, not a behaviour test.</b>
/// <see cref="RegionWriter"/> was complete, tested from thirty angles, and had NO PRODUCTION
/// CALLER: a user could scaffold a form, drop a button on the canvas, save, and build — and the
/// build would fail on the <c>InitializeComponent</c> the scaffold calls, because nothing ever
/// generated it. The unit tests all passed throughout. So these drive <see cref="ISaveable"/> from
/// the outside, through the same <c>SaveAsync</c> the Ctrl+S binding calls, and assert on what
/// lands in the FILE SERVICE.</para>
/// </summary>
[TestFixture]
public class FormCodeBehindWriteTests
{
    private const string Dir = "/proj/";

    /// <summary>
    /// A file service over a dictionary, so a test can assert what was written and — just as
    /// importantly — that nothing was.
    /// </summary>
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

    /// <summary>A scaffolded pair on "disk", plus a document view model open on the form.</summary>
    private static (CodeEditorDocumentViewModel Vm, Files Files, List<DiagnosticsUpdatedEvent> Published)
        Open(string formName, FormTarget target, string? documentText = null)
    {
        var scaffold = FormScaffolder.Create(formName, target);
        var files = new Files();
        files.Contents[Dir + scaffold.DocumentFileName] = documentText ?? scaffold.DocumentText;
        files.Contents[Dir + scaffold.CodeFileName] = scaffold.CodeText;

        var published = new List<DiagnosticsUpdatedEvent>();
        var events = new Mock<IEventAggregator>();
        events.Setup(e => e.Publish(It.IsAny<DiagnosticsUpdatedEvent>()))
            .Callback((DiagnosticsUpdatedEvent e) => published.Add(e));

        var vm = new CodeEditorDocumentViewModel(files.Service, events.Object)
        {
            FilePath = Dir + scaffold.DocumentFileName
        };
        vm.Text = files.Contents[vm.FilePath];

        files.Writes.Clear();
        return (vm, files, published);
    }

    [Test]
    public void PathFor_PairsEitherDocumentWithTheSameCodeBehind()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FormCodeBehind.PathFor("/p/LoginForm.blform"),
                Is.EqualTo(Path.Combine("/p", "LoginForm.bas")));
            Assert.That(FormCodeBehind.PathFor("/p/LoginForm.blwebform"),
                Is.EqualTo(Path.Combine("/p", "LoginForm.bas")));
        });
    }

    [Test]
    public async Task SavingAWinFormsDocument_GeneratesTheControlsIntoTheUsersFile()
    {
        // ⛔⛔ THE defect. The scaffold's `Public Sub New()` calls InitializeComponent, and until
        // this call existed nothing ever generated it — so the very file the IDE had just created
        // did not build, and the canvas, the property grid and the document round trip all worked
        // perfectly while the user's program was missing a member.
        var (vm, files, _) = Open("LoginForm", FormTarget.WinForms);

        vm.Text = """
            <Form Name="LoginForm" Version="1" Width="800" Height="450" Text="LoginForm">
              <Controls>
                <Button Id="btnLogin" Text="Sign in" X="96" Y="80" Width="100" Height="30" TabIndex="0"/>
              </Controls>
            </Form>
            """;

        Assert.That(await vm.SaveAsync(), Is.True);

        var code = files.Contents[Dir + "LoginForm.bas"];

        Assert.Multiple(() =>
        {
            Assert.That(files.Writes, Does.Contain(Dir + "LoginForm.bas"),
                "the companion .bas is written, not only the document");
            Assert.That(code, Does.Contain("Private btnLogin As Button"),
                "the field the user's own code refers to");
            Assert.That(code, Does.Contain("Private Sub InitializeComponent()"),
                "the method Public Sub New() calls");
            Assert.That(code, Does.Contain("btnLogin = New Button()"));
            Assert.That(code, Does.Contain("btnLogin.Location = New Point(96, 80)"));
            Assert.That(code, Does.Contain("Me.Controls.Add(btnLogin)"));
        });
    }

    [Test]
    public async Task SavingAWebDocument_GeneratesTheLookupsIntoTheUsersFile()
    {
        // The same call, the other target: on the web the controls are FOUND, not constructed.
        var (vm, files, _) = Open("LoginForm", FormTarget.Web);

        vm.Text = """
            <WebForm Name="LoginForm" Version="1">
              <Layout Kind="Grid" Cols="auto,1fr" Rows="auto" Gap="8px"/>
              <Controls>
                <Button Id="btnLogin" Text="Sign in" Col="0" Row="0" TabIndex="0"/>
              </Controls>
            </WebForm>
            """;

        Assert.That(await vm.SaveAsync(), Is.True);

        var code = files.Contents[Dir + "LoginForm.bas"];

        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Contain("Private btnLogin As Element"));
            Assert.That(code, Does.Contain("""btnLogin = doc.getElementById("btnLogin")"""));
        });
    }

    [Test]
    public async Task ANoOpSave_DoesNotTouchTheCodeBehind()
    {
        // ⚠ The .bas is the USER'S file. Saving a document that changed nothing must not restamp
        // it — a modified timestamp on a file the user did not edit is how a designer teaches
        // people not to trust it (and would churn every source-control diff).
        var (vm, files, _) = Open("LoginForm", FormTarget.WinForms);

        await vm.SaveAsync();
        files.Writes.Clear();

        await vm.SaveAsync();

        Assert.That(files.Writes, Does.Not.Contain(Dir + "LoginForm.bas"));
    }

    [Test]
    public async Task AHandEditedRegion_RefusesToWrite_AndSaysSo()
    {
        // ⛔⛔ Refuse-by-default, seen from the caller. The designer never silently discards
        // hand-written code — and a refusal the user is never SHOWN is indistinguishable from the
        // feature quietly not working, so the finding has to leave this method too.
        var (vm, files, published) = Open("LoginForm", FormTarget.WinForms);

        var code = files.Contents[Dir + "LoginForm.bas"];
        var marker = code.IndexOf("region=\"init\"", StringComparison.Ordinal);
        Assert.That(marker, Is.GreaterThan(0), "the scaffold carries the init region");
        var lineEnd = code.IndexOf('\n', marker) + 1;
        files.Contents[Dir + "LoginForm.bas"] =
            code[..lineEnd] + "        ' the user typed this inside the region\n" + code[lineEnd..];

        files.Writes.Clear();
        Assert.That(await vm.SaveAsync(), Is.True, "the DOCUMENT still saves");

        Assert.Multiple(() =>
        {
            Assert.That(files.Writes, Does.Not.Contain(Dir + "LoginForm.bas"),
                "the hand edit is not overwritten");
            Assert.That(files.Contents[Dir + "LoginForm.bas"],
                Does.Contain("the user typed this inside the region"));
            Assert.That(
                published.SelectMany(p => p.Diagnostics).Select(d => d.Id),
                Does.Contain(DesignCodes.RegionHandEdited),
                "and the user is told why nothing happened");
        });
    }

    [Test]
    public async Task AMissingCodeBehind_IsReported_NotThrown()
    {
        var (vm, files, published) = Open("LoginForm", FormTarget.WinForms);
        files.Contents.Remove(Dir + "LoginForm.bas");

        Assert.That(await vm.SaveAsync(), Is.True);
        Assert.That(published.SelectMany(p => p.Diagnostics).Select(d => d.Id),
            Does.Contain(DesignCodes.RegionAbsent));
    }

    [Test]
    public async Task SavingAnOrdinarySourceFile_TouchesNothingElse()
    {
        var files = new Files();
        files.Contents[Dir + "Program.bas"] = "Public Module M\nEnd Module\n";

        var vm = new CodeEditorDocumentViewModel(files.Service, new Mock<IEventAggregator>().Object)
        {
            FilePath = Dir + "Program.bas"
        };
        vm.Text = files.Contents[vm.FilePath];
        files.Writes.Clear();

        Assert.That(await vm.SaveAsync(), Is.True);
        Assert.That(files.Writes, Is.EqualTo(new[] { Dir + "Program.bas" }));
    }
}
