using System;
using System.Collections.Generic;
using System.Linq;
using BasicLang.Compiler.IR;

namespace BasicLang.Compiler.CodeGen.CPlusPlus
{
    /// <summary>
    /// ADR-0015's questions about a CLASS, answered once for both of their consumers: the
    /// capability checker (which refuses what cannot be lowered, by name) and the generator
    /// (which emits the rest). A second copy of any rule here would be a mirrored pair — the
    /// checker admitting a shape the generator then places wrongly, or the reverse.
    ///
    /// <list type="bullet">
    /// <item>A hierarchy ROOT is a class with no BasicLang base: no <c>Inherits</c>, or an
    /// <c>Inherits</c> naming something that is not a class of this module (a
    /// <c>#CppInclude</c>d C++ class). It alone carries <c>enable_shared_from_this</c> (D1).
    /// ⚠ <c>Inherits Exception</c> (or another built-in exception) is NOT a root (#151): the
    /// runtime's <c>BasicLang::Exception</c> is, and the class builds on it like on a BasicLang
    /// base.</item>
    /// <item>A FOREIGN-ROOTED hierarchy is one whose root's base is such a C++ class — an
    /// ANCESTOR walk, never a descendant scan (D2a).</item>
    /// <item>The <c>MyBase.New</c> arguments of a constructor are the operands of its
    /// <see cref="IRBaseConstructorCall"/>, and <c>Base::ctor_</c> is written where that instruction
    /// is (E11, ADR-0016 D1); in a foreign-rooted hierarchy the prologue must also be PURE
    /// (D2a).</item>
    /// </list>
    /// </summary>
    public static class CppObjectModel
    {
        /// <summary>True when <paramref name="name"/> is a (non-Structure) class of this module.</summary>
        public static bool IsUserClass(IRModule module, string name) =>
            !string.IsNullOrEmpty(name)
            && module?.Classes != null
            && module.Classes.TryGetValue(name, out var cls)
            && cls != null
            && !cls.IsStruct;

        /// <summary>
        /// The base <paramref name="cls"/> names in its <c>Inherits</c> when that base is NOT a
        /// BasicLang class of this module — a C++ class reached through <c>#CppInclude</c> — else
        /// null. A built-in .NET exception base (<c>Inherits Exception</c>) is NOT foreign (#151):
        /// it is the runtime's <c>BasicLang::Exception</c>, an ordinary two-phase base
        /// (<see cref="IsRuntimeExceptionBase"/>).
        /// </summary>
        public static string ForeignBaseOf(IRModule module, IRClass cls) =>
            cls != null && !cls.IsStruct
            && !string.IsNullOrEmpty(cls.BaseClass)
            && !IsUserClass(module, cls.BaseClass)
            && !IsRuntimeExceptionBase(module, cls.BaseClass)
                ? cls.BaseClass
                : null;

        /// <summary>
        /// #151: true when <paramref name="baseName"/>, written in an <c>Inherits</c>, is one of the
        /// built-in .NET exceptions (<see cref="CppExceptionTypes"/>) and not a class of this module.
        /// The C++ runtime's <c>BasicLang::Exception</c> (<see cref="CppExceptionRuntime"/>) stands
        /// in for it: an ordinary hierarchy root — <c>enable_shared_from_this</c>, a tag
        /// constructor and <c>ctor_</c> overloads — so the class below it is NOT a root and builds
        /// its base through <c>ctor_</c> like any BasicLang base, never through a member-initializer
        /// list. Which built-in it was survives only in the type chain a throw carries
        /// (<see cref="ExceptionChainOf"/>).
        /// </summary>
        public static bool IsRuntimeExceptionBase(IRModule module, string baseName) =>
            !string.IsNullOrEmpty(baseName)
            && !IsUserClass(module, baseName)
            && CppExceptionTypes.TryGetInheritanceChain(baseName, out _);

        /// <summary>
        /// #151: the built-in .NET exception at the top of <paramref name="cls"/>'s ancestor chain
        /// (<c>Exception</c>, <c>ArgumentException</c>, …), or null when the class is not an
        /// exception. An ANCESTOR walk, like <see cref="ForeignRootOf"/>.
        /// </summary>
        public static string ExceptionRootOf(IRModule module, IRClass cls)
        {
            var seen = new HashSet<IRClass>(ReferenceEqualityComparer.Instance);
            while (cls != null && !cls.IsStruct && seen.Add(cls))
            {
                if (string.IsNullOrEmpty(cls.BaseClass)) return null;
                if (!IsUserClass(module, cls.BaseClass))
                    return IsRuntimeExceptionBase(module, cls.BaseClass) ? cls.BaseClass : null;
                cls = module.Classes[cls.BaseClass];
            }
            return null;
        }

        /// <summary>#151: true when <paramref name="name"/> is a class of this module that is an exception.</summary>
        public static bool IsExceptionClass(IRModule module, string name) =>
            IsUserClass(module, name) && ExceptionRootOf(module, module.Classes[name]) != null;

        /// <summary>
        /// #151: true when <paramref name="cls"/> or a BasicLang class above it declares a field,
        /// property or method named <paramref name="member"/> — so a member access by that name
        /// reaches the user's member, not one <c>BasicLang::Exception</c> provides (or lacks).
        /// </summary>
        public static bool DeclaresMember(IRModule module, IRClass cls, string member)
        {
            var seen = new HashSet<IRClass>(ReferenceEqualityComparer.Instance);
            while (cls != null && seen.Add(cls))
            {
                if (cls.Fields.Any(f => string.Equals(f.Name, member, StringComparison.OrdinalIgnoreCase))
                    || cls.Properties.Any(p => string.Equals(p.Name, member, StringComparison.OrdinalIgnoreCase))
                    || cls.Methods.Any(m => string.Equals(m.Name, member, StringComparison.OrdinalIgnoreCase)))
                    return true;
                cls = IsUserClass(module, cls.BaseClass) ? module.Classes[cls.BaseClass] : null;
            }
            return false;
        }

        /// <summary>
        /// #151: the type chain a thrown <paramref name="cls"/> carries, most-derived FIRST and
        /// ';'-separated — this module's classes by their declared names, then the built-in root's
        /// .NET chain: <c>LeafErr;BaseErr;System.Exception</c>. It is what
        /// <c>BasicLang::NetException::Matches</c> walks, so a <c>Catch</c> of a user exception is
        /// decided by element equality on the class's name, exactly as a .NET-typed one is. Null
        /// when the class is not an exception.
        /// </summary>
        public static string ExceptionChainOf(IRModule module, IRClass cls)
        {
            var root = ExceptionRootOf(module, cls);
            if (root == null || !CppExceptionTypes.TryGetInheritanceChain(root, out var netChain)) return null;
            var names = new List<string>();
            var seen = new HashSet<IRClass>(ReferenceEqualityComparer.Instance);
            while (cls != null && seen.Add(cls))
            {
                names.Add(cls.Name);
                cls = IsUserClass(module, cls.BaseClass) ? module.Classes[cls.BaseClass] : null;
            }
            names.Add(netChain);
            return string.Join(";", names);
        }

        /// <summary>D1: the class that carries the hierarchy's one <c>enable_shared_from_this</c>.</summary>
        public static bool IsRoot(IRModule module, IRClass cls) =>
            cls != null && !cls.IsStruct
            && (string.IsNullOrEmpty(cls.BaseClass) || ForeignBaseOf(module, cls) != null);

        /// <summary>
        /// D2a: the foreign C++ class at the top of <paramref name="cls"/>'s ancestor chain, or
        /// null when the chain ends in a BasicLang root with no base.
        /// </summary>
        public static string ForeignRootOf(IRModule module, IRClass cls)
        {
            var seen = new HashSet<IRClass>(ReferenceEqualityComparer.Instance);
            while (cls != null && !cls.IsStruct && seen.Add(cls))
            {
                if (string.IsNullOrEmpty(cls.BaseClass)) return null;
                if (!IsUserClass(module, cls.BaseClass))
                    return IsRuntimeExceptionBase(module, cls.BaseClass) ? null : cls.BaseClass;
                cls = module.Classes[cls.BaseClass];
            }
            return null;
        }

        /// <summary>D2a: true when <paramref name="cls"/>'s ancestor chain ends in a C++ class.</summary>
        public static bool IsForeignRooted(IRModule module, IRClass cls) => ForeignRootOf(module, cls) != null;

        /// <summary>
        /// D2a's purity rule, read off the constructor's PROLOGUE (ADR-0016 D1): the base call sits in
        /// the entry block (a multi-block prologue is an <c>AndAlso</c>/<c>OrElse</c>, which builds its
        /// value across statements), every instruction before it is a pure operator or a constant,
        /// and every argument is pure (<see cref="IsPureBaseArgument"/>). Such a prologue has no
        /// statement the tag constructor's initializer list could not repeat as an expression.
        ///
        /// <para>ADR-0015's implementation note "the argument list can be STALE" is moot: the
        /// arguments are the instruction's operands, which every pass re-points when it replaces a
        /// node (strength reduction's <c>a * 2</c> → <c>a &lt;&lt; 1</c> included).</para>
        /// </summary>
        public static bool IsPurePrologue(IRModule module, IRFunction impl, IRBaseConstructorCall baseCall)
        {
            if (impl?.EntryBlock == null || baseCall == null) return false;
            var entry = impl.EntryBlock.Instructions;
            var at = entry.IndexOf(baseCall);
            if (at < 0) return false;
            for (var i = 0; i < at; i++)
                if (entry[i] is not (IRConstant or IRVariable or IRComment) && !(entry[i] is IRValue v && IsOperator(v)))
                    return false;
            return baseCall.Args.All(a => IsPureBaseArgument(module, impl, a));
        }

        /// <summary>An operator node that renders as one expression with no statement of its own.</summary>
        private static bool IsOperator(IRValue value) => value switch
        {
            IRBinaryOp => true,
            IRUnaryOp unary => unary.Operation is UnaryOpKind.Neg or UnaryOpKind.Not or UnaryOpKind.BitwiseNot,
            IRCompare => true,
            _ => false,
        };

        /// <summary>
        /// D2a's purity rule: a <c>MyBase.New</c> argument into a foreign-rooted class is
        /// evaluated in the tag constructor's member-initializer list AND, at depth two or more,
        /// again in <c>ctor_</c>'s step 1 — so it must be a parameter, a literal or constant, a
        /// module-level variable, or an operator over those, which reads the same both times.
        ///
        /// <para>⚠ A module-level VARIABLE is admitted beyond the ruling's literal list: between
        /// the two evaluations no BasicLang code runs (the tag constructors all run inside
        /// <c>make_shared</c>; <c>ctor_</c>'s step 1 precedes every body), so it reads the same
        /// value twice, and the shape already compiled and ran before ADR-0015
        /// (<c>MyBase.New(G)</c> into a <c>#CppInclude</c>d base) — refusing it would regress it.</para>
        /// </summary>
        public static bool IsPureBaseArgument(IRModule module, IRFunction impl, IRValue value) =>
            value switch
            {
                IRConstant => true,
                IRVariable variable => IsParameterOf(impl, variable) || IsGlobal(module, variable),
                IRBinaryOp binary => IsPureBaseArgument(module, impl, binary.Left)
                                     && IsPureBaseArgument(module, impl, binary.Right),
                IRUnaryOp unary => IsOperator(unary) && IsPureBaseArgument(module, impl, unary.Operand),
                IRCompare compare => IsPureBaseArgument(module, impl, compare.Left)
                                     && IsPureBaseArgument(module, impl, compare.Right),
                _ => false,
            };

        private static bool IsParameterOf(IRFunction impl, IRVariable variable) =>
            impl != null
            && impl.Parameters.Any(p => ReferenceEquals(p, variable)
                                        || (variable.IsParameter
                                            && string.Equals(p.Name, variable.Name, StringComparison.OrdinalIgnoreCase)));

        private static bool IsGlobal(IRModule module, IRVariable variable) =>
            module?.GlobalVariables != null
            && module.GlobalVariables.Values.Any(g => ReferenceEquals(g, variable)
                                                      || string.Equals(g.Name, variable.Name, StringComparison.OrdinalIgnoreCase));
    }
}
