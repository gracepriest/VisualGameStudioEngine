using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels;

/// <summary>
/// ViewModel for the enhanced status bar, providing interactive indicators
/// for line ending, encoding, language mode, and indentation settings.
/// </summary>
public partial class StatusBarViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _lineEnding = "CRLF";

    [ObservableProperty]
    private string _encoding = "UTF-8";

    [ObservableProperty]
    private string _languageMode = "BasicLang";

    [ObservableProperty]
    private string _indentation = "Spaces: 4";

    [ObservableProperty]
    private bool _useSpaces = true;

    [ObservableProperty]
    private int _indentSize = 4;

    // Events to notify the MainWindow to show picker popups
    public event EventHandler? LineEndingClicked;
    public event EventHandler? EncodingClicked;
    public event EventHandler? LanguageModeClicked;
    public event EventHandler? IndentationClicked;

    /// <summary>
    /// Updates the language mode based on the file extension.
    /// </summary>
    public void UpdateForFile(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            LanguageMode = "Plain Text";
            return;
        }

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        LanguageMode = ext switch
        {
            ".bas" => "BasicLang",
            ".bl" => "BasicLang",
            ".blproj" => "BasicLang Project",
            ".cs" => "C#",
            ".vb" => "Visual Basic",
            ".cpp" or ".cxx" or ".cc" => "C++",
            ".h" or ".hpp" => "C++ Header",
            ".c" => "C",
            ".xml" => "XML",
            ".json" => "JSON",
            ".txt" => "Plain Text",
            ".md" => "Markdown",
            _ => "Plain Text"
        };
    }

    /// <summary>
    /// Detects the line ending style from file content.
    /// </summary>
    public void DetectLineEnding(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            LineEnding = "CRLF";
            return;
        }

        var crlfCount = 0;
        var lfCount = 0;

        for (var i = 0; i < content.Length; i++)
        {
            if (content[i] == '\r' && i + 1 < content.Length && content[i + 1] == '\n')
            {
                crlfCount++;
                i++; // Skip the \n
            }
            else if (content[i] == '\n')
            {
                lfCount++;
            }
        }

        LineEnding = crlfCount >= lfCount ? "CRLF" : "LF";
    }

    /// <summary>
    /// Toggles between CRLF and LF line endings.
    /// </summary>
    [RelayCommand]
    private void CycleLineEnding()
    {
        LineEnding = LineEnding == "CRLF" ? "LF" : "CRLF";
        LineEndingClicked?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Cycles through common encoding options.
    /// </summary>
    [RelayCommand]
    private void CycleEncoding()
    {
        Encoding = Encoding switch
        {
            "UTF-8" => "UTF-8 with BOM",
            "UTF-8 with BOM" => "UTF-16 LE",
            "UTF-16 LE" => "UTF-16 BE",
            "UTF-16 BE" => "ASCII",
            "ASCII" => "UTF-8",
            _ => "UTF-8"
        };
        EncodingClicked?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Shows language mode picker.
    /// </summary>
    [RelayCommand]
    private void ShowLanguageMode()
    {
        LanguageModeClicked?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Cycles indentation settings (spaces 2/4/8 and tabs).
    /// </summary>
    [RelayCommand]
    private void CycleIndentation()
    {
        if (UseSpaces)
        {
            IndentSize = IndentSize switch
            {
                2 => 4,
                4 => 8,
                8 => 2,
                _ => 4
            };
            // After cycling through sizes, next click switches to tabs
            if (IndentSize == 2 && _previousIndentSize == 8)
            {
                UseSpaces = false;
                IndentSize = 4;
            }
        }
        else
        {
            UseSpaces = true;
            IndentSize = 2;
        }

        UpdateIndentationDisplay();
        IndentationClicked?.Invoke(this, EventArgs.Empty);
    }

    private int _previousIndentSize = 4;

    partial void OnIndentSizeChanged(int value)
    {
        _previousIndentSize = value;
    }

    private void UpdateIndentationDisplay()
    {
        Indentation = UseSpaces ? $"Spaces: {IndentSize}" : $"Tab Size: {IndentSize}";
    }
}
