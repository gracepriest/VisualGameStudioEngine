using BasicLang.Forms.Serialization;

namespace BasicLang.Forms;

/// <summary>
/// The <c>.bas</c> half of a form: which file the designer regions live in, and the one call that
/// regenerates them (D1).
///
/// <para>⛔⛔ <b>Without a caller, the whole designer is decorative.</b> <see cref="RegionWriter"/>
/// is the only thing that puts a control the user dragged onto the canvas into a program — the
/// document alone compiles to nothing on the desktop. For most of this branch's life it had no
/// production caller at all, so a scaffolded form's <c>Public Sub New()</c> called an
/// <c>InitializeComponent</c> that was never generated: the canvas drew the form, the document
/// saved, the build ran, and the user got a missing-member error in a file they had not written.
/// This type exists so that call has one obvious home rather than being inlined into whichever
/// view model happened to need it.</para>
///
/// <para>Pure text in, pure text out — the caller owns the file system, exactly as
/// <see cref="FormScaffolder"/> does, so the IDE's save path, the CLI and the tests all drive the
/// same code.</para>
/// </summary>
public static class FormCodeBehind
{
    /// <summary>
    /// The <c>.bas</c> that pairs with a form document.
    ///
    /// <para>⚠ Same directory, same base name — the pairing <see cref="FormScaffolder"/> creates
    /// and the one the region markers record. <c>LoginForm.blform</c> and
    /// <c>LoginForm.blwebform</c> both pair with <c>LoginForm.bas</c>, which is also why a project
    /// cannot hold both for one form.</para>
    /// </summary>
    public static string PathFor(string documentPath) =>
        Path.ChangeExtension(documentPath, ".bas");

    /// <summary>
    /// Rewrites <paramref name="codeText"/>'s designer regions from the document in
    /// <paramref name="file"/>.
    ///
    /// <para>⚠ The form file NAME, not its path, is what the region markers carry — that is what
    /// makes a region's ownership readable in the file itself and survives the project moving.
    /// Passing the full path here wrote an absolute path into the user's source.</para>
    ///
    /// <para>The result says whether anything changed and whether the write was refused; a caller
    /// that ignores <see cref="RegionWriteResult.Refused"/> hands the user a silent no-op, which is
    /// the failure the refuse-by-default policy exists to make visible.</para>
    /// </summary>
    public static RegionWriteResult Regenerate(FormFile file, string codePath, string codeText) =>
        RegionWriter.Write(codePath, codeText, file.Model, Path.GetFileName(file.FilePath));
}
