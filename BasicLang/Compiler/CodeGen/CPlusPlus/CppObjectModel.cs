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
    /// <c>#CppInclude</c>d C++ class). It alone carries <c>enable_shared_from_this</c> (D1).</item>
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
        /// null. (An <c>Inherits Exception</c> lands here too; it has no C++ class to name and
        /// fails in clang exactly as it did before ADR-0015.)
        /// </summary>
        public static string ForeignBaseOf(IRModule module, IRClass cls) =>
            cls != null && !cls.IsStruct
            && !string.IsNullOrEmpty(cls.BaseClass)
            && !IsUserClass(module, cls.BaseClass)
                ? cls.BaseClass
                : null;

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
                if (!IsUserClass(module, cls.BaseClass)) return cls.BaseClass;
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
