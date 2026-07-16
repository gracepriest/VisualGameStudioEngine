using System;
using System.IO;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Utilities;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// Decides which clangd executable the IDE should talk to: the <c>cpp.clangd.path</c> override when
/// the user has set one that exists, otherwise whatever is on PATH, otherwise nothing.
///
/// <para>The rule is <see cref="LanguageService.ResolveLspPathOverride"/> — the same one
/// <c>basiclang.lsp.path</c> already follows — with the auto-probe folded in, so callers get the
/// final answer rather than a two-step decision to re-implement.</para>
///
/// <para><b>Null is a real answer.</b> clangd does not ship with the IDE and is not installed on
/// every machine. Returning null (rather than the bare name <c>"clangd"</c>, or a guessed path)
/// keeps "no C++ IntelliSense available" a fact the caller can act on up front, rather than
/// deferring it to a spawn that fails.</para>
/// </summary>
public static class ClangdLocator
{
    /// <summary>
    /// The name to look for on PATH. Bare, with no <c>.exe</c>: <see cref="ExecutableLocator"/>
    /// applies PATHEXT, so this one name is correct on Windows and POSIX alike.
    /// </summary>
    public const string ClangdExecutableName = "clangd";

    /// <summary>
    /// Resolves clangd's path from settings, registering this class as the consumer of
    /// <see cref="LanguageServerDescriptor.ClangdSettingsKey"/> — the registration and the read it
    /// claims sit together on purpose (see <see cref="SettingsConsumerRegistry"/>).
    /// </summary>
    /// <param name="settingsService">
    /// Source of the override; null means "no override configured", not an error — the probe still runs.
    /// </param>
    /// <returns>The clangd path, or null when neither the override nor PATH yields one.</returns>
    public static string? Locate(ISettingsService? settingsService)
    {
        SettingsConsumerRegistry.RegisterConsumer(
            LanguageServerDescriptor.ClangdSettingsKey,
            "ClangdLocator → clangd executable path override for the C++ language server");

        return ResolveClangdPath(
            settingsService?.Get<string>(LanguageServerDescriptor.ClangdSettingsKey, ""));
    }

    /// <summary>
    /// The resolution rule: a non-empty <paramref name="configuredPath"/> naming an existing file
    /// wins (trimmed); otherwise <paramref name="pathProbe"/> decides. A configured path that does
    /// NOT exist falls through to the probe rather than failing — a stale override left behind by an
    /// uninstall should degrade to auto-detection.
    /// </summary>
    /// <param name="fileExists">
    /// Existence probe for the override; defaults to <see cref="File.Exists(string)"/>.
    /// </param>
    /// <param name="pathProbe">
    /// The auto-detection step; defaults to searching PATH for <see cref="ClangdExecutableName"/>.
    /// </param>
    /// <remarks>
    /// Pure and static with both dependencies injectable, matching
    /// <see cref="LanguageService.ResolveLspPathOverride"/>: it is the only seam these rules can be
    /// pinned through (the assembly exposes no internals to the test project), and clangd is absent
    /// from most dev machines — a probe hardwired to the real PATH could only ever be asserted null.
    /// </remarks>
    public static string? ResolveClangdPath(
        string? configuredPath,
        Func<string, bool>? fileExists = null,
        Func<string?>? pathProbe = null)
    {
        var overridePath = LanguageService.ResolveLspPathOverride(configuredPath, fileExists);
        if (overridePath != null) return overridePath;

        var probe = pathProbe ?? (() => ExecutableLocator.Find(ClangdExecutableName));
        return probe();
    }
}
