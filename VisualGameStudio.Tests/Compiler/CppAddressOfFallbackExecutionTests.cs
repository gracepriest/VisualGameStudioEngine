using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler.CodeGen.CPlusPlus;

namespace VisualGameStudio.Tests.Compiler;

// =====================================================================================
//  #201 — `AddressOf` on the C++ by-copy FALLBACK path (ADR-0019) RUNS, with VB's answer.
//
//  A root ClosureLowering refuses and W2 admits (here: a read-only Select Case `When` guard over a capture,
//  or an Iterator) is emitted by the by-copy `[=]` path, where an `AddressOf` reaches the backend as written
//  (an IRUnaryOp, never an IRDelegateCreate). Two shapes of it failed clang there while running on C#,
//  JavaScript and on the lowered C++ path: `AddressOf obj.M` / `Me.M` / a bare method of the class / a Shared
//  method rendered as the member READ `obj->M` ("undeclared identifier", "reference to non-static member
//  function must be called", "'MathOps' does not refer to a value"), and every AddressOf temp was declared at
//  its assignment, which a goto from before it to a label after it may not cross ("cannot jump from this goto
//  statement to its label"; a Return or a Select arm makes exactly that goto).
//
//  Every row is a program whose root(s) the fixture ASSERTS took the by-copy path in every in-process entry point
//  (CppClosurePath.ByCopy, as CppClosurePathTests does) — so the rows keep pinning the fallback, not the lowered
//  path — and then RUNS it through the real CLI, the CLI with --optimize, and CompileProjectFiles (the entry
//  point a .blproj and the IDE build use), expecting what vbc prints. The expected output is vbc's, from the
//  implementer's measured .exp files (the `When` guard is written as If/Else for vbc, which has no `When`).
//  E8 and E9e are the first two probes and live in UserDelegateConversionExecutionTests (their pins moved).
//
//  Mutants (each reverts ONE part of the fix; each is killed by the rows named here):
//    M1  the AddressOf temp is declared at its assignment again, not with the other temps ........ P6/P7, P8, P12
//    M2  the member read IRBuilder emits for `obj.M` is emitted again (`obj->M`) ................. P3/P9, P4, P5, P10, P11
//    M3  the receiver is captured BY REFERENCE, not copied (a reassigned receiver moves the delegate) ... P10
//
//  KNOWN GAPS — listed, deliberately not tested here:
//    * JavaScript: a bare `AddressOf SharedMethod` INSIDE its own class emits `Shout`, not `Greeter.Shout`
//      (a ReferenceError at run time; task #285). It is not specific to the fallback and is not C++.
//    * MSIL refuses these programs with a stated reason (the `When` guard; the Iterator) — ADR-0019's D9 list.
//
//  ⚠ Named "…ExecutionTests" but it runs no JavaScript: it is in JsExecutionTierRosterTests.NotJavaScriptExecution,
//  NOT the roster, and its case count is not in the roster's pin.
// =====================================================================================

[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class CppAddressOfFallbackExecutionTests
{
    private static readonly CppEntry[] AllEntries =
        { CppEntry.Plain, CppEntry.Standard, CppEntry.Aggressive, CppEntry.Project, CppEntry.Split };

    // P3 + P9: `AddressOf Me.M` and the bare spellings of an instance and a Shared method of the enclosing class.
    private const string MeAndBareOwnMethods = """
        Delegate Sub Notify(msg As String)

        Class Greeter
            Public Prefix As String
            Public Sub Greet(m As String)
                Console.WriteLine(Prefix & m)
            End Sub
            Public Shared Sub Shout(m As String)
                Console.WriteLine("! " & m)
            End Sub
            Public Function Self() As Notify
                Dim lim As Integer = 5
                Dim probe As Func(Of Integer) = Function() lim + 1
                Dim v As Integer = probe()
                Select Case v
                    Case Is > 0 When v > lim + 1
                        Console.WriteLine("big")
                    Case Else
                        Console.WriteLine("small")
                End Select
                Return AddressOf Me.Greet
            End Function
            Public Function Pick(loud As Boolean) As Notify
                Dim lim As Integer = 5
                Dim probe As Func(Of Integer) = Function() lim + 1
                Dim v As Integer = probe()
                Select Case v
                    Case Is > 0 When v > lim + 1
                        Console.WriteLine("big")
                    Case Else
                        Console.WriteLine("small")
                End Select
                If loud Then Return AddressOf Shout
                Return AddressOf Greet
            End Function
        End Class

        Sub Main()
            Dim g As New Greeter()
            g.Prefix = "hi "
            Dim s As Notify = g.Self()
            s("me")
            g.Pick(False)("a")
            g.Pick(True)("b")
        End Sub
        """;

    // P4: `AddressOf obj.Function`; the delegate sees a later write to the receiver's field (it shares the object).
    private const string ObjectFunction = """
        Delegate Function Transform(n As Integer) As Integer

        Class Scaler
            Public K As Integer
            Public Function Scale(n As Integer) As Integer
                Return n * K
            End Function
        End Class

        Sub Main()
            Dim lim As Integer = 5
            Dim probe As Func(Of Integer) = Function() lim + 1
            Dim v As Integer = probe()
            Select Case v
                Case Is > 0 When v > lim + 1
                    Console.WriteLine("big")
                Case Else
                    Console.WriteLine("small")
            End Select
            Dim sc As New Scaler()
            sc.K = 10
            Dim t As Transform = AddressOf sc.Scale
            Console.WriteLine(t(4))
            sc.K = 3
            Console.WriteLine(t(4))
        End Sub
        """;

    // P5: `AddressOf Class.SharedMethod` — a class name is not a value.
    private const string SharedMethod = """
        Delegate Function Transform(n As Integer) As Integer

        Class MathOps
            Public Shared Function Twice(n As Integer) As Integer
                Return n * 2
            End Function
        End Class

        Sub Main()
            Dim lim As Integer = 5
            Dim probe As Func(Of Integer) = Function() lim + 1
            Dim v As Integer = probe()
            Select Case v
                Case Is > 0 When v > lim + 1
                    Console.WriteLine("big")
                Case Else
                    Console.WriteLine("small")
            End Select
            Dim t As Transform = AddressOf MathOps.Twice
            Console.WriteLine(t(21))
        End Sub
        """;

    // P6 + P7: Return AddressOf on BOTH arms (E9e's goto shape, twice), and an If/Else assigning AddressOf on one arm
    // and a lambda on the other.
    private const string ReturnBothArmsAndIfElseAssign = """
        Delegate Function Transform(n As Integer) As Integer

        Function Inc(n As Integer) As Integer
            Return n + 1
        End Function

        Function Dec(n As Integer) As Integer
            Return n - 1
        End Function

        Function PickBoth(up As Boolean) As Transform
            Dim lim As Integer = 5
            Dim probe As Func(Of Integer) = Function() lim + 1
            Dim v As Integer = probe()
            Select Case v
                Case Is > 0 When v > lim + 1
                    Console.WriteLine("big")
                Case Else
                    Console.WriteLine("small")
            End Select
            If up Then Return AddressOf Inc
            Return AddressOf Dec
        End Function

        Function PickAssign(up As Boolean) As Transform
            Dim lim As Integer = 5
            Dim probe As Func(Of Integer) = Function() lim + 1
            Dim v As Integer = probe()
            Select Case v
                Case Is > 0 When v > lim + 1
                    Console.WriteLine("big")
                Case Else
                    Console.WriteLine("small")
            End Select
            Dim t As Transform
            If up Then
                t = AddressOf Inc
            Else
                t = Function(n) n - 1
            End If
            Return t
        End Function

        Sub Main()
            Console.WriteLine(PickBoth(True)(10) * 100 + PickBoth(False)(10))
            Console.WriteLine(PickAssign(True)(10) * 100 + PickAssign(False)(10))
        End Sub
        """;

    // P8: an AddressOf Returned from a Select arm.
    private const string SelectArm = """
        Delegate Function Transform(n As Integer) As Integer

        Function Pick(k As Integer) As Transform
            Dim lim As Integer = 5
            Dim probe As Func(Of Integer) = Function() lim + 1
            Dim v As Integer = probe()
            Select Case v
                Case Is > 0 When v > lim + 1
                    Console.WriteLine("big")
                Case Else
                    Console.WriteLine("small")
            End Select
            Select Case k
                Case 1
                    Return AddressOf Inc
                Case 2
                    Return Function(n) n * 3
            End Select
            Return Function(n) n - 1
        End Function

        Function Inc(n As Integer) As Integer
            Return n + 1
        End Function

        Sub Main()
            Console.WriteLine(Pick(1)(10) & " " & Pick(2)(10) & " " & Pick(3)(10))
        End Sub
        """;

    // P10: the receiver is reassigned AFTER the AddressOf. A delegate binds the object it was made over, so it still
    // multiplies by the OLD receiver's K (10 -> 40); the variable now names a new Scaler (K = 2 -> 8).
    private const string ReceiverReassigned = """
        Delegate Function Transform(n As Integer) As Integer

        Class Scaler
            Public K As Integer
            Public Function Scale(n As Integer) As Integer
                Return n * K
            End Function
        End Class

        Sub Main()
            Dim lim As Integer = 5
            Dim probe As Func(Of Integer) = Function() lim + 1
            Dim v As Integer = probe()
            Select Case v
                Case Is > 0 When v > lim + 1
                    Console.WriteLine("big")
                Case Else
                    Console.WriteLine("small")
            End Select
            Dim sc As New Scaler()
            sc.K = 10
            Dim t As Transform = AddressOf sc.Scale
            sc = New Scaler()
            sc.K = 2
            Console.WriteLine(t(4))
            Console.WriteLine(sc.Scale(4))
        End Sub
        """;

    // P11: a virtual method — the delegate dispatches on the receiver's runtime class.
    private const string VirtualMethod = """
        Delegate Function Describe() As String

        Class Animal
            Public Overridable Function Name() As String
                Return "animal"
            End Function
        End Class

        Class Dog
            Inherits Animal
            Public Overrides Function Name() As String
                Return "dog"
            End Function
        End Class

        Sub Main()
            Dim lim As Integer = 5
            Dim probe As Func(Of Integer) = Function() lim + 1
            Dim v As Integer = probe()
            Select Case v
                Case Is > 0 When v > lim + 1
                    Console.WriteLine("big")
                Case Else
                    Console.WriteLine("small")
            End Select
            Dim a As Animal = New Dog()
            Dim d As Describe = AddressOf a.Name
            Console.WriteLine(d())
        End Sub
        """;

    // P12: AddressOf as an ARGUMENT, inside an If arm (a module procedure on one, a bound method on the other).
    private const string ArgumentInAnIfArm = """
        Delegate Function Transform(n As Integer) As Integer

        Class Scaler
            Public K As Integer
            Public Function Scale(n As Integer) As Integer
                Return n * K
            End Function
        End Class

        Function Apply(f As Transform, x As Integer) As Integer
            Return f(x)
        End Function

        Function Inc(n As Integer) As Integer
            Return n + 1
        End Function

        Sub Run(up As Boolean)
            Dim lim As Integer = 5
            Dim probe As Func(Of Integer) = Function() lim + 1
            Dim v As Integer = probe()
            Select Case v
                Case Is > 0 When v > lim + 1
                    Console.WriteLine("big")
                Case Else
                    Console.WriteLine("small")
            End Select
            Dim sc As New Scaler()
            sc.K = 7
            If up Then
                Console.WriteLine(Apply(AddressOf Inc, 3))
            Else
                Console.WriteLine(Apply(AddressOf sc.Scale, 3))
            End If
        End Sub

        Sub Main()
            Run(True)
            Run(False)
        End Sub
        """;

    // P13: an Iterator root (a by-copy root with no `When` guard): AddressOf of a parameter's method inside a coroutine.
    private const string IteratorRoot = """
        Delegate Function Transform(n As Integer) As Integer

        Class Scaler
            Public K As Integer
            Public Function Scale(n As Integer) As Integer
                Return n * K
            End Function
        End Class

        Iterator Function Gen(sc As Scaler) As IEnumerable(Of Integer)
            Dim k As Integer = 1
            Dim f = Function() k + 1
            Dim t As Transform = AddressOf sc.Scale
            Yield f()
            Yield t(5)
        End Function

        Sub Main()
            Dim sc As New Scaler()
            sc.K = 3
            For Each x As Integer In Gen(sc)
                Console.WriteLine(x)
            Next
        End Sub
        """;

    /// <summary>(program, vbc's output, every root that creates a lambda — all of which must take the by-copy path).</summary>
    private static IEnumerable<TestCaseData> Rows()
    {
        yield return new TestCaseData(MeAndBareOwnMethods, "small\nhi me\nsmall\nhi a\nsmall\n! b",
            new[] { "Greeter.Self", "Greeter.Pick" }).SetName("P3_P9_M2_MeDotM_BareInstanceAndSharedMethod");
        yield return new TestCaseData(ObjectFunction, "small\n40\n12",
            new[] { "Main" }).SetName("P4_M2_ObjectFunction_SeesALaterFieldWrite");
        yield return new TestCaseData(SharedMethod, "small\n42",
            new[] { "Main" }).SetName("P5_M2_SharedMethod");
        yield return new TestCaseData(ReturnBothArmsAndIfElseAssign, "small\nsmall\n1109\nsmall\nsmall\n1109",
            new[] { "PickBoth", "PickAssign" }).SetName("P6_P7_M1_ReturnOnBothArms_IfElseAddressOfVsLambda");
        yield return new TestCaseData(SelectArm, "small\nsmall\nsmall\n11 30 9",
            new[] { "Pick" }).SetName("P8_M1_ReturnAddressOfFromASelectArm");
        yield return new TestCaseData(ReceiverReassigned, "small\n40\n8",
            new[] { "Main" }).SetName("P10_M2_M3_ReceiverReassignedAfterAddressOf");
        yield return new TestCaseData(VirtualMethod, "small\ndog",
            new[] { "Main" }).SetName("P11_M2_VirtualMethod");
        yield return new TestCaseData(ArgumentInAnIfArm, "small\n4\nsmall\n21",
            new[] { "Run" }).SetName("P12_M1_AddressOfAsAnArgumentInAnIfArm");
        yield return new TestCaseData(IteratorRoot, "2\n15",
            new[] { "Gen" }).SetName("P13_IteratorRoot");
    }

    /// <summary>
    /// ⭐ Each program prints vbc's answer through the real CLI, the CLI with <c>--optimize</c> and
    /// <c>CompileProjectFiles</c> — and its roots are, in every in-process entry point, exactly the listed ones and all
    /// on the by-copy path, so this is the FALLBACK's emission being run and not the lowered path's.
    /// </summary>
    [TestCaseSource(nameof(Rows))]
    public void AddressOfOnTheByCopyFallback_RunsWithVbsAnswer(string source, string expected, string[] roots)
    {
        Assert.Multiple(() =>
        {
            foreach (var entry in AllEntries)
            {
                var build = CppClosures.Compile(source, entry);
                Assert.That(build.Paths.Select(p => p.Root), Is.EquivalentTo(roots),
                    $"{entry}: the roots that create a lambda: {build.Describe()}");
                foreach (var root in roots)
                    Assert.That(build.PathOf(root), Is.EqualTo(CppClosurePath.ByCopy),
                        $"{entry}: root '{root}' must take the by-copy fallback — otherwise this row pins the lowered path, not #201");
            }
        });

        // (not inside Assert.Multiple: a missing C++ compiler is an Assert.Ignore, which fails the test there)
        Assert.That(CppClosures.RunViaCli(source, optimize: false), Is.EqualTo(expected), "CLI");
        Assert.That(CppClosures.RunViaCli(source, optimize: true), Is.EqualTo(expected), "CLI --optimize");
        Assert.That(CppClosures.Run(source, CppEntry.Project), Is.EqualTo(expected), "CompileProjectFiles");
    }
}
