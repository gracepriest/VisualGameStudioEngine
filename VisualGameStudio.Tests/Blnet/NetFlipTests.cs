using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BasicLang;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.ProjectSystem;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Net;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using TypeInfo = BasicLang.Compiler.SemanticAnalysis.TypeInfo;
using TypeKind = BasicLang.Compiler.SemanticAnalysis.TypeKind;

namespace VisualGameStudio.Tests.Blnet;

/// <summary>
/// P2a-2 Task 5 — THE FLIP (spec §6.3 / §11.4 / D-P7). The five P2-territory names
/// (Regex, Uri, Stream, FileInfo, DirectoryInfo) move Rejected → ManagedOwned, the
/// capability checker accepts them, declaration positions lower to the always-emitted
/// <c>BasicLang::NetRef</c> handle, and the analyzer's §6.5 findings
/// (BL6016/17/18/19/23/24) become build ERRORS on the native backend while the C#
/// backend keeps §6.3's warning row.
///
/// <para><b>What this fixture deliberately does NOT cover:</b> call lowering (a resolved
/// <c>r.IsMatch("x")</c> still emits pre-flip call shapes — Task 7a), collections crossing
/// the boundary (§8.5, Task 9), and the shim publish (Task 7b). Everything here is
/// analyzer/checker/declaration-level.</para>
/// </summary>
[TestFixture]
[NonParallelizable]
public class NetFlipTests
{
    /// <summary>One resolver for the fixture — construction reads ~170 assemblies.</summary>
    private static readonly Lazy<NetTypeResolver> SharedResolver =
        new(() => NetTypeResolver.Create(NetTypeResolverTestRefs.FrameworkPaths));

    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "blnet-flip-" + Guid.NewGuid().ToString("N"));
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

    // ------------------------------------------------------------------------------------
    // Harnesses (the builder one mirrors NetInertnessTests — the REAL native entry point)
    // ------------------------------------------------------------------------------------

    private (CppProjectBuildResult Result, CppEmitOutcome Outcome) EmitViaBuilder(string mainBas)
    {
        File.WriteAllText(Path.Combine(_dir, "Main.bas"), mainBas);
        var projectPath = Path.Combine(_dir, "FlipProbe.blproj");
        File.WriteAllText(projectPath, """
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>FlipProbe</ProjectName>
                <OutputType>Exe</OutputType>
                <TargetBackend>Cpp</TargetBackend>
              </PropertyGroup>
            </BasicLangProject>
            """);

        var result = new CppProjectBuildResult();
        var outcome = CppProjectBuilder.EmitCore(
            ProjectFile.Load(projectPath), "Debug", result,
            resolveToolchain: () => null, forIntelliSense: false);
        return (result, outcome);
    }

    private static SemanticAnalyzer Analyze(string source, bool nativeBackend)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty,
            "parse errors:\n" + string.Join("\n", parser.Errors.Select(e => e.Message)));

        var analyzer = new SemanticAnalyzer();
        analyzer.ConfigureNetResolution(() => SharedResolver.Value, nativeBackend);
        Assert.That(analyzer.Analyze(ast), Is.True,
            "semantic errors:\n" + string.Join("\n", analyzer.Errors.Select(e => e.Message)));
        return analyzer;
    }

    /// <summary>Emit-only combined-mode C++, with the resolver armed (native path).</summary>
    private static string CompileToCppWithResolver(string source)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty,
            "parse errors:\n" + string.Join("\n", parser.Errors.Select(e => e.Message)));

        var analyzer = new SemanticAnalyzer();
        analyzer.ConfigureNetResolution(() => SharedResolver.Value, nativeBackend: true);
        Assert.That(analyzer.Analyze(ast), Is.True,
            "semantic errors:\n" + string.Join("\n", analyzer.Errors.Select(e => e.Message)));

        var module = new IRBuilder(analyzer).Build(ast, "TestModule");
        return new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false })
            .Generate(module);
    }

    // ------------------------------------------------------------------------------------
    // The flip's acceptance shape: Dim r As New Regex("ab") + r.IsMatch("x")
    // ------------------------------------------------------------------------------------

    // The result is deliberately NOT captured into an inferred local: pre-Task-7a the
    // analyzer cannot type `r.IsMatch(...)`'s value (LookupNetTypeMember has no Regex
    // surface), so `Dim ok = ...` would degrade to Object — and Object is permanently
    // Rejected. The flip's acceptance shape is the declaration + the call.
    private const string RegexProgram = """
        Module M
         Sub Main()
          Dim r As New Regex("ab")
          r.IsMatch("x")
          Console.WriteLine("done")
         End Sub
        End Module
        """;

    [Test]
    public void FlippedRegexProgram_PassesAnalyzerAndChecker_OnTheNativePath()
    {
        var (result, outcome) = EmitViaBuilder(RegexProgram);

        var netCodes = new[] { "BL6016", "BL6017" };
        Assert.Multiple(() =>
        {
            Assert.That(outcome.NetReferences, Is.Not.Null, "EmitCore did not get past phase 1");
            Assert.That(outcome.NetReferences!.Diagnostics.Where(d => netCodes.Contains(d.Code)),
                Is.Empty,
                "Regex resolves through the ambient set and IsMatch name-matches — the flip "
                + "must not draw BL6016/BL6017 on the acceptance shape: "
                + string.Join(" | ", outcome.NetReferences.Diagnostics
                    .Select(d => d.Code + ": " + d.Message)));
            Assert.That(result.Diagnostics.Where(d => netCodes.Contains(d.Code)), Is.Empty,
                "…and none on result.Diagnostics either");
            Assert.That(result.Diagnostics.Where(d => d.Message.Contains("no C++ mapping")),
                Is.Empty,
                "the capability checker must ACCEPT ManagedOwned types after the flip — a "
                + "'no C++ mapping' blob means CppCapabilityChecker still rejects them: "
                + string.Join(" | ", result.Diagnostics.Select(d => d.Code + ": " + d.Message)));
        });
    }

    [Test]
    public void FlippedRegexProgram_DeclaresANetRefLocal()
    {
        var cpp = CompileToCppWithResolver(RegexProgram);
        Assert.That(cpp, Does.Contain("BasicLang::NetRef r"),
            "a ManagedOwned local must DECLARE as the BasicLang::NetRef handle (D-P7 / §8.3 "
            + "— declaration-level; call lowering is Task 7a):\n" + cpp);
    }

    [Test]
    public void UnresolvableSystemType_IsANativeError_FailingTheBuild()
    {
        var (result, outcome) = EmitViaBuilder("""
            Module M
             Sub Main()
              Dim x As System.Nope
              Console.WriteLine("done")
             End Sub
            End Module
            """);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False,
                "an unresolvable .NET type is a native build ERROR after the flip (§6.3)");
            Assert.That(result.Diagnostics.Where(d => d.Code == "BL6016" && !d.IsWarning),
                Is.Not.Empty,
                "BL6016 must surface as an ERROR diagnostic (Fail path), not a warning: "
                + string.Join(" | ", result.Diagnostics
                    .Select(d => $"{d.Code}({(d.IsWarning ? "warn" : "err")}): {d.Message}")));
            Assert.That(outcome.NetReferences!.Diagnostics
                    .Where(d => d.Code == "BL6016").Select(d => d.IsWarning),
                Has.All.False,
                "…and as IsWarning:false on the closure channel");
            Assert.That(outcome.Completed, Is.False,
                "EmitCore must stop at the analyzer merge — the checker/codegen phases must "
                + "not run on a program with .NET errors (D-P3)");
        });
    }

    /// <summary>The §6.3 severity split, at the analyzer seam: same program, both backends.</summary>
    [Test]
    public void UnresolvableSystemType_StaysAWarningOnTheCSharpBackend()
    {
        const string source = """
            Module M
             Sub Main()
              Dim x As System.Nope
              Console.WriteLine("done")
             End Sub
            End Module
            """;

        var native = Analyze(source, nativeBackend: true);
        var nativeBl6016 = native.NetDiagnostics.Where(d => d.Code == "BL6016").ToList();
        Assert.That(nativeBl6016, Has.Count.EqualTo(1));
        Assert.That(nativeBl6016[0].IsWarning, Is.False,
            "NATIVE: BL6016 is an error after the flip (§6.3's native row)");

        var csharp = Analyze(source, nativeBackend: false);
        var csharpBl6016 = csharp.NetDiagnostics.Where(d => d.Code == "BL6016").ToList();
        Assert.That(csharpBl6016, Has.Count.EqualTo(1));
        Assert.That(csharpBl6016[0].IsWarning, Is.True,
            "C#: BL6016 stays a warning (§6.3's C# row) — the flip is native-only");
    }

    // ------------------------------------------------------------------------------------
    // Flag 2: BL6024 extends to ManagedOwned/resolved CONSTRUCTORS in generic bodies —
    // the checker rejection that covered `New Regex(...)` in a template pre-flip is gone
    // with this commit, so without this Task 7a would receive a call inside a C++ template.
    // ------------------------------------------------------------------------------------

    [Test]
    public void ManagedOwnedConstructorInAGenericBody_IsABl6024Error_OnTheNativePath()
    {
        const string source = """
            Module M
             Function F(Of T)(x As T) As String
              Dim r As New Regex("a")
              Return "done"
             End Function
             Sub Main()
              Console.WriteLine("done")
             End Sub
            End Module
            """;

        var native = Analyze(source, nativeBackend: true);
        var bl6024 = native.NetDiagnostics.Where(d => d.Code == "BL6024").ToList();
        Assert.That(bl6024, Has.Count.EqualTo(1),
            "a ManagedOwned ctor inside generic F(Of T) must draw BL6024 — pre-flip the "
            + "checker's IRNewObject rejection covered this shape; post-flip only BL6024 "
            + "stands between it and a .NET call inside a C++ template. Got: "
            + string.Join(" | ", native.NetDiagnostics.Select(d => d.Code + ": " + d.Message)));
        Assert.That(bl6024[0].Message, Does.Contain("Regex"));
        Assert.That(bl6024[0].IsWarning, Is.False, "BL6024 is a native ERROR after the flip");

        var csharp = Analyze(source, nativeBackend: false);
        Assert.That(csharp.NetDiagnostics.Select(d => d.Code), Does.Not.Contain("BL6024"),
            "the C# backend compiles generic bodies with .NET ctors fine — BL6024 is native-only");
    }

    /// <summary>
    /// The severity promotion must not break natively-lowered shapes: `Throw New
    /// ArgumentException(...)` lowers to std::runtime_error and a catch variable's
    /// `.Message` lowers to what() — both are template-safe, no proxy involved. A BL6024
    /// (now an ERROR) on either would be a false build break on a valid program.
    /// </summary>
    [Test]
    public void ThrowAndCatchWithMessage_InsideAGenericBody_DrawsNoNetFindings()
    {
        const string source = """
            Module M
             Function F(Of T)(x As T) As String
              Try
               Throw New ArgumentException("boom")
              Catch e As ArgumentException
               Console.WriteLine(e.Message)
              End Try
              Return "done"
             End Function
             Sub Main()
              Console.WriteLine(F(Of Integer)(1))
             End Sub
            End Module
            """;

        var native = Analyze(source, nativeBackend: true);
        Assert.That(native.NetDiagnostics, Is.Empty,
            "a natively-lowered throw/catch/Message shape inside a generic body must draw "
            + "NOTHING — these never route through the shim. Got: "
            + string.Join(" | ", native.NetDiagnostics.Select(d => d.Code + ": " + d.Message)));
    }

    [Test]
    public void ClaimedCollectionConstructorInAGenericBody_DrawsNoBl6024()
    {
        const string source = """
            Module M
             Function F(Of T)(x As T) As Integer
              Dim items As New List(Of Integer)()
              items.Add(1)
              Return items.Count
             End Function
             Sub Main()
              Console.WriteLine(F(Of Integer)(1))
             End Sub
            End Module
            """;

        var native = Analyze(source, nativeBackend: true);
        Assert.That(native.NetDiagnostics.Select(d => d.Code), Does.Not.Contain("BL6024"),
            "`New List(Of Integer)` is claimed (§6.5 row (b)) and lowers natively — the ctor "
            + "probe must never judge claimed names, or every generic collection helper breaks");
    }

    // ------------------------------------------------------------------------------------
    // Item 4 + Flag 1: the reference-resolution BL6021 promotions
    // ------------------------------------------------------------------------------------

    [Test]
    public void ProjectReference_IsABl6021Error_NamingTheWorkaround()
    {
        var project = new ProjectFile { Backend = "cpp" };
        project.ProjectReferences.Add("..\\Sibling\\Sibling.blproj");

        var closure = NetReferenceResolver.Resolve(project, Path.Combine(_dir, "App.blproj"));

        var diag = closure.Diagnostics.Single();
        Assert.That(diag.Code, Is.EqualTo("BL6021"));
        Assert.That(diag.IsWarning, Is.False,
            "THE FLIP promotes <ProjectReference> to an ERROR on the native path — "
            + "cross-project compilation does not exist and the message names the fix");
        Assert.That(diag.Message, Does.Contain("HintPath"),
            "the message must keep naming the <Reference>+<HintPath> workaround");
    }

    /// <summary>
    /// Flag-1 decision (documented at the NetReferenceResolver site): the D-P2 TFM-rule
    /// BL6021 is promoted to an ERROR here, not surface-gated at Task 7b — it matches the
    /// severity of every sibling BL6021 in Resolve (a declared reference that cannot be
    /// used), and post-flip a net9 reference is guaranteed to break the pinned net8.0 shim
    /// publish the moment any surface exists.
    /// </summary>
    [Test]
    public void Net9AttributedReference_IsABl6021Error_AfterTheFlip()
    {
        var probe = EmitProbeAssembly("FlipNine", """
            [assembly: System.Runtime.Versioning.TargetFramework(".NETCoreApp,Version=v9.0",
                       FrameworkDisplayName = "probe")]
            namespace TfmLib { public static class TfmType { public static int Answer() => 42; } }
            """);

        var project = new ProjectFile { Backend = "cpp" };
        project.AssemblyReferences.Add(new AssemblyReference
        { Name = "FlipNine", HintPath = Path.GetFileName(probe) });

        var closure = NetReferenceResolver.Resolve(project, Path.Combine(_dir, "probe.blproj"));

        var tfm = closure.Diagnostics
            .Where(d => d.Code == "BL6021" && d.Message.Contains("newer than")).ToList();
        Assert.That(tfm, Has.Count.EqualTo(1), "guard: the D-P2 rule still fires");
        Assert.That(tfm[0].IsWarning, Is.False,
            "Flag-1: the net9+ TFM BL6021 is an ERROR after the flip (see the decision "
            + "comment at the NetReferenceResolver site)");
    }

    private string EmitProbeAssembly(string name, string source)
    {
        var path = Path.Combine(_dir, name + ".dll");
        var compilation = CSharpCompilation.Create(
            name,
            new[] { CSharpSyntaxTree.ParseText(source) },
            NetTypeResolverTestRefs.FrameworkPaths.Select(p => MetadataReference.CreateFromFile(p)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var emit = compilation.Emit(path);
        Assert.That(emit.Success, Is.True,
            "the FIXTURE failed to build its probe assembly: " + string.Join("\n",
                emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return path;
    }

    // ------------------------------------------------------------------------------------
    // Step 2a: the unresolved-base gate — no false BL6016, and .NET Inherits stays rejected
    // ------------------------------------------------------------------------------------

    /// <summary>
    /// The false-BL6016 shape: classes are not pre-registered, so a FORWARD-referenced user
    /// base under a `Using` hits the `_netNamespaces.Count > 0` opaque-base gate
    /// (SemanticAnalyzer.Visit(ClassDeclarationNode)). That gate is NOT probed — proving a
    /// valid program cannot draw a BL6016 ERROR from its own class hierarchy.
    /// </summary>
    [Test]
    public void ForwardReferencedUserBaseUnderAUsing_DrawsNoBl6016()
    {
        var analyzer = Analyze("""
            Using System

            Public Class Derived
             Inherits Root
             Public Sub New()
             End Sub
            End Class

            Public Class Root
             Public X As Integer
            End Class

            Module M
             Sub Main()
              Dim d As New Derived()
              Console.WriteLine("done")
             End Sub
            End Module
            """, nativeBackend: true);

        Assert.That(analyzer.NetDiagnostics.Select(d => d.Code), Does.Not.Contain("BL6016"),
            "the unresolved-base gate must stay UNPROBED: a forward-referenced user base "
            + "under a Using is a valid program, and post-flip a false BL6016 here would be "
            + "a false BUILD BREAK. Got: "
            + string.Join(" | ", analyzer.NetDiagnostics.Select(d => d.Code + ": " + d.Message)));
    }

    [Test]
    public void NativeInheritsOfANetClass_StaysCheckerRejected_NotABl6016()
    {
        const string source = """
            Using System.IO

            Public Class MyStream
             Inherits Stream
             Public Sub New()
             End Sub
            End Class

            Module M
             Sub Main()
              Console.WriteLine("done")
             End Sub
            End Module
            """;

        var (result, outcome) = EmitViaBuilder(source);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False,
                "native Inherits of a .NET class has no lowering — the flip must NOT make "
                + "it silently accepted (a ManagedOwned base would emit `: public NetRef`)");
            Assert.That(result.Diagnostics.Where(d => !d.IsWarning)
                    .Any(d => d.Message.Contains("Inherits") || d.Message.Contains("inherit")),
                Is.True,
                "the rejection must be the CHECKER's clean diagnostic naming inheritance, "
                + "not a raw C++ failure: "
                + string.Join(" | ", result.Diagnostics.Select(d => d.Code + ": " + d.Message)));
            Assert.That(outcome.NetReferences!.Diagnostics.Where(d => d.Code == "BL6016"),
                Is.Empty,
                "…and no BL6016: Stream RESOLVES; the finding is about inheritance, not lookup");
        });
    }

    // ------------------------------------------------------------------------------------
    // The user-type shadow guard (P1 Task-10 rider, extended to ManagedOwned by the flip)
    // ------------------------------------------------------------------------------------

    /// <summary>
    /// Without this guard, `Class Regex` + `Dim r As Regex` would silently remap the
    /// user's type to BasicLang::NetRef post-flip (MapType keys off the registry BEFORE
    /// user types) — exactly the silent-miscompile shape the P1 flip's NativeOwned
    /// conflict diagnostic exists for.
    /// </summary>
    [Test]
    public void UserClassShadowingAManagedOwnedName_IsRejectedCleanly()
    {
        var parser = new Parser(new Lexer("""
            Class Regex
             Public X As Integer
            End Class

            Module M
             Sub Main()
              Dim r As New Regex()
              Console.WriteLine("done")
             End Sub
            End Module
            """).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty);

        var analyzer = new SemanticAnalyzer();
        Assert.That(analyzer.Analyze(ast), Is.True,
            "semantic errors:\n" + string.Join("\n", analyzer.Errors.Select(e => e.Message)));
        var module = new IRBuilder(analyzer).Build(ast, "TestModule");

        var ex = Assert.Throws<CppCapabilityException>(() =>
            new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false })
                .Generate(module));
        Assert.That(ex!.Message, Does.Contain("Regex").And.Contain("rename"),
            "a user type shadowing a ManagedOwned name needs the same rename-request "
            + "conflict diagnostic NativeOwned names get");
    }

    // ------------------------------------------------------------------------------------
    // §12.4 mechanical drift invariants
    // ------------------------------------------------------------------------------------

    private sealed class MapTypeExposer : CppCodeGenerator
    {
        public MapTypeExposer() : base(new CppCodeGenOptions { GenerateComments = false }) { }
        public string Map(TypeInfo t) => MapType(t);
    }

    /// <summary>
    /// §12.4: for every name in ManagedOwned, codegen's type mapping yields the handle
    /// representation (NetRef) — and no other registry name does. Scoped to registry names
    /// deliberately: arbitrary resolved .NET types are Unknown to the registry by design.
    /// </summary>
    [Test]
    public void EveryManagedOwnedName_MapsToNetRef_AndNoOtherRegistryNameDoes()
    {
        var gen = new MapTypeExposer();

        var managedOwned = BoundaryTypeRegistry.NamesInCategory(BoundaryTypeCategory.ManagedOwned);
        Assert.That(managedOwned, Is.Not.Empty,
            "guard: ManagedOwned is empty, so this invariant proves nothing — the flip's "
            + "registry move did not land");

        foreach (var name in managedOwned)
        {
            Assert.That(gen.Map(new TypeInfo(name, TypeKind.Class)),
                Is.EqualTo("BasicLang::NetRef"),
                $"ManagedOwned '{name}' must map to the NetRef handle (spec §12.4)");
        }

        foreach (var category in new[]
        {
            BoundaryTypeCategory.NativeOwned, BoundaryTypeCategory.Bridged,
            BoundaryTypeCategory.Rejected,
        })
        {
            foreach (var name in BoundaryTypeRegistry.NamesInCategory(category))
            {
                Assert.That(gen.Map(new TypeInfo(name, TypeKind.Class)),
                    Is.Not.EqualTo("BasicLang::NetRef"),
                    $"{category} '{name}' must NOT map to NetRef (spec §12.4's scoping)");
            }
        }
    }

    /// <summary>
    /// §12.4: <c>Categorize</c> checks ManagedOwned before Rejected, so an overlap would
    /// resolve silently — the two sets must be disjoint.
    /// </summary>
    [Test]
    public void ManagedOwnedAndRejected_AreDisjoint()
    {
        var managedOwned = BoundaryTypeRegistry.NamesInCategory(BoundaryTypeCategory.ManagedOwned);
        var rejected = BoundaryTypeRegistry.NamesInCategory(BoundaryTypeCategory.Rejected);

        Assert.That(managedOwned.Intersect(rejected, StringComparer.OrdinalIgnoreCase),
            Is.Empty,
            "ManagedOwned ∩ Rejected must be ∅ — Categorize checks ManagedOwned first, so "
            + "an overlapping name would silently resolve ManagedOwned and never reject");
    }

    /// <summary>
    /// The name-string mapping route (delegate PARAMETERS go through MapTypeName, not
    /// MapType) must compose too, or a `Delegate Sub F(r As Regex)` parameter emits a
    /// bare undefined C++ name.
    /// </summary>
    [Test]
    public void DelegateParameterOfAManagedOwnedType_ComposesToNetRef()
    {
        var cpp = CompileToCppWithResolver("""
            Delegate Sub Handler(r As Regex)

            Module M
             Sub Main()
              Console.WriteLine("done")
             End Sub
            End Module
            """);
        Assert.That(cpp, Does.Contain("BasicLang::NetRef"),
            "a ManagedOwned delegate parameter must map to the NetRef handle through the "
            + "MapTypeName route:\n" + cpp);
        Assert.That(cpp, Does.Not.Contain("(Regex)"),
            "the bare undefined C++ name must not leak into the delegate alias:\n" + cpp);
    }

    /// <summary>D-P7 composition: the always-emitted runtime carries NetRef, so a
    /// declaration-only program (no shim, empty surface) still emits it.</summary>
    [Test]
    public void NetRefRuntime_IsEmittedUnconditionally()
    {
        var cpp = CompileToCppWithResolver("""
            Module M
             Sub Main()
              Console.WriteLine("done")
             End Sub
            End Module
            """);
        Assert.That(cpp, Does.Contain("class NetRef"),
            "BasicLang::NetRef must live in the ALWAYS-emitted runtime (D-P7) — a "
            + "declaration-only program compiles with no shim, so a surface-gated "
            + "declaration would leave NetRef declarations dangling");
    }
}
