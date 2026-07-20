using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.Shell.Services;

/// <summary>
/// Translates the Exception Settings dialog's result rows into the DAP
/// <c>setExceptionBreakpoints</c> arguments, in the vocabulary of the ACTIVE adapter
/// (<see cref="IDebugService.ActiveExceptionFilters"/>). Two modes:
///
/// <para>
/// <b>Managed vocabulary</b> — the available set contains any of the classic
/// <c>all</c>/<c>uncaught</c>/<c>thrown</c> ids: the legacy mapping, moved VERBATIM out of
/// <c>MainWindowViewModel.ShowExceptionSettingsAsync</c> (a refactor-with-tests; the dialog's
/// hardcoded category names are load-bearing there and pinned by the translator tests).
/// </para>
///
/// <para>
/// <b>Adapter vocabulary</b> — anything else (lldb-dap's <c>cpp_throw</c>/<c>cpp_catch</c>):
/// each checked row IS one of the adapter's advertised filters (the dialog rendered them),
/// so it maps straight to that filter's id. No invented options — the adapter never
/// advertised a conditional vocabulary the IDE could speak.
/// </para>
/// </summary>
public static class ExceptionFilterTranslator
{
    /// <summary>
    /// Maps dialog rows to <c>(filters, filterOptions)</c> for
    /// <see cref="IDebugService.SetExceptionBreakpointsAsync"/>. Options is null exactly
    /// when the legacy call site would have passed null (none collected).
    /// </summary>
    public static (List<string> Filters, List<ExceptionFilterOption>? Options) Translate(
        IReadOnlyList<ExceptionSettingResult> results,
        IReadOnlyList<DapExceptionFilter> available)
    {
        if (available.Any(f => f.Id is "all" or "uncaught" or "thrown"))
            return TranslateManaged(results);

        // Adapter vocabulary: a checked row names an advertised filter (by the label the
        // dialog rendered, or its id), and contributes that filter's id once.
        var checkedIds = new List<string>();
        foreach (var setting in results.Where(s => s.BreakWhenThrown))
        {
            var match = available.FirstOrDefault(f =>
                f.Label == setting.ExceptionType || f.Id == setting.ExceptionType);
            if (match != null && !checkedIds.Contains(match.Id))
            {
                checkedIds.Add(match.Id);
            }
        }

        return (checkedIds, null);
    }

    /// <summary>
    /// The legacy managed mapping — the body below is MainWindowViewModel.cs's former
    /// inline block, moved verbatim (only <c>result</c> renamed to <c>results</c>).
    /// </summary>
    private static (List<string> Filters, List<ExceptionFilterOption>? Options) TranslateManaged(
        IReadOnlyList<ExceptionSettingResult> results)
    {
        // Apply exception breakpoints to debug service
        var filters = new List<string>();
        var filterOptions = new List<ExceptionFilterOption>();

        foreach (var setting in results.Where(s => s.BreakWhenThrown))
        {
            if (setting.ExceptionType == "All Exceptions")
            {
                filters.Add("all");
            }
            else if (setting.ExceptionType == "Runtime Exceptions" ||
                     setting.ExceptionType == "IO Exceptions" ||
                     setting.ExceptionType == "User Exceptions")
            {
                // Category-level filter: add "uncaught" if user-unhandled is also set
                if (setting.BreakWhenUserUnhandled && !filters.Contains("uncaught"))
                {
                    filters.Add("uncaught");
                }
            }
            else
            {
                // Individual exception type: send as a filter option with condition
                if (!filters.Contains("thrown"))
                {
                    filters.Add("thrown");
                }
                filterOptions.Add(new ExceptionFilterOption
                {
                    FilterId = "thrown",
                    Condition = setting.ExceptionType
                });
            }
        }

        // Also add user-unhandled filter for settings that only have BreakWhenUserUnhandled
        foreach (var setting in results.Where(s => !s.BreakWhenThrown && s.BreakWhenUserUnhandled))
        {
            if (setting.ExceptionType != "All Exceptions" &&
                setting.ExceptionType != "Runtime Exceptions" &&
                setting.ExceptionType != "IO Exceptions" &&
                setting.ExceptionType != "User Exceptions")
            {
                filterOptions.Add(new ExceptionFilterOption
                {
                    FilterId = "uncaught",
                    Condition = setting.ExceptionType
                });
            }
        }

        return (filters, filterOptions.Count > 0 ? filterOptions : null);
    }
}
