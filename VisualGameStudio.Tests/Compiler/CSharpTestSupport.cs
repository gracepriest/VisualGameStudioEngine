using System;
using System.Collections.Generic;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using BasicLang.Compiler.SemanticAnalysis;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Shared front-end driver for the C# backend, and the optimizer-running variant the suite did not
/// have.
///
/// <para>⛔ <b>Why this exists.</b> Fourteen fixtures each carry their own private
/// <c>CompileToCSharp</c>, and <b>not one of them runs the optimizer</b> — measured, not assumed.
/// Every shipping route (CLI single-file, CLI project, the IDE's BuildService) runs
/// <c>OptimizationPipeline.AddStandardPasses()</c> unconditionally; <c>--optimize</c> only upgrades
/// standard to aggressive and nothing turns it off. So every C# assertion in this suite is pinned to
/// IR that never reaches a user, and a fixture written the obvious way is green on code that does
/// not ship. C++ and JavaScript both already have an optimizer-running helper
/// (<see cref="JsTestSupport.CompileOptimized"/>); C# was the gap.</para>
///
/// <para>This deliberately mirrors <see cref="JsTestSupport"/> line for line rather than inventing a
/// new shape — including its false-green guard, which is the part that matters most.</para>
/// </summary>
internal static class CSharpTestSupport
{
    /// <summary>Build an IRModule from BasicLang source, asserting a clean front end.</summary>
    public static IRModule BuildModule(string source, string sourceFilePath = "prog.bas")
    {
        var tokens = new Lexer(source).Tokenize();
        var parser = new Parser(tokens);
        var ast = parser.Parse();

        // ⛔ THE FALSE-GREEN GUARD, carried over from JsTestSupport verbatim in intent.
        // Parser.Parse() CATCHES ParseException internally: it records the error and
        // Synchronize()s forward, so a source file with a syntax error still returns normally —
        // usually with ZERO declarations. The SemanticAnalyzer then reports success (an empty
        // program is valid) and IRBuilder yields an empty module, so a fixture with a typo in its
        // source asserts against nothing and PASSES. Checking Analyze() alone is NOT enough; it is
        // downstream of the discarded declarations.
        Fail(parser.Errors.Count > 0, "parse",
            string.Join("; ", parser.Errors.Select(e => e.ToString())), source);

        var analyzer = new SemanticAnalyzer();
        Fail(!analyzer.Analyze(ast), "semantic analysis",
            string.Join("; ", analyzer.Errors.ConvertAll(e => e.Message)), source);

        return new IRBuilder(analyzer).Build(ast, "TestModule", sourceFilePath);
    }

    /// <summary>
    /// Compile straight to C# with NO optimizer. Equivalent to the fourteen per-fixture copies.
    /// <b>Do not treat a pass here as proof on its own</b> — see <see cref="CompileToCSharpOptimized"/>.
    /// </summary>
    public static string CompileToCSharp(string source) =>
        new CSharpCodeGenerator().Generate(BuildModule(source));

    /// <summary>
    /// Compile with the standard optimizer passes applied — <b>the IR that actually ships</b>.
    ///
    /// <para>Same shape as <see cref="JsTestSupport.CompileOptimized"/>: BuildModule →
    /// OptimizationPipeline → AddStandardPasses → Run → generate. Pin anything whose correctness the
    /// optimizer could disturb through BOTH this and the plain path; a difference between them is a
    /// finding, not noise.</para>
    ///
    /// <para>⚠ For a CODEGEN assertion (does this text appear) this is enough. For a BEHAVIOUR
    /// question — did a pass change what the program DOES — generated text is the wrong oracle and
    /// a real CLI run's stdout is the right one.</para>
    /// </summary>
    public static string CompileToCSharpOptimized(string source)
    {
        var module = BuildModule(source);

        var pipeline = new OptimizationPipeline();
        pipeline.AddStandardPasses();
        pipeline.Run(module);

        return new CSharpCodeGenerator().Generate(module);
    }

    /// <summary>
    /// Throws when a front-end stage rejected the source.
    ///
    /// <para>An exception rather than <c>Assert.Fail</c>, deliberately: this signals a broken TEST
    /// (its source does not compile), not a failed product assertion, and the two should not look
    /// alike in a run report.</para>
    /// </summary>
    private static void Fail(bool failed, string stage, string diagnostics, string source)
    {
        if (!failed) return;

        throw new InvalidOperationException(
            $"BasicLang {stage} failed for this test's source — the test is broken, not the " +
            $"backend.{Environment.NewLine}Diagnostics: {diagnostics}{Environment.NewLine}" +
            $"Source:{Environment.NewLine}{source}");
    }
}
