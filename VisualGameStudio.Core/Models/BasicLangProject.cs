namespace VisualGameStudio.Core.Models;

public class BasicLangProject
{
    public string Name { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string ProjectDirectory => Path.GetDirectoryName(FilePath) ?? "";
    public OutputType OutputType { get; set; } = OutputType.Exe;
    public string RootNamespace { get; set; } = "";
    public TargetBackend TargetBackend { get; set; } = TargetBackend.CSharp;
    public ProjectLanguage Language { get; set; } = ProjectLanguage.BasicLang;

    /// <summary>True when this project builds to a native binary (C++ language OR C++ backend) — routed through CppProjectBuilder.</summary>
    public bool IsNativeBuild => Language == ProjectLanguage.Cpp || TargetBackend == TargetBackend.Cpp;

    /// <summary>C++-only settings; null for BasicLang projects. Modeled so IDE saves round-trip them.</summary>
    public CppProjectSettings? CppSettings { get; set; }
    public string Version { get; set; } = "1.0";

    /// <summary>
    /// The .NET target framework moniker (<c>net8.0</c>, <c>net8.0-windows</c>, …).
    /// <c>null</c> means the project file carries no <c>&lt;TargetFramework&gt;</c> element.
    ///
    /// <para>⚠ Nullability is load-bearing for every property in this block. The serializer writes
    /// an element only when the model value differs from what the file already said, so a null here
    /// means "the file did not mention this" and leaves the file untouched. A non-nullable default
    /// would make every IDE save inject the default into projects that deliberately omit it.</para>
    /// </summary>
    public string? TargetFramework { get; set; }

    /// <summary>
    /// <c>&lt;UseWindowsForms&gt;</c>. Required for the WinForms half of the form designer, and the
    /// single property whose loss was most visible: an IDE save used to strip it from a WinForms
    /// project, after which the build no longer referenced the Windows Forms assemblies.
    /// </summary>
    public bool? UseWindowsForms { get; set; }

    /// <summary>&lt;UseWpf&gt;. Symmetric with <see cref="UseWindowsForms"/>; the VSIX ships a WPF template.</summary>
    public bool? UseWpf { get; set; }

    /// <summary>&lt;AssemblyName&gt;. Null when the file omits it (the build then falls back to the project name).</summary>
    public string? AssemblyName { get; set; }

    /// <summary>
    /// <c>&lt;ApplicationHighDpiMode&gt;</c> — a <c>System.Windows.Forms.HighDpiMode</c> name,
    /// normally <c>PerMonitorV2</c>. Null means the project file does not say, and the csproj
    /// emitters supply <c>PerMonitorV2</c> for a WinForms build.
    ///
    /// <para>Load-bearing for the form designer: in the legacy DPI-unaware mode the designer's
    /// pixel coordinates and the running window's are different units, so a form laid out at 100%
    /// comes up clipped on a scaled display.</para>
    /// </summary>
    public string? ApplicationHighDpiMode { get; set; }

    /// <summary>
    /// <see cref="TargetBackend"/> as this project was last loaded. Null for a model that was never
    /// loaded from a file.
    ///
    /// <para>⚠ Needed because <c>TargetBackend</c> is the one property whose loaded value does not
    /// come only from the file: <c>LoadAsync</c> seeds it from the IDE's
    /// <c>basiclang.compiler.backend</c> setting when the file carries no <c>&lt;TargetBackend&gt;</c>
    /// element. Without this, a preserving save re-parsing the file would see "file says nothing,
    /// model says Cpp", read that as a user edit, and inject <c>&lt;TargetBackend&gt;Cpp&lt;/TargetBackend&gt;</c>
    /// into a project the user never touched. Comparing against the value at load tells an actual
    /// edit apart from the seeded default.</para>
    /// </summary>
    public TargetBackend? BackendAtLoad { get; set; }

    public List<ProjectItem> Items { get; set; } = new();
    public List<ProjectReference> References { get; set; } = new();
    public List<PackageReference> PackageReferences { get; set; } = new();
    public Dictionary<string, BuildConfiguration> Configurations { get; set; } = new();

    public BuildConfiguration GetConfiguration(string name)
    {
        if (Configurations.TryGetValue(name, out var config))
            return config;
        return Configurations.Values.FirstOrDefault() ?? new BuildConfiguration { Name = "Debug" };
    }

    public IEnumerable<ProjectItem> GetSourceFiles()
    {
        return Items.Where(i => i.ItemType == ProjectItemType.Compile);
    }

    public string? GetMainFile()
    {
        var mainFile = Items.FirstOrDefault(i =>
            i.ItemType == ProjectItemType.Compile &&
            (i.FileName.Equals("Program.bas", StringComparison.OrdinalIgnoreCase) ||
             i.FileName.Equals("Main.bas", StringComparison.OrdinalIgnoreCase)));

        return mainFile != null ? Path.Combine(ProjectDirectory, mainFile.Include) : null;
    }
}

public enum OutputType
{
    Exe,
    Library,
    WinExe
}

public enum TargetBackend
{
    CSharp,
    Cpp,
    LLVM,
    MSIL,

    /// <summary>
    /// Emits a web site — a .js plus an index.html harness — rather than an executable.
    ///
    /// <para>Until this member existed the IDE could not REPRESENT a JavaScript project at
    /// all: <c>&lt;TargetBackend&gt;JavaScript&lt;/TargetBackend&gt;</c> failed
    /// <c>Enum.TryParse</c> in the serializer and the value silently reverted to CSharp with
    /// no diagnostic. The compiler-side <c>TargetPlatform</c> gained JavaScript in Phase 0;
    /// this is a DIFFERENT enum in a different assembly, so that did nothing for the IDE.</para>
    ///
    /// <para>Appended, never inserted — <c>Enum.TryParse</c> also accepts a numeric string, so
    /// a hand-edited <c>&lt;TargetBackend&gt;3&lt;/TargetBackend&gt;</c> parses by ordinal.
    /// Normal saves write the NAME, so round-tripping does not depend on this.</para>
    /// </summary>
    JavaScript
}

public enum ProjectLanguage
{
    BasicLang,
    Cpp
}

public class CppProjectSettings
{
    public string CppStandard { get; set; } = "c++20";
    /// <summary>Toolchain pin ("llvm" | "gcc" | "msvc", lowercase); null = machine probe.</summary>
    public string? CppToolchain { get; set; }
    public List<string> IncludeDirs { get; set; } = new();
    public List<string> NativeLibs { get; set; } = new();
    public List<string> Defines { get; set; } = new();
}
