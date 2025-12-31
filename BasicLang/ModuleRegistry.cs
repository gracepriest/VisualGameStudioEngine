using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BasicLang.Compiler.SemanticAnalysis;

namespace BasicLang.Compiler
{
    /// <summary>
    /// Registry for managing compiled modules
    /// Provides caching and prevents redundant compilation
    /// </summary>
    public class ModuleRegistry
    {
        private readonly Dictionary<string, CompilationUnit> _modules;
        private readonly ModuleResolver _resolver;
        private readonly object _lock = new object();

        /// <summary>
        /// Event fired when a module is compiled
        /// </summary>
        public event EventHandler<ModuleCompiledEventArgs> ModuleCompiled;

        /// <summary>
        /// Event fired when compilation fails
        /// </summary>
        public event EventHandler<ModuleErrorEventArgs> ModuleError;

        public ModuleRegistry(ModuleResolver resolver = null)
        {
            _modules = new Dictionary<string, CompilationUnit>(StringComparer.OrdinalIgnoreCase);
            _resolver = resolver ?? new ModuleResolver();
        }

        /// <summary>
        /// Get the module resolver
        /// </summary>
        public ModuleResolver Resolver => _resolver;

        /// <summary>
        /// Get all registered modules
        /// </summary>
        public IEnumerable<CompilationUnit> Modules => _modules.Values;

        /// <summary>
        /// Get a module by its path, creating if necessary
        /// </summary>
        public CompilationUnit GetOrCreate(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return null;

            var id = ModuleResolver.GetModuleId(filePath);

            lock (_lock)
            {
                if (_modules.TryGetValue(id, out var existing))
                {
                    return existing;
                }

                var unit = new CompilationUnit(filePath);
                _modules[id] = unit;
                return unit;
            }
        }

        /// <summary>
        /// Get a module by its ID
        /// </summary>
        public CompilationUnit Get(string id)
        {
            lock (_lock)
            {
                _modules.TryGetValue(id, out var unit);
                return unit;
            }
        }

        /// <summary>
        /// Check if a module is registered
        /// </summary>
        public bool Contains(string filePath)
        {
            var id = ModuleResolver.GetModuleId(filePath);
            lock (_lock)
            {
                return _modules.ContainsKey(id);
            }
        }

        /// <summary>
        /// Register a module
        /// </summary>
        public void Register(CompilationUnit unit)
        {
            if (unit == null)
                throw new ArgumentNullException(nameof(unit));

            lock (_lock)
            {
                _modules[unit.Id] = unit;
            }
        }

        /// <summary>
        /// Invalidate a module (force recompilation)
        /// </summary>
        public void Invalidate(string filePath)
        {
            var id = ModuleResolver.GetModuleId(filePath);
            lock (_lock)
            {
                if (_modules.TryGetValue(id, out var unit))
                {
                    unit.Reset();
                }
            }
        }

        /// <summary>
        /// Invalidate all modules
        /// </summary>
        public void InvalidateAll()
        {
            lock (_lock)
            {
                foreach (var unit in _modules.Values)
                {
                    unit.Reset();
                }
            }
        }

        /// <summary>
        /// Remove a module from the registry
        /// </summary>
        public bool Remove(string filePath)
        {
            var id = ModuleResolver.GetModuleId(filePath);
            lock (_lock)
            {
                return _modules.Remove(id);
            }
        }

        /// <summary>
        /// Clear all modules
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _modules.Clear();
            }
        }

        /// <summary>
        /// Get exported symbols from a module
        /// </summary>
        public IEnumerable<Symbol> GetExportedSymbols(string filePath)
        {
            var unit = Get(ModuleResolver.GetModuleId(filePath));
            return unit?.ExportedSymbols ?? Enumerable.Empty<Symbol>();
        }

        /// <summary>
        /// Find all modules that depend on a given module
        /// </summary>
        public IEnumerable<CompilationUnit> GetDependents(string filePath)
        {
            var id = ModuleResolver.GetModuleId(filePath);
            lock (_lock)
            {
                return _modules.Values
                    .Where(m => m.Dependencies.Contains(id, StringComparer.OrdinalIgnoreCase))
                    .ToList();
            }
        }

        /// <summary>
        /// Get modules that need recompilation
        /// </summary>
        public IEnumerable<CompilationUnit> GetModulesNeedingRecompilation()
        {
            lock (_lock)
            {
                return _modules.Values
                    .Where(m => m.NeedsRecompilation())
                    .ToList();
            }
        }

        /// <summary>
        /// Notify that a module was compiled
        /// </summary>
        internal void OnModuleCompiled(CompilationUnit unit)
        {
            ModuleCompiled?.Invoke(this, new ModuleCompiledEventArgs(unit));
        }

        /// <summary>
        /// Notify that a module had errors
        /// </summary>
        internal void OnModuleError(CompilationUnit unit, Exception exception = null)
        {
            ModuleError?.Invoke(this, new ModuleErrorEventArgs(unit, exception));
        }

        /// <summary>
        /// Get compilation statistics
        /// </summary>
        public RegistryStats GetStats()
        {
            lock (_lock)
            {
                return new RegistryStats
                {
                    TotalModules = _modules.Count,
                    CompiledModules = _modules.Values.Count(m => m.IsComplete),
                    ErrorModules = _modules.Values.Count(m => m.HasErrors),
                    PendingModules = _modules.Values.Count(m => m.Status == CompilationStatus.Pending)
                };
            }
        }
    }

    /// <summary>
    /// Event args for module compilation
    /// </summary>
    public class ModuleCompiledEventArgs : EventArgs
    {
        public CompilationUnit Unit { get; }

        public ModuleCompiledEventArgs(CompilationUnit unit)
        {
            Unit = unit;
        }
    }

    /// <summary>
    /// Event args for module errors
    /// </summary>
    public class ModuleErrorEventArgs : EventArgs
    {
        public CompilationUnit Unit { get; }
        public Exception Exception { get; }

        public ModuleErrorEventArgs(CompilationUnit unit, Exception exception = null)
        {
            Unit = unit;
            Exception = exception;
        }
    }

    /// <summary>
    /// Statistics about the registry
    /// </summary>
    public class RegistryStats
    {
        public int TotalModules { get; set; }
        public int CompiledModules { get; set; }
        public int ErrorModules { get; set; }
        public int PendingModules { get; set; }

        public override string ToString()
        {
            return $"Total: {TotalModules}, Compiled: {CompiledModules}, Errors: {ErrorModules}, Pending: {PendingModules}";
        }
    }
}
