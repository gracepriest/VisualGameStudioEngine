using System.Text.Json;
using System.Text.RegularExpressions;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models.Extensions;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// Loads VS Code JSON color themes and converts them to IDE-compatible theme data.
/// Supports the full VS Code theme format including tokenColors, colors, and semanticTokenColors.
/// </summary>
public class VsCodeThemeLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>
    /// Loads a VS Code theme from a JSON file.
    /// </summary>
    /// <param name="filePath">Absolute path to the theme JSON file.</param>
    /// <param name="themeId">The theme identifier.</param>
    /// <param name="label">The display label.</param>
    /// <param name="uiTheme">The base UI theme (vs-dark, vs, hc-black).</param>
    /// <param name="extensionId">The extension that owns this theme.</param>
    /// <returns>The loaded theme, or null if loading failed.</returns>
    public async Task<LoadedTheme?> LoadThemeAsync(string filePath, string themeId, string label, string uiTheme, string extensionId)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(filePath);
            return LoadThemeFromJson(json, themeId, label, uiTheme, extensionId, Path.GetDirectoryName(filePath));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Loads a VS Code theme from JSON content.
    /// </summary>
    public LoadedTheme? LoadThemeFromJson(string json, string themeId, string label, string uiTheme, string extensionId, string? baseDir = null)
    {
        try
        {
            // Strip comments from JSON (VS Code themes often have JSONC)
            json = StripJsonComments(json);

            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

            var root = doc.RootElement;
            var theme = new LoadedTheme
            {
                Id = themeId,
                Label = label,
                Type = VsCodeColorMapping.ParseUiTheme(uiTheme),
                ExtensionId = extensionId
            };

            // Parse "include" for base theme
            if (root.TryGetProperty("include", out var includeEl) && baseDir != null)
            {
                var includePath = Path.Combine(baseDir, includeEl.GetString() ?? "");
                if (File.Exists(includePath))
                {
                    try
                    {
                        var baseJson = File.ReadAllText(includePath);
                        var baseTheme = LoadThemeFromJson(baseJson, themeId, label, uiTheme, extensionId, Path.GetDirectoryName(includePath));
                        if (baseTheme != null)
                        {
                            // Start with base theme values
                            foreach (var kvp in baseTheme.Colors)
                                theme.Colors[kvp.Key] = kvp.Value;
                            theme.TokenColors.AddRange(baseTheme.TokenColors);
                            foreach (var kvp in baseTheme.SemanticTokenColors)
                                theme.SemanticTokenColors[kvp.Key] = kvp.Value;
                        }
                    }
                    catch
                    {
                        // Ignore base theme load errors
                    }
                }
            }

            // Parse "colors" section
            if (root.TryGetProperty("colors", out var colorsEl) && colorsEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in colorsEl.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.String)
                    {
                        theme.Colors[prop.Name] = prop.Value.GetString() ?? "";
                    }
                }
            }

            // Parse "tokenColors" section
            if (root.TryGetProperty("tokenColors", out var tokenColorsEl) && tokenColorsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var ruleEl in tokenColorsEl.EnumerateArray())
                {
                    var rule = ParseTokenColorRule(ruleEl);
                    if (rule != null)
                    {
                        theme.TokenColors.Add(rule);
                    }
                }
            }

            // Parse "semanticTokenColors" section
            if (root.TryGetProperty("semanticTokenColors", out var semanticEl) && semanticEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in semanticEl.EnumerateObject())
                {
                    var style = ParseTokenStyle(prop.Value);
                    if (style != null)
                    {
                        theme.SemanticTokenColors[prop.Name] = style;
                    }
                }
            }

            // Parse "semanticHighlighting"
            if (root.TryGetProperty("semanticHighlighting", out var semHighEl))
            {
                theme.SemanticHighlighting = semHighEl.ValueKind == JsonValueKind.True;
            }

            return theme;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Converts a loaded theme to a TextMateTheme for the IDE's TextMate service.
    /// </summary>
    public TextMateTheme ConvertToTextMateTheme(LoadedTheme theme)
    {
        var tmTheme = new TextMateTheme
        {
            Name = theme.Label,
            Type = theme.Type == ThemeType.Light ? "light" : "dark"
        };

        // Convert colors
        foreach (var kvp in theme.Colors)
        {
            tmTheme.Colors[kvp.Key] = kvp.Value;
        }

        // Convert token colors
        foreach (var rule in theme.TokenColors)
        {
            var tmRule = new TokenColorRule
            {
                Name = rule.Name,
                Scope = rule.Scopes.Count == 1 ? (object)rule.Scopes[0] : rule.Scopes,
                Settings = new TokenStyle
                {
                    Foreground = rule.Style.Foreground,
                    Background = rule.Style.Background,
                    FontStyle = BuildFontStyleString(rule.Style)
                }
            };
            tmTheme.TokenColors.Add(tmRule);
        }

        // Convert semantic token colors
        if (theme.SemanticTokenColors.Count > 0)
        {
            tmTheme.SemanticTokenColors = new Dictionary<string, TokenStyle>();
            foreach (var kvp in theme.SemanticTokenColors)
            {
                tmTheme.SemanticTokenColors[kvp.Key] = new TokenStyle
                {
                    Foreground = kvp.Value.Foreground,
                    Background = kvp.Value.Background,
                    FontStyle = BuildFontStyleString(kvp.Value)
                };
            }
        }

        return tmTheme;
    }

    /// <summary>
    /// Maps a loaded theme's colors to IDE DynamicResource keys.
    /// Returns a dictionary of resource key to hex color value.
    /// </summary>
    public Dictionary<string, string> MapToResourceKeys(LoadedTheme theme)
    {
        var mapped = new Dictionary<string, string>();

        foreach (var kvp in theme.Colors)
        {
            if (VsCodeColorMapping.ColorKeyMap.TryGetValue(kvp.Key, out var resourceKey))
            {
                mapped[resourceKey] = kvp.Value;
            }
        }

        return mapped;
    }

    /// <summary>
    /// Generates AvalonEdit-compatible XML syntax highlighting definition (.xshd) from token colors.
    /// This enables VS Code theme colors to drive AvalonEdit syntax highlighting.
    /// </summary>
    public string GenerateXshdFromTokenColors(LoadedTheme theme, string languageName)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\"?>");
        sb.AppendLine("<SyntaxDefinition name=\"" + EscapeXml(languageName) + "\"");
        sb.AppendLine("    xmlns=\"http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008\">");
        sb.AppendLine();

        // Generate colors from token rules
        sb.AppendLine("  <!-- Colors generated from VS Code theme -->");

        var colorIndex = 0;
        var scopeToColor = new Dictionary<string, string>();

        foreach (var rule in theme.TokenColors)
        {
            if (rule.Style.Foreground == null) continue;

            var colorName = $"ThemeColor{colorIndex++}";
            var attributes = new List<string>();

            attributes.Add($"foreground=\"{EscapeXml(rule.Style.Foreground)}\"");
            if (rule.Style.Background != null)
                attributes.Add($"background=\"{EscapeXml(rule.Style.Background)}\"");
            if (rule.Style.Bold)
                attributes.Add("fontWeight=\"bold\"");
            if (rule.Style.Italic)
                attributes.Add("fontStyle=\"italic\"");

            sb.AppendLine($"  <Color name=\"{colorName}\" {string.Join(" ", attributes)} />");

            foreach (var scope in rule.Scopes)
            {
                scopeToColor[scope] = colorName;
            }
        }

        sb.AppendLine();
        sb.AppendLine("  <RuleSet>");

        // Map common TextMate scopes to AvalonEdit rules
        if (scopeToColor.TryGetValue("comment", out var commentColor) ||
            scopeToColor.TryGetValue("comment.line", out commentColor) ||
            scopeToColor.TryGetValue("comment.block", out commentColor))
        {
            sb.AppendLine($"    <Span color=\"{commentColor}\" begin=\"//\" />");
            sb.AppendLine($"    <Span color=\"{commentColor}\" begin=\"/\\*\" end=\"\\*/\" multiline=\"true\" />");
        }

        if (scopeToColor.TryGetValue("string", out var stringColor) ||
            scopeToColor.TryGetValue("string.quoted.double", out stringColor))
        {
            sb.AppendLine($"    <Span color=\"{stringColor}\" begin=\"&quot;\" end=\"&quot;\" />");
        }

        if (scopeToColor.TryGetValue("string.quoted.single", out var singleStringColor))
        {
            sb.AppendLine($"    <Span color=\"{singleStringColor}\" begin=\"'\" end=\"'\" />");
        }

        if (scopeToColor.TryGetValue("keyword", out var keywordColor) ||
            scopeToColor.TryGetValue("keyword.control", out keywordColor))
        {
            sb.AppendLine($"    <Keywords color=\"{keywordColor}\">");
            sb.AppendLine("      <!-- Keywords would be added per-language -->");
            sb.AppendLine("    </Keywords>");
        }

        if (scopeToColor.TryGetValue("constant.numeric", out var numberColor))
        {
            sb.AppendLine($"    <Rule color=\"{numberColor}\">\\b[0-9]+(\\.[0-9]+)?\\b</Rule>");
        }

        sb.AppendLine("  </RuleSet>");
        sb.AppendLine("</SyntaxDefinition>");

        return sb.ToString();
    }

    /// <summary>
    /// Extracts a flat scope-to-style map from token color rules.
    /// For each TextMate scope, returns the foreground color and font style.
    /// When multiple rules match a scope, the last one wins (VS Code behavior).
    /// </summary>
    public Dictionary<string, (string? foreground, bool bold, bool italic)> ExtractTokenColorMap(LoadedTheme theme)
    {
        var map = new Dictionary<string, (string? foreground, bool bold, bool italic)>();

        foreach (var rule in theme.TokenColors)
        {
            foreach (var scope in rule.Scopes)
            {
                map[scope] = (rule.Style.Foreground, rule.Style.Bold, rule.Style.Italic);
            }
        }

        return map;
    }

    /// <summary>
    /// Maps a loaded theme's VS Code color keys to the IDE's Avalonia resource keys (Ide* keys).
    /// These keys match the SolidColorBrush resources defined in AppStyles.axaml.
    /// </summary>
    public Dictionary<string, string> MapToIdeResourceKeys(LoadedTheme theme)
    {
        var mapped = new Dictionary<string, string>();

        // Map VS Code color keys to Ide* AXAML resource keys
        TryMap(theme.Colors, "editor.background", "IdeBg", mapped);
        TryMap(theme.Colors, "editor.foreground", "IdeFg", mapped);
        TryMap(theme.Colors, "sideBar.background", "IdePanelBg", mapped);
        TryMap(theme.Colors, "menu.background", "IdeMenuBg", mapped);
        TryMap(theme.Colors, "panel.border", "IdeBorder", mapped);
        TryMap(theme.Colors, "list.hoverBackground", "IdeHoverBg", mapped);
        TryMap(theme.Colors, "input.background", "IdeInputBg", mapped);
        TryMap(theme.Colors, "input.border", "IdeInputBorder", mapped);
        TryMap(theme.Colors, "editor.selectionBackground", "IdeSelectionBg", mapped);
        TryMap(theme.Colors, "sideBarSectionHeader.background", "IdeHeaderBg", mapped);
        TryMap(theme.Colors, "scrollbarSlider.background", "IdeThumbBg", mapped);
        TryMap(theme.Colors, "scrollbarSlider.hoverBackground", "IdeThumbHover", mapped);
        TryMap(theme.Colors, "editorWidget.background", "IdeContextBg", mapped);
        TryMap(theme.Colors, "editorWidget.border", "IdeContextBorder", mapped);
        TryMap(theme.Colors, "button.background", "IdeSecondaryBg", mapped);
        TryMap(theme.Colors, "button.foreground", "IdeSecondaryFg", mapped);
        TryMap(theme.Colors, "button.hoverBackground", "IdeSecondaryHover", mapped);
        TryMap(theme.Colors, "list.hoverBackground", "IdeListHover", mapped);
        TryMap(theme.Colors, "list.activeSelectionBackground", "IdeListSelected", mapped);
        TryMap(theme.Colors, "statusBar.background", "IdeFooterBg", mapped);
        TryMap(theme.Colors, "statusBar.border", "IdeFooterBorder", mapped);

        // Fallbacks: if certain keys aren't set, derive from related colors
        if (!mapped.ContainsKey("IdeMenuBg") && theme.Colors.ContainsKey("editorGroupHeader.tabsBackground"))
            mapped["IdeMenuBg"] = theme.Colors["editorGroupHeader.tabsBackground"];
        if (!mapped.ContainsKey("IdeBorder") && theme.Colors.ContainsKey("sideBar.border"))
            mapped["IdeBorder"] = theme.Colors["sideBar.border"];
        if (!mapped.ContainsKey("IdeFooterBorder") && mapped.ContainsKey("IdeBorder"))
            mapped["IdeFooterBorder"] = mapped["IdeBorder"];

        return mapped;
    }

    private static void TryMap(Dictionary<string, string> source, string vsCodeKey, string ideKey, Dictionary<string, string> dest)
    {
        if (source.TryGetValue(vsCodeKey, out var value) && !string.IsNullOrEmpty(value))
        {
            dest[ideKey] = value;
        }
    }

    /// <summary>
    /// Quickly loads just the name and type from a VS Code theme JSON file without
    /// fully parsing all token colors. Useful for populating theme menus.
    /// </summary>
    public (string name, string type)? PeekThemeInfo(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return null;

            var json = StripJsonComments(File.ReadAllText(filePath));
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

            var root = doc.RootElement;
            var name = root.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? Path.GetFileNameWithoutExtension(filePath) : Path.GetFileNameWithoutExtension(filePath);
            var type = root.TryGetProperty("type", out var typeEl) ? typeEl.GetString() ?? "dark" : "dark";
            return (name, type);
        }
        catch
        {
            return null;
        }
    }

    #region Private Helpers

    private ThemeTokenColorRule? ParseTokenColorRule(JsonElement element)
    {
        var rule = new ThemeTokenColorRule();

        // Parse name
        if (element.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
        {
            rule.Name = nameEl.GetString();
        }

        // Parse scope(s)
        if (element.TryGetProperty("scope", out var scopeEl))
        {
            if (scopeEl.ValueKind == JsonValueKind.String)
            {
                var scopeStr = scopeEl.GetString() ?? "";
                // VS Code allows comma-separated scopes in a single string
                rule.Scopes = scopeStr.Split(',')
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();
            }
            else if (scopeEl.ValueKind == JsonValueKind.Array)
            {
                rule.Scopes = scopeEl.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString() ?? "")
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();
            }
        }

        // Parse settings
        if (element.TryGetProperty("settings", out var settingsEl))
        {
            var style = ParseTokenStyle(settingsEl);
            if (style != null)
            {
                rule.Style = style;
            }
        }

        // A rule with no scopes is a default/global rule (applies as base)
        return rule;
    }

    private ThemeTokenStyle? ParseTokenStyle(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            // Shorthand: just a color string
            return new ThemeTokenStyle { Foreground = element.GetString() };
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var style = new ThemeTokenStyle();

        if (element.TryGetProperty("foreground", out var fgEl) && fgEl.ValueKind == JsonValueKind.String)
        {
            style.Foreground = fgEl.GetString();
        }

        if (element.TryGetProperty("background", out var bgEl) && bgEl.ValueKind == JsonValueKind.String)
        {
            style.Background = bgEl.GetString();
        }

        if (element.TryGetProperty("fontStyle", out var fsEl) && fsEl.ValueKind == JsonValueKind.String)
        {
            var fontStyle = fsEl.GetString() ?? "";
            style.Bold = fontStyle.Contains("bold", StringComparison.OrdinalIgnoreCase);
            style.Italic = fontStyle.Contains("italic", StringComparison.OrdinalIgnoreCase);
            style.Underline = fontStyle.Contains("underline", StringComparison.OrdinalIgnoreCase);
            style.Strikethrough = fontStyle.Contains("strikethrough", StringComparison.OrdinalIgnoreCase);
        }

        return style;
    }

    private static string BuildFontStyleString(ThemeTokenStyle style)
    {
        var parts = new List<string>();
        if (style.Bold) parts.Add("bold");
        if (style.Italic) parts.Add("italic");
        if (style.Underline) parts.Add("underline");
        if (style.Strikethrough) parts.Add("strikethrough");
        return string.Join(" ", parts);
    }

    /// <summary>
    /// Strips single-line (//) and multi-line (/* */) comments from JSON (JSONC support).
    /// VS Code theme files are JSONC which allows comments.
    /// </summary>
    private static string StripJsonComments(string json)
    {
        var result = new System.Text.StringBuilder(json.Length);
        var i = 0;
        var inString = false;
        var escape = false;

        while (i < json.Length)
        {
            var ch = json[i];

            if (escape)
            {
                result.Append(ch);
                escape = false;
                i++;
                continue;
            }

            if (inString)
            {
                if (ch == '\\')
                {
                    escape = true;
                    result.Append(ch);
                }
                else if (ch == '"')
                {
                    inString = false;
                    result.Append(ch);
                }
                else
                {
                    result.Append(ch);
                }
                i++;
                continue;
            }

            if (ch == '"')
            {
                inString = true;
                result.Append(ch);
                i++;
                continue;
            }

            // Check for line comment
            if (ch == '/' && i + 1 < json.Length && json[i + 1] == '/')
            {
                // Skip until end of line
                while (i < json.Length && json[i] != '\n')
                    i++;
                continue;
            }

            // Check for block comment
            if (ch == '/' && i + 1 < json.Length && json[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < json.Length && !(json[i] == '*' && json[i + 1] == '/'))
                    i++;
                if (i + 1 < json.Length)
                    i += 2; // Skip */
                continue;
            }

            result.Append(ch);
            i++;
        }

        return result.ToString();
    }

    private static string EscapeXml(string value)
    {
        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }

    #endregion
}
