using System.ComponentModel.Composition;
using Microsoft.VisualStudio.ProjectSystem;

namespace BasicLang.VisualStudio.ProjectSystem;

/// <summary>
/// Defines the project capabilities for BasicLang projects.
/// </summary>
internal static class BasicLangProjectCapability
{
    /// <summary>
    /// The BasicLang project capability.
    /// </summary>
    public const string BasicLang = "BasicLang";
}

/// <summary>
/// Marks the project file extensions that should be recognized as BasicLang projects.
/// </summary>
[Export]
[AppliesTo(BasicLangProjectCapability.BasicLang)]
internal class BasicLangProjectExtension
{
    [Import]
    internal UnconfiguredProject? UnconfiguredProject { get; set; }
}
