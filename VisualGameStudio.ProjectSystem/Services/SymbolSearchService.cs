using System.Text.RegularExpressions;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// Provides symbol search capabilities for BasicLang source files.
/// </summary>
public class SymbolSearchService : ISymbolSearchService
{
    // Regex patterns for various symbol types
    private static readonly Regex ClassPattern = new(@"^\s*(Public\s+|Private\s+)?(Class|Module|Structure)\s+(\w+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
    private static readonly Regex EnumPattern = new(@"^\s*(Public\s+|Private\s+)?Enum\s+(\w+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
    private static readonly Regex MethodPattern = new(@"^\s*(Public\s+|Private\s+|Protected\s+|Friend\s+)?(Sub|Function)\s+(\w+)\s*(\([^)]*\))?(\s+As\s+(\w+))?", RegexOptions.IgnoreCase | RegexOptions.Multiline);
    private static readonly Regex PropertyPattern = new(@"^\s*(Public\s+|Private\s+|Protected\s+|Friend\s+)?Property\s+(\w+)(\s+As\s+(\w+))?", RegexOptions.IgnoreCase | RegexOptions.Multiline);
    private static readonly Regex FieldPattern = new(@"^\s*(Public\s+|Private\s+|Protected\s+|Friend\s+)?(Dim|Const)\s+(\w+)(\s+As\s+(\w+))?", RegexOptions.IgnoreCase | RegexOptions.Multiline);
    private static readonly Regex EventPattern = new(@"^\s*(Public\s+|Private\s+)?Event\s+(\w+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
    private static readonly Regex EndBlockPattern = new(@"^\s*End\s+(Class|Module|Structure|Enum|Sub|Function|Property)", RegexOptions.IgnoreCase);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SymbolSearchResult>> SearchInFileAsync(string sourceCode, string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(sourceCode) || string.IsNullOrEmpty(query))
        {
            return Array.Empty<SymbolSearchResult>();
        }

        var symbols = GetFileSymbols(sourceCode);
        var results = new List<SymbolSearchResult>();

        SearchSymbolsRecursive(symbols, query, "", results);

        return await Task.FromResult(results.OrderByDescending(r => r.Score).ToList());
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SymbolSearchResult>> SearchInProjectAsync(IEnumerable<string> filePaths, string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(query))
        {
            return Array.Empty<SymbolSearchResult>();
        }

        var allResults = new List<SymbolSearchResult>();

        foreach (var filePath in filePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (File.Exists(filePath))
                {
                    var sourceCode = await File.ReadAllTextAsync(filePath, cancellationToken);
                    var fileResults = await SearchInFileAsync(sourceCode, query, cancellationToken);

                    foreach (var result in fileResults)
                    {
                        result.FilePath = filePath;
                        allResults.Add(result);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Skip files that can't be read
            }
        }

        return allResults.OrderByDescending(r => r.Score).ToList();
    }

    /// <inheritdoc/>
    public IReadOnlyList<SymbolInfo> GetFileSymbols(string sourceCode)
    {
        if (string.IsNullOrEmpty(sourceCode))
        {
            return Array.Empty<SymbolInfo>();
        }

        var symbols = new List<SymbolInfo>();
        var lines = sourceCode.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var containerStack = new Stack<SymbolInfo>();

        for (int i = 0; i < lines.Length; i++)
        {
            var lineNumber = i + 1;
            var line = lines[i];

            // Check for end blocks first
            var endMatch = EndBlockPattern.Match(line);
            if (endMatch.Success && containerStack.Count > 0)
            {
                var container = containerStack.Pop();
                container.EndLine = lineNumber;
                container.EndColumn = line.Length;
                continue;
            }

            // Check for class/module/structure
            var classMatch = ClassPattern.Match(line);
            if (classMatch.Success)
            {
                var symbol = CreateSymbol(classMatch, lineNumber, line, GetKindFromKeyword(classMatch.Groups[2].Value));
                AddSymbolToHierarchy(symbol, symbols, containerStack);
                containerStack.Push(symbol);
                continue;
            }

            // Check for enum
            var enumMatch = EnumPattern.Match(line);
            if (enumMatch.Success)
            {
                var symbol = CreateSymbolFromEnum(enumMatch, lineNumber, line);
                AddSymbolToHierarchy(symbol, symbols, containerStack);
                containerStack.Push(symbol);
                continue;
            }

            // Check for method (Sub/Function)
            var methodMatch = MethodPattern.Match(line);
            if (methodMatch.Success)
            {
                var symbol = CreateSymbolFromMethod(methodMatch, lineNumber, line);
                AddSymbolToHierarchy(symbol, symbols, containerStack);
                containerStack.Push(symbol);
                continue;
            }

            // Check for property
            var propMatch = PropertyPattern.Match(line);
            if (propMatch.Success)
            {
                var symbol = CreateSymbolFromProperty(propMatch, lineNumber, line);
                AddSymbolToHierarchy(symbol, symbols, containerStack);
                containerStack.Push(symbol);
                continue;
            }

            // Check for field/constant
            var fieldMatch = FieldPattern.Match(line);
            if (fieldMatch.Success)
            {
                var symbol = CreateSymbolFromField(fieldMatch, lineNumber, line);
                AddSymbolToHierarchy(symbol, symbols, containerStack);
                continue;
            }

            // Check for event
            var eventMatch = EventPattern.Match(line);
            if (eventMatch.Success)
            {
                var symbol = CreateSymbolFromEvent(eventMatch, lineNumber, line);
                AddSymbolToHierarchy(symbol, symbols, containerStack);
                continue;
            }
        }

        // Close any unclosed containers
        while (containerStack.Count > 0)
        {
            var container = containerStack.Pop();
            container.EndLine = lines.Length;
        }

        return symbols;
    }

    /// <inheritdoc/>
    public SymbolInfo? GetSymbolAtLocation(string sourceCode, int line, int column)
    {
        var symbols = GetFileSymbols(sourceCode);
        return FindSymbolAtLocation(symbols, line, column);
    }

    /// <inheritdoc/>
    public IReadOnlyList<SymbolInfo> GetBreadcrumb(string sourceCode, int line)
    {
        var symbols = GetFileSymbols(sourceCode);
        var breadcrumb = new List<SymbolInfo>();
        BuildBreadcrumb(symbols, line, breadcrumb);
        return breadcrumb;
    }

    private void SearchSymbolsRecursive(IReadOnlyList<SymbolInfo> symbols, string query, string filePath, List<SymbolSearchResult> results)
    {
        foreach (var symbol in symbols)
        {
            var score = CalculateMatchScore(symbol.Name, query);
            if (score > 0)
            {
                results.Add(new SymbolSearchResult
                {
                    Symbol = symbol,
                    FilePath = filePath,
                    Score = score,
                    MatchedText = symbol.Name
                });
            }

            // Search in children
            if (symbol.Children.Count > 0)
            {
                SearchSymbolsRecursive(symbol.Children, query, filePath, results);
            }
        }
    }

    private static int CalculateMatchScore(string symbolName, string query)
    {
        if (string.IsNullOrEmpty(symbolName))
        {
            return 0;
        }

        // Exact match (case insensitive)
        if (symbolName.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            return 100;
        }

        // Starts with (case insensitive)
        if (symbolName.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 80;
        }

        // Contains (case insensitive)
        if (symbolName.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return 60;
        }

        // Fuzzy match (camelCase/PascalCase matching)
        if (MatchesCamelCase(symbolName, query))
        {
            return 40;
        }

        return 0;
    }

    private static bool MatchesCamelCase(string symbolName, string query)
    {
        // Simple camelCase matching: check if uppercase letters match query chars
        var upperChars = new string(symbolName.Where(char.IsUpper).ToArray());
        return upperChars.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static SymbolKind GetKindFromKeyword(string keyword)
    {
        return keyword.ToUpperInvariant() switch
        {
            "CLASS" => SymbolKind.Class,
            "MODULE" => SymbolKind.Module,
            "STRUCTURE" => SymbolKind.Struct,
            _ => SymbolKind.Class // Default to Class for unknown types
        };
    }

    private static AccessModifier GetAccessModifier(string? modifierText)
    {
        if (string.IsNullOrEmpty(modifierText))
        {
            return AccessModifier.None;
        }

        return modifierText.Trim().ToUpperInvariant() switch
        {
            "PUBLIC" => AccessModifier.Public,
            "PRIVATE" => AccessModifier.Private,
            "PROTECTED" => AccessModifier.Protected,
            "FRIEND" => AccessModifier.Friend,
            _ => AccessModifier.None
        };
    }

    private static SymbolInfo CreateSymbol(Match match, int lineNumber, string line, SymbolKind kind)
    {
        var name = match.Groups[3].Value;
        var modifier = GetAccessModifier(match.Groups[1].Value);

        return new SymbolInfo
        {
            Name = name,
            FullName = name,
            Kind = kind,
            StartLine = lineNumber,
            StartColumn = line.IndexOf(name, StringComparison.OrdinalIgnoreCase) + 1,
            EndLine = lineNumber,
            AccessModifier = modifier
        };
    }

    private static SymbolInfo CreateSymbolFromEnum(Match match, int lineNumber, string line)
    {
        var name = match.Groups[2].Value;
        var modifier = GetAccessModifier(match.Groups[1].Value);

        return new SymbolInfo
        {
            Name = name,
            FullName = name,
            Kind = SymbolKind.Enum,
            StartLine = lineNumber,
            StartColumn = line.IndexOf(name, StringComparison.OrdinalIgnoreCase) + 1,
            EndLine = lineNumber,
            AccessModifier = modifier
        };
    }

    private static SymbolInfo CreateSymbolFromMethod(Match match, int lineNumber, string line)
    {
        var isSub = match.Groups[2].Value.Equals("Sub", StringComparison.OrdinalIgnoreCase);
        var name = match.Groups[3].Value;
        var parameters = match.Groups[4].Value;
        var returnType = match.Groups[6].Value;
        var modifier = GetAccessModifier(match.Groups[1].Value);

        return new SymbolInfo
        {
            Name = name,
            FullName = name,
            Kind = isSub ? SymbolKind.Method : SymbolKind.Function,
            StartLine = lineNumber,
            StartColumn = line.IndexOf(name, StringComparison.OrdinalIgnoreCase) + 1,
            EndLine = lineNumber,
            AccessModifier = modifier,
            Signature = $"{name}{parameters}",
            ReturnType = string.IsNullOrEmpty(returnType) ? null : returnType
        };
    }

    private static SymbolInfo CreateSymbolFromProperty(Match match, int lineNumber, string line)
    {
        var name = match.Groups[2].Value;
        var returnType = match.Groups[4].Value;
        var modifier = GetAccessModifier(match.Groups[1].Value);

        return new SymbolInfo
        {
            Name = name,
            FullName = name,
            Kind = SymbolKind.Property,
            StartLine = lineNumber,
            StartColumn = line.IndexOf(name, StringComparison.OrdinalIgnoreCase) + 1,
            EndLine = lineNumber,
            AccessModifier = modifier,
            ReturnType = string.IsNullOrEmpty(returnType) ? null : returnType
        };
    }

    private static SymbolInfo CreateSymbolFromField(Match match, int lineNumber, string line)
    {
        var isConst = match.Groups[2].Value.Equals("Const", StringComparison.OrdinalIgnoreCase);
        var name = match.Groups[3].Value;
        var fieldType = match.Groups[5].Value;
        var modifier = GetAccessModifier(match.Groups[1].Value);

        return new SymbolInfo
        {
            Name = name,
            FullName = name,
            Kind = isConst ? SymbolKind.Constant : SymbolKind.Field,
            StartLine = lineNumber,
            StartColumn = line.IndexOf(name, StringComparison.OrdinalIgnoreCase) + 1,
            EndLine = lineNumber,
            AccessModifier = modifier,
            ReturnType = string.IsNullOrEmpty(fieldType) ? null : fieldType
        };
    }

    private static SymbolInfo CreateSymbolFromEvent(Match match, int lineNumber, string line)
    {
        var name = match.Groups[2].Value;
        var modifier = GetAccessModifier(match.Groups[1].Value);

        return new SymbolInfo
        {
            Name = name,
            FullName = name,
            Kind = SymbolKind.Event,
            StartLine = lineNumber,
            StartColumn = line.IndexOf(name, StringComparison.OrdinalIgnoreCase) + 1,
            EndLine = lineNumber,
            AccessModifier = modifier
        };
    }

    private static void AddSymbolToHierarchy(SymbolInfo symbol, List<SymbolInfo> rootSymbols, Stack<SymbolInfo> containerStack)
    {
        if (containerStack.Count > 0)
        {
            var container = containerStack.Peek();
            symbol.ContainerName = container.FullName;
            symbol.FullName = $"{container.FullName}.{symbol.Name}";
            container.Children.Add(symbol);
        }
        else
        {
            rootSymbols.Add(symbol);
        }
    }

    private static SymbolInfo? FindSymbolAtLocation(IReadOnlyList<SymbolInfo> symbols, int line, int column)
    {
        foreach (var symbol in symbols)
        {
            if (line >= symbol.StartLine && line <= symbol.EndLine)
            {
                // Check children first (more specific match)
                var childMatch = FindSymbolAtLocation(symbol.Children, line, column);
                if (childMatch != null)
                {
                    return childMatch;
                }

                // If on the start line, check column
                if (line == symbol.StartLine && column >= symbol.StartColumn)
                {
                    return symbol;
                }

                // If within the symbol's range but not on start line
                if (line > symbol.StartLine && line < symbol.EndLine)
                {
                    return symbol;
                }
            }
        }

        return null;
    }

    private static void BuildBreadcrumb(IReadOnlyList<SymbolInfo> symbols, int line, List<SymbolInfo> breadcrumb)
    {
        foreach (var symbol in symbols)
        {
            if (line >= symbol.StartLine && line <= symbol.EndLine)
            {
                breadcrumb.Add(symbol);
                BuildBreadcrumb(symbol.Children, line, breadcrumb);
                return;
            }
        }
    }
}
