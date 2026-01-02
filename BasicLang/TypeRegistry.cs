using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace BasicLang.Compiler.SemanticAnalysis
{
    /// <summary>
    /// Registry for .NET types loaded from assemblies.
    /// Provides lazy loading based on Using statements.
    /// </summary>
    public class TypeRegistry
    {
        // Namespace -> Assembly path mapping (lightweight index)
        private readonly Dictionary<string, List<string>> _namespaceIndex;

        // Loaded types per namespace (lazy loaded)
        private readonly Dictionary<string, List<NetTypeInfo>> _loadedTypes;

        // All loaded types by full name for quick lookup
        private readonly Dictionary<string, NetTypeInfo> _typesByName;

        // Assembly search paths (HashSet for O(1) Contains checks)
        private readonly HashSet<string> _searchPaths;

        // Cache file path for the namespace index
        private readonly string _cacheFilePath;

        // Track which namespaces have been loaded
        private readonly HashSet<string> _loadedNamespaces;

        public TypeRegistry()
        {
            _namespaceIndex = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            _loadedTypes = new Dictionary<string, List<NetTypeInfo>>(StringComparer.OrdinalIgnoreCase);
            _typesByName = new Dictionary<string, NetTypeInfo>(StringComparer.OrdinalIgnoreCase);
            _searchPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _loadedNamespaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Default cache location
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _cacheFilePath = Path.Combine(appData, "BasicLang", "namespace_index.json");
        }

        /// <summary>
        /// Add an assembly search path (e.g., .NET SDK ref assemblies folder)
        /// </summary>
        public void AddSearchPath(string path)
        {
            if (Directory.Exists(path))
            {
                _searchPaths.Add(path);  // HashSet.Add ignores duplicates
            }
        }

        /// <summary>
        /// Build the namespace index from configured search paths.
        /// This scans all assemblies and maps namespaces to assembly paths.
        /// </summary>
        public void BuildIndex()
        {
            _namespaceIndex.Clear();

            foreach (var searchPath in _searchPaths)
            {
                ScanDirectory(searchPath);
            }

            // Save to cache
            SaveIndexToCache();
        }

        /// <summary>
        /// Load index from cache if available
        /// </summary>
        public bool LoadIndexFromCache()
        {
            if (!File.Exists(_cacheFilePath))
                return false;

            try
            {
                var lines = File.ReadAllLines(_cacheFilePath);
                foreach (var line in lines)
                {
                    var parts = line.Split('|');
                    if (parts.Length == 2)
                    {
                        var ns = parts[0];
                        var assemblies = parts[1].Split(';').Where(s => !string.IsNullOrEmpty(s)).ToList();

                        if (!_namespaceIndex.ContainsKey(ns))
                            _namespaceIndex[ns] = new List<string>();

                        _namespaceIndex[ns].AddRange(assemblies);
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void SaveIndexToCache()
        {
            try
            {
                var dir = Path.GetDirectoryName(_cacheFilePath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var lines = _namespaceIndex.Select(kvp =>
                    $"{kvp.Key}|{string.Join(";", kvp.Value.Distinct())}");
                File.WriteAllLines(_cacheFilePath, lines);
            }
            catch
            {
                // Ignore cache write failures
            }
        }

        private void ScanDirectory(string path)
        {
            try
            {
                foreach (var dll in Directory.GetFiles(path, "*.dll"))
                {
                    ScanAssembly(dll);
                }
            }
            catch
            {
                // Ignore scan errors
            }
        }

        private void ScanAssembly(string assemblyPath)
        {
            try
            {
                // Use reflection to scan for namespaces
                var assembly = Assembly.LoadFrom(assemblyPath);
                var namespaces = new HashSet<string>();

                foreach (var type in assembly.GetExportedTypes())
                {
                    if (!string.IsNullOrEmpty(type.Namespace))
                    {
                        namespaces.Add(type.Namespace);
                    }
                }

                foreach (var ns in namespaces)
                {
                    if (!_namespaceIndex.ContainsKey(ns))
                        _namespaceIndex[ns] = new List<string>();

                    if (!_namespaceIndex[ns].Contains(assemblyPath))
                        _namespaceIndex[ns].Add(assemblyPath);
                }
            }
            catch
            {
                // Skip assemblies that can't be loaded
            }
        }

        /// <summary>
        /// Load types for a specific namespace (called when Using statement is encountered)
        /// </summary>
        public bool LoadNamespace(string namespaceName)
        {
            if (_loadedNamespaces.Contains(namespaceName))
                return true;

            // Find assemblies containing this namespace
            if (!_namespaceIndex.TryGetValue(namespaceName, out var assemblies))
            {
                // Try parent namespace matching (e.g., "System" should match "System.IO")
                var matchingNamespaces = _namespaceIndex.Keys
                    .Where(k => k.StartsWith(namespaceName + ".", StringComparison.OrdinalIgnoreCase) ||
                               k.Equals(namespaceName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (matchingNamespaces.Count == 0)
                    return false;

                assemblies = matchingNamespaces
                    .SelectMany(ns => _namespaceIndex[ns])
                    .Distinct()
                    .ToList();
            }

            // Load types from each assembly
            foreach (var assemblyPath in assemblies)
            {
                LoadTypesFromAssembly(assemblyPath, namespaceName);
            }

            _loadedNamespaces.Add(namespaceName);
            return true;
        }

        private void LoadTypesFromAssembly(string assemblyPath, string targetNamespace)
        {
            try
            {
                // Use reflection to load type metadata
                var assembly = Assembly.LoadFrom(assemblyPath);

                foreach (var type in assembly.GetExportedTypes())
                {
                    // Only load types from the target namespace (or child namespaces)
                    if (type.Namespace == null)
                        continue;

                    if (!type.Namespace.Equals(targetNamespace, StringComparison.OrdinalIgnoreCase) &&
                        !type.Namespace.StartsWith(targetNamespace + ".", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var typeInfo = CreateNetTypeInfo(type);

                    if (!_loadedTypes.ContainsKey(type.Namespace))
                        _loadedTypes[type.Namespace] = new List<NetTypeInfo>();

                    _loadedTypes[type.Namespace].Add(typeInfo);
                    _typesByName[type.FullName] = typeInfo;
                    _typesByName[type.Name] = typeInfo; // Also index by simple name
                }
            }
            catch
            {
                // Skip assemblies that can't be loaded
            }
        }

        /// <summary>
        /// Load all types from an assembly (used for NuGet packages)
        /// </summary>
        public List<NetTypeInfo> LoadTypesFromAssembly(string assemblyPath)
        {
            var types = new List<NetTypeInfo>();
            try
            {
                var assembly = Assembly.LoadFrom(assemblyPath);

                foreach (var type in assembly.GetExportedTypes())
                {
                    if (type.Namespace == null)
                        continue;

                    // Check if already loaded
                    if (_typesByName.ContainsKey(type.FullName))
                        continue;

                    var typeInfo = CreateNetTypeInfo(type);
                    types.Add(typeInfo);

                    if (!_loadedTypes.ContainsKey(type.Namespace))
                        _loadedTypes[type.Namespace] = new List<NetTypeInfo>();

                    _loadedTypes[type.Namespace].Add(typeInfo);
                    _typesByName[type.FullName] = typeInfo;
                    _typesByName[type.Name] = typeInfo;

                    // Track namespace as loaded
                    _loadedNamespaces.Add(type.Namespace);
                }
            }
            catch
            {
                // Skip assemblies that can't be loaded
            }
            return types;
        }

        /// <summary>
        /// Get extension methods for a given type
        /// </summary>
        public IEnumerable<NetMemberInfo> GetExtensionMethods(string typeName)
        {
            var extensionMethods = new List<NetMemberInfo>();

            // Look through all static types for extension methods
            foreach (var typeInfo in _typesByName.Values.Where(t => t.IsStatic))
            {
                foreach (var member in typeInfo.Members)
                {
                    if (member.Kind == NetMemberKind.Method &&
                        member.IsStatic &&
                        member.Parameters.Count > 0 &&
                        member.IsExtensionMethod)
                    {
                        // Check if first parameter type matches
                        var firstParamType = member.Parameters[0].Type;
                        if (IsTypeCompatible(typeName, firstParamType))
                        {
                            extensionMethods.Add(member);
                        }
                    }
                }
            }

            return extensionMethods;
        }

        private bool IsTypeCompatible(string actualType, string expectedType)
        {
            if (actualType.Equals(expectedType, StringComparison.OrdinalIgnoreCase))
                return true;

            // Handle generic type compatibility (e.g., List(Of String) with IEnumerable(Of T))
            if (expectedType.Contains("(Of T)") || expectedType.Contains("(Of TSource)"))
            {
                // Generic extension method - check base type
                var baseName = expectedType.Split('(')[0];
                if (baseName == "IEnumerable" && actualType.Contains("List") ||
                    actualType.Contains("Array") ||
                    actualType.Contains("Collection"))
                    return true;
            }

            return false;
        }

        private NetTypeInfo CreateNetTypeInfo(Type type)
        {
            var info = new NetTypeInfo
            {
                Name = type.Name,
                FullName = type.FullName,
                Namespace = type.Namespace,
                IsStatic = type.IsAbstract && type.IsSealed,
                IsClass = type.IsClass,
                IsInterface = type.IsInterface,
                IsEnum = type.IsEnum,
                IsStruct = type.IsValueType && !type.IsEnum,
                IsGeneric = type.IsGenericType,
                GenericParameters = type.IsGenericType
                    ? type.GetGenericArguments().Select(a => a.Name).ToList()
                    : new List<string>(),
                BaseType = type.BaseType != null ? GetTypeName(type.BaseType) : null,
                Interfaces = type.GetInterfaces().Select(i => GetTypeName(i)).ToList()
            };

            // Load constructors
            foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            {
                var ctorInfo = new NetMemberInfo
                {
                    Name = "New",
                    Kind = NetMemberKind.Constructor,
                    ReturnType = type.Name,
                    Parameters = ctor.GetParameters()
                        .Select(p => new NetParameterInfo
                        {
                            Name = p.Name,
                            Type = GetTypeName(p.ParameterType),
                            IsOptional = p.IsOptional
                        }).ToList()
                };
                info.Constructors.Add(ctorInfo);
            }

            // Load members
            var bindingFlags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;

            foreach (var method in type.GetMethods(bindingFlags))
            {
                if (method.IsSpecialName) continue; // Skip property accessors

                // Check for extension method attribute
                var isExtension = method.IsStatic &&
                    method.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false);

                var memberInfo = new NetMemberInfo
                {
                    Name = method.Name,
                    Kind = NetMemberKind.Method,
                    ReturnType = GetTypeName(method.ReturnType),
                    IsStatic = method.IsStatic,
                    IsExtensionMethod = isExtension,
                    IsGeneric = method.IsGenericMethod,
                    GenericParameters = method.IsGenericMethod
                        ? method.GetGenericArguments().Select(a => a.Name).ToList()
                        : new List<string>(),
                    Parameters = method.GetParameters()
                        .Select(p => new NetParameterInfo
                        {
                            Name = p.Name,
                            Type = GetTypeName(p.ParameterType),
                            IsOptional = p.IsOptional
                        }).ToList()
                };
                info.Members.Add(memberInfo);
            }

            foreach (var prop in type.GetProperties(bindingFlags))
            {
                var memberInfo = new NetMemberInfo
                {
                    Name = prop.Name,
                    Kind = NetMemberKind.Property,
                    ReturnType = GetTypeName(prop.PropertyType),
                    IsStatic = prop.GetMethod?.IsStatic ?? prop.SetMethod?.IsStatic ?? false,
                    CanRead = prop.CanRead,
                    CanWrite = prop.CanWrite
                };
                info.Members.Add(memberInfo);
            }

            foreach (var field in type.GetFields(bindingFlags))
            {
                var memberInfo = new NetMemberInfo
                {
                    Name = field.Name,
                    Kind = NetMemberKind.Field,
                    ReturnType = GetTypeName(field.FieldType),
                    IsStatic = field.IsStatic
                };
                info.Members.Add(memberInfo);
            }

            if (type.IsEnum)
            {
                foreach (var enumValue in Enum.GetNames(type))
                {
                    var memberInfo = new NetMemberInfo
                    {
                        Name = enumValue,
                        Kind = NetMemberKind.EnumValue,
                        ReturnType = type.Name,
                        IsStatic = true
                    };
                    info.Members.Add(memberInfo);
                }
            }

            return info;
        }

        private string GetTypeName(Type type)
        {
            if (type == typeof(void)) return "Void";
            if (type == typeof(int)) return "Integer";
            if (type == typeof(long)) return "Long";
            if (type == typeof(float)) return "Single";
            if (type == typeof(double)) return "Double";
            if (type == typeof(string)) return "String";
            if (type == typeof(bool)) return "Boolean";
            if (type == typeof(char)) return "Char";
            if (type == typeof(byte)) return "Byte";
            if (type == typeof(object)) return "Object";

            if (type.IsGenericType)
            {
                var genericName = type.Name.Split('`')[0];
                var args = string.Join(", ", type.GetGenericArguments().Select(GetTypeName));
                return $"{genericName}(Of {args})";
            }

            if (type.IsArray)
            {
                return $"{GetTypeName(type.GetElementType())}()";
            }

            return type.Name;
        }

        /// <summary>
        /// Get all types in a loaded namespace
        /// </summary>
        public IEnumerable<NetTypeInfo> GetTypesInNamespace(string namespaceName)
        {
            if (_loadedTypes.TryGetValue(namespaceName, out var types))
                return types;

            // Also check for child namespaces
            var result = new List<NetTypeInfo>();
            foreach (var kvp in _loadedTypes)
            {
                if (kvp.Key.StartsWith(namespaceName + ".") || kvp.Key.Equals(namespaceName, StringComparison.OrdinalIgnoreCase))
                {
                    result.AddRange(kvp.Value);
                }
            }

            return result;
        }

        /// <summary>
        /// Get a specific type by name
        /// </summary>
        public NetTypeInfo GetType(string typeName)
        {
            _typesByName.TryGetValue(typeName, out var type);
            return type;
        }

        /// <summary>
        /// Get all loaded types (for completion)
        /// </summary>
        public IEnumerable<NetTypeInfo> GetAllLoadedTypes()
        {
            return _typesByName.Values.Distinct();
        }

        /// <summary>
        /// Get members of a type by type name
        /// </summary>
        public IEnumerable<NetMemberInfo> GetTypeMembers(string typeName)
        {
            if (_typesByName.TryGetValue(typeName, out var type))
                return type.Members;

            return Enumerable.Empty<NetMemberInfo>();
        }

        /// <summary>
        /// Check if a namespace is loaded
        /// </summary>
        public bool IsNamespaceLoaded(string namespaceName)
        {
            return _loadedNamespaces.Contains(namespaceName);
        }

        /// <summary>
        /// Get all loaded namespaces
        /// </summary>
        public IEnumerable<string> GetLoadedNamespaces()
        {
            return _loadedNamespaces;
        }

        /// <summary>
        /// Detect .NET SDK path automatically
        /// </summary>
        public static string DetectDotNetSdkPath()
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var dotnetPath = Path.Combine(programFiles, "dotnet", "packs", "Microsoft.NETCore.App.Ref");

            if (Directory.Exists(dotnetPath))
            {
                // Find the latest version
                var versions = Directory.GetDirectories(dotnetPath)
                    .OrderByDescending(d => d)
                    .FirstOrDefault();

                if (versions != null)
                {
                    // Find ref folder for latest .NET version
                    var refPath = Directory.GetDirectories(Path.Combine(versions, "ref"))
                        .OrderByDescending(d => d)
                        .FirstOrDefault();

                    if (refPath != null && Directory.Exists(refPath))
                        return refPath;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Lightweight representation of a .NET type for IntelliSense
    /// </summary>
    public class NetTypeInfo
    {
        public string Name { get; set; }
        public string FullName { get; set; }
        public string Namespace { get; set; }
        public bool IsStatic { get; set; }
        public bool IsClass { get; set; }
        public bool IsInterface { get; set; }
        public bool IsEnum { get; set; }
        public bool IsStruct { get; set; }
        public bool IsGeneric { get; set; }
        public List<string> GenericParameters { get; set; } = new List<string>();
        public string BaseType { get; set; }
        public List<string> Interfaces { get; set; } = new List<string>();
        public List<NetMemberInfo> Members { get; set; } = new List<NetMemberInfo>();
        public List<NetMemberInfo> Constructors { get; set; } = new List<NetMemberInfo>();

        /// <summary>
        /// Get display name for the type (e.g., "List(Of T)" for generic types)
        /// </summary>
        public string DisplayName
        {
            get
            {
                if (IsGeneric && GenericParameters.Count > 0)
                {
                    var baseName = Name.Split('`')[0];
                    return $"{baseName}(Of {string.Join(", ", GenericParameters)})";
                }
                return Name;
            }
        }

        public override string ToString() => FullName ?? Name;
    }

    /// <summary>
    /// Lightweight representation of a .NET type member
    /// </summary>
    public class NetMemberInfo
    {
        public string Name { get; set; }
        public NetMemberKind Kind { get; set; }
        public string ReturnType { get; set; }
        public bool IsStatic { get; set; }
        public bool CanRead { get; set; }
        public bool CanWrite { get; set; }
        public bool IsExtensionMethod { get; set; }
        public bool IsGeneric { get; set; }
        public List<string> GenericParameters { get; set; } = new List<string>();
        public List<NetParameterInfo> Parameters { get; set; } = new List<NetParameterInfo>();

        public string GetSignature()
        {
            if (Kind == NetMemberKind.Method)
            {
                var genericStr = IsGeneric ? $"(Of {string.Join(", ", GenericParameters)})" : "";
                var paramStr = string.Join(", ", Parameters.Select(p => $"{p.Name} As {p.Type}"));
                return $"{Name}{genericStr}({paramStr}) As {ReturnType}";
            }
            return $"{Name} As {ReturnType}";
        }
    }

    /// <summary>
    /// Parameter information for method members
    /// </summary>
    public class NetParameterInfo
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public bool IsOptional { get; set; }
    }

    /// <summary>
    /// Kind of member in a .NET type
    /// </summary>
    public enum NetMemberKind
    {
        Method,
        Property,
        Field,
        Event,
        EnumValue,
        Constructor
    }
}
