using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.CodeGen.JavaScript;
using BasicLang.Compiler.CodeGen.MSIL;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using NUnit.Framework;
using VisualGameStudio.Tests.Msil;
using VisualGameStudio.Tests.Native;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>The four backends that print. LLVM has no console: see <see cref="TempExec.RunLlvm"/>.</summary>
[Flags]
public enum Bk
{
    CSharp = 1,
    Cpp = 2,
    JavaScript = 4,
    Msil = 8,
    All = CSharp | Cpp | JavaScript | Msil,
}

/// <summary>The three entry points every probe goes through (CLAUDE.md: "test both entry points",
/// "validate codegen through the CLI and the IR optimizer").</summary>
public enum EntryPoint
{
    /// <summary>The real <c>BasicLang &lt;file&gt; --target=…</c> binary, STANDARD passes.</summary>
    Cli,

    /// <summary>The real CLI with <c>--optimize</c>: the AGGRESSIVE passes.</summary>
    CliOptimize,

    /// <summary><c>BasicCompiler.CompileProjectFiles</c> with <c>OptimizeAggressive</c> — what a
    /// Release <c>.blproj</c> build and the IDE's build service both call.</summary>
    ProjectRelease,
}

/// <summary>
/// ⭐ ADR-0017 (#163): the probe programs. Every source is the implementer's own probe
/// (<c>S/t163/probes</c>, <c>probes2</c>, <c>witness</c>, <c>rerun-ctl</c>); every expected value
/// is VB's OWN answer — re-checked by the test-writer by compiling each source, wrapped in a VB
/// <c>Module</c>, with <c>vbc</c> and running it (see the header of
/// <see cref="CompilerTempExecutionTests"/>), never taken from a backend.
///
/// <para>The <see cref="Bk"/> flags name the backends that agree with VB on that source BOTH before
/// and after #163. A backend left out is a defect that predates #163 and is not this ADR's
/// (measured identical on the two builds): R4 on C# (<c>MyBase.New(v + 1)</c> is CS0103 there),
/// R6 and R10 on C# (the peephole's <c>x * 0</c> arm discards the operand, and C# never emitted
/// the orphan), R10 on C++ and JavaScript (the same), R9 everywhere.</para>
/// </summary>
public sealed record TempProbe(string Id, string Source, string Vb, Bk Agrees = Bk.All)
{
    public override string ToString() => Id;
}

internal static class TempProbes
{
    // ---------------------------------------------------------------------------------------------
    // The removal probes: an optimizer rewrite orphans a compiler temp, and DCE now deletes it.
    // ---------------------------------------------------------------------------------------------

    /// <summary>`-(-a)` and `Not (Not b)`: the peephole forwards the outer op to the inner operand and
    /// leaves the inner op behind. 1 `Neg` + 1 `Not` removed.</summary>
    internal static readonly TempProbe R1 = new("R1_NegNeg", """
        Sub Show(n As Integer)
            Console.WriteLine(CStr(n))
        End Sub

        Sub ShowB(b As Boolean)
            Console.WriteLine(CStr(b))
        End Sub

        Sub Run(a As Integer, b As Boolean)
            Show(-(-a))
            ShowB(Not (Not b))
        End Sub

        Sub Main()
            Run(7, True)
        End Sub
        """, "7\nTrue");

    /// <summary>`(a * 2) * 0`: strength reduction REPLACES the multiply (a shift, through
    /// `InheritIdentity`), then the peephole's `x * 0` arm orphans the replacement. 1 `Shl` removed —
    /// which needs the flag to have survived the replacement (mutant M2).</summary>
    internal static readonly TempProbe R2 = new("R2_MulZero", """
        Sub Show(n As Integer)
            Console.WriteLine(CStr(n))
        End Sub

        Sub Run(a As Integer)
            Show((a * 2) * 0)
            Show((a * 4) + 0)
        End Sub

        Sub Main()
            Run(7)
        End Sub
        """, "0\n28");

    /// <summary>Three `Not (Not …)` over a compare, an `Is Nothing` and an equality: 3 `Not` removed
    /// (IRCompare and IRIdentityCompare operands).</summary>
    internal static readonly TempProbe R8 = new("R8_Compare", """
        Class Node
            Public V As Integer
        End Class

        Sub ShowB(b As Boolean)
            Console.WriteLine(CStr(b))
        End Sub

        Sub Run(a As Integer, n As Node)
            ShowB(Not (Not (a > 3)))
            ShowB(Not (Not (n Is Nothing)))
            ShowB(Not (Not (a = 7)))
        End Sub

        Sub Main()
            Run(7, Nothing)
            Run(1, New Node())
        End Sub
        """, "True\nTrue\nTrue\nFalse\nFalse\nFalse");

    // ---------------------------------------------------------------------------------------------
    // Kept by KIND or by a USE: a marked temp that must survive.
    // ---------------------------------------------------------------------------------------------

    /// <summary>`Tag() * 0`: the peephole orphans the multiply, whose ONLY operand use of the call is
    /// that multiply. Deleting it would take the call from one use to none (C# would emit it as a
    /// statement instead of inlining it): ADR-0008 settled point 3, the twin case. Both `tag` prints
    /// must survive.</summary>
    internal static readonly TempProbe R3 = new("R3_CallMulZero", """
        Function Tag() As Integer
            Console.WriteLine("tag")
            Return 5
        End Function

        Sub Show(n As Integer)
            Console.WriteLine(CStr(n))
        End Sub

        Sub Main()
            Show(Tag() * 0)
            Show(-(-Tag()))
        End Sub
        """, "tag\n0\ntag\n5");

    /// <summary>`MyBase.New(v + 1)`: the temp's only use is an `IRBaseConstructorCall` operand (#170).
    /// C# is out: `CS0103` on both builds.</summary>
    internal static readonly TempProbe R4 = new("R4_MyBaseArgument", """
        Class Base
            Public V As Integer
            Sub New(n As Integer)
                V = n
            End Sub
        End Class

        Class Derived
            Inherits Base
            Sub New(v As Integer)
                MyBase.New(v + 1)
            End Sub
        End Class

        Sub Main()
            Dim d As New Derived(4)
            Console.WriteLine(CStr(d.V))
        End Sub
        """, "5", Bk.Cpp | Bk.JavaScript | Bk.Msil);

    /// <summary>The settled-point-3 hazard, built to hit it: `Show2(-(-F()), G())` leaves `F()`'s temp
    /// with two operand uses (the dead inner `-`, and the call) and `G()` between it and the call.
    /// Deleting the dead `-` would leave `F()` single-use AND away from its definition. The Trail
    /// proves the call ORDER (F, G, F).</summary>
    internal static readonly TempProbe R5 = new("R5_SettledPoint3", """
        Dim Trail As String = ""

        Function F() As Integer
            Trail = Trail & "F"
            Return 3
        End Function

        Function G() As Integer
            Trail = Trail & "G"
            Return 4
        End Function

        Sub Show2(a As Integer, b As Integer)
            Console.WriteLine(CStr(a) & "," & CStr(b))
        End Sub

        Sub Main()
            Show2(-(-F()), G())
            Dim x As Integer = -(-F())
            Console.WriteLine(CStr(x))
            Console.WriteLine(Trail)
        End Sub
        """, "3,4\n3\nFGF");

    /// <summary>`(a \ z) * 0` with z = 0: the orphaned multiply may go, the division may NOT, because
    /// an integer division by zero throws. C# is out (it never emitted the orphan, so it printed `0`
    /// on both builds).</summary>
    internal static readonly TempProbe R6 = new("R6_DivideByZero", """
        Sub Show(n As Integer)
            Console.WriteLine(CStr(n))
        End Sub

        Sub Run(a As Integer, z As Integer)
            Try
                Show((a \ z) * 0)
            Catch ex As Exception
                Console.WriteLine("caught")
            End Try
        End Sub

        Sub Main()
            Run(7, 0)
        End Sub
        """, "caught", Bk.Cpp | Bk.JavaScript | Bk.Msil);

    /// <summary>ADR-0017 Finding 2, the dead-store cascade: with the orphan `(a * 3)` gone, the two
    /// stores to `n` become adjacent and the peephole's existing dead-store arm drops the first.</summary>
    internal static readonly TempProbe R7 = new("R7_DeadStoreCascade", """
        Sub Run(a As Integer)
            Dim n As Integer = 0
            n = (a * 3) * 0
            Console.WriteLine(CStr(n))
            Console.WriteLine(CStr(-(-(a + 1))))
            Console.WriteLine(CStr(a))
        End Sub

        Sub Main()
            Run(7)
        End Sub
        """, "0\n8\n7");

    /// <summary>R7 for LLVM, which has no console: the same statements, the answer carried out as the
    /// process exit code (8 * 16 + 7 + 0 = 135) through a two-line C shim (<see cref="TempExec.RunLlvm"/>).
    /// VB's own exit code, measured with `vbc`, is 135.</summary>
    internal const string R7ForLlvm = """
        Function Run(a As Integer) As Integer
            Dim n As Integer = 0
            n = (a * 3) * 0
            Return (-(-(a + 1))) * 16 + a + n
        End Function

        Function Main() As Integer
            Return Run(7)
        End Function
        """;

    internal const int R7ForLlvmExitCode = 135;

    /// <summary>`(++a) * 0`: `++` WRITES its operand, so the unary is never removable. No VB
    /// equivalent (VB has no `++`), and every backend already misprints it: an IR-level probe only.</summary>
    internal const string R9Source = """
        Sub Show(n As Integer)
            Console.WriteLine(CStr(n))
        End Sub

        Sub Run(a As Integer)
            Show((++a) * 0)
            Show(a)
        End Sub

        Sub Main()
            Run(7)
        End Sub
        """;

    /// <summary>`arr(i) * 0` with an out-of-range index: an element read can trap. MSIL keeps the read
    /// (`caught`); C#, C++ and JavaScript already dropped it before #163.</summary>
    internal static readonly TempProbe R10 = new("R10_ElementRead", """
        Sub Show(n As Integer)
            Console.WriteLine(CStr(n))
        End Sub

        Sub Run(arr() As Integer, i As Integer)
            Try
                Show(arr(i) * 0)
            Catch ex As Exception
                Console.WriteLine("caught")
            End Try
        End Sub

        Sub Main()
            Dim a(2) As Integer
            Run(a, 1)
            Run(a, 5)
        End Sub
        """, "0\ncaught", Bk.Msil);

    // ---------------------------------------------------------------------------------------------
    // The U-spellings: USER storage spelled like a temp. Never removed, VB's output everywhere.
    // ---------------------------------------------------------------------------------------------

    /// <summary>`Dim t5 = a + b`: #118 measured this as 12 for 82 in 12 of 12 cells under a spelling guard.</summary>
    internal static readonly TempProbe U1 = new("U1_LocalT5", """
        Function F(a As Integer, b As Integer) As Integer
            Dim t5 As Integer = a + b
            Dim T6 As Integer = a * b
            Return t5 * 10 + T6
        End Function

        Sub Main()
            Console.WriteLine(CStr(F(3, 4)))
        End Sub
        """, "82");

    /// <summary>⭐ `_tmp1`: BEFORE #163 this printed `0` for VB's `67` in 12 of 12 cells. The old guard
    /// removed every unused value whose name STARTED with `_tmp`, and `Dim _tmp1 = a + b` is one (its
    /// later reads are reads of the variable, so the renamed binop has no operand use). A fix to a
    /// bug that was live, not latent.</summary>
    internal static readonly TempProbe U2 = new("U2_LocalTmp1", """
        Function F(a As Integer, b As Integer) As Integer
            Dim _tmp1 As Integer = a + b
            Dim _tmp2 As Integer = -a
            Return _tmp1 * 10 + _tmp2
        End Function

        Sub Main()
            Console.WriteLine(CStr(F(3, 4)))
        End Sub
        """, "67");

    internal static readonly TempProbe U3 = new("U3_LocalUpperT5", """
        Function F(a As Integer, b As Integer) As Integer
            Dim T5 As Integer = a + b
            Dim T0 As Boolean = Not (a > b)
            If T0 Then Return T5 * 10
            Return T5
        End Function

        Sub Main()
            Console.WriteLine(CStr(F(3, 4)))
        End Sub
        """, "70");

    /// <summary>`_t3` and `_t0`: the historical `_t` spelling, with an orphan (`-(-a)`) beside it that
    /// IS a compiler temp and IS removed.</summary>
    internal static readonly TempProbe U4 = new("U4_LocalUnderscoreT", """
        Function F(a As Integer, b As Integer) As Integer
            Dim _t3 As Integer = a - b
            Dim _t0 As Integer = -(-a)
            Return _t3 * 10 + _t0
        End Function

        Sub Main()
            Console.WriteLine(CStr(F(9, 4)))
        End Sub
        """, "59");

    /// <summary>Fields named `t0` and `t1`, written from expressions with an orphan (`-(-n)`).</summary>
    internal static readonly TempProbe U5 = new("U5_FieldsT0T1", """
        Class Box
            Public t0 As Integer
            Public t1 As Integer
            Sub Set1(n As Integer)
                t0 = n + 0
                t1 = -(-n)
            End Sub
            Function Total() As Integer
                Return t0 * 10 + t1
            End Function
        End Class

        Sub Main()
            Dim b As New Box()
            b.Set1(5)
            Console.WriteLine(CStr(b.Total()))
        End Sub
        """, "55");

    internal static readonly TempProbe U6 = new("U6_GlobalT3", """
        Module G
            Public t3 As Integer = 0
        End Module

        Sub Setup(n As Integer)
            t3 = n + 7
        End Sub

        Sub Main()
            Setup(5)
            Console.WriteLine(CStr(t3))
        End Sub
        """, "12");

    /// <summary>Three unused user variables spelled like temps: none of them is removable, and `Seen`
    /// proves the function ran.</summary>
    internal static readonly TempProbe U7 = new("U7_UnusedUserTemps", """
        Dim Seen As Integer = 0

        Function F(a As Integer, b As Integer) As Integer
            Dim t1 As Integer = a + b
            Dim t2 As Integer = -a
            Dim t9 As Boolean = a < b
            Seen = Seen + 1
            Return a
        End Function

        Sub Main()
            Console.WriteLine(CStr(F(3, 4)))
            Console.WriteLine(CStr(Seen))
        End Sub
        """, "3\n1");

    internal static readonly TempProbe U8 = new("U8_LoopAccumulatorT1", """
        Function Sum(n As Integer) As Integer
            Dim t1 As Integer = 0
            For i As Integer = 1 To n
                t1 = t1 + i
            Next
            Return t1
        End Function

        Sub Main()
            Console.WriteLine(CStr(Sum(4)))
        End Sub
        """, "10");

    // ---------------------------------------------------------------------------------------------
    // The by-name rule's witness and the #121 collision family: a user variable that IRBuilder does
    // not reserve (a For Each, Catch, pattern or LINQ range variable) spelled like a temp.
    // ---------------------------------------------------------------------------------------------

    /// <summary>⭐ THE WITNESS, `CT_wbr_t0`. `Catch t0` with orphans BEFORE the Try. Without D2's
    /// by-name rule (mutant M8) the orphan `t0 = -a` goes, the C++ backend's own temp counter
    /// renumbers, and the surviving string temp becomes `t0` inside the catch:
    /// `t0 = BasicLang::String(t0.what())`, "no viable overloaded '='". OK before #163, OK with the
    /// rule, COMPILE-FAIL without it, on C++ in all three entry points.</summary>
    internal static readonly TempProbe CtWbrT0 = new("CT_wbr_t0", """
        Sub Show(n As Integer)
            Console.WriteLine(CStr(n))
        End Sub

        Sub Run(a As Integer)
            Show(-(-a))
            Show((a * 2) * 0 + a)
            Try
                Throw New Exception("x")
            Catch t0 As Exception
                Show(-(-a))
                Console.WriteLine(t0.Message)
            End Try
        End Sub

        Sub Main()
            Run(5)
        End Sub
        """, "5\n5\n5\nx");

    /// <summary>The control for the witness: the same program with an ordinary name.</summary>
    internal static readonly TempProbe CtWbrK = new("CT_wbr_k", """
        Sub Show(n As Integer)
            Console.WriteLine(CStr(n))
        End Sub

        Sub Run(a As Integer)
            Show(-(-a))
            Show((a * 2) * 0 + a)
            Try
                Throw New Exception("x")
            Catch k As Exception
                Show(-(-a))
                Console.WriteLine(k.Message)
            End Try
        End Sub

        Sub Main()
            Run(5)
        End Sub
        """, "5\n5\n5\nx");

    /// <summary>R11: two For Each loops whose variables are `t0` and `t1`. VB: `3 | 4 | 3 | 4`. Every
    /// backend was already wrong (C# `-3|-4|3|4`, C++ `-3|-4|6|8`, MSIL `-3|-4|3|4`, JavaScript a TDZ
    /// `ReferenceError`). The by-name rule holds it there; #121 fixes it.</summary>
    internal static readonly TempProbe R11 = new("R11_ForEachT0", """
        Sub Show(n As Integer)
            Console.WriteLine(CStr(n))
        End Sub

        Sub Run(lst As List(Of Integer))
            For Each t0 As Integer In lst
                Show(-(-t0))
            Next
            For Each t1 As Integer In lst
                Show((t1 * 2) * 0 + t1)
            Next
        End Sub

        Sub Main()
            Dim lst As New List(Of Integer)
            lst.Add(3)
            lst.Add(4)
            Run(lst)
        End Sub
        """, "3\n4\n3\n4");

    internal static readonly TempProbe R11Control = new("R11_ForEachYZ", """
        Sub Show(n As Integer)
            Console.WriteLine(CStr(n))
        End Sub

        Sub Run(lst As List(Of Integer))
            For Each y As Integer In lst
                Show(-(-y))
            Next
            For Each z As Integer In lst
                Show((z * 2) * 0 + z)
            Next
        End Sub

        Sub Main()
            Dim lst As New List(Of Integer)
            lst.Add(3)
            lst.Add(4)
            Run(lst)
        End Sub
        """, "3\n4\n3\n4");

    /// <summary>`LC_t0`: a For Each variable `t0` CAPTURED by a lambda. ⚠ The one change of #163 that is
    /// not toward VB: MSIL WRONG (`7|-3|7|-4`) before, RUN-FAIL after. See the fence.</summary>
    internal static readonly TempProbe LcT0 = new("LC_t0", LambdaCapture("t0"), "7\n3\n7\n4");

    internal static readonly TempProbe LcT1 = new("LC_t1", LambdaCapture("t1"), "7\n3\n7\n4");
    internal static readonly TempProbe LcT3 = new("LC_t3", LambdaCapture("t3"), "7\n3\n7\n4");
    internal static readonly TempProbe LcControl = new("LC_x", LambdaCapture("x"), "7\n3\n7\n4");

    private static string LambdaCapture(string variable) => $$"""
        Sub Show(n As Integer)
            Console.WriteLine(CStr(n))
        End Sub

        Sub Run(lst As List(Of Integer), a As Integer)
            For Each {{variable}} As Integer In lst
                Dim f As Func(Of Integer) = Function() -(-{{variable}}) + (({{variable}} * 2) * 0)
                Show(-(-a))
                Show(f())
            Next
        End Sub

        Sub Main()
            Dim lst As New List(Of Integer)
            lst.Add(3)
            lst.Add(4)
            Run(lst, 7)
        End Sub
        """;

    /// <summary>`Catch tN`, the read BEFORE the colliding write ("rbw": the catch variable is read first).</summary>
    internal static TempProbe CatchReadBeforeWrite(string variable) => new($"CT_rbw_{variable}", $$"""
        Sub Show(n As Integer)
            Console.WriteLine(CStr(n))
        End Sub

        Sub Run(a As Integer)
            Try
                Throw New Exception("x")
            Catch {{variable}} As Exception
                Console.WriteLine({{variable}}.Message)
                Show(-(-a))
                Show((a * 2) * 0 + a)
            End Try
        End Sub

        Sub Main()
            Run(5)
        End Sub
        """, "x\n5\n5");

    /// <summary>`Catch tN`, orphans BEFORE the Try ("wbr": written before read) — CT_wbr_t0 is this at t0.</summary>
    internal static TempProbe CatchWriteBeforeRead(string variable) => new($"CT_wbr_{variable}", $$"""
        Sub Show(n As Integer)
            Console.WriteLine(CStr(n))
        End Sub

        Sub Run(a As Integer)
            Show(-(-a))
            Show((a * 2) * 0 + a)
            Try
                Throw New Exception("x")
            Catch {{variable}} As Exception
                Show(-(-a))
                Console.WriteLine({{variable}}.Message)
            End Try
        End Sub

        Sub Main()
            Run(5)
        End Sub
        """, "5\n5\n5\nx");

    /// <summary>A For Each variable spelled like a temp, read BEFORE the orphans ("rbw").</summary>
    internal static TempProbe ForEachReadBeforeWrite(string variable) => new($"FE_rbw_{variable}", $$"""
        Sub Show(n As Integer)
            Console.WriteLine(CStr(n))
        End Sub

        Sub Run(lst As List(Of Integer), a As Integer)
            For Each {{variable}} As Integer In lst
                Show({{variable}})
                Show(-(-a))
                Show(-(-{{variable}}))
            Next
        End Sub

        Sub Main()
            Dim lst As New List(Of Integer)
            lst.Add(3)
            lst.Add(4)
            Run(lst, 7)
        End Sub
        """, "3\n7\n3\n4\n7\n4");

    /// <summary>A For Each variable spelled like a temp, orphans BEFORE the loop ("wbr").</summary>
    internal static TempProbe ForEachWriteBeforeRead(string variable) => new($"FE_wbr_{variable}", $$"""
        Sub Show(n As Integer)
            Console.WriteLine(CStr(n))
        End Sub

        Sub Run(lst As List(Of Integer), a As Integer)
            Show(-(-a))
            Show((a * 2) * 0 + a)
            For Each {{variable}} As Integer In lst
                Show(-(-a))
                Show({{variable}})
            Next
        End Sub

        Sub Main()
            Dim lst As New List(Of Integer)
            lst.Add(3)
            lst.Add(4)
            Run(lst, 7)
        End Sub
        """, "7\n7\n7\n3\n7\n4");
}

/// <summary>
/// The shared plumbing every #163 test file uses: build IR, build a pipeline with the dead-code
/// pass replaced or removed, record what it deletes, and take a program through each entry point
/// and backend to a process.
/// </summary>
internal static class TempIr
{
    /// <summary>Front end → IR, with no optimizer. Asserts on parse and semantic errors.</summary>
    internal static IRModule Build(string source) => JsTestSupport.BuildModule(source);

    /// <summary>Front end → IR without asserting: null when the text is not a program this front end accepts.</summary>
    internal static IRModule TryBuild(string source)
    {
        try
        {
            var parser = new Parser(new Lexer(source).Tokenize());
            var ast = parser.Parse();
            if (parser.Errors.Count > 0) return null;
            var analyzer = new BasicLang.Compiler.SemanticAnalysis.SemanticAnalyzer();
            if (!analyzer.Analyze(ast)) return null;
            return new IRBuilder(analyzer).Build(ast, "TestModule");
        }
        catch (Exception)
        {
            return null;
        }
    }

    internal static List<IRFunction> Functions(IRModule module) => IRTempNames.AllFunctions(module);

    /// <summary>Every instruction of every block of <paramref name="function"/>.</summary>
    internal static IEnumerable<IRInstruction> Instructions(IRFunction function)
        => function.Blocks.SelectMany(b => b.Instructions).Where(i => i != null);

    /// <summary>Every value reachable from the body: block instructions and their operand trees.</summary>
    internal static List<IRValue> Reachable(IRFunction function)
    {
        var values = new List<IRValue>();
        var seen = new HashSet<IRInstruction>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<IRInstruction>(Instructions(function).Reverse());
        while (pending.Count > 0)
        {
            var inst = pending.Pop();
            if (inst == null || !seen.Add(inst)) continue;
            if (inst is IRValue v) values.Add(v);
            foreach (var operand in IROperandWalker.EnumerateOperands(inst)) pending.Push(operand);
        }
        return values;
    }

    /// <summary>The operand values reachable from the block instructions: what is USED by identity.</summary>
    internal static HashSet<IRValue> Used(IRFunction function)
    {
        var used = new HashSet<IRValue>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<IRInstruction>();
        foreach (var inst in Instructions(function))
            foreach (var operand in IROperandWalker.EnumerateOperands(inst)) pending.Push(operand);
        while (pending.Count > 0)
        {
            var inst = pending.Pop();
            if (inst is not IRValue v || !used.Add(v)) continue;
            foreach (var operand in IROperandWalker.EnumerateOperands(inst)) pending.Push(operand);
        }
        return used;
    }

    /// <summary>
    /// Unused compiler temps of the pure kinds a peephole rewrite orphans (binary, unary, compare,
    /// `Is`): minted names, not storage the program declared, and no operand of anything. Deliberately
    /// keyed on the NAME's provenance (<see cref="IRFunction.IsMintedTempName"/>) and NOT on the
    /// flag, so a flag lost in an optimizer replacement leaves the orphan standing and this sees it.
    /// </summary>
    internal static List<IRValue> PureOrphans(IRModule module)
    {
        var orphans = new List<IRValue>();
        foreach (var function in Functions(module))
        {
            var used = Used(function);
            foreach (var inst in Instructions(function))
            {
                if (inst is not IRValue v || v is IRVariable || v is IRConstant || v.NamedAfterVariable) continue;
                if (v is not (IRBinaryOp or IRUnaryOp or IRCompare or IRIdentityCompare)) continue;
                if (!function.IsMintedTempName(v.Name) || used.Contains(v)) continue;
                orphans.Add(v);
            }
        }
        return orphans;
    }

    /// <summary>Front end → the STANDARD or AGGRESSIVE passes, the dead-code pass as it ships.</summary>
    internal static IRModule Optimized(string source, bool aggressive)
    {
        var module = Build(source);
        Pipeline(aggressive).Run(module);
        return module;
    }

    /// <summary>The shipped pipeline, untouched.</summary>
    internal static OptimizationPipeline Pipeline(bool aggressive)
    {
        var pipeline = new OptimizationPipeline();
        if (aggressive) pipeline.AddAggressivePasses(); else pipeline.AddStandardPasses();
        return pipeline;
    }

    private static List<OptimizationPass> PassesOf(OptimizationPipeline pipeline)
    {
        var field = typeof(OptimizationPipeline).GetField("_passes", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, "OptimizationPipeline no longer keeps its passes in `_passes`: update TempIr.PassesOf.");
        return (List<OptimizationPass>)field!.GetValue(pipeline)!;
    }

    /// <summary>The shipped pipeline with its ONE dead-code pass taken out: what the optimizer leaves behind
    /// when nothing deletes an orphan. Asserts there is exactly one such pass, so a change to the pipeline
    /// fails loudly instead of silently testing something else.</summary>
    internal static OptimizationPipeline PipelineWithoutDce(bool aggressive)
    {
        var pipeline = Pipeline(aggressive);
        var passes = PassesOf(pipeline);
        Assert.That(passes.Count(p => p is DeadCodeEliminationPass), Is.EqualTo(1), "expected exactly one DeadCodeEliminationPass");
        passes.RemoveAll(p => p is DeadCodeEliminationPass);
        return pipeline;
    }

    /// <summary>The shipped pipeline with its dead-code pass replaced by <paramref name="recorder"/>, which IS a
    /// DeadCodeEliminationPass and deletes exactly what it deletes.</summary>
    internal static OptimizationPipeline PipelineRecording(bool aggressive, RecordingDeadCodePass recorder)
    {
        var pipeline = Pipeline(aggressive);
        var passes = PassesOf(pipeline);
        var index = passes.FindIndex(p => p is DeadCodeEliminationPass);
        Assert.That(passes.Count(p => p is DeadCodeEliminationPass), Is.EqualTo(1), "expected exactly one DeadCodeEliminationPass");
        passes[index] = recorder;
        return pipeline;
    }

    /// <summary>Runs the shipped pipeline over <paramref name="module"/> and returns what DCE deleted.</summary>
    internal static RecordingDeadCodePass RunRecording(IRModule module, bool aggressive)
    {
        var recorder = new RecordingDeadCodePass();
        PipelineRecording(aggressive, recorder).Run(module);
        return recorder;
    }
}

/// <summary>
/// A <see cref="DeadCodeEliminationPass"/> that writes down every instruction it deletes: which value,
/// what kind, whether it carried the marker, and what its operands' operand-use counts were before and
/// after the run. It deletes exactly what the pass it derives from deletes.
/// </summary>
internal sealed class RecordingDeadCodePass : DeadCodeEliminationPass
{
    internal sealed record Removal(IRFunction Function, IRValue Value, string Kind, IRValue[] Operands);

    internal sealed record OperandCount(IRValue Operand, int Before, int After);

    internal List<Removal> Removals { get; } = new();

    /// <summary>For every non-variable, non-constant operand of a deleted value: its operand-use count in the
    /// blocks before the run and after it.</summary>
    internal List<OperandCount> OperandCounts { get; } = new();

    public override bool Run(IRModule module)
    {
        var before = new List<(IRFunction Function, BasicBlock Block, IRInstruction Inst)>();
        var countsBefore = new Dictionary<IRFunction, Dictionary<IRValue, int>>();
        foreach (var function in TempIr.Functions(module))
        {
            foreach (var block in function.Blocks)
                foreach (var inst in block.Instructions.Where(i => i != null))
                    before.Add((function, block, inst));
            countsBefore[function] = UseCounts(function);
        }

        var changed = base.Run(module);

        foreach (var (function, block, inst) in before)
        {
            if (!function.Blocks.Contains(block)) continue; // an unreachable BLOCK went, not an instruction
            if (block.Instructions.Any(i => ReferenceEquals(i, inst))) continue;
            if (inst is not IRValue value) continue;
            var operands = UsesOf(value).ToArray();
            Removals.Add(new Removal(function, value, Describe(value), operands));

            var countsAfter = UseCounts(function);
            foreach (var operand in operands.Distinct())
            {
                if (operand is IRVariable || operand is IRConstant) continue;
                OperandCounts.Add(new OperandCount(operand,
                    countsBefore[function].TryGetValue(operand, out var b) ? b : 0,
                    countsAfter.TryGetValue(operand, out var a) ? a : 0));
            }
        }
        return changed;
    }

    /// <summary>The operand slots of the function's block instructions holding each value.</summary>
    internal static Dictionary<IRValue, int> UseCounts(IRFunction function)
    {
        var counts = new Dictionary<IRValue, int>(ReferenceEqualityComparer.Instance);
        foreach (var inst in TempIr.Instructions(function))
            foreach (var operand in UsesOf(inst))
                counts[operand] = counts.TryGetValue(operand, out var n) ? n + 1 : 1;
        return counts;
    }

    internal static string Describe(IRValue value) => value switch
    {
        IRBinaryOp b => "Binary." + b.Operation,
        IRUnaryOp u => "Unary." + u.Operation,
        IRCompare => "Compare",
        IRIdentityCompare => "IdentityCompare",
        IRLoad => "Load",
        _ => value.GetType().Name,
    };

    /// <summary>"Neg=4 Not=4 …" — the removals counted by kind, sorted, for a stable assertion message.</summary>
    internal static string KindCounts(IEnumerable<Removal> removals)
        => string.Join(" ", removals.GroupBy(r => r.Kind).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key}={g.Count()}"));
}

/// <summary>
/// One program through one entry point and one backend to a process — the plumbing of the
/// execution tier. The CLI legs run the REAL deployed <c>BasicLang</c> binary
/// (<see cref="CliTestHarness"/>), reading the file it wrote and running THAT; the project leg is
/// <c>BasicCompiler.CompileProjectFiles</c> in process. Copied in shape from the #170 fixture
/// (`BaseConstructorCallLoweringExecutionTests`), which each fixture that needs it keeps a copy of.
/// </summary>
internal static class TempExec
{
    internal static string Norm(string s) => FourBackends.Norm(s);

    internal static IEnumerable<Bk> Backends(Bk flags)
        => new[] { Bk.CSharp, Bk.Cpp, Bk.JavaScript, Bk.Msil }.Where(b => flags.HasFlag(b));

    internal static string Extension(Bk backend) => backend switch
    {
        Bk.CSharp => ".cs",
        Bk.Cpp => ".cpp",
        Bk.JavaScript => ".js",
        Bk.Msil => ".il",
        _ => throw new ArgumentException(backend.ToString()),
    };

    internal static string TargetName(Bk backend) => backend switch
    {
        Bk.CSharp => "csharp",
        Bk.Cpp => "cpp",
        Bk.JavaScript => "javascript",
        Bk.Msil => "msil",
        _ => throw new ArgumentException(backend.ToString()),
    };

    /// <summary>The generated text: from the spawned CLI (standard or <c>--optimize</c>) or from
    /// <c>CompileProjectFiles</c> (aggressive). Asserts the compile succeeded.</summary>
    internal static string Emit(Bk backend, EntryPoint entry, string source)
    {
        if (entry == EntryPoint.ProjectRelease) return EmitViaProject(backend, source);

        var dir = Path.Combine(Path.GetTempPath(), "bl-t163-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Prog.bas"), source);
            var args = new List<string> { "Prog.bas", "--target=" + TargetName(backend) };
            if (entry == EntryPoint.CliOptimize) args.Add("--optimize");
            var (exit, stdout, stderr) = CliTestHarness.RunProcess(CliTestHarness.CliPath(), args.ToArray(), dir, timeoutMs: 120_000);
            var path = Path.Combine(dir, "Prog" + Extension(backend));
            Assert.That(exit, Is.EqualTo(0), $"CLI --target={TargetName(backend)} {entry} failed:\n{stdout}{stderr}");
            Assert.That(File.Exists(path), Is.True, $"the CLI wrote no {Path.GetFileName(path)}:\n{stdout}{stderr}");
            return File.ReadAllText(path);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* temp */ } }
    }

    private static string EmitViaProject(Bk backend, string source)
    {
        var compiler = new BasicCompiler(new CompilerOptions { OptimizeAggressive = true });
        var dir = Path.Combine(Path.GetTempPath(), "bl-t163-proj-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "Main.bas");
            File.WriteAllText(path, source);
            var result = compiler.CompileProjectFiles(new List<string> { path });
            Assert.That(result.HasErrors, Is.False, string.Join(" | ", result.AllErrors.Select(e => e.Message)));
            Assert.That(result.CombinedIR, Is.Not.Null, "the project entry point produced no combined IR");
            var ir = result.CombinedIR;
            return backend switch
            {
                Bk.CSharp => new ImprovedCSharpCodeGenerator().Generate(ir),
                Bk.Cpp => new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false }).Generate(ir),
                Bk.JavaScript => new JavaScriptCodeGenerator().Generate(ir),
                Bk.Msil => new MSILCodeGenerator().Generate(ir),
                _ => throw new ArgumentException(backend.ToString()),
            };
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* temp */ } }
    }

    /// <summary>Generated text → what it prints. Skips (outside a multiple-assertion block) when the tool
    /// the backend's execution tier needs is missing: a C++ compiler, Node, ilasm.</summary>
    internal static string Run(Bk backend, string emitted) => backend switch
    {
        Bk.CSharp => FourBackends.RunEmittedCSharpText(emitted),
        Bk.Cpp => BclE2E.CompileRun(emitted),
        Bk.JavaScript => JavaScriptExecutionTests.RunNodeScript(emitted),
        Bk.Msil => MsilHarness.RunIlExpectingSuccess(emitted, "T"),
        _ => throw new ArgumentException(backend.ToString()),
    };

    internal static string Run(Bk backend, EntryPoint entry, string source) => Run(backend, Emit(backend, entry, source));

    /// <summary>Skips the calling test when <paramref name="backend"/>'s execution tool is not on this machine.
    /// Call it BEFORE any <c>Assert.Multiple</c>: NUnit fails an Ignore inside one.</summary>
    internal static void RequireTool(Bk backend)
    {
        switch (backend)
        {
            case Bk.Cpp:
                if (CppCompile.FindRunCompiler() == null) Assert.Ignore("No C++ compiler available on this machine");
                break;
            case Bk.JavaScript:
                if (BasicLang.Runtime.NodeLocator.Find() == null) Assert.Ignore("Node.js not found — the JS execution tier cannot run on this machine.");
                break;
            case Bk.Msil:
                MsilHarness.RequireIlasm();
                break;
        }
    }

    /// <summary>What an execution cell ended as. <see cref="Ran"/> carries stdout; the failures carry what to look for.</summary>
    internal enum Kind { Ran, RunFailed }

    internal sealed record Cell(Kind Kind, string Output);

    /// <summary>
    /// Like <see cref="Run(Bk, string)"/> for the two backends whose failure to RUN is a pinned outcome
    /// (JavaScript: a thrown ReferenceError; MSIL: an invalid program). The other two only ever ran here.
    /// </summary>
    internal static Cell Observe(Bk backend, string emitted)
    {
        switch (backend)
        {
            case Bk.JavaScript:
            {
                var (exit, stdout, stderr) = JavaScriptExecutionTests.RunNodeScriptForOutcome(emitted);
                return exit == 0 ? new Cell(Kind.Ran, Norm(stdout)) : new Cell(Kind.RunFailed, Norm(stdout + "\n" + stderr));
            }
            case Bk.Msil:
                return ObserveMsil(emitted);
            default:
                return new Cell(Kind.Ran, Norm(Run(backend, emitted)));
        }
    }

    /// <summary>
    /// MSIL, by EXIT CODE. <c>MsilHarness.RunIl</c> reads only the text ("Unhandled exception", InvalidProgramException) and
    /// classes a process that prints `7` and then dies of a segmentation fault (exit 139; on Windows an access violation) as
    /// <c>Ran</c> with output `7` — which is exactly what `LC_t0` does after #163, and what the fence must be able to see.
    /// </summary>
    private static Cell ObserveMsil(string il)
    {
        var ilasm = MsilHarness.RequireIlasm();
        var dir = Path.Combine(Path.GetTempPath(), "bl-t163-msil-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var ilPath = Path.Combine(dir, "T.il");
            var exePath = Path.Combine(dir, "T.exe");
            File.WriteAllText(ilPath, il);
            var (asmExit, asmOut, asmErr) = CliTestHarness.RunProcess(ilasm, new[] { ilPath, "-exe", "-output=" + exePath }, dir, timeoutMs: 120_000);
            Assert.That(asmExit == 0 && File.Exists(exePath), Is.True, $"ilasm rejected the emitted IL:\n{asmOut}{asmErr}");
            File.WriteAllText(Path.Combine(dir, "T.runtimeconfig.json"),
                "{\"runtimeOptions\":{\"tfm\":\"net8.0\",\"framework\":{\"name\":\"Microsoft.NETCore.App\",\"version\":\"8.0.0\"}}}");
            var (exit, stdout, stderr) = CliTestHarness.RunProcess("dotnet", new[] { exePath }, dir, timeoutMs: 60_000);
            return exit == 0 ? new Cell(Kind.Ran, Norm(stdout)) : new Cell(Kind.RunFailed, Norm(stdout + "\n" + stderr));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* temp */ } }
    }

    /// <summary>
    /// The LLVM leg. The backend has no console, and names its entry point `@Main`, so nothing it emits
    /// links on its own (`use of undefined value '@CStr'`, `undefined reference to main`, both before and
    /// after #163). The program returns its answer from `Main`, a two-line C shim calls it, and the
    /// PROCESS EXIT CODE is the answer. Returns the exit code.
    /// </summary>
    internal static int RunLlvm(string source, bool optimize)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-t163-llvm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Prog.bas"), source);
            var args = new List<string> { "Prog.bas", "--target=llvm" };
            if (optimize) args.Add("--optimize");
            var (exit, stdout, stderr) = CliTestHarness.RunProcess(CliTestHarness.CliPath(), args.ToArray(), dir, timeoutMs: 120_000);
            Assert.That(exit, Is.EqualTo(0), $"CLI --target=llvm failed:\n{stdout}{stderr}");
            Assert.That(File.Exists(Path.Combine(dir, "Prog.ll")), Is.True, "the CLI wrote no Prog.ll");

            File.WriteAllText(Path.Combine(dir, "shim.c"), "extern int Main(void);\nint main(void) { return Main(); }\n");
            var exe = Path.Combine(dir, "prog" + (OperatingSystem.IsWindows() ? ".exe" : ""));
            var (cc, ccOut, ccErr) = CliTestHarness.RunProcess("clang", new[] { "-w", "-o", exe, "Prog.ll", "shim.c", "-lm" }, dir, timeoutMs: 120_000);
            Assert.That(cc, Is.EqualTo(0), $"clang could not link the emitted LLVM IR:\n{ccOut}{ccErr}\n--- Prog.ll ---\n{File.ReadAllText(Path.Combine(dir, "Prog.ll"))}");
            var (rc, _, _) = CliTestHarness.RunProcess(exe, Array.Empty<string>(), dir, timeoutMs: 60_000);
            return rc;
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* temp */ } }
    }

    /// <summary>
    /// ⭐ One program, one backend, EVERY entry point: each must print VB's answer. The tool check comes first and
    /// outside any multiple-assertion block, because NUnit fails an Ignore inside one. A failed compile or run in one
    /// entry point is collected, not thrown, so the other two still report.
    /// </summary>
    internal static void AssertMatchesInEveryEntryPoint(Bk backend, string source, string expected, string label)
    {
        RequireTool(backend);
        var failures = new List<string>();
        foreach (var entry in Enum.GetValues<EntryPoint>())
        {
            try
            {
                var got = Norm(Run(backend, entry, source));
                if (got != Norm(expected)) failures.Add($"{entry}: printed [{got.Replace("\n", " | ")}] where VB prints [{Norm(expected).Replace("\n", " | ")}]");
            }
            catch (AssertionException ex)
            {
                failures.Add($"{entry}: {ex.Message.Split('\n')[0]}");
            }
        }
        Assert.That(failures, Is.Empty, $"{label} on {backend}, through {string.Join(", ", Enum.GetValues<EntryPoint>())}:\n" + string.Join("\n", failures));
    }

    /// <summary>What a pinned cell must look like: it Ran and printed exactly <see cref="Text"/>; or it failed to run and its
    /// output contains <see cref="Text"/> (a ReferenceError); or it printed <see cref="Text"/> and THEN died
    /// (<see cref="AfterPrinting"/>: the `7` MSIL prints before its segmentation fault).</summary>
    internal sealed record Pin(Kind Kind, string Text, bool AfterPrinting = false)
    {
        internal static Pin Ran(string text) => new(Kind.Ran, text);
        internal static Pin RunFailed(string contains) => new(Kind.RunFailed, contains);
        internal static Pin RunFailedAfterPrinting(string prefix) => new(Kind.RunFailed, prefix, true);

        internal bool Matches(Cell cell) => cell.Kind == Kind && Kind switch
        {
            Kind.Ran => cell.Output == Norm(Text),
            _ when AfterPrinting => cell.Output.StartsWith(Text, StringComparison.Ordinal),
            _ => cell.Output.Contains(Text, StringComparison.Ordinal),
        };
    }

    /// <summary>⭐ A CURRENT behaviour, pinned in every entry point. Used only for the #121 fence, where the pin is the point.</summary>
    internal static void AssertPinnedInEveryEntryPoint(Bk backend, string source, Pin pin, string label)
    {
        RequireTool(backend);
        var failures = new List<string>();
        foreach (var entry in Enum.GetValues<EntryPoint>())
        {
            try
            {
                var cell = Observe(backend, Emit(backend, entry, source));
                if (!pin.Matches(cell)) failures.Add($"{entry}: {cell.Kind} [{cell.Output.Replace("\n", " | ")}]");
            }
            catch (AssertionException ex)
            {
                failures.Add($"{entry}: {ex.Message.Split('\n')[0]}");
            }
        }
        Assert.That(failures, Is.Empty,
            $"{label} on {backend} was pinned as {pin.Kind} [{pin.Text.Replace("\n", " | ")}]. If #121 (reserving the temp-spelled names of For Each, Catch, pattern and LINQ " +
            $"variables) has landed, this row flips deliberately: update it with ADR-0017 and HANDOFF.\n" + string.Join("\n", failures));
    }

    /// <summary>Whether <c>clang</c> is on PATH — the only gate the LLVM leg needs.</summary>
    internal static bool ClangAvailable()
    {
        try
        {
            var (exit, _, _) = CliTestHarness.RunProcess("clang", new[] { "--version" }, Path.GetTempPath(), timeoutMs: 15_000);
            return exit == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
