using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.CPlusPlus;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// VB's <c>&amp;</c> against a FOREIGN <c>::</c> call on the C++ backend.
///
/// <para><b>The bug.</b> <c>StringifyForText</c> wraps each operand so <c>+</c> means
/// concatenation rather than pointer arithmetic — but it required BOTH operands to be
/// recognised, and a foreign call has no BasicLang type to recognise. The all-or-nothing pair
/// then stripped the wrap off the side that WAS known, emitting
/// <c>"text " + demo::GetName()</c>: a bare <c>const char*</c> on the left.</para>
///
/// <para><b>Why that is worse than the Single/Double case the same fall-through serves.</b> A
/// Double operand fails to BUILD, loudly. A foreign call returning an INTEGER compiles — at most
/// a <c>-Wstring-plus-int</c> warning — and advances the literal by that many bytes, walking off
/// the end into adjacent <c>.rdata</c>. Exit 0, plausible-looking output, no diagnostic. That is
/// the exact failure the stringifier exists to prevent.</para>
///
/// <para><b>The fix hands the decision to C++ overload resolution</b>, which is the only place
/// the foreign return type is knowable: <c>const char*</c> and <c>std::string</c> concatenate
/// correctly, an integer has no <c>operator+(std::string, int)</c> and becomes a build break.
/// A build break is what this module already prefers to a plausible wrong string.</para>
/// </summary>
[Category("Integration")]
[TestFixture]
public class CppForeignConcatTests
{
    private static string Emit(string source)
    {
        var tokens = new Lexer(source).Tokenize();
        var ast = new Parser(tokens).Parse();

        var analyzer = new SemanticAnalyzer();
        Assert.That(analyzer.Analyze(ast), Is.True,
            "the probe program must analyze cleanly: "
            + string.Join(" | ", analyzer.Errors.Select(e => e.Message)));

        var ir = new IRBuilder(analyzer).Build(ast, "TestModule");

        // The CLI-faithful path. The repo law: validate codegen through the optimizer, never the
        // non-optimizing helper alone.
        var pipeline = new BasicLang.Compiler.IR.Optimization.OptimizationPipeline();
        pipeline.AddStandardPasses();
        pipeline.Run(ir);

        return new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false }).Generate(ir);
    }

    private static string Program(string body) =>
        "#CppInclude \"show.h\"\nModule Main\n Sub Main()\n  " + body + "\n End Sub\nEnd Module\n";

    /// <summary>
    /// The one line of the output that performs the user program's concatenation. Reports every
    /// concat-shaped line when it cannot find the expected one — a bare <c>Single</c> throws
    /// "Sequence contains no matching element", which says nothing about what WAS emitted.
    /// </summary>
    private static string ConcatLine(string cpp, string marker)
    {
        var candidates = cpp.Split('\n').Select(l => l.Trim())
            .Where(l => l.Contains(" + ", StringComparison.Ordinal)
                        && l.StartsWith("t", StringComparison.Ordinal)
                        && l.EndsWith(";", StringComparison.Ordinal))
            .ToList();

        var hit = candidates.Where(l => l.Contains(marker, StringComparison.Ordinal)).ToList();
        Assert.That(hit, Has.Count.EqualTo(1),
            $"expected exactly one generated concat line containing '{marker}'. "
            + "Concat-shaped lines in the output were:\n  " + string.Join("\n  ", candidates));
        return hit[0];
    }

    /// <summary>
    /// The generated program plus the foreign header it names.
    ///
    /// <para>The <c>#include</c> is added here rather than left to <c>#CppInclude</c>: that
    /// directive is resolved by the PROJECT SYSTEM, which <c>CppCodeGenerator.Generate</c> alone
    /// never runs, and whose own coverage lives in the mixed-project fixtures. What is under test
    /// here is the concatenation the generator emits, so the TU is completed by hand rather than
    /// routed through a builder that now requires MSVC.</para>
    /// </summary>
    private static Dictionary<string, string> TranslationUnit(string cpp, string foreignBody) =>
        new(StringComparer.Ordinal)
        {
            ["show.h"] = "#pragma once\nnamespace demo { " + foreignBody + " }\n",
            ["prog.cpp"] = "#include \"show.h\"\n" + cpp,
        };

    // ------------------------------------------------------------------------------------

    /// <summary>
    /// <b>The run oracle — stdout, the only one that settles it.</b> The foreign function returns
    /// <c>const char*</c>, the shape a C++ author reaches for first.
    ///
    /// <para>Before the fix this did not merely misbehave, it did not compile: <c>const char* +
    /// const char*</c> has no operator. So the shape a user is most likely to write was simply
    /// unusable, and the advice was to wrap every call in <c>CStr()</c>.</para>
    /// </summary>
    [Test]
    public void ALiteralConcatenatedWithAForeignCall_Runs_AndConcatenates()
    {
        var compiler = Native.CppCompile.FindRunCompiler();
        if (compiler == null) Assert.Ignore("No C++ compiler available on this machine");

        var cpp = Emit(Program("Console.WriteLine(\"text \" & demo::GetName())"));

        var files = TranslationUnit(cpp, "inline const char* GetName() { return \"Widget\"; }");

        var output = Native.CppCompile
            .CompileAndRunFiles(files, new[] { "prog.cpp" }, compiler.Value)
            .Replace("\r\n", "\n");

        Assert.That(output, Is.EqualTo("text Widget\n"),
            "the literal and the foreign call must CONCATENATE. A compile failure here means the "
            + "known side lost its std::string wrap again, leaving const char* + const char*.");
    }

    /// <summary>The mirror: the foreign call on the LEFT is the same bug and the same fix.</summary>
    [Test]
    public void AForeignCallConcatenatedWithALiteral_Runs_AndConcatenates()
    {
        var compiler = Native.CppCompile.FindRunCompiler();
        if (compiler == null) Assert.Ignore("No C++ compiler available on this machine");

        var cpp = Emit(Program("Console.WriteLine(demo::GetName() & \" tail\")"));

        var files = TranslationUnit(cpp, "inline const char* GetName() { return \"Widget\"; }");

        var output = Native.CppCompile
            .CompileAndRunFiles(files, new[] { "prog.cpp" }, compiler.Value)
            .Replace("\r\n", "\n");

        Assert.That(output, Is.EqualTo("Widget tail\n"));
    }

    /// <summary>
    /// The emission itself, in both orders: the KNOWN side keeps its wrap even though the other
    /// side is unrecognised.
    ///
    /// <para>Asserted on the text as well as by running, because the run test passes for a
    /// <c>const char*</c> return either way once the wrap is present — it is this wrap, and only
    /// this wrap, that also makes the dangerous INTEGER return a build break instead of a pointer
    /// walk. Nothing that runs can observe that case, because the whole point is that it no longer
    /// produces a program.</para>
    /// </summary>
    [Test]
    public void TheKnownSideKeepsItsWrapWhenTheOtherSideIsForeign()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ConcatLine(Emit(Program("Console.WriteLine(\"text \" & demo::GetName())")),
                            "demo::GetName"),
                Is.EqualTo("t0 = std::string(\"text \") + demo::GetName();"),
                "the left literal must stay wrapped; a bare const char* on the left is pointer "
                + "arithmetic the moment the foreign call returns an integer.");

            Assert.That(ConcatLine(Emit(Program("Console.WriteLine(demo::GetName() & \" tail\")")),
                            "demo::GetName"),
                Is.EqualTo("t0 = demo::GetName() + std::string(\" tail\");"),
                "and the same with the operands the other way round.");
        });
    }

    /// <summary>
    /// The deliberate refusal this fall-through was written for must SURVIVE the fix.
    ///
    /// <para><c>StringifyForText</c> refuses Single/Double on purpose: there is no .NET-faithful
    /// rendering to hand, so they keep failing to build rather than printing a plausible wrong
    /// number. Wrapping the literal does not weaken that — <c>std::string("v") + aDouble</c> has
    /// no operator either — and this pins it, because "the fix made Double silently work" would be
    /// a regression disguised as an improvement.</para>
    /// </summary>
    [Test]
    public void ADoubleOperandIsStillRefusedRatherThanSilentlyRendered()
    {
        // The Double must NOT be constant-foldable, or there is no runtime concat to inspect:
        // `Dim d As Double = 1.5` folds to a literal string in the optimizer and StringifyForText
        // never sees the operand at all. A parameter cannot be folded.
        const string source =
            "#CppInclude \"show.h\"\n"
            + "Module Main\n"
            + " Sub Main()\n"
            + "  Show(1.5)\n"
            + " End Sub\n"
            + " Sub Show(d As Double)\n"
            + "  Console.WriteLine(\"v\" & d)\n"
            + " End Sub\n"
            + "End Module\n";

        var cpp = Emit(source);
        var line = ConcatLine(cpp, "std::string(\"v\")");

        Assert.Multiple(() =>
        {
            Assert.That(line, Does.Not.Contain("std::to_string"),
                "a Double must NOT be stringified — no .NET-faithful rendering exists here, so it "
                + "stays a build break rather than a plausible wrong number.");
            Assert.That(line, Does.Not.Contain(".ToString()"),
                "…and must not borrow the native-BCL ToString either.");
        });
    }

    /// <summary>
    /// Two foreign calls with no string operand are refused by the ANALYZER, before codegen ever
    /// sees them — which is what keeps the unreachable fall-through unreachable.
    /// </summary>
    [Test]
    public void TwoForeignCallsWithNoStringOperandAreRefusedByTheAnalyzer()
    {
        var tokens = new Lexer(Program("Console.WriteLine(demo::A() & demo::B())")).Tokenize();
        var ast = new Parser(tokens).Parse();
        var analyzer = new SemanticAnalyzer();

        Assert.That(analyzer.Analyze(ast), Is.False,
            "`&` with no string operand must be a diagnostic. This is what stops the emitter's "
            + "last fall-through — where NEITHER side can be wrapped — from being reachable.");
        Assert.That(string.Join(" | ", analyzer.Errors.Select(e => e.Message)),
            Does.Contain("at least one string operand"));
    }
}
