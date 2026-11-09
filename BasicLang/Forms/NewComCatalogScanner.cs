using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BasicLang.Forms;

/// <summary>
/// One installed NewCOM control, read from its <c>newcom.json</c> (<c>"kind": "control"</c>).
/// </summary>
/// <param name="Name">What the toolbox calls it — the manifest's <c>name</c>.</param>
/// <param name="Clsid">The GUID the generated code passes to <c>NewComControlHost</c>, normalised to <c>D</c> form.</param>
/// <param name="DefaultWidth">The manifest's <c>defaultSize[0]</c>, else the catalog row's default.</param>
/// <param name="DefaultHeight">The manifest's <c>defaultSize[1]</c>, else the catalog row's default.</param>
/// <param name="ManifestPath">Where it was read from — shown in the toolbox tip, so two installs can be told apart.</param>
public sealed record NewComControlManifest(
    string Name, string Clsid, int DefaultWidth, int DefaultHeight, string ManifestPath);

/// <summary>
/// Finds the NewCOM controls installed on this machine so the designer's toolbox can offer them.
///
/// <para>⛔ An installed control is NOT a catalog row. Every one of them is the SAME catalog kind,
/// <see cref="HostKind"/>, told apart by a <c>Clsid</c> attribute on the placed control — which is
/// what the <c>.blform</c> persists and what <c>RegionWriter</c> turns into
/// <c>New NewCom.Runtime.NewComControlHost("&lt;clsid&gt;")</c>. A row per manifest would put the
/// manifest's name into the document as its element name, so a form would stop loading on any
/// machine without that control installed; and a second row of kind <see cref="HostKind"/> would
/// shadow nothing (<c>Find</c> returns the first) while every list of rows showed it twice.</para>
///
/// <para>⚠ Snapshot, not live: <see cref="Installed"/> is what the last <see cref="Scan()"/> found.
/// The IDE scans once at startup, so a control installed while it runs appears after a restart.</para>
/// </summary>
public static class NewComCatalogScanner
{
    /// <summary>The catalog kind every NewCOM control is placed as. The one spelling of it.</summary>
    public const string HostKind = "NewComControl";

    private static IReadOnlyList<NewComControlManifest> _installed = Array.Empty<NewComControlManifest>();

    /// <summary>What the last scan found, in search-path order, one entry per clsid.</summary>
    public static IReadOnlyList<NewComControlManifest> Installed => _installed;

    /// <summary>The standard NewCOM search paths: <c>./components</c>, <c>~/.newcom</c>, then <c>$NEWCOM_PATH</c>.</summary>
    public static IReadOnlyList<string> DefaultSearchPaths()
    {
        var paths = new List<string>
        {
            Path.Combine(Directory.GetCurrentDirectory(), "components"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".newcom"),
        };

        var env = Environment.GetEnvironmentVariable("NEWCOM_PATH");
        if (!string.IsNullOrEmpty(env))
        {
            paths.AddRange(env.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
        }

        return paths;
    }

    /// <summary>Scans the standard search paths and replaces <see cref="Installed"/>.</summary>
    public static IReadOnlyList<NewComControlManifest> Scan() => Scan(DefaultSearchPaths());

    /// <summary>Scans <paramref name="searchPaths"/> and replaces <see cref="Installed"/>.</summary>
    public static IReadOnlyList<NewComControlManifest> Scan(IEnumerable<string> searchPaths)
    {
        var found = new List<NewComControlManifest>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in searchPaths)
        {
            string[] manifests;
            try
            {
                if (!Directory.Exists(root)) continue;
                manifests = Directory.GetFiles(root, "newcom.json", SearchOption.AllDirectories);
                Array.Sort(manifests, StringComparer.OrdinalIgnoreCase);   // stable order across file systems
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                System.Diagnostics.Trace.TraceWarning($"NewCOM: cannot search '{root}': {ex.Message}");
                continue;
            }

            foreach (var path in manifests)
            {
                // The FIRST install of a clsid wins, so a project's own ./components copy overrides
                // the user-wide one — the same precedence the runtime loader's search order gives.
                if (TryRead(path) is { } manifest && seen.Add(manifest.Clsid))
                {
                    found.Add(manifest);
                }
            }
        }

        _installed = found.AsReadOnly();
        return _installed;
    }

    /// <summary>
    /// Reads one manifest, or null when it is not a usable control manifest: unreadable, not JSON,
    /// not <c>"kind": "control"</c>, no name, or a clsid that is not a GUID.
    ///
    /// <para>⛔ The GUID check is a code-generation guard, not tidiness: the clsid is written verbatim
    /// into a string literal in the user's code-behind, so a manifest saying
    /// <c>"clsid": "x\") : Kill(\"y"</c> must never reach it.</para>
    /// </summary>
    public static NewComControlManifest? TryRead(string manifestPath)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            if (!root.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String ||
                !string.Equals(kind.GetString(), "control", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (!root.TryGetProperty("name", out var nameElement) || nameElement.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(nameElement.GetString()))
            {
                return Skip(manifestPath, "it has no \"name\"");
            }

            if (!root.TryGetProperty("clsid", out var clsidElement) || clsidElement.ValueKind != JsonValueKind.String ||
                !Guid.TryParse(clsidElement.GetString(), out var clsid))
            {
                return Skip(manifestPath, "its \"clsid\" is missing or not a GUID");
            }

            var row = FormControlCatalog.Find(HostKind);
            int width = row?.DefaultWidth ?? 100, height = row?.DefaultHeight ?? 30;
            if (root.TryGetProperty("defaultSize", out var size) && size.ValueKind == JsonValueKind.Array &&
                size.GetArrayLength() >= 2 &&
                size[0].TryGetInt32(out var w) && size[1].TryGetInt32(out var h) && w > 0 && h > 0)
            {
                width = w;
                height = h;
            }

            return new NewComControlManifest(nameElement.GetString()!.Trim(), clsid.ToString("D"), width, height, manifestPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return Skip(manifestPath, ex.Message);
        }
    }

    private static NewComControlManifest? Skip(string path, string why)
    {
        System.Diagnostics.Trace.TraceWarning($"NewCOM: ignoring control manifest '{path}': {why}.");
        return null;
    }
}
