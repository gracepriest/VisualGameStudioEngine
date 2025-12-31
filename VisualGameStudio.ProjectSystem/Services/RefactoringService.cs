using System.Text;
using System.Text.RegularExpressions;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.ProjectSystem.Services;

public class RefactoringService : IRefactoringService
{
    private readonly IProjectService _projectService;
    private readonly IFileService _fileService;

    public RefactoringService(IProjectService projectService, IFileService fileService)
    {
        _projectService = projectService;
        _fileService = fileService;
    }

    public async Task<RenameResult> RenameSymbolAsync(string filePath, int line, int column, string newName, CancellationToken cancellationToken = default)
    {
        try
        {
            var content = await _fileService.ReadFileAsync(filePath, cancellationToken);
            var lines = content.Split('\n');

            if (line < 1 || line > lines.Length)
            {
                return new RenameResult { Success = false, ErrorMessage = "Invalid line number" };
            }

            var lineText = lines[line - 1];
            var symbol = ExtractSymbolAtPosition(lineText, column);

            if (string.IsNullOrEmpty(symbol))
            {
                return new RenameResult { Success = false, ErrorMessage = "No symbol found at position" };
            }

            // Validate new name
            if (!IsValidIdentifier(newName))
            {
                return new RenameResult { Success = false, ErrorMessage = "Invalid identifier name" };
            }

            // Find all references in the project
            var references = await FindAllReferencesAsync(filePath, line, column, cancellationToken);

            // Group by file
            var fileEdits = new List<FileEdit>();
            var fileGroups = references.GroupBy(r => r.FilePath);

            foreach (var group in fileGroups)
            {
                var edits = group.Select(r => new TextEdit
                {
                    StartLine = r.Line,
                    StartColumn = r.Column,
                    EndLine = r.EndLine,
                    EndColumn = r.EndColumn,
                    NewText = newName
                }).OrderByDescending(e => e.StartLine).ThenByDescending(e => e.StartColumn).ToList();

                fileEdits.Add(new FileEdit
                {
                    FilePath = group.Key,
                    Edits = edits
                });
            }

            return new RenameResult
            {
                Success = true,
                FileEdits = fileEdits
            };
        }
        catch (Exception ex)
        {
            return new RenameResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<IReadOnlyList<SymbolLocation>> FindAllReferencesAsync(string filePath, int line, int column, CancellationToken cancellationToken = default)
    {
        var results = new List<SymbolLocation>();

        try
        {
            var content = await _fileService.ReadFileAsync(filePath, cancellationToken);
            var lines = content.Split('\n');

            if (line < 1 || line > lines.Length)
                return results;

            var lineText = lines[line - 1];
            var symbol = ExtractSymbolAtPosition(lineText, column);

            if (string.IsNullOrEmpty(symbol))
                return results;

            // Search in current file first
            await SearchFileForSymbol(filePath, symbol, results, cancellationToken);

            // Search in all project files
            if (_projectService.CurrentProject != null)
            {
                var sourceFiles = _projectService.CurrentProject.GetSourceFiles();
                foreach (var file in sourceFiles)
                {
                    var fullPath = Path.Combine(_projectService.CurrentProject.ProjectDirectory, file.Include);
                    if (fullPath != filePath && File.Exists(fullPath))
                    {
                        await SearchFileForSymbol(fullPath, symbol, results, cancellationToken);
                    }
                }
            }
        }
        catch
        {
            // Ignore errors
        }

        return results;
    }

    private async Task SearchFileForSymbol(string filePath, string symbol, List<SymbolLocation> results, CancellationToken cancellationToken)
    {
        var content = await _fileService.ReadFileAsync(filePath, cancellationToken);
        var lines = content.Split('\n');

        // Pattern to match whole word
        var pattern = $@"\b{Regex.Escape(symbol)}\b";
        var regex = new Regex(pattern, RegexOptions.IgnoreCase);

        for (var i = 0; i < lines.Length; i++)
        {
            var lineText = lines[i];
            var matches = regex.Matches(lineText);

            foreach (Match match in matches)
            {
                var locationType = DetermineLocationType(lineText, match.Index, symbol);
                results.Add(new SymbolLocation
                {
                    FilePath = filePath,
                    Line = i + 1,
                    Column = match.Index + 1,
                    EndLine = i + 1,
                    EndColumn = match.Index + match.Length + 1,
                    Text = lineText.Trim(),
                    Type = locationType
                });
            }
        }
    }

    private SymbolLocationType DetermineLocationType(string lineText, int position, string symbol)
    {
        var trimmed = lineText.TrimStart();

        // Check if it's a definition
        if (trimmed.StartsWith("Sub ", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("Function ", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("Class ", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("Module ", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("Property ", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("Dim ", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("Public ", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("Private ", StringComparison.OrdinalIgnoreCase))
        {
            // Check if symbol appears after the keyword
            var keywordMatch = Regex.Match(trimmed, @"^(Sub|Function|Class|Module|Property|Dim|Public|Private)\s+", RegexOptions.IgnoreCase);
            if (keywordMatch.Success)
            {
                var afterKeyword = trimmed.Substring(keywordMatch.Length);
                if (afterKeyword.StartsWith(symbol, StringComparison.OrdinalIgnoreCase))
                {
                    return SymbolLocationType.Definition;
                }
            }
        }

        return SymbolLocationType.Reference;
    }

    public async Task<ExtractMethodResult> ExtractMethodAsync(string filePath, int startLine, int startColumn, int endLine, int endColumn, string methodName, CancellationToken cancellationToken = default)
    {
        try
        {
            var content = await _fileService.ReadFileAsync(filePath, cancellationToken);
            var lines = content.Split('\n').ToList();

            if (startLine < 1 || endLine > lines.Count)
            {
                return new ExtractMethodResult { Success = false, ErrorMessage = "Invalid selection range" };
            }

            // Extract selected lines
            var selectedLines = new List<string>();
            for (var i = startLine - 1; i < endLine; i++)
            {
                selectedLines.Add(lines[i]);
            }

            var selectedCode = string.Join("\n", selectedLines);

            // Detect indentation
            var indent = new string(' ', 4);
            if (selectedLines.Any())
            {
                var firstNonEmpty = selectedLines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
                if (firstNonEmpty != null)
                {
                    var leadingSpaces = firstNonEmpty.TakeWhile(char.IsWhiteSpace).Count();
                    indent = new string(' ', leadingSpaces);
                }
            }

            // Create new method
            var newMethod = $"\n{indent}Private Sub {methodName}()\n";
            foreach (var line in selectedLines)
            {
                newMethod += $"{indent}    {line.TrimStart()}\n";
            }
            newMethod += $"{indent}End Sub\n";

            // Create edits
            var edits = new List<TextEdit>
            {
                // Replace selection with method call
                new TextEdit
                {
                    StartLine = startLine,
                    StartColumn = 1,
                    EndLine = endLine,
                    EndColumn = lines[endLine - 1].Length + 1,
                    NewText = $"{indent}{methodName}()"
                }
            };

            // Find End Sub/End Function to insert new method after
            var insertLine = FindEndOfCurrentMethod(lines, endLine);
            if (insertLine > 0)
            {
                edits.Add(new TextEdit
                {
                    StartLine = insertLine + 1,
                    StartColumn = 1,
                    EndLine = insertLine + 1,
                    EndColumn = 1,
                    NewText = newMethod
                });
            }

            return new ExtractMethodResult
            {
                Success = true,
                FileEdit = new FileEdit
                {
                    FilePath = filePath,
                    Edits = edits
                }
            };
        }
        catch (Exception ex)
        {
            return new ExtractMethodResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    private int FindEndOfCurrentMethod(List<string> lines, int currentLine)
    {
        for (var i = currentLine; i < lines.Count; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("End Sub", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("End Function", StringComparison.OrdinalIgnoreCase))
            {
                return i + 1;
            }
        }
        return lines.Count;
    }

    public Task<IReadOnlyList<CodeAction>> GetCodeActionsAsync(string filePath, int line, int column, CancellationToken cancellationToken = default)
    {
        var actions = new List<CodeAction>
        {
            new CodeAction
            {
                Id = "rename",
                Title = "Rename Symbol",
                Kind = CodeActionKind.Refactor
            },
            new CodeAction
            {
                Id = "extract-method",
                Title = "Extract Method",
                Kind = CodeActionKind.RefactorExtract
            },
            new CodeAction
            {
                Id = "generate-sub",
                Title = "Generate Sub",
                Kind = CodeActionKind.QuickFix
            }
        };

        return Task.FromResult<IReadOnlyList<CodeAction>>(actions);
    }

    public Task<TextEdit[]> ApplyCodeActionAsync(CodeAction action, CancellationToken cancellationToken = default)
    {
        // Implementation depends on the action
        return Task.FromResult(Array.Empty<TextEdit>());
    }

    public async Task<MethodInfo?> GetMethodInfoAsync(string filePath, int line, int column, CancellationToken cancellationToken = default)
    {
        try
        {
            var content = await _fileService.ReadFileAsync(filePath, cancellationToken);
            var lines = content.Split('\n');

            if (line < 1 || line > lines.Length)
                return null;

            var lineText = lines[line - 1];
            var symbol = ExtractSymbolAtPosition(lineText, column);

            if (string.IsNullOrEmpty(symbol))
                return null;

            // Find the method definition
            var methodDef = await FindMethodDefinitionAsync(filePath, symbol, cancellationToken);
            if (methodDef == null)
                return null;

            // Find all call sites
            var references = await FindAllReferencesAsync(filePath, line, column, cancellationToken);
            var callSites = references.Where(r => r.Type == SymbolLocationType.Reference).ToList();

            return new MethodInfo
            {
                Name = methodDef.Name,
                FilePath = methodDef.FilePath,
                DefinitionLine = methodDef.DefinitionLine,
                DefinitionEndLine = methodDef.DefinitionEndLine,
                Body = methodDef.Body,
                Parameters = methodDef.Parameters,
                IsFunction = methodDef.IsFunction,
                ReturnType = methodDef.ReturnType,
                CallSiteCount = callSites.Count,
                CallSites = callSites
            };
        }
        catch
        {
            return null;
        }
    }

    private async Task<MethodInfo?> FindMethodDefinitionAsync(string filePath, string methodName, CancellationToken cancellationToken)
    {
        var content = await _fileService.ReadFileAsync(filePath, cancellationToken);
        var lines = content.Split('\n');

        // Pattern to match Sub or Function definition
        var subPattern = new Regex($@"^\s*(Private|Public|Protected)?\s*(Sub|Function)\s+{Regex.Escape(methodName)}\s*\(([^)]*)\)(\s+As\s+(\w+))?", RegexOptions.IgnoreCase);

        for (var i = 0; i < lines.Length; i++)
        {
            var match = subPattern.Match(lines[i]);
            if (match.Success)
            {
                var isFunction = match.Groups[2].Value.Equals("Function", StringComparison.OrdinalIgnoreCase);
                var parameters = ParseParameters(match.Groups[3].Value);
                var returnType = match.Groups[5].Success ? match.Groups[5].Value : null;

                // Find the end of the method
                var endLine = FindEndOfMethod(lines, i, isFunction);

                // Extract the body (lines between definition and End Sub/Function)
                var bodyLines = new List<string>();
                for (var j = i + 1; j < endLine; j++)
                {
                    bodyLines.Add(lines[j]);
                }

                return new MethodInfo
                {
                    Name = methodName,
                    FilePath = filePath,
                    DefinitionLine = i + 1,
                    DefinitionEndLine = endLine + 1,
                    Body = string.Join("\n", bodyLines),
                    Parameters = parameters,
                    IsFunction = isFunction,
                    ReturnType = returnType
                };
            }
        }

        // Search in project files if not found in current file
        if (_projectService.CurrentProject != null)
        {
            var sourceFiles = _projectService.CurrentProject.GetSourceFiles();
            foreach (var file in sourceFiles)
            {
                var fullPath = Path.Combine(_projectService.CurrentProject.ProjectDirectory, file.Include);
                if (fullPath != filePath && File.Exists(fullPath))
                {
                    var result = await FindMethodDefinitionInFileAsync(fullPath, methodName, cancellationToken);
                    if (result != null)
                        return result;
                }
            }
        }

        return null;
    }

    private async Task<MethodInfo?> FindMethodDefinitionInFileAsync(string filePath, string methodName, CancellationToken cancellationToken)
    {
        var content = await _fileService.ReadFileAsync(filePath, cancellationToken);
        var lines = content.Split('\n');

        var subPattern = new Regex($@"^\s*(Private|Public|Protected)?\s*(Sub|Function)\s+{Regex.Escape(methodName)}\s*\(([^)]*)\)(\s+As\s+(\w+))?", RegexOptions.IgnoreCase);

        for (var i = 0; i < lines.Length; i++)
        {
            var match = subPattern.Match(lines[i]);
            if (match.Success)
            {
                var isFunction = match.Groups[2].Value.Equals("Function", StringComparison.OrdinalIgnoreCase);
                var parameters = ParseParameters(match.Groups[3].Value);
                var returnType = match.Groups[5].Success ? match.Groups[5].Value : null;

                var endLine = FindEndOfMethod(lines, i, isFunction);

                var bodyLines = new List<string>();
                for (var j = i + 1; j < endLine; j++)
                {
                    bodyLines.Add(lines[j]);
                }

                return new MethodInfo
                {
                    Name = methodName,
                    FilePath = filePath,
                    DefinitionLine = i + 1,
                    DefinitionEndLine = endLine + 1,
                    Body = string.Join("\n", bodyLines),
                    Parameters = parameters,
                    IsFunction = isFunction,
                    ReturnType = returnType
                };
            }
        }

        return null;
    }

    private string[] ParseParameters(string paramString)
    {
        if (string.IsNullOrWhiteSpace(paramString))
            return Array.Empty<string>();

        return paramString.Split(',')
            .Select(p => p.Trim())
            .Where(p => !string.IsNullOrEmpty(p))
            .ToArray();
    }

    private int FindEndOfMethod(string[] lines, int startLine, bool isFunction)
    {
        var endKeyword = isFunction ? "End Function" : "End Sub";
        for (var i = startLine + 1; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith(endKeyword, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return lines.Length - 1;
    }

    public async Task<InlineMethodResult> InlineMethodAsync(string filePath, int line, int column, bool removeDefinition = false, CancellationToken cancellationToken = default)
    {
        try
        {
            // Get method info
            var methodInfo = await GetMethodInfoAsync(filePath, line, column, cancellationToken);
            if (methodInfo == null)
            {
                return new InlineMethodResult { Success = false, ErrorMessage = "Could not find method definition" };
            }

            if (methodInfo.CallSiteCount == 0)
            {
                return new InlineMethodResult { Success = false, ErrorMessage = "No call sites found for this method" };
            }

            // Check if method has parameters - if so, we need to handle argument substitution
            if (methodInfo.Parameters.Length > 0)
            {
                return new InlineMethodResult { Success = false, ErrorMessage = "Inline method with parameters is not yet supported" };
            }

            // Check if it's a function with return value
            if (methodInfo.IsFunction)
            {
                return new InlineMethodResult { Success = false, ErrorMessage = "Inline function is not yet supported" };
            }

            var fileEdits = new Dictionary<string, List<TextEdit>>();

            // Process each call site
            foreach (var callSite in methodInfo.CallSites)
            {
                var callSiteContent = await _fileService.ReadFileAsync(callSite.FilePath, cancellationToken);
                var callSiteLines = callSiteContent.Split('\n');

                // Get the line with the call
                var callLine = callSiteLines[callSite.Line - 1];

                // Get the indentation of the call
                var indent = new string(' ', callLine.TakeWhile(char.IsWhiteSpace).Count());

                // Prepare the inlined body with proper indentation
                var bodyLines = methodInfo.Body.Split('\n');
                var inlinedCode = string.Join("\n", bodyLines.Select(l =>
                {
                    var trimmed = l.TrimStart();
                    return string.IsNullOrWhiteSpace(trimmed) ? "" : indent + trimmed;
                }));

                // Check if the call is a complete statement (e.g., "MethodName()")
                var callPattern = new Regex($@"^\s*{Regex.Escape(methodInfo.Name)}\s*\(\s*\)\s*$", RegexOptions.IgnoreCase);
                if (callPattern.IsMatch(callLine))
                {
                    // Replace the entire line with the inlined body
                    if (!fileEdits.ContainsKey(callSite.FilePath))
                        fileEdits[callSite.FilePath] = new List<TextEdit>();

                    fileEdits[callSite.FilePath].Add(new TextEdit
                    {
                        StartLine = callSite.Line,
                        StartColumn = 1,
                        EndLine = callSite.Line,
                        EndColumn = callLine.Length + 1,
                        NewText = inlinedCode.TrimEnd()
                    });
                }
            }

            // Remove the method definition if requested
            if (removeDefinition && methodInfo.CallSiteCount > 0)
            {
                if (!fileEdits.ContainsKey(methodInfo.FilePath))
                    fileEdits[methodInfo.FilePath] = new List<TextEdit>();

                fileEdits[methodInfo.FilePath].Add(new TextEdit
                {
                    StartLine = methodInfo.DefinitionLine,
                    StartColumn = 1,
                    EndLine = methodInfo.DefinitionEndLine,
                    EndColumn = 1,
                    NewText = ""
                });
            }

            // Convert to FileEdit list
            var result = fileEdits.Select(kvp => new FileEdit
            {
                FilePath = kvp.Key,
                Edits = kvp.Value.OrderByDescending(e => e.StartLine).ThenByDescending(e => e.StartColumn).ToList()
            }).ToList();

            return new InlineMethodResult
            {
                Success = true,
                FileEdits = result,
                CallSitesInlined = methodInfo.CallSiteCount,
                DefinitionRemoved = removeDefinition
            };
        }
        catch (Exception ex)
        {
            return new InlineMethodResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<IntroduceVariableResult> IntroduceVariableAsync(string filePath, int startLine, int startColumn, int endLine, int endColumn, string variableName, string? variableType = null, bool replaceAll = false, CancellationToken cancellationToken = default)
    {
        try
        {
            var content = await _fileService.ReadFileAsync(filePath, cancellationToken);
            var lines = content.Split('\n').ToList();

            if (startLine < 1 || endLine > lines.Count)
            {
                return new IntroduceVariableResult { Success = false, ErrorMessage = "Invalid selection range" };
            }

            // Validate variable name
            if (!IsValidIdentifier(variableName))
            {
                return new IntroduceVariableResult { Success = false, ErrorMessage = "Invalid variable name" };
            }

            // Extract the selected expression
            string selectedExpression;
            if (startLine == endLine)
            {
                var line = lines[startLine - 1];
                var start = Math.Min(startColumn - 1, line.Length);
                var end = Math.Min(endColumn - 1, line.Length);
                selectedExpression = line.Substring(start, end - start);
            }
            else
            {
                // Multi-line selection
                var sb = new System.Text.StringBuilder();
                for (var i = startLine - 1; i < endLine; i++)
                {
                    if (i == startLine - 1)
                        sb.Append(lines[i].Substring(Math.Min(startColumn - 1, lines[i].Length)));
                    else if (i == endLine - 1)
                        sb.Append(lines[i].Substring(0, Math.Min(endColumn - 1, lines[i].Length)));
                    else
                        sb.Append(lines[i]);

                    if (i < endLine - 1)
                        sb.Append('\n');
                }
                selectedExpression = sb.ToString();
            }

            if (string.IsNullOrWhiteSpace(selectedExpression))
            {
                return new IntroduceVariableResult { Success = false, ErrorMessage = "No expression selected" };
            }

            // Infer type from expression if not provided
            var inferredType = variableType ?? InferTypeFromExpression(selectedExpression);

            // Get the indentation of the current line
            var currentLine = lines[startLine - 1];
            var indent = new string(' ', currentLine.TakeWhile(char.IsWhiteSpace).Count());

            // Create the variable declaration
            var declaration = inferredType != null
                ? $"{indent}Dim {variableName} As {inferredType} = {selectedExpression.Trim()}"
                : $"{indent}Dim {variableName} = {selectedExpression.Trim()}";

            var edits = new List<TextEdit>();
            var occurrencesReplaced = 0;

            if (replaceAll)
            {
                // Find all occurrences of the expression in the current method
                var methodBounds = FindCurrentMethodBounds(lines, startLine);
                var expressionPattern = Regex.Escape(selectedExpression.Trim());

                for (var i = methodBounds.Start; i <= methodBounds.End && i < lines.Count; i++)
                {
                    var lineText = lines[i];
                    var matches = Regex.Matches(lineText, expressionPattern);

                    foreach (Match match in matches.Cast<Match>().Reverse())
                    {
                        // Skip if this is the declaration line we're about to add
                        if (i == startLine - 1 && match.Index == startColumn - 1)
                        {
                            // This is the original selection - replace it
                            edits.Add(new TextEdit
                            {
                                StartLine = i + 1,
                                StartColumn = match.Index + 1,
                                EndLine = i + 1,
                                EndColumn = match.Index + match.Length + 1,
                                NewText = variableName
                            });
                            occurrencesReplaced++;
                        }
                        else if (i > startLine - 1 || (i == startLine - 1 && match.Index > startColumn - 1))
                        {
                            // Replace other occurrences after the declaration point
                            edits.Add(new TextEdit
                            {
                                StartLine = i + 1,
                                StartColumn = match.Index + 1,
                                EndLine = i + 1,
                                EndColumn = match.Index + match.Length + 1,
                                NewText = variableName
                            });
                            occurrencesReplaced++;
                        }
                    }
                }
            }
            else
            {
                // Just replace the selected expression
                edits.Add(new TextEdit
                {
                    StartLine = startLine,
                    StartColumn = startColumn,
                    EndLine = endLine,
                    EndColumn = endColumn,
                    NewText = variableName
                });
                occurrencesReplaced = 1;
            }

            // Add the variable declaration before the current statement
            // Find the start of the current statement (the line where selection starts)
            edits.Add(new TextEdit
            {
                StartLine = startLine,
                StartColumn = 1,
                EndLine = startLine,
                EndColumn = 1,
                NewText = declaration + "\n"
            });

            // Sort edits in reverse order for proper application
            edits = edits.OrderByDescending(e => e.StartLine).ThenByDescending(e => e.StartColumn).ToList();

            return new IntroduceVariableResult
            {
                Success = true,
                FileEdit = new FileEdit
                {
                    FilePath = filePath,
                    Edits = edits
                },
                OccurrencesReplaced = occurrencesReplaced,
                VariableName = variableName,
                InferredType = inferredType
            };
        }
        catch (Exception ex)
        {
            return new IntroduceVariableResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<MethodSignatureInfo?> GetSignatureInfoAsync(string filePath, int line, int column, CancellationToken cancellationToken = default)
    {
        try
        {
            var content = await _fileService.ReadFileAsync(filePath, cancellationToken);
            var lines = content.Split('\n');

            if (line < 1 || line > lines.Length)
                return null;

            var lineText = lines[line - 1];
            var symbol = ExtractSymbolAtPosition(lineText, column);

            if (string.IsNullOrEmpty(symbol))
                return null;

            // Find the method definition
            var signatureInfo = await FindSignatureDefinitionAsync(filePath, symbol, cancellationToken);
            if (signatureInfo == null)
                return null;

            // Find all call sites
            var references = await FindAllReferencesAsync(filePath, line, column, cancellationToken);
            var callSites = references.Where(r => r.Type == SymbolLocationType.Reference).ToList();

            signatureInfo.CallSiteCount = callSites.Count;
            signatureInfo.CallSites = callSites;

            return signatureInfo;
        }
        catch
        {
            return null;
        }
    }

    private async Task<MethodSignatureInfo?> FindSignatureDefinitionAsync(string filePath, string methodName, CancellationToken cancellationToken)
    {
        var content = await _fileService.ReadFileAsync(filePath, cancellationToken);
        var lines = content.Split('\n');

        var result = ParseSignatureFromLines(lines, methodName, filePath);
        if (result != null)
            return result;

        // Search in project files if not found in current file
        if (_projectService.CurrentProject != null)
        {
            var sourceFiles = _projectService.CurrentProject.GetSourceFiles();
            foreach (var file in sourceFiles)
            {
                var fullPath = Path.Combine(_projectService.CurrentProject.ProjectDirectory, file.Include);
                if (fullPath != filePath && File.Exists(fullPath))
                {
                    var fileContent = await _fileService.ReadFileAsync(fullPath, cancellationToken);
                    var fileLines = fileContent.Split('\n');
                    result = ParseSignatureFromLines(fileLines, methodName, fullPath);
                    if (result != null)
                        return result;
                }
            }
        }

        return null;
    }

    private MethodSignatureInfo? ParseSignatureFromLines(string[] lines, string methodName, string filePath)
    {
        // Pattern to match Sub or Function definition with detailed parameter parsing
        var subPattern = new Regex($@"^\s*(Private|Public|Protected)?\s*(Shared\s+)?(Sub|Function)\s+{Regex.Escape(methodName)}\s*\(([^)]*)\)(\s+As\s+(\w+))?", RegexOptions.IgnoreCase);

        for (var i = 0; i < lines.Length; i++)
        {
            var match = subPattern.Match(lines[i]);
            if (match.Success)
            {
                var isFunction = match.Groups[3].Value.Equals("Function", StringComparison.OrdinalIgnoreCase);
                var parameters = ParseDetailedParameters(match.Groups[4].Value);
                var returnType = match.Groups[6].Success ? match.Groups[6].Value : null;

                // Find the end of the method
                var endLine = FindEndOfMethod(lines, i, isFunction);

                return new MethodSignatureInfo
                {
                    Name = methodName,
                    FilePath = filePath,
                    DefinitionLine = i + 1,
                    DefinitionEndLine = endLine + 1,
                    IsFunction = isFunction,
                    ReturnType = returnType,
                    Parameters = parameters
                };
            }
        }

        return null;
    }

    private List<MethodParameterInfo> ParseDetailedParameters(string paramString)
    {
        var parameters = new List<MethodParameterInfo>();

        if (string.IsNullOrWhiteSpace(paramString))
            return parameters;

        var paramParts = SplitParameters(paramString);

        for (var i = 0; i < paramParts.Count; i++)
        {
            var param = paramParts[i].Trim();
            if (string.IsNullOrEmpty(param))
                continue;

            var paramInfo = new MethodParameterInfo { OriginalIndex = i };

            // Check for ByRef/ByVal
            if (param.StartsWith("ByRef ", StringComparison.OrdinalIgnoreCase))
            {
                paramInfo.IsByRef = true;
                param = param.Substring(6).Trim();
            }
            else if (param.StartsWith("ByVal ", StringComparison.OrdinalIgnoreCase))
            {
                param = param.Substring(6).Trim();
            }

            // Check for Optional
            if (param.StartsWith("Optional ", StringComparison.OrdinalIgnoreCase))
            {
                paramInfo.IsOptional = true;
                param = param.Substring(9).Trim();
            }

            // Parse name, type, and default value
            // Format: name As Type = defaultValue
            var defaultMatch = Regex.Match(param, @"^(\w+)\s+As\s+(\w+)\s*=\s*(.+)$", RegexOptions.IgnoreCase);
            if (defaultMatch.Success)
            {
                paramInfo.Name = defaultMatch.Groups[1].Value;
                paramInfo.Type = defaultMatch.Groups[2].Value;
                paramInfo.DefaultValue = defaultMatch.Groups[3].Value.Trim();
                paramInfo.IsOptional = true;
            }
            else
            {
                // Format: name As Type
                var typeMatch = Regex.Match(param, @"^(\w+)\s+As\s+(\w+)$", RegexOptions.IgnoreCase);
                if (typeMatch.Success)
                {
                    paramInfo.Name = typeMatch.Groups[1].Value;
                    paramInfo.Type = typeMatch.Groups[2].Value;
                }
                else
                {
                    // Just name
                    paramInfo.Name = param.Split(' ')[0];
                }
            }

            parameters.Add(paramInfo);
        }

        return parameters;
    }

    private List<string> SplitParameters(string paramString)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var parenDepth = 0;

        foreach (var c in paramString)
        {
            if (c == '(')
                parenDepth++;
            else if (c == ')')
                parenDepth--;
            else if (c == ',' && parenDepth == 0)
            {
                result.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
            result.Add(current.ToString());

        return result;
    }

    public async Task<ChangeSignatureResult> ChangeSignatureAsync(string filePath, int line, int column, SignatureChange change, CancellationToken cancellationToken = default)
    {
        try
        {
            // Get signature info
            var signatureInfo = await GetSignatureInfoAsync(filePath, line, column, cancellationToken);
            if (signatureInfo == null)
            {
                return new ChangeSignatureResult { Success = false, ErrorMessage = "Could not find method definition" };
            }

            var fileEdits = new Dictionary<string, List<TextEdit>>();

            // Update the method definition
            await UpdateMethodDefinitionAsync(signatureInfo, change, fileEdits, cancellationToken);

            // Update all call sites
            var callSitesUpdated = await UpdateCallSitesAsync(signatureInfo, change, fileEdits, cancellationToken);

            // Convert to FileEdit list
            var result = fileEdits.Select(kvp => new FileEdit
            {
                FilePath = kvp.Key,
                Edits = kvp.Value.OrderByDescending(e => e.StartLine).ThenByDescending(e => e.StartColumn).ToList()
            }).ToList();

            return new ChangeSignatureResult
            {
                Success = true,
                FileEdits = result,
                CallSitesUpdated = callSitesUpdated
            };
        }
        catch (Exception ex)
        {
            return new ChangeSignatureResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    private async Task UpdateMethodDefinitionAsync(MethodSignatureInfo signatureInfo, SignatureChange change, Dictionary<string, List<TextEdit>> fileEdits, CancellationToken cancellationToken)
    {
        var content = await _fileService.ReadFileAsync(signatureInfo.FilePath, cancellationToken);
        var lines = content.Split('\n');
        var definitionLine = lines[signatureInfo.DefinitionLine - 1];

        // Build new parameter list
        var newParams = BuildNewParameterList(signatureInfo.Parameters, change.Parameters);
        var newParamString = string.Join(", ", newParams);

        // Determine method name (use new name if provided)
        var methodName = change.NewName ?? signatureInfo.Name;

        // Build the new signature line
        var indent = new string(' ', definitionLine.TakeWhile(char.IsWhiteSpace).Count());
        var accessModifier = ExtractAccessModifier(definitionLine);
        var sharedKeyword = definitionLine.Contains("Shared", StringComparison.OrdinalIgnoreCase) ? "Shared " : "";
        var methodKeyword = signatureInfo.IsFunction ? "Function" : "Sub";

        string newDefinition;
        if (signatureInfo.IsFunction)
        {
            var returnType = change.NewReturnType ?? signatureInfo.ReturnType ?? "Object";
            newDefinition = $"{indent}{accessModifier}{sharedKeyword}{methodKeyword} {methodName}({newParamString}) As {returnType}";
        }
        else
        {
            newDefinition = $"{indent}{accessModifier}{sharedKeyword}{methodKeyword} {methodName}({newParamString})";
        }

        if (!fileEdits.ContainsKey(signatureInfo.FilePath))
            fileEdits[signatureInfo.FilePath] = new List<TextEdit>();

        fileEdits[signatureInfo.FilePath].Add(new TextEdit
        {
            StartLine = signatureInfo.DefinitionLine,
            StartColumn = 1,
            EndLine = signatureInfo.DefinitionLine,
            EndColumn = definitionLine.Length + 1,
            NewText = newDefinition
        });

        // If method name changed, also update End Sub/Function
        if (change.NewName != null && change.NewName != signatureInfo.Name)
        {
            // Find and update the End Sub/Function line if it includes the method name
            // (BasicLang typically doesn't include method name in End Sub, but we check anyway)
        }
    }

    private string ExtractAccessModifier(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("Private ", StringComparison.OrdinalIgnoreCase))
            return "Private ";
        if (trimmed.StartsWith("Public ", StringComparison.OrdinalIgnoreCase))
            return "Public ";
        if (trimmed.StartsWith("Protected ", StringComparison.OrdinalIgnoreCase))
            return "Protected ";
        return "";
    }

    private List<string> BuildNewParameterList(List<MethodParameterInfo> originalParams, List<ParameterChange> changes)
    {
        var result = new List<string>();

        // Process parameters according to changes
        foreach (var change in changes.OrderBy(c => c.NewIndex))
        {
            if (change.Kind == ParameterChangeKind.Remove)
                continue;

            var paramStr = new System.Text.StringBuilder();

            if (change.IsByRef)
                paramStr.Append("ByRef ");
            else if (change.Kind != ParameterChangeKind.Add && originalParams.Any(p => p.OriginalIndex == change.OriginalIndex && !p.IsByRef))
                paramStr.Append("ByVal ");

            if (change.IsOptional)
                paramStr.Append("Optional ");

            paramStr.Append(change.Name);

            if (!string.IsNullOrEmpty(change.Type))
                paramStr.Append($" As {change.Type}");

            if (!string.IsNullOrEmpty(change.DefaultValue))
                paramStr.Append($" = {change.DefaultValue}");

            result.Add(paramStr.ToString());
        }

        return result;
    }

    private async Task<int> UpdateCallSitesAsync(MethodSignatureInfo signatureInfo, SignatureChange change, Dictionary<string, List<TextEdit>> fileEdits, CancellationToken cancellationToken)
    {
        var callSitesUpdated = 0;
        var oldName = signatureInfo.Name;
        var newName = change.NewName ?? oldName;

        foreach (var callSite in signatureInfo.CallSites)
        {
            var content = await _fileService.ReadFileAsync(callSite.FilePath, cancellationToken);
            var lines = content.Split('\n');

            if (callSite.Line < 1 || callSite.Line > lines.Length)
                continue;

            var callLine = lines[callSite.Line - 1];

            // Find the call and its arguments
            var callMatch = Regex.Match(callLine, $@"\b{Regex.Escape(oldName)}\s*\(([^)]*)\)", RegexOptions.IgnoreCase);
            if (!callMatch.Success)
                continue;

            var argsString = callMatch.Groups[1].Value;
            var originalArgs = SplitParameters(argsString);

            // Build new arguments list
            var newArgs = BuildNewArgumentsList(originalArgs, signatureInfo.Parameters, change.Parameters);
            var newArgsString = string.Join(", ", newArgs);

            // Build the new call
            var newCall = $"{newName}({newArgsString})";

            // Calculate the position of the call in the line
            var callStart = callMatch.Index;
            var callEnd = callMatch.Index + callMatch.Length;

            if (!fileEdits.ContainsKey(callSite.FilePath))
                fileEdits[callSite.FilePath] = new List<TextEdit>();

            fileEdits[callSite.FilePath].Add(new TextEdit
            {
                StartLine = callSite.Line,
                StartColumn = callStart + 1,
                EndLine = callSite.Line,
                EndColumn = callEnd + 1,
                NewText = newCall
            });

            callSitesUpdated++;
        }

        return callSitesUpdated;
    }

    private List<string> BuildNewArgumentsList(List<string> originalArgs, List<MethodParameterInfo> originalParams, List<ParameterChange> changes)
    {
        var result = new List<string>();

        foreach (var change in changes.OrderBy(c => c.NewIndex))
        {
            if (change.Kind == ParameterChangeKind.Remove)
                continue;

            if (change.Kind == ParameterChangeKind.Add)
            {
                // Use the default value for new parameters
                result.Add(change.DefaultValue ?? GetDefaultValueForType(change.Type));
            }
            else
            {
                // Keep or Modify - use the original argument at the original index
                if (change.OriginalIndex >= 0 && change.OriginalIndex < originalArgs.Count)
                {
                    result.Add(originalArgs[change.OriginalIndex].Trim());
                }
                else
                {
                    // Parameter was added or index is invalid
                    result.Add(change.DefaultValue ?? GetDefaultValueForType(change.Type));
                }
            }
        }

        return result;
    }

    private string GetDefaultValueForType(string? type)
    {
        if (string.IsNullOrEmpty(type))
            return "Nothing";

        return type.ToLowerInvariant() switch
        {
            "integer" or "int" or "long" or "short" or "byte" => "0",
            "single" or "double" or "decimal" => "0.0",
            "string" => "\"\"",
            "boolean" or "bool" => "False",
            "char" => "\"\"c",
            _ => "Nothing"
        };
    }

    private string? InferTypeFromExpression(string expression)
    {
        expression = expression.Trim();

        // String literals
        if (expression.StartsWith("\"") && expression.EndsWith("\""))
            return "String";

        // Integer literals
        if (int.TryParse(expression, out _))
            return "Integer";

        // Long literals
        if (expression.EndsWith("L", StringComparison.OrdinalIgnoreCase) &&
            long.TryParse(expression.TrimEnd('L', 'l'), out _))
            return "Long";

        // Double/Single literals
        if (expression.Contains('.') && double.TryParse(expression.TrimEnd('D', 'd', 'F', 'f'), out _))
        {
            if (expression.EndsWith("F", StringComparison.OrdinalIgnoreCase))
                return "Single";
            return "Double";
        }

        // Boolean literals
        if (expression.Equals("True", StringComparison.OrdinalIgnoreCase) ||
            expression.Equals("False", StringComparison.OrdinalIgnoreCase))
            return "Boolean";

        // Function calls - try to infer from common patterns
        if (expression.Contains("("))
        {
            var funcName = expression.Split('(')[0].Trim();
            switch (funcName.ToLowerInvariant())
            {
                case "len":
                case "instr":
                case "val":
                case "cint":
                case "abs":
                    return "Integer";
                case "mid":
                case "left":
                case "right":
                case "trim":
                case "ltrim":
                case "rtrim":
                case "ucase":
                case "lcase":
                case "str":
                case "cstr":
                    return "String";
                case "cdbl":
                case "sqrt":
                case "sin":
                case "cos":
                case "tan":
                    return "Double";
                case "cbool":
                    return "Boolean";
            }
        }

        // Cannot infer type
        return null;
    }

    private (int Start, int End) FindCurrentMethodBounds(List<string> lines, int currentLine)
    {
        var start = 0;
        var end = lines.Count - 1;

        // Find method start (Sub or Function declaration)
        for (var i = currentLine - 1; i >= 0; i--)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("Sub ", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Function ", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Private Sub ", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Private Function ", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Public Sub ", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Public Function ", StringComparison.OrdinalIgnoreCase))
            {
                start = i;
                break;
            }
        }

        // Find method end
        for (var i = currentLine - 1; i < lines.Count; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("End Sub", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("End Function", StringComparison.OrdinalIgnoreCase))
            {
                end = i;
                break;
            }
        }

        return (start, end);
    }

    private string ExtractSymbolAtPosition(string lineText, int column)
    {
        if (column < 1 || column > lineText.Length + 1)
            return "";

        var pos = column - 1;
        if (pos >= lineText.Length)
            pos = lineText.Length - 1;

        if (pos < 0 || !IsIdentifierChar(lineText[pos]))
            return "";

        // Find start of symbol
        var start = pos;
        while (start > 0 && IsIdentifierChar(lineText[start - 1]))
            start--;

        // Find end of symbol
        var end = pos;
        while (end < lineText.Length - 1 && IsIdentifierChar(lineText[end + 1]))
            end++;

        return lineText.Substring(start, end - start + 1);
    }

    private bool IsIdentifierChar(char c)
    {
        return char.IsLetterOrDigit(c) || c == '_';
    }

    private bool IsValidIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        if (!char.IsLetter(name[0]) && name[0] != '_')
            return false;

        return name.All(c => char.IsLetterOrDigit(c) || c == '_');
    }

    #region Encapsulate Field

    public async Task<FieldInfo?> GetFieldInfoAsync(string filePath, int line, int column, CancellationToken cancellationToken = default)
    {
        try
        {
            var content = await _fileService.ReadFileAsync(filePath, cancellationToken);
            var lines = content.Split('\n');

            if (line < 1 || line > lines.Length)
                return null;

            var lineText = lines[line - 1];
            var symbol = ExtractSymbolAtPosition(lineText, column);

            if (string.IsNullOrEmpty(symbol))
                return null;

            // Try to parse the field on the current line
            var fieldInfo = ParseFieldFromLine(lineText, symbol, filePath, line);
            if (fieldInfo == null)
            {
                // Search for field definition in the file
                fieldInfo = FindFieldDefinition(lines, symbol, filePath);
            }

            if (fieldInfo == null)
                return null;

            // Find all references
            var references = await FindAllReferencesAsync(filePath, line, column, cancellationToken);
            fieldInfo.References = references.Where(r => r.Type == SymbolLocationType.Reference).ToList();
            fieldInfo.ReferenceCount = fieldInfo.References.Count;

            return fieldInfo;
        }
        catch
        {
            return null;
        }
    }

    private FieldInfo? ParseFieldFromLine(string lineText, string fieldName, string filePath, int lineNumber)
    {
        // Pattern: [Public|Private|Protected|Friend] [Shared] [ReadOnly] Name As Type [= InitialValue]
        var pattern = new Regex(
            @"^\s*(Public|Private|Protected|Friend)?\s*(Shared\s+)?(ReadOnly\s+)?(\w+)\s+As\s+(\w+)(\s*=\s*(.+))?$",
            RegexOptions.IgnoreCase);

        var match = pattern.Match(lineText);
        if (!match.Success)
            return null;

        var parsedName = match.Groups[4].Value;
        if (!parsedName.Equals(fieldName, StringComparison.OrdinalIgnoreCase))
            return null;

        var accessibility = ParseAccessibility(match.Groups[1].Value);
        var isShared = match.Groups[2].Success;
        var isReadOnly = match.Groups[3].Success;
        var type = match.Groups[5].Value;
        var initialValue = match.Groups[7].Success ? match.Groups[7].Value.Trim() : null;

        return new FieldInfo
        {
            Name = parsedName,
            FilePath = filePath,
            DefinitionLine = lineNumber,
            Type = type,
            InitialValue = initialValue,
            Accessibility = accessibility,
            IsShared = isShared,
            IsReadOnly = isReadOnly
        };
    }

    private FieldInfo? FindFieldDefinition(string[] lines, string fieldName, string filePath)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            var fieldInfo = ParseFieldFromLine(lines[i], fieldName, filePath, i + 1);
            if (fieldInfo != null)
                return fieldInfo;
        }
        return null;
    }

    private FieldAccessibility ParseAccessibility(string value)
    {
        if (string.IsNullOrEmpty(value))
            return FieldAccessibility.Private;

        return value.ToLowerInvariant() switch
        {
            "public" => FieldAccessibility.Public,
            "private" => FieldAccessibility.Private,
            "protected" => FieldAccessibility.Protected,
            "friend" => FieldAccessibility.Friend,
            _ => FieldAccessibility.Private
        };
    }

    private string AccessibilityToString(FieldAccessibility accessibility)
    {
        return accessibility switch
        {
            FieldAccessibility.Public => "Public",
            FieldAccessibility.Private => "Private",
            FieldAccessibility.Protected => "Protected",
            FieldAccessibility.Friend => "Friend",
            _ => "Private"
        };
    }

    public async Task<EncapsulateFieldResult> EncapsulateFieldAsync(string filePath, int line, int column, EncapsulateFieldOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            // Get field info
            var fieldInfo = await GetFieldInfoAsync(filePath, line, column, cancellationToken);
            if (fieldInfo == null)
            {
                return new EncapsulateFieldResult { Success = false, ErrorMessage = "Could not find field definition" };
            }

            var content = await _fileService.ReadFileAsync(filePath, cancellationToken);
            var lines = content.Split('\n').ToList();
            var edits = new List<TextEdit>();

            // Generate the property code
            var propertyCode = GeneratePropertyCode(fieldInfo, options);

            // Generate the new field declaration (with new name and accessibility)
            var newFieldDecl = GenerateFieldDeclaration(fieldInfo, options);

            // Replace the original field declaration with new field + property
            var originalLine = lines[fieldInfo.DefinitionLine - 1];
            var indent = new string(' ', originalLine.TakeWhile(char.IsWhiteSpace).Count());

            // Build the replacement text
            var replacement = new System.Text.StringBuilder();
            replacement.AppendLine(indent + newFieldDecl);
            replacement.AppendLine();
            replacement.Append(propertyCode);

            edits.Add(new TextEdit
            {
                StartLine = fieldInfo.DefinitionLine,
                StartColumn = 1,
                EndLine = fieldInfo.DefinitionLine,
                EndColumn = originalLine.Length + 1,
                NewText = replacement.ToString().TrimEnd('\r', '\n')
            });

            // Update references if requested
            var referencesUpdated = 0;
            if (options.UpdateReferences && fieldInfo.References.Count > 0)
            {
                foreach (var reference in fieldInfo.References)
                {
                    if (reference.FilePath == filePath)
                    {
                        // Only update if it's not on the definition line
                        if (reference.Line != fieldInfo.DefinitionLine)
                        {
                            edits.Add(new TextEdit
                            {
                                StartLine = reference.Line,
                                StartColumn = reference.Column,
                                EndLine = reference.EndLine,
                                EndColumn = reference.EndColumn,
                                NewText = options.PropertyName
                            });
                            referencesUpdated++;
                        }
                    }
                }
            }

            // Sort edits in reverse order
            var sortedEdits = edits.OrderByDescending(e => e.StartLine).ThenByDescending(e => e.StartColumn).ToList();

            return new EncapsulateFieldResult
            {
                Success = true,
                FileEdits = new List<FileEdit>
                {
                    new FileEdit
                    {
                        FilePath = filePath,
                        Edits = sortedEdits
                    }
                },
                ReferencesUpdated = referencesUpdated,
                PropertyName = options.PropertyName,
                FieldName = options.FieldName
            };
        }
        catch (Exception ex)
        {
            return new EncapsulateFieldResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    private string GenerateFieldDeclaration(FieldInfo fieldInfo, EncapsulateFieldOptions options)
    {
        var sb = new System.Text.StringBuilder();

        sb.Append(AccessibilityToString(options.FieldAccessibility));
        sb.Append(' ');

        if (fieldInfo.IsShared)
            sb.Append("Shared ");

        if (fieldInfo.IsReadOnly)
            sb.Append("ReadOnly ");

        sb.Append(options.FieldName);
        sb.Append(" As ");
        sb.Append(fieldInfo.Type ?? "Object");

        if (!string.IsNullOrEmpty(fieldInfo.InitialValue))
        {
            sb.Append(" = ");
            sb.Append(fieldInfo.InitialValue);
        }

        return sb.ToString();
    }

    private string GeneratePropertyCode(FieldInfo fieldInfo, EncapsulateFieldOptions options)
    {
        var sb = new System.Text.StringBuilder();
        var indent = "    "; // Base indentation

        // Property declaration
        sb.Append(indent);
        sb.Append(AccessibilityToString(options.PropertyAccessibility));
        sb.Append(' ');

        if (fieldInfo.IsShared)
            sb.Append("Shared ");

        if (options.GenerateGetter && !options.GenerateSetter)
            sb.Append("ReadOnly ");

        sb.Append("Property ");
        sb.Append(options.PropertyName);
        sb.Append(" As ");
        sb.AppendLine(fieldInfo.Type ?? "Object");

        // Getter
        if (options.GenerateGetter)
        {
            sb.Append(indent);
            sb.Append(indent);
            sb.AppendLine("Get");
            sb.Append(indent);
            sb.Append(indent);
            sb.Append(indent);
            sb.Append("Return ");
            sb.AppendLine(options.FieldName);
            sb.Append(indent);
            sb.Append(indent);
            sb.AppendLine("End Get");
        }

        // Setter
        if (options.GenerateSetter && !fieldInfo.IsReadOnly)
        {
            sb.Append(indent);
            sb.Append(indent);
            sb.AppendLine("Set(value As " + (fieldInfo.Type ?? "Object") + ")");
            sb.Append(indent);
            sb.Append(indent);
            sb.Append(indent);
            sb.Append(options.FieldName);
            sb.AppendLine(" = value");
            sb.Append(indent);
            sb.Append(indent);
            sb.AppendLine("End Set");
        }

        // End Property
        sb.Append(indent);
        sb.Append("End Property");

        return sb.ToString();
    }

    #endregion

    #region Inline Field

    public async Task<InlineFieldResult> InlineFieldAsync(string filePath, int line, int column, InlineFieldOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            // Get field info
            var fieldInfo = await GetFieldInfoAsync(filePath, line, column, cancellationToken);
            if (fieldInfo == null)
            {
                return new InlineFieldResult
                {
                    Success = false,
                    ErrorMessage = "Could not find field at cursor position"
                };
            }

            // Check if field has an initializer
            if (string.IsNullOrEmpty(fieldInfo.InitialValue))
            {
                return new InlineFieldResult
                {
                    Success = false,
                    ErrorMessage = "Field has no initializer expression to inline"
                };
            }

            // Check if field is referenced
            if (fieldInfo.ReferenceCount == 0)
            {
                return new InlineFieldResult
                {
                    Success = false,
                    ErrorMessage = "Field is not used anywhere. Consider removing it instead."
                };
            }

            // Read the file content
            var content = await _fileService.ReadFileAsync(filePath, cancellationToken);
            var lines = content.Split('\n').ToList();

            var expression = fieldInfo.InitialValue;
            var fileEdits = new List<FileEdit>();
            var totalUsagesReplaced = 0;

            // Group references by file
            var referencesByFile = fieldInfo.References.GroupBy(r => r.FilePath);

            foreach (var fileGroup in referencesByFile)
            {
                var refFilePath = fileGroup.Key;

                // Skip files other than the current file if not inlining across files
                if (!options.InlineAcrossFiles && refFilePath != filePath)
                {
                    continue;
                }

                var fileContent = refFilePath == filePath
                    ? content
                    : await _fileService.ReadFileAsync(refFilePath, cancellationToken);
                var fileLines = fileContent.Split('\n').ToList();

                var edits = new List<TextEdit>();

                // Sort references in reverse order to maintain positions
                var sortedRefs = fileGroup
                    .Where(r => r.Line != fieldInfo.DefinitionLine) // Skip definition line
                    .OrderByDescending(r => r.Line)
                    .ThenByDescending(r => r.Column)
                    .ToList();

                foreach (var reference in sortedRefs)
                {
                    if (reference.Line < 1 || reference.Line > fileLines.Count)
                        continue;

                    var refLine = fileLines[reference.Line - 1];
                    var replacementExpr = expression;

                    // Add parentheses if needed
                    if (options.AddParenthesesIfNeeded && NeedsParenthesesForField(refLine, reference.Column, expression))
                    {
                        replacementExpr = $"({expression})";
                    }

                    edits.Add(new TextEdit
                    {
                        StartLine = reference.Line,
                        StartColumn = reference.Column,
                        EndLine = reference.EndLine,
                        EndColumn = reference.EndColumn,
                        NewText = replacementExpr
                    });

                    totalUsagesReplaced++;
                }

                if (edits.Count > 0)
                {
                    fileEdits.Add(new FileEdit
                    {
                        FilePath = refFilePath,
                        Edits = edits.OrderByDescending(e => e.StartLine).ThenByDescending(e => e.StartColumn).ToList()
                    });
                }
            }

            // Remove the field declaration if requested
            var declarationRemoved = false;
            if (options.RemoveDeclaration)
            {
                var declFilePath = fieldInfo.FilePath;
                var existingEdit = fileEdits.FirstOrDefault(e => e.FilePath == declFilePath);

                var removeEdit = new TextEdit
                {
                    StartLine = fieldInfo.DefinitionLine,
                    StartColumn = 1,
                    EndLine = fieldInfo.DefinitionLine + 1,
                    EndColumn = 1,
                    NewText = "" // Remove the entire line
                };

                if (existingEdit != null)
                {
                    var editList = existingEdit.Edits.ToList();
                    editList.Add(removeEdit);
                    existingEdit = new FileEdit
                    {
                        FilePath = declFilePath,
                        Edits = editList.OrderByDescending(e => e.StartLine).ThenByDescending(e => e.StartColumn).ToList()
                    };

                    // Replace in fileEdits list
                    fileEdits = fileEdits.Select(e => e.FilePath == declFilePath ? existingEdit : e).ToList();
                }
                else
                {
                    fileEdits.Add(new FileEdit
                    {
                        FilePath = declFilePath,
                        Edits = new List<TextEdit> { removeEdit }
                    });
                }

                declarationRemoved = true;
            }

            return new InlineFieldResult
            {
                Success = true,
                FileEdits = fileEdits,
                UsagesReplaced = totalUsagesReplaced,
                DeclarationRemoved = declarationRemoved,
                FieldName = fieldInfo.Name,
                InlinedExpression = expression
            };
        }
        catch (Exception ex)
        {
            return new InlineFieldResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private bool NeedsParenthesesForField(string line, int column, string expression)
    {
        // Check if the expression contains operators that might need parentheses
        var hasOperators = expression.Contains("+") ||
                          expression.Contains("-") ||
                          expression.Contains("*") ||
                          expression.Contains("/") ||
                          expression.Contains("&") ||
                          expression.Contains("And") ||
                          expression.Contains("Or") ||
                          expression.Contains("Mod");

        if (!hasOperators)
            return false;

        // Check the context around the usage
        if (column > 1)
        {
            var beforeChar = line[column - 2];
            // If preceded by a member access, don't add parentheses
            if (beforeChar == '.')
                return false;
        }

        // Check what follows the field name
        var afterIndex = column - 1;
        // Find the end of the identifier
        while (afterIndex < line.Length && (char.IsLetterOrDigit(line[afterIndex]) || line[afterIndex] == '_'))
        {
            afterIndex++;
        }

        if (afterIndex < line.Length)
        {
            var afterChar = line[afterIndex];
            // If followed by member access, don't add parentheses
            if (afterChar == '.')
                return false;
            // If followed by operators, add parentheses
            if (afterChar == '*' || afterChar == '/' || afterChar == '+' || afterChar == '-' || afterChar == '^')
                return true;
        }

        return false;
    }

    #endregion

    #region Extract Constant

    public async Task<LiteralInfo?> GetLiteralInfoAsync(string filePath, int startLine, int startColumn, int endLine, int endColumn, CancellationToken cancellationToken = default)
    {
        try
        {
            var content = await _fileService.ReadFileAsync(filePath, cancellationToken);
            var lines = content.Split('\n');

            if (startLine < 1 || startLine > lines.Length)
                return null;

            // Extract the selected text
            var selectedText = ExtractSelectedText(lines, startLine, startColumn, endLine, endColumn);
            if (string.IsNullOrWhiteSpace(selectedText))
                return null;

            // Determine if the selected text is a literal
            var literalType = DetermineLiteralType(selectedText);
            if (literalType == null)
                return null;

            // Find the containing type and method
            var containingType = FindContainingType(lines, startLine);
            var containingMethod = FindContainingMethodForConstant(lines, startLine);

            // Find all occurrences of this literal in the file
            var occurrences = FindLiteralOccurrences(lines, selectedText, filePath);

            // Generate a suggested name
            var suggestedName = GenerateConstantName(selectedText, literalType.Value);

            // Infer the type
            var inferredType = InferConstantType(literalType.Value);

            return new LiteralInfo
            {
                Value = selectedText,
                Type = literalType.Value,
                FilePath = filePath,
                StartLine = startLine,
                StartColumn = startColumn,
                EndLine = endLine,
                EndColumn = endColumn,
                ContainingType = containingType,
                ContainingMethod = containingMethod,
                OccurrenceCount = occurrences.Count,
                Occurrences = occurrences,
                SuggestedName = suggestedName,
                InferredType = inferredType
            };
        }
        catch
        {
            return null;
        }
    }

    public async Task<ExtractConstantResult> ExtractConstantAsync(string filePath, int startLine, int startColumn, int endLine, int endColumn, ExtractConstantOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            var literalInfo = await GetLiteralInfoAsync(filePath, startLine, startColumn, endLine, endColumn, cancellationToken);
            if (literalInfo == null)
            {
                return new ExtractConstantResult
                {
                    Success = false,
                    ErrorMessage = "No valid literal found at the specified location"
                };
            }

            if (string.IsNullOrEmpty(options.ConstantName))
            {
                return new ExtractConstantResult
                {
                    Success = false,
                    ErrorMessage = "Constant name is required"
                };
            }

            if (!IsValidIdentifier(options.ConstantName))
            {
                return new ExtractConstantResult
                {
                    Success = false,
                    ErrorMessage = "Invalid constant name"
                };
            }

            var content = await _fileService.ReadFileAsync(filePath, cancellationToken);
            var lines = content.Split('\n').ToList();
            var edits = new List<TextEdit>();

            var constantType = options.ConstantType ?? literalInfo.InferredType;
            var accessModifier = ConstantAccessibilityToString(options.Accessibility);

            // Determine where to insert the constant declaration
            int insertionLine;
            string indent;

            if (options.CreateAsShared)
            {
                // Find the class/module level to insert the constant
                var classInfo = FindContainingTypeInfo(lines, startLine);
                if (classInfo != null)
                {
                    insertionLine = classInfo.InsertionLine;
                    indent = classInfo.Indent;
                }
                else
                {
                    // Fallback: insert at the beginning of the file
                    insertionLine = 1;
                    indent = "";
                }

                // Generate the constant declaration
                var constDeclaration = $"{indent}{accessModifier} Const {options.ConstantName} As {constantType} = {literalInfo.Value}";

                edits.Add(new TextEdit
                {
                    StartLine = insertionLine,
                    StartColumn = 1,
                    EndLine = insertionLine,
                    EndColumn = 1,
                    NewText = constDeclaration + "\n"
                });
            }
            else
            {
                // Local constant - insert before the current line
                var currentLineText = lines[startLine - 1];
                indent = new string(' ', currentLineText.TakeWhile(char.IsWhiteSpace).Count());

                var constDeclaration = $"{indent}Const {options.ConstantName} As {constantType} = {literalInfo.Value}";

                edits.Add(new TextEdit
                {
                    StartLine = startLine,
                    StartColumn = 1,
                    EndLine = startLine,
                    EndColumn = 1,
                    NewText = constDeclaration + "\n"
                });
            }

            // Replace occurrences with the constant name
            var occurrencesToReplace = options.ReplaceAllOccurrences
                ? literalInfo.Occurrences
                : new List<SymbolLocation> { new SymbolLocation
                    {
                        FilePath = filePath,
                        Line = startLine,
                        Column = startColumn,
                        EndLine = endLine,
                        EndColumn = endColumn
                    }
                };

            // Sort occurrences in reverse order to maintain positions
            var sortedOccurrences = occurrencesToReplace
                .OrderByDescending(o => o.Line)
                .ThenByDescending(o => o.Column)
                .ToList();

            foreach (var occurrence in sortedOccurrences)
            {
                edits.Add(new TextEdit
                {
                    StartLine = occurrence.Line,
                    StartColumn = occurrence.Column,
                    EndLine = occurrence.EndLine,
                    EndColumn = occurrence.EndColumn,
                    NewText = options.ConstantName
                });
            }

            // Sort all edits in reverse order
            var sortedEdits = edits
                .OrderByDescending(e => e.StartLine)
                .ThenByDescending(e => e.StartColumn)
                .ToList();

            return new ExtractConstantResult
            {
                Success = true,
                FileEdit = new FileEdit
                {
                    FilePath = filePath,
                    Edits = sortedEdits
                },
                ConstantName = options.ConstantName,
                ConstantValue = literalInfo.Value,
                ConstantType = constantType,
                OccurrencesReplaced = occurrencesToReplace.Count
            };
        }
        catch (Exception ex)
        {
            return new ExtractConstantResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private string ExtractSelectedText(string[] lines, int startLine, int startColumn, int endLine, int endColumn)
    {
        if (startLine == endLine)
        {
            var line = lines[startLine - 1];
            if (startColumn <= line.Length && endColumn <= line.Length + 1)
            {
                return line.Substring(startColumn - 1, endColumn - startColumn);
            }
        }
        else
        {
            var sb = new System.Text.StringBuilder();
            for (var i = startLine; i <= endLine; i++)
            {
                var line = lines[i - 1];
                if (i == startLine)
                {
                    sb.Append(line.Substring(startColumn - 1));
                }
                else if (i == endLine)
                {
                    sb.Append(line.Substring(0, Math.Min(endColumn - 1, line.Length)));
                }
                else
                {
                    sb.Append(line);
                }
                if (i < endLine)
                {
                    sb.Append('\n');
                }
            }
            return sb.ToString();
        }
        return "";
    }

    private LiteralType? DetermineLiteralType(string text)
    {
        text = text.Trim();

        // String literal
        if (text.StartsWith("\"") && text.EndsWith("\""))
            return LiteralType.String;

        // Char literal
        if (text.StartsWith("\"") && text.EndsWith("\"c"))
            return LiteralType.Char;

        // Boolean literals
        if (text.Equals("True", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("False", StringComparison.OrdinalIgnoreCase))
            return LiteralType.Boolean;

        // Nothing literal
        if (text.Equals("Nothing", StringComparison.OrdinalIgnoreCase))
            return LiteralType.Nothing;

        // Numeric literals
        if (Regex.IsMatch(text, @"^-?\d+L$", RegexOptions.IgnoreCase))
            return LiteralType.Long;

        if (Regex.IsMatch(text, @"^-?\d+(\.\d+)?[FfRr]$"))
            return LiteralType.Single;

        if (Regex.IsMatch(text, @"^-?\d+(\.\d+)?[Dd]?$") && text.Contains("."))
            return LiteralType.Double;

        if (Regex.IsMatch(text, @"^-?\d+(\.\d+)?[Mm]$", RegexOptions.IgnoreCase))
            return LiteralType.Decimal;

        if (Regex.IsMatch(text, @"^-?\d+$"))
            return LiteralType.Integer;

        // Date literal
        if (text.StartsWith("#") && text.EndsWith("#"))
            return LiteralType.Date;

        return null;
    }

    private string GenerateConstantName(string value, LiteralType type)
    {
        value = value.Trim();

        switch (type)
        {
            case LiteralType.String:
                // Remove quotes and generate name from content
                var stringContent = value.Trim('"');
                if (string.IsNullOrEmpty(stringContent))
                    return "EMPTY_STRING";

                // Take first few words and convert to constant case
                var words = Regex.Replace(stringContent, @"[^a-zA-Z0-9\s]", "")
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Take(3)
                    .Select(w => w.ToUpperInvariant());
                return string.Join("_", words);

            case LiteralType.Integer:
            case LiteralType.Long:
            case LiteralType.Single:
            case LiteralType.Double:
            case LiteralType.Decimal:
                // Generate name based on the numeric value
                var numStr = Regex.Replace(value, @"[^0-9.-]", "");
                if (numStr == "0") return "ZERO";
                if (numStr == "1") return "ONE";
                if (numStr == "-1") return "NEGATIVE_ONE";
                if (numStr == "100") return "HUNDRED";
                if (numStr == "1000") return "THOUSAND";
                return $"VALUE_{numStr.Replace(".", "_").Replace("-", "NEG_")}";

            case LiteralType.Boolean:
                return value.ToUpperInvariant();

            case LiteralType.Char:
                return "CHAR_CONSTANT";

            case LiteralType.Date:
                return "DATE_CONSTANT";

            case LiteralType.Nothing:
                return "NULL_VALUE";

            default:
                return "CONSTANT";
        }
    }

    private string InferConstantType(LiteralType type)
    {
        return type switch
        {
            LiteralType.Integer => "Integer",
            LiteralType.Long => "Long",
            LiteralType.Single => "Single",
            LiteralType.Double => "Double",
            LiteralType.Decimal => "Decimal",
            LiteralType.String => "String",
            LiteralType.Char => "Char",
            LiteralType.Boolean => "Boolean",
            LiteralType.Date => "Date",
            LiteralType.Nothing => "Object",
            _ => "Object"
        };
    }

    private List<SymbolLocation> FindLiteralOccurrences(string[] lines, string literalValue, string filePath)
    {
        var occurrences = new List<SymbolLocation>();
        var escapedValue = Regex.Escape(literalValue);

        // For string literals, we need exact match including quotes
        // For other literals, we need word boundary matching
        string pattern;
        if (literalValue.StartsWith("\""))
        {
            pattern = escapedValue;
        }
        else
        {
            pattern = $@"(?<![a-zA-Z0-9_]){escapedValue}(?![a-zA-Z0-9_])";
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var matches = Regex.Matches(line, pattern);

            foreach (Match match in matches)
            {
                // Skip if inside a comment
                var commentIndex = line.IndexOf('\'');
                if (commentIndex >= 0 && match.Index >= commentIndex)
                    continue;

                occurrences.Add(new SymbolLocation
                {
                    FilePath = filePath,
                    Line = i + 1,
                    Column = match.Index + 1,
                    EndLine = i + 1,
                    EndColumn = match.Index + match.Length + 1,
                    Text = literalValue,
                    Type = SymbolLocationType.Reference
                });
            }
        }

        return occurrences;
    }

    private string? FindContainingMethodForConstant(string[] lines, int line)
    {
        for (var i = line - 1; i >= 0; i--)
        {
            var text = lines[i].TrimStart();

            // Check for End Sub/Function first to know we're not in a method
            if (Regex.IsMatch(text, @"^End\s+(Sub|Function)", RegexOptions.IgnoreCase))
                return null;

            var match = Regex.Match(text, @"^(?:Public\s+|Private\s+|Protected\s+|Friend\s+)?(?:Shared\s+)?(?:Overrides\s+)?(?:Overridable\s+)?(?:Sub|Function)\s+(\w+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }
        return null;
    }

    private class ContainingTypeInfo
    {
        public string Name { get; set; } = "";
        public int InsertionLine { get; set; }
        public string Indent { get; set; } = "";
    }

    private ContainingTypeInfo? FindContainingTypeInfo(List<string> lines, int currentLine)
    {
        for (var i = currentLine - 1; i >= 0; i--)
        {
            var text = lines[i].TrimStart();
            var match = Regex.Match(text, @"^(?:Public\s+|Private\s+|Protected\s+|Friend\s+)?(?:Partial\s+)?(?:Class|Module|Structure)\s+(\w+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                // Find the indentation of the next line after the class declaration
                var classIndent = new string(' ', lines[i].TakeWhile(char.IsWhiteSpace).Count());
                var memberIndent = classIndent + "    ";

                // Find a good insertion point (after class declaration, before first member)
                var insertLine = i + 2; // Default to line after class declaration

                for (var j = i + 1; j < lines.Count; j++)
                {
                    var memberText = lines[j].TrimStart();
                    // Skip empty lines and comments
                    if (string.IsNullOrWhiteSpace(memberText) || memberText.StartsWith("'"))
                    {
                        insertLine = j + 2;
                        continue;
                    }
                    // Found first member, insert before it
                    insertLine = j + 1;
                    break;
                }

                return new ContainingTypeInfo
                {
                    Name = match.Groups[1].Value,
                    InsertionLine = insertLine,
                    Indent = memberIndent
                };
            }
        }
        return null;
    }

    private string ConstantAccessibilityToString(ConstantAccessibility accessibility)
    {
        return accessibility switch
        {
            ConstantAccessibility.Public => "Public",
            ConstantAccessibility.Private => "Private",
            ConstantAccessibility.Protected => "Protected",
            ConstantAccessibility.Friend => "Friend",
            _ => "Private"
        };
    }

    #endregion

    #region Inline Constant

    public async Task<ConstantInfo?> GetConstantInfoAsync(string filePath, int line, int column, CancellationToken cancellationToken = default)
    {
        try
        {
            var content = await _fileService.ReadFileAsync(filePath, cancellationToken);
            var lines = content.Split('\n');

            if (line < 1 || line > lines.Length)
                return null;

            // Get the word at the cursor position
            var currentLine = lines[line - 1];
            var word = GetWordAtPosition(currentLine, column);
            if (string.IsNullOrEmpty(word))
                return null;

            // Find the constant declaration
            var constDecl = FindConstantDeclaration(lines, word);
            if (constDecl == null)
                return null;

            // Find all references to the constant
            var references = FindConstantReferences(lines, word, filePath, constDecl.DefinitionLine);

            return new ConstantInfo
            {
                Name = constDecl.Name,
                Value = constDecl.Value,
                Type = constDecl.Type,
                FilePath = filePath,
                DefinitionLine = constDecl.DefinitionLine,
                DefinitionColumn = constDecl.DefinitionColumn,
                Accessibility = constDecl.Accessibility,
                IsShared = constDecl.IsShared,
                ContainingType = FindContainingType(lines, constDecl.DefinitionLine),
                ReferenceCount = references.Count,
                References = references
            };
        }
        catch
        {
            return null;
        }
    }

    public async Task<InlineConstantResult> InlineConstantAsync(string filePath, int line, int column, InlineConstantOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            var constantInfo = await GetConstantInfoAsync(filePath, line, column, cancellationToken);
            if (constantInfo == null)
            {
                return new InlineConstantResult
                {
                    Success = false,
                    ErrorMessage = "Could not find constant at cursor position"
                };
            }

            if (string.IsNullOrEmpty(constantInfo.Value))
            {
                return new InlineConstantResult
                {
                    Success = false,
                    ErrorMessage = "Constant has no value to inline"
                };
            }

            var content = await _fileService.ReadFileAsync(filePath, cancellationToken);
            var lines = content.Split('\n').ToList();
            var edits = new List<TextEdit>();

            // Determine which references to inline
            var referencesToInline = options.InlineAllReferences
                ? constantInfo.References
                : constantInfo.References.Where(r => r.Line == line).ToList();

            if (referencesToInline.Count == 0)
            {
                return new InlineConstantResult
                {
                    Success = false,
                    ErrorMessage = "No references found to inline"
                };
            }

            // Sort references in reverse order to maintain positions
            var sortedRefs = referencesToInline
                .OrderByDescending(r => r.Line)
                .ThenByDescending(r => r.Column)
                .ToList();

            foreach (var reference in sortedRefs)
            {
                edits.Add(new TextEdit
                {
                    StartLine = reference.Line,
                    StartColumn = reference.Column,
                    EndLine = reference.EndLine,
                    EndColumn = reference.EndColumn,
                    NewText = constantInfo.Value
                });
            }

            // Remove the declaration if requested and all references are inlined
            var declarationRemoved = false;
            if (options.RemoveDeclaration && options.InlineAllReferences)
            {
                edits.Add(new TextEdit
                {
                    StartLine = constantInfo.DefinitionLine,
                    StartColumn = 1,
                    EndLine = constantInfo.DefinitionLine + 1,
                    EndColumn = 1,
                    NewText = ""
                });
                declarationRemoved = true;
            }

            // Sort all edits in reverse order
            var sortedEdits = edits
                .OrderByDescending(e => e.StartLine)
                .ThenByDescending(e => e.StartColumn)
                .ToList();

            return new InlineConstantResult
            {
                Success = true,
                FileEdit = new FileEdit
                {
                    FilePath = filePath,
                    Edits = sortedEdits
                },
                ConstantName = constantInfo.Name,
                InlinedValue = constantInfo.Value,
                ReferencesInlined = referencesToInline.Count,
                DeclarationRemoved = declarationRemoved
            };
        }
        catch (Exception ex)
        {
            return new InlineConstantResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private class ConstantDeclaration
    {
        public string Name { get; set; } = "";
        public string Value { get; set; } = "";
        public string? Type { get; set; }
        public int DefinitionLine { get; set; }
        public int DefinitionColumn { get; set; }
        public string Accessibility { get; set; } = "Private";
        public bool IsShared { get; set; }
    }

    private ConstantDeclaration? FindConstantDeclaration(string[] lines, string constantName)
    {
        // Pattern to match constant declarations:
        // [Public|Private|Protected|Friend] [Shared] Const name [As Type] = value
        var pattern = $@"^(\s*)(?:(Public|Private|Protected|Friend)\s+)?(?:(Shared)\s+)?Const\s+{Regex.Escape(constantName)}\s*(?:As\s+(\w+))?\s*=\s*(.+)$";

        for (var i = 0; i < lines.Length; i++)
        {
            var match = Regex.Match(lines[i], pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var indent = match.Groups[1].Value;
                var accessibility = match.Groups[2].Success ? match.Groups[2].Value : "Private";
                var isShared = match.Groups[3].Success;
                var type = match.Groups[4].Success ? match.Groups[4].Value : null;
                var value = match.Groups[5].Value.Trim();

                // Find the column where the constant name starts
                var nameIndex = lines[i].IndexOf(constantName, StringComparison.OrdinalIgnoreCase);

                return new ConstantDeclaration
                {
                    Name = constantName,
                    Value = value,
                    Type = type,
                    DefinitionLine = i + 1,
                    DefinitionColumn = nameIndex + 1,
                    Accessibility = accessibility,
                    IsShared = isShared
                };
            }
        }

        return null;
    }

    private List<SymbolLocation> FindConstantReferences(string[] lines, string constantName, string filePath, int definitionLine)
    {
        var references = new List<SymbolLocation>();
        var pattern = $@"(?<![a-zA-Z0-9_]){Regex.Escape(constantName)}(?![a-zA-Z0-9_])";

        for (var i = 0; i < lines.Length; i++)
        {
            // Skip the definition line
            if (i + 1 == definitionLine)
                continue;

            var line = lines[i];

            // Skip if this line is a Const declaration for this constant
            if (Regex.IsMatch(line, $@"Const\s+{Regex.Escape(constantName)}\s*(?:As|=)", RegexOptions.IgnoreCase))
                continue;

            var matches = Regex.Matches(line, pattern, RegexOptions.IgnoreCase);

            foreach (Match match in matches)
            {
                // Skip if inside a comment
                var commentIndex = line.IndexOf('\'');
                if (commentIndex >= 0 && match.Index >= commentIndex)
                    continue;

                // Skip if inside a string literal
                if (IsInsideString(line, match.Index))
                    continue;

                references.Add(new SymbolLocation
                {
                    FilePath = filePath,
                    Line = i + 1,
                    Column = match.Index + 1,
                    EndLine = i + 1,
                    EndColumn = match.Index + match.Length + 1,
                    Text = constantName,
                    Type = SymbolLocationType.Reference
                });
            }
        }

        return references;
    }

    private bool IsInsideString(string line, int position)
    {
        var inString = false;
        for (var i = 0; i < position && i < line.Length; i++)
        {
            if (line[i] == '"')
            {
                // Check for escaped quote
                if (i + 1 < line.Length && line[i + 1] == '"')
                {
                    i++; // Skip the escaped quote
                }
                else
                {
                    inString = !inString;
                }
            }
        }
        return inString;
    }

    #endregion

    #region Move Type To File

    public async Task<TypeDefinitionInfo?> GetTypeInfoAsync(string filePath, int line, int column, CancellationToken cancellationToken = default)
    {
        try
        {
            var content = await _fileService.ReadFileAsync(filePath, cancellationToken);
            var lines = content.Split('\n');

            if (line < 1 || line > lines.Length)
                return null;

            var lineText = lines[line - 1];
            var symbol = ExtractSymbolAtPosition(lineText, column);

            if (string.IsNullOrEmpty(symbol))
            {
                // Try to find type definition on the current line
                var typeOnLine = ParseTypeDefinitionFromLine(lineText);
                if (typeOnLine != null)
                    symbol = typeOnLine;
            }

            if (string.IsNullOrEmpty(symbol))
                return null;

            // Find the type definition
            return FindTypeDefinition(lines, symbol, filePath);
        }
        catch
        {
            return null;
        }
    }

    private string? ParseTypeDefinitionFromLine(string lineText)
    {
        var trimmed = lineText.TrimStart();

        // Pattern: [Access] [Partial] (Class|Module|Interface|Enum|Structure) Name
        var patterns = new[]
        {
            @"(?:Public\s+|Private\s+|Protected\s+|Friend\s+)?(?:Partial\s+)?Class\s+(\w+)",
            @"(?:Public\s+|Private\s+|Protected\s+|Friend\s+)?Module\s+(\w+)",
            @"(?:Public\s+|Private\s+|Protected\s+|Friend\s+)?(?:Partial\s+)?Interface\s+(\w+)",
            @"(?:Public\s+|Private\s+|Protected\s+|Friend\s+)?Enum\s+(\w+)",
            @"(?:Public\s+|Private\s+|Protected\s+|Friend\s+)?(?:Partial\s+)?Structure\s+(\w+)"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(trimmed, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }

        return null;
    }

    private TypeDefinitionInfo? FindTypeDefinition(string[] lines, string typeName, string filePath)
    {
        // Pattern to match type definitions
        var classPattern = new Regex(
            $@"^\s*(Public\s+|Private\s+|Protected\s+|Friend\s+)?(Partial\s+)?Class\s+{Regex.Escape(typeName)}\b",
            RegexOptions.IgnoreCase);
        var modulePattern = new Regex(
            $@"^\s*(Public\s+|Private\s+|Protected\s+|Friend\s+)?Module\s+{Regex.Escape(typeName)}\b",
            RegexOptions.IgnoreCase);
        var interfacePattern = new Regex(
            $@"^\s*(Public\s+|Private\s+|Protected\s+|Friend\s+)?(Partial\s+)?Interface\s+{Regex.Escape(typeName)}\b",
            RegexOptions.IgnoreCase);
        var enumPattern = new Regex(
            $@"^\s*(Public\s+|Private\s+|Protected\s+|Friend\s+)?Enum\s+{Regex.Escape(typeName)}\b",
            RegexOptions.IgnoreCase);
        var structPattern = new Regex(
            $@"^\s*(Public\s+|Private\s+|Protected\s+|Friend\s+)?(Partial\s+)?Structure\s+{Regex.Escape(typeName)}\b",
            RegexOptions.IgnoreCase);

        for (var i = 0; i < lines.Length; i++)
        {
            var lineText = lines[i];
            TypeDefinitionKind? kind = null;
            Match? match = null;

            if (classPattern.IsMatch(lineText))
            {
                kind = TypeDefinitionKind.Class;
                match = classPattern.Match(lineText);
            }
            else if (modulePattern.IsMatch(lineText))
            {
                kind = TypeDefinitionKind.Module;
                match = modulePattern.Match(lineText);
            }
            else if (interfacePattern.IsMatch(lineText))
            {
                kind = TypeDefinitionKind.Interface;
                match = interfacePattern.Match(lineText);
            }
            else if (enumPattern.IsMatch(lineText))
            {
                kind = TypeDefinitionKind.Enum;
                match = enumPattern.Match(lineText);
            }
            else if (structPattern.IsMatch(lineText))
            {
                kind = TypeDefinitionKind.Structure;
                match = structPattern.Match(lineText);
            }

            if (kind.HasValue && match != null)
            {
                // Find the end of the type
                var endLine = FindEndOfType(lines, i, kind.Value);

                // Extract the full definition
                var definitionLines = new List<string>();
                for (var j = i; j <= endLine && j < lines.Length; j++)
                {
                    definitionLines.Add(lines[j]);
                }

                // Collect imports from the beginning of the file
                var imports = CollectImports(lines);

                // Determine namespace if present
                var namespaceName = FindNamespace(lines, i);

                // Determine accessibility
                var accessibility = ExtractTypeAccessibility(lineText);

                // Check if partial
                var isPartial = lineText.Contains("Partial", StringComparison.OrdinalIgnoreCase);

                return new TypeDefinitionInfo
                {
                    Name = typeName,
                    FilePath = filePath,
                    StartLine = i + 1,
                    EndLine = endLine + 1,
                    Kind = kind.Value,
                    Namespace = namespaceName,
                    Accessibility = accessibility,
                    IsPartial = isPartial,
                    FullDefinition = string.Join("\n", definitionLines),
                    Imports = imports,
                    SuggestedFileName = typeName + ".bas"
                };
            }
        }

        return null;
    }

    private int FindEndOfType(string[] lines, int startLine, TypeDefinitionKind kind)
    {
        var endKeyword = kind switch
        {
            TypeDefinitionKind.Class => "End Class",
            TypeDefinitionKind.Module => "End Module",
            TypeDefinitionKind.Interface => "End Interface",
            TypeDefinitionKind.Enum => "End Enum",
            TypeDefinitionKind.Structure => "End Structure",
            _ => "End Class"
        };

        var depth = 1;
        var startKeyword = kind.ToString();

        for (var i = startLine + 1; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();

            // Check for nested types (increase depth)
            if (Regex.IsMatch(trimmed, @"^(Public\s+|Private\s+|Protected\s+|Friend\s+)?(Partial\s+)?(Class|Module|Interface|Enum|Structure)\s+\w+", RegexOptions.IgnoreCase))
            {
                depth++;
            }
            // Check for end of type
            else if (trimmed.StartsWith(endKeyword, StringComparison.OrdinalIgnoreCase))
            {
                depth--;
                if (depth == 0)
                    return i;
            }
            else if (Regex.IsMatch(trimmed, @"^End\s+(Class|Module|Interface|Enum|Structure)\b", RegexOptions.IgnoreCase))
            {
                depth--;
                if (depth == 0)
                    return i;
            }
        }

        return lines.Length - 1;
    }

    private List<string> CollectImports(string[] lines)
    {
        var imports = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("Imports ", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Using ", StringComparison.OrdinalIgnoreCase))
            {
                imports.Add(line.TrimEnd('\r', '\n'));
            }
            else if (!string.IsNullOrWhiteSpace(trimmed) &&
                     !trimmed.StartsWith("'") &&
                     !trimmed.StartsWith("Namespace", StringComparison.OrdinalIgnoreCase))
            {
                // Stop when we hit actual code (not comments or namespace)
                // unless we haven't found anything yet
                if (imports.Count > 0 || !trimmed.StartsWith("Namespace", StringComparison.OrdinalIgnoreCase))
                {
                    // Check if this is a type definition - if so, break
                    if (Regex.IsMatch(trimmed, @"^(Public\s+|Private\s+|Protected\s+|Friend\s+)?(Partial\s+)?(Class|Module|Interface|Enum|Structure)\s+\w+", RegexOptions.IgnoreCase))
                        break;
                }
            }
        }

        return imports;
    }

    private string? FindNamespace(string[] lines, int typeStartLine)
    {
        // Search backwards from the type to find a Namespace declaration
        for (var i = typeStartLine - 1; i >= 0; i--)
        {
            var trimmed = lines[i].TrimStart();
            var match = Regex.Match(trimmed, @"^Namespace\s+(\S+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }
        return null;
    }

    private TypeAccessibility ExtractTypeAccessibility(string lineText)
    {
        var trimmed = lineText.TrimStart().ToLowerInvariant();

        if (trimmed.StartsWith("public "))
            return TypeAccessibility.Public;
        if (trimmed.StartsWith("private "))
            return TypeAccessibility.Private;
        if (trimmed.StartsWith("protected "))
            return TypeAccessibility.Protected;
        if (trimmed.StartsWith("friend "))
            return TypeAccessibility.Friend;

        return TypeAccessibility.NotSpecified;
    }

    public async Task<MoveTypeToFileResult> MoveTypeToFileAsync(string filePath, int line, int column, MoveTypeToFileOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            // Get type info
            var typeInfo = await GetTypeInfoAsync(filePath, line, column, cancellationToken);
            if (typeInfo == null)
            {
                return new MoveTypeToFileResult { Success = false, ErrorMessage = "Could not find type definition at the specified location" };
            }

            // Validate the new file name
            if (string.IsNullOrWhiteSpace(options.NewFileName))
            {
                return new MoveTypeToFileResult { Success = false, ErrorMessage = "New file name is required" };
            }

            // Determine target directory
            var sourceDir = Path.GetDirectoryName(filePath) ?? "";
            var targetDir = string.IsNullOrEmpty(options.TargetDirectory) ? sourceDir : options.TargetDirectory;

            // Create full path for new file
            var newFilePath = Path.Combine(targetDir, options.NewFileName);

            // Check if file already exists
            if (File.Exists(newFilePath))
            {
                return new MoveTypeToFileResult { Success = false, ErrorMessage = $"File '{options.NewFileName}' already exists" };
            }

            // Build new file content
            var newFileContent = new System.Text.StringBuilder();

            // Add imports if requested
            if (options.IncludeImports && typeInfo.Imports.Count > 0)
            {
                foreach (var import in typeInfo.Imports)
                {
                    newFileContent.AppendLine(import);
                }
                newFileContent.AppendLine();
            }

            // Add namespace if present
            if (!string.IsNullOrEmpty(typeInfo.Namespace))
            {
                newFileContent.AppendLine($"Namespace {typeInfo.Namespace}");
                newFileContent.AppendLine();
            }

            // Add the type definition
            newFileContent.Append(typeInfo.FullDefinition);

            // Close namespace if present
            if (!string.IsNullOrEmpty(typeInfo.Namespace))
            {
                newFileContent.AppendLine();
                newFileContent.AppendLine();
                newFileContent.AppendLine("End Namespace");
            }

            // Create edit to remove type from original file if requested
            FileEdit? originalFileEdit = null;
            if (options.RemoveFromOriginalFile)
            {
                var content = await _fileService.ReadFileAsync(filePath, cancellationToken);
                var lines = content.Split('\n');

                // Find the range to remove (include any blank lines before the type)
                var startRemoveLine = typeInfo.StartLine;
                var endRemoveLine = typeInfo.EndLine;

                // Look for blank lines before the type
                while (startRemoveLine > 1 && string.IsNullOrWhiteSpace(lines[startRemoveLine - 2]))
                {
                    startRemoveLine--;
                }

                // Look for blank lines after the type
                while (endRemoveLine < lines.Length && string.IsNullOrWhiteSpace(lines[endRemoveLine]))
                {
                    endRemoveLine++;
                }

                originalFileEdit = new FileEdit
                {
                    FilePath = filePath,
                    Edits = new List<TextEdit>
                    {
                        new TextEdit
                        {
                            StartLine = startRemoveLine,
                            StartColumn = 1,
                            EndLine = endRemoveLine,
                            EndColumn = endRemoveLine <= lines.Length ? lines[endRemoveLine - 1].Length + 1 : 1,
                            NewText = ""
                        }
                    }
                };
            }

            return new MoveTypeToFileResult
            {
                Success = true,
                NewFilePath = newFilePath,
                OriginalFileEdit = originalFileEdit,
                NewFileContent = newFileContent.ToString()
            };
        }
        catch (Exception ex)
        {
            return new MoveTypeToFileResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    #endregion

    #region Extract Interface

    public async Task<ClassMemberInfo?> GetClassMembersAsync(string filePath, int line, int column, CancellationToken cancellationToken = default)
    {
        try
        {
            var content = await _fileService.ReadFileAsync(filePath, cancellationToken);
            var lines = content.Split('\n');

            if (line < 1 || line > lines.Length)
                return null;

            // Find the class at this position
            var classInfo = FindClassAtPosition(lines, line, filePath);
            if (classInfo == null)
                return null;

            var info = classInfo.Value;

            // Extract all public members from the class
            var members = ExtractClassMembers(lines, info.StartLine, info.EndLine);

            // Find existing implements clause
            var existingInterfaces = FindImplementedInterfaces(lines, info.StartLine);

            // Find namespace
            var ns = FindNamespace(lines, info.StartLine - 1);

            return new ClassMemberInfo
            {
                ClassName = info.Name,
                FilePath = filePath,
                StartLine = info.StartLine,
                EndLine = info.EndLine,
                Namespace = ns,
                Accessibility = info.Accessibility,
                Members = members,
                ExistingInterfaces = existingInterfaces
            };
        }
        catch
        {
            return null;
        }
    }

    private (string Name, int StartLine, int EndLine, TypeAccessibility Accessibility)? FindClassAtPosition(string[] lines, int targetLine, string filePath)
    {
        // Find the class containing the target line
        var classPattern = new Regex(@"^\s*(Public\s+|Private\s+|Protected\s+|Friend\s+)?(Partial\s+)?Class\s+(\w+)", RegexOptions.IgnoreCase);

        for (var i = 0; i < lines.Length; i++)
        {
            var match = classPattern.Match(lines[i]);
            if (match.Success)
            {
                var startLine = i + 1;
                var endLine = FindEndOfType(lines, i, TypeDefinitionKind.Class) + 1;

                // Check if target line is within this class
                if (targetLine >= startLine && targetLine <= endLine)
                {
                    var accessibility = ExtractTypeAccessibility(lines[i]);
                    return (match.Groups[3].Value, startLine, endLine, accessibility);
                }
            }
        }

        return null;
    }

    private List<ExtractableMember> ExtractClassMembers(string[] lines, int startLine, int endLine)
    {
        var members = new List<ExtractableMember>();

        // Patterns for different member types
        var subPattern = new Regex(@"^\s*(Public\s+|Private\s+|Protected\s+|Friend\s+)?(Shared\s+)?(Overridable\s+|Overrides\s+)?Sub\s+(\w+)\s*\(([^)]*)\)", RegexOptions.IgnoreCase);
        var functionPattern = new Regex(@"^\s*(Public\s+|Private\s+|Protected\s+|Friend\s+)?(Shared\s+)?(Overridable\s+|Overrides\s+)?Function\s+(\w+)\s*\(([^)]*)\)\s*(?:As\s+(\w+))?", RegexOptions.IgnoreCase);
        var propertyPattern = new Regex(@"^\s*(Public\s+|Private\s+|Protected\s+|Friend\s+)?(Shared\s+)?(ReadOnly\s+|WriteOnly\s+)?Property\s+(\w+)(?:\s*\(([^)]*)\))?\s*(?:As\s+(\w+))?", RegexOptions.IgnoreCase);
        var eventPattern = new Regex(@"^\s*(Public\s+|Private\s+|Protected\s+|Friend\s+)?Event\s+(\w+)\s*\(([^)]*)\)", RegexOptions.IgnoreCase);

        for (var i = startLine - 1; i < endLine - 1 && i < lines.Length; i++)
        {
            var lineText = lines[i];

            // Skip nested types
            if (Regex.IsMatch(lineText, @"^\s*(Public\s+|Private\s+|Protected\s+|Friend\s+)?(Partial\s+)?(Class|Module|Interface|Enum|Structure)\s+\w+", RegexOptions.IgnoreCase))
            {
                // Skip to end of nested type
                var nestedKind = GetTypeKindFromLine(lineText);
                if (nestedKind.HasValue)
                {
                    i = FindEndOfType(lines, i, nestedKind.Value);
                }
                continue;
            }

            // Check Sub
            var subMatch = subPattern.Match(lineText);
            if (subMatch.Success)
            {
                var accessibility = GetMemberAccessibility(subMatch.Groups[1].Value);
                if (accessibility == MemberAccessibility.Public)
                {
                    var name = subMatch.Groups[4].Value;
                    var parameters = subMatch.Groups[5].Value;

                    members.Add(new ExtractableMember
                    {
                        Name = name,
                        Kind = ExtractableMemberKind.Sub,
                        Signature = $"Sub {name}({parameters})",
                        ReturnType = null,
                        Parameters = ParseParameterNames(parameters),
                        Accessibility = accessibility,
                        IsShared = !string.IsNullOrEmpty(subMatch.Groups[2].Value),
                        IsOverridable = !string.IsNullOrEmpty(subMatch.Groups[3].Value),
                        Line = i + 1,
                        IsSelected = true
                    });
                }
                continue;
            }

            // Check Function
            var funcMatch = functionPattern.Match(lineText);
            if (funcMatch.Success)
            {
                var accessibility = GetMemberAccessibility(funcMatch.Groups[1].Value);
                if (accessibility == MemberAccessibility.Public)
                {
                    var name = funcMatch.Groups[4].Value;
                    var parameters = funcMatch.Groups[5].Value;
                    var returnType = funcMatch.Groups[6].Value;

                    members.Add(new ExtractableMember
                    {
                        Name = name,
                        Kind = ExtractableMemberKind.Function,
                        Signature = $"Function {name}({parameters}) As {(string.IsNullOrEmpty(returnType) ? "Object" : returnType)}",
                        ReturnType = string.IsNullOrEmpty(returnType) ? "Object" : returnType,
                        Parameters = ParseParameterNames(parameters),
                        Accessibility = accessibility,
                        IsShared = !string.IsNullOrEmpty(funcMatch.Groups[2].Value),
                        IsOverridable = !string.IsNullOrEmpty(funcMatch.Groups[3].Value),
                        Line = i + 1,
                        IsSelected = true
                    });
                }
                continue;
            }

            // Check Property
            var propMatch = propertyPattern.Match(lineText);
            if (propMatch.Success)
            {
                var accessibility = GetMemberAccessibility(propMatch.Groups[1].Value);
                if (accessibility == MemberAccessibility.Public)
                {
                    var name = propMatch.Groups[4].Value;
                    var parameters = propMatch.Groups[5].Value ?? "";
                    var returnType = propMatch.Groups[6].Value;
                    var isReadOnly = lineText.Contains("ReadOnly", StringComparison.OrdinalIgnoreCase);
                    var isWriteOnly = lineText.Contains("WriteOnly", StringComparison.OrdinalIgnoreCase);

                    var signature = isReadOnly ? "ReadOnly Property" : (isWriteOnly ? "WriteOnly Property" : "Property");
                    signature += $" {name}";
                    if (!string.IsNullOrEmpty(parameters))
                        signature += $"({parameters})";
                    if (!string.IsNullOrEmpty(returnType))
                        signature += $" As {returnType}";

                    members.Add(new ExtractableMember
                    {
                        Name = name,
                        Kind = ExtractableMemberKind.Property,
                        Signature = signature,
                        ReturnType = string.IsNullOrEmpty(returnType) ? "Object" : returnType,
                        Parameters = ParseParameterNames(parameters),
                        Accessibility = accessibility,
                        IsShared = !string.IsNullOrEmpty(propMatch.Groups[2].Value),
                        IsOverridable = false,
                        Line = i + 1,
                        IsSelected = true
                    });
                }
                continue;
            }

            // Check Event
            var eventMatch = eventPattern.Match(lineText);
            if (eventMatch.Success)
            {
                var accessibility = GetMemberAccessibility(eventMatch.Groups[1].Value);
                if (accessibility == MemberAccessibility.Public)
                {
                    var name = eventMatch.Groups[2].Value;
                    var parameters = eventMatch.Groups[3].Value;

                    members.Add(new ExtractableMember
                    {
                        Name = name,
                        Kind = ExtractableMemberKind.Event,
                        Signature = $"Event {name}({parameters})",
                        ReturnType = null,
                        Parameters = ParseParameterNames(parameters),
                        Accessibility = accessibility,
                        IsShared = false,
                        IsOverridable = false,
                        Line = i + 1,
                        IsSelected = true
                    });
                }
            }
        }

        return members;
    }

    private TypeDefinitionKind? GetTypeKindFromLine(string lineText)
    {
        var trimmed = lineText.TrimStart().ToLowerInvariant();
        if (Regex.IsMatch(trimmed, @"^(public\s+|private\s+|protected\s+|friend\s+)?(partial\s+)?class\s+"))
            return TypeDefinitionKind.Class;
        if (Regex.IsMatch(trimmed, @"^(public\s+|private\s+|protected\s+|friend\s+)?module\s+"))
            return TypeDefinitionKind.Module;
        if (Regex.IsMatch(trimmed, @"^(public\s+|private\s+|protected\s+|friend\s+)?(partial\s+)?interface\s+"))
            return TypeDefinitionKind.Interface;
        if (Regex.IsMatch(trimmed, @"^(public\s+|private\s+|protected\s+|friend\s+)?enum\s+"))
            return TypeDefinitionKind.Enum;
        if (Regex.IsMatch(trimmed, @"^(public\s+|private\s+|protected\s+|friend\s+)?(partial\s+)?structure\s+"))
            return TypeDefinitionKind.Structure;
        return null;
    }

    private MemberAccessibility GetMemberAccessibility(string accessibilityText)
    {
        var trimmed = accessibilityText.Trim().ToLowerInvariant();
        return trimmed switch
        {
            "public" or "public " => MemberAccessibility.Public,
            "private" or "private " => MemberAccessibility.Private,
            "protected" or "protected " => MemberAccessibility.Protected,
            "friend" or "friend " => MemberAccessibility.Friend,
            _ => MemberAccessibility.Public // Default to public if not specified
        };
    }

    private List<string> ParseParameterNames(string parameters)
    {
        var names = new List<string>();
        if (string.IsNullOrWhiteSpace(parameters))
            return names;

        var parts = parameters.Split(',');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            // Extract parameter name (format: [ByVal|ByRef] name As Type [= default])
            var match = Regex.Match(trimmed, @"(?:ByVal\s+|ByRef\s+)?(\w+)\s*(?:As\s+|\=)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                names.Add(match.Groups[1].Value);
            }
            else
            {
                // Try simpler format
                match = Regex.Match(trimmed, @"(?:ByVal\s+|ByRef\s+)?(\w+)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    names.Add(match.Groups[1].Value);
                }
            }
        }

        return names;
    }

    private List<string> FindImplementedInterfaces(string[] lines, int classStartLine)
    {
        var interfaces = new List<string>();

        // Check the class declaration line and following lines for Implements clause
        for (var i = classStartLine - 1; i < Math.Min(classStartLine + 5, lines.Length); i++)
        {
            var match = Regex.Match(lines[i], @"Implements\s+(.+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var interfaceList = match.Groups[1].Value;
                var parts = interfaceList.Split(',');
                foreach (var part in parts)
                {
                    var trimmed = part.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                    {
                        interfaces.Add(trimmed);
                    }
                }
            }
        }

        return interfaces;
    }

    public async Task<ExtractInterfaceResult> ExtractInterfaceAsync(string filePath, int line, int column, ExtractInterfaceOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            // Get class member info
            var classInfo = await GetClassMembersAsync(filePath, line, column, cancellationToken);
            if (classInfo == null)
            {
                return new ExtractInterfaceResult { Success = false, ErrorMessage = "Could not find class at the specified location" };
            }

            // Validate interface name
            if (string.IsNullOrWhiteSpace(options.InterfaceName))
            {
                return new ExtractInterfaceResult { Success = false, ErrorMessage = "Interface name is required" };
            }

            // Get selected members
            var selectedMembers = classInfo.Members
                .Where(m => options.SelectedMembers.Contains(m.Name))
                .ToList();

            if (selectedMembers.Count == 0)
            {
                return new ExtractInterfaceResult { Success = false, ErrorMessage = "No members selected for interface" };
            }

            // Generate interface code
            var interfaceCode = GenerateInterfaceCode(options.InterfaceName, selectedMembers, options.Accessibility, classInfo.Namespace);

            // Read original file
            var content = await _fileService.ReadFileAsync(filePath, cancellationToken);
            var lines = content.Split('\n');

            FileEdit? originalFileEdit = null;
            string? newFilePath = null;
            string? newFileContent = null;

            if (options.CreateInSameFile)
            {
                // Insert interface before the class
                var insertLine = classInfo.StartLine;

                // Find a good insertion point (before the class, after any imports/namespace)
                var edits = new List<TextEdit>();

                // Add interface before class
                edits.Add(new TextEdit
                {
                    StartLine = insertLine,
                    StartColumn = 1,
                    EndLine = insertLine,
                    EndColumn = 1,
                    NewText = interfaceCode + "\n\n"
                });

                // Add Implements clause if requested
                if (options.ImplementInterface)
                {
                    var classLineIndex = classInfo.StartLine - 1;
                    var classLine = lines[classLineIndex];

                    // Check if there's already an Implements clause
                    var hasImplements = classInfo.ExistingInterfaces.Count > 0;

                    if (hasImplements)
                    {
                        // Find the Implements line and add to it
                        for (var i = classLineIndex; i < Math.Min(classLineIndex + 5, lines.Length); i++)
                        {
                            if (lines[i].Contains("Implements", StringComparison.OrdinalIgnoreCase))
                            {
                                var implementsLine = lines[i].TrimEnd('\r', '\n');
                                edits.Add(new TextEdit
                                {
                                    StartLine = i + 1,
                                    StartColumn = 1,
                                    EndLine = i + 1,
                                    EndColumn = implementsLine.Length + 1,
                                    NewText = implementsLine + ", " + options.InterfaceName
                                });
                                break;
                            }
                        }
                    }
                    else
                    {
                        // Add Implements clause after class declaration
                        var classLineText = classLine.TrimEnd('\r', '\n');
                        edits.Add(new TextEdit
                        {
                            StartLine = classInfo.StartLine,
                            StartColumn = 1,
                            EndLine = classInfo.StartLine,
                            EndColumn = classLineText.Length + 1,
                            NewText = classLineText + "\n    Implements " + options.InterfaceName
                        });
                    }
                }

                // Sort edits in reverse order for proper application
                edits = edits.OrderByDescending(e => e.StartLine).ThenByDescending(e => e.StartColumn).ToList();

                originalFileEdit = new FileEdit
                {
                    FilePath = filePath,
                    Edits = edits
                };
            }
            else
            {
                // Create interface in new file
                var sourceDir = Path.GetDirectoryName(filePath) ?? "";
                var fileName = options.FileName ?? (options.InterfaceName + ".bas");
                newFilePath = Path.Combine(sourceDir, fileName);

                // Build new file content
                var sb = new System.Text.StringBuilder();

                // Add imports from original file
                var imports = CollectImports(lines);
                foreach (var import in imports)
                {
                    sb.AppendLine(import);
                }
                if (imports.Count > 0)
                    sb.AppendLine();

                // Add namespace if present
                if (!string.IsNullOrEmpty(classInfo.Namespace))
                {
                    sb.AppendLine($"Namespace {classInfo.Namespace}");
                    sb.AppendLine();
                }

                // Add interface (indented if in namespace)
                var indent = !string.IsNullOrEmpty(classInfo.Namespace) ? "    " : "";
                var interfaceLines = interfaceCode.Split('\n');
                foreach (var interfaceLine in interfaceLines)
                {
                    sb.Append(indent);
                    sb.AppendLine(interfaceLine.TrimEnd('\r'));
                }

                // Close namespace
                if (!string.IsNullOrEmpty(classInfo.Namespace))
                {
                    sb.AppendLine();
                    sb.AppendLine("End Namespace");
                }

                newFileContent = sb.ToString();

                // Add Implements clause to original class if requested
                if (options.ImplementInterface)
                {
                    var edits = new List<TextEdit>();
                    var classLineIndex = classInfo.StartLine - 1;

                    var hasImplements = classInfo.ExistingInterfaces.Count > 0;

                    if (hasImplements)
                    {
                        // Find the Implements line and add to it
                        for (var i = classLineIndex; i < Math.Min(classLineIndex + 5, lines.Length); i++)
                        {
                            if (lines[i].Contains("Implements", StringComparison.OrdinalIgnoreCase))
                            {
                                var implementsLine = lines[i].TrimEnd('\r', '\n');
                                edits.Add(new TextEdit
                                {
                                    StartLine = i + 1,
                                    StartColumn = 1,
                                    EndLine = i + 1,
                                    EndColumn = implementsLine.Length + 1,
                                    NewText = implementsLine + ", " + options.InterfaceName
                                });
                                break;
                            }
                        }
                    }
                    else
                    {
                        // Add Implements clause after class declaration
                        var classLine = lines[classLineIndex].TrimEnd('\r', '\n');
                        edits.Add(new TextEdit
                        {
                            StartLine = classInfo.StartLine,
                            StartColumn = 1,
                            EndLine = classInfo.StartLine,
                            EndColumn = classLine.Length + 1,
                            NewText = classLine + "\n    Implements " + options.InterfaceName
                        });
                    }

                    if (edits.Count > 0)
                    {
                        originalFileEdit = new FileEdit
                        {
                            FilePath = filePath,
                            Edits = edits
                        };
                    }
                }
            }

            // Apply edits to original file
            if (originalFileEdit != null)
            {
                var fileContent = await _fileService.ReadFileAsync(originalFileEdit.FilePath, cancellationToken);
                var fileLines = fileContent.Split('\n').ToList();

                // Apply edits in reverse order to preserve line numbers
                var sortedEdits = originalFileEdit.Edits.OrderByDescending(e => e.StartLine).ThenByDescending(e => e.StartColumn).ToList();

                foreach (var edit in sortedEdits)
                {
                    if (edit.StartLine == edit.EndLine)
                    {
                        // Single line edit
                        var lineIndex = edit.StartLine - 1;
                        if (lineIndex >= 0 && lineIndex < fileLines.Count)
                        {
                            var currentLine = fileLines[lineIndex];
                            var before = edit.StartColumn > 1 ? currentLine.Substring(0, edit.StartColumn - 1) : "";
                            var after = edit.EndColumn <= currentLine.Length ? currentLine.Substring(edit.EndColumn - 1) : "";
                            fileLines[lineIndex] = before + edit.NewText + after;
                        }
                    }
                    else
                    {
                        // Multi-line edit - replace entire range
                        var startIndex = edit.StartLine - 1;
                        var endIndex = edit.EndLine - 1;

                        if (startIndex >= 0 && endIndex < fileLines.Count)
                        {
                            // Remove the lines in the range
                            for (var i = endIndex; i >= startIndex; i--)
                            {
                                fileLines.RemoveAt(i);
                            }
                            // Insert new text
                            var newLines = edit.NewText.Split('\n');
                            for (var i = 0; i < newLines.Length; i++)
                            {
                                fileLines.Insert(startIndex + i, newLines[i]);
                            }
                        }
                    }
                }

                await _fileService.WriteFileAsync(originalFileEdit.FilePath, string.Join("\n", fileLines), cancellationToken);
            }

            // Write new file if created
            if (!string.IsNullOrEmpty(newFilePath) && !string.IsNullOrEmpty(newFileContent))
            {
                await _fileService.WriteFileAsync(newFilePath, newFileContent, cancellationToken);
            }

            return new ExtractInterfaceResult
            {
                Success = true,
                InterfaceName = options.InterfaceName,
                NewFilePath = newFilePath,
                NewFileContent = newFileContent,
                OriginalFileEdit = originalFileEdit,
                MembersExtracted = selectedMembers.Count
            };
        }
        catch (Exception ex)
        {
            return new ExtractInterfaceResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    private string GenerateInterfaceCode(string interfaceName, List<ExtractableMember> members, InterfaceAccessibility accessibility, string? namespaceName)
    {
        var sb = new System.Text.StringBuilder();
        var accessStr = accessibility == InterfaceAccessibility.Public ? "Public" : "Friend";

        sb.AppendLine($"{accessStr} Interface {interfaceName}");

        foreach (var member in members)
        {
            switch (member.Kind)
            {
                case ExtractableMemberKind.Sub:
                    sb.AppendLine($"    Sub {member.Name}({string.Join(", ", GetInterfaceParameters(member))})");
                    break;

                case ExtractableMemberKind.Function:
                    sb.AppendLine($"    Function {member.Name}({string.Join(", ", GetInterfaceParameters(member))}) As {member.ReturnType ?? "Object"}");
                    break;

                case ExtractableMemberKind.Property:
                    var propSignature = member.Signature;
                    // Remove accessibility modifiers for interface
                    propSignature = Regex.Replace(propSignature, @"^(Public\s+|Private\s+|Protected\s+|Friend\s+)", "", RegexOptions.IgnoreCase);
                    sb.AppendLine($"    {propSignature}");
                    break;

                case ExtractableMemberKind.Event:
                    sb.AppendLine($"    Event {member.Name}({string.Join(", ", GetInterfaceParameters(member))})");
                    break;
            }
        }

        sb.Append("End Interface");

        return sb.ToString();
    }

    private List<string> GetInterfaceParameters(ExtractableMember member)
    {
        // For interface, we need to parse the signature to get full parameter declarations
        var signatureMatch = Regex.Match(member.Signature, @"\(([^)]*)\)", RegexOptions.IgnoreCase);
        if (signatureMatch.Success)
        {
            var paramsText = signatureMatch.Groups[1].Value;
            if (string.IsNullOrWhiteSpace(paramsText))
                return new List<string>();

            return paramsText.Split(',').Select(p => p.Trim()).ToList();
        }

        return new List<string>();
    }

    #endregion

    #region Generate Constructor

    public async Task<ClassFieldsInfo?> GetClassFieldsAsync(string filePath, int line, int column, CancellationToken cancellationToken = default)
    {
        try
        {
            var content = await _fileService.ReadFileAsync(filePath, cancellationToken);
            var lines = content.Split('\n');

            if (line < 1 || line > lines.Length)
                return null;

            // Find the class at this position
            var classInfo = FindClassAtPosition(lines, line, filePath);
            if (classInfo == null)
                return null;

            var info = classInfo.Value;

            // Extract fields and properties from the class
            var fields = ExtractClassFields(lines, info.StartLine, info.EndLine);
            var properties = ExtractClassProperties(lines, info.StartLine, info.EndLine);

            // Find existing constructors
            var existingConstructors = FindExistingConstructors(lines, info.StartLine, info.EndLine);

            // Find namespace
            var ns = FindNamespace(lines, info.StartLine - 1);

            // Find insertion point (after fields/properties, before first method)
            var insertionLine = FindConstructorInsertionPoint(lines, info.StartLine, info.EndLine, fields, properties);

            return new ClassFieldsInfo
            {
                ClassName = info.Name,
                FilePath = filePath,
                ClassStartLine = info.StartLine,
                ClassEndLine = info.EndLine,
                Namespace = ns,
                Fields = fields,
                Properties = properties,
                ExistingConstructors = existingConstructors,
                InsertionLine = insertionLine
            };
        }
        catch
        {
            return null;
        }
    }

    private List<ConstructorFieldInfo> ExtractClassFields(string[] lines, int startLine, int endLine)
    {
        var fields = new List<ConstructorFieldInfo>();

        // Pattern for field declarations: [Access] [Shared] [ReadOnly] Dim/field As Type [= value]
        var fieldPattern = new Regex(
            @"^\s*(Public\s+|Private\s+|Protected\s+|Friend\s+)?(Shared\s+)?(ReadOnly\s+)?(?:Dim\s+)?(\w+)\s+As\s+(\w+)(\s*=\s*.+)?$",
            RegexOptions.IgnoreCase);

        for (var i = startLine - 1; i < endLine - 1 && i < lines.Length; i++)
        {
            var lineText = lines[i];

            // Skip nested types
            if (Regex.IsMatch(lineText, @"^\s*(Public\s+|Private\s+|Protected\s+|Friend\s+)?(Partial\s+)?(Class|Module|Interface|Enum|Structure)\s+\w+", RegexOptions.IgnoreCase))
            {
                var nestedKind = GetTypeKindFromLine(lineText);
                if (nestedKind.HasValue)
                {
                    i = FindEndOfType(lines, i, nestedKind.Value);
                }
                continue;
            }

            // Skip methods, properties, etc.
            if (Regex.IsMatch(lineText, @"^\s*(Public\s+|Private\s+|Protected\s+|Friend\s+)?(Shared\s+)?(Overridable\s+|Overrides\s+)?(Sub|Function|Property)\s+", RegexOptions.IgnoreCase))
            {
                continue;
            }

            var match = fieldPattern.Match(lineText);
            if (match.Success)
            {
                var accessibility = GetFieldAccessibility(match.Groups[1].Value);
                var isShared = !string.IsNullOrEmpty(match.Groups[2].Value);
                var isReadOnly = !string.IsNullOrEmpty(match.Groups[3].Value);
                var fieldName = match.Groups[4].Value;
                var fieldType = match.Groups[5].Value;
                var hasInitializer = !string.IsNullOrEmpty(match.Groups[6].Value);

                // Skip shared fields - they shouldn't be in constructor
                if (isShared)
                    continue;

                fields.Add(new ConstructorFieldInfo
                {
                    Name = fieldName,
                    Type = fieldType,
                    IsReadOnly = isReadOnly,
                    IsShared = isShared,
                    Accessibility = accessibility,
                    Line = i + 1,
                    HasInitializer = hasInitializer,
                    ParameterName = GenerateParameterName(fieldName),
                    IsSelected = !hasInitializer // Don't select fields with initializers by default
                });
            }
        }

        return fields;
    }

    private List<ConstructorFieldInfo> ExtractClassProperties(string[] lines, int startLine, int endLine)
    {
        var properties = new List<ConstructorFieldInfo>();

        // Pattern for auto-implemented properties
        var autoPropertyPattern = new Regex(
            @"^\s*(Public\s+|Private\s+|Protected\s+|Friend\s+)?(Shared\s+)?(ReadOnly\s+)?Property\s+(\w+)\s+As\s+(\w+)(\s*=\s*.+)?$",
            RegexOptions.IgnoreCase);

        for (var i = startLine - 1; i < endLine - 1 && i < lines.Length; i++)
        {
            var lineText = lines[i];

            // Skip nested types
            if (Regex.IsMatch(lineText, @"^\s*(Public\s+|Private\s+|Protected\s+|Friend\s+)?(Partial\s+)?(Class|Module|Interface|Enum|Structure)\s+\w+", RegexOptions.IgnoreCase))
            {
                var nestedKind = GetTypeKindFromLine(lineText);
                if (nestedKind.HasValue)
                {
                    i = FindEndOfType(lines, i, nestedKind.Value);
                }
                continue;
            }

            var match = autoPropertyPattern.Match(lineText);
            if (match.Success)
            {
                var accessibility = GetFieldAccessibility(match.Groups[1].Value);
                var isShared = !string.IsNullOrEmpty(match.Groups[2].Value);
                var isReadOnly = !string.IsNullOrEmpty(match.Groups[3].Value);
                var propName = match.Groups[4].Value;
                var propType = match.Groups[5].Value;
                var hasInitializer = !string.IsNullOrEmpty(match.Groups[6].Value);

                // Skip shared properties
                if (isShared)
                    continue;

                // Only include public/protected properties (they make sense for constructor initialization)
                if (accessibility != FieldAccessibility.Public && accessibility != FieldAccessibility.Protected)
                    continue;

                properties.Add(new ConstructorFieldInfo
                {
                    Name = propName,
                    Type = propType,
                    IsReadOnly = isReadOnly,
                    IsShared = isShared,
                    Accessibility = accessibility,
                    Line = i + 1,
                    HasInitializer = hasInitializer,
                    ParameterName = GenerateParameterName(propName),
                    IsSelected = false // Properties are not selected by default
                });
            }
        }

        return properties;
    }

    private FieldAccessibility GetFieldAccessibility(string accessText)
    {
        var trimmed = accessText.Trim().ToLowerInvariant();
        return trimmed switch
        {
            "public" or "public " => FieldAccessibility.Public,
            "private" or "private " => FieldAccessibility.Private,
            "protected" or "protected " => FieldAccessibility.Protected,
            "friend" or "friend " => FieldAccessibility.Friend,
            _ => FieldAccessibility.Private
        };
    }

    private string GenerateParameterName(string fieldName)
    {
        // Remove common prefixes like _ or m_
        var name = fieldName;
        if (name.StartsWith("_"))
            name = name.Substring(1);
        else if (name.StartsWith("m_"))
            name = name.Substring(2);

        // Make first letter lowercase
        if (name.Length > 0)
            name = char.ToLowerInvariant(name[0]) + name.Substring(1);

        return name;
    }

    private List<ExistingConstructorInfo> FindExistingConstructors(string[] lines, int startLine, int endLine)
    {
        var constructors = new List<ExistingConstructorInfo>();

        // Pattern for Sub New
        var constructorPattern = new Regex(
            @"^\s*(Public\s+|Private\s+|Protected\s+|Friend\s+)?Sub\s+New\s*\(([^)]*)\)",
            RegexOptions.IgnoreCase);

        for (var i = startLine - 1; i < endLine - 1 && i < lines.Length; i++)
        {
            var match = constructorPattern.Match(lines[i]);
            if (match.Success)
            {
                var paramsText = match.Groups[2].Value;
                var paramTypes = new List<string>();

                if (!string.IsNullOrWhiteSpace(paramsText))
                {
                    var parts = paramsText.Split(',');
                    foreach (var part in parts)
                    {
                        // Extract type from parameter (format: [ByVal|ByRef] name As Type)
                        var typeMatch = Regex.Match(part, @"As\s+(\w+)", RegexOptions.IgnoreCase);
                        if (typeMatch.Success)
                        {
                            paramTypes.Add(typeMatch.Groups[1].Value);
                        }
                    }
                }

                constructors.Add(new ExistingConstructorInfo
                {
                    Line = i + 1,
                    ParameterTypes = paramTypes,
                    Signature = $"Sub New({paramsText.Trim()})"
                });
            }
        }

        return constructors;
    }

    private int FindConstructorInsertionPoint(string[] lines, int startLine, int endLine, List<ConstructorFieldInfo> fields, List<ConstructorFieldInfo> properties)
    {
        // Find the last field/property line
        var lastMemberLine = startLine;

        foreach (var field in fields)
        {
            if (field.Line > lastMemberLine)
                lastMemberLine = field.Line;
        }

        foreach (var prop in properties)
        {
            if (prop.Line > lastMemberLine)
                lastMemberLine = prop.Line;
        }

        // Look for the first Sub/Function after the last field
        for (var i = lastMemberLine; i < endLine - 1 && i < lines.Length; i++)
        {
            var lineText = lines[i];
            if (Regex.IsMatch(lineText, @"^\s*(Public\s+|Private\s+|Protected\s+|Friend\s+)?(Shared\s+)?(Overridable\s+|Overrides\s+)?Sub\s+", RegexOptions.IgnoreCase))
            {
                return i + 1; // Insert before this line
            }
        }

        // If no method found, insert after the last field with a blank line
        return lastMemberLine + 1;
    }

    public async Task<GenerateConstructorResult> GenerateConstructorAsync(string filePath, int line, int column, GenerateConstructorOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            // Get class fields info
            var classInfo = await GetClassFieldsAsync(filePath, line, column, cancellationToken);
            if (classInfo == null)
            {
                return new GenerateConstructorResult { Success = false, ErrorMessage = "Could not find class at the specified location" };
            }

            // Get selected fields and properties
            var selectedFields = classInfo.Fields
                .Where(f => options.SelectedFields.Contains(f.Name))
                .ToList();

            var selectedProperties = classInfo.Properties
                .Where(p => options.SelectedProperties.Contains(p.Name))
                .ToList();

            var allSelected = selectedFields.Concat(selectedProperties).ToList();

            if (allSelected.Count == 0)
            {
                return new GenerateConstructorResult { Success = false, ErrorMessage = "No fields or properties selected" };
            }

            // Check for duplicate constructor signature
            var newParamTypes = allSelected.Select(f => f.Type ?? "Object").ToList();
            foreach (var existing in classInfo.ExistingConstructors)
            {
                if (existing.ParameterTypes.Count == newParamTypes.Count)
                {
                    var match = true;
                    for (var i = 0; i < newParamTypes.Count; i++)
                    {
                        if (!string.Equals(existing.ParameterTypes[i], newParamTypes[i], StringComparison.OrdinalIgnoreCase))
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match)
                    {
                        return new GenerateConstructorResult
                        {
                            Success = false,
                            ErrorMessage = $"A constructor with the same signature already exists: {existing.Signature}"
                        };
                    }
                }
            }

            // Generate constructor code
            var constructorCode = GenerateConstructorCode(classInfo.ClassName, allSelected, options);

            // Read original file
            var content = await _fileService.ReadFileAsync(filePath, cancellationToken);
            var lines = content.Split('\n').ToList();

            // Insert the constructor
            var insertLine = classInfo.InsertionLine - 1;
            if (insertLine < 0) insertLine = 0;
            if (insertLine > lines.Count) insertLine = lines.Count;

            // Add blank line before if needed
            var constructorLines = constructorCode.Split('\n');
            for (var i = constructorLines.Length - 1; i >= 0; i--)
            {
                lines.Insert(insertLine, constructorLines[i]);
            }

            // Add blank line after constructor
            lines.Insert(insertLine + constructorLines.Length, "");

            // Write file
            await _fileService.WriteFileAsync(filePath, string.Join("\n", lines), cancellationToken);

            return new GenerateConstructorResult
            {
                Success = true,
                ParameterCount = allSelected.Count,
                GeneratedCode = constructorCode,
                FileEdit = new FileEdit
                {
                    FilePath = filePath,
                    Edits = new List<TextEdit>
                    {
                        new TextEdit
                        {
                            StartLine = classInfo.InsertionLine,
                            StartColumn = 1,
                            EndLine = classInfo.InsertionLine,
                            EndColumn = 1,
                            NewText = constructorCode + "\n\n"
                        }
                    }
                }
            };
        }
        catch (Exception ex)
        {
            return new GenerateConstructorResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    private string GenerateConstructorCode(string className, List<ConstructorFieldInfo> members, GenerateConstructorOptions options)
    {
        var sb = new System.Text.StringBuilder();

        // Determine accessibility
        var accessStr = options.Accessibility switch
        {
            ConstructorAccessibility.Public => "Public",
            ConstructorAccessibility.Private => "Private",
            ConstructorAccessibility.Protected => "Protected",
            ConstructorAccessibility.Friend => "Friend",
            _ => "Public"
        };

        // Build parameter list
        var parameters = new List<string>();
        foreach (var member in members)
        {
            var paramName = member.ParameterName ?? GenerateParameterName(member.Name);
            var paramType = member.Type ?? "Object";
            parameters.Add($"{paramName} As {paramType}");
        }

        sb.AppendLine($"    {accessStr} Sub New({string.Join(", ", parameters)})");

        // Add base constructor call if requested
        if (options.CallBaseConstructor)
        {
            sb.AppendLine("        MyBase.New()");
        }

        // Add null checks if requested
        if (options.GenerateNullChecks)
        {
            foreach (var member in members)
            {
                var paramName = member.ParameterName ?? GenerateParameterName(member.Name);
                var memberType = member.Type?.ToLowerInvariant();

                // Only add null checks for reference types
                if (memberType == "string" || memberType == "object" ||
                    (memberType != "integer" && memberType != "long" && memberType != "short" &&
                     memberType != "byte" && memberType != "single" && memberType != "double" &&
                     memberType != "decimal" && memberType != "boolean" && memberType != "char" &&
                     memberType != "date"))
                {
                    sb.AppendLine($"        If {paramName} Is Nothing Then");
                    sb.AppendLine($"            Throw New ArgumentNullException(NameOf({paramName}))");
                    sb.AppendLine($"        End If");
                }
            }
        }

        // Add assignments
        foreach (var member in members)
        {
            var paramName = member.ParameterName ?? GenerateParameterName(member.Name);
            sb.AppendLine($"        {member.Name} = {paramName}");
        }

        sb.Append("    End Sub");

        return sb.ToString();
    }

    #endregion

    #region Implement Interface

    public async Task<ImplementableInterfacesInfo?> GetImplementableInterfacesAsync(string filePath, int line, int column, CancellationToken cancellationToken = default)
    {
        try
        {
            var content = await _fileService.ReadFileAsync(filePath, cancellationToken);
            var lines = content.Split('\n');

            // Find the class at the cursor position
            var classInfo = FindClassAtPosition(lines, line, filePath);
            if (classInfo == null)
            {
                return null;
            }

            var info = classInfo.Value;

            // Find implemented interfaces from class declaration
            var implementedInterfaces = FindImplementedInterfacesFromDeclaration(lines, info.StartLine);

            if (implementedInterfaces.Count == 0)
            {
                return null;
            }

            // Get existing members in the class
            var existingMembers = GetExistingClassMembers(lines, info.StartLine, info.EndLine);

            // Find interface definitions and their members
            var interfaces = new List<ImplementableInterface>();
            foreach (var interfaceName in implementedInterfaces)
            {
                var interfaceInfo = await FindInterfaceDefinitionAsync(filePath, interfaceName, cancellationToken);
                if (interfaceInfo != null)
                {
                    // Check which members are already implemented
                    foreach (var member in interfaceInfo.Members)
                    {
                        member.IsImplemented = IsMemberImplemented(member, existingMembers);
                        member.IsSelected = !member.IsImplemented;
                    }

                    interfaceInfo.UnimplementedCount = interfaceInfo.Members.Count(m => !m.IsImplemented);
                    interfaceInfo.IsFullyImplemented = interfaceInfo.UnimplementedCount == 0;

                    interfaces.Add(interfaceInfo);
                }
            }

            // Find insertion point (after last member or after class declaration)
            var insertionLine = FindMemberInsertionPoint(lines, info.StartLine, info.EndLine);

            // Find namespace
            string? classNamespace = null;
            for (var i = 0; i < info.StartLine; i++)
            {
                var nsMatch = Regex.Match(lines[i], @"^\s*Namespace\s+(\S+)", RegexOptions.IgnoreCase);
                if (nsMatch.Success)
                {
                    classNamespace = nsMatch.Groups[1].Value;
                }
            }

            return new ImplementableInterfacesInfo
            {
                ClassName = info.Name,
                FilePath = filePath,
                ClassStartLine = info.StartLine,
                ClassEndLine = info.EndLine,
                Namespace = classNamespace,
                Interfaces = interfaces,
                ExistingMembers = existingMembers,
                InsertionLine = insertionLine
            };
        }
        catch
        {
            return null;
        }
    }

    private List<string> FindImplementedInterfacesFromDeclaration(string[] lines, int classStartLine)
    {
        var interfaces = new List<string>();

        // Look at the class declaration line and subsequent lines for Implements statements
        for (var i = classStartLine - 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            // Check for inline Implements in class declaration
            var implementsMatch = Regex.Match(line, @"\bImplements\s+(.+)$", RegexOptions.IgnoreCase);
            if (implementsMatch.Success)
            {
                var interfaceList = implementsMatch.Groups[1].Value;
                var names = interfaceList.Split(',').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s));
                interfaces.AddRange(names);
            }

            // Stop at first member or End Class
            if (Regex.IsMatch(line, @"^\s*(Public|Private|Protected|Friend)?\s*(Shared\s+)?(Sub|Function|Property|Event)\s+", RegexOptions.IgnoreCase))
                break;
            if (Regex.IsMatch(line, @"^\s*End\s+Class\s*$", RegexOptions.IgnoreCase))
                break;
            if (Regex.IsMatch(line, @"^\s*(Private|Dim)\s+", RegexOptions.IgnoreCase))
                break;
        }

        return interfaces;
    }

    private List<string> GetExistingClassMembers(string[] lines, int classStartLine, int classEndLine)
    {
        var members = new List<string>();

        for (var i = classStartLine; i < classEndLine - 1 && i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            // Match Sub declarations
            var subMatch = Regex.Match(line, @"^\s*(?:Public|Private|Protected|Friend)?\s*(?:Shared\s+)?(?:Overridable\s+|Overrides\s+|MustOverride\s+)?Sub\s+(\w+)", RegexOptions.IgnoreCase);
            if (subMatch.Success)
            {
                members.Add($"Sub:{subMatch.Groups[1].Value}");
                continue;
            }

            // Match Function declarations
            var funcMatch = Regex.Match(line, @"^\s*(?:Public|Private|Protected|Friend)?\s*(?:Shared\s+)?(?:Overridable\s+|Overrides\s+|MustOverride\s+)?Function\s+(\w+)", RegexOptions.IgnoreCase);
            if (funcMatch.Success)
            {
                members.Add($"Function:{funcMatch.Groups[1].Value}");
                continue;
            }

            // Match Property declarations
            var propMatch = Regex.Match(line, @"^\s*(?:Public|Private|Protected|Friend)?\s*(?:Shared\s+)?(?:Overridable\s+|Overrides\s+|MustOverride\s+)?(?:ReadOnly\s+|WriteOnly\s+)?Property\s+(\w+)", RegexOptions.IgnoreCase);
            if (propMatch.Success)
            {
                members.Add($"Property:{propMatch.Groups[1].Value}");
                continue;
            }

            // Match Event declarations
            var eventMatch = Regex.Match(line, @"^\s*(?:Public|Private|Protected|Friend)?\s*Event\s+(\w+)", RegexOptions.IgnoreCase);
            if (eventMatch.Success)
            {
                members.Add($"Event:{eventMatch.Groups[1].Value}");
            }
        }

        return members;
    }

    private bool IsMemberImplemented(InterfaceMemberInfo member, List<string> existingMembers)
    {
        var kindStr = member.Kind switch
        {
            InterfaceMemberKind.Sub => "Sub",
            InterfaceMemberKind.Function => "Function",
            InterfaceMemberKind.Property => "Property",
            InterfaceMemberKind.Event => "Event",
            _ => ""
        };

        return existingMembers.Any(m => m.Equals($"{kindStr}:{member.Name}", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<ImplementableInterface?> FindInterfaceDefinitionAsync(string currentFilePath, string interfaceName, CancellationToken cancellationToken)
    {
        // First, search in the current file
        var content = await _fileService.ReadFileAsync(currentFilePath, cancellationToken);
        var result = ParseInterfaceFromContent(content, interfaceName, currentFilePath);
        if (result != null)
            return result;

        // Search in project files
        if (_projectService.CurrentProject != null)
        {
            var projectDir = Path.GetDirectoryName(_projectService.CurrentProject.FilePath);
            if (projectDir != null)
            {
                var files = Directory.GetFiles(projectDir, "*.bas", SearchOption.AllDirectories)
                    .Concat(Directory.GetFiles(projectDir, "*.bl", SearchOption.AllDirectories))
                    .Where(f => !f.Equals(currentFilePath, StringComparison.OrdinalIgnoreCase));

                foreach (var file in files)
                {
                    try
                    {
                        var fileContent = await _fileService.ReadFileAsync(file, cancellationToken);
                        result = ParseInterfaceFromContent(fileContent, interfaceName, file);
                        if (result != null)
                            return result;
                    }
                    catch
                    {
                        // Skip files we can't read
                    }
                }
            }
        }

        // If interface not found, create a stub with no members
        return new ImplementableInterface
        {
            Name = interfaceName,
            FilePath = null,
            Members = new List<InterfaceMemberInfo>()
        };
    }

    private ImplementableInterface? ParseInterfaceFromContent(string content, string interfaceName, string filePath)
    {
        var lines = content.Split('\n');

        // Find interface declaration
        var interfacePattern = $@"^\s*(?:Public\s+|Friend\s+)?Interface\s+{Regex.Escape(interfaceName)}\s*$";

        for (var i = 0; i < lines.Length; i++)
        {
            if (Regex.IsMatch(lines[i], interfacePattern, RegexOptions.IgnoreCase))
            {
                var startLine = i + 1;
                var members = new List<InterfaceMemberInfo>();

                // Parse interface members until End Interface
                for (var j = i + 1; j < lines.Length; j++)
                {
                    var line = lines[j].Trim();

                    if (Regex.IsMatch(line, @"^\s*End\s+Interface\s*$", RegexOptions.IgnoreCase))
                        break;

                    // Parse Sub
                    var subMatch = Regex.Match(line, @"^\s*Sub\s+(\w+)\s*\(([^)]*)\)", RegexOptions.IgnoreCase);
                    if (subMatch.Success)
                    {
                        members.Add(new InterfaceMemberInfo
                        {
                            Name = subMatch.Groups[1].Value,
                            Kind = InterfaceMemberKind.Sub,
                            Signature = line,
                            Parameters = ParseInterfaceParameters(subMatch.Groups[2].Value)
                        });
                        continue;
                    }

                    // Parse Function
                    var funcMatch = Regex.Match(line, @"^\s*Function\s+(\w+)\s*\(([^)]*)\)\s*As\s+(\w+)", RegexOptions.IgnoreCase);
                    if (funcMatch.Success)
                    {
                        members.Add(new InterfaceMemberInfo
                        {
                            Name = funcMatch.Groups[1].Value,
                            Kind = InterfaceMemberKind.Function,
                            Signature = line,
                            ReturnType = funcMatch.Groups[3].Value,
                            Parameters = ParseInterfaceParameters(funcMatch.Groups[2].Value)
                        });
                        continue;
                    }

                    // Parse Property (read-only, write-only, or read-write)
                    var propMatch = Regex.Match(line, @"^\s*(ReadOnly\s+|WriteOnly\s+)?Property\s+(\w+)(?:\s*\(([^)]*)\))?\s*As\s+(\w+)", RegexOptions.IgnoreCase);
                    if (propMatch.Success)
                    {
                        var isReadOnly = propMatch.Groups[1].Value.Trim().Equals("ReadOnly", StringComparison.OrdinalIgnoreCase);
                        var isWriteOnly = propMatch.Groups[1].Value.Trim().Equals("WriteOnly", StringComparison.OrdinalIgnoreCase);

                        members.Add(new InterfaceMemberInfo
                        {
                            Name = propMatch.Groups[2].Value,
                            Kind = InterfaceMemberKind.Property,
                            Signature = line,
                            ReturnType = propMatch.Groups[4].Value,
                            Parameters = ParseInterfaceParameters(propMatch.Groups[3].Value),
                            HasGetter = !isWriteOnly,
                            HasSetter = !isReadOnly
                        });
                        continue;
                    }

                    // Parse Event
                    var eventMatch = Regex.Match(line, @"^\s*Event\s+(\w+)\s*\(([^)]*)\)", RegexOptions.IgnoreCase);
                    if (eventMatch.Success)
                    {
                        members.Add(new InterfaceMemberInfo
                        {
                            Name = eventMatch.Groups[1].Value,
                            Kind = InterfaceMemberKind.Event,
                            Signature = line,
                            Parameters = ParseInterfaceParameters(eventMatch.Groups[2].Value)
                        });
                    }
                }

                return new ImplementableInterface
                {
                    Name = interfaceName,
                    FilePath = filePath,
                    Line = startLine,
                    Members = members
                };
            }
        }

        return null;
    }

    private List<InterfaceParameterInfo> ParseInterfaceParameters(string paramString)
    {
        var parameters = new List<InterfaceParameterInfo>();

        if (string.IsNullOrWhiteSpace(paramString))
            return parameters;

        var parts = paramString.Split(',');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            var param = new InterfaceParameterInfo();

            // Check for ByRef/ByVal
            if (trimmed.StartsWith("ByRef ", StringComparison.OrdinalIgnoreCase))
            {
                param.IsByRef = true;
                trimmed = trimmed.Substring(6).Trim();
            }
            else if (trimmed.StartsWith("ByVal ", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(6).Trim();
            }

            // Check for Optional
            if (trimmed.StartsWith("Optional ", StringComparison.OrdinalIgnoreCase))
            {
                param.IsOptional = true;
                trimmed = trimmed.Substring(9).Trim();
            }

            // Parse name As Type = DefaultValue
            var match = Regex.Match(trimmed, @"^(\w+)\s+As\s+(\w+)(?:\s*=\s*(.+))?$", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                param.Name = match.Groups[1].Value;
                param.Type = match.Groups[2].Value;
                if (match.Groups[3].Success)
                {
                    param.DefaultValue = match.Groups[3].Value.Trim();
                }
            }
            else
            {
                // Just a name without type
                param.Name = trimmed.Split(' ')[0];
                param.Type = "Object";
            }

            parameters.Add(param);
        }

        return parameters;
    }

    private int FindMemberInsertionPoint(string[] lines, int classStartLine, int classEndLine)
    {
        // Find the last member in the class, or return line after class declaration
        var lastMemberLine = classStartLine;

        for (var i = classStartLine; i < classEndLine - 1 && i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            if (Regex.IsMatch(line, @"^\s*End\s+(Sub|Function|Property|Get|Set)\s*$", RegexOptions.IgnoreCase))
            {
                lastMemberLine = i + 1;
            }
        }

        return lastMemberLine + 1;
    }

    public async Task<ImplementInterfaceResult> ImplementInterfaceAsync(string filePath, int line, int column, ImplementInterfaceOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            var interfacesInfo = await GetImplementableInterfacesAsync(filePath, line, column, cancellationToken);
            if (interfacesInfo == null)
            {
                return new ImplementInterfaceResult
                {
                    Success = false,
                    ErrorMessage = "Could not find class implementing interfaces at the specified location"
                };
            }

            // Find the requested interface
            var targetInterface = interfacesInfo.Interfaces.FirstOrDefault(i =>
                i.Name.Equals(options.InterfaceName, StringComparison.OrdinalIgnoreCase));

            if (targetInterface == null)
            {
                return new ImplementInterfaceResult
                {
                    Success = false,
                    ErrorMessage = $"Interface '{options.InterfaceName}' not found"
                };
            }

            // Get members to implement
            var membersToImplement = targetInterface.Members
                .Where(m => options.SelectedMembers.Contains(m.Name) || options.SelectedMembers.Count == 0)
                .Where(m => !m.IsImplemented)
                .ToList();

            if (membersToImplement.Count == 0)
            {
                return new ImplementInterfaceResult
                {
                    Success = false,
                    ErrorMessage = "No members to implement"
                };
            }

            // Generate implementation code
            var generatedCode = GenerateInterfaceImplementation(
                targetInterface.Name,
                membersToImplement,
                options);

            // Read file and insert code
            var content = await _fileService.ReadFileAsync(filePath, cancellationToken);
            var lines = content.Split('\n');

            // Find insertion point
            var insertionLine = interfacesInfo.InsertionLine;

            // Insert the generated code
            var newLines = new List<string>(lines);
            newLines.Insert(insertionLine - 1, generatedCode);

            var newContent = string.Join("\n", newLines);
            await _fileService.WriteFileAsync(filePath, newContent, cancellationToken);

            return new ImplementInterfaceResult
            {
                Success = true,
                MembersImplemented = membersToImplement.Count,
                GeneratedCode = generatedCode,
                FileEdit = new FileEdit
                {
                    FilePath = filePath,
                    Edits = new List<TextEdit>
                    {
                        new TextEdit
                        {
                            StartLine = insertionLine,
                            StartColumn = 1,
                            EndLine = insertionLine,
                            EndColumn = 1,
                            NewText = generatedCode + "\n"
                        }
                    }
                }
            };
        }
        catch (Exception ex)
        {
            return new ImplementInterfaceResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private string GenerateInterfaceImplementation(string interfaceName, List<InterfaceMemberInfo> members, ImplementInterfaceOptions options)
    {
        var sb = new System.Text.StringBuilder();

        if (options.InsertRegion)
        {
            sb.AppendLine();
            sb.AppendLine($"    #Region \"{interfaceName} Implementation\"");
        }

        sb.AppendLine();

        foreach (var member in members)
        {
            switch (member.Kind)
            {
                case InterfaceMemberKind.Sub:
                    GenerateSubImplementation(sb, member, interfaceName, options);
                    break;

                case InterfaceMemberKind.Function:
                    GenerateFunctionImplementation(sb, member, interfaceName, options);
                    break;

                case InterfaceMemberKind.Property:
                    GeneratePropertyImplementation(sb, member, interfaceName, options);
                    break;

                case InterfaceMemberKind.Event:
                    GenerateEventImplementation(sb, member, interfaceName);
                    break;
            }

            sb.AppendLine();
        }

        if (options.InsertRegion)
        {
            sb.AppendLine("    #End Region");
        }

        return sb.ToString().TrimEnd();
    }

    private void GenerateSubImplementation(System.Text.StringBuilder sb, InterfaceMemberInfo member, string interfaceName, ImplementInterfaceOptions options)
    {
        var paramsStr = GenerateParameterList(member.Parameters);
        var implementsClause = options.GenerateExplicitImplementation ? $" Implements {interfaceName}.{member.Name}" : "";

        sb.AppendLine($"    Public Sub {member.Name}({paramsStr}){implementsClause}");

        if (options.ThrowNotImplementedException)
        {
            sb.AppendLine("        Throw New NotImplementedException()");
        }
        else
        {
            sb.AppendLine("        ' TODO: Implement this method");
        }

        sb.AppendLine("    End Sub");
    }

    private void GenerateFunctionImplementation(System.Text.StringBuilder sb, InterfaceMemberInfo member, string interfaceName, ImplementInterfaceOptions options)
    {
        var paramsStr = GenerateParameterList(member.Parameters);
        var returnType = member.ReturnType ?? "Object";
        var implementsClause = options.GenerateExplicitImplementation ? $" Implements {interfaceName}.{member.Name}" : "";

        sb.AppendLine($"    Public Function {member.Name}({paramsStr}) As {returnType}{implementsClause}");

        if (options.ThrowNotImplementedException)
        {
            sb.AppendLine("        Throw New NotImplementedException()");
        }
        else
        {
            sb.AppendLine("        ' TODO: Implement this method");
            sb.AppendLine($"        Return Nothing");
        }

        sb.AppendLine("    End Function");
    }

    private void GeneratePropertyImplementation(System.Text.StringBuilder sb, InterfaceMemberInfo member, string interfaceName, ImplementInterfaceOptions options)
    {
        var returnType = member.ReturnType ?? "Object";
        var implementsClause = options.GenerateExplicitImplementation ? $" Implements {interfaceName}.{member.Name}" : "";

        // Determine property type
        var propertyModifier = "";
        if (member.HasGetter && !member.HasSetter)
            propertyModifier = "ReadOnly ";
        else if (!member.HasGetter && member.HasSetter)
            propertyModifier = "WriteOnly ";

        // Generate backing field if not throwing exception
        var backingFieldName = $"_{char.ToLower(member.Name[0])}{member.Name.Substring(1)}";

        if (!options.ThrowNotImplementedException)
        {
            sb.AppendLine($"    Private {backingFieldName} As {returnType}");
            sb.AppendLine();
        }

        // Check if property has parameters (indexer)
        var paramsStr = member.Parameters.Count > 0 ? $"({GenerateParameterList(member.Parameters)})" : "";

        sb.AppendLine($"    Public {propertyModifier}Property {member.Name}{paramsStr} As {returnType}{implementsClause}");

        if (member.HasGetter)
        {
            sb.AppendLine("        Get");
            if (options.ThrowNotImplementedException)
            {
                sb.AppendLine("            Throw New NotImplementedException()");
            }
            else
            {
                sb.AppendLine($"            Return {backingFieldName}");
            }
            sb.AppendLine("        End Get");
        }

        if (member.HasSetter)
        {
            sb.AppendLine($"        Set(value As {returnType})");
            if (options.ThrowNotImplementedException)
            {
                sb.AppendLine("            Throw New NotImplementedException()");
            }
            else
            {
                sb.AppendLine($"            {backingFieldName} = value");
            }
            sb.AppendLine("        End Set");
        }

        sb.AppendLine("    End Property");
    }

    private void GenerateEventImplementation(System.Text.StringBuilder sb, InterfaceMemberInfo member, string interfaceName)
    {
        var paramsStr = GenerateParameterList(member.Parameters);
        sb.AppendLine($"    Public Event {member.Name}({paramsStr})");
    }

    private string GenerateParameterList(List<InterfaceParameterInfo> parameters)
    {
        var parts = new List<string>();

        foreach (var param in parameters)
        {
            var sb = new System.Text.StringBuilder();

            if (param.IsOptional)
                sb.Append("Optional ");

            if (param.IsByRef)
                sb.Append("ByRef ");
            else
                sb.Append("ByVal ");

            sb.Append(param.Name);
            sb.Append(" As ");
            sb.Append(param.Type);

            if (param.DefaultValue != null)
            {
                sb.Append(" = ");
                sb.Append(param.DefaultValue);
            }

            parts.Add(sb.ToString());
        }

        return string.Join(", ", parts);
    }

    #endregion

    #region Override Method

    public async Task<OverridableMethodsInfo?> GetOverridableMethodsAsync(string filePath, int line, int column, CancellationToken cancellationToken = default)
    {
        try
        {
            var content = await _fileService.ReadFileAsync(filePath, cancellationToken);
            var lines = content.Split('\n');

            // Find the class at the cursor position
            var classInfo = FindClassAtPosition(lines, line, filePath);
            if (classInfo == null)
            {
                return null;
            }

            var info = classInfo.Value;

            // Find base class from Inherits clause
            var baseClassName = FindBaseClass(lines, info.StartLine);
            if (string.IsNullOrEmpty(baseClassName))
            {
                return null;
            }

            // Get existing overrides in the class
            var existingOverrides = GetExistingOverrides(lines, info.StartLine, info.EndLine);

            // Find base class definition and its overridable methods
            var (baseClassFilePath, overridableMethods) = await FindOverridableMethodsAsync(filePath, baseClassName, cancellationToken);

            // Mark methods that are already overridden
            foreach (var method in overridableMethods)
            {
                method.IsOverridden = existingOverrides.Contains(method.Name, StringComparer.OrdinalIgnoreCase);
                method.IsSelected = !method.IsOverridden;
            }

            // Find insertion point
            var insertionLine = FindMemberInsertionPoint(lines, info.StartLine, info.EndLine);

            return new OverridableMethodsInfo
            {
                ClassName = info.Name,
                FilePath = filePath,
                ClassStartLine = info.StartLine,
                ClassEndLine = info.EndLine,
                BaseClassName = baseClassName,
                BaseClassFilePath = baseClassFilePath,
                Methods = overridableMethods,
                ExistingOverrides = existingOverrides,
                InsertionLine = insertionLine
            };
        }
        catch
        {
            return null;
        }
    }

    private string? FindBaseClass(string[] lines, int classStartLine)
    {
        // Look for Inherits clause after class declaration
        for (var i = classStartLine - 1; i < Math.Min(classStartLine + 5, lines.Length); i++)
        {
            var line = lines[i].Trim();

            // Check for Inherits in class declaration line
            var inheritsMatch = Regex.Match(line, @"\bInherits\s+(\w+)", RegexOptions.IgnoreCase);
            if (inheritsMatch.Success)
            {
                return inheritsMatch.Groups[1].Value;
            }

            // Stop at first member
            if (Regex.IsMatch(line, @"^\s*(Public|Private|Protected|Friend)?\s*(Shared\s+)?(Sub|Function|Property|Event)\s+", RegexOptions.IgnoreCase))
                break;
            if (Regex.IsMatch(line, @"^\s*End\s+Class\s*$", RegexOptions.IgnoreCase))
                break;
        }

        return null;
    }

    private List<string> GetExistingOverrides(string[] lines, int classStartLine, int classEndLine)
    {
        var overrides = new List<string>();

        for (var i = classStartLine; i < classEndLine - 1 && i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            // Match Overrides Sub/Function/Property
            var overrideMatch = Regex.Match(line, @"\bOverrides\s+(?:Sub|Function|Property)\s+(\w+)", RegexOptions.IgnoreCase);
            if (overrideMatch.Success)
            {
                overrides.Add(overrideMatch.Groups[1].Value);
            }
        }

        return overrides;
    }

    private async Task<(string? FilePath, List<OverridableMethod>)> FindOverridableMethodsAsync(string currentFilePath, string baseClassName, CancellationToken cancellationToken)
    {
        var methods = new List<OverridableMethod>();

        // First, search in the current file
        var content = await _fileService.ReadFileAsync(currentFilePath, cancellationToken);
        var result = ParseOverridableMethodsFromContent(content, baseClassName, currentFilePath);
        if (result.Count > 0)
            return (currentFilePath, result);

        // Search in project files
        if (_projectService.CurrentProject != null)
        {
            var projectDir = Path.GetDirectoryName(_projectService.CurrentProject.FilePath);
            if (projectDir != null)
            {
                var files = Directory.GetFiles(projectDir, "*.bas", SearchOption.AllDirectories)
                    .Concat(Directory.GetFiles(projectDir, "*.bl", SearchOption.AllDirectories))
                    .Where(f => !f.Equals(currentFilePath, StringComparison.OrdinalIgnoreCase));

                foreach (var file in files)
                {
                    try
                    {
                        var fileContent = await _fileService.ReadFileAsync(file, cancellationToken);
                        result = ParseOverridableMethodsFromContent(fileContent, baseClassName, file);
                        if (result.Count > 0)
                            return (file, result);
                    }
                    catch
                    {
                        // Skip files we can't read
                    }
                }
            }
        }

        return (null, methods);
    }

    private List<OverridableMethod> ParseOverridableMethodsFromContent(string content, string baseClassName, string filePath)
    {
        var methods = new List<OverridableMethod>();
        var lines = content.Split('\n');

        // Find class declaration
        var classPattern = $@"^\s*(?:Public\s+|Friend\s+)?(?:MustInherit\s+)?Class\s+{Regex.Escape(baseClassName)}\s*";

        for (var i = 0; i < lines.Length; i++)
        {
            if (Regex.IsMatch(lines[i], classPattern, RegexOptions.IgnoreCase))
            {
                // Parse class members until End Class
                for (var j = i + 1; j < lines.Length; j++)
                {
                    var line = lines[j].Trim();

                    if (Regex.IsMatch(line, @"^\s*End\s+Class\s*$", RegexOptions.IgnoreCase))
                        break;

                    // Parse Overridable/MustOverride Sub
                    var subMatch = Regex.Match(line, @"^\s*(?:Public\s+|Protected\s+)?(?:Overridable|MustOverride)\s+Sub\s+(\w+)\s*\(([^)]*)\)", RegexOptions.IgnoreCase);
                    if (subMatch.Success)
                    {
                        var isAbstract = line.Contains("MustOverride", StringComparison.OrdinalIgnoreCase);
                        methods.Add(new OverridableMethod
                        {
                            Name = subMatch.Groups[1].Value,
                            Kind = OverridableMethodKind.Sub,
                            Signature = line,
                            Parameters = ParseOverridableParameters(subMatch.Groups[2].Value),
                            DeclaringClass = baseClassName,
                            IsAbstract = isAbstract,
                            SourceLine = j + 1
                        });
                        continue;
                    }

                    // Parse Overridable/MustOverride Function
                    var funcMatch = Regex.Match(line, @"^\s*(?:Public\s+|Protected\s+)?(?:Overridable|MustOverride)\s+Function\s+(\w+)\s*\(([^)]*)\)\s*As\s+(\w+)", RegexOptions.IgnoreCase);
                    if (funcMatch.Success)
                    {
                        var isAbstract = line.Contains("MustOverride", StringComparison.OrdinalIgnoreCase);
                        methods.Add(new OverridableMethod
                        {
                            Name = funcMatch.Groups[1].Value,
                            Kind = OverridableMethodKind.Function,
                            Signature = line,
                            ReturnType = funcMatch.Groups[3].Value,
                            Parameters = ParseOverridableParameters(funcMatch.Groups[2].Value),
                            DeclaringClass = baseClassName,
                            IsAbstract = isAbstract,
                            SourceLine = j + 1
                        });
                        continue;
                    }

                    // Parse Overridable/MustOverride Property
                    var propMatch = Regex.Match(line, @"^\s*(?:Public\s+|Protected\s+)?(?:Overridable|MustOverride)\s+(?:ReadOnly\s+|WriteOnly\s+)?Property\s+(\w+)(?:\s*\(([^)]*)\))?\s*As\s+(\w+)", RegexOptions.IgnoreCase);
                    if (propMatch.Success)
                    {
                        var isAbstract = line.Contains("MustOverride", StringComparison.OrdinalIgnoreCase);
                        methods.Add(new OverridableMethod
                        {
                            Name = propMatch.Groups[1].Value,
                            Kind = OverridableMethodKind.Property,
                            Signature = line,
                            ReturnType = propMatch.Groups[3].Value,
                            Parameters = ParseOverridableParameters(propMatch.Groups[2].Value),
                            DeclaringClass = baseClassName,
                            IsAbstract = isAbstract,
                            SourceLine = j + 1
                        });
                    }
                }

                break;
            }
        }

        // Also check if the base class itself inherits from another class
        // and recursively find overridable methods
        var baseBaseClass = FindBaseClassInContent(content, baseClassName);
        if (!string.IsNullOrEmpty(baseBaseClass))
        {
            var parentMethods = ParseOverridableMethodsFromContent(content, baseBaseClass, filePath);
            foreach (var method in parentMethods)
            {
                if (!methods.Any(m => m.Name.Equals(method.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    methods.Add(method);
                }
            }
        }

        return methods;
    }

    private string? FindBaseClassInContent(string content, string className)
    {
        var lines = content.Split('\n');
        var classPattern = $@"^\s*(?:Public\s+|Friend\s+)?(?:MustInherit\s+)?Class\s+{Regex.Escape(className)}\s*";

        for (var i = 0; i < lines.Length; i++)
        {
            if (Regex.IsMatch(lines[i], classPattern, RegexOptions.IgnoreCase))
            {
                // Look for Inherits in the next few lines
                for (var j = i; j < Math.Min(i + 5, lines.Length); j++)
                {
                    var inheritsMatch = Regex.Match(lines[j], @"\bInherits\s+(\w+)", RegexOptions.IgnoreCase);
                    if (inheritsMatch.Success)
                    {
                        return inheritsMatch.Groups[1].Value;
                    }

                    if (Regex.IsMatch(lines[j], @"^\s*(Public|Private|Protected|Friend)?\s*(Sub|Function|Property)", RegexOptions.IgnoreCase))
                        break;
                }
                break;
            }
        }

        return null;
    }

    private List<OverridableParameterInfo> ParseOverridableParameters(string paramString)
    {
        var parameters = new List<OverridableParameterInfo>();

        if (string.IsNullOrWhiteSpace(paramString))
            return parameters;

        var parts = paramString.Split(',');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            var param = new OverridableParameterInfo();

            // Check for ByRef/ByVal
            if (trimmed.StartsWith("ByRef ", StringComparison.OrdinalIgnoreCase))
            {
                param.IsByRef = true;
                trimmed = trimmed.Substring(6).Trim();
            }
            else if (trimmed.StartsWith("ByVal ", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(6).Trim();
            }

            // Check for Optional
            if (trimmed.StartsWith("Optional ", StringComparison.OrdinalIgnoreCase))
            {
                param.IsOptional = true;
                trimmed = trimmed.Substring(9).Trim();
            }

            // Parse name As Type = DefaultValue
            var match = Regex.Match(trimmed, @"^(\w+)\s+As\s+(\w+)(?:\s*=\s*(.+))?$", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                param.Name = match.Groups[1].Value;
                param.Type = match.Groups[2].Value;
                if (match.Groups[3].Success)
                {
                    param.DefaultValue = match.Groups[3].Value.Trim();
                }
            }
            else
            {
                param.Name = trimmed.Split(' ')[0];
                param.Type = "Object";
            }

            parameters.Add(param);
        }

        return parameters;
    }

    public async Task<OverrideMethodResult> OverrideMethodAsync(string filePath, int line, int column, OverrideMethodOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            var methodsInfo = await GetOverridableMethodsAsync(filePath, line, column, cancellationToken);
            if (methodsInfo == null)
            {
                return new OverrideMethodResult
                {
                    Success = false,
                    ErrorMessage = "Could not find class with base class at the specified location"
                };
            }

            // Get methods to override
            var methodsToOverride = methodsInfo.Methods
                .Where(m => options.SelectedMethods.Contains(m.Name) || options.SelectedMethods.Count == 0)
                .Where(m => !m.IsOverridden)
                .ToList();

            if (methodsToOverride.Count == 0)
            {
                return new OverrideMethodResult
                {
                    Success = false,
                    ErrorMessage = "No methods to override"
                };
            }

            // Generate override code
            var generatedCode = GenerateOverrideCode(methodsInfo.BaseClassName!, methodsToOverride, options);

            // Read file and insert code
            var content = await _fileService.ReadFileAsync(filePath, cancellationToken);
            var lines = content.Split('\n');

            // Insert the generated code
            var newLines = new List<string>(lines);
            newLines.Insert(methodsInfo.InsertionLine - 1, generatedCode);

            var newContent = string.Join("\n", newLines);
            await _fileService.WriteFileAsync(filePath, newContent, cancellationToken);

            return new OverrideMethodResult
            {
                Success = true,
                MethodsOverridden = methodsToOverride.Count,
                GeneratedCode = generatedCode,
                FileEdit = new FileEdit
                {
                    FilePath = filePath,
                    Edits = new List<TextEdit>
                    {
                        new TextEdit
                        {
                            StartLine = methodsInfo.InsertionLine,
                            StartColumn = 1,
                            EndLine = methodsInfo.InsertionLine,
                            EndColumn = 1,
                            NewText = generatedCode + "\n"
                        }
                    }
                }
            };
        }
        catch (Exception ex)
        {
            return new OverrideMethodResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private string GenerateOverrideCode(string baseClassName, List<OverridableMethod> methods, OverrideMethodOptions options)
    {
        var sb = new System.Text.StringBuilder();

        if (options.InsertRegion)
        {
            sb.AppendLine();
            sb.AppendLine($"    #Region \"Overrides\"");
        }

        sb.AppendLine();

        foreach (var method in methods)
        {
            switch (method.Kind)
            {
                case OverridableMethodKind.Sub:
                    GenerateOverrideSub(sb, method, baseClassName, options);
                    break;

                case OverridableMethodKind.Function:
                    GenerateOverrideFunction(sb, method, baseClassName, options);
                    break;

                case OverridableMethodKind.Property:
                    GenerateOverrideProperty(sb, method, baseClassName, options);
                    break;
            }

            sb.AppendLine();
        }

        if (options.InsertRegion)
        {
            sb.AppendLine("    #End Region");
        }

        return sb.ToString().TrimEnd();
    }

    private void GenerateOverrideSub(System.Text.StringBuilder sb, OverridableMethod method, string baseClassName, OverrideMethodOptions options)
    {
        var paramsStr = GenerateOverrideParameterList(method.Parameters);
        var argsStr = string.Join(", ", method.Parameters.Select(p => p.Name));

        sb.AppendLine($"    Public Overrides Sub {method.Name}({paramsStr})");

        if (options.CallBaseMethod && !method.IsAbstract)
        {
            if (string.IsNullOrEmpty(argsStr))
                sb.AppendLine($"        MyBase.{method.Name}()");
            else
                sb.AppendLine($"        MyBase.{method.Name}({argsStr})");
        }
        else
        {
            sb.AppendLine("        ' TODO: Add implementation");
        }

        sb.AppendLine("    End Sub");
    }

    private void GenerateOverrideFunction(System.Text.StringBuilder sb, OverridableMethod method, string baseClassName, OverrideMethodOptions options)
    {
        var paramsStr = GenerateOverrideParameterList(method.Parameters);
        var argsStr = string.Join(", ", method.Parameters.Select(p => p.Name));
        var returnType = method.ReturnType ?? "Object";

        sb.AppendLine($"    Public Overrides Function {method.Name}({paramsStr}) As {returnType}");

        if (options.CallBaseMethod && !method.IsAbstract)
        {
            if (string.IsNullOrEmpty(argsStr))
                sb.AppendLine($"        Return MyBase.{method.Name}()");
            else
                sb.AppendLine($"        Return MyBase.{method.Name}({argsStr})");
        }
        else
        {
            sb.AppendLine("        ' TODO: Add implementation");
            sb.AppendLine("        Return Nothing");
        }

        sb.AppendLine("    End Function");
    }

    private void GenerateOverrideProperty(System.Text.StringBuilder sb, OverridableMethod method, string baseClassName, OverrideMethodOptions options)
    {
        var returnType = method.ReturnType ?? "Object";

        sb.AppendLine($"    Public Overrides Property {method.Name} As {returnType}");
        sb.AppendLine("        Get");

        if (options.CallBaseMethod && !method.IsAbstract)
        {
            sb.AppendLine($"            Return MyBase.{method.Name}");
        }
        else
        {
            sb.AppendLine("            ' TODO: Add implementation");
            sb.AppendLine("            Return Nothing");
        }

        sb.AppendLine("        End Get");
        sb.AppendLine($"        Set(value As {returnType})");

        if (options.CallBaseMethod && !method.IsAbstract)
        {
            sb.AppendLine($"            MyBase.{method.Name} = value");
        }
        else
        {
            sb.AppendLine("            ' TODO: Add implementation");
        }

        sb.AppendLine("        End Set");
        sb.AppendLine("    End Property");
    }

    private string GenerateOverrideParameterList(List<OverridableParameterInfo> parameters)
    {
        var parts = new List<string>();

        foreach (var param in parameters)
        {
            var sb = new System.Text.StringBuilder();

            if (param.IsOptional)
                sb.Append("Optional ");

            if (param.IsByRef)
                sb.Append("ByRef ");
            else
                sb.Append("ByVal ");

            sb.Append(param.Name);
            sb.Append(" As ");
            sb.Append(param.Type);

            if (param.DefaultValue != null)
            {
                sb.Append(" = ");
                sb.Append(param.DefaultValue);
            }

            parts.Add(sb.ToString());
        }

        return string.Join(", ", parts);
    }

    #endregion

    #region Add Parameter

    public async Task<AddParameterInfo?> GetMethodForParameterAsync(string filePath, int line, int column, CancellationToken cancellationToken = default)
    {
        try
        {
            var content = await _fileService.ReadFileAsync(filePath, cancellationToken);
            var lines = content.Split('\n');

            if (line < 1 || line > lines.Length)
                return null;

            // First check if we're on a method definition line
            var methodDef = FindMethodAtLine(lines, line - 1);
            string methodName;

            if (methodDef.HasValue)
            {
                methodName = methodDef.Value.MethodName;
            }
            else
            {
                // Try to extract symbol at position (could be a call site)
                var lineText = lines[line - 1];
                var symbol = ExtractSymbolAtPosition(lineText, column);

                if (string.IsNullOrEmpty(symbol))
                    return null;

                methodName = symbol;
            }

            // Find the method definition
            var definition = await FindMethodDefinitionForParameterAsync(filePath, methodName, cancellationToken);
            if (!definition.HasValue)
                return null;

            var def = definition.Value;

            // Find all call sites
            var references = await FindAllReferencesAsync(def.FilePath, def.DefinitionLine, 1, cancellationToken);
            var callSites = references.Where(r => r.Type == SymbolLocationType.Reference).ToList();

            // Find containing type
            var containingType = FindContainingType(content.Split('\n'), def.DefinitionLine - 1);

            return new AddParameterInfo
            {
                MethodName = def.MethodName,
                FilePath = def.FilePath,
                DefinitionLine = def.DefinitionLine,
                DefinitionEndLine = def.DefinitionEndLine,
                IsFunction = def.IsFunction,
                ReturnType = def.ReturnType,
                ExistingParameters = def.Parameters,
                CallSiteCount = callSites.Count,
                CallSites = callSites,
                ContainingType = containingType,
                Signature = def.Signature
            };
        }
        catch
        {
            return null;
        }
    }

    private (string MethodName, string FilePath, int DefinitionLine, int DefinitionEndLine, bool IsFunction,
             string? ReturnType, List<ExistingParameterInfo> Parameters, string Signature)? FindMethodAtLine(string[] lines, int lineIndex)
    {
        if (lineIndex < 0 || lineIndex >= lines.Length)
            return null;

        var lineText = lines[lineIndex];
        var subPattern = new Regex(@"^\s*(Private|Public|Protected|Friend)?\s*(Shared)?\s*(Overridable|Overrides|MustOverride)?\s*(Sub|Function)\s+(\w+)\s*\(([^)]*)\)(\s+As\s+(\w+))?", RegexOptions.IgnoreCase);
        var match = subPattern.Match(lineText);

        if (!match.Success)
            return null;

        var isFunction = match.Groups[4].Value.Equals("Function", StringComparison.OrdinalIgnoreCase);
        var methodName = match.Groups[5].Value;
        var parametersText = match.Groups[6].Value;
        var returnType = match.Groups[8].Success ? match.Groups[8].Value : null;

        var parameters = ParseExistingParameters(parametersText);

        // Find end of method
        var endLine = FindEndOfMethod(lines, lineIndex, isFunction);

        var signature = BuildMethodSignature(isFunction, methodName, parameters, returnType);

        return (methodName, "", lineIndex + 1, endLine + 1, isFunction, returnType, parameters, signature);
    }

    private List<ExistingParameterInfo> ParseExistingParameters(string parametersText)
    {
        var parameters = new List<ExistingParameterInfo>();

        if (string.IsNullOrWhiteSpace(parametersText))
            return parameters;

        var paramParts = SplitParameters(parametersText);

        for (var i = 0; i < paramParts.Count; i++)
        {
            var part = paramParts[i].Trim();
            if (string.IsNullOrEmpty(part))
                continue;

            var param = ParseSingleParameter(part);
            param.Index = i;
            parameters.Add(param);
        }

        return parameters;
    }

    private ExistingParameterInfo ParseSingleParameter(string paramText)
    {
        var param = new ExistingParameterInfo();

        // Pattern: [Optional] [ByVal|ByRef] name As Type [= defaultValue]
        var pattern = new Regex(@"^\s*(Optional)?\s*(ByVal|ByRef)?\s*(\w+)\s*(As\s+(\w+))?\s*(=\s*(.+))?$", RegexOptions.IgnoreCase);
        var match = pattern.Match(paramText);

        if (match.Success)
        {
            param.IsOptional = match.Groups[1].Success;
            param.IsByRef = match.Groups[2].Success && match.Groups[2].Value.Equals("ByRef", StringComparison.OrdinalIgnoreCase);
            param.Name = match.Groups[3].Value;
            param.Type = match.Groups[5].Success ? match.Groups[5].Value : "Object";
            param.DefaultValue = match.Groups[7].Success ? match.Groups[7].Value.Trim() : null;
        }
        else
        {
            // Simple case: just a name
            param.Name = paramText.Trim();
            param.Type = "Object";
        }

        return param;
    }

    private string BuildMethodSignature(bool isFunction, string methodName, List<ExistingParameterInfo> parameters, string? returnType)
    {
        var sb = new System.Text.StringBuilder();

        if (isFunction)
        {
            sb.Append("Function ");
            sb.Append(methodName);
            sb.Append("(");
            sb.Append(FormatParameterList(parameters));
            sb.Append(") As ");
            sb.Append(returnType ?? "Object");
        }
        else
        {
            sb.Append("Sub ");
            sb.Append(methodName);
            sb.Append("(");
            sb.Append(FormatParameterList(parameters));
            sb.Append(")");
        }

        return sb.ToString();
    }

    private string FormatParameterList(List<ExistingParameterInfo> parameters)
    {
        var parts = new List<string>();

        foreach (var param in parameters)
        {
            var sb = new System.Text.StringBuilder();

            if (param.IsOptional)
                sb.Append("Optional ");

            if (param.IsByRef)
                sb.Append("ByRef ");

            sb.Append(param.Name);
            sb.Append(" As ");
            sb.Append(param.Type ?? "Object");

            if (param.DefaultValue != null)
            {
                sb.Append(" = ");
                sb.Append(param.DefaultValue);
            }

            parts.Add(sb.ToString());
        }

        return string.Join(", ", parts);
    }

    private async Task<(string MethodName, string FilePath, int DefinitionLine, int DefinitionEndLine, bool IsFunction,
                        string? ReturnType, List<ExistingParameterInfo> Parameters, string Signature)?>
        FindMethodDefinitionForParameterAsync(string filePath, string methodName, CancellationToken cancellationToken)
    {
        var content = await _fileService.ReadFileAsync(filePath, cancellationToken);
        var lines = content.Split('\n');

        var result = FindMethodDefinitionInContent(lines, filePath, methodName);
        if (result != null)
            return result;

        // Search in project files if not found in current file
        if (_projectService.CurrentProject != null)
        {
            var sourceFiles = _projectService.CurrentProject.GetSourceFiles();
            foreach (var file in sourceFiles)
            {
                var fullPath = Path.Combine(_projectService.CurrentProject.ProjectDirectory, file.Include);
                if (fullPath != filePath && File.Exists(fullPath))
                {
                    var fileContent = await _fileService.ReadFileAsync(fullPath, cancellationToken);
                    var fileLines = fileContent.Split('\n');
                    result = FindMethodDefinitionInContent(fileLines, fullPath, methodName);
                    if (result != null)
                        return result;
                }
            }
        }

        return null;
    }

    private (string MethodName, string FilePath, int DefinitionLine, int DefinitionEndLine, bool IsFunction,
             string? ReturnType, List<ExistingParameterInfo> Parameters, string Signature)?
        FindMethodDefinitionInContent(string[] lines, string filePath, string methodName)
    {
        var subPattern = new Regex($@"^\s*(Private|Public|Protected|Friend)?\s*(Shared)?\s*(Overridable|Overrides|MustOverride)?\s*(Sub|Function)\s+{Regex.Escape(methodName)}\s*\(([^)]*)\)(\s+As\s+(\w+))?", RegexOptions.IgnoreCase);

        for (var i = 0; i < lines.Length; i++)
        {
            var match = subPattern.Match(lines[i]);
            if (match.Success)
            {
                var isFunction = match.Groups[4].Value.Equals("Function", StringComparison.OrdinalIgnoreCase);
                var parametersText = match.Groups[5].Value;
                var returnType = match.Groups[7].Success ? match.Groups[7].Value : null;

                var parameters = ParseExistingParameters(parametersText);

                // Find end of method
                var endLine = FindEndOfMethod(lines, i, isFunction);

                var signature = BuildMethodSignature(isFunction, methodName, parameters, returnType);

                return (methodName, filePath, i + 1, endLine + 1, isFunction, returnType, parameters, signature);
            }
        }

        return null;
    }

    private string? FindContainingType(string[] lines, int lineIndex)
    {
        // Search backwards for a Class or Module definition
        for (var i = lineIndex - 1; i >= 0; i--)
        {
            var line = lines[i].TrimStart();
            var classMatch = Regex.Match(line, @"^(Public|Private|Friend)?\s*(Class|Module)\s+(\w+)", RegexOptions.IgnoreCase);
            if (classMatch.Success)
            {
                return classMatch.Groups[3].Value;
            }
        }

        return null;
    }

    public async Task<AddParameterResult> AddParameterAsync(string filePath, int line, int column, AddParameterOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            // Get method info
            var methodInfo = await GetMethodForParameterAsync(filePath, line, column, cancellationToken);
            if (methodInfo == null)
            {
                return new AddParameterResult
                {
                    Success = false,
                    ErrorMessage = "Could not find method definition"
                };
            }

            // Validate parameter name
            if (string.IsNullOrWhiteSpace(options.ParameterName))
            {
                return new AddParameterResult
                {
                    Success = false,
                    ErrorMessage = "Parameter name is required"
                };
            }

            // Check for duplicate parameter name
            if (methodInfo.ExistingParameters.Any(p => p.Name.Equals(options.ParameterName, StringComparison.OrdinalIgnoreCase)))
            {
                return new AddParameterResult
                {
                    Success = false,
                    ErrorMessage = $"A parameter named '{options.ParameterName}' already exists"
                };
            }

            var fileEdits = new List<FileEdit>();
            var callSitesUpdated = 0;

            // Update the method definition
            var definitionContent = await _fileService.ReadFileAsync(methodInfo.FilePath, cancellationToken);
            var definitionLines = definitionContent.Split('\n');
            var definitionLine = definitionLines[methodInfo.DefinitionLine - 1];

            // Build new parameter string
            var newParameter = BuildNewParameterString(options);

            // Insert parameter at the correct position
            var newDefinitionLine = InsertParameterIntoSignature(definitionLine, newParameter, options.InsertPosition, methodInfo.ExistingParameters.Count);

            var definitionEdit = new FileEdit
            {
                FilePath = methodInfo.FilePath,
                Edits = new[]
                {
                    new TextEdit
                    {
                        StartLine = methodInfo.DefinitionLine,
                        StartColumn = 1,
                        EndLine = methodInfo.DefinitionLine,
                        EndColumn = definitionLine.Length + 1,
                        NewText = newDefinitionLine
                    }
                }
            };
            fileEdits.Add(definitionEdit);

            // Update call sites if requested
            if (options.UpdateCallSites && methodInfo.CallSites.Count > 0)
            {
                var callSiteValue = options.CallSiteValue ?? GetDefaultValueForType(options.ParameterType);

                // Group call sites by file
                var callSitesByFile = methodInfo.CallSites.GroupBy(cs => cs.FilePath);

                foreach (var fileGroup in callSitesByFile)
                {
                    var callSiteFilePath = fileGroup.Key;
                    var callSiteContent = await _fileService.ReadFileAsync(callSiteFilePath, cancellationToken);
                    var callSiteLines = callSiteContent.Split('\n');

                    var edits = new List<TextEdit>();

                    foreach (var callSite in fileGroup.OrderByDescending(cs => cs.Line))
                    {
                        var callSiteLine = callSiteLines[callSite.Line - 1];
                        var updatedLine = InsertArgumentAtCallSite(callSiteLine, methodInfo.MethodName, callSiteValue, options.InsertPosition, methodInfo.ExistingParameters.Count);

                        if (updatedLine != callSiteLine)
                        {
                            edits.Add(new TextEdit
                            {
                                StartLine = callSite.Line,
                                StartColumn = 1,
                                EndLine = callSite.Line,
                                EndColumn = callSiteLine.Length + 1,
                                NewText = updatedLine
                            });
                            callSitesUpdated++;
                        }
                    }

                    if (edits.Count > 0)
                    {
                        // Check if we already have edits for this file
                        var existingEdit = fileEdits.FirstOrDefault(fe => fe.FilePath == callSiteFilePath);
                        if (existingEdit != null)
                        {
                            var allEdits = existingEdit.Edits.ToList();
                            allEdits.AddRange(edits);
                            fileEdits.Remove(existingEdit);
                            fileEdits.Add(new FileEdit { FilePath = callSiteFilePath, Edits = allEdits });
                        }
                        else
                        {
                            fileEdits.Add(new FileEdit { FilePath = callSiteFilePath, Edits = edits });
                        }
                    }
                }
            }

            // Apply all edits
            foreach (var edit in fileEdits)
            {
                await ApplyFileEditAsync(edit, cancellationToken);
            }

            // Build new signature for result
            var newParameters = new List<ExistingParameterInfo>(methodInfo.ExistingParameters);
            var newParam = new ExistingParameterInfo
            {
                Name = options.ParameterName,
                Type = options.ParameterType,
                IsByRef = options.IsByRef,
                IsOptional = options.IsOptional,
                DefaultValue = options.DefaultValue,
                Index = options.InsertPosition >= 0 ? options.InsertPosition : newParameters.Count
            };

            if (options.InsertPosition >= 0 && options.InsertPosition < newParameters.Count)
            {
                newParameters.Insert(options.InsertPosition, newParam);
            }
            else
            {
                newParameters.Add(newParam);
            }

            var newSignature = BuildMethodSignature(methodInfo.IsFunction, methodInfo.MethodName, newParameters, methodInfo.ReturnType);

            return new AddParameterResult
            {
                Success = true,
                FileEdits = fileEdits,
                CallSitesUpdated = callSitesUpdated,
                NewSignature = newSignature
            };
        }
        catch (Exception ex)
        {
            return new AddParameterResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private string BuildNewParameterString(AddParameterOptions options)
    {
        var sb = new System.Text.StringBuilder();

        if (options.IsOptional)
            sb.Append("Optional ");

        if (options.IsByRef)
            sb.Append("ByRef ");

        sb.Append(options.ParameterName);
        sb.Append(" As ");
        sb.Append(options.ParameterType);

        if (options.DefaultValue != null)
        {
            sb.Append(" = ");
            sb.Append(options.DefaultValue);
        }

        return sb.ToString();
    }

    private string InsertParameterIntoSignature(string line, string newParameter, int position, int existingCount)
    {
        // Find the parameter list: between ( and )
        var openParen = line.IndexOf('(');
        var closeParen = line.LastIndexOf(')');

        if (openParen == -1 || closeParen == -1 || closeParen <= openParen)
            return line;

        var beforeParams = line.Substring(0, openParen + 1);
        var paramsSection = line.Substring(openParen + 1, closeParen - openParen - 1);
        var afterParams = line.Substring(closeParen);

        var existingParams = SplitParameters(paramsSection);

        // Determine actual insert position
        var insertPos = position >= 0 ? Math.Min(position, existingParams.Count) : existingParams.Count;

        // Insert the new parameter
        if (existingParams.Count == 0 || (existingParams.Count == 1 && string.IsNullOrWhiteSpace(existingParams[0])))
        {
            // Empty parameter list
            return beforeParams + newParameter + afterParams;
        }
        else if (insertPos >= existingParams.Count)
        {
            // Add at the end
            return beforeParams + paramsSection.TrimEnd() + ", " + newParameter + afterParams;
        }
        else
        {
            // Insert at specific position
            existingParams.Insert(insertPos, newParameter);
            return beforeParams + string.Join(", ", existingParams.Select(p => p.Trim())) + afterParams;
        }
    }

    private string InsertArgumentAtCallSite(string line, string methodName, string newArgument, int position, int existingCount)
    {
        // Find the method call: methodName(...)
        var pattern = new Regex($@"\b{Regex.Escape(methodName)}\s*\(", RegexOptions.IgnoreCase);
        var match = pattern.Match(line);

        if (!match.Success)
            return line;

        var callStart = match.Index + match.Length - 1; // Position of '('

        // Find matching closing paren
        var depth = 1;
        var closeParen = -1;
        for (var i = callStart + 1; i < line.Length; i++)
        {
            if (line[i] == '(') depth++;
            else if (line[i] == ')')
            {
                depth--;
                if (depth == 0)
                {
                    closeParen = i;
                    break;
                }
            }
        }

        if (closeParen == -1)
            return line;

        var beforeCall = line.Substring(0, callStart + 1);
        var argsSection = line.Substring(callStart + 1, closeParen - callStart - 1);
        var afterCall = line.Substring(closeParen);

        var existingArgs = SplitParameters(argsSection);

        // Determine actual insert position
        var insertPos = position >= 0 ? Math.Min(position, existingArgs.Count) : existingArgs.Count;

        // Insert the new argument
        if (existingArgs.Count == 0 || (existingArgs.Count == 1 && string.IsNullOrWhiteSpace(existingArgs[0])))
        {
            // Empty argument list
            return beforeCall + newArgument + afterCall;
        }
        else if (insertPos >= existingArgs.Count)
        {
            // Add at the end
            return beforeCall + argsSection.TrimEnd() + ", " + newArgument + afterCall;
        }
        else
        {
            // Insert at specific position
            existingArgs.Insert(insertPos, " " + newArgument);
            return beforeCall + string.Join(",", existingArgs.Select(a => a.TrimStart())) + afterCall;
        }
    }

    private async Task ApplyFileEditAsync(FileEdit edit, CancellationToken cancellationToken)
    {
        var content = await _fileService.ReadFileAsync(edit.FilePath, cancellationToken);
        var lines = new List<string>(content.Split('\n'));

        // Apply edits in reverse order (bottom to top) to preserve line numbers
        foreach (var textEdit in edit.Edits.OrderByDescending(e => e.StartLine))
        {
            if (textEdit.StartLine >= 1 && textEdit.StartLine <= lines.Count)
            {
                lines[textEdit.StartLine - 1] = textEdit.NewText;
            }
        }

        var newContent = string.Join("\n", lines);
        await _fileService.WriteFileAsync(edit.FilePath, newContent, cancellationToken);
    }

    #endregion

    #region Remove Parameter

    public async Task<RemoveParameterResult> RemoveParameterAsync(string filePath, int line, int column, RemoveParameterOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            // Get method info (reuse from Add Parameter)
            var methodInfo = await GetMethodForParameterAsync(filePath, line, column, cancellationToken);
            if (methodInfo == null)
            {
                return new RemoveParameterResult
                {
                    Success = false,
                    ErrorMessage = "Could not find method definition"
                };
            }

            // Validate parameter indices
            if (options.ParameterIndices.Count == 0)
            {
                return new RemoveParameterResult
                {
                    Success = false,
                    ErrorMessage = "No parameters selected for removal"
                };
            }

            // Check all indices are valid
            foreach (var index in options.ParameterIndices)
            {
                if (index < 0 || index >= methodInfo.ExistingParameters.Count)
                {
                    return new RemoveParameterResult
                    {
                        Success = false,
                        ErrorMessage = $"Invalid parameter index: {index}"
                    };
                }
            }

            // Check we're not removing all parameters if there are any
            if (options.ParameterIndices.Count == methodInfo.ExistingParameters.Count && methodInfo.ExistingParameters.Count > 0)
            {
                // This is allowed - removing all parameters
            }

            var fileEdits = new List<FileEdit>();
            var callSitesUpdated = 0;

            // Update the method definition
            var definitionContent = await _fileService.ReadFileAsync(methodInfo.FilePath, cancellationToken);
            var definitionLines = definitionContent.Split('\n');
            var definitionLine = definitionLines[methodInfo.DefinitionLine - 1];

            // Remove parameters from signature
            var newDefinitionLine = RemoveParametersFromSignature(definitionLine, options.ParameterIndices);

            var definitionEdit = new FileEdit
            {
                FilePath = methodInfo.FilePath,
                Edits = new[]
                {
                    new TextEdit
                    {
                        StartLine = methodInfo.DefinitionLine,
                        StartColumn = 1,
                        EndLine = methodInfo.DefinitionLine,
                        EndColumn = definitionLine.Length + 1,
                        NewText = newDefinitionLine
                    }
                }
            };
            fileEdits.Add(definitionEdit);

            // Update call sites if requested
            if (options.UpdateCallSites && methodInfo.CallSites.Count > 0)
            {
                // Group call sites by file
                var callSitesByFile = methodInfo.CallSites.GroupBy(cs => cs.FilePath);

                foreach (var fileGroup in callSitesByFile)
                {
                    var callSiteFilePath = fileGroup.Key;
                    var callSiteContent = await _fileService.ReadFileAsync(callSiteFilePath, cancellationToken);
                    var callSiteLines = callSiteContent.Split('\n');

                    var edits = new List<TextEdit>();

                    foreach (var callSite in fileGroup.OrderByDescending(cs => cs.Line))
                    {
                        var callSiteLine = callSiteLines[callSite.Line - 1];
                        var updatedLine = RemoveArgumentsFromCallSite(callSiteLine, methodInfo.MethodName, options.ParameterIndices);

                        if (updatedLine != callSiteLine)
                        {
                            edits.Add(new TextEdit
                            {
                                StartLine = callSite.Line,
                                StartColumn = 1,
                                EndLine = callSite.Line,
                                EndColumn = callSiteLine.Length + 1,
                                NewText = updatedLine
                            });
                            callSitesUpdated++;
                        }
                    }

                    if (edits.Count > 0)
                    {
                        // Check if we already have edits for this file
                        var existingEdit = fileEdits.FirstOrDefault(fe => fe.FilePath == callSiteFilePath);
                        if (existingEdit != null)
                        {
                            var allEdits = existingEdit.Edits.ToList();
                            allEdits.AddRange(edits);
                            fileEdits.Remove(existingEdit);
                            fileEdits.Add(new FileEdit { FilePath = callSiteFilePath, Edits = allEdits });
                        }
                        else
                        {
                            fileEdits.Add(new FileEdit { FilePath = callSiteFilePath, Edits = edits });
                        }
                    }
                }
            }

            // Apply all edits
            foreach (var edit in fileEdits)
            {
                await ApplyFileEditAsync(edit, cancellationToken);
            }

            // Build new signature for result
            var remainingParameters = methodInfo.ExistingParameters
                .Where((p, i) => !options.ParameterIndices.Contains(i))
                .ToList();

            var newSignature = BuildMethodSignature(methodInfo.IsFunction, methodInfo.MethodName, remainingParameters, methodInfo.ReturnType);

            return new RemoveParameterResult
            {
                Success = true,
                FileEdits = fileEdits,
                CallSitesUpdated = callSitesUpdated,
                ParametersRemoved = options.ParameterIndices.Count,
                NewSignature = newSignature
            };
        }
        catch (Exception ex)
        {
            return new RemoveParameterResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private string RemoveParametersFromSignature(string line, List<int> indicesToRemove)
    {
        // Find the parameter list: between ( and )
        var openParen = line.IndexOf('(');
        var closeParen = line.LastIndexOf(')');

        if (openParen == -1 || closeParen == -1 || closeParen <= openParen)
            return line;

        var beforeParams = line.Substring(0, openParen + 1);
        var paramsSection = line.Substring(openParen + 1, closeParen - openParen - 1);
        var afterParams = line.Substring(closeParen);

        var existingParams = SplitParameters(paramsSection);

        // Remove parameters at specified indices (in reverse order to preserve indices)
        var sortedIndices = indicesToRemove.OrderByDescending(i => i).ToList();
        foreach (var index in sortedIndices)
        {
            if (index >= 0 && index < existingParams.Count)
            {
                existingParams.RemoveAt(index);
            }
        }

        // Rebuild parameter list
        var newParamsSection = string.Join(", ", existingParams.Select(p => p.Trim()).Where(p => !string.IsNullOrEmpty(p)));

        return beforeParams + newParamsSection + afterParams;
    }

    private string RemoveArgumentsFromCallSite(string line, string methodName, List<int> indicesToRemove)
    {
        // Find the method call: methodName(...)
        var pattern = new Regex($@"\b{Regex.Escape(methodName)}\s*\(", RegexOptions.IgnoreCase);
        var match = pattern.Match(line);

        if (!match.Success)
            return line;

        var callStart = match.Index + match.Length - 1; // Position of '('

        // Find matching closing paren
        var depth = 1;
        var closeParen = -1;
        for (var i = callStart + 1; i < line.Length; i++)
        {
            if (line[i] == '(') depth++;
            else if (line[i] == ')')
            {
                depth--;
                if (depth == 0)
                {
                    closeParen = i;
                    break;
                }
            }
        }

        if (closeParen == -1)
            return line;

        var beforeCall = line.Substring(0, callStart + 1);
        var argsSection = line.Substring(callStart + 1, closeParen - callStart - 1);
        var afterCall = line.Substring(closeParen);

        var existingArgs = SplitParameters(argsSection);

        // Remove arguments at specified indices (in reverse order to preserve indices)
        var sortedIndices = indicesToRemove.OrderByDescending(i => i).ToList();
        foreach (var index in sortedIndices)
        {
            if (index >= 0 && index < existingArgs.Count)
            {
                existingArgs.RemoveAt(index);
            }
        }

        // Rebuild argument list
        var newArgsSection = string.Join(", ", existingArgs.Select(a => a.Trim()).Where(a => !string.IsNullOrEmpty(a)));

        return beforeCall + newArgsSection + afterCall;
    }

    #endregion

    #region Reorder Parameters

    public async Task<ReorderParametersResult> ReorderParametersAsync(string filePath, int line, int column, ReorderParametersOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            // Get method info (reuse from Add Parameter)
            var methodInfo = await GetMethodForParameterAsync(filePath, line, column, cancellationToken);
            if (methodInfo == null)
            {
                return new ReorderParametersResult
                {
                    Success = false,
                    ErrorMessage = "Could not find method definition"
                };
            }

            // Validate new order
            if (options.NewOrder.Count != methodInfo.ExistingParameters.Count)
            {
                return new ReorderParametersResult
                {
                    Success = false,
                    ErrorMessage = $"New order must contain exactly {methodInfo.ExistingParameters.Count} indices"
                };
            }

            // Check all indices are valid and unique
            var sortedOrder = options.NewOrder.OrderBy(i => i).ToList();
            for (int i = 0; i < sortedOrder.Count; i++)
            {
                if (sortedOrder[i] != i)
                {
                    return new ReorderParametersResult
                    {
                        Success = false,
                        ErrorMessage = "New order must contain each index exactly once"
                    };
                }
            }

            // Check if order actually changed
            bool orderChanged = false;
            for (int i = 0; i < options.NewOrder.Count; i++)
            {
                if (options.NewOrder[i] != i)
                {
                    orderChanged = true;
                    break;
                }
            }

            if (!orderChanged)
            {
                return new ReorderParametersResult
                {
                    Success = true,
                    CallSitesUpdated = 0,
                    NewSignature = methodInfo.Signature
                };
            }

            var fileEdits = new List<FileEdit>();
            var callSitesUpdated = 0;

            // Update the method definition
            var definitionContent = await _fileService.ReadFileAsync(methodInfo.FilePath, cancellationToken);
            var definitionLines = definitionContent.Split('\n');
            var definitionLine = definitionLines[methodInfo.DefinitionLine - 1];

            // Reorder parameters in signature
            var newDefinitionLine = ReorderParametersInSignature(definitionLine, options.NewOrder);

            var definitionEdit = new FileEdit
            {
                FilePath = methodInfo.FilePath,
                Edits = new[]
                {
                    new TextEdit
                    {
                        StartLine = methodInfo.DefinitionLine,
                        StartColumn = 1,
                        EndLine = methodInfo.DefinitionLine,
                        EndColumn = definitionLine.Length + 1,
                        NewText = newDefinitionLine
                    }
                }
            };
            fileEdits.Add(definitionEdit);

            // Update call sites if requested
            if (options.UpdateCallSites && methodInfo.CallSites.Count > 0)
            {
                // Group call sites by file
                var callSitesByFile = methodInfo.CallSites.GroupBy(cs => cs.FilePath);

                foreach (var fileGroup in callSitesByFile)
                {
                    var callSiteFilePath = fileGroup.Key;
                    var callSiteContent = await _fileService.ReadFileAsync(callSiteFilePath, cancellationToken);
                    var callSiteLines = callSiteContent.Split('\n');

                    var edits = new List<TextEdit>();

                    foreach (var callSite in fileGroup.OrderByDescending(cs => cs.Line))
                    {
                        var callSiteLine = callSiteLines[callSite.Line - 1];
                        var updatedLine = ReorderArgumentsInCallSite(callSiteLine, methodInfo.MethodName, options.NewOrder);

                        if (updatedLine != callSiteLine)
                        {
                            edits.Add(new TextEdit
                            {
                                StartLine = callSite.Line,
                                StartColumn = 1,
                                EndLine = callSite.Line,
                                EndColumn = callSiteLine.Length + 1,
                                NewText = updatedLine
                            });
                            callSitesUpdated++;
                        }
                    }

                    if (edits.Count > 0)
                    {
                        // Check if we already have edits for this file
                        var existingEdit = fileEdits.FirstOrDefault(fe => fe.FilePath == callSiteFilePath);
                        if (existingEdit != null)
                        {
                            var allEdits = existingEdit.Edits.ToList();
                            allEdits.AddRange(edits);
                            fileEdits.Remove(existingEdit);
                            fileEdits.Add(new FileEdit { FilePath = callSiteFilePath, Edits = allEdits });
                        }
                        else
                        {
                            fileEdits.Add(new FileEdit { FilePath = callSiteFilePath, Edits = edits });
                        }
                    }
                }
            }

            // Apply all edits
            foreach (var edit in fileEdits)
            {
                await ApplyFileEditAsync(edit, cancellationToken);
            }

            // Build new signature for result
            var reorderedParameters = options.NewOrder
                .Select(i => methodInfo.ExistingParameters[i])
                .ToList();

            var newSignature = BuildMethodSignature(methodInfo.IsFunction, methodInfo.MethodName, reorderedParameters, methodInfo.ReturnType);

            return new ReorderParametersResult
            {
                Success = true,
                FileEdits = fileEdits,
                CallSitesUpdated = callSitesUpdated,
                NewSignature = newSignature
            };
        }
        catch (Exception ex)
        {
            return new ReorderParametersResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private string ReorderParametersInSignature(string line, List<int> newOrder)
    {
        // Find the parameter list: between ( and )
        var openParen = line.IndexOf('(');
        var closeParen = line.LastIndexOf(')');

        if (openParen == -1 || closeParen == -1 || closeParen <= openParen)
            return line;

        var beforeParams = line.Substring(0, openParen + 1);
        var paramsSection = line.Substring(openParen + 1, closeParen - openParen - 1);
        var afterParams = line.Substring(closeParen);

        var existingParams = SplitParameters(paramsSection);

        if (existingParams.Count != newOrder.Count)
            return line;

        // Reorder parameters according to newOrder
        var reorderedParams = newOrder.Select(i => existingParams[i].Trim()).ToList();

        // Rebuild parameter list
        var newParamsSection = string.Join(", ", reorderedParams);

        return beforeParams + newParamsSection + afterParams;
    }

    private string ReorderArgumentsInCallSite(string line, string methodName, List<int> newOrder)
    {
        // Find the method call: methodName(...)
        var pattern = new Regex($@"\b{Regex.Escape(methodName)}\s*\(", RegexOptions.IgnoreCase);
        var match = pattern.Match(line);

        if (!match.Success)
            return line;

        var callStart = match.Index + match.Length - 1; // Position of '('

        // Find matching closing paren
        var depth = 1;
        var closeParen = -1;
        for (var i = callStart + 1; i < line.Length; i++)
        {
            if (line[i] == '(') depth++;
            else if (line[i] == ')')
            {
                depth--;
                if (depth == 0)
                {
                    closeParen = i;
                    break;
                }
            }
        }

        if (closeParen == -1)
            return line;

        var beforeCall = line.Substring(0, callStart + 1);
        var argsSection = line.Substring(callStart + 1, closeParen - callStart - 1);
        var afterCall = line.Substring(closeParen);

        var existingArgs = SplitParameters(argsSection);

        // Only reorder if argument count matches
        if (existingArgs.Count != newOrder.Count)
            return line;

        // Reorder arguments according to newOrder
        var reorderedArgs = newOrder.Select(i => existingArgs[i].Trim()).ToList();

        // Rebuild argument list
        var newArgsSection = string.Join(", ", reorderedArgs);

        return beforeCall + newArgsSection + afterCall;
    }

    #endregion

    #region Rename Parameter

    public async Task<RenameParameterResult> RenameParameterAsync(string filePath, int line, int column, RenameParameterOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            // Get method info (reuse from Add Parameter)
            var methodInfo = await GetMethodForParameterAsync(filePath, line, column, cancellationToken);
            if (methodInfo == null)
            {
                return new RenameParameterResult
                {
                    Success = false,
                    ErrorMessage = "Could not find method definition"
                };
            }

            // Validate parameter index
            if (options.ParameterIndex < 0 || options.ParameterIndex >= methodInfo.ExistingParameters.Count)
            {
                return new RenameParameterResult
                {
                    Success = false,
                    ErrorMessage = $"Invalid parameter index: {options.ParameterIndex}"
                };
            }

            // Validate new name
            if (string.IsNullOrWhiteSpace(options.NewName))
            {
                return new RenameParameterResult
                {
                    Success = false,
                    ErrorMessage = "New parameter name cannot be empty"
                };
            }

            // Check if name is a valid identifier
            if (!IsValidIdentifier(options.NewName))
            {
                return new RenameParameterResult
                {
                    Success = false,
                    ErrorMessage = $"'{options.NewName}' is not a valid identifier"
                };
            }

            var oldParameter = methodInfo.ExistingParameters[options.ParameterIndex];
            var oldName = oldParameter.Name;

            // Check if name actually changed
            if (oldName.Equals(options.NewName, StringComparison.OrdinalIgnoreCase))
            {
                return new RenameParameterResult
                {
                    Success = true,
                    ReferencesUpdated = 0,
                    NewSignature = methodInfo.Signature
                };
            }

            // Check for name conflicts with other parameters
            foreach (var param in methodInfo.ExistingParameters)
            {
                if (param.Name.Equals(options.NewName, StringComparison.OrdinalIgnoreCase) && param != oldParameter)
                {
                    return new RenameParameterResult
                    {
                        Success = false,
                        ErrorMessage = $"A parameter named '{options.NewName}' already exists"
                    };
                }
            }

            var fileEdits = new List<FileEdit>();
            var referencesUpdated = 0;

            // Read the file content
            var fileContent = await _fileService.ReadFileAsync(methodInfo.FilePath, cancellationToken);
            var lines = fileContent.Split('\n');

            var edits = new List<TextEdit>();

            // 1. Update the parameter in the method signature
            var definitionLine = lines[methodInfo.DefinitionLine - 1];
            var newDefinitionLine = RenameParameterInSignature(definitionLine, oldName, options.NewName);

            if (newDefinitionLine != definitionLine)
            {
                edits.Add(new TextEdit
                {
                    StartLine = methodInfo.DefinitionLine,
                    StartColumn = 1,
                    EndLine = methodInfo.DefinitionLine,
                    EndColumn = definitionLine.Length + 1,
                    NewText = newDefinitionLine
                });
                referencesUpdated++;
            }

            // 2. Update all references to the parameter within the method body
            for (int i = methodInfo.DefinitionLine; i < methodInfo.DefinitionEndLine && i < lines.Length; i++)
            {
                var currentLine = lines[i];
                var updatedLine = RenameIdentifierInLine(currentLine, oldName, options.NewName);

                if (updatedLine != currentLine)
                {
                    // Check if we already have an edit for this line (e.g., the signature line)
                    var existingEdit = edits.FirstOrDefault(e => e.StartLine == i + 1);
                    if (existingEdit != null)
                    {
                        // Update the existing edit's new text
                        existingEdit.NewText = RenameIdentifierInLine(existingEdit.NewText, oldName, options.NewName);
                    }
                    else
                    {
                        edits.Add(new TextEdit
                        {
                            StartLine = i + 1,
                            StartColumn = 1,
                            EndLine = i + 1,
                            EndColumn = currentLine.Length + 1,
                            NewText = updatedLine
                        });
                    }
                    referencesUpdated++;
                }
            }

            if (edits.Count > 0)
            {
                fileEdits.Add(new FileEdit
                {
                    FilePath = methodInfo.FilePath,
                    Edits = edits
                });
            }

            // Apply all edits
            foreach (var edit in fileEdits)
            {
                await ApplyFileEditAsync(edit, cancellationToken);
            }

            // Build new signature for result
            var renamedParameters = methodInfo.ExistingParameters.Select((p, i) =>
            {
                if (i == options.ParameterIndex)
                {
                    return new ExistingParameterInfo
                    {
                        Name = options.NewName,
                        Type = p.Type,
                        IsByRef = p.IsByRef,
                        IsOptional = p.IsOptional,
                        DefaultValue = p.DefaultValue,
                        Index = p.Index
                    };
                }
                return p;
            }).ToList();

            var newSignature = BuildMethodSignature(methodInfo.IsFunction, methodInfo.MethodName, renamedParameters, methodInfo.ReturnType);

            return new RenameParameterResult
            {
                Success = true,
                FileEdits = fileEdits,
                ReferencesUpdated = referencesUpdated,
                NewSignature = newSignature
            };
        }
        catch (Exception ex)
        {
            return new RenameParameterResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private string RenameParameterInSignature(string line, string oldName, string newName)
    {
        // Find the parameter list: between ( and )
        var openParen = line.IndexOf('(');
        var closeParen = line.LastIndexOf(')');

        if (openParen == -1 || closeParen == -1 || closeParen <= openParen)
            return line;

        var beforeParams = line.Substring(0, openParen + 1);
        var paramsSection = line.Substring(openParen + 1, closeParen - openParen - 1);
        var afterParams = line.Substring(closeParen);

        // Replace the parameter name in the params section
        // Match pattern: word boundary + oldName + word boundary (before "As" or ",")
        var pattern = $@"\b{Regex.Escape(oldName)}\b(?=\s+As\b|\s*,|\s*\))";
        var newParamsSection = Regex.Replace(paramsSection, pattern, newName, RegexOptions.IgnoreCase);

        return beforeParams + newParamsSection + afterParams;
    }

    private string RenameIdentifierInLine(string line, string oldName, string newName)
    {
        // Replace all occurrences of the identifier with word boundaries
        // But avoid replacing in strings and comments
        var result = new System.Text.StringBuilder();
        var inString = false;
        var inComment = false;
        var stringChar = '\0';
        var i = 0;

        while (i < line.Length)
        {
            // Check for comment start
            if (!inString && i < line.Length - 1 && line[i] == '\'' || (line[i] == 'R' && i + 2 < line.Length && line.Substring(i, 3).Equals("REM", StringComparison.OrdinalIgnoreCase)))
            {
                // Rest of line is a comment
                result.Append(line.Substring(i));
                break;
            }

            // Check for string start/end
            if (!inComment && (line[i] == '"'))
            {
                if (!inString)
                {
                    inString = true;
                    stringChar = line[i];
                }
                else if (line[i] == stringChar)
                {
                    inString = false;
                }
                result.Append(line[i]);
                i++;
                continue;
            }

            // If in string or comment, just append
            if (inString || inComment)
            {
                result.Append(line[i]);
                i++;
                continue;
            }

            // Check if this position starts with the old name
            if (i + oldName.Length <= line.Length)
            {
                var potentialMatch = line.Substring(i, oldName.Length);
                if (potentialMatch.Equals(oldName, StringComparison.OrdinalIgnoreCase))
                {
                    // Check word boundaries
                    var charBefore = i > 0 ? line[i - 1] : ' ';
                    var charAfter = i + oldName.Length < line.Length ? line[i + oldName.Length] : ' ';

                    var isWordBoundaryBefore = !char.IsLetterOrDigit(charBefore) && charBefore != '_';
                    var isWordBoundaryAfter = !char.IsLetterOrDigit(charAfter) && charAfter != '_';

                    if (isWordBoundaryBefore && isWordBoundaryAfter)
                    {
                        result.Append(newName);
                        i += oldName.Length;
                        continue;
                    }
                }
            }

            result.Append(line[i]);
            i++;
        }

        return result.ToString();
    }

    #endregion

    #region Change Parameter Type

    public async Task<ChangeParameterTypeResult> ChangeParameterTypeAsync(string filePath, int line, int column, ChangeParameterTypeOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            // Get method info (reuse from Add Parameter)
            var methodInfo = await GetMethodForParameterAsync(filePath, line, column, cancellationToken);
            if (methodInfo == null)
            {
                return new ChangeParameterTypeResult
                {
                    Success = false,
                    ErrorMessage = "Could not find method definition"
                };
            }

            // Validate parameter index
            if (options.ParameterIndex < 0 || options.ParameterIndex >= methodInfo.ExistingParameters.Count)
            {
                return new ChangeParameterTypeResult
                {
                    Success = false,
                    ErrorMessage = $"Invalid parameter index: {options.ParameterIndex}"
                };
            }

            // Validate new type
            if (string.IsNullOrWhiteSpace(options.NewType))
            {
                return new ChangeParameterTypeResult
                {
                    Success = false,
                    ErrorMessage = "New type cannot be empty"
                };
            }

            var oldParameter = methodInfo.ExistingParameters[options.ParameterIndex];
            var oldType = oldParameter.Type ?? "Object";

            // Check if type actually changed
            if (oldType.Equals(options.NewType, StringComparison.OrdinalIgnoreCase))
            {
                return new ChangeParameterTypeResult
                {
                    Success = true,
                    NewSignature = methodInfo.Signature
                };
            }

            var fileEdits = new List<FileEdit>();

            // Read the file content
            var fileContent = await _fileService.ReadFileAsync(methodInfo.FilePath, cancellationToken);
            var lines = fileContent.Split('\n');

            var edits = new List<TextEdit>();

            // Update the parameter type in the method signature
            var definitionLine = lines[methodInfo.DefinitionLine - 1];
            var newDefinitionLine = ChangeParameterTypeInSignature(definitionLine, oldParameter.Name, options.NewType);

            if (newDefinitionLine != definitionLine)
            {
                edits.Add(new TextEdit
                {
                    StartLine = methodInfo.DefinitionLine,
                    StartColumn = 1,
                    EndLine = methodInfo.DefinitionLine,
                    EndColumn = definitionLine.Length + 1,
                    NewText = newDefinitionLine
                });
            }

            if (edits.Count > 0)
            {
                fileEdits.Add(new FileEdit
                {
                    FilePath = methodInfo.FilePath,
                    Edits = edits
                });
            }

            // Apply all edits
            foreach (var edit in fileEdits)
            {
                await ApplyFileEditAsync(edit, cancellationToken);
            }

            // Build new signature for result
            var updatedParameters = methodInfo.ExistingParameters.Select((p, i) =>
            {
                if (i == options.ParameterIndex)
                {
                    return new ExistingParameterInfo
                    {
                        Name = p.Name,
                        Type = options.NewType,
                        IsByRef = p.IsByRef,
                        IsOptional = p.IsOptional,
                        DefaultValue = p.DefaultValue,
                        Index = p.Index
                    };
                }
                return p;
            }).ToList();

            var newSignature = BuildMethodSignature(methodInfo.IsFunction, methodInfo.MethodName, updatedParameters, methodInfo.ReturnType);

            return new ChangeParameterTypeResult
            {
                Success = true,
                FileEdits = fileEdits,
                NewSignature = newSignature
            };
        }
        catch (Exception ex)
        {
            return new ChangeParameterTypeResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private string ChangeParameterTypeInSignature(string line, string paramName, string newType)
    {
        // Find the parameter list: between ( and )
        var openParen = line.IndexOf('(');
        var closeParen = line.LastIndexOf(')');

        if (openParen == -1 || closeParen == -1 || closeParen <= openParen)
            return line;

        var beforeParams = line.Substring(0, openParen + 1);
        var paramsSection = line.Substring(openParen + 1, closeParen - openParen - 1);
        var afterParams = line.Substring(closeParen);

        // Replace the parameter type in the params section
        // Match pattern: paramName As OldType (with optional modifiers before paramName)
        var pattern = $@"(\b{Regex.Escape(paramName)}\s+As\s+)\w+";
        var newParamsSection = Regex.Replace(paramsSection, pattern, $"$1{newType}", RegexOptions.IgnoreCase);

        return beforeParams + newParamsSection + afterParams;
    }

    #endregion

    #region Make Parameter Optional

    public async Task<MakeParameterOptionalResult> MakeParameterOptionalAsync(string filePath, int line, int column, MakeParameterOptionalOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            // Get method info (reuse from Add Parameter)
            var methodInfo = await GetMethodForParameterAsync(filePath, line, column, cancellationToken);
            if (methodInfo == null)
            {
                return new MakeParameterOptionalResult
                {
                    Success = false,
                    ErrorMessage = "Could not find method definition"
                };
            }

            // Validate parameter index
            if (options.ParameterIndex < 0 || options.ParameterIndex >= methodInfo.ExistingParameters.Count)
            {
                return new MakeParameterOptionalResult
                {
                    Success = false,
                    ErrorMessage = $"Invalid parameter index: {options.ParameterIndex}"
                };
            }

            var targetParameter = methodInfo.ExistingParameters[options.ParameterIndex];

            // Check if parameter is already optional
            if (targetParameter.IsOptional)
            {
                return new MakeParameterOptionalResult
                {
                    Success = false,
                    ErrorMessage = $"Parameter '{targetParameter.Name}' is already optional"
                };
            }

            // Validate default value
            if (string.IsNullOrWhiteSpace(options.DefaultValue))
            {
                return new MakeParameterOptionalResult
                {
                    Success = false,
                    ErrorMessage = "Default value is required for optional parameters"
                };
            }

            // Check if there are non-optional parameters after this one
            var hasNonOptionalAfter = methodInfo.ExistingParameters
                .Skip(options.ParameterIndex + 1)
                .Any(p => !p.IsOptional);

            if (hasNonOptionalAfter)
            {
                return new MakeParameterOptionalResult
                {
                    Success = false,
                    ErrorMessage = "Optional parameters must come after all required parameters. Consider reordering parameters first."
                };
            }

            var fileEdits = new List<FileEdit>();
            var callSitesUpdated = 0;

            // Read the file content
            var fileContent = await _fileService.ReadFileAsync(methodInfo.FilePath, cancellationToken);
            var lines = fileContent.Split('\n');

            var edits = new List<TextEdit>();

            // Update the method signature to make the parameter optional
            var definitionLine = lines[methodInfo.DefinitionLine - 1];
            var newDefinitionLine = MakeParameterOptionalInSignature(definitionLine, targetParameter.Name, options.DefaultValue);

            if (newDefinitionLine != definitionLine)
            {
                edits.Add(new TextEdit
                {
                    StartLine = methodInfo.DefinitionLine,
                    StartColumn = 1,
                    EndLine = methodInfo.DefinitionLine,
                    EndColumn = definitionLine.Length + 1,
                    NewText = newDefinitionLine
                });
            }

            if (edits.Count > 0)
            {
                fileEdits.Add(new FileEdit
                {
                    FilePath = methodInfo.FilePath,
                    Edits = edits
                });
            }

            // Optionally update call sites to remove default arguments
            if (options.RemoveDefaultArgumentsFromCallSites && methodInfo.CallSites.Count > 0)
            {
                var callSiteEdits = await RemoveDefaultArgumentsFromCallSitesAsync(
                    methodInfo,
                    options.ParameterIndex,
                    options.DefaultValue,
                    cancellationToken);

                foreach (var callSiteEdit in callSiteEdits)
                {
                    var existingEdit = fileEdits.FirstOrDefault(e => e.FilePath == callSiteEdit.FilePath);
                    if (existingEdit != null)
                    {
                        existingEdit.Edits = existingEdit.Edits.Concat(callSiteEdit.Edits).ToList();
                    }
                    else
                    {
                        fileEdits.Add(callSiteEdit);
                    }
                    callSitesUpdated += callSiteEdit.Edits.Count;
                }
            }

            // Apply all edits
            foreach (var edit in fileEdits)
            {
                await ApplyFileEditAsync(edit, cancellationToken);
            }

            // Build new signature for result
            var updatedParameters = methodInfo.ExistingParameters.Select((p, i) =>
            {
                if (i == options.ParameterIndex)
                {
                    return new ExistingParameterInfo
                    {
                        Name = p.Name,
                        Type = p.Type,
                        IsByRef = p.IsByRef,
                        IsOptional = true,
                        DefaultValue = options.DefaultValue,
                        Index = p.Index
                    };
                }
                return p;
            }).ToList();

            var newSignature = BuildMethodSignature(methodInfo.IsFunction, methodInfo.MethodName, updatedParameters, methodInfo.ReturnType);

            return new MakeParameterOptionalResult
            {
                Success = true,
                FileEdits = fileEdits,
                CallSitesUpdated = callSitesUpdated,
                NewSignature = newSignature
            };
        }
        catch (Exception ex)
        {
            return new MakeParameterOptionalResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private string MakeParameterOptionalInSignature(string line, string paramName, string defaultValue)
    {
        // Find the parameter list: between ( and )
        var openParen = line.IndexOf('(');
        var closeParen = line.LastIndexOf(')');

        if (openParen == -1 || closeParen == -1 || closeParen <= openParen)
            return line;

        var beforeParams = line.Substring(0, openParen + 1);
        var paramsSection = line.Substring(openParen + 1, closeParen - openParen - 1);
        var afterParams = line.Substring(closeParen);

        // Parse and update the parameter to add Optional keyword and default value
        // Match pattern: [ByRef|ByVal] paramName As Type
        // Need to add "Optional" at the start and "= defaultValue" at the end
        var pattern = $@"(\b(?:ByRef|ByVal)\s+)?({Regex.Escape(paramName)}\s+As\s+\w+)";
        var newParamsSection = Regex.Replace(paramsSection, pattern, match =>
        {
            var byRefPart = match.Groups[1].Value;
            var paramPart = match.Groups[2].Value;

            // Add Optional keyword and default value
            if (!string.IsNullOrEmpty(byRefPart))
            {
                return $"Optional {byRefPart}{paramPart} = {defaultValue}";
            }
            return $"Optional {paramPart} = {defaultValue}";
        }, RegexOptions.IgnoreCase);

        return beforeParams + newParamsSection + afterParams;
    }

    private async Task<List<FileEdit>> RemoveDefaultArgumentsFromCallSitesAsync(
        AddParameterInfo methodInfo,
        int parameterIndex,
        string defaultValue,
        CancellationToken cancellationToken)
    {
        var fileEdits = new List<FileEdit>();
        var editsByFile = new Dictionary<string, List<TextEdit>>();

        foreach (var callSite in methodInfo.CallSites)
        {
            var fileContent = await _fileService.ReadFileAsync(callSite.FilePath, cancellationToken);
            var lines = fileContent.Split('\n');

            if (callSite.Line < 1 || callSite.Line > lines.Length)
                continue;

            var callLine = lines[callSite.Line - 1];
            var newCallLine = RemoveDefaultArgumentFromCallSite(callLine, methodInfo.MethodName, parameterIndex, defaultValue, methodInfo.ExistingParameters.Count);

            if (newCallLine != callLine)
            {
                if (!editsByFile.ContainsKey(callSite.FilePath))
                {
                    editsByFile[callSite.FilePath] = new List<TextEdit>();
                }

                editsByFile[callSite.FilePath].Add(new TextEdit
                {
                    StartLine = callSite.Line,
                    StartColumn = 1,
                    EndLine = callSite.Line,
                    EndColumn = callLine.Length + 1,
                    NewText = newCallLine
                });
            }
        }

        foreach (var kvp in editsByFile)
        {
            fileEdits.Add(new FileEdit
            {
                FilePath = kvp.Key,
                Edits = kvp.Value
            });
        }

        return fileEdits;
    }

    private string RemoveDefaultArgumentFromCallSite(string line, string methodName, int parameterIndex, string defaultValue, int totalParams)
    {
        // Find the method call and its arguments
        var pattern = $@"\b{Regex.Escape(methodName)}\s*\(";
        var match = Regex.Match(line, pattern, RegexOptions.IgnoreCase);

        if (!match.Success)
            return line;

        var callStart = match.Index;
        var argsStart = match.Index + match.Length;

        // Find matching closing parenthesis
        var depth = 1;
        var argsEnd = argsStart;
        while (argsEnd < line.Length && depth > 0)
        {
            if (line[argsEnd] == '(') depth++;
            else if (line[argsEnd] == ')') depth--;
            argsEnd++;
        }
        argsEnd--; // Back to the closing paren

        if (argsEnd <= argsStart)
            return line;

        var argsSection = line.Substring(argsStart, argsEnd - argsStart);
        var args = ParseArgumentList(argsSection);

        // Check if the argument at parameterIndex matches the default value
        if (parameterIndex >= args.Count)
            return line;

        var argValue = args[parameterIndex].Trim();

        // Compare argument value with default value (simple string comparison)
        if (!argValue.Equals(defaultValue, StringComparison.OrdinalIgnoreCase) &&
            !argValue.Equals($"\"{defaultValue}\"", StringComparison.Ordinal) &&
            !$"\"{argValue}\"".Equals(defaultValue, StringComparison.Ordinal))
        {
            return line;
        }

        // Remove the argument if it's at the end
        if (parameterIndex == args.Count - 1)
        {
            args.RemoveAt(parameterIndex);
            var newArgsSection = string.Join(", ", args);
            return line.Substring(0, argsStart) + newArgsSection + line.Substring(argsEnd);
        }

        return line;
    }

    private List<string> ParseArgumentList(string argsSection)
    {
        var args = new List<string>();
        var current = new StringBuilder();
        var depth = 0;
        var inString = false;
        var stringChar = '"';

        foreach (var c in argsSection)
        {
            if (inString)
            {
                current.Append(c);
                if (c == stringChar)
                    inString = false;
            }
            else if (c == '"' || c == '\'')
            {
                current.Append(c);
                inString = true;
                stringChar = c;
            }
            else if (c == '(')
            {
                current.Append(c);
                depth++;
            }
            else if (c == ')')
            {
                current.Append(c);
                depth--;
            }
            else if (c == ',' && depth == 0)
            {
                args.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
        {
            args.Add(current.ToString().Trim());
        }

        return args;
    }

    #endregion

    #region Make Parameter Required

    public async Task<MakeParameterRequiredResult> MakeParameterRequiredAsync(string filePath, int line, int column, MakeParameterRequiredOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            // Get method info (reuse from Add Parameter)
            var methodInfo = await GetMethodForParameterAsync(filePath, line, column, cancellationToken);
            if (methodInfo == null)
            {
                return new MakeParameterRequiredResult
                {
                    Success = false,
                    ErrorMessage = "Could not find method definition"
                };
            }

            // Validate parameter index
            if (options.ParameterIndex < 0 || options.ParameterIndex >= methodInfo.ExistingParameters.Count)
            {
                return new MakeParameterRequiredResult
                {
                    Success = false,
                    ErrorMessage = $"Invalid parameter index: {options.ParameterIndex}"
                };
            }

            var targetParameter = methodInfo.ExistingParameters[options.ParameterIndex];

            // Check if parameter is already required
            if (!targetParameter.IsOptional)
            {
                return new MakeParameterRequiredResult
                {
                    Success = false,
                    ErrorMessage = $"Parameter '{targetParameter.Name}' is already required"
                };
            }

            // Validate call site value is provided
            if (string.IsNullOrWhiteSpace(options.CallSiteValue))
            {
                return new MakeParameterRequiredResult
                {
                    Success = false,
                    ErrorMessage = "A value is required to insert at call sites that omit this argument"
                };
            }

            var fileEdits = new List<FileEdit>();
            var callSitesUpdated = 0;

            // Read the file content
            var fileContent = await _fileService.ReadFileAsync(methodInfo.FilePath, cancellationToken);
            var lines = fileContent.Split('\n');

            var edits = new List<TextEdit>();

            // Update the method signature to make the parameter required
            var definitionLine = lines[methodInfo.DefinitionLine - 1];
            var newDefinitionLine = MakeParameterRequiredInSignature(definitionLine, targetParameter.Name);

            if (newDefinitionLine != definitionLine)
            {
                edits.Add(new TextEdit
                {
                    StartLine = methodInfo.DefinitionLine,
                    StartColumn = 1,
                    EndLine = methodInfo.DefinitionLine,
                    EndColumn = definitionLine.Length + 1,
                    NewText = newDefinitionLine
                });
            }

            if (edits.Count > 0)
            {
                fileEdits.Add(new FileEdit
                {
                    FilePath = methodInfo.FilePath,
                    Edits = edits
                });
            }

            // Update call sites that omit this argument
            if (methodInfo.CallSites.Count > 0)
            {
                var callSiteEdits = await AddMissingArgumentsToCallSitesAsync(
                    methodInfo,
                    options.ParameterIndex,
                    options.CallSiteValue,
                    cancellationToken);

                foreach (var callSiteEdit in callSiteEdits)
                {
                    var existingEdit = fileEdits.FirstOrDefault(e => e.FilePath == callSiteEdit.FilePath);
                    if (existingEdit != null)
                    {
                        existingEdit.Edits = existingEdit.Edits.Concat(callSiteEdit.Edits).ToList();
                    }
                    else
                    {
                        fileEdits.Add(callSiteEdit);
                    }
                    callSitesUpdated += callSiteEdit.Edits.Count;
                }
            }

            // Apply all edits
            foreach (var edit in fileEdits)
            {
                await ApplyFileEditAsync(edit, cancellationToken);
            }

            // Build new signature for result
            var updatedParameters = methodInfo.ExistingParameters.Select((p, i) =>
            {
                if (i == options.ParameterIndex)
                {
                    return new ExistingParameterInfo
                    {
                        Name = p.Name,
                        Type = p.Type,
                        IsByRef = p.IsByRef,
                        IsOptional = false,
                        DefaultValue = null,
                        Index = p.Index
                    };
                }
                return p;
            }).ToList();

            var newSignature = BuildMethodSignature(methodInfo.IsFunction, methodInfo.MethodName, updatedParameters, methodInfo.ReturnType);

            return new MakeParameterRequiredResult
            {
                Success = true,
                FileEdits = fileEdits,
                CallSitesUpdated = callSitesUpdated,
                NewSignature = newSignature
            };
        }
        catch (Exception ex)
        {
            return new MakeParameterRequiredResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private string MakeParameterRequiredInSignature(string line, string paramName)
    {
        // Find the parameter list: between ( and )
        var openParen = line.IndexOf('(');
        var closeParen = line.LastIndexOf(')');

        if (openParen == -1 || closeParen == -1 || closeParen <= openParen)
            return line;

        var beforeParams = line.Substring(0, openParen + 1);
        var paramsSection = line.Substring(openParen + 1, closeParen - openParen - 1);
        var afterParams = line.Substring(closeParen);

        // Remove Optional keyword and default value from the parameter
        // Pattern: Optional [ByRef|ByVal] paramName As Type = defaultValue
        var pattern = $@"Optional\s+((?:ByRef|ByVal)\s+)?({Regex.Escape(paramName)}\s+As\s+\w+)(?:\s*=\s*[^,)]+)?";
        var newParamsSection = Regex.Replace(paramsSection, pattern, match =>
        {
            var byRefPart = match.Groups[1].Value;
            var paramPart = match.Groups[2].Value;

            // Remove Optional keyword and default value
            if (!string.IsNullOrEmpty(byRefPart))
            {
                return $"{byRefPart}{paramPart}";
            }
            return paramPart;
        }, RegexOptions.IgnoreCase);

        return beforeParams + newParamsSection + afterParams;
    }

    private async Task<List<FileEdit>> AddMissingArgumentsToCallSitesAsync(
        AddParameterInfo methodInfo,
        int parameterIndex,
        string callSiteValue,
        CancellationToken cancellationToken)
    {
        var fileEdits = new List<FileEdit>();
        var editsByFile = new Dictionary<string, List<TextEdit>>();

        foreach (var callSite in methodInfo.CallSites)
        {
            var fileContent = await _fileService.ReadFileAsync(callSite.FilePath, cancellationToken);
            var lines = fileContent.Split('\n');

            if (callSite.Line < 1 || callSite.Line > lines.Length)
                continue;

            var callLine = lines[callSite.Line - 1];
            var newCallLine = AddMissingArgumentToCallSite(callLine, methodInfo.MethodName, parameterIndex, callSiteValue, methodInfo.ExistingParameters.Count);

            if (newCallLine != callLine)
            {
                if (!editsByFile.ContainsKey(callSite.FilePath))
                {
                    editsByFile[callSite.FilePath] = new List<TextEdit>();
                }

                editsByFile[callSite.FilePath].Add(new TextEdit
                {
                    StartLine = callSite.Line,
                    StartColumn = 1,
                    EndLine = callSite.Line,
                    EndColumn = callLine.Length + 1,
                    NewText = newCallLine
                });
            }
        }

        foreach (var kvp in editsByFile)
        {
            fileEdits.Add(new FileEdit
            {
                FilePath = kvp.Key,
                Edits = kvp.Value
            });
        }

        return fileEdits;
    }

    private string AddMissingArgumentToCallSite(string line, string methodName, int parameterIndex, string callSiteValue, int totalParams)
    {
        // Find the method call and its arguments
        var pattern = $@"\b{Regex.Escape(methodName)}\s*\(";
        var match = Regex.Match(line, pattern, RegexOptions.IgnoreCase);

        if (!match.Success)
            return line;

        var argsStart = match.Index + match.Length;

        // Find matching closing parenthesis
        var depth = 1;
        var argsEnd = argsStart;
        while (argsEnd < line.Length && depth > 0)
        {
            if (line[argsEnd] == '(') depth++;
            else if (line[argsEnd] == ')') depth--;
            argsEnd++;
        }
        argsEnd--; // Back to the closing paren

        if (argsEnd < argsStart)
            return line;

        var argsSection = line.Substring(argsStart, argsEnd - argsStart);
        var args = ParseArgumentList(argsSection);

        // If the call site already has enough arguments, no need to add
        if (args.Count > parameterIndex)
            return line;

        // Need to add the argument at the correct position
        // Fill in any missing arguments before this one with their default values if needed
        while (args.Count < parameterIndex)
        {
            // For now, we'll just add empty placeholders - this is a simplification
            // In a real implementation, you'd need to know the default values
            args.Add("Nothing");
        }

        // Add the new required argument
        args.Add(callSiteValue);

        var newArgsSection = string.Join(", ", args);
        return line.Substring(0, argsStart) + newArgsSection + line.Substring(argsEnd);
    }

    #endregion

    #region Convert To Named Arguments

    public async Task<CallSiteInfo?> GetCallSiteInfoAsync(string filePath, int line, int column, CancellationToken cancellationToken = default)
    {
        try
        {
            var fileContent = await _fileService.ReadFileAsync(filePath, cancellationToken);
            var lines = fileContent.Split('\n');

            if (line < 1 || line > lines.Length)
                return null;

            var currentLine = lines[line - 1];

            // Find method call on this line
            var callMatch = FindMethodCallAtPosition(currentLine, column);
            if (callMatch == null)
                return null;

            var (methodName, argsStart, argsEnd, argsSection) = callMatch.Value;

            // Parse arguments
            var args = ParseArgumentListWithDetails(argsSection);

            // Try to find the method definition to get parameter names
            var methodInfo = await FindMethodDefinitionAsync(filePath, methodName, args.Count, cancellationToken);

            var callSiteInfo = new CallSiteInfo
            {
                MethodName = methodName,
                FilePath = filePath,
                Line = line,
                Column = column,
                OriginalCall = currentLine.Trim(),
                HasNamedArguments = args.Any(a => a.IsNamed)
            };

            // Map arguments to parameters
            for (int i = 0; i < args.Count; i++)
            {
                var arg = args[i];
                var paramName = methodInfo?.ExistingParameters.ElementAtOrDefault(i)?.Name ?? $"param{i + 1}";
                var paramType = methodInfo?.ExistingParameters.ElementAtOrDefault(i)?.Type;

                callSiteInfo.Arguments.Add(new CallSiteArgumentInfo
                {
                    Index = i,
                    ParameterName = arg.IsNamed ? arg.Name : paramName,
                    ParameterType = paramType,
                    Value = arg.Value,
                    IsNamed = arg.IsNamed,
                    IsSelected = !arg.IsNamed // Select only positional arguments by default
                });
            }

            return callSiteInfo;
        }
        catch
        {
            return null;
        }
    }

    public async Task<ConvertToNamedArgumentsResult> ConvertToNamedArgumentsAsync(string filePath, int line, int column, ConvertToNamedArgumentsOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            var callSiteInfo = await GetCallSiteInfoAsync(filePath, line, column, cancellationToken);
            if (callSiteInfo == null)
            {
                return new ConvertToNamedArgumentsResult
                {
                    Success = false,
                    ErrorMessage = "Could not find method call at cursor position"
                };
            }

            if (callSiteInfo.Arguments.Count == 0)
            {
                return new ConvertToNamedArgumentsResult
                {
                    Success = false,
                    ErrorMessage = "Method call has no arguments to convert"
                };
            }

            // Check if all arguments are already named
            if (callSiteInfo.Arguments.All(a => a.IsNamed))
            {
                return new ConvertToNamedArgumentsResult
                {
                    Success = false,
                    ErrorMessage = "All arguments are already named"
                };
            }

            // Determine which arguments to convert
            var indicesToConvert = options.ConvertAll
                ? callSiteInfo.Arguments.Where(a => !a.IsNamed).Select(a => a.Index).ToList()
                : options.ArgumentIndices.Where(i => i >= 0 && i < callSiteInfo.Arguments.Count && !callSiteInfo.Arguments[i].IsNamed).ToList();

            if (indicesToConvert.Count == 0)
            {
                return new ConvertToNamedArgumentsResult
                {
                    Success = true,
                    NewCallSite = callSiteInfo.OriginalCall,
                    ArgumentsConverted = 0
                };
            }

            // Read file and generate new call
            var fileContent = await _fileService.ReadFileAsync(filePath, cancellationToken);
            var lines = fileContent.Split('\n');
            var currentLine = lines[line - 1];

            var newLine = ConvertArgumentsToNamed(currentLine, callSiteInfo.MethodName, callSiteInfo.Arguments, indicesToConvert);

            if (newLine == currentLine)
            {
                return new ConvertToNamedArgumentsResult
                {
                    Success = true,
                    NewCallSite = currentLine.Trim(),
                    ArgumentsConverted = 0
                };
            }

            var fileEdit = new FileEdit
            {
                FilePath = filePath,
                Edits = new List<TextEdit>
                {
                    new TextEdit
                    {
                        StartLine = line,
                        StartColumn = 1,
                        EndLine = line,
                        EndColumn = currentLine.Length + 1,
                        NewText = newLine
                    }
                }
            };

            // Apply the edit
            await ApplyFileEditAsync(fileEdit, cancellationToken);

            return new ConvertToNamedArgumentsResult
            {
                Success = true,
                FileEdit = fileEdit,
                NewCallSite = newLine.Trim(),
                ArgumentsConverted = indicesToConvert.Count
            };
        }
        catch (Exception ex)
        {
            return new ConvertToNamedArgumentsResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private (string methodName, int argsStart, int argsEnd, string argsSection)? FindMethodCallAtPosition(string line, int column)
    {
        // Find method calls in the line: MethodName(args)
        var pattern = @"\b([A-Za-z_]\w*)\s*\(";
        var matches = Regex.Matches(line, pattern);

        foreach (Match match in matches)
        {
            var methodName = match.Groups[1].Value;
            var openParenIndex = match.Index + match.Length - 1;

            // Find matching close paren
            var depth = 1;
            var closeParenIndex = openParenIndex + 1;
            var inString = false;
            var stringChar = '"';

            while (closeParenIndex < line.Length && depth > 0)
            {
                var c = line[closeParenIndex];
                if (inString)
                {
                    if (c == stringChar)
                        inString = false;
                }
                else if (c == '"' || c == '\'')
                {
                    inString = true;
                    stringChar = c;
                }
                else if (c == '(')
                {
                    depth++;
                }
                else if (c == ')')
                {
                    depth--;
                }
                closeParenIndex++;
            }

            if (depth == 0)
            {
                closeParenIndex--; // Back to the )

                // Check if column is within this call
                if (column >= match.Index + 1 && column <= closeParenIndex + 1)
                {
                    var argsSection = line.Substring(openParenIndex + 1, closeParenIndex - openParenIndex - 1);
                    return (methodName, openParenIndex + 1, closeParenIndex, argsSection);
                }
            }
        }

        // If no match at column, return the first call on the line
        if (matches.Count > 0)
        {
            var match = matches[0];
            var methodName = match.Groups[1].Value;
            var openParenIndex = match.Index + match.Length - 1;

            var depth = 1;
            var closeParenIndex = openParenIndex + 1;
            var inString = false;
            var stringChar = '"';

            while (closeParenIndex < line.Length && depth > 0)
            {
                var c = line[closeParenIndex];
                if (inString)
                {
                    if (c == stringChar)
                        inString = false;
                }
                else if (c == '"' || c == '\'')
                {
                    inString = true;
                    stringChar = c;
                }
                else if (c == '(')
                {
                    depth++;
                }
                else if (c == ')')
                {
                    depth--;
                }
                closeParenIndex++;
            }

            if (depth == 0)
            {
                closeParenIndex--;
                var argsSection = line.Substring(openParenIndex + 1, closeParenIndex - openParenIndex - 1);
                return (methodName, openParenIndex + 1, closeParenIndex, argsSection);
            }
        }

        return null;
    }

    private List<(string Name, string Value, bool IsNamed)> ParseArgumentListWithDetails(string argsSection)
    {
        var args = new List<(string Name, string Value, bool IsNamed)>();
        if (string.IsNullOrWhiteSpace(argsSection))
            return args;

        var current = new StringBuilder();
        var depth = 0;
        var inString = false;
        var stringChar = '"';

        foreach (var c in argsSection)
        {
            if (inString)
            {
                current.Append(c);
                if (c == stringChar)
                    inString = false;
            }
            else if (c == '"' || c == '\'')
            {
                current.Append(c);
                inString = true;
                stringChar = c;
            }
            else if (c == '(')
            {
                current.Append(c);
                depth++;
            }
            else if (c == ')')
            {
                current.Append(c);
                depth--;
            }
            else if (c == ',' && depth == 0)
            {
                var arg = current.ToString().Trim();
                args.Add(ParseSingleArgument(arg));
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
        {
            var arg = current.ToString().Trim();
            args.Add(ParseSingleArgument(arg));
        }

        return args;
    }

    private (string Name, string Value, bool IsNamed) ParseSingleArgument(string arg)
    {
        // Check for named argument: paramName:=value or paramName:value
        var namedMatch = Regex.Match(arg, @"^(\w+)\s*:=?\s*(.+)$");
        if (namedMatch.Success)
        {
            return (namedMatch.Groups[1].Value, namedMatch.Groups[2].Value.Trim(), true);
        }

        return ("", arg, false);
    }

    private async Task<AddParameterInfo?> FindMethodDefinitionAsync(string filePath, string methodName, int argCount, CancellationToken cancellationToken)
    {
        try
        {
            // Search in current file first
            var fileContent = await _fileService.ReadFileAsync(filePath, cancellationToken);
            var lines = fileContent.Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                // Match Sub or Function definition
                var match = Regex.Match(line, $@"\b(Sub|Function)\s+{Regex.Escape(methodName)}\s*\(", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    // Found a potential match - get method info
                    return await GetMethodForParameterAsync(filePath, i + 1, match.Index + 1, cancellationToken);
                }
            }

            // Search in project files
            var project = _projectService.CurrentProject;
            if (project != null)
            {
                var sourceFiles = project.GetSourceFiles();
                foreach (var sourceFile in sourceFiles)
                {
                    var fullPath = Path.Combine(project.ProjectDirectory, sourceFile.Include);
                    if (fullPath == filePath) continue;

                    try
                    {
                        var content = await _fileService.ReadFileAsync(fullPath, cancellationToken);
                        var fileLines = content.Split('\n');

                        for (int j = 0; j < fileLines.Length; j++)
                        {
                            var fileLine = fileLines[j];
                            var fileMatch = Regex.Match(fileLine, $@"\b(Sub|Function)\s+{Regex.Escape(methodName)}\s*\(", RegexOptions.IgnoreCase);
                            if (fileMatch.Success)
                            {
                                return await GetMethodForParameterAsync(fullPath, j + 1, fileMatch.Index + 1, cancellationToken);
                            }
                        }
                    }
                    catch
                    {
                        // Continue searching other files
                    }
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private string ConvertArgumentsToNamed(string line, string methodName, List<CallSiteArgumentInfo> arguments, List<int> indicesToConvert)
    {
        // Find the method call
        var pattern = $@"\b{Regex.Escape(methodName)}\s*\(";
        var match = Regex.Match(line, pattern, RegexOptions.IgnoreCase);

        if (!match.Success)
            return line;

        var argsStart = match.Index + match.Length;

        // Find matching close paren
        var depth = 1;
        var argsEnd = argsStart;
        var inString = false;
        var stringChar = '"';

        while (argsEnd < line.Length && depth > 0)
        {
            var c = line[argsEnd];
            if (inString)
            {
                if (c == stringChar)
                    inString = false;
            }
            else if (c == '"' || c == '\'')
            {
                inString = true;
                stringChar = c;
            }
            else if (c == '(')
            {
                depth++;
            }
            else if (c == ')')
            {
                depth--;
            }
            argsEnd++;
        }
        argsEnd--; // Back to )

        // Build new arguments list
        var newArgs = new List<string>();
        foreach (var arg in arguments)
        {
            if (indicesToConvert.Contains(arg.Index) && !arg.IsNamed)
            {
                // Convert to named argument using := syntax (VB style)
                newArgs.Add($"{arg.ParameterName}:={arg.Value}");
            }
            else if (arg.IsNamed)
            {
                // Keep existing named argument
                newArgs.Add($"{arg.ParameterName}:={arg.Value}");
            }
            else
            {
                // Keep positional argument
                newArgs.Add(arg.Value);
            }
        }

        var newArgsSection = string.Join(", ", newArgs);
        return line.Substring(0, argsStart) + newArgsSection + line.Substring(argsEnd);
    }

    #endregion

    #region Convert To Positional Arguments

    public async Task<ConvertToPositionalArgumentsResult> ConvertToPositionalArgumentsAsync(
        string filePath, int line, int column, ConvertToPositionalArgumentsOptions options,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get call site info
            var callSiteInfo = await GetCallSiteInfoAsync(filePath, line, column, cancellationToken);
            if (callSiteInfo == null)
            {
                return new ConvertToPositionalArgumentsResult
                {
                    Success = false,
                    ErrorMessage = "Could not find method call at cursor position"
                };
            }

            // Check if there are any named arguments to convert
            if (!callSiteInfo.HasNamedArguments)
            {
                return new ConvertToPositionalArgumentsResult
                {
                    Success = false,
                    ErrorMessage = "No named arguments to convert"
                };
            }

            // Determine which arguments to convert
            var indicesToConvert = new List<int>();
            if (options.ConvertAll)
            {
                indicesToConvert = callSiteInfo.Arguments
                    .Where(a => a.IsNamed)
                    .Select(a => a.Index)
                    .ToList();
            }
            else
            {
                indicesToConvert = options.ArgumentIndices
                    .Where(i => i >= 0 && i < callSiteInfo.Arguments.Count && callSiteInfo.Arguments[i].IsNamed)
                    .ToList();
            }

            if (!indicesToConvert.Any())
            {
                return new ConvertToPositionalArgumentsResult
                {
                    Success = false,
                    ErrorMessage = "No valid named arguments selected for conversion"
                };
            }

            // Read the file content
            var content = await _fileService.ReadFileAsync(filePath, cancellationToken);
            var lines = content.Split('\n');

            if (line < 1 || line > lines.Length)
            {
                return new ConvertToPositionalArgumentsResult
                {
                    Success = false,
                    ErrorMessage = "Invalid line number"
                };
            }

            var currentLine = lines[line - 1];

            // Convert arguments to positional - this requires reordering if out of order
            var newCallLine = ConvertArgumentsToPositional(currentLine, callSiteInfo.MethodName, callSiteInfo.Arguments, indicesToConvert);

            // Apply the edit
            lines[line - 1] = newCallLine;
            var newContent = string.Join("\n", lines);
            await _fileService.WriteFileAsync(filePath, newContent, cancellationToken);

            // Extract the new call site for preview
            var newCallSite = ExtractMethodCall(newCallLine, callSiteInfo.MethodName);

            return new ConvertToPositionalArgumentsResult
            {
                Success = true,
                FileEdit = new FileEdit
                {
                    FilePath = filePath,
                    Edits = new[]
                    {
                        new TextEdit
                        {
                            StartLine = line,
                            StartColumn = 1,
                            EndLine = line,
                            EndColumn = currentLine.Length + 1,
                            NewText = newCallLine
                        }
                    }
                },
                NewCallSite = newCallSite ?? callSiteInfo.OriginalCall,
                ArgumentsConverted = indicesToConvert.Count
            };
        }
        catch (Exception ex)
        {
            return new ConvertToPositionalArgumentsResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private string ConvertArgumentsToPositional(string line, string methodName, List<CallSiteArgumentInfo> arguments, List<int> indicesToConvert)
    {
        // Find the method call and argument list in the line
        var methodPattern = $@"\b{Regex.Escape(methodName)}\s*\(";
        var match = Regex.Match(line, methodPattern, RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return line;
        }

        // Find the argument list boundaries
        var argsStart = match.Index + match.Length;
        var parenDepth = 1;
        var argsEnd = argsStart;

        for (int i = argsStart; i < line.Length && parenDepth > 0; i++)
        {
            if (line[i] == '(') parenDepth++;
            else if (line[i] == ')') parenDepth--;
            argsEnd = i + 1;
        }
        argsEnd--; // Back to )

        // Sort arguments by their index (parameter position) to ensure correct order
        var sortedArgs = arguments.OrderBy(a => a.Index).ToList();

        // Build new arguments list - all positional (in order)
        var newArgs = new List<string>();
        foreach (var arg in sortedArgs)
        {
            if (indicesToConvert.Contains(arg.Index) && arg.IsNamed)
            {
                // Convert to positional argument - just use the value
                newArgs.Add(arg.Value);
            }
            else if (arg.IsNamed)
            {
                // Keep existing named argument (wasn't selected for conversion)
                newArgs.Add($"{arg.ParameterName}:={arg.Value}");
            }
            else
            {
                // Keep positional argument
                newArgs.Add(arg.Value);
            }
        }

        var newArgsSection = string.Join(", ", newArgs);
        return line.Substring(0, argsStart) + newArgsSection + line.Substring(argsEnd);
    }

    private string? ExtractMethodCall(string line, string methodName)
    {
        var methodPattern = $@"\b{Regex.Escape(methodName)}\s*\([^)]*\)";
        var match = Regex.Match(line, methodPattern, RegexOptions.IgnoreCase);
        return match.Success ? match.Value : null;
    }

    #endregion

    #region Inline Variable

    public async Task<LocalVariableInfo?> GetVariableInfoAsync(string filePath, int line, int column, CancellationToken cancellationToken = default)
    {
        try
        {
            var content = await _fileService.ReadFileAsync(filePath, cancellationToken);
            var lines = content.Split('\n');

            if (line < 1 || line > lines.Length)
            {
                return null;
            }

            var currentLine = lines[line - 1];

            // Get the word at the cursor position
            var variableName = GetWordAtPosition(currentLine, column);
            if (string.IsNullOrEmpty(variableName))
            {
                return null;
            }

            // Find the variable declaration
            var declarationInfo = FindVariableDeclaration(lines, variableName, line);
            if (declarationInfo == null)
            {
                return null;
            }

            // Find all usages of the variable
            var usages = FindVariableUsages(lines, variableName, declarationInfo.ScopeStartLine, declarationInfo.ScopeEndLine, declarationInfo.DeclarationLine);

            // Check if the variable is reassigned
            var isReassigned = CheckIfReassigned(lines, variableName, declarationInfo.DeclarationLine, declarationInfo.ScopeEndLine);

            return new LocalVariableInfo
            {
                Name = variableName,
                Type = declarationInfo.Type,
                InitializerExpression = declarationInfo.Initializer,
                FilePath = filePath,
                DeclarationLine = declarationInfo.DeclarationLine,
                DeclarationColumn = declarationInfo.Column,
                DeclarationEndColumn = declarationInfo.EndColumn,
                DeclarationText = declarationInfo.FullDeclaration,
                Usages = usages,
                ContainingMethod = declarationInfo.ContainingMethod,
                IsParameter = declarationInfo.IsParameter,
                IsField = declarationInfo.IsField,
                HasInitializer = !string.IsNullOrEmpty(declarationInfo.Initializer),
                IsReassigned = isReassigned
            };
        }
        catch
        {
            return null;
        }
    }

    public async Task<InlineVariableResult> InlineVariableAsync(string filePath, int line, int column, InlineVariableOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            var variableInfo = await GetVariableInfoAsync(filePath, line, column, cancellationToken);
            if (variableInfo == null)
            {
                return new InlineVariableResult
                {
                    Success = false,
                    ErrorMessage = "Could not find variable at cursor position"
                };
            }

            if (!variableInfo.HasInitializer)
            {
                return new InlineVariableResult
                {
                    Success = false,
                    ErrorMessage = "Variable has no initializer expression to inline"
                };
            }

            if (variableInfo.IsParameter)
            {
                return new InlineVariableResult
                {
                    Success = false,
                    ErrorMessage = "Cannot inline parameters"
                };
            }

            if (variableInfo.IsField)
            {
                return new InlineVariableResult
                {
                    Success = false,
                    ErrorMessage = "Cannot inline fields. Use 'Inline Field' refactoring instead."
                };
            }

            if (variableInfo.IsReassigned)
            {
                return new InlineVariableResult
                {
                    Success = false,
                    ErrorMessage = "Cannot inline variable that is reassigned after declaration"
                };
            }

            if (variableInfo.UsageCount == 0)
            {
                return new InlineVariableResult
                {
                    Success = false,
                    ErrorMessage = "Variable is not used anywhere. Consider removing it instead."
                };
            }

            // Read the file content
            var content = await _fileService.ReadFileAsync(filePath, cancellationToken);
            var lines = content.Split('\n').ToList();

            var expression = variableInfo.InitializerExpression;

            // Replace all usages with the expression (in reverse order to maintain line numbers)
            var sortedUsages = variableInfo.Usages
                .OrderByDescending(u => u.Line)
                .ThenByDescending(u => u.Column)
                .ToList();

            foreach (var usage in sortedUsages)
            {
                var usageLine = lines[usage.Line - 1];
                var replacementExpr = expression;

                // Add parentheses if needed (when the expression contains operators and is used in a context that might need them)
                if (options.AddParenthesesIfNeeded && NeedsParentheses(usageLine, usage.Column, expression))
                {
                    replacementExpr = $"({expression})";
                }

                // Replace the variable name with the expression
                var before = usageLine.Substring(0, usage.Column - 1);
                var after = usageLine.Substring(usage.Column - 1 + variableInfo.Name.Length);
                lines[usage.Line - 1] = before + replacementExpr + after;
            }

            // Remove the declaration if requested
            var declarationRemoved = false;
            if (options.RemoveDeclaration)
            {
                var declLine = variableInfo.DeclarationLine - 1;
                var declLineText = lines[declLine];

                // Check if this is the only declaration on the line
                if (IsOnlyDeclarationOnLine(declLineText, variableInfo.Name))
                {
                    // Remove the entire line
                    lines.RemoveAt(declLine);
                    declarationRemoved = true;
                }
                else
                {
                    // Multiple declarations on line - just remove this one
                    lines[declLine] = RemoveVariableFromDeclarationLine(declLineText, variableInfo.Name);
                    declarationRemoved = true;
                }
            }

            // Write the modified content
            var newContent = string.Join("\n", lines);
            await _fileService.WriteFileAsync(filePath, newContent, cancellationToken);

            return new InlineVariableResult
            {
                Success = true,
                FileEdit = new FileEdit
                {
                    FilePath = filePath,
                    Edits = Array.Empty<TextEdit>() // Full file replacement
                },
                UsagesReplaced = variableInfo.UsageCount,
                DeclarationRemoved = declarationRemoved,
                VariableName = variableInfo.Name,
                InlinedExpression = expression
            };
        }
        catch (Exception ex)
        {
            return new InlineVariableResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private class VariableDeclarationInfo
    {
        public int DeclarationLine { get; set; }
        public int Column { get; set; }
        public int EndColumn { get; set; }
        public string? Type { get; set; }
        public string Initializer { get; set; } = "";
        public string FullDeclaration { get; set; } = "";
        public string? ContainingMethod { get; set; }
        public int ScopeStartLine { get; set; }
        public int ScopeEndLine { get; set; }
        public bool IsParameter { get; set; }
        public bool IsField { get; set; }
    }

    private VariableDeclarationInfo? FindVariableDeclaration(string[] lines, string variableName, int currentLine)
    {
        // Search backwards from current line to find the declaration
        string? containingMethod = null;
        int scopeStartLine = 1;
        int scopeEndLine = lines.Length;

        // First, find the containing method/sub
        for (int i = currentLine - 1; i >= 0; i--)
        {
            var line = lines[i].Trim();
            var methodMatch = Regex.Match(line, @"^\s*(?:Public\s+|Private\s+)?(?:Shared\s+)?(Sub|Function)\s+(\w+)", RegexOptions.IgnoreCase);
            if (methodMatch.Success)
            {
                containingMethod = methodMatch.Groups[2].Value;
                scopeStartLine = i + 1;

                // Find the end of this method
                for (int j = i + 1; j < lines.Length; j++)
                {
                    var endLine = lines[j].Trim();
                    if (Regex.IsMatch(endLine, @"^\s*End\s+(Sub|Function)\s*$", RegexOptions.IgnoreCase))
                    {
                        scopeEndLine = j + 1;
                        break;
                    }
                }
                break;
            }
        }

        // Search for Dim declaration
        for (int i = currentLine - 1; i >= scopeStartLine - 1; i--)
        {
            var line = lines[i];

            // Match: Dim variableName As Type = expression
            // Or: Dim variableName = expression
            var dimPattern = $@"\bDim\s+{Regex.Escape(variableName)}\s*(?:As\s+(\w+))?\s*=\s*(.+)$";
            var dimMatch = Regex.Match(line, dimPattern, RegexOptions.IgnoreCase);
            if (dimMatch.Success)
            {
                var varNameStart = line.IndexOf(variableName, StringComparison.OrdinalIgnoreCase);
                return new VariableDeclarationInfo
                {
                    DeclarationLine = i + 1,
                    Column = varNameStart + 1,
                    EndColumn = varNameStart + variableName.Length + 1,
                    Type = dimMatch.Groups[1].Success ? dimMatch.Groups[1].Value : null,
                    Initializer = dimMatch.Groups[2].Value.Trim().TrimEnd('\r'),
                    FullDeclaration = line.Trim(),
                    ContainingMethod = containingMethod,
                    ScopeStartLine = scopeStartLine,
                    ScopeEndLine = scopeEndLine,
                    IsParameter = false,
                    IsField = false
                };
            }

            // Check for parameter in method signature
            if (i == scopeStartLine - 1 && containingMethod != null)
            {
                var paramPattern = $@"\b{Regex.Escape(variableName)}\s*(?:As\s+\w+)?(?:\s*=\s*[^,)]+)?";
                if (Regex.IsMatch(line, paramPattern, RegexOptions.IgnoreCase) &&
                    line.Contains("(") && line.Contains(variableName))
                {
                    return new VariableDeclarationInfo
                    {
                        DeclarationLine = i + 1,
                        Column = line.IndexOf(variableName, StringComparison.OrdinalIgnoreCase) + 1,
                        EndColumn = line.IndexOf(variableName, StringComparison.OrdinalIgnoreCase) + variableName.Length + 1,
                        Type = null,
                        Initializer = "",
                        FullDeclaration = line.Trim(),
                        ContainingMethod = containingMethod,
                        ScopeStartLine = scopeStartLine,
                        ScopeEndLine = scopeEndLine,
                        IsParameter = true,
                        IsField = false
                    };
                }
            }
        }

        // Check for field (class-level variable)
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var fieldPattern = $@"^\s*(?:Private|Public|Protected)?\s*{Regex.Escape(variableName)}\s*As\s+(\w+)(?:\s*=\s*(.+))?$";
            var fieldMatch = Regex.Match(line, fieldPattern, RegexOptions.IgnoreCase);
            if (fieldMatch.Success)
            {
                return new VariableDeclarationInfo
                {
                    DeclarationLine = i + 1,
                    Column = line.IndexOf(variableName, StringComparison.OrdinalIgnoreCase) + 1,
                    EndColumn = line.IndexOf(variableName, StringComparison.OrdinalIgnoreCase) + variableName.Length + 1,
                    Type = fieldMatch.Groups[1].Value,
                    Initializer = fieldMatch.Groups[2].Success ? fieldMatch.Groups[2].Value.Trim() : "",
                    FullDeclaration = line.Trim(),
                    ContainingMethod = null,
                    ScopeStartLine = 1,
                    ScopeEndLine = lines.Length,
                    IsParameter = false,
                    IsField = true
                };
            }
        }

        return null;
    }

    private List<SymbolLocation> FindVariableUsages(string[] lines, string variableName, int scopeStart, int scopeEnd, int declarationLine)
    {
        var usages = new List<SymbolLocation>();
        var pattern = $@"\b{Regex.Escape(variableName)}\b";

        for (int i = scopeStart - 1; i < scopeEnd && i < lines.Length; i++)
        {
            // Skip the declaration line
            if (i + 1 == declarationLine)
            {
                continue;
            }

            var line = lines[i];
            var matches = Regex.Matches(line, pattern, RegexOptions.IgnoreCase);

            foreach (Match match in matches)
            {
                // Make sure it's not part of a declaration or assignment target
                var beforeMatch = line.Substring(0, match.Index);

                // Skip if this is a Dim statement (declaration)
                if (Regex.IsMatch(beforeMatch, @"\bDim\s+$", RegexOptions.IgnoreCase))
                {
                    continue;
                }

                // Skip if this is an assignment target (left side of =)
                // But allow if it's part of an expression like x + y = ...
                var afterMatch = line.Substring(match.Index + match.Length).TrimStart();
                if (afterMatch.StartsWith("=") && !afterMatch.StartsWith("=="))
                {
                    // Check if there's an operator before this variable
                    var trimmedBefore = beforeMatch.TrimEnd();
                    if (!trimmedBefore.EndsWith("+") && !trimmedBefore.EndsWith("-") &&
                        !trimmedBefore.EndsWith("*") && !trimmedBefore.EndsWith("/") &&
                        !trimmedBefore.EndsWith("(") && !trimmedBefore.EndsWith(",") &&
                        !trimmedBefore.EndsWith("And") && !trimmedBefore.EndsWith("Or"))
                    {
                        continue; // This is an assignment target
                    }
                }

                usages.Add(new SymbolLocation
                {
                    FilePath = "",
                    Line = i + 1,
                    Column = match.Index + 1,
                    EndColumn = match.Index + match.Length + 1,
                    Text = variableName,
                    Type = SymbolLocationType.Reference
                });
            }
        }

        return usages;
    }

    private bool CheckIfReassigned(string[] lines, string variableName, int declarationLine, int scopeEnd)
    {
        var pattern = $@"\b{Regex.Escape(variableName)}\s*=\s*[^=]";

        for (int i = declarationLine; i < scopeEnd && i < lines.Length; i++)
        {
            var line = lines[i];

            // Skip the declaration line
            if (i + 1 == declarationLine)
            {
                continue;
            }

            // Check for reassignment (but not comparison ==)
            if (Regex.IsMatch(line, pattern, RegexOptions.IgnoreCase))
            {
                // Make sure it's not part of a Dim statement
                if (!Regex.IsMatch(line, @"\bDim\b", RegexOptions.IgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private string GetWordAtPosition(string line, int column)
    {
        if (column < 1 || column > line.Length)
        {
            return "";
        }

        var col = column - 1;
        var start = col;
        var end = col;

        // Find start of word
        while (start > 0 && IsIdentifierChar(line[start - 1]))
        {
            start--;
        }

        // Find end of word
        while (end < line.Length && IsIdentifierChar(line[end]))
        {
            end++;
        }

        if (start >= end)
        {
            return "";
        }

        return line.Substring(start, end - start);
    }

    private bool NeedsParentheses(string line, int column, string expression)
    {
        // Check if the expression contains operators that might need parentheses
        var hasOperators = Regex.IsMatch(expression, @"[\+\-\*\/\&]|And|Or|Mod", RegexOptions.IgnoreCase);
        if (!hasOperators)
        {
            return false;
        }

        // Check the context where the variable is used
        var beforeCol = column - 2;
        var afterEnd = column; // Will be adjusted after variable name

        if (beforeCol >= 0)
        {
            var charBefore = line[beforeCol];
            // If preceded by an operator, parentheses might be needed
            if (charBefore == '*' || charBefore == '/' || charBefore == '^')
            {
                return true;
            }
        }

        // Check what follows (would need to know variable length, but for safety add parens)
        return true;
    }

    private bool IsOnlyDeclarationOnLine(string line, string variableName)
    {
        // Check if this is the only variable declared on the line
        // Pattern: Dim varName As Type = expr (only one variable)
        var trimmed = line.Trim();

        // If there are commas, there might be multiple declarations
        if (trimmed.Contains(","))
        {
            return false;
        }

        return true;
    }

    private string RemoveVariableFromDeclarationLine(string line, string variableName)
    {
        // This handles the case where multiple variables are declared on one line
        // Dim a = 1, b = 2, c = 3 -> remove b -> Dim a = 1, c = 3

        // For now, this is a simplified implementation
        // A full implementation would need to parse the declaration properly
        var pattern = $@",?\s*{Regex.Escape(variableName)}\s*(?:As\s+\w+)?\s*=\s*[^,]+,?";
        return Regex.Replace(line, pattern, "", RegexOptions.IgnoreCase);
    }

    #endregion

    #region Safe Delete

    public async Task<DeletableSymbolInfo?> GetDeletableSymbolInfoAsync(string filePath, int line, int column, CancellationToken cancellationToken = default)
    {
        try
        {
            var content = await _fileService.ReadFileAsync(filePath, cancellationToken);
            var lines = content.Split('\n');

            if (line < 1 || line > lines.Length)
            {
                return null;
            }

            var currentLine = lines[line - 1];
            var symbolName = GetWordAtPosition(currentLine, column);

            if (string.IsNullOrEmpty(symbolName))
            {
                return null;
            }

            // Try to identify what kind of symbol this is and find its definition
            var symbolInfo = await IdentifyDeletableSymbolAsync(lines, symbolName, line, column, filePath, cancellationToken);

            if (symbolInfo == null)
            {
                return null;
            }

            // Find all usages of this symbol
            var usages = await FindSymbolUsagesForDeleteAsync(lines, symbolName, symbolInfo, filePath, cancellationToken);
            symbolInfo.Usages = usages;
            symbolInfo.UsageCount = usages.Count;

            // Generate warning message
            if (symbolInfo.UsageCount > 0)
            {
                symbolInfo.WarningMessage = $"Symbol '{symbolName}' has {symbolInfo.UsageCount} usage(s). Deleting it will cause errors.";
            }

            return symbolInfo;
        }
        catch
        {
            return null;
        }
    }

    private async Task<DeletableSymbolInfo?> IdentifyDeletableSymbolAsync(string[] lines, string symbolName, int cursorLine, int cursorColumn, string filePath, CancellationToken cancellationToken)
    {
        var currentLine = lines[cursorLine - 1];
        var trimmedLine = currentLine.Trim();

        // Check for local variable (Dim statement)
        var dimMatch = Regex.Match(trimmedLine, $@"^\s*Dim\s+{Regex.Escape(symbolName)}\b", RegexOptions.IgnoreCase);
        if (dimMatch.Success)
        {
            return CreateDeletableLocalVariableInfo(lines, symbolName, cursorLine, filePath, currentLine);
        }

        // Check if we're on a Dim line but cursor is on variable name
        if (Regex.IsMatch(currentLine, @"\bDim\b", RegexOptions.IgnoreCase))
        {
            var varMatch = Regex.Match(currentLine, $@"\bDim\s+{Regex.Escape(symbolName)}\b", RegexOptions.IgnoreCase);
            if (varMatch.Success)
            {
                return CreateDeletableLocalVariableInfo(lines, symbolName, cursorLine, filePath, currentLine);
            }
        }

        // Check for constant
        var constMatch = Regex.Match(trimmedLine, $@"^\s*(?:Public\s+|Private\s+|Protected\s+)?(?:Shared\s+)?Const\s+{Regex.Escape(symbolName)}\b", RegexOptions.IgnoreCase);
        if (constMatch.Success)
        {
            return CreateDeletableConstantInfo(lines, symbolName, cursorLine, filePath, currentLine);
        }

        // Check for field
        var fieldMatch = Regex.Match(trimmedLine, $@"^\s*(?:Public\s+|Private\s+|Protected\s+)?(?:Shared\s+)?(?:ReadOnly\s+)?(?:Dim\s+)?(?!Sub\b|Function\b|Property\b|Class\b|Module\b|Interface\b|Enum\b|Structure\b){Regex.Escape(symbolName)}\s+As\b", RegexOptions.IgnoreCase);
        if (fieldMatch.Success)
        {
            return CreateDeletableFieldInfo(lines, symbolName, cursorLine, filePath, currentLine);
        }

        // Check for Sub
        var subMatch = Regex.Match(trimmedLine, $@"^\s*(?:Public\s+|Private\s+|Protected\s+)?(?:Shared\s+)?(?:Overridable\s+|Overrides\s+)?Sub\s+{Regex.Escape(symbolName)}\b", RegexOptions.IgnoreCase);
        if (subMatch.Success)
        {
            return CreateDeletableMethodInfo(lines, symbolName, cursorLine, filePath, currentLine, false);
        }

        // Check for Function
        var funcMatch = Regex.Match(trimmedLine, $@"^\s*(?:Public\s+|Private\s+|Protected\s+)?(?:Shared\s+)?(?:Overridable\s+|Overrides\s+)?Function\s+{Regex.Escape(symbolName)}\b", RegexOptions.IgnoreCase);
        if (funcMatch.Success)
        {
            return CreateDeletableMethodInfo(lines, symbolName, cursorLine, filePath, currentLine, true);
        }

        // Check for Property
        var propMatch = Regex.Match(trimmedLine, $@"^\s*(?:Public\s+|Private\s+|Protected\s+)?(?:Shared\s+)?(?:ReadOnly\s+)?Property\s+{Regex.Escape(symbolName)}\b", RegexOptions.IgnoreCase);
        if (propMatch.Success)
        {
            return CreateDeletablePropertyInfo(lines, symbolName, cursorLine, filePath, currentLine);
        }

        // Check for Class
        var classMatch = Regex.Match(trimmedLine, $@"^\s*(?:Public\s+|Private\s+|Protected\s+)?(?:Partial\s+)?Class\s+{Regex.Escape(symbolName)}\b", RegexOptions.IgnoreCase);
        if (classMatch.Success)
        {
            return CreateDeletableTypeInfo(lines, symbolName, cursorLine, filePath, currentLine, DeletableSymbolKind.Class);
        }

        // Check for Module
        var moduleMatch = Regex.Match(trimmedLine, $@"^\s*(?:Public\s+|Private\s+)?Module\s+{Regex.Escape(symbolName)}\b", RegexOptions.IgnoreCase);
        if (moduleMatch.Success)
        {
            return CreateDeletableTypeInfo(lines, symbolName, cursorLine, filePath, currentLine, DeletableSymbolKind.Module);
        }

        // Check for Interface
        var interfaceMatch = Regex.Match(trimmedLine, $@"^\s*(?:Public\s+|Private\s+)?Interface\s+{Regex.Escape(symbolName)}\b", RegexOptions.IgnoreCase);
        if (interfaceMatch.Success)
        {
            return CreateDeletableTypeInfo(lines, symbolName, cursorLine, filePath, currentLine, DeletableSymbolKind.Interface);
        }

        // Check for Enum
        var enumMatch = Regex.Match(trimmedLine, $@"^\s*(?:Public\s+|Private\s+)?Enum\s+{Regex.Escape(symbolName)}\b", RegexOptions.IgnoreCase);
        if (enumMatch.Success)
        {
            return CreateDeletableTypeInfo(lines, symbolName, cursorLine, filePath, currentLine, DeletableSymbolKind.Enum);
        }

        // Check for Structure
        var structMatch = Regex.Match(trimmedLine, $@"^\s*(?:Public\s+|Private\s+)?Structure\s+{Regex.Escape(symbolName)}\b", RegexOptions.IgnoreCase);
        if (structMatch.Success)
        {
            return CreateDeletableTypeInfo(lines, symbolName, cursorLine, filePath, currentLine, DeletableSymbolKind.Structure);
        }

        // Check for parameter (in a Sub/Function signature)
        var paramMatch = Regex.Match(trimmedLine, $@"\(\s*.*\b(?:ByVal\s+|ByRef\s+)?{Regex.Escape(symbolName)}\s+As\b", RegexOptions.IgnoreCase);
        if (paramMatch.Success)
        {
            return CreateDeletableParameterInfo(lines, symbolName, cursorLine, filePath, currentLine);
        }

        // Try to find the symbol definition elsewhere in the file
        return await FindDeletableSymbolDefinitionAsync(lines, symbolName, cursorLine, filePath, cancellationToken);
    }

    private DeletableSymbolInfo CreateDeletableLocalVariableInfo(string[] lines, string symbolName, int line, string filePath, string currentLine)
    {
        var containingMethod = FindContainingMethodForDelete(lines, line);
        var accessibility = "Local";
        var type = ExtractTypeForDelete(currentLine, symbolName);
        var endLine = line;
        var declarationText = currentLine.Trim();

        return new DeletableSymbolInfo
        {
            Name = symbolName,
            Kind = DeletableSymbolKind.LocalVariable,
            FilePath = filePath,
            DefinitionLine = line,
            DefinitionEndLine = endLine,
            DefinitionColumn = currentLine.IndexOf(symbolName, StringComparison.OrdinalIgnoreCase) + 1,
            DefinitionEndColumn = currentLine.IndexOf(symbolName, StringComparison.OrdinalIgnoreCase) + symbolName.Length + 1,
            Accessibility = accessibility,
            Type = type,
            ContainingMethod = containingMethod,
            DeclarationText = declarationText
        };
    }

    private DeletableSymbolInfo CreateDeletableConstantInfo(string[] lines, string symbolName, int line, string filePath, string currentLine)
    {
        var containingType = FindContainingTypeForDelete(lines, line);
        var accessibility = ExtractAccessibilityForDelete(currentLine);
        var type = ExtractTypeForDelete(currentLine, symbolName);
        var declarationText = currentLine.Trim();

        return new DeletableSymbolInfo
        {
            Name = symbolName,
            Kind = DeletableSymbolKind.Constant,
            FilePath = filePath,
            DefinitionLine = line,
            DefinitionEndLine = line,
            DefinitionColumn = currentLine.IndexOf(symbolName, StringComparison.OrdinalIgnoreCase) + 1,
            DefinitionEndColumn = currentLine.IndexOf(symbolName, StringComparison.OrdinalIgnoreCase) + symbolName.Length + 1,
            Accessibility = accessibility,
            Type = type,
            ContainingType = containingType,
            DeclarationText = declarationText
        };
    }

    private DeletableSymbolInfo CreateDeletableFieldInfo(string[] lines, string symbolName, int line, string filePath, string currentLine)
    {
        var containingType = FindContainingTypeForDelete(lines, line);
        var accessibility = ExtractAccessibilityForDelete(currentLine);
        var type = ExtractTypeForDelete(currentLine, symbolName);
        var declarationText = currentLine.Trim();

        return new DeletableSymbolInfo
        {
            Name = symbolName,
            Kind = DeletableSymbolKind.Field,
            FilePath = filePath,
            DefinitionLine = line,
            DefinitionEndLine = line,
            DefinitionColumn = currentLine.IndexOf(symbolName, StringComparison.OrdinalIgnoreCase) + 1,
            DefinitionEndColumn = currentLine.IndexOf(symbolName, StringComparison.OrdinalIgnoreCase) + symbolName.Length + 1,
            Accessibility = accessibility,
            Type = type,
            ContainingType = containingType,
            DeclarationText = declarationText
        };
    }

    private DeletableSymbolInfo CreateDeletableMethodInfo(string[] lines, string symbolName, int startLine, string filePath, string currentLine, bool isFunction)
    {
        var containingType = FindContainingTypeForDelete(lines, startLine);
        var accessibility = ExtractAccessibilityForDelete(currentLine);
        var endLine = FindEndOfBlockForDelete(lines, startLine, isFunction ? "Function" : "Sub");
        var declarationText = currentLine.Trim();

        // Build full declaration text
        var fullDeclaration = new System.Text.StringBuilder();
        for (int i = startLine - 1; i < endLine && i < lines.Length; i++)
        {
            fullDeclaration.AppendLine(lines[i]);
        }

        return new DeletableSymbolInfo
        {
            Name = symbolName,
            Kind = isFunction ? DeletableSymbolKind.Function : DeletableSymbolKind.Sub,
            FilePath = filePath,
            DefinitionLine = startLine,
            DefinitionEndLine = endLine,
            DefinitionColumn = currentLine.IndexOf(symbolName, StringComparison.OrdinalIgnoreCase) + 1,
            DefinitionEndColumn = currentLine.IndexOf(symbolName, StringComparison.OrdinalIgnoreCase) + symbolName.Length + 1,
            Accessibility = accessibility,
            ContainingType = containingType,
            DeclarationText = fullDeclaration.ToString().Trim()
        };
    }

    private DeletableSymbolInfo CreateDeletablePropertyInfo(string[] lines, string symbolName, int startLine, string filePath, string currentLine)
    {
        var containingType = FindContainingTypeForDelete(lines, startLine);
        var accessibility = ExtractAccessibilityForDelete(currentLine);
        var type = ExtractPropertyReturnTypeForDelete(currentLine);
        var endLine = FindEndOfBlockForDelete(lines, startLine, "Property");
        var declarationText = currentLine.Trim();

        return new DeletableSymbolInfo
        {
            Name = symbolName,
            Kind = DeletableSymbolKind.Property,
            FilePath = filePath,
            DefinitionLine = startLine,
            DefinitionEndLine = endLine,
            DefinitionColumn = currentLine.IndexOf(symbolName, StringComparison.OrdinalIgnoreCase) + 1,
            DefinitionEndColumn = currentLine.IndexOf(symbolName, StringComparison.OrdinalIgnoreCase) + symbolName.Length + 1,
            Accessibility = accessibility,
            Type = type,
            ContainingType = containingType,
            DeclarationText = declarationText
        };
    }

    private DeletableSymbolInfo CreateDeletableTypeInfo(string[] lines, string symbolName, int startLine, string filePath, string currentLine, DeletableSymbolKind kind)
    {
        var accessibility = ExtractAccessibilityForDelete(currentLine);
        var endKeyword = kind switch
        {
            DeletableSymbolKind.Class => "Class",
            DeletableSymbolKind.Module => "Module",
            DeletableSymbolKind.Interface => "Interface",
            DeletableSymbolKind.Enum => "Enum",
            DeletableSymbolKind.Structure => "Structure",
            _ => "Class"
        };
        var endLine = FindEndOfBlockForDelete(lines, startLine, endKeyword);
        var declarationText = currentLine.Trim();

        return new DeletableSymbolInfo
        {
            Name = symbolName,
            Kind = kind,
            FilePath = filePath,
            DefinitionLine = startLine,
            DefinitionEndLine = endLine,
            DefinitionColumn = currentLine.IndexOf(symbolName, StringComparison.OrdinalIgnoreCase) + 1,
            DefinitionEndColumn = currentLine.IndexOf(symbolName, StringComparison.OrdinalIgnoreCase) + symbolName.Length + 1,
            Accessibility = accessibility,
            DeclarationText = declarationText
        };
    }

    private DeletableSymbolInfo CreateDeletableParameterInfo(string[] lines, string symbolName, int line, string filePath, string currentLine)
    {
        var containingMethod = FindContainingMethodForDelete(lines, line);
        var type = ExtractParameterTypeForDelete(currentLine, symbolName);

        return new DeletableSymbolInfo
        {
            Name = symbolName,
            Kind = DeletableSymbolKind.Parameter,
            FilePath = filePath,
            DefinitionLine = line,
            DefinitionEndLine = line,
            DefinitionColumn = currentLine.IndexOf(symbolName, StringComparison.OrdinalIgnoreCase) + 1,
            DefinitionEndColumn = currentLine.IndexOf(symbolName, StringComparison.OrdinalIgnoreCase) + symbolName.Length + 1,
            Accessibility = "Parameter",
            Type = type,
            ContainingMethod = containingMethod,
            DeclarationText = currentLine.Trim()
        };
    }

    private async Task<DeletableSymbolInfo?> FindDeletableSymbolDefinitionAsync(string[] lines, string symbolName, int cursorLine, string filePath, CancellationToken cancellationToken)
    {
        // Search backwards and forwards from cursor to find definition
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            // Check various definition patterns
            if (Regex.IsMatch(trimmed, $@"\bDim\s+{Regex.Escape(symbolName)}\b", RegexOptions.IgnoreCase))
            {
                return CreateDeletableLocalVariableInfo(lines, symbolName, i + 1, filePath, line);
            }
            if (Regex.IsMatch(trimmed, $@"\bConst\s+{Regex.Escape(symbolName)}\b", RegexOptions.IgnoreCase))
            {
                return CreateDeletableConstantInfo(lines, symbolName, i + 1, filePath, line);
            }
            if (Regex.IsMatch(trimmed, $@"\bSub\s+{Regex.Escape(symbolName)}\b", RegexOptions.IgnoreCase))
            {
                return CreateDeletableMethodInfo(lines, symbolName, i + 1, filePath, line, false);
            }
            if (Regex.IsMatch(trimmed, $@"\bFunction\s+{Regex.Escape(symbolName)}\b", RegexOptions.IgnoreCase))
            {
                return CreateDeletableMethodInfo(lines, symbolName, i + 1, filePath, line, true);
            }
            if (Regex.IsMatch(trimmed, $@"\bProperty\s+{Regex.Escape(symbolName)}\b", RegexOptions.IgnoreCase))
            {
                return CreateDeletablePropertyInfo(lines, symbolName, i + 1, filePath, line);
            }
            if (Regex.IsMatch(trimmed, $@"\bClass\s+{Regex.Escape(symbolName)}\b", RegexOptions.IgnoreCase))
            {
                return CreateDeletableTypeInfo(lines, symbolName, i + 1, filePath, line, DeletableSymbolKind.Class);
            }
            if (Regex.IsMatch(trimmed, $@"\bModule\s+{Regex.Escape(symbolName)}\b", RegexOptions.IgnoreCase))
            {
                return CreateDeletableTypeInfo(lines, symbolName, i + 1, filePath, line, DeletableSymbolKind.Module);
            }
            if (Regex.IsMatch(trimmed, $@"\bInterface\s+{Regex.Escape(symbolName)}\b", RegexOptions.IgnoreCase))
            {
                return CreateDeletableTypeInfo(lines, symbolName, i + 1, filePath, line, DeletableSymbolKind.Interface);
            }
            if (Regex.IsMatch(trimmed, $@"\bEnum\s+{Regex.Escape(symbolName)}\b", RegexOptions.IgnoreCase))
            {
                return CreateDeletableTypeInfo(lines, symbolName, i + 1, filePath, line, DeletableSymbolKind.Enum);
            }
            if (Regex.IsMatch(trimmed, $@"\bStructure\s+{Regex.Escape(symbolName)}\b", RegexOptions.IgnoreCase))
            {
                return CreateDeletableTypeInfo(lines, symbolName, i + 1, filePath, line, DeletableSymbolKind.Structure);
            }
        }

        return null;
    }

    private async Task<List<SymbolUsage>> FindSymbolUsagesForDeleteAsync(string[] lines, string symbolName, DeletableSymbolInfo symbolInfo, string filePath, CancellationToken cancellationToken)
    {
        var usages = new List<SymbolUsage>();
        var pattern = $@"\b{Regex.Escape(symbolName)}\b";

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            // Skip the definition line(s)
            if (i + 1 >= symbolInfo.DefinitionLine && i + 1 <= symbolInfo.DefinitionEndLine)
            {
                continue;
            }

            // Skip comments
            var commentIndex = line.IndexOf('\'');
            var searchLine = commentIndex >= 0 ? line.Substring(0, commentIndex) : line;

            var matches = Regex.Matches(searchLine, pattern, RegexOptions.IgnoreCase);
            foreach (Match match in matches)
            {
                // Skip if inside a string
                if (IsInsideString(searchLine, match.Index))
                {
                    continue;
                }

                var usageKind = DetermineUsageKindForDelete(line, match.Index, symbolInfo.Kind);
                var containingMethod = FindContainingMethodForDelete(lines, i + 1);
                var containingType = FindContainingTypeForDelete(lines, i + 1);

                usages.Add(new SymbolUsage
                {
                    FilePath = filePath,
                    Line = i + 1,
                    Column = match.Index + 1,
                    EndLine = i + 1,
                    EndColumn = match.Index + symbolName.Length + 1,
                    ContextLine = line.Trim(),
                    Kind = usageKind,
                    ContainingMethod = containingMethod,
                    ContainingType = containingType
                });
            }
        }

        return usages;
    }

    private SymbolUsageKind DetermineUsageKindForDelete(string line, int position, DeletableSymbolKind symbolKind)
    {
        var beforePosition = line.Substring(0, position).Trim();

        // Check for assignment
        if (beforePosition.EndsWith("=") || Regex.IsMatch(line.Substring(position), @"^\w+\s*=(?!=)"))
        {
            return SymbolUsageKind.Assignment;
        }

        // Check for call (method/sub)
        if (symbolKind == DeletableSymbolKind.Sub || symbolKind == DeletableSymbolKind.Function)
        {
            if (Regex.IsMatch(line.Substring(position), @"^\w+\s*\("))
            {
                return SymbolUsageKind.Call;
            }
        }

        // Check for inheritance
        if (Regex.IsMatch(beforePosition, @"\bInherits\s*$", RegexOptions.IgnoreCase))
        {
            return SymbolUsageKind.Inheritance;
        }

        // Check for implementation
        if (Regex.IsMatch(beforePosition, @"\bImplements\s*$", RegexOptions.IgnoreCase))
        {
            return SymbolUsageKind.Implementation;
        }

        // Check for type reference (As keyword)
        if (Regex.IsMatch(beforePosition, @"\bAs\s*$", RegexOptions.IgnoreCase))
        {
            return SymbolUsageKind.TypeReference;
        }

        return SymbolUsageKind.Reference;
    }

    private string FindContainingMethodForDelete(string[] lines, int lineNumber)
    {
        for (int i = lineNumber - 1; i >= 0; i--)
        {
            var line = lines[i].Trim();
            var match = Regex.Match(line, @"\b(Sub|Function)\s+(\w+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[2].Value;
            }
            // Check if we've exited the method
            if (Regex.IsMatch(line, @"^\s*End\s+(Sub|Function)\s*$", RegexOptions.IgnoreCase))
            {
                break;
            }
        }
        return "";
    }

    private string FindContainingTypeForDelete(string[] lines, int lineNumber)
    {
        for (int i = lineNumber - 1; i >= 0; i--)
        {
            var line = lines[i].Trim();
            var match = Regex.Match(line, @"\b(Class|Module|Interface|Structure)\s+(\w+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[2].Value;
            }
        }
        return "";
    }

    private string ExtractAccessibilityForDelete(string line)
    {
        if (Regex.IsMatch(line, @"\bPublic\b", RegexOptions.IgnoreCase)) return "Public";
        if (Regex.IsMatch(line, @"\bPrivate\b", RegexOptions.IgnoreCase)) return "Private";
        if (Regex.IsMatch(line, @"\bProtected\b", RegexOptions.IgnoreCase)) return "Protected";
        if (Regex.IsMatch(line, @"\bFriend\b", RegexOptions.IgnoreCase)) return "Friend";
        return "Private";
    }

    private string? ExtractTypeForDelete(string line, string symbolName)
    {
        var pattern = $@"\b{Regex.Escape(symbolName)}\s+As\s+(\w+)";
        var match = Regex.Match(line, pattern, RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    private string? ExtractPropertyReturnTypeForDelete(string line)
    {
        var match = Regex.Match(line, @"\)\s*As\s+(\w+)", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            match = Regex.Match(line, @"Property\s+\w+\s+As\s+(\w+)", RegexOptions.IgnoreCase);
        }
        return match.Success ? match.Groups[1].Value : null;
    }

    private string? ExtractParameterTypeForDelete(string line, string paramName)
    {
        var pattern = $@"\b{Regex.Escape(paramName)}\s+As\s+(\w+)";
        var match = Regex.Match(line, pattern, RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    private int FindEndOfBlockForDelete(string[] lines, int startLine, string blockType)
    {
        var endPattern = $@"^\s*End\s+{blockType}\s*$";
        var nestLevel = 1;
        var startPattern = $@"\b{blockType}\b";

        for (int i = startLine; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            // Check for nested blocks
            if (i > startLine - 1 && Regex.IsMatch(line, startPattern, RegexOptions.IgnoreCase))
            {
                nestLevel++;
            }

            if (Regex.IsMatch(line, endPattern, RegexOptions.IgnoreCase))
            {
                nestLevel--;
                if (nestLevel == 0)
                {
                    return i + 1;
                }
            }
        }

        return startLine; // No end found, return start line
    }

    public async Task<SafeDeleteResult> SafeDeleteAsync(string filePath, int line, int column, SafeDeleteOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            var symbolInfo = await GetDeletableSymbolInfoAsync(filePath, line, column, cancellationToken);
            if (symbolInfo == null)
            {
                return new SafeDeleteResult
                {
                    Success = false,
                    ErrorMessage = "Could not find symbol at cursor position"
                };
            }

            // Check for usages if not forcing delete
            if (!options.ForceDelete && symbolInfo.UsageCount > 0)
            {
                return new SafeDeleteResult
                {
                    Success = false,
                    ErrorMessage = $"Symbol '{symbolInfo.Name}' has {symbolInfo.UsageCount} usage(s). Use 'Force Delete' to delete anyway.",
                    SymbolName = symbolInfo.Name,
                    SymbolKind = symbolInfo.Kind,
                    UsagesRemaining = symbolInfo.UsageCount
                };
            }

            // Read the file content
            var content = await _fileService.ReadFileAsync(filePath, cancellationToken);
            var lines = content.Split('\n').ToList();

            var fileEdits = new List<FileEdit>();
            var deletedSymbols = new List<string>();
            var usagesCommentedOut = 0;

            // Comment out usages if requested
            if (options.CommentOutUsages && symbolInfo.UsageCount > 0)
            {
                foreach (var usage in symbolInfo.Usages.OrderByDescending(u => u.Line))
                {
                    var usageLine = lines[usage.Line - 1];
                    if (!usageLine.TrimStart().StartsWith("'"))
                    {
                        lines[usage.Line - 1] = "' " + usageLine;
                        usagesCommentedOut++;
                    }
                }
            }

            // Delete the symbol definition
            if (symbolInfo.DefinitionEndLine > symbolInfo.DefinitionLine)
            {
                // Multi-line symbol (method, class, etc.)
                for (int i = symbolInfo.DefinitionEndLine - 1; i >= symbolInfo.DefinitionLine - 1; i--)
                {
                    if (i < lines.Count)
                    {
                        lines.RemoveAt(i);
                    }
                }
            }
            else
            {
                // Single-line symbol
                if (symbolInfo.DefinitionLine - 1 < lines.Count)
                {
                    lines.RemoveAt(symbolInfo.DefinitionLine - 1);
                }
            }

            deletedSymbols.Add(symbolInfo.Name);

            // Write the modified content
            var newContent = string.Join("\n", lines);
            await _fileService.WriteFileAsync(filePath, newContent, cancellationToken);

            return new SafeDeleteResult
            {
                Success = true,
                SymbolName = symbolInfo.Name,
                SymbolKind = symbolInfo.Kind,
                FileEdits = new List<FileEdit>
                {
                    new FileEdit
                    {
                        FilePath = filePath,
                        Edits = Array.Empty<TextEdit>() // Full file replacement
                    }
                },
                UsagesRemaining = options.CommentOutUsages ? 0 : symbolInfo.UsageCount,
                UsagesCommentedOut = usagesCommentedOut,
                WasForced = options.ForceDelete && symbolInfo.UsageCount > 0,
                DeletedSymbols = deletedSymbols
            };
        }
        catch (Exception ex)
        {
            return new SafeDeleteResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    #endregion

    #region Pull Members Up

    public async Task<PullMembersUpInfo?> GetPullMembersUpInfoAsync(string filePath, int line, int column, CancellationToken cancellationToken = default)
    {
        try
        {
            var content = await _fileService.ReadFileAsync(filePath, cancellationToken);
            var lines = content.Split('\n');

            if (line < 1 || line > lines.Length)
            {
                return null;
            }

            // Find the containing class
            var classInfo = FindContainingClassForPullUp(lines, line);
            if (classInfo == null)
            {
                return null;
            }

            // Find base classes and interfaces
            var destinations = FindPullUpDestinations(lines, classInfo, filePath);
            if (destinations.Count == 0)
            {
                return null;
            }

            // Find members that can be pulled up
            var members = FindPullableMembers(lines, classInfo);

            return new PullMembersUpInfo
            {
                SourceTypeName = classInfo.Name,
                SourceTypeDeclaration = classInfo.Declaration,
                SourceFilePath = filePath,
                SourceTypeLine = classInfo.StartLine,
                Destinations = destinations,
                Members = members
            };
        }
        catch
        {
            return null;
        }
    }

    private class ClassInfoForPullUp
    {
        public string Name { get; set; } = "";
        public string Declaration { get; set; } = "";
        public int StartLine { get; set; }
        public int EndLine { get; set; }
        public string? BaseClass { get; set; }
        public List<string> Interfaces { get; set; } = new();
    }

    private ClassInfoForPullUp? FindContainingClassForPullUp(string[] lines, int cursorLine)
    {
        // Search backwards for class declaration
        for (int i = cursorLine - 1; i >= 0; i--)
        {
            var line = lines[i].Trim();
            var classMatch = Regex.Match(line, @"^\s*(?:Public\s+|Private\s+|Protected\s+)?(?:Partial\s+)?Class\s+(\w+)(?:\s+Inherits\s+(\w+))?(?:\s+Implements\s+(.+))?", RegexOptions.IgnoreCase);

            if (classMatch.Success)
            {
                var className = classMatch.Groups[1].Value;
                var baseClass = classMatch.Groups[2].Success ? classMatch.Groups[2].Value : null;
                var interfaces = new List<string>();

                if (classMatch.Groups[3].Success)
                {
                    interfaces = classMatch.Groups[3].Value.Split(',')
                        .Select(s => s.Trim())
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToList();
                }

                // Check for Inherits/Implements on subsequent lines
                for (int j = i + 1; j < lines.Length && j < i + 5; j++)
                {
                    var nextLine = lines[j].Trim();
                    if (nextLine.StartsWith("Inherits ", StringComparison.OrdinalIgnoreCase))
                    {
                        var inheritsMatch = Regex.Match(nextLine, @"Inherits\s+(\w+)", RegexOptions.IgnoreCase);
                        if (inheritsMatch.Success)
                        {
                            baseClass = inheritsMatch.Groups[1].Value;
                        }
                    }
                    else if (nextLine.StartsWith("Implements ", StringComparison.OrdinalIgnoreCase))
                    {
                        var implementsMatch = Regex.Match(nextLine, @"Implements\s+(.+)", RegexOptions.IgnoreCase);
                        if (implementsMatch.Success)
                        {
                            var newInterfaces = implementsMatch.Groups[1].Value.Split(',')
                                .Select(s => s.Trim())
                                .Where(s => !string.IsNullOrEmpty(s));
                            interfaces.AddRange(newInterfaces);
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(nextLine) &&
                             !nextLine.StartsWith("'") &&
                             !nextLine.StartsWith("Inherits", StringComparison.OrdinalIgnoreCase) &&
                             !nextLine.StartsWith("Implements", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }
                }

                // Find end of class
                var endLine = FindEndOfBlockForPullUp(lines, i + 1, "Class");

                return new ClassInfoForPullUp
                {
                    Name = className,
                    Declaration = lines[i].Trim(),
                    StartLine = i + 1,
                    EndLine = endLine,
                    BaseClass = baseClass,
                    Interfaces = interfaces
                };
            }

            // Check if we hit the end of a class (we've gone too far back)
            if (Regex.IsMatch(line, @"^\s*End\s+Class\s*$", RegexOptions.IgnoreCase))
            {
                return null;
            }
        }

        return null;
    }

    private List<PullMembersUpDestination> FindPullUpDestinations(string[] lines, ClassInfoForPullUp classInfo, string filePath)
    {
        var destinations = new List<PullMembersUpDestination>();

        // Add base class if exists
        if (!string.IsNullOrEmpty(classInfo.BaseClass))
        {
            var baseClassInfo = FindTypeDefinition(lines, classInfo.BaseClass);
            if (baseClassInfo != null)
            {
                destinations.Add(new PullMembersUpDestination
                {
                    Name = classInfo.BaseClass,
                    Kind = PullDestinationKind.BaseClass,
                    FilePath = filePath,
                    Line = baseClassInfo.Value.startLine,
                    EndLine = baseClassInfo.Value.endLine,
                    IsInSameFile = true,
                    Declaration = baseClassInfo.Value.declaration
                });
            }
            else
            {
                // Base class not in same file - still offer as destination
                destinations.Add(new PullMembersUpDestination
                {
                    Name = classInfo.BaseClass,
                    Kind = PullDestinationKind.BaseClass,
                    FilePath = "",
                    Line = 0,
                    EndLine = 0,
                    IsInSameFile = false,
                    Declaration = $"Class {classInfo.BaseClass}"
                });
            }
        }

        // Add interfaces
        foreach (var interfaceName in classInfo.Interfaces)
        {
            var interfaceInfo = FindTypeDefinition(lines, interfaceName);
            if (interfaceInfo != null)
            {
                destinations.Add(new PullMembersUpDestination
                {
                    Name = interfaceName,
                    Kind = PullDestinationKind.Interface,
                    FilePath = filePath,
                    Line = interfaceInfo.Value.startLine,
                    EndLine = interfaceInfo.Value.endLine,
                    IsInSameFile = true,
                    Declaration = interfaceInfo.Value.declaration
                });
            }
            else
            {
                destinations.Add(new PullMembersUpDestination
                {
                    Name = interfaceName,
                    Kind = PullDestinationKind.Interface,
                    FilePath = "",
                    Line = 0,
                    EndLine = 0,
                    IsInSameFile = false,
                    Declaration = $"Interface {interfaceName}"
                });
            }
        }

        return destinations;
    }

    private (int startLine, int endLine, string declaration)? FindTypeDefinition(string[] lines, string typeName)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            var classMatch = Regex.Match(line, $@"^\s*(?:Public\s+|Private\s+)?(?:Partial\s+)?(?:MustInherit\s+)?Class\s+{Regex.Escape(typeName)}\b", RegexOptions.IgnoreCase);
            if (classMatch.Success)
            {
                var endLine = FindEndOfBlockForPullUp(lines, i + 1, "Class");
                return (i + 1, endLine, line);
            }

            var interfaceMatch = Regex.Match(line, $@"^\s*(?:Public\s+|Private\s+)?Interface\s+{Regex.Escape(typeName)}\b", RegexOptions.IgnoreCase);
            if (interfaceMatch.Success)
            {
                var endLine = FindEndOfBlockForPullUp(lines, i + 1, "Interface");
                return (i + 1, endLine, line);
            }
        }
        return null;
    }

    private List<PullableMember> FindPullableMembers(string[] lines, ClassInfoForPullUp classInfo)
    {
        var members = new List<PullableMember>();

        for (int i = classInfo.StartLine; i < classInfo.EndLine - 1 && i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmedLine = line.Trim();

            // Skip empty lines, comments, and nested type definitions
            if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith("'"))
                continue;

            // Check for Sub
            var subMatch = Regex.Match(trimmedLine, @"^\s*(?:(Public|Private|Protected)\s+)?(?:(Shared)\s+)?(?:(Overridable|Overrides|MustOverride)\s+)?Sub\s+(\w+)\s*\(([^)]*)\)", RegexOptions.IgnoreCase);
            if (subMatch.Success)
            {
                var memberName = subMatch.Groups[4].Value;
                var accessibility = subMatch.Groups[1].Success ? subMatch.Groups[1].Value : "Public";
                var isShared = subMatch.Groups[2].Success;
                var modifier = subMatch.Groups[3].Success ? subMatch.Groups[3].Value : "";
                var parameters = subMatch.Groups[5].Value;
                var endLine = FindEndOfBlockForPullUp(lines, i + 1, "Sub");

                // Get full declaration
                var fullDeclaration = new System.Text.StringBuilder();
                for (int j = i; j < endLine && j < lines.Length; j++)
                {
                    fullDeclaration.AppendLine(lines[j]);
                }

                members.Add(new PullableMember
                {
                    Name = memberName,
                    Kind = PullableMemberKind.Sub,
                    Accessibility = accessibility,
                    Signature = $"Sub {memberName}({parameters})",
                    FullDeclaration = fullDeclaration.ToString().Trim(),
                    StartLine = i + 1,
                    EndLine = endLine,
                    IsAbstract = modifier.Equals("MustOverride", StringComparison.OrdinalIgnoreCase),
                    IsVirtual = modifier.Equals("Overridable", StringComparison.OrdinalIgnoreCase),
                    IsOverride = modifier.Equals("Overrides", StringComparison.OrdinalIgnoreCase),
                    IsShared = isShared,
                    Parameters = ParseParameterNames(parameters)
                });

                i = endLine - 1; // Skip to end of sub
                continue;
            }

            // Check for Function
            var funcMatch = Regex.Match(trimmedLine, @"^\s*(?:(Public|Private|Protected)\s+)?(?:(Shared)\s+)?(?:(Overridable|Overrides|MustOverride)\s+)?Function\s+(\w+)\s*\(([^)]*)\)\s*As\s+(\w+)", RegexOptions.IgnoreCase);
            if (funcMatch.Success)
            {
                var memberName = funcMatch.Groups[4].Value;
                var accessibility = funcMatch.Groups[1].Success ? funcMatch.Groups[1].Value : "Public";
                var isShared = funcMatch.Groups[2].Success;
                var modifier = funcMatch.Groups[3].Success ? funcMatch.Groups[3].Value : "";
                var parameters = funcMatch.Groups[5].Value;
                var returnType = funcMatch.Groups[6].Value;
                var endLine = FindEndOfBlockForPullUp(lines, i + 1, "Function");

                var fullDeclaration = new System.Text.StringBuilder();
                for (int j = i; j < endLine && j < lines.Length; j++)
                {
                    fullDeclaration.AppendLine(lines[j]);
                }

                members.Add(new PullableMember
                {
                    Name = memberName,
                    Kind = PullableMemberKind.Function,
                    Accessibility = accessibility,
                    ReturnType = returnType,
                    Signature = $"Function {memberName}({parameters}) As {returnType}",
                    FullDeclaration = fullDeclaration.ToString().Trim(),
                    StartLine = i + 1,
                    EndLine = endLine,
                    IsAbstract = modifier.Equals("MustOverride", StringComparison.OrdinalIgnoreCase),
                    IsVirtual = modifier.Equals("Overridable", StringComparison.OrdinalIgnoreCase),
                    IsOverride = modifier.Equals("Overrides", StringComparison.OrdinalIgnoreCase),
                    IsShared = isShared,
                    Parameters = ParseParameterNames(parameters)
                });

                i = endLine - 1;
                continue;
            }

            // Check for Property
            var propMatch = Regex.Match(trimmedLine, @"^\s*(?:(Public|Private|Protected)\s+)?(?:(Shared)\s+)?(?:(Overridable|Overrides|MustOverride)\s+)?(?:ReadOnly\s+)?Property\s+(\w+)(?:\s*\(([^)]*)\))?\s*As\s+(\w+)", RegexOptions.IgnoreCase);
            if (propMatch.Success)
            {
                var memberName = propMatch.Groups[4].Value;
                var accessibility = propMatch.Groups[1].Success ? propMatch.Groups[1].Value : "Public";
                var isShared = propMatch.Groups[2].Success;
                var modifier = propMatch.Groups[3].Success ? propMatch.Groups[3].Value : "";
                var indexerParams = propMatch.Groups[5].Success ? propMatch.Groups[5].Value : "";
                var returnType = propMatch.Groups[6].Value;
                var endLine = FindEndOfBlockForPullUp(lines, i + 1, "Property");

                var fullDeclaration = new System.Text.StringBuilder();
                for (int j = i; j < endLine && j < lines.Length; j++)
                {
                    fullDeclaration.AppendLine(lines[j]);
                }

                var signature = string.IsNullOrEmpty(indexerParams)
                    ? $"Property {memberName} As {returnType}"
                    : $"Property {memberName}({indexerParams}) As {returnType}";

                members.Add(new PullableMember
                {
                    Name = memberName,
                    Kind = PullableMemberKind.Property,
                    Accessibility = accessibility,
                    ReturnType = returnType,
                    Signature = signature,
                    FullDeclaration = fullDeclaration.ToString().Trim(),
                    StartLine = i + 1,
                    EndLine = endLine,
                    IsAbstract = modifier.Equals("MustOverride", StringComparison.OrdinalIgnoreCase),
                    IsVirtual = modifier.Equals("Overridable", StringComparison.OrdinalIgnoreCase),
                    IsOverride = modifier.Equals("Overrides", StringComparison.OrdinalIgnoreCase),
                    IsShared = isShared,
                    Parameters = ParseParameterNames(indexerParams)
                });

                i = endLine - 1;
                continue;
            }
        }

        return members;
    }

    private int FindEndOfBlockForPullUp(string[] lines, int startLine, string blockType)
    {
        var endPattern = $@"^\s*End\s+{blockType}\s*$";
        var nestLevel = 1;
        var startPattern = $@"\b{blockType}\b";

        for (int i = startLine; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            // Check for nested blocks of same type
            if (Regex.IsMatch(line, $@"^\s*(?:Public\s+|Private\s+|Protected\s+)?(?:\w+\s+)*{blockType}\s+", RegexOptions.IgnoreCase))
            {
                nestLevel++;
            }

            if (Regex.IsMatch(line, endPattern, RegexOptions.IgnoreCase))
            {
                nestLevel--;
                if (nestLevel == 0)
                {
                    return i + 1;
                }
            }
        }

        return startLine;
    }

    public async Task<PullMembersUpResult> PullMembersUpAsync(string filePath, int line, int column, PullMembersUpOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            var pullInfo = await GetPullMembersUpInfoAsync(filePath, line, column, cancellationToken);
            if (pullInfo == null)
            {
                return new PullMembersUpResult
                {
                    Success = false,
                    ErrorMessage = "Could not find class at cursor position or no base class/interface available"
                };
            }

            // Find the destination
            var destination = pullInfo.Destinations.FirstOrDefault(d => d.Name == options.DestinationName);
            if (destination == null)
            {
                return new PullMembersUpResult
                {
                    Success = false,
                    ErrorMessage = $"Destination '{options.DestinationName}' not found"
                };
            }

            // Get the members to pull up
            var membersToPull = pullInfo.Members
                .Where(m => options.MemberNames.Contains(m.Name))
                .ToList();

            if (membersToPull.Count == 0)
            {
                return new PullMembersUpResult
                {
                    Success = false,
                    ErrorMessage = "No members selected to pull up"
                };
            }

            // Check if destination is in same file
            if (!destination.IsInSameFile)
            {
                return new PullMembersUpResult
                {
                    Success = false,
                    ErrorMessage = $"Cannot pull members to '{destination.Name}' - it is defined in a different file"
                };
            }

            // Read the file content
            var content = await _fileService.ReadFileAsync(filePath, cancellationToken);
            var lines = content.Split('\n').ToList();

            var pulledMembers = new List<string>();

            // Generate code to add to destination
            var codeToAdd = new System.Text.StringBuilder();
            foreach (var member in membersToPull)
            {
                if (destination.Kind == PullDestinationKind.Interface)
                {
                    // For interfaces, add signature only
                    codeToAdd.AppendLine(GenerateInterfaceMemberSignature(member));
                }
                else
                {
                    // For base class
                    if (options.MakeAbstract)
                    {
                        codeToAdd.AppendLine(GenerateAbstractMemberDeclaration(member));
                    }
                    else
                    {
                        // Move the full implementation
                        codeToAdd.AppendLine(member.FullDeclaration);
                    }
                }
                pulledMembers.Add(member.Name);
            }

            // Find insertion point in destination (before End Class/Interface)
            var insertLine = destination.EndLine - 1;

            // Insert the new members
            var codeLines = codeToAdd.ToString().TrimEnd().Split('\n');
            for (int i = codeLines.Length - 1; i >= 0; i--)
            {
                lines.Insert(insertLine, "    " + codeLines[i]);
            }

            // If pulling to base class with abstract, update source class members to be Overrides
            if (destination.Kind == PullDestinationKind.BaseClass && options.MakeAbstract)
            {
                // Update members in source class to add Overrides keyword
                // Need to account for shifted line numbers
                var linesAdded = codeLines.Length;
                foreach (var member in membersToPull.OrderByDescending(m => m.StartLine))
                {
                    var sourceLine = member.StartLine - 1 + linesAdded;
                    if (sourceLine < lines.Count)
                    {
                        var memberLine = lines[sourceLine];
                        // Add Overrides if not already present
                        if (!Regex.IsMatch(memberLine, @"\bOverrides\b", RegexOptions.IgnoreCase))
                        {
                            memberLine = AddOverridesToMember(memberLine);
                            lines[sourceLine] = memberLine;
                        }
                    }
                }
            }
            else if (destination.Kind == PullDestinationKind.BaseClass && !options.KeepImplementation)
            {
                // Remove members from source class
                var linesAdded = codeLines.Length;
                foreach (var member in membersToPull.OrderByDescending(m => m.StartLine))
                {
                    var startIdx = member.StartLine - 1 + linesAdded;
                    var endIdx = member.EndLine - 1 + linesAdded;
                    for (int i = endIdx; i >= startIdx && i < lines.Count; i--)
                    {
                        lines.RemoveAt(i);
                    }
                }
            }

            // Write the modified content
            var newContent = string.Join("\n", lines);
            await _fileService.WriteFileAsync(filePath, newContent, cancellationToken);

            return new PullMembersUpResult
            {
                Success = true,
                DestinationName = destination.Name,
                DestinationKind = destination.Kind,
                PulledMembers = pulledMembers,
                MembersPulled = pulledMembers.Count,
                FileEdits = new List<FileEdit>
                {
                    new FileEdit
                    {
                        FilePath = filePath,
                        Edits = Array.Empty<TextEdit>()
                    }
                }
            };
        }
        catch (Exception ex)
        {
            return new PullMembersUpResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private string GenerateInterfaceMemberSignature(PullableMember member)
    {
        return member.Kind switch
        {
            PullableMemberKind.Sub => $"Sub {member.Name}({string.Join(", ", member.Parameters.Select(p => $"ByVal {p} As Object"))})",
            PullableMemberKind.Function => $"Function {member.Name}({string.Join(", ", member.Parameters.Select(p => $"ByVal {p} As Object"))}) As {member.ReturnType ?? "Object"}",
            PullableMemberKind.Property => $"Property {member.Name} As {member.ReturnType ?? "Object"}",
            _ => $"' {member.Name}"
        };
    }

    private string GenerateAbstractMemberDeclaration(PullableMember member)
    {
        var accessibility = member.Accessibility;
        return member.Kind switch
        {
            PullableMemberKind.Sub => $"{accessibility} MustOverride Sub {member.Name}({string.Join(", ", member.Parameters.Select(p => $"ByVal {p} As Object"))})",
            PullableMemberKind.Function => $"{accessibility} MustOverride Function {member.Name}({string.Join(", ", member.Parameters.Select(p => $"ByVal {p} As Object"))}) As {member.ReturnType ?? "Object"}",
            PullableMemberKind.Property => $"{accessibility} MustOverride Property {member.Name} As {member.ReturnType ?? "Object"}",
            _ => $"' {member.Name}"
        };
    }

    private string AddOverridesToMember(string line)
    {
        // Add Overrides keyword after accessibility modifier
        var patterns = new[]
        {
            (@"^(\s*)(Public\s+)(Sub\s+)", "$1$2Overrides $3"),
            (@"^(\s*)(Public\s+)(Function\s+)", "$1$2Overrides $3"),
            (@"^(\s*)(Public\s+)(Property\s+)", "$1$2Overrides $3"),
            (@"^(\s*)(Private\s+)(Sub\s+)", "$1$2Overrides $3"),
            (@"^(\s*)(Private\s+)(Function\s+)", "$1$2Overrides $3"),
            (@"^(\s*)(Private\s+)(Property\s+)", "$1$2Overrides $3"),
            (@"^(\s*)(Protected\s+)(Sub\s+)", "$1$2Overrides $3"),
            (@"^(\s*)(Protected\s+)(Function\s+)", "$1$2Overrides $3"),
            (@"^(\s*)(Protected\s+)(Property\s+)", "$1$2Overrides $3"),
            (@"^(\s*)(Sub\s+)", "$1Public Overrides $2"),
            (@"^(\s*)(Function\s+)", "$1Public Overrides $2"),
            (@"^(\s*)(Property\s+)", "$1Public Overrides $2")
        };

        foreach (var (pattern, replacement) in patterns)
        {
            if (Regex.IsMatch(line, pattern, RegexOptions.IgnoreCase))
            {
                return Regex.Replace(line, pattern, replacement, RegexOptions.IgnoreCase);
            }
        }

        return line;
    }

    #endregion

    #region Push Members Down

    public async Task<PushMembersDownInfo?> GetPushMembersDownInfoAsync(string filePath, int line, int column, CancellationToken cancellationToken = default)
    {
        var content = await _fileService.ReadFileAsync(filePath, cancellationToken);
        var lines = content.Split('\n');

        // Find the class at cursor position
        var classInfo = FindContainingClassForPushDown(lines, line);
        if (classInfo == null)
        {
            return null;
        }

        var info = new PushMembersDownInfo
        {
            SourceTypeName = classInfo.Value.Name,
            SourceTypeDeclaration = classInfo.Value.Declaration,
            SourceFilePath = filePath,
            SourceTypeLine = classInfo.Value.StartLine
        };

        // Find derived classes in the same file and project
        var destinations = await FindDerivedClassesAsync(filePath, classInfo.Value.Name, cancellationToken);
        info.Destinations.AddRange(destinations);

        // Find pushable members (non-private, non-abstract members that can be moved down)
        var members = FindPushableMembers(lines, classInfo.Value.StartLine, classInfo.Value.EndLine);
        info.Members.AddRange(members);

        return info;
    }

    private (string Name, string Declaration, int StartLine, int EndLine)? FindContainingClassForPushDown(string[] lines, int cursorLine)
    {
        // Look for class definition at or before cursor
        for (int i = cursorLine - 1; i >= 0; i--)
        {
            var line = lines[i];
            var classMatch = Regex.Match(line, @"^\s*(?:Public\s+|Private\s+|Protected\s+)?(?:MustInherit\s+)?Class\s+(\w+)", RegexOptions.IgnoreCase);
            if (classMatch.Success)
            {
                var className = classMatch.Groups[1].Value;
                var endLine = FindEndOfBlockForPushDown(lines, i, "Class");

                // Check if cursor is within this class
                if (cursorLine <= endLine + 1)
                {
                    return (className, line.Trim(), i + 1, endLine + 1);
                }
            }
        }
        return null;
    }

    private async Task<List<PushMembersDownDestination>> FindDerivedClassesAsync(string filePath, string baseClassName, CancellationToken cancellationToken)
    {
        var destinations = new List<PushMembersDownDestination>();
        var projectDir = Path.GetDirectoryName(filePath)!;

        // Search for derived classes in .bas files
        var basFiles = Directory.GetFiles(projectDir, "*.bas", SearchOption.AllDirectories);

        foreach (var file in basFiles)
        {
            var content = await _fileService.ReadFileAsync(file, cancellationToken);
            var lines = content.Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                // Look for classes that inherit from the base class
                var inheritMatch = Regex.Match(lines[i], $@"^\s*(?:Public\s+|Private\s+|Protected\s+)?Class\s+(\w+)", RegexOptions.IgnoreCase);
                if (inheritMatch.Success)
                {
                    var className = inheritMatch.Groups[1].Value;
                    var endLine = FindEndOfBlockForPushDown(lines, i, "Class");

                    // Check if this class inherits from the base class
                    for (int j = i + 1; j <= Math.Min(i + 5, lines.Length - 1); j++)
                    {
                        if (Regex.IsMatch(lines[j], $@"^\s*Inherits\s+{Regex.Escape(baseClassName)}\s*$", RegexOptions.IgnoreCase))
                        {
                            // Find existing overrides in this derived class
                            var existingOverrides = FindExistingOverrides(lines, i, endLine);

                            destinations.Add(new PushMembersDownDestination
                            {
                                Name = className,
                                FilePath = file,
                                Line = i + 1,
                                EndLine = endLine + 1,
                                IsInSameFile = file == filePath,
                                Declaration = lines[i].Trim(),
                                ExistingOverrides = existingOverrides
                            });
                            break;
                        }
                        // Stop if we hit another class-level statement
                        if (Regex.IsMatch(lines[j], @"^\s*(Public|Private|Protected|Friend|Sub|Function|Property|Dim|Const)", RegexOptions.IgnoreCase) &&
                            !Regex.IsMatch(lines[j], @"^\s*Inherits", RegexOptions.IgnoreCase) &&
                            !Regex.IsMatch(lines[j], @"^\s*Implements", RegexOptions.IgnoreCase))
                        {
                            break;
                        }
                    }
                }
            }
        }

        return destinations;
    }

    private List<string> FindExistingOverrides(string[] lines, int classStartLine, int classEndLine)
    {
        var overrides = new List<string>();

        for (int i = classStartLine; i <= classEndLine && i < lines.Length; i++)
        {
            var line = lines[i];
            if (Regex.IsMatch(line, @"\bOverrides\b", RegexOptions.IgnoreCase))
            {
                // Extract the member name
                var match = Regex.Match(line, @"(?:Sub|Function|Property)\s+(\w+)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    overrides.Add(match.Groups[1].Value);
                }
            }
        }

        return overrides;
    }

    private List<PushableMember> FindPushableMembers(string[] lines, int classStartLine, int classEndLine)
    {
        var members = new List<PushableMember>();

        for (int i = classStartLine; i < classEndLine && i < lines.Length; i++)
        {
            var line = lines[i];

            // Skip private members (can't push those down)
            if (Regex.IsMatch(line, @"^\s*Private\s+", RegexOptions.IgnoreCase))
            {
                continue;
            }

            // Skip abstract/MustOverride members (derived classes should already have them)
            if (Regex.IsMatch(line, @"\bMustOverride\b", RegexOptions.IgnoreCase))
            {
                continue;
            }

            // Skip shared/static members
            if (Regex.IsMatch(line, @"\bShared\b", RegexOptions.IgnoreCase))
            {
                continue;
            }

            // Check for Sub
            var subMatch = Regex.Match(line, @"^\s*((?:Public|Protected|Friend)\s+)?(?:Overridable\s+)?Sub\s+(\w+)\s*(\([^)]*\))?", RegexOptions.IgnoreCase);
            if (subMatch.Success)
            {
                var endLine = FindEndOfBlockForPushDown(lines, i, "Sub");
                var body = ExtractBodyForPushDown(lines, i, endLine);

                members.Add(new PushableMember
                {
                    Name = subMatch.Groups[2].Value,
                    Kind = PushableMemberKind.Sub,
                    Accessibility = ExtractAccessibility(line),
                    Signature = $"Sub {subMatch.Groups[2].Value}{subMatch.Groups[3].Value}",
                    FullDeclaration = line.Trim(),
                    Body = body,
                    StartLine = i + 1,
                    EndLine = endLine + 1,
                    IsOverridable = Regex.IsMatch(line, @"\bOverridable\b", RegexOptions.IgnoreCase),
                    Parameters = ExtractParameterNamesForPushDown(subMatch.Groups[3].Value)
                });
                i = endLine - 1;
                continue;
            }

            // Check for Function
            var funcMatch = Regex.Match(line, @"^\s*((?:Public|Protected|Friend)\s+)?(?:Overridable\s+)?Function\s+(\w+)\s*(\([^)]*\))?\s*As\s+(\w+)", RegexOptions.IgnoreCase);
            if (funcMatch.Success)
            {
                var endLine = FindEndOfBlockForPushDown(lines, i, "Function");
                var body = ExtractBodyForPushDown(lines, i, endLine);

                members.Add(new PushableMember
                {
                    Name = funcMatch.Groups[2].Value,
                    Kind = PushableMemberKind.Function,
                    Accessibility = ExtractAccessibility(line),
                    ReturnType = funcMatch.Groups[4].Value,
                    Signature = $"Function {funcMatch.Groups[2].Value}{funcMatch.Groups[3].Value} As {funcMatch.Groups[4].Value}",
                    FullDeclaration = line.Trim(),
                    Body = body,
                    StartLine = i + 1,
                    EndLine = endLine + 1,
                    IsOverridable = Regex.IsMatch(line, @"\bOverridable\b", RegexOptions.IgnoreCase),
                    Parameters = ExtractParameterNamesForPushDown(funcMatch.Groups[3].Value)
                });
                i = endLine - 1;
                continue;
            }

            // Check for Property
            var propMatch = Regex.Match(line, @"^\s*((?:Public|Protected|Friend)\s+)?(?:Overridable\s+)?Property\s+(\w+)(?:\s*\([^)]*\))?\s*As\s+(\w+)", RegexOptions.IgnoreCase);
            if (propMatch.Success)
            {
                var endLine = FindEndOfBlockForPushDown(lines, i, "Property");
                var body = ExtractBodyForPushDown(lines, i, endLine);

                members.Add(new PushableMember
                {
                    Name = propMatch.Groups[2].Value,
                    Kind = PushableMemberKind.Property,
                    Accessibility = ExtractAccessibility(line),
                    ReturnType = propMatch.Groups[3].Value,
                    Signature = $"Property {propMatch.Groups[2].Value} As {propMatch.Groups[3].Value}",
                    FullDeclaration = line.Trim(),
                    Body = body,
                    StartLine = i + 1,
                    EndLine = endLine + 1,
                    IsOverridable = Regex.IsMatch(line, @"\bOverridable\b", RegexOptions.IgnoreCase)
                });
                i = endLine - 1;
                continue;
            }

            // Check for Field (Dim/Public/Protected)
            var fieldMatch = Regex.Match(line, @"^\s*((?:Public|Protected|Friend)\s+)?(?:Dim\s+)?(\w+)\s+As\s+(\w+)(?:\s*=\s*(.+))?$", RegexOptions.IgnoreCase);
            if (fieldMatch.Success && !Regex.IsMatch(line, @"\b(Sub|Function|Property|Class|Module|Interface)\b", RegexOptions.IgnoreCase))
            {
                members.Add(new PushableMember
                {
                    Name = fieldMatch.Groups[2].Value,
                    Kind = PushableMemberKind.Field,
                    Accessibility = ExtractAccessibility(line),
                    ReturnType = fieldMatch.Groups[3].Value,
                    Signature = $"{fieldMatch.Groups[2].Value} As {fieldMatch.Groups[3].Value}",
                    FullDeclaration = line.Trim(),
                    Body = fieldMatch.Groups[4].Value,
                    StartLine = i + 1,
                    EndLine = i + 1
                });
                continue;
            }

            // Check for Const
            var constMatch = Regex.Match(line, @"^\s*((?:Public|Protected|Friend)\s+)?Const\s+(\w+)\s*(?:As\s+(\w+))?\s*=\s*(.+)$", RegexOptions.IgnoreCase);
            if (constMatch.Success)
            {
                members.Add(new PushableMember
                {
                    Name = constMatch.Groups[2].Value,
                    Kind = PushableMemberKind.Constant,
                    Accessibility = ExtractAccessibility(line),
                    ReturnType = constMatch.Groups[3].Value,
                    Signature = $"Const {constMatch.Groups[2].Value}",
                    FullDeclaration = line.Trim(),
                    Body = constMatch.Groups[4].Value,
                    StartLine = i + 1,
                    EndLine = i + 1
                });
            }
        }

        return members;
    }

    private string ExtractAccessibility(string line)
    {
        if (Regex.IsMatch(line, @"^\s*Public\b", RegexOptions.IgnoreCase)) return "Public";
        if (Regex.IsMatch(line, @"^\s*Protected\b", RegexOptions.IgnoreCase)) return "Protected";
        if (Regex.IsMatch(line, @"^\s*Friend\b", RegexOptions.IgnoreCase)) return "Friend";
        if (Regex.IsMatch(line, @"^\s*Private\b", RegexOptions.IgnoreCase)) return "Private";
        return "Public"; // Default
    }

    private string ExtractBodyForPushDown(string[] lines, int startLine, int endLine)
    {
        var bodyLines = new List<string>();
        for (int i = startLine; i <= endLine && i < lines.Length; i++)
        {
            bodyLines.Add(lines[i]);
        }
        return string.Join("\n", bodyLines);
    }

    private List<string> ExtractParameterNamesForPushDown(string parameters)
    {
        if (string.IsNullOrWhiteSpace(parameters))
            return new List<string>();

        return parameters.Trim('(', ')').Split(',')
            .Select(p => p.Trim())
            .Select(p =>
            {
                var match = Regex.Match(p, @"(?:ByVal\s+|ByRef\s+)?(\w+)\s+As", RegexOptions.IgnoreCase);
                return match.Success ? match.Groups[1].Value : p;
            })
            .Where(n => !string.IsNullOrEmpty(n))
            .ToList();
    }

    private int FindEndOfBlockForPushDown(string[] lines, int startLine, string blockType)
    {
        var endPattern = $@"^\s*End\s+{blockType}\s*$";
        var nestLevel = 1;
        var startPattern = $@"\b{blockType}\b";

        for (int i = startLine + 1; i < lines.Length; i++)
        {
            var line = lines[i];

            // Check for nested starts
            if (Regex.IsMatch(line, $@"^\s*(?:Public\s+|Private\s+|Protected\s+)?(?:Overridable\s+|MustOverride\s+)?{startPattern}", RegexOptions.IgnoreCase) &&
                !Regex.IsMatch(line, endPattern, RegexOptions.IgnoreCase))
            {
                nestLevel++;
            }
            else if (Regex.IsMatch(line, endPattern, RegexOptions.IgnoreCase))
            {
                nestLevel--;
                if (nestLevel == 0)
                {
                    return i;
                }
            }
        }

        return lines.Length - 1;
    }

    public async Task<PushMembersDownResult> PushMembersDownAsync(string filePath, int line, int column, PushMembersDownOptions options, CancellationToken cancellationToken = default)
    {
        var info = await GetPushMembersDownInfoAsync(filePath, line, column, cancellationToken);
        if (info == null)
        {
            return new PushMembersDownResult
            {
                Success = false,
                ErrorMessage = "Could not find a class at the specified location"
            };
        }

        if (info.Destinations.Count == 0)
        {
            return new PushMembersDownResult
            {
                Success = false,
                ErrorMessage = "No derived classes found to push members to"
            };
        }

        if (options.MemberNames.Count == 0)
        {
            return new PushMembersDownResult
            {
                Success = false,
                ErrorMessage = "No members selected to push down"
            };
        }

        var selectedMembers = info.Members.Where(m => options.MemberNames.Contains(m.Name)).ToList();
        if (selectedMembers.Count == 0)
        {
            return new PushMembersDownResult
            {
                Success = false,
                ErrorMessage = "Selected members not found in the base class"
            };
        }

        // Determine destinations
        var targetDestinations = options.DestinationNames.Count > 0
            ? info.Destinations.Where(d => options.DestinationNames.Contains(d.Name)).ToList()
            : info.Destinations;

        if (targetDestinations.Count == 0)
        {
            return new PushMembersDownResult
            {
                Success = false,
                ErrorMessage = "No valid destinations found"
            };
        }

        var fileEdits = new Dictionary<string, List<TextEdit>>();

        // Process each destination
        foreach (var destination in targetDestinations)
        {
            var destContent = await _fileService.ReadFileAsync(destination.FilePath, cancellationToken);
            var destLines = destContent.Split('\n').ToList();

            // Find insert position (after Inherits/Implements statements, before End Class)
            var insertLine = FindInsertPositionForPushDown(destLines, destination.Line - 1, destination.EndLine - 1);

            // Generate code for each member
            var membersToAdd = new List<string>();
            foreach (var member in selectedMembers)
            {
                // Skip if derived class already has this override
                if (destination.ExistingOverrides.Contains(member.Name))
                {
                    continue;
                }

                var memberCode = GeneratePushedMemberCode(member, options.MarkAsOverrides, GetIndentation(destLines, destination.Line - 1));
                membersToAdd.Add(memberCode);
            }

            if (membersToAdd.Count > 0)
            {
                var newCode = "\n" + string.Join("\n\n", membersToAdd) + "\n";

                if (!fileEdits.ContainsKey(destination.FilePath))
                {
                    fileEdits[destination.FilePath] = new List<TextEdit>();
                }

                fileEdits[destination.FilePath].Add(new TextEdit
                {
                    StartLine = insertLine,
                    StartColumn = 1,
                    EndLine = insertLine,
                    EndColumn = 1,
                    NewText = newCode
                });
            }
        }

        // Remove or make abstract in base class
        if (options.RemoveFromBase || options.MakeAbstractInBase)
        {
            var baseContent = await _fileService.ReadFileAsync(filePath, cancellationToken);
            var baseLines = baseContent.Split('\n').ToList();

            // Sort members by line number descending to avoid offset issues
            var sortedMembers = selectedMembers.OrderByDescending(m => m.StartLine).ToList();

            if (!fileEdits.ContainsKey(filePath))
            {
                fileEdits[filePath] = new List<TextEdit>();
            }

            foreach (var member in sortedMembers)
            {
                if (options.MakeAbstractInBase)
                {
                    // Convert to abstract/MustOverride
                    var abstractDeclaration = ConvertToAbstractForPushDown(member);
                    fileEdits[filePath].Add(new TextEdit
                    {
                        StartLine = member.StartLine,
                        StartColumn = 1,
                        EndLine = member.EndLine,
                        EndColumn = baseLines[member.EndLine - 1].Length + 1,
                        NewText = abstractDeclaration
                    });
                }
                else if (options.RemoveFromBase)
                {
                    // Remove the member entirely
                    fileEdits[filePath].Add(new TextEdit
                    {
                        StartLine = member.StartLine,
                        StartColumn = 1,
                        EndLine = member.EndLine + 1,
                        EndColumn = 1,
                        NewText = ""
                    });
                }
            }
        }

        // Apply file edits
        foreach (var kvp in fileEdits)
        {
            var content = await _fileService.ReadFileAsync(kvp.Key, cancellationToken);
            var editedContent = ApplyTextEditsForPushDown(content, kvp.Value);
            await _fileService.WriteFileAsync(kvp.Key, editedContent, cancellationToken);
        }

        return new PushMembersDownResult
        {
            Success = true,
            FileEdits = fileEdits.Select(kvp => new FileEdit
            {
                FilePath = kvp.Key,
                Edits = kvp.Value
            }).ToList(),
            DestinationNames = targetDestinations.Select(d => d.Name).ToList(),
            PushedMembers = options.MemberNames,
            MembersPushed = selectedMembers.Count,
            DestinationsUpdated = targetDestinations.Count
        };
    }

    private int FindInsertPositionForPushDown(List<string> lines, int classStartLine, int classEndLine)
    {
        // Find the position after Inherits/Implements but before the first member or End Class
        for (int i = classStartLine + 1; i < classEndLine; i++)
        {
            var line = lines[i];

            // Skip Inherits and Implements
            if (Regex.IsMatch(line, @"^\s*(Inherits|Implements)\s+", RegexOptions.IgnoreCase))
            {
                continue;
            }

            // Skip empty lines at the start
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            // Found a member or other content - insert before this
            return i + 1;
        }

        // Insert before End Class
        return classEndLine;
    }

    private string GetIndentation(List<string> lines, int classLine)
    {
        var line = lines[classLine];
        var match = Regex.Match(line, @"^(\s*)");
        var classIndent = match.Success ? match.Groups[1].Value : "";
        return classIndent + "    "; // Add one level of indentation
    }

    private string GeneratePushedMemberCode(PushableMember member, bool markAsOverrides, string indent)
    {
        var bodyLines = member.Body.Split('\n');
        var result = new List<string>();

        foreach (var line in bodyLines)
        {
            var trimmedLine = line.TrimStart();
            if (string.IsNullOrEmpty(trimmedLine))
            {
                result.Add("");
            }
            else
            {
                // Add indent and potentially modify the declaration line
                if (result.Count == 0 && markAsOverrides)
                {
                    // This is the first line (declaration) - add Overrides keyword if needed
                    var modifiedLine = AddOverridesKeyword(trimmedLine);
                    result.Add(indent + modifiedLine);
                }
                else
                {
                    result.Add(indent + trimmedLine);
                }
            }
        }

        return string.Join("\n", result);
    }

    private string AddOverridesKeyword(string declaration)
    {
        // Skip if already has Overrides
        if (Regex.IsMatch(declaration, @"\bOverrides\b", RegexOptions.IgnoreCase))
        {
            return declaration;
        }

        // Add Overrides after accessibility modifier
        var patterns = new[]
        {
            (@"^(Public\s+)(Sub\s+)", "$1Overrides $2"),
            (@"^(Public\s+)(Function\s+)", "$1Overrides $2"),
            (@"^(Public\s+)(Property\s+)", "$1Overrides $2"),
            (@"^(Protected\s+)(Sub\s+)", "$1Overrides $2"),
            (@"^(Protected\s+)(Function\s+)", "$1Overrides $2"),
            (@"^(Protected\s+)(Property\s+)", "$1Overrides $2"),
            (@"^(Friend\s+)(Sub\s+)", "$1Overrides $2"),
            (@"^(Friend\s+)(Function\s+)", "$1Overrides $2"),
            (@"^(Friend\s+)(Property\s+)", "$1Overrides $2"),
            (@"^(Sub\s+)", "Public Overrides $1"),
            (@"^(Function\s+)", "Public Overrides $1"),
            (@"^(Property\s+)", "Public Overrides $1")
        };

        foreach (var (pattern, replacement) in patterns)
        {
            if (Regex.IsMatch(declaration, pattern, RegexOptions.IgnoreCase))
            {
                // Also remove Overridable if present since we're adding Overrides
                declaration = Regex.Replace(declaration, @"\bOverridable\s+", "", RegexOptions.IgnoreCase);
                return Regex.Replace(declaration, pattern, replacement, RegexOptions.IgnoreCase);
            }
        }

        return declaration;
    }

    private string ConvertToAbstractForPushDown(PushableMember member)
    {
        var declaration = member.FullDeclaration;

        // Remove Overridable if present
        declaration = Regex.Replace(declaration, @"\bOverridable\s+", "", RegexOptions.IgnoreCase);

        // Add MustOverride
        var patterns = new[]
        {
            (@"^(\s*)(Public\s+)(Sub\s+)", "$1$2MustOverride $3"),
            (@"^(\s*)(Public\s+)(Function\s+)", "$1$2MustOverride $3"),
            (@"^(\s*)(Public\s+)(Property\s+)", "$1$2MustOverride $3"),
            (@"^(\s*)(Protected\s+)(Sub\s+)", "$1$2MustOverride $3"),
            (@"^(\s*)(Protected\s+)(Function\s+)", "$1$2MustOverride $3"),
            (@"^(\s*)(Protected\s+)(Property\s+)", "$1$2MustOverride $3"),
            (@"^(\s*)(Friend\s+)(Sub\s+)", "$1$2MustOverride $3"),
            (@"^(\s*)(Friend\s+)(Function\s+)", "$1$2MustOverride $3"),
            (@"^(\s*)(Friend\s+)(Property\s+)", "$1$2MustOverride $3"),
            (@"^(\s*)(Sub\s+)", "$1Public MustOverride $2"),
            (@"^(\s*)(Function\s+)", "$1Public MustOverride $2"),
            (@"^(\s*)(Property\s+)", "$1Public MustOverride $2")
        };

        foreach (var (pattern, replacement) in patterns)
        {
            if (Regex.IsMatch(declaration, pattern, RegexOptions.IgnoreCase))
            {
                return Regex.Replace(declaration, pattern, replacement, RegexOptions.IgnoreCase);
            }
        }

        return declaration;
    }

    private string ApplyTextEditsForPushDown(string content, List<TextEdit> edits)
    {
        var lines = content.Split('\n').ToList();

        // Sort edits by start line descending to apply from bottom to top
        var sortedEdits = edits.OrderByDescending(e => e.StartLine).ThenByDescending(e => e.StartColumn).ToList();

        foreach (var edit in sortedEdits)
        {
            if (edit.StartLine <= 0 || edit.StartLine > lines.Count + 1)
            {
                continue;
            }

            if (edit.StartLine == edit.EndLine && edit.StartColumn == edit.EndColumn)
            {
                // Insertion
                if (edit.StartLine <= lines.Count)
                {
                    var line = lines[edit.StartLine - 1];
                    var beforeInsert = edit.StartColumn <= line.Length ? line.Substring(0, edit.StartColumn - 1) : line;
                    var afterInsert = edit.StartColumn <= line.Length ? line.Substring(edit.StartColumn - 1) : "";
                    lines[edit.StartLine - 1] = beforeInsert + edit.NewText + afterInsert;
                }
                else
                {
                    lines.Add(edit.NewText);
                }
            }
            else
            {
                // Replacement or deletion
                var startLine = Math.Max(0, edit.StartLine - 1);
                var endLine = Math.Min(lines.Count - 1, edit.EndLine - 1);
                var linesToRemove = endLine - startLine + 1;

                if (linesToRemove > 0 && startLine < lines.Count)
                {
                    lines.RemoveRange(startLine, Math.Min(linesToRemove, lines.Count - startLine));
                }

                if (!string.IsNullOrEmpty(edit.NewText))
                {
                    var newLines = edit.NewText.Split('\n');
                    lines.InsertRange(startLine, newLines);
                }
            }
        }

        return string.Join("\n", lines);
    }

    #endregion

    #region Use Base Type

    public async Task<UseBaseTypeInfo?> GetUseBaseTypeInfoAsync(string filePath, int line, int column, CancellationToken cancellationToken = default)
    {
        var content = await _fileService.ReadFileAsync(filePath, cancellationToken);
        var lines = content.Split('\n');

        if (line < 1 || line > lines.Length)
        {
            return null;
        }

        var currentLine = lines[line - 1];

        // Try to find a type declaration at cursor position
        var symbolInfo = FindTypeDeclarationAtCursor(lines, line, column);
        if (symbolInfo == null)
        {
            return null;
        }

        var info = new UseBaseTypeInfo
        {
            SymbolName = symbolInfo.Value.Name,
            CurrentType = symbolInfo.Value.TypeName,
            SymbolKind = symbolInfo.Value.Kind,
            Declaration = currentLine.Trim(),
            DeclarationLine = line,
            FilePath = filePath
        };

        // Find base types for the current type
        var baseTypes = await FindBaseTypesAsync(filePath, symbolInfo.Value.TypeName, cancellationToken);
        info.BaseTypes.AddRange(baseTypes);

        if (info.BaseTypes.Count == 0)
        {
            return null; // No base types available
        }

        return info;
    }

    private (string Name, string TypeName, UseBaseTypeSymbolKind Kind)? FindTypeDeclarationAtCursor(string[] lines, int cursorLine, int cursorColumn)
    {
        var line = lines[cursorLine - 1];

        // Check for variable declaration: Dim name As Type
        var dimMatch = Regex.Match(line, @"^\s*(?:Dim|Const)\s+(\w+)\s+As\s+(\w+)", RegexOptions.IgnoreCase);
        if (dimMatch.Success)
        {
            return (dimMatch.Groups[1].Value, dimMatch.Groups[2].Value, UseBaseTypeSymbolKind.Variable);
        }

        // Check for parameter: ByVal/ByRef name As Type
        var paramMatch = Regex.Match(line, @"(?:ByVal|ByRef)\s+(\w+)\s+As\s+(\w+)", RegexOptions.IgnoreCase);
        if (paramMatch.Success)
        {
            return (paramMatch.Groups[1].Value, paramMatch.Groups[2].Value, UseBaseTypeSymbolKind.Parameter);
        }

        // Check for field declaration: Public/Private/Protected name As Type
        var fieldMatch = Regex.Match(line, @"^\s*(?:Public|Private|Protected|Friend)\s+(?!Sub|Function|Property|Class|Module|Interface)(\w+)\s+As\s+(\w+)", RegexOptions.IgnoreCase);
        if (fieldMatch.Success)
        {
            return (fieldMatch.Groups[1].Value, fieldMatch.Groups[2].Value, UseBaseTypeSymbolKind.Field);
        }

        // Check for property type: Property name As Type
        var propMatch = Regex.Match(line, @"Property\s+(\w+)(?:\s*\([^)]*\))?\s+As\s+(\w+)", RegexOptions.IgnoreCase);
        if (propMatch.Success)
        {
            return (propMatch.Groups[1].Value, propMatch.Groups[2].Value, UseBaseTypeSymbolKind.Property);
        }

        // Check for function return type: Function name(...) As Type
        var funcMatch = Regex.Match(line, @"Function\s+(\w+)\s*\([^)]*\)\s+As\s+(\w+)", RegexOptions.IgnoreCase);
        if (funcMatch.Success)
        {
            return (funcMatch.Groups[1].Value, funcMatch.Groups[2].Value, UseBaseTypeSymbolKind.ReturnType);
        }

        return null;
    }

    private async Task<List<BaseTypeCandidate>> FindBaseTypesAsync(string filePath, string typeName, CancellationToken cancellationToken)
    {
        var candidates = new List<BaseTypeCandidate>();
        var projectDir = Path.GetDirectoryName(filePath)!;

        // Search for the type definition in the project
        var basFiles = Directory.GetFiles(projectDir, "*.bas", SearchOption.AllDirectories);

        foreach (var file in basFiles)
        {
            var content = await _fileService.ReadFileAsync(file, cancellationToken);
            var lines = content.Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                // Look for class definition matching the type name
                var classMatch = Regex.Match(lines[i], $@"^\s*(?:Public\s+|Private\s+)?(?:MustInherit\s+)?Class\s+{Regex.Escape(typeName)}\s*$", RegexOptions.IgnoreCase);
                if (classMatch.Success)
                {
                    // Found the class, look for base class and interfaces
                    for (int j = i + 1; j < Math.Min(i + 10, lines.Length); j++)
                    {
                        var inheritsMatch = Regex.Match(lines[j], @"^\s*Inherits\s+(\w+)", RegexOptions.IgnoreCase);
                        if (inheritsMatch.Success)
                        {
                            var baseClassName = inheritsMatch.Groups[1].Value;
                            candidates.Add(new BaseTypeCandidate
                            {
                                TypeName = baseClassName,
                                IsBaseClass = true,
                                IsInterface = false,
                                Description = "Base class"
                            });

                            // Recursively find base types of the base class
                            var parentBaseTypes = await FindBaseTypesAsync(filePath, baseClassName, cancellationToken);
                            foreach (var pbt in parentBaseTypes)
                            {
                                if (!candidates.Any(c => c.TypeName == pbt.TypeName))
                                {
                                    candidates.Add(pbt);
                                }
                            }
                        }

                        var implementsMatch = Regex.Match(lines[j], @"^\s*Implements\s+(.+)$", RegexOptions.IgnoreCase);
                        if (implementsMatch.Success)
                        {
                            var interfaces = implementsMatch.Groups[1].Value.Split(',')
                                .Select(iface => iface.Trim())
                                .Where(iface => !string.IsNullOrEmpty(iface));

                            foreach (var iface in interfaces)
                            {
                                candidates.Add(new BaseTypeCandidate
                                {
                                    TypeName = iface,
                                    IsBaseClass = false,
                                    IsInterface = true,
                                    Description = "Implemented interface"
                                });
                            }
                        }

                        // Stop if we hit member declarations
                        if (Regex.IsMatch(lines[j], @"^\s*(Public|Private|Protected|Friend|Sub|Function|Property|Dim|Const)", RegexOptions.IgnoreCase) &&
                            !Regex.IsMatch(lines[j], @"^\s*(Inherits|Implements)", RegexOptions.IgnoreCase))
                        {
                            break;
                        }
                    }
                    break;
                }

                // Look for interface definition matching the type name
                var interfaceMatch = Regex.Match(lines[i], $@"^\s*(?:Public\s+)?Interface\s+{Regex.Escape(typeName)}\s*$", RegexOptions.IgnoreCase);
                if (interfaceMatch.Success)
                {
                    // Found interface, look for inherited interfaces
                    for (int j = i + 1; j < Math.Min(i + 10, lines.Length); j++)
                    {
                        var inheritsMatch = Regex.Match(lines[j], @"^\s*Inherits\s+(.+)$", RegexOptions.IgnoreCase);
                        if (inheritsMatch.Success)
                        {
                            var baseInterfaces = inheritsMatch.Groups[1].Value.Split(',')
                                .Select(iface => iface.Trim())
                                .Where(iface => !string.IsNullOrEmpty(iface));

                            foreach (var iface in baseInterfaces)
                            {
                                candidates.Add(new BaseTypeCandidate
                                {
                                    TypeName = iface,
                                    IsBaseClass = false,
                                    IsInterface = true,
                                    Description = "Base interface"
                                });
                            }
                        }

                        if (Regex.IsMatch(lines[j], @"^\s*(Sub|Function|Property)", RegexOptions.IgnoreCase))
                        {
                            break;
                        }
                    }
                    break;
                }
            }
        }

        // Add common base type "Object" if not already present
        if (!candidates.Any(c => c.TypeName.Equals("Object", StringComparison.OrdinalIgnoreCase)))
        {
            candidates.Add(new BaseTypeCandidate
            {
                TypeName = "Object",
                IsBaseClass = true,
                IsInterface = false,
                Description = "Ultimate base class"
            });
        }

        return candidates;
    }

    public async Task<UseBaseTypeResult> UseBaseTypeAsync(string filePath, int line, int column, UseBaseTypeOptions options, CancellationToken cancellationToken = default)
    {
        var info = await GetUseBaseTypeInfoAsync(filePath, line, column, cancellationToken);
        if (info == null)
        {
            return new UseBaseTypeResult
            {
                Success = false,
                ErrorMessage = "Could not find a type declaration at the specified location"
            };
        }

        if (string.IsNullOrEmpty(options.NewTypeName))
        {
            return new UseBaseTypeResult
            {
                Success = false,
                ErrorMessage = "No new type specified"
            };
        }

        if (options.NewTypeName == info.CurrentType)
        {
            return new UseBaseTypeResult
            {
                Success = false,
                ErrorMessage = "New type is the same as the current type"
            };
        }

        var content = await _fileService.ReadFileAsync(filePath, cancellationToken);
        var lines = content.Split('\n').ToList();

        var edits = new List<TextEdit>();
        var occurrencesUpdated = 0;

        if (options.UpdateAllOccurrences)
        {
            // Find all occurrences of the type in declarations
            for (int i = 0; i < lines.Count; i++)
            {
                var originalLine = lines[i];
                var newLine = ReplaceTypeInDeclaration(originalLine, info.CurrentType, options.NewTypeName);

                if (newLine != originalLine)
                {
                    edits.Add(new TextEdit
                    {
                        StartLine = i + 1,
                        StartColumn = 1,
                        EndLine = i + 1,
                        EndColumn = originalLine.Length + 1,
                        NewText = newLine
                    });
                    occurrencesUpdated++;
                }
            }
        }
        else
        {
            // Only update the declaration at cursor position
            var originalLine = lines[line - 1];
            var newLine = ReplaceTypeInDeclaration(originalLine, info.CurrentType, options.NewTypeName);

            if (newLine != originalLine)
            {
                edits.Add(new TextEdit
                {
                    StartLine = line,
                    StartColumn = 1,
                    EndLine = line,
                    EndColumn = originalLine.Length + 1,
                    NewText = newLine
                });
                occurrencesUpdated = 1;
            }
        }

        if (edits.Count == 0)
        {
            return new UseBaseTypeResult
            {
                Success = false,
                ErrorMessage = "No occurrences found to update"
            };
        }

        // Apply edits
        var editedContent = ApplyTextEditsForUseBaseType(content, edits);
        await _fileService.WriteFileAsync(filePath, editedContent, cancellationToken);

        return new UseBaseTypeResult
        {
            Success = true,
            FileEdits = new List<FileEdit>
            {
                new FileEdit
                {
                    FilePath = filePath,
                    Edits = edits
                }
            },
            OriginalType = info.CurrentType,
            NewType = options.NewTypeName,
            OccurrencesUpdated = occurrencesUpdated
        };
    }

    private string ReplaceTypeInDeclaration(string line, string oldType, string newType)
    {
        // Replace type in "As Type" patterns, being careful to match whole words only
        var pattern = $@"\bAs\s+{Regex.Escape(oldType)}\b";
        return Regex.Replace(line, pattern, $"As {newType}", RegexOptions.IgnoreCase);
    }

    private string ApplyTextEditsForUseBaseType(string content, List<TextEdit> edits)
    {
        var lines = content.Split('\n').ToList();

        // Sort edits by line number descending
        var sortedEdits = edits.OrderByDescending(e => e.StartLine).ToList();

        foreach (var edit in sortedEdits)
        {
            if (edit.StartLine <= 0 || edit.StartLine > lines.Count)
            {
                continue;
            }

            lines[edit.StartLine - 1] = edit.NewText;
        }

        return string.Join("\n", lines);
    }

    #endregion

    #region Convert to Interface

    public async Task<ConvertToInterfaceInfo?> GetConvertToInterfaceInfoAsync(string filePath, int line, int column, CancellationToken cancellationToken = default)
    {
        var content = await _fileService.ReadFileAsync(filePath, cancellationToken);
        var lines = content.Split('\n');

        if (line < 1 || line > lines.Length)
        {
            return null;
        }

        // Find the class at or near the cursor
        var classInfo = FindClassAtCursor(lines, line);
        if (classInfo == null)
        {
            return null;
        }

        var info = new ConvertToInterfaceInfo
        {
            ClassName = classInfo.Value.Name,
            ClassDeclaration = classInfo.Value.Declaration,
            FilePath = filePath,
            ClassLine = classInfo.Value.StartLine,
            ClassEndLine = classInfo.Value.EndLine,
            SuggestedInterfaceName = "I" + classInfo.Value.Name
        };

        // Extract public members that can be part of an interface
        ExtractInterfaceMembers(lines, classInfo.Value.StartLine, classInfo.Value.EndLine, info.Members);

        // Find existing interfaces the class implements
        FindExistingInterfaces(lines, classInfo.Value.StartLine, info.ExistingInterfaces);

        if (info.Members.Count == 0)
        {
            return null; // No eligible members for interface
        }

        return info;
    }

    private (string Name, string Declaration, int StartLine, int EndLine)? FindClassAtCursor(string[] lines, int cursorLine)
    {
        // First, find the class that contains the cursor position
        int classStartLine = -1;
        string className = "";
        string classDeclaration = "";

        // Search backwards for class declaration
        for (int i = cursorLine - 1; i >= 0; i--)
        {
            var classMatch = Regex.Match(lines[i], @"^\s*(?:Public\s+|Private\s+)?(?:MustInherit\s+)?Class\s+(\w+)", RegexOptions.IgnoreCase);
            if (classMatch.Success)
            {
                classStartLine = i + 1;
                className = classMatch.Groups[1].Value;
                classDeclaration = lines[i].Trim();
                break;
            }

            // If we hit End Class without finding a Class, we're not inside a class
            if (Regex.IsMatch(lines[i], @"^\s*End\s+Class", RegexOptions.IgnoreCase))
            {
                break;
            }
        }

        if (classStartLine == -1)
        {
            // Try searching forward for class declaration
            for (int i = cursorLine - 1; i < lines.Length; i++)
            {
                var classMatch = Regex.Match(lines[i], @"^\s*(?:Public\s+|Private\s+)?(?:MustInherit\s+)?Class\s+(\w+)", RegexOptions.IgnoreCase);
                if (classMatch.Success)
                {
                    classStartLine = i + 1;
                    className = classMatch.Groups[1].Value;
                    classDeclaration = lines[i].Trim();
                    break;
                }
            }
        }

        if (classStartLine == -1)
        {
            return null;
        }

        // Find End Class
        int classEndLine = -1;
        int nestingLevel = 1;
        for (int i = classStartLine; i < lines.Length; i++)
        {
            if (Regex.IsMatch(lines[i], @"^\s*(?:Public\s+|Private\s+)?(?:MustInherit\s+)?Class\s+\w+", RegexOptions.IgnoreCase))
            {
                nestingLevel++;
            }
            else if (Regex.IsMatch(lines[i], @"^\s*End\s+Class", RegexOptions.IgnoreCase))
            {
                nestingLevel--;
                if (nestingLevel == 0)
                {
                    classEndLine = i + 1;
                    break;
                }
            }
        }

        if (classEndLine == -1)
        {
            classEndLine = lines.Length;
        }

        return (className, classDeclaration, classStartLine, classEndLine);
    }

    private void ExtractInterfaceMembers(string[] lines, int classStartLine, int classEndLine, List<InterfaceMemberCandidate> members)
    {
        int i = classStartLine; // Skip class declaration line

        while (i < classEndLine - 1)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            // Only consider Public members for interface
            if (!trimmed.StartsWith("Public ", StringComparison.OrdinalIgnoreCase))
            {
                i++;
                continue;
            }

            // Check for Sub
            var subMatch = Regex.Match(trimmed, @"^Public\s+(?:Overridable\s+)?Sub\s+(\w+)\s*\(([^)]*)\)", RegexOptions.IgnoreCase);
            if (subMatch.Success)
            {
                var memberName = subMatch.Groups[1].Value;
                var parameters = subMatch.Groups[2].Value;
                var endLine = FindEndOfBlock(lines, i, "Sub");

                members.Add(new InterfaceMemberCandidate
                {
                    Name = memberName,
                    Kind = InterfaceMemberKind.Sub,
                    Signature = trimmed,
                    ReturnType = null,
                    Parameters = ParseParameterStrings(parameters),
                    StartLine = i + 1,
                    EndLine = endLine + 1,
                    InterfaceSignature = $"Sub {memberName}({parameters})"
                });

                i = endLine + 1;
                continue;
            }

            // Check for Function
            var funcMatch = Regex.Match(trimmed, @"^Public\s+(?:Overridable\s+)?Function\s+(\w+)\s*\(([^)]*)\)\s+As\s+(\w+)", RegexOptions.IgnoreCase);
            if (funcMatch.Success)
            {
                var memberName = funcMatch.Groups[1].Value;
                var parameters = funcMatch.Groups[2].Value;
                var returnType = funcMatch.Groups[3].Value;
                var endLine = FindEndOfBlock(lines, i, "Function");

                members.Add(new InterfaceMemberCandidate
                {
                    Name = memberName,
                    Kind = InterfaceMemberKind.Function,
                    Signature = trimmed,
                    ReturnType = returnType,
                    Parameters = ParseParameterStrings(parameters),
                    StartLine = i + 1,
                    EndLine = endLine + 1,
                    InterfaceSignature = $"Function {memberName}({parameters}) As {returnType}"
                });

                i = endLine + 1;
                continue;
            }

            // Check for Property
            var propMatch = Regex.Match(trimmed, @"^Public\s+(?:Overridable\s+)?Property\s+(\w+)(?:\s*\(([^)]*)\))?\s+As\s+(\w+)", RegexOptions.IgnoreCase);
            if (propMatch.Success)
            {
                var memberName = propMatch.Groups[1].Value;
                var parameters = propMatch.Groups[2].Success ? propMatch.Groups[2].Value : "";
                var propType = propMatch.Groups[3].Value;
                var endLine = FindEndOfBlock(lines, i, "Property");

                // Check if it has both Get and Set
                var hasGet = false;
                var hasSet = false;
                for (int j = i + 1; j <= endLine && j < lines.Length; j++)
                {
                    if (Regex.IsMatch(lines[j], @"^\s*Get", RegexOptions.IgnoreCase))
                        hasGet = true;
                    if (Regex.IsMatch(lines[j], @"^\s*Set", RegexOptions.IgnoreCase))
                        hasSet = true;
                }

                var indexerPart = string.IsNullOrEmpty(parameters) ? "" : $"({parameters})";
                var interfaceSig = $"Property {memberName}{indexerPart} As {propType}";

                members.Add(new InterfaceMemberCandidate
                {
                    Name = memberName,
                    Kind = InterfaceMemberKind.Property,
                    Signature = trimmed,
                    ReturnType = propType,
                    Parameters = ParseParameterStrings(parameters),
                    StartLine = i + 1,
                    EndLine = endLine + 1,
                    InterfaceSignature = interfaceSig
                });

                i = endLine + 1;
                continue;
            }

            // Check for Event
            var eventMatch = Regex.Match(trimmed, @"^Public\s+Event\s+(\w+)\s*\(([^)]*)\)", RegexOptions.IgnoreCase);
            if (eventMatch.Success)
            {
                var memberName = eventMatch.Groups[1].Value;
                var parameters = eventMatch.Groups[2].Value;

                members.Add(new InterfaceMemberCandidate
                {
                    Name = memberName,
                    Kind = InterfaceMemberKind.Event,
                    Signature = trimmed,
                    ReturnType = null,
                    Parameters = ParseParameterStrings(parameters),
                    StartLine = i + 1,
                    EndLine = i + 1,
                    InterfaceSignature = $"Event {memberName}({parameters})"
                });

                i++;
                continue;
            }

            i++;
        }
    }

    private int FindEndOfBlock(string[] lines, int startLine, string blockType)
    {
        var endPattern = $@"^\s*End\s+{blockType}";
        int nestingLevel = 1;

        for (int i = startLine + 1; i < lines.Length; i++)
        {
            var line = lines[i];

            // Check for nested block start
            if (Regex.IsMatch(line, $@"^\s*(?:Public\s+|Private\s+|Protected\s+|Friend\s+)?(?:Overridable\s+)?{blockType}\s+", RegexOptions.IgnoreCase))
            {
                nestingLevel++;
            }
            else if (Regex.IsMatch(line, endPattern, RegexOptions.IgnoreCase))
            {
                nestingLevel--;
                if (nestingLevel == 0)
                {
                    return i;
                }
            }
        }

        return lines.Length - 1;
    }

    private List<string> ParseParameterStrings(string parameters)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(parameters))
            return result;

        var parts = parameters.Split(',');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                result.Add(trimmed);
            }
        }

        return result;
    }

    private void FindExistingInterfaces(string[] lines, int classStartLine, List<string> existingInterfaces)
    {
        // Look for Implements statements after the class declaration
        for (int i = classStartLine; i < Math.Min(classStartLine + 10, lines.Length); i++)
        {
            var implementsMatch = Regex.Match(lines[i], @"^\s*Implements\s+(.+)$", RegexOptions.IgnoreCase);
            if (implementsMatch.Success)
            {
                var interfaces = implementsMatch.Groups[1].Value.Split(',')
                    .Select(iface => iface.Trim())
                    .Where(iface => !string.IsNullOrEmpty(iface));

                existingInterfaces.AddRange(interfaces);
            }

            // Stop if we hit member declarations
            if (Regex.IsMatch(lines[i], @"^\s*(Public|Private|Protected|Friend|Sub|Function|Property|Dim|Const|Event)", RegexOptions.IgnoreCase) &&
                !Regex.IsMatch(lines[i], @"^\s*(Inherits|Implements)", RegexOptions.IgnoreCase))
            {
                break;
            }
        }
    }

    public async Task<ConvertToInterfaceResult> ConvertToInterfaceAsync(string filePath, int line, int column, ConvertToInterfaceOptions options, CancellationToken cancellationToken = default)
    {
        var info = await GetConvertToInterfaceInfoAsync(filePath, line, column, cancellationToken);
        if (info == null)
        {
            return new ConvertToInterfaceResult
            {
                Success = false,
                ErrorMessage = "Could not find a class at the specified location"
            };
        }

        if (string.IsNullOrEmpty(options.InterfaceName))
        {
            return new ConvertToInterfaceResult
            {
                Success = false,
                ErrorMessage = "Interface name is required"
            };
        }

        if (options.MemberNames.Count == 0)
        {
            return new ConvertToInterfaceResult
            {
                Success = false,
                ErrorMessage = "At least one member must be selected"
            };
        }

        // Get selected members
        var selectedMembers = info.Members
            .Where(m => options.MemberNames.Contains(m.Name))
            .ToList();

        if (selectedMembers.Count == 0)
        {
            return new ConvertToInterfaceResult
            {
                Success = false,
                ErrorMessage = "No valid members selected"
            };
        }

        var content = await _fileService.ReadFileAsync(filePath, cancellationToken);
        var lines = content.Split('\n').ToList();
        var fileEdits = new List<FileEdit>();

        // Generate interface code
        var interfaceCode = GenerateInterfaceCode(options.InterfaceName, selectedMembers);

        if (options.CreateInSeparateFile)
        {
            // Create interface in a new file
            var interfaceFilePath = options.InterfaceFilePath;
            if (string.IsNullOrEmpty(interfaceFilePath))
            {
                var directory = Path.GetDirectoryName(filePath)!;
                interfaceFilePath = Path.Combine(directory, options.InterfaceName + ".bas");
            }

            // Write interface to new file
            await _fileService.WriteFileAsync(interfaceFilePath, interfaceCode, cancellationToken);

            // Update the class to implement the interface
            var updatedContent = AddImplementsToClass(lines, info.ClassLine, options.InterfaceName, info.ExistingInterfaces);
            await _fileService.WriteFileAsync(filePath, updatedContent, cancellationToken);

            return new ConvertToInterfaceResult
            {
                Success = true,
                InterfaceName = options.InterfaceName,
                InterfaceFilePath = interfaceFilePath,
                MembersIncluded = selectedMembers.Count,
                FileEdits = new List<FileEdit>
                {
                    new FileEdit { FilePath = filePath, Edits = new List<TextEdit>() },
                    new FileEdit { FilePath = interfaceFilePath, Edits = new List<TextEdit>() }
                }
            };
        }
        else
        {
            // Add interface above the class in the same file
            int insertLine = info.ClassLine - 1;

            // Find any Imports statements to insert after
            int lastImportLine = 0;
            for (int i = 0; i < insertLine; i++)
            {
                if (Regex.IsMatch(lines[i], @"^\s*Imports\s+", RegexOptions.IgnoreCase))
                {
                    lastImportLine = i + 1;
                }
            }

            if (options.AddAboveClass)
            {
                insertLine = info.ClassLine - 1;
            }
            else if (lastImportLine > 0)
            {
                insertLine = lastImportLine;
            }

            // Insert interface code
            var interfaceLines = interfaceCode.Split('\n').ToList();
            interfaceLines.Add(""); // Add blank line after interface

            for (int i = interfaceLines.Count - 1; i >= 0; i--)
            {
                lines.Insert(insertLine, interfaceLines[i]);
            }

            // Add implements to class (need to adjust line number due to insertion)
            var classLineAdjusted = info.ClassLine + interfaceLines.Count;
            var newLines = lines.ToArray();
            var updatedContent = AddImplementsToClass(newLines.ToList(), classLineAdjusted, options.InterfaceName, info.ExistingInterfaces);

            await _fileService.WriteFileAsync(filePath, updatedContent, cancellationToken);

            return new ConvertToInterfaceResult
            {
                Success = true,
                InterfaceName = options.InterfaceName,
                InterfaceFilePath = filePath,
                MembersIncluded = selectedMembers.Count,
                FileEdits = new List<FileEdit>
                {
                    new FileEdit { FilePath = filePath, Edits = new List<TextEdit>() }
                }
            };
        }
    }

    private string GenerateInterfaceCode(string interfaceName, List<InterfaceMemberCandidate> members)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Public Interface {interfaceName}");

        foreach (var member in members)
        {
            sb.AppendLine($"    {member.InterfaceSignature}");
        }

        sb.AppendLine("End Interface");
        return sb.ToString();
    }

    private string AddImplementsToClass(List<string> lines, int classLine, string interfaceName, List<string> existingInterfaces)
    {
        // Find where to add Implements statement
        int insertLine = classLine; // After class declaration

        // Check if there's already an Inherits line
        bool hasInherits = false;
        bool hasImplements = false;
        int implementsLine = -1;

        for (int i = classLine; i < Math.Min(classLine + 10, lines.Count); i++)
        {
            if (Regex.IsMatch(lines[i], @"^\s*Inherits\s+", RegexOptions.IgnoreCase))
            {
                hasInherits = true;
                insertLine = i + 1;
            }
            else if (Regex.IsMatch(lines[i], @"^\s*Implements\s+", RegexOptions.IgnoreCase))
            {
                hasImplements = true;
                implementsLine = i;
                break;
            }
            else if (Regex.IsMatch(lines[i], @"^\s*(Public|Private|Protected|Friend|Sub|Function|Property|Dim|Const|Event)", RegexOptions.IgnoreCase))
            {
                break;
            }
        }

        if (hasImplements && implementsLine >= 0)
        {
            // Append to existing Implements line
            var currentImplements = lines[implementsLine].TrimEnd();
            lines[implementsLine] = currentImplements + ", " + interfaceName;
        }
        else
        {
            // Insert new Implements line
            var indent = "    ";
            if (classLine < lines.Count)
            {
                var classLineText = lines[classLine - 1];
                var match = Regex.Match(classLineText, @"^(\s*)");
                if (match.Success)
                {
                    indent = match.Groups[1].Value + "    ";
                }
            }

            lines.Insert(insertLine, $"{indent}Implements {interfaceName}");
        }

        return string.Join("\n", lines);
    }

    #endregion

    #region Invert If

    public async Task<InvertIfInfo?> GetInvertIfInfoAsync(string filePath, int line, int column, CancellationToken cancellationToken = default)
    {
        var content = await _fileService.ReadFileAsync(filePath, cancellationToken);
        var lines = content.Split('\n');

        if (line < 1 || line > lines.Length)
        {
            return null;
        }

        var currentLine = lines[line - 1];
        var trimmed = currentLine.TrimStart();

        // Check if we're on an If statement
        var ifMatch = Regex.Match(trimmed, @"^If\s+(.+)\s+Then\s*$", RegexOptions.IgnoreCase);
        if (!ifMatch.Success)
        {
            return null;
        }

        var condition = ifMatch.Groups[1].Value;

        // Find the If block structure
        var ifBlockInfo = ParseIfBlockForInvert(lines, line - 1);
        if (ifBlockInfo == null || string.IsNullOrEmpty(ifBlockInfo.Value.ElseBranch))
        {
            return null;
        }

        var invertedCondition = InvertCondition(condition);
        var indent = GetLineIndent(currentLine);

        var preview = GenerateInvertedIfPreview(invertedCondition, ifBlockInfo.Value.ElseBranch, ifBlockInfo.Value.IfBranch, indent);

        return new InvertIfInfo
        {
            OriginalCondition = condition,
            InvertedCondition = invertedCondition,
            IfBranch = ifBlockInfo.Value.IfBranch,
            ElseBranch = ifBlockInfo.Value.ElseBranch,
            StartLine = line,
            EndLine = ifBlockInfo.Value.EndLine,
            FilePath = filePath,
            Preview = preview
        };
    }

    private (string IfBranch, string ElseBranch, int EndLine)? ParseIfBlockForInvert(string[] lines, int startIndex)
    {
        var ifBranchLines = new List<string>();
        var elseBranchLines = new List<string>();
        var inElse = false;
        var nestingLevel = 1;
        var endLine = startIndex;

        for (int i = startIndex + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            if (Regex.IsMatch(trimmed, @"^If\s+.+\s+Then\s*$", RegexOptions.IgnoreCase))
            {
                nestingLevel++;
            }

            if (Regex.IsMatch(trimmed, @"^End\s+If", RegexOptions.IgnoreCase))
            {
                nestingLevel--;
                if (nestingLevel == 0)
                {
                    endLine = i + 1;
                    break;
                }
            }

            if (nestingLevel == 1 && Regex.IsMatch(trimmed, @"^Else\s*$", RegexOptions.IgnoreCase))
            {
                inElse = true;
                continue;
            }

            if (nestingLevel == 1 && Regex.IsMatch(trimmed, @"^ElseIf\s+", RegexOptions.IgnoreCase))
            {
                return null;
            }

            if (inElse)
            {
                elseBranchLines.Add(line);
            }
            else
            {
                ifBranchLines.Add(line);
            }
        }

        return (string.Join("\n", ifBranchLines), string.Join("\n", elseBranchLines), endLine);
    }

    private string InvertCondition(string condition)
    {
        condition = condition.Trim();

        if (condition.StartsWith("Not ", StringComparison.OrdinalIgnoreCase))
        {
            return condition.Substring(4).Trim();
        }

        if (Regex.IsMatch(condition, @"^(.+)\s*<>\s*(.+)$"))
        {
            var match = Regex.Match(condition, @"^(.+)\s*<>\s*(.+)$");
            return $"{match.Groups[1].Value.Trim()} = {match.Groups[2].Value.Trim()}";
        }

        if (Regex.IsMatch(condition, @"^(.+)\s*>=\s*(.+)$"))
        {
            var match = Regex.Match(condition, @"^(.+)\s*>=\s*(.+)$");
            return $"{match.Groups[1].Value.Trim()} < {match.Groups[2].Value.Trim()}";
        }

        if (Regex.IsMatch(condition, @"^(.+)\s*<=\s*(.+)$"))
        {
            var match = Regex.Match(condition, @"^(.+)\s*<=\s*(.+)$");
            return $"{match.Groups[1].Value.Trim()} > {match.Groups[2].Value.Trim()}";
        }

        if (Regex.IsMatch(condition, @"^(.+)\s*>\s*(.+)$"))
        {
            var match = Regex.Match(condition, @"^(.+)\s*>\s*(.+)$");
            return $"{match.Groups[1].Value.Trim()} <= {match.Groups[2].Value.Trim()}";
        }

        if (Regex.IsMatch(condition, @"^(.+)\s*<\s*(.+)$"))
        {
            var match = Regex.Match(condition, @"^(.+)\s*<\s*(.+)$");
            return $"{match.Groups[1].Value.Trim()} >= {match.Groups[2].Value.Trim()}";
        }

        if (Regex.IsMatch(condition, @"^(.+)\s*=\s*(.+)$"))
        {
            var match = Regex.Match(condition, @"^(.+)\s*=\s*(.+)$");
            return $"{match.Groups[1].Value.Trim()} <> {match.Groups[2].Value.Trim()}";
        }

        return $"Not ({condition})";
    }

    private string GenerateInvertedIfPreview(string condition, string newIfBranch, string newElseBranch, string indent)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{indent}If {condition} Then");
        sb.AppendLine(newIfBranch);
        sb.AppendLine($"{indent}Else");
        sb.AppendLine(newElseBranch);
        sb.AppendLine($"{indent}End If");
        return sb.ToString();
    }

    public async Task<InvertIfResult> InvertIfAsync(string filePath, int line, int column, CancellationToken cancellationToken = default)
    {
        var info = await GetInvertIfInfoAsync(filePath, line, column, cancellationToken);
        if (info == null)
        {
            return new InvertIfResult
            {
                Success = false,
                ErrorMessage = "No If statement with Else branch found at the specified location"
            };
        }

        var content = await _fileService.ReadFileAsync(filePath, cancellationToken);
        var lines = content.Split('\n').ToList();
        var indent = GetLineIndent(lines[line - 1]);

        var newLines = new List<string> { $"{indent}If {info.InvertedCondition} Then" };
        newLines.AddRange(info.ElseBranch.Split('\n'));
        newLines.Add($"{indent}Else");
        newLines.AddRange(info.IfBranch.Split('\n'));
        newLines.Add($"{indent}End If");

        for (int i = info.EndLine - 1; i >= info.StartLine - 1; i--)
        {
            lines.RemoveAt(i);
        }

        lines.InsertRange(info.StartLine - 1, newLines);

        var newContent = string.Join("\n", lines);
        await _fileService.WriteFileAsync(filePath, newContent, cancellationToken);

        return new InvertIfResult
        {
            Success = true,
            FileEdits = new List<FileEdit> { new FileEdit { FilePath = filePath, Edits = new List<TextEdit>() } }
        };
    }

    #endregion

    #region Convert to Select Case

    public async Task<ConvertToSelectCaseInfo?> GetConvertToSelectCaseInfoAsync(string filePath, int line, int column, CancellationToken cancellationToken = default)
    {
        var content = await _fileService.ReadFileAsync(filePath, cancellationToken);
        var lines = content.Split('\n');

        if (line < 1 || line > lines.Length)
        {
            return null;
        }

        var currentLine = lines[line - 1];
        var trimmed = currentLine.TrimStart();

        var ifMatch = Regex.Match(trimmed, @"^If\s+(.+)\s+Then\s*$", RegexOptions.IgnoreCase);
        if (!ifMatch.Success)
        {
            return null;
        }

        var chainInfo = ParseIfElseIfChain(lines, line - 1);
        if (chainInfo == null || chainInfo.Value.Branches.Count < 2)
        {
            return null;
        }

        var testExpression = ExtractTestExpression(chainInfo.Value.Branches);
        if (string.IsNullOrEmpty(testExpression))
        {
            return null;
        }

        var indent = GetLineIndent(currentLine);
        var preview = GenerateSelectCasePreview(testExpression, chainInfo.Value.Branches, chainInfo.Value.ElseBranch, indent);

        return new ConvertToSelectCaseInfo
        {
            TestExpression = testExpression,
            Branches = chainInfo.Value.Branches,
            ElseBranch = chainInfo.Value.ElseBranch,
            StartLine = line,
            EndLine = chainInfo.Value.EndLine,
            FilePath = filePath,
            Preview = preview
        };
    }

    private (List<IfElseIfBranch> Branches, string? ElseBranch, int EndLine)? ParseIfElseIfChain(string[] lines, int startIndex)
    {
        var branches = new List<IfElseIfBranch>();
        var currentBranchBody = new List<string>();
        string? currentCondition = null;
        var startLine = startIndex;
        var nestingLevel = 1;
        string? elseBranch = null;
        var endLine = startIndex;

        var firstMatch = Regex.Match(lines[startIndex].TrimStart(), @"^If\s+(.+)\s+Then\s*$", RegexOptions.IgnoreCase);
        if (firstMatch.Success)
        {
            currentCondition = firstMatch.Groups[1].Value;
        }

        for (int i = startIndex + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            if (Regex.IsMatch(trimmed, @"^If\s+.+\s+Then\s*$", RegexOptions.IgnoreCase))
            {
                nestingLevel++;
                currentBranchBody.Add(line);
                continue;
            }

            if (Regex.IsMatch(trimmed, @"^End\s+If", RegexOptions.IgnoreCase))
            {
                nestingLevel--;
                if (nestingLevel == 0)
                {
                    if (currentCondition != null)
                    {
                        branches.Add(new IfElseIfBranch
                        {
                            Condition = currentCondition,
                            Body = string.Join("\n", currentBranchBody),
                            StartLine = startLine + 1,
                            EndLine = i
                        });
                    }
                    endLine = i + 1;
                    break;
                }
                currentBranchBody.Add(line);
                continue;
            }

            if (nestingLevel == 1)
            {
                var elseIfMatch = Regex.Match(trimmed, @"^ElseIf\s+(.+)\s+Then\s*$", RegexOptions.IgnoreCase);
                if (elseIfMatch.Success)
                {
                    if (currentCondition != null)
                    {
                        branches.Add(new IfElseIfBranch
                        {
                            Condition = currentCondition,
                            Body = string.Join("\n", currentBranchBody),
                            StartLine = startLine + 1,
                            EndLine = i - 1
                        });
                    }

                    currentCondition = elseIfMatch.Groups[1].Value;
                    currentBranchBody.Clear();
                    startLine = i;
                    continue;
                }

                if (Regex.IsMatch(trimmed, @"^Else\s*$", RegexOptions.IgnoreCase))
                {
                    if (currentCondition != null)
                    {
                        branches.Add(new IfElseIfBranch
                        {
                            Condition = currentCondition,
                            Body = string.Join("\n", currentBranchBody),
                            StartLine = startLine + 1,
                            EndLine = i - 1
                        });
                    }

                    var elseBodyLines = new List<string>();
                    for (int j = i + 1; j < lines.Length; j++)
                    {
                        if (Regex.IsMatch(lines[j].TrimStart(), @"^End\s+If", RegexOptions.IgnoreCase))
                        {
                            endLine = j + 1;
                            break;
                        }
                        elseBodyLines.Add(lines[j]);
                    }
                    elseBranch = string.Join("\n", elseBodyLines);
                    break;
                }
            }

            currentBranchBody.Add(line);
        }

        if (branches.Count == 0)
        {
            return null;
        }

        return (branches, elseBranch, endLine);
    }

    private string? ExtractTestExpression(List<IfElseIfBranch> branches)
    {
        string? testExpression = null;

        foreach (var branch in branches)
        {
            var match = Regex.Match(branch.Condition, @"^(\w+(?:\.\w+)*)\s*=\s*(.+)$", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return null;
            }

            var expr = match.Groups[1].Value;
            var value = match.Groups[2].Value.Trim();

            if (testExpression == null)
            {
                testExpression = expr;
            }
            else if (!testExpression.Equals(expr, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            branch.CaseValue = value;
        }

        return testExpression;
    }

    private string GenerateSelectCasePreview(string testExpression, List<IfElseIfBranch> branches, string? elseBranch, string indent)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{indent}Select Case {testExpression}");

        foreach (var branch in branches)
        {
            sb.AppendLine($"{indent}    Case {branch.CaseValue}");
            foreach (var bodyLine in branch.Body.Split('\n'))
            {
                if (!string.IsNullOrWhiteSpace(bodyLine))
                {
                    sb.AppendLine($"{indent}        {bodyLine.TrimStart()}");
                }
            }
        }

        if (!string.IsNullOrEmpty(elseBranch))
        {
            sb.AppendLine($"{indent}    Case Else");
            foreach (var elseLine in elseBranch.Split('\n'))
            {
                if (!string.IsNullOrWhiteSpace(elseLine))
                {
                    sb.AppendLine($"{indent}        {elseLine.TrimStart()}");
                }
            }
        }

        sb.AppendLine($"{indent}End Select");
        return sb.ToString();
    }

    public async Task<ConvertToSelectCaseResult> ConvertToSelectCaseAsync(string filePath, int line, int column, CancellationToken cancellationToken = default)
    {
        var info = await GetConvertToSelectCaseInfoAsync(filePath, line, column, cancellationToken);
        if (info == null)
        {
            return new ConvertToSelectCaseResult
            {
                Success = false,
                ErrorMessage = "No convertible If-ElseIf chain found at the specified location"
            };
        }

        var content = await _fileService.ReadFileAsync(filePath, cancellationToken);
        var lines = content.Split('\n').ToList();
        var indent = GetLineIndent(lines[line - 1]);

        var newLines = new List<string> { $"{indent}Select Case {info.TestExpression}" };

        foreach (var branch in info.Branches)
        {
            newLines.Add($"{indent}    Case {branch.CaseValue}");
            foreach (var bodyLine in branch.Body.Split('\n'))
            {
                if (!string.IsNullOrWhiteSpace(bodyLine))
                {
                    newLines.Add($"{indent}        {bodyLine.TrimStart()}");
                }
            }
        }

        if (!string.IsNullOrEmpty(info.ElseBranch))
        {
            newLines.Add($"{indent}    Case Else");
            foreach (var elseLine in info.ElseBranch.Split('\n'))
            {
                if (!string.IsNullOrWhiteSpace(elseLine))
                {
                    newLines.Add($"{indent}        {elseLine.TrimStart()}");
                }
            }
        }

        newLines.Add($"{indent}End Select");

        for (int i = info.EndLine - 1; i >= info.StartLine - 1; i--)
        {
            lines.RemoveAt(i);
        }

        lines.InsertRange(info.StartLine - 1, newLines);

        await _fileService.WriteFileAsync(filePath, string.Join("\n", lines), cancellationToken);

        return new ConvertToSelectCaseResult
        {
            Success = true,
            CasesCreated = info.Branches.Count,
            FileEdits = new List<FileEdit> { new FileEdit { FilePath = filePath, Edits = new List<TextEdit>() } }
        };
    }

    #endregion

    #region Split Declaration

    public async Task<SplitDeclarationInfo?> GetSplitDeclarationInfoAsync(string filePath, int line, int column, CancellationToken cancellationToken = default)
    {
        var content = await _fileService.ReadFileAsync(filePath, cancellationToken);
        var lines = content.Split('\n');

        if (line < 1 || line > lines.Length)
        {
            return null;
        }

        var currentLine = lines[line - 1];
        var trimmed = currentLine.TrimStart();

        var match = Regex.Match(trimmed, @"^Dim\s+(\w+)\s+As\s+(\w+)\s*=\s*(.+)$", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return null;
        }

        var varName = match.Groups[1].Value;
        var varType = match.Groups[2].Value;
        var initializer = match.Groups[3].Value.Trim();
        var indent = GetLineIndent(currentLine);

        return new SplitDeclarationInfo
        {
            VariableName = varName,
            VariableType = varType,
            InitializerExpression = initializer,
            DeclarationLine = currentLine.Trim(),
            Line = line,
            FilePath = filePath,
            PreviewDeclaration = $"{indent}Dim {varName} As {varType}",
            PreviewAssignment = $"{indent}{varName} = {initializer}"
        };
    }

    public async Task<SplitDeclarationResult> SplitDeclarationAsync(string filePath, int line, int column, CancellationToken cancellationToken = default)
    {
        var info = await GetSplitDeclarationInfoAsync(filePath, line, column, cancellationToken);
        if (info == null)
        {
            return new SplitDeclarationResult
            {
                Success = false,
                ErrorMessage = "No variable declaration with initialization found at the specified location"
            };
        }

        var content = await _fileService.ReadFileAsync(filePath, cancellationToken);
        var lines = content.Split('\n').ToList();
        var indent = GetLineIndent(lines[line - 1]);

        lines[line - 1] = $"{indent}Dim {info.VariableName} As {info.VariableType}";
        lines.Insert(line, $"{indent}{info.VariableName} = {info.InitializerExpression}");

        await _fileService.WriteFileAsync(filePath, string.Join("\n", lines), cancellationToken);

        return new SplitDeclarationResult
        {
            Success = true,
            FileEdits = new List<FileEdit> { new FileEdit { FilePath = filePath, Edits = new List<TextEdit>() } }
        };
    }

    #endregion

    #region Introduce Field

    public async Task<IntroduceFieldInfo?> GetIntroduceFieldInfoAsync(string filePath, int line, int column, CancellationToken cancellationToken = default)
    {
        var content = await _fileService.ReadFileAsync(filePath, cancellationToken);
        var lines = content.Split('\n');

        if (line < 1 || line > lines.Length)
        {
            return null;
        }

        var currentLine = lines[line - 1];
        var trimmed = currentLine.TrimStart();

        var match = Regex.Match(trimmed, @"^Dim\s+(\w+)\s+As\s+(\w+)(?:\s*=\s*(.+))?$", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return null;
        }

        var varName = match.Groups[1].Value;
        var varType = match.Groups[2].Value;
        var initializer = match.Groups[3].Success ? match.Groups[3].Value.Trim() : null;

        var classInfo = FindEnclosingClassForField(lines, line - 1);
        if (classInfo == null)
        {
            return null;
        }

        var suggestedFieldName = "_" + char.ToLower(varName[0]) + varName.Substring(1);

        return new IntroduceFieldInfo
        {
            VariableName = varName,
            VariableType = varType,
            InitializerExpression = initializer,
            SuggestedFieldName = suggestedFieldName,
            VariableLine = line,
            ClassName = classInfo.Value.Name,
            ClassStartLine = classInfo.Value.StartLine,
            FilePath = filePath
        };
    }

    private (string Name, int StartLine)? FindEnclosingClassForField(string[] lines, int fromLine)
    {
        for (int i = fromLine; i >= 0; i--)
        {
            var trimmed = lines[i].TrimStart();
            var classMatch = Regex.Match(trimmed, @"^(?:Public\s+|Private\s+)?Class\s+(\w+)", RegexOptions.IgnoreCase);
            if (classMatch.Success)
            {
                return (classMatch.Groups[1].Value, i + 1);
            }

            if (Regex.IsMatch(trimmed, @"^End\s+Class", RegexOptions.IgnoreCase))
            {
                return null;
            }
        }

        return null;
    }

    public async Task<IntroduceFieldResult> IntroduceFieldAsync(string filePath, int line, int column, IntroduceFieldOptions options, CancellationToken cancellationToken = default)
    {
        var info = await GetIntroduceFieldInfoAsync(filePath, line, column, cancellationToken);
        if (info == null)
        {
            return new IntroduceFieldResult
            {
                Success = false,
                ErrorMessage = "No local variable found at the specified location"
            };
        }

        if (string.IsNullOrWhiteSpace(options.FieldName))
        {
            return new IntroduceFieldResult
            {
                Success = false,
                ErrorMessage = "Field name is required"
            };
        }

        var content = await _fileService.ReadFileAsync(filePath, cancellationToken);
        var lines = content.Split('\n').ToList();

        var accessModifier = options.Accessibility switch
        {
            FieldAccessibility.Public => "Public",
            FieldAccessibility.Protected => "Protected",
            FieldAccessibility.Friend => "Friend",
            _ => "Private"
        };

        var fieldDeclaration = options.InitializeInline && info.InitializerExpression != null
            ? $"    {accessModifier} {options.FieldName} As {info.VariableType} = {info.InitializerExpression}"
            : $"    {accessModifier} {options.FieldName} As {info.VariableType}";

        var insertLine = info.ClassStartLine;
        for (int i = info.ClassStartLine; i < Math.Min(info.ClassStartLine + 10, lines.Count); i++)
        {
            var trimmed = lines[i].TrimStart();
            if (Regex.IsMatch(trimmed, @"^(Inherits|Implements)\s+", RegexOptions.IgnoreCase))
            {
                insertLine = i + 1;
            }
            else if (!string.IsNullOrWhiteSpace(trimmed) && !Regex.IsMatch(trimmed, @"^(Inherits|Implements)\s+", RegexOptions.IgnoreCase))
            {
                break;
            }
        }

        lines.Insert(insertLine, fieldDeclaration);

        var varLineAdjusted = line > insertLine ? line + 1 : line;

        if (options.RemoveLocalVariable)
        {
            if (info.InitializerExpression != null && !options.InitializeInline)
            {
                var indent = GetLineIndent(lines[varLineAdjusted - 1]);
                lines[varLineAdjusted - 1] = $"{indent}{options.FieldName} = {info.InitializerExpression}";
            }
            else
            {
                lines.RemoveAt(varLineAdjusted - 1);
            }
        }

        await _fileService.WriteFileAsync(filePath, string.Join("\n", lines), cancellationToken);

        return new IntroduceFieldResult
        {
            Success = true,
            FieldName = options.FieldName,
            FileEdits = new List<FileEdit> { new FileEdit { FilePath = filePath, Edits = new List<TextEdit>() } }
        };
    }

    private string GetLineIndent(string line)
    {
        var match = Regex.Match(line, @"^(\s*)");
        return match.Success ? match.Groups[1].Value : "";
    }

    #endregion

    #region Surround With

    public async Task<IReadOnlyList<SurroundWithOption>> GetSurroundWithOptionsAsync(string filePath, int startLine, int startColumn, int endLine, int endColumn, CancellationToken cancellationToken = default)
    {
        return await Task.FromResult(new List<SurroundWithOption>
        {
            new SurroundWithOption { Type = SurroundWithType.IfThen, Name = "If...Then...End If", Description = "Wrap with If/Then block" },
            new SurroundWithOption { Type = SurroundWithType.IfThenElse, Name = "If...Then...Else...End If", Description = "Wrap with If/Then/Else block" },
            new SurroundWithOption { Type = SurroundWithType.ForNext, Name = "For...Next", Description = "Wrap with For loop" },
            new SurroundWithOption { Type = SurroundWithType.ForEach, Name = "For Each...Next", Description = "Wrap with For Each loop" },
            new SurroundWithOption { Type = SurroundWithType.WhileWend, Name = "While...Wend", Description = "Wrap with While loop" },
            new SurroundWithOption { Type = SurroundWithType.DoLoopWhile, Name = "Do...Loop While", Description = "Wrap with Do/Loop While" },
            new SurroundWithOption { Type = SurroundWithType.DoLoopUntil, Name = "Do...Loop Until", Description = "Wrap with Do/Loop Until" },
            new SurroundWithOption { Type = SurroundWithType.DoWhileLoop, Name = "Do While...Loop", Description = "Wrap with Do While/Loop" },
            new SurroundWithOption { Type = SurroundWithType.DoUntilLoop, Name = "Do Until...Loop", Description = "Wrap with Do Until/Loop" },
            new SurroundWithOption { Type = SurroundWithType.TryCatch, Name = "Try...Catch...End Try", Description = "Wrap with Try/Catch block" },
            new SurroundWithOption { Type = SurroundWithType.TryCatchFinally, Name = "Try...Catch...Finally...End Try", Description = "Wrap with Try/Catch/Finally block" },
            new SurroundWithOption { Type = SurroundWithType.SelectCase, Name = "Select Case...End Select", Description = "Wrap with Select Case block" },
            new SurroundWithOption { Type = SurroundWithType.With, Name = "With...End With", Description = "Wrap with With block" },
            new SurroundWithOption { Type = SurroundWithType.Region, Name = "#Region...#End Region", Description = "Wrap with Region" }
        });
    }

    public async Task<SurroundWithResult> SurroundWithAsync(string filePath, int startLine, int startColumn, int endLine, int endColumn, SurroundWithType surroundType, CancellationToken cancellationToken = default)
    {
        try
        {
            var content = await _fileService.ReadFileAsync(filePath, cancellationToken);
            var lines = content.Split('\n').ToList();

            if (startLine < 1 || endLine > lines.Count)
            {
                return new SurroundWithResult { Success = false, ErrorMessage = "Invalid line range" };
            }

            // Get the selected lines
            var selectedLines = lines.Skip(startLine - 1).Take(endLine - startLine + 1).ToList();
            var indent = GetLineIndent(selectedLines.FirstOrDefault() ?? "");
            var innerIndent = indent + "    ";

            // Build the surround code
            var (startCode, endCode, cursorOffset) = GetSurroundCode(surroundType, indent, innerIndent);

            // Indent the selected content
            var indentedContent = selectedLines.Select(l =>
            {
                if (string.IsNullOrWhiteSpace(l)) return l;
                return innerIndent + l.TrimStart();
            }).ToList();

            // Build new content
            var newLines = new List<string>();
            newLines.AddRange(lines.Take(startLine - 1));
            newLines.Add(startCode);
            newLines.AddRange(indentedContent);
            newLines.Add(endCode);
            newLines.AddRange(lines.Skip(endLine));

            await _fileService.WriteFileAsync(filePath, string.Join("\n", newLines), cancellationToken);

            return new SurroundWithResult
            {
                Success = true,
                CursorLine = startLine + cursorOffset,
                CursorColumn = startCode.Length - startCode.TrimStart().Length + cursorOffset
            };
        }
        catch (Exception ex)
        {
            return new SurroundWithResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    private (string startCode, string endCode, int cursorOffset) GetSurroundCode(SurroundWithType type, string indent, string innerIndent)
    {
        return type switch
        {
            SurroundWithType.IfThen => ($"{indent}If condition Then", $"{indent}End If", 0),
            SurroundWithType.IfThenElse => ($"{indent}If condition Then", $"{innerIndent}\n{indent}Else\n{innerIndent}    ' Else code\n{indent}End If", 0),
            SurroundWithType.ForNext => ($"{indent}For i = 1 To 10", $"{indent}Next", 0),
            SurroundWithType.ForEach => ($"{indent}For Each item In collection", $"{indent}Next", 0),
            SurroundWithType.WhileWend => ($"{indent}While condition", $"{indent}Wend", 0),
            SurroundWithType.DoLoopWhile => ($"{indent}Do", $"{indent}Loop While condition", 0),
            SurroundWithType.DoLoopUntil => ($"{indent}Do", $"{indent}Loop Until condition", 0),
            SurroundWithType.DoWhileLoop => ($"{indent}Do While condition", $"{indent}Loop", 0),
            SurroundWithType.DoUntilLoop => ($"{indent}Do Until condition", $"{indent}Loop", 0),
            SurroundWithType.TryCatch => ($"{indent}Try", $"{indent}Catch ex As Exception\n{innerIndent}    ' Handle error\n{indent}End Try", 0),
            SurroundWithType.TryCatchFinally => ($"{indent}Try", $"{indent}Catch ex As Exception\n{innerIndent}    ' Handle error\n{indent}Finally\n{innerIndent}    ' Cleanup\n{indent}End Try", 0),
            SurroundWithType.SelectCase => ($"{indent}Select Case expression", $"{indent}End Select", 0),
            SurroundWithType.With => ($"{indent}With object", $"{indent}End With", 0),
            SurroundWithType.Region => ($"{indent}#Region \"Region Name\"", $"{indent}#End Region", 0),
            _ => ($"{indent}' Start", $"{indent}' End", 0)
        };
    }

    #endregion

    #region Go To Definition

    public async Task<DefinitionResult> GoToDefinitionAsync(string filePath, int line, int column, CancellationToken cancellationToken = default)
    {
        try
        {
            var content = await _fileService.ReadFileAsync(filePath, cancellationToken);
            var lines = content.Split('\n');

            if (line < 1 || line > lines.Length)
            {
                return new DefinitionResult { Success = false, ErrorMessage = "Invalid line number" };
            }

            var currentLine = lines[line - 1];
            var word = GetWordAtPosition(currentLine, column);

            if (string.IsNullOrEmpty(word))
            {
                return new DefinitionResult { Success = false, ErrorMessage = "No symbol at cursor position" };
            }

            // Search for definition in current file
            var definition = FindDefinitionInFile(lines, word, filePath);
            if (definition != null)
            {
                return definition;
            }

            // Search in project files
            var projectDir = Path.GetDirectoryName(filePath);
            if (projectDir != null)
            {
                var basFiles = Directory.GetFiles(projectDir, "*.bas", SearchOption.AllDirectories);
                foreach (var basFile in basFiles)
                {
                    if (basFile == filePath) continue;

                    var fileContent = await _fileService.ReadFileAsync(basFile, cancellationToken);
                    var fileLines = fileContent.Split('\n');
                    definition = FindDefinitionInFile(fileLines, word, basFile);
                    if (definition != null)
                    {
                        return definition;
                    }
                }
            }

            return new DefinitionResult { Success = false, ErrorMessage = $"Definition of '{word}' not found" };
        }
        catch (Exception ex)
        {
            return new DefinitionResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    private DefinitionResult? FindDefinitionInFile(string[] lines, string symbol, string filePath)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            // Check for Sub definition
            var subMatch = Regex.Match(line, $@"^\s*(Public\s+|Private\s+|Protected\s+|Friend\s+)?(Shared\s+)?Sub\s+{Regex.Escape(symbol)}\s*\(", RegexOptions.IgnoreCase);
            if (subMatch.Success)
            {
                return new DefinitionResult
                {
                    Success = true,
                    FilePath = filePath,
                    Line = i + 1,
                    Column = line.IndexOf(symbol, StringComparison.OrdinalIgnoreCase) + 1,
                    SymbolName = symbol,
                    SymbolKind = SymbolKind.Method,
                    Preview = line
                };
            }

            // Check for Function definition
            var funcMatch = Regex.Match(line, $@"^\s*(Public\s+|Private\s+|Protected\s+|Friend\s+)?(Shared\s+)?Function\s+{Regex.Escape(symbol)}\s*\(", RegexOptions.IgnoreCase);
            if (funcMatch.Success)
            {
                return new DefinitionResult
                {
                    Success = true,
                    FilePath = filePath,
                    Line = i + 1,
                    Column = line.IndexOf(symbol, StringComparison.OrdinalIgnoreCase) + 1,
                    SymbolName = symbol,
                    SymbolKind = SymbolKind.Function,
                    Preview = line
                };
            }

            // Check for Class definition
            var classMatch = Regex.Match(line, $@"^\s*(Public\s+|Private\s+|Protected\s+|Friend\s+)?Class\s+{Regex.Escape(symbol)}\b", RegexOptions.IgnoreCase);
            if (classMatch.Success)
            {
                return new DefinitionResult
                {
                    Success = true,
                    FilePath = filePath,
                    Line = i + 1,
                    Column = line.IndexOf(symbol, StringComparison.OrdinalIgnoreCase) + 1,
                    SymbolName = symbol,
                    SymbolKind = SymbolKind.Class,
                    Preview = line
                };
            }

            // Check for Module definition
            var moduleMatch = Regex.Match(line, $@"^\s*(Public\s+|Private\s+)?Module\s+{Regex.Escape(symbol)}\b", RegexOptions.IgnoreCase);
            if (moduleMatch.Success)
            {
                return new DefinitionResult
                {
                    Success = true,
                    FilePath = filePath,
                    Line = i + 1,
                    Column = line.IndexOf(symbol, StringComparison.OrdinalIgnoreCase) + 1,
                    SymbolName = symbol,
                    SymbolKind = SymbolKind.Module,
                    Preview = line
                };
            }

            // Check for Interface definition
            var interfaceMatch = Regex.Match(line, $@"^\s*(Public\s+|Private\s+)?Interface\s+{Regex.Escape(symbol)}\b", RegexOptions.IgnoreCase);
            if (interfaceMatch.Success)
            {
                return new DefinitionResult
                {
                    Success = true,
                    FilePath = filePath,
                    Line = i + 1,
                    Column = line.IndexOf(symbol, StringComparison.OrdinalIgnoreCase) + 1,
                    SymbolName = symbol,
                    SymbolKind = SymbolKind.Interface,
                    Preview = line
                };
            }

            // Check for Property definition
            var propMatch = Regex.Match(line, $@"^\s*(Public\s+|Private\s+|Protected\s+|Friend\s+)?(Shared\s+)?Property\s+{Regex.Escape(symbol)}\b", RegexOptions.IgnoreCase);
            if (propMatch.Success)
            {
                return new DefinitionResult
                {
                    Success = true,
                    FilePath = filePath,
                    Line = i + 1,
                    Column = line.IndexOf(symbol, StringComparison.OrdinalIgnoreCase) + 1,
                    SymbolName = symbol,
                    SymbolKind = SymbolKind.Property,
                    Preview = line
                };
            }

            // Check for Enum definition
            var enumMatch = Regex.Match(line, $@"^\s*(Public\s+|Private\s+)?Enum\s+{Regex.Escape(symbol)}\b", RegexOptions.IgnoreCase);
            if (enumMatch.Success)
            {
                return new DefinitionResult
                {
                    Success = true,
                    FilePath = filePath,
                    Line = i + 1,
                    Column = line.IndexOf(symbol, StringComparison.OrdinalIgnoreCase) + 1,
                    SymbolName = symbol,
                    SymbolKind = SymbolKind.Enum,
                    Preview = line
                };
            }

            // Check for Const definition
            var constMatch = Regex.Match(line, $@"^\s*(Public\s+|Private\s+|Protected\s+)?Const\s+{Regex.Escape(symbol)}\b", RegexOptions.IgnoreCase);
            if (constMatch.Success)
            {
                return new DefinitionResult
                {
                    Success = true,
                    FilePath = filePath,
                    Line = i + 1,
                    Column = line.IndexOf(symbol, StringComparison.OrdinalIgnoreCase) + 1,
                    SymbolName = symbol,
                    SymbolKind = SymbolKind.Constant,
                    Preview = line
                };
            }

            // Check for Dim (variable/field) definition at class level
            var dimMatch = Regex.Match(line, $@"^\s*(Public\s+|Private\s+|Protected\s+|Friend\s+)?(Shared\s+)?Dim\s+{Regex.Escape(symbol)}\b", RegexOptions.IgnoreCase);
            if (dimMatch.Success)
            {
                return new DefinitionResult
                {
                    Success = true,
                    FilePath = filePath,
                    Line = i + 1,
                    Column = line.IndexOf(symbol, StringComparison.OrdinalIgnoreCase) + 1,
                    SymbolName = symbol,
                    SymbolKind = SymbolKind.Field,
                    Preview = line
                };
            }
        }

        return null;
    }

    public async Task<PeekDefinitionResult> PeekDefinitionAsync(string filePath, int line, int column, CancellationToken cancellationToken = default)
    {
        var definition = await GoToDefinitionAsync(filePath, line, column, cancellationToken);

        if (!definition.Success || definition.FilePath == null)
        {
            return new PeekDefinitionResult { Success = false, ErrorMessage = definition.ErrorMessage };
        }

        try
        {
            var content = await _fileService.ReadFileAsync(definition.FilePath, cancellationToken);
            var lines = content.Split('\n');
            var defLine = definition.Line - 1;

            // Find the end of the definition
            var endLine = FindDefinitionEnd(lines, defLine, definition.SymbolKind);

            var sourceCode = string.Join("\n", lines.Skip(defLine).Take(endLine - defLine + 1));

            return new PeekDefinitionResult
            {
                Success = true,
                FilePath = definition.FilePath,
                StartLine = definition.Line,
                EndLine = endLine + 1,
                SymbolName = definition.SymbolName,
                SymbolKind = definition.SymbolKind,
                SourceCode = sourceCode
            };
        }
        catch (Exception ex)
        {
            return new PeekDefinitionResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    private int FindDefinitionEnd(string[] lines, int startLine, SymbolKind kind)
    {
        // Determine possible end keywords based on kind
        var endKeywords = kind switch
        {
            SymbolKind.Method => new[] { "End Sub", "End Function" },
            SymbolKind.Function => new[] { "End Function" },
            SymbolKind.Class => new[] { "End Class" },
            SymbolKind.Module => new[] { "End Module" },
            SymbolKind.Interface => new[] { "End Interface" },
            SymbolKind.Property => new[] { "End Property" },
            SymbolKind.Enum => new[] { "End Enum" },
            _ => Array.Empty<string>()
        };

        if (endKeywords.Length == 0)
        {
            return startLine; // Single line definition
        }

        for (int i = startLine + 1; i < lines.Length; i++)
        {
            var trimmedLine = lines[i].Trim();
            if (endKeywords.Any(ek => trimmedLine.Equals(ek, StringComparison.OrdinalIgnoreCase)))
            {
                return i;
            }
        }

        return startLine;
    }

    #endregion
}
