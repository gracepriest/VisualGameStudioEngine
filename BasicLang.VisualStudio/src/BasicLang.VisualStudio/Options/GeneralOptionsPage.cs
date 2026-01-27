using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace BasicLang.VisualStudio.Options;

/// <summary>
/// General options page for BasicLang.
/// Accessible via Tools > Options > BasicLang > General
/// </summary>
[Guid(Guids.GeneralOptionsGuidString)]
public class GeneralOptionsPage : DialogPage
{
    private bool _autoStartLanguageServer = true;
    private bool _enableSemanticHighlighting = true;
    private bool _enableInlayHints = true;
    private bool _enableCodeLens = true;
    private bool _enableDiagnostics = true;
    private string _languageServerPath = "";
    private LogLevel _logLevel = LogLevel.Information;

    /// <summary>
    /// Gets or sets whether to automatically start the language server.
    /// </summary>
    [Category("Language Server")]
    [DisplayName("Auto-Start Language Server")]
    [Description("Automatically start the BasicLang language server when a BasicLang file is opened.")]
    [DefaultValue(true)]
    public bool AutoStartLanguageServer
    {
        get => _autoStartLanguageServer;
        set => _autoStartLanguageServer = value;
    }

    /// <summary>
    /// Gets or sets the path to the language server.
    /// </summary>
    [Category("Language Server")]
    [DisplayName("Language Server Path")]
    [Description("Path to the BasicLang.exe language server. Leave empty to auto-detect.")]
    [DefaultValue("")]
    public string LanguageServerPath
    {
        get => _languageServerPath;
        set => _languageServerPath = value;
    }

    /// <summary>
    /// Gets or sets whether to enable semantic highlighting.
    /// </summary>
    [Category("Editor")]
    [DisplayName("Enable Semantic Highlighting")]
    [Description("Enable semantic token highlighting for more accurate syntax colors.")]
    [DefaultValue(true)]
    public bool EnableSemanticHighlighting
    {
        get => _enableSemanticHighlighting;
        set => _enableSemanticHighlighting = value;
    }

    /// <summary>
    /// Gets or sets whether to enable inlay hints.
    /// </summary>
    [Category("Editor")]
    [DisplayName("Enable Inlay Hints")]
    [Description("Show inline hints for parameter names and type annotations.")]
    [DefaultValue(true)]
    public bool EnableInlayHints
    {
        get => _enableInlayHints;
        set => _enableInlayHints = value;
    }

    /// <summary>
    /// Gets or sets whether to enable CodeLens.
    /// </summary>
    [Category("Editor")]
    [DisplayName("Enable CodeLens")]
    [Description("Show reference counts and other information above declarations.")]
    [DefaultValue(true)]
    public bool EnableCodeLens
    {
        get => _enableCodeLens;
        set => _enableCodeLens = value;
    }

    /// <summary>
    /// Gets or sets whether to enable diagnostics.
    /// </summary>
    [Category("Editor")]
    [DisplayName("Enable Diagnostics")]
    [Description("Show errors, warnings, and info messages in the editor.")]
    [DefaultValue(true)]
    public bool EnableDiagnostics
    {
        get => _enableDiagnostics;
        set => _enableDiagnostics = value;
    }

    /// <summary>
    /// Gets or sets the logging level.
    /// </summary>
    [Category("Diagnostics")]
    [DisplayName("Log Level")]
    [Description("The minimum level of messages to log to the output window.")]
    [DefaultValue(LogLevel.Information)]
    public LogLevel LogLevel
    {
        get => _logLevel;
        set => _logLevel = value;
    }

    /// <summary>
    /// Called when the options are saved.
    /// </summary>
    protected override void OnApply(PageApplyEventArgs e)
    {
        base.OnApply(e);

        // Notify the language client of settings changes
        System.Diagnostics.Debug.WriteLine("BasicLang general options saved");
    }
}

/// <summary>
/// Log level options.
/// </summary>
public enum LogLevel
{
    /// <summary>
    /// Show all messages including trace.
    /// </summary>
    Trace,

    /// <summary>
    /// Show debug and higher messages.
    /// </summary>
    Debug,

    /// <summary>
    /// Show info and higher messages.
    /// </summary>
    Information,

    /// <summary>
    /// Show warnings and errors only.
    /// </summary>
    Warning,

    /// <summary>
    /// Show errors only.
    /// </summary>
    Error,

    /// <summary>
    /// Disable logging.
    /// </summary>
    None
}
