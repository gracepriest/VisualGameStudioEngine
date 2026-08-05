using System;
using System.Collections.Generic;
using System.Linq;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;

namespace BasicLang.Compiler.CodeGen.JavaScript
{
    /// <summary>
    /// Refuses BasicLang features that cannot lower cleanly to JavaScript.
    ///
    /// <para><b>The governing rule:</b> a feature with a natural JS equivalent is emitted;
    /// a feature that would need emulation is REJECTED with a BL70xx code. That posture is
    /// the point of this backend. The features on the reject list are almost exactly the
    /// open C++ backend bug list — ByRef silently dropped, value semantics diverging — and
    /// every one of those shipped as a build that succeeded and then did the wrong thing at
    /// runtime. A build-time refusal is strictly better than a half implementation, and a
    /// rejection cannot be retrofitted credibly once a half implementation exists.</para>
    ///
    /// <para><b>Why this runs on the IR rather than the AST.</b> The spec proposed checking
    /// after semantic analysis and before IR construction, for the source positions AST
    /// nodes carry. Measurement (recorded in the plan's Phase 1 recon) showed every rejected
    /// feature except overloading survives IR construction intact, so the IR is sufficient —
    /// and calling from <c>Generate</c> puts the check on a seam no entry point can bypass.
    /// A separate pre-IR pass would be one more call site each of the CLI, the IDE and the
    /// LSP must remember to make, which is this repo's most common failure mode.</para>
    ///
    /// <para><b>Diagnostics name the offender; they do not cite a line.</b> Declaration
    /// <c>SourceLine</c> is 0 throughout the IR — it exists only on <c>IRInstruction</c>,
    /// and <c>IRFunction</c>/<c>IRClass</c>/<c>IRParameter</c> do not carry it at all. So a
    /// message says <i>"ByRef parameter 'x' in Sub 'Bump'"</i> rather than <i>file(4,12)</i>.
    /// Statement positions ARE populated, which is what source-map emission uses.</para>
    ///
    /// <para><b>Method overloading is deliberately absent from the reject list.</b> BasicLang
    /// has none — the SemanticAnalyzer already errors with "Subroutine 'F' is already
    /// defined in this scope" for both free subs and class methods. A BL7001 arm could never
    /// fire. Note also that duplicate names in <c>module.Functions</c> are LEGAL: two classes
    /// with a method of the same name both flatten to unqualified <c>IRFunction</c>s, so
    /// "duplicate name implies overloading" is a false positive waiting to happen.</para>
    /// </summary>
    public static class JsCapabilityChecker
    {
        /// <summary>
        /// Walks <paramref name="module"/> and throws <see cref="ForeignFeatureException"/>
        /// on the first construct the JavaScript backend refuses to lower.
        /// </summary>
        /// <remarks>
        /// Arms land one per task, each independently revertable:
        /// BL7002 ByRef, BL7003 Long, BL7004 Char, BL7005 value Structure,
        /// BL7006 operator overloading, BL7007 .NET BCL types.
        /// </remarks>
        public static void Check(IRModule module)
        {
            if (module == null) return;

            CheckByRef(module);
            CheckBannedTypes(module);
        }

        /// <summary>
        /// Types with no JavaScript equivalent, keyed by BasicLang type name.
        /// <c>Long</c> because a JS number is a double — exact only to 2^53 — and BigInt,
        /// the only precise alternative, contaminates every expression it touches.
        /// <c>Char</c> because JavaScript has no character type at all.
        /// </summary>
        private static readonly Dictionary<string, (string Code, string Why)> BannedTypes =
            new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
            {
                ["Long"] = ("BL7003",
                    "a JavaScript number is a double and is exact only to 2^53, so Long values " +
                    "silently lose precision. BigInt would be exact but contaminates every " +
                    "arithmetic expression it touches. Use Integer."),
                ["Char"] = ("BL7004",
                    "JavaScript has no character type. Use String."),
            };

        /// <summary>
        /// BL7003 / BL7004 — banned types in any declared position.
        ///
        /// <para><b>Two walks, deliberately.</b> The first is descriptive: it names the
        /// position ("local 'n' in Sub 'Main'") so the error is actionable, since declaration
        /// SourceLine is 0 and there is no line number to give. The second is
        /// <see cref="ModuleTypeWalker"/>, the shared maintained enumeration of declared type
        /// positions — used as a BACKSTOP. If it ever finds a banned type the descriptive
        /// walk missed, the two have drifted and we refuse anyway with a vaguer message.
        /// Refusing vaguely beats emitting silently-wrong JavaScript, and the vague message
        /// is itself the signal that the descriptive walk needs a new arm.</para>
        ///
        /// <para>⛔ <see cref="ModuleTypeWalker"/> yields only TOP-LEVEL TypeInfos and leaves
        /// recursion to callers, so <c>List(Of Long)</c> and <c>Long()</c> are invisible
        /// without <see cref="CheckTypeTree"/>. It also never visits delegates, extern
        /// declarations, or enum underlying types — those are walked explicitly below.</para>
        /// </summary>
        private static void CheckBannedTypes(IRModule module)
        {
            foreach (var (type, where) in DeclaredTypePositions(module))
                CheckTypeTree(type, where);

            foreach (var type in ModuleTypeWalker.AllTypes(module))
                CheckTypeTree(type, "this program");
        }

        /// <summary>
        /// Every declared type position, paired with a human description of where it is.
        /// Mirrors ModuleTypeWalker's coverage and adds the three containers it never
        /// visits: delegates, extern declarations, and enum underlying types.
        /// </summary>
        private static IEnumerable<(TypeInfo Type, string Where)> DeclaredTypePositions(IRModule module)
        {
            foreach (var f in module.Functions ?? Enumerable.Empty<IRFunction>())
            {
                var fn = DescribeFunction(f);
                yield return (f.ReturnType, $"the return type of {fn}");
                foreach (var p in f.Parameters ?? Enumerable.Empty<IRVariable>())
                    yield return (p?.Type, $"parameter '{p?.Name}' of {fn}");
                foreach (var v in f.LocalVariables ?? Enumerable.Empty<IRVariable>())
                    yield return (v?.Type, $"local variable '{v?.Name}' in {fn}");
            }

            foreach (var g in module.GlobalVariables?.Values ?? Enumerable.Empty<IRVariable>())
                yield return (g?.Type, $"global variable '{g?.Name}'");

            foreach (var c in module.Classes?.Values ?? Enumerable.Empty<IRClass>())
            {
                var kind = c.IsStruct ? "Structure" : "Class";
                foreach (var fld in c.Fields ?? Enumerable.Empty<IRField>())
                    yield return (fld?.Type, $"field '{c.Name}.{fld?.Name}' of {kind} '{c.Name}'");
                foreach (var prop in c.Properties ?? Enumerable.Empty<IRProperty>())
                    yield return (prop?.Type, $"property '{c.Name}.{prop?.Name}'");
                foreach (var m in c.Methods ?? Enumerable.Empty<IRMethod>())
                    yield return (m?.ReturnType, $"the return type of method '{c.Name}.{m?.Name}'");
            }

            foreach (var i in module.Interfaces?.Values ?? Enumerable.Empty<IRInterface>())
            {
                foreach (var m in i.Methods ?? Enumerable.Empty<IRInterfaceMethod>())
                {
                    yield return (m?.ReturnType, $"the return type of '{i.Name}.{m?.Name}'");
                    foreach (var p in m?.Parameters ?? Enumerable.Empty<IRParameter>())
                        yield return (p?.Type, $"parameter '{p?.Name}' of '{i.Name}.{m?.Name}'");
                }
                foreach (var prop in i.Properties ?? Enumerable.Empty<IRInterfaceProperty>())
                    yield return (prop?.Type, $"property '{i.Name}.{prop?.Name}'");
            }

            // ModuleTypeWalker visits none of the following.
            foreach (var d in module.Delegates?.Values ?? Enumerable.Empty<IRDelegate>())
            {
                yield return (d?.ReturnType, $"the return type of Delegate '{d?.Name}'");
                // ⛔ IRParameter.Type is NEVER assigned for delegates (IRBuilder.cs:1036-1044
                // sets TypeName only), so a TypeInfo-based walk sees nothing here. The name
                // string is the only carrier — synthesize a TypeInfo so one code path checks it.
                foreach (var p in d?.Parameters ?? Enumerable.Empty<IRParameter>())
                    yield return (p?.Type ?? NamedType(p?.TypeName),
                        $"parameter '{p?.Name}' of Delegate '{d?.Name}'");
            }

            foreach (var e in module.ExternDeclarations?.Values ?? Enumerable.Empty<IRExternDeclaration>())
            {
                yield return (e?.ReturnType, $"the return type of extern '{e?.Name}'");
                foreach (var p in e?.Parameters ?? Enumerable.Empty<IRParameter>())
                    yield return (p?.Type ?? NamedType(p?.TypeName),
                        $"parameter '{p?.Name}' of extern '{e?.Name}'");
            }

            foreach (var en in module.Enums?.Values ?? Enumerable.Empty<IREnum>())
                yield return (en?.UnderlyingType, $"the underlying type of Enum '{en?.Name}'");
        }

        private static TypeInfo NamedType(string name) =>
            string.IsNullOrEmpty(name) ? null : new TypeInfo(name, TypeKind.Class);

        /// <summary>
        /// Checks a type AND everything nested inside it. The nesting is the whole point:
        /// <c>List(Of Long)</c> and <c>Long()</c> carry the banned type in GenericArguments
        /// and ElementType respectively, and ModuleTypeWalker does not descend into either.
        /// Tuples and nullables are recursed for the same reason.
        /// </summary>
        private static void CheckTypeTree(TypeInfo type, string where, int depth = 0)
        {
            if (type == null || depth > 16) return;   // depth guard: malformed cyclic types

            if (type.Name != null && BannedTypes.TryGetValue(type.Name, out var banned))
                throw new ForeignFeatureException(
                    $"{banned.Code}: '{type.Name}' cannot be lowered to JavaScript — {banned.Why} " +
                    $"(found as {where}.)");

            foreach (var arg in type.GenericArguments ?? Enumerable.Empty<TypeInfo>())
                CheckTypeTree(arg, where, depth + 1);
            foreach (var el in type.TupleElementTypes ?? Enumerable.Empty<TypeInfo>())
                CheckTypeTree(el, where, depth + 1);
            CheckTypeTree(type.ElementType, where, depth + 1);
            CheckTypeTree(type.UnderlyingType, where, depth + 1);
        }

        /// <summary>
        /// BL7002 — <c>ByRef</c> parameters.
        ///
        /// <para>JavaScript passes primitives by value with no way to opt out, so a write
        /// through a ByRef parameter is invisible to the caller. Emulating it means boxing
        /// every argument, which D1 forbids.</para>
        ///
        /// <para><b>Five containers, two unrelated types.</b> ByRef reaches the IR as
        /// <c>IRVariable.IsByRef</c> (free functions and class methods, which flatten into
        /// <c>module.Functions</c>) and as <c>IRParameter.IsByRef</c> (interface methods,
        /// delegates, extern declarations). Checking one channel silently accepts the other.
        /// Do not "simplify" this to a single loop over <c>module.Functions</c>.</para>
        ///
        /// <para>Note <c>IRClass.Methods[].Parameters</c> is NOT one of the containers: it
        /// was measured empty, because a method's parameters live on its
        /// <c>Implementation</c>, and that implementation is already in
        /// <c>module.Functions</c>.</para>
        /// </summary>
        private static void CheckByRef(IRModule module)
        {
            foreach (var function in module.Functions ?? Enumerable.Empty<IRFunction>())
            {
                if (function.Parameters == null) continue;
                foreach (var p in function.Parameters)
                    if (p != null && p.IsByRef)
                        throw ByRefRejection(p.Name, DescribeFunction(function));
            }

            foreach (var iface in module.Interfaces?.Values ?? Enumerable.Empty<IRInterface>())
            foreach (var method in iface.Methods ?? Enumerable.Empty<IRInterfaceMethod>())
            foreach (var p in method.Parameters ?? Enumerable.Empty<IRParameter>())
                if (p != null && p.IsByRef)
                    throw ByRefRejection(p.Name, $"interface method '{iface.Name}.{method.Name}'");

            foreach (var del in module.Delegates?.Values ?? Enumerable.Empty<IRDelegate>())
            foreach (var p in del.Parameters ?? Enumerable.Empty<IRParameter>())
                if (p != null && p.IsByRef)
                    throw ByRefRejection(p.Name, $"delegate '{del.Name}'");

            foreach (var ext in module.ExternDeclarations?.Values ?? Enumerable.Empty<IRExternDeclaration>())
            foreach (var p in ext.Parameters ?? Enumerable.Empty<IRParameter>())
                if (p != null && p.IsByRef)
                    throw ByRefRejection(p.Name, $"extern declaration '{ext.Name}'");
        }

        private static string DescribeFunction(IRFunction f) =>
            $"{(f.ReturnType == null || f.ReturnType.Name == "Void" ? "Sub" : "Function")} '{f.Name}'";

        /// <summary>
        /// Names the offender rather than citing a line: declaration <c>SourceLine</c> is 0
        /// throughout the IR (it exists only on <c>IRInstruction</c>), so "parameter 'x' in
        /// Sub 'Bump'" is the most locating information available here.
        /// </summary>
        private static ForeignFeatureException ByRefRejection(string parameterName, string container) =>
            new ForeignFeatureException(
                $"BL7002: ByRef parameter '{parameterName}' in {container} cannot be lowered to " +
                "JavaScript. " + ByRefWhy);

        /// <summary>
        /// BL7002 raised from a CALL rather than a declaration.
        ///
        /// <para>Needed because <c>IRCall.ByRefArguments</c> has a second source the
        /// declaration walk cannot see: a resolved .NET target carries ref/out in its
        /// descriptor (<c>IRBuilder.cs:3585</c>), so no BasicLang parameter is ever marked
        /// <c>IsByRef</c>. Until BL7007 rejects .NET types outright this is the only guard
        /// on that path, and it must report BL7002 rather than "not implemented yet" —
        /// ByRef is refused by design, not pending.</para>
        /// </summary>
        public static ForeignFeatureException ByRefArgumentRejection(string calleeName) =>
            new ForeignFeatureException(
                $"BL7002: the call to '{calleeName}' passes an argument by reference, which " +
                "cannot be lowered to JavaScript. " + ByRefWhy);

        private const string ByRefWhy =
            "JavaScript has no reference parameters, so a write through a ByRef argument " +
            "would be invisible to the caller. Return a value instead.";
    }
}
