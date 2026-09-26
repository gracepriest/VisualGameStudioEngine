using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Spec §2.3 Retarget — FormRetarget visits FormRoot EXPLICITLY: a root row that applies to the
/// destination crosses; one that does not is NAMED (never carried silently); a root bind with no name
/// on the destination is dropped and named with RetargetBindLost.
/// </summary>
[TestFixture]
public class FormRootRetargetTests
{
    private static FormTarget Other(FormTarget t) => t == FormTarget.Web ? FormTarget.WinForms : FormTarget.Web;

    private static string Sample(FormPropertyDef row) => row.Type switch
    {
        FormPropertyType.Size => "641, 481",
        _ => "sample" + row.Name
    };

    /// <summary>
    /// ⛔ Catalog-driven: a new FormRoot row is covered the day it is added. A row that does not apply to
    /// the destination must be NAMED in some finding — RetargetLayoutCrossed for the pixel⇄cell rows
    /// (ClientSize/Cols/Rows/Gap, plan scope call S4), RetargetPropertyLost 'form.X' for every other.
    /// ⚠ RE-CHECK IN SLICE 3: the RetargetPropertyLost arm has no row until Properties-stored rows exist.
    /// </summary>
    [Test]
    public void EveryFormRootRow_CrossesOrIsNamed([Values(FormTarget.WinForms, FormTarget.Web)] FormTarget from)
    {
        var to = Other(from);
        var source = new FormDocument { Target = from, Name = "Sweep" };
        var rows = FormControlCatalog.FormRoot.Properties.Where(r => r.AppliesTo(from)).ToList();

        foreach (var row in rows)
        {
            Assert.That(FormRootValues.Set(source, row, Sample(row)), Is.True, $"form.{row.Name} sample must store");
        }

        var result = FormRetarget.Convert(source, to);
        var messages = result.Diagnostics.Select(d => d.Message).ToList();

        Assert.Multiple(() =>
        {
            foreach (var row in rows)
            {
                if (row.AppliesTo(to))
                {
                    Assert.That(FormRootValues.Get(result.Document, row), Is.EqualTo(Sample(row)),
                        $"form.{row.Name} applies on both targets and must cross {from}→{to}");
                    continue;
                }

                var pieces = Sample(row).Split(',').Select(p => p.Trim());
                Assert.That(messages.Any(m => pieces.All(p => m.Contains(p))), Is.True,
                    $"form.{row.Name} does not exist on {to}; it must be NAMED, never dropped silently. Findings:\n" +
                    string.Join("\n", messages));
            }
        });
    }

    [Test]
    public void TheCaption_CrossesBothWays()
    {
        var win = new FormDocument { Target = FormTarget.WinForms, Name = "Login", Text = "Sign in" };
        var web = new FormDocument { Target = FormTarget.Web, Name = "Login", Text = "Welcome" };

        Assert.Multiple(() =>
        {
            Assert.That(FormRetarget.Convert(win, FormTarget.Web).Document.Text, Is.EqualTo("Sign in"));
            Assert.That(FormRetarget.Convert(web, FormTarget.WinForms).Document.Text, Is.EqualTo("Welcome"));
        });
    }

    /// <summary>
    /// The page's title is Text ?? Name, so a caption EQUAL to the name is written as absence on the page
    /// — which keeps RoundTrip_AFixedPointWebForm_ComesBackByteIdentical byte-identical.
    /// </summary>
    [Test]
    public void ACaptionEqualToTheName_IsAbsenceOnThePage()
    {
        var win = new FormDocument { Target = FormTarget.WinForms, Name = "Login", Text = "Login" };

        Assert.That(FormRetarget.Convert(win, FormTarget.Web).Document.Text, Is.Null);
    }

    [Test]
    public void APageWithNoCaption_BecomesAWindowCaptionedWithItsName()
    {
        var web = new FormDocument { Target = FormTarget.Web, Name = "Login" };

        Assert.That(FormRetarget.Convert(web, FormTarget.WinForms).Document.Text, Is.EqualTo("Login"));
    }

    /// <summary>
    /// The web caption read from the FILE crosses — the reader models Text on both targets now, so it is
    /// no longer an unknown attribute the retarget used to name as dropped.
    /// </summary>
    [Test]
    public void AWebCaptionReadFromTheFile_CrossesToTheWindow_AndIsNotReportedLost()
    {
        var source = BasicLang.Forms.Serialization.FormDocumentReader.Read("Login.blwebform", """
            <WebForm Name="Login" Version="1" Text="Welcome back"><Controls/></WebForm>
            """).Model;

        var result = FormRetarget.Convert(source, FormTarget.WinForms);

        Assert.Multiple(() =>
        {
            Assert.That(result.Document.Text, Is.EqualTo("Welcome back"));
            Assert.That(result.Document.UnknownAttributes.ContainsKey("Text"), Is.False);
            Assert.That(result.Diagnostics.Where(d => d.Code == DesignCodes.RetargetPropertyLost), Is.Empty,
                "the caption crossed; nothing about it was lost");
        });
    }

    [Test]
    public void ARootBind_WithNoFormEventOnTheDestination_IsDroppedAndNamed(
        [Values(FormTarget.WinForms, FormTarget.Web)] FormTarget from)
    {
        var source = new FormDocument { Target = from, Name = "Login" };
        source.Binds.Add(new FormBind { Event = "Load", Handler = "Login_Load" });

        var result = FormRetarget.Convert(source, Other(from));

        Assert.Multiple(() =>
        {
            Assert.That(result.Document.Binds, Is.Empty, "never carried silently");
            Assert.That(result.Diagnostics.Single(d => d.Code == DesignCodes.RetargetBindLost).Message,
                Does.Contain("'form'").And.Contain("Login_Load"));
        });
    }

    [Test]
    public void ARootBind_ReadFromTheFile_IsDroppedAndNamed_NotCarriedAsAnUnknownChild()
    {
        var source = BasicLang.Forms.Serialization.FormDocumentReader.Read("Login.blform", """
            <Form Name="Login" Version="1"><Bind Event="Load" Handler="Login_Load"/><Controls/></Form>
            """).Model;

        var result = FormRetarget.Convert(source, FormTarget.Web);

        Assert.Multiple(() =>
        {
            Assert.That(result.Document.Binds, Is.Empty);
            Assert.That(result.Document.UnknownChildren, Is.Empty);
            Assert.That(result.Diagnostics.Count(d => d.Code == DesignCodes.RetargetBindLost), Is.EqualTo(1));
        });
    }

    [Test]
    public void ADegradedClientSize_IsDroppedAndNamed_NotCarriedAsAnUnknownAttribute()
    {
        var source = BasicLang.Forms.Serialization.FormDocumentReader.Read("Login.blform", """
            <Form Name="Login" Version="1" Width="12px" Height="300"><Controls/></Form>
            """).Model;

        var result = FormRetarget.Convert(source, FormTarget.Web);

        Assert.Multiple(() =>
        {
            Assert.That(result.Document.UnknownAttributes.ContainsKey("Width"), Is.False,
                "the source's own degraded ClientSize storage must not ride onto a page as dead data");
            Assert.That(result.Diagnostics.Any(d => d.Code == DesignCodes.RetargetPropertyLost && d.Message.Contains("12px")),
                Is.True);
        });
    }
}
