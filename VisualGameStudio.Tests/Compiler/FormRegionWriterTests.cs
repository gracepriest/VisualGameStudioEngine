using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 11: the region writer (D1).
///
/// <para>⛔ This is the code that edits a file the USER owns. The refusal tests matter more than the
/// generation tests: a writer that generates slightly wrong code produces a compile error someone
/// will notice, while a writer that regenerates over a hand edit destroys work silently.</para>
/// </summary>
[TestFixture]
public class FormRegionWriterTests
{
    private static FormDocument WebLoginForm()
    {
        var form = new FormDocument { Target = FormTarget.Web, Name = "LoginForm" };
        var button = new FormControl { Kind = "Button", Id = "btnLogin", TabIndex = 0 };
        button.Properties["Text"] = "Sign in";
        button.Binds.Add(new FormBind { Event = "click", Handler = "btnLogin_Click" });
        form.Controls.Add(button);
        return form;
    }

    private static FormDocument WinFormsLoginForm()
    {
        var form = new FormDocument { Target = FormTarget.WinForms, Name = "LoginForm" };
        var button = new FormControl { Kind = "Button", Id = "btnLogin", TabIndex = 0 };
        button.Properties["Text"] = "\"Sign in\"";
        button.Binds.Add(new FormBind { Event = "Click", Handler = "btnLogin_Click" });
        form.Controls.Add(button);
        return form;
    }

    /// <summary>
    /// A file with both regions, hashes correct for EMPTY bodies — the state a freshly scaffolded
    /// file is in. Handler declared before the init region, per D8's ordering rule.
    /// </summary>
    private static string ScaffoldedFile()
    {
        // ⛔ Normalised to LF. C# raw string literals take their line endings from the SOURCE FILE,
        // and .gitattributes is `* text=auto`, so this fixture is CRLF on a Windows checkout and LF
        // on Linux. Two tests below search it for an LF-spelled needle; on CRLF the Replace would
        // silently no-op, the region would stay Canon, and the tests would fail for a reason that
        // has nothing to do with what they are testing.
        var emptyHash = RegionMarkers.HashContent("");
        return Lf($"""
            Public Class LoginForm

            {RegionMarkers.FormatOpen("controls", "LoginForm.blwebform", emptyHash, "    ")}
            {RegionMarkers.FormatClose("    ")}

                Public Sub New()
                    InitializeComponent()
                End Sub

                Private Sub btnLogin_Click(e As DomEvent)
                End Sub

            {RegionMarkers.FormatOpen("init", "LoginForm.blwebform", emptyHash, "    ")}
            {RegionMarkers.FormatClose("    ")}

            End Class

            """);
    }

    /// <summary>Line-ending normalisation, so a fixture means the same thing on both checkouts.</summary>
    private static string Lf(string text) => text.Replace("\r\n", "\n");

    // ==================================================================
    // Scanning and classification
    // ==================================================================

    [Test]
    public void Scan_ClassifiesAMatchingRegionAsCanon()
    {
        var regions = RegionMarkers.Scan(ScaffoldedFile());

        Assert.That(regions.Select(r => r.Name), Is.EquivalentTo(new[] { "controls", "init" }));
        Assert.That(regions, Has.All.Property(nameof(FormRegion.State)).EqualTo(RegionState.Canon));
    }

    [Test]
    public void Scan_ClassifiesAHandEditedRegionAsHashMismatch()
    {
        var source = ScaffoldedFile().Replace(
            RegionMarkers.FormatClose("    ") + "\n\n    Public Sub New()",
            "        Private sneaky As Element\n" + RegionMarkers.FormatClose("    ") + "\n\n    Public Sub New()");

        var controls = RegionMarkers.Find(RegionMarkers.Scan(source), "controls")!;
        Assert.That(controls.State, Is.EqualTo(RegionState.HashMismatch),
            "content inside the region no longer hashes to what the marker claims");
    }

    [Test]
    public void Scan_ClassifiesAnUnclosedRegionAsMalformed()
    {
        var source = $"""
            Public Class F
            {RegionMarkers.FormatOpen("controls", "F.blwebform", "0000", "    ")}
                Private x As Element
            End Class
            """;

        Assert.That(RegionMarkers.Scan(source).Single().State, Is.EqualTo(RegionState.Malformed));
    }

    [Test]
    public void Scan_ClassifiesADuplicatedRegionAsMalformed()
    {
        // Individually balanced, but regenerating would have to pick one and silently orphan the other.
        var empty = RegionMarkers.HashContent("");
        var source = $"""
            Public Class F
            {RegionMarkers.FormatOpen("controls", "F.blwebform", empty, "    ")}
            {RegionMarkers.FormatClose("    ")}
            {RegionMarkers.FormatOpen("controls", "F.blwebform", empty, "    ")}
            {RegionMarkers.FormatClose("    ")}
            End Class
            """;

        Assert.That(RegionMarkers.Scan(source), Has.All.Property(nameof(FormRegion.State))
            .EqualTo(RegionState.Malformed));
    }

    [Test]
    public void Scan_ClassifiesAStrayCloseAsMalformed()
    {
        Assert.That(RegionMarkers.Scan("Public Class F\n    ' </vgs:designer>\nEnd Class").Single().State,
            Is.EqualTo(RegionState.Malformed),
            "a marker the user can see must be reported, not silently ignored");
    }

    [Test]
    public void Hash_IsStableAcrossLineEndings()
    {
        // ⛔ Without normalisation the same content hashes differently on a CRLF and an LF checkout,
        // every region reads as hand-edited, and every form silently opens read-only — a failure
        // that would look like data corruption and be nothing of the kind.
        Assert.That(RegionMarkers.HashContent("a\r\nb\r\n"), Is.EqualTo(RegionMarkers.HashContent("a\nb\n")));
    }

    [Test]
    public void Hash_IgnoresTrailingWhitespace()
    {
        // Editors strip it on save; a hash that flips when an editor tidies the file is useless.
        Assert.That(RegionMarkers.HashContent("a   \nb\t\n"), Is.EqualTo(RegionMarkers.HashContent("a\nb\n")));
    }

    // ==================================================================
    // Refusals — the part that protects the user's file
    // ==================================================================

    [Test]
    public void Write_RefusesAndChangesNothing_WhenARegionWasHandEdited()
    {
        var source = ScaffoldedFile().Replace(
            RegionMarkers.FormatClose("    ") + "\n\n    Public Sub New()",
            "        Private sneaky As Element\n" + RegionMarkers.FormatClose("    ") + "\n\n    Public Sub New()");

        var result = RegionWriter.Write("LoginForm.bas", source, WebLoginForm(), "LoginForm.blwebform");

        Assert.Multiple(() =>
        {
            Assert.That(result.Refused, Is.True);
            Assert.That(result.Changed, Is.False);
            Assert.That(result.Text, Is.EqualTo(source), "not one byte of the user's file may move");
            Assert.That(result.Text, Does.Contain("sneaky"), "the hand edit survives untouched");
            Assert.That(result.Diagnostics.Single().Code, Is.EqualTo(DesignCodes.RegionHandEdited));
            Assert.That(result.Diagnostics.Single().Message, Does.Contain("read-only"),
                "the diagnostic must say what happens and how to resolve it");
        });
    }

    [Test]
    public void Write_RefusesAndChangesNothing_WhenMarkersAreMalformed()
    {
        var source = $"""
            Public Class LoginForm
            {RegionMarkers.FormatOpen("controls", "LoginForm.blwebform", "0000", "    ")}
                Private btnLogin As Element
            End Class
            """;

        var result = RegionWriter.Write("LoginForm.bas", source, WebLoginForm(), "LoginForm.blwebform");

        Assert.Multiple(() =>
        {
            Assert.That(result.Refused, Is.True);
            Assert.That(result.Text, Is.EqualTo(source));
            Assert.That(result.Diagnostics.Single().Code, Is.EqualTo(DesignCodes.RegionMalformed));
        });
    }

    [Test]
    public void Write_WritesNothing_AndDoesNotRefuse_WhenThereAreNoRegionsAtAll()
    {
        // No markers is the IMPORT case, not an error (D1).
        var source = "Public Class LoginForm\nEnd Class\n";

        var result = RegionWriter.Write("LoginForm.bas", source, WebLoginForm(), "LoginForm.blwebform");

        Assert.Multiple(() =>
        {
            Assert.That(result.Changed, Is.False);
            Assert.That(result.Refused, Is.False, "nothing to do is not a refusal");
            Assert.That(result.Diagnostics.Single().Code, Is.EqualTo(DesignCodes.RegionAbsent));
            Assert.That(result.Diagnostics.Single().IsWarning, Is.True);
        });
    }

    // ==================================================================
    // Generation — web
    // ==================================================================

    [Test]
    public void Write_Web_GeneratesBothRegions_AndLeavesUserCodeUntouched()
    {
        var result = RegionWriter.Write("LoginForm.bas", ScaffoldedFile(), WebLoginForm(), "LoginForm.blwebform");

        Assert.That(result.Refused, Is.False, string.Join("; ", result.Diagnostics.Select(d => d.Format())));
        Assert.That(result.Changed, Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(result.Text, Does.Contain("Private btnLogin As Element"));
            Assert.That(result.Text, Does.Contain("Dim doc As Document = ::document"));
            Assert.That(result.Text, Does.Contain("""btnLogin = doc.getElementById("btnLogin")"""));
            Assert.That(result.Text, Does.Contain("""btnLogin.addEventListener("click", AddressOf btnLogin_Click)"""));

            // The user's own code, byte for byte.
            Assert.That(result.Text, Does.Contain("    Public Sub New()"));
            Assert.That(result.Text, Does.Contain("        InitializeComponent()"));
            Assert.That(result.Text, Does.Contain("    Private Sub btnLogin_Click(e As DomEvent)"));
        });
    }

    [Test]
    public void Write_Web_NeverEmitsAddHandlerAgainstADomReceiver()
    {
        // ⛔ TryEventCall emits `{recv}.add(handler)` unconditionally, so `AddHandler el.click, …`
        // becomes `el.click.add(H)` → a runtime TypeError. It compiles green; only the browser
        // complains.
        var result = RegionWriter.Write("LoginForm.bas", ScaffoldedFile(), WebLoginForm(), "LoginForm.blwebform");

        Assert.That(result.Text, Does.Not.Contain("AddHandler"),
            "the web target wires events with addEventListener, never AddHandler");
    }

    [Test]
    public void Write_NeverEmitsWithOrHandles()
    {
        // ⛔ `.Prop = value` inside a With block is SILENTLY DISCARDED by the IR builder, so the
        // running program would not set what the designer shows. `Handles` is lexed but never
        // parsed, so a file using it does not build at all.
        var web = RegionWriter.Write("LoginForm.bas", ScaffoldedFile(), WebLoginForm(), "LoginForm.blwebform");
        var winforms = RegionWriter.Write("LoginForm.bas", ScaffoldedFile(), WinFormsLoginForm(), "LoginForm.blform");

        foreach (var text in new[] { web.Text, winforms.Text })
        {
            Assert.Multiple(() =>
            {
                Assert.That(text, Does.Not.Contain("With "));
                Assert.That(text, Does.Not.Contain("End With"));
                Assert.That(text, Does.Not.Contain("Handles "));
            });
        }
    }

    [Test]
    public void Write_Web_UsesAddressOf_NotALambda()
    {
        // Three measured green-build-then-fail shapes are all avoided by AddressOf with the handler
        // declared first: a lambda calling an unqualified method of the enclosing class emits a bare
        // identifier (ReferenceError); Me.Method() inside a lambda is a hard error; and a qualified
        // module call has no JS container (ReferenceError).
        var result = RegionWriter.Write("LoginForm.bas", ScaffoldedFile(), WebLoginForm(), "LoginForm.blwebform");

        Assert.Multiple(() =>
        {
            Assert.That(result.Text, Does.Contain("AddressOf btnLogin_Click"));
            Assert.That(result.Text, Does.Not.Contain("Sub(e "), "no inline lambda");
            Assert.That(result.Text, Does.Not.Contain("Me.btnLogin_Click"));
            Assert.That(result.Text, Does.Not.Contain("LoginForm.InitializeComponent"));
        });
    }

    // ==================================================================
    // Generation — WinForms
    // ==================================================================

    [Test]
    public void Write_WinForms_ConstructsSetsWiresAndParents_OneStatementEach()
    {
        var source = ScaffoldedFile().Replace("LoginForm.blwebform", "LoginForm.blform");
        var result = RegionWriter.Write("LoginForm.bas", source, WinFormsLoginForm(), "LoginForm.blform");

        Assert.That(result.Refused, Is.False, string.Join("; ", result.Diagnostics.Select(d => d.Format())));
        Assert.Multiple(() =>
        {
            Assert.That(result.Text, Does.Contain("Private btnLogin As Button"));
            Assert.That(result.Text, Does.Contain("btnLogin = New Button()"));
            Assert.That(result.Text, Does.Contain("""btnLogin.Text = "Sign in" """.TrimEnd()));
            Assert.That(result.Text, Does.Contain("AddHandler btnLogin.Click, AddressOf btnLogin_Click"));
            Assert.That(result.Text, Does.Contain("Me.Controls.Add(btnLogin)"));
        });
    }

    [Test]
    public void Write_UsesTheCatalogsWinFormsTypeName()
    {
        var form = new FormDocument { Target = FormTarget.WinForms, Name = "F" };
        form.Controls.Add(new FormControl { Kind = "PictureBox", Id = "picLogo" });
        var source = ScaffoldedFile().Replace("LoginForm.blwebform", "F.blform");

        var result = RegionWriter.Write("F.bas", source, form, "F.blform");

        Assert.That(result.Text, Does.Contain("Private picLogo As PictureBox"));
    }

    // ==================================================================
    // The algebra, and D8's ordering rule
    // ==================================================================

    [Test]
    public void Write_IsIdempotent()
    {
        var first = RegionWriter.Write("LoginForm.bas", ScaffoldedFile(), WebLoginForm(), "LoginForm.blwebform");
        var second = RegionWriter.Write("LoginForm.bas", first.Text, WebLoginForm(), "LoginForm.blwebform");

        Assert.Multiple(() =>
        {
            Assert.That(second.Refused, Is.False,
                "the hash the first write stamped must match the content it wrote, or the second " +
                "save would report the designer's own output as a hand edit");
            Assert.That(second.Changed, Is.False, "a no-op save writes nothing");
            Assert.That(second.Text, Is.EqualTo(first.Text));
        });
    }

    [Test]
    public void Write_DoesNotEmitADegradedValue_IntoTheUsersFile()
    {
        // ⛔⛔ A Degraded value is one the catalog knows and cannot parse. Splicing it into source
        // produces a file the user cannot build: `lbl.TextAlign = ContentAlignment.Bogus` is
        // CS0117, and `lbl.Enabled = maybe` does not even get past BasicLang. D9 requires the
        // value to survive in the DOCUMENT and to show frozen in the property grid; it says
        // nothing about emitting it, and emitting it breaks the build for a value the designer has
        // already told the user it cannot use.
        var form = new FormDocument { Target = FormTarget.WinForms, Name = "LoginForm" };
        var label = new FormControl { Kind = "Label", Id = "lbl", TabIndex = 0 };
        label.Properties["Text"] = "Hello";
        label.Properties["TextAlign"] = "Bogus";      // not an allowed value
        label.Properties["Enabled"] = "maybe";        // not a Bool
        form.Controls.Add(label);

        var source = ScaffoldedFile().Replace("LoginForm.blwebform", "LoginForm.blform");
        var result = RegionWriter.Write("LoginForm.bas", source, form, "LoginForm.blform");

        Assert.That(result.Refused, Is.False, "a degraded value is a warning, not a refusal");
        Assert.Multiple(() =>
        {
            Assert.That(result.Text, Does.Contain("""lbl.Text = "Hello" """.TrimEnd()),
                "the rest of the control is still written");
            Assert.That(result.Text, Does.Not.Contain("Bogus"));
            Assert.That(result.Text, Does.Not.Contain("maybe"));
            Assert.That(result.Diagnostics.Select(d => d.Code),
                Has.Exactly(2).EqualTo(DesignCodes.DegradedProperty),
                "and the user is told which values did not make it");
        });
    }

    [Test]
    public void Write_StillEmitsRecognizerSource_WhichNeverParsesAsACatalogValue()
    {
        // ⚠ The other half. The recognizer stores what it READ, so an imported form carries
        // `ContentAlignment.MiddleLeft` and `New Font("Segoe UI", 12)` — neither of which parses
        // as a catalog value. Treating those as degraded would silently strip every property off
        // every imported form.
        var form = new FormDocument { Target = FormTarget.WinForms, Name = "LoginForm" };
        var label = new FormControl { Kind = "Label", Id = "lbl", TabIndex = 0 };
        label.Properties["Text"] = "\"Already quoted\"";
        label.Properties["TextAlign"] = "ContentAlignment.MiddleLeft";
        form.Controls.Add(label);

        var source = ScaffoldedFile().Replace("LoginForm.blwebform", "LoginForm.blform");
        var result = RegionWriter.Write("LoginForm.bas", source, form, "LoginForm.blform");

        Assert.Multiple(() =>
        {
            Assert.That(result.Text, Does.Contain("lbl.TextAlign = ContentAlignment.MiddleLeft"));
            Assert.That(result.Text, Does.Contain("""lbl.Text = "Already quoted" """.TrimEnd()));
            Assert.That(result.Diagnostics.Select(d => d.Code),
                Has.None.EqualTo(DesignCodes.DegradedProperty));
        });
    }

    [Test]
    public void Write_WinForms_AcceptsAHandlerDeclaredAfterTheRegionThatWiresIt()
    {
        // ⛔⛔ THE OPPOSITE of the web rule below, and the shape Owner decision 3 makes canonical.
        // The shipped VSIX template declares btnClick_Click BELOW the InitializeComponent that
        // wires it. Measured 2026-09-13: that file compiles through BasicLang AND csc, and the
        // handler binds with its full parameter types (object sender, EventArgs e) — the event is
        // an unresolvable .NET member typed as Object, so there is no declared delegate for an
        // erased handler to mismatch.
        //
        // Refusing it would make the designer reject the template it is modelled on, and would
        // block the D12 import route for every existing WinForms file.
        var empty = RegionMarkers.HashContent("");
        var source = Lf($"""
            Public Class LoginForm
                Inherits Form
            {RegionMarkers.FormatOpen("controls", "LoginForm.blform", empty, "    ")}
            {RegionMarkers.FormatClose("    ")}

                Public Sub New()
                    InitializeComponent()
                End Sub

            {RegionMarkers.FormatOpen("init", "LoginForm.blform", empty, "    ")}
            {RegionMarkers.FormatClose("    ")}

                Private Sub btnLogin_Click(sender As Object, e As EventArgs)
                End Sub
            End Class
            """);

        var form = new FormDocument { Target = FormTarget.WinForms, Name = "LoginForm" };
        var button = new FormControl { Kind = "Button", Id = "btnLogin", TabIndex = 0 };
        button.Binds.Add(new FormBind { Event = "Click", Handler = "btnLogin_Click" });
        form.Controls.Add(button);

        var result = RegionWriter.Write("LoginForm.bas", source, form, "LoginForm.blform");

        Assert.That(result.Refused, Is.False,
            string.Join("; ", result.Diagnostics.Select(d => d.Format())));
        Assert.That(result.Text, Does.Contain("AddHandler btnLogin.Click, AddressOf btnLogin_Click"));
    }

    [Test]
    public void Write_RefusesWhenAHandlerIsDeclaredAfterTheRegionThatWiresIt()
    {
        // ⛔⛔ D8, and it is real — ON THE WEB. Measured 2026-09-13: AddressOf naming a
        // later-declared Sub erases its parameter types to Action(Of Object) and then hard-errors
        // against Action(Of DomEvent), because the DOM signature declares the parameter type.
        // The WinForms case above is the control: same shape, no declared delegate, compiles.
        // ⚠ The spec's own D1 worked example has this shape — it is illustrating the marker layout,
        // not the ordering — so a reader copying it gets a file that does not build on the web.
        var empty = RegionMarkers.HashContent("");
        var source = $"""
            Public Class LoginForm
            {RegionMarkers.FormatOpen("controls", "LoginForm.blwebform", empty, "    ")}
            {RegionMarkers.FormatClose("    ")}
            {RegionMarkers.FormatOpen("init", "LoginForm.blwebform", empty, "    ")}
            {RegionMarkers.FormatClose("    ")}

                Private Sub btnLogin_Click(e As DomEvent)
                End Sub
            End Class
            """;

        var result = RegionWriter.Write("LoginForm.bas", source, WebLoginForm(), "LoginForm.blwebform");

        Assert.Multiple(() =>
        {
            Assert.That(result.Refused, Is.True);
            Assert.That(result.Changed, Is.False);
            Assert.That(result.Diagnostics.Single().Code, Is.EqualTo(DesignCodes.HandlerDeclaredAfterWiring));
            Assert.That(result.Diagnostics.Single().Message, Does.Contain("Move the handler"));
        });
    }

    [Test]
    public void Write_DoesNotComplain_AboutAHandlerThatIsNotDeclaredAtAll()
    {
        // A missing handler is Task 15's check, not this one's — reporting it here would give two
        // diagnostics for one problem, with different codes.
        var empty = RegionMarkers.HashContent("");
        var source = $"""
            Public Class LoginForm
            {RegionMarkers.FormatOpen("controls", "LoginForm.blwebform", empty, "    ")}
            {RegionMarkers.FormatClose("    ")}
            {RegionMarkers.FormatOpen("init", "LoginForm.blwebform", empty, "    ")}
            {RegionMarkers.FormatClose("    ")}
            End Class
            """;

        var result = RegionWriter.Write("LoginForm.bas", source, WebLoginForm(), "LoginForm.blwebform");

        Assert.That(result.Refused, Is.False,
            string.Join("; ", result.Diagnostics.Select(d => d.Format())));
    }

    // ==================================================================
    // Regressions found by review
    // ==================================================================

    [Test]
    public void Write_WinForms_ParentsAChildToItsContainer_NotToTheForm()
    {
        // ⛔ Walking the flat AllControls() list emitted Me.Controls.Add(x) for EVERY control,
        // including nested ones — so a Button inside a Panel was added to the FORM and a nested
        // layout rendered flat at run time. A silent divergence between the designer view and the
        // running program, which is the whole class of bug D9 exists to prevent. The catalog ships
        // Panel and GroupBox as containers, so this is reachable from ordinary use.
        var form = new FormDocument { Target = FormTarget.WinForms, Name = "LoginForm" };
        var panel = new FormControl { Kind = "Panel", Id = "pnlBox", TabIndex = 0 };
        panel.Children.Add(new FormControl { Kind = "Label", Id = "lblInner", TabIndex = 1 });
        form.Controls.Add(panel);

        var source = ScaffoldedFile().Replace("LoginForm.blwebform", "LoginForm.blform");
        var result = RegionWriter.Write("LoginForm.bas", source, form, "LoginForm.blform");

        Assert.That(result.Refused, Is.False, string.Join("; ", result.Diagnostics.Select(d => d.Format())));
        Assert.Multiple(() =>
        {
            Assert.That(result.Text, Does.Contain("pnlBox.Controls.Add(lblInner)"),
                "the child belongs to its container");
            Assert.That(result.Text, Does.Not.Contain("Me.Controls.Add(lblInner)"),
                "and must NOT also be added to the form");
            Assert.That(result.Text, Does.Contain("Me.Controls.Add(pnlBox)"),
                "the top-level container is still the form's");
            Assert.That(result.Text.IndexOf("pnlBox.Controls.Add(lblInner)", StringComparison.Ordinal),
                Is.LessThan(result.Text.IndexOf("Me.Controls.Add(pnlBox)", StringComparison.Ordinal)),
                "a container is populated before it is added, like the shipped template");
        });
    }

    [Test]
    public void Write_WinForms_QuotesAStringPropertyThatIsNotAlreadySourceText()
    {
        // ⛔ The same Properties dictionary means two things to two consumers: the DOCUMENT reader
        // stores the raw XML attribute text (Sign in, unquoted), while this writer splices values
        // into generated SOURCE. Emitting the raw text produced `btnLogin.Text = Sign in` — a syntax
        // error. The recognizer meanwhile stores already-quoted source. Typing the formatting off
        // the catalog is what lets both feed the same writer.
        // ⛔ A TextBox, not a Button. The formatting is typed off THE CONTROL'S OWN catalog row, so
        // "an Int is bare" can only be demonstrated on a control that actually declares an Int:
        // MaxLength belongs to TextBox, and asking a Button for it returns null — which lands in the
        // untyped arm and quotes the value. That is correct behaviour being shown the wrong input.
        var form = new FormDocument { Target = FormTarget.WinForms, Name = "LoginForm" };
        var box = new FormControl { Kind = "TextBox", Id = "txtUser", TabIndex = 0 };
        box.Properties["Text"] = "Sign in";        // as a DOCUMENT would carry it
        box.Properties["Enabled"] = "false";
        box.Properties["MaxLength"] = "12";
        form.Controls.Add(box);

        var source = ScaffoldedFile().Replace("LoginForm.blwebform", "LoginForm.blform");
        var result = RegionWriter.Write("LoginForm.bas", source, form, "LoginForm.blform");

        Assert.Multiple(() =>
        {
            Assert.That(result.Text, Does.Contain("""txtUser.Text = "Sign in" """.TrimEnd()));
            Assert.That(result.Text, Does.Contain("txtUser.Enabled = False"), "a Bool is bare, and BasicLang-cased");
            Assert.That(result.Text, Does.Contain("txtUser.MaxLength = 12"), "an Int is bare");
        });
    }

    [Test]
    public void Write_WinForms_LeavesAlreadyQuotedSourceTextAlone()
    {
        // The recognizer's convention: it read `"Sign in"` from source and stores it with quotes.
        // Double-quoting it would emit `""Sign in""`.
        var source = ScaffoldedFile().Replace("LoginForm.blwebform", "LoginForm.blform");
        var result = RegionWriter.Write("LoginForm.bas", source, WinFormsLoginForm(), "LoginForm.blform");

        Assert.Multiple(() =>
        {
            Assert.That(result.Text, Does.Contain("""btnLogin.Text = "Sign in" """.TrimEnd()));
            Assert.That(result.Text, Does.Not.Contain("\"\"Sign in\"\""));
        });
    }

    [Test]
    public void Write_RefusesAHalfAdoptedFile_NamingTheMissingRegion()
    {
        // ⚠ Telling a user their file "has no designer regions" when it visibly has one is worse
        // than saying nothing, and writing only the half that exists would leave the declarations
        // and the wiring out of step.
        var empty = RegionMarkers.HashContent("");
        var source = Lf($"""
            Public Class LoginForm
            {RegionMarkers.FormatOpen("controls", "LoginForm.blwebform", empty, "    ")}
            {RegionMarkers.FormatClose("    ")}
            End Class
            """);

        var result = RegionWriter.Write("LoginForm.bas", source, WebLoginForm(), "LoginForm.blwebform");

        Assert.Multiple(() =>
        {
            Assert.That(result.Refused, Is.True, "half-adopted is not the import case");
            Assert.That(result.Changed, Is.False);
            Assert.That(result.Diagnostics.Single().Code, Is.EqualTo(DesignCodes.RegionMalformed));
            Assert.That(result.Diagnostics.Single().Message, Does.Contain("init"),
                "the diagnostic must name the region that is missing");
        });
    }

    [Test]
    public void Write_PreservesTheFilesLineEndings()
    {
        var crlf = ScaffoldedFile().Replace("\r\n", "\n").Replace("\n", "\r\n");

        var result = RegionWriter.Write("LoginForm.bas", crlf, WebLoginForm(), "LoginForm.blwebform");

        Assert.That(result.Text.Replace("\r\n", ""), Does.Not.Contain("\n"),
            "a region write must not leave bare LFs in a CRLF file");
    }

    [Test]
    public void Write_KeepsTheIndentationOfTheMarkersItFound()
    {
        var result = RegionWriter.Write("LoginForm.bas", ScaffoldedFile(), WebLoginForm(), "LoginForm.blwebform");

        Assert.Multiple(() =>
        {
            Assert.That(result.Text, Does.Contain("    ' <vgs:designer region=\"controls\""));
            Assert.That(result.Text, Does.Contain("    Private Sub InitializeComponent()"));
            Assert.That(result.Text, Does.Contain("        Dim doc As Document"));
        });
    }
}
