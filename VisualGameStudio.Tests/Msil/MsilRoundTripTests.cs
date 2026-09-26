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
///
/// <para><b>As of 2026-09-16 there are none left</b> — the last one,
/// <c>ASharedMethodOnAUserClass_IsAPhantomCall_PinnedDivergence</c>, went red when Shared members
/// started working and was replaced by the assertions below. An empty pin list is not a claim that
/// the backend is complete: the gaps that remain are recorded in <c>docs/HANDOFF.md</c>, and the
/// ones that live in the FRONT END (an inherited Shared field's type, a float-valued
/// <c>Return</c> from an <c>As Integer</c> method, a module-level initializer that is not a
/// literal) cannot be pinned here because they fail before this backend runs.</para>
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

    // ---- The dotted static surface, fixed 2026-09-16 -----------------------------------
    //
    // `Math.Sqrt(16.0)` sanitised the dot out of the name and emitted
    // `call object Combined::MathSqrt(float64)` — a call on THIS module's class, to a method
    // nothing defines, dying with MissingMethodException exactly as Console did.
    //
    // ⚠ The pin this replaces said the choice was "the C++ proxy route or the C# direct route,
    // MSIL could do either". That was WRONG and is worth recording, because it nearly drove the
    // work in the wrong direction. The .NET proxy is a NATIVE C ABI bridge:
    // [UnmanagedCallersOnly] exports on a Native AOT shim reached through a function-pointer
    // table, which exists because native code has no other way into .NET. Managed code cannot
    // call an UnmanagedCallersOnly method at all — so MSIL could only reach the proxy by
    // P/Invoking the native export so it could call BACK into the CLR, for members the CLR
    // already offers, and every emitted binary would gain a dependency on that shim. Measured
    // too: ResolvedNetTarget, the descriptor that drives proxy lowering, is null on this path.
    // There was only ever one route for this backend.
    //
    // So: direct IL from a table of recorded signatures, keyed on the FULL dotted name.

    [Test]
    public void ADottedStaticCall_EmitsARealBclCall()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Sub Main()
              PrintLine(CStr(Math.Sqrt(16.0)))
             End Sub
            End Module
            """), Is.EqualTo("4\n"),
            "a MissingMethodException naming Combined::MathSqrt means the dotted name was "
            + "sanitised into a self-call again.");
    }

    [Test]
    public void TheRecordedStaticSurface_Runs()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Sub Main()
              PrintLine(CStr(Math.Sqrt(9.0)))
              PrintLine(CStr(Math.Floor(3.7)))
              PrintLine(CStr(Math.Pow(2.0, 10.0)))
              PrintLine(CStr(Math.Abs(-7)))
              PrintLine(CStr(Math.Max(3, 9)))
              PrintLine(CStr(Math.Min(3, 9)))
              PrintLine(CStr(Convert.ToInt32("123")))
              PrintLine(CStr(Convert.ToDouble("2.5")))
             End Sub
            End Module
            """), Is.EqualTo("3\n3\n1024\n7\n9\n3\n123\n2.5\n"));
    }

    /// <summary>
    /// ⛔ The soundness property the old pin named as the reason not to take the shortcut:
    /// stripping the <c>Type.</c> prefix and matching the member alone routes
    /// <c>Decimal.Round</c> onto <c>Math.Round</c> — a silent WRONG ANSWER rather than a missing
    /// one. The table is keyed on the full dotted name, so <c>Decimal.Round</c> is simply absent.
    /// </summary>
    [Test]
    public void AStaticOnAnUnrecordedType_IsRefusedNotReroutedOntoAnotherType()
    {
        var run = Run("""
            Module M
             Sub Main()
              PrintLine(CStr(Decimal.Round(3.567)))
             End Sub
            End Module
            """);

        Assert.Multiple(() =>
        {
            Assert.That(run.Outcome, Is.EqualTo(MsilOutcome.GenerateFailed),
                "refusal must come BEFORE any IL exists: " + run.Report);
            Assert.That(run.Detail,
                Does.Contain("Decimal.Round").And.Contain("Math.Round"),
                "and it must name the mis-routing it is preventing: " + run.Detail);
        });
    }

    [Test]
    public void AnUnrecordedMemberOfARecordedType_IsRefused()
    {
        var run = Run("""
            Module M
             Sub Main()
              PrintLine(CStr(Math.Tan(1.0)))
             End Sub
            End Module
            """);

        Assert.Multiple(() =>
        {
            Assert.That(run.Outcome, Is.EqualTo(MsilOutcome.GenerateFailed), run.Report);
            Assert.That(run.Detail,
                Does.Contain("Math.Tan").And.Contain("supported .NET static surface"),
                "and it must say how to widen the set: " + run.Detail);
        });
    }

    /// <summary>
    /// Overloads are matched on argument TYPES, never on count. <c>Math.Abs</c> has int32, int64
    /// and float64 forms that differ only in signature, so an exact match must be preferred over
    /// a widening — <c>Abs(-7)</c> takes the int32 form and stays an integer.
    /// </summary>
    [Test]
    public void AnExactOverloadWins_OverAWidenedOne()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Sub Main()
              PrintLine(CStr(Math.Abs(-7)))
              PrintLine(CStr(Math.Abs(-7.5)))
             End Sub
            End Module
            """), Is.EqualTo("7\n7.5\n"),
            "7 then 7.5: the integer argument must not be widened onto the float64 overload, and "
            + "the double argument must not be truncated onto the int32 one.");
    }

    /// <summary>
    /// That an exact overload wins is pinned in the IL, because <b>it cannot be seen at run
    /// time</b>.
    ///
    /// <para>⚠ Measured, not assumed: with the float64 overload declared first and the
    /// exact-match pass removed, <c>Math.Abs(-7)</c> selects <c>Abs(float64)</c> and still prints
    /// <c>7</c>. The output is identical, so every round-trip assertion above stays green while
    /// the call binds to the wrong member and silently returns a Double where an Integer was
    /// asked for. Without the exact-match pass, selection would depend on the order rows happen
    /// to be written in <c>NetStaticMembers</c> — a trap for whoever next edits that table.</para>
    /// </summary>
    [Test]
    public void AnExactOverload_IsChosenRegardlessOfTableOrder()
    {
        var il = CompileToIl("""
            Module M
             Sub Main()
              PrintLine(CStr(Math.Abs(-7)))
             End Sub
            End Module
            """);

        Assert.Multiple(() =>
        {
            Assert.That(il, Does.Contain("Math::'Abs'(int32)"),
                "an int32 argument must bind the int32 overload:\n" + il);
            Assert.That(il, Does.Not.Contain("Math::'Abs'(float64)"),
                "and must not be widened onto the float64 one, which prints the same and hides "
                + "the mis-binding:\n" + il);
        });
    }

    /// <summary>
    /// A LOSSLESS widening is allowed, because the C# backend accepts <c>Math.Sqrt(16)</c> too
    /// (C# widens implicitly) and refusing it would put two backends at odds over an integer
    /// literal. ⛔ <c>int64</c>/<c>uint64</c> to <c>float64</c> is deliberately NOT on the list:
    /// it rounds above 2^53, and silently returning a different number than the caller passed is
    /// the class of defect this backend keeps producing.
    /// </summary>
    [Test]
    public void AnIntegerArgument_WidensToADoubleParameter()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Sub Main()
              PrintLine(CStr(Math.Sqrt(16)))
             End Sub
            End Module
            """), Is.EqualTo("4\n"));
    }

    /// <summary>
    /// ⛔ The other half of the widening rule, and the one that keeps it honest: a <c>Long</c>
    /// argument is REFUSED rather than widened. <c>int64</c> to <c>float64</c> is a legal IL
    /// conversion but it ROUNDS above 2^53, so admitting it would silently hand the BCL a
    /// different number than the caller wrote. Refusing says so instead.
    /// </summary>
    [Test]
    public void ALongArgument_IsRefusedRatherThanLossilyWidened()
    {
        var run = Run("""
            Module M
             Sub Main()
              Dim big As Long = 9007199254740993
              PrintLine(CStr(Math.Sqrt(big)))
             End Sub
            End Module
            """);

        Assert.Multiple(() =>
        {
            Assert.That(run.Outcome, Is.EqualTo(MsilOutcome.GenerateFailed),
                "a Long must not be silently widened onto Sqrt(float64): " + run.Report);
            Assert.That(run.Detail,
                Does.Contain("Math.Sqrt").And.Contain("int64"),
                "and the refusal must name the argument type it will not accept: " + run.Detail);
        });
    }

    // ---- Arrays, fixed 2026-09-16 ------------------------------------------------------
    //
    // `Dim a(2) As String` declared `[0] string[] a` and stopped. `.locals init` zeroes a slot;
    // it does not construct anything, so every access dereferenced null. Allocating exposed three
    // more defects on the same path, each of which had been unreachable behind the first:
    //
    //  - an IRGetElementPtr temp was typed as the ELEMENT, but `ldelema` pushes a managed pointer.
    //    The address was stored as if it were a value and `stind` then treated an integer as an
    //    address — AccessViolationException. The reference-typed half of this looked like it
    //    WORKED (a `string&` in a `string` slot printed the right answer), which makes it the
    //    more dangerous one: unverifiable IL that passes its test by luck.
    //  - `.field public Integer[] Cells` used the BasicLang type name, which ilasm rejects.
    //  - the array-literal emitter wrote `stloc t0` — an IR value NAME where IL wants a slot
    //    index — and an alloca store went through the indirect path, storing THROUGH a null slot.

    [Test]
    public void AnArrayLocal_IsAllocated_AndHoldsItsElements()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Sub Main()
              Dim a(2) As String
              a(0) = "ZERO"
              PrintLine(a(0))
             End Sub
            End Module
            """), Is.EqualTo("ZERO\n"),
            "a NullReferenceException here means the array was declared but never created.");
    }

    /// <summary>
    /// ⚠ <b><c>[n]</c> declares n ELEMENTS; <c>(n)</c> declares UPPER BOUND n (n + 1 elements).</b>
    /// That is the language rule since 26d8478a (VB's <c>Dim a(3)</c> holds indices 0..3); it
    /// replaced the older rule this test used to pin, under which <c>(n)</c> was an element count.
    /// Both forms come from the same <c>TypeInfo.ArrayDimensionSizes</c>, and MSIL must agree with
    /// C#, C++ and JavaScript on each — measured: all four print <c>1,3,len=3,upper=4</c> on this
    /// source, on the CLI, the CLI with <c>-O</c> and a Release <c>.blproj</c>.
    /// </summary>
    [Test]
    public void ABracketSize_IsAnElementCount_AndAParenSize_IsAnUpperBound()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Sub Main()
              Dim a[3] As Integer
              Dim b(3) As Integer
              a(0) = 1
              a(2) = 3
              PrintLine(CStr(a(0)) & "," & CStr(a(2)) & ",len=" & CStr(a.Length) & ",upper=" & CStr(b.Length))
             End Sub
            End Module
            """), Is.EqualTo("1,3,len=3,upper=4\n"),
            "len=4 would mean this backend read [3] as an upper bound; upper=3 would mean it read "
            + "(3) as an element count (the rule before 26d8478a) and drifted from C#/C++/JavaScript; "
            + "an IndexOutOfRange on a(2) would mean it allocated one element too few.");
    }

    [Test]
    public void AnArrayOfIntegers_ReadsAndWritesAcrossALoop()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Sub Main()
              Dim a[4] As Integer
              Dim i As Integer
              For i = 0 To a.Length - 1
               a(i) = i * i
              Next
              For i = 0 To a.Length - 1
               PrintLine(CStr(a(i)))
              Next
             End Sub
            End Module
            """), Is.EqualTo("0\n1\n4\n9\n"),
            "an int element is written through a managed pointer; if that pointer's slot is typed "
            + "as int32 rather than int32&, stind treats the value as an address and the process "
            + "dies with AccessViolationException.");
    }

    /// <summary>
    /// <c>a.Length</c> is neither a field nor a property in IL — it is the <c>ldlen</c> opcode.
    /// Emitting it as a field named it on a class that does not exist and failed to assemble.
    /// </summary>
    [Test]
    public void AnArrayLength_IsReadable()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Sub Main()
              Dim a[3] As Integer
              PrintLine(CStr(a.Length))
             End Sub
            End Module
            """), Is.EqualTo("3\n"));
    }

    /// <summary>
    /// Array FIELDS are the second allocation site and are not optional — the C++ backend's own
    /// note records that its first version of this fix did locals only, which turned "does not
    /// build" into "builds and access-violates".
    /// </summary>
    [Test]
    public void AnArrayField_IsAllocatedByTheConstructor()
    {
        Assert.That(RunExpectingSuccess("""
            Class Board
             Public Cells(3) As Integer
            End Class

            Module M
             Sub Main()
              Dim b As New Board()
              b.Cells(1) = 9
              PrintLine(CStr(b.Cells(1)))
             End Sub
            End Module
            """), Is.EqualTo("9\n"),
            "the field must also be DECLARED with an IL type spec — `.field public Integer[] "
            + "Cells` carries the BasicLang name and ilasm refuses the file.");
    }

    [Test]
    public void AnArrayOfObjects_HoldsReferences()
    {
        Assert.That(RunExpectingSuccess("""
            Class P
             Public N As Integer
            End Class

            Module M
             Sub Main()
              Dim a(2) As P
              a(0) = New P()
              a(0).N = 5
              PrintLine(CStr(a(0).N))
             End Sub
            End Module
            """), Is.EqualTo("5\n"));
    }

    [Test]
    public void AnArrayLiteral_BuildsAndIndexes()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Sub Main()
              Dim a() As Integer = {10, 20, 30}
              PrintLine(CStr(a(1)))
             End Sub
            End Module
            """), Is.EqualTo("20\n"),
            "the literal emitter wrote `stloc t0` — an IR value name where IL wants a slot index "
            + "— and the assignment stored THROUGH the destination slot rather than into it.");
    }

    [Test]
    public void AnUnsizedArrayDeclaration_AllocatesNothing()
    {
        var run = Run("""
            Module M
             Sub Main()
              Dim a() As String
              PrintLine("DECLARED")
             End Sub
            End Module
            """);

        Assert.Multiple(() =>
        {
            Assert.That(run.Output, Is.EqualTo("DECLARED\n"), run.Report);
            Assert.That(run.Il, Does.Not.Contain("newarr"),
                "`Dim a() As String` declares a variable whose storage an assignment supplies "
                + "later; inventing a size for it would be wrong: " + run.Il);
        });
    }

    /// <summary>
    /// Refused rather than allocated, on purpose. A rank-2 declaration currently collapses to a
    /// rank-1 IL type and indexing emits <c>ldelema</c> with <c>Indices[0]</c> alone, so
    /// <c>g(1, 2)</c> reads and writes <c>g(1)</c>. Allocating it would upgrade a loud
    /// NullReferenceException into a quiet wrong answer.
    /// </summary>
    [Test]
    public void AMultiDimensionalArray_IsRefusedNotSilentlyFlattened()
    {
        var run = Run("""
            Module M
             Sub Main()
              Dim g(2, 3) As Integer
              g(1, 2) = 42
              PrintLine(CStr(g(1, 2)))
             End Sub
            End Module
            """);

        Assert.Multiple(() =>
        {
            Assert.That(run.Outcome, Is.EqualTo(MsilOutcome.GenerateFailed),
                "refusal must come BEFORE any IL exists: " + run.Report);
            Assert.That(run.Detail,
                Does.Contain("2-dimensional").And.Contain("silently reads and writes"),
                "and it must name the rank and the wrong answer it prevents: " + run.Detail);
        });
    }

    /// <summary>
    /// A member call on a BCL receiver resolves to the BCL type — <c>s.ToUpper()</c> runs.
    ///
    /// <para>The receiver used to be <c>SanitizeName(type.Name)</c> unconditionally, so this
    /// emitted a call on a class literally named <c>String</c> and ilasm refused it. The same
    /// missing step produced <c>Console.WriteLine</c>'s phantom self-call; that one was closed in
    /// <c>75c1dcc</c> ("MSIL: the .NET Console surface, in two halves") and its pin deleted, so
    /// the cross-reference that used to sit here named a test that no longer exists.</para>
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

    // ---- Instance methods, fixed 2026-09-16 --------------------------------------------
    //
    // ⚠ The pin this replaces named the wrong cause. It read "this is a CALL-side defect", but
    // the call was never the problem — an instance method that touches nothing runs fine, and the
    // stack trace said `at Greeter.Greet()`, inside the callee. The real cause is that the
    // emitter had no notion of `Me`: an instance method is handed its receiver in argument slot
    // 0, and nothing accounted for that. Five failures followed from the one omission:
    //
    //   1. Every parameter was off by one. `Add(a, b)` emitted `ldarg.0 / ldarg.1 / add`, adding
    //      the OBJECT REFERENCE to `a`. That is the dangerous one — 20 + 22 returned 872452332
    //      rather than crashing, and no pinned test was watching it.
    //   2. A bare field READ pushed nothing, so the next instruction ran an operand short.
    //   3. A bare field WRITE stored into a temporary and was dropped: `N = N + 1` computed the
    //      sum and threw it away.
    //   4. `Me` resolved to nothing, so `Me.Name` emitted `ldfld` against an empty stack.
    //   5. A sibling self-call emitted `call int32 Program::Inner()` — a static call, on a class
    //      that does not exist, to a method that is not static.

    [Test]
    public void AnInstanceMethod_ReadsItsOwnFields()
    {
        Assert.That(RunExpectingSuccess("""
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
            """), Is.EqualTo("HI-BOB\n"),
            "a bare field name inside an instance method must become `ldarg.0` + `ldfld`; "
            + "resolving it to nothing leaves String::Concat one operand short and the CLR "
            + "rejects the whole method.");
    }

    /// <summary>
    /// ⚠ <b>The most dangerous defect in this group, and the one no pin was watching.</b> It did
    /// not crash — it returned a plausible-looking integer. <c>ldarg.0</c> is <c>Me</c> in an
    /// instance method, so numbering parameters from 0 made the first one read the object
    /// reference: <c>Add(20, 22)</c> returned 872452332.
    /// </summary>
    [Test]
    public void AnInstanceMethodsParameters_AreOffsetPastMe()
    {
        Assert.That(RunExpectingSuccess("""
            Class Adder
             Public Function Add(a As Integer, b As Integer) As Integer
              Return a + b
             End Function
            End Class

            Module M
             Sub Main()
              Dim x As New Adder()
              PrintLine(CStr(x.Add(20, 22)))
             End Sub
            End Module
            """), Is.EqualTo("42\n"),
            "any answer other than 42 — especially a large one — means a parameter is reading the "
            + "argument slot before it.");
    }

    /// <summary>
    /// Several parameters AND a field in one expression, so an offset that is wrong by a
    /// different amount per position cannot pass. Each parameter contributes a distinct decimal
    /// place, so a swap or a shift changes the digits rather than the total.
    /// </summary>
    [Test]
    public void AnInstanceMethod_MixesSeveralParametersWithAField()
    {
        Assert.That(RunExpectingSuccess("""
            Class Calc
             Public Base As Integer
             Public Function Mix(a As Integer, b As Integer, c As Integer) As Integer
              Return Base + a * 100 + b * 10 + c
             End Function
            End Class

            Module M
             Sub Main()
              Dim k As New Calc()
              k.Base = 5000
              PrintLine(CStr(k.Mix(1, 2, 3)))
             End Sub
            End Module
            """), Is.EqualTo("5123\n"));
    }

    [Test]
    public void AnInstanceMethod_WritesItsOwnFields()
    {
        Assert.That(RunExpectingSuccess("""
            Class Counter
             Public N As Integer
             Public Sub Bump()
              N = N + 1
             End Sub
             Public Function Value() As Integer
              Return N
             End Function
            End Class

            Module M
             Sub Main()
              Dim c As New Counter()
              c.Bump()
              c.Bump()
              PrintLine(CStr(c.Value()))
             End Sub
            End Module
            """), Is.EqualTo("2\n"),
            "`stfld` wants the object UNDER the value and this backend emits the value first, so "
            + "the store goes through a scratch slot. 0 means the assignment was dropped into a "
            + "temporary, which is what it used to do.");
    }

    [Test]
    public void MeQualifiedFieldAccess_Resolves()
    {
        Assert.That(RunExpectingSuccess("""
            Class Greeter
             Public Name As String
             Public Function Greet() As String
              Return "HI-" & Me.Name
             End Function
            End Class

            Module M
             Sub Main()
              Dim g As New Greeter()
              g.Name = "BOB"
              PrintLine(g.Greet())
             End Sub
            End Module
            """), Is.EqualTo("HI-BOB\n"),
            "`Me` is the receiver in argument slot 0; resolving it to nothing left `ldfld` with "
            + "an empty stack.");
    }

    [Test]
    public void AnInstanceMethod_CallsASiblingOnItself()
    {
        Assert.That(RunExpectingSuccess("""
            Class Chain
             Public Function Inner() As Integer
              Return 7
             End Function
             Public Function Outer() As Integer
              Return Inner() + 1
             End Function
            End Class

            Module M
             Sub Main()
              Dim c As New Chain()
              PrintLine(CStr(c.Outer()))
             End Sub
            End Module
            """), Is.EqualTo("8\n"),
            "an unqualified sibling call needs the receiver pushed and a `callvirt instance`; it "
            + "used to emit a static call on a phantom `Program` class.");
    }

    [Test]
    public void AConstructorParameter_InitializesAField()
    {
        Assert.That(RunExpectingSuccess("""
            Class P
             Public N As Integer
             Public Sub New(v As Integer)
              N = v
             End Sub
            End Class

            Module M
             Sub Main()
              Dim p As New P(5)
              PrintLine(CStr(p.N))
             End Sub
            End Module
            """), Is.EqualTo("5\n"),
            "a constructor is an instance member too, and its body needs a locals declaration — "
            + "which the constructor path never emitted at all.");
    }

    /// <summary>
    /// A <c>Try</c> inside a class member. This was a loud refusal before ("the catch variable
    /// has no local slot") because only the module-level path prepared exception-handling state;
    /// the class paths now share it, along with the emitted-block set and the lowered-return exit
    /// block that a <c>Return</c> inside a region leaves to.
    /// </summary>
    [Test]
    public void ATryInsideAClassMethod_Runs()
    {
        Assert.That(RunExpectingSuccess("""
            Class Risky
             Public Name As String
             Public Function Run() As String
              Try
               Throw New Exception("x")
              Catch ex As Exception
               Return "CAUGHT-" & Name
              End Try
              Return "NONE"
             End Function
            End Class

            Module M
             Sub Main()
              Dim r As New Risky()
              r.Name = "Z"
              PrintLine(r.Run())
             End Sub
            End Module
            """), Is.EqualTo("CAUGHT-Z\n"));
    }

    // ====================================================================================
    // Shared members on user classes, fixed 2026-09-16. This replaces
    // ASharedMethodOnAUserClass_IsAPhantomCall_PinnedDivergence, which asserted
    // MissingMethodException and went red the moment the call started working.
    //
    // ⛔ The pin named ONE defect and there were two, entangled:
    //
    //  1. `MathUtil.Twice` reached the emitter as a dotted name and SanitizeName strips dots
    //     (it is shared across backends, in ICodeGenerator), so the call became
    //     `call Combined::MathUtilTwice` — the module's own class, a method nothing defines.
    //  2. IRModule.Functions holds every class member too: IRBuilder does member.Accept(this),
    //     which appends to Functions, then stores that SAME IRFunction as
    //     IRMethod.Implementation. The module class emitted a second static copy of every
    //     method on every class.
    //
    // Fixing (1) alone leaves the duplicates; fixing (2) alone BREAKS the unqualified sibling
    // call, which today resolves to the duplicate. Neither is safe without the other.
    // ====================================================================================

    /// <summary>The shape the pin asserted was broken. It must now print the answer.</summary>
    [Test]
    public void ASharedMethodOnAUserClass_Runs()
    {
        Assert.That(RunExpectingSuccess("""
            Class MathUtil
             Public Shared Function Twice(v As Integer) As Integer
              Return v * 2
             End Function
            End Class

            Module M
             Sub Main()
              PrintLine(CStr(MathUtil.Twice(21)))
             End Sub
            End Module
            """), Is.EqualTo("42\n"));
    }

    [Test]
    public void ASharedSub_Runs()
    {
        Assert.That(RunExpectingSuccess("""
            Class Logger
             Public Shared Sub Say(m As String)
              PrintLine("LOG:" & m)
             End Sub
            End Class

            Module M
             Sub Main()
              Logger.Say("hi")
             End Sub
            End Module
            """), Is.EqualTo("LOG:hi\n"));
    }

    /// <summary>
    /// ⛔ The call that makes the two fixes inseparable. Before, this bound to the DUPLICATE the
    /// module class carried and printed the right answer for the wrong reason; remove the
    /// duplicates without teaching the unqualified arm to resolve, and it becomes a
    /// MissingMethodException. It is the regression test for doing only half the work.
    /// </summary>
    [Test]
    public void AnUnqualifiedSiblingCallToASharedMethod_BindsToTheDeclaringClass()
    {
        Assert.That(RunExpectingSuccess("""
            Class Box
             Public Shared Function Adjust(v As Integer) As Integer
              Return v - 42
             End Function
             Public Function Use() As Integer
              Return Adjust(84)
             End Function
            End Class

            Module M
             Sub Main()
              Dim b As New Box()
              PrintLine(CStr(b.Use()))
             End Sub
            End Module
            """), Is.EqualTo("42\n"));
    }

    /// <summary>A <c>Shared</c> method naming its own class from inside that class.</summary>
    [Test]
    public void ASharedMethodQualifiedByItsOwnClass_Runs()
    {
        Assert.That(RunExpectingSuccess("""
            Class Box
             Public Shared Function Adjust(v As Integer) As Integer
              Return v - 42
             End Function
             Public Function Use() As Integer
              Return Box.Adjust(84)
             End Function
            End Class

            Module M
             Sub Main()
              Dim b As New Box()
              PrintLine(CStr(b.Use()))
             End Sub
            End Module
            """), Is.EqualTo("42\n"));
    }

    /// <summary>One Shared member calling another, where there is no receiver anywhere in sight.</summary>
    [Test]
    public void ASharedMethodCallingAnotherSharedMethod_Runs()
    {
        Assert.That(RunExpectingSuccess("""
            Class Box
             Public Shared Function Inner() As Integer
              Return 42
             End Function
             Public Shared Function Outer() As Integer
              Return Inner()
             End Function
            End Class

            Module M
             Sub Main()
              PrintLine(CStr(Box.Outer()))
             End Sub
            End Module
            """), Is.EqualTo("42\n"));
    }

    /// <summary>
    /// ⛔ BasicLang lets a <c>Shared</c> member be reached through an INSTANCE; IL does not. The
    /// method has no <c>this</c> parameter, so <c>callvirt instance string Box::Tag()</c> with a
    /// receiver on the stack is a method the CLR cannot find. The receiver is still evaluated and
    /// then discarded, because the expression may have side effects.
    /// </summary>
    [Test]
    public void ASharedMethodReachedThroughAnInstance_StillBindsStatically()
    {
        Assert.That(RunExpectingSuccess("""
            Class Box
             Public Shared Function Tag() As String
              Return "T"
             End Function
            End Class

            Module M
             Sub Main()
              Dim b As New Box()
              PrintLine(b.Tag())
             End Sub
            End Module
            """), Is.EqualTo("T\n"));
    }

    /// <summary>
    /// ⛔ The duplication defect at its loudest. Two classes each declaring <c>Go</c> both
    /// flattened onto the module class, and ilasm refused the whole file with "Duplicate method
    /// declaration" — the program did not build at all. Nothing about this needs <c>Shared</c>:
    /// the instance case failed identically, which is why the filter is on class MEMBERSHIP and
    /// not on staticness.
    /// </summary>
    [Test]
    public void TwoClassesMayDeclareAMethodOfTheSameName()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RunExpectingSuccess("""
                Class A
                 Public Shared Function Go() As String
                  Return "A"
                 End Function
                End Class

                Class B
                 Public Shared Function Go() As String
                  Return "B"
                 End Function
                End Class

                Module M
                 Sub Main()
                  PrintLine(A.Go() & B.Go())
                 End Sub
                End Module
                """), Is.EqualTo("AB\n"), "two Shared methods of the same name");

            Assert.That(RunExpectingSuccess("""
                Class A
                 Public Function Go() As String
                  Return "A"
                 End Function
                End Class

                Class B
                 Public Function Go() As String
                  Return "B"
                 End Function
                End Class

                Module M
                 Sub Main()
                  Dim a As New A()
                  Dim b As New B()
                  PrintLine(a.Go() & b.Go())
                 End Sub
                End Module
                """), Is.EqualTo("AB\n"), "and two INSTANCE methods of the same name");
        });
    }

    /// <summary>
    /// ⛔ A <c>Shared</c> field read as <c>Counter.Total</c>. The receiver is a TYPE NAME, not a
    /// value: loading it emitted <c>// WARNING: Unknown local 'Counter'</c> and pushed nothing, so
    /// the <c>ldfld</c> that followed read a field off an empty stack and the CLR rejected the
    /// method. The 42 also pins the initializer: <c>= 5</c> lives in the class's <c>.cctor</c>,
    /// and dropping it gives 37 rather than a crash.
    /// </summary>
    [Test]
    public void ASharedField_ReadsAndWritesThroughItsType_AndKeepsItsInitializer()
    {
        Assert.That(RunExpectingSuccess("""
            Class Counter
             Public Shared Total As Integer = 5
            End Class

            Module M
             Sub Main()
              Counter.Total = Counter.Total + 37
              PrintLine(CStr(Counter.Total))
             End Sub
            End Module
            """), Is.EqualTo("42\n"));
    }

    /// <summary>
    /// The same field by BARE name inside its own class. Registering it is what routes the
    /// assignment through the field rather than into an anonymous temporary — without it
    /// <c>Total = Total + 1</c> computes the sum and drops it, and this prints 0.
    /// </summary>
    [Test]
    public void ASharedField_IsReachableByBareNameInsideItsClass()
    {
        Assert.That(RunExpectingSuccess("""
            Class Counter
             Public Shared Total As Integer
             Public Shared Sub Bump()
              Total = Total + 1
             End Sub
            End Class

            Module M
             Sub Main()
              Counter.Bump()
              Counter.Bump()
              PrintLine(CStr(Counter.Total))
             End Sub
            End Module
            """), Is.EqualTo("2\n"));
    }

    /// <summary>
    /// A sized <c>Shared</c> array. <c>EmitArrayFieldAllocations</c> deliberately skips static
    /// fields ("a static field is not this instance's to create"), so the class's type
    /// initializer is the ONLY place this gets storage — left out, the first index throws.
    /// </summary>
    [Test]
    public void ASharedArrayField_HasStorage()
    {
        Assert.That(RunExpectingSuccess("""
            Class Pool
             Public Shared Slots(3) As Integer
            End Class

            Module M
             Sub Main()
              Pool.Slots(1) = 7
              PrintLine(CStr(Pool.Slots(1)))
             End Sub
            End Module
            """), Is.EqualTo("7\n"));
    }

    /// <summary>
    /// ⚠ Resolution order: a receiver name is only read as a TYPE when nothing nearer in scope
    /// answers to it. Here <c>Counter</c> is a local of type <c>Holder</c> AND the name of a class
    /// with a <c>Shared Total</c>, so <c>Counter.Total</c> must reach the local's instance field.
    /// Letting the class win prints 9.
    ///
    /// <para>⛔ The read is deliberately NOT preceded by a write, and the first version of this
    /// test was wrong for exactly that reason. <c>Counter.Total = 4</c> followed by a read prints
    /// 4 either way — the mis-resolution writes and reads the SAME wrong location, so it is
    /// self-consistent and invisible. Only a value the program did not put there (the
    /// constructor's 4 versus the class's 9) tells the two apart.</para>
    /// </summary>
    [Test]
    public void ALocalShadowsAClassOfTheSameName_WhenUsedAsAReceiver()
    {
        Assert.That(RunExpectingSuccess("""
            Class Counter
             Public Shared Total As Integer = 9
            End Class

            Class Holder
             Public Total As Integer
             Public Sub New()
              Total = 4
             End Sub
            End Class

            Module M
             Sub Main()
              Dim Counter As New Holder()
              PrintLine(CStr(Counter.Total))
             End Sub
            End Module
            """), Is.EqualTo("4\n"),
            "the local's instance field, not the like-named class's Shared field");
    }

    /// <summary>
    /// ⛔ IL TEXT, for the same reason as the module class's copy of this: nothing a round trip can
    /// observe distinguishes <c>beforefieldinit</c> from its absence in the IL this backend emits.
    /// A user class gets a type initializer only when a <c>Shared</c> field needs one, and it
    /// drops the flag when it does — matching what the C# compiler does for a class with a static
    /// constructor, and matching the module class so the two cannot drift.
    /// </summary>
    [Test]
    public void AUserClass_DropsBeforeFieldInit_OnlyWhenASharedFieldNeedsInitializing()
    {
        var initialized = CompileToIl("""
            Class Counter
             Public Shared Total As Integer = 5
            End Class

            Module M
             Sub Main()
              PrintLine(CStr(Counter.Total))
             End Sub
            End Module
            """);

        var bare = CompileToIl("""
            Class Counter
             Public Shared Total As Integer
            End Class

            Module M
             Sub Main()
              PrintLine(CStr(Counter.Total))
             End Sub
            End Module
            """);

        Assert.Multiple(() =>
        {
            Assert.That(ClassLine(initialized, "Counter"), Does.Not.Contain("beforefieldinit"),
                "a Shared field with an initializer needs a .cctor, so the flag goes");
            Assert.That(ClassLine(bare, "Counter"), Does.Contain("beforefieldinit"),
                "a zero-valued Shared field needs no .cctor — the CLR already zeroes it — so the "
                + "relaxed flag stays and no dead type initializer is emitted");
        });
    }

    /// <summary>
    /// ⛔ <c>Derived.Tag()</c> where <c>Tag</c> is Shared on <c>Base</c>. The call must name the
    /// class that DECLARES the method — <c>call ... Derived::Tag()</c> leaves the CLR looking for
    /// something Derived does not define. The call's own type is not a usable source for the
    /// signature either: this one arrives typed <c>object</c> where the method returns
    /// <c>string</c>, so the signature is spelled from the declaration instead.
    /// </summary>
    [Test]
    public void AnInheritedSharedMethod_NamesItsDeclaringClass()
    {
        Assert.That(RunExpectingSuccess("""
            Class Base
             Public Shared Function Tag() As String
              Return "BASE"
             End Function
            End Class

            Class Derived
             Inherits Base
            End Class

            Module M
             Sub Main()
              PrintLine(Derived.Tag())
             End Sub
            End Module
            """), Is.EqualTo("BASE\n"));
    }

    // ---- Try/Catch, fixed 2026-09-16 ---------------------------------------------------
    //
    // Five separate defects, of which only the first was known:
    //
    //  1. Visit(IRTryCatch) inlined the try block's non-branch instructions into `.try { }` and
    //     hard-coded `leave EndTry`. A Try body is a GRAPH of blocks, so only the entry block's
    //     straight-line instructions landed in the region — and GenerateBasicBlock then emitted
    //     those same blocks AGAIN as ordinary labelled blocks, because nothing marked them
    //     handled. The real work ran OUTSIDE the protected region. A Try around an If printed
    //     the right answer while protecting nothing.
    //  2. The catch variable was allocated during emission with a counter unrelated to
    //     _localIndices, after `.locals init` had already been written — producing `stloc 0` in
    //     a method with no locals at all, or a store onto an unrelated variable of an unrelated
    //     type where a slot happened to exist.
    //  3. FinallyBlock was ignored entirely, leaving `.try { }` with NO handler, which ilasm
    //     rejects outright.
    //  4. A catch type was spelled "[mscorlib]System." + the clause's name, so a user-defined
    //     exception became a reference to a BCL type that does not exist.
    //  5. Throw emitted NOTHING. ICodeGenerator declares Visit(IRThrow) as an empty virtual and
    //     MSIL never overrode it, so execution continued straight past every Throw — which is
    //     why nothing could reach a handler to expose 1–4 in the first place.

    [Test]
    public void TryCatch_RunsTheTryBlock_WhenNothingThrows()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Sub Main()
              Try
               PrintLine("TRY")
              Catch ex As Exception
               PrintLine("CATCH")
              End Try
              PrintLine("AFTER")
             End Sub
            End Module
            """), Is.EqualTo("TRY\nAFTER\n"),
            "the happy path must run the try body once and skip the handler. CATCH here, or a "
            + "doubled TRY, means the body is being emitted outside its own region.");
    }

    /// <summary>
    /// The first test that can fail for the RIGHT reason: it needs <c>Throw</c> to actually
    /// throw. While <c>Visit(IRThrow)</c> was an inherited no-op, every Try/Catch test was
    /// vacuous — no input could reach a handler.
    /// </summary>
    [Test]
    public void Throw_ReachesTheCatchBlock()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Sub Main()
              Try
               PrintLine("TRY")
               Throw New Exception("boom")
               PrintLine("UNREACHED")
              Catch ex As Exception
               PrintLine("CAUGHT")
              End Try
              PrintLine("AFTER")
             End Sub
            End Module
            """), Is.EqualTo("TRY\nCAUGHT\nAFTER\n"),
            "UNREACHED in the output means Throw emitted nothing and execution walked straight "
            + "past it — the defect that made every other Try/Catch assertion vacuous.");
    }

    [Test]
    public void CatchVariable_ReadsTheExceptionMessage()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Sub Main()
              Try
               Throw New InvalidOperationException("kaboom")
              Catch ex As InvalidOperationException
               PrintLine(ex.Message)
              End Try
             End Sub
            End Module
            """), Is.EqualTo("kaboom\n"),
            "an exception's members arrive as FIELD accesses and none of them are fields; "
            + "`ldfld ...::Message` assembles and then dies with MissingFieldException.");
    }

    /// <summary>
    /// Ordering matters and is observable: the handler runs first, the finally second, and both
    /// before the statement after End Try.
    /// </summary>
    [Test]
    public void TryCatchFinally_RunsTheHandlerThenTheFinally()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Sub Main()
              Try
               PrintLine("TRY")
               Throw New Exception("boom")
              Catch ex As Exception
               PrintLine("CATCH")
              Finally
               PrintLine("FIN")
              End Try
              PrintLine("AFTER")
             End Sub
            End Module
            """), Is.EqualTo("TRY\nCATCH\nFIN\nAFTER\n"),
            "IL forbids catch and finally on ONE region, so this shape must nest — the inner "
            + "region carries the catches, the outer the finally. A missing FIN means the "
            + "finally was dropped; FIN before CATCH means the nesting is inverted.");
    }

    [Test]
    public void TryFinally_WithNoCatch_RunsTheFinally()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Sub Main()
              Try
               PrintLine("TRY")
              Finally
               PrintLine("FIN")
              End Try
              PrintLine("AFTER")
             End Sub
            End Module
            """), Is.EqualTo("TRY\nFIN\nAFTER\n"),
            "a Try with only a Finally used to emit `.try { }` with no handler at all, which "
            + "ilasm refuses to assemble.");
    }

    [Test]
    public void MultipleCatchClauses_SelectTheMatchingType()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Sub Main()
              Try
               Throw New InvalidOperationException("bad")
              Catch ex As FormatException
               PrintLine("FORMAT")
              Catch ex As InvalidOperationException
               PrintLine("INVALIDOP")
              Catch ex As Exception
               PrintLine("GENERAL")
              End Try
              PrintLine("AFTER")
             End Sub
            End Module
            """), Is.EqualTo("INVALIDOP\nAFTER\n"),
            "the clause order is preserved and the type actually discriminates: FORMAT means "
            + "the first handler ran regardless of type, GENERAL means the specific clause was "
            + "skipped.");
    }

    /// <summary>
    /// A clause whose type cannot match must NOT swallow the exception. The C++ backend shipped
    /// exactly this bug — every typed Catch emitted a byte-identical handler, so the first one
    /// stole exceptions from the correct outer handler.
    /// </summary>
    [Test]
    public void ANonMatchingCatch_DoesNotSwallowTheException()
    {
        var run = Run("""
            Module M
             Sub Main()
              Try
               Throw New FormatException("fmt")
              Catch ex As InvalidOperationException
               PrintLine("WRONG")
              End Try
              PrintLine("AFTER")
             End Sub
            End Module
            """);

        Assert.Multiple(() =>
        {
            Assert.That(run.Output, Does.Not.Contain("WRONG"),
                "a FormatException must not enter an InvalidOperationException handler.");
            Assert.That(run.Output, Does.Not.Contain("AFTER"),
                "and it must not be silently swallowed either — nothing after End Try runs.");
            Assert.That(run.Output, Does.Contain("System.FormatException"),
                "it propagates out of Main as itself: " + run.Report);
        });
    }

    [Test]
    public void ACatchWithNoVariable_StillRuns()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Sub Main()
              Try
               Throw New Exception("x")
              Catch
               PrintLine("CAUGHT-NOVAR")
              End Try
              PrintLine("AFTER")
             End Sub
            End Module
            """), Is.EqualTo("CAUGHT-NOVAR\nAFTER\n"));
    }

    /// <summary>
    /// The variable-less handler's <c>pop</c>, pinned on a shape that can actually see it.
    ///
    /// <para>⚠ <b>The straight-line case above cannot.</b> A handler is entered with the exception
    /// on the stack, and <c>leave</c> empties the evaluation stack on its way out — so a handler
    /// that never pops, and whose only exit is a <c>leave</c>, runs correctly with the exception
    /// sitting underneath everything. Deleting the <c>pop</c> passes it. This shape branches
    /// inside the handler and nests a Try in it, so the leftover operand has to survive a
    /// join point, and the CLR rejects the method instead. Measured both ways, not assumed.</para>
    /// </summary>
    [Test]
    public void ACatchWithNoVariable_PopsTheException()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Sub Main()
              Dim n As Integer = 5
              Try
               Throw New Exception("x")
              Catch
               If n > 3 Then
                PrintLine("BIG")
               Else
                PrintLine("SMALL")
               End If
               Try
                Throw New Exception("y")
               Catch
                PrintLine("INNER")
               End Try
               PrintLine("DONE" & CStr(n + 1))
              End Try
              PrintLine("AFTER")
             End Sub
            End Module
            """), Is.EqualTo("BIG\nINNER\nDONE6\nAFTER\n"),
            "an unpopped exception left under a branch join makes the whole method unverifiable.");
    }

    /// <summary>
    /// <c>ret</c> is illegal inside a protected region, so a Return in a Try is lowered to a
    /// store plus a <c>leave</c> to one exit that owns the real <c>ret</c>. Going out through
    /// <c>leave</c> is also what runs the finally — which is why FIN must appear before the
    /// returned value is printed.
    /// </summary>
    [Test]
    public void ReturnInsideATry_ReturnsTheValue_AndStillRunsTheFinally()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Function F() As Integer
              Try
               PrintLine("TRY")
               Return 7
              Finally
               PrintLine("FIN")
              End Try
             End Function
             Sub Main()
              PrintLine(CStr(F()))
             End Sub
            End Module
            """), Is.EqualTo("TRY\nFIN\n7\n"),
            "FIN missing means the finally was bypassed by the return; a wrong number means the "
            + "result never reached the exit block.");
    }

    [Test]
    public void ReturnFromBothArms_PicksTheArmThatRan()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Function F() As Integer
              Try
               Throw New Exception("x")
              Catch ex As Exception
               Return 2
              End Try
              Return 3
             End Function
             Sub Main()
              PrintLine(CStr(F()))
             End Sub
            End Module
            """), Is.EqualTo("2\n"),
            "3 means the catch's Return fell through instead of leaving to the exit.");
    }

    [Test]
    public void ANestedTry_HandlesItsOwnExceptionWithoutDisturbingTheOuterOne()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Sub Main()
              Try
               PrintLine("OUTER-TRY")
               Try
                Throw New Exception("inner")
               Catch ex As Exception
                PrintLine("INNER-CATCH")
               End Try
               PrintLine("OUTER-CONT")
              Catch ex As Exception
               PrintLine("OUTER-CATCH")
              End Try
              PrintLine("AFTER")
             End Sub
            End Module
            """), Is.EqualTo("OUTER-TRY\nINNER-CATCH\nOUTER-CONT\nAFTER\n"),
            "the inner Try owns its arms; collecting them into the enclosing region too emits "
            + "them twice and ilasm rejects the file with 'Duplicate label'. OUTER-CATCH means "
            + "the inner handler did not contain the exception.");
    }

    [Test]
    public void TwoTryStatementsInOneMethod_DoNotCollide()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Sub Main()
              Try
               Throw New Exception("a")
              Catch ex As Exception
               PrintLine("C1")
              End Try
              Try
               Throw New Exception("b")
              Catch ex As Exception
               PrintLine("C2")
              End Try
              PrintLine("AFTER")
             End Sub
            End Module
            """), Is.EqualTo("C1\nC2\nAFTER\n"),
            "the region's exit label used to be the fixed string 'EndTry', emitted once per "
            + "Try — two in a method produced a duplicate label.");
    }

    [Test]
    public void ATryInsideALoop_CatchesOncePerIteration()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Sub Main()
              Dim i As Integer
              For i = 1 To 3
               Try
                If i = 2 Then
                 Throw New Exception("two")
                End If
                PrintLine("OK" & CStr(i))
               Catch ex As Exception
                PrintLine("ERR" & CStr(i))
               End Try
              Next
              PrintLine("AFTER")
             End Sub
            End Module
            """), Is.EqualTo("OK1\nERR2\nOK3\nAFTER\n"),
            "a multi-block try body inside a loop: the If's blocks must be emitted INSIDE the "
            + "protected region, and the loop must resume normally after each handler.");
    }

    [Test]
    public void ABareThrow_RethrowsToTheCaller()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Sub Inner()
              Try
               Throw New Exception("deep")
              Catch ex As Exception
               PrintLine("INNER-SAW")
               Throw
              End Try
             End Sub
             Sub Main()
              Try
               Inner()
              Catch ex As Exception
               PrintLine("OUTER-CAUGHT")
              End Try
              PrintLine("AFTER")
             End Sub
            End Module
            """), Is.EqualTo("INNER-SAW\nOUTER-CAUGHT\nAFTER\n"),
            "a bare Throw is `rethrow`, valid only inside a catch handler. A missing "
            + "OUTER-CAUGHT means the rethrow was dropped and the exception died in Inner.");
    }

    [Test]
    public void AUserDefinedExceptionType_IsCaughtByItsOwnClause()
    {
        Assert.That(RunExpectingSuccess("""
            Class MyError
             Inherits Exception
            End Class

            Module M
             Sub Main()
              Try
               Throw New MyError()
              Catch ex As MyError
               PrintLine("MYERR")
              Catch ex As Exception
               PrintLine("GENERAL")
              End Try
              PrintLine("AFTER")
             End Sub
            End Module
            """), Is.EqualTo("MYERR\nAFTER\n"),
            "the clause type, the class's `extends`, and its base-constructor call must all name "
            + "the type the same way. Spelling a user type as '[mscorlib]System.MyError' — or "
            + "leaving `extends Exception` unqualified — fails to assemble.");
    }

    /// <summary>
    /// The exception surface is a narrow recorded table, on the same principle as
    /// <see cref="ACollectionMemberOutsideTheTable_IsRefusedNotGuessed"/>: a guessed member
    /// reference assembles cleanly and fails at run time, so it is refused at generation time.
    /// </summary>
    [Test]
    public void AnExceptionMemberOutsideTheTable_IsRefusedNotGuessed()
    {
        var run = Run("""
            Module M
             Sub Main()
              Try
               Throw New Exception("x")
              Catch ex As Exception
               PrintLine(ex.HelpLink)
              End Try
             End Sub
            End Module
            """);

        Assert.Multiple(() =>
        {
            Assert.That(run.Outcome, Is.EqualTo(MsilOutcome.GenerateFailed),
                "refusal must come BEFORE any IL exists: " + run.Report);
            Assert.That(run.Detail,
                Does.Contain("HelpLink").And.Contain("supported exception surface"),
                "and it must name the member and say how to widen the set: " + run.Detail);
        });
    }

    // ====================================================================================
    // Module-level variables.
    //
    // ⛔ The backend did not read IRModule.GlobalVariables AT ALL — the identifier did not
    // appear in MSILBackend.cs. Every one of these programs either died with
    // InvalidProgramException or, worse, printed the right answer through a stack accident.
    // ====================================================================================

    /// <summary>
    /// ⛔ The initializer is NOWHERE in the function IR. Main's instruction list for this program
    /// is just <c>t0 = call CStr(@n)</c> — nothing in any method body assigns the 7. Emitting the
    /// field without a type initializer is not a build error, it is a program that prints 0.
    /// </summary>
    [Test]
    public void AModuleLevelVariable_HoldsItsInitializer()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Dim n As Integer = 7
             Sub Main()
              PrintLine(CStr(n))
             End Sub
            End Module
            """), Is.EqualTo("7\n"),
            "a module-level Dim with an initializer must carry that value into Main");
    }

    /// <summary>
    /// The point of a module-level variable: one storage location every procedure sees. A
    /// per-method local would print 1, not 3.
    /// </summary>
    [Test]
    public void AModuleLevelVariable_IsSharedAcrossProcedures()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Dim counter As Integer = 0
             Sub Bump()
              counter = counter + 1
             End Sub
             Sub Main()
              Bump()
              Bump()
              Bump()
              PrintLine(CStr(counter))
             End Sub
            End Module
            """), Is.EqualTo("3\n"),
            "every procedure must read and write the SAME location");
    }

    /// <summary>
    /// A sized array global needs storage created for it in the type initializer, for exactly the
    /// reason a sized array local and a sized array field do: left bare it is a null reference and
    /// the first <c>g(1) = …</c> throws.
    /// </summary>
    [Test]
    public void AModuleLevelArray_HasStorage()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Dim g(3) As Integer
             Sub Main()
              g(1) = 5
              PrintLine(CStr(g(1)))
             End Sub
            End Module
            """), Is.EqualTo("5\n"));
    }

    /// <summary>
    /// ⚠ The counter-case: a global with no initializer needs NO type initializer, because the CLR
    /// zeroes every static field and zero/null/false is exactly what an uninitialized BasicLang
    /// variable holds. This pins that the "do nothing" path is right rather than merely untested.
    /// </summary>
    [Test]
    public void AModuleLevelVariableWithNoInitializer_StartsAtItsTypesZero()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Dim s As String
             Dim k As Integer
             Dim f As Boolean
             Sub Main()
              PrintLine("[" & s & "]")
              PrintLine(CStr(k))
              PrintLine(CStr(f))
             End Sub
            End Module
            """), Is.EqualTo("[]\n0\nFalse\n"));
    }

    /// <summary>
    /// ⛔ Resolution order, and the reason the global arm goes LAST in <c>EmitLoadLocal</c>. A
    /// local of the same name must win inside its own procedure while the global stays intact for
    /// everyone else. Getting this backwards prints 7 then 7 — both readings wrong, and neither
    /// one crashes.
    /// </summary>
    [Test]
    public void ALocalShadowsAModuleLevelVariableOfTheSameName()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Dim n As Integer = 7
             Sub Main()
              Dim n As Integer = 99
              PrintLine(CStr(n))
              Show()
             End Sub
             Sub Show()
              PrintLine(CStr(n))
             End Sub
            End Module
            """), Is.EqualTo("99\n7\n"),
            "the local shadows inside Main; Show still sees the module-level 7");
    }

    /// <summary>
    /// ⛔ This is what forces <c>assembly</c> rather than <c>private</c> on the field. IL's
    /// <c>private</c> means "the declaring type only", and the module class is NOT the class this
    /// method lives on — so a faithful translation of BasicLang's Private is refused by the CLR
    /// with FieldAccessException at the first read.
    /// </summary>
    [Test]
    public void AModuleLevelVariable_IsReadableFromAUserClassMethod()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Dim tally As Integer = 10
             Class Box
              Public Function Peek() As Integer
               Return tally
              End Function
             End Class
             Sub Main()
              Dim b As New Box()
              PrintLine(CStr(b.Peek()))
             End Sub
            End Module
            """), Is.EqualTo("10\n"));
    }

    /// <summary>
    /// ⛔ <c>Dim d As Double = 7</c> carries an int32 constant into a float64 field, and IL does
    /// not coerce on <c>stsfld</c>. Measured: nothing rejects the mismatch — ilasm assembles it
    /// silently and the JIT runs it, copying the int32's BIT PATTERN into the low half of the
    /// slot, so <c>d</c> becomes 3.5E-323 and this program prints <c>1.5</c>. A silent wrong
    /// answer, which is why the widening is emitted from the field's declared type instead of
    /// being left to a verifier that never objects.
    /// </summary>
    [Test]
    public void AnIntegerLiteralInitializingADouble_IsWidened_NotBitCopied()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Dim d As Double = 7
             Dim e As Double = 1.5
             Sub Main()
              PrintLine(CStr(d + e))
             End Sub
            End Module
            """), Is.EqualTo("8.5\n"));
    }

    /// <summary>
    /// Globals across a protected region: <c>stsfld</c> inside a <c>.try</c>, a <c>catch</c> and a
    /// <c>finally</c> must each land, and none of them may unbalance the stack that <c>leave</c>
    /// and <c>endfinally</c> depend on.
    /// </summary>
    [Test]
    public void AModuleLevelVariable_IsWritableFromEveryArmOfATry()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Dim log As String = "start"
             Sub Main()
              Try
               log = log & "-try"
               Throw New Exception("boom")
              Catch ex As Exception
               log = log & "-catch"
              Finally
               log = log & "-fin"
              End Try
              PrintLine(log)
             End Sub
            End Module
            """), Is.EqualTo("start-try-catch-fin\n"));
    }

    /// <summary>
    /// ⛔ IL TEXT, because this property is INVISIBLE at run time. <c>beforefieldinit</c> lets the
    /// CLR run the type initializer at any point at or before the first static-field access;
    /// without it the first access is the guaranteed trigger. For the IL this backend emits both
    /// produce the same answers today, so no round-trip test can hold the line — exactly as the C#
    /// compiler drops the flag for a class with a static constructor, it is dropped here, and only
    /// reading the IL can say so.
    /// </summary>
    [Test]
    public void TheModuleClass_DropsBeforeFieldInit_WhenItHasATypeInitializer()
    {
        var withInitializer = CompileToIl("""
            Module M
             Dim n As Integer = 7
             Sub Main()
              PrintLine(CStr(n))
             End Sub
            End Module
            """);

        var withoutInitializer = CompileToIl("""
            Module M
             Dim n As Integer
             Sub Main()
              PrintLine(CStr(n))
             End Sub
            End Module
            """);

        Assert.Multiple(() =>
        {
            Assert.That(withInitializer, Does.Contain(".cctor"),
                "an initialized global needs a type initializer: " + withInitializer);
            Assert.That(ModuleClassLine(withInitializer), Does.Not.Contain("beforefieldinit"),
                "and the class carrying it must not be beforefieldinit");

            Assert.That(withoutInitializer, Does.Not.Contain(".cctor"),
                "a zero-valued global needs no type initializer at all — the CLR already zeroes "
                + "the field, so emitting one would be dead IL: " + withoutInitializer);
            Assert.That(ModuleClassLine(withoutInitializer), Does.Contain("beforefieldinit"),
                "and with no initializer the relaxed flag stays");
        });
    }

    /// <summary>
    /// ⛔ IL TEXT for the same reason: <c>Public</c> and <c>Private</c> globals both READ and WRITE
    /// identically from inside the assembly, so no program this fixture can run distinguishes
    /// <c>public</c> from <c>assembly</c> on the field. The distinction only becomes observable to
    /// a SEPARATE assembly referencing this one, which nothing here does.
    /// </summary>
    [Test]
    public void APublicModuleLevelVariable_IsEmittedPublic_AndAPrivateOneAssembly()
    {
        var il = CompileToIl("""
            Module M
             Public Exported As Integer = 1
             Dim Internal As Integer = 2
             Sub Main()
              PrintLine(CStr(Exported + Internal))
             End Sub
            End Module
            """);

        Assert.Multiple(() =>
        {
            Assert.That(il, Does.Contain(".field public static int32 'Exported'"), il);
            Assert.That(il, Does.Contain(".field assembly static int32 'Internal'"),
                "Private must widen to assembly, not private — see "
                + nameof(AModuleLevelVariable_IsReadableFromAUserClassMethod) + ": " + il);
        });
    }

    /// <summary>
    /// ⛔ The type initializer is a METHOD, and it is emitted LAST — after every module procedure.
    /// It therefore inherits whatever local, parameter and temporary tables the previous procedure
    /// left behind unless they are cleared first, and a global initializer CAN name another global
    /// (<c>Dim b As Integer = K</c> emits <c>ldsfld</c>). Measured with the reset removed: the
    /// decoy's <c>Dim K</c> is still in the slot table, so the .cctor resolves the global K to
    /// <c>ldloc.0</c> — a local slot the .cctor does not declare — and the program dies with
    /// TypeInitializationException wrapping InvalidProgramException.
    ///
    /// <para>The decoy Sub has to come LAST in the module for this: it is the procedure whose
    /// tables the .cctor would inherit. Reorder it above Main and the shape stops discriminating,
    /// which is why the name is what it is.</para>
    /// </summary>
    [Test]
    public void TheTypeInitializer_DoesNotInheritTheLastProceduresLocalSlots()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Const K As Integer = 5
             Dim b As Integer = K
             Sub Main()
              Decoy()
              PrintLine(CStr(b))
             End Sub
             Sub Decoy()
              Dim K As Integer = 111
              Dim pad As Integer = K
             End Sub
            End Module
            """), Is.EqualTo("5\n"),
            "the global K, not the decoy's local K");
    }

    /// <summary>
    /// The rank &gt; 1 refusal reaches module level too. This is a NEW site for it — the check now
    /// runs from the module class's own emission, not just from a method prologue or a constructor
    /// — and it has to refuse there for the same reason: a rank-2 declaration collapses to a
    /// rank-1 IL type, so allocating one would turn a NullReferenceException into a silent wrong
    /// answer at the first <c>grid(i, j)</c>.
    /// </summary>
    [Test]
    public void AMultiDimensionalModuleLevelArray_IsRefusedNotAllocated()
    {
        var run = Run("""
            Module M
             Dim grid(2, 2) As Integer
             Sub Main()
              PrintLine("X")
             End Sub
            End Module
            """);

        Assert.Multiple(() =>
        {
            Assert.That(run.Outcome, Is.EqualTo(MsilOutcome.GenerateFailed),
                "refusal must come BEFORE any IL exists: " + run.Report);
            Assert.That(run.Detail, Does.Contain("2-dimensional").And.Contain("wrong answer"),
                "and it must say what allocating it would cost: " + run.Detail);
        });
    }

    /// <summary>The <c>.class</c> line of the module class, whose flags several tests read.</summary>
    private static string ModuleClassLine(string il) => ClassLine(il, "MsilProbe");

    /// <summary>The <c>.class</c> line declaring <paramref name="name"/>, for flag assertions.</summary>
    /// <summary>
    /// ⚠ The class name is SINGLE-QUOTED in the emitted IL — every user-chosen name is, so that a
    /// name which happens to be an IL keyword still assembles (<c>MSILCodeGenerator.SanitizeName</c>).
    /// </summary>
    private static string ClassLine(string il, string name) =>
        il.Split('\n').First(line => line.StartsWith(".class") && line.TrimEnd().EndsWith(" '" + name + "'"));
}
