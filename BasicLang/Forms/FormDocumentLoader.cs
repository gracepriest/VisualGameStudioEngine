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
        IEnumerable<string> sourcePaths, Action<string>? warn = null)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);

        var forms = new List<FormDocument>();

        foreach (var path in sourcePaths)
        {
            // ⛔ TargetOfExtension, not a literal ".blwebform". BasicLang does not reference
            // VisualGameStudio.Core, so its extension list cannot be reached from here — and a
            // fresh literal would be the third copy of that decision in this assembly.
            if (FormDocumentReader.TargetOfExtension(path) != FormTarget.Web)
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
                    $"'{Path.GetFileName(path)}' was not turned into a page: " +
                    string.Join("; ", file.Diagnostics.Where(d => !d.IsWarning).Select(d => d.Message)));
                continue;
            }

            forms.Add(file.Model);
        }

        return forms;
    }
}
