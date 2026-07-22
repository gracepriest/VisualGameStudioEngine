using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace VisualGameStudio.Tests;

/// <summary>
/// Pins Task 10 — <c>MainWindowViewModel.StartDebuggingAsync</c> must resolve the
/// per-backend debugger override (Settings > C++) for a native, pinned-backend project
/// before falling to the default lldb-dap/adapter probe chain, and must abort F5 outright
/// (never silently fall back) when the configured path is invalid.
///
/// <see cref="VisualGameStudio.Shell.ViewModels.MainWindowViewModel"/> is DI-only and never
/// constructed in this suite (~40 services), so this is a SOURCE GUARD — it reads the
/// source text directly, mirroring BuildSolutionAmplifierGuardTests.cs's FindRepoFile /
/// ReadMainWindowViewModelSource / ExtractMethodBody pattern. The pure tri-state
/// <c>CppToolchainOverrides.ResolveDebugger</c> behavior (blank/usable/invalid) is already
/// covered by CppToolchainOverridesTests — this guard only pins that MWVM's F5 site wires
/// it in, not the resolver logic itself.
/// </summary>
[TestFixture]
public class MwvmDebuggerOverrideSourceGuardTests
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

    private static string? ReadMainWindowViewModelSource()
    {
        var path = FindRepoFile("VisualGameStudio.Shell", "ViewModels", "MainWindowViewModel.cs");
        if (path == null)
        {
            Assert.Ignore("MainWindowViewModel.cs not found from the test base directory — skipping source guard.");
            return null;
        }
        return File.ReadAllText(path);
    }

    /// <summary>Extracts a method's full body (braces included) by brace-depth scanning, so the
    /// guard doesn't depend on which method happens to follow it in the file.</summary>
    private static string ExtractMethodBody(string src, string methodSignatureNeedle)
    {
        var startIdx = src.IndexOf(methodSignatureNeedle, StringComparison.Ordinal);
        Assert.That(startIdx, Is.GreaterThanOrEqualTo(0), $"'{methodSignatureNeedle}' not found in source.");

        var braceStart = src.IndexOf('{', startIdx);
        Assert.That(braceStart, Is.GreaterThan(startIdx), "Could not find the method body's opening brace.");

        var depth = 0;
        var i = braceStart;
        for (; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}')
            {
                depth--;
                if (depth == 0) break;
            }
        }
        Assert.That(i, Is.LessThan(src.Length), "Could not find the method body's closing brace.");
        return src.Substring(braceStart, i - braceStart + 1);
    }

    [Test]
    public void StartDebugging_Resolves_Per_Backend_Debugger_Override()
    {
        var src = ReadMainWindowViewModelSource();
        if (src == null) return;

        var body = ExtractMethodBody(src, "private async Task StartDebuggingAsync()");

        Assert.That(body, Does.Contain("ResolveDebugger"),
            "StartDebuggingAsync must resolve the per-backend debugger override via " +
            "CppToolchainOverrides.ResolveDebugger before falling to the default adapter probe chain.");
        Assert.That(body, Does.Contain("AdapterExecutableOverride"),
            "StartDebuggingAsync must thread the resolved override path onto " +
            "DebugConfiguration.AdapterExecutableOverride.");
        Assert.That(body, Does.Contain("IsNativeBuild"),
            "The debugger override must only be resolved for a native project — non-native " +
            "projects keep the default (managed) adapter chain untouched.");
    }

    [Test]
    public void StartDebugging_Aborts_On_Invalid_Override_Without_Silent_Fallback()
    {
        var src = ReadMainWindowViewModelSource();
        if (src == null) return;

        var body = ExtractMethodBody(src, "private async Task StartDebuggingAsync()");

        Assert.That(body, Does.Contain("debugger path is invalid"),
            "An Invalid per-backend debugger override must abort F5 with a message naming " +
            "the bad path — never silently fall back to the default probe chain.");
        Assert.That(body, Does.Match(@"OverrideState\.Invalid[\s\S]{0,400}return;"),
            "The Invalid branch must return (abort F5) rather than proceed.");
    }

    [Test]
    public void StartDebugging_InstalledCheck_Treats_Override_As_Installed()
    {
        var src = ReadMainWindowViewModelSource();
        if (src == null) return;

        var body = ExtractMethodBody(src, "private async Task StartDebuggingAsync()");

        Assert.That(body, Does.Contain("debuggerOverridePath is null"),
            "The installed-check must only report the adapter missing when there is no " +
            "usable override AND the default probe chain also finds nothing.");
    }
}
