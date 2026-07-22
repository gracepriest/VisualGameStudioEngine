using System;
using System.IO;
using System.Linq;

namespace VisualGameStudio.ProjectSystem.Services;

public enum ToolchainSlotKind { Compiler, Debugger }
public enum ToolchainPathStatus { Empty, Valid, Warning, Invalid }

public readonly record struct VersionProbeResult(bool Ran, bool Ok, string? Version);

public readonly record struct ValidationResult(
    ToolchainPathStatus Status, string Message, string? DetectedVersion, string? ResolvedPath)
{
    public bool Usable => Status is ToolchainPathStatus.Valid or ToolchainPathStatus.Warning;
}

/// <summary>
/// Pure validator for a user-configured compiler/debugger path. Existence is the
/// authoritative gate; a recognized-driver basename additionally enables a --version
/// smoke (never run on an unrecognized binary). Mirrors LanguageService.ResolveLspPathOverride's
/// injectable-existence shape so it is headless-testable.
/// </summary>
public static class ToolchainPathValidator
{
    private static readonly string[] LlvmDrivers = { "clang++", "clang", "clang-cl" };
    private static readonly string[] GccDrivers  = { "g++", "c++", "gcc" };
    private static readonly string[] DapDrivers   = { "lldb-dap" };

    /// <summary>
    /// Validates a user-configured compiler/debugger path. Existence (via
    /// <paramref name="fileExists"/>/<paramref name="dirExists"/>, defaulting to
    /// <see cref="File.Exists(string)"/>/<see cref="Directory.Exists(string)"/>) is the
    /// authoritative gate; a recognized driver basename additionally becomes eligible for a
    /// <c>--version</c> smoke.
    /// </summary>
    /// <param name="backendId">"llvm" | "gcc" | "msvc" (case-insensitive).</param>
    /// <param name="kind">Whether <paramref name="path"/> is a compiler or debugger slot.</param>
    /// <param name="path">The user-configured path; blank/whitespace/null yields <see cref="ToolchainPathStatus.Empty"/>.</param>
    /// <param name="fileExists">Injectable existence probe; defaults to <see cref="File.Exists(string)"/>.</param>
    /// <param name="dirExists">Injectable directory-existence probe; defaults to <see cref="Directory.Exists(string)"/>.</param>
    /// <param name="versionProbe">
    /// OPT-IN <c>--version</c> smoke. Null (the default) means no process is ever spawned — a
    /// recognized, existing binary resolves to <see cref="ToolchainPathStatus.Warning"/> rather
    /// than <see cref="ToolchainPathStatus.Valid"/>. Build and probe callers must always pass
    /// null; only the Settings dialog's background enrichment passes <see cref="RealVersionProbe"/>.
    /// </param>
    public static ValidationResult Validate(
        string? backendId, ToolchainSlotKind kind, string? path,
        Func<string, bool>? fileExists = null,
        Func<string, bool>? dirExists = null,
        Func<string, VersionProbeResult>? versionProbe = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new(ToolchainPathStatus.Empty, "", null, null);

        var exists = fileExists ?? File.Exists;
        var dirs   = dirExists ?? Directory.Exists;
        var trimmed = path.Trim();
        var id = backendId?.Trim().ToLowerInvariant();

        // MSVC compiler slot: must resolve to a vcvars64.bat (directly or via a VS-install dir).
        if (kind == ToolchainSlotKind.Compiler && id == "msvc")
        {
            var vcvars = ResolveVcvars(trimmed, exists, dirs);
            return vcvars != null
                ? new(ToolchainPathStatus.Valid, "vcvars64.bat found", null, vcvars)
                : new(ToolchainPathStatus.Invalid,
                    $"Not a vcvars64.bat or Visual Studio install directory: {trimmed}",
                    null, null);
        }

        if (!exists(trimmed))
            return new(ToolchainPathStatus.Invalid, $"File not found: {trimmed}", null, null);

        // Existence passed. Enrichment: only smoke a recognized driver basename.
        var basename = Path.GetFileNameWithoutExtension(trimmed).ToLowerInvariant();
        var recognized = RecognizedFor(id, kind).Contains(basename);

        // msvc.debugger is honestly limited even when the binary is fine.
        var pdbAdvisory = kind == ToolchainSlotKind.Debugger && id == "msvc"
            ? " lldb-dap can't read MSVC PDB — breakpoints may not bind." : "";

        if (!recognized)
            return new(ToolchainPathStatus.Warning, ("Found; unrecognized name — using anyway." + pdbAdvisory).Trim(),
                null, trimmed);

        // Version smoke is OPT-IN: null => no --version spawn. Build/probe/instant-dialog
        // callers pass nothing (existence is the gate); only the dialog's BACKGROUND
        // enrichment passes RealVersionProbe. This keeps builds and the wizard probe from
        // ever spawning a process, and gives the dialog its instant red/green + later version.
        var vr = versionProbe?.Invoke(trimmed) ?? new VersionProbeResult(Ran: false, Ok: false, Version: null);
        if (vr.Ran && vr.Ok)
        {
            // recognized + smoked clean; msvc.debugger stays Warning (PDB) despite a clean smoke.
            return pdbAdvisory.Length > 0
                ? new(ToolchainPathStatus.Warning, ("Found." + pdbAdvisory).Trim(), vr.Version, trimmed)
                : new(ToolchainPathStatus.Valid, "Valid.", vr.Version, trimmed);
        }
        return new(ToolchainPathStatus.Warning, ("Found; couldn't confirm version — using anyway." + pdbAdvisory).Trim(),
            null, trimmed);
    }

    /// <summary>Resolve a vcvars64.bat from a .bat path or a VS-install dir; null if neither.</summary>
    public static string? ResolveVcvars(string path, Func<string, bool> exists, Func<string, bool> dirs)
    {
        if (path.EndsWith("vcvars64.bat", StringComparison.OrdinalIgnoreCase) && exists(path))
            return path;
        if (dirs(path))
        {
            var derived = Path.Combine(path, "VC", "Auxiliary", "Build", "vcvars64.bat");
            if (exists(derived)) return derived;
        }
        return null;
    }

    private static string[] RecognizedFor(string? id, ToolchainSlotKind kind) =>
        kind == ToolchainSlotKind.Debugger ? DapDrivers
        : id == "gcc" ? GccDrivers
        : id == "llvm" ? LlvmDrivers
        : Array.Empty<string>();

    /// <summary>The real --version smoke. Public so the Settings dialog's background
    /// enrichment can opt in; build/probe callers pass null (no spawn).</summary>
    public static VersionProbeResult RealVersionProbe(string exePath)
    {
        try
        {
            // Stderr is deliberately NOT redirected (mirrors ClangdLocator's vswhere probe):
            // RedirectStandardError without draining it (ErrorDataReceived + BeginErrorReadLine)
            // fills the ~4KB pipe buffer and wedges a chatty binary forever — see
            // LanguageService.BuildStartInfo's remarks. Only stdout is captured here.
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exePath, "--version")
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
            var outText = p!.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(3000))
            {
                try { p.Kill(); } catch { /* best effort */ }
                return new(true, false, null);
            }
            var ok = p.ExitCode == 0;
            return new(true, ok, ok ? outText.Split('\n').FirstOrDefault()?.Trim() : null);
        }
        catch { return new(true, false, null); }
    }
}
