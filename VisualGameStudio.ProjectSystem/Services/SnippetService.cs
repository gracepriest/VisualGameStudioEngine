using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;

namespace VisualGameStudio.ProjectSystem.Services;

public class SnippetService : ISnippetService
{
    private readonly List<Snippet> _builtInSnippets = new();
    private readonly Dictionary<string, List<Snippet>> _userSnippetsByLanguage = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    // Legacy support
    private readonly Dictionary<string, CodeSnippet> _legacySnippets = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler? SnippetsChanged;

    public SnippetService()
    {
        RegisterBuiltInSnippets();
        LoadSnippets();
    }

    #region ISnippetService Implementation

    public IReadOnlyList<Snippet> GetSnippets(string language = "basiclang")
    {
        lock (_lock)
        {
            var result = new List<Snippet>();
            result.AddRange(_builtInSnippets.Where(s =>
                string.IsNullOrEmpty(s.Scope) ||
                s.Scope.Equals(language, StringComparison.OrdinalIgnoreCase)));

            if (_userSnippetsByLanguage.TryGetValue(language, out var userSnippets))
            {
                result.AddRange(userSnippets);
            }

            return result;
        }
    }

    public IReadOnlyList<Snippet> GetAllSnippets()
    {
        lock (_lock)
        {
            var result = new List<Snippet>();
            result.AddRange(_builtInSnippets);
            foreach (var list in _userSnippetsByLanguage.Values)
            {
                result.AddRange(list);
            }
            return result;
        }
    }

    public IReadOnlyList<Snippet> SearchSnippets(string query, string? language = null)
    {
        var all = language != null ? GetSnippets(language) : GetAllSnippets();
        if (string.IsNullOrWhiteSpace(query))
            return all;

        return all.Where(s =>
            s.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            s.Prefix.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            s.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            s.Category.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public Snippet? GetSnippetByPrefix(string prefix, string language = "basiclang")
    {
        if (string.IsNullOrEmpty(prefix)) return null;
        return GetSnippets(language).FirstOrDefault(s =>
            s.Prefix.Equals(prefix, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<Snippet> FindByPrefixStart(string prefixStart, string language = "basiclang")
    {
        if (string.IsNullOrEmpty(prefixStart)) return Array.Empty<Snippet>();
        return GetSnippets(language).Where(s =>
            s.Prefix.StartsWith(prefixStart, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public void AddUserSnippet(string language, Snippet snippet)
    {
        lock (_lock)
        {
            snippet.IsBuiltIn = false;
            snippet.Scope = language;

            if (!_userSnippetsByLanguage.TryGetValue(language, out var list))
            {
                list = new List<Snippet>();
                _userSnippetsByLanguage[language] = list;
            }

            // Remove existing with same name
            list.RemoveAll(s => s.Name.Equals(snippet.Name, StringComparison.OrdinalIgnoreCase));
            list.Add(snippet);
        }

        SaveUserSnippets(language);
        SnippetsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateUserSnippet(string language, Snippet snippet)
    {
        AddUserSnippet(language, snippet);
    }

    public void RemoveUserSnippet(string language, string name)
    {
        lock (_lock)
        {
            if (_userSnippetsByLanguage.TryGetValue(language, out var list))
            {
                list.RemoveAll(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            }
        }

        SaveUserSnippets(language);
        SnippetsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void LoadSnippets()
    {
        var dir = GetUserSnippetsDirectory();
        if (!Directory.Exists(dir)) return;

        lock (_lock)
        {
            _userSnippetsByLanguage.Clear();

            foreach (var file in Directory.GetFiles(dir, "*.json"))
            {
                var language = Path.GetFileNameWithoutExtension(file);
                try
                {
                    var loaded = LoadSnippetsFromFile(file, language);
                    if (loaded.Count > 0)
                    {
                        _userSnippetsByLanguage[language] = new List<Snippet>(loaded);
                    }
                }
                catch
                {
                    // Skip files that can't be parsed
                }
            }
        }
    }

    public IReadOnlyList<Snippet> LoadSnippetsFromFile(string filePath, string language)
    {
        if (!File.Exists(filePath)) return Array.Empty<Snippet>();

        try
        {
            var json = File.ReadAllText(filePath);
            return ParseVSCodeSnippetJson(json, language, filePath);
        }
        catch
        {
            return Array.Empty<Snippet>();
        }
    }

    public void SaveUserSnippets(string language)
    {
        var dir = GetUserSnippetsDirectory();
        Directory.CreateDirectory(dir);

        List<Snippet> snippets;
        lock (_lock)
        {
            if (!_userSnippetsByLanguage.TryGetValue(language, out var list) || list.Count == 0)
            {
                // Delete the file if no snippets remain
                var path = Path.Combine(dir, $"{language}.json");
                if (File.Exists(path))
                    File.Delete(path);
                return;
            }
            snippets = new List<Snippet>(list);
        }

        var filePath = Path.Combine(dir, $"{language}.json");
        ExportSnippets(filePath, snippets);
    }

    public string ExpandSnippet(Snippet snippet, Dictionary<string, string>? variables = null)
    {
        var body = string.Join("\n", snippet.Body);
        body = ReplaceBuiltInVariables(body, variables);
        return body;
    }

    public IReadOnlyList<Snippet> ImportSnippets(string filePath, string language)
    {
        var snippets = LoadSnippetsFromFile(filePath, language);
        foreach (var snippet in snippets)
        {
            AddUserSnippet(language, snippet);
        }
        return snippets;
    }

    public void ExportSnippets(string filePath, IEnumerable<Snippet> snippets)
    {
        var dict = new Dictionary<string, object>();
        foreach (var snippet in snippets)
        {
            dict[snippet.Name] = new
            {
                prefix = snippet.Prefix,
                body = snippet.Body,
                description = snippet.Description,
                scope = snippet.Scope,
                category = snippet.Category
            };
        }

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        var json = JsonSerializer.Serialize(dict, options);
        File.WriteAllText(filePath, json);
    }

    public string GetUserSnippetsDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".vgs", "snippets");
    }

    #endregion

    #region Legacy ISnippetService Methods (backward compatibility)

    public IReadOnlyList<CodeSnippet> GetLegacySnippets()
    {
        return _legacySnippets.Values.ToList();
    }

    public IReadOnlyList<CodeSnippet> SearchLegacySnippets(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return GetLegacySnippets();

        return _legacySnippets.Values
            .Where(s => s.Shortcut.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                       s.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                       s.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public CodeSnippet? GetSnippet(string shortcut)
    {
        return _legacySnippets.GetValueOrDefault(shortcut);
    }

    public string ExpandSnippet(CodeSnippet snippet, Dictionary<string, string>? variables = null)
    {
        var result = snippet.Body;

        // Replace variables with their values or defaults
        result = Regex.Replace(result, @"\$\{(\d+):([^}]*)\}", match =>
        {
            var index = match.Groups[1].Value;
            var defaultValue = match.Groups[2].Value;
            if (variables != null && variables.TryGetValue(index, out var value))
                return value;
            return defaultValue;
        });

        // Handle simple placeholders ${N}
        result = Regex.Replace(result, @"\$\{(\d+)\}", match =>
        {
            var index = match.Groups[1].Value;
            if (variables != null && variables.TryGetValue(index, out var value))
                return value;
            return "";
        });

        return result;
    }

    public void RegisterSnippet(CodeSnippet snippet)
    {
        _legacySnippets[snippet.Shortcut] = snippet;
    }

    public void RemoveSnippet(string shortcut)
    {
        _legacySnippets.Remove(shortcut);
    }

    #endregion

    #region Private Helpers

    private IReadOnlyList<Snippet> ParseVSCodeSnippetJson(string json, string language, string sourcePath)
    {
        var snippets = new List<Snippet>();

        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            var element = prop.Value;
            if (element.ValueKind != JsonValueKind.Object) continue;

            var snippet = new Snippet
            {
                Name = prop.Name,
                Scope = language,
                IsBuiltIn = false,
                Source = sourcePath
            };

            // prefix
            if (element.TryGetProperty("prefix", out var prefix))
            {
                snippet.Prefix = prefix.ValueKind == JsonValueKind.String
                    ? prefix.GetString() ?? prop.Name
                    : prop.Name;
            }
            else
            {
                snippet.Prefix = prop.Name;
            }

            // body (string or array)
            if (element.TryGetProperty("body", out var body))
            {
                if (body.ValueKind == JsonValueKind.String)
                {
                    snippet.Body = (body.GetString() ?? "").Split('\n');
                }
                else if (body.ValueKind == JsonValueKind.Array)
                {
                    var lines = new List<string>();
                    foreach (var line in body.EnumerateArray())
                        lines.Add(line.GetString() ?? "");
                    snippet.Body = lines.ToArray();
                }
            }

            // description
            if (element.TryGetProperty("description", out var desc))
                snippet.Description = desc.GetString() ?? "";

            // scope override
            if (element.TryGetProperty("scope", out var scope))
                snippet.Scope = scope.GetString() ?? language;

            // category
            if (element.TryGetProperty("category", out var cat))
                snippet.Category = cat.GetString() ?? "General";

            snippets.Add(snippet);
        }

        return snippets;
    }

    private string ReplaceBuiltInVariables(string body, Dictionary<string, string>? extraVars = null)
    {
        var now = DateTime.Now;

        // File variables
        body = ReplaceVar(body, "TM_FILENAME", extraVars?.GetValueOrDefault("TM_FILENAME") ?? "Untitled.bas");
        body = ReplaceVar(body, "TM_FILENAME_BASE", extraVars?.GetValueOrDefault("TM_FILENAME_BASE") ?? "Untitled");
        body = ReplaceVar(body, "TM_FILEPATH", extraVars?.GetValueOrDefault("TM_FILEPATH") ?? "");
        body = ReplaceVar(body, "TM_DIRECTORY", extraVars?.GetValueOrDefault("TM_DIRECTORY") ?? "");
        body = ReplaceVar(body, "TM_SELECTED_TEXT", extraVars?.GetValueOrDefault("TM_SELECTED_TEXT") ?? "");
        body = ReplaceVar(body, "TM_CURRENT_LINE", extraVars?.GetValueOrDefault("TM_CURRENT_LINE") ?? "");
        body = ReplaceVar(body, "TM_CURRENT_WORD", extraVars?.GetValueOrDefault("TM_CURRENT_WORD") ?? "");
        body = ReplaceVar(body, "TM_LINE_INDEX", extraVars?.GetValueOrDefault("TM_LINE_INDEX") ?? "0");
        body = ReplaceVar(body, "TM_LINE_NUMBER", extraVars?.GetValueOrDefault("TM_LINE_NUMBER") ?? "1");

        // Date/time variables
        body = ReplaceVar(body, "CURRENT_YEAR", now.Year.ToString());
        body = ReplaceVar(body, "CURRENT_YEAR_SHORT", now.Year.ToString().Substring(2));
        body = ReplaceVar(body, "CURRENT_MONTH", now.Month.ToString("D2"));
        body = ReplaceVar(body, "CURRENT_MONTH_NAME", now.ToString("MMMM"));
        body = ReplaceVar(body, "CURRENT_MONTH_NAME_SHORT", now.ToString("MMM"));
        body = ReplaceVar(body, "CURRENT_DATE", now.Day.ToString("D2"));
        body = ReplaceVar(body, "CURRENT_DAY_NAME", now.ToString("dddd"));
        body = ReplaceVar(body, "CURRENT_DAY_NAME_SHORT", now.ToString("ddd"));
        body = ReplaceVar(body, "CURRENT_HOUR", now.Hour.ToString("D2"));
        body = ReplaceVar(body, "CURRENT_MINUTE", now.Minute.ToString("D2"));
        body = ReplaceVar(body, "CURRENT_SECOND", now.Second.ToString("D2"));
        body = ReplaceVar(body, "CURRENT_SECONDS_UNIX", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
        body = ReplaceVar(body, "CURRENT_TIMEZONE_OFFSET", now.ToString("zzz"));

        // Clipboard
        body = ReplaceVar(body, "CLIPBOARD", extraVars?.GetValueOrDefault("CLIPBOARD") ?? "");

        // Random
        body = ReplaceVar(body, "UUID", Guid.NewGuid().ToString());
        body = ReplaceVar(body, "RANDOM", Random.Shared.Next(100000, 999999).ToString());
        body = ReplaceVar(body, "RANDOM_HEX", Random.Shared.Next(0x100000, 0xFFFFFF).ToString("x"));

        // Block comment
        body = ReplaceVar(body, "BLOCK_COMMENT_START", "'");
        body = ReplaceVar(body, "BLOCK_COMMENT_END", "");
        body = ReplaceVar(body, "LINE_COMMENT", "'");

        return body;
    }

    private static string ReplaceVar(string body, string varName, string value)
    {
        body = body.Replace($"${varName}", value);
        body = body.Replace($"${{{varName}}}", value);
        return body;
    }

    #endregion

    #region Built-in Snippet Registration

    private void RegisterBuiltInSnippets()
    {
        // Control Flow
        AddBuiltIn("Sub", "sub", "Control Flow",
            "Create a new subroutine",
            "Sub ${1:Name}()", "    $0", "End Sub");

        AddBuiltIn("Function", "func", "Control Flow",
            "Create a new function",
            "Function ${1:Name}() As ${2:Integer}", "    $0", "End Function");

        AddBuiltIn("If Statement", "if", "Control Flow",
            "If...Then...End If block",
            "If ${1:condition} Then", "    $0", "End If");

        AddBuiltIn("If Else Statement", "ifelse", "Control Flow",
            "If...Then...Else...End If block",
            "If ${1:condition} Then", "    $2", "Else", "    $0", "End If");

        AddBuiltIn("If ElseIf Statement", "ifeif", "Control Flow",
            "If...ElseIf...Else...End If block",
            "If ${1:condition} Then", "    $2", "ElseIf ${3:condition} Then", "    $4", "Else", "    $0", "End If");

        AddBuiltIn("For Loop", "for", "Control Flow",
            "For...Next loop",
            "For ${1:i} As Integer = ${2:0} To ${3:10}", "    $0", "Next");

        AddBuiltIn("For Step Loop", "fors", "Control Flow",
            "For...Next loop with Step",
            "For ${1:i} As Integer = ${2:0} To ${3:10} Step ${4:1}", "    $0", "Next");

        AddBuiltIn("For Each Loop", "foreach", "Control Flow",
            "For Each...Next loop",
            "For Each ${1:item} In ${2:collection}", "    $0", "Next");

        AddBuiltIn("While Loop", "while", "Control Flow",
            "While...End While loop",
            "While ${1:condition}", "    $0", "End While");

        AddBuiltIn("Do While Loop", "dowhile", "Control Flow",
            "Do While...Loop",
            "Do While ${1:condition}", "    $0", "Loop");

        AddBuiltIn("Do Until Loop", "dountil", "Control Flow",
            "Do Until...Loop",
            "Do Until ${1:condition}", "    $0", "Loop");

        AddBuiltIn("Do Loop While", "doloopwhile", "Control Flow",
            "Do...Loop While",
            "Do", "    $0", "Loop While ${1:condition}");

        AddBuiltIn("Select Case", "select", "Control Flow",
            "Select Case...End Select",
            "Select Case ${1:expression}", "    Case ${2:value}", "        $0", "End Select");

        AddBuiltIn("Try Catch", "try", "Control Flow",
            "Try...Catch...End Try block",
            "Try", "    $0", "Catch ${1:ex} As Exception", "    ", "End Try");

        AddBuiltIn("Try Catch Finally", "tryf", "Control Flow",
            "Try...Catch...Finally...End Try",
            "Try", "    $0", "Catch ${1:ex} As Exception", "    ", "Finally", "    ", "End Try");

        // Declarations
        AddBuiltIn("Class", "class", "Declarations",
            "Create a new class",
            "Class ${1:Name}", "    $0", "End Class");

        AddBuiltIn("Class with Constructor", "classc", "Declarations",
            "Class with constructor",
            "Class ${1:Name}", "    Private _${2:field} As ${3:Type}", "",
            "    Sub New(${2:field} As ${3:Type})", "        _${2:field} = ${2:field}", "    End Sub", "",
            "    $0", "End Class");

        AddBuiltIn("Module", "module", "Declarations",
            "Create a new module",
            "Module ${1:Name}", "    $0", "End Module");

        AddBuiltIn("Namespace", "namespace", "Declarations",
            "Create a namespace",
            "Namespace ${1:Name}", "    $0", "End Namespace");

        AddBuiltIn("Interface", "interface", "Declarations",
            "Create an interface",
            "Interface ${1:IName}", "    $0", "End Interface");

        AddBuiltIn("Enum", "enum", "Declarations",
            "Create an enumeration",
            "Enum ${1:Name}", "    ${2:Value1}", "    ${3:Value2}", "    $0", "End Enum");

        AddBuiltIn("Structure", "struct", "Declarations",
            "Create a structure",
            "Structure ${1:Name}", "    Public ${2:Field1} As ${3:Type}", "    $0", "End Structure");

        AddBuiltIn("Event", "event", "Declarations",
            "Declare an event",
            "Public Event ${1:EventName}(sender As Object, e As ${2:EventArgs})");

        AddBuiltIn("Delegate", "delegate", "Declarations",
            "Declare a delegate",
            "Public Delegate Sub ${1:DelegateName}(${2:params})");

        // Properties
        AddBuiltIn("Property", "prop", "Properties",
            "Create a full property",
            "Property ${1:Name} As ${2:Type}", "    Get", "        Return _${1:Name}", "    End Get",
            "    Set(value As ${2:Type})", "        _${1:Name} = value", "    End Set", "End Property");

        AddBuiltIn("Auto Property", "propa", "Properties",
            "Create an auto-implemented property",
            "Public Property ${1:Name} As ${2:String}");

        AddBuiltIn("ReadOnly Property", "propr", "Properties",
            "Create a read-only property",
            "ReadOnly Property ${1:Name} As ${2:Type}", "    Get", "        Return ${3:value}", "    End Get", "End Property");

        // Variables
        AddBuiltIn("Dim Variable", "dim", "Variables",
            "Declare a variable",
            "Dim ${1:name} As ${2:Integer} = ${0:0}");

        AddBuiltIn("Dim String", "dims", "Variables",
            "String variable declaration",
            "Dim ${1:name} As String = \"${0}\"");

        AddBuiltIn("Dim Boolean", "dimb", "Variables",
            "Boolean variable declaration",
            "Dim ${1:name} As Boolean = ${0:False}");

        AddBuiltIn("Array Declaration", "arr", "Variables",
            "Array declaration",
            "Dim ${1:name}(${2:10}) As ${3:Integer}");

        AddBuiltIn("Const", "const", "Variables",
            "Declare a constant",
            "Const ${1:NAME} As ${2:Type} = ${3:value}");

        // Output
        AddBuiltIn("Console.PrintLine", "print", "Output",
            "Print line to console",
            "Console.PrintLine($0)");

        AddBuiltIn("Console.WriteLine", "cw", "Output",
            "Write line to console",
            "Console.WriteLine(${0})");

        AddBuiltIn("Console.ReadLine", "cr", "Output",
            "Read line from console",
            "Console.ReadLine()");

        // Constructors
        AddBuiltIn("Constructor", "ctor", "Declarations",
            "Create a constructor",
            "Public Sub New()", "    $0", "End Sub");

        AddBuiltIn("Constructor with Parameters", "ctorp", "Declarations",
            "Constructor with parameters",
            "Public Sub New(${1:param} As ${2:Type})", "    _${1:param} = ${1:param}", "    $0", "End Sub");

        // Entry Points
        AddBuiltIn("Main", "main", "Patterns",
            "Main entry point",
            "Sub Main()", "    $0", "End Sub");

        AddBuiltIn("Main with Args", "maina", "Patterns",
            "Main with command-line arguments",
            "Sub Main(args As String())", "    $0", "End Sub");

        // Async
        AddBuiltIn("Async Function", "asyncf", "Async",
            "Async function",
            "Async Function ${1:Name}(${2:params}) As Task(Of ${3:Result})", "    $0", "End Function");

        AddBuiltIn("Async Sub", "asyncs", "Async",
            "Async subroutine",
            "Async Sub ${1:Name}(${2:params})", "    $0", "End Sub");

        AddBuiltIn("Await", "await", "Async",
            "Await expression",
            "Dim ${1:result} = Await ${0:task}");

        // Game Development
        AddBuiltIn("Game Template", "game", "Game Development",
            "Full game template with Initialize, Update, Render, and Shutdown",
            "Module Game",
            "    Sub Initialize()",
            "        ' Set up window and resources",
            "        InitWindow(${1:800}, ${2:600}, \"${3:My Game}\")",
            "        SetTargetFPS(60)",
            "    End Sub",
            "",
            "    Sub Update()",
            "        ' Update game logic",
            "        $0",
            "    End Sub",
            "",
            "    Sub Render()",
            "        BeginDrawing()",
            "        ClearBackground(RAYWHITE)",
            "        ' Draw game objects",
            "        EndDrawing()",
            "    End Sub",
            "",
            "    Sub Shutdown()",
            "        CloseWindow()",
            "    End Sub",
            "",
            "    Sub Main()",
            "        Initialize()",
            "        While Not WindowShouldClose()",
            "            Update()",
            "            Render()",
            "        End While",
            "        Shutdown()",
            "    End Sub",
            "End Module");

        AddBuiltIn("Game Loop", "gameloop", "Game Development",
            "Basic game loop",
            "While Not WindowShouldClose()",
            "    ' Update",
            "    $0",
            "",
            "    ' Render",
            "    BeginDrawing()",
            "    ClearBackground(RAYWHITE)",
            "    EndDrawing()",
            "End While");

        AddBuiltIn("Draw Text", "drawtext", "Game Development",
            "Draw text on screen",
            "DrawText(\"${1:Hello World}\", ${2:10}, ${3:10}, ${4:20}, ${5:DARKGRAY})");

        AddBuiltIn("Draw Rectangle", "drawrect", "Game Development",
            "Draw a rectangle",
            "DrawRectangle(${1:x}, ${2:y}, ${3:width}, ${4:height}, ${5:RED})");

        // Patterns
        AddBuiltIn("Singleton Pattern", "singleton", "Patterns",
            "Singleton class pattern",
            "Public Class ${1:ClassName}",
            "    Private Shared _instance As ${1:ClassName}",
            "",
            "    Private Sub New()",
            "        ' Private constructor",
            "    End Sub",
            "",
            "    Public Shared Function GetInstance() As ${1:ClassName}",
            "        If _instance Is Nothing Then",
            "            _instance = New ${1:ClassName}()",
            "        End If",
            "        Return _instance",
            "    End Function",
            "    $0",
            "End Class");

        AddBuiltIn("Observer Pattern", "observer", "Patterns",
            "Observer/event pattern",
            "Public Event ${1:OnChanged}(sender As Object, e As EventArgs)",
            "",
            "Protected Sub Raise${1:OnChanged}()",
            "    RaiseEvent ${1:OnChanged}(Me, EventArgs.Empty)",
            "End Sub");

        // Using/Import
        AddBuiltIn("Using Block", "using", "Statements",
            "Using block for disposable resources",
            "Using ${1:resource} As ${2:IDisposable} = ${3:New Object()}", "    $0", "End Using");

        AddBuiltIn("Import", "import", "Statements",
            "Import a module or file",
            "Import ${1:ModuleName}");

        AddBuiltIn("Using Directive", "usingns", "Statements",
            "Using namespace directive",
            "Using ${1:System.Collections.Generic}");

        // Comments
        AddBuiltIn("Region", "region", "Comments",
            "Create a code region",
            "#Region \"${1:RegionName}\"", "$0", "#End Region");

        AddBuiltIn("Comment Block", "comment", "Comments",
            "Multi-line comment header",
            "' ============================================",
            "' ${1:Description}",
            "' ============================================");

        AddBuiltIn("TODO Comment", "todo", "Comments",
            "TODO comment",
            "' TODO: ${0:description}");

        AddBuiltIn("Doc Comment", "doc", "Comments",
            "Documentation comment",
            "' <summary>",
            "' ${0:Description}",
            "' </summary>");

        // Lambda
        AddBuiltIn("Lambda Expression", "lambda", "Expressions",
            "Lambda expression",
            "Function(${1:x}) ${2:x * 2}");

        AddBuiltIn("Lambda Sub", "lambdasub", "Expressions",
            "Lambda subroutine",
            "Sub(${1:x}) ${2:Console.WriteLine(x)}");

        // Also register legacy snippets for backward compat
        RegisterLegacySnippets();
    }

    private void AddBuiltIn(string name, string prefix, string category, string description, params string[] body)
    {
        _builtInSnippets.Add(new Snippet
        {
            Name = name,
            Prefix = prefix,
            Body = body,
            Description = description,
            Scope = "basiclang",
            IsBuiltIn = true,
            Source = "built-in",
            Category = category
        });
    }

    private void RegisterLegacySnippets()
    {
        // Map built-in snippets to legacy format for backward compat
        foreach (var snippet in _builtInSnippets)
        {
            _legacySnippets[snippet.Prefix] = new CodeSnippet
            {
                Shortcut = snippet.Prefix,
                Title = snippet.Name,
                Description = snippet.Description,
                Category = snippet.Category,
                Body = string.Join("\n", snippet.Body)
            };
        }
    }

    #endregion
}
