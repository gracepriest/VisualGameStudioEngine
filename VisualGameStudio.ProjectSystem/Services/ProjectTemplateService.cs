using System.Text;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// Service for managing project templates.
/// </summary>
public class ProjectTemplateService : IProjectTemplateService
{
    private readonly List<ProjectTemplate> _customTemplates = new();
    private readonly List<string> _recentTemplateIds = new();
    private readonly IGitService? _gitService;

    public ProjectTemplateService(IGitService? gitService = null)
    {
        _gitService = gitService;
    }

    public IReadOnlyList<SolutionType> GetSolutionTypes()
    {
        return SolutionTypes.All;
    }

    public IReadOnlyList<ProjectTemplate> GetProjectTemplates(SolutionType solutionType)
    {
        return GetAllProjectTemplates()
            .Where(t => t.SupportedSolutionTypes.Contains(solutionType.Id))
            .OrderBy(t => t.Order)
            .ToList();
    }

    public IReadOnlyList<ProjectTemplate> GetAllProjectTemplates()
    {
        var templates = new List<ProjectTemplate>();
        templates.AddRange(ProjectTemplates.All);
        templates.AddRange(_customTemplates);
        return templates.OrderBy(t => t.Order).ToList();
    }

    public async Task<ProjectCreationResult> CreateProjectAsync(CreateProjectOptions options, CancellationToken cancellationToken = default)
    {
        var result = new ProjectCreationResult();

        // Validate options
        var validation = ValidateProjectOptions(options);
        if (!validation.IsValid)
        {
            result.Error = string.Join("; ", validation.Errors);
            return result;
        }

        result.Warnings.AddRange(validation.Warnings);

        try
        {
            // Determine paths
            var projectDir = options.CreateSolutionFolder
                ? Path.Combine(options.Location, options.Name, options.Name)
                : Path.Combine(options.Location, options.Name);

            var solutionDir = options.CreateSolutionFolder
                ? Path.Combine(options.Location, options.Name)
                : options.Location;

            // Create directories
            Directory.CreateDirectory(projectDir);

            // Generate project files
            var projectFile = await GenerateProjectFilesAsync(projectDir, options, cancellationToken);
            result.ProjectPath = projectFile;

            // Generate source files
            var filesToOpen = await GenerateSourceFilesAsync(projectDir, options, cancellationToken);
            result.FilesToOpen.AddRange(filesToOpen);

            // Create or update solution
            if (!options.AddToExistingSolution && options.Template.CreateSolution)
            {
                var solutionFile = await CreateSolutionFileAsync(solutionDir, options, projectFile, cancellationToken);
                result.SolutionPath = solutionFile;
            }
            else if (options.AddToExistingSolution && !string.IsNullOrEmpty(options.ExistingSolutionPath))
            {
                await AddProjectToSolutionAsync(options.ExistingSolutionPath, projectFile, cancellationToken);
                result.SolutionPath = options.ExistingSolutionPath;
            }

            // Initialize git repository
            if (options.CreateGitRepository && _gitService != null)
            {
                await _gitService.InitRepositoryAsync(solutionDir);
                await CreateGitIgnoreAsync(solutionDir, options.SolutionType, cancellationToken);
            }

            // Track recent template
            TrackRecentTemplate(options.Template.Id);

            result.Success = true;
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
        }

        return result;
    }

    public async Task<SolutionCreationResult> CreateSolutionAsync(CreateSolutionOptions options, CancellationToken cancellationToken = default)
    {
        var result = new SolutionCreationResult();

        try
        {
            var solutionDir = Path.Combine(options.Location, options.Name);
            Directory.CreateDirectory(solutionDir);

            // Create solution file
            var solutionFile = Path.Combine(solutionDir, $"{options.Name}{options.SolutionType.SolutionExtension}");
            await File.WriteAllTextAsync(solutionFile, GenerateEmptySolutionContent(options.Name), cancellationToken);
            result.SolutionPath = solutionFile;

            // Create initial projects
            foreach (var projectOptions in options.InitialProjects)
            {
                projectOptions.Location = solutionDir;
                projectOptions.AddToExistingSolution = true;
                projectOptions.ExistingSolutionPath = solutionFile;
                projectOptions.CreateSolutionFolder = false;
                projectOptions.CreateGitRepository = false;

                var projectResult = await CreateProjectAsync(projectOptions, cancellationToken);
                if (projectResult.Success && projectResult.ProjectPath != null)
                {
                    result.ProjectPaths.Add(projectResult.ProjectPath);
                    result.FilesToOpen.AddRange(projectResult.FilesToOpen);
                }
            }

            // Initialize git repository
            if (options.CreateGitRepository && _gitService != null)
            {
                await _gitService.InitRepositoryAsync(solutionDir);
                await CreateGitIgnoreAsync(solutionDir, options.SolutionType, cancellationToken);
            }

            result.Success = true;
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
        }

        return result;
    }

    public ProjectValidationResult ValidateProjectOptions(CreateProjectOptions options)
    {
        var result = new ProjectValidationResult { IsValid = true };

        // Validate name
        if (string.IsNullOrWhiteSpace(options.Name))
        {
            result.Errors.Add("Project name is required.");
            result.IsValid = false;
        }
        else if (!IsValidProjectName(options.Name))
        {
            result.Errors.Add("Project name contains invalid characters.");
            result.IsValid = false;
        }

        // Validate location
        if (string.IsNullOrWhiteSpace(options.Location))
        {
            result.Errors.Add("Project location is required.");
            result.IsValid = false;
        }
        else if (!Directory.Exists(options.Location))
        {
            result.Warnings.Add("Location directory does not exist and will be created.");
        }

        // Check if project already exists
        var projectDir = options.CreateSolutionFolder
            ? Path.Combine(options.Location, options.Name)
            : options.Location;

        if (Directory.Exists(Path.Combine(projectDir, options.Name)))
        {
            result.Warnings.Add("A folder with this name already exists. Files may be overwritten.");
        }

        // Validate template supports solution type
        if (!options.Template.SupportedSolutionTypes.Contains(options.SolutionType.Id))
        {
            result.Errors.Add($"Template '{options.Template.Name}' does not support solution type '{options.SolutionType.Name}'.");
            result.IsValid = false;
        }

        return result;
    }

    public void RegisterTemplate(ProjectTemplate template)
    {
        _customTemplates.Add(template);
    }

    public IReadOnlyList<ProjectTemplate> GetRecentTemplates()
    {
        return _recentTemplateIds
            .Select(id => GetAllProjectTemplates().FirstOrDefault(t => t.Id == id))
            .Where(t => t != null)
            .Cast<ProjectTemplate>()
            .ToList();
    }

    #region Private Methods

    private static bool IsValidProjectName(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return !name.Any(c => invalidChars.Contains(c)) &&
               !string.IsNullOrWhiteSpace(name) &&
               !name.StartsWith('.') &&
               name.Length <= 100;
    }

    private void TrackRecentTemplate(string templateId)
    {
        _recentTemplateIds.Remove(templateId);
        _recentTemplateIds.Insert(0, templateId);
        while (_recentTemplateIds.Count > 10)
        {
            _recentTemplateIds.RemoveAt(_recentTemplateIds.Count - 1);
        }
    }

    private async Task<string> GenerateProjectFilesAsync(string projectDir, CreateProjectOptions options, CancellationToken cancellationToken)
    {
        var projectFile = Path.Combine(projectDir, $"{options.Name}{options.SolutionType.ProjectExtension}");

        var content = GenerateProjectFileContent(options);
        await File.WriteAllTextAsync(projectFile, content, cancellationToken);

        return projectFile;
    }

    private string GenerateProjectFileContent(CreateProjectOptions options)
    {
        var outputType = GetOutputType(options.Template);
        var targetBackend = options.SolutionType.Id switch
        {
            "dotnet" => "CSharp",
            "msil" => "MSIL",
            "native" => "Cpp",
            "llvm" => "LLVM",
            _ => "CSharp"
        };

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<BasicLangProject Version=\"1.0\">");
        sb.AppendLine("  <PropertyGroup>");
        sb.AppendLine($"    <ProjectName>{options.Name}</ProjectName>");
        sb.AppendLine($"    <OutputType>{(outputType == "exe" ? "Exe" : "WinExe")}</OutputType>");
        sb.AppendLine($"    <RootNamespace>{options.Namespace ?? options.Name}</RootNamespace>");
        sb.AppendLine($"    <TargetBackend>{targetBackend}</TargetBackend>");
        sb.AppendLine("  </PropertyGroup>");
        sb.AppendLine("  <PropertyGroup Condition=\"'$(Configuration)' == 'Debug'\">");
        sb.AppendLine("    <OutputPath>bin\\Debug</OutputPath>");
        sb.AppendLine("    <DebugSymbols>true</DebugSymbols>");
        sb.AppendLine("    <Optimize>false</Optimize>");
        sb.AppendLine("  </PropertyGroup>");
        sb.AppendLine("  <PropertyGroup Condition=\"'$(Configuration)' == 'Release'\">");
        sb.AppendLine("    <OutputPath>bin\\Release</OutputPath>");
        sb.AppendLine("    <DebugSymbols>false</DebugSymbols>");
        sb.AppendLine("    <Optimize>true</Optimize>");
        sb.AppendLine("  </PropertyGroup>");
        sb.AppendLine("  <ItemGroup>");
        sb.AppendLine($"    <Compile Include=\"Main{options.SolutionType.SourceExtension}\" />");
        sb.AppendLine("  </ItemGroup>");
        sb.AppendLine("</BasicLangProject>");

        return sb.ToString();
    }

    private static string GetOutputType(ProjectTemplate template)
    {
        return template.Id switch
        {
            "console-app" => "exe",
            "game-app" => "winexe",
            "winforms-app" => "winexe",
            "wpf-app" => "winexe",
            "avalonia-app" => "winexe",
            _ => "exe"
        };
    }

    private async Task<List<string>> GenerateSourceFilesAsync(string projectDir, CreateProjectOptions options, CancellationToken cancellationToken)
    {
        var filesToOpen = new List<string>();
        var mainFile = Path.Combine(projectDir, $"Main{options.SolutionType.SourceExtension}");
        var content = GenerateMainFileContent(options);

        await File.WriteAllTextAsync(mainFile, content, cancellationToken);
        filesToOpen.Add(mainFile);

        // Generate additional files based on template
        switch (options.Template.Id)
        {
            case "game-app":
                await GenerateGameFilesAsync(projectDir, options, cancellationToken);
                break;
            case "winforms-app":
                await GenerateWinFormsFilesAsync(projectDir, options, cancellationToken);
                break;
            case "wpf-app":
                await GenerateWpfFilesAsync(projectDir, options, cancellationToken);
                break;
            case "avalonia-app":
                await GenerateAvaloniaFilesAsync(projectDir, options, cancellationToken);
                break;
        }

        return filesToOpen;
    }

    private string GenerateMainFileContent(CreateProjectOptions options)
    {
        var ns = options.Namespace ?? options.Name;

        return options.Template.Id switch
        {
            "console-app" => GenerateConsoleAppMain(ns),
            "game-app" => GenerateGameAppMain(ns),
            "winforms-app" => GenerateWinFormsMain(ns),
            "wpf-app" => GenerateWpfMain(ns),
            "avalonia-app" => GenerateAvaloniaMain(ns),
            _ => GenerateConsoleAppMain(ns)
        };
    }

    private static string GenerateConsoleAppMain(string ns)
    {
        return $@"' {ns} - Console Application
' Generated by Visual Game Studio

' Import .NET namespaces
Using System
Using System.IO

Sub Main()
    ' Write to file for debugging
    File.WriteAllText(""debug_output.txt"", ""Program started at "" & DateTime.Now.ToString())

    ' Using .NET Console
    Console.WriteLine(""Hello, World!"")
    Console.WriteLine(""Welcome to {ns}!"")

    ' Using .NET DateTime
    Console.WriteLine(""Current time: "" & DateTime.Now.ToString())

    ' Using .NET Environment
    Console.WriteLine(""Machine: "" & Environment.MachineName)

    ' Using System.IO
    Console.WriteLine(""Current directory: "" & Directory.GetCurrentDirectory())

    Console.WriteLine()
    Console.WriteLine(""Program completed successfully!"")

    ' Append to debug file
    File.AppendAllText(""debug_output.txt"", "" - Program finished"")
End Sub
";
    }

    private static string GenerateGameAppMain(string ns)
    {
        return $@"' {ns} - Game Application
' Generated by Visual Game Studio

Const SCREEN_WIDTH As Integer = 800
Const SCREEN_HEIGHT As Integer = 600

Dim gameRunning As Boolean = True

Sub Main()
    GameInit(SCREEN_WIDTH, SCREEN_HEIGHT, ""{ns}"")

    While gameRunning And Not GameShouldClose()
        Update()
        Draw()
    Wend

    GameShutdown()
End Sub

Sub Update()
    Dim dt As Single
    dt = GameGetDeltaTime()

    ' Handle escape key to exit (ESC = 256)
    If IsKeyPressed(256) Then
        gameRunning = False
    End If

    ' Add your game update logic here
End Sub

Sub Draw()
    GameBeginFrame()

    ' Clear the screen with dark blue
    ClearBackground(20, 40, 80)

    ' Draw welcome text
    DrawText(""Hello, {ns}!"", 300, 280, 32, 255, 255, 255, 255)
    DrawText(""Press ESC to exit"", 320, 320, 20, 200, 200, 200, 255)

    GameEndFrame()
End Sub
";
    }

    private static string GenerateWinFormsMain(string ns)
    {
        return $@"' {ns} - Windows Forms Application
' Generated by Visual Game Studio
' Note: WinForms requires .NET backend compilation

Sub Main()
    PrintLine(""Starting {ns} WinForms Application..."")
    PrintLine(""Note: Full WinForms support requires .NET backend."")
    PrintLine(""Press Enter to exit."")
    Input()
End Sub
";
    }

    private static string GenerateWpfMain(string ns)
    {
        return $@"' {ns} - WPF Application
' Generated by Visual Game Studio
' Note: WPF requires .NET backend compilation

Sub Main()
    PrintLine(""Starting {ns} WPF Application..."")
    PrintLine(""Note: Full WPF support requires .NET backend."")
    PrintLine(""Press Enter to exit."")
    Input()
End Sub
";
    }

    private static string GenerateAvaloniaMain(string ns)
    {
        return $@"' {ns} - Avalonia UI Application
' Generated by Visual Game Studio
' Note: Avalonia requires .NET backend compilation

Sub Main()
    PrintLine(""Starting {ns} Avalonia Application..."")
    PrintLine(""Note: Full Avalonia support requires .NET backend."")
    PrintLine(""Press Enter to exit."")
    Input()
End Sub
";
    }

    private async Task GenerateGameFilesAsync(string projectDir, CreateProjectOptions options, CancellationToken cancellationToken)
    {
        // Create assets directories
        Directory.CreateDirectory(Path.Combine(projectDir, "Assets", "Textures"));
        Directory.CreateDirectory(Path.Combine(projectDir, "Assets", "Sounds"));
        Directory.CreateDirectory(Path.Combine(projectDir, "Assets", "Fonts"));

        // Create a sample sprite class
        var spriteFile = Path.Combine(projectDir, $"Sprite{options.SolutionType.SourceExtension}");
        await File.WriteAllTextAsync(spriteFile, GenerateSpriteClass(options.Namespace ?? options.Name), cancellationToken);
    }

    private static string GenerateSpriteClass(string ns)
    {
        return $@"' Sprite helper functions for {ns}
' Generated by Visual Game Studio

' Sprite data arrays
Dim spriteTextures(100) As Integer
Dim spriteX(100) As Single
Dim spriteY(100) As Single
Dim spriteRotation(100) As Single
Dim spriteScale(100) As Single
Dim spriteCount As Integer = 0

Function CreateSprite(texturePath As String) As Integer
    Dim id As Integer
    id = spriteCount
    spriteTextures(id) = LoadTexture(texturePath)
    spriteX(id) = 0
    spriteY(id) = 0
    spriteRotation(id) = 0
    spriteScale(id) = 1
    spriteCount = spriteCount + 1
    Return id
End Function

Sub SetSpritePosition(id As Integer, x As Single, y As Single)
    spriteX(id) = x
    spriteY(id) = y
End Sub

Sub SetSpriteRotation(id As Integer, rotation As Single)
    spriteRotation(id) = rotation
End Sub

Sub SetSpriteScale(id As Integer, scale As Single)
    spriteScale(id) = scale
End Sub

Sub DrawSprite(id As Integer)
    DrawTextureEx(spriteTextures(id), spriteX(id), spriteY(id), spriteRotation(id), spriteScale(id), 255, 255, 255, 255)
End Sub

Sub DrawSpriteColored(id As Integer, r As Integer, g As Integer, b As Integer, a As Integer)
    DrawTextureEx(spriteTextures(id), spriteX(id), spriteY(id), spriteRotation(id), spriteScale(id), r, g, b, a)
End Sub
";
    }

    private async Task GenerateWinFormsFilesAsync(string projectDir, CreateProjectOptions options, CancellationToken cancellationToken)
    {
        var ns = options.Namespace ?? options.Name;

        // Create UI helper file
        var formFile = Path.Combine(projectDir, $"UIHelpers{options.SolutionType.SourceExtension}");
        var formContent = $@"' UI Helper functions for {ns}
' Generated by Visual Game Studio

' Application settings
Dim appTitle As String = ""{options.Name}""
Dim appWidth As Integer = 800
Dim appHeight As Integer = 600

Sub SetApplicationTitle(title As String)
    appTitle = title
End Sub

Sub SetApplicationSize(width As Integer, height As Integer)
    appWidth = width
    appHeight = height
End Sub

Sub ShowMessage(message As String)
    PrintLine(message)
End Sub

Sub ShowError(message As String)
    PrintLine(""Error: "" + message)
End Sub
";
        await File.WriteAllTextAsync(formFile, formContent, cancellationToken);
    }

    private async Task GenerateWpfFilesAsync(string projectDir, CreateProjectOptions options, CancellationToken cancellationToken)
    {
        var ns = options.Namespace ?? options.Name;

        // Create UI helper file
        var windowFile = Path.Combine(projectDir, $"UIHelpers{options.SolutionType.SourceExtension}");
        var windowContent = $@"' UI Helper functions for {ns}
' Generated by Visual Game Studio

' Application settings
Dim appTitle As String = ""{options.Name}""
Dim appWidth As Integer = 800
Dim appHeight As Integer = 600

Sub SetApplicationTitle(title As String)
    appTitle = title
End Sub

Sub SetApplicationSize(width As Integer, height As Integer)
    appWidth = width
    appHeight = height
End Sub

Sub ShowMessage(message As String)
    PrintLine(message)
End Sub

Sub ShowError(message As String)
    PrintLine(""Error: "" + message)
End Sub
";
        await File.WriteAllTextAsync(windowFile, windowContent, cancellationToken);
    }

    private async Task GenerateAvaloniaFilesAsync(string projectDir, CreateProjectOptions options, CancellationToken cancellationToken)
    {
        var ns = options.Namespace ?? options.Name;

        // Create UI helper file
        var windowFile = Path.Combine(projectDir, $"UIHelpers{options.SolutionType.SourceExtension}");
        var windowContent = $@"' UI Helper functions for {ns}
' Generated by Visual Game Studio

' Application settings
Dim appTitle As String = ""{options.Name}""
Dim appWidth As Integer = 800
Dim appHeight As Integer = 600

Sub SetApplicationTitle(title As String)
    appTitle = title
End Sub

Sub SetApplicationSize(width As Integer, height As Integer)
    appWidth = width
    appHeight = height
End Sub

Sub ShowMessage(message As String)
    PrintLine(message)
End Sub

Sub ShowError(message As String)
    PrintLine(""Error: "" + message)
End Sub
";
        await File.WriteAllTextAsync(windowFile, windowContent, cancellationToken);
    }

    private async Task<string> CreateSolutionFileAsync(string solutionDir, CreateProjectOptions options, string projectFile, CancellationToken cancellationToken)
    {
        var solutionFile = Path.Combine(solutionDir, $"{options.Name}{options.SolutionType.SolutionExtension}");
        var projectRelativePath = Path.GetRelativePath(solutionDir, projectFile);
        var content = GenerateSolutionContent(options.Name, projectRelativePath);

        await File.WriteAllTextAsync(solutionFile, content, cancellationToken);
        return solutionFile;
    }

    private static string GenerateSolutionContent(string name, string projectRelativePath)
    {
        // Solution files use JSON format
        var projectId = Guid.NewGuid();
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine($"  \"name\": \"{name}\",");
        sb.AppendLine($"  \"version\": \"1.0\",");
        sb.AppendLine($"  \"projects\": [");
        sb.AppendLine($"    {{");
        sb.AppendLine($"      \"id\": \"{projectId}\",");
        sb.AppendLine($"      \"name\": \"{name}\",");
        sb.AppendLine($"      \"relativePath\": \"{projectRelativePath.Replace("\\", "\\\\")}\",");
        sb.AppendLine($"      \"isStartupProject\": true");
        sb.AppendLine($"    }}");
        sb.AppendLine($"  ],");
        sb.AppendLine($"  \"folders\": [],");
        sb.AppendLine($"  \"globalProperties\": {{}}");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string GenerateEmptySolutionContent(string name)
    {
        // Solution files use JSON format - empty solution
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine($"  \"name\": \"{name}\",");
        sb.AppendLine($"  \"version\": \"1.0\",");
        sb.AppendLine($"  \"projects\": [],");
        sb.AppendLine($"  \"folders\": [],");
        sb.AppendLine($"  \"globalProperties\": {{}}");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private async Task AddProjectToSolutionAsync(string solutionPath, string projectPath, CancellationToken cancellationToken)
    {
        var content = await File.ReadAllTextAsync(solutionPath, cancellationToken);
        var projectName = Path.GetFileNameWithoutExtension(projectPath);
        var projectRelativePath = Path.GetRelativePath(Path.GetDirectoryName(solutionPath)!, projectPath);

        // Parse existing JSON and add project
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(content);
            var root = doc.RootElement;

            var newProject = new
            {
                id = Guid.NewGuid(),
                name = projectName,
                relativePath = projectRelativePath,
                isStartupProject = false
            };

            // Simple approach: deserialize, modify, serialize
            var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };
            var solutionData = System.Text.Json.JsonSerializer.Deserialize<SolutionJsonData>(content, options) ?? new SolutionJsonData();
            solutionData.Projects ??= new List<ProjectJsonData>();
            solutionData.Projects.Add(new ProjectJsonData
            {
                Id = Guid.NewGuid(),
                Name = projectName,
                RelativePath = projectRelativePath,
                IsStartupProject = false
            });

            var newContent = System.Text.Json.JsonSerializer.Serialize(solutionData, options);
            await File.WriteAllTextAsync(solutionPath, newContent, cancellationToken);
        }
        catch
        {
            // If parsing fails, create new solution content
            var newContent = GenerateSolutionContent(projectName, projectRelativePath);
            await File.WriteAllTextAsync(solutionPath, newContent, cancellationToken);
        }
    }

    private class SolutionJsonData
    {
        public string? Name { get; set; }
        public string? Version { get; set; }
        public List<ProjectJsonData>? Projects { get; set; }
        public List<object>? Folders { get; set; }
        public Dictionary<string, string>? GlobalProperties { get; set; }
    }

    private class ProjectJsonData
    {
        public Guid Id { get; set; }
        public string? Name { get; set; }
        public string? RelativePath { get; set; }
        public bool IsStartupProject { get; set; }
    }

    private async Task CreateGitIgnoreAsync(string directory, SolutionType solutionType, CancellationToken cancellationToken)
    {
        var gitIgnorePath = Path.Combine(directory, ".gitignore");
        var content = GenerateGitIgnoreContent(solutionType);
        await File.WriteAllTextAsync(gitIgnorePath, content, cancellationToken);
    }

    private static string GenerateGitIgnoreContent(SolutionType solutionType)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Build outputs");
        sb.AppendLine("bin/");
        sb.AppendLine("obj/");
        sb.AppendLine("BuildOutput/");
        sb.AppendLine("out/");
        sb.AppendLine();
        sb.AppendLine("# IDE");
        sb.AppendLine(".vs/");
        sb.AppendLine(".vscode/");
        sb.AppendLine(".idea/");
        sb.AppendLine("*.user");
        sb.AppendLine("*.suo");
        sb.AppendLine();
        sb.AppendLine("# OS");
        sb.AppendLine(".DS_Store");
        sb.AppendLine("Thumbs.db");
        sb.AppendLine();

        if (solutionType.Id == "dotnet" || solutionType.Id == "msil")
        {
            sb.AppendLine("# .NET");
            sb.AppendLine("*.dll");
            sb.AppendLine("*.exe");
            sb.AppendLine("*.pdb");
            sb.AppendLine("packages/");
            sb.AppendLine();
        }

        if (solutionType.Id == "native")
        {
            sb.AppendLine("# C++");
            sb.AppendLine("*.o");
            sb.AppendLine("*.obj");
            sb.AppendLine("*.lib");
            sb.AppendLine("*.a");
            sb.AppendLine();
        }

        if (solutionType.Id == "llvm")
        {
            sb.AppendLine("# LLVM");
            sb.AppendLine("*.ll");
            sb.AppendLine("*.bc");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    #endregion
}
