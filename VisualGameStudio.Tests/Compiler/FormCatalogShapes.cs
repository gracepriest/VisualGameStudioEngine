using BasicLang.Forms;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// The ONE answer to "how does a catalog row appear in a document" for every catalog-driven gate
/// (spec §2, Gates row). Three gates each chose their fixture by IsComponent alone; a fifth shape
/// would have been forgotten by all three again.
/// </summary>
internal static class FormCatalogShapes
{
    /// <summary>Adds <paramref name="definition"/>'s canonical shape to <paramref name="document"/> and returns the control it added.</summary>
    public static FormControl Canonical(FormDocument document, FormControlDef definition, string id, string? hostId = null,
        FormGeometry? geometry = null)
    {
        switch (definition.Place)
        {
            case FormPlace.Tray:
            {
                var c = new FormControl { Kind = definition.Kind, Id = id };
                document.Components.Add(c);
                return c;
            }
            case FormPlace.Docked:
            {
                var c = new FormControl { Kind = definition.Kind, Id = id };
                var dock = definition.Property("Dock");
                if (dock?.Default != null) c.Properties["Dock"] = dock.Default;
                document.Controls.Add(c);
                return c;
            }
            case FormPlace.Item:
            {
                // The first DOCKED host row listing it — deterministic, never a menu item hosting a menu item.
                var hostDef = FormControlCatalog.All.First(d => d.Place == FormPlace.Docked && d.Items?.Accepts(definition.Kind) == true);
                var host = Canonical(document, hostDef, hostId ?? id + "Host");
                var c = new FormControl { Kind = definition.Kind, Id = id };
                if (definition.Property("Text") != null) c.Properties["Text"] = id;
                host.Children.Add(c);
                return c;
            }
            default:
            {
                var c = new FormControl
                {
                    Kind = definition.Kind, Id = id, TabIndex = 0,
                    Geometry = geometry ?? (document.Target == FormTarget.Web
                        ? new GridGeometry { Col = 0, Row = 0 }
                        : new PixelGeometry { X = 96, Y = 80, Width = 120, Height = 24, Anchor = "Top" })
                };
                document.Controls.Add(c);
                return c;
            }
        }
    }

    /// <summary>Finds the canonical control of <paramref name="definition"/> again — on a crossed retarget, say.</summary>
    public static FormControl? Locate(FormDocument document, FormControlDef definition) =>
        document.AllControls().Concat(document.AllComponents()).FirstOrDefault(c => c.Kind == definition.Kind);
}
