using BasicLang.Compiler.SemanticAnalysis;

namespace BasicLang.Forms;

/// <summary>
/// Task 34's single answer to "is this name one of the portable library's types?". Shared by the
/// analyzer's web/desktop errors, the JavaScript backend's BL7007 reword and the source-level
/// checks. The catalog is the source of truth for control kinds; <see cref="PortableMembers"/>
/// supplies the method-manifest classes the catalog does not list.
/// </summary>
public static class PortableLibraryTypes
{
    private static readonly HashSet<string> SupportClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Control", "Form", "ControlCollection", "MessageBox", "Padding", "Cursor", "Cursors",
        "ObjectCollection"
    };

    /// <summary>Drawing types with no declaration in the portable library.</summary>
    private static readonly HashSet<string> DrawingTypeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Bitmap", "Image", "Icon", "Graphics", "Pen", "Brush", "SolidBrush", "Font", "FontFamily",
        "Rectangle", "RectangleF", "PointF", "SizeF"
    };

    /// <summary>A catalog control kind (whether or not the web library declares it).</summary>
    public static bool IsKnownWinFormsKind(string? name) =>
        !string.IsNullOrEmpty(name) && FormControlCatalog.Find(name) != null;

    /// <summary>
    /// A control class the library ships (or the base pair the catalog implies). Tray components
    /// and strip items are library classes, but they are not controls for
    /// <c>RuntimeControlCreation</c> (spec §5.11: <c>New</c> of a library CONTROL).
    /// </summary>
    public static bool IsControlClass(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (string.Equals(name, "Control", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Form", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var def = FormControlCatalog.Find(name);
        return def != null && def.Place is FormPlace.Positioned or FormPlace.Docked;
    }

    /// <summary>A class the portable library declares, by simple name.</summary>
    public static bool IsLibraryClass(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        var simple = SimpleName(name);
        return IsKnownWinFormsKind(simple)
            || string.Equals(simple, "Control", StringComparison.OrdinalIgnoreCase)
            || string.Equals(simple, "Form", StringComparison.OrdinalIgnoreCase)
            || SupportClasses.Contains(simple)
            || PortableMembers.Manifest.Any(m => string.Equals(m.DeclaringType, simple, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The receiver's declared type names a library class, directly or through its base chain
    /// (<c>Me</c> inside a class that <c>Inherits Form</c>). Returns the library class name the
    /// chain supplies, or null when the chain never reaches one.
    /// </summary>
    public static string? LibraryTypeName(TypeInfo? type)
    {
        for (var depth = 0; type != null && depth < 64; depth++)
        {
            if (IsLibraryClass(type.Name)) return SimpleName(type.Name);
            type = type.BaseType;
        }
        return null;
    }

    public static bool IsLibraryReceiver(TypeInfo? type) => LibraryTypeName(type) != null;

    /// <summary>A System.Drawing type that has no place in the browser and no library declaration.</summary>
    public static bool IsUnavailableDrawingType(string? name) =>
        !string.IsNullOrEmpty(name) && DrawingTypeNames.Contains(SimpleName(name));

    public static string SimpleName(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot < 0 ? name : name[(dot + 1)..];
    }
}
