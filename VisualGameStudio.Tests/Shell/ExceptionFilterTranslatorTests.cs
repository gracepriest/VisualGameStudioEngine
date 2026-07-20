using System.Collections.Concurrent;
using System.Text.Json;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Services;
using VisualGameStudio.Shell.Services;
using VisualGameStudio.Shell.ViewModels.Dialogs;
using VisualGameStudio.Tests.Services;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// Phase 4 Task 10 — adapter-driven Exception Settings. The translator is the legacy
/// dialog-result-to-DAP mapping MOVED out of <c>MainWindowViewModel.ShowExceptionSettingsAsync</c>
/// (a refactor-with-tests, so the managed-vocabulary cases below pin the moved logic
/// exactly), plus a second mode: when the active adapter's vocabulary is NOT the classic
/// managed trio, checked rows map straight to the adapter's advertised filter ids.
/// </summary>
[TestFixture]
public class ExceptionFilterTranslatorTests
{
    private static readonly IReadOnlyList<DapExceptionFilter> ManagedTrio = new[]
    {
        new DapExceptionFilter("all", "All Exceptions", false),
        new DapExceptionFilter("uncaught", "Uncaught Exceptions", true),
        new DapExceptionFilter("thrown", "Thrown Exceptions", false, SupportsCondition: true),
    };

    private static readonly IReadOnlyList<DapExceptionFilter> CppFilters = new[]
    {
        new DapExceptionFilter("cpp_throw", "C++ Throw", false),
        new DapExceptionFilter("cpp_catch", "C++ Catch", false),
    };

    private static ExceptionSettingResult Row(string type, bool thrown, bool userUnhandled = true)
        => new() { ExceptionType = type, BreakWhenThrown = thrown, BreakWhenUserUnhandled = userUnhandled };

    [Test]
    public void Translate_ManagedVocabulary_MatchesTheLegacyMappingExactly()
    {
        // "All Exceptions" checked -> the "all" filter, nothing else.
        var (filters, options) = ExceptionFilterTranslator.Translate(
            new[] { Row("All Exceptions", thrown: true) }, ManagedTrio);
        Assert.That(filters, Is.EqualTo(new[] { "all" }));
        Assert.That(options, Is.Null, "no filter options for the all-exceptions row");

        // Category rows checked with user-unhandled -> ONE "uncaught" (deduped across
        // all three category names).
        (filters, options) = ExceptionFilterTranslator.Translate(new[]
        {
            Row("Runtime Exceptions", thrown: true, userUnhandled: true),
            Row("IO Exceptions", thrown: true, userUnhandled: true),
            Row("User Exceptions", thrown: true, userUnhandled: true),
        }, ManagedTrio);
        Assert.That(filters, Is.EqualTo(new[] { "uncaught" }));
        Assert.That(options, Is.Null);

        // A category row checked WITHOUT user-unhandled contributes nothing (legacy quirk,
        // preserved verbatim).
        (filters, options) = ExceptionFilterTranslator.Translate(
            new[] { Row("Runtime Exceptions", thrown: true, userUnhandled: false) }, ManagedTrio);
        Assert.That(filters, Is.Empty);
        Assert.That(options, Is.Null);

        // An individual exception type checked -> "thrown" + a per-type condition option.
        (filters, options) = ExceptionFilterTranslator.Translate(
            new[] { Row("NullReferenceException", thrown: true) }, ManagedTrio);
        Assert.That(filters, Is.EqualTo(new[] { "thrown" }));
        Assert.That(options, Is.Not.Null);
        Assert.That(options, Has.Count.EqualTo(1));
        Assert.That(options![0].FilterId, Is.EqualTo("thrown"));
        Assert.That(options[0].Condition, Is.EqualTo("NullReferenceException"));

        // An individual type with ONLY user-unhandled -> no filter, but an "uncaught"
        // condition option (legacy behavior, preserved verbatim).
        (filters, options) = ExceptionFilterTranslator.Translate(
            new[] { Row("OverflowException", thrown: false, userUnhandled: true) }, ManagedTrio);
        Assert.That(filters, Is.Empty);
        Assert.That(options, Is.Not.Null);
        Assert.That(options, Has.Count.EqualTo(1));
        Assert.That(options![0].FilterId, Is.EqualTo("uncaught"));
        Assert.That(options[0].Condition, Is.EqualTo("OverflowException"));

        // The combined case pins ordering and dedupe: thrown-rows first (result order),
        // then the unhandled-only sweep.
        (filters, options) = ExceptionFilterTranslator.Translate(new[]
        {
            Row("All Exceptions", thrown: true),
            Row("Runtime Exceptions", thrown: true, userUnhandled: true),
            Row("NullReferenceException", thrown: true),
            Row("IndexOutOfRangeException", thrown: true),
            Row("OverflowException", thrown: false, userUnhandled: true),
        }, ManagedTrio);
        Assert.That(filters, Is.EqualTo(new[] { "all", "uncaught", "thrown" }),
            "filter order and dedupe must match the legacy mapping");
        Assert.That(options, Is.Not.Null);
        Assert.That(options!.Select(o => (o.FilterId, o.Condition)), Is.EqualTo(new[]
        {
            ("thrown", "NullReferenceException"),
            ("thrown", "IndexOutOfRangeException"),
            ("uncaught", "OverflowException"),
        }));

        // Mode selection: ANY of all/uncaught/thrown in the available set keeps the
        // legacy mapping, even if the trio is incomplete.
        (filters, _) = ExceptionFilterTranslator.Translate(
            new[] { Row("All Exceptions", thrown: true) },
            new[] { new DapExceptionFilter("all", "All Exceptions", false) });
        Assert.That(filters, Is.EqualTo(new[] { "all" }));
    }

    [Test]
    public void Translate_AdapterFilters_MapCheckedRowsToFilterIds()
    {
        // The dialog rows carry the filter LABELS; a checked row maps to that filter's id.
        var (filters, options) = ExceptionFilterTranslator.Translate(new[]
        {
            Row("C++ Throw", thrown: true),
            Row("C++ Catch", thrown: false),
        }, CppFilters);

        Assert.That(filters, Is.EqualTo(new[] { "cpp_throw" }));
        Assert.That(options, Is.Null, "adapter-vocabulary mode never invents filter options");

        // A row naming the filter by ID matches too, and the same filter checked twice
        // (label row + id row) is sent once.
        (filters, options) = ExceptionFilterTranslator.Translate(new[]
        {
            Row("cpp_catch", thrown: true),
            Row("C++ Catch", thrown: true),
            Row("SomethingTheAdapterNeverAdvertised", thrown: true),
        }, CppFilters);

        Assert.That(filters, Is.EqualTo(new[] { "cpp_catch" }));
        Assert.That(options, Is.Null);
    }

    [Test]
    public void Translate_NothingChecked_SendsEmptyFilters()
    {
        // Empty filters must still be SENT (setExceptionBreakpoints with []) — that is
        // how server-side exception state gets cleared. Both vocabularies.
        var (filters, options) = ExceptionFilterTranslator.Translate(new[]
        {
            Row("C++ Throw", thrown: false, userUnhandled: false),
            Row("C++ Catch", thrown: false, userUnhandled: false),
        }, CppFilters);
        Assert.That(filters, Is.Empty);
        Assert.That(options, Is.Null);

        (filters, options) = ExceptionFilterTranslator.Translate(new[]
        {
            Row("All Exceptions", thrown: false, userUnhandled: false),
            Row("NullReferenceException", thrown: false, userUnhandled: false),
        }, ManagedTrio);
        Assert.That(filters, Is.Empty);
        Assert.That(options, Is.Null);
    }

    [Test]
    public void DialogVm_BuildsCategoriesFromAdapterFilters()
    {
        // A non-managed vocabulary renders EXACTLY the adapter's filters — labels as the
        // rows, filter.Default as the initial checked state, and no hardcoded managed tree.
        var filters = new[]
        {
            new DapExceptionFilter("cpp_throw", "C++ Throw", Default: false),
            new DapExceptionFilter("cpp_catch", "C++ Catch", Default: true),
        };

        var vm = new ExceptionSettingsViewModel(null, filters);

        Assert.That(vm.ExceptionCategories, Has.Count.EqualTo(2),
            "one category per advertised filter, nothing else");
        Assert.That(vm.ExceptionCategories.Select(c => c.Name),
            Is.EqualTo(new[] { "C++ Throw", "C++ Catch" }));
        Assert.That(vm.ExceptionCategories[0].BreakWhenThrown, Is.False,
            "unchecked when the filter's default is false");
        Assert.That(vm.ExceptionCategories[1].BreakWhenThrown, Is.True,
            "checked when the filter advertised default:true");
        Assert.That(vm.ExceptionCategories.Select(c => c.Name),
            Has.No.Member("User Exceptions"),
            "the User-Exceptions affordance does not exist for adapter vocabularies");

        // The add-custom-exception affordance is inert without a User Exceptions category.
        vm.CustomExceptionName = "MyException";
        vm.AddCustomExceptionCommand.Execute(null);
        Assert.That(vm.ExceptionCategories, Has.Count.EqualTo(2),
            "adding a custom exception must be impossible for adapter vocabularies");

        // A current setting wins over the filter's default.
        var vmWithSettings = new ExceptionSettingsViewModel(
            new[] { new ExceptionSetting { ExceptionType = "C++ Throw", BreakWhenThrown = true } },
            filters);
        Assert.That(vmWithSettings.ExceptionCategories[0].BreakWhenThrown, Is.True,
            "a persisted setting must win over the advertised default");

        // The classic managed trio keeps the existing hardcoded tree byte-identical.
        var managedVm = new ExceptionSettingsViewModel(null, new[]
        {
            new DapExceptionFilter("all", "All Exceptions", false),
            new DapExceptionFilter("uncaught", "Uncaught Exceptions", true),
            new DapExceptionFilter("thrown", "Thrown Exceptions", false, SupportsCondition: true),
        });
        Assert.That(managedVm.ExceptionCategories.Select(c => c.Name), Is.EqualTo(new[]
        {
            "All Exceptions", "Runtime Exceptions", "IO Exceptions", "User Exceptions",
        }), "the managed trio must keep today's hardcoded categories");
    }

    // The SHIPPED managed adapter advertises exactly TWO filters — all + uncaught, a trio
    // SUBSET (BasicLang/Debugger/DebugSession.cs initialize response). During a live managed
    // session ActiveExceptionFilters therefore serves that pair, and BOTH halves — the dialog
    // and the translator — must recognize it as the managed vocabulary. A predicate mismatch
    // here is a live defect: adapter-mode rows fed into the managed mapping turn a checked
    // "Uncaught Exceptions" row into filters:["thrown"] + a thrown-condition for a type that
    // does not exist.

    private static readonly IReadOnlyList<DapExceptionFilter> RealManagedAdapterPair = new[]
    {
        new DapExceptionFilter("all", "All Exceptions", false),
        new DapExceptionFilter("uncaught", "Uncaught Exceptions", true),
    };

    [Test]
    public void DialogVm_RealManagedAdapterPair_KeepsTheClassicTree()
    {
        var vm = new ExceptionSettingsViewModel(null, RealManagedAdapterPair);

        Assert.That(vm.ExceptionCategories.Select(c => c.Name), Is.EqualTo(new[]
        {
            "All Exceptions", "Runtime Exceptions", "IO Exceptions", "User Exceptions",
        }), "a live managed session must render the classic hardcoded tree, not two flat adapter rows");

        // And the classic path means current settings still apply through the tree walk.
        var vmWithSettings = new ExceptionSettingsViewModel(
            new[] { new ExceptionSetting { ExceptionType = "NullReferenceException", BreakWhenThrown = true } },
            RealManagedAdapterPair);
        var runtime = vmWithSettings.ExceptionCategories.First(c => c.Name == "Runtime Exceptions");
        Assert.That(runtime.Children!.First(c => c.Name == "NullReferenceException").BreakWhenThrown, Is.True,
            "persisted per-type settings must keep applying to the classic tree");
    }

    [Test]
    public void Translate_RealManagedAdapterPair_StaysInManagedMode()
    {
        // The dialog rendered the classic tree, so the rows carry the legacy category and
        // type names — and the translator must map them with the legacy mapping.
        var (filters, options) = ExceptionFilterTranslator.Translate(new[]
        {
            Row("All Exceptions", thrown: true),
            Row("Runtime Exceptions", thrown: true, userUnhandled: true),
            Row("NullReferenceException", thrown: true),
        }, RealManagedAdapterPair);

        Assert.That(filters, Is.EqualTo(new[] { "all", "uncaught", "thrown" }),
            "the real adapter's all+uncaught pair must select the MANAGED mapping");
        Assert.That(options, Is.Not.Null);
        Assert.That(options!.Select(o => (o.FilterId, o.Condition)),
            Is.EqualTo(new[] { ("thrown", "NullReferenceException") }));
    }
}

/// <summary>
/// Task 10's service half — <see cref="IDebugService.ActiveExceptionFilters"/> prefers what
/// the adapter ADVERTISED, falls back to the active descriptor's known vocabulary, and
/// defaults to the managed trio with no session at all. Plus the Task 4 carry-forward:
/// filters armed before any session exists must be REPLAYED during the next handshake's
/// configuration phase (the retention was shipped in Task 4 without a pinning test; this
/// fixture is its designated home). Driven over the REAL DebugService against the scripted
/// fake — wire assertions, not internal state. Every await is budget-bounded.
/// </summary>
[TestFixture]
public class DebugServiceActiveExceptionFilterTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(10);

    [Test]
    public async Task ActiveFilters_PreferAdvertised_FallBackToDescriptorDefaults()
    {
        var output = new RecordingOutputService();

        // Session 1's adapter advertises ONLY cpp_throw; session 2's advertises nothing.
        using var advertisingFake = new FakeDapAdapter
        {
            Timing = FakeDapAdapter.InitializedTiming.AfterLaunchRequestReceived,
            DeferLaunchResponseUntilConfigurationDone = true,
            CapabilitiesBody = new
            {
                supportsConfigurationDoneRequest = true,
                exceptionBreakpointFilters = new object[]
                {
                    new { filter = "cpp_throw", label = "C++ Throw", @default = false }
                }
            }
        };
        using var silentFake = new FakeDapAdapter
        {
            Timing = FakeDapAdapter.InitializedTiming.AfterLaunchRequestReceived,
            DeferLaunchResponseUntilConfigurationDone = true,
            CapabilitiesBody = new { supportsConfigurationDoneRequest = true }
        };

        var session1 = new DapSession(advertisingFake.SessionReads, advertisingFake.SessionWrites, output);
        var session2 = new DapSession(silentFake.SessionReads, silentFake.SessionWrites, output);
        var sessions = new Queue<DapSession>(new[] { session1, session2 });

        var registry = new DebugAdapterRegistry();
        var lldb = DebugAdapterDescriptor.LldbDap(() => @"C:\fake\tools\lldb-dap.exe");
        registry.Register(lldb);

        var service = new DebugService(output, registry, (_, _) => sessions.Dequeue());
        try
        {
            // No session ever: the managed trio is the only sane vocabulary to offer.
            Assert.That(service.ActiveExceptionFilters.Select(f => f.Id),
                Is.EqualTo(new[] { "all", "uncaught", "thrown" }),
                "with no session the dialog must offer the managed fallback vocabulary");

            var config = new DebugConfiguration
            {
                Program = "FakeGame.exe",
                WorkingDirectory = Path.GetTempPath(),
                AdapterId = DebugAdapterDescriptor.LldbDapId
            };

            var started = await WithTimeout(service.StartDebuggingAsync(config),
                "the advertising-adapter handshake");
            Assert.That(started, Is.True, "session 1 handshake failed:\n" + output.Dump());

            // Advertised beats everything — including the descriptor's richer fallback.
            Assert.That(service.ActiveExceptionFilters.Select(f => (f.Id, f.Label)),
                Is.EqualTo(new[] { ("cpp_throw", "C++ Throw") }),
                "the adapter's ADVERTISED filters must win when non-empty");

            await WithTimeout(service.StopDebuggingAsync(), "stopping session 1");

            started = await WithTimeout(service.StartDebuggingAsync(config),
                "the silent-adapter handshake");
            Assert.That(started, Is.True, "session 2 handshake failed:\n" + output.Dump());

            // Nothing advertised: the ACTIVE descriptor's known vocabulary, not the managed trio.
            Assert.That(service.ActiveExceptionFilters, Is.EqualTo(lldb.FallbackExceptionFilters),
                "an adapter that discloses no filters falls back to its descriptor's vocabulary");
            Assert.That(service.ActiveExceptionFilters.Select(f => f.Id),
                Is.EqualTo(new[] { "cpp_throw", "cpp_catch" }));
        }
        finally
        {
            service.Dispose();
            session1.Dispose();
            session2.Dispose();
        }
    }

    [Test]
    public async Task ArmedFilters_PreSession_ReplayedDuringConfigurationPhase()
    {
        // Task 4 carry-forward: SetExceptionBreakpointsAsync with NO live session retains
        // the filters, and the next handshake's configuration phase replays them — i.e.
        // the adapter must receive setExceptionBreakpoints BEFORE configurationDone.
        var output = new RecordingOutputService();
        using var fake = FakeDapAdapter.ManagedShaped();
        var session = new DapSession(fake.SessionReads, fake.SessionWrites, output);
        var service = new DebugService(output, sessionFactory: (_, _) => session);

        try
        {
            await service.SetExceptionBreakpointsAsync(
                new[] { "uncaught" },
                new[] { new ExceptionFilterOption { FilterId = "uncaught", Condition = "OverflowException" } });
            Assert.That(fake.Received, Is.Empty,
                "no session exists yet — nothing may reach any adapter");

            var started = await WithTimeout(service.StartDebuggingAsync(new DebugConfiguration
            {
                Program = "FakeApp.exe",
                WorkingDirectory = Path.GetTempPath()
            }), "the handshake that should replay the armed filters");
            Assert.That(started, Is.True, "handshake failed:\n" + output.Dump());

            var received = fake.Received.ToArray();
            var exceptionIndex = Array.FindIndex(received, r => r.Command == "setExceptionBreakpoints");
            var configDoneIndex = Array.FindIndex(received, r => r.Command == "configurationDone");

            Assert.That(exceptionIndex, Is.GreaterThanOrEqualTo(0),
                "the armed filters were never replayed; the adapter saw: " +
                string.Join(", ", received.Select(r => r.Command)));
            Assert.That(configDoneIndex, Is.GreaterThanOrEqualTo(0), "the handshake never completed");
            Assert.That(exceptionIndex, Is.LessThan(configDoneIndex),
                "armed filters must ride the CONFIGURATION phase, before configurationDone");

            var args = received[exceptionIndex].Arguments;
            Assert.That(args.TryGetProperty("filters", out var wireFilters), Is.True,
                "setExceptionBreakpoints carried no filters: " + args.GetRawText());
            Assert.That(wireFilters.EnumerateArray().Select(f => f.GetString()),
                Is.EqualTo(new[] { "uncaught" }),
                "the wire filters must be exactly what was armed pre-session");
            Assert.That(args.TryGetProperty("filterOptions", out var wireOptions), Is.True,
                "the armed filter options must replay too: " + args.GetRawText());
            Assert.That(wireOptions.EnumerateArray().Select(o => o.GetProperty("condition").GetString()),
                Is.EqualTo(new[] { "OverflowException" }));
        }
        finally
        {
            service.Dispose();
            session.Dispose();
        }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static async Task<T> WithTimeout<T>(Task<T> task, string what)
    {
        var completed = await Task.WhenAny(task, Task.Delay(Budget));
        if (completed != task)
            Assert.Fail($"Timed out after {Budget.TotalSeconds:F0}s waiting for: {what}");
        return await task;
    }

    private static async Task WithTimeout(Task task, string what)
    {
        var completed = await Task.WhenAny(task, Task.Delay(Budget));
        if (completed != task)
            Assert.Fail($"Timed out after {Budget.TotalSeconds:F0}s waiting for: {what}");
        await task;
    }

    /// <summary>Duplicated per suite convention (the siblings elsewhere are private).</summary>
    private sealed class RecordingOutputService : IOutputService
    {
        private readonly ConcurrentQueue<string> _lines = new();

        public string Dump() => string.Join(Environment.NewLine, _lines);

        public void WriteLine(string message, OutputCategory category = OutputCategory.General) => _lines.Enqueue(message);
        public void Write(string message, OutputCategory category = OutputCategory.General) => _lines.Enqueue(message);
        public void WriteError(string message, OutputCategory category = OutputCategory.General) => _lines.Enqueue("ERROR: " + message);
        public void Clear(OutputCategory category) { }
        public void ClearAll() { }
        public void Activate(OutputCategory category) { }
        public IReadOnlyList<string> GetMessages(OutputCategory category) => _lines.ToArray();
        public event EventHandler<OutputEventArgs>? OutputReceived { add { } remove { } }
        public IOutputChannel CreateChannel(string name) => throw new NotSupportedException();
        public IOutputChannel? GetChannel(string name) => null;
        public IReadOnlyList<IOutputChannel> Channels => Array.Empty<IOutputChannel>();
        public IOutputChannel? ActiveChannel { get; set; }
        public event EventHandler<string>? ChannelCreated { add { } remove { } }
        public event EventHandler<IOutputChannel?>? ActiveChannelChanged { add { } remove { } }
        public void ShowOutput() { }
    }
}
