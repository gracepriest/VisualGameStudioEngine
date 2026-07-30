using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace BasicLang.Net
{
    /// <summary>
    /// One reference-resolution finding. Deliberately transport-neutral (no
    /// <c>CppDiagnostic</c>, no file/line): the resolver is a pure function over a
    /// <see cref="Compiler.ProjectSystem.ProjectFile"/> and its caller decides how to surface
    /// these. All of them carry BL6021 today (spec §11.4); BL6022 is reserved for
    /// <c>&lt;NetProxy&gt;</c> naming an unknown type and must not be borrowed here.
    /// </summary>
    internal sealed record NetReferenceDiagnostic(string Code, string Message, bool IsWarning);

    /// <summary>
    /// The assembly closure a project can see. <see cref="AssemblyPaths"/> is what the project
    /// DECLARED (<c>&lt;Reference&gt;</c> + <c>&lt;PackageReference&gt;</c>);
    /// <see cref="FrameworkPaths"/> is the always-present framework set and is deliberately a
    /// SEPARATE list — spec §6.5 requires <c>Dim r As New Regex("a")</c> to resolve with no
    /// <c>&lt;Reference&gt;</c> element at all, so the framework set cannot be conditional on
    /// the project having declared something.
    ///
    /// <para>All three lists are de-duplicated by full path and order-stable: Task 15's shim
    /// cache key hashes them, and an unstable order would turn every build into a cache
    /// miss.</para>
    ///
    /// <para><b>Deliberately a class and not a record.</b> Two record features are actively wrong
    /// for this type. (1) <see cref="All"/> is DERIVED from the other two lists, and a record's
    /// copy constructor copies the backing field verbatim — so
    /// <c>closure with { AssemblyPaths = other }</c> would silently hand back an <c>All</c> that
    /// does not contain <c>other</c>. (2) Record equality over <see cref="IReadOnlyList{T}"/>
    /// members degenerates to reference equality, which is a trap for Task 15 specifically:
    /// <c>==</c> would look like a content comparison and behave like an identity one. A class
    /// makes both mistakes unrepresentable — construct a new closure instead of mutating one.</para>
    /// </summary>
    internal sealed class NetReferenceClosure
    {
        public NetReferenceClosure(
            IReadOnlyList<string> assemblyPaths,     // what the project DECLARED
            IReadOnlyList<string> frameworkPaths,    // always populated, independent of declarations
            IReadOnlyList<NetReferenceDiagnostic> diagnostics)
        {
            AssemblyPaths = assemblyPaths;
            FrameworkPaths = frameworkPaths;
            Diagnostics = diagnostics;
            All = frameworkPaths.Concat(assemblyPaths).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Assemblies the project declared, in declaration order.</summary>
        public IReadOnlyList<string> AssemblyPaths { get; }

        /// <summary>
        /// The shared framework's assemblies. See <see cref="NetReferenceResolver"/>'s remarks for
        /// why this is filtered to the shared-framework directory rather than taken from the host
        /// process's TPA list wholesale.
        /// </summary>
        public IReadOnlyList<string> FrameworkPaths { get; }

        public IReadOnlyList<NetReferenceDiagnostic> Diagnostics { get; }

        /// <summary>Everything Roslyn should see. Order-stable and de-duplicated by full path.</summary>
        public IReadOnlyList<string> All { get; }
    }

    /// <summary>
    /// Turns a <c>.blproj</c> into the assembly set the .NET resolver and the AOT shim both need
    /// (spec §5). Pure and synchronous: package RESTORE is async and belongs to the caller, which
    /// hands the already-resolved package assemblies in through
    /// <c>packageAssemblies</c>/<c>packageErrors</c>.
    ///
    /// <para><b>Why this exists.</b> Before P2a-1 every reference element was parsed into the
    /// project model and then silently discarded on the native path
    /// (<c>Program.cs:436</c> returned before restore, and <c>CppProjectBuilder</c> read no
    /// reference item at all), so a typo'd <c>&lt;HintPath&gt;</c> produced no output
    /// whatsoever. BL6021 replaces that silence.</para>
    ///
    /// <para><b>Inertness.</b> A project that declares nothing produces zero diagnostics and an
    /// empty <see cref="NetReferenceClosure.AssemblyPaths"/> — which is every native project that
    /// exists today, and is what lets this run on the build AND IntelliSense paths without
    /// changing the behavior of a single existing program.</para>
    /// </summary>
    internal static class NetReferenceResolver
    {
        /// <summary>
        /// The shared-framework directory: where <c>System.Private.CoreLib</c> lives. Null when it
        /// cannot be determined.
        ///
        /// <para><b>This is the fix for a real trap, and the filter must not be widened.</b> The
        /// framework set has to be the SHARED FRAMEWORK, not "whatever assemblies the host process
        /// trusts" — those are not the same thing. <c>BasicLang.dll</c> runs in two hosts:
        /// <c>BasicLang.exe</c> (the CLI) and <c>VisualGameStudio.exe</c> (the IDE, which is the
        /// only native build path via <c>BuildService</c> and the only IntelliSense path via
        /// <c>IntelliSenseEmissionService</c>). The IDE ships ~40 non-framework managed DLLs beside
        /// itself — <c>Avalonia.*</c>, <c>AvaloniaEdit</c>, <c>Dock.*</c>,
        /// <c>CommunityToolkit.Mvvm</c>, <c>Newtonsoft.Json</c>, <c>SkiaSharp</c> — and every one
        /// of them is in that process's <c>TRUSTED_PLATFORM_ASSEMBLIES</c>. Taking TPA wholesale
        /// would mean <c>&lt;Reference Include="Avalonia" /&gt;</c> with no <c>&lt;HintPath&gt;</c>
        /// resolves clean in the IDE and is a BL6021 error in the CLI for a byte-identical
        /// <c>.blproj</c> — exactly the cross-entry-point divergence this repo's "test both entry
        /// points" rule exists to prevent — and it would make Task 15's cache key host-dependent
        /// and the shim's compile-time framework set depend on who started the build.</para>
        ///
        /// <para>CoreLib's own directory is used rather than probing for an SDK or a targeting
        /// pack: the runtime is by definition present (we are executing on it), so this adds no new
        /// environment requirement to a native path that today needs only a C++ toolchain.</para>
        /// </summary>
        private static string SharedFrameworkDirectory()
        {
            // Assembly.Location is empty inside a single-file bundle; GetRuntimeDirectory still
            // answers there. If neither works the framework set is simply empty (see FrameworkSet).
            var coreLib = typeof(object).Assembly.Location;
            var dir = string.IsNullOrEmpty(coreLib) ? null : Path.GetDirectoryName(coreLib);
            if (string.IsNullOrEmpty(dir))
                dir = RuntimeEnvironment.GetRuntimeDirectory();
            return string.IsNullOrEmpty(dir) ? null : NormalizeDirectory(dir);
        }

        /// <summary>
        /// The framework assembly set: the host's <c>TRUSTED_PLATFORM_ASSEMBLIES</c> RESTRICTED to
        /// <see cref="SharedFrameworkDirectory"/>.
        ///
        /// <para>Intersecting the two is what makes this both host-independent and safe to hand to
        /// Roslyn. TPA alone is host-dependent (see <see cref="SharedFrameworkDirectory"/>);
        /// enumerating the directory alone would pick up the NATIVE DLLs that live there
        /// (<c>coreclr.dll</c>, <c>clrjit.dll</c>, <c>mscordbi.dll</c>, …), which Roslyn cannot read
        /// as metadata. TPA contributes "managed and trusted"; the directory filter contributes
        /// "framework, not the host's own dependencies".</para>
        ///
        /// <para><b>The native-DLL failure is DEFERRED, not thrown</b> — measured while building
        /// <see cref="NetTypeResolver"/>. <c>MetadataReference.CreateFromFile</c> succeeds on
        /// <c>coreclr.dll</c> and on a truncated file; the badness only shows up later, as
        /// <c>Compilation.GetAssemblyOrModuleSymbol</c> returning null for that reference. (It does
        /// throw <c>FileNotFoundException</c> for a path that does not exist.) So the hazard this
        /// filter removes is a SILENT one: unfiltered, a native DLL would sit in the closure looking
        /// fine and simply contribute no types. <see cref="NetTypeResolver"/> now detects that
        /// residual case and reports BL6021 — but keeping it out of the framework set in the first
        /// place is what stops every build from carrying a handful of bogus warnings.</para>
        ///
        /// <para>If either input is unavailable this is empty and resolution proceeds — a project
        /// with no .NET usage is unaffected either way.</para>
        ///
        /// <para>Computed once per process: the list is ~200 entries and each is stat'ed, which is
        /// negligible once but not worth paying on every <c>EmitCore</c> call — and computing it
        /// once is also what makes the order stable across calls.</para>
        /// </summary>
        private static readonly Lazy<IReadOnlyList<string>> FrameworkSet = new(() =>
        {
            var tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
            var frameworkDir = SharedFrameworkDirectory();
            if (string.IsNullOrEmpty(tpa) || frameworkDir == null)
                return (IReadOnlyList<string>)Array.Empty<string>();

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var paths = new List<string>();
            foreach (var entry in tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!entry.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    continue;
                var full = TryGetFullPath(entry);
                if (full == null || !seen.Add(full))
                    continue;
                var dir = Path.GetDirectoryName(full);
                if (dir == null || !string.Equals(NormalizeDirectory(dir), frameworkDir,
                        StringComparison.OrdinalIgnoreCase))
                    continue;   // a HOST dependency, not the shared framework
                if (!File.Exists(full))
                    continue;
                paths.Add(full);
            }
            return paths;
        });

        /// <summary>Simple assembly name -&gt; framework path, first entry wins.</summary>
        private static readonly Lazy<IReadOnlyDictionary<string, string>> FrameworkByName = new(() =>
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in FrameworkSet.Value)
            {
                var name = Path.GetFileNameWithoutExtension(path);
                if (!string.IsNullOrEmpty(name) && !map.ContainsKey(name))
                    map[name] = path;
            }
            return (IReadOnlyDictionary<string, string>)map;
        });

        /// <summary>
        /// Test seam for the drift invariant: the one directory every
        /// <see cref="NetReferenceClosure.FrameworkPaths"/> entry must live in.
        /// </summary>
        internal static string FrameworkDirectoryForTests => SharedFrameworkDirectory();

        /// <summary>
        /// Resolves <paramref name="project"/>'s reference elements into an assembly closure.
        /// </summary>
        /// <param name="projectFilePath">
        /// The <c>.blproj</c> path. <c>&lt;HintPath&gt;</c> resolves relative to THIS file's
        /// directory, never to the output directory — the pre-existing C# backend hazard spec §5
        /// records is that the generated csproj lands in <c>bin/&lt;config&gt;/&lt;TFM&gt;</c>
        /// with HintPath copied verbatim, so a relative HintPath there resolves against the output
        /// dir. If the two ever disagree, the C# backend is the bug.
        /// </param>
        /// <param name="packageAssemblies">
        /// Assemblies produced by the caller's <c>PackageManager</c> restore, or null when the
        /// project declares no packages. Restore is async; keeping it out here is what lets this
        /// stay a pure, synchronously-testable function.
        /// </param>
        /// <param name="packageErrors">Restore failures, mapped one-to-one to BL6021 errors.</param>
        public static NetReferenceClosure Resolve(
            Compiler.ProjectSystem.ProjectFile project, string projectFilePath,
            IReadOnlyList<string> packageAssemblies = null,
            IReadOnlyList<string> packageErrors = null)
        {
            var diagnostics = new List<NetReferenceDiagnostic>();
            var assemblies = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Declare(string fullPath)
            {
                if (seen.Add(fullPath))
                    assemblies.Add(fullPath);
            }

            var projectDir = Path.GetDirectoryName(projectFilePath);
            if (string.IsNullOrEmpty(projectDir))
                projectDir = ".";

            // ---- 1. <Reference> + optional <HintPath> ----
            foreach (var reference in project.AssemblyReferences)
            {
                var include = reference.Name ?? "";

                if (string.IsNullOrWhiteSpace(reference.HintPath))
                {
                    // No HintPath: the only thing that can satisfy it is the framework set,
                    // matched by simple name.
                    if (FrameworkByName.Value.TryGetValue(SimpleAssemblyName(include), out var frameworkPath))
                    {
                        // Also recorded as DECLARED even though it is already in FrameworkPaths —
                        // AssemblyPaths is "what the project asked for". `All` de-duplicates.
                        Declare(frameworkPath);
                        continue;
                    }

                    diagnostics.Add(new NetReferenceDiagnostic("BL6021",
                        $"Reference '{include}' could not be resolved: it has no <HintPath> and no "
                        + "framework assembly of that name exists. Add a <HintPath> pointing at "
                        + "the assembly file, relative to the project file.",
                        IsWarning: false));
                    continue;
                }

                var hint = reference.HintPath;
                var candidate = TryGetFullPath(Path.IsPathRooted(hint) ? hint : Path.Combine(projectDir, hint));
                if (candidate != null && File.Exists(candidate))
                {
                    Declare(candidate);
                    continue;
                }

                diagnostics.Add(new NetReferenceDiagnostic("BL6021",
                    $"Reference '{include}' could not be resolved: <HintPath> '{hint}' does not exist "
                    + $"(resolved to '{candidate ?? hint}', relative to the project file).",
                    IsWarning: false));
            }

            // ---- 2. <PackageReference> (restored by the caller) ----
            foreach (var path in packageAssemblies ?? Array.Empty<string>())
            {
                var full = TryGetFullPath(path);
                if (full != null)
                    Declare(full);
            }
            foreach (var error in packageErrors ?? Array.Empty<string>())
            {
                // BL6021, NOT BL6022 — spec §11.4 reserves BL6022 for <NetProxy> naming an
                // unknown type. A package that will not restore is a reference that will not
                // resolve.
                diagnostics.Add(new NetReferenceDiagnostic("BL6021",
                    "Package reference could not be restored: " + error, IsWarning: false));
            }

            // ---- 3. <ProjectReference> ----
            // A WARNING in P2a-1, promoted to an error at the P2a-2 flip. The IDE writes this
            // element into native projects itself — "Add Project Reference" is gated only on
            // HasSolution && IsProject && Projects.Count >= 2 with NO backend filter
            // (SolutionExplorerViewModel.cs:625-627 -> :689) — and such a project builds fine on
            // master because CppProjectBuilder reads no reference item today. Making it an error
            // here would break projects the IDE itself creates.
            foreach (var include in project.ProjectReferences)
            {
                diagnostics.Add(new NetReferenceDiagnostic("BL6021",
                    $"<ProjectReference> is not used for .NET access on the native path: '{include}' "
                    + "is ignored. Cross-project compilation does not exist on any BasicLang build "
                    + "path — reference the sibling project's BUILT assembly with <Reference> plus "
                    + "<HintPath> instead.",
                    IsWarning: true));
            }

            return new NetReferenceClosure(assemblies, FrameworkSet.Value, diagnostics);
        }

        /// <summary>
        /// The simple name out of an MSBuild <c>Include</c>. A strong display name
        /// (<c>"System.Data, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089"</c>)
        /// is legal MSBuild and is exactly what someone pastes out of a csproj — without this it
        /// could never match a lookup keyed on the file's base name, so the user would be told to
        /// add a <c>&lt;HintPath&gt;</c> for an assembly already sitting in the framework set.
        /// </summary>
        private static string SimpleAssemblyName(string include)
        {
            var comma = include.IndexOf(',');
            return (comma < 0 ? include : include.Substring(0, comma)).Trim();
        }

        /// <summary>A directory path comparable by value: absolute, no trailing separator.</summary>
        private static string NormalizeDirectory(string dir)
        {
            var full = TryGetFullPath(dir);
            return full == null ? null : Path.TrimEndingDirectorySeparator(full);
        }

        /// <summary>
        /// <see cref="Path.GetFullPath(string)"/> that answers null instead of throwing. A
        /// malformed <c>&lt;HintPath&gt;</c> is user input and must become a BL6021, never an
        /// exception escaping the builder.
        /// </summary>
        private static string TryGetFullPath(string path)
        {
            try { return Path.GetFullPath(path); }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException
                                       || ex is PathTooLongException || ex is IOException
                                       || ex is System.Security.SecurityException)
            {
                return null;
            }
        }
    }
}
