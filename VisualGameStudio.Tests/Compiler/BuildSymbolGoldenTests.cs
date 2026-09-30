using System;
using System.IO;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.CodeGen.JavaScript;
using BasicLang.Compiler.IR.Optimization;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// O19 — the build now defines WEB/DESKTOP/DEBUG/RELEASE. A program that names none of them must preprocess
/// byte-for-byte as before; one that does changes exactly as listed. No <c>.bas</c>/<c>.mod</c>/<c>.cls</c>, include
/// or template in the repo uses <c>#IfDef</c>/<c>#IfNDef</c> (spec K16, re-grepped for Task 4), so the corpus is every
/// <c>#IfDef</c>/<c>#IfNDef</c> source in the suite (<c>CppPassthroughTests</c> <c>#IfNDef WINDOWS</c>,
/// <c>JavaScriptInteropTests</c> <c>#IfDef NEVER_DEFINED</c>), the shapes the shipped docs teach
/// (<c>docs/BasicLang-Reference.md</c>, <c>docs/articles/basiclang-guide.md</c>, <c>docs/wiki/content/language.md</c>),
/// and shapes users write. "Before" is a preprocessor with no symbol defined, which is what every route did until
/// Task 3.
/// </summary>
[TestFixture]
public class BuildSymbolGoldenTests
{
    private static readonly string[] Corpus =
    {
        "#IfNDef WINDOWS\n#CppInclude <unistd.h>\n#EndIf\nSub Main()\nEnd Sub",
        "#IfDef NEVER_DEFINED\n#JsImport \"./nope.js\"\n#EndIf\nSub Main()\nEnd Sub",
        "#IfDef TRACE\nSub Log()\nEnd Sub\n#Else\nSub Log2()\nEnd Sub\n#EndIf",
        "#Define LOCAL\n#IfDef LOCAL\nSub A()\nEnd Sub\n#EndIf",
        "#If TRACE Or Not LOCAL Then\nSub B()\nEnd Sub\n#ElseIf LOCAL Then\nSub C()\nEnd Sub\n#End If",
        "#Region \"Initialization\"\nSub Init()\nEnd Sub\n#End Region",
    };

    /// <summary>
    /// The docs' own examples NAME <c>DEBUG</c>, but either <c>#Define</c> it themselves or test it under a symbol no
    /// build defines — so they, too, must preprocess identically on every build. A user who copied them is unaffected.
    /// </summary>
    private static readonly string[] NamesABuildSymbolButIsUnaffected =
    {
        "#Define DEBUG\n#IfDef DEBUG\n    PrintLine(\"Debug build\")\n#Else\n    PrintLine(\"Release build\")\n#EndIf",
        "#IfNDef SHIPPING\n    PrintLine(\"Not a shipping build\")\n#EndIf",
        "#IfDef WINDOWS\n  #IfDef DEBUG\n    PrintLine(\"Windows debug\")\n  #EndIf\n#EndIf",
    };

    private static readonly string[] NamesABuildSymbol =
    {
        "#IfDef DEBUG\nSub Trace()\nEnd Sub\n#EndIf",
        "#IfNDef RELEASE\nSub Trace()\nEnd Sub\n#EndIf",
        "#IfDef WEB\nSub OnlyWeb()\nEnd Sub\n#EndIf",
    };

    /// <summary>The preprocessed text AND its errors — a symbol that turned an error into a non-error is a change too.</summary>
    private static string Pre(string source, params string[] symbols)
    {
        var pre = new Preprocessor();
        foreach (var s in symbols) pre.Define(s);
        var text = pre.Process(source, "golden.bas");
        return text + "\n--errors--\n" + string.Join("\n", pre.Errors.Select(e => e.ToString()));
    }

    private static readonly (string Backend, string? Config)[] Builds =
    {
        ("csharp", "Debug"), ("csharp", "Release"), ("javascript", "Debug"), ("javascript", "Release"),
        ("cpp", "Debug"), ("cpp", null),
    };

    [Test]
    public void AProgramNamingNoBuildSymbol_PreprocessesIdentically_OnEveryBuild()
    {
        Assert.Multiple(() =>
        {
            foreach (var source in Corpus.Concat(NamesABuildSymbolButIsUnaffected))
            foreach (var (backend, config) in Builds)
            {
                Assert.That(Pre(source, BuildSymbols.For(backend, config, null).ToArray()), Is.EqualTo(Pre(source)),
                    $"{backend}/{config ?? "none"}: {source.Split('\n')[0]}");
            }
        });
    }

    private static string[] Symbols(string backend, string? config) => BuildSymbols.For(backend, config, null).ToArray();

    /// <summary>
    /// The listed difference: code under #IfDef DEBUG now compiles in Debug builds (the release note). The symbols
    /// come from <see cref="BuildSymbols.For"/>, never typed here, so the list is checked against what builds define.
    /// </summary>
    [Test]
    public void AProgramNamingABuildSymbol_ChangesExactlyAsListed()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Pre(NamesABuildSymbol[0]), Does.Not.Contain("\nSub Trace()"), "before: never compiled");
            Assert.That(Pre(NamesABuildSymbol[0], Symbols("csharp", "Debug")), Does.Contain("\nSub Trace()"));
            Assert.That(Pre(NamesABuildSymbol[0], Symbols("javascript", "Debug")), Does.Contain("\nSub Trace()"));
            Assert.That(Pre(NamesABuildSymbol[0], Symbols("csharp", "Release")), Does.Not.Contain("\nSub Trace()"));
            Assert.That(Pre(NamesABuildSymbol[0], Symbols("cpp", null)), Does.Not.Contain("\nSub Trace()"));
            Assert.That(Pre(NamesABuildSymbol[1], Symbols("csharp", "Debug")), Does.Contain("\nSub Trace()"));
            Assert.That(Pre(NamesABuildSymbol[1], Symbols("csharp", "Release")), Does.Not.Contain("\nSub Trace()"));
            Assert.That(Pre(NamesABuildSymbol[1], Symbols("cpp", null)), Does.Contain("\nSub Trace()"),
                "no configuration defines neither, so #IfNDef RELEASE stays active as before");
            Assert.That(Pre(NamesABuildSymbol[2]), Does.Not.Contain("\nSub OnlyWeb()"), "before: never compiled");
            Assert.That(Pre(NamesABuildSymbol[2], Symbols("javascript", "Release")), Does.Contain("\nSub OnlyWeb()"));
            Assert.That(Pre(NamesABuildSymbol[2], Symbols("csharp", "Debug")), Does.Not.Contain("\nSub OnlyWeb()"));
            Assert.That(Pre(NamesABuildSymbol[2], Symbols("cpp", "Release")), Does.Not.Contain("\nSub OnlyWeb()"));
        });
    }

    /// <summary>
    /// The same guard through the REAL compiler (the <see cref="BasicCompiler"/> constructor is the one place a
    /// route's symbols are defined): a program naming no build symbol generates identical code in every
    /// configuration, and the code is the no-symbol branch of each block.
    /// </summary>
    [Test]
    public void AProgramNamingNoBuildSymbol_GeneratesIdenticalCode_ThroughTheCompiler_InEveryConfiguration()
    {
        const string program =
            "#Define LOCAL\n" +
            "Sub Main()\n" +
            "#IfDef TRACE\n    Console.WriteLine(\"trace-on\")\n#Else\n    Console.WriteLine(\"trace-off\")\n#EndIf\n" +
            "#IfDef LOCAL\n    Console.WriteLine(\"local-on\")\n#EndIf\n" +
            "#IfNDef WINDOWS\n    Console.WriteLine(\"not-windows\")\n#EndIf\n" +
            "End Sub\n";

        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "bl-symgolden-" + Path.GetRandomFileName())).FullName;
        try
        {
            var main = Path.Combine(dir, "Main.bas");
            File.WriteAllText(main, program);

            Assert.Multiple(() =>
            {
                foreach (var backend in new[] { "csharp", "javascript" })
                {
                    var outputs = new[] { "Debug", "Release", null }.Select(config =>
                    {
                        var r = new BasicCompiler(new CompilerOptions { TargetBackend = backend, Configuration = config })
                            .CompileProjectFiles(new[] { main });
                        Assert.That(r.HasErrors, Is.False,
                            $"{backend}/{config ?? "none"}: " + string.Join(" | ", r.AllErrors.Select(e => e.Message)));
                        if (r.HasErrors) return "";
                        var ir = r.CombinedIR!;
                        var pipeline = new OptimizationPipeline();
                        pipeline.AddStandardPasses();
                        pipeline.Run(ir);
                        return backend == "csharp"
                            ? new CSharpCodeGenerator().Generate(ir)
                            : new JavaScriptCodeGenerator().Generate(ir);
                    }).ToArray();

                    Assert.That(outputs[0], Does.Contain("trace-off").And.Contain("local-on").And.Contain("not-windows")
                        .And.Not.Contain("trace-on"), $"{backend}/Debug takes the no-symbol branches");
                    Assert.That(outputs[1], Is.EqualTo(outputs[0]), $"{backend}: Release == Debug");
                    Assert.That(outputs[2], Is.EqualTo(outputs[0]), $"{backend}: no configuration == Debug");
                }
            });
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
