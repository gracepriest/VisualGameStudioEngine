using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Services;

/// <summary>
/// Keeps the JS→C# request surface honest.
///
/// <para>The extension host's JS calls the IDE two ways, and they fail in OPPOSITE ways when no
/// handler exists. A <c>sendNotification</c> is fire-and-forget: a missing handler is a silent
/// no-op, so an output channel simply never clears and nothing is logged. A <c>sendRequest</c>
/// returns a promise held in <c>_pendingRequests</c> (rpc.js:53); a missing handler produces a
/// JSON-RPC error response, which REJECTS that promise — usually inside an extension's activate(),
/// where it takes the whole activation down.</para>
///
/// <para>So the requests are the ones that actively break extensions, and this test enumerates them
/// from the JS itself rather than from anyone's memory of the list. Every <c>sendRequest</c> must
/// either have a handler or be named in <see cref="KnownUnimplemented"/> — which makes the remaining
/// gap explicit and, because the second test fails once an entry becomes registered, forces the list
/// to SHRINK rather than rot.</para>
/// </summary>
[TestFixture]
public class ExtensionHostRequestCoverageTests
{
    /// <summary>
    /// Requests the JS can send that the IDE does not answer yet. Each one rejects into the calling
    /// extension today. Remove an entry when its handler lands — the second test enforces that.
    /// </summary>
    private static readonly HashSet<string> KnownUnimplemented = new(StringComparer.Ordinal)
    {
        "configuration/update",
        "workspace/openTextDocument",
        "workspace/findFiles",
        "workspace/saveAll",
        "executeCommand",
        "getCommands",
        "secrets/get",
        "secrets/store",
        "secrets/delete",
        "window/showQuickPick",
        "window/showInputBox",
        "window/showOpenDialog",
        "window/showSaveDialog",
        "window/showTextDocument",
        "env/clipboardRead",
        "env/clipboardWrite",
        "env/openExternal",
        "languages/getLanguages",
        "languages/setLanguage",
        "treeView/reveal",
        "webview/postMessage",
        "debug/startDebugging",
        "debug/stopDebugging",
        "tasks/executeTask",
    };

    private static DirectoryInfo? RepoDir(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (Directory.Exists(candidate)) return new DirectoryInfo(candidate);
            dir = dir.Parent;
        }
        return null;
    }

    private static string? RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    private static HashSet<string> RequestsSentByTheHost()
    {
        var jsRoot = RepoDir(Path.Combine("VisualGameStudio.ProjectSystem", "Services", "ExtensionHost"));
        if (jsRoot == null)
        {
            Assert.Ignore("extension-host JS not found from the test base directory — skipping.");
        }

        var found = new HashSet<string>(StringComparer.Ordinal);
        var pattern = new Regex(@"sendRequest\(\s*['""]([^'""]+)['""]", RegexOptions.Compiled);

        foreach (var file in jsRoot!.GetFiles("*.js", SearchOption.AllDirectories))
        {
            foreach (Match m in pattern.Matches(File.ReadAllText(file.FullName)))
            {
                found.Add(m.Groups[1].Value);
            }
        }

        return found;
    }

    private static HashSet<string> MethodsRegisteredByTheIde()
    {
        var host = RepoFile("VisualGameStudio.ProjectSystem", "Services", "ExtensionHost.cs");
        if (host == null)
        {
            Assert.Ignore("ExtensionHost.cs not found from the test base directory — skipping.");
        }

        var pattern = new Regex(@"AddLocalRpcMethod\(\s*""([^""]+)""", RegexOptions.Compiled);

        return pattern.Matches(File.ReadAllText(host!))
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Every request the JS can send is either answered or explicitly acknowledged as a gap. A new
    /// <c>sendRequest</c> added to the JS without a handler fails here instead of rejecting inside
    /// somebody's extension at runtime.
    /// </summary>
    [Test]
    public void EveryRequestIsEitherHandledOrAKnownGap()
    {
        var sent = RequestsSentByTheHost();
        var handled = MethodsRegisteredByTheIde();

        Assert.That(sent, Is.Not.Empty, "sanity: the JS must send some requests");

        var unaccounted = sent.Where(m => !handled.Contains(m) && !KnownUnimplemented.Contains(m))
                              .OrderBy(m => m, StringComparer.Ordinal)
                              .ToList();

        Assert.That(unaccounted, Is.Empty,
            "these requests have no C# handler and are not listed as known gaps, so they reject "
            + "into the calling extension: " + string.Join(", ", unaccounted));
    }

    /// <summary>
    /// ⛔ The gap list must SHRINK. Once a handler is registered its entry has to go, or the list
    /// becomes a record of what was once missing rather than what is missing — and the next person
    /// reading it plans work that is already done.
    /// </summary>
    [Test]
    public void TheKnownGapListContainsNothingAlreadyImplemented()
    {
        var handled = MethodsRegisteredByTheIde();

        var stale = KnownUnimplemented.Where(handled.Contains)
                                      .OrderBy(m => m, StringComparer.Ordinal)
                                      .ToList();

        Assert.That(stale, Is.Empty,
            "these are registered in ExtensionHost but still listed as unimplemented — remove them "
            + "from KnownUnimplemented: " + string.Join(", ", stale));
    }

    /// <summary>
    /// The workspace.fs family specifically — the slice just implemented. Pinned by name because
    /// these eight are what an extension touches first, and a regression here is invisible until an
    /// extension fails to activate.
    /// </summary>
    [Test]
    public void TheWorkspaceFileSystemFamilyIsHandled()
    {
        var handled = MethodsRegisteredByTheIde();

        foreach (var method in new[]
                 {
                     "workspace/fs/readFile", "workspace/fs/writeFile", "workspace/fs/stat",
                     "workspace/fs/readDirectory", "workspace/fs/createDirectory",
                     "workspace/fs/delete", "workspace/fs/rename", "workspace/fs/copy",
                 })
        {
            Assert.That(handled, Does.Contain(method),
                $"{method} must be registered — workspace.fs is the first thing most extensions use");
        }
    }
}
