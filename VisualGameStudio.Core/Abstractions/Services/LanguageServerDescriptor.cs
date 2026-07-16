using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VisualGameStudio.Core.Utilities;

namespace VisualGameStudio.Core.Abstractions.Services;

/// <summary>
/// The identity of one language server: who it is, which files it owns, and what to launch.
/// Everything <c>LanguageService</c> used to hardcode about BasicLang lives here instead, so
/// one client class can drive N servers.
///
/// <para>
/// <b>PURE IDENTITY — no project-scoped state (design decision D1).</b> A descriptor is
/// constructed once, through DI at app startup, when no project is open and no project
/// directory is knowable. Anything project-scoped — clangd's
/// <c>--compile-commands-dir=&lt;projectDir&gt;/obj</c> above all — is DERIVED from the
/// <c>workspaceRoot</c> passed to <see cref="ArgumentsFor"/> at launch time, never stored.
/// The rejected alternative (build the descriptor lazily when a project opens) puts the
/// service outside the DI container, which is the never-disposed / orphaned-process bug the
/// registry exists to prevent.
/// </para>
///
/// <para>
/// <b>Not a record, deliberately.</b> It carries a launch-argument delegate, for which
/// value equality would compare delegate references — a meaningless answer that "descriptors
/// are records" would invite callers to rely on. Nothing compares descriptors; they are
/// compared by <see cref="Id"/> if at all.
/// </para>
///
/// <para>
/// <b>The constructor is private on purpose.</b> Every descriptor must come from a factory
/// below, because <see cref="Extensions"/> is derived from
/// <see cref="LanguageFileTypes.LspExtensionsFor"/> rather than hand-listed — the routing map
/// is the single source of truth for which extensions a language owns, and a hand-listed copy
/// here is how a server silently stops owning a file type.
/// </para>
/// </summary>
public sealed class LanguageServerDescriptor
{
    /// <summary>
    /// Settings key overriding the BasicLang compiler path used for the spawned
    /// <c>--lsp</c> server. A const because it must be read BEFORE the descriptor it
    /// configures can be built.
    /// </summary>
    public const string BasicLangSettingsKey = "basiclang.lsp.path";

    /// <summary>Settings key overriding the discovered clangd executable path.</summary>
    public const string ClangdSettingsKey = "cpp.clangd.path";

    /// <summary>
    /// The compilation-database directory, relative to the project directory. MUST match
    /// where <c>CompileCommandsWriter.Write</c> puts <c>compile_commands.json</c>
    /// (<c>&lt;projectDir&gt;/obj/compile_commands.json</c>) — clangd reads exactly what the
    /// build writes, or the editor and the build disagree about flags with no error anywhere.
    /// If that ever moves, both move together.
    /// </summary>
    private const string CompileCommandsDirectoryName = "obj";

    private readonly Func<string?, string> _argumentsFor;

    private LanguageServerDescriptor(
        string id,
        string displayName,
        IReadOnlyList<string> languageIds,
        string serverPath,
        string fileName,
        Func<string?, string> argumentsFor,
        string settingsKey)
    {
        Id = id;
        DisplayName = displayName;
        LanguageIds = languageIds;
        ServerPath = serverPath;
        FileName = fileName;
        SettingsKey = settingsKey;
        _argumentsFor = argumentsFor;
        Extensions = languageIds.SelectMany(LanguageFileTypes.LspExtensionsFor).ToArray();
    }

    /// <summary>Stable identifier for this server — <c>basiclang</c>, <c>clangd</c>.</summary>
    /// <remarks>
    /// This is the SERVER's id, not a language id: clangd's is "clangd" while the language it
    /// serves is "cpp". Do not send it to a server.
    /// </remarks>
    public string Id { get; }

    /// <summary>Human-readable name for logs, the status bar and notifications.</summary>
    public string DisplayName { get; }

    /// <summary>
    /// The LSP language ids this server serves, as <see cref="LanguageFileTypes.GetLspLanguageId"/>
    /// reports them. A list because a server can own more than one language (clangd will own
    /// "c" as well as "cpp" the day <c>.c</c> is routed) — which is precisely why
    /// <see cref="LanguageIdFor"/> reads the FILE instead of answering with a constant.
    /// </summary>
    public IReadOnlyList<string> LanguageIds { get; }

    /// <summary>
    /// Every file extension this server owns. Derived from <see cref="LanguageIds"/> via
    /// <see cref="LanguageFileTypes.LspExtensionsFor"/> — never hand-listed.
    /// </summary>
    public IReadOnlyList<string> Extensions { get; }

    /// <summary>
    /// The file that must exist on disk for this server to be launchable.
    /// <para>
    /// <b>Not the same as <see cref="FileName"/>.</b> BasicLang launches
    /// <c>dotnet &lt;BasicLang.dll&gt;</c>: the thing to probe for is the managed assembly,
    /// while FileName is "dotnet" — resolved via PATH, for which <c>File.Exists</c> is always
    /// false. For clangd the two coincide.
    /// </para>
    /// </summary>
    public string ServerPath { get; }

    /// <summary>The executable to start — <see cref="System.Diagnostics.ProcessStartInfo.FileName"/>.</summary>
    public string FileName { get; }

    /// <summary>
    /// The settings key that overrides this server's <see cref="ServerPath"/>. Resolving it is
    /// the job of whoever BUILDS the descriptor (the path must be known before there is a
    /// descriptor to hold it), so this is here to be reported, not read behind a caller's back.
    /// </summary>
    public string SettingsKey { get; }

    /// <summary>
    /// The command-line arguments to launch this server with, for a session rooted at
    /// <paramref name="workspaceRoot"/>.
    /// </summary>
    /// <param name="workspaceRoot">
    /// The workspace root, <b>already normalized</b> by <c>LanguageService.BuildStartInfo</c> —
    /// null when there is no usable root, otherwise a trimmed path. This method deliberately
    /// does NOT re-normalize: the trim rule has exactly one home (LanguageService's
    /// <c>NormalizeWorkspaceRoot</c>, shared with the working directory and the <c>initialize</c>
    /// wire shape), and a second copy of it here is how two rules that merely happen to agree
    /// get started.
    /// </param>
    public string ArgumentsFor(string? workspaceRoot) => _argumentsFor(workspaceRoot);

    /// <summary>
    /// Whether this server owns <paramref name="path"/> — i.e. whether the routing map assigns
    /// the file to a language this server serves. False for a file no server owns (<c>.txt</c>)
    /// AND for a file another server owns (<c>.cpp</c>, asked of BasicLang).
    /// </summary>
    public bool Owns(string? path) => ResolveLanguageId(path) != null;

    /// <summary>
    /// The LSP <c>languageId</c> to announce <paramref name="path"/> to this server with —
    /// read from the FILE via <see cref="LanguageFileTypes.GetLspLanguageId"/>, never a
    /// per-server constant.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The file is not one this server owns. Deliberately loud, and deliberately NOT nullable:
    /// <list type="bullet">
    /// <item><description>answering with this server's own id would tell the server a lie it
    /// cannot detect — the exact "null silently becomes basiclang" trap the routing map is
    /// documented against;</description></item>
    /// <item><description>a <c>string?</c> return would flow a null into the <c>didOpen</c>
    /// payload unchallenged — CS8604 is in <c>NoWarn</c> repo-wide (see
    /// <see cref="LanguageFileTypes"/>) and the serializer OMITS null members, producing a
    /// malformed notification and no error anywhere.</description></item>
    /// </list>
    /// Ask <see cref="Owns"/> first; a file reaching the wrong server is a routing bug, and
    /// callers that cannot crash (fire-and-forget notifications) must report it themselves.
    /// </exception>
    public string LanguageIdFor(string path) =>
        ResolveLanguageId(path) ??
        throw new ArgumentException(
            $"The {DisplayName} language server does not own '{path}' — it owns " +
            $"{string.Join(", ", Extensions)}. Route with LanguageServerDescriptor.Owns / " +
            "LanguageFileTypes.GetLspLanguageId before announcing a document.",
            nameof(path));

    /// <summary>
    /// The one ownership rule, shared by <see cref="Owns"/> and <see cref="LanguageIdFor"/> so
    /// the question and the answer can never disagree: the routing map's id for this file, when
    /// it is one this server serves; otherwise null.
    /// </summary>
    private string? ResolveLanguageId(string? path)
    {
        var languageId = LanguageFileTypes.GetLspLanguageId(path);
        return languageId != null && LanguageIds.Contains(languageId) ? languageId : null;
    }

    /// <summary>
    /// BasicLang's own language server: <c>dotnet &lt;compilerPath&gt; --lsp</c>.
    /// </summary>
    /// <param name="compilerPath">
    /// Path to <c>BasicLang.dll</c>, already resolved (override or probe) by the caller.
    /// </param>
    public static LanguageServerDescriptor BasicLang(string compilerPath) => new(
        id: "basiclang",
        displayName: "BasicLang",
        languageIds: new[] { "basiclang" },
        serverPath: compilerPath,
        // The compiler is a managed assembly, so the process to start is the .NET host and the
        // assembly is its first argument. Quoted — the IDE is routinely installed under a path
        // with spaces.
        fileName: "dotnet",
        argumentsFor: _ => $"\"{compilerPath}\" --lsp",
        settingsKey: BasicLangSettingsKey);

    /// <summary>
    /// clangd, the C++ language server.
    /// </summary>
    /// <param name="clangdPath">
    /// Absolute path to the clangd executable, already discovered by the caller. clangd is
    /// spawned by absolute path — never by bare name.
    /// </param>
    /// <remarks>
    /// Takes NO project state: see D1 on the class. The compilation database is derived from the
    /// workspace root at launch.
    /// </remarks>
    public static LanguageServerDescriptor Clangd(string clangdPath) => new(
        id: "clangd",
        displayName: "clangd",
        languageIds: new[] { "cpp" },
        serverPath: clangdPath,
        fileName: clangdPath,
        argumentsFor: BuildClangdArguments,
        settingsKey: ClangdSettingsKey);

    /// <summary>
    /// clangd's arguments for a session rooted at <paramref name="workspaceRoot"/> (already
    /// normalized — see <see cref="ArgumentsFor"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠ <b>NEVER add <c>--offset-encoding</c> here</b> — not <c>=utf-8</c>, not anything.
    /// It is the most copy-pasted clangd flag on the internet and it would silently break this
    /// client: LSP positions are converted as <c>character = column - 1</c> at 12+ call sites
    /// against AvaloniaEdit's <c>Caret.Column</c>, a 1-based UTF-16 code-unit index, so utf-16
    /// is the only encoding it can read. It is negotiated properly via
    /// <c>general.positionEncodings</c> (see <c>LanguageService.BuildClientCapabilities</c>).
    /// A utf-8 override shifts every position on every line containing a non-ASCII character,
    /// with no error on either side.
    /// </para>
    /// <para>
    /// <b>Rootless launch omits the flag entirely</b> rather than pointing clangd at a made-up
    /// directory. clangd then falls back to searching upward from each file for a
    /// <c>compile_commands.json</c>, which will not find <c>obj/</c> — the session is degraded
    /// either way, and a wrong <c>--compile-commands-dir</c> is strictly worse than an absent
    /// one. Note this is reachable today: the IDE's autostart path starts the server with no
    /// root at all. Fixing that is the registry's job, not this method's.
    /// </para>
    /// <para>
    /// The whole token is quoted (<c>"--flag=value"</c>, not <c>--flag="value"</c>): project
    /// paths contain spaces, and Windows' <c>CommandLineToArgvW</c> — which is how clangd's
    /// argv is built — would otherwise split the argument at the space and clangd would ignore
    /// the fragment. <c>Path.Combine</c> never yields a trailing separator, so the closing quote
    /// can never be escaped by one.
    /// </para>
    /// </remarks>
    private static string BuildClangdArguments(string? workspaceRoot)
    {
        if (workspaceRoot is null) return string.Empty;

        var compileCommandsDir = Path.Combine(workspaceRoot, CompileCommandsDirectoryName);
        return $"\"--compile-commands-dir={compileCommandsDir}\"";
    }
}
