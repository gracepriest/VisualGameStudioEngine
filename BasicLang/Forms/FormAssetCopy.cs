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
        // Keyed case-INsensitively to the first spelling seen: on Windows two spellings are one file (copied once), and a
        // second spelling that differs only in case is warned — a case-sensitive server or disk would not find it.
        var copied = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var written = new List<string>();

        foreach (var (form, documentPath) in forms)
        {
            var locate = AttributeLocator(documentPath);
            foreach (var (controlId, owner, row, value) in References(form))
            {
                void Warn(string why)
                {
                    var (line, column) = locate(controlId, row.Name);
                    report(new DesignDiagnostic(DesignCodes.AssetNotCopied,
                        $"{DesignCodes.AssetNotCopied}: '{form.Name}.{owner}' = \"{value}\" was not copied into the output: {why}",
                        documentPath, line, column, IsWarning: true));
                }

                // ⛔ The catalog FIRST: a value it refuses on this target (a rooted path on the web, an .svg on WinForms) or
                // cannot read at all (Degraded — `../x.png`) is named by BL8009 at generation, and nothing emitted references
                // it. A BL8036 for it too would be a second, and possibly false, account of the same value.
                if (!row.Accepts(value, form.Target) || FormAssetPaths.IsUrl(value))
                {
                    continue; // ... and a web address is fetched, never copied
                }

                if (FormAssetPaths.IsRooted(value))
                {
                    Warn("it is an absolute path, so the program will look for it at that path on the machine it runs on. " +
                         "Put the file in the project to ship it.");
                    continue;
                }

                var relative = Key(value);
                var source = Path.GetFullPath(Path.Combine(project, relative.Replace('/', Path.DirectorySeparatorChar)));
                // Defensive: Accepts already admits only paths inside the project; this is the containment rule restated
                // at the point of reading, so no future catalog change can make the copy read outside the project.
                if (!SafeZip.IsWithin(project, relative) || !source.StartsWith(project, StringComparison.OrdinalIgnoreCase))
                {
                    Warn("it resolves outside the project, and nothing outside the project is copied.");
                    continue;
                }

                if (Directory.Exists(source))
                {
                    Warn($"it names a folder ({source}), not a file. Name the image file inside it.");
                    continue;
                }

                if (!File.Exists(source))
                {
                    Warn($"the file is missing at {source}.");
                    continue;
                }

                if (copied.TryGetValue(relative, out var first))
                {
                    if (!string.Equals(first, relative, StringComparison.Ordinal))
                    {
                        Warn($"it differs only in letter case from '{first}', which was copied under that spelling. Windows " +
                             "treats them as one file, but a case-sensitive web server or disk will not find this one. " +
                             "Use one spelling.");
                    }

                    continue; // the same file, referenced again
                }

                copied.Add(relative, relative);
                var target = Path.GetFullPath(Path.Combine(output, relative.Replace('/', Path.DirectorySeparatorChar)));
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    ReplaceFile(target, source);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Warn($"writing {target} failed ({ex.Message}). Close whatever holds it open — a running copy of the " +
                         "program, a viewer — and build again.");
                    continue;
                }

                written.Add(target);
            }
        }

        return written;
    }

    /// <summary>
    /// The one spelling of a relative path, for the copy and for "is this the same file": forward slashes, with <c>.</c>
    /// and empty segments dropped (<c>./Resources//logo.png</c> is <c>Resources/logo.png</c>).
    /// </summary>
    private static string Key(string value) =>
        string.Join("/", FormAssetPaths.Normalise(value).Split('/').Where(s => s.Length > 0 && s != "."));

    /// <summary>
    /// Where a property's attribute sits in the form DOCUMENT (1-based line and column), so BL8036 can take the user to
    /// it: the root element for the Form's own rows, else the element whose <c>Id</c> is the control's. (0, 0) when the
    /// document cannot be read or the attribute is not there — the diagnostic then names the document path alone.
    /// </summary>
    private static Func<string?, string, (int Line, int Column)> AttributeLocator(string documentPath)
    {
        System.Xml.Linq.XDocument? document = null;
        var loaded = false;
        return (controlId, property) =>
        {
            if (!loaded)
            {
                loaded = true;
                try
                {
                    document = System.Xml.Linq.XDocument.Load(documentPath, System.Xml.Linq.LoadOptions.SetLineInfo);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException
                                               or ArgumentException or NotSupportedException)
                {
                    document = null;
                }
            }

            var root = document?.Root;
            var element = root == null ? null
                : controlId == null ? root
                : root.Descendants().FirstOrDefault(e => string.Equals(Attribute(e, "Id")?.Value, controlId, StringComparison.Ordinal));
            return element != null && Attribute(element, property) is System.Xml.IXmlLineInfo info && info.HasLineInfo()
                ? (info.LineNumber, info.LinePosition)
                : (0, 0);
        };

        static System.Xml.Linq.XAttribute? Attribute(System.Xml.Linq.XElement element, string name) =>
            element.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Every Image/Icon value the form references: the Form's own rows that apply to its document, then every control
    /// (the visual tree) and every tray component — each walker chosen explicitly, as the CLAUDE.md tray rule requires.
    /// </summary>
    private static IEnumerable<(string? ControlId, string Owner, FormPropertyDef Row, string Value)> References(FormDocument form)
    {
        foreach (var row in FormControlCatalog.FormRoot.Properties.Where(IsAsset))
        {
            if (FormRootValues.Applies(row, form) && form.Properties.TryGetValue(row.Name, out var value))
            {
                yield return (null, row.Name, row, value);
            }
        }

        foreach (var control in form.AllControls().Concat(form.AllComponents()))
        {
            foreach (var row in control.Definition?.Properties.Where(IsAsset) ?? Enumerable.Empty<FormPropertyDef>())
            {
                if (row.AppliesTo(form.Target) && control.Properties.TryGetValue(row.Name, out var value))
                {
                    yield return (control.Id, $"{control.Id}.{row.Name}", row, value);
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
