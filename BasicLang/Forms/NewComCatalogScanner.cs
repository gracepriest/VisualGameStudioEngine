using System;
using System.IO;
using System.Text.Json;

namespace BasicLang.Forms;

/// <summary>
/// Phase 3: scans standard NewCOM search paths for control manifests and registers
/// them as external catalog rows so they appear in the toolbox and can be placed.
/// </summary>
public static class NewComCatalogScanner
{
    public static void Scan()
    {
        var searchPaths = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "components"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".newcom"),
        };
        var env = Environment.GetEnvironmentVariable("NEWCOM_PATH");
        if (!string.IsNullOrEmpty(env))
            searchPaths = searchPaths.Append(env).ToArray();

        foreach (var sp in searchPaths)
        {
            if (!Directory.Exists(sp)) continue;
            foreach (var manifest in Directory.GetFiles(sp, "newcom.json", SearchOption.AllDirectories))
            {
                try { TryRegister(manifest); } catch { }
            }
        }
    }

    private static void TryRegister(string manifestPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = doc.RootElement;
        if (!root.TryGetProperty("kind", out var kind) || kind.GetString() != "control")
            return;

        var name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "NewComControl" : "NewComControl";
        int w = 100, h = 30;
        if (root.TryGetProperty("defaultSize", out var ds) && ds.ValueKind == JsonValueKind.Array && ds.GetArrayLength() >= 2)
        {
            w = ds[0].GetInt32();
            h = ds[1].GetInt32();
        }

        var row = new FormControlDef(
            Kind: "NewComControl",
            WinFormsType: "NewCom.Runtime.NewComControlHost",
            HtmlTag: null,
            HtmlInputType: null,
            IsContainer: false,
            Properties: Array.Empty<FormPropertyDef>(),
            DefaultWidth: w,
            DefaultHeight: h,
            Schematic: FormSchematic.NewComControl,
            Events: null,
            Place: FormPlace.Positioned);

        FormControlCatalog.RegisterExternal(row);
    }
}