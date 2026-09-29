using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BasicLang.Forms;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Events;
using VisualGameStudio.Shell.Controls;
using VisualGameStudio.Shell.ViewModels.Designer;
using VisualGameStudio.Shell.ViewModels.Documents;
using VisualGameStudio.Shell.Views.Controls;
using VisualGameStudio.Shell.Views.Documents;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// Spec §8 "Real view": the property grid driven through the REAL <see cref="CodeEditorDocumentView"/>,
/// its real view model, a real .blform and a scaffolded .bas on a dictionary "disk" — because every
/// piece-level designer test passed while the owner hit four real bugs (MEMORY: host the REAL view).
///
/// <para>Real input wherever headless input reaches the control: canvas clicks, typing into a row's
/// editor, clicking away to commit, the search box, the sort buttons, the right-click on a row. Where a
/// test sets a control property instead (the object selector's <c>SelectedIndex</c>), it says so: the
/// binding behind it is what is under test.</para>
///
/// <para>⛔ Selection is asserted at TWO canvas zooms (a narrow window fits the 640x480 form below 1.0; a
/// wide one reaches 1.0), because every canvas test once ran at the one zoom where a defect hid. ⛔ Every
/// window is CLOSED (<see cref="Rig.Dispose"/>): an unclosed headless window keeps its bindings live for
/// the rest of the run. ⛔ A repeated click is OFFSET: two presses at one point with no time between are
/// a double-click to the headless pipeline.</para>
/// </summary>
[TestFixture]
public class FormPropertyGridRealViewTests
{
    private const string Dir = "/proj/";

    private const string Doc = """
        <Form Name="GridForm" Version="1" Width="640" Height="480" Text="GridForm">
          <Controls>
            <Label Id="lbl" X="16" Y="16" Width="100" Height="23" TabIndex="0" Text="Hello"/>
            <Button Id="btn" X="16" Y="56" Width="75" Height="23" TabIndex="1"/>
          </Controls>
          <Components>
            <Timer Id="tmr"/>
          </Components>
          <Resources/>
        </Form>
        """;

    /// <summary>A narrow window (the canvas fits the form BELOW 1.0) and a wide one (AT 1.0).</summary>
    private static readonly (double Width, double Height)[] TwoZooms = { (800, 560), (1400, 900) };

    /// <summary>A file service over a dictionary: what the document and its .bas hold on "disk".</summary>
    private sealed class Files
    {
        public readonly Dictionary<string, string> Contents = new(StringComparer.Ordinal);

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
                        return Task.CompletedTask;
                    });
                return mock.Object;
            }
        }
    }

    private sealed class Rig : IDisposable
    {
        public required CodeEditorDocumentViewModel Vm { get; init; }
        public required Window Window { get; init; }
        public required FormPropertyGridView Grid { get; init; }
        public required FormCanvasControl Canvas { get; init; }
        public required Files Files { get; init; }

        public FormPropertyGridViewModel GridVm => Vm.PropertyGrid;
        public ListBox List => Grid.FindControl<ListBox>("PropertyList")!;
        public ComboBox Selector => Grid.FindControl<ComboBox>("ObjectSelector")!;
        public TextBox Search => Grid.FindControl<TextBox>("SearchBox")!;
        public FormDocument Doc => Vm.DesignDocument!;

        public FormControl Control(string id) => Doc.FindById(id)!;

        public FormPropertyRow Row(string name) => GridVm.Rows.Single(r => r.Name == name);

        /// <summary>The row's (or header's) container, scrolled into view first — the real list virtualises.</summary>
        public ListBoxItem Container(object item)
        {
            List.ScrollIntoView(item);
            Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            return (ListBoxItem?)List.ContainerFromItem(item)
                   ?? throw new InvalidOperationException($"{item} is not realised in the real grid");
        }

        /// <summary>The visible text box of a row's typed editor (a frozen row's read-only box is hidden).</summary>
        public TextBox EditorBox(FormPropertyRow row) =>
            Container(row).GetVisualDescendants().OfType<TextBox>().First(t => t.IsEffectivelyVisible);

        public TextBlock NameCell(FormPropertyRow row) =>
            Container(row).GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == row.Name);

        public Panel ValuePanel(FormPropertyRow row) =>
            Container(row).GetVisualDescendants().OfType<Panel>()
                .First(p => p.GetType() == typeof(Panel) && Avalonia.Controls.Grid.GetColumn(p) == 2);

        public Point CentreInWindow(Visual target, double dx = 0) =>
            target.TranslatePoint(new Point(target.Bounds.Width / 2 + dx, target.Bounds.Height / 2), Window)
            ?? throw new InvalidOperationException($"{target} is not in the window's visual tree");

        public void Click(Visual target, double dx = 0, MouseButton button = MouseButton.Left)
        {
            var at = CentreInWindow(target, dx);
            Window.MouseDown(at, button);
            Window.MouseUp(at, button);
            Dispatcher.UIThread.RunJobs();
        }

        /// <summary>Canvas-local centre of a control's positioned entry, read live from the canvas's own fit.</summary>
        public Point CanvasCentreOf(FormControl control)
        {
            var fit = FormCanvasControl.Fit(Doc, Canvas.Bounds.Size);
            var entry = FormCanvasTransform.Layout(Doc, Canvas.SelectedControl)
                .Single(e => ReferenceEquals(e.Control, control) && e.Role == FormLayoutRole.Control);
            return fit.ToCanvas(entry.Bounds).Center;
        }

        public double Zoom => FormCanvasControl.Fit(Doc, Canvas.Bounds.Size).Zoom;

        public void ClickOnCanvas(Point canvasLocal)
        {
            var at = Canvas.TranslatePoint(canvasLocal, Window)
                     ?? throw new InvalidOperationException("the canvas is not in the window");
            Window.MouseDown(at, MouseButton.Left);
            Window.MouseUp(at, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }

        public string Code => Files.Contents[Dir + "GridForm.bas"];

        public void Dispose() => Window.Close();
    }

    /// <summary>Opens the real view on <see cref="Doc"/>, with the scaffolded GridForm.bas beside it.</summary>
    private static Rig Open(double width = 1000, double height = 700)
    {
        var scaffold = FormScaffolder.Create("GridForm", FormTarget.WinForms);
        var files = new Files();
        files.Contents[Dir + scaffold.DocumentFileName] = Doc;
        files.Contents[Dir + scaffold.CodeFileName] = scaffold.CodeText;

        var vm = new CodeEditorDocumentViewModel(files.Service, new Mock<IEventAggregator>().Object)
        {
            FilePath = Dir + scaffold.DocumentFileName
        };
        vm.SetContent(Doc);
        Assert.That(vm.EnterDesignModeForFormDocument(), Is.True, "precondition: the designer must open");

        var view = new CodeEditorDocumentView { DataContext = vm };
        var window = new Window { Width = width, Height = height, Content = view };

        // ⚠ The IDE's OWN styles and theme brushes (App.axaml includes exactly this file), which the headless
        // app (FluentTheme alone) does not load. Measured: without IdeBg a category header's presenter has a
        // NULL background, is not hit-testable, and a real click on it falls through to the list item — a
        // collapse that "does not work" here and works in the IDE.
        window.Styles.Add(new StyleInclude(new Uri("avares://VisualGameStudio/"))
        {
            Source = new Uri("avares://VisualGameStudio/Resources/Styles/AppStyles.axaml")
        });

        try
        {
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var grid = view.FindControl<FormPropertyGridView>("PropertyGridView")
                       ?? throw new InvalidOperationException("PropertyGridView not found in the real document view");
            var canvas = view.FindControl<FormCanvasControl>("DesignCanvas")
                         ?? throw new InvalidOperationException("DesignCanvas not found in the real document view");
            Assert.That(window.FindResource("IdeBg"), Is.Not.Null, "precondition: the IDE's styles are loaded");
            return new Rig { Vm = vm, Window = window, Grid = grid, Canvas = canvas, Files = files };
        }
        catch
        {
            window.Close(); // ⛔ never leave a window alive with its bindings live
            throw;
        }
    }

    /// <summary>Selects through the real canvas: a click on the control's own rectangle.</summary>
    private static void SelectOnCanvas(Rig rig, string id)
    {
        rig.ClickOnCanvas(rig.CanvasCentreOf(rig.Control(id)));
        rig.Window.UpdateLayout();
        Assert.That(rig.GridVm.SelectedControl, Is.SameAs(rig.Control(id)), $"precondition: {id} is selected");
    }

    // ==================================================================
    // Hosting, and the one selection store in both directions
    // ==================================================================

    [AvaloniaTest]
    public void TheRealDocumentView_HostsTheGrid_ShowingTheFormUntilSomethingIsSelected()
    {
        using var rig = Open();

        Assert.Multiple(() =>
        {
            Assert.That(rig.Grid.DataContext, Is.SameAs(rig.GridVm), "the extracted view is bound to PropertyGrid");
            Assert.That((rig.Selector.SelectedItem as FormObjectItem)?.Name, Is.EqualTo("GridForm"),
                "nothing selected: the selector shows the form");
            Assert.That(rig.Selector.ItemCount, Is.EqualTo(4), "the form, lbl, btn and the tray's tmr");
            Assert.That(rig.List.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "ClientSize"), Is.True,
                "the FORM's rows are realised in the real list");
        });
    }

    /// <summary>
    /// A click on the canvas selects the control, and the grid follows: the selector, the rows in the
    /// list, and a category header — at two zooms.
    /// </summary>
    [AvaloniaTest]
    public void ClickingAControlOnTheCanvas_MovesTheSelectorAndTheRows_AtTwoZooms()
    {
        var zooms = new List<double>();
        foreach (var (w, h) in TwoZooms)
        {
            using var rig = Open(w, h);
            zooms.Add(rig.Zoom);

            SelectOnCanvas(rig, "btn");

            Assert.Multiple(() =>
            {
                Assert.That((rig.Selector.SelectedItem as FormObjectItem)?.Name, Is.EqualTo("btn"), $"{w}x{h}: the selector");
                Assert.That(rig.Vm.Selection.Primary, Is.SameAs(rig.Control("btn")), $"{w}x{h}: the store");
                Assert.That(rig.List.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Appearance"), Is.True,
                    $"{w}x{h}: the Button's rows are listed under category headers");
                Assert.That(rig.List.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "ClientSize"), Is.False,
                    $"{w}x{h}: the form's rows are gone");
            });

            // Back to the form: a click on the canvas away from every control.
            rig.ClickOnCanvas(new Point(4, 4));
            Assert.That((rig.Selector.SelectedItem as FormObjectItem)?.Name, Is.EqualTo("GridForm"),
                $"{w}x{h}: an empty click selects nothing, and the selector shows the form again");
        }

        TestContext.WriteLine($"[zooms] {string.Join(", ", zooms)}");
        Assert.That(zooms[0], Is.LessThan(zooms[1]).And.LessThan(1.0), "precondition: two DIFFERENT zooms, one below 1.0");
    }

    /// <summary>
    /// ⛔ A pick in the selector goes through the ONE store: the canvas, the tray and the grid all follow,
    /// at two zooms. The pick is made on the real ComboBox's <c>SelectedIndex</c>: its SelectedItem binding
    /// is what is under test.
    /// </summary>
    [AvaloniaTest]
    public void PickingInTheObjectSelector_SelectsOnTheCanvasAndInTheTray_AtTwoZooms()
    {
        foreach (var (w, h) in TwoZooms)
        {
            using var rig = Open(w, h);
            int IndexOf(string name) => rig.GridVm.Objects.ToList().FindIndex(o => o.Name == name);

            rig.Selector.SelectedIndex = IndexOf("btn");
            Dispatcher.UIThread.RunJobs();

            Assert.Multiple(() =>
            {
                Assert.That(rig.Vm.Selection.Primary, Is.SameAs(rig.Control("btn")), $"{w}x{h}: the store selected it");
                Assert.That(rig.Canvas.SelectedControl, Is.SameAs(rig.Control("btn")), $"{w}x{h}: the canvas follows");
                Assert.That(rig.GridVm.Rows.Any(r => r.Name == "Text"), Is.True, $"{w}x{h}: the Button's rows");
            });

            rig.Selector.SelectedIndex = IndexOf("tmr");
            Dispatcher.UIThread.RunJobs();

            Assert.Multiple(() =>
            {
                Assert.That(rig.Vm.Selection.Primary, Is.SameAs(rig.Control("tmr")), $"{w}x{h}: a tray component");
                Assert.That(rig.Vm.Tray.Items.Single(i => i.Id == "tmr").IsSelected, Is.True, $"{w}x{h}: the tray highlights it");
                Assert.That(rig.Canvas.SelectedControl, Is.SameAs(rig.Control("tmr")), $"{w}x{h}: the canvas agrees");
            });

            rig.Selector.SelectedIndex = 0; // the form
            Dispatcher.UIThread.RunJobs();

            Assert.Multiple(() =>
            {
                Assert.That(rig.Vm.Selection.Primary, Is.Null, $"{w}x{h}: choosing the form is SelectInDesigner(null)");
                Assert.That(rig.Canvas.SelectedControl, Is.Null, $"{w}x{h}: the canvas deselects");
                Assert.That(rig.Vm.Tray.Items.All(i => !i.IsSelected), Is.True, $"{w}x{h}: and so does the tray");
            });
        }
    }

    // ==================================================================
    // Editing through the real editor → the document, then the .bas on save
    // ==================================================================

    /// <summary>
    /// Typing a caption into the Text row's real TextBox and clicking away commits it (LostFocus, D13):
    /// the model, the .blform text and — on save — the regenerated region in GridForm.bas all carry it.
    /// </summary>
    [AvaloniaTest]
    public void TypingInARowsEditor_AndClickingAway_RewritesTheDocument_AndSaveRegeneratesTheBas()
    {
        using var rig = Open();
        SelectOnCanvas(rig, "lbl");
        var box = rig.EditorBox(rig.Row("Text"));

        rig.Click(box);
        rig.Window.KeyPress(Key.A, RawInputModifiers.Control);
        rig.Window.KeyRelease(Key.A, RawInputModifiers.Control);
        rig.Window.KeyTextInput("World");
        Dispatcher.UIThread.RunJobs();
        Assert.That(rig.Control("lbl").Properties["Text"], Is.EqualTo("Hello"), "nothing commits per keystroke");

        rig.Click(rig.Search); // focus leaves the editor: the LostFocus commit

        Assert.Multiple(() =>
        {
            Assert.That(rig.Control("lbl").Properties["Text"], Is.EqualTo("World"), "the model");
            Assert.That(rig.Vm.Text, Does.Contain("Text=\"World\"").And.Not.Contain("Text=\"Hello\""), "the .blform text");
            Assert.That(rig.NameCell(rig.Row("Text")).FontWeight, Is.EqualTo(FontWeight.Bold), "still bold: set, not the default");
        });

        Assert.That(rig.Vm.SaveAsync().GetAwaiter().GetResult(), Is.True, "the save succeeds");

        Assert.Multiple(() =>
        {
            Assert.That(rig.Files.Contents[Dir + "GridForm.blform"], Does.Contain("Text=\"World\""), "the document on disk");
            Assert.That(rig.Code, Does.Contain("\"World\"").And.Not.Contain("\"Hello\""),
                "the generated region in the user's .bas carries the new caption");
        });
    }

    // ==================================================================
    // §7: a refused value snaps the real editor back
    // ==================================================================

    /// <summary>
    /// ⛔ Spec §7 "refused in the editor, never written", through the REAL TextBox. <c>12345</c> is not a
    /// colour: after the LostFocus commit the box must show what the document holds again (ForeColor is
    /// absent and has no default: empty), the file must be untouched, and the description pane must say
    /// why.
    ///
    /// <para>⚠ This is the measurement the pre-flight asked for: the row snaps back by raising
    /// PropertyChanged(StringValue) from inside the binding's own write to the source, and whether
    /// Avalonia re-reads the source during its own push was unmeasured.</para>
    /// </summary>
    [AvaloniaTest]
    public void ARefusedValue_SnapsTheRealEditorBack_LeavesTheFileAlone_AndThePaneSaysWhy()
    {
        using var rig = Open();
        SelectOnCanvas(rig, "lbl");
        var row = rig.Row("ForeColor");
        var box = rig.EditorBox(row);
        var before = rig.Vm.Text;
        var edits = 0;
        rig.GridVm.Edited += (_, _) => edits++;

        rig.Click(box);
        rig.Window.KeyTextInput("12345");
        Dispatcher.UIThread.RunJobs();
        Assume.That(box.Text, Is.EqualTo("12345"), "precondition: the typing landed in the box");

        rig.Click(rig.Search); // the LostFocus commit
        rig.Window.UpdateLayout();
        TestContext.WriteLine($"[snap-back] box.Text after the refused commit = \"{box.Text}\"");

        Assert.Multiple(() =>
        {
            Assert.That(box.Text, Is.EqualTo(""), "the editor snapped back to what the document holds");
            Assert.That(rig.Control("lbl").Properties.ContainsKey("ForeColor"), Is.False, "never written to the model");
            Assert.That(rig.Vm.Text, Is.EqualTo(before), "nor to the file");
            Assert.That(edits, Is.Zero, "a refusal is not an edit");
            Assert.That(rig.GridVm.DescriptionTitle, Is.EqualTo("ForeColor"), "the pane is about the refused row");
            Assert.That(rig.GridVm.DescriptionBody, Does.Contain("'12345'").And.Contain("not applied"),
                "and says what was refused and that it was not applied");
        });

        // The same box accepts a real colour afterwards: the snap-back left the editor usable.
        rig.Click(box, dx: 8);
        rig.Window.KeyTextInput("Red");
        rig.Click(rig.Search, dx: 8);

        Assert.Multiple(() =>
        {
            Assert.That(rig.Control("lbl").Properties.GetValueOrDefault("ForeColor"), Is.EqualTo("Red"));
            Assert.That(rig.GridVm.DescriptionBody, Does.Not.Contain("'12345'"), "a good edit clears the refusal");
        });
    }

    /// <summary>
    /// The OTHER refusal route: a value the catalog accepts but the store refuses (the pre-flight's case,
    /// <c>0, 300</c> parses as a Size and <see cref="FormRootValues.Set"/> rejects a non-positive width).
    /// The form's real ClientSize box must snap back to the size the form still has, and the pane must say why.
    /// </summary>
    [AvaloniaTest]
    public void AClientSizeTheStoreRefuses_SnapsTheRealEditorBack()
    {
        using var rig = Open();
        var box = rig.EditorBox(rig.Row("ClientSize"));
        var shown = box.Text;
        var before = rig.Vm.Text;
        var edits = 0;
        rig.GridVm.Edited += (_, _) => edits++;
        Assume.That(shown, Is.Not.Empty, "precondition: the form's size is shown");

        rig.Click(box);
        rig.Window.KeyPress(Key.A, RawInputModifiers.Control);
        rig.Window.KeyRelease(Key.A, RawInputModifiers.Control);
        rig.Window.KeyTextInput("0, 300");
        Dispatcher.UIThread.RunJobs();
        Assume.That(box.Text, Is.EqualTo("0, 300"), "precondition: the typing replaced the size");

        rig.Click(rig.Search);
        rig.Window.UpdateLayout();

        Assert.Multiple(() =>
        {
            Assert.That(box.Text, Is.EqualTo(shown), "the editor snapped back to the form's real size");
            Assert.That(rig.Vm.Text, Is.EqualTo(before), "and the file is unchanged");
            Assert.That(edits, Is.Zero, "a refusal is not an edit");
            // ⛔ Plan 2026-09-27 Task 9 (pre-flight B1): the store's refusal is NAMED now — this pinned silence was the
            // follow-up slice 2 recorded, and naming MobileBreakpoint's refusal names this one on the same route.
            Assert.That(rig.Row("ClientSize").Refusal, Does.Contain("'0, 300'").And.Contain("not applied"));
            Assert.That(rig.GridVm.DescriptionBody, Does.Contain("'0, 300'"), "the pane says why");
        });
    }

    // ==================================================================
    // The natural commit gesture: type in a row, then click ANOTHER control on the canvas
    // ==================================================================

    /// <summary>
    /// Type into a row, then click a different control on the canvas: the press moves focus (the LostFocus
    /// commit) AND the selection (a rebuild that replaces every row and recycles their containers). The
    /// value must land on the control it was typed for — never lost, never written onto the new selection.
    /// </summary>
    [AvaloniaTest]
    public void TypingAValue_ThenClickingAnotherControlOnTheCanvas_CommitsItToTheFirstControl()
    {
        using var rig = Open();
        SelectOnCanvas(rig, "lbl");
        var box = rig.EditorBox(rig.Row("Text"));

        rig.Click(box);
        rig.Window.KeyPress(Key.A, RawInputModifiers.Control);
        rig.Window.KeyRelease(Key.A, RawInputModifiers.Control);
        rig.Window.KeyTextInput("World");
        Dispatcher.UIThread.RunJobs();

        rig.ClickOnCanvas(rig.CanvasCentreOf(rig.Control("btn")));
        rig.Window.UpdateLayout();

        Assert.Multiple(() =>
        {
            Assert.That(rig.GridVm.SelectedControl, Is.SameAs(rig.Control("btn")), "precondition: the click selected btn");
            Assert.That(rig.Control("lbl").Properties.GetValueOrDefault("Text"), Is.EqualTo("World"), "committed to lbl");
            Assert.That(rig.Vm.Text, Does.Contain("Text=\"World\""), "and to the file");
            Assert.That(rig.Control("btn").Properties.ContainsKey("Text"), Is.False, "never onto the new selection");
            Assert.That(rig.Row("Text").StringValue, Is.Empty, "the grid shows btn's own (absent) Text");
        });
    }

    /// <summary>
    /// The same gesture with a REFUSED value. It must not reach either control — and the user must still be
    /// able to learn why, although the rows it was typed into are gone. Decided behaviour: the refusal is
    /// CARRIED over the selection change once, titled with the control it belongs to ("lbl.ForeColor"),
    /// until the next edit, row pick or selection change.
    /// </summary>
    [AvaloniaTest]
    public void ARefusedValue_ThenClickingAnotherControlOnTheCanvas_IsNotWritten_AndItsReasonStaysVisible()
    {
        using var rig = Open();
        SelectOnCanvas(rig, "lbl");
        var box = rig.EditorBox(rig.Row("ForeColor"));
        var before = rig.Vm.Text;

        rig.Click(box);
        rig.Window.KeyTextInput("12345");
        Dispatcher.UIThread.RunJobs();

        rig.ClickOnCanvas(rig.CanvasCentreOf(rig.Control("btn")));
        rig.Window.UpdateLayout();
        TestContext.WriteLine($"[canvas-click] title=\"{rig.GridVm.DescriptionTitle}\" body=\"{rig.GridVm.DescriptionBody}\"");

        Assert.Multiple(() =>
        {
            Assert.That(rig.GridVm.SelectedControl, Is.SameAs(rig.Control("btn")), "precondition: the click selected btn");
            Assert.That(rig.Control("lbl").Properties.ContainsKey("ForeColor"), Is.False, "never written to lbl");
            Assert.That(rig.Control("btn").Properties.ContainsKey("ForeColor"), Is.False, "nor to btn");
            Assert.That(rig.Vm.Text, Is.EqualTo(before), "the file is unchanged");
            Assert.That(rig.GridVm.DescriptionTitle, Is.EqualTo("lbl.ForeColor"), "the pane names whose value it was");
            Assert.That(rig.GridVm.DescriptionBody, Does.Contain("'12345'"), "and why it was refused");
            Assert.That(rig.EditorBox(rig.Row("ForeColor")).Text, Is.Empty, "btn's ForeColor editor shows btn's value");
        });

        // Picking a row asks the pane about THAT row: the carried refusal is retracted.
        rig.GridVm.SelectedItem = rig.Row("Text");
        Assert.That(rig.GridVm.DescriptionBody, Does.Not.Contain("'12345'"));
    }

    /// <summary>
    /// The Int editor's snap-back, through the real NumericUpDown: Width is clamped to at least 1, so with
    /// Width already 1, typing 0 changes nothing — the store reports no change, and the editor must show
    /// the 1 the model still holds, not the 0 it was given.
    /// </summary>
    [AvaloniaTest]
    public void AnIntTheStoreClampsBackToTheSameValue_SnapsTheRealNumericUpDownBack()
    {
        using var rig = Open();
        SelectOnCanvas(rig, "lbl");
        var row = rig.Row("Width");
        var spinner = rig.Container(row).GetVisualDescendants().OfType<NumericUpDown>().Single(n => n.IsEffectivelyVisible);
        var text = spinner.GetVisualDescendants().OfType<TextBox>().First();

        void Type(string value, double dx)
        {
            rig.Click(text, dx);
            rig.Window.KeyPress(Key.A, RawInputModifiers.Control);
            rig.Window.KeyRelease(Key.A, RawInputModifiers.Control);
            rig.Window.KeyTextInput(value);
            Dispatcher.UIThread.RunJobs();
            rig.Click(rig.Search, dx);
            rig.Window.UpdateLayout();
        }

        Type("1", 0);
        Assume.That(((PixelGeometry)rig.Control("lbl").Geometry!).Width, Is.EqualTo(1), "precondition: Width is 1");
        var before = rig.Vm.Text;
        var edits = 0;
        rig.GridVm.Edited += (_, _) => edits++;

        Type("0", 8);
        TestContext.WriteLine($"[int-snap-back] Value={spinner.Value} Text=\"{text.Text}\"");

        Assert.Multiple(() =>
        {
            Assert.That(((PixelGeometry)rig.Control("lbl").Geometry!).Width, Is.EqualTo(1), "clamped: still 1");
            Assert.That(spinner.Value, Is.EqualTo(1m), "the NumericUpDown snapped back to 1");
            Assert.That(text.Text, Is.EqualTo("1"), "and shows it");
            Assert.That(rig.Vm.Text, Is.EqualTo(before), "the file is unchanged");
            Assert.That(edits, Is.Zero, "no change, no edit");
        });
    }

    // ==================================================================
    // Bold / greyed, Reset
    // ==================================================================

    [AvaloniaTest]
    public void AnAbsentEnabled_ShowsOnAndGreyed_AndAPresentText_IsBold()
    {
        using var rig = Open();
        SelectOnCanvas(rig, "lbl");
        using (rig.Window.CaptureRenderedFrame()) { }

        var enabled = rig.Row("Enabled");
        var toggle = rig.Container(enabled).GetVisualDescendants().OfType<ToggleSwitch>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(toggle.IsChecked, Is.True, "an unset Enabled is enabled — it used to render off");
            Assert.That(rig.ValuePanel(enabled).Opacity, Is.EqualTo(0.6).Within(0.01), "greyed: the default, not a value");
            Assert.That(rig.NameCell(enabled).FontWeight, Is.Not.EqualTo(FontWeight.Bold));
            Assert.That(rig.NameCell(rig.Row("Text")).FontWeight, Is.EqualTo(FontWeight.Bold), "Text=\"Hello\" differs from the default");
            Assert.That(rig.ValuePanel(rig.Row("Text")).Opacity, Is.EqualTo(1.0));
        });
    }

    /// <summary>
    /// Reset from the row's context menu: a real right-click on the row's container opens it, the Reset
    /// item is clicked in the menu's own popup, and the attribute leaves the FILE — then the row is
    /// greyed and no longer bold, in the real list.
    /// </summary>
    [AvaloniaTest]
    public void ResetFromTheRowsContextMenu_RemovesTheAttributeFromTheFile()
    {
        using var rig = Open();
        SelectOnCanvas(rig, "lbl");
        var row = rig.Row("Text");
        var container = rig.Container(row);

        // ⚠ On the NAME cell: the centre of the row is its value editor, a TextBox whose own
        // cut/copy/paste menu takes the request first (as Shift+F10 in the editor does, by design).
        rig.Click(rig.NameCell(row), button: MouseButton.Right);
        var menu = container.ContextMenu;
        Assert.That(menu, Is.Not.Null, "the row's container carries the menu");
        TestContext.WriteLine($"[context-menu] IsOpen after a real right-click = {menu!.IsOpen}");
        try
        {
            Assert.That(menu.IsOpen, Is.True, "the right-click opened it");
            var reset = menu.Items.OfType<MenuItem>().Single(m => (string?)m.Header == "Reset");
            Assert.That(reset.Command, Is.SameAs(row.ResetCommand), "bound to THIS row's Reset");

            // The click lands in the menu's own top level (a popup root), in ITS coordinates.
            var popupTop = TopLevel.GetTopLevel(reset) ?? throw new InvalidOperationException("the menu item has no top level");
            var at = reset.TranslatePoint(new Point(reset.Bounds.Width / 2, reset.Bounds.Height / 2), popupTop)!.Value;
            TestContext.WriteLine($"[context-menu] item top level = {popupTop.GetType().Name}; at {at}");
            popupTop.MouseDown(at, MouseButton.Left);
            popupTop.MouseUp(at, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            rig.Window.UpdateLayout();
        }
        finally
        {
            menu.Close();
        }

        Assert.Multiple(() =>
        {
            Assert.That(rig.Control("lbl").Properties.ContainsKey("Text"), Is.False, "removed from the model");
            Assert.That(rig.Vm.Text, Does.Not.Contain("Text=\"Hello\""), "and from the FILE — not written as a default");
            Assert.That(rig.NameCell(row).FontWeight, Is.Not.EqualTo(FontWeight.Bold), "no longer bold");
            Assert.That(rig.ValuePanel(row).Opacity, Is.EqualTo(0.6).Within(0.01), "greyed: now the default");
        });
    }

    // ==================================================================
    // Search, sort, collapse
    // ==================================================================

    [AvaloniaTest]
    public void SearchSortAndCollapse_ThroughTheRealView()
    {
        using var rig = Open();
        SelectOnCanvas(rig, "lbl");
        IEnumerable<string> ListedRows() =>
            rig.List.GetRealizedContainers().Select(c => rig.List.ItemFromContainer(c)).OfType<FormPropertyRow>().Select(r => r.Name);

        // Collapse: a real click on the Appearance header.
        var appearance = rig.GridVm.DisplayItems.OfType<FormPropertyCategoryHeader>().Single(h => h.Name == "Appearance");
        rig.Click(rig.Container(appearance).GetVisualDescendants().OfType<ToggleButton>().First());
        rig.Window.UpdateLayout();
        Assert.Multiple(() =>
        {
            Assert.That(appearance.IsExpanded, Is.False, "the click collapsed it");
            Assert.That(ListedRows(), Does.Not.Contain("ForeColor"), "its rows left the real list");
        });
        rig.Click(rig.Container(appearance).GetVisualDescendants().OfType<ToggleButton>().First(), dx: 8);
        Assert.That(appearance.IsExpanded, Is.True, "a second (offset) click expands it again");

        // Search: typed into the real box.
        rig.Click(rig.Search);
        rig.Window.KeyTextInput("Fore");
        Dispatcher.UIThread.RunJobs();
        rig.Window.UpdateLayout();
        Assert.That(ListedRows(), Is.EqualTo(new[] { "ForeColor" }), "the search filters the real list");

        rig.Search.Text = "";
        Dispatcher.UIThread.RunJobs();

        // Sort: the real A-Z button.
        rig.Click(rig.Grid.FindControl<ToggleButton>("AlphabeticalButton")!);
        rig.Window.UpdateLayout();
        var shown = rig.GridVm.DisplayItems.ToList();
        Assert.Multiple(() =>
        {
            Assert.That(rig.GridVm.IsAlphabetical, Is.True);
            Assert.That(shown.OfType<FormPropertyCategoryHeader>(), Is.Empty, "A-Z shows no headers");
            Assert.That(shown.OfType<FormPropertyRow>().Select(r => r.Name),
                Is.EqualTo(shown.OfType<FormPropertyRow>().Select(r => r.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase)),
                "rows in name order");
            Assert.That(rig.List.GetVisualDescendants().OfType<ToggleButton>().Any(b => b.DataContext is FormPropertyCategoryHeader),
                Is.False, "and the real list draws none");
        });
    }

    // ==================================================================
    // The selector after paste, cut and undo (pre-flight: RefreshObjects runs only on a rebuild)
    // ==================================================================

    [AvaloniaTest]
    public void TheSelector_ListsWhatPasteAdds_DropsWhatCutRemoves_AndUndoBringsItBack()
    {
        using var rig = Open();
        string[] Listed() => rig.Selector.Items.OfType<FormObjectItem>().Select(o => o.Name).ToArray();

        SelectOnCanvas(rig, "btn");
        rig.Vm.CopyControlsCommand.Execute(null);
        rig.Vm.PasteControlsCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var pasted = rig.Vm.Selection.Primary!;
        Assert.Multiple(() =>
        {
            Assert.That(pasted.Id, Is.Not.EqualTo("btn"), "precondition: the paste renamed the copy");
            Assert.That(Listed(), Does.Contain(pasted.Id), "the selector lists the pasted control");
            Assert.That((rig.Selector.SelectedItem as FormObjectItem)?.Name, Is.EqualTo(pasted.Id), "and shows it selected");
        });

        rig.Vm.CutControlsCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Multiple(() =>
        {
            Assert.That(Listed(), Does.Not.Contain(pasted.Id), "the selector drops the cut control");
            Assert.That((rig.Selector.SelectedItem as FormObjectItem)?.Name, Is.EqualTo("GridForm"), "and shows the form");
        });

        rig.Vm.UndoDesignerEditCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Multiple(() =>
        {
            Assert.That(Listed(), Does.Contain(pasted.Id), "undo brings the control back into the selector");
            Assert.That(rig.Doc.FindById(pasted.Id), Is.Not.Null, "precondition: and into the document");
            Assert.That(rig.Vm.Selection.Primary == null || rig.Doc.AllControls().Concat(rig.Doc.AllComponents())
                    .Contains(rig.Vm.Selection.Primary), Is.True,
                "⛔ one store: whatever the selection holds after an undo is in the document the grid shows");
            Assert.That(rig.GridVm.SelectedControl, Is.SameAs(rig.Vm.Selection.Primary), "the grid and the store agree");
        });
    }
}
