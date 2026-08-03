using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Net;

namespace BasicLang.Compiler.CodeGen.Net
{
    /// <summary>
    /// Everything about the build that is NOT the surface or the reference closure but still
    /// changes what a Native-AOT publish produces (spec §10.2's "TFM + RID + toolchain identity +
    /// .NET SDK and ILCompiler version" row).
    ///
    /// <para><b>Every field is required.</b> A null-or-empty field makes
    /// <see cref="NetShimCache.KeyFor"/> return null, which is a permanent MISS — see that
    /// method's remarks for why "unknown" must never be spelled as "empty string" and quietly
    /// folded into a key.</para>
    /// </summary>
    /// <param name="Configuration">
    /// The build configuration. Also in the key, not only in the manifest PATH: the path is
    /// sanitized (<see cref="NetShimCache.CacheDirectory"/>) and sanitization is lossy, so two
    /// exotic configuration names could share one manifest file. Carrying the raw name in the key
    /// downgrades that collision from a false HIT to a false miss.
    /// </param>
    /// <param name="TargetFramework">The shim csproj's TFM — <c>net8.0</c> today (spec §8.1).</param>
    /// <param name="RuntimeIdentifier">The publish RID, e.g. <c>win-x64</c>.</param>
    /// <param name="ToolchainIdentity">
    /// The C++ toolchain's identity (<c>CppToolchain.DisplayName</c>). The shim itself is linked
    /// by ILCompiler and not by this toolchain, so this is conservative rather than load-bearing —
    /// it can only add misses.
    /// </param>
    /// <param name="SdkIdentity">
    /// The .NET SDK that will run <c>dotnet publish</c> — see
    /// <see cref="NetShimCache.ProbeSdkIdentity"/>, which is where the ILCompiler half of §10.2's
    /// row comes from too.
    /// </param>
    internal sealed record NetShimCacheEnvironment(
        string Configuration,
        string TargetFramework,
        string RuntimeIdentifier,
        string ToolchainIdentity,
        string SdkIdentity);

    /// <summary>A validated cache hit: the key it matched and the native DLL it vouches for.</summary>
    internal sealed record NetShimCacheHit(string Key, string DllPath);

    /// <summary>
    /// Spec §10.2's content-hash cache for the phase-5 shim publish. Task 1 measured a warm AOT
    /// publish at ~11 s and a cold one at ~27 s, so this is a nicety — but a FALSE HIT ships a
    /// native executable bound to a shim whose exports no longer match, which is a silent
    /// wrong-answer failure. <b>Every ambiguous decision in this class is therefore resolved
    /// toward MISS.</b> Concretely: a null key, an absent manifest, an unparsable manifest, a
    /// missing DLL, an MVID that cannot be read, an unknown SDK version and an empty environment
    /// field are all misses, and none of them can be spelled in a way that produces a hit.
    ///
    /// <para><b>The key (spec §10.2), five separable components:</b></para>
    /// <list type="number">
    /// <item><description><b>Reference identities as assembly MVID</b>, not path + timestamp +
    /// size. The MVID is a fresh GUID in every non-deterministic compilation and a content hash in
    /// a deterministic one, so a rebuilt assembly whose length and stamp are unchanged still keys
    /// differently. Each entry contributes its PATH as well, because the path is what the
    /// generated csproj's <c>&lt;HintPath&gt;</c> carries.</description></item>
    /// <item><description><b>The mangled member set</b> — <see cref="NetNameMangler"/> over
    /// <see cref="NetSurface.Members"/>, de-duplicated first-seen in surface order, which is
    /// exactly how <c>NetShimGenerator</c> and <c>NetProxyEmitter</c> collapse slots — plus
    /// <see cref="NetSurface.DeclaredTypeNames"/>. The mangler is a pure function with no statics
    /// precisely so this component is stable across processes.</description></item>
    /// <item><description><b>The shim template version</b>: <see cref="BlnetContract.AbiVersion"/>
    /// plus the three <see cref="BlnetShimSources"/> texts, plus <b>this assembly's own
    /// MVID</b>. The last one is what covers a change to the GENERATOR rather than to the
    /// templates: a hand-maintained "template version" constant is a drift hazard (edit
    /// <c>NetShimGenerator</c>, forget the constant, ship a stale shim), whereas the compiler
    /// assembly's MVID changes whenever the compiler is rebuilt and cannot be forgotten. It is
    /// read from the ALREADY-LOADED assembly via reflection — no file is opened.</description></item>
    /// <item><description><b>Configuration + TFM + RID + toolchain identity</b> — see
    /// <see cref="NetShimCacheEnvironment"/>.</description></item>
    /// <item><description><b>The .NET SDK identity</b> — see
    /// <see cref="ProbeSdkIdentity"/>.</description></item>
    /// </list>
    ///
    /// <para><b>Why the manifest cannot go stale, even after a crash.</b> The manifest records the
    /// published DLL's own SHA-256, and <see cref="TryGetHit"/> re-hashes the file on disk before
    /// answering. That closes the one dangerous window a key-only manifest has: a publish that
    /// overwrote the DLL and then died before rewriting the manifest would leave the OLD key
    /// vouching for the NEW binary, and a later build with the old inputs would hit it. With the
    /// content hash in the manifest that combination simply does not validate. It also means
    /// <see cref="Invalidate"/> is a courtesy for <c>clean</c>, not a correctness requirement on
    /// the publish path — a caller that forgets it cannot produce a false hit.</para>
    ///
    /// <para><b>Not wired into the build.</b> P2a-1 Task 15 builds the cache; Task 16 is what
    /// threads it through <c>CppProjectBuilder</c>'s phase 5. Nothing calls
    /// <see cref="KeyFor"/> in production yet, so this class changes the behavior of no existing
    /// program — the same inertness claim every other P2a-1 task carries.</para>
    /// </summary>
    // PUBLIC for exactly one member: BuildService.CleanAsync (VisualGameStudio.ProjectSystem)
    // must delete CacheRoot, and BasicLang's InternalsVisibleTo names only the test project.
    // Publishing one path helper is a far smaller widening than granting an entire assembly
    // access to BasicLang's internals, and far safer than re-spelling "obj/blnet" in the IDE.
    public static class NetShimCache
    {
        /// <summary>
        /// The cache directory under the project's <c>obj/</c>. Deliberately NOT under
        /// <c>obj/gen/</c>: <c>CppProjectBuilder.CleanGeneratedDir</c> sweeps that directory on
        /// every successful build, and a cache that a routine build wipes is not a cache.
        /// </summary>
        internal const string CacheDirectoryName = "blnet";

        /// <summary>The publish output directory's name inside a configuration's cache dir.</summary>
        internal const string PublishDirectoryName = "publish";

        internal const string ManifestFileName = "shim-cache.txt";

        /// <summary>
        /// First line of every manifest. Bumping it retires every manifest in the wild as a MISS,
        /// which is the correct way to change the format — an old manifest read by new parsing
        /// rules is the exact shape of a false hit.
        /// </summary>
        internal const string ManifestFormatMarker = "blnet-shim-cache/1";

        /// <summary>SHA-256 rendered as lowercase hex.</summary>
        private const int HashHexLength = 64;

        // Non-printable separators, spelled as character arithmetic rather than escape sequences
        // for the reason NetNameMangler gives: they must stay exactly these raw control
        // characters without depending on an escape surviving every tool that touches this file.
        private static readonly char FieldSeparator = (char)1;
        private static readonly char RecordSeparator = (char)2;

        // -------------------------------------------------------------------------------
        // Locations.
        // -------------------------------------------------------------------------------

        /// <summary>
        /// The shim cache root for a project: <c>&lt;projectDir&gt;/obj/blnet</c>. Covers every
        /// configuration, so <c>clean</c> drops the lot with one delete.
        ///
        /// <para>The one public member of this class — see the class's own remarks.</para>
        /// </summary>
        public static string CacheRoot(string projectDirectory)
        {
            if (string.IsNullOrEmpty(projectDirectory))
                throw new ArgumentException("A project directory is required.", nameof(projectDirectory));
            return Path.Combine(projectDirectory, "obj", CacheDirectoryName);
        }

        /// <summary>
        /// One configuration's cache directory. The configuration name is sanitized for the file
        /// system; the RAW name is separately in the key (see
        /// <see cref="NetShimCacheEnvironment.Configuration"/>) so a sanitization collision costs
        /// a miss, never a hit.
        /// </summary>
        internal static string CacheDirectory(string projectDirectory, string configuration) =>
            Path.Combine(CacheRoot(projectDirectory), SanitizePathSegment(configuration));

        /// <summary>Where the shim's AOT publish output goes — the manifest lives beside it (§10.2).</summary>
        internal static string PublishDirectory(string projectDirectory, string configuration) =>
            Path.Combine(CacheDirectory(projectDirectory, configuration), PublishDirectoryName);

        internal static string ManifestPath(string projectDirectory, string configuration) =>
            Path.Combine(CacheDirectory(projectDirectory, configuration), ManifestFileName);

        /// <summary>
        /// A configuration name reduced to <c>[A-Za-z0-9._-]</c>. Lossy and NOT collision-free on
        /// purpose — uniqueness is the key's job, not the path's.
        /// </summary>
        private static string SanitizePathSegment(string configuration)
        {
            if (string.IsNullOrEmpty(configuration))
                return "_";

            var sb = new StringBuilder(configuration.Length);
            foreach (var c in configuration)
            {
                var ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')
                         || c == '.' || c == '_' || c == '-';
                sb.Append(ok ? c : '_');
            }

            // "." and ".." are directory names no one wants to create.
            var text = sb.ToString();
            return text is "." or ".." ? "_" : text;
        }

        // -------------------------------------------------------------------------------
        // The key.
        // -------------------------------------------------------------------------------

        /// <summary>
        /// The cache key for one shim publish, or <b>null</b> when the inputs cannot be identified
        /// with certainty — which every caller must treat as "publish unconditionally".
        ///
        /// <para>Null is returned when: the surface is empty (there is no shim to cache); any
        /// environment field is null or empty (an unknown SDK version must not be folded in as
        /// <c>""</c> — that would make two genuinely different SDKs key identically); or ANY
        /// reference's MVID cannot be read, including because the file is missing, is not managed,
        /// or is locked by another process. That last rule is deliberately absolute: contributing
        /// a placeholder for an unreadable reference would let a transient read failure and a real
        /// content change produce the same key.</para>
        ///
        /// <para>Every component is hashed with SHA-256 over UTF-8 bytes.
        /// <c>string.GetHashCode()</c> is randomized per process in .NET Core and would make the
        /// same build key differently on every run — the failure Task 9 hit in the mangler.</para>
        /// </summary>
        /// <param name="surface">
        /// The project's .NET surface. Contributes its mangled member set and its declared type
        /// names; nothing else about it is read.
        /// </param>
        /// <param name="referencePaths">
        /// The assemblies the shim compiles against, in the order the generated csproj lists them.
        /// Order-stable and de-duplicated by <c>NetReferenceClosure</c>, which exists in that shape
        /// specifically so this method can hash it.
        /// </param>
        internal static string KeyFor(
            NetSurface surface,
            IReadOnlyList<string> referencePaths,
            NetShimCacheEnvironment environment)
            => KeyFor(surface, referencePaths, environment, TemplateIdentity());

        /// <summary>
        /// <see cref="KeyFor(NetSurface,IReadOnlyList{string},NetShimCacheEnvironment)"/> with the
        /// template component supplied explicitly.
        ///
        /// <para><b>The seam exists so component 3 is testable at all.</b> Every other component
        /// of the key is reachable from a parameter, but the template is built from constants
        /// (<see cref="BlnetContract.AbiVersion"/>, <see cref="BlnetShimSources"/>, this
        /// assembly's MVID) that a test cannot vary — which would leave "the template is actually
        /// in the key" as an untested claim, and an untested claim about a cache key is exactly the
        /// kind of hole that ships a stale shim. Production always passes
        /// <see cref="TemplateIdentity"/>.</para>
        /// </summary>
        internal static string KeyFor(
            NetSurface surface,
            IReadOnlyList<string> referencePaths,
            NetShimCacheEnvironment environment,
            string templateIdentity)
        {
            if (surface == null) throw new ArgumentNullException(nameof(surface));
            if (environment == null) throw new ArgumentNullException(nameof(environment));
            if (templateIdentity == null) throw new ArgumentNullException(nameof(templateIdentity));

            // An empty surface emits no shim at all (NetShimGenerator's inertness rule), so there
            // is nothing to cache and nothing a hit could legitimately skip.
            if (!surface.IsNonEmpty)
                return null;

            if (IsBlank(environment.Configuration) || IsBlank(environment.TargetFramework)
                || IsBlank(environment.RuntimeIdentifier) || IsBlank(environment.ToolchainIdentity)
                || IsBlank(environment.SdkIdentity))
                return null;

            var identity = new StringBuilder();

            // ---- 1. Reference identities: path + MVID, never timestamp or size. ----
            identity.Append("refs");
            foreach (var path in referencePaths ?? Array.Empty<string>())
            {
                var mvid = TryReadMvid(path);
                if (mvid == null)
                    return null;    // uncertain input -> no key -> miss
                identity.Append(RecordSeparator).Append(path)
                        .Append(FieldSeparator).Append(mvid);
            }

            // ---- 2. The mangled member set + the declared types. ----
            // De-duplicated first-seen, the SAME collapse NetShimGenerator and NetProxyEmitter
            // perform, so the key tracks the exports that actually get emitted.
            identity.Append(RecordSeparator).Append("members");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var member in surface.Members)
            {
                var mangled = NetNameMangler.Mangle(member);
                if (seen.Add(mangled))
                    identity.Append(FieldSeparator).Append(mangled);
            }
            identity.Append(RecordSeparator).Append("declared");
            foreach (var declared in surface.DeclaredTypeNames)
                identity.Append(FieldSeparator).Append(declared);

            // ---- 3. Shim template version + the generator that assembles it. ----
            identity.Append(RecordSeparator).Append("template")
                    .Append(FieldSeparator).Append(templateIdentity);

            // ---- 4. Configuration + TFM + RID + toolchain. ----
            identity.Append(RecordSeparator).Append("env")
                    .Append(FieldSeparator).Append(environment.Configuration)
                    .Append(FieldSeparator).Append(environment.TargetFramework)
                    .Append(FieldSeparator).Append(environment.RuntimeIdentifier)
                    .Append(FieldSeparator).Append(environment.ToolchainIdentity);

            // ---- 5. SDK (and, through it, ILCompiler). ----
            identity.Append(RecordSeparator).Append("sdk")
                    .Append(FieldSeparator).Append(environment.SdkIdentity);

            return Sha256Hex(identity.ToString());
        }

        /// <summary>
        /// Key component 3: everything that decides what the shim's FIXED half looks like —
        /// <see cref="BlnetContract.AbiVersion"/>, the four <see cref="BlnetShimSources"/> texts
        /// (hashed, so this stays short), and the compiler assembly's own MVID.
        ///
        /// <para>Returned RAW rather than pre-hashed so a test can assert that each input is
        /// genuinely present — a hash would only be comparable against a re-implementation of this
        /// method, which proves nothing.</para>
        /// </summary>
        internal static string TemplateIdentity() =>
            new StringBuilder()
                .Append(BlnetContract.AbiVersion.ToString(CultureInfo.InvariantCulture))
                .Append(FieldSeparator).Append(Sha256Hex(BlnetShimSources.HandleTable))
                .Append(FieldSeparator).Append(Sha256Hex(BlnetShimSources.BlnetStatusCs))
                .Append(FieldSeparator).Append(Sha256Hex(BlnetShimSources.ShimAbiCs))
                .Append(FieldSeparator).Append(Sha256Hex(BlnetShimSources.MarshalCs))
                .Append(FieldSeparator).Append(CompilerAssemblyMvid)
                .ToString();

        /// <summary>
        /// This compiler assembly's MVID, read from the LOADED assembly through reflection —
        /// no file is opened, so there is no lock and no <c>Assembly.LoadFrom</c> (banned here by
        /// Task 7). Changes on every rebuild of <c>BasicLang.dll</c>, which is what makes the
        /// generator's own emission logic part of the key without a hand-maintained constant.
        /// </summary>
        private static readonly string CompilerAssemblyMvid =
            typeof(NetShimCache).Assembly.ManifestModule.ModuleVersionId.ToString("N", CultureInfo.InvariantCulture);

        /// <summary>
        /// The assembly's Module Version ID as 32 lowercase hex digits, or <b>null</b> if it
        /// cannot be read for ANY reason — missing file, native image, truncated PE, sharing
        /// violation, permission error.
        ///
        /// <para><b>Reads metadata; does not load the assembly.</b> <c>Assembly.LoadFrom</c> is
        /// banned in this codebase (Task 7 removed it from <c>TypeRegistry</c> over file locks and
        /// the impossibility of unloading). <see cref="PEReader"/> over a <see cref="FileStream"/>
        /// opened <c>FileShare.ReadWrite | FileShare.Delete</c> imposes no restriction on any other
        /// process for as long as it is open — another process may write to or delete the file
        /// meanwhile — and the handle is closed when this method returns. Nothing is added to the
        /// load context, so the file is not locked afterwards either.</para>
        ///
        /// <para><see cref="PEReader.HasMetadata"/> is checked rather than assumed: the shared
        /// framework directory holds native DLLs (<c>coreclr.dll</c>, <c>clrjit.dll</c>) beside the
        /// managed ones, and <c>GetMetadataReader</c> throws on those.</para>
        /// </summary>
        internal static string TryReadMvid(string assemblyPath)
        {
            if (string.IsNullOrEmpty(assemblyPath))
                return null;

            try
            {
                using var stream = new FileStream(
                    assemblyPath, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete, bufferSize: 4096, FileOptions.None);
                using var pe = new PEReader(stream);
                if (!pe.HasMetadata)
                    return null;
                var reader = pe.GetMetadataReader();
                return reader.GetGuid(reader.GetModuleDefinition().Mvid)
                    .ToString("N", CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is IOException || ex is BadImageFormatException
                                       || ex is UnauthorizedAccessException || ex is ArgumentException
                                       || ex is NotSupportedException || ex is InvalidOperationException)
            {
                return null;
            }
        }

        // -------------------------------------------------------------------------------
        // The manifest.
        // -------------------------------------------------------------------------------

        /// <summary>
        /// The cached publish for <paramref name="key"/>, or <b>null</b> for a miss.
        ///
        /// <para>A hit requires ALL of: a non-null key; a manifest that exists, parses, and carries
        /// the right format marker; a recorded key equal to <paramref name="key"/>; a DLL that
        /// still exists at the recorded path; and a DLL whose SHA-256 still equals the recorded
        /// one. Any failure — including an exception reading either file — is a miss.</para>
        /// </summary>
        internal static NetShimCacheHit TryGetHit(string projectDirectory, string configuration, string key)
        {
            if (key == null)
                return null;

            var manifest = TryReadManifest(ManifestPath(projectDirectory, configuration));
            if (manifest == null)
                return null;

            var (recordedKey, recordedDllHash, dllPath) = manifest.Value;
            if (!string.Equals(recordedKey, key, StringComparison.Ordinal))
                return null;

            var actual = TryHashFile(dllPath);
            if (actual == null || !string.Equals(actual, recordedDllHash, StringComparison.Ordinal))
                return null;

            return new NetShimCacheHit(key, dllPath);
        }

        /// <summary>
        /// Records a SUCCESSFUL publish. Returns false — writing nothing — when
        /// <paramref name="key"/> is null, when <paramref name="dllPath"/> is blank, or when the
        /// DLL cannot be read and hashed. A publish that failed therefore leaves no manifest
        /// behind, which is the write-after-success half of the corruption story; the
        /// temp-file-then-rename below is the partial-write half.
        ///
        /// <para>The key is required to be a well-formed hash, the same shape
        /// <see cref="TryReadManifest"/> will later demand. Writing a key the reader would reject
        /// is worse than refusing it: it produces a manifest that exists, looks committed, and can
        /// never hit.</para>
        /// </summary>
        internal static bool Commit(string projectDirectory, string configuration, string key, string dllPath)
        {
            if (!IsHash(key) || IsBlank(dllPath))
                return false;

            var dllHash = TryHashFile(dllPath);
            if (dllHash == null)
                return false;

            var manifestPath = ManifestPath(projectDirectory, configuration);
            var text = ManifestFormatMarker + "\n" + key + "\n" + dllHash + "\n"
                       + Path.GetFullPath(dllPath) + "\n";

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(manifestPath));

                // Temp-then-rename: a reader never observes a half-written manifest, and a crash
                // mid-write leaves the PREVIOUS manifest — which is safe, because TryGetHit
                // re-hashes the DLL and the previous manifest's hash no longer matches whatever a
                // partially-completed publish left on disk.
                var temp = manifestPath + ".tmp";
                File.WriteAllText(temp, text);      // UTF-8, no BOM (the .NET default)
                File.Move(temp, manifestPath, overwrite: true);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException
                                       || ex is ArgumentException || ex is NotSupportedException)
            {
                return false;
            }
        }

        /// <summary>
        /// Drops one configuration's manifest. A courtesy for <c>clean</c>-like paths, NOT a
        /// correctness requirement before publishing — see this class's remarks on why a stale
        /// manifest cannot validate. Returns false if a manifest is still present afterwards.
        /// </summary>
        internal static bool Invalidate(string projectDirectory, string configuration)
        {
            var path = ManifestPath(projectDirectory, configuration);
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
                return !File.Exists(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException
                                       || ex is ArgumentException || ex is NotSupportedException)
            {
                return false;
            }
        }

        /// <summary>
        /// Parses a manifest into (key, dllHash, dllPath), or null for ANY defect: missing file,
        /// wrong marker, wrong line count, a key or hash that is not 64 lowercase hex digits, an
        /// empty path, or an IO failure. There is no lenient branch — a manifest this method
        /// cannot fully vouch for is a miss.
        /// </summary>
        private static (string Key, string DllHash, string DllPath)? TryReadManifest(string manifestPath)
        {
            string text;
            try
            {
                if (!File.Exists(manifestPath))
                    return null;
                text = File.ReadAllText(manifestPath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException
                                       || ex is ArgumentException || ex is NotSupportedException)
            {
                return null;
            }

            // \r\n is normalized away: the manifest is written with \n, but core.autocrlf and any
            // editor that touches it could re-line-end the file, and that is not corruption.
            var lines = text.Replace("\r\n", "\n").Split('\n');
            if (lines.Length < 4)
                return null;
            if (!string.Equals(lines[0], ManifestFormatMarker, StringComparison.Ordinal))
                return null;
            if (!IsHash(lines[1]) || !IsHash(lines[2]) || IsBlank(lines[3]))
                return null;

            return (lines[1], lines[2], lines[3]);
        }

        // -------------------------------------------------------------------------------
        // The SDK probe.
        // -------------------------------------------------------------------------------

        /// <summary>
        /// The .NET SDK version <c>dotnet publish</c> would use from
        /// <paramref name="workingDirectory"/>, or <b>null</b> when it cannot be determined —
        /// which makes <see cref="KeyFor"/> return null and disables the cache entirely. That is
        /// the intended outcome: a publish whose SDK is unknown must not be skipped.
        ///
        /// <para><b>Why a process, and why keyed on the working directory.</b> There is no
        /// in-process way to learn which SDK <c>dotnet</c> will select:
        /// <c>RuntimeInformation.FrameworkDescription</c> and <see cref="Environment.Version"/>
        /// describe the RUNTIME this compiler happens to be executing on, not the SDK, and an SDK
        /// upgrade that leaves the runtime alone would then key identically — a false hit. And the
        /// selection is per-directory, because <c>global.json</c> pins it, so one memoized answer
        /// per process would give two projects with different pins the same key.</para>
        ///
        /// <para><b>ILCompiler.</b> §10.2 asks for the ILCompiler version too. The shim csproj does
        /// not pin one, so the SDK picks it (<c>BundledNETCoreAppPackageVersion</c>) — the SDK
        /// version determines it. The residual gap, stated rather than hidden: the same SDK
        /// restoring a different ILCompiler build would not be noticed here.</para>
        ///
        /// <para>Memoized per working directory, including negative answers: a machine without the
        /// SDK on PATH must not pay a failed process launch on every build, and "no SDK" is a
        /// permanent miss, which is safe.</para>
        /// </summary>
        internal static string ProbeSdkIdentity(string workingDirectory)
        {
            var dir = IsBlank(workingDirectory) ? Environment.CurrentDirectory : workingDirectory;
            string full;
            try { full = Path.GetFullPath(dir); }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException
                                       || ex is PathTooLongException || ex is IOException
                                       || ex is System.Security.SecurityException)
            {
                return null;
            }

            return SdkIdentityCache.GetOrAdd(full, RunDotnetVersion);
        }

        private static readonly ConcurrentDictionary<string, string> SdkIdentityCache =
            new(StringComparer.OrdinalIgnoreCase);

        private static string RunDotnetVersion(string workingDirectory)
        {
            try
            {
                if (!Directory.Exists(workingDirectory))
                    return null;

                var psi = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                psi.ArgumentList.Add("--version");

                using var proc = Process.Start(psi);
                if (proc == null)
                    return null;

                var stdout = proc.StandardOutput.ReadToEnd();
                proc.StandardError.ReadToEnd();
                if (!proc.WaitForExit(30_000))
                {
                    try { proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
                    return null;
                }
                if (proc.ExitCode != 0)
                    return null;

                var version = stdout.Trim();
                return version.Length == 0 ? null : version;
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidOperationException
                                       || ex is System.ComponentModel.Win32Exception
                                       || ex is UnauthorizedAccessException
                                       || ex is PlatformNotSupportedException)
            {
                // dotnet not on PATH is the common case and a supported one (spec §10.5) — the
                // publish will fail on its own terms; the cache just never hits.
                return null;
            }
        }

        // -------------------------------------------------------------------------------
        // Hashing.
        // -------------------------------------------------------------------------------

        /// <summary>SHA-256 over UTF-8 bytes as 64 lowercase hex digits.</summary>
        private static string Sha256Hex(string text) =>
            ToHex(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

        /// <summary>
        /// A file's SHA-256 as 64 lowercase hex digits, or null on any read failure. Opened
        /// <c>FileShare.ReadWrite | FileShare.Delete</c> for the same no-lock reason
        /// <see cref="TryReadMvid"/> gives.
        /// </summary>
        private static string TryHashFile(string path)
        {
            if (IsBlank(path))
                return null;
            try
            {
                using var stream = new FileStream(
                    path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete, bufferSize: 65536, FileOptions.SequentialScan);
                return ToHex(SHA256.HashData(stream));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException
                                       || ex is ArgumentException || ex is NotSupportedException)
            {
                return null;
            }
        }

        private static string ToHex(byte[] digest)
        {
            var hex = new StringBuilder(digest.Length * 2);
            foreach (var b in digest)
                hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return hex.ToString();
        }

        private static bool IsHash(string s)
        {
            if (s == null || s.Length != HashHexLength)
                return false;
            foreach (var c in s)
            {
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
                    return false;
            }
            return true;
        }

        private static bool IsBlank(string s) => string.IsNullOrWhiteSpace(s);
    }
}
