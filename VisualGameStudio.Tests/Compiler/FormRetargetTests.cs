using BasicLang.Forms;
using BasicLang.Forms.Serialization;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 21 — one form, two targets: <c>FormRetarget</c> converts a document between
/// <c>.blform</c> and <c>.blwebform</c>, mapping the shared grammar losslessly and reporting every
/// property that cannot cross as a <c>BL8xxx</c> finding rather than dropping it silently.
///
/// <para>⛔ The hard edge is absolute pixels ⇄ grid cells. Nothing here claims that edge is
/// lossless — the claim is that the loss is EXPLICIT: one finding per control saying what it had
/// and where it landed, and one for the document saying what the window (or the page layout) lost.
/// A retarget that quietly stacked every control at (0,0) would pass a "does it convert" test and
/// would be exactly the silent loss the task forbids.</para>
/// </summary>
[TestFixture]
public class FormRetargetTests
{
    /// <summary>The spec's WinForms login form, with a bind on each of the two interactive controls.</summary>
    private const string WinFormsLogin = """
        <Form Name="LoginForm" Version="1" Width="400" Height="300" Text="Sign in">
          <Controls>
            <Label   Id="lblUser"  Text="User"    X="20" Y="20" Width="60"  Height="23" TabIndex="0"/>
            <TextBox Id="txtUser"  X="90" Y="20"  Width="200" Height="23" Anchor="Left,Top,Right" TabIndex="1">
              <Bind Event="TextChanged" Handler="txtUser_TextChanged"/>
            </TextBox>
            <Button  Id="btnLogin" Text="Sign in" X="190" Y="60" Width="100" Height="30" TabIndex="2">
              <Bind Event="Click" Handler="btnLogin_Click"/>
            </Button>
          </Controls>
          <Components/>
          <Resources/>
        </Form>
        """;

    private static FormDocument Read(string xml, string fileName)
    {
        var file = FormDocumentReader.Read(Path.Combine("C:", "forms", fileName), xml);
        Assert.That(file.IsRefused, Is.False,
            "fixture refused: " + string.Join("; ", file.Diagnostics.Select(d => d.Format())));
        return file.Model;
    }

    private static FormDocument WinForms(string xml) => Read(xml, "LoginForm.blform");

    private static FormDocument Web(string xml) => Read(xml, "LoginForm.blwebform");

    private static IEnumerable<DesignDiagnostic> Of(FormRetargetResult result, string code) =>
        result.Diagnostics.Where(d => d.Code == code);

    // ==================================================================
    // The contract
    // ==================================================================

    [Test]
    public void Convert_ToTheSourcesOwnTarget_IsACallerError()
    {
        var source = WinForms(WinFormsLogin);

        Assert.Throws<ArgumentException>(() => FormRetarget.Convert(source, FormTarget.WinForms));
    }

    [Test]
    public void Convert_LeavesTheSourceUntouched()
    {
        var source = WinForms(WinFormsLogin);
        var before = FormDocumentWriter.Create(source);

        FormRetarget.Convert(source, FormTarget.Web);

        Assert.That(FormDocumentWriter.Create(source), Is.EqualTo(before),
            "a retarget must produce a NEW document; the designer still owns the source");
    }

    // ==================================================================
    // WinForms → Web: the shared subset crosses, the pixels do not
    // ==================================================================

    [Test]
    public void ToWeb_KeepsTheSharedSubset()
    {
        var result = FormRetarget.Convert(WinForms(WinFormsLogin), FormTarget.Web);
        var doc = result.Document;

        Assert.Multiple(() =>
        {
            Assert.That(doc.Target, Is.EqualTo(FormTarget.Web));
            Assert.That(doc.RootElementName, Is.EqualTo("WebForm"));
            Assert.That(doc.Name, Is.EqualTo("LoginForm"));
            Assert.That(doc.Version, Is.EqualTo(1));
            Assert.That(doc.Controls.Select(c => c.Id), Is.EqualTo(new[] { "lblUser", "txtUser", "btnLogin" }),
                "document order is z-order on both targets and must survive");
            Assert.That(doc.Controls.Select(c => c.Kind), Is.EqualTo(new[] { "Label", "TextBox", "Button" }));
            Assert.That(doc.Controls.Select(c => c.TabIndex), Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(doc.FindById("btnLogin")!.Properties["Text"], Is.EqualTo("Sign in"));
            Assert.That(doc.FindById("lblUser")!.Properties["Text"], Is.EqualTo("User"));
            Assert.That(doc.FindById("btnLogin")!.Binds.Single().Handler, Is.EqualTo("btnLogin_Click"),
                "a handler name is the user's own Sub and crosses verbatim");
            Assert.That(doc.Width, Is.Null, "a page has no window size");
            Assert.That(doc.Text, Is.EqualTo("Sign in"), "Text is one vocabulary on both targets (D2): it becomes the page title");
        });
    }

    [Test]
    public void ToWeb_DerivesOneRowPerDistinctY_AndOneColumnPerDistinctX()
    {
        // X: 20, 90, 190 → columns 0, 1, 2.  Y: 20, 60 → rows 0, 1.
        // Deterministic and explainable: aligned controls share a track. No tolerance, no guessing.
        var doc = FormRetarget.Convert(WinForms(WinFormsLogin), FormTarget.Web).Document;

        GridGeometry Cell(string id) => (GridGeometry)doc.FindById(id)!.Geometry!;

        Assert.Multiple(() =>
        {
            Assert.That((Cell("lblUser").Col, Cell("lblUser").Row), Is.EqualTo((0, 0)));
            Assert.That((Cell("txtUser").Col, Cell("txtUser").Row), Is.EqualTo((1, 0)));
            Assert.That((Cell("btnLogin").Col, Cell("btnLogin").Row), Is.EqualTo((2, 1)));
            Assert.That(Cell("txtUser").ColSpan, Is.EqualTo(1), "a span cannot be inferred from pixels");
        });
    }

    [Test]
    public void ToWeb_TheDerivedLayout_IsAGridSizedToTheCells()
    {
        var doc = FormRetarget.Convert(WinForms(WinFormsLogin), FormTarget.Web).Document;

        Assert.That(doc.Layout, Is.Not.Null, "a web document needs a <Layout> or the emitter guesses");
        Assert.Multiple(() =>
        {
            Assert.That(doc.Layout!.Kind, Is.EqualTo(FormLayoutKind.Grid));
            Assert.That(doc.Layout.Cols, Is.EqualTo("auto,auto,auto"), "one track per distinct X");
            Assert.That(doc.Layout.Rows, Is.EqualTo("auto,auto"), "one track per distinct Y");
            Assert.That(doc.Layout.Gap, Is.EqualTo("8px"), "the scaffolder's own gap");
        });
    }

    [Test]
    public void ToWeb_ReportsEveryControlsPixelGeometry_AsCrossedLoss()
    {
        var result = FormRetarget.Convert(WinForms(WinFormsLogin), FormTarget.Web);
        var crossed = Of(result, DesignCodes.RetargetLayoutCrossed).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(crossed.All(d => d.IsWarning), Is.True,
                "loss at the hard edge is a WARNING: the retarget succeeded, and the user reviews it");

            var txt = crossed.SingleOrDefault(d => d.Message.Contains("'txtUser'"));
            Assert.That(txt, Is.Not.Null, "one finding per control, naming it");
            Assert.That(txt!.Message, Does.Contain("X=90").And.Contain("Y=20")
                .And.Contain("Width=200").And.Contain("Height=23"),
                "the finding must say what the control HAD");
            Assert.That(txt.Message, Does.Contain("Anchor=Left,Top,Right"),
                "an anchor has no grid meaning and must be named as lost");
            Assert.That(txt.Message, Does.Contain("Col=1").And.Contain("Row=0"),
                "…and where it LANDED, so the user can review the cell");

            var window = crossed.SingleOrDefault(d => d.Message.Contains("400") && d.Message.Contains("300"));
            Assert.That(window, Is.Not.Null, "the window's size is lost once, for the document");
            Assert.That(window!.Message, Does.Not.Contain("Sign in"), "the caption now CROSSES (D2); it is not lost");

            Assert.That(crossed, Has.Count.EqualTo(4), "three controls + the window; nothing else at this edge");
        });
    }

    // ==================================================================
    // Kinds and properties that do not exist on the destination
    // ==================================================================

    [Test]
    public void ToWeb_AKindWithNoWebRow_IsRemovedAndReported()
    {
        // The catalog says so rather than faking a <div> (FormCatalogCoverageTests pins the eight
        // WinForms-only kinds); the retarget is where that honesty becomes a finding.
        var source = WinForms("""
            <Form Name="LoginForm" Version="1">
              <Controls>
                <Label Id="lbl" Text="Rows" X="8" Y="8" Width="60" Height="23" TabIndex="0"/>
                <DataGridView Id="grid" X="8" Y="40" Width="280" Height="150" ReadOnly="true" TabIndex="1"/>
              </Controls>
            </Form>
            """);

        var result = FormRetarget.Convert(source, FormTarget.Web);

        Assert.Multiple(() =>
        {
            Assert.That(result.Document.FindById("grid"), Is.Null, "a DataGridView has no honest page equivalent");
            Assert.That(result.Document.Controls.Select(c => c.Id), Is.EqualTo(new[] { "lbl" }));

            var lost = Of(result, DesignCodes.RetargetControlLost).Single();
            Assert.That(lost.IsWarning, Is.True);
            Assert.That(lost.Message, Does.Contain("'grid'").And.Contain("DataGridView"));
        });
    }

    [Test]
    public void ToWeb_AContainerWithNoWebRow_HoistsItsChildrenIntoItsPlace()
    {
        // A TabControl full of buttons is the user's work; dropping the subtree would lose it all.
        // The children move up to where the container was, in order, and the finding says so.
        var source = WinForms("""
            <Form Name="LoginForm" Version="1">
              <Controls>
                <Label Id="lblFirst" Text="First" X="8" Y="8" Width="60" Height="23" TabIndex="0"/>
                <TabControl Id="tabs" X="8" Y="40" Width="240" Height="160" TabIndex="1">
                  <Button Id="btnInside" Text="Inside" X="8" Y="8" Width="75" Height="23" TabIndex="2"/>
                </TabControl>
                <Button Id="btnAfter" Text="After" X="8" Y="220" Width="75" Height="23" TabIndex="3"/>
              </Controls>
            </Form>
            """);

        var result = FormRetarget.Convert(source, FormTarget.Web);

        Assert.Multiple(() =>
        {
            Assert.That(result.Document.Controls.Select(c => c.Id),
                Is.EqualTo(new[] { "lblFirst", "btnInside", "btnAfter" }),
                "the child takes the container's position among its siblings");

            // Its pixels were relative to the TabControl at (8,40), so on the form it sat at (16,48):
            // X 8,16 → columns 0,1 and Y 8,48,220 → rows 0,1,2. Without the translation it would
            // share lblFirst's cell — two controls at (0,0) with nothing to say so.
            var inside = (GridGeometry)result.Document.FindById("btnInside")!.Geometry!;
            Assert.That((inside.Col, inside.Row), Is.EqualTo((1, 1)),
                "a hoisted child is placed by its position ON THE FORM, not inside the container that is gone");

            var lost = Of(result, DesignCodes.RetargetControlLost).Single();
            Assert.That(lost.Message, Does.Contain("'tabs'").And.Contain("1 child"),
                "the finding must say the children were moved, or the user cannot review it");
        });
    }

    [Test]
    public void ToWeb_APropertyTheWebDoesNotHave_IsDroppedAndReported()
    {
        // NumericUpDown.DecimalPlaces is WinForms-only in the catalog; Minimum exists on both.
        var source = WinForms("""
            <Form Name="LoginForm" Version="1">
              <Controls>
                <NumericUpDown Id="num" X="8" Y="8" Width="120" Height="23" Minimum="1" DecimalPlaces="2" TabIndex="0"/>
              </Controls>
            </Form>
            """);

        var result = FormRetarget.Convert(source, FormTarget.Web);
        var num = result.Document.FindById("num")!;

        Assert.Multiple(() =>
        {
            Assert.That(num.Properties["Minimum"], Is.EqualTo("1"), "a shared property crosses verbatim");
            Assert.That(num.Properties.ContainsKey("DecimalPlaces"), Is.False,
                "a property the page cannot have must not ride along as dead data");

            var lost = Of(result, DesignCodes.RetargetPropertyLost).Single();
            Assert.That(lost.IsWarning, Is.True);
            Assert.That(lost.Message, Does.Contain("'num.DecimalPlaces'").And.Contain("2"),
                "the finding names the property AND its value — that is what the user has to re-express");
        });
    }

    [Test]
    public void ToWinForms_APropertyWinFormsDoesNotHave_IsDroppedAndReported()
    {
        // RadioButton.GroupName is web-only: WinForms groups radios by container (csc: CS1061).
        var source = Web("""
            <WebForm Name="LoginForm" Version="1">
              <Layout Kind="Grid" Cols="auto" Rows="auto"/>
              <Controls>
                <RadioButton Id="rdo" Text="A" GroupName="choice" Checked="true" Col="0" Row="0" TabIndex="0"/>
              </Controls>
            </WebForm>
            """);

        var result = FormRetarget.Convert(source, FormTarget.WinForms);
        var rdo = result.Document.FindById("rdo")!;

        Assert.Multiple(() =>
        {
            Assert.That(rdo.Properties["Checked"], Is.EqualTo("true"));
            Assert.That(rdo.Properties.ContainsKey("GroupName"), Is.False);
            Assert.That(Of(result, DesignCodes.RetargetPropertyLost).Single().Message,
                Does.Contain("'rdo.GroupName'").And.Contain("choice"));
        });
    }

    // ==================================================================
    // A property that crosses with a VALUE the destination refuses (Task 4 review → slice 1 Task 7)
    // ==================================================================

    [Test]
    public void ToWeb_ASystemColourTheWebCannotUse_CrossesPreserved_AndIsNamed()
    {
        // ActiveCaption is a Windows system colour with no CSS equivalent: valid on the window, Degraded
        // on the page. The value is preserved (the user's own text), but the loss of MEANING is named.
        var source = WinForms("""
            <Form Name="LoginForm" Version="1">
              <Controls>
                <Label Id="lbl" Text="Hi" ForeColor="ActiveCaption" X="8" Y="8" Width="60" Height="23" TabIndex="0"/>
              </Controls>
            </Form>
            """);

        var result = FormRetarget.Convert(source, FormTarget.Web);
        var property = FormControlCatalog.Find("Label")!.Property("ForeColor")!;

        Assert.Multiple(() =>
        {
            Assert.That(result.Document.FindById("lbl")!.Properties["ForeColor"], Is.EqualTo("ActiveCaption"),
                "preserved exactly as written — the page opens it Degraded, never rewritten");
            var named = Of(result, DesignCodes.RetargetPropertyLost).Single();
            Assert.That(named.Message, Does.Contain("'lbl.ForeColor'")
                .And.Contain(property.DescribeRefusal("ActiveCaption", FormTarget.Web)),
                "the reason is the catalog's own refusal, never a hand-written one");
        });
    }

    [Test]
    public void ToWinForms_ACssColourNameWinFormsDoesNotKnow_CrossesPreserved_AndIsNamed()
    {
        // RebeccaPurple is a CSS name System.Drawing.Color lacks: fine on the page, CS0117 on the window.
        var source = Web("""
            <WebForm Name="LoginForm" Version="1">
              <Layout Kind="Grid" Cols="auto" Rows="auto"/>
              <Controls>
                <Label Id="lbl" Text="Hi" ForeColor="RebeccaPurple" Col="0" Row="0" TabIndex="0"/>
              </Controls>
            </WebForm>
            """);

        var result = FormRetarget.Convert(source, FormTarget.WinForms);
        var property = FormControlCatalog.Find("Label")!.Property("ForeColor")!;

        Assert.Multiple(() =>
        {
            Assert.That(result.Document.FindById("lbl")!.Properties["ForeColor"], Is.EqualTo("RebeccaPurple"));
            var named = Of(result, DesignCodes.RetargetPropertyLost).Single();
            Assert.That(named.Message, Does.Contain("'lbl.ForeColor'")
                .And.Contain(property.DescribeRefusal("RebeccaPurple", FormTarget.WinForms)));
        });
    }

    [Test]
    public void AColourBothTargetsAccept_CrossesWithNoFinding([Values(FormTarget.WinForms, FormTarget.Web)] FormTarget from)
    {
        var source = new FormDocument { Target = from, Name = "Sweep" };
        var label = FormCatalogShapes.Canonical(source, FormControlCatalog.Find("Label")!, "c");
        label.Properties["ForeColor"] = "Red";

        var result = FormRetarget.Convert(source, Other(from));

        Assert.That(Of(result, DesignCodes.RetargetPropertyLost), Is.Empty,
            "only a REFUSED value is named — a usable one crossing is not a loss");
    }

    // ==================================================================
    // Binds: the event name is the target's vocabulary (D8)
    // ==================================================================

    [Test]
    public void ToWeb_TheDefaultEventBind_TakesTheWebEventName()
    {
        // Click → click, and TextChanged → input: the catalog's per-kind default events are the
        // measured mapping between the two vocabularies. The handler is the user's Sub and stays.
        var doc = FormRetarget.Convert(WinForms(WinFormsLogin), FormTarget.Web).Document;

        Assert.Multiple(() =>
        {
            var click = doc.FindById("btnLogin")!.Binds.Single();
            Assert.That(click.Event, Is.EqualTo("click"));
            Assert.That(click.Handler, Is.EqualTo("btnLogin_Click"));

            var input = doc.FindById("txtUser")!.Binds.Single();
            Assert.That(input.Event, Is.EqualTo("input"));
            Assert.That(input.Handler, Is.EqualTo("txtUser_TextChanged"));
        });
    }

    [Test]
    public void ToWeb_ABindOnAnEventTheCatalogCannotName_IsDroppedAndReported()
    {
        // Carrying "MouseEnter" into a web document would emit addEventListener("MouseEnter", …),
        // which registers cleanly and never fires — the silent failure this task forbids.
        var source = WinForms("""
            <Form Name="LoginForm" Version="1">
              <Controls>
                <Button Id="btn" Text="Go" X="8" Y="8" Width="75" Height="23" TabIndex="0">
                  <Bind Event="Click" Handler="btn_Click"/>
                  <Bind Event="MouseEnter" Handler="btn_Hover"/>
                </Button>
              </Controls>
            </Form>
            """);

        var result = FormRetarget.Convert(source, FormTarget.Web);
        var btn = result.Document.FindById("btn")!;

        Assert.Multiple(() =>
        {
            Assert.That(btn.Binds.Select(b => b.Event), Is.EqualTo(new[] { "click" }));

            var lost = Of(result, DesignCodes.RetargetBindLost).Single();
            Assert.That(lost.IsWarning, Is.True);
            Assert.That(lost.Message, Does.Contain("'btn'").And.Contain("MouseEnter").And.Contain("btn_Hover"),
                "the finding names the handler so the user can wire it by hand on the other side");
        });
    }

    // ==================================================================
    // Web → WinForms: cells become pixels by a rule the finding can state
    // ==================================================================

    /// <summary>The spec's web login form, with the same two binds.</summary>
    private const string WebLogin = """
        <WebForm Name="LoginForm" Version="1">
          <Layout Kind="Grid" Cols="120px,1fr" Rows="auto,auto" Gap="8px"/>
          <Controls>
            <Label   Id="lblUser"  Text="User"    Col="0" Row="0" TabIndex="0"/>
            <TextBox Id="txtUser"  Col="1" Row="0" TabIndex="1">
              <Bind Event="input" Handler="txtUser_TextChanged"/>
            </TextBox>
            <Button  Id="btnLogin" Text="Sign in" Col="1" Row="1" TabIndex="2">
              <Bind Event="click" Handler="btnLogin_Click"/>
            </Button>
          </Controls>
          <Literal><![CDATA[<p class="hint">Use your work account.</p>]]></Literal>
          <Components/>
          <Resources/>
        </WebForm>
        """;

    private static PixelGeometry Pixels(FormDocument doc, string id) => (PixelGeometry)doc.FindById(id)!.Geometry!;

    [Test]
    public void ToWinForms_AGridForm_PlacesControlsByCell_WithCatalogSizes()
    {
        // Sizes are the catalog defaults (Label 100x23, TextBox 100x23, Button 75x23). The column
        // pitch is the widest sibling + Gap = 108, the row pitch the tallest + Gap = 31, and the
        // origin is Margin. All stated in the finding, all reviewable, none of it guessed.
        var doc = FormRetarget.Convert(Web(WebLogin), FormTarget.WinForms).Document;

        Assert.Multiple(() =>
        {
            Assert.That(doc.Target, Is.EqualTo(FormTarget.WinForms));
            Assert.That(doc.Layout, Is.Null, "a window has no <Layout>");
            Assert.That(doc.Literal, Is.Null, "a window has no markup to pass through");

            var lbl = Pixels(doc, "lblUser");
            Assert.That((lbl.X, lbl.Y, lbl.Width, lbl.Height), Is.EqualTo((16, 16, 100, 23)));

            var txt = Pixels(doc, "txtUser");
            Assert.That((txt.X, txt.Y, txt.Width, txt.Height), Is.EqualTo((124, 16, 100, 23)));

            var btn = Pixels(doc, "btnLogin");
            Assert.That((btn.X, btn.Y, btn.Width, btn.Height), Is.EqualTo((124, 47, 75, 23)));

            Assert.That(btn.Anchor, Is.Null, "unset means WinForms' own Top,Left default; nothing is invented");
            Assert.That(btn.Dock, Is.Null);

            Assert.That((doc.Width, doc.Height), Is.EqualTo((800, 450)),
                "never smaller than a new form — the scaffolder's own size");
            Assert.That(doc.Text, Is.EqualTo("LoginForm"), "the caption is the form's name");
        });
    }

    [Test]
    public void ToWinForms_TheWindowGrows_WhenThePlacedControlsNeedMoreThanANewForm()
    {
        // Ten columns of TextBox at pitch 108: the last right edge is 16 + 9*108 + 100 = 1088.
        var controls = string.Join("\n", Enumerable.Range(0, 10).Select(i =>
            $"""<TextBox Id="t{i}" Col="{i}" Row="0" TabIndex="{i}"/>"""));
        var source = Web($"""
            <WebForm Name="Wide" Version="1">
              <Layout Kind="Grid"/>
              <Controls>
            {controls}
              </Controls>
            </WebForm>
            """);

        var doc = FormRetarget.Convert(source, FormTarget.WinForms).Document;

        Assert.That(doc.Width, Is.EqualTo(1088 + 16), "right edge plus the margin");
        Assert.That(doc.Height, Is.EqualTo(450), "one row does not need more than a new form");
    }

    [Test]
    public void ToWinForms_AFlowForm_StacksControlsInDocumentOrder()
    {
        // A Flow layout has no cells: Dir decides the axis, document order decides the position.
        var vertical = FormRetarget.Convert(Web("""
            <WebForm Name="LoginForm" Version="1">
              <Layout Kind="Flow" Dir="Vertical"/>
              <Controls>
                <Label  Id="a" TabIndex="0"/>
                <Label  Id="b" TabIndex="1"/>
                <Button Id="c" TabIndex="2"/>
              </Controls>
            </WebForm>
            """), FormTarget.WinForms).Document;

        var horizontal = FormRetarget.Convert(Web("""
            <WebForm Name="LoginForm" Version="1">
              <Layout Kind="Flow"/>
              <Controls>
                <Label  Id="a" TabIndex="0"/>
                <Label  Id="b" TabIndex="1"/>
                <Button Id="c" TabIndex="2"/>
              </Controls>
            </WebForm>
            """), FormTarget.WinForms).Document;

        Assert.Multiple(() =>
        {
            Assert.That(new[] { "a", "b", "c" }.Select(id => (Pixels(vertical, id).X, Pixels(vertical, id).Y)),
                Is.EqualTo(new[] { (16, 16), (16, 47), (16, 78) }), "Dir=Vertical stacks down one row pitch at a time");
            Assert.That(new[] { "a", "b", "c" }.Select(id => (Pixels(horizontal, id).X, Pixels(horizontal, id).Y)),
                Is.EqualTo(new[] { (16, 16), (124, 16), (232, 16) }),
                "no Dir is a row, as the emitter's flex-direction says");
        });
    }

    [Test]
    public void ToWinForms_AContainerGrows_ToHoldItsPlacedChildren()
    {
        // A Panel's catalog default is 200x100. Four rows of children at pitch 31 reach a bottom of
        // 16 + 3*31 + 23 = 132, so the Panel must be 132 + Margin = 148 tall or it clips them.
        var doc = FormRetarget.Convert(Web("""
            <WebForm Name="LoginForm" Version="1">
              <Layout Kind="Grid"/>
              <Controls>
                <Panel Id="pnl" Col="0" Row="0" TabIndex="0">
                  <Label Id="l0" Col="0" Row="0" TabIndex="1"/>
                  <Label Id="l1" Col="0" Row="1" TabIndex="2"/>
                  <Label Id="l2" Col="0" Row="2" TabIndex="3"/>
                  <Label Id="l3" Col="0" Row="3" TabIndex="4"/>
                </Panel>
              </Controls>
            </WebForm>
            """), FormTarget.WinForms).Document;

        var panel = Pixels(doc, "pnl");
        var last = Pixels(doc, "l3");

        Assert.Multiple(() =>
        {
            Assert.That((last.X, last.Y), Is.EqualTo((16, 109)), "children are placed relative to their container");
            Assert.That(panel.Height, Is.EqualTo(148));
            Assert.That(panel.Width, Is.EqualTo(200), "the default is kept where it already fits");
        });
    }

    [Test]
    public void ToWinForms_AGridControlWithNoCell_LandsBelowEveryPlacedRow()
    {
        // The browser auto-places it; emulating that is a guess. A row of its own under everything
        // that WAS positioned is a rule, and it cannot land on top of a placed control.
        var doc = FormRetarget.Convert(Web("""
            <WebForm Name="LoginForm" Version="1">
              <Layout Kind="Grid"/>
              <Controls>
                <Label  Id="placed0" Col="0" Row="0" TabIndex="0"/>
                <Label  Id="loose"   TabIndex="1"/>
                <Label  Id="placed1" Col="0" Row="1" TabIndex="2"/>
              </Controls>
            </WebForm>
            """), FormTarget.WinForms).Document;

        Assert.That((Pixels(doc, "loose").X, Pixels(doc, "loose").Y), Is.EqualTo((16, 78)),
            "row 2: one below the highest explicit row");
    }

    [Test]
    public void ToWinForms_ReportsTheLayoutAndTheLiteral_AsCrossedLoss()
    {
        var result = FormRetarget.Convert(Web(WebLogin), FormTarget.WinForms);
        var crossed = Of(result, DesignCodes.RetargetLayoutCrossed).ToList();

        Assert.Multiple(() =>
        {
            var layout = crossed.SingleOrDefault(d => d.Message.Contains("Cols=\"120px,1fr\""));
            Assert.That(layout, Is.Not.Null, "the page's layout is named, verbatim, as the thing that was lost");
            Assert.That(layout!.Message, Does.Contain("800x450"), "…and the window it became");

            Assert.That(crossed.Any(d => d.Message.Contains("<Literal>")), Is.True,
                "pass-through markup has no place in a window and must be named as dropped");

            var btn = crossed.Single(d => d.Message.Contains("'btnLogin'"));
            Assert.That(btn.Message, Does.Contain("Col=1 Row=1").And.Contain("X=124 Y=47"),
                "what it had, and where it landed");

            Assert.That(crossed, Has.Count.EqualTo(5), "layout + literal + three controls");
        });
    }

    // ==================================================================
    // Unknown content (D9): not ours, so it crosses — unless the other side would READ it
    // ==================================================================

    [Test]
    public void UnknownContent_CrossesVerbatim()
    {
        var source = WinForms("""
            <Form Name="LoginForm" Version="1" Theme="dark">
              <Controls>
                <Button Id="btn" X="8" Y="8" Width="75" Height="23" TabIndex="0" Tooltip="Go">
                  <Meta Source="designer-v2"/>
                </Button>
              </Controls>
              <FutureSection Something="42"/>
            </Form>
            """);

        var doc = FormRetarget.Convert(source, FormTarget.Web).Document;
        var btn = doc.FindById("btn")!;

        Assert.Multiple(() =>
        {
            Assert.That(doc.UnknownAttributes["Theme"], Is.EqualTo("dark"));
            Assert.That(doc.UnknownChildren.Single().Name.LocalName, Is.EqualTo("FutureSection"));
            Assert.That(btn.UnknownAttributes["Tooltip"], Is.EqualTo("Go"));
            Assert.That(btn.UnknownChildren.Single().Name.LocalName, Is.EqualTo("Meta"));
        });
    }

    [Test]
    public void UnknownContent_TheDestinationWouldReadAsLayout_IsDroppedAndReported()
    {
        // A stray Col="2" on a .blform control is an unknown attribute there and a CELL on the web;
        // a <Layout> under a <Form> is an unknown element there and THE layout on the web. Carrying
        // either over would let stale text overrule what this retarget derived — Create() writes
        // unknown content after the modelled kind, and SetAttributeValue replaces.
        var source = WinForms("""
            <Form Name="LoginForm" Version="1">
              <Layout Kind="Flow"/>
              <Controls>
                <Button Id="btn" X="8" Y="8" Width="75" Height="23" TabIndex="0" Col="2"/>
              </Controls>
            </Form>
            """);

        var result = FormRetarget.Convert(source, FormTarget.Web);
        var doc = result.Document;

        Assert.Multiple(() =>
        {
            Assert.That(doc.FindById("btn")!.UnknownAttributes.ContainsKey("Col"), Is.False);
            Assert.That(((GridGeometry)doc.FindById("btn")!.Geometry!).Col, Is.EqualTo(0), "the derived cell stands");
            Assert.That(doc.UnknownChildren.Any(e => e.Name.LocalName == "Layout"), Is.False);
            Assert.That(doc.Layout!.Kind, Is.EqualTo(FormLayoutKind.Grid), "the derived layout stands");

            var lost = Of(result, DesignCodes.RetargetPropertyLost).Select(d => d.Message).ToList();
            Assert.That(lost, Has.Count.EqualTo(2));
            Assert.That(lost.Any(m => m.Contains("'btn.Col'")), Is.True);
            Assert.That(lost.Any(m => m.Contains("<Layout>")), Is.True);
        });
    }

    [Test]
    public void UnknownContent_AWindowSizeOnAPage_IsDroppedRatherThanOverrulingTheDerivedOne()
    {
        var source = Web("""
            <WebForm Name="LoginForm" Version="1" Width="123">
              <Layout Kind="Grid"/>
              <Controls><Button Id="btn" Col="0" Row="0" TabIndex="0"/></Controls>
            </WebForm>
            """);

        var result = FormRetarget.Convert(source, FormTarget.WinForms);

        Assert.Multiple(() =>
        {
            Assert.That(result.Document.Width, Is.EqualTo(800), "the derived size, not the stale attribute");
            Assert.That(result.Document.UnknownAttributes.ContainsKey("Width"), Is.False);
            Assert.That(Of(result, DesignCodes.RetargetPropertyLost).Single().Message, Does.Contain("Width=\"123\""));
        });
    }

    // ==================================================================
    // Round trips
    // ==================================================================

    /// <summary>
    /// A <c>.blform</c> laid out exactly as the WinForms placement rule would lay it out: catalog
    /// sizes, Margin origin, pitch 108x31, the scaffolder's window. Going to the web and back must
    /// reproduce it BYTE FOR BYTE — the shared subset because it crosses losslessly, the geometry
    /// because the two derivations are each other's inverse on this shape.
    /// </summary>
    private const string FixedPointWinForms = """
        <Form Name="LoginForm" Version="1" Width="800" Height="450" Text="LoginForm">
          <Controls>
            <Label   Id="lblUser"  Text="User"    X="16"  Y="16" Width="100" Height="23" TabIndex="0"/>
            <TextBox Id="txtUser"  X="124" Y="16" Width="100" Height="23" TabIndex="1">
              <Bind Event="TextChanged" Handler="txtUser_TextChanged"/>
            </TextBox>
            <Button  Id="btnLogin" Text="Sign in" X="124" Y="47" Width="75" Height="23" TabIndex="2">
              <Bind Event="Click" Handler="btnLogin_Click"/>
            </Button>
          </Controls>
          <Components/>
          <Resources/>
        </Form>
        """;

    /// <summary>The web counterpart: all-auto tracks and the scaffolder's gap, as the cell derivation writes them.</summary>
    private const string FixedPointWeb = """
        <WebForm Name="LoginForm" Version="1">
          <Layout Kind="Grid" Cols="auto,auto" Rows="auto,auto" Gap="8px"/>
          <Controls>
            <Label   Id="lblUser"  Text="User"    Col="0" Row="0" TabIndex="0"/>
            <TextBox Id="txtUser"  Col="1" Row="0" TabIndex="1">
              <Bind Event="input" Handler="txtUser_TextChanged"/>
            </TextBox>
            <Button  Id="btnLogin" Text="Sign in" Col="1" Row="1" TabIndex="2">
              <Bind Event="click" Handler="btnLogin_Click"/>
            </Button>
          </Controls>
          <Components/>
          <Resources/>
        </WebForm>
        """;

    private static FormDocument RoundTrip(FormDocument source)
    {
        var there = FormRetarget.Convert(source, Other(source.Target)).Document;

        // Through the WRITER and the READER on the far side, not model to model: the document the
        // user gets is the file, and a model the reader would refuse is no result at all.
        var text = FormDocumentWriter.Create(there);
        var reread = FormDocumentReader.Read(Path.Combine("C:", "forms", "LoginForm" + there.FileExtension), text);
        Assert.That(reread.IsRefused, Is.False,
            "the retargeted document was refused by its own reader: " +
            string.Join("; ", reread.Diagnostics.Select(d => d.Format())));

        return FormRetarget.Convert(reread.Model, source.Target).Document;
    }

    private static FormTarget Other(FormTarget target) =>
        target == FormTarget.Web ? FormTarget.WinForms : FormTarget.Web;

    [Test]
    public void RoundTrip_AFixedPointWinFormsForm_ComesBackByteIdentical()
    {
        var source = WinForms(FixedPointWinForms);

        var back = RoundTrip(source);

        Assert.That(FormDocumentWriter.Create(back), Is.EqualTo(FormDocumentWriter.Create(source)));
    }

    [Test]
    public void RoundTrip_AFixedPointWebForm_ComesBackByteIdentical()
    {
        var source = Web(FixedPointWeb);

        var back = RoundTrip(source);

        Assert.That(FormDocumentWriter.Create(back), Is.EqualTo(FormDocumentWriter.Create(source)));
    }

    /// <summary>
    /// The general case: a form NOT laid out the way the placement rule lays one out. The geometry
    /// is expected to differ — that is the hard edge, reported — and everything else must not.
    /// </summary>
    [Test]
    public void RoundTrip_AnyForm_KeepsTheSharedSubset_AndOnlyTheGeometryMoves()
    {
        var source = WinForms(WinFormsLogin);

        var back = RoundTrip(source);

        Assert.That(SharedSubset(back), Is.EqualTo(SharedSubset(source)),
            "ids, kinds, tab order, properties and wiring must survive a round trip byte for byte");
        Assert.That(FormDocumentWriter.Create(back), Is.Not.EqualTo(FormDocumentWriter.Create(source)),
            "…while the pixel geometry, which was not at the rule's fixed point, moves — and says so");
    }

    /// <summary>
    /// The document with its layout vocabulary erased: root size/caption/layout/literal gone,
    /// every control's geometry gone. What is left is exactly what D2 says the two formats share.
    /// </summary>
    private static string SharedSubset(FormDocument doc)
    {
        var copy = new FormDocument { Target = FormTarget.Web, Name = doc.Name, Version = doc.Version };
        foreach (var control in doc.Controls)
        {
            copy.Controls.Add(Strip(control));
        }

        foreach (var (name, value) in doc.UnknownAttributes) copy.UnknownAttributes[name] = value;
        copy.UnknownChildren.AddRange(doc.UnknownChildren);
        return FormDocumentWriter.Create(copy);

        static FormControl Strip(FormControl control)
        {
            var stripped = control.Clone();
            stripped.Geometry = null;
            stripped.Children.Clear();
            foreach (var child in control.Children)
            {
                stripped.Children.Add(Strip(child));
            }

            return stripped;
        }
    }

    // ==================================================================
    // Task 25 — components cross with the same rules, and never touch the layout edge
    // ==================================================================

    private const string WinFormsWithTray = """
        <Form Name="LoginForm" Version="1" Width="400" Height="300" Text="Sign in">
          <Controls>
            <Button Id="btnLogin" Text="Sign in" X="190" Y="60" Width="100" Height="30" TabIndex="0"/>
          </Controls>
          <Components>
            <Timer Id="tmr" Interval="500" Enabled="true" Note="keep me">
              <Bind Event="Tick" Handler="tmr_Tick"/>
            </Timer>
            <ToolTip Id="tip" InitialDelay="300"/>
          </Components>
          <Resources/>
        </Form>
        """;

    [Test]
    public void ToWeb_ATimerCrossesToTheTray_WithItsTickBind_AndLosesWhatTheWebLacks()
    {
        var source = WinForms(WinFormsWithTray);

        var result = FormRetarget.Convert(source, FormTarget.Web);
        var tmr = result.Document.Components.Single(c => c.Id == "tmr");

        Assert.Multiple(() =>
        {
            Assert.That(result.Document.Controls.Select(c => c.Id), Is.EqualTo(new[] { "btnLogin" }), "a component is not hoisted into the controls");
            Assert.That(tmr.Geometry, Is.Null);
            Assert.That(tmr.Properties["Interval"], Is.EqualTo("500"), "shared, so it crosses");
            Assert.That(tmr.Properties.ContainsKey("Enabled"), Is.False, "WinForms-only: a JS interval cannot exist disabled");
            Assert.That(tmr.UnknownAttributes["Note"], Is.EqualTo("keep me"), "D9: not ours, so it crosses");
            Assert.That(tmr.Binds.Single().Event, Is.EqualTo("tick"), "the catalog's default event, in the web's vocabulary");
            Assert.That(tmr.Binds.Single().Handler, Is.EqualTo("tmr_Tick"));

            // Enabled="true" on a WIRED Timer is not a loss: on the web the wiring IS the enabling.
            // Reporting it as BL8024 told the user to "re-express it on the other side" — of nothing.
            Assert.That(Of(result, DesignCodes.RetargetPropertyLost), Is.Empty);
            var crossed = Of(result, DesignCodes.RetargetRunStateCrossed).Single();
            Assert.That(crossed.IsWarning, Is.True);
            Assert.That(crossed.Message, Does.Contain("'tmr.Enabled'").And.Contain("wiring"));
            Assert.That(source.Components[0].Properties.ContainsKey("Enabled"), Is.True, "the source is untouched");
        });
    }

    // ==================================================================
    // "Wired means running" is a RULE, and it crosses by name (review, 2026-09-19)
    //
    // A web Timer runs the moment it is wired; a WinForms one runs only with Enabled=True. The
    // catalog row states the equivalence (FormWebScript.Implies), and the retarget applies it in
    // both directions and names what it did — never a Timer that silently stops, or silently starts.
    // ==================================================================

    private const string WebWithTimer = """
        <WebForm Name="LoginForm" Version="1">
          <Layout Kind="Grid"/>
          <Controls><Button Id="btn" Col="0" Row="0" TabIndex="0"/></Controls>
          <Components><Timer Id="tmr" Interval="50"><Bind Event="tick" Handler="tmr_Tick"/></Timer></Components>
        </WebForm>
        """;

    [Test]
    public void ToWeb_AWiredTimerThatIsNotEnabled_WarnsThatThePageWillRunIt()
    {
        var source = WinForms(WinFormsWithTray.Replace(" Enabled=\"true\"", ""));

        var result = FormRetarget.Convert(source, FormTarget.Web);

        Assert.Multiple(() =>
        {
            Assert.That(result.Document.Components.Single(c => c.Id == "tmr").Binds.Single().Event, Is.EqualTo("tick"),
                "the wiring still crosses — dropping the handler would be a bigger silent loss");
            var crossed = Of(result, DesignCodes.RetargetRunStateCrossed).Single();
            Assert.That(crossed.IsWarning, Is.True);
            Assert.That(crossed.Message, Does.Contain("'tmr'").And.Contain("Enabled").And.Contain("run"));
        });
    }

    [Test]
    public void ToWeb_AnUnwiredTimer_LosesEnabledAsBefore_AndSaysNothingAboutRunning()
    {
        var source = WinForms(WinFormsWithTray.Replace("<Bind Event=\"Tick\" Handler=\"tmr_Tick\"/>", ""));

        var result = FormRetarget.Convert(source, FormTarget.Web);

        Assert.Multiple(() =>
        {
            Assert.That(Of(result, DesignCodes.RetargetPropertyLost).Single().Message, Does.Contain("'tmr.Enabled'"),
                "with no handler nothing runs on either side, so Enabled is simply a property the web lacks");
            Assert.That(Of(result, DesignCodes.RetargetRunStateCrossed), Is.Empty);
        });
    }

    [Test]
    public void ToWinForms_AWiredWebTimer_ArrivesEnabled_AndSaysSo()
    {
        var source = Web(WebWithTimer);

        var result = FormRetarget.Convert(source, FormTarget.WinForms);
        var tmr = result.Document.Components.Single();

        Assert.Multiple(() =>
        {
            Assert.That(tmr.Properties["Enabled"], Is.EqualTo("true"),
                "a web Timer runs as soon as it is wired; the WinForms default (False) would have made it never fire");
            var crossed = Of(result, DesignCodes.RetargetRunStateCrossed).Single();
            Assert.That(crossed.IsWarning, Is.True);
            Assert.That(crossed.Message, Does.Contain("'tmr'").And.Contain("Enabled"));
        });

        Assert.That(FormRetarget.ConvertToPair(source, FormTarget.WinForms).CodeText, Does.Contain("tmr.Enabled = True"),
            "and the generated window actually starts it");
    }

    [Test]
    public void ToWinForms_AnUnwiredWebTimer_ArrivesAsItWas()
    {
        var source = Web(WebWithTimer.Replace("<Bind Event=\"tick\" Handler=\"tmr_Tick\"/>", ""));

        var result = FormRetarget.Convert(source, FormTarget.WinForms);

        Assert.Multiple(() =>
        {
            Assert.That(result.Document.Components.Single().Properties.ContainsKey("Enabled"), Is.False, "nothing was running");
            Assert.That(Of(result, DesignCodes.RetargetRunStateCrossed), Is.Empty);
        });
    }

    [Test]
    public void ToWeb_AComponentWithNoWebRow_IsLost_AndNamed()
    {
        var result = FormRetarget.Convert(WinForms(WinFormsWithTray), FormTarget.Web);

        Assert.Multiple(() =>
        {
            Assert.That(result.Document.FindById("tip"), Is.Null, "a ToolTip has no honest web form");
            var lost = Of(result, DesignCodes.RetargetControlLost).Single();
            Assert.That(lost.Message, Does.Contain("'tip'").And.Contain("ToolTip"));
        });
    }

    [Test]
    public void Components_NeverTouchTheLayoutEdge()
    {
        // The hard edge is pixels ⇄ cells. A component has neither, so it must not be reported at it.
        var result = FormRetarget.Convert(WinForms(WinFormsWithTray), FormTarget.Web);

        Assert.That(Of(result, DesignCodes.RetargetLayoutCrossed).Select(d => d.Message),
            Has.None.Contains("'tmr'").And.None.Contains("'tip'"));
    }

    [Test]
    public void ToWinForms_AWebTimer_CrossesToTheTray_WithItsTickBind()
    {
        var source = Web(WebWithTimer);

        var doc = FormRetarget.Convert(source, FormTarget.WinForms).Document;
        var tmr = doc.Components.Single();

        Assert.Multiple(() =>
        {
            Assert.That(tmr.Binds.Single().Event, Is.EqualTo("Tick"));
            Assert.That(tmr.Properties["Interval"], Is.EqualTo("50"));
            Assert.That(tmr.Geometry, Is.Null, "no pixels are invented for a component");
            Assert.That(doc.Controls.Single().Geometry, Is.TypeOf<PixelGeometry>(), "…while the control is placed");
        });
    }

    [Test]
    public void RoundTrip_AFixedPointFormWithATimer_ComesBackByteIdentical()
    {
        // Task 21's fixed point, plus a Timer: the component crosses losslessly in both directions.
        var xml = FixedPointWinForms.Replace(
            "  <Components/>",
            "  <Components>\n    <Timer Id=\"tmr\" Interval=\"500\" Enabled=\"true\">\n      <Bind Event=\"Tick\" Handler=\"tmr_Tick\"/>\n    </Timer>\n  </Components>");
        Assume.That(xml, Does.Contain("<Timer"), "the fixture's <Components/> line must match");
        var source = WinForms(xml);

        var back = RoundTrip(source);

        // Enabled is WinForms-only and does not exist on the web — but a WIRED web Timer runs, and the
        // way back derives Enabled="true" from that wiring (FormWebScript.Implies), so the round trip
        // is byte-identical INCLUDING the one attribute the web cannot hold.
        Assert.That(FormDocumentWriter.Create(back), Is.EqualTo(FormDocumentWriter.Create(source)));
    }

    [Test]
    public void ConvertToPair_ForAWebTimer_WritesTheTypedSetInterval_AndAParameterlessStub()
    {
        var pair = FormRetarget.ConvertToPair(WinForms(WinFormsWithTray), FormTarget.Web);

        Assert.Multiple(() =>
        {
            Assert.That(pair.DocumentText, Does.Contain("<Timer Id=\"tmr\"").And.Contain("Event=\"tick\""));
            Assert.That(pair.CodeText, Does.Contain("Private tmr As Integer"));
            Assert.That(pair.CodeText, Does.Contain("Dim w As Window = ::window"));
            Assert.That(pair.CodeText, Does.Contain("tmr = w.setInterval(AddressOf tmr_Tick, 500)"));
            Assert.That(pair.CodeText, Does.Contain("Private Sub tmr_Tick()"), "parameterless: Window.setInterval takes an Action");
            Assert.That(pair.CodeText, Does.Not.Contain("tip"), "the ToolTip did not cross");
        });
    }

    // ==================================================================
    // Task 26 — a strip and its items cross GEOMETRY-LESS, never sized like an ordinary control
    // ==================================================================
    //
    // ⛔ RED for the right reason today: Place() (FormRetarget.cs:584) sizes and positions every
    // sibling unconditionally, including a Docked strip and, by recursing into DeriveCells/Place, its
    // Item children — so a strip currently receives a PixelGeometry/GridGeometry and a BL8025 finding
    // naming it, exactly like an ordinary control. These tests must fail on THAT (geometry not null,
    // or a BL8025 message naming the strip/an item) — not on a compile error or an unrelated exception.

    private static IEnumerable<FormControl> Recurse(IEnumerable<FormControl> controls)
    {
        foreach (var c in controls)
        {
            yield return c;
            foreach (var d in Recurse(c.Children))
            {
                yield return d;
            }
        }
    }

    [Test]
    public void ToWinForms_AStripAndItsItems_CrossGeometryLess_WithDock()
    {
        var source = Web("""
            <WebForm Name="LoginForm" Version="1">
              <Layout Kind="Grid"/>
              <Controls>
                <MenuStrip Id="menuStrip1" Dock="Top">
                  <ToolStripMenuItem Id="mnuFile" Text="&amp;File">
                    <ToolStripMenuItem Id="mnuOpen" Text="&amp;Open..."/>
                  </ToolStripMenuItem>
                  <ToolStripMenuItem Id="mnuExit" Text="E&amp;xit"/>
                </MenuStrip>
                <StatusStrip Id="statusStrip1">
                  <ToolStripStatusLabel Id="lblStatus" Text="Ready"/>
                </StatusStrip>
                <Button Id="btnGo" Text="Go" Col="0" Row="0" TabIndex="0"/>
              </Controls>
            </WebForm>
            """);

        var result = FormRetarget.Convert(source, FormTarget.WinForms);
        var doc = result.Document;

        var menuStrip = doc.Controls.Single(c => c.Id == "menuStrip1");
        var statusStrip = doc.Controls.Single(c => c.Id == "statusStrip1");
        var items = Recurse(menuStrip.Children).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(menuStrip.Geometry, Is.Null, "a strip docks to the form; it is never given pixels");
            Assert.That(menuStrip.Properties["Dock"], Is.EqualTo("Top"),
                "the fixture set Dock explicitly — it crosses because Dock applies to both targets, " +
                "not because the retarget invented it");

            Assert.That(items.Select(i => i.Id), Is.EquivalentTo(new[] { "mnuFile", "mnuOpen", "mnuExit" }));
            foreach (var item in items)
            {
                Assert.That(item.Geometry, Is.Null, $"'{item.Id}' is an Item; it must not receive geometry");
            }
            Assert.That(menuStrip.Children.Select(c => c.Id), Is.EqualTo(new[] { "mnuFile", "mnuExit" }),
                "items stay nested under their host, in document order");
            Assert.That(menuStrip.Children.Single(c => c.Id == "mnuFile").Children.Select(c => c.Id),
                Is.EqualTo(new[] { "mnuOpen" }));

            var btn = doc.Controls.Single(c => c.Id == "btnGo");
            Assert.That(btn.Geometry, Is.TypeOf<PixelGeometry>(), "the ordinary control is still placed");

            var crossedMessages = Of(result, DesignCodes.RetargetLayoutCrossed).Select(d => d.Message).ToList();
            foreach (var id in new[] { "menuStrip1", "mnuFile", "mnuOpen", "mnuExit", "statusStrip1", "lblStatus" })
            {
                Assert.That(crossedMessages, Has.None.Contains($"'{id}'"),
                    $"'{id}' is a strip or an item; it must never be named at the pixel⇄cell edge");
            }

            // The fixture deliberately left Dock unset on the StatusStrip: it must not gain one, and
            // the catalog's OWN default for the row (Bottom — every StatusStrip docks to the bottom
            // unless told otherwise) must still be what "IsDockedToBottom" for this row means.
            Assert.That(statusStrip.Properties.ContainsKey("Dock"), Is.False,
                "nothing was set on the source, so nothing must be invented on the destination");
            Assert.That(FormControlCatalog.Find("StatusStrip")!.Property("Dock")!.Default, Is.EqualTo("Bottom"),
                "the row's own default IS 'docked to bottom' — the catalog states it once");
        });
    }

    [Test]
    public void ToWinForms_APageWhoseTopLevelIsAStripAlone_DoesNotThrow()
    {
        // Guards the Max()-on-an-empty-sequence trap once "positioned" filtering exists: a page whose
        // only top-level control is a strip has ZERO positioned siblings, and Place() must not throw.
        var source = Web("""
            <WebForm Name="LoginForm" Version="1">
              <Controls>
                <MenuStrip Id="menuStrip1" Dock="Top">
                  <ToolStripMenuItem Id="mnuFile" Text="&amp;File"/>
                </MenuStrip>
              </Controls>
            </WebForm>
            """);

        FormRetargetResult result = null!;
        Assert.DoesNotThrow(() => result = FormRetarget.Convert(source, FormTarget.WinForms));

        Assert.That(result.Document.Controls.Single(c => c.Id == "menuStrip1").Geometry, Is.Null);
    }

    [Test]
    public void ToWeb_AStripCrosses_AndTheRetargetedPageIsChrome()
    {
        var source = WinForms("""
            <Form Name="LoginForm" Version="1" Width="400" Height="300" Text="Sign in">
              <Controls>
                <MenuStrip Id="menuStrip1" Dock="Top">
                  <ToolStripMenuItem Id="mnuFile" Text="&amp;File">
                    <ToolStripMenuItem Id="mnuOpen" Text="&amp;Open..."/>
                  </ToolStripMenuItem>
                </MenuStrip>
                <Button Id="btnLogin" Text="Sign in" X="190" Y="60" Width="100" Height="30" TabIndex="0"/>
              </Controls>
              <Components/>
              <Resources/>
            </Form>
            """);

        var pair = FormRetarget.ConvertToPair(source, FormTarget.Web);
        var read = FormDocumentReader.Read(Path.Combine("C:", "forms", pair.DocumentFileName), pair.DocumentText);
        Assert.That(read.IsRefused, Is.False,
            "the retargeted document was refused by its own reader: " +
            string.Join("; ", read.Diagnostics.Select(d => d.Format())));

        var html = FormAssetEmitter.Html(read.Model, "app.js");

        var nav = html.IndexOf("<nav", StringComparison.Ordinal);
        var formDiv = html.IndexOf("<div class=\"vgs-form\"", StringComparison.Ordinal);
        Assert.That(nav, Is.GreaterThanOrEqualTo(0), "the MenuStrip must be emitted as page chrome");
        Assert.That(formDiv, Is.GreaterThanOrEqualTo(0));
        Assert.That(nav, Is.LessThan(formDiv), "chrome docks to the page, before the form div");

        // ⚠ DeriveCells' else-branch (FormRetarget.cs:529-534) already nulls a control's geometry when
        // it has no PixelGeometry to derive a cell from — which is every item, since Task 26's own
        // fixture never gives one pixels. This assertion may already be GREEN; if so, that is stated
        // here rather than assumed.
        var menuStrip = read.Model.Controls.Single(c => c.Id == "menuStrip1");
        foreach (var item in Recurse(menuStrip.Children))
        {
            Assert.That(item.Geometry, Is.Null, $"'{item.Id}' must not receive geometry crossing to the web");
        }
    }

    // ==================================================================
    // The catalog gate: every kind, every property, both directions
    // ==================================================================

    /// <summary>
    /// ⛔ Driven from the catalog so a new row is covered the day it is added. For each kind the
    /// source target has, a control with EVERY property set is retargeted; the properties reported
    /// lost must be exactly those the catalog says do not apply to the destination, and the kind
    /// itself is lost exactly when the destination has no row for it.
    /// </summary>
    [Test]
    public void EveryCatalogKind_Retargets_ReportingExactlyThePropertiesThatCannotCross(
        [Values(FormTarget.WinForms, FormTarget.Web)] FormTarget from)
    {
        var to = Other(from);

        foreach (var definition in FormControlCatalog.For(from))
        {
            var source = new FormDocument { Target = from, Name = "Sweep" };
            // Task 24, commit 24b: the shape (Tray/Docked/Item/Positioned) is the ONE answer in
            // FormCatalogShapes.Canonical rather than a hand-picked list — an Item definition (none
            // exist yet; commit 24c adds the first) nests "c" under a host, so the crossed control is
            // found by kind with Locate rather than by Single()-ing a top-level list.
            var control = FormCatalogShapes.Canonical(source, definition, "c");
            foreach (var property in definition.Properties.Where(p => p.AppliesTo(from)))
            {
                control.Properties[property.Name] = Sample(property);
            }

            var result = FormRetarget.Convert(source, to);

            // ⛔ Task 24, commit 24b: computed ONCE, as a LIST, rather than through
            // FormCatalogShapes.Locate's FirstOrDefault. The old code trusted a bare `!` on Locate's
            // result to mean "exactly one" — it means "at least one, or null" — so a regression that
            // converted "c" TWICE (a duplicate control of the same kind) would still pass every
            // assertion below: the unsupported branch never saw the duplicate (Locate found the
            // first one and it was non-null, so Is.Null would rightly fail anyway) and the supported
            // branch would silently read the FIRST match's properties while a second, unaccounted-for
            // copy sat in the document. Asserting the count explicitly is what makes that regression
            // visible.
            //
            // ⛔ Deliberately NOT also a total-control-count assertion. An Item has no destination row
            // when SupportsTarget(to) is false, and its Docked HOST legitimately survives the
            // conversion (only the Item itself is lost) — a total-count check would go red the day
            // 24c lands an Item row, which is exactly the regression this sweep exists to prevent.
            var matches = result.Document.AllControls().Concat(result.Document.AllComponents())
                .Where(c => c.Kind == definition.Kind)
                .ToList();

            if (!definition.SupportsTarget(to))
            {
                Assert.That(matches, Is.Empty, $"{definition.Kind} has no {to} row and must go");
                Assert.That(Of(result, DesignCodes.RetargetControlLost).Count(), Is.EqualTo(1), definition.Kind);
                continue;
            }

            var expectedLost = definition.Properties
                .Where(p => p.AppliesTo(from) && !p.AppliesTo(to))
                .Select(p => p.Name)
                .OrderBy(n => n)
                .ToList();

            // ⚠ Scoped to messages naming 'c.' — Canonical's Item branch (none exist yet) would also
            // add a host control whose OWN lost properties must not be mistaken for "c"'s.
            var reportedLost = Of(result, DesignCodes.RetargetPropertyLost)
                .Select(d => d.Message)
                .Where(m => m.Contains("'c."))
                .Select(m => definition.Properties.Single(p => m.Contains($"'c.{p.Name}'")).Name)
                .OrderBy(n => n)
                .ToList();

            Assert.That(matches, Has.Count.EqualTo(1),
                $"{definition.Kind} {from}→{to}: expected exactly one converted control, found {matches.Count}");

            var crossed = matches[0].Properties.Keys.OrderBy(n => n).ToList();
            var expectedCrossed = definition.Properties
                .Where(p => p.AppliesTo(from) && p.AppliesTo(to))
                .Select(p => p.Name)
                .OrderBy(n => n)
                .ToList();

            Assert.Multiple(() =>
            {
                Assert.That(reportedLost, Is.EqualTo(expectedLost), $"{definition.Kind} {from}→{to}: reported loss");
                Assert.That(crossed, Is.EqualTo(expectedCrossed), $"{definition.Kind} {from}→{to}: what crossed");

                // Task 26: a Docked strip or an Item never receives geometry, and is never named at
                // the pixel⇄cell edge (BL8025/RetargetLayoutCrossed) — that edge is for Positioned
                // controls only. Scoped to messages naming exactly 'c' (quoted), never the host "cHost".
                if (definition.Place is FormPlace.Docked or FormPlace.Item)
                {
                    Assert.That(matches[0].Geometry, Is.Null,
                        $"{definition.Kind} {from}→{to}: a {definition.Place} control must not receive geometry");
                    var crossedAtLayoutEdge = Of(result, DesignCodes.RetargetLayoutCrossed)
                        .Select(d => d.Message)
                        .Where(m => m.Contains("'c'"))
                        .ToList();
                    Assert.That(crossedAtLayoutEdge, Is.Empty,
                        $"{definition.Kind} {from}→{to}: a {definition.Place} control must not be reported " +
                        "at the pixel⇄cell edge");
                }
            });
        }
    }

    /// <summary>
    /// Values usable on one target and refused on the other — the only target-specific refusals the
    /// catalog makes today (<c>FormPropertyDef.Accepts(value, target)</c>): a system colour with no CSS
    /// equivalent, and a CSS colour name System.Drawing.Color lacks. The sweep picks, per row, whichever
    /// one the source accepts and the destination refuses, so a new refusal kind needs only a new entry.
    /// </summary>
    private static readonly string[] TargetRefusedSamples = { "ActiveCaption", "RebeccaPurple" };

    /// <summary>
    /// ⛔ Catalog-driven companion of the sweep above: every property that APPLIES on both targets but
    /// holds a value the destination refuses crosses PRESERVED (the user's own text, opened Degraded on
    /// the other side) and is NAMED with the catalog's own refusal reason — never carried silently.
    /// Asserts it exercised at least one row, so it cannot pass by absence.
    /// </summary>
    [Test]
    public void EveryCatalogProperty_WhoseValueTheDestinationRefuses_CrossesPreserved_AndIsNamed(
        [Values(FormTarget.WinForms, FormTarget.Web)] FormTarget from)
    {
        var to = Other(from);
        var exercised = 0;

        foreach (var definition in FormControlCatalog.For(from).Where(d => d.SupportsTarget(to)))
        {
            foreach (var property in definition.Properties.Where(p => p.AppliesTo(from) && p.AppliesTo(to)))
            {
                var value = TargetRefusedSamples.FirstOrDefault(v => property.Accepts(v, from) && !property.Accepts(v, to));
                if (value == null)
                {
                    continue;
                }

                var source = new FormDocument { Target = from, Name = "Sweep" };
                FormCatalogShapes.Canonical(source, definition, "c").Properties[property.Name] = value;

                var result = FormRetarget.Convert(source, to);
                var converted = result.Document.AllControls().Concat(result.Document.AllComponents())
                    .Single(c => c.Kind == definition.Kind);
                var named = Of(result, DesignCodes.RetargetPropertyLost)
                    .Where(d => d.Message.Contains($"'c.{property.Name}'"))
                    .ToList();

                Assert.Multiple(() =>
                {
                    Assert.That(converted.Properties.GetValueOrDefault(property.Name), Is.EqualTo(value),
                        $"{definition.Kind}.{property.Name} {from}→{to}: preserved exactly as written");
                    Assert.That(named, Has.Count.EqualTo(1),
                        $"{definition.Kind}.{property.Name} = \"{value}\" {from}→{to}: must be named once");
                    Assert.That(named.FirstOrDefault()?.Message, Does.Contain(property.DescribeRefusal(value, to)),
                        $"{definition.Kind}.{property.Name} {from}→{to}: the catalog's own reason");
                });
                exercised++;
            }
        }

        Assert.That(exercised, Is.GreaterThan(0), $"{from}→{to}: no row was exercised — the sweep is vacuous");
    }

    private static string Sample(FormPropertyDef property) => property.Type switch
    {
        FormPropertyType.Int => "1",
        FormPropertyType.Bool => "true",
        FormPropertyType.Color => "#ff0000",
        FormPropertyType.Enum => property.AllowedValues![0],
        FormPropertyType.Size => "75, 23",
        _ => "x"
    };
}
