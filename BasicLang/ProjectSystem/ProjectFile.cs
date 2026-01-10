using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        // Compiler options
        public bool OptimizationsEnabled { get; set; } = true;
        public bool DebugSymbols { get; set; } = true;
        public string Backend { get; set; } = "CSharp"; // CSharp, MSIL, LLVM

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

                var optimize = propertyGroup.Element("Optimize")?.Value;
                if (optimize != null) project.OptimizationsEnabled = bool.Parse(optimize);

                var debug = propertyGroup.Element("DebugSymbols")?.Value;
                if (debug != null) project.DebugSymbols = bool.Parse(debug);
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
                new XElement("Project",
                    new XAttribute("Sdk", "BasicLang.Sdk"),

                    // Main PropertyGroup
                    new XElement("PropertyGroup",
                        new XElement("OutputType", OutputType),
                        new XElement("TargetFramework", TargetFramework),
                        string.IsNullOrEmpty(ProjectName) ? null : new XElement("ProjectName", ProjectName),
                        string.IsNullOrEmpty(RootNamespace) ? null : new XElement("RootNamespace", RootNamespace),
                        string.IsNullOrEmpty(AssemblyName) ? null : new XElement("AssemblyName", AssemblyName),
                        new XElement("Version", Version),
                        string.IsNullOrEmpty(Authors) ? null : new XElement("Authors", Authors),
                        string.IsNullOrEmpty(Description) ? null : new XElement("Description", Description),
                        new XElement("Backend", Backend),
                        new XElement("Optimize", OptimizationsEnabled),
                        new XElement("DebugSymbols", DebugSymbols)
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

            // Remove null elements
            doc.Descendants().Where(e => e.IsEmpty && !e.HasAttributes).Remove();

            doc.Save(path);
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
                // Default: all .bas and .bl files
                foreach (var file in Directory.GetFiles(projectDir, "*.bas", SearchOption.AllDirectories))
                    yield return file;
                foreach (var file in Directory.GetFiles(projectDir, "*.bl", SearchOption.AllDirectories))
                    yield return file;
                foreach (var file in Directory.GetFiles(projectDir, "*.basic", SearchOption.AllDirectories))
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
