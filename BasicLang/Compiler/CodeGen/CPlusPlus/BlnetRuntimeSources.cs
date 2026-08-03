namespace BasicLang.Compiler.CodeGen.CPlusPlus
{
    /// <summary>
    /// Single source of truth for the .NET⇄native boundary runtime headers
    /// (spec: docs/superpowers/specs/2026-07-26-dotnet-native-boundary-contract-design.md):
    /// <c>blnet.h</c> (C-compatible contract surface) and <c>blnet_runtime.hpp</c>
    /// (header-only C++20 native runtime). The status <c>#define</c> section of
    /// <c>blnet.h</c> is spliced from <see cref="BlnetContract.GenerateStatusHeader"/>
    /// at read time so the header can never drift from the contract table
    /// (drift-tested in <c>BlnetRuntimeSourcesTests</c>, compile-smoked in
    /// <c>BlnetNativeRuntimeTests</c>).
    ///
    /// <para><b>P2a §9.3 additions (plan Task 12).</b> <c>blnet_runtime.hpp</c> also carries
    /// the three symbols the generated startup TU needs and P0 never had:
    /// <c>g_native_vtable</c> (the native half of P0's 2-slot positional vtable),
    /// <c>blnet_bind_core</c> (binds P0's seven exports into <c>g_shim</c>), and the
    /// platform primitives <c>blnet_load_module</c> / <c>blnet_get_symbol</c> /
    /// <c>blnet_load_error</c>. The first two are transport-neutral and P2b reuses them
    /// unchanged; only the platform primitives are transport-A-specific, and their
    /// DEFINITIONS sit behind <c>BLNET_IMPLEMENT_LOADER</c> so <c>&lt;windows.h&gt;</c>
    /// reaches exactly one translation unit. See the header text for why that matters.</para>
    /// </summary>
    public static class BlnetRuntimeSources
    {
        /// <summary>
        /// Complete <c>blnet.h</c> text. TWO sections are spliced from <see cref="BlnetContract"/>
        /// rather than written here: the status <c>#define</c>s and the
        /// <c>BLNET_EXPORT_*</c> names. Both were literals once; both are lists whose members
        /// must agree with something else (the shim's status enum, the shim's
        /// <c>[UnmanagedCallersOnly]</c> entry points), and a literal copy of such a list is the
        /// exact drift shape <see cref="BlnetContract"/> exists to remove.
        /// </summary>
        public static string BlnetHeader =>
            Header1 + BlnetContract.GenerateStatusHeader() + Header2 + BlnetContract.GenerateCoreExportHeader();

        /// <summary>
        /// Complete <c>blnet_runtime.hpp</c> text. Since the P2a-2 flip (D-P7) the
        /// <c>NetRef</c> class DEFINITION is spliced from <see cref="CppNetRefRuntime"/> —
        /// the same constant the always-emitted generated runtime splices — at file scope,
        /// before the <c>blnet</c> namespace opens; the namespace re-exports it and binds
        /// its hooks to <c>g_shim</c>. One definition, two headers, include-guard dedup.
        /// Task 7a adds <see cref="CppNetExceptionRuntime"/> the same way: <c>NetCheckTyped</c>
        /// (§9.2's typed status conversion, called by every generated proxy) throws
        /// <c>BasicLang::NetException</c>, so the class must be in scope here too.
        /// </summary>
        public static string BlnetRuntime =>
            RuntimeHead + CppNetExceptionRuntime.Source + CppNetRefRuntime.GuardedSource + RuntimeBody;

        /// <summary><c>blnet.h</c> — everything before the generated status section.</summary>
        private const string Header1 = @"/* blnet.h — .NET⇄native boundary contract v1 (spec 2026-07-26). */
/* SOURCE OF TRUTH: BasicLang BlnetRuntimeSources.cs — do not edit the emitted copy. */
#pragma once
#include <stdint.h>

#if defined(_WIN32) && defined(_M_IX86)
#define BLNET_CALL __cdecl
#else
#define BLNET_CALL
#endif

/* C2: {generation: high 32 | index: low 32}. Index 0 is reserved (a zero handle is never valid). */
typedef uint64_t blnet_handle;
typedef uint64_t blnet_callback;

";

        /// <summary><c>blnet.h</c> — everything after the generated status section.</summary>
        private const string Header2 = @"
/* C5 slot descriptor: how one 64-bit slot is encoded (needed for deep-copy at enqueue). */
typedef enum BlnetSlotKind {
    BLNET_SLOT_VALUE = 0,   /* blittable scalar or struct <= 8 bytes, in-slot */
    BLNET_SLOT_STRING = 1,  /* UTF-8 char*, C3 ownership rules */
    BLNET_SLOT_STRUCT = 2,  /* pointer to blittable struct > 8 bytes, borrowed for the call */
    BLNET_SLOT_HANDLE = 3,  /* blnet_handle */
    BLNET_SLOT_OUT = 4      /* caller-provided pointer the callee writes through (inline-only) */
} BlnetSlotKind;

typedef struct BlnetSlotDesc { int32_t kind; int32_t size; /* bytes; used for STRUCT/OUT */ } BlnetSlotDesc;

/* C5: the single universal thunk (native-side). */
typedef int32_t (BLNET_CALL *BlnetInvokeCallbackFn)(
    uint64_t callback_handle, const uint64_t* args, int32_t argc, uint64_t* result);
/* Retrieves the pending native-exception message after BLNET_E_NATIVE_EXCEPTION
   (buffer allocated with blnet_alloc; receiver frees via blnet_free). */
typedef int32_t (BLNET_CALL *BlnetGetNativeErrorFn)(char** message);

typedef struct BlnetNativeVtable {
    BlnetInvokeCallbackFn invoke_callback;
    BlnetGetNativeErrorFn get_native_error;
} BlnetNativeVtable;

/* Shim exports (managed side). Native code binds these by name. */
";

        /// <summary><c>blnet_runtime.hpp</c> — everything before the D-P7 NetRef splice.</summary>
        private const string RuntimeHead = @"/* blnet_runtime.hpp — native-side runtime of the boundary contract v1. Header-only C++20. */
#pragma once
#include ""blnet.h""
#include <atomic>
#include <cstring>
#include <deque>
#include <functional>
#include <memory>
#include <mutex>
#include <stdexcept>
#include <string>
#include <vector>

";

        /// <summary><c>blnet_runtime.hpp</c> — everything after the D-P7 NetRef splice.</summary>
        private const string RuntimeBody = @"
namespace BasicLang { namespace blnet {

/* ---- Shim binding (filled by the host: harness now, generated startup in P2) ---- */
struct ShimApi {
    int32_t (BLNET_CALL *abi_version)(void) = nullptr;
    int32_t (BLNET_CALL *initialize)(int32_t, const BlnetNativeVtable*) = nullptr;
    int32_t (BLNET_CALL *addref)(blnet_handle) = nullptr;
    int32_t (BLNET_CALL *release)(blnet_handle) = nullptr;
    void*   (BLNET_CALL *alloc)(int64_t) = nullptr;
    void    (BLNET_CALL *free_)(void*) = nullptr;
    int32_t (BLNET_CALL *last_error)(char**, char**) = nullptr;
};
inline ShimApi g_shim;

/* ---- C6/C5 same-thread detection: depth of native->managed calls on this thread ---- */
inline thread_local int g_call_depth = 0;
struct BlnetCallScope { BlnetCallScope() { ++g_call_depth; } ~BlnetCallScope() { --g_call_depth; } };

/* ---- C4: NetCheck — status to C++ exception ---- */
inline void NetCheck(int32_t status) {
    if (status == BLNET_OK) return;
    std::string msg = ""blnet status "" + std::to_string(status);
    if (g_shim.last_error) {
        char* type = nullptr; char* m = nullptr;
        if (g_shim.last_error(&type, &m) == BLNET_OK) {
            if (type) { msg += "" [""; msg += type; msg += ""]""; g_shim.free_(type); }
            if (m)    { msg += "": ""; msg += m;   g_shim.free_(m); }
        }
    }
    throw std::runtime_error(msg);
}

/* ---- §9.2/§11.1 (P2a-2 Task 7a): the GENERATED-PROXY status check. Converts a non-OK
   status into the typed BasicLang::NetException — the last-error TYPE field carries the
   thrown type's ';'-separated fully-qualified inheritance chain (most-derived first), and
   what() is the RAW managed message — so BasicLang typed Catch ladders match per §11.1
   and `ex.Message` is the managed message verbatim (parity with the C# leg). P0's
   NetCheck above predates the typed ladder and composes a diagnostic string; the frozen
   P0 conformance suite pins that exact shape, so it stays untouched for hand-written
   hosts while every generated proxy calls this one. Without an error channel (or an
   empty type) this degrades to NetCheck's plain runtime_error shape. ---- */
inline void NetCheckTyped(int32_t status) {
    if (status == BLNET_OK) return;
    std::string chain, message;
    if (g_shim.last_error) {
        char* type = nullptr; char* m = nullptr;
        if (g_shim.last_error(&type, &m) == BLNET_OK) {
            if (type) { chain = type;   if (g_shim.free_) g_shim.free_(type); }
            if (m)    { message = m;    if (g_shim.free_) g_shim.free_(m); }
        }
    }
    if (!chain.empty())
        throw ::BasicLang::NetException(
            chain, message.empty() ? (""blnet status "" + std::to_string(status)) : message);
    throw std::runtime_error(""blnet status "" + std::to_string(status));
}

/* ---- C2: NetRef. The class DEFINITION moved to the always-emitted BasicLang runtime at
   the P2a-2 flip (D-P7: generated declaration positions hold a NetRef with no shim
   present) and is spliced ABOVE this namespace from the same single source, behind an
   include guard — a TU that sees both this header and a generated runtime header gets
   exactly one definition. This namespace re-exports the name (BasicLang::blnet::NetRef
   stays a valid spelling for the harness, proxies and P2b) and binds the class's
   addref/release hooks to g_shim, preserving the original semantics exactly:
   destruction releases when a shim is bound (release status deliberately ignored, as
   before), Duplicate does a CHECKED addref (NetCheck throws on failure), and both no-op
   on handle 0 / unbound shim. Binding happens in a static initializer so hand-written
   hosts (the conformance harness fills g_shim directly, never calling blnet_bind_core)
   get working hooks without a call-site change; the wrappers read g_shim at CALL time,
   so initialization order vs g_shim is immaterial. ---- */
using ::BasicLang::NetRef;

inline void netref_addref_via_shim(std::uint64_t h) { if (g_shim.addref) NetCheck(g_shim.addref(h)); }
inline void netref_release_via_shim(std::uint64_t h) { if (g_shim.release) g_shim.release(h); }
namespace detail {
    struct NetRefHookBinder {
        NetRefHookBinder() {
            ::BasicLang::g_netref_addref = &netref_addref_via_shim;
            ::BasicLang::g_netref_release = &netref_release_via_shim;
        }
    };
    inline NetRefHookBinder g_netref_hook_binder;
}

/* ---- C5: callback table (generation-tagged, mirrors C2) ---- */
using NativeCallbackFn = std::function<int32_t(const uint64_t* args, int32_t argc, uint64_t* result)>;

struct CallbackFlags { bool result_bearing = false; bool immediate = false; };

namespace detail {
    struct CallbackEntry {
        NativeCallbackFn fn; std::vector<BlnetSlotDesc> slots;
        uint32_t generation = 1; CallbackFlags flags{}; bool alive = false;
    };
    struct QueuedInvocation {
        blnet_callback handle{};
        std::vector<uint64_t> args;
        /* deep-copied storage owned by the queue (freed by the pump after execution) */
        std::vector<std::unique_ptr<char[]>> owned_strings;
        std::vector<std::vector<unsigned char>> owned_structs;
        std::vector<blnet_handle> owned_handles; /* addref'd at enqueue */
    };
    inline std::mutex g_cb_mutex;
    inline std::vector<CallbackEntry> g_callbacks;      /* index 0 reserved */
    inline std::vector<uint32_t> g_cb_freelist;
    inline std::mutex g_queue_mutex;
    inline std::deque<QueuedInvocation> g_queue;
    inline std::atomic<bool> g_pumping{false};
    inline thread_local std::string g_native_error;      /* pending native-exception message */
    inline void (*g_error_hook)(int32_t, const char*) = nullptr;

    inline CallbackEntry* lookup(blnet_callback h, uint32_t* out_index) {
        uint32_t index = static_cast<uint32_t>(h & 0xFFFFFFFFu);
        uint32_t gen   = static_cast<uint32_t>(h >> 32);
        if (index == 0 || index >= g_callbacks.size()) return nullptr;
        auto& e = g_callbacks[index];
        if (!e.alive || e.generation != gen) return nullptr;
        if (out_index) *out_index = index;
        return &e;
    }
}

inline blnet_callback blnet_register_callback(
    NativeCallbackFn fn, const BlnetSlotDesc* slots, int32_t argc, CallbackFlags flags) {
    std::lock_guard<std::mutex> lk(detail::g_cb_mutex);
    if (detail::g_callbacks.empty()) detail::g_callbacks.emplace_back(); /* burn index 0 */
    uint32_t index;
    if (!detail::g_cb_freelist.empty()) { index = detail::g_cb_freelist.back(); detail::g_cb_freelist.pop_back(); }
    else { index = static_cast<uint32_t>(detail::g_callbacks.size()); detail::g_callbacks.emplace_back(); }
    auto& e = detail::g_callbacks[index];
    e.fn = std::move(fn); e.flags = flags; e.alive = true;
    e.slots.clear();
    if (argc > 0) e.slots.assign(slots, slots + argc); /* guard: zero-arg registration may pass slots == nullptr */
    return (static_cast<uint64_t>(e.generation) << 32) | index;
}

inline int32_t blnet_callback_release(blnet_callback h) {
    std::lock_guard<std::mutex> lk(detail::g_cb_mutex);
    uint32_t index;
    auto* e = detail::lookup(h, &index);
    if (!e) return BLNET_E_STALE_CALLBACK;
    e->alive = false; e->fn = nullptr; ++e->generation;    /* generation bumps on free (C2 rule mirrored) */
    detail::g_cb_freelist.push_back(index);
    return BLNET_OK;
}

inline void blnet_set_error_hook(void (*hook)(int32_t, const char*)) { detail::g_error_hook = hook; }

/* invoke inline, translating native exceptions per C4 */
inline int32_t invoke_entry(detail::CallbackEntry& e, const uint64_t* args, int32_t argc, uint64_t* result) {
    try { return e.fn(args, argc, result); }
    catch (const std::exception& ex) { detail::g_native_error = ex.what(); return BLNET_E_NATIVE_EXCEPTION; }
    catch (...) { detail::g_native_error = ""unknown native exception""; return BLNET_E_NATIVE_EXCEPTION; }
}

/* C5: THE universal thunk — managed code holds exactly this function pointer. */
inline int32_t BLNET_CALL blnet_invoke_callback(
    uint64_t callback_handle, const uint64_t* args, int32_t argc, uint64_t* result) {
    detail::CallbackEntry snapshot; /* copy under lock, invoke outside it */
    {
        std::lock_guard<std::mutex> lk(detail::g_cb_mutex);
        auto* e = detail::lookup(callback_handle, nullptr);
        if (!e) return BLNET_E_STALE_CALLBACK;
        snapshot = *e;
    }
    const bool same_thread = g_call_depth > 0;
    if (same_thread || snapshot.flags.immediate)
        return invoke_entry(snapshot, args, argc, result);
    if (snapshot.flags.result_bearing)
        return BLNET_E_CROSS_THREAD_RESULT;
    /* queued fire-and-forget notification: deep-copy per slot descriptors */
    detail::QueuedInvocation q; q.handle = callback_handle; q.args.assign(args, args + argc);
    for (int32_t i = 0; i < argc; ++i) {
        switch (snapshot.slots[i].kind) {
            case BLNET_SLOT_STRING: {
                const char* s = reinterpret_cast<const char*>(args[i]);
                size_t n = s ? std::strlen(s) + 1 : 1;
                auto buf = std::make_unique<char[]>(n);
                std::memcpy(buf.get(), s ? s : """", n);
                q.args[i] = reinterpret_cast<uint64_t>(buf.get());
                q.owned_strings.push_back(std::move(buf));
                break;
            }
            case BLNET_SLOT_STRUCT: {
                if (!args[i]) break; /* null struct pointer: tolerated as a null slot, no copy */
                auto size = static_cast<size_t>(snapshot.slots[i].size);
                std::vector<unsigned char> buf(size);
                std::memcpy(buf.data(), reinterpret_cast<const void*>(args[i]), size);
                q.args[i] = reinterpret_cast<uint64_t>(buf.data());
                q.owned_structs.push_back(std::move(buf));
                break;
            }
            case BLNET_SLOT_HANDLE: {
                if (args[i] && g_shim.addref) {
                    int32_t st = g_shim.addref(args[i]);
                    if (st != BLNET_OK) {
                        /* stale at enqueue fails the invocation immediately — but first
                           release the refs already taken for EARLIER handle slots, or
                           they leak (the queue never sees this invocation). */
                        for (auto h : q.owned_handles) if (g_shim.release) g_shim.release(h);
                        return st;
                    }
                    q.owned_handles.push_back(args[i]);
                }
                break;
            }
            default: break; /* BLNET_SLOT_VALUE: already in q.args */
        }
    }
    { std::lock_guard<std::mutex> lk(detail::g_queue_mutex); detail::g_queue.push_back(std::move(q)); }
    return BLNET_OK;
}

/* C4/C5: drain the queue on the pump thread. Continues on failure; hook fires per
   failure; returns the FIRST failure's status. Reentry is a defined failure. */
inline int32_t blnet_pump() {
    bool expected = false;
    if (!detail::g_pumping.compare_exchange_strong(expected, true)) return BLNET_E_PUMP_REENTRY;
    int32_t first_failure = BLNET_OK;
    for (;;) {
        detail::QueuedInvocation q;
        {
            std::lock_guard<std::mutex> lk(detail::g_queue_mutex);
            if (detail::g_queue.empty()) break;
            q = std::move(detail::g_queue.front()); detail::g_queue.pop_front();
        }
        int32_t st;
        detail::CallbackEntry snapshot;
        {
            std::lock_guard<std::mutex> lk(detail::g_cb_mutex);
            auto* e = detail::lookup(q.handle, nullptr);
            st = e ? BLNET_OK : BLNET_E_STALE_CALLBACK;
            if (e) snapshot = *e;
        }
        if (st == BLNET_OK)
            st = invoke_entry(snapshot, q.args.data(), static_cast<int32_t>(q.args.size()), nullptr);
        if (st != BLNET_OK) {
            if (first_failure == BLNET_OK) first_failure = st;
            /* a throwing hook must not leave g_pumping stuck true */
            if (detail::g_error_hook)
                try { detail::g_error_hook(st, detail::g_native_error.c_str()); } catch (...) {}
        }
        for (auto h : q.owned_handles) if (g_shim.release) g_shim.release(h);
        /* owned_strings / owned_structs free when q goes out of scope — queue-owned storage, pump-freed */
    }
    detail::g_pumping.store(false);
    return first_failure;
}

/* Vtable entry: shim pulls the pending native-exception message (blnet_alloc'd; shim frees). */
inline int32_t BLNET_CALL blnet_get_native_error(char** message) {
    if (!message) return BLNET_E_ALLOC;
    const auto& s = detail::g_native_error;
    char* buf = static_cast<char*>(g_shim.alloc ? g_shim.alloc(static_cast<int64_t>(s.size() + 1)) : nullptr);
    if (!buf) { *message = nullptr; return BLNET_E_ALLOC; }
    std::memcpy(buf, s.c_str(), s.size() + 1);
    *message = buf;
    return BLNET_OK;
}

/* ---- P2a §9.3: the native side of P0's 2-slot POSITIONAL vtable ----
   Slot order must match BlnetNativeVtable's declaration in blnet.h: swapping the two
   silently routes every callback invocation into the error-message puller instead.
   Both members are defined above in this same header, so this table is always fully
   bound — unlike g_shim, which the generated startup TU fills at run time. */
inline BlnetNativeVtable g_native_vtable{ &blnet_invoke_callback, &blnet_get_native_error };

/* ---- P2a §9.3: module loading + core binding ----
   blnet_load_module / blnet_get_symbol / blnet_load_error are DECLARED here and
   DEFINED at the bottom of this header, behind BLNET_IMPLEMENT_LOADER. Exactly one
   translation unit — the generated blnet_startup.g.cpp — defines that macro, so
   <windows.h> / <dlfcn.h> never reach a TU that also contains generated BasicLang
   code. That confinement is not tidiness: windows.h macro-replaces ordinary
   identifiers (CreateFile, DeleteFile, CopyFile, min, max, ...) and would break a
   user program whose only sin is naming a Sub `DeleteFile`.

   ORDERING HAZARD: this header is #pragma once, so a TU that includes it WITHOUT the
   macro and then defines the macro and includes it again gets nothing. Define
   BLNET_IMPLEMENT_LOADER before the implementing TU's FIRST include of this header. */
void* blnet_load_module(const char* name);
void* blnet_get_symbol(void* module, const char* name);
/* Why the last blnet_load_module returned null: an OS error code (Windows) or the
   dlerror text (POSIX). Empty when nothing has failed. Never null. */
const char* blnet_load_error();

namespace detail { inline thread_local std::string g_load_error; }

/* Binds P0's seven core exports (blnet.h's BLNET_EXPORT_* names) into g_shim.
   TRANSPORT-NEUTRAL: it needs only blnet_get_symbol, which P2b redefines over
   hostfxr's delegate lookup.

   Returns nullptr on success, or the FIRST export name that would not resolve.
   Deliberately NOT an int32_t status: the contract has no ""missing export"" code, and
   adding one bumps BLNET_ABI_VERSION (contract rule C7), which would invalidate P0's
   frozen conformance shim. The caller needs the NAME anyway — §9.3's normative message
   quotes it. */
inline const char* blnet_bind_core(void* module) {
    void* p = blnet_get_symbol(module, BLNET_EXPORT_ABI_VERSION);
    if (!p) return BLNET_EXPORT_ABI_VERSION;
    g_shim.abi_version = reinterpret_cast<int32_t (BLNET_CALL *)(void)>(p);

    p = blnet_get_symbol(module, BLNET_EXPORT_INITIALIZE);
    if (!p) return BLNET_EXPORT_INITIALIZE;
    g_shim.initialize = reinterpret_cast<int32_t (BLNET_CALL *)(int32_t, const BlnetNativeVtable*)>(p);

    p = blnet_get_symbol(module, BLNET_EXPORT_ADDREF);
    if (!p) return BLNET_EXPORT_ADDREF;
    g_shim.addref = reinterpret_cast<int32_t (BLNET_CALL *)(blnet_handle)>(p);

    p = blnet_get_symbol(module, BLNET_EXPORT_RELEASE);
    if (!p) return BLNET_EXPORT_RELEASE;
    g_shim.release = reinterpret_cast<int32_t (BLNET_CALL *)(blnet_handle)>(p);

    p = blnet_get_symbol(module, BLNET_EXPORT_ALLOC);
    if (!p) return BLNET_EXPORT_ALLOC;
    g_shim.alloc = reinterpret_cast<void* (BLNET_CALL *)(int64_t)>(p);

    p = blnet_get_symbol(module, BLNET_EXPORT_FREE);
    if (!p) return BLNET_EXPORT_FREE;
    g_shim.free_ = reinterpret_cast<void (BLNET_CALL *)(void*)>(p);

    p = blnet_get_symbol(module, BLNET_EXPORT_LAST_ERROR);
    if (!p) return BLNET_EXPORT_LAST_ERROR;
    g_shim.last_error = reinterpret_cast<int32_t (BLNET_CALL *)(char**, char**)>(p);

    return nullptr;
}

}} /* namespace BasicLang::blnet */

/* ---- Platform loader definitions — see blnet_bind_core's remarks above. ---- */
#ifdef BLNET_IMPLEMENT_LOADER
#if defined(_WIN32)
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#else
#include <dlfcn.h>
#endif

namespace BasicLang { namespace blnet {

void* blnet_load_module(const char* name) {
#if defined(_WIN32)
    HMODULE m = ::LoadLibraryA(name);
    if (!m) detail::g_load_error =
        ""os error "" + std::to_string(static_cast<unsigned long>(::GetLastError()));
    return reinterpret_cast<void*>(m);
#else
    void* m = ::dlopen(name, RTLD_NOW | RTLD_LOCAL);
    if (!m) { const char* e = ::dlerror(); detail::g_load_error = e ? e : ""dlopen failed""; }
    return m;
#endif
}

void* blnet_get_symbol(void* module, const char* name) {
#if defined(_WIN32)
    return reinterpret_cast<void*>(::GetProcAddress(reinterpret_cast<HMODULE>(module), name));
#else
    return ::dlsym(module, name);
#endif
}

const char* blnet_load_error() { return detail::g_load_error.c_str(); }

}} /* namespace BasicLang::blnet */
#endif /* BLNET_IMPLEMENT_LOADER */
";
    }
}
