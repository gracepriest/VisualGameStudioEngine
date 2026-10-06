using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #208, RUN. VB runs a class's `Shared Sub New` ONCE, before the first Shared member access or the first instance creation (.NET's type initializer), and after the Shared field initializers. BasicLang
//  accepted the declaration and emitted it on every backend as a parameterless INSTANCE constructor: it ran on each `New` and never on a Shared access, so `C.F` read 0, and beside an instance `Sub New()` it was
//  a duplicate member. Now `IRClass.TypeInitializer` carries it and each backend runs it as the type initializer: C# a native `static C()`; MSIL the body of the class's one `.cctor`, after the Shared field
//  initializers, without `beforefieldinit`; JavaScript lazily, through `C.$typeInit()` called from constructors (before `super()`), Shared methods and Shared accessors, with every Shared field behind a static
//  accessor over a `$C$Name` slot; C++ `blTypeInit_()` called from every `ctor_` (before the base's), Shared method and Shared accessor, and at every Shared FIELD access as `(C::blTypeInit_(), C::F)`.
//
//  ORACLE: vbc. Every probe below is one of the implementer's `S/t208/probes` (P01-P19, `.bas` verbatim) or one of the test-writer's `S/t208/tw/probes` (X1-X3, X5, X6, P18b), each wrapped in a VB Module and run
//  with vbc (S/t136/tools/vbv2.py); its expected text is that run's output, never a backend's. The test-writer re-ran P01-P19 through vbc again before writing these and every answer matched its `.exp`.
//  ⚠ P15 (and X6) is the order a reader may not expect: vbc prints "derived init" BEFORE "base init". `New Derived()` runs Derived's type initializer first (it is Derived's first use); Base's runs when Derived's
//  constructor calls Base's. The assertion is vbc's, not "base first".
//
//  ENTRY POINTS: every probe goes through the spawned CLI, the CLI with `--optimize` and `BasicCompiler.CompileProjectFiles` (aggressive), on every backend it runs on: `TempExec.AssertMatchesInEveryEntryPoint`.
//  ⛔ Every probe is `HangSafe`: its C# leg runs in a child process with a time limit (`CSharpProcessRunner`, #256), never the in-process runner. A backend whose tool is missing (a C++ compiler, Node, ilasm)
//  SKIPS its cells: never a failure. The test is ignored only when no cell could run.
//  ⚠ Named "...ExecutionTests" and runs JavaScript under Node: it is in `JsExecutionTierRosterTests`' roster.
//
//  EXCLUDED CELLS (stated, each pre-existing and measured identical with no type initializer in the program):
//    * P18 (a Shared field passed ByRef) runs on C# and C++ only: JavaScript refuses a ByRef parameter by design (BL7002) and MSIL refuses a ByRef argument that is not a local. P18b is the same program without
//      the ByRef call, and runs on all four.
//    * P19 (a bare write to an inherited Shared field inside a derived Shared method) is not run on MSIL: it drops the write and prints 7 where VB prints 8 (#270). With no Shared ctor in the base it does too.
//
//  MUTANTS (each built for real from a plain source copy of `92a0e3cc` with ONE change, and run against THESE fixtures; the cases that go red, measured):
//    * M1 JavaScript sets `$typeInitRan` AFTER the body, so the body's own guarded Shared write re-enters it    -> 12 of 12 cases here: every JS cell whose initializer writes a Shared field dies with a RangeError (stack overflow)
//    * M2 MSIL runs the Shared Sub New body BEFORE the Shared field initializers in the merged `.cctor`         -> `ASharedFieldInitializer_RunsBeforeTheSharedCtorBody` (P07 prints 1, VB 6) and
//                                                                                                                  `CompoundAssignmentByRefAndAnArrayElement_...` (P18b: the Shared array is created after the body wrote it)
//    * M3 C++ runs the type-initializer guard AFTER the base's `ctor_`                                           -> `ConstructingADerivedClass_RunsItsSharedCtorBeforeTheBases` ONLY (P15 and X6 print "base init" first)
//    * M4 the IR keeps the Shared Sub New ALSO as an instance constructor                                        -> `AFieldAndAnAutoPropertySetInTheSharedCtor_...`, `ASharedCtorBesideAnInstanceCtor_...`,
//                                                                                                                  `ConstructingADerivedClass_...`, `TheProgramsThatRanRightBeforeTheFix_...` (it then runs per `New`)
//    * M5 C# emits no static constructor                                                                         -> 13 of 14 cases (every one but the diagnostics case), incl. `AClassWithNoSharedSubNew_...` whose control then has no `static C()`
//    * M6 C++ drops the Shared FIELD access guard                                                                -> 8 of 14: `AFieldAndAnAutoProperty...` (P01 prints 0), `TheSharedCtor_RunsOnFirstUse...` (P04 "main start | 0 | 0 | main end"),
//                                                                                                                  `CompoundAssignmentByRef...` (P18 no longer compiles), `ASharedFieldInitializer...`, `AClassNeverTouched...`,
//                                                                                                                  `TwoClasses...`, `ASharedCtorBody...`, and the text case (its control has no `(C::blTypeInit_(), C::F)`)
//    The diagnostics / emission fixture's own mutants (M7-M12) are listed in its header.
//
//  ⛔ KNOWN GAPS — listed, deliberately NOT tested (asserting one would pin a defect):
//    * JavaScript runs a GENERIC class's initializer once in total, where .NET runs it once per closed type (JS erases the type arguments). C++ is per instantiation, as .NET is.
//    * LLVM emits no body for the type initializer (it no longer emits it as an instance constructor either).
//    * `Public Shared Sub New` is still accepted; vbc says BC30480.
//    * A bare `MyBase.New()` (no arguments) inside a Shared Sub New is not detected: the parse records no arguments either way, so only `MyBase.New(args)` is BC30043.
//    * #295: C++ spells a ByRef or indexed Shared field of a class WITHOUT a Shared Sub New as `C->F` (a compile failure, before and after #208), and JavaScript / MSIL refuse a Shared field passed ByRef.
//    * #270: MSIL's inherited Shared write above.
//    * A Structure cannot declare a Shared member at all (the parser stops at `Public Shared Count`), so a Structure has no type initializer and the C++ Structure-constructor guard cannot be reached today.
// ================================================================================================

/// <summary>#208 — a `Shared Sub New` runs as the type initializer, on first use, on C#, C++, JavaScript and MSIL.</summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the g++, Node, ilasm and C# child runs share the machine with the spawned CLI
public class SharedConstructorExecutionTests
{
    /// <summary>
    /// One group of single-file probes, each on every backend it runs on, through every entry point. A failing cell is collected, not thrown, so every other one still reports and the failure text names
    /// the probe, the backend and the entry point. (A copy of the helper `OperandEvaluationOrderExecutionTests` keeps: each fixture owns its own.)
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
    /// (1) The field a `Shared Sub New` sets is what the FIRST qualified access reads, with no `New` before it: a Shared field (P01), a ReadOnly Shared auto-property assigned in it (P10) and a read-write Shared
    /// auto-property bumped with `+=` and then rewritten (X1). All were 0 / refused before. M5 (no C# static constructor) prints 0 for P01; M6 (no C++ field guard) prints 0 on C++.
    /// </summary>
    [Test]
    public void AFieldAndAnAutoPropertySetInTheSharedCtor_AreSeenByTheFirstQualifiedAccess()
        => AssertSingleFile(SharedCtorProbes.P01, SharedCtorProbes.P10, SharedCtorProbes.X1);

    /// <summary>
    /// (2) The other two first uses: an INSTANCE method reading the Shared field after `New C()` (P02), and a Shared FUNCTION called first (P03), which on C++ and JavaScript is guarded by the method itself
    /// rather than by an access site.
    /// </summary>
    [Test]
    public void AnInstanceMethodAfterNew_AndAFirstSharedCall_SeeWhatTheSharedCtorSet()
        => AssertSingleFile(SharedCtorProbes.P02, SharedCtorProbes.P03);

    /// <summary>
    /// (3) PRINT ORDER: the Shared ctor's own print comes AFTER `Main`'s "main start" and before the first value read (P04); a Shared STORE is a use (P11: `C.F = 1` runs the initializer first, so "init" prints
    /// and the stored 1 wins, not the initializer's 50); and the initializer reads state `Main` changed first, so it runs at first use, not at program start (P14: `G.Seed = 4` before `C.F` gives 400).
    /// An eager initializer (a JS `static {}` block, C++ before `main`) fails P04 and P14. M6 prints "main start | 0 | 0 | main end" on C++.
    /// </summary>
    [Test]
    public void TheSharedCtor_RunsOnFirstUse_NotAtProgramStart_AndASharedStoreIsAUse()
        => AssertSingleFile(SharedCtorProbes.P04, SharedCtorProbes.P11, SharedCtorProbes.P14);

    /// <summary>
    /// (4) TWO classes: each runs its own initializer at its own first use, in the order of use (P05: B, then A), and one initializer may use another class, which then runs its own inside it (P12: B's reads A.V, so
    /// "B init", "A init").
    /// </summary>
    [Test]
    public void TwoClasses_EachRunTheirOwnSharedCtor_OnTheirFirstUse_AndOneMayUseTheOther()
        => AssertSingleFile(SharedCtorProbes.P05, SharedCtorProbes.P12);

    /// <summary>(5) A class the program never touches never runs its Shared ctor: "never init" must not print (P09). The control that keeps this from being vacuous is the class beside it, which is used.</summary>
    [Test]
    public void AClassNeverTouched_NeverRunsItsSharedCtor()
        => AssertSingleFile(SharedCtorProbes.P09);

    /// <summary>
    /// (6) A Shared ctor BESIDE an instance ctor: the Shared one runs once and first, the instance one on every `New` (P06: "shared" once, then "inst 101", "inst 102"; P13: "init" once for two `New`s, whose
    /// class declares ONLY a Shared Sub New and so keeps VB's implicit instance constructor; X5: an instance ctor WITH an argument). It was a duplicate member before. M4 (the IR also keeps it as an instance
    /// constructor) fails P06 and P13 to compile on every backend.
    /// </summary>
    [Test]
    public void ASharedCtorBesideAnInstanceCtor_RunsOnce_AndTheInstanceCtorOnEveryNew()
        => AssertSingleFile(SharedCtorProbes.P06, SharedCtorProbes.P13, SharedCtorProbes.X5);

    /// <summary>
    /// (7) The Shared FIELD INITIALIZER runs before the Shared Sub New body: `Shared X As Integer = 5` and `X = X + 1` in the ctor gives 6 (P07). M2 (MSIL runs the body before the initializers) prints 1, as the
    /// initializer then overwrites the increment.
    /// </summary>
    [Test]
    public void ASharedFieldInitializer_RunsBeforeTheSharedCtorBody()
        => AssertSingleFile(SharedCtorProbes.P07);

    /// <summary>
    /// (8) INHERITANCE: `New Derived()` runs Derived's type initializer, THEN Base's (P15: "derived init", "base init", as vbc prints them: see the header). X6 puts a call in `MyBase.New(Compute())`: Derived's
    /// initializer runs before the argument is computed, which runs before the base constructor. M3 (C++ guard after the base's `ctor_`) prints "base init" first.
    /// </summary>
    [Test]
    public void ConstructingADerivedClass_RunsItsSharedCtorBeforeTheBases()
        => AssertSingleFile(SharedCtorProbes.P15, SharedCtorProbes.X6);

    /// <summary>
    /// (9) The CONTROLS, which ran right before #208 and must stay right: P16 (`Main` changes the state the initializer reads BEFORE its first `New`: 400), P17 (the initializer prints once, between "start" and
    /// "end"), and P08 (a Module's variable initializer, which is not a type initializer). An eager initializer breaks P16 and P17, which is why none is eager.
    /// </summary>
    [Test]
    public void TheProgramsThatRanRightBeforeTheFix_StillRunRight()
        => AssertSingleFile(SharedCtorProbes.P16, SharedCtorProbes.P17, SharedCtorProbes.P08);

    /// <summary>
    /// (10) A Shared field as an lvalue: `C.F += 1`, passed ByRef (`Bump(C.F)`), and a Shared array element `C.Arr(1) = 7` (P18, C# and C++: see the header for the JS / MSIL exclusion), and the same
    /// without the ByRef call on all four backends (P18b). The C++ spelling `(C::blTypeInit_(), C::F)` must stay an lvalue. M6 makes P18 a C++ compile failure.
    /// </summary>
    [Test]
    public void CompoundAssignmentByRefAndAnArrayElement_OnASharedField_RunTheSharedCtorFirst()
        => AssertSingleFile(SharedCtorProbes.P18, SharedCtorProbes.P18b);

    /// <summary>
    /// (11) A bare name that is an INHERITED Shared field, read (`Return B`) and written (`B = v + B`) inside a derived Shared member, runs the BASE's initializer first: only the derived class's own entry
    /// points ran its own (P19). C#, C++ and JavaScript; not MSIL (#270, see the header).
    /// </summary>
    [Test]
    public void ABareInheritedSharedField_InADerivedSharedMember_RunsTheBasesSharedCtor()
        => AssertSingleFile(SharedCtorProbes.P19);

    /// <summary>
    /// (12) A Shared Sub New BODY with a local, a `For` loop, an `If` and a String (X2), and with a `Try` / `Catch` (X3): the multi-block body is emitted inside the C# `static C()`, the MSIL `.cctor` (after the
    /// field initializers, with its own `.locals`), the JS `$typeInit` and the C++ `blTypeInit_`.
    /// </summary>
    [Test]
    public void ASharedCtorBody_WithLocalsALoopAndATry_RunsOnEveryBackend()
        => AssertSingleFile(SharedCtorProbes.X2, SharedCtorProbes.X3);
}

/// <summary>
/// The #208 probes. Sources are the implementer's `S/t208/probes` (P01-P19) and the test-writer's `S/t208/tw/probes` (X1-X3, X5, X6, P18b) verbatim, and every expected value is vbc's OWN output for it (see the
/// fixture header).
/// </summary>
internal static class SharedCtorProbes
{
    /// <summary>Every probe is HangSafe (#256): its C# leg runs in a time-limited child process.</summary>
    private static TempProbe P(string id, string source, string vb, Bk agrees = Bk.All) => new(id, source, vb, agrees, HangSafe: true);

    /// <summary>A Shared field set in the Shared Sub New, read first through `C.F`.</summary>
    internal static readonly TempProbe P01 = P("P01_field", """
        Class C
            Public Shared F As Integer
            Shared Sub New()
                F = 42
            End Sub
        End Class
        Sub Main()
            Console.WriteLine(C.F)
        End Sub
        """, "42");

    /// <summary>The Shared field read from an INSTANCE method after `New C()`.</summary>
    internal static readonly TempProbe P02 = P("P02_inst", """
        Class C
            Public Shared F As Integer
            Shared Sub New()
                F = 7
            End Sub
            Public Function GetF() As Integer
                Return F
            End Function
        End Class
        Sub Main()
            Dim c As New C()
            Console.WriteLine(c.GetF())
        End Sub
        """, "7");

    /// <summary>A Shared function called first, reading a Private Shared field.</summary>
    internal static readonly TempProbe P03 = P("P03_method", """
        Class C
            Private Shared F As Integer
            Shared Sub New()
                F = 5
            End Sub
            Public Shared Function Twice() As Integer
                Return F * 2
            End Function
        End Class
        Sub Main()
            Console.WriteLine(C.Twice())
        End Sub
        """, "10");

    /// <summary>PRINT ORDER: "cctor" prints at the first access, between "main start" and the first value, and once.</summary>
    internal static readonly TempProbe P04 = P("P04_order", """
        Class C
            Public Shared F As Integer
            Shared Sub New()
                Console.WriteLine("cctor")
                F = 3
            End Sub
        End Class
        Sub Main()
            Console.WriteLine("main start")
            Console.WriteLine(C.F)
            Console.WriteLine(C.F)
            Console.WriteLine("main end")
        End Sub
        """, "main start\ncctor\n3\n3\nmain end");

    /// <summary>Two classes, each with a Shared Sub New, used in the order B, A.</summary>
    internal static readonly TempProbe P05 = P("P05_two", """
        Class A
            Public Shared V As Integer
            Shared Sub New()
                Console.WriteLine("A init")
                V = 1
            End Sub
        End Class
        Class B
            Public Shared V As Integer
            Shared Sub New()
                Console.WriteLine("B init")
                V = 2
            End Sub
        End Class
        Sub Main()
            Console.WriteLine(B.V)
            Console.WriteLine(A.V)
            Console.WriteLine(A.V + B.V)
        End Sub
        """, "B init\n2\nA init\n1\n3");

    /// <summary>A Shared Sub New beside an instance Sub New: "shared" once, "inst" per `New`.</summary>
    internal static readonly TempProbe P06 = P("P06_both", """
        Class C
            Public Shared Count As Integer
            Public Id As Integer
            Shared Sub New()
                Count = 100
                Console.WriteLine("shared")
            End Sub
            Public Sub New()
                Count = Count + 1
                Id = Count
                Console.WriteLine("inst " & Id)
            End Sub
        End Class
        Sub Main()
            Dim a As New C()
            Dim b As New C()
            Console.WriteLine(a.Id & " " & b.Id & " " & C.Count)
        End Sub
        """, "shared\ninst 101\ninst 102\n101 102 102");

    /// <summary>A Shared field INITIALIZER (5) and a Shared Sub New that adds one: the initializer runs first.</summary>
    internal static readonly TempProbe P07 = P("P07_init", """
        Class C
            Public Shared X As Integer = 5
            Shared Sub New()
                X = X + 1
            End Sub
        End Class
        Sub Main()
            Console.WriteLine(C.X)
        End Sub
        """, "6");

    /// <summary>The control: a Module's variable initializer, no type initializer anywhere.</summary>
    internal static readonly TempProbe P08 = P("P08_module", """
        Module M
            Public V As Integer = 9
            Public Function Twice() As Integer
                Return V * 2
            End Function
        End Module
        Sub Main()
            Console.WriteLine(M.V)
            Console.WriteLine(M.Twice())
        End Sub
        """, "9\n18");

    /// <summary>`Never` is not touched, so its Shared Sub New never runs; `Used` is the control.</summary>
    internal static readonly TempProbe P09 = P("P09_untouched", """
        Class Never
            Public Shared V As Integer
            Shared Sub New()
                Console.WriteLine("never init")
                V = 1
            End Sub
        End Class
        Class Used
            Public Shared V As Integer
            Shared Sub New()
                Console.WriteLine("used init")
                V = 2
            End Sub
        End Class
        Sub Main()
            Console.WriteLine("start")
            Console.WriteLine(Used.V)
        End Sub
        """, "start\nused init\n2");

    /// <summary>A ReadOnly Shared auto-property assigned in the Shared Sub New (a CS0200 on C# before).</summary>
    internal static readonly TempProbe P10 = P("P10_roprop", """
        Class Ctx
            Public Shared ReadOnly Property S As Integer
            Shared Sub New()
                S = 11
            End Sub
        End Class
        Sub Main()
            Console.WriteLine(Ctx.S)
        End Sub
        """, "11");

    /// <summary>A Shared STORE is the first use: "init" prints, then the stored 1 wins over the initializer's 50.</summary>
    internal static readonly TempProbe P11 = P("P11_storefirst", """
        Class C
            Public Shared F As Integer
            Shared Sub New()
                Console.WriteLine("init")
                F = 50
            End Sub
        End Class
        Sub Main()
            C.F = 1
            Console.WriteLine(C.F)
        End Sub
        """, "init\n1");

    /// <summary>One initializer uses another class's Shared field: B's runs, and inside it A's.</summary>
    internal static readonly TempProbe P12 = P("P12_crossdep", """
        Class A
            Public Shared V As Integer
            Shared Sub New()
                Console.WriteLine("A init")
                V = 10
            End Sub
        End Class
        Class B
            Public Shared W As Integer
            Shared Sub New()
                Console.WriteLine("B init")
                W = A.V + 1
            End Sub
        End Class
        Sub Main()
            Console.WriteLine(B.W)
        End Sub
        """, "B init\nA init\n11");

    /// <summary>A class that declares ONLY a Shared Sub New (so it keeps the implicit instance constructor), built twice: "init" once.</summary>
    internal static readonly TempProbe P13 = P("P13_newonly", """
        Class C
            Shared Sub New()
                Console.WriteLine("init")
            End Sub
            Public Sub Hello()
                Console.WriteLine("hello")
            End Sub
        End Class
        Sub Main()
            Console.WriteLine("start")
            Dim x As New C()
            x.Hello()
            Dim y As New C()
            y.Hello()
        End Sub
        """, "start\ninit\nhello\nhello");

    /// <summary>The initializer reads a module global that `Main` changed BEFORE the first access: lazy gives 400, eager gives 100.</summary>
    internal static readonly TempProbe P14 = P("P14_global", """
        Module G
            Public Seed As Integer = 1
        End Module
        Class C
            Public Shared F As Integer
            Shared Sub New()
                F = G.Seed * 100
            End Sub
        End Class
        Sub Main()
            G.Seed = 4
            Console.WriteLine(C.F)
        End Sub
        """, "400");

    /// <summary>`New Derived()` runs Derived's initializer, THEN Base's (the order vbc prints).</summary>
    internal static readonly TempProbe P15 = P("P15_inherit", """
        Class Base
            Public Shared B As Integer
            Shared Sub New()
                Console.WriteLine("base init")
                B = 1
            End Sub
        End Class
        Class Derived
            Inherits Base
            Public Shared D As Integer
            Shared Sub New()
                Console.WriteLine("derived init")
                D = 2
            End Sub
        End Class
        Sub Main()
            Console.WriteLine("start")
            Dim x As New Derived()
            Console.WriteLine(Derived.D + Base.B)
        End Sub
        """, "start\nderived init\nbase init\n3");

    /// <summary>CONTROL, right before #208: `Main` changes the state the initializer reads, then the first `New` runs it (400).</summary>
    internal static readonly TempProbe P16 = P("P16_newseed", """
        Module G
            Public Seed As Integer = 1
        End Module
        Class C
            Public Shared F As Integer
            Shared Sub New()
                F = G.Seed * 100
            End Sub
        End Class
        Sub Main()
            G.Seed = 4
            Dim x As New C()
            Console.WriteLine(C.F)
        End Sub
        """, "400");

    /// <summary>CONTROL, right before #208: the initializer prints once, at the first `New`, between "start" and "end".</summary>
    internal static readonly TempProbe P17 = P("P17_onenew", """
        Class C
            Shared Sub New()
                Console.WriteLine("init")
            End Sub
        End Class
        Sub Main()
            Console.WriteLine("start")
            Dim x As New C()
            Console.WriteLine("end")
        End Sub
        """, "start\ninit\nend");

    /// <summary>`C.F += 1`, `Bump(C.F)` ByRef and a Shared array element, all first uses. C# and C++ only: JavaScript refuses the ByRef parameter (BL7002), MSIL the ByRef argument.</summary>
    internal static readonly TempProbe P18 = P("P18_lvalues", """
        Class C
            Public Shared F As Integer
            Public Shared Arr(2) As Integer
            Shared Sub New()
                Console.WriteLine("init")
                F = 10
            End Sub
        End Class
        Sub Bump(ByRef x As Integer)
            x = x + 5
        End Sub
        Sub Main()
            Console.WriteLine("start")
            C.F += 1
            Console.WriteLine(C.F)
            Bump(C.F)
            Console.WriteLine(C.F)
            C.Arr(1) = 7
            Console.WriteLine(C.Arr(1))
        End Sub
        """, "start\ninit\n11\n16\n7", Bk.CSharp | Bk.Cpp);

    /// <summary>A bare read and a bare write of an INHERITED Shared field in a derived Shared member. Not MSIL (#270: it drops the write).</summary>
    internal static readonly TempProbe P19 = P("P19_inheritbare", """
        Class Base
            Public Shared B As Integer
            Shared Sub New()
                Console.WriteLine("base init")
                B = 7
            End Sub
        End Class
        Class Derived
            Inherits Base
            Public Shared Function GetB() As Integer
                Return B
            End Function
            Public Shared Sub SetB(v As Integer)
                B = v + B
            End Sub
        End Class
        Sub Main()
            Console.WriteLine("start")
            Console.WriteLine(Derived.GetB())
            Derived.SetB(1)
            Console.WriteLine(Base.B)
        End Sub
        """, "start\nbase init\n7\n8", Bk.CSharp | Bk.Cpp | Bk.JavaScript);

    /// <summary>A read-write Shared auto-property: `+=`, then a rewrite from its own value. Set to 20 in the initializer.</summary>
    internal static readonly TempProbe X1 = P("X1_autoprop", """
        Class C
            Public Shared Property P As Integer
            Shared Sub New()
                Console.WriteLine("init")
                P = 20
            End Sub
        End Class
        Sub Main()
            Console.WriteLine("start")
            C.P += 2
            Console.WriteLine(C.P)
            C.P = C.P * 2
            Console.WriteLine(C.P)
        End Sub
        """, "start\ninit\n22\n44");

    /// <summary>A Shared Sub New body with a local, a For loop, an If and a String.</summary>
    internal static readonly TempProbe X2 = P("X2_body", """
        Class C
            Public Shared Total As Integer
            Public Shared Tag As String
            Shared Sub New()
                Dim i As Integer
                Dim s As String = ""
                For i = 1 To 4
                    If i Mod 2 = 0 Then
                        Total = Total + i
                    Else
                        s = s & i
                    End If
                Next
                Tag = s
                Console.WriteLine("init " & Total & " " & Tag)
            End Sub
        End Class
        Sub Main()
            Console.WriteLine("start")
            Console.WriteLine(C.Total)
            Console.WriteLine(C.Tag)
        End Sub
        """, "start\ninit 6 13\n6\n13");

    /// <summary>A Shared Sub New body with a Try / Catch and a local.</summary>
    internal static readonly TempProbe X3 = P("X3_try", """
        Class C
            Public Shared F As Integer
            Shared Sub New()
                Dim n As Integer = 0
                Try
                    n = 10
                    Throw New Exception("boom")
                Catch ex As Exception
                    F = n + 5
                End Try
                Console.WriteLine("init " & F)
            End Sub
        End Class
        Sub Main()
            Console.WriteLine("start")
            Console.WriteLine(C.F)
        End Sub
        """, "start\ninit 15\n15");

    /// <summary>An instance constructor WITH an argument beside the Shared Sub New, run twice: "init" once, then 101 and 102.</summary>
    internal static readonly TempProbe X5 = P("X5_ctorargs", """
        Class C
            Public Shared Made As Integer
            Public V As Integer
            Shared Sub New()
                Console.WriteLine("init")
                Made = 100
            End Sub
            Public Sub New(v As Integer)
                Me.V = v
                Made = Made + 1
            End Sub
        End Class
        Sub Main()
            Console.WriteLine("start")
            Dim a As New C(5)
            Dim b As New C(6)
            Console.WriteLine(a.V + b.V)
            Console.WriteLine(C.Made)
        End Sub
        """, "start\ninit\n11\n102");

    /// <summary>`MyBase.New(Compute())`: the derived initializer runs before the argument is computed, which runs before the base constructor.</summary>
    internal static readonly TempProbe X6 = P("X6_baseargs", """
        Function Compute() As Integer
            Console.WriteLine("compute")
            Return 3
        End Function
        Class B
            Public V As Integer
            Public Sub New(v As Integer)
                Console.WriteLine("base ctor")
                Me.V = v
            End Sub
        End Class
        Class D
            Inherits B
            Shared Sub New()
                Console.WriteLine("derived init")
            End Sub
            Public Sub New()
                MyBase.New(Compute())
                Console.WriteLine("derived ctor")
            End Sub
        End Class
        Sub Main()
            Console.WriteLine("start")
            Dim d As New D()
            Console.WriteLine(d.V)
        End Sub
        """, "start\nderived init\ncompute\nbase ctor\nderived ctor\n3");

    /// <summary>P18 without the ByRef call: `C.F += 1`, `C.F = C.F * 2` and a Shared array element, with the array also set in the initializer. All four backends.</summary>
    internal static readonly TempProbe P18b = P("P18b_lvalues_noref", """
        Class C
            Public Shared F As Integer
            Public Shared Arr(2) As Integer
            Shared Sub New()
                Console.WriteLine("init")
                F = 10
                Arr(0) = 4
            End Sub
        End Class
        Sub Main()
            Console.WriteLine("start")
            C.F += 1
            Console.WriteLine(C.F)
            C.F = C.F * 2
            Console.WriteLine(C.F)
            C.Arr(1) = 7
            Console.WriteLine(C.Arr(0) + C.Arr(1))
        End Sub
        """, "start\ninit\n11\n22\n11");
}
