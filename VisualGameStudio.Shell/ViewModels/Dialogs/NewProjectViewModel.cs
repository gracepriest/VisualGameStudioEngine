using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

public partial class NewProjectViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _projectName = "MyProject";

    [ObservableProperty]
    private string _location = "";

    [ObservableProperty]
    private SimpleProjectTemplate? _selectedTemplate;

    [ObservableProperty]
    private ObservableCollection<SimpleProjectTemplate> _templates = new();

    [ObservableProperty]
    private bool _createDirectory = true;

    [ObservableProperty]
    private string? _errorMessage;

    public bool DialogResult { get; private set; }
    public string? CreatedProjectPath { get; private set; }

    public Action? CloseDialog { get; set; }

    public NewProjectViewModel()
    {
        // Set default location
        Location = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "BasicLang Projects");

        // Add project templates
        Templates.Add(new SimpleProjectTemplate
        {
            Name = "Console Application",
            Description = "A command-line application that runs in the console.",
            Icon = "🖥️",
            TemplateType = SimpleProjectTemplateType.Console,
            DefaultCode = """
                ' Console Application
                Module Program
                    Sub Main()
                        PrintLine("Hello, World!")
                    End Sub
                End Module
                """
        });

        Templates.Add(new SimpleProjectTemplate
        {
            Name = "Class Library",
            Description = "A library of reusable classes and functions.",
            Icon = "📚",
            TemplateType = SimpleProjectTemplateType.Library,
            DefaultCode = """
                ' Class Library
                Namespace MyLibrary
                    Public Class MyClass
                        Public Sub DoSomething()
                            ' TODO: Implement
                        End Sub
                    End Class
                End Namespace
                """
        });

        Templates.Add(new SimpleProjectTemplate
        {
            Name = "Game Project",
            Description = "A game project with a main game loop.",
            Icon = "🎮",
            TemplateType = SimpleProjectTemplateType.Game,
            DefaultCode = """
                ' Game Project
                Module Game
                    Dim running As Boolean

                    Sub Main()
                        running = True
                        Initialize()

                        While running
                            Update()
                            Render()
                        Wend

                        Cleanup()
                    End Sub

                    Sub Initialize()
                        PrintLine("Game initialized")
                    End Sub

                    Sub Update()
                        ' Update game logic
                    End Sub

                    Sub Render()
                        ' Render game graphics
                    End Sub

                    Sub Cleanup()
                        PrintLine("Game ended")
                    End Sub
                End Module
                """
        });

        Templates.Add(new SimpleProjectTemplate
        {
            Name = "Empty Project",
            Description = "An empty project with no default files.",
            Icon = "📄",
            TemplateType = SimpleProjectTemplateType.Empty,
            DefaultCode = ""
        });

        SelectedTemplate = Templates[0];
    }

    [RelayCommand]
    private void BrowseLocation()
    {
        // This would be handled by the view to show a folder picker
        // For now, we'll just use the default location
    }

    [RelayCommand]
    private async Task CreateProjectAsync()
    {
        ErrorMessage = null;

        // Validate inputs
        if (string.IsNullOrWhiteSpace(ProjectName))
        {
            ErrorMessage = "Project name is required.";
            return;
        }

        if (string.IsNullOrWhiteSpace(Location))
        {
            ErrorMessage = "Location is required.";
            return;
        }

        if (SelectedTemplate == null)
        {
            ErrorMessage = "Please select a project template.";
            return;
        }

        // Validate project name
        var invalidChars = Path.GetInvalidFileNameChars();
        if (ProjectName.IndexOfAny(invalidChars) >= 0)
        {
            ErrorMessage = "Project name contains invalid characters.";
            return;
        }

        try
        {
            // Create project directory
            var projectDir = CreateDirectory
                ? Path.Combine(Location, ProjectName)
                : Location;

            if (Directory.Exists(projectDir) && Directory.GetFiles(projectDir).Length > 0)
            {
                ErrorMessage = "Directory already exists and is not empty.";
                return;
            }

            Directory.CreateDirectory(projectDir);

            // Create project file
            var projectPath = Path.Combine(projectDir, $"{ProjectName}.blproj");
            var outputType = SelectedTemplate.TemplateType == SimpleProjectTemplateType.Library ? "Library" : "Exe";

            var projectContent = $"""
                <?xml version="1.0" encoding="utf-8"?>
                <BasicLangProject Version="1.0">
                  <PropertyGroup>
                    <ProjectName>{ProjectName}</ProjectName>
                    <OutputType>{outputType}</OutputType>
                    <RootNamespace>{ProjectName}</RootNamespace>
                    <TargetBackend>Interpreter</TargetBackend>
                  </PropertyGroup>
                  <ItemGroup>
                    {(SelectedTemplate.TemplateType != SimpleProjectTemplateType.Empty ? "<Compile Include=\"Program.bas\" />" : "")}
                  </ItemGroup>
                  <PropertyGroup Condition="'$(Configuration)' == 'Debug'">
                    <OutputPath>bin\Debug\</OutputPath>
                    <DebugSymbols>true</DebugSymbols>
                  </PropertyGroup>
                  <PropertyGroup Condition="'$(Configuration)' == 'Release'">
                    <OutputPath>bin\Release\</OutputPath>
                    <Optimize>true</Optimize>
                  </PropertyGroup>
                </BasicLangProject>
                """;

            await File.WriteAllTextAsync(projectPath, projectContent);

            // Create main source file
            if (SelectedTemplate.TemplateType != SimpleProjectTemplateType.Empty && !string.IsNullOrEmpty(SelectedTemplate.DefaultCode))
            {
                var sourcePath = Path.Combine(projectDir, "Program.bas");
                await File.WriteAllTextAsync(sourcePath, SelectedTemplate.DefaultCode);
            }

            CreatedProjectPath = projectPath;
            DialogResult = true;
            CloseDialog?.Invoke();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to create project: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
        CloseDialog?.Invoke();
    }
}

public class SimpleProjectTemplate
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Icon { get; set; } = "";
    public SimpleProjectTemplateType TemplateType { get; set; }
    public string DefaultCode { get; set; } = "";
}

public enum SimpleProjectTemplateType
{
    Console,
    Library,
    Game,
    Empty
}
