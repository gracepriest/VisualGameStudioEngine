using System;
using System.Collections.Generic;
using System.Linq;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.StdLib.CSharp;
using BasicLang.Compiler.StdLib.Cpp;
using BasicLang.Compiler.StdLib.LLVM;
using BasicLang.Compiler.StdLib.MSIL;
using BasicLang.Compiler.StdLib.Framework;

namespace BasicLang.Compiler.StdLib
{
    /// <summary>
    /// Registry for standard library providers across all backends
    /// Provides unified access to stdlib implementations
    /// </summary>
    public static class StdLibRegistry
    {
        private static readonly Dictionary<TargetPlatform, List<IStdLibProvider>> _providers
            = new Dictionary<TargetPlatform, List<IStdLibProvider>>();

        private static bool _initialized = false;

        /// <summary>
        /// Initialize the registry with default providers
        /// </summary>
        public static void Initialize()
        {
            if (_initialized) return;

            // Register providers for each backend (order matters - checked first to last)
            Register(TargetPlatform.CSharp, new FrameworkStdLibProvider()); // Game engine first
            Register(TargetPlatform.CSharp, new CSharpStdLibProvider());
            Register(TargetPlatform.Cpp, new CppStdLibProvider());
            Register(TargetPlatform.LLVM, new LLVMStdLibProvider());
            Register(TargetPlatform.MSIL, new MSILStdLibProvider());

            _initialized = true;
        }

        /// <summary>
        /// Register a stdlib provider for a target platform
        /// </summary>
        public static void Register(TargetPlatform target, IStdLibProvider provider)
        {
            if (!_providers.ContainsKey(target))
                _providers[target] = new List<IStdLibProvider>();
            _providers[target].Add(provider);
        }

        /// <summary>
        /// Get all stdlib providers for a target platform
        /// </summary>
        public static IEnumerable<IStdLibProvider> GetProviders(TargetPlatform target)
        {
            Initialize();

            if (_providers.TryGetValue(target, out var providers))
                return providers;

            return Enumerable.Empty<IStdLibProvider>();
        }

        /// <summary>
        /// Get the first provider that can handle a function for a target platform
        /// </summary>
        public static IStdLibProvider GetProvider(TargetPlatform target)
        {
            Initialize();

            if (_providers.TryGetValue(target, out var providers) && providers.Count > 0)
                return providers[0];

            throw new NotSupportedException($"No stdlib provider registered for target: {target}");
        }

        /// <summary>
        /// Get provider that can handle a specific function
        /// </summary>
        public static IStdLibProvider GetProviderForFunction(TargetPlatform target, string functionName)
        {
            Initialize();

            if (_providers.TryGetValue(target, out var providers))
            {
                foreach (var provider in providers)
                {
                    if (provider.CanHandle(functionName))
                        return provider;
                }
            }

            return null;
        }

        /// <summary>
        /// Check if a function is a standard library function
        /// </summary>
        public static bool IsStdLibFunction(string functionName)
        {
            Initialize();
            return _providers.Values.SelectMany(p => p).Any(p => p.CanHandle(functionName));
        }

        /// <summary>
        /// Check if a specific backend can handle a stdlib function
        /// </summary>
        public static bool CanHandle(TargetPlatform target, string functionName)
        {
            Initialize();

            if (_providers.TryGetValue(target, out var providers))
                return providers.Any(p => p.CanHandle(functionName));

            return false;
        }

        /// <summary>
        /// Emit a stdlib function call for the specified backend
        /// </summary>
        public static string EmitCall(TargetPlatform target, string functionName, params string[] arguments)
        {
            var provider = GetProviderForFunction(target, functionName);
            if (provider == null)
                throw new NotSupportedException($"No provider found for function: {functionName}");
            return provider.EmitCall(functionName, arguments);
        }

        /// <summary>
        /// Get required imports for a stdlib function
        /// </summary>
        public static IEnumerable<string> GetRequiredImports(TargetPlatform target, string functionName)
        {
            var provider = GetProviderForFunction(target, functionName);
            if (provider == null)
                return Enumerable.Empty<string>();
            return provider.GetRequiredImports(functionName);
        }

        /// <summary>
        /// Get inline implementation if needed
        /// </summary>
        public static string GetInlineImplementation(TargetPlatform target, string functionName)
        {
            var provider = GetProviderForFunction(target, functionName);
            return provider?.GetInlineImplementation(functionName);
        }

        /// <summary>
        /// Check if game framework bindings are being used
        /// </summary>
        public static bool UsesFramework(TargetPlatform target, IEnumerable<string> functionNames)
        {
            Initialize();
            if (!_providers.TryGetValue(target, out var providers))
                return false;

            var frameworkProvider = providers.OfType<FrameworkStdLibProvider>().FirstOrDefault();
            if (frameworkProvider == null)
                return false;

            return functionNames.Any(f => frameworkProvider.CanHandle(f));
        }

        /// <summary>
        /// Get the Framework runtime helper code to include in generated code
        /// </summary>
        public static string GetFrameworkRuntimeCode()
        {
            return FrameworkStdLibProvider.GetRuntimeHelperCode();
        }

        /// <summary>
        /// Get all registered stdlib function names
        /// </summary>
        public static IEnumerable<string> GetAllFunctionNames()
        {
            Initialize();

            var functions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Collect all function names from all providers
            foreach (var category in Enum.GetValues<StdLibCategory>())
            {
                functions.UnionWith(GetFunctionsByCategory(category));
            }

            return functions.OrderBy(f => f);
        }

        /// <summary>
        /// Get stdlib functions by category
        /// </summary>
        public static IEnumerable<string> GetFunctionsByCategory(StdLibCategory category)
        {
            return category switch
            {
                StdLibCategory.IO => new[] { "Print", "PrintLine", "Input", "ReadLine" },
                StdLibCategory.String => new[] { "Len", "Mid", "Left", "Right", "UCase", "LCase", "Trim", "InStr", "Replace" },
                StdLibCategory.Math => new[] { "Abs", "Sqrt", "Pow", "Sin", "Cos", "Tan", "Log", "Exp", "Floor", "Ceiling", "Round", "Min", "Max", "Rnd", "Randomize" },
                StdLibCategory.Array => new[] { "UBound", "LBound", "Length", "ReDim" },
                StdLibCategory.Conversion => new[] { "CInt", "CLng", "CDbl", "CSng", "CStr", "CBool", "CChar" },
                StdLibCategory.System => Array.Empty<string>(),
                _ => Array.Empty<string>()
            };
        }

        /// <summary>
        /// Get the category of a stdlib function
        /// </summary>
        public static StdLibCategory? GetCategory(string functionName)
        {
            foreach (var category in Enum.GetValues<StdLibCategory>())
            {
                if (GetFunctionsByCategory(category).Contains(functionName, StringComparer.OrdinalIgnoreCase))
                    return category;
            }
            return null;
        }

        /// <summary>
        /// Generate a comparison report of stdlib support across backends
        /// </summary>
        public static string GenerateSupportMatrix()
        {
            Initialize();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Standard Library Support Matrix");
            sb.AppendLine("================================");
            sb.AppendLine();

            var targets = new[] { TargetPlatform.CSharp, TargetPlatform.Cpp, TargetPlatform.LLVM, TargetPlatform.MSIL };

            // Header
            sb.Append("Function".PadRight(15));
            foreach (var target in targets)
            {
                sb.Append($"| {target}".PadRight(10));
            }
            sb.AppendLine();
            sb.AppendLine(new string('-', 55));

            // Functions by category
            foreach (var category in Enum.GetValues<StdLibCategory>())
            {
                var functions = GetFunctionsByCategory(category);
                if (!functions.Any()) continue;

                sb.AppendLine($"--- {category} ---");

                foreach (var func in functions)
                {
                    sb.Append(func.PadRight(15));
                    foreach (var target in targets)
                    {
                        var supported = CanHandle(target, func) ? "Yes" : "No";
                        sb.Append($"| {supported}".PadRight(10));
                    }
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }
    }
}
