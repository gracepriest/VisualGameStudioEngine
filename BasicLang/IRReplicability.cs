using System;

namespace BasicLang.Compiler.IR
{
    /// <summary>
    /// Whether evaluating an <see cref="IRValue"/> more than once is indistinguishable from
    /// evaluating it once — ADR-0001's <c>IsReplicable</c>, with ADR-0004 D2's whitelist.
    ///
    /// <para><b>Structural, whitelist-only, default FALSE.</b> A wrong "yes" here is a silent
    /// miscompile (an effect duplicated, or deleted by a merge); a wrong "no" only costs a
    /// declared local. So anything not recognised below is not replicable.</para>
    ///
    /// <para>Replicable: literals; <c>Me</c>, parameters and locals (read by name); pure operators
    /// (<see cref="IRBinaryOp"/>, <see cref="IRUnaryOp"/>, <see cref="IRCompare"/>,
    /// <see cref="IRCast"/>) over replicable operands. NEVER: a non-<c>Const</c> global, a
    /// <c>ByRef</c> parameter, any call (free, instance or base), a property / indexer / field
    /// load, an allocation, an await, anything unrecognised.</para>
    ///
    /// <para>⛔ <b>This is not a value-stability test</b> (ADR-0004 D2). "No write to an operand
    /// between a multi-use value's definition and its last use" is Invariant S, owned by the pass
    /// that creates the sharing and checked by a verifier — never by widening this predicate
    /// with dataflow. And it must NOT delegate to CSE's <c>ReadsCallVisible</c>: that answers
    /// "can a call change this?" (kill), not "may this be evaluated twice?" (replicate). The
    /// two lists overlap by design; coupling the functions would let a CSE change silently move
    /// codegen.</para>
    ///
    /// <para>One predicate, shared: the C# backend's materialisation (ADR-0001's
    /// <c>ShouldEmitInstruction</c> arm) today; <c>AlgebraicSimplificationPass</c>'s
    /// <c>2 * x → x + x</c> gate and CSE's candidate gate are to consume the same one.</para>
    /// </summary>
    public static class IRReplicability
    {
        /// <summary>The structural predicate alone.</summary>
        public static bool IsReplicable(IRValue value) => IsReplicable(value, null);

        /// <summary>
        /// The structural predicate, with a consumer hook consulted first for EVERY node visited
        /// (the value itself and each operand, recursively). A consumer that has already bound a
        /// value to a declared local answers <c>true</c> for it — a materialised temp is
        /// replicable, since reading it twice reads a variable twice. <c>null</c> falls through
        /// to the structural rules. The hook can only answer for a node; it cannot widen what
        /// the rules below call pure.
        /// </summary>
        public static bool IsReplicable(IRValue value, Func<IRValue, bool?> consumerOverride)
        {
            if (value == null) return false;

            var answered = consumerOverride?.Invoke(value);
            if (answered.HasValue) return answered.Value;

            switch (value)
            {
                case IRConstant:
                    return true;

                case IRVariable variable:
                    if (variable.IsByRef) return false;
                    if (variable.IsGlobal) return variable.IsConst;
                    return true; // Me, a parameter, a local

                case IRBinaryOp binary:
                    return IsReplicable(binary.Left, consumerOverride)
                        && IsReplicable(binary.Right, consumerOverride);

                case IRUnaryOp unary:
                    return IsReplicable(unary.Operand, consumerOverride);

                case IRCompare compare:
                    return IsReplicable(compare.Left, consumerOverride)
                        && IsReplicable(compare.Right, consumerOverride);

                case IRCast cast:
                    return IsReplicable(cast.Value, consumerOverride);

                default:
                    // Calls, instance/base calls, field/indexer/element loads, allocations,
                    // awaits, and every kind added after this was written.
                    return false;
            }
        }
    }
}
