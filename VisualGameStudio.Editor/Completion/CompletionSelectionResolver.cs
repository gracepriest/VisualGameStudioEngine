using CoreCompletionItem = VisualGameStudio.Core.Abstractions.Services.CompletionItem;

namespace VisualGameStudio.Editor.Completion;

/// <summary>
/// Coordinates lazy <c>completionItem/resolve</c> requests as the completion-list selection
/// moves: at most one resolve in flight, keyed to the CURRENT selection. Moving the selection
/// cancels the previous request's token, and a reply that still lands for a de-selected row is
/// discarded (selection-token compare) — a slow server can never paint item A's documentation
/// onto item B.
///
/// <para>
/// Headless-testable ON PURPOSE: no Avalonia types anywhere in its signature — the resolve
/// call is an injected delegate (the document's already-routed
/// <c>ILanguageService.ResolveCompletionAsync</c> in production) and the only UI entanglement
/// is the <c>descriptionUpdated</c> callback, where CodeEditorControl re-pokes the AvaloniaEdit
/// tooltip. Driven directly by ClientCompletionResolveTests.
/// </para>
///
/// <para>
/// Deliberately independent of <see cref="CompletionSession"/>'s pending-request gate
/// (CompletionSession.HasPendingRequest): that gate suppresses new completion REQUESTS while
/// one is outstanding, and routing per-selection resolves through it would starve typing.
/// Resolve is per-selection enrichment; completion is per-session — they never share state.
/// </para>
/// </summary>
public sealed class CompletionSelectionResolver
{
    // Both are touched only from the caller's context (the UI thread in production — the
    // await below has no ConfigureAwait(false), so the apply continuation comes back on the
    // same context and never races these fields).
    private CancellationTokenSource? _cts;
    private CompletionData? _current;

    /// <summary>
    /// Called on every selection change of the completion list (including null when the
    /// selection was cleared or moved to a non-LSP row). Cancels the previous selection's
    /// in-flight resolve, then — when the newly selected row carries a resolve token — fires
    /// <paramref name="resolve"/> in the background and, if the reply is still for the current
    /// selection, applies the merged description to <paramref name="selected"/> and announces
    /// it through <paramref name="descriptionUpdated"/>.
    /// Returns the in-flight work so tests can await it deterministically; the UI discards it
    /// (fire-and-forget — selection movement and typing are never blocked).
    /// </summary>
    public Task OnSelectionChanged(
        CompletionData? selected,
        Func<CoreCompletionItem, CancellationToken, Task<CoreCompletionItem>>? resolve,
        Action<CompletionData>? descriptionUpdated = null)
    {
        // Re-announcing the SAME still-current row is a no-op: its resolve is either in
        // flight or already applied. This is also the feedback-loop breaker — the UI's
        // tooltip refresh re-raises SelectionChanged for the row that was just enriched,
        // and without this guard that refire would resolve → repaint → refire forever.
        if (selected != null && ReferenceEquals(selected, _current)) return Task.CompletedTask;

        // The selection moved: whatever was in flight is now stale. The CTS is deliberately
        // NOT disposed here — the in-flight resolve still holds its token, and cancel-then-
        // drop lets it observe the cancellation safely.
        _cts?.Cancel();
        _cts = null;
        _current = selected;

        if (selected == null || resolve == null) return Task.CompletedTask;

        // Free short-circuit: no data token = nothing the server could resolve against
        // (S0.3 measured: that is EVERY clangd completion today — resolveProvider false and
        // no data on its items — so C++ selection moves cost nothing here). The capability
        // half of the gate lives inside ResolveCompletionAsync (ShouldResolve).
        var item = selected.SourceItem;
        if (item?.Data is null) return Task.CompletedTask;

        var cts = new CancellationTokenSource();
        _cts = cts;
        return ResolveThenApplyAsync(selected, item, resolve, cts, descriptionUpdated);
    }

    /// <summary>
    /// Cancels any in-flight resolve and forgets the tracked selection — called when the
    /// completion window closes; a reply for a list nobody can see must never be applied.
    /// </summary>
    public void Cancel()
    {
        _cts?.Cancel();
        _cts = null;
        _current = null;
    }

    private async Task ResolveThenApplyAsync(
        CompletionData selected,
        CoreCompletionItem item,
        Func<CoreCompletionItem, CancellationToken, Task<CoreCompletionItem>> resolve,
        CancellationTokenSource cts,
        Action<CompletionData>? descriptionUpdated)
    {
        CoreCompletionItem resolved;
        try
        {
            resolved = await resolve(item, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // ResolveCompletionAsync rethrows ONLY caller-initiated cancellation — ours,
            // meaning the selection moved on. Exactly the reply to drop.
            return;
        }
        catch
        {
            // UI boundary: ResolveCompletionAsync already degrades every failure to the
            // original item; anything that still escapes must never surface out of a
            // selection move.
            return;
        }

        // Stale-drop, both halves: the per-selection token (cancelled the moment the
        // selection moved) and the selection-token compare (the reply may have been in
        // transit when the cancel landed — cancellation cannot recall it).
        if (cts.IsCancellationRequested) return;
        if (!ReferenceEquals(_current, selected)) return;

        // Same instance back = the gate was off or the server had nothing (the pinned
        // Task 12 contract) — the list already shows everything there is.
        if (ReferenceEquals(resolved, item)) return;

        // Announce only a REAL change: a new-instance reply whose merged description
        // equals what the row already shows would otherwise buy a tooltip repaint plus a
        // SelectionChanged refire for zero visible difference.
        if (selected.UpdateDescription(
            CompletionData.BuildDescription(resolved.Detail, resolved.Documentation)))
        {
            descriptionUpdated?.Invoke(selected);
        }
    }
}
