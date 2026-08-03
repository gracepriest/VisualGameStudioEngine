namespace BasicLang.Compiler.CodeGen.CPlusPlus
{
    /// <summary>
    /// P2a-2 THE FLIP, D-P7: <c>BasicLang::NetRef</c> — the RAII handle every ManagedOwned
    /// declaration position lowers to — lives in the ALWAYS-emitted native runtime, so a
    /// declaration-only program (empty surface, no shim) compiles and runs. Null-slot-safe by
    /// construction: the addref/release hooks are null until a boundary runtime binds them,
    /// and both paths no-op on handle 0 / null hook — a declaration-only program can never
    /// hold a non-zero handle, so the hooks are unreachable there.
    ///
    /// <para><b>Single definition, spliced into TWO headers from this one constant:</b> the
    /// generated runtime preamble (combined: GenerateHeader in CppCodeGenerator.cs; split:
    /// EmitRuntimeHeader in CppCodeGenerator.Split.cs — keep them in sync, same contract as
    /// <see cref="CppNetExceptionRuntime"/>) and <c>blnet_runtime.hpp</c>
    /// (<see cref="BlnetRuntimeSources"/>), whose <c>blnet</c> namespace re-exports the type
    /// (<c>using ::BasicLang::NetRef;</c>) and binds the hooks to <c>g_shim</c>. The
    /// <c>#ifndef</c> guard is what makes the two splices one definition when a translation
    /// unit sees both headers.</para>
    ///
    /// <para>Unlike the include-free splices, this block carries its OWN
    /// <c>#include &lt;cstdint&gt;/&lt;memory&gt;</c> — deliberately: adding to the emission
    /// modes' include HashSets would reorder their enumeration (a resize re-hashes) and churn
    /// every generated header, breaking the flip's emission-identity guarantee. An
    /// <c>#include</c> at file scope mid-header is legal C++ and keeps the diff to exactly
    /// this block.</para>
    /// </summary>
    public static class CppNetRefRuntime
    {
        public const string GuardedSource = @"#ifndef BASICLANG_NETREF_RUNTIME
#define BASICLANG_NETREF_RUNTIME
#include <cstdint>
#include <memory>
namespace BasicLang {

/* D-P7 hooks: null until a boundary runtime (blnet_runtime.hpp) binds them to the live
   shim. A program whose .NET surface is empty never binds them — and never holds a
   non-zero handle, so both stay unreachable there (P0's zero-handle rule). */
inline void (*g_netref_addref)(std::uint64_t) = nullptr;
inline void (*g_netref_release)(std::uint64_t) = nullptr;

/* C2: NetRef — RAII over a managed handle (shared_ptr custom-deleter pattern, mirroring
   the collection layer's reference semantics). Copy = shared_ptr copy (no ABI call); a
   new INDEPENDENT table reference goes through Duplicate (checked addref). */
class NetRef {
    std::shared_ptr<void> ref_;
public:
    NetRef() = default;
    /* Takes ownership of one table reference (fresh handles are born refcount 1). */
    explicit NetRef(std::uint64_t h)
        : ref_(h ? std::shared_ptr<void>(reinterpret_cast<void*>(h),
              [](void* p) { if (g_netref_release) g_netref_release(reinterpret_cast<std::uint64_t>(p)); })
                 : nullptr) {}
    std::uint64_t get() const { return reinterpret_cast<std::uint64_t>(ref_.get()); }
    explicit operator bool() const { return static_cast<bool>(ref_); }
    /* A new INDEPENDENT NetRef for the same object goes through blnet_addref. */
    static NetRef Duplicate(const NetRef& other) {
        if (other && g_netref_addref) g_netref_addref(other.get());
        return NetRef(other.get());
    }
};

} /* namespace BasicLang */
#endif /* BASICLANG_NETREF_RUNTIME */
";
    }
}
