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
    public void ProjectReference_IsABL6021_ERROR_WithTheDocumentedWorkaround()
    {
        var project = new ProjectFile { Backend = "cpp" };
        project.ProjectReferences.Add("..\\Sibling\\Sibling.blproj");

        var result = NetReferenceResolver.Resolve(project, Path.Combine(_dir, "App.blproj"));

        var diag = result.Diagnostics.Single();
        Assert.That(diag.Code, Is.EqualTo("BL6021"));
        // P2a-2 THE FLIP promoted this from the P2a-1 warning: a silently-ignored
        // reference element is a lie about the build, and the error names the fix.
        Assert.That(diag.IsWarning, Is.False,
            "An ERROR since the P2a-2 flip (plan Task 5 item 4). The P2a-1 IDE concern " +
            "('Add Project Reference' has no backend filter) is resolved BY reporting — " +
            "the message tells the user exactly what to do instead.");
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

    // ------------------------------------------------------------------
    // THE framework-set drift invariant.
    // ------------------------------------------------------------------

    [Test]
    public void FrameworkPaths_ContainOnlyAssembliesFromTheSharedFrameworkDirectory()
    {
        var project = new ProjectFile { Backend = "cpp" };

        var result = NetReferenceResolver.Resolve(project, Path.Combine(_dir, "App.blproj"));

        var frameworkDir = NetReferenceResolver.FrameworkDirectoryForTests;
        Assert.That(frameworkDir, Is.Not.Null.And.Not.Empty,
            "the shared-framework directory must be discoverable — we are executing on that runtime");

        var strays = result.FrameworkPaths
            .Where(p => !string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(p)!),
                frameworkDir, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.That(strays, Is.Empty,
            "FrameworkPaths must be the SHARED FRAMEWORK, never the host process's whole "
            + "TRUSTED_PLATFORM_ASSEMBLIES list. BasicLang.dll runs in two hosts: BasicLang.exe "
            + "(CLI) and VisualGameStudio.exe (the IDE — the only native build path via "
            + "BuildService, and the only IntelliSense path via IntelliSenseEmissionService). The "
            + "IDE ships ~40 non-framework managed DLLs beside itself (Avalonia.*, AvaloniaEdit, "
            + "Dock.*, CommunityToolkit.Mvvm, Newtonsoft.Json, SkiaSharp), all of which are in ITS "
            + "TPA. Widening this filter makes `<Reference Include=\"Avalonia\" />` with no "
            + "HintPath resolve in the IDE and fail BL6021 in the CLI for a byte-identical "
            + ".blproj, and makes Task 15's cache key host-dependent. DO NOT 'fix' a failure here "
            + "by relaxing the filter — fix whatever put a non-framework path in the list.\n"
            + "Framework dir: " + frameworkDir + "\nStrays:\n" + string.Join("\n", strays));

        // Non-vacuousness. The invariant above is satisfied trivially by an empty list, and would
        // also be satisfied by a host that happens to load nothing extra. This test host loads
        // plenty — NUnit, Avalonia, AvaloniaEdit, … exactly as VisualGameStudio.exe does — so the
        // filter MUST be removing entries. This is the assertion that fails if the fix is reverted.
        var tpaDllCount = ((AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string) ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Count(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
        Assert.That(result.FrameworkPaths.Count, Is.LessThan(tpaDllCount),
            "FrameworkPaths must be a STRICT subset of this host's TPA .dll entries. Equal counts "
            + "mean the framework set IS the raw host TPA list — i.e. the shared-framework "
            + "directory filter has been removed and every host dependency is now visible to "
            + "BasicLang programs, in whichever host happened to start the build.");

        // Asserting the absence of specific names (e.g. "Avalonia") is deliberately NOT how this is
        // expressed: it would pin an accident of THIS host's dependency list rather than the rule.
        // The directory invariant plus the strict-subset count is the rule; the check below guards
        // the opposite failure, over-filtering.
        Assert.That(result.FrameworkPaths.Select(Path.GetFileNameWithoutExtension),
            Does.Contain("System.Runtime").And.Contain("System.Console")
                .And.Contain("System.Text.RegularExpressions"),
            "the filter must not have emptied or gutted the framework set — spec §6.5 needs "
            + "Regex/Console to resolve with no <Reference> at all.");
    }

    [Test]
    public void ReferenceWithAStrongDisplayNameInInclude_StillResolvesAgainstTheFrameworkSet()
    {
        // Legal MSBuild, and exactly what someone pastes out of a csproj. Keyed on the file's
        // base name it could never match, so the user would be told to add a <HintPath> for an
        // assembly already sitting in the framework set.
        var project = new ProjectFile { Backend = "cpp" };
        project.AssemblyReferences.Add(new AssemblyReference
        {
            Name = "System.Runtime, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a"
        });

        var result = NetReferenceResolver.Resolve(project, Path.Combine(_dir, "App.blproj"));

        Assert.That(result.Diagnostics, Is.Empty,
            "a strong display name must be reduced to its simple name before the framework "
            + "lookup. Fix NetReferenceResolver.SimpleAssemblyName.");
        Assert.That(result.AssemblyPaths.Single(), Does.EndWith("System.Runtime.dll").IgnoreCase);
    }

    // ------------------------------------------------------------------
    // The <PackageReference> half of the closure. These pin CppProjectBuilder's
    // inertness guards, which nothing but a doc comment defended before.
    // ------------------------------------------------------------------

    private ProjectFile PackagelessProject() =>
        new ProjectFile { Backend = "cpp", FilePath = Path.Combine(_dir, "App.blproj") };

    [Test]
    public void RestorePackagesForClosure_WithNoPackages_DoesNotRestore_EvenOnTheBuildPath()
    {
        // THE inertness guard. If a refactor ever hoists `new PackageManager()` or the restore
        // call above the Count == 0 check, every existing native project starts writing
        // obj/project.assets.json and printing "Restoring packages for X..." — and without this
        // test the whole suite stays green while it happens.
        var project = PackagelessProject();

        var (assemblies, errors) =
            CppProjectBuilder.RestorePackagesForClosure(project, "Release", forIntelliSense: false);

        Assert.That(assemblies, Is.Empty);
        Assert.That(errors, Is.Empty);
        Assert.That(Directory.Exists(Path.Combine(_dir, "obj")), Is.False,
            "a package-free project must not cause PackageManager.RestoreAsync to run at all — it "
            + "creates obj/ and writes project.assets.json unconditionally. The Count == 0 guard "
            + "in CppProjectBuilder.RestorePackagesForClosure is what prevents that; restore it.");
    }

    [Test]
    public void RestorePackagesForClosure_OnTheIntelliSensePath_SkipsFloatingVersions_AndNeverRestores()
    {
        // A floating version cannot be pinned without the network, and opening a project must
        // never wait on nuget.org. Machine-independent: nothing here can reach the network.
        var project = PackagelessProject();
        project.PackageReferences.Add(new PackageReference { Name = "Contoso.DoesNotExist", Version = "*" });

        var (assemblies, errors) =
            CppProjectBuilder.RestorePackagesForClosure(project, "Release", forIntelliSense: true);

        Assert.That(assemblies, Is.Empty);
        Assert.That(errors, Is.Empty,
            "the IntelliSense path reports nothing — the next build restores and reports for real. "
            + "An error here would light up the Output panel on every project open.");
        Assert.That(Directory.Exists(Path.Combine(_dir, "obj")), Is.False,
            "the IntelliSense branch must be cache-only: no restore, no assets file.");
    }

    [Test]
    public void RestorePackagesForClosure_OnTheIntelliSensePath_ConcreteVersionCacheMiss_IsSilent()
    {
        // The branch that actually calls GetPackageAssemblies. A package that has never been
        // restored contributes nothing and reports nothing — still no network, still no writes.
        var project = PackagelessProject();
        project.PackageReferences.Add(new PackageReference
        {
            Name = "Contoso.DoesNotExist",
            Version = "9.9.9-netreferenceresolvertests"
        });

        var (assemblies, errors) =
            CppProjectBuilder.RestorePackagesForClosure(project, "Release", forIntelliSense: true);

        Assert.That(assemblies, Is.Empty);
        Assert.That(errors, Is.Empty);
        Assert.That(Directory.Exists(Path.Combine(_dir, "obj")), Is.False);
    }
}
