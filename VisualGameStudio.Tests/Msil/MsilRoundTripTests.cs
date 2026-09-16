using System;
using System.Linq;
using NUnit.Framework;
using static VisualGameStudio.Tests.Msil.MsilHarness;

namespace VisualGameStudio.Tests.Msil;

/// <summary>
/// The MSIL backend's capability matrix, established by RUNNING generated programs.
///
/// <para><b>This fixture is new because the backend had none.</b> 2,136 lines of generator
/// shipped with zero tests while JavaScript had three files and C++ had many. The first probe
/// found a <c>Select Case</c> that assembles, runs, and silently answers <c>Else</c> for every
/// input — a class of defect that only a round trip catches, and that a backend with a test
/// suite would not have kept.</para>
///
/// <para><b>Known-broken shapes are pinned too</b>, as <c>_PinnedDivergence</c> tests that
/// assert the CURRENT wrong behaviour. That is not blessing it: it is the difference between a
/// gap someone measured and a gap nobody knows about. Each one carries the root cause and goes
/// RED the moment it starts working, which is the signal to promote it to a real assertion.
/// The repo already uses this convention for the .NET boundary's divergences.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class MsilRoundTripTests
{
    // ====================================================================================
    // What works. These are the floor — a regression here is a real regression.
    // ====================================================================================

    [Test]
    public void ArithmeticAndLocals_Run()
    {
        // PrintLine rather than Console.WriteLine deliberately: it is the ONE output path that
        // works today (TryEmitStdLibCall's "printline" arm), so it is the oracle every other
        // assertion in this fixture is built on. See ConsoleWriteLine_IsAPhantomSelfCall.
        Assert.That(RunExpectingSuccess("""
            Module M
             Sub Main()
              Dim a As Integer = 20
              Dim b As Integer = a + 1
              If b = 21 Then
               PrintLine("TWENTY-ONE")
              End If
             End Sub
            End Module
            """), Is.EqualTo("TWENTY-ONE\n"));
    }

    [Test]
    public void IfElse_TakesTheRightBranch()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Sub Main()
              Dim n As Integer = 7
              If n > 5 Then
               PrintLine("BIG")
              Else
               PrintLine("SMALL")
              End If
             End Sub
            End Module
            """), Is.EqualTo("BIG\n"));
    }

    [Test]
    public void StringConcatenation_Runs()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Sub Main()
              Dim s As String = "a" & "b"
              PrintLine(s)
             End Sub
            End Module
            """), Is.EqualTo("ab\n"));
    }

    /// <summary>
    /// A loop whose RESULT is observable. A loop that merely runs proves nothing — it has to
    /// be able to get the arithmetic wrong and be caught, which means the value has to escape.
    /// </summary>
    [Test]
    public void AWhileLoop_AccumulatesCorrectly()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Sub Main()
              Dim i As Integer = 0
              Dim s As String = ""
              While i < 3
               i = i + 1
               s = s & "X"
              End While
              If i = 3 Then
               PrintLine(s)
              End If
             End Sub
            End Module
            """), Is.EqualTo("XXX\n"));
    }

    [Test]
    public void AUserFunctionsReturnValue_CrossesCorrectly()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Function Twice(v As Integer) As Integer
              Return v + v
             End Function
             Sub Main()
              If Twice(21) = 42 Then
               PrintLine("FORTY-TWO")
              Else
               PrintLine("WRONG")
              End If
             End Sub
            End Module
            """), Is.EqualTo("FORTY-TWO\n"));
    }

    /// <summary>
    /// <b><c>AndAlso</c>/<c>OrElse</c> short-circuit correctly on MSIL</b> — the complete truth
    /// table, asserted by whether the right operand's SIDE EFFECT happened.
    ///
    /// <para><b>Why this fixture pins something that already works.</b> Nothing did. The
    /// behaviour comes from <c>IRBuilder</c> lowering these to real control flow before codegen,
    /// which is invisible from the backend — and <c>MSILTypeMapper</c> carries
    /// <c>AndAlso → "and"</c>, the non-short-circuit instruction, which reads like a live bug
    /// and was briefly documented as one. It is unreachable: poisoning those map values with
    /// unmistakable markers puts zero of them in the emitted IL. Should anyone ever "simplify"
    /// the IRBuilder lowering into a plain binary operator, these go red rather than the
    /// program quietly evaluating things it must not.</para>
    ///
    /// <para>The left operand is computed from a variable on purpose. <c>False AndAlso f()</c>
    /// is constant-folded, so it short-circuits whatever the backend does — a test built on it
    /// passes for a reason that has nothing to do with the property under test.</para>
    /// </summary>
    [TestCase("AndAlso", -1, "END\n", TestName = "ShortCircuit(AndAlso, left FALSE -> right must NOT run)")]
    [TestCase("AndAlso", 1, "RIGHT-RAN\nTAKEN\nEND\n", TestName = "ShortCircuit(AndAlso, left TRUE -> right runs)")]
    [TestCase("OrElse", 1, "TAKEN\nEND\n", TestName = "ShortCircuit(OrElse, left TRUE -> right must NOT run)")]
    [TestCase("OrElse", -1, "RIGHT-RAN\nTAKEN\nEND\n", TestName = "ShortCircuit(OrElse, left FALSE -> right runs)")]
    public void ShortCircuit(string op, int n, string expected)
    {
        var source = $"""
            Module M
             Function Mark() As Boolean
              PrintLine("RIGHT-RAN")
              Return True
             End Function
             Function Val(v As Integer) As Boolean
              Return v > 0
             End Function
             Sub Main()
              Dim n As Integer = {n}
              If Val(n) {op} Mark() Then
               PrintLine("TAKEN")
              End If
              PrintLine("END")
             End Sub
            End Module
            """;

        Assert.That(RunExpectingSuccess(source), Is.EqualTo(expected),
            "a missing RIGHT-RAN where one is expected, or a present one where it is not, means "
            + "the right operand's evaluation stopped following the left's value.");
    }

    // ---- Select Case, fixed 2026-09-15 -------------------------------------------------
    //
    // Was the most dangerous defect in this backend: it assembled, it ran, and EVERY input fell
    // to Case Else. The generator emitted IL's `switch` instruction over IRSwitch.Cases — but
    // the parser routes every case value into IRSwitch.PatternCases, so the table was literally
    // `switch ()` followed by an unconditional `br` to the default. No conditional branch of any
    // kind existed. (IL `switch` is also INDEX-based, so a populated table would still have been
    // wrong for `Case 1, 2, 3`.) It now lowers to an ordered comparison chain, the same shape
    // CppCodeGenerator uses for the same IR.
    //
    // Every test below runs the program, because that is the only oracle that catches this class
    // of defect: the broken generator produced IL that assembled clean and exited zero.

    [Test]
    public void SelectCase_TakesTheMatchingArm()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Sub Main()
              Dim n As Integer = 2
              Select Case n
               Case 1
                PrintLine("ONE")
               Case 2
                PrintLine("TWO")
               Case Else
                PrintLine("OTHER")
              End Select
             End Sub
            End Module
            """), Is.EqualTo("TWO\n"),
            "n = 2 must reach Case 2. A fall to OTHER is the original defect returning: the "
            + "case tests were not emitted at all.");
    }

    [Test]
    public void SelectCase_WithNoMatchingArm_TakesElse()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Sub Main()
              Dim n As Integer = 99
              Select Case n
               Case 1
                PrintLine("ONE")
               Case 2
                PrintLine("TWO")
               Case Else
                PrintLine("OTHER")
              End Select
             End Sub
            End Module
            """), Is.EqualTo("OTHER\n"),
            "the Else arm must still be reachable — this is the half the broken generator got "
            + "right, and a comparison chain that never falls through would break it.");
    }

    /// <summary>
    /// The four value-pattern shapes plus multiple values per clause, in one program so an
    /// ordering or label-collision bug between consecutive switches in one method shows up.
    /// </summary>
    [Test]
    public void SelectCase_MatchesEveryValuePatternShape()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Sub Main()
              Dim i As Integer = 3
              Select Case i
               Case 1, 2
                PrintLine("LOW")
               Case 3, 4
                PrintLine("MID")
               Case Else
                PrintLine("HIGH")
              End Select

              Dim r As Integer = 7
              Select Case r
               Case 1 To 5
                PrintLine("R1-5")
               Case 6 To 10
                PrintLine("R6-10")
               Case Else
                PrintLine("ROUT")
              End Select

              Dim c As Integer = -4
              Select Case c
               Case Is > 0
                PrintLine("POS")
               Case Is < 0
                PrintLine("NEG")
               Case Else
                PrintLine("ZERO")
              End Select

              Dim o As Integer = 2
              Select Case o
               Case 1 Or 2 Or 3
                PrintLine("ORMATCH")
               Case Else
                PrintLine("OROTHER")
              End Select
             End Sub
            End Module
            """), Is.EqualTo("MID\nR6-10\nNEG\nORMATCH\n"),
            "comma-separated values, a range, a comparison and an Or pattern, in that order. "
            + "NEG in particular pins the SIGNED comparison: c = -4 read as unsigned is a huge "
            + "positive number and would print POS.");
    }

    [Test]
    public void SelectCase_IsEqualAndIsNotEqual_Match()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Sub Main()
              Dim n As Integer = 7
              Select Case n
               Case Is = 7
                PrintLine("EQ7")
               Case Else
                PrintLine("NOTEQ7")
              End Select
              Select Case n
               Case Is <> 7
                PrintLine("NE7")
               Case Else
                PrintLine("ISEQ7")
              End Select
             End Sub
            End Module
            """), Is.EqualTo("EQ7\nISEQ7\n"),
            "'Case Is <>' is the one comparison whose no-match edge is EQUALITY; inverting it "
            + "the wrong way prints NE7.");
    }

    /// <summary>
    /// ⚠ <b>This test is worthless if the subject is a literal.</b> The CLR interns literals, so
    /// a reference compare matches two equal literals and a test written that way confirms a
    /// broken generator. The subject here is CONCATENATED at run time — the IL really does call
    /// <c>String::Concat</c> before the switch — so the resulting string is a different object
    /// from the interned <c>"hello"</c> the case loads. A <c>ceq</c>/<c>bne.un</c> lowering
    /// prints SOTHER; only <c>String::Equals</c> prints HELLO.
    /// </summary>
    [Test]
    public void SelectCase_OnStrings_ComparesByValueNotByReference()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Sub Main()
              Dim a As String = "he"
              Dim b As String = "llo"
              Dim s As String = a & b
              Select Case s
               Case "nope"
                PrintLine("NOPE")
               Case "hello"
                PrintLine("HELLO")
               Case Else
                PrintLine("SOTHER")
              End Select

              Dim z As String = "zz"
              Select Case z
               Case Is <> "zz"
                PrintLine("SNE")
               Case Else
                PrintLine("SEQ")
              End Select
             End Sub
            End Module
            """), Is.EqualTo("HELLO\nSEQ\n"),
            "SOTHER means the string case compared REFERENCES, which is wrong for every string "
            + "that was not interned by the runtime.");
    }

    /// <summary>
    /// A <c>When</c> guard is built by IRBuilder with instruction emission SUPPRESSED, so its
    /// operands never entered a block and never got a local slot. Loading them the ordinary way
    /// pushes nothing and unbalances the stack; the generator rebuilds the guard tree in place
    /// instead. Two cases share the value 5 here so the test also pins that a FAILED guard falls
    /// through to the next case rather than jumping to Else.
    /// </summary>
    [Test]
    public void SelectCase_WhenGuard_IsEvaluated_AndFallsThroughOnFailure()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Sub Main()
              Dim g As Integer = 5
              Dim k As Integer = 1
              Select Case g
               Case 5 When k > 2
                PrintLine("GUARD-FIRST")
               Case 5 When k > 0
                PrintLine("GUARD-SECOND")
               Case Else
                PrintLine("GUARD-ELSE")
              End Select
             End Sub
            End Module
            """), Is.EqualTo("GUARD-SECOND\n"),
            "GUARD-FIRST means the guard was never evaluated; GUARD-ELSE means a failed guard "
            + "abandoned the whole Select instead of trying the next case.");
    }

    [Test]
    public void SelectCase_TakesTheFirstMatchingArm_InSourceOrder()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Sub Main()
              Dim d As Integer = 4
              Select Case d
               Case Is >= 4
                PrintLine("FIRST")
               Case 4
                PrintLine("SECOND")
               Case Else
                PrintLine("NEITHER")
              End Select
             End Sub
            End Module
            """), Is.EqualTo("FIRST\n"),
            "both arms match 4; VB takes the first in source order. (The C# backend cannot even "
            + "compile this shape — Roslyn rejects the second case as unreachable — so MSIL is "
            + "the only backend that pins the ordering rule.)");
    }

    /// <summary>
    /// Nesting, a loop body, a Function whose arms <c>Return</c> with no <c>Case Else</c>, and a
    /// floating-point range — the shapes where a single shared label counter or a missing
    /// fall-through would produce unassemblable or mis-branching IL.
    /// </summary>
    [Test]
    public void SelectCase_Nested_InALoop_AndInAReturningFunction()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Function Classify(v As Integer) As String
              Select Case v
               Case Is < 0
                Return "NEG"
               Case 0
                Return "ZERO"
               Case Is > 100
                Return "BIG"
              End Select
              Return "SMALL"
             End Function

             Sub Main()
              Dim i As Integer
              For i = 0 To 2
               Dim label As String = ""
               Select Case i
                Case 0
                 label = "a"
                Case 1
                 Select Case i * 2
                  Case 2
                   label = "b-nested"
                  Case Else
                   label = "b-else"
                 End Select
                Case Else
                 label = "z"
               End Select
               PrintLine(label)
              Next

              PrintLine(Classify(-5))
              PrintLine(Classify(0))
              PrintLine(Classify(500))
              PrintLine(Classify(7))

              Dim d As Double = 2.5
              Select Case d
               Case 1.5 To 3.0
                PrintLine("DRANGE")
               Case Else
                PrintLine("DOUT")
              End Select
             End Sub
            End Module
            """), Is.EqualTo("a\nb-nested\nz\nNEG\nZERO\nBIG\nSMALL\nDRANGE\n"),
            "SMALL in particular pins the no-Case-Else path: the default target must fall out of "
            + "the Select and reach the trailing Return.");
    }

    [Test]
    public void SelectCase_Nothing_MatchesTheDefaultValue()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Sub Main()
              Dim z As Integer = 0
              Select Case z
               Case Nothing
                PrintLine("ZNOTHING")
               Case Else
                PrintLine("ZOTHER")
              End Select
             End Sub
            End Module
            """), Is.EqualTo("ZNOTHING\n"),
            "VB's Nothing is the type's default value, so a zero Integer matches it.");
    }

    /// <summary>
    /// The comparison chain must END in an explicit branch to the default target.
    ///
    /// <para>⚠ <b>This is a text assertion on purpose, and it is the only kind that can hold
    /// this.</b> Every other Select Case test here runs the program, but no run-time oracle can
    /// see this property: <c>ControlFlowGraph</c> adds the default edge FIRST, so
    /// <c>GenerateBasicBlock</c> always lays the default block out immediately after the switch
    /// block, and a chain that just runs off its end falls into the default anyway and answers
    /// correctly. Deleting the branch passes all eleven round-trip tests. It stays because that
    /// adjacency is incidental — nothing in the emitter promises it — and the day a case block
    /// is laid out first, falling through would silently execute the wrong arm. So the branch is
    /// pinned where it is visible: in the generated text.</para>
    /// </summary>
    [Test]
    public void SelectCase_ChainEndsWithAnExplicitBranchToTheDefault()
    {
        var il = CompileToIl("""
            Module M
             Sub Main()
              Dim n As Integer = 2
              Select Case n
               Case 1
                PrintLine("ONE")
               Case Else
                PrintLine("OTHER")
              End Select
             End Sub
            End Module
            """);

        var lines = il.Split('\n').Select(l => l.TrimEnd('\r').Trim()).ToList();
        var defaultLabel = lines.FindIndex(l => l == "switch0default:");
        Assert.That(defaultLabel, Is.GreaterThan(0), "no default block in:\n" + il);
        Assert.That(lines[defaultLabel - 1], Is.EqualTo("br switch0default"),
            "the instruction before the default block's label must be an explicit branch to it, "
            + "not a fall-through that depends on block layout:\n" + il);
    }

    /// <summary>
    /// A type pattern needs <c>isinst</c> plus a binding slot, which this backend does not have.
    /// Emitting nothing for it is what the old generator effectively did — the case vanished and
    /// the input silently took Else — so it is refused at generation time instead.
    /// </summary>
    [Test]
    public void SelectCase_TypePattern_IsRefusedNotDropped()
    {
        var run = Run("""
            Module M
             Sub Main()
              Dim o As Object = 5
              Select Case o
               Case x As Integer
                PrintLine("INT")
               Case Else
                PrintLine("OTHER")
              End Select
             End Sub
            End Module
            """);

        Assert.Multiple(() =>
        {
            Assert.That(run.Outcome, Is.EqualTo(MsilOutcome.GenerateFailed),
                "refusal must come BEFORE any IL exists: " + run.Report);
            Assert.That(run.Detail,
                Does.Contain("IRTypePatternCase").And.Contain("no IL lowering"),
                "and it must name the pattern it cannot lower: " + run.Detail);
        });
    }

    /// <summary>
    /// IL's ordering opcodes are numeric-only. Applied to two object references they assemble
    /// but produce an unverifiable method, so <c>Case Is &gt; "b"</c> would have become an
    /// InvalidProgramException at run time — the failure mode this backend has hit twice before.
    /// </summary>
    [Test]
    public void SelectCase_OrderingComparisonOnStrings_IsRefused()
    {
        var run = Run("""
            Module M
             Sub Main()
              Dim s As String = "m"
              Select Case s
               Case Is > "b"
                PrintLine("GT")
               Case Else
                PrintLine("OTHER")
              End Select
             End Sub
            End Module
            """);

        Assert.Multiple(() =>
        {
            Assert.That(run.Outcome, Is.EqualTo(MsilOutcome.GenerateFailed),
                "refusal must come BEFORE any IL exists: " + run.Report);
            Assert.That(run.Detail,
                Does.Contain("String").And.Contain("String::Equals"),
                "and it must point at the string shapes that DO work: " + run.Detail);
        });
    }

    // ====================================================================================
    // Pinned divergences. Each names its root cause; each goes RED when fixed.
    // ====================================================================================

    // ---- Fixed 2026-09-15: the .NET Console surface ------------------------------------
    //
    // IRBuilder names a static member call `Type.Member` — WITH the dot — so
    // Console.WriteLine("x") reached TryEmitStdLibCall as "Console.WriteLine", matched no arm,
    // and fell through to the emit-a-call-on-the-current-class default:
    // `call object Combined::ConsoleWriteLine(string)`, which nothing defines. ilasm accepts a
    // MemberRef with no definition, so it died at RUN time with MissingMethodException.
    //
    // The fix has TWO halves and the first alone made things worse. Routing the name to the
    // printline arm produced `call void ...Console::WriteLine(string)` followed by a `stloc` —
    // because IRBuilder types Console.WriteLine as returning Object, so the caller stored a
    // result the void arm never pushed. That stack underflow is an InvalidProgramException:
    // a different, later failure in place of the original one.

    /// <summary>
    /// The whole Console surface in one transcript, including the two cases most likely to be
    /// got wrong: a non-string overload, and the ZERO-ARG <c>WriteLine()</c> that is a bare
    /// newline rather than a call with a missing argument.
    ///
    /// <para><c>Write</c> followed by <c>WriteLine()</c> must produce ONE line — that pairing is
    /// what proves <c>Write</c> really omitted its newline instead of the two calls happening
    /// to print on separate lines.</para>
    /// </summary>
    [Test]
    public void TheConsoleSurface_Runs()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Sub Main()
              Console.WriteLine("HELLO")
              Console.WriteLine(42)
              Console.Write("NO-NL")
              Console.WriteLine()
              Console.WriteLine("END")
             End Sub
            End Module
            """), Is.EqualTo("HELLO\n42\nNO-NL\nEND\n"));
    }

    /// <summary>
    /// <c>Console.ReadLine</c> — the alias whose arm genuinely RETURNS a value, so its result
    /// store must survive the void-arm suppression that <c>WriteLine</c> needed. Aliasing all
    /// three Console spellings and then suppressing all three would break this one silently.
    ///
    /// <para>The explicit <c>CType</c> is required by the FRONT END, not by MSIL: the analyzer
    /// types every <c>Type.Member</c> call as <c>Object</c>, so the C# backend rejects the
    /// uncast version with the identical message. Not this backend's gap.</para>
    /// </summary>
    [Test]
    public void ConsoleReadLine_ReadsStdin_AndKeepsItsValue()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Sub Main()
              Dim s As String = CType(Console.ReadLine(), String)
              PrintLine("GOT:" & s)
             End Sub
            End Module
            """, stdin: "TYPED\n"), Is.EqualTo("GOT:TYPED\n"));
    }

    /// <summary>
    /// ⛔ <b>The REST of the dotted static surface is still the phantom self-call.</b>
    /// <c>Math.Sqrt(16.0)</c> emits <c>call object Combined::MathSqrt(float64)</c> and dies with
    /// MissingMethodException, exactly as Console did.
    ///
    /// <para>Deliberately NOT fixed by aliasing, because the right answer is a design decision
    /// rather than a missing row. Measured: the C++ backend routes <c>Math.Sqrt</c> through the
    /// .NET PROXY (<c>bl_net_System_Math_Sqrt__System_Double_…</c>), while the C# backend emits
    /// <c>Math.Sqrt</c> directly. MSIL runs on .NET and could do either, and picking one decides
    /// how the whole <c>KnownNetStaticTypes</c> surface reaches this backend.</para>
    ///
    /// <para>⛔ And the obvious shortcut is unsound: stripping the <c>Type.</c> prefix and
    /// re-matching would route <c>Decimal.Round</c> onto the <c>Math.Round</c> arm — a silent
    /// mis-emission rather than a missing one. That is why the Console fix uses an explicit
    /// three-entry alias table instead.</para>
    /// </summary>
    [Test]
    public void TheRestOfTheDottedStaticSurface_IsStillAPhantomSelfCall_PinnedDivergence()
    {
        var run = Run("""
            Module M
             Sub Main()
              PrintLine(CStr(Math.Sqrt(16.0)))
             End Sub
            End Module
            """);

        Assert.Multiple(() =>
        {
            Assert.That(run.Outcome, Is.EqualTo(MsilOutcome.RunFailed), run.Report);
            Assert.That(run.Output, Does.Contain("MissingMethodException"), run.Detail);
            Assert.That(run.Il, Does.Contain("MathSqrt"),
                "still the dotted name sanitised into a self-call. When this goes red, the "
                + "design decision above was made — promote it to a real assertion.");
        });
    }

    /// <summary>
    /// ⛔ <b>An array local is declared but never ALLOCATED.</b> The type spec is right now
    /// (<c>string[]</c> assembles), so this moved from an ilasm rejection to a runtime
    /// <c>NullReferenceException</c> — the next cause in the same shape, and a more useful
    /// failure than the one it replaced.
    ///
    /// <para>Measured: the generated method contains ZERO <c>newarr</c> instructions.
    /// <c>Dim a(2) As String</c> must emit <c>ldc.i4.3 / newarr [mscorlib]System.String /
    /// stloc</c>, and element writes need <c>stelem</c>.</para>
    /// </summary>
    [Test]
    public void AnArrayLocal_IsNeverAllocated_PinnedDivergence()
    {
        var run = Run("""
            Module M
             Sub Main()
              Dim a(2) As String
              a(0) = "ZERO"
              PrintLine(a(0))
             End Sub
            End Module
            """);

        Assert.Multiple(() =>
        {
            Assert.That(run.Outcome, Is.EqualTo(MsilOutcome.RunFailed), run.Report);
            Assert.That(run.Output, Does.Contain("NullReferenceException"),
                "still the un-allocated array: " + run.Detail);
            Assert.That(run.Il, Does.Not.Contain("newarr"),
                "and the cause is still that no array is created. When newarr appears, this "
                + "test has done its job — assert the value instead.");
        });
    }

    /// <summary>
    /// A member call on a BCL receiver resolves to the BCL type — <c>s.ToUpper()</c> runs.
    ///
    /// <para>The receiver used to be <c>SanitizeName(type.Name)</c> unconditionally, so this
    /// emitted a call on a class literally named <c>String</c> and ilasm refused it. The same
    /// missing step produced <c>Console.WriteLine</c>'s phantom self-call, which is still open
    /// because it arrives through a different path — see
    /// <see cref="ConsoleWriteLine_IsAPhantomSelfCall_PinnedDivergence"/>.</para>
    /// </summary>
    [Test]
    public void AStringMethodCall_ResolvesToTheBclType_AndRuns()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Sub Main()
              Dim s As String = "hello"
              PrintLine(s.ToUpper())
             End Sub
            End Module
            """), Is.EqualTo("HELLO\n"));
    }

    // ---- Collections, native on MSIL as of 2026-09-15 ----------------------------------
    //
    // The honesty matrix (spec decision 12) grouped MSIL with LLVM here. That is permanent for
    // LLVM, which has no BCL to reach for; it was only ever true for MSIL while the backend was
    // unmaintained, because MSIL RUNS on .NET and List`1 is already in the runtime it targets.
    //
    // What made it non-trivial: IL requires a method on a generic instantiation to carry the
    // GENERIC DEFINITION's signature — List`1<string>::Add(!0), never Add(string) — and the
    // tempting rule ("substitute any parameter whose type equals a generic argument") is
    // UNSOUND, getting List(Of Integer).Add(5) right and RemoveAt(0) wrong. So the generic
    // positions are recorded per member in MSILCodeGenerator.CollectionMembers, and a member
    // outside that table is REFUSED rather than guessed.

    /// <summary>
    /// <c>List(Of String)</c> end to end: element read through the indexer, <c>Count</c>, and
    /// <c>Contains</c> — all three asserted on their VALUES.
    ///
    /// <para>The values matter more than usual here. An earlier revision of this ran, printed
    /// <c>4259924</c>, and carried on: the indexer stored its result over the LIST's own local
    /// slot and then read a different, uninitialized one. It did not crash, and it corrupted
    /// the collection for every later use.</para>
    /// </summary>
    [Test]
    public void AList_ReadsIndexesCountsAndContains()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Sub Main()
              Dim l As New List(Of String)
              l.Add("ALPHA")
              l.Add("BETA")
              PrintLine(l(1))
              PrintLine(CStr(l.Count))
              If l.Contains("ALPHA") Then
               PrintLine("HAS-ALPHA")
              End If
             End Sub
            End Module
            """), Is.EqualTo("BETA\n2\nHAS-ALPHA\n"));
    }

    /// <summary>
    /// <c>Dictionary(Of String, Integer)</c> end to end.
    ///
    /// <para>The indexer is the assertion that earns its place: it used to emit
    /// <c>IList`1&lt;int32&gt;::get_Item(string)</c> — naming the VALUE type as the list's
    /// element and passing the KEY as an integer index. Wrong in both positions, on a call that
    /// assembled cleanly. It is now <c>Dictionary`2&lt;string,int32&gt;::get_Item(!0)</c>
    /// returning <c>!1</c>, and <c>d("k") + 1</c> proves the value really came back as an
    /// Integer rather than something that merely type-checked.</para>
    /// </summary>
    [Test]
    public void ADictionary_ReadsByKeyCountsAndContainsKey()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Sub Main()
              Dim d As New Dictionary(Of String, Integer)
              d.Add("k", 41)
              PrintLine(CStr(d("k") + 1))
              PrintLine(CStr(d.Count))
              If d.ContainsKey("k") Then
               PrintLine("HAS-K")
              End If
             End Sub
            End Module
            """), Is.EqualTo("42\n1\nHAS-K\n"));
    }

    /// <summary>
    /// A collection crossing a function SIGNATURE, which is a different emission path from a
    /// local and was broken in a different way.
    ///
    /// <para>Signature sites read the parameter's type NAME, and a string cannot carry generic
    /// arguments — so a <c>List(Of Integer)</c> parameter emitted a bare <c>List</c> in both
    /// the declaration and the call, and neither assembled. The full suite caught this through
    /// the honesty-matrix guard tests after the narrower MSIL filter had gone green, which is
    /// the argument for running the whole suite before merging a backend change.</para>
    /// </summary>
    [Test]
    public void ACollectionCrossesAFunctionSignature()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Function Total(items As List(Of Integer)) As Integer
              Return items.Count
             End Function
             Sub Main()
              Dim l As New List(Of Integer)
              l.Add(7)
              l.Add(8)
              PrintLine(CStr(Total(l)))
             End Sub
            End Module
            """), Is.EqualTo("2\n"));
    }

    /// <summary>
    /// A collection that never becomes a local at all — an expression temporary whose member is
    /// read directly.
    /// </summary>
    [Test]
    public void ACollectionExpressionTemporary_Runs()
    {
        Assert.That(RunExpectingSuccess("""
            Module Program
             Function GetCount() As Integer
              Return New List(Of Integer)().Count
             End Function
             Sub Main()
              PrintLine(CStr(GetCount()))
             End Sub
            End Module
            """), Is.EqualTo("0\n"));
    }

    /// <summary>
    /// <b>A collection member OUTSIDE the table is refused, and that is what makes a narrow
    /// table safe to ship.</b>
    ///
    /// <para><c>RemoveAt</c> is the example on purpose: it is exactly the member the unsound
    /// substitution rule would have mis-emitted as <c>RemoveAt(!0)</c> on a
    /// <c>List(Of Integer)</c>. Refusing before any IL is emitted keeps the clean BasicLang
    /// diagnostic users had when collections were refused wholesale — the alternative is a call
    /// that assembles and dies with <c>MissingMethodException</c>.</para>
    /// </summary>
    [Test]
    public void ACollectionMemberOutsideTheTable_IsRefusedNotGuessed()
    {
        var run = Run("""
            Module M
             Sub Main()
              Dim l As New List(Of String)
              l.Add("A")
              l.RemoveAt(0)
             End Sub
            End Module
            """);

        Assert.Multiple(() =>
        {
            Assert.That(run.Outcome, Is.EqualTo(MsilOutcome.GenerateFailed),
                "refusal must come BEFORE any IL exists: " + run.Report);
            Assert.That(run.Detail, Does.Contain("RemoveAt").And.Contain("supported collection surface"),
                "and it must name the member and say how to widen the set: " + run.Detail);
        });
    }

    /// <summary>
    /// ⛔ Calling an instance method on a user class produces a method the CLR rejects. The
    /// local and the object now exist (see <see cref="ALocalOfAUserClass_Runs"/>), so this is a
    /// CALL-side defect, distinct from the type-spec one that was fixed.
    /// </summary>
    [Test]
    public void AUserClassInstanceMethodCall_IsAnInvalidProgram_PinnedDivergence()
    {
        var run = Run("""
            Class Greeter
             Public Name As String
             Public Function Greet() As String
              Return "HI-" & Name
             End Function
            End Class

            Module M
             Sub Main()
              Dim g As New Greeter()
              g.Name = "BOB"
              PrintLine(g.Greet())
             End Sub
            End Module
            """);

        Assert.Multiple(() =>
        {
            Assert.That(run.Outcome, Is.EqualTo(MsilOutcome.RunFailed), run.Report);
            Assert.That(run.Output, Does.Contain("InvalidProgramException"), run.Detail);
        });
    }

    /// <summary>
    /// ⛔ <c>Try</c>/<c>Catch</c> assembles but the CLR rejects the method. IL exception
    /// handling is structural — <c>.try { … } catch … { … }</c> regions with <c>leave</c> out
    /// of each, recorded in the method's EH table — and emitting the bodies as straight-line
    /// code with ordinary branches produces a method the verifier will not accept.
    /// </summary>
    [Test]
    public void TryCatch_ProducesAnInvalidProgram_PinnedDivergence()
    {
        var run = Run("""
            Module M
             Sub Main()
              Try
               PrintLine("TRY")
              Catch ex As Exception
               PrintLine("CATCH")
              End Try
             End Sub
            End Module
            """);

        Assert.Multiple(() =>
        {
            Assert.That(run.Outcome, Is.EqualTo(MsilOutcome.RunFailed), run.Report);
            Assert.That(run.Output, Does.Contain("InvalidProgramException"),
                "the CLR must still be rejecting the method: " + run.Detail);
        });
    }
}
