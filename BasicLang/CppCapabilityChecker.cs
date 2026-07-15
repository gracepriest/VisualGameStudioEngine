using System;
using System.Collections.Generic;
using System.Linq;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;

namespace BasicLang.Compiler.CodeGen.CPlusPlus
{
    /// <summary>
    /// .NET exception type names the C++ backend maps onto std::runtime_error /
    /// std::exception. Shared by the capability checker and the code generator.
    /// </summary>
    public static class CppExceptionTypes
    {
        private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
        {
            "Exception", "SystemException", "ApplicationException", "ArgumentException",
            "ArgumentNullException", "InvalidOperationException", "NotImplementedException",
            "NullReferenceException", "IndexOutOfRangeException", "FormatException",
            "OverflowException", "DivideByZeroException"
        };

        public static bool IsNetException(string name) => name != null && Names.Contains(name);
    }

    /// <summary>
    /// Thrown when an IRModule uses features the C++ backend cannot lower to correct C++.
    /// </summary>
    public class CppCapabilityException : Exception
    {
        public IReadOnlyList<string> Diagnostics { get; }

        public CppCapabilityException(List<string> diagnostics)
            : base("C++ backend: unsupported feature(s):\n  " + string.Join("\n  ", diagnostics))
        {
            Diagnostics = diagnostics;
        }
    }

    /// <summary>
    /// Walks an IRModule and reports constructs the C++ backend cannot emit correct code for.
    /// Feature checks (async/yield/finally/lambda/generics) are deleted as each feature lands
    /// (see docs/superpowers/plans/2026-07-05-cpp-backend-overhaul.md tasks 2-6).
    /// The unmapped-.NET-type and Object checks are permanent until a .NET-surface design exists.
    /// </summary>
    public class CppCapabilityChecker
    {
        // Primitives the C++ backend can lower. INVARIANT: this must be exactly the key
        // set of CppTypeMapper._typeMap (TypeMapper.cs InitializeTypeMappings), MINUS
        // 'Object' — which IS a _typeMap key (mapped to void*) but is deliberately
        // rejected below because void* erasure is unsound. Any name NOT in this set,
        // not a supported generic (List/Dictionary/HashSet/Task/IEnumerable/Func/Action),
        // and not a '::' foreign type is rejected (via the class-kind check or the
        // UnmappedNetTypes list). Note: SByte and Decimal are NOT mapped by CppTypeMapper,
        // so they must NOT appear here — they are rejected via UnmappedNetTypes.
        private static readonly HashSet<string> MappedTypeNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Integer", "Long", "Single", "Double", "String", "Boolean", "Char", "Void",
            "Byte", "Short", "UByte", "UShort", "UInteger", "ULong"
        };

        // .NET types the C++ backend has NO mapping for. Rejected regardless of TypeKind —
        // the semantic analyzer sometimes leaves these as Kind.Primitive in signature
        // positions (e.g. an interface method's `As DateTime` return), where the class-kind
        // gate below would otherwise let them slip past. Listing them explicitly (like
        // 'Object') keeps the rejection Kind-independent with zero false-positive risk:
        // none of these has any valid C++ lowering. Decimal and SByte are here because
        // CppTypeMapper does not map them — without a clean rejection, `Dim x As Decimal`
        // would emit a bare, undefined C++ type `Decimal` (silent miscompile).
        private static readonly HashSet<string> UnmappedNetTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "Decimal", "SByte",
            "DateTime", "DateTimeOffset", "TimeSpan", "Guid", "StringBuilder", "Regex",
            "Uri", "Stream", "FileInfo", "DirectoryInfo"
        };

        private HashSet<string> _userDefinedNames;

        public List<string> Check(IRModule module)
        {
            var diags = new List<string>();

            _userDefinedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in module.Classes.Keys) _userDefinedNames.Add(name);
            foreach (var name in module.Interfaces.Keys) _userDefinedNames.Add(name);
            foreach (var name in module.Enums.Keys) _userDefinedNames.Add(name);
            foreach (var name in module.Delegates.Keys) _userDefinedNames.Add(name);

            foreach (var func in module.Functions)
            {
                // Iterator return types (IEnumerable(Of T)) lower to BasicLang::Generator<T>
                if (!func.IsIterator)
                    CheckType(func.ReturnType, $"return type of '{func.Name}'", diags);
                foreach (var p in func.Parameters)
                    CheckType(p.Type, $"parameter '{p.Name}' of '{func.Name}'", diags);
                foreach (var lv in func.LocalVariables)
                    CheckType(lv.Type, $"local '{lv.Name}' in '{func.Name}'", diags);

                foreach (var block in func.Blocks)
                    foreach (var inst in block.Instructions)
                        CheckInstruction(inst, func.Name, diags);
            }

            // Positions beyond module.Functions. The pre-existing Functions loop
            // already covers everything lowered into a function body — property
            // getters/setters, method impls, and ctor impls all become IRFunctions —
            // so the remaining gaps are the type positions that DON'T lower to a
            // function: module globals, class fields, and pure signatures (interface
            // members + abstract class methods with no impl). Walking these directly
            // (keeping per-position labels) covers the same set of positions as
            // ModuleTypeWalker.AllTypes — keep in sync with ForeignFeatureChecker /
            // CppCodeGenerator.ModuleUsesCollections.

            // Module-scope globals: a file-scope `Dim g As DateTime` (or any unmapped
            // .NET type) must be rejected just like a function local would be.
            if (module.GlobalVariables != null)
                foreach (var gv in module.GlobalVariables.Values)
                    CheckType(gv.Type, $"global '{gv.Name}'", diags);

            if (module.Classes != null)
                foreach (var cls in module.Classes.Values)
                {
                    // Class field types.
                    if (cls.Fields != null)
                        foreach (var fld in cls.Fields)
                            CheckType(fld.Type, $"field '{fld.Name}' of '{cls.Name}'", diags);

                    // Abstract method SIGNATURES with no impl function — these never
                    // appear in module.Functions, so the Functions loop misses them.
                    // (Concrete methods lower to an IRFunction and are covered there.)
                    if (cls.Methods != null)
                        foreach (var m in cls.Methods)
                        {
                            if (m.Implementation != null) continue;
                            CheckType(m.ReturnType, $"return type of '{cls.Name}.{m.Name}'", diags);
                            if (m.Parameters != null)
                                foreach (var p in m.Parameters)
                                    CheckType(p.Type, $"parameter '{p.Name}' of '{cls.Name}.{m.Name}'", diags);
                        }
                }

            // Pure interface signatures: method return/parameter types and property
            // types. An interface carries no impl body, so nothing lowers to a
            // function; without this walk a `Function Foo() As DateTime` on an
            // interface degrades to a raw C++ compiler error instead of a clean
            // BasicLang diagnostic.
            if (module.Interfaces != null)
                foreach (var iface in module.Interfaces.Values)
                {
                    if (iface.Methods != null)
                        foreach (var m in iface.Methods)
                        {
                            CheckType(m.ReturnType, $"interface method '{iface.Name}.{m.Name}' return", diags);
                            if (m.Parameters != null)
                                foreach (var p in m.Parameters)
                                    CheckType(p.Type, $"parameter '{p.Name}' of interface method '{iface.Name}.{m.Name}'", diags);
                        }
                    if (iface.Properties != null)
                        foreach (var prop in iface.Properties)
                            CheckType(prop.Type, $"interface property '{iface.Name}.{prop.Name}'", diags);
                }

            return diags.Distinct().ToList();
        }

        private void CheckInstruction(IRInstruction inst, string funcName, List<string> diags)
        {
            switch (inst)
            {
                case IRTryCatch tc:
                    if (tc.TryBlock != null)
                        foreach (var i in tc.TryBlock.Instructions) CheckInstruction(i, funcName, diags);
                    foreach (var cc in tc.CatchClauses)
                        if (cc.Block != null)
                            foreach (var i in cc.Block.Instructions) CheckInstruction(i, funcName, diags);
                    if (tc.FinallyBlock != null)
                        foreach (var i in tc.FinallyBlock.Instructions) CheckInstruction(i, funcName, diags);
                    break;

                case IRForEach fe:
                    // Direct `For Each ... In someDictionary` is a v1 non-goal: BasicLang::Dictionary
                    // has no begin()/end(), so the generated range-for (`for (... : (*dict))`) does
                    // not compile (cl C3312). Reject it cleanly here with an actionable message
                    // instead of emitting broken C++. Iterating `.Keys`/`.Values` (which lower to a
                    // BasicLang::List) or a List/HashSet still works — those carry a non-Dictionary
                    // collection type and pass this gate.
                    if (fe.Collection?.Type?.Name is string collName
                        && collName.Equals("Dictionary", StringComparison.OrdinalIgnoreCase))
                    {
                        diags.Add($"For Each over a Dictionary (in '{funcName}') is not supported; " +
                                  "iterate .Keys or .Values instead");
                    }
                    break;

                case IRSwitch sw:
                    // The C++ switch lowering (CppCodeGenerator.Visit(IRSwitch)) emits only
                    // integral `case` labels + gotos and drops PATTERN cases entirely — it cannot
                    // carry a '::'-qualified foreign C++ VALUE in a Select-Case expression, a Case
                    // constant, a range/comparison, or a When guard. Left alone these silently
                    // MISCOMPILE (the case is dropped, or SanitizeName strips the '::' to an
                    // undefined identifier). Reject any foreign value in a switch position with a
                    // clean capability diagnostic, honoring the honesty matrix on the C++ backend
                    // too. (Non-foreign pattern Select Case is a separate pre-existing gap.)
                    foreach (var op in IROperandWalker.EnumerateOperands(sw))
                        if (IROperandWalker.ForeignName(op) is string foreignName)
                            diags.Add($"a '::'-qualified foreign C++ value ('{foreignName}') in a " +
                                      $"Select Case / Case / When position (in '{funcName}') is not " +
                                      "supported on the C++ backend; foreign case values and guards " +
                                      "cannot be lowered — use an If/ElseIf chain instead");
                    break;
            }
        }

        /// <summary>
        /// PERMANENT check: class-kind types must be user-defined in this module or have a
        /// known C++ mapping; everything else (List, Dictionary, DateTime, Object, ...) is an
        /// error until a .NET-surface design exists for the C++ backend.
        /// </summary>
        private void CheckType(TypeInfo type, string where, List<string> diags)
        {
            if (type == null) return;

            if (type.Kind == TypeKind.Array)
            {
                CheckType(type.ElementType, where, diags);
                return;
            }

            // Bare type parameters are covered by the generics diagnostics above
            if (type.Kind == TypeKind.TypeParameter) return;

            if (type.GenericArguments != null)
                foreach (var ga in type.GenericArguments)
                    CheckType(ga, where, diags);

            var name = type.Name;
            if (string.IsNullOrEmpty(name) || MappedTypeNames.Contains(name)) return;
            if (CppExceptionTypes.IsNetException(name)) return; // mapped to std::runtime_error
            if (name.Equals("Task", StringComparison.OrdinalIgnoreCase)) return; // BasicLang::Task<T>
            if (name.Equals("IEnumerable", StringComparison.OrdinalIgnoreCase)
                && type.GenericArguments != null && type.GenericArguments.Count > 0) return; // BasicLang::Generator<T>
            if (name.Equals("Func", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Action", StringComparison.OrdinalIgnoreCase)) return; // std::function

            if (name.Equals("Object", StringComparison.OrdinalIgnoreCase))
            {
                diags.Add($"'Object' ({where}) — 'Object' has no C++ mapping");
                return;
            }

            // Known unmapped .NET types are rejected regardless of Kind (the analyzer
            // may leave them as Kind.Primitive in signature positions).
            if (UnmappedNetTypes.Contains(name))
            {
                diags.Add($".NET type '{name}' ({where}) — no C++ mapping exists for this type");
                return;
            }

            if (name.Equals("List", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Dictionary", StringComparison.OrdinalIgnoreCase)
                || name.Equals("HashSet", StringComparison.OrdinalIgnoreCase))
                return; // BasicLang::List/Dictionary/HashSet wrappers; generic args already recursed above
            if (name.Contains("::"))
                return; // ::-qualified C++ foreign type (opaque passthrough)

            if (type.Kind == TypeKind.Class || type.Kind == TypeKind.Interface || type.Kind == TypeKind.Structure)
            {
                if (!_userDefinedNames.Contains(name))
                    diags.Add($".NET type '{name}' ({where}) — no C++ mapping exists for this type");
            }
        }
    }
}
