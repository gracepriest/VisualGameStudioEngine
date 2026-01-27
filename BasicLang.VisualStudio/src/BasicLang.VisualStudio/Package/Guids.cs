namespace BasicLang.VisualStudio;

/// <summary>
/// GUIDs used throughout the BasicLang Visual Studio extension.
/// </summary>
public static class Guids
{
    // Package GUID - main VS package registration
    public const string PackageGuidString = "95a8f3e1-1234-4567-8901-abcdef123456";
    public static readonly Guid Package = new(PackageGuidString);

    // Command Set GUID - for menu commands
    public const string CommandSetGuidString = "95a8f3e1-1234-4567-8902-abcdef123456";
    public static readonly Guid CommandSet = new(CommandSetGuidString);

    // Project Type GUID - for CPS project system
    public const string ProjectTypeGuidString = "95a8f3e1-1234-4567-8903-abcdef123456";
    public static readonly Guid ProjectType = new(ProjectTypeGuidString);

    // Language Service GUID
    public const string LanguageServiceGuidString = "95a8f3e1-1234-4567-8906-abcdef123456";
    public static readonly Guid LanguageService = new(LanguageServiceGuidString);

    // Editor Factory GUID
    public const string EditorFactoryGuidString = "95a8f3e1-1234-4567-8907-abcdef123456";
    public static readonly Guid EditorFactory = new(EditorFactoryGuidString);

    // Debug Engine GUID
    public const string DebugEngineGuidString = "95a8f3e1-1234-4567-8908-abcdef123456";
    public static readonly Guid DebugEngine = new(DebugEngineGuidString);

    // Options Page GUIDs
    public const string GeneralOptionsGuidString = "95a8f3e1-1234-4567-8909-abcdef123456";
    public static readonly Guid GeneralOptions = new(GeneralOptionsGuidString);

    public const string CompilerOptionsGuidString = "95a8f3e1-1234-4567-890a-abcdef123456";
    public static readonly Guid CompilerOptions = new(CompilerOptionsGuidString);
}

/// <summary>
/// Constants for BasicLang extension.
/// </summary>
public static class BasicLangConstants
{
    public const string LanguageName = "BasicLang";
    public const string ContentTypeName = "basiclang";
    public const string FileExtension = ".bl";
    public const string FileExtension2 = ".bas";
    public const string ProjectExtension = ".blproj";
    public const string ProjectTypeCapability = "BasicLang";

    // File display names
    public const string SourceFileDisplayName = "BasicLang Source File";
    public const string ProjectFileDisplayName = "BasicLang Project";

    // Compiler backends
    public const string BackendCSharp = "CSharp";
    public const string BackendMSIL = "MSIL";
    public const string BackendLLVM = "LLVM";
    public const string BackendCPlusPlus = "CPlusPlus";
}
