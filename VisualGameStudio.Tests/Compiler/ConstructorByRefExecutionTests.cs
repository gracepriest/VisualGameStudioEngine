using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.JavaScript;
using BasicLang.Compiler.CodeGen.MSIL;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #144 — a ByRef CONSTRUCTOR parameter writes back on every backend, and a VALUE passed to one is copied in (VB's rule). RUN, against vbc, through every entry point.
//
//  ⛔ THE BUG. `Sub New(ByRef n As Integer) : n = 100` called as `New Box(p)` left `p` unchanged on every backend (vbc writes 100 into it). Both halves of the ByRef were missing for a
//  construction: IRBuilder built a constructor's parameters without IsByRef (C# spelled `int n`, C++ `int32_t n` — and `const std::string&` for a String, which did not compile once
//  written — MSIL `int32`, and JavaScript's BL7002 walk never saw it), and IRNewObject / IRBaseConstructorCall carried no ByRefArguments, unlike IRCall / IRInstanceMethodCall. A literal or
//  an expression passed to a ByRef constructor parameter (`New Box(5)`, `New Box(p + 1)`, `MyBase.New(7)`) ran everywhere BEFORE (the callee wrote a copy); writing the flags honestly made it
//  CS1510 on C#, a named refusal on MSIL and BL7002 on JavaScript — so IRBuilder.CopyInByRefArguments gives such an argument VB's own copy-in temp (IRVariable.IsByRefCopyIn): a fresh
//  variable the callee writes and nobody reads, rendered on C# IN PLACE as the expression `ref (new T[] { v })[0]` (a statement before the call ran `Seed(2)` before `Seed(1)`, measured, and
//  `: base(...)` admits no statement at all).
//
//  ⭐ THE ORACLE IS vbc, not a backend. Every row below is a program from the implementer's probes (S/t144/probes N1..N15, each with a vbc `.exp`), and its expected text is what the SDK's vbc prints
//  for it (the program wrapped in a VB Module) — measured, never taken from a backend.
//
//  ⭐ ENTRY POINTS (CLAUDE.md: "test both entry points", "validate codegen through the CLI and the IR optimizer"): the real BasicLang CLI (standard passes), the real CLI with `--optimize`
//  (aggressive) and `BasicCompiler.CompileProjectFiles` with `OptimizeAggressive` (what a Release .blproj build and the IDE call) — `TempExec.AssertMatchesInEveryEntryPoint` runs all three.
//  The FAST half is ConstructorByRefShapeTests, which reads the C#.
//
//  ⭐ ONE TEST PER (GROUP, BACKEND), EACH LOOPING OVER ITS ROWS: the group is a table (`VariableRows`, `CopyInRows`), a row that fails is NAMED in the message with its id, and one case counts for the
//  whole table. Every row of a group prints on EVERY backend the group names, so a backend whose row does not is a defect, not a gap. MSIL legs SKIP (not fail) where ilasm is missing: the tool
//  check runs first, outside any multiple-assertion block, in `TempExec.RequireTool`.
//
//  ⛔⛔ EVERY C# RUN HERE IS HANG-SAFE (`hangSafe: true` -> CSharpProcessRunner): several rows hold loops, and a C# loop that never ends freezes the whole test host if it runs in process.
//
//  ⭐ MUTANTS (each is the fix plus ONE change, built from the rebased source and run against a copy of the test output with its BasicLang.dll swapped):
//    M1  constructor parameters lose IsByRef (the facts are not carried to the `New` call)       KILLED by `n1_local`, `n6_mybase_new` (C#: CS1615; C++ / MSIL print `3,3` / `5`) and the fast `F2`
//    M2  no copy-in temp (a literal / expression argument is passed bare)                        KILLED by `n9_literal`, `n10_mybase_literal`, `n12_const_call_loop` (C#: CS1510; MSIL: a named
//                                                                                                refusal; JavaScript: BL7002; C++ `n12` writes the Const `K`: 21) and the fast `F1`, `F3`
//    M3  C# writes the copy-in temp as a STATEMENT instead of `ref (new T[] { v })[0]`          KILLED by `n13_eval_order` (C#: seed2 before seed1) and `n10_mybase_literal` (C#: "needs a statement
//                                                                                                before the base call") and the fast `F1`, `F3`
//
//  ⛔ KNOWN GAPS — NOT #144's, measured on the fixed build, and with NO test (asserting one would pin the defect):
//    - an ORDINARY method called with a literal or an expression for a ByRef parameter (`Bump(41)`) is still CS1510 on C# and refused on MSIL (HANDOFF "Bump(41)"): the copy-in is constructor-only here
//      (`New` and `MyBase.New`); extending it to every call kind is the follow-up.
//    - JavaScript refuses ByRef for an argument WITH STORAGE (a variable, a field, an array element) by design — BL7002, `JavaScript has no reference parameters` — for a constructor as for every
//      call; `ByAVariableOnJavaScript_BL7002` is the only row that says so, and KillVocabularyExtensions.B2_JavaScript_RefusesByRef_BL7002 pins the same policy for a method.
//    - MSIL refuses `New Box(Me.K)` (a field read through `Me.`) by name, exactly as it refuses `b.Bump(Me.K)` on master (`MsilRefusesAFieldRead_AsItRefusesOnAnOrdinaryMethod`): the argument is an
//      expression's value, not an address.
//
//  ⚠ Named "…ExecutionTests" on purpose: its copy-in rows RUN under Node, so it is in JsExecutionTierRosterTests' roster.
// ================================================================================================

/// <summary>
/// #144 RUN: a ByRef constructor parameter writes back (a variable argument) or takes a copy (a value argument) on C#, C++, MSIL and — for values — JavaScript, and prints vbc's answer through
/// the CLI, the CLI with <c>--optimize</c> and <c>CompileProjectFiles</c>.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the JavaScript legs and the C++ compiles share the machine with the spawned runners
public class ConstructorByRefExecutionTests
{
    private const Bk Three = Bk.CSharp | Bk.Cpp | Bk.Msil;

    // ============================================================================================
    // THE ROWS
    // ============================================================================================

    /// <summary>
    /// A VARIABLE (or an element, a field) passed to a ByRef constructor parameter: the write is the caller's. C#, C++ and MSIL print vbc's answer; JavaScript refuses it (BL7002) — it has no
    /// reference parameters. `n4_field` is the one row MSIL does not run: it refuses `New Box(Me.K)` by name (see `MsilRefusesAFieldRead_…`).
    /// </summary>
    internal static readonly IReadOnlyList<TempProbe> VariableRows = new TempProbe[]
    {
        // N1 — a local; the optimizer must not move `p` / `q` across the construction, and `a` (p + q) is read BEFORE it.
        new("n1_local", """
            Function Seed(v As Integer) As Integer
                Console.WriteLine("seed")
                Return v
            End Function

            Class Box
                Sub New(ByRef n As Integer)
                    n = 100
                End Sub
            End Class

            Sub Main()
                Dim p As Integer = Seed(1)
                Dim q As Integer = Seed(2)
                Dim l As New List(Of Integer)()
                l.Add(0)
                Dim a As Integer = p + q
                Dim b As New Box(p)
                l(0) = p + q
                Console.WriteLine(CStr(l(0)) & "," & CStr(a))
            End Sub
            """, "seed\nseed\n102,3", Three, HangSafe: true),

        // N2 — a String: C++ spelled the parameter `const std::string&`, which does not compile once written.
        new("n2_string", """
            Class Box
                Public Tag As String
                Sub New(ByRef s As String)
                    Tag = s
                    s = s & "!"
                End Sub
            End Class

            Sub Main()
                Dim s As String = "hi"
                Dim b As New Box(s)
                Console.WriteLine(s & "," & b.Tag)
            End Sub
            """, "hi!,hi", Three, HangSafe: true),

        // N3 — a loop-carried local, constructed in a `Dim c As Counter = New Counter(total)` declaration.
        new("n3_loop_local", """
            Class Counter
                Sub New(ByRef n As Integer)
                    n = n + 10
                End Sub
            End Class

            Sub Main()
                Dim total As Integer = 0
                For i As Integer = 1 To 3
                    Dim c As Counter = New Counter(total)
                    total = total + 1
                Next
                Console.WriteLine(CStr(total))
            End Sub
            """, "33", Three, HangSafe: true),

        // N4 — a field, `New Box(Me.K)`: C# and C++ run it; MSIL refuses it by name (the row's own test).
        new("n4_field", """
            Class Box
                Sub New(ByRef n As Integer)
                    n = 100
                End Sub
            End Class

            Class Holder
                Public K As Integer = 5
                Sub Run()
                    Dim b As New Box(Me.K)
                    Console.WriteLine(CStr(K))
                End Sub
            End Class

            Sub Main()
                Dim h As New Holder()
                h.Run()
            End Sub
            """, "100", Bk.CSharp | Bk.Cpp, HangSafe: true),

        // N5 — an array element: C++ aliases it, C# writes `ref arr[1]`, MSIL loads its address.
        new("n5_array_element", """
            Class Box
                Sub New(ByRef n As Integer)
                    n = n * 7
                End Sub
            End Class

            Sub Main()
                Dim arr(2) As Integer
                arr(0) = 1
                arr(1) = 2
                arr(2) = 3
                Dim b As New Box(arr(1))
                Console.WriteLine(CStr(arr(0)) & "," & CStr(arr(1)) & "," & CStr(arr(2)))
            End Sub
            """, "1,14,3", Three, HangSafe: true),

        // N6 — `MyBase.New(m)` from a derived constructor whose own parameter is ByRef: the write passes through two constructors.
        new("n6_mybase_new", """
            Class BaseBox
                Sub New(ByRef n As Integer)
                    n = n + 1000
                End Sub
            End Class

            Class Derived
                Inherits BaseBox
                Sub New(ByRef m As Integer)
                    MyBase.New(m)
                    m = m + 1
                End Sub
            End Class

            Sub Main()
                Dim p As Integer = 5
                Dim d As New Derived(p)
                Console.WriteLine(CStr(p))
            End Sub
            """, "1006", Three, HangSafe: true),

        // N7 — ByRef beside an Optional left out, and the same constructor with the Optional given: the flags stay in lockstep with the filled argument list.
        new("n7_byref_with_optional", """
            Class Box
                Public Extra As Integer
                Sub New(ByRef n As Integer, Optional k As Integer = 3)
                    Extra = k
                    n = n + k
                End Sub
            End Class

            Sub Main()
                Dim p As Integer = 10
                Dim b As New Box(p)
                Dim q As Integer = 20
                Dim c As New Box(q, 5)
                Console.WriteLine(CStr(p) & "," & CStr(b.Extra) & "," & CStr(q) & "," & CStr(c.Extra))
            End Sub
            """, "13,3,25,5", Three, HangSafe: true),

        // N8 — `b = New Box(p)` as an assignment, and `Return New Box(x)` inside a Function whose own parameter is ByRef.
        new("n8_assignment_and_return", """
            Class Box
                Public V As Integer
                Sub New(ByRef n As Integer)
                    n = n + 1
                    V = n
                End Sub
            End Class

            Function Make(ByRef x As Integer) As Box
                Return New Box(x)
            End Function

            Sub Main()
                Dim p As Integer = 1
                Dim b As Box
                b = New Box(p)
                Dim c As Box = Make(p)
                Console.WriteLine(CStr(p) & "," & CStr(b.V) & "," & CStr(c.V))
            End Sub
            """, "3,2,3", Three, HangSafe: true),

        // N14 — an Optional ByRef left out through an IMPLICIT base constructor and through an explicit `MyBase.New(k * 2)`, and a String ByRef beside an Optional String ByRef.
        new("n14_optional_byref", """
            Class BaseBox
                Public V As Integer
                Sub New(Optional ByRef n As Integer = 3)
                    n = n + 1
                    V = n
                End Sub
            End Class

            Class Implicit
                Inherits BaseBox
            End Class

            Class Explicit
                Inherits BaseBox
                Sub New(k As Integer)
                    MyBase.New(k * 2)
                End Sub
            End Class

            Class Direct
                Public S As String
                Sub New(ByRef s0 As String, Optional ByRef t As String = "t")
                    S = s0 & t
                    s0 = "changed"
                    t = "x"
                End Sub
            End Class

            Sub Main()
                Dim i As New Implicit()
                Dim e As New Explicit(5)
                Dim w As String = "w"
                Dim d As New Direct(w)
                Dim d2 As New Direct("lit" & w, "u")
                Console.WriteLine(CStr(i.V) & "," & CStr(e.V) & "," & w & "," & d.S & "," & d2.S)
            End Sub
            """, "4,11,changed,wt,litchangedu", Three, HangSafe: true),
    };

    /// <summary>
    /// A VALUE (a literal, an expression, a Const, a call) passed to a ByRef constructor parameter: VB copies it into a temporary the callee writes and nobody reads. All FOUR backends print vbc's
    /// answer — JavaScript runs them without BL7002, because a copy-in argument has no storage a write could be lost from.
    /// </summary>
    internal static readonly IReadOnlyList<TempProbe> CopyInRows = new TempProbe[]
    {
        // N9 — `New Box(5)` and `New Box(p + 1)`: the callee's `n = n + 1` is invisible to the caller (`p` stays 4).
        new("n9_literal", """
            Class Box
                Public V As Integer
                Sub New(ByRef n As Integer)
                    V = n
                    n = n + 1
                End Sub
            End Class

            Sub Main()
                Dim p As Integer = 4
                Dim a As New Box(5)
                Dim b As New Box(p + 1)
                Console.WriteLine(CStr(p) & "," & CStr(a.V) & "," & CStr(b.V))
            End Sub
            """, "4,5,5", Bk.All, HangSafe: true),

        // N10 — `MyBase.New(7)`: `: base(...)` admits only EXPRESSIONS on C#, so the temp has to be one.
        new("n10_mybase_literal", """
            Class BaseBox
                Public V As Integer
                Sub New(ByRef n As Integer)
                    V = n
                    n = n + 1
                End Sub
            End Class

            Class Derived
                Inherits BaseBox
                Sub New()
                    MyBase.New(7)
                End Sub
            End Class

            Sub Main()
                Dim d As New Derived()
                Console.WriteLine(CStr(d.V))
            End Sub
            """, "7", Bk.All, HangSafe: true),

        // N12 — a Const (its value must survive: C++ would write through a `K` it aliased), a call result, and a loop expression.
        new("n12_const_call_loop", """
            Class Box
                Public V As Integer
                Sub New(ByRef n As Integer)
                    n = n + 1
                    V = n
                End Sub
            End Class

            Const K As Integer = 20

            Function Twice(x As Integer) As Integer
                Return x * 2
            End Function

            Sub Main()
                Dim c As New Box(K)
                Dim t As New Box(Twice(15))
                Dim total As Integer = 0
                For i As Integer = 1 To 3
                    Dim b As New Box(i * 100)
                    total = total + b.V
                Next
                Console.WriteLine(CStr(c.V) & "," & CStr(t.V) & "," & CStr(K) & "," & CStr(total))
            End Sub
            """, "21,31,20,603", Bk.All, HangSafe: true),

        // N13 — EVALUATION ORDER: `New Pair(Seed(1), Seed(2))` runs Seed(1) first. A copy-in temp written as a STATEMENT ran Seed(2) first (measured, M3).
        new("n13_eval_order", """
            Function Seed(v As Integer) As Integer
                Console.WriteLine("seed" & CStr(v))
                Return v
            End Function

            Class Pair
                Public A As Integer
                Public B As Integer
                Sub New(x As Integer, ByRef y As Integer)
                    A = x
                    B = y
                    y = 0
                End Sub
            End Class

            Sub Main()
                Dim p As New Pair(Seed(1), Seed(2))
                Dim q As New Pair(Seed(3), Seed(4) * 10)
                Console.WriteLine(CStr(p.A + p.B) & "," & CStr(q.A + q.B))
            End Sub
            """, "seed1\nseed2\nseed3\nseed4\n3,43", Bk.All, HangSafe: true),

        // N15 — the copy-in inside a For and a While: a fresh temp per iteration, never shared (and never a name the loop's own locals can collide with).
        new("n15_loops", """
            Class Box
                Public V As Integer
                Sub New(ByRef n As Integer)
                    n = n + 1
                    V = n
                End Sub
            End Class

            Sub Main()
                Dim total As Integer = 0
                For i As Integer = 1 To 3
                    Dim b As New Box(5)
                    total = total + b.V
                Next
                Dim k As Integer = 0
                While k < 2
                    Dim c As New Box(10)
                    total = total + c.V
                    k = k + 1
                End While
                Console.WriteLine(CStr(total))
            End Sub
            """, "40", Bk.All, HangSafe: true),
    };

    /// <summary>The refusal rows (variable arguments, JavaScript): a variable to `New Box(...)` and to `MyBase.New(...)`.</summary>
    private static readonly string[] JsRefusedIds = { "n1_local", "n6_mybase_new" };

    private static IEnumerable<TempProbe> RowsFor(IEnumerable<TempProbe> table, Bk backend) => table.Where(p => p.Agrees.HasFlag(backend));

    private static string Names(IEnumerable<TempProbe> rows) => string.Join(",", rows.Select(p => p.Id));

    // ============================================================================================
    // the loop every group test runs
    // ============================================================================================

    /// <summary>
    /// Every row of <paramref name="rows"/> on <paramref name="backend"/>, through the CLI, the CLI with <c>--optimize</c> and <c>CompileProjectFiles</c>; a row that fails is collected with its id so
    /// the others still report. The tool check is FIRST and outside any multiple-assertion block (NUnit fails an Ignore inside one).
    /// </summary>
    private static void AssertRowsPrintVbcsAnswer(Bk backend, IEnumerable<TempProbe> rows)
    {
        TempExec.RequireTool(backend);

        var failures = new List<string>();
        var count = 0;
        foreach (var row in rows)
        {
            count++;
            try
            {
                TempExec.AssertMatchesInEveryEntryPoint(backend, row.Source, row.Vb, row.Id, hangSafe: row.HangSafe);
            }
            catch (AssertionException ex)
            {
                failures.Add(ex.Message);
            }
            catch (Exception ex) when (ex is not ResultStateException)
            {
                failures.Add($"{row.Id} on {backend}: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
            }
        }

        Assert.That(count, Is.GreaterThan(0), "no rows for " + backend);
        Assert.That(failures, Is.Empty, $"{failures.Count} of {count} rows differ from vbc on {backend}:\n" + string.Join("\n", failures));
    }

    // ============================================================================================
    // THE TABLES
    // ============================================================================================

    /// <summary>
    /// The tables ARE the proof, so their shape is pinned: a row cannot vanish, and a backend cannot be dropped from one, without this test saying so. It is also one of the fixture's plain [Test]s:
    /// <c>JsExecutionTierRosterTests</c> counts attributes, and a fixture whose tests are all [TestCaseSource] counts as empty.
    /// </summary>
    [Test]
    public void TheTables_HaveTheirRows()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Names(VariableRows), Is.EqualTo("n1_local,n2_string,n3_loop_local,n4_field,n5_array_element,n6_mybase_new,n7_byref_with_optional,n8_assignment_and_return,n14_optional_byref"));
            Assert.That(Names(CopyInRows), Is.EqualTo("n9_literal,n10_mybase_literal,n12_const_call_loop,n13_eval_order,n15_loops"));
            Assert.That(VariableRows.Concat(CopyInRows).Select(p => p.Id).Distinct().Count(), Is.EqualTo(14), "no id twice");
            Assert.That(VariableRows.Concat(CopyInRows).Where(p => !p.HangSafe).Select(p => p.Id), Is.Empty, "every C# run here is hang-safe");

            // the variable rows run on C# and C++ everywhere, on MSIL everywhere but the field row (refused by name), and on no JavaScript at all (BL7002)
            Assert.That(VariableRows.Where(p => !p.Agrees.HasFlag(Bk.Msil)).Select(p => p.Id), Is.EqualTo(new[] { "n4_field" }));
            Assert.That(VariableRows.Where(p => p.Agrees.HasFlag(Bk.JavaScript)).Select(p => p.Id), Is.Empty, "JavaScript refuses a variable passed ByRef");
            Assert.That(VariableRows.Where(p => !p.Agrees.HasFlag(Bk.CSharp) || !p.Agrees.HasFlag(Bk.Cpp)).Select(p => p.Id), Is.Empty);

            // the copy-in rows run on all four
            Assert.That(CopyInRows.Select(p => p.Agrees), Is.All.EqualTo(Bk.All));

            // the refusal rows are rows of the variable table
            Assert.That(JsRefusedIds.Except(VariableRows.Select(p => p.Id)), Is.Empty);
        });
    }

    // ============================================================================================
    // A VARIABLE — the write is the caller's: C#, C++ and MSIL print vbc's answer
    // ============================================================================================

    /// <summary>
    /// C# writes `Sub New(ref int n)` and `new Box(ref p)` (a local, a String, a loop local, a field, an array element, `MyBase.New(ref m)`, beside an Optional, in an assignment and a Return, an
    /// Optional ByRef through an implicit and an explicit base constructor). M1 (a constructor parameter without IsByRef) is CS1615 here.
    /// </summary>
    [Test]
    public void AVariablePassedToAByRefConstructorParameter_WritesBack_OnCSharp()
        => AssertRowsPrintVbcsAnswer(Bk.CSharp, RowsFor(VariableRows, Bk.CSharp));

    /// <summary>C++ spells the parameter `int32_t&amp;` (a String one `std::string&amp;`) and aliases the argument. M1 prints `3,3` for `n1_local` and `5` for `n6_mybase_new`.</summary>
    [Test]
    public void AVariablePassedToAByRefConstructorParameter_WritesBack_OnCpp()
        => AssertRowsPrintVbcsAnswer(Bk.Cpp, RowsFor(VariableRows, Bk.Cpp));

    /// <summary>MSIL names the constructor from the DECLARATION (`.ctor(int32&amp;)`) and passes the variable's address: `newobj` and the base `.ctor` load through the same declaration the signature is built from.</summary>
    [Test]
    public void AVariablePassedToAByRefConstructorParameter_WritesBack_OnMsil()
        => AssertRowsPrintVbcsAnswer(Bk.Msil, RowsFor(VariableRows, Bk.Msil));

    // ============================================================================================
    // A VALUE — VB copies it in: all four backends print vbc's answer
    // ============================================================================================

    /// <summary>C# renders the copy-in temp IN PLACE: `new Box(ref (new int[] { 5 })[0])`, `: base(ref (new int[] { 7 })[0])`. M2 is CS1510, M3 is `n13`'s seed order and `n10`'s "needs a statement before the base call".</summary>
    [Test]
    public void AValuePassedToAByRefConstructorParameter_IsCopiedIn_OnCSharp()
        => AssertRowsPrintVbcsAnswer(Bk.CSharp, CopyInRows);

    /// <summary>C++ copies a literal or an expression into a braced temp the callee writes — and must never alias the Const `K` (M2 prints `21,31,21,603` for `n12`).</summary>
    [Test]
    public void AValuePassedToAByRefConstructorParameter_IsCopiedIn_OnCpp()
        => AssertRowsPrintVbcsAnswer(Bk.Cpp, CopyInRows);

    /// <summary>
    /// JavaScript RUNS them: its parameters are values already, and a copy-in argument has no storage a write could be lost from, so the BL7002 walk skips it (a variable is refused, below). Under Node.
    /// M2 is BL7002 here.
    /// </summary>
    [Test]
    public void AValuePassedToAByRefConstructorParameter_IsCopiedIn_OnJavaScript()
        => AssertRowsPrintVbcsAnswer(Bk.JavaScript, CopyInRows);

    /// <summary>MSIL loads the copy-in temp's address. M2 is the named refusal "a literal has no address" / "an expression's value lives in a temporary".</summary>
    [Test]
    public void AValuePassedToAByRefConstructorParameter_IsCopiedIn_OnMsil()
        => AssertRowsPrintVbcsAnswer(Bk.Msil, CopyInRows);

    // ============================================================================================
    // THE REFUSALS — by design, and the same as a method call's
    // ============================================================================================

    /// <summary>
    /// JavaScript refuses a VARIABLE passed to a ByRef constructor parameter — `New Box(p)` and `MyBase.New(m)` — with BL7002 naming the call, through the CLI (exit code non-zero, no stack trace),
    /// the CLI with `--optimize` and `CompileProjectFiles` (the generator's refusal). Before #144 it RAN: the constructor's parameter carried no ByRef, so the walk never saw it, and the program
    /// printed the unchanged variable.
    /// </summary>
    [Test]
    public void ByAVariableOnJavaScript_BL7002()
    {
        var failures = new List<string>();
        foreach (var (id, callee) in new[] { ("n1_local", "'New Box'"), ("n6_mybase_new", "'MyBase.New'") })
        {
            var row = VariableRows.Single(p => p.Id == id);
            foreach (var entry in Enum.GetValues<EntryPoint>())
            {
                var text = RefusalText(Bk.JavaScript, entry, row.Source);
                if (!text.Contains("BL7002") || !text.Contains("the call to " + callee)) failures.Add($"{id} {entry}: [{text.Split('\n')[0]}]");
                if (text.Contains("Unhandled exception")) failures.Add($"{id} {entry}: a stack trace");
            }
        }
        Assert.That(failures, Is.Empty, "JavaScript must refuse with BL7002 naming the call:\n" + string.Join("\n", failures));
    }

    /// <summary>
    /// MSIL refuses `New Box(Me.K)` BY NAME — "argument 1 of the ByRef call to 'Box.New' cannot be passed by reference — an expression's value lives in a temporary" — exactly as it refuses
    /// `b.Bump(Me.K)` on an ordinary method (the control, the same text on master): passing it by value would assemble, run and drop the write-back. C# and C++ run it (`n4_field`). Codegen only: no ilasm.
    /// </summary>
    [Test]
    public void MsilRefusesAFieldRead_AsItRefusesOnAnOrdinaryMethod()
    {
        const string control = """
            Class Box
                Sub Bump(ByRef n As Integer)
                    n = 100
                End Sub
            End Class

            Class Holder
                Public K As Integer = 5
                Sub Run()
                    Dim b As New Box()
                    b.Bump(Me.K)
                    Console.WriteLine(CStr(K))
                End Sub
            End Class

            Sub Main()
                Dim h As New Holder()
                h.Run()
            End Sub
            """;
        var ctor = VariableRows.Single(p => p.Id == "n4_field").Source;

        var failures = new List<string>();
        foreach (var (label, source, callee) in new[] { ("New Box(Me.K)", ctor, "'Box.New'"), ("b.Bump(Me.K) — master's refusal", control, "'Bump'") })
        {
            foreach (var entry in Enum.GetValues<EntryPoint>())
            {
                var text = RefusalText(Bk.Msil, entry, source);
                if (!text.Contains("the ByRef call to " + callee + " cannot be passed by reference") || !text.Contains("an expression's value lives in a temporary"))
                    failures.Add($"{label} {entry}: [{text.Split('\n')[0]}]");
            }
        }
        Assert.That(failures, Is.Empty, "MSIL must refuse a field read passed ByRef, by name:\n" + string.Join("\n", failures));
    }

    /// <summary>
    /// The text a backend REFUSES a program with, through <paramref name="entry"/>: the spawned CLI must exit non-zero (its output is returned), and CompileProjectFiles must compile and the
    /// generator throw <see cref="ForeignFeatureException"/> (its message is returned). A program that is NOT refused fails the calling test.
    /// </summary>
    private static string RefusalText(Bk backend, EntryPoint entry, string source)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-t144-refuse-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            if (entry == EntryPoint.ProjectRelease)
            {
                var path = Path.Combine(dir, "Main.bas");
                File.WriteAllText(path, source);
                var result = new BasicCompiler(new CompilerOptions { OptimizeAggressive = true }).CompileProjectFiles(new List<string> { path });
                Assert.That(result.HasErrors, Is.False, "the front end must accept the program (the refusal is the backend's): " + string.Join(" | ", result.AllErrors.Select(e => e.Message)));
                var ir = result.CombinedIR;
                Assert.That(ir, Is.Not.Null, "the project entry point produced no combined IR");
                var ex = Assert.Throws<ForeignFeatureException>(() =>
                {
                    if (backend == Bk.JavaScript) new JavaScriptCodeGenerator().Generate(ir);
                    else new MSILCodeGenerator().Generate(ir);
                }, $"{backend} must refuse the program through CompileProjectFiles");
                return ex!.Message;
            }

            File.WriteAllText(Path.Combine(dir, "Prog.bas"), source);
            var args = new List<string> { "Prog.bas", "--target=" + TempExec.TargetName(backend) };
            if (entry == EntryPoint.CliOptimize) args.Add("--optimize");
            var (exit, stdout, stderr) = CliTestHarness.RunProcess(CliTestHarness.CliPath(), args.ToArray(), dir, timeoutMs: 120_000);
            Assert.That(exit, Is.Not.Zero, $"CLI --target={TempExec.TargetName(backend)} {entry} must refuse the program:\n{stdout}{stderr}");
            return stdout + stderr;
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* temp */ } }
    }
}
