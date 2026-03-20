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
        sb.AppendLine($"    <OutputType>{outputType switch { "exe" => "Exe", "library" => "Library", _ => "WinExe" }}</OutputType>");
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

        // Add compile items based on template type
        var compileItems = GetCompileItems(options.Template.Id);
        foreach (var item in compileItems)
        {
            sb.AppendLine($"    <Compile Include=\"{item}\" />");
        }

        sb.AppendLine("  </ItemGroup>");
        sb.AppendLine("</BasicLangProject>");

        return sb.ToString();
    }

    private static string GetOutputType(ProjectTemplate template)
    {
        return template.Id switch
        {
            "console-app" => "exe",
            "game-app" => "exe",
            "winforms-app" => "winexe",
            "wpf-app" => "winexe",
            "avalonia-app" => "winexe",
            "class-library" => "library",
            "web-api" => "exe",
            "unit-test" => "exe",
            _ => "exe"
        };
    }

    private static List<string> GetCompileItems(string templateId)
    {
        return templateId switch
        {
            "game-app" => new List<string> { "Main.bas", "GameState.mod", "Player.cls" },
            "console-app" => new List<string> { "Main.bas", "Helpers.mod" },
            "class-library" => new List<string> { "Library.mod", "Types.cls" },
            _ => new List<string> { "Main.bas" }
        };
    }

    private async Task<List<string>> GenerateSourceFilesAsync(string projectDir, CreateProjectOptions options, CancellationToken cancellationToken)
    {
        var filesToOpen = new List<string>();

        // Templates that use Main.bas as entry point
        if (options.Template.Id != "class-library")
        {
            var mainFile = Path.Combine(projectDir, $"Main{options.SolutionType.SourceExtension}");
            var content = GenerateMainFileContent(options);
            await File.WriteAllTextAsync(mainFile, content, cancellationToken);
            filesToOpen.Add(mainFile);
        }

        // Generate additional files based on template
        switch (options.Template.Id)
        {
            case "game-app":
                await GenerateGameFilesAsync(projectDir, options, cancellationToken);
                break;
            case "console-app":
                await GenerateConsoleFilesAsync(projectDir, options, cancellationToken);
                break;
            case "class-library":
                var libraryFiles = await GenerateClassLibraryFilesAsync(projectDir, options, cancellationToken);
                filesToOpen.AddRange(libraryFiles);
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
            "class-library" => GenerateClassLibraryMain(ns),
            "web-api" => GenerateWebApiMain(ns),
            "unit-test" => GenerateUnitTestMain(ns),
            _ => GenerateConsoleAppMain(ns)
        };
    }

    private static string GenerateConsoleAppMain(string ns)
    {
        return $@"' {ns} - Console Application
' Generated by Visual Game Studio

Using System

Module Main
    Sub Main()
        PrintHeader(""{ns}"")
        Console.WriteLine(FormatMessage(""Hello, World!""))
        Console.WriteLine(FormatMessage(""Welcome to {ns}!""))
        Console.WriteLine()
        Console.WriteLine(""Current time: "" & DateTime.Now.ToString())
        Console.WriteLine(""Program completed successfully!"")
    End Sub
End Module
";
    }

    private static string GenerateGameAppMain(string ns)
    {
        return $@"Module Main
    Sub Main()
        GameInit(800, 600, ""My Game"")

        Dim player As New Player(""Hero"")

        While Not GameShouldClose()
            GameBeginFrame()
            ClearBackground(40, 40, 60, 255)
            DrawText(player.Name, 10, 10, 20, 255, 255, 255, 255)
            GameEndFrame()
        End While

        GameShutdown()
    End Sub
End Module
";
    }

    private static string GenerateWinFormsMain(string ns)
    {
        return $@"' {ns} - Windows Forms Application
' Generated by Visual Game Studio
' Requires .NET backend compilation

Using System
Using System.Drawing
Using System.Windows.Forms

Module Program
    Sub Main()
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)
        Application.Run(New MainForm())
    End Sub
End Module

Public Class MainForm
    Inherits Form

    Private lblMessage As Label
    Private btnClick As Button

    Public Sub New()
        Me.Text = ""{ns}""
        Me.Size = New Size(400, 300)
        Me.StartPosition = FormStartPosition.CenterScreen

        ' Create label
        lblMessage = New Label()
        lblMessage.Text = ""Hello, {ns}!""
        lblMessage.Location = New Point(20, 20)
        lblMessage.Size = New Size(300, 30)
        lblMessage.Font = New Font(""Segoe UI"", 12)
        Me.Controls.Add(lblMessage)

        ' Create button
        btnClick = New Button()
        btnClick.Text = ""Click Me""
        btnClick.Location = New Point(20, 60)
        btnClick.Size = New Size(100, 30)
        AddHandler btnClick.Click, AddressOf OnButtonClick
        Me.Controls.Add(btnClick)
    End Sub

    Private Sub OnButtonClick(sender As Object, e As EventArgs)
        MessageBox.Show(""Button clicked!"", ""{ns}"", MessageBoxButtons.OK, MessageBoxIcon.Information)
    End Sub
End Class
";
    }

    private static string GenerateWpfMain(string ns)
    {
        return $@"' {ns} - WPF Application
' Generated by Visual Game Studio
' Requires .NET backend compilation

Using System
Using System.Windows
Using System.Windows.Controls

Module Program
    <STAThread>
    Sub Main()
        Dim app As New Application()
        Dim window As New MainWindow()
        app.Run(window)
    End Sub
End Module

Public Class MainWindow
    Inherits Window

    Private btnClick As Button

    Public Sub New()
        Me.Title = ""{ns}""
        Me.Width = 400
        Me.Height = 300
        Me.WindowStartupLocation = WindowStartupLocation.CenterScreen

        ' Create layout
        Dim panel As New StackPanel()
        panel.VerticalAlignment = VerticalAlignment.Center
        panel.HorizontalAlignment = HorizontalAlignment.Center

        ' Create label
        Dim label As New TextBlock()
        label.Text = ""Hello, {ns}!""
        label.FontSize = 24
        label.Margin = New Thickness(0, 0, 0, 20)
        panel.Children.Add(label)

        ' Create button
        btnClick = New Button()
        btnClick.Content = ""Click Me""
        btnClick.Width = 100
        btnClick.Height = 30
        AddHandler btnClick.Click, AddressOf OnButtonClick
        panel.Children.Add(btnClick)

        Me.Content = panel
    End Sub

    Private Sub OnButtonClick(sender As Object, e As RoutedEventArgs)
        MessageBox.Show(""Button clicked!"", ""{ns}"", MessageBoxButton.OK, MessageBoxImage.Information)
    End Sub
End Class
";
    }

    private static string GenerateAvaloniaMain(string ns)
    {
        return $@"' {ns} - Avalonia UI Application
' Generated by Visual Game Studio
' Requires .NET backend compilation with Avalonia NuGet packages

Using System
Using Avalonia
Using Avalonia.Controls
Using Avalonia.Layout
Using Avalonia.Media

Module Program
    Sub Main()
        AppBuilder.Configure(Of App)() _
            .UsePlatformDetect() _
            .StartWithClassicDesktopLifetime(Nothing)
    End Sub
End Module

Public Class App
    Inherits Application

    Public Overrides Sub OnFrameworkInitializationCompleted()
        Dim window As New MainWindow()
        window.Show()
        MyBase.OnFrameworkInitializationCompleted()
    End Sub
End Class

Public Class MainWindow
    Inherits Window

    Public Sub New()
        Me.Title = ""{ns}""
        Me.Width = 400
        Me.Height = 300
        Me.WindowStartupLocation = WindowStartupLocation.CenterScreen

        ' Create layout
        Dim panel As New StackPanel()
        panel.VerticalAlignment = VerticalAlignment.Center
        panel.HorizontalAlignment = HorizontalAlignment.Center

        ' Create label
        Dim label As New TextBlock()
        label.Text = ""Hello, {ns}!""
        label.FontSize = 24
        label.Margin = New Thickness(0, 0, 0, 20)
        panel.Children.Add(label)

        ' Create button
        Dim btn As New Button()
        btn.Content = ""Click Me""
        btn.Width = 100
        btn.Height = 30
        panel.Children.Add(btn)

        Me.Content = panel
    End Sub
End Class
";
    }

    private static string GenerateClassLibraryMain(string ns)
    {
        return $@"' {ns} - Class Library
' Generated by Visual Game Studio
' This is a reusable code library module

Module {ns}

' Example utility function
Public Function Add(a As Integer, b As Integer) As Integer
    Return a + b
End Function

' Example string utility
Public Function Capitalize(text As String) As String
    If text = """" Then Return """"
    Return text.Substring(0, 1).ToUpper() & text.Substring(1)
End Function

' Example validation function
Public Function IsValidEmail(email As String) As Boolean
    Return email.Contains(""@"") And email.Contains(""."")
End Function

End Module
";
    }

    private static string GenerateWebApiMain(string ns)
    {
        return $@"' {ns} - Web API
' Generated by Visual Game Studio
' A REST API service using .NET

Using System
Using System.Net
Using System.IO

' API Configuration
Const API_PORT As Integer = 5000
Const API_HOST As String = ""localhost""

Sub Main()
    Console.WriteLine(""Starting {ns} Web API..."")
    Console.WriteLine(""Listening on http://"" & API_HOST & "":"" & API_PORT.ToString())
    Console.WriteLine(""Press Ctrl+C to stop the server."")

    ' Note: Full Web API support requires ASP.NET Core integration
    ' This is a basic template demonstrating the structure

    Console.WriteLine()
    Console.WriteLine(""Available endpoints:"")
    Console.WriteLine(""  GET  /api/health  - Health check"")
    Console.WriteLine(""  GET  /api/items   - List items"")
    Console.WriteLine(""  POST /api/items   - Create item"")

    ' Keep running
    Console.WriteLine()
    Console.WriteLine(""Press Enter to exit."")
    Console.ReadLine()
End Sub

' Example API handler
Function HandleHealthCheck() As String
    Return ""{{""""status"""": """"healthy"""", """"timestamp"""": """""" & DateTime.Now.ToString() & """"""""}}""
End Function
";
    }

    private static string GenerateUnitTestMain(string ns)
    {
        return $@"' {ns} - Unit Tests
' Generated by Visual Game Studio
' Unit testing project

Using System

' Test class
Module {ns}Tests

' Test counter
Dim passedTests As Integer = 0
Dim failedTests As Integer = 0

Sub Main()
    Console.WriteLine(""Running {ns} Tests..."")
    Console.WriteLine(""========================="")
    Console.WriteLine()

    ' Run all tests
    TestAddition()
    TestSubtraction()
    TestStringOperations()

    ' Print summary
    Console.WriteLine()
    Console.WriteLine(""========================="")
    Console.WriteLine(""Tests Passed: "" & passedTests.ToString())
    Console.WriteLine(""Tests Failed: "" & failedTests.ToString())

    If failedTests = 0 Then
        Console.WriteLine(""All tests passed!"")
    End If
End Sub

Sub TestAddition()
    Dim result As Integer = 2 + 2
    AssertEqual(result, 4, ""TestAddition: 2 + 2 should equal 4"")
End Sub

Sub TestSubtraction()
    Dim result As Integer = 10 - 3
    AssertEqual(result, 7, ""TestSubtraction: 10 - 3 should equal 7"")
End Sub

Sub TestStringOperations()
    Dim text As String = ""Hello""
    AssertEqual(text.Length, 5, ""TestStringOperations: 'Hello'.Length should be 5"")
End Sub

Sub AssertEqual(actual As Integer, expected As Integer, message As String)
    If actual = expected Then
        Console.WriteLine(""[PASS] "" & message)
        passedTests = passedTests + 1
    Else
        Console.WriteLine(""[FAIL] "" & message & "" (got "" & actual.ToString() & "")"")
        failedTests = failedTests + 1
    End If
End Sub

End Module
";
    }

    private async Task GenerateGameFilesAsync(string projectDir, CreateProjectOptions options, CancellationToken cancellationToken)
    {
        // Create assets directories
        Directory.CreateDirectory(Path.Combine(projectDir, "Assets", "Textures"));
        Directory.CreateDirectory(Path.Combine(projectDir, "Assets", "Sounds"));
        Directory.CreateDirectory(Path.Combine(projectDir, "Assets", "Fonts"));

        // Create GameState.mod — shared game state
        var gameStateFile = Path.Combine(projectDir, "GameState.mod");
        await File.WriteAllTextAsync(gameStateFile, GenerateGameStateModule(), cancellationToken);

        // Create Player.cls — player class
        var playerFile = Path.Combine(projectDir, "Player.cls");
        await File.WriteAllTextAsync(playerFile, GeneratePlayerClass(), cancellationToken);
    }

    private static string GenerateGameStateModule()
    {
        return @"' Global game state — accessible from all files

Public Score As Integer = 0
Public Level As Integer = 1
Public IsGameOver As Boolean = False

Public Sub ResetGame()
    Score = 0
    Level = 1
    IsGameOver = False
End Sub
";
    }

    private static string GeneratePlayerClass()
    {
        return @"Public

Public Name As String
Public X As Single = 400
Public Y As Single = 300
Public Speed As Single = 5.0

Public Sub New(name As String)
    Me.Name = name
End Sub

Public Sub Update()
    If IsKeyDown(87) Then Y = Y - Speed
    If IsKeyDown(83) Then Y = Y + Speed
    If IsKeyDown(65) Then X = X - Speed
    If IsKeyDown(68) Then X = X + Speed
End Sub
";
    }

    private async Task GenerateConsoleFilesAsync(string projectDir, CreateProjectOptions options, CancellationToken cancellationToken)
    {
        // Create Helpers.mod — utility functions
        var helpersFile = Path.Combine(projectDir, "Helpers.mod");
        await File.WriteAllTextAsync(helpersFile, GenerateHelpersModule(options.Namespace ?? options.Name), cancellationToken);
    }

    private static string GenerateHelpersModule(string ns)
    {
        return $@"' Utility functions for {ns}

Public Function FormatMessage(message As String) As String
    Return ""["" & DateTime.Now.ToString(""HH:mm:ss"") & ""] "" & message
End Function

Public Sub PrintHeader(title As String)
    Dim separator As String = New String(""=""c, title.Length + 4)
    Console.WriteLine(separator)
    Console.WriteLine(""  "" & title)
    Console.WriteLine(separator)
End Sub
";
    }

    private async Task<List<string>> GenerateClassLibraryFilesAsync(string projectDir, CreateProjectOptions options, CancellationToken cancellationToken)
    {
        var ns = options.Namespace ?? options.Name;
        var filesToOpen = new List<string>();

        // Create Library.mod — public API module
        var libraryFile = Path.Combine(projectDir, "Library.mod");
        await File.WriteAllTextAsync(libraryFile, GenerateLibraryModule(ns), cancellationToken);
        filesToOpen.Add(libraryFile);

        // Create Types.cls — public data types
        var typesFile = Path.Combine(projectDir, "Types.cls");
        await File.WriteAllTextAsync(typesFile, GenerateTypesClass(ns), cancellationToken);

        return filesToOpen;
    }

    private static string GenerateLibraryModule(string ns)
    {
        return $@"' {ns} — Public API module

Public Function Add(a As Integer, b As Integer) As Integer
    Return a + b
End Function

Public Function Capitalize(text As String) As String
    If text = """" Then Return """"
    Return text.Substring(0, 1).ToUpper() & text.Substring(1)
End Function

Public Function IsValidEmail(email As String) As Boolean
    Return email.Contains(""@"") And email.Contains(""."")
End Function
";
    }

    private static string GenerateTypesClass(string ns)
    {
        return $@"' {ns} — Public data types

Public Class Result
    Public Success As Boolean
    Public Message As String

    Public Sub New(success As Boolean, message As String)
        Me.Success = success
        Me.Message = message
    End Sub
End Class
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
