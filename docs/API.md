# Visual Game Studio Engine - API Reference

This document provides an overview of the core services and APIs available in the Visual Game Studio Engine IDE.

## Table of Contents

- [Core Services](#core-services)
  - [IProjectService](#iprojectservice)
  - [IBuildService](#ibuildservice)
  - [IDebugService](#idebugservice)
  - [IFileService](#ifileservice)
- [Editor Services](#editor-services)
  - [ILanguageService](#ilanguageservice)
  - [ICodeFormattingService](#icodeformattingservice)
  - [ISymbolSearchService](#isymbolsearchservice)
  - [IFindReplaceService](#ifindReplaceservice)
  - [IRefactoringService](#irefactoringservice)
- [Utility Services](#utility-services)
  - [ICodeMetricsService](#icodemetricsservice)
  - [IBookmarkService](#ibookmarkservice)
  - [INavigationService](#inavigationservice)
  - [ISnippetService](#isnippetservice)
  - [IGitService](#igitservice)
- [Models](#models)

---

## Core Services

### IProjectService

Manages projects and solutions in the IDE.

```csharp
public interface IProjectService
{
    BasicLangProject? CurrentProject { get; }
    BasicLangSolution? CurrentSolution { get; }
    bool HasUnsavedChanges { get; }

    Task<BasicLangProject> CreateProjectAsync(string name, string path, ProjectTemplate template, CancellationToken ct = default);
    Task<BasicLangProject> OpenProjectAsync(string path, CancellationToken ct = default);
    Task SaveProjectAsync(CancellationToken ct = default);
    Task CloseProjectAsync();

    Task<BasicLangSolution> CreateSolutionAsync(string name, string path, CancellationToken ct = default);
    Task<BasicLangSolution> OpenSolutionAsync(string path, CancellationToken ct = default);
    Task SaveSolutionAsync(CancellationToken ct = default);
    Task CloseSolutionAsync();

    Task AddFileToProjectAsync(string filePath, CancellationToken ct = default);
    Task RemoveFileFromProjectAsync(string filePath);
    Task<ProjectItem> AddNewFileAsync(string fileName, string template, CancellationToken ct = default);

    event EventHandler<ProjectEventArgs>? ProjectOpened;
    event EventHandler<ProjectEventArgs>? ProjectClosed;
    event EventHandler<SolutionEventArgs>? SolutionOpened;
    event EventHandler<SolutionEventArgs>? SolutionClosed;
    event EventHandler? ProjectChanged;
}
```

**Project Templates:**
- `ConsoleApplication` - Terminal-based application
- `WindowsFormsApplication` - GUI application
- `GameApplication` - Game using BasicLang game engine
- `ClassLibrary` - Reusable library

---

### IBuildService

Provides build and compilation services.

```csharp
public interface IBuildService
{
    bool IsBuilding { get; }
    BuildConfiguration CurrentConfiguration { get; set; }

    Task<BuildResult> BuildProjectAsync(BasicLangProject project, CancellationToken ct = default);
    Task<BuildResult> BuildSolutionAsync(BasicLangSolution solution, CancellationToken ct = default);
    Task<BuildResult> RebuildProjectAsync(BasicLangProject project, CancellationToken ct = default);
    Task CancelBuildAsync();
    Task CleanAsync(BasicLangProject project, CancellationToken ct = default);

    event EventHandler<BuildProgressEventArgs>? BuildProgress;
    event EventHandler<BuildCompletedEventArgs>? BuildCompleted;
    event EventHandler? BuildStarted;
    event EventHandler? BuildCancelled;
}
```

**Usage Example:**
```csharp
buildService.BuildProgress += (s, e) => Console.WriteLine($"{e.PercentComplete}% - {e.Message}");
var result = await buildService.BuildProjectAsync(project);
if (result.Success)
    Console.WriteLine("Build succeeded!");
```

---

### IDebugService

Provides debugging capabilities using the Debug Adapter Protocol (DAP).

```csharp
public interface IDebugService : IDisposable
{
    DebugState State { get; }
    bool IsDebugging { get; }
    string? CurrentFile { get; }
    int? CurrentLine { get; }
    IReadOnlyList<StackFrame> CallStack { get; }

    Task StartAsync(string projectPath, CancellationToken ct = default);
    Task PauseAsync();
    Task ContinueAsync();
    Task StopAsync();
    Task StepOverAsync();
    Task StepIntoAsync();
    Task StepOutAsync();
    Task RunToCursorAsync(string filePath, int line);

    // Breakpoints
    Task<Breakpoint> SetBreakpointAsync(string filePath, int line, BreakpointOptions? options = null);
    Task RemoveBreakpointAsync(string filePath, int line);
    Task<IReadOnlyList<Breakpoint>> GetBreakpointsAsync();

    // Variables
    Task<IReadOnlyList<Variable>> GetLocalsAsync();
    Task<string> EvaluateExpressionAsync(string expression);

    event EventHandler<DebugStateChangedEventArgs>? StateChanged;
    event EventHandler<DebugStoppedEventArgs>? Stopped;
    event EventHandler<OutputEventArgs>? OutputReceived;
}
```

**Debug States:** `NotStarted`, `Running`, `Paused`, `Stopped`

---

### IFileService

Provides file system operations with change notifications.

```csharp
public interface IFileService
{
    Task<string> ReadFileAsync(string path, CancellationToken ct = default);
    Task WriteFileAsync(string path, string content, CancellationToken ct = default);
    bool FileExists(string path);
    bool DirectoryExists(string path);
    Task<IReadOnlyList<string>> GetSubdirectoriesAsync(string path, CancellationToken ct = default);
    Task<IReadOnlyList<string>> FindFilesAsync(string directory, string pattern, bool recursive = true, CancellationToken ct = default);
    DateTime GetLastWriteTime(string path);
    void WatchFile(string path);
    void UnwatchFile(string path);

    event EventHandler<FileChangedEventArgs>? FileChanged;
}
```

---

## Editor Services

### ILanguageService

LSP-based IntelliSense and code intelligence.

```csharp
public interface ILanguageService : IDisposable
{
    bool IsConnected { get; }

    Task ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync();

    // Document Management
    Task OpenDocumentAsync(string filePath, string content);
    Task UpdateDocumentAsync(string filePath, string content, int version);
    Task CloseDocumentAsync(string filePath);

    // IntelliSense
    Task<IReadOnlyList<CompletionItem>> GetCompletionsAsync(string filePath, int line, int column, CancellationToken ct = default);
    Task<HoverInfo?> GetHoverAsync(string filePath, int line, int column, CancellationToken ct = default);
    Task<SignatureHelp?> GetSignatureHelpAsync(string filePath, int line, int column, CancellationToken ct = default);
    Task<Location?> GoToDefinitionAsync(string filePath, int line, int column, CancellationToken ct = default);
    Task<IReadOnlyList<Location>> FindReferencesAsync(string filePath, int line, int column, CancellationToken ct = default);
    Task<IReadOnlyList<DocumentSymbol>> GetDocumentSymbolsAsync(string filePath, CancellationToken ct = default);

    event EventHandler<DiagnosticsEventArgs>? DiagnosticsReceived;
    event EventHandler<ConnectionChangedEventArgs>? ConnectionChanged;
}
```

---

### ICodeFormattingService

Auto-formats BasicLang source code.

```csharp
public interface ICodeFormattingService
{
    FormattingOptions Options { get; set; }

    string FormatDocument(string sourceCode);
    string FormatSelection(string sourceCode, int startLine, int endLine);
    string FormatLine(string line, int indentLevel);
    int CalculateIndentLevel(string sourceCode, int lineNumber);
    string RemoveTrailingWhitespace(string sourceCode);
    string NormalizeLineEndings(string sourceCode, LineEndingStyle lineEnding);
    IReadOnlyList<FormattingIssue> ValidateFormatting(string sourceCode);
}
```

**Formatting Options:**
| Option | Default | Description |
|--------|---------|-------------|
| `UseTabs` | `false` | Use tabs instead of spaces |
| `IndentSize` | `4` | Spaces per indent level |
| `TrimTrailingWhitespace` | `true` | Remove trailing whitespace |
| `InsertFinalNewline` | `true` | Ensure file ends with newline |
| `MaxLineLength` | `120` | Maximum line length (0 = no limit) |
| `SpaceAroundOperators` | `true` | Add spaces around operators |

**Usage Example:**
```csharp
var formatter = new CodeFormattingService();
formatter.Options.IndentSize = 2;
formatter.Options.UseTabs = false;

var formatted = formatter.FormatDocument(sourceCode);
var issues = formatter.ValidateFormatting(sourceCode);
```

---

### ISymbolSearchService

Provides "Go to Symbol" functionality.

```csharp
public interface ISymbolSearchService
{
    Task<IReadOnlyList<SymbolSearchResult>> SearchInFileAsync(string sourceCode, string query, CancellationToken ct = default);
    Task<IReadOnlyList<SymbolSearchResult>> SearchInProjectAsync(IEnumerable<string> filePaths, string query, CancellationToken ct = default);
    IReadOnlyList<SymbolInfo> GetFileSymbols(string sourceCode);
    SymbolInfo? GetSymbolAtLocation(string sourceCode, int line, int column);
    IReadOnlyList<SymbolInfo> GetBreadcrumb(string sourceCode, int line);
}
```

**Symbol Kinds:** `Class`, `Module`, `Struct`, `Enum`, `Interface`, `Method`, `Function`, `Property`, `Event`, `Field`, `Constant`, `Variable`, `EnumMember`, `Namespace`

**Usage Example:**
```csharp
var symbolService = new SymbolSearchService();

// Get file outline
var symbols = symbolService.GetFileSymbols(sourceCode);
foreach (var symbol in symbols)
{
    Console.WriteLine($"{symbol.Kind}: {symbol.Name} (line {symbol.StartLine})");
    foreach (var child in symbol.Children)
        Console.WriteLine($"  - {child.Kind}: {child.Name}");
}

// Search for symbols
var results = await symbolService.SearchInFileAsync(sourceCode, "Calculate");

// Get breadcrumb for current position
var breadcrumb = symbolService.GetBreadcrumb(sourceCode, currentLine);
// Returns: ["MyClass", "MyMethod"] for position inside MyClass.MyMethod
```

---

### IFindReplaceService

Search and replace functionality.

```csharp
public interface IFindReplaceService
{
    FindReplaceOptions Options { get; set; }

    // Single document operations
    IReadOnlyList<FindMatch> FindInDocument(string content, string pattern);
    FindMatch? FindNext(string content, string pattern, int startOffset);
    FindMatch? FindPrevious(string content, string pattern, int startOffset);
    string ReplaceOne(string content, FindMatch match, string replacement);
    ReplaceAllResult ReplaceAll(string content, string pattern, string replacement);

    // Multi-file operations
    Task<IReadOnlyList<FileSearchResult>> FindInFilesAsync(IEnumerable<string> filePaths, string pattern, string? filePattern = null, CancellationToken ct = default);
    Task<ReplaceInFilesResult> ReplaceInFilesAsync(IEnumerable<string> filePaths, string pattern, string replacement, string? filePattern = null, CancellationToken ct = default);

    // Validation
    bool IsValidPattern(string pattern);
    string? GetPatternError(string pattern);

    event EventHandler<FindReplaceEventArgs>? SearchStarted;
    event EventHandler<FindReplaceEventArgs>? SearchCompleted;
    event EventHandler<FindReplaceProgressEventArgs>? SearchProgress;
}
```

**Find/Replace Options:**
| Option | Default | Description |
|--------|---------|-------------|
| `CaseSensitive` | `false` | Match case exactly |
| `WholeWord` | `false` | Match whole words only |
| `UseRegex` | `false` | Treat pattern as regex |
| `WrapAround` | `true` | Wrap search at document end |
| `PreserveCase` | `false` | Preserve case in replacements |
| `Scope` | `CurrentDocument` | Search scope |

**Usage Example:**
```csharp
var findReplace = new FindReplaceService();
findReplace.Options.CaseSensitive = true;
findReplace.Options.WholeWord = true;

// Find all occurrences
var matches = findReplace.FindInDocument(content, "MyClass");
Console.WriteLine($"Found {matches.Count} matches");

// Replace all
var result = findReplace.ReplaceAll(content, "oldName", "newName");
Console.WriteLine($"Made {result.ReplacementCount} replacements");

// Search in project files
findReplace.Options.UseRegex = true;
var fileResults = await findReplace.FindInFilesAsync(projectFiles, @"TODO:.*", "*.bl");
foreach (var file in fileResults)
{
    Console.WriteLine($"{file.FileName}: {file.MatchCount} matches");
}
```

---

### IRefactoringService

Comprehensive code refactoring capabilities.

```csharp
public interface IRefactoringService
{
    // Rename
    Task<RenameResult> RenameSymbolAsync(string filePath, int line, int column, string newName, CancellationToken ct = default);
    Task<IReadOnlyList<SymbolLocation>> FindAllReferencesAsync(string filePath, int line, int column, CancellationToken ct = default);

    // Extract
    Task<ExtractMethodResult> ExtractMethodAsync(string filePath, int startLine, int startColumn, int endLine, int endColumn, string methodName, CancellationToken ct = default);
    Task<IntroduceVariableResult> IntroduceVariableAsync(string filePath, int startLine, int startColumn, int endLine, int endColumn, string variableName, string? variableType = null, bool replaceAll = false, CancellationToken ct = default);
    Task<ExtractConstantResult> ExtractConstantAsync(string filePath, int startLine, int startColumn, int endLine, int endColumn, ExtractConstantOptions options, CancellationToken ct = default);

    // Inline
    Task<InlineMethodResult> InlineMethodAsync(string filePath, int line, int column, bool removeDefinition = false, CancellationToken ct = default);
    Task<InlineVariableResult> InlineVariableAsync(string filePath, int line, int column, InlineVariableOptions options, CancellationToken ct = default);

    // Class operations
    Task<ExtractInterfaceResult> ExtractInterfaceAsync(string filePath, int line, int column, ExtractInterfaceOptions options, CancellationToken ct = default);
    Task<GenerateConstructorResult> GenerateConstructorAsync(string filePath, int line, int column, GenerateConstructorOptions options, CancellationToken ct = default);
    Task<ImplementInterfaceResult> ImplementInterfaceAsync(string filePath, int line, int column, ImplementInterfaceOptions options, CancellationToken ct = default);
    Task<OverrideMethodResult> OverrideMethodAsync(string filePath, int line, int column, OverrideMethodOptions options, CancellationToken ct = default);

    // Safe operations
    Task<SafeDeleteResult> SafeDeleteAsync(string filePath, int line, int column, SafeDeleteOptions options, CancellationToken ct = default);
}
```

---

## Utility Services

### ICodeMetricsService

Analyzes code complexity and quality metrics.

```csharp
public interface ICodeMetricsService
{
    CodeMetrics CalculateMetrics(string sourceCode);
    MethodMetrics CalculateMethodMetrics(string methodCode);
}
```

**CodeMetrics Properties:**
- `TotalLines` - Total line count
- `CodeLines` - Lines containing code
- `CommentLines` - Lines containing comments
- `BlankLines` - Empty lines
- `ClassCount` - Number of classes/modules
- `MethodCount` - Number of methods/functions
- `AverageCyclomaticComplexity` - Average code complexity
- `MaxNestingDepth` - Maximum nesting level
- `MaintainabilityIndex` - Maintainability score (0-100)

**MethodMetrics Properties:**
- `Name` - Method name
- `LineCount` - Lines in method
- `ParameterCount` - Number of parameters
- `CyclomaticComplexity` - Complexity score
- `NestingDepth` - Maximum nesting
- `LocalVariableCount` - Local variables
- `Rating` - Complexity rating (`Simple`, `Moderate`, `Complex`, `VeryComplex`)

---

### IBookmarkService

Manages code bookmarks.

```csharp
public interface IBookmarkService
{
    IReadOnlyList<Bookmark> Bookmarks { get; }

    void ToggleBookmark(string filePath, int line, string? label = null);
    void RemoveBookmark(string filePath, int line);
    Bookmark? GetNextBookmark(string filePath, int line);
    Bookmark? GetPreviousBookmark(string filePath, int line);
    void UpdateLinesAfterEdit(string filePath, int startLine, int linesAdded);
    void ClearAll();

    event EventHandler<BookmarkChangedEventArgs>? BookmarksChanged;
}
```

---

### INavigationService

Provides back/forward navigation history.

```csharp
public interface INavigationService
{
    bool CanGoBack { get; }
    bool CanGoForward { get; }

    void RecordNavigation(string filePath, int line, int column);
    NavigationLocation? GoBack();
    NavigationLocation? GoForward();
    void Clear();

    event EventHandler? NavigationChanged;
}
```

---

### ISnippetService

Manages and expands code snippets.

```csharp
public interface ISnippetService
{
    IReadOnlyList<CodeSnippet> Snippets { get; }

    void RegisterSnippet(CodeSnippet snippet);
    void RemoveSnippet(string shortcut);
    IReadOnlyList<CodeSnippet> SearchSnippets(string query);
    string ExpandSnippet(CodeSnippet snippet, IDictionary<string, string>? variables = null);

    event EventHandler? SnippetsChanged;
}
```

---

### IGitService

Git version control integration.

```csharp
public interface IGitService
{
    bool IsRepository { get; }
    string? CurrentBranch { get; }
    GitStatus Status { get; }

    Task<GitStatus> GetStatusAsync(CancellationToken ct = default);
    Task<string> GetDiffAsync(string? filePath = null, CancellationToken ct = default);
    Task StageAsync(string filePath, CancellationToken ct = default);
    Task UnstageAsync(string filePath, CancellationToken ct = default);
    Task CommitAsync(string message, CancellationToken ct = default);

    // Branches
    Task<IReadOnlyList<string>> GetBranchesAsync(CancellationToken ct = default);
    Task CreateBranchAsync(string name, CancellationToken ct = default);
    Task CheckoutAsync(string branch, CancellationToken ct = default);
    Task MergeAsync(string branch, CancellationToken ct = default);

    // Remote
    Task PushAsync(CancellationToken ct = default);
    Task PullAsync(CancellationToken ct = default);
    Task FetchAsync(CancellationToken ct = default);

    // History
    Task<IReadOnlyList<GitCommit>> GetCommitsAsync(int count = 50, CancellationToken ct = default);
    Task<IReadOnlyList<BlameLine>> BlameAsync(string filePath, CancellationToken ct = default);

    event EventHandler<GitStatusChangedEventArgs>? StatusChanged;
}
```

---

## Models

### BasicLangProject

```csharp
public class BasicLangProject
{
    public string Name { get; set; }
    public string FilePath { get; set; }
    public string RootDirectory { get; }
    public ProjectType ProjectType { get; set; }
    public IReadOnlyList<ProjectItem> Items { get; }
    public IReadOnlyList<ProjectReference> References { get; }
    public BuildConfiguration Configuration { get; set; }
}
```

### BuildResult

```csharp
public class BuildResult
{
    public bool Success { get; set; }
    public IReadOnlyList<Diagnostic> Errors { get; set; }
    public IReadOnlyList<Diagnostic> Warnings { get; set; }
    public string? OutputPath { get; set; }
    public TimeSpan Duration { get; set; }
}
```

### Diagnostic

```csharp
public class Diagnostic
{
    public string Message { get; set; }
    public DiagnosticSeverity Severity { get; set; }
    public string FilePath { get; set; }
    public int Line { get; set; }
    public int Column { get; set; }
    public string Code { get; set; }
}
```

---

## Getting Started

### Basic Project Workflow

```csharp
// Create services
var projectService = new ProjectService();
var buildService = new BuildService();
var debugService = new DebugService();

// Create a new project
var project = await projectService.CreateProjectAsync(
    "MyGame",
    @"C:\Projects",
    ProjectTemplate.GameApplication);

// Build the project
buildService.BuildProgress += (s, e) => Console.WriteLine(e.Message);
var result = await buildService.BuildProjectAsync(project);

if (result.Success)
{
    // Start debugging
    await debugService.StartAsync(project.FilePath);

    // Set a breakpoint
    await debugService.SetBreakpointAsync("main.bl", 10);
}
```

### Editor Integration

```csharp
// Format code on save
var formatter = new CodeFormattingService();
var formatted = formatter.FormatDocument(sourceCode);

// Provide IntelliSense
var languageService = new LanguageService();
await languageService.ConnectAsync();
await languageService.OpenDocumentAsync(filePath, content);

var completions = await languageService.GetCompletionsAsync(filePath, line, column);
foreach (var item in completions)
{
    Console.WriteLine($"{item.Label} - {item.Kind}");
}
```

---

## License

Visual Game Studio Engine is proprietary software.
