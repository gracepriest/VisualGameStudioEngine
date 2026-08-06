using System;
using System.Collections.Generic;
using System.Linq;
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
    /// <param name="sourceFilePath">
    /// Threaded onto every IRFunction as <c>SourceFilePath</c>. Omitting it leaves the path
    /// NULL, which silently disables source-map recording — the generator has no file name to
    /// attribute a mapping to. The real compiler always supplies one (Compiler.cs:724), so a
    /// null here is an artefact of the helper rather than a real shape.
    /// </param>
    public static IRModule BuildModule(string source, bool runPreprocessor = false,
        string sourceFilePath = null)
    {
        string processed = source;
        var cppIncludes = new List<string>();

        if (runPreprocessor)
        {
            var pre = new Preprocessor();
            processed = pre.Process(source, "test.bas");
            Fail(pre.Errors.Count > 0, "preprocess",
                string.Join("; ", pre.Errors.ConvertAll(e => e.Message)), source);
            cppIncludes = new List<string>(pre.CppIncludes);
        }

        var tokens = new Lexer(processed).Tokenize();
        var parser = new Parser(tokens);
        var ast = parser.Parse();

        // ⛔ THE FALSE-GREEN GUARD. Parser.Parse() CATCHES ParseException internally: it
        // records the error and Synchronize()s forward, so a source file with a syntax error
        // still returns normally — usually with ZERO declarations. The SemanticAnalyzer then
        // reports success (an empty program is valid) and IRBuilder yields an empty module,
        // so a fixture with a typo in its source asserts against nothing and PASSES.
        //
        // Every capability rejection in this backend is asserted through this helper, so a
        // silent parse failure makes those rejections untestable. Checking Analyze() alone is
        // NOT enough — it is downstream of the discarded declarations.
        Fail(parser.Errors.Count > 0, "parse",
            string.Join("; ", parser.Errors.Select(e => e.ToString())), source);

        var analyzer = new SemanticAnalyzer();
        Fail(!analyzer.Analyze(ast), "semantic analysis",
            string.Join("; ", analyzer.Errors.ConvertAll(e => e.Message)), source);

        var module = new IRBuilder(analyzer).Build(ast, "TestModule", sourceFilePath);
        module.CppIncludes.AddRange(cppIncludes);
        return module;
    }

    /// <summary>
    /// Throws when a front-end stage rejected the source.
    ///
    /// <para>An exception rather than <c>Assert.Fail</c>, deliberately: this signals a broken
    /// TEST (its source does not compile), not a failed product assertion, and the two should
    /// not look alike in a run report. It is also what makes the guard itself testable —
    /// NUnit records an assertion failure even when the exception is caught, so a helper
    /// built on Assert cannot be verified by a test.</para>
    /// </summary>
    private static void Fail(bool failed, string stage, string diagnostics, string source)
    {
        if (!failed) return;

        throw new InvalidOperationException(
            $"BasicLang {stage} failed for this test's source — the test is broken, not the " +
            $"backend.{Environment.NewLine}Diagnostics: {diagnostics}{Environment.NewLine}" +
            $"Source:{Environment.NewLine}{source}");
    }

    /// <summary>Compile BasicLang source straight to JavaScript text.</summary>
    public static string Compile(string source, bool runPreprocessor = false) =>
        new JavaScriptCodeGenerator().Generate(BuildModule(source, runPreprocessor));
}
