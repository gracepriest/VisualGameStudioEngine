using System.ComponentModel.Composition;
using Microsoft.VisualStudio.ProjectSystem;

namespace BasicLang.VisualStudio.ProjectSystem;

/// <summary>
/// Exports for BasicLang unconfigured project.
/// </summary>
[Export]
[AppliesTo(BasicLangProjectCapability.BasicLang)]
internal class BasicLangUnconfiguredProject
{
    /// <summary>
    /// Gets the UnconfiguredProject instance for this project.
    /// </summary>
    [Import]
    internal UnconfiguredProject? UnconfiguredProject { get; set; }

    /// <summary>
    /// Gets the project threading service.
    /// </summary>
    [Import]
    internal IProjectThreadingService? ThreadingService { get; set; }

    /// <summary>
    /// Gets the project GUID.
    /// </summary>
    public Guid ProjectGuid => Guids.ProjectType;

    /// <summary>
    /// Gets a friendly name for the project type.
    /// </summary>
    public string ProjectTypeName => BasicLangConstants.LanguageName;
}
