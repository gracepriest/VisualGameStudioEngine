using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #203, RUN. VB reads operands LEFT TO RIGHT: in `K + Bump()`, `F(K, Bump())` and `Handler(Swap(1))` the value of K / Handler is taken BEFORE the call runs, so a call that writes it is
//  not seen. A bare field (an implicit-Me read), a Shared field, a module global and a ByRef parameter lower to an IRVariable (ADR-0007's bare-name rule), which C++, JavaScript and MSIL read BY NAME at the
//  instruction that CONSUMES it, after every later operand's call, so they printed the NEW value: a silent wrong answer. C# renders the expression inline, in source order, and was right everywhere but an
//  `If()` operand. `Me.K` was always right (an IRFieldAccess is an instruction, evaluated where it stands). The fix is in `IRBuilder` (`ReadOperand` / `ValueBeforeLaterOperands`): when a LATER operand may write
//  the storage, the value is copied into a `__snap{n}` carrier AT the read, and the carrier stands in for the operand.
//
//  ORACLE: vbc. Every probe below is `S/t203/probes*/*.bas` verbatim, wrapped in a VB Module and run with vbc (S/t136/tools/vbv2.py), and its expected text is that run's output, never a backend's. The
//  test-writer re-ran all 32 probes through vbc again before writing these and every answer matched.
//
//  ENTRY POINTS: every probe goes through the spawned CLI, the CLI with `--optimize` and `BasicCompiler.CompileProjectFiles` (aggressive), on every backend it runs on: `TempExec.AssertMatchesInEveryEntryPoint`.
//  ⛔ Every probe is `HangSafe`: its C# leg runs in a child process with a time limit (`CSharpProcessRunner`, #256), never the in-process runner — two probes hold a loop whose condition is the thing under
//  test, and a wrong carrier there is exactly the shape that turns an exit into a hang.
//  A backend whose tool is missing (a C++ compiler, Node, ilasm) SKIPS its cells: never a failure. The test is ignored only when no cell could run.
//  ⚠ Named "...ExecutionTests" and runs JavaScript under Node: it is in `JsExecutionTierRosterTests`' roster.
//
//  MUTANTS (each built for real from a plain copy of the fix and run against THESE tests; the cases and cells that go red, measured):
//    * M1 the binary-operator copy removed         -> `ABareFieldBeforeALaterCall_InAnOperatorExpression_IsReadFirst` (kplus, kmul, kcmp, kcat, kmebump, nested, cmpstr: C++, JavaScript, MSIL), and every other
//                                                     group whose hazard is a binary operator: shared, global / modqual, byref / byrefparam, whilecond / doloop, lambda (the same three backends; byref on C++ / MSIL)
//                                                     and iff (all four); plus the text test's `K + Bump()` control, which then emits no carrier.
//    * M2 a ByRef ARGUMENT copied like a value      -> `AByRefArgument_PassesTheStorage_NotACopy` ONLY: byrefarg prints 110 where VB prints 111, on C#, C++ and MSIL.
//    * M3 only the read's own block scanned         -> `ABareFieldBeforeAnIfOperand_IsReadFirst` ONLY: iff on all four backends (the call behind `If()` is in a block made after the read).
//    * M4 the callee's copy removed                 -> `ADelegateFieldCalledBare_IsReadBeforeItsArgument` ONLY: g2b and addrof print "new 1" on C++, JavaScript and MSIL (also the moved pin in `DelegateMemberInvocationExecutionTests`).
//    * M5 a plain field READ counted as a call      -> NOT here: the output does not change, only the emitted text does. `OperandEvaluationOrderTextTests` (fast) is the only case that goes red.
//
//  ⛔ KNOWN GAPS — listed, deliberately NOT tested (asserting one would pin a defect). Follow-up task #292. Each is outside what #203 fixes, and measured the same before and after it:
//    * COMPOUND ASSIGNMENT `K += Bump()` and `Me.K += Bump()`: IRBuilder evaluates the VALUE before it reads the TARGET, in every spelling, so C++, JavaScript and MSIL print 111 where VB prints 11
//      (S/t203/probes/kcompound, probes2/mecompound). C# is right.
//    * A LAMBDA-CAPTURED LOCAL that a later call writes (`x + bumpx()` with `bumpx` a lambda that assigns `x`): C++, JavaScript and MSIL print 106 where VB prints 6 (probes2/capt).
//    * A METHOD RECEIVER (`Items.Add(Bump())` where `Bump` reassigns `Items`): the receiver is read after the argument, so C++, JavaScript and MSIL add to the NEW list, "0,1" where VB prints "1,0"
//      (probes2/recv).
//    * Two cells are still excluded below: JavaScript refuses a ByRef parameter by design (BL7002), so `byrefarg` is not run there; MSIL refuses `String.Concat` (outside its static surface), so `netcall`
//      is not run there. (A third exclusion is GONE: C# refused a ByRef argument inside an expression, CS1620 — #232, fixed — so `byref` and `byrefparam` ran on C++ and MSIL only; they run on C# too now.
//      The mutant table above is #203's own, measured before that; M1's `byref` / `byrefparam` reds are the C++ / MSIL cells.)
// ================================================================================================

/// <summary>#203 — a bare field / global / ByRef operand is read BEFORE a later operand's call, as VB reads it, on every backend.</summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the g++, Node, ilasm and C# child runs share the machine with the spawned CLI
public class OperandEvaluationOrderExecutionTests
{
    /// <summary>
    /// One group of single-file probes, each on every backend it runs on, through every entry point. A failing cell is collected, not thrown, so every other one still reports and the failure text names
    /// the probe, the backend and the entry point. (A copy of the helper `UserDelegateGapsExecutionTests` keeps: each fixture owns its own.)
    /// </summary>
    private static void AssertSingleFile(params TempProbe[] probes)
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

    /// <summary>
    /// (1) A bare field to the LEFT of a call in the same operator expression: `K + Bump()`, `K * Bump()`, `If K &lt; Bump() + 50`, `K &amp; Bump()`, `K + Me.Bump()`, `K + (L + Bump())` and a String `Name = Rename()`.
    /// Each was WRONG on C++, JavaScript and MSIL (the field's new value was read). M1 removes the binary-operator copy.
    /// </summary>
    [Test]
    public void ABareFieldBeforeALaterCall_InAnOperatorExpression_IsReadFirst()
        => AssertSingleFile(OperandOrderProbes.Kplus, OperandOrderProbes.Kmul, OperandOrderProbes.Kcmp, OperandOrderProbes.Kcat, OperandOrderProbes.KMeBump, OperandOrderProbes.Nested, OperandOrderProbes.CmpStr);

    /// <summary>
    /// (2) A bare field as an ARGUMENT before a later argument's call: `F(K, Bump())`, a `ParamArray`, and `F(K, L, Bump())` (two earlier fields, which must keep their source order once both are copied).
    /// </summary>
    [Test]
    public void ABareFieldBeforeALaterArgumentsCall_IsReadFirst()
        => AssertSingleFile(OperandOrderProbes.FArgs, OperandOrderProbes.ParamArr, OperandOrderProbes.Siblings);

    /// <summary>(3) A Shared field, from an instance method and from a Shared one: `S + BumpS()`.</summary>
    [Test]
    public void ASharedField_BeforeALaterCall_IsReadFirst()
        => AssertSingleFile(OperandOrderProbes.Shared);

    /// <summary>(4) A module global, bare (`G + BumpG()`, `Pair(G, BumpG())`) and qualified (`Store.G + Store.BumpG()`).</summary>
    [Test]
    public void AModuleGlobal_BareAndQualified_IsReadBeforeALaterCall()
        => AssertSingleFile(OperandOrderProbes.Global, OperandOrderProbes.ModQual);

    /// <summary>
    /// (5) A local passed ByRef (`x + BumpLocalViaByRef(x)`) and a ByRef PARAMETER that aliases a global (`p + BumpG()`), on C#, C++ and MSIL. (C# joined with #232: the call sits inside an expression, and it was
    /// CS1620.) Not JavaScript (BL7002): see the header.
    /// </summary>
    [Test]
    public void AByRefLocalAndParameter_BeforeALaterCall_IsReadFirst()
        => AssertSingleFile(OperandOrderProbes.ByRefLocal, OperandOrderProbes.ByRefParam);

    /// <summary>
    /// (6) The OTHER side of the ByRef rule: `SetTo(K, Bump())` passes K's STORAGE to a ByRef parameter, so it must not be copied, and the write lands in the field: prints 111, not 110. M2 copies it like a value
    /// (the callee then writes the carrier). On C#, C++ and MSIL; JavaScript refuses the ByRef parameter (BL7002).
    /// </summary>
    [Test]
    public void AByRefArgument_PassesTheStorage_NotACopy()
        => AssertSingleFile(OperandOrderProbes.ByRefArg);

    /// <summary>
    /// (7) The call sits behind control flow: `K + If(flag, Bump(), 0)`. The `If()` is lowered to blocks made AFTER the read, so a scan of the read's own block alone misses it (M3). C# was WRONG here too, before.
    /// </summary>
    [Test]
    public void ABareFieldBeforeAnIfOperand_IsReadFirst()
        => AssertSingleFile(OperandOrderProbes.IfOperand);

    /// <summary>(8) A `While` and a `Do ... Loop While` condition, `K &lt; Bump() + 300`, re-evaluated on every pass (so the carrier is taken afresh each time).</summary>
    [Test]
    public void ABareFieldInALoopCondition_IsReadFirstOnEveryPass()
        => AssertSingleFile(OperandOrderProbes.WhileCond, OperandOrderProbes.DoLoop);

    /// <summary>(9) A constructor's arguments: `New Pair(K, Bump())`.</summary>
    [Test]
    public void ABareFieldBeforeAConstructorArgumentsCall_IsReadFirst()
        => AssertSingleFile(OperandOrderProbes.Ctor);

    /// <summary>(10) The same expression inside a lambda body: `Function() K + Bump()`.</summary>
    [Test]
    public void ABareFieldInALambdaBody_IsReadBeforeALaterCall()
        => AssertSingleFile(OperandOrderProbes.LambdaBody);

    /// <summary>
    /// (11) The CALLEE of a delegate-valued field, called bare: `Handler(Swap(1))` where `Swap` reassigns `Handler` (G2b, the shape #188 pinned as wrong), through `Me.` and through a receiver as the controls
    /// that were always right; and a bound `AddressOf` delegate replaced the same way. Both print the OLD handler. M4 removes the callee's copy (both print "new 1").
    /// </summary>
    [Test]
    public void ADelegateFieldCalledBare_IsReadBeforeItsArgument()
        => AssertSingleFile(OperandOrderProbes.G2b, OperandOrderProbes.AddrOf);

    /// <summary>(12) A .NET static call: `String.Concat(K, ",", Bump())`. C#, C++ and JavaScript; MSIL refuses `String.Concat` (see the header).</summary>
    [Test]
    public void ABareFieldBeforeADotNetCallsArguments_IsReadFirst()
        => AssertSingleFile(OperandOrderProbes.NetCall);

    /// <summary>
    /// (13) The CONTROLS, already right before #203 and required to stay right: `Bump() + K` (the call first), `Me.K + Bump()` (a Me-qualified read) and `K + 1 + L` (no call at all, so no carrier).
    /// </summary>
    [Test]
    public void TheSpellingsThatWereAlreadyRight_StayRight()
        => AssertSingleFile(OperandOrderProbes.BumpPlus, OperandOrderProbes.MeK, OperandOrderProbes.KOne);
}

/// <summary>
/// The #203 probes. Every source is the implementer's own (`S/t203/probes`, `probes2`) and every expected value is vbc's OWN output for it (see the fixture header). Each class has a `Bump()` that writes the
/// fields `K` and `L` (and reassigns `Items`), so a later operand that calls it is the hazard; the loops, the delegate and the ByRef probes carry their own writer.
/// </summary>
internal static class OperandOrderProbes
{
    /// <summary>Every probe is HangSafe (#256): its C# leg runs in a time-limited child process.</summary>
    private static TempProbe P(string id, string source, string vb, Bk agrees = Bk.All) => new(id, source, vb, agrees, HangSafe: true);

    /// <summary>`Return K + Bump()`: the left bare field is read before the call that adds 100 to it.</summary>
    internal static readonly TempProbe Kplus = P("kplus", """
        Class C
            Public K As Integer = 10
            Public Function Bump() As Integer
                K = K + 100
                Return 1
            End Function
            Public Function Go() As Integer
                Return K + Bump()
            End Function
        End Class

        Sub Main()
            Dim c As New C()
            Console.WriteLine(c.Go())
            Console.WriteLine(c.K)
        End Sub
        """, "11\n110");

    /// <summary>`Return K * Bump()`.</summary>
    internal static readonly TempProbe Kmul = P("kmul", """
        Class C
            Public K As Integer = 10
            Public Function Bump() As Integer
                K = K + 100
                Return 1
            End Function
            Public Function Go() As Integer
                Return K * Bump()
            End Function
        End Class

        Sub Main()
            Dim c As New C()
            Console.WriteLine(c.Go())
            Console.WriteLine(c.K)
        End Sub
        """, "10\n110");

    /// <summary>`If K &lt; Bump() + 50 Then`: a comparison, with the call inside a later arithmetic operand.</summary>
    internal static readonly TempProbe Kcmp = P("kcmp", """
        Class C
            Public K As Integer = 10
            Public Function Bump() As Integer
                K = K + 100
                Return 1
            End Function
            Public Function Go() As String
                If K < Bump() + 50 Then
                    Return "less"
                End If
                Return "notless"
            End Function
        End Class

        Sub Main()
            Dim c As New C()
            Console.WriteLine(c.Go())
            Console.WriteLine(c.K)
        End Sub
        """, "less\n110");

    /// <summary>`Console.WriteLine(K &amp; Bump())`: a concatenation.</summary>
    internal static readonly TempProbe Kcat = P("kcat", """
        Class C
            Public K As Integer = 10
            Public Function Bump() As Integer
                K = K + 100
                Return 1
            End Function
            Public Function Go() As String
                Console.WriteLine(K & Bump())
                Return "done"
            End Function
        End Class

        Sub Main()
            Dim c As New C()
            Console.WriteLine(c.Go())
            Console.WriteLine(c.K)
        End Sub
        """, "101\ndone\n110");

    /// <summary>`K + Me.Bump()`: the call is Me-qualified, the read is bare.</summary>
    internal static readonly TempProbe KMeBump = P("kmebump", """
        Class C
            Public K As Integer = 10
            Public Function Bump() As Integer
                K = K + 100
                Return 1
            End Function
            Public Function Go() As Integer
                Return K + Me.Bump()
            End Function
        End Class

        Sub Main()
            Dim c As New C()
            Console.WriteLine(c.Go())
            Console.WriteLine(c.K)
        End Sub
        """, "11\n110");

    /// <summary>`K + (L + Bump())`: the call is two operands deep, and L (read second) is also a field Bump writes.</summary>
    internal static readonly TempProbe Nested = P("nested", """
        Class C
            Public K As Integer = 10
            Public L As Integer = 20
            Public Items As List(Of Integer)
            Public Sub New()
                Items = New List(Of Integer)()
            End Sub
            Public Function Bump() As Integer
                K = K + 100
                L = L + 1000
                Items = New List(Of Integer)
                Return 1
            End Function
            Public Function Go() As Integer
                Return K + (L + Bump())
            End Function
        End Class

        Sub Main()
            Dim c As New C()
            Console.WriteLine(c.Go())
            Console.WriteLine(c.K & "," & c.L)
        End Sub
        """, "31\n110,1020");

    /// <summary>`Name = Rename()`: a String compare, which lowers through the String-equality path.</summary>
    internal static readonly TempProbe CmpStr = P("cmpstr", """
        Class C
            Public K As Integer = 10
            Public L As Integer = 20
            Public Items As List(Of Integer)
            Public Sub New()
                Items = New List(Of Integer)()
            End Sub
            Public Function Bump() As Integer
                K = K + 100
                L = L + 1000
                Items = New List(Of Integer)
                Return 1
            End Function
            Public Name As String = "a"
            Public Function Rename() As String
                Name = "z"
                Return "a"
            End Function
            Public Function Go() As Boolean
                Return Name = Rename()
            End Function
        End Class

        Sub Main()
            Dim c As New C()
            Console.WriteLine(c.Go())
            Console.WriteLine(c.K & "," & c.L)
        End Sub
        """, "True\n10,20");

    /// <summary>`F(K, Bump())`: an argument list.</summary>
    internal static readonly TempProbe FArgs = P("fargs", """
        Class C
            Public K As Integer = 10
            Public Function Bump() As Integer
                K = K + 100
                Return 1
            End Function
            Public Function F(a As Integer, b As Integer) As String
                Return a & "," & b
            End Function
            Public Function Go() As String
                Return F(K, Bump())
            End Function
        End Class

        Sub Main()
            Dim c As New C()
            Console.WriteLine(c.Go())
            Console.WriteLine(c.K)
        End Sub
        """, "10,1\n110");

    /// <summary>`Sum(K, Bump())` over a `ParamArray`.</summary>
    internal static readonly TempProbe ParamArr = P("paramarr", """
        Class C
            Public K As Integer = 10
            Public L As Integer = 20
            Public Items As List(Of Integer)
            Public Sub New()
                Items = New List(Of Integer)()
            End Sub
            Public Function Bump() As Integer
                K = K + 100
                L = L + 1000
                Items = New List(Of Integer)
                Return 1
            End Function
            Public Function Sum(ParamArray v() As Integer) As Integer
                Dim t As Integer = 0
                For Each x In v
                    t = t + x
                Next
                Return t
            End Function
            Public Function Go() As Integer
                Return Sum(K, Bump())
            End Function
        End Class

        Sub Main()
            Dim c As New C()
            Console.WriteLine(c.Go())
            Console.WriteLine(c.K & "," & c.L)
        End Sub
        """, "11\n110,1020");

    /// <summary>`F(K, L, Bump())`: two earlier arguments, both fields Bump writes, must keep their source order.</summary>
    internal static readonly TempProbe Siblings = P("siblings", """
        Class C
            Public K As Integer = 10
            Public L As Integer = 20
            Public Items As List(Of Integer)
            Public Sub New()
                Items = New List(Of Integer)()
            End Sub
            Public Function Bump() As Integer
                K = K + 100
                L = L + 1000
                Items = New List(Of Integer)
                Return 1
            End Function
            Public Function F(a As Integer, b As Integer, d As Integer) As String
                Return a & "," & b & "," & d
            End Function
            Public Function Go() As String
                Return F(K, L, Bump())
            End Function
        End Class

        Sub Main()
            Dim c As New C()
            Console.WriteLine(c.Go())
            Console.WriteLine(c.K & "," & c.L)
        End Sub
        """, "10,20,1\n110,1020");

    /// <summary>A Shared field, `S + BumpS()`, from an instance method and from a Shared one.</summary>
    internal static readonly TempProbe Shared = P("shared", """
        Class Counter
            Public Shared S As Integer = 10
            Public Shared Function BumpS() As Integer
                S = S + 100
                Return 1
            End Function
            Public Function Go() As Integer
                Return S + BumpS()
            End Function
            Public Shared Function GoS() As Integer
                Return S + BumpS()
            End Function
        End Class

        Sub Main()
            Dim obj As New Counter()
            Console.WriteLine(obj.Go())
            Console.WriteLine(Counter.GoS())
            Console.WriteLine(Counter.S)
        End Sub
        """, "11\n111\n210");

    /// <summary>A module global, bare: `G + BumpG()` and `Pair(G, BumpG())`.</summary>
    internal static readonly TempProbe Global = P("global", """
        Dim G As Integer = 10

        Function BumpG() As Integer
            G = G + 100
            Return 1
        End Function

        Function Pair(a As Integer, b As Integer) As String
            Return a & "," & b
        End Function

        Sub Main()
            Console.WriteLine(G + BumpG())
            Console.WriteLine(Pair(G, BumpG()))
            Console.WriteLine(G)
        End Sub
        """, "11\n110,1\n210");

    /// <summary>A Module's variable, qualified and bare: `Store.G + Store.BumpG()` and `G + BumpG()`.</summary>
    internal static readonly TempProbe ModQual = P("modqual", """
        Module Store
            Public G As Integer = 10
            Public Function BumpG() As Integer
                G = G + 100
                Return 1
            End Function
        End Module

        Sub Main()
            Console.WriteLine(Store.G + Store.BumpG())
            Console.WriteLine(G + BumpG())
            Console.WriteLine(Store.G)
        End Sub
        """, "11\n111\n210");

    /// <summary>A local passed ByRef to the callee: `x + BumpLocalViaByRef(x)`.</summary>
    internal static readonly TempProbe ByRefLocal = P("byref", """
        Function BumpLocalViaByRef(ByRef v As Integer) As Integer
            v = v + 100
            Return 1
        End Function

        Sub Main()
            Dim x As Integer = 10
            Console.WriteLine(x + BumpLocalViaByRef(x))
            Console.WriteLine(x)
        End Sub
        """, "11\n110", Bk.CSharp | Bk.Cpp | Bk.Msil);

    /// <summary>A ByRef PARAMETER aliasing a global: `p + BumpG()` inside `Work(ByRef p)`, called as `Work(G)`.</summary>
    internal static readonly TempProbe ByRefParam = P("byrefparam", """
        Dim G As Integer = 10

        Function BumpG() As Integer
            G = G + 100
            Return 1
        End Function

        Function Work(ByRef p As Integer) As Integer
            Return p + BumpG()
        End Function

        Sub Main()
            Console.WriteLine(Work(G))
            Console.WriteLine(G)
        End Sub
        """, "11\n110", Bk.CSharp | Bk.Cpp | Bk.Msil);

    /// <summary>`SetTo(K, Bump())` where `SetTo`'s first parameter is ByRef: the argument passes the STORAGE, so it must NOT be copied; prints 111, not 110.</summary>
    internal static readonly TempProbe ByRefArg = P("byrefarg", """
        Class C
            Public K As Integer = 10
            Public L As Integer = 20
            Public Items As List(Of Integer)
            Public Sub New()
                Items = New List(Of Integer)()
            End Sub
            Public Function Bump() As Integer
                K = K + 100
                L = L + 1000
                Items = New List(Of Integer)
                Return 1
            End Function
            Public Sub SetTo(ByRef a As Integer, v As Integer)
                a = a + v
            End Sub
            Public Sub Go()
                SetTo(K, Bump())
            End Sub
        End Class

        Sub Main()
            Dim c As New C()
            c.Go()
            Console.WriteLine(c.K & "," & c.L)
        End Sub
        """, "111,1020", Bk.CSharp | Bk.Cpp | Bk.Msil);

    /// <summary>`K + If(flag, Bump(), 0)`: the call is behind control flow, in blocks created after the read.</summary>
    internal static readonly TempProbe IfOperand = P("iff", """
        Class C
            Public K As Integer = 10
            Public L As Integer = 20
            Public Items As List(Of Integer)
            Public Sub New()
                Items = New List(Of Integer)()
            End Sub
            Public Function Bump() As Integer
                K = K + 100
                L = L + 1000
                Items = New List(Of Integer)
                Return 1
            End Function
            Public Function Go(flag As Boolean) As Integer
                Return K + If(flag, Bump(), 0)
            End Function
        End Class

        Sub Main()
            Dim c As New C()
            Console.WriteLine(c.Go(True))
            Console.WriteLine(c.Go(False))
            Console.WriteLine(c.K & "," & c.L)
        End Sub
        """, "11\n110\n110,1020");

    /// <summary>`While K &lt; Bump() + 300`: a loop condition, re-evaluated every pass.</summary>
    internal static readonly TempProbe WhileCond = P("whilecond", """
        Class C
            Public K As Integer = 10
            Public L As Integer = 20
            Public Items As List(Of Integer)
            Public Sub New()
                Items = New List(Of Integer)()
            End Sub
            Public Function Bump() As Integer
                K = K + 100
                L = L + 1000
                Items = New List(Of Integer)
                Return 1
            End Function
            Public Function Go() As Integer
                Dim n As Integer = 0
                While K < Bump() + 300
                    n = n + 1
                End While
                Return n
            End Function
        End Class

        Sub Main()
            Dim c As New C()
            Console.WriteLine(c.Go())
            Console.WriteLine(c.K & "," & c.L)
        End Sub
        """, "3\n410,4020");

    /// <summary>`Loop While K &lt; Bump() + 300`: a bottom-tested condition.</summary>
    internal static readonly TempProbe DoLoop = P("doloop", """
        Class C
            Public K As Integer = 10
            Public L As Integer = 20
            Public Items As List(Of Integer)
            Public Sub New()
                Items = New List(Of Integer)()
            End Sub
            Public Function Bump() As Integer
                K = K + 100
                L = L + 1000
                Items = New List(Of Integer)
                Return 1
            End Function
            Public Function Go() As Integer
                Dim n As Integer = 0
                Do
                    n = n + 1
                Loop While K < Bump() + 300
                Return n
            End Function
        End Class

        Sub Main()
            Dim c As New C()
            Console.WriteLine(c.Go())
            Console.WriteLine(c.K & "," & c.L)
        End Sub
        """, "4\n410,4020");

    /// <summary>`New Pair(K, Bump())`: a constructor's arguments.</summary>
    internal static readonly TempProbe Ctor = P("ctor", """
        Class Pair
            Public A As Integer
            Public B As Integer
            Public Sub New(x As Integer, y As Integer)
                A = x
                B = y
            End Sub
        End Class

        Class C
            Public K As Integer = 10
            Public L As Integer = 20
            Public Items As List(Of Integer)
            Public Sub New()
                Items = New List(Of Integer)()
            End Sub
            Public Function Bump() As Integer
                K = K + 100
                L = L + 1000
                Items = New List(Of Integer)
                Return 1
            End Function
            Public Function Go() As String
                Dim p As New Pair(K, Bump())
                Return p.A & "," & p.B
            End Function
        End Class

        Sub Main()
            Dim c As New C()
            Console.WriteLine(c.Go())
            Console.WriteLine(c.K & "," & c.L)
        End Sub
        """, "10,1\n110,1020");

    /// <summary>`Function() K + Bump()`: the expression inside a lambda body.</summary>
    internal static readonly TempProbe LambdaBody = P("lambda", """
        Class C
            Public K As Integer = 10
            Public L As Integer = 20
            Public Items As List(Of Integer)
            Public Sub New()
                Items = New List(Of Integer)()
            End Sub
            Public Function Bump() As Integer
                K = K + 100
                L = L + 1000
                Items = New List(Of Integer)
                Return 1
            End Function
            Public Function Go() As Integer
                Dim f As Func(Of Integer) = Function() K + Bump()
                Return f()
            End Function
        End Class

        Sub Main()
            Dim c As New C()
            Console.WriteLine(c.Go())
            Console.WriteLine(c.K & "," & c.L)
        End Sub
        """, "11\n110,1020");

    /// <summary>The delegate FIELD called bare (`Handler(Swap(1))`), through `Me.` and through a receiver: the callee's VALUE is taken before its argument reassigns the field.</summary>
    internal static readonly TempProbe G2b = P("g2b", """
        Class Bus
            Public Handler As Action(Of Integer)
            Public Function Swap(n As Integer) As Integer
                Handler = Sub(v As Integer) Console.WriteLine("new " & v)
                Return n
            End Function
            Public Sub Send()
                Handler(Swap(1))
            End Sub
            Public Sub SendMe()
                Me.Handler(Swap(2))
            End Sub
        End Class

        Sub Main()
            Dim b As New Bus()
            b.Handler = Sub(v As Integer) Console.WriteLine("old " & v)
            b.Send()
            b.Handler = Sub(v As Integer) Console.WriteLine("old " & v)
            b.SendMe()
            b.Handler = Sub(v As Integer) Console.WriteLine("old " & v)
            b.Handler(b.Swap(3))
        End Sub
        """, "old 1\nold 2\nold 3");

    /// <summary>`Handler = AddressOf Show` then `Handler(Swap(1))`: the callee's value is a bound AddressOf delegate that Swap replaces.</summary>
    internal static readonly TempProbe AddrOf = P("addrof", """
        Class C
            Public K As Integer = 10
            Public L As Integer = 20
            Public Items As List(Of Integer)
            Public Sub New()
                Items = New List(Of Integer)()
            End Sub
            Public Function Bump() As Integer
                K = K + 100
                L = L + 1000
                Items = New List(Of Integer)
                Return 1
            End Function
            Public Handler As Action(Of Integer)
            Public Tag As String = "inst"
            Public Sub Show(v As Integer)
                Console.WriteLine(Tag & " " & v)
            End Sub
            Public Function Swap(n As Integer) As Integer
                Handler = Sub(v As Integer) Console.WriteLine("new " & v)
                Return n
            End Function
            Public Sub Go()
                Handler = AddressOf Show
                Handler(Swap(1))
            End Sub
        End Class

        Sub Main()
            Dim c As New C()
            c.Go()
            Console.WriteLine(c.K & "," & c.L)
        End Sub
        """, "inst 1\n10,20");

    /// <summary>A .NET static call, `String.Concat(K, ",", Bump())`. Not MSIL: String.Concat is outside its static surface (a stated refusal).</summary>
    internal static readonly TempProbe NetCall = P("netcall", """
        Class C
            Public K As Integer = 10
            Public L As Integer = 20
            Public Items As List(Of Integer)
            Public Sub New()
                Items = New List(Of Integer)()
            End Sub
            Public Function Bump() As Integer
                K = K + 100
                L = L + 1000
                Items = New List(Of Integer)
                Return 1
            End Function
            Public Sub Go()
                Console.WriteLine(String.Concat(K, ",", Bump()))
            End Sub
        End Class

        Sub Main()
            Dim c As New C()
            c.Go()
            Console.WriteLine(c.K & "," & c.L)
        End Sub
        """, "10,1\n110,1020", Bk.CSharp | Bk.Cpp | Bk.JavaScript);

    /// <summary>CONTROL: `Bump() + K` (the call FIRST) was already right and stays right.</summary>
    internal static readonly TempProbe BumpPlus = P("bumpplus", """
        Class C
            Public K As Integer = 10
            Public Function Bump() As Integer
                K = K + 100
                Return 1
            End Function
            Public Function Go() As Integer
                Return Bump() + K
            End Function
        End Class

        Sub Main()
            Dim c As New C()
            Console.WriteLine(c.Go())
            Console.WriteLine(c.K)
        End Sub
        """, "111\n110");

    /// <summary>CONTROL: `Me.K + Bump()` (Me-qualified read) was already right and stays right.</summary>
    internal static readonly TempProbe MeK = P("mek", """
        Class C
            Public K As Integer = 10
            Public Function Bump() As Integer
                K = K + 100
                Return 1
            End Function
            Public Function Go() As Integer
                Return Me.K + Bump()
            End Function
        End Class

        Sub Main()
            Dim c As New C()
            Console.WriteLine(c.Go())
            Console.WriteLine(c.K)
        End Sub
        """, "11\n110");

    /// <summary>CONTROL: `K + 1 + L`, no call anywhere: nothing may change.</summary>
    internal static readonly TempProbe KOne = P("kone", """
        Class C
            Public K As Integer = 10
            Public Function Bump() As Integer
                K = K + 100
                Return 1
            End Function
            Public Function Go() As Integer
                Dim L As Integer = 3
                Return K + 1 + L
            End Function
        End Class

        Sub Main()
            Dim c As New C()
            Console.WriteLine(c.Go())
            Console.WriteLine(c.K)
        End Sub
        """, "14\n10");

}
