namespace VisualGameStudio.Core.Abstractions.Services;

/// <summary>
/// Service for managing project templates.
/// </summary>
public interface IProjectTemplateService
{
    /// <summary>
    /// Gets all available solution types (backends).
    /// </summary>
    IReadOnlyList<SolutionType> GetSolutionTypes();

    /// <summary>
    /// Gets project templates for a given solution type.
    /// </summary>
    IReadOnlyList<ProjectTemplate> GetProjectTemplates(SolutionType solutionType);

    /// <summary>
    /// Gets all project templates.
    /// </summary>
    IReadOnlyList<ProjectTemplate> GetAllProjectTemplates();

    /// <summary>
    /// Creates a new project from a template.
    /// </summary>
    Task<ProjectCreationResult> CreateProjectAsync(CreateProjectOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new solution.
    /// </summary>
    Task<SolutionCreationResult> CreateSolutionAsync(CreateSolutionOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates project creation options.
    /// </summary>
    ProjectValidationResult ValidateProjectOptions(CreateProjectOptions options);

    /// <summary>
    /// Registers a custom project template.
    /// </summary>
    void RegisterTemplate(ProjectTemplate template);

    /// <summary>
    /// Gets recently used templates.
    /// </summary>
    IReadOnlyList<ProjectTemplate> GetRecentTemplates();
}

#region Solution Types

/// <summary>
/// Represents a solution type (backend/target).
/// </summary>
public class SolutionType
{
    /// <summary>
    /// Unique identifier.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Display name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Description.
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// Icon path or key.
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>
    /// File extension for project files.
    /// </summary>
    public string ProjectExtension { get; set; } = ".blproj";

    /// <summary>
    /// File extension for solution files.
    /// </summary>
    public string SolutionExtension { get; set; } = ".blsln";

    /// <summary>
    /// Source file extension.
    /// </summary>
    public string SourceExtension { get; set; } = ".bl";

    /// <summary>
    /// Whether this solution type is available.
    /// </summary>
    public bool IsAvailable { get; set; } = true;

    /// <summary>
    /// Reason if not available.
    /// </summary>
    public string? UnavailableReason { get; set; }
}

/// <summary>
/// Built-in solution types.
/// </summary>
public static class SolutionTypes
{
    /// <summary>
    /// .NET (C#) solution type - compiles to .NET assemblies.
    /// </summary>
    public static readonly SolutionType DotNet = new()
    {
        Id = "dotnet",
        Name = ".NET (C#)",
        Description = "Compile BasicLang to .NET assemblies using the C# compiler. Full .NET ecosystem support.",
        Icon = "dotnet",
        ProjectExtension = ".blproj",
        SolutionExtension = ".blsln",
        SourceExtension = ".bl"
    };

    /// <summary>
    /// MSIL solution type - compiles directly to IL.
    /// </summary>
    public static readonly SolutionType Msil = new()
    {
        Id = "msil",
        Name = "MSIL",
        Description = "Compile BasicLang directly to Microsoft Intermediate Language (MSIL/CIL). Low-level .NET targeting.",
        Icon = "msil",
        ProjectExtension = ".blproj",
        SolutionExtension = ".blsln",
        SourceExtension = ".bl"
    };

    /// <summary>
    /// Native (C++) solution type.
    /// </summary>
    public static readonly SolutionType Native = new()
    {
        Id = "native",
        Name = "Native (C++)",
        Description = "Compile BasicLang to native code via C++ transpilation. High performance, no runtime required.",
        Icon = "cpp",
        ProjectExtension = ".blproj",
        SolutionExtension = ".blsln",
        SourceExtension = ".bl"
    };

    /// <summary>
    /// LLVM solution type.
    /// </summary>
    public static readonly SolutionType Llvm = new()
    {
        Id = "llvm",
        Name = "LLVM",
        Description = "Compile BasicLang to LLVM IR for cross-platform native compilation. Maximum portability.",
        Icon = "llvm",
        ProjectExtension = ".blproj",
        SolutionExtension = ".blsln",
        SourceExtension = ".bl"
    };

    /// <summary>
    /// All solution types.
    /// </summary>
    public static IReadOnlyList<SolutionType> All => new[] { DotNet, Msil, Native, Llvm };
}

#endregion

#region Project Templates

/// <summary>
/// Represents a project template.
/// </summary>
public class ProjectTemplate
{
    /// <summary>
    /// Unique identifier.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Display name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Short description.
    /// </summary>
    public string ShortDescription { get; set; } = "";

    /// <summary>
    /// Full description.
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// Icon path or key.
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>
    /// Category for grouping.
    /// </summary>
    public string Category { get; set; } = "General";

    /// <summary>
    /// Tags for searching.
    /// </summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// Solution types this template supports.
    /// </summary>
    public List<string> SupportedSolutionTypes { get; set; } = new();

    /// <summary>
    /// Template files.
    /// </summary>
    public List<TemplateFile> Files { get; set; } = new();

    /// <summary>
    /// Default namespace.
    /// </summary>
    public string DefaultNamespace { get; set; } = "";

    /// <summary>
    /// Whether to create a solution file.
    /// </summary>
    public bool CreateSolution { get; set; } = true;

    /// <summary>
    /// Dependencies/packages to include.
    /// </summary>
    public List<PackageReference> Dependencies { get; set; } = new();

    /// <summary>
    /// Post-creation commands.
    /// </summary>
    public List<string> PostCreateCommands { get; set; } = new();

    /// <summary>
    /// Order for display.
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Whether this is a built-in template.
    /// </summary>
    public bool IsBuiltIn { get; set; }
}

/// <summary>
/// A file in a project template.
/// </summary>
public class TemplateFile
{
    /// <summary>
    /// Relative path in the project.
    /// </summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// File content (with template variables).
    /// </summary>
    public string Content { get; set; } = "";

    /// <summary>
    /// Whether this file should be opened after creation.
    /// </summary>
    public bool OpenAfterCreate { get; set; }

    /// <summary>
    /// Encoding to use (default UTF-8).
    /// </summary>
    public string Encoding { get; set; } = "utf-8";
}

/// <summary>
/// A package reference.
/// </summary>
public class PackageReference
{
    /// <summary>
    /// Package name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Package version.
    /// </summary>
    public string Version { get; set; } = "";

    /// <summary>
    /// Whether this is optional.
    /// </summary>
    public bool Optional { get; set; }
}

/// <summary>
/// Built-in project templates.
/// </summary>
public static class ProjectTemplates
{
    #region Console App Templates

    public static readonly ProjectTemplate ConsoleApp = new()
    {
        Id = "console-app",
        Name = "Console Application",
        ShortDescription = "A command-line application",
        Description = "A simple console application that runs in the terminal. Perfect for scripts, tools, and learning BasicLang.",
        Icon = "console",
        Category = "Console",
        Tags = new List<string> { "console", "terminal", "cli", "command-line" },
        SupportedSolutionTypes = new List<string> { "dotnet", "msil", "native", "llvm" },
        Order = 1,
        IsBuiltIn = true
    };

    #endregion

    #region Game Development Templates

    public static readonly ProjectTemplate GameApp = new()
    {
        Id = "game-app",
        Name = "Game Development Application",
        ShortDescription = "A game using the BasicLang game framework",
        Description = "A game application using the BasicLang game development framework with rendering, input handling, and game loop.",
        Icon = "game",
        Category = "Games",
        Tags = new List<string> { "game", "gamedev", "graphics", "2d", "3d" },
        SupportedSolutionTypes = new List<string> { "dotnet", "msil", "native", "llvm" },
        Order = 2,
        IsBuiltIn = true
    };

    #endregion

    #region UI Templates (.NET and MSIL only)

    public static readonly ProjectTemplate WinFormsApp = new()
    {
        Id = "winforms-app",
        Name = "Windows Forms Application",
        ShortDescription = "A Windows desktop app using Windows Forms",
        Description = "A Windows desktop application using the Windows Forms UI framework. Classic Windows GUI development.",
        Icon = "winforms",
        Category = "Desktop",
        Tags = new List<string> { "winforms", "windows", "desktop", "gui", "ui" },
        SupportedSolutionTypes = new List<string> { "dotnet", "msil" },
        Order = 3,
        IsBuiltIn = true
    };

    public static readonly ProjectTemplate WpfApp = new()
    {
        Id = "wpf-app",
        Name = "WPF Application",
        ShortDescription = "A Windows desktop app using WPF",
        Description = "A Windows desktop application using Windows Presentation Foundation (WPF). Modern Windows UI with XAML.",
        Icon = "wpf",
        Category = "Desktop",
        Tags = new List<string> { "wpf", "xaml", "windows", "desktop", "gui", "ui" },
        SupportedSolutionTypes = new List<string> { "dotnet", "msil" },
        Order = 4,
        IsBuiltIn = true
    };

    public static readonly ProjectTemplate AvaloniaApp = new()
    {
        Id = "avalonia-app",
        Name = "Avalonia UI Application",
        ShortDescription = "A cross-platform desktop app using Avalonia",
        Description = "A cross-platform desktop application using Avalonia UI. Build once, run on Windows, Linux, and macOS.",
        Icon = "avalonia",
        Category = "Desktop",
        Tags = new List<string> { "avalonia", "cross-platform", "desktop", "gui", "ui", "xaml" },
        SupportedSolutionTypes = new List<string> { "dotnet", "msil" },
        Order = 5,
        IsBuiltIn = true
    };

    #endregion

    #region Library Templates

    public static readonly ProjectTemplate ClassLibrary = new()
    {
        Id = "class-library",
        Name = "Class Library",
        ShortDescription = "A reusable code library",
        Description = "A class library for creating reusable code modules. Can be referenced by other projects.",
        Icon = "library",
        Category = "Library",
        Tags = new List<string> { "library", "module", "reusable", "dll" },
        SupportedSolutionTypes = new List<string> { "dotnet", "msil", "native", "llvm" },
        Order = 6,
        IsBuiltIn = true,
        CreateSolution = false
    };

    public static readonly ProjectTemplate WebApi = new()
    {
        Id = "web-api",
        Name = "Web API",
        ShortDescription = "A REST API web service",
        Description = "A web API service using ASP.NET Core. Build RESTful services and HTTP endpoints.",
        Icon = "webapi",
        Category = "Web",
        Tags = new List<string> { "web", "api", "rest", "http", "service" },
        SupportedSolutionTypes = new List<string> { "dotnet" },
        Order = 7,
        IsBuiltIn = true
    };

    public static readonly ProjectTemplate UnitTest = new()
    {
        Id = "unit-test",
        Name = "Unit Test Project",
        ShortDescription = "A unit testing project",
        Description = "A unit test project for testing your code. Includes test framework setup.",
        Icon = "test",
        Category = "Testing",
        Tags = new List<string> { "test", "unit", "testing", "nunit", "xunit" },
        SupportedSolutionTypes = new List<string> { "dotnet", "msil" },
        Order = 8,
        IsBuiltIn = true,
        CreateSolution = false
    };

    #endregion

    /// <summary>
    /// All built-in templates.
    /// </summary>
    public static IReadOnlyList<ProjectTemplate> All => new[]
    {
        ConsoleApp,
        GameApp,
        WinFormsApp,
        WpfApp,
        AvaloniaApp,
        ClassLibrary,
        WebApi,
        UnitTest
    };
}

#endregion

#region Creation Options and Results

/// <summary>
/// Options for creating a project.
/// </summary>
public class CreateProjectOptions
{
    /// <summary>
    /// Project name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Project location (parent directory).
    /// </summary>
    public string Location { get; set; } = "";

    /// <summary>
    /// Solution type.
    /// </summary>
    public SolutionType SolutionType { get; set; } = SolutionTypes.DotNet;

    /// <summary>
    /// Project template.
    /// </summary>
    public ProjectTemplate Template { get; set; } = ProjectTemplates.ConsoleApp;

    /// <summary>
    /// Whether to create solution folder.
    /// </summary>
    public bool CreateSolutionFolder { get; set; } = true;

    /// <summary>
    /// Whether to create git repository.
    /// </summary>
    public bool CreateGitRepository { get; set; } = true;

    /// <summary>
    /// Whether to add to existing solution.
    /// </summary>
    public bool AddToExistingSolution { get; set; }

    /// <summary>
    /// Existing solution path (if adding to solution).
    /// </summary>
    public string? ExistingSolutionPath { get; set; }

    /// <summary>
    /// Custom namespace (defaults to project name).
    /// </summary>
    public string? Namespace { get; set; }

    /// <summary>
    /// Target framework (for .NET projects).
    /// </summary>
    public string TargetFramework { get; set; } = "net8.0";
}

/// <summary>
/// Options for creating a solution.
/// </summary>
public class CreateSolutionOptions
{
    /// <summary>
    /// Solution name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Solution location (parent directory).
    /// </summary>
    public string Location { get; set; } = "";

    /// <summary>
    /// Solution type.
    /// </summary>
    public SolutionType SolutionType { get; set; } = SolutionTypes.DotNet;

    /// <summary>
    /// Whether to create git repository.
    /// </summary>
    public bool CreateGitRepository { get; set; } = true;

    /// <summary>
    /// Initial projects to create.
    /// </summary>
    public List<CreateProjectOptions> InitialProjects { get; set; } = new();
}

/// <summary>
/// Result of project creation.
/// </summary>
public class ProjectCreationResult
{
    /// <summary>
    /// Whether creation succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Path to the created project file.
    /// </summary>
    public string? ProjectPath { get; set; }

    /// <summary>
    /// Path to the solution file (if created/modified).
    /// </summary>
    public string? SolutionPath { get; set; }

    /// <summary>
    /// Error message if failed.
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Files that should be opened.
    /// </summary>
    public List<string> FilesToOpen { get; set; } = new();

    /// <summary>
    /// Warnings during creation.
    /// </summary>
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// Result of solution creation.
/// </summary>
public class SolutionCreationResult
{
    /// <summary>
    /// Whether creation succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Path to the created solution file.
    /// </summary>
    public string? SolutionPath { get; set; }

    /// <summary>
    /// Paths to created project files.
    /// </summary>
    public List<string> ProjectPaths { get; set; } = new();

    /// <summary>
    /// Error message if failed.
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Files that should be opened.
    /// </summary>
    public List<string> FilesToOpen { get; set; } = new();
}

/// <summary>
/// Result of project option validation.
/// </summary>
public class ProjectValidationResult
{
    /// <summary>
    /// Whether options are valid.
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// Validation errors.
    /// </summary>
    public List<string> Errors { get; set; } = new();

    /// <summary>
    /// Validation warnings.
    /// </summary>
    public List<string> Warnings { get; set; } = new();
}

#endregion
