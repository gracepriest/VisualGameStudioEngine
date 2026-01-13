using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace BasicLang.Compiler.LSP
{
    /// <summary>
    /// Simple LSP server implementation without OmniSharp dependencies
    /// </summary>
    public class SimpleLspServer
    {
        private readonly Stream _input;
        private readonly Stream _output;
        private readonly DocumentManager _documentManager;
        private readonly CompletionService _completionService;
        private bool _initialized;
        private bool _shutdown;

        public SimpleLspServer(Stream input, Stream output)
        {
            _input = input;
            _output = output;
            _documentManager = new DocumentManager();
            _completionService = new CompletionService();
        }

        public async Task RunAsync()
        {
            try
            {
                while (!_shutdown)
                {
                    var message = await ReadMessageAsync();
                    if (message == null) break;

                    await ProcessMessageAsync(message);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"LSP Error: {ex.Message}");
            }
        }

        private async Task<JsonNode?> ReadMessageAsync()
        {
            var reader = new StreamReader(_input, Encoding.UTF8, leaveOpen: true);

            // Read headers
            int contentLength = 0;
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (string.IsNullOrEmpty(line)) break;

                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                {
                    contentLength = int.Parse(line.Substring(15).Trim());
                }
            }

            if (contentLength == 0) return null;

            // Read content
            var buffer = new char[contentLength];
            var totalRead = 0;
            while (totalRead < contentLength)
            {
                var read = await reader.ReadAsync(buffer, totalRead, contentLength - totalRead);
                if (read == 0) return null;
                totalRead += read;
            }

            var json = new string(buffer);
            return JsonNode.Parse(json);
        }

        private async Task SendMessageAsync(JsonNode message)
        {
            var json = message.ToJsonString();
            var content = Encoding.UTF8.GetBytes(json);
            var header = $"Content-Length: {content.Length}\r\n\r\n";
            var headerBytes = Encoding.UTF8.GetBytes(header);

            await _output.WriteAsync(headerBytes);
            await _output.WriteAsync(content);
            await _output.FlushAsync();
        }

        private async Task SendResponseAsync(JsonNode? id, JsonNode? result, JsonNode? error = null)
        {
            var response = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id?.DeepClone()
            };

            if (error != null)
                response["error"] = error;
            else
                response["result"] = result;

            await SendMessageAsync(response);
        }

        private async Task SendNotificationAsync(string method, JsonNode? parms)
        {
            var notification = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = method,
                ["params"] = parms
            };
            await SendMessageAsync(notification);
        }

        private async Task ProcessMessageAsync(JsonNode message)
        {
            var method = message["method"]?.GetValue<string>();
            var id = message["id"];
            var parms = message["params"];

            if (method == null) return;

            // Log all incoming requests to stderr for debugging
            Console.Error.WriteLine($"[LSP] Received: {method}");

            try
            {
                switch (method)
                {
                    case "initialize":
                        await HandleInitializeAsync(id, parms);
                        break;
                    case "initialized":
                        _initialized = true;
                        break;
                    case "shutdown":
                        _shutdown = true;
                        await SendResponseAsync(id, JsonValue.Create<object?>(null));
                        break;
                    case "exit":
                        return;
                    case "textDocument/didOpen":
                        HandleDidOpen(parms);
                        break;
                    case "textDocument/didChange":
                        HandleDidChange(parms);
                        break;
                    case "textDocument/didClose":
                        HandleDidClose(parms);
                        break;
                    case "textDocument/completion":
                        await HandleCompletionAsync(id, parms);
                        break;
                    case "textDocument/hover":
                        await HandleHoverAsync(id, parms);
                        break;
                    default:
                        // Unknown method - return null for requests
                        if (id != null)
                        {
                            await SendResponseAsync(id, JsonValue.Create<object?>(null));
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error handling {method}: {ex.Message}");
                if (id != null)
                {
                    var error = new JsonObject
                    {
                        ["code"] = -32603,
                        ["message"] = ex.Message
                    };
                    await SendResponseAsync(id, null, error);
                }
            }
        }

        private async Task HandleInitializeAsync(JsonNode? id, JsonNode? parms)
        {
            var result = new JsonObject
            {
                ["capabilities"] = new JsonObject
                {
                    ["textDocumentSync"] = new JsonObject
                    {
                        ["openClose"] = true,
                        ["change"] = 1, // Full sync
                        ["save"] = new JsonObject { ["includeText"] = true }
                    },
                    ["completionProvider"] = new JsonObject
                    {
                        ["triggerCharacters"] = new JsonArray(".", "(", " "),
                        ["resolveProvider"] = false
                    },
                    ["hoverProvider"] = true
                },
                ["serverInfo"] = new JsonObject
                {
                    ["name"] = "BasicLang Language Server",
                    ["version"] = "1.0.0"
                }
            };

            await SendResponseAsync(id, result);
        }

        private void HandleDidOpen(JsonNode? parms)
        {
            var uri = parms?["textDocument"]?["uri"]?.GetValue<string>();
            var text = parms?["textDocument"]?["text"]?.GetValue<string>();

            if (uri != null && text != null)
            {
                _documentManager.UpdateDocument(
                    OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.Parse(uri),
                    text);
            }
        }

        private void HandleDidChange(JsonNode? parms)
        {
            var uri = parms?["textDocument"]?["uri"]?.GetValue<string>();
            var changes = parms?["contentChanges"]?.AsArray();

            if (uri != null && changes != null && changes.Count > 0)
            {
                var text = changes[0]?["text"]?.GetValue<string>();
                if (text != null)
                {
                    _documentManager.UpdateDocument(
                        OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.Parse(uri),
                        text);
                }
            }
        }

        private void HandleDidClose(JsonNode? parms)
        {
            var uri = parms?["textDocument"]?["uri"]?.GetValue<string>();
            if (uri != null)
            {
                _documentManager.CloseDocument(
                    OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.Parse(uri));
            }
        }

        private async Task HandleCompletionAsync(JsonNode? id, JsonNode? parms)
        {
            var uri = parms?["textDocument"]?["uri"]?.GetValue<string>();
            var line = parms?["position"]?["line"]?.GetValue<int>() ?? 0;
            var character = parms?["position"]?["character"]?.GetValue<int>() ?? 0;

            Console.Error.WriteLine($"[LSP] Completion request: uri={uri}, line={line}, char={character}");

            var items = new JsonArray();
            DocumentState? state = null;

            if (uri != null)
            {
                var docUri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.Parse(uri);
                state = _documentManager.GetDocument(docUri);
                Console.Error.WriteLine($"[LSP] Document state found: {state != null}");
                if (state != null)
                {
                    Console.Error.WriteLine($"[LSP] Document content length: {state.Content?.Length ?? 0}");
                    Console.Error.WriteLine($"[LSP] ParseSuccessful: {state.ParseSuccessful}, SemanticSuccessful: {state.SemanticSuccessful}");
                    Console.Error.WriteLine($"[LSP] SemanticAnalyzer: {state.SemanticAnalyzer != null}, TypeRegistry: {state.TypeRegistry != null}");
                    if (state.Diagnostics?.Count > 0)
                    {
                        Console.Error.WriteLine($"[LSP] Diagnostics: {state.Diagnostics.Count} errors");
                        foreach (var diag in state.Diagnostics.Take(3))
                        {
                            Console.Error.WriteLine($"[LSP]   - Line {diag.Line}: {diag.Message}");
                        }
                    }
                }
                else
                {
                    Console.Error.WriteLine($"[LSP] No document found for URI: {uri}");
                }
            }

            // Always get completions - even without document state, we get keywords/built-ins
            var completions = _completionService.GetCompletions(state, line, character);
            Console.Error.WriteLine($"[LSP] Completions count: {completions.Count}");

            foreach (var c in completions)
            {
                var item = new JsonObject
                {
                    ["label"] = c.Label,
                    ["kind"] = (int)c.Kind,
                    ["detail"] = c.Detail,
                    ["insertText"] = c.InsertText ?? c.Label
                };

                if (c.InsertTextFormat == OmniSharp.Extensions.LanguageServer.Protocol.Models.InsertTextFormat.Snippet)
                {
                    item["insertTextFormat"] = 2; // Snippet
                }

                items.Add(item);
            }

            var result = new JsonObject
            {
                ["isIncomplete"] = false,
                ["items"] = items
            };

            Console.Error.WriteLine($"[LSP] Returning {items.Count} completions");
            await SendResponseAsync(id, result);
        }

        private async Task HandleHoverAsync(JsonNode? id, JsonNode? parms)
        {
            var uri = parms?["textDocument"]?["uri"]?.GetValue<string>();
            var line = parms?["position"]?["line"]?.GetValue<int>() ?? 0;
            var character = parms?["position"]?["character"]?.GetValue<int>() ?? 0;

            JsonNode? result = null;

            if (uri != null)
            {
                var docUri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.Parse(uri);
                var state = _documentManager.GetDocument(docUri);

                if (state != null)
                {
                    var word = state.GetWordAtPosition(line, character);
                    if (!string.IsNullOrEmpty(word))
                    {
                        result = new JsonObject
                        {
                            ["contents"] = new JsonObject
                            {
                                ["kind"] = "plaintext",
                                ["value"] = $"Symbol: {word}"
                            }
                        };
                    }
                }
            }

            await SendResponseAsync(id, result);
        }
    }
}
