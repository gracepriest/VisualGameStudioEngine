using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace BasicLang.Compiler.ProjectSystem
{
    /// <summary>
    /// Manages NuGet package operations for BasicLang projects
    /// </summary>
    public class PackageManager
    {
        private readonly HttpClient _httpClient;
        private readonly string _globalPackagesFolder;
        private readonly List<string> _packageSources;

        // NuGet API endpoints
        private const string NuGetSearchApi = "https://api.nuget.org/v3/registration5-semver1/";
        private const string NuGetPackageBaseAddress = "https://api.nuget.org/v3-flatcontainer/";

        public PackageManager()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "BasicLang-PackageManager/1.0");

            // Default global packages folder
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            _globalPackagesFolder = Path.Combine(userProfile, ".basiclang", "packages");

            _packageSources = new List<string>
            {
                "https://api.nuget.org/v3/index.json"
            };

            // Ensure packages folder exists
            Directory.CreateDirectory(_globalPackagesFolder);
        }

        /// <summary>
        /// Search for packages on NuGet
        /// </summary>
        public async Task<List<PackageSearchResult>> SearchAsync(string query, int take = 20)
        {
            var results = new List<PackageSearchResult>();

            try
            {
                var searchUrl = $"https://azuresearch-usnc.nuget.org/query?q={Uri.EscapeDataString(query)}&take={take}";
                var response = await _httpClient.GetStringAsync(searchUrl);
                var json = JsonDocument.Parse(response);

                foreach (var item in json.RootElement.GetProperty("data").EnumerateArray())
                {
                    results.Add(new PackageSearchResult
                    {
                        Id = item.GetProperty("id").GetString(),
                        Version = item.GetProperty("version").GetString(),
                        Description = item.TryGetProperty("description", out var desc) ? desc.GetString() : "",
                        Authors = item.TryGetProperty("authors", out var authors)
                            ? string.Join(", ", authors.EnumerateArray().Select(a => a.GetString()))
                            : "",
                        TotalDownloads = item.TryGetProperty("totalDownloads", out var downloads)
                            ? downloads.GetInt64()
                            : 0
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error searching packages: {ex.Message}");
            }

            return results;
        }

        /// <summary>
        /// Get available versions for a package
        /// </summary>
        public async Task<List<string>> GetVersionsAsync(string packageId)
        {
            var versions = new List<string>();

            try
            {
                var url = $"{NuGetPackageBaseAddress}{packageId.ToLowerInvariant()}/index.json";
                var response = await _httpClient.GetStringAsync(url);
                var json = JsonDocument.Parse(response);

                foreach (var version in json.RootElement.GetProperty("versions").EnumerateArray())
                {
                    versions.Add(version.GetString());
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting versions: {ex.Message}");
            }

            return versions;
        }

        /// <summary>
        /// Get the latest version of a package
        /// </summary>
        public async Task<string> GetLatestVersionAsync(string packageId)
        {
            var versions = await GetVersionsAsync(packageId);
            return versions.LastOrDefault(); // NuGet returns versions in ascending order
        }

        /// <summary>
        /// Restore all packages for a project
        /// </summary>
        public async Task<RestoreResult> RestoreAsync(ProjectFile project, string configuration = "Debug")
        {
            var result = new RestoreResult();
            var projectDir = Path.GetDirectoryName(project.FilePath) ?? ".";
            var objDir = Path.Combine(projectDir, "obj");
            Directory.CreateDirectory(objDir);

            Console.WriteLine($"Restoring packages for {project.ProjectName}...");

            foreach (var packageRef in project.PackageReferences)
            {
                try
                {
                    var version = packageRef.Version;

                    // Resolve version if it's a range or latest
                    if (version == "*" || version.StartsWith("[") || version.StartsWith("("))
                    {
                        version = await GetLatestVersionAsync(packageRef.Name);
                        if (string.IsNullOrEmpty(version))
                        {
                            result.Errors.Add($"Could not resolve version for {packageRef.Name}");
                            continue;
                        }
                    }

                    // Check if already downloaded
                    var packagePath = GetPackagePath(packageRef.Name, version);
                    if (!Directory.Exists(packagePath))
                    {
                        Console.WriteLine($"  Downloading {packageRef.Name} {version}...");
                        await DownloadPackageAsync(packageRef.Name, version);
                    }
                    else
                    {
                        Console.WriteLine($"  {packageRef.Name} {version} (cached)");
                    }

                    // Find the DLLs for this package
                    var dlls = GetPackageAssemblies(packageRef.Name, version, project.TargetFramework);
                    result.ResolvedAssemblies.AddRange(dlls);
                    result.RestoredPackages.Add(new RestoredPackage
                    {
                        Name = packageRef.Name,
                        Version = version,
                        Assemblies = dlls
                    });
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Error restoring {packageRef.Name}: {ex.Message}");
                }
            }

            // Write assets file for IDE/build system
            await WriteAssetsFileAsync(project, result, objDir);

            if (result.Errors.Count == 0)
            {
                Console.WriteLine($"Restored {result.RestoredPackages.Count} packages successfully.");
            }
            else
            {
                Console.WriteLine($"Restore completed with {result.Errors.Count} errors.");
            }

            return result;
        }

        /// <summary>
        /// Download a package from NuGet
        /// </summary>
        public async Task DownloadPackageAsync(string packageId, string version)
        {
            var packagePath = GetPackagePath(packageId, version);
            Directory.CreateDirectory(packagePath);

            var nupkgUrl = $"{NuGetPackageBaseAddress}{packageId.ToLowerInvariant()}/{version.ToLowerInvariant()}/{packageId.ToLowerInvariant()}.{version.ToLowerInvariant()}.nupkg";

            var nupkgPath = Path.Combine(packagePath, $"{packageId}.{version}.nupkg");

            using (var response = await _httpClient.GetAsync(nupkgUrl))
            {
                response.EnsureSuccessStatusCode();
                using (var fs = File.Create(nupkgPath))
                {
                    await response.Content.CopyToAsync(fs);
                }
            }

            // Extract the nupkg (it's a zip file)
            ZipFile.ExtractToDirectory(nupkgPath, packagePath, overwriteFiles: true);
        }

        /// <summary>
        /// Get the path where a package is stored
        /// </summary>
        public string GetPackagePath(string packageId, string version)
        {
            return Path.Combine(_globalPackagesFolder, packageId.ToLowerInvariant(), version.ToLowerInvariant());
        }

        /// <summary>
        /// Get the assemblies (DLLs) from a package for a target framework
        /// </summary>
        public List<string> GetPackageAssemblies(string packageId, string version, string targetFramework)
        {
            var assemblies = new List<string>();
            var packagePath = GetPackagePath(packageId, version);
            var libPath = Path.Combine(packagePath, "lib");

            if (!Directory.Exists(libPath))
                return assemblies;

            // Find the best matching framework folder
            var frameworkFolders = Directory.GetDirectories(libPath)
                .Select(Path.GetFileName)
                .ToList();

            var bestMatch = FindBestFrameworkMatch(frameworkFolders, targetFramework);

            if (!string.IsNullOrEmpty(bestMatch))
            {
                var frameworkPath = Path.Combine(libPath, bestMatch);
                assemblies.AddRange(Directory.GetFiles(frameworkPath, "*.dll"));
            }

            return assemblies;
        }

        private string FindBestFrameworkMatch(List<string> available, string target)
        {
            // Priority order for net8.0 target
            var priorities = new[]
            {
                target,                    // net8.0
                "net7.0", "net6.0",        // Lower .NET versions
                "netstandard2.1", "netstandard2.0", "netstandard1.6",
                "netcoreapp3.1", "netcoreapp3.0", "netcoreapp2.1",
                "net48", "net472", "net471", "net47", "net462", "net461", "net46", "net45"
            };

            foreach (var priority in priorities)
            {
                var match = available.FirstOrDefault(f =>
                    f.Equals(priority, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    return match;
            }

            return available.FirstOrDefault();
        }

        /// <summary>
        /// Add a package to a project
        /// </summary>
        public async Task<bool> AddPackageAsync(ProjectFile project, string packageId, string version = null)
        {
            // Get latest version if not specified
            if (string.IsNullOrEmpty(version))
            {
                version = await GetLatestVersionAsync(packageId);
                if (string.IsNullOrEmpty(version))
                {
                    Console.WriteLine($"Package '{packageId}' not found.");
                    return false;
                }
            }

            // Check if package exists
            var versions = await GetVersionsAsync(packageId);
            if (!versions.Contains(version, StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine($"Version '{version}' not found for package '{packageId}'.");
                return false;
            }

            // Add to project
            project.AddPackage(packageId, version);
            project.Save();

            Console.WriteLine($"Added {packageId} {version} to {Path.GetFileName(project.FilePath)}");

            // Restore the package
            await RestoreAsync(project);

            return true;
        }

        /// <summary>
        /// Remove a package from a project
        /// </summary>
        public bool RemovePackage(ProjectFile project, string packageId)
        {
            if (project.RemovePackage(packageId))
            {
                project.Save();
                Console.WriteLine($"Removed {packageId} from {Path.GetFileName(project.FilePath)}");
                return true;
            }

            Console.WriteLine($"Package '{packageId}' not found in project.");
            return false;
        }

        /// <summary>
        /// List installed packages
        /// </summary>
        public void ListPackages(ProjectFile project)
        {
            if (project.PackageReferences.Count == 0)
            {
                Console.WriteLine("No packages installed.");
                return;
            }

            Console.WriteLine("Installed packages:");
            foreach (var pkg in project.PackageReferences)
            {
                Console.WriteLine($"  {pkg.Name} ({pkg.Version})");
            }
        }

        private async Task WriteAssetsFileAsync(ProjectFile project, RestoreResult result, string objDir)
        {
            var assetsPath = Path.Combine(objDir, "project.assets.json");

            var assets = new
            {
                version = 3,
                targets = new Dictionary<string, object>
                {
                    [project.TargetFramework] = result.RestoredPackages.ToDictionary(
                        p => $"{p.Name}/{p.Version}",
                        p => new
                        {
                            type = "package",
                            compile = p.Assemblies.ToDictionary(
                                a => Path.GetFileName(a),
                                a => new { }
                            ),
                            runtime = p.Assemblies.ToDictionary(
                                a => Path.GetFileName(a),
                                a => new { }
                            )
                        }
                    )
                },
                libraries = result.RestoredPackages.ToDictionary(
                    p => $"{p.Name}/{p.Version}",
                    p => new
                    {
                        type = "package",
                        path = GetPackagePath(p.Name, p.Version)
                    }
                ),
                project = new
                {
                    restore = new
                    {
                        projectName = project.ProjectName,
                        projectPath = project.FilePath
                    }
                }
            };

            var json = JsonSerializer.Serialize(assets, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(assetsPath, json);
        }
    }

    /// <summary>
    /// Result of a package search
    /// </summary>
    public class PackageSearchResult
    {
        public string Id { get; set; }
        public string Version { get; set; }
        public string Description { get; set; }
        public string Authors { get; set; }
        public long TotalDownloads { get; set; }

        public override string ToString() => $"{Id} ({Version}) - {Description}";
    }

    /// <summary>
    /// Result of a package restore operation
    /// </summary>
    public class RestoreResult
    {
        public List<RestoredPackage> RestoredPackages { get; set; } = new List<RestoredPackage>();
        public List<string> ResolvedAssemblies { get; set; } = new List<string>();
        public List<string> Errors { get; set; } = new List<string>();

        public bool Success => Errors.Count == 0;
    }

    /// <summary>
    /// Information about a restored package
    /// </summary>
    public class RestoredPackage
    {
        public string Name { get; set; }
        public string Version { get; set; }
        public List<string> Assemblies { get; set; } = new List<string>();
    }
}
