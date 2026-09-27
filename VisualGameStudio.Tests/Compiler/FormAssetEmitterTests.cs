using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 12: the build-time markup/CSS emitter (D6, D7).
///
/// <para>⛔ <b>Exit 0 is not a gate when the deliverable is files.</b> These assert the files exist,
/// carry the expected ids, and contain no unlowered BasicLang and no unsubstituted placeholders —
/// the <c>AssertSiteWasWritten</c> discipline. A test that only checks the emitter returned without
/// throwing proves nothing about what landed on disk.</para>
/// </summary>
[TestFixture]
public class FormAssetEmitterTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-assets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown() { try { Directory.Delete(_dir, true); } catch { } }

    private static FormDocument LoginForm()
    {
        var form = new FormDocument { Target = FormTarget.Web, Name = "LoginForm" };
        form.Layout = new FormLayout
        {
            Kind = FormLayoutKind.Grid, Cols = "120px,1fr", Rows = "auto,auto", Gap = "8px"
        };

        var label = new FormControl { Kind = "Label", Id = "lblUser", TabIndex = 0,
            Geometry = new GridGeometry { Col = 0, Row = 0 } };
        label.Properties["Text"] = "User";

        var box = new FormControl { Kind = "TextBox", Id = "txtUser", TabIndex = 1,
            Geometry = new GridGeometry { Col = 1, Row = 0 } };

        var button = new FormControl { Kind = "Button", Id = "btnLogin", TabIndex = 2,
            Geometry = new GridGeometry { Col = 1, Row = 1 } };
        button.Properties["Text"] = "Sign in";
        button.Binds.Add(new FormBind { Event = "click", Handler = "btnLogin_Click" });

        form.Controls.Add(label);
        form.Controls.Add(box);
        form.Controls.Add(button);
        form.Literal = """<p class="hint">Use your work account.</p>""";
        return form;
    }

    /// <summary>The file-deliverable gate, modelled on TemplateBuildSweepTests.AssertSiteWasWritten.</summary>
    private void AssertPageWasWritten(string formName)
    {
        var html = Path.Combine(_dir, formName + ".html");
        var css = Path.Combine(_dir, formName + ".css");

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(html), Is.True, $"{formName}.html was not written");
            Assert.That(File.Exists(css), Is.True, $"{formName}.css was not written");
        });

        var markup = File.ReadAllText(html);
        Assert.Multiple(() =>
        {
            Assert.That(markup, Does.Not.Contain("End Sub"), "BasicLang text leaked into the page");
            Assert.That(markup, Does.Not.Contain("{{"), "an unsubstituted placeholder reached the page");
            Assert.That(markup, Does.Not.Contain("AddressOf"), "wiring code leaked into the markup");
            Assert.That(markup, Does.StartWith("<!DOCTYPE html>"));
        });
    }

    // ==================================================================
    // What lands on disk
    // ==================================================================

    [Test]
    public void Emit_WritesAPageAndAStylesheetPerForm()
    {
        var written = FormAssetEmitter.Emit(_dir, "Site.js", new[] { LoginForm() });

        AssertPageWasWritten("LoginForm");
        Assert.That(written.Select(Path.GetFileName),
            Is.EquivalentTo(new[] { "LoginForm.html", "LoginForm.css" }));
    }

    [Test]
    public void Emit_WritesIntoTheDirectoryItWasHanded_AndNeverComputesOne()
    {
        // The IDE uses bin\Debug and the CLI bin\Debug\<tfm>. Reusing the JS emitter's own output
        // directory is what makes them agree; computing a third path here is the bug this prevents.
        var nested = Path.Combine(_dir, "bin", "Debug", "net8.0");
        Directory.CreateDirectory(nested);

        FormAssetEmitter.Emit(nested, "Site.js", new[] { LoginForm() });

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(nested, "LoginForm.html")), Is.True);
            Assert.That(Directory.GetFiles(_dir, "*.html"), Is.Empty,
                "nothing may be written beside the sources");
        });
    }

    [Test]
    public void Emit_AlwaysOverwritesAFormPage()
    {
        // Form pages are generated files, not a harness: every form needs a starting point on every
        // build. index.html and package.json remain the only never-overwrite outputs.
        File.WriteAllText(Path.Combine(_dir, "LoginForm.html"), "stale");

        FormAssetEmitter.Emit(_dir, "Site.js", new[] { LoginForm() });

        Assert.That(File.ReadAllText(Path.Combine(_dir, "LoginForm.html")), Does.Not.Contain("stale"));
    }

    [Test]
    public void Emit_DoesNotTouchIndexHtmlOrPackageJson()
    {
        File.WriteAllText(Path.Combine(_dir, "index.html"), "<html>the user's own harness</html>");
        File.WriteAllText(Path.Combine(_dir, "package.json"), "{ \"name\": \"mine\" }");

        FormAssetEmitter.Emit(_dir, "Site.js", new[] { LoginForm() });

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(Path.Combine(_dir, "index.html")),
                Is.EqualTo("<html>the user's own harness</html>"), "index.html must be byte-unchanged");
            Assert.That(File.ReadAllText(Path.Combine(_dir, "package.json")),
                Is.EqualTo("{ \"name\": \"mine\" }"));
        });
    }

    [Test]
    public void Emit_SkipsAWinFormsDocument()
    {
        // A .blform is a desktop window; it has no page. A mixed project is normal.
        var desktop = new FormDocument { Target = FormTarget.WinForms, Name = "MainForm" };

        var written = FormAssetEmitter.Emit(_dir, "Site.js", new[] { desktop });

        Assert.That(written, Is.Empty);
        Assert.That(Directory.GetFiles(_dir), Is.Empty);
    }

    // ==================================================================
    // The markup
    // ==================================================================

    [Test]
    public void Html_CarriesStableIds_AndNamesItsFormOnTheBody()
    {
        var html = FormAssetEmitter.Html(LoginForm(), "Site.js");

        Assert.Multiple(() =>
        {
            // The ids ARE the document's control ids — that is what lets the generated
            // InitializeComponent find each element with getElementById.
            Assert.That(html, Does.Contain("""id="lblUser" """.TrimEnd()));
            Assert.That(html, Does.Contain("""id="txtUser" """.TrimEnd()));
            Assert.That(html, Does.Contain("""id="btnLogin" """.TrimEnd()));
            Assert.That(html, Does.Contain("""<body data-form="LoginForm">"""),
                "Main() dispatches on this attribute");
            Assert.That(html, Does.Contain("""<script type="module" src="Site.js">"""));
            Assert.That(html, Does.Contain("""<link rel="stylesheet" href="LoginForm.css">"""));
        });
    }

    [Test]
    public void Html_UsesTheCatalogsTagAndInputType()
    {
        var html = FormAssetEmitter.Html(LoginForm(), "Site.js");

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("<label id=\"lblUser\""));
            Assert.That(html, Does.Contain("<input id=\"txtUser\"").And.Contain("type=\"text\""));
            Assert.That(html, Does.Contain("<button id=\"btnLogin\""));
        });
    }

    [Test]
    public void Html_PutsTextInTheRightPlaceForEachTag()
    {
        var html = FormAssetEmitter.Html(LoginForm(), "Site.js");

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain(">Sign in</button>"), "a button carries its text as content");
            Assert.That(html, Does.Contain(">User</label>"));
            Assert.That(html, Does.Not.Contain("</input>"), "input is a void element");
        });
    }

    [Test]
    public void Html_PassesTheLiteralThroughUntouched()
    {
        // The runat="server" inversion (D9): it is markup the user wrote to BE markup.
        var html = FormAssetEmitter.Html(LoginForm(), "Site.js");

        Assert.That(html, Does.Contain("""<p class="hint">Use your work account.</p>"""),
            "the literal must not be escaped — escaping it would render the tags as text");
    }

    [Test]
    public void Html_EscapesUserTextButNotTheLiteral()
    {
        var form = new FormDocument { Target = FormTarget.Web, Name = "Esc" };
        var label = new FormControl { Kind = "Label", Id = "lbl", TabIndex = 0 };
        label.Properties["Text"] = "a < b & c";
        form.Controls.Add(label);

        var html = FormAssetEmitter.Html(form, "Site.js");

        Assert.That(html, Does.Contain("a &lt; b &amp; c"),
            "user text is text — an unescaped '<' would silently start a tag");
    }

    [Test]
    public void Html_EscapesAQuoteInAnAttributeValue()
    {
        // An unescaped quote closes the attribute early and makes the page unparseable.
        var form = new FormDocument { Target = FormTarget.Web, Name = "Q" };
        var box = new FormControl { Kind = "TextBox", Id = "txt", TabIndex = 0 };
        box.Properties["Text"] = """say "hi" """.TrimEnd();
        form.Controls.Add(box);

        var html = FormAssetEmitter.Html(form, "Site.js");

        Assert.That(html, Does.Contain("&quot;hi&quot;"));
    }

    [Test]
    public void Html_LeavesAVisibleCommentForAControlWithNoWebCatalogRow()
    {
        // Silently absent from the page would be indistinguishable from a layout bug.
        var form = new FormDocument { Target = FormTarget.Web, Name = "Gap" };
        form.Controls.Add(new FormControl { Kind = "Mystery", Id = "odd", TabIndex = 0 });

        var html = FormAssetEmitter.Html(form, "Site.js");

        Assert.That(html, Does.Contain("no web catalog row"));
    }

    // ==================================================================
    // The stylesheet
    // ==================================================================

    [Test]
    public void Css_TurnsTheDocumentsTrackListIntoRealGridTemplates()
    {
        // The document stores "120px,1fr" because it is one XML attribute; CSS wants spaces.
        var css = FormAssetEmitter.Css(LoginForm());

        Assert.Multiple(() =>
        {
            Assert.That(css, Does.Contain("display: grid;"));
            Assert.That(css, Does.Contain("grid-template-columns: 120px 1fr;"));
            Assert.That(css, Does.Contain("grid-template-rows: auto auto;"));
            Assert.That(css, Does.Contain("gap: 8px;"));
        });
    }

    [Test]
    public void Css_ConvertsZeroBasedCellsToOneBasedGridLines()
    {
        // ⚠ CSS grid lines are 1-based; the document's Col/Row are 0-based. Off by one puts every
        // control one cell down and to the right — which looks like a layout bug and is an indexing
        // one.
        var css = FormAssetEmitter.Css(LoginForm());

        Assert.Multiple(() =>
        {
            Assert.That(css, Does.Contain("#lblUser { grid-column: 1; grid-row: 1; }"));
            Assert.That(css, Does.Contain("#btnLogin { grid-column: 2; grid-row: 2; }"));
        });
    }

    [Test]
    public void Css_EmitsFlexForAFlowLayout()
    {
        var form = LoginForm();
        form.Layout = new FormLayout { Kind = FormLayoutKind.Flow, Dir = "Vertical", Gap = "4px" };

        var css = FormAssetEmitter.Css(form);

        Assert.Multiple(() =>
        {
            Assert.That(css, Does.Contain("display: flex;"));
            Assert.That(css, Does.Contain("flex-direction: column;"));
            Assert.That(css, Does.Not.Contain("grid-column"), "a flow layout has no grid cells");
        });
    }

    [Test]
    public void Css_HidesAnInvisibleControlRatherThanOmittingTheElement()
    {
        // The element must still EXIST for getElementById to find it, or the generated
        // InitializeComponent fails at run time on a control the user merely hid.
        var form = LoginForm();
        form.FindById("btnLogin")!.Properties["Visible"] = "false";

        Assert.Multiple(() =>
        {
            Assert.That(FormAssetEmitter.Css(form), Does.Contain("display: none"));
            Assert.That(FormAssetEmitter.Html(form, "Site.js"), Does.Contain("""id="btnLogin" """.TrimEnd()));
        });
    }

    // ==================================================================
    // Task 10 guard (spec 2026-09-27 §3 "Grid/Flow paths unchanged"): the Grid, Flow and layout-less pages are
    // BYTE-identical to the emitter before Task 10. Hashes captured at e1c3de72.
    // ==================================================================

    private static FormDocument FlowWithStrips()
    {
        var form = new FormDocument
        {
            Target = FormTarget.Web, Name = "FlowPage",
            Layout = new FormLayout { Kind = FormLayoutKind.Flow, Dir = "Horizontal", Gap = "4px" }
        };
        var menu = new FormControl { Kind = "MenuStrip", Id = "menuStrip1" };
        var file = new FormControl { Kind = "ToolStripMenuItem", Id = "fileItem" };
        file.Properties["Text"] = "&File";
        menu.Children.Add(file);
        form.Controls.Add(menu);

        var button = new FormControl { Kind = "Button", Id = "btn", TabIndex = 0 };
        button.Properties["Visible"] = "False";
        button.Properties["BackColor"] = "#FF112233";
        form.Controls.Add(button);

        form.Controls.Add(new FormControl { Kind = "StatusStrip", Id = "statusStrip1" });
        var tool = new FormControl { Kind = "ToolStrip", Id = "toolStrip1" };
        tool.Properties["Dock"] = "Bottom";
        form.Controls.Add(tool);
        return form;
    }

    private static FormDocument LayoutlessWithPanel()
    {
        var form = new FormDocument { Target = FormTarget.Web, Name = "Bare" };
        var panel = new FormControl
        {
            Kind = "Panel", Id = "pnl", TabIndex = 0, Geometry = new GridGeometry { Col = 1, Row = 2, ColSpan = 2 }
        };
        panel.Children.Add(new FormControl { Kind = "Label", Id = "lbl", TabIndex = 0, Geometry = new GridGeometry() });
        form.Controls.Add(panel);
        form.Controls.Add(new FormControl { Kind = "MenuStrip", Id = "menuStrip1" });
        return form;
    }

    // ⚠ Captured by running this test at the BASE (e1c3de72). Never re-capture after a red: diff against the base.
    private static readonly Dictionary<string, string> PreTask10Hashes = new()
    {
        ["GridLogin"] = "A7EA8AFD116895DA4190FF8E582454E333F4F4D06FB87F8570969F948BB292D1",
        ["FlowWithStrips"] = "43C59E8EC54CA8AC971B34AC887C89A2C7955A3F27684D967F505CF99802CEB7",
        ["LayoutlessWithPanel"] = "3E3874F015FDEDDE21CB3FD1F906B0D89FD249E55912BB02C04E9ABF43AD1AFA"
    };

    [TestCase("GridLogin")]
    [TestCase("FlowWithStrips")]
    [TestCase("LayoutlessWithPanel")]
    public void AGridOrFlowPage_IsByteIdenticalToThePreTask10Emitter(string fixture)
    {
        var form = fixture switch
        {
            "GridLogin" => LoginForm(),
            "FlowWithStrips" => FlowWithStrips(),
            _ => LayoutlessWithPanel()
        };

        var text = (FormAssetEmitter.Html(form, "App.js") + "\n/* CSS */\n" + FormAssetEmitter.Css(form))
            .Replace("\r\n", "\n");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

        Assert.That(hash, Is.EqualTo(PreTask10Hashes[fixture]),
            $"the {fixture} page changed. Task 10 must not touch a Grid/Flow page. The output was:\n{text}");
    }

    // ==================================================================
    // The Main() dispatch (D7)
    // ==================================================================

    [Test]
    public void DispatchSource_ReadsTheBodyAttributeInTwoSteps()
    {
        // ⛔⛔ Measured: `Dim s As String = doc.body.getAttribute("data-form")` fails with "Cannot
        // assign value of type 'Object' to variable of type 'String'" — chained access through a
        // declared Property loses the declared type, even though both members are declared.
        var source = FormAssetEmitter.DispatchSource(new[] { "LoginForm", "SignupForm" });

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("Dim b As Element = doc.body"));
            Assert.That(source, Does.Contain("Dim formName As String = b.getAttribute(\"data-form\")"));
            Assert.That(source, Does.Not.Contain("doc.body.getAttribute"),
                "the chained form does not type — this is the whole reason for the two-step shape");
        });
    }

    [Test]
    public void ASelectsItems_BecomeOptionChildren()
    {
        // ⛔⛔ The WinForms side emits Items.Add(...) per entry. Without the matching <option>s a
        // ComboBox rendered as an EMPTY dropdown on the web and a populated one on the desktop,
        // from the SAME document — a designer/runtime divergence across targets.
        var form = new FormDocument { Target = FormTarget.Web, Name = "F" };
        var combo = new FormControl { Kind = "ComboBox", Id = "cmb", TabIndex = 0 };
        combo.Properties["Items"] = "Alpha, Beta, Gamma";
        combo.Properties["Text"] = "Pick one";
        form.Controls.Add(combo);

        var html = FormAssetEmitter.Html(form, "App.js");

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("<option>Alpha</option>"));
            Assert.That(html, Does.Contain("<option>Beta</option>"));
            Assert.That(html, Does.Contain("<option>Gamma</option>"));
            // ⛔ A <select> takes <option> children and nothing else. Text was being written as a
            // bare text node inside it, which browsers drop or render as stray text.
            Assert.That(html, Does.Not.Contain(">Pick one<"));
            Assert.That(html, Does.Contain("""title="Pick one" """.TrimEnd()),
                "a select's Text labels it rather than filling it");
        });
    }

    [Test]
    public void ASelectsSelectedIndex_MarksThatOption()
    {
        // ⛔ The same cross-target divergence one property over. WinForms emits
        // `cmb.SelectedIndex = 1`; the page has no such property, so without marking the option the
        // desktop opened on Beta and the web opened on Alpha — from the same document.
        var form = new FormDocument { Target = FormTarget.Web, Name = "F" };
        var combo = new FormControl { Kind = "ComboBox", Id = "cmb", TabIndex = 0 };
        combo.Properties["Items"] = "Alpha, Beta, Gamma";
        combo.Properties["SelectedIndex"] = "1";
        form.Controls.Add(combo);

        var html = FormAssetEmitter.Html(form, "App.js");

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("<option>Alpha</option>"));
            Assert.That(html, Does.Contain("<option selected>Beta</option>"));
            Assert.That(html, Does.Contain("<option>Gamma</option>"));
        });
    }

    [Test]
    public void ASelectsSelectedIndexOutOfRange_MarksNothing()
    {
        // -1 is the catalog default (nothing chosen) and anything past the end is a document the
        // user can produce by shortening Items. Neither may mark an arbitrary option.
        var form = new FormDocument { Target = FormTarget.Web, Name = "F" };
        var combo = new FormControl { Kind = "ComboBox", Id = "cmb", TabIndex = 0 };
        combo.Properties["Items"] = "Alpha, Beta";
        combo.Properties["SelectedIndex"] = "7";
        form.Controls.Add(combo);

        Assert.That(FormAssetEmitter.Html(form, "App.js"), Does.Not.Contain("selected"));
    }

    /// <summary>
    /// ⛔ Both targets must agree on which SelectedIndex values are valid. WinForms judges these
    /// Degraded (<c>FormPropertyDef.TryParseInt</c>: ASCII space only, culture-free) and emits no
    /// assignment; a whitespace-tolerant <c>int.TryParse</c> on the web marked Beta anyway, so the
    /// same document opened on different entries per target.
    /// </summary>
    [TestCase("1\r\n")]
    [TestCase("\t1")]
    [TestCase("1\n")]
    public void ASelectsSelectedIndex_ThatWinFormsJudgesDegraded_MarksNothing(string raw)
    {
        var form = new FormDocument { Target = FormTarget.Web, Name = "F" };
        var combo = new FormControl { Kind = "ComboBox", Id = "cmb", TabIndex = 0 };
        combo.Properties["Items"] = "Alpha, Beta";
        combo.Properties["SelectedIndex"] = raw;
        form.Controls.Add(combo);

        Assert.Multiple(() =>
        {
            Assert.That(FormControlCatalog.Find("ComboBox")!.Property("SelectedIndex")!.Accepts(raw), Is.False,
                "precondition: the catalog calls this value Degraded");
            Assert.That(FormAssetEmitter.Html(form, "App.js"), Does.Not.Contain("selected"));
        });
    }

    private static string NumericHtml(string property, string raw)
    {
        var form = new FormDocument { Target = FormTarget.Web, Name = "F" };
        var numeric = new FormControl { Kind = "NumericUpDown", Id = "num", TabIndex = 0 };
        numeric.Properties[property] = raw;
        form.Controls.Add(numeric);
        return FormAssetEmitter.Html(form, "App.js");
    }

    /// <summary>
    /// ⛔ An Int row's HTML attribute (min/max/value/step) is re-emitted from the PARSED number, the
    /// same way WinForms emits it — never the document's text, whatever the parser tolerated around it.
    /// </summary>
    [Test]
    public void AnIntHtmlAttribute_IsEmittedFromTheParsedNumber()
    {
        Assert.Multiple(() =>
        {
            Assert.That(NumericHtml("Value", " 5 "), Does.Contain(" value=\"5\""));
            Assert.That(NumericHtml("Value", "+007"), Does.Contain(" value=\"7\""));
        });
    }

    /// <summary>
    /// ⛔ WinForms emits NOTHING for a Degraded Int (the catalog refuses it); the page must not carry
    /// it either, or the same document means a number on one target and nothing on the other.
    /// </summary>
    [TestCase("abc", TestName = "{m}(abc)")]
    [TestCase("5\r\n", TestName = "{m}(trailing CRLF)")]
    [TestCase("<MINUS>5", TestName = "{m}(U+2212)")]
    public void ADegradedIntHtmlAttribute_IsNotEmitted(string marked)
    {
        var raw = marked.Replace("<MINUS>", UnicodeMinusCulture.Minus);

        Assert.Multiple(() =>
        {
            Assert.That(FormControlCatalog.Find("NumericUpDown")!.Property("Value")!.Accepts(raw), Is.False,
                "precondition: the catalog calls this value Degraded");
            Assert.That(NumericHtml("Value", raw), Does.Not.Contain(" value="));
        });
    }

    [Test]
    [SetCulture("sv-SE")]
    public void ANegativeIntHtmlAttribute_UnderAUnicodeMinusCulture_UsesAnAsciiHyphen()
    {
        UnicodeMinusCulture.Require();

        var html = NumericHtml("Minimum", "-5");

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain(" min=\"-5\""));
            Assert.That(html, Does.Not.Contain(UnicodeMinusCulture.Minus));
        });
    }

    [Test]
    public void ASelectsSelectedIndex_WithAsciiSpaces_StillMarksThatOption()
    {
        // Space is what a person types beside a number; TryParseInt accepts it, so both targets do.
        var form = new FormDocument { Target = FormTarget.Web, Name = "F" };
        var combo = new FormControl { Kind = "ComboBox", Id = "cmb", TabIndex = 0 };
        combo.Properties["Items"] = "Alpha, Beta";
        combo.Properties["SelectedIndex"] = " 1 ";
        form.Controls.Add(combo);

        Assert.That(FormAssetEmitter.Html(form, "App.js"), Does.Contain("<option selected>Beta</option>"));
    }

    [Test]
    public void ASelectWithNoItems_EmitsNoOptions()
    {
        var form = new FormDocument { Target = FormTarget.Web, Name = "F" };
        form.Controls.Add(new FormControl { Kind = "ListBox", Id = "lst", TabIndex = 0 });

        Assert.That(FormAssetEmitter.Html(form, "App.js"), Does.Not.Contain("<option"));
    }

    [Test]
    public void DispatchSource_ConstructsTheForm_WithoutCallingInitializeComponentAgain()
    {
        // ⛔⛔ The scaffolded `Public Sub New()` already calls InitializeComponent. Calling it
        // again here ran the entire init body TWICE — so every addEventListener registered its
        // handler twice and a single click fired it twice. Nothing about the page looked wrong.
        var source = FormAssetEmitter.DispatchSource(new[] { "LoginForm" });

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("Dim f As New LoginForm()"));
            Assert.That(source, Does.Not.Contain("InitializeComponent"),
                "the constructor does it; a second call double-registers every handler");
        });
    }

    [Test]
    public void DispatchSource_BranchesOnEveryForm_UnderOneTopLevelName()
    {
        // One generated top-level name per project, fixed spelling, so --check can collision-check
        // it once. Per-control fields are class members and cannot collide across forms.
        var source = FormAssetEmitter.DispatchSource(new[] { "LoginForm", "SignupForm" });

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain($"Sub {FormAssetEmitter.DispatchSubName}()"));
            Assert.That(source, Does.Contain("""If formName = "LoginForm" Then"""));
            Assert.That(source, Does.Contain("""ElseIf formName = "SignupForm" Then"""));
            Assert.That(source, Does.Contain("End If"));
            // ⛔ Counted as two separate properties, not as occurrences of "Sub ". The closing
            // keyword is "End Sub" at the end of its line, with no trailing space, so a single
            // count of "Sub " never sees it — an assertion of 2 fails against output that is
            // exactly right, and one of 1 would pass just as well against output carrying a second
            // declaration and no terminator.
            Assert.That(source.Split("Sub ").Length - 1, Is.EqualTo(1),
                "exactly one Sub DECLARATION — no second top-level name to collide across forms");
            Assert.That(source.Split("End Sub").Length - 1, Is.EqualTo(1),
                "and it is terminated exactly once");
        });
    }

    [Test]
    public void DispatchSource_IsWellFormed_WithNoForms()
    {
        var source = FormAssetEmitter.DispatchSource(Array.Empty<string>());

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain($"Sub {FormAssetEmitter.DispatchSubName}()"));
            Assert.That(source, Does.Contain("End Sub"));
            Assert.That(source, Does.Not.Contain("End If"),
                "an If that was never opened must not be closed");
        });
    }

    // ==================================================================
    // Task 10 (spec 2026-09-27 §5) — the phone stretch flag is a CATALOG facet, never a Kind switch
    // ==================================================================

    [Test]
    public void StretchesWhenStacked_IsOnlyOnPositionedRowsTheWebHas()
    {
        var wrong = FormControlCatalog.All
            .Where(d => d.StretchesWhenStacked &&
                        (d.Place != FormPlace.Positioned || !d.SupportsTarget(FormTarget.Web)))
            .Select(d => d.Kind)
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(wrong, Is.Empty, "a phone never stacks a strip, an item, a tray component or a WinForms-only row");
            Assert.That(FormControlCatalog.Find("TextBox")!.StretchesWhenStacked, Is.True, "spec §5: inputs stretch");
            Assert.That(FormControlCatalog.Find("Button")!.StretchesWhenStacked, Is.False, "spec §5: small controls keep their size");
        });
    }

    // ==================================================================
    // Task 10 (spec 2026-09-27 §3, §4) — a Canvas page: the WinForms client area, in pixels
    // ==================================================================

    private static FormDocument CanvasPage(int? width = 640, int? height = 480, string? breakpoint = "600") => new()
    {
        Target = FormTarget.Web, Name = "Page", Width = width, Height = height,
        Layout = new FormLayout { Kind = FormLayoutKind.Canvas, MobileBreakpoint = breakpoint }
    };

    private static FormControl At(string kind, string id, int x, int y, int width, int height,
        string? anchor = null, string? dock = null, params FormControl[] children)
    {
        var control = new FormControl
        {
            Kind = kind, Id = id,
            Geometry = new PixelGeometry { X = x, Y = y, Width = width, Height = height, Anchor = anchor, Dock = dock }
        };
        control.Children.AddRange(children);
        return control;
    }

    private static FormControl StripOf(string kind, string id) => new() { Kind = kind, Id = id };

    private const string PositionedPrefix = "position: absolute; box-sizing: border-box; margin: 0; ";

    /// <summary>The declarations of <c>#id { … }</c> OUTSIDE the phone query, or null. Rules start at a line start.</summary>
    private static string? DesktopRule(string css, string id) =>
        RuleIn(css.Split("@media (width <")[0], "\n#" + id + " { ");

    /// <summary>The declarations of <c>#id { … }</c> INSIDE the phone query, or null.</summary>
    private static string? PhoneRule(string css, string id)
    {
        var parts = css.Split("@media (width <");
        return parts.Length < 2 ? null : RuleIn(parts[1], "\n  #" + id + " { ");
    }

    private static string? RuleIn(string text, string opener)
    {
        var at = text.IndexOf(opener, StringComparison.Ordinal);
        if (at < 0)
        {
            return null;
        }

        at += opener.Length;
        return text.Substring(at, text.IndexOf(" }", at, StringComparison.Ordinal) - at);
    }

    private static string Declarations(IEnumerable<(string Property, string Value)> declarations) =>
        string.Join("; ", declarations.Select(d => $"{d.Property}: {d.Value}"));

    [Test]
    public void ACanvasPage_PutsItsStripsInsideTheFormArea_InDocumentOrder()
    {
        // ⛔ Spec §3: the coordinate space is the WinForms CLIENT AREA, strips included — a control at Y=30 is 6px
        // below a 24px menu. On a Grid/Flow page the strips stay chrome outside the div (FormStripEmissionTests).
        var page = CanvasPage();
        page.Controls.Add(StripOf("StatusStrip", "statusStrip1"));
        page.Controls.Add(StripOf("MenuStrip", "menuStrip1"));
        page.Controls.Add(At("Button", "btn", 10, 40, 75, 23));
        page.Controls.Add(StripOf("ToolStrip", "toolStrip1"));

        var html = FormAssetEmitter.Html(page, "App.js");
        var open = html.IndexOf("<div class=\"vgs-form\">", StringComparison.Ordinal);
        var close = html.LastIndexOf("</div>", StringComparison.Ordinal);
        int Where(string id) => html.IndexOf($"id=\"{id}\"", StringComparison.Ordinal);
        var ids = new[] { "statusStrip1", "menuStrip1", "btn", "toolStrip1" };

        Assert.Multiple(() =>
        {
            foreach (var id in ids)
            {
                Assert.That(Where(id), Is.GreaterThan(open).And.LessThan(close), $"{id} is inside the form area");
            }

            Assert.That(ids.Select(Where), Is.Ordered,
                "DOCUMENT order (S12): absolutely positioned siblings paint later-on-top, WinForms' z-order");
        });
    }

    [Test]
    public void ACanvasPagesLiteral_FlowsInsideTheFormArea()
    {
        var page = CanvasPage();
        page.Literal = """<p class="hint">Use your work account.</p>""";

        var html = FormAssetEmitter.Html(page, "App.js");

        Assert.That(html.IndexOf("<p class=\"hint\">", StringComparison.Ordinal),
            Is.GreaterThan(html.IndexOf("<div class=\"vgs-form\">", StringComparison.Ordinal))
              .And.LessThan(html.LastIndexOf("</div>", StringComparison.Ordinal)));
    }

    [TestCase(640, 480, 640, 480)]
    [TestCase(null, null, 400, 300)]
    public void TheFormArea_FillsTheWindow_WithTheDesignSizeAsItsMinimum(int? width, int? height, int w, int h)
    {
        var page = CanvasPage(width, height);
        Assert.That(page.DesignSize, Is.EqualTo((w, h)), "precondition: DesignSize is the one form size");

        var css = FormAssetEmitter.Css(page);

        Assert.Multiple(() =>
        {
            Assert.That(css, Does.Contain("body { margin: 0; }"));
            Assert.That(css, Does.Contain(
                ".vgs-form {\n  position: relative;\n  width: 100%;\n" +
                $"  min-width: {w}px;\n  height: 100vh;\n  min-height: {h}px;\n  box-sizing: border-box;\n}}"));
            Assert.That(css, Does.Contain(".vgs-form [hidden] { display: none !important; }"),
                "B7: a phone's display:flex must not un-hide a control user code hid");
        });
    }

    [TestCase(null)]
    [TestCase("Top,Left")]
    [TestCase("Right")]
    [TestCase("Left,Right")]
    [TestCase("Bottom")]
    [TestCase("Top,Bottom")]
    [TestCase("None")]
    [TestCase("Top,Bottom,Left,Right")]
    public void APositionedControl_IsWhereFormAnchorCssPutsIt(string? anchor)
    {
        var page = CanvasPage();
        var button = At("Button", "btn", 500, 400, 100, 30, anchor);
        page.Controls.Add(button);

        Assert.That(DesktopRule(FormAssetEmitter.Css(page), "btn"), Does.StartWith(
            PositionedPrefix + Declarations(FormAnchorCss.Positioned((PixelGeometry)button.Geometry!, 640, 480))));
    }

    [Test]
    public void ARightAnchoredControl_KeepsItsDistanceFromTheRightEdge()
    {
        var page = CanvasPage();
        page.Controls.Add(At("Button", "btn", 500, 400, 100, 30, "Top,Right"));

        Assert.That(DesktopRule(FormAssetEmitter.Css(page), "btn"),
            Does.Contain("right: 40px; width: 100px; top: 400px; height: 30px"), "non-vacuity: 640 - 500 - 100");
    }

    [Test]
    public void ANestedControl_IsAnchoredAgainstItsContainersClientSize_FromFormDockLayout()
    {
        // ⛔ C2: a container's size is FormDockLayoutResult's — a docked Panel's RESOLVED bounds, never its stale
        // stored 10x10, and never the form's.
        var inner = At("Button", "inner", 200, 10, 50, 20, "Top,Right");
        var fill = At("Panel", "fill", 7, 7, 10, 10, dock: "Fill", children: inner);
        var boxed = At("Button", "boxed", 200, 10, 50, 20, "Top,Right");
        var panel = At("Panel", "pnl", 20, 300, 300, 150, children: boxed);
        var page = CanvasPage();
        page.Controls.Add(StripOf("MenuStrip", "menuStrip1"));
        page.Controls.Add(fill);
        page.Controls.Add(panel);

        var runtime = FormDockLayout.Resolve(page, FormDockMode.Runtime);
        var client = runtime.ClientSizeOf(fill);
        var css = FormAssetEmitter.Css(page);

        Assert.Multiple(() =>
        {
            Assert.That(client, Is.EqualTo((640, 456)), "precondition: a Fill under a 24px menu");
            Assert.That(DesktopRule(css, "inner"), Does.Contain(
                Declarations(FormAnchorCss.Positioned((PixelGeometry)inner.Geometry!, client.Width, client.Height))));
            Assert.That(DesktopRule(css, "inner"), Does.Contain("right: 390px"), "640 - 200 - 50");
            Assert.That(DesktopRule(css, "boxed"), Does.Contain(
                Declarations(FormAnchorCss.Positioned((PixelGeometry)boxed.Geometry!, 300, 150))),
                "an undocked Panel's client size is its stored size");
        });
    }

    [Test]
    public void DockedThings_AreWhereFormDockLayoutPutsThem()
    {
        var page = CanvasPage();
        page.Controls.Add(At("Panel", "pnlTop", 300, 300, 10, 40, dock: "Top")); // stored X/Y/Width are stale by design
        page.Controls.Add(StripOf("MenuStrip", "menuStrip1"));
        // ⚠ The status strip BEFORE the Fill: a Fill takes what is left WHEN IT DOCKS, so a later Bottom would overlap it.
        page.Controls.Add(StripOf("StatusStrip", "statusStrip1"));
        page.Controls.Add(At("Panel", "fill", 0, 0, 1, 1, dock: "Fill"));

        var css = FormAssetEmitter.Css(page);
        var runtime = FormDockLayout.Resolve(page, FormDockMode.Runtime);

        Assert.Multiple(() =>
        {
            foreach (var docked in runtime.All)
            {
                Assert.That(DesktopRule(css, docked.Control.Id),
                    Does.StartWith(PositionedPrefix + Declarations(FormAnchorCss.Docked(docked))), docked.Control.Id);
            }

            Assert.That(DesktopRule(css, "menuStrip1"), Does.Contain("top: 40px; height: 24px"),
                "non-vacuity: under the Dock=Top panel that precedes it (spec §4)");
            Assert.That(DesktopRule(css, "fill"), Does.Contain("left: 0px; right: 0px; top: 64px; bottom: 22px"));
        });
    }

    [Test]
    public void EveryStrip_IsItsRowsDefaultHeight()
    {
        var rows = FormControlCatalog.All
            .Where(d => d.Place == FormPlace.Docked && d.SupportsTarget(FormTarget.Web))
            .ToList();
        Assert.That(rows, Is.Not.Empty);

        Assert.Multiple(() =>
        {
            foreach (var row in rows)
            {
                var page = CanvasPage();
                FormCatalogShapes.Canonical(page, row, "strip");
                Assert.That(DesktopRule(FormAssetEmitter.Css(page), "strip"),
                    Does.Contain($"height: {row.DefaultHeight}px"), row.Kind);
            }
        });
    }

    [Test]
    public void AHiddenDockedPanel_GivesUpItsEdge_InThePagesFirstState()
    {
        // ⛔ Owner decision 2026-09-27: the page OPENS in FormDockMode.Runtime (a hidden control takes no space); the
        // reflow script (Part 4) re-docks on a change. The hidden panel keeps its DESIGNER insets (B3).
        var page = CanvasPage();
        var hidden = At("Panel", "pnlTop", 0, 0, 10, 40, dock: "Top");
        hidden.Properties["Visible"] = "False";
        page.Controls.Add(hidden);
        page.Controls.Add(StripOf("MenuStrip", "menuStrip1"));

        var css = FormAssetEmitter.Css(page);
        Assert.That(FormDockLayout.Resolve(page, FormDockMode.Designer).TryGet(hidden, out var designed), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(DesktopRule(css, "menuStrip1"), Does.Contain("top: 0px; height: 24px"),
                "the menu closes the gap while the panel is hidden");
            Assert.That(DesktopRule(css, "pnlTop"),
                Does.Contain(Declarations(FormAnchorCss.Docked(designed))).And.Contain("display: none"));
        });
    }

    [Test]
    public void AChildOfAHiddenPanel_IsStillPositioned()
    {
        // B3: Runtime does not walk a hidden container, and ClientSizeOf would THROW for it.
        var child = At("Button", "child", 10, 10, 75, 23, "Top,Right");
        var hidden = At("Panel", "pnl", 20, 20, 300, 200, children: child);
        hidden.Properties["Visible"] = "False";
        var page = CanvasPage();
        page.Controls.Add(hidden);

        Assert.That(DesktopRule(FormAssetEmitter.Css(page), "child"), Does.StartWith(
            PositionedPrefix + Declarations(FormAnchorCss.Positioned((PixelGeometry)child.Geometry!, 300, 200))));
    }

    [Test]
    public void ANonPositiveSize_WritesNoSize()
    {
        // Spec §3 / C4: content-sized on the page, invisible on WinForms — an accepted divergence.
        var page = CanvasPage();
        page.Controls.Add(At("Label", "lbl", 10, 12, 0, -5));

        var rule = DesktopRule(FormAssetEmitter.Css(page), "lbl");

        Assert.Multiple(() =>
        {
            Assert.That(rule, Does.Not.Contain("width:"));
            Assert.That(rule, Does.Not.Contain("height:"));
            Assert.That(rule, Does.Contain("left: 10px; top: 12px"));
        });
    }

    [Test]
    public void AnItem_AndATrayComponent_AreNeverPositioned()
    {
        var page = CanvasPage();
        var menu = StripOf("MenuStrip", "menuStrip1");
        var item = new FormControl { Kind = "ToolStripMenuItem", Id = "fileItem" };
        item.Properties["Visible"] = "False"; // so the item HAS a rule to inspect
        menu.Children.Add(item);
        page.Controls.Add(menu);
        page.Components.Add(new FormControl { Kind = "Timer", Id = "tmr" });

        var css = FormAssetEmitter.Css(page);

        Assert.Multiple(() =>
        {
            Assert.That(DesktopRule(css, "fileItem"), Is.EqualTo("display: none;"), "an item keeps only its catalog CSS");
            Assert.That(css, Does.Not.Contain("#tmr"), "a tray component has no element");
        });
    }

    [Test]
    public void TheCatalogsCss_IsStillEmitted_AfterTheGeometry()
    {
        var page = CanvasPage();
        var button = At("Button", "btn", 10, 10, 75, 23);
        button.Properties["BackColor"] = "#FF112233";
        page.Controls.Add(button);
        var expected = FormCss.Declaration(FormControlCatalog.Find("Button")!.Property("BackColor")!, "#FF112233")!.Value;

        Assert.That(DesktopRule(FormAssetEmitter.Css(page), "btn"),
            Does.EndWith($"height: 23px; {expected.Property}: {expected.Value};"));
    }

    [Test]
    public void AMenuStripsDropdowns_AreLiftedAboveTheControlsAfterIt()
    {
        // ⛔ B4: bands are in DOCUMENT order (S12), so a control after the strip paints over its open dropdown.
        // WinForms opens a dropdown as its own window. Only the row's children-wrapper lists are lifted: the bar's
        // own list is static (z-index does nothing there), every nested one is absolutely positioned.
        var page = CanvasPage();
        page.Controls.Add(StripOf("MenuStrip", "menuStrip1"));
        page.Controls.Add(StripOf("ToolStrip", "toolStrip1"));

        var css = FormAssetEmitter.Css(page);

        Assert.Multiple(() =>
        {
            Assert.That(css, Does.Contain("\n#menuStrip1 ul { z-index: 1; }\n"));
            Assert.That(css, Does.Not.Contain("#toolStrip1 ul"), "a ToolStrip's row declares no children wrapper");
            Assert.That(DesktopRule(css, "menuStrip1"), Does.Not.Contain("z-index"), "the band itself stays in document order");
        });
    }
}
