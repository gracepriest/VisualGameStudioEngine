using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BasicLang.Compiler.IR;

namespace BasicLang.Compiler.CodeGen.CPlusPlus
{
    /// <summary>
    /// Result of split emission for mixed BasicLang+C++ projects: one runtime header, one
    /// aggregate declarations header, per-module shim headers, and per-module definition
    /// translation units (design decisions D1-D5, D11).
    /// </summary>
    public sealed class CppSplitResult
    {
        // fileName → content. OrdinalIgnoreCase: generated files land on Windows-y
        // case-insensitive file systems, so "game.g.h" and "Game.g.h" are the SAME file —
        // colliding names must throw (via Add), never silently coexist or overwrite.
        public Dictionary<string, string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool HasBasicLangMain { get; set; }
        public bool UsesFramework { get; set; }
        public string ProjectHeaderFileName { get; set; } = "";
        public List<string> TranslationUnitFileNames { get; } = new();     // .g.cpp files to compile
    }

    public partial class CppCodeGenerator
    {
        internal const string RuntimeHeaderFileName = "BasicLangRuntime.g.h";

        /// <summary>
        /// Split-emission counterpart of <see cref="Generate"/>: instead of one combined
        /// translation unit, emits BasicLangRuntime.g.h + &lt;Project&gt;.g.h (all types,
        /// declarations, templates, inline data) + per-module shim headers + per-module
        /// .g.cpp definition files, so user C++ files can <c>#include "&lt;Module&gt;.g.h"</c>.
        /// <paramref name="combined"/> is the already-optimized combined IR;
        /// <paramref name="unitModules"/> supplies only the module-name roster.
        /// PRECONDITION: <paramref name="projectName"/> must be filename-safe — it is used
        /// verbatim in emitted file names (&lt;Project&gt;.g.h etc.). Callers (the Task-4
        /// builder passes AssemblyName/ProjectName) own that guarantee; no transformation
        /// happens here. A module name that collides with a reserved output file name
        /// (BasicLangRuntime, &lt;Project&gt;.main, &lt;Project&gt;.__shared, any case) throws
        /// <see cref="ArgumentException"/> rather than silently overwriting output.
        /// </summary>
        public CppSplitResult GenerateSplit(IRModule combined, string projectName,
                                            IReadOnlyList<IRModule> unitModules, bool emitMain)
        {
            var capabilityDiags = new CppCapabilityChecker().Check(combined);
            if (capabilityDiags.Count > 0)
                throw new CppCapabilityException(capabilityDiags);

            // Same per-Generate state reset as Generate()
            _module = combined;
            _output.Clear();
            _valueNames.Clear();
            _allTemporaries.Clear();
            _usesFramework = false;
            _frameworkFunctionsUsed.Clear();
            _tempCounter = 0;

            var result = new CppSplitResult();
            var projectHeaderName = projectName + ".g.h";
            result.ProjectHeaderFileName = projectHeaderName;

            var standaloneFunctions = combined.Functions
                .Where(f => !f.IsExternal && !f.IsLambda && !IsClassMethod(f, combined))
                .ToList();
            // Template functions live entirely in the header (decl + def); everything else
            // gets a header declaration and a per-module definition.
            var templateFunctions = standaloneFunctions
                .Where(f => f.GenericParameters != null && f.GenericParameters.Count > 0)
                .ToList();
            var nonTemplateFunctions = standaloneFunctions
                .Where(f => f.GenericParameters == null || f.GenericParameters.Count == 0)
                .ToList();

            result.HasBasicLangMain = standaloneFunctions
                .Any(f => f.Name.Equals("Main", StringComparison.OrdinalIgnoreCase));

            // Module-name roster (which shims/definition TUs exist); D11: OrdinalIgnoreCase.
            var moduleNames = new List<string>();
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var unit in unitModules)
            {
                if (!string.IsNullOrEmpty(unit?.Name) && seenNames.Add(unit.Name))
                    moduleNames.Add(unit.Name);
            }

            // Preflight: reject module names that would emit onto a reserved output file name.
            // Actionable message naming the MODULE (Task 4 surfaces it verbatim as a diagnostic);
            // the Files.Add calls below remain as a backstop for anything this misses.
            foreach (var name in moduleNames)
            {
                string reservedFile = null;
                if (name.Equals("BasicLangRuntime", StringComparison.OrdinalIgnoreCase))
                    reservedFile = RuntimeHeaderFileName;
                else if (name.Equals(projectName + ".main", StringComparison.OrdinalIgnoreCase))
                    reservedFile = projectName + ".main.g.cpp";
                else if (name.Equals(projectName + ".__shared", StringComparison.OrdinalIgnoreCase))
                    reservedFile = projectName + ".__shared.g.cpp";
                if (reservedFile != null)
                    throw new ArgumentException(
                        $"Module '{name}' collides with reserved generated file '{reservedFile}'.");
            }

            var buckets = new Dictionary<string, List<IRFunction>>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in moduleNames)
                buckets[name] = new List<IRFunction>();
            var sharedBucket = new List<IRFunction>();
            foreach (var fn in nonTemplateFunctions)
            {
                if (!string.IsNullOrEmpty(fn.ModuleName) && buckets.TryGetValue(fn.ModuleName, out var bucket))
                    bucket.Add(fn);
                else
                    sharedBucket.Add(fn);
            }

            // Definition sections are captured FIRST so _usesFramework and the framework
            // call set reflect real usage before the runtime header renders LAST.
            result.Files.Add(projectHeaderName, CaptureSection(() =>
                EmitAggregateHeader(combined, standaloneFunctions, templateFunctions)));

            foreach (var name in moduleNames)
            {
                var functions = buckets[name];
                if (functions.Count == 0) continue;   // module contributes no definitions
                var fileName = name + ".g.cpp";
                result.Files.Add(fileName, CaptureSection(() => EmitDefinitionUnit(projectHeaderName, functions)));
                result.TranslationUnitFileNames.Add(fileName);
            }

            if (sharedBucket.Count > 0)
            {
                var fileName = projectName + ".__shared.g.cpp";
                result.Files.Add(fileName, CaptureSection(() => EmitDefinitionUnit(projectHeaderName, sharedBucket)));
                result.TranslationUnitFileNames.Add(fileName);
            }

            if (emitMain)
            {
                var fileName = projectName + ".main.g.cpp";
                result.Files.Add(fileName, CaptureSection(() =>
                {
                    WriteLine($"#include \"{projectHeaderName}\"");
                    WriteLine();
                    GenerateMainFunction(combined);
                }));
                result.TranslationUnitFileNames.Add(fileName);
            }

            // Per-module shim headers. Skip a module named like the project (D11,
            // OrdinalIgnoreCase): <Module>.g.h and <Project>.g.h are the same file on
            // Windows and the shim would silently overwrite the aggregate header.
            foreach (var name in moduleNames)
            {
                if (name.Equals(projectName, StringComparison.OrdinalIgnoreCase)) continue;
                result.Files.Add(name + ".g.h", CaptureSection(() =>
                {
                    WriteLine("#pragma once");
                    WriteLine($"#include \"{projectHeaderName}\"");
                }));
            }

            result.Files.Add(RuntimeHeaderFileName, CaptureSection(() => EmitRuntimeHeader(combined)));
            result.UsesFramework = _usesFramework;
            return result;
        }

        /// <summary>
        /// Swap <see cref="_output"/> to a fresh buffer, run <paramref name="emit"/> at indent
        /// level 0, and return the captured text (buffer and indent restored even on throw).
        /// </summary>
        private string CaptureSection(Action emit)
        {
            var savedOutput = _output;
            var savedIndent = _indentLevel;
            _output = new StringBuilder();
            try
            {
                _indentLevel = 0;
                emit();
                return _output.ToString();
            }
            finally
            {
                _output = savedOutput;
                _indentLevel = savedIndent;
            }
        }

        /// <summary>
        /// The ONE aggregate declarations header (D1): all types with inline bodies,
        /// inline data definitions (D3), extern "C" user declarations, standalone function
        /// declarations, and full definitions of template standalone functions.
        /// </summary>
        private void EmitAggregateHeader(IRModule module, List<IRFunction> standaloneFunctions,
                                         List<IRFunction> templateFunctions)
        {
            // KEEP IN SYNC with Generate() section order (CppCodeGenerator.cs): forward decls →
            // enums → delegates → interfaces → classes → static inits → globals → externs → functions.
            WriteLine("#pragma once");
            WriteLine($"#include \"{RuntimeHeaderFileName}\"");
            WriteLine();

            if (module.Classes.Count > 0)
            {
                WriteLine("// Forward declarations");
                foreach (var irClass in module.Classes.Values)
                {
                    var fwdTemplate = TemplatePrefix(irClass.GenericParameters);
                    if (fwdTemplate != null) WriteLine(fwdTemplate);
                    WriteLine($"{(irClass.IsStruct ? "struct" : "class")} {SanitizeName(irClass.Name)};");
                }
                WriteLine();
            }

            if (module.Enums.Count > 0)
            {
                WriteLine("// Enums");
                foreach (var irEnum in module.Enums.Values)
                {
                    GenerateEnum(irEnum);
                    WriteLine();
                }
            }

            if (module.Delegates.Count > 0)
            {
                WriteLine("// Delegate types");
                foreach (var irDelegate in module.Delegates.Values)
                {
                    GenerateDelegate(irDelegate);
                }
                WriteLine();
            }

            if (module.Interfaces.Count > 0)
            {
                WriteLine("// Interfaces (abstract classes)");
                foreach (var irInterface in module.Interfaces.Values)
                {
                    GenerateInterface(irInterface);
                    WriteLine();
                }
            }

            if (module.Classes.Count > 0)
            {
                WriteLine("// Classes");
                foreach (var irClass in module.Classes.Values)
                {
                    GenerateClass(irClass);
                    WriteLine();
                }

                // Static member definitions as C++17 inline variables: header-safe under
                // multiple inclusion across translation units (D3).
                WriteLine("// Static member initializations");
                foreach (var irClass in module.Classes.Values)
                {
                    EmitInlineStaticMemberInitializations(irClass);
                }
                if (module.Classes.Values.Any(c => c.Fields.Any(f => f.IsStatic)))
                    WriteLine();
            }

            if (module.GlobalVariables.Count > 0)
            {
                WriteLine("// Global variables (inline: one definition across all TUs, D3)");
                foreach (var globalVar in module.GlobalVariables.Values)
                {
                    var type = MapType(globalVar.Type);
                    var name = SanitizeName(globalVar.Name);
                    WriteLine($"inline {type} {name} = {{}};");
                }
                WriteLine();
            }

            if (module.ExternDeclarations.Count > 0)
            {
                WriteLine("// External C library declarations");
                WriteLine("extern \"C\" {");
                Indent();
                foreach (var externDecl in module.ExternDeclarations.Values)
                {
                    GenerateExternDeclaration(externDecl);
                }
                Unindent();
                WriteLine("}");
                WriteLine();
            }

            if (standaloneFunctions.Count > 0)
            {
                WriteLine("// Function declarations");
                foreach (var function in standaloneFunctions)
                {
                    GenerateFunctionDeclaration(function);
                }
                WriteLine();
            }

            if (templateFunctions.Count > 0)
            {
                WriteLine("// Template function definitions (templates must live in the header)");
                foreach (var function in templateFunctions)
                {
                    GenerateFunction(function);
                    WriteLine();
                }
            }
        }

        /// <summary>
        /// Split-mode variant of GenerateStaticMemberInitializations: `inline` prefix makes
        /// the out-of-class definition header-safe (C++17 inline variables, D3). Delegates to
        /// the shared core in CppCodeGenerator.cs — one emission shape, two prefixes.
        /// </summary>
        private void EmitInlineStaticMemberInitializations(IRClass irClass)
        {
            EmitStaticMemberInitializationsCore(irClass, prefix: "inline ");
        }

        /// <summary>One per-module .g.cpp: the module's non-template standalone definitions.</summary>
        private void EmitDefinitionUnit(string projectHeaderName, List<IRFunction> functions)
        {
            WriteLine($"#include \"{projectHeaderName}\"");
            WriteLine();
            foreach (var function in functions)
            {
                GenerateFunction(function);
                WriteLine();
            }
        }

        /// <summary>
        /// BasicLangRuntime.g.h: full std-include superset, user #CppInclude tokens,
        /// `using namespace std;` (D2), all runtime support pieces unconditionally, and the
        /// FULL framework extern catalog (D5) — declarations cost nothing, and user C++
        /// translation units include this header too.
        /// </summary>
        private void EmitRuntimeHeader(IRModule module)
        {
            WriteLine("#pragma once");
            WriteLine("// requires -std=c++20 (coroutines)");

            // "iterator": Generator<T> uses std::default_sentinel_t (declared in <iterator>);
            // split mode compiles the runtime in every build, so don't rely on transitive includes.
            var includes = new HashSet<string>
            {
                "iostream", "vector", "string", "cstdint", "cmath", "algorithm", "cstdlib",
                "ctime", "functional", "coroutine", "exception", "iterator",
                "unordered_map", "unordered_set", "stdexcept"
            };
            foreach (var inc in _headerIncludes)
            {
                includes.Add(inc);
            }
            foreach (var include in includes)
            {
                WriteLine($"#include <{include}>");
            }

            // User #CppInclude passthrough headers (tokens carry their own <>/"" delimiters),
            // deduped, before using-namespace — same contract as the combined emission.
            foreach (var tok in module.CppIncludes.Distinct())
            {
                WriteLine($"#include {tok}");
            }

            WriteLine();
            WriteLine("using namespace std;");
            WriteLine();

            SpliceRuntimeSource(CppRuntimeSources.DotNetSurfaceHelpers);

            WriteLine("namespace BasicLang {");
            SpliceRuntimeSource(CppRuntimeSources.TaskEmulation);
            SpliceRuntimeSource(CppRuntimeSources.GeneratorCoroutine);
            WriteLine("}");
            WriteLine();

            SpliceRuntimeSource(CppCollectionsRuntime.Source);

            EmitFrameworkCatalog();
        }

        /// <summary>The FULL Framework_* extern "C" catalog (D5), unconditionally.</summary>
        private void EmitFrameworkCatalog()
        {
            WriteLine("// VisualGameStudioEngine framework function declarations");
            WriteLine("// Link with VisualGameStudioEngine.dll");
            WriteLine("#ifndef FRAMEWORK_API");
            WriteLine("#ifdef _WIN32");
            WriteLine("#define FRAMEWORK_API __declspec(dllimport)");
            WriteLine("#else");
            WriteLine("#define FRAMEWORK_API");
            WriteLine("#endif");
            WriteLine("#define BASICLANG_OWNS_FRAMEWORK_API");
            WriteLine("#endif");
            WriteLine();
            WriteLine("extern \"C\" {");
            foreach (var pair in FrameworkSignatures.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                WriteLine($"    FRAMEWORK_API {pair.Value};");
            }
            WriteLine("}");
            // Don't leak the helper macro into user translation units that include this header —
            // but only undef what WE defined; an externally pre-defined FRAMEWORK_API (which the
            // #ifndef above honored) belongs to the user and survives.
            WriteLine("#ifdef BASICLANG_OWNS_FRAMEWORK_API");
            WriteLine("#undef FRAMEWORK_API");
            WriteLine("#undef BASICLANG_OWNS_FRAMEWORK_API");
            WriteLine("#endif");
        }
    }
}
