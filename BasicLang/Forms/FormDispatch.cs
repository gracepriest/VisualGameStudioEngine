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

    /// <summary>
    /// True when some source in <paramref name="sourcePaths"/> mentions the dispatch helper.
    ///
    /// <para>⛔ Generating the helper is only half the job: the backend emits ONE invocation, for
    /// <c>Main</c> (D7 keeps it that way deliberately — per-form entry points are a backend change),
    /// so a helper nobody calls is a page that loads a script and does nothing. The user cannot see
    /// why, because everything about the build succeeded.</para>
    ///
    /// <para>⚠ A text match, not a resolved call. It runs before the compile, when there is no
    /// symbol table to ask, and it drives a WARNING — so the failure mode is a warning not shown
    /// for a mention inside a comment, which costs nothing. Missing the call entirely is what
    /// costs.</para>
    /// </summary>
    public static bool IsCalled(IEnumerable<string> sourcePaths)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);

        foreach (var path in sourcePaths)
        {
            if (string.Equals(Path.GetFileName(path), GeneratedFileName, StringComparison.OrdinalIgnoreCase))
            {
                // The helper declares itself; that is not a call.
                continue;
            }

            try
            {
                if (File.ReadAllText(path).Contains(FormAssetEmitter.DispatchSubName, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A file the build will complain about on its own account.
            }
        }

        return false;
    }

    /// <summary>What to tell the user when the helper is generated and nothing calls it.</summary>
    public static string NotCalledMessage =>
        $"{DesignCodes.DispatchNotCalled}: this project has form pages, but no source calls " +
        $"'{FormAssetEmitter.DispatchCall}'. Each page names its form in " +
        "<body data-form=\"...\">, and that attribute is only read by the generated dispatch — so " +
        $"every page will load and show nothing. Add '{FormAssetEmitter.DispatchCall}' to Main().";
}
