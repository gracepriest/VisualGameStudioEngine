using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// LSP client driving ONE language server, whose identity — what to launch, which files it
/// owns, which <c>languageId</c> to announce them with — comes entirely from its
/// <see cref="LanguageServerDescriptor"/>. Everything else on this class is server-agnostic:
/// every request is keyed by uri + position.
/// <para>
/// One instance per server. Every field below (the process, the reader/writer, the request-id
/// space, the restart budget) is single-server state that a second server would corrupt.
/// </para>
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
    /// <summary>Frame serializer whose waits (lock AND write) are all token-bounded.</summary>
    private readonly LspFrameWriter _frameWriter = new();
    private readonly IOutputService _outputService;

    /// <summary>
    /// Default per-request timeout so a hung/dead server can never leave
    /// callers awaiting forever. It covers BOTH the stdin write (a wedged
    /// server that stops draining stdin leaves the pipe full and the write
    /// blocked) and the wait for the response.
    /// </summary>
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(10);

    /// <summary>True while a deliberate Stop/Dispose is in progress — suppresses crash recovery.</summary>
    private volatile bool _stopping;
    /// <summary>True once disposed — the service must never start a server again.</summary>
    private volatile bool _disposed;
    /// <summary>Restart budget: refunded only after a connection survives the stability window.</summary>
    private readonly RestartPolicy _restartPolicy = new();
    /// <summary>Interlocked guard so a single crash (read-loop EOF + Process.Exited) is handled once.</summary>
    private int _disconnectHandled;

    /// <summary>
    /// Makes <see cref="StartCoreAsync"/> single-flight. Without it, two concurrent starts both
    /// pass the <c>IsConnected</c> check (neither has connected yet) and BOTH spawn a server:
    /// the second overwrites <c>_serverProcess</c>/<c>_writer</c>/<c>_reader</c>, orphaning the
    /// first process past IDE exit (Dispose only kills the one it can still see) while the first
    /// start's read loop keeps reading the SECOND server's stdout through the swapped field —
    /// two loops on one stream, corrupting the byte-exact framing for both.
    /// <para>
    /// Not hypothetical: the shell fire-and-forgets BasicLang's rootless autostart from the
    /// MainWindowViewModel constructor and <c>StartAllAsync</c> from ProjectOpened, and a project
    /// auto-opened at launch fires the second inside the first's multi-second handshake. The
    /// second caller now waits, then sees <c>IsConnected</c> and no-ops — same net effect as
    /// arriving late. Hold time is bounded by the 10s initialize timeout.
    /// </para>
    /// <para>
    /// Deliberately never disposed, for the same reason as the registry's lifecycle lock: nothing
    /// touches <c>AvailableWaitHandle</c>, so disposal frees nothing — while disposing it from
    /// <see cref="Dispose"/> mid-start would turn a benign shutdown race into an
    /// <see cref="ObjectDisposedException"/>.
    /// </para>
    /// </summary>
    private readonly SemaphoreSlim _startGate = new(1, 1);

    public bool IsConnected { get; private set; }

    /// <summary>
    /// Who this instance talks to. Fixed for the lifetime of the service — a descriptor is pure
    /// identity, so re-pointing one at another server would only mean constructing another
    /// service.
    /// </summary>
    public LanguageServerDescriptor Descriptor { get; }

    /// <summary>What the last successful handshake reported; only meaningful while connected.</summary>
    private ServerCapabilities? _capabilities;

    /// <summary>
    /// The workspace root the current (or most recent) server process was started with.
    /// <para>
    /// Held in a field rather than only threaded through as a parameter because
    /// <see cref="TryRestartAsync"/> re-enters <see cref="StartCoreAsync"/> with no caller
    /// to supply it. A restart that dropped the root would silently downgrade the server —
    /// clangd would come back up unable to find compile_commands.json and answer with
    /// garbage diagnostics, with nothing in the log to connect it to the crash.
    /// </para>
    /// Assigned inside <see cref="StartCoreAsync"/> past its early-returns, so it always
    /// describes the server that was actually started.
    /// <para>
    /// Deliberately NOT <c>volatile</c>, unlike <see cref="_stopping"/>/<see cref="_disposed"/>
    /// which are written and read by racing threads. The only cross-thread read is the
    /// restart in <see cref="HandleServerDisconnect"/>, and both the <c>Interlocked.Exchange</c>
    /// on <see cref="_disconnectHandled"/> and the <c>Task.Run</c> that schedules
    /// <see cref="TryRestartAsync"/> establish happens-before against this write — so the
    /// restart thread cannot observe a stale root. <c>volatile</c> here would be cargo cult.
    /// </para>
    /// </summary>
    private string? _workspaceRoot;

    /// <inheritdoc />
    /// <remarks>
    /// Derived from <see cref="IsConnected"/> rather than cleared at each teardown.
    /// This class assigns <c>IsConnected = false</c> at five sites across four paths
    /// (StopAsync, HandleServerDisconnect, both SendMessageAsync pipe-death catches,
    /// and Dispose), and <see cref="CleanupConnection"/> is reached from only the
    /// start/restart paths — so any per-site clear would silently drift out of date
    /// as paths are added. Deriving it makes "capabilities are readable only while
    /// connected" true by construction: a disconnected server supports nothing as far
    /// as callers are concerned, which is the whole point of capturing this at all.
    /// </remarks>
    public ServerCapabilities? Capabilities => IsConnected ? _capabilities : null;

    /// <summary>
    /// Process ID of the most recently started language-server process, or null if a
    /// server was never started. Deliberately retained after Stop/Dispose so callers
    /// (diagnostics, integration tests) can verify the server process actually exited.
    /// </summary>
    public int? ServerProcessId { get; private set; }

    public event EventHandler<bool>? ConnectionChanged;
    public event EventHandler<DiagnosticsEventArgs>? DiagnosticsReceived;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <param name="descriptor">
    /// Which server this instance drives. Defaults to BasicLang — resolved from the
    /// <c>basiclang.lsp.path</c> override or the auto-probe — so the DI registration and the
    /// existing call sites keep working until the registry constructs one service per
    /// descriptor. A default parameter cannot BE the descriptor (it is not a compile-time
    /// constant), hence the null sentinel.
    /// </param>
    public LanguageService(
        IOutputService outputService,
        ISettingsService? settingsService = null,
        LanguageServerDescriptor? descriptor = null)
    {
        _outputService = outputService;
        Descriptor = descriptor ?? CreateBasicLangDescriptor(settingsService);
    }

    /// <summary>
    /// The default descriptor: BasicLang, launched from the <c>basiclang.lsp.path</c> override
    /// when it names an existing file, otherwise from the auto-probe.
    /// </summary>
    /// <remarks>
    /// The settings read — and therefore the <see cref="SettingsConsumerRegistry"/> registration
    /// that must sit next to it — lives HERE rather than in the constructor, because the path has
    /// to be resolved before there is a descriptor to hold it. A caller supplying its own
    /// descriptor has already resolved the path by its own route, and registering a consumer on
    /// its behalf would claim a read this class does not perform.
    /// <para>
    /// Settings are loaded at startup (App.LoadUserSettingsAtStartup) before this singleton is
    /// constructed via MainWindowViewModel, so the value is available during construction.
    /// </para>
    /// </remarks>
    private static LanguageServerDescriptor CreateBasicLangDescriptor(ISettingsService? settingsService)
    {
        SettingsConsumerRegistry.RegisterConsumer(
            LanguageServerDescriptor.BasicLangSettingsKey,
            "LanguageService → BasicLang compiler path override for the spawned --lsp server");

        // A user-supplied override (Settings → BasicLang → "LSP Server Path") wins whenever it
        // names a file that exists; otherwise fall back to the auto-probe.
        var overridePath = ResolveLspPathOverride(
            settingsService?.Get<string>(LanguageServerDescriptor.BasicLangSettingsKey, ""));

        return LanguageServerDescriptor.BasicLang(overridePath ?? ProbeBasicLangCompilerPath());
    }

    /// <summary>
    /// Finds BasicLang.dll: next to the IDE, else the solution's Release build, else its Debug
    /// build. Returns the last candidate even when nothing exists — the caller reports a missing
    /// server against the path it actually looked for.
    /// </summary>
    private static string ProbeBasicLangCompilerPath()
    {
        var baseDir = AppContext.BaseDirectory;

        var deployed = Path.Combine(baseDir, "BasicLang.dll");
        if (File.Exists(deployed)) return deployed;

        // Fall back to development paths
        var release = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "BasicLang", "bin", "Release", "net8.0", "BasicLang.dll"));
        if (File.Exists(release)) return release;

        return Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "BasicLang", "bin", "Debug", "net8.0", "BasicLang.dll"));
    }

    /// <summary>
    /// Resolves the <c>basiclang.lsp.path</c> override: returns the trimmed configured path when it
    /// is non-empty AND points at an existing file, otherwise null (meaning "use the auto-probe").
    /// Pure and static so the resolution rule can be pinned headlessly; <paramref name="fileExists"/>
    /// is injectable for tests and defaults to <see cref="File.Exists(string)"/>.
    /// </summary>
    public static string? ResolveLspPathOverride(string? configuredPath, Func<string, bool>? fileExists = null)
    {
        if (string.IsNullOrWhiteSpace(configuredPath)) return null;
        var trimmed = configuredPath.Trim();
        var exists = fileExists ?? File.Exists;
        return exists(trimmed) ? trimmed : null;
    }

    /// <summary>
    /// The ProcessStartInfo to launch <paramref name="descriptor"/>'s server with, for a session
    /// rooted at <paramref name="workspaceRoot"/>. Pure and static so every launch rule can be
    /// pinned headlessly, without spawning a server.
    /// </summary>
    /// <param name="directoryExists">
    /// Injectable existence probe for the working directory; defaults to
    /// <see cref="Directory.Exists(string)"/>. Mirrors <see cref="ResolveWorkingDirectory"/> and
    /// <see cref="ResolveLspPathOverride"/> — this assembly has no <c>InternalsVisibleTo</c> for
    /// the test project, so a public static with an injectable dependency is the only seam a test
    /// can reach.
    /// </param>
    /// <remarks>
    /// <para>
    /// ⚠ <b>THIS IS THE ONLY PLACE A LANGUAGE SERVER IS CONFIGURED TO START.</b> It takes the
    /// descriptor rather than letting each descriptor build its own ProcessStartInfo precisely so
    /// that the three hardening fixes below cannot be forgotten by a server added later. Each of
    /// them was a real, silent wedge:
    /// </para>
    /// <list type="bullet">
    /// <item><description><b>BOM-less StandardInputEncoding</b> — accessing
    /// <c>Process.StandardInput</c> sets <c>AutoFlush=true</c>, which flushes the wrapper
    /// StreamWriter and writes the encoding preamble. With <c>Encoding.UTF8</c> (BOM) that injects
    /// EF BB BF into the server's stdin, corrupting the first Content-Length header — the server
    /// never replies.</description></item>
    /// <item><description><b>RedirectStandardError</b> — stderr MUST be redirected AND drained
    /// (the caller's <c>ErrorDataReceived</c> + <c>BeginErrorReadLine</c>). An undrained stderr
    /// fills its ~4KB pipe buffer and the server blocks forever. Not optional for clangd, which is
    /// chatty on stderr.</description></item>
    /// <item><description>The <b>Latin1 framing reader</b> lives at the call site (it wraps the
    /// started process's stdout), so it is not visible here — but it is the third of the set:
    /// Content-Length is a BYTE count, and a UTF-8 reader over-reads on any multi-byte character
    /// and corrupts the framing of every subsequent message.</description></item>
    /// </list>
    /// <para>
    /// ⚠ <b>SERVER ARGUMENTS — READ BEFORE ADDING ANY ENCODING FLAG.</b> Never pass clangd's
    /// <c>--offset-encoding=utf-8</c> (widely copy-pasted from blog posts and other editors'
    /// configs) or any other encoding override. This client converts LSP positions as
    /// <c>character = column - 1</c> at 12+ call sites against AvaloniaEdit's <c>Caret.Column</c>,
    /// which is a 1-based UTF-16 code-unit index — so utf-16 is the ONLY encoding it can read. It
    /// is negotiated explicitly via <c>general.positionEncodings</c>; see
    /// <see cref="BuildClientCapabilities"/> and <see cref="ServerCapabilities.Utf16"/>. A utf-8
    /// override shifts every position on every line containing a non-ASCII character, silently and
    /// with no error anywhere.
    /// </para>
    /// <para>
    /// The workspace root is normalized ONCE, here, and the normalized value is what the
    /// descriptor derives its arguments from and what the working directory is probed for — so a
    /// server's <c>--compile-commands-dir</c> and its cwd can never disagree about what the root
    /// IS. See <see cref="NormalizeWorkspaceRoot"/>.
    /// </para>
    /// </remarks>
    public static ProcessStartInfo BuildStartInfo(
        LanguageServerDescriptor descriptor,
        string? workspaceRoot,
        Func<string, bool>? directoryExists = null)
    {
        var root = NormalizeWorkspaceRoot(workspaceRoot);

        var startInfo = new ProcessStartInfo
        {
            FileName = descriptor.FileName,
            Arguments = descriptor.ArgumentsFor(root),
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        };

        // Without this the server inherits the IDE's cwd — wherever the IDE happened to be
        // launched from, and meaningless to the server. Left unset (== "") when the root names no
        // existing directory: Process.Start THROWS on a missing WorkingDirectory, and an unusable
        // root must cost us the cwd, not the whole language server.
        var workingDirectory = ResolveWorkingDirectory(root, directoryExists);
        if (workingDirectory != null)
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        return startInfo;
    }

    /// <inheritdoc />
    public Task StartAsync(string? workspaceRoot = null, CancellationToken cancellationToken = default)
    {
        // A disposed service must never start a server again — and only an
        // EXPLICIT (user-initiated) start clears a previous Stop. Auto-restart
        // goes through StartCoreAsync so an in-flight restart racing a
        // Stop/Dispose cannot erase the shutdown request and resurrect the
        // server (orphaned dotnet child processes after IDE exit).
        if (_disposed) return Task.CompletedTask;

        _stopping = false;
        return StartCoreAsync(workspaceRoot, cancellationToken);
    }

    private async Task StartCoreAsync(string? workspaceRoot, CancellationToken cancellationToken)
    {
        // Single-flight (see _startGate). The IsConnected check MUST be inside the gate: a
        // concurrent caller has to wait for the in-flight start to finish and then observe its
        // outcome, not race past the check while it is still false.
        await _startGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await StartLockedAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _startGate.Release();
        }
    }

    private async Task StartLockedAsync(string? workspaceRoot, CancellationToken cancellationToken)
    {
        if (IsConnected) return;
        if (_stopping || _disposed) return;

        // Past the early-returns: we are committed to starting a server, so this is
        // the root that server will have. Auto-restart reads it back (see the field).
        //
        // ⚠ MUST stay BELOW the early-returns — this placement is load-bearing. Hoisted
        // above `if (IsConnected) return;`, a StartAsync(otherRoot) against an ALREADY
        // CONNECTED server would poison the field without re-rooting anything, and the
        // next crash-restart would silently bring the server back up rooted at a
        // workspace the crashed server never had. Nothing would fail.
        _workspaceRoot = workspaceRoot;

        try
        {
            _cts = new CancellationTokenSource();

            // Log the path being used
            _outputService.WriteLine(
                $"[LSP] Looking for {Descriptor.DisplayName} at: {Descriptor.ServerPath}",
                OutputCategory.Debug);

            // ServerPath, NOT StartInfo.FileName: BasicLang's FileName is "dotnet", resolved via
            // PATH, for which File.Exists is always false. See LanguageServerDescriptor.ServerPath.
            if (!File.Exists(Descriptor.ServerPath))
            {
                _outputService.WriteError(
                    $"[LSP] {Descriptor.DisplayName} not found at: {Descriptor.ServerPath}",
                    OutputCategory.Build);
                return;
            }

            var startInfo = BuildStartInfo(Descriptor, workspaceRoot);

            var requestedRoot = NormalizeWorkspaceRoot(workspaceRoot);
            if (requestedRoot != null && string.IsNullOrEmpty(startInfo.WorkingDirectory))
            {
                // Say so rather than swallowing it: a root that names no directory is a
                // caller bug, and this whole change exists to kill silent LSP failures.
                // Reports the normalized root — that is what was actually probed.
                _outputService.WriteError(
                    $"[LSP] Workspace root does not exist: {requestedRoot} — the server will " +
                    "inherit the IDE's working directory.",
                    OutputCategory.Build);
            }

            _outputService.WriteLine(
                $"[LSP] Starting: {startInfo.FileName} {startInfo.Arguments}", OutputCategory.Debug);

            _serverProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            _serverProcess.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    _outputService.WriteLine($"{e.Data}", OutputCategory.Build);
                }
            };
            // Detect server crashes even when the read loop is idle
            _serverProcess.Exited += OnServerProcessExited;

            _serverProcess.Start();
            ServerProcessId = _serverProcess.Id;
            _serverProcess.BeginErrorReadLine();

            _writer = new StreamWriter(_serverProcess.StandardInput.BaseStream, new UTF8Encoding(false))
            {
                AutoFlush = false
            };
            // Latin1 maps every byte 1:1 to a char, so Content-Length (a BYTE count)
            // can be honoured exactly; the body is re-decoded as UTF-8 afterwards.
            // A UTF-8 StreamReader here would over-read whenever a message contains
            // multi-byte characters, corrupting the framing of subsequent messages.
            _reader = new StreamReader(_serverProcess.StandardOutput.BaseStream, Encoding.Latin1);

            // Start reading messages
            _readTask = Task.Run(() => ReadMessagesAsync(_cts.Token), _cts.Token);

            _outputService.WriteLine("[LSP] Sending initialize request...", OutputCategory.Debug);

            // Initialize the server with timeout
            using var initCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            initCts.CancelAfter(TimeSpan.FromSeconds(10));

            try
            {
                await InitializeAsync(workspaceRoot, initCts.Token);
            }
            catch (OperationCanceledException)
            {
                _outputService.WriteError("[LSP] Initialize timed out after 10 seconds", OutputCategory.Build);
                CleanupConnection();
                return;
            }

            // A Stop/Dispose that arrived during the multi-second handshake
            // wins: tear the fresh connection down instead of announcing it.
            if (_stopping || _disposed)
            {
                CleanupConnection();
                return;
            }

            // Arm crash detection for this connection before announcing it
            Interlocked.Exchange(ref _disconnectHandled, 0);
            IsConnected = true;
            _restartPolicy.OnConnected(DateTime.UtcNow);
            ConnectionChanged?.Invoke(this, true);
            _outputService.WriteLine("Language server connected", OutputCategory.Build);
        }
        catch (Exception ex)
        {
            _outputService.WriteError($"Failed to start language server: {ex.Message}", OutputCategory.Build);
            CleanupConnection();
        }
    }

    public async Task StopAsync()
    {
        // Deliberate stop: suppress crash-recovery (read-loop EOF and
        // Process.Exited will fire as we tear the connection down).
        _stopping = true;
        IsConnected = false;
        ConnectionChanged?.Invoke(this, false);

        if (_writer != null)
        {
            // Send the shutdown request BEFORE cancelling the read loop —
            // otherwise the response can never be read and the request would
            // hang forever. Use a short timeout in case the server is dead.
            try
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await SendRequestAsync("shutdown", new { }, timeoutCts.Token);
            }
            catch { }

            try
            {
                await SendNotificationAsync("exit", new { });
            }
            catch { }
        }

        _cts?.Cancel();
        FailPendingRequests();

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

    /// <summary>
    /// Cancels all in-flight requests so callers awaiting a response do not
    /// hang forever once the server connection is gone.
    /// </summary>
    private void FailPendingRequests()
    {
        lock (_lock)
        {
            foreach (var pending in _pendingRequests.Values)
            {
                pending.TrySetCanceled();
            }
            _pendingRequests.Clear();
        }
    }

    /// <summary>
    /// The client capabilities advertised to every language server. Pure and static
    /// so the utf-16 position-encoding pin below can be asserted headlessly.
    /// </summary>
    /// <remarks>
    /// ⚠ <c>general.positionEncodings</c> offers utf-16 and ONLY utf-16 — that is a
    /// deliberate decision, not an oversight. This client converts LSP positions as
    /// <c>character = column - 1</c> at 12+ call sites against AvaloniaEdit's
    /// <c>Caret.Column</c> (a 1-based UTF-16 code-unit index). Offering utf-8 would
    /// silently shift every position on every line containing a non-ASCII character.
    /// See <see cref="ServerCapabilities.Utf16"/>.
    /// </remarks>
    public static object BuildClientCapabilities() => new
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
        },
        general = new
        {
            positionEncodings = new[] { ServerCapabilities.Utf16 }
        }
    };

    /// <summary>
    /// Parses an LSP <c>initialize</c> result (the <c>result</c> object, i.e. the
    /// <c>{"capabilities":{...}}</c> envelope) into <see cref="ServerCapabilities"/>.
    /// Never throws: every non-conforming payload yields empty capabilities, because a
    /// server whose handshake we cannot read supports nothing as far as we know.
    /// Pure and static for testability.
    /// </summary>
    /// <remarks>
    /// Takes the <see cref="JsonElement"/> directly rather than a string so callers need
    /// no external guard. <c>Undefined</c> — what <see cref="ProcessMessage"/> hands back
    /// for a response carrying no <c>result</c> member — is handled here as one more
    /// non-conforming case; its <c>GetRawText()</c> throws <c>InvalidOperationException</c>
    /// (NOT <c>JsonException</c>), so a string-based signature would push that trap onto
    /// every call site to remember independently.
    /// </remarks>
    public static ServerCapabilities ParseServerCapabilities(JsonElement initializeResult)
    {
        if (initializeResult.ValueKind != JsonValueKind.Object) return new ServerCapabilities();

        // Read BEFORE the capabilities guard below, and from the RESULT rather than from
        // `capabilities`: clangd's offsetEncoding is a top-level sibling of that object. Read
        // from the wrong parent it would answer null forever and DescribeEncodingMismatch
        // would be inert — green, and checking nothing.
        var offsetEncoding = ReadString(initializeResult, "offsetEncoding");

        if (!initializeResult.TryGetProperty("capabilities", out var caps) ||
            caps.ValueKind != JsonValueKind.Object)
        {
            // Still carries offsetEncoding: a reply we otherwise consider non-conforming must
            // not be able to smuggle a utf-8 claim past the guard by omitting `capabilities`.
            return new ServerCapabilities { OffsetEncoding = offsetEncoding };
        }

        return new ServerCapabilities
        {
            OffsetEncoding = offsetEncoding,
            HasCompletionProvider = HasProvider(caps, "completionProvider"),
            HasCompletionResolveProvider =
                caps.TryGetProperty("completionProvider", out var completion) &&
                completion.ValueKind == JsonValueKind.Object &&
                completion.TryGetProperty("resolveProvider", out var resolve) &&
                resolve.ValueKind == JsonValueKind.True,
            HasHoverProvider = HasProvider(caps, "hoverProvider"),
            HasDefinitionProvider = HasProvider(caps, "definitionProvider"),
            HasReferencesProvider = HasProvider(caps, "referencesProvider"),
            HasDocumentSymbolProvider = HasProvider(caps, "documentSymbolProvider"),
            HasSignatureHelpProvider = HasProvider(caps, "signatureHelpProvider"),
            PositionEncoding = ReadString(caps, "positionEncoding") ?? ServerCapabilities.Utf16
        };
    }

    /// <summary>
    /// The value of <paramref name="name"/> on <paramref name="obj"/> when it is a JSON string,
    /// otherwise null. Absent, null, and a non-string value are all "the server did not say".
    /// </summary>
    private static string? ReadString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>
    /// Checks the encodings a server actually reported against the only one this client can
    /// read. Returns <b>null when the handshake is safe</b>, otherwise a message naming what the
    /// server said and why it cannot be used.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>This is the one check standing between us and silently corrupting every position we
    /// send.</b> Positions convert as <c>character = column - 1</c> against AvaloniaEdit's
    /// <c>Caret.Column</c> (1-based UTF-16 code units) at 12+ call sites, so utf-16 is the only
    /// encoding this client can consume — and <b>clangd's own default is utf-8</b>. It answers
    /// utf-16 solely because <see cref="BuildClientCapabilities"/> pins
    /// <c>general.positionEncodings</c> to utf-16. That pin winning the negotiation is what makes
    /// us correct; this function is what makes us KNOW it won, rather than assume it.
    /// <para>
    /// Both fields are checked because nothing guarantees they agree. ⚠ Measured, not assumed:
    /// against real clangd 22.1.6 an <c>--offset-encoding=utf-8</c> flag moves BOTH fields to
    /// utf-8, so for that version the standard field alone would have caught it. The second read
    /// is therefore defence in depth, not the load-bearing check: <c>offsetEncoding</c> is the
    /// field clangd's own semantics predate the standard one with, so a version — or another
    /// server borrowing the extension — that moved only its own field would be invisible to the
    /// standard read alone. It costs one string compare.
    /// </para>
    /// <para>
    /// A server that reports NOTHING is safe, not suspicious: LSP 3.17 defaults positionEncoding
    /// to utf-16, and <see cref="ServerCapabilities.OffsetEncoding"/> is null for every server but
    /// clangd. Casing is not part of the contract — an "UTF-16" is still utf-16, and must not cost
    /// the user their language server over a spelling difference.
    /// </para>
    /// </remarks>
    public static string? DescribeEncodingMismatch(ServerCapabilities capabilities)
    {
        var reported = new List<string>();

        if (!IsUtf16(capabilities.PositionEncoding))
        {
            reported.Add($"capabilities.positionEncoding={capabilities.PositionEncoding}");
        }

        // Null means "never sent" — not a claim, and nothing to contradict.
        if (capabilities.OffsetEncoding != null && !IsUtf16(capabilities.OffsetEncoding))
        {
            reported.Add($"offsetEncoding={capabilities.OffsetEncoding}");
        }

        if (reported.Count == 0) return null;

        return $"the server negotiated a position encoding this client cannot read " +
               $"({string.Join(", ", reported)}). Only utf-16 is supported: LSP positions are " +
               "converted as `character = column - 1` against AvaloniaEdit's 1-based UTF-16 " +
               "Caret.Column, so any other encoding shifts every position on every line " +
               "containing a non-ASCII character. Check that no --offset-encoding argument is " +
               "passed and that general.positionEncodings still advertises utf-16.";
    }

    private static bool IsUtf16(string? encoding) =>
        string.Equals(encoding, ServerCapabilities.Utf16, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Convenience overload parsing raw JSON text; malformed input yields empty
    /// capabilities. Delegates to the <see cref="JsonElement"/> overload, which is the
    /// one the client itself uses — a server's reply is already parsed by then.
    /// </summary>
    public static ServerCapabilities ParseServerCapabilities(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new ServerCapabilities();

        try
        {
            using var doc = JsonDocument.Parse(json);
            // Safe to dispose the document here: the parse copies every value it keeps
            // (GetString allocates), so nothing in the result points back into it.
            return ParseServerCapabilities(doc.RootElement);
        }
        catch (JsonException)
        {
            return new ServerCapabilities();
        }
    }

    /// <summary>
    /// LSP types most provider capabilities as <c>boolean | XxxOptions</c>: a server
    /// answering with the options object DOES support the feature. Absent, null and
    /// literal false all mean unsupported.
    /// </summary>
    /// <remarks>
    /// Accepting the object form is required, not defensive: BasicLang's real
    /// <c>--lsp</c> server answers <c>"hoverProvider": {}</c> — an EMPTY object — and
    /// likewise for definition/references/documentSymbol. Accepting only
    /// <c>JsonValueKind.True</c> would report every one of them as unsupported.
    /// (The <c>--lsp-simple</c> fallback server does send bare booleans, so both
    /// forms genuinely occur in this repo.)
    /// </remarks>
    private static bool HasProvider(JsonElement capabilities, string name)
    {
        if (!capabilities.TryGetProperty(name, out var provider)) return false;

        return provider.ValueKind is JsonValueKind.True or JsonValueKind.Object;
    }

    /// <summary>
    /// Builds the <c>initialize</c> request params for a server rooted at
    /// <paramref name="workspaceRoot"/> (a local directory path), or with no workspace
    /// at all when it is null/blank. Pure and static so the wire shape can be asserted
    /// headlessly; does no filesystem I/O.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A server with no root has no project. This client used to hardcode
    /// <c>rootUri: null</c> — under which clangd cannot locate
    /// <c>compile_commands.json</c> and reports every translation unit as broken,
    /// silently and with no error on either side.
    /// </para>
    /// <para>
    /// ⚠ Built as a dictionary so that "no workspace" OMITS the three members outright.
    /// An anonymous type would have to write <c>rootUri = null</c> and lean on the
    /// caller serializing with <c>DefaultIgnoreCondition.WhenWritingNull</c> to make
    /// them disappear — coupling the wire shape to a serializer setting configured far
    /// away. Omission is a correctness requirement here, not a formatting preference.
    /// </para>
    /// <para>
    /// ⚠ CONSEQUENCE: the keys below are LOAD-BEARING and must stay camelCase by hand.
    /// <c>JsonOptions.PropertyNamingPolicy = CamelCase</c> renames POCO/anonymous-type
    /// PROPERTIES; dictionary KEYS are governed by <c>DictionaryKeyPolicy</c>, which is
    /// unset — so these ship verbatim. Writing <c>["RootUri"]</c> would send
    /// <c>RootUri</c>, the server would ignore it, and neither the compiler nor a test
    /// would object. That is the exact silent failure this method exists to prevent.
    /// </para>
    /// <para>
    /// All three members are sent because servers disagree on which they read:
    /// <c>rootPath</c> is deprecated but still honoured by older servers, <c>rootUri</c>
    /// superseded it, and <c>workspaceFolders</c> superseded that.
    /// </para>
    /// </remarks>
    public static object BuildInitializeParams(string? workspaceRoot)
    {
        var initParams = new Dictionary<string, object?>
        {
            ["processId"] = Environment.ProcessId,
            ["capabilities"] = BuildClientCapabilities()
        };

        var root = NormalizeWorkspaceRoot(workspaceRoot);
        if (root != null)
        {
            var uri = PathToUri(root);

            initParams["rootUri"] = uri;
            // rootPath carries a PATH, not a URI — sending the URI here is a common
            // copy-paste error that servers accept and then resolve nothing against.
            initParams["rootPath"] = root;
            initParams["workspaceFolders"] = new[] { new { uri, name = WorkspaceFolderName(root) } };
        }

        return initParams;
    }

    /// <summary>
    /// The single definition of "the workspace root string": trimmed, or null when there is
    /// no usable root at all.
    /// </summary>
    /// <remarks>
    /// Both consumers of a root — <see cref="BuildInitializeParams"/> (what goes on the wire)
    /// and <see cref="ResolveWorkingDirectory"/> (what the process gets as its cwd) —
    /// normalize through here, so the two can never disagree about what the root IS. They
    /// each trimmed independently before, which meant <c>"  C:\proj  "</c> reached
    /// <c>initialize</c> as <c>C:\proj</c> while the working directory was skipped entirely.
    /// <para>
    /// The two DO legitimately differ on whether a root is USABLE: a root naming no existing
    /// directory still belongs on the wire (it tells the server what the client meant) but
    /// cannot be a cwd. That distinction lives in <see cref="ResolveWorkingDirectory"/>, not here.
    /// </para>
    /// </remarks>
    private static string? NormalizeWorkspaceRoot(string? workspaceRoot) =>
        string.IsNullOrWhiteSpace(workspaceRoot) ? null : workspaceRoot.Trim();

    /// <summary>
    /// The working directory to give the language-server process: the workspace root when it
    /// names an existing directory, otherwise null — meaning "inherit the IDE's cwd".
    /// </summary>
    /// <remarks>
    /// Guarded on existence because <see cref="Process.Start()"/> THROWS on a non-existent
    /// <see cref="ProcessStartInfo.WorkingDirectory"/>: an unusable root must cost us the cwd,
    /// not the entire language server. The root still reaches the server via
    /// <see cref="BuildInitializeParams"/>, which does no filesystem I/O.
    /// <para>
    /// Pure and static with an injectable <paramref name="directoryExists"/>, mirroring
    /// <see cref="ResolveLspPathOverride"/> — this assembly has no <c>InternalsVisibleTo</c>
    /// for the test project, so a public static is the only seam a test can reach.
    /// </para>
    /// </remarks>
    public static string? ResolveWorkingDirectory(string? workspaceRoot, Func<string, bool>? directoryExists = null)
    {
        var root = NormalizeWorkspaceRoot(workspaceRoot);
        if (root == null) return null;

        var exists = directoryExists ?? Directory.Exists;
        return exists(root) ? root : null;
    }

    /// <summary>
    /// Display label for a workspace folder: the directory's own name.
    /// </summary>
    /// <remarks>
    /// <see cref="Path.GetFileName(string)"/> alone returns "" for any path ending in a
    /// separator (<c>C:\proj\</c>) — and a workspaceFolders entry with an empty name is
    /// malformed, which is worse than sending no folder at all. Drive/UNC roots have no
    /// name component to take, so they fall back to the path itself.
    /// </remarks>
    private static string WorkspaceFolderName(string root)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(root);
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? trimmed : name;
    }

    private async Task InitializeAsync(string? workspaceRoot, CancellationToken cancellationToken)
    {
        var initParams = BuildInitializeParams(workspaceRoot);

        // No need to clear _capabilities first: IsConnected is false for the whole of
        // this method (StartCoreAsync returns early if already connected and only sets
        // IsConnected after we return), so Capabilities already reads null throughout.
        var initResult = await SendRequestAsync("initialize", initParams, cancellationToken);

        // No guard needed: ParseServerCapabilities takes the element and treats a
        // missing/Undefined result as "told us nothing" (see its remarks).
        _capabilities = ParseServerCapabilities(initResult);

        // REFUSE a server whose position encoding we cannot read, rather than connecting and
        // mis-placing every position on every non-ASCII line for the rest of the session. Loud
        // and dead beats quiet and wrong: the failure is a visible "no IntelliSense" plus this
        // message, instead of hovers that point one character off and server textEdits that
        // land in the wrong place. Thrown (not returned) so StartCoreAsync's catch performs the
        // same CleanupConnection any other failed handshake gets; the server is never announced
        // connected, and nothing auto-restarts a start that never succeeded.
        var mismatch = DescribeEncodingMismatch(_capabilities);
        if (mismatch != null)
        {
            throw new InvalidOperationException(
                $"{Descriptor.DisplayName}: {mismatch}");
        }

        await SendNotificationAsync("initialized", new { });
    }

    public async Task OpenDocumentAsync(string uri, string text, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return;

        // A document this server does not own must never be announced to it: the languageId
        // would either be another server's (a lie the server cannot detect) or absent (the
        // serializer omits nulls — a malformed didOpen, silently). Report and drop instead.
        //
        // Reported rather than thrown even though the descriptor throws: didOpen is fired
        // and forgotten (`_ = OpenDocumentAsync(...)`) at the reconnect re-sync, so an
        // exception here would become an unobserved task exception and vanish. Routing is the
        // caller's job; being loud about a routing bug is ours.
        if (!Descriptor.Owns(uri))
        {
            _outputService.WriteError(
                $"[LSP] Not announcing '{uri}' to the {Descriptor.DisplayName} language server — " +
                $"it owns {string.Join(", ", Descriptor.Extensions)}. This is a routing bug.",
                OutputCategory.Build);
            return;
        }

        await SendNotificationAsync("textDocument/didOpen", new
        {
            textDocument = new
            {
                uri = PathToUri(uri),
                // From the FILE, not a per-server constant: a server can own more than one
                // language (clangd: .cpp and .h are both "cpp"; "c" the day .c is routed).
                languageId = Descriptor.LanguageIdFor(uri),
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

    public async Task SaveDocumentAsync(string uri, string text, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return;

        await SendNotificationAsync("textDocument/didSave", new
        {
            textDocument = new { uri = PathToUri(uri) },
            text
        });
    }

    public async Task<IReadOnlyList<CompletionItem>> GetCompletionsAsync(string uri, int line, int column, CancellationToken cancellationToken = default)
    {
        // NOTE: these traces run on every completion keystroke — they must go
        // to the Debug output category, never the user's Build pane.
        _outputService.WriteLine($"[LSP] GetCompletionsAsync: uri={uri}, line={line}, col={column}", OutputCategory.Debug);

        if (!IsConnected)
        {
            _outputService.WriteLine($"[LSP] Not connected, returning empty", OutputCategory.Debug);
            return Array.Empty<CompletionItem>();
        }

        try
        {
            var lspUri = PathToUri(uri);

            var result = await SendRequestAsync("textDocument/completion", new
            {
                textDocument = new { uri = lspUri },
                position = new { line = line - 1, character = column - 1 }
            }, cancellationToken);

            var completions = ParseCompletions(result);
            _outputService.WriteLine($"[LSP] Received {completions.Count} completions", OutputCategory.Debug);
            return completions;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Caller-initiated cancellation (a newer request superseded this
            // one) — let the caller distinguish it from an empty result.
            throw;
        }
        catch (Exception ex)
        {
            _outputService.WriteLine($"[LSP] Completion error: {ex.Message}", OutputCategory.Debug);
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
        catch (Exception ex)
        {
            // Don't silently mask server errors — they are indistinguishable
            // from a legitimate "no hover here" for callers otherwise.
            _outputService.WriteLine($"[LSP] Hover error: {ex.Message}", OutputCategory.Debug);
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

    public async Task<LocationInfo?> GetImplementationAsync(string uri, int line, int column, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return null;

        try
        {
            var result = await SendRequestAsync("textDocument/implementation", new
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
        var disconnected = false;

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
            catch (EndOfStreamException)
            {
                // Server stdout closed (crash/exit) — do NOT keep spinning
                disconnected = true;
                break;
            }
            catch (IOException)
            {
                disconnected = true;
                break;
            }
            catch (ObjectDisposedException)
            {
                disconnected = true;
                break;
            }
            catch (Exception ex)
            {
                _outputService.WriteLine($"[LSP] Read error: {ex.Message}", OutputCategory.Debug);
            }
        }

        if (disconnected && !cancellationToken.IsCancellationRequested)
        {
            HandleServerDisconnect("server output stream closed");
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
            if (line == null)
            {
                // null from ReadLineAsync means END OF STREAM, not an empty
                // message — the server process is gone.
                throw new EndOfStreamException("LSP server stdout reached end of stream");
            }
            if (line.Length == 0) break;

            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(line.Substring(15).Trim(), out contentLength))
                    contentLength = 0;
            }
        }

        if (contentLength == 0) return null;

        // Read content — contentLength is a BYTE count. The reader uses Latin1
        // (1 byte == 1 char), so reading contentLength chars reads exactly the
        // message body; re-decode those bytes as UTF-8 to get the real JSON.
        var buffer = new char[contentLength];
        var read = 0;
        while (read < contentLength)
        {
            var chunk = await _reader.ReadAsync(buffer.AsMemory(read, contentLength - read), cancellationToken);
            if (chunk == 0)
            {
                throw new EndOfStreamException("LSP server stdout closed mid-message");
            }
            read += chunk;
        }

        var bytes = Encoding.Latin1.GetBytes(buffer);
        var json = Encoding.UTF8.GetString(bytes);
        return JsonSerializer.Deserialize<JsonElement>(json, JsonOptions);
    }

    private void OnServerProcessExited(object? sender, EventArgs e)
    {
        HandleServerDisconnect("server process exited");
    }

    /// <summary>
    /// Handles an unexpected loss of the language server: marks the service
    /// disconnected, fails all pending requests so awaiting callers do not
    /// hang, notifies the user, and starts a bounded auto-restart.
    /// Safe to call from multiple detection paths — only the first wins.
    /// </summary>
    private void HandleServerDisconnect(string reason)
    {
        if (_stopping) return;
        if (Interlocked.Exchange(ref _disconnectHandled, 1) == 1) return;

        IsConnected = false;
        // Refunds the restart budget only if this connection proved stable —
        // a server crashing shortly after every reconnect must exhaust the
        // budget instead of entering an endless kill/spawn cycle.
        _restartPolicy.OnDisconnected(DateTime.UtcNow);
        FailPendingRequests();
        try { ConnectionChanged?.Invoke(this, false); } catch { }
        _outputService.WriteError(
            $"[LSP] Language server connection lost ({reason}). IntelliSense is temporarily unavailable.",
            OutputCategory.Build);

        _ = Task.Run(TryRestartAsync);
    }

    /// <summary>
    /// Bounded auto-restart with exponential backoff (1s, 2s, 4s). The
    /// attempt budget is NOT refunded on a successful reconnect — only a
    /// connection that survives <see cref="RestartPolicy.StabilityWindow"/>
    /// earns it back (see <see cref="HandleServerDisconnect"/>), so a
    /// crash-after-every-reconnect loop terminates at the cap.
    /// </summary>
    private async Task TryRestartAsync()
    {
        while (!_stopping && !_disposed && _restartPolicy.CanAttempt)
        {
            var delay = _restartPolicy.BeginAttempt();
            _outputService.WriteLine(
                $"[LSP] Restarting language server in {delay.TotalSeconds:0}s (attempt {_restartPolicy.Attempts}/{RestartPolicy.MaxAttempts})...",
                OutputCategory.Build);

            try { await Task.Delay(delay); } catch { return; }
            if (_stopping || _disposed) return;

            CleanupConnection();

            // Restart with the SAME workspace root the crashed server had — a restart
            // that silently dropped it would leave the server rootless (see _workspaceRoot).
            try { await StartCoreAsync(_workspaceRoot, CancellationToken.None); } catch { }

            if (IsConnected)
            {
                _outputService.WriteLine("[LSP] Language server restarted successfully.", OutputCategory.Build);
                return;
            }
        }

        if (!_stopping && !_disposed)
        {
            _outputService.WriteError(
                $"[LSP] Language server could not be restarted after {RestartPolicy.MaxAttempts} attempts. Restart the IDE to re-enable IntelliSense.",
                OutputCategory.Build);
        }
    }

    /// <summary>
    /// Tears down the current server process/streams without the shutdown
    /// handshake. Used for failed starts and pre-restart cleanup; deliberate
    /// stops go through <see cref="StopAsync"/>.
    /// </summary>
    private void CleanupConnection()
    {
        _cts?.Cancel();

        try { _writer?.Dispose(); } catch { }
        try { _reader?.Dispose(); } catch { }
        _writer = null;
        _reader = null;

        var process = _serverProcess;
        _serverProcess = null;
        if (process != null)
        {
            try { process.Exited -= OnServerProcessExited; } catch { }
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch { }
            try { process.Dispose(); } catch { }
        }
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
        // RunContinuationsAsynchronously prevents awaiter continuations from
        // executing inline on the read-loop thread (inside its lock).
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_lock)
        {
            _pendingRequests[id] = tcs;
        }

        try
        {
            var request = new { jsonrpc = "2.0", id, method, @params = parms };

            // Per-request timeout, armed BEFORE the write: a wedged server
            // that stops draining its stdin blocks the write itself (full OS
            // pipe), so the timeout must cover the write phase too — never
            // only the wait for the response.
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(DefaultRequestTimeout);
            using var ctr = timeoutCts.Token.Register(() => tcs.TrySetCanceled(cancellationToken));

            await SendMessageAsync(request, timeoutCts.Token);
            return await tcs.Task;
        }
        finally
        {
            // Remove the entry on cancellation/timeout as well — otherwise
            // requests the server never answers leak dictionary entries.
            lock (_lock)
            {
                _pendingRequests.Remove(id);
            }
        }
    }

    private async Task SendNotificationAsync(string method, object parms)
    {
        var notification = new { jsonrpc = "2.0", method, @params = parms };

        // Notifications get the same write timeout: one didChange blocked on
        // a full pipe must not hang its sender (and every queued sender)
        // forever while the wedged process stays alive.
        using var timeoutCts = new CancellationTokenSource(DefaultRequestTimeout);
        try
        {
            await SendMessageAsync(notification, timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            // The server stopped reading stdin — drop the notification.
        }
    }

    private async Task SendMessageAsync(object message, CancellationToken cancellationToken = default)
    {
        var writer = _writer;
        if (writer == null) return;

        try
        {
            var json = JsonSerializer.Serialize(message, JsonOptions);
            await _frameWriter.WriteFrameAsync(writer, json, cancellationToken);
        }
        catch (IOException)
        {
            // LSP server pipe closed — mark as disconnected
            _writer = null;
            IsConnected = false;
        }
        catch (ObjectDisposedException)
        {
            _writer = null;
            IsConnected = false;
        }
    }

    /// <summary>
    /// Converts a local file path to a fully percent-encoded file:// URI.
    /// Uses System.Uri so '#', '%', spaces and non-ASCII characters round-trip
    /// correctly (RFC 3986) instead of silently corrupting document identity.
    /// Public and static for testability.
    /// </summary>
    public static string PathToUri(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        if (path.StartsWith("file://", StringComparison.OrdinalIgnoreCase)) return path;

        try
        {
            return new Uri(path).AbsoluteUri;
        }
        catch (UriFormatException)
        {
            // Relative/opaque input — fall back to the legacy best-effort form
            return "file:///" + path.Replace("\\", "/").Replace(" ", "%20");
        }
    }

    /// <summary>
    /// Converts a file:// URI back to a local file path, decoding all
    /// percent-encoded characters. Non-file URIs pass through unchanged.
    /// Public and static for testability.
    /// </summary>
    public static string UriToPath(string uri)
    {
        if (string.IsNullOrEmpty(uri)) return uri;
        if (!uri.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return uri;

        try
        {
            return new Uri(uri).LocalPath;
        }
        catch (UriFormatException)
        {
            // Legacy space-only encoded form
            return uri.StartsWith("file:///") ? uri.Substring(8).Replace("/", "\\").Replace("%20", " ") : uri;
        }
    }

    /// <summary>
    /// Parses an LSP textDocument/completion result (CompletionItem[] or
    /// CompletionList) into the Core model, including insertTextFormat and
    /// preselect. Public and static for testability.
    /// </summary>
    public static IReadOnlyList<CompletionItem> ParseCompletions(JsonElement result)
    {
        var items = new List<CompletionItem>();

        JsonElement itemsArray;
        if (result.ValueKind == JsonValueKind.Array)
        {
            itemsArray = result;
        }
        else if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("items", out var arr))
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
                SortText = item.TryGetProperty("sortText", out var st) ? st.GetString() : null,
                InsertTextFormat = item.TryGetProperty("insertTextFormat", out var itf)
                                   && itf.ValueKind == JsonValueKind.Number
                                   && itf.GetInt32() == (int)InsertTextFormat.Snippet
                    ? InsertTextFormat.Snippet
                    : InsertTextFormat.PlainText,
                Preselect = item.TryGetProperty("preselect", out var ps) && ps.ValueKind == JsonValueKind.True
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

    public async Task<IReadOnlyList<TypeHierarchyItemInfo>> GetSupertypesAsync(string uri, int line, int column, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return Array.Empty<TypeHierarchyItemInfo>();

        try
        {
            // First, prepare type hierarchy to get the starting item
            var prepareResult = await SendRequestAsync("textDocument/prepareTypeHierarchy", new
            {
                textDocument = new { uri = PathToUri(uri) },
                position = new { line = line - 1, character = column - 1 }
            }, cancellationToken);

            if (prepareResult.ValueKind != JsonValueKind.Array || !prepareResult.EnumerateArray().Any())
                return Array.Empty<TypeHierarchyItemInfo>();

            var item = prepareResult.EnumerateArray().First();

            // Get supertypes
            var result = await SendRequestAsync("typeHierarchy/supertypes", new
            {
                item
            }, cancellationToken);

            return ParseTypeHierarchyItems(result);
        }
        catch
        {
            return Array.Empty<TypeHierarchyItemInfo>();
        }
    }

    public async Task<IReadOnlyList<TypeHierarchyItemInfo>> GetSubtypesAsync(string uri, int line, int column, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return Array.Empty<TypeHierarchyItemInfo>();

        try
        {
            // First, prepare type hierarchy to get the starting item
            var prepareResult = await SendRequestAsync("textDocument/prepareTypeHierarchy", new
            {
                textDocument = new { uri = PathToUri(uri) },
                position = new { line = line - 1, character = column - 1 }
            }, cancellationToken);

            if (prepareResult.ValueKind != JsonValueKind.Array || !prepareResult.EnumerateArray().Any())
                return Array.Empty<TypeHierarchyItemInfo>();

            var item = prepareResult.EnumerateArray().First();

            // Get subtypes
            var result = await SendRequestAsync("typeHierarchy/subtypes", new
            {
                item
            }, cancellationToken);

            return ParseTypeHierarchyItems(result);
        }
        catch
        {
            return Array.Empty<TypeHierarchyItemInfo>();
        }
    }

    public async Task<IReadOnlyList<CallHierarchyItemInfo>> GetIncomingCallsAsync(string uri, int line, int column, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return Array.Empty<CallHierarchyItemInfo>();

        try
        {
            // First, prepare call hierarchy to get the starting item
            var prepareResult = await SendRequestAsync("textDocument/prepareCallHierarchy", new
            {
                textDocument = new { uri = PathToUri(uri) },
                position = new { line = line - 1, character = column - 1 }
            }, cancellationToken);

            if (prepareResult.ValueKind != JsonValueKind.Array || !prepareResult.EnumerateArray().Any())
                return Array.Empty<CallHierarchyItemInfo>();

            var item = prepareResult.EnumerateArray().First();

            // Get incoming calls
            var result = await SendRequestAsync("callHierarchy/incomingCalls", new
            {
                item
            }, cancellationToken);

            return ParseIncomingCalls(result);
        }
        catch
        {
            return Array.Empty<CallHierarchyItemInfo>();
        }
    }

    public async Task<IReadOnlyList<CallHierarchyItemInfo>> GetOutgoingCallsAsync(string uri, int line, int column, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return Array.Empty<CallHierarchyItemInfo>();

        try
        {
            // First, prepare call hierarchy to get the starting item
            var prepareResult = await SendRequestAsync("textDocument/prepareCallHierarchy", new
            {
                textDocument = new { uri = PathToUri(uri) },
                position = new { line = line - 1, character = column - 1 }
            }, cancellationToken);

            if (prepareResult.ValueKind != JsonValueKind.Array || !prepareResult.EnumerateArray().Any())
                return Array.Empty<CallHierarchyItemInfo>();

            var item = prepareResult.EnumerateArray().First();

            // Get outgoing calls
            var result = await SendRequestAsync("callHierarchy/outgoingCalls", new
            {
                item
            }, cancellationToken);

            return ParseOutgoingCalls(result);
        }
        catch
        {
            return Array.Empty<CallHierarchyItemInfo>();
        }
    }

    private static IReadOnlyList<TypeHierarchyItemInfo> ParseTypeHierarchyItems(JsonElement result)
    {
        var items = new List<TypeHierarchyItemInfo>();
        if (result.ValueKind != JsonValueKind.Array) return items;

        foreach (var item in result.EnumerateArray())
        {
            var info = new TypeHierarchyItemInfo
            {
                Name = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                Detail = item.TryGetProperty("detail", out var d) ? d.GetString() : null,
                Kind = item.TryGetProperty("kind", out var k) ? MapSymbolKindToTypeKind(k.GetInt32()) : HierarchyTypeKind.Class
            };

            if (item.TryGetProperty("uri", out var uri))
                info.FilePath = UriToPath(uri.GetString() ?? "");

            if (item.TryGetProperty("selectionRange", out var range) && range.TryGetProperty("start", out var start))
            {
                info.Line = start.TryGetProperty("line", out var l) ? l.GetInt32() + 1 : 0;
                info.Column = start.TryGetProperty("character", out var c) ? c.GetInt32() + 1 : 0;
            }

            items.Add(info);
        }

        return items;
    }

    private static IReadOnlyList<CallHierarchyItemInfo> ParseIncomingCalls(JsonElement result)
    {
        var items = new List<CallHierarchyItemInfo>();
        if (result.ValueKind != JsonValueKind.Array) return items;

        foreach (var call in result.EnumerateArray())
        {
            if (!call.TryGetProperty("from", out var from)) continue;

            var info = new CallHierarchyItemInfo
            {
                Name = from.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                Detail = from.TryGetProperty("detail", out var d) ? d.GetString() : null,
                Kind = from.TryGetProperty("kind", out var k) ? MapSymbolKindToCallableKind(k.GetInt32()) : HierarchyCallableKind.Method
            };

            if (from.TryGetProperty("uri", out var uri))
                info.FilePath = UriToPath(uri.GetString() ?? "");

            if (from.TryGetProperty("selectionRange", out var range) && range.TryGetProperty("start", out var start))
            {
                info.Line = start.TryGetProperty("line", out var l) ? l.GetInt32() + 1 : 0;
                info.Column = start.TryGetProperty("character", out var c) ? c.GetInt32() + 1 : 0;
            }

            // Parse call sites (fromRanges)
            if (call.TryGetProperty("fromRanges", out var ranges) && ranges.ValueKind == JsonValueKind.Array)
            {
                foreach (var r in ranges.EnumerateArray())
                {
                    if (r.TryGetProperty("start", out var s))
                    {
                        info.CallSites.Add(new CallSiteItemInfo
                        {
                            FilePath = info.FilePath,
                            Line = s.TryGetProperty("line", out var ln) ? ln.GetInt32() + 1 : 0,
                            Column = s.TryGetProperty("character", out var ch) ? ch.GetInt32() + 1 : 0
                        });
                    }
                }
            }

            items.Add(info);
        }

        return items;
    }

    private static IReadOnlyList<CallHierarchyItemInfo> ParseOutgoingCalls(JsonElement result)
    {
        var items = new List<CallHierarchyItemInfo>();
        if (result.ValueKind != JsonValueKind.Array) return items;

        foreach (var call in result.EnumerateArray())
        {
            if (!call.TryGetProperty("to", out var to)) continue;

            var info = new CallHierarchyItemInfo
            {
                Name = to.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                Detail = to.TryGetProperty("detail", out var d) ? d.GetString() : null,
                Kind = to.TryGetProperty("kind", out var k) ? MapSymbolKindToCallableKind(k.GetInt32()) : HierarchyCallableKind.Method
            };

            if (to.TryGetProperty("uri", out var uri))
                info.FilePath = UriToPath(uri.GetString() ?? "");

            if (to.TryGetProperty("selectionRange", out var range) && range.TryGetProperty("start", out var start))
            {
                info.Line = start.TryGetProperty("line", out var l) ? l.GetInt32() + 1 : 0;
                info.Column = start.TryGetProperty("character", out var c) ? c.GetInt32() + 1 : 0;
            }

            // Parse call sites (fromRanges)
            if (call.TryGetProperty("fromRanges", out var ranges) && ranges.ValueKind == JsonValueKind.Array)
            {
                foreach (var r in ranges.EnumerateArray())
                {
                    if (r.TryGetProperty("start", out var s))
                    {
                        info.CallSites.Add(new CallSiteItemInfo
                        {
                            FilePath = info.FilePath,
                            Line = s.TryGetProperty("line", out var ln) ? ln.GetInt32() + 1 : 0,
                            Column = s.TryGetProperty("character", out var ch) ? ch.GetInt32() + 1 : 0
                        });
                    }
                }
            }

            items.Add(info);
        }

        return items;
    }

    private static HierarchyTypeKind MapSymbolKindToTypeKind(int symbolKind)
    {
        return symbolKind switch
        {
            5 => HierarchyTypeKind.Class,      // Class
            11 => HierarchyTypeKind.Interface, // Interface
            23 => HierarchyTypeKind.Struct,    // Struct
            2 => HierarchyTypeKind.Module,     // Module
            _ => HierarchyTypeKind.Class
        };
    }

    private static HierarchyCallableKind MapSymbolKindToCallableKind(int symbolKind)
    {
        return symbolKind switch
        {
            6 => HierarchyCallableKind.Method,      // Method
            12 => HierarchyCallableKind.Function,   // Function
            9 => HierarchyCallableKind.Constructor, // Constructor
            7 => HierarchyCallableKind.Property,    // Property
            _ => HierarchyCallableKind.Method
        };
    }

    public async Task<WorkspaceEditInfo?> RenameAsync(string uri, int line, int column, string newName, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return null;

        try
        {
            var result = await SendRequestAsync("textDocument/rename", new
            {
                textDocument = new { uri = PathToUri(uri) },
                position = new { line = line - 1, character = column - 1 },
                newName
            }, cancellationToken);

            return ParseWorkspaceEdit(result);
        }
        catch
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<CodeActionInfo>> GetCodeActionsAsync(string uri, int startLine, int startColumn, int endLine, int endColumn, IReadOnlyList<DiagnosticItem>? diagnostics = null, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return Array.Empty<CodeActionInfo>();

        try
        {
            var lspDiagnostics = diagnostics?.Select(d => new
            {
                range = new
                {
                    start = new { line = d.Line - 1, character = d.Column - 1 },
                    end = new { line = d.Line - 1, character = d.Column + 10 }
                },
                message = d.Message,
                severity = d.Severity switch
                {
                    DiagnosticSeverity.Error => 1,
                    DiagnosticSeverity.Warning => 2,
                    DiagnosticSeverity.Info => 3,
                    _ => 4
                },
                code = d.Id
            }).ToArray() ?? Array.Empty<object>();

            var result = await SendRequestAsync("textDocument/codeAction", new
            {
                textDocument = new { uri = PathToUri(uri) },
                range = new
                {
                    start = new { line = startLine - 1, character = startColumn - 1 },
                    end = new { line = endLine - 1, character = endColumn - 1 }
                },
                context = new { diagnostics = lspDiagnostics }
            }, cancellationToken);

            return ParseCodeActions(result);
        }
        catch
        {
            return Array.Empty<CodeActionInfo>();
        }
    }

    public async Task<IReadOnlyList<TextEditInfo>> FormatDocumentAsync(string uri, FormattingOptionsInfo? options = null, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return Array.Empty<TextEditInfo>();

        try
        {
            var opts = options ?? new FormattingOptionsInfo();
            var result = await SendRequestAsync("textDocument/formatting", new
            {
                textDocument = new { uri = PathToUri(uri) },
                options = new
                {
                    tabSize = opts.TabSize,
                    insertSpaces = opts.InsertSpaces
                }
            }, cancellationToken);

            return ParseTextEdits(result);
        }
        catch
        {
            return Array.Empty<TextEditInfo>();
        }
    }

    public async Task<IReadOnlyList<TextEditInfo>> FormatRangeAsync(string uri, int startLine, int startColumn, int endLine, int endColumn, FormattingOptionsInfo? options = null, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return Array.Empty<TextEditInfo>();

        try
        {
            var opts = options ?? new FormattingOptionsInfo();
            var result = await SendRequestAsync("textDocument/rangeFormatting", new
            {
                textDocument = new { uri = PathToUri(uri) },
                range = new
                {
                    start = new { line = startLine - 1, character = startColumn - 1 },
                    end = new { line = endLine - 1, character = endColumn - 1 }
                },
                options = new
                {
                    tabSize = opts.TabSize,
                    insertSpaces = opts.InsertSpaces
                }
            }, cancellationToken);

            return ParseTextEdits(result);
        }
        catch
        {
            return Array.Empty<TextEditInfo>();
        }
    }

    public async Task<IReadOnlyList<TextEditInfo>> OnTypeFormattingAsync(string uri, int line, int column, string ch, FormattingOptionsInfo? options = null, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return Array.Empty<TextEditInfo>();

        try
        {
            var opts = options ?? new FormattingOptionsInfo();
            var result = await SendRequestAsync("textDocument/onTypeFormatting", new
            {
                textDocument = new { uri = PathToUri(uri) },
                position = new { line = line - 1, character = column - 1 },
                ch,
                options = new
                {
                    tabSize = opts.TabSize,
                    insertSpaces = opts.InsertSpaces
                }
            }, cancellationToken);

            return ParseTextEdits(result);
        }
        catch
        {
            return Array.Empty<TextEditInfo>();
        }
    }

    private static WorkspaceEditInfo? ParseWorkspaceEdit(JsonElement result)
    {
        if (result.ValueKind == JsonValueKind.Null || result.ValueKind == JsonValueKind.Undefined)
            return null;

        var edit = new WorkspaceEditInfo();

        if (result.TryGetProperty("changes", out var changes) && changes.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in changes.EnumerateObject())
            {
                var filePath = UriToPath(prop.Name);
                var edits = new List<TextEditInfo>();

                if (prop.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var e in prop.Value.EnumerateArray())
                    {
                        edits.Add(ParseTextEdit(e));
                    }
                }

                edit.Changes[filePath] = edits;
            }
        }

        return edit;
    }

    private static IReadOnlyList<CodeActionInfo> ParseCodeActions(JsonElement result)
    {
        var actions = new List<CodeActionInfo>();
        if (result.ValueKind != JsonValueKind.Array) return actions;

        foreach (var item in result.EnumerateArray())
        {
            var action = new CodeActionInfo
            {
                Title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                IsPreferred = item.TryGetProperty("isPreferred", out var p) && p.GetBoolean()
            };

            if (item.TryGetProperty("kind", out var k))
            {
                var kindStr = k.GetString() ?? "";
                action.Kind = kindStr switch
                {
                    "quickfix" => CodeActionKind.QuickFix,
                    "refactor" => CodeActionKind.Refactor,
                    "refactor.extract" => CodeActionKind.RefactorExtract,
                    "refactor.inline" => CodeActionKind.RefactorInline,
                    "refactor.rewrite" => CodeActionKind.RefactorRewrite,
                    "source" => CodeActionKind.Source,
                    "source.organizeImports" => CodeActionKind.SourceOrganizeImports,
                    "source.fixAll" => CodeActionKind.SourceFixAll,
                    _ => CodeActionKind.QuickFix
                };
            }

            if (item.TryGetProperty("edit", out var edit))
            {
                action.Edit = ParseWorkspaceEdit(edit);
            }

            actions.Add(action);
        }

        return actions;
    }

    private static IReadOnlyList<TextEditInfo> ParseTextEdits(JsonElement result)
    {
        var edits = new List<TextEditInfo>();
        if (result.ValueKind != JsonValueKind.Array) return edits;

        foreach (var item in result.EnumerateArray())
        {
            edits.Add(ParseTextEdit(item));
        }

        return edits;
    }

    private static TextEditInfo ParseTextEdit(JsonElement e)
    {
        var edit = new TextEditInfo
        {
            NewText = e.TryGetProperty("newText", out var nt) ? nt.GetString() ?? "" : ""
        };

        if (e.TryGetProperty("range", out var range))
        {
            if (range.TryGetProperty("start", out var start))
            {
                edit.StartLine = start.TryGetProperty("line", out var l) ? l.GetInt32() + 1 : 0;
                edit.StartColumn = start.TryGetProperty("character", out var c) ? c.GetInt32() + 1 : 0;
            }
            if (range.TryGetProperty("end", out var end))
            {
                edit.EndLine = end.TryGetProperty("line", out var l) ? l.GetInt32() + 1 : 0;
                edit.EndColumn = end.TryGetProperty("character", out var c) ? c.GetInt32() + 1 : 0;
            }
        }

        return edit;
    }

    public async Task<IReadOnlyList<CodeLensInfo>> GetCodeLensAsync(string uri, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return Array.Empty<CodeLensInfo>();

        try
        {
            var result = await SendRequestAsync("textDocument/codeLens", new
            {
                textDocument = new { uri = PathToUri(uri) }
            }, cancellationToken);

            return ParseCodeLenses(result);
        }
        catch
        {
            return Array.Empty<CodeLensInfo>();
        }
    }

    private static IReadOnlyList<CodeLensInfo> ParseCodeLenses(JsonElement result)
    {
        var lenses = new List<CodeLensInfo>();
        if (result.ValueKind != JsonValueKind.Array) return lenses;

        foreach (var item in result.EnumerateArray())
        {
            var lens = new CodeLensInfo();

            if (item.TryGetProperty("range", out var range) && range.TryGetProperty("start", out var start))
            {
                lens.Line = start.TryGetProperty("line", out var l) ? l.GetInt32() + 1 : 0;
                lens.Column = start.TryGetProperty("character", out var c) ? c.GetInt32() + 1 : 0;
            }

            if (item.TryGetProperty("command", out var cmd))
            {
                lens.Title = cmd.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                lens.CommandName = cmd.TryGetProperty("command", out var n) ? n.GetString() ?? "" : "";
            }

            lenses.Add(lens);
        }

        return lenses;
    }

    public async Task<LocationInfo?> GetTypeDefinitionAsync(string uri, int line, int column, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return null;

        try
        {
            var result = await SendRequestAsync("textDocument/typeDefinition", new
            {
                textDocument = new { uri = PathToUri(uri) },
                position = new { line = line - 1, character = column - 1 }
            }, cancellationToken);

            if (result.ValueKind == JsonValueKind.Null || result.ValueKind == JsonValueKind.Undefined)
                return null;

            // Can return Location, Location[], or null
            JsonElement locationElement;
            if (result.ValueKind == JsonValueKind.Array)
            {
                var arr = result.EnumerateArray().ToList();
                if (arr.Count == 0) return null;
                locationElement = arr[0];
            }
            else
            {
                locationElement = result;
            }

            if (locationElement.TryGetProperty("uri", out var uriProp) &&
                locationElement.TryGetProperty("range", out var range) &&
                range.TryGetProperty("start", out var start))
            {
                return new LocationInfo
                {
                    Uri = UriToPath(uriProp.GetString() ?? ""),
                    Line = start.TryGetProperty("line", out var l) ? l.GetInt32() + 1 : 0,
                    Column = start.TryGetProperty("character", out var c) ? c.GetInt32() + 1 : 0
                };
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<SemanticTokensResult?> GetSemanticTokensAsync(string uri, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return null;

        try
        {
            var result = await SendRequestAsync("textDocument/semanticTokens/full", new
            {
                textDocument = new { uri = PathToUri(uri) }
            }, cancellationToken);

            if (result.ValueKind == JsonValueKind.Null || result.ValueKind == JsonValueKind.Undefined)
                return null;

            var tokens = new SemanticTokensResult();
            if (result.TryGetProperty("resultId", out var rid))
                tokens.ResultId = rid.GetString();
            if (result.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                tokens.Data = data.EnumerateArray().Select(e => e.GetInt32()).ToArray();

            return tokens;
        }
        catch
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<InlayHintInfo>> GetInlayHintsAsync(string uri, int startLine, int startColumn, int endLine, int endColumn, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return Array.Empty<InlayHintInfo>();

        try
        {
            var result = await SendRequestAsync("textDocument/inlayHint", new
            {
                textDocument = new { uri = PathToUri(uri) },
                range = new
                {
                    start = new { line = startLine - 1, character = startColumn - 1 },
                    end = new { line = endLine - 1, character = endColumn - 1 }
                }
            }, cancellationToken);

            if (result.ValueKind != JsonValueKind.Array) return Array.Empty<InlayHintInfo>();

            var hints = new List<InlayHintInfo>();
            foreach (var item in result.EnumerateArray())
            {
                var hint = new InlayHintInfo();

                if (item.TryGetProperty("position", out var pos))
                {
                    hint.Line = pos.TryGetProperty("line", out var l) ? l.GetInt32() + 1 : 0;
                    hint.Column = pos.TryGetProperty("character", out var c) ? c.GetInt32() + 1 : 0;
                }

                if (item.TryGetProperty("label", out var label))
                {
                    hint.Label = label.ValueKind == JsonValueKind.String
                        ? label.GetString() ?? ""
                        : label.ValueKind == JsonValueKind.Array
                            ? string.Join("", label.EnumerateArray().Select(l =>
                                l.TryGetProperty("value", out var v) ? v.GetString() ?? "" : ""))
                            : "";
                }

                if (item.TryGetProperty("kind", out var kind))
                    hint.Kind = (InlayHintKind)kind.GetInt32();
                if (item.TryGetProperty("paddingLeft", out var pl))
                    hint.PaddingLeft = pl.GetBoolean();
                if (item.TryGetProperty("paddingRight", out var pr))
                    hint.PaddingRight = pr.GetBoolean();

                hints.Add(hint);
            }

            return hints;
        }
        catch
        {
            return Array.Empty<InlayHintInfo>();
        }
    }

    public async Task<IReadOnlyList<SelectionRangeInfo>> GetSelectionRangesAsync(string uri, IReadOnlyList<(int line, int column)> positions, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return Array.Empty<SelectionRangeInfo>();

        try
        {
            var result = await SendRequestAsync("textDocument/selectionRange", new
            {
                textDocument = new { uri = PathToUri(uri) },
                positions = positions.Select(p => new { line = p.line - 1, character = p.column - 1 })
            }, cancellationToken);

            if (result.ValueKind != JsonValueKind.Array) return Array.Empty<SelectionRangeInfo>();

            var ranges = new List<SelectionRangeInfo>();
            foreach (var item in result.EnumerateArray())
            {
                ranges.Add(ParseSelectionRange(item));
            }
            return ranges;
        }
        catch
        {
            return Array.Empty<SelectionRangeInfo>();
        }
    }

    private static SelectionRangeInfo ParseSelectionRange(JsonElement element)
    {
        var info = new SelectionRangeInfo();
        if (element.TryGetProperty("range", out var range))
        {
            if (range.TryGetProperty("start", out var start))
            {
                info.StartLine = start.TryGetProperty("line", out var l) ? l.GetInt32() + 1 : 0;
                info.StartColumn = start.TryGetProperty("character", out var c) ? c.GetInt32() + 1 : 0;
            }
            if (range.TryGetProperty("end", out var end))
            {
                info.EndLine = end.TryGetProperty("line", out var l) ? l.GetInt32() + 1 : 0;
                info.EndColumn = end.TryGetProperty("character", out var c) ? c.GetInt32() + 1 : 0;
            }
        }
        if (element.TryGetProperty("parent", out var parent) && parent.ValueKind == JsonValueKind.Object)
        {
            info.Parent = ParseSelectionRange(parent);
        }
        return info;
    }

    public async Task<IReadOnlyList<DocumentHighlightResult>> GetDocumentHighlightsAsync(string uri, int line, int column, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return Array.Empty<DocumentHighlightResult>();

        try
        {
            var result = await SendRequestAsync("textDocument/documentHighlight", new
            {
                textDocument = new { uri = PathToUri(uri) },
                position = new { line = line - 1, character = column - 1 }
            }, cancellationToken);

            if (result.ValueKind != JsonValueKind.Array) return Array.Empty<DocumentHighlightResult>();

            var highlights = new List<DocumentHighlightResult>();
            foreach (var item in result.EnumerateArray())
            {
                var hl = new DocumentHighlightResult();
                if (item.TryGetProperty("range", out var range))
                {
                    if (range.TryGetProperty("start", out var start))
                    {
                        hl.StartLine = start.TryGetProperty("line", out var l) ? l.GetInt32() + 1 : 0;
                        hl.StartColumn = start.TryGetProperty("character", out var c) ? c.GetInt32() + 1 : 0;
                    }
                    if (range.TryGetProperty("end", out var end))
                    {
                        hl.EndLine = end.TryGetProperty("line", out var l) ? l.GetInt32() + 1 : 0;
                        hl.EndColumn = end.TryGetProperty("character", out var c) ? c.GetInt32() + 1 : 0;
                    }
                }
                if (item.TryGetProperty("kind", out var kind))
                    hl.Kind = (DocumentHighlightKind)kind.GetInt32();
                else
                    hl.Kind = DocumentHighlightKind.Text;

                highlights.Add(hl);
            }
            return highlights;
        }
        catch
        {
            return Array.Empty<DocumentHighlightResult>();
        }
    }

    public async Task<IReadOnlyList<DocumentLinkInfo>> GetDocumentLinksAsync(string uri, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return Array.Empty<DocumentLinkInfo>();

        try
        {
            var result = await SendRequestAsync("textDocument/documentLink", new
            {
                textDocument = new { uri = PathToUri(uri) }
            }, cancellationToken);

            if (result.ValueKind != JsonValueKind.Array) return Array.Empty<DocumentLinkInfo>();

            var links = new List<DocumentLinkInfo>();
            foreach (var item in result.EnumerateArray())
            {
                var link = new DocumentLinkInfo();
                if (item.TryGetProperty("range", out var range))
                {
                    if (range.TryGetProperty("start", out var start))
                    {
                        link.StartLine = start.TryGetProperty("line", out var l) ? l.GetInt32() + 1 : 0;
                        link.StartColumn = start.TryGetProperty("character", out var c) ? c.GetInt32() + 1 : 0;
                    }
                    if (range.TryGetProperty("end", out var end))
                    {
                        link.EndLine = end.TryGetProperty("line", out var l) ? l.GetInt32() + 1 : 0;
                        link.EndColumn = end.TryGetProperty("character", out var c) ? c.GetInt32() + 1 : 0;
                    }
                }
                if (item.TryGetProperty("target", out var target))
                    link.Target = target.GetString() ?? "";
                if (item.TryGetProperty("tooltip", out var tooltip))
                    link.Tooltip = tooltip.GetString();

                links.Add(link);
            }
            return links;
        }
        catch
        {
            return Array.Empty<DocumentLinkInfo>();
        }
    }

    public async Task<IReadOnlyList<FoldingRangeInfo>> GetFoldingRangesAsync(string uri, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return Array.Empty<FoldingRangeInfo>();

        try
        {
            var result = await SendRequestAsync("textDocument/foldingRange", new
            {
                textDocument = new { uri = PathToUri(uri) }
            }, cancellationToken);

            if (result.ValueKind != JsonValueKind.Array) return Array.Empty<FoldingRangeInfo>();

            var ranges = new List<FoldingRangeInfo>();
            foreach (var item in result.EnumerateArray())
            {
                var range = new FoldingRangeInfo
                {
                    // LSP uses 0-based lines; convert to 1-based
                    StartLine = item.TryGetProperty("startLine", out var sl) ? sl.GetInt32() + 1 : 0,
                    EndLine = item.TryGetProperty("endLine", out var el) ? el.GetInt32() + 1 : 0,
                    Kind = item.TryGetProperty("kind", out var k) ? k.GetString() : null
                };
                if (range.StartLine > 0 && range.EndLine > 0)
                    ranges.Add(range);
            }
            return ranges;
        }
        catch
        {
            return Array.Empty<FoldingRangeInfo>();
        }
    }

    public async Task<IReadOnlyList<WorkspaceSymbolInfo>> GetWorkspaceSymbolsAsync(string query, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return Array.Empty<WorkspaceSymbolInfo>();

        try
        {
            var result = await SendRequestAsync("workspace/symbol", new
            {
                query
            }, cancellationToken);

            if (result.ValueKind != JsonValueKind.Array) return Array.Empty<WorkspaceSymbolInfo>();

            var symbols = new List<WorkspaceSymbolInfo>();
            foreach (var item in result.EnumerateArray())
            {
                var sym = new WorkspaceSymbolInfo
                {
                    Name = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    Kind = item.TryGetProperty("kind", out var k) ? (SymbolKind)k.GetInt32() : SymbolKind.Variable,
                    ContainerName = item.TryGetProperty("containerName", out var cn) ? cn.GetString() ?? "" : ""
                };

                if (item.TryGetProperty("location", out var location))
                {
                    if (location.TryGetProperty("uri", out var uri))
                        sym.FilePath = UriToPath(uri.GetString() ?? "");

                    if (location.TryGetProperty("range", out var range) && range.TryGetProperty("start", out var start))
                    {
                        sym.Line = start.TryGetProperty("line", out var l) ? l.GetInt32() + 1 : 0;
                        sym.Column = start.TryGetProperty("character", out var c) ? c.GetInt32() + 1 : 0;
                    }
                }

                symbols.Add(sym);
            }
            return symbols;
        }
        catch
        {
            return Array.Empty<WorkspaceSymbolInfo>();
        }
    }

    public async Task<LinkedEditingRangeResult?> GetLinkedEditingRangesAsync(string uri, int line, int column, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return null;

        try
        {
            var result = await SendRequestAsync("textDocument/linkedEditingRange", new
            {
                textDocument = new { uri = PathToUri(uri) },
                position = new { line = line - 1, character = column - 1 }
            }, cancellationToken);

            return ParseLinkedEditingRanges(result);
        }
        catch
        {
            return null;
        }
    }

    private LinkedEditingRangeResult? ParseLinkedEditingRanges(JsonElement result)
    {
        if (result.ValueKind == JsonValueKind.Null || result.ValueKind == JsonValueKind.Undefined)
            return null;

        var linked = new LinkedEditingRangeResult();

        if (result.TryGetProperty("wordPattern", out var wp))
            linked.WordPattern = wp.GetString();

        if (result.TryGetProperty("ranges", out var rangesArray) && rangesArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var range in rangesArray.EnumerateArray())
            {
                var item = new LinkedEditingRange();

                if (range.TryGetProperty("start", out var start))
                {
                    item.StartLine = start.TryGetProperty("line", out var sl) ? sl.GetInt32() + 1 : 0;
                    item.StartColumn = start.TryGetProperty("character", out var sc) ? sc.GetInt32() + 1 : 0;
                }

                if (range.TryGetProperty("end", out var end))
                {
                    item.EndLine = end.TryGetProperty("line", out var el) ? el.GetInt32() + 1 : 0;
                    item.EndColumn = end.TryGetProperty("character", out var ec) ? ec.GetInt32() + 1 : 0;
                }

                linked.Ranges.Add(item);
            }
        }

        return linked.Ranges.Count > 0 ? linked : null;
    }

    public void Dispose()
    {
        // Synchronous cleanup only — blocking on StopAsync() here could stall
        // (or deadlock) the UI thread, and previously always burned the full
        // wait timeout because the shutdown response could never be read.
        _disposed = true;
        _stopping = true;
        IsConnected = false;
        _cts?.Cancel();
        FailPendingRequests();

        try { _writer?.Dispose(); } catch { }
        try { _reader?.Dispose(); } catch { }
        _writer = null;
        _reader = null;

        if (_serverProcess != null)
        {
            try
            {
                if (!_serverProcess.HasExited)
                {
                    _serverProcess.Kill();
                }
            }
            catch { }
            try { _serverProcess.Dispose(); } catch { }
            _serverProcess = null;
        }

        _frameWriter.Dispose();
    }
}
