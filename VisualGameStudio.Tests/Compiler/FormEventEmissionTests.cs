using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Property-grid slice 5, Task 2 (pre-flight D-3, D-7, D-12; ADR 0021): what the init region emits for the FORM's
/// own binds and for a FILTERED web event (KeyPress, Enter, Leave) — and that a page with neither is byte-identical to
/// what it was before.
/// </summary>
[TestFixture]
public class FormEventEmissionTests
{
    // ==================================================================
    // Fixtures
    // ==================================================================

    private static FormDocument WebForm(params FormControl[] controls)
    {
        var form = new FormDocument { Target = FormTarget.Web, Name = "F" };
        form.Controls.AddRange(controls);
        return form;
    }

    private static FormDocument WinForm(params FormControl[] controls)
    {
        var form = new FormDocument { Target = FormTarget.WinForms, Name = "F", Width = 400, Height = 300 };
        form.Controls.AddRange(controls);
        return form;
    }

    private static FormControl Web(string kind, string id, params (string Event, string Handler)[] binds)
    {
        var control = new FormControl { Kind = kind, Id = id, Geometry = new GridGeometry() };
        foreach (var (evt, handler) in binds)
        {
            control.Binds.Add(new FormBind { Event = evt, Handler = handler });
        }

        return control;
    }

    private static FormControl Pixel(string kind, string id, params (string Event, string Handler)[] binds)
    {
        var control = new FormControl
        {
            Kind = kind, Id = id, TabIndex = 0, Geometry = new PixelGeometry { X = 8, Y = 8, Width = 75, Height = 23 }
        };
        foreach (var (evt, handler) in binds)
        {
            control.Binds.Add(new FormBind { Event = evt, Handler = handler });
        }

        return control;
    }

    private static string Scaffold(FormTarget target) => FormScaffolder.Create("F", target).CodeText;

    private static RegionWriteResult Write(FormDocument form, string? code = null) =>
        RegionWriter.Write("F.bas", code ?? Scaffold(form.Target), form,
            form.Target == FormTarget.Web ? "F.blwebform" : "F.blform");

    private static string Clean(RegionWriteResult result)
    {
        Assert.That(result.Refused, Is.False, string.Join("; ", result.Diagnostics.Select(d => d.Format())));
        return result.Text;
    }

    /// <summary>The lines of the generated <c>InitializeComponent</c>, trimmed, from its header to its <c>End Sub</c>.</summary>
    private static List<string> InitLines(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).ToList();
        var start = lines.IndexOf("Private Sub InitializeComponent()");
        Assert.That(start, Is.GreaterThanOrEqualTo(0), "no InitializeComponent in:\n" + text);
        var end = lines.FindIndex(start, l => l == "End Sub");
        return lines.GetRange(start, end - start + 1);
    }

    /// <summary>A Sub declared BELOW the designer's init region (after the region, before End Class).</summary>
    private static string WithSubBelowTheRegion(string code, string declaration)
    {
        var at = code.LastIndexOf("End Class", StringComparison.Ordinal);
        return code[..at] + "    " + declaration + "\n    End Sub\n\n" + code[at..];
    }

    // ==================================================================
    // The Form's binds — WinForms
    // ==================================================================

    /// <summary>
    /// Rewrites slice 1's <c>ARootBind_IsWarned_NotEmitted_UntilFormEventsExist</c>: a root bind is now WIRED, as VS
    /// wires <c>this.Load += …</c> — the last statement of InitializeComponent, after the reference rows.
    /// </summary>
    [Test]
    public void ARootBind_IsWired_AsTheLastStatement_OnWinForms()
    {
        var form = WinForm(Pixel("Button", "btnOk"));
        form.Properties["AcceptButton"] = "btnOk";
        form.Binds.Add(new FormBind { Event = "Load", Handler = "F_Load" });

        var result = Write(form);
        var init = InitLines(Clean(result));

        Assert.Multiple(() =>
        {
            Assert.That(init[^2], Is.EqualTo("AddHandler Me.Load, AddressOf F_Load"));
            Assert.That(init.IndexOf("AddHandler Me.Load, AddressOf F_Load"),
                Is.GreaterThan(init.IndexOf("Me.AcceptButton = btnOk")));
            Assert.That(result.Diagnostics.Select(d => d.Code), Has.No.Member(DesignCodes.BindNotOnTarget),
                "the slice-1 'not generated yet' warning is gone");
        });
    }

    [Test]
    public void AWinFormsRootBind_OnAWinFormsOnlyEvent_IsWired_WithTheDocumentsSpelling()
    {
        var form = WinForm();
        form.Binds.Add(new FormBind { Event = "FormClosing", Handler = "F_FormClosing" });

        Assert.That(InitLines(Clean(Write(form))), Does.Contain("AddHandler Me.FormClosing, AddressOf F_FormClosing"));
    }

    // ==================================================================
    // The Form's binds — the page
    // ==================================================================

    /// <summary>
    /// D-3: Load is a CALL, Me.-qualified (the JS unqualified-self-call trap), and the very last statement — after
    /// every getElementById, so a handler that touches a control finds it.
    /// </summary>
    [Test]
    public void TheWebLoad_IsAMeQualifiedCall_LastInInitializeComponent()
    {
        var form = WebForm(Web("Button", "btn", ("click", "btn_Click")));
        form.Binds.Add(new FormBind { Event = "load", Handler = "F_Load" });

        var init = InitLines(Clean(Write(form)));

        Assert.Multiple(() =>
        {
            Assert.That(init[^2], Is.EqualTo("Me.F_Load()"));
            Assert.That(init.FindLastIndex(l => l.Contains("getElementById")), Is.LessThan(init.Count - 2));
            Assert.That(init.Any(l => l.Contains("\"load\"")), Is.False, "never a load listener — it can fire before");
        });
    }

    [Test]
    public void AWebRootKeyBind_ListensOnTheBody()
    {
        var form = WebForm(Web("Button", "btn"));
        form.Binds.Add(new FormBind { Event = "keydown", Handler = "F_KeyDown" });

        var init = InitLines(Clean(Write(form)));

        Assert.Multiple(() =>
        {
            Assert.That(init, Does.Contain("doc.body.addEventListener(\"keydown\", AddressOf F_KeyDown)"));
            Assert.That(init.IndexOf("doc.body.addEventListener(\"keydown\", AddressOf F_KeyDown)"),
                Is.GreaterThan(init.FindLastIndex(l => l.Contains("getElementById"))), "after the controls");
        });
    }

    [Test]
    public void AWebRootBind_UsesTheCatalogsSpelling_NotTheDocuments()
    {
        var form = WebForm();
        form.Binds.Add(new FormBind { Event = "Click", Handler = "F_Click" });

        Assert.That(InitLines(Clean(Write(form))), Does.Contain("doc.body.addEventListener(\"click\", AddressOf F_Click)"));
    }

    [Test]
    public void AWebRootResizeBind_ListensOnTheWindow_AndDeclaresW()
    {
        var form = WebForm();
        form.Binds.Add(new FormBind { Event = "resize", Handler = "F_Resize" });

        var init = InitLines(Clean(Write(form)));

        Assert.Multiple(() =>
        {
            Assert.That(init, Does.Contain("Dim w As Window = ::window"));
            Assert.That(init, Does.Contain("w.addEventListener(\"resize\", AddressOf F_Resize)"));
            Assert.That(init.Any(l => l.Contains("doc.body.addEventListener(\"resize\"")), Is.False);
        });
    }

    /// <summary>Every existing page's region stays byte-identical: no root bind on the window, no <c>w</c>.</summary>
    [Test]
    public void APageWithoutWindowBinds_DeclaresNoW_AndNoWrapper()
    {
        var form = WebForm(Web("Button", "btn", ("click", "btn_Click")));
        form.Binds.Add(new FormBind { Event = "click", Handler = "F_Click" });
        form.Binds.Add(new FormBind { Event = "load", Handler = "F_Load" });

        var text = Clean(Write(form));

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Not.Contain("Dim w As Window"));
            Assert.That(text, Does.Not.Contain("VgsOn_"));
        });
    }

    /// <summary>D-7: a root bind on an event the Form does not wire on the page is REFUSED (BL8032), not warned.</summary>
    [Test]
    public void AWebRootBind_OnShown_IsRefusedAsBL8032_NamingTheForm()
    {
        var form = WebForm();
        form.Binds.Add(new FormBind { Event = "Shown", Handler = "F_Shown" });
        var source = Scaffold(FormTarget.Web);

        var result = Write(form, source);

        Assert.Multiple(() =>
        {
            Assert.That(result.Refused, Is.True);
            var finding = result.Diagnostics.Single(d => d.Code == DesignCodes.UnknownWebEvent);
            Assert.That(finding.Message, Does.Contain("'form'").And.Contain("Shown").And.Contain("F_Shown")
                .And.Contain("'load', 'resize', 'click'"), "names the owner and lists the Form's web events, comma-joined");
            Assert.That(result.Diagnostics.Select(d => d.Code), Has.No.Member(DesignCodes.BindNotOnTarget));
            Assert.That(result.Text, Is.EqualTo(source), "a refusal writes nothing");
        });
    }

    [Test]
    public void TheControlsBL8032Message_IsCommaJoined()
    {
        var result = Write(WebForm(Web("Button", "btn", ("mouseover", "btn_Over"))));

        Assert.That(result.Diagnostics.Single(d => d.Code == DesignCodes.UnknownWebEvent).Message,
            Does.Contain("'click', 'mousedown', 'mouseup'"));
    }

    // ==================================================================
    // BL8013 for the root (D-7)
    // ==================================================================

    [Test]
    public void AWebRootKeyHandler_DeclaredBelowTheRegion_IsBL8013()
    {
        var form = WebForm();
        form.Binds.Add(new FormBind { Event = "keydown", Handler = "F_KeyDown" });

        var result = Write(form, WithSubBelowTheRegion(Scaffold(FormTarget.Web), "Private Sub F_KeyDown(e As DomEvent)"));

        Assert.That(result.Diagnostics.Select(d => d.Code), Does.Contain(DesignCodes.HandlerDeclaredAfterWiring),
            "the body listener names the handler through AddressOf — the erasure bites");
    }

    [Test]
    public void AWebLoadHandler_DeclaredBelow_IsNot()
    {
        var form = WebForm();
        form.Binds.Add(new FormBind { Event = "load", Handler = "F_Load" });

        var result = Write(form, WithSubBelowTheRegion(Scaffold(FormTarget.Web), "Private Sub F_Load()"));

        Assert.That(result.Diagnostics.Select(d => d.Code), Has.No.Member(DesignCodes.HandlerDeclaredAfterWiring),
            "Load is a direct call, which the AddressOf erasure does not bite (M2)");
    }

    // ==================================================================
    // D-12 — the filtered listener is a generated wrapper Sub
    // ==================================================================

    /// <summary>M7's measured text, exactly, ABOVE InitializeComponent — and the listener is a keydown, never keypress.</summary>
    [Test]
    public void AWebKeyPressBind_EmitsTheKeyFilterWrapper_AboveInitializeComponent()
    {
        var text = Clean(Write(WebForm(Web("TextBox", "txt", ("keypress", "txt_KeyPress"))))).Replace("\r\n", "\n");

        // Review fix 3: the length is counted in CODE POINTS (an emoji key is one character, two UTF-16 units).
        const string wrapper =
            "    Private Sub VgsOn_txt_KeyPress(e As DomEvent)\n" +
            "        Dim k As String = e.key\n" +
            "        Dim n As Integer = ::Array.from(k).length\n" +
            "        If n = 1 OrElse k = \"Enter\" OrElse k = \"Backspace\" OrElse k = \"Escape\" Then\n" +
            "            Me.txt_KeyPress(e)\n" +
            "        End If\n" +
            "    End Sub\n";

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain(wrapper));
            Assert.That(text.IndexOf(wrapper, StringComparison.Ordinal),
                Is.LessThan(text.IndexOf("Private Sub InitializeComponent()", StringComparison.Ordinal)));
            Assert.That(InitLines(text), Does.Contain("txt.addEventListener(\"keydown\", AddressOf VgsOn_txt_KeyPress)"));
            Assert.That(text, Does.Not.Contain("\"keypress\""));
        });
    }

    [Test]
    public void AWebPanelEnterAndLeave_EmitTheRelatedTargetWrapper_OnFocusinAndFocusout()
    {
        var text = Clean(Write(WebForm(Web("Panel", "pnl", ("focusin", "pnl_Enter"), ("focusout", "pnl_Leave")))))
            .Replace("\r\n", "\n");

        const string enter =
            "    Private Sub VgsOn_pnl_Enter(e As DomEvent)\n" +
            "        If e.relatedTarget Is Nothing OrElse Not e.currentTarget.contains(e.relatedTarget) Then\n" +
            "            Me.pnl_Enter(e)\n" +
            "        End If\n" +
            "    End Sub\n";

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain(enter));
            Assert.That(text, Does.Contain("Private Sub VgsOn_pnl_Leave(e As DomEvent)"));
            Assert.That(text, Does.Contain("            Me.pnl_Leave(e)\n"));
            var init = InitLines(text);
            Assert.That(init, Does.Contain("pnl.addEventListener(\"focusin\", AddressOf VgsOn_pnl_Enter)"));
            Assert.That(init, Does.Contain("pnl.addEventListener(\"focusout\", AddressOf VgsOn_pnl_Leave)"));
        });
    }

    [Test]
    public void AWebRootKeyPress_WrapsOnTheBody()
    {
        var form = WebForm();
        form.Binds.Add(new FormBind { Event = "keypress", Handler = "F_KeyPress" });

        var text = Clean(Write(form));

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("Private Sub VgsOn_F_KeyPress(e As DomEvent)"));
            Assert.That(text, Does.Contain("Me.F_KeyPress(e)"));
            Assert.That(InitLines(text), Does.Contain("doc.body.addEventListener(\"keydown\", AddressOf VgsOn_F_KeyPress)"));
        });
    }

    /// <summary>
    /// The wrapper reaches the user's handler by a DIRECT call (no erasure, M2), so a wrapped handler declared below
    /// the region is fine; an unwrapped Click handler below it is still BL8013.
    /// </summary>
    [Test]
    public void AWrappedHandlerBelowTheRegion_IsNotBL8013_ButAClickHandlerStillIs()
    {
        var wrapped = Write(WebForm(Web("TextBox", "txt", ("keypress", "txt_KeyPress"))),
            WithSubBelowTheRegion(Scaffold(FormTarget.Web), "Private Sub txt_KeyPress(e As DomEvent)"));
        var plain = Write(WebForm(Web("TextBox", "txt", ("click", "txt_Click"))),
            WithSubBelowTheRegion(Scaffold(FormTarget.Web), "Private Sub txt_Click(e As DomEvent)"));

        Assert.Multiple(() =>
        {
            Assert.That(wrapped.Diagnostics.Select(d => d.Code), Has.No.Member(DesignCodes.HandlerDeclaredAfterWiring));
            Assert.That(plain.Diagnostics.Select(d => d.Code), Does.Contain(DesignCodes.HandlerDeclaredAfterWiring));
        });
    }

    /// <summary>Task 3 (D-11): BL8013 finds the handler through the ONE case-insensitive scanner — another case is the same Sub.</summary>
    [Test]
    public void BL8013_FindsAHandlerDeclaredInAnotherCase()
    {
        var result = Write(WebForm(Web("TextBox", "txt", ("click", "txt_Click"))),
            WithSubBelowTheRegion(Scaffold(FormTarget.Web), "Private Sub TXT_CLICK(e As DomEvent)"));

        Assert.That(result.Diagnostics.Select(d => d.Code), Does.Contain(DesignCodes.HandlerDeclaredAfterWiring));
    }

    [Test]
    public void TwoHandlersOnOneFilteredEvent_ShareOneWrapper_AndOneListener()
    {
        var text = Clean(Write(WebForm(Web("TextBox", "txt", ("keypress", "a_KeyPress"), ("keypress", "b_KeyPress")))));

        Assert.Multiple(() =>
        {
            Assert.That(text.Split("Private Sub VgsOn_txt_KeyPress(").Length - 1, Is.EqualTo(1), "one wrapper — two would be a duplicate member");
            Assert.That(text, Does.Contain("Me.a_KeyPress(e)").And.Contain("Me.b_KeyPress(e)"));
            Assert.That(InitLines(text).Count(l => l.Contains("AddressOf VgsOn_txt_KeyPress")), Is.EqualTo(1));
        });
    }

    /// <summary>
    /// Review fix 1: WinForms ALWAYS raises KeyDown before KeyPress (suppressing a KeyPress from KeyDown relies on it).
    /// Both listen to the DOM's <c>keydown</c> on one target, so the DOM runs them in LISTENER order — which must not
    /// be document order. Binds stored in REVERSE order still emit KeyDown's listener first, on a control and the Form.
    /// </summary>
    [Test]
    public void KeyDownsListener_PrecedesTheKeyPressWrapper_EvenWhenTheBindsAreStoredInReverse()
    {
        var form = WebForm(Web("TextBox", "txt", ("keypress", "txt_KeyPress"), ("keydown", "txt_KeyDown")));
        form.Binds.Add(new FormBind { Event = "keypress", Handler = "F_KeyPress" });
        form.Binds.Add(new FormBind { Event = "keydown", Handler = "F_KeyDown" });

        var init = InitLines(Clean(Write(form)));

        Assert.Multiple(() =>
        {
            Assert.That(init.IndexOf("txt.addEventListener(\"keydown\", AddressOf txt_KeyDown)"),
                Is.GreaterThan(0).And.LessThan(init.IndexOf("txt.addEventListener(\"keydown\", AddressOf VgsOn_txt_KeyPress)")));
            Assert.That(init.IndexOf("doc.body.addEventListener(\"keydown\", AddressOf F_KeyDown)"),
                Is.GreaterThan(0).And.LessThan(init.IndexOf("doc.body.addEventListener(\"keydown\", AddressOf VgsOn_F_KeyPress)")));
        });
    }

    // ==================================================================
    // Review fix 2 — a control named like the form
    // ==================================================================

    /// <summary>
    /// A control whose Id equals the form's class name is refused (BL8017, the duplicate-id code): on WinForms it is a
    /// member named like its enclosing type (CS0542), and on the page its wrapper name would collide with the Form's
    /// (<c>VgsOn_F_KeyPress</c> twice). Case-insensitive, as BasicLang names are.
    /// </summary>
    [TestCase(FormTarget.Web, "F")]
    [TestCase(FormTarget.Web, "f")]
    [TestCase(FormTarget.WinForms, "F")]
    public void AControlNamedLikeTheForm_IsRefused_BeforeAnythingIsWritten(FormTarget target, string id)
    {
        var form = target == FormTarget.Web
            ? WebForm(Web("TextBox", id, ("keypress", "a_KeyPress")))
            : WinForm(Pixel("TextBox", id));
        if (target == FormTarget.Web)
        {
            form.Binds.Add(new FormBind { Event = "keypress", Handler = "b_KeyPress" });
        }

        var source = Scaffold(target);
        var result = Write(form, source);

        Assert.Multiple(() =>
        {
            Assert.That(result.Refused, Is.True);
            Assert.That(result.Diagnostics.Single(d => d.Code == DesignCodes.DuplicateControlId).Message,
                Does.Contain($"'{id}'").And.Contain("form"));
            Assert.That(result.Text, Is.EqualTo(source));
        });
    }

    [TestCase("F.blform", "<Form Name=\"F\" Version=\"1\"><Controls><Button Id=\"F\" X=\"8\" Y=\"8\" Width=\"75\" Height=\"23\" TabIndex=\"0\"/></Controls></Form>")]
    [TestCase("F.blwebform", "<WebForm Name=\"F\" Version=\"1\"><Layout Kind=\"Grid\"/><Controls><Button Id=\"f\" Col=\"0\" Row=\"0\" TabIndex=\"0\"/></Controls></WebForm>")]
    public void TheReader_RefusesAControlNamedLikeTheForm(string fileName, string xml)
    {
        var file = BasicLang.Forms.Serialization.FormDocumentReader.Read(fileName, xml);

        Assert.That(file.Diagnostics.Where(d => !d.IsWarning).Select(d => d.Code), Does.Contain(DesignCodes.DuplicateControlId));
    }

    [Test]
    public void AControlNamedOtherwise_IsNotRefused() =>
        Assert.That(Write(WebForm(Web("TextBox", "Form1"))).Refused, Is.False);

    /// <summary>WinForms raises KeyPress itself: a plain AddHandler, no wrapper.</summary>
    [Test]
    public void AWinFormsKeyPress_IsAPlainAddHandler()
    {
        var text = Clean(Write(WinForm(Pixel("TextBox", "txt", ("KeyPress", "txt_KeyPress")))));

        Assert.Multiple(() =>
        {
            Assert.That(InitLines(text), Does.Contain("AddHandler txt.KeyPress, AddressOf txt_KeyPress"));
            Assert.That(text, Does.Not.Contain("VgsOn_"));
        });
    }

    // ==================================================================
    // The scaffold says where Load runs (D-3's user-facing consequence)
    // ==================================================================

    [Test]
    public void TheWebScaffold_SaysLoadRunsAtTheEndOfInitializeComponent_AndTheWinFormsOneDoesNot()
    {
        var web = Scaffold(FormTarget.Web).Replace("\r\n", "\n");
        var winForms = Scaffold(FormTarget.WinForms);

        Assert.Multiple(() =>
        {
            Assert.That(web, Does.Contain(
                "        Me.InitializeComponent()\n" +
                "        ' On a web page, Form Load runs at the end of InitializeComponent: code after this line runs after Load.\n" +
                "        ' On WinForms Load runs later, when the form is shown.\n"));
            Assert.That(winForms, Does.Not.Contain("Form Load runs"));
        });
    }
}
