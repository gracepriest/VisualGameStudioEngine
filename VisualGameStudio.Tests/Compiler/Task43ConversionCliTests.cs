using System.Text;
using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 43's missing gate: the CLI's <c>design --convert</c> must write EXACTLY the bytes
/// <see cref="FormCodeConversion.Convert"/> produces — no BOM, no line-ending rewrite, no
/// second implementation drifting from the pure function the IDE also calls.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class Task43ConversionCliTests
{
    [Test]
    public async Task DesignConvert_WritesTheSameBytesAsTheFunction()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-task43-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var scaffold = FormScaffolder.Create("ConvertMe", FormTarget.Web);
            var document = new FormDocument { Target = FormTarget.Web, Name = "ConvertMe" };
            var button = new FormControl { Kind = "Button", Id = "btnGo" };
            button.Binds.Add(new FormBind { Event = "Click", Handler = "btnGo_Click" });
            document.Controls.Add(button);

            var marker = "    ' <vgs:designer region=\"init\"";
            var at = scaffold.CodeText.IndexOf(marker, StringComparison.Ordinal);
            Assert.That(at, Is.GreaterThanOrEqualTo(0), "precondition: the scaffold carries the init marker");
            var code = scaffold.CodeText.Insert(
                at,
                "    Private Sub btnGo_Click(e As DomEvent)\n" +
                "        txtUser.value = \"x\"\n" +
                "    End Sub\n\n");

            var documentPath = Path.Combine(dir, scaffold.DocumentFileName);
            var codePath = Path.Combine(dir, scaffold.CodeFileName);
            File.WriteAllText(documentPath, BasicLang.Forms.Serialization.FormDocumentWriter.Create(document));
            File.WriteAllText(codePath, code);

            var expected = FormCodeConversion.Convert(
                BasicLang.Forms.Serialization.FormDocumentReader.Read(documentPath, File.ReadAllText(documentPath)).Model,
                code).Text;

            var (exit, stdout, stderr) = await CliTestHarness.RunCli(dir, "design", "--convert", documentPath);

            Assert.Multiple(() =>
            {
                Assert.That(exit, Is.EqualTo(0), $"stdout:\n{stdout}\nstderr:\n{stderr}");
                Assert.That(File.ReadAllBytes(codePath), Is.EqualTo(Encoding.UTF8.GetBytes(expected)),
                    "the CLI wrote the function's bytes, BOM and line endings included");
            });
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }
}
