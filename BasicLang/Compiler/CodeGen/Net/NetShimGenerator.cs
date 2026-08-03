using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Net;

namespace BasicLang.Compiler.CodeGen.Net
{
    /// <summary>
    /// Emits the MANAGED half of the .NET boundary for one project (spec §8.1–§8.2): the shim
    /// <c>.csproj</c> and <c>Exports.g.cs</c>, plus the four fixed scaffolding files spliced
    /// from <see cref="BlnetShimSources"/>. <see cref="NetProxyEmitter"/> emits the native half;
    /// the two are held together by §12.4 — one <see cref="NetNameMangler"/> output is
    /// simultaneously the proxy-table slot, this shim's <c>EntryPoint</c> string, and the name of
    /// the generated C# method.
    ///
    /// <para><b>Inertness, same rule as the native side.</b> An EMPTY surface emits NOTHING —
    /// not an empty csproj, not a directory. Every project in the repo has an empty surface
    /// (<see cref="NetSurface.Empty"/>), and a stray <c>.csproj</c> under <c>obj/gen</c> is worse
    /// than a stray header: IDEs and `dotnet build` sweeps discover project files.</para>
    ///
    /// <para><b>Four binding decisions inherited from earlier tasks, restated because breaking
    /// any of them is silent:</b></para>
    /// <list type="number">
    /// <item><description><b>Namespace</b> (Task 11). The generated shim keeps the hand shim's
    /// <c>BlnetTestShim</c> namespace verbatim, so <c>Exports.g.cs</c> must declare it to see
    /// <see cref="BlnetShimSources.HandleTable"/>. <c>BlnetStatus</c> carries no namespace (it is
    /// pinned byte-equal to <see cref="BlnetContract.GenerateStatusEnumCs"/>) and therefore lands
    /// in the GLOBAL namespace; that compiles by fall-out lookup and must not be "fixed".</description></item>
    /// <item><description><b><c>ImplicitUsings=enable</c></b> is required, not stylistic:
    /// <c>HandleTable</c> names <c>List&lt;T&gt;</c>, <c>Stack&lt;T&gt;</c> and LINQ's
    /// <c>Count(predicate)</c> with no <c>using</c> of its own.</description></item>
    /// <item><description><b>Assembly name</b> (Task 13) is
    /// <see cref="ShimAssemblyName"/>'s output, called rather than retyped — the startup TU's
    /// <c>blnet_load_module</c> literal is derived from the same function, and the two
    /// disagreeing is a load failure at process start. (P2a-2 Task 7b MOVED that function here
    /// from <c>CppProjectBuilder</c>: wiring phase 5 makes <c>ProjectSystem</c> call into this
    /// namespace, and the old callback the other way round would have made the dependency
    /// bidirectional.)</description></item>
    /// <item><description><b>Export names</b> come from <see cref="NetNameMangler"/>. There is no
    /// second naming scheme anywhere in this class.</description></item>
    /// </list>
    ///
    /// <para><b>Two deviations from the spec's literal text, both deliberate:</b></para>
    /// <list type="bullet">
    /// <item><description>§8.2 writes the handle encode inline as
    /// <c>*result = rv is null ? 0UL : Table.Create(rv)</c>. This class routes it through a
    /// <c>ToHandle(object?)</c> helper with exactly that body. The behavior is identical and the
    /// helper is strictly more robust: §8.3's default row sends every unrecognized type —
    /// including every ENUM, which is what §8.3's "enum → underlying integral" row cannot express
    /// through a type NAME — down the handle path, and <c>rv is null</c> on a non-nullable value
    /// type is a raw CS0037. The inline form makes a generated shim that returns
    /// <c>RegexOptions</c> fail to compile.</description></item>
    /// <item><description>§8.2's decode is emitted inline, per-argument, as the spec writes
    /// it — there the guard has to be inline, because an early <c>return</c> of the table's
    /// status cannot be factored into a helper without inventing an out-parameter protocol the
    /// spec does not have.</description></item>
    /// </list>
    ///
    /// <para><b>Known gaps — all of them the surface collector's, none shipping behavior in
    /// P2a-1 (nothing populates a surface):</b></para>
    /// <list type="bullet">
    /// <item><description><b>§8.6's array copy helpers and §8.4's delegate dispatcher are NOT
    /// emitted.</b> Both need information a <see cref="NetMemberDescriptor"/> does not carry —
    /// an array's ELEMENT type and a parameter's delegate-ness — and both are §12.4-exempt (they
    /// are not <c>BlnetProxyTable</c> slots). Emitting thirteen speculative, uncalled and
    /// untestable array helpers into every shim would be worse than the honest gap.</description></item>
    /// <item><description><b>Property/field SETTERS.</b> Mirrors
    /// <see cref="NetProxyEmitter"/> exactly: one slot per descriptor, shaped as the getter. §8.5
    /// already implies the fix — the collector hands accessors over as members.</description></item>
    /// <item><description><b>Generic methods.</b> A member with
    /// <see cref="NetMemberDescriptor.Arity"/> &gt; 0 is emitted with NO type arguments, which
    /// compiles only when C# can infer them. <c>[UnmanagedCallersOnly]</c> forbids generic type
    /// parameters outright (§8.2), so only a constructed instantiation is ever exportable and the
    /// descriptor carries arity but not type arguments.</description></item>
    /// <item><description><b>Type names are emitted as C# type syntax</b> after a
    /// <c>global::</c> prefix. A resolver spelling such as <c>List&lt;T&gt;</c> or a nested type
    /// spelled with <c>+</c> would not compile; producing a valid C# spelling is
    /// <c>NetTypeResolver.TypeName</c>'s job, and it is already documented as fully
    /// qualifying.</description></item>
    /// </list>
    ///
    /// <para><b>Hazard for whoever wires this into the build.</b> The shim lands under the
    /// user's <c>obj/gen/shim/</c>, so a <c>Directory.Build.props</c> anywhere above it is
    /// imported BEFORE this csproj's own <c>PropertyGroup</c> and can rewrite §8.1's properties
    /// (a repo-wide <c>TargetFramework</c>, <c>TreatWarningsAsErrors</c>, central package
    /// management). <c>ImportDirectoryBuildProps=false</c> does NOT help — it is read before the
    /// project body evaluates — so the fix, if this ever bites, is to publish the shim from a
    /// staged copy outside the user tree.</para>
    /// </summary>
    internal static class NetShimGenerator
    {
        /// <summary>Task 11's binding decision. See this class's header, decision 1.</summary>
        internal const string ShimNamespace = "BlnetTestShim";

        /// <summary>
        /// The shim's target framework (spec §8.1). <c>net8.0</c> is the NU1201 floor and the
        /// AOT-analysis floor. Named rather than inlined because §10.2 puts the TFM in the shim
        /// cache key, and a key that disagreed with the csproj would hit across a TFM change.
        /// </summary>
        internal const string TargetFramework = "net8.0";

        /// <summary>The one per-project source file — everything else is fixed scaffolding.</summary>
        internal const string ExportsFileName = "Exports.g.cs";

        /// <summary>
        /// Spliced from <see cref="BlnetShimSources.HandleTable"/>.
        ///
        /// <para><b>Named without a <c>.g.</c> infix on purpose</b>, matching spec §9.1's
        /// inventory ("<c>HandleTable.cs</c> / <c>BlnetStatus.cs</c> from BlnetShimSources") and
        /// the hand shim these are verbatim copies of, rather than
        /// <see cref="BlnetShimSources"/>' doc comments, which call them <c>*.g.cs</c>. This is
        /// not cosmetic: Roslyn classifies any <c>*.g.cs</c> file as auto-generated and then
        /// requires an explicit <c>#nullable</c> directive before it will honour a nullable
        /// annotation (CS8669). These three files are spliced BYTE-FOR-BYTE from
        /// <see cref="BlnetShimSources"/> — §12.4's invariant — so a directive cannot be
        /// prepended to them, which leaves the file name as the only lever.
        /// <see cref="ExportsFileName"/> keeps its <c>.g.cs</c> (the plan and §9.1 both name it
        /// that) and carries the directive instead, since this class owns its text.</para>
        /// </summary>
        internal const string HandleTableFileName = "HandleTable.cs";

        /// <summary>Spliced from <see cref="BlnetShimSources.BlnetStatusCs"/>. See
        /// <see cref="HandleTableFileName"/> for why the name carries no <c>.g.</c> infix.</summary>
        internal const string StatusFileName = "BlnetStatus.cs";

        /// <summary>Spliced from <see cref="BlnetShimSources.ShimAbiCs"/>. See
        /// <see cref="HandleTableFileName"/> for why the name carries no <c>.g.</c> infix.</summary>
        internal const string ShimAbiFileName = "ShimAbi.cs";

        /// <summary>
        /// Spliced from <see cref="BlnetShimSources.MarshalCs"/> — spec §6.4's conversion pairs,
        /// managed side (P2a-2 Task 6). Carries no <c>.g.</c> infix for the same CS8669 reason as
        /// its three siblings; unlike them it is not byte-pinned to a hand-shim copy (the hand
        /// shim predates the conversion pairs) and instead carries its own <c>#nullable</c>
        /// directive and explicit usings (a raw Roslyn compile has no ImplicitUsings).
        /// </summary>
        internal const string MarshalFileName = "BlnetMarshal.cs";

        /// <summary>
        /// The shim project's file name. <c>dotnet publish -p:NativeLib=Shared</c> names its
        /// native output after the PROJECT, and <see cref="NetShimPublisher.ExpectedDllPath"/>
        /// re-derives it the same way, so this basename is what
        /// <c>blnet_startup.g.cpp</c>'s <c>blnet_load_module</c> will look for.
        /// </summary>
        internal static string ProjectFileName(string shimAssemblyName) =>
            shimAssemblyName + ".csproj";

        /// <summary>
        /// The generated shim assembly's name for a project — THE one place it is decided, so the
        /// name baked into <c>blnet_startup.g.cpp</c>'s <c>blnet_load_module</c> call, the csproj
        /// <see cref="EmitProject"/> writes, and the DLL phase 5 deploys cannot each invent their
        /// own spelling. <c>dotnet publish -p:NativeLib=Shared</c> names its output after the
        /// project (<see cref="NetShimPublisher.ExpectedDllPath"/>), so naming the csproj from
        /// this function is what makes the loaded file name correct by construction.
        ///
        /// <para>Per-project rather than a fixed constant because the shim DLL is deployed next to
        /// the executable, where a fixed name would collide across two BasicLang programs sharing
        /// an output directory.</para>
        ///
        /// <para><b>Lives HERE, not in <c>CppProjectBuilder</c>, since P2a-2 Task 7b.</b> Phase 5
        /// makes <c>ProjectSystem</c> drive this class (generate → publish → map → deploy); the
        /// pre-7b arrangement — this class calling back into <c>CppProjectBuilder</c> for the name
        /// — would have closed that into a cycle between the two namespaces. The direction is now
        /// one-way: <c>ProjectSystem</c> → <c>CodeGen.Net</c>.</para>
        /// </summary>
        /// <param name="safeProjectName">
        /// <c>CppProjectBuilder.SanitizeProjectName</c>'s output — this becomes a file name, so an
        /// unsanitized project name would produce an unopenable module.
        /// </param>
        internal static string ShimAssemblyName(string safeProjectName) =>
            safeProjectName + ".Blnet";

        // ------------------------------------------------------------------------------
        // The artifact set.
        // ------------------------------------------------------------------------------

        /// <summary>
        /// The complete <c>obj/gen/shim</c> artifact set for <paramref name="surface"/>, keyed by
        /// file name, or an EMPTY dictionary when the surface is empty. Pure — no IO.
        /// </summary>
        /// <param name="safeProjectName">
        /// <c>CppProjectBuilder.SanitizeProjectName</c>'s output. The shim assembly name is
        /// derived from it by calling <see cref="ShimAssemblyName"/>, which is the same function
        /// the startup TU's module literal comes from.
        /// </param>
        /// <param name="referenceAssemblyPaths">
        /// The project's reference closure (§5) as absolute assembly paths. Empty for a surface
        /// over BCL types only.
        /// </param>
        /// <param name="valueTypeReceiverNames">
        /// Fully-qualified names of declaring types the caller determined are VALUE types, so
        /// their receivers are reached through <c>Unsafe.Unbox&lt;T&gt;</c> (§8.5). A
        /// <see cref="NetMemberDescriptor"/> carries no value-type flag — the surface collector
        /// holds the <c>ITypeSymbol</c> and is the only thing that can know — so this is passed
        /// in rather than guessed from a name. Omitting it means "every receiver is a reference
        /// type", which is the correct answer for a surface that has none.
        /// </param>
        internal static IReadOnlyDictionary<string, string> Emit(
            NetSurface surface,
            string safeProjectName,
            IReadOnlyList<string>? referenceAssemblyPaths = null,
            IReadOnlyCollection<string>? valueTypeReceiverNames = null)
        {
            if (surface == null) throw new ArgumentNullException(nameof(surface));
            if (string.IsNullOrEmpty(safeProjectName))
                throw new ArgumentException("A project name is required.", nameof(safeProjectName));

            if (!surface.IsNonEmpty)
                return new Dictionary<string, string>(0);

            var assemblyName = ShimAssemblyName(safeProjectName);
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ProjectFileName(assemblyName)] = EmitProject(assemblyName, referenceAssemblyPaths),
                [ExportsFileName] = EmitExports(surface, valueTypeReceiverNames),
                [HandleTableFileName] = BlnetShimSources.HandleTable,
                [StatusFileName] = BlnetShimSources.BlnetStatusCs,
                [ShimAbiFileName] = BlnetShimSources.ShimAbiCs,
                [MarshalFileName] = BlnetShimSources.MarshalCs,
            };
        }

        /// <summary>
        /// Writes <see cref="Emit"/>'s set into <paramref name="shimDir"/> and returns the full
        /// paths written, in file-name order.
        ///
        /// <para><b>On an empty surface this touches the file system not at all</b> — it does not
        /// create <paramref name="shimDir"/>. Directory creation lives INSIDE the non-empty
        /// branch for exactly that reason; hoisting it out as a precondition silently breaks
        /// P2a-1's inertness claim.</para>
        ///
        /// <para><b>Stale compilable files are PRUNED, not left</b> (P2a-2 Task-8 Step 0,
        /// 7b-I6). The shim csproj is an SDK project rooted here, so it globs <c>**/*.cs</c> —
        /// any orphan left behind by an earlier build is COMPILED INTO the shim. Latent while
        /// the emitted file set was fixed; Tasks 9/10/11 make it surface-dependent (per-element
        /// array helpers, per-delegate dispatchers), so a removed member's orphaned
        /// <c>.g.cs</c> would keep its wrapper alive against a member the surface no longer
        /// has. Only TOP-LEVEL <c>.cs</c>/<c>.csproj</c> are considered, which is what keeps
        /// <c>obj/</c> and <c>bin/</c> (the publish's own working state) untouched. A delete
        /// that fails propagates: the caller reports BL6006, which is right — a shim compiled
        /// with an orphan is a wrong shim, and building it anyway is the failure this prevents.</para>
        /// </summary>
        internal static IReadOnlyList<string> WriteTo(
            string shimDir,
            NetSurface surface,
            string safeProjectName,
            IReadOnlyList<string>? referenceAssemblyPaths = null,
            IReadOnlyCollection<string>? valueTypeReceiverNames = null)
        {
            if (string.IsNullOrEmpty(shimDir))
                throw new ArgumentException("A destination directory is required.", nameof(shimDir));

            var files = Emit(surface, safeProjectName, referenceAssemblyPaths, valueTypeReceiverNames);
            if (files.Count == 0)
                return Array.Empty<string>();

            Directory.CreateDirectory(shimDir);
            PruneStaleArtifacts(shimDir, files.Keys);
            var written = new List<string>(files.Count);
            foreach (var name in files.Keys.OrderBy(n => n, StringComparer.Ordinal))
            {
                var path = Path.Combine(shimDir, name);
                File.WriteAllText(path, files[name]);
                written.Add(path);
            }
            return written;
        }

        /// <summary>
        /// Deletes top-level <c>*.cs</c>/<c>*.csproj</c> in <paramref name="shimDir"/> that are
        /// not in <paramref name="keep"/>. See <see cref="WriteTo"/> for why an orphan here is
        /// not cosmetic — the SDK globs this directory.
        ///
        /// <para>The extension filter is the safety property: it means this can only ever
        /// remove files whose presence would CHANGE what the shim compiles to. Anything else a
        /// user or tool left here (a log, a marker, a <c>.props</c> the publish reads) is not
        /// ours to delete, and <c>obj/</c>/<c>bin/</c> are out of reach because the enumeration
        /// is deliberately non-recursive.</para>
        /// </summary>
        private static void PruneStaleArtifacts(string shimDir, IEnumerable<string> keep)
        {
            var expected = new HashSet<string>(keep, StringComparer.OrdinalIgnoreCase);
            foreach (var path in Directory.EnumerateFiles(shimDir))
            {
                var name = Path.GetFileName(path);
                if (expected.Contains(name)) continue;

                var extension = Path.GetExtension(name);
                if (!string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(extension, ".csproj", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                File.Delete(path);
            }
        }

        // ------------------------------------------------------------------------------
        // §8.1 — the project file.
        // ------------------------------------------------------------------------------

        /// <summary>
        /// The shim <c>.csproj</c> (spec §8.1). <c>NativeLib=Shared</c> and <c>-r win-x64</c> are
        /// deliberately NOT here — they are publish-command arguments, which is where
        /// <see cref="NetShimPublisher.BuildPublishArguments"/> puts them, matching P0's proven
        /// recipe. Putting them in the project instead would fork the recipe in two places.
        /// </summary>
        internal static string EmitProject(
            string shimAssemblyName, IReadOnlyList<string>? referenceAssemblyPaths = null)
        {
            if (string.IsNullOrEmpty(shimAssemblyName))
                throw new ArgumentException("A shim assembly name is required.", nameof(shimAssemblyName));

            var sb = new StringBuilder();
            L(sb, "<!-- " + ProjectFileName(shimAssemblyName) + " — GENERATED by NetShimGenerator.");
            L(sb, "     Do not edit; edits are lost on the next build (obj/gen is regenerated).");
            L(sb, "     Source of truth: BasicLang NetShimGenerator.cs. -->");
            L(sb, "<Project Sdk=\"Microsoft.NET.Sdk\">");
            L(sb, "  <PropertyGroup>");
            L(sb, "    <!-- net8.0 is the NU1201 floor and the AOT-analysis floor (spec §8.1). -->");
            L(sb, "    <TargetFramework>" + TargetFramework + "</TargetFramework>");
            L(sb, "    <AssemblyName>" + Xml(shimAssemblyName) + "</AssemblyName>");
            L(sb, "    <RootNamespace>" + ShimNamespace + "</RootNamespace>");
            L(sb, "    <Nullable>enable</Nullable>");
            L(sb, "    <!-- REQUIRED: " + HandleTableFileName + " names List<T>/Stack<T>/Enumerable.Count with");
            L(sb, "         no using of its own (BlnetShimSources' namespace decision). -->");
            L(sb, "    <ImplicitUsings>enable</ImplicitUsings>");
            L(sb, "    <!-- REQUIRED: every export's signature is a pointer signature. -->");
            L(sb, "    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>");
            L(sb, "    <PublishAot>true</PublishAot>");
            L(sb, "    <InvariantGlobalization>true</InvariantGlobalization>");
            L(sb, "    <!-- Turns the four trim/AOT analyzers on. -->");
            L(sb, "    <IsAotCompatible>true</IsAotCompatible>");
            L(sb, "    <!-- LOAD-BEARING for BL6020 (spec §8.1, D5): the SDK default (true) collapses");
            L(sb, "         every trim/AOT warning from a packaged assembly into ONE assembly-level");
            L(sb, "         IL2104/IL3053 carrying no member name, leaving AotDiagnosticMapper");
            L(sb, "         nothing to attribute back to a .bas line. -->");
            L(sb, "    <TrimmerSingleWarn>false</TrimmerSingleWarn>");
            L(sb, "  </PropertyGroup>");

            if (referenceAssemblyPaths is { Count: > 0 })
            {
                L(sb, "  <ItemGroup>");
                L(sb, "    <!-- The project's reference closure (§5). HintPath references rather than");
                L(sb, "         PackageReferences: the closure is already resolved to absolute paths by");
                L(sb, "         the time it reaches here, and re-resolving it would let NuGet pick a");
                L(sb, "         different assembly than the one the analyzer bound against. -->");
                foreach (var path in referenceAssemblyPaths)
                {
                    L(sb, "    <Reference Include=\"" + Xml(Path.GetFileNameWithoutExtension(path)) + "\">");
                    L(sb, "      <HintPath>" + Xml(path) + "</HintPath>");
                    L(sb, "    </Reference>");
                }
                L(sb, "  </ItemGroup>");
            }

            L(sb, "</Project>");
            return sb.ToString();
        }

        // ------------------------------------------------------------------------------
        // §8.2 — Exports.g.cs.
        // ------------------------------------------------------------------------------

        /// <summary>
        /// The surface-derived export names, de-duplicated on the mangled name in first-seen
        /// order — the SAME key and the SAME order <see cref="NetProxyEmitter"/> collapses slots
        /// on, which is what makes §12.4's set comparison meaningful on a surface nobody thought
        /// to de-duplicate (§7.1's collector walks call sites, so one member arrives once per
        /// call site).
        /// </summary>
        internal static IReadOnlyList<string> SurfaceDerivedExportNames(NetSurface surface)
        {
            if (surface == null) throw new ArgumentNullException(nameof(surface));
            return Plan(surface).Select(p => p.ExportName).ToList();
        }

        /// <summary>
        /// Spec §11.3's provenance map: mangled wrapper name → where that wrapper came from, for
        /// the wrappers this generator ACTUALLY emits. <c>AotDiagnosticMapper</c> reads it to lift
        /// an ILC diagnostic reported against <c>obj/gen/shim/Exports.g.cs</c> back onto the
        /// <c>.bas</c> line that caused it (tier 1).
        ///
        /// <para><b>Why the filter is the point, and why this lives here rather than at the two
        /// producers.</b> <paramref name="origins"/> arrives from upstream — the analyzer's
        /// annotation table for §7.1 call sites, the surface collector for §7.2
        /// <c>&lt;NetProxy&gt;</c> expansions — and both producers can legitimately offer an entry
        /// for a member that never becomes an export: a name-only (non-exact) annotation, a member
        /// the §7.2 omission filter dropped, a call the optimizer deleted before phase 3 walked the
        /// IR. Letting such an entry into the map would let a BL6020 name a <c>.bas</c> line for a
        /// wrapper that is not in the shim at all. This class owns <see cref="Plan"/> — the ONE
        /// answer to "which exports exist" — so intersecting here is the only place the map cannot
        /// out-claim the emission.</para>
        ///
        /// <para>Entries are applied in order and LAST WINS, matching
        /// <see cref="NetProvenanceMap"/>'s own rule: one member reached from two call sites
        /// mangles to one export, and a collision is expected rather than an error. Callers
        /// therefore order the sources by which location they would rather show.</para>
        /// </summary>
        internal static NetProvenanceMap Provenance(
            NetSurface surface, IEnumerable<KeyValuePair<string, NetWrapperOrigin>> origins)
        {
            if (surface == null) throw new ArgumentNullException(nameof(surface));
            if (origins == null) return NetProvenanceMap.Empty;

            var exported = new HashSet<string>(SurfaceDerivedExportNames(surface), StringComparer.Ordinal);
            return new NetProvenanceMap(origins.Where(e => exported.Contains(e.Key ?? string.Empty)));
        }

        /// <summary>
        /// Complete <c>Exports.g.cs</c> text: P0's seven core exports plus one wrapper per
        /// distinct surface member. Called on a non-empty surface by <see cref="Emit"/>; safe to
        /// call on an empty one, which yields the core-only shim.
        /// </summary>
        internal static string EmitExports(
            NetSurface surface, IReadOnlyCollection<string>? valueTypeReceiverNames = null)
        {
            if (surface == null) throw new ArgumentNullException(nameof(surface));

            var valueTypes = valueTypeReceiverNames is null
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(valueTypeReceiverNames, StringComparer.Ordinal);

            var sb = new StringBuilder();
            L(sb, "// " + ExportsFileName + " — GENERATED by NetShimGenerator. Do not edit; edits are");
            L(sb, "// lost on the next build. Source of truth: BasicLang NetShimGenerator.cs.");
            L(sb, "//");
            L(sb, "// Every method below is [UnmanagedCallersOnly] and therefore MUST be static, take");
            L(sb, "// blittable arguments only, use no generic type parameters, and never let an");
            L(sb, "// exception escape — an escaping exception under Native AOT is a FailFast, not a");
            L(sb, "// managed error. That is what the try/Fail wrapper on every body is for (spec C4).");
            L(sb, "");
            L(sb, "// REQUIRED, not redundant with the project's <Nullable>enable</Nullable>: Roslyn");
            L(sb, "// classifies a *.g.cs file as auto-generated, and an auto-generated file needs an");
            L(sb, "// EXPLICIT directive before a nullable annotation is honoured rather than reported");
            L(sb, "// as CS8669. Without this line every `object?`/`string?` below is a warning.");
            L(sb, "#nullable enable");
            L(sb, "");
            L(sb, "using System.Runtime.InteropServices;");
            L(sb, "using System.Text;");
            L(sb, "");
            L(sb, "// Task 11's binding decision: the same namespace as HandleTable.g.cs, so this file");
            L(sb, "// can see it. BlnetStatus deliberately has NO namespace and is reached by fall-out");
            L(sb, "// lookup into the global namespace.");
            L(sb, "namespace " + ShimNamespace + ";");
            L(sb, "");
            L(sb, "public static unsafe class Exports");
            L(sb, "{");
            sb.Append(Prologue);
            L(sb, "");
            L(sb, "    // ---- P0's seven core exports (blnet.h's BLNET_EXPORT_* names) ----");
            EmitCoreExports(sb);

            var plans = Plan(surface);
            L(sb, "");
            L(sb, "    // ---- Surface-derived wrappers (spec §8.2) ----");
            if (plans.Count == 0)
                L(sb, "    // The surface declared types but contributed no callable members.");
            foreach (var plan in plans)
            {
                L(sb, "");
                L(sb, "    // " + Comment(plan.Member.ToString()));
                EmitWrapper(sb, plan, valueTypes);
            }
            L(sb, "}");
            return sb.ToString();
        }

        /// <summary>
        /// P0's seven, driven off <see cref="BlnetContract.CoreExportNames"/> rather than a list
        /// spelled here. The <c>throw</c> is the point: adding an export to the contract without
        /// implementing it would otherwise ship a shim that fails <c>blnet_bind_core</c> at
        /// startup — a runtime message, not a build error.
        /// </summary>
        private static void EmitCoreExports(StringBuilder sb)
        {
            foreach (var name in BlnetContract.CoreExportNames)
            {
                if (!CoreExportBodies.TryGetValue(name, out var body))
                    throw new InvalidOperationException(
                        $"BlnetContract declares core export '{name}' but NetShimGenerator has no "
                        + "C# implementation for it, so every generated shim would fail "
                        + "blnet_bind_core at startup with \"shim is missing export\". Add the body "
                        + "to NetShimGenerator.CoreExportBodies — do not drop the name from the "
                        + "contract unless the ABI really lost it (which bumps AbiVersion, rule C7).");

                L(sb, "");
                L(sb, "    " + UnmanagedCallersOnly(name));
                sb.Append(body);
            }
        }

        // ------------------------------------------------------------------------------
        // Wrapper emission.
        // ------------------------------------------------------------------------------

        private static void EmitWrapper(StringBuilder sb, SlotPlan plan, HashSet<string> valueTypes)
        {
            L(sb, "    " + UnmanagedCallersOnly(plan.ExportName));
            L(sb, "    public static int " + plan.ExportName + "(" + CsSignature(plan) + ")");
            L(sb, "    {");
            L(sb, "        try");
            L(sb, "        {");

            // §8.2's guarded decode. Handle 0 means Nothing and must never reach the table:
            // HandleTable.Validate rejects index 0, so an unconditional TryGet would report
            // BLNET_E_STALE_HANDLE for every null.
            if (plan.HasReceiver)
                EmitHandleDecode(sb, "self", "self_", "st_self");
            foreach (var p in plan.Parameters.Where(p => p.Wire.Kind == WireKind.Handle))
                EmitHandleDecode(sb, p.Name, p.Local, "st_" + p.Name);

            // ByRef slots arrive as pointers; C# needs a real variable to pass ref/out/in.
            foreach (var p in plan.Parameters.Where(p => p.ByRef))
                L(sb, "            " + p.Wire.CsManagedType + " " + p.Local + " = "
                      + (p.Descriptor.RefKind == NetRefKind.Out
                          ? "default"
                          : FromWire(p.Wire, "*" + p.Name)) + ";");

            var call = Invocation(plan, valueTypes);
            L(sb, "            " + (plan.Return.Kind == WireKind.Void ? call : "var rv_ = " + call) + ";");

            foreach (var p in plan.Parameters.Where(p => p.ByRef))
                L(sb, "            *" + p.Name + " = " + ToWire(p.Wire, p.Local) + ";");

            switch (plan.Return.Kind)
            {
                case WireKind.Void:
                    break;
                case WireKind.Scalar:
                    L(sb, "            *result = " + ToWire(plan.Return, "rv_") + ";");
                    break;
                case WireKind.String:
                    L(sb, "            // P0 string ownership: the buffer is blnet_alloc'd and the");
                    L(sb, "            // native receiver frees it with blnet_free.");
                    L(sb, "            *result = rv_ is null ? null : AllocUtf8(rv_);");
                    break;
                case WireKind.Handle:
                    L(sb, "            *result = ToHandle(rv_);");
                    break;
            }

            L(sb, "            return (int)BlnetStatus.BLNET_OK;");
            L(sb, "        }");
            L(sb, "        catch (Exception ex) { return Fail(ex); }");
            L(sb, "    }");
        }

        private static void EmitHandleDecode(StringBuilder sb, string wire, string local, string status)
        {
            L(sb, "            object? " + local + " = null;");
            L(sb, "            if (" + wire + " != 0)");
            L(sb, "            {");
            L(sb, "                var " + status + " = Table.TryGet(" + wire + ", out " + local + ");");
            L(sb, "                if (" + status + " != BlnetStatus.BLNET_OK) return (int)" + status + ";");
            L(sb, "            }");
        }

        /// <summary>
        /// The C# parameter list, matching <c>NetProxyEmitter.CSignature</c> position for
        /// position: receiver, parameters (pointer-shaped when ByRef), then the result pointer.
        /// </summary>
        private static string CsSignature(SlotPlan plan)
        {
            var args = new List<string>();
            if (plan.HasReceiver) args.Add("ulong self");
            foreach (var p in plan.Parameters)
                args.Add((p.ByRef ? p.Wire.CsOutType : p.Wire.CsParamType) + " " + p.Name);
            if (plan.Return.Kind != WireKind.Void) args.Add(plan.Return.CsOutType + " result");
            return string.Join(", ", args);
        }

        /// <summary>
        /// The managed call itself. The generator computes NO conversions (§8.2) — it emits C#
        /// and lets <c>csc</c> resolve implicit conversions, optional parameters and
        /// <c>params</c>, because Roslyn already resolved the overload up front (§6.1).
        /// </summary>
        private static string Invocation(SlotPlan plan, HashSet<string> valueTypes)
        {
            var member = plan.Member;
            var declaring = Qualified(member.DeclaringTypeFullName);
            var args = string.Join(", ", plan.Parameters.Select(Argument));

            if (member.Kind == NetMemberCategory.Constructor)
                return "new " + declaring + "(" + args + ")";

            var target = plan.HasReceiver ? Receiver(member, valueTypes) : declaring;

            // A Property WITH parameters is an indexer; §8.5 expects the collector to hand
            // indexers over as get_Item/set_Item methods, but shaping it here costs nothing and
            // is the only spelling that could compile if one arrives as a Property.
            if (member.Kind == NetMemberCategory.Property && plan.Parameters.Count > 0)
                return target + "[" + args + "]";

            if (member.Kind == NetMemberCategory.Property || member.Kind == NetMemberCategory.Field)
                return target + "." + member.Name;

            // Task 7a: a synthesized set_X accessor (NetAccessorSynthesis — how IRBuilder
            // stamps a property/field WRITE) spells as an assignment: C# cannot call an
            // accessor by its metadata name (CS0571). See IsSyntheticSetterShape's caveat
            // about foreign metadata declaring an ordinary method named set_X — that shape
            // fails to compile in csc, loudly, never silently calling the wrong member.
            if (NetAccessorSynthesis.IsSyntheticSetterShape(member))
                return target + "." + member.Name.Substring(NetAccessorSynthesis.SetterPrefix.Length)
                       + " = " + args;

            return target + "." + member.Name + "(" + args + ")";
        }

        /// <summary>
        /// §8.5: a boxed VALUE-type receiver is reached through <c>Unsafe.Unbox&lt;T&gt;</c>,
        /// which yields a <c>ref T</c> into the box. An unboxing cast would instead produce a
        /// temporary — <c>((T)o!).Mutate()</c> mutates the temporary and leaves the box untouched
        /// (the infinite-<c>MoveNext</c> failure §8.5 describes), and <c>((T)o!).Prop = v</c> is a
        /// hard CS0445 that would make the whole shim fail to compile.
        /// </summary>
        private static string Receiver(NetMemberDescriptor member, HashSet<string> valueTypes) =>
            valueTypes.Contains(member.DeclaringTypeFullName)
                ? "global::System.Runtime.CompilerServices.Unsafe.Unbox<"
                  + Qualified(member.DeclaringTypeFullName) + ">(self_!)"
                : "((" + Qualified(member.DeclaringTypeFullName) + ")self_!)";

        private static string Argument(ParameterPlan p)
        {
            if (p.ByRef)
                return p.Descriptor.RefKind switch
                {
                    NetRefKind.Out => "out " + p.Local,
                    NetRefKind.Ref => "ref " + p.Local,
                    _ => "in " + p.Local,      // In / RefReadOnly
                };

            return p.Wire.Kind switch
            {
                // Utf8ToString returns null for a null pointer, which is the right value for
                // Nothing; a callee that rejects null throws, and C4 turns that into
                // BLNET_E_MANAGED_EXCEPTION — the same thing the C# leg does.
                WireKind.String => "Utf8ToString(" + p.Name + ")!",

                // ONE cast spelling for both reference and value types: for a reference type it
                // is an ordinary downcast, for a value type an unboxing conversion. Neither needs
                // to know which, so parameters — unlike receivers (§8.5) — require no
                // value-type knowledge at all.
                WireKind.Handle => "(" + Qualified(p.Descriptor.TypeFullName) + ")" + p.Local + "!",

                _ => FromWire(p.Wire, p.Name),
            };
        }

        // ------------------------------------------------------------------------------
        // §8.3 marshaling, C# column. The C column is NetProxyEmitter.WireOf; the two are
        // separate code and NetShimGeneratorTests compares the signatures they produce.
        // ------------------------------------------------------------------------------

        private enum WireKind { Void, Scalar, String, Handle }

        /// <summary>
        /// One row of §8.3 in C#. <see cref="CsParamType"/> is the inbound spelling and
        /// <see cref="CsOutType"/> the pointer a result or ByRef slot uses;
        /// <see cref="CsManagedType"/> is what the managed call actually sees, which differs for
        /// the two rows §8.3 re-widths: <c>Boolean</c> travels as <c>int</c> 0/1 (a C# bool is
        /// not blittable for <c>[UnmanagedCallersOnly]</c>) and <c>Char</c> as a
        /// <c>ushort</c> UTF-16 code unit.
        /// </summary>
        private readonly record struct WireForm(
            WireKind Kind, string CsParamType, string CsOutType, string CsManagedType);

        private static class Wire
        {
            internal static readonly WireForm Void = new(WireKind.Void, "void", "", "void");

            internal static readonly WireForm String =
                new(WireKind.String, "byte*", "byte**", "string");

            internal static readonly WireForm Handle =
                new(WireKind.Handle, "ulong", "ulong*", "object");

            internal static readonly WireForm Boolean =
                new(WireKind.Scalar, "int", "int*", "bool");

            internal static readonly WireForm Char =
                new(WireKind.Scalar, "ushort", "ushort*", "char");

            /// <summary>
            /// §6.4 single-slot pairs (Task 7a): the wire is the P1 layout scalar; the
            /// managed value converts through <c>BlnetMarshal</c> (BlnetShimSources.MarshalCs
            /// — the Task-6 managed half) in <see cref="FromWire"/>/<see cref="ToWire"/>.
            /// </summary>
            internal static readonly WireForm DateTime =
                new(WireKind.Scalar, "ulong", "ulong*", "System.DateTime");

            internal static readonly WireForm TimeSpan =
                new(WireKind.Scalar, "long", "long*", "System.TimeSpan");

            internal static WireForm Scalar(string cs) =>
                new(WireKind.Scalar, cs, cs + "*", cs);
        }

        /// <summary>
        /// §8.3 read as a table, in the SAME row order as <c>NetProxyEmitter.WireOf</c>.
        /// Everything not listed is a handle — §8.3's "other non-ref value types → handle
        /// (boxed)" row plus every reference type — which is the safe default: being wrong about
        /// an opaque handle costs a missing convenience, never a misinterpreted 64 bits.
        ///
        /// <para><b>The other two encodings, and what ties each to this one.</b>
        /// <c>NetProxyEmitter.WireOf</c> is the C column, held equal to this one
        /// signature-for-signature by
        /// <see cref="NetShimGeneratorTests.ExportSignaturesMatchTheProxyTableSlotSignatures"/>
        /// over <c>NetProxyEmitterTests.WireShapeSurface</c> — a row absent from that fixture is
        /// a row the comparison cannot see. <c>NetMarshalTable.WireRows</c> is the call site's,
        /// tied to <c>blnet_marshal.hpp</c> by <c>NetConversionPairTests</c>.</para>
        ///
        /// <para><b>What may ARRIVE here is decided by
        /// <c>NetSurfaceCollector.FirstUnmarshalable</c></b>, the §7.2 admissibility filter: a
        /// pointer, an open type parameter, <c>Object</c> or a <c>ref struct</c> never reaches a
        /// wrapper. Adding an arm for one here would silently re-admit a type the collector
        /// refused.</para>
        /// </summary>
        private static WireForm WireOf(string netTypeFullName) => netTypeFullName switch
        {
            "System.Void" => Wire.Void,
            "System.Boolean" => Wire.Boolean,
            "System.SByte" => Wire.Scalar("sbyte"),
            "System.Byte" => Wire.Scalar("byte"),
            "System.Int16" => Wire.Scalar("short"),
            "System.UInt16" => Wire.Scalar("ushort"),
            "System.Int32" => Wire.Scalar("int"),
            "System.UInt32" => Wire.Scalar("uint"),
            "System.Int64" => Wire.Scalar("long"),
            "System.UInt64" => Wire.Scalar("ulong"),
            "System.Single" => Wire.Scalar("float"),
            "System.Double" => Wire.Scalar("double"),
            "System.Char" => Wire.Char,
            "System.String" => Wire.String,
            // §6.4 single-slot pairs — SAME rows, SAME order as NetProxyEmitter.WireOf (the
            // two are compared by NetShimGeneratorTests). The multi-slot pairs (Decimal,
            // Guid, DateTimeOffset, StringBuilder) stay in the handle row on BOTH sides
            // until Task 8's drift completion; the analyzer refuses them at resolved call
            // sites, so no BasicLang call reaches the handle-shaped slot.
            "System.DateTime" => Wire.DateTime,
            "System.TimeSpan" => Wire.TimeSpan,
            _ => Wire.Handle,
        };

        /// <summary>Wire value to managed value: <c>Boolean</c>, <c>Char</c>, and the §6.4
        /// single-slot pairs convert (the latter through the Task-6 <c>BlnetMarshal</c>).</summary>
        private static string FromWire(WireForm wire, string expression) =>
            wire.CsManagedType switch
            {
                "bool" => "(" + expression + " != 0)",
                "char" => "(char)(" + expression + ")",
                "System.DateTime" => "BlnetMarshal.DateTimeFromWire(" + expression + ")",
                "System.TimeSpan" => "BlnetMarshal.TimeSpanFromWire(" + expression + ")",
                _ => expression,
            };

        /// <summary>Managed value back to wire value.</summary>
        private static string ToWire(WireForm wire, string expression) =>
            wire.CsManagedType switch
            {
                "bool" => expression + " ? 1 : 0",
                "char" => "(ushort)" + expression,
                "System.DateTime" => "BlnetMarshal.DateTimeToWire(" + expression + ")",
                "System.TimeSpan" => "BlnetMarshal.TimeSpanToWire(" + expression + ")",
                _ => expression,
            };

        // ------------------------------------------------------------------------------
        // Planning. Mirrors NetProxyEmitter.Plan exactly — same key, same order.
        // ------------------------------------------------------------------------------

        private static IReadOnlyList<SlotPlan> Plan(NetSurface surface)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var plans = new List<SlotPlan>();
            foreach (var member in surface.Members)
            {
                var name = NetNameMangler.Mangle(member);
                if (!seen.Add(name)) continue;
                plans.Add(PlanMember(name, member));
            }
            return plans;
        }

        private static SlotPlan PlanMember(string exportName, NetMemberDescriptor member)
        {
            // A constructor's metadata return type is System.Void, but its export hands back the
            // object it just created (§8.2's worked shape ends in Table.Create). Same correction
            // NetProxyEmitter.PlanMember makes — the two must agree or the signatures diverge.
            var returnWire = member.Kind == NetMemberCategory.Constructor
                ? Wire.Handle
                : WireOf(member.TypeFullName);

            var parameters = new List<ParameterPlan>();
            var index = 0;
            foreach (var p in member.Parameters ?? (IReadOnlyList<NetParameterDescriptor>)Array.Empty<NetParameterDescriptor>())
                parameters.Add(new ParameterPlan(p, WireOf(p.TypeFullName), index++));

            return new SlotPlan(
                exportName,
                member,
                hasReceiver: !member.IsStatic && member.Kind != NetMemberCategory.Constructor,
                returnWire,
                parameters);
        }

        // ------------------------------------------------------------------------------
        // Fixed text.
        // ------------------------------------------------------------------------------

        private static string UnmanagedCallersOnly(string entryPoint) =>
            "[UnmanagedCallersOnly(EntryPoint = \"" + entryPoint
            + "\", CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]";

        /// <summary>
        /// The class prologue: the handle table, P0's native vtable slots, the thread-local last
        /// error, and the four helpers every body uses. Byte-for-byte the hand shim's, which is
        /// the copy the frozen P0 conformance suite exercises.
        /// </summary>
        private const string Prologue = @"    internal static readonly HandleTable Table = new();

    // P0's 2-slot POSITIONAL native vtable, captured by blnet_initialize. Written here and read
    // by §8.4's delegate dispatcher, which P2a-1 does not generate yet — see NetShimGenerator's
    // ""known gaps"". They are stored regardless so the handshake stays complete.
    private static delegate* unmanaged[Cdecl]<ulong, ulong*, int, ulong*, int> _thunk;
    private static delegate* unmanaged[Cdecl]<byte**, int> _getNativeError;

    [ThreadStatic] private static string? _lastErrorType;
    [ThreadStatic] private static string? _lastErrorMessage;

    /// <summary>C4: records a managed exception for blnet_last_error and degrades to a status.</summary>
    private static int Fail(Exception ex)
    {
        try { _lastErrorType = InheritanceChain(ex.GetType()); _lastErrorMessage = ex.Message; }
        catch { /* C4: the handler itself must be non-throwing; degrade to status-only */ }
        return (int)BlnetStatus.BLNET_E_MANAGED_EXCEPTION;
    }

    /// <summary>
    /// Spec §11.1's wire format for the last-error TYPE field: the thrown type's inheritance
    /// chain, MOST-DERIVED FIRST, fully qualified, ';'-separated — e.g.
    /// ""System.ArgumentNullException;System.ArgumentException;System.SystemException;System.Exception"".
    ///
    /// NOT ex.GetType().FullName. The native side's NetCheckTyped builds a BasicLang::NetException
    /// from this exact string and matches typed Catch clauses by ';'-delimited ELEMENT equality,
    /// so a single name makes `Catch ex As Exception` stop catching an ArgumentNullException —
    /// silently, with the exception escaping to the next handler out.
    ///
    /// System.Object is deliberately NOT in the chain: it terminates at System.Exception, which
    /// is where §11.1's worked example ends and the last type a BasicLang Catch can name.
    /// </summary>
    private static string InheritanceChain(Type type)
    {
        var chain = new StringBuilder();
        for (var t = type; t is not null && t != typeof(object); t = t.BaseType)
        {
            if (chain.Length != 0) chain.Append(';');
            chain.Append(t.FullName ?? t.Name);
        }
        return chain.ToString();
    }

    /// <summary>
    /// §8.2's handle encode. A null result is handle 0 — Table.Create(null) would mint a live
    /// non-zero handle for Nothing, which the native NetRef would then dutifully release.
    /// Taking object? rather than inlining the conditional at each call site is what keeps a
    /// value-type (in particular ENUM) result compiling: `rv is null` on a non-nullable value
    /// type is CS0037, and §8.3's default row sends every enum down this path.
    /// </summary>
    private static ulong ToHandle(object? o) => o is null ? 0UL : Table.Create(o);

    /// <summary>C3: allocates a UTF-8 buffer the native receiver frees via blnet_free.</summary>
    private static byte* AllocUtf8(string s)
    {
        var bytes = Encoding.UTF8.GetByteCount(s);
        var buf = (byte*)NativeMemory.Alloc((nuint)(bytes + 1));
        fixed (char* c = s) Encoding.UTF8.GetBytes(c, s.Length, buf, bytes);
        buf[bytes] = 0;
        return buf;
    }

    private static string? Utf8ToString(byte* p) => p == null ? null : Marshal.PtrToStringUTF8((nint)p);
";

        /// <summary>
        /// One C# body per contract core export, keyed by the EXPORT name so
        /// <see cref="EmitCoreExports"/> can drive the loop off
        /// <see cref="BlnetContract.CoreExportNames"/> and fail loudly on a name it cannot
        /// implement. Bodies are pre-indented to the class's four spaces.
        /// </summary>
        private static readonly IReadOnlyDictionary<string, string> CoreExportBodies =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["blnet_abi_version"] =
@"    // Single source: BlnetShimSources splices ShimAbi from BlnetContract.AbiVersion.
    public static int AbiVersion() => ShimAbi.AbiVersion;
",
                ["blnet_initialize"] =
@"    public static int Initialize(int expectedAbi, void* vtable)
    {
        try
        {
            if (expectedAbi != ShimAbi.AbiVersion) return (int)BlnetStatus.BLNET_E_VERSION_MISMATCH;
            // C4: an exception escaping [UnmanagedCallersOnly] under Native AOT is a FailFast —
            // guard the dereference and keep the whole body non-throwing like every other export.
            if (vtable == null) return (int)BlnetStatus.BLNET_E_MANAGED_EXCEPTION;
            var vt = (void**)vtable;
            _thunk = (delegate* unmanaged[Cdecl]<ulong, ulong*, int, ulong*, int>)vt[0];
            _getNativeError = (delegate* unmanaged[Cdecl]<byte**, int>)vt[1];
            return (int)BlnetStatus.BLNET_OK;
        }
        catch (Exception ex) { return Fail(ex); }
    }
",
                ["blnet_addref"] =
@"    public static int AddRef(ulong h) { try { return (int)Table.AddRef(h); } catch (Exception ex) { return Fail(ex); } }
",
                ["blnet_release"] =
@"    public static int Release(ulong h) { try { return (int)Table.Release(h); } catch (Exception ex) { return Fail(ex); } }
",
                ["blnet_alloc"] =
@"    public static void* Alloc(long size) { try { return NativeMemory.Alloc((nuint)size); } catch { return null; } }
",
                ["blnet_free"] =
@"    public static void Free(void* p) { if (p != null) NativeMemory.Free(p); }
",
                ["blnet_last_error"] =
@"    public static int LastError(byte** typeName, byte** message)
    {
        try
        {
            if (typeName != null) *typeName = _lastErrorType is null ? null : AllocUtf8(_lastErrorType);
            if (message != null) *message = _lastErrorMessage is null ? null : AllocUtf8(_lastErrorMessage);
            return (int)BlnetStatus.BLNET_OK;
        }
        catch (Exception ex) { return Fail(ex); }
    }
",
            };

        // ------------------------------------------------------------------------------
        // Text helpers. Newlines are '\n' EXPLICITLY, never Environment.NewLine: Task 15's
        // content-hash cache keys on this text, and a hash that changed with the host OS would
        // produce a false miss on one machine and a false hit on another.
        // ------------------------------------------------------------------------------

        private static void L(StringBuilder sb, string line) => sb.Append(line).Append('\n');

        /// <summary>
        /// A .NET type spelled as C# type syntax. <c>global::</c> is not decoration — a project
        /// type named <c>System</c>, or the shim's own <c>BlnetTestShim</c> namespace, would
        /// otherwise shadow the framework name being cast to.
        /// </summary>
        private static string Qualified(string netTypeFullName) => "global::" + netTypeFullName;

        /// <summary>Keeps a member signature from breaking out of its <c>//</c> comment.</summary>
        private static string Comment(string text) =>
            text.Replace("\r", " ").Replace("\n", " ");

        private static string Xml(string text) => text
            .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
            .Replace("\"", "&quot;");

        // ------------------------------------------------------------------------------

        private sealed class ParameterPlan
        {
            internal ParameterPlan(NetParameterDescriptor descriptor, WireForm wire, int index)
            {
                Descriptor = descriptor;
                Wire = wire;
                Name = "a" + index.ToString(CultureInfo.InvariantCulture);
                Local = "a" + index.ToString(CultureInfo.InvariantCulture) + "_";
            }

            internal NetParameterDescriptor Descriptor { get; }
            internal WireForm Wire { get; }

            /// <summary>The wire parameter's name — the same spelling NetProxyEmitter uses.</summary>
            internal string Name { get; }

            /// <summary>The managed local: the decoded handle, or the ByRef temporary.</summary>
            internal string Local { get; }

            internal bool ByRef => Descriptor.RefKind != NetRefKind.None;
        }

        private sealed class SlotPlan
        {
            internal SlotPlan(
                string exportName, NetMemberDescriptor member, bool hasReceiver,
                WireForm returnWire, IReadOnlyList<ParameterPlan> parameters)
            {
                ExportName = exportName;
                Member = member;
                HasReceiver = hasReceiver;
                Return = returnWire;
                Parameters = parameters;
            }

            internal string ExportName { get; }
            internal NetMemberDescriptor Member { get; }
            internal bool HasReceiver { get; }
            internal WireForm Return { get; }
            internal IReadOnlyList<ParameterPlan> Parameters { get; }
        }
    }
}
