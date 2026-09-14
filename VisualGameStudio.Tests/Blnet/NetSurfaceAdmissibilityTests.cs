using System;
using System.Collections.Generic;
using System.Linq;
using BasicLang.Net;
using BasicLang.Compiler.ProjectSystem;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Blnet;

/// <summary>
/// Spec §7.2's omission rules and §8.3's row table, tied together — the
/// admissibility⇄wire-form chip carried forward from P2a-2 Task 8 Step 2b, which shipped a
/// NARROWER oracle (the six §6.4 rows against <c>blnet_marshal.hpp</c>) and left the actual ask
/// open. Before this fixture, <c>NetSurfaceCollector.FirstUnmarshalable</c> — the gate every
/// declared member passes through — was referenced by NO test at all.
///
/// <para><b>Why the obvious form of this test is worthless, in the chip's own words.</b> The ask
/// reads "every type <c>FirstUnmarshalable</c> ADMITS gets a real wire form from BOTH emitters".
/// Asserted directly that is vacuous: both <c>WireOf</c>s DEFAULT to Handle, so "has a wire
/// form" is true of everything. The chip therefore specifies the CONTRAPOSITIVE PAIR, and that
/// is what lives here:</para>
/// <list type="number">
///   <item><description><see cref="AnUnmarshalableSignatureIsDroppedBeforeEitherEmitter"/> —
///     every type the collector REJECTS must be unreachable as a slot, asserted by driving a
///     surface that contains one and watching it be dropped.</description></item>
///   <item><description><see cref="EverySection83RowSurvivesDeclaredCollection"/> — every §8.3
///     row must be ADMITTED, so a row silently demoted to Handle fails here instead of
///     passing. That demotion is exactly §6.4's "a native value must never become a handle".
///     </description></item>
/// </list>
///
/// <para><b>Both halves need controls.</b> "Absent from the surface" is a claim about a member
/// that could otherwise have been there — if the probe type failed to resolve at all, every row
/// would be absent and every assertion green. Each test therefore asserts an admitted member
/// beside the omitted ones.</para>
/// </summary>
[TestFixture]
public class NetSurfaceAdmissibilityTests
{
    // ------------------------------------------------------------------------------------
    // Part 1 — the rejection contrapositive.
    // ------------------------------------------------------------------------------------

    /// <summary>
    /// One member per reason <c>FirstUnmarshalable</c> returns non-null, plus two controls.
    ///
    /// <para>Two of its branches are deliberately absent and neither is an oversight.
    /// <c>TypeKind.Error</c> cannot be produced by a probe that COMPILES — it is the
    /// resolver's answer for a type whose assembly is missing, reached through a different
    /// path. <c>System.TypedReference</c> cannot appear as an ordinary parameter in C# at all.
    /// Both are listed here so the next reader knows they were considered rather than
    /// missed.</para>
    /// </summary>
    private const string RejectProbeSource = """
        namespace Adm.Probe
        {
            public ref struct RefLikeMarker { public int V; }

            public static class Reject
            {
                // CONTROLS — admitted, and the proof the type resolved at all.
                public static int ScalarControl(int v) => v;
                public static string StringControl(string s) => s;

                // FirstUnmarshalable: IPointerTypeSymbol
                public static unsafe int TakesPointer(int* p) => 0;
                // FirstUnmarshalable: SpecialType.System_Object, argument position
                public static int TakesObject(object o) => 0;
                // …and return position, which SignatureTypes walks too
                public static object ReturnsObject() => null;
                // FirstUnmarshalable: IsRefLikeType
                public static int TakesRefLike(RefLikeMarker m) => m.V;
                // FirstUnmarshalable: IArrayTypeSymbol -> recurses on the ELEMENT
                public static int TakesObjectArray(object[] a) => a.Length;
                // the generic-method arity gate that sits just above FirstUnmarshalable
                public static T GenericMethod<T>(T v) => v;
            }
        }
        """;

    private static readonly string[] MustBeOmitted =
    {
        "TakesPointer", "TakesObject", "ReturnsObject", "TakesRefLike",
        "TakesObjectArray", "GenericMethod",
    };

    private static readonly string[] MustBeAdmitted = { "ScalarControl", "StringControl" };

    [Test]
    public void AnUnmarshalableSignatureIsDroppedBeforeEitherEmitter()
    {
        using var probe = new ProbeAssembly("AdmRejectProbe", RejectProbeSource, unsafeCode: true);
        var diagnostics = new List<NetReferenceDiagnostic>();
        var surface = CollectDeclaring(probe, diagnostics, "Adm.Probe.Reject");

        var names = surface.Members.Select(m => m.Name).ToList();

        Assert.That(names, Is.SupersetOf(MustBeAdmitted),
            "CONTROL: the probe type resolved but its marshalable members are missing, so the "
            + "omission assertions below would be green for the wrong reason — nothing reached "
            + "the surface at all. Got: " + string.Join(", ", names));

        Assert.That(names.Intersect(MustBeOmitted), Is.Empty,
            "a signature type NetSurfaceCollector.FirstUnmarshalable rejects reached the "
            + "collected surface. The surface is what the shim and the proxy table are both "
            + "GENERATED FROM, so a member here becomes an export csc or clang must then spell "
            + "— a pointer, a ref struct or an open T cannot be spelled in a monomorphic C "
            + "export, and Object is permanently Rejected (§8.3: void* erasure is unsound). "
            + "Fix FirstUnmarshalable, not this test. Got: " + string.Join(", ", names));

        // §7.2: omission is a DIAGNOSED event, never silent. Without this the rule could be
        // satisfied by a collector that dropped members for no stated reason.
        var omitted = diagnostics.Where(d => d.Code == "BL6026").ToList();
        Assert.That(omitted, Is.Not.Empty,
            "the members were dropped but no BL6026 was raised. §7.2 requires the omission be "
            + "reported — a declared surface that silently loses members leaves the user's "
            + "`<NetProxy>` quietly meaning less than it says.");
    }

    // ------------------------------------------------------------------------------------
    // Part 2 — the admission contrapositive, over the REAL row table.
    // ------------------------------------------------------------------------------------

    /// <summary>
    /// Every §8.3 row, DERIVED from <see cref="NetMarshalTable.WireRows"/> rather than listed:
    /// a row added to the table without a wire form fails here instead of joining it untested.
    /// The probe source is GENERATED from the same table, so there is no second list to drift.
    /// </summary>
    private static IEnumerable<string> RowTypeNames() =>
        NetMarshalTable.WireRows.Keys
            .Where(k => k != "System.Void")
            .OrderBy(k => k, StringComparer.Ordinal);

    [Test]
    public void EverySection83RowSurvivesDeclaredCollection()
    {
        var rows = RowTypeNames().ToList();
        Assert.That(rows, Is.Not.Empty, "guard: §8.3's row table is empty, so this proves nothing");

        // One member per row, named for the row, so a failure names the type that fell out.
        var members = string.Join("\n", rows.Select((t, i) =>
            $"        public static void Row{i}(global::{t} v) {{ }}"));
        var source = "namespace Adm.Probe\n{\n    public static class Rows\n    {\n"
            + members + "\n    }\n}\n";

        using var probe = new ProbeAssembly("AdmRowProbe", source);
        var diagnostics = new List<NetReferenceDiagnostic>();
        var surface = CollectDeclaring(probe, diagnostics, "Adm.Probe.Rows");

        var present = surface.Members.Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
        var missing = rows
            .Select((t, i) => (Type: t, Name: "Row" + i))
            .Where(r => !present.Contains(r.Name))
            .Select(r => r.Type)
            .ToList();

        Assert.That(missing, Is.Empty,
            "a §8.3 row was DROPPED by NetSurfaceCollector, so a type the marshal table promises "
            + "a wire form for cannot cross at all. This is the demotion §6.4 forbids — a native "
            + "value silently becoming a handle, or in this case vanishing — and it is invisible "
            + "in the emitters, because both WireOf defaults answer Handle for anything they do "
            + "not recognise. Either the row left §8.3 or FirstUnmarshalable grew a branch that "
            + "swallows it. Rows lost: " + string.Join(", ", missing));
    }

    // ------------------------------------------------------------------------------------

    private static NetSurface CollectDeclaring(
        ProbeAssembly probe, ICollection<NetReferenceDiagnostic> diagnostics, params string[] types)
    {
        var project = new ProjectFile();
        project.NetProxyTypes.AddRange(types);

        var resolver = NetTypeResolver.Create(
            NetTypeResolverTestRefs.FrameworkPaths.Concat(new[] { probe.Path }));

        return NetSurfaceCollector.Collect(
            Array.Empty<BasicLang.Compiler.IR.IRModule>(), project, () => resolver, diagnostics);
    }

    /// <summary>A C# probe assembly compiled with Roslyn, owning a temp directory.</summary>
    private sealed class ProbeAssembly : IDisposable
    {
        private readonly string _dir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "blnet-adm-" + Guid.NewGuid().ToString("N"));

        internal string Path { get; }

        internal ProbeAssembly(string name, string source, bool unsafeCode = false)
        {
            System.IO.Directory.CreateDirectory(_dir);
            Path = System.IO.Path.Combine(_dir, name + ".dll");

            var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
                name,
                new[] { Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source) },
                NetTypeResolverTestRefs.FrameworkPaths.Select(
                    p => Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(p)),
                new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(
                    Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary,
                    allowUnsafe: unsafeCode));

            Microsoft.CodeAnalysis.Emit.EmitResult emit;
            using (var stream = System.IO.File.Create(Path))
                emit = compilation.Emit(stream);

            Assert.That(emit.Success, Is.True, "probe assembly failed to build: "
                + string.Join("\n", emit.Diagnostics.Where(
                    d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error))
                + "\n\n" + source);
        }

        public void Dispose()
        {
            try { System.IO.Directory.Delete(_dir, recursive: true); }
            catch (System.IO.IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
