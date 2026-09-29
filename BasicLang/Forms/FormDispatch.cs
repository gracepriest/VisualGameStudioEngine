namespace BasicLang.Forms;

/// <summary>
/// D7's <c>Main()</c> dispatch helper, as a real source file in the build.
///
/// <para>⛔⛔ <b><see cref="FormAssetEmitter.DispatchSource"/> had no production caller.</b> Every
/// generated page says which form it is (<c>&lt;body data-form="LoginForm"&gt;</c>) and NOTHING
/// read it: <c>VgsDispatchForm</c> was never generated, never compiled and never shipped, so a web
/// project with three forms produced three pages that all ran the same <c>Main()</c> and showed
/// nothing. The emitter's unit tests were green the whole time, which is what a dead production
/// path looks like from inside the suite.</para>
///
/// <para>⚠ It has to be a SOURCE file, generated before the compile — the dispatch is BasicLang
/// that must be parsed, type-checked and lowered with everything else. Appending it to the emitted
/// JavaScript afterwards would skip every check the rest of the program gets.</para>
/// </summary>
public static class FormDispatch
{
    /// <summary>
    /// The generated file's name. Fixed, and <c>.g.</c> so it reads as generated in a file list.
    ///
    /// <para>⚠ Written under <c>obj/</c>, never beside the user's sources: it is a build artifact,
    /// it is rewritten on every build, and a generated file in the project directory is one a user
    /// edits once and loses.</para>
    /// </summary>
    public const string GeneratedFileName = "VgsFormDispatch.g.bas";

    /// <summary>
    /// Writes the dispatch helper for <paramref name="forms"/> into <paramref name="directory"/>
    /// and returns its path, or null when the project has no web forms to dispatch between.
    /// </summary>
    public static string? Write(
        IReadOnlyList<FormDocument> forms, string directory, Action<string>? warn = null)
    {
        ArgumentNullException.ThrowIfNull(forms);

        // ⛔⛔ ONLY forms that have a class to construct. `Dim f As New LoginForm()` naming a
        // class no file declares is BL7007 — reported against the GENERATED file, which the user
        // did not write and cannot open, for a mistake that is really "this .blwebform has no
        // code-behind". Measured with a real `BasicLang build`: adding the dispatch turned a green
        // build into a failure whose message pointed nowhere useful. Skipping the form and saying
        // so puts the finding back on the file the user can actually fix.
        var dispatchable = new List<FormDocument>();

        foreach (var form in forms)
        {
            if (form.SourcePath.Length == 0 || File.Exists(FormCodeBehind.PathFor(form.SourcePath)))
            {
                dispatchable.Add(form);
                continue;
            }

            warn?.Invoke(
                $"{DesignCodes.RegionAbsent}: '{Path.GetFileName(form.SourcePath)}' has no " +
                $"code-behind ('{Path.GetFileName(FormCodeBehind.PathFor(form.SourcePath))}' is " +
                "missing), so there is no class to show. Its page is still generated, but nothing " +
                "will open it.");
        }

        if (dispatchable.Count == 0)
        {
            return null;
        }

        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, GeneratedFileName);
        File.WriteAllText(path, FormAssetEmitter.DispatchSource(dispatchable.Select(f => f.Name)));
        return path;
    }

    // ⛔ There is no "is it called?" check any more (BL8018, retired 2026-09-28). Generating the helper
    // used to be only half the job — the backend invoked Main and nothing else, so a helper nobody
    // called was a page that loaded and showed nothing, flagged only by a warning. The JavaScript
    // entry point now calls it after Main itself whenever no user code does (FormAssetEmitter.IsStartupDispatch;
    // the "is it called?" question moved onto the IR, JavaScriptBackend.UserCodeCallsDispatch), and the helper
    // runs once per page, so a user's own call is optional, decides when the form starts, and never doubles it.
}
