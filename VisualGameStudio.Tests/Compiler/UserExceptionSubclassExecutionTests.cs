using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VisualGameStudio.Tests.Msil;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #151 — a user class that `Inherits Exception` reads the base's `Message` on JavaScript and MSIL. RUN, against vbc, through every entry point.
//
//  ⛔ THE BUG. A green build, a crash at run time, on two backends. JAVASCRIPT: a bare `Message` inside the subclass was emitted as the bare name (ReferenceError) — `MemberNames` walked only
//  this module's classes and stopped at the provided runtime base; a chain that ends in a provided exception class now also inherits `Message`, and the read becomes `this.Message`. MSIL:
//  `e.Message` / `Me.Message` on the subclass went out as `ldfld MyErr::Message` (MissingFieldException), and a bare `Message` pushed nothing (InvalidProgramException) —
//  `TryExceptionMember` now follows a user class's chain to its .NET exception root unless a class in the chain DECLARES the member itself, and `EmitLoadLocal` has a bare arm
//  (`ldarg.0` + `callvirt [mscorlib]System.Exception::get_Message()`). The base-constructor call was already right. C# was right throughout.
//
//  ⭐ THE ORACLE IS vbc. Each row's `Vb` is what the SDK's vbc prints for the program (wrapped in a VB Module; S/t151/probes, the `.exp` beside each `.bas`). ONE TEST CASE PER ROW: it runs
//  JavaScript (Node) and MSIL (ilasm), each through the spawned CLI, the CLI with `--optimize` and `BasicCompiler.CompileProjectFiles` (aggressive — what a Release .blproj build and the
//  IDE call), and reports every failing cell. A backend whose tool is missing is SKIPPED (`TempExec.RequireTool`: Node, ilasm), never failed; the case is ignored only when none could run.
//  Nothing here runs C#, so there is no hang to guard against; Node and ilasm runs carry their own timeouts.
//
//  ⛔ KNOWN GAPS — each a defect or a decision that is NOT #151's, listed with NO test (asserting one would pin the defect):
//    C++    `Inherits Exception` still fails to compile (`unknown type name 'Exception'`). The runtime has no exception base class (exceptions are std::runtime_error / BasicLang::NetException);
//           the options — refuse with a diagnostic, a runtime exception-object base, throw the shared_ptr — are an OWNER decision, pending. `BarePropertyLoweringExecutionTests.
//           P15_ExceptionMessageBareInSubclass_JsAndMsilPrintVbcsAnswer_Task151` keeps the one leg that says it does not build.
//    JS     `Me.Message.ToUpper()` (probe V5) is a TypeError. Pre-existing: a plain `Exception`'s `Message.ToUpper()` fails the same way.
//    FRONT  `Inherits ApplicationException` is "Unknown base class" (V2); `Catch ... When` is a parse error (V4). Both fail on every backend before the backend is reached.
//    MSIL   reading a .NET Exception member that is NOT in the exception member table through a user subclass (e.g. `e.HelpLink`) is now REFUSED at compile time; before the fix it died at run
//           time with MissingFieldException.
//
//  ⭐ MUTANTS (S/t151/mut: the fix plus ONE change, built from a plain source copy and run against a copy of the test output with its BasicLang.dll swapped):
//    M1 JS inherits no `Message` from a provided exception base: `P15_bare_message_in_method` and `V8_two_level_chain` (JavaScript cells: ReferenceError), and the moved P15 pin in BarePropertyLoweringTests.
//    M2 MSIL does not follow a user class's chain to its .NET root: every row but `P15_bare_message_in_method` (the bare read has its own arm) — E05, V1, V3, V6, V7, V8, V9, V10 — and the IL-shape test of V8 and V10.
//    M3 MSIL has no bare arm in `EmitLoadLocal`: `P15_bare_message_in_method` and `V8_two_level_chain` (MSIL cells: InvalidProgramException), the moved P15 pin, and the IL-shape test of both.
//    M4 MSIL ignores a member the chain declares (`DeclaresMember`): `V10_declared_field_wins` only (and its IL-shape test) — a user-declared public field `Code` is then looked up as an exception member and refused at compile time
//       (ForeignFeatureException "'FieldErr.Code' is outside the supported exception surface").
//    (M2, M3 and M4 are killed by MSIL cells, which SKIP where ilasm is missing; `UserExceptionSubclassMsilIlShapeTests` kills them from the IL text alone, in the fast subset.)
//
//  ⚠ Named "…ExecutionTests" on purpose: its rows RUN under Node, so it is in JsExecutionTierRosterTests' roster.
// ================================================================================================

/// <summary>
/// #151 RUN: a user class that inherits <c>Exception</c> reads <c>Message</c> — bare, through <c>Me</c>, through a <c>Catch</c> variable, down a two-level chain, beside a user member of its own —
/// and prints vbc's answer on JavaScript and MSIL, through every entry point.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the Node and ilasm runs share the machine with the spawned CLI
public class UserExceptionSubclassExecutionTests
{
    // ================================================================================================
    // The rows
    // ================================================================================================

    /// <summary>a BARE `Message` inside the subclass's own method (JS: ReferenceError; MSIL: InvalidProgramException). Kills M1 (JS) and M3 (MSIL's bare arm).</summary>
    internal static readonly TempProbe P15_bare_message_in_method = new("P15_bare_message_in_method", """
        Class MyErr
            Inherits Exception

            Sub New()
                MyBase.New("boom")
            End Sub

            Function Describe() As String
                Return "E:" & Message
            End Function
        End Class

        Sub Main()
            Dim e As New MyErr()
            Console.WriteLine(e.Describe())
        End Sub
        """, """
        E:boom
        """, Bk.JavaScript | Bk.Msil);

    /// <summary>`e.Message` read through a `Catch e As MyErr` and through a `Catch e As Exception`, one thrown subclass each (MSIL: `ldfld MyErr::Message`, MissingFieldException). Kills M2.</summary>
    internal static readonly TempProbe E05_catch_user_and_base = new("E05_catch_user_and_base", """
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
        """, """
        MyErr x
        Exception y
        """, Bk.JavaScript | Bk.Msil);

    /// <summary>a user `ReadOnly Property Code` declared BESIDE the inherited `Message`: `e.Message & " " & e.Code`.</summary>
    internal static readonly TempProbe V1_property_beside_message = new("V1_property_beside_message", """
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
        """, """
        bad 42
        """, Bk.JavaScript | Bk.Msil);

    /// <summary>`e.Message` in an inner Catch, a bare `Throw`, then `e.Message` in the outer Catch.</summary>
    internal static readonly TempProbe V3_rethrow = new("V3_rethrow", """
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
        """, """
        inner deep
        outer deep
        """, Bk.JavaScript | Bk.Msil);

    /// <summary>two sibling user exceptions told apart by their Catch clauses, in a loop.</summary>
    internal static readonly TempProbe V6_two_siblings = new("V6_two_siblings", """
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
        """, """
        A a
        B b
        """, Bk.JavaScript | Bk.Msil);

    /// <summary>the qualified `Me.Message` inside the subclass's own method.</summary>
    internal static readonly TempProbe V7_me_message = new("V7_me_message", """
        Class MyErr
            Inherits Exception
            Public Sub New(msg As String)
                MyBase.New(msg)
            End Sub
            Public Function Wrap() As String
                Return "[" & Me.Message & "]"
            End Function
        End Class
        Sub Main()
            Dim e As New MyErr("w")
            Console.WriteLine(e.Wrap())
        End Sub
        """, """
        [w]
        """, Bk.JavaScript | Bk.Msil);

    /// <summary>a TWO-LEVEL chain (`LeafErr : BaseErr : Exception`): `e.Message` through a `Catch e As BaseErr` and a bare `Message` in the leaf's own method. Kills M1 (JS) and M3 (MSIL) and exercises the chain walk.</summary>
    internal static readonly TempProbe V8_two_level_chain = new("V8_two_level_chain", """
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
        """, """
        leaf:z
        D leaf:z
        """, Bk.JavaScript | Bk.Msil);

    /// <summary>a LOCAL named `message` inside the subclass shadows the inherited `Message` (the bare read must still find the local), then `e.Message` outside reads the inherited one.</summary>
    internal static readonly TempProbe V9_local_named_message = new("V9_local_named_message", """
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
        """, """
        local/d
        m
        """, Bk.JavaScript | Bk.Msil);

    /// <summary>a user-declared PUBLIC FIELD `Code` beside `e.Message`: the user's member is read as the user's field, and the inherited `Message` as the accessor. Kills M4 (MSIL's `DeclaresMember` ignored).</summary>
    internal static readonly TempProbe V10_declared_field_wins = new("V10_declared_field_wins", """
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
        """, """
        f 7
        """, Bk.JavaScript | Bk.Msil);

    internal static readonly IReadOnlyList<TempProbe> Rows = new[]
    {
        P15_bare_message_in_method, E05_catch_user_and_base, V1_property_beside_message, V3_rethrow, V6_two_siblings,
        V7_me_message, V8_two_level_chain, V9_local_named_message, V10_declared_field_wins,
    };

    private static IEnumerable<TestCaseData> RowCells() => Rows.Select(p => new TestCaseData(p).SetName(p.Id));

    /// <summary>
    /// The table IS the proof, so its shape is pinned: a row cannot vanish, and a backend cannot be dropped from one, without this test saying so. It is also the fixture's one plain [Test]
    /// besides its case source: <c>JsExecutionTierRosterTests</c> counts attributes, and a fixture whose tests are all [TestCaseSource] counts as empty.
    /// </summary>
    [Test]
    public void TheTable_HasItsRows()
        => Assert.Multiple(() =>
        {
            Assert.That(Rows.Select(p => p.Id), Is.EqualTo(new[]
            {
                "P15_bare_message_in_method", "E05_catch_user_and_base", "V1_property_beside_message", "V3_rethrow", "V6_two_siblings",
                "V7_me_message", "V8_two_level_chain", "V9_local_named_message", "V10_declared_field_wins",
            }));
            Assert.That(Rows.Where(p => p.Agrees != (Bk.JavaScript | Bk.Msil)).Select(p => p.Id), Is.Empty, "every row runs on JavaScript AND MSIL");
            Assert.That(Rows.Sum(p => TempExec.Backends(p.Agrees).Count()), Is.EqualTo(9 * 2), "9 rows x 2 backends, each through three entry points");
            Assert.That(Rows.Select(p => p.Id).Distinct().Count(), Is.EqualTo(Rows.Count));
        });

    // ============================================================================================
    // RUN — vbc's answer, JavaScript and MSIL, three entry points
    // ============================================================================================

    /// <summary>
    /// Each row on JavaScript and MSIL, through the spawned CLI (standard passes), the CLI with `--optimize` and CompileProjectFiles with the aggressive passes. A bare `Message` that is not
    /// bound is a ReferenceError (JavaScript) or an InvalidProgramException (MSIL); `e.Message` on the subclass is a MissingFieldException (MSIL); a user member that does not win is a
    /// compile-time refusal (MSIL) — so each of the four mutants changes a cell of this table. A backend whose tool is missing is skipped; the test is ignored only when none could run.
    /// </summary>
    [TestCaseSource(nameof(RowCells))]
    public void AUserExceptionSubclass_ReadsMessage_AndPrintsVbcsAnswer(TempProbe probe)
    {
        var backends = TempExec.Backends(probe.Agrees).ToList();
        var failures = new List<string>();
        var skipped = new List<Bk>();
        foreach (var backend in backends)
        {
            try
            {
                TempExec.AssertMatchesInEveryEntryPoint(backend, probe.Source, probe.Vb, probe.Id);
            }
            catch (IgnoreException)
            {
                skipped.Add(backend);
            }
            catch (AssertionException ex)
            {
                failures.Add(ex.Message);
            }
        }

        Assert.That(failures, Is.Empty, string.Join("\n", failures));
        if (skipped.Count == backends.Count) Assert.Ignore($"{probe.Id}: no execution tool for {string.Join(", ", skipped)} on this machine.");
    }
}

/// <summary>
/// #151, MSIL, from the IL TEXT — no ilasm, no process, so it runs in the fast subset on a machine whose MSIL execution cells all skip. A read of the inherited `Message` is the accessor call
/// on the .NET root (`callvirt instance string [mscorlib]System.Exception::get_Message()`), never a field load of the user class; a member the user's own class declares stays a field.
/// </summary>
[TestFixture]
public class UserExceptionSubclassMsilIlShapeTests
{
    private const string Getter = "callvirt instance string [mscorlib]System.Exception::get_Message()";

    /// <summary>The bare `Message` (P15: M3, no bare arm) and `e.Message` through a two-level chain (V8: M2, no chain walk): the accessor, once per read, and no `ldfld` of a user class's `Message`.</summary>
    [TestCase("P15_bare_message_in_method", 1)]
    [TestCase("V8_two_level_chain", 2)]
    public void AnInheritedMessageRead_IsTheGetterCall_NeverAFieldLoad(string rowId, int reads)
    {
        var row = UserExceptionSubclassExecutionTests.Rows.Single(r => r.Id == rowId);
        foreach (var aggressive in new[] { false, true })
        {
            var il = MsilHarness.CompileToIl(row.Source, "T", aggressive: aggressive);
            Assert.Multiple(() =>
            {
                Assert.That(il.Split(Getter).Length - 1, Is.EqualTo(reads), $"{rowId}, aggressive={aggressive}: one getter call per read of `Message`");
                Assert.That(il, Does.Not.Contain("::'Message'"), $"{rowId}, aggressive={aggressive}: `Message` is not a field of the user class (MissingFieldException)");
            });
        }
    }

    /// <summary>V10 (M4, `DeclaresMember` ignored): `Code` is the user's own public field and is read as one; `Message` beside it is still the getter.</summary>
    [Test]
    public void AMemberTheUserClassDeclares_StaysAField_BesideTheInheritedGetter()
    {
        var row = UserExceptionSubclassExecutionTests.Rows.Single(r => r.Id == "V10_declared_field_wins");
        var il = MsilHarness.CompileToIl(row.Source, "T");
        Assert.Multiple(() =>
        {
            Assert.That(il, Does.Contain("ldfld int32 'FieldErr'::'Code'"), "the user's own field is loaded as a field");
            Assert.That(il, Does.Contain(Getter), "the inherited Message is the getter");
            Assert.That(il, Does.Not.Contain("::'Message'"));
        });
    }
}
