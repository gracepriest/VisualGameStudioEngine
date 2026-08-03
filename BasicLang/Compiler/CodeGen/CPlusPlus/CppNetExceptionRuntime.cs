namespace BasicLang.Compiler.CodeGen.CPlusPlus
{
    /// <summary>
    /// P2a spec §11.1: <c>BasicLang::NetException</c> — the C++ shape of a managed
    /// exception crossing the .NET boundary — plus the <c>BasicLang::String</c> alias
    /// the generator's <c>&lt;catchVar&gt;.Message</c> lowering names.
    ///
    /// Spliced UNCONDITIONALLY in BOTH emission modes (combined: GenerateHeader in
    /// CppCodeGenerator.cs; split: EmitRuntimeHeader in CppCodeGenerator.Split.cs —
    /// keep them in sync). The §11.1 trigger is SOURCE-level, not surface-level: a
    /// .NET-typed <c>Catch</c> emits the leading NetException ladder even when the
    /// project's .NET surface is empty (valid dead code), so a surface-gated
    /// declaration would leave existing typed-catch fixtures referencing an
    /// undeclared type.
    ///
    /// Include-free by contract (like CppBclRuntime): needs &lt;stdexcept&gt;,
    /// &lt;string&gt; and &lt;cstring&gt;, all present in both modes' unconditional
    /// include sets. Opens its OWN <c>namespace BasicLang</c> (a sibling re-open at
    /// file scope, indent 0). ODR-safe: a class definition with in-class (implicitly
    /// inline) members and a using-alias only — legal in every translation unit.
    ///
    /// <para><b>#ifndef-guarded since Task 7a</b> (the same two-headers-one-definition
    /// contract as <see cref="CppNetRefRuntime"/>): <c>blnet_runtime.hpp</c> now ALSO
    /// splices this block — its <c>NetCheckTyped</c> throws <c>BasicLang::NetException</c>
    /// (§9.2's typed conversion for generated proxies) — and a translation unit that sees
    /// both that header and a generated runtime preamble must get exactly one class
    /// definition.</para>
    /// </summary>
    public static class CppNetExceptionRuntime
    {
        public const string Source = @"#ifndef BASICLANG_NETEXCEPTION_RUNTIME
#define BASICLANG_NETEXCEPTION_RUNTIME
namespace BasicLang {

/* BasicLang String is spelled std::string on the C++ backend (MapType); this alias
   gives runtime and lowered code the BL-facing name (`BasicLang::String(...)`). */
using String = std::string;

/* §11.1: a managed exception reaching BasicLang. Carries the thrown type's
   fully-qualified inheritance chain (most-derived FIRST, ';'-separated, e.g.
   ""System.ArgumentNullException;System.ArgumentException;System.SystemException;System.Exception"")
   plus the message — what() IS the message. Derives from std::runtime_error ON
   PURPOSE (spec §11.1): a BasicLang-thrown `Throw New ArgumentException(...)` lowers
   to std::runtime_error and must fall through the NetException ladder to the
   per-clause handlers, while a NetException escaping an unmatched ladder stays
   catchable by generic std::runtime_error / std::exception handlers in outer tries.
   Do not change the derivation. */
class NetException : public std::runtime_error {
public:
    NetException(const char* inheritanceChain, const char* message)
        : std::runtime_error(message != nullptr ? message : """"),
          chain_(inheritanceChain != nullptr ? inheritanceChain : """") {}
    NetException(const std::string& inheritanceChain, const std::string& message)
        : std::runtime_error(message), chain_(inheritanceChain) {}

    /* ';'-delimited ELEMENT equality — never substring: ""ArgumentException"" is a
       substring of ""MyArgumentException"" but must not match it. */
    bool Matches(const char* fqName) const {
        if (fqName == nullptr) return false;
        const std::size_t n = std::strlen(fqName);
        std::size_t pos = 0;
        for (;;) {
            const std::size_t sep = chain_.find(';', pos);
            const std::size_t end = (sep == std::string::npos) ? chain_.size() : sep;
            if (end - pos == n && chain_.compare(pos, n, fqName) == 0) return true;
            if (sep == std::string::npos) return false;
            pos = sep + 1;
        }
    }

    const std::string& InheritanceChain() const { return chain_; }

private:
    std::string chain_;
};

} /* namespace BasicLang */
#endif /* BASICLANG_NETEXCEPTION_RUNTIME */
";
    }
}
