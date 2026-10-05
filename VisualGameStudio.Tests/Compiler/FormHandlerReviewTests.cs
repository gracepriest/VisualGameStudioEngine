using BasicLang.Forms;
using BasicLang.Forms.Serialization;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Slice 5, the review of Task 3 / the fix-2 correction (coordinator rulings 1–7, owner delegated, VS/VB behaviour): the
/// scanner reads only the FORM class's own Subs; ByRef never fits; ONE style and ONE call shape; a case-variant handler of
/// ANOTHER owner is never reused; Unbind is exact; the wrapper's locals cannot shadow a control.
/// </summary>
[TestFixture]
public class FormHandlerReviewTests
{
    private static string Names(string code, string? formName = "LoginForm") =>
        string.Join(",", FormCodeScan.DeclaredSubs(code, formName).Select(s => s.Name));

    // ==================================================================
    // 1 — the scanner reads the FORM class only
    // ==================================================================

    [Test]
    public void ASubInANestedClass_OrInAnotherClass_OrAnInterface_IsNotTheFormsOwn()
    {
        const string code =
            "Public Class Helper\n    Public Sub Other(sender As Object, e As EventArgs)\n    End Sub\nEnd Class\n" +
            "Public Interface IThing\n    Sub Declared(sender As Object, e As EventArgs)\nEnd Interface\n" +
            "Public Class LoginForm\n" +
            "    Private Sub Mine(sender As Object, e As EventArgs)\n    End Sub\n" +
            "    Private Class Nested\n        Private Sub Inner(sender As Object, e As EventArgs)\n        End Sub\n    End Class\n" +
            "    Private Structure S\n        Public Sub InS()\n        End Sub\n    End Structure\n" +
            "    Private Sub AfterNested()\n    End Sub\n" +
            "End Class\n";

        Assert.That(Names(code), Is.EqualTo("Mine,AfterNested"));
    }

    [Test]
    public void WithNoClassOfTheFormsName_TheFirstTopLevelClassIsTheForm_InsideANamespaceToo()
    {
        const string code =
            "Namespace App\n    Public Class Page\n        Private Sub A()\n        End Sub\n    End Class\n" +
            "    Public Class Later\n        Private Sub B()\n        End Sub\n    End Class\nEnd Namespace\n";

        Assert.That(Names(code, "Missing"), Is.EqualTo("A"));
    }

    [Test]
    public void ASubInsideIfFalse_OrIfZero_IsSkipped_AndItsElseBranchCounts()
    {
        const string code =
            "Public Class LoginForm\n" +
            "#If False Then\n    Private Sub Dead1()\n    End Sub\n#Else\n    Private Sub Alive1()\n    End Sub\n#End If\n" +
            "#If 0 Then\n    Private Sub Dead2()\n    End Sub\n#If DEBUG Then\n    Private Sub Dead3()\n    End Sub\n#End If\n#End If\n" +
            "#If DEBUG Then\n    Private Sub Alive2()\n    End Sub\n#End If\n" +
            "End Class\n";

        Assert.That(Names(code), Is.EqualTo("Alive1,Alive2"),
            "an unevaluable #If (DEBUG) is read as active — full #If evaluation is a follow-up (piece 2's ProcessForEditor)");
    }

    [Test]
    public void AttributesOnTheSameLine_AGenericSub_AndAnUnderscoreContinuation_AreRead()
    {
        const string code =
            "Public Class LoginForm\n" +
            "    <Obsolete(\"x\")> <CLSCompliant(False)> Private Sub Attributed(sender As Object, e As EventArgs)\n    End Sub\n" +
            "    Private Sub Generic(Of T)(item As T, e As EventArgs)\n    End Sub\n" +
            "    Private Sub Tabbed(sender As Object,\t_\n        e As EventArgs)\n    End Sub\n" +
            "    Private Sub Split _\n        (sender As Object, e As MouseEventArgs)\n    End Sub\n" +
            "End Class\n";

        var subs = FormCodeScan.DeclaredSubs(code, "LoginForm").ToDictionary(s => s.Name);

        Assert.Multiple(() =>
        {
            Assert.That(subs.Keys, Is.EquivalentTo(new[] { "Attributed", "Generic", "Tabbed", "Split" }));
            Assert.That(subs["Attributed"].Parameters.Select(p => p.Type), Is.EqualTo(new[] { "Object", "EventArgs" }));
            Assert.That(subs["Generic"].Parameters.Select(p => p.Name), Is.EqualTo(new[] { "item", "e" }),
                "(Of T) is the type parameter list, not the parameters");
            Assert.That(subs["Tabbed"].Parameters.Select(p => (p.Name, p.Type)),
                Is.EqualTo(new[] { ("sender", (string?)"Object"), ("e", "EventArgs") }),
                "the tab-then-underscore continuation is whitespace, not part of the next parameter's name");
            Assert.That(subs["Split"].Parameters.Select(p => p.Type), Is.EqualTo(new[] { "Object", "MouseEventArgs" }));
        });
    }

    // ==================================================================
    // 2 — ByRef never fits
    // ==================================================================

    [Test]
    public void AByRefParameter_IsKept_AndNeverFits()
    {
        const string code = "Public Class LoginForm\n    Private Sub H(sender As Object, ByRef e As EventArgs)\n    End Sub\nEnd Class\n";
        var sub = FormCodeScan.FindSub(code, "H", "LoginForm")!;
        var form = new FormDocument { Target = FormTarget.WinForms, Name = "LoginForm" };
        var owner = new FormBindOwner(form);

        Assert.Multiple(() =>
        {
            Assert.That(sub.Parameters[1].IsByRef, Is.True);
            Assert.That(sub.Parameters[0].IsByRef, Is.False);
            Assert.That(FormHandlers.Fits(owner, FormControlCatalog.FormRoot.Events!.Single(e => e.Name == "Load"),
                FormTarget.WinForms, sub), Is.False, "csc CS0123: a ref parameter matches no event delegate");
        });
    }

    // ==================================================================
    // 3 — one style, one call shape
    // ==================================================================

    [Test]
    public void TheCallShape_IsTheShapesParameters_MeQualified()
    {
        var form = new FormDocument { Target = FormTarget.Web, Name = "F" };
        var root = new FormBindOwner(form);
        var load = FormControlCatalog.FormRoot.Events!.Single(e => e.Name == "Load");
        var keyPress = FormControlCatalog.FormRoot.Events!.Single(e => e.Name == "KeyPress");

        Assert.Multiple(() =>
        {
            Assert.That(FormHandlers.Shape(root, load, FormTarget.Web).Call("F_Load"), Is.EqualTo("Me.F_Load()"));
            Assert.That(FormHandlers.Shape(root, keyPress, FormTarget.Web).Call("F_KeyPress"), Is.EqualTo("Me.F_KeyPress(e)"));
        });
    }

    private static string SourceOf(params string[] parts)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }
        }

        Assert.Fail(string.Join("/", parts) + " not found above the test binaries");
        return "";
    }

    /// <summary>⛔ Every call site asks the ONE rule: the region writer spells no handler call, and the style is read once.</summary>
    [Test]
    public void EveryCallSite_AsksTheOneRule()
    {
        var writer = SourceOf("BasicLang", "Forms", "RegionWriter.cs");
        var handlers = SourceOf("BasicLang", "Forms", "FormHandlers.cs");

        Assert.Multiple(() =>
        {
            Assert.That(System.Text.RegularExpressions.Regex.IsMatch(writer, @"Me\.\{(handler|bind\.Handler)\}"), Is.False,
                "the region writer must take a handler call from FormHandlerShape.Call");
            Assert.That(writer, Does.Contain(".Call("));
            Assert.That(handlers.Split("StyleOf(").Length - 1, Is.EqualTo(2), "declared once, read once (in Shape)");
            Assert.That(handlers.Split("FormCodeStyle.Classic").Length - 1, Is.EqualTo(2),
                "Classic is named only by StyleOf's body and Shape's arm");
        });
    }

    // ==================================================================
    // 4 — a case-variant handler of ANOTHER owner is never reused
    // ==================================================================

    [TestCase(FormTarget.WinForms)]
    [TestCase(FormTarget.Web)]
    public void ACaseVariantHandler_BoundByAnotherOwner_IsNotReused_TheNewOneIsSuffixed(FormTarget target)
    {
        var form = new FormDocument { Target = target, Name = "Pic" };
        var pic = new FormControl { Kind = "Button", Id = "pic", TabIndex = 0 };
        form.Controls.Add(pic);
        var root = new FormBindOwner(form);
        var click = FormControlCatalog.FormRoot.Events!.Single(e => e.Name == "Click");

        var formPlan = FormHandlers.Plan(form, root, click, FormScaffolder.Create("Pic", target).CodeText);
        FormHandlers.EnsureBind(root, formPlan.EventName, formPlan.Handler);
        var picPlan = FormHandlers.PlanDefault(form, pic, formPlan.CodeText);

        Assert.Multiple(() =>
        {
            Assert.That(formPlan.Handler, Is.EqualTo("Pic_Click"));
            Assert.That(picPlan.Outcome, Is.EqualTo(HandlerOutcome.Created), "never bound to the FORM's Pic_Click");
            Assert.That(picPlan.Handler, Is.EqualTo("pic_Click_1"), "VS appends _1");
            Assert.That(FormCodeScan.FindSub(picPlan.CodeText, "pic_Click_1", "Pic"), Is.Not.Null);
        });
    }

    [Test]
    public void ACaseVariantHandler_OfTheSameOwner_IsStillNavigatedTo()
    {
        var form = new FormDocument { Target = FormTarget.WinForms, Name = "LoginForm" };
        var btn = new FormControl { Kind = "Button", Id = "btn", TabIndex = 0 };
        form.Controls.Add(btn);
        btn.Binds.Add(new FormBind { Event = "Click", Handler = "btn_click" });
        var code = FormScaffolder.Create("LoginForm", FormTarget.WinForms).CodeText
            .Replace("' Your event handlers go here.", "Private Sub btn_click(sender As Object, e As EventArgs)\n    End Sub");

        Assert.That(FormHandlers.PlanDefault(form, btn, code).Outcome, Is.EqualTo(HandlerOutcome.Navigated));
    }

    [Test]
    public void TwoControlsWhoseIdsDifferOnlyInCase_AreToldToRenameOne_NotThatTheFormNameClashes()
    {
        var form = new FormDocument { Target = FormTarget.Web, Name = "LoginForm" };
        foreach (var id in new[] { "f", "F" })
        {
            var c = new FormControl { Kind = "TextBox", Id = id, Geometry = new GridGeometry() };
            c.Binds.Add(new FormBind { Event = "keypress", Handler = id + "_KP" });
            form.Controls.Add(c);
        }

        var result = RegionWriter.Write("LoginForm.bas", FormScaffolder.Create("LoginForm", FormTarget.Web).CodeText, form,
            "LoginForm.blwebform");
        var message = result.Diagnostics.Single(d => d.Code == DesignCodes.DuplicateControlId).Message;

        Assert.Multiple(() =>
        {
            Assert.That(message, Does.Contain("'f'").And.Contain("'F'").And.Contain("Rename one"));
            Assert.That(message, Does.Not.Contain("form's name"));
        });
    }

    // ==================================================================
    // 5 — Unbind is exact
    // ==================================================================

    [Test]
    public void Unbind_NeverRemovesAReservedDataBindingSharingTheEventName()
    {
        var form = new FormDocument { Target = FormTarget.WinForms, Name = "LoginForm" };
        var btn = new FormControl { Kind = "Button", Id = "btn" };
        btn.Binds.Add(new FormBind { Event = "Click", Handler = "btn_Click" });
        btn.Binds.Add(new FormBind { Event = "Click", Property = "Text", Source = "model" });
        form.Controls.Add(btn);

        Assert.Multiple(() =>
        {
            Assert.That(FormHandlers.Unbind(new FormBindOwner(form, btn), "Click"), Is.True);
            Assert.That(btn.Binds.Single().UsesReservedDataBinding, Is.True);
        });
    }

    /// <summary>
    /// Byte-exact: after Unbind, the saved document differs ONLY by the removed &lt;Bind&gt; line, and the code-behind only
    /// by that bind's listener, its (now unused) wrapper and the wrapper comment — plus the region marker's hash.
    /// </summary>
    [Test]
    public void Unbind_TheSavedFilesDiffer_OnlyByTheBind_ItsListener_AndItsWrapper()
    {
        // ⚠ The writer's own canonical spelling (` />`): any change re-serialises the whole document through XmlWriter, which
        // writes every empty element that way (pre-existing, unrelated to Unbind) — so the fixture is canonical and only
        // Unbind's change is measured.
        const string document = """
            <WebForm Name="LoginForm" Version="1">
              <Layout Kind="Grid" Cols="auto" Rows="auto" Gap="8px" />
              <Controls>
                <TextBox Id="txt" Col="0" Row="0" TabIndex="0">
                  <Bind Event="input" Handler="txt_TextChanged" />
                  <Bind Event="keypress" Handler="txt_KeyPress" />
                </TextBox>
              </Controls>
            </WebForm>
            """;
        var file = FormDocumentReader.Read("LoginForm.blwebform", document);
        var code = FormScaffolder.Create("LoginForm", FormTarget.Web).CodeText;
        var before = RegionWriter.Write("LoginForm.bas", code, file.Model, "LoginForm.blwebform").Text;

        Assert.That(FormHandlers.Unbind(new FormBindOwner(file.Model, file.Model.FindById("txt")), "keypress"), Is.True);
        var savedDocument = FormDocumentWriter.Write(file);
        var after = RegionWriter.Write("LoginForm.bas", before, file.Model, "LoginForm.blwebform").Text;

        var bindLine = document.Split('\n').Single(l => l.Contains("\"keypress\"")) + "\n";
        var codeBefore = before.Replace("\r\n", "\n").Split('\n').ToList();
        var codeAfter = after.Replace("\r\n", "\n").Split('\n').ToList();
        var removedCode = codeBefore.Where(l => !codeAfter.Contains(l)).Select(l => l.Trim()).ToList();
        var addedCode = codeAfter.Where(l => !codeBefore.Contains(l)).Select(l => l.Trim()).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(savedDocument, Is.EqualTo(document.Replace(bindLine, "")),
                "byte-exact: the document loses that one <Bind> line and nothing else");
            Assert.That(addedCode.Count, Is.EqualTo(1), "only the region marker (its hash) changes:\n" + string.Join("\n", addedCode));
            Assert.That(addedCode.Single(), Does.StartWith("' <vgs:designer"));
            Assert.That(removedCode.Where(l => !l.StartsWith("' <vgs:designer", StringComparison.Ordinal)), Is.EquivalentTo(new[]
            {
                "' VgsOn_ Subs are generated (a reserved prefix): each turns a page event into what WinForms means by it, then calls your handler.",
                "Private Sub VgsOn_txt_KeyPress(e As DomEvent)",
                "Dim vgsKey As String = e.key",
                "Dim vgsLen As Integer = ::Array.from(vgsKey).length",
                "Dim vgsComposing As Boolean = ::Boolean(e.isComposing)",
                "If Not vgsComposing AndAlso (vgsLen = 1 OrElse vgsKey = \"Enter\" OrElse vgsKey = \"Backspace\" OrElse vgsKey = \"Escape\") Then",
                "Me.txt_KeyPress(e)",
                "End If",
                "txt.addEventListener(\"keydown\", AddressOf VgsOn_txt_KeyPress)"
            }), "the wrapper's End Sub is not counted: InitializeComponent's End Sub keeps that line present");
        });
    }
}
