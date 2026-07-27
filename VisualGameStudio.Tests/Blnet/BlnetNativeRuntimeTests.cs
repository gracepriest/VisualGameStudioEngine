using BasicLang.Compiler.CodeGen.CPlusPlus;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Blnet;

/// <summary>
/// Compile-smokes the blnet native runtime headers (<see cref="BlnetRuntimeSources"/>)
/// against a real C++ compiler. Later tasks build behavioral harnesses on the same
/// helper; this fixture proves the header strings are valid C++20 as emitted.
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
}
