using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;
using GenericTypeParameter = BasicLang.Compiler.AST.GenericTypeParameter;

namespace BasicLang.Compiler.CodeGen.CPlusPlus
{
    /// <summary>
    /// C++ code generator - transpiles IR to C++
    /// Targets C++17 for modern language features
    /// </summary>
    public partial class CppCodeGenerator : CodeGeneratorBase
    {
        // Not readonly: swapped temporarily while rendering inline lambda bodies
        private StringBuilder _output;
        // #line directive state (EmitLineDirectives): dedupe of consecutive same-line
        // instructions, the captured file's own name (reset target for synthesized code),
        // and suppression while a lambda body is rendered into a side buffer (a #line
        // there would land mid-expression and break the compile).
        private int _lastEmittedSourceLine = -1;
        private string _lastEmittedSourceFile;      // normalized (forward slashes)
        private string _currentGeneratedFileName;   // set per captured split file; module-derived in Generate()
        private bool _suppressLineDirectives;       // true while capturing inline lambda bodies
        private readonly CppCodeGenOptions _options;
        private readonly HashSet<IRValue> _allTemporaries;
        private readonly List<string> _headerIncludes;
        private readonly HashSet<string> _declaredIdentifiers;
        private IRClass _emittingClass;
        // Foreign ::-qualified instance method calls that are the RECEIVER of another
        // member access (e.g. the inner m.foo() in m.foo().bar()). These are rendered
        // inline as expressions by GetValueName and must NOT also emit a standalone
        // statement, so chained opaque access collapses to one C++ expression.
        private readonly HashSet<IRValue> _inlinedForeignCalls = new HashSet<IRValue>();
        // Foreign ::-qualified locals that carry an initializer: their standalone
        // declaration is suppressed and the type is emitted at the first assignment, so
        // `Dim it As some::type = expr` becomes a single `some::type it = expr;`. Foreign
        // types may be non-default-constructible or non-assignable, so declare-then-assign
        // (the default local path) can fail to compile. Consumed here as a work set — a
        // name is removed once its combined decl-init has been emitted.
        private readonly HashSet<string> _foreignLocalsInitInline =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _foreignLocalTypeByName =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // §11.1 `<catchVar>.Message`: the IRFieldAccess nodes that read `Message` off a
        // catch variable INSIDE that clause's own body region. Collected per function
        // (InitializeFunctionContext) because their result temps must already be
        // declared std::string by DeclareLocalsAndTemporaries — the analyzer types the
        // access Object/unknown for most exception types, which would declare void*.
        // Visit(IRFieldAccess) lowers members of this set to BasicLang::String(v.what()).
        // Strictly catch-variable-scoped: no general exception-object model exists.
        private readonly HashSet<IRValue> _catchMessageAccesses = new HashSet<IRValue>();
        private bool _usesFramework;
        private readonly HashSet<string> _frameworkFunctionsUsed;
        private IRModule _module;

        public override string BackendName => "C++";
        public override TargetPlatform Target => TargetPlatform.Cpp;

        /// <summary>True when the last Generate/GenerateSplit saw Framework_* calls (link decision input).</summary>
        internal bool UsesFramework => _usesFramework;

        public CppCodeGenerator(CppCodeGenOptions options = null)
        {
            _output = new StringBuilder();
            _options = options ?? new CppCodeGenOptions();
            _allTemporaries = new HashSet<IRValue>();
            _headerIncludes = new List<string> { "iostream", "vector", "string", "memory" };
            _declaredIdentifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _frameworkFunctionsUsed = new HashSet<string>();
            _typeMapper = new CppTypeMapper();
        }
        
        public override string Generate(IRModule module)
        {
            var capabilityDiags = new CppCapabilityChecker().Check(module);
            if (capabilityDiags.Count > 0)
                throw new CppCapabilityException(capabilityDiags);

            _module = module;
            _output.Clear();
            _valueNames.Clear();
            _allTemporaries.Clear();
            _usesFramework = false;
            _frameworkFunctionsUsed.Clear();
            _tempCounter = 0;
            // Single-file path (unit tests, demos): the whole module is one notional file.
            // The real build goes through GenerateSplit, where CaptureSection sets this
            // per captured file.
            _currentGeneratedFileName = module.Name + ".g.cpp";
            _lastEmittedSourceLine = -1;
            _lastEmittedSourceFile = null;

            // Generate header
            GenerateHeader(module);

            // KEEP IN SYNC with GenerateSplit/EmitAggregateHeader (CppCodeGenerator.Split.cs):
            // the section order below (forward decls → enums → delegates → interfaces →
            // classes → static inits → globals → externs → functions) is mirrored there.
            // Generate forward declarations for classes
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

            // Generate enums
            if (module.Enums.Count > 0)
            {
                WriteLine("// Enums");
                foreach (var irEnum in module.Enums.Values)
                {
                    GenerateEnum(irEnum);
                    WriteLine();
                }
            }

            // Generate delegate types (using std::function)
            if (module.Delegates.Count > 0)
            {
                WriteLine("// Delegate types");
                foreach (var irDelegate in module.Delegates.Values)
                {
                    GenerateDelegate(irDelegate);
                }
                WriteLine();
            }

            // Generate interfaces (abstract classes)
            if (module.Interfaces.Count > 0)
            {
                WriteLine("// Interfaces (abstract classes)");
                foreach (var irInterface in module.Interfaces.Values)
                {
                    GenerateInterface(irInterface);
                    WriteLine();
                }
            }

            // Generate classes
            if (module.Classes.Count > 0)
            {
                WriteLine("// Classes");
                foreach (var irClass in module.Classes.Values)
                {
                    GenerateClass(irClass);
                    WriteLine();
                }

                // Generate static member initializations outside class
                WriteLine("// Static member initializations");
                foreach (var irClass in module.Classes.Values)
                {
                    GenerateStaticMemberInitializations(irClass);
                }
                if (module.Classes.Values.Any(c => c.Fields.Any(f => f.IsStatic)))
                    WriteLine();
            }

            // Generate global variables
            if (module.GlobalVariables.Count > 0)
            {
                WriteLine("// Global variables");
                foreach (var globalVar in module.GlobalVariables.Values)
                {
                    var type = MapType(globalVar.Type);
                    var name = SanitizeName(globalVar.Name);
                    WriteLine($"{type} {name} = {{}};");
                }
                WriteLine();
            }

            // Generate extern "C" declarations for C library interop
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

            // Get standalone functions (not class methods; lambdas are inlined at use sites)
            var standaloneFunctions = module.Functions
                .Where(f => !f.IsExternal && !f.IsLambda && !IsClassMethod(f, module))
                .ToList();

            // Generate function declarations
            if (standaloneFunctions.Count > 0)
            {
                WriteLine("// Function declarations");
                foreach (var function in standaloneFunctions)
                {
                    GenerateFunctionDeclaration(function);
                }
                WriteLine();

                // Generate function implementations
                WriteLine("// Function implementations");
                foreach (var function in standaloneFunctions)
                {
                    GenerateFunction(function);
                    WriteLine();
                }
            }

            // Generate main function if requested
            if (_options.GenerateMainFunction)
            {
                GenerateMainFunction(module);
            }

            var result = _output.ToString();

            // Insert framework extern declarations if game framework functions were used
            if (_usesFramework)
            {
                var marker = "using namespace std;";
                var insertIdx = result.IndexOf(marker);
                if (insertIdx >= 0)
                {
                    // Find the end of the line containing "using namespace std;"
                    var lineEnd = result.IndexOf('\n', insertIdx);
                    if (lineEnd < 0) lineEnd = result.Length;
                    else lineEnd++; // include the \n
                    result = result.Insert(lineEnd, "\n" + GenerateFrameworkExternDeclarations() + "\n");
                }
            }

            return result;
        }

        /// <summary>
        /// True when <paramref name="function"/> is a class member (method / constructor /
        /// property accessor) rather than a standalone function — class-member
        /// implementations also live in <c>module.Functions</c>, so free-function passes
        /// filter them out with this. Static + internal so the ProjectSystem builder can
        /// count standalone BasicLang mains with the identical rule. Null-hardened so a
        /// partially-built IR unit can't NRE it.
        /// </summary>
        internal static bool IsClassMethod(IRFunction function, IRModule module)
        {
            if (module?.Classes == null)
                return false;
            foreach (var irClass in module.Classes.Values)
            {
                if (irClass.Methods != null && irClass.Methods.Any(m => m.Implementation == function))
                    return true;
                if (irClass.Constructors != null && irClass.Constructors.Any(c => c.Implementation == function))
                    return true;
                if (irClass.Properties != null && irClass.Properties.Any(p => p.Getter == function || p.Setter == function))
                    return true;
            }
            return false;
        }
        
        private void GenerateHeader(IRModule module)
        {
            var hasAsync = module.Functions.Any(f => f.IsAsync);
            var hasIterators = module.Functions.Any(f => f.IsIterator);
            var usesCollections = ModuleUsesCollections(module);

            WriteLine("#pragma once");
            if (hasIterators)
                WriteLine("// requires -std=c++20 (coroutines)");

            // Collect unique includes. cstdio/cstring/ostream/stdexcept are unconditional
            // because the always-spliced P1 BCL runtime bodies (bl_bcltypes/bl_decimal,
            // see below) need them — the spliced consts are include-free by contract
            // (CppBclRuntimeTests pins that), so the generator owns their std headers.
            var includes = new HashSet<string> { "iostream", "vector", "string", "cstdint", "cmath", "algorithm", "cstdlib", "ctime", "functional", "cstdio", "cstring", "ostream", "stdexcept" };
            if (hasIterators)
            {
                includes.Add("coroutine");
                includes.Add("exception");
            }
            if (usesCollections)
            {
                includes.Add("unordered_map");
                includes.Add("unordered_set");
            }
            foreach (var inc in _headerIncludes)
            {
                includes.Add(inc);
            }

            foreach (var include in includes)
            {
                WriteLine($"#include <{include}>");
            }

            // User #CppInclude passthrough headers. Each token already carries its
            // delimiters (<...> or "...") so angle-vs-quote form survives to emission.
            // Distinct() removes duplicate/repeated headers while preserving first-seen order.
            foreach (var tok in module.CppIncludes.Distinct())
            {
                WriteLine($"#include {tok}");
            }

            WriteLine();
            WriteLine("using namespace std;");
            WriteLine();

            if (hasAsync || hasIterators || usesCollections)
                EmitRuntimePreamble(hasAsync, hasIterators, usesCollections);

            // P1 native BCL runtime (spec §12): spliced UNCONDITIONALLY in both emission
            // modes (split-mode counterpart: EmitRuntimeHeader in CppCodeGenerator.Split.cs —
            // keep them in sync). Like the collections runtime, each body opens its OWN
            // `namespace BasicLang { … }` (a sibling re-open at file scope, indent 0) and its
            // std::hash specializations follow the namespace close. The bodies are
            // include-free; their std headers live in the unconditional include set above.
            SpliceRuntimeSource(CppBclRuntime.BclBody);
            SpliceRuntimeSource(CppDecimalRuntime.DecimalBody);

            // §11.1 NetException + the BasicLang::String alias: UNCONDITIONAL in both
            // modes — the typed-catch ladder's trigger is source-level (any .NET-typed
            // Catch), not surface-level, so the declaration must always exist
            // (split-mode counterpart: EmitRuntimeHeader in CppCodeGenerator.Split.cs).
            SpliceRuntimeSource(CppNetExceptionRuntime.Source);
        }

        /// <summary>
        /// True when the module references List/Dictionary/HashSet anywhere in a function's
        /// return type, parameters, locals, or a class field type (including nested generic
        /// arguments and array element types). Also scans IRNewObject class names inside
        /// function bodies as a fallback, since a `Dim l As New List(...)` temporary/local may
        /// not always carry the collection type on its declared local. Drives emission of the
        /// wrapper runtime preamble and the extra std headers it needs.
        /// </summary>
        private static bool ModuleUsesCollections(IRModule module)
        {
            bool IsColl(TypeInfo t)
            {
                if (t == null) return false;
                // Case-insensitive: BasicLang is VB-style and TypeInfo.Name preserves source
                // casing, so `list`/`LIST` must match the same as `List`.
                if (string.Equals(t.Name, "List", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(t.Name, "Dictionary", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(t.Name, "HashSet", StringComparison.OrdinalIgnoreCase)) return true;
                if (t.GenericArguments != null && t.GenericArguments.Any(IsColl)) return true;
                return IsColl(t.ElementType);
            }

            // Every type-bearing position in the module (functions, GLOBALS, class
            // members, interfaces) via the shared walker — keep in sync with
            // ForeignFeatureChecker / CppCapabilityChecker. Previously this missed
            // module.GlobalVariables, so a file-scope `Dim g As List(Of Integer)`
            // emitted `BasicLang::List<...> g;` with no runtime preamble (broken C++).
            if (ModuleTypeWalker.AllTypes(module).Any(IsColl)) return true;

            // Fallback: `New List(...)` construction in a function body (a temporary/
            // local may not carry the collection type on its declared local).
            foreach (var f in module.Functions)
                foreach (var block in f.Blocks)
                    foreach (var instr in block.Instructions)
                        if (instr is IRNewObject no
                            && (no.ClassName == "List" || no.ClassName == "Dictionary" || no.ClassName == "HashSet"))
                            return true;

            return false;
        }

        /// <summary>
        /// Runtime support types for async (synchronous Task&lt;T&gt; emulation - type-correct,
        /// no scheduler) and iterators (C++20 coroutine Generator&lt;T&gt;).
        /// </summary>
        private void EmitRuntimePreamble(bool hasAsync, bool hasIterators, bool usesCollections)
        {
            // Only open the async/iterator namespace when there is something to put in it;
            // otherwise a collections-only module would emit an empty `namespace BasicLang { }`.
            if (hasAsync || hasIterators)
            {
                WriteLine("namespace BasicLang {");
                if (hasAsync)
                    SpliceRuntimeSource(CppRuntimeSources.TaskEmulation);
                if (hasIterators)
                    SpliceRuntimeSource(CppRuntimeSources.GeneratorCoroutine);
                WriteLine("}");
                WriteLine();
            }

            // The collections runtime opens its OWN `namespace BasicLang { … }`, so it is
            // emitted OUTSIDE the block just closed above (a sibling namespace re-open) to
            // avoid double-nesting into BasicLang::BasicLang::List.
            if (usesCollections)
                SpliceRuntimeSource(CppCollectionsRuntime.Source);
        }

        /// <summary>
        /// Emit a pre-indented runtime source const line-by-line through WriteLine (indent
        /// level must be 0: the consts carry their own indentation).
        /// </summary>
        private void SpliceRuntimeSource(string source)
        {
            foreach (var line in source.Split('\n'))
                WriteLine(line.TrimEnd('\r'));
        }

        /// <summary>
        /// Return type for a function signature: iterators return BasicLang::Generator&lt;T&gt;,
        /// async functions return BasicLang::Task&lt;T&gt; (wrapping bare declared types).
        /// </summary>
        private string MapReturnType(IRFunction function)
        {
            if (function.IsIterator)
            {
                var element = (function.ReturnType?.GenericArguments != null && function.ReturnType.GenericArguments.Count > 0)
                    ? MapType(function.ReturnType.GenericArguments[0])
                    : "int32_t";
                return $"BasicLang::Generator<{element}>";
            }

            var mapped = MapType(function.ReturnType);
            if (function.IsAsync && !mapped.StartsWith("BasicLang::Task<"))
                return mapped == "void" ? "BasicLang::Task<void>" : $"BasicLang::Task<{mapped}>";
            return mapped;
        }

        private void GenerateEnum(IREnum irEnum)
        {
            var enumName = SanitizeName(irEnum.Name);

            // Use C++11 enum class for type safety
            var underlyingType = "";
            if (irEnum.UnderlyingType != null && irEnum.UnderlyingType.Name != "Int32")
            {
                underlyingType = " : " + MapType(irEnum.UnderlyingType);
            }

            WriteLine($"enum class {enumName}{underlyingType}");
            WriteLine("{");
            Indent();

            for (int i = 0; i < irEnum.Members.Count; i++)
            {
                var member = irEnum.Members[i];
                var comma = i < irEnum.Members.Count - 1 ? "," : "";
                var value = member.Value != null ? $" = {member.Value}" : "";
                WriteLine($"{SanitizeName(member.Name)}{value}{comma}");
            }

            Unindent();
            WriteLine("};");
        }

        private void GenerateDelegate(IRDelegate irDelegate)
        {
            var delegateName = SanitizeName(irDelegate.Name);
            var returnType = MapType(irDelegate.ReturnType);
            var paramTypes = string.Join(", ", irDelegate.Parameters.Select(p => MapTypeName(p.TypeName)));

            // Use std::function for delegate types
            WriteLine($"using {delegateName} = std::function<{returnType}({paramTypes})>;");
        }

        /// <summary>
        /// "template &lt;typename T, typename U&gt;" prefix line, or null when not generic.
        /// </summary>
        private string TemplatePrefix(List<string> genericParams)
        {
            if (genericParams == null || genericParams.Count == 0)
                return null;
            return "template <" + string.Join(", ", genericParams.Select(p => $"typename {SanitizeName(p)}")) + ">";
        }

        /// <summary>
        /// C++ type mapping with template and reference-semantics support:
        /// - type parameters stay bare (T)
        /// - generic instantiations map recursively (Pair(Of Integer) -> Pair&lt;int32_t&gt;)
        /// - class/interface values are std::shared_ptr&lt;T&gt; (BasicLang objects are references)
        /// </summary>
        protected override string MapType(TypeInfo type)
        {
            if (type == null) return base.MapType(type);
            if (type.Kind == TypeKind.TypeParameter) return SanitizeName(type.Name);
            // VB arrays lower to std::vector<T>: an assignable/copyable value type (unlike a
            // C array, which cannot be assigned or returned). Route the element type through
            // the mapper so `Integer` becomes int32_t instead of leaking verbatim into C++.
            if (type.Kind == TypeKind.Array && type.ElementType != null)
                return $"std::vector<{MapType(type.ElementType)}>";
            if (type.Kind == TypeKind.Array || type.Kind == TypeKind.Pointer || type.IsPointer)
                return base.MapType(type);

            // Foreign C++ passthrough type (::-qualified) — emit verbatim, value semantics.
            // A ForeignSuffix (e.g. "::iterator") applies AFTER the <...> instantiation:
            // std::vector(Of Integer)::iterator -> std::vector<int32_t>::iterator.
            if (type.Name != null && type.Name.Contains("::"))
            {
                var suffix = type.ForeignSuffix ?? "";
                if (type.GenericArguments != null && type.GenericArguments.Count > 0)
                    return $"{type.Name}<{string.Join(", ", type.GenericArguments.Select(MapType))}>{suffix}";
                return type.Name + suffix;
            }
            // Everyday collections -> BasicLang wrappers wrapped in std::shared_ptr. .NET
            // List/Dictionary/HashSet are REFERENCE types: ByVal params alias, `Dim b = a`
            // aliases, field stores alias, and `[=]` lambda capture copies the (shared) pointer.
            // Value semantics (the previous behavior) diverged from .NET and the C# backend.
            // Match case-insensitively (VB-style) but always emit the canonical capitalized name.
            // (BareCollectionType produces the un-wrapped pointee for make_shared/decl of the class.)
            {
                var bareColl = BareCollectionType(type);
                if (bareColl != null)
                    return $"std::shared_ptr<{bareColl}>";
            }

            // IEnumerable(Of T) -> the coroutine generator (iterators are its only producer)
            if (type.Name == "IEnumerable" && type.GenericArguments != null && type.GenericArguments.Count > 0)
                return $"BasicLang::Generator<{MapType(type.GenericArguments[0])}>";

            // Task(Of T) -> synchronous BasicLang::Task<T> emulation
            if (type.Name == "Task")
            {
                if (type.GenericArguments != null && type.GenericArguments.Count > 0)
                    return $"BasicLang::Task<{MapType(type.GenericArguments[0])}>";
                return "BasicLang::Task<void>";
            }

            // .NET delegate types: Func(Of ..., TResult) / Action(Of ...) -> std::function
            if (type.Name == "Func" && type.GenericArguments != null && type.GenericArguments.Count > 0)
            {
                var funcRet = MapType(type.GenericArguments[type.GenericArguments.Count - 1]);
                var funcParams = string.Join(", ",
                    type.GenericArguments.Take(type.GenericArguments.Count - 1).Select(MapType));
                return $"std::function<{funcRet}({funcParams})>";
            }
            if (type.Name == "Action")
            {
                var actionParams = string.Join(", ",
                    (type.GenericArguments ?? new List<TypeInfo>()).Select(MapType));
                return $"std::function<void({actionParams})>";
            }

            // P1 native BCL types (spec §6.2). Keyed off the REGISTRY (not a name list),
            // and checked BEFORE the generator's own _typeMap. Five are value structs;
            // StringBuilder is the ONE reference type (shared_ptr, matching .NET
            // reference semantics).
            if (IsNativeOwnedBclType(type.Name))
            {
                return type.Name.Equals("StringBuilder", StringComparison.OrdinalIgnoreCase)
                    ? "std::shared_ptr<BasicLang::StringBuilder>"
                    : "BasicLang::" + BclCanonicalName(type.Name);
            }

            string bare;
            if (type.GenericArguments != null && type.GenericArguments.Count > 0)
            {
                var baseName = _typeMap.TryGetValue(type.Name, out var mapped) ? mapped : SanitizeName(type.Name);
                var args = string.Join(", ", type.GenericArguments.Select(MapType));
                bare = $"{baseName}<{args}>";
            }
            else if (type.Name != null && _typeMap.ContainsKey(type.Name))
            {
                // Primitive-mapped (String -> std::string, Object -> void*, ...): never wrapped
                return base.MapType(type);
            }
            else
            {
                bare = base.MapType(type);
            }

            if (type.Kind == TypeKind.Class || type.Kind == TypeKind.Interface)
                return $"std::shared_ptr<{bare}>";
            return bare;
        }

        /// <summary>
        /// The bare (un-shared_ptr-wrapped) C++ wrapper type for a BasicLang collection —
        /// <c>BasicLang::List&lt;T&gt;</c> / <c>BasicLang::Dictionary&lt;K,V&gt;</c> /
        /// <c>BasicLang::HashSet&lt;T&gt;</c> — or <c>null</c> when <paramref name="type"/> is
        /// not one of those collections. This is the pointee type: <see cref="MapType"/> wraps
        /// it in <c>std::shared_ptr&lt;…&gt;</c> (reference semantics), while
        /// <c>make_shared&lt;pointee&gt;</c> and forward-decls need it un-wrapped. Case-insensitive
        /// (VB-style); always emits the canonical capitalized name. Nested generic arguments
        /// recurse through <see cref="MapType"/> so e.g. a List(Of List(Of Integer)) element is a
        /// <c>std::shared_ptr&lt;BasicLang::List&lt;int32_t&gt;&gt;</c>.
        /// </summary>
        private string BareCollectionType(TypeInfo type)
        {
            if (type?.Name == null) return null;
            if (string.Equals(type.Name, "List", StringComparison.OrdinalIgnoreCase) && type.GenericArguments?.Count > 0)
                return $"BasicLang::List<{MapType(type.GenericArguments[0])}>";
            if (string.Equals(type.Name, "Dictionary", StringComparison.OrdinalIgnoreCase) && type.GenericArguments?.Count > 1)
                return $"BasicLang::Dictionary<{MapType(type.GenericArguments[0])}, {MapType(type.GenericArguments[1])}>";
            if (string.Equals(type.Name, "HashSet", StringComparison.OrdinalIgnoreCase) && type.GenericArguments?.Count > 0)
                return $"BasicLang::HashSet<{MapType(type.GenericArguments[0])}>";
            return null;
        }

        /// <summary>
        /// True when <paramref name="type"/> is a BasicLang collection (List/Dictionary/HashSet),
        /// which the C++ backend lowers as <c>std::shared_ptr</c> (reference semantics). Used to
        /// select <c>-&gt;</c> member access, pointer-deref indexing/iteration, and null defaults.
        /// Case-insensitive (VB-style).
        /// </summary>
        private static bool IsCollectionType(TypeInfo type)
        {
            var n = type?.Name;
            return n != null
                && (string.Equals(n, "List", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(n, "Dictionary", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(n, "HashSet", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// True when <paramref name="typeName"/> is a NativeOwned P1 BCL type per the
        /// boundary registry (spec §6.2) — DateTime, TimeSpan, Guid, StringBuilder,
        /// Decimal, DateTimeOffset. Keyed off the registry (never a local name list) so
        /// the registry stays the single source of truth for every branch below;
        /// CppCapabilityChecker gates the same set, member by member, against
        /// <see cref="NativeBclSurface"/>.
        /// </summary>
        private static bool IsNativeOwnedBclType(string typeName) =>
            typeName != null
            && BoundaryTypeRegistry.Categorize(typeName) == BoundaryTypeCategory.NativeOwned;

        /// <summary>
        /// True for the BL integral type names (the family the C++ backend lowers to
        /// intN_t/uintN_t). Used by the P1 Decimal cast lowering to pick the EXACT
        /// integer ctor over the lossy 15-digit double route — and by
        /// <see cref="CppCapabilityChecker"/>'s conversion gate, which must accept
        /// EXACTLY the set this lowering handles. Shared deliberately: two copies could
        /// drift into a gate that admits a conversion the generator cannot emit.
        /// </summary>
        internal static bool IsIntegralTypeName(string typeName) =>
            typeName != null && typeName.ToLowerInvariant() is
                "byte" or "ubyte" or "sbyte" or "short" or "ushort"
                or "integer" or "uinteger" or "long" or "ulong";

        /// <summary>
        /// Canonical C++ spelling of a P1 native BCL type name (BasicLang is
        /// case-insensitive, so `datetime` must emit as `DateTime`). Only called for
        /// names the registry categorizes NativeOwned; passthrough via SanitizeName is
        /// the defensive fallback for any future registry addition.
        /// </summary>
        private string BclCanonicalName(string typeName) => typeName.ToLowerInvariant() switch
        {
            "datetime" => "DateTime",
            "timespan" => "TimeSpan",
            "guid" => "Guid",
            "decimal" => "Decimal",
            "datetimeoffset" => "DateTimeOffset",
            "stringbuilder" => "StringBuilder",
            _ => SanitizeName(typeName)
        };

        /// <summary>
        /// References to __lambda_N functions render as inline C++ lambda expressions
        /// (mirrors the C# backend's inlining at use sites - CSharpBackend.cs).
        /// </summary>
        protected override string GetValueName(IRValue value)
        {
            if (value is IRVariable v && v.Name != null && v.Name.StartsWith("__lambda_"))
            {
                var lambdaFunc = _module?.Functions.FirstOrDefault(f => f.Name == v.Name && f.IsLambda);
                if (lambdaFunc != null)
                    return GenerateLambdaExpression(lambdaFunc);
            }

            // A foreign ::-qualified instance method call has no valid C++ temp type,
            // so anything consuming its value renders the call inline as an expression
            // (this is how m.foo().bar() and any foreign-result use stay opaque).
            if (value is IRInstanceMethodCall foreignCall && foreignCall.Type?.Kind == TypeKind.Foreign)
                return RenderForeignMethodCall(foreignCall);

            // A foreign ::-qualified data-member READ (c.field) is opaque the same way:
            // its result is a synthetic "T::field" pseudo-type, never a real C++ temp, so
            // it is rendered inline wherever consumed (Dim x = c.field -> auto x = c.field).
            if (value is IRFieldAccess foreignField && foreignField.Type?.Kind == TypeKind.Foreign)
                return RenderForeignFieldAccess(foreignField);

            // A foreign ::-qualified FREE-FUNCTION call (mathlib::freeAdd(3,4), ::globalFn(x))
            // is opaque like a foreign instance call: no valid C++ temp type, so it is rendered
            // inline (verbatim '::' name, bypassing SanitizeName) at each use site.
            if (value is IRCall foreignFreeCall && foreignFreeCall.Type?.Kind == TypeKind.Foreign)
                return RenderForeignFreeCall(foreignFreeCall);

            // A foreign ::-qualified GLOBAL/CONSTANT read is an IRVariable whose Foreign type
            // name is a verbatim C++ scope path (mathlib::kAnswer, ::kMax). Emit it verbatim —
            // SanitizeName would strip the '::' to "mathlibkAnswer". (A foreign LOCAL such as
            // `Dim m As std::mutex` has a plain name WITHOUT '::' and is unaffected by this.)
            if (value is IRVariable foreignGlobal && foreignGlobal.Type?.Kind == TypeKind.Foreign
                && foreignGlobal.Name != null && foreignGlobal.Name.Contains("::"))
                return foreignGlobal.Name;

            // The IRBuilder names result values after their assignment target (an IRAwait
            // named "x" for `Dim x = Await ...`, an IRBinaryOp named "total" for
            // `total = total + i`). The base implementation ignores .Name for
            // non-variables and invents a fresh temp, which both loses the assignment
            // and references an undeclared identifier. Honor the destination name.
            if (value != null && !(value is IRVariable) && !(value is IRConstant)
                && !string.IsNullOrEmpty(value.Name)
                && _declaredIdentifiers.Contains(value.Name)
                && !_valueNames.ContainsKey(value))
            {
                var name = SanitizeName(value.Name);
                _valueNames[value] = name;
                return name;
            }

            return base.GetValueName(value);
        }

        /// <summary>
        /// Render a lambda IRFunction as an inline C++ lambda: [=](params) -> ret { body }.
        /// The statement visitors write through _output, so the body is rendered into a
        /// temporary buffer while the surrounding function's emit state is saved/restored.
        /// </summary>
        private string GenerateLambdaExpression(IRFunction lambda)
        {
            var savedOutput = _output;
            var savedFunction = _currentFunction;
            var savedIndent = _indentLevel;
            var savedTempCounter = _tempCounter;
            var savedNames = new Dictionary<IRValue, string>(_valueNames);
            var savedDeclared = new HashSet<string>(_declaredIdentifiers, StringComparer.OrdinalIgnoreCase);
            var savedTemps = new HashSet<IRValue>(_allTemporaries);
            var savedCatchMessageAccesses = new HashSet<IRValue>(_catchMessageAccesses);
            var savedSuppress = _suppressLineDirectives;

            try
            {
                _output = new StringBuilder();
                _indentLevel = 0;
                // A #line directive inside an inlined lambda body would land mid-expression
                // and break the compile — suppress for the whole capture (nesting-safe).
                _suppressLineDirectives = true;

                var ps = string.Join(", ",
                    lambda.Parameters.Select(p => $"{MapType(p.Type)} {SanitizeName(p.Name)}"));
                var ret = MapType(lambda.ReturnType);
                var header = ret == "void" ? $"[=]({ps})" : $"[=]({ps}) -> {ret}";

                _output.Append(header);
                _output.Append(" {\n");
                _indentLevel = 1;

                _currentFunction = lambda;
                InitializeFunctionContext(lambda);
                DeclareLocalsAndTemporaries(lambda);
                GenerateFunctionBody(lambda);

                _output.Append('}');
                return _output.ToString();
            }
            finally
            {
                _output = savedOutput;
                _suppressLineDirectives = savedSuppress;
                _currentFunction = savedFunction;
                _indentLevel = savedIndent;
                _tempCounter = savedTempCounter;
                _valueNames.Clear();
                foreach (var kv in savedNames) _valueNames[kv.Key] = kv.Value;
                _declaredIdentifiers.Clear();
                foreach (var d in savedDeclared) _declaredIdentifiers.Add(d);
                _allTemporaries.Clear();
                foreach (var t in savedTemps) _allTemporaries.Add(t);
                _catchMessageAccesses.Clear();
                foreach (var t in savedCatchMessageAccesses) _catchMessageAccesses.Add(t);
            }
        }

        /// <summary>Member access operator for an object value: -> for shared_ptr objects and
        /// the raw `this` pointer, . for everything else (structures, std::string, ...).</summary>
        private string MemberAccessOp(IRValue obj)
        {
            if (obj is IRVariable v && v.Name == "this") return "->";

            var tn = obj?.Type?.Name;

            // Collections (BasicLang::List/Dictionary/HashSet) are .NET REFERENCE types, lowered
            // as std::shared_ptr — member access is `->` (e.g. numbers->Add(1)). Case-INSENSITIVE
            // (BasicLang is case-insensitive).
            if (IsCollectionType(obj?.Type))
                return "->";

            // A foreign, already-qualified C++ type (name contains "::") is a VALUE: member
            // access stays `.`, never `->`.
            if (tn != null && tn.Contains("::"))
                return ".";

            // P1 native BCL types: the five value structs use `.`; StringBuilder is the
            // one reference type (shared_ptr) → `->`.
            if (IsNativeOwnedBclType(tn))
                return tn.Equals("StringBuilder", StringComparison.OrdinalIgnoreCase) ? "->" : ".";

            var kind = obj?.Type?.Kind;
            if ((kind == TypeKind.Class || kind == TypeKind.Interface)
                && (obj.Type.Name == null || !_typeMap.ContainsKey(obj.Type.Name)))
                return "->";
            return ".";
        }

        private string MapTypeName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return "void*";
            return typeName.ToLowerInvariant() switch
            {
                "integer" => "int32_t",
                "long" => "int64_t",
                "single" => "float",
                "double" => "double",
                "string" => "std::string",
                "boolean" => "bool",
                "byte" => "uint8_t",
                "short" => "int16_t",
                "object" => "void*",
                "void" => "void",
                "sbyte" => "int8_t",
                // P1 native BCL types (spec §6.2). `datetime` said std::time_t before the
                // Task 10 flip — that shim (BasicLangRt::Now/FormatTime) is gone.
                "datetime" => "BasicLang::DateTime",
                "timespan" => "BasicLang::TimeSpan",
                "guid" => "BasicLang::Guid",
                "decimal" => "BasicLang::Decimal",
                "datetimeoffset" => "BasicLang::DateTimeOffset",
                "stringbuilder" => "std::shared_ptr<BasicLang::StringBuilder>",
                _ => SanitizeName(typeName)
            };
        }

        private void GenerateInterface(IRInterface irInterface)
        {
            var interfaceName = SanitizeName(irInterface.Name);

            // Interfaces become abstract classes in C++
            var baseList = "";
            if (irInterface.BaseInterfaces.Count > 0)
            {
                baseList = " : " + string.Join(", ", irInterface.BaseInterfaces.Select(b => $"public {SanitizeName(b)}"));
            }

            WriteLine($"class {interfaceName}{baseList}");
            WriteLine("{");
            WriteLine("public:");
            Indent();

            // Virtual destructor for proper cleanup
            WriteLine($"virtual ~{interfaceName}() = default;");
            WriteLine();

            // Generate pure virtual method declarations
            foreach (var method in irInterface.Methods)
            {
                var returnType = MapType(method.ReturnType);
                var methodName = SanitizeName(method.Name);
                // Prefer the fully-resolved Type (carries generic arguments, e.g.
                // std::shared_ptr<BasicLang::Dictionary<std::string, int32_t>>) when present;
                // fall back to the bare TypeName string.
                var paramList = string.Join(", ", method.Parameters.Select(p =>
                    $"{(p.Type != null ? MapType(p.Type) : MapTypeName(p.TypeName))} {SanitizeName(p.Name)}"));

                WriteLine($"virtual {returnType} {methodName}({paramList}) = 0;");
            }

            // Generate property getter/setter declarations
            foreach (var prop in irInterface.Properties)
            {
                var propType = MapType(prop.Type);
                var propName = SanitizeName(prop.Name);
                if (prop.HasGetter)
                    WriteLine($"virtual {propType} get_{propName}() = 0;");
                if (prop.HasSetter)
                    WriteLine($"virtual void set_{propName}({propType} value) = 0;");
            }

            Unindent();
            WriteLine("};");
        }

        private void GenerateStaticMemberInitializations(IRClass irClass)
        {
            EmitStaticMemberInitializationsCore(irClass, prefix: "");
        }

        /// <summary>
        /// Shared core for out-of-class static member definitions. Combined emission uses no
        /// prefix (single TU); split emission passes <c>"inline "</c> so the definitions are
        /// header-safe under multiple inclusion (C++17 inline variables, D3).
        /// </summary>
        private void EmitStaticMemberInitializationsCore(IRClass irClass, string prefix)
        {
            var className = SanitizeName(irClass.Name);

            foreach (var field in irClass.Fields.Where(f => f.IsStatic))
            {
                var type = MapType(field.Type);
                var name = SanitizeName(field.Name);
                var defaultValue = field.Initializer != null
                    ? (field.Initializer is IRConstant c ? EmitConstant(c) : GetValueName(field.Initializer))
                    : GetDefaultValue(field.Type);

                WriteLine($"{prefix}{type} {className}::{name} = {defaultValue};");
            }
        }

        private void GenerateClass(IRClass irClass)
        {
            // Member bodies must treat the class's fields as declared
            // identifiers: the IRBuilder names computed values after their
            // assignment target ("Y" for `Y = Y - Speed`), and without the
            // field names registered the destination silently decayed to a
            // temp — field mutations vanished from the generated code.
            var previousEmittingClass = _emittingClass;
            _emittingClass = irClass;
            try
            {
                GenerateClassCore(irClass);
            }
            finally
            {
                _emittingClass = previousEmittingClass;
            }
        }

        private void GenerateClassCore(IRClass irClass)
        {
            var className = SanitizeName(irClass.Name);

            // Build inheritance list
            var baseList = new List<string>();
            if (!string.IsNullOrEmpty(irClass.BaseClass))
            {
                baseList.Add($"public {SanitizeName(irClass.BaseClass)}");
            }
            foreach (var iface in irClass.Interfaces)
            {
                baseList.Add($"public {SanitizeName(iface)}");
            }

            var inheritance = baseList.Count > 0 ? " : " + string.Join(", ", baseList) : "";

            var classTemplate = TemplatePrefix(irClass.GenericParameters);
            if (classTemplate != null)
            {
                EmitConstraintsComment(irClass.GenericTypeParams);
                WriteLine(classTemplate);
            }
            WriteLine($"{(irClass.IsStruct ? "struct" : "class")} {className}{inheritance}");
            WriteLine("{");

            // Private members
            var privateFields = irClass.Fields.Where(f => f.Access == AccessModifier.Private).ToList();
            if (privateFields.Count > 0)
            {
                WriteLine("private:");
                Indent();
                foreach (var field in privateFields)
                {
                    var staticMod = field.IsStatic ? "static " : "";
                    var type = MapType(field.Type);
                    var name = SanitizeName(field.Name);
                    WriteLine($"{staticMod}{type} {name};");
                }
                Unindent();
                WriteLine();
            }

            // Protected members
            var protectedFields = irClass.Fields.Where(f => f.Access == AccessModifier.Protected).ToList();
            if (protectedFields.Count > 0)
            {
                WriteLine("protected:");
                Indent();
                foreach (var field in protectedFields)
                {
                    var staticMod = field.IsStatic ? "static " : "";
                    var type = MapType(field.Type);
                    var name = SanitizeName(field.Name);
                    WriteLine($"{staticMod}{type} {name};");
                }
                Unindent();
                WriteLine();
            }

            // Public members
            WriteLine("public:");
            Indent();

            // Public fields
            var publicFields = irClass.Fields.Where(f => f.Access == AccessModifier.Public).ToList();
            foreach (var field in publicFields)
            {
                var staticMod = field.IsStatic ? "static " : "";
                var type = MapType(field.Type);
                var name = SanitizeName(field.Name);
                WriteLine($"{staticMod}{type} {name};");
            }

            if (publicFields.Count > 0)
                WriteLine();

            // Constructors
            foreach (var ctor in irClass.Constructors)
            {
                GenerateConstructor(irClass, ctor);
            }

            // Destructor - virtual if has base class, interfaces, or virtual methods
            bool needsVirtualDestructor = !string.IsNullOrEmpty(irClass.BaseClass) ||
                                         irClass.Interfaces.Count > 0 ||
                                         irClass.Methods.Any(m => m.IsVirtual || m.IsOverride);

            if (needsVirtualDestructor)
            {
                WriteLine($"virtual ~{className}() {{}}");
                WriteLine();
            }
            else
            {
                WriteLine($"~{className}() = default;");
                WriteLine();
            }

            // Value-type structs get C++20 defaulted equality so field-wise value equality
            // matches .NET ValueType.Equals. Without it, List(Of SomeStruct).Contains/IndexOf/
            // Remove (which call std::find, needing `item == *it`) fail to compile (C2676).
            // Reference-type classes are lowered to std::shared_ptr, whose == is pointer
            // identity — matching .NET reference equality — so they are intentionally excluded.
            if (irClass.IsStruct)
            {
                WriteLine($"bool operator==(const {className}& other) const = default;");
                WriteLine();
            }

            // Properties (as getter/setter methods)
            foreach (var prop in irClass.Properties)
            {
                GenerateProperty(irClass, prop);
            }

            // Generate simple inline getters/setters for private fields with public access pattern
            GenerateSimplePropertyAccessors(irClass);

            // Events
            foreach (var evt in irClass.Events)
            {
                GenerateEvent(evt);
            }

            // Methods
            foreach (var method in irClass.Methods)
            {
                GenerateMethod(irClass, method);
            }

            Unindent();
            WriteLine("};");
        }

        private void GenerateConstructor(IRClass irClass, IRConstructor ctor)
        {
            var className = SanitizeName(irClass.Name);

            // Generate parameter list from implementation with const references for complex types
            var paramList = "";
            if (ctor.Implementation != null)
            {
                paramList = string.Join(", ", ctor.Implementation.Parameters.Select(p =>
                {
                    var paramType = MapType(p.Type);
                    // Use const reference for string and complex types
                    if (paramType == "std::string" || (p.Type != null && p.Type.Kind == TypeKind.Class))
                        return $"const {paramType}& {SanitizeName(p.Name)}";
                    return $"{paramType} {SanitizeName(p.Name)}";
                }));
            }

            // Build comprehensive initializer list
            var initItems = new List<string>();

            // Base constructor call first
            if (!string.IsNullOrEmpty(irClass.BaseClass) && ctor.BaseConstructorArgs.Count > 0)
            {
                var baseArgs = string.Join(", ", ctor.BaseConstructorArgs.Select(a =>
                    a is IRConstant c ? EmitConstant(c) : SanitizeName(a.Name)));
                initItems.Add($"{SanitizeName(irClass.BaseClass)}({baseArgs})");
            }
            else if (!string.IsNullOrEmpty(irClass.BaseClass))
            {
                // Default base constructor call
                initItems.Add($"{SanitizeName(irClass.BaseClass)}()");
            }

            // Field initializations - match constructor parameters to fields
            if (ctor.Implementation != null)
            {
                foreach (var param in ctor.Implementation.Parameters)
                {
                    // Find matching field (by name or by backing field pattern)
                    var field = irClass.Fields.FirstOrDefault(f =>
                        f.Name.Equals(param.Name, StringComparison.OrdinalIgnoreCase) ||
                        f.Name.Equals("_" + param.Name, StringComparison.OrdinalIgnoreCase));

                    if (field != null)
                    {
                        var fieldName = SanitizeName(field.Name);
                        var paramName = SanitizeName(param.Name);
                        initItems.Add($"{fieldName}({paramName})");
                    }
                }
            }

            var initList = initItems.Count > 0 ? " : " + string.Join(", ", initItems) : "";

            WriteLine($"{className}({paramList}){initList}");
            WriteLine("{");
            Indent();

            // Generate body from implementation
            if (ctor.Implementation != null)
            {
                _currentFunction = ctor.Implementation;
                _lastEmittedSourceLine = -1;
                _lastEmittedSourceFile = null;
                InitializeFunctionContext(ctor.Implementation);
                DeclareLocalsAndTemporaries(ctor.Implementation);
                GenerateFunctionBody(ctor.Implementation);

                _currentFunction = null;
            }

            Unindent();
            WriteLine("}");
            WriteLine();
        }

        private void GenerateSimplePropertyAccessors(IRClass irClass)
        {
            // Find private fields that could have simple getters/setters
            // Pattern: private field _name could have getName/setName methods
            var privateFields = irClass.Fields.Where(f =>
                f.Access == AccessModifier.Private &&
                !f.IsStatic &&
                f.Name.StartsWith("_")).ToList();

            // Only generate if they don't already have complex property implementations
            foreach (var field in privateFields)
            {
                var fieldName = SanitizeName(field.Name);
                var propName = fieldName.TrimStart('_');

                // Capitalize first letter for property name
                if (propName.Length > 0)
                    propName = char.ToUpper(propName[0]) + propName.Substring(1);

                // Check if there's already a property with this name
                var hasProperty = irClass.Properties.Any(p =>
                    p.Name.Equals(propName, StringComparison.OrdinalIgnoreCase));

                if (hasProperty)
                    continue; // Skip if already has explicit property

                var type = MapType(field.Type);
                var returnType = type;
                var paramType = type;

                // Use const reference for complex types
                if (type == "std::string" || (field.Type != null && field.Type.Kind == TypeKind.Class))
                {
                    returnType = $"const {type}&";
                    paramType = $"const {type}&";
                }

                // Generate simple inline getter
                WriteLine($"// Auto-generated getter for {fieldName}");
                WriteLine($"{returnType} get{propName}() const {{ return {fieldName}; }}");
                WriteLine();

                // Generate simple inline setter
                WriteLine($"// Auto-generated setter for {fieldName}");
                WriteLine($"void set{propName}({paramType} value) {{ {fieldName} = value; }}");
                WriteLine();
            }
        }

        private void GenerateProperty(IRClass irClass, IRProperty prop)
        {
            var propType = MapType(prop.Type);
            var propName = SanitizeName(prop.Name);
            var staticMod = prop.IsStatic ? "static " : "";

            // Determine if getter should be const (non-static, read-only access)
            var constQualifier = (!prop.IsStatic) ? " const" : "";

            // Use const reference for return type if it's a string or class type
            var returnType = propType;
            if (propType == "std::string" || (prop.Type != null && prop.Type.Kind == TypeKind.Class))
                returnType = $"const {propType}&";

            // Getter - returns const reference for complex types, const method for non-static
            if (prop.Getter != null && !prop.IsWriteOnly)
            {
                WriteLine($"{staticMod}{returnType} get_{propName}(){constQualifier}");
                WriteLine("{");
                Indent();

                _currentFunction = prop.Getter;
                _lastEmittedSourceLine = -1;
                _lastEmittedSourceFile = null;
                InitializeFunctionContext(prop.Getter);
                DeclareLocalsAndTemporaries(prop.Getter);
                GenerateFunctionBody(prop.Getter);

                _currentFunction = null;
                Unindent();
                WriteLine("}");
                WriteLine();
            }

            // Setter - takes const reference for complex types
            if (prop.Setter != null && !prop.IsReadOnly)
            {
                var paramType = propType;
                if (propType == "std::string" || (prop.Type != null && prop.Type.Kind == TypeKind.Class))
                    paramType = $"const {propType}&";

                WriteLine($"{staticMod}void set_{propName}({paramType} value)");
                WriteLine("{");
                Indent();

                _currentFunction = prop.Setter;
                _lastEmittedSourceLine = -1;
                _lastEmittedSourceFile = null;
                InitializeFunctionContext(prop.Setter);
                DeclareLocalsAndTemporaries(prop.Setter);
                GenerateFunctionBody(prop.Setter);

                _currentFunction = null;
                Unindent();
                WriteLine("}");
                WriteLine();
            }
        }

        private void GenerateEvent(IREvent evt)
        {
            var delegateType = SanitizeName(evt.DelegateType);
            var eventName = SanitizeName(evt.Name);
            var staticMod = evt.IsStatic ? "static " : "";

            // Events in C++ are typically implemented as vectors of callbacks
            WriteLine($"{staticMod}std::vector<{delegateType}> {eventName}_handlers;");
            WriteLine();

            // Add handler method
            WriteLine($"{staticMod}void add_{eventName}({delegateType} handler)");
            WriteLine("{");
            Indent();
            WriteLine($"{eventName}_handlers.push_back(handler);");
            Unindent();
            WriteLine("}");
            WriteLine();

            // Remove handler method (simplified - removes last matching)
            WriteLine($"{staticMod}void remove_{eventName}({delegateType} handler)");
            WriteLine("{");
            Indent();
            WriteLine($"// Note: Simplified removal - C++ function comparison is complex");
            WriteLine($"if (!{eventName}_handlers.empty()) {eventName}_handlers.pop_back();");
            Unindent();
            WriteLine("}");
            WriteLine();

            // Raise event method
            var paramList = "";
            // For simplicity, events with no params
            WriteLine($"{staticMod}void raise_{eventName}()");
            WriteLine("{");
            Indent();
            WriteLine($"for (auto& handler : {eventName}_handlers) {{ handler(); }}");
            Unindent();
            WriteLine("}");
            WriteLine();
        }

        private void GenerateMethod(IRClass irClass, IRMethod method)
        {
            var returnType = MapType(method.ReturnType);
            var methodName = SanitizeName(method.Name);
            var staticMod = method.IsStatic ? "static " : "";
            var virtualMod = method.IsVirtual && !method.IsOverride ? "virtual " : "";
            var overrideMod = method.IsOverride ? " override" : "";

            // Generate parameter list
            var paramList = "";
            if (method.Implementation != null)
            {
                paramList = string.Join(", ", method.Implementation.Parameters.Select(p =>
                    FormatParameter(p.Type, SanitizeName(p.Name))));
            }

            var methodTemplate = TemplatePrefix(method.GenericParameters);
            if (methodTemplate != null) WriteLine(methodTemplate);
            WriteLine($"{virtualMod}{staticMod}{returnType} {methodName}({paramList}){overrideMod}");
            WriteLine("{");
            Indent();

            // Generate body
            if (method.Implementation != null)
            {
                _currentFunction = method.Implementation;
                _lastEmittedSourceLine = -1;
                _lastEmittedSourceFile = null;
                InitializeFunctionContext(method.Implementation);
                DeclareLocalsAndTemporaries(method.Implementation);
                GenerateFunctionBody(method.Implementation);

                _currentFunction = null;
            }

            Unindent();
            WriteLine("}");
            WriteLine();
        }

        private void InitializeFunctionContext(IRFunction function)
        {
            _valueNames.Clear();
            _declaredIdentifiers.Clear();
            _allTemporaries.Clear();
            _tempCounter = 0;

            // Track declared identifiers
            foreach (var param in function.Parameters)
                _declaredIdentifiers.Add(param.Name);

            foreach (var local in function.LocalVariables)
                _declaredIdentifiers.Add(local.Name);

            if (_module != null)
            {
                foreach (var g in _module.GlobalVariables.Values)
                    _declaredIdentifiers.Add(g.Name);
            }

            // Class fields are valid assignment destinations inside member
            // bodies (`Y = Y - Speed` binds the computed value to the field);
            // without them registered the destination decays to a temp and the
            // mutation is lost.
            if (_emittingClass != null)
            {
                foreach (var field in _emittingClass.Fields)
                    _declaredIdentifiers.Add(field.Name);
            }

            // Foreign ::-qualified locals that are written by an assignment/store in the
            // body carry an initializer: suppress their standalone declaration and emit the
            // type at the first write (some::type it = expr;). Foreign types may be
            // non-default-constructible / non-assignable, so declare-then-assign can fail.
            //
            // BUT only defer when the FIRST write is in the ENTRY block. The entry block
            // dominates all uses and precedes every label, so a deferred `type n = expr;`
            // there is always executed before any read. If the first write is inside a
            // branch/loop body, its label can be jumped over by a `goto` — deferring the
            // declaration to it would make the C++ init "skipped by goto" (MSVC C2362).
            // Those locals fall back to the plain top-level `type name;` declaration.
            _foreignLocalsInitInline.Clear();
            _foreignLocalTypeByName.Clear();
            var foreignLocalNames = new HashSet<string>(
                function.LocalVariables.Where(l => l.Type?.Kind == TypeKind.Foreign)
                                       .Select(l => l.Name),
                StringComparer.OrdinalIgnoreCase);
            // A TYPE-INFERRED foreign local (`Dim x = w.compute()`) has a synthetic member-path
            // pseudo-type (e.g. "probe::Widget::compute") that is NOT a real C++ type — it must be
            // declared `auto` at its (entry-block) first write. An EXPLICIT foreign type
            // (`Dim it As std::...::iterator`) is a real type and keeps its verbatim spelling.
            // (Inferred foreign locals inside control-flow blocks are rejected at semantic
            // analysis, so only the entry-block `auto x = expr;` form reaches codegen.)
            foreach (var local in function.LocalVariables.Where(l => l.Type?.Kind == TypeKind.Foreign))
                _foreignLocalTypeByName[local.Name] =
                    local.IsInferredType ? "auto" : MapType(local.Type);
            // Foreign locals whose first write we've already seen (so a later write in a
            // different block can't retroactively flip the deferral decision).
            var foreignLocalFirstWriteSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var entryBlock = function.EntryBlock;

            // Collect temporaries (values that aren't named destinations)
            _inlinedForeignCalls.Clear();
            foreach (var block in function.Blocks)
            {
                foreach (var instruction in block.Instructions)
                {
                    // A write to a foreign local is its initializer — declare-at-first-write,
                    // but ONLY when that first write is in the entry block. A deferred
                    // `type n = expr;` inside a branch/loop body could be jumped over by a goto
                    // (MSVC C2362 "initialization skipped by goto"), so a branch-first-write
                    // foreign local falls back to a plain top-level declaration. This holds for
                    // BOTH explicit foreign types (`type name;` at top) and inferred ones — a
                    // type-inferred foreign local (`auto`) has no legal standalone declaration,
                    // so the semantic analyzer rejects one declared inside a control-flow block
                    // (see SemanticAnalyzer.Visit(VariableDeclarationNode)); only the entry-block
                    // form ever reaches here.
                    var writtenLocal = AssignmentTargetName(instruction);
                    if (writtenLocal != null && foreignLocalNames.Contains(writtenLocal)
                        && foreignLocalFirstWriteSeen.Add(writtenLocal)
                        && block == entryBlock)
                        _foreignLocalsInitInline.Add(writtenLocal);

                    // Any operand that is a foreign ::-qualified instance method call is
                    // consumed inline at that use site (as a member-access receiver, a call
                    // argument, a store value, a condition, ...). GetValueName renders it
                    // inline; recording it here suppresses the duplicate standalone
                    // statement, so the opaque call runs EXACTLY ONCE — never as both an
                    // inline expression AND a discarded `expr;` statement.
                    foreach (var operand in IROperandWalker.EnumerateOperands(instruction))
                    {
                        if (operand is IRInstanceMethodCall foreignCall
                            && foreignCall.Type?.Kind == TypeKind.Foreign)
                            _inlinedForeignCalls.Add(foreignCall);
                        // A ::-qualified foreign FREE-FUNCTION call consumed as an operand is
                        // likewise rendered inline (GetValueName -> RenderForeignFreeCall); record
                        // it so Visit(IRCall) suppresses the duplicate standalone statement.
                        else if (operand is IRCall foreignFreeCall
                            && foreignFreeCall.Type?.Kind == TypeKind.Foreign)
                            _inlinedForeignCalls.Add(foreignFreeCall);
                    }

                    if (instruction is IRValue value && !(value is IRConstant))
                    {
                        if (!IsNamedDestination(value))
                        {
                            _allTemporaries.Add(value);
                        }
                    }
                }
            }

            // §11.1: collect `<catchVar>.Message` reads, scoped to each catch clause's
            // OWN body region and its OWN variable name (an unnamed clause has no
            // referenceable exception variable and contributes nothing). The set backs
            // both the std::string temp declaration and the what() lowering.
            _catchMessageAccesses.Clear();
            foreach (var block in function.Blocks)
                foreach (var instruction in block.Instructions)
                {
                    if (instruction is not IRTryCatch tc) continue;
                    foreach (var cc in tc.CatchClauses)
                    {
                        if (string.IsNullOrEmpty(cc.VariableName) || cc.Block == null) continue;
                        foreach (var regionBlock in ComputeInlineRegion(cc.Block, tc.EndBlock))
                            foreach (var inst in regionBlock.Instructions)
                                if (inst is IRFieldAccess fa
                                    && string.Equals(fa.FieldName, "Message", StringComparison.OrdinalIgnoreCase)
                                    && fa.Object is IRVariable fv
                                    && string.Equals(fv.Name, cc.VariableName, StringComparison.OrdinalIgnoreCase))
                                    _catchMessageAccesses.Add(fa);
                    }
                }
        }

        /// <summary>
        /// The name of the local an instruction WRITES to (an IRAssignment target or an
        /// IRStore into an IRVariable address), or null. Used to detect a foreign local's
        /// initializer so its declaration can be folded into the first write.
        /// </summary>
        private static string AssignmentTargetName(IRInstruction instruction) => instruction switch
        {
            IRAssignment asn when asn.Target is IRVariable tv => tv.Name,
            IRStore st when st.Address is IRVariable sv => sv.Name,
            _ => null
        };

        private void DeclareLocalsAndTemporaries(IRFunction function)
        {
            // Declare local variables
            foreach (var local in function.LocalVariables)
            {
                var localType = MapType(local.Type);
                var localName = SanitizeName(local.Name);
                if (localType != "void")
                {
                    // Foreign ::-qualified types (e.g. std::mutex) are opaque: default-construct
                    // with a plain declaration. A copy-list-init `= {}` would break non-copyable
                    // std types, and we can't know a foreign type's initializer shape anyway.
                    if (local.Type?.Kind == TypeKind.Foreign)
                    {
                        // A foreign local with an initializer is declared at its first write
                        // (some::type it = expr;) — skip the standalone declaration here.
                        if (_foreignLocalsInitInline.Contains(local.Name))
                            continue;
                        // A type-INFERRED foreign local's type is a synthetic member-path
                        // pseudo-type (not a real C++ type) — a standalone `pseudo-type x;`
                        // would not compile, and `auto x;` is illegal. It is always declared
                        // at its first write (auto x = expr;); never emit a bare declaration.
                        if (local.IsInferredType)
                            continue;
                        WriteLine($"{localType} {localName};");
                    }
                    else
                    {
                        var defaultVal = GetDefaultValue(local.Type);
                        WriteLine($"{localType} {localName} = {defaultVal};");
                    }
                }
            }

            // Declare temporaries with proper typing (skip void types; exception objects
            // are consumed at the throw site and never materialize as C++ values).
            // Foreign ::-qualified result temps are skipped too: a foreign member call's
            // pseudo-type (e.g. "std::mutex::lock") is not a real C++ type, so it can't be
            // declared — Visit(IRInstanceMethodCall) emits those calls as bare statements.
            var tempsByType = _allTemporaries
                .Where(t => t.Type?.Name != "Void" && MapType(t.Type) != "void")
                .Where(t => t.Type?.Kind != TypeKind.Foreign)
                .Where(t => !CppExceptionTypes.IsNetException(t.Type?.Name))
                // §11.1: a `<catchVar>.Message` read lowers to
                // BasicLang::String(v.what()) — its temp must be std::string
                // regardless of what the analyzer inferred (often Object -> void*).
                .GroupBy(t => _catchMessageAccesses.Contains(t) ? "std::string" : MapType(t.Type))
                .ToList();

            foreach (var group in tempsByType)
            {
                var cppType = group.Key;
                var tempNames = group.Select(GetValueName).Distinct().ToList();

                foreach (var tempName in tempNames)
                {
                    WriteLine($"{cppType} {tempName} = {{}};");
                }
            }

            if (function.LocalVariables.Count > 0 || _allTemporaries.Count > 0)
                WriteLine();
        }

        private string MapAccessModifier(AccessModifier access)
        {
            return access switch
            {
                AccessModifier.Public => "public",
                AccessModifier.Private => "private",
                AccessModifier.Protected => "protected",
                AccessModifier.Friend => "public",  // C++ doesn't have internal
                _ => "private"
            };
        }

        private void GenerateFunctionDeclaration(IRFunction function)
        {
            var returnType = MapReturnType(function);
            var functionName = SanitizeName(function.Name);

            var parameters = string.Join(", ",
                function.Parameters.Select(p => FormatParameter(p.Type, GetValueName(p))));

            var fnTemplate = TemplatePrefix(function.GenericParameters);
            if (fnTemplate != null) WriteLine(fnTemplate);
            WriteLine($"{returnType} {functionName}({parameters});");
        }

        /// <summary>
        /// Format one parameter: type parameters pass by const-ref (safe for any T),
        /// everything else keeps its existing by-value form.
        /// </summary>
        private string FormatParameter(TypeInfo type, string name)
        {
            var paramType = MapType(type);
            if (type != null && type.Kind == TypeKind.TypeParameter)
                return $"const {paramType}& {name}";
            return $"{paramType} {name}";
        }

        /// <summary>Constraints are dropped for C++ (parity with C# backend today); leave a trace.</summary>
        private void EmitConstraintsComment(List<GenericTypeParameter> typeParams)
        {
            if (typeParams == null) return;
            foreach (var tp in typeParams)
            {
                if (tp.Constraints != null && tp.Constraints.Count > 0)
                    WriteLine($"// constraints dropped: {tp.Name} As {string.Join(", ", tp.Constraints)}");
            }
        }
        
        private void GenerateFunction(IRFunction function)
        {
            _currentFunction = function;
            // Fresh dedupe per function (mirror CSharpBackend): the first statement must
            // always get a directive even if the previous function ended on the same line.
            _lastEmittedSourceLine = -1;
            _lastEmittedSourceFile = null;
            InitializeFunctionContext(function);

            // Generate signature
            var returnType = MapReturnType(function);
            var functionName = SanitizeName(function.Name);
            var parameters = string.Join(", ",
                function.Parameters.Select(p => FormatParameter(p.Type, GetValueName(p))));

            var fnTemplate = TemplatePrefix(function.GenericParameters);
            if (fnTemplate != null)
            {
                EmitConstraintsComment(function.GenericTypeParams);
                WriteLine(fnTemplate);
            }
            WriteLine($"{returnType} {functionName}({parameters})");
            WriteLine("{");
            Indent();

            DeclareLocalsAndTemporaries(function);

            // Generate body
            if (function.EntryBlock != null)
            {
                GenerateFunctionBody(function);
            }

            Unindent();
            WriteLine("}");
        }
        
        /// <summary>C++ labels cannot contain dots (block names like "if0.then" do).</summary>
        private string LabelName(string blockName) => blockName.Replace('.', '_') + _regionLabelSuffix;

        /// <summary>
        /// Appended to every emitted label and goto target while set. Used ONLY to keep the two
        /// copies of a Finally body (the catch(...) rethrow path and the normal path) from
        /// defining the same C++ label twice when the Finally contains control flow — each copy
        /// gets a distinct suffix so labels/gotos stay internally consistent yet globally unique.
        /// Empty everywhere else.
        /// </summary>
        private string _regionLabelSuffix = "";

        /// <summary>
        /// Emit a function body: the entry block, then every remaining block in creation
        /// order. BasicBlock.Successors is only populated by the ControlFlowGraph analysis
        /// pass (which codegen does not run), so successor-walking alone would silently
        /// drop every non-entry block (any If branch, loop continuation, post-Try code).
        /// Blocks consumed inline by structured statements (try/catch/finally bodies,
        /// foreach bodies) are excluded.
        /// </summary>
        private void GenerateFunctionBody(IRFunction function)
        {
            var visited = new HashSet<BasicBlock>();
            var consumed = CollectInlineEmittedBlocks(function);
            if (function.EntryBlock != null)
                GenerateBlock(function.EntryBlock, visited, consumed);
            foreach (var block in function.Blocks)
            {
                if (block == null || visited.Contains(block) || consumed.Contains(block)) continue;
                GenerateBlock(block, visited, consumed);
            }
        }

        private HashSet<BasicBlock> CollectInlineEmittedBlocks(IRFunction function)
        {
            // Structured constructs (For Each, Try/Catch) emit their body regions INLINE inside
            // the native C++ construct (range-for / try{}/catch{}). Everything in those regions —
            // not just the single entry block, but every block the body reaches (the `if0.then`/
            // `if0.end` split blocks of a nested If, a nested loop's blocks, ...) up to the
            // construct's EndBlock — is emitted there. The top-level block walker must therefore
            // treat the WHOLE region as consumed; otherwise those inner blocks land at function
            // scope AFTER the `return` as unreachable dead code (the original bug: control flow
            // inside a For Each/Try body was silently orphaned and never ran).
            var consumed = new HashSet<BasicBlock>();
            foreach (var block in function.Blocks)
            {
                foreach (var inst in block.Instructions)
                {
                    switch (inst)
                    {
                        case IRTryCatch tc:
                            AddRegion(consumed, ComputeInlineRegion(tc.TryBlock, tc.EndBlock));
                            foreach (var cc in tc.CatchClauses)
                                AddRegion(consumed, ComputeInlineRegion(cc.Block, tc.EndBlock));
                            AddRegion(consumed, ComputeInlineRegion(tc.FinallyBlock, tc.EndBlock));
                            break;
                        case IRForEach fe:
                            AddRegion(consumed, ComputeInlineRegion(fe.BodyBlock, fe.EndBlock));
                            break;
                    }
                }
            }
            return consumed;
        }

        private static void AddRegion(HashSet<BasicBlock> target, IReadOnlyList<BasicBlock> region)
        {
            foreach (var b in region) target.Add(b);
        }

        /// <summary>
        /// All blocks belonging to a structured construct's inline body region: every block
        /// reachable from <paramref name="entry"/> by control flow (branch targets AND nested
        /// structured-construct body/continuation edges), stopping at — and never including —
        /// the construct's <paramref name="boundary"/> (its EndBlock). The boundary block is the
        /// loop/try continuation; it holds code that runs AFTER the construct and is emitted at
        /// the enclosing scope, so it must not be pulled into the region. Blocks are returned in
        /// the function's creation order for stable, readable output. Reachability is computed
        /// from the IR's own branch/structured edges (not <c>BasicBlock.Successors</c>, which is
        /// only populated when a CFG pass has run), so it works on the un-optimized path too.
        /// </summary>
        private IReadOnlyList<BasicBlock> ComputeInlineRegion(BasicBlock entry, BasicBlock boundary)
        {
            var region = new HashSet<BasicBlock>();
            if (entry == null || entry == boundary) return new List<BasicBlock>();

            var stack = new Stack<BasicBlock>();
            stack.Push(entry);
            while (stack.Count > 0)
            {
                var block = stack.Pop();
                if (block == null || block == boundary || !region.Add(block)) continue;
                foreach (var succ in ControlFlowTargets(block))
                    if (succ != null && succ != boundary && !region.Contains(succ))
                        stack.Push(succ);
            }

            // Emit in creation order (stable); the goto/label model makes order non-semantic.
            return _currentFunction.Blocks.Where(region.Contains).ToList();
        }

        /// <summary>
        /// The control-flow successor blocks of <paramref name="block"/> as the IR defines them:
        /// branch/conditional-branch targets, plus the body/continuation blocks carried by
        /// nested structured constructs (For Each -&gt; Body/End, Try -&gt; Try/Catch/Finally/End).
        /// Mirrors the edge set <see cref="ControlFlowGraph"/> builds, but read straight off the
        /// instructions so it does not depend on a CFG pass having run.
        /// </summary>
        private static IEnumerable<BasicBlock> ControlFlowTargets(BasicBlock block)
        {
            foreach (var inst in block.Instructions)
            {
                switch (inst)
                {
                    case IRBranch br:
                        if (br.Target != null) yield return br.Target;
                        break;
                    case IRConditionalBranch cb:
                        if (cb.TrueTarget != null) yield return cb.TrueTarget;
                        if (cb.FalseTarget != null) yield return cb.FalseTarget;
                        break;
                    case IRSwitch sw:
                        if (sw.DefaultTarget != null) yield return sw.DefaultTarget;
                        foreach (var (_, target) in sw.Cases)
                            if (target != null) yield return target;
                        break;
                    case IRForEach fe:
                        if (fe.BodyBlock != null) yield return fe.BodyBlock;
                        if (fe.EndBlock != null) yield return fe.EndBlock;
                        break;
                    case IRTryCatch tc:
                        if (tc.TryBlock != null) yield return tc.TryBlock;
                        foreach (var cc in tc.CatchClauses)
                            if (cc.Block != null) yield return cc.Block;
                        if (tc.FinallyBlock != null) yield return tc.FinallyBlock;
                        if (tc.EndBlock != null) yield return tc.EndBlock;
                        break;
                }
            }
        }

        private void GenerateBlock(BasicBlock block, HashSet<BasicBlock> visited, HashSet<BasicBlock> consumed)
        {
            if (visited.Contains(block)) return;
            visited.Add(block);

            // Label (if needed); trailing ';' keeps a label at block end valid C++
            if (block.Predecessors.Count > 1 || block != _currentFunction.EntryBlock)
            {
                Unindent();
                WriteLine($"{LabelName(block.Name)}: ;");
                Indent();
            }

            // Instructions
            foreach (var instruction in block.Instructions)
            {
                if (instruction.SourceLine > 0)
                    EmitLineDirective(instruction.SourceLine, _currentFunction?.SourceFilePath);
                else
                    EmitLineReset();
                instruction.Accept(this);
            }

            // Process successors (populated only when a CFG pass has run — e.g. the optimizer's
            // DeadCodeEliminationPass — otherwise empty). Skip blocks CONSUMED by a structured
            // statement (a For Each body, or a try/catch/finally body): those are emitted inline
            // by Visit(IRForEach)/Visit(IRTryCatch), so following a CFG edge into them here would
            // emit them a SECOND time as a stray labeled block.
            foreach (var successor in block.Successors.Where(s => !visited.Contains(s) && !consumed.Contains(s)))
            {
                GenerateBlock(successor, visited, consumed);
            }
        }
        
        private void GenerateMainFunction(IRModule module)
        {
            WriteLine("int main(int argc, char* argv[])");
            WriteLine("{");
            Indent();
            
            var mainFunc = module.Functions.FirstOrDefault(f =>
                f.Name.Equals("Main", StringComparison.OrdinalIgnoreCase));
            
            if (mainFunc != null)
            {
                var functionName = SanitizeName(mainFunc.Name);
                if (mainFunc.ReturnType.Name == "Void")
                {
                    WriteLine($"{functionName}();");
                }
                else
                {
                    WriteLine($"auto result = {functionName}();");
                    WriteLine("cout << result << endl;");
                }
            }
            else
            {
                WriteLine("cout << \"No Main function found\" << endl;");
            }
            
            WriteLine("return 0;");
            Unindent();
            WriteLine("}");
        }
        
        protected override void InitializeTypeMap()
        {
            base.InitializeTypeMap();
            
            // C++ specific mappings
            _typeMap["Integer"] = "int32_t";
            _typeMap["Long"] = "int64_t";
            _typeMap["Single"] = "float";
            _typeMap["Double"] = "double";
            _typeMap["String"] = "std::string";
            _typeMap["Boolean"] = "bool";
            _typeMap["Char"] = "char";
            _typeMap["Void"] = "void";
            _typeMap["Object"] = "void*";
            _typeMap["Byte"] = "uint8_t";
            _typeMap["Short"] = "int16_t";
            _typeMap["SByte"] = "int8_t";
            _typeMap["UByte"] = "uint8_t";
            _typeMap["UShort"] = "uint16_t";
            _typeMap["UInteger"] = "uint32_t";
            _typeMap["ULong"] = "uint64_t";
            // No "Decimal" entry: P1 lowers Decimal as the native BasicLang::Decimal value
            // struct via the NativeOwned branch in MapType (the old `long double` mapping
            // was a lossy stand-in with no reachable green path — Decimal is registry-gated).
        }
        
        #region Visitor Methods
        
        public override void Visit(IRFunction function) { }
        public override void Visit(BasicBlock block) { }
        public override void Visit(IRConstant constant) { }
        public override void Visit(IRVariable variable) { }
        
        public override void Visit(IRBinaryOp binaryOp)
        {
            var left = GetValueName(binaryOp.Left);
            var right = GetValueName(binaryOp.Right);
            var op = MapBinaryOperator(binaryOp.Operation);
            var result = GetValueName(binaryOp);
            
            WriteLine($"{result} = {left} {op} {right};");
        }
        
        public override void Visit(IRUnaryOp unaryOp)
        {
            var operand = GetValueName(unaryOp.Operand);
            var op = MapUnaryOperator(unaryOp.Operation);
            var result = GetValueName(unaryOp);
            
            if (unaryOp.Operation == UnaryOpKind.Inc || unaryOp.Operation == UnaryOpKind.Dec)
            {
                WriteLine($"{result}{op};");
            }
            else
            {
                WriteLine($"{result} = {op}{operand};");
            }
        }
        
        public override void Visit(IRCompare compare)
        {
            var left = GetValueName(compare.Left);
            var right = GetValueName(compare.Right);
            var op = MapCompareOperator(compare.Comparison);
            var result = GetValueName(compare);
            
            WriteLine($"{result} = {left} {op} {right};");
        }
        
        public override void Visit(IRAssignment assignment)
        {
            var value = GetValueName(assignment.Value);
            var target = GetValueName(assignment.Target);

            WriteLine($"{ForeignInitDeclPrefix(assignment.Target?.Name)}{target} = {value};");
        }

        /// <summary>
        /// If <paramref name="name"/> is a foreign local whose declaration was deferred to
        /// its initializer, return its C++ type + space (so the write becomes a combined
        /// `some::type name = expr;`) and consume the pending flag so later writes are plain
        /// assignments. Otherwise return "".
        /// </summary>
        private string ForeignInitDeclPrefix(string name)
        {
            if (name != null && _foreignLocalsInitInline.Remove(name)
                && _foreignLocalTypeByName.TryGetValue(name, out var cppType))
                return cppType + " ";
            return "";
        }
        
        public override void Visit(IRLoad load)
        {
            var address = GetValueName(load.Address);
            var result = GetValueName(load);
            
            WriteLine($"{result} = *{address};");
        }
        
        public override void Visit(IRStore store)
        {
            var value = GetValueName(store.Value);

            // For IRVariable addresses (local variables), emit as simple assignment
            if (store.Address is IRVariable variable)
            {
                var varName = SanitizeName(variable.Name);
                WriteLine($"{ForeignInitDeclPrefix(variable.Name)}{varName} = {value};");
                return;
            }

            // An alloca address (<name>_addr) is the backing memory for a local — write
            // straight to that local (mirrors the C# backend). Emitted for array locals; without
            // this the store targets a fresh unnamed temp and the value never reaches the local.
            if (store.Address is IRAlloca alloca)
            {
                var allocaVar = alloca.Name != null
                    && alloca.Name.EndsWith("_addr", StringComparison.OrdinalIgnoreCase)
                        ? alloca.Name.Substring(0, alloca.Name.Length - "_addr".Length)
                        : alloca.Name;
                WriteLine($"{ForeignInitDeclPrefix(allocaVar)}{SanitizeName(allocaVar)} = {value};");
                return;
            }

            // Array element store
            if (store.Address is IRGetElementPtr gep)
            {
                var baseExpr = GetValueName(gep.BasePointer);
                var index = GetValueName(gep.Indices[0]);
                WriteLine($"{baseExpr}[{index}] = {value};");
                return;
            }

            var address = GetValueName(store.Address);

            // Check if this is a pointer store or a regular assignment
            if (store.Address.Type?.Kind == TypeKind.Pointer)
            {
                WriteLine($"*{address} = {value};");
            }
            else
            {
                // Regular variable assignment
                WriteLine($"{address} = {value};");
            }
        }
        
        public override void Visit(IRCall call)
        {
            // A ::-qualified foreign C++ free-function call (mathlib::freeAdd(3,4),
            // ::globalFn(x)) is opaque like a foreign instance call: its result type is a
            // synthetic Foreign pseudo-type with no valid C++ temp, so it is rendered inline
            // (verbatim, bypassing SanitizeName) at each use site by GetValueName. When the
            // result is CONSUMED (arg / initializer / condition) it is rendered there, so
            // suppress the duplicate standalone statement; emit `expr;` only when discarded.
            if (call.Type?.Kind == TypeKind.Foreign)
            {
                if (_inlinedForeignCalls.Contains(call))
                    return;
                WriteLine($"{RenderForeignFreeCall(call)};");
                return;
            }

            var args = call.Arguments.Select(GetValueName).ToList();
            var functionName = call.FunctionName;
            var hasReturn = call.Type != null && !call.Type.Name.Equals("Void", StringComparison.OrdinalIgnoreCase);

            // Invoking a VOID-returning delegate (an Action, or a user delegate / Func whose
            // return type is void) yields nothing — but the semantic analyzer types the
            // invocation result as Object, so hasReturn was wrongly true and we emitted
            // `void* t = adder();` (MSVC C2440: can't assign void to void*, and a redeclared
            // result temp). Treat it as no-return so it emits a bare `adder(args);`. This is a
            // pre-existing bug (reproduces WITHOUT collections) that the lambda+collection test
            // depends on. Foreign ::-qualified calls are handled elsewhere and excluded here.
            if (hasReturn && IsVoidDelegateInvocation(call))
                hasReturn = false;

            // Destination for the call result: a user-declared variable, else the
            // temp that later instructions reference this value by. Every emission
            // path below must honor it — the extern/stdlib paths used to drop temp
            // destinations, so `If IsKeyDown(87) Then` emitted the call as a bare
            // statement and branched on an uninitialized temp.
            string destination = null;
            if (hasReturn)
            {
                if (IsNamedDestination(call))
                    destination = SanitizeName(call.Name);
                else if (!string.IsNullOrEmpty(call.Name))
                    destination = GetValueName(call);
            }

            void EmitCallStatement(string expression)
            {
                WriteLine(destination != null ? $"{destination} = {expression};" : $"{expression};");
            }

            // Check if this is an extern function call
            if (_module != null && _module.IsExtern(functionName))
            {
                var externDecl = _module.GetExtern(functionName);
                if (externDecl != null && externDecl.HasImplementation("Cpp"))
                {
                    var impl = externDecl.GetImplementation("Cpp");
                    var argsArr = args.ToArray();

                    string externCall;
                    if (impl.Contains("{"))
                    {
                        externCall = string.Format(impl, argsArr);
                    }
                    else
                    {
                        externCall = $"{impl}({string.Join(", ", args)})";
                    }

                    EmitCallStatement(externCall);
                    return;
                }
            }

            // Handle standard library calls. Statement-like console calls are
            // emitted WITHOUT a destination even when the IR types them (the
            // analyzer gives Console.WriteLine an Object result, but assigning
            // a cout expression to a temp is not valid C++).
            var stdlibCall = EmitStdLibCall(functionName, args, call);
            if (stdlibCall != null)
            {
                if (IsVoidStdLibCall(functionName))
                    WriteLine($"{stdlibCall};");
                else
                    EmitCallStatement(stdlibCall);
                return;
            }

            // Regular function call
            var sanitizedName = SanitizeName(ResolveFlattenedFunctionName(functionName));
            var argsStr = string.Join(", ", args);
            EmitCallStatement($"{sanitizedName}({argsStr})");
        }

        /// <summary>
        /// True when <paramref name="call"/> invokes a VOID-returning delegate value (an
        /// <c>Action</c>, or a user delegate whose declared return type is void) rather than a
        /// named function or a value-returning delegate. Such an invocation yields nothing, but
        /// the semantic analyzer types its result as Object — so without this the backend emits
        /// `void* t = adder();` (invalid: assigning void to void*). Resolves the callee by name
        /// among the current function's locals/parameters and the module globals.
        ///
        /// Only VARIABLE-targeted invocations qualify: a normal call to a named void Sub already
        /// has Type.Name == "Void" and never reaches here. An <c>Action</c> is void by definition;
        /// a <c>Func</c> is value-returning (its last generic arg is the result). A user delegate's
        /// void-ness is read from its <see cref="IRDelegate.ReturnType"/>.
        /// </summary>
        private bool IsVoidDelegateInvocation(IRCall call)
        {
            // A foreign ::-qualified result is handled by the foreign-call path, not here.
            if (call.Type?.Kind == TypeKind.Foreign) return false;

            // Resolve the callee's declared type. For a delegate invocation the FunctionName is
            // the variable's name (locals/params first, then module globals). CalleeValue, when
            // present (e.g. f(a)(b)), carries the type directly.
            TypeInfo calleeType = call.CalleeValue?.Type;
            if (calleeType == null && !string.IsNullOrEmpty(call.FunctionName))
            {
                bool Match(IRVariable v) =>
                    string.Equals(v?.Name, call.FunctionName, StringComparison.OrdinalIgnoreCase);

                calleeType =
                    _currentFunction?.LocalVariables?.FirstOrDefault(Match)?.Type
                    ?? _currentFunction?.Parameters?.FirstOrDefault(Match)?.Type;

                if (calleeType == null && _module != null
                    && _module.GlobalVariables.TryGetValue(call.FunctionName, out var g))
                    calleeType = g.Type;
            }

            if (calleeType == null) return false;

            // Action(...) is void by definition.
            if (string.Equals(calleeType.Name, "Action", StringComparison.OrdinalIgnoreCase))
                return true;

            // A user delegate: read its declared return type from the module.
            if (calleeType.Kind == TypeKind.Delegate && calleeType.Name != null
                && _module != null && _module.Delegates.TryGetValue(calleeType.Name, out var del))
            {
                var rt = del.ReturnType?.Name;
                return rt == null || string.Equals(rt, "Void", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        /// <summary>
        /// The C++ backend flattens module procedures to free functions with
        /// UNQUALIFIED names, but cross-module call sites arrive with the
        /// qualified "Module.Function" name (which the C# backend needs for
        /// its static-class emission). Align the call with the flattened
        /// definition — otherwise every multi-file program fails to link
        /// (call `HelpersPrintHeader(...)` vs definition `PrintHeader(...)`).
        /// </summary>
        private string ResolveFlattenedFunctionName(string functionName)
        {
            if (string.IsNullOrEmpty(functionName))
                return functionName;

            var lastDot = functionName.LastIndexOf('.');
            if (lastDot < 0 || lastDot == functionName.Length - 1)
                return functionName;

            if (_module?.Functions == null)
                return functionName;

            // Only rewrite when the flattened name actually exists and the
            // qualified one does not — never break a genuinely qualified match.
            if (_module.Functions.Any(f => f.Name == functionName))
                return functionName;

            var plain = functionName.Substring(lastDot + 1);
            return _module.Functions.Any(f => f.Name == plain) ? plain : functionName;
        }

        private static string GetArg(List<string> args, int index) => index < args.Count ? args[index] : "0";

        /// <summary>
        /// Widen a Byte/SByte console argument for printing (spec §14.6). <c>cout</c>
        /// streams <c>int8_t</c>/<c>uint8_t</c> as a CHARACTER — <c>Console.WriteLine(b)</c>
        /// with b = 65 printed 'A' — while .NET (and the C# backend) print the NUMBER.
        /// Wrapping the arg in <c>static_cast&lt;int32_t&gt;</c> restores parity. Char is
        /// deliberately NOT wrapped: .NET prints a Char as its character too.
        /// </summary>
        private static string NumericPrintArg(string rendered, IRCall call, int index)
        {
            var typeName = call != null && index < call.Arguments.Count
                ? call.Arguments[index]?.Type?.Name
                : null;
            if (typeName == null) return rendered;
            return typeName.ToLowerInvariant() is "byte" or "sbyte" or "ubyte"
                ? $"static_cast<int32_t>({rendered})"
                : rendered;
        }

        /// <summary>
        /// Stdlib mappings that are statements, not value expressions — their
        /// rendered form (cout chains, srand) must never be assigned to the
        /// destination temp even when the IR types the call as value-returning.
        /// </summary>
        private static bool IsVoidStdLibCall(string functionName) =>
            functionName.ToLowerInvariant() is "print" or "printline" or "randomize"
                or "console.writeline" or "console.write";

        /// <summary>
        /// Re-render call arguments for a Framework_* extern "C" export:
        /// std::string values become const char* via .c_str(). String literals
        /// (IRConstant) already render as C string literals and pass through.
        /// </summary>
        private List<string> MarshalFrameworkArgs(List<string> rendered, IRCall call)
        {
            if (call == null) return rendered;

            var marshaled = new List<string>(rendered.Count);
            for (int i = 0; i < rendered.Count; i++)
            {
                var arg = rendered[i];
                if (i < call.Arguments.Count)
                {
                    var irArg = call.Arguments[i];
                    var isStringValue = irArg != null && !(irArg is IRConstant) &&
                        string.Equals(irArg.Type?.Name, "String", StringComparison.OrdinalIgnoreCase);
                    if (isStringValue)
                        arg = $"{arg}.c_str()";
                }
                marshaled.Add(arg);
            }
            return marshaled;
        }

        /// <summary>
        /// Names of Functions/Subs the user's program defines — both the qualified form the
        /// IR carries ("M.Run") and its unqualified last segment ("Run"), because a call site
        /// may use either. Rebuilt whenever <see cref="_module"/> changes rather than at each
        /// entry point, so <c>Generate</c> and <c>GenerateSplit</c> cannot drift apart (the
        /// classic both-modes trap).
        /// </summary>
        private readonly HashSet<string> _userFunctionNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private IRModule _userFunctionNamesSource;

        /// <summary>
        /// True when the user's program defines a Function/Sub called <paramref name="functionName"/>.
        /// Such a definition SHADOWS a stdlib builtin of the same name — the rule the C# backend
        /// has always enforced (<c>CSharpBackend.StdLibCanHandle</c>'s <c>_userFunctionNames</c>
        /// guard), which this backend was missing entirely: <c>EmitStdLibCall</c> is consulted
        /// before the user-function path, so a user's <c>Function Year(d As DateTime)</c> was
        /// EMITTED but never CALLED (silently answering with <c>d.Year()</c> instead), and a
        /// user's <c>Function Day(n As Integer)</c> lowered to the uncompilable <c>(21).Day()</c>.
        /// </summary>
        private bool IsUserDefinedFunctionName(string functionName)
        {
            if (string.IsNullOrEmpty(functionName) || _module?.Functions == null) return false;

            if (!ReferenceEquals(_userFunctionNamesSource, _module))
            {
                _userFunctionNamesSource = _module;
                _userFunctionNames.Clear();
                foreach (var f in _module.Functions)
                {
                    if (string.IsNullOrEmpty(f.Name)) continue;
                    _userFunctionNames.Add(f.Name);
                    var dot = f.Name.LastIndexOf('.');
                    if (dot >= 0 && dot < f.Name.Length - 1)
                        _userFunctionNames.Add(f.Name.Substring(dot + 1));
                }
            }

            return _userFunctionNames.Contains(functionName);
        }

        /// <remarks>
        /// <c>internal</c> only so <c>NetClaimPredicateTests</c> can assert the claim predicate
        /// against THIS method rather than against a re-inlined copy of its body. Comparing the
        /// predicate to <see cref="HasStdLibEmission"/> alone is circular — both read the same
        /// three helpers — and would stay green if an arm were added here without going through
        /// <see cref="StdLibArm"/>.
        /// </remarks>
        internal string EmitStdLibCall(string functionName, List<string> args, IRCall call = null)
        {
            // A user definition wins over EVERY builtin below — framework exports, the P1
            // static dispatch, and the plain stdlib arms alike — matching the single guard
            // the C# backend puts in front of both of its providers. Returning null here
            // falls through to the regular user-function call in the caller.
            // NOTE this is deliberately checked against the call name AS WRITTEN: a dotted
            // `DateTime.Parse(s)` never matches a user `Function Parse(...)` (the set holds
            // "M.Parse"/"Parse", not "DateTime.Parse"), so static dispatch is unaffected.
            if (IsUserDefinedFunctionName(functionName)) return null;

            // Check game framework functions first (case-insensitive match).
            // Framework exports are extern "C" — none take std::string — so
            // string-typed args are marshaled to const char* via .c_str()
            // (string literals already render as const char* and stay as-is).
            var frameworkCall = EmitFrameworkCall(functionName, MarshalFrameworkArgs(args, call));
            if (frameworkCall != null) return frameworkCall;

            // P1 static dispatch, call form: a
            // dotted call on a NativeOwned type name (`DateTime.Parse(s)`,
            // `Guid.NewGuid()`, `TimeSpan.FromDays(n)`) lowers to the runtime's static
            // member function `BasicLang::X::Member(args)`. StaticProperty covers the
            // parenthesized access form (`DateTime.Now()`); the paren-less property form
            // arrives as an IRFieldAccess and is handled in Visit(IRFieldAccess).
            if (TryGetNativeBclStaticMember(functionName, out var bclTypeName, out var bclStatic))
            {
                return $"BasicLang::{BclCanonicalName(bclTypeName)}::{bclStatic.CppName ?? bclStatic.MemberName}({string.Join(", ", args)})";
            }

            return StdLibArm(functionName, args, call);
        }

        /// <summary>
        /// The <c>NativeBclSurface</c> static-dispatch branch of <see cref="EmitStdLibCall"/>
        /// (spec §6.5 row (c), historically inline at lines 2234-2246), lifted out so
        /// <see cref="BasicLang.Net.NetClaimPredicate"/> can ask the SAME question the generator
        /// answers instead of re-deriving it. A dotted call on a NativeOwned type name
        /// (<c>DateTime.Parse(s)</c>, <c>Guid.NewGuid()</c>, <c>TimeSpan.FromDays(n)</c>) lowers to
        /// the runtime's static member function <c>BasicLang::X::Member(args)</c>. StaticProperty
        /// covers the parenthesized access form (<c>DateTime.Now()</c>); the paren-less property
        /// form arrives as an IRFieldAccess and is handled in Visit(IRFieldAccess).
        /// </summary>
        internal static bool TryGetNativeBclStaticMember(
            string functionName, out string typeName, out NativeBclMember member)
        {
            typeName = null;
            member = null;
            if (string.IsNullOrEmpty(functionName)) return false;

            var dot = functionName.LastIndexOf('.');
            if (dot <= 0 || dot >= functionName.Length - 1) return false;

            var candidateType = functionName.Substring(0, dot);
            var candidateMember = functionName.Substring(dot + 1);
            if (!IsNativeOwnedBclType(candidateType)
                || !NativeBclSurface.TryGetMember(candidateType, candidateMember, out var found)
                || (found.Kind != NativeBclMemberKind.StaticMethod
                    && found.Kind != NativeBclMemberKind.StaticProperty))
            {
                return false;
            }

            typeName = candidateType;
            member = found;
            return true;
        }

        /// <summary>
        /// The plain stdlib arm table — the <c>functionName.ToLower()</c> switch that used to sit
        /// inline at the end of <see cref="EmitStdLibCall"/> (historically lines 2256-2346).
        ///
        /// <para><b>Extracted so there is exactly ONE arm table, not two.</b> Spec §6.5 row (c)
        /// makes "does an arm exist for this call?" a question the claim predicate must answer
        /// identically to the generator, and a predicate carrying its own parallel switch is how
        /// <c>Console.WriteLine</c> silently stops being claimed and every existing program gets
        /// rewritten through the shim. <see cref="HasStdLibEmission"/> calls this method; so does
        /// <see cref="EmitStdLibCall"/>. Static and free of generator state precisely so the
        /// predicate can reach it. Pinned by
        /// <c>NetClaimPredicateTests.RowCUsesTheGeneratorsOwnArmCheckAndCannotDrift</c>.</para>
        /// </summary>
        internal static string StdLibArm(string functionName, List<string> args, IRCall call)
        {
            if (functionName == null) return null;

            // The C* conversion intrinsics (CInt/CLng/CDbl/CSng/CStr/CBool) take the
            // stdlib route here, NOT Visit(IRCast) — on a Decimal-typed argument a
            // static_cast / std::to_string is invalid C++ (the engine deliberately has no
            // conversion operator), so those arms route through the engine instead.
            var arg0IsDecimal = call != null && call.Arguments.Count > 0
                && IsNativeOwnedBclType(call.Arguments[0]?.Type?.Name)
                && call.Arguments[0].Type.Name.Equals("Decimal", StringComparison.OrdinalIgnoreCase);

            return functionName.ToLower() switch
            {
                // EVERY cout surface widens Byte/SByte the same way (see NumericPrintArg,
                // spec §14.6) — a split where PrintLine printed 'A' and Console.WriteLine
                // printed 65 would be a fresh internal inconsistency.
                "print" => $"cout << {NumericPrintArg(args[0], call, 0)}",
                "printline" => $"cout << {NumericPrintArg(args[0], call, 0)} << endl",
                // .NET Console surface (console-app template parity).
                "console.writeline" => args.Count == 0 ? "cout << endl" : $"cout << {NumericPrintArg(args[0], call, 0)} << endl",
                "console.write" => args.Count == 0 ? "cout << \"\"" : $"cout << {NumericPrintArg(args[0], call, 0)}",
                "readline" => "([](){ string s; getline(cin, s); return s; })()",
                "len" => $"static_cast<int32_t>({args[0]}.length())",
                "left" => $"{args[0]}.substr(0, {args[1]})",
                "right" => $"{args[0]}.substr({args[0]}.length() - {args[1]})",
                "mid" => $"{args[0]}.substr({args[1]} - 1, {args[2]})",
                "trim" => $"([](string s){{ auto start = s.find_first_not_of(\" \\t\"); auto end = s.find_last_not_of(\" \\t\"); return start == string::npos ? \"\" : s.substr(start, end - start + 1); }})({args[0]})",
                "ucase" => $"([](string s){{ transform(s.begin(), s.end(), s.begin(), ::toupper); return s; }})({args[0]})",
                "lcase" => $"([](string s){{ transform(s.begin(), s.end(), s.begin(), ::tolower); return s; }})({args[0]})",
                "instr" => $"static_cast<int32_t>({args[0]}.find({args[1]}) + 1)",
                "replace" => $"([](string s, const string& from, const string& to){{ size_t pos = 0; while ((pos = s.find(from, pos)) != string::npos) {{ s.replace(pos, from.length(), to); pos += to.length(); }} return s; }})({args[0]}, {args[1]}, {args[2]})",
                "abs" => $"abs({args[0]})",
                "sqrt" => $"sqrt({args[0]})",
                "pow" => $"pow({args[0]}, {args[1]})",
                "sin" => $"sin({args[0]})",
                "cos" => $"cos({args[0]})",
                "tan" => $"tan({args[0]})",
                "log" => $"log({args[0]})",
                "exp" => $"exp({args[0]})",
                "floor" => $"floor({args[0]})",
                "ceiling" => $"ceil({args[0]})",
                "round" => $"round({args[0]})",
                "min" => $"min({args[0]}, {args[1]})",
                "max" => $"max({args[0]}, {args[1]})",
                // Decimal → integral goes through ToDouble() and then the C++ cast, which
                // truncates toward zero — the SAME convention every other C* arm here has
                // always used (this backend's `cint` on a Double truncates too).
                // PRE-EXISTING cross-backend divergence, NOT introduced by P1 and not
                // Decimal-specific: the C# backend emits Convert.ToInt32, which ROUNDS.
                // Use CType(x, Integer) — truncating on both backends — when parity matters.
                // Precision note: ToDouble is exact to 15 significant digits, so a
                // >15-digit Decimal narrows approximately.
                "cint" => arg0IsDecimal
                    ? $"static_cast<int32_t>(({args[0]}).ToDouble())"
                    : $"static_cast<int32_t>({args[0]})",
                "clng" => arg0IsDecimal
                    ? $"static_cast<int64_t>(({args[0]}).ToDouble())"
                    : $"static_cast<int64_t>({args[0]})",
                "cdbl" => arg0IsDecimal
                    ? $"({args[0]}).ToDouble()"
                    : $"static_cast<double>({args[0]})",
                "csng" => arg0IsDecimal
                    ? $"static_cast<float>(({args[0]}).ToDouble())"
                    : $"static_cast<float>({args[0]})",
                // std::to_string has no Decimal overload; the engine's scale-preserving
                // ToString() is the .NET-faithful rendering.
                "cstr" => arg0IsDecimal
                    ? $"({args[0]}).ToString()"
                    : $"to_string({args[0]})",
                // .NET Convert.ToBoolean(decimal) is `value != 0`; IsZeroMag() is the exact
                // magnitude test (and treats a canonicalized -0 as zero, like .NET).
                "cbool" => arg0IsDecimal
                    ? $"(!({args[0]}).IsZeroMag())"
                    : $"static_cast<bool>({args[0]})",
                // ---- VB date/time stdlib (spec §7, P1 Task 12). The analyzer
                // registrations (Task 5) copied the repo's C# StdLib function table
                // VERBATIM, and these emissions mirror StdLib/CSharpStdLib.cs's
                // EmitDateTimeCall arm-for-arm so the two backends agree:
                //   DateAdd(date, interval, number) / DateDiff(date1, date2, interval)
                //   — the repo table's argument order, NOT classic VB's.
                //   DateDiff returns Integer (the repo table again), so every branch
                //   narrows exactly where the C# emission's `(int)` does.
                "now" => "BasicLang::DateTime::Now()",
                "today" => "BasicLang::DateTime::Today()",
                "year" => $"({args[0]}).Year()",
                "month" => $"({args[0]}).Month()",
                "day" => $"({args[0]}).Day()",
                "hour" => $"({args[0]}).Hour()",
                "minute" => $"({args[0]}).Minute()",
                "second" => $"({args[0]}).Second()",
                "dateadd" => EmitVbDateAdd(args[0], args[1], args[2]),
                "datediff" => EmitVbDateDiff(args[0], args[1], args[2]),
                "formatdate" => $"({args[0]}).ToString({args[1]})",
                // String return, matching the analyzer registration and the C#
                // backend's `Guid.NewGuid().ToString()`.
                "newguid" => "BasicLang::Guid::NewGuid().ToString()",
                "ubound" => $"(static_cast<int32_t>({args[0]}.size()) - 1)",
                "lbound" => "0",
                "rnd" => "(static_cast<double>(rand()) / RAND_MAX)",
                "randomize" => "srand(time(nullptr))",
                _ => null
            };
        }

        /// <summary>
        /// Lowercase an interval-part string IN PLACE, ASCII-only — the C++ counterpart of
        /// the C# emission's <c>interval.ToLower()</c>. Written as an explicit range-for
        /// rather than <c>std::transform(..., ::tolower)</c> so the emitted lambda depends
        /// on no header the generator does not already emit unconditionally
        /// (<c>&lt;cctype&gt;</c> is not in that set).
        /// </summary>
        private const string LowercaseIntervalStmt =
            "for (char& bl_c : bl_iv) if (bl_c >= 'A' && bl_c <= 'Z') bl_c = static_cast<char>(bl_c - 'A' + 'a'); ";

        /// <summary>
        /// <c>DateAdd(date, interval, number)</c> — spec §7 / §14.3. The accepted
        /// interval-part set is EXACTLY the one <c>CSharpStdLib.EmitDateAdd</c> accepts,
        /// compared case-insensitively as its <c>.ToLower()</c> does:
        /// <c>d</c> days, <c>m</c> months, <c>y</c> years, <c>h</c> hours,
        /// <c>n</c> minutes, <c>s</c> seconds.
        ///
        /// An UNRECOGNIZED interval is deliberately NOT a runtime throw: the C# emission's
        /// switch has a <c>_ =&gt; date</c> default, so the repo's cross-backend contract is
        /// "returns the date unchanged". Mirroring that is the whole point of this arm;
        /// diverging into a <c>std::runtime_error</c> would break the Task 13 parity oracle.
        ///
        /// Emitted as an immediately-invoked NON-capturing lambda, which binds each argument
        /// to a parameter EXACTLY ONCE. The argument that makes this matter is the INTERVAL,
        /// not the date: an inlined if-chain would have to place the interval expression in
        /// up to six conditions and re-run the ASCII-lowercase transform on each, whereas the
        /// taken branch evaluates the date exactly once either way (the same reason the C#
        /// emission gets away with textually repeating <c>{date}</c> across its switch arms).
        /// </summary>
        private static string EmitVbDateAdd(string date, string interval, string number) =>
            "([](const BasicLang::DateTime& bl_d, std::string bl_iv, int32_t bl_n) -> BasicLang::DateTime { "
            + LowercaseIntervalStmt
            + "if (bl_iv == \"d\") return bl_d.AddDays(bl_n); "
            + "if (bl_iv == \"m\") return bl_d.AddMonths(bl_n); "
            + "if (bl_iv == \"y\") return bl_d.AddYears(bl_n); "
            + "if (bl_iv == \"h\") return bl_d.AddHours(bl_n); "
            + "if (bl_iv == \"n\") return bl_d.AddMinutes(bl_n); "
            + "if (bl_iv == \"s\") return bl_d.AddSeconds(bl_n); "
            + "return bl_d; })"
            + $"({date}, {interval}, {number})";

        /// <summary>
        /// <c>DateDiff(date1, date2, interval)</c> — the repo's argument order (date1 is the
        /// EARLIER operand; the result is date2 − date1). Same interval-part set as
        /// <see cref="EmitVbDateAdd"/>, and the same non-throwing default: an unrecognized
        /// interval yields 0, exactly as <c>CSharpStdLib.EmitDateDiff</c>'s <c>_ =&gt; 0</c>.
        /// The month/year arms are calendar differences (not elapsed time), matching the C#
        /// emission's <c>(y2−y1)*12 + m2 − m1</c> and <c>y2 − y1</c>.
        /// </summary>
        private static string EmitVbDateDiff(string date1, string date2, string interval) =>
            "([](const BasicLang::DateTime& bl_d1, const BasicLang::DateTime& bl_d2, std::string bl_iv) -> int32_t { "
            + LowercaseIntervalStmt
            + "if (bl_iv == \"d\") return static_cast<int32_t>((bl_d2 - bl_d1).TotalDays()); "
            + "if (bl_iv == \"m\") return (bl_d2.Year() - bl_d1.Year()) * 12 + bl_d2.Month() - bl_d1.Month(); "
            + "if (bl_iv == \"y\") return bl_d2.Year() - bl_d1.Year(); "
            + "if (bl_iv == \"h\") return static_cast<int32_t>((bl_d2 - bl_d1).TotalHours()); "
            + "if (bl_iv == \"n\") return static_cast<int32_t>((bl_d2 - bl_d1).TotalMinutes()); "
            + "if (bl_iv == \"s\") return static_cast<int32_t>((bl_d2 - bl_d1).TotalSeconds()); "
            + "return 0; })"
            + $"({date1}, {date2}, {interval})";

        /// <summary>
        /// Emit calls to the VisualGameStudioEngine C DLL functions.
        /// These map directly to Framework_* exports from framework.h.
        /// </summary>
        private string EmitFrameworkCall(string functionName, List<string> args)
        {
            var result = FrameworkCallExpression(functionName, args);

            if (result != null)
            {
                _usesFramework = true;
                // Extract the C function name from the result (everything before the first '(')
                var parenIdx = result.IndexOf('(');
                if (parenIdx > 0)
                    _frameworkFunctionsUsed.Add(result.Substring(0, parenIdx));
            }

            return result;
        }

        /// <summary>
        /// The game-framework arm table, split out of <see cref="EmitFrameworkCall"/> so it can be
        /// asked WITHOUT its side effects (<c>_usesFramework</c> and <c>_frameworkFunctionsUsed</c>,
        /// which decide whether the generated translation unit declares the Framework_* externs).
        /// <see cref="HasStdLibEmission"/> — and through it spec §6.5's claim predicate — needs the
        /// answer only; marking the framework as used because a PREDICATE asked would emit extern
        /// declarations into programs that never call the engine.
        ///
        /// <para>Every arm key here is a bare, undotted name, so this can never claim a
        /// <c>Type.Member</c> call. It is still consulted by <see cref="HasStdLibEmission"/> rather
        /// than assumed away, because "no arm has a dot" is a property of this table that a future
        /// edit could change silently.</para>
        /// </summary>
        internal static string FrameworkCallExpression(string functionName, List<string> args)
        {
            var allArgs = string.Join(", ", args);

            return functionName switch
            {
                // Core
                "GameInit" => $"Framework_Initialize({allArgs})",
                "GameShutdown" => "Framework_Shutdown()",
                "GameBeginFrame" => "Framework_BeginDrawing()",
                "GameEndFrame" => "Framework_EndDrawing()",
                "GameShouldClose" => "Framework_ShouldClose()",
                "GameGetDeltaTime" => "Framework_GetDeltaTime()",
                "GameGetFPS" => "Framework_GetFPS()",

                // Input
                "IsKeyPressed" => $"Framework_IsKeyPressed({allArgs})",
                "IsKeyDown" => $"Framework_IsKeyDown({allArgs})",
                "IsKeyReleased" => $"Framework_IsKeyReleased({allArgs})",
                "IsMouseButtonPressed" => $"Framework_IsMouseButtonPressed({allArgs})",
                "IsMouseButtonDown" => $"Framework_IsMouseButtonDown({allArgs})",
                "GetMouseX" => "Framework_GetMouseX()",
                "GetMouseY" => "Framework_GetMouseY()",

                // Drawing
                "ClearBackground" => $"Framework_ClearBackground((uint8_t)({GetArg(args, 0)}), (uint8_t)({GetArg(args, 1)}), (uint8_t)({GetArg(args, 2)}), 255)",
                "DrawRectangle" => $"Framework_DrawRectangle({GetArg(args, 0)}, {GetArg(args, 1)}, {GetArg(args, 2)}, {GetArg(args, 3)}, (uint8_t)({GetArg(args, 4)}), (uint8_t)({GetArg(args, 5)}), (uint8_t)({GetArg(args, 6)}), (uint8_t)({GetArg(args, 7)}))",
                "DrawCircle" => $"Framework_DrawCircle({GetArg(args, 0)}, {GetArg(args, 1)}, {GetArg(args, 2)}, (uint8_t)({GetArg(args, 3)}), (uint8_t)({GetArg(args, 4)}), (uint8_t)({GetArg(args, 5)}), (uint8_t)({GetArg(args, 6)}))",
                "DrawLine" => $"Framework_DrawLine({GetArg(args, 0)}, {GetArg(args, 1)}, {GetArg(args, 2)}, {GetArg(args, 3)}, (uint8_t)({GetArg(args, 4)}), (uint8_t)({GetArg(args, 5)}), (uint8_t)({GetArg(args, 6)}), (uint8_t)({GetArg(args, 7)}))",
                "DrawText" => $"Framework_DrawText({GetArg(args, 0)}, {GetArg(args, 1)}, {GetArg(args, 2)}, {GetArg(args, 3)}, (uint8_t)({GetArg(args, 4)}), (uint8_t)({GetArg(args, 5)}), (uint8_t)({GetArg(args, 6)}), (uint8_t)({GetArg(args, 7)}))",
                "DrawRectangleLines" => $"Framework_DrawRectangleLines({GetArg(args, 0)}, {GetArg(args, 1)}, {GetArg(args, 2)}, {GetArg(args, 3)}, (uint8_t)({GetArg(args, 4)}), (uint8_t)({GetArg(args, 5)}), (uint8_t)({GetArg(args, 6)}), (uint8_t)({GetArg(args, 7)}))",
                "DrawCircleLines" => $"Framework_DrawCircleLines({GetArg(args, 0)}, {GetArg(args, 1)}, {GetArg(args, 2)}, (uint8_t)({GetArg(args, 3)}), (uint8_t)({GetArg(args, 4)}), (uint8_t)({GetArg(args, 5)}), (uint8_t)({GetArg(args, 6)}))",
                "DrawTriangle" => $"Framework_DrawTriangle({allArgs})",
                "DrawTriangleLines" => $"Framework_DrawTriangleLines({allArgs})",
                "DrawFPS" => $"Framework_DrawFPS({allArgs})",

                // Textures
                "LoadTexture" => $"Framework_LoadTexture({allArgs})",
                "UnloadTexture" => $"Framework_UnloadTexture({allArgs})",
                "DrawTexture" => $"Framework_DrawTextureSimple({GetArg(args, 0)}, {GetArg(args, 1)}, {GetArg(args, 2)}, (uint8_t)({GetArg(args, 3)}), (uint8_t)({GetArg(args, 4)}), (uint8_t)({GetArg(args, 5)}), (uint8_t)({GetArg(args, 6)}))",
                "DrawTextureEx" => $"Framework_DrawTextureEx({allArgs})",

                // Entities
                "CreateEntity" => "Framework_Entity_Create()",
                "DestroyEntity" => $"Framework_Entity_Destroy({allArgs})",
                "EntitySetPosition" => $"Framework_Entity_SetPosition({allArgs})",
                "EntityGetX" => $"Framework_Entity_GetPositionX({allArgs})",
                "EntityGetY" => $"Framework_Entity_GetPositionY({allArgs})",
                "EntitySetVelocity" => $"Framework_Entity_SetVelocity({allArgs})",
                "EntitySetSprite" => $"Framework_Entity_SetSprite({allArgs})",
                "EntitySetCollider" => $"Framework_Entity_SetColliderBox({allArgs})",
                "EntityIsActive" => $"Framework_Entity_IsActive({allArgs})",
                "EntitySetActive" => $"Framework_Entity_SetActive({allArgs})",

                // Audio
                "LoadSound" => $"Framework_LoadSound({allArgs})",
                "PlaySound" => $"Framework_PlaySound({allArgs})",
                "StopSound" => $"Framework_StopSound({allArgs})",
                "SetSoundVolume" => $"Framework_SetSoundVolume({allArgs})",
                "LoadMusic" => $"Framework_LoadMusic({allArgs})",
                "PlayMusic" => $"Framework_PlayMusic({allArgs})",
                "StopMusic" => $"Framework_StopMusic({allArgs})",

                // Audio Init
                "InitAudio" => "Framework_InitAudio()",
                "CloseAudio" => "Framework_CloseAudio()",
                "SetMasterVolume" => $"Framework_SetMasterVolume({allArgs})",

                // Time
                "SetTargetFPS" => $"Framework_SetTargetFPS({allArgs})",
                "GetFrameTime" => "Framework_GetFrameTime()",
                "GetTime" => "Framework_GetTime()",
                "SetTimeScale" => $"Framework_SetTimeScale({allArgs})",
                "GetTimeScale" => "Framework_GetTimeScale()",

                // Camera
                "CameraSetPosition" => $"Framework_Camera_SetPosition({allArgs})",
                "CameraSetZoom" => $"Framework_Camera_SetZoom({allArgs})",
                "CameraFollow" => $"Framework_Camera_FollowEntity({allArgs})",
                "CameraBeginMode" => "Framework_Camera_BeginMode()",
                "CameraEndMode" => "Framework_Camera_EndMode()",
                "CameraUpdate" => $"Framework_Camera_Update({allArgs})",
                "CameraReset" => "Framework_Camera_Reset()",
                "CameraSetTarget" => $"Framework_Camera_SetTarget({allArgs})",
                "CameraSetOffset" => $"Framework_Camera_SetOffset({allArgs})",
                "CameraSetRotation" => $"Framework_Camera_SetRotation({allArgs})",
                "CameraGetZoom" => "Framework_Camera_GetZoom()",
                "CameraGetRotation" => "Framework_Camera_GetRotation()",
                "CameraPanTo" => $"Framework_Camera_PanTo({allArgs})",
                "CameraZoomTo" => $"Framework_Camera_ZoomTo({allArgs})",
                "CameraSetBounds" => $"Framework_Camera_SetBounds({allArgs})",
                "CameraSetBoundsEnabled" => $"Framework_Camera_SetBoundsEnabled({allArgs})",
                "CameraSetFollowLerp" => $"Framework_Camera_SetFollowLerp({allArgs})",
                "CameraShake" => $"Framework_Camera_Shake({allArgs})",

                // Cursor
                "ShowCursor" => "Framework_ShowCursor()",
                "HideCursor" => "Framework_HideCursor()",
                "IsCursorHidden" => "Framework_IsCursorHidden()",
                "SetMousePosition" => $"Framework_SetMousePosition({allArgs})",
                "GetMouseWheelMove" => "Framework_GetMouseWheelMove()",

                // Update
                "GameUpdate" => "Framework_Update()",

                _ => null
            };
        }

        /// <summary>
        /// Spec §6.5 row (c)'s second half: does <paramref name="functionName"/> have a native emit
        /// arm at all? True iff <see cref="EmitStdLibCall"/> would return non-null for it through
        /// any of its three providers — <see cref="FrameworkCallExpression"/>,
        /// <see cref="TryGetNativeBclStaticMember"/>, or <see cref="StdLibArm"/>.
        ///
        /// <para><b>This is the anti-drift seam and the reason those three are static.</b> The
        /// claim predicate must not carry its own copy of the arm table: reading "claimed" as
        /// "registry + CppCapabilityChecker" and losing <c>Console.WriteLine</c> would route every
        /// existing program's output through the shim.</para>
        ///
        /// <para><b>The module-shadowing guard is deliberately NOT applied.</b>
        /// <see cref="EmitStdLibCall"/> first checks <see cref="IsUserDefinedFunctionName"/>, which
        /// depends on the module being generated; this asks only whether a BUILTIN arm exists. A
        /// user function shadowing a builtin is dispatched as a user function and never reaches
        /// the shim either, so the narrower question is the safe one — and it keeps the predicate a
        /// pure function of the name, which is what lets the analyzer call it before any module
        /// exists.</para>
        ///
        /// <para>Arguments are placeholders: no arm's EXISTENCE depends on argument values. Eight
        /// is exactly the highest count any arm reads — <see cref="FrameworkCallExpression"/>'s
        /// <c>DrawRectangle</c>/<c>DrawLine</c>/<c>DrawText</c>/<c>DrawRectangleLines</c> read
        /// <c>GetArg(args, 7)</c>, well past <see cref="StdLibArm"/>'s highest direct index
        /// (<c>args[2]</c>, in <c>mid</c>/<c>replace</c>/<c>dateadd</c>/<c>datediff</c>). Do NOT
        /// shrink this array below 8: <see cref="StdLibArm"/> indexes <c>args</c> DIRECTLY and
        /// would throw, and <c>GetArg</c>'s tolerance does not cover it. <c>call</c> is null, which
        /// only makes the Decimal-aware C* arms pick their non-Decimal shape — still non-null.</para>
        /// </summary>
        internal static bool HasStdLibEmission(string functionName)
        {
            if (string.IsNullOrEmpty(functionName)) return false;

            return FrameworkCallExpression(functionName, ArmProbeArgs) != null
                   || TryGetNativeBclStaticMember(functionName, out _, out _)
                   || StdLibArm(functionName, ArmProbeArgs, null) != null;
        }

        private static readonly List<string> ArmProbeArgs =
            new List<string> { "a", "a", "a", "a", "a", "a", "a", "a" };

        /// <summary>
        /// Map of Framework_* function name to its C declaration (without __declspec).
        /// These match the signatures in VisualGameStudioEngine/framework.h. Shared by the
        /// legacy used-only extern insert (Generate) and the full-catalog runtime header
        /// (GenerateSplit, D5).
        /// </summary>
        private static readonly Dictionary<string, string> FrameworkSignatures
            = new Dictionary<string, string>
            {
                // Core
                ["Framework_Initialize"] = "bool Framework_Initialize(int width, int height, const char* title)",
                ["Framework_Update"] = "void Framework_Update()",
                ["Framework_ShouldClose"] = "bool Framework_ShouldClose()",
                ["Framework_Shutdown"] = "void Framework_Shutdown()",
                ["Framework_BeginDrawing"] = "void Framework_BeginDrawing()",
                ["Framework_EndDrawing"] = "void Framework_EndDrawing()",
                ["Framework_ClearBackground"] = "void Framework_ClearBackground(unsigned char r, unsigned char g, unsigned char b, unsigned char a)",

                // Timing
                ["Framework_SetTargetFPS"] = "void Framework_SetTargetFPS(int fps)",
                ["Framework_GetFrameTime"] = "float Framework_GetFrameTime()",
                ["Framework_GetDeltaTime"] = "float Framework_GetDeltaTime()",
                ["Framework_GetTime"] = "double Framework_GetTime()",
                ["Framework_GetFPS"] = "int Framework_GetFPS()",

                // Input - Keyboard
                ["Framework_IsKeyPressed"] = "bool Framework_IsKeyPressed(int key)",
                ["Framework_IsKeyDown"] = "bool Framework_IsKeyDown(int key)",
                ["Framework_IsKeyReleased"] = "bool Framework_IsKeyReleased(int key)",

                // Input - Mouse
                ["Framework_IsMouseButtonPressed"] = "bool Framework_IsMouseButtonPressed(int button)",
                ["Framework_IsMouseButtonDown"] = "bool Framework_IsMouseButtonDown(int button)",
                ["Framework_GetMouseX"] = "int Framework_GetMouseX()",
                ["Framework_GetMouseY"] = "int Framework_GetMouseY()",
                ["Framework_GetMouseWheelMove"] = "float Framework_GetMouseWheelMove()",

                // Cursor
                ["Framework_ShowCursor"] = "void Framework_ShowCursor()",
                ["Framework_HideCursor"] = "void Framework_HideCursor()",

                // Drawing - Shapes
                ["Framework_DrawText"] = "void Framework_DrawText(const char* text, int x, int y, int fontSize, unsigned char r, unsigned char g, unsigned char b, unsigned char a)",
                ["Framework_DrawRectangle"] = "void Framework_DrawRectangle(int x, int y, int width, int height, unsigned char r, unsigned char g, unsigned char b, unsigned char a)",
                ["Framework_DrawRectangleLines"] = "void Framework_DrawRectangleLines(int x, int y, int width, int height, unsigned char r, unsigned char g, unsigned char b, unsigned char a)",
                ["Framework_DrawCircle"] = "void Framework_DrawCircle(int centerX, int centerY, float radius, unsigned char r, unsigned char g, unsigned char b, unsigned char a)",
                ["Framework_DrawCircleLines"] = "void Framework_DrawCircleLines(int centerX, int centerY, float radius, unsigned char r, unsigned char g, unsigned char b, unsigned char a)",
                ["Framework_DrawLine"] = "void Framework_DrawLine(int startX, int startY, int endX, int endY, unsigned char r, unsigned char g, unsigned char b, unsigned char a)",
                ["Framework_DrawTriangle"] = "void Framework_DrawTriangle(int x1, int y1, int x2, int y2, int x3, int y3, unsigned char r, unsigned char g, unsigned char b, unsigned char a)",
                ["Framework_DrawTriangleLines"] = "void Framework_DrawTriangleLines(int x1, int y1, int x2, int y2, int x3, int y3, unsigned char r, unsigned char g, unsigned char b, unsigned char a)",
                ["Framework_DrawFPS"] = "void Framework_DrawFPS(int x, int y)",

                // Textures
                ["Framework_LoadTexture"] = "int Framework_LoadTexture(const char* fileName)",
                ["Framework_UnloadTexture"] = "void Framework_UnloadTexture(int texture)",
                ["Framework_DrawTextureSimple"] = "void Framework_DrawTextureSimple(int texture, int posX, int posY, unsigned char r, unsigned char g, unsigned char b, unsigned char a)",
                ["Framework_DrawTextureEx"] = "void Framework_DrawTextureEx(int texture, float posX, float posY, float rotation, float scale, unsigned char r, unsigned char g, unsigned char b, unsigned char a)",

                // Entities
                ["Framework_Entity_Create"] = "int Framework_Entity_Create()",
                ["Framework_Entity_Destroy"] = "void Framework_Entity_Destroy(int id)",
                ["Framework_Entity_SetPosition"] = "void Framework_Entity_SetPosition(int id, float x, float y)",
                ["Framework_Entity_GetPositionX"] = "float Framework_Entity_GetPositionX(int id)",
                ["Framework_Entity_GetPositionY"] = "float Framework_Entity_GetPositionY(int id)",
                ["Framework_Entity_SetVelocity"] = "void Framework_Entity_SetVelocity(int id, float vx, float vy)",
                ["Framework_Entity_SetSprite"] = "void Framework_Entity_SetSprite(int id, int texture)",
                ["Framework_Entity_SetColliderBox"] = "void Framework_Entity_SetColliderBox(int id, float w, float h)",
                ["Framework_Entity_IsActive"] = "bool Framework_Entity_IsActive(int id)",
                ["Framework_Entity_SetActive"] = "void Framework_Entity_SetActive(int id, bool active)",

                // Audio
                ["Framework_InitAudio"] = "bool Framework_InitAudio()",
                ["Framework_LoadSound"] = "int Framework_LoadSound(const char* file)",
                ["Framework_PlaySound"] = "void Framework_PlaySound(int handle)",
                ["Framework_StopSound"] = "void Framework_StopSound(int handle)",
                ["Framework_SetSoundVolume"] = "void Framework_SetSoundVolume(int handle, float volume)",
                ["Framework_LoadMusic"] = "int Framework_LoadMusic(const char* file)",
                ["Framework_PlayMusic"] = "void Framework_PlayMusic(int handle)",
                ["Framework_StopMusic"] = "void Framework_StopMusic(int handle)",

                // Audio extras
                ["Framework_CloseAudio"] = "void Framework_CloseAudio()",
                ["Framework_SetMasterVolume"] = "void Framework_SetMasterVolume(float volume)",

                // Camera
                ["Framework_Camera_SetPosition"] = "void Framework_Camera_SetPosition(float x, float y)",
                ["Framework_Camera_SetTarget"] = "void Framework_Camera_SetTarget(float x, float y)",
                ["Framework_Camera_SetRotation"] = "void Framework_Camera_SetRotation(float rotation)",
                ["Framework_Camera_SetZoom"] = "void Framework_Camera_SetZoom(float zoom)",
                ["Framework_Camera_SetOffset"] = "void Framework_Camera_SetOffset(float x, float y)",
                ["Framework_Camera_GetZoom"] = "float Framework_Camera_GetZoom()",
                ["Framework_Camera_GetRotation"] = "float Framework_Camera_GetRotation()",
                ["Framework_Camera_FollowEntity"] = "void Framework_Camera_FollowEntity(int entity)",
                ["Framework_Camera_BeginMode"] = "void Framework_Camera_BeginMode()",
                ["Framework_Camera_EndMode"] = "void Framework_Camera_EndMode()",
                ["Framework_Camera_Update"] = "void Framework_Camera_Update(float dt)",
                ["Framework_Camera_Reset"] = "void Framework_Camera_Reset()",
                ["Framework_Camera_PanTo"] = "void Framework_Camera_PanTo(float worldX, float worldY, float duration)",
                ["Framework_Camera_ZoomTo"] = "void Framework_Camera_ZoomTo(float targetZoom, float duration)",
                ["Framework_Camera_SetBounds"] = "void Framework_Camera_SetBounds(float minX, float minY, float maxX, float maxY)",
                ["Framework_Camera_SetBoundsEnabled"] = "void Framework_Camera_SetBoundsEnabled(bool enabled)",
                ["Framework_Camera_SetFollowLerp"] = "void Framework_Camera_SetFollowLerp(float lerpSpeed)",
                ["Framework_Camera_Shake"] = "void Framework_Camera_Shake(float intensity, float duration)",

                // Timing extras
                ["Framework_SetTimeScale"] = "void Framework_SetTimeScale(float scale)",
                ["Framework_GetTimeScale"] = "float Framework_GetTimeScale()",

                // Cursor extras
                ["Framework_IsCursorHidden"] = "bool Framework_IsCursorHidden()",
                ["Framework_SetMousePosition"] = "void Framework_SetMousePosition(int x, int y)",
            };

        /// <summary>
        /// Generate extern "C" declarations for Framework_* functions used in the code.
        /// </summary>
        private string GenerateFrameworkExternDeclarations()
        {
            var sb = new StringBuilder();
            sb.AppendLine("// VisualGameStudioEngine framework function declarations");
            sb.AppendLine("// Link with VisualGameStudioEngine.dll");
            sb.AppendLine("#ifdef _WIN32");
            sb.AppendLine("#define FRAMEWORK_API __declspec(dllimport)");
            sb.AppendLine("#else");
            sb.AppendLine("#define FRAMEWORK_API");
            sb.AppendLine("#endif");
            sb.AppendLine();
            sb.AppendLine("extern \"C\" {");

            foreach (var funcName in _frameworkFunctionsUsed.OrderBy(f => f))
            {
                if (FrameworkSignatures.TryGetValue(funcName, out var sig))
                {
                    sb.AppendLine($"    FRAMEWORK_API {sig};");
                }
                else
                {
                    // Unknown function — emit a compiler error so it's caught at build time
                    sb.AppendLine($"    #error \"Unknown framework function: {funcName} — declaration not found, check framework.h\"");
                }
            }

            sb.AppendLine("}");
            return sb.ToString();
        }

        public override void Visit(IRReturn ret)
        {
            // Coroutines must end with co_return, never a plain return
            if (_currentFunction != null && _currentFunction.IsIterator)
            {
                WriteLine("co_return;");
                return;
            }

            // Async functions wrap the value in the Task<T> emulation struct
            if (_currentFunction != null && _currentFunction.IsAsync)
            {
                if (ret.Value != null)
                    WriteLine($"return {MapReturnType(_currentFunction)}{{ {GetValueName(ret.Value)} }};");
                else
                    WriteLine("return {};");
                return;
            }

            if (ret.Value != null)
            {
                var value = GetValueName(ret.Value);
                WriteLine($"return {value};");
            }
            else
            {
                WriteLine("return;");
            }
        }
        
        public override void Visit(IRBranch branch)
        {
            WriteLine($"goto {LabelName(branch.Target.Name)};");
        }
        
        public override void Visit(IRConditionalBranch condBranch)
        {
            var condition = GetValueName(condBranch.Condition);
            
            WriteLine($"if ({condition}) {{");
            Indent();
            WriteLine($"goto {LabelName(condBranch.TrueTarget.Name)};");
            Unindent();
            WriteLine("}");
            WriteLine($"else {{");
            Indent();
            WriteLine($"goto {LabelName(condBranch.FalseTarget.Name)};");
            Unindent();
            WriteLine("}");
        }
        
        public override void Visit(IRSwitch switchInst)
        {
            var value = GetValueName(switchInst.Value);

            // C++'s native `switch` only accepts integral compile-time-constant labels, so it
            // cannot express range/comparison/When/Or patterns or even a string constant. The
            // BasicLang parser routes EVERY `Select Case` value into IRSwitch.PatternCases
            // (Cases stays empty), so lower the whole switch to an if/else-if chain of gotos:
            // one boolean test per case, in source order (first match wins), falling through to
            // the default target. Type/tuple/binding patterns have no clean C++ lowering and are
            // rejected up front by CppCapabilityChecker, so only value-comparison patterns reach
            // here. (Legacy integral Cases are handled in the same ordered chain for robustness,
            // though the parser never populates them today.)
            foreach (var (caseValue, target) in switchInst.Cases)
            {
                WriteLine($"if ({value} == {ValueText(caseValue)}) {{ goto {LabelName(target.Name)}; }}");
            }

            foreach (var patternCase in switchInst.PatternCases)
            {
                var cond = PatternCaseCondition(value, patternCase);
                WriteLine($"if ({cond}) {{ goto {LabelName(patternCase.Target.Name)}; }}");
            }

            WriteLine($"goto {LabelName(switchInst.DefaultTarget.Name)};");
        }

        /// <summary>
        /// Full boolean C++ condition that is true when the discriminant <paramref name="value"/>
        /// matches <paramref name="pc"/> — the value/range/comparison test AND its optional When
        /// guard. Type/tuple/binding patterns are rejected by CppCapabilityChecker and never reach
        /// here.
        /// </summary>
        private string PatternCaseCondition(string value, IRPatternCase pc)
        {
            var match = PatternMatchCondition(value, pc);
            if (pc.WhenGuard != null)
                return $"({match}) && ({RenderInline(pc.WhenGuard)})";
            return match;
        }

        /// <summary>The value/range/comparison test for a single pattern case (no When guard).</summary>
        private string PatternMatchCondition(string value, IRPatternCase pc)
        {
            switch (pc)
            {
                case IRConstantPatternCase c:
                    return $"{value} == {ValueText(c.Value)}";
                case IRRangePatternCase r:
                    return $"({value} >= {ValueText(r.LowerBound)} && {value} <= {ValueText(r.UpperBound)})";
                case IRComparisonPatternCase cmp:
                    return $"{value} {MapCasePatternOperator(cmp.Operator)} {ValueText(cmp.CompareValue)}";
                case IRNothingPatternCase:
                    return $"{value} == nullptr";
                case IROrPatternCase or:
                    if (or.Alternatives.Count == 0) return "false";
                    return "(" + string.Join(" || ", or.Alternatives.Select(a => PatternMatchCondition(value, a))) + ")";
                default:
                    // Unreachable: CppCapabilityChecker rejects type/tuple/binding patterns before
                    // emission. Fail loudly rather than silently drop if that guard is ever bypassed.
                    throw new CppCapabilityException(new List<string>
                    {
                        $"Select Case pattern '{pc.GetType().Name}' has no C++ lowering"
                    });
            }
        }

        /// <summary>VB Case comparison operator ('=','&lt;&gt;','&gt;','&lt;','&gt;=','&lt;=') to its C++ form.</summary>
        private static string MapCasePatternOperator(string op) => op switch
        {
            "=" => "==",
            "<>" => "!=",
            _ => op   // >, <, >=, <= are identical in C++
        };

        /// <summary>
        /// Render a pattern VALUE (a constant, a range bound, a comparison operand). These are
        /// evaluated as instructions BEFORE the switch (or are IRConstants), so reference them by
        /// their value name / literal — never re-inline, which could duplicate a side-effecting
        /// bound expression (e.g. `Case Foo() To Bar()`).
        /// </summary>
        private string ValueText(IRValue v) => v is IRConstant c ? EmitConstant(c) : GetValueName(v);

        /// <summary>
        /// Render an un-emitted expression tree (a suppressed When guard) as an inline C++
        /// expression. The guard is built with IRBuilder._suppressEmit set, so its operand
        /// instructions never entered a block; GetValueName alone would reference undeclared
        /// temps. Recurse over the common guard shapes (binary/compare/unary over
        /// constants/variables); fall back to GetValueName for leaves (variables, already-emitted
        /// values, inline foreign calls).
        /// </summary>
        private string RenderInline(IRValue v)
        {
            switch (v)
            {
                case IRConstant c:
                    return EmitConstant(c);
                case IRBinaryOp b:
                    return $"({RenderInline(b.Left)} {MapBinaryOperator(b.Operation)} {RenderInline(b.Right)})";
                case IRCompare cmp:
                    return $"({RenderInline(cmp.Left)} {MapCompareOperator(cmp.Comparison)} {RenderInline(cmp.Right)})";
                case IRUnaryOp u:
                    return $"({MapUnaryOperator(u.Operation)}{RenderInline(u.Operand)})";
                default:
                    return GetValueName(v);
            }
        }
        
        public override void Visit(IRPhi phi)
        {
            WriteLine($"// Phi node: {phi.Name}");
        }
        
        public override void Visit(IRAlloca alloca)
        {
            // Memory allocation handled in declarations
        }
        
        public override void Visit(IRGetElementPtr gep)
        {
            var basePtr = GetValueName(gep.BasePointer);
            var result = GetValueName(gep);
            
            if (gep.Indices.Count == 1)
            {
                var index = GetValueName(gep.Indices[0]);
                WriteLine($"{result} = &{basePtr}[{index}];");
            }
            else
            {
                var indices = string.Join("][", gep.Indices.Select(GetValueName));
                WriteLine($"{result} = &{basePtr}[{indices}];");
            }
        }
        
        public override void Visit(IRCast cast)
        {
            var value = GetValueName(cast.Value);
            var result = GetValueName(cast);

            // P1 Decimal conversions: a raw static_cast to/from the BasicLang::Decimal
            // struct is invalid C++ (the engine deliberately defines no conversion
            // operator), so EVERY CType direction routes through the engine (spec §6.2).
            // Anything not lowered here is rejected by CppCapabilityChecker.CheckNativeBclCast
            // with a clean diagnostic — never as a raw C++ error.
            var castTargetIsDecimal = IsNativeOwnedBclType(cast.Type?.Name)
                && cast.Type.Name.Equals("Decimal", StringComparison.OrdinalIgnoreCase);
            var castSourceIsDecimal = IsNativeOwnedBclType(cast.Value?.Type?.Name)
                && cast.Value.Type.Name.Equals("Decimal", StringComparison.OrdinalIgnoreCase);
            if (castTargetIsDecimal)
            {
                // Decimal → Decimal is the identity (never a lossy double round-trip).
                // Integral sources are EXACT via the int64/uint64 ctors (spec §10; the C#
                // backend's (decimal)x is exact — routing a Long ≥ ~1e15 through the double
                // ctor's 15-significant-digit rule would silently lose digits). Only
                // Single/Double sources take the converting double ctor's .NET 15-digit rule.
                if (castSourceIsDecimal)
                    WriteLine($"{result} = {value};");
                else if (IsIntegralTypeName(cast.Value?.Type?.Name))
                    WriteLine(string.Equals(cast.Value.Type.Name, "ULong", StringComparison.OrdinalIgnoreCase)
                        ? $"{result} = BasicLang::Decimal(static_cast<uint64_t>({value}));"
                        : $"{result} = BasicLang::Decimal(static_cast<int64_t>({value}));");
                else
                    WriteLine($"{result} = BasicLang::Decimal(static_cast<double>({value}));");
                return;
            }
            if (castSourceIsDecimal)
            {
                var castTargetName = cast.Type?.Name;
                if (string.Equals(castTargetName, "Double", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(castTargetName, "Single", StringComparison.OrdinalIgnoreCase))
                {
                    // .NET's explicit operator double (VarR8FromDec); Single narrows on assignment.
                    WriteLine($"{result} = ({value}).ToDouble();");
                    return;
                }
                if (IsIntegralTypeName(castTargetName))
                {
                    // .NET's explicit operator int/long/… TRUNCATES TOWARD ZERO (it does not
                    // round): (int)2.9m == 2, (int)(-2.9m) == -2. A C++ floating→integral
                    // static_cast has exactly that rule, so ToDouble() + the cast matches.
                    // Precision caveat: ToDouble is the .NET VarR8FromDec formula, exact to
                    // 15 significant digits — a Decimal with more digits narrows approximately.
                    WriteLine($"{result} = static_cast<{MapType(cast.Type)}>(({value}).ToDouble());");
                    return;
                }
                if (string.Equals(castTargetName, "String", StringComparison.OrdinalIgnoreCase))
                {
                    // std::to_string has no Decimal overload; the engine's scale-preserving
                    // ToString() is the .NET-faithful rendering (invariant, trailing zeros kept).
                    WriteLine($"{result} = ({value}).ToString();");
                    return;
                }
                if (string.Equals(castTargetName, "Boolean", StringComparison.OrdinalIgnoreCase))
                {
                    // .NET has no (bool)decimal operator at all — Convert.ToBoolean(decimal)
                    // is `value != 0`, and that is the only sane reading of CType(d, Boolean).
                    // (The C# backend emits `(bool)(d)` here, which csc rejects — a
                    // pre-existing C#-backend gap, tracked outside P1.)
                    WriteLine($"{result} = !({value}).IsZeroMag();");
                    return;
                }
            }

            var targetType = MapType(cast.Type);
            WriteLine($"{result} = static_cast<{targetType}>({value});");
        }
        
        public override void Visit(IRLabel label)
        {
            Unindent();
            WriteLine($"{LabelName(label.Name)}: ;");
            Indent();
        }
        
        public override void Visit(IRComment comment)
        {
            if (_options.GenerateComments)
            {
                WriteLine($"// {comment.Text}");
            }
        }

        public override void Visit(IRArrayAlloc arrayAlloc)
        {
            // The temp is already declared as std::vector<T> by DeclareLocalsAndTemporaries,
            // so SIZE it here rather than redeclaring it (a C array `T name[N]` both double-
            // declares and is not assignable). Name it via GetValueName — not .Name — so this
            // allocation, the element stores, and the assignment into the target local all
            // reference the same identifier (.Name and GetValueName can diverge for temps).
            var elementType = MapType(arrayAlloc.ElementType);
            var arrayName = GetValueName(arrayAlloc);
            WriteLine($"{arrayName} = std::vector<{elementType}>({arrayAlloc.Size});");
        }

        public override void Visit(IRArrayStore arrayStore)
        {
            var arrayName = GetValueName(arrayStore.Array);
            var indexVal = arrayStore.Index is IRConstant c ? c.Value.ToString() : GetValueName(arrayStore.Index);
            var valueVal = arrayStore.Value is IRConstant vc ? EmitConstant(vc) : GetValueName(arrayStore.Value);
            WriteLine($"{arrayName}[{indexVal}] = {valueVal};");
        }

        public override void Visit(IRAwait awaitInst)
        {
            // Synchronous Task<T> emulation: awaiting just unwraps the value.
            // Await over a direct call embeds the IRCall in the IRAwait without emitting
            // it as a block instruction (IRBuilder.Visit(AwaitExpressionNode)) - render
            // the call inline here.
            string task;
            if (awaitInst.Expression is IRCall embeddedCall)
            {
                var args = string.Join(", ", embeddedCall.Arguments.Select(GetValueName));
                task = $"{SanitizeName(embeddedCall.FunctionName)}({args})";
            }
            else
            {
                task = GetValueName(awaitInst.Expression);
            }
            WriteLine($"{GetValueName(awaitInst)} = {task}.get();");
        }

        public override void Visit(IRYield yieldInst)
        {
            // C++20 coroutine (BasicLang::Generator<T> in the runtime preamble)
            if (yieldInst.IsBreak)
                WriteLine("co_return;");
            else
                WriteLine($"co_yield {GetValueName(yieldInst.Value)};");
        }

        public override void Visit(IRNewObject newObj)
        {
            // Exception objects are consumed at the throw site (std::runtime_error);
            // there is no C++ class to construct here.
            if (CppExceptionTypes.IsNetException(newObj.ClassName)) return;

            // .NET-surface shim: `New String("="c, n)` is (char, count) but
            // std::string's fill constructor is (count, char) — swap. Emitting
            // `String(...)` verbatim references a nonexistent C++ type.
            if (string.Equals(newObj.ClassName, "String", StringComparison.OrdinalIgnoreCase))
            {
                var target = GetValueName(newObj);
                var ctorArgs = newObj.Arguments.Select(a => GetValueName(a)).ToList();
                var expr = ctorArgs.Count switch
                {
                    2 => $"std::string({ctorArgs[1]}, {ctorArgs[0]})",
                    1 => $"std::string({ctorArgs[0]})",
                    _ => "std::string()"
                };
                WriteLine($"{target} = {expr};");
                return;
            }

            // ::-qualified foreign C++ passthrough type (e.g. New std::string("hi"),
            // New std::vector(Of Integer)(), New my::ns::Foo(1, 2)). Foreign types are VALUES
            // (NOT std::shared_ptr / make_shared like collections and user classes). The type
            // name is emitted VERBATIM (keeping '::' — SanitizeName would strip it to
            // "stdstring") with generic args mapped ((Of Integer) -> <int32_t>) via MapType.
            //
            // The result temp cannot be pre-declared: a foreign type may be non-default-
            // constructible / non-assignable, and DeclareLocalsAndTemporaries deliberately
            // skips Foreign temps. So fold declaration + construction into one `auto` binding
            // (the temp is single-assignment and always initialized before any use).
            if (newObj.Type?.Kind == TypeKind.Foreign
                || (newObj.ClassName != null && newObj.ClassName.Contains("::")))
            {
                var foreignType = newObj.Type?.Kind == TypeKind.Foreign
                    ? MapType(newObj.Type)
                    : newObj.ClassName;
                var ctorArgs = string.Join(", ", newObj.Arguments.Select(a => GetValueName(a)));
                WriteLine($"auto {GetValueName(newObj)} = {foreignType}({ctorArgs});");
                return;
            }

            // Everyday collections are .NET REFERENCE types: construct via std::make_shared so
            // the resulting std::shared_ptr aliases when copied/passed/stored (matching .NET and
            // the C# backend). make_shared needs the BARE pointee type (BasicLang::List<int32_t>),
            // NOT MapType's shared_ptr-wrapped form — hence BareCollectionType.
            {
                var bareColl = BareCollectionType(newObj.Type);
                if (bareColl != null)
                {
                    var ctorArgs = string.Join(", ", newObj.Arguments.Select(a => GetValueName(a)));
                    WriteLine($"{GetValueName(newObj)} = std::make_shared<{bareColl}>({ctorArgs});");
                    return;
                }
            }

            // P1 native BCL types. The five value structs construct by value;
            // StringBuilder MUST go through make_shared — its
            // Append chain returns shared_from_this(), which throws std::bad_weak_ptr
            // (since C++17) when the object is not owned by a shared_ptr.
            if (IsNativeOwnedBclType(newObj.ClassName))
            {
                var bclArgs = string.Join(", ", newObj.Arguments.Select(a => GetValueName(a)));
                var bclResult = GetValueName(newObj);
                if (newObj.ClassName.Equals("StringBuilder", StringComparison.OrdinalIgnoreCase))
                    WriteLine($"{bclResult} = std::make_shared<BasicLang::StringBuilder>({bclArgs});");
                else
                    WriteLine($"{bclResult} = BasicLang::{BclCanonicalName(newObj.ClassName)}({bclArgs});");
                return;
            }

            // Result temps are pre-declared by DeclareLocalsAndTemporaries: assign, don't redeclare.
            // Generic instantiations need template arguments on the constructor: Pair<int32_t>(...)
            string bareName;
            if (newObj.Type?.GenericArguments != null && newObj.Type.GenericArguments.Count > 0)
            {
                var typeArgs = string.Join(", ", newObj.Type.GenericArguments.Select(MapType));
                bareName = $"{SanitizeName(newObj.ClassName)}<{typeArgs}>";
            }
            else
            {
                bareName = SanitizeName(newObj.ClassName);
            }

            var args = string.Join(", ", newObj.Arguments.Select(a => GetValueName(a)));
            var result = GetValueName(newObj);
            var isReferenceType = newObj.Type == null
                || newObj.Type.Kind == TypeKind.Class
                || newObj.Type.Kind == TypeKind.Interface;

            if (isReferenceType)
                WriteLine($"{result} = std::make_shared<{bareName}>({args});");
            else
                WriteLine($"{result} = {bareName}({args});");
        }

        public override void Visit(IRInstanceMethodCall methodCall)
        {
            // A foreign ::-qualified member call is opaque: the compiler can't know whether
            // it returns void or a value (its result type is a synthetic "T::member" Foreign
            // type, not a real C++ type). It is rendered inline by RenderForeignMethodCall.
            if (methodCall.Type?.Kind == TypeKind.Foreign)
            {
                // When this call's result is CONSUMED anywhere (an outer member access
                // like m.foo().bar(), a call argument like WriteLine(m.try_lock()), a
                // store, a condition, ...) it is rendered inline at that use site — so
                // don't ALSO emit it as a standalone statement, or the opaque call would
                // run twice. Emit the standalone `expr;` only when the result is discarded.
                if (_inlinedForeignCalls.Contains(methodCall))
                    return;
                WriteLine($"{RenderForeignMethodCall(methodCall)};");
                return;
            }

            var obj = GetValueName(methodCall.Object);

            // .NET-surface shim: ToString has no C++ counterpart — lower it by
            // receiver type (DateTime → runtime formatter, numbers → to_string).
            if (string.Equals(methodCall.MethodName, "ToString", StringComparison.OrdinalIgnoreCase))
            {
                var shim = EmitToStringShim(methodCall, obj);
                if (shim != null)
                {
                    WriteLine($"{GetValueName(methodCall)} = {shim};");
                    return;
                }
            }

            var op = MemberAccessOp(methodCall.Object);
            var methodName = SanitizeName(methodCall.MethodName);
            var args = string.Join(", ", methodCall.Arguments.Select(a => GetValueName(a)));
            if (methodCall.Type == null || methodCall.Type.Name == "Void")
                WriteLine($"{obj}{op}{methodName}({args});");
            else
                WriteLine($"{GetValueName(methodCall)} = {obj}{op}{methodName}({args});");
        }

        /// <summary>
        /// Render a foreign ::-qualified instance method call as an inline C++ expression
        /// (obj.method(args)). Chained calls collapse recursively: the receiver of an
        /// inlined inner foreign call is itself rendered inline, so m.foo().bar() emits
        /// as a single opaque expression rather than being split across temp statements.
        /// </summary>
        private string RenderForeignMethodCall(IRInstanceMethodCall methodCall)
        {
            var obj = methodCall.Object is IRInstanceMethodCall innerForeign
                      && innerForeign.Type?.Kind == TypeKind.Foreign
                ? RenderForeignMethodCall(innerForeign)
                : GetValueName(methodCall.Object);
            var op = MemberAccessOp(methodCall.Object);
            var methodName = SanitizeName(methodCall.MethodName);
            var args = string.Join(", ", methodCall.Arguments.Select(a => GetValueName(a)));
            return $"{obj}{op}{methodName}({args})";
        }

        /// <summary>
        /// Render a foreign ::-qualified data-member read as an inline C++ expression
        /// (obj.field). The receiver is rendered inline too, so a member read off a
        /// foreign call (m.foo().field) collapses into one opaque expression. Data-member
        /// reads are idempotent and side-effect-free, so inlining at each use site is safe
        /// even when the value is consumed more than once.
        /// </summary>
        private string RenderForeignFieldAccess(IRFieldAccess fieldAccess)
        {
            var obj = GetValueName(fieldAccess.Object);
            var op = MemberAccessOp(fieldAccess.Object);
            var field = SanitizeName(fieldAccess.FieldName);
            return $"{obj}{op}{field}";
        }

        /// <summary>
        /// Render a foreign ::-qualified free-function call as an inline C++ expression
        /// (ns::func(args)). The verbatim '::'-qualified FunctionName is emitted as-is —
        /// SanitizeName would strip the '::' to "nsfunc". Arguments render normally, so a
        /// nested foreign call/read argument collapses inline too. Like foreign instance calls,
        /// this is rendered at each use site rather than stored to an (undeclarable) foreign temp.
        /// </summary>
        private string RenderForeignFreeCall(IRCall call)
        {
            var args = string.Join(", ", call.Arguments.Select(GetValueName));
            return $"{call.FunctionName}({args})";
        }

        /// <summary>
        /// Lower `x.ToString(...)` by the receiver's static type. Returns null
        /// when the receiver isn't a known .NET-surface type (user classes may
        /// legitimately define their own ToString).
        /// </summary>
        private string EmitToStringShim(IRInstanceMethodCall methodCall, string obj)
        {
            // P1 native BCL receivers: the native types implement ToString(...) themselves —
            // emit a direct member call (`d.ToString(fmt)`, `sb->ToString()`). This replaced
            // the std::time_t `BasicLangRt::FormatTime` shim at the Task 10 flip.
            if (IsNativeOwnedBclType(methodCall.Object?.Type?.Name))
            {
                var bclOp = MemberAccessOp(methodCall.Object);
                return methodCall.Arguments.Count > 0
                    ? $"{obj}{bclOp}ToString({GetValueName(methodCall.Arguments[0])})"
                    : $"{obj}{bclOp}ToString()";
            }

            var receiverTypeName = methodCall.Object?.Type?.Name?.ToLowerInvariant();

            switch (receiverTypeName)
            {
                case "integer":
                case "long":
                case "short":
                case "byte":
                case "single":
                case "double":
                    return $"std::to_string({obj})";
                case "boolean":
                    return $"std::string({obj} ? \"True\" : \"False\")";
                case "string":
                    return obj;
                default:
                    return null;
            }
        }

        public override void Visit(IRBaseMethodCall baseCall)
        {
            var methodName = SanitizeName(baseCall.MethodName);
            var args = string.Join(", ", baseCall.Arguments.Select(a => GetValueName(a)));

            // Find the current class from the module to get base class name
            string baseClassName = "Base";
            if (_module != null && _currentFunction != null)
            {
                foreach (var irClass in _module.Classes.Values)
                {
                    if (irClass.Methods.Any(m => m.Implementation == _currentFunction) ||
                        irClass.Constructors.Any(c => c.Implementation == _currentFunction) ||
                        irClass.Properties.Any(p => p.Getter == _currentFunction || p.Setter == _currentFunction))
                    {
                        if (!string.IsNullOrEmpty(irClass.BaseClass))
                            baseClassName = SanitizeName(irClass.BaseClass);
                        break;
                    }
                }
            }

            // Generate base class method call
            if (baseCall.Type == null || baseCall.Type.Name == "Void")
            {
                WriteLine($"{baseClassName}::{methodName}({args});");
            }
            else
            {
                var result = GetValueName(baseCall);
                WriteLine($"{result} = {baseClassName}::{methodName}({args});");
            }
        }

        public override void Visit(IRFieldAccess fieldAccess)
        {
            // A foreign ::-qualified data-member read has a synthetic pseudo-type that is
            // not a real C++ type, so its result temp is never pre-declared
            // (DeclareLocalsAndTemporaries skips foreign temps). Like a foreign method call,
            // it is rendered inline at each use site (GetValueName -> RenderForeignFieldAccess);
            // emitting a standalone `t = c.field;` here would assign an undeclared temp. A
            // discarded read is a side-effect-free no-op, so suppressing the statement is safe.
            if (fieldAccess.Type?.Kind == TypeKind.Foreign)
                return;

            // Result temps are pre-declared by DeclareLocalsAndTemporaries: assign, don't redeclare.
            var result = GetValueName(fieldAccess);

            // §11.1 `<catchVar>.Message` (collected in InitializeFunctionContext, strictly
            // scoped to catch variables inside their own clause regions): NetException's
            // message IS its what(), and the per-clause std::exception/std::runtime_error
            // bindings carry the message the same way — one lowering serves both copies
            // of the (twice-emitted) catch body.
            if (_catchMessageAccesses.Contains(fieldAccess) && fieldAccess.Object is IRVariable catchVar)
            {
                WriteLine($"{result} = BasicLang::String({SanitizeName(catchVar.Name)}.what());");
                return;
            }

            // P1 static dispatch: a NativeOwned TYPE-NAME receiver (`DateTime.Now`,
            // `DateTime.MinValue`, `Decimal.One`, `TimeSpan.Zero`) with a surface
            // StaticProperty/StaticMethod lowers to the runtime's static member FUNCTION:
            // `BasicLang::X::Member()`. This is what `DateTime.Now` flows through since
            // the Task 10 flip removed the one-off IsDateTimeNowAccess/BasicLangRt::Now
            // shim. A declared local shadowing the type name wins.
            if (fieldAccess.Object is IRVariable staticRecv
                && IsNativeOwnedBclType(staticRecv.Name)
                && !_declaredIdentifiers.Contains(staticRecv.Name)
                && NativeBclSurface.TryGetMember(staticRecv.Name, fieldAccess.FieldName, out var staticMember)
                && (staticMember.Kind == NativeBclMemberKind.StaticProperty
                    || staticMember.Kind == NativeBclMemberKind.StaticMethod))
            {
                WriteLine($"{result} = BasicLang::{BclCanonicalName(staticRecv.Name)}::{staticMember.CppName ?? staticMember.MemberName}();");
                return;
            }

            // .NET-surface shim — the raw emission below would produce an
            // uncompilable `.Length` member access on std::string / std::vector.
            if (string.Equals(fieldAccess.FieldName, "Length", StringComparison.OrdinalIgnoreCase) &&
                fieldAccess.Object != null)
            {
                var receiverType = fieldAccess.Object.Type;
                // `<catchVar>.Message` results are std::string regardless of the
                // analyzer's inferred type (see _catchMessageAccesses), so
                // `ex.Message.Length` takes the String path too.
                if (string.Equals(receiverType?.Name, "String", StringComparison.OrdinalIgnoreCase)
                    || _catchMessageAccesses.Contains(fieldAccess.Object))
                {
                    WriteLine($"{result} = static_cast<int32_t>({GetValueName(fieldAccess.Object)}.length());");
                    return;
                }
                if (receiverType?.Kind == TypeKind.Array)
                {
                    WriteLine($"{result} = static_cast<int32_t>({GetValueName(fieldAccess.Object)}.size());");
                    return;
                }
            }

            // Collection property bridge: `.Count`/`.Keys`/`.Values` on a List/Dictionary/HashSet
            // arrive as an IRFieldAccess (no call syntax in the source), but the C++ wrappers
            // expose these as METHODS. Rewrite to a zero-arg call. The receiver is a shared_ptr
            // (reference semantics), so use the member-access op for it: `recv->Count()`.
            if (IsCollectionType(fieldAccess.Object?.Type)
                && (fieldAccess.FieldName is "Count" or "Keys" or "Values"))
            {
                var recv = GetValueName(fieldAccess.Object);
                var accessOp = MemberAccessOp(fieldAccess.Object);
                WriteLine($"{result} = {recv}{accessOp}{SanitizeName(fieldAccess.FieldName)}();");
                return;
            }

            // P1 property bridge: a property access
            // on a NativeOwned receiver (`d.Year`, `sb.Length`) arrives as an IRFieldAccess,
            // but the native runtime exposes these as METHODS — rewrite to a zero-arg call
            // via the surface table (`.Year()`; `sb->Length()`). CppName carries the two
            // pinned renames: DateTimeOffset's BL `DateTime` → ClockDateTime(), `Ticks` →
            // TicksValue() (a C++ member can't share its enclosing type's name).
            if (IsNativeOwnedBclType(fieldAccess.Object?.Type?.Name)
                && NativeBclSurface.TryGetMember(fieldAccess.Object.Type.Name, fieldAccess.FieldName, out var bclProp)
                && bclProp.Kind == NativeBclMemberKind.Property)
            {
                var recv = GetValueName(fieldAccess.Object);
                var accessOp = MemberAccessOp(fieldAccess.Object);
                WriteLine($"{result} = {recv}{accessOp}{bclProp.CppName ?? bclProp.MemberName}();");
                return;
            }

            var obj = GetValueName(fieldAccess.Object);
            var op = MemberAccessOp(fieldAccess.Object);
            var fieldName = SanitizeName(fieldAccess.FieldName);
            WriteLine($"{result} = {obj}{op}{fieldName};");
        }

        public override void Visit(IRFieldStore fieldStore)
        {
            var obj = GetValueName(fieldStore.Object);
            var op = MemberAccessOp(fieldStore.Object);
            var fieldName = SanitizeName(fieldStore.FieldName);
            var value = GetValueName(fieldStore.Value);
            WriteLine($"{obj}{op}{fieldName} = {value};");
        }

        public override void Visit(IRTupleElement tupleElement)
        {
            var tuple = GetValueName(tupleElement.Tuple);
            var type = MapType(tupleElement.Type);
            // C++ uses std::get<index>(tuple)
            WriteLine($"{type} {SanitizeName(tupleElement.Name)} = std::get<{tupleElement.Index}>({tuple});");
        }

        public override void Visit(IRTryCatch tryCatch)
        {
            // C++ exception handling with try-catch. The try/catch/finally BODIES are emitted as
            // full inline regions (not just their single entry block) so control flow inside a
            // body — a conditional Throw, a nested If/loop — actually branches inside the braces.
            // A body's normal exit branches to EndBlock; here that is a legal forward `goto
            // try_end;` (a jump out of the try to the function-scope continuation label), so no
            // loopEnd remap is needed (loopEnd: null).
            WriteLine("try");
            WriteLine("{");
            Indent();
            EmitInlineRegion(tryCatch.TryBlock, tryCatch.EndBlock, RegionEnd.GotoEnd);
            Unindent();
            WriteLine("}");

            // §11.1: a Try with at least one .NET-typed clause gets ONE leading
            // catch (const BasicLang::NetException&) holding an if/else-if ladder over
            // ALL clauses in source order (chain element matching), ending in a bare
            // throw; reached only when no clause matched. ORDER IS LOAD-BEARING: it
            // must precede the MapCatchType handlers AND the catch(...) finally pair —
            // NetException IS a std::runtime_error, so any later position would be
            // swallowed. A BL-thrown Throw New ArgumentException(...) is a plain
            // std::runtime_error: it skips this handler and lands in the per-clause
            // handlers below (the dual-shape behavior). Each clause body is emitted
            // TWICE (ladder arm + per-clause handler) and C++ labels are
            // function-scoped, so the ladder's copies carry the _nex label suffix —
            // the same trick the finally duplication plays with _fex/_fnorm.
            if (tryCatch.CatchClauses.Any(cc => CppExceptionTypes.IsNetException(cc.ExceptionType?.Name)))
            {
                WriteLine("catch (const BasicLang::NetException& __nex)");
                WriteLine("{");
                Indent();
                _regionLabelSuffix = "_nex";
                var firstArm = true;
                foreach (var catchClause in tryCatch.CatchClauses)
                {
                    string fqName;
                    var clauseTypeName = catchClause.ExceptionType?.Name;
                    if (clauseTypeName == null)
                    {
                        // An untyped Catch catches everything; every managed chain
                        // ends "System.Exception".
                        fqName = "System.Exception";
                    }
                    else if (!CppExceptionTypes.TryGetNetFullName(clauseTypeName, out fqName))
                    {
                        // A user-defined BL exception type can never arrive as a
                        // managed NetException — skipping its arm preserves the
                        // first-matching-clause-in-source-order semantics.
                        continue;
                    }
                    WriteLine($"{(firstArm ? "if" : "else if")} (__nex.Matches(\"{fqName}\"))");
                    firstArm = false;
                    WriteLine("{");
                    Indent();
                    var armVar = !string.IsNullOrEmpty(catchClause.VariableName)
                        ? SanitizeName(catchClause.VariableName)
                        : "ex";
                    WriteLine($"const BasicLang::NetException& {armVar} = __nex;");
                    EmitInlineRegion(catchClause.Block, tryCatch.EndBlock, RegionEnd.GotoEnd);
                    Unindent();
                    WriteLine("}");
                }
                _regionLabelSuffix = "";
                WriteLine("throw;");
                Unindent();
                WriteLine("}");
            }

            foreach (var catchClause in tryCatch.CatchClauses)
            {
                var exType = MapCatchType(catchClause.ExceptionType?.Name);
                var varName = !string.IsNullOrEmpty(catchClause.VariableName)
                    ? SanitizeName(catchClause.VariableName)
                    : "ex";
                WriteLine($"catch (const {exType}& {varName})");
                WriteLine("{");
                Indent();
                EmitInlineRegion(catchClause.Block, tryCatch.EndBlock, RegionEnd.GotoEnd);
                Unindent();
                WriteLine("}");
            }

            if (tryCatch.FinallyBlock != null)
            {
                // The finally body is emitted TWICE (exceptional + normal path). When it contains
                // control flow its region has interior labels; a distinct label suffix per copy
                // keeps the two emissions from redefining the same C++ label (C2045).
                // Known limitation: Return inside Try bypasses the finally body.
                WriteLine("catch (...)");
                WriteLine("{");
                Indent();
                WriteLine("{");
                Indent();
                _regionLabelSuffix = "_fex";
                EmitInlineRegion(tryCatch.FinallyBlock, tryCatch.EndBlock, RegionEnd.FallThrough);
                _regionLabelSuffix = "";
                Unindent();
                WriteLine("}");
                WriteLine("throw;");
                Unindent();
                WriteLine("}");

                // Normal path; braces scope the duplicated body against redeclarations.
                WriteLine("{");
                Indent();
                _regionLabelSuffix = "_fnorm";
                EmitInlineRegion(tryCatch.FinallyBlock, tryCatch.EndBlock, RegionEnd.FallThrough);
                _regionLabelSuffix = "";
                Unindent();
                WriteLine("}");
            }
        }

        private void EmitBlockInstructions(BasicBlock block)
        {
            if (block == null) return;
            foreach (var inst in block.Instructions)
            {
                // Directive ABOVE the branch skip: terminator lines (If/While conditions)
                // would otherwise lose their mapping.
                if (inst.SourceLine > 0) EmitLineDirective(inst.SourceLine, _currentFunction?.SourceFilePath);
                else EmitLineReset();
                if (inst is IRBranch or IRConditionalBranch) continue;
                inst.Accept(this);
            }
        }

        /// <summary>How a branch to the region's boundary (EndBlock) is emitted inside the
        /// enclosing native construct.</summary>
        private enum RegionEnd
        {
            /// A For Each range-for: end-of-iteration -&gt; <c>continue;</c> (the range-for owns
            /// iteration; a literal <c>goto end;</c> would break out of the whole loop).
            LoopContinue,
            /// A Try/Catch body: a normal forward <c>goto try_end;</c> out of the try to the
            /// function-scope continuation label (legal C++, and where control should land).
            GotoEnd,
            /// A Finally body: emit nothing — control falls out of the <c>{ }</c> scope, so the
            /// exceptional-path copy proceeds to its <c>throw;</c> and the normal-path copy falls
            /// through to the continuation. (A <c>goto</c> here would skip the rethrow.)
            FallThrough,
        }

        /// <summary>
        /// Emit a structured construct's inline body region (see <see cref="ComputeInlineRegion"/>)
        /// inside the native C++ construct's braces. Unlike <see cref="EmitBlockInstructions"/>
        /// (single block, terminators dropped), this emits EVERY block of the region — including
        /// the If/loop split blocks a nested control-flow statement produces — each with its label
        /// and its branch terminator, so inner control flow actually branches inside the braces
        /// instead of being stranded at function scope after the return.
        ///
        /// <paramref name="endMode"/> selects how a branch to <paramref name="endBlock"/> (the
        /// construct's EndBlock) is lowered for the enclosing native construct.
        /// </summary>
        private void EmitInlineRegion(BasicBlock entry, BasicBlock endBlock, RegionEnd endMode)
        {
            var region = ComputeInlineRegion(entry, endBlock);
            // Blocks a NESTED structured construct owns are emitted by that construct's own Visit
            // (inline, inside ITS braces); skip them here so they are not emitted twice.
            var nestedConsumed = new HashSet<BasicBlock>();
            foreach (var b in region)
                foreach (var inst in b.Instructions)
                {
                    if (inst is IRForEach fe)
                        AddRegion(nestedConsumed, ComputeInlineRegion(fe.BodyBlock, fe.EndBlock));
                    else if (inst is IRTryCatch tc)
                    {
                        AddRegion(nestedConsumed, ComputeInlineRegion(tc.TryBlock, tc.EndBlock));
                        foreach (var cc in tc.CatchClauses)
                            AddRegion(nestedConsumed, ComputeInlineRegion(cc.Block, tc.EndBlock));
                        AddRegion(nestedConsumed, ComputeInlineRegion(tc.FinallyBlock, tc.EndBlock));
                    }
                }

            foreach (var block in region)
            {
                if (block != entry && nestedConsumed.Contains(block)) continue;

                // Label every block except the region entry (nothing branches to the entry — it is
                // the first statement of the body). Interior blocks are goto targets and need one.
                if (block != entry)
                {
                    Unindent();
                    WriteLine($"{LabelName(block.Name)}: ;");
                    Indent();
                }

                EmitRegionInstructions(block, endBlock, endMode);
            }
        }

        /// <summary>
        /// Emit one region block's instructions, lowering a branch to <paramref name="endBlock"/>
        /// per <paramref name="endMode"/>. Interior branches (to other region blocks) fall through
        /// to the normal goto/if-goto emitted by <see cref="Visit(IRBranch)"/>/
        /// <see cref="Visit(IRConditionalBranch)"/>.
        /// </summary>
        private void EmitRegionInstructions(BasicBlock block, BasicBlock endBlock, RegionEnd endMode)
        {
            foreach (var inst in block.Instructions)
            {
                // Directive ABOVE the branch/end-mode switch, so terminator lines stay mapped.
                if (inst.SourceLine > 0) EmitLineDirective(inst.SourceLine, _currentFunction?.SourceFilePath);
                else EmitLineReset();
                switch (inst)
                {
                    case IRBranch br when br.Target == endBlock:
                        EmitRegionEnd(endBlock, endMode);
                        break;
                    case IRConditionalBranch cb when cb.TrueTarget == endBlock || cb.FalseTarget == endBlock:
                        EmitConditionalBranchToEnd(cb, endBlock, endMode);
                        break;
                    default:
                        inst.Accept(this);
                        break;
                }
            }
        }

        /// <summary>Lower a whole-block branch to the region's EndBlock per the enclosing construct.</summary>
        private void EmitRegionEnd(BasicBlock endBlock, RegionEnd endMode)
        {
            switch (endMode)
            {
                case RegionEnd.LoopContinue: WriteLine("continue;"); break;
                case RegionEnd.GotoEnd: WriteLine($"goto {EndLabelName(endBlock)};"); break;
                case RegionEnd.FallThrough: /* emit nothing — fall out of the { } scope */ break;
            }
        }

        /// <summary>
        /// The label of the construct's EndBlock as emitted at FUNCTION scope — always WITHOUT the
        /// finally-copy suffix, since the end block is written once, unsuffixed, by the top-level
        /// block walker. A goto out of a suffixed region must still target that single label.
        /// </summary>
        private static string EndLabelName(BasicBlock endBlock) => endBlock.Name.Replace('.', '_');

        /// <summary>
        /// A conditional branch where one arm is the region's EndBlock and the other an interior
        /// label. The end arm is lowered per <paramref name="endMode"/>; the interior arm is a goto.
        /// </summary>
        private void EmitConditionalBranchToEnd(IRConditionalBranch cb, BasicBlock endBlock, RegionEnd endMode)
        {
            var condition = GetValueName(cb.Condition);
            WriteLine($"if ({condition}) {{");
            Indent();
            EmitBranchArm(cb.TrueTarget, endBlock, endMode);
            Unindent();
            WriteLine("}");
            WriteLine("else {");
            Indent();
            EmitBranchArm(cb.FalseTarget, endBlock, endMode);
            Unindent();
            WriteLine("}");
        }

        private void EmitBranchArm(BasicBlock target, BasicBlock endBlock, RegionEnd endMode)
        {
            if (target == endBlock)
            {
                switch (endMode)
                {
                    case RegionEnd.LoopContinue: WriteLine("continue;"); break;
                    // Both GotoEnd and FallThrough branch an if/else arm to the function-scope end
                    // label. FallThrough has no single-statement fall-out form for one arm, so a
                    // goto to the (unsuffixed) end label is the correct exit. This one-arm end only
                    // arises for a Finally whose body itself has a conditional exit — a shape the
                    // finally-duplication design does not fully support; the goto keeps it valid.
                    case RegionEnd.GotoEnd:
                    case RegionEnd.FallThrough:
                        WriteLine($"goto {EndLabelName(endBlock)};");
                        break;
                }
            }
            else
            {
                WriteLine($"goto {LabelName(target.Name)};");
            }
        }

        /// <summary>Base Exception catches everything (std::exception); specific .NET
        /// exception types map to std::runtime_error (what our throws produce).</summary>
        private string MapCatchType(string netExceptionName)
        {
            if (netExceptionName == null || netExceptionName.Equals("Exception", StringComparison.OrdinalIgnoreCase))
                return "std::exception";
            return "std::runtime_error";
        }

        public override void Visit(IRThrow throwInst)
        {
            if (throwInst.Exception == null)
            {
                WriteLine("throw;");
                return;
            }

            // Throw New Exception("msg"): unwrap the message, throw std::runtime_error
            if (throwInst.Exception is IRNewObject newEx && CppExceptionTypes.IsNetException(newEx.ClassName))
            {
                var msg = newEx.Arguments.Count > 0 ? GetValueName(newEx.Arguments[0]) : "\"exception\"";
                WriteLine($"throw std::runtime_error({msg});");
                return;
            }

            WriteLine($"throw std::runtime_error({GetValueName(throwInst.Exception)});");
        }

        public override void Visit(IRInlineCode inlineCode)
        {
            if (inlineCode.Language.ToLower() == "cpp")
            {
                // Emit the C++ code directly
                WriteLine("// Inline C++ code");
                foreach (var line in inlineCode.Code.Split('\n'))
                {
                    WriteLine(line.TrimEnd());
                }
            }
            else
            {
                // For non-C++ inline code, emit a comment indicating it's not supported
                WriteLine($"// WARNING: Inline {inlineCode.Language} code not supported in C++ backend");
                WriteLine($"// Original code ({inlineCode.Code.Length} chars) was skipped");
            }
        }

        public override void Visit(IRForEach forEach)
        {
            // C++ range-based for loop
            var elemType = MapType(forEach.ElementType);
            var varName = SanitizeName(forEach.VariableName);
            var collection = GetValueName(forEach.Collection);

            // A BasicLang collection (List/Dictionary/HashSet, incl. a Keys()/Values() result
            // which is a List) is a std::shared_ptr — deref it so the range-for iterates the
            // pointee's begin()/end(). A plain array is iterated directly (not a shared_ptr).
            if (IsCollectionType(forEach.Collection?.Type))
                collection = $"(*{collection})";

            WriteLine($"for ({elemType} {varName} : {collection})");
            WriteLine("{");
            Indent();

            // Emit the ENTIRE body region inside the loop braces, not just BodyBlock. Any control
            // flow the body contains (an If, a nested loop, a Try) is split by the CFG into extra
            // blocks (if0.then/if0.end/...); those must be emitted here so their labels and gotos
            // live inside the loop. An end-of-iteration branch to EndBlock becomes `continue;`
            // (the range-for owns iteration), never a `goto` that would break the whole loop.
            EmitInlineRegion(forEach.BodyBlock, forEach.EndBlock, RegionEnd.LoopContinue);

            Unindent();
            WriteLine("}");
        }

        public override void Visit(IRIndexerAccess indexer)
        {
            var collection = GetValueName(indexer.Collection);
            var indices = string.Join("][", indexer.Indices.Select(i => GetValueName(i)));
            var result = GetValueName(indexer);
            var collType = indexer.Collection?.Type;

            // Dictionary read -> ->Get(k) (shared_ptr; throws on a missing key, .NET-faithful:
            //   `dict[missing]` throws KeyNotFoundException).
            // List read -> (*collection)[i] (deref the shared_ptr, then operator[]).
            // Array/other read -> collection[i] (plain array, not a shared_ptr — unchanged).
            if (collType?.Name is not null
                && string.Equals(collType.Name, "Dictionary", StringComparison.OrdinalIgnoreCase))
                WriteLine($"{result} = {collection}->Get({indices});");
            else if (collType?.Name is not null
                && string.Equals(collType.Name, "List", StringComparison.OrdinalIgnoreCase))
                WriteLine($"{result} = (*{collection})[{indices}];");
            else
                WriteLine($"{result} = {collection}[{indices}];");
        }

        public override void Visit(IRIndexerStore indexerStore)
        {
            var collection = GetValueName(indexerStore.Collection);
            var indices = string.Join("][", indexerStore.Indices.Select(i => GetValueName(i)));
            var value = GetValueName(indexerStore.Value);
            var collType = indexerStore.Collection?.Type;

            // Dictionary write -> ->Set(k, v) (shared_ptr; insert-or-update, matching .NET
            //   `dict[k] = v`).
            // List write -> (*collection)[i] = v (deref the shared_ptr, operator[] returns a
            //   mutable reference).
            // Array/other write -> collection[i] = v (plain array — unchanged).
            if (collType?.Name is not null
                && string.Equals(collType.Name, "Dictionary", StringComparison.OrdinalIgnoreCase))
                WriteLine($"{collection}->Set({indices}, {value});");
            else if (collType?.Name is not null
                && string.Equals(collType.Name, "List", StringComparison.OrdinalIgnoreCase))
                WriteLine($"(*{collection})[{indices}] = {value};");
            else
                WriteLine($"{collection}[{indices}] = {value};");
        }

        #endregion

        private string MapBinaryOperator(BinaryOpKind op) => op switch
        {
            BinaryOpKind.Add => "+",
            BinaryOpKind.Sub => "-",
            BinaryOpKind.Mul => "*",
            BinaryOpKind.Div => "/",
            BinaryOpKind.Mod => "%",
            BinaryOpKind.And => "&",
            BinaryOpKind.Or => "|",
            BinaryOpKind.Xor => "^",
            BinaryOpKind.Shl => "<<",
            BinaryOpKind.Shr => ">>",
            BinaryOpKind.Concat => "+",
            _ => "?"
        };
        
        private string MapUnaryOperator(UnaryOpKind op) => op switch
        {
            UnaryOpKind.Neg => "-",
            UnaryOpKind.Not => "!",
            UnaryOpKind.BitwiseNot => "~",
            UnaryOpKind.Inc => "++",
            UnaryOpKind.Dec => "--",
            _ => "?"
        };
        
        private string MapCompareOperator(CompareKind cmp) => cmp switch
        {
            CompareKind.Eq => "==",
            CompareKind.Ne => "!=",
            CompareKind.Lt => "<",
            CompareKind.Le => "<=",
            CompareKind.Gt => ">",
            CompareKind.Ge => ">=",
            _ => "?"
        };

        private bool IsNamedDestination(IRValue value)
        {
            if (value == null) return false;
            if (string.IsNullOrEmpty(value.Name)) return false;
            return _declaredIdentifiers.Contains(value.Name);
        }

        private string GetDefaultValue(TypeInfo type)
        {
            if (type == null) return "{}";

            var typeName = type.Name?.ToLower() ?? "";

            return typeName switch
            {
                "integer" or "int" => "0",
                "long" => "0LL",
                "single" or "float" => "0.0f",
                "double" => "0.0",
                "boolean" or "bool" => "false",
                "char" => "'\\0'",
                "string" => "\"\"",
                // Collections are std::shared_ptr (reference semantics): default to nullptr,
                // matching .NET (a `Dim l As List(Of Integer)` without New is null; a member
                // call on it throws, mirroring NullReferenceException — no null guard added).
                _ when IsCollectionType(type) => "nullptr",
                _ when type.Kind == TypeKind.Array => "{}",
                _ when type.Kind == TypeKind.Pointer => "nullptr",
                // P1 relies on this `{}` fallback: it zero-inits the five NativeOwned value
                // structs to exactly their spec §6.2 pinned defaults (DateTime.MinValue /
                // TimeSpan.Zero / Guid.Empty / 0D / DateTimeOffset MinValue+00:00) and gives
                // StringBuilder (a shared_ptr) null, matching .NET — no per-type entry needed.
                _ => "{}"
            };
        }

        protected override string EmitConstant(IRConstant constant)
        {
            if (constant.Value == null)
                return "nullptr";

            if (constant.Value is string str)
                return $"\"{EscapeString(str)}\"";

            if (constant.Value is char ch)
                return $"'{EscapeChar(ch)}'";

            if (constant.Value is bool b)
                return b ? "true" : "false";

            if (constant.Value is float f)
                return $"{f}f";

            if (constant.Value is long l)
                return $"{l}LL";

            // P1 Decimal literal: emit the exact .NET bit pattern
            // through the engine, never a lossy double literal. GetBits: [0..2] = 96-bit
            // magnitude (lo/mid/hi), [3] = flags (scale in bits 16-23, sign in bit 31).
            if (constant.Value is decimal dec)
            {
                var bits = decimal.GetBits(dec);
                var lo = unchecked((uint)bits[0]);
                var mid = unchecked((uint)bits[1]);
                var hi = unchecked((uint)bits[2]);
                var neg = (bits[3] & int.MinValue) != 0;
                var scale = (bits[3] >> 16) & 0xFF;
                // The engine canonicalizes -0 to +0 (documented divergence, unobservable
                // through ToString) — emit a signed zero as unsigned so FromParts round-trips.
                if (lo == 0 && mid == 0 && hi == 0)
                    neg = false;
                return $"BasicLang::Decimal::FromParts({lo}u, {mid}u, {hi}u, {(neg ? "true" : "false")}, {scale})";
            }

            return constant.Value.ToString();
        }

        protected new string EscapeString(string str)
        {
            return str.Replace("\\", "\\\\")
                     .Replace("\"", "\\\"")
                     .Replace("\n", "\\n")
                     .Replace("\r", "\\r")
                     .Replace("\t", "\\t");
        }

        protected new string EscapeChar(char ch)
        {
            if (ch == '\'') return "\\'";
            if (ch == '\\') return "\\\\";
            if (ch == '\n') return "\\n";
            if (ch == '\r') return "\\r";
            if (ch == '\t') return "\\t";
            return ch.ToString();
        }

        private void Write(string text) => _output.Append(text);

        private void EmitLineDirective(int sourceLine, string sourceFile)
        {
            if (!_options.EmitLineDirectives || _suppressLineDirectives) return;
            if (sourceLine <= 0 || string.IsNullOrEmpty(sourceFile)) return;
            // C++ #line filenames are ESCAPE-PROCESSED string literals — unlike C#'s
            // (CSharpBackend.EmitLineDirective takes the path verbatim). A raw Windows
            // path breaks the compile: "C:\Users\..." contains \U, a malformed
            // universal-character-name. Forward slashes are accepted by MSVC/clang/gcc.
            var normalized = sourceFile.Replace('\\', '/');
            if (sourceLine == _lastEmittedSourceLine &&
                string.Equals(normalized, _lastEmittedSourceFile, StringComparison.OrdinalIgnoreCase))
                return;
            _lastEmittedSourceLine = sourceLine;
            _lastEmittedSourceFile = normalized;
            // Column-0, unindented, bypassing WriteLine's indent — same rationale as the C# backend.
            _output.AppendLine($"#line {sourceLine} \"{normalized}\"");
        }

        // C++ has no `#line hidden`: optimizer-synthesized instructions (SourceLine == 0)
        // are re-pointed at the generated file ITSELF so the line table maps them to real
        // lines instead of smearing them onto the last user statement.
        private void EmitLineReset()
        {
            if (!_options.EmitLineDirectives || _suppressLineDirectives) return;
            if (_lastEmittedSourceFile == null) return;   // already in generated-file coordinates
            if (string.IsNullOrEmpty(_currentGeneratedFileName)) return;
            _lastEmittedSourceLine = -1;
            _lastEmittedSourceFile = null;
            // `#line N` numbers the line FOLLOWING the directive. The directive lands on
            // physical line (newlines-so-far + 1), so N = newlines + 2. O(buffer) scan,
            // bounded by mapped->synthesized transitions — Debug-only cost, measured fine.
            int newlines = 0;
            for (int i = 0; i < _output.Length; i++) if (_output[i] == '\n') newlines++;
            _output.AppendLine($"#line {newlines + 2} \"{_currentGeneratedFileName}\"");
        }

        /// <summary>
        /// Generate extern declaration for C library interop
        /// </summary>
        private void GenerateExternDeclaration(IRExternDeclaration externDecl)
        {
            // Skip if this is a platform-specific extern
            if (string.IsNullOrEmpty(externDecl.LibraryName) && externDecl.PlatformImplementations.Count > 0)
            {
                // This is a platform-specific extern, check for C++ implementation
                if (externDecl.PlatformImplementations.TryGetValue("Cpp", out var impl))
                {
                    WriteLine(impl);
                }
                return;
            }

            // Build the function declaration
            var returnType = externDecl.IsFunction
                ? MapType(externDecl.ReturnType)
                : "void";

            var paramList = new List<string>();
            foreach (var param in externDecl.Parameters)
            {
                var paramType = MapType(param.Type);
                var paramName = SanitizeName(param.Name);

                // Handle ByRef parameters
                if (param.IsByRef)
                {
                    paramList.Add($"{paramType}& {paramName}");
                }
                else
                {
                    paramList.Add($"{paramType} {paramName}");
                }
            }

            var methodName = SanitizeName(externDecl.Name);
            WriteLine($"{returnType} {methodName}({string.Join(", ", paramList)});");
        }

        private void WriteLine(string text = "")
        {
            if (!string.IsNullOrEmpty(text))
            {
                _output.Append(new string(' ', _indentLevel * _options.IndentSize));
                _output.AppendLine(text);
            }
            else
            {
                _output.AppendLine();
            }
        }
    }

    public class CppCodeGenOptions
    {
        public int IndentSize { get; set; } = 4;
        public bool GenerateComments { get; set; } = true;
        public bool GenerateMainFunction { get; set; } = true;
        public string Namespace { get; set; } = "BasicLang";
        /// <summary>Emit C++ #line directives mapping statements back to their .bas source
        /// (native source-level debugging). Default false: every existing constructor site
        /// stays byte-identical; the project builder opts in for Debug builds only.</summary>
        public bool EmitLineDirectives { get; set; } = false;
    }
}
