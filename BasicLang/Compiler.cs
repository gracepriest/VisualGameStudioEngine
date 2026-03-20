using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using BasicLang.Compiler.SemanticAnalysis;

namespace BasicLang.Compiler
{
    /// <summary>
    /// Result of a compilation
    /// </summary>
    public class CompilationResult
    {
        public bool Success { get; set; }
        public List<CompilationUnit> Units { get; set; }
        public List<SemanticError> AllErrors { get; set; }
        public IRModule CombinedIR { get; set; }
        public TimeSpan Duration { get; set; }

        public CompilationResult()
        {
            Units = new List<CompilationUnit>();
            AllErrors = new List<SemanticError>();
        }

        public bool HasErrors => AllErrors.Count > 0;

        public override string ToString()
        {
            if (Success)
                return $"Compilation succeeded: {Units.Count} file(s) in {Duration.TotalMilliseconds:F0}ms";
            return $"Compilation failed with {AllErrors.Count} error(s)";
        }
    }

    /// <summary>
    /// Compiler options
    /// </summary>
    public class CompilerOptions
    {
        public bool OptimizeAggressive { get; set; } = false;
        public bool GenerateDebugInfo { get; set; } = true;
        public string OutputPath { get; set; }
        public string TargetBackend { get; set; } = "csharp";
        public List<string> SearchPaths { get; set; } = new List<string>();
    }

    /// <summary>
    /// Multi-file compiler orchestrator
    /// </summary>
    public class BasicCompiler
    {
        private readonly ModuleResolver _resolver;
        private readonly ModuleRegistry _registry;
        private readonly DependencyGraph _dependencyGraph;
        private readonly CompilerOptions _options;
        private readonly Preprocessor _preprocessor;

        public ModuleResolver Resolver => _resolver;
        public ModuleRegistry Registry => _registry;

        public BasicCompiler(CompilerOptions options = null)
        {
            _options = options ?? new CompilerOptions();
            _resolver = new ModuleResolver();
            _registry = new ModuleRegistry(_resolver);
            _dependencyGraph = new DependencyGraph();
            _preprocessor = new Preprocessor();

            // Add configured search paths
            foreach (var path in _options.SearchPaths)
            {
                _resolver.AddSearchPath(path);
                _preprocessor.AddIncludePath(path);
            }
        }

        /// <summary>
        /// Compile a single file
        /// </summary>
        public CompilationResult CompileFile(string filePath)
        {
            var startTime = DateTime.UtcNow;
            var result = new CompilationResult();

            try
            {
                // Normalize path
                filePath = Path.GetFullPath(filePath);

                if (!File.Exists(filePath))
                {
                    result.AllErrors.Add(new SemanticError($"File not found: {filePath}", 0, 0));
                    return result;
                }

                // Add current file's directory to search paths
                var fileDir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(fileDir))
                {
                    _resolver.AddSearchPath(fileDir);
                }

                // Create entry point unit
                var entryUnit = _registry.GetOrCreate(filePath);
                entryUnit.SourceCode = File.ReadAllText(filePath);
                entryUnit.LastModified = File.GetLastWriteTimeUtc(filePath);

                // Preprocess .mod files: wrap contents in implicit Module block
                PreprocessModFile(entryUnit);
                // Preprocess .cls/.class files: wrap contents in implicit Class block
                PreprocessClassFile(entryUnit);

                // Phase 1: Parse all files and collect imports
                ParseAndCollectImports(entryUnit, result);
                if (result.HasErrors) return FinalizeResult(result, startTime);

                // Phase 2: Build dependency graph and check for cycles
                BuildDependencyGraph(result);
                if (result.HasErrors) return FinalizeResult(result, startTime);

                // Phase 3: Compile in topological order
                var compilationOrder = GetCompilationOrder(entryUnit.Id, result);
                if (result.HasErrors) return FinalizeResult(result, startTime);

                // Phase 4: Compile each unit
                foreach (var moduleId in compilationOrder)
                {
                    var unit = _registry.Get(moduleId);
                    if (unit != null && !unit.IsComplete)
                    {
                        CompileUnit(unit, result);
                    }
                }

                // Phase 5: Combine IR if successful
                if (!result.HasErrors)
                {
                    result.CombinedIR = CombineIRModules(compilationOrder);

                    // Apply optimizations
                    if (result.CombinedIR != null)
                    {
                        var pipeline = new OptimizationPipeline();
                        if (_options.OptimizeAggressive)
                            pipeline.AddAggressivePasses();
                        else
                            pipeline.AddStandardPasses();
                        pipeline.Run(result.CombinedIR);
                    }

                    result.Success = true;
                }

                result.Units = _registry.Modules.ToList();
            }
            catch (CircularDependencyException ex)
            {
                result.AllErrors.Add(new SemanticError(
                    $"Circular dependency: {string.Join(" -> ", ex.Cycle)}", 0, 0));
            }
            catch (Exception ex)
            {
                result.AllErrors.Add(new SemanticError($"Compilation error: {ex.Message}", 0, 0));
            }

            return FinalizeResult(result, startTime);
        }

        /// <summary>
        /// Compile a project (multiple files)
        /// </summary>
        public CompilationResult CompileProject(string entryPoint, IEnumerable<string> additionalFiles = null)
        {
            var startTime = DateTime.UtcNow;
            var result = new CompilationResult();

            try
            {
                // Add all files to registry
                var allFiles = new List<string> { entryPoint };
                if (additionalFiles != null)
                {
                    allFiles.AddRange(additionalFiles);
                }

                foreach (var file in allFiles)
                {
                    if (File.Exists(file))
                    {
                        var unit = _registry.GetOrCreate(file);
                        unit.SourceCode = File.ReadAllText(file);
                        unit.LastModified = File.GetLastWriteTimeUtc(file);
                        PreprocessModFile(unit);
                        PreprocessClassFile(unit);
                    }
                }

                // Compile starting from entry point
                return CompileFile(entryPoint);
            }
            catch (Exception ex)
            {
                result.AllErrors.Add(new SemanticError($"Project compilation error: {ex.Message}", 0, 0));
            }

            return FinalizeResult(result, startTime);
        }

        /// <summary>
        /// Parse a file and collect its imports
        /// </summary>
        private void ParseAndCollectImports(CompilationUnit unit, CompilationResult result)
        {
            if (unit.Status != CompilationStatus.Pending)
                return;

            unit.Status = CompilationStatus.Parsing;

            try
            {
                // Preprocess the source code
                var processedSource = _preprocessor.Process(unit.SourceCode, unit.FilePath);

                // Line offset from .mod/.cls wrapper preprocessing
                int lineOffset = unit.LineOffset;

                // Check for preprocessor errors
                if (_preprocessor.Errors.Count > 0)
                {
                    foreach (var error in _preprocessor.Errors)
                    {
                        int adjustedLine = Math.Max(1, error.Line - lineOffset);
                        result.AllErrors.Add(new SemanticError(error.Message, adjustedLine, error.Column));
                    }
                }

                // Lex and parse
                var lexer = new Lexer(processedSource);
                var tokens = lexer.Tokenize();
                var parser = new Parser(tokens);
                unit.AST = parser.Parse();

                // Check for parse errors
                if (parser.Errors.Count > 0)
                {
                    foreach (var error in parser.Errors)
                    {
                        int adjustedLine = Math.Max(1, error.Token.Line - lineOffset);
                        result.AllErrors.Add(new SemanticError(error.ToString(), adjustedLine, error.Token.Column));
                    }
                    unit.Status = CompilationStatus.Error;
                    return;
                }

                // Collect imports from AST
                CollectImportsFromAST(unit, result);
            }
            catch (Exception ex)
            {
                result.AllErrors.Add(new SemanticError($"Parse error in {unit.ModuleName}: {ex.Message}", 0, 0));
                unit.Status = CompilationStatus.Error;
            }
        }

        /// <summary>
        /// Extract import directives from AST and recursively parse dependencies
        /// </summary>
        private void CollectImportsFromAST(CompilationUnit unit, CompilationResult result)
        {
            if (unit.AST == null) return;

            foreach (var decl in unit.AST.Declarations)
            {
                if (decl is ImportDirectiveNode importNode)
                {
                    var importInfo = new ImportInfo(importNode.Module, importNode.Line, importNode.Column);
                    unit.Imports.Add(importInfo);

                    // Resolve the import
                    var resolvedPath = _resolver.ResolveModule(importNode.Module, unit.FilePath);
                    if (resolvedPath != null)
                    {
                        importInfo.ResolvedPath = resolvedPath;
                        var depId = ModuleResolver.GetModuleId(resolvedPath);
                        unit.Dependencies.Add(depId);

                        // Recursively process the dependency
                        var depUnit = _registry.GetOrCreate(resolvedPath);
                        if (depUnit.Status == CompilationStatus.Pending)
                        {
                            depUnit.SourceCode = File.ReadAllText(resolvedPath);
                            depUnit.LastModified = File.GetLastWriteTimeUtc(resolvedPath);
                            PreprocessModFile(depUnit);
                            PreprocessClassFile(depUnit);
                            ParseAndCollectImports(depUnit, result);
                        }
                    }
                    else
                    {
                        result.AllErrors.Add(new SemanticError(
                            $"Cannot resolve import '{importNode.Module}'",
                            importNode.Line, importNode.Column));
                    }
                }
                else if (decl is UsingDirectiveNode usingNode)
                {
                    var usingInfo = new UsingInfo(usingNode.Namespace, usingNode.Line, usingNode.Column);
                    unit.Usings.Add(usingInfo);

                    // Resolve namespace files
                    var files = _resolver.ResolveNamespace(usingNode.Namespace, unit.FilePath);
                    usingInfo.ResolvedPaths.AddRange(files);

                    foreach (var file in files)
                    {
                        var depId = ModuleResolver.GetModuleId(file);
                        unit.Dependencies.Add(depId);

                        var depUnit = _registry.GetOrCreate(file);
                        if (depUnit.Status == CompilationStatus.Pending)
                        {
                            depUnit.SourceCode = File.ReadAllText(file);
                            depUnit.LastModified = File.GetLastWriteTimeUtc(file);
                            PreprocessModFile(depUnit);
                            PreprocessClassFile(depUnit);
                            ParseAndCollectImports(depUnit, result);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Build the dependency graph from all units
        /// </summary>
        private void BuildDependencyGraph(CompilationResult result)
        {
            _dependencyGraph.Clear();

            foreach (var unit in _registry.Modules)
            {
                _dependencyGraph.AddModule(unit.Id);

                foreach (var depId in unit.Dependencies)
                {
                    _dependencyGraph.AddDependency(unit.Id, depId);
                }
            }

            // Check for cycles
            var cycle = _dependencyGraph.DetectCycle();
            if (cycle != null)
            {
                var cycleModules = cycle.Select(id => _registry.Get(id)?.ModuleName ?? id);
                result.AllErrors.Add(new SemanticError(
                    $"Circular dependency detected: {string.Join(" -> ", cycleModules)}", 0, 0));
            }
        }

        /// <summary>
        /// Get compilation order using topological sort
        /// </summary>
        private List<string> GetCompilationOrder(string entryId, CompilationResult result)
        {
            try
            {
                return _dependencyGraph.GetCompilationOrderFor(entryId);
            }
            catch (CircularDependencyException ex)
            {
                result.AllErrors.Add(new SemanticError(ex.Message, 0, 0));
                return new List<string>();
            }
        }

        /// <summary>
        /// Compile a single unit (semantic analysis + IR generation)
        /// </summary>
        private void CompileUnit(CompilationUnit unit, CompilationResult result)
        {
            if (unit.AST == null)
            {
                result.AllErrors.Add(new SemanticError($"No AST for {unit.ModuleName}", 0, 0));
                unit.Status = CompilationStatus.Error;
                return;
            }

            unit.Status = CompilationStatus.Analyzing;

            try
            {
                // Semantic analysis
                var analyzer = new SemanticAnalyzer();
                analyzer.ConfigureModuleSystem(_registry, _resolver, unit);

                bool success = analyzer.Analyze(unit.AST);
                unit.Symbols = analyzer.GlobalScope;

                // Adjust line numbers for .mod/.cls wrapper offset
                int lineOffset = unit.LineOffset;
                if (lineOffset > 0)
                {
                    foreach (var error in analyzer.Errors)
                    {
                        error.Line = Math.Max(1, error.Line - lineOffset);
                    }
                }

                unit.Errors.AddRange(analyzer.Errors);

                if (!success)
                {
                    result.AllErrors.AddRange(analyzer.Errors);
                    unit.Status = CompilationStatus.Error;
                    return;
                }

                // Collect exported symbols (public functions, classes, etc.)
                CollectExportedSymbols(unit, analyzer.GlobalScope);

                // IR generation
                unit.Status = CompilationStatus.GeneratingIR;
                var irBuilder = new IRBuilder(analyzer);
                unit.IR = irBuilder.Build(unit.AST, unit.ModuleName, unit.FilePath);

                unit.Status = CompilationStatus.Complete;
                unit.CompletedAt = DateTime.UtcNow;

                _registry.OnModuleCompiled(unit);
            }
            catch (Exception ex)
            {
                result.AllErrors.Add(new SemanticError($"Error compiling {unit.ModuleName}: {ex.Message}", 0, 0));
                unit.Status = CompilationStatus.Error;
                _registry.OnModuleError(unit, ex);
            }
        }

        /// <summary>
        /// Collect public symbols that can be imported by other modules
        /// </summary>
        private void CollectExportedSymbols(CompilationUnit unit, Scope scope)
        {
            foreach (var symbol in scope.Symbols.Values)
            {
                // For .cls/.class files: only export the class symbol (for Import resolution)
                // The class itself carries the access modifier (Public/Private)
                if (unit.IsClassFile)
                {
                    if (symbol.Kind == SymbolKind.Class)
                    {
                        unit.ExportedSymbols.Add(symbol);
                    }
                    continue;
                }

                // Export public symbols
                if (symbol.Access == AST.AccessModifier.Public ||
                    symbol.Kind == SymbolKind.Function ||
                    symbol.Kind == SymbolKind.Subroutine ||
                    symbol.Kind == SymbolKind.Class)
                {
                    unit.ExportedSymbols.Add(symbol);
                }
            }
        }

        /// <summary>
        /// Combine IR modules from all compilation units
        /// </summary>
        private IRModule CombineIRModules(List<string> compilationOrder)
        {
            var combined = new IRModule("Combined");

            foreach (var moduleId in compilationOrder)
            {
                var unit = _registry.Get(moduleId);
                if (unit?.IR == null) continue;

                // Add functions from this module
                foreach (var func in unit.IR.Functions)
                {
                    // Prefix with module name to avoid conflicts
                    if (!combined.Functions.Any(f => f.Name == func.Name))
                    {
                        combined.Functions.Add(func);
                    }
                }

                // Add globals
                foreach (var global in unit.IR.GlobalVariables)
                {
                    if (!combined.GlobalVariables.ContainsKey(global.Key))
                    {
                        combined.GlobalVariables[global.Key] = global.Value;
                    }
                }

                // Add classes
                foreach (var cls in unit.IR.Classes)
                {
                    if (!combined.Classes.ContainsKey(cls.Key))
                    {
                        combined.Classes[cls.Key] = cls.Value;
                    }
                }

                // Add interfaces
                foreach (var iface in unit.IR.Interfaces)
                {
                    if (!combined.Interfaces.ContainsKey(iface.Key))
                    {
                        combined.Interfaces[iface.Key] = iface.Value;
                    }
                }

                // Add enums
                foreach (var enumDecl in unit.IR.Enums)
                {
                    if (!combined.Enums.ContainsKey(enumDecl.Key))
                    {
                        combined.Enums[enumDecl.Key] = enumDecl.Value;
                    }
                }

                // Add delegates
                foreach (var del in unit.IR.Delegates)
                {
                    if (!combined.Delegates.ContainsKey(del.Key))
                    {
                        combined.Delegates[del.Key] = del.Value;
                    }
                }

                // Add extern declarations
                foreach (var ext in unit.IR.ExternDeclarations)
                {
                    if (!combined.ExternDeclarations.ContainsKey(ext.Key))
                    {
                        combined.ExternDeclarations[ext.Key] = ext.Value;
                    }
                }

                // Add namespaces
                foreach (var ns in unit.IR.Namespaces)
                {
                    if (!combined.Namespaces.Contains(ns))
                    {
                        combined.Namespaces.Add(ns);
                    }
                }

                // Add .NET usings
                foreach (var netUsing in unit.IR.NetUsings)
                {
                    if (!combined.NetUsings.Any(u => u.Namespace == netUsing.Namespace))
                    {
                        combined.NetUsings.Add(netUsing);
                    }
                }
            }

            return combined;
        }

        /// <summary>
        /// Preprocess a .mod file by wrapping its contents in an implicit Module block.
        /// The module name is derived from the filename. If the source already contains
        /// a top-level Module declaration, a warning is emitted about the implicit wrapper.
        /// .mod module members are globally accessible without Import.
        /// </summary>
        private void PreprocessModFile(CompilationUnit unit)
        {
            if (unit == null || string.IsNullOrEmpty(unit.FilePath))
                return;

            if (!Path.GetExtension(unit.FilePath).Equals(".mod", StringComparison.OrdinalIgnoreCase))
                return;

            unit.IsModFile = true;

            var source = unit.SourceCode ?? string.Empty;
            var moduleName = Path.GetFileNameWithoutExtension(unit.FilePath);

            // Check if source already contains a top-level Module declaration
            // Strip BOM (U+FEFF) which is not considered whitespace in .NET Core/5+
            var trimmed = source.TrimStart('\uFEFF').TrimStart();
            if (trimmed.StartsWith("Module ", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Module\t", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Module\r", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Module\n", StringComparison.OrdinalIgnoreCase))
            {
                // Emit a warning - the file already has a Module declaration but will be double-wrapped
                Console.Error.WriteLine(
                    $"Warning: '{Path.GetFileName(unit.FilePath)}': Module declaration is implicit in .mod files — the outer Module wrapper will be used");
            }

            // Wrap source in Module block — adds 1 line before the original content
            unit.SourceCode = $"Module {moduleName}\n{source}\nEnd Module\n";
            unit.LineOffset = 1;
        }

        /// <summary>
        /// Preprocess a .cls/.class file by wrapping its contents in an implicit Class block.
        /// The filename becomes the class name. Private by default unless the first line is "Public".
        /// </summary>
        private void PreprocessClassFile(CompilationUnit unit)
        {
            if (unit == null || string.IsNullOrEmpty(unit.FilePath))
                return;

            if (!ModuleResolver.IsClassFile(unit.FilePath))
                return;

            unit.IsClassFile = true;

            var source = unit.SourceCode ?? string.Empty;
            var className = Path.GetFileNameWithoutExtension(unit.FilePath);
            // Strip BOM (U+FEFF) which is not considered whitespace in .NET Core/5+
            var trimmed = source.TrimStart('\uFEFF').TrimStart();
            string accessModifier = "Private";
            string body = source;

            // Check if file starts with "Public" keyword on its own line
            if (trimmed.StartsWith("Public", StringComparison.OrdinalIgnoreCase))
            {
                var afterPublic = trimmed.Substring(6);
                // Make sure "Public" is standalone keyword on its own line (not "Public Sub" etc.)
                if (afterPublic.Length == 0 || afterPublic[0] == '\r' || afterPublic[0] == '\n')
                {
                    var firstLine = trimmed.Split('\n')[0].Trim().TrimEnd('\r');
                    if (firstLine.Equals("Public", StringComparison.OrdinalIgnoreCase))
                    {
                        accessModifier = "Public";
                        var newlineIndex = trimmed.IndexOf('\n');
                        body = newlineIndex >= 0 ? trimmed.Substring(newlineIndex + 1) : string.Empty;
                    }
                }
            }

            unit.SourceCode = $"{accessModifier} Class {className}\n{body}\nEnd Class\n";
            // When "Public" was on its own line and stripped from body, the class
            // declaration replaces it so the remaining body lines keep their original
            // positions (offset = 0). Otherwise 1 line is inserted before the body.
            unit.LineOffset = (accessModifier == "Public" && body != source) ? 0 : 1;
        }

        private CompilationResult FinalizeResult(CompilationResult result, DateTime startTime)
        {
            result.Duration = DateTime.UtcNow - startTime;
            return result;
        }
    }
}
