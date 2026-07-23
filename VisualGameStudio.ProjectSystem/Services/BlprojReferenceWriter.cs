using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// Adds a &lt;ProjectReference&gt; to an existing .blproj, in place and idempotently.
/// Consumed by the LSP WorkspaceManager for cross-project IntelliSense.
/// Writes BOM-less UTF-8 — XDocument.Save(path) would inject a BOM and corrupt the file.
/// </summary>
public static class BlprojReferenceWriter
{
    public static void AddReference(string blprojPath, string includeRelativePath)
    {
        var doc = XDocument.Load(blprojPath);
        var root = doc.Root;
        if (root == null)
        {
            throw new InvalidOperationException($"'{blprojPath}' has no root element.");
        }

        var alreadyPresent = root.Descendants("ProjectReference")
            .Any(e => string.Equals((string?)e.Attribute("Include"), includeRelativePath, StringComparison.OrdinalIgnoreCase));
        if (alreadyPresent)
        {
            return;
        }

        var itemGroup = root.Elements("ItemGroup").FirstOrDefault(g => g.Elements("ProjectReference").Any());
        if (itemGroup == null)
        {
            itemGroup = new XElement("ItemGroup");
            root.Add(itemGroup);
        }

        itemGroup.Add(new XElement("ProjectReference", new XAttribute("Include", includeRelativePath)));

        using var writer = new StreamWriter(blprojPath, false, new UTF8Encoding(false));
        doc.Save(writer);
    }
}
