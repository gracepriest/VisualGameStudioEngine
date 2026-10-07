using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.JavaScript;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #230, RUN. A `Structure` declared only fields: `Parser.ParseStructure` read `[access] [Dim] name As T`, so a Sub, Function, Property, Operator, Const, Shared field or `Sub New` inside one was a
//  PARSE error ("Expected member name") on every backend, where VB allows all of them. Now the Structure body is read by the CLASS member parser and the non-field members are lowered by the class
//  builder, flagged `IsStruct`. What each backend had to learn to make the result a VALUE and not a copy:
//    * C#     a `struct` with members: nothing to change.
//    * C++    `Me` is `(*this)`; a method call, a field store and a property accessor on a Structure FIELD or ARRAY ELEMENT go through its STORAGE (`StructPlaceLValue`), never a temp copy. That also fixes the
//             pre-existing `o.S.V = 5` store into a Structure field of an object, which wrote a copy (it printed 0 where vbc prints 6). A declared constructor delegates to a defaulted `P() = default`.
//    * MSIL   a Structure always has `.ctor()`; its methods and accessors are `call`ed on the ADDRESS of the storage they are reached through (`TryEmitStructAddress`), a non-storage receiver on a boxed copy.
//    * JS     unchanged: BL7005 refuses every Structure, with methods or without (one row below).
//  And the front end reports VB's own diagnostics for it (`StructureMembersDiagnosticsTests`): BC36638, BC30629, BC31049, BC36713, BC30435, BC30269. `New P(args)` binds a declared constructor; `New P()` is
//  ALWAYS the zeroing parameterless one (vbc never chooses an all-Optional `Sub New` for it).
//
//  ORACLE: vbc. Every program below is the implementer's own probe (`S/t230/probes`: c01, c03, p01-p29, s4, verbatim or merged into one program with its neighbours) and its expected text is the output of
//  that program wrapped in a VB Module and run with vbc (`S/t136/tools/vbv2.py`; the test-writer's merged programs are `S/t230/tw/probes/*.bas`, each with its `.exp`), never a backend's answer.
//
//  ENTRY POINTS: every program goes through the spawned CLI, the CLI with `--optimize` and `BasicCompiler.CompileProjectFiles` (aggressive), on C#, C++ and MSIL: `TempExec.AssertMatchesInEveryEntryPoint`.
//  ⛔ Every program is `HangSafe`: its C# leg runs in a child process with a time limit (`CSharpProcessRunner`, #256), never in the in-process runner. A backend whose tool is missing (a C++ compiler, ilasm)
//  SKIPS its cells: never a failure. The test is ignored only when no cell could run.
//  ⚠ Named "...ExecutionTests" and runs NO JavaScript: it is in `JsExecutionTierRosterTests.NotJavaScriptExecution`, and the roster's count stays 131.
//
//  MUTANTS (each built for real from a plain source copy of the fix with ONE change and run against THESE two fixtures (16 cases); what goes red is measured):
//    * M1 BC36638 never reported                                   -> 1 of 16: `ALambdaUsingMeOrAnInstanceMember_IsBC36638` ONLY (`StructureMembersDiagnosticsTests`; the analyzer reports nothing). Without the front end the
//                                                                     backends disagree and no execution row notices, because a refused program runs nowhere: C# is CS1673, C++ RUNS it and prints the
//                                                                     copy's answer (7), MSIL refuses it by ClosureLowering's own ADR-0010 D6 text.
//    * M2 the class builder's `IsStruct` flag never set            -> 7 of 16: every execution row that declares a method (C# and MSIL: NullReferenceException, the Structure is a null class reference;
//                                                                     `ADeclaredConstructor_...`: C# CS7036, no zeroing constructor). NOT red: the fields-only rows (`AStoreInto...`, the control), which
//                                                                     keep the old path, and the JavaScript row, because JavaScript refuses the Structure TYPE at the `Dim s As P` as well as the flag.
//    * M3 C++ reaches a Structure field / element through a COPY   -> 3 of 16, C++ cells only: `AMutatingSub_OnAFieldOfAnObject_...` (r3a prints `0 | 5 | 0` for `6 | 6 | 1`, r3b `1 | 0 0` for `2 | 2 0`),
//                                                                     `AGetSetProperty_...` (r5c prints `0 0 0` for `18 22 0`) and `AStoreIntoAStructureField_...` (r8 prints `0 7 8` for `6 7 8`)
//    * M4 MSIL calls a Structure's method on a boxed COPY          -> 4 of 16, MSIL cells only: `AMutatingSub_OnALocal_...` (r1 prints `7 | 3 | 3 | 3 | 4` for `7 | 5 | 7 | 16 | 17`), `AMutatingSub_OnACopy_...`
//                                                                     (r2a `5 | 5 | 5 | 5 | 5 | 5 5`; r2b `3 3 | 3 1 2`), `AMutatingSub_OnAFieldOfAnObject_...` (r3a `5 | 5 | 0`, r3b `0 | 0 0`) and
//                                                                     `AGetSetProperty_...` (r5a `0 | 42 | 0`, r5b `0 | 0`, r5c `0 0 0`)
//    * M5 `New P()` binds a declared constructor of a Structure    -> 1 of 16: `ADeclaredConstructor_NewWithNoArguments_IsStillTheZeroingOne` ONLY, on all three backends: R7a is refused ("Compilation failed with 1 error(s)":
//                                                                     P declares no constructor that takes no arguments) and R7c prints `9 | 4 | 0` for vbc's `0 | 4 | 0`
//
//  ⛔ KNOWN GAPS — listed, deliberately NOT tested (asserting one would pin a defect, or the fix is another task's):
//    * `Implements` on a Structure and a generic `Structure S(Of T)` do not parse.
//    * A METHOD overload is refused, as it is for a Class.
//    * A Structure declared BELOW its first use is refused (a Class is not).
//    * LSP completion after `s.` lists a Structure's FIELDS only.
//    * A Protected Sub or Function is refused with BC30435; vbc says BC31067 for a method (BC30435 for a field or a property, which is pinned). Both refuse; the CODE differs for a method.
//    * LLVM: a Structure with methods is not lowered there.
//    * INHERITED from Class, measured identical on a Class (control `c04`): C# writes the MODULE global for a field assignment shadowed by a file-level `Dim` of the same name (`p20`: 6 1006 for vbc's 6 1000);
//      C++ emits `override` on `Overrides ToString` with no virtual base (`p30`, control `c30`: a Class fails the same way).
// ================================================================================================

/// <summary>#230 — a Structure declares methods, properties, Shared members and constructors, and they run as VB's value type does, on C#, C++ and MSIL.</summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C++, ilasm and C# child runs share the machine with the spawned CLI
public class StructureMembersExecutionTests
{
    /// <summary>
    /// One group of single-file programs, each on every backend it runs on, through every entry point. A failing cell is collected, not thrown, so every other one still reports and the failure text names
    /// the program, the backend and the entry point. (A copy of the helper `PropertyByRefCopyOutExecutionTests` keeps: each fixture owns its own.)
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
    /// The text JavaScript's GENERATOR refuses a program with, through <paramref name="entry"/>: the spawned CLI must exit non-zero (its output is returned), and CompileProjectFiles must compile and the
    /// generator throw <see cref="ForeignFeatureException"/> (its message is returned). A program that is NOT refused fails the calling test. (A copy of `PropertyByRefCopyOutExecutionTests`' helper.)
    /// </summary>
    private static string JavaScriptRefusalText(EntryPoint entry, string source)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-t230-refuse-" + Guid.NewGuid().ToString("N"));
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
                var ex = Assert.Throws<ForeignFeatureException>(() => new JavaScriptCodeGenerator().Generate(ir), "JavaScript must refuse the program through CompileProjectFiles");
                return ex!.Message;
            }

            File.WriteAllText(Path.Combine(dir, "Prog.bas"), source);
            var args = new List<string> { "Prog.bas", "--target=javascript" };
            if (entry == EntryPoint.CliOptimize) args.Add("--optimize");
            var (exit, stdout, stderr) = CliTestHarness.RunProcess(CliTestHarness.CliPath(), args.ToArray(), dir, timeoutMs: 120_000);
            Assert.That(exit, Is.Not.Zero, $"CLI --target=javascript {entry} must refuse the program:\n{stdout}{stderr}");
            return stdout + stderr;
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* temp */ } }
    }

    // ============================================================================================
    //  THE SHAPES — every one prints vbc's answer on C#, C++ and MSIL
    // ============================================================================================

    /// <summary>
    /// (1) The headline, on a LOCAL: an instance Function reading fields (`s.Sum()`), a Sub that writes a field called twice (`s.Bump()`), a Sub calling another by its bare name, and `Me.X = v * 2` / `Me.X + 1`
    /// inside a method. All were a parse error before. M4 (MSIL calls the method on a boxed copy) prints `7 | 3 | 3 | 3 | 4` on MSIL: the Sub's write never reaches `s`.
    /// </summary>
    [Test]
    public void AMutatingSub_OnALocal_AFunctionReadingFields_MeAndABareSelfCall_Run()
        => AssertSingleFile(StructureMembersProbes.Members);

    /// <summary>
    /// (2) A COPY is a copy: `Dim t As P = s` then `t.Bump()` leaves `s` alone (5 / 6); a ByVal argument is bumped inside the Sub and `s` is unchanged, a ByRef one is not (`6 | 5 | 6`); `Dim c As P = Me`
    /// inside a method snapshots the value before the next write (`106 6`) (R2a). A List ELEMENT and a `For Each` variable are copies too, so `lst(0).Bump()` changes nothing and the loop variable's bump
    /// never reaches the array (`3 3`, `5 1 2`) (R2b). The value semantics a Class would get wrong, on a type that now has methods to get it wrong with.
    /// </summary>
    [Test]
    public void AMutatingSub_OnACopy_LeavesTheOriginalAlone()
        => AssertSingleFile(StructureMembersProbes.Copies, StructureMembersProbes.ListAndForEachCopies);

    /// <summary>
    /// (3) THE edge the implementer flagged as risky, M3's and M4's killer: a Sub called on a Structure FIELD of an object (`o.S.Bump()`), on an ARRAY element (`a(0).Bump()`), and on a Structure inside a
    /// Structure, both from outside (`o.I.Bump()`) and from its container's own method (`I.Bump()`) mutate IN PLACE, as the storage is a place and not a value (R3a, R3b). M3 (C++ calls it on a temp copy)
    /// prints `0 | 5 | 0` (R3a) and `1 | 0 0` (R3b) on C++; M4 (MSIL boxes the receiver) prints `5 | 5 | 0` and `0 | 0 0` on MSIL: the bump lands on a copy.
    /// </summary>
    [Test]
    public void AMutatingSub_OnAFieldOfAnObject_AnArrayElement_AndANestedStructure_MutatesTheStorage()
        => AssertSingleFile(StructureMembersProbes.InPlace, StructureMembersProbes.Nested);

    /// <summary>
    /// (4) A method called on a Function's RESULT (`Make().Bumped()`): the call runs on a temporary, so it prints 41 and the next `Make()` is 40 again, while the same call on a local that holds the result
    /// bumps the local (`41 | 40 | 41 | 40`). There is no storage to reach here, so a backend that tried to take an address of a call result is wrong in the other direction.
    /// </summary>
    [Test]
    public void AMethod_OnAFunctionsResult_RunsOnATemporary()
        => AssertSingleFile(StructureMembersProbes.Result);

    /// <summary>
    /// (5) Properties: a Get/Set one whose Set and Get transform the value (`s.V = 4` reads 50), a ReadOnly one and a Shared ReadOnly one that returns a Structure (R5a); an AUTO-property pair, copied with
    /// `Dim t As P = s` and written through the copy (R5b); and a property reached through a field chain (R5c): `o.S.Bump()` and `arr(1).Bump()` run a method that WRITES a property, in place (`18 22`),
    /// while `o.Q.Bump()` on a Class PROPERTY of Structure type is a copy and changes nothing (`0`). M3 prints `0 0 0` for R5c on C++ (every accessor ran on a temp copy); M4 prints `0 | 42 | 0` for R5a and `0 | 0` for R5b on MSIL.
    /// </summary>
    [Test]
    public void AGetSetProperty_AReadOnlyOne_AnAutoProperty_AndAPropertyOnAFieldChain_Run()
        => AssertSingleFile(StructureMembersProbes.GetSet, StructureMembersProbes.AutoProperty, StructureMembersProbes.PropertyChain);

    /// <summary>
    /// (6) Shared members: a Shared field with an initializer, a `Const`, a Shared Property with an initializer, a Shared Function and an instance method that reads all of them (R6a: 50, 72, then 142 / 120
    /// after `P.K = 100`); a `Shared Sub New` that runs as the type initializer on first use (R6b: 42, 43); a lambda in a Structure method that captures a LOCAL and a Shared field and so is legal, which is
    /// the one thing that distinguishes BC36638's reach (S4: 107), and a Shared Operator (R6c: 11,22). Before #230 a Structure could declare no Shared member, so it had no type initializer.
    /// </summary>
    [Test]
    public void SharedMembers_AFieldAConstAPropertyASubNewAnOperatorAndALambdaOverAShared_Run()
        => AssertSingleFile(StructureMembersProbes.SharedMembers, StructureMembersProbes.SharedSubNew, StructureMembersProbes.SharedLambdaAndOperator);

    /// <summary>
    /// (7) Constructors, M5's killer. `Sub New(a, b)` with arguments (`3,9`); a plain `Dim d As P` and `New P()` are the ZEROING constructor (`0,0`, `0`), never the declared one (R7a). And the case that was
    /// risky, in its own program so that nothing else can mask it (R7c): `Structure Q` with only `Sub New(Optional a As Integer = 9)`. `New Q()` is still the zeroing one and prints 0 where a class would print
    /// 9, `New Q(4)` binds the declared one (4), and a plain `Dim u As Q` is 0. `Me.X = x` inside a constructor and a method returning `New Vec(X + o.X, ...)` (R7b). M5 (`New P()` binds a declared constructor):
    /// R7a is REFUSED (P declares no constructor that takes no arguments) and R7c prints 9 for `Dim z As New Q()`, on every backend and entry point.
    /// </summary>
    [Test]
    public void ADeclaredConstructor_NewWithNoArguments_IsStillTheZeroingOne()
        => AssertSingleFile(StructureMembersProbes.Constructors, StructureMembersProbes.OptionalConstructor, StructureMembersProbes.VecConstructor);

    /// <summary>
    /// (8) The pre-existing C++ defect the fix closes, with NO method anywhere: a store into a Structure field of an object, `o.S.V = 5`, wrote a COPY on C++ (it printed `0 7 8` for vbc's `6 7 8`; the
    /// array-element stores beside it were right). A fields-only Structure, so this is the row that moved without anything new being parsed. M3 (C++ reaches the field through a copy) brings the defect back.
    /// </summary>
    [Test]
    public void AStoreIntoAStructureField_OfAnObjectAndOfAnArrayElement_WritesTheStorage()
        => AssertSingleFile(StructureMembersProbes.FieldStore);

    /// <summary>
    /// (9) CONTROL: a fields-only Structure, unchanged. A copy is a copy, a ByVal Structure argument is not written back, and a List holds the VALUE it was given: `5n4 3 5`. Nothing here uses
    /// a member, so this is the old grammar and the old lowering (`Visit(StructureNode)` keeps its own path for it) and it must print the same on all three.
    /// </summary>
    [Test]
    public void AFieldsOnlyStructure_IsUnchanged()
        => AssertSingleFile(StructureMembersProbes.Control);

    /// <summary>
    /// (10) JavaScript still REFUSES a Structure, now one WITH methods: BL7005 ("a Structure cannot be lowered to JavaScript"), through the CLI (a non-zero exit, no stack trace), the CLI with `--optimize`
    /// and CompileProjectFiles (the generator's refusal). The front end accepts it: BL7005 is a capability decision of the JavaScript backend, which refuses the Structure declaration and every use of the type. Nothing runs under Node.
    /// </summary>
    [Test]
    public void JavaScript_StillRefusesAStructureWithMethods_BL7005()
    {
        var failures = new List<string>();
        foreach (var entry in Enum.GetValues<EntryPoint>())
        {
            var text = JavaScriptRefusalText(entry, StructureMembersProbes.Members.Source);
            if (!text.Contains("BL7005")) failures.Add($"{entry}: [{text.Split('\n')[0]}]");
            if (text.Contains("Unhandled exception")) failures.Add($"{entry}: a stack trace");
        }
        Assert.That(failures, Is.Empty, "JavaScript must refuse the Structure with BL7005:\n" + string.Join("\n", failures));
    }
}

/// <summary>
/// The #230 programs. Each is the implementer's `S/t230/probes` program(s), verbatim or merged with its neighbours (the test-writer's `S/t230/tw/probes`, each with its vbc `.exp`), and every expected value
/// is vbc's OWN output for it (see the fixture header). Every program is HangSafe (#256): its C# leg runs in a time-limited child process.
/// </summary>
internal static class StructureMembersProbes
{
    private const Bk Three = Bk.CSharp | Bk.Cpp | Bk.Msil;

    private static TempProbe P(string id, string source, string vb, Bk agrees = Three) => new(id, source, vb, agrees, HangSafe: true);

    /// <summary>r1 (p01 + p02 + p11 + p13): a Function over fields, a mutating Sub called twice, a bare self-call, `Me.X` written and read.</summary>
    internal static readonly TempProbe Members = P("r1_members", """
        Structure P
            Public X As Integer
            Public Y As Integer
            Public Function Sum() As Integer
                Return X + Y
            End Function
            Public Sub Bump()
                X = X + 1
            End Sub
            Public Sub BumpTwice()
                Bump()
                Bump()
            End Sub
            Public Sub SetTo(v As Integer)
                Me.X = v * 2
            End Sub
            Public Function Next1() As Integer
                Return Me.X + 1
            End Function
        End Structure
        Sub Main()
            Dim s As P
            s.X = 3
            s.Y = 4
            Console.WriteLine(s.Sum())
            s.Bump()
            s.Bump()
            Console.WriteLine(s.X)
            s.BumpTwice()
            Console.WriteLine(s.X)
            s.SetTo(8)
            Console.WriteLine(s.X)
            Console.WriteLine(s.Next1())
        End Sub
        """, "7\n5\n7\n16\n17");

    /// <summary>r2a (p03 + p15 + p24): a copy, a ByVal argument, a ByRef argument, `Dim c As P = Me`.</summary>
    internal static readonly TempProbe Copies = P("r2a_copy", """
        Structure P
            Public V As Integer
            Public Sub Bump()
                V = V + 1
            End Sub
            Public Function Snapshot() As P
                Dim c As P = Me
                V = V + 100
                Return c
            End Function
        End Structure
        Sub Change(x As P)
            x.Bump()
            Console.WriteLine(x.V)
        End Sub
        Sub ChangeRef(ByRef x As P)
            x.Bump()
        End Sub
        Sub Main()
            Dim s As P
            s.V = 5
            Dim t As P = s
            t.Bump()
            Console.WriteLine(s.V)
            Console.WriteLine(t.V)
            Change(s)
            Console.WriteLine(s.V)
            ChangeRef(s)
            Console.WriteLine(s.V)
            Dim u As P = s.Snapshot()
            Console.WriteLine(s.V & " " & u.V)
        End Sub
        """, "5\n6\n6\n5\n6\n106 6");

    /// <summary>r2b (p26 + p27): a List element and a `For Each` variable are copies.</summary>
    internal static readonly TempProbe ListAndForEachCopies = P("r2b_listcopy", """
        Structure P
            Public V As Integer
            Public Sub Bump()
                V = V + 1
            End Sub
            Public Function Peek() As Integer
                Return V
            End Function
        End Structure
        Sub Main()
            Dim lst As New List(Of P)
            Dim a As P
            a.V = 3
            lst.Add(a)
            lst(0).Bump()
            Console.WriteLine(lst(0).V & " " & lst(0).Peek())
            Dim b(1) As P
            b(0).V = 1
            b(1).V = 2
            Dim total As Integer = 0
            For Each q As P In b
                q.Bump()
                total = total + q.V
            Next
            Console.WriteLine(total & " " & b(0).V & " " & b(1).V)
        End Sub
        """, "3 3\n5 1 2");

    /// <summary>r3a (p04 + p05): a Sub on a Structure field of an object and on array elements mutates in place.</summary>
    internal static readonly TempProbe InPlace = P("r3a_inplace", """
        Structure P
            Public V As Integer
            Public Sub Bump()
                V = V + 1
            End Sub
        End Structure
        Class Holder
            Public S As P
        End Class
        Sub Main()
            Dim o As New Holder()
            o.S.V = 5
            o.S.Bump()
            Console.WriteLine(o.S.V)
            Dim a(2) As P
            a(0).V = 5
            a(0).Bump()
            a(1).Bump()
            Console.WriteLine(a(0).V)
            Console.WriteLine(a(1).V)
        End Sub
        """, "6\n6\n1");

    /// <summary>r3b (p21): a Structure inside a Structure, bumped from outside and from its container's own method, on a local and on an array element.</summary>
    internal static readonly TempProbe Nested = P("r3b_nested", """
        Structure Inner
            Public N As Integer
            Public Sub Bump()
                N = N + 1
            End Sub
        End Structure
        Structure Outer
            Public I As Inner
            Public Sub BumpInner()
                I.Bump()
            End Sub
        End Structure
        Sub Main()
            Dim o As Outer
            o.I.Bump()
            o.BumpInner()
            Console.WriteLine(o.I.N)
            Dim a(1) As Outer
            a(1).BumpInner()
            a(1).I.Bump()
            Console.WriteLine(a(1).I.N & " " & a(0).I.N)
        End Sub
        """, "2\n2 0");

    /// <summary>r4 (p06): a method on a Function's result runs on a temporary.</summary>
    internal static readonly TempProbe Result = P("r4_result", """
        Structure P
            Public V As Integer
            Public Function Bumped() As Integer
                V = V + 1
                Return V
            End Function
        End Structure
        Function Make() As P
            Dim r As P
            r.V = 40
            Return r
        End Function
        Sub Main()
            Console.WriteLine(Make().Bumped())
            Dim s As P = Make()
            Console.WriteLine(s.V)
            Console.WriteLine(s.Bumped())
            Console.WriteLine(Make().V)
        End Sub
        """, "41\n40\n41\n40");

    /// <summary>r5a (p07 + p25): a Get/Set property, a ReadOnly one, a Shared ReadOnly one returning a Structure.</summary>
    internal static readonly TempProbe GetSet = P("r5a_getset", """
        Structure P
            Private _v As Integer
            Public X As Integer
            Public Property V As Integer
                Get
                    Return _v * 10
                End Get
                Set(value As Integer)
                    _v = value + 1
                End Set
            End Property
            Public ReadOnly Property Twice As Integer
                Get
                    Return X * 2
                End Get
            End Property
            Public Shared ReadOnly Property Zero As P
                Get
                    Dim z As P
                    Return z
                End Get
            End Property
        End Structure
        Sub Main()
            Dim s As P
            s.V = 4
            Console.WriteLine(s.V)
            s.X = 21
            Console.WriteLine(s.Twice)
            Console.WriteLine(P.Zero.X)
        End Sub
        """, "50\n42\n0");

    /// <summary>r5b (p08): auto-properties, copied.</summary>
    internal static readonly TempProbe AutoProperty = P("r5b_autoprop", """
        Structure P
            Public Property Name As String
            Public Property N As Integer
        End Structure
        Sub Main()
            Dim s As P
            s.Name = "abc"
            s.N = 7
            Dim t As P = s
            t.N = 9
            Console.WriteLine(s.Name & " " & s.N)
            Console.WriteLine(t.N)
        End Sub
        """, "abc 7\n9");

    /// <summary>r5c (p29): a method that writes a property, called through a field, an array element and a Class property.</summary>
    internal static readonly TempProbe PropertyChain = P("r5c_propchain", """
        Structure P
            Private _v As Integer
            Public Property V As Integer
                Get
                    Return _v
                End Get
                Set(value As Integer)
                    _v = value * 2
                End Set
            End Property
            Public Sub Bump()
                V = V + 1
            End Sub
        End Structure
        Class Holder
            Public S As P
            Public Property Q As P
        End Class
        Sub Main()
            Dim o As New Holder()
            o.S.V = 4
            o.S.Bump()
            Dim arr(1) As P
            arr(1).V = 5
            arr(1).Bump()
            o.Q.Bump()
            Console.WriteLine(o.S.V & " " & arr(1).V & " " & o.Q.V)
        End Sub
        """, "18 22 0");

    /// <summary>r6a (p09 + p19 + p32): a Shared field with an initializer, a Const, a Shared Property with an initializer, a Shared Function.</summary>
    internal static readonly TempProbe SharedMembers = P("r6a_shared", """
        Structure P
            Public V As Integer
            Public Shared K As Integer = 30
            Public Const C As Integer = 12
            Public Shared Property L As Integer = 7
            Public Shared Function Twice(x As Integer) As Integer
                Return x * 2 + K
            End Function
            Public Function Total() As Integer
                Return V + K + C + L
            End Function
        End Structure
        Sub Main()
            Dim s As P
            s.V = 1
            Console.WriteLine(s.Total())
            Console.WriteLine(P.Twice(21))
            P.K = 100
            Console.WriteLine(P.Twice(21))
            Console.WriteLine(s.Total())
            Console.WriteLine(P.L)
        End Sub
        """, "50\n72\n142\n120\n7");

    /// <summary>r6b (p14): a `Shared Sub New` is the type initializer.</summary>
    internal static readonly TempProbe SharedSubNew = P("r6b_sharednew", """
        Structure P
            Public V As Integer
            Public Shared K As Integer
            Shared Sub New()
                K = 42
            End Sub
            Public Function Plus() As Integer
                Return V + K
            End Function
        End Structure
        Sub Main()
            Dim s As P
            s.V = 1
            Console.WriteLine(P.K)
            Console.WriteLine(s.Plus())
        End Sub
        """, "42\n43");

    /// <summary>r6c (s4 + p28): a lambda over a local and a Shared field is legal (107); a Shared Operator on a Structure.</summary>
    internal static readonly TempProbe SharedLambdaAndOperator = P("r6c_lambdashared", """
        Structure P
            Public V As Integer
            Public Shared K As Integer
            Public Function Getter() As Func(Of Integer)
                Dim local As Integer = V
                Return Function() local + K
            End Function
        End Structure
        Structure V2
            Public X As Integer
            Public Y As Integer
            Public Shared Operator +(a As V2, b As V2) As V2
                Dim r As V2
                r.X = a.X + b.X
                r.Y = a.Y + b.Y
                Return r
            End Operator
        End Structure
        Sub Main()
            P.K = 100
            Dim q As New P()
            q.V = 7
            Dim f As Func(Of Integer) = q.Getter()
            Console.WriteLine(f())
            Dim a As V2
            a.X = 1
            a.Y = 2
            Dim b As V2
            b.X = 10
            b.Y = 20
            Dim c As V2 = a + b
            Console.WriteLine(c.X & "," & c.Y)
        End Sub
        """, "107\n11,22");

    /// <summary>r7a (p10): `Sub New(a, b)`; `New P()` and a plain `Dim` are the zeroing constructor.</summary>
    internal static readonly TempProbe Constructors = P("r7a_ctor", """
        Structure P
            Public X As Integer
            Public Y As Integer
            Public Sub New(a As Integer, b As Integer)
                X = a
                Y = b
            End Sub
        End Structure
        Sub Main()
            Dim s As New P(3, 9)
            Console.WriteLine(s.X & "," & s.Y)
            Dim d As P
            Console.WriteLine(d.X & "," & d.Y)
            Dim e As New P()
            Console.WriteLine(e.X)
        End Sub
        """, "3,9\n0,0\n0");

    /// <summary>r7c (p18): an all-Optional `Sub New` is not chosen for `New Q()`.</summary>
    internal static readonly TempProbe OptionalConstructor = P("r7c_optctor", """
        Structure Q
            Public V As Integer
            Public Sub New(Optional a As Integer = 9)
                V = a
            End Sub
        End Structure
        Sub Main()
            Dim z As New Q()
            Console.WriteLine(z.V)
            Dim t As New Q(4)
            Console.WriteLine(t.V)
            Dim u As Q
            Console.WriteLine(u.V)
        End Sub
        """, "0\n4\n0");

    /// <summary>r7b (p22): `Me.X = x` in a constructor, a method taking and returning a Structure.</summary>
    internal static readonly TempProbe VecConstructor = P("r7b_vec", """
        Structure Vec
            Public X As Double
            Public Y As Double
            Public Sub New(x As Double, y As Double)
                Me.X = x
                Me.Y = y
            End Sub
            Public Function Add(o As Vec) As Vec
                Return New Vec(X + o.X, Y + o.Y)
            End Function
            Public Function Len2() As Double
                Return X * X + Y * Y
            End Function
        End Structure
        Sub Main()
            Dim a As New Vec(1, 2)
            Dim b As New Vec(3, 4)
            Dim c As Vec = a.Add(b)
            Console.WriteLine(c.X & "," & c.Y & " " & c.Len2())
        End Sub
        """, "4,6 52");

    /// <summary>c03: a store into a Structure field of an object and into array elements. A fields-only Structure.</summary>
    internal static readonly TempProbe FieldStore = P("r8_fieldstore", """
        Structure P
            Public V As Integer
        End Structure
        Class Holder
            Public S As P
        End Class
        Sub Main()
            Dim o As New Holder()
            o.S.V = 5
            o.S.V = o.S.V + 1
            Dim a(2) As P
            a(0).V = 7
            a(1).V = a(0).V + 1
            Console.WriteLine(o.S.V & " " & a(0).V & " " & a(1).V)
        End Sub
        """, "6 7 8");

    /// <summary>c01: the control, a fields-only Structure.</summary>
    internal static readonly TempProbe Control = P("r9_control", """
        Structure P
            Public X As Integer
            Public Name As String
        End Structure
        Sub Change(p As P)
            p.X = 99
        End Sub
        Sub Main()
            Dim s As P
            s.X = 3
            s.Name = "n"
            Dim t As P = s
            t.X = 4
            Change(s)
            Dim lst As New List(Of P)
            lst.Add(s)
            s.X = 5
            lst.Add(s)
            Console.WriteLine(s.X & s.Name & t.X & " " & lst(0).X & " " & lst(1).X)
        End Sub
        """, "5n4 3 5");
}
