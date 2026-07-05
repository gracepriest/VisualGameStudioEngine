using System.Text.Json;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.Core.Utilities;

/// <summary>
/// Parses completion items returned by the VS Code-compatible extension host
/// into the Core completion model.
///
/// Extension items follow the vscode API shape, not LSP:
///   - label is a string OR a CompletionItemLabel object {label, detail, ...}
///   - insertText is a plain string OR a serialized vscode.SnippetString,
///     which JSON-serializes as {"value": "..."} — a SnippetString is
///     snippet-format by definition, so its tab stops must be expanded on
///     commit, never inserted literally.
///   - pass-through LSP-shaped items may carry insertTextFormat (2 = Snippet).
///
/// One malformed item must never abort the enumeration: each item is parsed
/// under its own guard so the rest of the list survives.
/// </summary>
public static class ExtensionCompletionParsing
{
    public static IReadOnlyList<CompletionItem> Parse(JsonElement json)
    {
        var completions = new List<CompletionItem>();

        JsonElement items;
        if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty("items", out items))
        {
            // CompletionList format
        }
        else if (json.ValueKind == JsonValueKind.Array)
        {
            items = json;
        }
        else
        {
            return completions;
        }

        foreach (var item in items.EnumerateArray())
        {
            try
            {
                var parsed = ParseItem(item);
                if (parsed != null)
                {
                    completions.Add(parsed);
                }
            }
            catch (Exception)
            {
                // A single bad item (unexpected shape from a third-party
                // extension) must not drop the remaining items.
            }
        }

        return completions;
    }

    private static CompletionItem? ParseItem(JsonElement item)
    {
        string? label = null;
        if (item.TryGetProperty("label", out var lbl))
        {
            if (lbl.ValueKind == JsonValueKind.String)
            {
                label = lbl.GetString();
            }
            else if (lbl.ValueKind == JsonValueKind.Object &&
                     lbl.TryGetProperty("label", out var inner) &&
                     inner.ValueKind == JsonValueKind.String)
            {
                label = inner.GetString();
            }
        }
        if (string.IsNullOrEmpty(label)) return null;

        var kind = item.TryGetProperty("kind", out var k) && k.ValueKind == JsonValueKind.Number
            ? k.GetInt32()
            : 0;
        var detail = item.TryGetProperty("detail", out var d) && d.ValueKind == JsonValueKind.String
            ? d.GetString()
            : null;

        var insertText = label;
        var insertTextFormat = InsertTextFormat.PlainText;
        if (item.TryGetProperty("insertText", out var it))
        {
            if (it.ValueKind == JsonValueKind.String)
            {
                insertText = it.GetString();
            }
            else if (it.ValueKind == JsonValueKind.Object &&
                     it.TryGetProperty("value", out var snippetValue) &&
                     snippetValue.ValueKind == JsonValueKind.String)
            {
                // vscode.SnippetString serialized as {"value": "..."}
                insertText = snippetValue.GetString();
                insertTextFormat = InsertTextFormat.Snippet;
            }
        }

        // LSP-shaped pass-through items: insertTextFormat 2 == Snippet
        if (item.TryGetProperty("insertTextFormat", out var itf) &&
            itf.ValueKind == JsonValueKind.Number &&
            itf.GetInt32() == 2)
        {
            insertTextFormat = InsertTextFormat.Snippet;
        }

        return new CompletionItem
        {
            Label = label!,
            InsertText = insertText ?? label!,
            Detail = detail,
            Kind = MapCompletionKind(kind),
            InsertTextFormat = insertTextFormat,
        };
    }

    /// <summary>
    /// Maps the vscode API's CompletionItemKind numbering (0-based, Text = 0)
    /// to the Core (LSP-numbered) kinds.
    /// </summary>
    public static CompletionItemKind MapCompletionKind(int kind) => kind switch
    {
        1 => CompletionItemKind.Method,
        2 => CompletionItemKind.Function,
        3 => CompletionItemKind.Constructor,
        4 => CompletionItemKind.Field,
        5 => CompletionItemKind.Variable,
        6 => CompletionItemKind.Class,
        7 => CompletionItemKind.Interface,
        8 => CompletionItemKind.Module,
        9 => CompletionItemKind.Property,
        10 => CompletionItemKind.Unit,
        11 => CompletionItemKind.Value,
        12 => CompletionItemKind.Enum,
        13 => CompletionItemKind.Keyword,
        14 => CompletionItemKind.Snippet,
        15 => CompletionItemKind.Color,
        16 => CompletionItemKind.File,
        17 => CompletionItemKind.Reference,
        18 => CompletionItemKind.Folder,
        19 => CompletionItemKind.EnumMember,
        20 => CompletionItemKind.Constant,
        21 => CompletionItemKind.Struct,
        22 => CompletionItemKind.Event,
        23 => CompletionItemKind.Operator,
        24 => CompletionItemKind.TypeParameter,
        _ => CompletionItemKind.Text,
    };
}
