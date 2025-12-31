using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Panels;

public partial class DocumentOutlineViewModel : ViewModelBase
{
    [ObservableProperty]
    private ObservableCollection<OutlineNode> _nodes = new();

    [ObservableProperty]
    private OutlineNode? _selectedNode;

    [ObservableProperty]
    private string _currentFile = "";

    [ObservableProperty]
    private bool _sortAlphabetically;

    public event EventHandler<OutlineNavigationEventArgs>? NavigationRequested;

    public void UpdateOutline(string filePath, string content)
    {
        CurrentFile = Path.GetFileName(filePath);
        Nodes.Clear();

        if (string.IsNullOrEmpty(content)) return;

        var lines = content.Split('\n');
        var nodeStack = new Stack<OutlineNode>();
        OutlineNode? currentModule = null;
        OutlineNode? currentClass = null;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            var lineNumber = i + 1;

            // Skip empty lines and comments
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("'") || line.StartsWith("REM "))
                continue;

            // Module
            if (line.StartsWith("Module ", StringComparison.OrdinalIgnoreCase))
            {
                var name = ExtractName(line, "Module ");
                currentModule = new OutlineNode
                {
                    Name = name,
                    NodeType = OutlineNodeType.Module,
                    Line = lineNumber,
                    Icon = "📦"
                };
                Nodes.Add(currentModule);
                currentClass = null;
            }
            // End Module
            else if (line.StartsWith("End Module", StringComparison.OrdinalIgnoreCase))
            {
                currentModule = null;
            }
            // Class
            else if (line.StartsWith("Class ", StringComparison.OrdinalIgnoreCase) ||
                     line.Contains(" Class ", StringComparison.OrdinalIgnoreCase))
            {
                var name = ExtractName(line, "Class ");
                currentClass = new OutlineNode
                {
                    Name = name,
                    NodeType = OutlineNodeType.Class,
                    Line = lineNumber,
                    Icon = "🔷"
                };

                if (currentModule != null)
                    currentModule.Children.Add(currentClass);
                else
                    Nodes.Add(currentClass);
            }
            // End Class
            else if (line.StartsWith("End Class", StringComparison.OrdinalIgnoreCase))
            {
                currentClass = null;
            }
            // Sub
            else if (line.StartsWith("Sub ", StringComparison.OrdinalIgnoreCase) ||
                     line.Contains(" Sub ", StringComparison.OrdinalIgnoreCase))
            {
                var name = ExtractMethodName(line, "Sub ");
                if (!string.IsNullOrEmpty(name))
                {
                    var node = new OutlineNode
                    {
                        Name = name,
                        NodeType = OutlineNodeType.Sub,
                        Line = lineNumber,
                        Icon = "🔹"
                    };

                    AddToParent(node, currentClass, currentModule);
                }
            }
            // Function
            else if (line.StartsWith("Function ", StringComparison.OrdinalIgnoreCase) ||
                     line.Contains(" Function ", StringComparison.OrdinalIgnoreCase))
            {
                var name = ExtractMethodName(line, "Function ");
                if (!string.IsNullOrEmpty(name))
                {
                    var node = new OutlineNode
                    {
                        Name = name,
                        NodeType = OutlineNodeType.Function,
                        Line = lineNumber,
                        Icon = "🔸"
                    };

                    AddToParent(node, currentClass, currentModule);
                }
            }
            // Property
            else if (line.StartsWith("Property ", StringComparison.OrdinalIgnoreCase) ||
                     line.Contains(" Property ", StringComparison.OrdinalIgnoreCase))
            {
                var name = ExtractMethodName(line, "Property ");
                if (!string.IsNullOrEmpty(name))
                {
                    var node = new OutlineNode
                    {
                        Name = name,
                        NodeType = OutlineNodeType.Property,
                        Line = lineNumber,
                        Icon = "🔧"
                    };

                    AddToParent(node, currentClass, currentModule);
                }
            }
            // Enum
            else if (line.StartsWith("Enum ", StringComparison.OrdinalIgnoreCase))
            {
                var name = ExtractName(line, "Enum ");
                var node = new OutlineNode
                {
                    Name = name,
                    NodeType = OutlineNodeType.Enum,
                    Line = lineNumber,
                    Icon = "📋"
                };

                AddToParent(node, currentClass, currentModule);
            }
            // Interface
            else if (line.StartsWith("Interface ", StringComparison.OrdinalIgnoreCase))
            {
                var name = ExtractName(line, "Interface ");
                var node = new OutlineNode
                {
                    Name = name,
                    NodeType = OutlineNodeType.Interface,
                    Line = lineNumber,
                    Icon = "🔶"
                };

                if (currentModule != null)
                    currentModule.Children.Add(node);
                else
                    Nodes.Add(node);
            }
        }

        if (SortAlphabetically)
        {
            SortNodes(Nodes);
        }
    }

    private void AddToParent(OutlineNode node, OutlineNode? currentClass, OutlineNode? currentModule)
    {
        if (currentClass != null)
            currentClass.Children.Add(node);
        else if (currentModule != null)
            currentModule.Children.Add(node);
        else
            Nodes.Add(node);
    }

    private void SortNodes(ObservableCollection<OutlineNode> nodes)
    {
        var sorted = nodes.OrderBy(n => n.Name).ToList();
        nodes.Clear();
        foreach (var node in sorted)
        {
            nodes.Add(node);
            if (node.Children.Count > 0)
            {
                SortNodes(node.Children);
            }
        }
    }

    private static string ExtractName(string line, string keyword)
    {
        var idx = line.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return "";
        var rest = line.Substring(idx + keyword.Length).Trim();
        var endIdx = rest.IndexOfAny(new[] { ' ', '(', '\r', '\n', ':' });
        return endIdx > 0 ? rest.Substring(0, endIdx) : rest;
    }

    private static string ExtractMethodName(string line, string keyword)
    {
        var idx = line.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return "";
        var rest = line.Substring(idx + keyword.Length).Trim();
        var endIdx = rest.IndexOf('(');
        if (endIdx < 0) endIdx = rest.IndexOfAny(new[] { ' ', '\r', '\n' });
        return endIdx > 0 ? rest.Substring(0, endIdx).Trim() : rest.Trim();
    }

    [RelayCommand]
    private void NavigateToNode()
    {
        if (SelectedNode != null)
        {
            NavigationRequested?.Invoke(this, new OutlineNavigationEventArgs
            {
                Line = SelectedNode.Line
            });
        }
    }

    [RelayCommand]
    private void ToggleSort()
    {
        SortAlphabetically = !SortAlphabetically;
        // Re-parse would be needed to apply, or just sort in place
        if (SortAlphabetically)
        {
            SortNodes(Nodes);
        }
    }

    public void Clear()
    {
        Nodes.Clear();
        CurrentFile = "";
    }
}

public partial class OutlineNode : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private OutlineNodeType _nodeType;

    [ObservableProperty]
    private int _line;

    [ObservableProperty]
    private string _icon = "📄";

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private ObservableCollection<OutlineNode> _children = new();
}

public class OutlineNavigationEventArgs : EventArgs
{
    public int Line { get; set; }
}

public enum OutlineNodeType
{
    Module,
    Class,
    Interface,
    Sub,
    Function,
    Property,
    Enum,
    Field
}
