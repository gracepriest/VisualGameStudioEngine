using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

/// <summary>
/// Guards the extension contribution-loading path against the sync-over-async deadlock that
/// froze the IDE on the first-ever successful extension install.
///
/// The original shape: <c>ExtensionService.LoadContributions</c> was a synchronous void that
/// called <c>LoadGrammarsFromExtension</c>, which did
/// <c>_textMateRegistrar.RegisterFromExtensionAsync(...).GetAwaiter().GetResult()</c>.
/// <c>RegisterFromExtensionAsync</c> awaits <c>File.ReadAllTextAsync</c> without
/// <c>ConfigureAwait(false)</c>, so its continuation is posted back to the captured
/// SynchronizationContext. Reached from a RelayCommand on Avalonia's UI thread, the UI thread
/// blocked on a task whose continuation needed the UI thread — a permanent freeze.
///
/// It fired for every extension including pure themes, because the grammar loader runs
/// unconditionally, and it fired during discovery (before activation), so a folder with any
/// extension in it was enough.
/// </summary>
[TestFixture]
public class ExtensionContributionsAsyncTests
{
    /// <summary>
    /// A single-threaded SynchronizationContext that models Avalonia's UI thread: posted
    /// continuations run only when the owning thread pumps them.
    /// </summary>
    private sealed class SingleThreadContext : SynchronizationContext
    {
        private readonly BlockingCollection<(SendOrPostCallback cb, object? state)> _queue = new();

        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

        public void PumpUntil(Func<bool> done, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (!done() && DateTime.UtcNow < deadline)
            {
                if (_queue.TryTake(out var work, TimeSpan.FromMilliseconds(25)))
                {
                    work.cb(work.state);
                }
            }
        }
    }

    private static string CreateThemeOnlyExtension()
    {
        // A pure theme: no "grammars" contribution, mirroring Dracula. The grammar loader still
        // runs for it, which is why the deadlock was not limited to language extensions.
        var dir = Path.Combine(Path.GetTempPath(), "vgs-ext-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "package.json"), """
        {
          "name": "theme-test",
          "publisher": "test",
          "version": "1.0.0",
          "contributes": {
            "themes": [ { "label": "Test Theme", "uiTheme": "vs-dark", "path": "./theme.json" } ]
          }
        }
        """);
        return dir;
    }

    /// <summary>
    /// Pins the deadlock MECHANISM rather than the hang itself: asserting that a blocking call
    /// times out would be racy (a cached file read can complete synchronously and never post a
    /// continuation). Instead this proves the continuation resumes on the captured context —
    /// which is exactly what makes blocking that thread fatal.
    /// </summary>
    [Test]
    public void RegisterFromExtensionAsync_ResumesOnTheCapturedContext()
    {
        var dir = CreateThemeOnlyExtension();
        try
        {
            var registrar = new TextMateRegistrar(Mock.Of<ITextMateService>());
            var ctx = new SingleThreadContext();
            var original = SynchronizationContext.Current;

            int? resumedOnPumpThread = null;
            var finished = false;
            var pumpThreadId = Environment.CurrentManagedThreadId;

            try
            {
                SynchronizationContext.SetSynchronizationContext(ctx);

                _ = Task.Run(() => { }); // keep the analyzer honest about mixed contexts

                async Task Drive()
                {
                    await registrar.RegisterFromExtensionAsync(dir, "test.theme-test");
                    resumedOnPumpThread = Environment.CurrentManagedThreadId;
                    finished = true;
                }

                var driving = Drive();
                ctx.PumpUntil(() => finished, TimeSpan.FromSeconds(10));

                Assert.That(finished, Is.True,
                    "RegisterFromExtensionAsync did not complete while its context was being pumped.");
                Assert.That(driving.IsCompleted, Is.True);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(original);
            }

            Assert.That(resumedOnPumpThread, Is.EqualTo(pumpThreadId),
                "The continuation resumed on the pumping thread, i.e. the awaits capture the " +
                "SynchronizationContext. Any caller that BLOCKS this thread waiting on the returned " +
                "task therefore deadlocks — which is why ExtensionService must await this call, " +
                "never GetAwaiter().GetResult() it.");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Source guard for the fix itself. ExtensionService's extensions directory is hard-coded to
    /// ~/.vgs/extensions (ExtensionService.cs ~:76), so a behavioural test at that layer would
    /// write into the developer's real profile. Asserting on the source text is the same tactic
    /// the suite already uses for DI-only types (see BuildSolutionAmplifierGuardTests).
    /// </summary>
    [Test]
    public void ExtensionService_DoesNotBlockOnTheGrammarRegistrar()
    {
        var path = FindRepoFile("VisualGameStudio.ProjectSystem", "Services", "ExtensionService.cs");
        if (path == null)
        {
            Assert.Ignore("ExtensionService.cs not found from the test base directory — skipping source guard.");
            return;
        }

        var src = File.ReadAllText(path);

        Assert.That(src, Does.Not.Contain("GetAwaiter().GetResult()"),
            "ExtensionService must not block on an async call. The contribution loader runs on the UI " +
            "thread via the Extensions panel's Install command, and the awaited work resumes on the " +
            "captured context — blocking it freezes the IDE permanently.");

        Assert.That(src, Does.Contain("await LoadContributionsAsync("),
            "Contribution loading must be awaited at its call sites rather than run synchronously.");
    }

    private static string? FindRepoFile(params string[] relativeParts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
