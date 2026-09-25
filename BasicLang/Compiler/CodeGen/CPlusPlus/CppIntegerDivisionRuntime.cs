namespace BasicLang.Compiler.CodeGen.CPlusPlus
{
    /// <summary>
    /// <c>BasicLang::IntDiv</c> / <c>BasicLang::IntMod</c>: integral <c>\</c> and <c>Mod</c> with
    /// .NET's checks. A bare C++ <c>/</c> or <c>%</c> by zero is UNDEFINED BEHAVIOUR, and on x86 it
    /// is a hardware trap — MEASURED: <c>Try : x = a \ z : Catch e As DivideByZeroException</c>
    /// killed the process with SIGFPE ("Floating point exception", exit 136) and the handler never
    /// ran. <c>MinValue \ -1</c> (and <c>MinValue Mod -1</c>) traps the same way on 32- and 64-bit
    /// operands; .NET throws <c>OverflowException</c> for both, so the helpers do too.
    ///
    /// <para>The throws carry the .NET inheritance chain, so they enter the §11.1
    /// <c>NetException</c> ladder like a BL <c>Throw New DivideByZeroException</c> does — built
    /// from <see cref="CppExceptionTypes"/> so the two cannot drift.</para>
    ///
    /// <para>The result type is <c>decltype(a / b)</c>: the ordinary C++ promotion, the same type
    /// the bare operator produced, so every existing call site stays byte-for-byte compatible
    /// apart from the checks. Sub-<c>int</c> operands promote to <c>int</c>, where
    /// <c>-32768 \ -1</c> is 32768 and cannot trap — the same promotion .NET performs.</para>
    ///
    /// <para>Spliced UNCONDITIONALLY, right after <see cref="CppNetExceptionRuntime"/> (whose
    /// class it throws), in BOTH emission modes: GenerateHeader in CppCodeGenerator.cs and
    /// EmitRuntimeHeader in CppCodeGenerator.Split.cs — keep them in sync. Include-free: needs
    /// <c>&lt;limits&gt;</c>, in both modes' unconditional include sets. Function templates only,
    /// so ODR-safe in every translation unit.</para>
    /// </summary>
    public static class CppIntegerDivisionRuntime
    {
        public static string Source { get; } = Build();

        private static string Build()
        {
            CppExceptionTypes.TryGetInheritanceChain("DivideByZeroException", out var divideByZero);
            CppExceptionTypes.TryGetInheritanceChain("OverflowException", out var overflow);

            return @"#ifndef BASICLANG_INTDIV_RUNTIME
#define BASICLANG_INTDIV_RUNTIME
namespace BasicLang {

/* Integral `\` and `Mod`: .NET's DivideByZeroException / OverflowException instead of the
   SIGFPE a bare `/` or `%` raises on x86 (see CppIntegerDivisionRuntime). */
template <typename L, typename R>
inline void CheckIntDivOperands(L a, R b) {
    using T = decltype(a / b);
    if ((T)b == 0)
        throw NetException(""" + divideByZero + @""", ""Attempted to divide by zero."");
    if constexpr (std::numeric_limits<T>::is_signed) {
        if ((T)b == (T)-1 && (T)a == std::numeric_limits<T>::min())
            throw NetException(""" + overflow + @""", ""Arithmetic operation resulted in an overflow."");
    }
}

template <typename L, typename R>
inline auto IntDiv(L a, R b) -> decltype(a / b) {
    CheckIntDivOperands(a, b);
    return a / b;
}

template <typename L, typename R>
inline auto IntMod(L a, R b) -> decltype(a % b) {
    CheckIntDivOperands(a, b);
    return a % b;
}

} /* namespace BasicLang */
#endif /* BASICLANG_INTDIV_RUNTIME */
";
        }
    }
}
