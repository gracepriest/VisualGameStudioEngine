using BasicLang.Forms;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Events;
using VisualGameStudio.Shell.ViewModels.Documents;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 44: opening a DOM-style web form offers the one-way conversion to the portable Forms
/// library. These drive the same view-model methods the IDE notification's two actions call, so the
/// route is covered headlessly; the notification's exact wording is the view's, not this contract.
/// </summary>
[TestFixture]
public class Task44ConversionOfferTests
{
    private const string Dir = "/proj/";

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

    private static (CodeEditorDocumentViewModel Vm, Files Files, string CodePath, List<DesignerDiagnosticsEvent> Published)
        Open(bool portable = false)
    {
        var scaffold = FormScaffolder.Create("ConvertMe", FormTarget.Web, portable: portable);

        var files = new Files();
        files.Contents[Dir + scaffold.DocumentFileName] = scaffold.DocumentText;
        files.Contents[Dir + scaffold.CodeFileName] = scaffold.CodeText;

        var published = new List<DesignerDiagnosticsEvent>();
        var events = new Mock<IEventAggregator>();
        events.Setup(e => e.Publish(It.IsAny<DesignerDiagnosticsEvent>()))
            .Callback((DesignerDiagnosticsEvent e) => published.Add(e));

        var vm = new CodeEditorDocumentViewModel(files.Service, events.Object)
        {
            FilePath = Dir + scaffold.DocumentFileName
        };
        vm.SetContent(files.Contents[vm.FilePath]);
        vm.EnterDesignModeForFormDocument();

        return (vm, files, Dir + scaffold.CodeFileName, published);
    }

    [Test]
    public async Task ADomStyleForm_IsOfferedOnce_AndNotNowTouchesNoByte()
    {
        var (vm, files, codePath, _) = Open();
        var before = files.Contents[codePath];

        Assert.That(await vm.ShouldOfferPortableConversionAsync(), Is.True);
        Assert.That(await vm.ShouldOfferPortableConversionAsync(), Is.False, "the offer is made once per open");

        vm.DismissPortableConversionOffer();
        var stillOffered = await vm.ShouldOfferPortableConversionAsync();

        Assert.Multiple(() =>
        {
            Assert.That(files.Contents[codePath], Is.EqualTo(before), "Not now is byte-identical");
            Assert.That(stillOffered, Is.False, "and the offer stays dismissed");
        });
    }

    [Test]
    public async Task Convert_WritesTheSameTextTheCliFunctionWould_AndPublishesItsFindings()
    {
        var (vm, files, codePath, published) = Open();
        var before = files.Contents[codePath];
        var expected = FormCodeConversion.Convert(vm.DesignDocument!, before).Text;

        var converted = await vm.ConvertToPortableAsync();

        Assert.Multiple(() =>
        {
            Assert.That(converted, Is.True);
            Assert.That(files.Contents[codePath], Is.EqualTo(expected),
                "the IDE route uses FormCodeConversion, exactly as design --convert does");
            Assert.That(files.Contents[codePath], Does.Contain("style=\"portable\""));
            Assert.That(published, Has.Count.EqualTo(1), "one publish replaces the Error List collection");
            Assert.That(published[0].FilePath, Is.EqualTo(codePath), "findings are keyed to the code-behind");
        });

        Assert.That(await vm.ShouldOfferPortableConversionAsync(), Is.False, "a converted form is no longer offered");
    }

    [Test]
    public async Task AnAlreadyPortableForm_IsNotOffered()
    {
        var (vm, _, _, _) = Open(portable: true);

        Assert.That(await vm.ShouldOfferPortableConversionAsync(), Is.False);
    }

    /// <summary>
    /// The notification itself is view code; this pins that the real OpenFileAsync route still asks the
    /// offer question and wires both actions to the covered methods. A rename of either action would
    /// otherwise leave a dead button in a green suite.
    /// </summary>
    [Test]
    public void MainWindowOpenRoute_OffersConvertAndNotNow()
    {
        var path = FindRepoFile("VisualGameStudio.Shell", "ViewModels", "MainWindowViewModel.cs");
        Assert.That(path, Is.Not.Null, "the Shell source is reachable from the test output");
        var source = File.ReadAllText(path!);

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("ShouldOfferPortableConversionAsync()"));
            Assert.That(source, Does.Contain("ConvertToPortableAsync()"));
            Assert.That(source, Does.Contain("DismissPortableConversionOffer"));
            Assert.That(source, Does.Contain("new(\"Convert\""));
            Assert.That(source, Does.Contain("new(\"Not now\""));
        });
    }

    private static string? FindRepoFile(params string[] relativeParts)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
