using System.ComponentModel.Composition;
using Microsoft.VisualStudio.ProjectSystem;

namespace BasicLang.VisualStudio.ProjectSystem;

/// <summary>
/// Provides configuration-specific services for BasicLang projects.
/// </summary>
[Export]
[AppliesTo(BasicLangProjectCapability.BasicLang)]
internal class BasicLangConfiguredProject
{
    /// <summary>
    /// Gets the configured project instance.
    /// </summary>
    [Import]
    internal ConfiguredProject? ConfiguredProject { get; set; }

    /// <summary>
    /// Gets the active configuration.
    /// </summary>
    public string? ActiveConfiguration => ConfiguredProject?.ProjectConfiguration?.Name;

    /// <summary>
    /// Gets the backend setting for BasicLang compilation.
    /// </summary>
    public string GetBackend()
    {
        return BasicLangConstants.BackendCSharp;
    }
}
