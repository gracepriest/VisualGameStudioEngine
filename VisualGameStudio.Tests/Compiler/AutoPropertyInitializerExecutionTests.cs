using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #210, RUN. `Public Property P As Integer = 7` (an auto-property initializer, and its ReadOnly and Shared variants) was a PARSE error on every backend ("Unexpected token in class: '='"). Now the parser
//  reads `= expr`, the analyzer judges it as the field written in its place (`CheckDeclaredInitializer`) and refuses it on a Get/Set property and on an interface property (BC36714), and the IR places it:
//    * INSTANCE property: `Me.P = <constant>` as an IRFieldStore right after the base call, in EVERY instance constructor (`FoldPropertyInitializers` / `EmitPropertyInitializers`), and a class that declares
//      no constructor is synthesized one. The store goes THROUGH the property, so an Overridable one reaches a derived setter, as vbc does.
//    * SHARED property: `IRProperty.Initializer`, placed where each backend places a Shared FIELD's initializer: C# an auto-property initializer; C++ an inline static; JavaScript the static, or #208's guarded
//      `$C$P` slot; MSIL the class's one `.cctor`, after the Shared field initializers and before the #208 body, with no beforefieldinit.
//
//  ORACLE: vbc. Every probe below is one of the implementer's `S/t210/probes` / `S/t210/probes-port` (P01-P23, Q01-Q04, `.bas` verbatim) or one of the test-writer's `S/t210/tw/probes` (P02js, P08b), each wrapped in
//  a VB Module and run with vbc (S/t136/tools/vbv2.py; P19's file-level `Const` as a `Public Const` in its own Module, because the wrapper makes it Private); its expected text is that run's output, never a
//  backend's. The test-writer re-ran every probe through vbc again before writing these and every answer matched its `.exp`.
//
//  ENTRY POINTS: every probe goes through the spawned CLI, the CLI with `--optimize` and `BasicCompiler.CompileProjectFiles` (aggressive), on every backend it runs on: `TempExec.AssertMatchesInEveryEntryPoint`.
//  ⛔ Every probe is `HangSafe`: its C# leg runs in a child process with a time limit (`CSharpProcessRunner`, #256), never the in-process runner. A backend whose tool is missing (a C++ compiler, Node, ilasm)
//  SKIPS its cells: never a failure. The test is ignored only when no cell could run.
//  ⚠ Named "...ExecutionTests" and runs JavaScript under Node: it is in `JsExecutionTierRosterTests`' roster.
//
//  EXCLUDED CELLS (stated, each pre-existing and measured identical on master 621d1348 with NO property initializer in the program):
//    * P02 (a Decimal and a Long property) runs on C#, C++ and MSIL: JavaScript refuses `Decimal` (BL7007) and `Long` (BL7003). P02js is the same program without those two, and runs on all four.
//    * P08 (two constructors) runs on C#, C++ and MSIL: JavaScript has no constructor overloads and emits two `constructor`s (#238). P08b is the same program with ONE constructor, on all four.
//    * (P06 and Q01 / Q03 / Q04 used to run on C#, JavaScript and MSIL only: a bare store to a plain auto-property (`P = P + 10`, `S += 1`, `Count += 1`) was dropped on C++ (#254), with or without an initializer.
//      FIXED by #218 / #254 in `CppCodeGenerator.IsStorageAutoProperty`: all four now run on C++ too, and `CppAutoPropertyBareStoreExecutionTests` covers the shapes without an initializer.)
//    * P07 runs on all four. JavaScript's class-field timing (#234 family) shows only where a base constructor reads a property the DERIVED class initializes WITHOUT overriding (P07p prints "base sees undefined"
//      for vbc's 0) or where a derived Set override reads its own field (P20): both listed below, neither tested.
//
//  MUTANTS (each built for real from a plain source copy of the fix with ONE change, and run against THESE fixtures; the cases that go red, measured):
//    * M1 a DECLARED constructor skips the property initializers (only a synthesized one runs them)              -> 5 of 11: `ADeclaredConstructor_...` (P06 prints 1 | 0 | 3 |  | 10 for 1 | 2 | 3 | q | 12), `AReadOnlyInitializer_...`
//                                                                                                                 (P08 prints 0 | 0 | 70 | 10 | 3 | 0), `MyBaseWithArguments_...` (P17: no Tag, N or Z), `ABaseConstructorReading...` (P07: the
//                                                                                                                 base declares a constructor, so it sees 0) and `ASynthesizedConstructor_...` (P23: the Base's own declared one, "base 5 0")
//    * M2 no constructor is synthesized for a class that only has initializers to run                              -> 6 of 11: `AnIntegerAndAString...` (P01 prints 0 | 9 | 0 | 0), `AReadOnlyInitializer_...` (P03 0 | 0),
//                                                                                                                 `ABaseConstructorReading...` (P07: the derived 5 never lands), `ASynthesizedConstructor_...` (P22 0 | 0),
//                                                                                                                 `Nothing_Widening...` (P18 all 0; P12 on JavaScript prints False), `AClassWithOnlyASharedSubNew_...` (Q02 0 | 0)
//    * M3 MSIL's `.cctor` leaves a Shared auto-property's backing field alone                                      -> 4 of 11, MSIL cells only: `ASharedPropertyInitializer_IsSeenWithNoNew...` (P04 prints 0 | 1 |  | 0),
//                                                                                                                 `Nothing_Widening...` (P18 / P19: the Shared ones print 0), `ASharedPropertyInitializer_RunsBeforeTheSharedSubNewBody` (Q01 prints 1,
//                                                                                                                 Q04 2 | 3 | 3) and `ASharedMethodCalledFirst_...` (Q03: "init sees 0")
//    * M4 the analyzer analyzes a property initializer and never judges it (no Nothing / conversion / BC30439)     -> `AutoPropertyInitializerDiagnosticsTests` (P11 reports nothing) and `AnIntegerAndAString...` (the Decimal property of
//                                                                                                                 P02 is no longer retyped: C# CS0664, MSIL "a float64 value reaches a Decimal slot")
//    * M5 BC36714 is never reported on a Get/Set property (the initializer is accepted and silently dropped)       -> `AutoPropertyInitializerDiagnosticsTests` ONLY (P13 reports nothing)
//    * M6 C# drops a Shared auto-property's initializer                                                            -> 4 of 11, C# cells only: the same four as M3 (P04 0 | 1 |  | 0; Q01 1; Q03 "init sees 0"; P18 / P19's Shared ones 0)
//
//  ⛔ KNOWN GAPS — listed, deliberately NOT tested (asserting one would pin a defect, or vbc agrees with the refusal only for the property's own reasons):
//    * `As New T(...)` on a property (`Property P As New C()`) is refused by the parser, as it is on a field declared without `Dim`. It would be refused anyway: it is not a constant.
//    * A NON-constant initializer (`= Twice(3)`) is refused, as it is on a field (#236); vbc runs it. The refusal is pinned only as PARITY with the field, in `AutoPropertyInitializerDiagnosticsTests`.
//    * Within one class, field initializers run before property initializers. With constant initializers that is observable only through a derived Set that reads a field of the same object.
//    * LLVM places nothing new.
//    * JavaScript has no Decimal (BL7007) and no Long (BL7003).
//    * (C++ dropped a bare store to a plain auto-property, #254: P06, Q01, Q03, Q04. FIXED by #218 / #254, see above; no longer a gap.)
//    * JavaScript class-field timing (P07p, P20): the property is installed after `super()` returns, so a base constructor's virtual call sees `undefined`, and a derived Set override that reads a field
//      sees it before it is assigned.
//    * JavaScript overloaded constructors (#238): P08.
//    * MSIL generic `As T` (#239): a property of a generic class `Property P As T = ...`.
//    * (C++ `Inherits Exception`, #151, did not compile with or without a property initializer. FIXED by #151's C++ half: `CppUserExceptionExecutionTests`; no longer a gap.)
// ================================================================================================

/// <summary>#210 — an auto-property initializer parses and runs, on C#, C++, JavaScript and MSIL.</summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the g++, Node, ilasm and C# child runs share the machine with the spawned CLI
public class AutoPropertyInitializerExecutionTests
{
    /// <summary>
    /// One group of single-file probes, each on every backend it runs on, through every entry point. A failing cell is collected, not thrown, so every other one still reports and the failure text names
    /// the probe, the backend and the entry point. (A copy of the helper `SharedConstructorExecutionTests` keeps: each fixture owns its own.)
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
    /// (1) An Integer initializer, an arithmetic one, and a String / Double / Single / Boolean (and, off JavaScript, Long and Decimal) one, read after `New C()`; a Set overwrites it on THAT instance only, and a
    /// second `New` starts from the initializer again (P01, P02, P02js). All were a parse error before. M2 (no synthesized constructor) prints 0 | 9 | 0 | 0 for P01; M4 (the analyzer never judges the initializer) fails P02's Decimal property.
    /// </summary>
    [Test]
    public void AnIntegerAndAStringInitializer_AreSeenAfterNew_AndASetOverwritesOnlyThatInstance()
        => AssertSingleFile(AutoPropertyInitProbes.P01, AutoPropertyInitProbes.P02, AutoPropertyInitProbes.P02js);

    /// <summary>
    /// (2) A ReadOnly property with an initializer (P03: read from outside and from a method of the class), and a ReadOnly one OVERWRITTEN in the class's own constructor, which sees the initializer first
    /// (P08 with two constructors, one that takes an argument; P08b with one). M1 (a declared constructor skips the initializers) prints 0 | 0 | 70 | 10 | 3 | 0 for P08; M2 prints 0 | 0 for P03.
    /// </summary>
    [Test]
    public void AReadOnlyInitializer_IsSeen_AndTheClassesOwnConstructorMayOverwriteIt()
        => AssertSingleFile(AutoPropertyInitProbes.P03, AutoPropertyInitProbes.P08, AutoPropertyInitProbes.P08b);

    /// <summary>
    /// (3) A Shared property, a Shared String one and a Shared ReadOnly one, read with NO `New` before the first read, then written (P04). C#: an auto-property initializer; C++: an inline static; JavaScript:
    /// the static; MSIL: the `.cctor`. M3 (the MSIL `.cctor` skips a Shared property) prints `0 | 1 |  | 0` on MSIL; M6 (C# drops the Shared initializer) prints 0 on C#.
    /// </summary>
    [Test]
    public void ASharedPropertyInitializer_IsSeenWithNoNew_AndTheProperty_IsStillWritable()
        => AssertSingleFile(AutoPropertyInitProbes.P04);

    /// <summary>
    /// (4) A DECLARED constructor runs the initializers first, in declaration order and interleaved with the field initializers' values, then its own body: the body reads 1, 2, 3, q (the field and the property
    /// values) and then writes `P = P + 10` (P06; on C++ that store was dropped until #218 / #254: the last line printed 2 for 12). M1 prints 1 | 0 | 3 |  | 10: the properties are never initialized when the class declares a constructor.
    /// </summary>
    [Test]
    public void ADeclaredConstructor_RunsTheInitializersBeforeItsBody_AndItsBodyWritesLast()
        => AssertSingleFile(AutoPropertyInitProbes.P06);

    /// <summary>
    /// (5) A base constructor reading an OVERRIDABLE property, and the derived class overriding it WITH an initializer (P07): vbc assigns THROUGH the property, after the base call, in every constructor, so the
    /// base's own `New Base()` sees 1 and so does the one the derived instance runs; the derived instance then holds 5 ("base sees 1 | base sees 1 | 5"). A field-slot initializer would run before the base
    /// call on C# (the base would see 5) and after `super()` on JavaScript. M2 (no synthesized constructor) prints `base sees 1 | base sees 1 | 1`: the derived override's 5 never lands. M1 prints
    /// `base sees 0 | base sees 0 | 5`: the base declares a constructor, and it no longer runs the base's initializer.
    /// </summary>
    [Test]
    public void ABaseConstructorReadingAnOverridableProperty_SeesTheBasesInitializer_AndTheDerivedOneLandsAfter()
        => AssertSingleFile(AutoPropertyInitProbes.P07);

    /// <summary>
    /// (6) A class that declares NO constructor, over a base whose constructor has an Optional parameter (P23: the synthesized constructor must fill the Optional AND run the initializers, base first), and a
    /// MustInherit base with a property initializer under a derived class with its own (P22). The implicit constructor existed before only for the Optional fill. M2 (no synthesis for initializers) prints
    /// 0 | 0 for P22; M1 prints `base 5 0 | 2` for P23 (the Base's own declared constructor skips its initializer).
    /// </summary>
    [Test]
    public void ASynthesizedConstructor_OverAnOptionalBase_AndAMustInheritBase_RunsTheInitializers()
        => AssertSingleFile(AutoPropertyInitProbes.P23, AutoPropertyInitProbes.P22);

    /// <summary>
    /// (7) The values the initializer takes: `Nothing` into a String, an Integer (its default, 0) and a class reference (P12); a numeric literal WIDENED to the property's type: Byte, Short, Double, Single,
    /// UInteger, and the same two on Shared properties (P18); a file-level `Const` and an arithmetic / `&amp;` constant over it, instance and Shared (P19). M3 (MSIL `.cctor` skips Shared properties) and M6 (C#
    /// drops the Shared initializer) print 0 for the last two P18 values and for P19's Shared one; M2 prints 0 for every instance value of P18 and, on JavaScript, False for P12's `Is Nothing`.
    /// </summary>
    [Test]
    public void Nothing_WideningAndAConstReference_AreTheInitializersValue()
        => AssertSingleFile(AutoPropertyInitProbes.P12, AutoPropertyInitProbes.P18, AutoPropertyInitProbes.P19);

    /// <summary>
    /// (8) `MyBase.New(x + 1)` in a derived constructor, over a base whose constructor reads ITS OWN property initializer, and a third class one more level down (P17): each constructor runs the initializers of
    /// its own class right after its base call, so the base prints "base 2 base" (its Tag already "base") and the derived "derived 3 base". M1 (declared constructors skip the initializers) prints
    /// `base 2  | derived 0  | 0 | base 11  | derived 0  | 0`: no Tag, no N, no Z.
    /// </summary>
    [Test]
    public void MyBaseWithArguments_RunsTheBasesInitializersBeforeTheBasesBody_AndEachClassItsOwnAfterTheBaseCall()
        => AssertSingleFile(AutoPropertyInitProbes.P17);

    /// <summary>
    /// (9) #208 x #210: a Shared property with an initializer AND a `Shared Sub New` that bumps it with `S += 1` (Q01: 5 then 6; the initializer runs BEFORE the Shared Sub New's body, as vbc orders them), and the
    /// same with an instance constructor that reads it (Q04: 100, bumped by the Shared ctor and again by each `New`). On C++ too since #218 / #254 (the bare `S += 1` / `Count += 1` stores used to be dropped there: Q01 printed 5 for 6, Q04 `100 | 100 | 100` for `102 | 103 | 103`). M3 (MSIL) and M6 (C#) print 1 for Q01 (the body runs, the initializer
    /// never did) and `2 | 3 | 3` for Q04.
    /// </summary>
    [Test]
    public void ASharedPropertyInitializer_RunsBeforeTheSharedSubNewBody()
        => AssertSingleFile(AutoPropertyInitProbes.Q01, AutoPropertyInitProbes.Q04);

    /// <summary>
    /// (10) #208 x #210: a class whose ONLY constructor is a `Shared Sub New` still gets the instance constructor VB gives it implicitly, and that one runs the instance property initializer (Q02: "start", "type
    /// init", 7, 7). #208 keeps the Shared constructor out of `Constructors`, so without the synthesized one the property reads 0.
    /// </summary>
    [Test]
    public void AClassWithOnlyASharedSubNew_StillInitializesItsInstanceProperties()
        => AssertSingleFile(AutoPropertyInitProbes.Q02);

    /// <summary>
    /// (11) #208 x #210: a Shared METHOD called first, with no `New` and no Shared access before it, runs the type initializer (Q03: "start", "init sees 5", 50, "t"): the Shared property's initializer (5) is what
    /// the Shared Sub New body reads, and the Shared String one is set too. On C++ too since #218 / #254 (its `S = S * 10` used to be dropped: it printed "init sees 5" then 5). M3 (MSIL) and M6 (C#) print "init sees 0".
    /// </summary>
    [Test]
    public void ASharedMethodCalledFirst_SeesTheSharedPropertyInitializer_InsideTheSharedSubNew()
        => AssertSingleFile(AutoPropertyInitProbes.Q03);
}

/// <summary>
/// The #210 probes. Sources are the implementer's `S/t210/probes` (P01-P23) and `S/t210/probes-port` (Q01-Q04) and the test-writer's `S/t210/tw/probes` (P02js, P08b) verbatim, and every expected value is vbc's OWN
/// output for it (see the fixture header).
/// </summary>
internal static class AutoPropertyInitProbes
{
    /// <summary>Every probe is HangSafe (#256): its C# leg runs in a time-limited child process.</summary>
    private static TempProbe P(string id, string source, string vb, Bk agrees = Bk.All) => new(id, source, vb, agrees, HangSafe: true);

    /// <summary>An Integer and a constant-expression Integer initializer; a Set on one instance does not touch the next.</summary>
    internal static readonly TempProbe P01 = P("P01_int", """
        Class C
            Public Property P As Integer = 7
            Public Property Q As Integer = 2 * 3 + 1
        End Class
        Sub Main()
            Dim c As New C()
            Console.WriteLine(c.P)
            c.P = 9
            Console.WriteLine(c.P)
            Dim d As New C()
            Console.WriteLine(d.P)
            Console.WriteLine(d.Q)
        End Sub
        """, "7\n9\n7\n7");

    /// <summary>String, Double, Single, Decimal, Long, Boolean. JavaScript refuses Decimal (BL7007) and Long (BL7003): see P02js.</summary>
    internal static readonly TempProbe P02 = P("P02_types", """
        Class C
            Public Property Name As String = "hello"
            Public Property D As Double = 2.5
            Public Property F As Single = 1.5
            Public Property M As Decimal = 1.25
            Public Property Big As Long = 5000000000
            Public Property B As Boolean = True
        End Class
        Sub Main()
            Dim c As New C()
            Console.WriteLine(c.Name)
            Console.WriteLine(c.D)
            Console.WriteLine(c.F)
            Console.WriteLine(c.M)
            Console.WriteLine(c.Big)
            Console.WriteLine(c.B)
        End Sub
        """, "hello\n2.5\n1.5\n1.25\n5000000000\nTrue", Bk.CSharp | Bk.Cpp | Bk.Msil);

    /// <summary>P02 without the Decimal and Long properties (JavaScript refuses both): all four backends.</summary>
    internal static readonly TempProbe P02js = P("P02js_types", """
        Class C
            Public Property Name As String = "hello"
            Public Property D As Double = 2.5
            Public Property F As Single = 1.5
            Public Property B As Boolean = True
        End Class
        Sub Main()
            Dim c As New C()
            Console.WriteLine(c.Name)
            Console.WriteLine(c.D)
            Console.WriteLine(c.F)
            Console.WriteLine(c.B)
        End Sub
        """, "hello\n2.5\n1.5\nTrue");

    /// <summary>ReadOnly with an initializer, read from outside and from a method of the class.</summary>
    internal static readonly TempProbe P03 = P("P03_readonly", """
        Class C
            Public ReadOnly Property R As String = "x"
            Public ReadOnly Property N As Integer = 4
            Public Function Twice() As Integer
                Return N * 2
            End Function
        End Class
        Sub Main()
            Dim c As New C()
            Console.WriteLine(c.R)
            Console.WriteLine(c.N)
            Console.WriteLine(c.Twice())
        End Sub
        """, "x\n4\n8");

    /// <summary>Shared Integer, String and ReadOnly, read with no `New` before.</summary>
    internal static readonly TempProbe P04 = P("P04_shared", """
        Class C
            Public Shared Property S As Integer = 3
            Public Shared Property T As String = "t"
            Public Shared ReadOnly Property K As Integer = 5
        End Class
        Sub Main()
            Console.WriteLine(C.S)
            C.S = C.S + 1
            Console.WriteLine(C.S)
            Console.WriteLine(C.T)
            Console.WriteLine(C.K)
        End Sub
        """, "3\n4\nt\n5");

    /// <summary>Field and property initializers, a declared constructor reading all four and then writing `P = P + 10`. On C++ too since #218 / #254 (the bare store used to be dropped, the last line printed 2).</summary>
    internal static readonly TempProbe P06 = P("P06_order", """
        Class C
            Public A As Integer = 1
            Public Property P As Integer = 2
            Public B As Integer = 3
            Public Property Q As String = "q"
            Public Sub New()
                Console.WriteLine(A)
                Console.WriteLine(P)
                Console.WriteLine(B)
                Console.WriteLine(Q)
                P = P + 10
            End Sub
        End Class
        Sub Main()
            Dim c As New C()
            Console.WriteLine(c.P)
        End Sub
        """, "1\n2\n3\nq\n12");

    /// <summary>A base constructor reads an OVERRIDABLE property; the derived class overrides it with an initializer of its own.</summary>
    internal static readonly TempProbe P07 = P("P07_basector", """
        Class Base
            Public Overridable Property V As Integer = 1
            Public Sub New()
                Console.WriteLine("base sees " & V)
            End Sub
        End Class
        Class Derived
            Inherits Base
            Public Overrides Property V As Integer = 5
        End Class
        Sub Main()
            Dim b As New Base()
            Dim d As New Derived()
            Console.WriteLine(d.V)
        End Sub
        """, "base sees 1\nbase sees 1\n5");

    /// <summary>The class's own constructors overwrite a property and a ReadOnly one; a second constructor takes an argument. Two constructors: not JavaScript (#238).</summary>
    internal static readonly TempProbe P08 = P("P08_overwrite", """
        Class C
            Public Property P As Integer = 7
            Public ReadOnly Property R As Integer = 1
            Public Sub New()
                Console.WriteLine(P)
                Console.WriteLine(R)
                P = 70
                R = 10
            End Sub
            Public Sub New(x As Integer)
                P = x
            End Sub
        End Class
        Sub Main()
            Dim a As New C()
            Console.WriteLine(a.P)
            Console.WriteLine(a.R)
            Dim b As New C(3)
            Console.WriteLine(b.P)
            Console.WriteLine(b.R)
        End Sub
        """, "7\n1\n70\n10\n3\n1", Bk.CSharp | Bk.Cpp | Bk.Msil);

    /// <summary>P08 with ONE constructor, constructed twice: all four backends.</summary>
    internal static readonly TempProbe P08b = P("P08b_overwrite1", """
        Class C
            Public Property P As Integer = 7
            Public ReadOnly Property R As Integer = 1
            Public Sub New()
                Console.WriteLine(P)
                Console.WriteLine(R)
                P = 70
                R = 10
            End Sub
        End Class
        Sub Main()
            Dim a As New C()
            Console.WriteLine(a.P)
            Console.WriteLine(a.R)
            Dim b As New C()
            Console.WriteLine(b.P)
            Console.WriteLine(b.R)
        End Sub
        """, "7\n1\n70\n10\n7\n1\n70\n10");

    /// <summary>`Nothing` into a String, an Integer and a class reference.</summary>
    internal static readonly TempProbe P12 = P("P12_nothing", """
        Class C
            Public Property S As String = Nothing
            Public Property I As Integer = Nothing
            Public Property O As C = Nothing
        End Class
        Sub Main()
            Dim c As New C()
            Console.WriteLine(c.S Is Nothing)
            Console.WriteLine(c.I)
            Console.WriteLine(c.O Is Nothing)
        End Sub
        """, "True\n0\nTrue");

    /// <summary>`MyBase.New(x + 1)`, a base constructor reading its own property initializer, and a third class.</summary>
    internal static readonly TempProbe P17 = P("P17_derivedargs", """
        Class Base
            Public Property Tag As String = "base"
            Public Sub New(x As Integer)
                Console.WriteLine("base " & x & " " & Tag)
            End Sub
        End Class
        Class Derived
            Inherits Base
            Public Property N As Integer = 3
            Public Sub New(x As Integer)
                MyBase.New(x + 1)
                Console.WriteLine("derived " & N & " " & Tag)
            End Sub
        End Class
        Class Plain
            Inherits Derived
            Public Property Z As Integer = 9
            Public Sub New()
                MyBase.New(10)
            End Sub
        End Class
        Sub Main()
            Dim d As New Derived(1)
            Console.WriteLine(d.N)
            Dim p As New Plain()
            Console.WriteLine(p.Z)
        End Sub
        """, "base 2 base\nderived 3 base\n3\nbase 11 base\nderived 3 base\n9");

    /// <summary>A numeric literal widened to Byte, Short, Double, Single, UInteger, and to Shared Double and Byte.</summary>
    internal static readonly TempProbe P18 = P("P18_widen", """
        Class C
            Public Property B As Byte = 65
            Public Property Sh As Short = -3
            Public Property D As Double = 2
            Public Property F As Single = 3
            Public Property U As UInteger = 9
            Public Shared Property SD As Double = 6
            Public Shared Property SB As Byte = 200
        End Class
        Sub Main()
            Dim o As New C()
            Console.WriteLine(o.B)
            Console.WriteLine(o.Sh)
            Console.WriteLine(o.D / 4)
            Console.WriteLine(o.F / 2)
            Console.WriteLine(o.U)
            Console.WriteLine(C.SD / 4)
            Console.WriteLine(C.SB)
        End Sub
        """, "65\n-3\n0.5\n1.5\n9\n1.5\n200");

    /// <summary>A file-level `Const`, referenced by an instance, a Shared and a String-concatenation initializer. (vbc's run: `Public Const` in its own Module, see the header.)</summary>
    internal static readonly TempProbe P19 = P("P19_constref", """
        Const BASE_HP As Integer = 40
        Class Unit
            Public Property HP As Integer = BASE_HP * 2
            Public Shared Property Count As Integer = BASE_HP + 1
            Public Property Name As String = "u" & "nit"
        End Class
        Sub Main()
            Dim u As New Unit()
            Console.WriteLine(u.HP)
            Console.WriteLine(Unit.Count)
            Console.WriteLine(u.Name)
        End Sub
        """, "80\n41\nunit");

    /// <summary>A MustInherit base with a property initializer, under a derived class with one of its own. (`MustOverride` does not parse: the base's function is Overridable.)</summary>
    internal static readonly TempProbe P22 = P("P22_abstract", """
        MustInherit Class Shape
            Public Property Sides As Integer = 4
            Public Overridable Function Area() As Integer
                Return 0
            End Function
        End Class
        Class Sq
            Inherits Shape
            Public Property W As Integer = 3
            Public Overrides Function Area() As Integer
                Return W * W
            End Function
        End Class
        Sub Main()
            Dim s As Shape = New Sq()
            Console.WriteLine(s.Sides)
            Console.WriteLine(s.Area())
        End Sub
        """, "4\n9");

    /// <summary>A derived class with NO constructor over a base whose constructor has an Optional parameter: the synthesized constructor fills the Optional and runs both classes' initializers.</summary>
    internal static readonly TempProbe P23 = P("P23_optbase", """
        Class Base
            Public Property Tag As Integer = 1
            Public Sub New(Optional a As Integer = 5)
                Console.WriteLine("base " & a & " " & Tag)
            End Sub
        End Class
        Class Derived
            Inherits Base
            Public Property N As Integer = 2
        End Class
        Sub Main()
            Dim d As New Derived()
            Console.WriteLine(d.N + d.Tag)
        End Sub
        """, "base 5 1\n3");

    /// <summary>A Shared property with an initializer AND a `Shared Sub New` that bumps it.</summary>
    internal static readonly TempProbe Q01 = P("Q01_sharedinit_ssn", """
        Class C
            Public Shared Property S As Integer = 5
            Shared Sub New()
                S += 1
            End Sub
        End Class
        Sub Main()
            Console.WriteLine(C.S)
        End Sub
        """, "6");

    /// <summary>A class whose ONLY constructor is a `Shared Sub New`, with an instance property initializer.</summary>
    internal static readonly TempProbe Q02 = P("Q02_ssn_only_instance", """
        Class C
            Public Property P As Integer = 7
            Shared Sub New()
                Console.WriteLine("type init")
            End Sub
        End Class
        Sub Main()
            Console.WriteLine("start")
            Dim o As New C()
            Console.WriteLine(o.P)
            Dim o2 As New C()
            Console.WriteLine(o2.P)
        End Sub
        """, "start\ntype init\n7\n7");

    /// <summary>A Shared METHOD called first; the Shared Sub New body reads the Shared property's initializer. Its `S = S * 10` is a bare store (dropped on C++ until #218 / #254).</summary>
    internal static readonly TempProbe Q03 = P("Q03_sharedinit_method_first", """
        Class C
            Public Shared Property S As Integer = 5
            Public Shared Property T As String = "t"
            Shared Sub New()
                Console.WriteLine("init sees " & S)
                S = S * 10
            End Sub
            Public Shared Function Read() As Integer
                Return S
            End Function
        End Class
        Sub Main()
            Console.WriteLine("start")
            Console.WriteLine(C.Read())
            Console.WriteLine(C.T)
        End Sub
        """, "start\ninit sees 5\n50\nt");

    /// <summary>A Shared property, a Shared Sub New and an instance constructor, each bumping `Count`.</summary>
    internal static readonly TempProbe Q04 = P("Q04_both_ctors", """
        Class C
            Public Shared Property Count As Integer = 100
            Public Property Id As Integer = 1
            Shared Sub New()
                Count += 1
            End Sub
            Public Sub New()
                Count += 1
                Id = Count
            End Sub
        End Class
        Sub Main()
            Dim a As New C()
            Dim b As New C()
            Console.WriteLine(a.Id)
            Console.WriteLine(b.Id)
            Console.WriteLine(C.Count)
        End Sub
        """, "102\n103\n103");
}
