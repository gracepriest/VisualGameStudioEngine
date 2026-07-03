using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using BasicLang.Compiler.SemanticAnalysis;

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
        private readonly WorkspaceManager _workspaceManager;
        private bool _initialized;
        private bool _shutdownRequested;
        private bool _exit;

        // Serializes writes to the output stream: debounced diagnostics are
        // published from background tasks and must not interleave with responses.
        private readonly SemaphoreSlim _outputLock = new SemaphoreSlim(1, 1);

        // Per-document debounce state for rapid didChange notifications
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _pendingDiagnostics
            = new ConcurrentDictionary<string, CancellationTokenSource>();
        private const int DiagnosticsDebounceMs = 250;

        public SimpleLspServer(Stream input, Stream output)
        {
            _input = input;
            _output = output;
            _documentManager = new DocumentManager();
            _completionService = new CompletionService();
            _workspaceManager = new WorkspaceManager();
        }

        public async Task RunAsync()
        {
            try
            {
                while (!_exit)
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
            // Read headers as ASCII lines directly from the raw stream.
            // A StreamReader must NOT be used here: it read-ahead buffers bytes
            // belonging to subsequent messages, which would be discarded when the
            // reader goes away (dropping batched messages like didOpen+didChange).
            int contentLength = 0;
            string? line;
            while ((line = await ReadHeaderLineAsync()) != null)
            {
                if (line.Length == 0) break;

                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                {
                    contentLength = int.Parse(line.Substring(15).Trim());
                }
            }

            if (line == null || contentLength == 0) return null;

            // Content-Length is a BYTE count per the LSP spec: read exactly that
            // many bytes, then UTF-8 decode (non-ASCII text is multi-byte).
            var buffer = new byte[contentLength];
            var totalRead = 0;
            while (totalRead < contentLength)
            {
                var read = await _input.ReadAsync(buffer, totalRead, contentLength - totalRead);
                if (read == 0) return null;
                totalRead += read;
            }

            var json = Encoding.UTF8.GetString(buffer);
            return JsonNode.Parse(json);
        }

        private async Task<string?> ReadHeaderLineAsync()
        {
            var sb = new StringBuilder();
            var single = new byte[1];
            while (true)
            {
                var read = await _input.ReadAsync(single, 0, 1);
                if (read == 0)
                {
                    // EOF: return partial line if any, otherwise signal end of stream
                    return sb.Length > 0 ? sb.ToString() : null;
                }

                var ch = (char)single[0];
                if (ch == '\n')
                {
                    if (sb.Length > 0 && sb[sb.Length - 1] == '\r')
                    {
                        sb.Length--;
                    }
                    return sb.ToString();
                }

                sb.Append(ch);
            }
        }

        private async Task SendMessageAsync(JsonNode message)
        {
            var json = message.ToJsonString();
            var content = Encoding.UTF8.GetBytes(json);
            var header = $"Content-Length: {content.Length}\r\n\r\n";
            var headerBytes = Encoding.UTF8.GetBytes(header);

            await _outputLock.WaitAsync();
            try
            {
                await _output.WriteAsync(headerBytes);
                await _output.WriteAsync(content);
                await _output.FlushAsync();
            }
            finally
            {
                _outputLock.Release();
            }
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

            // After shutdown, only the exit notification is valid; requests get InvalidRequest
            if (_shutdownRequested && method != "exit")
            {
                if (id != null)
                {
                    var invalidRequest = new JsonObject
                    {
                        ["code"] = -32600,
                        ["message"] = "Server is shutting down"
                    };
                    await SendResponseAsync(id, null, invalidRequest);
                }
                return;
            }

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
                        // Per the LSP spec, shutdown only prepares for exit; the
                        // server must keep running until the exit notification.
                        _shutdownRequested = true;
                        await SendResponseAsync(id, JsonValue.Create<object?>(null));
                        break;
                    case "exit":
                        _exit = true;
                        return;
                    case "textDocument/didOpen":
                        await HandleDidOpenAsync(parms);
                        break;
                    case "textDocument/didChange":
                        HandleDidChange(parms);
                        break;
                    case "textDocument/didClose":
                        await HandleDidCloseAsync(parms);
                        break;
                    case "workspace/didChangeWorkspaceFolders":
                        await HandleDidChangeWorkspaceFoldersAsync(parms);
                        break;
                    case "textDocument/completion":
                        await HandleCompletionAsync(id, parms);
                        break;
                    case "textDocument/hover":
                        await HandleHoverAsync(id, parms);
                        break;
                    case "textDocument/definition":
                        await HandleDefinitionAsync(id, parms);
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
                    ["hoverProvider"] = true,
                    ["definitionProvider"] = true,
                    ["workspace"] = new JsonObject
                    {
                        ["workspaceFolders"] = new JsonObject
                        {
                            ["supported"] = true,
                            ["changeNotifications"] = true
                        }
                    }
                },
                ["serverInfo"] = new JsonObject
                {
                    ["name"] = "BasicLang Language Server",
                    ["version"] = "1.0.0"
                }
            };

            await SendResponseAsync(id, result);
        }

        private async Task HandleDidOpenAsync(JsonNode? parms)
        {
            var uri = parms?["textDocument"]?["uri"]?.GetValue<string>();
            var text = parms?["textDocument"]?["text"]?.GetValue<string>();

            if (uri != null && text != null)
            {
                var state = _documentManager.UpdateDocument(
                    OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.Parse(uri),
                    text);

                // Publish immediately on open (no debounce)
                await PublishDiagnosticsAsync(uri, state);
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
                    var state = _documentManager.UpdateDocument(
                        OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.Parse(uri),
                        text);

                    // Debounce diagnostics for rapid successive edits: only the
                    // last change within the debounce window gets published.
                    var cts = new CancellationTokenSource();
                    _pendingDiagnostics.AddOrUpdate(uri, cts, (_, old) =>
                    {
                        old.Cancel();
                        old.Dispose();
                        return cts;
                    });

                    var token = cts.Token;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(DiagnosticsDebounceMs, token);
                            if (!token.IsCancellationRequested)
                            {
                                await PublishDiagnosticsAsync(uri, state);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            // Superseded by a newer change - skip
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"[LSP] Failed to publish diagnostics: {ex.Message}");
                        }
                    });
                }
            }
        }

        private async Task HandleDidCloseAsync(JsonNode? parms)
        {
            var uri = parms?["textDocument"]?["uri"]?.GetValue<string>();
            if (uri != null)
            {
                if (_pendingDiagnostics.TryRemove(uri, out var pending))
                {
                    pending.Cancel();
                    pending.Dispose();
                }

                _documentManager.CloseDocument(
                    OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.Parse(uri));

                // Clear any squiggles for the closed document
                await SendNotificationAsync("textDocument/publishDiagnostics", new JsonObject
                {
                    ["uri"] = uri,
                    ["diagnostics"] = new JsonArray()
                });
            }
        }

        /// <summary>
        /// Convert the document's compiler diagnostics (1-based lines/columns)
        /// to LSP diagnostics (0-based) and publish them. An empty array is
        /// published when the document has no diagnostics so previously shown
        /// squiggles are cleared once the code becomes clean.
        /// </summary>
        private async Task PublishDiagnosticsAsync(string uri, DocumentState? state)
        {
            var diagnostics = new JsonArray();

            if (state?.Diagnostics != null)
            {
                foreach (var diag in state.Diagnostics)
                {
                    // Compiler positions are 1-based; LSP is 0-based
                    var startLine = Math.Max(0, diag.Line - 1);
                    var startChar = Math.Max(0, diag.Column - 1);
                    var endLine = diag.EndLine > 0 ? Math.Max(startLine, diag.EndLine - 1) : startLine;
                    var endChar = diag.EndColumn > 0 ? Math.Max(0, diag.EndColumn - 1) : startChar + 10;
                    if (endLine == startLine && endChar <= startChar)
                    {
                        endChar = startChar + 1;
                    }

                    var item = new JsonObject
                    {
                        ["range"] = new JsonObject
                        {
                            ["start"] = new JsonObject { ["line"] = startLine, ["character"] = startChar },
                            ["end"] = new JsonObject { ["line"] = endLine, ["character"] = endChar }
                        },
                        ["severity"] = (int)diag.Severity,
                        ["source"] = "basiclang",
                        ["message"] = diag.Message
                    };

                    if (diag.Tags.Count > 0)
                    {
                        var tags = new JsonArray();
                        foreach (var tag in diag.Tags)
                        {
                            tags.Add((int)tag);
                        }
                        item["tags"] = tags;
                    }

                    diagnostics.Add(item);
                }
            }

            Console.Error.WriteLine($"[LSP] Publishing {diagnostics.Count} diagnostics for {uri}");

            await SendNotificationAsync("textDocument/publishDiagnostics", new JsonObject
            {
                ["uri"] = uri,
                ["diagnostics"] = diagnostics
            });
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
                        var symbol = ResolveSymbol(word, state, out _);
                        if (symbol != null)
                        {
                            // Known symbol: show its full signature as markdown
                            result = new JsonObject
                            {
                                ["contents"] = new JsonObject
                                {
                                    ["kind"] = "markdown",
                                    ["value"] = $"```basiclang\n{FormatSymbolSignature(symbol)}\n```"
                                }
                            };
                        }
                        else
                        {
                            // Unknown symbol: fall back to the plain word echo
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
            }

            await SendResponseAsync(id, result);
        }

        private async Task HandleDefinitionAsync(JsonNode? id, JsonNode? parms)
        {
            var uri = parms?["textDocument"]?["uri"]?.GetValue<string>();
            var line = parms?["position"]?["line"]?.GetValue<int>() ?? 0;
            var character = parms?["position"]?["character"]?.GetValue<int>() ?? 0;

            JsonNode? result = null;

            if (uri != null)
            {
                var docUri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.Parse(uri);
                var state = _documentManager.GetDocument(docUri);

                var word = state?.GetWordAtPosition(line, character);
                if (!string.IsNullOrEmpty(word))
                {
                    var symbol = ResolveSymbol(word, state, out var definingState);
                    if (symbol != null && symbol.Line > 0)
                    {
                        // Compiler positions are 1-based; LSP is 0-based
                        var defLine = symbol.Line - 1;
                        var defChar = Math.Max(0, symbol.Column - 1);

                        // Symbol.Column often points at the start of the declaration
                        // statement (e.g. the 'Function' keyword); snap the range to
                        // the identifier token on the declaration line when possible.
                        var identToken = (definingState ?? state)?.Tokens?.FirstOrDefault(t =>
                            t.Line == symbol.Line &&
                            t.Type == TokenType.Identifier &&
                            string.Equals(t.Lexeme, symbol.Name, StringComparison.OrdinalIgnoreCase));
                        if (identToken != null)
                        {
                            defChar = Math.Max(0, identToken.Column - 1);
                        }

                        result = new JsonObject
                        {
                            ["uri"] = definingState?.Uri.ToString() ?? uri,
                            ["range"] = new JsonObject
                            {
                                ["start"] = new JsonObject { ["line"] = defLine, ["character"] = defChar },
                                ["end"] = new JsonObject { ["line"] = defLine, ["character"] = defChar + symbol.Name.Length }
                            }
                        };
                    }
                }
            }

            await SendResponseAsync(id, result);
        }

        /// <summary>
        /// Resolve a symbol by name: first in the given document's symbol table,
        /// then across all other open documents (cross-file navigation).
        /// </summary>
        private Symbol? ResolveSymbol(string name, DocumentState? state, out DocumentState? definingState)
        {
            definingState = null;
            if (string.IsNullOrEmpty(name)) return null;

            if (state != null)
            {
                var symbol = FindSymbolInScope(state.SemanticAnalyzer?.GlobalScope, name);
                if (symbol != null)
                {
                    definingState = state;
                    return symbol;
                }
            }

            foreach (var other in _documentManager.GetAllDocuments())
            {
                if (state != null && other.Uri == state.Uri) continue;

                var symbol = FindSymbolInScope(other.SemanticAnalyzer?.GlobalScope, name);
                if (symbol != null)
                {
                    definingState = other;
                    return symbol;
                }
            }

            return null;
        }

        /// <summary>
        /// Breadth-first search for a named symbol so outer-scope declarations
        /// (functions, classes, module-level variables) win over shadowed locals.
        /// Names are case-insensitive per BasicLang semantics.
        /// </summary>
        private static Symbol? FindSymbolInScope(Scope? root, string name)
        {
            if (root == null) return null;

            var queue = new Queue<Scope>();
            queue.Enqueue(root);
            Symbol? fallback = null;

            while (queue.Count > 0)
            {
                var scope = queue.Dequeue();
                if (scope.Symbols.TryGetValue(name, out var symbol))
                {
                    // Prefer a symbol with a real source location; remember one
                    // without (e.g. pre-registered/imported) as a fallback.
                    if (symbol.Line > 0) return symbol;
                    fallback ??= symbol;
                }

                if (scope.Children != null)
                {
                    foreach (var child in scope.Children)
                        queue.Enqueue(child);
                }
            }

            return fallback;
        }

        /// <summary>
        /// Format a symbol's signature (kind, name, parameters, return type)
        /// in BasicLang syntax for hover display.
        /// </summary>
        private static string FormatSymbolSignature(Symbol symbol)
        {
            string FormatParams(List<Symbol> parameters) =>
                string.Join(", ", (parameters ?? new List<Symbol>()).Select(p =>
                {
                    var prefix = p.IsParamArray ? "ParamArray " : p.IsByRef ? "ByRef " : "";
                    var optional = p.IsOptional ? "Optional " : "";
                    return $"{optional}{prefix}{p.Name} As {p.Type?.ToString() ?? "Object"}";
                }));

            switch (symbol.Kind)
            {
                case SymbolKind.Function:
                    return $"Function {symbol.Name}({FormatParams(symbol.Parameters)}) As {symbol.ReturnType?.ToString() ?? symbol.Type?.ToString() ?? "Object"}";
                case SymbolKind.Subroutine:
                    return $"Sub {symbol.Name}({FormatParams(symbol.Parameters)})";
                case SymbolKind.Class:
                    return $"Class {symbol.Name}";
                case SymbolKind.Interface:
                    return $"Interface {symbol.Name}";
                case SymbolKind.Structure:
                    return $"Structure {symbol.Name}";
                case SymbolKind.Module:
                    return $"Module {symbol.Name}";
                case SymbolKind.Namespace:
                    return $"Namespace {symbol.Name}";
                case SymbolKind.Constant:
                    return $"Const {symbol.Name} As {symbol.Type?.ToString() ?? "Object"}";
                case SymbolKind.Parameter:
                    return $"(parameter) {symbol.Name} As {symbol.Type?.ToString() ?? "Object"}";
                case SymbolKind.Property:
                    return $"Property {symbol.Name} As {symbol.Type?.ToString() ?? "Object"}";
                case SymbolKind.Event:
                    return $"Event {symbol.Name}";
                default:
                    return $"Dim {symbol.Name} As {symbol.Type?.ToString() ?? "Object"}";
            }
        }

        private async Task HandleDidChangeWorkspaceFoldersAsync(JsonNode? parms)
        {
            var eventNode = parms?["event"];
            if (eventNode == null) return;

            var added = new List<string>();
            var removed = new List<string>();

            var addedArray = eventNode["added"]?.AsArray();
            if (addedArray != null)
            {
                foreach (var folder in addedArray)
                {
                    var uri = folder?["uri"]?.GetValue<string>();
                    if (uri != null)
                    {
                        // Convert URI to file path
                        var path = Uri.UnescapeDataString(new Uri(uri).LocalPath);
                        added.Add(path);
                    }
                }
            }

            var removedArray = eventNode["removed"]?.AsArray();
            if (removedArray != null)
            {
                foreach (var folder in removedArray)
                {
                    var uri = folder?["uri"]?.GetValue<string>();
                    if (uri != null)
                    {
                        var path = Uri.UnescapeDataString(new Uri(uri).LocalPath);
                        removed.Add(path);
                    }
                }
            }

            if (added.Count > 0 || removed.Count > 0)
            {
                Console.Error.WriteLine($"[LSP] Workspace folders changed: +{added.Count} -{removed.Count}");
                await _workspaceManager.DidChangeWorkspaceFoldersAsync(added, removed);
            }
        }
    }
}
