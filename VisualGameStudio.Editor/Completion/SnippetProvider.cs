using System.Text.RegularExpressions;
using AvaloniaEdit.Snippets;

namespace VisualGameStudio.Editor.Completion;

/// <summary>
/// Defines a code snippet with tab-stop placeholders.
/// Mirrors the VS Code snippet format from vscode-basiclang/snippets/basiclang.json.
/// </summary>
public class SnippetDefinition
{
    public string Name { get; set; } = "";
    public string[] Prefixes { get; set; } = Array.Empty<string>();
    public string[] BodyLines { get; set; } = Array.Empty<string>();
    public string Description { get; set; } = "";

    /// <summary>
    /// Expands the snippet body into final text, stripping tab-stop markers.
    /// Returns the text and the offset of the final cursor position ($0 or end of text).
    /// </summary>
    public (string Text, int CursorOffset) Expand(string currentIndent)
    {
        var lines = new List<string>();
        foreach (var line in BodyLines)
        {
            // First line gets no extra indent (it replaces the prefix)
            var prefix = lines.Count == 0 ? "" : currentIndent;
            lines.Add(prefix + line);
        }

        var raw = string.Join(Environment.NewLine, lines);

        // Strip tab-stop markers: ${N:defaultText} -> defaultText, $N -> ""
        // First handle ${N:text} placeholders - keep the default text
        var expanded = Regex.Replace(raw, @"\$\{(\d+):([^}]*)\}", "$2");
        // Then handle bare $N placeholders (except $0 which we handle specially)
        // Find $0 position first
        int cursorMarkerIndex = expanded.IndexOf("$0", StringComparison.Ordinal);

        // Remove all remaining $N markers
        expanded = Regex.Replace(expanded, @"\$\d+", "");

        // Recalculate cursor position after removing markers
        if (cursorMarkerIndex >= 0)
        {
            // Count how many $N markers appear before $0
            var beforeCursor = raw.Substring(0, raw.IndexOf("$0", StringComparison.Ordinal));
            beforeCursor = Regex.Replace(beforeCursor, @"\$\{(\d+):([^}]*)\}", "$2");
            beforeCursor = Regex.Replace(beforeCursor, @"\$\d+", "");
            return (expanded, beforeCursor.Length);
        }

        // No $0 marker — place cursor at end
        return (expanded, expanded.Length);
    }

    /// <summary>
    /// Builds an AvaloniaEdit Snippet object with proper tab-stop elements.
    /// Parses $1, $2, ${1:default}, $0 patterns and creates SnippetReplaceableTextElement
    /// entries that support Tab/Shift+Tab cycling between placeholder positions.
    /// Repeated tab-stop numbers (e.g. multiple ${1:Name}) use SnippetBoundElement
    /// so editing one updates all linked occurrences.
    /// </summary>
    public Snippet BuildSnippet(string currentIndent)
    {
        var lines = new List<string>();
        foreach (var line in BodyLines)
        {
            var prefix = lines.Count == 0 ? "" : currentIndent;
            lines.Add(prefix + line);
        }

        var raw = string.Join(Environment.NewLine, lines);

        // Parse the raw text into a sequence of snippet elements.
        // We scan for $0, $N, ${N:text} patterns and split around them.
        var snippet = new Snippet();

        // Dictionary mapping tab-stop number -> the first SnippetReplaceableTextElement for that number.
        // Subsequent occurrences of the same number become SnippetBoundElement linked to the first.
        var tabStopElements = new Dictionary<int, SnippetReplaceableTextElement>();

        // Regex matches ${N:text} or $N patterns
        var pattern = new Regex(@"\$\{(\d+):([^}]*)\}|\$(\d+)");

        int lastIndex = 0;
        foreach (Match match in pattern.Matches(raw))
        {
            // Add any literal text before this match
            if (match.Index > lastIndex)
            {
                snippet.Elements.Add(new SnippetTextElement
                {
                    Text = raw.Substring(lastIndex, match.Index - lastIndex)
                });
            }

            int tabStopNumber;
            string defaultText;

            if (match.Groups[1].Success)
            {
                // ${N:text} pattern
                tabStopNumber = int.Parse(match.Groups[1].Value);
                defaultText = match.Groups[2].Value;
            }
            else
            {
                // $N pattern
                tabStopNumber = int.Parse(match.Groups[3].Value);
                defaultText = "";
            }

            if (tabStopNumber == 0)
            {
                // $0 is the final caret position
                snippet.Elements.Add(new SnippetCaretElement());
            }
            else if (!tabStopElements.ContainsKey(tabStopNumber))
            {
                // First occurrence of this tab-stop: create a replaceable element
                var replaceable = new SnippetReplaceableTextElement { Text = defaultText };
                tabStopElements[tabStopNumber] = replaceable;
                snippet.Elements.Add(replaceable);
            }
            else
            {
                // Subsequent occurrence: create a bound element linked to the first
                snippet.Elements.Add(new SnippetBoundElement
                {
                    TargetElement = tabStopElements[tabStopNumber]
                });
            }

            lastIndex = match.Index + match.Length;
        }

        // Add any remaining literal text after the last match
        if (lastIndex < raw.Length)
        {
            snippet.Elements.Add(new SnippetTextElement
            {
                Text = raw.Substring(lastIndex)
            });
        }

        return snippet;
    }
}

/// <summary>
/// Provides all built-in BasicLang snippets, matching the VS Code extension's snippet set.
/// </summary>
public static class SnippetProvider
{
    private static readonly List<SnippetDefinition> _snippets = new();

    static SnippetProvider()
    {
        RegisterAll();
    }

    public static IReadOnlyList<SnippetDefinition> Snippets => _snippets;

    /// <summary>
    /// Finds snippets whose prefix matches the given text (case-insensitive).
    /// </summary>
    public static IEnumerable<SnippetDefinition> FindByPrefix(string prefix)
    {
        if (string.IsNullOrEmpty(prefix)) return Enumerable.Empty<SnippetDefinition>();
        return _snippets.Where(s =>
            s.Prefixes.Any(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Finds a snippet with an exact prefix match (case-insensitive).
    /// </summary>
    public static SnippetDefinition? FindExactMatch(string prefix)
    {
        if (string.IsNullOrEmpty(prefix)) return null;
        return _snippets.FirstOrDefault(s =>
            s.Prefixes.Any(p => string.Equals(p, prefix, StringComparison.OrdinalIgnoreCase)));
    }

    private static void Add(string name, string[] prefixes, string[] body, string description)
    {
        _snippets.Add(new SnippetDefinition
        {
            Name = name,
            Prefixes = prefixes,
            BodyLines = body,
            Description = description
        });
    }

    private static void RegisterAll()
    {
        Add("Function", new[] { "func", "function" }, new[]
        {
            "Function ${1:Name}(${2:params}) As ${3:Integer}",
            "    ${0}",
            "End Function"
        }, "Create a new function");

        Add("Sub", new[] { "sub", "subroutine" }, new[]
        {
            "Sub ${1:Name}(${2:params})",
            "    ${0}",
            "End Sub"
        }, "Create a new subroutine");

        Add("If Statement", new[] { "if" }, new[]
        {
            "If ${1:condition} Then",
            "    ${0}",
            "End If"
        }, "If statement");

        Add("If Else Statement", new[] { "ife" }, new[]
        {
            "If ${1:condition} Then",
            "    ${2}",
            "Else",
            "    ${0}",
            "End If"
        }, "If-Else statement");

        Add("If ElseIf Statement", new[] { "ifeif" }, new[]
        {
            "If ${1:condition} Then",
            "    ${2}",
            "ElseIf ${3:condition} Then",
            "    ${4}",
            "Else",
            "    ${0}",
            "End If"
        }, "If-ElseIf-Else statement");

        Add("For Loop", new[] { "for" }, new[]
        {
            "For ${1:i} As Integer = ${2:0} To ${3:10}",
            "    ${0}",
            "Next"
        }, "For loop");

        Add("For Step Loop", new[] { "fors" }, new[]
        {
            "For ${1:i} As Integer = ${2:0} To ${3:10} Step ${4:1}",
            "    ${0}",
            "Next"
        }, "For loop with step");

        Add("For Each Loop", new[] { "foreach", "fore" }, new[]
        {
            "For Each ${1:item} As ${2:Type} In ${3:collection}",
            "    ${0}",
            "Next"
        }, "For Each loop");

        Add("While Loop", new[] { "while" }, new[]
        {
            "While ${1:condition}",
            "    ${0}",
            "End While"
        }, "While loop");

        Add("Do While Loop", new[] { "dowhile" }, new[]
        {
            "Do While ${1:condition}",
            "    ${0}",
            "Loop"
        }, "Do While loop");

        Add("Do Until Loop", new[] { "dountil" }, new[]
        {
            "Do Until ${1:condition}",
            "    ${0}",
            "Loop"
        }, "Do Until loop");

        Add("Do Loop While", new[] { "doloopwhile" }, new[]
        {
            "Do",
            "    ${0}",
            "Loop While ${1:condition}"
        }, "Do Loop While");

        Add("Select Case", new[] { "select", "switch" }, new[]
        {
            "Select Case ${1:variable}",
            "    Case ${2:value1}",
            "        ${3}",
            "    Case ${4:value2}",
            "        ${5}",
            "    Case Else",
            "        ${0}",
            "End Select"
        }, "Select Case statement");

        Add("Class", new[] { "class" }, new[]
        {
            "Class ${1:ClassName}",
            "    ${0}",
            "End Class"
        }, "Create a new class");

        Add("Class with Constructor", new[] { "classc" }, new[]
        {
            "Class ${1:ClassName}",
            "    Private _${2:field} As ${3:Type}",
            "",
            "    Sub New(${2:field} As ${3:Type})",
            "        _${2:field} = ${2:field}",
            "    End Sub",
            "",
            "    ${0}",
            "End Class"
        }, "Create a class with constructor");

        Add("Module", new[] { "module" }, new[]
        {
            "Module ${1:ModuleName}",
            "    ${0}",
            "End Module"
        }, "Create a new module");

        Add("Namespace", new[] { "namespace" }, new[]
        {
            "Namespace ${1:NamespaceName}",
            "    ${0}",
            "End Namespace"
        }, "Create a namespace");

        Add("Property", new[] { "prop" }, new[]
        {
            "Property ${1:Name} As ${2:Type}",
            "    Get",
            "        Return _${1:Name}",
            "    End Get",
            "    Set(value As ${2:Type})",
            "        _${1:Name} = value",
            "    End Set",
            "End Property"
        }, "Create a property");

        Add("Auto Property", new[] { "propa" }, new[]
        {
            "Public Property ${1:Name} As ${2:Type}"
        }, "Create an auto property");

        Add("ReadOnly Property", new[] { "propr" }, new[]
        {
            "ReadOnly Property ${1:Name} As ${2:Type}",
            "    Get",
            "        Return ${3:value}",
            "    End Get",
            "End Property"
        }, "Create a read-only property");

        Add("Try Catch", new[] { "try" }, new[]
        {
            "Try",
            "    ${0}",
            "Catch ${1:ex} As Exception",
            "    ' Handle error",
            "End Try"
        }, "Try-Catch block");

        Add("Try Catch Finally", new[] { "tryf" }, new[]
        {
            "Try",
            "    ${0}",
            "Catch ${1:ex} As Exception",
            "    ' Handle error",
            "Finally",
            "    ' Cleanup",
            "End Try"
        }, "Try-Catch-Finally block");

        Add("Using Block", new[] { "using" }, new[]
        {
            "Using ${1:resource} As ${2:IDisposable} = ${3:New Object()}",
            "    ${0}",
            "End Using"
        }, "Using block for disposable resources");

        Add("Event", new[] { "event" }, new[]
        {
            "Public Event ${1:EventName}(sender As Object, e As ${2:EventArgs})"
        }, "Declare an event");

        Add("Delegate", new[] { "delegate" }, new[]
        {
            "Public Delegate Sub ${1:DelegateName}(${2:params})"
        }, "Declare a delegate");

        Add("Enum", new[] { "enum" }, new[]
        {
            "Enum ${1:EnumName}",
            "    ${2:Value1}",
            "    ${3:Value2}",
            "    ${0}",
            "End Enum"
        }, "Create an enumeration");

        Add("Interface", new[] { "interface" }, new[]
        {
            "Interface ${1:IInterfaceName}",
            "    ${0}",
            "End Interface"
        }, "Create an interface");

        Add("Structure", new[] { "struct" }, new[]
        {
            "Structure ${1:StructName}",
            "    Public ${2:Field1} As ${3:Type}",
            "    ${0}",
            "End Structure"
        }, "Create a structure");

        Add("Console.WriteLine", new[] { "cw", "writeline" }, new[]
        {
            "Console.WriteLine(${0})"
        }, "Write to console");

        Add("Console.ReadLine", new[] { "cr", "readline" }, new[]
        {
            "Console.ReadLine()"
        }, "Read from console");

        Add("Dim Variable", new[] { "dim" }, new[]
        {
            "Dim ${1:name} As ${2:Type}"
        }, "Declare a variable");

        Add("Dim With Value", new[] { "dimv" }, new[]
        {
            "Dim ${1:name} As ${2:Type} = ${3:value}"
        }, "Declare a variable with initial value");

        Add("Const", new[] { "const" }, new[]
        {
            "Const ${1:NAME} As ${2:Type} = ${3:value}"
        }, "Declare a constant");

        Add("Main", new[] { "main" }, new[]
        {
            "Sub Main()",
            "    ${0}",
            "End Sub"
        }, "Main entry point");

        Add("Main with Args", new[] { "maina" }, new[]
        {
            "Sub Main(args As String())",
            "    ${0}",
            "End Sub"
        }, "Main entry point with arguments");

        Add("Lambda", new[] { "lambda" }, new[]
        {
            "Function(${1:x}) ${2:x * 2}"
        }, "Lambda expression");

        Add("Async Function", new[] { "asyncf" }, new[]
        {
            "Async Function ${1:Name}(${2:params}) As Task(Of ${3:Result})",
            "    ${0}",
            "End Function"
        }, "Async function");

        Add("Async Sub", new[] { "asyncs" }, new[]
        {
            "Async Sub ${1:Name}(${2:params})",
            "    ${0}",
            "End Sub"
        }, "Async subroutine");

        Add("Region", new[] { "region" }, new[]
        {
            "#Region \"${1:RegionName}\"",
            "${0}",
            "#End Region"
        }, "Create a code region");
    }
}
