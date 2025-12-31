using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

public partial class PeekDefinitionViewModel : ViewModelBase
{
    private readonly IRefactoringService _refactoringService;
    private readonly IFileService _fileService;

    [ObservableProperty]
    private string _symbolName = "";

    [ObservableProperty]
    private string _definitionFilePath = "";

    [ObservableProperty]
    private string _definitionFileName = "";

    [ObservableProperty]
    private int _definitionLine;

    [ObservableProperty]
    private int _definitionColumn;

    [ObservableProperty]
    private string _definitionCode = "";

    [ObservableProperty]
    private ObservableCollection<DefinitionLocation> _definitions = new();

    [ObservableProperty]
    private DefinitionLocation? _selectedDefinition;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasDefinition;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private int _contextLinesBefore = 3;

    [ObservableProperty]
    private int _contextLinesAfter = 10;

    public event EventHandler<NavigateToLocationEventArgs>? NavigateToLocation;
    public event EventHandler? Closed;

    public PeekDefinitionViewModel(IRefactoringService refactoringService, IFileService fileService)
    {
        _refactoringService = refactoringService;
        _fileService = fileService;
    }

    public async Task LoadDefinitionAsync(string filePath, int line, int column, string symbolName)
    {
        SymbolName = symbolName;
        IsLoading = true;
        ErrorMessage = null;
        HasDefinition = false;
        Definitions.Clear();
        DefinitionCode = "";

        try
        {
            // Find all references and filter for definitions
            var references = await _refactoringService.FindAllReferencesAsync(filePath, line, column);

            var definitionRefs = references.Where(r => r.Type == SymbolLocationType.Definition).ToList();

            if (definitionRefs.Count == 0)
            {
                // If no definitions found, check if current location is the definition
                var currentRef = references.FirstOrDefault(r =>
                    r.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase) &&
                    r.Line == line);

                if (currentRef != null)
                {
                    ErrorMessage = $"'{symbolName}' is defined at the current location.";
                }
                else
                {
                    ErrorMessage = $"No definition found for '{symbolName}'.";
                }
                return;
            }

            foreach (var def in definitionRefs)
            {
                Definitions.Add(new DefinitionLocation
                {
                    FilePath = def.FilePath,
                    FileName = Path.GetFileName(def.FilePath),
                    Line = def.Line,
                    Column = def.Column,
                    Preview = def.Text
                });
            }

            if (Definitions.Count > 0)
            {
                SelectedDefinition = Definitions[0];
                HasDefinition = true;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to find definition: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSelectedDefinitionChanged(DefinitionLocation? value)
    {
        if (value != null)
        {
            _ = LoadDefinitionCodeAsync(value);
        }
    }

    private async Task LoadDefinitionCodeAsync(DefinitionLocation definition)
    {
        try
        {
            DefinitionFilePath = definition.FilePath;
            DefinitionFileName = definition.FileName;
            DefinitionLine = definition.Line;
            DefinitionColumn = definition.Column;

            // Read the file and get context around the definition
            if (File.Exists(definition.FilePath))
            {
                var lines = await File.ReadAllLinesAsync(definition.FilePath);

                var startLine = Math.Max(0, definition.Line - 1 - ContextLinesBefore);
                var endLine = Math.Min(lines.Length, definition.Line + ContextLinesAfter);

                var codeLines = new List<string>();
                for (int i = startLine; i < endLine; i++)
                {
                    var lineNum = (i + 1).ToString().PadLeft(4);
                    var marker = (i + 1 == definition.Line) ? "→ " : "  ";
                    codeLines.Add($"{marker}{lineNum}  {lines[i]}");
                }

                DefinitionCode = string.Join(Environment.NewLine, codeLines);
            }
        }
        catch (Exception ex)
        {
            DefinitionCode = $"Error loading code: {ex.Message}";
        }
    }

    [RelayCommand]
    private void GoToDefinition()
    {
        if (SelectedDefinition != null)
        {
            NavigateToLocation?.Invoke(this, new NavigateToLocationEventArgs(
                SelectedDefinition.FilePath,
                SelectedDefinition.Line,
                SelectedDefinition.Column));
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    private void Close()
    {
        Closed?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void NextDefinition()
    {
        if (Definitions.Count > 1 && SelectedDefinition != null)
        {
            var index = Definitions.IndexOf(SelectedDefinition);
            if (index < Definitions.Count - 1)
            {
                SelectedDefinition = Definitions[index + 1];
            }
            else
            {
                SelectedDefinition = Definitions[0];
            }
        }
    }

    [RelayCommand]
    private void PreviousDefinition()
    {
        if (Definitions.Count > 1 && SelectedDefinition != null)
        {
            var index = Definitions.IndexOf(SelectedDefinition);
            if (index > 0)
            {
                SelectedDefinition = Definitions[index - 1];
            }
            else
            {
                SelectedDefinition = Definitions[^1];
            }
        }
    }
}

public class DefinitionLocation
{
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; }
    public string Preview { get; set; } = "";
}

public class NavigateToLocationEventArgs : EventArgs
{
    public string FilePath { get; }
    public int Line { get; }
    public int Column { get; }

    public NavigateToLocationEventArgs(string filePath, int line, int column)
    {
        FilePath = filePath;
        Line = line;
        Column = column;
    }
}
