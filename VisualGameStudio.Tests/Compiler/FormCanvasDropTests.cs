using BasicLang.Forms;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Events;
using VisualGameStudio.Shell.ViewModels.Documents;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Dropping a control from the toolbox, seen from the CALLER.
///
/// <para>⛔⛔ <b>These are the tests that decide whether the feature exists.</b>
/// <see cref="VisualGameStudio.Shell.ViewModels.Designer.FormPlacement"/> is covered from thirty
/// angles by <c>FormPlacementTests</c>, and every one of those would still pass if nothing in the
/// IDE ever called it — which is precisely how <c>RegionWriter</c>, <c>DispatchSource</c> and
/// <c>JavaScriptEmitter.Emit(forms:)</c> each shipped complete, unit-tested and unreachable in this
/// same feature. So these drive the document view model the canvas actually talks to, and assert on
/// the <b>document text</b>: the thing that gets saved, compiled and run.</para>
///
/// <para>⚠ What is NOT claimed here: that the toolbox starts a drag, that the cursor shows a drop
/// effect, or that the canvas is where the pointer thinks it is. Those need a running IDE. The
/// pointer-to-form-space mapping is <c>FormCanvasTransform</c>'s, and has its own tests.</para>
/// </summary>
[TestFixture]
public class FormCanvasDropTests
{
    private const string Dir = "/proj/";

    private static (CodeEditorDocumentViewModel Vm, Dictionary<string, string> Files) Open(
        FormTarget target, string? documentText = null)
    {
        var scaffold = FormScaffolder.Create("LoginForm", target);
        var contents = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Dir + scaffold.DocumentFileName] = documentText ?? scaffold.DocumentText,
            [Dir + scaffold.CodeFileName] = scaffold.CodeText
        };

        var files = new Mock<IFileService>();
        files.Setup(f => f.ReadFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string p, CancellationToken _) => Task.FromResult(contents[p]));
        files.Setup(f => f.FileExistsAsync(It.IsAny<string>()))
            .Returns((string p) => Task.FromResult(contents.ContainsKey(p)));
        files.Setup(f => f.WriteFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string p, string text, CancellationToken _) =>
            {
                contents[p] = text;
                return Task.CompletedTask;
            });

        var vm = new CodeEditorDocumentViewModel(files.Object, new Mock<IEventAggregator>().Object)
        {
            FilePath = Dir + scaffold.DocumentFileName
        };
        vm.Text = contents[vm.FilePath];

        return (vm, contents);
    }

    [Test]
    public void DroppingAButton_PutsItInTheDocumentText()
    {
        // ⛔⛔ THE test. Not "the placer returned a control" — the .blform the user saves, builds
        // and runs now contains it. Everything between the drop and the file is the part that has
        // been silently missing three times in this feature.
        var (vm, _) = Open(FormTarget.WinForms);

        var refusal = vm.PlaceControl("Button", 96, 80);

        Assert.That(refusal, Is.Null);
        Assert.Multiple(() =>
        {
            Assert.That(vm.Text, Does.Contain("<Button"));
            Assert.That(vm.Text, Does.Contain("Id=\"Button1\""));
            Assert.That(vm.Text, Does.Contain("X=\"96\""));
            Assert.That(vm.Text, Does.Contain("Y=\"80\""));
        });
    }

    [Test]
    public void ADroppedControl_SurvivesAReRead()
    {
        // The writer is structure-preserving: it edits the existing XML rather than regenerating
        // it. A new element that the READER cannot read back would round-trip to nothing, and the
        // control would vanish the next time the document was opened.
        var (vm, _) = Open(FormTarget.WinForms);

        vm.PlaceControl("TextBox", 20, 30);

        var reread = BasicLang.Forms.Serialization.FormDocumentReader.Read(vm.FilePath!, vm.Text);
        Assert.That(reread.IsRefused, Is.False);
        var control = reread.Model.FindById("TextBox1");
        Assert.That(control, Is.Not.Null);
        var pixel = (PixelGeometry)control!.Geometry!;
        Assert.Multiple(() =>
        {
            Assert.That(pixel.X, Is.EqualTo(20));
            Assert.That(pixel.Y, Is.EqualTo(30));
        });
    }

    [Test]
    public void ADroppedControl_BecomesTheSelection()
    {
        // What a designer does: you drop a control and its properties are in front of you. Without
        // this the user drops one and has to go find it on the canvas to edit anything.
        var (vm, _) = Open(FormTarget.WinForms);

        vm.PlaceControl("Button", 40, 40);

        Assert.That(vm.PropertyGrid.SelectedControl, Is.Not.Null);
        Assert.That(vm.PropertyGrid.SelectedControl!.Id, Is.EqualTo("Button1"));
    }

    [Test]
    public void ADrop_RepaintsTheCanvas()
    {
        // ⛔ The canvas holds the SAME document reference before and after — the model is mutated
        // in place so the grid, the canvas and the writer share one object graph. Nothing about the
        // drop invalidates the visual, so without the revision bump the control is in the file and
        // not on the screen: the exact shape of "the designer ignored my drop".
        var (vm, _) = Open(FormTarget.WinForms);
        var before = vm.DesignModelRevision;

        vm.PlaceControl("Button", 40, 40);

        Assert.That(vm.DesignModelRevision, Is.GreaterThan(before));
    }

    [Test]
    public void TwoDrops_ProduceTwoControls()
    {
        var (vm, _) = Open(FormTarget.WinForms);

        vm.PlaceControl("Button", 10, 10);
        vm.PlaceControl("Button", 10, 50);

        var reread = BasicLang.Forms.Serialization.FormDocumentReader.Read(vm.FilePath!, vm.Text);
        Assert.That(reread.Model.Controls.Select(c => c.Id),
            Is.EquivalentTo(new[] { "Button1", "Button2" }));
    }

    [Test]
    public void ADropOnAWebDocument_IsRefused_AndChangesNothing()
    {
        var (vm, _) = Open(FormTarget.Web);
        var before = vm.Text;

        var refusal = vm.PlaceControl("Button", 10, 10);

        Assert.Multiple(() =>
        {
            Assert.That(refusal, Is.Not.Null.And.Not.Empty);
            Assert.That(vm.Text, Is.EqualTo(before));
        });
    }

    [Test]
    public void ADropOnANonFormDocument_IsRefused()
    {
        // The canvas is only shown for a form document, but the VM is the one every .bas uses too —
        // and a refusal is cheaper than trusting that the view can never call this.
        var files = new Mock<IFileService>();
        var vm = new CodeEditorDocumentViewModel(files.Object, new Mock<IEventAggregator>().Object)
        {
            FilePath = Dir + "Program.bas"
        };
        vm.Text = "Public Class Program\nEnd Class\n";

        var refusal = vm.PlaceControl("Button", 10, 10);

        Assert.That(refusal, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void TheCommandTheCanvasIsBoundTo_PlacesTheControl()
    {
        // ⛔⛔ The seam. The canvas does not call PlaceControl — it executes DropCommand, which the
        // AXAML binds to PlaceDroppedControlCommand. Testing only PlaceControl would leave the one
        // link the user's gesture actually travels untested, which is the same "complete and
        // unreachable" shape this feature has produced three times. Compiled bindings prove the
        // name resolves; this proves the command does the work.
        var (vm, _) = Open(FormTarget.WinForms);

        vm.PlaceDroppedControlCommand.Execute(
            new VisualGameStudio.Shell.Controls.FormControlDropRequest("Button", 96, 80));

        Assert.Multiple(() =>
        {
            Assert.That(vm.Text, Does.Contain("Id=\"Button1\""));
            Assert.That(vm.Text, Does.Contain("X=\"96\""));
        });
    }

    [Test]
    public void ADroppedControl_DoesNotDisturbWhatWasAlreadyThere()
    {
        // ⚠ The .blform is the user's file: a drop adds one element and must not reorder, restamp
        // or drop the rest. It is NOT byte-exact, and that is the writer's documented boundary
        // rather than something this feature chose — the first REAL edit to a hand-written document
        // normalises inter-attribute whitespace and self-closing spacing, because the tree is
        // re-serialized once it actually changes (see FormDocumentWriter's class comment and
        // Write_NormalisesAHandWrittenDocument_OnlyOnTheFirstRealEdit). A property-grid edit does
        // exactly the same. What must survive is everything the reader did not model: comments,
        // unknown elements, and every attribute's spelling, value and ORDER.
        var (vm, _) = Open(FormTarget.WinForms, """
            <Form Name="LoginForm" Version="1" Width="800" Height="450" Text="LoginForm">
              <!-- the user's own note -->
              <Controls>
                <Label Id="lblUser" Text="User" X="12" Y="16" Width="60" Height="23" TabIndex="0"/>
              </Controls>
              <FutureSection Something="42"/>
            </Form>
            """);

        vm.PlaceControl("TextBox", 80, 16);

        Assert.Multiple(() =>
        {
            Assert.That(vm.Text, Does.Contain(
                "<Label Id=\"lblUser\" Text=\"User\" X=\"12\" Y=\"16\" Width=\"60\" Height=\"23\" TabIndex=\"0\""),
                "the untouched control keeps its attributes, their values and their order");
            Assert.That(vm.Text, Does.Contain("<!-- the user's own note -->"),
                "a comment the reader never modelled is still there");
            Assert.That(vm.Text, Does.Contain("Something=\"42\""),
                "and so is an element from a newer designer");
        });
    }
}
