using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Services;

/// <summary>
/// Guards the two defects that kept the Node extension host from EVER starting through the IDE.
///
/// <para>Commit 23a631c fixed the layer above this: <c>onLanguage</c> was never fired, so activation
/// never ran. Its test comment records the then-current belief that "firing it is what starts the
/// host". That belief was wrong, and this fixture exists because the IDE proved it: with the event
/// firing correctly, ESLint still logged <c>Activated (static only)</c>.</para>
///
/// <para>Source guards, not behavioural tests: exercising these paths for real starts a Node.js
/// child process. Same rationale and precedent as ExtensionActivationWiringTests.</para>
/// </summary>
[TestFixture]
public class ExtensionHostStartupTests
{
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

    private static string Source()
    {
        var path = FindRepoFile("VisualGameStudio.ProjectSystem", "Services", "ExtensionService.cs");
        Assert.That(path, Is.Not.Null, "could not locate ExtensionService.cs");
        return File.ReadAllText(path!);
    }

    /// <summary>Extracts a method body by brace matching, so assertions are scoped to one method.</summary>
    private static string MethodBody(string src, string signature)
    {
        var idx = src.IndexOf(signature, StringComparison.Ordinal);
        Assert.That(idx, Is.GreaterThanOrEqualTo(0), $"could not find '{signature}'");

        var braceStart = src.IndexOf('{', idx);
        Assert.That(braceStart, Is.GreaterThanOrEqualTo(0));

        var depth = 0;
        var i = braceStart;
        for (; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}' && --depth == 0) break;
        }
        Assert.That(i, Is.LessThan(src.Length), "unbalanced braces while extracting the method body");
        return src.Substring(braceStart, i - braceStart + 1);
    }

    /// <summary>
    /// BUG 10. Activating an extension that has a JS entry point must START the host when it is not
    /// already running.
    ///
    /// <para>StartExtensionHostAsync had exactly two callers in the solution: RestartExtensionHostAsync
    /// (itself uncalled) and the crash-restart handler, which can only fire if the host was ALREADY
    /// running. So nothing ever started it, and ActivateAsync — the one place that naturally would —
    /// saw <c>IsRunning != true</c> and returned "static only" instead. That is bug 8's shape one
    /// layer down: a guard whose precondition nothing establishes.</para>
    /// </summary>
    [Test]
    public void ActivatingAnExtensionWithAJsEntryPoint_StartsTheHost()
    {
        var body = MethodBody(Source(), "public async Task<bool> ActivateAsync(");

        Assert.That(body, Does.Contain("StartExtensionHostAsync"),
            "ActivateAsync must start the extension host when an extension has a `main` and the "
            + "host is not yet running. Without this nothing in the IDE ever starts it, so every "
            + "extension with a JS entry point degrades to 'static only' forever.");
    }

    /// <summary>
    /// BUG 10, ordering. The start attempt must come BEFORE the static-only fallback, or the
    /// fallback's early return makes the start unreachable — the same closed loop, relocated.
    /// </summary>
    [Test]
    public void TheHostStartAttempt_PrecedesTheStaticOnlyFallback()
    {
        var body = MethodBody(Source(), "public async Task<bool> ActivateAsync(");

        var startIdx = body.IndexOf("StartExtensionHostAsync", StringComparison.Ordinal);
        var staticOnlyIdx = body.IndexOf("static only", StringComparison.Ordinal);

        Assert.That(startIdx, Is.GreaterThanOrEqualTo(0), "no host start attempt at all");
        Assert.That(staticOnlyIdx, Is.GreaterThanOrEqualTo(0),
            "premise: the static-only fallback still exists as the degraded path");
        Assert.That(startIdx, Is.LessThan(staticOnlyIdx),
            "the host must be started before falling back to static-only; after it, the fallback's "
            + "early return makes the start unreachable");
    }

    /// <summary>
    /// BUG 11. <c>onStartupFinished</c> is one of VS Code's most common activation events — it is
    /// ESLint's ONLY one — and had zero occurrences anywhere in the repo. An extension declaring it
    /// could never activate, no matter what else worked.
    /// </summary>
    [Test]
    public void StartupFiresTheOnStartupFinishedActivationEvent()
    {
        var body = MethodBody(Source(), "public async Task ActivateStaticContributionsAsync(");

        Assert.That(body, Does.Contain("onStartupFinished"),
            "startup must fire onStartupFinished once discovery is complete; it is the only "
            + "activation event many real extensions declare, ESLint among them");
    }
}
