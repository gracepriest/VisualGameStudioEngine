using BasicLang.Forms.Serialization;

namespace BasicLang.Forms;

/// <summary>
/// Reads the form documents listed in a project, for the build routes that emit from them.
///
/// <para>⛔⛔ This exists because the markup emitter was UNREACHABLE. <c>JavaScriptEmitter.Emit</c>
/// takes an optional <c>forms</c> argument and no production caller passed it — not the CLI's
/// project build, not the IDE's build service — so <c>FormAssetEmitter.Emit</c> never ran outside
/// its own tests. A project containing a <c>.blwebform</c> built successfully and wrote no
/// <c>.html</c> and no <c>.css</c>, and F5's startup-page lookup always found zero pages. Every
/// test passed the argument explicitly, which is exactly how a dead production path stays green.
/// </para>
///
/// <para>⚠ Web documents only. A <c>.blform</c> becomes WinForms code through the region writer;
/// there is no markup to emit for it, and handing one to the page emitter would produce an HTML
/// file for a desktop window.</para>
/// </summary>
public static class FormDocumentLoader
{
    /// <summary>
    /// The web form documents among <paramref name="sourcePaths"/>, in listed order.
    ///
    /// <para>⚠ A refused document is SKIPPED with a warning rather than failing the build. The
    /// document is not a program — the compile routes already skip it (BL8001) — so a build that
    /// otherwise succeeds must not be turned into a failure by a page that cannot be generated.
    /// Saying so is what stops the missing page from being a silent one.</para>
    /// </summary>
    public static IReadOnlyList<FormDocument> LoadWebForms(
        IEnumerable<string> sourcePaths, Action<string>? warn = null) =>
        Load(sourcePaths, FormTarget.Web, "was not turned into a page", warn).Select(f => f.Model).ToList();

    /// <summary>
    /// The form documents of <paramref name="target"/> among <paramref name="sourcePaths"/>, in listed order, each with its
    /// path (slice 4 D-5c: the build copy names the document a BL8036 belongs to).
    /// </summary>
    /// <param name="consequence">What a refused document costs on THIS route, finishing the warning
    /// <c>'X' {consequence}: …</c> — "was not turned into a page" on the web route, "— its images and icons were not
    /// copied into the output" on the C# route. ⛔ A parameter, never a literal here: one route's consequence is false on
    /// the other.</param>
    public static IReadOnlyList<LoadedForm> Load(
        IEnumerable<string> sourcePaths, FormTarget target, string consequence, Action<string>? warn = null)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);

        var forms = new List<LoadedForm>();

        foreach (var path in sourcePaths)
        {
            // ⛔ TargetOfExtension, not a literal ".blwebform". BasicLang does not reference
            // VisualGameStudio.Core, so its extension list cannot be reached from here — and a
            // fresh literal would be the third copy of that decision in this assembly.
            if (FormDocumentReader.TargetOfExtension(path) != target)
            {
                continue;
            }

            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                warn?.Invoke($"could not read form document '{Path.GetFileName(path)}' — {ex.Message}");
                continue;
            }

            var file = FormDocumentReader.Read(path, text);
            if (file.IsRefused)
            {
                warn?.Invoke(
                    $"'{Path.GetFileName(path)}' {consequence}: " +
                    string.Join("; ", file.Diagnostics.Where(d => !d.IsWarning).Select(d => d.Message)));
                continue;
            }

            forms.Add(new LoadedForm(file.Model, path));
        }

        return forms;
    }
}

/// <summary>A form document read for a build, with the path it was read from.</summary>
public sealed record LoadedForm(FormDocument Model, string Path);
