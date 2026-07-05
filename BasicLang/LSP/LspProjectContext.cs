using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.ProjectSystem;
using BasicLang.Compiler.SemanticAnalysis;

namespace BasicLang.Compiler.LSP
{
    /// <summary>
    /// Immutable snapshot of the project a document belongs to: the set of
    /// source files and the project-wide symbol table built from all of them.
    /// Shared by every open document of the same project.
    /// </summary>
    public class LspProjectContext
    {
        private readonly HashSet<string> _fileSet;

        /// <summary>Normalized cache key: the .blproj path, or the directory for implicit projects.</summary>
        public string ProjectKey { get; }

        /// <summary>Path to the .blproj file, or null for an implicit (sibling-scan) project.</summary>
        public string ProjectFilePath { get; }

        /// <summary>Absolute paths of all source files in the project.</summary>
        public IReadOnlyList<string> SourceFiles { get; }

        /// <summary>Public symbols of every file/module in the project.</summary>
        public ProjectSymbolTable Symbols { get; }

        /// <summary>
        /// Content stamp over all project files. Two contexts with the same
        /// stamp are semantically identical; a document analyzed against an
        /// older stamp is stale and must re-run semantic analysis.
        /// </summary>
        public string Stamp { get; }

        /// <summary>
        /// Module names the LSP could not index but cannot prove unresolvable:
        /// files that exist but currently fail to parse, and module-named
        /// subdirectories the compiler's resolver would find. Imports of these
        /// must not be flagged as errors.
        /// </summary>
        public IReadOnlyCollection<string> IndeterminateImports { get; }

        public LspProjectContext(
            string projectKey,
            string projectFilePath,
            IReadOnlyList<string> sourceFiles,
            ProjectSymbolTable symbols,
            string stamp,
            IReadOnlyCollection<string> indeterminateImports = null)
        {
            ProjectKey = projectKey;
            ProjectFilePath = projectFilePath;
            SourceFiles = sourceFiles;
            Symbols = symbols;
            Stamp = stamp;
            IndeterminateImports = indeterminateImports ?? Array.Empty<string>();
            _fileSet = new HashSet<string>(sourceFiles ?? (IReadOnlyList<string>)Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
        }

        public bool ContainsFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return false;
            try
            {
                return _fileSet.Contains(Path.GetFullPath(filePath));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Find the file that defines a public symbol, along with the symbol
        /// itself. Modules registered from explicit Module blocks and from
        /// file-level declarations are both searched. Optionally skips symbols
        /// defined in <paramref name="excludeFilePath"/> (the requesting file).
        /// </summary>
        public (Symbol symbol, ModuleSymbols module) FindPublicSymbol(string name, string excludeFilePath = null)
        {
            if (string.IsNullOrEmpty(name) || Symbols == null)
                return (null, null);

            foreach (var moduleName in Symbols.GetModuleNames())
            {
                var module = Symbols.GetModule(moduleName);
                if (module == null) continue;

                var symbol = module.GetPublicSymbol(name) ?? module.GetFriendSymbol(name);
                if (symbol == null) continue;

                // Exclusion is per SYMBOL, not per module: several files can
                // contribute to one module name, so a module's FilePath alone
                // doesn't say where this particular symbol lives.
                if (excludeFilePath != null)
                {
                    var symbolPath = symbol.SourceFilePath ?? module.FilePath;
                    if (symbolPath != null &&
                        string.Equals(Path.GetFullPath(symbolPath), Path.GetFullPath(excludeFilePath),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                return (symbol, module);
            }

            return (null, null);
        }
    }

    /// <summary>
    /// Locates and caches the project context for documents opened in the LSP
    /// server. A document belongs to the nearest .blproj that lists it in a
    /// &lt;Compile Include&gt; item; otherwise its project is the set of sibling
    /// .bas/.bl/.mod/.cls files in the same directory (implicit project).
    ///
    /// Contexts are cached per project and invalidated by a content stamp so
    /// didChange on any project file rebuilds the shared symbol table.
    /// </summary>
    public class LspProjectContextProvider
    {
        private const int MaxSiblingFiles = 64;
        private const long MaxFileBytes = 1024 * 1024; // ignore giant files
        private const int MaxBlprojWalkUpDepth = 6;

        private readonly ConcurrentDictionary<string, DiskSnapshot> _diskCache =
            new ConcurrentDictionary<string, DiskSnapshot>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, ParsedFile> _astCache =
            new ConcurrentDictionary<string, ParsedFile>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, LspProjectContext> _contexts =
            new ConcurrentDictionary<string, LspProjectContext>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, BlprojSnapshot> _blprojCache =
            new ConcurrentDictionary<string, BlprojSnapshot>(StringComparer.OrdinalIgnoreCase);

        private sealed class DiskSnapshot
        {
            public DateTime LastWriteUtc;
            public string Content;
        }

        private sealed class ParsedFile
        {
            public string ContentHash;
            public ProgramNode AST; // null when the file does not parse
        }

        private sealed class BlprojSnapshot
        {
            public DateTime LastWriteUtc;
            public List<string> SourceFiles; // normalized full paths (null on load failure)
        }

        /// <summary>
        /// Build (or fetch from cache) the project context for a document.
        /// </summary>
        /// <param name="filePath">Absolute path of the document being analyzed.</param>
        /// <param name="currentContent">The document's (possibly unsaved) content.</param>
        /// <param name="openContentProvider">
        /// Returns the open-editor content for another project file, or null to
        /// read it from disk.
        /// </param>
        public LspProjectContext GetContext(string filePath, string currentContent,
            Func<string, string> openContentProvider = null)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath))
                    return null;

                filePath = Path.GetFullPath(filePath);

                // Only documents that exist on disk participate in a project;
                // unsaved/virtual documents keep single-file analysis.
                if (!File.Exists(filePath))
                    return null;

                var sourceFiles = DiscoverProjectFiles(filePath, out var projectFilePath, out var projectKey);
                if (sourceFiles == null || sourceFiles.Count == 0)
                    return null;

                // Gather the effective content of every project file:
                // current document > open editor buffer > disk.
                var entries = new List<(string Path, string Content)>();
                foreach (var sourceFile in sourceFiles)
                {
                    string content;
                    if (string.Equals(sourceFile, filePath, StringComparison.OrdinalIgnoreCase))
                    {
                        content = currentContent;
                    }
                    else
                    {
                        content = openContentProvider?.Invoke(sourceFile) ?? ReadDiskCached(sourceFile);
                    }

                    if (content != null)
                        entries.Add((sourceFile, content));
                }

                if (entries.Count == 0)
                    return null;

                var stamp = ComputeStamp(projectKey, entries);
                if (_contexts.TryGetValue(projectKey, out var cached) && cached.Stamp == stamp)
                    return cached;

                var table = new ProjectSymbolTable();
                var indeterminate = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (path, content) in entries)
                {
                    var ast = ParseCached(path, content);
                    if (ast != null)
                    {
                        LspModuleSymbolCollector.Collect(ast, path, table);
                    }
                    else
                    {
                        // The file exists but is (transiently) unparseable — its
                        // module can't be indexed right now, so imports of it
                        // must not be reported as unresolvable.
                        indeterminate.Add(Path.GetFileNameWithoutExtension(path));
                    }
                }

                // The compiler's ModuleResolver can resolve `Import X` via a
                // subdirectory named X; the LSP scan is top-directory-only, so
                // treat module-named subdirectories with source files as
                // resolvable-elsewhere rather than erroring on them.
                AddSourceSubdirectories(
                    projectFilePath != null ? Path.GetDirectoryName(projectFilePath) : Path.GetDirectoryName(filePath),
                    indeterminate);

                var context = new LspProjectContext(
                    projectKey,
                    projectFilePath,
                    entries.Select(e => e.Path).ToList(),
                    table,
                    stamp,
                    indeterminate);

                _contexts[projectKey] = context;
                return context;
            }
            catch
            {
                // Project context is best-effort; never break single-file analysis.
                return null;
            }
        }

        /// <summary>
        /// Add the names of immediate subdirectories that contain BasicLang
        /// source files — the compiler's module resolver can resolve imports
        /// into those, so the LSP must tolerate them.
        /// </summary>
        private static void AddSourceSubdirectories(string rootDir, HashSet<string> names)
        {
            try
            {
                if (string.IsNullOrEmpty(rootDir) || !Directory.Exists(rootDir))
                    return;

                foreach (var subDir in Directory.GetDirectories(rootDir))
                {
                    try
                    {
                        var hasSource =
                            Directory.EnumerateFiles(subDir, "*.bas", SearchOption.TopDirectoryOnly).Any() ||
                            Directory.EnumerateFiles(subDir, "*.bl", SearchOption.TopDirectoryOnly).Any() ||
                            Directory.EnumerateFiles(subDir, "*.mod", SearchOption.TopDirectoryOnly).Any() ||
                            Directory.EnumerateFiles(subDir, "*.cls", SearchOption.TopDirectoryOnly).Any();
                        if (hasSource)
                            names.Add(Path.GetFileName(subDir));
                    }
                    catch
                    {
                        // Unreadable subdirectory — skip
                    }
                }
            }
            catch
            {
                // Best-effort: an IO failure must never break analysis
            }
        }

        /// <summary>
        /// Find the source-file set for a document: nearest .blproj listing the
        /// file, else sibling source files in the same directory.
        /// </summary>
        private List<string> DiscoverProjectFiles(string filePath, out string projectFilePath, out string projectKey)
        {
            projectFilePath = null;
            projectKey = filePath.ToLowerInvariant();

            var dir = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return null;

            // Walk up looking for a .blproj whose <Compile Include> items contain this file
            var current = dir;
            for (int depth = 0; depth < MaxBlprojWalkUpDepth && !string.IsNullOrEmpty(current); depth++)
            {
                string[] projFiles;
                try
                {
                    projFiles = Directory.GetFiles(current, "*.blproj", SearchOption.TopDirectoryOnly);
                }
                catch
                {
                    break;
                }

                foreach (var proj in projFiles)
                {
                    var files = GetBlprojSourceFiles(proj);
                    if (files != null && files.Any(f => string.Equals(f, filePath, StringComparison.OrdinalIgnoreCase)))
                    {
                        projectFilePath = Path.GetFullPath(proj);
                        projectKey = projectFilePath.ToLowerInvariant();
                        return files;
                    }
                }

                current = Path.GetDirectoryName(current);
            }

            // Fallback: sibling source files in the same directory (implicit project)
            var siblings = new List<string> { filePath };
            foreach (var pattern in new[] { "*.bas", "*.bl", "*.mod", "*.cls" })
            {
                try
                {
                    foreach (var sibling in Directory.GetFiles(dir, pattern, SearchOption.TopDirectoryOnly))
                    {
                        var full = Path.GetFullPath(sibling);
                        if (!siblings.Contains(full, StringComparer.OrdinalIgnoreCase))
                            siblings.Add(full);
                        if (siblings.Count >= MaxSiblingFiles)
                            break;
                    }
                }
                catch
                {
                    // Skip unreadable patterns/directories
                }

                if (siblings.Count >= MaxSiblingFiles)
                    break;
            }

            projectKey = dir.ToLowerInvariant();
            return siblings;
        }

        private List<string> GetBlprojSourceFiles(string blprojPath)
        {
            try
            {
                blprojPath = Path.GetFullPath(blprojPath);
                var lastWrite = File.GetLastWriteTimeUtc(blprojPath);

                if (_blprojCache.TryGetValue(blprojPath, out var cached) && cached.LastWriteUtc == lastWrite)
                    return cached.SourceFiles;

                List<string> files = null;
                try
                {
                    var projectFile = ProjectFile.Load(blprojPath);
                    files = projectFile.GetSourceFiles()
                        .Select(Path.GetFullPath)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
                catch
                {
                    // Malformed project file: remember the failure until it changes
                }

                _blprojCache[blprojPath] = new BlprojSnapshot { LastWriteUtc = lastWrite, SourceFiles = files };
                return files;
            }
            catch
            {
                return null;
            }
        }

        private string ReadDiskCached(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length > MaxFileBytes)
                    return null;

                var lastWrite = info.LastWriteTimeUtc;
                if (_diskCache.TryGetValue(path, out var cached) && cached.LastWriteUtc == lastWrite)
                    return cached.Content;

                var content = File.ReadAllText(path);
                _diskCache[path] = new DiskSnapshot { LastWriteUtc = lastWrite, Content = content };
                return content;
            }
            catch
            {
                return null;
            }
        }

        private ProgramNode ParseCached(string path, string content)
        {
            var hash = ComputeHash(content);
            if (_astCache.TryGetValue(path, out var cached) && cached.ContentHash == hash)
                return cached.AST;

            ProgramNode ast = null;
            try
            {
                var lexer = new Lexer(content);
                var tokens = lexer.Tokenize();
                var parser = new Parser(tokens);
                // .mod/.cls siblings parse with their implicit Module/Class
                // wrapper (AST-synthesized, so symbol lines match the file)
                ast = ImplicitContainer.Parse(parser, path, content);
            }
            catch
            {
                // Unparseable sibling contributes no symbols (cached as null
                // so we don't re-parse the same broken content repeatedly)
            }

            _astCache[path] = new ParsedFile { ContentHash = hash, AST = ast };
            return ast;
        }

        private static string ComputeStamp(string projectKey, List<(string Path, string Content)> entries)
        {
            var sb = new StringBuilder();
            sb.Append(projectKey).Append('\n');
            foreach (var entry in entries.OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase))
            {
                sb.Append(entry.Path.ToLowerInvariant()).Append('|').Append(ComputeHash(entry.Content)).Append('\n');
            }
            return ComputeHash(sb.ToString());
        }

        private static string ComputeHash(string content)
        {
            using (var sha256 = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(content ?? string.Empty);
                return Convert.ToBase64String(sha256.ComputeHash(bytes));
            }
        }
    }

    /// <summary>
    /// Collects the public/exported symbols of a parsed file into a
    /// ProjectSymbolTable, mirroring the compiler's export rules:
    /// functions, subroutines and classes are always exported (the compiler's
    /// CollectExportedSymbols does the same), other members require
    /// Public/Friend access (.mod files promote Private to Public).
    ///
    /// IMPORTANT (Wave 4 lesson): declarations are usually nested inside a
    /// Module block, so the walk recurses into ModuleNode (and NamespaceNode).
    /// </summary>
    internal static class LspModuleSymbolCollector
    {
        public static void Collect(ProgramNode ast, string filePath, ProjectSymbolTable table)
        {
            if (ast?.Declarations == null || table == null)
                return;

            var extension = Path.GetExtension(filePath)?.ToLowerInvariant();
            var isModuleFile = extension == ".mod";
            var fileModuleName = Path.GetFileNameWithoutExtension(filePath);

            // Merge into an existing module entry when a previous file (or an
            // explicit Module block) already registered the same name.
            var fileModule = table.GetModule(fileModuleName)
                ?? new ModuleSymbols(fileModuleName, filePath) { IsModuleFile = isModuleFile };

            CollectDeclarations(ast.Declarations, filePath, table, fileModule, isModuleFile);

            // A Module block sharing the file's base name may have registered
            // its own entry during the walk (in this file or a sibling). The
            // file-level symbols must be MERGED into it, never dropped.
            var registered = table.GetModule(fileModuleName);
            if (registered == null)
                table.RegisterModule(fileModuleName, fileModule);
            else if (!ReferenceEquals(registered, fileModule))
                registered.MergeFrom(fileModule);
        }

        private static void CollectDeclarations(IEnumerable<ASTNode> declarations, string filePath,
            ProjectSymbolTable table, ModuleSymbols fileModule, bool isModuleFile)
        {
            foreach (var declaration in declarations)
            {
                switch (declaration)
                {
                    case ModuleNode moduleNode:
                        // Everything is (usually) in a Module — recurse into it and
                        // register the members under the explicit module name.
                        var moduleSymbols = table.GetModule(moduleNode.Name);
                        var isNewModule = moduleSymbols == null;
                        moduleSymbols ??= new ModuleSymbols(moduleNode.Name, filePath) { IsModuleFile = isModuleFile };

                        if (moduleNode.Members != null)
                        {
                            foreach (var member in moduleNode.Members)
                                AddMember(member, moduleSymbols, isModuleFile, filePath);
                        }

                        if (isNewModule)
                            table.RegisterModule(moduleNode.Name, moduleSymbols);
                        break;

                    case NamespaceNode namespaceNode:
                        if (namespaceNode.Members != null)
                            CollectDeclarations(namespaceNode.Members, filePath, table, fileModule, isModuleFile);
                        break;

                    default:
                        AddMember(declaration, fileModule, isModuleFile, filePath);
                        break;
                }
            }
        }

        private static void AddMember(ASTNode declaration, ModuleSymbols target, bool isModuleFile, string filePath)
        {
            switch (declaration)
            {
                case FunctionNode func:
                {
                    var symbol = new Symbol(func.Name, SymbolKind.Function,
                        ConvertTypeReference(func.ReturnType), func.Line, func.Column)
                    {
                        ReturnType = ConvertTypeReference(func.ReturnType),
                        Parameters = ConvertParameters(func.Parameters),
                        Access = EffectiveAccess(func.Access, isModuleFile)
                    };
                    // Functions are always exported (compiler parity)
                    symbol.SourceFilePath = filePath;
                    target.AddSymbol(symbol, AccessModifier.Public);
                    break;
                }

                case SubroutineNode sub:
                {
                    var symbol = new Symbol(sub.Name, SymbolKind.Subroutine,
                        new TypeInfo("Void", TypeKind.Void), sub.Line, sub.Column)
                    {
                        ReturnType = new TypeInfo("Void", TypeKind.Void),
                        Parameters = ConvertParameters(sub.Parameters),
                        Access = EffectiveAccess(sub.Access, isModuleFile)
                    };
                    // Subroutines are always exported (compiler parity)
                    symbol.SourceFilePath = filePath;
                    target.AddSymbol(symbol, AccessModifier.Public);
                    break;
                }

                case ClassNode classNode:
                {
                    var classType = new TypeInfo(classNode.Name, TypeKind.Class);
                    PopulateClassTypeMembers(classType, classNode, filePath);

                    var symbol = new Symbol(classNode.Name, SymbolKind.Class,
                        classType, classNode.Line, classNode.Column)
                    {
                        Access = EffectiveAccess(classNode.Access, isModuleFile)
                    };
                    // Classes are always exported (compiler parity)
                    symbol.SourceFilePath = filePath;
                    target.AddSymbol(symbol, AccessModifier.Public);
                    break;
                }

                case StructureNode structNode:
                {
                    var access = EffectiveAccess(structNode.Access, isModuleFile);
                    if (access == AccessModifier.Public || access == AccessModifier.Friend)
                    {
                        var symbol = new Symbol(structNode.Name, SymbolKind.Structure,
                            new TypeInfo(structNode.Name, TypeKind.Structure), structNode.Line, structNode.Column)
                        {
                            Access = access
                        };
                        symbol.SourceFilePath = filePath;
                        target.AddSymbol(symbol, access);
                    }
                    break;
                }

                case EnumNode enumNode:
                {
                    var access = EffectiveAccess(enumNode.Access, isModuleFile);
                    if (access == AccessModifier.Public || access == AccessModifier.Friend)
                    {
                        var symbol = new Symbol(enumNode.Name, SymbolKind.Type,
                            new TypeInfo(enumNode.Name, TypeKind.Enum), enumNode.Line, enumNode.Column)
                        {
                            Access = access
                        };
                        symbol.SourceFilePath = filePath;
                        target.AddSymbol(symbol, access);
                    }
                    break;
                }

                case InterfaceNode interfaceNode:
                {
                    var symbol = new Symbol(interfaceNode.Name, SymbolKind.Interface,
                        new TypeInfo(interfaceNode.Name, TypeKind.Interface), interfaceNode.Line, interfaceNode.Column);
                    symbol.SourceFilePath = filePath;
                    target.AddSymbol(symbol, AccessModifier.Public);
                    break;
                }

                case VariableDeclarationNode varDecl:
                {
                    var access = EffectiveAccess(varDecl.Access, isModuleFile);
                    if (access == AccessModifier.Public || access == AccessModifier.Friend)
                    {
                        var symbol = new Symbol(varDecl.Name, SymbolKind.Variable,
                            ConvertTypeReference(varDecl.Type), varDecl.Line, varDecl.Column)
                        {
                            Access = access
                        };
                        symbol.SourceFilePath = filePath;
                        target.AddSymbol(symbol, access);
                    }
                    break;
                }

                case ConstantDeclarationNode constDecl:
                {
                    var access = EffectiveAccess(constDecl.Access, isModuleFile);
                    if (access == AccessModifier.Public || access == AccessModifier.Friend)
                    {
                        var symbol = new Symbol(constDecl.Name, SymbolKind.Constant,
                            ConvertTypeReference(constDecl.Type), constDecl.Line, constDecl.Column)
                        {
                            Access = access,
                            IsConstant = true
                        };
                        symbol.SourceFilePath = filePath;
                        target.AddSymbol(symbol, access);
                    }
                    break;
                }

                case PropertyNode propNode:
                {
                    var access = EffectiveAccess(propNode.Access, isModuleFile);
                    if (access == AccessModifier.Public || access == AccessModifier.Friend)
                    {
                        var symbol = new Symbol(propNode.Name, SymbolKind.Property,
                            ConvertTypeReference(propNode.PropertyType), propNode.Line, propNode.Column)
                        {
                            Access = access
                        };
                        symbol.SourceFilePath = filePath;
                        target.AddSymbol(symbol, access);
                    }
                    break;
                }
            }
        }

        /// <summary>
        /// Record a class's non-private members on its TypeInfo so member
        /// completion and go-to-definition work for cross-file instances
        /// ("player." where Player lives in a sibling file).
        /// </summary>
        private static void PopulateClassTypeMembers(TypeInfo classType, ClassNode classNode, string filePath)
        {
            if (classNode.Members == null)
                return;

            foreach (var member in classNode.Members)
            {
                Symbol memberSymbol = null;

                switch (member)
                {
                    case FunctionNode func when func.Access != AccessModifier.Private:
                        memberSymbol = new Symbol(func.Name, SymbolKind.Function,
                            ConvertTypeReference(func.ReturnType), func.Line, func.Column)
                        {
                            ReturnType = ConvertTypeReference(func.ReturnType),
                            Parameters = ConvertParameters(func.Parameters),
                            Access = func.Access
                        };
                        break;

                    case SubroutineNode sub when sub.Access != AccessModifier.Private:
                        memberSymbol = new Symbol(sub.Name, SymbolKind.Subroutine,
                            new TypeInfo("Void", TypeKind.Void), sub.Line, sub.Column)
                        {
                            ReturnType = new TypeInfo("Void", TypeKind.Void),
                            Parameters = ConvertParameters(sub.Parameters),
                            Access = sub.Access
                        };
                        break;

                    case VariableDeclarationNode field when field.Access != AccessModifier.Private:
                        memberSymbol = new Symbol(field.Name, SymbolKind.Variable,
                            ConvertTypeReference(field.Type), field.Line, field.Column)
                        {
                            Access = field.Access
                        };
                        break;

                    case PropertyNode prop when prop.Access != AccessModifier.Private:
                        memberSymbol = new Symbol(prop.Name, SymbolKind.Property,
                            ConvertTypeReference(prop.PropertyType), prop.Line, prop.Column)
                        {
                            Access = prop.Access
                        };
                        break;

                    case ConstantDeclarationNode constant when constant.Access != AccessModifier.Private:
                        memberSymbol = new Symbol(constant.Name, SymbolKind.Constant,
                            ConvertTypeReference(constant.Type), constant.Line, constant.Column)
                        {
                            Access = constant.Access,
                            IsConstant = true
                        };
                        break;

                    case EventDeclarationNode evt when evt.Access != AccessModifier.Private:
                        memberSymbol = new Symbol(evt.Name, SymbolKind.Event,
                            ConvertTypeReference(evt.EventType), evt.Line, evt.Column)
                        {
                            Access = evt.Access
                        };
                        break;
                }

                if (memberSymbol != null && !string.IsNullOrEmpty(memberSymbol.Name))
                {
                    memberSymbol.SourceFilePath = filePath;
                    classType.Members[memberSymbol.Name] = memberSymbol;
                }
            }
        }

        private static AccessModifier EffectiveAccess(AccessModifier declared, bool isModuleFile)
        {
            // .mod files default to public exports
            if (isModuleFile && declared == AccessModifier.Private)
                return AccessModifier.Public;
            return declared;
        }

        private static List<Symbol> ConvertParameters(List<ParameterNode> parameters)
        {
            if (parameters == null)
                return new List<Symbol>();

            return parameters.Select(p => new Symbol(p.Name, SymbolKind.Parameter,
                ConvertTypeReference(p.Type), p.Line, p.Column)
            {
                IsByRef = p.IsByRef,
                IsOptional = p.IsOptional || p.DefaultValue != null,
                IsParamArray = p.IsParamArray
            }).ToList();
        }

        private static TypeInfo ConvertTypeReference(TypeReference typeRef)
        {
            if (typeRef == null)
                return null;

            var kind = typeRef.Name.ToLowerInvariant() switch
            {
                "integer" or "int" or "int32" => TypeKind.Primitive,
                "long" or "int64" => TypeKind.Primitive,
                "short" or "int16" => TypeKind.Primitive,
                "byte" => TypeKind.Primitive,
                "single" or "float" => TypeKind.Primitive,
                "double" => TypeKind.Primitive,
                "decimal" => TypeKind.Primitive,
                "boolean" or "bool" => TypeKind.Primitive,
                "string" => TypeKind.Primitive,
                "char" => TypeKind.Primitive,
                "object" => TypeKind.Class,
                "void" => TypeKind.Void,
                _ => TypeKind.UserDefinedType
            };

            return new TypeInfo(typeRef.Name, kind);
        }
    }
}
