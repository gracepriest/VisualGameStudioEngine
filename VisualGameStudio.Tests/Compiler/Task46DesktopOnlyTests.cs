using BasicLang.Forms;
using BasicLang.Forms.Serialization;
using NUnit.Framework;
using VisualGameStudio.Shell.ViewModels.Designer;

namespace VisualGameStudio.Tests.Compiler;

[TestFixture]
public class Task46DesktopOnlyTests
{
    [Test]
    public void WebToolbox_ListsDesktopOnlyKindsWithBadge()
    {
        var toolbox = new FormToolboxViewModel { Target = FormTarget.Web };
        var dataGrid = toolbox.Items.Single(i => i.Kind == "DataGridView");
        Assert.That(dataGrid.DesktopOnly, Is.True);
        Assert.That(dataGrid.Badge, Is.EqualTo("desktop"));
        Assert.That(toolbox.Items.Single(i => i.Kind == "Button").DesktopOnly, Is.False);
    }

    [Test]
    public void LoadWebForms_RefusesADesktopOnlyKindByCode()
    {
        var document = new FormDocument { Target = FormTarget.Web, Name = "DesktopOnly" };
        document.Controls.Add(new FormControl { Kind = "DataGridView", Id = "grid" });
        var dir = Path.Combine(Path.GetTempPath(), "bl-task46-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "DesktopOnly.blwebform");
        File.WriteAllText(path, FormDocumentWriter.Create(document));

        var warnings = new List<string>();
        var forms = FormDocumentLoader.LoadWebForms(new[] { path }, warnings.Add);

        Assert.That(forms, Is.Empty);
        Assert.That(warnings.Single(), Does.Contain(DesignCodes.DesktopOnlyKind));
        Assert.That(warnings.Single(), Does.Contain("DataGridView"));
        Directory.Delete(dir, recursive: true);
    }
}
