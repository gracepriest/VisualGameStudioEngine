namespace BasicLang.Compiler.CodeGen.CPlusPlus
{
    /// <summary>
    /// Single source of truth for the C++ runtime support code emitted by the C++ backend
    /// (companion to <see cref="CppCollectionsRuntime"/>, which owns the collection wrappers).
    /// Each const is pre-indented exactly as the combined translation unit renders it and is
    /// spliced line-by-line into BOTH the single-string <c>Generate</c> output and the split
    /// runtime header (<c>BasicLangRuntime.g.h</c>), so the two emission modes can never drift.
    /// </summary>
    public static class CppRuntimeSources
    {
        /// <summary>
        /// Minimal .NET-surface runtime helpers (DateTime.Now / ToString(fmt)) in
        /// <c>namespace BasicLangRt</c>. Ends with a blank line (the separator the
        /// combined-output emission always wrote).
        /// </summary>
        public const string DotNetSurfaceHelpers = @"// Minimal .NET-surface runtime (DateTime helpers)
namespace BasicLangRt {
    inline std::time_t Now() { return std::time(nullptr); }
    inline std::string FormatTime(std::time_t t, const std::string& netFormat = """") {
        std::string fmt = netFormat.empty() ? std::string(""%Y-%m-%d %H:%M:%S"") : netFormat;
        if (!netFormat.empty()) {
            auto replaceAll = [&fmt](const std::string& from, const std::string& to) {
                size_t pos = 0;
                while ((pos = fmt.find(from, pos)) != std::string::npos) { fmt.replace(pos, from.size(), to); pos += to.size(); }
            };
            // lowercase tokens first so mm/MM cannot interfere
            replaceAll(""yyyy"", ""%Y""); replaceAll(""ss"", ""%S""); replaceAll(""mm"", ""%M"");
            replaceAll(""dd"", ""%d""); replaceAll(""HH"", ""%H""); replaceAll(""MM"", ""%m"");
        }
        std::tm tmv{};
        #ifdef _WIN32
        localtime_s(&tmv, &t);
        #else
        localtime_r(&t, &tmv);
        #endif
        char buf[128];
        std::strftime(buf, sizeof(buf), fmt.c_str(), &tmv);
        return std::string(buf);
    }
}
";

        /// <summary>
        /// Synchronous <c>Task&lt;T&gt;</c> emulation body. Emitted INSIDE an already-open
        /// <c>namespace BasicLang {</c> block (one indent level baked in). Ends with a blank
        /// line so async-only and async+iterator layouts match the historical output.
        /// </summary>
        public const string TaskEmulation = @"    // Synchronous Task<T> emulation: type-correct, no scheduler
    template <typename T> struct Task {
        T Value;
        T get() const { return Value; }
    };
    template <> struct Task<void> { void get() const { } };
";

        /// <summary>
        /// C++20 coroutine <c>Generator&lt;T&gt;</c> body. Emitted INSIDE an already-open
        /// <c>namespace BasicLang {</c> block (one indent level baked in). No trailing blank
        /// line — the namespace close follows immediately, matching the historical output.
        /// </summary>
        public const string GeneratorCoroutine = @"    template <typename T> struct Generator {
        struct promise_type {
            T current;
            Generator get_return_object() { return Generator{ std::coroutine_handle<promise_type>::from_promise(*this) }; }
            std::suspend_always initial_suspend() noexcept { return {}; }
            std::suspend_always final_suspend() noexcept { return {}; }
            std::suspend_always yield_value(T v) { current = v; return {}; }
            void return_void() {}
            void unhandled_exception() { std::terminate(); }
        };
        std::coroutine_handle<promise_type> h;
        Generator() : h(nullptr) {}
        explicit Generator(std::coroutine_handle<promise_type> handle) : h(handle) {}
        Generator(Generator&& other) noexcept : h(other.h) { other.h = nullptr; }
        Generator(const Generator&) = delete;
        Generator& operator=(Generator&& other) noexcept { if (this != &other) { if (h) h.destroy(); h = other.h; other.h = nullptr; } return *this; }
        ~Generator() { if (h) h.destroy(); }
        struct iterator {
            std::coroutine_handle<promise_type> h;
            iterator& operator++() { h.resume(); return *this; }
            T operator*() const { return h.promise().current; }
            bool operator!=(std::default_sentinel_t) const { return !h.done(); }
        };
        iterator begin() { h.resume(); return iterator{ h }; }
        std::default_sentinel_t end() { return {}; }
    };";
    }
}
