using System.Text;

namespace BasicLang.Forms;

/// <summary>The two files a new form is: the document, and the user's code-behind.</summary>
/// <param name="DocumentFileName">e.g. <c>LoginForm.blwebform</c>.</param>
/// <param name="DocumentText">A minimal, valid form document.</param>
/// <param name="CodeFileName">e.g. <c>LoginForm.bas</c>.</param>
/// <param name="CodeText">An empty class carrying two empty designer regions.</param>
public sealed record FormScaffold(
    string DocumentFileName,
    string DocumentText,
    string CodeFileName,
    string CodeText);

/// <summary>
/// Creates the document + <c>.bas</c> pair a new form consists of — <c>.blform</c> for WinForms,
/// <c>.blwebform</c> for the web (D2).
///
/// <para>Pure text in, pure text out — no file system, no project model. That is deliberate: the
/// IDE's add-item flow, the CLI, and the tests all need the same two strings, and the part that is
/// easy to get wrong (the region markers and their hashes) has nothing to do with where the files
/// land.</para>
/// </summary>
public static class FormScaffolder
{
    /// <summary>
    /// ⛔ A form's name becomes a CLASS name, so it must be a legal type name: PascalCase-ish and
    /// <b>containing no underscore</b>. The PascalCase heuristic is passed the type name, and a type
    /// name with <c>_</c> falls out of it — rename <c>MainForm</c> to <c>Main_Form</c> and every
    /// <c>Me.&lt;inherited member&gt;</c> becomes a hard error. Control IDENTIFIERS may contain
    /// underscores freely; this rule is only about the form's own name.
    /// </summary>
    public static bool IsLegalFormName(string? name) =>
        !string.IsNullOrEmpty(name) &&
        char.IsLetter(name[0]) &&
        name.All(char.IsLetterOrDigit);

    /// <summary>Why <paramref name="name"/> is not usable as a form name, or null when it is.</summary>
    public static string? DescribeIllegalName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "a form needs a name.";
        }

        if (!char.IsLetter(name[0]))
        {
            return $"'{name}' must start with a letter — it becomes a class name.";
        }

        if (name.Contains('_', StringComparison.Ordinal))
        {
            return $"'{name}' cannot contain an underscore: a TYPE name with '_' falls out of the " +
                   "compiler's .NET-type heuristic, and every Me.<inherited member> in the class then " +
                   "becomes a hard error. Control names may contain underscores; the form's own name " +
                   "may not.";
        }

        if (!name.All(char.IsLetterOrDigit))
        {
            return $"'{name}' may contain only letters and digits — it becomes a class name.";
        }

        return null;
    }

    /// <summary>
    /// The pair for a new, empty form.
    ///
    /// <para>The <c>.bas</c> carries both regions <b>already present and already hashed</b>, so the
    /// very first designer save finds them Canon and writes. A scaffold that emitted the markers
    /// without correct hashes would make the first save report the designer's own output as a hand
    /// edit (BL8011) — the feature would refuse to work on the file it had just created.</para>
    ///
    /// <para>⛔⛔ <b>The region order differs by target, and both orders are measured.</b></para>
    ///
    /// <para><b>Web:</b> the init region comes LAST, after the handler area, because D8's ordering
    /// rule is real there — <c>addEventListener("click", AddressOf H)</c> with H declared later
    /// fails with "cannot convert from 'Action(Of Object)' to 'Action(Of DomEvent)'". The DOM
    /// signature declares the parameter type, so the erased handler has something concrete to fail
    /// against.</para>
    ///
    /// <para><b>WinForms:</b> the init region comes in the canonical VSIX position — straight after
    /// <c>New()</c>, with handlers below it — because Owner decision 3 makes that shape canonical
    /// and because the ordering rule does NOT bite there: measured 2026-09-13, the shipped template
    /// declares <c>btnClick_Click</c> below the <c>InitializeComponent</c> that wires it and
    /// compiles through BasicLang and csc with the handler's full parameter types intact. The event
    /// is an unresolvable .NET member typed as <c>Object</c>, so there is no declared delegate to
    /// mismatch.</para>
    /// </summary>
    public static FormScaffold Create(string formName, FormTarget target = FormTarget.Web)
    {
        var illegal = DescribeIllegalName(formName);
        if (illegal != null)
        {
            throw new ArgumentException(illegal, nameof(formName));
        }

        var document = new FormDocument { Target = target, Name = formName };
        if (target == FormTarget.Web)
        {
            document.Layout = new FormLayout
            {
                Kind = FormLayoutKind.Grid, Cols = "auto,1fr", Rows = "auto", Gap = "8px"
            };
        }
        else
        {
            // D3's other half: a window has a size and a caption where a page has a layout. 800x450
            // is the size `dotnet new winforms` gives a new form, so a form created here and a form
            // created by the shipped template open the same size rather than differing for no
            // reason the user could name.
            document.Width = 800;
            document.Height = 450;
            document.Text = formName;
        }

        var documentFileName = formName + document.FileExtension;

        return new FormScaffold(
            documentFileName,
            Serialization.FormDocumentWriter.Create(document),
            formName + ".bas",
            CodeBehind(formName, documentFileName, target));
    }

    private static void AppendInitRegion(
        StringBuilder sb, string documentFileName, string emptyHash, string indent)
    {
        sb.Append(RegionMarkers.FormatOpen(RegionMarkers.Init, documentFileName, emptyHash, indent)).Append('\n');
        sb.Append(RegionMarkers.FormatClose(indent)).Append('\n');
        sb.Append('\n');
    }

    private static string CodeBehind(string formName, string documentFileName, FormTarget target)
    {
        const string indent = "    ";
        var emptyHash = RegionMarkers.HashContent("");
        var sb = new StringBuilder();

        if (target == FormTarget.WinForms)
        {
            sb.Append("Using System\n");
            sb.Append("Using System.Drawing\n");
            sb.Append("Using System.Windows.Forms\n\n");
        }

        sb.Append($"Public Class {formName}\n");
        if (target == FormTarget.WinForms)
        {
            sb.Append($"{indent}Inherits Form\n");
        }

        sb.Append('\n');

        // Region 1: the field declarations.
        sb.Append(RegionMarkers.FormatOpen(RegionMarkers.Controls, documentFileName, emptyHash, indent)).Append('\n');
        sb.Append(RegionMarkers.FormatClose(indent)).Append('\n');
        sb.Append('\n');

        sb.Append($"{indent}Public Sub New()\n");

        // ⛔⛔ Me., NEVER the bare call. MEASURED 2026-09-18 by building and RUNNING a probe: the
        // JavaScript backend emits an unqualified call to an instance method of the enclosing class
        // as a BARE GLOBAL — `InitializeComponent();` rather than `this.InitializeComponent();` —
        // while `Me.Method()` emits correctly. The build says "Compilation successful!" either way
        // and the page dies on load with `ReferenceError: InitializeComponent is not defined`.
        // Every scaffolded web form had this shape, so every one of them was dead on arrival.
        // The backend defect is unfixed and filed separately; this is not generating the trigger.
        sb.Append($"{indent}{indent}Me.InitializeComponent()\n");
        sb.Append($"{indent}End Sub\n");
        sb.Append('\n');

        if (target == FormTarget.WinForms)
        {
            // Canonical VSIX order: InitializeComponent right after New(), handlers below it.
            AppendInitRegion(sb, documentFileName, emptyHash, indent);
            sb.Append($"{indent}' Your event handlers go here.\n");
            sb.Append('\n');
        }
        else
        {
            // ⛔ Web only: handlers MUST precede the region that wires them, or addEventListener
            // rejects the erased Action(Of Object).
            sb.Append($"{indent}' Your event handlers go here, ABOVE the designer's init region —\n");
            sb.Append($"{indent}' on the web an AddressOf naming a Sub declared later loses its\n");
            sb.Append($"{indent}' parameter types and addEventListener will not accept it.\n");
            sb.Append('\n');
            AppendInitRegion(sb, documentFileName, emptyHash, indent);
        }

        sb.Append("End Class\n");
        return sb.ToString();
    }
}
