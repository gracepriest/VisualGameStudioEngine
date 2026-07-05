using System;
using System.Collections.Generic;
using System.Linq;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;

namespace BasicLang.Compiler.CodeGen.CPlusPlus
{
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
        // Types CppTypeMapper can map (keep in sync with CppTypeMapper.InitializeTypeMappings)
        private static readonly HashSet<string> MappedTypeNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Integer", "Long", "Single", "Double", "String", "Boolean", "Char", "Void",
            "Byte", "Short", "UByte", "UShort", "UInteger", "ULong"
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
                if (func.IsAsync)
                    diags.Add($"Async function '{func.Name}' — Async/Await is not yet supported by the C++ backend");
                if (func.IsIterator)
                    diags.Add($"Iterator function '{func.Name}' — Yield is not yet supported by the C++ backend");
                if (func.IsLambda)
                    diags.Add($"Lambda '{func.Name}' — lambdas are not yet supported by the C++ backend");
                if (func.GenericParameters != null && func.GenericParameters.Count > 0)
                    diags.Add($"Function '{func.Name}' — generic functions are not yet supported by the C++ backend");

                CheckType(func.ReturnType, $"return type of '{func.Name}'", diags);
                foreach (var p in func.Parameters)
                    CheckType(p.Type, $"parameter '{p.Name}' of '{func.Name}'", diags);
                foreach (var lv in func.LocalVariables)
                    CheckType(lv.Type, $"local '{lv.Name}' in '{func.Name}'", diags);

                foreach (var block in func.Blocks)
                    foreach (var inst in block.Instructions)
                        CheckInstruction(inst, func.Name, diags);
            }

            foreach (var irClass in module.Classes.Values)
            {
                if (irClass.GenericParameters != null && irClass.GenericParameters.Count > 0)
                    diags.Add($"Class '{irClass.Name}' — generic classes are not yet supported by the C++ backend");
            }

            return diags.Distinct().ToList();
        }

        private void CheckInstruction(IRInstruction inst, string funcName, List<string> diags)
        {
            switch (inst)
            {
                case IRTryCatch tc:
                    if (tc.FinallyBlock != null)
                        diags.Add($"Try/Finally in '{funcName}' — Finally blocks are not yet supported by the C++ backend");
                    if (tc.TryBlock != null)
                        foreach (var i in tc.TryBlock.Instructions) CheckInstruction(i, funcName, diags);
                    foreach (var cc in tc.CatchClauses)
                        if (cc.Block != null)
                            foreach (var i in cc.Block.Instructions) CheckInstruction(i, funcName, diags);
                    if (tc.FinallyBlock != null)
                        foreach (var i in tc.FinallyBlock.Instructions) CheckInstruction(i, funcName, diags);
                    break;
                case IRAwait:
                    diags.Add($"Await in '{funcName}' — Async/Await is not yet supported by the C++ backend");
                    break;
                case IRYield:
                    diags.Add($"Yield in '{funcName}' — Yield is not yet supported by the C++ backend");
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

            if (name.Equals("Object", StringComparison.OrdinalIgnoreCase))
            {
                diags.Add($"'Object' ({where}) — 'Object' has no C++ mapping");
                return;
            }

            if (type.Kind == TypeKind.Class || type.Kind == TypeKind.Interface || type.Kind == TypeKind.Structure)
            {
                if (!_userDefinedNames.Contains(name))
                    diags.Add($".NET type '{name}' ({where}) — no C++ mapping exists for this type");
            }
        }
    }
}
