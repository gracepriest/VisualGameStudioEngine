using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.ProjectSystem;

namespace BasicLang.VisualStudio.ProjectSystem;

/// <summary>
/// Provides custom icons and tree modifications for BasicLang projects in Solution Explorer.
/// </summary>
[Export(typeof(IProjectTreePropertiesProvider))]
[AppliesTo(BasicLangProjectCapability.BasicLang)]
internal class BasicLangProjectTreeProvider : IProjectTreePropertiesProvider
{
    /// <summary>
    /// Calculates the tree item properties for display in Solution Explorer.
    /// </summary>
    public void CalculatePropertyValues(IProjectTreeCustomizablePropertyContext propertyContext,
        IProjectTreeCustomizablePropertyValues propertyValues)
    {
        if (propertyContext.Metadata == null)
            return;

        var itemType = propertyContext.Metadata.TryGetValue("ItemType", out var value) ? value : null;
        var extension = Path.GetExtension(propertyContext.ItemName)?.ToLowerInvariant();

        // Set icons based on file type
        switch (extension)
        {
            case ".bas":
            case ".bl":
                // Use VB file icon for BasicLang source files
                SetIcon(propertyValues, KnownMonikers.VBFileNode);
                break;

            case ".blproj":
                // Use VB project icon for BasicLang projects
                SetIcon(propertyValues, KnownMonikers.VBProjectNode);
                break;
        }

        // Handle special item types
        if (string.Equals(itemType, "BasicLangCompile", StringComparison.OrdinalIgnoreCase))
        {
            SetIcon(propertyValues, KnownMonikers.VBFileNode);
        }
    }

    /// <summary>
    /// Sets both regular and expanded icons.
    /// </summary>
    private static void SetIcon(IProjectTreeCustomizablePropertyValues propertyValues, ImageMoniker moniker)
    {
        propertyValues.Icon = new ProjectImageMoniker(moniker.Guid, moniker.Id);
        propertyValues.ExpandedIcon = new ProjectImageMoniker(moniker.Guid, moniker.Id);
    }
}
