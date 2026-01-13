using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using VisualGameStudio.Core.Snippets;

namespace VisualGameStudio.ProjectSystem.Snippets;

/// <summary>
/// Service for managing code snippets
/// </summary>
public partial class SnippetService : ISnippetService
{
    private readonly ConcurrentDictionary<string, List<Snippet>> _snippetsByLanguage = new();

    // Regex for parsing snippet placeholders
    // Matches: $1, ${1}, ${1:default}, ${1|choice1,choice2|}, ${1/regex/replace/flags}
    [GeneratedRegex(@"\$(\d+)|\$\{(\d+)(?::([^}|/]+))?\}|\$\{(\d+)\|([^}]+)\|\}|\$\{(\d+)/([^/]+)/([^/]*)/([^}]*)?\}|\$\{([a-zA-Z_][a-zA-Z0-9_]*)\}|\$([a-zA-Z_][a-zA-Z0-9_]*)")]
    private static partial Regex PlaceholderRegex();

    public SnippetService()
    {
        // Register built-in snippets
        RegisterBuiltInSnippets();
    }

    private void RegisterBuiltInSnippets()
    {
        // BasicLang snippets
        RegisterSnippets(new[]
        {
            new Snippet
            {
                Name = "Function",
                Prefix = "func",
                Description = "Create a new function",
                LanguageId = "basiclang",
                Body = "Function ${1:name}(${2:params}) As ${3:Integer}\n    ${0}\nEnd Function"
            },
            new Snippet
            {
                Name = "Sub",
                Prefix = "sub",
                Description = "Create a new subroutine",
                LanguageId = "basiclang",
                Body = "Sub ${1:name}(${2:params})\n    ${0}\nEnd Sub"
            },
            new Snippet
            {
                Name = "If Statement",
                Prefix = "if",
                Description = "If statement",
                LanguageId = "basiclang",
                Body = "If ${1:condition} Then\n    ${0}\nEnd If"
            },
            new Snippet
            {
                Name = "If Else Statement",
                Prefix = "ife",
                Description = "If-Else statement",
                LanguageId = "basiclang",
                Body = "If ${1:condition} Then\n    ${2}\nElse\n    ${0}\nEnd If"
            },
            new Snippet
            {
                Name = "For Loop",
                Prefix = "for",
                Description = "For loop",
                LanguageId = "basiclang",
                Body = "For ${1:i} As Integer = ${2:0} To ${3:10}\n    ${0}\nNext"
            },
            new Snippet
            {
                Name = "For Each Loop",
                Prefix = "foreach",
                Description = "For Each loop",
                LanguageId = "basiclang",
                Body = "For Each ${1:item} As ${2:Type} In ${3:collection}\n    ${0}\nNext"
            },
            new Snippet
            {
                Name = "While Loop",
                Prefix = "while",
                Description = "While loop",
                LanguageId = "basiclang",
                Body = "While ${1:condition}\n    ${0}\nEnd While"
            },
            new Snippet
            {
                Name = "Class",
                Prefix = "class",
                Description = "Create a new class",
                LanguageId = "basiclang",
                Body = "Class ${1:ClassName}\n    ${0}\nEnd Class"
            },
            new Snippet
            {
                Name = "Property",
                Prefix = "prop",
                Description = "Create a property",
                LanguageId = "basiclang",
                Body = "Property ${1:Name} As ${2:Type}\n    Get\n        Return ${3:_field}\n    End Get\n    Set(value As ${2:Type})\n        ${3:_field} = value\n    End Set\nEnd Property"
            },
            new Snippet
            {
                Name = "Try Catch",
                Prefix = "try",
                Description = "Try-Catch block",
                LanguageId = "basiclang",
                Body = "Try\n    ${0}\nCatch ${1:ex} As Exception\n    ' Handle error\nEnd Try"
            },
            new Snippet
            {
                Name = "Console.WriteLine",
                Prefix = "cw",
                Description = "Write to console",
                LanguageId = "basiclang",
                Body = "Console.WriteLine(${0})"
            }
        });
    }

    public async Task<IReadOnlyList<Snippet>> LoadSnippetsFromFileAsync(string snippetPath, string languageId)
    {
        try
        {
            var content = await File.ReadAllTextAsync(snippetPath);
            return LoadSnippetsFromJson(content, languageId);
        }
        catch (Exception)
        {
            return Array.Empty<Snippet>();
        }
    }

    public IReadOnlyList<Snippet> LoadSnippetsFromJson(string jsonContent, string languageId)
    {
        var snippets = new List<Snippet>();

        try
        {
            using var doc = JsonDocument.Parse(jsonContent, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var snippet = ParseSnippet(prop.Name, prop.Value, languageId);
                if (snippet != null)
                {
                    snippets.Add(snippet);
                }
            }
        }
        catch (Exception)
        {
            // Return empty list on parse error
        }

        return snippets;
    }

    private Snippet? ParseSnippet(string name, JsonElement element, string languageId)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        var snippet = new Snippet
        {
            Name = name,
            LanguageId = languageId
        };

        // Parse prefix (can be string or array)
        if (element.TryGetProperty("prefix", out var prefix))
        {
            if (prefix.ValueKind == JsonValueKind.String)
            {
                snippet.Prefix = prefix.GetString() ?? name;
            }
            else if (prefix.ValueKind == JsonValueKind.Array)
            {
                var prefixes = new List<string>();
                foreach (var p in prefix.EnumerateArray())
                {
                    var val = p.GetString();
                    if (!string.IsNullOrEmpty(val))
                    {
                        prefixes.Add(val);
                    }
                }

                if (prefixes.Count > 0)
                {
                    snippet.Prefix = prefixes[0];
                    snippet.AlternatePrefixes = prefixes.Skip(1).ToList();
                }
            }
        }
        else
        {
            snippet.Prefix = name;
        }

        // Parse body (can be string or array of lines)
        if (element.TryGetProperty("body", out var body))
        {
            if (body.ValueKind == JsonValueKind.String)
            {
                snippet.Body = body.GetString() ?? "";
            }
            else if (body.ValueKind == JsonValueKind.Array)
            {
                var lines = new List<string>();
                foreach (var line in body.EnumerateArray())
                {
                    lines.Add(line.GetString() ?? "");
                }
                snippet.Body = string.Join("\n", lines);
            }
        }

        // Parse description
        if (element.TryGetProperty("description", out var description))
        {
            snippet.Description = description.GetString();
        }

        // Parse scope
        if (element.TryGetProperty("scope", out var scope))
        {
            snippet.Scope = scope.GetString();
        }

        // Parse placeholders
        snippet.Placeholders = ParsePlaceholders(snippet.Body);

        // Check if file template
        if (element.TryGetProperty("isFileTemplate", out var isTemplate))
        {
            snippet.IsFileTemplate = isTemplate.GetBoolean();
        }

        return snippet;
    }

    private List<SnippetPlaceholder> ParsePlaceholders(string body)
    {
        var placeholders = new List<SnippetPlaceholder>();
        var matches = PlaceholderRegex().Matches(body);

        foreach (Match match in matches)
        {
            var placeholder = new SnippetPlaceholder
            {
                StartPosition = match.Index,
                Length = match.Length
            };

            // Simple placeholder $1 or ${1}
            if (!string.IsNullOrEmpty(match.Groups[1].Value))
            {
                placeholder.Index = int.Parse(match.Groups[1].Value);
            }
            else if (!string.IsNullOrEmpty(match.Groups[2].Value))
            {
                placeholder.Index = int.Parse(match.Groups[2].Value);
                placeholder.DefaultValue = match.Groups[3].Value;
            }
            // Choice placeholder ${1|choice1,choice2|}
            else if (!string.IsNullOrEmpty(match.Groups[4].Value))
            {
                placeholder.Index = int.Parse(match.Groups[4].Value);
                placeholder.Choices = match.Groups[5].Value.Split(',').ToList();
                if (placeholder.Choices.Count > 0)
                {
                    placeholder.DefaultValue = placeholder.Choices[0];
                }
            }
            // Transform placeholder ${1/regex/replace/flags}
            else if (!string.IsNullOrEmpty(match.Groups[6].Value))
            {
                placeholder.Index = int.Parse(match.Groups[6].Value);
                placeholder.Transform = $"/{match.Groups[7].Value}/{match.Groups[8].Value}/{match.Groups[9].Value}";
            }
            // Named variable ${TM_FILENAME}
            else if (!string.IsNullOrEmpty(match.Groups[10].Value))
            {
                placeholder.Name = match.Groups[10].Value;
                placeholder.Index = -1; // Special index for variables
            }
            // Named variable $TM_FILENAME
            else if (!string.IsNullOrEmpty(match.Groups[11].Value))
            {
                placeholder.Name = match.Groups[11].Value;
                placeholder.Index = -1;
            }

            placeholders.Add(placeholder);
        }

        return placeholders;
    }

    public IReadOnlyList<Snippet> GetSnippetsForLanguage(string languageId)
    {
        var key = languageId.ToLowerInvariant();
        if (_snippetsByLanguage.TryGetValue(key, out var snippets))
        {
            return snippets;
        }
        return Array.Empty<Snippet>();
    }

    public IReadOnlyList<Snippet> GetSnippetsByPrefix(string languageId, string prefix)
    {
        var snippets = GetSnippetsForLanguage(languageId);
        var normalizedPrefix = prefix.ToLowerInvariant();

        return snippets
            .Where(s => s.Prefix.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase) ||
                       s.AlternatePrefixes.Any(p => p.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(s => s.Prefix.Length)
            .ThenBy(s => s.Prefix)
            .ToList();
    }

    public void RegisterSnippet(Snippet snippet)
    {
        var key = snippet.LanguageId.ToLowerInvariant();
        var snippets = _snippetsByLanguage.GetOrAdd(key, _ => new List<Snippet>());

        lock (snippets)
        {
            // Remove existing snippet with same prefix
            snippets.RemoveAll(s => s.Prefix == snippet.Prefix);
            snippets.Add(snippet);
        }
    }

    public void RegisterSnippets(IEnumerable<Snippet> snippets)
    {
        foreach (var snippet in snippets)
        {
            RegisterSnippet(snippet);
        }
    }

    public IReadOnlyList<string> GetLanguagesWithSnippets()
    {
        return _snippetsByLanguage.Keys.ToList();
    }

    public string ExpandSnippet(Snippet snippet, Dictionary<string, string>? placeholderValues = null)
    {
        var result = snippet.Body;
        placeholderValues ??= new Dictionary<string, string>();

        // Replace built-in variables
        result = ReplaceBuiltInVariables(result);

        // Sort placeholders by index descending so we can replace from end to start
        // without messing up indices
        var sortedPlaceholders = snippet.Placeholders
            .OrderByDescending(p => p.StartPosition)
            .ToList();

        // For a proper implementation, we'd need to track cursor positions
        // For now, just replace with default values or provided values
        var sb = new StringBuilder(result);

        foreach (var placeholder in sortedPlaceholders)
        {
            string replacement;

            if (placeholder.Index == 0)
            {
                // $0 is the final cursor position, leave empty
                replacement = "";
            }
            else if (placeholder.Index > 0 && placeholderValues.TryGetValue(placeholder.Index.ToString(), out var value))
            {
                replacement = value;
            }
            else if (!string.IsNullOrEmpty(placeholder.Name) && placeholderValues.TryGetValue(placeholder.Name, out var namedValue))
            {
                replacement = namedValue;
            }
            else
            {
                replacement = placeholder.DefaultValue ?? "";
            }

            if (placeholder.StartPosition >= 0 && placeholder.Length > 0 &&
                placeholder.StartPosition + placeholder.Length <= sb.Length)
            {
                sb.Remove(placeholder.StartPosition, placeholder.Length);
                sb.Insert(placeholder.StartPosition, replacement);
            }
        }

        return sb.ToString();
    }

    private string ReplaceBuiltInVariables(string body)
    {
        // Common VS Code snippet variables
        var now = DateTime.Now;

        body = body.Replace("$TM_SELECTED_TEXT", "");
        body = body.Replace("${TM_SELECTED_TEXT}", "");
        body = body.Replace("$TM_CURRENT_LINE", "");
        body = body.Replace("${TM_CURRENT_LINE}", "");
        body = body.Replace("$TM_CURRENT_WORD", "");
        body = body.Replace("${TM_CURRENT_WORD}", "");
        body = body.Replace("$TM_LINE_INDEX", "0");
        body = body.Replace("${TM_LINE_INDEX}", "0");
        body = body.Replace("$TM_LINE_NUMBER", "1");
        body = body.Replace("${TM_LINE_NUMBER}", "1");

        body = body.Replace("$CURRENT_YEAR", now.Year.ToString());
        body = body.Replace("${CURRENT_YEAR}", now.Year.ToString());
        body = body.Replace("$CURRENT_YEAR_SHORT", now.Year.ToString().Substring(2));
        body = body.Replace("${CURRENT_YEAR_SHORT}", now.Year.ToString().Substring(2));
        body = body.Replace("$CURRENT_MONTH", now.Month.ToString("D2"));
        body = body.Replace("${CURRENT_MONTH}", now.Month.ToString("D2"));
        body = body.Replace("$CURRENT_MONTH_NAME", now.ToString("MMMM"));
        body = body.Replace("${CURRENT_MONTH_NAME}", now.ToString("MMMM"));
        body = body.Replace("$CURRENT_MONTH_NAME_SHORT", now.ToString("MMM"));
        body = body.Replace("${CURRENT_MONTH_NAME_SHORT}", now.ToString("MMM"));
        body = body.Replace("$CURRENT_DATE", now.Day.ToString("D2"));
        body = body.Replace("${CURRENT_DATE}", now.Day.ToString("D2"));
        body = body.Replace("$CURRENT_DAY_NAME", now.ToString("dddd"));
        body = body.Replace("${CURRENT_DAY_NAME}", now.ToString("dddd"));
        body = body.Replace("$CURRENT_DAY_NAME_SHORT", now.ToString("ddd"));
        body = body.Replace("${CURRENT_DAY_NAME_SHORT}", now.ToString("ddd"));
        body = body.Replace("$CURRENT_HOUR", now.Hour.ToString("D2"));
        body = body.Replace("${CURRENT_HOUR}", now.Hour.ToString("D2"));
        body = body.Replace("$CURRENT_MINUTE", now.Minute.ToString("D2"));
        body = body.Replace("${CURRENT_MINUTE}", now.Minute.ToString("D2"));
        body = body.Replace("$CURRENT_SECOND", now.Second.ToString("D2"));
        body = body.Replace("${CURRENT_SECOND}", now.Second.ToString("D2"));

        body = body.Replace("$CLIPBOARD", "");
        body = body.Replace("${CLIPBOARD}", "");

        body = body.Replace("$UUID", Guid.NewGuid().ToString());
        body = body.Replace("${UUID}", Guid.NewGuid().ToString());

        body = body.Replace("$RANDOM", Random.Shared.Next(100000, 999999).ToString());
        body = body.Replace("${RANDOM}", Random.Shared.Next(100000, 999999).ToString());
        body = body.Replace("$RANDOM_HEX", Random.Shared.Next(0x100000, 0xFFFFFF).ToString("x"));
        body = body.Replace("${RANDOM_HEX}", Random.Shared.Next(0x100000, 0xFFFFFF).ToString("x"));

        return body;
    }
}
