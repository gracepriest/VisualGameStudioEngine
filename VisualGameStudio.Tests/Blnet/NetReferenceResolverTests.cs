using BasicLang.Net;
using BasicLang.Compiler.ProjectSystem;   // NOT BasicLang.ProjectSystem — see ProjectFile.cs:8
using NUnit.Framework;

namespace VisualGameStudio.Tests.Blnet;

/// <summary>
/// Reference resolution for native projects. Before P2a-1 every reference element was parsed
/// into the model and then silently dropped (Program.cs:436 returned before restore), so a
/// typo'd HintPath produced no output at all. These tests pin that references now resolve and
/// that failures are BL6021 rather than silence. (BL6022 is reserved by spec §11.4 for
/// &lt;NetProxy&gt; naming an unknown type — a P2a-2 concern.)
/// </summary>
[TestFixture]
public class NetReferenceResolverTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "netref-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        for (var i = 0; i < 3; i++)
        {
            try { Directory.Delete(_dir, recursive: true); return; }
            catch { Thread.Sleep(200); }
        }
    }

    [Test]
    public void HintPath_ResolvesRelativeToTheProjectFile_NotTheOutputDirectory()
    {
        var libDir = Path.Combine(_dir, "lib");
        Directory.CreateDirectory(libDir);
        var dll = Path.Combine(libDir, "MyLib.dll");
        File.WriteAllBytes(dll, new byte[] { 0x4D, 0x5A });   // "MZ" — existence is all that is checked here

        var project = new ProjectFile { Backend = "cpp" };
        project.AssemblyReferences.Add(new AssemblyReference { Name = "MyLib", HintPath = "lib\\MyLib.dll" });

        var result = NetReferenceResolver.Resolve(project, Path.Combine(_dir, "App.blproj"));

        Assert.That(result.Diagnostics, Is.Empty);
        Assert.That(result.AssemblyPaths, Does.Contain(dll),
            "HintPath must resolve relative to the PROJECT FILE. Resolving against the output " +
            "directory is the pre-existing C# backend hazard recorded in spec §5.");
    }

    [Test]
    public void MissingHintPath_IsBL6021_NotSilence()
    {
        var project = new ProjectFile { Backend = "cpp" };
        project.AssemblyReferences.Add(new AssemblyReference { Name = "Ghost", HintPath = "lib\\Ghost.dll" });

        var result = NetReferenceResolver.Resolve(project, Path.Combine(_dir, "App.blproj"));

        Assert.That(result.Diagnostics.Select(d => d.Code), Does.Contain("BL6021"));
        Assert.That(result.Diagnostics.Single().Message, Does.Contain("Ghost"));
    }

    [Test]
    public void ProjectReference_IsABL6021_WARNING_WithTheDocumentedWorkaround()
    {
        var project = new ProjectFile { Backend = "cpp" };
        project.ProjectReferences.Add("..\\Sibling\\Sibling.blproj");

        var result = NetReferenceResolver.Resolve(project, Path.Combine(_dir, "App.blproj"));

        var diag = result.Diagnostics.Single();
        Assert.That(diag.Code, Is.EqualTo("BL6021"));
        Assert.That(diag.IsWarning, Is.True,
            "MUST be a warning in P2a-1. The IDE writes <ProjectReference> into native projects " +
            "itself — 'Add Project Reference' has NO backend filter " +
            "(SolutionExplorerViewModel.cs:625-627 -> :689). An error here breaks projects the " +
            "IDE creates and falsifies this plan's inertness claim. P2a-2 promotes it.");
        Assert.That(diag.Message, Does.Contain("HintPath"),
            "The message must name the <Reference>+<HintPath> workaround (spec §5, §14.9) — " +
            "cross-project compilation does not exist on any build path.");
    }

    [Test]
    public void NoReferences_ProducesNoDiagnosticsAndNoDeclaredAssemblies()
    {
        var project = new ProjectFile { Backend = "cpp" };

        var result = NetReferenceResolver.Resolve(project, Path.Combine(_dir, "App.blproj"));

        Assert.That(result.Diagnostics, Is.Empty);
        Assert.That(result.AssemblyPaths, Is.Empty,
            "AssemblyPaths holds only what the project DECLARED — a project with no references " +
            "must cost nothing, which is what keeps existing native projects unaffected.");
        Assert.That(result.FrameworkPaths, Is.Not.Empty,
            "FrameworkPaths is always populated and is SEPARATE from AssemblyPaths. Spec §6.5 " +
            "step 2 requires `Dim r As New Regex(\"a\")` to resolve with no <Reference> at all, " +
            "so the framework set cannot be conditional on the project declaring something.");
    }

    // ------------------------------------------------------------------
    // Properties Task 15's cache key depends on (order-stable + de-duplicated
    // by full path), and the framework/declared split the closure promises.
    // ------------------------------------------------------------------

    [Test]
    public void DuplicateReferences_AreDeDuplicatedByFullPath_AndOrderIsStable()
    {
        var libDir = Path.Combine(_dir, "lib");
        Directory.CreateDirectory(libDir);
        var a = Path.Combine(libDir, "A.dll");
        var b = Path.Combine(libDir, "B.dll");
        File.WriteAllBytes(a, new byte[] { 0x4D, 0x5A });
        File.WriteAllBytes(b, new byte[] { 0x4D, 0x5A });

        var project = new ProjectFile { Backend = "cpp" };
        project.AssemblyReferences.Add(new AssemblyReference { Name = "A", HintPath = "lib\\A.dll" });
        project.AssemblyReferences.Add(new AssemblyReference { Name = "B", HintPath = "lib\\B.dll" });
        // Same file, spelled differently — must collapse to one entry.
        project.AssemblyReferences.Add(new AssemblyReference { Name = "A", HintPath = "lib\\..\\lib\\A.dll" });

        var result = NetReferenceResolver.Resolve(project, Path.Combine(_dir, "App.blproj"));

        Assert.That(result.Diagnostics, Is.Empty);
        Assert.That(result.AssemblyPaths, Is.EqualTo(new[] { a, b }),
            "AssemblyPaths must be de-duplicated by FULL path and keep declaration order — " +
            "Task 15 hashes this list into the shim cache key, so an unstable order would " +
            "make every build a cache miss. Fix NetReferenceResolver, not the test.");
    }

    [Test]
    public void All_IsFrameworkThenDeclared_AndDeDuplicated()
    {
        var libDir = Path.Combine(_dir, "lib");
        Directory.CreateDirectory(libDir);
        var dll = Path.Combine(libDir, "MyLib.dll");
        File.WriteAllBytes(dll, new byte[] { 0x4D, 0x5A });

        var project = new ProjectFile { Backend = "cpp" };
        project.AssemblyReferences.Add(new AssemblyReference { Name = "MyLib", HintPath = "lib\\MyLib.dll" });

        var result = NetReferenceResolver.Resolve(project, Path.Combine(_dir, "App.blproj"));

        Assert.That(result.All.Count, Is.EqualTo(result.All.Distinct(StringComparer.OrdinalIgnoreCase).Count()),
            "All must be de-duplicated — it is what Roslyn sees, and duplicate metadata " +
            "references are a Roslyn error. Fix NetReferenceResolver.");
        Assert.That(result.All.Take(result.FrameworkPaths.Count), Is.EqualTo(result.FrameworkPaths));
        Assert.That(result.All, Does.Contain(dll));
    }

    [Test]
    public void ReferenceWithoutHintPath_ResolvesAgainstTheFrameworkSetBySimpleName()
    {
        var project = new ProjectFile { Backend = "cpp" };
        project.AssemblyReferences.Add(new AssemblyReference { Name = "System.Runtime" });

        var result = NetReferenceResolver.Resolve(project, Path.Combine(_dir, "App.blproj"));

        Assert.That(result.Diagnostics, Is.Empty,
            "A <Reference> with no <HintPath> naming a framework assembly must resolve against " +
            "the framework set (the compiler's own TRUSTED_PLATFORM_ASSEMBLIES), not fail.");
        Assert.That(result.AssemblyPaths.Single(),
            Does.EndWith("System.Runtime.dll").IgnoreCase);
    }

    [Test]
    public void ReferenceWithoutHintPath_NotInTheFrameworkSet_IsBL6021Error()
    {
        var project = new ProjectFile { Backend = "cpp" };
        project.AssemblyReferences.Add(new AssemblyReference { Name = "Contoso.NotAFrameworkAssembly" });

        var result = NetReferenceResolver.Resolve(project, Path.Combine(_dir, "App.blproj"));

        var diag = result.Diagnostics.Single();
        Assert.That(diag.Code, Is.EqualTo("BL6021"));
        Assert.That(diag.IsWarning, Is.False);
        Assert.That(diag.Message, Does.Contain("Contoso.NotAFrameworkAssembly").And.Contain("HintPath"));
    }

    [Test]
    public void UnrestorablePackage_IsBL6021_NotBL6022()
    {
        // BL6022 is reserved by spec §11.4 for <NetProxy> naming an unknown type. Package
        // restore failures are reference-resolution failures and share BL6021.
        var project = new ProjectFile { Backend = "cpp" };

        var result = NetReferenceResolver.Resolve(project, Path.Combine(_dir, "App.blproj"),
            packageAssemblies: null,
            packageErrors: new[] { "Error restoring Contoso.Ghost: 404" });

        var diag = result.Diagnostics.Single();
        Assert.That(diag.Code, Is.EqualTo("BL6021"));
        Assert.That(diag.IsWarning, Is.False);
        Assert.That(diag.Message, Does.Contain("Contoso.Ghost"));
        Assert.That(result.Diagnostics.Select(d => d.Code), Does.Not.Contain("BL6022"));
    }
}
