using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VisualGameStudio.Tests.Msil;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #225 — on MSIL, a GENERIC type in an instruction's type-token position (`newarr`, `stelem`, `ldelema`, `unbox.any`) is spelled `class [mscorlib]System.Func`1<int32>`. RUN, through every
//  entry point. MSIL, with the C# backend as the reference.
//
//  ⛔ THE BUG. ilasm reads a token as a bare class NAME (`[mscorlib]System.String`, `'Foo'`) or a whole TYPE, and type arguments and an array rank exist only in the second form. The token speller
//  (`MSILCodeGenerator.IlTypeToken`) wrote the name form for everything, so `newarr [mscorlib]System.Func`1<int32>` and `unbox.any [mscorlib]System.Func`1<int32>` were syntax errors at the `<`: no
//  program with a sized array of a Func/Action/List, or a For Each over a List of them, ever assembled. Behind it two more sites had the same blind spot and surfaced once it assembled: `ldelema` read
//  `MapType` (`ldelema Func`, and `ldelema Action` for the NON-generic Action: "Reference to undefined class") and the array's own local spec came from its NAME, `Func[]`, which has lost the arguments
//  (`class 'Func'[] 'fs'`). Now the generic tokens come from ONE rule (`TryBclToken`, shared with a call's receiver), `ldelema` asks the token speller like `newarr`/`stelem`, an array of a BCL generic
//  is spelled from its element, and an ARRAY token is its whole type (`int32[]` — `unbox.any [mscorlib]System.Int32[]` was the same syntax error, for every For Each over a `List(Of Integer())`).
//
//  ORACLE: vbc, via `S/t136/tools/vbv2.py`, over each program wrapped in a VB Module (S/t225/probes: P01-P04, P07, P10, C01-C03, J1). Every expected string is vbc's output, and each program is also RUN
//  on the C# backend through the same three entry points, so the reference cannot rot.
//
//  ENTRY POINTS: the spawned CLI (standard passes), the CLI with `--optimize` and `BasicCompiler.CompileProjectFiles` (aggressive) — `TempExec.AssertMatchesInEveryEntryPoint`. Every C# run is a child
//  process (`CSharpProcessRunner`, #256): each program loops. A machine with no ilasm SKIPS the MSIL cells, never fails them. The IL TEXT half, which needs no ilasm, is
//  `MsilGenericTypeTokenTextTests` below.
//
//  ⭐ MUTANTS (S/t225/mut: the fix plus ONE change, built from a plain source copy and run against a copy of the test output with its BasicLang.dll swapped; each is killed by a committed test):
//    M1 the old spelling at the token speller (no `class` on a generic):  `ForEach_…`, `ASizedArray…`, `AnArrayOfListOfInteger_…`, both text cases, the moved K1/K2 pins and FE1
//       (ilasm: "syntax error at token '<'"); 8 of the 10 new and moved cases
//    M2 `class` on every token, value types too (`unbox.any class [mscorlib]System.Int32`):  `AnArrayOfListOfInteger_…`, `AForEachOverAListOfIntegerArrays_…`, both text cases and FE1
//       (TypeLoadException: "value type mismatch")
//    M3 the generic arguments dropped (`newarr class [mscorlib]System.Func`1`):  the same 8 as M1 (TypeLoadException: "Could not load type 'System.Func`1'")
//    Against master's own BasicLang.dll all 10 fail; the unmutated fix passes all 10.
//
//  ⛔ KNOWN GAPS — each pre-existing and NOT #225's (no opcode is mis-spelled: none is emitted), listed with NO test (asserting one would pin the defect):
//    - `CType(o, T)` / `DirectCast(o, T)` from Object to ANY reference type emits no `castclass` on MSIL (`// WARNING: Unknown cast`), so a wrong object passes where VB throws InvalidCastException,
//      and `TypeOf o Is Foo` is `o IsNot Nothing` (no `isinst`) — `New Bar()` answers True. Measured for a user class and for `Func(Of String)` over a `Func(Of Integer)`; vbc throws / prints False.
//    - `TypeOf o Is Func(Of Integer)` is refused by the front end on EVERY backend ("the type must be a class or an interface"); vbc accepts it.
//    - a user generic class (`Class Box(Of T)`) on MSIL: "Reference to undefined class 'T'" — task #239. Its tokens are the erased bare name `'Box'`, which this fix does not touch.
//
//  ⚠ Named "…ExecutionTests" but NOT in JsExecutionTierRosterTests' roster: it runs no JavaScript, so it is in that file's `NotJavaScriptExecution`.
// ================================================================================================

/// <summary>#225 RUN: a generic delegate or collection in an IL type-token position — a sized array of one, a For Each over a List of one — assembles and prints vbc's answer on MSIL, as on C#.</summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the spawned CLI, ilasm and the C# child runs share the machine
public class MsilGenericTypeTokenExecutionTests
{
    /// <summary>P01 — For Each over a `List(Of Func(Of Integer))`: `unbox.any class …Func`1&lt;int32&gt;` (was the syntax error). vbc prints 10 20 30.</summary>
    private static readonly TempProbe ForEachOverAListOfFunc = P("foreach_list_of_func", """
        Sub Main()
            Dim fs As New List(Of Func(Of Integer))()
            fs.Add(Function() 10)
            fs.Add(Function() 20)
            Dim total As Integer = 0
            For Each f As Func(Of Integer) In fs
                total = total + f()
                Console.WriteLine(f())
            Next
            Console.WriteLine(total)
        End Sub
        """, "10\n20\n30");

    /// <summary>P02 — `Dim fs(2) As Func(Of Integer)`, filled and invoked: `newarr`, `ldelema` and the array LOCAL's spec. vbc prints 6 2.</summary>
    private static readonly TempProbe ASizedArrayOfFunc = P("array_of_func", """
        Sub Main()
            Dim fs(2) As Func(Of Integer)
            fs(0) = Function() 1
            fs(1) = Function() 2
            fs(2) = Function() 3
            Dim s As Integer = 0
            For i As Integer = 0 To 2
                s = s + fs(i)()
            Next
            Console.WriteLine(s)
            Dim g As Func(Of Integer) = fs(1)
            Console.WriteLine(g())
        End Sub
        """, "6\n2");

    /// <summary>P03 — an `Action(Of String)` array. vbc prints a:x b:x.</summary>
    private static readonly TempProbe AnArrayOfActionOfString = P("array_of_action_of_string", """
        Sub Main()
            Dim acts(1) As Action(Of String)
            acts(0) = Sub(s As String) Console.WriteLine("a:" & s)
            acts(1) = Sub(s As String) Console.WriteLine("b:" & s)
            For i As Integer = 0 To 1
                acts(i)("x")
            Next
        End Sub
        """, "a:x\nb:x");

    /// <summary>P04 — a `Func(Of Integer, Integer)` array, then a For Each over a List of the same type (two type arguments in both tokens). vbc prints 49 107 9 103.</summary>
    private static readonly TempProbe FuncOfTwo = P("func_of_two", """
        Sub Main()
            Dim sq(1) As Func(Of Integer, Integer)
            sq(0) = Function(n As Integer) n * n
            sq(1) = Function(n As Integer) n + 100
            Console.WriteLine(sq(0)(7))
            Console.WriteLine(sq(1)(7))
            Dim l As New List(Of Func(Of Integer, Integer))()
            l.Add(sq(0))
            l.Add(sq(1))
            For Each f As Func(Of Integer, Integer) In l
                Console.WriteLine(f(3))
            Next
        End Sub
        """, "49\n107\n9\n103");

    /// <summary>
    /// C02 — the generic COLLECTION in the same positions: a `List(Of Integer)` array (`newarr class …List`1&lt;int32&gt;` was the syntax error), a For Each over a `List(Of List(Of Integer))`, and the
    /// value-type CONTROL, a For Each over a `List(Of Integer)`, whose `unbox.any [mscorlib]System.Int32` must stay bare (M2 makes it `class …Int32`: TypeLoadException). vbc prints 3 1 2 7 8.
    /// </summary>
    private static readonly TempProbe AnArrayOfListOfInteger = P("array_of_list_of_integer", """
        Sub Main()
            Dim ls(1) As List(Of Integer)
            ls(0) = New List(Of Integer)()
            ls(1) = New List(Of Integer)()
            ls(0).Add(3)
            ls(1).Add(4)
            ls(1).Add(5)
            Console.WriteLine(ls(0).Count + ls(1).Count)
            Dim outer As New List(Of List(Of Integer))()
            outer.Add(ls(0))
            outer.Add(ls(1))
            For Each inner As List(Of Integer) In outer
                Console.WriteLine(inner.Count)
            Next
            Dim xs As New List(Of Integer)()
            xs.Add(7)
            xs.Add(8)
            For Each x As Integer In xs
                Console.WriteLine(x)
            Next
        End Sub
        """, "3\n1\n2\n7\n8");

    /// <summary>J1 — an ARRAY in the token position: For Each over a `List(Of Integer())` is `unbox.any int32[]` (`[mscorlib]System.Int32[]` was a syntax error at the `[`). vbc prints 2.</summary>
    private static readonly TempProbe AForEachOverAListOfIntegerArrays = P("foreach_list_of_integer_arrays", """
        Sub Main()
            Dim l As New List(Of Integer())()
            Dim a() As Integer = {1, 2}
            l.Add(a)
            For Each x As Integer() In l
                Console.WriteLine(x(1))
            Next
        End Sub
        """, "2");

    /// <summary>C03 — the NON-generic `Action` keeps its bare token (`newarr [mscorlib]System.Action`) and an array of it now assembles too: its `ldelema` read MapType (`ldelema Action`). vbc prints plain.</summary>
    private static readonly TempProbe AnArrayOfTheNonGenericAction = P("array_of_action", """
        Sub Main()
            Dim acts(0) As Action
            acts(0) = Sub() Console.WriteLine("plain")
            acts(0)()
        End Sub
        """, "plain");

    /// <summary>C01 — the CONTROL: a user `Delegate` is nominal (declared in this assembly, a bare token) in an array and a For Each. It ran before #225 and must still. vbc prints 30 10 20.</summary>
    private static readonly TempProbe AUserDelegate = P("user_delegate", """
        Delegate Function IntGetter() As Integer

        Function Ten() As Integer
            Return 10
        End Function

        Function Twenty() As Integer
            Return 20
        End Function

        Sub Main()
            Dim gs(1) As IntGetter
            gs(0) = AddressOf Ten
            gs(1) = AddressOf Twenty
            Console.WriteLine(gs(0)() + gs(1)())
            Dim l As New List(Of IntGetter)()
            l.Add(gs(0))
            l.Add(gs(1))
            For Each g As IntGetter In l
                Console.WriteLine(g())
            Next
        End Sub
        """, "30\n10\n20");

    /// <summary>P07 — the CONTROL: a `Dictionary(Of String, Func(Of Integer))` value read takes no type token and ran before #225; it must still. vbc prints 22 11.</summary>
    private static readonly TempProbe ADictionaryOfFunc = P("dictionary_of_func", """
        Sub Main()
            Dim d As New Dictionary(Of String, Func(Of Integer))()
            d.Add("a", Function() 11)
            d.Add("b", Function() 22)
            Dim f As Func(Of Integer) = d("b")
            Console.WriteLine(f())
            Console.WriteLine(d("a")())
        End Sub
        """, "22\n11");

    private static TempProbe P(string id, string source, string vb) => new(id, source, vb, Bk.CSharp | Bk.Msil, HangSafe: true);

    /// <summary>
    /// Each probe on MSIL and on C# (the reference), each through every entry point. A failing cell is collected, not thrown, so every other one still reports, and the failure names the probe, the
    /// backend and the entry point. (A copy of the helper `NetReadOnlyPropertyExecutionTests` keeps: each fixture owns its own.)
    /// </summary>
    private static void AssertMsilAndCSharp(params TempProbe[] probes)
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

    /// <summary>The headline: For Each over a List of a generic delegate (`unbox.any`). Before #225 every MSIL cell was ilasm's "syntax error at token '&lt;'". Kills M1 and M3.</summary>
    [Test]
    public void ForEach_OverAListOfFunc_PrintsVbcsAnswer()
        => AssertMsilAndCSharp(ForEachOverAListOfFunc);

    /// <summary>
    /// A sized array of a generic delegate — `Func(Of Integer)`, `Action(Of String)`, `Func(Of Integer, Integer)` — filled, indexed and invoked (`newarr`, `ldelema`, `stelem`, the array local's spec), and
    /// a For Each over a List of a two-argument Func. Before #225 every MSIL cell failed to assemble. Kills M1 and M3.
    /// </summary>
    [Test]
    public void ASizedArrayOfAGenericDelegate_PrintsVbcsAnswer()
        => AssertMsilAndCSharp(ASizedArrayOfFunc, AnArrayOfActionOfString, FuncOfTwo);

    /// <summary>The generic COLLECTION in the same token positions, beside the `Integer` token that must stay bare. Before #225 every MSIL cell failed to assemble. Kills M1, M2 and M3.</summary>
    [Test]
    public void AnArrayOfListOfInteger_AndAForEachOverAListOfLists_PrintsVbcsAnswer()
        => AssertMsilAndCSharp(AnArrayOfListOfInteger);

    /// <summary>An array TYPE in the token position (`unbox.any int32[]`). Before #225 every MSIL cell was ilasm's "syntax error at token '['". Kills M2 (`unbox.any class int32[]`).</summary>
    [Test]
    public void AForEachOverAListOfIntegerArrays_PrintsVbcsAnswer()
        => AssertMsilAndCSharp(AForEachOverAListOfIntegerArrays);

    /// <summary>
    /// The non-generic `Action` array (it did not assemble either: `ldelema Action`), and the two CONTROLS that already ran — a user `Delegate` and a `Dictionary(Of String, Func(Of Integer))` read — which
    /// must still print vbc's answer.
    /// </summary>
    [Test]
    public void TheNonGenericAction_AUserDelegate_AndADictionaryOfFunc_PrintVbcsAnswer()
        => AssertMsilAndCSharp(AnArrayOfTheNonGenericAction, AUserDelegate, ADictionaryOfFunc);
}

/// <summary>
/// #225, the IL TEXT: what the type-token speller writes, read without ilasm, so a machine that cannot assemble still sees a regression. Every generic token carries `class` and its arguments; a value
/// type's token never does. The standard pipeline (`MsilHarness.CompileToIl`) and the aggressive one alike. Kills M1, M2 and M3.
/// </summary>
[TestFixture]
public class MsilGenericTypeTokenTextTests
{
    private const string Program = """
        Sub Main()
            Dim fs(1) As Func(Of Integer)
            fs(0) = Function() 1
            fs(1) = Function() 2
            Dim l As New List(Of Func(Of Integer))()
            l.Add(fs(1))
            For Each f As Func(Of Integer) In l
                Console.WriteLine(f())
            Next
            Dim xs As New List(Of Integer)()
            xs.Add(fs(0)())
            For Each x As Integer In xs
                Console.WriteLine(x)
            Next
        End Sub
        """;

    [TestCase(false)]
    [TestCase(true)]
    public void EveryGenericTokenCarriesClassAndItsArguments_AValueTypeTokenNever(bool aggressive)
    {
        var il = MsilHarness.CompileToIl(Program, aggressive: aggressive);
        const string func = "class [mscorlib]System.Func`1<int32>";
        Assert.Multiple(() =>
        {
            Assert.That(il, Does.Contain("newarr " + func), "newarr of the Func array");
            Assert.That(il, Does.Contain("ldelema " + func), "ldelema of a Func element (it read MapType: `ldelema Func`)");
            Assert.That(il, Does.Contain("unbox.any " + func), "the For Each over the List of Func");
            Assert.That(il, Does.Contain(func + "[] "), "the Func array's LOCAL spec (it was `class 'Func'[]`)");
            Assert.That(il, Does.Contain("unbox.any [mscorlib]System.Int32"), "the For Each over the List of Integer keeps the bare value-type token");
            Assert.That(il, Does.Not.Contain("class [mscorlib]System.Int32"), "a value type is never spelled `class`");
            Assert.That(il, Does.Not.Match(@"(newarr|ldelema|stelem|unbox\.any) \[mscorlib\]System\.(Func|Action|Collections)"), "a generic token without its `class`");
            Assert.That(il, Does.Not.Match(@"System\.Func`1(?!<)"), "a Func`1 with its type argument dropped");
        });
    }
}
