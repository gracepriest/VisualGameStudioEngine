using System.Text.Json.Serialization;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// The <c>vscode.workspace.fs</c> operations, as the extension host's JS side calls them.
///
/// <para>Separate from <see cref="ExtensionHost"/> so this logic can be tested without a process:
/// the host's RPC handlers are thin adapters that unwrap the parameter object and call these.</para>
///
/// <para>⛔ NOT SANDBOXED TO THE WORKSPACE, deliberately. The extension host is a Node process the
/// extension fully controls — it can <c>require('fs')</c> and reach anything the IDE's user can, so
/// a check here would be bypassed by one line of JS while reading like a boundary. VS Code makes the
/// same call. The protection that matters for third-party code is which extensions get installed,
/// not a guard on this path.</para>
/// </summary>
public static class ExtensionFileSystem
{
    /// <summary>VS Code's <c>FileType</c> enum, which extensions compare against numerically.</summary>
    private const int FileTypeFile = 1;
    private const int FileTypeDirectory = 2;

    /// <summary>
    /// Every uri on this wire is a <c>file://</c> string produced by the JS side's
    /// <c>Uri.toString()</c>, so it is percent-encoded. Treating it as a path would look for a
    /// literal directory named "file:". A bare path passes through unchanged.
    /// </summary>
    private static string Local(string uri) => ExtensionService.ToLocalDocumentPath(uri);

    /// <summary>
    /// Reads a file as base64. The JS side does <c>Buffer.from(result, 'base64')</c>, so the return
    /// must be bare base64 — the encoding exists so non-UTF8 content can cross the JSON channel.
    /// </summary>
    public static string ReadFile(string uri) =>
        Convert.ToBase64String(File.ReadAllBytes(Local(uri)));

    /// <summary>
    /// Writes base64 content. Missing parent directories are created, matching VS Code, whose
    /// writeFile defaults to <c>create: true</c> — without it an extension writing its own cache
    /// into a fresh folder fails on the first call.
    /// </summary>
    public static void WriteFile(string uri, string content)
    {
        var path = Local(uri);

        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        File.WriteAllBytes(path, Convert.FromBase64String(content));
    }

    /// <summary>
    /// Stats a path. ⛔ A missing path THROWS rather than answering a zero-sized stat: extensions use
    /// stat as an existence probe, and one that always succeeds tells them every path exists.
    /// </summary>
    public static ExtensionFileStat Stat(string uri)
    {
        var path = Local(uri);

        if (Directory.Exists(path))
        {
            var info = new DirectoryInfo(path);
            return new ExtensionFileStat
            {
                Type = FileTypeDirectory,
                Ctime = ToEpochMilliseconds(info.CreationTimeUtc),
                Mtime = ToEpochMilliseconds(info.LastWriteTimeUtc),
                Size = 0,
            };
        }

        if (File.Exists(path))
        {
            var info = new FileInfo(path);
            return new ExtensionFileStat
            {
                Type = FileTypeFile,
                Ctime = ToEpochMilliseconds(info.CreationTimeUtc),
                Mtime = ToEpochMilliseconds(info.LastWriteTimeUtc),
                Size = info.Length,
            };
        }

        throw new FileNotFoundException($"No file or directory at '{path}'.", path);
    }

    /// <summary>
    /// Lists a directory as <c>[[name, type], …]</c> pairs — the shape VS Code's readDirectory
    /// returns. The name is the entry name alone, never a full path.
    /// </summary>
    public static List<object[]> ReadDirectory(string uri)
    {
        var path = Local(uri);

        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"No directory at '{path}'.");
        }

        var entries = new List<object[]>();

        foreach (var dir in Directory.EnumerateDirectories(path))
        {
            entries.Add(new object[] { Path.GetFileName(dir), FileTypeDirectory });
        }

        foreach (var file in Directory.EnumerateFiles(path))
        {
            entries.Add(new object[] { Path.GetFileName(file), FileTypeFile });
        }

        return entries;
    }

    /// <summary>Creates a directory. Idempotent, as VS Code's is — an existing directory is not an error.</summary>
    public static void CreateDirectory(string uri) => Directory.CreateDirectory(Local(uri));

    /// <summary>
    /// Deletes a file or directory.
    ///
    /// <para>⛔ <paramref name="recursive"/> is load-bearing: without it a NON-EMPTY directory must
    /// fail rather than have its contents removed. That is the difference between an extension
    /// clearing its own cache folder and an extension clearing a folder of the user's source.
    /// <see cref="Directory.Delete(string, bool)"/> already refuses, so this passes the flag through
    /// rather than reimplementing the check.</para>
    /// </summary>
    public static void Delete(string uri, bool recursive)
    {
        var path = Local(uri);

        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive);
            return;
        }

        if (File.Exists(path))
        {
            File.Delete(path);
            return;
        }

        throw new FileNotFoundException($"No file or directory at '{path}'.", path);
    }

    /// <summary>
    /// Moves a file or directory. ⛔ <paramref name="overwrite"/> false REFUSES an existing target
    /// rather than clobbering it — this is the flag standing between an extension's "save as" and a
    /// destroyed file.
    /// </summary>
    public static void Rename(string source, string target, bool overwrite)
    {
        var from = Local(source);
        var to = Local(target);

        if (Directory.Exists(from))
        {
            if (Directory.Exists(to) || File.Exists(to))
            {
                if (!overwrite)
                {
                    throw new IOException($"'{to}' already exists.");
                }

                DeleteExisting(to);
            }

            Directory.Move(from, to);
            return;
        }

        if (!File.Exists(from))
        {
            throw new FileNotFoundException($"No file or directory at '{from}'.", from);
        }

        // File.Move's own overwrite flag already throws when the target exists and it is false, so
        // the refusal is the framework's rather than a check that could drift from it.
        File.Move(from, to, overwrite);
    }

    /// <summary>Copies a file or directory tree, leaving the source in place.</summary>
    public static void Copy(string source, string target, bool overwrite)
    {
        var from = Local(source);
        var to = Local(target);

        if (Directory.Exists(from))
        {
            CopyDirectory(from, to, overwrite);
            return;
        }

        if (!File.Exists(from))
        {
            throw new FileNotFoundException($"No file at '{from}'.", from);
        }

        var parent = Path.GetDirectoryName(to);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        File.Copy(from, to, overwrite);
    }

    private static void CopyDirectory(string from, string to, bool overwrite)
    {
        if (Directory.Exists(to) && !overwrite)
        {
            throw new IOException($"'{to}' already exists.");
        }

        Directory.CreateDirectory(to);

        foreach (var file in Directory.EnumerateFiles(from))
        {
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite);
        }

        foreach (var dir in Directory.EnumerateDirectories(from))
        {
            CopyDirectory(dir, Path.Combine(to, Path.GetFileName(dir)), overwrite);
        }
    }

    private static void DeleteExisting(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        else if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>VS Code's FileStat times are milliseconds since the Unix epoch, not DateTimes.</summary>
    private static long ToEpochMilliseconds(DateTime utc) =>
        new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
}

/// <summary>
/// VS Code's <c>FileStat</c>. The property names are the wire contract — an extension reads
/// <c>stat.type</c> and <c>stat.size</c> directly.
/// </summary>
public class ExtensionFileStat
{
    /// <summary>1 = file, 2 = directory (VS Code's FileType).</summary>
    [JsonPropertyName("type")]
    public int Type { get; set; }

    [JsonPropertyName("ctime")]
    public long Ctime { get; set; }

    [JsonPropertyName("mtime")]
    public long Mtime { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }
}
