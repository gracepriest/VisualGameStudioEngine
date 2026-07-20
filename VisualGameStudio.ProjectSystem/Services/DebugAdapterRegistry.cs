using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// The debug-adapter roster (Phase 4 Task 8): a registration-ordered descriptor list
/// under a lock. See <see cref="IDebugAdapterRegistry"/> for the contracts; the one
/// implementation note worth having here is what this class does NOT do — it never
/// resolves a launch command, never spawns anything, and holds nothing disposable.
/// Whoever starts a session (DebugService) asks the descriptor at that moment.
/// </summary>
public sealed class DebugAdapterRegistry : IDebugAdapterRegistry
{
    private readonly object _lock = new();
    private readonly List<DebugAdapterDescriptor> _descriptors = new();

    /// <inheritdoc />
    public IReadOnlyList<DebugAdapterDescriptor> All
    {
        get
        {
            // Snapshot, not the live list: callers may enumerate while an extension
            // registers on another thread.
            lock (_lock) { return _descriptors.ToArray(); }
        }
    }

    /// <inheritdoc />
    public void Register(DebugAdapterDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        lock (_lock)
        {
            // Ordinal: ids are wire-stable tokens, not display text — "LLDB-DAP" is a
            // DIFFERENT (if ill-advised) id, not a collision.
            if (_descriptors.Any(d => string.Equals(d.Id, descriptor.Id, StringComparison.Ordinal)))
            {
                throw new ArgumentException(
                    $"A debug adapter with id '{descriptor.Id}' is already registered. " +
                    "One id, one registration — a duplicate would leave a second adapter " +
                    "nothing routes to.",
                    nameof(descriptor));
            }

            _descriptors.Add(descriptor);
        }
    }

    /// <inheritdoc />
    public DebugAdapterDescriptor? GetFor(BasicLangProject? project)
    {
        if (project == null) return null;

        lock (_lock)
        {
            // First registered whose Serves says yes — registration order IS the
            // priority order, exactly like the LSP registry's extension routing.
            return _descriptors.FirstOrDefault(d => d.Serves(project));
        }
    }

    /// <inheritdoc />
    public DebugAdapterDescriptor? GetById(string id)
    {
        lock (_lock)
        {
            return _descriptors.FirstOrDefault(
                d => string.Equals(d.Id, id, StringComparison.Ordinal));
        }
    }
}
