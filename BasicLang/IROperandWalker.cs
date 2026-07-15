using System.Collections.Generic;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;

namespace BasicLang.Compiler.CodeGen
{
    /// <summary>
    /// Single source of truth for "the direct IRValue operands an instruction CONSUMES" —
    /// every position where a value flows INTO an instruction (call receiver/arguments, store
    /// value, condition, arithmetic operands, switch value AND its case/pattern operands, ...).
    ///
    /// Extracted so the C++ code generator's inline-foreign pre-pass and the backend guards
    /// (ForeignFeatureChecker's inline-foreign rejection, CppCapabilityChecker's foreign-in-
    /// switch rejection) all traverse the SAME operand set. Previously CppCodeGenerator and
    /// ForeignFeatureChecker each hand-rolled a byte-identical copy that yielded only
    /// <see cref="IRSwitch.Value"/> — missing <see cref="IRSwitch.Cases"/> case values and
    /// <see cref="IRPatternCase"/> operands (When guards, range bounds, comparison/constant
    /// values). An inline foreign value in a `Case ns::const` / `When ns::call(...)` position
    /// therefore slipped past the managed honesty guard and silently miscompiled. Keep every
    /// consumer wired through here so that class of C++/managed divergence cannot recur.
    ///
    /// The switch is scoped to the IR node kinds the compiler actually emits into function
    /// bodies; LLVM-oriented / not-produced-here nodes (IRGetElementPtr, IRPhi, IRArrayAlloc,
    /// IRTupleElement, ...) are omitted — a foreign value never appears as their operand.
    /// </summary>
    public static class IROperandWalker
    {
        /// <summary>
        /// If <paramref name="value"/> is a ::-qualified foreign C++ value, return the verbatim
        /// name to report; otherwise null. Detection is BOTH type-based (Kind == Foreign) AND
        /// name-based (the verbatim "::" survives on the IRVariable name / IRCall FunctionName).
        /// The name-based fallback matters because Select-Case pattern values and When guards are
        /// NOT run through the semantic analyzer (SemanticAnalyzer.Visit(CaseClauseNode) never
        /// visits node.Patterns), so a foreign value there carries a null Type — but its "::"
        /// name still identifies it. A regular BasicLang identifier / function name can never
        /// contain "::", so there is no false-positive risk.
        /// </summary>
        public static string ForeignName(IRValue value)
        {
            if (value == null) return null;
            if (value is IRVariable v && v.Name != null && v.Name.Contains("::")) return v.Name;
            if (value is IRCall c && c.FunctionName != null && c.FunctionName.Contains("::")) return c.FunctionName;
            if (value.Type?.Kind == TypeKind.Foreign) return value.Type.Name;
            return null;
        }

        public static IEnumerable<IRValue> EnumerateOperands(IRInstruction instruction)
        {
            switch (instruction)
            {
                case IRInstanceMethodCall mc:
                    if (mc.Object != null) yield return mc.Object;
                    foreach (var a in mc.Arguments) yield return a;
                    break;
                case IRCall call:
                    if (call.CalleeValue != null) yield return call.CalleeValue;
                    foreach (var a in call.Arguments) yield return a;
                    break;
                case IRBaseMethodCall bc:
                    foreach (var a in bc.Arguments) yield return a;
                    break;
                case IRNewObject no:
                    foreach (var a in no.Arguments) yield return a;
                    break;
                case IRFieldAccess fa:
                    if (fa.Object != null) yield return fa.Object;
                    break;
                case IRFieldStore fs:
                    if (fs.Object != null) yield return fs.Object;
                    if (fs.Value != null) yield return fs.Value;
                    break;
                case IRStore st:
                    if (st.Value != null) yield return st.Value;
                    if (st.Address != null) yield return st.Address;
                    break;
                case IRAssignment asn:
                    if (asn.Value != null) yield return asn.Value;
                    break;
                case IRReturn ret:
                    if (ret.Value != null) yield return ret.Value;
                    break;
                case IRBinaryOp bin:
                    if (bin.Left != null) yield return bin.Left;
                    if (bin.Right != null) yield return bin.Right;
                    break;
                case IRCompare cmp:
                    if (cmp.Left != null) yield return cmp.Left;
                    if (cmp.Right != null) yield return cmp.Right;
                    break;
                case IRUnaryOp un:
                    if (un.Operand != null) yield return un.Operand;
                    break;
                case IRCast cast:
                    if (cast.Value != null) yield return cast.Value;
                    break;
                case IRConditionalBranch cbr:
                    if (cbr.Condition != null) yield return cbr.Condition;
                    break;
                case IRSwitch sw:
                    if (sw.Value != null) yield return sw.Value;
                    // Case VALUES (Select Case v : Case ns::const) and the operands of every
                    // PATTERN case (When guard, 1 To 10 bounds, Is > 0 value, constant value,
                    // recursing Or/Tuple alternatives) — all positions a foreign value can hide.
                    if (sw.Cases != null)
                        foreach (var (caseValue, _) in sw.Cases)
                            if (caseValue != null) yield return caseValue;
                    if (sw.PatternCases != null)
                        foreach (var pc in sw.PatternCases)
                            foreach (var op in EnumeratePatternOperands(pc))
                                yield return op;
                    break;
                case IRIndexerAccess ia:
                    if (ia.Collection != null) yield return ia.Collection;
                    foreach (var i in ia.Indices) yield return i;
                    break;
                case IRIndexerStore ist:
                    if (ist.Collection != null) yield return ist.Collection;
                    foreach (var i in ist.Indices) yield return i;
                    if (ist.Value != null) yield return ist.Value;
                    break;
                case IRArrayStore ast:
                    if (ast.Array != null) yield return ast.Array;
                    if (ast.Index != null) yield return ast.Index;
                    if (ast.Value != null) yield return ast.Value;
                    break;
                case IRThrow th:
                    if (th.Exception != null) yield return th.Exception;
                    break;
                case IRYield y:
                    if (y.Value != null) yield return y.Value;
                    break;
                case IRAwait aw:
                    if (aw.Expression != null) yield return aw.Expression;
                    break;
                case IRForEach fe:
                    if (fe.Collection != null) yield return fe.Collection;
                    break;
            }
        }

        /// <summary>
        /// The IRValue operands a pattern case CONSUMES: its optional <c>When</c> guard plus the
        /// value fields specific to the pattern subtype. Or/Tuple patterns recurse into their
        /// alternatives/elements so a foreign value nested inside them is still surfaced.
        /// </summary>
        private static IEnumerable<IRValue> EnumeratePatternOperands(IRPatternCase pattern)
        {
            if (pattern == null) yield break;

            if (pattern.WhenGuard != null) yield return pattern.WhenGuard;

            switch (pattern)
            {
                case IRRangePatternCase range:
                    if (range.LowerBound != null) yield return range.LowerBound;
                    if (range.UpperBound != null) yield return range.UpperBound;
                    break;
                case IRComparisonPatternCase comp:
                    if (comp.CompareValue != null) yield return comp.CompareValue;
                    break;
                case IRConstantPatternCase constant:
                    if (constant.Value != null) yield return constant.Value;
                    break;
                case IROrPatternCase or:
                    if (or.Alternatives != null)
                        foreach (var alt in or.Alternatives)
                            foreach (var op in EnumeratePatternOperands(alt))
                                yield return op;
                    break;
                case IRTuplePatternCase tuple:
                    if (tuple.Elements != null)
                        foreach (var elem in tuple.Elements)
                            foreach (var op in EnumeratePatternOperands(elem))
                                yield return op;
                    break;
            }
        }
    }
}
