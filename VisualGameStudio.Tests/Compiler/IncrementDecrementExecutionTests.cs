using System.Collections.Generic;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #141 — BasicLang's own `++` and `--` (VB has neither) WRITE their operand, with C's meaning, on every backend. RUN, through
//  every entry point.
//
//  ⛔ THE BUG. The operator reached the backends as an IRUnaryOp Inc/Dec over the operand's VALUE and each backend read that its own
//  way. Measured on master, no cell ran right: a statement `x++` was DROPPED on all four backends; C++ wrote `t++;` and incremented the
//  temp; JavaScript and MSIL refused any `++` the optimizer had not folded; C# wrote `t = ++x`, so `y = x++` got the NEW value, and
//  once the optimizer folded the operand the write was lost too. IRBuilder now lowers it ONCE to what `x += 1` lowers to (a read, an
//  add of 1, the declared-type coercion, the same store chain), with a carrier local `__inc{n}` when the value is used: the postfix
//  value is the one read BEFORE the store, the prefix value the one stored, and neither is re-read from the target afterwards.
//
//  ⭐ THE ORACLE IS C, NOT vbc. VB has no `++`, so there is no compiler to ask: each expected value below is the C answer worked by
//  hand (`y = x++` gives the old value, `y = ++x` the new, `x++ + x++` is 1 + 2). The programs hold SEVERAL shapes each, and print after
//  every one, so a cell that fails names the line that went wrong.
//
//  ⛔⛔ EVERY C# RUN HERE IS HANG-SAFE (CSharpProcessRunner: a child process, 20 s, killed), not the in-process runner that has no
//  timeout. The `loops` program holds `Do While j-- > 0`, the shape whose C# emission HUNG until `OpensReentrantLoop` learned that a
//  one-block loop condition which STORES is not `while (cond)`: it wrote the condition's statements once, above the loop, and tested
//  a stale carrier forever.
//
//  ⭐ ENTRY POINTS (CLAUDE.md: "test both entry points", "validate codegen through the CLI and the IR optimizer"): the real CLI
//  (standard passes), the real CLI with `--optimize` (aggressive) and `BasicCompiler.CompileProjectFiles` with `OptimizeAggressive`
//  (what a Release .blproj build and the IDE call). The same lowering, on text and without running anything, is
//  IncrementDecrementLoweringTests.
//
//  ⛔ KNOWN GAPS — each is a limitation that is NOT #141's, measured on the fixed build. No test pins one (a pin would have to be
//  deleted the day the gap closes); the rows simply are not here.
//
//    JavaScript   ByRef parameters (BL7002), Long and ULong (BL7003): the backend refuses the PROGRAM, with or without `++`. So `byref` has
//                 no JavaScript row and no Long/ULong program runs there.
//    MSIL         Decimal: `m += 1` fails the same way ("undefined class valuetype System.Runtime.System.Decimal"). No Decimal row.
//    With         `.P++` inside a With block keeps the old IRUnaryOp: the store chain has no arm for `.P`, which is why `.P = v` is dropped
//                 there. With is broken on every backend (`__with` is never declared), so there is nothing to compare against.
//    Follow-ups   `5++` (a literal operand) is ACCEPTED, and keeps the old IRUnaryOp; `a(f()) += 1` and `a(f())++` evaluate `f` twice
//                 (the receiver and the indices are evaluated once for the read and again for the store).
//
//  ⚠ Named "…ExecutionTests" on purpose: its JavaScript cells run under Node, so it is in JsExecutionTierRosterTests' roster. Its cases are
//  [TestCase] rows because that roster counts attributes, and a [TestCaseSource] fixture counts as empty.
// ================================================================================================

/// <summary>
/// #141 RUN: `x++`, `++x`, `x--`, `--x` as statements and as values, on a local, a field, an array element, a ByRef parameter, a
/// module member, a property and in a loop condition — the C answer on C#, C++, JavaScript and MSIL, wherever the backend can print it.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the JavaScript legs and the C++ compiles share the machine with the spawned runners
public class IncrementDecrementExecutionTests
{
    private static string Lines(params string[] lines) => string.Join("\n", lines);

    /// <summary>id → (source, what C prints). The ids are the first argument of every row below.</summary>
    private static readonly Dictionary<string, (string Source, string Expected)> Programs = new()
    {
        // A statement on a local in all four spellings; `y = x++` / `y = ++x` / `y = x--` / `y = --x`; a declaration's initializer;
        // `x++ + x++` (1 + 2: the second reads what the first stored); a value in an expression and as an argument.
        ["values"] = ("""
            Sub Main()
                Dim x As Integer = 5
                x++
                Console.WriteLine(x)
                ++x
                Console.WriteLine(x)
                x--
                Console.WriteLine(x)
                --x
                Console.WriteLine(x)
                Dim y As Integer
                y = x++
                Console.WriteLine(y)
                Console.WriteLine(x)
                y = ++x
                Console.WriteLine(y)
                Console.WriteLine(x)
                y = x--
                Console.WriteLine(y)
                Console.WriteLine(x)
                y = --x
                Console.WriteLine(y)
                Console.WriteLine(x)
                Dim z As Integer = x++
                Console.WriteLine(z)
                Console.WriteLine(x)
                Dim a As Integer = 1
                Dim s As Integer = a++ + a++
                Console.WriteLine(s)
                Console.WriteLine(a)
                s = ++a * 2
                Console.WriteLine(s)
                Console.WriteLine(a)
                Console.WriteLine(a++)
                Console.WriteLine(a)
                Console.WriteLine(--a + 100)
            End Sub
            """,
            Lines("6", "7", "6", "5",
                  "5", "6", "7", "7", "7", "6", "5", "5",
                  "5", "6",
                  "3", "3", "8", "4", "4", "5", "104")),

        // A field (through a variable, bare in a method, through Me.), an array element (a literal and a variable index), a module's
        // member (qualified and bare) and a top-level global, Short and Byte, a Double, and a property (its getter, then its setter,
        // as `+=` does).
        ["targets"] = ("""
            Class Counter
                Public N As Integer
                Public Sub Bump()
                    N++
                    ++Me.N
                End Sub
                Public Function Take() As Integer
                    Return N++
                End Function
            End Class

            Class Box
                Private _v As Integer
                Public Property V As Integer
                    Get
                        Console.WriteLine("get")
                        Return _v
                    End Get
                    Set(value As Integer)
                        Console.WriteLine("set " & value)
                        _v = value
                    End Set
                End Property
            End Class

            Module Counters
                Public Hits As Integer = 0
            End Module

            Dim g As Integer = 100

            Sub Touch()
                g++
                Counters.Hits++
            End Sub

            Sub Main()
                Dim c As Counter = New Counter()
                c.N = 1
                c.N++
                Console.WriteLine(c.N)
                ++c.N
                Console.WriteLine(c.N)
                c.Bump()
                Console.WriteLine(c.N)
                Dim y As Integer = c.N--
                Console.WriteLine(y)
                Console.WriteLine(c.N)
                Console.WriteLine(c.Take())
                Console.WriteLine(c.N)

                Dim a(3) As Integer
                a(1) = 7
                a(1)++
                Console.WriteLine(a(1))
                ++a(1)
                Console.WriteLine(a(1))
                y = a(1)--
                Console.WriteLine(y)
                Console.WriteLine(a(1))
                Dim i As Integer = 2
                a(i)++
                a(i)++
                Console.WriteLine(a(2))
                y = --a(i)
                Console.WriteLine(y)
                Console.WriteLine(a(2))

                For k As Integer = 1 To 3
                    Touch()
                Next
                Console.WriteLine(g)
                Console.WriteLine(Counters.Hits)
                Dim before As Integer = g--
                Console.WriteLine(before)
                Console.WriteLine(g)
                Hits++
                Console.WriteLine(Hits)

                Dim s As Short = 5
                s++
                Console.WriteLine(s)
                Dim t As Short = s--
                Console.WriteLine(t)
                Console.WriteLine(s)
                Dim bt As Byte = 7
                ++bt
                Console.WriteLine(bt)
                Dim d As Double = 1.5
                d++
                Console.WriteLine(d)
                Dim e As Double = d--
                Console.WriteLine(e)
                Console.WriteLine(d)

                Dim bx As Box = New Box()
                bx.V = 1
                bx.V++
                Console.WriteLine(bx.V)
            End Sub
            """,
            Lines("2", "3", "5", "5", "4", "4", "5",
                  "8", "9", "9", "8", "2", "1", "1",
                  "103", "3", "103", "102", "4",
                  "6", "6", "5", "8",
                  "2.5", "2.5", "1.5",
                  "set 1", "get", "set 2", "get", "2")),

        // A ByRef parameter: a statement, a prefix value and a postfix value — each must write the CALLER's variable.
        ["byref"] = ("""
            Sub Bump(ByRef v As Integer)
                v++
            End Sub

            Function PreInc(ByRef v As Integer) As Integer
                Return ++v
            End Function

            Function PostDec(ByRef v As Integer) As Integer
                Return v--
            End Function

            Sub Main()
                Dim x As Integer = 1
                Bump(x)
                Console.WriteLine(x)
                Dim r As Integer = PreInc(x)
                Console.WriteLine(r)
                Console.WriteLine(x)
                r = PostDec(x)
                Console.WriteLine(r)
                Console.WriteLine(x)
            End Sub
            """,
            Lines("2", "3", "3", "3", "2")),

        // `++`/`--` in a loop CONDITION, in every loop form: the condition's store must re-run on every test, and the test must read the
        // value from BEFORE (postfix) or AFTER (prefix) it. `Do While j-- > 0` is the shape that hung C#. Also a statement in a body and
        // `n += k++`.
        ["loops"] = ("""
            Sub Main()
                Dim i As Integer = 0
                Dim total As Integer = 0
                While i < 5
                    total += i
                    i++
                End While
                Console.WriteLine(total)
                Console.WriteLine(i)
                Dim k As Integer = 0
                Dim n As Integer = 0
                Do While k < 10
                    n += k++
                Loop
                Console.WriteLine(n)
                Console.WriteLine(k)
                Dim j As Integer = 3
                Dim cnt As Integer = 0
                Do While j-- > 0
                    cnt++
                Loop
                Console.WriteLine(cnt)
                Console.WriteLine(j)

                Dim a As Integer = 3
                Dim c As Integer = 0
                While a-- > 0
                    c++
                End While
                Console.WriteLine(c & " " & a)

                Dim b As Integer = 0
                c = 0
                Do Until ++b >= 4
                    c++
                Loop
                Console.WriteLine(c & " " & b)

                Dim d As Integer = 0
                c = 0
                Do
                    c++
                Loop While d++ < 2
                Console.WriteLine(c & " " & d)

                Dim e As Integer = 5
                c = 0
                Do
                    c++
                Loop Until --e <= 2
                Console.WriteLine(c & " " & e)

                Dim f As Integer = 0
                Dim arr(4) As Integer
                While arr(1)++ < 3
                    f++
                End While
                Console.WriteLine(f & " " & arr(1))
            End Sub
            """,
            Lines("10", "5", "45", "10", "3", "-1",
                  "3 -1", "3 4", "3 3", "3 2", "3 4")),
    };

    // ============================================================================================
    // THE CELLS — every program on every backend that can print it, through the CLI, `--optimize` and CompileProjectFiles.
    // ============================================================================================

    [TestCase("values", Bk.CSharp)]
    [TestCase("values", Bk.Cpp)]
    [TestCase("values", Bk.JavaScript)]
    [TestCase("values", Bk.Msil)]
    [TestCase("targets", Bk.CSharp)]
    [TestCase("targets", Bk.Cpp)]
    [TestCase("targets", Bk.JavaScript)]
    [TestCase("targets", Bk.Msil)]
    [TestCase("byref", Bk.CSharp)]    // no JavaScript row: it has no reference parameters (BL7002), with or without `++`
    [TestCase("byref", Bk.Cpp)]
    [TestCase("byref", Bk.Msil)]
    [TestCase("loops", Bk.CSharp)]
    [TestCase("loops", Bk.Cpp)]
    [TestCase("loops", Bk.JavaScript)]
    [TestCase("loops", Bk.Msil)]
    public void IncrementAndDecrement_WriteTheirOperand_WithCSemantics(string program, Bk backend)
    {
        var (source, expected) = Programs[program];

        // hangSafe on every cell: C# goes through CSharpProcessRunner, never the in-process runner that has no timeout.
        TempExec.AssertMatchesInEveryEntryPoint(backend, source, expected, program, hangSafe: true);
    }
}
