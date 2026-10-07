using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #151, C++ half, RUN. A user class that `Inherits Exception` (or another built-in exception) used to fail to compile on C++ (`unknown type name 'Exception'`): the runtime had no exception base class
//  and the generator treated `Exception` as a `#CppInclude`d foreign base. The owner ruled option (b), a runtime exception base: `CppExceptionRuntime` defines `BasicLang::Exception`, an ADR-0015 hierarchy
//  root with a `Message` data member, an overridable `ToString()` and a virtual `blExceptionChain_()` (the object's type chain, most-derived first, `LeafErr;BaseErr;System.Exception`); .NET's default message
//  (`Exception of type 'X' was thrown.`) comes from its parameterless `ctor_()`. A `Throw` of such an object is `BasicLang::ThrowObject`, a `ThrownException` that DERIVES from `NetException` and carries the
//  object and its DYNAMIC chain, so it enters the existing typed-Catch ladder and is decided by TYPE, never by clause position; a user-typed clause binds the SAME object back (`BasicLang::Caught<T>`, a
//  `dynamic_pointer_cast`), so the class's own fields and properties read back. `Catch ex As Exception` still binds the NetException and reads Message through `what()`.
//  `CppCapabilityChecker` refuses, by name and instead of a clang error, what the runtime base cannot do: `CppUserExceptionRefusalTests` (fast, below).
//
//  ⭐ THE ORACLE IS vbc. Each row's `Vb` is what the SDK's vbc prints for the program wrapped in a VB Module (S/t151c/probes and xprobes, the `.exp` beside each `.bas`; the test-writer re-ran every
//  one through vbc again before writing these, `S/t151c/tw/vbc-rerun.txt`, and every answer matched its `.exp`). The expected text is vbc's, never a backend's.
//
//  ENTRY POINTS: every row goes through the spawned CLI, the CLI with `--optimize` and `BasicCompiler.CompileProjectFiles` (aggressive), on C++ and, as a REFERENCE that the expected text is not a C++ artefact,
//  on C#: `TempExec.AssertMatchesInEveryEntryPoint`. ⛔ Every row is `HangSafe`: its C# leg runs in a child process with a time limit (`CSharpProcessRunner`, #256), never the in-process runner. A backend whose
//  tool is missing (a C++ compiler) SKIPS its cells: never a failure. The test is ignored only when no cell could run.
//  ⚠ ONE row is C++ only: X2 (the default message) names the thrown type, and the C# backend lands in `namespace GeneratedCode`, so it prints `Exception of type 'GeneratedCode.SubErr' was thrown.` where vbc prints
//  `SubErr` (measured); its C# leg would say nothing about C++. Every other row runs both.
//  ⚠ Named "...ExecutionTests" but it runs NO Node: JavaScript and MSIL already answered these programs (`UserExceptionSubclassExecutionTests`, `JavaScriptCatchDiscriminationTests`). So it is listed under
//  `JsExecutionTierRosterTests.NotJavaScriptExecution`, not in the roster, and the roster's count is unchanged.
//
//  MUTANTS (each built for real from a plain source copy of the fix with ONE change, and run against THESE tests through a copy of the test output with BasicLang.dll swapped; the cases that go red, measured):
//    * M1 the Catch ladder's user arm matches ANY thrown exception (`Matches("System.Exception")` where it names the class)                  -> 2 of 14: `ACatchLadder_PicksTheClauseByType_...` (V6 and N2: the first user clause takes every
//                                                                      object, its `Caught<T>` is null and the program dies with exit 139) and `AnUnmatchedUserException_...` (N4: the `AErr` enters the `BErr` clause, the same crash)
//    * M2 no per-class chain (`blExceptionChain_` is the base's: every object throws as plain System.Exception, so no user arm ever matches)  -> 7 of 14: every row that catches a user class by name: E05, V1 / V10, V3, V6 / N2,
//                                                                      V8, N3b and X2 (the first throw escapes: exit 134, std::terminate). V9 (nothing is thrown), N4 (it reaches the outer `Catch ex As Exception`
//                                                                      either way) and the N6 control are unaffected
//    * M3 no std::string typing for `x.Message` off a user exception (it keeps the analyzer's Object under an opaque .NET base)             -> 1 of 14: `AClassOverABuiltInException_...` ONLY (N3b: the C++ does not compile)
//    * M4 no .NET default message (the parameterless `ctor_()` leaves Message empty)                                                         -> 1 of 14: `TheDefaultMessage_...` ONLY (X2 prints an empty line)
//    * M5 no member refusal (the checker's `UnprovidedExceptionMembers` test inverted: nothing is "unprovided")                              -> 1 of 14: `AnExceptionMemberTheRuntimeBaseLacks_...` ONLY (nothing is refused, in any entry point or the CLI;
//                                                                      the program would go on to clang)
//    * M6 / M7 / M8 each of the other three refusals removed (generic class / `MyBase.New` with more than a message / `Overrides Message`)  -> 1 of 4 refusal cases each, the one that names it
//    The control (N6) and V9 are killed by none of these on purpose: they pin that the built-in path and a method-local `message` are unchanged.
//
//  ⛔ KNOWN GAPS — listed, deliberately NOT tested (asserting one would pin a defect). Each is outside what #151's C++ half fixes, and is the same before and after:
//    * N7  A `Throw` out of a Catch body SKIPS the enclosing Try's Finally on C++ (`Catch e As MyErr : Throw New Other(...)` under a `Finally`): the Finally text never runs. PRE-EXISTING, and the same for a
//          BUILT-IN exception; filed separately.
//    * V5  `Me.Message.ToUpper()` does not compile on C++: `String.ToUpper` has no C++ lowering (`Message` is a plain `std::string`). Pre-existing and not about exceptions (JavaScript has the same TypeError).
//    * N3  `Inherits ArgumentException` WITHOUT `Using System` is "Unknown base class 'ArgumentException'" in the front end, on every backend, before any backend is reached (N3b below, with the `Using`, runs).
//    * X3 / X4  A DECLARATION typed `Exception` (`Dim ex As Exception = New MyErr(...)`, `Sub Log(e As Exception)`) still maps to an undeclared `std::shared_ptr<Exception>` and is a clang error on C++, in every
//          program, with or without a user exception class. It needs a design ruling (is `Exception` a value, a `NetException`, or the new runtime object?); filed separately.
// ================================================================================================

/// <summary>#151 (C++ half) — a user class that inherits <c>Exception</c> builds and runs on C++, as vbc runs it.</summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the g++ / clang++ and C# child runs share the machine with the spawned CLI
public class CppUserExceptionExecutionTests
{
    /// <summary>
    /// One group of single-file probes, each on C++ and C#, through every entry point. A failing cell is collected, not thrown, so every other one still reports and the failure text names the probe, the backend
    /// and the entry point. (A copy of the helper `CppAutoPropertyBareStoreExecutionTests` keeps: each fixture owns its own.)
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
    /// (1) E05: a user class that `Inherits Exception` is thrown and caught BY ITS OWN NAME (`Catch e As MyErr`, `e.Message` is what MyBase.New was given), and a second one is thrown and caught as the
    /// BASE (`Catch e As Exception`, whose Message reads through the C++ exception). Before the change neither program compiled. M2 (no per-class chain: every object throws as plain System.Exception) lets the
    /// `MyErr` clause match nothing, so the first throw escapes.
    /// </summary>
    [Test]
    public void AUserException_IsCaughtByItsOwnName_AndByException()
        => AssertSingleFile(CppUserExceptionProbes.E05);

    /// <summary>
    /// (2) V1 / V10: the Catch binds the SAME object, so a user PROPERTY (`Code`, over a private field, read through a Get) and a user public FIELD read back beside the inherited `Message`: `bad 42`, `f 7`.
    /// A catch variable that held only the C++ exception text could not say 42 or 7.
    /// </summary>
    [Test]
    public void AUserExceptionsOwnPropertyAndField_ReadBackInTheCatch()
        => AssertSingleFile(CppUserExceptionProbes.V1, CppUserExceptionProbes.V10);

    /// <summary>
    /// (3) V3: a bare `Throw` inside `Catch e As MyErr` rethrows the SAME user object, and the OUTER `Catch e As MyErr` still sees it as one (`inner deep`, `outer deep`). The rethrow carries the object and its
    /// chain, not a rebuilt `NetException`.
    /// </summary>
    [Test]
    public void ARethrow_ReachesTheOuterCatch_AsTheSameUserObject()
        => AssertSingleFile(CppUserExceptionProbes.V3);

    /// <summary>
    /// (4) V6 and N2, ⭐ the M1 killer: which clause runs is decided by the thrown object's TYPE, not by where the clause stands. V6 throws an `AErr` then a `BErr` through two sibling clauses (`A a`, `B b`);
    /// N2 throws a `MidErr` and a `BaseErr` into `Catch e As BaseErr` listed AFTER a sibling `Catch e As OtherErr`, and an `OtherErr` into the first (`base mid:one`, `base two`, `other three`). M1 (the user arm
    /// matches ANY exception) sends every object into the first user clause, whose `dynamic_pointer_cast` yields a null object: V6 and N2 die with a segmentation fault (exit -11) where vbc prints the lines.
    /// </summary>
    [Test]
    public void ACatchLadder_PicksTheClauseByType_NotByPosition_SiblingsAndABaseTwoLevelsUp()
        => AssertSingleFile(CppUserExceptionProbes.V6, CppUserExceptionProbes.N2);

    /// <summary>
    /// (5) V8: a leaf over a base over `Exception`. The base's catch variable holds the LEAF object (`Message` is the leaf's `leaf:z`), `CType(e, LeafErr)` casts it back down to call the leaf's own method, and a bare
    /// `Message` inside that method reads the inherited one (`D leaf:z`): the object keeps its dynamic type through the carrier.
    /// </summary>
    [Test]
    public void ATwoLevelChain_ReadsThroughTheBasesCatch_AndCastsBackDown()
        => AssertSingleFile(CppUserExceptionProbes.V8);

    /// <summary>
    /// (6) V9: a method-local named `message` (`Dim message As String = "local"`) is the local, and the inherited `Message` read from outside is still `m`: the base's `Message` member does not leak into a method that
    /// declares a variable of that name (VB is case-insensitive; the C++ member is a data member of the base).
    /// </summary>
    [Test]
    public void AMethodLocalNamedMessage_DoesNotHideTheInheritedOne()
        => AssertSingleFile(CppUserExceptionProbes.V9);

    /// <summary>
    /// (7) N3b, ⭐ the M3 killer: `Inherits ArgumentException` (under `Using System`) is an OPAQUE .NET base to the analyzer, so `e.Message` through the user class types as Object; the generator must still spell it
    /// as the `std::string` the runtime base holds, or `"ArgumentException " & e.Message` does not compile. The same program catches a `BadArg` as `ArgumentException` after a non-matching
    /// `InvalidOperationException` clause, then by its own name (`ArgumentException arg`, `BadArg arg2`): the chain carries the built-in's chain (`BadArg;System.ArgumentException;...`). M3 (no std::string typing for
    /// a user exception's `Message`) is a clang error on this program.
    /// </summary>
    [Test]
    public void AClassOverABuiltInException_IsCaughtAsTheBuiltIn_AndByItsOwnName()
        => AssertSingleFile(CppUserExceptionProbes.N3b);

    /// <summary>
    /// (8) N4: a user exception that no inner clause matches (`Catch e As BErr` for a thrown `AErr`) is not swallowed: the inner `Finally` runs (`inner fin`) and an OUTER `Catch ex As Exception` receives it
    /// with its message (`outer escapes`). An arm that matched by position would take the `AErr` into the `BErr` clause.
    /// </summary>
    [Test]
    public void AnUnmatchedUserException_RunsTheFinally_AndReachesTheOuterExceptionCatch()
        => AssertSingleFile(CppUserExceptionProbes.N4);

    /// <summary>
    /// (9) X2, ⭐ the M4 killer: a class that passes NO message, two levels down (`SubErr` over `QuietErr` over `Exception`), reads .NET's default message,
    /// `Exception of type 'SubErr' was thrown.`, naming the DYNAMIC type, through the catching base's variable. M4 (the parameterless base constructor leaves Message empty) prints an empty line.
    /// C++ only: the C# backend lands in `namespace GeneratedCode` and prints `GeneratedCode.SubErr`, so its leg is no reference for this row.
    /// </summary>
    [Test]
    public void TheDefaultMessage_IsDotNets_AndNamesTheThrownType()
        => AssertSingleFile(CppUserExceptionProbes.X2);

    /// <summary>
    /// (10) CONTROL, N6: a BUILT-IN `InvalidOperationException` thrown and caught by name takes no user class and no `ThrowObject`, and prints `IOE x`: byte-identical C++ before and after (the byte compare of
    /// 11,270 cells saw no change in any program without an exception class).
    /// </summary>
    [Test]
    public void ABuiltInException_StillRunsUnchanged_Control()
        => AssertSingleFile(CppUserExceptionProbes.N6);
}

/// <summary>
/// The #151 probes. Sources are the implementer's `S/t151c/probes` and `xprobes` verbatim, and every expected value is vbc's OWN output for it (see the fixture header).
/// </summary>
internal static class CppUserExceptionProbes
{
    /// <summary>Every row is HangSafe (#256: its C# leg runs in a time-limited child process) and runs on C++ with C# as the reference.</summary>
    private static TempProbe P(string id, string source, string vb, Bk agrees = Bk.CSharp | Bk.Cpp) => new(id, source, vb, agrees, HangSafe: true);

    /// <summary>A user class `Inherits Exception`, thrown and caught by its own name, then another thrown and caught as `Exception`: `e.Message` reads the message MyBase.New was given in both.</summary>
    internal static readonly TempProbe E05 = P("E05_catch_user_and_base", """
        Class MyErr
            Inherits Exception
            Public Sub New(msg As String)
                MyBase.New(msg)
            End Sub
        End Class
        Sub Main()
            Try
                Throw New MyErr("x")
            Catch e As MyErr
                Console.WriteLine("MyErr " & e.Message)
            End Try
            Try
                Throw New MyErr("y")
            Catch e As Exception
                Console.WriteLine("Exception " & e.Message)
            End Try
        End Sub
        """, "MyErr x\nException y");

    /// <summary>A user PROPERTY (`Code`) read off the caught object beside the inherited `Message`: the Catch binds the SAME object, so the class's own members are there.</summary>
    internal static readonly TempProbe V1 = P("V1_property_beside_message", """
        Class CodeErr
            Inherits Exception
            Private _code As Integer
            Public Sub New(msg As String, code As Integer)
                MyBase.New(msg)
                _code = code
            End Sub
            Public ReadOnly Property Code As Integer
                Get
                    Return _code
                End Get
            End Property
        End Class
        Sub Main()
            Try
                Throw New CodeErr("bad", 42)
            Catch e As CodeErr
                Console.WriteLine(e.Message & " " & e.Code)
            End Try
        End Sub
        """, "bad 42");

    /// <summary>A user public FIELD (`Code`) read off the caught object beside `Message`.</summary>
    internal static readonly TempProbe V10 = P("V10_declared_field", """
        Class FieldErr
            Inherits Exception
            Public Code As Integer
            Public Sub New(msg As String, c As Integer)
                MyBase.New(msg)
                Code = c
            End Sub
        End Class
        Sub Main()
            Try
                Throw New FieldErr("f", 7)
            Catch e As FieldErr
                Console.WriteLine(e.Message & " " & e.Code)
            End Try
        End Sub
        """, "f 7");

    /// <summary>A bare `Throw` in a Catch rethrows the SAME user object: the outer Catch of the same class sees it.</summary>
    internal static readonly TempProbe V3 = P("V3_rethrow", """
        Class MyErr
            Inherits Exception
            Public Sub New(msg As String)
                MyBase.New(msg)
            End Sub
        End Class
        Sub Inner()
            Try
                Throw New MyErr("deep")
            Catch e As MyErr
                Console.WriteLine("inner " & e.Message)
                Throw
            End Try
        End Sub
        Sub Main()
            Try
                Inner()
            Catch e As MyErr
                Console.WriteLine("outer " & e.Message)
            End Try
        End Sub
        """, "inner deep\nouter deep");

    /// <summary>Two sibling exception classes thrown in turn from one Sub and told apart by their Catch clauses (a type switch).</summary>
    internal static readonly TempProbe V6 = P("V6_two_siblings", """
        Class AErr
            Inherits Exception
            Public Sub New(msg As String)
                MyBase.New(msg)
            End Sub
        End Class
        Class BErr
            Inherits Exception
            Public Sub New(msg As String)
                MyBase.New(msg)
            End Sub
        End Class
        Sub Raise(k As Integer)
            If k = 1 Then
                Throw New AErr("a")
            Else
                Throw New BErr("b")
            End If
        End Sub
        Sub Main()
            For k As Integer = 1 To 2
                Try
                    Raise(k)
                Catch e As AErr
                    Console.WriteLine("A " & e.Message)
                Catch e As BErr
                    Console.WriteLine("B " & e.Message)
                End Try
            Next
        End Sub
        """, "A a\nB b");

    /// <summary>A TWO-level hierarchy: a `MidErr` and a `BaseErr` caught by `Catch e As BaseErr`, a sibling `OtherErr` caught by its own clause listed BEFORE it. Decided by TYPE, not by clause position.</summary>
    internal static readonly TempProbe N2 = P("N2_hierarchy_caught_by_its_base", """
        Class BaseErr
            Inherits Exception
            Public Sub New(msg As String)
                MyBase.New(msg)
            End Sub
        End Class
        Class MidErr
            Inherits BaseErr
            Public Sub New(msg As String)
                MyBase.New("mid:" & msg)
            End Sub
        End Class
        Class OtherErr
            Inherits Exception
            Public Sub New(msg As String)
                MyBase.New(msg)
            End Sub
        End Class
        Sub Main()
            For k As Integer = 1 To 3
                Try
                    If k = 1 Then Throw New MidErr("one")
                    If k = 2 Then Throw New BaseErr("two")
                    Throw New OtherErr("three")
                Catch e As OtherErr
                    Console.WriteLine("other " & e.Message)
                Catch e As BaseErr
                    Console.WriteLine("base " & e.Message)
                End Try
            Next
        End Sub
        """, "base mid:one\nbase two\nother three");

    /// <summary>A leaf class over a base class over `Exception`: read through the base's catch variable, cast down with `CType`, and a bare `Message` in the leaf's own method.</summary>
    internal static readonly TempProbe V8 = P("V8_two_level_chain", """
        Class BaseErr
            Inherits Exception
            Public Sub New(msg As String)
                MyBase.New(msg)
            End Sub
        End Class
        Class LeafErr
            Inherits BaseErr
            Public Sub New(msg As String)
                MyBase.New("leaf:" & msg)
            End Sub
            Public Function Describe() As String
                Return "D " & Message
            End Function
        End Class
        Sub Main()
            Try
                Throw New LeafErr("z")
            Catch e As BaseErr
                Console.WriteLine(e.Message)
                Console.WriteLine(CType(e, LeafErr).Describe())
            End Try
        End Sub
        """, "leaf:z\nD leaf:z");

    /// <summary>A LOCAL named `message` inside the class's own method does not hide the inherited `Message` read from outside.</summary>
    internal static readonly TempProbe V9 = P("V9_local_named_message", """
        Class MyErr
            Inherits Exception
            Private _detail As String
            Public Sub New(msg As String, detail As String)
                MyBase.New(msg)
                _detail = detail
            End Sub
            Public Function Info() As String
                Dim message As String = "local"
                Return message & "/" & _detail
            End Function
        End Class
        Sub Main()
            Dim e As New MyErr("m", "d")
            Console.WriteLine(e.Info())
            Console.WriteLine(e.Message)
        End Sub
        """, "local/d\nm");

    /// <summary>A class that `Inherits ArgumentException` (under `Using System`), thrown, caught as `ArgumentException` after a non-matching `InvalidOperationException` clause, then caught by its own name.</summary>
    internal static readonly TempProbe N3b = P("N3b_inherits_argumentexception", """
        Using System
        Class BadArg
            Inherits ArgumentException
            Public Sub New(msg As String)
                MyBase.New(msg)
            End Sub
        End Class
        Sub Main()
            Try
                Throw New BadArg("arg")
            Catch e As InvalidOperationException
                Console.WriteLine("wrong")
            Catch e As ArgumentException
                Console.WriteLine("ArgumentException " & e.Message)
            End Try
            Try
                Throw New BadArg("arg2")
            Catch e As BadArg
                Console.WriteLine("BadArg " & e.Message)
            End Try
        End Sub
        """, "ArgumentException arg\nBadArg arg2");

    /// <summary>A user exception no inner clause matches: the inner Finally runs, and an outer `Catch ex As Exception` receives it with its message.</summary>
    internal static readonly TempProbe N4 = P("N4_unmatched_reaches_outer_exception", """
        Class AErr
            Inherits Exception
            Public Sub New(msg As String)
                MyBase.New(msg)
            End Sub
        End Class
        Class BErr
            Inherits Exception
            Public Sub New(msg As String)
                MyBase.New(msg)
            End Sub
        End Class
        Sub Work()
            Try
                Throw New AErr("escapes")
            Catch e As BErr
                Console.WriteLine("wrong " & e.Message)
            Finally
                Console.WriteLine("inner fin")
            End Try
        End Sub
        Sub Main()
            Try
                Work()
            Catch ex As Exception
                Console.WriteLine("outer " & ex.Message)
            End Try
        End Sub
        """, "inner fin\nouter escapes");

    /// <summary>A class that passes NO message, two levels down: `Message` is .NET's default, `Exception of type 'SubErr' was thrown.` (the dynamic type, not the catching class). C++ ONLY: the C# backend lands in `namespace GeneratedCode`, so it prints `GeneratedCode.SubErr`, not vbc's `SubErr`; the reference leg would say nothing about C++.</summary>
    internal static readonly TempProbe X2 = P("X2_default_message", """
        Class QuietErr
            Inherits Exception
        End Class
        Class SubErr
            Inherits QuietErr
        End Class
        Sub Main()
            Try
                Throw New SubErr()
            Catch e As QuietErr
                Console.WriteLine(e.Message)
            End Try
        End Sub
        """, "Exception of type 'SubErr' was thrown.", Bk.Cpp);

    /// <summary>CONTROL: a BUILT-IN `InvalidOperationException` thrown and caught by name. No user class: the path every program took before #151's C++ half, and still does.</summary>
    internal static readonly TempProbe N6 = P("N6_builtin_control", """
        Sub Main()
            Try
                Throw New InvalidOperationException("x")
            Catch e As InvalidOperationException
                Console.WriteLine("IOE " & e.Message)
            End Try
        End Sub
        """, "IOE x");
}

/// <summary>
/// #151 (C++ half), the REFUSALS: a user exception class the runtime base cannot carry is refused BY NAME at code generation, as a <see cref="BasicLang.Compiler.CodeGen.CPlusPlus.CppCapabilityException"/>,
/// never left to clang (which printed `no member named 'StackTrace'`, `use of undeclared identifier`, an overload failure). Needs no native compiler, so it runs in the fast subset. Every program is refused in
/// EVERY in-process entry point (no optimizer, standard, aggressive, `CompileProjectFiles`, and the split generator the IDE's C++ project build uses) and by the real CLI, with and without `--optimize`, which
/// exits non-zero and writes NO `.cpp`.
/// </summary>
[TestFixture]
[NonParallelizable] // the spawned CLI
public class CppUserExceptionRefusalTests
{
    private static readonly CppEntry[] InProcessEntries =
        { CppEntry.Plain, CppEntry.Standard, CppEntry.Aggressive, CppEntry.Project, CppEntry.Split };

    /// <summary>The program is refused with a message that contains every fragment, in every in-process entry point and, when <paramref name="cli"/>, through the real CLI (standard and `--optimize`).</summary>
    private static void AssertRefused(string label, string source, bool cli, params string[] fragments)
    {
        Assert.Multiple(() =>
        {
            foreach (var entry in InProcessEntries)
            {
                var message = CppClosures.Refusal(source, entry)?.Message ?? "(not refused)";
                foreach (var fragment in fragments)
                    Assert.That(message, Does.Contain(fragment), $"{label}, {entry}");
            }

            foreach (var optimize in cli ? new[] { false, true } : Array.Empty<bool>())
            {
                var cli = CppClosures.Cli(source, optimize);
                Assert.That(cli.Exit, Is.Not.EqualTo(0), $"{label}, CLI{(optimize ? " --optimize" : "")} must refuse:\n{cli.Console}");
                Assert.That(cli.Cpp, Is.Null, $"{label}, CLI{(optimize ? " --optimize" : "")} wrote a .cpp for a refused program");
                foreach (var fragment in fragments)
                    Assert.That(cli.Console, Does.Contain(fragment), $"{label}, CLI{(optimize ? " --optimize" : "")}");
            }
        });
    }

    private const string UserExceptionHead = """
        Class MyErr
            Inherits Exception
            Public Sub New(msg As String)
                MyBase.New(msg)
            End Sub
        End Class
        Sub Main()
            Try
                Throw New MyErr("x")
            Catch e As MyErr
        """;

    /// <summary>The one catch body that reads <paramref name="read"/> off the caught user exception.</summary>
    private static string ReadsOffACaughtUserException(string read)
        => UserExceptionHead + "\n        Console.WriteLine(" + read + ")\n    End Try\nEnd Sub\n";

    /// <summary>
    /// (11) X5, ⭐ the M5 killer: a `System.Exception` member the runtime base does NOT provide (it provides `Message` and `ToString()`), read off a user exception object, is refused by name:
    /// `'StackTrace' of Exception (read in 'Main') is not supported on the C++ backend for a user exception class`. clang's own answer was `no member named 'StackTrace'`. Every member the checker lists (StackTrace,
    /// InnerException, Source, HResult, HelpLink, TargetSite, Data, and the call `GetBaseException()`) in every in-process entry point, and StackTrace through the real CLI. M5 (no member refusal) lets every one of them
    /// through to clang.
    /// </summary>
    [Test]
    public void AnExceptionMemberTheRuntimeBaseLacks_IsRefusedByName_NotAClangError()
    {
        var members = new[]
        {
            ("StackTrace", "e.StackTrace Is Nothing"), ("InnerException", "e.InnerException Is Nothing"), ("Source", "e.Source Is Nothing"),
            ("HResult", "e.HResult"), ("HelpLink", "e.HelpLink Is Nothing"), ("TargetSite", "e.TargetSite Is Nothing"),
            ("Data", "e.Data Is Nothing"), ("GetBaseException", "e.GetBaseException() Is Nothing"),
        };
        Assert.Multiple(() =>
        {
            foreach (var (member, read) in members)
                AssertRefused($"reading {member}", ReadsOffACaughtUserException(read), cli: member == "StackTrace",
                    $"'{member}' of Exception", "not supported on the C++ backend for a user exception class");
        });
    }

    /// <summary>
    /// (12) A GENERIC exception class (`Class BoxErr(Of T) : Inherits Exception`) is refused by name: a Catch matches the thrown object by its class name alone, which cannot tell `BoxErr(Of Integer)` from
    /// `BoxErr(Of String)`, so a generic one would catch the wrong instantiation. The refusal names the class and says to declare one class per type.
    /// </summary>
    [Test]
    public void AGenericExceptionClass_IsRefusedByName()
        => AssertRefused("generic exception class", """
            Class BoxErr(Of T)
                Inherits Exception
                Public Sub New(msg As String)
                    MyBase.New(msg)
                End Sub
            End Class
            Sub Main()
                Try
                    Throw New BoxErr(Of Integer)("g")
                Catch e As BoxErr(Of Integer)
                    Console.WriteLine(e.Message)
                End Try
            End Sub
            """, cli: true, "'BoxErr' is a generic class built on 'Exception'", "cannot be generic");

    /// <summary>
    /// (13) `MyBase.New(msg, inner)`: the InnerException constructor (and `paramName`, and every other built-in constructor beyond a message) is refused by name, counting the arguments:
    /// `'MyErr' passes 2 arguments to MyBase.New of 'Exception'`. The inner exception is built in place (`New InvalidOperationException(...)`) rather than passed as a parameter typed `Exception`, which is the
    /// separate, still-open declaration gap (X3 / X4) and would add a clang error of its own.
    /// </summary>
    [Test]
    public void MyBaseNewWithMoreThanAMessage_IsRefusedByName()
        => AssertRefused("MyBase.New(msg, inner)", """
            Class MyErr
                Inherits Exception
                Public Sub New(msg As String)
                    MyBase.New(msg, New InvalidOperationException("root"))
                End Sub
            End Class
            Sub Main()
                Try
                    Throw New MyErr("wrapped")
                Catch e As MyErr
                    Console.WriteLine(e.Message)
                End Try
            End Sub
            """, cli: true, "'MyErr' passes 2 arguments to MyBase.New of 'Exception'", "takes only a message");

    /// <summary>
    /// (14) `Overrides Message` (`Public Overrides ReadOnly Property Message As String`) is refused by name: the runtime's `Message` is a data member read by name everywhere, so an override could not take effect.
    /// The refusal says to pass the message to MyBase.New instead.
    /// </summary>
    [Test]
    public void OverridingMessage_IsRefusedByName()
        => AssertRefused("Overrides Message", """
            Class MyErr
                Inherits Exception
                Public Overrides ReadOnly Property Message As String
                    Get
                        Return "custom"
                    End Get
                End Property
            End Class
            Sub Main()
                Try
                    Throw New MyErr()
                Catch ex As Exception
                    Console.WriteLine(ex.Message)
                End Try
            End Sub
            """, cli: true, "'MyErr' overrides Exception.Message", "pass the message to MyBase.New instead");
}
