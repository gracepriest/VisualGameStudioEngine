using System;
using System.Collections.Generic;
using System.Linq;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.SemanticAnalysis;

namespace BasicLang.Compiler
{
    /// <summary>
    /// Manages symbols across all files in a project for multi-file compilation.
    /// Provides lookup for qualified names (ModuleName.Symbol) and tracks public/private visibility.
    /// </summary>
    public class ProjectSymbolTable
    {
        private readonly Dictionary<string, ModuleSymbols> _modules = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Register a module and its symbols
        /// </summary>
        public void RegisterModule(string moduleName, ModuleSymbols symbols)
        {
            _modules[moduleName] = symbols;
        }

        /// <summary>
        /// Check if a module exists
        /// </summary>
        public bool HasModule(string moduleName)
        {
            return _modules.ContainsKey(moduleName);
        }

        /// <summary>
        /// Get a module by name
        /// </summary>
        public ModuleSymbols GetModule(string moduleName)
        {
            return _modules.TryGetValue(moduleName, out var module) ? module : null;
        }

        /// <summary>
        /// Get all module names
        /// </summary>
        public IEnumerable<string> GetModuleNames()
        {
            return _modules.Keys;
        }

        /// <summary>
        /// Lookup a symbol with qualified name: "ModuleName.Symbol"
        /// </summary>
        public Symbol LookupQualified(string moduleName, string symbolName)
        {
            if (_modules.TryGetValue(moduleName, out var module))
            {
                return module.GetPublicSymbol(symbolName);
            }
            return null;
        }

        /// <summary>
        /// Try to resolve a potentially qualified name.
        /// Returns the symbol and whether it was found.
        /// </summary>
        public (Symbol symbol, string moduleName) ResolveQualifiedName(string name)
        {
            // Check if it's a qualified name (ModuleName.Symbol)
            var dotIndex = name.IndexOf('.');
            if (dotIndex > 0)
            {
                var moduleName = name.Substring(0, dotIndex);
                var symbolName = name.Substring(dotIndex + 1);

                var symbol = LookupQualified(moduleName, symbolName);
                if (symbol != null)
                {
                    return (symbol, moduleName);
                }
            }

            return (null, null);
        }

        /// <summary>
        /// Get all public symbols from a module (for Import shorthand)
        /// </summary>
        public IEnumerable<Symbol> GetPublicSymbols(string moduleName)
        {
            if (_modules.TryGetValue(moduleName, out var module))
            {
                return module.GetAllPublicSymbols();
            }
            return Enumerable.Empty<Symbol>();
        }

        /// <summary>
        /// Get all public symbols from all modules
        /// </summary>
        public IEnumerable<(string moduleName, Symbol symbol)> GetAllPublicSymbols()
        {
            foreach (var kvp in _modules)
            {
                foreach (var symbol in kvp.Value.GetAllPublicSymbols())
                {
                    yield return (kvp.Key, symbol);
                }
            }
        }

        /// <summary>
        /// Clear all modules
        /// </summary>
        public void Clear()
        {
            _modules.Clear();
        }
    }

    /// <summary>
    /// Stores symbols for a single module/file
    /// </summary>
    public class ModuleSymbols
    {
        public string ModuleName { get; set; }
        public string FilePath { get; set; }
        public bool IsModuleFile { get; set; }  // True for .mod files (default public)

        private readonly Dictionary<string, Symbol> _publicSymbols = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Symbol> _privateSymbols = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Symbol> _friendSymbols = new(StringComparer.OrdinalIgnoreCase);

        public ModuleSymbols(string moduleName, string filePath = null)
        {
            ModuleName = moduleName;
            FilePath = filePath;
        }

        /// <summary>
        /// Add a symbol with the specified access level
        /// </summary>
        public void AddSymbol(Symbol symbol, AccessModifier access)
        {
            switch (access)
            {
                case AccessModifier.Public:
                    _publicSymbols[symbol.Name] = symbol;
                    break;
                case AccessModifier.Friend:
                    _friendSymbols[symbol.Name] = symbol;
                    break;
                default:
                    _privateSymbols[symbol.Name] = symbol;
                    break;
            }
        }

        /// <summary>
        /// Get a public symbol by name
        /// </summary>
        public Symbol GetPublicSymbol(string name)
        {
            return _publicSymbols.TryGetValue(name, out var s) ? s : null;
        }

        /// <summary>
        /// Get a friend symbol by name (internal to project)
        /// </summary>
        public Symbol GetFriendSymbol(string name)
        {
            // Friend symbols are also accessible
            if (_friendSymbols.TryGetValue(name, out var s))
                return s;
            // Public symbols are accessible as friend too
            return GetPublicSymbol(name);
        }

        /// <summary>
        /// Get any symbol by name (for use within the same module)
        /// </summary>
        public Symbol GetSymbol(string name)
        {
            if (_privateSymbols.TryGetValue(name, out var s))
                return s;
            if (_friendSymbols.TryGetValue(name, out s))
                return s;
            if (_publicSymbols.TryGetValue(name, out s))
                return s;
            return null;
        }

        /// <summary>
        /// Get all public symbols
        /// </summary>
        public IEnumerable<Symbol> GetAllPublicSymbols()
        {
            return _publicSymbols.Values;
        }

        /// <summary>
        /// Get all friend symbols (includes public)
        /// </summary>
        public IEnumerable<Symbol> GetAllFriendSymbols()
        {
            return _publicSymbols.Values.Concat(_friendSymbols.Values);
        }

        /// <summary>
        /// Get all symbols
        /// </summary>
        public IEnumerable<Symbol> GetAllSymbols()
        {
            return _publicSymbols.Values
                .Concat(_friendSymbols.Values)
                .Concat(_privateSymbols.Values);
        }

        /// <summary>
        /// Check if a symbol exists (any access level)
        /// </summary>
        public bool HasSymbol(string name)
        {
            return _publicSymbols.ContainsKey(name) ||
                   _friendSymbols.ContainsKey(name) ||
                   _privateSymbols.ContainsKey(name);
        }

        /// <summary>
        /// Get the count of all symbols
        /// </summary>
        public int SymbolCount =>
            _publicSymbols.Count + _friendSymbols.Count + _privateSymbols.Count;
    }
}
