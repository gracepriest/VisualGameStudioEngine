using System;
using System.Collections.Generic;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.CodeGen.LLVM;
using BasicLang.Compiler.CodeGen.MSIL;

namespace BasicLang.Compiler.CodeGen
{
    /// <summary>
    /// Registry for code generator backends
    /// Provides factory pattern for backend instantiation
    /// </summary>
    public static class BackendRegistry
    {
        private static readonly Dictionary<TargetPlatform, Func<CodeGenOptions, ICodeGenerator>> _factories
            = new Dictionary<TargetPlatform, Func<CodeGenOptions, ICodeGenerator>>();

        private static readonly Dictionary<string, TargetPlatform> _nameToTarget
            = new Dictionary<string, TargetPlatform>(StringComparer.OrdinalIgnoreCase);

        private static bool _initialized = false;

        /// <summary>
        /// Initialize the registry with default backends
        /// </summary>
        public static void Initialize()
        {
            if (_initialized) return;

            // Register C# backend
            Register(TargetPlatform.CSharp, "C#", opts => new CSharpCodeGenerator(opts));
            Register(TargetPlatform.CSharp, "CSharp", opts => new CSharpCodeGenerator(opts));

            // Register C++ backend
            Register(TargetPlatform.Cpp, "C++", opts => new CppCodeGenerator(new CppCodeGenOptions
            {
                IndentSize = opts.IndentSize,
                GenerateComments = opts.GenerateComments,
                GenerateMainFunction = opts.GenerateMainMethod
            }));
            Register(TargetPlatform.Cpp, "Cpp", opts => new CppCodeGenerator(new CppCodeGenOptions
            {
                IndentSize = opts.IndentSize,
                GenerateComments = opts.GenerateComments,
                GenerateMainFunction = opts.GenerateMainMethod
            }));

            // Register LLVM backend
            Register(TargetPlatform.LLVM, "LLVM", opts => new LLVMCodeGenerator(new LLVMCodeGenOptions
            {
                GenerateComments = opts.GenerateComments
            }));
            Register(TargetPlatform.LLVM, "llvm", opts => new LLVMCodeGenerator(new LLVMCodeGenOptions
            {
                GenerateComments = opts.GenerateComments
            }));

            // Register MSIL backend
            Register(TargetPlatform.MSIL, "MSIL", opts => new MSILCodeGenerator(new MSILCodeGenOptions
            {
                GenerateComments = opts.GenerateComments,
                AssemblyName = opts.ClassName ?? "GeneratedAssembly"
            }));
            Register(TargetPlatform.MSIL, "IL", opts => new MSILCodeGenerator(new MSILCodeGenOptions
            {
                GenerateComments = opts.GenerateComments,
                AssemblyName = opts.ClassName ?? "GeneratedAssembly"
            }));

            _initialized = true;
        }

        /// <summary>
        /// Register a backend factory
        /// </summary>
        public static void Register(TargetPlatform target, string name, Func<CodeGenOptions, ICodeGenerator> factory)
        {
            _factories[target] = factory;
            _nameToTarget[name] = target;
        }

        /// <summary>
        /// Create a code generator for the specified target
        /// </summary>
        public static ICodeGenerator Create(TargetPlatform target, CodeGenOptions options = null)
        {
            Initialize(); // Ensure backends are registered
            options ??= new CodeGenOptions();

            if (!_factories.TryGetValue(target, out var factory))
                throw new NotSupportedException($"Backend not registered for target: {target}");

            return factory(options);
        }

        /// <summary>
        /// Create a code generator by name
        /// </summary>
        public static ICodeGenerator Create(string backendName, CodeGenOptions options = null)
        {
            Initialize(); // Ensure backends are registered

            if (!_nameToTarget.TryGetValue(backendName, out var target))
                throw new NotSupportedException($"Unknown backend: {backendName}");

            return Create(target, options);
        }

        /// <summary>
        /// Check if a backend is registered
        /// </summary>
        public static bool IsRegistered(TargetPlatform target) => _factories.ContainsKey(target);

        /// <summary>
        /// Check if a backend is registered by name
        /// </summary>
        public static bool IsRegistered(string backendName) => _nameToTarget.ContainsKey(backendName);

        /// <summary>
        /// Get all registered backend names
        /// </summary>
        public static IEnumerable<string> GetRegisteredBackends() => _nameToTarget.Keys;

        /// <summary>
        /// Get target platform from name
        /// </summary>
        public static TargetPlatform? GetTarget(string name)
        {
            if (_nameToTarget.TryGetValue(name, out var target))
                return target;
            return null;
        }
    }
}
