using BasicLang.Runtime;

namespace BasicLang.Forms;

/// <summary>
/// ⛔ Slice 4 D-5c: the build step that puts every image and icon a form references BESIDE THE OUTPUT — the exe's folder
/// on WinForms (where <c>System.AppContext.BaseDirectory</c> points), the site folder on the web (where the page's relative
/// <c>src</c> resolves). ONE helper, called from BOTH build routes (the CLI's <c>Program.cs</c> and the IDE's
/// <c>BuildService</c>) — a copy written into one route alone is the mirrored-pair failure this repo keeps paying for.
///
/// <para>The three rules of <c>JavaScriptEmitter.CopyImportedModules</c>: containment (<c>SafeZip.IsWithin</c>, nothing
/// is ever read from outside the project or written outside the output), a missing file is a WARNING (BL8036) and never
/// fails the build, and every write goes through temp + rename (a mapped previous copy — ERROR_USER_MAPPED_FILE — cannot
/// fail a rebuild).</para>
/// </summary>
public static class FormAssetCopy
{
    /// <summary>
    /// Copies <c>projectDir/&lt;path&gt;</c> → <c>outputDir/&lt;path&gt;</c> (sub-path preserved) for every Image/Icon value
    /// of every control, component and the Form itself, each file once; reports BL8036 for what it did not copy.
    /// </summary>
    /// <returns>The output paths written.</returns>
    public static IReadOnlyList<string> Copy(
        IEnumerable<LoadedForm> forms, string projectDir, string outputDir, Action<DesignDiagnostic> report)
    {
        ArgumentNullException.ThrowIfNull(forms);
        ArgumentNullException.ThrowIfNull(report);

        var project = Path.GetFullPath(projectDir);
        var output = Path.GetFullPath(outputDir);
        var copied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var written = new List<string>();

        foreach (var (form, documentPath) in forms)
        {
            foreach (var (owner, row, value) in References(form))
            {
                void Warn(string why) => report(new DesignDiagnostic(DesignCodes.AssetNotCopied,
                    $"{DesignCodes.AssetNotCopied}: '{form.Name}.{owner}' = \"{value}\" was not copied into the output: {why}",
                    documentPath, 0, 0, IsWarning: true));

                if (FormAssetPaths.IsUrl(value))
                {
                    continue; // a web address is fetched, never copied
                }

                if (FormAssetPaths.IsRooted(value))
                {
                    Warn("it is an absolute path, so the program will look for it at that path on the machine it runs on. " +
                         "Put the file in the project to ship it.");
                    continue;
                }

                if (FormAssetPaths.EscapesProject(value) || !FormAssetPaths.IsInsideProject(value))
                {
                    Warn("it resolves outside the project, and nothing outside the project is copied.");
                    continue;
                }

                if (!row.Accepts(value, form.Target))
                {
                    continue; // refused on this target (BL8009 names it at generation); nothing emitted references it
                }

                var relative = FormAssetPaths.Normalise(value);
                var source = Path.GetFullPath(Path.Combine(project, relative.Replace('/', Path.DirectorySeparatorChar)));
                if (!SafeZip.IsWithin(project, relative) || !source.StartsWith(project, StringComparison.OrdinalIgnoreCase))
                {
                    Warn("it resolves outside the project, and nothing outside the project is copied.");
                    continue;
                }

                if (!File.Exists(source))
                {
                    Warn($"the file is missing at {source}.");
                    continue;
                }

                if (!copied.Add(relative))
                {
                    continue; // the same file, referenced again
                }

                var target = Path.GetFullPath(Path.Combine(output, relative.Replace('/', Path.DirectorySeparatorChar)));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                ReplaceFile(target, source);
                written.Add(target);
            }
        }

        return written;
    }

    /// <summary>
    /// Every Image/Icon value the form references: the Form's own rows that apply to its document, then every control
    /// (the visual tree) and every tray component — each walker chosen explicitly, as the CLAUDE.md tray rule requires.
    /// </summary>
    private static IEnumerable<(string Owner, FormPropertyDef Row, string Value)> References(FormDocument form)
    {
        foreach (var row in FormControlCatalog.FormRoot.Properties.Where(IsAsset))
        {
            if (FormRootValues.Applies(row, form) && form.Properties.TryGetValue(row.Name, out var value))
            {
                yield return (row.Name, row, value);
            }
        }

        foreach (var control in form.AllControls().Concat(form.AllComponents()))
        {
            foreach (var row in control.Definition?.Properties.Where(IsAsset) ?? Enumerable.Empty<FormPropertyDef>())
            {
                if (row.AppliesTo(form.Target) && control.Properties.TryGetValue(row.Name, out var value))
                {
                    yield return ($"{control.Id}.{row.Name}", row, value);
                }
            }
        }
    }

    private static bool IsAsset(FormPropertyDef row) => row.Type is FormPropertyType.Image or FormPropertyType.Icon;

    /// <summary>Temp + rename beside the target: a rebuild over a copy a scanner has mapped cannot fail mid-write.</summary>
    private static void ReplaceFile(string target, string source)
    {
        var temp = Path.Combine(Path.GetDirectoryName(target)!,
            "." + Path.GetFileName(target) + "." + Path.GetRandomFileName() + ".tmp");
        try
        {
            File.Copy(source, temp, overwrite: true);
            File.Move(temp, target, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }
}
