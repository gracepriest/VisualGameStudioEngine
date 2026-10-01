using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using BasicLang.Compiler.SemanticAnalysis;
using VisualGameStudio.Tests.Native;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// The in-process ways a BasicLang program reaches the C++ backend (CLAUDE.md: "test both entry
/// points", "validate codegen through the IR optimizer"). The real CLI is driven by
/// <see cref="CppClosures.Cli"/>; these are the in-process ones.
/// </summary>
internal enum CppEntry
{
    /// <summary>Front end straight to <c>CppCodeGenerator.Generate</c>: no optimizer at all.</summary>
    Plain,
    /// <summary>The standard optimizer passes — what <c>BclE2E.CompileToCppOptimized</c> does and what
    /// the CLI's <c>--optimize</c> runs.</summary>
    Standard,
    /// <summary>The aggressive passes (<see cref="AggressivePipeline"/>).</summary>
    Aggressive,
    /// <summary><c>BasicCompiler.CompileProjectFiles</c> (aggressive options — the entry point a
    /// <c>.blproj</c> build and the IDE's build service both use), then <c>Generate</c>.</summary>
    Project,
    /// <summary><c>CompileProjectFiles</c> then <c>GenerateSplit</c>: the per-module file set the IDE's
    /// C++ project build (<c>CppProjectBuilder</c>) writes into <c>obj/gen</c>.</summary>
    Split,
}

/// <summary>What one C++ generation produced: the text, and the path every root that creates a
/// lambda took (<see cref="CppCodeGenerator.ClosurePaths"/>, #140 ruling D5.4).</summary>
internal sealed record CppBuild(
    string Cpp,
    IReadOnlyList<CppClosureRootPath> Paths,
    IReadOnlyDictionary<string, string> Files = null,
    IReadOnlyList<string> TranslationUnits = null)
{
    /// <summary>The path the root named <paramref name="root"/> took; fails when no such root is listed.</summary>
    public CppClosurePath PathOf(string root)
    {
        var match = Paths.Where(p => p.Root == root).ToList();
        Assert.That(match, Has.Count.EqualTo(1),
            $"ClosurePaths has no unique root '{root}'. It lists: [{Describe()}]");
        return match[0].Path;
    }

    /// <summary>Every root, as <c>Root=Path</c>, in the order <c>ClosurePaths</c> lists them.</summary>
    public string Describe() => string.Join(", ", Paths.Select(p => $"{p.Root}={p.Path}"));

    /// <summary>The roots that took <paramref name="path"/>.</summary>
    public List<string> RootsOn(CppClosurePath path) => Paths.Where(p => p.Path == path).Select(p => p.Root).ToList();

    /// <summary>The generated text: the combined unit, or every file of a split build.</summary>
    public string AllText => Files == null ? Cpp
        : string.Join("\n", Files.Where(f => f.Key != CppCodeGenerator.RuntimeHeaderFileName).Select(f => f.Value));
}

/// <summary>Shared plumbing for the #140 C++ closure tests: compile, read the path each root took,
/// run.</summary>
internal static class CppClosures
{
    /// <summary>The entry points that compile AND run in a test; the real CLI is separate.</summary>
    internal static readonly CppEntry[] RunEntries = { CppEntry.Standard, CppEntry.Aggressive, CppEntry.Project };

    /// <summary>Front end to IR with no optimizer; asserts the program is accepted.</summary>
    internal static IRModule Ir(string source)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty, "parse errors:\n" + string.Join("\n", parser.Errors.Select(e => e.Message)));
        var analyzer = new SemanticAnalyzer();
        Assert.That(analyzer.Analyze(ast), Is.True,
            "semantic errors:\n" + string.Join("\n", analyzer.Errors.Select(e => e.ToString())));
        return new IRBuilder(analyzer).Build(ast, "TestModule");
    }

    /// <summary>The module after <paramref name="entry"/>'s optimizer passes (Plain: none).</summary>
    internal static IRModule Optimized(string source, CppEntry entry)
    {
        var module = Ir(source);
        switch (entry)
        {
            case CppEntry.Plain: break;
            case CppEntry.Standard:
                var pipeline = new OptimizationPipeline();
                pipeline.AddStandardPasses();
                pipeline.Run(module);
                break;
            case CppEntry.Aggressive:
                AggressivePipeline.Apply(module);
                break;
            default: throw new System.ArgumentException("not an in-process single-file entry: " + entry);
        }
        return module;
    }

    /// <summary>Generates C++ for <paramref name="source"/> through <paramref name="entry"/>. Throws
    /// <see cref="CppCapabilityException"/> for a program the backend refuses.</summary>
    internal static CppBuild Compile(string source, CppEntry entry = CppEntry.Standard)
    {
        if (entry == CppEntry.Project || entry == CppEntry.Split) return CompileProject(source, entry == CppEntry.Split);
        var gen = new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false });
        var cpp = gen.Generate(Optimized(source, entry));
        return new CppBuild(cpp, gen.ClosurePaths.ToList());
    }

    private static CppBuild CompileProject(string source, bool split)
    {
        var compiler = new BasicCompiler(new CompilerOptions { OptimizeAggressive = true });
        var dir = Path.Combine(Path.GetTempPath(), "bl-t140-proj-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "Main.bas");
            File.WriteAllText(path, source);
            var result = compiler.CompileProjectFiles(new List<string> { path });
            Assert.That(result.HasErrors, Is.False, string.Join(" | ", result.AllErrors.Select(e => e.Message)));
            Assert.That(result.CombinedIR, Is.Not.Null, "the project entry point produced no combined IR");

            var gen = new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false });
            if (!split) return new CppBuild(gen.Generate(result.CombinedIR), gen.ClosurePaths.ToList());

            var units = result.Units.Select(u => u.IR).Where(ir => ir != null).ToList();
            var files = gen.GenerateSplit(result.CombinedIR, "P", units, emitMain: true);
            return new CppBuild(null, gen.ClosurePaths.ToList(), files.Files, files.TranslationUnitFileNames.ToList());
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* temp */ } }
    }

    /// <summary>
    /// Like <see cref="Compile"/> for the single-file entry points, but NEVER asserts or throws: null when the text
    /// is not a program the front end accepts, or the backend refuses it, or generation fails for any reason. For
    /// sweeps over many programs, where a fragment is not a failure (an NUnit <c>Assert</c> that fails inside a
    /// <c>catch</c> is still recorded against the test).
    /// </summary>
    internal static CppBuild TryCompile(string source, CppEntry entry = CppEntry.Standard)
    {
        try
        {
            var module = TempIr.TryBuild(source);
            if (module == null) return null;
            switch (entry)
            {
                case CppEntry.Plain: break;
                case CppEntry.Standard:
                    var pipeline = new OptimizationPipeline();
                    pipeline.AddStandardPasses();
                    pipeline.Run(module);
                    break;
                case CppEntry.Aggressive: AggressivePipeline.Apply(module); break;
                default: throw new System.ArgumentException("not an in-process single-file entry: " + entry);
            }
            var gen = new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false });
            var cpp = gen.Generate(module);
            return new CppBuild(cpp, gen.ClosurePaths.ToList());
        }
        catch (System.Exception) { return null; }
    }

    /// <summary>The paths alone (no clang): the fast way to assert which representation a root took.</summary>
    internal static IReadOnlyList<CppClosureRootPath> Paths(string source, CppEntry entry = CppEntry.Standard)
        => Compile(source, entry).Paths;

    /// <summary>Generates, compiles with a real C++ compiler, runs, and returns the normalised stdout.
    /// Ignores when no C++ compiler is installed (<c>BclE2E.CompileRun</c>'s rule).</summary>
    internal static string Run(string source, CppEntry entry = CppEntry.Standard) => Run(Compile(source, entry));

    internal static string Run(CppBuild build)
    {
        if (build.Files == null) return FourBackends.Norm(BclE2E.CompileRun(build.Cpp));
        var compiler = CppCompile.FindRunCompiler();
        if (compiler == null) Assert.Ignore("No C++ compiler available on this machine");
        return FourBackends.Norm(BclE2E.WithoutRuntimeInFailures(() =>
            CppCompile.CompileAndRunFiles(build.Files, build.TranslationUnits, compiler.Value)));
    }

    /// <summary>Compiles only (no run) and reports whether the C++ compiler accepted the text, with its
    /// output — for a program whose contract is that the generated C++ does not compile (a named gap).</summary>
    internal static (bool Compiled, string Output) TryClang(string cpp)
    {
        var compiler = CppCompile.FindRunCompiler();
        if (compiler == null) Assert.Ignore("No C++ compiler available on this machine");
        return CppCompile.TryCompile(cpp, compiler.Value);
    }

    /// <summary>Asserts every program prints <paramref name="expected"/> through the standard, aggressive
    /// and project entry points.</summary>
    internal static void RunsInAllModes(string source, string expected, string what = "C++")
    {
        Assert.Multiple(() =>
        {
            foreach (var entry in RunEntries)
                Assert.That(Run(source, entry), Is.EqualTo(expected), $"{what}, {entry}");
        });
    }

    /// <summary>Asserts the root took <paramref name="expected"/> in every in-process mode.</summary>
    internal static void RootTakes(string source, string root, CppClosurePath expected)
    {
        Assert.Multiple(() =>
        {
            foreach (var entry in new[] { CppEntry.Plain, CppEntry.Standard, CppEntry.Aggressive, CppEntry.Project, CppEntry.Split })
                Assert.That(Compile(source, entry).PathOf(root), Is.EqualTo(expected), $"root '{root}', {entry}");
        });
    }

    /// <summary>The exception <paramref name="source"/> is refused with in <paramref name="entry"/>.</summary>
    internal static CppCapabilityException Refusal(string source, CppEntry entry)
        => Assert.Throws<CppCapabilityException>(() => Compile(source, entry), $"{entry} must refuse");

    /// <summary>
    /// A program BOTH paths refuse (#140 ruling D1, case 3): the message carries W2's text first
    /// (<c>not supported on C++ (#140)</c>, naming <paramref name="capturedVariable"/> of
    /// <paramref name="root"/> when given), then the lowering's own reason
    /// (<c>closure lowering cannot lower '&lt;root&gt;' either (#140): C++: …</c>, which must contain
    /// <paramref name="loweringReason"/>) — in that order, in every in-process entry point.
    /// </summary>
    internal static void AssertBothRefused(string source, string root, string loweringReason, string capturedVariable = null)
    {
        Assert.Multiple(() =>
        {
            foreach (var entry in new[] { CppEntry.Plain, CppEntry.Standard, CppEntry.Aggressive, CppEntry.Project, CppEntry.Split })
                AssertBothRefusedMessage(Refusal(source, entry).Message, root, loweringReason, capturedVariable, entry.ToString());
        });
    }

    internal static void AssertBothRefusedMessage(string message, string root, string loweringReason, string capturedVariable, string where)
    {
        var w2 = message.IndexOf("not supported on C++ (#140)", System.StringComparison.Ordinal);
        var lowering = message.IndexOf($"closure lowering cannot lower '{root}' either (#140): C++: ", System.StringComparison.Ordinal);
        Assert.That(w2, Is.GreaterThanOrEqualTo(0), $"{where}: W2's text is missing.\n{message}");
        Assert.That(lowering, Is.GreaterThanOrEqualTo(0), $"{where}: the lowering's reason for '{root}' is missing.\n{message}");
        Assert.That(w2, Is.LessThan(lowering), $"{where}: W2's text must come FIRST, then the lowering's reason.\n{message}");
        Assert.That(message.Substring(lowering), Does.Contain(loweringReason), $"{where}: the lowering's reason.\n{message}");
        if (capturedVariable != null)
            Assert.That(message, Does.Contain($"captures '{capturedVariable}' of '{root}'"), $"{where}: W2 names the variable.\n{message}");
    }

    // ------------------------------------------------------------------------------------------
    // The real CLI
    // ------------------------------------------------------------------------------------------

    /// <summary>What <c>BasicLang Prog.bas --target=cpp [--optimize]</c> did: the exit code, what it
    /// printed, the generated <c>Prog.cpp</c> (null when none was written) and every <c>.cpp</c> found
    /// under the working directory.</summary>
    internal sealed record CliResult(int Exit, string Console, string Cpp, IReadOnlyList<string> CppFilesWritten);

    /// <summary>Runs the real CLI on a copy of <paramref name="source"/> in a fresh temp directory.
    /// <paramref name="project"/> true drives <c>build P.blproj -c Release</c> — the route the IDE's
    /// C++ build takes (<c>CppProjectBuilder</c>) — instead of a single file; on a machine without MSVC
    /// that build stops at BL6015 after it has written <c>obj/gen</c>, so only the files it wrote and
    /// its diagnostics are meaningful there.</summary>
    internal static CliResult Cli(string source, bool optimize = false, bool project = false)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-t140-cli-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Prog.bas"), source);
            string[] args;
            if (project)
            {
                File.WriteAllText(Path.Combine(dir, "P.blproj"),
                    "<BasicLangProject>\n  <PropertyGroup><ProjectName>P</ProjectName><AssemblyName>P</AssemblyName>" +
                    "<OutputType>Exe</OutputType><TargetBackend>Cpp</TargetBackend></PropertyGroup>\n</BasicLangProject>\n");
                args = new[] { "build", "P.blproj", "-c", "Release" };
            }
            else
            {
                var list = new List<string> { "Prog.bas", "--target=cpp" };
                if (optimize) list.Add("--optimize");
                args = list.ToArray();
            }
            var (exit, stdout, stderr) = CliTestHarness.RunProcess(CliTestHarness.CliPath(), args, dir, timeoutMs: 300_000);
            var cppPath = Path.Combine(dir, "Prog.cpp");
            var written = Directory.EnumerateFiles(dir, "*.cpp", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(dir, f)).OrderBy(f => f).ToList();
            return new CliResult(exit, stdout + stderr, File.Exists(cppPath) ? File.ReadAllText(cppPath) : null, written);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* temp */ } }
    }

    /// <summary>Runs the real CLI, asserts it succeeded, compiles and runs the file it wrote.</summary>
    internal static string RunViaCli(string source, bool optimize)
    {
        var r = Cli(source, optimize);
        Assert.That(r.Exit, Is.EqualTo(0), $"CLI --target=cpp{(optimize ? " --optimize" : "")} failed:\n{r.Console}");
        Assert.That(r.Cpp, Is.Not.Null, "the CLI wrote no Prog.cpp");
        return FourBackends.Norm(BclE2E.CompileRun(r.Cpp));
    }
}

/// <summary>
/// A text-level check of the one C++ rule the label-and-<c>goto</c> emitter can break without any test noticing until a C++
/// compiler runs: <b>a <c>goto</c> may leave a <c>try</c> block or a <c>catch</c> handler, never ENTER one</b> ("cannot jump
/// from this goto statement to its label"). #140's per-iteration environment wraps a loop body in a try/finally, and
/// <c>ComputeInlineRegion</c> must keep everything after the loop out of it (#226's rule: an <c>Exit</c> that leaves the
/// region ends it); lose that and the post-loop label lands inside the try. This lint finds it from the generated TEXT, so it
/// runs without a C++ compiler.
///
/// <para>Scopes: a label belongs to its enclosing FUNCTION-like block (a function, method or lambda body — a block opened after
/// a parameter list that is not an <c>if</c>/<c>while</c>/<c>for</c>/<c>switch</c>/<c>catch</c> condition); only the
/// <c>try</c>/<c>catch</c> blocks opened INSIDE that scope count. Strings, character literals and comments are skipped. Only the
/// generated program is read (from "// Forward declarations", after the spliced runtime), as the runtime has no labels.</para>
/// </summary>
internal static class CppGotoLint
{
    /// <summary>Every <c>goto</c> that would enter a <c>try</c> block or <c>catch</c> handler its label is in; empty when there is none.</summary>
    internal static List<string> IllegalJumps(string cpp)
    {
        var problems = new List<string>();
        var start = cpp.IndexOf("// Forward declarations", System.StringComparison.Ordinal);
        var s = start >= 0 ? cpp.Substring(start) : cpp;

        // Tokenize into identifiers and single punctuation characters, with line numbers.
        var tokens = new List<(string Text, int Line)>();
        var line = 1;
        for (var i = 0; i < s.Length;)
        {
            var c = s[i];
            if (c == '\n') { line++; i++; continue; }
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '/') { while (i < s.Length && s[i] != '\n') i++; continue; }
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < s.Length && !(s[i] == '*' && s[i + 1] == '/')) { if (s[i] == '\n') line++; i++; }
                i += 2; continue;
            }
            if (c == '"' || c == '\'')
            {
                var quote = c; i++;
                while (i < s.Length && s[i] != quote) { if (s[i] == '\\') i++; if (i < s.Length && s[i] == '\n') line++; i++; }
                i++; tokens.Add(("\"\"", line)); continue;
            }
            if (char.IsLetter(c) || c == '_')
            {
                var j = i;
                while (j < s.Length && (char.IsLetterOrDigit(s[j]) || s[j] == '_')) j++;
                tokens.Add((s.Substring(i, j - i), line)); i = j; continue;
            }
            tokens.Add((c.ToString(), line)); i++;
        }

        string[] controlWords = { "if", "while", "for", "switch", "catch" };
        // block stack: (kind, id, functionScopeId)
        var stack = new List<(string Kind, int Id)>();
        var nextId = 0;
        var labels = new Dictionary<(int Scope, string Name), (List<int> Tries, int Line)>();
        var gotos = new List<(int Scope, string Name, List<int> Tries, int Line)>();

        int CurrentScope()
        {
            for (var k = stack.Count - 1; k >= 0; k--) if (stack[k].Kind == "function") return stack[k].Id;
            return -1;
        }
        List<int> TriesInScope()
        {
            var tries = new List<int>();
            for (var k = stack.Count - 1; k >= 0 && stack[k].Kind != "function"; k--)
                if (stack[k].Kind == "try" || stack[k].Kind == "catch") tries.Insert(0, stack[k].Id);
            return tries;
        }
        string OpenerKind(int braceIndex)
        {
            var p = braceIndex - 1;
            if (p < 0) return "other";
            var prev = tokens[p].Text;
            if (prev == "try") return "try";
            if (prev == "else" || prev == "do") return "control";
            if (prev == ")")
            {
                var depth = 0; var q = p;
                for (; q >= 0; q--)
                {
                    if (tokens[q].Text == ")") depth++;
                    else if (tokens[q].Text == "(") { depth--; if (depth == 0) break; }
                }
                if (q > 0)
                {
                    var word = tokens[q - 1].Text;
                    if (word == "catch") return "catch";
                    if (System.Array.IndexOf(controlWords, word) >= 0) return "control";
                }
                return "function";
            }
            // `int32_t {` after a lambda's `->` return type, `const {`, `mutable {`, `override {` ...: function-like
            return (prev == "{" || prev == "}" || prev == ";") ? "other" : "function";
        }

        for (var t = 0; t < tokens.Count; t++)
        {
            var text = tokens[t].Text;
            if (text == "{")
            {
                var kind = OpenerKind(t);
                stack.Add((kind, nextId++));
            }
            else if (text == "}")
            {
                if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
            }
            else if (text == "goto" && t + 2 < tokens.Count && tokens[t + 2].Text == ";")
            {
                gotos.Add((CurrentScope(), tokens[t + 1].Text, TriesInScope(), tokens[t].Line));
            }
            else if (t + 2 < tokens.Count && tokens[t + 1].Text == ":" && tokens[t + 2].Text == ";"
                     && (char.IsLetter(text[0]) || text[0] == '_')
                     && text != "default" && text != "public" && text != "private" && text != "protected"
                     && !(t > 0 && tokens[t - 1].Text == ":"))
            {
                labels[(CurrentScope(), text)] = (TriesInScope(), tokens[t].Line);
            }
        }

        foreach (var g in gotos)
        {
            if (!labels.TryGetValue((g.Scope, g.Name), out var label)) continue;
            // legal iff every try/catch block the label is in also encloses the goto (the label's stack is a prefix of the goto's)
            var legal = label.Tries.Count <= g.Tries.Count && !label.Tries.Where((id, k) => g.Tries[k] != id).Any();
            if (!legal) problems.Add($"line {g.Line}: 'goto {g.Name}' jumps INTO a try block or catch handler (the label is at line {label.Line})");
        }
        return problems;
    }
}
