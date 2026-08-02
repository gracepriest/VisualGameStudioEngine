using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BasicLang.Net;
using Microsoft.CodeAnalysis;
// BasicLang declares its OWN TypeKind (SymbolTable.cs:181) and it wins the unqualified name here,
// so Roslyn's must be aliased rather than used bare — the two have no values in common and an
// accidental bind is a compile error today but would be a silent mismap if they ever overlapped.
using RoslynTypeKind = Microsoft.CodeAnalysis.TypeKind;

namespace BasicLang.Compiler.SemanticAnalysis
{
    /// <summary>
    /// Registry for .NET types the LSP offers IntelliSense over. Provides lazy loading based on
    /// Using statements.
    ///
    /// <para><b>Assemblies are READ, never LOADED (spec §6.2).</b> Until P2a-1 Task 7 the three
    /// assembly-reading paths here — the namespace index scan, the per-namespace type load, and the
    /// public whole-assembly load used for project references — all called
    /// <c>Assembly.LoadFrom</c> behind a bare <c>catch {}</c>. That had three consequences, and the
    /// third is the one that mattered:</para>
    /// <list type="number">
    /// <item><description>It LOCKED every DLL in every search path for the life of the LSP process,
    /// so a referenced assembly could not be rebuilt while the editor was open, and the LSP could
    /// not see the new one without a restart.</description></item>
    /// <item><description>It ran module initializers from arbitrary user assemblies inside the
    /// compiler process, and a loaded assembly cannot be unloaded.</description></item>
    /// <item><description><b>It could not read a reference assembly built for a higher framework
    /// version than the running runtime at all.</b> The LSP process is net8.0 and
    /// <see cref="DetectDotNetSdkPath"/> hands it the NEWEST installed reference pack, so on any
    /// machine whose newest pack outranks the runtime, <c>Assembly.LoadFrom</c> failed with
    /// <c>FileLoadException</c> — measured 141 of 164 assemblies over
    /// <c>Microsoft.NETCore.App.Ref/9.0.12</c> — the <c>catch {}</c> swallowed every one, and the
    /// namespace index came out EMPTY after ~7.3 s of work. Every .NET completion the LSP has been
    /// offering came from <see cref="PreloadCoreTypes"/> instead.</description></item>
    /// </list>
    /// <para>Reading metadata through <see cref="NetTypeResolver"/> has none of those properties:
    /// no lock, no load, no version coupling. Measured over the same 163-assembly pack it is also
    /// ~4x faster (≈620 ms against 3030 ms) and finds MORE — 122 namespaces and 3778 public types
    /// against 93 and 2198 — because a metadata walk does not depend on the host runtime being able
    /// to execute what it is describing.</para>
    ///
    /// <para><b><see cref="PreloadCoreTypes"/> still uses reflection, deliberately.</b> It describes
    /// the RUNNING runtime's own <see cref="Type"/> objects and never touches a file, so it has none
    /// of the defects above. That leaves two producers of <see cref="NetTypeInfo"/> in this file —
    /// <see cref="CreateNetTypeInfo(Type)"/> and <see cref="CreateNetTypeInfo(INamedTypeSymbol)"/> —
    /// kept side by side so that any drift between them is visible in one screen. They are pinned
    /// against each other by
    /// <c>TypeRegistryMetadataTests.TheReflectionAndMetadataProducersAgreeOnShape</c>.</para>
    /// </summary>
    public class TypeRegistry
    {
        // Namespace -> Assembly path mapping (lightweight index)
        private readonly Dictionary<string, List<string>> _namespaceIndex;

        // Loaded types per namespace (lazy loaded)
        private readonly Dictionary<string, List<NetTypeInfo>> _loadedTypes;

        // All loaded types by full name for quick lookup
        private readonly Dictionary<string, NetTypeInfo> _typesByName;

        // Assembly search paths (HashSet for O(1) Contains checks)
        private readonly HashSet<string> _searchPaths;

        // Cache file path for the namespace index
        private readonly string _cacheFilePath;

        // Track which namespaces have been loaded
        private readonly HashSet<string> _loadedNamespaces;

        /// <summary>
        /// Guards the five collections above. One lock, not five: they are mutated together (loading
        /// a namespace writes <see cref="_loadedTypes"/>, <see cref="_typesByName"/> and
        /// <see cref="_loadedNamespaces"/> in one pass) and a reader wants a consistent view across
        /// them.
        ///
        /// <para><b>Why this is needed even though LSP requests are normally serialized.</b> One
        /// registry is shared by every open document (<c>DocumentManager</c> is a DI singleton and
        /// hands the same instance to each <c>DocumentState</c>). OmniSharp schedules didOpen /
        /// didChange / didSave as <c>Serial</c> and completion / hover / definition / signature-help
        /// as <c>Parallel</c>, with an outer concat over groups, so mutation and reads do not
        /// normally overlap — which is why this was never observed. The hatch is the TIMEOUT path:
        /// <c>DefaultRequestInvoker.RouteRequest</c> races each request against a timer, and when the
        /// timer wins the scheduler advances to the next group WHILE THE HANDLER IS STILL RUNNING.
        /// The sync handlers take a <c>CancellationToken</c> and never observe it, and
        /// <c>UpdateDocument</c> → <c>RunSemanticAnalysis</c> → <see cref="LoadNamespace"/> is
        /// synchronous with no cancellation checks, so a timed-out didChange keeps writing here while
        /// a completion request reads.</para>
        ///
        /// <para><b>Task 7 raised the stakes rather than creating the race.</b> Before it,
        /// <see cref="_typesByName"/> was effectively frozen after <see cref="PreloadCoreTypes"/>
        /// because every assembly read silently failed; it now grows by hundreds of entries during
        /// editing, which lengthens each didChange and so raises both the chance of hitting that
        /// hatch and the damage when it is hit. The damage is not a lost entry: an insert racing a
        /// resize can leave a bucket chain cyclic, so a later <c>TryGetValue</c> SPINS FOREVER — an
        /// LSP hang — and the enumerating readers throw
        /// <see cref="InvalidOperationException"/> instead.</para>
        ///
        /// <para><b>Lock ordering.</b> This lock may be taken before <see cref="_resolverLock"/> or
        /// <see cref="_diagnostics"/>, never after. Neither of those ever reaches back into these
        /// collections, so there is no cycle.</para>
        /// </summary>
        private readonly object _stateLock = new object();

        // ------------------------------------------------------------------
        // Metadata reading (spec §6.2). See the class remarks for what this replaced.
        // ------------------------------------------------------------------

        /// <summary>
        /// Every assembly path a caller has asked this registry to read. Grows only; a path already
        /// present never triggers a rebuild. The resolver's actual reference set is this plus, when
        /// needed, the shared framework — see <see cref="ResolverFor"/>.
        /// </summary>
        private readonly HashSet<string> _requestedPaths;

        /// <summary>
        /// Assemblies that make a reference set self-sufficient: they declare
        /// <c>System.Object</c>/<c>System.Enum</c>, so a set containing one needs no framework added.
        ///
        /// <para><c>System.Private.CoreLib.dll</c> is deliberately NOT here even though it is the
        /// runtime's real core library — Roslyn cannot read it as a metadata reference at all
        /// (measured: <c>GetAssemblyOrModuleSymbol</c> returns null for it), so its presence makes a
        /// set self-sufficient in name only.</para>
        /// </summary>
        private static readonly HashSet<string> CoreLibraryFileNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "System.Runtime.dll",   // every reference pack and the shared framework
                "mscorlib.dll"          // desktop-style layouts
            };

        /// <summary>
        /// ONE resolver per registry, rebuilt only when a genuinely new assembly path appears.
        ///
        /// <para><b>The lifetime is the point.</b> Construction was measured at 209 ms cold and a
        /// steady 46-49 ms for every subsequent fresh instance over the same closure, and a fresh
        /// instance also throws away the lookup cache so the ~17 ms miss path recurs. A registry is
        /// owned by the LSP's document manager and lives as long as the server, so one resolver per
        /// registry is exactly one per reference closure — which is what
        /// <see cref="NetTypeResolver"/>'s remarks ask for. Building one per call, or per debounced
        /// IntelliSense pass, would cost ~47 ms and a cold cache on every keystroke.</para>
        ///
        /// <para>A reference set is fixed at construction, so a path outside the current set forces a
        /// rebuild over the UNION. That is why callers hand in every path they are about to need in
        /// one batch — a per-path rebuild in a 163-assembly loop would be ~163 constructions.</para>
        /// </summary>
        private NetTypeResolver _resolver;

        private readonly object _resolverLock = new object();

        private int _resolverBuildCount;

        /// <summary>
        /// How many times a resolver has been constructed for this registry. Observability for the
        /// ownership invariant above — one build, then reuse. Not part of the LSP-facing surface.
        /// </summary>
        internal int ResolverBuildCount
        {
            get { lock (_resolverLock) { return _resolverBuildCount; } }
        }

        private readonly List<string> _diagnostics = new List<string>();

        /// <summary>
        /// Cap on <see cref="_diagnostics"/>. A search path full of native DLLs yields one entry per
        /// file, and this list is as long-lived as the LSP session; the point of the list is that a
        /// failure is visible at all, which the first few entries already achieve.
        /// </summary>
        private const int MaxDiagnostics = 256;

        /// <summary>
        /// Assemblies that could not be read, one BL6021 each — the replacement for the three bare
        /// <c>catch {}</c> blocks this class used to hide its failures in. Empty on a healthy setup.
        ///
        /// <para>A swallowed error here is exactly why nobody noticed the namespace index was empty
        /// for the life of the feature, so these are also written to stderr as they happen (stdout
        /// carries LSP framing and must never be used for logging).</para>
        /// </summary>
        internal IReadOnlyList<string> Diagnostics
        {
            get { lock (_diagnostics) { return _diagnostics.ToList(); } }
        }

        public TypeRegistry() : this(DefaultCacheFilePath())
        {
        }

        /// <summary>
        /// TEST SEAM — the cache file path. Production uses <see cref="DefaultCacheFilePath"/>, a
        /// single USER-SCOPE file.
        ///
        /// <para><b>A test that lets this default is not isolated, it is destructive.</b>
        /// <see cref="BuildIndex"/> CLEARS the index and then <see cref="SaveIndexToCache"/>
        /// REPLACES that one file — so a fixture that builds an index over a temp directory
        /// destroys the developer's real cache. <see cref="LoadIndexFromCache"/> now rejects an
        /// index whose entries or assembly paths are gone, so the shipped LSP at least rebuilds
        /// instead of running on the dead index (the incident that motivated that validation), but
        /// the cache the user had is still lost. Every fixture must pass a path inside its own
        /// temp directory.</para>
        /// </summary>
        internal TypeRegistry(string cacheFilePath)
        {
            _namespaceIndex = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            _loadedTypes = new Dictionary<string, List<NetTypeInfo>>(StringComparer.OrdinalIgnoreCase);
            _typesByName = new Dictionary<string, NetTypeInfo>(StringComparer.OrdinalIgnoreCase);
            _searchPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _loadedNamespaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _requestedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _cacheFilePath = cacheFilePath;
        }

        private static string DefaultCacheFilePath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(appData, "BasicLang", "namespace_index.json");
        }

        /// <summary>
        /// Records a failure instead of swallowing it. Deliberately never throws: every caller is on
        /// the LSP request path, where a malformed reference must degrade the answer, not the server.
        /// </summary>
        private void Report(string message)
        {
            // The CAP IS ON THE LIST, NOT ON THE REPORTING. Returning early here once the list is
            // full would silently drop every failure past the cap — a partial reintroduction of the
            // bare catch {} this class was fixed to remove.
            lock (_diagnostics)
            {
                if (_diagnostics.Count < MaxDiagnostics)
                    _diagnostics.Add(message);
            }

            try { Console.Error.WriteLine("[TypeRegistry] " + message); }
            catch { /* a closed or redirected stderr must not break an LSP request */ }
        }

        /// <summary>
        /// The resolver covering <paramref name="assemblyPaths"/>, rebuilt over the union only if
        /// some path is not covered yet. Callers pass a whole batch — see <see cref="_resolver"/>.
        /// </summary>
        private NetTypeResolver ResolverFor(IEnumerable<string> assemblyPaths)
        {
            lock (_resolverLock)
            {
                var added = false;
                foreach (var path in assemblyPaths)
                {
                    if (!string.IsNullOrWhiteSpace(path) && _requestedPaths.Add(path))
                        added = true;
                }

                if (_resolver != null && !added)
                    return _resolver;

                _resolver = NetTypeResolver.Create(EffectiveReferenceSet());
                _resolverBuildCount++;
                return _resolver;
            }
        }

        /// <summary>
        /// True when <paramref name="assemblyPath"/> is readable as .NET metadata; reports a BL6021
        /// naming it when not. This is the replacement for the three bare <c>catch {}</c> blocks.
        ///
        /// <para><b>Reported here rather than forwarded from
        /// <see cref="NetTypeResolver.Diagnostics"/>.</b> The reference set also contains the shared
        /// framework, which no caller asked for, and forwarding the resolver's list wholesale put a
        /// crop of "not a managed assembly" warnings on stderr at every LSP startup — about files the
        /// user never named and cannot act on. Measured: 23 of them in one run. A diagnostic is worth
        /// having only if it is about something the reader chose, so this reports exactly the paths a
        /// caller handed in.</para>
        /// </summary>
        private bool CanRead(NetTypeResolver resolver, string assemblyPath)
        {
            if (resolver.Covers(assemblyPath))
                return true;

            Report($"BL6021: '{assemblyPath}' could not be read as .NET metadata and contributes no "
                   + "types to IntelliSense — it is not a managed assembly, or it was removed after "
                   + "being resolved.");
            return false;
        }

        /// <summary>
        /// The reference set to build a resolver over: the requested paths, plus the shared framework
        /// ONLY IF the requested paths bring no core library of their own.
        ///
        /// <para><b>Why the framework has to be added at all.</b> Reflection never needed it, because
        /// a LOADED assembly resolves its own references through the runtime. Roslyn resolves them
        /// only against the references it is handed, so an assembly read on its own has an ERROR
        /// symbol for every base type. Measured on one probe assembly read alone:
        /// <c>class Widget : Base</c> lost all four of <c>System.Object</c>'s members from its
        /// completion list, and a <c>public enum</c> reported <c>IsEnum = false</c> — a type's
        /// enum-ness IS "its base type is <c>System.Enum</c>", which is unknowable without corlib.
        /// That is a project <c>&lt;Reference&gt;</c>'s situation, and it is the common case.</para>
        ///
        /// <para><b>Why it must NOT be added unconditionally.</b> Two framework versions in one
        /// compilation is far worse than none. The LSP's own search path is a reference PACK, chosen
        /// by <see cref="DetectDotNetSdkPath"/> as the newest installed — normally a different
        /// version from the runtime this process is on. Measured over
        /// <c>Microsoft.NETCore.App.Ref/9.0.12</c> plus the net8.0 shared framework: every core type
        /// is declared twice, so <c>System.Enum</c> resolves to NOTHING and
        /// <c>System.IO.FileMode</c> and <c>System.Text.StringBuilder</c> stop being found at all —
        /// against both sets working perfectly on their own. De-duplicating by file name does not
        /// rescue it either (measured): the pack declares the core types in
        /// <c>System.Runtime.dll</c> and the shared framework in
        /// <c>System.Private.CoreLib.dll</c>, so there is no name collision to remove.</para>
        ///
        /// <para>Hence: whoever brings a core library owns the framework, and the shared framework is
        /// a FALLBACK for callers who bring none. Recomputed per build rather than latched, so a
        /// registry that is handed a reference pack after a bare DLL converges on the pack instead of
        /// keeping a poisoned mixture.</para>
        /// </summary>
        private IReadOnlyList<string> EffectiveReferenceSet()
        {
            var set = new List<string>(_requestedPaths);

            var bringsOwnCoreLibrary = _requestedPaths.Any(
                p => CoreLibraryFileNames.Contains(Path.GetFileName(p)));
            if (bringsOwnCoreLibrary)
                return set;

            // Taken WHOLE, including System.Private.CoreLib. Whether Roslyn can read that one is
            // host-dependent (measured: unreadable in a plain console host, readable in the test
            // host) and in the host where it is readable it is what makes base types resolve at all —
            // dropping it cost every probe assembly System.Object's members. Any residual
            // unreadable entry is silent here by design: see ResolverFor, which reports only the
            // paths a CALLER asked for.
            var seen = new HashSet<string>(_requestedPaths, StringComparer.OrdinalIgnoreCase);
            foreach (var frameworkPath in NetReferenceResolver.FrameworkAssemblies)
            {
                if (seen.Add(frameworkPath))
                    set.Add(frameworkPath);
            }
            return set;
        }

        /// <summary>
        /// Add an assembly search path (e.g., .NET SDK ref assemblies folder)
        /// </summary>
        public void AddSearchPath(string path)
        {
            if (Directory.Exists(path))
            {
                lock (_stateLock)
                {
                    _searchPaths.Add(path);  // HashSet.Add ignores duplicates
                }
            }
        }

        /// <summary>
        /// Build the namespace index from configured search paths.
        /// This scans all assemblies and maps namespaces to assembly paths.
        /// </summary>
        public void BuildIndex()
        {
            lock (_stateLock)
            {
                _namespaceIndex.Clear();

                // Collect every candidate FIRST, then build one resolver over the whole batch. A
                // resolver's reference set is fixed at construction, so indexing assembly-by-assembly
                // would construct one per file — 163 constructions at 46-49 ms over a framework pack.
                var assemblyPaths = new List<string>();
                foreach (var searchPath in _searchPaths)
                    assemblyPaths.AddRange(EnumerateAssemblies(searchPath));

                if (assemblyPaths.Count > 0)
                {
                    var resolver = ResolverFor(assemblyPaths);
                    foreach (var assemblyPath in assemblyPaths)
                        IndexAssembly(resolver, assemblyPath);
                }

                // Save to cache
                SaveIndexToCache();
            }
        }

        /// <summary>
        /// Load index from cache if available.
        ///
        /// <para><b>Parseable is not valid.</b> Both LSP consumers skip <see cref="BuildIndex"/>
        /// whenever this returns <c>true</c> — and <c>DocumentManager</c> skips SDK detection with
        /// it — so accepting a zero-byte file, a file of junk lines (no <c>|</c> means every line
        /// is silently skipped), or a stale index whose assembly paths are all gone leaves
        /// IntelliSense running on a dead index for the life of the session (measured — see the
        /// <see cref="TypeRegistry(string)"/> remarks). A load is valid only if at least one entry
        /// parsed AND at least one referenced assembly still exists on disk; the existence probe
        /// short-circuits on the first hit, so a healthy cache pays ~one
        /// <see cref="File.Exists(string)"/>. One live path suffices because individually dead
        /// paths already degrade per-namespace later, through the <see cref="CanRead"/>/BL6021
        /// path, rather than poisoning the whole load.</para>
        ///
        /// <para>Parsing goes through a LOCAL structure and merges only on success — a rejected file
        /// must leave the in-memory index exactly as it was. The old shape merged into
        /// <see cref="_namespaceIndex"/> WHILE parsing, so even the paths that did return
        /// <c>false</c> could leave partial state behind.</para>
        /// </summary>
        public bool LoadIndexFromCache()
        {
            if (!File.Exists(_cacheFilePath))
                return false;

            try
            {
                var parsed = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in File.ReadAllLines(_cacheFilePath))
                {
                    var parts = line.Split('|');
                    if (parts.Length == 2)
                    {
                        var ns = parts[0];
                        var assemblies = parts[1].Split(';').Where(s => !string.IsNullOrEmpty(s)).ToList();

                        if (!parsed.TryGetValue(ns, out var list))
                            parsed[ns] = list = new List<string>();

                        list.AddRange(assemblies);
                    }
                }

                if (parsed.Count == 0)
                    return false;

                if (!parsed.Values.SelectMany(assemblies => assemblies).Any(File.Exists))
                    return false;

                lock (_stateLock)
                {
                    foreach (var kvp in parsed)
                    {
                        if (!_namespaceIndex.TryGetValue(kvp.Key, out var existing))
                            _namespaceIndex[kvp.Key] = kvp.Value;
                        else
                            existing.AddRange(kvp.Value);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Report($"BL6021: namespace index cache '{_cacheFilePath}' could not be read and "
                       + $"will be rebuilt: {ex.Message}");
                return false;
            }
        }

        private void SaveIndexToCache()
        {
            try
            {
                var dir = Path.GetDirectoryName(_cacheFilePath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var lines = _namespaceIndex.Select(kvp =>
                    $"{kvp.Key}|{string.Join(";", kvp.Value.Distinct())}");
                File.WriteAllLines(_cacheFilePath, lines);
            }
            catch
            {
                // Ignore cache write failures
            }
        }

        private IReadOnlyList<string> EnumerateAssemblies(string path)
        {
            try
            {
                return Directory.GetFiles(path, "*.dll");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException
                                       || ex is ArgumentException)
            {
                // A search path can be removed or made unreadable between AddSearchPath and here.
                Report($"BL6021: search path '{path}' could not be enumerated and will contribute "
                       + $"no .NET types: {ex.Message}");
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// Indexes which namespaces one assembly declares. Was <c>Assembly.LoadFrom</c> +
        /// <c>GetExportedTypes()</c> (see the class remarks); now a metadata walk.
        /// </summary>
        private void IndexAssembly(NetTypeResolver resolver, string assemblyPath)
        {
            if (!CanRead(resolver, assemblyPath))
                return;

            var namespaces = new HashSet<string>(StringComparer.Ordinal);
            foreach (var type in resolver.PublicTypesIn(assemblyPath))
            {
                var ns = NamespaceOf(type);
                if (!string.IsNullOrEmpty(ns))
                    namespaces.Add(ns);
            }

            foreach (var ns in namespaces)
            {
                if (!_namespaceIndex.ContainsKey(ns))
                    _namespaceIndex[ns] = new List<string>();

                if (!_namespaceIndex[ns].Contains(assemblyPath))
                    _namespaceIndex[ns].Add(assemblyPath);
            }
        }

        /// <summary>
        /// Preload a curated set of core .NET types from the RUNNING runtime via
        /// reflection so member completion works out of the box ("Console.",
        /// "String.", "List." etc.) without requiring SDK reference assemblies
        /// on disk. Covers System, System.Collections.Generic, System.Text,
        /// System.IO and System.Linq essentials.
        /// </summary>
        public void PreloadCoreTypes()
        {
            var coreTypes = new[]
            {
                // System primitives and core types
                typeof(object), typeof(string), typeof(int), typeof(long), typeof(short),
                typeof(byte), typeof(float), typeof(double), typeof(decimal), typeof(bool),
                typeof(char), typeof(Console), typeof(Math), typeof(DateTime), typeof(TimeSpan),
                typeof(Random), typeof(Convert), typeof(Environment), typeof(Guid),
                typeof(Exception), typeof(Array), typeof(Enum), typeof(Version), typeof(Uri),
                typeof(ConsoleColor), typeof(ConsoleKeyInfo), typeof(StringComparison),
                typeof(DayOfWeek),

                // System.Collections.Generic
                typeof(System.Collections.Generic.List<>),
                typeof(System.Collections.Generic.Dictionary<,>),
                typeof(System.Collections.Generic.HashSet<>),
                typeof(System.Collections.Generic.Queue<>),
                typeof(System.Collections.Generic.Stack<>),
                typeof(System.Collections.Generic.LinkedList<>),
                typeof(System.Collections.Generic.SortedDictionary<,>),
                typeof(System.Collections.Generic.KeyValuePair<,>),
                typeof(System.Collections.Generic.IEnumerable<>),
                typeof(System.Collections.Generic.IList<>),
                typeof(System.Collections.Generic.IDictionary<,>),

                // System.Text
                typeof(System.Text.StringBuilder),
                typeof(System.Text.Encoding),
                typeof(System.Text.RegularExpressions.Regex),

                // System.IO
                typeof(System.IO.File), typeof(System.IO.Directory), typeof(System.IO.Path),
                typeof(System.IO.FileInfo), typeof(System.IO.DirectoryInfo),
                typeof(System.IO.StreamReader), typeof(System.IO.StreamWriter),
                typeof(System.IO.MemoryStream), typeof(System.IO.Stream),
                typeof(System.IO.FileStream), typeof(System.IO.TextReader),
                typeof(System.IO.TextWriter),

                // System.Linq / diagnostics
                typeof(System.Linq.Enumerable),
                typeof(System.Diagnostics.Stopwatch),
                typeof(System.Threading.Tasks.Task),
                typeof(System.Threading.Tasks.Task<>)
            };

            foreach (var type in coreTypes)
            {
                RegisterRuntimeType(type);
            }
        }

        /// <summary>
        /// Register a single runtime type (and index it by full, simple and —
        /// for generics — bare name) so GetType lookups resolve it.
        /// </summary>
        private void RegisterRuntimeType(Type type)
        {
            try
            {
                lock (_stateLock)
                {
                    if (type.FullName != null && _typesByName.ContainsKey(type.FullName))
                        return;

                    var info = CreateNetTypeInfo(type);
                    var ns = type.Namespace ?? string.Empty;

                    if (!_loadedTypes.ContainsKey(ns))
                        _loadedTypes[ns] = new List<NetTypeInfo>();
                    _loadedTypes[ns].Add(info);

                    if (type.FullName != null)
                        _typesByName[type.FullName] = info;
                    if (!_typesByName.ContainsKey(type.Name))
                        _typesByName[type.Name] = info;

                    // Generic definitions: also index by the bare name ("List" -> List`1)
                    if (type.IsGenericTypeDefinition)
                    {
                        var bareName = type.Name.Split('`')[0];
                        if (!_typesByName.ContainsKey(bareName))
                            _typesByName[bareName] = info;
                    }

                    _loadedNamespaces.Add(ns);
                }
            }
            catch
            {
                // Best-effort preload — never fail initialization
            }
        }

        /// <summary>
        /// Load types for a specific namespace (called when Using statement is encountered)
        /// </summary>
        public bool LoadNamespace(string namespaceName)
        {
            lock (_stateLock)
            {
                if (_loadedNamespaces.Contains(namespaceName))
                    return true;

                // Find assemblies containing this namespace
                if (!_namespaceIndex.TryGetValue(namespaceName, out var assemblies))
                {
                    // Try parent namespace matching (e.g., "System" should match "System.IO")
                    var matchingNamespaces = _namespaceIndex.Keys
                        .Where(k => k.StartsWith(namespaceName + ".", StringComparison.OrdinalIgnoreCase) ||
                                   k.Equals(namespaceName, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (matchingNamespaces.Count == 0)
                        return false;

                    assemblies = matchingNamespaces
                        .SelectMany(ns => _namespaceIndex[ns])
                        .Distinct()
                        .ToList();
                }

                // Load types from each assembly. One resolver for the whole batch — see BuildIndex.
                // The paths may come from the on-disk cache and so predate this process, which is the
                // one case where they are legitimately outside the current reference set.
                var resolver = ResolverFor(assemblies);
                foreach (var assemblyPath in assemblies)
                {
                    LoadTypesFromAssembly(resolver, assemblyPath, namespaceName);
                }

                _loadedNamespaces.Add(namespaceName);
                return true;
            }
        }

        /// <summary>
        /// CALLER MUST HOLD <see cref="_stateLock"/> — the only call site is
        /// <see cref="LoadNamespace"/>, which holds it across the whole batch so that a namespace
        /// becomes visible all at once rather than assembly by assembly.
        /// </summary>
        private void LoadTypesFromAssembly(NetTypeResolver resolver, string assemblyPath, string targetNamespace)
        {
            if (!CanRead(resolver, assemblyPath))
                return;

            foreach (var type in resolver.PublicTypesIn(assemblyPath))
            {
                // Only load types from the target namespace (or child namespaces)
                var ns = NamespaceOf(type);
                if (ns == null)
                    continue;

                if (!ns.Equals(targetNamespace, StringComparison.OrdinalIgnoreCase) &&
                    !ns.StartsWith(targetNamespace + ".", StringComparison.OrdinalIgnoreCase))
                    continue;

                var typeInfo = CreateNetTypeInfo(type);

                if (!_loadedTypes.ContainsKey(ns))
                    _loadedTypes[ns] = new List<NetTypeInfo>();

                _loadedTypes[ns].Add(typeInfo);
                _typesByName[typeInfo.FullName] = typeInfo;
                _typesByName[typeInfo.Name] = typeInfo; // Also index by simple name
            }
        }

        /// <summary>
        /// Load all types from an assembly (used for NuGet packages and project
        /// <c>&lt;Reference&gt;</c>s).
        ///
        /// <para>Returns an empty list — never throws — when the assembly cannot be read, leaving a
        /// BL6021 in <see cref="Diagnostics"/>. Every path that reaches here comes from user text: a
        /// <c>&lt;HintPath&gt;</c> can name a native DLL, a truncated file, or something deleted
        /// between resolution and use.</para>
        /// </summary>
        public List<NetTypeInfo> LoadTypesFromAssembly(string assemblyPath)
        {
            var types = new List<NetTypeInfo>();

            var resolver = ResolverFor(new[] { assemblyPath });
            if (!CanRead(resolver, assemblyPath))
                return types;

            lock (_stateLock)
            {
                foreach (var type in resolver.PublicTypesIn(assemblyPath))
                {
                    var ns = NamespaceOf(type);
                    if (ns == null)
                        continue;

                    var fullName = MetadataFullName(type);

                    // Check if already loaded
                    if (_typesByName.ContainsKey(fullName))
                        continue;

                    var typeInfo = CreateNetTypeInfo(type);
                    types.Add(typeInfo);

                    if (!_loadedTypes.ContainsKey(ns))
                        _loadedTypes[ns] = new List<NetTypeInfo>();

                    _loadedTypes[ns].Add(typeInfo);
                    _typesByName[typeInfo.FullName] = typeInfo;
                    _typesByName[typeInfo.Name] = typeInfo;

                    // Track namespace as loaded
                    _loadedNamespaces.Add(ns);
                }
            }

            return types;
        }

        /// <summary>
        /// Get extension methods for a given type
        /// </summary>
        public IEnumerable<NetMemberInfo> GetExtensionMethods(string typeName)
        {
            var extensionMethods = new List<NetMemberInfo>();

            // Snapshot under the lock: enumerating _typesByName.Values while another thread inserts
            // throws InvalidOperationException. A type's own Members list is append-only during
            // construction and frozen once published, so it is safe to walk outside the lock.
            List<NetTypeInfo> staticTypes;
            lock (_stateLock)
            {
                staticTypes = _typesByName.Values.Where(t => t.IsStatic).ToList();
            }

            // Look through all static types for extension methods
            foreach (var typeInfo in staticTypes)
            {
                foreach (var member in typeInfo.Members)
                {
                    if (member.Kind == NetMemberKind.Method &&
                        member.IsStatic &&
                        member.Parameters.Count > 0 &&
                        member.IsExtensionMethod)
                    {
                        // Check if first parameter type matches
                        var firstParamType = member.Parameters[0].Type;
                        if (IsTypeCompatible(typeName, firstParamType))
                        {
                            extensionMethods.Add(member);
                        }
                    }
                }
            }

            return extensionMethods;
        }

        private bool IsTypeCompatible(string actualType, string expectedType)
        {
            if (actualType.Equals(expectedType, StringComparison.OrdinalIgnoreCase))
                return true;

            // Handle generic type compatibility (e.g., List(Of String) with IEnumerable(Of T))
            if (expectedType.Contains("(Of T)") || expectedType.Contains("(Of TSource)"))
            {
                // Generic extension method - check base type
                var baseName = expectedType.Split('(')[0];
                if (baseName == "IEnumerable" && actualType.Contains("List") ||
                    actualType.Contains("Array") ||
                    actualType.Contains("Collection"))
                    return true;
            }

            return false;
        }

        private NetTypeInfo CreateNetTypeInfo(Type type)
        {
            var info = new NetTypeInfo
            {
                Name = type.Name,
                FullName = type.FullName,
                Namespace = type.Namespace,
                IsStatic = type.IsAbstract && type.IsSealed,
                IsClass = type.IsClass,
                IsInterface = type.IsInterface,
                IsEnum = type.IsEnum,
                IsStruct = type.IsValueType && !type.IsEnum,
                IsGeneric = type.IsGenericType,
                GenericParameters = type.IsGenericType
                    ? type.GetGenericArguments().Select(a => a.Name).ToList()
                    : new List<string>(),
                BaseType = type.BaseType != null ? GetTypeName(type.BaseType) : null,
                Interfaces = type.GetInterfaces().Select(i => GetTypeName(i)).ToList()
            };

            // Load constructors
            foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            {
                var ctorInfo = new NetMemberInfo
                {
                    Name = "New",
                    Kind = NetMemberKind.Constructor,
                    ReturnType = type.Name,
                    Parameters = ctor.GetParameters()
                        .Select(p => new NetParameterInfo
                        {
                            Name = p.Name,
                            Type = GetTypeName(p.ParameterType),
                            IsOptional = p.IsOptional
                        }).ToList()
                };
                info.Constructors.Add(ctorInfo);
            }

            // Load members
            var bindingFlags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;

            foreach (var method in type.GetMethods(bindingFlags))
            {
                if (method.IsSpecialName) continue; // Skip property accessors

                // Check for extension method attribute
                var isExtension = method.IsStatic &&
                    method.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false);

                var memberInfo = new NetMemberInfo
                {
                    Name = method.Name,
                    Kind = NetMemberKind.Method,
                    ReturnType = GetTypeName(method.ReturnType),
                    IsStatic = method.IsStatic,
                    IsExtensionMethod = isExtension,
                    IsGeneric = method.IsGenericMethod,
                    GenericParameters = method.IsGenericMethod
                        ? method.GetGenericArguments().Select(a => a.Name).ToList()
                        : new List<string>(),
                    Parameters = method.GetParameters()
                        .Select(p => new NetParameterInfo
                        {
                            Name = p.Name,
                            Type = GetTypeName(p.ParameterType),
                            IsOptional = p.IsOptional
                        }).ToList()
                };
                info.Members.Add(memberInfo);
            }

            foreach (var prop in type.GetProperties(bindingFlags))
            {
                var memberInfo = new NetMemberInfo
                {
                    Name = prop.Name,
                    Kind = NetMemberKind.Property,
                    ReturnType = GetTypeName(prop.PropertyType),
                    IsStatic = prop.GetMethod?.IsStatic ?? prop.SetMethod?.IsStatic ?? false,
                    CanRead = prop.CanRead,
                    CanWrite = prop.CanWrite
                };
                info.Members.Add(memberInfo);
            }

            foreach (var field in type.GetFields(bindingFlags))
            {
                var memberInfo = new NetMemberInfo
                {
                    Name = field.Name,
                    Kind = NetMemberKind.Field,
                    ReturnType = GetTypeName(field.FieldType),
                    IsStatic = field.IsStatic
                };
                info.Members.Add(memberInfo);
            }

            if (type.IsEnum)
            {
                foreach (var enumValue in Enum.GetNames(type))
                {
                    var memberInfo = new NetMemberInfo
                    {
                        Name = enumValue,
                        Kind = NetMemberKind.EnumValue,
                        ReturnType = type.Name,
                        IsStatic = true
                    };
                    info.Members.Add(memberInfo);
                }
            }

            return info;
        }

        // ------------------------------------------------------------------
        // The metadata producer. Mirrors CreateNetTypeInfo(Type) above member for member; see the
        // class remarks for why both exist.
        // ------------------------------------------------------------------

        /// <summary>
        /// Reflection's <c>Type.Namespace</c>: the ENCLOSING namespace, so a nested type reports
        /// <c>System</c> and not <c>System.Environment</c>. Null for the global namespace, which is
        /// the value both callers skip on — matching reflection, where <c>Type.Namespace</c> is null
        /// rather than empty for a type declared outside any namespace.
        /// </summary>
        private static string NamespaceOf(INamedTypeSymbol type)
        {
            var outermost = type;
            while (outermost.ContainingType != null)
                outermost = outermost.ContainingType;

            var ns = outermost.ContainingNamespace;
            return ns == null || ns.IsGlobalNamespace ? null : ns.ToDisplayString();
        }

        /// <summary>
        /// Reflection's <c>Type.FullName</c>: namespace-qualified, nesting spelled with <c>+</c>,
        /// generic arity kept (<c>System.Collections.Generic.Dictionary`2+Enumerator</c>). This is
        /// the spelling the registry indexes and the LSP looks up, so it has to match exactly.
        /// </summary>
        private static string MetadataFullName(INamedTypeSymbol type)
        {
            var parts = new List<string>();
            for (var current = type; current != null; current = current.ContainingType)
                parts.Insert(0, current.MetadataName);

            var nested = string.Join("+", parts);
            var ns = NamespaceOf(type);
            return ns == null ? nested : ns + "." + nested;
        }

        /// <summary>
        /// Reflection's <c>Type.GetGenericArguments()</c>, which for a type nested inside a generic
        /// includes the ENCLOSING type's parameters — <c>Dictionary`2+Enumerator</c> reports
        /// <c>[TKey, TValue]</c> and is <c>IsGenericType</c> even though it declares none of its own.
        /// Roslyn's <c>TypeParameters</c> is per-level, so the chain has to be walked outermost-first
        /// to reproduce both the membership and the order.
        /// </summary>
        private static List<string> GenericParameterNames(INamedTypeSymbol type)
        {
            var levels = new List<INamedTypeSymbol>();
            for (var current = type; current != null; current = current.ContainingType)
                levels.Insert(0, current);

            var names = new List<string>();
            foreach (var level in levels)
            {
                foreach (var parameter in level.TypeParameters)
                    names.Add(parameter.Name);
            }
            return names;
        }

        private NetTypeInfo CreateNetTypeInfo(INamedTypeSymbol type)
        {
            var genericParameters = GenericParameterNames(type);

            var info = new NetTypeInfo
            {
                Name = type.MetadataName,          // reflection's Type.Name keeps the `1 arity suffix
                FullName = MetadataFullName(type),
                Namespace = NamespaceOf(type),
                // reflection computed this as IsAbstract && IsSealed, which is what a static class
                // is in metadata and what INamedTypeSymbol.IsStatic reports.
                IsStatic = type.IsStatic,
                // reflection's Type.IsClass is !IsInterface && !IsValueType, so it is TRUE for a
                // delegate. TypeKind splits them, so Delegate has to be folded back in.
                IsClass = type.TypeKind == RoslynTypeKind.Class || type.TypeKind == RoslynTypeKind.Delegate,
                IsInterface = type.TypeKind == RoslynTypeKind.Interface,
                IsEnum = type.TypeKind == RoslynTypeKind.Enum,
                IsStruct = type.IsValueType && type.TypeKind != RoslynTypeKind.Enum,
                IsGeneric = genericParameters.Count > 0,
                GenericParameters = genericParameters,
                BaseType = type.BaseType != null ? GetTypeName(type.BaseType) : null,
                // reflection's GetInterfaces() is TRANSITIVE, so AllInterfaces and not Interfaces.
                // Verified equal in count on DayOfWeek (4) and StringBuilder (1).
                Interfaces = type.AllInterfaces.Select(GetTypeName).ToList()
            };

            // Constructors: reflection's GetConstructors(Public | Instance).
            //
            // IsImplicitlyDeclared is load-bearing. Roslyn synthesizes a public parameterless
            // constructor for every metadata VALUE type; reflection reports none, because metadata
            // holds no such token — typeof(Gear).GetConstructors(...) is empty and
            // typeof(DateTime)'s 17 are all declared. Without this filter every struct gains a
            // constructor the LSP would offer and no call could reach.
            foreach (var constructor in type.InstanceConstructors)
            {
                if (constructor.DeclaredAccessibility != Accessibility.Public
                    || constructor.IsImplicitlyDeclared)
                    continue;

                info.Constructors.Add(new NetMemberInfo
                {
                    Name = "New",
                    Kind = NetMemberKind.Constructor,
                    ReturnType = type.MetadataName,
                    Parameters = DescribeParameters(constructor.Parameters)
                });
            }

            AddMembers(info, type);
            return info;
        }

        /// <summary>
        /// Reproduces <c>GetMethods/GetProperties/GetFields(Public | Instance | Static)</c> — the
        /// exact flags the reflection producer passes, and the three rules they imply that a naive
        /// "members of this type" walk gets wrong:
        ///
        /// <list type="number">
        /// <item><description><b>Inherited INSTANCE members are included</b> — the flags omit
        /// <c>DeclaredOnly</c>, so the base chain is walked and <c>System.Object</c>'s
        /// <c>ToString</c>/<c>Equals</c>/<c>GetHashCode</c>/<c>GetType</c> are part of every class's
        /// completion list. <see cref="NetTypeResolver.GetMembers"/> deliberately EXCLUDES those,
        /// because spec §7.2 is about which members deserve a shim export — a different question
        /// with a different right answer. The two member sets are not interchangeable, and using
        /// §7.2's here would silently shorten every completion list.</description></item>
        /// <item><description><b>Inherited STATIC members are NOT included</b> — that needs
        /// <c>FlattenHierarchy</c>, which the flags omit. Measured on a two-level hierarchy:
        /// <c>Derived</c> reports its own statics and the base's INSTANCE members, but not the
        /// base's statics, and not <c>Object.ReferenceEquals</c>.</description></item>
        /// <item><description><b>An override appears once</b>, under its most-derived declaration.
        /// The walk is derived-to-base and first-seen wins, so the signature key collapses an
        /// override onto the member it overrides — which is what reflection reports (measured: one
        /// <c>Overridden</c>, <c>DeclaringType</c> = <c>Derived</c>). The key therefore has to carry
        /// generic arity and parameter types, or two real overloads collapse into one.</description>
        /// </item>
        /// </list>
        ///
        /// <para>Members are appended methods-then-properties-then-fields-then-enum-values, matching
        /// the reflection producer's order, because the LSP renders this list directly.</para>
        /// </summary>
        private void AddMembers(NetTypeInfo info, INamedTypeSymbol type)
        {
            var methods = new List<NetMemberInfo>();
            var properties = new List<NetMemberInfo>();
            var fields = new List<NetMemberInfo>();

            // Separators are written as ESCAPES, never as raw control bytes — see
            // NetOverloadProbe.ResolveOverload's key, which spells the same two the same way. U+0001
            // and U+0002 cannot occur in a metadata name or a VB type spelling, which is what keeps
            // two different signatures from sharing one key; the VB spellings already contain '(',
            // ')', ',' and spaces, so an ordinary punctuation separator would not.
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (var current = type; current != null; current = current.BaseType)
            {
                var isQueriedType = ReferenceEquals(current, type);

                foreach (var member in current.GetMembers())
                {
                    if (member.DeclaredAccessibility != Accessibility.Public)
                        continue;

                    // Rule 2: a base type contributes instance members only.
                    if (!isQueriedType && member.IsStatic)
                        continue;

                    switch (member)
                    {
                        case IMethodSymbol method when method.MethodKind == MethodKind.Ordinary:
                            // reflection skipped IsSpecialName, which is what kept property and
                            // event accessors and user-defined operators out. MethodKind.Ordinary
                            // is the metadata-symbol equivalent.
                            if (!seen.Add("M\u0001" + method.Name + "\u0001" + method.Arity + "\u0001"
                                          + string.Join("\u0002", method.Parameters.Select(p => GetParameterTypeName(p)))))
                                continue;

                            methods.Add(new NetMemberInfo
                            {
                                Name = method.Name,
                                Kind = NetMemberKind.Method,
                                ReturnType = GetTypeName(method.ReturnType),
                                IsStatic = method.IsStatic,
                                IsExtensionMethod = method.IsExtensionMethod,
                                IsGeneric = method.Arity > 0,
                                GenericParameters = method.Arity > 0
                                    ? method.TypeParameters.Select(p => p.Name).ToList()
                                    : new List<string>(),
                                Parameters = DescribeParameters(method.Parameters)
                            });
                            break;

                        case IPropertySymbol property:
                            // MetadataName, so an indexer is "Item" as reflection reported it, and
                            // its parameters are part of the key — two indexers differ only there.
                            if (!seen.Add("P\u0001" + property.MetadataName + "\u0001"
                                          + string.Join("\u0002", property.Parameters.Select(p => GetParameterTypeName(p)))))
                                continue;

                            properties.Add(new NetMemberInfo
                            {
                                Name = property.MetadataName,
                                Kind = NetMemberKind.Property,
                                ReturnType = GetTypeName(property.Type),
                                IsStatic = property.IsStatic,
                                CanRead = property.GetMethod != null,
                                CanWrite = property.SetMethod != null
                            });
                            break;

                        case IFieldSymbol field:
                            if (!seen.Add("F\u0001" + field.Name))
                                continue;

                            fields.Add(new NetMemberInfo
                            {
                                Name = field.Name,
                                Kind = NetMemberKind.Field,
                                ReturnType = GetTypeName(field.Type),
                                IsStatic = field.IsStatic
                            });
                            break;
                    }
                }
            }

            info.Members.AddRange(methods);
            info.Members.AddRange(properties);
            info.Members.AddRange(fields);

            if (type.TypeKind == RoslynTypeKind.Enum)
            {
                // reflection added Enum.GetNames(type) on TOP of the literal fields GetFields
                // already returned, so each name legitimately appears twice — once as a Field and
                // once as an EnumValue. Preserved: CompletionService renders the two kinds
                // differently and has always seen both.
                foreach (var member in type.GetMembers())
                {
                    if (member is IFieldSymbol literal && literal.HasConstantValue
                        && literal.DeclaredAccessibility == Accessibility.Public)
                    {
                        info.Members.Add(new NetMemberInfo
                        {
                            Name = literal.Name,
                            Kind = NetMemberKind.EnumValue,
                            ReturnType = type.MetadataName,
                            IsStatic = true
                        });
                    }
                }
            }
        }

        private List<NetParameterInfo> DescribeParameters(
            System.Collections.Immutable.ImmutableArray<IParameterSymbol> parameters) =>
            parameters
                .Select(p => new NetParameterInfo
                {
                    Name = p.Name,
                    Type = GetParameterTypeName(p),
                    IsOptional = p.IsOptional
                })
                .ToList();

        /// <summary>
        /// A parameter's type, with the by-ref suffix re-added. Reflection models by-ref-ness on the
        /// TYPE (it hands <see cref="GetTypeName(Type)"/> a by-ref <see cref="Type"/>); Roslyn models
        /// it on the PARAMETER, so <see cref="IParameterSymbol.Type"/> is the bare type and the
        /// suffix has to be restored here or every by-ref overload's signature help loses it.
        ///
        /// <para>Both producers spell the result the VB way — <c>Integer&amp;</c>, not
        /// <c>Int32&amp;</c>. See <see cref="GetTypeName(Type)"/>'s first two lines for why the
        /// reflection side had to change to agree, and
        /// <c>TypeRegistryMetadataTests.TheReflectionAndMetadataProducersAgreeOnShape</c> for the
        /// pin.</para>
        /// </summary>
        private string GetParameterTypeName(IParameterSymbol parameter)
        {
            var name = GetTypeName(parameter.Type);
            return parameter.RefKind == RefKind.None ? name : name + "&";
        }

        /// <summary>
        /// The metadata mirror of <see cref="GetTypeName(Type)"/>, arm for arm and in the same order
        /// — VB primitive spellings, then <c>Name(Of …)</c> for a constructed generic, then
        /// <c>Name()</c> for an array, then the bare simple name.
        /// </summary>
        private string GetTypeName(ITypeSymbol type)
        {
            switch (type.SpecialType)
            {
                case SpecialType.System_Void: return "Void";
                case SpecialType.System_Int32: return "Integer";
                case SpecialType.System_Int64: return "Long";
                case SpecialType.System_Single: return "Single";
                case SpecialType.System_Double: return "Double";
                case SpecialType.System_String: return "String";
                case SpecialType.System_Boolean: return "Boolean";
                case SpecialType.System_Char: return "Char";
                case SpecialType.System_Byte: return "Byte";
                case SpecialType.System_Object: return "Object";
            }

            if (type is INamedTypeSymbol named && named.TypeArguments.Length > 0)
            {
                var args = string.Join(", ", named.TypeArguments.Select(GetTypeName));
                return $"{named.Name}(Of {args})";
            }

            if (type is IArrayTypeSymbol array)
                return $"{GetTypeName(array.ElementType)}()";

            // Roslyn's IPointerTypeSymbol.Name is empty, so a pointer is the one shape whose Name is
            // unusable and has to be spelled out rather than silently becoming "". The reflection
            // producer unwraps pointers too, so both agree on "Integer*".
            if (type is IPointerTypeSymbol pointer)
                return $"{GetTypeName(pointer.PointedAtType)}*";

            return type.Name;
        }

        private string GetTypeName(Type type)
        {
            // BY-REF AND POINTER ARE UNWRAPPED FIRST, and this is a deliberate change from what
            // reflection did on its own. typeof(int).MakeByRefType() is not typeof(int), so none of
            // the primitive arms below ever matched a by-ref parameter and the ladder fell through to
            // Type.Name — spelling Integer.TryParse's second parameter "Int32&" while the metadata
            // producer spells it "Integer&". Both producers are live in ONE registry
            // (PreloadCoreTypes registers System.Int32 by reflection while a project <Reference> is
            // read as metadata), so that divergence put two spellings of the same signature into two
            // completion lists. VB spelling wins for both: every other name this class produces is
            // already VB ("Integer", "String", "List(Of T)"), so "Int32&" was the odd one out.
            if (type.IsByRef) return GetTypeName(type.GetElementType()) + "&";
            if (type.IsPointer) return GetTypeName(type.GetElementType()) + "*";

            if (type == typeof(void)) return "Void";
            if (type == typeof(int)) return "Integer";
            if (type == typeof(long)) return "Long";
            if (type == typeof(float)) return "Single";
            if (type == typeof(double)) return "Double";
            if (type == typeof(string)) return "String";
            if (type == typeof(bool)) return "Boolean";
            if (type == typeof(char)) return "Char";
            if (type == typeof(byte)) return "Byte";
            if (type == typeof(object)) return "Object";

            if (type.IsGenericType)
            {
                var genericName = type.Name.Split('`')[0];
                var args = string.Join(", ", type.GetGenericArguments().Select(GetTypeName));
                return $"{genericName}(Of {args})";
            }

            if (type.IsArray)
            {
                return $"{GetTypeName(type.GetElementType())}()";
            }

            return type.Name;
        }

        /// <summary>
        /// Get all types in a loaded namespace
        /// </summary>
        public IEnumerable<NetTypeInfo> GetTypesInNamespace(string namespaceName)
        {
            lock (_stateLock)
            {
                // A COPY, not the live list: the caller would otherwise be enumerating the same
                // List<T> that LoadNamespace appends to.
                if (_loadedTypes.TryGetValue(namespaceName, out var types))
                    return types.ToList();

                // Also check for child namespaces
                var result = new List<NetTypeInfo>();
                foreach (var kvp in _loadedTypes)
                {
                    if (kvp.Key.StartsWith(namespaceName + ".") || kvp.Key.Equals(namespaceName, StringComparison.OrdinalIgnoreCase))
                    {
                        result.AddRange(kvp.Value);
                    }
                }

                return result;
            }
        }

        /// <summary>
        /// Get a specific type by name
        /// </summary>
        public NetTypeInfo GetType(string typeName)
        {
            lock (_stateLock)
            {
                _typesByName.TryGetValue(typeName, out var type);
                return type;
            }
        }

        /// <summary>
        /// Get all loaded types (for completion)
        /// </summary>
        public IEnumerable<NetTypeInfo> GetAllLoadedTypes()
        {
            lock (_stateLock)
            {
                // Materialized inside the lock. Returning the lazy Distinct() would defer the
                // enumeration of _typesByName.Values to the caller, i.e. to outside the lock.
                return _typesByName.Values.Distinct().ToList();
            }
        }

        /// <summary>
        /// Get members of a type by type name
        /// </summary>
        public IEnumerable<NetMemberInfo> GetTypeMembers(string typeName)
        {
            lock (_stateLock)
            {
                if (_typesByName.TryGetValue(typeName, out var type))
                    return type.Members;
            }

            return Enumerable.Empty<NetMemberInfo>();
        }

        /// <summary>
        /// Check if a namespace is loaded
        /// </summary>
        public bool IsNamespaceLoaded(string namespaceName)
        {
            lock (_stateLock)
            {
                return _loadedNamespaces.Contains(namespaceName);
            }
        }

        /// <summary>
        /// Get all loaded namespaces. A COPY — handing out the live <see cref="HashSet{T}"/> let a
        /// caller enumerate it while <see cref="LoadNamespace"/> added to it.
        /// </summary>
        public IEnumerable<string> GetLoadedNamespaces()
        {
            lock (_stateLock)
            {
                return _loadedNamespaces.ToList();
            }
        }

        /// <summary>
        /// The directory whose LEAF name carries the highest version — <b>never a string sort</b>.
        /// <c>"9.0.12"</c> outranks <c>"10.0.0"</c> ordinally and <c>"net9.0"</c> outranks
        /// <c>"net10.0"</c>, so the <c>OrderByDescending(d =&gt; d)</c> this replaces (here and in
        /// <c>WorkspaceManager.InitializeTypeRegistry</c>) could never select .NET 10 once
        /// installed.
        ///
        /// <para>Keying: an optional leading <c>net</c> is stripped (ref monikers), the numeric
        /// prefix before any <c>-</c> is taken (so <c>10.0.0-preview.7.25380.108</c> keys as
        /// 10.0.0), and the remainder must <see cref="Version.TryParse(string, out Version)"/> —
        /// both <c>10.0</c> and <c>10.0.0</c> shapes parse. Directories whose names do not parse
        /// sort BELOW every parseable one, ordered ordinally among themselves for determinism.
        /// Returns <c>null</c> for empty input.</para>
        /// </summary>
        internal static string PickNewestVersionDirectory(IEnumerable<string> directories)
        {
            if (directories == null)
                return null;

            return directories
                .Where(d => !string.IsNullOrEmpty(d))
                .Select(d => new { Dir = d, Version = ParseVersionLeaf(Path.GetFileName(d)) })
                .OrderByDescending(x => x.Version != null)
                .ThenByDescending(x => x.Version)
                .ThenByDescending(x => x.Dir, StringComparer.Ordinal)
                .Select(x => x.Dir)
                .FirstOrDefault();
        }

        /// <summary>
        /// The version a directory leaf name denotes, or <c>null</c> when it denotes none — see
        /// <see cref="PickNewestVersionDirectory"/> for the accepted shapes.
        /// </summary>
        private static Version ParseVersionLeaf(string leafName)
        {
            if (string.IsNullOrEmpty(leafName))
                return null;

            var candidate = leafName.StartsWith("net", StringComparison.OrdinalIgnoreCase)
                ? leafName.Substring(3)
                : leafName;

            var dash = candidate.IndexOf('-');
            if (dash >= 0)
                candidate = candidate.Substring(0, dash);

            return Version.TryParse(candidate, out var version) ? version : null;
        }

        /// <summary>
        /// Detect .NET SDK path automatically
        /// </summary>
        public static string DetectDotNetSdkPath()
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var dotnetPath = Path.Combine(programFiles, "dotnet", "packs", "Microsoft.NETCore.App.Ref");

            if (Directory.Exists(dotnetPath))
            {
                // Find the latest version — by version, not by string (see PickNewestVersionDirectory)
                var versions = PickNewestVersionDirectory(Directory.GetDirectories(dotnetPath));

                if (versions != null)
                {
                    // Find ref folder for latest .NET version. Guarded: a corrupted install can
                    // leave the picked pack version without a ref\ subfolder, and the caller
                    // (DocumentManager's LSP init) sits outside any try/catch — GetDirectories on
                    // the missing folder would take down server initialization.
                    var refDir = Path.Combine(versions, "ref");
                    if (!Directory.Exists(refDir))
                        return null;

                    var refPath = PickNewestVersionDirectory(Directory.GetDirectories(refDir));

                    if (refPath != null && Directory.Exists(refPath))
                        return refPath;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Lightweight representation of a .NET type for IntelliSense
    /// </summary>
    public class NetTypeInfo
    {
        public string Name { get; set; }
        public string FullName { get; set; }
        public string Namespace { get; set; }
        public bool IsStatic { get; set; }
        public bool IsClass { get; set; }
        public bool IsInterface { get; set; }
        public bool IsEnum { get; set; }
        public bool IsStruct { get; set; }
        public bool IsGeneric { get; set; }
        public List<string> GenericParameters { get; set; } = new List<string>();
        public string BaseType { get; set; }
        public List<string> Interfaces { get; set; } = new List<string>();
        public List<NetMemberInfo> Members { get; set; } = new List<NetMemberInfo>();
        public List<NetMemberInfo> Constructors { get; set; } = new List<NetMemberInfo>();

        /// <summary>
        /// Get display name for the type (e.g., "List(Of T)" for generic types)
        /// </summary>
        public string DisplayName
        {
            get
            {
                if (IsGeneric && GenericParameters.Count > 0)
                {
                    var baseName = Name.Split('`')[0];
                    return $"{baseName}(Of {string.Join(", ", GenericParameters)})";
                }
                return Name;
            }
        }

        public override string ToString() => FullName ?? Name;
    }

    /// <summary>
    /// Lightweight representation of a .NET type member
    /// </summary>
    public class NetMemberInfo
    {
        public string Name { get; set; }
        public NetMemberKind Kind { get; set; }
        public string ReturnType { get; set; }
        public bool IsStatic { get; set; }
        public bool CanRead { get; set; }
        public bool CanWrite { get; set; }
        public bool IsExtensionMethod { get; set; }
        public bool IsGeneric { get; set; }
        public List<string> GenericParameters { get; set; } = new List<string>();
        public List<NetParameterInfo> Parameters { get; set; } = new List<NetParameterInfo>();

        public string GetSignature()
        {
            if (Kind == NetMemberKind.Method)
            {
                var genericStr = IsGeneric ? $"(Of {string.Join(", ", GenericParameters)})" : "";
                var paramStr = string.Join(", ", Parameters.Select(p => $"{p.Name} As {p.Type}"));
                return $"{Name}{genericStr}({paramStr}) As {ReturnType}";
            }
            return $"{Name} As {ReturnType}";
        }
    }

    /// <summary>
    /// Parameter information for method members
    /// </summary>
    public class NetParameterInfo
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public bool IsOptional { get; set; }
    }

    /// <summary>
    /// Kind of member in a .NET type
    /// </summary>
    public enum NetMemberKind
    {
        Method,
        Property,
        Field,
        Event,
        EnumValue,
        Constructor
    }
}
