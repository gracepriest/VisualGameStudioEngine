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
        // ⚠ Not 600: MobileBreakpoint's default, which a sweep could satisfy by accident.
        FormPropertyType.Int => "480",
        // Slice 3's Properties-stored rows — each a value usable on BOTH targets where the row exists on both, so a
        // crossing row crosses clean and a single-target row is the RetargetPropertyLost arm.
        FormPropertyType.Bool => "true",
        FormPropertyType.Enum => row.AllowedValues![^1],
        FormPropertyType.Color => "#123456",
        FormPropertyType.Font => "Arial, 11pt, style=Bold",
        FormPropertyType.Fraction => "0.75",
        FormPropertyType.Reference => "btnSample",
        FormPropertyType.Image => "Resources/sample.png",
        FormPropertyType.Icon => "Resources/app.ico",
        _ => "sample" + row.Name
    };

    /// <summary>
    /// The sweep's source documents. Web Canvas (spec 2026-09-27 §6, Task 11): its design size crosses to the
    /// window EXACTLY, and MobileBreakpoint — web Canvas only — is dropped and named.
    /// </summary>
    private static IEnumerable<TestCaseData> Sources()
    {
        yield return new TestCaseData(FormTarget.WinForms, null).SetName("{m}(WinForms)");
        yield return new TestCaseData(FormTarget.Web, FormLayoutKind.Grid).SetName("{m}(Web Grid)");
        yield return new TestCaseData(FormTarget.Web, FormLayoutKind.Flow).SetName("{m}(Web Flow)");
        yield return new TestCaseData(FormTarget.Web, FormLayoutKind.Canvas).SetName("{m}(Web Canvas)");
    }

    /// <summary>
    /// ⛔ Catalog-driven: a new FormRoot row is covered the day it is added. A row that does not apply to
    /// the destination must be NAMED in some finding — RetargetLayoutCrossed for the pixel⇄cell rows
    /// (ClientSize/Cols/Rows/Gap, plan scope call S4), RetargetPropertyLost 'form.X' for every other
    /// (MobileBreakpoint from a Canvas page today).
    /// </summary>
    [TestCaseSource(nameof(Sources))]
    public void EveryFormRootRow_CrossesOrIsNamed(FormTarget from, FormLayoutKind? layout)
    {
        var to = Other(from);
        FormLayoutKind? toLayout = to == FormTarget.Web ? FormLayoutKind.Grid : null;   // what the retarget produces
        var source = new FormDocument
        {
            Target = from, Name = "Sweep",
            Layout = layout is { } kind ? new FormLayout { Kind = kind } : null
        };
        var rows = FormControlCatalog.FormRoot.Properties.Where(r => FormRootValues.Applies(r, from, layout)).ToList();

        foreach (var row in rows)
        {
            Assert.That(FormRootValues.Set(source, row, Sample(row)), Is.True, $"form.{row.Name} sample must store");
        }

        var result = FormRetarget.Convert(source, to);
        var all = string.Join("\n", result.Diagnostics.Select(d => d.Message));

        Assert.Multiple(() =>
        {
            foreach (var row in rows)
            {
                if (FormRootValues.Applies(row, to, toLayout))
                {
                    Assert.That(FormRootValues.Get(result.Document, row), Is.EqualTo(Sample(row)),
                        $"form.{row.Name} applies on both targets and must cross {from}→{to}");
                    continue;
                }

                // ⛔ The CODE is asserted, not just the text: the pixel⇄cell edge rows are named by the
                // layout finding (scope call S4); every other row by RetargetPropertyLost 'form.X'.
                var pieces = Sample(row).Split(',').Select(p => p.Trim()).ToList();
                var named = IsLayoutEdge(row)
                    ? result.Diagnostics.Any(d => d.Code == DesignCodes.RetargetLayoutCrossed &&
                                                  pieces.All(p => d.Message.Contains(p)))
                    : result.Diagnostics.Any(d => d.Code == DesignCodes.RetargetPropertyLost &&
                                                  d.Message.Contains($"'form.{row.Name}'") &&
                                                  pieces.All(p => d.Message.Contains(p)));
                Assert.That(named, Is.True,
                    $"form.{row.Name} does not exist on {to}; it must be NAMED with " +
                    $"{(IsLayoutEdge(row) ? "RetargetLayoutCrossed" : "RetargetPropertyLost 'form." + row.Name + "'")}, " +
                    $"never dropped silently. Findings:\n{all}");
            }
        });
    }

    /// <summary>
    /// The rows that ARE the pixel⇄cell edge (plan scope call S4). Every OTHER single-target root row —
    /// slice 3's Properties-stored rows — falls to the RetargetPropertyLost 'form.X' arm of the sweep.
    /// </summary>
    private static bool IsLayoutEdge(FormPropertyDef row) => row.Name is "ClientSize" or "Cols" or "Rows" or "Gap";

    [Test]
    public void EveryFormRootRow_CrossesOrIsNamed_ExercisesBothArmsItHasRowsFor()
    {
        // Guards the sweep above from passing by absence: today it must see at least one row that crosses
        // (Text) and at least one layout-edge row in each direction.
        var rows = FormControlCatalog.FormRoot.Properties;

        Assert.Multiple(() =>
        {
            Assert.That(rows.Any(r => FormRootValues.Applies(r, FormTarget.WinForms, null) &&
                                      FormRootValues.Applies(r, FormTarget.Web, FormLayoutKind.Grid)), Is.True);
            Assert.That(rows.Any(r => IsLayoutEdge(r) && FormRootValues.Applies(r, FormTarget.WinForms, null) &&
                                      !FormRootValues.Applies(r, FormTarget.Web, FormLayoutKind.Grid)), Is.True);
            Assert.That(rows.Any(r => IsLayoutEdge(r) && FormRootValues.Applies(r, FormTarget.Web, FormLayoutKind.Grid) &&
                                      !FormRootValues.Applies(r, FormTarget.WinForms, null)), Is.True);
            // The RetargetPropertyLost arm (Task 11): a row that is not the layout edge and does not cross.
            Assert.That(rows.Any(r => !IsLayoutEdge(r) && FormRootValues.Applies(r, FormTarget.Web, FormLayoutKind.Canvas) &&
                                      !FormRootValues.Applies(r, FormTarget.WinForms, null)), Is.True);
            // …and a Canvas row that crosses EXACTLY (ClientSize), so the Canvas case exercises both arms.
            Assert.That(rows.Any(r => IsLayoutEdge(r) && FormRootValues.Applies(r, FormTarget.Web, FormLayoutKind.Canvas) &&
                                      FormRootValues.Applies(r, FormTarget.WinForms, null)), Is.True);
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

    /// <summary>
    /// Slice 3: the sweep above now has a broad RetargetPropertyLost arm (every WinForms-only Form row), and this pins
    /// the crossing arm for a Properties-stored row that exists on both targets.
    /// </summary>
    [Test]
    public void AFormRowOnBothTargets_Crosses_AndAWinFormsOnlyOne_IsNamed()
    {
        var win = new FormDocument { Target = FormTarget.WinForms, Name = "Login" };
        win.Properties["BackColor"] = "Red";
        win.Properties["FormBorderStyle"] = "FixedDialog";

        var result = FormRetarget.Convert(win, FormTarget.Web);

        Assert.Multiple(() =>
        {
            Assert.That(result.Document.Properties["BackColor"], Is.EqualTo("Red"));
            Assert.That(result.Document.Properties, Does.Not.ContainKey("FormBorderStyle"));
            Assert.That(result.Diagnostics.Any(d => d.Code == DesignCodes.RetargetPropertyLost &&
                                                    d.Message.Contains("'form.FormBorderStyle'") &&
                                                    d.Message.Contains("FixedDialog")), Is.True);
        });
    }

    /// <summary>
    /// The control rule, at the root: a value the destination REFUSES (a system colour with no CSS equivalent) crosses
    /// PRESERVED — the user's text, opened Degraded on the other side — and is NAMED with the catalog's own reason.
    /// </summary>
    [Test]
    public void AFormValueTheDestinationRefuses_CrossesPreserved_AndIsNamed()
    {
        var win = new FormDocument { Target = FormTarget.WinForms, Name = "Login" };
        win.Properties["BackColor"] = "ActiveCaption";
        var row = FormControlCatalog.FormRoot.Property("BackColor")!;

        var result = FormRetarget.Convert(win, FormTarget.Web);

        Assert.Multiple(() =>
        {
            Assert.That(result.Document.Properties["BackColor"], Is.EqualTo("ActiveCaption"));
            Assert.That(result.Diagnostics.Single(d => d.Code == DesignCodes.RetargetPropertyLost).Message,
                Does.Contain("'form.BackColor'").And.Contain(row.DescribeRefusal("ActiveCaption", FormTarget.Web)));
        });
    }

    /// <summary>
    /// Backlog (4): a root value ALREADY Degraded on the source (refused on both targets) lost nothing in the retarget — it
    /// crosses preserved and is NOT named (its Degraded row on each side already says so), exactly as a control's does.
    /// </summary>
    [Test]
    public void AFormValueDegradedOnBothTargets_CrossesPreserved_Unnamed()
    {
        var win = new FormDocument { Target = FormTarget.WinForms, Name = "Login" };
        win.Properties["BackColor"] = "12345";

        var result = FormRetarget.Convert(win, FormTarget.Web);

        Assert.Multiple(() =>
        {
            Assert.That(result.Document.Properties["BackColor"], Is.EqualTo("12345"));
            Assert.That(result.Diagnostics.Where(d => d.Code == DesignCodes.RetargetPropertyLost), Is.Empty);
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
            // ⛔ The exact branch: ClientSize targets the web now, but a WinForms → web retarget produces a
            // GRID page, where the row does not exist — asked through FormRootValues.Applies with the
            // destination's layout, never AppliesTo(Web) alone (which would say "carried as the web
            // form's value").
            Assert.That(result.Diagnostics.Any(d => d.Code == DesignCodes.RetargetPropertyLost &&
                                                    d.Message.Contains("12px") &&
                                                    d.Message.Contains("and does not exist on a web one, so it was dropped.")),
                Is.True, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        });
    }
}
