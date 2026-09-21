using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.SemanticAnalysis;
using VisualGameStudio.Tests.Msil;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// The VB string intrinsics (<c>Mid</c>, <c>Left</c>, <c>Right</c>, <c>UCase</c>, <c>LCase</c>,
/// <c>Trim</c>, <c>Replace</c>, <c>InStr</c>, <c>Chr</c>, <c>Asc</c>) on MSIL.
///
/// <para>⛔ <b>What was wrong.</b> Every one of these emitted a PHANTOM SELF-CALL —
/// e.g. <c>call string 'MsilProbe'::'UCase'(string)</c>, a method the module class never
/// declares. <c>ilasm</c> accepts a <c>MemberRef</c> with no matching definition — it does not
/// resolve member references — so the program ASSEMBLED and died at run time with
/// <c>MissingMethodException</c>. <c>Len</c> alone worked (see
/// <see cref="Len_IsTheControl_ThatAlreadyWorked"/>), which is what showed the mechanism (a real
/// table-driven arm in <c>MSILCodeGenerator.TryEmitStdLibCall</c>) already existed and only the
/// table was nine rows short.</para>
///
/// <para><b>⛔ THE CONTRACT IS "MATCH THE C# BACKEND", NOT "MATCH VB".</b>
/// <c>CSharpStdLibProvider.EmitMid</c>/<c>EmitLeft</c>/<c>EmitRight</c>/… (<c>BasicLang/StdLib/
/// CSharpStdLib.cs:352-364</c>) is the specification, and MSIL now emits the IL that C#'s OUTPUT
/// compiles to, instruction for instruction — including on the inputs that throw. Real VB's
/// <c>Mid</c>/<c>Left</c>/<c>Right</c> CLAMP; this front end's C# backend does not, and
/// <c>BasicLang.Runtime.BasicLangRuntime.Mid</c> — which DOES clamp — is dead code no backend
/// calls. ⛔ <b>Do not assert VB clamping semantics anywhere below</b> — it would pin an answer
/// nothing gives.</para>
///
/// <para>⭐ There are now NO deviations from "match C# byte for byte". There used to be exactly one:
/// <see cref="RightWithAnEffectfulReceiver_IsEvaluatedOnce"/>, where the C# backend evaluated
/// <c>Right</c>'s receiver twice and MSIL evaluated it once. That C#-backend defect has been fixed,
/// and every case in this fixture now asserts MSIL against C#.</para>
///
/// <para>⛔ <b><c>Chr</c> and <c>Asc</c> are UNREGISTERED in the front end</b> —
/// <c>SemanticAnalyzer.RegisterStdLibFunctions</c> has no row for either — so, unlike every other
/// intrinsic here, their arguments were never type-checked by anything upstream. Before this fix
/// a mistyped argument did not fail: it ran and printed garbage (measured on the parent commit:
/// <c>Chr("x")</c> printed <c>Ԙ</c>, <c>Chr(Asc("A"))</c> printed <c>鍀</c>, <c>Asc(5)</c> died
/// with <c>NullReferenceException</c>). The fix refuses all three at compile time — see the
/// "UNREGISTERED INTRINSICS" section.</para>
///
/// <para>⛔ <b>C++ is not a usable oracle for this family, outside <c>Replace</c>.</b>
/// <c>CppCodeGenerator</c>'s <c>left</c>/<c>right</c>/<c>mid</c>/<c>instr</c> arms call
/// <c>.substr</c>/<c>.find</c> directly on the argument, so a STRING-LITERAL receiver is a bare
/// <c>const char*</c> and the program does not compile — recorded, not this family's, and not
/// used as an oracle anywhere below. (Also unrelated: <c>Replace("banana", "", "o")</c> hangs and
/// is killed, exit 137, on C++.)</para>
///
/// <para>⚠ Kept to ONE shape per test. Both <c>MsilHarness.RunExpectingSuccess</c> and the C# leg
/// (<c>FourBackends.RunEmittedCSharp</c>) use <c>Assert.Multiple</c> internally — grouping several
/// shapes in one test mis-attributes one shape's failure onto another.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class MsilStringIntrinsicTests
{
    /// <summary>Both .NET backends compile AND RUN the program, and agree with each other.</summary>
    private static void MsilAgreesWithCSharp(string program, string expected)
    {
        var cs = FourBackends.Norm(FourBackends.RunEmittedCSharp(program));
        var msil = FourBackends.Norm(MsilHarness.RunExpectingSuccess(program));
        Assert.Multiple(() =>
        {
            Assert.That(cs, Is.EqualTo(expected), "C# (the reference .NET backend)");
            Assert.That(msil, Is.EqualTo(expected), "MSIL");
        });
    }

    /// <summary>
    /// Pins that BOTH .NET backends throw for an out-of-range shape, and that they throw the SAME
    /// exception TYPE. Never the message: it is BCL text that can move between runtimes — see the
    /// "OUT-OF-RANGE SHAPES THROW ON PURPOSE" section.
    /// </summary>
    private static void BothThrow(string program, string exceptionTypeName)
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => FourBackends.RunEmittedCSharp(program),
                Throws.InstanceOf<AssertionException>().With.Message.Contains(exceptionTypeName),
                "C# (the reference .NET backend) must throw " + exceptionTypeName + " too");

            var msil = MsilHarness.Run(program);
            Assert.That(msil.Outcome, Is.EqualTo(MsilHarness.MsilOutcome.RunFailed), msil.Report);
            Assert.That(msil.Output, Does.Contain(exceptionTypeName), msil.Report);
        });
    }

    /// <summary>MSIL refuses to generate the program at all — a named diagnostic, not a crash.</summary>
    private static void Refused(string program)
    {
        var r = MsilHarness.Run(program);
        Assert.That(r.Outcome, Is.EqualTo(MsilHarness.MsilOutcome.GenerateFailed), r.Report);
    }

    private static string Wrap(string expr) =>
        "Sub Main()\n    PrintLine(\"[\" & " + expr + " & \"]\")\nEnd Sub";

    private static string Num(string expr) =>
        "Sub Main()\n    PrintLine(CStr(" + expr + "))\nEnd Sub";

    // ============================================================================================
    // RISKY EDGE — Right evaluates its receiver ONCE; C# evaluates it TWICE.
    // ============================================================================================

    /// <summary>
    /// ⭐ <c>EmitRight</c> (C# backend) used to interpolate the receiver expression <c>{str}</c>
    /// TWICE — once for <c>str.Length</c>, once for the <c>Substring</c> call. MSIL uses <c>dup</c>
    /// and evaluates it once. Measured with an EFFECTFUL receiver (a function that prints): at
    /// f20435d C# printed <c>tag</c> TWICE before <c>[ef]</c> where JavaScript, C++ and MSIL all
    /// printed it once.
    ///
    /// <para>⭐ That C#-backend defect is fixed — <c>EmitRight</c> now emits
    /// <c>({str})[^({length})..]</c>, one evaluation — and C# prints <c>tag\n[ef]</c> like everyone
    /// else (re-measured). So this is no longer a deliberate deviation and no longer needs the
    /// JavaScript oracle: it holds both .NET backends to the same answer. The C#-side contract for
    /// <c>Right</c>'s receiver and length lives in <see cref="CSharpRightReceiverTests"/>.</para>
    /// </summary>
    [Test]
    public void RightWithAnEffectfulReceiver_IsEvaluatedOnce()
        => MsilAgreesWithCSharp(@"
Function Tag() As String
    PrintLine(""tag"")
    Return ""abcdef""
End Function

Sub Main()
    PrintLine(""["" & Right(Tag(), 2) & ""]"")
End Sub", "tag\n[ef]");

    /// <summary>
    /// ⚠ <c>n = 2</c>, deliberately NOT <c>3</c>: with a 6-character string, a correct
    /// <c>Right(s, 3)</c> and a mutant that forgets the <c>str.Length - n</c> subtraction both
    /// answer <c>def</c> — the shape cannot tell them apart. <c>n = 2</c> (answer <c>ef</c>) is
    /// the one that can.
    /// </summary>
    [Test]
    public void Right_TakesTheLastNCharacters()
        => MsilAgreesWithCSharp(Wrap("Right(\"abcdef\", 2)"), "[ef]");

    // ============================================================================================
    // RISKY EDGE — the out-of-range shapes throw ON PURPOSE. This is contract, not accident: the
    // C# backend's Substring/Replace/Chars calls are raw BCL with no clamping, and MSIL now
    // matches. Only the exception TYPE is pinned; BCL messages move between runtimes.
    // ============================================================================================

    [Test]
    public void Mid_LengthRunsPastTheEndOfTheString_Throws()
        => BothThrow(Wrap("Mid(\"abcdef\", 5, 10)"), "ArgumentOutOfRangeException");

    [Test]
    public void Mid_StartIndexPastTheEndOfTheString_Throws()
        => BothThrow(Wrap("Mid(\"abc\", 5, 2)"), "ArgumentOutOfRangeException");

    [Test]
    public void Mid_OnAnEmptyString_Throws()
        => BothThrow(Wrap("Mid(\"\", 1, 1)"), "ArgumentOutOfRangeException");

    /// <summary>VB's 1-based <c>Mid</c> start of 0 becomes Substring's <c>startIndex = -1</c>.</summary>
    [Test]
    public void Mid_WithAZeroStart_Throws()
        => BothThrow(Wrap("Mid(\"abc\", 0, 2)"), "ArgumentOutOfRangeException");

    [Test]
    public void Left_LengthLongerThanTheString_Throws()
        => BothThrow(Wrap("Left(\"abcdef\", 10)"), "ArgumentOutOfRangeException");

    [Test]
    public void Left_OnAnEmptyString_Throws()
        => BothThrow(Wrap("Left(\"\", 1)"), "ArgumentOutOfRangeException");

    [Test]
    public void Left_WithANegativeLength_Throws()
        => BothThrow(Wrap("Left(\"abc\", -1)"), "ArgumentOutOfRangeException");

    [Test]
    public void Right_LengthLongerThanTheString_Throws()
        => BothThrow(Wrap("Right(\"abcdef\", 10)"), "ArgumentOutOfRangeException");

    [Test]
    public void Right_OnAnEmptyString_Throws()
        => BothThrow(Wrap("Right(\"\", 1)"), "ArgumentOutOfRangeException");

    [Test]
    public void Replace_WithAnEmptyNeedle_Throws()
        => BothThrow(Wrap("Replace(\"banana\", \"\", \"o\")"), "ArgumentException");

    [Test]
    public void Asc_OfAnEmptyString_Throws()
        => BothThrow(Num("Asc(\"\")"), "IndexOutOfRangeException");

    /// <summary>
    /// <c>Mid</c> is registered with exactly THREE parameters; the 2-argument spelling never
    /// reaches any emitter on any backend.
    ///
    /// <para>⚠ Checked directly against <c>SemanticAnalyzer</c>, NOT through
    /// <see cref="MsilHarness.Run"/>/<see cref="MsilHarness.CompileToIl"/>: those assert
    /// <c>analyzer.Analyze(ast)</c> is TRUE internally (they exist to compile a program that is
    /// supposed to succeed), so a deliberately semantically-INVALID program fails NUnit's own
    /// assertion tracking for this test even though <c>MsilHarness.Run</c>'s try/catch converts
    /// it to <c>GenerateFailed</c> — NUnit records the failed <c>Assert.That</c> against the
    /// current test regardless of whether the exception it throws is later caught. Analysing
    /// directly and asserting <c>Is.False</c> is the correct shape for a program this front-end
    /// rejects before any backend runs.</para>
    /// </summary>
    [Test]
    public void Mid_TheTwoArgumentForm_IsRefusedByTheFrontEnd()
    {
        const string source = "Sub Main()\n    PrintLine(\"[\" & Mid(\"abcdef\", 3) & \"]\")\nEnd Sub";
        var ast = new Parser(new Lexer(source).Tokenize()).Parse();
        var analyzer = new SemanticAnalyzer();

        Assert.That(analyzer.Analyze(ast), Is.False,
            "Mid takes exactly 3 arguments; the 2-argument form must be a semantic error");
        Assert.That(string.Join(" | ", analyzer.Errors.Select(e => e.Message)),
            Does.Contain("Mid").And.Contain("3 argument"));
    }

    // ============================================================================================
    // RISKY EDGE — Chr / Asc are UNREGISTERED in the front end, so nothing upstream type-checks
    // their arguments. A mistyped argument must be REFUSED, not silently miscompiled.
    // ============================================================================================

    /// <summary>⛔ Pre-fix, measured: ran clean and printed a Cyrillic glyph (Ԙ).</summary>
    [Test]
    public void ChrOfAString_IsRefused()
        => Refused(Wrap("Chr(\"x\")"));

    /// <summary>
    /// ⛔ <c>Asc</c>'s result is typed Object (also unregistered), so this reaches <c>Chr</c> as a
    /// BOXED int rather than a literal string — the same defect through composition rather than a
    /// literal. Pre-fix, measured: ran clean and printed a CJK glyph (鍀).
    /// </summary>
    [Test]
    public void ChrOfAnObject_IsRefused()
        => Refused(Wrap("Chr(Asc(\"A\"))"));

    /// <summary>⛔ Pre-fix, measured: assembled and died with NullReferenceException — an
    /// integer used as a string reference at <c>String::get_Chars</c>.</summary>
    [Test]
    public void AscOfANumber_IsRefused()
        => Refused(Num("Asc(5)"));

    /// <summary>
    /// ⚠ The OTHER direction must still work: <c>Asc</c> accepts an Object-typed argument, because
    /// that is how the IR types <c>Chr</c>'s result, and the <c>callvirt</c> dispatches correctly
    /// when the object really is a string. This is the composition the guard must not break.
    /// </summary>
    [Test]
    public void AscOfChr_StillWorks()
        => MsilAgreesWithCSharp(Num("Asc(Chr(66))"), "66");

    // ============================================================================================
    // RISKY EDGE — Chr's conv.u2. A NON-INTEGER argument is the only thing that can observe it:
    // on the CLR evaluation stack `char` IS `int32`, so every INTEGER argument assembles and runs
    // identically with or without the conversion (see SURVIVORS.md's g1-chr-no-conv, which
    // survived a sweep of 27 integer-only shapes for exactly this reason).
    // ============================================================================================

    /// <summary>⭐ Without <c>conv.u2</c> this is <c>InvalidProgramException</c> — the ONE mutant
    /// that survived every integer-argument shape in the probe sweep.</summary>
    [Test]
    public void ChrOfADouble_NeedsTheNarrowingConversion()
        => MsilAgreesWithCSharp(@"
Sub Main()
    Dim d As Double = 65.0
    PrintLine(""["" & Chr(d) & ""]"")
End Sub", "[A]");

    [Test]
    public void ChrOfALong_NeedsTheNarrowingConversion()
        => MsilAgreesWithCSharp(@"
Sub Main()
    Dim n As Long = 65
    PrintLine(""["" & Chr(n) & ""]"")
End Sub", "[A]");

    // ============================================================================================
    // RISKY EDGE — Asc sets the Object/int32 boxing bridge (_stdLibResultSpec); the NEXT
    // value-returning intrinsic in the same method must not inherit it. Order matters: Asc first,
    // then a string-returning arm.
    // ============================================================================================

    [Test]
    public void AscThenAStringIntrinsic_TheBoxingBridgeDoesNotLeak()
        => MsilAgreesWithCSharp(@"
Sub Main()
    PrintLine(CStr(Asc(""A"")))
    PrintLine(""["" & UCase(""bc"") & ""]"")
End Sub", "65\n[BC]");

    // ============================================================================================
    // RISKY EDGE — InStr's single `+ 1` carries TWO contract clauses (1-based indexing, and
    // not-found-is-0-not-negative-1). One shape alone would leave half the site untested.
    // ============================================================================================

    /// <summary>1-based: 'l' is the 3rd character, not the 2nd a 0-based IndexOf would answer.</summary>
    [Test]
    public void InStr_IsOneBased()
        => MsilAgreesWithCSharp(Num("InStr(\"hello\", \"l\")"), "3");

    [Test]
    public void InStr_MatchAtTheStart_IsPositionOne()
        => MsilAgreesWithCSharp(Num("InStr(\"hello\", \"h\")"), "1");

    /// <summary>Not found is 0, not .NET's -1.</summary>
    [Test]
    public void InStr_NotFound_IsZero()
        => MsilAgreesWithCSharp(Num("InStr(\"hello\", \"z\")"), "0");

    [Test]
    public void InStr_WithAnEmptyNeedle_IsPositionOne()
        => MsilAgreesWithCSharp(Num("InStr(\"hello\", \"\")"), "1");

    [Test]
    public void InStr_OnAnEmptyReceiver_IsZero()
        => MsilAgreesWithCSharp(Num("InStr(\"\", \"a\")"), "0");

    // ============================================================================================
    // CONTRACT — every intrinsic, in range. C# and MSIL must agree byte for byte.
    // ============================================================================================

    [Test]
    public void Mid_TakesTheMiddleSubstring()
        => MsilAgreesWithCSharp(Wrap("Mid(\"abcdef\", 2, 3)"), "[bcd]");

    [Test]
    public void Mid_FromTheFirstCharacter()
        => MsilAgreesWithCSharp(Wrap("Mid(\"abcdef\", 1, 2)"), "[ab]");

    [Test]
    public void Mid_ThroughTheLastCharacter()
        => MsilAgreesWithCSharp(Wrap("Mid(\"abcdef\", 6, 1)"), "[f]");

    [Test]
    public void Left_TakesTheFirstNCharacters()
        => MsilAgreesWithCSharp(Wrap("Left(\"abcdef\", 3)"), "[abc]");

    [Test]
    public void Left_WithZeroLength_IsEmpty()
        => MsilAgreesWithCSharp(Wrap("Left(\"abcdef\", 0)"), "[]");

    [Test]
    public void Left_WithTheWholeLength_IsTheWholeString()
        => MsilAgreesWithCSharp(Wrap("Left(\"abcdef\", 6)"), "[abcdef]");

    [Test]
    public void Right_WithZeroLength_IsEmpty()
        => MsilAgreesWithCSharp(Wrap("Right(\"abcdef\", 0)"), "[]");

    [Test]
    public void Right_WithTheWholeLength_IsTheWholeString()
        => MsilAgreesWithCSharp(Wrap("Right(\"abcdef\", 6)"), "[abcdef]");

    [Test]
    public void UCase_UppercasesEveryLetter()
        => MsilAgreesWithCSharp(Wrap("UCase(\"aBc\")"), "[ABC]");

    [Test]
    public void UCase_OfAnEmptyString_IsEmpty()
        => MsilAgreesWithCSharp(Wrap("UCase(\"\")"), "[]");

    [Test]
    public void LCase_LowercasesEveryLetter()
        => MsilAgreesWithCSharp(Wrap("LCase(\"AbC\")"), "[abc]");

    [Test]
    public void LCase_OfAnEmptyString_IsEmpty()
        => MsilAgreesWithCSharp(Wrap("LCase(\"\")"), "[]");

    [Test]
    public void Trim_RemovesLeadingAndTrailingWhitespace()
        => MsilAgreesWithCSharp(Wrap("Trim(\"  hi  \")"), "[hi]");

    [Test]
    public void Trim_OfAnEmptyString_IsEmpty()
        => MsilAgreesWithCSharp(Wrap("Trim(\"\")"), "[]");

    [Test]
    public void Trim_WithNoWhitespace_IsUnchanged()
        => MsilAgreesWithCSharp(Wrap("Trim(\"hi\")"), "[hi]");

    /// <summary>EVERY occurrence, not only the first — "banana"/"a"/"o" -> "bonono".</summary>
    [Test]
    public void Replace_ReplacesEveryOccurrence()
        => MsilAgreesWithCSharp(Wrap("Replace(\"banana\", \"a\", \"o\")"), "[bonono]");

    [Test]
    public void Replace_WithNoMatch_LeavesTheStringUnchanged()
        => MsilAgreesWithCSharp(Wrap("Replace(\"banana\", \"z\", \"o\")"), "[banana]");

    [Test]
    public void Replace_OnAnEmptyReceiver_IsEmpty()
        => MsilAgreesWithCSharp(Wrap("Replace(\"\", \"a\", \"o\")"), "[]");

    [Test]
    public void Chr_OfAnUppercaseLetterCode()
        => MsilAgreesWithCSharp(Wrap("Chr(65)"), "[A]");

    [Test]
    public void Chr_OfALowercaseLetterCode()
        => MsilAgreesWithCSharp(Wrap("Chr(97)"), "[a]");

    /// <summary>
    /// ⚠ Above 65535, so the 16-bit narrowing wraps (65601 mod 65536 = 65 = 'A'). This does NOT
    /// discriminate the missing-<c>conv.u2</c> mutant — on the CLR stack an int32 argument
    /// assembles and runs identically either way (see SURVIVORS.md) — it is here purely as a
    /// correctness pin for a large-integer argument.
    ///
    /// <para>⚠ Routed through a LOCAL, not a literal <c>Chr(65601)</c>: C# emits a literal
    /// <c>(char)65601</c>, and the C# COMPILER rejects that as a constant-expression overflow
    /// (<c>CS0221</c>) — a Roslyn compile-time check on constants, unrelated to this family. A
    /// variable makes the same narrowing a RUN-TIME cast, which C# allows without
    /// <c>unchecked</c>.</para>
    /// </summary>
    [Test]
    public void Chr_WithACodeAboveSixtyFiveThousandFiveThirtyFive_Wraps()
        => MsilAgreesWithCSharp(@"
Sub Main()
    Dim code As Integer = 65601
    PrintLine(""["" & Chr(code) & ""]"")
End Sub", "[A]");

    [Test]
    public void Asc_OfAnUppercaseLetter()
        => MsilAgreesWithCSharp(Num("Asc(\"A\")"), "65");

    /// <summary>Only the FIRST character's code, not the whole string's.</summary>
    [Test]
    public void Asc_TakesOnlyTheFirstCharacterOfAMultiCharacterString()
        => MsilAgreesWithCSharp(Num("Asc(\"abc\")"), "97");

    /// <summary>The control: the one arm that already worked before this fix.</summary>
    [Test]
    public void Len_IsTheControl_ThatAlreadyWorked()
        => MsilAgreesWithCSharp(Num("Len(\"abcdef\")"), "6");

    // ============================================================================================
    // COMPOSITION — an intrinsic's result feeding another intrinsic, an intrinsic as an argument,
    // and results reached through declared locals rather than printed inline.
    // ============================================================================================

    [Test]
    public void MidThroughLocalVariables()
        => MsilAgreesWithCSharp(@"
Sub Main()
    Dim s As String = ""abcdef""
    Dim n As Integer = 2
    PrintLine(""["" & Mid(s, n, 3) & ""]"")
End Sub", "[bcd]");

    [Test]
    public void UCaseOfMid_NestedIntrinsics()
        => MsilAgreesWithCSharp(Wrap("UCase(Mid(\"abcdef\", 2, 3))"), "[BCD]");

    [Test]
    public void LenAsAnArgumentToLeft_IntrinsicFeedingAnotherIntrinsic()
        => MsilAgreesWithCSharp(Wrap("Left(\"abcdef\", Len(\"ab\"))"), "[ab]");

    [Test]
    public void ResultsStoredInDeclaredLocals_ThenReadBack()
        => MsilAgreesWithCSharp(@"
Sub Main()
    Dim u As String = UCase(""abc"")
    Dim k As Integer = InStr(""hello"", ""l"")
    PrintLine(""["" & u & ""]"")
    PrintLine(CStr(k))
End Sub", "[ABC]\n3");
}
