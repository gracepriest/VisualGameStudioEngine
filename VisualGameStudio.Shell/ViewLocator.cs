using Avalonia.Controls;
using Avalonia.Controls.Templates;
using VisualGameStudio.Shell.Dock;
using VisualGameStudio.Shell.Views.Documents;
using VisualGameStudio.Shell.Views.Panels;

namespace VisualGameStudio.Shell;

public class ViewLocator : IDataTemplate
{
    public Control Build(object? data)
    {
        if (data is null)
            return new TextBlock { Text = "Data is null" };

        return data switch
        {
            SolutionExplorerTool tool => new SolutionExplorerView { DataContext = tool.ViewModel },
            OutputTool tool => new OutputPanelView { DataContext = tool.ViewModel },
            ErrorListTool tool => new ErrorListView { DataContext = tool.ViewModel },
            CallStackTool tool => new CallStackView { DataContext = tool.ViewModel },
            VariablesTool tool => new VariablesView { DataContext = tool.ViewModel },
            BreakpointsTool tool => new BreakpointsView { DataContext = tool.ViewModel },
            FindInFilesTool tool => new FindInFilesView { DataContext = tool.ViewModel },
            TerminalTool tool => new TerminalView { DataContext = tool.ViewModel },
            GitChangesTool tool => new GitChangesView { DataContext = tool.ViewModel },
            WatchTool tool => new WatchView { DataContext = tool.ViewModel },
            DocumentOutlineTool tool => new DocumentOutlineView { DataContext = tool.ViewModel },
            BookmarksTool tool => new BookmarksView { DataContext = tool.ViewModel },
            CodeEditorDocument doc => new CodeEditorDocumentView { DataContext = doc.ViewModel },
            WelcomeDocument => new WelcomeDocumentView(),
            _ => CreateDefault(data)
        };
    }

    private static Control CreateDefault(object data)
    {
        var typeName = data.GetType().FullName!;
        var viewName = typeName.Replace("ViewModel", "View");

        var type = Type.GetType(viewName);
        if (type != null)
        {
            return (Control)Activator.CreateInstance(type)!;
        }

        return new TextBlock { Text = $"Not Found: {viewName}" };
    }

    public bool Match(object? data)
    {
        return data is SolutionExplorerTool
            or OutputTool
            or ErrorListTool
            or CallStackTool
            or VariablesTool
            or BreakpointsTool
            or FindInFilesTool
            or TerminalTool
            or GitChangesTool
            or WatchTool
            or DocumentOutlineTool
            or BookmarksTool
            or CodeEditorDocument
            or WelcomeDocument;
    }
}
