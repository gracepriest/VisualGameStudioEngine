using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.SemanticAnalysis;
using TypeInfo = BasicLang.Compiler.SemanticAnalysis.TypeInfo;

namespace BasicLang.Compiler
{
    /// <summary>
    /// Loads external libraries (compiled BasicLang libraries or .NET assemblies)
    /// and makes their symbols available for import.
    /// </summary>
    public class ExternalLibraryLoader
    {
        private readonly Dictionary<string, ExternalLibrary> _loadedLibraries = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _libraryPaths = new();
        private readonly List<string> _errors = new();

        public IReadOnlyList<string> Errors => _errors;

        /// <summary>
        /// Add a path to search for external libraries
        /// </summary>
        public void AddLibraryPath(string path)
        {
            if (!_libraryPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
                _libraryPaths.Add(path);
        }

        /// <summary>
        /// Load an external library by name or path
        /// </summary>
        /// <param name="nameOrPath">Library name (e.g., "GameFramework") or path (e.g., "./libs/GameFramework.dll")</param>
        /// <returns>The loaded library, or null if not found</returns>
        public ExternalLibrary LoadLibrary(string nameOrPath)
        {
            // Check if already loaded
            if (_loadedLibraries.TryGetValue(nameOrPath, out var existing))
                return existing;

            // Try to resolve the library path
            var resolvedPath = ResolveLibraryPath(nameOrPath);
            if (resolvedPath == null)
            {
                _errors.Add($"Cannot find library: {nameOrPath}");
                return null;
            }

            // Check if already loaded by resolved path
            if (_loadedLibraries.TryGetValue(resolvedPath, out existing))
            {
                // Cache by name too
                _loadedLibraries[nameOrPath] = existing;
                return existing;
            }

            // Determine library type and load
            var extension = Path.GetExtension(resolvedPath).ToLowerInvariant();
            ExternalLibrary library = null;

            try
            {
                switch (extension)
                {
                    case ".dll":
                        // Could be .NET assembly or compiled BasicLang library
                        library = LoadDotNetAssembly(resolvedPath);
                        break;
                    case ".blb":
                        // BasicLang compiled library
                        library = LoadBasicLangLibrary(resolvedPath);
                        break;
                    case ".bh":
                        // BasicLang header file - parse for declarations
                        library = LoadBasicLangHeader(resolvedPath);
                        break;
                    default:
                        _errors.Add($"Unsupported library format: {extension}");
                        return null;
                }
            }
            catch (Exception ex)
            {
                _errors.Add($"Error loading library '{nameOrPath}': {ex.Message}");
                return null;
            }

            if (library != null)
            {
                _loadedLibraries[nameOrPath] = library;
                _loadedLibraries[resolvedPath] = library;
            }

            return library;
        }

        /// <summary>
        /// Resolve a library name or path to a full path
        /// </summary>
        private string ResolveLibraryPath(string nameOrPath)
        {
            // If it's an absolute path and exists, use it
            if (Path.IsPathRooted(nameOrPath) && File.Exists(nameOrPath))
                return nameOrPath;

            // Check if it's a relative path from current directory
            if (File.Exists(nameOrPath))
                return Path.GetFullPath(nameOrPath);

            // Try adding common extensions
            var extensions = new[] { ".dll", ".blb", ".bh" };

            // First check if the name already has an extension
            var hasExtension = extensions.Any(e => nameOrPath.EndsWith(e, StringComparison.OrdinalIgnoreCase));

            if (!hasExtension)
            {
                // Try with each extension
                foreach (var ext in extensions)
                {
                    var pathWithExt = nameOrPath + ext;
                    if (File.Exists(pathWithExt))
                        return Path.GetFullPath(pathWithExt);

                    // Search in library paths
                    foreach (var libPath in _libraryPaths)
                    {
                        var fullPath = Path.Combine(libPath, pathWithExt);
                        if (File.Exists(fullPath))
                            return fullPath;
                    }
                }
            }

            // Search in library paths without extension changes
            foreach (var libPath in _libraryPaths)
            {
                var fullPath = Path.Combine(libPath, nameOrPath);
                if (File.Exists(fullPath))
                    return fullPath;
            }

            // Try standard library location
            var standardLibPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib", nameOrPath);
            if (!hasExtension)
            {
                foreach (var ext in extensions)
                {
                    if (File.Exists(standardLibPath + ext))
                        return standardLibPath + ext;
                }
            }
            else if (File.Exists(standardLibPath))
            {
                return standardLibPath;
            }

            return null;
        }

        /// <summary>
        /// Load a .NET assembly and extract its public types
        /// </summary>
        private ExternalLibrary LoadDotNetAssembly(string path)
        {
            var assembly = Assembly.LoadFrom(path);
            var library = new ExternalLibrary
            {
                Name = assembly.GetName().Name,
                Path = path,
                IsNetAssembly = true,
                AssemblyRef = assembly
            };

            // Extract public types
            foreach (var type in assembly.GetExportedTypes())
            {
                var typeSymbol = CreateSymbolFromType(type);
                if (typeSymbol != null)
                {
                    library.Symbols[type.Name] = typeSymbol;

                    // Also store by full name for qualified access
                    if (!string.IsNullOrEmpty(type.Namespace))
                    {
                        library.Namespaces.Add(type.Namespace);
                        library.SymbolsByFullName[$"{type.Namespace}.{type.Name}"] = typeSymbol;
                    }
                }
            }

            return library;
        }

        /// <summary>
        /// Create a Symbol from a .NET Type
        /// </summary>
        private Symbol CreateSymbolFromType(Type type)
        {
            var symbol = new Symbol(type.Name, SymbolKind.Type)
            {
                IsPublic = type.IsPublic
            };

            // Set type info
            symbol.Type = new TypeInfo(type.Name, MapNetTypeKind(type));

            // Store member information for later lookup
            var members = new List<NetMemberInfo>();

            // Get public methods
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Where(m => !m.IsSpecialName))
            {
                members.Add(new NetMemberInfo
                {
                    Name = method.Name,
                    Kind = NetMemberKind.Method,
                    ReturnType = method.ReturnType.Name,
                    IsStatic = method.IsStatic,
                    Parameters = method.GetParameters().Select(p => new NetParameterInfo
                    {
                        Name = p.Name,
                        Type = p.ParameterType.Name,
                        IsOptional = p.IsOptional
                    }).ToList()
                });
            }

            // Get public properties
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                members.Add(new NetMemberInfo
                {
                    Name = prop.Name,
                    Kind = NetMemberKind.Property,
                    ReturnType = prop.PropertyType.Name,
                    IsStatic = prop.GetMethod?.IsStatic ?? prop.SetMethod?.IsStatic ?? false,
                    CanRead = prop.CanRead,
                    CanWrite = prop.CanWrite
                });
            }

            // Get public fields
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                members.Add(new NetMemberInfo
                {
                    Name = field.Name,
                    Kind = field.IsLiteral ? NetMemberKind.EnumValue : NetMemberKind.Field,
                    ReturnType = field.FieldType.Name,
                    IsStatic = field.IsStatic || field.IsLiteral
                });
            }

            // Store members for IntelliSense
            symbol.NetMembers = members;
            symbol.NetType = type;

            return symbol;
        }

        private TypeKind MapNetTypeKind(Type type)
        {
            if (type.IsEnum) return TypeKind.Enum;
            if (type.IsInterface) return TypeKind.Interface;
            if (type.IsValueType && !type.IsPrimitive) return TypeKind.Structure;
            if (type.IsClass) return TypeKind.Class;
            return TypeKind.Class;
        }

        /// <summary>
        /// Load a compiled BasicLang library (.blb)
        /// </summary>
        private ExternalLibrary LoadBasicLangLibrary(string path)
        {
            // BasicLang compiled libraries store metadata about exported symbols
            using var reader = new BinaryReader(File.OpenRead(path));

            // Read header
            var magic = reader.ReadString();
            if (magic != "BLB1")
            {
                throw new InvalidDataException("Invalid BasicLang library format");
            }

            var library = new ExternalLibrary
            {
                Name = reader.ReadString(),
                Path = path,
                IsNetAssembly = false
            };

            // Read symbol count
            var symbolCount = reader.ReadInt32();

            for (int i = 0; i < symbolCount; i++)
            {
                var symbolName = reader.ReadString();
                var symbolKindValue = reader.ReadInt32();
                var typeName = reader.ReadString();

                var symbolKind = (SymbolKind)symbolKindValue;
                var symbol = new Symbol(symbolName, symbolKind)
                {
                    IsPublic = true
                };

                // Read type info
                if (!string.IsNullOrEmpty(typeName))
                {
                    symbol.Type = ParseTypeInfo(typeName);
                }

                // Read parameters for functions
                if (symbolKind == SymbolKind.Function || symbolKind == SymbolKind.Subroutine)
                {
                    var paramCount = reader.ReadInt32();
                    symbol.Parameters = new List<Symbol>();

                    for (int p = 0; p < paramCount; p++)
                    {
                        var paramName = reader.ReadString();
                        var paramType = reader.ReadString();
                        symbol.Parameters.Add(new Symbol(paramName, SymbolKind.Parameter)
                        {
                            Type = ParseTypeInfo(paramType)
                        });
                    }
                }

                library.Symbols[symbolName] = symbol;
            }

            return library;
        }

        /// <summary>
        /// Load a BasicLang header file (.bh) and parse its declarations
        /// </summary>
        private ExternalLibrary LoadBasicLangHeader(string path)
        {
            var source = File.ReadAllText(path);
            var lexer = new Lexer(source);
            var parser = new Parser(lexer.Tokenize().ToList());
            var program = parser.Parse();

            if (parser.Errors.Any())
            {
                throw new Exception($"Parse errors in header file: {string.Join(", ", parser.Errors)}");
            }

            var library = new ExternalLibrary
            {
                Name = Path.GetFileNameWithoutExtension(path),
                Path = path,
                IsNetAssembly = false,
                IsHeaderFile = true
            };

            // Extract declarations from the parsed AST
            foreach (var decl in program.Declarations)
            {
                switch (decl)
                {
                    case FunctionNode func:
                        var funcSymbol = new Symbol(func.Name, SymbolKind.Function)
                        {
                            IsPublic = func.Access == AccessModifier.Public,
                            Type = ParseTypeInfo(func.ReturnType?.Name ?? "Void")
                        };
                        funcSymbol.Parameters = func.Parameters.Select(p => new Symbol(p.Name, SymbolKind.Parameter)
                        {
                            Type = ParseTypeInfo(p.Type?.Name ?? "Object")
                        }).ToList();
                        library.Symbols[func.Name] = funcSymbol;
                        break;

                    case SubroutineNode sub:
                        var subSymbol = new Symbol(sub.Name, SymbolKind.Subroutine)
                        {
                            IsPublic = sub.Access == AccessModifier.Public,
                            Type = new TypeInfo("Void", TypeKind.Void)
                        };
                        subSymbol.Parameters = sub.Parameters.Select(p => new Symbol(p.Name, SymbolKind.Parameter)
                        {
                            Type = ParseTypeInfo(p.Type?.Name ?? "Object")
                        }).ToList();
                        library.Symbols[sub.Name] = subSymbol;
                        break;

                    case ExternDeclarationNode ext:
                        var extSymbol = new Symbol(ext.Name, ext.IsFunction ? SymbolKind.Function : SymbolKind.Subroutine)
                        {
                            IsPublic = true,
                            IsExtern = true,
                            Type = ext.IsFunction ? ParseTypeInfo(ext.ReturnType?.Name ?? "Void") : new TypeInfo("Void", TypeKind.Void)
                        };
                        extSymbol.Parameters = ext.Parameters?.Select(p => new Symbol(p.Name, SymbolKind.Parameter)
                        {
                            Type = ParseTypeInfo(p.Type?.Name ?? "Object")
                        }).ToList() ?? new List<Symbol>();
                        library.Symbols[ext.Name] = extSymbol;
                        break;

                    case ConstantDeclarationNode constNode:
                        var constSymbol = new Symbol(constNode.Name, SymbolKind.Constant)
                        {
                            IsPublic = constNode.Access == AccessModifier.Public,
                            Type = ParseTypeInfo(constNode.Type?.Name ?? "Object")
                        };
                        library.Symbols[constNode.Name] = constSymbol;
                        break;

                    case VariableDeclarationNode varNode:
                        // Extern variables
                        if (varNode.IsExtern)
                        {
                            var varSymbol = new Symbol(varNode.Name, SymbolKind.Variable)
                            {
                                IsPublic = true,
                                IsExtern = true,
                                Type = ParseTypeInfo(varNode.Type?.Name ?? "Object")
                            };
                            library.Symbols[varNode.Name] = varSymbol;
                        }
                        break;

                    case ClassNode classNode:
                        var classSymbol = new Symbol(classNode.Name, SymbolKind.Type)
                        {
                            IsPublic = classNode.Access == AccessModifier.Public,
                            Type = new TypeInfo(classNode.Name, TypeKind.Class)
                        };
                        library.Symbols[classNode.Name] = classSymbol;
                        break;

                    case StructureNode structNode:
                        var structSymbol = new Symbol(structNode.Name, SymbolKind.Type)
                        {
                            IsPublic = structNode.Access == AccessModifier.Public,
                            Type = new TypeInfo(structNode.Name, TypeKind.Structure)
                        };
                        library.Symbols[structNode.Name] = structSymbol;
                        break;

                    case EnumNode enumNode:
                        var enumSymbol = new Symbol(enumNode.Name, SymbolKind.Type)
                        {
                            IsPublic = enumNode.Access == AccessModifier.Public,
                            Type = new TypeInfo(enumNode.Name, TypeKind.Enum)
                        };
                        library.Symbols[enumNode.Name] = enumSymbol;
                        break;
                }
            }

            return library;
        }

        private TypeInfo ParseTypeInfo(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return new TypeInfo("Void", TypeKind.Void);

            // Map to appropriate TypeKind
            var lowerName = typeName.ToLower();
            TypeKind kind;

            switch (lowerName)
            {
                case "integer":
                case "int":
                case "int32":
                case "long":
                case "int64":
                case "single":
                case "float":
                case "double":
                case "string":
                case "boolean":
                case "bool":
                case "byte":
                    kind = TypeKind.Primitive;
                    break;
                case "void":
                case "sub":
                    return new TypeInfo("Void", TypeKind.Void);
                case "object":
                    return new TypeInfo("Object", TypeKind.Class);
                default:
                    kind = TypeKind.UserDefinedType;
                    break;
            }

            // Normalize type name
            var normalizedName = lowerName switch
            {
                "integer" or "int" or "int32" => "Integer",
                "long" or "int64" => "Long",
                "single" or "float" => "Single",
                "double" => "Double",
                "string" => "String",
                "boolean" or "bool" => "Boolean",
                "byte" => "Byte",
                _ => typeName
            };

            return new TypeInfo(normalizedName, kind);
        }

        /// <summary>
        /// Get all loaded libraries
        /// </summary>
        public IEnumerable<ExternalLibrary> GetLoadedLibraries()
        {
            return _loadedLibraries.Values.Distinct();
        }

        /// <summary>
        /// Check if a library is loaded
        /// </summary>
        public bool IsLoaded(string nameOrPath)
        {
            return _loadedLibraries.ContainsKey(nameOrPath);
        }

        /// <summary>
        /// Clear all loaded libraries
        /// </summary>
        public void Clear()
        {
            _loadedLibraries.Clear();
            _errors.Clear();
        }
    }

    /// <summary>
    /// Represents an external library (compiled BasicLang or .NET assembly)
    /// </summary>
    public class ExternalLibrary
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public bool IsNetAssembly { get; set; }
        public bool IsHeaderFile { get; set; }
        public Assembly AssemblyRef { get; set; }

        /// <summary>
        /// Symbols exported by this library (by short name)
        /// </summary>
        public Dictionary<string, Symbol> Symbols { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Symbols by fully qualified name (namespace.typename)
        /// </summary>
        public Dictionary<string, Symbol> SymbolsByFullName { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Namespaces defined in this library
        /// </summary>
        public HashSet<string> Namespaces { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Get a symbol by name
        /// </summary>
        public Symbol GetSymbol(string name)
        {
            if (Symbols.TryGetValue(name, out var symbol))
                return symbol;
            if (SymbolsByFullName.TryGetValue(name, out symbol))
                return symbol;
            return null;
        }

        /// <summary>
        /// Get all public symbols
        /// </summary>
        public IEnumerable<Symbol> GetPublicSymbols()
        {
            return Symbols.Values.Where(s => s.IsPublic);
        }
    }
}
