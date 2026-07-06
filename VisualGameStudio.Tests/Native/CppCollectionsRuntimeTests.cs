using NUnit.Framework;
using BasicLang.Compiler.CodeGen.CPlusPlus;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Isolated proof that the <c>BasicLang::List/Dictionary/HashSet</c> wrapper runtime
/// (<see cref="CppCollectionsRuntime.Source"/>) behaves correctly. Builds a small C++ program
/// = standard headers + the wrapper source + a <c>main</c> exercising the semantics, compiles
/// it to a real executable, runs it, and asserts stdout exactly.
///
/// No compiler wiring is involved here — this validates the runtime const in isolation. If no
/// C++ compiler is available (e.g. CI without a toolchain), the test is cleanly Ignored.
/// </summary>
[TestFixture]
public class CppCollectionsRuntimeTests
{
    [Test]
    public void Wrappers_CompileAndRun_WithExpectedSemantics()
    {
        var compiler = CppCompile.FindRunCompiler();
        if (compiler is null)
            Assert.Ignore("No C++ compiler (clang++/g++/MSVC) available; skipping native runtime test.");

        var program =
            "#include <vector>\n" +
            "#include <unordered_map>\n" +
            "#include <unordered_set>\n" +
            "#include <algorithm>\n" +
            "#include <stdexcept>\n" +
            "#include <cstdint>\n" +
            "#include <string>\n" +
            "#include <iostream>\n" +
            "\n" +
            CppCollectionsRuntime.Source +
            "\n" +
            "int main() {\n" +
            "    // --- List ---\n" +
            "    BasicLang::List<int32_t> list;\n" +
            "    list.Add(10);\n" +
            "    list.Add(20);\n" +
            "    list.Add(30);\n" +
            "    std::cout << list.Count() << \" \" << list[1] << \" \" << list.Contains(20) << \"\\n\";\n" +
            "\n" +
            "    // --- Dictionary ---\n" +
            "    BasicLang::Dictionary<std::string, int32_t> dict;\n" +
            "    dict.Add(\"a\", 1);\n" +
            "    dict.Set(\"b\", 2);\n" +
            "    int32_t got = 0;\n" +
            "    bool ok = dict.TryGetValue(\"b\", got);\n" +
            "    std::cout << dict.Count() << \" \" << dict.ContainsKey(\"a\") << \" \" << ok << \" \" << got << \" \" << dict.Get(\"a\") << \"\\n\";\n" +
            "    bool threw = false;\n" +
            "    try { dict.Get(\"missing\"); } catch (std::runtime_error&) { threw = true; }\n" +
            "    std::cout << threw << \"\\n\";\n" +
            "\n" +
            "    // --- HashSet ---\n" +
            "    BasicLang::HashSet<int32_t> set;\n" +
            "    bool first = set.Add(5);\n" +
            "    bool second = set.Add(5);\n" +
            "    std::cout << first << \" \" << second << \" \" << set.Contains(5) << \" \" << set.Count() << \"\\n\";\n" +
            "    return 0;\n" +
            "}\n";

        var stdout = CppCompile.CompileAndRun(program, compiler!.Value);
        var normalized = stdout.Replace("\r\n", "\n");

        var expected =
            "3 20 1\n" +       // List: Count() [1] Contains(20)
            "2 1 1 2 1\n" +    // Dictionary: Count() ContainsKey("a") TryGetValue ok, got, Get("a")
            "1\n" +            // Dictionary: Get("missing") threw
            "1 0 1 1\n";       // HashSet: Add(5) Add(5) Contains(5) Count()
        Assert.That(normalized, Is.EqualTo(expected));
    }
}
