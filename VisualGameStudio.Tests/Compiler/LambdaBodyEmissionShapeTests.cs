using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.CSharp;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #136 — the SHAPE of the C# a lambda is emitted as. Nothing runs here, nothing spawns: the emitted text is read, and handed to Roslyn
//  to compile (never to run), so this is the fast subset's half of the fix, and the half that names the mutants the RUN fixture
//  (LambdaBodyEmissionExecutionTests) cannot tell apart cheaply.
//
//  What it pins, each through the standard pipeline AND the aggressive one (what `--optimize` and a Release project build run), and the
//  project entry point (`BasicCompiler.CompileProjectFiles`, what the IDE's build service calls):
//    - a PURE single-expression lambda keeps its bytes (`f = (int x) => (x * 3) + 1;`), a lambda that writes before its Return is a BLOCK lambda
//      and holds the write, and the three expression shapes the old rule refused (a call, a write, an alloca before the return) stay blocks;
//    - a lambda's own Dim is declared INSIDE the lambda, a statement-form ByRef call in it writes `ref`, a call whose result is used is written once;
//    - no `#line` inside a lambda body, and the `#line`s around the lambda are where they were;
//    - the block is indented one level deeper than the line it sits on, at every nesting depth;
//    - the text of the ENCLOSING function after a lambda is what it would be without it: a lambda parameter or Dim spelled like a module global or a
//      field must not leave its spelling in the name map (`t` read after a lambda with `Dim T` stays `t`);
//    - every program of the run fixture's table compiles (CS1643 and CS0103 were the old failures).
//
//  ⭐ MUTANTS (S/t136/mut: each is the fix plus ONE change, built in a detached worktree and run against these tests with its BasicLang.dll swapped into a copy
//  of the test output). Killed here: M1 the old "every value before the return" rule for an expression lambda; M2/M3 the lambda's parameters/locals not tracked as
//  names; M4 nothing restored after the lambda; M4b only the name tables not restored; M5 module globals not added to the names; M6 `#line` written inside a
//  lambda body; M8 the "no local" check removed from the expression rule; M9 the "would the function emitter write it" check removed; M10 the body one level
//  too deep; M11 the per-iteration plan not restored; M13 a lambda's sized array not allocated; M16 the enclosing function's use counts; M18 the enclosing names
//  not visible; M20 the processed-block set not restored; M21 the pending If-merge claims not restored. NOT killed, and why: M7 (the `_materialised.Count` check:
//  ShouldEmitInstruction returns true for a materialised value first, so the check never decides), M12 (the For Each rename table is balanced by each loop's own
//  restore), M14 (a lambda's blocks never branch to the enclosing function's blocks, the only thing the loop-end stack is asked), M17 and M19 (no lambda IR holds a
//  materialised temp or an enclosing temp; measured on 126 probes, M12, M14, M17 and M19 change not one byte of the C#).
// ================================================================================================

[TestFixture]
[NonParallelizable] // switches IRVerifier.Mode off around the module-initialiser rows, like the other fixtures that mutate it
public class LambdaBodyEmissionShapeTests
{
    // ============================================================================================
    // helpers
    // ============================================================================================

    private enum Pipeline { Standard, Aggressive }

    private static readonly Pipeline[] Pipelines = { Pipeline.Standard, Pipeline.Aggressive };

    /// <summary>BasicLang -> C# through the front end, the chosen optimizer pipeline and the C# backend, with a source file name so `#line` is written.</summary>
    private static string Emit(string source, Pipeline pipeline = Pipeline.Standard)
    {
        var module = CSharpTestSupport.BuildModule(source, "Prog.bas");
        if (pipeline == Pipeline.Aggressive)
        {
            AggressivePipeline.Apply(module);
        }
        else
        {
            var passes = new BasicLang.Compiler.IR.Optimization.OptimizationPipeline();
            passes.AddStandardPasses();
            passes.Run(module);
        }

        return new ImprovedCSharpCodeGenerator().Generate(module).Replace("\r\n", "\n");
    }

    /// <summary>The same program through <c>BasicCompiler.CompileProjectFiles</c> (aggressive): the entry point of a Release project build and the IDE.</summary>
    private static string EmitViaProject(string source) => TempExec.Emit(Bk.CSharp, EntryPoint.ProjectRelease, source).Replace("\r\n", "\n");

    private static IEnumerable<(string Name, Func<string, string> Emit)> EntryPoints()
    {
        yield return ("standard", s => Emit(s));
        yield return ("aggressive", s => Emit(s, Pipeline.Aggressive));
        yield return ("project", EmitViaProject);
    }

    /// <summary>One lambda written as a block: the line that ends in <c>=&gt; {</c> through the line that closes it at the same indentation.</summary>
    internal sealed record Block(int Start, int End, string[] Lines)
    {
        /// <summary>The statements between the braces, trimmed.</summary>
        public string[] Body => Lines.Skip(1).Take(Lines.Length - 2).Select(l => l.Trim()).ToArray();

        public string Text => string.Join("\n", Lines);
    }

    private static int IndentOf(string line) => line.Length - line.TrimStart(' ').Length;

    internal static List<Block> Blocks(string csharp)
    {
        var lines = csharp.Split('\n');
        var found = new List<Block>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].TrimEnd().EndsWith("=> {", StringComparison.Ordinal)) continue;
            var indent = IndentOf(lines[i]);
            var j = i + 1;
            while (j < lines.Length && !(IndentOf(lines[j]) == indent && lines[j].TrimStart(' ').StartsWith('}'))) j++;
            Assert.That(j, Is.LessThan(lines.Length), "a block lambda that never closes:\n" + csharp);
            found.Add(new Block(i, j, lines[i..(j + 1)]));
        }

        return found;
    }

    /// <summary>The program with every block lambda's INTERIOR cut out (its opening and closing lines stay), blank lines and `#line` directives dropped.</summary>
    private static string[] OutsideTheLambdas(string csharp)
    {
        var lines = csharp.Split('\n');
        var inside = new HashSet<int>();
        foreach (var block in Blocks(csharp))
            for (var i = block.Start + 1; i < block.End; i++) inside.Add(i);

        return lines.Where((l, i) => !inside.Contains(i)).Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith("#line", StringComparison.Ordinal)).ToArray();
    }

    private static string[] WithoutDirectives(string csharp)
        => csharp.Split('\n').Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith("#line", StringComparison.Ordinal)).ToArray();

    private static string Probe(string id) => LambdaBodyProbes.All.Single(p => p.Id == id).Source;

    /// <summary>
    /// ⚠ A lambda written in a MODULE GLOBAL's initialiser (`Dim bump As Action = Sub() …`, rows k_global and k_global2) is an orphan to IRVerifier's Invariant P(d)
    /// ("every IR lambda is created exactly once": no instruction of any block creates it), so a pipeline run with the verifier on — the test host's setting — throws
    /// <c>IRVerificationException</c>. PRE-EXISTING and not #136's (measured on 0f6d2ced; HANDOFF's #140 section calls it "the ONE pre-existing verifier fire"); the shipping
    /// CLI runs with the verifier off. These two rows are emitted with it off, which changes nothing about the C# written (the backend never reads it).
    /// </summary>
    private static readonly HashSet<string> ModuleInitialiserRows = new(StringComparer.Ordinal) { "k_global", "k_global2" };

    private static T WithTheVerifierOffFor<T>(string id, Func<T> emit)
    {
        if (!ModuleInitialiserRows.Contains(id)) return emit();
        var previous = BasicLang.Compiler.IR.Optimization.IRVerifier.Mode;
        BasicLang.Compiler.IR.Optimization.IRVerifier.Mode = BasicLang.Compiler.IR.Optimization.IRVerifierMode.Off;
        try { return emit(); }
        finally { BasicLang.Compiler.IR.Optimization.IRVerifier.Mode = previous; }
    }

    private static string EmitProbe(string id, Pipeline pipeline = Pipeline.Standard) => WithTheVerifierOffFor(id, () => Emit(Probe(id), pipeline));

    private static string EmitProbeViaProject(string id) => WithTheVerifierOffFor(id, () => EmitViaProject(Probe(id)));

    // ============================================================================================
    // A PURE single-expression lambda keeps its bytes
    // ============================================================================================

    /// <summary>
    /// ⭐ The lambdas that were already right are written as they were: no block, no braces. The text is the BEFORE build's byte for byte (S/t136/tw/emit:
    /// e1, e2, e3 emitted identically on 0f6d2ced and on the fix), and is pinned through both pipelines and the project entry point. Mutant M1 (the old
    /// "every instruction before the return is a value" rule) and M7/M8/M9 (parts of the new rule removed) change what a lambda that WRITES becomes, not these;
    /// they are here because "a pure expression stays an expression" is half of the rule.
    /// </summary>
    [TestCase("Sub Main()\n    Dim f = Function(x As Integer) x * 3 + 1\n    Console.WriteLine(f(2))\nEnd Sub\n",
        "f = (int x) => (x * 3) + 1;", TestName = "ExpressionLambda_OfAParameter")]
    [TestCase("Sub Main()\n    Dim k As Integer = 5\n    Dim add = Function(x As Integer) x + k\n    Console.WriteLine(add(2))\nEnd Sub\n",
        "add = (int x) => x + k;", TestName = "ExpressionLambda_ReadingACapture")]
    [TestCase("Sub Main()\n    Dim mk = Function(a As Integer) Function(b As Integer) a * 10 + b\n    Dim g = mk(4)\n    Console.WriteLine(g(2))\nEnd Sub\n",
        "mk = (int a) => (int b) => (a * 10) + b;", TestName = "ExpressionLambda_InsideAnExpressionLambda")]
    [TestCase("Sub Main()\n    Dim d = Function(s As String) s.Length + 1\n    Console.WriteLine(d(\"abc\"))\nEnd Sub\n",
        "d = (string s) => s.Length + 1;", TestName = "ExpressionLambda_OfAMemberRead")]
    public void APureExpressionLambda_KeepsItsBytes(string source, string expectedLine)
    {
        foreach (var (name, emit) in EntryPoints())
        {
            var csharp = emit(source);
            Assert.Multiple(() =>
            {
                Assert.That(csharp.Split('\n').Select(l => l.Trim()), Does.Contain(expectedLine), $"{name}:\n{csharp}");
                Assert.That(Blocks(csharp), Is.Empty, $"{name}: a pure expression lambda is not written as a block:\n{csharp}");
            });
        }
    }

    // ============================================================================================
    // A lambda that writes before its Return, or holds anything but `return expr;`, is a BLOCK lambda — and the write is in it
    // ============================================================================================

    /// <summary>
    /// ⭐ F1/F7's shape. <c>n = n + 1</c> is an IRBinaryOp renamed <c>n</c> — a "value" — before the Return, so the old rule saw a one-block lambda ending in a
    /// valued return with nothing but values before it, wrote the EXPRESSION lambda <c>() =&gt; 5</c> and the write was gone. Now the write is the first statement
    /// of a block. Kills M1 (the old syntactic rule) and M9 (the "would the function emitter write it" test removed): both give <c>() =&gt; 5</c>.
    /// </summary>
    [TestCase("c_asg", "n = n + 1;|return 5;", TestName = "AWriteBeforeTheReturn")]
    [TestCase("g_once", "Tick();|return (Tick() * 10) + Tick();", TestName = "ACallBeforeTheReturn_AndTwoUsedInIt")]
    [TestCase("k4_ownparam", "X = X + 1;|Console.WriteLine(X);", TestName = "AWriteToTheLambdasOwnParameter")]
    [TestCase("b_fnpar", "x = x * 3;|return x + 1;", TestName = "AWriteToTheParameterBeforeTheReturn")]
    public void ALambdaThatWritesBeforeItsReturn_IsABlock_AndTheWriteIsInIt(string id, string body)
    {
        foreach (var pipeline in Pipelines)
        {
            var csharp = EmitProbe(id, pipeline);
            var blocks = Blocks(csharp);
            Assert.That(blocks, Has.Count.EqualTo(1), $"{id} {pipeline}:\n{csharp}");
            Assert.That(blocks[0].Body, Is.EqualTo(body.Split('|')), $"{id} {pipeline}:\n{csharp}");
        }
    }

    /// <summary>
    /// F7's own shape, from MultiLineFunctionLambdaProbes: a Function lambda that adds to a field of the enclosing class and returns it. The write is a
    /// field store (<c>_total = _total + v</c>); it is the line before the Return in the block.
    /// </summary>
    [Test]
    public void AWriteToAField_BeforeTheReturn_IsInTheBlock()
    {
        foreach (var pipeline in Pipelines)
        {
            var csharp = Emit(MultiLineFunctionLambdaProbes.F7, pipeline);
            var block = Blocks(csharp).Single();
            Assert.That(block.Body, Does.Contain("_total = _total + v;"), csharp);
            Assert.That(block.Body.Last(), Does.StartWith("return "), csharp);
        }
    }

    /// <summary>
    /// The three kinds the old rule refused — a call, a write through an address, a local — still make a block; and a Sub lambda whose single statement is a
    /// call is a block with that call. (`Function() Foo(Bar())` is a block, `return Foo(Bar());`, and `Bar` is written ONCE: #179 wrote it three times.)
    /// </summary>
    [Test]
    public void ACallInAnExpressionLambda_StaysABlock_AndIsWrittenOnce()
    {
        foreach (var pipeline in Pipelines)
        {
            var csharp = EmitProbe("i_expr", pipeline);
            var blocks = Blocks(csharp);
            Assert.That(blocks.Select(b => b.Body), Is.EqualTo(new[]
            {
                new[] { "return Foo(Bar());" },
                new[] { "Console.WriteLine(\"c\");" },
            }), $"{pipeline}:\n{csharp}");
            Assert.That(Regex.Matches(csharp, @"\bBar\(\)").Count, Is.EqualTo(2), $"{pipeline}: Bar's declaration and its one call:\n{csharp}");
        }
    }

    /// <summary>
    /// An If, a loop, a Select Case and a Try in a lambda are written INSIDE the block, with the statements the function emitter writes for them. Before, the
    /// block was `{ ; }`: CS1643 for a Function lambda, a Sub lambda that did nothing.
    /// </summary>
    [TestCase("c_if", "if (v > 2)", "return r;", TestName = "AnIf")]
    [TestCase("c_loop", "while (i <= n)", "return t;", TestName = "ALoop")]
    [TestCase("c_while", "while (k < 5)", "k = k + 2;", TestName = "AWhileInASubLambda")]
    [TestCase("c_sel", "switch (v)", "case 1:", TestName = "ASelectCase")]
    [TestCase("c_try", "try", "catch (Exception ex)", TestName = "ATry")]
    [TestCase("c_tryfin", "finally", "log = log + \"b\";", TestName = "ATryFinally")]
    [TestCase("j_exitsub", "return;", "hits = hits + v;", TestName = "AnExitSub")]
    public void ABlockAfterTheEntryBlock_IsWrittenInTheLambda(string id, string firstFragment, string secondFragment)
    {
        foreach (var pipeline in Pipelines)
        {
            var csharp = EmitProbe(id, pipeline);
            var block = Blocks(csharp).Single();
            Assert.Multiple(() =>
            {
                Assert.That(block.Body.Any(l => l.StartsWith(firstFragment, StringComparison.Ordinal)), Is.True, $"{id} {pipeline}: no `{firstFragment}`:\n{csharp}");
                Assert.That(block.Body.Any(l => l.StartsWith(secondFragment, StringComparison.Ordinal)), Is.True, $"{id} {pipeline}: no `{secondFragment}`:\n{csharp}");
                Assert.That(block.Body.Length, Is.GreaterThan(1), $"{id} {pipeline}: an empty block is the defect:\n{csharp}");
            });
        }
    }

    // ============================================================================================
    // The EXACT text of representatives: the statement, its indentation, and the `#line`s around it
    // ============================================================================================

    /// <summary>
    /// ⭐ The whole statement, byte for byte, with the `#line`s: the block sits on the line the statement starts at, its statements are ONE level deeper
    /// (16 spaces under a 12-space statement), its closing brace is at the statement's level, and NO `#line` is written between them — the lambda's own
    /// statements have source lines too (5 and 6), and a `#line` there would put the debugger's line mapping inside a lambda body that C# maps by the
    /// statement. The `#line`s BEFORE (line 5) and AFTER (line 7) are written where they were. Kills M6 (the `#line` guard removed) and an indentation off by one.
    /// </summary>
    [Test]
    public void TheBlock_IsIndentedOneDeeper_HasNoLineDirectiveInside_AndTheLinesAroundItAreWhereTheyWere()
    {
        var source = Probe("c_asg");
        foreach (var (name, emit) in EntryPoints().Take(2))
        {
            var csharp = emit(source);
            var expected =
                "#line 5 \"Prog.bas\"\n" +
                "            f = () => {\n" +
                "                n = n + 1;\n" +
                "                return 5;\n" +
                "            };\n" +
                "#line 7 \"Prog.bas\"\n" +
                "            Console.WriteLine(f());\n";
            Assert.That(csharp, Does.Contain(expected), $"{name}:\n{csharp}");
        }
    }

    /// <summary>
    /// Nested block lambdas: each is one level deeper than the line it sits on, and closes at that level. (Kills an indentation mutant of the block writer, which
    /// the single-level pin above also kills; here it is the SECOND level that says the base is the line's indentation and not a constant.)
    /// </summary>
    [Test]
    public void NestedBlocks_AreEachOneLevelDeeper()
    {
        foreach (var pipeline in Pipelines)
        {
            var csharp = EmitProbe("d_nest", pipeline);
            var expected =
                "            outer = (int k) => {\n" +
                "                Action<int> inner = default!;\n" +
                "                inner = (int j) => {\n" +
                "                    total = total + j;\n" +
                "                };\n" +
                "                inner(k);\n" +
                "                inner(k * 10);\n" +
                "            };\n";
            Assert.That(WithoutDirectivesText(csharp), Does.Contain(expected), $"{pipeline}:\n{csharp}");
        }
    }

    private static string WithoutDirectivesText(string csharp) => string.Join("\n", WithoutDirectives(csharp)) + "\n";

    /// <summary>
    /// A lambda in the arguments of <c>MyBase.New(...)</c> and one in a module global's initialiser are written at the level of the line they sit on: the
    /// constructor's `: base((int x) => {` and the field's `internal static Action bump = () => {`.
    /// </summary>
    [Test]
    public void ALambdaInABaseConstructorCall_AndInAModuleGlobalInitialiser_IsWrittenWhereItSits()
    {
        foreach (var pipeline in Pipelines)
        {
            var mybase = WithoutDirectivesText(EmitProbe("e_mybase", pipeline));
            Assert.That(mybase, Does.Contain(
                "        public D(int k) : base((int x) => {\n" +
                "            int t = 0;\n" +
                "            t = x * k;\n" +
                "            return t + 1;\n" +
                "        })\n"), $"{pipeline}:\n{mybase}");

            var global = WithoutDirectivesText(EmitProbe("k_global", pipeline));
            Assert.That(global, Does.Contain(
                "        internal static Action bump = () => {\n" +
                "            counter = counter + 3;\n" +
                "            Console.WriteLine(\"bump \" + counter);\n" +
                "        };\n"), $"{pipeline}:\n{global}");
            Assert.That(global, Does.Contain(
                "        internal static Func<int, int> calc = (int v) => {\n" +
                "            int t = 0;\n" +
                "            t = v + counter;\n" +
                "            return t << 1;\n" +
                "        };\n"), $"{pipeline}:\n{global}");
        }
    }

    // ============================================================================================
    // `#line`: never inside a lambda body — over every program of the run fixture
    // ============================================================================================

    /// <summary>
    /// ⭐ Mutant M6 changed NO program's behaviour (162 of 236 cells differ in TEXT only: `#line` lines inside lambda bodies), so only a shape test sees it. Every
    /// block of every program in <see cref="LambdaBodyProbes"/>, standard and aggressive, holds no `#line`; and a program with a lambda is not written WITHOUT
    /// `#line` altogether (the directives outside are still there).
    /// </summary>
    [Test]
    public void NoLineDirective_IsWrittenInsideAnyLambdaBody()
    {
        var failures = new List<string>();
        var inspected = 0;
        foreach (var probe in LambdaBodyProbes.All)
            foreach (var pipeline in Pipelines)
            {
                var csharp = EmitProbe(probe.Id, pipeline);
                var blocks = Blocks(csharp);
                inspected += blocks.Count;
                foreach (var block in blocks.Where(b => b.Lines.Any(l => l.TrimStart().StartsWith("#line", StringComparison.Ordinal))))
                    failures.Add($"{probe.Id} {pipeline}: a #line inside a lambda body:\n{block.Text}");

                if (blocks.Count > 0 && !Regex.IsMatch(csharp, "#line [0-9]+ \"Prog.bas\""))
                    failures.Add($"{probe.Id} {pipeline}: no #line outside the lambda either");
            }

        Assert.That(failures, Is.Empty, string.Join("\n", failures));
        Assert.That(inspected, Is.GreaterThan(120), "the sweep must have looked at the lambda blocks of the table");
    }

    // ============================================================================================
    // A lambda's own Dim is declared INSIDE the lambda
    // ============================================================================================

    /// <summary>
    /// ⭐ #165. The lambda's own locals are declared at the top of ITS block (`int t = 0;`) and nowhere in the enclosing function. A one-block Function lambda
    /// whose own Dim has no initialiser (<c>n_uninit</c>) is a BLOCK, never `(a) => t + a` with an undeclared `t` (mutant M8: CS0103). Two lambdas that
    /// declare the same name each declare it in their own block.
    /// </summary>
    [TestCase("n_uninit", "t", "int t = 0;", TestName = "ADimWithNoInitialiser")]
    [TestCase("j_names", "t", "int t = 0;", TestName = "TheSameNameInTwoLambdas")]
    [TestCase("n_uninit2", "s", "string s = \"\";", TestName = "AStringDim")]
    public void ALambdasOwnDim_IsDeclaredInTheLambda_NotInTheEnclosingFunction(string id, string name, string declaration)
    {
        foreach (var pipeline in Pipelines)
        {
            var csharp = EmitProbe(id, pipeline);
            var blocks = Blocks(csharp);
            Assert.That(blocks, Is.Not.Empty, csharp);
            foreach (var block in blocks)
                Assert.That(block.Body, Does.Contain(declaration), $"{id} {pipeline}: the lambda declares its own {name}:\n{csharp}");

            Assert.That(OutsideTheLambdas(csharp).Where(l => Regex.IsMatch(l, $@"\b(int|string) {name}\b")), Is.Empty,
                $"{id} {pipeline}: the enclosing function must not declare the lambda's {name}:\n{csharp}");
        }
    }

    /// <summary>
    /// A sized array Dim in a lambda is allocated where it is declared (`int[] a = new int[4];`), as a module function's is: nothing else allocates it
    /// (`DeclareLocals(sizedArrays: true)`).
    /// </summary>
    [Test]
    public void ASizedArrayDimInALambda_IsAllocatedAtItsDeclaration()
    {
        foreach (var pipeline in Pipelines)
        {
            var csharp = EmitProbe("j_lambdaarr", pipeline);
            Assert.That(Blocks(csharp).Single().Body, Does.Contain("int[] a = new int[4];"), $"{pipeline}:\n{csharp}");
        }
    }

    // ============================================================================================
    // A statement-form ByRef call in a lambda writes `ref`; a used call is written once
    // ============================================================================================

    /// <summary>
    /// ⭐ #166, statement form. <c>Inc(n)</c> on a captured local, <c>Inc(a)</c> on the lambda's own parameter: `Inc(ref n);`, `Inc(ref a);` — before, `Inc(n);`
    /// (CS1620). Mutant: the lambda's calls written without the function emitter's ByRef rule.
    /// </summary>
    [TestCase("h166_byref", "Inc(ref n);", TestName = "OnACapturedLocal")]
    [TestCase("h166_byref_par", "Inc(ref a);", TestName = "OnTheLambdasParameter")]
    [TestCase("h166_byref_loc", "Inc(ref t);", TestName = "OnTheLambdasOwnLocal")]
    public void AStatementFormByRefCall_InALambda_WritesRef(string id, string call)
    {
        foreach (var pipeline in Pipelines)
        {
            var csharp = EmitProbe(id, pipeline);
            Assert.That(Blocks(csharp).Single().Body, Does.Contain(call), $"{id} {pipeline}:\n{csharp}");
        }
    }

    /// <summary>
    /// ⭐ #179. A call whose result is used is written ONCE, inside the expression that uses it: `Tick();` as a statement, and `Tick()` twice in the one
    /// `return` — three calls in all (the program's counter reads 3). Before, each used call was ALSO written as a statement of its own, so the same program
    /// ran five. The Sub lambda shape: `Show(Tick());`, `Tick();`, `Show(Tick() + Tick());`.
    /// </summary>
    [TestCase("g_once", 3, TestName = "InAFunctionLambda")]
    [TestCase("g_once_sub", 4, TestName = "InASubLambda")]
    public void EveryCallInALambda_IsWrittenOnce(string id, int calls)
    {
        foreach (var pipeline in Pipelines)
        {
            var csharp = EmitProbe(id, pipeline);
            var block = Blocks(csharp).Single();
            Assert.That(Regex.Matches(string.Join("\n", block.Body), @"\bTick\(\)").Count, Is.EqualTo(calls), $"{id} {pipeline}:\n{block.Text}");
        }

        Assert.That(EmitProbe("g_once_sub").Split('\n').Select(l => l.Trim()), Does.Contain("Show(Tick() + Tick());"));
    }

    // ============================================================================================
    // The enclosing function's text AFTER a lambda is what it would be without the lambda's body
    // ============================================================================================

    /// <summary>The same function around two different lambdas. The text from the first `f();` on is compared.</summary>
    private const string EnclosingFunction = """
        Dim g As Integer = 5

        Function Calc(v As Integer) As Integer
            Console.WriteLine("calc " & v)
            Return v * 2
        End Function

        Sub Main()
            Dim n As Integer = 0
            Dim a As Integer = Calc(1) + 1
            Dim f = {LAMBDA}
            f()
            Dim b As Integer = Calc(a) + n + g
            Dim h = Function() a + b
            f()
            Console.WriteLine(h() & " " & n & " " & g)
        End Sub
        """;

    private const string BusyLambda = """
        Sub()
                        Dim G As Integer = 7
                        Dim t As Integer = Calc(2)
                        For i As Integer = 1 To 3
                            If i Mod 2 = 1 Then
                                n = n + G
                            End If
                        Next
                        Console.WriteLine("f " & n & " " & t)
                    End Sub
        """;

    /// <summary>
    /// ⭐ Everything the lambda's emission touches — the use counts, the materialised temps, the temp definitions, the name tables, the loop and exit bookkeeping,
    /// the processed blocks — is put back (<c>ExitLambdaScope</c>): the text after a lambda with its own Dims (<c>G</c> spelled like the module global
    /// <c>g</c>, a call and a loop in its body) is the text after a lambda that writes one variable. Kills M4 (no restore at all) and M4b (the name tables not
    /// restored: the later `g` read comes out `G`, CS0103).
    /// </summary>
    [Test]
    public void TheEnclosingFunction_AfterALambda_IsWhatItWouldBeWithoutTheLambdasBody()
    {
        foreach (var pipeline in Pipelines)
        {
            var busy = WithoutDirectives(Emit(EnclosingFunction.Replace("{LAMBDA}", BusyLambda), pipeline));
            var trivial = WithoutDirectives(Emit(EnclosingFunction.Replace("{LAMBDA}", "Sub() n = n + 1"), pipeline));

            var afterBusy = busy.SkipWhile(l => l.Trim() != "f();").ToArray();
            var afterTrivial = trivial.SkipWhile(l => l.Trim() != "f();").ToArray();
            Assert.That(afterBusy, Is.Not.Empty, $"{pipeline}: no `f();`:\n{string.Join("\n", busy)}");
            Assert.That(afterBusy, Is.EqualTo(afterTrivial), $"{pipeline}:\n{string.Join("\n", busy)}\n------\n{string.Join("\n", trivial)}");
            Assert.That(string.Join("\n", afterBusy), Does.Contain("+ g;").Or.Contain("+ g)"), $"{pipeline}: the global is still read as `g`:\n{string.Join("\n", afterBusy)}");
        }
    }

    /// <summary>
    /// ⭐ The name-leak probes. <c>n_leakparam</c>: a lambda PARAMETER <c>G</c> beside a module global <c>g</c>; <c>n_leaklocal</c>: a lambda's own <c>Dim T</c> beside
    /// a global <c>t</c>; <c>n_leaklocal2</c>: the same with a class FIELD <c>t</c>. The name map folds names case-insensitively, so a name left in it after the
    /// lambda made the next read of the global come out as the lambda's spelling (`T`, `G`: CS0103). The text outside the lambdas holds no `T`/`G` at all.
    /// Kills M4b (the name tables not restored).
    /// </summary>
    [TestCase("n_leakparam", "g", "G", TestName = "AParameterSpelledLikeAGlobal")]
    [TestCase("n_leaklocal", "t", "T", TestName = "ADimSpelledLikeAGlobal")]
    [TestCase("n_leaklocal2", "t", "T", TestName = "ADimSpelledLikeAField")]
    public void ALambdasName_DoesNotLeakIntoTheEnclosingFunction(string id, string own, string lambdas)
    {
        foreach (var pipeline in Pipelines)
        {
            var csharp = EmitProbe(id, pipeline);
            var outside = OutsideTheLambdas(csharp).Where(l => !l.Contains("=>")).ToArray();
            Assert.That(outside.Where(l => Regex.IsMatch(l, $@"\b{lambdas}\b")), Is.Empty,
                $"{id} {pipeline}: `{lambdas}` is the lambda's, and must not appear outside it:\n{string.Join("\n", outside)}");
            Assert.That(outside.Where(l => Regex.IsMatch(l, $@"\b{own}\b")), Is.Not.Empty,
                $"{id} {pipeline}: the enclosing function still reads `{own}`:\n{string.Join("\n", outside)}");
        }
    }

    /// <summary>
    /// ⭐ A lambda in the MIDDLE arm of an ElseIf chain. The chain's arms share one merge block, and the enclosing function holds a claim on it for as long as the
    /// chain is open (<c>_pendingIfMerges</c>); <c>ExitLambdaScope</c> puts the claim back. Without it the arm after the lambda takes the merge for its own and
    /// writes the loop's continuation (`i = i + 1;`) INSIDE the last else — the loop then never ends (mutant M21; the run fixture sees it as <c>hung</c>). The
    /// text is read: the continuation is at the indentation of the chain's first `if`, after the chain closes.
    /// </summary>
    [Test]
    public void AnElseIfChain_WithALambdaInItsMiddleArm_ClosesBeforeTheLoopsContinuation()
    {
        foreach (var pipeline in Pipelines)
        {
            var lines = EmitProbe("p_elseif_lambda", pipeline).Split('\n').Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith("#line", StringComparison.Ordinal)).ToArray();
            var head = Array.FindIndex(lines, l => l.Trim() == "if (i == 1)");
            Assert.That(head, Is.GreaterThanOrEqualTo(0), string.Join("\n", lines));
            var steps = lines.Select((l, i) => (l, i)).Where(t => t.l.Trim() == "i = i + 1;").ToArray();
            Assert.That(steps, Has.Length.EqualTo(1), $"{pipeline}: the loop's increment is written once:\n{string.Join("\n", lines)}");
            Assert.That(IndentOf(steps[0].l), Is.EqualTo(IndentOf(lines[head])),
                $"{pipeline}: the loop's increment follows the chain at the chain's own level:\n{string.Join("\n", lines)}");
            Assert.That(steps[0].i, Is.GreaterThan(Array.FindIndex(lines, l => l.Trim() == "n = n + 1000;")), $"{pipeline}: after the last arm");
        }
    }

    // ============================================================================================
    // Every program of the run fixture compiles; both entry points write the same block
    // ============================================================================================

    private static readonly Lazy<ImmutableArray<MetadataReference>> References = new(() =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToImmutableArray());

    private static string[] RoslynErrors(string csharp)
    {
        var compilation = CSharpCompilation.Create(
            "LambdaShape_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(csharp) },
            References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToArray();
    }

    /// <summary>
    /// ⭐ Roslyn is handed what the compiler writes for every program of <see cref="LambdaBodyProbes"/> (74), through the standard and the aggressive pipeline,
    /// and must find nothing wrong. Compiled, never run: CS1643 (a block lambda with no return on a path — the old `{ ; }`), CS0103 (a lambda's Dim not declared, a
    /// name that leaked), CS0131 (a write to a name the emitter did not map) and CS1620 (a ByRef argument without `ref`) were the old failures, so this is
    /// also the cheap, hang-free kill of every mutant that breaks a name table.
    /// </summary>
    [Test]
    public void TheEmittedCSharp_OfEveryProgramInTheTable_Compiles()
    {
        var failures = new List<string>();
        foreach (var probe in LambdaBodyProbes.All)
            foreach (var pipeline in Pipelines)
            {
                var errors = RoslynErrors(EmitProbe(probe.Id, pipeline));
                if (errors.Length > 0) failures.Add($"{probe.Id} {pipeline}: {errors[0]}");
            }

        Assert.That(failures, Is.Empty, string.Join("\n", failures));
    }

    /// <summary>
    /// "Test both entry points": the block of a lambda is the same text through the standard pipeline, the aggressive one and
    /// <c>CompileProjectFiles</c> (aggressive) for these programs, and Roslyn accepts the project entry point's text. (The CLI's text is compared in the run
    /// fixture, which spawns it.)
    /// </summary>
    [TestCase("c_asg", TestName = "AWriteBeforeTheReturn")]
    [TestCase("d_nest", TestName = "NestedLambdas")]
    [TestCase("h166_byref_par", TestName = "AByRefCall")]
    [TestCase("e_mybase", TestName = "MyBaseArguments")]
    [TestCase("k_global", TestName = "AModuleGlobalInitialiser")]
    public void TheProjectEntryPoint_WritesTheSameBlocks_AsThePipelines(string id)
    {
        var standard = Blocks(EmitProbe(id)).Select(b => b.Lines.Select(l => l.TrimEnd()).ToArray()).ToList();
        var aggressive = Blocks(EmitProbe(id, Pipeline.Aggressive)).Select(b => b.Lines.Select(l => l.TrimEnd()).ToArray()).ToList();
        var csharp = EmitProbeViaProject(id);
        var project = Blocks(csharp).Select(b => b.Lines.Select(l => l.TrimEnd()).ToArray()).ToList();

        Assert.That(standard, Is.Not.Empty);
        Assert.That(aggressive, Is.EqualTo(standard), "aggressive vs standard");
        Assert.That(project, Is.EqualTo(standard), "CompileProjectFiles vs standard");
        Assert.That(RoslynErrors(csharp), Is.Empty, csharp);
    }

    // ============================================================================================
    // THE RULE: no lambda test runs emitted C# in the TEST HOST
    // ============================================================================================

    /// <summary>
    /// ⛔⛔ A lambda body holds loops, and the in-process runner has no timeout: a C# loop that never ends there freezes the whole test host (#256). So the run
    /// fixture never calls it: every C# run is <c>hangSafe: true</c> (<see cref="CSharpProcessRunner"/>). This reads the fixture's code (comments dropped) and
    /// fails on a call of the in-process runners, and on a <c>TempExec.Run</c> / <c>AssertMatchesInEveryEntryPoint</c> call that names C# without
    /// <c>hangSafe: true</c>. (The controls' call takes a backend VARIABLE; <c>TheTable_HasItsRows</c> pins that it never holds C#.)
    /// </summary>
    [Test]
    public void TheRunFixture_NeverRunsEmittedCSharpInTheTestHost()
    {
        var path = Path.Combine(SampleSources.RepoRoot(), "VisualGameStudio.Tests", "Compiler", "LambdaBodyEmissionExecutionTests.cs");
        var code = string.Join("\n", File.ReadAllLines(path).Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        var problems = new List<string>();
        foreach (var call in new[] { "RunEmittedCSharp(", "RunEmittedCSharpText(", "RunEmittedCSharpAggressive(", "RunsOnEveryBackend(", "RunsOnEveryBackendAggressive(" })
            if (code.Contains(call, StringComparison.Ordinal)) problems.Add($"a call of the in-process runner {call}");

        var csharpRuns = Regex.Matches(code, @"TempExec\.(?:Run|AssertMatchesInEveryEntryPoint)\(\s*Bk\.CSharp(?<args>[^;]*);");
        Assert.That(csharpRuns.Count, Is.GreaterThanOrEqualTo(2), "the guard must have found the fixture's C# runs");
        foreach (Match m in csharpRuns)
            if (!m.Groups["args"].Value.Contains("hangSafe: true", StringComparison.Ordinal))
                problems.Add($"a C# run that is not hang-safe: {m.Value}");

        Assert.That(problems, Is.Empty, string.Join("\n", problems));
    }
}
