using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.CodeGen.MSIL;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #232, RUN. A call used INSIDE AN EXPRESSION (`Console.WriteLine(Bump(n))`, `Dim r = Bump(n) + 1`, `If Bump(n) > 3`, `Do While Bump(n) < 5`, `Console.WriteLine(b.Bump(q))`) is rendered by
//  `CSharpBackend.EmitExpression`'s INLINE arms, and the `IRCall` and `IRInstanceMethodCall` arms built their argument lists with no by-reference modifier: csc refused the program with CS1620 ("Argument 1 must
//  be passed with the 'ref' keyword"). The STATEMENT form (`Bump(n)`, `Dim r = Bump(n)`) always built, because `Visit(IRCall)` and `Visit(IRInstanceMethodCall)` wrote the modifier. ⚠ #232's title ("a ByRef
//  parameter plus a lambda") named the wrong shape: the same program with no lambda anywhere failed identically.
//
//  ROOT CAUSE: the modifier rule lived in two places that had drifted. `WithRefModifier` (instance / base / constructor calls) wrote only `ref`; `Visit(IRCall)` had a private copy that also honoured
//  `NetArgumentRefKinds` (`out` for a .NET out parameter, nothing for in); the inline arms used neither. The fix (CSharpBackend only): `WithRefModifier` is now THE rule, taking the optional .NET ref kinds,
//  and `Visit(IRCall)` and both inline arms call it. A resolved .NET out call inlined into an expression (`If Int32.TryParse("42", n)`) gets `out n` too (it was CS1503, the Span overload).
//
//  ORACLE: vbc. Every `Vb` below is what the SDK's vbc printed for the program wrapped in a Module (the implementer's `S/t232/probes`, `probes2`, `probes4`, each with its `.exp`; the test-writer's merged
//  programs are `S/t232/tw/probes`, each VB-RAN). Never a backend's answer.
//
//  ENTRY POINTS: each row goes through the spawned CLI, the CLI with `--optimize` and `BasicCompiler.CompileProjectFiles` (aggressive) — on C#, and on C++ and MSIL where the cell already printed vbc's answer
//  before the fix (the reference: nothing in a row asserts a backend it is not named for). ⛔ Every C# run is hang-safe (`TempProbe.HangSafe` -> `CSharpProcessRunner`, a child process with a time limit; two rows
//  hold a loop). ⚠ THE PROJECT LEG ARMS .NET RESOLUTION ITSELF (`EmitViaArmedProject`): `TempExec.Emit`'s `ProjectRelease` does not, and without a resolver `Int32.TryParse` is untyped (an Object result, no
//  `out`), so the TryParse row would be red on that leg alone — a configuration the CLI and the IDE's BuildService never ship. A backend whose tool is missing (a C++ compiler, ilasm) SKIPS its cells.
//  ⚠ Named "...ExecutionTests", but it spawns no Node: JavaScript refuses a ByRef parameter by design (BL7002). It is in `JsExecutionTierRosterTests.NotJavaScriptExecution`, and the roster stays 131.
//
//  MUTANTS (each = the fix with ONE change, built from a plain source copy and run against a copy of the test output with BasicLang.dll swapped; the tests that go red against THIS fixture, the two moved pins and
//  the `p11` row, measured):
//    * M1 the inlined `IRCall` arm writes no modifier (master's inline call)       -> 11 of 76 run: 8 of this fixture's 11 rows (NOT red: `InstanceCall_…`, `AnInstanceFunction_…` and `Controls_…`, which hold no inlined
//                                                                                      `IRCall`), `LoopWhile_ByRefArgumentInTheCondition_p11` (BottomTestedLoopCSharpExecutionTests), `R3_ByRefCopiedIntoLocalFirst_CSharpRuns8`
//                                                                                      (LambdaBoundaryDiagnostics) and `AByRefLocalAndParameter_BeforeALaterCall_IsReadFirst` (OperandEvaluationOrder: its `byref` / `byrefparam` C# cells).
//    * M2 the inlined `IRInstanceMethodCall` arm writes no modifier                 -> 3: `InstanceCall_InsideAnExpression`, `AnInstanceFunction_GivenAProperty_…` and the R5 pin (LambdaBoundaryDiagnostics, which now reads
//                                                                                      `Does.Not.Contain("CS1620")`: R5's call site `b.Twice(a)` is an inlined instance call, so csc adds CS1620 to the CS1628).
//    * M3 the inlined `IRCall` arm drops the .NET ref kinds (always `ref`)          -> 1: `DotNetOutParameter_InsideAnExpression_IsOutNotRef` ONLY (`ref` where csc needs `out`: CS1503, it tries the Span overload; `If Int32.TryParse("42", n)`).
//    * M4 the shared rule spells a .NET `out` as `ref`                              -> 1: `DotNetOutParameter_…` ONLY (CS1503 again).
//    * M5 the STATEMENT `IRCall` path writes no modifier                            -> 3: `Controls_TheStatementForm_AndAByValCall_AreUnchanged`, `DotNetOutParameter_…` (its `Dim ok = Int32.TryParse(…)` line)
//                                                                                      and `AByRefArgument_PassesTheStorage_NotACopy` (OperandEvaluationOrder).
//
//  ⛔ KNOWN GAPS — each is NOT #232's fix, and has NO test (asserting one would pin a defect):
//    * A LITERAL or EXPRESSION passed ByRef to an ordinary method (`Bump(41)`, `Bump(n + 1)`) is CS1510 on C#, as the STATEMENT form already was (it was CS1620 inside an expression). VB passes a copy-in
//      temporary and leaves `n` alone. The IR has the carrier (`IRVariable.IsByRefCopyIn`) for constructor calls only; giving ordinary calls one changes the C++ and MSIL output too (#144's follow-up).
//    * A .NET INSTANCE method's `out` argument (`d.TryGetValue(k, v)`) is never ByRef-flagged in the IR, so it is CS1620 in the statement form and inside an expression. C++ prints `False 5` where vbc prints `False 0`;
//      JavaScript has no `TryGetValue`; MSIL says it is outside the collection surface.
//    * `Integer.TryParse(s, v)` is unresolved (an Object result, no flag); only `Int32.TryParse` carries the `out`.
//    * MSIL refuses a literal, `n + 1` or a FIELD passed ByRef, by name ("an expression's value lives in a temporary"): why `ByRefPlace_…` is C# and C++ only.
//    * JavaScript refuses every ByRef parameter (BL7002), by design.
// ================================================================================================

/// <summary>
/// #232 RUN: a ByRef argument of a call that is INLINED INTO AN EXPRESSION is passed by reference on C#, as vbc passes it, through every entry point.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the spawned runners, the C++ and ilasm toolchains and the CLI processes share the machine
public class ByRefCallInExpressionCSharpExecutionTests
{
    private const Bk Three = Bk.CSharp | Bk.Cpp | Bk.Msil;

    /// <summary>Every row is hang-safe (its C# leg is a child process with a time limit, <see cref="TempProbe.HangSafe"/>); <paramref name="agrees"/> names the backends whose cell is run.</summary>
    private static TempProbe Row(string id, string source, string vb, Bk agrees = Three) => new(id, source, vb, agrees, HangSafe: true);

    internal static readonly TempProbe[] Rows =
    {
        // M1's killer. `Console.WriteLine(Bump(n))` then `n`, and `Bump(m) + 1`: the two shapes whose argument was written bare (CS1620).
        Row("ByRefCall_AsAnArgumentAndAnOperand", """
            Function Bump(ByRef v As Integer) As Integer
                v = v + 100
                Return v
            End Function

            Sub Main()
                Dim n As Integer = 1
                Console.WriteLine(Bump(n))
                Console.WriteLine(n)
                Dim m As Integer = 1
                Dim r = Bump(m) + 1
                Console.WriteLine(r & " " & m)
            End Sub
            """, "101\n101\n102 101"),

        // M2's killer. An INLINED instance call, `Console.WriteLine(b.Bump(q))` and `b.Bump(q) * 2`: the IRInstanceMethodCall arm (#142's follow-up 1).
        Row("InstanceCall_InsideAnExpression", """
            Class Counter
                Public Total As Integer
                Public Function Bump(ByRef v As Integer) As Integer
                    v = v + 10
                    Total = Total + v
                    Return v
                End Function
            End Class

            Sub Main()
                Dim b As New Counter()
                Dim q As Integer = 1
                Console.WriteLine(b.Bump(q))
                Dim r As Integer = b.Bump(q) * 2
                Console.WriteLine(r & " " & q & " " & b.Total)
            End Sub
            """, "11\n42 21 32"),

        // A property passed ByRef to an INSTANCE Function inside an expression, `c.F(b.P) + 1` (#209's Q02, a gap there until #232): the call is inlined, its argument is a copy-in carrier,
        // and the property is written back after the call: `45 22`. The instance arm and the property carrier together; PropertyByRefCopyOutExecutionTests lists Q02 as a gap and runs nothing for it.
        Row("AnInstanceFunction_GivenAProperty_InsideAnExpression_WritesTheValueBack", """
            Class Box
                Public Property P As Integer
                Public Sub New()
                    P = 10
                End Sub
            End Class

            Class Calc
                Public Function F(ByRef x As Integer) As Integer
                    x += 12
                    Return x * 2
                End Function
            End Class

            Sub Main()
                Dim b As New Box()
                Dim c As New Calc()
                Dim r As Integer = c.F(b.P) + 1
                Console.WriteLine(r & " " & b.P)
            End Sub
            """, "45 22"),

        // A ByRef call as an `If` condition (twice, so `n` is read after each), as the condition of a single-line `If … Then … Else` inside a For,
        // as a `Select Case` subject, and `WriteLine(Twice(a))` where `Twice(ByRef n)` holds a lambda (#232's own title: the lambda is not what fails).
        Row("ByRefCall_InAnIf_AnInlineIf_AndASelectCaseSubject", """
            Function Bump(ByRef v As Integer) As Integer
                v = v + 1
                Return v
            End Function

            Function Twice(ByRef n As Integer) As Integer
                Dim copy As Integer = n
                Dim g As Func(Of Integer) = Function() copy * 2
                Return g()
            End Function

            Sub Main()
                Dim n As Integer = 2
                If Bump(n) > 3 Then
                    Console.WriteLine("big " & n)
                Else
                    Console.WriteLine("small " & n)
                End If
                If Bump(n) > 3 Then
                    Console.WriteLine("big " & n)
                Else
                    Console.WriteLine("small " & n)
                End If
                Dim a As Integer = 4
                Console.WriteLine(Twice(a))
                Dim m As Integer = 0
                Dim k As Integer = 0
                For i As Integer = 1 To 3
                    If Bump(m) Mod 2 = 0 Then k = k + 10 Else k = k + 1
                Next
                Console.WriteLine(m & " " & k)
                Select Case Bump(m)
                    Case 4
                        Console.WriteLine("four " & m)
                    Case Else
                        Console.WriteLine("other " & m)
                End Select
            End Sub
            """, "small 3\nbig 4\n8\n3 12\nfour 4"),

        // A ByRef call in a loop CONDITION (#227's `p11` family): `Do While`, `While … End While` and `Do … Loop Until`. (`Loop While` is a row of BottomTestedLoopCSharpExecutionTests.)
        Row("ByRefCall_InDoWhile_While_AndLoopUntilConditions", """
            Dim calls As Integer = 0

            Function Bump(ByRef v As Integer) As Integer
                calls = calls + 1
                v = v + 1
                Return v
            End Function

            Sub Main()
                Dim n As Integer = 0
                Dim seen As String = ""
                Do While Bump(n) < 5
                    seen = seen & n
                Loop
                Console.WriteLine("n=" & n & " calls=" & calls & " seen=" & seen)
                Dim w As Integer = 0
                Dim k As Integer = 0
                While Bump(w) < 4
                    k = k + 1
                End While
                Console.WriteLine(w & " " & k)
                Dim m As Integer = 0
                Do
                    k = k + 1
                Loop Until Bump(m) >= 3
                Console.WriteLine(m & " " & k)
            End Sub
            """, "n=5 calls=5 seen=1234\n4 3\n3 6"),

        // `Bump(n) + Bump(n)` and `Bump(n) - Bump(n)` (each call sees the last one's write), `x + Bump(x)` (x is read BEFORE the call: 102), `Bump(y) + y` (AFTER: 202)
        // and `z & Bump(z) & z`. C# takes the address of a `ref` argument and reads the other operands in written order, as VB does.
        Row("TwoByRefCalls_AndTheOperandsAroundOne_RunLeftToRight", """
            Function Bump(ByRef v As Integer) As Integer
                v = v + 100
                Console.WriteLine("bump " & v)
                Return v
            End Function

            Sub Main()
                Dim n As Integer = 1
                Dim r As Integer = Bump(n) + Bump(n)
                Console.WriteLine(r & " " & n)
                Console.WriteLine(Bump(n) - Bump(n))
                Console.WriteLine(n)
                Dim x As Integer = 1
                Dim a As Integer = x + Bump(x)
                Console.WriteLine(a & " " & x)
                Dim y As Integer = 1
                Dim b As Integer = Bump(y) + y
                Console.WriteLine(b & " " & y)
                Dim z As Integer = 1
                Console.WriteLine(z & " " & Bump(z) & " " & z)
            End Sub
            """, "bump 101\nbump 201\n302 201\nbump 301\nbump 401\n-100\n401\nbump 101\n102 101\nbump 101\n202 101\nbump 101\n1 101 101"),

        // The risky one: a call whose OTHER arguments have side effects on the very variable passed ByRef, `Mix(SetN(5), n, SetN(7))`. VB evaluates left to right, so Mix sees n = 7.
        Row("ByRefArgument_IsReadAfterTheArgumentsBeforeIt", """
            Dim n As Integer = 0

            Function SetN(v As Integer) As Integer
                Console.WriteLine("set " & v)
                n = v
                Return v
            End Function

            Function Mix(a As Integer, ByRef v As Integer, c As Integer) As Integer
                Console.WriteLine("mix " & a & " " & v & " " & c)
                v = v + 1000
                Return a + c
            End Function

            Sub Main()
                Console.WriteLine(Mix(SetN(5), n, SetN(7)))
                Console.WriteLine(n)
                Dim r As Integer = Mix(SetN(1), n, SetN(2)) * 10
                Console.WriteLine(r & " " & n)
            End Sub
            """, "set 5\nset 7\nmix 5 7 7\n12\n1007\nset 1\nset 2\nmix 1 2 2\n30 1002"),

        // An array element (`a(1)`), a field (`h.V`) and a String variable passed ByRef inside an expression. C# and C++ only: MSIL refuses a field passed ByRef by name (a gap, in the header).
        Row("ByRefPlace_AnArrayElement_AField_AndAString", """
            Class Holder
                Public V As Integer
            End Class

            Function Bump(ByRef v As Integer) As Integer
                v = v + 100
                Return v
            End Function

            Function Grow(ByRef s As String) As Integer
                s = s & "x"
                Return s.Length
            End Function

            Sub Main()
                Dim a(2) As Integer
                a(1) = 5
                Console.WriteLine(Bump(a(1)))
                Dim r As Integer = Bump(a(1)) + 1
                Console.WriteLine(r & " " & a(1))
                Dim h As New Holder()
                h.V = 3
                Console.WriteLine(Bump(h.V))
                Dim q As Integer = Bump(h.V) + 1
                Console.WriteLine(q & " " & h.V)
                Dim s As String = "ab"
                Console.WriteLine(Grow(s) & s)
                Console.WriteLine(s & Grow(s))
                Console.WriteLine(s)
            End Sub
            """, "105\n206 205\n103\n204 203\n3abx\nabx4\nabxx", Bk.CSharp | Bk.Cpp),

        // h166's second half: `Bump(a) + 1` and `Console.WriteLine(Bump(a))` INSIDE a lambda body, on the lambda's own parameter.
        Row("ByRefCall_InsideALambda", """
            Function Bump(ByRef v As Integer) As Integer
                v = v + 100
                Return v
            End Function

            Sub Main()
                Dim f = Function(a As Integer) As Integer
                            Dim q As Integer = Bump(a) + 1
                            Return q + a
                        End Function
                Console.WriteLine(f(2))
                Dim g = Function(a As Integer) As Integer
                            Console.WriteLine(Bump(a))
                            Return a
                        End Function
                Console.WriteLine(g(3))
            End Sub
            """, "205\n103\n103"),

        // M3's and M4's killer. `If Int32.TryParse("42", n)` and `WriteLine(Int32.TryParse("x", n))`: the inline call must write `out`, not `ref` (M4) and not nothing (M3: CS1503 picks the Span overload).
        // The `Dim ok = Int32.TryParse("7", n)` line is the STATEMENT form of the same rule (M5). C# only: C++, MSIL and JavaScript do not resolve the .NET method.
        Row("DotNetOutParameter_InsideAnExpression_IsOutNotRef", """
            Sub Main()
                Dim n As Integer = 0
                If Int32.TryParse("42", n) Then
                    Console.WriteLine("ok " & n)
                End If
                Console.WriteLine(Int32.TryParse("x", n))
                Console.WriteLine(n)
                Dim ok = Int32.TryParse("7", n)
                Console.WriteLine(ok & " " & n)
            End Sub
            """, "ok 42\nFalse\n0\nTrue 7", Bk.CSharp),

        // M5's killer. The statement form (`Bump(n)` as a statement, `Dim r = Bump(n)`) still writes `ref`, and a ByVal call writes none (`Twice(m)` leaves `m` at 3).
        Row("Controls_TheStatementForm_AndAByValCall_AreUnchanged", """
            Function Bump(ByRef v As Integer) As Integer
                v = v + 100
                Return v
            End Function

            Function Twice(v As Integer) As Integer
                v = v * 2
                Return v
            End Function

            Sub Main()
                Dim n As Integer = 1
                Bump(n)
                Dim r As Integer = Bump(n)
                Console.WriteLine(r & " " & n)
                Dim m As Integer = 3
                Console.WriteLine(Twice(m))
                Dim t As Integer = Twice(m) + 1
                Console.WriteLine(t & " " & m)
            End Sub
            """, "201 201\n6\n7 3"),

    };

    private static IEnumerable<TestCaseData> Cells() => Rows.Select(r => new TestCaseData(r).SetName(r.Id));

    /// <summary>
    /// <c>CompileProjectFiles</c> with the aggressive passes and .NET resolution ARMED, to the backend's generated text: what a Release <c>.blproj</c> build and the IDE's BuildService call, and both arm
    /// it (as does the CLI's single-file compile). <c>TempExec.Emit</c>'s project leg does NOT, and an unarmed `Int32.TryParse` is untyped (an Object result, no `out`): the TryParse row would go red on that
    /// leg alone, which reads as a product bug and is not one (the same arming `NetSubtypeWideningExecutionTests` and `LinqQueryExpressionExecutionTests` carry).
    /// </summary>
    private static string EmitViaArmedProject(Bk backend, string source)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-t232-armed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "Main.bas");
            File.WriteAllText(path, source);
            var options = new CompilerOptions { OptimizeAggressive = true, TargetBackend = TempExec.TargetName(backend) };
            options.EnableNetResolution();
            var result = new BasicCompiler(options).CompileProjectFiles(new List<string> { path });
            Assert.That(result.HasErrors, Is.False, string.Join(" | ", result.AllErrors.Select(e => e.Message)));
            Assert.That(result.CombinedIR, Is.Not.Null, "the project entry point produced no combined IR");
            return backend switch
            {
                Bk.CSharp => new ImprovedCSharpCodeGenerator().Generate(result.CombinedIR),
                Bk.Cpp => new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false }).Generate(result.CombinedIR),
                Bk.Msil => new MSILCodeGenerator().Generate(result.CombinedIR),
                _ => throw new ArgumentException(backend.ToString()),
            };
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* temp */ } }
    }

    /// <summary>One program on one backend through the CLI, the CLI with `--optimize` and the armed project leg; every failing entry point is collected, so the other two still report.</summary>
    private static void AssertMatchesInEveryEntryPoint(Bk backend, TempProbe probe)
    {
        TempExec.RequireTool(backend);
        var failures = new List<string>();
        foreach (var entry in Enum.GetValues<EntryPoint>())
        {
            try
            {
                var emitted = entry == EntryPoint.ProjectRelease ? EmitViaArmedProject(backend, probe.Source) : TempExec.Emit(backend, entry, probe.Source);
                var got = TempExec.Norm(TempExec.Run(backend, emitted, hangSafe: true));
                if (got != TempExec.Norm(probe.Vb))
                    failures.Add($"{entry}: printed [{got.Replace("\n", " | ")}] where VB prints [{TempExec.Norm(probe.Vb).Replace("\n", " | ")}]");
            }
            catch (AssertionException ex)
            {
                failures.Add($"{entry}: {ex.Message[..Math.Min(ex.Message.Length, 1200)]}");
            }
        }

        Assert.That(failures, Is.Empty, $"{probe.Id} on {backend}:\n" + string.Join("\n", failures));
    }

    /// <summary>
    /// Each program, on C# and (where the row says so) C++ and MSIL, through the CLI, the CLI with `--optimize` and `CompileProjectFiles`: it prints what vbc printed. A failing backend is collected, not
    /// thrown, so every other one still reports and the text names the backend and the entry point. A backend whose tool is missing (a C++ compiler, ilasm) SKIPS its cells; the C# cells always run.
    /// </summary>
    [TestCaseSource(nameof(Cells))]
    public void AByRefCallInsideAnExpression_IsPassedByReference_AsVbcPassesIt(TempProbe probe)
    {
        var failures = new List<string>();
        int ran = 0, skipped = 0;
        foreach (var backend in TempExec.Backends(probe.Agrees))
        {
            try
            {
                AssertMatchesInEveryEntryPoint(backend, probe);
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

        Assert.That(failures, Is.Empty, string.Join("\n", failures));
        if (ran == 0) Assert.Ignore($"no execution tool on this machine ({skipped} cells skipped).");
    }
}
