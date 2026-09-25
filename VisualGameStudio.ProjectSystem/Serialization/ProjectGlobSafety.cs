using BasicLang.Compiler.ProjectSystem;
using VisualGameStudio.Core.Models;

namespace VisualGameStudio.ProjectSystem.Serialization;

/// <summary>
/// Protects a project that relies on the default source glob from losing its sources the moment
/// something adds an explicit item to it.
///
/// <para>⛔⛔ <b>The hazard.</b> <c>ProjectFile.GetSourceFiles()</c> globs <c>**/*.bas</c> (and the
/// other source extensions) <b>only while the project has no explicit <c>&lt;Compile&gt;</c>
/// items</b>; the first one flips it to the explicit list. So adding ONE file — a new form, a new
/// class, anything — to a project that had been relying on the glob <b>silently removes every other
/// source from the build</b>. No diagnostic: the build simply compiles one file and reports
/// success.</para>
///
/// <para>The fix is to materialise what the glob would have found BEFORE the first explicit item
/// lands, so the explicit list starts out saying exactly what the glob already said. After that the
/// project is explicit and stays explicit, which is the state every IDE-created project is in
/// already.</para>
/// </summary>
public static class ProjectGlobSafety
{
    /// <summary>
    /// If <paramref name="project"/> has no <c>Compile</c> items, adds the files the compiler's
    /// default glob would have found. A no-op for a project that is already explicit.
    /// </summary>
    /// <returns>The includes that were added, relative to the project directory.</returns>
    public static IReadOnlyList<string> MaterialiseGlobbedSources(BasicLangProject project)
    {
        if (project.Items.Any(i => i.ItemType == ProjectItemType.Compile))
        {
            // Already explicit. Adding more is safe and changes nothing about the glob.
            return Array.Empty<string>();
        }

        var directory = project.ProjectDirectory;
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return Array.Empty<string>();
        }

        var added = new List<string>();

        // ⛔ Mirrors ProjectFile.GetSourceFiles' default branch EXACTLY — the same extension list, and
        // AllDirectories, not TopDirectoryOnly. Any divergence here silently changes which files a
        // project builds at the moment it becomes explicit, which is the worst possible time.
        foreach (var extension in ProjectFile.BasicLangSourceExtensions)
        {
            foreach (var file in Directory.GetFiles(directory, "*" + extension, SearchOption.AllDirectories))
            {
                // ⛔⛔ The exact-extension check, because Win32 globbing lets a three-character
                // pattern match LONGER extensions: "*.bas" matches `Main.basic` and, worse, a
                // stray `Main.bas~`. Without it this method materialises an explicit <Compile>
                // item for a file the compiler's own glob rejects — and the moment the list
                // becomes explicit, GetSourceFiles takes its explicit branch, which does NO
                // extension filtering at all. So adding a form to a project could make it start
                // compiling a backup file. This guard was added to GetSourceFiles and missed here
                // for exactly one commit, which is precisely the divergence the comment above
                // warns about.
                if (!string.Equals(Path.GetExtension(file), extension, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Skip build output. Both globs exclude it now; this one must regardless, because
                // adding bin\ and obj\ content as EXPLICIT items would freeze generated output
                // into the project file forever after rather than merely for one build.
                if (IsUnderBuildOutput(directory, file))
                {
                    continue;
                }

                var include = Path.GetRelativePath(directory, file);
                if (added.Contains(include, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                added.Add(include);
                project.Items.Add(new ProjectItem(include, ProjectItemType.Compile));
            }
        }

        return added;
    }

    /// <summary>
    /// ⚠ This used to be a deliberate divergence: <c>GetSourceFiles</c> swept <c>bin\</c> and
    /// <c>obj\</c>, which was harmless while the list is recomputed every build but would be
    /// permanent once written into the project file.
    ///
    /// <para>It is no longer a divergence — the compiler's glob excludes build output too, after a
    /// generated source under <c>obj/</c> got swept back in and compiled twice on the second build.
    /// The exclusion stays here on its own merits: an EXPLICIT item freezes the mistake into the
    /// project file rather than lasting one build.</para>
    /// </summary>
    private static bool IsUnderBuildOutput(string projectDirectory, string file)
    {
        var relative = Path.GetRelativePath(projectDirectory, file);
        var first = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .FirstOrDefault() ?? "";

        return first.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || first.Equals("obj", StringComparison.OrdinalIgnoreCase);
    }
}
