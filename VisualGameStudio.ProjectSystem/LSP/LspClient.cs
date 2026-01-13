using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VisualGameStudio.Core.Extensions;
using VisualGameStudio.Core.LSP;

namespace VisualGameStudio.ProjectSystem.LSP;

/// <summary>
/// Generic LSP client implementation that can connect to any language server
/// </summary>
public class LspClient : ILspClient
{
    private readonly LanguageServerConfig _config;
    private Process? _serverProcess;
    private StreamWriter? _writer;
    private StreamReader? _reader;
    private Task? _readerTask;
    private CancellationTokenSource? _cts;
    private int _requestId;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement?>> _pendingRequests = new();
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _disposed;

    public bool IsConnected { get; private set; }
    public ServerCapabilities? Capabilities { get; private set; }

    public event EventHandler<LspNotificationEventArgs>? NotificationReceived;
    public event EventHandler<PublishDiagnosticsEventArgs>? DiagnosticsReceived;

    public LspClient(LanguageServerConfig config)
    {
        _config = config;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };
    }

    public async Task<bool> InitializeAsync(string workspaceRoot, CancellationToken cancellationToken = default)
    {
        try
        {
            // Start the language server process
            var startInfo = new ProcessStartInfo
            {
                FileName = _config.StartInfo.Command,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = _config.StartInfo.WorkingDirectory ?? workspaceRoot
            };

            foreach (var arg in _config.StartInfo.Arguments)
            {
                startInfo.ArgumentList.Add(arg);
            }

            if (_config.StartInfo.Environment != null)
            {
                foreach (var (key, value) in _config.StartInfo.Environment)
                {
                    startInfo.Environment[key] = value;
                }
            }

            _serverProcess = Process.Start(startInfo);
            if (_serverProcess == null)
            {
                return false;
            }

            _writer = _serverProcess.StandardInput;
            _reader = _serverProcess.StandardOutput;

            // Start reading responses
            _cts = new CancellationTokenSource();
            _readerTask = ReadMessagesAsync(_cts.Token);

            // Send initialize request
            var initParams = new
            {
                processId = Environment.ProcessId,
                rootUri = new Uri(workspaceRoot).AbsoluteUri,
                rootPath = workspaceRoot,
                capabilities = new
                {
                    textDocument = new
                    {
                        synchronization = new
                        {
                            dynamicRegistration = false,
                            willSave = false,
                            willSaveWaitUntil = false,
                            didSave = true
                        },
                        completion = new
                        {
                            dynamicRegistration = false,
                            completionItem = new
                            {
                                snippetSupport = true,
                                commitCharactersSupport = true,
                                documentationFormat = new[] { "markdown", "plaintext" }
                            }
                        },
                        hover = new
                        {
                            dynamicRegistration = false,
                            contentFormat = new[] { "markdown", "plaintext" }
                        },
                        signatureHelp = new
                        {
                            dynamicRegistration = false,
                            signatureInformation = new
                            {
                                documentationFormat = new[] { "markdown", "plaintext" }
                            }
                        },
                        definition = new { dynamicRegistration = false },
                        references = new { dynamicRegistration = false },
                        documentSymbol = new
                        {
                            dynamicRegistration = false,
                            hierarchicalDocumentSymbolSupport = true
                        },
                        codeAction = new { dynamicRegistration = false },
                        formatting = new { dynamicRegistration = false },
                        rename = new { dynamicRegistration = false, prepareSupport = true },
                        publishDiagnostics = new { relatedInformation = true }
                    },
                    workspace = new
                    {
                        workspaceFolders = true,
                        configuration = true
                    }
                },
                initializationOptions = _config.InitializationOptions,
                workspaceFolders = new[]
                {
                    new { uri = new Uri(workspaceRoot).AbsoluteUri, name = Path.GetFileName(workspaceRoot) }
                }
            };

            var result = await SendRawRequestAsync("initialize", initParams, cancellationToken);
            if (result.HasValue)
            {
                Capabilities = ParseCapabilities(result.Value);

                // Send initialized notification
                await SendNotificationAsync("initialized", new { });
                IsConnected = true;
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to initialize LSP client: {ex.Message}");
            return false;
        }
    }

    private ServerCapabilities ParseCapabilities(JsonElement element)
    {
        var caps = new ServerCapabilities();

        if (element.TryGetProperty("capabilities", out var capsProp))
        {
            element = capsProp;
        }

        if (element.TryGetProperty("hoverProvider", out var hover))
        {
            caps.HoverProvider = hover.ValueKind == JsonValueKind.True ||
                                 (hover.ValueKind == JsonValueKind.Object);
        }

        if (element.TryGetProperty("definitionProvider", out var def))
        {
            caps.DefinitionProvider = def.ValueKind == JsonValueKind.True ||
                                      (def.ValueKind == JsonValueKind.Object);
        }

        if (element.TryGetProperty("referencesProvider", out var refs))
        {
            caps.ReferencesProvider = refs.ValueKind == JsonValueKind.True ||
                                      (refs.ValueKind == JsonValueKind.Object);
        }

        if (element.TryGetProperty("documentSymbolProvider", out var docSym))
        {
            caps.DocumentSymbolProvider = docSym.ValueKind == JsonValueKind.True ||
                                          (docSym.ValueKind == JsonValueKind.Object);
        }

        if (element.TryGetProperty("codeActionProvider", out var codeAction))
        {
            caps.CodeActionProvider = codeAction.ValueKind == JsonValueKind.True ||
                                      (codeAction.ValueKind == JsonValueKind.Object);
        }

        if (element.TryGetProperty("documentFormattingProvider", out var format))
        {
            caps.DocumentFormattingProvider = format.ValueKind == JsonValueKind.True ||
                                              (format.ValueKind == JsonValueKind.Object);
        }

        if (element.TryGetProperty("completionProvider", out var completion))
        {
            caps.CompletionProvider = new CompletionOptions();
            if (completion.TryGetProperty("triggerCharacters", out var triggers))
            {
                caps.CompletionProvider.TriggerCharacters = triggers.EnumerateArray()
                    .Select(t => t.GetString() ?? "")
                    .ToList();
            }
            if (completion.TryGetProperty("resolveProvider", out var resolve))
            {
                caps.CompletionProvider.ResolveProvider = resolve.GetBoolean();
            }
        }

        if (element.TryGetProperty("signatureHelpProvider", out var sigHelp))
        {
            caps.SignatureHelpProvider = new SignatureHelpOptions();
            if (sigHelp.TryGetProperty("triggerCharacters", out var triggers))
            {
                caps.SignatureHelpProvider.TriggerCharacters = triggers.EnumerateArray()
                    .Select(t => t.GetString() ?? "")
                    .ToList();
            }
        }

        return caps;
    }

    public async Task ShutdownAsync()
    {
        if (!IsConnected) return;

        try
        {
            await SendRequestAsync<object>("shutdown", null);
            await SendNotificationAsync("exit", null);
        }
        catch
        {
            // Ignore errors during shutdown
        }

        IsConnected = false;
    }

    public async Task<TResponse?> SendRequestAsync<TResponse>(string method, object? parameters, CancellationToken cancellationToken = default)
    {
        var jsonResult = await SendRawRequestAsync(method, parameters, cancellationToken);
        if (jsonResult == null)
        {
            return default;
        }
        return JsonSerializer.Deserialize<TResponse>(jsonResult.Value.GetRawText(), _jsonOptions);
    }

    /// <summary>
    /// Send a request and get raw JSON response
    /// </summary>
    private async Task<JsonElement?> SendRawRequestAsync(string method, object? parameters, CancellationToken cancellationToken = default)
    {
        var id = Interlocked.Increment(ref _requestId);
        var tcs = new TaskCompletionSource<JsonElement?>();
        _pendingRequests[id] = tcs;

        try
        {
            var request = new
            {
                jsonrpc = "2.0",
                id,
                method,
                @params = parameters
            };

            await SendMessageAsync(request);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            using (cts.Token.Register(() => tcs.TrySetCanceled()))
            {
                return await tcs.Task;
            }
        }
        finally
        {
            _pendingRequests.TryRemove(id, out _);
        }
    }

    public async Task SendNotificationAsync(string method, object? parameters)
    {
        var notification = new
        {
            jsonrpc = "2.0",
            method,
            @params = parameters
        };

        await SendMessageAsync(notification);
    }

    private async Task SendMessageAsync(object message)
    {
        if (_writer == null) return;

        var json = JsonSerializer.Serialize(message, _jsonOptions);
        var content = $"Content-Length: {Encoding.UTF8.GetByteCount(json)}\r\n\r\n{json}";

        await _writer.WriteAsync(content);
        await _writer.FlushAsync();
    }

    private async Task ReadMessagesAsync(CancellationToken cancellationToken)
    {
        var headerBuffer = new StringBuilder();
        var contentBuffer = new byte[65536];

        while (!cancellationToken.IsCancellationRequested && _reader != null)
        {
            try
            {
                // Read headers
                headerBuffer.Clear();
                int contentLength = 0;

                while (true)
                {
                    var line = await _reader.ReadLineAsync(cancellationToken);
                    if (line == null) return;

                    if (string.IsNullOrEmpty(line))
                    {
                        break;
                    }

                    if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    {
                        contentLength = int.Parse(line.Substring(15).Trim());
                    }
                }

                if (contentLength == 0) continue;

                // Read content
                var buffer = new char[contentLength];
                var totalRead = 0;
                while (totalRead < contentLength)
                {
                    var read = await _reader.ReadAsync(buffer, totalRead, contentLength - totalRead);
                    if (read == 0) return;
                    totalRead += read;
                }

                var json = new string(buffer);
                ProcessMessage(json);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error reading LSP message: {ex.Message}");
            }
        }
    }

    private void ProcessMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("id", out var idProp))
            {
                // Response to a request
                var id = idProp.GetInt32();
                if (_pendingRequests.TryRemove(id, out var tcs))
                {
                    if (root.TryGetProperty("result", out var result))
                    {
                        tcs.SetResult(result.Clone());
                    }
                    else if (root.TryGetProperty("error", out var error))
                    {
                        Debug.WriteLine($"LSP error: {error}");
                        tcs.SetResult(null);
                    }
                    else
                    {
                        tcs.SetResult(null);
                    }
                }
            }
            else if (root.TryGetProperty("method", out var methodProp))
            {
                // Notification from server
                var method = methodProp.GetString();
                JsonElement? parameters = null;

                if (root.TryGetProperty("params", out var paramsProp))
                {
                    parameters = paramsProp.Clone();
                }

                HandleNotification(method!, parameters);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error processing LSP message: {ex.Message}");
        }
    }

    private void HandleNotification(string method, JsonElement? parameters)
    {
        if (method == "textDocument/publishDiagnostics" && parameters.HasValue)
        {
            var args = new PublishDiagnosticsEventArgs();
            var p = parameters.Value;

            if (p.TryGetProperty("uri", out var uri))
            {
                args.Uri = uri.GetString() ?? "";
            }

            if (p.TryGetProperty("version", out var version))
            {
                args.Version = version.GetInt32();
            }

            if (p.TryGetProperty("diagnostics", out var diagnostics))
            {
                var diagList = new List<Diagnostic>();
                foreach (var diag in diagnostics.EnumerateArray())
                {
                    diagList.Add(ParseDiagnostic(diag));
                }
                args.Diagnostics = diagList;
            }

            DiagnosticsReceived?.Invoke(this, args);
        }

        NotificationReceived?.Invoke(this, new LspNotificationEventArgs
        {
            Method = method,
            Parameters = parameters
        });
    }

    private Diagnostic ParseDiagnostic(JsonElement element)
    {
        var diag = new Diagnostic();

        if (element.TryGetProperty("range", out var range))
        {
            diag.Range = ParseRange(range);
        }

        if (element.TryGetProperty("severity", out var severity))
        {
            diag.Severity = (DiagnosticSeverity)severity.GetInt32();
        }

        if (element.TryGetProperty("code", out var code))
        {
            diag.Code = code.ToString();
        }

        if (element.TryGetProperty("source", out var source))
        {
            diag.Source = source.GetString();
        }

        if (element.TryGetProperty("message", out var message))
        {
            diag.Message = message.GetString() ?? "";
        }

        return diag;
    }

    private LspRange ParseRange(JsonElement element)
    {
        var range = new LspRange();

        if (element.TryGetProperty("start", out var start))
        {
            range.Start = ParsePosition(start);
        }

        if (element.TryGetProperty("end", out var end))
        {
            range.End = ParsePosition(end);
        }

        return range;
    }

    private Position ParsePosition(JsonElement element)
    {
        var pos = new Position();

        if (element.TryGetProperty("line", out var line))
        {
            pos.Line = line.GetInt32();
        }

        if (element.TryGetProperty("character", out var character))
        {
            pos.Character = character.GetInt32();
        }

        return pos;
    }

    #region Document Notifications

    public Task DidOpenAsync(string uri, string languageId, int version, string text)
    {
        return SendNotificationAsync("textDocument/didOpen", new
        {
            textDocument = new
            {
                uri,
                languageId,
                version,
                text
            }
        });
    }

    public Task DidChangeAsync(string uri, int version, IReadOnlyList<TextDocumentContentChange> changes)
    {
        return SendNotificationAsync("textDocument/didChange", new
        {
            textDocument = new { uri, version },
            contentChanges = changes.Select(c => new
            {
                range = c.Range != null ? new
                {
                    start = new { line = c.Range.Start.Line, character = c.Range.Start.Character },
                    end = new { line = c.Range.End.Line, character = c.Range.End.Character }
                } : null,
                rangeLength = c.RangeLength,
                text = c.Text
            })
        });
    }

    public Task DidCloseAsync(string uri)
    {
        return SendNotificationAsync("textDocument/didClose", new
        {
            textDocument = new { uri }
        });
    }

    public Task DidSaveAsync(string uri, string? text = null)
    {
        return SendNotificationAsync("textDocument/didSave", new
        {
            textDocument = new { uri },
            text
        });
    }

    #endregion

    #region Language Features

    public async Task<CompletionList?> GetCompletionAsync(string uri, int line, int character, CancellationToken cancellationToken = default)
    {
        var result = await SendRawRequestAsync("textDocument/completion", new
        {
            textDocument = new { uri },
            position = new { line, character }
        }, cancellationToken);

        if (!result.HasValue) return null;

        var list = new CompletionList();

        if (result.Value.ValueKind == JsonValueKind.Array)
        {
            list.Items = ParseCompletionItems(result.Value);
        }
        else if (result.Value.ValueKind == JsonValueKind.Object)
        {
            if (result.Value.TryGetProperty("isIncomplete", out var inc))
            {
                list.IsIncomplete = inc.GetBoolean();
            }
            if (result.Value.TryGetProperty("items", out var items))
            {
                list.Items = ParseCompletionItems(items);
            }
        }

        return list;
    }

    private List<CompletionItem> ParseCompletionItems(JsonElement array)
    {
        var items = new List<CompletionItem>();

        foreach (var item in array.EnumerateArray())
        {
            var ci = new CompletionItem();

            if (item.TryGetProperty("label", out var label))
            {
                ci.Label = label.GetString() ?? "";
            }

            if (item.TryGetProperty("kind", out var kind))
            {
                ci.Kind = (CompletionItemKind)kind.GetInt32();
            }

            if (item.TryGetProperty("detail", out var detail))
            {
                ci.Detail = detail.GetString();
            }

            if (item.TryGetProperty("insertText", out var insertText))
            {
                ci.InsertText = insertText.GetString();
            }

            if (item.TryGetProperty("sortText", out var sortText))
            {
                ci.SortText = sortText.GetString();
            }

            if (item.TryGetProperty("filterText", out var filterText))
            {
                ci.FilterText = filterText.GetString();
            }

            items.Add(ci);
        }

        return items;
    }

    public async Task<Hover?> GetHoverAsync(string uri, int line, int character, CancellationToken cancellationToken = default)
    {
        var result = await SendRawRequestAsync("textDocument/hover", new
        {
            textDocument = new { uri },
            position = new { line, character }
        }, cancellationToken);

        if (!result.HasValue || result.Value.ValueKind == JsonValueKind.Null) return null;

        var hover = new Hover();

        if (result.Value.TryGetProperty("contents", out var contents))
        {
            hover.Contents = ParseMarkupContent(contents);
        }

        if (result.Value.TryGetProperty("range", out var range))
        {
            hover.Range = ParseRange(range);
        }

        return hover;
    }

    private MarkupContent ParseMarkupContent(JsonElement element)
    {
        var mc = new MarkupContent();

        if (element.ValueKind == JsonValueKind.String)
        {
            mc.Value = element.GetString() ?? "";
        }
        else if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("kind", out var kind))
            {
                mc.Kind = kind.GetString() ?? "plaintext";
            }
            if (element.TryGetProperty("value", out var value))
            {
                mc.Value = value.GetString() ?? "";
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var sb = new StringBuilder();
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    sb.AppendLine(item.GetString());
                }
                else if (item.TryGetProperty("value", out var value))
                {
                    sb.AppendLine(value.GetString());
                }
            }
            mc.Value = sb.ToString();
        }

        return mc;
    }

    public async Task<IReadOnlyList<Location>?> GetDefinitionAsync(string uri, int line, int character, CancellationToken cancellationToken = default)
    {
        var result = await SendRawRequestAsync("textDocument/definition", new
        {
            textDocument = new { uri },
            position = new { line, character }
        }, cancellationToken);

        if (!result.HasValue || result.Value.ValueKind == JsonValueKind.Null) return null;

        return ParseLocations(result.Value);
    }

    public async Task<IReadOnlyList<Location>?> GetReferencesAsync(string uri, int line, int character, bool includeDeclaration, CancellationToken cancellationToken = default)
    {
        var result = await SendRawRequestAsync("textDocument/references", new
        {
            textDocument = new { uri },
            position = new { line, character },
            context = new { includeDeclaration }
        }, cancellationToken);

        if (!result.HasValue || result.Value.ValueKind == JsonValueKind.Null) return null;

        return ParseLocations(result.Value);
    }

    private List<Location> ParseLocations(JsonElement element)
    {
        var locations = new List<Location>();

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                locations.Add(ParseLocation(item));
            }
        }
        else if (element.ValueKind == JsonValueKind.Object)
        {
            locations.Add(ParseLocation(element));
        }

        return locations;
    }

    private Location ParseLocation(JsonElement element)
    {
        var loc = new Location();

        if (element.TryGetProperty("uri", out var uri))
        {
            loc.Uri = uri.GetString() ?? "";
        }

        if (element.TryGetProperty("range", out var range))
        {
            loc.Range = ParseRange(range);
        }

        return loc;
    }

    public async Task<IReadOnlyList<DocumentSymbol>?> GetDocumentSymbolsAsync(string uri, CancellationToken cancellationToken = default)
    {
        var result = await SendRawRequestAsync("textDocument/documentSymbol", new
        {
            textDocument = new { uri }
        }, cancellationToken);

        if (!result.HasValue || result.Value.ValueKind == JsonValueKind.Null) return null;

        var symbols = new List<DocumentSymbol>();
        foreach (var item in result.Value.EnumerateArray())
        {
            symbols.Add(ParseDocumentSymbol(item));
        }

        return symbols;
    }

    private DocumentSymbol ParseDocumentSymbol(JsonElement element)
    {
        var sym = new DocumentSymbol();

        if (element.TryGetProperty("name", out var name))
        {
            sym.Name = name.GetString() ?? "";
        }

        if (element.TryGetProperty("detail", out var detail))
        {
            sym.Detail = detail.GetString();
        }

        if (element.TryGetProperty("kind", out var kind))
        {
            sym.Kind = (SymbolKind)kind.GetInt32();
        }

        if (element.TryGetProperty("range", out var range))
        {
            sym.Range = ParseRange(range);
        }

        if (element.TryGetProperty("selectionRange", out var selRange))
        {
            sym.SelectionRange = ParseRange(selRange);
        }

        if (element.TryGetProperty("children", out var children))
        {
            sym.Children = new List<DocumentSymbol>();
            foreach (var child in children.EnumerateArray())
            {
                sym.Children.Add(ParseDocumentSymbol(child));
            }
        }

        return sym;
    }

    public async Task<SignatureHelp?> GetSignatureHelpAsync(string uri, int line, int character, CancellationToken cancellationToken = default)
    {
        var result = await SendRawRequestAsync("textDocument/signatureHelp", new
        {
            textDocument = new { uri },
            position = new { line, character }
        }, cancellationToken);

        if (!result.HasValue || result.Value.ValueKind == JsonValueKind.Null) return null;

        var help = new SignatureHelp();

        if (result.Value.TryGetProperty("signatures", out var sigs))
        {
            foreach (var sig in sigs.EnumerateArray())
            {
                var sigInfo = new SignatureInformation();

                if (sig.TryGetProperty("label", out var label))
                {
                    sigInfo.Label = label.GetString() ?? "";
                }

                if (sig.TryGetProperty("documentation", out var doc))
                {
                    sigInfo.Documentation = ParseMarkupContent(doc);
                }

                if (sig.TryGetProperty("parameters", out var parameters))
                {
                    sigInfo.Parameters = new List<ParameterInformation>();
                    foreach (var param in parameters.EnumerateArray())
                    {
                        var paramInfo = new ParameterInformation();
                        if (param.TryGetProperty("label", out var paramLabel))
                        {
                            paramInfo.Label = paramLabel.GetString() ?? "";
                        }
                        if (param.TryGetProperty("documentation", out var paramDoc))
                        {
                            paramInfo.Documentation = ParseMarkupContent(paramDoc);
                        }
                        sigInfo.Parameters.Add(paramInfo);
                    }
                }

                help.Signatures.Add(sigInfo);
            }
        }

        if (result.Value.TryGetProperty("activeSignature", out var activeSig))
        {
            help.ActiveSignature = activeSig.GetInt32();
        }

        if (result.Value.TryGetProperty("activeParameter", out var activeParam))
        {
            help.ActiveParameter = activeParam.GetInt32();
        }

        return help;
    }

    public async Task<IReadOnlyList<CodeAction>?> GetCodeActionsAsync(string uri, LspRange range, IReadOnlyList<Diagnostic>? diagnostics, CancellationToken cancellationToken = default)
    {
        var result = await SendRawRequestAsync("textDocument/codeAction", new
        {
            textDocument = new { uri },
            range = new
            {
                start = new { line = range.Start.Line, character = range.Start.Character },
                end = new { line = range.End.Line, character = range.End.Character }
            },
            context = new
            {
                diagnostics = diagnostics?.Select(d => new
                {
                    range = new
                    {
                        start = new { line = d.Range.Start.Line, character = d.Range.Start.Character },
                        end = new { line = d.Range.End.Line, character = d.Range.End.Character }
                    },
                    severity = (int)d.Severity,
                    code = d.Code,
                    source = d.Source,
                    message = d.Message
                })
            }
        }, cancellationToken);

        if (!result.HasValue || result.Value.ValueKind == JsonValueKind.Null) return null;

        var actions = new List<CodeAction>();
        foreach (var item in result.Value.EnumerateArray())
        {
            var action = new CodeAction();

            if (item.TryGetProperty("title", out var title))
            {
                action.Title = title.GetString() ?? "";
            }

            if (item.TryGetProperty("isPreferred", out var preferred))
            {
                action.IsPreferred = preferred.GetBoolean();
            }

            actions.Add(action);
        }

        return actions;
    }

    public async Task<IReadOnlyList<TextEdit>?> FormatDocumentAsync(string uri, FormattingOptions options, CancellationToken cancellationToken = default)
    {
        var result = await SendRawRequestAsync("textDocument/formatting", new
        {
            textDocument = new { uri },
            options = new
            {
                tabSize = options.TabSize,
                insertSpaces = options.InsertSpaces,
                trimTrailingWhitespace = options.TrimTrailingWhitespace,
                insertFinalNewline = options.InsertFinalNewline,
                trimFinalNewlines = options.TrimFinalNewlines
            }
        }, cancellationToken);

        if (!result.HasValue || result.Value.ValueKind == JsonValueKind.Null) return null;

        var edits = new List<TextEdit>();
        foreach (var item in result.Value.EnumerateArray())
        {
            var edit = new TextEdit();

            if (item.TryGetProperty("range", out var range))
            {
                edit.Range = ParseRange(range);
            }

            if (item.TryGetProperty("newText", out var newText))
            {
                edit.NewText = newText.GetString() ?? "";
            }

            edits.Add(edit);
        }

        return edits;
    }

    public async Task<WorkspaceEdit?> RenameAsync(string uri, int line, int character, string newName, CancellationToken cancellationToken = default)
    {
        var result = await SendRawRequestAsync("textDocument/rename", new
        {
            textDocument = new { uri },
            position = new { line, character },
            newName
        }, cancellationToken);

        if (!result.HasValue || result.Value.ValueKind == JsonValueKind.Null) return null;

        var edit = new WorkspaceEdit();

        if (result.Value.TryGetProperty("changes", out var changes))
        {
            edit.Changes = new Dictionary<string, List<TextEdit>>();

            foreach (var prop in changes.EnumerateObject())
            {
                var fileEdits = new List<TextEdit>();
                foreach (var item in prop.Value.EnumerateArray())
                {
                    var textEdit = new TextEdit();
                    if (item.TryGetProperty("range", out var range))
                    {
                        textEdit.Range = ParseRange(range);
                    }
                    if (item.TryGetProperty("newText", out var newText))
                    {
                        textEdit.NewText = newText.GetString() ?? "";
                    }
                    fileEdits.Add(textEdit);
                }
                edit.Changes[prop.Name] = fileEdits;
            }
        }

        return edit;
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts?.Cancel();

        try
        {
            _writer?.Dispose();
            _reader?.Dispose();

            if (_serverProcess != null && !_serverProcess.HasExited)
            {
                _serverProcess.Kill();
            }
            _serverProcess?.Dispose();
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
