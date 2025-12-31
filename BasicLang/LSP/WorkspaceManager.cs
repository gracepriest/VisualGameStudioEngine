using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Compiler.ProjectSystem;

namespace BasicLang.Compiler.LSP
{
    /// <summary>
    /// Manages workspace state including multiple projects, cross-project references,
    /// and coordination between the LSP and compiler infrastructure.
    /// </summary>
    public class WorkspaceManager
    {
        private readonly Dictionary<string, ProjectContext> _projects;
        private readonly Dictionary<string, string> _fileToProject;
        private readonly TypeRegistry _typeRegistry;
        private readonly object _lock = new object();
        private string _workspaceRoot;
        private SolutionFile _solution;

        public event EventHandler<ProjectChangedEventArgs> ProjectChanged;
        public event EventHandler<WorkspaceChangedEventArgs> WorkspaceChanged;

        public WorkspaceManager()
        {
            _projects = new Dictionary<string, ProjectContext>(StringComparer.OrdinalIgnoreCase);
            _fileToProject = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _typeRegistry = new TypeRegistry();

            // Initialize type registry with .NET reference assemblies
            InitializeTypeRegistry();
        }

        public string WorkspaceRoot => _workspaceRoot;
        public TypeRegistry TypeRegistry => _typeRegistry;
        public IReadOnlyDictionary<string, ProjectContext> Projects => _projects;

        /// <summary>
        /// Initialize the workspace from a root folder
        /// </summary>
        public async Task InitializeAsync(string rootPath)
        {
            _workspaceRoot = rootPath;

            // Look for solution file first
            var slnFiles = Directory.GetFiles(rootPath, "*.sln", SearchOption.TopDirectoryOnly);
            if (slnFiles.Length > 0)
            {
                await LoadSolutionAsync(slnFiles[0]);
                return;
            }

            // Look for project files
            var projFiles = Directory.GetFiles(rootPath, "*.blproj", SearchOption.AllDirectories);
            foreach (var projFile in projFiles)
            {
                await LoadProjectAsync(projFile);
            }

            // If no projects found, treat as single-file workspace
            if (_projects.Count == 0)
            {
                var sourceFiles = Directory.GetFiles(rootPath, "*.bas", SearchOption.AllDirectories)
                    .Concat(Directory.GetFiles(rootPath, "*.bl", SearchOption.AllDirectories))
                    .ToList();

                if (sourceFiles.Any())
                {
                    // Create an implicit project
                    var implicitProject = CreateImplicitProject(rootPath, sourceFiles);
                    _projects[implicitProject.ProjectPath] = implicitProject;
                }
            }

            WorkspaceChanged?.Invoke(this, new WorkspaceChangedEventArgs
            {
                ChangeType = WorkspaceChangeType.Initialized,
                WorkspaceRoot = rootPath
            });
        }

        /// <summary>
        /// Load a solution file
        /// </summary>
        public async Task LoadSolutionAsync(string solutionPath)
        {
            _solution = SolutionFile.Load(solutionPath);
            _workspaceRoot = Path.GetDirectoryName(solutionPath);

            foreach (var projectRef in _solution.Projects)
            {
                var fullPath = Path.GetFullPath(Path.Combine(_workspaceRoot, projectRef.Path));
                if (File.Exists(fullPath))
                {
                    await LoadProjectAsync(fullPath);
                }
            }
        }

        /// <summary>
        /// Load a project file
        /// </summary>
        public async Task LoadProjectAsync(string projectPath)
        {
            projectPath = Path.GetFullPath(projectPath);

            if (_projects.ContainsKey(projectPath))
                return;

            var projectFile = ProjectFile.Load(projectPath);
            var projectDir = Path.GetDirectoryName(projectPath);

            var context = new ProjectContext
            {
                ProjectPath = projectPath,
                ProjectFile = projectFile,
                ProjectDirectory = projectDir,
                SourceFiles = new Dictionary<string, SourceFileState>(StringComparer.OrdinalIgnoreCase),
                References = new List<string>(),
                PackageTypes = new Dictionary<string, NetTypeInfo>(StringComparer.OrdinalIgnoreCase)
            };

            // Index source files
            foreach (var sourceFile in projectFile.GetSourceFiles())
            {
                var fullPath = Path.GetFullPath(sourceFile);
                context.SourceFiles[fullPath] = new SourceFileState
                {
                    FilePath = fullPath,
                    LastModified = File.GetLastWriteTimeUtc(fullPath)
                };
                _fileToProject[fullPath] = projectPath;
            }

            // Load project references
            foreach (var projRef in projectFile.ProjectReferences)
            {
                var refPath = Path.GetFullPath(Path.Combine(projectDir, projRef));
                context.References.Add(refPath);

                // Ensure referenced project is loaded
                if (!_projects.ContainsKey(refPath))
                {
                    await LoadProjectAsync(refPath);
                }
            }

            // Load package references and their types
            await LoadPackageTypesAsync(context);

            lock (_lock)
            {
                _projects[projectPath] = context;
            }

            ProjectChanged?.Invoke(this, new ProjectChangedEventArgs
            {
                ChangeType = ProjectChangeType.Added,
                ProjectPath = projectPath,
                Context = context
            });
        }

        /// <summary>
        /// Load types from NuGet packages referenced by the project
        /// </summary>
        private async Task LoadPackageTypesAsync(ProjectContext context)
        {
            var packageDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".basiclang", "packages");

            foreach (var pkgRef in context.ProjectFile.PackageReferences)
            {
                var pkgPath = Path.Combine(packageDir, pkgRef.Name.ToLowerInvariant(), pkgRef.Version);
                if (!Directory.Exists(pkgPath))
                    continue;

                // Find lib folder for target framework
                var libPath = FindLibFolder(pkgPath, context.ProjectFile.TargetFramework);
                if (libPath == null)
                    continue;

                // Add to type registry search paths
                _typeRegistry.AddSearchPath(libPath);

                // Load types from assemblies in this package
                var assemblies = Directory.GetFiles(libPath, "*.dll");
                foreach (var assembly in assemblies)
                {
                    try
                    {
                        var types = _typeRegistry.LoadTypesFromAssembly(assembly);
                        foreach (var type in types)
                        {
                            context.PackageTypes[type.FullName] = type;
                        }
                    }
                    catch
                    {
                        // Skip assemblies that can't be loaded
                    }
                }
            }
        }

        private string FindLibFolder(string packagePath, string targetFramework)
        {
            var libPath = Path.Combine(packagePath, "lib");
            if (!Directory.Exists(libPath))
                return null;

            // Try exact match first
            var exactPath = Path.Combine(libPath, targetFramework);
            if (Directory.Exists(exactPath))
                return exactPath;

            // Try compatible frameworks
            var compatibleFrameworks = new[]
            {
                targetFramework,
                "net8.0", "net7.0", "net6.0", "net5.0",
                "netstandard2.1", "netstandard2.0",
                "netcoreapp3.1", "netcoreapp3.0"
            };

            foreach (var fw in compatibleFrameworks)
            {
                var fwPath = Path.Combine(libPath, fw);
                if (Directory.Exists(fwPath))
                    return fwPath;
            }

            // Return first available
            var dirs = Directory.GetDirectories(libPath);
            return dirs.FirstOrDefault();
        }

        /// <summary>
        /// Get the project context for a source file
        /// </summary>
        public ProjectContext GetProjectForFile(string filePath)
        {
            filePath = Path.GetFullPath(filePath);

            if (_fileToProject.TryGetValue(filePath, out var projectPath))
            {
                if (_projects.TryGetValue(projectPath, out var context))
                {
                    return context;
                }
            }

            return null;
        }

        /// <summary>
        /// Get all source files across all projects
        /// </summary>
        public IEnumerable<string> GetAllSourceFiles()
        {
            return _projects.Values.SelectMany(p => p.SourceFiles.Keys);
        }

        /// <summary>
        /// Get types available in a project (including package types and referenced projects)
        /// </summary>
        public IEnumerable<NetTypeInfo> GetAvailableTypes(ProjectContext project)
        {
            // Types from packages
            foreach (var type in project.PackageTypes.Values)
            {
                yield return type;
            }

            // Types from referenced projects
            foreach (var refPath in project.References)
            {
                if (_projects.TryGetValue(refPath, out var refProject))
                {
                    foreach (var type in refProject.PackageTypes.Values)
                    {
                        yield return type;
                    }
                }
            }

            // Types from loaded namespaces in type registry
            foreach (var type in _typeRegistry.GetAllLoadedTypes())
            {
                yield return type;
            }
        }

        /// <summary>
        /// Notify that a file has changed
        /// </summary>
        public void OnFileChanged(string filePath)
        {
            var project = GetProjectForFile(filePath);
            if (project != null && project.SourceFiles.TryGetValue(filePath, out var state))
            {
                state.LastModified = DateTime.UtcNow;
                state.IsDirty = true;

                ProjectChanged?.Invoke(this, new ProjectChangedEventArgs
                {
                    ChangeType = ProjectChangeType.FileChanged,
                    ProjectPath = project.ProjectPath,
                    Context = project,
                    ChangedFile = filePath
                });
            }
        }

        /// <summary>
        /// Add a new file to a project
        /// </summary>
        public void AddFileToProject(string projectPath, string filePath)
        {
            if (!_projects.TryGetValue(projectPath, out var project))
                return;

            filePath = Path.GetFullPath(filePath);
            project.SourceFiles[filePath] = new SourceFileState
            {
                FilePath = filePath,
                LastModified = File.Exists(filePath) ? File.GetLastWriteTimeUtc(filePath) : DateTime.UtcNow
            };
            _fileToProject[filePath] = projectPath;

            ProjectChanged?.Invoke(this, new ProjectChangedEventArgs
            {
                ChangeType = ProjectChangeType.FileAdded,
                ProjectPath = projectPath,
                Context = project,
                ChangedFile = filePath
            });
        }

        /// <summary>
        /// Remove a file from a project
        /// </summary>
        public void RemoveFileFromProject(string projectPath, string filePath)
        {
            if (!_projects.TryGetValue(projectPath, out var project))
                return;

            filePath = Path.GetFullPath(filePath);
            project.SourceFiles.Remove(filePath);
            _fileToProject.Remove(filePath);

            ProjectChanged?.Invoke(this, new ProjectChangedEventArgs
            {
                ChangeType = ProjectChangeType.FileRemoved,
                ProjectPath = projectPath,
                Context = project,
                ChangedFile = filePath
            });
        }

        private ProjectContext CreateImplicitProject(string rootPath, List<string> sourceFiles)
        {
            var context = new ProjectContext
            {
                ProjectPath = Path.Combine(rootPath, "_implicit.blproj"),
                ProjectDirectory = rootPath,
                IsImplicit = true,
                SourceFiles = new Dictionary<string, SourceFileState>(StringComparer.OrdinalIgnoreCase),
                References = new List<string>(),
                PackageTypes = new Dictionary<string, NetTypeInfo>(StringComparer.OrdinalIgnoreCase)
            };

            foreach (var file in sourceFiles)
            {
                var fullPath = Path.GetFullPath(file);
                context.SourceFiles[fullPath] = new SourceFileState
                {
                    FilePath = fullPath,
                    LastModified = File.GetLastWriteTimeUtc(fullPath)
                };
                _fileToProject[fullPath] = context.ProjectPath;
            }

            return context;
        }

        private void InitializeTypeRegistry()
        {
            // Add .NET reference assemblies
            var dotnetPath = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            if (string.IsNullOrEmpty(dotnetPath))
            {
                dotnetPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet");
            }

            // Find ref assemblies for .NET 8
            var refPath = Path.Combine(dotnetPath, "packs", "Microsoft.NETCore.App.Ref");
            if (Directory.Exists(refPath))
            {
                var versions = Directory.GetDirectories(refPath)
                    .OrderByDescending(d => d)
                    .FirstOrDefault();

                if (versions != null)
                {
                    var netPath = Path.Combine(versions, "ref", "net8.0");
                    if (!Directory.Exists(netPath))
                        netPath = Path.Combine(versions, "ref", "net7.0");
                    if (!Directory.Exists(netPath))
                        netPath = Path.Combine(versions, "ref", "net6.0");

                    if (Directory.Exists(netPath))
                    {
                        _typeRegistry.AddSearchPath(netPath);
                    }
                }
            }

            // Try to load from cache, otherwise build index
            if (!_typeRegistry.LoadIndexFromCache())
            {
                _typeRegistry.BuildIndex();
            }
        }
    }

    /// <summary>
    /// Represents a project's context within the workspace
    /// </summary>
    public class ProjectContext
    {
        public string ProjectPath { get; set; }
        public string ProjectDirectory { get; set; }
        public ProjectFile ProjectFile { get; set; }
        public bool IsImplicit { get; set; }
        public Dictionary<string, SourceFileState> SourceFiles { get; set; }
        public List<string> References { get; set; }
        public Dictionary<string, NetTypeInfo> PackageTypes { get; set; }

        // Compilation state
        public Compiler.BasicCompiler Compiler { get; set; }
        public IR.IRModule LastIR { get; set; }
        public List<SemanticError> LastErrors { get; set; } = new List<SemanticError>();
    }

    /// <summary>
    /// Represents the state of a source file
    /// </summary>
    public class SourceFileState
    {
        public string FilePath { get; set; }
        public DateTime LastModified { get; set; }
        public bool IsDirty { get; set; }
        public AST.ProgramNode AST { get; set; }
        public List<SemanticError> Errors { get; set; } = new List<SemanticError>();
    }

    /// <summary>
    /// Represents a solution file (.sln)
    /// </summary>
    public class SolutionFile
    {
        public string FilePath { get; set; }
        public string Name { get; set; }
        public List<SolutionProjectReference> Projects { get; set; } = new List<SolutionProjectReference>();

        public static SolutionFile Load(string path)
        {
            var solution = new SolutionFile
            {
                FilePath = path,
                Name = Path.GetFileNameWithoutExtension(path)
            };

            // Parse simple solution format
            // Project("{GUID}") = "Name", "Path", "{GUID}"
            foreach (var line in File.ReadAllLines(path))
            {
                if (line.TrimStart().StartsWith("Project("))
                {
                    var parts = line.Split('"');
                    if (parts.Length >= 6)
                    {
                        solution.Projects.Add(new SolutionProjectReference
                        {
                            Name = parts[3],
                            Path = parts[5]
                        });
                    }
                }
            }

            return solution;
        }

        public void Save()
        {
            var lines = new List<string>
            {
                "Microsoft Visual Studio Solution File, Format Version 12.00",
                "# BasicLang Solution",
                ""
            };

            foreach (var proj in Projects)
            {
                var guid = Guid.NewGuid().ToString("B").ToUpperInvariant();
                lines.Add($"Project(\"{guid}\") = \"{proj.Name}\", \"{proj.Path}\", \"{guid}\"");
                lines.Add("EndProject");
            }

            lines.Add("Global");
            lines.Add("EndGlobal");

            File.WriteAllLines(FilePath, lines);
        }
    }

    public class SolutionProjectReference
    {
        public string Name { get; set; }
        public string Path { get; set; }
    }

    public enum ProjectChangeType
    {
        Added,
        Removed,
        FileAdded,
        FileRemoved,
        FileChanged,
        ReferencesChanged
    }

    public class ProjectChangedEventArgs : EventArgs
    {
        public ProjectChangeType ChangeType { get; set; }
        public string ProjectPath { get; set; }
        public ProjectContext Context { get; set; }
        public string ChangedFile { get; set; }
    }

    public enum WorkspaceChangeType
    {
        Initialized,
        ProjectAdded,
        ProjectRemoved,
        Closed
    }

    public class WorkspaceChangedEventArgs : EventArgs
    {
        public WorkspaceChangeType ChangeType { get; set; }
        public string WorkspaceRoot { get; set; }
    }
}
