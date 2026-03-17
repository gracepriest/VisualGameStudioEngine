using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using VisualGameStudio.Core.Models;
using VisualGameStudio.Editor.Completion;
using VisualGameStudio.Editor.Controls;
using VisualGameStudio.Shell.ViewModels;
using VisualGameStudio.Shell.ViewModels.Documents;
using VisualGameStudio.Shell.Views.Controls;

namespace VisualGameStudio.Shell.Views.Documents;

public partial class CodeEditorDocumentView : UserControl
{
    private int _currentMatchIndex;
    private int _totalMatches;
    private CodeEditorDocumentViewModel? _subscribedVm;
    private bool _editorEventsWired;

    public CodeEditorDocumentView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        KeyDown += OnViewKeyDown;
    }

    private void UnsubscribeFromViewModel(CodeEditorDocumentViewModel vm)
    {
        vm.NavigationRequested -= OnNavigationRequested;
        vm.FindRequested -= OnFindRequested;
        vm.ReplaceRequested -= OnReplaceRequested;
        vm.ToggleCommentRequested -= OnToggleCommentRequestedHandler;
        vm.DuplicateLineRequested -= OnDuplicateLineRequestedHandler;
        vm.MoveLineUpRequested -= OnMoveLineUpRequestedHandler;
        vm.MoveLineDownRequested -= OnMoveLineDownRequestedHandler;
        vm.DeleteLineRequested -= OnDeleteLineRequestedHandler;
        vm.GetSelectionInfo = null;
        vm.DiagnosticsUpdated -= OnDiagnosticsUpdated;
        vm.CodeLensUpdated -= OnCodeLensUpdated;
        vm.InlineDebugValuesUpdated -= OnInlineDebugValuesUpdated;
        vm.ExecutionLineChanged -= OnExecutionLineChanged;
        vm.CompletionReceived -= OnCompletionReceived;
        vm.HoverResultReceived -= OnHoverResultReceived;
        vm.SignatureHelpResultReceived -= OnSignatureHelpResultReceived;
        vm.DocumentHighlightResultReceived -= OnDocumentHighlightResultReceived;
        vm.RenameResultReceived -= OnRenameResultReceived;
        vm.SelectionRangeReceived -= OnSelectionRangeReceived;
    }

    private void UnsubscribeFromEditor()
    {
        if (MainEditor != null && _editorEventsWired)
        {
            MainEditor.CompletionRequested -= OnCompletionRequested;
            MainEditor.GoToDefinitionRequested -= OnEditorGoToDefinition;
            MainEditor.PeekDefinitionRequested -= OnEditorPeekDefinition;
            MainEditor.FindAllReferencesRequested -= OnEditorFindAllReferences;
            MainEditor.RenameSymbolRequested -= OnEditorRenameSymbol;
            MainEditor.CodeActionsRequested -= OnEditorCodeActions;
            MainEditor.FormatDocumentRequested -= OnEditorFormatDocument;
            MainEditor.CodeLensClicked -= OnEditorCodeLensClicked;
            MainEditor.DataTipRequested -= OnDataTipRequested;
            MainEditor.SignatureHelpRequested -= OnEditorSignatureHelp;
            MainEditor.DocumentHighlightRequested -= OnEditorDocumentHighlight;
            MainEditor.DocumentLinkClicked -= OnEditorDocumentLinkClicked;
            MainEditor.EditorReady -= OnEditorReady;
            _editorEventsWired = false;
        }
    }

    // Named handlers to allow unsubscribe (replaces lambdas)
    private void OnToggleCommentRequestedHandler(object? s, EventArgs args) => MainEditor?.ToggleLineComment();
    private void OnDuplicateLineRequestedHandler(object? s, EventArgs args) => MainEditor?.DuplicateLine();
    private void OnMoveLineUpRequestedHandler(object? s, EventArgs args) => MainEditor?.MoveLineUp();
    private void OnMoveLineDownRequestedHandler(object? s, EventArgs args) => MainEditor?.MoveLineDown();
    private void OnDeleteLineRequestedHandler(object? s, EventArgs args) => MainEditor?.DeleteLine();

    private void OnEditorGoToDefinition(object? s, EventArgs e) => _subscribedVm?.RequestGoToDefinition();
    private void OnEditorPeekDefinition(object? s, EventArgs e) => _subscribedVm?.RequestPeekDefinition();
    private void OnEditorFindAllReferences(object? s, EventArgs e) => _subscribedVm?.RequestFindAllReferences();
    private void OnEditorRenameSymbol(object? s, EventArgs e) => _subscribedVm?.RequestRenameSymbol();
    private void OnEditorCodeActions(object? s, EventArgs e) => _subscribedVm?.RequestCodeActions();
    private void OnEditorFormatDocument(object? s, EventArgs e) => _subscribedVm?.RequestFormatDocument();
    private void OnEditorCodeLensClicked(object? s, Editor.TextMarkers.CodeLensClickedEventArgs e) =>
        _subscribedVm?.OnCodeLensClicked(new CodeLensClickedInfo
        {
            Title = e.Title,
            CommandName = e.CommandName,
            CommandArguments = e.CommandArguments,
            Line = e.Line
        });
    private void OnEditorSignatureHelp(object? s, Editor.Controls.SignatureHelpRequestEventArgs e) =>
        _subscribedVm?.RequestSignatureHelp(e.Line, e.Column);
    private void OnEditorDocumentHighlight(object? s, Editor.Controls.DocumentHighlightRequestEventArgs e) =>
        _subscribedVm?.RequestDocumentHighlight(e.Line, e.Column);

    private void OnEditorDocumentLinkClicked(object? s, Editor.Controls.DocumentLinkClickedEventArgs e)
    {
        try
        {
            if (string.IsNullOrEmpty(e.Target)) return;

            var target = e.Target;

            // Convert file URI to path
            if (target.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
            {
                target = Uri.UnescapeDataString(target.Substring(8));
                // Handle Windows paths (file:///C:/path -> C:\path)
                target = target.Replace('/', System.IO.Path.DirectorySeparatorChar);
            }
            else if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                     target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                // Open URLs in the default browser
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = target,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[DocumentLink] Failed to open URL: {ex.Message}");
                }
                return;
            }

            // Open the file in the IDE - find the MainWindowViewModel to call OpenFileAsync
            if (System.IO.File.Exists(target))
            {
                // Navigate up the visual tree to find MainWindow and its DataContext
                var mainWindow = TopLevel.GetTopLevel(this) as Avalonia.Controls.Window;
                if (mainWindow?.DataContext is MainWindowViewModel mainVm)
                {
                    _ = mainVm.OpenFileFromLinkAsync(target);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DocumentLink] Error handling link: {ex.Message}");
        }
    }

    private void OnEditorReady(object? s, EventArgs e)
    {
        if (_subscribedVm != null)
            InitializeBreakpointSupport(_subscribedVm);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        try
        {
        // Unsubscribe from previous ViewModel
        if (_subscribedVm != null)
        {
            UnsubscribeFromViewModel(_subscribedVm);
            UnsubscribeFromEditor();
            _subscribedVm = null;
        }

        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            _subscribedVm = vm;

            vm.NavigationRequested += OnNavigationRequested;
            vm.FindRequested += OnFindRequested;
            vm.ReplaceRequested += OnReplaceRequested;
            vm.ToggleCommentRequested += OnToggleCommentRequestedHandler;
            vm.DuplicateLineRequested += OnDuplicateLineRequestedHandler;
            vm.MoveLineUpRequested += OnMoveLineUpRequestedHandler;
            vm.MoveLineDownRequested += OnMoveLineDownRequestedHandler;
            vm.DeleteLineRequested += OnDeleteLineRequestedHandler;

            // Wire up selection info callback for extract method
            vm.GetSelectionInfo = () =>
            {
                var selectionInfo = MainEditor?.GetSelectionInfo();
                if (selectionInfo == null) return null;

                return new SelectionInfoDto
                {
                    StartLine = selectionInfo.StartLine,
                    StartColumn = selectionInfo.StartColumn,
                    EndLine = selectionInfo.EndLine,
                    EndColumn = selectionInfo.EndColumn,
                    SelectedText = selectionInfo.SelectedText
                };
            };

            // Initialize bookmark margin if service is available
            if (vm.BookmarkService != null && MainEditor != null)
            {
                MainEditor.InitializeBookmarks(vm.BookmarkService, vm.FilePath);
            }

            // Wire up diagnostics (error highlighting)
            vm.DiagnosticsUpdated += OnDiagnosticsUpdated;

            // Wire up code lens
            vm.CodeLensUpdated += OnCodeLensUpdated;

            // Wire up inline debug values
            vm.InlineDebugValuesUpdated += OnInlineDebugValuesUpdated;

            // Wire up execution line highlighting
            vm.ExecutionLineChanged += OnExecutionLineChanged;

            // Wire up code completion
            if (MainEditor != null)
            {
                MainEditor.CompletionRequested += OnCompletionRequested;

                // Wire up keyboard shortcut events from editor
                MainEditor.GoToDefinitionRequested += OnEditorGoToDefinition;
                MainEditor.PeekDefinitionRequested += OnEditorPeekDefinition;
                MainEditor.FindAllReferencesRequested += OnEditorFindAllReferences;
                MainEditor.RenameSymbolRequested += OnEditorRenameSymbol;
                MainEditor.CodeActionsRequested += OnEditorCodeActions;
                MainEditor.FormatDocumentRequested += OnEditorFormatDocument;
                MainEditor.CodeLensClicked += OnEditorCodeLensClicked;

                // Wire up hover/data tip events
                MainEditor.DataTipRequested += OnDataTipRequested;

                MainEditor.SignatureHelpRequested += OnEditorSignatureHelp;
                MainEditor.DocumentHighlightRequested += OnEditorDocumentHighlight;
                MainEditor.DocumentLinkClicked += OnEditorDocumentLinkClicked;

                _editorEventsWired = true;

                // Initialize breakpoints when editor is ready
                if (MainEditor.IsReady)
                {
                    InitializeBreakpointSupport(vm);
                }
                else
                {
                    MainEditor.EditorReady += OnEditorReady;
                }
            }
            vm.CompletionReceived += OnCompletionReceived;

            // Wire up hover result display
            vm.HoverResultReceived += OnHoverResultReceived;

            vm.SignatureHelpResultReceived += OnSignatureHelpResultReceived;
            vm.DocumentHighlightResultReceived += OnDocumentHighlightResultReceived;
            vm.RenameResultReceived += OnRenameResultReceived;
            vm.SelectionRangeReceived += OnSelectionRangeReceived;
        }
        }
        catch (Exception ex)
        {
            try
            {
                var crashLogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "vgs_crash.log");
                var msg = $"[{DateTime.Now:HH:mm:ss}] [DataContextChanged] {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}\n";
                System.IO.File.AppendAllText(crashLogPath, msg);
            }
            catch { }
        }
    }

    private void OnSignatureHelpResultReceived(object? sender, SignatureHelpResultEventArgs e)
    {
        if (e.Help == null || e.Help.Signatures.Count == 0)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => MainEditor?.DismissSignatureHelp());
            return;
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var sig = e.Help.Signatures[Math.Min(e.Help.ActiveSignature, e.Help.Signatures.Count - 1)];
            var activeParam = e.Help.ActiveParameter;
            var paramName = activeParam >= 0 && activeParam < sig.Parameters.Count
                ? sig.Parameters[activeParam].Label
                : null;
            var paramDoc = activeParam >= 0 && activeParam < sig.Parameters.Count
                ? sig.Parameters[activeParam].Documentation
                : null;

            MainEditor?.ShowSignatureHelp(
                sig.Label,
                paramName,
                paramDoc ?? sig.Documentation,
                activeParam,
                e.Help.Signatures.Count);
        });
    }

    private void OnDocumentHighlightResultReceived(object? sender, DocumentHighlightResultEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (e.Highlights.Count == 0)
            {
                MainEditor?.ClearDocumentHighlights();
                return;
            }

            MainEditor?.ShowDocumentHighlights(
                e.Highlights.Select(h => (h.StartLine, h.StartColumn, h.EndLine, h.EndColumn, h.IsWrite)));
        });
    }

    private void OnRenameResultReceived(object? sender, RenameResultEventArgs e)
    {
        // Rename results are handled by MainWindowViewModel which applies workspace edits
    }

    private void OnSelectionRangeReceived(object? sender, SelectionRangeResultEventArgs e)
    {
        if (e.Range == null) return;

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            MainEditor?.SetSelection(e.Range.StartLine, e.Range.StartColumn, e.Range.EndLine, e.Range.EndColumn);
        });
    }

    private void OnDiagnosticsUpdated(object? sender, IEnumerable<DiagnosticItem> diagnostics)
    {
        MainEditor?.UpdateDiagnostics(diagnostics);
    }

    private void OnCodeLensUpdated(object? sender, IEnumerable<CodeLensItemInfo> lenses)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var lensList = lenses.ToList();
            if (lensList.Count == 0)
            {
                MainEditor?.ClearCodeLenses();
            }
            else
            {
                MainEditor?.ShowCodeLenses(
                    lensList.Select(l => new VisualGameStudio.Editor.TextMarkers.CodeLensItem
                    {
                        Line = l.Line,
                        Title = l.Title,
                        CommandName = l.CommandName,
                        CommandArguments = l.CommandArguments
                    }));
            }
        });
    }

    private void OnInlineDebugValuesUpdated(object? sender, IEnumerable<InlineDebugValueInfo> values)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var valueList = values.ToList();
            if (valueList.Count == 0)
            {
                MainEditor?.ClearInlineDebugValues();
            }
            else
            {
                MainEditor?.ShowInlineDebugValues(
                    valueList.Select(v => new VisualGameStudio.Editor.TextMarkers.InlineDebugValue
                    {
                        Line = v.Line,
                        Name = v.Name,
                        Value = v.Value
                    }));
            }
        });
    }

    private void OnExecutionLineChanged(object? sender, int? line)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            SetCurrentExecutionLine(line);
        });
    }

    private void OnCompletionRequested(object? sender, CompletionRequestEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestCompletion(e.Line, e.Column);
        }
    }

    private void OnCompletionReceived(object? sender, IEnumerable<Core.Abstractions.Services.CompletionItem> completions)
    {
        if (MainEditor == null) return;

        // Convert CompletionItem to CompletionData for AvaloniaEdit
        // Materialize the list immediately to avoid deferred execution issues
        var completionDataList = completions.Select(c => new CompletionData(
            c.Label,
            c.Detail,
            ConvertCompletionKind(c.Kind),
            c.InsertText ?? c.Label
        )).ToList();

        // Ensure we're on the UI thread
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            MainEditor?.ShowCompletion(completionDataList);
        });
    }

    private static Editor.Completion.CompletionItemKind ConvertCompletionKind(Core.Abstractions.Services.CompletionItemKind kind)
    {
        return kind switch
        {
            Core.Abstractions.Services.CompletionItemKind.Method => Editor.Completion.CompletionItemKind.Method,
            Core.Abstractions.Services.CompletionItemKind.Function => Editor.Completion.CompletionItemKind.Function,
            Core.Abstractions.Services.CompletionItemKind.Constructor => Editor.Completion.CompletionItemKind.Constructor,
            Core.Abstractions.Services.CompletionItemKind.Field => Editor.Completion.CompletionItemKind.Field,
            Core.Abstractions.Services.CompletionItemKind.Variable => Editor.Completion.CompletionItemKind.Variable,
            Core.Abstractions.Services.CompletionItemKind.Class => Editor.Completion.CompletionItemKind.Class,
            Core.Abstractions.Services.CompletionItemKind.Interface => Editor.Completion.CompletionItemKind.Interface,
            Core.Abstractions.Services.CompletionItemKind.Module => Editor.Completion.CompletionItemKind.Module,
            Core.Abstractions.Services.CompletionItemKind.Property => Editor.Completion.CompletionItemKind.Property,
            Core.Abstractions.Services.CompletionItemKind.Keyword => Editor.Completion.CompletionItemKind.Keyword,
            Core.Abstractions.Services.CompletionItemKind.Snippet => Editor.Completion.CompletionItemKind.Snippet,
            Core.Abstractions.Services.CompletionItemKind.Enum => Editor.Completion.CompletionItemKind.Enum,
            Core.Abstractions.Services.CompletionItemKind.Constant => Editor.Completion.CompletionItemKind.Constant,
            Core.Abstractions.Services.CompletionItemKind.Struct => Editor.Completion.CompletionItemKind.Struct,
            _ => Editor.Completion.CompletionItemKind.Text
        };
    }

    private HashSet<int> _breakpointLines = new();

    private void InitializeBreakpointSupport(CodeEditorDocumentViewModel vm)
    {
        if (MainEditor == null) return;

        MainEditor.InitializeBreakpoints(_breakpointLines, line =>
        {
            // Toggle breakpoint visually
            if (_breakpointLines.Contains(line))
            {
                _breakpointLines.Remove(line);
            }
            else
            {
                _breakpointLines.Add(line);
            }
            MainEditor.UpdateBreakpoints(_breakpointLines);

            // Notify the ViewModel so breakpoint is registered with debugger
            vm.OnBreakpointToggled(line);
        });
    }

    /// <summary>
    /// Updates the visual breakpoints to match the debugger's state
    /// </summary>
    public void SyncBreakpoints(IEnumerable<int> lines)
    {
        _breakpointLines = new HashSet<int>(lines);
        MainEditor?.UpdateBreakpoints(_breakpointLines);
    }

    /// <summary>
    /// Sets the current execution line (during debugging)
    /// </summary>
    public void SetCurrentExecutionLine(int? line)
    {
        MainEditor?.SetCurrentExecutionLine(line);
    }

    /// <summary>
    /// Gets the breakpoint lines
    /// </summary>
    public HashSet<int> GetBreakpointLines() => _breakpointLines;

    private void OnViewKeyDown(object? sender, KeyEventArgs e)
    {
        // Handle F3 for find next/previous even when find bar is hidden
        if (e.Key == Key.F3 && FindReplaceBar.IsVisible)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                OnFindPrevious(this, EventArgs.Empty);
            }
            else
            {
                OnFindNext(this, EventArgs.Empty);
            }
            e.Handled = true;
        }
    }

    private void OnFindRequested(object? sender, EventArgs e)
    {
        ShowFindBar(showReplace: false);
    }

    private void OnReplaceRequested(object? sender, EventArgs e)
    {
        ShowFindBar(showReplace: true);
    }

    public void ShowFindBar(bool showReplace = false)
    {
        // Use the inline find/replace overlay embedded in the editor control
        if (MainEditor != null)
        {
            if (showReplace)
                MainEditor.ShowInlineFindReplace();
            else
                MainEditor.ShowInlineFind();
        }
    }

    public void HideFindBar()
    {
        MainEditor?.HideInlineFind();
    }

    private void OnDataTipRequested(object? sender, DataTipRequestEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            // Request debug data tip evaluation (works when debugging and paused)
            vm.RequestDataTipEvaluation(e.Expression, e.ScreenX, e.ScreenY);

            // Also request LSP hover info (works when LSP is connected)
            if (e.Line > 0 && e.Column > 0)
            {
                vm.RequestHover(e.Line, e.Column);
            }
        }
    }

    private void OnHoverResultReceived(object? sender, HoverResultEventArgs e)
    {
        if (e.Hover == null || string.IsNullOrWhiteSpace(e.Hover.Contents)) return;

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            MainEditor?.ShowHoverTooltip(e.Hover.Contents);
        });
    }

    private void OnNavigationRequested(object? sender, NavigationRequestedEventArgs e)
    {
        MainEditor?.SetCaretPosition(e.Line, e.Column);
        MainEditor?.Focus();
    }

    private void OnTextChanged(object? sender, EventArgs e)
    {
        // Since we're using Document binding, edits go directly to vm.TextDocument
        // We just need to sync vm.Text for dirty tracking and other features
        if (sender is CodeEditorControl editor && DataContext is CodeEditorDocumentViewModel vm)
        {
            // Get text from the shared document
            var currentText = vm.TextDocument?.Text ?? "";
            vm.UpdateTextFromEditor(currentText);
        }
    }

    private System.Timers.Timer? _highlightDebounceTimer;

    private void OnCaretPositionChanged(object? sender, EventArgs e)
    {
        if (sender is CodeEditorControl editor && DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.UpdateCaretPosition(editor.CaretLine, editor.CaretColumn);
            UpdateBreadcrumb(editor.CaretLine);

            // Debounce document highlight requests (250ms delay like VS Code)
            if (_highlightDebounceTimer == null)
            {
                _highlightDebounceTimer = new System.Timers.Timer(250);
                _highlightDebounceTimer.AutoReset = false;
                _highlightDebounceTimer.Elapsed += (_, _) =>
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        if (_subscribedVm != null && MainEditor != null)
                            _subscribedVm.RequestDocumentHighlight(MainEditor.CaretLine, MainEditor.CaretColumn);
                    });
                };
            }
            _highlightDebounceTimer.Stop();
            _highlightDebounceTimer.Start();
        }
    }

    private void UpdateBreadcrumb(int line)
    {
        if (DataContext is not CodeEditorDocumentViewModel vm || BreadcrumbBar == null) return;

        var text = vm.Text;
        if (string.IsNullOrEmpty(text)) return;

        var breadcrumbItems = new List<(string Name, int Line, string Kind)>();
        var lines = text.Split('\n');

        // Simple breadcrumb parser - track containing structures
        string? currentModule = null;
        int moduleStartLine = 0;
        string? currentClass = null;
        int classStartLine = 0;
        string? currentMethod = null;
        int methodStartLine = 0;

        for (int i = 0; i < Math.Min(line, lines.Length); i++)
        {
            var lineText = lines[i].Trim();
            var lineNum = i + 1;

            if (lineText.StartsWith("Module ", StringComparison.OrdinalIgnoreCase))
            {
                currentModule = ExtractName(lineText, "Module ");
                moduleStartLine = lineNum;
                currentClass = null;
                currentMethod = null;
            }
            else if (lineText.StartsWith("End Module", StringComparison.OrdinalIgnoreCase))
            {
                if (line <= lineNum) break;
                currentModule = null;
            }
            else if (lineText.StartsWith("Class ", StringComparison.OrdinalIgnoreCase) ||
                     lineText.Contains(" Class ", StringComparison.OrdinalIgnoreCase))
            {
                currentClass = ExtractName(lineText, "Class ");
                classStartLine = lineNum;
                currentMethod = null;
            }
            else if (lineText.StartsWith("End Class", StringComparison.OrdinalIgnoreCase))
            {
                if (line <= lineNum) break;
                currentClass = null;
            }
            else if (lineText.StartsWith("Sub ", StringComparison.OrdinalIgnoreCase) ||
                     lineText.Contains(" Sub ", StringComparison.OrdinalIgnoreCase))
            {
                currentMethod = ExtractMethodName(lineText, "Sub ");
                methodStartLine = lineNum;
            }
            else if (lineText.StartsWith("Function ", StringComparison.OrdinalIgnoreCase) ||
                     lineText.Contains(" Function ", StringComparison.OrdinalIgnoreCase))
            {
                currentMethod = ExtractMethodName(lineText, "Function ");
                methodStartLine = lineNum;
            }
            else if (lineText.StartsWith("End Sub", StringComparison.OrdinalIgnoreCase) ||
                     lineText.StartsWith("End Function", StringComparison.OrdinalIgnoreCase))
            {
                if (line <= lineNum) break;
                currentMethod = null;
            }
        }

        // Build breadcrumb path
        if (!string.IsNullOrEmpty(currentModule))
            breadcrumbItems.Add((currentModule, moduleStartLine, "Module"));
        if (!string.IsNullOrEmpty(currentClass))
            breadcrumbItems.Add((currentClass, classStartLine, "Class"));
        if (!string.IsNullOrEmpty(currentMethod))
            breadcrumbItems.Add((currentMethod, methodStartLine, "Function"));

        BreadcrumbBar.UpdateBreadcrumb(breadcrumbItems);
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

    private void OnBreadcrumbItemClicked(object? sender, BreadcrumbItem item)
    {
        MainEditor?.GoToLine(item.Line);
    }

    private void OnCut(object? sender, RoutedEventArgs e)
    {
        MainEditor?.Cut();
    }

    private void OnCopy(object? sender, RoutedEventArgs e)
    {
        MainEditor?.Copy();
    }

    private void OnPaste(object? sender, RoutedEventArgs e)
    {
        MainEditor?.Paste();
    }

    private void OnAddToWatch(object? sender, RoutedEventArgs e)
    {
        MainEditor?.RequestAddToWatch();
    }

    private void OnAddToWatchRequested(object? sender, string expression)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestAddToWatch(expression);
        }
    }

    private void OnToggleComment(object? sender, RoutedEventArgs e)
    {
        MainEditor?.ToggleLineComment();
    }

    private void OnGoToDefinition(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestGoToDefinition();
        }
    }

    private void OnFindAllReferences(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestFindAllReferences();
        }
    }

    private void OnRenameSymbol(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestRenameSymbol();
        }
    }

    private void OnExtractMethod(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestExtractMethod();
        }
    }

    private void OnInlineMethod(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestInlineMethod();
        }
    }

    private void OnIntroduceVariable(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestIntroduceVariable();
        }
    }

    private void OnExtractConstant(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestExtractConstant();
        }
    }

    private void OnInlineConstant(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestInlineConstant();
        }
    }

    private void OnInlineVariable(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestInlineVariable();
        }
    }

    private void OnChangeSignature(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestChangeSignature();
        }
    }

    private void OnEncapsulateField(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestEncapsulateField();
        }
    }

    private void OnInlineField(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestInlineField();
        }
    }

    private void OnMoveTypeToFile(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestMoveTypeToFile();
        }
    }

    private void OnExtractInterface(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestExtractInterface();
        }
    }

    private void OnGenerateConstructor(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestGenerateConstructor();
        }
    }

    private void OnImplementInterface(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestImplementInterface();
        }
    }

    private void OnOverrideMethod(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestOverrideMethod();
        }
    }

    private void OnAddParameter(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestAddParameter();
        }
    }

    private void OnRemoveParameter(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestRemoveParameter();
        }
    }

    private void OnReorderParameters(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestReorderParameters();
        }
    }

    private void OnRenameParameter(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestRenameParameter();
        }
    }

    private void OnChangeParameterType(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestChangeParameterType();
        }
    }

    private void OnMakeParameterOptional(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestMakeParameterOptional();
        }
    }

    private void OnMakeParameterRequired(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestMakeParameterRequired();
        }
    }

    private void OnConvertToNamedArguments(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestConvertToNamedArguments();
        }
    }

    private void OnConvertToPositionalArguments(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestConvertToPositionalArguments();
        }
    }

    private void OnSafeDelete(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestSafeDelete();
        }
    }

    private void OnPullMembersUp(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestPullMembersUp();
        }
    }

    private void OnPushMembersDown(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestPushMembersDown();
        }
    }

    private void OnUseBaseType(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestUseBaseType();
        }
    }

    private void OnConvertToInterface(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestConvertToInterface();
        }
    }

    private void OnInvertIf(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestInvertIf();
        }
    }

    private void OnConvertToSelectCase(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestConvertToSelectCase();
        }
    }

    private void OnSplitDeclaration(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestSplitDeclaration();
        }
    }

    private void OnIntroduceField(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestIntroduceField();
        }
    }

    private void OnSurroundWith(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestSurroundWith();
        }
    }

    private void OnPeekDefinition(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestPeekDefinition();
        }
    }

    #region Find/Replace

    private void OnFindNext(object? sender, EventArgs e)
    {
        if (MainEditor == null || string.IsNullOrEmpty(FindReplaceBar.SearchText))
            return;

        var found = MainEditor.Find(
            FindReplaceBar.SearchText,
            FindReplaceBar.MatchCase,
            FindReplaceBar.WholeWord,
            FindReplaceBar.UseRegex);

        if (found)
        {
            _currentMatchIndex++;
            if (_currentMatchIndex > _totalMatches)
                _currentMatchIndex = 1;
        }

        UpdateMatchCount();
    }

    private void OnFindPrevious(object? sender, EventArgs e)
    {
        if (MainEditor == null || string.IsNullOrEmpty(FindReplaceBar.SearchText))
            return;

        // For find previous, we need to search backwards
        // The editor's Find method searches forward, so we implement backward search here
        var found = FindPreviousMatch();

        if (found)
        {
            _currentMatchIndex--;
            if (_currentMatchIndex < 1)
                _currentMatchIndex = _totalMatches;
        }

        UpdateMatchCount();
    }

    private bool FindPreviousMatch()
    {
        if (MainEditor == null) return false;

        var text = MainEditor.Text;
        var searchText = FindReplaceBar.SearchText;
        var comparison = FindReplaceBar.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        // Start searching from before the current selection
        var startOffset = MainEditor.CaretOffset - 1;
        if (startOffset < 0) startOffset = text.Length - 1;

        if (FindReplaceBar.UseRegex)
        {
            try
            {
                var options = FindReplaceBar.MatchCase
                    ? System.Text.RegularExpressions.RegexOptions.None
                    : System.Text.RegularExpressions.RegexOptions.IgnoreCase;
                var regex = new System.Text.RegularExpressions.Regex(searchText, options);

                // Find all matches and get the one before current position
                var matches = regex.Matches(text);
                System.Text.RegularExpressions.Match? lastMatch = null;

                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    if (match.Index < startOffset)
                        lastMatch = match;
                    else
                        break;
                }

                // If no match found before, wrap to end
                if (lastMatch == null && matches.Count > 0)
                    lastMatch = matches[matches.Count - 1];

                if (lastMatch != null)
                {
                    SelectInEditor(lastMatch.Index, lastMatch.Length);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
        else
        {
            var foundIndex = text.LastIndexOf(searchText, startOffset, comparison);
            if (foundIndex < 0)
            {
                // Wrap to end
                foundIndex = text.LastIndexOf(searchText, comparison);
            }

            if (foundIndex >= 0)
            {
                if (FindReplaceBar.WholeWord && !IsWholeWord(text, foundIndex, searchText.Length))
                {
                    // Continue searching backwards
                    var searchStart = foundIndex - 1;
                    while (searchStart >= 0)
                    {
                        foundIndex = text.LastIndexOf(searchText, searchStart, comparison);
                        if (foundIndex < 0) break;
                        if (IsWholeWord(text, foundIndex, searchText.Length))
                        {
                            SelectInEditor(foundIndex, searchText.Length);
                            return true;
                        }
                        searchStart = foundIndex - 1;
                    }
                    return false;
                }

                SelectInEditor(foundIndex, searchText.Length);
                return true;
            }
        }

        return false;
    }

    private void SelectInEditor(int offset, int length)
    {
        if (MainEditor == null) return;

        MainEditor.SetSelection(offset, length);
    }

    private bool IsWholeWord(string text, int offset, int length)
    {
        var start = offset > 0 && char.IsLetterOrDigit(text[offset - 1]);
        var end = offset + length < text.Length && char.IsLetterOrDigit(text[offset + length]);
        return !start && !end;
    }

    private void OnReplace(object? sender, EventArgs e)
    {
        if (MainEditor == null || string.IsNullOrEmpty(FindReplaceBar.SearchText))
            return;

        MainEditor.Replace(
            FindReplaceBar.SearchText,
            FindReplaceBar.ReplaceText ?? "",
            FindReplaceBar.MatchCase,
            FindReplaceBar.WholeWord);

        UpdateMatchCount();
    }

    private void OnReplaceAll(object? sender, EventArgs e)
    {
        if (MainEditor == null || string.IsNullOrEmpty(FindReplaceBar.SearchText))
            return;

        var count = MainEditor.ReplaceAll(
            FindReplaceBar.SearchText,
            FindReplaceBar.ReplaceText ?? "",
            FindReplaceBar.MatchCase,
            FindReplaceBar.WholeWord,
            FindReplaceBar.UseRegex);

        FindReplaceBar.MatchCountText = $"{count} replaced";
        _totalMatches = 0;
        _currentMatchIndex = 0;
    }

    private void OnCloseFindReplace(object? sender, EventArgs e)
    {
        HideFindBar();
    }

    private void OnSearchTextChanged(object? sender, EventArgs e)
    {
        UpdateMatchCount();

        // Auto-find as you type (highlight first match)
        if (!string.IsNullOrEmpty(FindReplaceBar.SearchText) && MainEditor != null)
        {
            MainEditor.Find(
                FindReplaceBar.SearchText,
                FindReplaceBar.MatchCase,
                FindReplaceBar.WholeWord,
                FindReplaceBar.UseRegex);
        }
    }

    private void UpdateMatchCount()
    {
        if (MainEditor == null || string.IsNullOrEmpty(FindReplaceBar.SearchText))
        {
            FindReplaceBar.MatchCountText = "";
            _totalMatches = 0;
            _currentMatchIndex = 0;
            return;
        }

        var text = MainEditor.Text;
        var searchText = FindReplaceBar.SearchText;

        try
        {
            if (FindReplaceBar.UseRegex)
            {
                var options = FindReplaceBar.MatchCase
                    ? System.Text.RegularExpressions.RegexOptions.None
                    : System.Text.RegularExpressions.RegexOptions.IgnoreCase;
                var regex = new System.Text.RegularExpressions.Regex(searchText, options);
                _totalMatches = regex.Matches(text).Count;
            }
            else
            {
                var comparison = FindReplaceBar.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                var count = 0;
                var index = 0;

                while ((index = text.IndexOf(searchText, index, comparison)) != -1)
                {
                    if (!FindReplaceBar.WholeWord || IsWholeWord(text, index, searchText.Length))
                    {
                        count++;
                    }
                    index += searchText.Length;
                }

                _totalMatches = count;
            }

            if (_totalMatches == 0)
            {
                FindReplaceBar.MatchCountText = "No results";
                _currentMatchIndex = 0;
            }
            else if (_currentMatchIndex == 0)
            {
                _currentMatchIndex = 1;
                FindReplaceBar.MatchCountText = $"1 of {_totalMatches}";
            }
            else
            {
                FindReplaceBar.MatchCountText = $"{_currentMatchIndex} of {_totalMatches}";
            }
        }
        catch
        {
            // Invalid regex
            FindReplaceBar.MatchCountText = "Invalid pattern";
            _totalMatches = 0;
        }
    }

    #endregion
}
