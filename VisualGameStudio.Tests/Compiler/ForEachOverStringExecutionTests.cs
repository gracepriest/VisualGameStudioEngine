using BasicLang.Compiler.CodeGen;
using NUnit.Framework;
using VisualGameStudio.Tests.Msil;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task #171 — execution: a <c>For Each</c> over a <c>String</c> RUNS, and prints what VB's rule
/// (String enumerates as Char) says it must, on every backend that can run it at all.
///
/// <para><b>Why not <see cref="FourBackends.RunsOnEveryBackend"/> everywhere.</b> That helper
/// bundles all FOUR backends into one <c>Assert.Multiple</c> against one expected string — right
/// for a shape all four agree on, wrong for one where JavaScript's refusal is the DESIGNED
/// answer (a <c>Char</c> local or a <c>Char</c> literal, BL7004 — JavaScript has no character
/// type). <see cref="RunOnCSharpCppMsil"/> is the same three-of-four combination with JavaScript
/// left out, for exactly those shapes.</para>
///
/// <para>Expected strings below are transcribed from the probes' own <c>.exp</c> files
/// (<c>S/t171/probes/*.exp</c>, <c>S/t171/edge/*.exp</c>), themselves measured on C#/JS — never
/// copied from MSIL, which is the backend two of this family's own defects lived in.</para>
///
/// <para>⚠ <c>[NonParallelizable]</c>: the C# leg (<see cref="FourBackends.RunEmittedCSharp"/>)
/// redirects <c>Console.Out</c>, matching every other fixture that calls it.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class ForEachOverStringExecutionTests
{
    /// <summary>C#, C++ (through the standard IR optimizer — the CLI-equivalent path) and MSIL
    /// all print <paramref name="expected"/>. No JavaScript leg.</summary>
    private static void RunOnCSharpCppMsil(string program, string expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program))),
                Is.EqualTo(expected), "C++");
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(program)),
                Is.EqualTo(expected), "C#");
            Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(program)),
                Is.EqualTo(expected), "MSIL");
        });
    }

    /// <summary>C# and C++ only — for S4, where MSIL's allowed exception is the surface refusal
    /// asserted separately in <see cref="S4_Msil_RefusesCharIsUpper_AsALoudForeignFeatureException"/>.</summary>
    private static void RunOnCSharpCpp(string program, string expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program))),
                Is.EqualTo(expected), "C++");
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(program)),
                Is.EqualTo(expected), "C#");
        });
    }

    /// <summary>C# and MSIL only — for C1, whose <c>Dim o As Object = c</c> the C++ backend
    /// refuses outright (<c>CppCapabilityException: 'Object' has no C++ mapping</c>), a wide,
    /// pre-existing gap (<c>docs/superpowers/specs/2026-07-07-cpp-backend-preexisting-gaps.md</c>)
    /// this task does not touch and C1 is not the test to re-open.</summary>
    private static void RunOnCSharpAndMsil(string program, string expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(program)),
                Is.EqualTo(expected), "C#");
            Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(program)),
                Is.EqualTo(expected), "MSIL");
        });
    }

    // ========================================================================================
    // S1-S7 (the implementer's contract probes) — S6 (AscW) is task #181's separate gap and is
    // deliberately not covered here.
    // ========================================================================================

    /// <summary>S1 — the #168 reuse form over a literal, plus the post-loop read. Also the
    /// killing test for (c1): the C++ std::string wrap removed makes a LITERAL collection range-
    /// for walk its NUL terminator, printing an extra character.</summary>
    [Test]
    public void S1_ReuseFormOverALiteral()
        => RunOnCSharpCppMsil(
            "Sub Main()\n" +
            " Dim c As Char\n" +
            " For Each c In \"xyz\"\n" +
            "  Console.Write(c)\n" +
            " Next\n" +
            " Console.WriteLine()\n" +
            " Console.WriteLine(c)\n" +
            "End Sub",
            "xyz\nz");

    /// <summary>S2 — the plain bare form, on all four backends (JavaScript keeps running: only
    /// a Char LOCAL or LITERAL is refused, and this shape has neither).</summary>
    [Test]
    public void S2_PlainPrint_OnEveryBackend()
        => FourBackends.RunsOnEveryBackend(
            "Sub Main()\n" +
            " For Each ch In \"abc\"\n" +
            "  Console.WriteLine(ch)\n" +
            " Next\n" +
            "End Sub",
            "a\nb\nc");

    /// <summary>S3 — an explicit `As Char` over a String VARIABLE, on all four backends.</summary>
    [Test]
    public void S3_ExplicitCharOverAVariable_OnEveryBackend()
        => FourBackends.RunsOnEveryBackend(
            "Sub Main()\n" +
            " Dim s As String = \"hey\"\n" +
            " For Each ch As Char In s\n" +
            "  Console.Write(ch)\n" +
            "  Console.Write(\"-\")\n" +
            " Next\n" +
            " Console.WriteLine()\n" +
            "End Sub",
            "h-e-y-");

    /// <summary>
    /// S4 — the element assigned into a second Char local, then <c>Char.IsUpper</c>. C# and C++
    /// only: MSIL's allowed outcome is a refusal, asserted separately below. Also the killing
    /// test for (c1) alongside S1 (the C++ leg prints a bogus third value without the wrap).
    /// </summary>
    [Test]
    public void S4_CharIsUpper_OnCSharpAndCpp()
        => RunOnCSharpCpp(
            "Sub Main()\n" +
            " For Each ch In \"aB\"\n" +
            "  Dim d As Char = ch\n" +
            "  Console.WriteLine(Char.IsUpper(d))\n" +
            " Next\n" +
            "End Sub",
            "False\nTrue");

    /// <summary>
    /// S4 on MSIL: <c>Char.IsUpper</c> is outside <c>MSILCodeGenerator.NetStaticMembers</c>'
    /// recorded surface. The contract (implementer brief) requires this to stay a LOUD refusal —
    /// never a wrong answer — so this asserts the actual exception type, not merely that
    /// <c>MsilHarness.Run</c> reports failure (which would also be true of an unrelated crash).
    /// </summary>
    [Test]
    public void S4_Msil_RefusesCharIsUpper_AsALoudForeignFeatureException()
    {
        const string program =
            "Sub Main()\n" +
            " For Each ch In \"aB\"\n" +
            "  Dim d As Char = ch\n" +
            "  Console.WriteLine(Char.IsUpper(d))\n" +
            " Next\n" +
            "End Sub";

        var ex = Assert.Throws<ForeignFeatureException>(() => MsilHarness.CompileToIl(program));
        Assert.That(ex!.Message, Does.Contain("Char.IsUpper"));
        Assert.That(ex.Message, Does.Contain("outside the supported .NET static surface"));
    }

    /// <summary>S5 — `ch = "a"c` inside a Function, counting matches. C#, C++ and MSIL: before
    /// this fix C# was CS0019 (object == char), C++ did not compile, and MSIL compared
    /// references and printed 0. JavaScript keeps its BL7004 literal refusal (pinned below).</summary>
    [Test]
    public void S5_CharComparison_OnCSharpCppAndMsil()
        => RunOnCSharpCppMsil(
            "Function CountA(s As String) As Integer\n" +
            " Dim n As Integer = 0\n" +
            " For Each ch In s\n" +
            "  If ch = \"a\"c Then n = n + 1\n" +
            " Next\n" +
            " Return n\n" +
            "End Function\n" +
            "Sub Main()\n" +
            " Console.WriteLine(CountA(\"banana\"))\n" +
            "End Sub",
            "3");

    /// <summary>
    /// The BL7004 refusal text, pinned ONCE for each of its two shapes so a future JavaScript
    /// Char change is visible here rather than only in a probe matrix. S1's shape (a DECLARED Char
    /// local) and S5's shape (a bare Char LITERAL, which reaches the generator with no declared
    /// position at all — <c>JsCapabilityChecker.BannedConstantRejection</c>) are deliberately
    /// different code paths and get different messages.
    /// </summary>
    [Test]
    public void S1AndS5_JavaScript_KeepTheirDesignedBL7004Refusals()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                Reject(
                    "Sub Main()\n" +
                    " Dim c As Char\n" +
                    " For Each c In \"xyz\"\n" +
                    "  Console.Write(c)\n" +
                    " Next\n" +
                    "End Sub"),
                Is.EqualTo(
                    "BL7004: 'Char' cannot be lowered to JavaScript — JavaScript has no character " +
                    "type. Use String. (found as local variable 'c' in Sub 'Main'.)"),
                "S1 (a declared Char local)");

            Assert.That(
                Reject(
                    "Function CountA(s As String) As Integer\n" +
                    " Dim n As Integer = 0\n" +
                    " For Each ch In s\n" +
                    "  If ch = \"a\"c Then n = n + 1\n" +
                    " Next\n" +
                    " Return n\n" +
                    "End Function\n" +
                    "Sub Main()\n" +
                    " Console.WriteLine(CountA(\"banana\"))\n" +
                    "End Sub"),
                Is.EqualTo(
                    "BL7004: the literal 'a' is a Char, which cannot be lowered to JavaScript — " +
                    "JavaScript has no character type. Use String."),
                "S5 (a bare Char literal)");
        });
    }

    private static string Reject(string source)
    {
        var module = JsTestSupport.BuildModule(source);
        var ex = Assert.Throws<ForeignFeatureException>(
            () => new BasicLang.Compiler.CodeGen.JavaScript.JavaScriptCodeGenerator().Generate(module));
        return ex!.Message;
    }

    /// <summary>S7 — a String FUNCTION RESULT concatenated back together, on all four backends.
    /// Before this fix MSIL printed garbage (the Char operand of `&amp;` reaching
    /// <c>String::Concat</c> raw); also the killing test for (b2) alongside the C1 repro below.</summary>
    [Test]
    public void S7_FunctionResultConcatenation_OnEveryBackend()
        => FourBackends.RunsOnEveryBackend(
            "Function Name() As String\n" +
            " Return \"Qz\"\n" +
            "End Function\n" +
            "Sub Main()\n" +
            " Dim acc As String = \"\"\n" +
            " For Each ch In Name()\n" +
            "  acc = acc & ch & \".\"\n" +
            " Next\n" +
            " Console.WriteLine(acc)\n" +
            "End Sub",
            "Q.z.");

    // ========================================================================================
    // EDGE PROBES.
    // ========================================================================================

    /// <summary>E1 — an empty-string literal prints nothing. Also the sharpest killing test for
    /// (c1): without the C++ wrap, the range-for over a literal `""` still walks ONE element (the
    /// NUL terminator) and prints an extra, otherwise-invisible line.</summary>
    [Test]
    public void E1_EmptyStringLiteral_PrintsNothing()
        => FourBackends.RunsOnEveryBackend(
            "Sub Main()\n" +
            " Console.WriteLine(\"[\")\n" +
            " For Each ch In \"\"\n" +
            "  Console.WriteLine(ch)\n" +
            " Next\n" +
            " Console.WriteLine(\"]\")\n" +
            "End Sub",
            "[\n]");

    /// <summary>E2 — nested For Each over two different string literals.</summary>
    [Test]
    public void E2_NestedLoops_OverTwoStrings()
        => FourBackends.RunsOnEveryBackend(
            "Sub Main()\n" +
            " For Each a In \"xy\"\n" +
            "  For Each b In \"12\"\n" +
            "   Console.Write(a)\n" +
            "   Console.Write(b)\n" +
            "   Console.Write(\" \")\n" +
            "  Next\n" +
            " Next\n" +
            " Console.WriteLine()\n" +
            "End Sub",
            "x1 x2 y1 y2");

    /// <summary>E3 — a String FIELD of a class, read both through a method and bare from
    /// Main. C#, C++ and MSIL (no JS leg in this fixture's roster; JS runs this shape fine too,
    /// just not part of the contract this file pins).</summary>
    [Test]
    public void E3_StringField_OfAClass()
        => RunOnCSharpCppMsil(
            "Class Box\n" +
            " Public Text As String\n" +
            " Public Sub New(t As String)\n" +
            "  Text = t\n" +
            " End Sub\n" +
            " Public Function Spell() As String\n" +
            "  Dim acc As String = \"\"\n" +
            "  For Each ch In Text\n" +
            "   acc = acc & ch & \"-\"\n" +
            "  Next\n" +
            "  Return acc\n" +
            " End Function\n" +
            "End Class\n" +
            "Sub Main()\n" +
            " Dim b As New Box(\"abc\")\n" +
            " Console.WriteLine(b.Spell())\n" +
            " Dim acc2 As String = \"\"\n" +
            " For Each ch In b.Text\n" +
            "  acc2 = acc2 & ch\n" +
            " Next\n" +
            " Console.WriteLine(acc2)\n" +
            "End Sub",
            "a-b-c-\nabc");

    /// <summary>E6 — Exit For inside a String loop, on every backend.</summary>
    [Test]
    public void E6_ExitFor_InsideAStringLoop()
        => FourBackends.RunsOnEveryBackend(
            "Sub Main()\n" +
            " Dim k As Integer = 0\n" +
            " For Each ch In \"abcdef\"\n" +
            "  k = k + 1\n" +
            "  If k = 3 Then Exit For\n" +
            "  Console.Write(ch)\n" +
            " Next\n" +
            " Console.WriteLine()\n" +
            " Console.WriteLine(k)\n" +
            "End Sub",
            "ab\n3");

    /// <summary>
    /// E7 — the body REASSIGNS the variable it iterates. String is immutable in BasicLang, so
    /// this proves .NET's snapshot semantics: the loop keeps enumerating the ORIGINAL "abc", not
    /// whatever `s` names by the last iteration. On every backend.
    /// </summary>
    [Test]
    public void E7_ReassigningTheIteratedVariable_KeepsTheOriginalSnapshot()
        => FourBackends.RunsOnEveryBackend(
            "Sub Main()\n" +
            " Dim s As String = \"abc\"\n" +
            " Dim n As Integer = 0\n" +
            " For Each ch In s\n" +
            "  s = s & \"!\"\n" +
            "  n = n + 1\n" +
            " Next\n" +
            " Console.WriteLine(n)\n" +
            " Console.WriteLine(s)\n" +
            "End Sub",
            "3\nabc!!!");

    /// <summary>E11 — an explicit `As Char` literal loop followed by a bare loop over a Const
    /// String, back to back in one Sub. C#, C++ and MSIL (no JS leg in this fixture's roster).</summary>
    [Test]
    public void E11_ExplicitCharLiteral_ThenBareOverAConst()
        => RunOnCSharpCppMsil(
            "Const W As String = \"hi\"\n" +
            "Sub Main()\n" +
            " For Each ch As Char In \"ok\"\n" +
            "  Console.Write(ch)\n" +
            " Next\n" +
            " For Each ch In W\n" +
            "  Console.Write(ch)\n" +
            " Next\n" +
            " Console.WriteLine()\n" +
            "End Sub",
            "okhi");

    /// <summary>
    /// E12 — the body reassigns `s` to a DIFFERENT, unrelated literal (not an extension of the
    /// original, unlike E7) mid-loop. THE killing test for (c2): wrapping only LITERAL
    /// collections leaves a variable's For Each bound to the variable's own (reassignable)
    /// storage, and reassigning it to a different allocation invalidates the iteration —
    /// measured as garbage output. E7's simple `s = s & "!"` growth does NOT kill (c2); only
    /// this shape does.
    /// </summary>
    [Test]
    public void E12_ReassigningToADifferentLiteral_MidLoop_OnEveryBackend()
        => FourBackends.RunsOnEveryBackend(
            "Sub Main()\n" +
            " Dim s As String = \"abcdefghijklmnopqrstuvwxyz\"\n" +
            " Dim acc As String = \"\"\n" +
            " For Each ch In s\n" +
            "  s = \"0123456789012345678901234567890123456789\"\n" +
            "  acc = acc & ch\n" +
            " Next\n" +
            " Console.WriteLine(acc)\n" +
            "End Sub",
            "abcdefghijklmnopqrstuvwxyz");

    // ========================================================================================
    // PRE-EXISTING REPRO — a Char LOCAL, no loop at all. Guards both MSIL arms this fix touches
    // (Console.WriteLine(char) and the `&amp;` Char-to-string conversion) more directly than any
    // For Each shape does, since neither needs a loop to reach. The killing test for (b1) and
    // (b2) together.
    //
    // ⛔ The full repro (S/t171/w/pre/C1.bas) has a THIRD statement — `Dim o As Object = c` then
    // `Console.WriteLine(o)` — deliberately dropped here. MEASURED AT THE TIME: it threw
    // NullReferenceException on MSIL, but NOT because of Char or this fix — the identical shape
    // with `Dim o As Object = 5` (an Integer) threw the SAME exception at the SAME frame
    // (`Console.WriteLine(Object value)`). That was a general MSIL box-to-Object defect for ANY
    // value type, outside task #171's fix and outside this fixture's job — filed and FIXED as
    // task #177 (`MsilObjectBoxingTests`/`MsilObjectBoxingExecutionTests`): a value stored into
    // an Object slot now boxes, so this third statement would print correctly today. Left out of
    // this fixture regardless, since #171's own C1 stays scoped to the Char shapes it is about.
    // ========================================================================================

    [Test]
    public void C1_CharLocal_NoLoop_RunsOnMsil()
        => RunOnCSharpAndMsil(
            "Sub Main()\n" +
            " Dim c As Char = \"x\"c\n" +
            " Console.WriteLine(c)\n" +
            " Dim s As String = \"a\" & c & \".\"\n" +
            " Console.WriteLine(s)\n" +
            "End Sub",
            "x\nax.");
}
