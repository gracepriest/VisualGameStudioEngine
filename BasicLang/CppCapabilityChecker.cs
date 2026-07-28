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
    /// <para>
    /// TWO passes, deliberately separate:
    /// </para>
    /// <list type="number">
    ///   <item><description>
    ///     <b>Type positions</b> (<see cref="CheckType"/>): every declared type in the
    ///     module — return types, parameters, locals, globals, class fields, pure
    ///     interface/abstract signatures — gated by <see cref="BoundaryTypeRegistry"/>.
    ///   </description></item>
    ///   <item><description>
    ///     <b>Member surface</b> (P1 spec §4.1, <c>CheckNativeBclSurfaceUse</c>): member
    ///     calls, property reads, <c>New</c> expressions and conversions involving the
    ///     NativeOwned BCL types, gated by <see cref="NativeBclSurface"/>. This pass also
    ///     reaches EXPRESSION-position uses of Rejected types, which pass 1 structurally
    ///     cannot see (no declared type slot exists for <c>New Regex("x")</c> as an
    ///     argument).
    ///   </description></item>
    /// </list>
    /// The type-position walk is hand-mirrored against ModuleTypeWalker.AllTypes — keep it
    /// in sync with ForeignFeatureChecker / CppCodeGenerator.ModuleUsesCollections.
    /// </summary>
    public class CppCapabilityChecker
    {
        private HashSet<string> _userDefinedNames;
        /// <summary>
        /// The current function's locals and parameters plus the module globals — the
        /// subset of CppCodeGenerator._declaredIdentifiers that can shadow a TYPE NAME in
        /// receiver position. (The generator's set is wider: it also carries class fields
        /// while emitting a member body and foreign <c>::</c> locals. Neither can appear as
        /// the bare IRVariable receiver of a static access, so they are irrelevant here.)
        /// A static member access arrives as an IRFieldAccess whose receiver is an
        /// IRVariable literally NAMED after the type ("DateTime"); a value of that name
        /// being in scope means the access is an ordinary instance access instead.
        /// </summary>
        private HashSet<string> _valueNamesInScope;
        /// <summary>
        /// Every function name the module defines. Used ONLY to mirror the generator's
        /// fallback for a dotted call whose prefix is a native BCL type name but whose
        /// member is not on the surface: CppCodeGenerator flattens such a call to a plain
        /// function name, which is legitimate when the module actually defines it.
        /// </summary>
        private HashSet<string> _moduleFunctionNames;

        /// <summary>
        /// The VB conversion intrinsics, keyed to the BL type each converts TO. They lower
        /// through CppCodeGenerator.EmitStdLibCall, NOT Visit(IRCast), so they need the
        /// same native-conversion gate applied on their own path (spec §6.2 + the Task 10
        /// Decimal-conversion rider) — keep this table in sync with that switch.
        /// </summary>
        private static readonly Dictionary<string, string> ConversionIntrinsicTargets =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["CInt"] = "Integer",
                ["CLng"] = "Long",
                ["CDbl"] = "Double",
                ["CSng"] = "Single",
                ["CStr"] = "String",
                ["CBool"] = "Boolean",
            };

        public List<string> Check(IRModule module)
        {
            var diags = new List<string>();

            _userDefinedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in module.Classes.Keys) _userDefinedNames.Add(name);
            foreach (var name in module.Interfaces.Keys) _userDefinedNames.Add(name);
            foreach (var name in module.Enums.Keys) _userDefinedNames.Add(name);
            foreach (var name in module.Delegates.Keys) _userDefinedNames.Add(name);

            // P1 Task 10 rider: a user-defined type that shadows one of the NativeOwned
            // BCL names is a hard conflict on the C++ backend. Before the flip these were
            // name-rejected as "unmapped .NET types"; after it, MapType would SILENTLY
            // remap the user's `Class Guid` to BasicLang::Guid (constructing the runtime
            // type, losing every user member). BasicLang has no namespaces to disambiguate
            // with, so the only honest answer is a rename request.
            foreach (var name in _userDefinedNames)
                if (BoundaryTypeRegistry.Categorize(name) == BoundaryTypeCategory.NativeOwned)
                    diags.Add($"'{name}' conflicts with a native BCL type on the C++ backend — rename the type");

            var globalNames = module.GlobalVariables?.Values.Select(g => g.Name) ?? Enumerable.Empty<string>();
            _moduleFunctionNames = new HashSet<string>(
                module.Functions.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);

            foreach (var func in module.Functions)
            {
                _valueNamesInScope = new HashSet<string>(globalNames, StringComparer.OrdinalIgnoreCase);
                foreach (var p in func.Parameters) _valueNamesInScope.Add(p.Name);
                foreach (var lv in func.LocalVariables) _valueNamesInScope.Add(lv.Name);

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
            // P1 spec §4.1: the member-surface pass. Runs on EVERY instruction kind
            // (including the ones with their own cases below) before the switch.
            CheckNativeBclSurfaceUse(inst, funcName, diags);

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
                    // Two independent C++ Select Case gaps, both rejected cleanly here (honesty
                    // matrix). (1) A '::'-qualified foreign C++ VALUE in the Select-Case expression,
                    // a Case constant, a range/comparison, or a When guard cannot be lowered
                    // (SanitizeName would strip the '::' to an undefined identifier) — reject it.
                    foreach (var op in IROperandWalker.EnumerateOperands(sw))
                        if (IROperandWalker.ForeignName(op) is string foreignName)
                            diags.Add($"a '::'-qualified foreign C++ value ('{foreignName}') in a " +
                                      $"Select Case / Case / When position (in '{funcName}') is not " +
                                      "supported on the C++ backend; foreign case values and guards " +
                                      "cannot be lowered — use an If/ElseIf chain instead");
                    // (2) The if/else-if goto lowering (CppCodeGenerator.Visit(IRSwitch)) handles
                    // constant/range/comparison/Or/Nothing patterns (+ When guards), but NOT type
                    // patterns (RTTI), tuple deconstruction, or variable bindings — reject those.
                    foreach (var pc in sw.PatternCases)
                        CheckSwitchPattern(pc, funcName, diags);
                    break;
            }
        }

        // ====================================================================
        // P1 member-surface pass (spec §4.1). NativeBclSurface is the single
        // source of truth for what the native BCL types implement; anything off
        // that table gets a clean BasicLang diagnostic here instead of dying as
        // a raw C++ compiler error at the end of the pipeline. The pass also
        // closes the PRE-EXISTING leak where a Rejected type used only in
        // EXPRESSION position (never in a declared type) slipped past CheckType.
        // ====================================================================

        /// <summary>
        /// True when <paramref name="typeName"/> is one of the P1 native BCL types AND
        /// is not shadowed by a user-defined type of the same name (that collision has
        /// its own diagnostic, emitted once in <see cref="Check"/> — the member pass
        /// must not pile a second, misleading "no native member" message on top).
        /// </summary>
        private bool IsNativeBcl(string typeName) =>
            typeName != null
            && BoundaryTypeRegistry.Categorize(typeName) == BoundaryTypeCategory.NativeOwned
            && !_userDefinedNames.Contains(typeName);

        private void CheckNativeBclSurfaceUse(IRInstruction inst, string funcName, List<string> diags)
        {
            switch (inst)
            {
                case IRInstanceMethodCall mc:
                    if (IsNativeBcl(mc.Object?.Type?.Name))
                        CheckSurfaceMember(mc.Object.Type.Name, mc.MethodName, mc.Arguments?.Count ?? 0,
                            isCallSyntax: true, funcName, diags);
                    break;

                case IRFieldAccess fa:
                    // Static form: the receiver is an IRVariable literally NAMED after the
                    // type ("DateTime.Now"), unless a value of that name is in scope.
                    if (fa.Object is IRVariable staticRecv
                        && IsNativeBcl(staticRecv.Name)
                        && !_valueNamesInScope.Contains(staticRecv.Name))
                    {
                        CheckSurfaceMember(staticRecv.Name, fa.FieldName, argCount: 0,
                            isCallSyntax: false, funcName, diags);
                    }
                    else if (IsNativeBcl(fa.Object?.Type?.Name))
                    {
                        // Instance form: only Property entries survive the codegen bridge
                        // (`d.Year` → `.Year()`); anything else emits a raw member access.
                        CheckSurfaceMember(fa.Object.Type.Name, fa.FieldName, argCount: 0,
                            isCallSyntax: false, funcName, diags);
                    }
                    break;

                case IRNewObject no:
                    var newCategory = BoundaryTypeRegistry.Categorize(no.ClassName);
                    if (newCategory == BoundaryTypeCategory.Rejected)
                    {
                        // LEAK CLOSURE (spec §4.1): `Console.WriteLine(New Regex("x"))` never
                        // declares a Regex-typed position, so CheckType never saw it and the
                        // construction reached codegen as an undefined C++ type.
                        diags.Add($".NET type '{no.ClassName}' (New expression in '{funcName}') — " +
                                  "no C++ mapping exists for this type");
                    }
                    else if (IsNativeBcl(no.ClassName))
                    {
                        CheckSurfaceCtor(no.ClassName, no.Arguments?.Count ?? 0, funcName, diags);
                    }
                    break;

                case IRCast cast:
                    CheckNativeConversion(cast.Value?.Type?.Name, cast.Type?.Name, funcName, diags);
                    break;

                case IRCall irCall when irCall.CalleeValue == null
                                        && !string.IsNullOrEmpty(irCall.FunctionName):
                    CheckNativeBclCall(irCall, funcName, diags);
                    break;
            }
        }

        /// <summary>
        /// The two IRCall shapes that reach native BCL types. Neither goes through
        /// <c>IRCast</c> or <c>IRInstanceMethodCall</c>, so without this arm both leaked
        /// past the checker and died as raw C++:
        /// <list type="number">
        ///   <item><description>
        ///     A dotted STATIC call (<c>DateTime.FromBinary(5)</c>): the generator's static
        ///     dispatch only fires for members on the surface, so an unknown one fell
        ///     through to a flattened phantom function (<c>DateTimeFromBinary(5)</c>).
        ///   </description></item>
        ///   <item><description>
        ///     A C* conversion INTRINSIC (<c>CStr(dt)</c> → <c>to_string(dt)</c>,
        ///     <c>CDbl(dt)</c> → <c>static_cast&lt;double&gt;(dt)</c>): the lowering's own
        ///     guard covers Decimal only, so the other five native types emitted a
        ///     std::to_string / static_cast against a struct with no such conversion.
        ///   </description></item>
        /// </list>
        /// </summary>
        private void CheckNativeBclCall(IRCall call, string funcName, List<string> diags)
        {
            var name = call.FunctionName;
            var argCount = call.Arguments?.Count ?? 0;

            var dot = name.LastIndexOf('.');
            if (dot > 0 && dot < name.Length - 1)
            {
                var typeName = name.Substring(0, dot);
                var memberName = name.Substring(dot + 1);
                if (IsNativeBcl(typeName))
                {
                    // Mirror the generator's fallback: when the member is NOT on the
                    // surface it flattens the call to a plain name, which is legitimate if
                    // the module really defines it (a user module that happens to share a
                    // native type's name). Only a genuinely undefined target is a leak.
                    if (!NativeBclSurface.TryGetMember(typeName, memberName, out _)
                        && (_moduleFunctionNames.Contains(name) || _moduleFunctionNames.Contains(memberName)))
                        return;

                    // CAVEAT: the generator DELIBERATELY accepts the parenthesized
                    // static-property form (`DateTime.Now()` → BasicLang::DateTime::Now()),
                    // so call syntax on a StaticProperty must NOT be rejected here.
                    CheckSurfaceMember(typeName, memberName, argCount,
                        isCallSyntax: true, funcName, diags, allowParenthesizedProperty: true);
                    return;
                }
            }

            if (argCount > 0 && ConversionIntrinsicTargets.TryGetValue(name, out var intrinsicTarget))
                CheckNativeConversion(call.Arguments[0]?.Type?.Name, intrinsicTarget, funcName, diags);
        }

        /// <summary>
        /// Validate one member use against <see cref="NativeBclSurface"/>. Unknown name,
        /// wrong member KIND for the syntax used, or an unsupported arity all produce a
        /// clean diagnostic naming the type and the member.
        /// </summary>
        private void CheckSurfaceMember(string typeName, string memberName, int argCount,
            bool isCallSyntax, string funcName, List<string> diags,
            bool allowParenthesizedProperty = false)
        {
            if (memberName == null) return;

            if (!NativeBclSurface.TryGetMember(typeName, memberName, out var member)
                || member.Kind == NativeBclMemberKind.Constructor)
            {
                diags.Add($"'{typeName}' has no native member '{memberName}' on the C++ backend " +
                          $"(in '{funcName}')");
                return;
            }

            // Paren-less access (IRFieldAccess) is only valid for the property kinds; call
            // syntax (IRInstanceMethodCall) only for the method kinds. A mismatch would
            // slip past the codegen bridges and emit an uncompilable member access.
            var isPropertyKind = member.Kind == NativeBclMemberKind.Property
                || member.Kind == NativeBclMemberKind.StaticProperty;

            if (isCallSyntax && isPropertyKind)
            {
                // The dotted static-call path passes allowParenthesizedProperty: the
                // generator deliberately lowers `DateTime.Now()` through the same static
                // dispatch as the paren-less form. Everywhere else, calling a property is
                // an error the codegen bridges cannot express.
                if (!allowParenthesizedProperty)
                {
                    diags.Add($"'{typeName}.{memberName}' is a property on the C++ backend's native " +
                              $"surface and cannot be used as a method call (in '{funcName}') — " +
                              "drop the parentheses");
                    return;
                }
                if (argCount != 0)
                {
                    diags.Add($"'{typeName}.{memberName}' is a property on the C++ backend's native " +
                              $"surface and takes no arguments, not {argCount} (in '{funcName}')");
                }
                return;
            }

            if (!isCallSyntax && !isPropertyKind)
            {
                diags.Add($"'{typeName}.{memberName}' is a method on the C++ backend's native " +
                          $"surface and cannot be used as a property (in '{funcName}') — " +
                          "call it with parentheses");
                return;
            }

            if (isPropertyKind) return;

            if (!AcceptedArities(member.ParamCounts).Contains(argCount))
            {
                diags.Add($"'{typeName}.{memberName}' on the C++ backend takes " +
                          string.Join(" or ", AcceptedArities(member.ParamCounts)) +
                          $" argument(s), not {argCount} (in '{funcName}')");
            }
        }

        /// <summary>
        /// The arities a surface row accepts. An EMPTY/null ParamCounts means "takes none"
        /// — the zero-arg methods share the NoParams sentinel with the property rows.
        /// </summary>
        private static int[] AcceptedArities(int[] paramCounts) =>
            paramCounts != null && paramCounts.Length > 0 ? paramCounts : new[] { 0 };

        /// <summary>Validate a <c>New</c> against the surface's ctor arities (spec §5).</summary>
        private void CheckSurfaceCtor(string typeName, int argCount, string funcName, List<string> diags)
        {
            if (!NativeBclSurface.TryGetMember(typeName, ".ctor", out var ctor))
            {
                diags.Add($"'{typeName}' has no native constructor on the C++ backend (in '{funcName}')");
                return;
            }
            if (!AcceptedArities(ctor.ParamCounts).Contains(argCount))
            {
                diags.Add($"'New {typeName}' on the C++ backend takes " +
                          string.Join(" or ", AcceptedArities(ctor.ParamCounts)) +
                          $" argument(s), not {argCount} (in '{funcName}')");
            }
        }

        private static bool IsDecimalName(string name) =>
            name != null && name.Equals("Decimal", StringComparison.OrdinalIgnoreCase);

        private static bool IsFloatingName(string name) =>
            name != null && (name.Equals("Double", StringComparison.OrdinalIgnoreCase)
                          || name.Equals("Single", StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Gate the conversion pairs the C++ backend actually lowers for the native BCL
        /// types (spec §6.2 + the Task 10 Decimal-conversion rider). Decimal has explicit
        /// lowerings in both directions; every OTHER NativeOwned conversion would emit a
        /// <c>static_cast</c> / <c>std::to_string</c> on a struct with no such conversion —
        /// invalid C++, which this design forbids reaching the C++ compiler.
        /// Shared by BOTH lowering routes: <c>CType</c> (IRCast) and the C* intrinsics
        /// (IRCall through EmitStdLibCall).
        /// </summary>
        private void CheckNativeConversion(string source, string target, string funcName, List<string> diags)
        {
            var targetNative = IsNativeBcl(target);
            var sourceNative = IsNativeBcl(source);
            if (!targetNative && !sourceNative) return;

            // Decimal ← Decimal / integral / Single / Double  (spec §10: integral is exact).
            if (IsDecimalName(target) && targetNative
                && (IsDecimalName(source) || CppCodeGenerator.IsIntegralTypeName(source) || IsFloatingName(source)))
                return;

            // Decimal → Decimal / Single / Double / integral / String / Boolean.
            if (IsDecimalName(source) && sourceNative
                && (IsDecimalName(target) || IsFloatingName(target) || CppCodeGenerator.IsIntegralTypeName(target)
                    || string.Equals(target, "String", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(target, "Boolean", StringComparison.OrdinalIgnoreCase)))
                return;

            // Identity on any native type (CType(d, DateTime) where d is already a DateTime).
            if (targetNative && sourceNative
                && string.Equals(target, source, StringComparison.OrdinalIgnoreCase))
                return;

            diags.Add($"converting '{source ?? "?"}' to '{target ?? "?"}' (in '{funcName}') is not " +
                      "supported on the C++ backend; the native BCL types define no such conversion");
        }

        /// <summary>
        /// Reject the Select Case pattern kinds the C++ backend cannot lower. Constant, range,
        /// comparison, Nothing and Or patterns (and When guards over them) lower fine; type,
        /// tuple and binding patterns do not. Recurses into Or alternatives so a nested
        /// unsupported alternative (e.g. <c>Case 1 Or Is String</c>) is caught too.
        /// </summary>
        private void CheckSwitchPattern(IRPatternCase pc, string funcName, List<string> diags)
        {
            switch (pc)
            {
                case IRTypePatternCase:
                    diags.Add($"Select Case with a type pattern (Case Is <Type> / Case x As <Type>) " +
                              $"(in '{funcName}') is not supported on the C++ backend; use constant, " +
                              "range (a To b), comparison (Is > n), or Or patterns");
                    break;
                case IRTuplePatternCase:
                    diags.Add($"Select Case with a tuple/deconstruction pattern (in '{funcName}') is not " +
                              "supported on the C++ backend; use constant, range, comparison, or Or patterns");
                    break;
                case IRBindingPatternCase:
                    diags.Add($"Select Case with a binding pattern (Case name When ...) (in '{funcName}') is " +
                              "not supported on the C++ backend; use a comparison (Case Is > 0) or a When " +
                              "guard over outer variables (Case value When <condition>)");
                    break;
                case IROrPatternCase or:
                    foreach (var alt in or.Alternatives)
                        CheckSwitchPattern(alt, funcName, diags);
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
            var category = BoundaryTypeRegistry.Categorize(name);
            if (string.IsNullOrEmpty(name) || category == BoundaryTypeCategory.Bridged) return;
            // P1 (spec §2/§6.2): the six native BCL types are real C++ implementations
            // (BasicLang::DateTime/TimeSpan/Guid/Decimal/DateTimeOffset + the shared_ptr
            // StringBuilder) and MapType lowers them BY CATEGORY. The registry flip alone
            // is NOT sufficient: without this branch a NativeOwned class-kind type falls
            // through to the unknown-class rejection at the bottom of this method, and a
            // primitive-kind one silently miscompiles. A user-defined type SHADOWING one
            // of these names is caught by the conflict diagnostic in Check().
            if (category == BoundaryTypeCategory.NativeOwned) return;
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
            if (category == BoundaryTypeCategory.Rejected)
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
