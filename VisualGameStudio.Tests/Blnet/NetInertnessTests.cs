using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BasicLang.Compiler.ProjectSystem;
using BasicLang.Net;
using NUnit.Framework;
using VisualGameStudio.Tests.Compiler;

namespace VisualGameStudio.Tests.Blnet;

/// <summary>
/// P2a-1's central claim, made observable.
///
/// <para><b>Why "0 failed" and byte-identical stdout cannot cover this.</b> Task 8 makes the .NET
/// resolver reachable from the analyzer. The failure mode it introduces is a NEW WARNING emitted
/// on programs that compiled clean before — and a warning changes no exit code, no generated C++,
/// and no stdout, so the whole existing suite can stay green while every build in the IDE grows a
/// diagnostic line and every file in the editor grows a squiggle.</para>
///
/// <para><b>This fixture drives <see cref="CppProjectBuilder.EmitCore"/> — the real native build
/// entry point — on real temp <c>.blproj</c> projects.</b> An earlier draft re-implemented
/// <c>EmitCore</c>'s resolver-factory wiring inside the fixture, which left every line of that
/// plumbing untested: the factory hand-off, the <c>NetDiagnostics</c> merge into the closure, and
/// the <c>CppDiagnostic</c> emission could each be deleted with the gate still green. A standing
/// gate must sit on the entry point it claims to guard.</para>
///
/// <para><b>Scope of the wiring, stated precisely so nobody infers more than is true.</b>
/// <c>CompilerOptions.NetResolverFactory</c> is set at exactly ONE site repo-wide —
/// <c>CppProjectBuilder</c>, the native build path. Every C#-backend path (<c>Program.cs</c>,
/// <c>BuildService</c>, <c>MultiTargetCompiler</c>) leaves it null, and so does the LSP. So in
/// P2a-1 a BL6016/BL6017/BL6023 can appear ONLY on a native build: no C# project can produce one,
/// and none of them reaches an editor squiggle. Spec §6.3's C#-backend warning row is deliberately
/// unimplemented here — wiring it would add spurious-warning risk to a path P2a-1 gains nothing
/// from — and moves to P2a-2. If a later task wires either, widen this fixture to cover it, or the
/// gate silently stops guarding the path that actually regressed.</para>
///
/// <para><b>Keep this fixture through Tasks 9-16.</b> It is the standing guard on the plan's
/// central claim, and every later task widens what the resolver sees.</para>
/// </summary>
[TestFixture]
[NonParallelizable]
public class NetInertnessTests
{
    /// <summary>
    /// The codes P2a-1 introduces. Asserted instead of a blanket <c>StartsWith("BL60")</c> because
    /// <see cref="CppProjectBuilder.EmitCore"/> legitimately produces PRE-EXISTING build-rule
    /// diagnostics on this path — BL6005 (no toolchain resolved, since these tests deliberately
    /// pass none) and BL6007 (a project with no translation units). Those are not P2a-1 output and
    /// folding them in would make the gate permanently red for reasons unrelated to its claim.
    /// </summary>
    private static readonly string[] NetFindingCodes = { "BL6016", "BL6017", "BL6018", "BL6023" };

    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "blnet-inert-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        for (var i = 0; i < 3; i++)
        {
            try { Directory.Delete(_dir, recursive: true); return; }
            catch (IOException) { System.Threading.Thread.Sleep(200); }
            catch (UnauthorizedAccessException) { System.Threading.Thread.Sleep(200); }
        }
    }

    private static string Blproj(string outputType) => $"""
        <BasicLangProject Version="1.0">
          <PropertyGroup>
            <ProjectName>InertnessProbe</ProjectName>
            <OutputType>{outputType}</OutputType>
            <TargetBackend>Cpp</TargetBackend>
          </PropertyGroup>
        </BasicLangProject>
        """;

    /// <summary>
    /// Compiles a project through the REAL native entry point. <c>resolveToolchain</c> answers null
    /// deliberately: EmitCore runs phase 1 (.NET references), the transpile, and this task's
    /// diagnostic block BEFORE the toolchain gate, so everything under test executes and nothing
    /// depends on a C++ compiler being installed.
    /// </summary>
    private (CppProjectBuildResult Result, CppEmitOutcome Outcome) EmitViaBuilder(
        IReadOnlyDictionary<string, string> files, string outputType = "Exe")
    {
        foreach (var file in files)
        {
            var full = Path.Combine(_dir, file.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, file.Value);
        }

        var projectPath = Path.Combine(_dir, "InertnessProbe.blproj");
        File.WriteAllText(projectPath, Blproj(outputType));

        var result = new CppProjectBuildResult();
        var outcome = CppProjectBuilder.EmitCore(
            ProjectFile.Load(projectPath), "Debug", result,
            resolveToolchain: () => null, forIntelliSense: false);
        return (result, outcome);
    }

    /// <summary>
    /// Asserts the contract on BOTH channels this task writes to. Checking only
    /// <c>result.Diagnostics</c> would miss the closure merge; checking only the closure would miss
    /// the <c>CppDiagnostic</c> emission.
    /// </summary>
    private static void AssertNoNetFindings(
        string label, CppProjectBuildResult result, CppEmitOutcome outcome)
    {
        var onResult = result.Diagnostics
            .Where(d => NetFindingCodes.Contains(d.Code))
            .Select(d => d.Code + ": " + d.Message).ToList();
        var onClosure = (outcome.NetReferences?.Diagnostics ?? Array.Empty<NetReferenceDiagnostic>())
            .Where(d => NetFindingCodes.Contains(d.Code))
            .Select(d => d.Code + ": " + d.Message).ToList();

        Assert.That(onResult.Concat(onClosure), Is.Empty,
            $"'{label}' drew a new .NET diagnostic — on result.Diagnostics: "
            + (onResult.Count == 0 ? "(none)" : string.Join(" | ", onResult))
            + "; on outcome.NetReferences.Diagnostics: "
            + (onClosure.Count == 0 ? "(none)" : string.Join(" | ", onClosure))
            + ". P2a-1 must emit NO new BL60xx diagnostic on a program that compiled clean at "
            + "dfee728. A warning is still new output: it changes CLI stdout and the IDE error "
            + "list. Fix the claim predicate or the analyzer's probe gating "
            + "(SemanticAnalyzer.ProbeNetTypeReference / ProbeNetMemberAccess) — do NOT relax this "
            + "assertion.");
    }

    // ---- The 13 P1 parity programs -------------------------------------------------------

    [TestCaseSource(typeof(BclBackendParityTests), nameof(BclBackendParityTests.ParityCases))]
    public void ParityProgramsDrawNoNetDiagnostics(BclBackendParityTests.ParityProgram program)
    {
        var (result, outcome) =
            EmitViaBuilder(new Dictionary<string, string> { ["Main.bas"] = program.Source });

        AssertNoNetFindings(program.Name, result, outcome);
    }

    // ---- Shapes the parity corpus cannot reach ------------------------------------------
    //
    // The 13 parity programs are P1 BCL exercises that obey P1's ten constraints by construction:
    // no Using directive, no collections, no LINQ, no lambda, no interface, no generic user type.
    // Measured: they were blind to a mutation that made every unresolved PascalCase name a BL6016.
    // These four cover the shapes that mutation DOES reach.

    /// <summary>
    /// The only program in the corpus that exercises <c>_netNamespaces</c> — the analyzer's
    /// <c>Using</c>-directive set, which <c>NetCandidateNames</c> searches before the ambient set.
    /// This is the exact shape spec §6.3 says must not regress: names imported by a <c>Using</c>
    /// and then used unqualified.
    /// </summary>
    private const string UsingDirectivesProgram = """
        Using System
        Using System.Text
        Using System.Collections.Generic

        Module M
         Sub Main()
          Dim sb As New StringBuilder()
          sb.Append("a")
          sb.Append("b")
          Console.WriteLine(sb.ToString())
          Dim names As New List(Of String)
          names.Add("x")
          Console.WriteLine(names.Count)
         End Sub
        End Module
        """;

    /// <summary>
    /// Collections, iteration, a delegate and a LINQ query — the collection-member and delegate
    /// branches of the analyzer, which nothing else in this corpus touches.
    /// </summary>
    private const string CollectionsAndLambdaProgram = """
        Module M
         Sub Main()
          Dim nums As New List(Of Integer)
          nums.Add(3)
          nums.Add(1)
          nums.Add(2)
          For Each n As Integer In nums
           Console.WriteLine(n)
          Next
          Dim twice As Func(Of Integer, Integer) = Function(x As Integer) x * 2
          Console.WriteLine(twice(21))
          Dim lookup As New Dictionary(Of String, Integer)
          lookup.Add("one", 1)
          Console.WriteLine(lookup.Count)
         End Sub
        End Module
        """;

    /// <summary>
    /// <b>The sharpest false-positive probe available.</b> User types whose names COLLIDE with
    /// ambient .NET types (<c>System.Threading.Timer</c>, <c>System.Version</c>) and which have
    /// members of their own. If the probe gating ever lets a user type reach
    /// <c>ProbeNetMemberAccess</c>, this draws
    /// <c>BL6017: .NET type 'System.Threading.Timer' has no accessible member named 'TickCount'</c>
    /// — a false accusation about the user's own class. Safe today only because the receiver
    /// resolves to the user symbol first; nothing else pins that.
    /// </summary>
    private const string AmbientNameCollisionProgram = """
        Using System

        Public Class Timer
         Public TickCount As Integer
         Public Sub New()
          Me.TickCount = 0
         End Sub
         Public Sub Advance()
          Me.TickCount = Me.TickCount + 1
         End Sub
        End Class

        Public Class Version
         Public Label As String
         Public Sub New(label As String)
          Me.Label = label
         End Sub
        End Class

        Module M
         Sub Main()
          Dim t As New Timer()
          t.Advance()
          t.Advance()
          Console.WriteLine(t.TickCount)
          Dim v As New Version("1.0")
          Console.WriteLine(v.Label)
         End Sub
        End Module
        """;

    /// <summary>
    /// An interface plus a collection of it — neither appears anywhere else in the corpus.
    /// </summary>
    private const string InterfaceProgram = """
        Public Interface IShape
         Function Area() As Double
        End Interface

        Public Class Box
         Implements IShape
         Public W As Double
         Public H As Double
         Public Sub New(w As Double, h As Double)
          Me.W = w
          Me.H = h
         End Sub
         Public Function Area() As Double Implements IShape.Area
          Return Me.W * Me.H
         End Function
        End Class

        Module M
         Sub Main()
          Dim shapes As New List(Of IShape)
          shapes.Add(New Box(2.0, 3.0))
          For Each s As IShape In shapes
           Console.WriteLine(s.Area())
          Next
         End Sub
        End Module
        """;

    private static IEnumerable<TestCaseData> ShapeCases()
    {
        yield return new TestCaseData("using-directives", UsingDirectivesProgram)
            .SetArgDisplayNames("using-directives");
        yield return new TestCaseData("collections-lambda", CollectionsAndLambdaProgram)
            .SetArgDisplayNames("collections-lambda");
        yield return new TestCaseData("ambient-name-collision", AmbientNameCollisionProgram)
            .SetArgDisplayNames("ambient-name-collision");
        yield return new TestCaseData("interface", InterfaceProgram)
            .SetArgDisplayNames("interface");
    }

    [TestCaseSource(nameof(ShapeCases))]
    public void LanguageShapesDrawNoNetDiagnostics(string label, string source)
    {
        var (result, outcome) = EmitViaBuilder(new Dictionary<string, string> { ["Main.bas"] = source });

        AssertNoNetFindings(label, result, outcome);
    }

    // ---- The built-in project templates --------------------------------------------------

    [TestCase("console")]
    [TestCase("game")]
    [TestCase("classlib")]
    [TestCase("empty")]
    public void ProjectTemplatesDrawNoNetDiagnostics(string templateName)
    {
        var template = new TemplateEngine().GetTemplate(templateName);
        Assert.That(template, Is.Not.Null,
            $"TemplateEngine no longer has a '{templateName}' template. Update this fixture's "
            + "corpus to the template's new name rather than dropping the coverage.");

        var files = template.Files
            .Where(f => IsBasicLangSource(f.Key))
            .ToDictionary(f => f.Key,
                          f => f.Value.Replace("{{ProjectName}}", "InertnessProbe")
                                      .Replace("{{Date}}", "2026-07-30"));

        // "empty" legitimately contributes no sources — that IS the shape under test (EmitCore
        // reaches its no-translation-units rule, BL6007, without any .NET finding).
        if (templateName != "empty")
        {
            Assert.That(files, Is.Not.Empty,
                $"The '{templateName}' template contributed no BasicLang sources, so this case "
                + "proves nothing. Fix the source-extension filter in this fixture.");
        }

        // classlib is a Library by definition (its own .blproj says so) and has no Sub Main;
        // building it as an Exe would trip the entry-point rule for a reason unrelated to .NET.
        var (result, outcome) = EmitViaBuilder(files, templateName == "classlib" ? "Library" : "Exe");

        // The .NET contract is asserted unconditionally and BEFORE any tolerance below: the
        // analyzer runs to completion and its findings are collected even for a unit that fails
        // for an unrelated reason, so this coverage never depends on the template compiling.
        AssertNoNetFindings(templateName, result, outcome);

        // "empty" has no sources, so BL6007 (no translation units) is its correct outcome.
        if (templateName == "empty")
        {
            Assert.That(result.Diagnostics.Select(d => d.Code), Does.Contain("BL6007"),
                "An 'empty' project should reach EmitCore's no-translation-units rule. If it no "
                + "longer does, this case is no longer testing the minimal-project shape.");
            return;
        }

        Assert.That(TranspileErrors(result), Is.Empty,
            $"The '{templateName}' template no longer transpiles. That is a regression in the "
            + "compiler or the template, not in this fixture.");
    }

    /// <summary>
    /// Compile errors only — excludes warnings and the toolchain/entry-point BUILD-RULE codes this
    /// path always produces because these tests intentionally resolve no toolchain.
    /// </summary>
    private static List<string> TranspileErrors(CppProjectBuildResult result) =>
        result.Diagnostics
            .Where(d => !d.IsWarning)
            .Where(d => d.Code is not ("BL6005" or "BL6007" or "BL6009" or "BL6015"))
            .Select(d => d.Message)
            .ToList();

    private static bool IsBasicLangSource(string fileName) =>
        fileName.EndsWith(".bas", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".mod", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".cls", StringComparison.OrdinalIgnoreCase);

    // ---- Guards on the fixture itself ----------------------------------------------------

    private const string UnresolvableNetTypeProgram = """
        Module M
         Sub Main()
          Dim x As System.ThisTypeDoesNotExistAnywhere
          Console.WriteLine("done")
         End Sub
        End Module
        """;

    /// <summary>
    /// A gate that cannot fail is worse than no gate. This proves the corpus really runs with .NET
    /// resolution ACTIVE, and it is the ONLY test that covers <c>EmitCore</c>'s three new lines end
    /// to end: the <c>NetResolverFactory</c> hand-off to <c>BasicCompiler</c>, the
    /// <c>NetDiagnostics</c> merge into the closure, and the <c>CppDiagnostic</c> emission. Delete
    /// any one of them and this goes red.
    /// </summary>
    [Test]
    public void TheGateIsArmed_AnUnresolvableSystemTypeReachesBothChannels()
    {
        var (result, outcome) =
            EmitViaBuilder(new Dictionary<string, string> { ["Main.bas"] = UnresolvableNetTypeProgram });

        Assert.Multiple(() =>
        {
            Assert.That(outcome.NetReferences, Is.Not.Null,
                "EmitCore did not get past phase 1, so nothing below is meaningful.");
            Assert.That(outcome.NetReferences!.Diagnostics.Select(d => d.Code), Does.Contain("BL6016"),
                "The analyzer's .NET findings are not reaching CppEmitOutcome.NetReferences"
                + ".Diagnostics. Check the NetDiagnostics merge in CppProjectBuilder, and that "
                + "CompilerOptions.NetResolverFactory is still passed to BasicCompiler — without "
                + "it every other assertion in this fixture is vacuous.");
            Assert.That(result.Diagnostics.Select(d => d.Code), Does.Contain("BL6016"),
                "The analyzer's .NET findings are not reaching result.Diagnostics, so a user would "
                + "never see them. Check the CppDiagnostic emission block in CppProjectBuilder.");
            // P2a-2 THE FLIP: native findings are ERRORS (spec §6.3's native row) —
            // MergeNetDiagnostics routes IsWarning:false through Fail().
            Assert.That(result.Diagnostics.Where(d => d.Code == "BL6016").All(d => !d.IsWarning),
                Is.True,
                "A native BL6016 was emitted as a WARNING. Since the P2a-2 flip the native "
                + "backend hard-errors on an unresolved .NET type — a warning here would let "
                + "the build limp on to raw C++ failure.");
        });
    }

    /// <summary>
    /// P2a-2 THE FLIP: the severity contract inverts on the native path — §6.5 findings are
    /// build ERRORS (spec §6.3's native row), routed through <c>MergeNetDiagnostics</c>'
    /// <c>Fail()</c> arm, and <c>EmitCore</c> STOPS at the analyzer merge (D-P3: a program
    /// with .NET errors never reaches the checker or codegen, so the old "no C++ mapping"
    /// sanity companion is structurally unreachable here now). The C# backend's warning row
    /// is pinned by <c>NetFlipTests.UnresolvableSystemType_StaysAWarningOnTheCSharpBackend</c>.
    /// (Pre-flip this test was <c>NetFindingsAreNeverErrors</c> — deliberately churned HERE
    /// and nowhere else, per the plan's standing inertness rule.)
    /// </summary>
    [Test]
    public void NetFindingsAreNativeErrors_FailingTheBuild()
    {
        var (result, outcome) =
            EmitViaBuilder(new Dictionary<string, string> { ["Main.bas"] = UnresolvableNetTypeProgram });

        var errorCodes = result.Diagnostics.Where(d => !d.IsWarning).Select(d => d.Code).ToList();

        Assert.That(errorCodes, Does.Contain("BL6016"),
            "The native BL6016 must go through Fail() and surface as an ERROR diagnostic. Got "
            + "errors: " + string.Join(" | ", result.Diagnostics.Where(d => !d.IsWarning)
                                             .Select(d => d.Code + ": " + d.Message)));

        Assert.That(result.Success, Is.False,
            "An unresolvable .NET type must FAIL a native build after the flip (§6.3).");

        Assert.That(outcome.NetReferences!.Diagnostics.Where(d => NetFindingCodes.Contains(d.Code)),
            Is.All.Matches<NetReferenceDiagnostic>(d => !d.IsWarning),
            "Native findings must be IsWarning:false on the closure channel too — the IDE's "
            + "Error List severity comes from this bit.");

        // Sanity (the flip's D-P3 half): the build stopped AT the analyzer merge — the
        // checker never ran, so its "no C++ mapping" blob must NOT appear alongside.
        Assert.That(TranspileErrors(result),
            Has.None.Contains("no C++ mapping exists for this type"),
            "EmitCore continued past the analyzer's .NET errors into the capability checker. "
            + "The flip's contract is fail-fast at the merge (analyzerNetErrors > 0 returns).");
    }

    /// <summary>
    /// Constraint (a) from the Task 8 review: <see cref="NetTypeResolver"/> REQUIRES generic arity
    /// and never guesses, so an UNCLAIMED generic must be asked for as <c>Queue`1</c>.
    ///
    /// <para><b>The names are FULLY QUALIFIED on purpose.</b> A bare <c>Queue(Of Integer)</c> never
    /// reaches the resolver at all — <c>ProbeNetTypeReference</c> short-circuits on
    /// <c>IsExplicitlyNetQualified</c> — so a bare-name case would pass identically if
    /// <c>NetCandidateNames</c> emitted no <c>`N</c> suffix, testing nothing. These spellings do
    /// reach it, and go red if the arity mapping is dropped.</para>
    /// </summary>
    [TestCase("System.Collections.Generic.Queue(Of Integer)")]
    [TestCase("System.Collections.Generic.Stack(Of Integer)")]
    [TestCase("System.Collections.Generic.SortedDictionary(Of Integer, String)")]
    [TestCase("System.Collections.Generic.LinkedList(Of Integer)")]
    public void QualifiedUnclaimedGenericsResolveByArity(string typeSpelling)
    {
        var source = $"""
            Module M
             Sub Main()
              Dim q As {typeSpelling}
              Console.WriteLine("done")
             End Sub
            End Module
            """;

        var (result, outcome) = EmitViaBuilder(new Dictionary<string, string> { ["Main.bas"] = source });

        AssertNoNetFindings(typeSpelling, result, outcome);
    }

    /// <summary>
    /// The bare spelling, for the record: it must also stay silent — since Task 4 replaced
    /// the native evidence bar with real §6.5 resolution (and the Task-5 flip made findings
    /// errors), the reason is that these RESOLVE through the ambient
    /// System.Collections.Generic by arity, exactly like the qualified spellings above.
    /// Kept separate so a regression in the ambient set is not confused with one in the
    /// arity mapping. (Re-pinned to resolved behavior at the P2a-2 flip; the pre-Task-4
    /// name was BareUnclaimedGenericsAreBelowTheEvidenceBar.)
    /// </summary>
    [TestCase("Queue(Of Integer)")]
    [TestCase("SortedDictionary(Of Integer, String)")]
    public void BareUnclaimedGenericsResolveThroughTheAmbientSet(string typeSpelling)
    {
        var source = $"""
            Module M
             Sub Main()
              Dim q As {typeSpelling}
              Console.WriteLine("done")
             End Sub
            End Module
            """;

        var (result, outcome) = EmitViaBuilder(new Dictionary<string, string> { ["Main.bas"] = source });

        AssertNoNetFindings(typeSpelling, result, outcome);
    }
}
