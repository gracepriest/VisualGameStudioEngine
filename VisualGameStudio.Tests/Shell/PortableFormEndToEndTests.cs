using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Threading;
using BasicLang.Forms;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;
using VisualGameStudio.Editor.Controls;
using VisualGameStudio.Shell.Configuration;
using VisualGameStudio.Shell.Controls;
using VisualGameStudio.Shell.Dock;
using VisualGameStudio.Shell.ViewModels;
using VisualGameStudio.Shell.ViewModels.Documents;
using VisualGameStudio.Shell.Views.Documents;
using VisualGameStudio.Tests.Compiler;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// Task 35A, spec §11.8. One form, driven from the IDE's generated command to two real runtimes:
/// the generated Add Web Form (portable) command, a real toolbox drop at two canvas sizes, a real
/// double-click, a real code-editor edit, the IDE's BuildService, the CLI, Edge, and the WinForms
/// reference window. The same typed input and click must make the same Label text on both targets.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class PortableFormEndToEndTests
{
    private const string FormName = "PortableEndToEnd";
    private const string ProjectName = "PortableEndToEnd";
    private const string WebLabelId = "Label1";
    private const string WebTextBoxId = "TextBox1";
    private const string WebButtonId = "Button1";
    private const string TypedText = "Ada";
    private const string ExpectedLabel = "Ada!";

    private static readonly (double Width, double Height) FirstSize = (800, 560);
    private static readonly (double Width, double Height) SecondSize = (1000, 700);

    private sealed class FormRig : IDisposable
    {
        public required CodeEditorDocumentViewModel Vm { get; init; }
        public required Window Window { get; init; }
        public required CodeEditorDocumentView View { get; init; }
        public required FormCanvasControl Canvas { get; init; }

        private bool _disposed;

        public FormDocument Doc => Vm.DesignDocument!;

        public FormCanvasTransform Fit => FormCanvasControl.Fit(Doc, Canvas.Bounds.Size);

        public double Zoom => Fit.Zoom;

        public Point InWindow(double formX, double formY) =>
            Canvas.TranslatePoint(Fit.ToCanvas(new Point(formX, formY)), Window)
            ?? throw new InvalidOperationException("the designer canvas is not in the window");

        public void DropFromToolbox(string kind, double formX, double formY)
        {
            var at = InWindow(formX, formY);
            var data = new DataObject();
            data.Set(FormCanvasControl.ControlKindFormat, kind);
            Window.DragDrop(at, RawDragEventType.DragEnter, data, DragDropEffects.Copy);
            Window.DragDrop(at, RawDragEventType.DragOver, data, DragDropEffects.Copy);
            Window.DragDrop(at, RawDragEventType.Drop, data, DragDropEffects.Copy);
            Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        public Point CentreOf(FormControl control)
        {
            if (control.Geometry is not PixelGeometry pixel)
            {
                throw new InvalidOperationException($"{control.Id} is not in pixel space");
            }

            return InWindow(pixel.X + pixel.Width / 2.0, pixel.Y + pixel.Height / 2.0);
        }

        public void DoubleClick(Point at)
        {
            Window.MouseDown(at, MouseButton.Left);
            Window.MouseUp(at, MouseButton.Left);
            Window.MouseDown(at, MouseButton.Left);
            Window.MouseUp(at, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }

        public void Resize(double width, double height)
        {
            Window.Width = width;
            Window.Height = height;
            Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Window.Close();
        }
    }

    private static FormRig OpenFormRig(CodeEditorDocumentViewModel vm, double width, double height)
    {
        var view = new CodeEditorDocumentView { DataContext = vm };
        var window = new Window
        {
            Width = width,
            Height = height,
            Content = view,
            RequestedThemeVariant = ThemeVariant.Dark
        };
        window.Styles.Add(new StyleInclude(new Uri("avares://VisualGameStudio/"))
        {
            Source = new Uri("avares://VisualGameStudio/Resources/Styles/AppStyles.axaml")
        });

        try
        {
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var canvas = view.FindControl<FormCanvasControl>("DesignCanvas")
                         ?? throw new InvalidOperationException("DesignCanvas not found");
            return new FormRig { Vm = vm, Window = window, View = view, Canvas = canvas };
        }
        catch
        {
            window.Close();
            throw;
        }
    }

    private static (Window Window, CodeEditorDocumentView View) OpenCodeRig(
        CodeEditorDocumentViewModel vm, double width, double height)
    {
        var view = new CodeEditorDocumentView { DataContext = vm };
        var window = new Window
        {
            Width = width,
            Height = height,
            Content = view,
            RequestedThemeVariant = ThemeVariant.Dark
        };
        window.Styles.Add(new StyleInclude(new Uri("avares://VisualGameStudio/"))
        {
            Source = new Uri("avares://VisualGameStudio/Resources/Styles/AppStyles.axaml")
        });

        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return (window, view);
    }

    private static async Task<T> WaitForAsync<T>(Func<T?> read, string what, int timeoutMs = 10_000)
        where T : class
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            var value = read();
            if (value != null)
            {
                return value;
            }

            await Task.Delay(20);
        }

        Assert.Fail($"timed out waiting for {what}");
        throw new InvalidOperationException("unreachable");
    }

    private static string InsertHandlerBody(string code, string signature, string body)
    {
        var at = code.IndexOf(signature, StringComparison.Ordinal);
        Assert.That(at, Is.GreaterThanOrEqualTo(0), $"handler signature not found: {signature}");
        var newline = code.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lineEnd = code.IndexOf(newline, at, StringComparison.Ordinal);
        Assert.That(lineEnd, Is.GreaterThan(at), "the handler signature is not a complete line");
        var insertAt = lineEnd + newline.Length;
        return code.Insert(insertAt, "        " + body + newline);
    }

    private static string AddCover(string code) =>
        code.Insert(
            code.LastIndexOf("End Class", StringComparison.Ordinal),
            "    Public Sub Cover()\n    End Sub\n");

    [AvaloniaTest]
    public async Task TheGeneratedPortableForm_RunsTheSameBehaviourInEdgeAndWinForms()
    {
        PortableRuntimeHarnesses.RequireWeb();

        var work = Path.Combine(Path.GetTempPath(), "bl-portable-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);

        using var provider = new ServiceCollection()
            .ConfigureServices()
            .AddSingleton<IDialogService>(CreateDialogs())
            .BuildServiceProvider();

        try
        {
            var projectPath = WriteProject(work);
            var projectService = provider.GetRequiredService<IProjectService>();
            var project = await projectService.OpenProjectAsync(projectPath);
            Assert.That(project.TargetBackend, Is.EqualTo(TargetBackend.JavaScript));

            var main = provider.GetRequiredService<MainWindowViewModel>();
            Assert.That(main.SolutionExplorer.AddNewFormCommand, Is.Not.Null,
                "the generated portable form command must exist on the real solution explorer");

            await main.SolutionExplorer.AddNewFormCommand.ExecuteAsync(null);

            var formPath = Path.Combine(project.ProjectDirectory, FormName + ".blwebform");
            var codePath = BasicLang.Forms.FormCodeBehind.PathFor(formPath);
            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(formPath), Is.True, "the command wrote no form document");
                Assert.That(File.Exists(codePath), Is.True, "the command wrote no code-behind");
                Assert.That(project.Items.Any(i =>
                    string.Equals(i.Include, FormName + ".blwebform", StringComparison.OrdinalIgnoreCase)),
                    Is.True, "the form document is not an explicit project item");
                Assert.That(project.Items.Any(i =>
                    string.Equals(i.Include, FormName + ".bas", StringComparison.OrdinalIgnoreCase)),
                    Is.True, "the code-behind is not an explicit project item");
            });

            var dock = provider.GetRequiredService<DockFactory>();
            var formDoc = await WaitForAsync(
                () => dock.GetAllDocuments().OfType<CodeEditorDocumentViewModel>()
                    .SingleOrDefault(d => string.Equals(d.FilePath, formPath, StringComparison.OrdinalIgnoreCase)),
                "the form document to open in the real main window");
            Assert.That(formDoc.IsDesignMode, Is.True, "the shell's file-open route must enter design mode");
            Assert.That(formDoc.DesignDocument, Is.Not.Null);

            using var formRig = OpenFormRig(formDoc, FirstSize.Width, FirstSize.Height);
            Assert.That(formRig.Zoom, Is.Not.EqualTo(1.0).Within(0.001),
                "the first window size must exercise a non-1 canvas zoom");

            formRig.DropFromToolbox("Label", 40, 40);
            var firstZoom = formRig.Zoom;
            formRig.Resize(SecondSize.Width, SecondSize.Height);
            Assert.That(formRig.Zoom, Is.Not.EqualTo(1.0).Within(0.001),
                "the second window size must exercise a non-1 canvas zoom");
            Assert.That(formRig.Zoom, Is.Not.EqualTo(firstZoom).Within(0.001),
                "the second window size must really change the canvas scale");
            formRig.DropFromToolbox("TextBox", 40, 90);
            formRig.DropFromToolbox("Button", 40, 140);

            var model = formRig.Doc;
            Assert.That(model.Controls.Select(c => c.Kind), Is.EqualTo(new[] { "Label", "TextBox", "Button" }));
            Assert.That(model.Controls.Select(c => c.Id),
                Is.EqualTo(new[] { WebLabelId, WebTextBoxId, WebButtonId }),
                "drop order must keep the fixture ids stable");

            var button = model.FindById(WebButtonId)!;
            Assert.That(formRig.Canvas.ActivateControlCommand, Is.Not.Null,
                "the real design view never bound the activate command");
            formRig.DoubleClick(formRig.CentreOf(button));
            Assert.That(formRig.Canvas.SelectedControl, Is.SameAs(button),
                "the double-click did not land on the dropped Button");

            var handlerLine = "Private Sub Button1_Click(sender As Object, e As EventArgs)";
            var codeDoc = await WaitForAsync(
                () => dock.GetAllDocuments().OfType<CodeEditorDocumentViewModel>()
                    .SingleOrDefault(d => string.Equals(d.FilePath, codePath, StringComparison.OrdinalIgnoreCase)),
                "the double-click to open the code-behind through the shell's navigation route");
            await WaitForAsync(
                () => File.Exists(codePath) && File.ReadAllText(codePath).Contains(handlerLine, StringComparison.Ordinal)
                    ? codeDoc : null,
                "the handler stub to land in the code-behind");

            var codeText = File.ReadAllText(codePath);
            var handlerAt = codeText.IndexOf(handlerLine, StringComparison.Ordinal);
            var initAt = codeText.IndexOf("<vgs:designer region=\"init\"", StringComparison.Ordinal);
            Assert.Multiple(() =>
            {
                Assert.That(handlerAt, Is.GreaterThan(initAt),
                    "the portable handler stub must land after the init region");
                Assert.That(codeDoc.Text, Does.Contain(handlerLine),
                    "the open code-behind must show the handler, not just the disk");
                Assert.That(model.FindById(WebButtonId)!.Binds.Any(b => b.Handler == "Button1_Click" && b.Event == "Click"),
                    Is.True, "double-click must wire the handler in the form document");
            });

            Assert.That(await formDoc.SaveAsync(), Is.True, "the designer save must succeed");

            var (codeWindow, codeView) = OpenCodeRig(codeDoc, 1100, 720);
            try
            {
                var editor = codeView.FindControl<CodeEditorControl>("MainEditor")
                             ?? throw new InvalidOperationException("MainEditor not found");
                var editorDocument = editor.Document
                                     ?? throw new InvalidOperationException("the real code editor has no document");
                var body = "        Label1.Text = TextBox1.Text & \"!\"\n";
                var signatureAt = editorDocument.Text.IndexOf(handlerLine, StringComparison.Ordinal);
                Assert.That(signatureAt, Is.GreaterThanOrEqualTo(0));
                var newline = editorDocument.Text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
                var lineEnd = editorDocument.Text.IndexOf(newline, signatureAt, StringComparison.Ordinal);
                editorDocument.Replace(lineEnd + newline.Length, 0, body);
                Dispatcher.UIThread.RunJobs();

                Assert.That(codeDoc.Text, Does.Contain("Label1.Text = TextBox1.Text & \"!\""),
                    "the code editor's document must feed the view model");
                Assert.That(await codeDoc.SaveAsync(), Is.True, "saving the typed handler must succeed");
                Assert.That(File.ReadAllText(codePath), Does.Contain("Label1.Text = TextBox1.Text & \"!\""),
                    "SaveAsync must write the handler body, not leave it in the buffer");
            }
            finally
            {
                codeWindow.Close();
            }

            formRig.Dispose();

            var buildService = provider.GetRequiredService<IBuildService>();
            var build = await buildService.BuildProjectAsync(projectService.CurrentProject!);
            Assert.That(build.Success, Is.True,
                "the IDE BuildService failed:\n" + string.Join("\n", build.Diagnostics.Select(d => d.Message)));

            var site = build.OutputPath!;
            var html = Path.Combine(site, FormName + ".html");
            Assert.That(File.Exists(html), Is.True,
                $"BuildService wrote no page for the generated form in '{site}'");

            var webCase = new PortableRuntimeCase(
                FormName,
                model,
                File.ReadAllText(codePath),
                WebActions: new[]
                {
                    new PortableWebAction("clear", WebTextBoxId),
                    new PortableWebAction("type", WebTextBoxId, TypedText),
                    new PortableWebAction("click", WebButtonId),
                    new PortableWebAction(
                        "eval",
                        Script: "document.title = \"|\" + document.getElementById(\"" + WebLabelId + "\").textContent;")
                });
            var edge = PortableRuntimeHarnesses.RunEdge(site, new[] { webCase })[FormName];
            Assert.That(edge, Is.EqualTo(ExpectedLabel), "the web page did not produce the typed label");

            // Prove the CLI route separately: remove the IDE-built site, then let the real CLI build it.
            Directory.Delete(Path.Combine(project.ProjectDirectory, "bin"), recursive: true);
            var (exit, stdout, stderr) = CliTestHarness.RunProcess(
                CliTestHarness.CliPath(), new[] { "build", projectPath }, project.ProjectDirectory, timeoutMs: 180_000);
            Assert.That(exit, Is.Zero, $"the real CLI did not build the generated form.\n{stdout}\n{stderr}");
            var cliSite = Path.Combine(project.ProjectDirectory, "bin", "Debug", project.TargetFramework ?? "net8.0");
            var cliHtml = Path.Combine(cliSite, FormName + ".html");
            Assert.That(File.Exists(cliHtml), Is.True,
                "the CLI wrote no page for the generated form: " +
                (Directory.Exists(Path.Combine(project.ProjectDirectory, "bin"))
                    ? string.Join(", ", Directory.GetFiles(Path.Combine(project.ProjectDirectory, "bin"), "*",
                        SearchOption.AllDirectories).Select(f => Path.GetRelativePath(project.ProjectDirectory, f)))
                    : "<no bin>"));

            var desktopPair = FormRetarget.ConvertToPair(model, FormTarget.WinForms);
            var desktopSource = InsertHandlerBody(
                desktopPair.CodeText,
                "Private Sub Button1_Click(sender As Object, e As EventArgs)",
                "Label1.Text = TextBox1.Text & \"!\"");
            desktopSource = AddCover(desktopSource);
            var desktopCase = new PortableRuntimeCase(
                FormName,
                model,
                File.ReadAllText(codePath),
                DesktopDriver:
                    "SelectAllKeys(Find(form, \"TextBox1\"));\n" +
                    "Key(Find(form, \"TextBox1\"), 8, (char)0);\n" +
                    "TypeText(Find(form, \"TextBox1\"), \"" + TypedText + "\");\n" +
                    "Click(Find(form, \"Button1\"));\n" +
                    "Console.WriteLine(Find(form, \"Label1\").Text);",
                DesktopSource: desktopSource);

            var winForms = PortableRuntimeHarnesses.RunWinForms(work, new[] { desktopCase })[FormName];
            Assert.That(winForms, Is.EqualTo(edge),
                "WinForms and Edge must read the same label after the same typed text and click");
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch { /* best effort */ }
        }
    }

    private static IDialogService CreateDialogs()
    {
        var dialogs = new Mock<IDialogService>();
        dialogs.Setup(d => d.ShowInputDialogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(FormName);
        dialogs.Setup(d => d.ShowMessageAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DialogButtons>(), It.IsAny<DialogIcon>()))
            .ReturnsAsync(DialogResult.Ok);
        return dialogs.Object;
    }

    private static string WriteProject(string work)
    {
        var dir = Path.Combine(work, "project");
        Directory.CreateDirectory(dir);
        var projectPath = Path.Combine(dir, ProjectName + ".blproj");
        File.WriteAllText(projectPath, $"""
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>{ProjectName}</ProjectName>
                <OutputType>Exe</OutputType>
                <TargetBackend>JavaScript</TargetBackend>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="Main.bas" />
              </ItemGroup>
            </BasicLangProject>
            """);
        File.WriteAllText(Path.Combine(dir, "Main.bas"),
            "Sub Main()\n    Console.WriteLine(\"App loaded\")\nEnd Sub\n");
        return projectPath;
    }
}
