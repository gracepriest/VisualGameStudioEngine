using BasicLang.Compiler.CodeGen.CPlusPlus;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Blnet;

/// <summary>
/// Compile-smokes the blnet native runtime headers (<see cref="BlnetRuntimeSources"/>)
/// against a real C++ compiler, then asserts native-only runtime conformance of the
/// callback table / queue / pump (no .NET shim involved): each test compiles a small
/// <c>main</c> that registers native lambdas, drives <c>blnet_invoke_callback</c> from
/// <c>main</c> and from <c>std::thread</c>s, and prints markers the C# side asserts.
/// </summary>
[TestFixture]
[Category("Integration")]
public class BlnetNativeRuntimeTests
{
    private (string exe, string argsTemplate)? _compiler;

    [OneTimeSetUp]
    public void FindCompiler() => _compiler = Native.CppCompile.FindRunCompiler();

    private string Run(string mainBody)
    {
        if (_compiler is null) Assert.Ignore("No C++ compiler available");
        var src = "#include \"blnet_runtime.hpp\"\n#include <cstdio>\n" + mainBody;
        return Native.CppCompile.CompileAndRun(src, _compiler!.Value, new Dictionary<string, string>
        {
            ["blnet.h"] = BlnetRuntimeSources.BlnetHeader,
            ["blnet_runtime.hpp"] = BlnetRuntimeSources.BlnetRuntime,
        });
    }

    [Test]
    public void HeadersCompileStandalone() =>
        Assert.That(Run("int main(){ printf(\"ok\"); return 0; }"), Is.EqualTo("ok"));

    // ---- Native-only behavioral conformance (callback table, queue, pump) ----
    // Markers are printed as value-independent comparisons (e.g. "stale=%d", st == BLNET_E_STALE_CALLBACK)
    // so the C# assertions never hard-code status values.

    /// <summary>Depth &gt; 0 via <c>BlnetCallScope</c> means the thunk dispatches inline, synchronously.</summary>
    [Test]
    public void SameThreadInlineDispatch()
    {
        var output = Run(@"
using namespace BasicLang::blnet;
static int g_flag = 0;
int main() {
    blnet_callback cb = blnet_register_callback(
        [](const uint64_t*, int32_t, uint64_t*) -> int32_t { g_flag = 1; return BLNET_OK; },
        nullptr, 0, CallbackFlags{});
    BlnetCallScope scope; /* depth > 0 => same-thread inline dispatch */
    int32_t st = blnet_invoke_callback(cb, nullptr, 0, nullptr);
    printf(""st_ok=%d\n"", st == BLNET_OK);
    printf(""inline=%d\n"", g_flag); /* flag already set: ran inline, not queued */
    return 0;
}
");
        Assert.That(output, Does.Contain("st_ok=1"));
        Assert.That(output, Does.Contain("inline=1"));
    }

    /// <summary>Invoking or re-releasing a released callback handle fails with the stale-callback status.</summary>
    [Test]
    public void ReleasedCallbackIsStale()
    {
        var output = Run(@"
using namespace BasicLang::blnet;
int main() {
    blnet_callback cb = blnet_register_callback(
        [](const uint64_t*, int32_t, uint64_t*) -> int32_t { return BLNET_OK; },
        nullptr, 0, CallbackFlags{});
    printf(""rel_ok=%d\n"", blnet_callback_release(cb) == BLNET_OK);
    int32_t st = blnet_invoke_callback(cb, nullptr, 0, nullptr);
    printf(""invoke_stale=%d\n"", st == BLNET_E_STALE_CALLBACK);
    printf(""double_release_stale=%d\n"", blnet_callback_release(cb) == BLNET_E_STALE_CALLBACK);
    return 0;
}
");
        Assert.That(output, Does.Contain("rel_ok=1"));
        Assert.That(output, Does.Contain("invoke_stale=1"));
        Assert.That(output, Does.Contain("double_release_stale=1"));
    }

    /// <summary>
    /// After release + re-register the slot index is reused with a bumped generation:
    /// the old handle stays dead, the new handle works.
    /// </summary>
    [Test]
    public void GenerationTagKillsOldHandleAfterSlotReuse()
    {
        var output = Run(@"
using namespace BasicLang::blnet;
static int g_new_ran = 0;
int main() {
    blnet_callback old_cb = blnet_register_callback(
        [](const uint64_t*, int32_t, uint64_t*) -> int32_t { return BLNET_OK; },
        nullptr, 0, CallbackFlags{});
    blnet_callback_release(old_cb);
    blnet_callback new_cb = blnet_register_callback(
        [](const uint64_t*, int32_t, uint64_t*) -> int32_t { g_new_ran = 1; return BLNET_OK; },
        nullptr, 0, CallbackFlags{});
    /* freelist reuse: same low-32 index, different high-32 generation */
    printf(""slot_reused=%d\n"", (uint32_t)(old_cb & 0xFFFFFFFFu) == (uint32_t)(new_cb & 0xFFFFFFFFu));
    printf(""gen_differs=%d\n"", (uint32_t)(old_cb >> 32) != (uint32_t)(new_cb >> 32));
    BlnetCallScope scope;
    printf(""old_stale=%d\n"", blnet_invoke_callback(old_cb, nullptr, 0, nullptr) == BLNET_E_STALE_CALLBACK);
    printf(""new_ok=%d\n"", blnet_invoke_callback(new_cb, nullptr, 0, nullptr) == BLNET_OK);
    printf(""new_ran=%d\n"", g_new_ran);
    return 0;
}
");
        Assert.That(output, Does.Contain("slot_reused=1"));
        Assert.That(output, Does.Contain("gen_differs=1"));
        Assert.That(output, Does.Contain("old_stale=1"));
        Assert.That(output, Does.Contain("new_ok=1"));
        Assert.That(output, Does.Contain("new_ran=1"));
    }

    /// <summary>
    /// A notification invoked from a foreign thread (depth 0, no Immediate flag) queues;
    /// it does not run until <c>blnet_pump()</c> drains it on the pump thread.
    /// </summary>
    [Test]
    public void CrossThreadNotificationQueuesUntilPump()
    {
        var output = Run(@"
#include <thread>
using namespace BasicLang::blnet;
static int g_flag = 0;
int main() {
    blnet_callback cb = blnet_register_callback(
        [](const uint64_t*, int32_t, uint64_t*) -> int32_t { g_flag = 1; return BLNET_OK; },
        nullptr, 0, CallbackFlags{});
    int32_t st = -1;
    std::thread t([&] { st = blnet_invoke_callback(cb, nullptr, 0, nullptr); });
    t.join();
    printf(""invoke_ok=%d\n"", st == BLNET_OK);
    printf(""before_pump=%d\n"", g_flag); /* queued, must NOT have run yet */
    printf(""pump_ok=%d\n"", blnet_pump() == BLNET_OK);
    printf(""after_pump=%d\n"", g_flag);
    return 0;
}
");
        Assert.That(output, Does.Contain("invoke_ok=1"));
        Assert.That(output, Does.Contain("before_pump=0"));
        Assert.That(output, Does.Contain("pump_ok=1"));
        Assert.That(output, Does.Contain("after_pump=1"));
    }

    /// <summary>
    /// A result-bearing callback invoked cross-thread (without Immediate) is rejected with
    /// <c>BLNET_E_CROSS_THREAD_RESULT</c> and never runs — not even on a later pump.
    /// </summary>
    [Test]
    public void CrossThreadResultBearingIsRejected()
    {
        var output = Run(@"
#include <thread>
using namespace BasicLang::blnet;
static int g_ran = 0;
int main() {
    CallbackFlags flags; flags.result_bearing = true;
    blnet_callback cb = blnet_register_callback(
        [](const uint64_t*, int32_t, uint64_t* result) -> int32_t { g_ran = 1; if (result) *result = 42; return BLNET_OK; },
        nullptr, 0, flags);
    int32_t st = -1;
    uint64_t result = 0;
    std::thread t([&] { st = blnet_invoke_callback(cb, nullptr, 0, &result); });
    t.join();
    printf(""rejected=%d\n"", st == BLNET_E_CROSS_THREAD_RESULT);
    printf(""ran=%d\n"", g_ran);
    printf(""pump_empty_ok=%d\n"", blnet_pump() == BLNET_OK); /* nothing was queued */
    printf(""ran_after_pump=%d\n"", g_ran);
    return 0;
}
");
        Assert.That(output, Does.Contain("rejected=1"));
        Assert.That(output, Does.Contain("ran=0"));
        Assert.That(output, Does.Contain("pump_empty_ok=1"));
        Assert.That(output, Does.Contain("ran_after_pump=0"));
    }

    /// <summary>
    /// The Immediate flag lets a result-bearing callback run inline on the calling
    /// (foreign) thread; the result slot is filled before the invoke returns.
    /// </summary>
    [Test]
    public void ImmediateFlagRunsInlineOnForeignThread()
    {
        var output = Run(@"
#include <thread>
using namespace BasicLang::blnet;
static int g_ran = 0;
int main() {
    CallbackFlags flags; flags.result_bearing = true; flags.immediate = true;
    blnet_callback cb = blnet_register_callback(
        [](const uint64_t*, int32_t, uint64_t* result) -> int32_t { g_ran = 1; if (result) *result = 42; return BLNET_OK; },
        nullptr, 0, flags);
    int32_t st = -1;
    uint64_t result = 0;
    std::thread t([&] {
        st = blnet_invoke_callback(cb, nullptr, 0, &result);
        printf(""ran_on_thread=%d\n"", g_ran); /* before join: proves it ran on THIS thread */
    });
    t.join();
    printf(""st_ok=%d\n"", st == BLNET_OK);
    printf(""result=%d\n"", (int)result);
    return 0;
}
");
        Assert.That(output, Does.Contain("ran_on_thread=1"));
        Assert.That(output, Does.Contain("st_ok=1"));
        Assert.That(output, Does.Contain("result=42"));
    }

    /// <summary>
    /// The pump drains the whole queue even through failures: all entries are attempted,
    /// the error hook fires once per failure with the exception message, the return value
    /// is the FIRST failure's status, and the queue is empty afterwards.
    /// </summary>
    [Test]
    public void PumpDrainsFullyAndReportsFirstFailure()
    {
        var output = Run(@"
#include <thread>
#include <string>
#include <stdexcept>
using namespace BasicLang::blnet;
static int g_attempted = 0;
static int g_hook_count = 0;
static std::string g_hook_msgs;
int main() {
    blnet_callback ok_cb = blnet_register_callback(
        [](const uint64_t*, int32_t, uint64_t*) -> int32_t { ++g_attempted; return BLNET_OK; },
        nullptr, 0, CallbackFlags{});
    blnet_callback boom2_cb = blnet_register_callback(
        [](const uint64_t*, int32_t, uint64_t*) -> int32_t { ++g_attempted; throw std::runtime_error(""boom2""); },
        nullptr, 0, CallbackFlags{});
    blnet_callback boom3_cb = blnet_register_callback(
        [](const uint64_t*, int32_t, uint64_t*) -> int32_t { ++g_attempted; throw std::runtime_error(""boom3""); },
        nullptr, 0, CallbackFlags{});
    blnet_set_error_hook([](int32_t, const char* msg) { ++g_hook_count; g_hook_msgs += msg; g_hook_msgs += "";""; });
    std::thread t([&] {
        blnet_invoke_callback(ok_cb, nullptr, 0, nullptr);
        blnet_invoke_callback(boom2_cb, nullptr, 0, nullptr);
        blnet_invoke_callback(boom3_cb, nullptr, 0, nullptr);
    });
    t.join();
    int32_t pst = blnet_pump();
    printf(""attempted=%d\n"", g_attempted);
    printf(""hooks=%d\n"", g_hook_count);
    printf(""first_failure_native=%d\n"", pst == BLNET_E_NATIVE_EXCEPTION);
    printf(""msgs=%s\n"", g_hook_msgs.c_str());
    printf(""second_pump_ok=%d\n"", blnet_pump() == BLNET_OK); /* queue fully drained */
    printf(""attempted_after=%d\n"", g_attempted);             /* nothing re-ran */
    return 0;
}
");
        Assert.That(output, Does.Contain("attempted=3"));
        Assert.That(output, Does.Contain("hooks=2"));
        Assert.That(output, Does.Contain("first_failure_native=1"));
        Assert.That(output, Does.Contain("msgs=boom2;boom3;"));
        Assert.That(output, Does.Contain("second_pump_ok=1"));
        Assert.That(output, Does.Contain("attempted_after=3"));
    }

    /// <summary>
    /// Calling <c>blnet_pump()</c> from inside a callback executing on the pump thread
    /// (i.e. during a drain) is a defined failure: <c>BLNET_E_PUMP_REENTRY</c>.
    /// </summary>
    [Test]
    public void PumpReentryFromInsideCallbackFails()
    {
        var output = Run(@"
#include <thread>
using namespace BasicLang::blnet;
static int g_inner_status = -1;
int main() {
    blnet_callback cb = blnet_register_callback(
        [](const uint64_t*, int32_t, uint64_t*) -> int32_t { g_inner_status = blnet_pump(); return BLNET_OK; },
        nullptr, 0, CallbackFlags{});
    std::thread t([&] { blnet_invoke_callback(cb, nullptr, 0, nullptr); }); /* queue it */
    t.join();
    int32_t pst = blnet_pump(); /* drain runs the callback, which re-enters the pump */
    printf(""outer_ok=%d\n"", pst == BLNET_OK);
    printf(""inner_reentry=%d\n"", g_inner_status == BLNET_E_PUMP_REENTRY);
    return 0;
}
");
        Assert.That(output, Does.Contain("outer_ok=1"));
        Assert.That(output, Does.Contain("inner_reentry=1"));
    }

    /// <summary>
    /// A <c>BLNET_SLOT_STRING</c> argument is deep-copied at enqueue time: the callback
    /// sees the original bytes (non-ASCII included) even after the caller's buffer is
    /// overwritten between enqueue and pump.
    /// </summary>
    [Test]
    public void StringArgumentDeepCopiedAtEnqueue()
    {
        var output = Run(@"
#include <thread>
#include <cstring>
#include <string>
using namespace BasicLang::blnet;
static std::string g_seen;
int main() {
    BlnetSlotDesc slots[1] = { { BLNET_SLOT_STRING, 0 } };
    blnet_callback cb = blnet_register_callback(
        [](const uint64_t* args, int32_t, uint64_t*) -> int32_t {
            g_seen = reinterpret_cast<const char*>(args[0]);
            return BLNET_OK;
        }, slots, 1, CallbackFlags{});
    char buffer[32];
    std::strcpy(buffer, ""h\xC3\xA9llo""); /* UTF-8 bytes via narrow-literal escapes (u8 would be char8_t) */
    std::thread t([&] {
        uint64_t args[1] = { reinterpret_cast<uint64_t>(buffer) };
        printf(""queued_ok=%d\n"", blnet_invoke_callback(cb, args, 1, nullptr) == BLNET_OK);
    });
    t.join();
    std::strcpy(buffer, ""CLOBBERED""); /* the queue must not see this */
    printf(""pump_ok=%d\n"", blnet_pump() == BLNET_OK);
    printf(""deep_copy=%d\n"", g_seen == ""h\xC3\xA9llo"");
    printf(""len=%d\n"", (int)g_seen.size());
    return 0;
}
");
        Assert.That(output, Does.Contain("queued_ok=1"));
        Assert.That(output, Does.Contain("pump_ok=1"));
        Assert.That(output, Does.Contain("deep_copy=1"));
        Assert.That(output, Does.Contain("len=6")); // h + 2-byte e-acute + llo
    }
}
