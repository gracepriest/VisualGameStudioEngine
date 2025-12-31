using System.Text.RegularExpressions;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.ProjectSystem.Services;

public class SnippetService : ISnippetService
{
    private readonly Dictionary<string, CodeSnippet> _snippets = new(StringComparer.OrdinalIgnoreCase);

    public SnippetService()
    {
        LoadBuiltInSnippets();
    }

    private void LoadBuiltInSnippets()
    {
        // Control Flow
        RegisterSnippet(new CodeSnippet
        {
            Shortcut = "if",
            Title = "If Statement",
            Description = "If...Then...End If block",
            Category = "Control Flow",
            Body = "If ${1:condition} Then\n    ${2:' code}\nEnd If",
            Variables = new[]
            {
                new SnippetVariable { Name = "1", DefaultValue = "condition", Description = "Condition" },
                new SnippetVariable { Name = "2", DefaultValue = "' code", Description = "Code block" }
            }
        });

        RegisterSnippet(new CodeSnippet
        {
            Shortcut = "ife",
            Title = "If-Else Statement",
            Description = "If...Then...Else...End If block",
            Category = "Control Flow",
            Body = "If ${1:condition} Then\n    ${2:' true code}\nElse\n    ${3:' false code}\nEnd If",
            Variables = new[]
            {
                new SnippetVariable { Name = "1", DefaultValue = "condition", Description = "Condition" },
                new SnippetVariable { Name = "2", DefaultValue = "' true code", Description = "True branch" },
                new SnippetVariable { Name = "3", DefaultValue = "' false code", Description = "False branch" }
            }
        });

        RegisterSnippet(new CodeSnippet
        {
            Shortcut = "ifel",
            Title = "If-ElseIf-Else Statement",
            Description = "If...ElseIf...Else...End If block",
            Category = "Control Flow",
            Body = "If ${1:condition1} Then\n    ${2:' code 1}\nElseIf ${3:condition2} Then\n    ${4:' code 2}\nElse\n    ${5:' else code}\nEnd If"
        });

        RegisterSnippet(new CodeSnippet
        {
            Shortcut = "for",
            Title = "For Loop",
            Description = "For...Next loop",
            Category = "Control Flow",
            Body = "For ${1:i} = ${2:0} To ${3:10}\n    ${4:' code}\nNext",
            Variables = new[]
            {
                new SnippetVariable { Name = "1", DefaultValue = "i", Description = "Loop variable" },
                new SnippetVariable { Name = "2", DefaultValue = "0", Description = "Start value" },
                new SnippetVariable { Name = "3", DefaultValue = "10", Description = "End value" }
            }
        });

        RegisterSnippet(new CodeSnippet
        {
            Shortcut = "fors",
            Title = "For Loop with Step",
            Description = "For...Next loop with Step",
            Category = "Control Flow",
            Body = "For ${1:i} = ${2:0} To ${3:10} Step ${4:1}\n    ${5:' code}\nNext"
        });

        RegisterSnippet(new CodeSnippet
        {
            Shortcut = "fore",
            Title = "For Each Loop",
            Description = "For Each...Next loop",
            Category = "Control Flow",
            Body = "For Each ${1:item} In ${2:collection}\n    ${3:' code}\nNext"
        });

        RegisterSnippet(new CodeSnippet
        {
            Shortcut = "while",
            Title = "While Loop",
            Description = "While...Wend loop",
            Category = "Control Flow",
            Body = "While ${1:condition}\n    ${2:' code}\nWend"
        });

        RegisterSnippet(new CodeSnippet
        {
            Shortcut = "do",
            Title = "Do Loop",
            Description = "Do...Loop While",
            Category = "Control Flow",
            Body = "Do\n    ${1:' code}\nLoop While ${2:condition}"
        });

        RegisterSnippet(new CodeSnippet
        {
            Shortcut = "dou",
            Title = "Do Until Loop",
            Description = "Do...Loop Until",
            Category = "Control Flow",
            Body = "Do\n    ${1:' code}\nLoop Until ${2:condition}"
        });

        RegisterSnippet(new CodeSnippet
        {
            Shortcut = "sel",
            Title = "Select Case",
            Description = "Select Case...End Select block",
            Category = "Control Flow",
            Body = "Select Case ${1:expression}\n    Case ${2:value1}\n        ${3:' code 1}\n    Case ${4:value2}\n        ${5:' code 2}\n    Case Else\n        ${6:' default code}\nEnd Select"
        });

        RegisterSnippet(new CodeSnippet
        {
            Shortcut = "try",
            Title = "Try-Catch Block",
            Description = "Try...Catch...End Try block",
            Category = "Control Flow",
            Body = "Try\n    ${1:' code}\nCatch ${2:ex} As Exception\n    ${3:' error handling}\nEnd Try"
        });

        RegisterSnippet(new CodeSnippet
        {
            Shortcut = "tryf",
            Title = "Try-Catch-Finally Block",
            Description = "Try...Catch...Finally...End Try block",
            Category = "Control Flow",
            Body = "Try\n    ${1:' code}\nCatch ${2:ex} As Exception\n    ${3:' error handling}\nFinally\n    ${4:' cleanup}\nEnd Try"
        });

        // Declarations
        RegisterSnippet(new CodeSnippet
        {
            Shortcut = "sub",
            Title = "Sub Procedure",
            Description = "Sub...End Sub block",
            Category = "Declarations",
            Body = "Sub ${1:MethodName}(${2:})\n    ${3:' code}\nEnd Sub"
        });

        RegisterSnippet(new CodeSnippet
        {
            Shortcut = "func",
            Title = "Function",
            Description = "Function...End Function block",
            Category = "Declarations",
            Body = "Function ${1:FunctionName}(${2:}) As ${3:Integer}\n    ${4:' code}\n    Return ${5:0}\nEnd Function"
        });

        RegisterSnippet(new CodeSnippet
        {
            Shortcut = "class",
            Title = "Class",
            Description = "Class...End Class block",
            Category = "Declarations",
            Body = "Public Class ${1:ClassName}\n    ${2:' fields}\n\n    Public Sub New()\n        ${3:' constructor}\n    End Sub\n\n    ${4:' methods}\nEnd Class"
        });

        RegisterSnippet(new CodeSnippet
        {
            Shortcut = "mod",
            Title = "Module",
            Description = "Module...End Module block",
            Category = "Declarations",
            Body = "Module ${1:ModuleName}\n    ${2:' code}\nEnd Module"
        });

        RegisterSnippet(new CodeSnippet
        {
            Shortcut = "prop",
            Title = "Property",
            Description = "Property with Get and Set",
            Category = "Declarations",
            Body = "Private _${1:name} As ${2:String}\n\nPublic Property ${1:name} As ${2:String}\n    Get\n        Return _${1:name}\n    End Get\n    Set(value As ${2:String})\n        _${1:name} = value\n    End Set\nEnd Property"
        });

        RegisterSnippet(new CodeSnippet
        {
            Shortcut = "propg",
            Title = "Property (Get Only)",
            Description = "Read-only Property",
            Category = "Declarations",
            Body = "Private _${1:name} As ${2:String}\n\nPublic ReadOnly Property ${1:name} As ${2:String}\n    Get\n        Return _${1:name}\n    End Get\nEnd Property"
        });

        RegisterSnippet(new CodeSnippet
        {
            Shortcut = "enum",
            Title = "Enum",
            Description = "Enum...End Enum block",
            Category = "Declarations",
            Body = "Public Enum ${1:EnumName}\n    ${2:Value1}\n    ${3:Value2}\n    ${4:Value3}\nEnd Enum"
        });

        RegisterSnippet(new CodeSnippet
        {
            Shortcut = "intf",
            Title = "Interface",
            Description = "Interface...End Interface block",
            Category = "Declarations",
            Body = "Public Interface ${1:IInterfaceName}\n    ${2:Sub MethodName()}\nEnd Interface"
        });

        // Variables
        RegisterSnippet(new CodeSnippet
        {
            Shortcut = "dim",
            Title = "Dim Variable",
            Description = "Variable declaration",
            Category = "Variables",
            Body = "Dim ${1:variableName} As ${2:Integer}"
        });

        RegisterSnippet(new CodeSnippet
        {
            Shortcut = "dims",
            Title = "Dim String",
            Description = "String variable declaration",
            Category = "Variables",
            Body = "Dim ${1:variableName} As String = \"${2:}\""
        });

        RegisterSnippet(new CodeSnippet
        {
            Shortcut = "dimi",
            Title = "Dim Integer",
            Description = "Integer variable declaration",
            Category = "Variables",
            Body = "Dim ${1:variableName} As Integer = ${2:0}"
        });

        RegisterSnippet(new CodeSnippet
        {
            Shortcut = "dimb",
            Title = "Dim Boolean",
            Description = "Boolean variable declaration",
            Category = "Variables",
            Body = "Dim ${1:variableName} As Boolean = ${2:False}"
        });

        RegisterSnippet(new CodeSnippet
        {
            Shortcut = "arr",
            Title = "Array Declaration",
            Description = "Array variable declaration",
            Category = "Variables",
            Body = "Dim ${1:arrayName}(${2:10}) As ${3:Integer}"
        });

        // Output
        RegisterSnippet(new CodeSnippet
        {
            Shortcut = "pl",
            Title = "PrintLine",
            Description = "Print line to console",
            Category = "Output",
            Body = "PrintLine(${1:\"message\"})"
        });

        RegisterSnippet(new CodeSnippet
        {
            Shortcut = "pr",
            Title = "Print",
            Description = "Print to console",
            Category = "Output",
            Body = "Print(${1:\"message\"})"
        });

        // Comments
        RegisterSnippet(new CodeSnippet
        {
            Shortcut = "rem",
            Title = "Comment Block",
            Description = "Multi-line comment",
            Category = "Comments",
            Body = "' ============================================\n' ${1:Description}\n' ============================================"
        });

        RegisterSnippet(new CodeSnippet
        {
            Shortcut = "todo",
            Title = "TODO Comment",
            Description = "TODO comment",
            Category = "Comments",
            Body = "' TODO: ${1:description}"
        });

        // Common patterns
        RegisterSnippet(new CodeSnippet
        {
            Shortcut = "main",
            Title = "Main Entry Point",
            Description = "Main Sub procedure",
            Category = "Patterns",
            Body = "Module Program\n    Sub Main()\n        ${1:' Your code here}\n    End Sub\nEnd Module"
        });

        RegisterSnippet(new CodeSnippet
        {
            Shortcut = "singleton",
            Title = "Singleton Pattern",
            Description = "Singleton class pattern",
            Category = "Patterns",
            Body = "Public Class ${1:ClassName}\n    Private Shared _instance As ${1:ClassName}\n\n    Private Sub New()\n        ' Private constructor\n    End Sub\n\n    Public Shared Function GetInstance() As ${1:ClassName}\n        If _instance Is Nothing Then\n            _instance = New ${1:ClassName}()\n        End If\n        Return _instance\n    End Function\nEnd Class"
        });
    }

    public IReadOnlyList<CodeSnippet> GetSnippets()
    {
        return _snippets.Values.ToList();
    }

    public IReadOnlyList<CodeSnippet> SearchSnippets(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return GetSnippets();

        return _snippets.Values
            .Where(s => s.Shortcut.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                       s.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                       s.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public CodeSnippet? GetSnippet(string shortcut)
    {
        return _snippets.GetValueOrDefault(shortcut);
    }

    public string ExpandSnippet(CodeSnippet snippet, Dictionary<string, string>? variables = null)
    {
        var result = snippet.Body;

        // Replace variables with their values or defaults
        var pattern = @"\$\{(\d+):([^}]*)\}";
        result = Regex.Replace(result, pattern, match =>
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
        _snippets[snippet.Shortcut] = snippet;
    }

    public void RemoveSnippet(string shortcut)
    {
        _snippets.Remove(shortcut);
    }
}
