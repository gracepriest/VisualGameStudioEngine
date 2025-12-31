namespace VisualGameStudio.Core.Models;

public class BuildConfiguration
{
    public string Name { get; set; } = "Debug";
    public string OutputPath { get; set; } = "bin\\Debug";
    public bool DebugSymbols { get; set; } = true;
    public bool Optimize { get; set; }
    public string? DefineConstants { get; set; }
    public WarningLevel WarningLevel { get; set; } = WarningLevel.Default;
    public bool TreatWarningsAsErrors { get; set; }
    public Dictionary<string, string> AdditionalProperties { get; set; } = new();
}

public enum WarningLevel
{
    None = 0,
    Low = 1,
    Default = 2,
    High = 3,
    All = 4
}
