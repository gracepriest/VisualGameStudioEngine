using BasicLang.Forms;
using BasicLang.Forms.Serialization;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// A positioned control's <c>Dock</c> the designer cannot honour is REFUSED (BL8033), never emitted and never
/// silently read as "not docked"; a strip's bad <c>Dock</c> DEGRADES (D9) and docks at its row default everywhere.
///
/// <para>⛔ The defect this pins: a positioned control's <c>Dock</c> was never validated. The WinForms region
/// writer spliced it verbatim — <c>Dock="Rigth"</c> became <c>btn.Dock = DockStyle.Rigth</c>, which BasicLang
/// compiles green (a WinForms member degrades to <c>Object</c>) and csc rejects later — while a Canvas page's
/// dock resolver (<see cref="FormDockLayout.EdgeOf"/>) read the same value as "not docked". A hand-edited
/// STRIP <c>Dock="Left"</c>/<c>"Fill"</c>/<c>"None"</c> resolved to Top on the canvas and the page while
/// WinForms kept the row default — a StatusStrip drawn at the top that runs at the bottom.</para>
///
/// <para>⛔ The rule (<see cref="FormDock"/>): a positioned control's Dock is matched TRIMMED and
/// CASE-INSENSITIVELY against DockStyle's six members, and the emitter writes the enum's own spelling —
/// <c>DockStyle.fill</c> is a csc error (plan 2026-09-27 S11). A strip's Dock is its catalog row's own
/// values (Top, Bottom) under the row's Enum rule; anything else is Degraded and read as the row default.</para>
/// </summary>
[TestFixture]
public class FormDockValidationTests
{
    private const string UnknownDock = "BL8033";

    private static string WinFormsButtonDocked(string dock) => $"""
        <Form Name="DockForm" Version="1" Width="400" Height="300" Text="D">
          <Controls><Button Id="btn" X="8" Y="8" Width="75" Height="23" Dock="{dock}" TabIndex="0"/></Controls>
        </Form>
        """;

    private static string CanvasButtonDocked(string dock) => $"""
        <WebForm Name="DockForm" Version="1" Width="640" Height="400">
          <Layout Kind="Canvas"/>
          <Controls><Button Id="btn" X="8" Y="8" Width="75" Height="23" Dock="{dock}" TabIndex="0"/></Controls>
        </WebForm>
        """;

    private static string WinFormsStripDocked(string kind, string dock) => $"""
        <Form Name="DockForm" Version="1" Width="400" Height="300" Text="D">
          <Controls><{kind} Id="strip" Dock="{dock}"/></Controls>
        </Form>
        """;

    private static string GridPageStripDocked(string kind, string dock) => $"""
        <WebForm Name="DockForm" Version="1">
          <Layout Kind="Grid"/>
          <Controls><{kind} Id="strip" Dock="{dock}"/></Controls>
        </WebForm>
        """;

    internal static FormDocument Read(string xml, FormTarget target)
    {
        var file = target == FormTarget.Web ? "DockForm.blwebform" : "DockForm.blform";
        var read = FormDocumentReader.Read(Path.Combine(Path.GetTempPath(), file), xml);
        Assert.That(read.IsRefused, Is.False,
            "the fixture must reach the region writer: " + string.Join("; ", read.Diagnostics.Select(d => d.Format())));
        return read.Model;
    }

    private static RegionWriteResult Write(FormDocument form)
    {
        var scaffold = FormScaffolder.Create("DockForm", form.Target);
        return RegionWriter.Write(scaffold.CodeFileName, scaffold.CodeText, form, scaffold.DocumentFileName);
    }

    private static string Findings(RegionWriteResult result) =>
        string.Join("\n", result.Diagnostics.Select(d => d.Format()));

    /// <summary>Every generated <c>id.Dock = …</c> statement, indentation and line ending removed.</summary>
    private static string[] DockLines(string text, string id) =>
        text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith(id + ".Dock ", StringComparison.Ordinal))
            .ToArray();

    private static List<DesignDiagnostic> DockFindings(RegionWriteResult result) =>
        result.Diagnostics.Where(d => d.Code == UnknownDock).ToList();

    // ==================================================================
    // A positioned control
    // ==================================================================

    [TestCase("Rigth")]
    [TestCase("Fil")]
    [TestCase("Top,Left")]
    [TestCase("DockStyle.Fill")]
    public void AnUnknownDock_OnAWindow_IsRefused_NamingTheControlAndTheValidValues(string dock)
    {
        var result = Write(Read(WinFormsButtonDocked(dock), FormTarget.WinForms));

        Assert.Multiple(() =>
        {
            Assert.That(result.Refused, Is.True, Findings(result));
            var finding = DockFindings(result);
            Assert.That(finding, Has.Count.EqualTo(1), Findings(result));
            Assert.That(finding.Single().IsWarning, Is.False);
            Assert.That(finding.Single().Message,
                Does.Contain("'btn'").And.Contain($"'{dock}'")
                    .And.Contain("None").And.Contain("Top").And.Contain("Bottom")
                    .And.Contain("Left").And.Contain("Right").And.Contain("Fill"));
            Assert.That(result.Text, Does.Not.Contain("DockStyle." + dock),
                "a refused write hands back the source unchanged");
        });
    }

    /// <summary>
    /// ⛔ A Canvas page reads Dock too (<see cref="FormDockLayout.EdgeOf"/>), and silently took an unknown value
    /// as "not docked" — the page and the window it retargets to would disagree.
    /// </summary>
    [Test]
    public void AnUnknownDock_OnACanvasPage_IsRefused()
    {
        var result = Write(Read(CanvasButtonDocked("Rigth"), FormTarget.Web));

        Assert.Multiple(() =>
        {
            Assert.That(result.Refused, Is.True, Findings(result));
            Assert.That(DockFindings(result), Has.Count.EqualTo(1), Findings(result));
            Assert.That(DockFindings(result).Single().Message, Does.Contain("'btn'").And.Contain("Rigth"));
        });
    }

    /// <summary>
    /// ⛔ The accepted spellings are the CANONICAL member once emitted: <c>DockStyle.fill</c> is a csc error
    /// (plan 2026-09-27 S11) and <c>DockStyle. Top </c> is not the member either.
    /// </summary>
    [TestCase("fill", "Fill")]
    [TestCase(" Top ", "Top")]
    [TestCase("LEFT", "Left")]
    [TestCase("right", "Right")]
    [TestCase("bottom", "Bottom")]
    [TestCase("none", "None")]
    [TestCase("Fill", "Fill")]
    public void ACaseOrWhitespaceVariant_IsAccepted_AndEmittedAsTheEnumsOwnSpelling(string dock, string member)
    {
        var result = Write(Read(WinFormsButtonDocked(dock), FormTarget.WinForms));

        Assert.Multiple(() =>
        {
            Assert.That(result.Refused, Is.False, Findings(result));
            Assert.That(DockFindings(result), Is.Empty);
            Assert.That(DockLines(result.Text, "btn"), Is.EqualTo(new[] { $"btn.Dock = DockStyle.{member}" }),
                "exactly the member, in the enum's spelling, nothing around it");
        });
    }

    [TestCase("fill", FormDockEdge.Fill)]
    [TestCase(" Top ", FormDockEdge.Top)]
    [TestCase("RIGHT", FormDockEdge.Right)]
    public void TheCanvasResolver_ReadsAnAcceptedVariant_AsTheSameEdgeTheWindowRunsWith(string dock, FormDockEdge edge)
    {
        var form = Read(CanvasButtonDocked(dock), FormTarget.Web);

        Assert.That(FormDockLayout.EdgeOf(form.FindById("btn")!), Is.EqualTo(edge));
    }

    /// <summary>
    /// One rule, three readers: for every spelling, the check refuses EXACTLY the values the resolver cannot
    /// read as an edge (apart from None, which is valid and docks nowhere).
    /// </summary>
    [TestCase("Top", true)]
    [TestCase("  bottom", true)]
    [TestCase("Fill ", true)]
    [TestCase("None", true)]
    [TestCase("NONE", true)]
    [TestCase("Rigth", false)]
    [TestCase("Centre", false)]
    [TestCase("Top Left", false)]
    public void TheCheck_AndTheResolver_AgreeOnEverySpelling(string dock, bool accepted)
    {
        var form = Read(CanvasButtonDocked(dock), FormTarget.Web);
        var refused = RegionWriter.DockRefusals(form.SourcePath, form).Count > 0;
        var edge = FormDockLayout.EdgeOf(form.FindById("btn")!);
        var isNone = string.Equals(dock.Trim(), "None", StringComparison.OrdinalIgnoreCase);

        Assert.Multiple(() =>
        {
            Assert.That(refused, Is.EqualTo(!accepted), "the check");
            Assert.That(edge != null, Is.EqualTo(accepted && !isNone), "the resolver");
        });
    }

    /// <summary>An absent or blank Dock is "not docked", never a refusal.</summary>
    [TestCase("")]
    [TestCase("   ")]
    public void ABlankDock_IsNotDocked_AndNotRefused(string dock)
    {
        var result = Write(Read(WinFormsButtonDocked(dock), FormTarget.WinForms));

        Assert.Multiple(() =>
        {
            Assert.That(DockFindings(result), Is.Empty, Findings(result));
            Assert.That(result.Text, Does.Not.Contain("btn.Dock"));
        });
    }

    /// <summary>
    /// A Grid page has no pixel geometry, so its controls carry no Dock to refuse — and a stray attribute there is
    /// an unknown attribute the reader already keeps aside.
    /// </summary>
    [Test]
    public void AGridPage_HasNoPositionedDockToRefuse()
    {
        var form = Read("""
            <WebForm Name="DockForm" Version="1">
              <Layout Kind="Grid"/>
              <Controls><Button Id="btn" Col="0" Row="0" Dock="Rigth" TabIndex="0"/></Controls>
            </WebForm>
            """, FormTarget.Web);

        Assert.That(RegionWriter.DockRefusals(form.SourcePath, form), Is.Empty);
    }

    // ==================================================================
    // A strip — a bad Dock DEGRADES (D9), it is never refused
    // ==================================================================

    private static string RowDefault(string kind) => FormControlCatalog.Find(kind)!.Property("Dock")!.Default!;

    /// <summary>
    /// ⛔ A strip's Dock is a catalog PROPERTY, so a value its row does not accept costs one row, never the document:
    /// the reader freezes it (Degraded), the writer skips it with a BL8009 warning, and the save still succeeds. The
    /// canvas edge, the page edge and the window's edge must all be the ROW DEFAULT — WinForms never receives the
    /// value, so it runs the strip at its default, and the canvas used to read anything but "Bottom" as Top.
    /// </summary>
    [TestCase("MenuStrip", "Left")]
    [TestCase("MenuStrip", "Fill")]
    [TestCase("StatusStrip", "None")]
    [TestCase("StatusStrip", "Right")]
    [TestCase("StatusStrip", "Left")]
    [TestCase("StatusStrip", " Bottom ")]
    [TestCase("ToolStrip", "Top Left")]
    public void AStripDockedToAnEdgeItsRowDoesNotHave_Degrades_AndDocksAtTheRowDefaultEverywhere(string kind, string dock)
    {
        var file = FormDocumentReader.Read(Path.Combine(Path.GetTempPath(), "DockForm.blform"), WinFormsStripDocked(kind, dock));
        var result = Write(file.Model);
        var rowDefault = RowDefault(kind);

        Assert.Multiple(() =>
        {
            Assert.That(file.IsRefused, Is.False);
            Assert.That(file.Degraded.Select(d => $"{d.ControlId}.{d.Property}"), Does.Contain("strip.Dock"),
                "the reader freezes the row and keeps the value");

            Assert.That(result.Refused, Is.False, "one bad value costs one row, never the save: " + Findings(result));
            Assert.That(DockFindings(result), Is.Empty);
            Assert.That(result.Diagnostics.Where(d => d.Code == DesignCodes.DegradedProperty && d.IsWarning)
                    .Select(d => d.Message), Has.Some.Contains("'strip.Dock'"),
                "the skip is reported, never silent");
            Assert.That(DockLines(result.Text, "strip"), Is.Empty,
                "a Degraded value is never emitted, so the window runs the strip at its row default");

            Assert.That(FormDockLayout.EdgeOf(file.Model.FindById("strip")!).ToString(), Is.EqualTo(rowDefault),
                "the canvas and the page dock it where the window runs it — the row default");
        });
    }

    /// <summary>A Grid page's footer reads the same strip edge: a hand-edited StatusStrip Dock still saves, at the bottom.</summary>
    [Test]
    public void AStripDockedLeft_OnAGridPage_StillSaves_AndDocksAtItsRowDefault()
    {
        var form = Read(GridPageStripDocked("StatusStrip", "Left"), FormTarget.Web);
        var result = Write(form);

        Assert.Multiple(() =>
        {
            Assert.That(result.Refused, Is.False, Findings(result));
            Assert.That(DockFindings(result), Is.Empty);
            Assert.That(FormDockLayout.EdgeOf(form.FindById("strip")!), Is.EqualTo(FormDockEdge.Bottom));
            Assert.That(form.FindById("strip")!.IsDockedToBottom, Is.True, "the page's footer rule");
        });
    }

    /// <summary>
    /// The one Degraded spelling the writer DOES emit is the row's own source form — <c>DockStyle.Bottom</c> is written
    /// as itself — so the canvas must read it as the member it names, not fall back to the default (Top for a ToolStrip).
    /// </summary>
    [Test]
    public void AStripsSourceFormDock_IsReadAsTheMemberTheWindowRunsWith()
    {
        var form = Read(WinFormsStripDocked("ToolStrip", "DockStyle.Bottom"), FormTarget.WinForms);
        var result = Write(form);

        Assert.Multiple(() =>
        {
            Assert.That(RowDefault("ToolStrip"), Is.EqualTo("Top"), "otherwise this case cannot tell the two apart");
            Assert.That(result.Refused, Is.False, Findings(result));
            Assert.That(DockLines(result.Text, "strip"), Is.EqualTo(new[] { "strip.Dock = DockStyle.Bottom" }));
            Assert.That(FormDockLayout.EdgeOf(form.FindById("strip")!), Is.EqualTo(FormDockEdge.Bottom));
        });
    }

    [TestCase("MenuStrip", "Top", "Top")]
    [TestCase("StatusStrip", "bottom", "Bottom")]
    [TestCase("ToolStrip", "BOTTOM", "Bottom")]
    public void AStripDockedToItsOwnEdge_InAnyCase_IsAccepted(string kind, string dock, string member)
    {
        var form = Read(WinFormsStripDocked(kind, dock), FormTarget.WinForms);
        var result = Write(form);

        Assert.Multiple(() =>
        {
            Assert.That(result.Refused, Is.False, Findings(result));
            Assert.That(result.Text, Does.Contain($"strip.Dock = DockStyle.{member}"));
            Assert.That(FormDockLayout.EdgeOf(form.FindById("strip")!).ToString(), Is.EqualTo(member),
                "the canvas docks the strip where the window runs it");
        });
    }

    /// <summary>
    /// An EMPTY strip Dock is the row default (<c>FormControl.DockEdge</c>'s own guard), never a refusal — a StatusStrip
    /// written <c>Dock=""</c> still docks to the bottom on the canvas.
    /// </summary>
    [Test]
    public void AStripWithAnEmptyDock_TakesItsRowDefault_AndIsNotRefused()
    {
        var form = Read(WinFormsStripDocked("StatusStrip", ""), FormTarget.WinForms);

        Assert.Multiple(() =>
        {
            Assert.That(RegionWriter.DockRefusals(form.SourcePath, form), Is.Empty);
            Assert.That(FormDockLayout.EdgeOf(form.FindById("strip")!), Is.EqualTo(FormDockEdge.Bottom));
        });
    }

    // ==================================================================
    // Retarget — refused politely, before anything is produced
    // ==================================================================

    [Test]
    public void ACanvasPageWithAnUnknownDock_IsRefusedByTheRetarget_NeverAnInternalError()
    {
        var source = Read(CanvasButtonDocked("Rigth"), FormTarget.Web);

        var refusals = FormRetarget.Refusals(source, FormTarget.WinForms);

        Assert.Multiple(() =>
        {
            Assert.That(refusals.Select(d => d.Code), Is.EqualTo(new[] { UnknownDock }));
            Assert.That(refusals.Single().Message, Does.Contain("'btn'").And.Contain("Rigth"));
            var thrown = Assert.Throws<ArgumentException>(() => FormRetarget.ConvertToPair(source, FormTarget.WinForms));
            Assert.That(thrown!.Message, Does.Contain(UnknownDock));
        });
    }

    /// <summary>
    /// A strip's bad Dock crosses in BOTH directions as a Degraded value, like every other bad catalog value — never a
    /// refusal, never the "cannot happen" InvalidOperationException — and docks at the row default on the far side.
    /// </summary>
    [TestCase(FormTarget.Web)]
    [TestCase(FormTarget.WinForms)]
    public void AStripDockedLeft_IsCarriedByTheRetarget_AsDegraded(FormTarget to)
    {
        var source = to == FormTarget.Web
            ? Read(WinFormsStripDocked("StatusStrip", "Left"), FormTarget.WinForms)
            : Read("""
                <WebForm Name="DockForm" Version="1" Width="640" Height="400">
                  <Layout Kind="Canvas"/>
                  <Controls><StatusStrip Id="strip" Dock="Left"/></Controls>
                </WebForm>
                """, FormTarget.Web);

        Assert.That(FormRetarget.Refusals(source, to), Is.Empty);

        FormRetargetPair pair = null!;
        Assert.DoesNotThrow(() => pair = FormRetarget.ConvertToPair(source, to));

        var carried = FormDocumentReader.Read(Path.Combine(Path.GetTempPath(), pair.DocumentFileName), pair.DocumentText);
        Assert.Multiple(() =>
        {
            Assert.That(carried.IsRefused, Is.False);
            Assert.That(carried.Model.FindById("strip")!.Properties["Dock"], Is.EqualTo("Left"), "carried as written");
            Assert.That(carried.Degraded.Select(d => $"{d.ControlId}.{d.Property}"), Does.Contain("strip.Dock"),
                "…and Degraded on the far side too");
            Assert.That(FormDockLayout.EdgeOf(carried.Model.FindById("strip")!), Is.EqualTo(FormDockEdge.Bottom));
            Assert.That(pair.CodeText, Does.Not.Contain("strip.Dock"), "never emitted");
        });
    }

    /// <summary>A window's positioned Dock never reaches a Grid page — it becomes a cell (BL8025) — so nothing is refused.</summary>
    [Test]
    public void AWindowWithAnUnknownPositionedDock_RetargetsToTheWeb_WithoutARefusal()
    {
        var source = Read(WinFormsButtonDocked("Rigth"), FormTarget.WinForms);

        Assert.That(FormRetarget.Refusals(source, FormTarget.Web), Is.Empty);
    }

    // ==================================================================
    // design --check tells the user what the save would refuse
    // ==================================================================

    [Test]
    public void DesignCheck_ReportsAnUnknownDock_AndAnUnknownAnchor_AsErrors()
    {
        var findings = DesignCheck.CheckFormDocument(Path.Combine(Path.GetTempPath(), "DockForm.blform"), """
            <Form Name="DockForm" Version="1" Width="400" Height="300" Text="D">
              <Controls>
                <Button Id="btnA" X="8" Y="8" Width="75" Height="23" Dock="Rigth" TabIndex="0"/>
                <Button Id="btnB" X="8" Y="40" Width="75" Height="23" Anchor="Top,Sideways" TabIndex="1"/>
              </Controls>
            </Form>
            """);

        Assert.Multiple(() =>
        {
            Assert.That(findings.Where(f => f.Code == UnknownDock && !f.IsWarning).Select(f => f.Message),
                Has.One.Contains("'btnA'"));
            Assert.That(findings.Where(f => f.Code == DesignCodes.AnchorNotExpressible && !f.IsWarning).Select(f => f.Message),
                Has.One.Contains("'btnB'"));
        });
    }

    [Test]
    public void DesignCheck_AcceptsACanonicalisableDock()
    {
        var findings = DesignCheck.CheckFormDocument(
            Path.Combine(Path.GetTempPath(), "DockForm.blform"), WinFormsButtonDocked(" fill "));

        Assert.That(findings.Where(f => !f.IsWarning), Is.Empty,
            string.Join("; ", findings.Select(f => f.Format())));
    }

    // ==================================================================
    // ⛔⛔ csc — text proves nothing for a WinForms member
    // ==================================================================

    [TestCase("fill")]
    [TestCase(" right ")]
    [TestCase("NONE")]
    [TestCase("Bottom")]
    [Category("Integration")]
    public void TheCanonicalisedDock_Compiles(string dock)
    {
        var written = Write(Read(WinFormsButtonDocked(dock), FormTarget.WinForms));
        Assert.That(written.Refused, Is.False, Findings(written));

        WinFormsCompile.AssertCompiles(
            WinFormsCatalogSweepTests.CompileToCSharp(written.Text),
            $"a control docked '{dock}' must produce C# csc accepts");
    }

    [TestCase("StatusStrip", "bottom")]
    [TestCase("MenuStrip", "TOP")]
    [TestCase("StatusStrip", "Left")]
    [Category("Integration")]
    public void AStripsCaseVariant_Compiles(string kind, string dock)
    {
        var written = Write(Read(WinFormsStripDocked(kind, dock), FormTarget.WinForms));
        Assert.That(written.Refused, Is.False, Findings(written));

        WinFormsCompile.AssertCompiles(
            WinFormsCatalogSweepTests.CompileToCSharp(written.Text),
            $"a {kind} docked '{dock}' must produce C# csc accepts");
    }
}
