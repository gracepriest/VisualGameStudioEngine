using System.Collections.ObjectModel;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Utilities;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// Routes documents to the language server that owns them, and owns those servers' lifetime.
/// <para>
/// Holds the services it is given; it never constructs one. See
/// <see cref="ILanguageServiceRegistry"/> for why that is load-bearing rather than a style
/// preference (a service built outside the DI container is never disposed and orphans its
/// server process on every exit).
/// </para>
/// </summary>
public sealed class LanguageServiceRegistry : ILanguageServiceRegistry
{
    /// <summary>
    /// The registered servers. A <see cref="ReadOnlyCollection{T}"/>, not the backing array, so
    /// <see cref="All"/> can hand it out directly: <c>IReadOnlyList&lt;T&gt;</c> is a read-only
    /// VIEW, not a read-only object — a caller who downcast <c>All</c> back to <c>ILanguageService[]</c>
    /// could swap a server out from under the registry. Same defence <c>LanguageFileTypes</c>
    /// applies to its routing arrays; this class must not hand out a mutable handle to its own state.
    /// </summary>
    private readonly ReadOnlyCollection<ILanguageService> _services;

    /// <summary>
    /// languageId → the one server serving it. Built once: routing is a hot path
    /// (<see cref="GetFor"/> runs on keystrokes) and the map cannot change — a descriptor is
    /// pure identity and services are fixed at construction.
    /// </summary>
    private readonly Dictionary<string, ILanguageService> _byLanguageId;

    private bool _disposed;

    /// <summary>
    /// The workspace root the servers were last started for, or null before the first start.
    /// Read only under <see cref="_lifecycleLock"/>.
    /// </summary>
    private string? _startedRoot;

    /// <summary>
    /// Serializes <see cref="StartAllAsync"/>/<see cref="StopAllAsync"/> against each other.
    /// <para>
    /// Re-rooting is a read-modify-write over <see cref="_startedRoot"/> spanning a multi-second
    /// stop-then-start, so two overlapping project opens could otherwise interleave into servers
    /// rooted at different projects — the exact state this class exists to prevent, arrived at
    /// from the other side. Uncontended in practice (project open is a UI-thread event).
    /// </para>
    /// <para>
    /// Deliberately never disposed. <see cref="SemaphoreSlim.Dispose"/> only matters once
    /// <c>AvailableWaitHandle</c> has been touched, which nothing here does — while disposing it
    /// from <see cref="Dispose"/> (which runs on shutdown, possibly with a start still in flight)
    /// would turn a benign race into an <see cref="ObjectDisposedException"/> on a best-effort
    /// cleanup path.
    /// </para>
    /// </summary>
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);

    /// <param name="services">
    /// One service per language server, already constructed by the container.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="services"/> is empty, or two services claim the same language id.
    /// </exception>
    public LanguageServiceRegistry(IEnumerable<ILanguageService> services)
    {
        ArgumentNullException.ThrowIfNull(services);

        _services = new ReadOnlyCollection<ILanguageService>(services.ToArray());

        // An empty registry is not a degenerate case that "just routes nothing" — it is a DI
        // mistake that presents as every IntelliSense feature in the IDE quietly doing nothing,
        // with no error to connect it to its cause. Fail at startup instead.
        if (_services.Count == 0)
        {
            throw new ArgumentException(
                "A language service registry with no servers routes every document to nothing — " +
                "every IntelliSense feature would silently do nothing. Register at least the " +
                "BasicLang service.",
                nameof(services));
        }

        _byLanguageId = BuildRoutingMap(_services);
    }

    /// <summary>
    /// The language id → server map, and the guard that exactly one server serves each language.
    /// </summary>
    /// <remarks>
    /// ⚠ Duplicate language ids are not merely ambiguous — the way they arise is the two-process
    /// bug. Registering BasicLang twice means two <c>dotnet --lsp</c> children, of which only one
    /// is ever routed to, started and stopped; the other lives and dies with the IDE process as an
    /// orphan. Neither routing nor a passing test suite would notice. Since
    /// <see cref="LanguageServerDescriptor.Extensions"/> is derived from the same language ids,
    /// this also covers two servers claiming one extension.
    /// </remarks>
    private static Dictionary<string, ILanguageService> BuildRoutingMap(IReadOnlyList<ILanguageService> services)
    {
        var map = new Dictionary<string, ILanguageService>(StringComparer.Ordinal);

        foreach (var service in services)
        {
            foreach (var languageId in service.Descriptor.LanguageIds)
            {
                if (map.TryGetValue(languageId, out var existing))
                {
                    throw new ArgumentException(
                        $"Language servers '{existing.Descriptor.Id}' and '{service.Descriptor.Id}' both " +
                        $"claim the language '{languageId}', so routing it is ambiguous — and if these are " +
                        "the same server registered twice, one of the two server processes is already " +
                        "orphaned. Register exactly one service per language.",
                        nameof(services));
                }

                map[languageId] = service;
            }
        }

        return map;
    }

    /// <inheritdoc />
    public IReadOnlyList<ILanguageService> All => _services;

    /// <inheritdoc />
    public ILanguageService? GetFor(string? path)
    {
        // LanguageFileTypes is the single source of truth for who owns a file — asking each
        // descriptor's Owns() in turn would answer from the same map, one indirection later.
        var languageId = LanguageFileTypes.GetLspLanguageId(path);
        if (languageId is null) return null;

        // Not every routed language has a registered server: C++ has none until clangd is
        // registered, and none on a machine where clangd was never found.
        return _byLanguageId.TryGetValue(languageId, out var service) ? service : null;
    }

    /// <inheritdoc />
    public ILanguageService? GetById(string id)
    {
        // Linear over the (tiny, fixed) service set: this is a lifecycle-event call, not the
        // GetFor keystroke hot path, so it does not warrant a second lookup map.
        foreach (var service in _services)
        {
            if (string.Equals(service.Descriptor.Id, id, StringComparison.Ordinal)) return service;
        }

        return null;
    }

    /// <inheritdoc />
    public bool IsConnectedFor(string? path) => GetFor(path)?.IsConnected ?? false;

    /// <inheritdoc />
    public async Task StartAllAsync(string workspaceRoot, CancellationToken cancellationToken = default)
    {
        // Checked before anything starts, so a rootless call cannot leave half the servers up.
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            throw new ArgumentException(
                "Language servers must be started with a workspace root. A rootless clangd omits " +
                "--compile-commands-dir entirely, never finds obj/compile_commands.json, and answers " +
                "from guessed compiler flags — with no error anywhere. Start the servers when a " +
                "project opens (IProjectService.ProjectOpened), not before one is known.",
                nameof(workspaceRoot));
        }

        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // THE PROJECT SWITCH. StartAsync no-ops on an already-connected server and does NOT
            // re-root it, so opening project B while the servers are up for project A would leave
            // clangd reading project A's obj/compile_commands.json — answering completions and
            // diagnostics that look right and are for the wrong project, with no error anywhere.
            // Re-rooting means stop, THEN start.
            //
            // Ordinal, and against the raw root: the two ways to be wrong are not symmetric. A
            // false "different" costs one needless restart; a false "same" leaves a server pinned
            // to the old project silently. Ordinal only ever errs the safe way (the root comes
            // from Project.ProjectDirectory and is stable per project, so it does not misfire in
            // practice).
            var reRooting = _startedRoot != null &&
                            !string.Equals(_startedRoot, workspaceRoot, StringComparison.Ordinal);

            // Set before the work: we are committed to this root either way, and a start that
            // throws part-way must not leave the registry believing it is still rooted where it
            // was — the next open would then skip the re-root it needs.
            _startedRoot = workspaceRoot;

            var failures = await ForEachServerAsync(async service =>
            {
                if (reRooting)
                {
                    // Per-server stop-then-start rather than two bulk phases, so that a server
                    // which cannot stop is reported as a failure and NOT restarted. Restarting it
                    // would no-op (it is still connected) and silently pin it to the old root
                    // while we reported success — the very bug being fixed. Each server is
                    // independent, so one refusing to stop cannot cost the others their re-root.
                    await service.StopAsync().ConfigureAwait(false);
                }

                await service.StartAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
            }).ConfigureAwait(false);

            if (failures.Count > 0)
            {
                throw new AggregateException(
                    $"{failures.Count} of {_services.Count} language server(s) failed to start.", failures);
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task StopAllAsync()
    {
        await _lifecycleLock.WaitAsync().ConfigureAwait(false);
        try
        {
            // No servers are rooted anywhere now, so the next start must not think it can skip
            // the re-root.
            _startedRoot = null;

            var failures = await ForEachServerAsync(service => service.StopAsync()).ConfigureAwait(false);

            if (failures.Count > 0)
            {
                throw new AggregateException(
                    $"{failures.Count} of {_services.Count} language server(s) failed to stop.", failures);
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    /// <summary>
    /// Runs <paramref name="operation"/> against every server and returns whatever it threw.
    /// </summary>
    /// <remarks>
    /// Every server is attempted even when one fails — the servers are independent, and one that
    /// cannot start (or hangs on shutdown) must not cost the others. Concurrently, because
    /// <c>StartAsync</c> is a multi-second handshake per server and serializing them would add
    /// that latency to project open for no reason; the services share no state, which is the whole
    /// premise of one service per descriptor.
    /// </remarks>
    private async Task<IReadOnlyList<Exception>> ForEachServerAsync(Func<ILanguageService, Task> operation)
    {
        var results = await Task.WhenAll(_services.Select(async service =>
        {
            try
            {
                // Awaited INSIDE the try so a synchronous throw from `operation` is caught too —
                // outside it, one server throwing before it returned its Task would abort the
                // Select and leave the remaining servers untouched.
                await operation(service).ConfigureAwait(false);
                return null as Exception;
            }
            catch (Exception ex)
            {
                return ex;
            }
        })).ConfigureAwait(false);

        return results.OfType<Exception>().ToArray();
    }

    /// <summary>
    /// Disposes every server, which is what kills their child processes.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Never throws</b>, and swallows what each service's Dispose throws. This runs inside
    /// the DI container's disposal loop on <c>App.ShutdownRequested</c>: an exception escaping
    /// here aborts that loop, so every singleton the container has not disposed yet — including
    /// any other server in this very registry — leaks. On a best-effort cleanup path that is
    /// strictly worse than losing the message. The failure is not silent in practice either: a
    /// service that cannot be disposed leaves a live child process, which is visible.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var service in _services)
        {
            try
            {
                service.Dispose();
            }
            catch
            {
                // Deliberate: see above. The next server still gets disposed.
            }
        }
    }
}
