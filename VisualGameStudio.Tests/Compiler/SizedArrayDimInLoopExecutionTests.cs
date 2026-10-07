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
//  Task #228, RUN. VB gives `Dim a(2) As Integer` inside a loop body a NEW zeroed array each time the statement executes. BasicLang allocated it ONCE, at function top, on every backend: the bounds were never
//  an IR instruction, so a value written in one iteration was still there in the next, and every array a List or a lambda had kept from an earlier iteration was the same array (vbc `1|2|3`, BasicLang `6|6|6`
//  for a captured array and `1|3|6` for a plain accumulator).
//
//  Now a sized local `Dim` with no initializer, inside a loop of its OWN function (ADR-0014 D3's innermost-loop rule, through If / Select / Try, never across a lambda), lowers to an `IRCall` of
//  `IRBuilder.SizedArrayDimIntrinsic` (`__BLDimArray`) at the statement, named after the variable as a ReDim's call is. Each backend renders it with the helper its function-top declaration already used:
//  C# `a = new int[3];`, JavaScript `a = new Array(3).fill(0);`, C++ `a = std::vector<int32_t>(3);` (a NEW Array handle), MSIL `ldc.i4 3; newarr; stloc`. A sized Dim OUTSIDE every loop emits exactly what it did:
//  the function-top allocation is still the only one (`SizedArrayDimInLoopTextTests`, fast, below). ADR-0014 Amendment A-228.
//
//  ORACLE: vbc, via `S/t136/tools/vbv2.py`, over each program wrapped in a VB Module (S/t228/tw-probes: r1-r7, each a join of the implementer's own probes S/t228/probes p01-p20, one Sub per shape and a `Main`
//  that calls them). Every expected string is vbc's output (the `.exp` beside each `.bas`), never a backend's; a program prints one line per iteration so ONE row can carry several shapes.
//
//  ENTRY POINTS: the spawned CLI (standard passes), the CLI with `--optimize` and `BasicCompiler.CompileProjectFiles` (aggressive) - `TempExec.AssertMatchesInEveryEntryPoint` - on C#, C++, JavaScript and MSIL.
//  ⛔ Every row is `HangSafe` (the C# cells run in a child process with a time limit, `CSharpProcessRunner`, #256): each program loops. A backend whose tool is missing (a C++ compiler, Node, ilasm) SKIPS its
//  cells, never fails them; the test is ignored only when no cell could run.
//  ⚠ Named "...ExecutionTests" and runs JavaScript under Node: it is in `JsExecutionTierRosterTests`' roster. `SizedArrayDimInLoopTextTests` (in process, no Node, no [Category("Integration")]) is NOT.
//
//  MUTANTS (the fix plus ONE change, built from a plain source copy and run against a copy of the test output with its BasicLang.dll swapped; each is killed by a committed test; recipes S/t228/tw-mut.py).
//  A mutant of one backend fails every row's cell on THAT backend (the rows run all four), so each is named by the cells that go red, measured:
//    M0 the unfixed source (the five files as at f60bd8a0)                                                -> 9 of 10: every case but `TheControls_...`; the two text cases fail on the in-a-loop control
//    M1 the loop check dropped (`AllocatesAtTheStatement` returns true for every sized local Dim)          -> exactly the two `SizedArrayDimInLoopTextTests` cases: a loop-free Dim gains an allocation statement on
//                                                                              every backend and an `__BLDimArray` call in the IR (2 of them). NO run sees it: the program still prints vbc's answer.
//    M3 the call not renamed to the variable (`TryRenameToVariable` dropped)                              -> exactly `ATempShapedName_CapturedByALambda_StaysTheUsersAndFresh` (a Dim named `t1`: the call is not the variable;
//                                                                              C# is CS0103 on a minted `t27`, the others print the old values). Every other row prints vbc's answer under M3.
//    M4 C# renders the declaration's default (null) at the statement                                      -> the 15 C# cells of r1-r5 (NullReferenceException), and both text cases
//    M5 JavaScript allocates `new Array(3)` with no `.fill(0)`                                            -> the 15 JavaScript cells of r1-r5 (`undefined` where vbc prints 0), and both text cases
//    M7 C++ assigns the variable to itself (no new storage, so the old handle is kept)                    -> the 15 C++ cells of r1-r5: r1 prints 0|1|2, r4 and r5 the old values, r2 and r3 (a captured array) do not compile; and both text cases
//
//  ⛔ KNOWN GAPS - each is the same before and after #228 and NOT its to fix; listed with NO test (asserting one would pin a defect):
//    p08  a JavaScript String array's elements start as "", where VB's are Nothing: `Dim a(2) As String` then `a(0) Is Nothing` is False on JavaScript, on every pass (C#, C++ and MSIL print vbc's "fresh").
//    p09  `Dim a(i) As Integer` with a non-constant size is refused by the front end on every backend ("Array size must be a compile-time constant"), so an in-loop one never reaches this change.
//    p11  MSIL refuses ReDim (`__BLReDim` has no lowering), so the ReDim control below runs on C#, C++ and JavaScript only.
//    p13  MSIL refuses a rank-2 sized array ("a 2-dimensional array has no IL lowering"); C#, C++ and JavaScript allocate it per iteration like a rank-1.
//    p15  two SAME-NAMED sibling Dims (`#229`): ⚠ FIXED (2026-10-07) - the second is its own variable (`a_1`), so two sibling sized arrays are two arrays and run on C#, C++, JavaScript
//         and MSIL (`SiblingDimSameNameExecutionTests`); this fixture's own header claim, that C++ and MSIL refuse to compile it, no longer holds.
//    #264 a class method's or an MSIL lambda's sized array OUTSIDE a loop is allocated nowhere (`int[] a = default!`): unchanged. Only the in-loop case now works, and it is a row here.
//    LLVM allocates no sized array anywhere, so it renders the new call as a call to an undefined `@__BLDimArray`, exactly as it does `@__BLReDim`; there is no LLVM execution row, and
//    `SizedArrayDimInLoopTextTests` reads its text only to keep a loop-free program free of the call.
// ================================================================================================

/// <summary>#228 RUN: a sized array Dim in a loop body is a new zeroed array every time it runs, on C#, C++, JavaScript and MSIL, as vbc runs it.</summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the spawned CLI, the C# child runs, the C++ compiler, ilasm and Node share the machine
public class SizedArrayDimInLoopExecutionTests
{
    /// <summary>
    /// r1 - every loop kind reads a FRESH array: a counted For, a While (two elements), a `Do While`, a `Do ... Loop Until`, and a For Each through an If (D3: through an If, not only the loop's own body).
    /// vbc prints `0` x3, `0` x3, `0` x3, `0` x3, then `1 2 3` (the For Each's element is written before it is read).
    /// </summary>
    private static readonly TempProbe LoopForms = P("r1_loop_forms", """
        Sub ForForm()
            For i As Integer = 1 To 3
                Dim a(2) As Integer
                Console.WriteLine(a(0))
                a(0) = i
            Next
        End Sub

        Sub WhileForm()
            Dim i As Integer = 0
            While i < 3
                i = i + 1
                Dim a(2) As Integer
                Console.WriteLine(a(0) + a(1))
                a(0) = i
                a(1) = 10 * i
            End While
        End Sub

        Sub DoWhileForm()
            Dim i As Integer = 0
            Do While i < 3
                i = i + 1
                Dim a(2) As Integer
                Console.WriteLine(a(1))
                a(1) = a(1) + i
            Loop
        End Sub

        Sub DoUntilForm()
            Dim i As Integer = 0
            Do
                i = i + 1
                Dim a(2) As Integer
                Console.WriteLine(a(1))
                a(1) = a(1) + i
            Loop Until i >= 3
        End Sub

        Sub ForEachThroughAnIf()
            Dim xs As New List(Of Integer)()
            xs.Add(1)
            xs.Add(2)
            xs.Add(3)
            For Each v As Integer In xs
                If v > 0 Then
                    Dim a(2) As Integer
                    a(0) = a(0) + v
                    Console.WriteLine(a(0))
                End If
            Next
        End Sub

        Sub Main()
            ForForm()
            WhileForm()
            DoWhileForm()
            DoUntilForm()
            ForEachThroughAnIf()
        End Sub
        """, "0\n0\n0\n0\n0\n0\n0\n0\n0\n0\n0\n0\n1\n2\n3");

    /// <summary>
    /// r2 - an array KEPT past its iteration is that iteration's own: pushed into a `List(Of Integer())`, captured by a lambda (the E15 shape, #136's fixture), and captured by a lambda in a loop that leaves with
    /// an `Exit For` and is entered again (p17). Before #228 every backend printed one array's final value for all of them. vbc prints `1 2 3`, `1 2 3`, `11 12 21 22`.
    /// </summary>
    private static readonly TempProbe KeptArrays = P("r2_kept_arrays", """
        Sub KeptInAList()
            Dim lst As New List(Of Integer())()
            For i As Integer = 1 To 3
                Dim a(2) As Integer
                a(0) = a(0) + i
                lst.Add(a)
            Next
            For k As Integer = 0 To lst.Count - 1
                Dim b As Integer() = lst(k)
                Console.WriteLine(b(0))
            Next
        End Sub

        Sub CapturedByALambda()
            Dim fs As New List(Of Func(Of Integer))()
            For i As Integer = 1 To 3
                Dim a(2) As Integer
                a(0) = a(0) + i
                fs.Add(Function() a(0))
            Next
            For k As Integer = 0 To fs.Count - 1
                Dim f As Func(Of Integer) = fs(k)
                Console.WriteLine(f())
            Next
        End Sub

        Sub CapturedThroughAnExitFor()
            Dim fs As New List(Of Func(Of Integer))()
            For r As Integer = 1 To 2
                For i As Integer = 1 To 3
                    Dim a(2) As Integer
                    a(0) = a(0) + i + 10 * r
                    fs.Add(Function() a(0))
                    If i = 2 Then Exit For
                Next
            Next
            For k As Integer = 0 To fs.Count - 1
                Console.WriteLine(fs(k)())
            Next
        End Sub

        Sub Main()
            KeptInAList()
            CapturedByALambda()
            CapturedThroughAnExitFor()
        End Sub
        """, "1\n2\n3\n1\n2\n3\n11\n12\n21\n22");

    /// <summary>
    /// r3 - the M3 killer (p20): the array is NAMED like a compiler temp (`t1`, beside a `t0`) and is captured. The new call must be named after THE USER'S variable, never a minted temp: with the name dropped
    /// the call is a free-standing value, nothing assigns `t1`, and C# reads a minted `t27` (CS0103) while the others keep printing the old array. vbc prints `1 2 3` then `1 2 3`.
    /// </summary>
    private static readonly TempProbe ATempShapedName = P("r3_temp_shaped_name", """
        Sub Main()
            Dim fs As New List(Of Func(Of Integer))()
            For i As Integer = 1 To 3
                Dim t1(2) As Integer
                Dim t0 As Integer = t1(0)
                t1(0) = t1(0) + i
                fs.Add(Function() t1(0))
                Console.WriteLine(t0 * 10 + t1(0))
            Next
            For k As Integer = 0 To fs.Count - 1
                Console.WriteLine(fs(k)())
            Next
        End Sub
        """, "1\n2\n3\n1\n2\n3");

    /// <summary>
    /// r4 - two sized Dims in ONE body (p19: each is its own array, neither disturbs the other) and a nested loop (p07: the outer array lives across the inner loop's iterations while the inner one is fresh
    /// each time). vbc prints `11 22` then `111 213 121 223`.
    /// </summary>
    private static readonly TempProbe TwoDimsAndNesting = P("r4_two_dims_and_nesting", """
        Sub TwoDimsInOneBody()
            For i As Integer = 1 To 2
                Dim a(2) As Integer
                Dim b(2) As Integer
                a(0) = a(0) + i
                b(0) = b(0) + 10 * i
                Console.WriteLine(a(0) + b(0))
            Next
        End Sub

        Sub NestedLoops()
            For i As Integer = 1 To 2
                Dim b(2) As Integer
                b(0) = b(0) + i
                For j As Integer = 1 To 2
                    Dim a(2) As Integer
                    a(0) = a(0) + j
                    b(1) = b(1) + j
                    Console.WriteLine(a(0) * 100 + b(0) * 10 + b(1))
                Next
            Next
        End Sub

        Sub Main()
            TwoDimsInOneBody()
            NestedLoops()
        End Sub
        """, "11\n22\n111\n213\n121\n223");

    /// <summary>
    /// r5 - the two places a sized array in a loop was never allocated at all (#264's in-loop half, which this fixes as it falls out): a class METHOD's loop (C# `int[] a = default!` then a
    /// NullReferenceException, MSIL the same) and a LAMBDA's OWN loop (MSIL NullReferenceException). vbc prints `1 2 3` then `123`.
    /// </summary>
    private static readonly TempProbe MethodAndLambdaLoops = P("r5_method_and_lambda_loops", """
        Class C
            Public Sub Run()
                For i As Integer = 1 To 3
                    Dim a(2) As Integer
                    a(0) = a(0) + i
                    Console.WriteLine(a(0))
                Next
            End Sub
        End Class

        Sub LambdasOwnLoop()
            Dim f As Func(Of Integer) = Function()
                                            Dim t As Integer = 0
                                            For i As Integer = 1 To 3
                                                Dim a(2) As Integer
                                                a(0) = a(0) + i
                                                t = t * 10 + a(0)
                                            Next
                                            Return t
                                        End Function
            Console.WriteLine(f())
        End Sub

        Sub Main()
            Dim c As New C()
            c.Run()
            LambdasOwnLoop()
        End Sub
        """, "1\n2\n3\n123");

    /// <summary>
    /// r6 - CONTROLS that must not move: a sized Dim OUTSIDE the loop is ONE array for the whole call (it accumulates, vbc `1 3 6`), and `Dim a() As Integer = {1, 2}` in a loop is a literal, which already
    /// allocated at the statement (vbc `2 3 4`).
    /// </summary>
    private static readonly TempProbe ControlsOutsideTheChange = P("r6_controls", """
        Sub SizedDimOutsideTheLoop()
            Dim a(2) As Integer
            For i As Integer = 1 To 3
                a(0) = a(0) + i
                Console.WriteLine(a(0))
            Next
        End Sub

        Sub ArrayLiteralInTheLoop()
            For i As Integer = 1 To 3
                Dim a() As Integer = {1, 2}
                a(0) = a(0) + i
                Console.WriteLine(a(0))
            Next
        End Sub

        Sub Main()
            SizedDimOutsideTheLoop()
            ArrayLiteralInTheLoop()
        End Sub
        """, "1\n3\n6\n2\n3\n4");

    /// <summary>
    /// r7 - the ReDim control: `ReDim a(2)` in a loop is a new array each pass (vbc `0 0 0`), which is the helper-call shape the new intrinsic copies. NOT on MSIL, which refuses ReDim (p11, a known gap).
    /// </summary>
    private static readonly TempProbe AReDimInTheLoop = new("r7_redim_control", """
        Sub Main()
            Dim a() As Integer
            For i As Integer = 1 To 3
                ReDim a(2)
                Console.WriteLine(a(0))
                a(0) = i
            Next
        End Sub
        """, "0\n0\n0", Bk.CSharp | Bk.Cpp | Bk.JavaScript, HangSafe: true);

    private static TempProbe P(string id, string source, string vb) => new(id, source, vb, Bk.All, HangSafe: true);

    /// <summary>
    /// Each probe on every backend it runs on, each through every entry point. A failing cell is collected, not thrown, so every other one still reports, and the failure names the probe, the backend and the
    /// entry point. A backend whose tool is missing skips its cells. (A copy of the helper `MsilGenericTypeTokenExecutionTests` keeps: each fixture owns its own.)
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

    /// <summary>The headline. A For, While, `Do While`, `Do ... Loop Until` and a For Each through an If each read a fresh zeroed array on every pass. Before #228 the second pass read the first's values. Kills M4, M5 and M7.</summary>
    [Test]
    public void EveryLoopForm_StartsEachIterationFromAFreshZeroedArray()
        => AssertRows(LoopForms);

    /// <summary>An array kept in a List, captured by a lambda, or captured through an `Exit For` is the iteration's own (was one array's last value, 6|6|6). Kills M5 and M7 (C++: the old handle is kept).</summary>
    [Test]
    public void AnArrayKeptByAListOrACapturingLambda_IsTheIterationsOwn()
        => AssertRows(KeptArrays);

    /// <summary>
    /// A sized Dim named like a compiler temp (`t1`) and captured: the new call is named after the USER'S variable. Kills M3 (the call not renamed: C# CS0103, the others the old array), the only mutant no other row can see.
    /// </summary>
    [Test]
    public void ATempShapedName_CapturedByALambda_StaysTheUsersAndFresh()
        => AssertRows(ATempShapedName);

    /// <summary>Two sized Dims in one body are two arrays, and a nested loop's inner array is fresh while the outer one lives on.</summary>
    [Test]
    public void TwoDimsInOneBody_AndNestedLoops_AreIndependentFreshArrays()
        => AssertRows(TwoDimsAndNesting);

    /// <summary>A class method's loop and a lambda's OWN loop allocate their sized array (was a NullReferenceException on C# / MSIL: #264's in-loop half). Kills M4 on the method row.</summary>
    [Test]
    public void AnArrayInAMethodsLoop_AndInTheLambdasOwnLoop_IsAllocated()
        => AssertRows(MethodAndLambdaLoops);

    /// <summary>The CONTROLS: a sized Dim outside the loop stays one accumulating array, an array literal in a loop and a ReDim in a loop still print vbc's answer (the ReDim on C#, C++ and JavaScript: MSIL refuses it).</summary>
    [Test]
    public void TheControls_ADimOutsideTheLoop_ALiteralAndAReDim_StillPrintVbcsAnswer()
        => AssertRows(ControlsOutsideTheChange, AReDimInTheLoop);
}

/// <summary>
/// #228, the TEXT: a sized Dim OUTSIDE every loop emits what it always did - the function-top allocation and nothing at the statement - on every backend, so the change stays byte-identical outside a loop.
/// Read without Node, a C++ compiler or ilasm, so a machine that cannot run the programs still sees M1 (the loop check dropped: every sized Dim would allocate at its statement). NO run can see M1: the program
/// still prints vbc's answer. A positive control (the same Dim inside a loop) proves each pattern is live: without it an emitter that renamed the statement would pass every "no allocation" check.
/// </summary>
[TestFixture]
public class SizedArrayDimInLoopTextTests
{
    /// <summary>Two functions, no sized Dim inside a loop: one with no loop at all, one whose loop comes AFTER the Dim. Two function-top allocations (`a`, `b`) and no statement allocation.</summary>
    private const string LoopFree = """
        Sub NoLoop()
            Dim a(2) As Integer
            a(0) = 5
            Console.WriteLine(a(0))
        End Sub

        Sub BeforeTheLoop()
            Dim b(2) As Integer
            For i As Integer = 1 To 2
                b(0) = b(0) + i
                Console.WriteLine(b(0))
            Next
        End Sub

        Sub Main()
            NoLoop()
            BeforeTheLoop()
        End Sub
        """;

    private const int LoopFreeDeclarations = 2;

    /// <summary>The positive control: ONE sized Dim, inside a loop. One function-top allocation and one at the statement.</summary>
    private const string InALoop = """
        Sub Main()
            For i As Integer = 1 To 3
                Dim c(2) As Integer
                c(0) = c(0) + i
                Console.WriteLine(c(0))
            Next
        End Sub
        """;

    private const int InALoopDeclarations = 1;

    private static readonly string[] Backends = { "csharp", "cpp", "javascript", "msil", "llvm" };

    /// <summary>
    /// How many allocations a program's text writes AT A STATEMENT (not at a declaration) for a sized Int array of 3. C#, JavaScript and C++ write a declaration as `T v = ...` / `let v = ...`, so an allocation
    /// that starts the line with the bare variable is the new one; MSIL has no statement form, so it is every `newarr` beyond the declarations; LLVM writes the intrinsic as a call to `@__BLDimArray`.
    /// </summary>
    private static int StatementAllocations(string backend, string text, int declarations) => backend switch
    {
        "csharp" => Regex.Matches(text, @"(?m)^\s*[abc] = new int\[3\];").Count,
        "javascript" => Regex.Matches(text, @"(?m)^\s*[abc] = new Array\(3\)\.fill\(0\);").Count,
        "cpp" => Regex.Matches(text, @"(?m)^\s*[abc] = std::vector<int32_t>\(3\);").Count,
        "msil" => Regex.Matches(text, @"\bnewarr\b").Count - declarations,
        "llvm" => Regex.Matches(text, "__BLDimArray").Count,
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

    private static int IntrinsicCalls(IRModule module)
        => TempIr.Functions(module)
            .SelectMany(TempIr.Instructions)
            .OfType<IRCall>()
            .Count(c => c.FunctionName == IRBuilder.SizedArrayDimIntrinsic);

    /// <summary>
    /// Both programs on every backend, through the standard and the aggressive passes (in process). Loop-free: no allocation at a statement and no intrinsic call in the IR. In a loop: exactly one of each, so
    /// the patterns are live. Kills M1 (the loop check dropped).
    /// </summary>
    [Test]
    public void ASizedDimOutsideEveryLoop_EmitsNoAllocationAtTheStatement_OnEveryBackend_BothPipelines()
    {
        Assert.Multiple(() =>
        {
            foreach (var aggressive in new[] { false, true })
            {
                var pipeline = aggressive ? "aggressive" : "standard";
                Assert.That(IntrinsicCalls(Build(LoopFree, aggressive)), Is.EqualTo(0), $"loop-free IR, {pipeline}: a __BLDimArray call outside every loop");
                Assert.That(IntrinsicCalls(Build(InALoop, aggressive)), Is.EqualTo(1), $"in-a-loop IR, {pipeline}: the control must hold exactly one __BLDimArray call");
                foreach (var backend in Backends)
                {
                    var loopFree = Emit(backend, Build(LoopFree, aggressive));
                    var inALoop = Emit(backend, Build(InALoop, aggressive));
                    Assert.That(StatementAllocations(backend, loopFree, LoopFreeDeclarations), Is.EqualTo(0),
                        $"{backend}, {pipeline}: a sized Dim outside every loop allocated at its statement\n--- emitted ---\n{loopFree}");
                    Assert.That(StatementAllocations(backend, inALoop, InALoopDeclarations), Is.EqualTo(1),
                        $"{backend}, {pipeline}: the in-a-loop control must allocate once at its statement (the pattern is not live)\n--- emitted ---\n{inALoop}");
                }
            }
        });
    }

    /// <summary>
    /// The same two programs through <c>BasicCompiler.CompileProjectFiles</c> (aggressive: what a Release .blproj build and the IDE call), on C#, C++, JavaScript and MSIL: the loop-free text writes no allocation at a
    /// statement, the control writes one. The second entry point for M1.
    /// </summary>
    [Test]
    public void ASizedDimOutsideEveryLoop_EmitsNoAllocationAtTheStatement_ThroughCompileProjectFiles()
    {
        Assert.Multiple(() =>
        {
            foreach (var backend in new[] { Bk.CSharp, Bk.Cpp, Bk.JavaScript, Bk.Msil })
            {
                var name = TempExec.TargetName(backend);
                var loopFree = TempExec.Emit(backend, EntryPoint.ProjectRelease, LoopFree);
                var inALoop = TempExec.Emit(backend, EntryPoint.ProjectRelease, InALoop);
                Assert.That(StatementAllocations(name, loopFree, LoopFreeDeclarations), Is.EqualTo(0),
                    $"{name}, CompileProjectFiles: a sized Dim outside every loop allocated at its statement\n--- emitted ---\n{loopFree}");
                Assert.That(StatementAllocations(name, inALoop, InALoopDeclarations), Is.EqualTo(1),
                    $"{name}, CompileProjectFiles: the in-a-loop control must allocate once at its statement (the pattern is not live)\n--- emitted ---\n{inALoop}");
            }
        });
    }
}
