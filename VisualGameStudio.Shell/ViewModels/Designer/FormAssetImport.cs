using BasicLang.Forms;

namespace VisualGameStudio.Shell.ViewModels.Designer;

/// <summary>What the user chose for a picked file that lies OUTSIDE the project (slice 4 D-5e).</summary>
public enum FormAssetImportChoice
{
    /// <summary>Copy it into <c>&lt;project&gt;/Resources/</c> and store that relative path (the recommended answer).</summary>
    CopyIntoProject,

    /// <summary>Store the absolute path as it is — WinForms only (the web cannot reach the author's disk); BL8036 at build.</summary>
    KeepAbsolutePath,

    /// <summary>Write nothing.</summary>
    Cancel
}

/// <summary>
/// The image/icon picker's decision (slice 4 D-5e): which VALUE a picked file becomes, and — for a file outside the project —
/// the copy into <c>Resources/</c>. View-free and disk-only, so it is tested without a window.
///
/// <para>⛔ The project directory is <see cref="FormAssetPaths.ProjectRootFor"/> — the nearest ancestor holding a
/// <c>*.blproj</c>, else the document's own folder — the same root the build copies from whenever the <c>.blproj</c> is an
/// ancestor of the form (the normal shape).</para>
/// </summary>
public static class FormAssetImport
{
    /// <summary>The folder a file from outside the project is copied into.</summary>
    public const string ResourcesFolder = "Resources";

    /// <summary>
    /// The value to store for <paramref name="pickedPath"/>, or null for nothing. A file inside the project → its path
    /// relative to the project, forward slashes. Outside → <paramref name="choose"/> is asked (with whether keeping the
    /// absolute path is offered: never on the web): a copy into <c>Resources/</c> (identical bytes already there are reused;
    /// a different file of that name becomes <c>name (2).ext</c>), the absolute path (WinForms), or nothing.
    /// </summary>
    public static string? Import(
        string pickedPath, string documentPath, FormTarget target, Func<bool, FormAssetImportChoice> choose)
    {
        var picked = Path.GetFullPath(pickedPath);
        var project = FormAssetPaths.ProjectRootFor(documentPath);

        if (RelativeInsideProject(pickedPath, documentPath) is { } inside)
        {
            return inside;
        }

        var offerKeep = target == FormTarget.WinForms;
        var choice = choose(offerKeep);
        if (choice == FormAssetImportChoice.KeepAbsolutePath && offerKeep)
        {
            return picked;
        }

        if (choice != FormAssetImportChoice.CopyIntoProject)
        {
            return null;
        }

        var folder = Path.Combine(project, ResourcesFolder);
        Directory.CreateDirectory(folder);
        var name = Path.GetFileNameWithoutExtension(picked);
        var extension = Path.GetExtension(picked);
        for (var n = 1; ; n++)
        {
            var fileName = n == 1 ? name + extension : $"{name} ({n}){extension}";
            var destination = Path.Combine(folder, fileName);
            if (!File.Exists(destination))
            {
                File.Copy(picked, destination);
                return $"{ResourcesFolder}/{fileName}";
            }

            if (SameBytes(destination, picked))
            {
                return $"{ResourcesFolder}/{fileName}"; // already there: reuse it, never a second copy
            }
        }
    }

    /// <summary>
    /// The picked file's path relative to the document's project, forward slashes — or null when it lies outside (the view
    /// then asks the user what to do, BEFORE <see cref="Import"/> runs, since the question is asynchronous there).
    /// </summary>
    public static string? RelativeInsideProject(string pickedPath, string documentPath)
    {
        var relative = Path.GetRelativePath(FormAssetPaths.ProjectRootFor(documentPath), Path.GetFullPath(pickedPath));
        // ⚠ The first SEGMENT is "..", not the first two characters: a project-root file named `..logo.png` is inside.
        return relative.Split('\\', '/')[0] != ".." && !Path.IsPathRooted(relative)
            ? relative.Replace('\\', '/')
            : null;
    }

    private static bool SameBytes(string a, string b) =>
        new FileInfo(a).Length == new FileInfo(b).Length && File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
}
