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
    /// <item>The <c>MyBase.New</c> arguments of a constructor must be evaluable as a straight-line
    /// PREFIX of the constructor's entry block, because <c>Base::ctor_</c> is placed right after
    /// them (E11); in a foreign-rooted hierarchy they must also be PURE (D2a).</item>
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
        /// E11: where a <c>ctor_</c>'s base call goes, and how each <c>MyBase.New</c> argument is
        /// spelled there — or a refusal naming why it cannot be placed.
        ///
        /// <para>IRBuilder evaluates the arguments FIRST, into the entry block, then the body.
        /// <c>Base::ctor_</c> goes immediately after that evaluation, and nothing else may precede
        /// it: <c>PrefixLength</c> counts the entry block's leading instructions that compute the
        /// arguments — their operand closure, extended over the stores that fill an array literal
        /// the closure allocated.</para>
        ///
        /// <para>⛔ THE ARGUMENT LIST CAN BE STALE. <see cref="IRConstructor.BaseConstructorArgs"/> is
        /// not an instruction operand, so an optimizer pass that REPLACES an argument's node (strength
        /// reduction turns <c>a * 2</c> into a new <c>a &lt;&lt; 1</c> node and re-points only
        /// instruction operands; constant folding drops the node for a constant) leaves the list
        /// pointing at a node no block holds — measured on the default pipeline, without
        /// <c>-O</c>. Such a node is rendered INLINE (<c>Inline</c>) when it is an operator, whose
        /// replacement is equivalent to it by construction; its operands are placed like any
        /// argument's.</para>
        ///
        /// <para>Refused — never guessed — when an argument needs control flow
        /// (<c>AndAlso</c>/<c>OrElse</c> build a local across blocks), when anything else is
        /// interleaved with its evaluation, or when a later instruction still writes into a value
        /// the call receives. Before ADR-0015 every computed argument was a C++ compile error
        /// ("use of undeclared identifier"), so a refusal regresses nothing, while a WRONG
        /// placement would compile and pass a value that is not yet computed.</para>
        /// </summary>
        public static (int PrefixLength, HashSet<IRValue> Inline, string Refusal) PlanBaseArguments(
            IRModule module, IRConstructor ctor)
        {
            var inline = new HashSet<IRValue>(ReferenceEqualityComparer.Instance);
            var args = ctor?.BaseConstructorArgs;
            if (args == null || args.Count == 0) return (0, inline, null);

            var impl = ctor.Implementation;
            var entry = impl?.EntryBlock;
            var instructions = entry?.Instructions ?? new List<IRInstruction>();
            var position = new Dictionary<IRInstruction, int>(ReferenceEqualityComparer.Instance);
            for (var i = 0; i < instructions.Count; i++)
                position.TryAdd(instructions[i], i);

            var closure = new HashSet<IRValue>(ReferenceEqualityComparer.Instance);
            string refusal = null;

            void Visit(IRValue value)
            {
                if (refusal != null || value == null || value is IRConstant) return;
                if (value is IRVariable variable)
                {
                    // A lambda renders INLINE at its use site (the generator's GetValueName), so it
                    // needs no statement before the base call either.
                    if (!IsParameterOf(impl, variable) && !IsGlobal(module, variable) && !IsLambda(module, variable))
                        refusal = $"reads '{variable.Name}', which is neither a parameter, a module-level value " +
                                  "nor a lambda (AndAlso / OrElse build such a value across several statements)";
                    return;
                }
                if (position.ContainsKey(value))
                {
                    if (!closure.Add(value)) return;
                }
                else if (IsOperator(value))
                {
                    if (!inline.Add(value)) return;
                }
                else
                {
                    refusal = "is computed with control flow (AndAlso / OrElse)";
                    return;
                }
                foreach (var operand in IROperandWalker.EnumerateOperands(value))
                    Visit(operand);
            }

            foreach (var arg in args) Visit(arg);
            if (refusal != null) return (0, inline, refusal);

            var length = closure.Count == 0 ? 0 : closure.Max(v => position[v]) + 1;

            // An array literal is its allocation followed by one store per element: the stores
            // are part of evaluating the argument even though no operand edge leads to them.
            while (length < instructions.Count && WritesInto(instructions[length], closure))
            {
                if (instructions[length] is IRValue gep) closure.Add(gep);
                length++;
            }

            for (var i = 0; i < length; i++)
            {
                var inst = instructions[i];
                if (inst is IRValue v && closure.Contains(v)) continue;
                if (WritesInto(inst, closure) || inst is IRComment) continue;
                return (0, inline, "is evaluated with other statements in between");
            }

            // A store into an argument AFTER Base::ctor_ would complete the value too late.
            foreach (var block in impl?.Blocks ?? new List<BasicBlock>())
                for (var i = block == entry ? length : 0; i < block.Instructions.Count; i++)
                    if (WritesInto(block.Instructions[i], closure))
                        return (0, inline, "is still being filled after it would be passed");

            return (length, inline, null);
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

        private static bool IsLambda(IRModule module, IRVariable variable) =>
            variable.Name != null
            && variable.Name.StartsWith("__lambda_", StringComparison.Ordinal)
            && module?.Functions != null
            && module.Functions.Any(f => f.IsLambda && f.Name == variable.Name);

        private static bool IsGlobal(IRModule module, IRVariable variable) =>
            module?.GlobalVariables != null
            && module.GlobalVariables.Values.Any(g => ReferenceEquals(g, variable)
                                                      || string.Equals(g.Name, variable.Name, StringComparison.OrdinalIgnoreCase));

        private static bool WritesInto(IRInstruction inst, HashSet<IRValue> closure) => inst switch
        {
            IRArrayStore store => store.Array != null && closure.Contains(store.Array),
            IRGetElementPtr gep => gep.BasePointer != null && closure.Contains(gep.BasePointer),
            IRStore store => store.Address != null && closure.Contains(store.Address),
            _ => false,
        };
    }
}
