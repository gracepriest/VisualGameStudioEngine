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
            await File.WriteAllTextAsync(solutionFile, GenerateSolutionContent(options.Name, options.SolutionType), cancellationToken);
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

namespace {ns}

module Program
    sub Main(args as string())
        Console.WriteLine(""Hello, World!"")
        Console.WriteLine(""Welcome to BasicLang!"")
    end sub
end module

end namespace
";
    }

    private static string GenerateGameAppMain(string ns)
    {
        return $@"' {ns} - Game Application
' Generated by Visual Game Studio

imports BasicLang.GameFramework
imports BasicLang.GameFramework.Graphics
imports BasicLang.GameFramework.Input

namespace {ns}

class GameMain extends Game
    private title as string = ""{ns}""
    private width as integer = 800
    private height as integer = 600

    public sub New()
        base.New(title, width, height)
    end sub

    protected override sub Initialize()
        ' Initialize game resources here
        base.Initialize()
    end sub

    protected override sub LoadContent()
        ' Load textures, sounds, and other assets here
    end sub

    protected override sub Update(deltaTime as single)
        ' Update game logic here
        if Input.IsKeyPressed(Keys.Escape) then
            Exit()
        end if
    end sub

    protected override sub Draw()
        ' Draw game graphics here
        Graphics.Clear(Color.CornflowerBlue)
    end sub
end class

module Program
    sub Main(args as string())
        dim game as new GameMain()
        game.Run()
    end sub
end module

end namespace
";
    }

    private static string GenerateWinFormsMain(string ns)
    {
        return $@"' {ns} - Windows Forms Application
' Generated by Visual Game Studio

imports System.Windows.Forms

namespace {ns}

module Program
    sub Main(args as string())
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(false)
        Application.Run(new MainForm())
    end sub
end module

end namespace
";
    }

    private static string GenerateWpfMain(string ns)
    {
        return $@"' {ns} - WPF Application
' Generated by Visual Game Studio

imports System.Windows

namespace {ns}

module Program
    sub Main(args as string())
        dim app as new Application()
        dim mainWindow as new MainWindow()
        app.Run(mainWindow)
    end sub
end module

end namespace
";
    }

    private static string GenerateAvaloniaMain(string ns)
    {
        return $@"' {ns} - Avalonia UI Application
' Generated by Visual Game Studio

imports Avalonia
imports Avalonia.Controls.ApplicationLifetimes

namespace {ns}

module Program
    sub Main(args as string())
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args)
    end sub

    function BuildAvaloniaApp() as AppBuilder
        return AppBuilder.Configure(of App)() _
            .UsePlatformDetect() _
            .LogToTrace()
    end function
end module

end namespace
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
        return $@"' Sprite class
' Generated by Visual Game Studio

imports BasicLang.GameFramework
imports BasicLang.GameFramework.Graphics

namespace {ns}

class Sprite
    public property Texture as Texture2D
    public property Position as Vector2
    public property Rotation as single
    public property Scale as Vector2 = Vector2.One
    public property Color as Color = Color.White

    public sub New(texture as Texture2D)
        me.Texture = texture
        me.Position = Vector2.Zero
        me.Rotation = 0
    end sub

    public sub Draw(graphics as Graphics2D)
        graphics.Draw(Texture, Position, Rotation, Scale, Color)
    end sub
end class

end namespace
";
    }

    private async Task GenerateWinFormsFilesAsync(string projectDir, CreateProjectOptions options, CancellationToken cancellationToken)
    {
        var ns = options.Namespace ?? options.Name;

        // Create MainForm
        var formFile = Path.Combine(projectDir, $"MainForm{options.SolutionType.SourceExtension}");
        var formContent = $@"' MainForm
' Generated by Visual Game Studio

imports System.Windows.Forms

namespace {ns}

class MainForm extends Form
    public sub New()
        InitializeComponent()
    end sub

    private sub InitializeComponent()
        me.Text = ""{options.Name}""
        me.Size = new System.Drawing.Size(800, 600)
        me.StartPosition = FormStartPosition.CenterScreen
    end sub
end class

end namespace
";
        await File.WriteAllTextAsync(formFile, formContent, cancellationToken);
    }

    private async Task GenerateWpfFilesAsync(string projectDir, CreateProjectOptions options, CancellationToken cancellationToken)
    {
        var ns = options.Namespace ?? options.Name;

        // Create MainWindow
        var windowFile = Path.Combine(projectDir, $"MainWindow{options.SolutionType.SourceExtension}");
        var windowContent = $@"' MainWindow
' Generated by Visual Game Studio

imports System.Windows

namespace {ns}

class MainWindow extends Window
    public sub New()
        me.Title = ""{options.Name}""
        me.Width = 800
        me.Height = 600
        me.WindowStartupLocation = WindowStartupLocation.CenterScreen
    end sub
end class

end namespace
";
        await File.WriteAllTextAsync(windowFile, windowContent, cancellationToken);
    }

    private async Task GenerateAvaloniaFilesAsync(string projectDir, CreateProjectOptions options, CancellationToken cancellationToken)
    {
        var ns = options.Namespace ?? options.Name;

        // Create App class
        var appFile = Path.Combine(projectDir, $"App{options.SolutionType.SourceExtension}");
        var appContent = $@"' App
' Generated by Visual Game Studio

imports Avalonia
imports Avalonia.Controls.ApplicationLifetimes
imports Avalonia.Markup.Xaml

namespace {ns}

class App extends Application
    public override sub Initialize()
        AvaloniaXamlLoader.Load(me)
    end sub

    public override sub OnFrameworkInitializationCompleted()
        if TypeOf ApplicationLifetime is IClassicDesktopStyleApplicationLifetime then
            dim desktop = DirectCast(ApplicationLifetime, IClassicDesktopStyleApplicationLifetime)
            desktop.MainWindow = new MainWindow()
        end if
        base.OnFrameworkInitializationCompleted()
    end sub
end class

end namespace
";
        await File.WriteAllTextAsync(appFile, appContent, cancellationToken);

        // Create MainWindow
        var windowFile = Path.Combine(projectDir, $"MainWindow{options.SolutionType.SourceExtension}");
        var windowContent = $@"' MainWindow
' Generated by Visual Game Studio

imports Avalonia.Controls

namespace {ns}

class MainWindow extends Window
    public sub New()
        me.Title = ""{options.Name}""
        me.Width = 800
        me.Height = 600
    end sub
end class

end namespace
";
        await File.WriteAllTextAsync(windowFile, windowContent, cancellationToken);
    }

    private async Task<string> CreateSolutionFileAsync(string solutionDir, CreateProjectOptions options, string projectFile, CancellationToken cancellationToken)
    {
        var solutionFile = Path.Combine(solutionDir, $"{options.Name}{options.SolutionType.SolutionExtension}");
        var content = GenerateSolutionContent(options.Name, options.SolutionType);

        // Add project reference
        var projectRelativePath = Path.GetRelativePath(solutionDir, projectFile);
        content += $"\n[projects]\n\"{options.Name}\" = \"{projectRelativePath}\"\n";

        await File.WriteAllTextAsync(solutionFile, content, cancellationToken);
        return solutionFile;
    }

    private static string GenerateSolutionContent(string name, SolutionType solutionType)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# BasicLang Solution File");
        sb.AppendLine($"# Generated by Visual Game Studio");
        sb.AppendLine();
        sb.AppendLine($"[solution]");
        sb.AppendLine($"name = \"{name}\"");
        sb.AppendLine($"target = \"{solutionType.Id}\"");
        sb.AppendLine($"version = \"1.0\"");
        sb.AppendLine();
        return sb.ToString();
    }

    private async Task AddProjectToSolutionAsync(string solutionPath, string projectPath, CancellationToken cancellationToken)
    {
        var content = await File.ReadAllTextAsync(solutionPath, cancellationToken);
        var projectName = Path.GetFileNameWithoutExtension(projectPath);
        var projectRelativePath = Path.GetRelativePath(Path.GetDirectoryName(solutionPath)!, projectPath);

        if (!content.Contains("[projects]"))
        {
            content += "\n[projects]\n";
        }

        content += $"\"{projectName}\" = \"{projectRelativePath}\"\n";
        await File.WriteAllTextAsync(solutionPath, content, cancellationToken);
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
