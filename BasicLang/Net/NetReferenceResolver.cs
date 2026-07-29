using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
    /// Both lists are de-duplicated by full path and order-stable: Task 15's shim cache key
    /// hashes them, and an unstable order would turn every build into a cache miss.
    /// </summary>
    internal sealed record NetReferenceClosure(
        IReadOnlyList<string> AssemblyPaths,     // what the project DECLARED
        IReadOnlyList<string> FrameworkPaths,    // always populated, independent of declarations
        IReadOnlyList<NetReferenceDiagnostic> Diagnostics)
    {
        /// <summary>Everything Roslyn should see. Order-stable and de-duplicated by full path.</summary>
        public IReadOnlyList<string> All { get; } =
            FrameworkPaths.Concat(AssemblyPaths).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
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
    /// empty <see cref="NetReferenceClosure.AssemblyPaths"/> — which is every native project
    /// that exists today, and is what lets this run on the build AND IntelliSense paths without
    /// changing the behavior of a single existing program.</para>
    /// </summary>
    internal static class NetReferenceResolver
    {
        /// <summary>
        /// The framework assembly set, taken from the compiler's own
        /// <c>TRUSTED_PLATFORM_ASSEMBLIES</c>.
        ///
        /// <para>Deliberately NOT a targeting pack: the compiler is itself a net8.0 process, so
        /// this needs no SDK and no targeting pack installed. That matters because the native
        /// path today requires only a C++ toolchain (<c>CppProjectBuilder.cs</c>'s BL6005 gate)
        /// — depending on a targeting pack would invent a brand-new environment failure mode for
        /// projects that use no .NET at all. If the TPA list is somehow empty this is empty too
        /// and resolution proceeds; a project with no .NET usage is unaffected either way.</para>
        ///
        /// <para>Computed once per process: the list is ~200 entries and each is stat'ed, which
        /// is negligible once but not worth paying on every <c>EmitCore</c> call — and computing
        /// it once is also what makes the order stable across calls.</para>
        /// </summary>
        private static readonly Lazy<IReadOnlyList<string>> FrameworkSet = new(() =>
        {
            var tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
            if (string.IsNullOrEmpty(tpa))
                return (IReadOnlyList<string>)Array.Empty<string>();

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var paths = new List<string>();
            foreach (var entry in tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!entry.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    continue;
                var full = TryGetFullPath(entry);
                if (full == null || !seen.Add(full) || !File.Exists(full))
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
        /// Resolves <paramref name="project"/>'s reference elements into an assembly closure.
        /// </summary>
        /// <param name="projectFilePath">
        /// The <c>.blproj</c> path. <c>&lt;HintPath&gt;</c> resolves relative to THIS file's
        /// directory, never to the output directory — the pre-existing C# backend hazard spec §5
        /// records is that the generated csproj lands in <c>bin/&lt;config&gt;/&lt;TFM&gt;</c>
        /// with HintPath copied verbatim, so a relative HintPath there resolves against the
        /// output dir. If the two ever disagree, the C# backend is the bug.
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
                var name = reference.Name ?? "";

                if (string.IsNullOrWhiteSpace(reference.HintPath))
                {
                    // No HintPath: the only thing that can satisfy it is the framework set,
                    // matched by simple name.
                    if (FrameworkByName.Value.TryGetValue(name, out var frameworkPath))
                    {
                        // Also recorded as DECLARED even though it is already in FrameworkPaths —
                        // AssemblyPaths is "what the project asked for". `All` de-duplicates.
                        Declare(frameworkPath);
                        continue;
                    }

                    diagnostics.Add(new NetReferenceDiagnostic("BL6021",
                        $"Reference '{name}' could not be resolved: it has no <HintPath> and no "
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
                    $"Reference '{name}' could not be resolved: <HintPath> '{hint}' does not exist "
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
