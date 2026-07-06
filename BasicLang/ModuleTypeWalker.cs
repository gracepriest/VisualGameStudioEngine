using System.Collections.Generic;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;

namespace BasicLang.Compiler.CodeGen
{
    /// <summary>
    /// Single source of truth for "every type-bearing position in an IRModule".
    /// Yields the top-level <see cref="TypeInfo"/> declared at each such position;
    /// callers recurse into <c>GenericArguments</c>/<c>ElementType</c> themselves.
    ///
    /// Extracted so the backend guards/detectors — ForeignFeatureChecker,
    /// CppCapabilityChecker, and CppCodeGenerator.ModuleUsesCollections — all
    /// traverse the SAME set of positions. Previously each hand-rolled its own walk
    /// and every one of them silently missed <c>module.GlobalVariables</c>, so a
    /// file-scope <c>Dim g As List(Of Integer)</c> / <c>Dim g As std::mutex</c>
    /// bypassed the honesty guard. Keep every consumer wired through here so that
    /// class of gap cannot recur.
    ///
    /// Positions covered: function return/parameter/local types; module global
    /// variable types; class field/property types, method return+parameter types,
    /// constructor parameter types; interface method return+parameter types and
    /// property types. (Events carry only a delegate type NAME, not a TypeInfo.)
    /// </summary>
    public static class ModuleTypeWalker
    {
        public static IEnumerable<TypeInfo> AllTypes(IRModule module)
        {
            if (module == null) yield break;

            // Functions: return, parameters, locals.
            if (module.Functions != null)
            {
                foreach (var func in module.Functions)
                {
                    if (func == null) continue;
                    yield return func.ReturnType;
                    if (func.Parameters != null)
                        foreach (var p in func.Parameters)
                            yield return p?.Type;
                    if (func.LocalVariables != null)
                        foreach (var lv in func.LocalVariables)
                            yield return lv?.Type;
                }
            }

            // Module-scope globals (the position every hand-rolled walk missed).
            if (module.GlobalVariables != null)
            {
                foreach (var gv in module.GlobalVariables.Values)
                    yield return gv?.Type;
            }

            // Classes: fields, properties, method signatures, constructor parameters.
            if (module.Classes != null)
            {
                foreach (var cls in module.Classes.Values)
                {
                    if (cls == null) continue;
                    if (cls.Fields != null)
                        foreach (var fld in cls.Fields)
                            yield return fld?.Type;
                    if (cls.Properties != null)
                        foreach (var prop in cls.Properties)
                            yield return prop?.Type;
                    if (cls.Methods != null)
                    {
                        foreach (var m in cls.Methods)
                        {
                            if (m == null) continue;
                            yield return m.ReturnType;
                            if (m.Parameters != null)
                                foreach (var p in m.Parameters)
                                    yield return p?.Type;
                        }
                    }
                    if (cls.Constructors != null)
                    {
                        foreach (var ctor in cls.Constructors)
                        {
                            if (ctor?.Parameters == null) continue;
                            foreach (var p in ctor.Parameters)
                                yield return p?.Type;
                        }
                    }
                }
            }

            // Interfaces: method signatures and property types.
            if (module.Interfaces != null)
            {
                foreach (var iface in module.Interfaces.Values)
                {
                    if (iface == null) continue;
                    if (iface.Methods != null)
                    {
                        foreach (var m in iface.Methods)
                        {
                            if (m == null) continue;
                            yield return m.ReturnType;
                            if (m.Parameters != null)
                                foreach (var p in m.Parameters)
                                    yield return p?.Type;
                        }
                    }
                    if (iface.Properties != null)
                        foreach (var prop in iface.Properties)
                            yield return prop?.Type;
                }
            }
        }
    }
}
