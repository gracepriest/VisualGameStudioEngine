using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Tasks #142, #265 and #213 — a `MyBase.M(...)` call carries its TARGET's parameter facts. RUN, against vbc, through every entry point.
//
//  ⛔ THE BUG. IRBuilder lowered `MyBase.M(args)` in its own arm, from the bare argument values, and never consulted the method it calls. Four facts an ordinary method call carries were
//  lost: BYREF (no `ref` on C#, CS1620; MSIL named `SetIt(int32)` for a method declared `int32&` — MissingMethodException, #142 — also when Derived overrides the method itself, which is
//  why #142's title "an inherited method" is too narrow), an OPTIONAL left out (too few arguments on C++, `undefined` on JavaScript, MissingMethodException on MSIL), the DECLARED
//  PARAMETER TYPE (MSIL spelled the signature from the ARGUMENTS, so `MyBase.Show(5)` into `o As Object` named `Show(int32)`, #213) and a PARAMARRAY (a clang error on C++, a TypeError on
//  JavaScript, MissingMethodException on MSIL). The fix lowers the base call's arguments through the instance call's own path (`IRBuilder.LowerMethodCallArguments`) against the method
//  the analyzer bound, and `IRBaseMethodCall.ByRefArguments` is the mirror of `IRInstanceMethodCall`'s.
//
//  ⭐ THE ORACLE IS vbc, not a backend. A row's `Vb` is what the SDK's vbc prints for the program (wrapped in a VB Module); `Agrees` is the set of backends whose three entry points print it
//  on this build. Rows are the implementer's probes (S/t142/probes/m, m2) — b01 + b03, b05 + b06 and b07 + b08 + b09 are MERGED into one program each, and their expected text was taken
//  from vbc for the merged program. ONE TEST CASE PER ROW: it runs every backend of `Agrees`, each through the spawned CLI, the CLI with `--optimize` and
//  `BasicCompiler.CompileProjectFiles` (aggressive; what a Release .blproj build and the IDE call), and reports every failing cell.
//
//  ⛔⛔ EVERY C# RUN HERE IS HANG-SAFE (`hangSafe: true` -> CSharpProcessRunner), and the shape fixture's guard reads this file for it.
//
//  ⛔ KNOWN GAPS — cells with no expectation, each a defect that is NOT #142/#265/#213's, and NOT tested here (asserting one would pin the defect):
//    JS    a ByRef argument to a base method: BL7002, a refusal by design (JavaScript has no reference parameters). KillVocabularyExtensions.B2_JavaScript_RefusesByRef_BL7002 pins it.
//    C++   an `Object` parameter: `'Object' has no C++ mapping` (the parameter itself, not the base call) — `object_parameter` and `grandparent` have no C++ cell.
//    JS    a `Long` Optional parameter: BL7003 (`stmt_value_widening` has no JavaScript cell).
//    C#    ✅ follow-up 1 is FIXED by #232 (no row here): the INLINED instance call (`Console.WriteLine(b.Bump(q))`) and an inlined IRCall wrote no `ref` (CS1620), where the base call's inlined
//          value form always did (the `byref_sub_and_value` row reads `Console.WriteLine(MyBase.Bump(q))`). All three now go through `CSharpBackend.WithRefModifier`; the instance call is the row
//          `InstanceCall_InsideAnExpression` of ByRefCallInExpressionCSharpExecutionTests.
//
//  ⭐ MUTANTS (S/t142/mut: each is the fix plus ONE change, built from a plain source copy and run against a copy of the test output with its BasicLang.dll swapped; all four are killed by this fixture, 87 tests run against each: this fixture's 12, the shape fixture's, and the moved pins).
//    M1 the base arm records no ByRef flag: `byref_sub_and_value`, `byref_field_with_optional`, `grandparent` and `byref_captured_csharp` (C#: CS1620), the Release build of `byref_sub_and_value`, and the
//       REFUSAL test — a base call that hides its ByRef argument from ClosureLowering is compiled by C++, which writes the update into a copy and prints `5 10`.
//    M2 the base arm packs no ParamArray tail: `paramarray_0_1_3`.
//    M3 MSIL spells a base call's signature from the arguments again: `byref_sub_and_value`, `byref_field_with_optional`, `grandparent` and `object_parameter` (MSIL cells; #213 and #142).
//    M4 C#'s INLINED base call writes no `ref`: `byref_sub_and_value` (`Console.WriteLine(MyBase.Bump(q))`) and its Release build, the only tests of the 87 run that notice.
//
//  ⚠ Named "…ExecutionTests" on purpose: its rows RUN under Node (`optional_left_out`, `paramarray_0_1_3`, `object_parameter`), so it is in JsExecutionTierRosterTests' roster.
// ================================================================================================

/// <summary>
/// #142 / #265 / #213 RUN: a <c>MyBase.M(...)</c> call passes ByRef arguments by reference, fills an Optional left out, packs a ParamArray and names its target's declared parameter types —
/// and prints vbc's answer on every backend that prints it, through every entry point.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the JavaScript legs and the C++ compiles share the machine with the spawned runners
public class MyBaseCallArgumentsExecutionTests
{
    // ================================================================================================
    // The rows
    // ================================================================================================

    /// <summary>a ByRef argument to a base method: a Sub and a Function, each OVERRIDDEN in Derived (so a call that resolved to the override would print -1); the Function as a Dim and as an inline value. (probes b01 + b03)</summary>
    internal static readonly TempProbe byref_sub_and_value = new("byref_sub_and_value", """
        ' MyBase.M(x) with a ByRef parameter: a Sub and a Function (statement, Dim and inline value forms), both overridden in Derived
        Class Base
            Public Overridable Sub SetIt(ByRef n As Integer)
                n = n + 100
            End Sub
            Public Overridable Function Bump(ByRef n As Integer) As Integer
                n = n + 100
                Return n * 2
            End Function
        End Class

        Class Derived
            Inherits Base
            Public Overrides Sub SetIt(ByRef n As Integer)
                n = -1
            End Sub
            Public Overrides Function Bump(ByRef n As Integer) As Integer
                n = -1
                Return -1
            End Function
            Public Sub Work(p As Integer)
                MyBase.SetIt(p)
                Console.WriteLine(p)
                Dim q As Integer = p
                Dim r As Integer = MyBase.Bump(q)
                Console.WriteLine(q & " " & r)
                Console.WriteLine(MyBase.Bump(q))
                Console.WriteLine(q)
            End Sub
        End Class

        Sub Main()
            Dim d As New Derived()
            d.Work(5)
        End Sub
        """, """
        105
        205 410
        610
        305
        """, Bk.CSharp | Bk.Cpp | Bk.Msil, HangSafe: true);

    /// <summary>a ByRef argument that is a FIELD, plus an Optional left out, called from the Overrides of the same method. (probe b12)</summary>
    internal static readonly TempProbe byref_field_with_optional = new("byref_field_with_optional", """
        ' MyBase.M(ByRef field) plus an omitted Optional, called from an Overrides of the same method
        Class Base
            Public Overridable Sub Fill(ByRef n As Integer, Optional add As Integer = 1)
                n = n + add
            End Sub
        End Class

        Class Derived
            Inherits Base
            Public Total As Integer = 10
            Public Overrides Sub Fill(ByRef n As Integer, Optional add As Integer = 1)
                MyBase.Fill(n, add * 2)
                MyBase.Fill(Total)
            End Sub
        End Class

        Sub Main()
            Dim d As New Derived()
            Dim v As Integer = 0
            d.Fill(v, 4)
            Console.WriteLine(v & " " & d.Total)
        End Sub
        """, """
        8 11
        """, Bk.CSharp | Bk.Cpp | Bk.Msil, HangSafe: true);

    /// <summary>`MyBase.Show(5)` into an `Object` parameter: MSIL names `Show(object)`, not `Show(int32)` (#213). (probe b04)</summary>
    internal static readonly TempProbe object_parameter = new("object_parameter", """
        ' MyBase.M(5) into an Object parameter (#213's shape)
        Class Base
            Public Sub Show(o As Object)
                Console.WriteLine(o)
            End Sub
        End Class

        Class Derived
            Inherits Base
            Public Sub Relay()
                MyBase.Show(5)
                Console.WriteLine("done")
            End Sub
        End Class

        Sub Main()
            Dim d As New Derived()
            d.Relay()
        End Sub
        """, """
        5
        done
        """, Bk.CSharp | Bk.JavaScript | Bk.Msil, HangSafe: true);

    /// <summary>an Optional left out: one of two (a Sub, overridden) and all of them (a Function, as a value). (probes b05 + b06)</summary>
    internal static readonly TempProbe optional_left_out = new("optional_left_out", """
        ' MyBase.M(...) leaving out ONE of two Optional parameters (a Sub, overridden) and ALL of them (a Function, a value call)
        Class Base
            Public Overridable Sub Show(a As Integer, Optional b As Integer = 7, Optional c As String = "x")
                Console.WriteLine("base " & a & "," & b & "," & c)
            End Sub
            Public Function Calc(Optional a As Integer = 3, Optional d As Double = 0.5) As Double
                Return a + d
            End Function
        End Class

        Class Derived
            Inherits Base
            Public Overrides Sub Show(a As Integer, Optional b As Integer = 7, Optional c As String = "x")
                Console.WriteLine("derived " & a & "," & b & "," & c)
            End Sub
            Public Sub Work()
                MyBase.Show(1, 2)
                MyBase.Show(3, 4, "y")
                Dim r As Double = MyBase.Calc()
                Console.WriteLine(r)
                Console.WriteLine(MyBase.Calc() * 2)
            End Sub
        End Class

        Sub Main()
            Dim d As New Derived()
            d.Work()
        End Sub
        """, """
        base 1,2,x
        base 3,4,y
        3.5
        7
        """, Bk.All, HangSafe: true);

    /// <summary>a ParamArray with 0, 1 and 3 arguments: packed into an array on every backend. (probes b07 + b08 + b09)</summary>
    internal static readonly TempProbe paramarray_0_1_3 = new("paramarray_0_1_3", """
        ' MyBase.Sum(...) into a ParamArray with 0, 1 and 3 arguments
        Class Base
            Public Function Sum(label As String, ParamArray v() As Integer) As Integer
                Dim t As Integer = 0
                For Each x As Integer In v
                    t = t + x
                Next
                Console.WriteLine(label & " n=" & v.Length)
                Return t
            End Function
        End Class

        Class Derived
            Inherits Base
            Public Sub Work()
                Console.WriteLine(MyBase.Sum("s"))
                Console.WriteLine(MyBase.Sum("s", 5))
                Console.WriteLine(MyBase.Sum("s", 1, 2, 3))
            End Sub
        End Class

        Sub Main()
            Dim d As New Derived()
            d.Work()
        End Sub
        """, """
        s n=0
        0
        s n=1
        5
        s n=3
        6
        """, Bk.All, HangSafe: true);

    /// <summary>a method declared on the GRANDPARENT only: ByRef + an Optional left out, and an Object parameter. (probe b10)</summary>
    internal static readonly TempProbe grandparent = new("grandparent", """
        ' MyBase.M(...) where M is declared on the GRANDPARENT only: ByRef, Optional and an Object parameter
        Class GrandBase
            Public Sub Adjust(ByRef n As Integer, Optional k As Integer = 10)
                n = n + k
            End Sub
            Public Function Tag(o As Object) As String
                Return "tag:" & o.ToString()
            End Function
        End Class

        Class Base
            Inherits GrandBase
        End Class

        Class Derived
            Inherits Base
            Public Sub Work()
                Dim q As Integer = 1
                MyBase.Adjust(q)
                Console.WriteLine(q)
                MyBase.Adjust(q, 5)
                Console.WriteLine(q)
                Console.WriteLine(MyBase.Tag(42))
            End Sub
        End Class

        Sub Main()
            Dim d As New Derived()
            d.Work()
        End Sub
        """, """
        11
        16
        tag:42
        """, Bk.CSharp | Bk.Msil, HangSafe: true);

    /// <summary>a Function through MyBase as a statement and as a value, an Optional `Long` left out, a widening argument. (probe b11)</summary>
    internal static readonly TempProbe stmt_value_widening = new("stmt_value_widening", """
        ' A Function called through MyBase as a STATEMENT (result discarded) and as a VALUE, with a widening argument
        Class Base
            Public Overridable Function Scale(x As Double, Optional f As Long = 3) As Double
                Console.WriteLine("scale " & x & " " & f)
                Return x * f
            End Function
        End Class

        Class Derived
            Inherits Base
            Public Overrides Function Scale(x As Double, Optional f As Long = 3) As Double
                Return 0
            End Function
            Public Sub Work()
                MyBase.Scale(2)
                Dim r As Double = MyBase.Scale(5, 2)
                Console.WriteLine(r)
                Console.WriteLine(MyBase.Scale(1) + 1)
            End Sub
        End Class

        Sub Main()
            Dim d As New Derived()
            d.Work()
        End Sub
        """, """
        scale 2 3
        scale 5 2
        10
        scale 1 3
        4
        """, Bk.CSharp | Bk.Cpp | Bk.Msil, HangSafe: true);

    /// <summary>a variable passed ByRef to MyBase.SetIt that a lambda ALSO captures. C# passes `ref q` to the closure's own variable: `105 210`. (probe b13; C++ and MSIL REFUSE it, see the refusal test)</summary>
    internal static readonly TempProbe byref_captured_csharp = new("byref_captured_csharp", """
        ' MyBase.SetIt(q) where q is ALSO captured by a lambda in the same method
        Class Base
            Public Sub SetIt(ByRef n As Integer)
                n = n + 100
            End Sub
        End Class

        Class Derived
            Inherits Base
            Public Sub Work()
                Dim q As Integer = 5
                Dim f As Func(Of Integer) = Function() q * 2
                MyBase.SetIt(q)
                Console.WriteLine(q & " " & f())
            End Sub
        End Class

        Sub Main()
            Dim d As New Derived()
            d.Work()
        End Sub
        """, """
        105 210
        """, Bk.CSharp, HangSafe: true);

    internal static readonly IReadOnlyList<TempProbe> Rows = new[]
    {
        byref_sub_and_value, byref_field_with_optional, object_parameter, optional_left_out, paramarray_0_1_3, grandparent, stmt_value_widening, byref_captured_csharp
    };
    private static IEnumerable<TestCaseData> RowCells() => Rows.Select(p => new TestCaseData(p).SetName(p.Id));

    private static IEnumerable<TestCaseData> BuildCommandCells()
        => new[] { byref_sub_and_value, paramarray_0_1_3 }.Select(p => new TestCaseData(p).SetName($"{p.Id}_BuildRelease"));

    /// <summary>
    /// The table IS the proof, so its shape is pinned: a row cannot vanish, and a backend cannot be dropped from one, without this test saying so. It is also the fixture's one plain [Test]
    /// besides its sources and cases: <c>JsExecutionTierRosterTests</c> counts attributes, and a fixture whose tests are all [TestCaseSource] counts as empty.
    /// </summary>
    [Test]
    public void TheTable_HasItsRows()
    {
        string Ids(Func<TempProbe, bool> where) => string.Join(",", Rows.Where(where).Select(p => p.Id));

        Assert.Multiple(() =>
        {
            Assert.That(Rows.Select(p => p.Id), Is.EqualTo(new[]
            {
                "byref_sub_and_value", "byref_field_with_optional", "object_parameter", "optional_left_out",
                "paramarray_0_1_3", "grandparent", "stmt_value_widening", "byref_captured_csharp",
            }));
            Assert.That(Rows.Where(p => !p.HangSafe).Select(p => p.Id), Is.Empty, "every C# run of a base call is hang-safe");
            Assert.That(Ids(p => !p.Agrees.HasFlag(Bk.CSharp)), Is.Empty, "C# has a cell on every row: it is the backend whose ByRef, Optional and ParamArray were wrong first");

            // the cells with no expectation, by backend — each is an unrelated defect or a refusal by design (the header)
            Assert.That(Ids(p => !p.Agrees.HasFlag(Bk.Cpp)), Is.EqualTo("object_parameter,grandparent,byref_captured_csharp"), "C++: an Object parameter has no mapping; a captured ByRef is refused");
            Assert.That(Ids(p => !p.Agrees.HasFlag(Bk.JavaScript)),
                Is.EqualTo("byref_sub_and_value,byref_field_with_optional,grandparent,stmt_value_widening,byref_captured_csharp"),
                "JavaScript: BL7002 (ByRef), BL7003 (Long)");
            Assert.That(Ids(p => !p.Agrees.HasFlag(Bk.Msil)), Is.EqualTo("byref_captured_csharp"), "MSIL runs every row but the one it refuses");

            // the cells that run, backend by backend: 8 rows -> 8 C#, 5 C++, 3 JavaScript.. each through three entry points
            Assert.That(Rows.Sum(p => TempExec.Backends(p.Agrees).Count()), Is.EqualTo(8 + 5 + 3 + 7), "C# 8, C++ 5, JavaScript 3, MSIL 7");
            Assert.That(BuildCommandCells().Count(), Is.EqualTo(2));
        });
    }

    // ============================================================================================
    // RUN — vbc's answer, every backend that prints it, three entry points
    // ============================================================================================

    /// <summary>
    /// Each row on every backend of its `Agrees`, through the spawned CLI (standard passes), the CLI with `--optimize` and CompileProjectFiles with the aggressive passes. A ByRef that is not
    /// passed prints the unchanged variable (or is CS1620, or a MissingMethodException); an Optional left out is an error or `undefined`; a ParamArray not packed is an error or a
    /// TypeError; an Object parameter named from its argument is a MissingMethodException on MSIL — so every one of the four defects changes a cell of this table.
    /// A backend whose tool is missing (a C++ compiler, Node, ilasm) is skipped; the test is ignored only when none of its backends could run.
    /// </summary>
    [TestCaseSource(nameof(RowCells))]
    public void ABaseCall_CarriesItsTargetsParameterFacts_AndPrintsVbcsAnswer(TempProbe probe)
    {
        var backends = TempExec.Backends(probe.Agrees).ToList();
        var failures = new List<string>();
        var skipped = new List<Bk>();
        foreach (var backend in backends)
        {
            try
            {
                TempExec.AssertMatchesInEveryEntryPoint(backend, probe.Source, probe.Vb, probe.Id, hangSafe: true);
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

    // ============================================================================================
    // THE REFUSAL — b13: a variable passed ByRef to a base method that a lambda ALSO captures
    // ============================================================================================

    /// <summary>
    /// ⛔ A variable that a lambda captures lives in a closure environment's FIELD, and neither C++ nor MSIL can pass a field of one by reference. ClosureLowering refuses it for an instance
    /// call (`Me.SetIt(q)`); before the fix a base call carried no ByRef flag, so it was NOT refused: C++ compiled it, wrote the update into a COPY and printed `5 10` where vbc prints
    /// `105 210`, and MSIL threw MissingMethodException. A refusal is the right answer (as for `Me.SetIt(q)`), through every entry point, and it must name the variable and the call.
    /// C# is the control: it passes `ref` to the closure's variable and prints vbc's answer (row `byref_captured_csharp`).
    /// </summary>
    [Test]
    public void ACapturedVariablePassedByRefToABaseMethod_IsRefused_OnCppAndMsil_InEveryEntryPoint()
    {
        var failures = new List<string>();
        foreach (var backend in new[] { Bk.Cpp, Bk.Msil })
            foreach (var entry in Enum.GetValues<EntryPoint>())
            {
                var message = RefusalMessage(backend, entry, byref_captured_csharp.Source);
                if (message == null)
                    failures.Add($"{backend} {entry}: compiled, where it must be refused (a captured variable cannot be passed ByRef)");
                else if (!message.Contains("'q' is captured by a lambda", StringComparison.Ordinal) || !message.Contains("'MyBase.SetIt'", StringComparison.Ordinal))
                    failures.Add($"{backend} {entry}: refused, but not for the captured ByRef argument 'q' to 'MyBase.SetIt':\n{message}");
            }

        Assert.That(failures, Is.Empty, string.Join("\n", failures));
    }

    /// <summary>The refusal a backend gives <paramref name="source"/> through <paramref name="entry"/>, or null when it compiles it.</summary>
    private static string? RefusalMessage(Bk backend, EntryPoint entry, string source)
    {
        if (entry == EntryPoint.ProjectRelease)
        {
            try
            {
                TempExec.Emit(backend, entry, source);
                return null;
            }
            catch (ForeignFeatureException ex)  // MSIL's refusal, thrown by the generator
            {
                return ex.Message;
            }
            catch (CppCapabilityException ex)   // C++'s, thrown by its generator
            {
                return ex.Message;
            }
            catch (AssertionException ex)
            {
                return ex.Message;
            }
        }

        var dir = Path.Combine(Path.GetTempPath(), "bl-t142-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Prog.bas"), source);
            var args = new List<string> { "Prog.bas", "--target=" + TempExec.TargetName(backend) };
            if (entry == EntryPoint.CliOptimize) args.Add("--optimize");
            var (exit, stdout, stderr) = CliTestHarness.RunProcess(CliTestHarness.CliPath(), args.ToArray(), dir, timeoutMs: 120_000);
            return exit == 0 ? null : stdout + stderr;
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* temp */ } }
    }

    // ============================================================================================
    // THE REAL BUILD — `BasicLang build App.blproj -c Release`, the route the IDE's build service takes
    // ============================================================================================

    /// <summary>
    /// The project build writes the exe and the test RUNS it with a limit, killing the tree: a hang is a failure. `byref_sub_and_value` (CS1620 before the fix, `ref` now) and
    /// `paramarray_0_1_3`. Shares its body with MyBaseMethodCallStatementExecutionTests' build test.
    /// </summary>
    [TestCaseSource(nameof(BuildCommandCells))]
    public void TheReleaseProjectBuild_PrintsVbcsAnswer(TempProbe probe)
        => MyBaseMethodCallStatementExecutionTests.AssertReleaseBuildPrintsVbcsAnswer(probe, "bl-t142-build-");
}
