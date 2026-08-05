using System.Collections.Generic;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Compiler.CodeGen.JavaScript;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Shared front-end driver for the JavaScript backend fixtures.
///
/// <para>Mirrors the pipeline in <c>ForeignFeatureGuardTests</c> (Lexer → Parser →
/// SemanticAnalyzer → IRBuilder) but lives in one place rather than being copied per
/// fixture — the JS backend gets a lot of fixtures, and a drifting copy of the driver
/// would let them disagree about what "compiled" means.</para>
///
/// <para><b>This is the NON-optimizing path.</b> CLAUDE.md is explicit that a green
/// suite built only on a helper like this hides bugs the optimizer and CLI expose, so
/// plan task 29 adds optimizer-running and CLI variants. Do not treat a pass here as
/// proof on its own.</para>
/// </summary>
internal static class JsTestSupport
{
    /// <summary>Build an IRModule from BasicLang source, asserting a clean front end.</summary>
    public static IRModule BuildModule(string source, bool runPreprocessor = false)
    {
        string processed = source;
        var cppIncludes = new List<string>();

        if (runPreprocessor)
        {
            var pre = new Preprocessor();
            processed = pre.Process(source, "test.bas");
            Assert.That(pre.Errors, Is.Empty,
                string.Join("; ", pre.Errors.ConvertAll(e => e.Message)));
            cppIncludes = new List<string>(pre.CppIncludes);
        }

        var tokens = new Lexer(processed).Tokenize();
        var ast = new Parser(tokens).Parse();

        var analyzer = new SemanticAnalyzer();
        Assert.That(analyzer.Analyze(ast), Is.True,
            string.Join("; ", analyzer.Errors.ConvertAll(e => e.Message)));

        var module = new IRBuilder(analyzer).Build(ast, "TestModule");
        module.CppIncludes.AddRange(cppIncludes);
        return module;
    }

    /// <summary>Compile BasicLang source straight to JavaScript text.</summary>
    public static string Compile(string source, bool runPreprocessor = false) =>
        new JavaScriptCodeGenerator().Generate(BuildModule(source, runPreprocessor));
}
