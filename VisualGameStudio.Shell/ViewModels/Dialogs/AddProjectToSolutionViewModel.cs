using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

public partial class AddProjectToSolutionViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _projectName = "";

    [ObservableProperty]
    private string _selectedTemplate = "Console";

    [ObservableProperty]
    private string _selectedBackend = "CSharp";

    [ObservableProperty]
    private string _projectLocation = "";

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private bool _createDirectory = true;

    public ObservableCollection<string> Templates { get; } = new()
    {
        "Console", "Library", "Game", "WinForms", "WPF", "Empty"
    };

    public ObservableCollection<string> Backends { get; } = new()
    {
        "CSharp", "LLVM", "MSIL", "Cpp"
    };

    public ObservableCollection<CheckableItem> ProjectReferences { get; } = new();

    public bool DialogResult { get; private set; }
    public Action? CloseDialog { get; set; }

    /// <summary>
    /// Initializes the dialog with the solution directory and existing project names.
    /// </summary>
    public void Initialize(string solutionDirectory, IEnumerable<string> existingProjectNames)
    {
        ProjectLocation = solutionDirectory;
        foreach (var name in existingProjectNames)
        {
            ProjectReferences.Add(new CheckableItem { Name = name, IsChecked = false });
        }
    }

    /// <summary>
    /// Gets the output type for the selected template.
    /// </summary>
    public string GetOutputType()
    {
        return SelectedTemplate switch
        {
            "Library" => "Library",
            "WinForms" or "WPF" => "WinExe",
            _ => "Exe"
        };
    }

    /// <summary>
    /// Gets the project references that were checked by the user.
    /// </summary>
    public List<string> GetSelectedReferences()
    {
        return ProjectReferences.Where(r => r.IsChecked).Select(r => r.Name).ToList();
    }

    /// <summary>
    /// Gets the default source code for the selected template.
    /// </summary>
    public string GetDefaultCode()
    {
        return SelectedTemplate switch
        {
            "Console" => """
                ' Console Application
                Module Program
                    Sub Main()
                        PrintLine("Hello, World!")
                    End Sub
                End Module
                """,
            "Library" => """
                ' Class Library
                Namespace {NAME}
                    Public Class MyClass
                        Public Sub DoSomething()
                            ' TODO: Implement
                        End Sub
                    End Class
                End Namespace
                """.Replace("{NAME}", ProjectName),
            "Game" => """
                ' Game Project
                Module Game
                    Dim running As Boolean

                    Sub Main()
                        GameInit(800, 600, "My Game")
                        SetTargetFPS(60)
                        running = True

                        While running And Not GameShouldClose()
                            Update()
                            Render()
                        Wend

                        GameShutdown()
                    End Sub

                    Sub Update()
                        If IsKeyPressed(256) Then
                            running = False
                        End If
                    End Sub

                    Sub Render()
                        GameBeginFrame()
                        ClearBackground(20, 40, 80)
                        DrawText("Hello, Game!", 300, 280, 32, 255, 255, 255, 255)
                        GameEndFrame()
                    End Sub
                End Module
                """,
            "WinForms" => """
                ' WinForms Application
                Using System.Windows.Forms

                Module Program
                    Sub Main()
                        Dim form As New Form()
                        form.Text = "My WinForms App"
                        form.Width = 800
                        form.Height = 600
                        Application.Run(form)
                    End Sub
                End Module
                """,
            "WPF" => """
                ' WPF Application
                Using System.Windows

                Module Program
                    Sub Main()
                        Dim app As New Application()
                        Dim win As New Window()
                        win.Title = "My WPF App"
                        win.Width = 800
                        win.Height = 600
                        app.Run(win)
                    End Sub
                End Module
                """,
            _ => "" // Empty
        };
    }

    [RelayCommand]
    private void Create()
    {
        ErrorMessage = "";

        if (string.IsNullOrWhiteSpace(ProjectName))
        {
            ErrorMessage = "Project name is required.";
            return;
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        if (ProjectName.IndexOfAny(invalidChars) >= 0)
        {
            ErrorMessage = "Project name contains invalid characters.";
            return;
        }

        if (string.IsNullOrWhiteSpace(ProjectLocation))
        {
            ErrorMessage = "Project location is required.";
            return;
        }

        var projectDir = CreateDirectory
            ? Path.Combine(ProjectLocation, ProjectName)
            : ProjectLocation;

        if (Directory.Exists(projectDir) && Directory.GetFiles(projectDir).Length > 0)
        {
            ErrorMessage = "Directory already exists and is not empty.";
            return;
        }

        DialogResult = true;
        CloseDialog?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
        CloseDialog?.Invoke();
    }

    [RelayCommand]
    private void BrowseLocation()
    {
        // Handled by the view code-behind
    }
}

public class CheckableItem : ObservableObject
{
    public string Name { get; set; } = "";

    private bool _isChecked;
    public bool IsChecked
    {
        get => _isChecked;
        set => SetProperty(ref _isChecked, value);
    }
}
