using System.Text;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;
using VisualGameStudio.ProjectSystem.Serialization;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// Service for managing project templates.
/// </summary>
public class ProjectTemplateService : IProjectTemplateService
{
    private readonly List<ProjectTemplate> _customTemplates = new();
    private readonly List<string> _recentTemplateIds = new();
    private readonly IGitService? _gitService;
    private readonly SolutionSerializer _solutionSerializer = new();

    public ProjectTemplateService(IGitService? gitService = null)
    {
        _gitService = gitService;
    }

    public IReadOnlyList<SolutionType> GetSolutionTypes()
    {
        return SolutionTypes.All;
    }

    public IReadOnlyList<ProjectTemplate> GetProjectTemplates(SolutionType solutionType)
    {
        return GetAllProjectTemplates()
            .Where(t => t.SupportedSolutionTypes.Contains(solutionType.Id))
            .OrderBy(t => t.Order)
            .ToList();
    }

    public IReadOnlyList<ProjectTemplate> GetAllProjectTemplates()
    {
        var templates = new List<ProjectTemplate>();
        templates.AddRange(ProjectTemplates.All);
        templates.AddRange(_customTemplates);
        return templates.OrderBy(t => t.Order).ToList();
    }

    public async Task<ProjectCreationResult> CreateProjectAsync(CreateProjectOptions options, CancellationToken cancellationToken = default)
    {
        var result = new ProjectCreationResult();

        // Validate options
        var validation = ValidateProjectOptions(options);
        if (!validation.IsValid)
        {
            result.Error = string.Join("; ", validation.Errors);
            return result;
        }

        result.Warnings.AddRange(validation.Warnings);

        try
        {
            // Determine paths
            var projectDir = options.CreateSolutionFolder
                ? Path.Combine(options.Location, options.Name, options.Name)
                : Path.Combine(options.Location, options.Name);

            var solutionDir = options.CreateSolutionFolder
                ? Path.Combine(options.Location, options.Name)
                : options.Location;

            // Create directories
            Directory.CreateDirectory(projectDir);

            // Generate project files
            var projectFile = await GenerateProjectFilesAsync(projectDir, options, cancellationToken);
            result.ProjectPath = projectFile;

            // Generate source files
            var filesToOpen = await GenerateSourceFilesAsync(projectDir, options, cancellationToken);
            result.FilesToOpen.AddRange(filesToOpen);

            // Create or update solution
            if (!options.AddToExistingSolution && options.Template.CreateSolution)
            {
                var solutionFile = await CreateSolutionFileAsync(solutionDir, options, projectFile, cancellationToken);
                result.SolutionPath = solutionFile;
            }
            else if (options.AddToExistingSolution && !string.IsNullOrEmpty(options.ExistingSolutionPath))
            {
                await AddProjectToSolutionAsync(options.ExistingSolutionPath, projectFile, options.Template, cancellationToken);
                result.SolutionPath = options.ExistingSolutionPath;
            }

            // Initialize git repository
            if (options.CreateGitRepository && _gitService != null)
            {
                await _gitService.InitRepositoryAsync(solutionDir);
                await CreateGitIgnoreAsync(solutionDir, options.SolutionType, cancellationToken);
            }

            // Track recent template
            TrackRecentTemplate(options.Template.Id);

            result.Success = true;
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
        }

        return result;
    }

    public async Task<SolutionCreationResult> CreateSolutionAsync(CreateSolutionOptions options, CancellationToken cancellationToken = default)
    {
        var result = new SolutionCreationResult();

        try
        {
            var solutionDir = Path.Combine(options.Location, options.Name);
            Directory.CreateDirectory(solutionDir);

            // Create solution file (XML, via SolutionSerializer)
            var solutionFile = Path.Combine(solutionDir, $"{options.Name}{options.SolutionType.SolutionExtension}");
            var emptySolution = new BasicLangSolution
            {
                FilePath = solutionFile,
                SolutionName = options.Name
            };
            await _solutionSerializer.SaveAsync(emptySolution, cancellationToken);
            result.SolutionPath = solutionFile;

            // Create initial projects
            foreach (var projectOptions in options.InitialProjects)
            {
                projectOptions.Location = solutionDir;
                projectOptions.AddToExistingSolution = true;
                projectOptions.ExistingSolutionPath = solutionFile;
                projectOptions.CreateSolutionFolder = false;
                projectOptions.CreateGitRepository = false;

                var projectResult = await CreateProjectAsync(projectOptions, cancellationToken);
                if (projectResult.Success && projectResult.ProjectPath != null)
                {
                    result.ProjectPaths.Add(projectResult.ProjectPath);
                    result.FilesToOpen.AddRange(projectResult.FilesToOpen);
                }
            }

            // Initialize git repository
            if (options.CreateGitRepository && _gitService != null)
            {
                await _gitService.InitRepositoryAsync(solutionDir);
                await CreateGitIgnoreAsync(solutionDir, options.SolutionType, cancellationToken);
            }

            result.Success = true;
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
        }

        return result;
    }

    public ProjectValidationResult ValidateProjectOptions(CreateProjectOptions options)
    {
        var result = new ProjectValidationResult { IsValid = true };

        // Validate name
        if (string.IsNullOrWhiteSpace(options.Name))
        {
            result.Errors.Add("Project name is required.");
            result.IsValid = false;
        }
        else if (!IsValidProjectName(options.Name))
        {
            result.Errors.Add("Project name contains invalid characters.");
            result.IsValid = false;
        }

        // Validate location
        if (string.IsNullOrWhiteSpace(options.Location))
        {
            result.Errors.Add("Project location is required.");
            result.IsValid = false;
        }
        else if (!Directory.Exists(options.Location))
        {
            result.Warnings.Add("Location directory does not exist and will be created.");
        }

        // Check if project already exists
        var projectDir = options.CreateSolutionFolder
            ? Path.Combine(options.Location, options.Name)
            : options.Location;

        if (Directory.Exists(Path.Combine(projectDir, options.Name)))
        {
            result.Warnings.Add("A folder with this name already exists. Files may be overwritten.");
        }

        // Validate template supports solution type
        if (!options.Template.SupportedSolutionTypes.Contains(options.SolutionType.Id))
        {
            result.Errors.Add($"Template '{options.Template.Name}' does not support solution type '{options.SolutionType.Name}'.");
            result.IsValid = false;
        }

        return result;
    }

    public void RegisterTemplate(ProjectTemplate template)
    {
        _customTemplates.Add(template);
    }

    public IReadOnlyList<ProjectTemplate> GetRecentTemplates()
    {
        return _recentTemplateIds
            .Select(id => GetAllProjectTemplates().FirstOrDefault(t => t.Id == id))
            .Where(t => t != null)
            .Cast<ProjectTemplate>()
            .ToList();
    }

    #region Private Methods

    private static bool IsValidProjectName(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return !name.Any(c => invalidChars.Contains(c)) &&
               !string.IsNullOrWhiteSpace(name) &&
               !name.StartsWith('.') &&
               name.Length <= 100;
    }

    private void TrackRecentTemplate(string templateId)
    {
        _recentTemplateIds.Remove(templateId);
        _recentTemplateIds.Insert(0, templateId);
        while (_recentTemplateIds.Count > 10)
        {
            _recentTemplateIds.RemoveAt(_recentTemplateIds.Count - 1);
        }
    }

    private async Task<string> GenerateProjectFilesAsync(string projectDir, CreateProjectOptions options, CancellationToken cancellationToken)
    {
        var projectFile = Path.Combine(projectDir, $"{options.Name}{options.SolutionType.ProjectExtension}");

        var content = GenerateProjectFileContent(options);
        await File.WriteAllTextAsync(projectFile, content, cancellationToken);

        return projectFile;
    }

    private string GenerateProjectFileContent(CreateProjectOptions options)
    {
        var outputType = GetOutputType(options.Template);
        // ⚠ No default arm. This switch used to fall back to "CSharp", so a solution
        // type without an arm did not fail — it silently created a C# project (the
        // same shape of bug BuildService.GetBackendId documents). A missing id now
        // surfaces as the creation error; ProjectTemplateBackendMappingTests pins
        // every id in SolutionTypes.All.
        var targetBackend = options.SolutionType.Id switch
        {
            "dotnet" => "CSharp",
            "msil" => "MSIL",
            "native" => "Cpp",
            "llvm" => "LLVM",
            "cpp" => "Cpp",
            "javascript" => "JavaScript",
            _ => throw new NotSupportedException(
                $"Solution type '{options.SolutionType.Id}' has no TargetBackend mapping — add an arm to GenerateProjectFileContent.")
        };

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<BasicLangProject Version=\"1.0\">");
        sb.AppendLine("  <PropertyGroup>");
        // A project file is XML and a project NAME is user text. '&' is a legal file-name
        // character that IsValidProjectName admits, so an unescaped name made the .blproj
        // malformed: creation succeeded and the very first build died with
        // "'<' is an unexpected token. The expected token is ';'." See
        // TemplateProjectNameEscapingTests. SOURCE files keep the raw name — a .bas is not XML.
        sb.AppendLine($"    <ProjectName>{Xml(options.Name)}</ProjectName>");
        sb.AppendLine($"    <OutputType>{outputType switch { "exe" => "Exe", "library" => "Library", _ => "WinExe" }}</OutputType>");
        sb.AppendLine($"    <RootNamespace>{Xml(options.Namespace ?? options.Name)}</RootNamespace>");
        sb.AppendLine($"    <TargetBackend>{targetBackend}</TargetBackend>");
        // The wizard's TFM picker was decorative until this line: ProjectCreationOptions
        // .TargetFramework was collected and then discarded, so every generated project silently
        // got the "net8.0" default no matter what the user chose. Both csproj emitters append
        // "-windows" themselves when a UI framework is on, so a desktop template need not
        // pre-suffix it here.
        if (!string.IsNullOrWhiteSpace(options.TargetFramework))
        {
            sb.AppendLine($"    <TargetFramework>{Xml(options.TargetFramework)}</TargetFramework>");
        }
        // Pure C++ projects (Language=Cpp): user-authored C++ built by
        // CppProjectBuilder through a discovered native toolchain — the
        // BasicLang pipeline is not involved.
        if (options.SolutionType.Id == "cpp")
        {
            sb.AppendLine("    <Language>Cpp</Language>");
            sb.AppendLine($"    <CppStandard>{(string.IsNullOrEmpty(options.CppStandard) ? "c++20" : options.CppStandard)}</CppStandard>");
            if (!string.IsNullOrEmpty(options.CppToolchain))
            {
                sb.AppendLine($"    <CppToolchain>{options.CppToolchain}</CppToolchain>");
            }
        }
        // Windows desktop UI frameworks need the net*-windows TFM at build time.
        if (options.Template.Id == "winforms-app")
        {
            sb.AppendLine("    <UseWindowsForms>true</UseWindowsForms>");
            // Without a DPI mode WinForms runs in the legacy unaware mode, where the designer's
            // pixel coordinates and the running window's disagree on any scaled display — a form
            // laid out at 100% comes up clipped at 150%. PerMonitorV2 is the mode that makes
            // designer pixels and runtime pixels the same unit.
            sb.AppendLine("    <ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>");
        }
        else if (options.Template.Id == "wpf-app")
        {
            sb.AppendLine("    <UseWPF>true</UseWPF>");
        }
        sb.AppendLine("  </PropertyGroup>");
        sb.AppendLine("  <PropertyGroup Condition=\"'$(Configuration)' == 'Debug'\">");
        sb.AppendLine("    <OutputPath>bin\\Debug</OutputPath>");
        sb.AppendLine("    <DebugSymbols>true</DebugSymbols>");
        sb.AppendLine("    <Optimize>false</Optimize>");
        sb.AppendLine("  </PropertyGroup>");
        sb.AppendLine("  <PropertyGroup Condition=\"'$(Configuration)' == 'Release'\">");
        sb.AppendLine("    <OutputPath>bin\\Release</OutputPath>");
        sb.AppendLine("    <DebugSymbols>false</DebugSymbols>");
        sb.AppendLine("    <Optimize>true</Optimize>");
        sb.AppendLine("  </PropertyGroup>");
        sb.AppendLine("  <ItemGroup>");

        // Add compile items based on template type
        var compileItems = GetCompileItems(options.Template.Id);
        foreach (var item in compileItems)
        {
            sb.AppendLine($"    <Compile Include=\"{XmlAttr(item)}\" />");
        }

        // The C++ game template links the engine import library; CppProjectBuilder
        // resolves the .lib via EngineDeployment and deploys the native DLLs.
        if (options.Template.Id == "cpp-game-app")
        {
            sb.AppendLine("    <NativeLib Include=\"VisualGameStudioEngine.lib\" />");
        }

        sb.AppendLine("  </ItemGroup>");

        // NuGet packages required by the template (flowed into the generated
        // csproj by the build; ignored by older loaders).
        var packageRefs = GetPackageReferences(options.Template.Id);
        if (packageRefs.Count > 0)
        {
            sb.AppendLine("  <ItemGroup>");
            foreach (var (packageId, version) in packageRefs)
            {
                sb.AppendLine($"    <PackageReference Include=\"{packageId}\" Version=\"{version}\" />");
            }
            sb.AppendLine("  </ItemGroup>");
        }

        sb.AppendLine("</BasicLangProject>");

        return sb.ToString();
    }

    /// <summary>Escapes user text for XML ELEMENT content (the .blproj carries no user text in
    /// an attribute). Hand-rolled rather than SecurityElement/XElement so the generator stays a
    /// StringBuilder that emits exactly the layout above.</summary>
    private static string Xml(string text) => (text ?? string.Empty)
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;");

    /// <summary>
    /// Escapes text destined for a double-quoted XML ATTRIBUTE value, where <see cref="Xml"/> is
    /// not enough — an unescaped <c>"</c> closes the attribute early and makes the file unparseable.
    ///
    /// <para>ℹ️ Prospective hardening rather than a live defect: every compile-item path reaching
    /// this today comes from a closed switch of literal filenames in <c>GetCompileItems</c>. It
    /// becomes live as soon as an item is named by the user, which is what creating a form does.</para>
    /// </summary>
    private static string XmlAttr(string text) => Xml(text).Replace("\"", "&quot;");

    private static List<(string PackageId, string Version)> GetPackageReferences(string templateId)
    {
        return templateId switch
        {
            "avalonia-app" => new List<(string, string)>
            {
                ("Avalonia.Desktop", "11.1.0"),
                ("Avalonia.Themes.Fluent", "11.1.0")
            },
            _ => new List<(string, string)>()
        };
    }

    private static string GetOutputType(ProjectTemplate template)
    {
        return template.Id switch
        {
            "console-app" => "exe",
            "game-app" => "exe",
            "winforms-app" => "winexe",
            "wpf-app" => "winexe",
            "avalonia-app" => "winexe",
            "class-library" => "library",
            "web-api" => "exe",
            "unit-test" => "exe",
            "cpp-console-app" => "exe",
            "cpp-game-app" => "exe",
            "cpp-library" => "library",
            "web-site" => "exe",       // Sub Main is the page's entry point
            _ => "exe"
        };
    }

    private static List<string> GetCompileItems(string templateId)
    {
        return templateId switch
        {
            "game-app" => new List<string> { "Main.bas", "GameState.mod", "Player.cls" },
            "console-app" => new List<string> { "Main.bas", "Helpers.mod" },
            "class-library" => new List<string> { "Library.mod", "Types.cls" },
            // GUI templates also generate UIHelpers.bas on disk; it must be a
            // compile item or it becomes an orphan excluded from the build.
            // ⛔ MainForm.bas MUST be listed. GetSourceFiles() globs **/*.bas only while a project
            // has no explicit <Compile> items, and these templates are explicit — an unlisted file
            // is an orphan excluded from the build, with no diagnostic.
            "winforms-app" => new List<string> { "Main.bas", "MainForm.bas", "UIHelpers.bas" },
            "wpf-app" => new List<string> { "Main.bas", "UIHelpers.bas" },
            "avalonia-app" => new List<string> { "Main.bas", "UIHelpers.bas" },
            "cpp-console-app" => new List<string> { "main.cpp" },
            "cpp-game-app" => new List<string> { "main.cpp" },
            "cpp-library" => new List<string> { "mathutils.cpp" },
            _ => new List<string> { "Main.bas" }
        };
    }

    private async Task<List<string>> GenerateSourceFilesAsync(string projectDir, CreateProjectOptions options, CancellationToken cancellationToken)
    {
        var filesToOpen = new List<string>();

        // Templates that use Main.bas as entry point. Pure C++ solution types
        // write their own entry files below — the generic BasicLang-content
        // Main.<ext> write must never run for them (it would leave a bogus
        // BasicLang-content main.cpp on disk).
        if (options.SolutionType.Id != "cpp" && options.Template.Id != "class-library")
        {
            var mainFile = Path.Combine(projectDir, $"Main{options.SolutionType.SourceExtension}");
            var content = GenerateMainFileContent(options);
            await File.WriteAllTextAsync(mainFile, content, cancellationToken);
            filesToOpen.Add(mainFile);
        }

        // Generate additional files based on template
        switch (options.Template.Id)
        {
            case "game-app":
                await GenerateGameFilesAsync(projectDir, options, cancellationToken);
                break;
            case "console-app":
                await GenerateConsoleFilesAsync(projectDir, options, cancellationToken);
                break;
            case "class-library":
                var libraryFiles = await GenerateClassLibraryFilesAsync(projectDir, options, cancellationToken);
                filesToOpen.AddRange(libraryFiles);
                break;
            case "winforms-app":
                await GenerateWinFormsFilesAsync(projectDir, options, cancellationToken);
                break;
            case "wpf-app":
                await GenerateWpfFilesAsync(projectDir, options, cancellationToken);
                break;
            case "avalonia-app":
                await GenerateAvaloniaFilesAsync(projectDir, options, cancellationToken);
                break;
            case "cpp-console-app":
                filesToOpen.Add(await GenerateCppConsoleFilesAsync(projectDir, options, cancellationToken));
                break;
            case "cpp-library":
                filesToOpen.AddRange(await GenerateCppLibraryFilesAsync(projectDir, cancellationToken));
                break;
            case "cpp-game-app":
                filesToOpen.Add(await GenerateCppGameFilesAsync(projectDir, options, cancellationToken));
                break;
        }

        return filesToOpen;
    }

    private string GenerateMainFileContent(CreateProjectOptions options)
    {
        var ns = options.Namespace ?? options.Name;

        return options.Template.Id switch
        {
            "console-app" => GenerateConsoleAppMain(ns),
            "game-app" => GenerateGameAppMain(ns),
            "winforms-app" => GenerateWinFormsMain(ns),
            "wpf-app" => GenerateWpfMain(ns),
            "avalonia-app" => GenerateAvaloniaMain(ns),
            "class-library" => GenerateClassLibraryMain(ns),
            "web-api" => GenerateWebApiMain(ns),
            "unit-test" => GenerateUnitTestMain(ns),
            "web-site" => GenerateWebSiteMain(ns),
            _ => GenerateConsoleAppMain(ns)
        };
    }

    #region JavaScript template files

    // NOTE: This file content must stay in sync with the CLI's "web" template in
    // BasicLang/ProjectSystem/TemplateEngine.cs (the two template systems are
    // known to drift — change one, change both; TemplateBuildSweepTests enforces
    // it). The CLI substitutes {{ProjectName}}; here Replace does the same.
    //
    // The page is the shape verified in a browser when the JavaScript backend
    // shipped: a typed DOM entry (`As Document = ::document`), a class with an
    // Event, AddHandler with a single-line Sub lambda, and a DOM listener. Keep
    // to constructs the JavaScript capability checker admits — a BL70xx here
    // means File → New Project produces a project that does not build.
    private static string GenerateWebSiteMain(string ns)
    {
        const string template = @"' {{ProjectName}} - Web Site
' Generated by Visual Game Studio
'
' This project compiles to JavaScript. Build writes index.html, {{ProjectName}}.js
' and a source map under bin\; press F5 to preview the site in your browser.
' The browser DOM is typed through the shipped dom-core declarations (Document,
' Element, DomEvent, ...) and ::name reaches any other browser global.

Class ClickCounter
    Public Event Changed(count As Integer)
    Private clicks As Integer

    Public Sub Bump()
        clicks = clicks + 1
        RaiseEvent Changed(clicks)
    End Sub
End Class

Sub Main()
    Dim doc As Document = ::document
    Dim counter As New ClickCounter()

    Dim heading As Element = doc.createElement(""h1"")
    heading.textContent = ""Hello from {{ProjectName}}""
    doc.body.appendChild(heading)

    Dim button As Element = doc.createElement(""button"")
    button.textContent = ""Click me""
    doc.body.appendChild(button)

    AddHandler counter.Changed, Sub(count As Integer) heading.textContent = ""Clicked "" & count & "" times""
    button.addEventListener(""click"", Sub(e As DomEvent) counter.Bump())

    Console.WriteLine(""{{ProjectName}} loaded"")
End Sub
";
        return template.Replace("{{ProjectName}}", ns);
    }

    #endregion

    private static string GenerateConsoleAppMain(string ns)
    {
        return $@"' {ns} - Console Application
' Generated by Visual Game Studio

Using System

Module Main
    Sub Main()
        PrintHeader(""{ns}"")
        Console.WriteLine(FormatMessage(""Hello, World!""))
        Console.WriteLine(FormatMessage(""Welcome to {ns}!""))
        Console.WriteLine()
        Console.WriteLine(""Current time: "" & DateTime.Now.ToString())
        Console.WriteLine(""Program completed successfully!"")
    End Sub
End Module
";
    }

    private static string GenerateGameAppMain(string ns)
    {
        return $@"Module Main
    Sub Main()
        GameInit(800, 600, ""My Game"")

        Dim player As New Player(""Hero"")

        While Not GameShouldClose()
            GameBeginFrame()
            ClearBackground(40, 40, 60)
            DrawText(player.Name, 10, 10, 20, 255, 255, 255, 255)
            GameEndFrame()
        End While

        GameShutdown()
    End Sub
End Module
";
    }

    // ⛔⛔ THE CANONICAL WINFORMS SHAPE (Owner decision 3). This file is the ENTRY POINT ONLY;
    // the form lives in its own MainForm.bas, written by GenerateWinFormsFilesAsync, and carries a
    // real InitializeComponent.
    //
    // Until 2026-09-13 this template emitted ONE file holding both the module and the form, with
    // all control setup inline in `Public Sub New()` and no InitializeComponent at all — while the
    // shipped VSIX template (BasicLang.VisualStudio/.../Templates/Projects/WinFormsApp/) emitted
    // two files WITH one, and the CLI had no WinForms template at all. Three shapes, and the
    // designer has to write into whichever one the user happens to have. The VSIX shape wins: it
    // is the one proven end to end, and it is what the designer's region writer generates.
    //
    // ⚠ Deliberate deviation from the VSIX NAMING: the entry file stays `Main.bas` rather than
    // becoming `Program.bas`, because every IDE template writes `Main.<ext>` through one shared
    // path — singling this one out would trade a cross-product divergence for an in-product one.
    // The shape that matters to the designer is identical.
    private static string GenerateWinFormsMain(string ns)
    {
        return $@"' {ns} - Windows Forms Application
' Generated by Visual Game Studio
' Requires .NET backend compilation

Using System
Using System.Windows.Forms

Module Program
    ''' <summary>Main entry point for the application.</summary>
    Sub Main()
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)
        Application.Run(New MainForm())
    End Sub
End Module
";
    }

    /// <summary>
    /// <c>MainForm.bas</c> — the canonical form shape, matching the shipped VSIX template.
    ///
    /// <para>⛔ The handler is named <c>btnClick_Click</c> (<c>&lt;control&gt;_&lt;Event&gt;</c>),
    /// not <c>OnButtonClick</c>. That is the name the designer generates and the name its
    /// recognizer expects when importing; a template that disagrees hands every new project a
    /// handler the designer will not re-wire.</para>
    ///
    /// <para>⚠ The handler is declared BELOW the InitializeComponent that wires it, exactly as the
    /// VSIX template has it. Measured 2026-09-13: that compiles through BasicLang and csc with the
    /// handler's full parameter types intact. The web ordering rule does NOT apply here — there is
    /// no declared delegate for an erased handler to mismatch.</para>
    ///
    /// <para>⚠ One deliberate difference from the VSIX file: <c>Me.ClientSize</c>, where that
    /// template writes <c>Me.Size</c>. The designer's own model calls Width/Height the CLIENT size
    /// and its region writer emits <c>ClientSize</c>, so a template using <c>Size</c> would be
    /// silently RESIZED the first time the form was saved from the designer — the two differ by
    /// the border and title bar. Matching the generated shape keeps that round trip stable.</para>
    /// </summary>
    private static string GenerateWinFormsForm(string ns)
    {
        return $@"' MainForm.bas - Main application window
' {ns}

Using System
Using System.Drawing
Using System.Windows.Forms

    ''' <summary>Main application form.</summary>
Public Class MainForm
    Inherits Form

    Private lblMessage As Label
    Private btnClick As Button

    ''' <summary>Creates a new instance of MainForm.</summary>
    Public Sub New()
        InitializeComponent()
    End Sub

    ''' <summary>Initializes form components.</summary>
    Private Sub InitializeComponent()
        Me.Text = ""{ns}""
        Me.ClientSize = New Size(400, 300)
        Me.StartPosition = FormStartPosition.CenterScreen

        lblMessage = New Label()
        lblMessage.Location = New Point(20, 20)
        lblMessage.Size = New Size(300, 30)
        lblMessage.Text = ""Hello, {ns}!""
        lblMessage.Font = New Font(""Segoe UI"", 12)
        Me.Controls.Add(lblMessage)

        btnClick = New Button()
        btnClick.Location = New Point(20, 60)
        btnClick.Size = New Size(100, 30)
        btnClick.Text = ""Click Me""
        AddHandler btnClick.Click, AddressOf btnClick_Click
        Me.Controls.Add(btnClick)
    End Sub

    ''' <summary>Handles the button click.</summary>
    Private Sub btnClick_Click(sender As Object, e As EventArgs)
        MessageBox.Show(""Button clicked!"", ""{ns}"", MessageBoxButtons.OK, MessageBoxIcon.Information)
    End Sub
End Class
";
    }

    private static string GenerateWpfMain(string ns)
    {
        return $@"' {ns} - WPF Application
' Generated by Visual Game Studio
' Requires .NET backend compilation

Using System
Using System.Windows
Using System.Windows.Controls

Module Program
    <STAThread>
    Sub Main()
        Dim app As New Application()
        Dim window As New MainWindow()
        app.Run(window)
    End Sub
End Module

Public Class MainWindow
    Inherits Window

    Private btnClick As Button

    Public Sub New()
        Me.Title = ""{ns}""
        Me.Width = 400
        Me.Height = 300
        Me.WindowStartupLocation = WindowStartupLocation.CenterScreen

        ' Create layout
        Dim panel As New StackPanel()
        panel.VerticalAlignment = VerticalAlignment.Center
        panel.HorizontalAlignment = HorizontalAlignment.Center

        ' Create label
        Dim label As New TextBlock()
        label.Text = ""Hello, {ns}!""
        label.FontSize = 24
        label.Margin = New Thickness(0, 0, 0, 20)
        panel.Children.Add(label)

        ' Create button
        btnClick = New Button()
        btnClick.Content = ""Click Me""
        btnClick.Width = 100
        btnClick.Height = 30
        AddHandler btnClick.Click, AddressOf OnButtonClick
        panel.Children.Add(btnClick)

        Me.Content = panel
    End Sub

    Private Sub OnButtonClick(sender As Object, e As RoutedEventArgs)
        MessageBox.Show(""Button clicked!"", ""{ns}"", MessageBoxButton.OK, MessageBoxImage.Information)
    End Sub
End Class
";
    }

    private static string GenerateAvaloniaMain(string ns)
    {
        return $@"' {ns} - Avalonia UI Application
' Generated by Visual Game Studio
' Requires .NET backend compilation with Avalonia NuGet packages

Using System
Using Avalonia
Using Avalonia.Controls
Using Avalonia.Layout
Using Avalonia.Media

Module Program
    Sub Main()
        AppBuilder.Configure(Of App)() _
            .UsePlatformDetect() _
            .StartWithClassicDesktopLifetime(Nothing)
    End Sub
End Module

Public Class App
    Inherits Application

    Public Overrides Sub OnFrameworkInitializationCompleted()
        Dim window As New MainWindow()
        window.Show()
        MyBase.OnFrameworkInitializationCompleted()
    End Sub
End Class

Public Class MainWindow
    Inherits Window

    Public Sub New()
        Me.Title = ""{ns}""
        Me.Width = 400
        Me.Height = 300
        Me.WindowStartupLocation = WindowStartupLocation.CenterScreen

        ' Create layout
        Dim panel As New StackPanel()
        panel.VerticalAlignment = VerticalAlignment.Center
        panel.HorizontalAlignment = HorizontalAlignment.Center

        ' Create label
        Dim label As New TextBlock()
        label.Text = ""Hello, {ns}!""
        label.FontSize = 24
        label.Margin = New Thickness(0, 0, 0, 20)
        panel.Children.Add(label)

        ' Create button
        Dim btn As New Button()
        btn.Content = ""Click Me""
        btn.Width = 100
        btn.Height = 30
        panel.Children.Add(btn)

        Me.Content = panel
    End Sub
End Class
";
    }

    private static string GenerateClassLibraryMain(string ns)
    {
        return $@"' {ns} - Class Library
' Generated by Visual Game Studio
' This is a reusable code library module

Module {ns}

' Example utility function
Public Function Add(a As Integer, b As Integer) As Integer
    Return a + b
End Function

' Example string utility
Public Function Capitalize(text As String) As String
    If text = """" Then Return """"
    Return text.Substring(0, 1).ToUpper() & text.Substring(1)
End Function

' Example validation function
Public Function IsValidEmail(email As String) As Boolean
    Return email.Contains(""@"") And email.Contains(""."")
End Function

End Module
";
    }

    private static string GenerateWebApiMain(string ns)
    {
        return $@"' {ns} - Web API
' Generated by Visual Game Studio
' A REST API service using .NET

Using System
Using System.Net
Using System.IO

' API Configuration
Const API_PORT As Integer = 5000
Const API_HOST As String = ""localhost""

Sub Main()
    Console.WriteLine(""Starting {ns} Web API..."")
    Console.WriteLine(""Listening on http://"" & API_HOST & "":"" & API_PORT.ToString())
    Console.WriteLine(""Press Ctrl+C to stop the server."")

    ' Note: Full Web API support requires ASP.NET Core integration
    ' This is a basic template demonstrating the structure

    Console.WriteLine()
    Console.WriteLine(""Available endpoints:"")
    Console.WriteLine(""  GET  /api/health  - Health check"")
    Console.WriteLine(""  GET  /api/items   - List items"")
    Console.WriteLine(""  POST /api/items   - Create item"")

    ' Keep running
    Console.WriteLine()
    Console.WriteLine(""Press Enter to exit."")
    Console.ReadLine()
End Sub

' Example API handler
Function HandleHealthCheck() As String
    Return ""{{""""status"""": """"healthy"""", """"timestamp"""": """""" & DateTime.Now.ToString() & """"""}}""
End Function
";
    }

    private static string GenerateUnitTestMain(string ns)
    {
        return $@"' {ns} - Unit Tests
' Generated by Visual Game Studio
' Unit testing project

Using System

' Test class
Module {ns}Tests

' Test counter
Dim passedTests As Integer = 0
Dim failedTests As Integer = 0

Sub Main()
    Console.WriteLine(""Running {ns} Tests..."")
    Console.WriteLine(""========================="")
    Console.WriteLine()

    ' Run all tests
    TestAddition()
    TestSubtraction()
    TestStringOperations()

    ' Print summary
    Console.WriteLine()
    Console.WriteLine(""========================="")
    Console.WriteLine(""Tests Passed: "" & passedTests.ToString())
    Console.WriteLine(""Tests Failed: "" & failedTests.ToString())

    If failedTests = 0 Then
        Console.WriteLine(""All tests passed!"")
    End If
End Sub

Sub TestAddition()
    Dim result As Integer = 2 + 2
    AssertEqual(result, 4, ""TestAddition: 2 + 2 should equal 4"")
End Sub

Sub TestSubtraction()
    Dim result As Integer = 10 - 3
    AssertEqual(result, 7, ""TestSubtraction: 10 - 3 should equal 7"")
End Sub

Sub TestStringOperations()
    Dim text As String = ""Hello""
    AssertEqual(text.Length, 5, ""TestStringOperations: 'Hello'.Length should be 5"")
End Sub

Sub AssertEqual(actual As Integer, expected As Integer, message As String)
    If actual = expected Then
        Console.WriteLine(""[PASS] "" & message)
        passedTests = passedTests + 1
    Else
        Console.WriteLine(""[FAIL] "" & message & "" (got "" & actual.ToString() & "")"")
        failedTests = failedTests + 1
    End If
End Sub

End Module
";
    }

    private async Task GenerateGameFilesAsync(string projectDir, CreateProjectOptions options, CancellationToken cancellationToken)
    {
        // Create assets directories
        Directory.CreateDirectory(Path.Combine(projectDir, "Assets", "Textures"));
        Directory.CreateDirectory(Path.Combine(projectDir, "Assets", "Sounds"));
        Directory.CreateDirectory(Path.Combine(projectDir, "Assets", "Fonts"));

        // Create GameState.mod — shared game state
        var gameStateFile = Path.Combine(projectDir, "GameState.mod");
        await File.WriteAllTextAsync(gameStateFile, GenerateGameStateModule(), cancellationToken);

        // Create Player.cls — player class
        var playerFile = Path.Combine(projectDir, "Player.cls");
        await File.WriteAllTextAsync(playerFile, GeneratePlayerClass(), cancellationToken);
    }

    private static string GenerateGameStateModule()
    {
        return @"' Global game state — accessible from all files

Public Score As Integer = 0
Public Level As Integer = 1
Public IsGameOver As Boolean = False

Public Sub ResetGame()
    Score = 0
    Level = 1
    IsGameOver = False
End Sub
";
    }

    private static string GeneratePlayerClass()
    {
        return @"Public Name As String
Public X As Single = 400
Public Y As Single = 300
Public Speed As Single = 5.0

Public Sub New(name As String)
    Me.Name = name
End Sub

Public Sub Update()
    If IsKeyDown(87) Then Y = Y - Speed
    If IsKeyDown(83) Then Y = Y + Speed
    If IsKeyDown(65) Then X = X - Speed
    If IsKeyDown(68) Then X = X + Speed
End Sub
";
    }

    private async Task GenerateConsoleFilesAsync(string projectDir, CreateProjectOptions options, CancellationToken cancellationToken)
    {
        // Create Helpers.mod — utility functions
        var helpersFile = Path.Combine(projectDir, "Helpers.mod");
        await File.WriteAllTextAsync(helpersFile, GenerateHelpersModule(options.Namespace ?? options.Name), cancellationToken);
    }

    private static string GenerateHelpersModule(string ns)
    {
        return $@"' Utility functions for {ns}

Public Function FormatMessage(message As String) As String
    Return ""["" & DateTime.Now.ToString(""HH:mm:ss"") & ""] "" & message
End Function

Public Sub PrintHeader(title As String)
    Dim separator As String = New String(""=""c, title.Length + 4)
    Console.WriteLine(separator)
    Console.WriteLine(""  "" & title)
    Console.WriteLine(separator)
End Sub
";
    }

    private async Task<List<string>> GenerateClassLibraryFilesAsync(string projectDir, CreateProjectOptions options, CancellationToken cancellationToken)
    {
        var ns = options.Namespace ?? options.Name;
        var filesToOpen = new List<string>();

        // Create Library.mod — public API module
        var libraryFile = Path.Combine(projectDir, "Library.mod");
        await File.WriteAllTextAsync(libraryFile, GenerateLibraryModule(ns), cancellationToken);
        filesToOpen.Add(libraryFile);

        // Create Types.cls — public data types
        var typesFile = Path.Combine(projectDir, "Types.cls");
        await File.WriteAllTextAsync(typesFile, GenerateTypesClass(ns), cancellationToken);

        return filesToOpen;
    }

    private static string GenerateLibraryModule(string ns)
    {
        return $@"' {ns} — Public API module

Public Function Add(a As Integer, b As Integer) As Integer
    Return a + b
End Function

Public Function Capitalize(text As String) As String
    If text = """" Then Return """"
    Return text.Substring(0, 1).ToUpper() & text.Substring(1)
End Function

Public Function IsValidEmail(email As String) As Boolean
    Return email.Contains(""@"") And email.Contains(""."")
End Function
";
    }

    private static string GenerateTypesClass(string ns)
    {
        return $@"' {ns} — Public data types

Public Class Result
    Public Success As Boolean
    Public Message As String

    Public Sub New(success As Boolean, message As String)
        Me.Success = success
        Me.Message = message
    End Sub
End Class
";
    }

    private async Task GenerateWinFormsFilesAsync(string projectDir, CreateProjectOptions options, CancellationToken cancellationToken)
    {
        var ns = options.Namespace ?? options.Name;

        // The form itself, in its own file — the canonical VSIX split. Main.bas is the entry
        // point only.
        await File.WriteAllTextAsync(
            Path.Combine(projectDir, $"MainForm{options.SolutionType.SourceExtension}"),
            GenerateWinFormsForm(ns), cancellationToken);

        // Create UI helper file
        var formFile = Path.Combine(projectDir, $"UIHelpers{options.SolutionType.SourceExtension}");
        var formContent = $@"' UI Helper functions for {ns}
' Generated by Visual Game Studio

' Application settings
Dim appTitle As String = ""{options.Name}""
Dim appWidth As Integer = 800
Dim appHeight As Integer = 600

Sub SetApplicationTitle(title As String)
    appTitle = title
End Sub

Sub SetApplicationSize(width As Integer, height As Integer)
    appWidth = width
    appHeight = height
End Sub

Sub ShowMessage(message As String)
    PrintLine(message)
End Sub

Sub ShowError(message As String)
    PrintLine(""Error: "" & message)
End Sub
";
        await File.WriteAllTextAsync(formFile, formContent, cancellationToken);
    }

    private async Task GenerateWpfFilesAsync(string projectDir, CreateProjectOptions options, CancellationToken cancellationToken)
    {
        var ns = options.Namespace ?? options.Name;

        // Create UI helper file
        var windowFile = Path.Combine(projectDir, $"UIHelpers{options.SolutionType.SourceExtension}");
        var windowContent = $@"' UI Helper functions for {ns}
' Generated by Visual Game Studio

' Application settings
Dim appTitle As String = ""{options.Name}""
Dim appWidth As Integer = 800
Dim appHeight As Integer = 600

Sub SetApplicationTitle(title As String)
    appTitle = title
End Sub

Sub SetApplicationSize(width As Integer, height As Integer)
    appWidth = width
    appHeight = height
End Sub

Sub ShowMessage(message As String)
    PrintLine(message)
End Sub

Sub ShowError(message As String)
    PrintLine(""Error: "" & message)
End Sub
";
        await File.WriteAllTextAsync(windowFile, windowContent, cancellationToken);
    }

    private async Task GenerateAvaloniaFilesAsync(string projectDir, CreateProjectOptions options, CancellationToken cancellationToken)
    {
        var ns = options.Namespace ?? options.Name;

        // Create UI helper file
        var windowFile = Path.Combine(projectDir, $"UIHelpers{options.SolutionType.SourceExtension}");
        var windowContent = $@"' UI Helper functions for {ns}
' Generated by Visual Game Studio

' Application settings
Dim appTitle As String = ""{options.Name}""
Dim appWidth As Integer = 800
Dim appHeight As Integer = 600

Sub SetApplicationTitle(title As String)
    appTitle = title
End Sub

Sub SetApplicationSize(width As Integer, height As Integer)
    appWidth = width
    appHeight = height
End Sub

Sub ShowMessage(message As String)
    PrintLine(message)
End Sub

Sub ShowError(message As String)
    PrintLine(""Error: "" & message)
End Sub
";
        await File.WriteAllTextAsync(windowFile, windowContent, cancellationToken);
    }

    #region Pure C++ template files

    // NOTE: These file contents must stay in sync with the CLI's cpp-console /
    // cpp-library / cpp-game templates in BasicLang/ProjectSystem/TemplateEngine.cs
    // (the two template systems are known to drift — change one, change both).
    // The CLI substitutes {{ProjectName}}; here Replace does the same.
    // Intentional .blproj divergence: the IDE emits the user-chosen
    // <CppStandard>/<CppToolchain> from CreateProjectOptions
    // (GenerateProjectFileContent); the CLI templates emit defaults only
    // (hardcoded c++20, no <CppToolchain>). Only the SOURCE files above are
    // held identical.

    private async Task<string> GenerateCppConsoleFilesAsync(string projectDir, CreateProjectOptions options, CancellationToken cancellationToken)
    {
        const string template = @"#include <iostream>

int main()
{
    std::cout << ""Hello from {{ProjectName}}!"" << std::endl;
    return 0;
}
";
        var mainFile = Path.Combine(projectDir, "main.cpp");
        await File.WriteAllTextAsync(mainFile, template.Replace("{{ProjectName}}", options.Name), cancellationToken);
        return mainFile;
    }

    private async Task<List<string>> GenerateCppLibraryFilesAsync(string projectDir, CancellationToken cancellationToken)
    {
        var headerFile = Path.Combine(projectDir, "mathutils.h");
        await File.WriteAllTextAsync(headerFile, "#pragma once\nint Add(int a, int b);\n", cancellationToken);

        var sourceFile = Path.Combine(projectDir, "mathutils.cpp");
        await File.WriteAllTextAsync(sourceFile, "#include \"mathutils.h\"\nint Add(int a, int b) { return a + b; }\n", cancellationToken);

        return new List<string> { sourceFile };
    }

    private async Task<string> GenerateCppGameFilesAsync(string projectDir, CreateProjectOptions options, CancellationToken cancellationToken)
    {
        const string template = @"// Engine C-ABI declarations (see VisualGameStudioEngine/framework.h).
extern ""C"" {
    bool Framework_Initialize(int width, int height, const char* title);
    void Framework_Update();
    bool Framework_ShouldClose();
    void Framework_Shutdown();
    void Framework_BeginDrawing();
    void Framework_EndDrawing();
    void Framework_ClearBackground(unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    void Framework_DrawText(const char* text, int x, int y, int fontSize,
                            unsigned char r, unsigned char g, unsigned char b, unsigned char a);
}

int main()
{
    if (!Framework_Initialize(800, 450, ""{{ProjectName}}""))
        return 1;

    while (!Framework_ShouldClose())
    {
        Framework_Update();
        Framework_BeginDrawing();
        Framework_ClearBackground(30, 30, 46, 255);
        Framework_DrawText(""Hello from {{ProjectName}}!"", 260, 200, 24, 205, 214, 244, 255);
        Framework_EndDrawing();
    }

    Framework_Shutdown();
    return 0;
}
";
        var mainFile = Path.Combine(projectDir, "main.cpp");
        await File.WriteAllTextAsync(mainFile, template.Replace("{{ProjectName}}", options.Name), cancellationToken);
        return mainFile;
    }

    #endregion

    private async Task<string> CreateSolutionFileAsync(string solutionDir, CreateProjectOptions options, string projectFile, CancellationToken cancellationToken)
    {
        var solutionFile = Path.Combine(solutionDir, $"{options.Name}{options.SolutionType.SolutionExtension}");

        var solution = new BasicLangSolution
        {
            FilePath = solutionFile,
            SolutionName = options.Name,
            DefaultProject = options.Name
        };
        solution.Projects.Add(new SolutionProject
        {
            Name = options.Name,
            RelativePath = Path.GetRelativePath(solutionDir, projectFile),
            Type = GetSolutionProjectType(options.Template),
            IsStartupProject = true
        });

        // Written through SolutionSerializer so the file is always in the XML
        // format SolutionService can open.
        await _solutionSerializer.SaveAsync(solution, cancellationToken);
        return solutionFile;
    }

    private static string GetSolutionProjectType(ProjectTemplate template)
    {
        return GetOutputType(template) switch
        {
            "winexe" => "WinExe",
            "library" => "Library",
            _ => "Exe"
        };
    }

    private async Task AddProjectToSolutionAsync(string solutionPath, string projectPath, ProjectTemplate template, CancellationToken cancellationToken)
    {
        var projectName = Path.GetFileNameWithoutExtension(projectPath);
        var projectRelativePath = Path.GetRelativePath(Path.GetDirectoryName(solutionPath)!, projectPath);

        // Load-modify-save through the serializer. A corrupt or unreadable
        // solution surfaces as an error to the caller instead of being
        // silently replaced with a single-project solution.
        var solution = await _solutionSerializer.LoadAsync(solutionPath, cancellationToken);

        if (!solution.Projects.Any(p => p.RelativePath.Equals(projectRelativePath, StringComparison.OrdinalIgnoreCase)))
        {
            solution.Projects.Add(new SolutionProject
            {
                Name = projectName,
                RelativePath = projectRelativePath,
                Type = GetSolutionProjectType(template)
            });
        }

        await _solutionSerializer.SaveAsync(solution, cancellationToken);
    }

    private async Task CreateGitIgnoreAsync(string directory, SolutionType solutionType, CancellationToken cancellationToken)
    {
        var gitIgnorePath = Path.Combine(directory, ".gitignore");
        var content = GenerateGitIgnoreContent(solutionType);
        await File.WriteAllTextAsync(gitIgnorePath, content, cancellationToken);
    }

    private static string GenerateGitIgnoreContent(SolutionType solutionType)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Build outputs");
        sb.AppendLine("bin/");
        sb.AppendLine("obj/");
        sb.AppendLine("BuildOutput/");
        sb.AppendLine("out/");
        sb.AppendLine();
        sb.AppendLine("# IDE");
        sb.AppendLine(".vs/");
        sb.AppendLine(".vscode/");
        sb.AppendLine(".idea/");
        sb.AppendLine("*.user");
        sb.AppendLine("*.suo");
        sb.AppendLine();
        sb.AppendLine("# OS");
        sb.AppendLine(".DS_Store");
        sb.AppendLine("Thumbs.db");
        sb.AppendLine();

        if (solutionType.Id == "dotnet" || solutionType.Id == "msil")
        {
            sb.AppendLine("# .NET");
            sb.AppendLine("*.dll");
            sb.AppendLine("*.exe");
            sb.AppendLine("*.pdb");
            sb.AppendLine("packages/");
            sb.AppendLine();
        }

        if (solutionType.Id == "native" || solutionType.Id == "cpp")
        {
            sb.AppendLine("# C++");
            sb.AppendLine("*.o");
            sb.AppendLine("*.obj");
            sb.AppendLine("*.lib");
            sb.AppendLine("*.a");
            sb.AppendLine();
        }

        if (solutionType.Id == "llvm")
        {
            sb.AppendLine("# LLVM");
            sb.AppendLine("*.ll");
            sb.AppendLine("*.bc");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    #endregion
}
