using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace BasicLang.Compiler.ProjectSystem
{
    /// <summary>
    /// Represents a BasicLang project file (.blproj)
    /// </summary>
    public class ProjectFile
    {
        public string FilePath { get; set; }
        public string ProjectName { get; set; }
        public string OutputType { get; set; } = "Exe"; // Exe, Library
        public string TargetFramework { get; set; } = "net8.0";
        public string RootNamespace { get; set; }
        public string AssemblyName { get; set; }
        public string Version { get; set; } = "1.0.0";
        public string Authors { get; set; }
        public string Description { get; set; }

        // Source files (if empty, defaults to **/*.bas, **/*.bl)
        public List<string> SourceFiles { get; set; } = new List<string>();

        // Package references (NuGet)
        public List<PackageReference> PackageReferences { get; set; } = new List<PackageReference>();

        // Project references (other .blproj files)
        public List<string> ProjectReferences { get; set; } = new List<string>();

        // Assembly references (direct DLL references)
        public List<AssemblyReference> AssemblyReferences { get; set; } = new List<AssemblyReference>();

        // .NET proxy type declarations (P2a spec §7.2): <NetProxy Include="Full.Type.Name" />
        // declares that hand-written C++ in this project uses the named .NET type, whose full
        // public surface is then collected by NetSurfaceCollector. Verbatim Include values,
        // in declaration order.
        public List<string> NetProxyTypes { get; set; } = new List<string>();

        // Compiler options
        public bool OptimizationsEnabled { get; set; } = true;
        public bool DebugSymbols { get; set; } = true;
        public string Backend { get; set; } = "CSharp"; // CSharp, MSIL, LLVM

        // Project language: "BasicLang" (default) or "Cpp" (user-authored C++
        // sources compiled directly by the native toolchain — no transpile).
        public string Language { get; set; } = "BasicLang";

        public bool IsCppProject =>
            string.Equals(Language, "Cpp", StringComparison.OrdinalIgnoreCase);

        // A project builds natively (through CppProjectBuilder) when it is a
        // Language=Cpp project OR a BasicLang project targeting the C++ backend.
        public bool IsNativeProject =>
            IsCppProject ||
            (Backend != null &&
             (Backend.Equals("cpp", StringComparison.OrdinalIgnoreCase) ||
              Backend.Equals("c++", StringComparison.OrdinalIgnoreCase)));

        // C++-only settings (ignored for BasicLang projects)
        public string CppStandard { get; set; } = "c++20";

        // Native-build setting (applies to any IsNativeProject, incl. mixed)
        public string? CppToolchain { get; set; }   // "llvm" | "gcc" | "msvc"; null = machine probe

        /// <summary>
        /// The toolchain id a native BUILD of THIS project must use; null = machine probe.
        /// The single source of truth for build-toolchain policy — both build consumers read
        /// it (CppProjectBuilder's toolchain gate and the IDE BuildService's invalid-override
        /// pre-check) so the CLI and the IDE cannot drift.
        ///
        /// <para>IntelliSense emission does NOT consult this: it must not spawn a probe on the
        /// project-open path (IntelliSenseEmissionService's D2). See the toolchain gate in
        /// <c>CppProjectBuilder.EmitCore</c>, which applies the exemption.</para>
        ///
        /// <para>A BasicLang native project ALWAYS builds with MSVC (owner directive): no
        /// machine probe, never clang++ or g++, and a hand-edited <c>&lt;CppToolchain&gt;</c>
        /// does not override it — the wizard never writes that element for a BasicLang
        /// project, so the only way to reach one is by editing the file by hand.</para>
        ///
        /// <para>Pure C++ projects (<see cref="IsCppProject"/>) are deliberately untouched:
        /// their wizard offers an explicit llvm/gcc/msvc pick, and null there still means
        /// "probe this machine".</para>
        /// </summary>
        public string? EffectiveCppToolchain => IsCppProject ? CppToolchain : "msvc";
        public List<string> IncludeDirs { get; set; } = new List<string>();
        public List<string> NativeLibs { get; set; } = new List<string>();
        public List<string> Defines { get; set; } = new List<string>();

        /// <summary>File extensions treated as C++ translation units (headers are not compiled).</summary>
        public static readonly string[] CppTranslationUnitExtensions = { ".cpp", ".cc", ".cxx", ".c" };

        /// <summary>
        /// File extensions treated as BasicLang source (the default-glob set). Single
        /// source of truth shared by the default source glob below and consumers that
        /// need to tell BasicLang sources apart from C++ translation units
        /// (CppProjectBuilder's mixed-source partition, and the LSP file filter).
        /// </summary>
        /// <remarks>
        /// ⛔ NEVER add <c>.blform</c> or <c>.blwebform</c> here. This list drives the default
        /// source glob in <see cref="GetSourceFiles"/>, which feeds files straight to
        /// <c>File.ReadAllText</c> and then the BasicLang lexer. A form document is XML. It rides
        /// as an explicit <c>&lt;Compile&gt;</c> item and is partitioned out in
        /// <c>CompileProjectFiles</c> — it must never be swept in by the glob, where no
        /// <c>&lt;Compile&gt;</c> item and no diagnostic would mark its arrival.
        /// The IDE-side list (<c>VisualGameStudio.Core.Constants.FileExtensions.SourceExtensions</c>)
        /// DOES include them, and that difference is deliberate.
        /// </remarks>
        public static readonly string[] BasicLangSourceExtensions =
            { ".bas", ".bl", ".basic", ".mod", ".cls", ".class", ".bli" };   // .bli = declarations (plan 2c)

        /// <summary>
        /// Form DOCUMENT extensions — deliberately NOT in
        /// <see cref="BasicLangSourceExtensions"/>, and never to be merged into it: that list feeds
        /// the lexer and a form document is XML.
        ///
        /// <para>⛔ It still needs a glob of its own. A project with explicit
        /// <c>&lt;Compile&gt;</c> items listed its .blwebform and got pages; a project with NO
        /// explicit items — the default shape — got none, silently: the page emitter was handed
        /// GetSourceFiles(), whose glob cannot yield a form document by design, so the build
        /// succeeded and wrote no .html at all. Two project shapes, two behaviours, no diagnostic.
        /// </para>
        /// </summary>
        public static readonly string[] FormDocumentExtensions = { ".blform", ".blwebform" };

        // Windows desktop UI frameworks (require the net*-windows TFM)
        public bool UseWindowsForms { get; set; } = false;
        public bool UseWpf { get; set; } = false;

        /// <summary>
        /// &lt;ApplicationHighDpiMode&gt; — a System.Windows.Forms.HighDpiMode name, normally
        /// PerMonitorV2. Null means the project file does not say, and the csproj emitter supplies
        /// PerMonitorV2 for a WinForms build. In the legacy DPI-unaware mode the form designer's
        /// pixel coordinates and the running window's are different units.
        /// </summary>
        public string? ApplicationHighDpiMode { get; set; }

        // Build configurations
        public Dictionary<string, BuildConfiguration> Configurations { get; set; } = new Dictionary<string, BuildConfiguration>();

        public ProjectFile()
        {
            // Add default configurations
            Configurations["Debug"] = new BuildConfiguration
            {
                Name = "Debug",
                OptimizationsEnabled = false,
                DebugSymbols = true,
                DefineConstants = new List<string> { "DEBUG" }
            };
            Configurations["Release"] = new BuildConfiguration
            {
                Name = "Release",
                OptimizationsEnabled = true,
                DebugSymbols = false,
                DefineConstants = new List<string> { "RELEASE" }
            };
        }

        /// <summary>
        /// Load a project file from disk
        /// </summary>
        public static ProjectFile Load(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"Project file not found: {path}");

            var doc = XDocument.Load(path);
            var project = new ProjectFile { FilePath = path };

            var root = doc.Root;
            if (root?.Name.LocalName != "Project" && root?.Name.LocalName != "BasicLangProject")
                throw new InvalidOperationException("Invalid project file: root element must be <Project> or <BasicLangProject>");

            // Parse PropertyGroup
            var propertyGroup = root.Element("PropertyGroup");
            if (propertyGroup != null)
            {
                project.ProjectName = propertyGroup.Element("ProjectName")?.Value;
                project.OutputType = propertyGroup.Element("OutputType")?.Value ?? "Exe";
                project.TargetFramework = propertyGroup.Element("TargetFramework")?.Value ?? "net8.0";
                project.RootNamespace = propertyGroup.Element("RootNamespace")?.Value;
                project.AssemblyName = propertyGroup.Element("AssemblyName")?.Value;
                project.Version = propertyGroup.Element("Version")?.Value ?? "1.0.0";
                project.Authors = propertyGroup.Element("Authors")?.Value;
                project.Description = propertyGroup.Element("Description")?.Value;
                project.Backend = propertyGroup.Element("Backend")?.Value
                    ?? propertyGroup.Element("TargetBackend")?.Value
                    ?? "CSharp";
                project.Language = propertyGroup.Element("Language")?.Value ?? "BasicLang";
                project.CppStandard = propertyGroup.Element("CppStandard")?.Value ?? "c++20";
                project.CppToolchain = propertyGroup.Element("CppToolchain")?.Value.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(project.CppToolchain)) project.CppToolchain = null;

                var optimize = propertyGroup.Element("Optimize")?.Value;
                if (optimize != null) project.OptimizationsEnabled = bool.Parse(optimize);

                var debug = propertyGroup.Element("DebugSymbols")?.Value;
                if (debug != null) project.DebugSymbols = bool.Parse(debug);

                var useWinForms = propertyGroup.Element("UseWindowsForms")?.Value;
                if (useWinForms != null && bool.TryParse(useWinForms, out var uwf)) project.UseWindowsForms = uwf;

                var useWpf = propertyGroup.Element("UseWPF")?.Value;
                if (useWpf != null && bool.TryParse(useWpf, out var uwp)) project.UseWpf = uwp;

                var highDpiMode = propertyGroup.Element("ApplicationHighDpiMode")?.Value?.Trim();
                if (!string.IsNullOrEmpty(highDpiMode)) project.ApplicationHighDpiMode = highDpiMode;
            }

            // Parse ItemGroup for various references
            foreach (var itemGroup in root.Elements("ItemGroup"))
            {
                // Source files
                foreach (var compile in itemGroup.Elements("Compile"))
                {
                    var include = compile.Attribute("Include")?.Value;
                    if (!string.IsNullOrEmpty(include))
                        project.SourceFiles.Add(include);
                }

                // C++ include directories
                foreach (var includeDir in itemGroup.Elements("IncludeDir"))
                {
                    var include = includeDir.Attribute("Include")?.Value;
                    if (!string.IsNullOrEmpty(include))
                        project.IncludeDirs.Add(include);
                }

                // C++ native libraries to link
                foreach (var nativeLib in itemGroup.Elements("NativeLib"))
                {
                    var include = nativeLib.Attribute("Include")?.Value;
                    if (!string.IsNullOrEmpty(include))
                        project.NativeLibs.Add(include);
                }

                // C++ preprocessor defines
                foreach (var define in itemGroup.Elements("Define"))
                {
                    var include = define.Attribute("Include")?.Value;
                    if (!string.IsNullOrEmpty(include))
                        project.Defines.Add(include);
                }

                // Package references
                foreach (var packageRef in itemGroup.Elements("PackageReference"))
                {
                    var include = packageRef.Attribute("Include")?.Value;
                    var version = packageRef.Attribute("Version")?.Value ?? packageRef.Element("Version")?.Value;
                    if (!string.IsNullOrEmpty(include))
                    {
                        project.PackageReferences.Add(new PackageReference
                        {
                            Name = include,
                            Version = version ?? "*"
                        });
                    }
                }

                // Project references
                foreach (var projectRef in itemGroup.Elements("ProjectReference"))
                {
                    var include = projectRef.Attribute("Include")?.Value;
                    if (!string.IsNullOrEmpty(include))
                        project.ProjectReferences.Add(include);
                }

                // Assembly references
                foreach (var assemblyRef in itemGroup.Elements("Reference"))
                {
                    var include = assemblyRef.Attribute("Include")?.Value;
                    var hintPath = assemblyRef.Element("HintPath")?.Value;
                    if (!string.IsNullOrEmpty(include))
                    {
                        project.AssemblyReferences.Add(new AssemblyReference
                        {
                            Name = include,
                            HintPath = hintPath
                        });
                    }
                }

                // .NET proxy type declarations (P2a spec §7.2)
                foreach (var netProxy in itemGroup.Elements("NetProxy"))
                {
                    var include = netProxy.Attribute("Include")?.Value;
                    if (!string.IsNullOrEmpty(include))
                        project.NetProxyTypes.Add(include);
                }
            }

            // Parse build configurations
            foreach (var configGroup in root.Elements("PropertyGroup"))
            {
                var condition = configGroup.Attribute("Condition")?.Value;
                if (condition != null && condition.Contains("$(Configuration)"))
                {
                    var configName = ExtractConfigurationName(condition);
                    if (!string.IsNullOrEmpty(configName))
                    {
                        var config = new BuildConfiguration { Name = configName };

                        var optimize = configGroup.Element("Optimize")?.Value;
                        if (optimize != null) config.OptimizationsEnabled = bool.Parse(optimize);

                        var debug = configGroup.Element("DebugSymbols")?.Value;
                        if (debug != null) config.DebugSymbols = bool.Parse(debug);

                        var constants = configGroup.Element("DefineConstants")?.Value;
                        if (constants != null)
                            config.DefineConstants = constants.Split(';').ToList();

                        project.Configurations[configName] = config;
                    }
                }
            }

            // Default project name from file name
            if (string.IsNullOrEmpty(project.ProjectName))
                project.ProjectName = Path.GetFileNameWithoutExtension(path);

            if (string.IsNullOrEmpty(project.AssemblyName))
                project.AssemblyName = project.ProjectName;

            if (string.IsNullOrEmpty(project.RootNamespace))
                project.RootNamespace = project.ProjectName;

            return project;
        }

        private static string ExtractConfigurationName(string condition)
        {
            // Extract from: '$(Configuration)' == 'Debug'
            var match = System.Text.RegularExpressions.Regex.Match(
                condition, @"'\$\(Configuration\)'\s*==\s*'(\w+)'");
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>
        /// Save the project file to disk
        /// </summary>
        public void Save(string path = null)
        {
            path ??= FilePath;
            if (string.IsNullOrEmpty(path))
                throw new InvalidOperationException("No path specified for saving project file");

            var doc = new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement("BasicLangProject",
                    new XAttribute("Version", "1.0"),

                    // Main PropertyGroup
                    new XElement("PropertyGroup",
                        string.IsNullOrEmpty(ProjectName) ? null : new XElement("ProjectName", ProjectName),
                        new XElement("OutputType", OutputType),
                        string.IsNullOrEmpty(RootNamespace) ? null : new XElement("RootNamespace", RootNamespace),
                        new XElement("TargetBackend", Backend),
                        IsCppProject ? new XElement("Language", Language) : null,
                        IsCppProject ? new XElement("CppStandard", CppStandard) : null,
                        string.IsNullOrEmpty(CppToolchain) ? null : new XElement("CppToolchain", CppToolchain),
                        string.IsNullOrEmpty(Description) ? null : new XElement("Description", Description),
                        string.IsNullOrEmpty(Authors) ? null : new XElement("Authors", Authors),
                        new XElement("Version", Version)
                    )
                )
            );

            var root = doc.Root;

            // Add configuration-specific PropertyGroups
            foreach (var config in Configurations.Values)
            {
                root.Add(new XElement("PropertyGroup",
                    new XAttribute("Condition", $"'$(Configuration)' == '{config.Name}'"),
                    new XElement("Optimize", config.OptimizationsEnabled),
                    new XElement("DebugSymbols", config.DebugSymbols),
                    config.DefineConstants.Count > 0
                        ? new XElement("DefineConstants", string.Join(";", config.DefineConstants))
                        : null
                ));
            }

            // Add ItemGroup for source files (if explicitly specified)
            if (SourceFiles.Count > 0)
            {
                root.Add(new XElement("ItemGroup",
                    SourceFiles.Select(f => new XElement("Compile", new XAttribute("Include", f)))
                ));
            }

            // Add ItemGroup for package references
            if (PackageReferences.Count > 0)
            {
                root.Add(new XElement("ItemGroup",
                    PackageReferences.Select(p => new XElement("PackageReference",
                        new XAttribute("Include", p.Name),
                        new XAttribute("Version", p.Version)
                    ))
                ));
            }

            // Add ItemGroup for project references
            if (ProjectReferences.Count > 0)
            {
                root.Add(new XElement("ItemGroup",
                    ProjectReferences.Select(p => new XElement("ProjectReference",
                        new XAttribute("Include", p)
                    ))
                ));
            }

            // Add ItemGroup for assembly references
            if (AssemblyReferences.Count > 0)
            {
                root.Add(new XElement("ItemGroup",
                    AssemblyReferences.Select(a => new XElement("Reference",
                        new XAttribute("Include", a.Name),
                        string.IsNullOrEmpty(a.HintPath) ? null : new XElement("HintPath", a.HintPath)
                    ))
                ));
            }

            // Add ItemGroup for .NET proxy type declarations (P2a spec §7.2)
            if (NetProxyTypes.Count > 0)
            {
                root.Add(new XElement("ItemGroup",
                    NetProxyTypes.Select(t => new XElement("NetProxy", new XAttribute("Include", t)))
                ));
            }

            // Add ItemGroup for C++ items (include dirs, native libs, defines)
            if (IncludeDirs.Count > 0 || NativeLibs.Count > 0 || Defines.Count > 0)
            {
                root.Add(new XElement("ItemGroup",
                    IncludeDirs.Select(d => new XElement("IncludeDir", new XAttribute("Include", d))),
                    NativeLibs.Select(l => new XElement("NativeLib", new XAttribute("Include", l))),
                    Defines.Select(d => new XElement("Define", new XAttribute("Include", d)))
                ));
            }

            // Remove null elements
            doc.Descendants().Where(e => e.IsEmpty && !e.HasAttributes).Remove();

            // Write BOM-less UTF-8. XDocument.Save(path) honors the declared "utf-8"
            // encoding by emitting a UTF-8 BOM, but every .blproj/.blsln in this repo is
            // BOM-less — a BOM corrupts the file for the other tooling that reads it.
            using (var writer = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                doc.Save(writer);
            }

            FilePath = path;
        }

        /// <summary>
        /// Get all source files for this project (resolves globs)
        /// </summary>
        public IEnumerable<string> GetSourceFiles()
        {
            var projectDir = Path.GetDirectoryName(FilePath) ?? ".";

            if (SourceFiles.Count == 0)
            {
                // Default: all .bas, .bl, .basic, .mod, .cls, and .class files (same
                // patterns, same order as before — driven off the shared extension list).
                //
                // ⛔⛔ bin/ and obj/ are EXCLUDED, the same rule GetCppTranslationUnits has always
                // had. Measured: the build writes a generated VgsFormDispatch.g.bas into obj/, and
                // on the SECOND build of a glob-shaped project this walk swept it back in — the
                // build log read "Compiling VgsFormDispatch.g.bas..." TWICE, from a build that
                // still reported success. Generated sources under obj/ are the build's own output;
                // compiling your own output is never what a glob means, and the exact-extension
                // check is here for the reason the C++ one names: Win32 globbing lets "*.bas"
                // match a longer extension that merely starts with it.
                foreach (var ext in BasicLangSourceExtensions)
                    foreach (var file in Directory.GetFiles(projectDir, "*" + ext, SearchOption.AllDirectories))
                        if (string.Equals(Path.GetExtension(file), ext, StringComparison.OrdinalIgnoreCase)
                            && !IsInBuildOutputDir(projectDir, file))
                            yield return file;
            }
            else
            {
                foreach (var pattern in SourceFiles)
                {
                    var fullPattern = Path.Combine(projectDir, pattern);
                    var dir = Path.GetDirectoryName(fullPattern) ?? projectDir;
                    var filePattern = Path.GetFileName(fullPattern);

                    if (Directory.Exists(dir))
                    {
                        foreach (var file in Directory.GetFiles(dir, filePattern))
                            yield return file;
                    }
                }
            }
        }

        /// <summary>
        /// The form documents this project owns.
        ///
        /// <para>⚠ The SAME rule as <see cref="GetSourceFiles"/>, one extension list over:
        /// no explicit <c>&lt;Compile&gt;</c> items means a recursive glob, explicit items mean
        /// exactly what was listed, filtered to form extensions. Anything else makes the default
        /// project shape and the explicit one disagree about whether a form exists — which is
        /// precisely the bug this method was added for.</para>
        /// </summary>
        public IEnumerable<string> GetFormDocuments()
        {
            var projectDir = Path.GetDirectoryName(FilePath) ?? ".";

            if (SourceFiles.Count == 0)
            {
                foreach (var ext in FormDocumentExtensions)
                    foreach (var file in Directory.GetFiles(projectDir, "*" + ext, SearchOption.AllDirectories))
                        // ⚠ The bin/obj exclusion is the load-bearing one here: they live under the
                        // project directory, so an unguarded recursive glob walks the build output.
                        //
                        // The exact-extension check is DEFENSIVE ONLY, and the justification first
                        // written here was wrong: Win32's prefix over-match applies to
                        // three-character patterns (which is why "*.bas" matching `.basic` is real
                        // and matters in GetSourceFiles above), and ".blform"/".blwebform" are too
                        // long to be short-name extensions. It costs nothing and keeps the two
                        // globs the same shape, but no measurement supports needing it.
                        if (string.Equals(Path.GetExtension(file), ext, StringComparison.OrdinalIgnoreCase)
                            && !IsInBuildOutputDir(projectDir, file))
                            yield return file;

                yield break;
            }

            foreach (var file in GetSourceFiles())
            {
                if (FormDocumentExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                {
                    yield return file;
                }
            }
        }

        /// <summary>
        /// C++ translation units for a Language=Cpp project. Default (no explicit
        /// Compile items): recursive glob of TU extensions excluding bin/ and obj/
        /// (build outputs live under the project dir). Explicit Compile items are
        /// resolved by GetSourceFiles' existing rules, then filtered to TU
        /// extensions so headers can be listed without being compiled.
        /// </summary>
        public IEnumerable<string> GetCppTranslationUnits()
        {
            var projectDir = Path.GetDirectoryName(FilePath) ?? ".";

            if (SourceFiles.Count == 0)
            {
                foreach (var ext in CppTranslationUnitExtensions)
                    foreach (var file in Directory.GetFiles(projectDir, "*" + ext, SearchOption.AllDirectories))
                        // Exact-extension check: Win32 globbing lets "*.c" match
                        // longer extensions that merely start with "c".
                        if (string.Equals(Path.GetExtension(file), ext, StringComparison.OrdinalIgnoreCase)
                            && !IsInBuildOutputDir(projectDir, file))
                            yield return file;
            }
            else
            {
                foreach (var file in GetSourceFiles())
                {
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    if (Array.IndexOf(CppTranslationUnitExtensions, ext) >= 0)
                        yield return file;
                }
            }
        }

        internal static bool IsInBuildOutputDir(string projectDir, string file)
        {
            var rel = Path.GetRelativePath(projectDir, file);
            return rel.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || rel.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Add a package reference
        /// </summary>
        public void AddPackage(string name, string version)
        {
            var existing = PackageReferences.FirstOrDefault(p =>
                p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                existing.Version = version;
            }
            else
            {
                PackageReferences.Add(new PackageReference { Name = name, Version = version });
            }
        }

        /// <summary>
        /// Remove a package reference
        /// </summary>
        public bool RemovePackage(string name)
        {
            var existing = PackageReferences.FirstOrDefault(p =>
                p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                PackageReferences.Remove(existing);
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Represents a NuGet package reference
    /// </summary>
    public class PackageReference
    {
        public string Name { get; set; }
        public string Version { get; set; }
        public bool IncludeAssets { get; set; } = true;
        public bool PrivateAssets { get; set; } = false;

        public override string ToString() => $"{Name} ({Version})";
    }

    /// <summary>
    /// Represents a direct assembly reference
    /// </summary>
    public class AssemblyReference
    {
        public string Name { get; set; }
        public string HintPath { get; set; }

        public override string ToString() => Name;
    }

    /// <summary>
    /// Represents a build configuration (Debug, Release, etc.)
    /// </summary>
    public class BuildConfiguration
    {
        public string Name { get; set; }
        public bool OptimizationsEnabled { get; set; }
        public bool DebugSymbols { get; set; }
        public List<string> DefineConstants { get; set; } = new List<string>();
    }
}
