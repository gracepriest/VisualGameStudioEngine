using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Tasks #218 and #254, RUN. On the C++ backend a COMPUTED store to a PLAIN auto-property (no Get/Set block), written by its bare name inside its own class, was silently DROPPED:
//  `P = P + 10`, `P += 20`, `Count += 1`, `S = S * 10`, `P++`, `P = Twice(P)`. No diagnostic, no crash, the program just ran with the old value (E16 printed 2 for 22, E27 0 for 7, PRa 0 for 30, V6shared
//  `10 0 6` for `12 4 6`). A plain `P = 5` still landed, which is why the defect hid: the IR does not emit an IRAssignment for a computed store, it NAMES the computed value after its assignment target
//  (`P = P + 10` is an IRBinaryOp called `P`), and the C++ backend honours that name only when `IsNamedDestination` recognises it. It knew parameters, locals, globals and fields, not a plain auto-property,
//  although `GenerateProperty` gives one a data member of its own name (ADR-0007) — so the value decayed to a temp, `t0 = P + 10;`.
//  The fix is in `CppCodeGenerator` only: `IsNamedDestination` also answers true for a plain (not accessor-backed) auto-property of the emitting class or a base (`IsStorageAutoProperty`: the walk goes up the
//  base chain, the NEAREST declaration decides), and `GetValueName`'s two destination arms — the #208 inherited-Shared guard and the naming arm — both ask it. It holds for an instance or a Shared property, in a
//  constructor, a method, a `Shared Sub New`, a lambda, a generic class and an inherited property.
//  ⛔ The property is deliberately NOT added to `_declaredIdentifiers` beside the fields: that set also decides whether a name SHADOWS A TYPE, and a property is often named after its own type. Measured:
//  with `Property DateTime As DateTime`, `DateTime.Now` inside the class went from `BasicLang::DateTime::Now()` (runs) to `DateTime.Now` (does not compile). A28 below is that control.
//
//  ORACLE: vbc. Every probe is one of the implementer's `S/t218/probes*` (the A-numbers are theirs, `.bas` verbatim), wrapped in a VB Module and run with vbc (S/t136/tools/vbv2.py); its expected text is that
//  run's output, never a backend's. The test-writer re-ran every probe through vbc again before writing these and every answer matched its `.exp`. Two exceptions, each stated at the probe: B01 is the
//  test-writer's own (a constructor that takes the value, so the store is not folded to a constant), and A10 uses `++` / `--`, which VB does not have (its expected text is the same program written
//  `+= 1` / `-= 1`, run with vbc, and C# agrees).
//
//  ENTRY POINTS: every probe goes through the spawned CLI, the CLI with `--optimize` and `BasicCompiler.CompileProjectFiles` (aggressive), on C++ and, as a REFERENCE that the expected text is not a C++ artefact, on
//  C#: `TempExec.AssertMatchesInEveryEntryPoint`. ⛔ Every probe is `HangSafe`: its C# leg runs in a child process with a time limit (`CSharpProcessRunner`, #256), never the in-process runner. A backend whose
//  tool is missing (a C++ compiler) SKIPS its cells: never a failure. The test is ignored only when no cell could run.
//  ⚠ Named "...ExecutionTests" but it runs NO Node: JavaScript and MSIL already answered these programs (they are in the implementer's 396-cell matrix, unchanged by the fix). So it is listed under
//  `JsExecutionTierRosterTests.NotJavaScriptExecution`, not in the roster, and the roster's count is unchanged.
//
//  MUTANTS (each built for real from a plain source copy of the fix with ONE change, and run against THESE tests through a copy of the test output with BasicLang.dll swapped; the cases that go red, measured):
//    * M0 the UNFIXED source (master before #218)                                                       -> 8 of 9: every case but `Controls_...`. A03 prints 2 for 42, A04 `1 | 1` for `33 | 33`, B01 2 for 32, A06 3 for 30, A07 5 for 50,
//                                                                      A08 `0 | 0` for `2 | 7`, A19 `1,0,9 | 2,1,9`, A13 7 for 14, A29 100 for 8, A10 `7 0 | 7 0`, A09 `5 0 | 5 0`, A11 `5 | 3`, A20 `0 | 1` for `9 | 14`.
//                                                                      ⚠ A21 (a store INSIDE a lambda) was already right before the fix: the closure stores through Me. It is kept as the lambda row, not as a killer; A20 is the lambda-shaped killer.
//    * M1 the walk stops at the emitting class (an INHERITED plain auto-property is no destination)     -> 2 of 9: `AnInheritedProperty_...` (A13 prints 7 for 14) and `AnInheritedSharedProperty_...` (A29 100 for 8)
//    * M2 only an INSTANCE plain auto-property is a destination (every Shared one loses its store)       -> 4 of 9: `ASharedProperty_...` (A06 3 for 30, A07 5 for 50), `ASharedCounter_...` (A08 `0 | 0`, A19 `1,0,9 | 2,1,9`),
//                                                                      `AnInheritedSharedProperty_...` (A29 100) and `IncrementAndDecrement_...` (A10 `7 0 | 5 0`: the Shared `S++` is lost)
//    * M3 only a SHARED plain auto-property is a destination (every instance one loses its store)        -> 5 of 9: `AnInstanceProperty_...` (A03 2, A04 `1 | 1`, B01 2), `AnInheritedProperty_...` (A13 7), `AChainedStore_...` (A09 `5 0 | 5 0`,
//                                                                      A11 `5 | 3`), `IncrementAndDecrement_...` (A10 `7 1 | 7 -1`) and `ALambdaAndAGenericClass_...` (A20 `0 | 1`)
//    * M4 ANY property is a destination (the accessor-backed exclusion dropped)                          -> 0 of 9: SURVIVES, and is EQUIVALENT (ADR-0007): an accessor-backed property never reaches the backend as a bare name or a
//                                                                      destination (it arrives as `Me.P`), so the exclusion is never asked about one. No test can kill it.
//    * M5 the #208 inherited-Shared guard keeps asking `_declaredIdentifiers` (a destination property is not guarded)  -> 1 of 9, `AnInheritedSharedProperty_...` ONLY (A29: the store lands in the base's `S` BEFORE its type initializer
//                                                                      has run, and the initializer's 100 then overwrites it: prints 100 where vbc prints 8)
//    * M6 the REJECTED first design: the unfixed source plus every plain auto-property registered in `_declaredIdentifiers` beside the fields  -> 1 of 9, `Controls_...` ONLY (A28: the C++ does not compile, `no viable overloaded '='`,
//                                                                      because `DateTime.Now` stopped binding to the TYPE). Nothing else notices this design: every store case passes under it.
//
//  ⛔ KNOWN GAPS — listed, deliberately NOT tested (asserting one would pin a defect). Each is outside what #218 / #254 fix, and is the same before and after:
//    * A member named like its TYPE does not compile on C++ (A22: `Property Box As Box` beside `Class Box`): the accessor `std::shared_ptr<Box> get_Box()` is emitted after the member `Box`, which then names the member.
//    * A `Property` inside a `Structure` (A23) or a `Module` (A24) is refused by the PARSER on every backend ("Expected member name but found Property"), so neither can be stored to bare. (#230: a Structure's Property parses now, and `StructureMembersExecutionTests` runs a Structure method that writes its own GET/SET property bare, `V = V + 1`; a bare store to a plain AUTO-property of a Structure is not tested there; a Module's is still refused.)
//    * Pre-existing and not C++: MSIL throws a NullReferenceException on a String property in a loop (A12), and does not run a generic class's `As T` property (A20); MSIL prints 100 for 8 on an inherited Shared FIELD of a
//      base with a type initializer (A30, #270's family); JavaScript fails on a property named after a .NET type and a Shared access through it (A22, A25, A28: `Cannot read properties of null (reading 'Now')`);
//      C# is CS0176 on A25 (a Get/Set property named like its type, then a Shared member through the type).
// ================================================================================================

/// <summary>#218 / #254 — a bare store to a plain auto-property lands on the C++ backend.</summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the g++ / clang++ and C# child runs share the machine with the spawned CLI
public class CppAutoPropertyBareStoreExecutionTests
{
    /// <summary>
    /// One group of single-file probes, each on C++ and C#, through every entry point. A failing cell is collected, not thrown, so every other one still reports and the failure text names the probe, the backend
    /// and the entry point. (A copy of the helper `AutoPropertyInitializerExecutionTests` keeps: each fixture owns its own.)
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
    /// (1) An INSTANCE property stored by its bare name: `P += 20` in a method (A03: 2 -> 22 -> 42), `P = P + 10` then `P = P * k` in a method that returns it (A04: 1 -> 11 -> 33), and `P = k`, `P += 20`,
    /// `P = P + 10` in a constructor that takes the value (B01: the argument keeps the store from folding to a constant, which is why A01 / A02, with literals, were right before the fix). Before the fix, and under
    /// M3 (Shared only), A03 prints 2, A04 `1 | 1` and B01 2.
    /// </summary>
    [Test]
    public void AnInstanceProperty_StoredByItsBareName_InAConstructorAndAMethod()
        => AssertSingleFile(CppBareStoreProbes.A03, CppBareStoreProbes.A04, CppBareStoreProbes.B01);

    /// <summary>
    /// (2) A SHARED property stored by its bare name: `S = 2 : S += 1 : S = S * 10` in a Shared method (A06: 30) and `S = 4 : S += 1 : S = S * 10` in a `Shared Sub New` (A07: 50, the type initializer, #208).
    /// M2 (instance only) prints 3 for A06 and 5 for A07.
    /// </summary>
    [Test]
    public void ASharedProperty_StoredByItsBareName_InASharedMethodAndASharedSubNew()
        => AssertSingleFile(CppBareStoreProbes.A06, CppBareStoreProbes.A07);

    /// <summary>
    /// (3) `Count += 1` and `Total = Total + k` from an INSTANCE constructor into SHARED properties (A08: two `New`s, 2 and 7), and a Private Shared `Count = Count + 1` from an instance method, the property declared
    /// BELOW its use (A19, the shape #274 listed as "a Shared-property increment is wrong on C++": `2,1,9`, then `4,2,9`). The store crosses from an instance member to a Shared property. M2 (instance only) prints
    /// `0 | 0` for A08 and `1,0,9 | 2,1,9` for A19.
    /// </summary>
    [Test]
    public void ASharedCounter_BumpedFromAnInstanceMember()
        => AssertSingleFile(CppBareStoreProbes.A08, CppBareStoreProbes.A19);

    /// <summary>
    /// (4) An INHERITED property: the derived constructor stores the base's plain auto-property by its bare name, `P = 3 : P += 4 : P = P * 2` (A13: 14). The walk has to go up the base chain. The plain `P = 3` lands
    /// and both computed stores are lost, so it prints 7: before the fix, and under M1 (own class only) and M3 (Shared only).
    /// </summary>
    [Test]
    public void AnInheritedProperty_StoredByItsBareName_InTheDerivedClass()
        => AssertSingleFile(CppBareStoreProbes.A13);

    /// <summary>
    /// (5) An inherited SHARED property of a base that has a `Shared Sub New` (A29): `S = Seed() + 1` in the derived class writes the base's `S` THROUGH #208's type-initializer guard, so the base's initializer
    /// (`S = 100`) runs first and the derived store (8) lands after it. Without the guard the store lands first and the initializer overwrites it: 100. M5 (the guard's gate still asks `_declaredIdentifiers`)
    /// kills ONLY this case; M1 (own class only) and M2 (instance only) print 100 here too.
    /// </summary>
    [Test]
    public void AnInheritedSharedProperty_OfABaseWithATypeInitializer_IsStoredThroughTheGuard()
        => AssertSingleFile(CppBareStoreProbes.A29);

    /// <summary>
    /// (6) `P++` / `P--` and `S++` / `S--` on an instance and a Shared property, in a constructor and a method (A10: `7 1`, then `5 -1`). M2 (instance only) loses the Shared steps (`7 0 | 5 0`) and M3 (Shared only) the instance ones (`7 1 | 7 -1`).
    /// </summary>
    [Test]
    public void IncrementAndDecrement_OfAPropertyByItsBareName_Land()
        => AssertSingleFile(CppBareStoreProbes.A10);

    /// <summary>
    /// (7) A chained store, `Q = P + 1 : R = Q * 10` (A09: each computed value is read by the next statement, 5 50, then 6 60 from a method), and a ReadOnly property assigned in its own constructor, `Q = k : Q += P :
    /// Q = Q * 2` (A11: the ReadOnly carve-out writes the data member, 5 and 16). Before the fix A09 prints `5 0 | 5 0` and A11 `5 | 3`; M3 (Shared only) prints the same.
    /// </summary>
    [Test]
    public void AChainedStore_AndAReadOnlyProperty_AssignedInItsOwnConstructor()
        => AssertSingleFile(CppBareStoreProbes.A09, CppBareStoreProbes.A11);

    /// <summary>
    /// (8) A LAMBDA that stores an instance and a Shared property by their bare names, called twice (A21: `22 4`; already right before the fix, the closure stores through Me, so it is the lambda ROW and not a
    /// killer), and a GENERIC class `Box(Of T)` whose method stores `N = N + k : N += 1`, beside a class whose method stores the result of a lambda call, `P = f(P) + 5` (A20: 9 and 14; `0 | 1` before the fix and
    /// under M3, Shared only).
    /// </summary>
    [Test]
    public void ALambdaAndAGenericClass_StoreAPlainAutoPropertyByItsBareName()
        => AssertSingleFile(CppBareStoreProbes.A21, CppBareStoreProbes.A20);

    /// <summary>
    /// (9) CONTROLS that were right before the fix and must stay right: a FIELD stored the same ways (K01: it was always a declared identifier), the property through `Me.P` (A05: an IRFieldStore, never a bare
    /// name), a property NAMED AFTER ITS OWN TYPE with `DateTime.Now` read inside the class (A28: it must still bind to the TYPE, the reason the property is not registered in `_declaredIdentifiers`; M6, the
    /// rejected registration, makes the C++ not compile), and a LOCAL that shadows the property (A26: `P = P + 1` stores the local, `Me.P` the property).
    /// </summary>
    [Test]
    public void Controls_AField_MeP_APropertyNamedAfterItsType_AndALocalShadowingAProperty_StillRun()
        => AssertSingleFile(CppBareStoreProbes.K01, CppBareStoreProbes.A05, CppBareStoreProbes.A28, CppBareStoreProbes.A26);
}

/// <summary>
/// The #218 / #254 probes. Sources are the implementer's `S/t218/probes*` verbatim (B01 and A10's comment aside, see the fixture header), and every expected value is vbc's OWN output for it. Every probe runs on
/// C++ and C# and is HangSafe (#256).
/// </summary>
internal static class CppBareStoreProbes
{
    private static TempProbe P(string id, string source, string vb) => new(id, source, vb, Bk.Cpp | Bk.CSharp, HangSafe: true);

    /// <summary>`P += 20` in a method, on an instance whose P was set from outside.</summary>
    internal static readonly TempProbe A03 = P("A03_method_compound", """
        Class C
            Public Property P As Integer
            Public Sub Bump()
                P += 20
            End Sub
        End Class
        Sub Main()
            Dim c As New C()
            c.P = 2
            c.Bump()
            c.Bump()
            Console.WriteLine(c.P)
        End Sub
        """, "42");

    /// <summary>`P = P + 10` then `P = P * k` in a Function that returns P.</summary>
    internal static readonly TempProbe A04 = P("A04_method_computed", """
        Class C
            Public Property P As Integer
            Public Function Bump(k As Integer) As Integer
                P = P + 10
                P = P * k
                Return P
            End Function
        End Class
        Sub Main()
            Dim c As New C()
            c.P = 1
            Console.WriteLine(c.Bump(3))
            Console.WriteLine(c.P)
        End Sub
        """, "33\n33");

    /// <summary>The test-writer's: a constructor that TAKES the value, so `P = k : P += 20 : P = P + 10` is not constant-folded (A01 / A02, with literals, were right before the fix).</summary>
    internal static readonly TempProbe B01 = P("B01_ctor_param", """
        Class C
            Public Property P As Integer
            Public Sub New(k As Integer)
                P = k
                P += 20
                P = P + 10
            End Sub
        End Class
        Sub Main()
            Dim c As New C(2)
            Console.WriteLine(c.P)
        End Sub
        """, "32");

    /// <summary>A Shared property stored in a Shared method.</summary>
    internal static readonly TempProbe A06 = P("A06_shared_method", """
        Class C
            Public Shared Property S As Integer
            Public Shared Sub Go()
                S = 2
                S += 1
                S = S * 10
            End Sub
        End Class
        Sub Main()
            C.Go()
            Console.WriteLine(C.S)
        End Sub
        """, "30");

    /// <summary>A Shared property stored in the `Shared Sub New` (the type initializer, #208).</summary>
    internal static readonly TempProbe A07 = P("A07_shared_ssn", """
        Class C
            Public Shared Property S As Integer
            Shared Sub New()
                S = 4
                S += 1
                S = S * 10
            End Sub
        End Class
        Sub Main()
            Console.WriteLine(C.S)
        End Sub
        """, "50");

    /// <summary>Shared properties stored from an INSTANCE constructor.</summary>
    internal static readonly TempProbe A08 = P("A08_shared_from_ictor", """
        Class C
            Public Shared Property Count As Integer
            Public Shared Property Total As Integer
            Public Sub New(k As Integer)
                Count += 1
                Total = Total + k
            End Sub
        End Class
        Sub Main()
            Dim a As New C(3)
            Dim b As New C(4)
            Console.WriteLine(C.Count)
            Console.WriteLine(C.Total)
        End Sub
        """, "2\n7");

    /// <summary>The derived constructor stores the BASE's plain auto-property by its bare name.</summary>
    internal static readonly TempProbe A13 = P("A13_inherited", """
        Class B
            Public Property P As Integer
        End Class
        Class D
            Inherits B
            Public Sub New()
                P = 3
                P += 4
                P = P * 2
            End Sub
        End Class
        Sub Main()
            Dim d As New D()
            Console.WriteLine(d.P)
        End Sub
        """, "14");

    /// <summary>An inherited SHARED property of a base with a `Shared Sub New`: the store must go through the type-initializer guard.</summary>
    internal static readonly TempProbe A29 = P("A29_inherited_shared_typeinit", """
        Class Base
            Public Shared Property S As Integer
            Shared Sub New()
                S = 100
            End Sub
        End Class
        Class D
            Inherits Base
            Private Shared Function Seed() As Integer
                Return 7
            End Function
            Public Shared Sub Bump()
                S = Seed() + 1
            End Sub
        End Class
        Sub Main()
            D.Bump()
            Console.WriteLine(Base.S)
        End Sub
        """, "8");

    /// <summary>
    /// `++` / `--` on an instance and a Shared property. VB has no `++`: the expected text is the same program written `+= 1` / `-= 1`, run with vbc (and C# agrees).
    /// </summary>
    internal static readonly TempProbe A10 = P("A10_incr", """
        Class C
            Public Property P As Integer
            Public Shared Property S As Integer
            Public Sub New()
                P = 6
                P++
                S++
            End Sub
            Public Sub Down()
                P--
                S--
            End Sub
        End Class
        Sub Main()
            Dim o As New C()
            Console.WriteLine(o.P & " " & C.S)
            o.Down()
            o.Down()
            Console.WriteLine(o.P & " " & C.S)
        End Sub
        """, "7 1\n5 -1");

    /// <summary>A chained store: each computed value is read by the next statement.</summary>
    internal static readonly TempProbe A09 = P("A09_chained", """
        Class C
            Public Property P As Integer
            Public Property Q As Integer
            Public Property R As Integer
            Public Sub New()
                P = 4
                Q = P + 1 : R = Q * 10
            End Sub
            Public Sub Again()
                Q = P + 2
                R = Q * 10
            End Sub
        End Class
        Sub Main()
            Dim c As New C()
            Console.WriteLine(c.Q & " " & c.R)
            c.Again()
            Console.WriteLine(c.Q & " " & c.R)
        End Sub
        """, "5 50\n6 60");

    /// <summary>ReadOnly properties assigned, and compounded, in their own constructor.</summary>
    internal static readonly TempProbe A11 = P("A11_readonly", """
        Class C
            Public ReadOnly Property P As Integer
            Public ReadOnly Property Q As Integer
            Public Sub New(k As Integer)
                P = 5
                Q = k
                Q += P
                Q = Q * 2
            End Sub
        End Class
        Sub Main()
            Dim c As New C(3)
            Console.WriteLine(c.P)
            Console.WriteLine(c.Q)
        End Sub
        """, "5\n16");

    /// <summary>A lambda that stores an instance and a Shared property by their bare names.</summary>
    internal static readonly TempProbe A21 = P("A21_lambda_write", """
        Class C
            Public Property P As Integer
            Public Shared Property S As Integer
            Public Sub Run()
                Dim f As Action = Sub()
                                      P = P + 1
                                      P += 10
                                      S = S + 2
                                  End Sub
                f()
                f()
            End Sub
        End Class
        Sub Main()
            Dim o As New C()
            o.Run()
            Console.WriteLine(o.P & " " & C.S)
        End Sub
        """, "22 4");

    /// <summary>A Private Shared property declared BELOW its use, stored by its bare name (`Count = Count + 1`) and by its class (`Box.Count = Box.Count + 1`) from an instance method (the shape #274 listed).</summary>
    internal static readonly TempProbe A19 = P("A19_P17sh", """
        Class Box
            Sub Work()
                Count = Count + 1
                Box.Count = Box.Count + 1
                Console.WriteLine(CStr(Count) & "," & CStr(Half(Count)) & "," & CStr(Tally))
            End Sub

            Private Shared Property Count As Integer
            Private Shared Tally As Integer = 9

            Private Shared Function Half(x As Integer) As Integer
                Return x \ 2
            End Function
        End Class

        Sub Main()
            Dim b As New Box()
            b.Work()
            b.Work()
        End Sub
        """, "2,1,9\n4,2,9");

    /// <summary>A generic class storing its plain property, and a method that reads a lambda's result into one.</summary>
    internal static readonly TempProbe A20 = P("A20_lambda_generic", """
        Class Box(Of T)
            Public Property N As Integer
            Public Sub Add(x As T, k As Integer)
                N = N + k
                N += 1
            End Sub
        End Class
        Class C
            Public Property P As Integer
            Public Sub Run()
                Dim f As Func(Of Integer, Integer) = Function(x) x + 1
                P = f(P) + 5
                P = P * 2
            End Sub
        End Class
        Sub Main()
            Dim b As New Box(Of String)()
            b.Add("a", 3)
            b.Add("b", 4)
            Console.WriteLine(b.N)
            Dim o As New C()
            o.P = 1
            o.Run()
            Console.WriteLine(o.P)
        End Sub
        """, "9\n14");

    /// <summary>CONTROL: a FIELD stored the same ways. It was always a declared identifier.</summary>
    internal static readonly TempProbe K01 = P("K01_field", """
        Class C
            Public P As Integer
            Public Shared S As Integer
            Public Shared Count As Integer
            Public Sub New()
                P = 2
                P += 20
                P = P + 10
                Count += 1
                Count = Count + 1
            End Sub
            Public Sub Bump()
                P += 1
                P = P * 2
            End Sub
            Public Shared Sub Go()
                S = 2
                S += 1
                S = S * 10
            End Sub
        End Class
        Sub Main()
            Dim o As New C()
            Console.WriteLine(o.P)
            o.Bump()
            Console.WriteLine(o.P)
            C.Go()
            Console.WriteLine(C.S)
            Console.WriteLine(C.Count)
        End Sub
        """, "32\n66\n30\n2");

    /// <summary>CONTROL: the property through `Me.`, which is a field store and never a bare name.</summary>
    internal static readonly TempProbe A05 = P("A05_me_qualified", """
        Class C
            Public Property P As Integer
            Public Sub New()
                Me.P = 2
                Me.P += 20
                Me.P = Me.P + 10
            End Sub
            Public Sub Bump()
                Me.P += 1
                Me.P = Me.P * 2
            End Sub
        End Class
        Sub Main()
            Dim c As New C()
            Console.WriteLine(c.P)
            c.Bump()
            Console.WriteLine(c.P)
        End Sub
        """, "32\n66");

    /// <summary>CONTROL: a property named after its own TYPE, with `DateTime.Now` read inside the class. VB binds `DateTime.Now` to the TYPE (Shared member through the property's name).</summary>
    internal static readonly TempProbe A28 = P("A28_prop_named_datetime", """
        Class Ev
            Public Property DateTime As DateTime
            Public Property N As Integer
            Public Sub Go()
                Dim y As Integer = DateTime.Now.Year
                If y > 2000 Then N = 5
                Console.WriteLine(N)
            End Sub
        End Class
        Sub Main()
            Dim e As New Ev()
            e.Go()
        End Sub
        """, "5");

    /// <summary>CONTROL: a LOCAL named like the property takes the store; `Me.P` reaches the property.</summary>
    internal static readonly TempProbe A26 = P("A26_local_shadows_prop", """
        Class C
            Public Property P As Integer
            Public Sub Go()
                Dim P As Integer = 5
                P = P + 1
                Console.WriteLine(P)
                Me.P = Me.P + 100
            End Sub
        End Class
        Sub Main()
            Dim o As New C()
            o.Go()
            o.Go()
            Console.WriteLine(o.P)
        End Sub
        """, "6\n6\n200");
}
