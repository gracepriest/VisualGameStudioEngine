using BasicLang.Compiler.ProjectSystem;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

[TestFixture]
public class CppProjectFileTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-cppproj-" + Guid.NewGuid().ToString("N"));
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

    private string WriteProject(string xml)
    {
        var path = Path.Combine(_dir, "Test.blproj");
        File.WriteAllText(path, xml);
        return path;
    }

    [Test]
    public void Load_ParsesLanguageCppStandardAndCppItems()
    {
        var path = WriteProject("""
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>Test</ProjectName>
                <OutputType>Exe</OutputType>
                <Language>Cpp</Language>
                <CppStandard>c++17</CppStandard>
                <TargetBackend>Cpp</TargetBackend>
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="main.cpp" />
                <IncludeDir Include="vendor/include" />
                <NativeLib Include="VisualGameStudioEngine.lib" />
                <Define Include="MY_FLAG" />
              </ItemGroup>
            </BasicLangProject>
            """);

        var project = ProjectFile.Load(path);

        Assert.That(project.Language, Is.EqualTo("Cpp"));
        Assert.That(project.IsCppProject, Is.True);
        Assert.That(project.CppStandard, Is.EqualTo("c++17"));
        Assert.That(project.IncludeDirs, Is.EqualTo(new[] { "vendor/include" }));
        Assert.That(project.NativeLibs, Is.EqualTo(new[] { "VisualGameStudioEngine.lib" }));
        Assert.That(project.Defines, Is.EqualTo(new[] { "MY_FLAG" }));
    }

    [Test]
    public void Load_DefaultsLanguageToBasicLang()
    {
        var path = WriteProject("""
            <BasicLangProject Version="1.0">
              <PropertyGroup><ProjectName>Test</ProjectName></PropertyGroup>
            </BasicLangProject>
            """);
        var project = ProjectFile.Load(path);
        Assert.That(project.Language, Is.EqualTo("BasicLang"));
        Assert.That(project.IsCppProject, Is.False);
        Assert.That(project.CppStandard, Is.EqualTo("c++20"), "default standard");
    }

    [Test]
    public void SaveThenLoad_RoundTripsLanguageAndCppItems()
    {
        var project = new ProjectFile
        {
            FilePath = Path.Combine(_dir, "RT.blproj"),
            ProjectName = "RT",
            Language = "Cpp",
            CppStandard = "c++20",
        };
        project.SourceFiles.Add("main.cpp");
        project.IncludeDirs.Add("inc");
        project.NativeLibs.Add("foo.lib");
        project.Defines.Add("A_DEFINE");

        project.Save();
        var reloaded = ProjectFile.Load(project.FilePath);

        Assert.That(reloaded.Language, Is.EqualTo("Cpp"));
        Assert.That(reloaded.CppStandard, Is.EqualTo("c++20"));
        Assert.That(reloaded.IncludeDirs, Is.EqualTo(new[] { "inc" }));
        Assert.That(reloaded.NativeLibs, Is.EqualTo(new[] { "foo.lib" }));
        Assert.That(reloaded.Defines, Is.EqualTo(new[] { "A_DEFINE" }));
    }

    [Test]
    public void Save_BasicLangProject_DoesNotEmitLanguageOrCppElements()
    {
        var project = new ProjectFile { FilePath = Path.Combine(_dir, "BL.blproj"), ProjectName = "BL" };
        project.Save();
        var text = File.ReadAllText(project.FilePath);
        Assert.That(text, Does.Not.Contain("<Language>"), "old-format files must stay untouched");
        Assert.That(text, Does.Not.Contain("<CppStandard>"));
    }

    [Test]
    public void Load_ReadsCppToolchain_Lowercased()
    {
        var path = WriteProject("""
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>Test</ProjectName>
                <Language>Cpp</Language>
                <CppToolchain>GCC</CppToolchain>
              </PropertyGroup>
            </BasicLangProject>
            """);

        var project = ProjectFile.Load(path);

        Assert.That(project.CppToolchain, Is.EqualTo("gcc"));
    }

    [Test]
    public void Load_NoCppToolchainElement_IsNull()
    {
        var path = WriteProject("""
            <BasicLangProject Version="1.0">
              <PropertyGroup><ProjectName>Test</ProjectName><Language>Cpp</Language></PropertyGroup>
            </BasicLangProject>
            """);

        var project = ProjectFile.Load(path);

        Assert.That(project.CppToolchain, Is.Null, "no element = machine probe");
    }

    [Test]
    public void Save_EmitsCppToolchain_WhenSet_EvenForMixedProjects()
    {
        // Mixed project: Language=BasicLang on the C++ backend still builds
        // natively, so its toolchain choice must survive a save.
        var project = new ProjectFile
        {
            FilePath = Path.Combine(_dir, "Mixed.blproj"),
            ProjectName = "Mixed",
            Backend = "Cpp",
            CppToolchain = "msvc",
        };
        project.Save();
        var text = File.ReadAllText(project.FilePath);
        Assert.That(text, Does.Contain("<CppToolchain>msvc</CppToolchain>"));
    }

    [Test]
    public void Save_OmitsCppToolchain_WhenNull()
    {
        var project = new ProjectFile
        {
            FilePath = Path.Combine(_dir, "NoTc.blproj"),
            ProjectName = "NoTc",
            Language = "Cpp",
        };
        project.Save();
        var text = File.ReadAllText(project.FilePath);
        Assert.That(text, Does.Not.Contain("<CppToolchain>"));
    }

    [Test]
    public void GetCppTranslationUnits_DefaultGlob_FindsTUsExcludingBinObjAndHeaders()
    {
        File.WriteAllText(Path.Combine(_dir, "main.cpp"), "// tu");
        File.WriteAllText(Path.Combine(_dir, "util.cc"), "// tu");
        File.WriteAllText(Path.Combine(_dir, "util.h"), "// header, not a TU");
        Directory.CreateDirectory(Path.Combine(_dir, "src"));
        File.WriteAllText(Path.Combine(_dir, "src", "extra.cpp"), "// tu");
        Directory.CreateDirectory(Path.Combine(_dir, "bin", "Debug"));
        File.WriteAllText(Path.Combine(_dir, "bin", "Debug", "generated.cpp"), "// must be excluded");
        Directory.CreateDirectory(Path.Combine(_dir, "obj"));
        File.WriteAllText(Path.Combine(_dir, "obj", "stale.cpp"), "// must be excluded");

        var path = WriteProject("""
            <BasicLangProject Version="1.0">
              <PropertyGroup><ProjectName>Test</ProjectName><Language>Cpp</Language></PropertyGroup>
            </BasicLangProject>
            """);
        var project = ProjectFile.Load(path);

        var tus = project.GetCppTranslationUnits().Select(Path.GetFileName).ToList();

        Assert.That(tus, Is.EquivalentTo(new[] { "main.cpp", "util.cc", "extra.cpp" }));
    }

    [Test]
    public void IsNativeProject_LanguageCpp_IsTrue()
    {
        var path = WriteProject("""
            <BasicLangProject Version="1.0">
              <PropertyGroup><ProjectName>Test</ProjectName><Language>Cpp</Language></PropertyGroup>
            </BasicLangProject>
            """);
        Assert.That(ProjectFile.Load(path).IsNativeProject, Is.True);
    }

    [Test]
    public void IsNativeProject_BackendCpp_NoLanguage_IsTrue()
    {
        // A BasicLang project (no <Language>) on the C++ backend still builds
        // natively — this is the Task-5 route the private predicate missed.
        var path = WriteProject("""
            <BasicLangProject Version="1.0">
              <PropertyGroup><ProjectName>Test</ProjectName><TargetBackend>Cpp</TargetBackend></PropertyGroup>
            </BasicLangProject>
            """);
        var project = ProjectFile.Load(path);
        Assert.That(project.IsCppProject, Is.False, "no <Language>Cpp> — not a C++-language project");
        Assert.That(project.IsNativeProject, Is.True);
    }

    [Test]
    public void IsNativeProject_BackendCPlusPlusSpelling_IsTrue()
    {
        var path = WriteProject("""
            <BasicLangProject Version="1.0">
              <PropertyGroup><ProjectName>Test</ProjectName><TargetBackend>C++</TargetBackend></PropertyGroup>
            </BasicLangProject>
            """);
        Assert.That(ProjectFile.Load(path).IsNativeProject, Is.True);
    }

    [TestCase("CSharp")]
    [TestCase("LLVM")]
    [TestCase("MSIL")]
    public void IsNativeProject_ManagedBackends_AreFalse(string backend)
    {
        var path = WriteProject($"""
            <BasicLangProject Version="1.0">
              <PropertyGroup><ProjectName>Test</ProjectName><TargetBackend>{backend}</TargetBackend></PropertyGroup>
            </BasicLangProject>
            """);
        Assert.That(ProjectFile.Load(path).IsNativeProject, Is.False);
    }

    [Test]
    public void IsNativeProject_DefaultProject_IsFalse()
    {
        // No <Language>, no <TargetBackend> — the default is a managed C# project.
        var path = WriteProject("""
            <BasicLangProject Version="1.0">
              <PropertyGroup><ProjectName>Test</ProjectName></PropertyGroup>
            </BasicLangProject>
            """);
        Assert.That(ProjectFile.Load(path).IsNativeProject, Is.False);
    }

    [Test]
    public void GetCppTranslationUnits_ExplicitCompileItems_FiltersToTuExtensions()
    {
        File.WriteAllText(Path.Combine(_dir, "main.cpp"), "// tu");
        File.WriteAllText(Path.Combine(_dir, "util.h"), "// header");
        var path = WriteProject("""
            <BasicLangProject Version="1.0">
              <PropertyGroup><ProjectName>Test</ProjectName><Language>Cpp</Language></PropertyGroup>
              <ItemGroup>
                <Compile Include="main.cpp" />
                <Compile Include="util.h" />
              </ItemGroup>
            </BasicLangProject>
            """);
        var project = ProjectFile.Load(path);
        var tus = project.GetCppTranslationUnits().Select(Path.GetFileName).ToList();
        Assert.That(tus, Is.EqualTo(new[] { "main.cpp" }), "headers ride along as Compile items but are not compiled");
    }
}
