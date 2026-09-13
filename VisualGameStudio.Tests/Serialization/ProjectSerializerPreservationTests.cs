using NUnit.Framework;
using VisualGameStudio.Core.Models;
using VisualGameStudio.ProjectSystem.Serialization;

namespace VisualGameStudio.Tests.Serialization;

/// <summary>
/// Task 1: an IDE project save must not destroy what the serializer does not model.
///
/// <para>Before this fixture, <c>SaveAsync</c> built a brand-new <c>&lt;BasicLangProject&gt;</c>
/// document from the model and emitted seven property elements. Everything else in the file —
/// <c>&lt;TargetFramework&gt;</c>, <c>&lt;UseWindowsForms&gt;</c>, the <c>Sdk</c> attribute, both
/// <c>&lt;Import&gt;</c>s, <c>&lt;ProjectCapability&gt;</c>, item metadata, every comment — was
/// silently dropped on the first save. The designer's core gesture is *add a file*, which commits
/// a project save on the next Build/F5, so this fires constantly.</para>
///
/// <para>The fix preserves unknown elements rather than enumerating the lost ones: adding the three
/// properties we know about today would leave the defect in place for the fourth.</para>
/// </summary>
[TestFixture]
public class ProjectSerializerPreservationTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-ideser-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown() { try { Directory.Delete(_dir, true); } catch { } }

    private string Write(string name, string xml)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, xml);
        return path;
    }

    // ------------------------------------------------------------------
    // (a) An IDE-shaped project carrying properties the model does not know
    // ------------------------------------------------------------------

    [Test]
    public async Task Save_PreservesPropertiesTheModelDoesNotKnow()
    {
        var path = Write("App.blproj", """
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>App</ProjectName>
                <OutputType>WinExe</OutputType>
                <RootNamespace>App</RootNamespace>
                <TargetBackend>CSharp</TargetBackend>
                <TargetFramework>net8.0-windows</TargetFramework>
                <UseWindowsForms>true</UseWindowsForms>
                <AssemblyName>App</AssemblyName>
                <Authors>Grace</Authors>
                <NetProxy>true</NetProxy>
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="Program.bas">
                  <SubType>Form</SubType>
                  <DependentUpon>MainForm.blform</DependentUpon>
                </Compile>
              </ItemGroup>
            </BasicLangProject>
            """);

        var before = File.ReadAllText(path);
        var serializer = new ProjectSerializer();
        var project = await serializer.LoadAsync(path);

        await serializer.SaveAsync(project);

        var after = File.ReadAllText(path);
        Assert.That(after, Is.EqualTo(before),
            "an IDE save that changes nothing must leave the project file byte-identical — " +
            "SaveAsync used to rebuild the document from the model and destroy every element it " +
            "does not parse (<TargetFramework>, <UseWindowsForms>, <Authors>, <NetProxy>, item metadata)");
    }

    [Test]
    public async Task Save_PreservesCompileItemMetadata()
    {
        var path = Write("Meta.blproj", """
            <BasicLangProject Version="1.0">
              <PropertyGroup><ProjectName>Meta</ProjectName></PropertyGroup>
              <ItemGroup>
                <Compile Include="MainForm.bas">
                  <DependentUpon>MainForm.blform</DependentUpon>
                </Compile>
              </ItemGroup>
            </BasicLangProject>
            """);

        var serializer = new ProjectSerializer();
        var project = await serializer.LoadAsync(path);
        await serializer.SaveAsync(project);

        Assert.That(File.ReadAllText(path), Does.Contain("<DependentUpon>MainForm.blform</DependentUpon>"),
            "item metadata must survive an IDE save — the designer pairs a .bas with its form document " +
            "through exactly this element, so losing it unparents every designed form");
    }

    // ------------------------------------------------------------------
    // (b) A VS 2022 / VSIX-shaped project
    // ------------------------------------------------------------------

    /// <summary>
    /// Byte-for-byte the shape the VSIX ships at
    /// <c>BasicLang.VisualStudio/src/BasicLang.VisualStudio/Templates/Projects/WinFormsApp/Project.blproj</c>.
    /// Copied rather than read from disk so the test states its own contract and does not silently
    /// change meaning when the template is edited.
    /// </summary>
    private const string VsixShapedProject = """
        <Project Sdk="Microsoft.NET.Sdk">

          <PropertyGroup>
            <!-- BasicLang Project Type -->
            <ProjectTypeGuids>{95a8f3e1-1234-4567-8903-abcdef123456}</ProjectTypeGuids>
            <IsBasicLangProject>true</IsBasicLangProject>

            <!-- Output settings -->
            <OutputType>WinExe</OutputType>
            <TargetFramework>net8.0-windows</TargetFramework>
            <UseWindowsForms>true</UseWindowsForms>

            <!-- BasicLang compiler settings -->
            <BasicLangBackend>CSharp</BasicLangBackend>
            <RootNamespace>WinFormsApp1</RootNamespace>
            <AssemblyName>WinFormsApp1</AssemblyName>
          </PropertyGroup>

          <!-- BasicLang project capability (activates CPS components) -->
          <ItemGroup>
            <ProjectCapability Include="BasicLang" />
          </ItemGroup>

          <!-- Import BasicLang build targets -->
          <Import Project="$(BasicLangTargetsPath)" Condition="'$(BasicLangTargetsPath)' != '' And Exists('$(BasicLangTargetsPath)')" />
          <Import Project="$(MSBuildExtensionsPath)\BasicLang\BasicLang.targets" Condition="Exists('$(MSBuildExtensionsPath)\BasicLang\BasicLang.targets')" />

          <!-- BasicLang source files -->
          <ItemGroup>
            <BasicLangCompile Include="**\*.bas" />
            <BasicLangCompile Include="**\*.bl" />
          </ItemGroup>

        </Project>
        """;

    [Test]
    public async Task Save_PreservesVsixShapedProject_ByteForByte()
    {
        var path = Write("Project.blproj", VsixShapedProject);

        var before = File.ReadAllText(path);
        var serializer = new ProjectSerializer();
        var project = await serializer.LoadAsync(path);

        await serializer.SaveAsync(project);

        Assert.That(File.ReadAllText(path), Is.EqualTo(before),
            "a VS 2022-created .blproj must survive an IDE save untouched — SaveAsync used to rewrite " +
            "the root as <BasicLangProject>, dropping the Sdk attribute, both <Import>s, " +
            "<ProjectCapability>, <ProjectTypeGuids> and the <BasicLangCompile> globs, which is what " +
            "made VS 2022 refuse to reload the project");
    }

    [Test]
    public async Task Save_KeepsTheRootElementAndSdkAttribute()
    {
        var path = Write("Project.blproj", VsixShapedProject);
        var serializer = new ProjectSerializer();

        await serializer.SaveAsync(await serializer.LoadAsync(path));

        var after = File.ReadAllText(path);
        Assert.Multiple(() =>
        {
            Assert.That(after, Does.Contain("""<Project Sdk="Microsoft.NET.Sdk">"""),
                "the root element and its Sdk attribute are what make the file loadable by VS 2022");
            Assert.That(after, Does.Not.Contain("<BasicLangProject"),
                "an IDE save must not rename the root element of an SDK-style project");
            Assert.That(after, Does.Contain("<UseWindowsForms>true</UseWindowsForms>"));
            Assert.That(after, Does.Contain("<TargetFramework>net8.0-windows</TargetFramework>"));
            Assert.That(after, Does.Contain("""<ProjectCapability Include="BasicLang" />"""));
            Assert.That(after, Does.Contain("""<BasicLangCompile Include="**\*.bas" />"""));
            Assert.That(after, Does.Contain("$(BasicLangTargetsPath)"), "both <Import>s must survive");
            Assert.That(after, Does.Contain("<!-- Output settings -->"), "comments must survive");
        });
    }

    // ------------------------------------------------------------------
    // Preserving is not the same as never writing
    // ------------------------------------------------------------------

    [Test]
    public async Task Save_StillPersistsAChangedModelValue()
    {
        var path = Write("Change.blproj", """
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>Change</ProjectName>
                <TargetBackend>CSharp</TargetBackend>
                <Authors>Grace</Authors>
              </PropertyGroup>
            </BasicLangProject>
            """);

        var serializer = new ProjectSerializer();
        var project = await serializer.LoadAsync(path);
        project.TargetBackend = TargetBackend.JavaScript;

        await serializer.SaveAsync(project);

        var after = File.ReadAllText(path);
        Assert.Multiple(() =>
        {
            Assert.That(after, Does.Contain("<TargetBackend>JavaScript</TargetBackend>"),
                "a property the user actually changed must be written through");
            Assert.That(after, Does.Not.Contain("<TargetBackend>CSharp</TargetBackend>"),
                "the old value must be replaced, not duplicated");
            Assert.That(after, Does.Contain("<Authors>Grace</Authors>"),
                "preserving unknown elements must survive a real edit, not only a no-op save");
        });

        var reloaded = await serializer.LoadAsync(path);
        Assert.That(reloaded.TargetBackend, Is.EqualTo(TargetBackend.JavaScript));
    }

    [Test]
    public async Task Save_WritesANewPropertyIntoAFileThatLacksIt()
    {
        var path = Write("AddProp.blproj", """
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>AddProp</ProjectName>
                <Authors>Grace</Authors>
              </PropertyGroup>
            </BasicLangProject>
            """);

        var serializer = new ProjectSerializer();
        var project = await serializer.LoadAsync(path);
        project.TargetFramework = "net8.0-windows";
        project.UseWindowsForms = true;

        await serializer.SaveAsync(project);

        var after = File.ReadAllText(path);
        Assert.Multiple(() =>
        {
            Assert.That(after, Does.Contain("<TargetFramework>net8.0-windows</TargetFramework>"),
                "a newly-set property must be added to a file that did not carry it — this is how the " +
                "WinForms designer marks a project as targeting Windows Forms");
            Assert.That(after, Does.Contain("<UseWindowsForms>true</UseWindowsForms>"));
            Assert.That(after, Does.Contain("<Authors>Grace</Authors>"));
        });

        var reloaded = await serializer.LoadAsync(path);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.TargetFramework, Is.EqualTo("net8.0-windows"));
            Assert.That(reloaded.UseWindowsForms, Is.True);
        });
    }

    // ------------------------------------------------------------------
    // Item reconciliation — the designer's core gesture
    // ------------------------------------------------------------------

    [Test]
    public async Task Save_AddsANewCompileItem_WithoutDisturbingTheRest()
    {
        var path = Write("AddItem.blproj", """
            <BasicLangProject Version="1.0">
              <PropertyGroup><ProjectName>AddItem</ProjectName></PropertyGroup>
              <ItemGroup>
                <Compile Include="Program.bas" />
              </ItemGroup>
            </BasicLangProject>
            """);

        var serializer = new ProjectSerializer();
        var project = await serializer.LoadAsync(path);
        project.Items.Add(new ProjectItem("MainForm.bas", ProjectItemType.Compile));

        await serializer.SaveAsync(project);

        var reloaded = await serializer.LoadAsync(path);
        Assert.That(
            reloaded.Items.Where(i => i.ItemType == ProjectItemType.Compile).Select(i => i.Include),
            Is.EquivalentTo(new[] { "Program.bas", "MainForm.bas" }),
            "adding a file is the designer's core gesture — it must land in the project file");
    }

    [Test]
    public async Task Save_RemovesADeletedCompileItem()
    {
        var path = Write("DelItem.blproj", """
            <BasicLangProject Version="1.0">
              <PropertyGroup><ProjectName>DelItem</ProjectName></PropertyGroup>
              <ItemGroup>
                <Compile Include="Program.bas" />
                <Compile Include="Gone.bas" />
              </ItemGroup>
            </BasicLangProject>
            """);

        var serializer = new ProjectSerializer();
        var project = await serializer.LoadAsync(path);
        project.Items.RemoveAll(i => i.Include == "Gone.bas");

        await serializer.SaveAsync(project);

        var after = File.ReadAllText(path);
        Assert.Multiple(() =>
        {
            Assert.That(after, Does.Not.Contain("Gone.bas"), "a removed item must leave the project file");
            Assert.That(after, Does.Contain("Program.bas"));
        });
    }

    [Test]
    public async Task Save_DoesNotTouchItemKindsItDoesNotModel()
    {
        var path = Write("Unknown.blproj", """
            <BasicLangProject Version="1.0">
              <PropertyGroup><ProjectName>Unknown</ProjectName></PropertyGroup>
              <ItemGroup>
                <Compile Include="Program.bas" />
                <EmbeddedResource Include="icon.ico" />
                <None Include="app.config" />
              </ItemGroup>
            </BasicLangProject>
            """);

        var serializer = new ProjectSerializer();
        var project = await serializer.LoadAsync(path);
        project.Items.Add(new ProjectItem("Added.bas", ProjectItemType.Compile));

        await serializer.SaveAsync(project);

        var after = File.ReadAllText(path);
        Assert.Multiple(() =>
        {
            Assert.That(after, Does.Contain("""<EmbeddedResource Include="icon.ico" />"""),
                "an item kind the serializer does not model must not be removed by reconciliation");
            Assert.That(after, Does.Contain("""<None Include="app.config" />"""));
            Assert.That(after, Does.Contain("Added.bas"));
        });
    }

    // ------------------------------------------------------------------
    // The create-from-model path must keep working
    // ------------------------------------------------------------------

    [Test]
    public async Task Save_CreatesTheFileFromTheModel_WhenItDoesNotExist()
    {
        var path = Path.Combine(_dir, "Fresh.blproj");
        var project = new BasicLangProject
        {
            FilePath = path,
            Name = "Fresh",
            RootNamespace = "Fresh",
            TargetBackend = TargetBackend.JavaScript
        };
        project.Items.Add(new ProjectItem("Program.bas", ProjectItemType.Compile));

        await new ProjectSerializer().SaveAsync(project);

        Assert.That(File.Exists(path), Is.True, "a project that is not on disk yet must still be created");
        var reloaded = await new ProjectSerializer().LoadAsync(path);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Name, Is.EqualTo("Fresh"));
            Assert.That(reloaded.TargetBackend, Is.EqualTo(TargetBackend.JavaScript));
            Assert.That(reloaded.Items.Select(i => i.Include), Is.EquivalentTo(new[] { "Program.bas" }));
        });
    }

    // ------------------------------------------------------------------
    // The IDE loads with a configured default backend; the save must not read that as an edit
    // ------------------------------------------------------------------

    private const string ProjectWithoutBackend = """
        <BasicLangProject Version="1.0">
          <PropertyGroup>
            <ProjectName>NoBackend</ProjectName>
            <Authors>Grace</Authors>
          </PropertyGroup>
        </BasicLangProject>
        """;

    [Test]
    public async Task Save_DoesNotInjectTargetBackend_WhenTheFileOmitsItAndTheIdeDefaultDiffers()
    {
        // ProjectService loads with its basiclang.compiler.backend setting as
        // defaultBackendWhenOmitted, so a file with no <TargetBackend> comes back carrying that
        // value. Comparing against a freshly default-seeded re-parse would read that as a user edit
        // and write <TargetBackend>Cpp</TargetBackend> into a project nobody touched — on the VSIX
        // shape, which uses <BasicLangBackend>, that means every single IDE save.
        var path = Write("NoBackend.blproj", ProjectWithoutBackend);
        var before = File.ReadAllText(path);

        var serializer = new ProjectSerializer();
        var project = await serializer.LoadAsync(path, TargetBackend.Cpp);
        await serializer.SaveAsync(project);

        Assert.That(File.ReadAllText(path), Is.EqualTo(before),
            "the IDE's configured default backend is not a user edit and must not be written into " +
            "a file that deliberately omits the element");
    }

    [Test]
    public async Task Save_PersistsAChangedBackend_EvenWhenTheFileOmittedTheElement()
    {
        // The other half of the same rule: suppressing the seeded default must not also suppress a
        // real change the user made on top of it.
        var path = Write("NoBackend.blproj", ProjectWithoutBackend);

        var serializer = new ProjectSerializer();
        var project = await serializer.LoadAsync(path, TargetBackend.Cpp);
        project.TargetBackend = TargetBackend.JavaScript;
        await serializer.SaveAsync(project);

        Assert.That(File.ReadAllText(path), Does.Contain("<TargetBackend>JavaScript</TargetBackend>"));
        var reloaded = await serializer.LoadAsync(path, TargetBackend.Cpp);
        Assert.That(reloaded.TargetBackend, Is.EqualTo(TargetBackend.JavaScript));
    }

    // ------------------------------------------------------------------
    // The written file's prologue and line endings
    // ------------------------------------------------------------------

    [Test]
    public async Task Save_DoesNotPrependAnXmlDeclaration_ToAFileThatHadNone()
    {
        // XDocument.Save(TextWriter, SaveOptions) calls WriteStartDocument() unconditionally and
        // leaves OmitXmlDeclaration false, so it prepends <?xml …?> even to a document that never
        // had one — and with formatting disabled, with no newline after it. On the VSIX shape that
        // produces `<?xml…?><Project Sdk="Microsoft.NET.Sdk">` on one line.
        var path = Write("NoDecl.blproj", VsixShapedProject);

        var serializer = new ProjectSerializer();
        var project = await serializer.LoadAsync(path);
        project.RootNamespace = "Renamed";          // force a real write
        await serializer.SaveAsync(project);

        var after = File.ReadAllText(path);
        Assert.Multiple(() =>
        {
            Assert.That(after, Does.Not.Contain("<?xml"),
                "a file with no XML declaration must not gain one on save");
            Assert.That(after, Does.StartWith("""<Project Sdk="Microsoft.NET.Sdk">"""));
        });
    }

    [Test]
    public async Task Save_KeepsAnExistingXmlDeclaration_OnItsOwnLine()
    {
        var path = Write("Decl.blproj",
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
            "<BasicLangProject Version=\"1.0\">\n" +
            "  <PropertyGroup>\n" +
            "    <ProjectName>Decl</ProjectName>\n" +
            "  </PropertyGroup>\n" +
            "</BasicLangProject>");

        var serializer = new ProjectSerializer();
        var project = await serializer.LoadAsync(path);
        project.RootNamespace = "Renamed";
        await serializer.SaveAsync(project);

        var after = File.ReadAllText(path);
        Assert.Multiple(() =>
        {
            Assert.That(after, Does.StartWith("<?xml"));
            Assert.That(after, Does.Contain("?>\n<BasicLangProject"),
                "the declaration must stay on its own line, not be jammed against the root");
        });
    }

    [Test]
    public async Task Save_DoesNotRewriteEveryLineEnding()
    {
        // The XML parser is required to normalise CRLF to LF, and XmlWriter re-expands LF using
        // NewLineChars, which defaults to CRLF. Left alone, editing one property flips every line
        // ending in the file and turns a one-element change into a whole-file diff.
        var lf = "<BasicLangProject Version=\"1.0\">\n  <PropertyGroup>\n    <ProjectName>Lf</ProjectName>\n  </PropertyGroup>\n</BasicLangProject>";
        var path = Write("Lf.blproj", lf);

        var serializer = new ProjectSerializer();
        var project = await serializer.LoadAsync(path);
        project.RootNamespace = "Renamed";
        await serializer.SaveAsync(project);

        var after = File.ReadAllText(path);
        Assert.That(after, Does.Not.Contain("\r\n"),
            "an LF project file must stay LF — rewriting every line ending makes a one-property " +
            "edit look like a whole-file rewrite in source control");
    }

    [Test]
    public async Task Save_KeepsCrLfLineEndings_WhenThatIsWhatTheFileUses()
    {
        var crlf = "<BasicLangProject Version=\"1.0\">\r\n  <PropertyGroup>\r\n    <ProjectName>Crlf</ProjectName>\r\n  </PropertyGroup>\r\n</BasicLangProject>";
        var path = Write("Crlf.blproj", crlf);

        var serializer = new ProjectSerializer();
        var project = await serializer.LoadAsync(path);
        project.RootNamespace = "Renamed";
        await serializer.SaveAsync(project);

        var after = File.ReadAllText(path);
        Assert.Multiple(() =>
        {
            Assert.That(after, Does.Contain("\r\n"));
            Assert.That(after.Replace("\r\n", ""), Does.Not.Contain("\n"),
                "no line may be left with a bare LF in a CRLF file");
        });
    }

    // ------------------------------------------------------------------
    // Duplicates, defaults, and files we cannot understand
    // ------------------------------------------------------------------

    [Test]
    public async Task Save_WritesToTheOccurrenceTheLoaderActuallyReads()
    {
        // LoadAsync's PropertyGroup loop assigns unconditionally in document order, so a later
        // duplicate wins. Writing to the FIRST occurrence would leave the file still resolving to
        // the old value: the model and the file would disagree after a save, and every subsequent
        // save would see a difference and rewrite the file again without ever converging.
        var path = Write("Dupe.blproj", """
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>Dupe</ProjectName>
                <RootNamespace>First</RootNamespace>
              </PropertyGroup>
              <PropertyGroup>
                <RootNamespace>Second</RootNamespace>
              </PropertyGroup>
            </BasicLangProject>
            """);

        var serializer = new ProjectSerializer();
        var project = await serializer.LoadAsync(path);
        Assert.That(project.RootNamespace, Is.EqualTo("Second"), "sanity: the loader takes the last");

        project.RootNamespace = "Third";
        await serializer.SaveAsync(project);

        var reloaded = await serializer.LoadAsync(path);
        Assert.That(reloaded.RootNamespace, Is.EqualTo("Third"),
            "the value the loader reads back must be the value that was written");

        // ...and a second save must be a no-op, proving the write converged.
        var afterFirstSave = File.ReadAllText(path);
        await serializer.SaveAsync(await serializer.LoadAsync(path));
        Assert.That(File.ReadAllText(path), Is.EqualTo(afterFirstSave),
            "a save that does not converge rewrites the file on every Build/F5 forever");
    }

    [Test]
    public async Task Save_DoesNotMaterializeADefault_IntoAConfigurationGroupThatOmittedIt()
    {
        // LoadAsync reads a missing <DebugSymbols> as false. Writing all four children whenever any
        // one differs would materialise <DebugSymbols>false</DebugSymbols> into a Debug group that
        // was relying on the toolchain default — silently turning debug symbols off because the
        // user edited DefineConstants.
        var path = Write("Cfg.blproj", """
            <BasicLangProject Version="1.0">
              <PropertyGroup><ProjectName>Cfg</ProjectName></PropertyGroup>
              <PropertyGroup Condition="'$(Configuration)' == 'Debug'">
                <DefineConstants>DEBUG;TRACE</DefineConstants>
              </PropertyGroup>
            </BasicLangProject>
            """);

        var serializer = new ProjectSerializer();
        var project = await serializer.LoadAsync(path);
        project.Configurations["Debug"].DefineConstants = "DEBUG;TRACE;EXTRA";
        await serializer.SaveAsync(project);

        var after = File.ReadAllText(path);
        Assert.Multiple(() =>
        {
            Assert.That(after, Does.Contain("DEBUG;TRACE;EXTRA"), "the edit must land");
            Assert.That(after, Does.Not.Contain("<DebugSymbols>"),
                "an element the group deliberately omitted must not be materialised by an unrelated edit");
            Assert.That(after, Does.Not.Contain("<Optimize>"));
        });
    }

    [Test]
    public async Task Save_LeavesConfigurationsAlone_WhenAConditionIsOneWeCannotParse()
    {
        // VS writes '$(Configuration)|$(Platform)' == 'Debug|AnyCPU', which ExtractConfigurationName
        // does not match — so LoadAsync never read that group. Adding our own
        // '$(Configuration)' == 'Debug' group beside it would give the project two Debug groups
        // that disagree.
        var path = Write("VsCfg.blproj", """
            <BasicLangProject Version="1.0">
              <PropertyGroup><ProjectName>VsCfg</ProjectName></PropertyGroup>
              <PropertyGroup Condition="'$(Configuration)|$(Platform)' == 'Debug|AnyCPU'">
                <Optimize>false</Optimize>
              </PropertyGroup>
            </BasicLangProject>
            """);

        var serializer = new ProjectSerializer();
        var project = await serializer.LoadAsync(path);
        project.RootNamespace = "Renamed";      // force a write for an unrelated reason
        await serializer.SaveAsync(project);

        var after = File.ReadAllText(path);
        Assert.Multiple(() =>
        {
            Assert.That(after, Does.Contain("Renamed"), "the unrelated edit must still land");
            Assert.That(after, Does.Not.Contain("'$(Configuration)' == 'Debug'"),
                "a second, disagreeing Debug group must not be appended beside the VS-style one");
            Assert.That(System.Text.RegularExpressions.Regex.Matches(after, "Debug").Count, Is.EqualTo(1),
                "exactly one Debug configuration group must remain");
        });
    }

    [Test]
    public void Save_RefusesToOverwriteAFileItCannotParse()
    {
        // The original defect, reachable only from a damaged file — i.e. exactly when the user can
        // least afford it. Refusing is recoverable; replacing the file with a skeleton is not.
        var path = Write("Broken.blproj", "<BasicLangProject><PropertyGroup><Name>oops & broken");
        var before = File.ReadAllText(path);

        var project = new BasicLangProject { FilePath = path, Name = "Broken" };

        Assert.ThrowsAsync<System.Xml.XmlException>(() => new ProjectSerializer().SaveAsync(project));
        Assert.That(File.ReadAllText(path), Is.EqualTo(before),
            "a malformed project file must be left exactly as it is, never rebuilt from the model");
    }

    [Test]
    public void Save_RefusesToOverwriteAFileWithAnUnexpectedRoot()
    {
        var path = Write("Alien.blproj", "<SomethingElse><Data>keep me</Data></SomethingElse>");
        var before = File.ReadAllText(path);

        var project = new BasicLangProject { FilePath = path, Name = "Alien" };

        Assert.ThrowsAsync<InvalidOperationException>(() => new ProjectSerializer().SaveAsync(project));
        Assert.That(File.ReadAllText(path), Is.EqualTo(before));
    }

    [Test]
    public async Task Save_WritesBomlessUtf8()
    {
        var path = Write("Bom.blproj", """
            <BasicLangProject Version="1.0">
              <PropertyGroup><ProjectName>Bom</ProjectName></PropertyGroup>
            </BasicLangProject>
            """);

        var serializer = new ProjectSerializer();
        var project = await serializer.LoadAsync(path);
        project.RootNamespace = "Changed";
        await serializer.SaveAsync(project);

        var bytes = await File.ReadAllBytesAsync(path);
        Assert.That(bytes.Length, Is.GreaterThan(3));
        Assert.That(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, Is.False,
            "project files in this repo are BOM-less UTF-8; XDocument.Save(path) would inject a BOM");
    }
}
