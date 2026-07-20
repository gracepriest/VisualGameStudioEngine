using System.Text.Json;

namespace VisualGameStudio.Core.Abstractions.Services;

/// <summary>Per-adapter timeout budgets (spec §8). Lives on the descriptor from Task 6 on.</summary>
public sealed record DapTimeoutProfile(TimeSpan Launch, TimeSpan Request, TimeSpan Step, TimeSpan DisconnectGrace)
{
    public static readonly DapTimeoutProfile Managed =
        new(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));

    /// <summary>lldb-dap can spend a long time loading symbols before launch completes.</summary>
    public static readonly DapTimeoutProfile LldbDap = Managed with { Launch = TimeSpan.FromSeconds(60) };
}

/// <summary>One entry of the adapter's <c>exceptionBreakpointFilters</c> capability.</summary>
public sealed record DapExceptionFilter(string Id, string Label, bool Default, bool SupportsCondition = false);

/// <summary>
/// The adapter's initialize response, RETAINED for the life of the session (it used to be
/// discarded the moment the response arrived). Carries the raw body for ad-hoc probes plus
/// the parsed exception filters (lldb-dap's cpp_throw/cpp_catch, the managed adapter's
/// all/uncaught) the UI needs to offer.
/// </summary>
public sealed class DapCapabilities
{
    private static readonly IReadOnlyList<DapExceptionFilter> NoFilters = Array.Empty<DapExceptionFilter>();

    private DapCapabilities(JsonElement raw, IReadOnlyList<DapExceptionFilter> exceptionBreakpointFilters)
    {
        Raw = raw;
        ExceptionBreakpointFilters = exceptionBreakpointFilters;
    }

    /// <summary>
    /// The raw initialize response body. May be Undefined when the adapter sent no body —
    /// never access it without a ValueKind guard: default(JsonElement) access throws
    /// InvalidOperationException, not JsonException (the 3a lesson).
    /// </summary>
    public JsonElement Raw { get; }

    /// <summary>The adapter's exception breakpoint filters; empty when undisclosed.</summary>
    public IReadOnlyList<DapExceptionFilter> ExceptionBreakpointFilters { get; }

    /// <summary>
    /// True only when the adapter explicitly advertised the capability as true.
    /// Absent, undefined, or non-boolean all read as unsupported (spec §3.3.3:
    /// the client skips what the adapter disclaims).
    /// </summary>
    public bool Supports(string capabilityName)
        => Raw.ValueKind == JsonValueKind.Object
           && Raw.TryGetProperty(capabilityName, out var value)
           && value.ValueKind == JsonValueKind.True;

    public static DapCapabilities Parse(JsonElement initializeResponseBody)
    {
        // The adapter may send no body at all — the correlation layer hands over
        // default(JsonElement) then, whose every accessor except ValueKind throws.
        if (initializeResponseBody.ValueKind != JsonValueKind.Object)
            return new DapCapabilities(initializeResponseBody, NoFilters);

        var filters = NoFilters;
        if (initializeResponseBody.TryGetProperty("exceptionBreakpointFilters", out var filterArray)
            && filterArray.ValueKind == JsonValueKind.Array)
        {
            var list = new List<DapExceptionFilter>();
            foreach (var entry in filterArray.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object) continue;

                // Entries: { filter, label, default?, supportsCondition? } — only the
                // id is load-bearing; a filter without one cannot be sent back.
                var id = entry.TryGetProperty("filter", out var f) && f.ValueKind == JsonValueKind.String
                    ? f.GetString()
                    : null;
                if (string.IsNullOrEmpty(id)) continue;

                var label = entry.TryGetProperty("label", out var l) && l.ValueKind == JsonValueKind.String
                    ? l.GetString() ?? id
                    : id;

                list.Add(new DapExceptionFilter(
                    id,
                    label,
                    Default: entry.TryGetProperty("default", out var d) && d.ValueKind == JsonValueKind.True,
                    SupportsCondition: entry.TryGetProperty("supportsCondition", out var sc) && sc.ValueKind == JsonValueKind.True));
            }

            if (list.Count > 0) filters = list;
        }

        return new DapCapabilities(initializeResponseBody, filters);
    }
}
