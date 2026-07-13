using System;
using System.Collections.Generic;
using System.Linq;

namespace VisualGameStudio.Core.Abstractions.Services;

/// <summary>
/// Process-wide registry mapping each settings key to the consumer(s) that actually read it and
/// act on it. Every setting exposed by the Tools → Settings dialog must name at least one consumer
/// here so the Phase 3 contract test can fail the build when a dialog setting persists but nothing
/// consumes it (the "persists-but-dead" class of defect that motivated this whole plan).
///
/// Each consumer registers itself once, at its own initialization (a static constructor, a
/// view-model constructor, a service constructor, etc.) via a single <see cref="RegisterConsumer"/>
/// line next to the code that reads the setting.
///
/// Thread-safe: registrations can arrive from UI-thread control/view-model initializers and from
/// background service constructors concurrently. Multiple consumers per key are allowed — their
/// descriptions are combined (de-duplicated, insertion order preserved).
///
/// <para><b>Lazy-initialization failure mode (read before writing the Phase 3 contract test):</b>
/// registration only happens when a consumer's type/instance initializer actually RUNS. In a
/// headless test process, a consumer type that was never loaded looks unregistered even though its
/// wiring is fine — the contract test must force initialization first:
/// <list type="bullet">
/// <item><description><b>Static-ctor registrants</b> — force with
/// <c>RuntimeHelpers.RunClassConstructor(typeof(T).TypeHandle)</c>: <c>CodeEditorDocumentView</c>
/// (editor.fontFamily / fontSize / fontLigatures / lineNumbers / wordWrap / tabSize / insertSpaces
/// / highlightCurrentLine / stickyScroll.enabled / bracketPairColorization / autoClosingBrackets /
/// smoothScrolling / minimap.enabled / renderWhitespace) and <c>ThemeManager</c>
/// (workbench.colorTheme).</description></item>
/// <item><description><b>Instance-ctor registrants</b> — an instance must be constructed:
/// <c>MainWindowViewModel</c> (editor.trimTrailingWhitespaceOnSave / formatOnSave / tabSize /
/// insertSpaces / minimap.enabled / renderWhitespace), <c>AutoSaveService</c> (files.autoSave /
/// autoSaveDelay / autoSaveSkipOnErrors), <c>HotExitService</c> (files.hotExit). Where
/// construction is too heavy for a headless test (MainWindowViewModel needs ~40 services), note
/// that all of its keys except formatOnSave and trimTrailingWhitespaceOnSave are also registered
/// by the <c>CodeEditorDocumentView</c> static ctor — the contract test must cover those two keys
/// by another route (e.g. a dedicated one-line registration assertion or a lightweight seam).
/// </description></item>
/// </list>
/// Keep this list in sync when adding consumers in Tasks 2.2+.</para>
/// </summary>
public static class SettingsConsumerRegistry
{
    private static readonly object _lock = new();

    // key -> distinct consumer descriptions in registration order.
    private static readonly Dictionary<string, List<string>> _consumers =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Records that <paramref name="consumerDescription"/> reads and acts on <paramref name="key"/>.
    /// Idempotent: registering the same (key, description) pair again is a no-op; registering a
    /// second, different description for the same key appends it (both are reported by
    /// <see cref="Consumers"/>, combined with "; ").
    /// </summary>
    /// <param name="key">The dot-notation settings key (e.g. <c>editor.tabSize</c>).</param>
    /// <param name="consumerDescription">
    /// A short human-readable description of the consumer site (e.g.
    /// <c>CodeEditorDocumentView.ApplyEditorSettings → editor IndentationSize</c>).
    /// </param>
    public static void RegisterConsumer(string key, string consumerDescription)
    {
        if (string.IsNullOrWhiteSpace(key)) return;

        lock (_lock)
        {
            if (!_consumers.TryGetValue(key, out var descriptions))
            {
                descriptions = new List<string>();
                _consumers[key] = descriptions;
            }

            if (!string.IsNullOrWhiteSpace(consumerDescription) &&
                !descriptions.Contains(consumerDescription))
            {
                descriptions.Add(consumerDescription);
            }
        }
    }

    /// <summary>
    /// A snapshot of every registered key mapped to its combined consumer description(s).
    /// Multiple descriptions for one key are joined with "; ".
    /// </summary>
    public static IReadOnlyDictionary<string, string> Consumers
    {
        get
        {
            lock (_lock)
            {
                return _consumers.ToDictionary(
                    kvp => kvp.Key,
                    kvp => string.Join("; ", kvp.Value),
                    StringComparer.Ordinal);
            }
        }
    }

    /// <summary>
    /// True if at least one consumer has registered for <paramref name="key"/>.
    /// </summary>
    public static bool IsRegistered(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        lock (_lock)
        {
            return _consumers.ContainsKey(key);
        }
    }

    // Deliberately append-only: there is no Clear(). Consumers register once (from type
    // initializers / constructors that run at most once per process) and their entries must stay
    // for the lifetime of the process. A Clear() would let one test wipe registrations that a
    // once-run static constructor would never rebuild, silently breaking the Phase 3 contract test
    // depending on test order. Tests achieve isolation by using unique keys, not by clearing.
}
