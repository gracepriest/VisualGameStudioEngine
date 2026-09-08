using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;

namespace BasicLang.Compiler.CodeGen.JavaScript
{
    /// <summary>
    /// Type and operator mapping for the JavaScript backend.
    ///
    /// <para><b>Types erase</b> (spec D2). JS carries no type annotations, so
    /// <see cref="MapType"/> has nothing to emit; it exists to satisfy
    /// <see cref="ITypeMapper"/>. The type system stays a compile-time contract enforced
    /// by the semantic analyzer and discarded here — the same treatment generics and
    /// interfaces get, and the opposite of the C++ backend, which preserves the type
    /// system into its output and pays for it in complexity.</para>
    ///
    /// <para><b>One numeric type.</b> Integer, Single and Double all become JS
    /// <c>number</c>. Consequences live in <see cref="MapBinaryOperator"/>.</para>
    /// </summary>
    public class JavaScriptTypeMapper : ITypeMapper
    {
        /// <summary>Erased — JS has no type annotations. Never emit this into output.</summary>
        public string MapType(TypeInfo type) => "";

        public string GetDefaultValue(TypeInfo type)
        {
            if (type == null) return "null";

            switch (type.Name)
            {
                // One numeric type: Integer/Single/Double all default to 0.
                case "Integer":
                case "Single":
                case "Double":
                    return "0";
                case "Boolean":
                    return "false";
                case "String":
                    return "\"\"";
                default:
                    return "null";
            }
        }

        /// <summary>
        /// Infix JS operator for a binary op.
        ///
        /// <para><b><see cref="BinaryOpKind.IntDiv"/> is deliberately absent.</b> BasicLang's
        /// <c>\</c> truncates toward zero and JS has no such operator — it needs
        /// <c>Math.trunc(a / b)</c>, which is a call, not an infix form this method could
        /// return. The generator special-cases IntDiv before consulting the mapper. Adding
        /// an <c>IntDiv =&gt; "/"</c> arm here would make <c>7 \ 2</c> quietly evaluate to
        /// 3.5, so the default throws rather than guessing.</para>
        /// </summary>
        public string MapBinaryOperator(BinaryOpKind op) => op switch
        {
            BinaryOpKind.Add => "+",
            BinaryOpKind.Sub => "-",
            BinaryOpKind.Mul => "*",
            BinaryOpKind.Div => "/",
            // .NET's % and JS's % agree on sign: both take the sign of the dividend.
            BinaryOpKind.Mod => "%",

            // JS `&` / `|` on booleans coerce to 32-bit ints and return a NUMBER, so the
            // non-short-circuit pair cannot use them. Both pairs render as `&&`/`||` here;
            // JS's own short-circuiting is then a divergence from VB's And/Or for a right
            // operand with a side effect — the same gap the C++ statement position has, and
            // it closes the same way (control-flow lowering), not by changing this text.
            BinaryOpKind.And => "&&",
            BinaryOpKind.Or => "||",
            BinaryOpKind.AndAlso => "&&",
            BinaryOpKind.OrElse => "||",

            BinaryOpKind.BitwiseAnd => "&",
            BinaryOpKind.BitwiseOr => "|",
            BinaryOpKind.Xor => "^",
            BinaryOpKind.Shl => "<<",
            BinaryOpKind.Shr => ">>",

            // BasicLang comparisons are value comparisons over erased numerics and
            // immutable strings, so strict equality is the correct mapping.
            BinaryOpKind.Eq => "===",
            BinaryOpKind.Ne => "!==",
            BinaryOpKind.Lt => "<",
            BinaryOpKind.Le => "<=",
            BinaryOpKind.Gt => ">",
            BinaryOpKind.Ge => ">=",

            // String concat: JS + over two strings is concatenation.
            BinaryOpKind.Concat => "+",

            _ => throw new System.NotSupportedException(
                $"JavaScript backend: BinaryOpKind.{op} has no infix JS form. " +
                "IntDiv is handled by the generator (Math.trunc); anything else reaching " +
                "here is a new IR opcode that needs a deliberate lowering decision.")
        };

        public string MapComparisonOperator(CompareKind op) => op switch
        {
            CompareKind.Eq => "===",
            CompareKind.Ne => "!==",
            CompareKind.Lt => "<",
            CompareKind.Le => "<=",
            CompareKind.Gt => ">",
            CompareKind.Ge => ">=",
            _ => throw new System.NotSupportedException(
                $"JavaScript backend: CompareKind.{op} has no JS form.")
        };

        public string MapUnaryOperator(UnaryOpKind op) => op switch
        {
            UnaryOpKind.Neg => "-",
            UnaryOpKind.Not => "!",
            UnaryOpKind.BitwiseNot => "~",
            UnaryOpKind.Inc => "++",
            UnaryOpKind.Dec => "--",
            // AddressOf builds a delegate; in JS a function value IS the reference, so
            // there is no operator to emit. The generator emits the bare name.
            _ => throw new System.NotSupportedException(
                $"JavaScript backend: UnaryOpKind.{op} has no prefix JS form.")
        };
    }
}
