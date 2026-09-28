using System;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.CodeGen.CSharp;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task #189 — a <c>Nothing</c> String in <c>&amp;</c> / <c>Write</c>/<c>WriteLine</c> (JS, C#);
/// a <c>Catch</c> variable captured by a lambda (C++). Fixed in <c>JavaScriptBackend.cs</c>,
/// <c>CSharpBackend.cs</c> and <c>CppCodeGenerator.cs</c> (fix commit 381b95ff).
///
/// <para>Pure codegen-TEXT assertions only — no Node, no Roslyn compile-and-run, no C++ compiler.
/// Behaviour (does the emitted program actually PRINT the right thing) is
/// <c>NothingStringTextExecutionTests</c>, <c>[Category("Integration")]</c>. CLAUDE.md's own rule
/// for this codebase: validate codegen through the CLI/IR-optimizer path too, not only the
/// non-optimizing helper — <see cref="CSharpNullConstantCastTests"/> does both for the C# leg,
/// because the C# cast this task adds is reachable ONLY after the optimizer's
/// <c>CopyPropagationPass</c> folds a <c>Nothing</c>-initialized String local into the literal
/// constant <c>ConcatOperand</c> looks for; <c>JsTestSupport.Compile</c> (no optimizer at all)
/// never produces that shape.</para>
/// </summary>
[TestFixture]
public class JsNothingStringConcatTextTests
{
    /// <summary>
    /// A <c>Nothing</c> CONSTANT operand of <c>&amp;</c> — the literal itself, not a variable that
    /// happens to hold it — renders <c>""</c> directly: <c>TextOf</c>'s
    /// <c>nothingIsEmpty &amp;&amp; value is IRConstant {{ Value: null }}</c> arm, checked before
    /// <c>MayHoldNothing</c> even runs.
    /// </summary>
    [Test]
    public void NothingLiteral_InConcat_RendersEmptyStringConstant()
    {
        var js = JsTestSupport.Compile(
            "Sub Main()\n    Console.WriteLine(\"[\" & Nothing & \"]\")\nEnd Sub\n");
        Assert.Multiple(() =>
        {
            Assert.That(js, Does.Contain("(\"[\" + \"\")"), js);
            // NOT a blanket Does.Not.Contain("null") — a bare Nothing literal used directly as a
            // Concat operand types as Object (measured), so UsesTextHelper's scan emits the
            // __blStr PRELUDE regardless (its own body legitimately says "x == null"); that is
            // dead code here, not a rendering defect. The actual rendering of THIS operand is the
            // "" constant asserted above, so check the CONCAT ITSELF carries no "null" token.
            var mainBody = js[js.IndexOf("function Main()", StringComparison.Ordinal)..];
            Assert.That(mainBody, Does.Not.Contain("null"), js);
        });
    }

    /// <summary>A String PARAMETER — can hold <c>Nothing</c> at run time, no data-flow memory of
    /// what the caller passed — gets the <c>?? ""</c> guard as a Concat operand.</summary>
    [Test]
    public void StringParameter_InConcat_IsGuardedWithNullishCoalescing()
    {
        var js = JsTestSupport.Compile(
            "Sub Take(t As String)\n    Console.WriteLine(\"[\" & t & \"]\")\nEnd Sub\n" +
            "Sub Main()\n    Take(Nothing)\nEnd Sub\n");
        Assert.That(js, Does.Match(Regexes.NullishGuard("t")), js);
    }

    /// <summary>A String FIELD, read through an instance — same guard as a parameter.</summary>
    [Test]
    public void StringField_InConcat_IsGuardedWithNullishCoalescing()
    {
        var js = JsTestSupport.Compile(
            "Class H\n    Public F As String\nEnd Class\n" +
            "Sub Main()\n    Dim h As New H()\n    Console.WriteLine(\"[\" & h.F & \"]\")\nEnd Sub\n");
        // The field read lands in its own temp (t2 = h.F) before the guard, per the JS backend's
        // usual one-temp-per-instruction shape — assert the guard on WHATEVER that temp is named,
        // not a specific number, which is an implementation detail this test does not own.
        Assert.That(js, Does.Match(@"\(\(\w+ \?\? """"\) \+ """"\)|\(""\["" \+ \(\w+ \?\? """"\)\)"), js);
    }

    /// <summary>A function CALL RESULT typed String — same guard.</summary>
    [Test]
    public void CallResult_InConcat_IsGuardedWithNullishCoalescing()
    {
        var js = JsTestSupport.Compile(
            "Function F() As String\n    Return Nothing\nEnd Function\n" +
            "Sub Main()\n    Console.WriteLine(\"[\" & F() & \"]\")\nEnd Sub\n");
        Assert.That(js, Does.Match(Regexes.NullishGuard(@"\w+")), js);
    }

    /// <summary>
    /// A String LOCAL that is REASSIGNED (so the optimizer's <c>CopyPropagationPass</c> cannot
    /// fold every use to one constant) still gets the guard on every read — J6's exact shape
    /// (<c>acc = Nothing</c>, then <c>acc = acc &amp; i</c> in a loop). Before #189 this was a
    /// SILENT WRONG ANSWER: JS's own <c>+</c> treated <c>null + 1</c> as NUMERIC addition, so the
    /// loop summed 1+2+3 and printed <c>6</c> instead of VB's <c>"123"</c>.
    /// </summary>
    [Test]
    public void ReassignedLocal_InLoopConcat_IsGuardedWithNullishCoalescing()
    {
        var js = JsTestSupport.Compile(
            "Sub Main()\n    Dim acc As String = Nothing\n" +
            "    For i As Integer = 1 To 3\n        acc = acc & i\n    Next\n" +
            "    Console.WriteLine(acc)\nEnd Sub\n");
        Assert.Multiple(() =>
        {
            Assert.That(js, Does.Contain("acc = ((acc ?? \"\") + i)"),
                "J6's own killer shape: without the guard this is `acc = (acc + i)`, which JS " +
                "runs as NUMERIC addition the moment acc is null.\n" + js);
            Assert.That(js, Does.Contain("console.log((acc ?? \"\"))"), js);
        });
    }

    /// <summary>
    /// A LITERAL, a Concat RESULT and a CStr RESULT as a Concat operand all stay BARE — none of
    /// them can hold <c>Nothing</c> (<c>MayHoldNothing</c>'s three <c>false</c> arms), and
    /// guarding them would be the "always guard" over-reach the churn report explicitly rules
    /// out. This is the churn BOUNDARY: <c>JsBooleanTextTests
    /// .NonBooleanConcat_SkipsBooleanText_GuardedOnlyWhereNothingIsPossible</c> is the sibling
    /// pin for a plain variable operand (which DOES get guarded); this one is for the three
    /// shapes that must NOT be.
    /// </summary>
    [Test]
    public void LiteralConcatResultAndCStrResult_AsConcatOperands_StayBare()
    {
        var js = JsTestSupport.Compile(
            "Function N() As Integer\n    Return 5\nEnd Function\n" +
            "Sub Main()\n" +
            "    Console.WriteLine((\"x\" & \"y\") & N())\n" + // Concat result operand
            "    Console.WriteLine(CStr(N()) & \"z\")\n" +      // CStr result operand
            "    Console.WriteLine(\"lit\" & \"eral\")\n" +      // literal operand (folds to one constant)
            "End Sub\n");
        Assert.Multiple(() =>
        {
            Assert.That(js, Does.Not.Contain("??"), js);
            // No optimizer runs here (JsTestSupport.Compile), so "x" & "y" is NOT folded into one
            // "xy" constant — it stays two literals joined by a bare +, and THAT temp (a Concat
            // result) is what the outer & consumes, unguarded.
            Assert.That(js, Does.Match(@"\(""x"" \+ ""y""\)"), js);
            Assert.That(js, Does.Match(@"\(\w+ \+ \w+\)"),
                "the outer & must consume the inner Concat result BARE, no ?? guard\n" + js);
            Assert.That(js, Does.Match(@"= String\(\w+\);"),
                "the CStr result must still be produced by the OLD, unguarded String(...) call\n" + js);
            Assert.That(js, Does.Match(@"\(\w+ \+ ""z""\)"),
                "and consumed bare — no ?? guard on a value CStr already made a real string\n" + js);
        });
    }

    /// <summary><c>Console.Write</c> of a possibly-<c>Nothing</c> String parameter is guarded —
    /// the same <c>nothingIsEmpty</c> flag as <c>&amp;</c>, now passed at the <c>mustBeString</c>
    /// call site too, and taking priority OVER the old <c>String(...)</c> wrap.</summary>
    [Test]
    public void ConsoleWrite_OfPossiblyNothingString_IsGuarded()
    {
        var js = JsTestSupport.Compile(
            "Sub Show(t As String)\n    Console.Write(t)\n    Console.WriteLine(\"|\")\nEnd Sub\n");
        Assert.Multiple(() =>
        {
            Assert.That(js, Does.Contain("process.stdout.write((t ?? \"\"))"), js);
            Assert.That(js, Does.Not.Contain("String(t)"),
                "the old mustBeString wrap must not survive alongside the new guard\n" + js);
        });
    }

    /// <summary><c>Console.WriteLine</c> of a possibly-<c>Nothing</c> String is guarded — J5's
    /// shape (<c>Console.WriteLine(s)</c> with <c>s</c> Nothing printed the real word
    /// <c>null</c>; VB/C# print an empty line).</summary>
    [Test]
    public void ConsoleWriteLine_OfPossiblyNothingString_IsGuarded()
    {
        var js = JsTestSupport.Compile(
            "Sub Show(t As String)\n    Console.WriteLine(t)\nEnd Sub\n");
        Assert.That(js, Does.Contain("console.log((t ?? \"\"))"), js);
    }

    /// <summary>
    /// CStr and <c>.ToString()</c> are explicitly OUT of #189's contract ("CStr and .ToString()
    /// are unchanged") — their own codegen keeps the OLD, unguarded spelling. This is not a claim
    /// that <c>CStr(Nothing)</c> prints "" on JS (measured: it still prints the literal word
    /// "null", a separate, pre-existing, filed-elsewhere gap) — only that this task's fix did not
    /// touch that call form.
    /// </summary>
    [Test]
    public void CStr_OfPossiblyNothingString_KeepsItsOldUnguardedSpelling()
    {
        var js = JsTestSupport.Compile(
            "Sub Show(t As String)\n    Console.WriteLine(CStr(t))\nEnd Sub\n");
        Assert.Multiple(() =>
        {
            Assert.That(js, Does.Contain("String(t)"), js);
            Assert.That(js, Does.Not.Contain("??"), js);
        });
    }

    /// <summary>
    /// <c>__blStr</c> — the runtime helper an Object-typed <c>&amp;</c>/<c>Write</c> operand goes
    /// through — returns <c>""</c> for <c>null</c>/<c>undefined</c> rather than JS's own
    /// <c>String(null)</c> spelling ("null").
    /// </summary>
    [Test]
    public void BlStrHelper_ReturnsEmptyStringForNull()
    {
        var js = JsTestSupport.Compile(
            "Sub Main()\n    Dim o As Object = Nothing\n    Console.Write(o)\n    Console.WriteLine(\"|\")\nEnd Sub\n");
        Assert.That(js, Does.Contain(
            "return x == null ? \"\" : typeof x === \"boolean\" ? (x ? \"True\" : \"False\") : String(x);"),
            js);
    }

    private static class Regexes
    {
        /// <summary>A <c>(&lt;name&gt; ?? "")</c> guard, where <paramref name="namePattern"/> is
        /// either a literal identifier or a regex fragment matching one.</summary>
        public static string NullishGuard(string namePattern) => $@"\({namePattern} \?\? """"\)";
    }
}

/// <summary>
/// The C# leg: <c>ConcatOperand</c> emits a <c>Nothing</c> constant operand of <c>&amp;</c> as
/// <c>(string)null</c> unless the OTHER operand is already a non-null String — reachable ONLY
/// once the optimizer's <c>CopyPropagationPass</c> has folded a <c>Nothing</c>-initialized String
/// local into the literal <c>null</c> constant <c>ConcatOperand</c> looks for (J4's own shape).
/// </summary>
[TestFixture]
public class CSharpNullConstantCastTests
{
    /// <summary>
    /// WITHOUT the optimizer, <c>s</c> stays a variable (statically typed <c>string</c>), never an
    /// <c>IRConstant</c> — so <c>ConcatOperand</c> has nothing to match, and C# needs no cast at
    /// all: <c>s + 5</c> already resolves to string concatenation because <c>s</c>'s OWN static
    /// type is <c>string</c>. This is the negative control that shows the fix's cast is scoped to
    /// the constant-operand shape, not a blanket rewrite of every Concat.
    /// </summary>
    [Test]
    public void UnpropagatedStringLocal_NeedsNoCast_NonOptimizingHelper()
    {
        var csharp = CompileNonOptimizing(
            "Sub Main()\n    Dim s As String = Nothing\n    Dim t As String = s & 5\n    Console.WriteLine(t)\nEnd Sub\n");
        Assert.Multiple(() =>
        {
            Assert.That(csharp, Does.Contain("t = s + 5;"), csharp);
            Assert.That(csharp, Does.Not.Contain("(string)null"), csharp);
        });
    }

    /// <summary>
    /// BasicLang source straight to C# text, with NO optimizer pass run at all — the C# analog of
    /// <see cref="JsTestSupport.Compile"/>'s "non-optimizing path", rolled locally per this
    /// file's own need rather than promoted to a shared helper (no such shared C# helper exists
    /// in this suite today; every other fixture that wants this rolls its own small copy the same
    /// way, e.g. <c>CastExpressionTests.CompileToCSharp</c>).
    /// </summary>
    private static string CompileNonOptimizing(string source)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty,
            "parse errors:\n" + string.Join("\n", parser.Errors.Select(e => e.Message)));

        var analyzer = new SemanticAnalyzer();
        Assert.That(analyzer.Analyze(ast), Is.True,
            "semantic errors:\n" + string.Join("\n", analyzer.Errors.Select(e => e.ToString())));

        var module = new IRBuilder(analyzer).Build(ast, "NothingStringTextProbe");
        return new ImprovedCSharpCodeGenerator().Generate(module);
    }

    /// <summary>
    /// J4's EXACT shape, through the optimizer-running helper — the CLI-equivalent path.
    /// <c>CopyPropagationPass</c> (a STANDARD pass) folds <c>s</c>'s single assignment
    /// (<c>Dim s As String = Nothing</c>) into its one use, so by the time
    /// <c>CSharpBackend</c> sees <c>s &amp; 5</c> the left operand IS an <c>IRConstant</c> with a
    /// null value. Without <c>ConcatOperand</c>'s cast this is C#'s int?-lifted <c>null + 5</c> —
    /// CS0029, "Cannot implicitly convert type 'int?' to 'string'" (measured, the pre-fix
    /// failure). <c>(string)null</c> forces C#'s string <c>+</c> overload, which reads null as ""
    /// exactly as VB's <c>&amp;</c> does.
    /// </summary>
    [Test]
    public void PropagatedNothingConstant_InConcatWithNonStringOperand_GetsCast()
    {
        var csharp = ReturnCoercionTests.EmitCSharpForTest(
            "Sub Main()\n    Dim s As String = Nothing\n    Dim t As String = s & 5\n    Console.WriteLine(t)\nEnd Sub\n");
        Assert.That(csharp, Does.Contain("t = (string)null + 5;"), csharp);
    }

    /// <summary>Same propagated shape, checked on the AGGRESSIVE pipeline too — the second half
    /// of "check this on the optimizer-running helper" (both pipelines, not just standard).</summary>
    [Test]
    public void PropagatedNothingConstant_InConcatWithNonStringOperand_GetsCast_Aggressive()
    {
        var csharp = ReturnCoercionTests.EmitCSharpAggressiveForTest(
            "Sub Main()\n    Dim s As String = Nothing\n    Dim t As String = s & 5\n    Console.WriteLine(t)\nEnd Sub\n");
        Assert.That(csharp, Does.Contain("t = (string)null + 5;"), csharp);
    }

    /// <summary>
    /// When the OTHER operand is already a non-null String, the cast is left off — C# picks
    /// string <c>+</c> anyway once one operand is a string literal, and the fix's own contract
    /// ("choose the spelling that changes the fewest existing .cs outputs") depends on this: the
    /// implementer measured 0 <c>.cs</c> files changed across the whole corpus.
    /// </summary>
    [Test]
    public void PropagatedNothingConstant_WithNonNullStringOperand_NeedsNoCast()
    {
        var csharp = ReturnCoercionTests.EmitCSharpForTest(
            "Sub Main()\n    Dim s As String = Nothing\n    Dim other As String = \"z\"\n" +
            "    Console.WriteLine(s & other)\nEnd Sub\n");
        Assert.Multiple(() =>
        {
            Assert.That(csharp, Does.Not.Contain("(string)null"), csharp);
            Assert.That(csharp, Does.Contain("null + \"z\""), csharp);
        });
    }
}

/// <summary>
/// C++ codegen text: a lambda written inside a <c>Catch</c> clause that reads its caught
/// exception's <c>Message</c> renders the SAME <c>.what()</c> spelling the Catch clause's own
/// body uses — never <c>ex-&gt;Message</c>, a shared_ptr field access against an exception
/// representation C++ holds by VALUE. Text-only (no C++ compiler invoked) — behaviour is
/// <c>NothingStringTextExecutionTests</c>.
/// </summary>
[TestFixture]
public class CppCatchLambdaTextTests
{
    private const string LambdaAfterTry = """
        Sub Main()
            Dim f As Action = Nothing
            Try
                Throw New Exception("boom")
            Catch ex As Exception
                f = Sub() Console.WriteLine(ex.Message)
            End Try
            f()
        End Sub
        """;

    [Test]
    public void CapturedCatchVariable_MessageRead_UsesWhatSpelling_NeverArrowMessage()
    {
        var cpp = BclE2E.CompileToCppOptimized(LambdaAfterTry);
        Assert.Multiple(() =>
        {
            Assert.That(cpp, Does.Contain("BasicLang::String(ex.what())"), cpp);
            Assert.That(cpp, Does.Not.Contain("ex->Message"), cpp);
        });
    }

    /// <summary>The lambda's capture list init-captures the caught exception's MESSAGE into a
    /// fresh <c>std::runtime_error</c> — never a plain <c>[=]</c> copy, which would copy the
    /// binding's STATIC type and slice a by-value <c>const std::exception&amp;</c>.</summary>
    [Test]
    public void LambdaCaptureList_InitCapturesCatchVariable_AsRuntimeError()
    {
        var cpp = BclE2E.CompileToCppOptimized(LambdaAfterTry);
        Assert.That(cpp, Does.Contain("[=, ex = std::runtime_error(ex.what())]"), cpp);
    }

    /// <summary>
    /// A <c>Try</c> written INSIDE a lambda that itself lives in a <c>Catch</c> clause gets its
    /// OWN region labels — no leftover <c>_nex</c> suffix from the enclosing catch ladder's own
    /// label scheme. MEASURED before the fix (the implementer's own doc comment,
    /// <c>GenerateLambdaExpression</c>): the inner <c>Try</c>'s end label was DECLARED
    /// <c>try1_end_nex:</c> while the <c>goto</c> that reaches it targeted the un-suffixed
    /// <c>try1_end</c> — "use of undeclared label", a compile failure with no diagnostic pointing
    /// at the real cause.
    /// </summary>
    [Test]
    public void NestedTryInsideCatchLambda_GetsItsOwnUnsuffixedRegionLabels()
    {
        const string nestedTryInLambda = """
            Sub Main()
                Dim f As Action = Nothing
                Try
                    Throw New Exception("outer")
                Catch ex As Exception
                    f = Sub()
                            Try
                                Throw New Exception("inner")
                            Catch ex2 As Exception
                                Console.WriteLine(ex.Message & "/" & ex2.Message)
                            End Try
                            Console.WriteLine("after:" & ex.Message)
                        End Sub
                End Try
                f()
            End Sub
            """;
        var cpp = BclE2E.CompileToCppOptimized(nestedTryInLambda);
        Assert.Multiple(() =>
        {
            Assert.That(cpp, Does.Contain("try1_end:"),
                "the lambda's own inner Try must get a plain, unsuffixed region label.\n" + cpp);
            Assert.That(cpp, Does.Not.Match(@"try\d+_end_nex\b"),
                "a leftover _nex suffix means the lambda inherited the enclosing catch ladder's " +
                "label scheme (the region-reset mutant) — this is #189's own measured defect " +
                "shape, 'use of undeclared label'.\n" + cpp);
        });
    }
}
