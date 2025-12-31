using System;
using System.Collections.Generic;

namespace BasicLang.Compiler.CodeGen
{
    /// <summary>
    /// Configuration options for code generation
    /// </summary>
    public class CodeGenOptions
    {
        /// <summary>
        /// Target backend platform
        /// </summary>
        public TargetPlatform TargetBackend { get; set; } = TargetPlatform.CSharp;

        /// <summary>
        /// Namespace for generated code (C#, C++)
        /// </summary>
        public string Namespace { get; set; } = "GeneratedCode";
        
        /// <summary>
        /// Class name for the main generated class
        /// </summary>
        public string ClassName { get; set; } = "Program";
        
        /// <summary>
        /// Whether to generate a Main method
        /// </summary>
        public bool GenerateMainMethod { get; set; } = true;
        
        /// <summary>
        /// Whether to generate comments in the output
        /// </summary>
        public bool GenerateComments { get; set; } = true;
        
        /// <summary>
        /// Access modifier for generated methods (public, private, internal)
        /// </summary>
        public string MethodAccessModifier { get; set; } = "public";
        
        /// <summary>
        /// Access modifier for generated classes (public, private, internal)
        /// </summary>
        public string ClassAccessModifier { get; set; } = "public";
        
        /// <summary>
        /// Number of spaces per indentation level
        /// </summary>
        public int IndentSize { get; set; } = 4;
        
        /// <summary>
        /// Whether to use tabs instead of spaces
        /// </summary>
        public bool UseTabs { get; set; } = false;
        
        /// <summary>
        /// Whether to inline IR temporaries into expressions (reduces t0/t1 style temps)
        /// </summary>
        public bool InlineTemporaries { get; set; } = true;

        /// <summary>
        /// Whether to generate XML documentation comments
        /// </summary>
        public bool GenerateXmlDocs { get; set; } = false;

        /// <summary>
        /// C# language version (default: 8.0 for default interface methods support)
        /// </summary>
        public string CSharpLanguageVersion { get; set; } = "8.0";

        /// <summary>
        /// Whether to generate LINQ queries using query syntax instead of method syntax
        /// </summary>
        public bool UseLinqQuerySyntax { get; set; } = true;

        /// <summary>
        /// Backend-specific options dictionary
        /// </summary>
        public Dictionary<string, object> BackendOptions { get; set; } = new Dictionary<string, object>();

        /// <summary>
        /// Get a backend-specific option with default value
        /// </summary>
        public T GetBackendOption<T>(string key, T defaultValue = default)
        {
            if (BackendOptions.TryGetValue(key, out var value) && value is T typedValue)
                return typedValue;
            return defaultValue;
        }

        /// <summary>
        /// Set a backend-specific option
        /// </summary>
        public void SetBackendOption(string key, object value)
        {
            BackendOptions[key] = value;
        }
    }
}
