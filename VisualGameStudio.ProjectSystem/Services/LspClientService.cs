using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// LSP client implementation for connecting to language servers.
/// </summary>
public class LspClientService : ILspClientService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement?>> _pendingRequests = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private Process? _serverProcess;
    private TcpClient? _tcpClient;
    private Stream? _inputStream;
    private Stream? _outputStream;
    private CancellationTokenSource? _readCts;
    private Task? _readTask;
    private int _requestId;
    private string _workspaceRoot = "";
    private bool _disposed;

    /// <inheritdoc/>
    public LspConnectionState State { get; private set; } = LspConnectionState.Disconnected;

    /// <inheritdoc/>
    public ServerCapabilities? Capabilities { get; private set; }

    /// <inheritdoc/>
    public ServerInfo? ServerInfo { get; private set; }

    /// <inheritdoc/>
    public event EventHandler<LspStateChangedEventArgs>? StateChanged;

    /// <inheritdoc/>
    public event EventHandler<LspDiagnosticsEventArgs>? DiagnosticsReceived;

    /// <inheritdoc/>
    public event EventHandler<LogMessageEventArgs>? LogMessageReceived;

    /// <inheritdoc/>
    public event EventHandler<ShowMessageEventArgs>? ShowMessageReceived;

    /// <inheritdoc/>
    public async Task<bool> StartServerAsync(string serverPath, string? arguments, string workspaceRoot, CancellationToken cancellationToken = default)
    {
        if (State != LspConnectionState.Disconnected)
        {
            await StopAsync();
        }

        _workspaceRoot = workspaceRoot;
        SetState(LspConnectionState.Connecting);

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = serverPath,
                Arguments = arguments ?? "",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            _serverProcess = new Process { StartInfo = startInfo };
            _serverProcess.Start();

            _inputStream = _serverProcess.StandardOutput.BaseStream;
            _outputStream = _serverProcess.StandardInput.BaseStream;

            StartReading();

            return await InitializeAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            SetState(LspConnectionState.Error, ex.Message);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> ConnectAsync(string host, int port, string workspaceRoot, CancellationToken cancellationToken = default)
    {
        if (State != LspConnectionState.Disconnected)
        {
            await StopAsync();
        }

        _workspaceRoot = workspaceRoot;
        SetState(LspConnectionState.Connecting);

        try
        {
            _tcpClient = new TcpClient();
            await _tcpClient.ConnectAsync(host, port, cancellationToken);

            var stream = _tcpClient.GetStream();
            _inputStream = stream;
            _outputStream = stream;

            StartReading();

            return await InitializeAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            SetState(LspConnectionState.Error, ex.Message);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task StopAsync()
    {
        if (State == LspConnectionState.Disconnected)
        {
            return;
        }

        SetState(LspConnectionState.ShuttingDown);

        try
        {
            // Send shutdown request
            await SendRequestAsync("shutdown", null, CancellationToken.None);

            // Send exit notification
            await SendNotificationAsync("exit", null);
        }
        catch
        {
            // Ignore errors during shutdown
        }

        Cleanup();
        SetState(LspConnectionState.Disconnected);
    }

    /// <inheritdoc/>
    public async Task DidOpenAsync(string uri, string languageId, int version, string text)
    {
        await SendNotificationAsync("textDocument/didOpen", new
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

    /// <inheritdoc/>
    public async Task DidChangeAsync(string uri, int version, IEnumerable<TextDocumentContentChangeEvent> changes)
    {
        await SendNotificationAsync("textDocument/didChange", new
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

    /// <inheritdoc/>
    public async Task DidSaveAsync(string uri, string? text = null)
    {
        await SendNotificationAsync("textDocument/didSave", new
        {
            textDocument = new { uri },
            text
        });
    }

    /// <inheritdoc/>
    public async Task DidCloseAsync(string uri)
    {
        await SendNotificationAsync("textDocument/didClose", new
        {
            textDocument = new { uri }
        });
    }

    /// <inheritdoc/>
    public async Task<CompletionList?> GetCompletionAsync(string uri, int line, int character, CancellationToken cancellationToken = default)
    {
        var result = await SendRequestAsync("textDocument/completion", new
        {
            textDocument = new { uri },
            position = new { line, character }
        }, cancellationToken);

        if (result == null) return null;

        return JsonSerializer.Deserialize<CompletionList>(result.Value.GetRawText(), JsonOptions);
    }

    /// <inheritdoc/>
    public async Task<Hover?> GetHoverAsync(string uri, int line, int character, CancellationToken cancellationToken = default)
    {
        var result = await SendRequestAsync("textDocument/hover", new
        {
            textDocument = new { uri },
            position = new { line, character }
        }, cancellationToken);

        if (result == null) return null;

        return JsonSerializer.Deserialize<Hover>(result.Value.GetRawText(), JsonOptions);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Location>> GetDefinitionAsync(string uri, int line, int character, CancellationToken cancellationToken = default)
    {
        var result = await SendRequestAsync("textDocument/definition", new
        {
            textDocument = new { uri },
            position = new { line, character }
        }, cancellationToken);

        if (result == null) return Array.Empty<Location>();

        // Definition can return Location, Location[], or null
        try
        {
            if (result.Value.ValueKind == JsonValueKind.Array)
            {
                return JsonSerializer.Deserialize<List<Location>>(result.Value.GetRawText(), JsonOptions) ?? new List<Location>();
            }
            else if (result.Value.ValueKind == JsonValueKind.Object)
            {
                var loc = JsonSerializer.Deserialize<Location>(result.Value.GetRawText(), JsonOptions);
                return loc != null ? new List<Location> { loc } : new List<Location>();
            }
        }
        catch { }

        return Array.Empty<Location>();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Location>> GetReferencesAsync(string uri, int line, int character, bool includeDeclaration, CancellationToken cancellationToken = default)
    {
        var result = await SendRequestAsync("textDocument/references", new
        {
            textDocument = new { uri },
            position = new { line, character },
            context = new { includeDeclaration }
        }, cancellationToken);

        if (result == null) return Array.Empty<Location>();

        return JsonSerializer.Deserialize<List<Location>>(result.Value.GetRawText(), JsonOptions) ?? new List<Location>();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<LspDocumentSymbol>> GetDocumentSymbolsAsync(string uri, CancellationToken cancellationToken = default)
    {
        var result = await SendRequestAsync("textDocument/documentSymbol", new
        {
            textDocument = new { uri }
        }, cancellationToken);

        if (result == null) return Array.Empty<LspDocumentSymbol>();

        return JsonSerializer.Deserialize<List<LspDocumentSymbol>>(result.Value.GetRawText(), JsonOptions) ?? new List<LspDocumentSymbol>();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SymbolInformation>> GetWorkspaceSymbolsAsync(string query, CancellationToken cancellationToken = default)
    {
        var result = await SendRequestAsync("workspace/symbol", new
        {
            query
        }, cancellationToken);

        if (result == null) return Array.Empty<SymbolInformation>();

        return JsonSerializer.Deserialize<List<SymbolInformation>>(result.Value.GetRawText(), JsonOptions) ?? new List<SymbolInformation>();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<LspCodeAction>> GetCodeActionsAsync(string uri, LspRange range, CancellationToken cancellationToken = default)
    {
        var result = await SendRequestAsync("textDocument/codeAction", new
        {
            textDocument = new { uri },
            range = new
            {
                start = new { line = range.Start.Line, character = range.Start.Character },
                end = new { line = range.End.Line, character = range.End.Character }
            },
            context = new { diagnostics = Array.Empty<object>() }
        }, cancellationToken);

        if (result == null) return Array.Empty<LspCodeAction>();

        return JsonSerializer.Deserialize<List<LspCodeAction>>(result.Value.GetRawText(), JsonOptions) ?? new List<LspCodeAction>();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<LspTextEdit>> FormatDocumentAsync(string uri, LspFormattingOptions options, CancellationToken cancellationToken = default)
    {
        var result = await SendRequestAsync("textDocument/formatting", new
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

        if (result == null) return Array.Empty<LspTextEdit>();

        return JsonSerializer.Deserialize<List<LspTextEdit>>(result.Value.GetRawText(), JsonOptions) ?? new List<LspTextEdit>();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<LspTextEdit>> FormatRangeAsync(string uri, LspRange range, LspFormattingOptions options, CancellationToken cancellationToken = default)
    {
        var result = await SendRequestAsync("textDocument/rangeFormatting", new
        {
            textDocument = new { uri },
            range = new
            {
                start = new { line = range.Start.Line, character = range.Start.Character },
                end = new { line = range.End.Line, character = range.End.Character }
            },
            options = new
            {
                tabSize = options.TabSize,
                insertSpaces = options.InsertSpaces
            }
        }, cancellationToken);

        if (result == null) return Array.Empty<LspTextEdit>();

        return JsonSerializer.Deserialize<List<LspTextEdit>>(result.Value.GetRawText(), JsonOptions) ?? new List<LspTextEdit>();
    }

    /// <inheritdoc/>
    public async Task<WorkspaceEdit?> RenameAsync(string uri, int line, int character, string newName, CancellationToken cancellationToken = default)
    {
        var result = await SendRequestAsync("textDocument/rename", new
        {
            textDocument = new { uri },
            position = new { line, character },
            newName
        }, cancellationToken);

        if (result == null) return null;

        return JsonSerializer.Deserialize<WorkspaceEdit>(result.Value.GetRawText(), JsonOptions);
    }

    /// <inheritdoc/>
    public async Task<LspSignatureHelp?> GetSignatureHelpAsync(string uri, int line, int character, CancellationToken cancellationToken = default)
    {
        var result = await SendRequestAsync("textDocument/signatureHelp", new
        {
            textDocument = new { uri },
            position = new { line, character }
        }, cancellationToken);

        if (result == null) return null;

        return JsonSerializer.Deserialize<LspSignatureHelp>(result.Value.GetRawText(), JsonOptions);
    }

    /// <inheritdoc/>
    public async Task<JsonElement?> SendRequestAsync(string method, object? parameters, CancellationToken cancellationToken = default)
    {
        if (State != LspConnectionState.Ready && method != "initialize" && method != "shutdown")
        {
            return null;
        }

        var id = Interlocked.Increment(ref _requestId);
        var tcs = new TaskCompletionSource<JsonElement?>();
        _pendingRequests[id] = tcs;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            var message = new
            {
                jsonrpc = "2.0",
                id,
                method,
                @params = parameters
            };

            await WriteMessageAsync(message);

            using var registration = cts.Token.Register(() => tcs.TrySetCanceled());
            return await tcs.Task;
        }
        finally
        {
            _pendingRequests.TryRemove(id, out _);
        }
    }

    /// <inheritdoc/>
    public async Task SendNotificationAsync(string method, object? parameters)
    {
        if (State != LspConnectionState.Ready && method != "exit" && method != "initialized")
        {
            return;
        }

        var message = new
        {
            jsonrpc = "2.0",
            method,
            @params = parameters
        };

        await WriteMessageAsync(message);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Cleanup();
    }

    #region Private Methods

    private async Task<bool> InitializeAsync(CancellationToken cancellationToken)
    {
        SetState(LspConnectionState.Initializing);

        try
        {
            var result = await SendRequestAsync("initialize", new
            {
                processId = Environment.ProcessId,
                rootUri = new Uri(_workspaceRoot).AbsoluteUri,
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
                                documentationFormat = new[] { "plaintext", "markdown" }
                            }
                        },
                        hover = new { dynamicRegistration = false },
                        signatureHelp = new { dynamicRegistration = false },
                        definition = new { dynamicRegistration = false },
                        references = new { dynamicRegistration = false },
                        documentSymbol = new { dynamicRegistration = false },
                        codeAction = new { dynamicRegistration = false },
                        formatting = new { dynamicRegistration = false },
                        rangeFormatting = new { dynamicRegistration = false },
                        rename = new { dynamicRegistration = false }
                    },
                    workspace = new
                    {
                        symbol = new { dynamicRegistration = false }
                    }
                }
            }, cancellationToken);

            if (result != null)
            {
                var initResult = JsonSerializer.Deserialize<InitializeResult>(result.Value.GetRawText(), JsonOptions);
                Capabilities = initResult?.Capabilities;
                ServerInfo = initResult?.ServerInfo;
            }

            // Send initialized notification
            await SendNotificationAsync("initialized", new { });

            SetState(LspConnectionState.Ready);
            return true;
        }
        catch (Exception ex)
        {
            SetState(LspConnectionState.Error, ex.Message);
            return false;
        }
    }

    private void StartReading()
    {
        _readCts = new CancellationTokenSource();
        _readTask = Task.Run(() => ReadLoopAsync(_readCts.Token));
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        var headerBuffer = new StringBuilder();

        try
        {
            while (!cancellationToken.IsCancellationRequested && _inputStream != null)
            {
                // Read headers
                headerBuffer.Clear();
                int contentLength = -1;

                while (true)
                {
                    var b = _inputStream.ReadByte();
                    if (b == -1) return;

                    headerBuffer.Append((char)b);
                    var headers = headerBuffer.ToString();

                    if (headers.EndsWith("\r\n\r\n"))
                    {
                        // Parse Content-Length
                        foreach (var line in headers.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
                        {
                            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                            {
                                contentLength = int.Parse(line.Substring(15).Trim());
                            }
                        }
                        break;
                    }
                }

                if (contentLength <= 0) continue;

                // Read content
                var content = new byte[contentLength];
                var read = 0;
                while (read < contentLength)
                {
                    var chunk = await _inputStream.ReadAsync(content, read, contentLength - read, cancellationToken);
                    if (chunk == 0) return;
                    read += chunk;
                }

                var json = Encoding.UTF8.GetString(content);
                ProcessMessage(json);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation
        }
        catch (Exception ex)
        {
            SetState(LspConnectionState.Error, ex.Message);
        }
    }

    private void ProcessMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Check if it's a response
            if (root.TryGetProperty("id", out var idElement))
            {
                var id = idElement.GetInt32();
                if (_pendingRequests.TryRemove(id, out var tcs))
                {
                    if (root.TryGetProperty("error", out var error))
                    {
                        tcs.TrySetException(new LspException(
                            error.GetProperty("code").GetInt32(),
                            error.GetProperty("message").GetString() ?? "Unknown error"));
                    }
                    else if (root.TryGetProperty("result", out var result))
                    {
                        tcs.TrySetResult(result.Clone());
                    }
                    else
                    {
                        tcs.TrySetResult(null);
                    }
                }
            }
            // Check if it's a notification
            else if (root.TryGetProperty("method", out var methodElement))
            {
                var method = methodElement.GetString();
                root.TryGetProperty("params", out var paramsElement);

                HandleNotification(method!, paramsElement);
            }
        }
        catch (Exception ex)
        {
            LogMessageReceived?.Invoke(this, new LogMessageEventArgs(MessageType.Error, $"Failed to process message: {ex.Message}"));
        }
    }

    private void HandleNotification(string method, JsonElement parameters)
    {
        switch (method)
        {
            case "textDocument/publishDiagnostics":
                var uri = parameters.GetProperty("uri").GetString()!;
                var diagnostics = JsonSerializer.Deserialize<List<LspDiagnostic>>(
                    parameters.GetProperty("diagnostics").GetRawText(), JsonOptions) ?? new List<LspDiagnostic>();
                DiagnosticsReceived?.Invoke(this, new LspDiagnosticsEventArgs(uri, diagnostics));
                break;

            case "window/logMessage":
                var logType = (MessageType)parameters.GetProperty("type").GetInt32();
                var logMessage = parameters.GetProperty("message").GetString()!;
                LogMessageReceived?.Invoke(this, new LogMessageEventArgs(logType, logMessage));
                break;

            case "window/showMessage":
                var showType = (MessageType)parameters.GetProperty("type").GetInt32();
                var showMessage = parameters.GetProperty("message").GetString()!;
                ShowMessageReceived?.Invoke(this, new ShowMessageEventArgs(showType, showMessage));
                break;
        }
    }

    private async Task WriteMessageAsync(object message)
    {
        if (_outputStream == null) return;

        var json = JsonSerializer.Serialize(message, JsonOptions);
        var content = Encoding.UTF8.GetBytes(json);
        var header = $"Content-Length: {content.Length}\r\n\r\n";
        var headerBytes = Encoding.ASCII.GetBytes(header);

        await _writeLock.WaitAsync();
        try
        {
            await _outputStream.WriteAsync(headerBytes);
            await _outputStream.WriteAsync(content);
            await _outputStream.FlushAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void SetState(LspConnectionState newState, string? error = null)
    {
        var oldState = State;
        State = newState;
        StateChanged?.Invoke(this, new LspStateChangedEventArgs(oldState, newState, error));
    }

    private void Cleanup()
    {
        _readCts?.Cancel();

        try { _inputStream?.Close(); } catch { }
        try { _outputStream?.Close(); } catch { }
        try { _tcpClient?.Close(); } catch { }
        try
        {
            if (_serverProcess != null && !_serverProcess.HasExited)
            {
                _serverProcess.Kill();
            }
            _serverProcess?.Dispose();
        }
        catch { }

        _inputStream = null;
        _outputStream = null;
        _tcpClient = null;
        _serverProcess = null;

        foreach (var pending in _pendingRequests.Values)
        {
            pending.TrySetCanceled();
        }
        _pendingRequests.Clear();
    }

    #endregion

    #region Helper Classes

    private class InitializeResult
    {
        public ServerCapabilities? Capabilities { get; set; }
        public ServerInfo? ServerInfo { get; set; }
    }

    #endregion
}

/// <summary>
/// Exception for LSP errors.
/// </summary>
public class LspException : Exception
{
    public int Code { get; }

    public LspException(int code, string message) : base(message)
    {
        Code = code;
    }
}
