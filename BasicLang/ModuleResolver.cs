using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BasicLang.Compiler
{
    /// <summary>
    /// Resolves module names and paths to actual file locations
    /// Supports both VB-style module names and path-based imports
    /// </summary>
    public class ModuleResolver
    {
        private readonly List<string> _searchPaths;
        private static readonly string[] SupportedExtensions = { ".bas", ".bl", ".basic" };

        public IReadOnlyList<string> SearchPaths => _searchPaths;

        public ModuleResolver()
        {
            _searchPaths = new List<string>();
        }

        /// <summary>
        /// Add a directory to search for modules
        /// </summary>
        public void AddSearchPath(string path)
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            {
                var fullPath = Path.GetFullPath(path);
                if (!_searchPaths.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
                {
                    _searchPaths.Add(fullPath);
                }
            }
        }

        /// <summary>
        /// Remove a search path
        /// </summary>
        public void RemoveSearchPath(string path)
        {
            var fullPath = Path.GetFullPath(path);
            _searchPaths.RemoveAll(p => p.Equals(fullPath, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Resolve a module reference to a file path
        /// </summary>
        /// <param name="moduleReference">The module name or path from import statement</param>
        /// <param name="currentFilePath">The file containing the import (for relative path resolution)</param>
        /// <returns>The resolved file path, or null if not found</returns>
        public string ResolveModule(string moduleReference, string currentFilePath = null)
        {
            if (string.IsNullOrWhiteSpace(moduleReference))
                return null;

            // Check if it's a quoted path (path-based import)
            if (IsQuotedPath(moduleReference))
            {
                return ResolvePathImport(moduleReference, currentFilePath);
            }

            // VB-style module name import
            return ResolveModuleNameImport(moduleReference, currentFilePath);
        }

        /// <summary>
        /// Check if the reference is a quoted path
        /// </summary>
        private bool IsQuotedPath(string reference)
        {
            return reference.StartsWith("\"") && reference.EndsWith("\"") ||
                   reference.StartsWith("'") && reference.EndsWith("'");
        }

        /// <summary>
        /// Resolve a path-based import (e.g., "./utils.bas", "../lib/helpers.bl")
        /// </summary>
        private string ResolvePathImport(string quotedPath, string currentFilePath)
        {
            // Remove quotes
            var path = quotedPath.Trim('"', '\'');

            // Handle absolute paths
            if (Path.IsPathRooted(path))
            {
                return File.Exists(path) ? Path.GetFullPath(path) : null;
            }

            // Handle relative paths (relative to current file)
            if (currentFilePath != null)
            {
                var currentDir = Path.GetDirectoryName(currentFilePath);
                if (!string.IsNullOrEmpty(currentDir))
                {
                    var resolvedPath = Path.GetFullPath(Path.Combine(currentDir, path));
                    if (File.Exists(resolvedPath))
                    {
                        return resolvedPath;
                    }
                }
            }

            // Try search paths
            foreach (var searchPath in _searchPaths)
            {
                var resolvedPath = Path.GetFullPath(Path.Combine(searchPath, path));
                if (File.Exists(resolvedPath))
                {
                    return resolvedPath;
                }
            }

            return null;
        }

        /// <summary>
        /// Resolve a VB-style module name import (e.g., "MyModule")
        /// </summary>
        private string ResolveModuleNameImport(string moduleName, string currentFilePath)
        {
            // Search order:
            // 1. Current directory (if currentFilePath provided)
            // 2. Search paths in order

            var searchDirs = new List<string>();

            // Add current file's directory first
            if (currentFilePath != null)
            {
                var currentDir = Path.GetDirectoryName(currentFilePath);
                if (!string.IsNullOrEmpty(currentDir))
                {
                    searchDirs.Add(currentDir);
                }
            }

            // Add configured search paths
            searchDirs.AddRange(_searchPaths);

            // Try to find module in each directory with each extension
            foreach (var dir in searchDirs)
            {
                foreach (var ext in SupportedExtensions)
                {
                    var candidatePath = Path.Combine(dir, moduleName + ext);
                    if (File.Exists(candidatePath))
                    {
                        return Path.GetFullPath(candidatePath);
                    }
                }

                // Also check for a subdirectory with the module name
                var subDir = Path.Combine(dir, moduleName);
                if (Directory.Exists(subDir))
                {
                    // Look for index file (module.bas, index.bas, etc.)
                    foreach (var ext in SupportedExtensions)
                    {
                        var indexPath = Path.Combine(subDir, moduleName + ext);
                        if (File.Exists(indexPath))
                        {
                            return Path.GetFullPath(indexPath);
                        }

                        indexPath = Path.Combine(subDir, "index" + ext);
                        if (File.Exists(indexPath))
                        {
                            return Path.GetFullPath(indexPath);
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Resolve a namespace reference (for Using statements)
        /// </summary>
        /// <param name="namespaceName">The namespace name (e.g., "System.Collections")</param>
        /// <param name="currentFilePath">The current file path for context</param>
        /// <returns>List of files that provide this namespace</returns>
        public List<string> ResolveNamespace(string namespaceName, string currentFilePath = null)
        {
            var results = new List<string>();

            // Convert namespace to directory structure (System.Collections -> System/Collections)
            var namespacePath = namespaceName.Replace('.', Path.DirectorySeparatorChar);

            var searchDirs = new List<string>();
            if (currentFilePath != null)
            {
                var currentDir = Path.GetDirectoryName(currentFilePath);
                if (!string.IsNullOrEmpty(currentDir))
                {
                    searchDirs.Add(currentDir);
                }
            }
            searchDirs.AddRange(_searchPaths);

            foreach (var dir in searchDirs)
            {
                var namespaceDir = Path.Combine(dir, namespacePath);
                if (Directory.Exists(namespaceDir))
                {
                    // Find all source files in the namespace directory
                    foreach (var ext in SupportedExtensions)
                    {
                        var pattern = "*" + ext;
                        results.AddRange(Directory.GetFiles(namespaceDir, pattern));
                    }
                }
            }

            return results.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Get a unique module identifier from a file path
        /// </summary>
        public static string GetModuleId(string filePath)
        {
            return Path.GetFullPath(filePath).ToLowerInvariant();
        }

        /// <summary>
        /// Check if a file is a valid source file
        /// </summary>
        public static bool IsSourceFile(string filePath)
        {
            var ext = Path.GetExtension(filePath);
            return SupportedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Get the module name from a file path
        /// </summary>
        public static string GetModuleName(string filePath)
        {
            return Path.GetFileNameWithoutExtension(filePath);
        }
    }
}
