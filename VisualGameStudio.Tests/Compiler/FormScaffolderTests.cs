using BasicLang.Forms;
using BasicLang.Forms.Serialization;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 13: creating a form.
///
/// <para>Without this nothing produces the documents the rest of the feature reads, writes and
/// gates, and the <c>.bas</c> + <c>.blwebform</c> pair is never exercised by the path a user
/// actually takes.</para>
/// </summary>
[TestFixture]
public class FormScaffolderTests
{
    /// <summary>
    /// ⛔⛔ <b>The scaffold must call <c>Me.InitializeComponent()</c>, never the bare form.</b>
    ///
    /// <para>MEASURED 2026-09-18, by building and RUNNING a two-method probe: the JavaScript backend
    /// emits an unqualified call to an instance method of the enclosing class as a BARE GLOBAL —
    /// <c>InitializeComponent();</c> instead of <c>this.InitializeComponent();</c> — while
    /// <c>Me.Method()</c> emits correctly as <c>this.Method()</c>. The build reports
    /// "Compilation successful!" either way, and the page dies on load with
    /// <i>ReferenceError: InitializeComponent is not defined</i>.</para>
    ///
    /// <para>⚠ <c>CLAUDE.md</c> recorded this as affecting "a lambda calling an unqualified method of
    /// the enclosing class". It is broader than that: ANY unqualified self-call in a class, including
    /// one straight from the constructor, which is exactly the shape every scaffolded form had. Every
    /// web form this designer produced was dead on arrival, and nothing caught it because no test ran
    /// a generated form — only the dispatch.</para>
    ///
    /// <para>The compiler defect itself is unfixed and filed separately; this is the designer not
    /// generating the shape that triggers it.</para>
    /// </summary>
    [TestCase(FormTarget.Web)]
    [TestCase(FormTarget.WinForms)]
    public void TheScaffoldQualifiesItsInitializeComponentCall(FormTarget target)
    {
        var code = FormScaffolder.Create("LoginForm", target).CodeText;

        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Contain("Me.InitializeComponent()"));
            Assert.That(code, Does.Not.Match(@"(?m)^\s*InitializeComponent\(\)"),
                "an unqualified self-call compiles green and dies at run time on the web");
        });
    }

    // ==================================================================
    // Naming — stricter than a filename check, for a measured reason
    // ==================================================================

    [TestCase("LoginForm", true)]
    [TestCase("Form1", true)]
    [TestCase("A", true)]
    [TestCase("Login_Form", false, Description = "a TYPE name with '_' breaks Me.<inherited member>")]
    [TestCase("1Login", false)]
    [TestCase("Login Form", false)]
    [TestCase("Login-Form", false)]
    [TestCase("", false)]
    [TestCase(null, false)]
    public void IsLegalFormName_AppliesTheTypeNameRule_NotTheFileNameRule(string? name, bool expected)
    {
        Assert.That(FormScaffolder.IsLegalFormName(name), Is.EqualTo(expected));
    }

    [Test]
    public void DescribeIllegalName_ExplainsTheUnderscoreRule_RatherThanJustRefusing()
    {
        // The failure it prevents is bizarre if you do not know the rule: every Me.<inherited member>
        // in the class becomes a hard error, which reads as a compiler bug rather than a naming one.
        var reason = FormScaffolder.DescribeIllegalName("Login_Form");

        Assert.That(reason, Is.Not.Null);
        Assert.That(reason, Does.Contain("underscore").And.Contain("Control names"));
    }

    [Test]
    public void Create_RefusesAnIllegalName()
    {
        Assert.Throws<ArgumentException>(() => FormScaffolder.Create("Login_Form"));
    }

    // ==================================================================
    // The pair
    // ==================================================================

    [Test]
    public void Create_ProducesBothFiles_NamedFromTheForm()
    {
        var scaffold = FormScaffolder.Create("LoginForm");

        Assert.Multiple(() =>
        {
            Assert.That(scaffold.DocumentFileName, Is.EqualTo("LoginForm.blwebform"));
            Assert.That(scaffold.CodeFileName, Is.EqualTo("LoginForm.bas"));
        });
    }

    [Test]
    public void Create_ProducesADocumentThatReadsBackClean()
    {
        var scaffold = FormScaffolder.Create("LoginForm");

        var form = FormDocumentReader.Read("LoginForm.blwebform", scaffold.DocumentText);

        Assert.Multiple(() =>
        {
            Assert.That(form.IsRefused, Is.False,
                string.Join("; ", form.Diagnostics.Select(d => d.Format())));
            Assert.That(form.Model.Name, Is.EqualTo("LoginForm"));
            Assert.That(form.Model.Layout, Is.Not.Null);
            Assert.That(form.Model.Controls, Is.Empty, "a new form starts empty");
        });
    }

    [Test]
    public void Create_ProducesACodeFileWhoseRegionsAreImmediatelyCanon()
    {
        // ⛔ The hashes must be right for EMPTY bodies. A scaffold that emitted the markers without
        // correct hashes would make the very first designer save report the designer's own output
        // as a hand edit (BL8011) — the feature would refuse to work on the file it just created.
        var scaffold = FormScaffolder.Create("LoginForm");

        var regions = RegionMarkers.Scan(scaffold.CodeText);

        Assert.Multiple(() =>
        {
            Assert.That(regions.Select(r => r.Name), Is.EquivalentTo(new[] { "controls", "init" }));
            Assert.That(regions, Has.All.Property(nameof(FormRegion.State)).EqualTo(RegionState.Canon));
        });
    }

    [Test]
    public void Create_Web_PutsTheInitRegionAfterTheHandlerArea()
    {
        // ⛔⛔ D8's ordering rule, and it is real ON THE WEB — measured 2026-09-13:
        // `addEventListener("click", AddressOf H)` with H declared later fails with "cannot
        // convert from 'Action(Of Object)' to 'Action(Of DomEvent)'". The DOM signature declares
        // the parameter type, so the erased handler has something concrete to fail against.
        // This is the opposite of the layout in D1's worked example, which illustrates the marker
        // shape rather than the ordering.
        var text = FormScaffolder.Create("LoginForm").CodeText;

        var controls = text.IndexOf("region=\"controls\"", StringComparison.Ordinal);
        var handlerArea = text.IndexOf("Your event handlers go here", StringComparison.Ordinal);
        var init = text.IndexOf("region=\"init\"", StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(controls, Is.GreaterThan(0));
            Assert.That(handlerArea, Is.GreaterThan(controls));
            Assert.That(init, Is.GreaterThan(handlerArea),
                "on the web the init region must come after where handlers are written");
        });
    }

    [Test]
    public void Create_WinForms_PutsTheInitRegionWhereTheShippedTemplateDoes()
    {
        // ⛔⛔ The OPPOSITE order from the web, and deliberately so. Owner decision 3 makes the
        // VSIX shape canonical, and that template declares btnClick_Click BELOW the
        // InitializeComponent that wires it. Measured 2026-09-13: it compiles through BasicLang
        // AND csc, and the handler binds with its full parameter types (object sender, EventArgs
        // e) — the event is an unresolvable .NET member typed as Object, so there is no declared
        // delegate for an erased handler to mismatch. Emitting the web order here would make the
        // designer generate something that disagrees with the template it is modelled on.
        var text = FormScaffolder.Create("MainForm", FormTarget.WinForms).CodeText;

        var ctor = text.IndexOf("Public Sub New()", StringComparison.Ordinal);
        var init = text.IndexOf("region=\"init\"", StringComparison.Ordinal);
        var handlerArea = text.IndexOf("Your event handlers go here", StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(ctor, Is.GreaterThan(0));
            Assert.That(init, Is.GreaterThan(ctor), "InitializeComponent follows the constructor");
            Assert.That(handlerArea, Is.GreaterThan(init),
                "handlers sit below the init region, as the shipped VSIX template has them");
        });
    }

    [Test]
    public void Create_TheFirstDesignerSaveSucceeds_AndIsIdempotent()
    {
        // The whole pair, exercised the way a user would: scaffold, then have the designer write a
        // control into it.
        var scaffold = FormScaffolder.Create("LoginForm");
        var document = FormDocumentReader.Read("LoginForm.blwebform", scaffold.DocumentText);

        var button = new FormControl { Kind = "Button", Id = "btnLogin", TabIndex = 0 };
        button.Properties["Text"] = "Sign in";
        document.Model.Controls.Add(button);

        var first = RegionWriter.Write(
            "LoginForm.bas", scaffold.CodeText, document.Model, scaffold.DocumentFileName);

        Assert.That(first.Refused, Is.False, string.Join("; ", first.Diagnostics.Select(d => d.Format())));
        Assert.Multiple(() =>
        {
            Assert.That(first.Changed, Is.True);
            Assert.That(first.Text, Does.Contain("Private btnLogin As Element"));
            Assert.That(first.Text, Does.Contain("""btnLogin = doc.getElementById("btnLogin")"""));
        });

        var second = RegionWriter.Write(
            "LoginForm.bas", first.Text, document.Model, scaffold.DocumentFileName);

        Assert.Multiple(() =>
        {
            Assert.That(second.Refused, Is.False, "the hash it just stamped must match what it wrote");
            Assert.That(second.Changed, Is.False, "a no-op save writes nothing");
        });
    }

    [Test]
    public void Create_WinForms_ProducesAFormThatInheritsForm()
    {
        var scaffold = FormScaffolder.Create("MainForm", FormTarget.WinForms);

        Assert.Multiple(() =>
        {
            Assert.That(scaffold.DocumentFileName, Is.EqualTo("MainForm.blform"));
            Assert.That(scaffold.CodeText, Does.Contain("Public Class MainForm"));
            Assert.That(scaffold.CodeText, Does.Contain("Inherits Form"));
            Assert.That(scaffold.CodeText, Does.Contain("Using System.Windows.Forms"));
        });
    }

    [Test]
    public void Create_CodeBehindNamesTheDocumentInBothMarkers()
    {
        // The marker's form= attribute is how a reader knows which document owns the region.
        var scaffold = FormScaffolder.Create("LoginForm");

        Assert.That(
            scaffold.CodeText.Split("LoginForm.blwebform").Length - 1, Is.EqualTo(2),
            "both region markers must name the document");
    }
}
