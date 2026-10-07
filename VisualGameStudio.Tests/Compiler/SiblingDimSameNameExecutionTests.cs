using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BasicLang.Compiler.CodeGen.LLVM;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #229, RUN. In VB, `If c Then : Dim x As Integer = 1 ... Else : Dim x As String = "a" ... End If` declares TWO variables, as do two sibling loops' `Dim x`, two Select Case arms', and a lambda's own
//  `Dim y` beside its creator's loop `Dim y`. BasicLang made every such pair ONE function-level variable: the IR is flat and every backend identifies a local by NAME. C# and JavaScript gave the second block the
//  first one's value (or its type: CS0029), C++ failed on a redefinition, ilasm on a duplicate local slot, and ADR-0014's one-declaration rule left both out of per-iteration identity (E16 printed 20|20|20|20
//  on every backend, vbc 1|2|10|20; E20 printed 50|2|2 on C# and JavaScript and was REFUSED by MSIL, vbc 50|1|2).
//
//  Now a local `Dim` whose spelling (ignoring case) is already a local it would MEET - its own function, a function enclosing it, or a lambda created inside it; two sibling lambdas never meet - is a variable of
//  its own, under the IR name `{spelling}_{k}`: the first k that is no name the program can name, no module-level IR name and no local it meets. The earlier declaration keeps its spelling, so a procedure with no
//  such pair is byte-identical (`SiblingDimSameNameTextTests`, fast, below). A reference reaches its own declaration's variable; a counted `For` that drives or reuses a renamed local writes its increment back
//  under the local's own name. ADR-0014 Amendment A-229.
//
//  ⚠ OWNER RULING (2026-10-07), on ADR-0013's rejected row "Uniquify IR names per declaration": ACCEPT THE RENAME. The second variable shows as `x_1` in the generated source and in the debugger. Only a SECOND
//  declaration of a spelling is renamed, which is why this narrows that row rather than reversing it. A follow-up maps the name back to `x` in the debugger.
//
//  ORACLE: vbc, via `S/t136/tools/vbv2.py`, over each program wrapped in a VB Module (S/t229/tw-probes: r1-r11, each a join of the implementer's own probes S/t229/probes p01-p20, one Sub per shape and a
//  `Main` that calls them). Every expected string is vbc's output (the `.exp` beside each `.bas`), never a backend's; a program prints one line per shape so ONE row can carry several.
//
//  ENTRY POINTS: the spawned CLI (standard passes), the CLI with `--optimize` and `BasicCompiler.CompileProjectFiles` (aggressive) - `TempExec.AssertMatchesInEveryEntryPoint` - on C#, C++, JavaScript and MSIL.
//  ⛔ Every row is `HangSafe` (the C# cells run in a child process with a time limit, `CSharpProcessRunner`, #256): the counted For of r8 HANGS when the fix is broken (mutant M3), and the other rows loop.
//  A backend whose tool is missing (a C++ compiler, Node, ilasm) SKIPS its cells, never fails them; the test is ignored only when no cell could run.
//  ⚠ Named "...ExecutionTests" and runs JavaScript under Node: it is in `JsExecutionTierRosterTests`' roster (132 with this one). `SiblingDimSameNameTextTests` (in process, no Node, no
//  [Category("Integration")]) is NOT.
//
//  MUTANTS (the fix plus ONE change, built from a plain source copy; recipes S/t229/tools/mut.py + twmut.sh: its BasicLang.dll swapped into a copy of the test output, this file run there).
//  Each is killed by the cases below, MEASURED (every other case stays green; nothing hangs the test host):
//    M1 a reference no longer looks its declaration up (`_localsByDeclaration` dropped: the version stack only)  -> EQUIVALENT on every program vbc accepts: the stack's top is the declaration's own variable
//                                                    unless a LATER, hiding declaration was pushed, which vbc refuses (BC30616, #231's shape, p15). No vbc-answered case can see it; not pinned.
//    M2 a Dim meets only its own function (an enclosing function's and a created lambda's locals ignored)       -> `ALambdasOwnLocal_...` (r6) alone: C# and JavaScript print 50|2|2 for 50|1|2, and MSIL refuses E20 and
//                                                    E12_later_sibling at ClosureLowering's N9 backstop (C++ still lowers them: the refusal is MSIL's)
//    M3 a counted For writes a renamed local back under the spelling                                            -> `ACountedFor_...` (r8) alone: HANGS on all four backends (the For never reaches its end). The C# cells are cut by
//                                                    CSharpProcessRunner ("hung"), the others by their own 30 s limits. ⛔ C++ was NOT cut until #229: `CppCompile.RunToCompletion` read the program's
//                                                    stdout BEFORE waiting, so its timeout was unreachable and this mutant froze the host. It reads both streams asynchronously now.
//    M4 the minted name is checked against the procedure's own locals only                                       -> `ARenameSteps_PastAFunctionAndLocals...` (r9: C# CS0149, C++ clang, JavaScript TypeError; MSIL happens to
//                                                    run it) and `ARenameSteps_PastAClassField...` (r10: every backend prints the field where the new local is read, with no error anywhere)
//    M5 an array slot keeps the written spelling (`x_addr`, not `x_1_addr`)                                      -> `ALambdaOverSiblingSizedArrays_...` (r7) alone: 2|2|10|20 for 1|2|10|20 on every backend
//
//  ⛔ KNOWN GAPS - each is the same before and after #229 and NOT its to fix; listed with NO test (asserting one would pin a defect):
//    p13  a `For x` with NO `As` after two sibling `Dim x` of different types reuses the second's storage: a compile failure on C# and C++ (CS0029 / a string compared), a NullReferenceException on MSIL, and it
//         runs on JavaScript. It is a For declaration, not a Dim (follow-up filed).
//    p15  #231's hiding shape (`Dim x` in an If under a function-level `Dim x`): vbc refuses it (BC30616), BasicLang accepts it and a reference reaches its own declaration (2|1, was 2|2). #231 is next.
//    A local `Dim` that shares a spelling with a PARAMETER, a For Each variable, a Catch variable or a `For x` control variable keeps the function-level rule (one name, two declarations of different kinds).
//    The debugger shows `x_1` for the second variable (owner-accepted; follow-up filed).
// ================================================================================================

/// <summary>#229 RUN: two same-named `Dim`s in sibling blocks are two variables on C#, C++, JavaScript and MSIL, as vbc runs them.</summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the spawned CLI, the C# child runs, the C++ compiler, ilasm and Node share the machine
public class SiblingDimSameNameExecutionTests
{
    /// <summary>
    /// r1 - an If and its Else each declare `x`: the same type with different initial values (p01), DIFFERENT types, Integer and String (p02: C# was CS0029, MSIL a duplicate slot, C++ a redefinition), and
    /// the same name in a different CASE, `x` and `X` (p16: one variable to VB's case-insensitive lookup, two declarations here). vbc prints `1 2`, `8 ab`, `6 up!`.
    /// </summary>
    private static readonly TempProbe IfElsePairs = P("r1_ifelse_pairs", """
        Sub SameType(c As Boolean)
            If c Then
                Dim x As Integer = 1
                Console.WriteLine(x)
            Else
                Dim x As Integer = 2
                Console.WriteLine(x)
            End If
        End Sub

        Sub DifferentTypes(c As Boolean)
            If c Then
                Dim x As Integer = 7
                Console.WriteLine(x + 1)
            Else
                Dim x As String = "a"
                Console.WriteLine(x & "b")
            End If
        End Sub

        Sub DifferentCase(c As Boolean)
            If c Then
                Dim x As Integer = 3
                Console.WriteLine(x * 2)
            Else
                Dim X As String = "up"
                Console.WriteLine(X & "!")
            End If
        End Sub

        Sub Main()
            SameType(True)
            SameType(False)
            DifferentTypes(True)
            DifferentTypes(False)
            DifferentCase(True)
            DifferentCase(False)
        End Sub
        """, "1\n2\n8\nab\n6\nup!");

    /// <summary>
    /// r2 - two sibling `For` loops each declare `x` with no initializer (p03: VB keeps a body Dim's value across iterations of ITS loop, and the second loop's `x` starts from zero, not the first's 6), and the
    /// arms of a Select Case each declare `s` as a String, an Integer and a Double (p04). vbc prints `1 3 6`, `10 20`, then `one`, `44`, `1.5`.
    /// </summary>
    private static readonly TempProbe LoopsAndCaseArms = P("r2_loops_and_cases", """
        Sub SiblingLoops()
            For i As Integer = 1 To 3
                Dim x As Integer
                x = x + i
                Console.WriteLine(x)
            Next
            For j As Integer = 1 To 2
                Dim x As Integer
                x = x + 10
                Console.WriteLine(x)
            Next
        End Sub

        Sub Pick(k As Integer)
            Select Case k
                Case 1
                    Dim s As String = "one"
                    Console.WriteLine(s)
                Case 2
                    Dim s As Integer = 22
                    Console.WriteLine(s * 2)
                Case Else
                    Dim s As Double = 1.5
                    Console.WriteLine(s)
            End Select
        End Sub

        Sub Main()
            SiblingLoops()
            Pick(1)
            Pick(2)
            Pick(3)
        End Sub
        """, "1\n3\n6\n10\n20\none\n44\n1.5");

    /// <summary>
    /// r3 - an UNASSIGNED second `Dim x As Integer` reads 0, not the first block's 42 (p05: before the fix both were one variable, which still held 42), and three `x` in nested and sibling blocks stay
    /// apart (p09). vbc prints `42 0`, then `1 2 0 100`.
    /// </summary>
    private static readonly TempProbe UnassignedAndNested = P("r3_unassigned_and_nested", """
        Sub UnassignedSecond()
            For i As Integer = 1 To 2
                If i = 1 Then
                    Dim x As Integer = 42
                    Console.WriteLine(x)
                Else
                    Dim x As Integer
                    Console.WriteLine(x)
                End If
            Next
        End Sub

        Sub Nested()
            For i As Integer = 1 To 2
                If i = 1 Then
                    For k As Integer = 1 To 2
                        Dim x As Integer
                        x = x + 1
                        Console.WriteLine(x)
                    Next
                    If i > 0 Then
                        Dim x As Integer
                        Console.WriteLine(x)
                    End If
                Else
                    Dim x As Integer = 100
                    Console.WriteLine(x)
                End If
            Next
        End Sub

        Sub Main()
            UnassignedSecond()
            Nested()
        End Sub
        """, "42\n0\n1\n2\n0\n100");

    /// <summary>
    /// r4 - two sibling loops each declare a sized array `Dim a(2)` (p15 of #228, whose fix left it refused on C++ and MSIL): each is its own zeroed array, the second loop's `a(0)` is 0 and not the first
    /// loop's last value. vbc prints `1 2`, then `10 20`.
    /// </summary>
    private static readonly TempProbe SiblingSizedArrays = P("r4_sibling_arrays", """
        Sub Main()
            For i As Integer = 1 To 2
                Dim a(2) As Integer
                a(0) = a(0) + i
                Console.WriteLine(a(0))
            Next
            For j As Integer = 1 To 2
                Dim a(2) As Integer
                a(1) = a(1) + 10 * j
                Console.WriteLine(a(0) + a(1))
            Next
        End Sub
        """, "1\n2\n10\n20");

    /// <summary>
    /// r5 - a lambda captures each of two sibling `Dim x` (E16: two loops; p10: an If and its Else inside one loop, #242's shape). Each closure keeps ITS variable and iteration's value: vbc prints `1 2 10 20`,
    /// then `5 6`. The C++ root is lowered, an environment per variable.
    /// </summary>
    private static readonly TempProbe LambdaCapturingEach = P("r5_lambda_each", """
        Sub SiblingLoopsCaptured()
            Dim fs As New List(Of Func(Of Integer))()
            For i As Integer = 1 To 2
                Dim x As Integer = i
                fs.Add(Function() x)
            Next
            For j As Integer = 1 To 2
                Dim x As Integer = j * 10
                fs.Add(Function() x)
            Next
            For k As Integer = 0 To fs.Count - 1
                Dim f As Func(Of Integer) = fs(k)
                Console.WriteLine(f())
            Next
        End Sub

        Sub IfElseCaptured()
            Dim fs As New List(Of Func(Of Integer))()
            For i As Integer = 1 To 2
                If i = 1 Then
                    Dim x As Integer = 5
                    fs.Add(Function() x)
                Else
                    Dim x As Integer = 6
                    fs.Add(Function() x)
                End If
            Next
            For k As Integer = 0 To fs.Count - 1
                Dim f As Func(Of Integer) = fs(k)
                Console.WriteLine(f())
            Next
        End Sub

        Sub Main()
            SiblingLoopsCaptured()
            IfElseCaptured()
        End Sub
        """, "1\n2\n10\n20\n5\n6");

    /// <summary>
    /// r6 - the M2 killer. A lambda's OWN `Dim y` beside its creator's captured loop `Dim y` (E20: C# and JavaScript printed 50|2|2, MSIL refused the program at its N9 backstop, C++ fell back to by-copy),
    /// and a lambda's own `Dim x` beside a LATER sibling block's `Dim x` (E12_later_sibling: MSIL refused it too). The lambda's local is a variable of its own, so the loop's `y` is per-iteration and the
    /// lambda reads its 50 (and its 3). vbc prints `50 1 2`, then `8`.
    /// </summary>
    private static readonly TempProbe LambdaOwnLocal = P("r6_lambda_own_local", """
        Sub LambdaOwnBesideLoopDim()
            Dim fs As New List(Of Func(Of Integer))()
            For i As Integer = 1 To 2
                Dim y As Integer = i
                fs.Add(Function() y)
            Next
            Dim h As Func(Of Integer) = Function()
                    Dim y As Integer = 50
                    Console.Write("")
                    Return y
                End Function
            Console.WriteLine(h())
            For k As Integer = 0 To fs.Count - 1
                Dim f As Func(Of Integer) = fs(k)
                Console.WriteLine(f())
            Next
        End Sub

        Sub LambdaOwnBesideLaterSibling()
            Dim f As Func(Of Integer) = Nothing
            If True Then
                f = Function()
                        Dim x As Integer = 3
                        Return x
                    End Function
            End If
            If True Then
                Dim x As Integer = 5
                Console.WriteLine(f() + x)
            End If
        End Sub

        Sub Main()
            LambdaOwnBesideLoopDim()
            LambdaOwnBesideLaterSibling()
        End Sub
        """, "50\n1\n2\n8");

    /// <summary>
    /// r7 - the M5 killer (p20): a lambda captures each of two sibling sized arrays `a`. The second array's allocation slot is named after ITS variable (`a_1_addr`); named after the spelling it is the
    /// first array's slot, the lambda reads the wrong storage and every backend prints `2 2 10 20` for vbc's `1 2 10 20`.
    /// </summary>
    private static readonly TempProbe LambdaOverArrays = P("r7_lambda_over_arrays", """
        Sub Main()
            Dim fs As New List(Of Func(Of Integer))()
            For i As Integer = 1 To 2
                Dim a(1) As Integer
                a(0) = a(0) + i
                fs.Add(Function() a(0))
            Next
            For j As Integer = 1 To 2
                Dim a(1) As Integer
                a(1) = a(1) + 10 * j
                fs.Add(Function() a(0) + a(1))
            Next
            For k As Integer = 0 To fs.Count - 1
                Dim f As Func(Of Integer) = fs(k)
                Console.WriteLine(f())
            Next
        End Sub
        """, "1\n2\n10\n20");

    /// <summary>
    /// r8 - the M3 killer (p14). A counted `For x = 1 To 3` drives the SECOND `Dim x` of a block while a first one is already declared. The increment must be written back to THAT variable: written to the
    /// spelling it increments the first `x`, the loop never reaches its end and the program HANGS (every backend, measured). vbc prints `5`, then `4` (the control variable's value after the loop).
    /// </summary>
    private static readonly TempProbe ForDrivesTheSecond = P("r8_for_drives", """
        Sub Main()
            If 1 > 0 Then
                Dim x As Integer = 5
                Console.WriteLine(x)
            End If
            If 1 > 0 Then
                Dim x As Integer
                For x = 1 To 3
                Next
                Console.WriteLine(x)
            End If
        End Sub
        """, "5\n4");

    /// <summary>
    /// r9 - the M4 killer (p18): the program ALREADY uses the names the rename would mint. A Function named `x_1` and a local `x_2` sit beside two sibling `Dim x`, and a third block declares `x_3`: the
    /// second `x` must step past all of them (`x_4`), or a call `x_1()` becomes a read of a local (C# CS0149, C++ "called object type 'int'", JavaScript `TypeError: x_1 is not a function`). vbc prints
    /// `1 1101 7`.
    /// </summary>
    private static readonly TempProbe NamesAlreadyTaken = P("r9_names_taken", """
        Function x_1() As Integer
            Return 1000
        End Function

        Sub Main()
            Dim x_2 As Integer = 99
            If x_2 > 0 Then
                Dim x As Integer = 1
                Console.WriteLine(x)
            End If
            If x_2 > 0 Then
                Dim x As Integer = 2
                Console.WriteLine(x + x_1() + x_2)
            End If
            If x_2 > 0 Then
                Dim x_3 As Integer = 7
                Console.WriteLine(x_3)
            End If
        End Sub
        """, "1\n1101\n7");

    /// <summary>
    /// r10 - the M4 killer's other half (p19): the name the rename would mint is a CLASS FIELD, `x_1`, read in the method beside the pair of `Dim x`. A name that is no local of the procedure is still a name the
    /// program can name: the second `x` must not become the field (every backend then prints the field's 50 where the local is read, and the program compiles, so nothing else sees it). vbc prints `1 52`.
    /// </summary>
    private static readonly TempProbe ClassFieldTaken = P("r10_class_field", """
        Class C
            Private x_1 As Integer = 50

            Public Sub Run()
                If x_1 > 0 Then
                    Dim x As Integer = 1
                    Console.WriteLine(x)
                End If
                If x_1 > 0 Then
                    Dim x As Integer = 2
                    Console.WriteLine(x + x_1)
                End If
            End Sub
        End Class

        Sub Main()
            Dim c As New C()
            c.Run()
        End Sub
        """, "1\n52");

    /// <summary>
    /// r11 - CONTROLS that must not move: ONE `Dim x` reused by a loop and captured on each pass is ADR-0014's per-iteration variable (vbc `1 3 6`), two blocks with DISTINCT names stay distinct (`1 b`), and
    /// two SIBLING lambdas that each declare their own `y` never meet (p17: `1 two`).
    /// </summary>
    private static readonly TempProbe Controls = P("r11_controls", """
        Sub OneDimReusedInALoop()
            Dim fs As New List(Of Func(Of Integer))()
            For i As Integer = 1 To 3
                Dim x As Integer
                x = x + i
                fs.Add(Function() x)
            Next
            For k As Integer = 0 To fs.Count - 1
                Dim f As Func(Of Integer) = fs(k)
                Console.WriteLine(f())
            Next
        End Sub

        Sub DistinctNames()
            For i As Integer = 1 To 2
                If i = 1 Then
                    Dim a As Integer = 1
                    Console.WriteLine(a)
                Else
                    Dim b As String = "b"
                    Console.WriteLine(b)
                End If
            Next
        End Sub

        Sub SiblingLambdasSameLocal()
            Dim f As Func(Of Integer) = Function()
                    Dim y As Integer = 1
                    Return y
                End Function
            Dim g As Func(Of String) = Function()
                    Dim y As String = "two"
                    Return y
                End Function
            Console.WriteLine(f())
            Console.WriteLine(g())
        End Sub

        Sub Main()
            OneDimReusedInALoop()
            DistinctNames()
            SiblingLambdasSameLocal()
        End Sub
        """, "1\n3\n6\n1\nb\n1\ntwo");

    private static TempProbe P(string id, string source, string vb) => new(id, source, vb, Bk.All, HangSafe: true);

    /// <summary>
    /// Each probe on every backend it runs on, each through every entry point. A failing cell is collected, not thrown, so every other one still reports, and the failure names the probe, the backend and the
    /// entry point. A backend whose tool is missing skips its cells. (A copy of the helper `SizedArrayDimInLoopExecutionTests` keeps: each fixture owns its own.)
    /// </summary>
    private static void AssertRows(params TempProbe[] probes)
    {
        var failures = new List<string>();
        int ran = 0, skipped = 0;
        foreach (var probe in probes)
        {
            foreach (var backend in TempExec.Backends(probe.Agrees))
            {
                try
                {
                    TempExec.AssertMatchesInEveryEntryPoint(backend, probe.Source, probe.Vb, probe.Id, probe.HangSafe);
                    ran++;
                }
                catch (IgnoreException)
                {
                    skipped++;
                }
                catch (AssertionException ex)
                {
                    ran++;
                    failures.Add(ex.Message);
                }
            }
        }

        Assert.That(failures, Is.Empty, string.Join("\n", failures));
        if (ran == 0) Assert.Ignore($"no execution tool on this machine ({skipped} cells skipped).");
    }

    /// <summary>The headline. An If and its Else declare their own `x`: the same type, different types, and a different case. Before #229 C# was CS0029 on the String, MSIL a duplicate slot, C++ a redefinition.</summary>
    [Test]
    public void AnIfAndItsElse_DeclareTheirOwnX_ForTheSameType_DifferentTypes_AndDifferentCase()
        => AssertRows(IfElsePairs);

    /// <summary>Two sibling loops' `Dim x` and a Select Case's three arms' `Dim s` are separate variables of their own types: the second loop starts from zero, an arm reads its own value.</summary>
    [Test]
    public void SiblingLoops_AndSelectCaseArms_EachDeclareTheirOwnVariable()
        => AssertRows(LoopsAndCaseArms);

    /// <summary>An unassigned second `Dim x As Integer` reads 0, not the first block's value; three `x` in nested and sibling blocks stay apart.</summary>
    [Test]
    public void AnUnassignedSecondDim_ReadsZero_AndNestedBlocksStayApart()
        => AssertRows(UnassignedAndNested);

    /// <summary>Two sibling sized arrays are two zeroed arrays (#228's p15: refused on C++ and MSIL before).</summary>
    [Test]
    public void SiblingSizedArrays_AreTwoZeroedArrays()
        => AssertRows(SiblingSizedArrays);

    /// <summary>A lambda capturing each of two sibling Dims (two loops; an If and its Else in a loop) keeps its own variable and its iteration's value (E16 was 20|20|20|20).</summary>
    [Test]
    public void ALambdaCapturingEachOfTwoSiblingDims_KeepsItsOwnVariable()
        => AssertRows(LambdaCapturingEach);

    /// <summary>A lambda's own local beside its creator's loop Dim, and beside a later sibling block's Dim, are two variables (E20 was 50|2|2 on C# / JavaScript and refused by MSIL). Kills M2.</summary>
    [Test]
    public void ALambdasOwnLocal_BesideTheCreatorsLoopDimAndALaterSibling_IsAVariableOfItsOwn()
        => AssertRows(LambdaOwnLocal);

    /// <summary>A lambda capturing each of two sibling sized arrays reads that array's own storage. Kills M5.</summary>
    [Test]
    public void ALambdaOverSiblingSizedArrays_ReadsEachArraysOwnStorage()
        => AssertRows(LambdaOverArrays);

    /// <summary>A counted For driving the second of two same-named Dims increments THAT variable and ends (a hang before the For's write-back followed the rename). Kills M3.</summary>
    [Test]
    public void ACountedFor_DrivingTheSecondOfTwoSameNamedDims_IncrementsThatDimAndEnds()
        => AssertRows(ForDrivesTheSecond);

    /// <summary>The rename steps past a Function `x_1`, a local `x_2` and a Dim `x_3` the program already uses. Kills M4.</summary>
    [Test]
    public void ARenameSteps_PastAFunctionAndLocalsTheProgramAlreadyNames()
        => AssertRows(NamesAlreadyTaken);

    /// <summary>The rename steps past a class FIELD `x_1`, which is no local of the procedure. Kills M4 (the one every backend prints wrong without a compile error).</summary>
    [Test]
    public void ARenameSteps_PastAClassFieldTheProgramAlreadyNames()
        => AssertRows(ClassFieldTaken);

    /// <summary>The CONTROLS: one Dim reused by a loop stays ADR-0014's per-iteration variable, distinct names stay distinct, two sibling lambdas' same-named locals never meet.</summary>
    [Test]
    public void TheControls_OneDimInALoop_DistinctNames_AndSiblingLambdas_StillPrintVbcsAnswer()
        => AssertRows(Controls);
}

/// <summary>
/// #229, the TEXT. The rename is visible in the generated source, and it is visible only where there is a pair: the second declaration is `x_1` and the first keeps `x` (the one the rename shape
/// asserts), and a procedure with NO same-named pair emits no suffixed name on any backend - the byte identity that keeps every program outside the fix unchanged. Read without Node, a C++ compiler or ilasm, so
/// a machine that cannot run the programs still sees them. Both pipelines (standard and aggressive, in process) on five backends, and `CompileProjectFiles` on four. A positive control (the pair) proves each
/// pattern is live: without it an emitter that renamed nothing would pass the "no suffixed name" check.
/// </summary>
[TestFixture]
public class SiblingDimSameNameTextTests
{
    /// <summary>The pair: an If and its Else each declare an Integer `x` from a parameter, so the optimizer has nothing to fold away. Two declarations of one spelling: `x` and `x_1`.</summary>
    private const string Pair = """
        Sub Show(n As Integer, c As Boolean)
            If c Then
                Dim x As Integer = n + 1
                Console.WriteLine(x)
            Else
                Dim x As Integer = n * 2
                Console.WriteLine(x)
            End If
        End Sub

        Sub Main()
            Show(3, True)
            Show(3, False)
        End Sub
        """;

    /// <summary>
    /// NO pair, in the shapes a careless rename would catch: the same name in two different procedures and in two methods of a class (not the same function), and one Dim reused by a loop (a single
    /// declaration). Locals: x, total, i. On every backend, LLVM included.
    /// </summary>
    private const string NoPair = """
        Sub A()
            Dim x As Integer = 1
            Console.WriteLine(x)
        End Sub

        Sub B()
            Dim x As String = "b"
            Console.WriteLine(x)
        End Sub

        Class Box
            Public Sub Run()
                Dim x As Integer = 3
                Console.WriteLine(x)
            End Sub

            Public Sub Walk()
                Dim x As Integer = 4
                Console.WriteLine(x)
            End Sub
        End Class

        Sub Loopy()
            For i As Integer = 1 To 3
                Dim total As Integer
                total = total + i
                Console.WriteLine(total)
            Next
        End Sub

        Sub Main()
            A()
            B()
            Dim bx As New Box()
            bx.Run()
            bx.Walk()
            Loopy()
        End Sub
        """;

    /// <summary>
    /// NO pair, with lambdas: one Dim reused by a loop and captured on each pass, and two SIBLING lambdas that each declare `y` (they never meet). Locals: total, i, fs, f, g, y. ⚠ Not on LLVM, which refuses a
    /// List and has no closures (a stated refusal, so the program never reaches its text); the other four backends and both pipelines read it.
    /// </summary>
    private const string NoPairLambdas = """
        Sub Loopy()
            Dim fs As New List(Of Func(Of Integer))()
            For i As Integer = 1 To 3
                Dim total As Integer
                total = total + i
                fs.Add(Function() total)
            Next
            Dim f As Func(Of Integer) = Function()
                    Dim y As Integer = 1
                    Return y
                End Function
            Dim g As Func(Of String) = Function()
                    Dim y As String = "two"
                    Return y
                End Function
            Console.WriteLine(f())
            Console.WriteLine(g())
            Console.WriteLine(fs(0)())
        End Sub

        Sub Main()
            Loopy()
        End Sub
        """;

    private static readonly string[] Backends = { "csharp", "cpp", "javascript", "msil", "llvm" };

    /// <summary>The user-named locals of <see cref="NoPair"/>: none of them may appear with a numeric suffix.</summary>
    private static readonly Regex SuffixedUserName = new(@"\b(?:x|y|total|i|fs|f|g)_\d+\b", RegexOptions.Compiled);

    /// <summary>How many times <paramref name="backend"/>'s text declares a local named <paramref name="name"/> of Integer type (an LLVM local is its `%name.addr` slot, an MSIL one a quoted local).</summary>
    private static int IntegerDeclarations(string backend, string text, string name) => backend switch
    {
        "csharp" => Regex.Matches(text, $@"(?m)^\s*int {name}\b").Count,
        "cpp" => Regex.Matches(text, $@"(?m)^\s*int32_t {name}\b").Count,
        "javascript" => Regex.Matches(text, $@"(?m)^\s*(?:let|var|const) {name}\b").Count,
        "msil" => Regex.Matches(text, $@"int32 '{name}'").Count,
        "llvm" => Regex.Matches(text, $@"%{name}\.addr = alloca i32").Count,
        _ => throw new ArgumentException(backend),
    };

    private static IRModule Build(string source, bool aggressive)
    {
        var module = JsTestSupport.BuildModule(source, sourceFilePath: "prog.bas");
        if (aggressive)
        {
            AggressivePipeline.Apply(module);
        }
        else
        {
            var pipeline = new OptimizationPipeline();
            pipeline.AddStandardPasses();
            pipeline.Run(module);
        }
        return module;
    }

    /// <summary>The backend's text for <paramref name="module"/> - a fresh module per backend: a generator is free to rewrite the one it is given.</summary>
    private static string Emit(string backend, IRModule module) => backend switch
    {
        "csharp" => new BasicLang.Compiler.CodeGen.CSharp.ImprovedCSharpCodeGenerator().Generate(module),
        "cpp" => new BasicLang.Compiler.CodeGen.CPlusPlus.CppCodeGenerator(new BasicLang.Compiler.CodeGen.CPlusPlus.CppCodeGenOptions { GenerateComments = false }).Generate(module),
        "javascript" => new BasicLang.Compiler.CodeGen.JavaScript.JavaScriptCodeGenerator().Generate(module),
        "msil" => new BasicLang.Compiler.CodeGen.MSIL.MSILCodeGenerator().Generate(module),
        "llvm" => new LLVMCodeGenerator().Generate(module),
        _ => throw new ArgumentException(backend),
    };

    /// <summary>Every text a program is read from: (label, backend, text) for both pipelines in process on <paramref name="backends"/>, and `CompileProjectFiles` on the four that run.</summary>
    private static IEnumerable<(string Label, string Backend, string Text)> Texts(string source, params string[] backends)
    {
        foreach (var aggressive in new[] { false, true })
            foreach (var backend in backends)
                yield return ($"{backend}, {(aggressive ? "aggressive" : "standard")}", backend, Emit(backend, Build(source, aggressive)));
        foreach (var backend in new[] { Bk.CSharp, Bk.Cpp, Bk.JavaScript, Bk.Msil })
            yield return ($"{TempExec.TargetName(backend)}, CompileProjectFiles", TempExec.TargetName(backend), TempExec.Emit(backend, EntryPoint.ProjectRelease, source));
    }

    /// <summary>
    /// The rename shape: of two Integer `x` in one procedure the FIRST keeps its spelling and the SECOND is `x_1`, on every backend through both pipelines and `CompileProjectFiles`; a third name (`x_2`) is
    /// never minted for two declarations.
    /// </summary>
    [Test]
    public void ASecondSameNamedDim_IsEmittedAsX_1_AndTheFirstKeepsX_OnEveryBackend()
    {
        Assert.Multiple(() =>
        {
            foreach (var (label, backend, text) in Texts(Pair, Backends))
            {
                Assert.That(IntegerDeclarations(backend, text, "x"), Is.EqualTo(1), $"{label}: the first declaration keeps 'x'\n--- emitted ---\n{text}");
                Assert.That(IntegerDeclarations(backend, text, "x_1"), Is.EqualTo(1), $"{label}: the second declaration is 'x_1'\n--- emitted ---\n{text}");
                Assert.That(IntegerDeclarations(backend, text, "x_2"), Is.EqualTo(0), $"{label}: two declarations mint one new name\n--- emitted ---\n{text}");
            }
        });
    }

    /// <summary>
    /// A procedure with no same-named pair emits no suffixed name on any backend (byte identity outside the fix): the same name in two procedures or two methods, one Dim reused by a loop (with and without a
    /// capturing lambda) and two sibling lambdas' `y` are NOT renamed. The pair above proves the pattern live.
    /// </summary>
    [Test]
    public void AProgramWithNoSameNamedPair_EmitsNoSuffixedName_OnEveryBackend()
    {
        var plain = Texts(NoPair, Backends).ToList();
        var withLambdas = Texts(NoPairLambdas, Backends.Where(b => b != "llvm").ToArray()).ToList();
        Assert.Multiple(() =>
        {
            foreach (var (label, backend, text) in plain.Concat(withLambdas))
            {
                var suffixed = SuffixedUserName.Matches(text).Select(m => m.Value).Distinct().ToList();
                Assert.That(suffixed, Is.Empty, $"{label}: a name with no same-named partner was renamed\n--- emitted ---\n{text}");
            }
            foreach (var (label, backend, text) in plain)
                Assert.That(IntegerDeclarations(backend, text, "x"), Is.GreaterThan(0),
                    $"{label}: the Integer 'x' of procedure A must still be declared as 'x' (the pattern is not live)\n--- emitted ---\n{text}");
        });
    }
}
