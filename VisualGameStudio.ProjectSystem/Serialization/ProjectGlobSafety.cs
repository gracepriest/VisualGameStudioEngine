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
                // Skip build output — the glob does not exclude it, but adding bin\ and obj\ content
                // as explicit items would make the project build its own artifacts forever after.
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
    /// ⚠ A deliberate divergence from the compiler's glob, and the only one. <c>GetSourceFiles</c>
    /// sweeps <c>bin\</c> and <c>obj\</c> too, which is harmless while the list is recomputed every
    /// build but would be permanent once written into the project file. Freezing generated output
    /// into the source list is a worse failure than the asymmetry.
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
