using System;
using System.Collections.Generic;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.CodeGen.MSIL;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using BasicLang.Compiler.SemanticAnalysis;
using NUnit.Framework;
using VisualGameStudio.Tests.Compiler;

namespace VisualGameStudio.Tests.Msil;

// =====================================================================================
//  Task #155 / ADR-0010 — the D9 refusals, D8's "neither" throw, D1's isolation contract, and
//  the verifier sweep. NONE of these need ilasm: MsilHarness.CompileToIl never calls
//  RequireIlasm, so a refusal (or a verifier failure) is caught at Generate() time, in-process.
//  This fixture therefore carries NO [Category("Integration")] and runs in the fast subset —
//  matching "compile-only tests need no ilasm" in the test-writer brief. Every message string
//  asserted below was measured against this working tree (see the doc comment on each test);
//  none is guessed from reading ClosureLowering.cs alone.
// =====================================================================================

[TestFixture]
public class ClosureLoweringRefusalTests
{
    private static ForeignFeatureException CompileAndCaptureRefusal(string source, bool aggressive = false) =>
        Assert.Throws<ForeignFeatureException>(() => MsilHarness.CompileToIl(source, aggressive: aggressive));

    // ---- R1: ByRef capture (D4) ------------------------------------------------------------

    private const string R1Source = """
        Sub Work(ByRef n As Integer)
            Dim f = Sub() n = n + 1
            f()
        End Sub
        Sub Main()
            Dim v As Integer = 1
            Work(v)
            Console.WriteLine(v)
        End Sub
        """;

    /// <summary>
    /// ⛔⛔ <b>THIS PIN MOVED (task #174).</b> Before #174, a ByRef parameter captured by a lambda
    /// reached MSIL's OWN "D4" refusal inside <c>ClosureLowering</c>/<c>MSILBackend</c> — the
    /// only place VB's rule (BC36639) was enforced at all, and with a non-VB message. #174 now
    /// reports VB's own BC36639 at the FRONT END, for every backend, before any IR is even built:
    /// <c>MsilHarness.CompileToIl</c>'s own <c>Assert.That(analyzer.Analyze(ast), Is.True, …)</c>
    /// now fails this shape at that assertion, never reaching <c>MSILCodeGenerator</c> at all, so
    /// <c>Assert.Throws&lt;ForeignFeatureException&gt;</c> no longer holds. Re-measured directly:
    /// <c>SemanticAnalyzer.Analyze</c> reports BC36639 at the write (line 2), naming 'n'.
    /// <see cref="R1_BackstopStillThrows_WhenFedUncheckedIr"/> keeps the MSIL backend's own D4
    /// refusal covered, from the one place left that can still reach it.
    /// </summary>
    [Test]
    public void R1_ByRefParameterCaptured_RefusedByTheFrontEnd_WithBC36639()
    {
        var ast = new Parser(new Lexer(R1Source).Tokenize()).Parse();
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);

        var match = analyzer.Errors.FirstOrDefault(e => e.ErrorCode == "BC36639");
        Assert.That(match, Is.Not.Null,
            "expected BC36639; got: " + string.Join(" | ", analyzer.Errors.Select(e => $"{e.ErrorCode}:{e.Message}")));
        Assert.That(match!.Message, Does.Contain("ByRef").And.Contain("'n'"));
    }

    /// <summary>The MSIL backend's own D4 refusal (<c>ClosureLowering</c>/<c>MSILBackend</c>) is
    /// still THERE and still throws — only reachable, post-#174, from IR the front end's own gate
    /// was bypassed for (the same <c>CompileToIlFromUncheckedIr</c> idiom task #178 used for its
    /// own moved MSIL pins), never from a checked compile. #174 touches only
    /// <c>SemanticAnalyzer.cs</c>'s diagnostics, so the IR this produces is exactly what a
    /// pre-#174 checked compile produced.</summary>
    [Test]
    public void R1_BackstopStillThrows_WhenFedUncheckedIr()
    {
        var ast = new Parser(new Lexer(R1Source).Tokenize()).Parse();
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);   // errors intentionally IGNORED -- see this test's own summary
        var module = new IRBuilder(analyzer).Build(ast, "MsilProbe");

        var ex = Assert.Throws<ForeignFeatureException>(() => new MSILCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("ByRef"));
        Assert.That(ex.Message, Does.Contain("'n'"));
        Assert.That(ex.Message, Does.Contain("D4"));
    }

    // ---- R2/R3: a lambda inside an Iterator or Async creator (D9) -------------------------

    [Test]
    public void R2_LambdaInsideIteratorFunction_Refused()
    {
        var ex = CompileAndCaptureRefusal("""
            Iterator Function Gen() As IEnumerable(Of Integer)
                Dim k As Integer = 1
                Dim f = Function() k + 1
                Yield f()
            End Function
            Sub Main()
                For Each v As Integer In Gen()
                    Console.WriteLine(v)
                Next
            End Sub
            """);
        Assert.That(ex.Message, Does.Contain("Iterator"));
        Assert.That(ex.Message, Does.Contain("'Gen'"));
        Assert.That(ex.Message, Does.Contain("D9"));
    }

    [Test]
    public void R3_LambdaInsideAsyncFunction_Refused()
    {
        var ex = CompileAndCaptureRefusal("""
            Async Function Work() As Task(Of Integer)
                Dim k As Integer = 1
                Dim f = Function() k + 1
                Return f()
            End Function
            Sub Main()
                Console.WriteLine(Work().Result)
            End Sub
            """);
        Assert.That(ex.Message, Does.Contain("Async"));
        Assert.That(ex.Message, Does.Contain("'Work'"));
        Assert.That(ex.Message, Does.Contain("D9"));
    }

    // ---- R5: N9 — a lambda declares a name it also uses from the creator (D3) -------------

    private const string R5Source = """
        Sub Main()
            Dim n As Integer = 1
            Dim f = Sub()
                        Console.WriteLine(n)
                        Dim n As Integer = 5
                        Console.WriteLine(n)
                    End Sub
            f()
        End Sub
        """;

    /// <summary>
    /// ⛔⛔ <b>THIS PIN MOVED (task #174).</b> This EXACT shape — a lambda <c>Dim</c> that hides a
    /// name declared in an ENCLOSING block of the creator, which the lambda ALSO reads before its
    /// own declaration — is precisely BC30616 ("Variable 'n' hides a variable in an enclosing
    /// block."), reported at the FRONT END before any IR is built. Before #174 this reached
    /// MSIL's own "N9" refusal (ADR-0010 D3) with a non-VB message; now
    /// <c>SemanticAnalyzer.Analyze</c> reports BC30616 directly, at the lambda's own <c>Dim n</c>
    /// (line 5), and <c>MsilHarness.CompileToIl</c>'s own front-end assertion fails first, so
    /// <c>Assert.Throws&lt;ForeignFeatureException&gt;</c> no longer holds.
    /// <see cref="R5_BackstopStillReached_ByALambdasOwnForEachVariable_X1_NotByADim"/> and
    /// <see cref="R5_BackstopStillReached_ByALambdasOwnCatchVariable_X3_NotBySiblingBlockHiding_E12"/> keep the MSIL
    /// backend's own N9 refusal covered, from the two shapes left that can still reach it (⭐ #229: a Dim is not
    /// one of them any more — see those tests).
    /// </summary>
    [Test]
    public void R5_LambdaDeclaresNameItAlsoUsesFromCreator_RefusedByTheFrontEnd_WithBC30616()
    {
        var ast = new Parser(new Lexer(R5Source).Tokenize()).Parse();
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);

        var match = analyzer.Errors.FirstOrDefault(e => e.ErrorCode == "BC30616");
        Assert.That(match, Is.Not.Null,
            "expected BC30616; got: " + string.Join(" | ", analyzer.Errors.Select(e => $"{e.ErrorCode}:{e.Message}")));
        Assert.That(match!.Message, Does.Contain("'n'"));
    }

    /// <summary>
    /// ⭐ MOVED PIN (#229; was <c>R5_BackstopStillThrows_WhenFedUncheckedIr</c>). The MSIL backend's own N9 refusal is still THERE
    /// and still throws, but a lambda's own <c>Dim</c> no longer reaches it: R5's lambda `Dim n` beside the creator's `n` is the IR
    /// local `n_1` now (<c>IRBuilder.EmittedLocalName</c>, ADR-0014 Amendment A-229), so nothing is declared twice and the unchecked IR
    /// this test used to feed the generator builds. What still reaches the backstop is a lambda's own FOR EACH variable: X1
    /// declares `For Each x` in a lambda while its creator's `x` is captured by another. BasicLang accepts X1 on purpose (VB
    /// rejects it, BC30616; C#, JavaScript and C++ print 6), so a CHECKED compile reaches the refusal, no bypass needed.
    /// </summary>
    [Test]
    public void R5_BackstopStillReached_ByALambdasOwnForEachVariable_X1_NotByADim()
    {
        var ex = CompileAndCaptureRefusal(CppClosurePrograms.X1);
        Assert.That(ex.Message, Does.Contain("N9"));
        Assert.That(ex.Message, Does.Contain("'x'"));

        // The shape that used to be the unchecked-IR pin: R5's Dim, its front-end error IGNORED. It builds now.
        var ast = new Parser(new Lexer(R5Source).Tokenize()).Parse();
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);   // BC30616 intentionally IGNORED -- see this test's own summary
        var module = new IRBuilder(analyzer).Build(ast, "MsilProbe");
        Assert.That(() => new MSILCodeGenerator().Generate(module), Throws.Nothing,
            "a lambda's own Dim is the IR local 'n_1' now: the N9 backstop must not see it");
    }

    /// <summary>
    /// ⭐ MOVED PIN (#229; was <c>R5_BackstopStillReached_ByASiblingBlockHiding_E12</c>). #174's lexical-scope walk follows only the
    /// blocks OPEN around a lambda's declaration, so a SIBLING block's <c>Dim x</c> (E12_later_sibling: the lambda declares `x`, and an
    /// <c>If</c> that comes later declares another) is not refused at the front end, and <c>ClosureLowering</c>'s coarser,
    /// whole-function N9 check used to refuse it. ⭐ The lambda's `x` is `x_1` now: E12 builds on MSIL (and runs, see
    /// <c>SiblingDimSameNameExecutionTests</c>), and the backstop is reached instead by X3, a lambda's own CATCH variable
    /// (`Catch ex` in a lambda while its creator's `ex` is captured by another; VB rejects it, BC30616, and BasicLang accepts it on
    /// purpose), through a checked compile.
    /// </summary>
    [Test]
    public void R5_BackstopStillReached_ByALambdasOwnCatchVariable_X3_NotBySiblingBlockHiding_E12()
    {
        var ex = CompileAndCaptureRefusal(CppClosurePrograms.X3);
        Assert.That(ex.Message, Does.Contain("N9"));
        Assert.That(ex.Message, Does.Contain("'ex'"));

        var ast = new Parser(new Lexer(CppClosurePrograms.N9LaterSibling).Tokenize()).Parse();
        var analyzer = new SemanticAnalyzer();
        Assert.That(analyzer.Analyze(ast), Is.True,
            "#174 must NOT refuse this at the front end (E12 is a SIBLING block, not an "
            + "ancestor): " + string.Join(" | ", analyzer.Errors.Select(e => e.ToString())));
        Assert.That(() => MsilHarness.CompileToIl(CppClosurePrograms.N9LaterSibling), Throws.Nothing,
            "the lambda's own 'x' is 'x_1' now: the N9 backstop must not see a sibling block's Dim");
    }

    // ---- R6: #169/ADR-0013 — a name matches a lambda parameter case-insensitively but not
    //      exactly, D3's backstop obligation --------------------------------------------------

    /// <summary>
    /// ⛔⛔ <b>THIS PIN MOVED.</b> Before ADR-0013 (#169), this shape reached MSIL's own "differs
    /// only by case" backstop (<c>ClosureLowering.BuildEnvironments</c>) because the IR builder
    /// re-resolved a bare name through its own Ordinal maps: the body's <c>X</c> never found the
    /// lambda's own <c>x</c> (a case-sensitive lookup), so it silently bound the CREATOR's <c>X</c>
    /// instead — exactly the shape the backstop was written to catch and refuse rather than
    /// mis-emit. ADR-0013 D1 closes the hole the backstop was standing in for: the semantic
    /// analyzer now resolves <c>X</c> inside the lambda body case-insensitively to the lambda's
    /// OWN parameter <c>x</c> (the innermost declaration wins, VB's own rule), and the IR builder binds
    /// through that record — so the shape the backstop used to catch can no longer be PRODUCED by
    /// any front-end-analyzed program at all. ADR-0013's own Obligations section says exactly
    /// this: "after #169 it must never fire on a program the analyzer accepted; a firing means a
    /// reference bypassed the binding."
    ///
    /// <para>RE-MEASURED against this working tree: the program below now compiles AND RUNS on
    /// MSIL, printing <c>10</c> then <c>2</c> (VB's own answer — the lambda's own parameter
    /// <c>x</c> = 1, <c>X + 1</c> = 2). This fixture is compile-only by design (see its own header
    /// — no <c>ilasm</c>, fast subset), so this test only proves the backstop is no longer
    /// REACHED, which needs no <c>ilasm</c>.</para>
    ///
    /// <para>⭐ RESHAPED (task #217). The original program had the creator's <c>Dim X</c> in the
    /// lambda's own enclosing block, so the parameter <c>x</c> hid it — VB's BC36641, which the front
    /// end now reports, and <see cref="MsilHarness.CompileToIl"/> asserts a clean analysis. The
    /// creator's <c>X</c> is now a local of a SIBLING block (an <c>If</c> that has closed): it does
    /// not enclose the lambda, vbc accepts the program, and the IR builder still has an <c>X</c> to
    /// confuse with the parameter <c>x</c>, which is what the backstop is the defence against.</para>
    /// </summary>
    [Test]
    public void R6_NameMatchesLambdaParameterOnlyByCase_Task169_NoLongerReachesTheBackstop()
    {
        const string program = """
            Sub Main()
                If True Then
                    Dim X As Integer = 10
                    Console.WriteLine(X)
                End If
                Dim f = Function(x As Integer) X + 1
                Console.WriteLine(f(1))
            End Sub
            """;
        // MsilHarness.CompileToIl asserts a clean semantic analysis and returns IL text; it
        // throws ForeignFeatureException only when MSILCodeGenerator (ClosureLowering included)
        // itself refuses. This used to throw "... differs from its parameter ... only by case";
        // it no longer does.
        Assert.That(() => MsilHarness.CompileToIl(program), Throws.Nothing);
    }

    /// <summary>
    /// The backstop itself (<c>ClosureLowering.BuildEnvironments</c>'s "differs from its
    /// parameter ... only by case" check) is STILL THERE and still throws — ADR-0013's own
    /// Obligation keeps it as a defence in depth, never removes it. It is unreachable from ANY
    /// program a checked front end produces now (<see cref="R6_NameMatchesLambdaParameterOnlyByCase_Task169_NoLongerReachesTheBackstop"/>
    /// is the one shape that used to reach it, and no longer does), so the only way left to
    /// exercise it is IR that bypasses the front end's binding altogether.
    ///
    /// <para>⛔ <b>#178's <c>CompileToIlFromUncheckedIr</c> (<c>MsilInterfacePropertyTests</c>)
    /// does NOT reach it — tried, and confirmed it cannot.</b> That helper ignores the analyzer's
    /// ERROR list, but still RUNS the analyzer, and #169's binding is set at the analyzer's one
    /// recording point (<c>SetNodeSymbol</c>) regardless of whether the program has unrelated
    /// errors elsewhere. VB scope resolution picks the lambda's own parameter (the innermost
    /// declaration) case-insensitively no matter what else is wrong with the program, so a
    /// case-differing reference to a lambda parameter is ALWAYS bound to the parameter now —
    /// there is no analyzed AST, checked or not, that still hands <c>ClosureLowering</c> the
    /// pre-#169 shape. (Confirmed directly: R6's own program, run through the exact same
    /// three-stage pipeline <c>CompileToIlFromUncheckedIr</c> uses, does not throw either — it
    /// has no semantic errors to begin with, so "unchecked" changes nothing for this shape.)</para>
    ///
    /// <para>What DOES still reach it: renaming a lambda's own parameter's
    /// <see cref="IRValue.Name"/> AFTER the IR is built, directly on the built
    /// <see cref="IRFunction"/> — IR the front end never produced, the same kind of direct
    /// post-build tamper <c>NameBindingTests</c>' bound-miss (M4) test makes on
    /// <c>Binding.DeclaredName</c>. The lambda's genuinely captured names are already computed by
    /// the time this runs, so renaming only the PARAMETER (not the captured variable) reproduces
    /// exactly the case-mismatch the backstop's own message describes, without hand-building an
    /// entire <see cref="IRModule"/> from scratch.</para>
    /// </summary>
    [Test]
    public void BackstopStillThrows_WhenALambdaParameterIsRenamedAfterTheIrIsBuilt()
    {
        const string program = """
            Sub Main()
                Dim n As Integer = 1
                Dim bump = Sub(p As Integer) n = n + p
                bump(5)
                Console.WriteLine(n)
            End Sub
            """;
        var ast = new Parser(new Lexer(program).Tokenize()).Parse();
        var analyzer = new SemanticAnalyzer();
        Assert.That(analyzer.Analyze(ast), Is.True,
            "semantic errors:\n" + string.Join("\n", analyzer.Errors.Select(e => e.ToString())));
        var module = new IRBuilder(analyzer).Build(ast, "MsilProbe");

        var lambda = module.Functions.Single(f => f.Name == "__lambda_0");
        // The ONLY hand-tamper: rename the lambda's own parameter from its declared "p" to "N" --
        // case-insensitively equal to the creator's genuinely captured "n", but not Ordinal-equal.
        // No front-end-analyzed program can produce this (see this test's own doc comment).
        lambda.Parameters[0].Name = "N";

        var ex = Assert.Throws<ForeignFeatureException>(() => new MSILCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("'n'").And.Contain("'N'").And.Contain("only by case"));
    }

    // ---- ADR-0018 (#121): the case-guard compares only the creator's VARIABLES the lambda uses -----

    private static IRModule BuildIr(string program)
    {
        var ast = new Parser(new Lexer(program).Tokenize()).Parse();
        var analyzer = new SemanticAnalyzer();
        Assert.That(analyzer.Analyze(ast), Is.True,
            "semantic errors:\n" + string.Join("\n", analyzer.Errors.Select(e => e.ToString())));
        return new IRBuilder(analyzer).Build(ast, "MsilProbe");
    }

    private const string Show = """
        Sub Show(n As Integer)
            Console.WriteLine(CStr(n))
        End Sub

        """;

    /// <summary>
    /// Three programs VB accepts and C#, C++ and JavaScript run, each with a lambda parameter spelled <c>T0</c> whose body mints
    /// temps. IRBuilder's build-time record of that lambda (<c>CapturedVariables</c>) holds its temps' names from BEFORE the renamer
    /// ran, <c>t0</c> among them, and the guard used to compare every name in it with the parameter: <c>t0</c> differs from <c>T0</c>
    /// only by case, so MSIL refused all three (MEASURED on the #121 base, every entry point).
    /// <list type="bullet">
    /// <item><b>LP_T0</b>: the matrix's own cell (<c>NameReservationExecutionTests</c>). Refused whatever the filter, before #121.</item>
    /// <item><b>SiblingForEach</b>: a <c>For Each t0</c> the lambda cannot see. The reservation is function-wide, so the creator OWNS
    /// <c>t0</c>: filtering the old set by ownership alone still refused it (MEASURED).</item>
    /// <item><b>NestedLambda</b>: the <c>T0</c> lambda creates another, whose build-time record is folded into the capture set the
    /// optimizer recomputes: reading that set alone, with ownership, still refused it (MEASURED).</item>
    /// </list>
    /// </summary>
    private static IEnumerable<TestCaseData> UpperCaseLambdaParameterPrograms()
    {
        yield return new TestCaseData(Show + """
            Sub Run(a As Integer)
                Dim f As Func(Of Integer, Integer) = Function(T0 As Integer) -(-T0) + ((T0 * 2) * 0)
                Show(f(a))
            End Sub
            Sub Main()
                Run(5)
            End Sub
            """).SetName("TheCaseGuard_IgnoresATempsStaleName_LP_T0");
        yield return new TestCaseData(Show + """
            Sub Run(lst As List(Of Integer), a As Integer)
                For Each t0 As Integer In lst
                    Show(t0)
                Next
                Dim f As Func(Of Integer, Integer) = Function(T0 As Integer) -(-T0) + ((T0 * 2) * 0)
                Show(f(a))
            End Sub
            Sub Main()
                Dim lst As New List(Of Integer)
                lst.Add(3)
                Run(lst, 5)
            End Sub
            """).SetName("TheCaseGuard_IgnoresATempsStaleName_SiblingForEachT0");
        yield return new TestCaseData(Show + """
            Sub Run(lst As List(Of Integer), a As Integer)
                For Each t0 As Integer In lst
                    Show(t0)
                Next
                Dim f As Func(Of Integer, Integer) = Function(T0 As Integer)
                                                         Dim h As Func(Of Integer) = Function() -(-T0) + ((T0 * 2) * 0)
                                                         Return h()
                                                     End Function
                Show(f(a))
            End Sub
            Sub Main()
                Dim lst As New List(Of Integer)
                lst.Add(3)
                Run(lst, 5)
            End Sub
            """).SetName("TheCaseGuard_IgnoresATempsStaleName_NestedLambda");
    }

    /// <summary>
    /// ⭐ ADR-0018 (#121), direct: a lambda parameter spelled <c>T0</c> whose body mints temps is NOT refused on MSIL, standard or
    /// aggressive. Not vacuous: the stale <c>t0</c> the old guard read is asserted present in IRBuilder's record first.
    /// </summary>
    [TestCaseSource(nameof(UpperCaseLambdaParameterPrograms))]
    public void TheCaseGuard_IgnoresATempsStaleName(string program)
    {
        var lambdas = BuildIr(program).Functions.Where(f => f.IsLambda).ToList();
        Assert.That(lambdas.SelectMany(l => l.CapturedVariables).Select(c => c.name), Does.Contain("t0"),
            "precondition: IRBuilder's build-time record still holds the temp's pre-rename name t0");

        Assert.Multiple(() =>
        {
            Assert.That(() => MsilHarness.CompileToIl(program), Throws.Nothing, "standard passes");
            Assert.That(() => MsilHarness.CompileToIl(program, aggressive: true), Throws.Nothing, "aggressive passes");
        });
    }

    /// <summary>
    /// ⭐ ADR-0018 (#121), direct: the guard still refuses what it exists for — a VARIABLE OF THE CREATOR the lambda uses, spelled
    /// like a lambda parameter in another case — here a temp-SHAPED one, the For Each <c>t0</c> the lambda captures. The shape is
    /// unreachable from source since #169 (<see cref="R6_NameMatchesLambdaParameterOnlyByCase_Task169_NoLongerReachesTheBackstop"/>:
    /// a case-differing reference binds to the parameter), so, as the backstop test above does, the built IR is tampered: the
    /// lambda's parameter <c>p</c> is renamed <c>T0</c> after the build.
    /// </summary>
    [Test]
    public void TheCaseGuard_StillRefusesACreatorVariable_ThatDiffersFromTheParameterOnlyByCase()
    {
        var module = BuildIr(Show + """
            Sub Run(lst As List(Of Integer))
                For Each t0 As Integer In lst
                    Dim f As Func(Of Integer, Integer) = Function(p As Integer) t0 + p
                    Show(f(1))
                Next
            End Sub
            Sub Main()
                Dim lst As New List(Of Integer)
                lst.Add(3)
                Run(lst)
            End Sub
            """);
        var lambda = module.Functions.Single(f => f.Name == "__lambda_0");
        lambda.Parameters[0].Name = "T0";

        var ex = Assert.Throws<ForeignFeatureException>(() => new MSILCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("'t0'").And.Contain("'T0'").And.Contain("only by case"));
    }

    // ---- R9/R17: an arity above the measured cap (D7) --------------------------------------

    [Test]
    public void R9_ActionWithNineTypeArguments_AboveTheCap_Refused()
    {
        var ex = CompileAndCaptureRefusal("""
            Sub Main()
                Dim f As Action(Of Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer) = Sub(a As Integer, b As Integer, c As Integer, d As Integer, e As Integer, g As Integer, h As Integer, i As Integer, j As Integer) Console.WriteLine(a + j)
                f(1, 2, 3, 4, 5, 6, 7, 8, 9)
            End Sub
            """);
        Assert.That(ex.Message, Does.Contain("Action"));
        Assert.That(ex.Message, Does.Contain("9"));
    }

    [Test]
    public void R17_FuncWithTenTypeArguments_AboveTheCap_Refused()
    {
        var ex = CompileAndCaptureRefusal("""
            Sub Main()
                Dim g2 As Func(Of Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer) = Function(a As Integer, b As Integer, c As Integer, d As Integer, e As Integer, g As Integer, h As Integer, i As Integer, j As Integer) a * j
                Console.WriteLine(g2(2, 2, 3, 4, 5, 6, 7, 8, 9))
            End Sub
            """);
        Assert.That(ex.Message, Does.Contain("Func"));
        Assert.That(ex.Message, Does.Contain("10"));
    }

    // ---- R10: a captured variable typed by a generic parameter of the creator (D9) --------

    [Test]
    public void R10_CapturedVariableTypedByCreatorsGenericParameter_Refused()
    {
        var ex = CompileAndCaptureRefusal("""
            Function Wrap(Of T)(v As T) As T
                Dim f = Function() v
                Return f()
            End Function
            Sub Main()
                Console.WriteLine(Wrap(Of Integer)(5))
            End Sub
            """);
        Assert.That(ex.Message, Does.Contain("generic parameter"));
        Assert.That(ex.Message, Does.Contain("'T'"));
        Assert.That(ex.Message, Does.Contain("D9"));
    }

    // ---- R11: a capture set that cannot be listed (D3) -------------------------------------

    /// <summary>Raw inline <c>msil{ }</c> code inside the lambda body makes #122's capture
    /// analysis unable to enumerate what the lambda touches (null capture set) — closure
    /// conversion refuses rather than guess.</summary>
    [Test]
    public void R11_CaptureSetCannotBeEnumerated_RawInlineCode_Refused()
    {
        var ex = CompileAndCaptureRefusal("""
            Sub Main()
                Dim n As Integer = 1
                Dim f = Sub()
                            msil{
                                nop
                            }
                            Console.WriteLine(n)
                        End Sub
                f()
            End Sub
            """);
        Assert.That(ex.Message, Does.Contain("could not be enumerated"));
        Assert.That(ex.Message, Does.Contain("D3"));
    }

    // ---- R12: a captured variable passed ByRef (D9) ----------------------------------------

    [Test]
    public void R12_CapturedVariablePassedByRef_Refused()
    {
        var ex = CompileAndCaptureRefusal("""
            Sub Bump(ByRef n As Integer)
                n = n + 1
            End Sub
            Sub Main()
                Dim n As Integer = 1
                Dim f = Sub() Console.WriteLine(n)
                Bump(n)
                f()
            End Sub
            """);
        Assert.That(ex.Message, Does.Contain("'n'"));
        Assert.That(ex.Message, Does.Contain("ByRef"));
        Assert.That(ex.Message, Does.Contain("'Bump'"));
    }

    // ---- R14: MyBase in a lambda (D9) -------------------------------------------------------

    [Test]
    public void R14_MyBaseInsideLambda_Refused()
    {
        var ex = CompileAndCaptureRefusal("""
            Class Base
                Public Overridable Function Tag() As Integer
                    Return 1
                End Function
            End Class
            Class Derived
                Inherits Base
                Public Overrides Function Tag() As Integer
                    Dim f = Function() MyBase.Tag() + 10
                    Return f()
                End Function
            End Class
            Sub Main()
                Dim d As New Derived()
                Console.WriteLine(d.Tag())
            End Sub
            """);
        Assert.That(ex.Message, Does.Contain("MyBase.Tag"));
    }

    // ---- R15: a When guard reading a captured variable (D9) --------------------------------

    [Test]
    public void R15_WhenGuardReadsCapturedVariable_Refused()
    {
        var ex = CompileAndCaptureRefusal("""
            Sub Main()
                Dim lim As Integer = 5
                Dim f = Sub() lim = lim + 1
                f()
                Dim v As Integer = 7
                Select Case v
                    Case Is > 0 When v > lim + 1
                        Console.WriteLine("big")
                    Case Else
                        Console.WriteLine("small")
                End Select
            End Sub
            """);
        Assert.That(ex.Message, Does.Contain("When"));
        Assert.That(ex.Message, Does.Contain("guard"));
    }

    // ---- (c) D8: a call to a name that is neither a declared procedure nor a delegate value --

    /// <summary>The RUNNING half of this rule (a delegate variable shadowing a module procedure
    /// invokes the delegate) is <c>ClosureLoweringContractTests.D8_DelegateVariable_
    /// ShadowsModuleProcedure_InvokesDelegate</c>, which needs ilasm+dotnet; this half needs
    /// neither. Nothing in this program declares 'Ghost' — measured: the front end does not
    /// itself reject an undefined call, so this exercises MSILBackend's own D8 guard.</summary>
    [Test]
    public void D8_CallToUndeclaredName_NeitherProcedureNorDelegate_Refused()
    {
        var ex = CompileAndCaptureRefusal("""
            Sub Main()
                Ghost()
            End Sub
            """);
        Assert.That(ex.Message, Does.Contain("'Ghost'"));
        Assert.That(ex.Message, Does.Contain("neither a procedure"));
        Assert.That(ex.Message, Does.Contain("D8"));
    }

    // ---- Every refusal fires identically under the aggressive pipeline too ----------------

    /// <summary>
    /// ⛔⛔ <b>THIS PIN MOVED (task #174).</b> One representative refusal (R1), re-asserted with
    /// <c>aggressive: true</c> — meant as a smoke check that the aggressive pipeline does not
    /// somehow dodge the refusal (e.g. by folding the capture away). #174's own BC36639 fires at
    /// <c>SemanticAnalyzer.Analyze</c>, BEFORE any IR is built and so before either pipeline
    /// (standard or aggressive) is even chosen — "aggressive" is moot at this stage by
    /// construction, which is itself the answer to what this test asked: no optimizer choice can
    /// affect a refusal that happens before optimization exists. <c>MsilHarness.CompileToIl(...,
    /// aggressive: true)</c> still calls the SAME front-end assertion <c>CompileToIl(...,
    /// aggressive: false)</c> does, before ever branching on the aggressive flag, so it fails
    /// there identically — <c>Assert.Throws&lt;ForeignFeatureException&gt;</c> no longer holds
    /// for either. The REAL aggressive-pipeline coverage this test's own name promised — that the
    /// refusal survives all the way through the CLI's <c>--optimize</c> and a Release
    /// <c>.blproj</c> build, not merely the in-process front end — now lives in
    /// <c>LambdaBoundaryDiagnosticsExecutionTests.R1_ByRefWrite_CliOptimizeAlsoRefuses_BC36639</c>
    /// and <c>.R1_ByRefWrite_ReleaseBlprojBuildAlsoRefuses_BC36639</c>.
    /// </summary>
    [Test]
    public void ARefusal_FiresIdenticallyRegardlessOfPipelineChoice_BecauseItFiresBeforeEither()
    {
        var ast = new Parser(new Lexer(R1Source).Tokenize()).Parse();
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);

        var match = analyzer.Errors.FirstOrDefault(e => e.ErrorCode == "BC36639");
        Assert.That(match, Is.Not.Null,
            "expected BC36639; got: " + string.Join(" | ", analyzer.Errors.Select(e => $"{e.ErrorCode}:{e.Message}")));

        // Both CompileToIl(aggressive: false) and (aggressive: true) hit the SAME front-end
        // assertion before ever branching on the flag — confirmed identically for both.
        Assert.That(() => MsilHarness.CompileToIl(R1Source, aggressive: false), Throws.TypeOf<AssertionException>());
        Assert.That(() => MsilHarness.CompileToIl(R1Source, aggressive: true), Throws.TypeOf<AssertionException>());
    }
}

// =====================================================================================
//  (e) D1 isolation — ClosureLowering.Run's own contract, independent of MSIL emission.
// =====================================================================================

[TestFixture]
public class ClosureLoweringIsolationTests
{
    private static IRModule BuildOptimizedModule(string source, string moduleName = "Probe")
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty,
            "parse errors:\n" + string.Join("\n", parser.Errors.Select(e => e.Message)));

        var analyzer = new SemanticAnalyzer();
        Assert.That(analyzer.Analyze(ast), Is.True,
            "semantic errors:\n" + string.Join("\n", analyzer.Errors.Select(e => e.ToString())));

        var module = new IRBuilder(analyzer).Build(ast, moduleName);
        var pipeline = new OptimizationPipeline();
        pipeline.AddStandardPasses();
        pipeline.Run(module);
        return module;
    }

    /// <summary>D1's contract, first half: no lambda, no AddressOf, no delegate-value call —
    /// <c>Run</c> must hand back the SAME instance, so a program without delegates reaches the
    /// backend byte-for-byte as before.</summary>
    [Test]
    public void Run_NoLambdaAddressOfOrDelegateCall_ReturnsTheSameModuleInstance()
    {
        var module = BuildOptimizedModule("""
            Sub Main()
                Console.WriteLine("x")
            End Sub
            """);

        var result = ClosureLowering.Run(module);

        Assert.That(result, Is.SameAs(module));
    }

    /// <summary>D1's contract, second half: for a program that DOES have a lambda, <c>Run</c>
    /// must hand back a DIFFERENT instance and must leave the INPUT's own printed IR
    /// byte-identical — the clone, not a mutation-in-place.</summary>
    [Test]
    public void Run_WithLambda_ReturnsADifferentInstance_AndLeavesTheInputsPrintedIrByteIdentical()
    {
        var module = BuildOptimizedModule(ClosureLoweringProbes.L4);
        var printedBefore = new IRPrettyPrinter().Print(module);

        var result = ClosureLowering.Run(module);

        Assert.That(result, Is.Not.SameAs(module),
            "a program with a lambda must get back a LOWERED CLONE, not the same instance.");

        var printedAfter = new IRPrettyPrinter().Print(module);
        Assert.That(printedAfter, Is.EqualTo(printedBefore),
            "ClosureLowering.Run must not mutate the module it was handed — the input's own IR " +
            "must print identically before and after.");
    }

    /// <summary>D1's other stated way to prove invisibility: one module, MSIL generated first
    /// (which runs ClosureLowering internally), THEN C# generated from the SAME module
    /// reference — must give byte-identical C# to generating C# from a fresh, untouched module.
    /// This is the test that would catch mutant (f): the lowering applied to C# as well, or the
    /// clone skipped so the input module is mutated.</summary>
    [Test]
    public void GeneratingMsilThenCSharp_FromOneModule_GivesTheSameCSharpAsCSharpAlone()
    {
        var sharedModule = BuildOptimizedModule(ClosureLoweringProbes.L4);
        _ = new MSILCodeGenerator().Generate(sharedModule);
        var csharpAfterMsil = new ImprovedCSharpCodeGenerator().Generate(sharedModule);

        var freshModule = BuildOptimizedModule(ClosureLoweringProbes.L4);
        var csharpAlone = new ImprovedCSharpCodeGenerator().Generate(freshModule);

        Assert.That(csharpAfterMsil, Is.EqualTo(csharpAlone));
    }
}

// =====================================================================================
//  (f) The verifier on lowered IR — with verification forced on, lowering every contract
//  program must raise no verifier failure, on both pipelines.
// =====================================================================================

[TestFixture]
public class ClosureLoweringVerifierTests
{
    [Test]
    public void LoweringEveryContractProgram_RaisesNoVerifierFailure()
    {
        var previousMode = IRVerifier.Mode;
        IRVerifier.Mode = IRVerifierMode.Throw;
        try
        {
            foreach (var (name, source) in ClosureLoweringProbes.AllContractSources())
            {
                Assert.DoesNotThrow(() => MsilHarness.CompileToIl(source, moduleName: name, aggressive: false),
                    $"{name}, standard pipeline: lowering must not fail IRVerifier.");
                Assert.DoesNotThrow(() => MsilHarness.CompileToIl(source, moduleName: name, aggressive: true),
                    $"{name}, aggressive pipeline: lowering must not fail IRVerifier.");
            }
        }
        finally
        {
            IRVerifier.Mode = previousMode;
        }
    }
}
