namespace VisualGameStudio.Core.Models.Extensions;

/// <summary>
/// Represents a VS Code-compatible color theme that has been parsed and is ready to apply.
/// </summary>
public class LoadedTheme
{
    /// <summary>
    /// Gets or sets the theme identifier.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Gets or sets the display label.
    /// </summary>
    public string Label { get; set; } = "";

    /// <summary>
    /// Gets or sets the base UI theme type.
    /// </summary>
    public ThemeType Type { get; set; } = ThemeType.Dark;

    /// <summary>
    /// Gets or sets the extension ID that provided this theme.
    /// </summary>
    public string ExtensionId { get; set; } = "";

    /// <summary>
    /// Gets or sets the editor/UI colors (VS Code "colors" section).
    /// Keys are VS Code color IDs like "editor.background", "activityBar.foreground".
    /// Values are hex color strings like "#1E1E1E".
    /// </summary>
    public Dictionary<string, string> Colors { get; set; } = new();

    /// <summary>
    /// Gets or sets the token color rules (VS Code "tokenColors" section).
    /// </summary>
    public List<ThemeTokenColorRule> TokenColors { get; set; } = new();

    /// <summary>
    /// Gets or sets semantic token color rules.
    /// </summary>
    public Dictionary<string, ThemeTokenStyle> SemanticTokenColors { get; set; } = new();

    /// <summary>
    /// Gets or sets semantic highlighting enabled state.
    /// </summary>
    public bool SemanticHighlighting { get; set; }
}

/// <summary>
/// Theme type corresponding to VS Code uiTheme values.
/// </summary>
public enum ThemeType
{
    /// <summary>
    /// Light theme (VS Code "vs").
    /// </summary>
    Light,

    /// <summary>
    /// Dark theme (VS Code "vs-dark").
    /// </summary>
    Dark,

    /// <summary>
    /// High contrast dark (VS Code "hc-black").
    /// </summary>
    HighContrastDark,

    /// <summary>
    /// High contrast light (VS Code "hc-light").
    /// </summary>
    HighContrastLight
}

/// <summary>
/// A token color rule from a VS Code theme.
/// </summary>
public class ThemeTokenColorRule
{
    /// <summary>
    /// Gets or sets the rule name/comment.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the TextMate scopes this rule targets.
    /// </summary>
    public List<string> Scopes { get; set; } = new();

    /// <summary>
    /// Gets or sets the style to apply.
    /// </summary>
    public ThemeTokenStyle Style { get; set; } = new();
}

/// <summary>
/// Style settings for a token color rule.
/// </summary>
public class ThemeTokenStyle
{
    /// <summary>
    /// Gets or sets the foreground color (hex).
    /// </summary>
    public string? Foreground { get; set; }

    /// <summary>
    /// Gets or sets the background color (hex).
    /// </summary>
    public string? Background { get; set; }

    /// <summary>
    /// Gets or sets whether the text is bold.
    /// </summary>
    public bool Bold { get; set; }

    /// <summary>
    /// Gets or sets whether the text is italic.
    /// </summary>
    public bool Italic { get; set; }

    /// <summary>
    /// Gets or sets whether the text is underlined.
    /// </summary>
    public bool Underline { get; set; }

    /// <summary>
    /// Gets or sets whether the text has strikethrough.
    /// </summary>
    public bool Strikethrough { get; set; }
}

/// <summary>
/// A loaded VS Code snippet ready for use in the editor.
/// </summary>
public class LoadedSnippet
{
    /// <summary>
    /// Gets or sets the snippet name/key.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the trigger prefix(es).
    /// </summary>
    public List<string> Prefixes { get; set; } = new();

    /// <summary>
    /// Gets or sets the snippet body lines.
    /// </summary>
    public List<string> Body { get; set; } = new();

    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the language this snippet applies to.
    /// </summary>
    public string Language { get; set; } = "";

    /// <summary>
    /// Gets or sets the extension ID that provided this snippet.
    /// </summary>
    public string ExtensionId { get; set; } = "";

    /// <summary>
    /// Gets the expanded body as a single string with tab stops resolved.
    /// </summary>
    public string GetExpandedBody()
    {
        return string.Join(Environment.NewLine, Body);
    }

    /// <summary>
    /// Gets the tab stops defined in this snippet.
    /// </summary>
    public List<SnippetTabStop> GetTabStops()
    {
        var tabStops = new List<SnippetTabStop>();
        var body = GetExpandedBody();
        var index = 0;

        while (index < body.Length)
        {
            if (body[index] == '$')
            {
                if (index + 1 < body.Length && body[index + 1] == '{')
                {
                    // ${1:placeholder} or ${1|choice1,choice2|}
                    var endBrace = body.IndexOf('}', index + 2);
                    if (endBrace > 0)
                    {
                        var inner = body.Substring(index + 2, endBrace - index - 2);
                        var colonIndex = inner.IndexOf(':');
                        var pipeIndex = inner.IndexOf('|');

                        if (colonIndex > 0 && int.TryParse(inner.Substring(0, colonIndex), out var tabIndex))
                        {
                            tabStops.Add(new SnippetTabStop
                            {
                                Index = tabIndex,
                                Placeholder = inner.Substring(colonIndex + 1),
                                Offset = index
                            });
                        }
                        else if (pipeIndex > 0 && int.TryParse(inner.Substring(0, pipeIndex), out var choiceIndex))
                        {
                            var choicesStr = inner.Substring(pipeIndex + 1).TrimEnd('|');
                            tabStops.Add(new SnippetTabStop
                            {
                                Index = choiceIndex,
                                Choices = choicesStr.Split(',').ToList(),
                                Offset = index
                            });
                        }
                        index = endBrace + 1;
                        continue;
                    }
                }
                else if (index + 1 < body.Length && char.IsDigit(body[index + 1]))
                {
                    // $1, $2, etc.
                    var numStr = "";
                    var j = index + 1;
                    while (j < body.Length && char.IsDigit(body[j]))
                    {
                        numStr += body[j];
                        j++;
                    }
                    if (int.TryParse(numStr, out var tabIdx))
                    {
                        tabStops.Add(new SnippetTabStop
                        {
                            Index = tabIdx,
                            Offset = index
                        });
                    }
                    index = j;
                    continue;
                }
            }
            index++;
        }

        return tabStops.OrderBy(t => t.Index).ToList();
    }
}

/// <summary>
/// A tab stop in a snippet.
/// </summary>
public class SnippetTabStop
{
    /// <summary>
    /// Gets or sets the tab stop index (0 = final cursor position).
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Gets or sets the placeholder text.
    /// </summary>
    public string? Placeholder { get; set; }

    /// <summary>
    /// Gets or sets the choice options (for choice tab stops).
    /// </summary>
    public List<string>? Choices { get; set; }

    /// <summary>
    /// Gets or sets the offset in the expanded body.
    /// </summary>
    public int Offset { get; set; }
}

/// <summary>
/// Mapping between VS Code color keys and IDE resource keys.
/// </summary>
public static class VsCodeColorMapping
{
    /// <summary>
    /// Maps VS Code color IDs to Avalonia DynamicResource keys used in the IDE.
    /// </summary>
    public static readonly Dictionary<string, string> ColorKeyMap = new()
    {
        // Editor
        ["editor.background"] = "EditorBackground",
        ["editor.foreground"] = "EditorForeground",
        ["editor.lineHighlightBackground"] = "EditorLineHighlightBackground",
        ["editor.selectionBackground"] = "EditorSelectionBackground",
        ["editor.selectionForeground"] = "EditorSelectionForeground",
        ["editor.inactiveSelectionBackground"] = "EditorInactiveSelectionBackground",
        ["editor.findMatchBackground"] = "EditorFindMatchBackground",
        ["editor.findMatchHighlightBackground"] = "EditorFindMatchHighlightBackground",
        ["editor.wordHighlightBackground"] = "EditorWordHighlightBackground",
        ["editor.wordHighlightStrongBackground"] = "EditorWordHighlightStrongBackground",
        ["editorCursor.foreground"] = "EditorCursorForeground",
        ["editorWhitespace.foreground"] = "EditorWhitespaceForeground",
        ["editorIndentGuide.background"] = "EditorIndentGuideBackground",
        ["editorIndentGuide.activeBackground"] = "EditorIndentGuideActiveBackground",
        ["editorLineNumber.foreground"] = "EditorLineNumberForeground",
        ["editorLineNumber.activeForeground"] = "EditorLineNumberActiveForeground",
        ["editorBracketMatch.background"] = "EditorBracketMatchBackground",
        ["editorBracketMatch.border"] = "EditorBracketMatchBorder",
        ["editorOverviewRuler.border"] = "EditorOverviewRulerBorder",
        ["editorGutter.background"] = "EditorGutterBackground",
        ["editorError.foreground"] = "EditorErrorForeground",
        ["editorWarning.foreground"] = "EditorWarningForeground",
        ["editorInfo.foreground"] = "EditorInfoForeground",

        // Activity Bar
        ["activityBar.background"] = "ActivityBarBackground",
        ["activityBar.foreground"] = "ActivityBarForeground",
        ["activityBar.inactiveForeground"] = "ActivityBarInactiveForeground",
        ["activityBarBadge.background"] = "ActivityBarBadgeBackground",
        ["activityBarBadge.foreground"] = "ActivityBarBadgeForeground",

        // Side Bar
        ["sideBar.background"] = "SideBarBackground",
        ["sideBar.foreground"] = "SideBarForeground",
        ["sideBarTitle.foreground"] = "SideBarTitleForeground",
        ["sideBarSectionHeader.background"] = "SideBarSectionHeaderBackground",
        ["sideBarSectionHeader.foreground"] = "SideBarSectionHeaderForeground",

        // Status Bar
        ["statusBar.background"] = "StatusBarBackground",
        ["statusBar.foreground"] = "StatusBarForeground",
        ["statusBar.debuggingBackground"] = "StatusBarDebuggingBackground",
        ["statusBar.debuggingForeground"] = "StatusBarDebuggingForeground",
        ["statusBar.noFolderBackground"] = "StatusBarNoFolderBackground",

        // Title Bar
        ["titleBar.activeBackground"] = "TitleBarActiveBackground",
        ["titleBar.activeForeground"] = "TitleBarActiveForeground",
        ["titleBar.inactiveBackground"] = "TitleBarInactiveBackground",
        ["titleBar.inactiveForeground"] = "TitleBarInactiveForeground",

        // Tab
        ["tab.activeBackground"] = "TabActiveBackground",
        ["tab.activeForeground"] = "TabActiveForeground",
        ["tab.inactiveBackground"] = "TabInactiveBackground",
        ["tab.inactiveForeground"] = "TabInactiveForeground",
        ["tab.border"] = "TabBorder",
        ["tab.activeBorderTop"] = "TabActiveBorderTop",

        // Panel
        ["panel.background"] = "PanelBackground",
        ["panel.border"] = "PanelBorder",
        ["panelTitle.activeBorder"] = "PanelTitleActiveBorder",
        ["panelTitle.activeForeground"] = "PanelTitleActiveForeground",
        ["panelTitle.inactiveForeground"] = "PanelTitleInactiveForeground",

        // Terminal
        ["terminal.background"] = "TerminalBackground",
        ["terminal.foreground"] = "TerminalForeground",
        ["terminal.ansiBlack"] = "TerminalAnsiBlack",
        ["terminal.ansiRed"] = "TerminalAnsiRed",
        ["terminal.ansiGreen"] = "TerminalAnsiGreen",
        ["terminal.ansiYellow"] = "TerminalAnsiYellow",
        ["terminal.ansiBlue"] = "TerminalAnsiBlue",
        ["terminal.ansiMagenta"] = "TerminalAnsiMagenta",
        ["terminal.ansiCyan"] = "TerminalAnsiCyan",
        ["terminal.ansiWhite"] = "TerminalAnsiWhite",
        ["terminal.ansiBrightBlack"] = "TerminalAnsiBrightBlack",
        ["terminal.ansiBrightRed"] = "TerminalAnsiBrightRed",
        ["terminal.ansiBrightGreen"] = "TerminalAnsiBrightGreen",
        ["terminal.ansiBrightYellow"] = "TerminalAnsiBrightYellow",
        ["terminal.ansiBrightBlue"] = "TerminalAnsiBrightBlue",
        ["terminal.ansiBrightMagenta"] = "TerminalAnsiBrightMagenta",
        ["terminal.ansiBrightCyan"] = "TerminalAnsiBrightCyan",
        ["terminal.ansiBrightWhite"] = "TerminalAnsiBrightWhite",

        // Lists
        ["list.activeSelectionBackground"] = "ListActiveSelectionBackground",
        ["list.activeSelectionForeground"] = "ListActiveSelectionForeground",
        ["list.inactiveSelectionBackground"] = "ListInactiveSelectionBackground",
        ["list.hoverBackground"] = "ListHoverBackground",
        ["list.hoverForeground"] = "ListHoverForeground",
        ["list.focusBackground"] = "ListFocusBackground",

        // Input
        ["input.background"] = "InputBackground",
        ["input.foreground"] = "InputForeground",
        ["input.border"] = "InputBorder",
        ["input.placeholderForeground"] = "InputPlaceholderForeground",
        ["inputOption.activeBorder"] = "InputOptionActiveBorder",

        // Dropdown
        ["dropdown.background"] = "DropdownBackground",
        ["dropdown.foreground"] = "DropdownForeground",
        ["dropdown.border"] = "DropdownBorder",

        // Button
        ["button.background"] = "ButtonBackground",
        ["button.foreground"] = "ButtonForeground",
        ["button.hoverBackground"] = "ButtonHoverBackground",

        // Badge
        ["badge.background"] = "BadgeBackground",
        ["badge.foreground"] = "BadgeForeground",

        // Scrollbar
        ["scrollbarSlider.background"] = "ScrollbarSliderBackground",
        ["scrollbarSlider.hoverBackground"] = "ScrollbarSliderHoverBackground",
        ["scrollbarSlider.activeBackground"] = "ScrollbarSliderActiveBackground",

        // Minimap
        ["minimap.background"] = "MinimapBackground",
        ["minimap.selectionHighlight"] = "MinimapSelectionHighlight",
        ["minimap.findMatchHighlight"] = "MinimapFindMatchHighlight",

        // Git decorations
        ["gitDecoration.modifiedResourceForeground"] = "GitModifiedForeground",
        ["gitDecoration.deletedResourceForeground"] = "GitDeletedForeground",
        ["gitDecoration.untrackedResourceForeground"] = "GitUntrackedForeground",
        ["gitDecoration.conflictingResourceForeground"] = "GitConflictingForeground",
        ["gitDecoration.ignoredResourceForeground"] = "GitIgnoredForeground",

        // Diff editor
        ["diffEditor.insertedTextBackground"] = "DiffInsertedBackground",
        ["diffEditor.removedTextBackground"] = "DiffRemovedBackground",

        // Peek view
        ["peekView.border"] = "PeekViewBorder",
        ["peekViewEditor.background"] = "PeekViewEditorBackground",
        ["peekViewResult.background"] = "PeekViewResultBackground",
        ["peekViewTitle.background"] = "PeekViewTitleBackground",

        // Breadcrumb
        ["breadcrumb.foreground"] = "BreadcrumbForeground",
        ["breadcrumb.focusForeground"] = "BreadcrumbFocusForeground",
        ["breadcrumb.activeSelectionForeground"] = "BreadcrumbActiveSelectionForeground",

        // Notifications
        ["notificationCenter.border"] = "NotificationBorder",
        ["notifications.background"] = "NotificationBackground",
        ["notifications.foreground"] = "NotificationForeground",

        // Focus border
        ["focusBorder"] = "FocusBorder",

        // Foreground/background
        ["foreground"] = "Foreground",
        ["descriptionForeground"] = "DescriptionForeground",
        ["errorForeground"] = "ErrorForeground",
        ["widget.shadow"] = "WidgetShadow",
        ["selection.background"] = "SelectionBackground",
    };

    /// <summary>
    /// Converts a VS Code uiTheme string to a ThemeType enum value.
    /// </summary>
    public static ThemeType ParseUiTheme(string uiTheme)
    {
        return uiTheme.ToLowerInvariant() switch
        {
            "vs" => ThemeType.Light,
            "vs-dark" => ThemeType.Dark,
            "hc-black" => ThemeType.HighContrastDark,
            "hc-light" => ThemeType.HighContrastLight,
            _ => ThemeType.Dark
        };
    }
}
