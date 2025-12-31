using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// LSP client that communicates with BasicLang language server
/// </summary>
public class LanguageService : ILanguageService
{
    private Process? _serverProcess;
    private StreamWriter? _writer;
    private StreamReader? _reader;
    private Task? _readTask;
    private CancellationTokenSource? _cts;
    private int _requestId;
    private readonly Dictionary<int, TaskCompletionSource<JsonElement>> _pendingRequests = new();
    private readonly object _lock = new();
    private readonly string _compilerPath;
    private readonly IOutputService _outputService;

    public bool IsConnected { get; private set; }
    public event EventHandler<bool>? ConnectionChanged;
    public event EventHandler<DiagnosticsEventArgs>? DiagnosticsReceived;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public LanguageService(IOutputService outputService)
    {
        _outputService = outputService;
        // Find the BasicLang compiler
        var baseDir = AppContext.BaseDirectory;
        _compilerPath = Path.Combine(baseDir, "BasicLang.dll");

        // Fall back to development path
        if (!File.Exists(_compilerPath))
        {
            _compilerPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "..", "gracepriest", "BasicLangvb", "BasicLang", "bin", "Debug", "net8.0", "BasicLang.dll"));
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsConnected) return;

        try
        {
            _cts = new CancellationTokenSource();

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{_compilerPath}\" --lsp",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardInputEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8
            };

            _serverProcess = new Process { StartInfo = startInfo };
            _serverProcess.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    _outputService.WriteLine($"[LSP Error] {e.Data}", OutputCategory.Debug);
                }
            };

            _serverProcess.Start();
            _serverProcess.BeginErrorReadLine();

            _writer = new StreamWriter(_serverProcess.StandardInput.BaseStream, new UTF8Encoding(false))
            {
                AutoFlush = false
            };
            _reader = new StreamReader(_serverProcess.StandardOutput.BaseStream, Encoding.UTF8);

            // Start reading messages
            _readTask = Task.Run(() => ReadMessagesAsync(_cts.Token), _cts.Token);

            // Initialize the server
            await InitializeAsync(cancellationToken);

            IsConnected = true;
            ConnectionChanged?.Invoke(this, true);
            _outputService.WriteLine("Language server connected", OutputCategory.Build);
        }
        catch (Exception ex)
        {
            _outputService.WriteError($"Failed to start language server: {ex.Message}", OutputCategory.Build);
            await StopAsync();
        }
    }

    public async Task StopAsync()
    {
        IsConnected = false;
        ConnectionChanged?.Invoke(this, false);

        _cts?.Cancel();

        if (_writer != null)
        {
            try
            {
                await SendRequestAsync("shutdown", new { });
                await SendNotificationAsync("exit", new { });
            }
            catch { }
        }

        _writer?.Dispose();
        _reader?.Dispose();

        if (_serverProcess != null && !_serverProcess.HasExited)
        {
            try
            {
                _serverProcess.Kill();
            }
            catch { }
        }
        _serverProcess?.Dispose();

        _serverProcess = null;
        _writer = null;
        _reader = null;
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var initParams = new
        {
            processId = Environment.ProcessId,
            rootUri = (string?)null,
            capabilities = new
            {
                textDocument = new
                {
                    synchronization = new { dynamicRegistration = false, willSave = false, willSaveWaitUntil = false, didSave = true },
                    completion = new { dynamicRegistration = false, completionItem = new { snippetSupport = true, documentationFormat = new[] { "plaintext", "markdown" } } },
                    hover = new { dynamicRegistration = false, contentFormat = new[] { "plaintext", "markdown" } },
                    signatureHelp = new { dynamicRegistration = false },
                    definition = new { dynamicRegistration = false },
                    references = new { dynamicRegistration = false },
                    documentSymbol = new { dynamicRegistration = false },
                    publishDiagnostics = new { relatedInformation = true }
                }
            }
        };

        await SendRequestAsync("initialize", initParams, cancellationToken);
        await SendNotificationAsync("initialized", new { });
    }

    public async Task OpenDocumentAsync(string uri, string text, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return;

        await SendNotificationAsync("textDocument/didOpen", new
        {
            textDocument = new
            {
                uri = PathToUri(uri),
                languageId = "basiclang",
                version = 1,
                text
            }
        });
    }

    public async Task ChangeDocumentAsync(string uri, string text, int version, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return;

        await SendNotificationAsync("textDocument/didChange", new
        {
            textDocument = new { uri = PathToUri(uri), version },
            contentChanges = new[] { new { text } }
        });
    }

    public async Task CloseDocumentAsync(string uri, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return;

        await SendNotificationAsync("textDocument/didClose", new
        {
            textDocument = new { uri = PathToUri(uri) }
        });
    }

    public async Task<IReadOnlyList<CompletionItem>> GetCompletionsAsync(string uri, int line, int column, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return Array.Empty<CompletionItem>();

        try
        {
            var result = await SendRequestAsync("textDocument/completion", new
            {
                textDocument = new { uri = PathToUri(uri) },
                position = new { line = line - 1, character = column - 1 }
            }, cancellationToken);

            return ParseCompletions(result);
        }
        catch
        {
            return Array.Empty<CompletionItem>();
        }
    }

    public async Task<HoverInfo?> GetHoverAsync(string uri, int line, int column, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return null;

        try
        {
            var result = await SendRequestAsync("textDocument/hover", new
            {
                textDocument = new { uri = PathToUri(uri) },
                position = new { line = line - 1, character = column - 1 }
            }, cancellationToken);

            return ParseHover(result);
        }
        catch
        {
            return null;
        }
    }

    public async Task<LocationInfo?> GetDefinitionAsync(string uri, int line, int column, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return null;

        try
        {
            var result = await SendRequestAsync("textDocument/definition", new
            {
                textDocument = new { uri = PathToUri(uri) },
                position = new { line = line - 1, character = column - 1 }
            }, cancellationToken);

            return ParseLocation(result);
        }
        catch
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<LocationInfo>> FindReferencesAsync(string uri, int line, int column, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return Array.Empty<LocationInfo>();

        try
        {
            var result = await SendRequestAsync("textDocument/references", new
            {
                textDocument = new { uri = PathToUri(uri) },
                position = new { line = line - 1, character = column - 1 },
                context = new { includeDeclaration = true }
            }, cancellationToken);

            return ParseLocations(result);
        }
        catch
        {
            return Array.Empty<LocationInfo>();
        }
    }

    public async Task<IReadOnlyList<DocumentSymbol>> GetDocumentSymbolsAsync(string uri, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return Array.Empty<DocumentSymbol>();

        try
        {
            var result = await SendRequestAsync("textDocument/documentSymbol", new
            {
                textDocument = new { uri = PathToUri(uri) }
            }, cancellationToken);

            return ParseDocumentSymbols(result);
        }
        catch
        {
            return Array.Empty<DocumentSymbol>();
        }
    }

    public async Task<SignatureHelp?> GetSignatureHelpAsync(string uri, int line, int column, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return null;

        try
        {
            var result = await SendRequestAsync("textDocument/signatureHelp", new
            {
                textDocument = new { uri = PathToUri(uri) },
                position = new { line = line - 1, character = column - 1 }
            }, cancellationToken);

            return ParseSignatureHelp(result);
        }
        catch
        {
            return null;
        }
    }

    private async Task ReadMessagesAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _reader != null)
        {
            try
            {
                var message = await ReadMessageAsync(cancellationToken);
                if (message != null)
                {
                    ProcessMessage(message.Value);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _outputService.WriteLine($"[LSP] Read error: {ex.Message}", OutputCategory.Debug);
            }
        }
    }

    private async Task<JsonElement?> ReadMessageAsync(CancellationToken cancellationToken)
    {
        if (_reader == null) return null;

        // Read headers
        int contentLength = 0;
        while (true)
        {
            var line = await _reader.ReadLineAsync(cancellationToken);
            if (line == null) return null;
            if (line.Length == 0) break;

            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                contentLength = int.Parse(line.Substring(15).Trim());
            }
        }

        if (contentLength == 0) return null;

        // Read content
        var buffer = new char[contentLength];
        var read = 0;
        while (read < contentLength)
        {
            var chunk = await _reader.ReadAsync(buffer.AsMemory(read, contentLength - read), cancellationToken);
            if (chunk == 0) return null;
            read += chunk;
        }

        var json = new string(buffer);
        return JsonSerializer.Deserialize<JsonElement>(json, JsonOptions);
    }

    private void ProcessMessage(JsonElement message)
    {
        if (message.TryGetProperty("id", out var idProp))
        {
            // Response to a request
            var id = idProp.GetInt32();
            lock (_lock)
            {
                if (_pendingRequests.TryGetValue(id, out var tcs))
                {
                    _pendingRequests.Remove(id);
                    if (message.TryGetProperty("result", out var result))
                    {
                        tcs.SetResult(result);
                    }
                    else if (message.TryGetProperty("error", out var error))
                    {
                        var errorMsg = error.TryGetProperty("message", out var msg) ? msg.GetString() : "Unknown error";
                        tcs.SetException(new Exception(errorMsg));
                    }
                    else
                    {
                        tcs.SetResult(default);
                    }
                }
            }
        }
        else if (message.TryGetProperty("method", out var methodProp))
        {
            // Notification from server
            var method = methodProp.GetString();
            if (method == "textDocument/publishDiagnostics" && message.TryGetProperty("params", out var parms))
            {
                ProcessDiagnostics(parms);
            }
        }
    }

    private void ProcessDiagnostics(JsonElement parms)
    {
        if (!parms.TryGetProperty("uri", out var uriProp)) return;
        var uri = uriProp.GetString() ?? "";
        var filePath = UriToPath(uri);

        var diagnostics = new List<DiagnosticItem>();
        if (parms.TryGetProperty("diagnostics", out var diagArray))
        {
            foreach (var diag in diagArray.EnumerateArray())
            {
                var item = new DiagnosticItem
                {
                    FilePath = filePath,
                    Message = diag.TryGetProperty("message", out var msg) ? msg.GetString() ?? "" : "",
                    Id = diag.TryGetProperty("code", out var code) ? code.ToString() : ""
                };

                if (diag.TryGetProperty("severity", out var sev))
                {
                    item.Severity = sev.GetInt32() switch
                    {
                        1 => DiagnosticSeverity.Error,
                        2 => DiagnosticSeverity.Warning,
                        3 => DiagnosticSeverity.Info,
                        _ => DiagnosticSeverity.Info
                    };
                }

                if (diag.TryGetProperty("range", out var range) && range.TryGetProperty("start", out var start))
                {
                    item.Line = start.TryGetProperty("line", out var l) ? l.GetInt32() + 1 : 0;
                    item.Column = start.TryGetProperty("character", out var c) ? c.GetInt32() + 1 : 0;
                }

                diagnostics.Add(item);
            }
        }

        DiagnosticsReceived?.Invoke(this, new DiagnosticsEventArgs { Uri = filePath, Diagnostics = diagnostics });
    }

    private async Task<JsonElement> SendRequestAsync(string method, object parms, CancellationToken cancellationToken = default)
    {
        var id = Interlocked.Increment(ref _requestId);
        var tcs = new TaskCompletionSource<JsonElement>();

        lock (_lock)
        {
            _pendingRequests[id] = tcs;
        }

        var request = new { jsonrpc = "2.0", id, method, @params = parms };
        await SendMessageAsync(request);

        using var ctr = cancellationToken.Register(() => tcs.TrySetCanceled());
        return await tcs.Task;
    }

    private async Task SendNotificationAsync(string method, object parms)
    {
        var notification = new { jsonrpc = "2.0", method, @params = parms };
        await SendMessageAsync(notification);
    }

    private async Task SendMessageAsync(object message)
    {
        if (_writer == null) return;

        var json = JsonSerializer.Serialize(message, JsonOptions);
        var content = Encoding.UTF8.GetBytes(json);

        var header = $"Content-Length: {content.Length}\r\n\r\n";
        await _writer.WriteAsync(header);
        await _writer.WriteAsync(json);
        await _writer.FlushAsync();
    }

    private static string PathToUri(string path)
    {
        if (path.StartsWith("file://")) return path;
        return "file:///" + path.Replace("\\", "/").Replace(" ", "%20");
    }

    private static string UriToPath(string uri)
    {
        if (!uri.StartsWith("file:///")) return uri;
        return uri.Substring(8).Replace("/", "\\").Replace("%20", " ");
    }

    private static IReadOnlyList<CompletionItem> ParseCompletions(JsonElement result)
    {
        var items = new List<CompletionItem>();

        JsonElement itemsArray;
        if (result.ValueKind == JsonValueKind.Array)
        {
            itemsArray = result;
        }
        else if (result.TryGetProperty("items", out var arr))
        {
            itemsArray = arr;
        }
        else
        {
            return items;
        }

        foreach (var item in itemsArray.EnumerateArray())
        {
            items.Add(new CompletionItem
            {
                Label = item.TryGetProperty("label", out var l) ? l.GetString() ?? "" : "",
                Detail = item.TryGetProperty("detail", out var d) ? d.GetString() : null,
                Documentation = item.TryGetProperty("documentation", out var doc)
                    ? (doc.ValueKind == JsonValueKind.String ? doc.GetString() : doc.TryGetProperty("value", out var v) ? v.GetString() : null)
                    : null,
                Kind = item.TryGetProperty("kind", out var k) ? (CompletionItemKind)k.GetInt32() : CompletionItemKind.Text,
                InsertText = item.TryGetProperty("insertText", out var it) ? it.GetString() : null,
                FilterText = item.TryGetProperty("filterText", out var ft) ? ft.GetString() : null,
                SortText = item.TryGetProperty("sortText", out var st) ? st.GetString() : null
            });
        }

        return items;
    }

    private static HoverInfo? ParseHover(JsonElement result)
    {
        if (result.ValueKind == JsonValueKind.Null || result.ValueKind == JsonValueKind.Undefined)
            return null;

        var hover = new HoverInfo();

        if (result.TryGetProperty("contents", out var contents))
        {
            if (contents.ValueKind == JsonValueKind.String)
            {
                hover.Contents = contents.GetString() ?? "";
            }
            else if (contents.TryGetProperty("value", out var value))
            {
                hover.Contents = value.GetString() ?? "";
            }
            else if (contents.ValueKind == JsonValueKind.Array)
            {
                var sb = new StringBuilder();
                foreach (var c in contents.EnumerateArray())
                {
                    if (c.ValueKind == JsonValueKind.String)
                        sb.AppendLine(c.GetString());
                    else if (c.TryGetProperty("value", out var v))
                        sb.AppendLine(v.GetString());
                }
                hover.Contents = sb.ToString();
            }
        }

        if (result.TryGetProperty("range", out var range))
        {
            if (range.TryGetProperty("start", out var start))
            {
                hover.StartLine = start.TryGetProperty("line", out var l) ? l.GetInt32() + 1 : 0;
                hover.StartColumn = start.TryGetProperty("character", out var c) ? c.GetInt32() + 1 : 0;
            }
            if (range.TryGetProperty("end", out var end))
            {
                hover.EndLine = end.TryGetProperty("line", out var l) ? l.GetInt32() + 1 : 0;
                hover.EndColumn = end.TryGetProperty("character", out var c) ? c.GetInt32() + 1 : 0;
            }
        }

        return hover;
    }

    private static LocationInfo? ParseLocation(JsonElement result)
    {
        if (result.ValueKind == JsonValueKind.Null || result.ValueKind == JsonValueKind.Undefined)
            return null;

        JsonElement loc = result;
        if (result.ValueKind == JsonValueKind.Array)
        {
            var arr = result.EnumerateArray();
            if (!arr.Any()) return null;
            loc = arr.First();
        }

        var location = new LocationInfo();
        if (loc.TryGetProperty("uri", out var uri))
            location.Uri = UriToPath(uri.GetString() ?? "");

        if (loc.TryGetProperty("range", out var range) && range.TryGetProperty("start", out var start))
        {
            location.Line = start.TryGetProperty("line", out var l) ? l.GetInt32() + 1 : 0;
            location.Column = start.TryGetProperty("character", out var c) ? c.GetInt32() + 1 : 0;
        }

        return location;
    }

    private static IReadOnlyList<LocationInfo> ParseLocations(JsonElement result)
    {
        var locations = new List<LocationInfo>();
        if (result.ValueKind != JsonValueKind.Array) return locations;

        foreach (var loc in result.EnumerateArray())
        {
            var location = new LocationInfo();
            if (loc.TryGetProperty("uri", out var uri))
                location.Uri = UriToPath(uri.GetString() ?? "");

            if (loc.TryGetProperty("range", out var range) && range.TryGetProperty("start", out var start))
            {
                location.Line = start.TryGetProperty("line", out var l) ? l.GetInt32() + 1 : 0;
                location.Column = start.TryGetProperty("character", out var c) ? c.GetInt32() + 1 : 0;
            }

            locations.Add(location);
        }

        return locations;
    }

    private static IReadOnlyList<DocumentSymbol> ParseDocumentSymbols(JsonElement result)
    {
        var symbols = new List<DocumentSymbol>();
        if (result.ValueKind != JsonValueKind.Array) return symbols;

        foreach (var sym in result.EnumerateArray())
        {
            symbols.Add(ParseSymbol(sym));
        }

        return symbols;
    }

    private static DocumentSymbol ParseSymbol(JsonElement sym)
    {
        var symbol = new DocumentSymbol
        {
            Name = sym.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
            Detail = sym.TryGetProperty("detail", out var d) ? d.GetString() : null,
            Kind = sym.TryGetProperty("kind", out var k) ? (SymbolKind)k.GetInt32() : SymbolKind.Variable
        };

        if (sym.TryGetProperty("range", out var range) && range.TryGetProperty("start", out var start))
        {
            symbol.Line = start.TryGetProperty("line", out var l) ? l.GetInt32() + 1 : 0;
            symbol.Column = start.TryGetProperty("character", out var c) ? c.GetInt32() + 1 : 0;
        }

        if (sym.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
            {
                symbol.Children.Add(ParseSymbol(child));
            }
        }

        return symbol;
    }

    private static SignatureHelp? ParseSignatureHelp(JsonElement result)
    {
        if (result.ValueKind == JsonValueKind.Null || result.ValueKind == JsonValueKind.Undefined)
            return null;

        var help = new SignatureHelp
        {
            ActiveSignature = result.TryGetProperty("activeSignature", out var aS) ? aS.GetInt32() : 0,
            ActiveParameter = result.TryGetProperty("activeParameter", out var aP) ? aP.GetInt32() : 0
        };

        if (result.TryGetProperty("signatures", out var sigs) && sigs.ValueKind == JsonValueKind.Array)
        {
            foreach (var sig in sigs.EnumerateArray())
            {
                var sigInfo = new SignatureInfo
                {
                    Label = sig.TryGetProperty("label", out var l) ? l.GetString() ?? "" : "",
                    Documentation = sig.TryGetProperty("documentation", out var doc)
                        ? (doc.ValueKind == JsonValueKind.String ? doc.GetString() : null)
                        : null
                };

                if (sig.TryGetProperty("parameters", out var parms) && parms.ValueKind == JsonValueKind.Array)
                {
                    foreach (var p in parms.EnumerateArray())
                    {
                        sigInfo.Parameters.Add(new ParameterInfo
                        {
                            Label = p.TryGetProperty("label", out var pl) ? pl.GetString() ?? "" : "",
                            Documentation = p.TryGetProperty("documentation", out var pd)
                                ? (pd.ValueKind == JsonValueKind.String ? pd.GetString() : null)
                                : null
                        });
                    }
                }

                help.Signatures.Add(sigInfo);
            }
        }

        return help;
    }

    public void Dispose()
    {
        StopAsync().Wait(TimeSpan.FromSeconds(2));
    }
}
