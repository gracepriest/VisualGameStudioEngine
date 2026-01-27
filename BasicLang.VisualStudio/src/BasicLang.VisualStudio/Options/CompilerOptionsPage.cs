using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace BasicLang.VisualStudio.Options;

/// <summary>
/// Compiler options page for BasicLang.
/// Accessible via Tools > Options > BasicLang > Compiler
/// </summary>
[Guid(Guids.CompilerOptionsGuidString)]
public class CompilerOptionsPage : DialogPage
{
    private CompilerBackend _defaultBackend = CompilerBackend.CSharp;
    private bool _treatWarningsAsErrors = false;
    private bool _enableOptimizations = false;
    private bool _generateDebugInfo = true;
    private string _additionalCompilerArgs = "";
    private TargetFramework _defaultTargetFramework = TargetFramework.Net80;
    private bool _enableNullableReferenceTypes = true;
    private bool _enableImplicitUsings = true;

    /// <summary>
    /// Gets or sets the default compiler backend.
    /// </summary>
    [Category("Compilation")]
    [DisplayName("Default Backend")]
    [Description("The default compiler backend for new projects.")]
    [DefaultValue(CompilerBackend.CSharp)]
    public CompilerBackend DefaultBackend
    {
        get => _defaultBackend;
        set => _defaultBackend = value;
    }

    /// <summary>
    /// Gets or sets whether to treat warnings as errors.
    /// </summary>
    [Category("Compilation")]
    [DisplayName("Treat Warnings as Errors")]
    [Description("Treat all compiler warnings as errors.")]
    [DefaultValue(false)]
    public bool TreatWarningsAsErrors
    {
        get => _treatWarningsAsErrors;
        set => _treatWarningsAsErrors = value;
    }

    /// <summary>
    /// Gets or sets whether to enable optimizations.
    /// </summary>
    [Category("Compilation")]
    [DisplayName("Enable Optimizations")]
    [Description("Enable compiler optimizations (may affect debugging).")]
    [DefaultValue(false)]
    public bool EnableOptimizations
    {
        get => _enableOptimizations;
        set => _enableOptimizations = value;
    }

    /// <summary>
    /// Gets or sets whether to generate debug information.
    /// </summary>
    [Category("Compilation")]
    [DisplayName("Generate Debug Info")]
    [Description("Generate debug symbols for debugging support.")]
    [DefaultValue(true)]
    public bool GenerateDebugInfo
    {
        get => _generateDebugInfo;
        set => _generateDebugInfo = value;
    }

    /// <summary>
    /// Gets or sets additional compiler arguments.
    /// </summary>
    [Category("Advanced")]
    [DisplayName("Additional Arguments")]
    [Description("Additional command-line arguments to pass to the BasicLang compiler.")]
    [DefaultValue("")]
    public string AdditionalCompilerArgs
    {
        get => _additionalCompilerArgs;
        set => _additionalCompilerArgs = value;
    }

    /// <summary>
    /// Gets or sets the default target framework.
    /// </summary>
    [Category("Project Defaults")]
    [DisplayName("Default Target Framework")]
    [Description("The default target framework for new projects.")]
    [DefaultValue(TargetFramework.Net80)]
    public TargetFramework DefaultTargetFramework
    {
        get => _defaultTargetFramework;
        set => _defaultTargetFramework = value;
    }

    /// <summary>
    /// Gets or sets whether to enable nullable reference types.
    /// </summary>
    [Category("Project Defaults")]
    [DisplayName("Enable Nullable Reference Types")]
    [Description("Enable nullable reference types in new projects.")]
    [DefaultValue(true)]
    public bool EnableNullableReferenceTypes
    {
        get => _enableNullableReferenceTypes;
        set => _enableNullableReferenceTypes = value;
    }

    /// <summary>
    /// Gets or sets whether to enable implicit usings.
    /// </summary>
    [Category("Project Defaults")]
    [DisplayName("Enable Implicit Usings")]
    [Description("Enable implicit global usings in new projects.")]
    [DefaultValue(true)]
    public bool EnableImplicitUsings
    {
        get => _enableImplicitUsings;
        set => _enableImplicitUsings = value;
    }

    /// <summary>
    /// Called when the options are saved.
    /// </summary>
    protected override void OnApply(PageApplyEventArgs e)
    {
        base.OnApply(e);

        System.Diagnostics.Debug.WriteLine("BasicLang compiler options saved");
    }
}

/// <summary>
/// Available compiler backends.
/// </summary>
public enum CompilerBackend
{
    /// <summary>
    /// Transpile to C# (recommended, best .NET integration).
    /// </summary>
    [Description("C# (Recommended)")]
    CSharp,

    /// <summary>
    /// Direct MSIL generation.
    /// </summary>
    [Description("MSIL (Direct IL)")]
    MSIL,

    /// <summary>
    /// Native code generation via LLVM.
    /// </summary>
    [Description("LLVM (Native)")]
    LLVM,

    /// <summary>
    /// Transpile to C++.
    /// </summary>
    [Description("C++ (Transpilation)")]
    CPlusPlus
}

/// <summary>
/// Available target frameworks.
/// </summary>
public enum TargetFramework
{
    /// <summary>
    /// .NET 6.0
    /// </summary>
    [Description(".NET 6.0")]
    Net60,

    /// <summary>
    /// .NET 7.0
    /// </summary>
    [Description(".NET 7.0")]
    Net70,

    /// <summary>
    /// .NET 8.0
    /// </summary>
    [Description(".NET 8.0")]
    Net80,

    /// <summary>
    /// .NET 9.0
    /// </summary>
    [Description(".NET 9.0")]
    Net90,

    /// <summary>
    /// .NET Framework 4.8
    /// </summary>
    [Description(".NET Framework 4.8")]
    NetFramework48
}
